# Keyboard Key Switch

A small Windows tray app with configurable key bindings that activate when a selected keyboard is connected. No keyboard driver or installation is needed.

## Download

[Download AppleKeySwap.exe for Windows](https://github.com/jessewheelernz/keyboard-key-switch/releases/latest/download/AppleKeySwap.exe) or [view the latest release](https://github.com/jessewheelernz/keyboard-key-switch/releases/latest).

Save the executable in a writable folder and double-click it to start. No installation is needed. This is an unsigned x64 Windows build; Windows may show an unrecognized-app warning. Settings are saved alongside the executable. Mac and virtual-machine compatibility has not yet been tested.

## Choose a keyboard and bindings

1. Do not use conflicting key remappers alongside this app.
2. Run `AppleKeySwap.exe`. Settings opens automatically on the first run.
3. Choose a keyboard from the automatically detected list. The list refreshes about every 1.5 seconds as devices connect or disconnect.
4. Use **Add binding** and choose a **From key** and **To key**. Each source and destination can appear only once. A swap needs two rows, one for each direction.
5. Choose **Save and close**. Bindings activate while the selected keyboard is connected. When it disconnects, normal keys return.

**Bindings affect every keyboard while the selected keyboard is connected.** The selected keyboard is an activation trigger, not an input filter. Windows' low-level keyboard hook does not identify the source keyboard; this app does not provide per-device remapping.

Open Settings again by double-clicking the tray icon or choosing **Settings...** from its menu. Remapping is suspended while Settings is open, making it possible to edit bindings without typing through them. Closing with Cancel discards changes.

The **Command / Option preset** swaps left and right Windows/Command with left and right Alt/Option, preserving the original app's behavior. The default activation trigger is an Apple keyboard with USB ID `05AC:024F`. Choosing a detected device saves its specific device path. A disconnected selection stays in the list; if moving ports changes its path, select it again.

The list may include virtual keyboards or multiple entries exposed by a receiver. Device identifiers distinguish otherwise identical names. Only keyboards reported by Windows Raw Input can be listed.

## Windows on a Mac

This app can also be useful for choosing how a Mac keyboard behaves in Windows. In Settings, select the keyboard Windows detects and configure Command, Option and other keys to suit your preferred layout. The Command / Option swap preset is optional.

- **Boot Camp on an Intel Mac:** run the app inside Windows and select the built-in or connected keyboard. [Apple's Boot Camp guide](https://support.apple.com/guide/bootcamp-assistant/welcome/mac) covers running Windows on Intel-based Macs.
- **Parallels or another virtual machine:** run the app inside Windows and select the keyboard exposed to the guest, which may be a virtual keyboard. Only key presses delivered to Windows can be remapped. Check the VM's own keyboard mappings and shortcut settings for conflicts; [Parallels can translate shortcuts or reserve them for macOS](https://docs.parallels.com/landing/pdfm-ug/v20-en-us/parallels-desktop-for-mac-20-users-guide/parallels-desktop-preferences-and-virtual-machine-settings/parallels-desktop-preferences/shortcuts-preferences).

Mac and virtual-machine compatibility has not yet been tested. This is a Windows app; it does not remap keys in macOS. As with other setups, the selected keyboard activates bindings for all keyboards visible to Windows.

## Supported bindings

Single keys: letters, digits, punctuation, left/right modifiers, function keys, navigation keys and common numpad keys. Fn keys, special firmware keys, Pause/Break, Print Screen, macros and multi-key destination shortcuts are not supported. Key names are Windows names; the Apple labels are also shown for Command and Option.

Bindings are direct: A → B and B → C makes A send B, without applying the B → C rule to that injected event. Existing held mappings finish with their original destination when paused or when Settings opens. Release all keyboard keys before saving. Disconnecting the selected device releases any mapped keys still held.

Settings are saved beside the executable in `keyboard-settings.xml`. They are personal and excluded from Git. The app needs write access to that folder. Clear all bindings and save to disable mapping without removing the keyboard selection.

## Tray controls and startup

- **Settings...**: select a keyboard and edit bindings.
- **Pause remapping**: temporarily stop new remaps.
- **Exit and restore normal keys**: stop the app.

Only one copy can run at a time. The tray icon may be under Windows' hidden-icons arrow.

After testing, press Win+R, enter `shell:startup`, and place a shortcut to `AppleKeySwap.exe` there. Remove the shortcut to stop automatic startup. Quit the app to undo remapping; no registry mapping or driver needs removal.

## Build and check

From the project folder in PowerShell, using the Windows .NET Framework compiler:

```powershell
& C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:x64 /out:AppleKeySwap.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll AppleKeySwap.cs
$test = Start-Process .\AppleKeySwap.exe -ArgumentList '--self-test' -Wait -PassThru
$test.ExitCode
$uiTest = Start-Process .\AppleKeySwap.exe -ArgumentList '--ui-self-test' -Wait -PassThru
$uiTest.ExitCode
```

Exit code 0 means success. The self-test covers preset/custom bindings, validation, XML save/load and key-event scan codes. The UI test checks binding rows, disconnected-device selection and selection preservation. These tests do not install the remapping hook or type into other apps. The executable is built locally and excluded from Git.

Before enabling startup, manually check your chosen bindings on both keyboards; unplug/replug the selected device; test sleep/wake; and verify pause, Settings and exit. With the Apple preset, Command+Tab should switch apps and Option should open Start while connected. Unplug it and check normal laptop Alt+Tab and Windows-key behavior.

## Limitations

The app must remain running. Windows can remove its keyboard hook if it becomes unresponsive. This is an unsigned local app, not a driver-level solution. It cannot reliably remap input in administrator apps when run normally, or in Windows sign-in and secure prompts. Some games and applications that consume raw input may behave differently. Avoid conflicting keyboard remappers.

Native API references: [keyboard-hook event structure](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) and [virtual-key to scan-code translation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mapvirtualkeyw).
