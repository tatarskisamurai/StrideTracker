# Stride

MVP Windows time tracker that records how long each foreground app stays active.

## Current stage

- Polls active window every second.
- Aggregates time by process name.
- Prints live samples to console.
- Saves report to JSON on stop (`Ctrl+C`).

## Run

1. Install .NET 8 SDK on Windows.
2. Open terminal in `StrideTracker`.
3. Run:

```powershell
dotnet run
```

Stop with `Ctrl+C` and get a `usage-YYYYMMDD-HHMMSS.json` report in the build output folder.

## Next steps

- Add SQLite storage and session history.
- Build tray app (WinUI/WPF) instead of console.
- Add category mapping (work/study/entertainment).
- Track idle time and exclude AFK periods.
