<p align="center">
  <img src="images/icon.png" width="96" height="96" alt="XivMark icon">
</p>

<h1 align="center">XivMark</h1>

A Dalamud plugin that automatically marks enemy players by job in FFXIV Frontline. Defaults to Dark Knight and Dancer.

## Install

Add this URL in `/xlsettings` → **Experimental** → **Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/kuchris/DalamudPlugins/main/repo.json
```

Enable the repository, then search for **XivMark** in `/xlplugins`. Open `/xivmark` to configure and enable it.

If switching from a dev build, disable that copy first. Manual downloads are available under [Releases](https://github.com/kuchris/xivmark/releases).

## Features

- Choose target jobs and the order of up to 13 markers: Attack 1–8, Chain 1–3 and Stop 1–2.
- Customize range, marking interval, start delay and combat/death filters.
- Keep assignments fixed through death and distance.
- Optionally clear enabled markers on unselected enemy jobs, including pre-existing marks.
- View live status and nearby players in Diagnostics.

## Commands

| Command | Action |
| --- | --- |
| `/xivmark` | Open settings |
| `/xivmark on` | Enable automation |
| `/xivmark off` | Pause automation and keep existing marks |
| `/xivmark status` | Print current status |

## Build

Requires .NET SDK 10 and Dalamud API 15 development files.

```powershell
dotnet build XivMark.slnx -c Release
dotnet run --project XivMark.Tests/XivMark.Tests.csproj -c Release
```
