# KEYMON

KEYMON is a Windows desktop background app that watches your typing and activity rhythm locally, then shows your focus level through a pixel cat overlay and taskbar tray icon.

## Download The App

Normal users should download KEYMON from the repository's **Releases** page.

1. Open the GitHub repository.
2. Click **Releases** on the right side of the repository page.
3. Download the latest `KEYMON-windows-x64.zip` file.
4. Extract the zip file.
5. Double-click `KEYMON.exe`.

You do not need Visual Studio, the .NET SDK, or `dotnet run` when using the release zip.

## Important: Do Not Use Code Download ZIP

Use **Releases**, not **Code -> Download ZIP**.

The Code ZIP contains the source code only. It is for developers and does not include the packaged Windows runtime files needed by normal users. The release zip contains `KEYMON.exe`, required runtime files, libraries, and app assets.

## What You Will See

- A pixel cat overlay appears on screen.
- A KEYMON icon appears in the Windows taskbar tray.
- Right-click the tray icon to open the dashboard, pause monitoring, toggle the overlay, enable startup, or exit.
- If KEYMON is already running, opening `KEYMON.exe` again will not start a second copy.

## Privacy

KEYMON uses local rhythm-based metrics such as input timing, activity counts, window switching, and movement patterns. It does not store typed text content or send telemetry to an external server.

## Build From Source

Developers can build KEYMON from source with the .NET 10 SDK.

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Clone the repository.
3. Build the solution:

```powershell
dotnet build keymon.sln
```

4. Run tests:

```powershell
dotnet test keymon.sln
```

5. Run from source:

```powershell
dotnet run --project src/Keymon.Core.csproj
```

## Create A Release Package

To create a self-contained Windows x64 package:

```powershell
dotnet publish src/Keymon.Core.csproj -c Release -r win-x64 --self-contained true -o release/KEYMON-windows-x64 /p:DebugType=None /p:DebugSymbols=false
Compress-Archive -Path release/KEYMON-windows-x64/* -DestinationPath release/KEYMON-windows-x64.zip -Force
```

Upload `release/KEYMON-windows-x64.zip` to a GitHub Release.

## Team Rules

- Do not push directly to `main`.
- Use a feature branch, for example `feature/your-feature-name`.
- Open a pull request before merging.
