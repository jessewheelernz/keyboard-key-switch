APPLE KEY SWAP

1. Turn off PowerToys Keyboard Manager before starting this app.
2. Double-click AppleKeySwap.exe. No installation or extra driver is needed.
3. While the Apple keyboard (USB ID 05AC:024F) is connected:
   Command becomes Alt; Option becomes Windows, on both sides.
4. Unplug the Apple keyboard and wait about two seconds: normal keys return.

The mapping applies to ALL keyboards while the Apple keyboard is connected.
Release Alt/Option and Windows/Command when plugging or unplugging it.
The app waits for held modifiers to be released before changing modes.

A tray icon (possibly under the hidden-icons arrow) shows the current state.
Right-click it to pause swapping or exit and restore normal keys.
Only one copy can run at a time.

TEST BEFORE ADDING TO STARTUP
With Apple connected: Command+Tab switches apps; Option opens Start.
With Apple unplugged: laptop Alt+Tab and Windows key work normally.
Test both sides, unplug/replug, and sleep/wake.

LIMITATIONS
The app must remain running. It uses a Windows keyboard hook, so Windows
can remove the hook if the process becomes unresponsive. This is not a
driver-level solution or a guarantee of improved reliability over PowerToys.
When run normally, it cannot remap input reliably in administrator apps.
Windows sign-in and secure prompts are outside the scope of this app.
This is an unsigned locally built application. Source is included for review.

OPTIONAL STARTUP (after testing)
Press Win+R, enter shell:startup, and place a SHORTCUT to AppleKeySwap.exe
in that folder. Remove the shortcut to stop automatic startup.

UNDO
Quit via the tray menu. There are no registry mappings or drivers to undo.
Then delete the app and any startup shortcut if no longer wanted.

BUILD FROM INCLUDED SOURCE (64-bit .NET Framework C# compiler)
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:x64 /out:AppleKeySwap.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll AppleKeySwap.cs
