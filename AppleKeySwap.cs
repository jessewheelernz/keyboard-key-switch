using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.IO;
using System.Xml;
using Microsoft.Win32;

static class AppleKeySwap {
    const int LAlt=0xA4, RAlt=0xA5, LWin=0x5B, RWin=0x5C;
    static bool enabled, paused, editing; static Settings settings; static SettingsWindow window; static HashSet<int> physicalDown=new HashSet<int>(); static HashSet<int> released=new HashSet<int>();
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
    internal const string DefaultKeyboard="VID_05AC&PID_024F";
    internal static string SettingsPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"keyboard-settings.xml"); } }
    internal sealed class Binding { public int From, To; public Binding(int from,int to) { From=from; To=to; } }
    internal sealed class Settings {
        public string Keyboard=DefaultKeyboard;
        public List<Binding> Bindings=Preset();
        public static List<Binding> Preset() { return new List<Binding>{new Binding(LAlt,LWin),new Binding(LWin,LAlt),new Binding(RAlt,RWin),new Binding(RWin,RAlt)}; }
        public int Map(int key) { foreach(var b in Bindings) if(b.From==key) return b.To; return key; }
        public static Settings Load(string path) {
            if(!File.Exists(path)) return new Settings();
            var xml=new XmlDocument(); xml.XmlResolver=null;
            using(var reader=XmlReader.Create(path,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null })) xml.Load(reader);
            var root=xml.DocumentElement;
            if(root==null || root.Name!="keyboardSettings") throw new InvalidDataException("Invalid settings file.");
            var result=new Settings {Keyboard=root.GetAttribute("keyboard"),Bindings=new List<Binding>()};
            if(String.IsNullOrWhiteSpace(result.Keyboard)) throw new InvalidDataException("No keyboard selected.");
            foreach(XmlNode node in root.ChildNodes) {
                var element=node as XmlElement; if(element==null || element.Name!="binding") continue;
                result.Bindings.Add(new Binding(Int32.Parse(element.GetAttribute("from")),Int32.Parse(element.GetAttribute("to"))));
            }
            string error=Validate(result.Bindings); if(error!=null) throw new InvalidDataException(error);
            return result;
        }
        public void Save(string path) {
            var xml=new XmlDocument(); var root=xml.CreateElement("keyboardSettings"); xml.AppendChild(root); root.SetAttribute("keyboard",Keyboard);
            foreach(var b in Bindings) { var item=xml.CreateElement("binding"); item.SetAttribute("from",b.From.ToString()); item.SetAttribute("to",b.To.ToString()); root.AppendChild(item); }
            string temp=path+".tmp";
            try { xml.Save(temp); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
    }
    internal sealed class KeyChoice {
        public int Code {get;set;} public string Name {get;set;}
        public KeyChoice(int code,string name) { Code=code; Name=name; }
        public override string ToString() { return Name; }
    }
    internal static List<KeyChoice> KeyChoices() {
        var list=new List<KeyChoice>();
        list.Add(new KeyChoice(LWin,"Left Windows / Command")); list.Add(new KeyChoice(RWin,"Right Windows / Command"));
        list.Add(new KeyChoice(LAlt,"Left Alt / Option")); list.Add(new KeyChoice(RAlt,"Right Alt / Option"));
        list.Add(new KeyChoice(0xA2,"Left Ctrl")); list.Add(new KeyChoice(0xA3,"Right Ctrl"));
        list.Add(new KeyChoice(0xA0,"Left Shift")); list.Add(new KeyChoice(0xA1,"Right Shift"));
        foreach(int code in new[]{8,9,13,20,27,32,33,34,35,36,37,38,39,40,45,46,93,106,107,109,110,111,144,145,186,187,188,189,190,191,192,219,220,221,222,226})
            list.Add(new KeyChoice(code,((Keys)code).ToString()));
        for(int code=48;code<=90;code++) if(code<=57 || code>=65) list.Add(new KeyChoice(code,((char)code).ToString()));
        for(int code=96;code<=105;code++) list.Add(new KeyChoice(code,"Numpad "+(code-96)));
        for(int code=112;code<=135;code++) list.Add(new KeyChoice(code,"F"+(code-111)));
        return list;
    }
    internal static string Validate(List<Binding> bindings) {
        var allowed=new HashSet<int>(); foreach(var k in KeyChoices()) allowed.Add(k.Code);
        var sources=new HashSet<int>(); var targets=new HashSet<int>();
        foreach(var b in bindings) {
            if(!allowed.Contains(b.From) || !allowed.Contains(b.To)) return "Choose supported keys for both columns.";
            if(b.From==b.To) return "A binding must change the key.";
            if(!sources.Add(b.From)) return "Each source key can appear only once.";
            if(!targets.Add(b.To)) return "Each destination key can appear only once.";
        }
        return null;
    }
    [STAThread] static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--icon-preview") {
            using(var icon=KeyboardIcon()) using(var bitmap=icon.ToBitmap()) bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png); return 0;
        }
        if(args.Length>0 && args[0]=="--self-test") return SelfTest()?0:1;
        if(args.Length>0 && args[0]=="--ui-self-test") {settings=new Settings(); Application.EnableVisualStyles(); return SettingsWindow.Test()?0:1;} if(args.Length>0 && args[0]=="--list-keyboards") { foreach(var device in Keyboards()) Console.WriteLine(device.Id); return 0; }
        if(args.Length==2 && args[0]=="--ui-preview") { settings=new Settings(); Application.EnableVisualStyles(); using(var form=new SettingsWindow()) { form.Show(); Application.DoEvents(); using(var image=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height)); image.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);} } return 0; } bool loadFailed=false;
        try { settings=Settings.Load(SettingsPath); }
        catch(Exception ex) { settings=new Settings(); loadFailed=true; MessageBox.Show("Could not read saved bindings. Remapping is paused. Open Settings to review and save them.\n\n"+ex.Message,"Keyboard Key Switch"); }
        if(args.Length>0 && args[0]=="--detect") return SelectedConnected()?0:2;
        bool fresh;
        using(var mutex=new Mutex(true,"Local\\AppleKeySwap_05AC_024F",out fresh)) {
            if(!fresh) { MessageBox.Show("Keyboard Key Switch is already running. Exit the existing AppleKeySwap from its tray icon before starting this version.","Keyboard Key Switch"); return 0; }
            Application.EnableVisualStyles(); paused=loadFailed;
            hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
            if(hook==IntPtr.Zero) { MessageBox.Show("Could not start keyboard mapping.","Keyboard Key Switch"); return 1; }
            var menu=new ContextMenuStrip();
            menu.Items.Add("Settings...",null,(s,e)=>ShowSettings());
            var pause=new ToolStripMenuItem("Pause remapping") {CheckOnClick=true,Checked=paused};
            pause.CheckedChanged+=(s,e)=>{paused=pause.Checked; Refresh();}; menu.Items.Add(pause);
            menu.Items.Add("Exit and restore normal keys",null,(s,e)=>Application.Exit());
            var keyboardIcon=KeyboardIcon();
            tray=new NotifyIcon { Icon=keyboardIcon, Visible=true, ContextMenuStrip=menu };
            tray.DoubleClick+=(s,e)=>ShowSettings();
            var timer=new System.Windows.Forms.Timer {Interval=1500};
            var focusTimer=new System.Windows.Forms.Timer {Interval=250};
            timer.Tick+=(s,e)=>Refresh(); focusTimer.Tick+=(s,e)=>RefreshHookOrder();
            Refresh(); timer.Start(); focusTimer.Start();
            if(!File.Exists(SettingsPath) || (args.Length>0 && args[0]=="--settings")) ShowSettings();
            try { Application.Run(); }
            finally {
                timer.Stop(); focusTimer.Stop(); enabled=false; ReleaseMappedKeys(); UnhookWindowsHookEx(hook);
                if(window!=null) window.Dispose(); tray.Dispose(); keyboardIcon.Dispose(); timer.Dispose(); focusTimer.Dispose(); menu.Dispose();
            }
        }
        return 0;
    }
    static void ShowSettings() {
        if(window!=null && !window.IsDisposed) { window.Show(); window.Activate(); return; }
        editing=true; enabled=false;
        window=new SettingsWindow();
        window.FormClosed+=(s,e)=>{ editing=false; window=null; Refresh(); };
        window.Show();
    }
    static void ReleaseMappedKeys() {
        foreach(var pair in held) { SendKey(pair.Value,true); released.Add(pair.Key); }
        held.Clear();
    }
    internal sealed class KeyboardChoice {
        public string Id, Name; public KeyboardChoice(string id,string name) { Id=id; Name=name; }
        public override string ToString() { return Name; }
    }
    internal static List<KeyboardChoice> Keyboards() {
        var result=new List<KeyboardChoice>(); var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint count=0, size=(uint)Marshal.SizeOf(typeof(RAWDEVICE));
        if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue || count==0) return result;
        var devices=new RAWDEVICE[count]; uint got=GetRawInputDeviceList(devices,ref count,size);
        if(got==uint.MaxValue) return result;
        for(int i=0;i<got;i++) {
            if(devices[i].type!=1) continue;
            uint chars=0;
            if(GetRawInputDeviceInfo(devices[i].handle,0x20000007,IntPtr.Zero,ref chars)==uint.MaxValue || chars==0) continue;
            IntPtr buffer=Marshal.AllocHGlobal(checked((int)chars*2));
            try {
                if(GetRawInputDeviceInfo(devices[i].handle,0x20000007,buffer,ref chars)==uint.MaxValue) continue;
                string id=Marshal.PtrToStringUni(buffer);
                if(!String.IsNullOrEmpty(id) && ids.Add(id)) result.Add(new KeyboardChoice(id,DeviceName(id)));
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        result.Sort((a,b)=>String.Compare(a.Name,b.Name,StringComparison.OrdinalIgnoreCase));
        return result;
    }
    static string DeviceName(string id) {
        string[] parts=id.Split('#'); string name="Keyboard";
        try {
            if(parts.Length>=3) using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\"+parts[0].Replace(@"\\?\","")+"\\"+parts[1]+"\\"+parts[2])) {
                if(key!=null) { name=(key.GetValue("FriendlyName") ?? key.GetValue("DeviceDesc") ?? name).ToString(); int separator=name.LastIndexOf(';'); if(separator>=0) name=name.Substring(separator+1); }
            }
        } catch(System.Security.SecurityException) {} catch(UnauthorizedAccessException) {}
        if(id.IndexOf(DefaultKeyboard,StringComparison.OrdinalIgnoreCase)>=0) name="Apple keyboard";
        return name+" — "+(parts.Length>=3?parts[1]+" / "+parts[2]:id);
    }
    static bool SelectedConnected() { foreach(var device in Keyboards()) if(Matches(settings.Keyboard,device.Id)) return true; return false; }
    internal static bool Matches(string selected,string id) { return selected==DefaultKeyboard?id.IndexOf(DefaultKeyboard,StringComparison.OrdinalIgnoreCase)>=0:String.Equals(selected,id,StringComparison.OrdinalIgnoreCase); }
    static bool SelfTest() {
        try {
            var preset=new Settings();
            if(preset.Map(LAlt)!=LWin || preset.Map(LWin)!=LAlt || preset.Map(RAlt)!=RWin || preset.Map(RWin)!=RAlt || preset.Map(65)!=65 || Marshal.SizeOf(typeof(INPUT))!=40 || !CheckKeyEvents()) return false;
            if(Validate(new List<Binding>{new Binding(65,66),new Binding(65,67)})==null || Validate(new List<Binding>{new Binding(65,66),new Binding(67,66)})==null || Validate(new List<Binding>{new Binding(65,65)})==null) return false;
            if(!Matches(DefaultKeyboard,@"\\?\HID#VID_05AC&PID_024F#test") || Matches("one","two")) return false;
            string path=Path.Combine(Path.GetTempPath(),"keyboard-switch-test-"+Guid.NewGuid()+".xml");
            try {
                var custom=new Settings {Keyboard=@"\\?\HID#TEST&DEVICE",Bindings=new List<Binding>{new Binding(65,66),new Binding(0xA3,13)}};
                custom.Save(path); var loaded=Settings.Load(path); if(loaded.Keyboard!=custom.Keyboard || loaded.Map(65)!=66 || loaded.Map(0xA3)!=13 || loaded.Map(67)!=67) return false;
                custom.Bindings.Clear(); custom.Save(path); if(Settings.Load(path).Bindings.Count!=0) return false;
                File.WriteAllText(path,"<keyboardSettings keyboard='test'><binding from='65' to='65'/></keyboardSettings>");
                bool rejected=false; try {Settings.Load(path);} catch(InvalidDataException) {rejected=true;} if(!rejected) return false;
            } finally {if(File.Exists(path)) File.Delete(path);}
            return true;
        } catch(Exception ex) { Console.WriteLine(ex); return false; }
    }
    static void RefreshHookOrder() {
        IntPtr foreground=GetForegroundWindow();
        if(foreground==lastForeground || held.Count!=0 || physicalDown.Count!=0) return;
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
        bool connected=SelectedConnected(); bool wanted=!paused && !editing && connected && settings.Bindings.Count>0;
        if(!connected) { enabled=false; ReleaseMappedKeys(); physicalDown.Clear(); }
        else if(held.Count==0 && physicalDown.Count==0) enabled=wanted;
        tray.Text=paused?"Keyboard Key Switch: paused":editing?"Keyboard Key Switch: settings open":enabled?"Keyboard Key Switch: remapping":"Keyboard Key Switch: normal";
    }
    static IntPtr OnKey(int code,IntPtr message,IntPtr data) {
        if(code>=0) {
            KEYEVENT key=(KEYEVENT)Marshal.PtrToStructure(data,typeof(KEYEVENT));
            int vk=(int)key.vk;
            if((key.flags&0x10)==0) {
                bool up=(key.flags&0x80)!=0; if(up) physicalDown.Remove(vk); else physicalDown.Add(vk); if(released.Remove(vk) && up) return (IntPtr)1;
                int target;
                if(held.TryGetValue(vk,out target)) {
                    if(SendKey(target,up)) {
                        if(up) held.Remove(vk);
                        return (IntPtr)1;
                    }
                } else if(enabled && !paused && !editing && !up && settings.Map(vk)!=vk) {
                    target=settings.Map(vk);
                    if(SendKey(target,false)) {held[vk]=target; return (IntPtr)1;}
                }
            }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    static INPUT KeyInput(int vk,bool up) {
        uint mapped=MapVirtualKey((uint)vk,4); ushort scan=(ushort)(mapped&0xFF);
        // Scan codes preserve the behavior of modifiers in Remote Desktop.
        // Numpad navigation and divide need their explicit extended bit.
        bool extended=(mapped&0xFF00)==0xE000 || vk==RAlt || vk==0xA3 || vk==LWin || vk==RWin || vk==93 || vk==111 || vk==144 || (vk>=33 && vk<=40) || vk==45 || vk==46;
        uint flags=(up?2u:0u);
        if(scan!=0 && (mapped&0xFF00)!=0xE100) flags|=8u|(extended?1u:0u);
        return new INPUT {type=1,value=new INPUTUNION {keyboard=new KEYINPUT {vk=(ushort)((flags&8)!=0?0:vk),scan=scan,flags=flags}}};
    }
    static bool CheckKeyEvents() {
        int[] keys={LAlt,RAlt,LWin,RWin,65,13,0xA0,0xA3,37,111};
        ushort[] scans={0x38,0x38,0x5B,0x5C,0x1E,0x1C,0x2A,0x1D,0x4B,0x35};
        uint[] flags={8,9,9,9,8,8,8,9,9,9};
        for(int i=0;i<keys.Length;i++) {
            INPUT down=KeyInput(keys[i],false),up=KeyInput(keys[i],true);
            if(down.type!=1 || down.value.keyboard.scan!=scans[i] || down.value.keyboard.flags!=flags[i] || up.value.keyboard.flags!=(flags[i]|2u)) {Console.WriteLine("Key event mismatch: "+keys[i]+" scan="+down.value.keyboard.scan+" flags="+down.value.keyboard.flags); return false;}
        }
        return true;
    }
    internal sealed class SettingsWindow : Form {
        ComboBox keyboards=new ComboBox(); DataGridView grid=new DataGridView(); Label status=new Label();
        System.Windows.Forms.Timer discovery=new System.Windows.Forms.Timer {Interval=1500}; string inventory="";
        public SettingsWindow() {
            Text="Keyboard Key Switch — Settings"; ClientSize=new Size(780,580); MinimumSize=new Size(690,540);
            StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",10); AutoScaleMode=AutoScaleMode.Dpi;
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=7};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{32,40,70,30}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            layout.Controls.Add(new Label {Text="Activate bindings when this keyboard is connected",AutoSize=true},0,0);
            keyboards.Dock=DockStyle.Fill; keyboards.DropDownStyle=ComboBoxStyle.DropDownList; layout.Controls.Add(keyboards,0,1);
            layout.Controls.Add(new Label {Text="Connected keyboards are detected automatically. These bindings affect ALL keyboards while the selected keyboard is connected. Remapping is suspended while this window is open.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(125,70,15)},0,2);
            status.Dock=DockStyle.Fill; layout.Controls.Add(status,0,3);
            grid.Dock=DockStyle.Fill; grid.AutoGenerateColumns=false; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect=false; grid.BackgroundColor=Color.White; grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            foreach(string heading in new[]{"From key","To key"}) grid.Columns.Add(new DataGridViewComboBoxColumn {HeaderText=heading,DataSource=KeyChoices(),DisplayMember="Name",ValueMember="Code",FlatStyle=FlatStyle.Flat});
            layout.Controls.Add(grid,0,4);
            var tools=new FlowLayoutPanel {Dock=DockStyle.Fill};
            AddButton(tools,"Add binding",()=>{grid.Rows.Add(65,66);});
            AddButton(tools,"Remove",()=>{if(grid.CurrentRow!=null) grid.Rows.Remove(grid.CurrentRow);});
            AddButton(tools,"Command / Option preset",()=>Fill(Settings.Preset()));
            AddButton(tools,"Clear all",()=>grid.Rows.Clear()); layout.Controls.Add(tools,0,5);
            var bottom=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            AddButton(bottom,"Cancel",()=>Close()); AddButton(bottom,"Save and close",()=>Save()); layout.Controls.Add(bottom,0,6);
            Controls.Add(layout); Fill(settings.Bindings); RefreshDevices(settings.Keyboard);
            keyboards.SelectedIndexChanged+=(s,e)=>UpdateStatus(); discovery.Tick+=(s,e)=>RefreshDevices(null); discovery.Start();
        }
        static void AddButton(FlowLayoutPanel panel,string text,Action action) { var button=new Button {Text=text,AutoSize=true,Height=32}; button.Click+=(s,e)=>action(); panel.Controls.Add(button); }
        void Fill(List<Binding> bindings) { grid.Rows.Clear(); foreach(var b in bindings) grid.Rows.Add(b.From,b.To); }
        void RefreshDevices(string initial) {
            var devices=Keyboards(); string current=initial ?? (keyboards.SelectedItem==null?settings.Keyboard:((KeyboardChoice)keyboards.SelectedItem).Id);
            string fingerprint=String.Join("\n",devices.ConvertAll(d=>d.Id).ToArray());
            if(initial==null && fingerprint==inventory) {UpdateStatus();return;}
            inventory=fingerprint; keyboards.BeginUpdate(); keyboards.Items.Clear();
            if(current==DefaultKeyboard) keyboards.Items.Add(new KeyboardChoice(DefaultKeyboard,"Apple keyboard preset (USB 05AC:024F)"));
            bool found=false; foreach(var device in devices) {keyboards.Items.Add(device); if(String.Equals(device.Id,current,StringComparison.OrdinalIgnoreCase)) found=true;}
            if(!found && current!=DefaultKeyboard) keyboards.Items.Add(new KeyboardChoice(current,"Saved keyboard (disconnected) — "+current));
            for(int i=0;i<keyboards.Items.Count;i++) if(String.Equals(((KeyboardChoice)keyboards.Items[i]).Id,current,StringComparison.OrdinalIgnoreCase)) {keyboards.SelectedIndex=i;break;}
            keyboards.EndUpdate(); UpdateStatus();
        }
        void UpdateStatus() {
            var chosen=keyboards.SelectedItem as KeyboardChoice; bool connected=false;
            if(chosen!=null) foreach(var d in Keyboards()) if(Matches(chosen.Id,d.Id)) {connected=true;break;}
            status.Text=connected?"Selected keyboard is connected.":"Selected keyboard is disconnected. Bindings will activate when it reconnects.";
        }
        void Save() {
            grid.EndEdit(); var chosen=keyboards.SelectedItem as KeyboardChoice; if(chosen==null) return;
            var bindings=new List<Binding>();
            foreach(DataGridViewRow row in grid.Rows) {
                if(row.Cells[0].Value==null || row.Cells[1].Value==null) {MessageBox.Show(this,"Choose a key in both columns."); return;}
                bindings.Add(new Binding(Convert.ToInt32(row.Cells[0].Value),Convert.ToInt32(row.Cells[1].Value)));
            }
            string error=AppleKeySwap.Validate(bindings); if(error!=null) {MessageBox.Show(this,error,"Check bindings"); return;}
            if(held.Count!=0 || physicalDown.Count!=0) {MessageBox.Show(this,"Release all keyboard keys before saving."); return;}
            var candidate=new Settings {Keyboard=chosen.Id,Bindings=bindings};
            try {candidate.Save(SettingsPath);} catch(Exception ex) {MessageBox.Show(this,"Could not save settings.\n\n"+ex.Message);return;}
            settings=candidate; Close();
        }
        internal static bool Test() {
            using(var form=new SettingsWindow()) {
                form.Show(); Application.DoEvents();
                if(form.grid.Rows.Count!=4 || form.keyboards.SelectedItem==null) return false;
                form.Fill(new List<Binding>{new Binding(65,66)});
                if(Convert.ToInt32(form.grid.Rows[0].Cells[0].Value)!=65 || Convert.ToInt32(form.grid.Rows[0].Cells[1].Value)!=66) return false;
                form.RefreshDevices("disconnected-test-device");
                if(((KeyboardChoice)form.keyboards.SelectedItem).Id!="disconnected-test-device" || !form.status.Text.Contains("disconnected")) return false;
                form.RefreshDevices(null);
                if(((KeyboardChoice)form.keyboards.SelectedItem).Id!="disconnected-test-device") return false;
                form.Fill(new List<Binding>()); if(form.grid.Rows.Count!=0) return false;
                return true;
            }
        }
        protected override void Dispose(bool disposing) {if(disposing) discovery.Dispose();base.Dispose(disposing);}
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
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code,uint mapType);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] input,int size);
    [DllImport("user32.dll")] static extern uint GetRawInputDeviceList([In,Out] RAWDEVICE[] devices,ref uint count,uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
}
