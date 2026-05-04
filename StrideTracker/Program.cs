using StrideTracker.Tracking;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var tracker = new AppUsageTracker();
var startedAt = DateTimeOffset.Now;

Console.WriteLine("StrideTracker started.");
Console.WriteLine("Press Ctrl+C to stop and save report.");
Console.WriteLine();

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var sample = AppUsageTracker.TryGetActiveWindowInfo();
        if (sample is not null)
        {
            tracker.AddSample(sample);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {sample.ProcessName} - {sample.WindowTitle}");
        }

        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Graceful shutdown via Ctrl+C.
}

tracker.Flush(DateTimeOffset.UtcNow);

var reportPath = Path.Combine(AppContext.BaseDirectory, $"usage-{DateTime.Now:yyyyMMdd-HHmmss}.json");
await tracker.SaveJsonAsync(reportPath, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"Tracking window: {startedAt:G} -> {DateTimeOffset.Now:G}");
Console.WriteLine("Top apps:");

foreach (var item in tracker.DurationsByApp.OrderByDescending(x => x.Value).Take(10))
{
    Console.WriteLine($"- {item.Key}: {item.Value:hh\\:mm\\:ss}");
}

Console.WriteLine();
Console.WriteLine($"Report saved: {reportPath}");
