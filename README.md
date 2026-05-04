# Stride

MVP Windows time tracker that records how long each foreground app stays active.

## Current stage

- WinForms GUI with live table of app usage.
- Polls active window every second.
- Aggregates time by process name.
- Saves report to JSON from GUI button.
- Automatically saves tracker state on close and restores it on next launch.

## Run

1. Install .NET 8 SDK on Windows.
2. Open terminal in `StrideTracker`.
3. Run:

```powershell
dotnet run
```

Use buttons:

- `Start` to begin tracking.
- `Stop` to pause tracking.
- `Save report` to export `usage-YYYYMMDD-HHMMSS.json`.

## Next steps

- Add SQLite storage and session history.
- Add system tray mode and autostart with Windows.
- Add category mapping (work/study/entertainment).
- Track idle time and exclude AFK periods.
