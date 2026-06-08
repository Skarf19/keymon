# KEYMON

KEYMON is a Windows desktop app that monitors your local typing and activity rhythm, then shows your focus level with a pixel cat overlay and taskbar tray icon.

## Use The App

For normal use, download and run the installer:

```text
release/KEYMON-Setup.exe
```

If you are on this computer, the setup file is here:

```text
C:\Users\user\Desktop\Manage\keymon-develop\release\KEYMON-Setup.exe
```

To share the app through Google Drive, upload this file:

```text
C:\Users\user\Downloads\KEYMON-GoogleDrive-Upload\KEYMON-Setup.exe
```

## Install

1. Download `KEYMON-Setup.exe`.
2. Double-click `KEYMON-Setup.exe`.
3. If Windows SmartScreen appears, click **More info**, then **Run anyway**.
4. KEYMON installs to your user app folder.
5. A KEYMON shortcut appears on your Desktop and in the Start Menu.
6. Open KEYMON from the Desktop shortcut.

Windows may show a warning because this installer is not code-signed yet. This is common for new apps shared directly through Google Drive.

## Do Not Use Code Download ZIP

Do not use **Code -> Download ZIP** for normal installation. That download contains source code, not the ready-to-run installer.

Use `KEYMON-Setup.exe` instead.

## What You Will See

- A pixel cat overlay appears on screen.
- A KEYMON icon appears in the Windows taskbar tray.
- Right-click the tray icon to open the dashboard, pause monitoring, toggle the overlay, enable startup, or exit.
- If KEYMON is already running, opening it again will not start a second copy.

## Privacy

KEYMON uses local rhythm-based metrics such as input timing, activity counts, window switching, and movement patterns. It does not store typed text content or send telemetry to an external server.

## Build From Source

Developers can build KEYMON from source with the .NET 10 SDK.

```powershell
dotnet build keymon.sln
dotnet test keymon.sln
dotnet run --project src/Keymon.Core.csproj
```

## Create A Release Zip

```powershell
dotnet publish src/Keymon.Core.csproj -c Release -r win-x64 --self-contained true -o release/KEYMON-windows-x64 /p:DebugType=None /p:DebugSymbols=false
Compress-Archive -Path release/KEYMON-windows-x64/* -DestinationPath release/KEYMON-windows-x64.zip -Force
```

The current setup installer is stored at:

```text
release/KEYMON-Setup.exe
```

## Team Rules

- Do not push directly to `main`.
- Use a feature branch, for example `feature/your-feature-name`.
- Open a pull request before merging.