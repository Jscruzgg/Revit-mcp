# Revit Multi Bridge (MCP)

MCP bridge between Claude and Autodesk Revit **2024, 2025, 2026 and 2027** that serves **several Revit windows at once**.
Every Revit window that starts takes the first free port of a shared pool (**5310-5329**), whatever its version, so three windows of
Revit 2024 and two of Revit 2026 can all be open and reachable at the same time.

Based on the MCP part of the Claude Revit Bridge add-in (C# scripts run on Revit's API thread, dry run before writing, the
Bridge window with Start / Stop, Re-claim and Cancel), extended for multiport.

## How it works

```
Claude ──stdio──> RevitMultiBridgeGateway.exe ──HTTP──> Revit 2024 (port 5310)
                                               ├─────> Revit 2024 (port 5311)
                                               ├─────> Revit 2025 (port 5312)
                                               └─────> Revit 2027 (port 5313)
```

- **Add-in** (`src/Core` + `src/Revit`, one build per Revit version): an MCP server inside each Revit window, on its own port.
  `GET /status` says who is on a port (pid, Revit year, active document, view, open documents, busy).
- **Gateway** (`src/Gateway`): the one MCP server Claude starts. It finds every Revit with the bridge (probes the pool) and forwards
  each call to the window named by `target`.
- Each Revit window is also a complete MCP server by itself (`http://localhost:<port>/mcp`) if you prefer to connect to one directly.

## Tools

| Tool | What it does |
|---|---|
| `revit_instances` | Every Revit window with the bridge: port, version, pid, active document, busy |
| `revit_read` | C# script in the active document, always rolled back |
| `revit_write` | C# script that modifies the model: dry run by default, `execute=true` commits one undo step |
| `revit_write_unsafe` | C# script without a host transaction (save, sync, open views...) |
| `revit_status` | Version, active document / view, open documents |
| `revit_cancel` | Cancels the running script of that window |

`target` (all tools but `revit_instances`): a port (`"5311"`, most precise), `"pid:1234"`, a year (`"2025"`) or part of the
document name. Optional when only one Revit is open; an ambiguous target (two Revit 2024 windows and `"2024"`) is refused with the list.
Calls to different windows run in parallel; one window runs one script at a time (a second call gets `BUSY`).

Script globals: `Doc`, `UIDoc`, `App`, `UIApp`, `Print(object)`, `CancelToken`.

## The Multi Bridge window (ribbon tab MCP > Multi Bridge)

- Status of this window: RUNNING on port N / STOPPED, its URL, the running script.
- **Bridges on this PC**: every Revit window with the bridge, any version (this one first).
- **Recent calls** to this window, with OK / Failed pills.
- **Start / Stop**: joins or leaves the pool (Start takes the selected port, or the first free one).
- **Re-claim**: takes the selected port; the Revit that had it moves to another free port and stays reachable.
- **Cancel script**, **Open log** (`%LOCALAPPDATA%\revit_multi_bridge\bridge.log`, shared, each line names its pid / Revit / port).

## Build and install

Needs pyRevit (Roslyn is copied from it) and the .NET Framework 4.8 that Windows has. No Visual Studio, no .NET SDK.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Register   # every installed Revit 2024-2027 + gateway + add to Claude Code
powershell -ExecutionPolicy Bypass -File build.ps1 -Years 2025  # one version
powershell -ExecutionPolicy Bypass -File build.ps1 -TestOut C:\temp\rmb   # compile only
```

Close the Revit versions you install for (their dll is locked while open). Installed per user, no admin:
`%APPDATA%\Autodesk\Revit\Addins\<Year>\RevitMultiBridge\` + `RevitMultiBridge.addin`, gateway in
`%LOCALAPPDATA%\revit_multi_bridge\gateway\`. `-Register` runs `claude mcp add -s user revit-multi -- <gateway exe>`.

Settings (environment variables): `REVIT_MULTI_BRIDGE_PORTS=5310-5329` (pool), `REVIT_MULTI_BRIDGE_AUTOSTART=off` (start from the window).

## Tests (no Revit needed)

```powershell
powershell -ExecutionPolicy Bypass -File tests\build_tests.ps1
powershell -ExecutionPolicy Bypass -File tests\run_multiport_test.ps1
```

`FakeRevit.exe` runs the real server and window with a fake Revit backend; the test starts four of them (two Revit 2024, one 2025,
one 2027), drives the gateway over stdio like Claude does (targets, parallel calls, busy, cancel, Re-claim) and saves a picture
of the window in `tests\out\bridge_window.png`.

## Sources

C# 5 syntax only: Revit 2024 builds with the .NET Framework `csc`, Revit 2025+ (.NET 8 / .NET 10) with Roslyn through
`src\Build\RRCompile.cs` (`/define:REVIT2026_OR_GREATER` from 2026). On .NET 8/10 the add-in loads its Roslyn at startup so an
older copy shipped by another add-in cannot break script compilation.
