using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;

static class AppleKeySwap {
    const int LAlt=0xA4, RAlt=0xA5, LWin=0x5B, RWin=0x5C;
    static bool enabled, paused;
    static IntPtr hook;
    static IntPtr lastForeground;
    static HookProc callback=OnKey;
    static Dictionary<int,int> held=new Dictionary<int,int>();
    static NotifyIcon tray;
    static Icon KeyboardIcon() {
        using(var bitmap=new Bitmap(32,32)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);
                using(var body=new SolidBrush(Color.FromArgb(30,80,135)))
                using(var key=new SolidBrush(Color.White)) {
                    g.FillRectangle(body,1,6,30,20);
                    for(int row=0;row<2;row++)
                        for(int col=0;col<6;col++)
                            g.FillRectangle(key,4+col*4,9+row*5,3,3);
                    g.FillRectangle(key,4,19,3,3);
                    g.FillRectangle(key,8,19,15,3);
                    g.FillRectangle(key,24,19,3,3);
                }
            }
            IntPtr handle=bitmap.GetHicon();
            try { using(var borrowed=Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
        }
    }
    static int Swap(int k) { return k==LAlt?LWin:k==LWin?LAlt:k==RAlt?RWin:k==RWin?RAlt:k; }
    [STAThread] static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--icon-preview") {
            using(var icon=KeyboardIcon()) using(var bitmap=icon.ToBitmap()) bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);
            return 0;
        }
        if(args.Length>0 && args[0]=="--detect") return AppleConnected()?0:2;
        if(args.Length>0 && args[0]=="--self-test") {
            return Swap(LAlt)==LWin && Swap(LWin)==LAlt && Swap(RAlt)==RWin && Swap(RWin)==RAlt && Swap(65)==65 && Marshal.SizeOf(typeof(INPUT))==40 && CheckKeyEvents() ? 0:1;
        }
        bool fresh;
        using(var mutex=new Mutex(true,"Local\\AppleKeySwap_05AC_024F",out fresh)) {
            if(!fresh) return 0;
            Application.EnableVisualStyles();
            hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
            if(hook==IntPtr.Zero) { MessageBox.Show("Could not start keyboard mapping.","Apple Key Swap"); return 1; }
            var menu=new ContextMenuStrip();
            var pause=new ToolStripMenuItem("Pause swapping");
            pause.CheckOnClick=true;
            pause.CheckedChanged+=(s,e)=>{paused=pause.Checked; Refresh();};
            menu.Items.Add(pause);
            menu.Items.Add("Exit and restore normal keys",null,(s,e)=>Application.Exit());
            var keyboardIcon=KeyboardIcon();
            tray=new NotifyIcon { Icon=keyboardIcon, Visible=true, ContextMenuStrip=menu };
            var timer=new System.Windows.Forms.Timer {Interval=1500};
            var focusTimer=new System.Windows.Forms.Timer {Interval=250};
            timer.Tick+=(s,e)=>Refresh();
            focusTimer.Tick+=(s,e)=>RefreshHookOrder();
            Refresh(); timer.Start();
            focusTimer.Start();
            try { Application.Run(); }
            finally {
                timer.Stop(); focusTimer.Stop(); enabled=false;
                UnhookWindowsHookEx(hook);
                foreach(var k in held.Values) SendKey(k,true);
                held.Clear(); tray.Dispose(); keyboardIcon.Dispose(); timer.Dispose(); focusTimer.Dispose(); menu.Dispose();
            }
        }
        return 0;
    }
    static void RefreshHookOrder() {
        IntPtr foreground=GetForegroundWindow();
        if(foreground==lastForeground || held.Count!=0 || Down(LAlt) || Down(RAlt) || Down(LWin) || Down(RWin)) return;
        // RDP can add its own hook when activated. A newly installed hook is
        // placed at the front of the chain, allowing our remap to run first.
        IntPtr replacement=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
        if(replacement==IntPtr.Zero) return;
        IntPtr previous=hook;
        hook=replacement;
        UnhookWindowsHookEx(previous);
        lastForeground=foreground;
    }
    static void Refresh() {
        bool wanted=!paused && AppleConnected();
        // Do not change a held modifier's meaning halfway through a shortcut.
        if(held.Count==0 && !Down(LAlt) && !Down(RAlt) && !Down(LWin) && !Down(RWin)) enabled=wanted;
        tray.Text=paused?"Apple Key Swap: paused":enabled?"Apple Key Swap: swapped":"Apple Key Swap: normal";
    }
    static bool Down(int k) { return (GetAsyncKeyState(k)&0x8000)!=0; }
    static bool AppleConnected() {
        uint count=0, size=(uint)Marshal.SizeOf(typeof(RAWDEVICE));
        if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue) return false;
        if(count==0) return false;
        var devices=new RAWDEVICE[count];
        uint got=GetRawInputDeviceList(devices,ref count,size);
        if(got==uint.MaxValue) return false;
        for(int i=0;i<got;i++) {
            if(devices[i].type!=1) continue;
            uint chars=0;
            if(GetRawInputDeviceInfo(devices[i].handle,0x20000007,IntPtr.Zero,ref chars)==uint.MaxValue || chars==0) continue;
            IntPtr buffer=Marshal.AllocHGlobal(checked((int)chars*2));
            try {
                if(GetRawInputDeviceInfo(devices[i].handle,0x20000007,buffer,ref chars)==uint.MaxValue) continue;
                string name=Marshal.PtrToStringUni(buffer);
                if(name!=null && name.IndexOf("VID_05AC&PID_024F",StringComparison.OrdinalIgnoreCase)>=0) return true;
            } finally {Marshal.FreeHGlobal(buffer);}
        }
        return false;
    }
    static IntPtr OnKey(int code,IntPtr message,IntPtr data) {
        if(code>=0) {
            KEYEVENT key=(KEYEVENT)Marshal.PtrToStructure(data,typeof(KEYEVENT));
            int vk=(int)key.vk;
            if((key.flags&0x10)==0 && Swap(vk)!=vk) {
                bool up=(key.flags&0x80)!=0;
                int target;
                if(held.TryGetValue(vk,out target)) {
                    if(SendKey(target,up)) {
                        if(up) held.Remove(vk);
                        return (IntPtr)1;
                    }
                } else if(enabled && !up) {
                    target=Swap(vk);
                    if(SendKey(target,false)) {held[vk]=target; return (IntPtr)1;}
                }
            }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    static INPUT KeyInput(int vk,bool up) {
        // Supply the physical scan code as well as extended/up flags so remote
        // clients receive a complete keyboard event rather than only a VK code.
        ushort scan=(ushort)((vk==LAlt || vk==RAlt)?0x38:vk==LWin?0x5B:0x5C);
        uint flags=8u|(up?2u:0u)|((vk==RAlt || vk==LWin || vk==RWin)?1u:0u);
        return new INPUT {type=1, value=new INPUTUNION {keyboard=new KEYINPUT {scan=scan,flags=flags}}};
    }
    static bool CheckKeyEvents() {
        int[] keys={LAlt,RAlt,LWin,RWin};
        ushort[] scans={0x38,0x38,0x5B,0x5C};
        uint[] flags={8,9,9,9};
        for(int i=0;i<keys.Length;i++) {
            INPUT down=KeyInput(keys[i],false), up=KeyInput(keys[i],true);
            if(down.type!=1 || down.value.keyboard.scan!=scans[i] || down.value.keyboard.flags!=flags[i] || up.value.keyboard.flags!=(flags[i]|2u)) return false;
        }
        return true;
    }
    static bool SendKey(int vk,bool up) {
        INPUT input=KeyInput(vk,up);
        return SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))==1;
    }
    delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct RAWDEVICE {public IntPtr handle; public uint type;}
    [StructLayout(LayoutKind.Sequential)] struct KEYEVENT {public uint vk,scan,flags,time; public UIntPtr extra;}
    [StructLayout(LayoutKind.Sequential)] struct KEYINPUT {public ushort vk,scan; public uint flags,time; public UIntPtr extra;}
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT {public int x,y; public uint data,flags,time; public UIntPtr extra;}
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION {[FieldOffset(0)] public KEYINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse;}
    [StructLayout(LayoutKind.Sequential)] struct INPUT {public uint type; public INPUTUNION value;}
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] input,int size);
    [DllImport("user32.dll")] static extern uint GetRawInputDeviceList([In,Out] RAWDEVICE[] devices,ref uint count,uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
}
