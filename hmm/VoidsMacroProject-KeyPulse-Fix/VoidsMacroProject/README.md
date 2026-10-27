# Voids Macro Project

A Windows 11 desktop macro utility written in C# / WPF. The project targets .NET 10 and publishes as a self-contained Windows x64 executable.

## Build the EXE

1. On a Windows 11 computer, install the **.NET 10 SDK**: https://dotnet.microsoft.com/download/dotnet/10.0
2. Extract this project folder.
3. Open PowerShell in the extracted folder.
4. Run:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\build.ps1
   ```

5. The executable will be created at `dist\VoidsMacroProject.exe`.

The current build environment did not have the .NET SDK installed, so the project source and build script are provided rather than a precompiled EXE. The build script performs a self-contained publish, so the target computer should not need a separately installed .NET runtime.

## Main behavior

- Four presets, switched with top-row number keys **1–4 while the macro is enabled**. The number keys are reserved for switching and are not macro inputs.
- Selecting a preset does not automatically press any action key.
- Hold an enabled key or mouse button to repeat it. Release it to stop.
- Physical keyboard and mouse input is passed through normally; the program does not swallow physical key events.
- Switching presets cancels all existing repeaters. A key already held during a switch will not restart under the new preset; release and press it again.
- Disable the macro using the application button. This cancels repetition and stops preset switching by the number keys; normal physical input continues.
- Each key can use the global interval or a custom interval from 10 to 2000 ms.
- Settings save automatically, with manual JSON import/export available.
- Supported mouse inputs: left, right, and middle buttons.

## Settings location

`%APPDATA%\Voids Macro Project\config.json`

## Important limitations

The app uses standard Windows low-level input hooks and `SendInput`. It does not inject code into Roblox or modify game files. Some games may ignore simulated input or handle it differently. A macro cannot reduce network latency, bypass ability cooldowns, or guarantee that every input registers with the game server. Test it first in a safe context.

The macro begins sending repeated pulses after the configured interval; the user's original physical keydown still passes through as a normal input. Windows scheduling and application input handling can make real timing less exact than the configured interval.
