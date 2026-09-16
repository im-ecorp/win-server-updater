# win-server-updater — WinUpdateManager

[![build](https://github.com/im-ecorp/win-server-updater/actions/workflows/build.yml/badge.svg)](https://github.com/im-ecorp/win-server-updater/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Windows Server 2008–2025](https://img.shields.io/badge/Windows%20Server-2008%20%E2%80%93%202025-0078D6)
![.NET Framework 3.5 / 4.x](https://img.shields.io/badge/.NET%20Framework-3.5%20%7C%204.x-512BD4)

A single, dependency-free **C# program for managing Windows Update on every Windows Server version**:
Windows Server 2008, 2008 R2, 2012, 2012 R2, 2016, 2019, 2022 and 2025 — Desktop Experience and Server Core.

It uses the built-in Windows Update Agent API (`Microsoft.Update.Session`), the Task Scheduler API, the Service
Control Manager and the registry directly. It is not a wrapper around PowerShell or batch scripts.

```
==========================================================================
 WinUpdateManager 1.0.0  -  Windows Server Update Manager
 Host: SRV01  |  Windows Server 2022 (build 20348.4052)  |  Server Core
 Administrator: Yes  |  Restart pending: No
==========================================================================
   1) Show system and Windows Update status
   2) Scan for available updates
   3) Install ALL available updates
   4) Choose which updates to install
   ...
```

## Features

| Area | What you can do |
|------|-----------------|
| Status | OS family/build/UBR, Server Core, domain role, support lifecycle, WUA version, update source (WSUS / internet), last scan & install, service states, pending restart |
| Updates | Scan, download, install (one by one with live progress and per-update result), uninstall, hide/unhide |
| Filters | KB, exclude KB, classification, MSRC severity, title include/exclude, max size, optional updates, drivers, source (WSUS / Windows Update / Microsoft Update), raw WUA criteria |
| Restart | Detects pending restart (CBS, WU, file renames, rename, domain join), restart with delay, abort restart, `--reboot if-required` |
| Policy / WSUS | Automatic update mode, install day/time, WSUS server + status server, target group, reset |
| Services | Status, start/stop/restart, startup type (fix a disabled `wuauserv`), full **Windows Update component reset** |
| Automation | Creates a Task Scheduler job running as SYSTEM (daily/weekly), JSON output, meaningful exit codes, log files |
| Microsoft Update | Enable/disable updates for SQL Server, .NET, Visual C++ and other Microsoft products |
| Safe testing | `--simulate` runs everything against a built-in virtual server — nothing is changed |

## Compatibility

| Windows Server | Runtime used | Notes |
|----------------|--------------|-------|
| 2008 / 2008 R2 | .NET 3.5 (in-box) or 4.x | Enable the .NET Framework 3.5.1 feature on 2008 R2 Server Core |
| 2012 / 2012 R2 | .NET 4.5+ (in-box) | |
| 2016 / 2019 / 2022 / 2025 | .NET 4.6.2 – 4.8.1 (in-box) | Desktop Experience and Server Core |

The main build targets .NET Framework 3.5 and ships with `WinUpdateManager.exe.config`, which lets the **same exe**
run on the 2.0 runtime *and* the 4.x runtime. **Always keep the `.config` file next to the `.exe`.**
A `net40` build is also produced for servers that only have .NET 4.x.

## Download

- **Releases:** download `WinUpdateManager.zip` from the
  [Releases page](https://github.com/im-ecorp/win-server-updater/releases) (created automatically for `v*` tags).
- **Latest build:** open the latest successful run on the
  [Actions page](https://github.com/im-ecorp/win-server-updater/actions/workflows/build.yml) and download the `WinUpdateManager` artifact.
- **From source:** see [Building from source](#building-from-source).

On the server you can also download a release directly from an elevated PowerShell:

```powershell
New-Item -ItemType Directory -Force C:\Tools\WinUpdateManager | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest https://github.com/im-ecorp/win-server-updater/releases/latest/download/WinUpdateManager.zip -OutFile C:\Tools\WinUpdateManager.zip
Expand-Archive C:\Tools\WinUpdateManager.zip C:\Tools\WinUpdateManager -Force
```

- `releases/latest/download/...` only points to **full** releases. To get a pre-release, use its version in the URL,
  e.g. `https://github.com/im-ecorp/win-server-updater/releases/download/v1.0.0-beta.1/WinUpdateManager.zip`.
- Each release also has `WinUpdateManager.zip.sha256`. Check the download with
  `(Get-FileHash C:\Tools\WinUpdateManager.zip -Algorithm SHA256).Hash` and compare the values.

## Quick start

1. Copy `WinUpdateManager.exe` **and** `WinUpdateManager.exe.config` to the server, e.g. `C:\Tools\WinUpdateManager\`.
2. Open **Command Prompt or PowerShell as Administrator**.
3. Run it:

```bat
cd C:\Tools\WinUpdateManager

REM Try it safely first - a simulated server, nothing is changed
WinUpdateManager.exe --simulate

REM Interactive menu on the real server
WinUpdateManager.exe

REM Or use commands
WinUpdateManager.exe status
WinUpdateManager.exe list
WinUpdateManager.exe install --yes --reboot if-required
```

## Command reference

```text
WinUpdateManager <command> [options]

status                    System, Windows Update configuration and reboot state
list                      Search available updates (--hidden / --installed for other scopes)
download                  Download matching updates without installing
install                   Download and install matching updates
uninstall --kb <KB>       Uninstall an installed update
hide / unhide             Hide or unhide updates
history [--count n]       Windows Update history
pending-reboot            Exit code 3010 if a restart is pending
reboot [--delay s]        Restart (--abort cancels)
config [show|set|reset]   Automatic update policy and WSUS
services [status|start|stop|restart|startup|reset]
schedule [show|create|delete]
microsoft-update [status|enable|disable]
menu | help [command] | version
```

Global options: `--json`, `--yes` / `-y`, `--simulate`, `--log <file>`, `--no-log`, `--quiet`, `--no-color`.
Run `WinUpdateManager help <command>` for all options of a command.

### Examples

```bat
REM Only security and critical updates, skip preview updates
WinUpdateManager list --category Security,Critical --exclude-title Preview

REM Install specific KBs without prompting
WinUpdateManager install --kb KB5065432,KB5064401 --yes

REM Install everything except drivers larger than 500 MB, restart 5 minutes later if needed
WinUpdateManager install --max-size-mb 500 --yes --reboot if-required --reboot-delay 300

REM Search Microsoft Update directly even if the server normally uses WSUS
WinUpdateManager list --source microsoftupdate

REM Point the server to WSUS and apply immediately
WinUpdateManager config set --wsus-server http://wsus01:8530 --target-group Production --mode download --restart-service

REM Weekly automatic installation, Sunday 03:00, restart if required
WinUpdateManager schedule create --frequency weekly --day sunday --at 03:00 --reboot if-required --exclude-title Preview

REM Repair a broken Windows Update client
WinUpdateManager services reset --yes

REM Machine-readable output for monitoring
WinUpdateManager status --json
WinUpdateManager list --json > updates.json
```

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error |
| 2 | Invalid command line |
| 4 | Some updates failed |
| 5 | Administrator rights required |
| 3010 | Success, restart required |

### Logs

Every run is logged to `%ProgramData%\WinUpdateManager\logs\WinUpdateManager-YYYYMMDD.log`
(change with `--log`, disable with `--no-log`). Scheduled tasks log to the same place.

## Testing with the simulator

`--simulate` replaces all Windows APIs with a virtual Windows Server 2022 that has cumulative, .NET, Defender,
MSRT, preview, driver and SQL Server (Microsoft Update) updates. One update fails on its first installation
so you can see error handling, and installs create a pending restart. Nothing on your computer is modified.

```bat
WinUpdateManager --simulate
WinUpdateManager --simulate install --yes
WinUpdateManager --simulate schedule create --day monday --at 02:00
```

## Building from source

Requires the .NET 8 SDK (Windows, Linux or macOS — the .NET Framework reference assemblies come from NuGet).

```bash
git clone https://github.com/im-ecorp/win-server-updater.git
cd win-server-updater

dotnet build src/WinUpdateManager/WinUpdateManager.csproj -c Release
# output: src/WinUpdateManager/bin/Release/net35/  and  .../net40/

dotnet test tests/WinUpdateManager.Tests/WinUpdateManager.Tests.csproj
```

The tests run on any OS: platform independent code and the simulator are compiled into the test project,
and GitHub Actions additionally builds and smoke-tests the executable on Windows Server 2022 and 2025 runners.

### Project layout

```
src/WinUpdateManager/
  Program.cs            entry point, chooses real Windows or simulated services
  App.cs                all commands (shared by the command line and the menu)
  Cli/                  argument parser, help text, selection parser
  Core/                 models, filters, policy logic, error codes, OS catalog
  Interactive/          menu UI
  Output/               console tables/colors, JSON writer, logger
  Simulation/           virtual server used by --simulate and tests
  Windows/              WUA COM, registry, services, Task Scheduler, restart (Windows only)
tests/WinUpdateManager.Tests/
```

## Notes and limitations

- Installing updates requires an elevated process (Administrator or SYSTEM). Read-only commands work without elevation.
- Windows Update Agent does not allow remote installation over DCOM; run the tool on each server
  (for many servers use the scheduled task, your RMM tool, or `Invoke-Command`/PsExec to start it).
- Domain Group Policy overrides values written by `config set` at the next policy refresh.
- Some updates (for example recent cumulative updates) cannot be removed through the WUA API; the tool prints
  the `wusa.exe` / `DISM` alternative.
- Windows Server 2008/2008 R2/2012/2012 R2 only receive updates with Extended Security Updates (ESU).

## Contributing

Issues and pull requests are welcome. Please run `dotnet test` before submitting, and test changes to the
`Windows/` folder on a real Windows Server (or use the `--simulate` mode for everything else).

## License

[MIT](LICENSE) © 2026 Mohammad Hossein Saeidi
