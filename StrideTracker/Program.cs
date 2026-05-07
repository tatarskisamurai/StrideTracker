using StrideTracker.UI;

try
{
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
catch (Exception ex)
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var logDir = Path.Combine(appData, "StrideTracker");
    Directory.CreateDirectory(logDir);
    var logPath = Path.Combine(logDir, "startup-error.log");
    File.WriteAllText(logPath, ex.ToString());

    MessageBox.Show(
        $"StrideTracker failed to start.\n\nDetails written to:\n{logPath}\n\n{ex.Message}",
        "StrideTracker Startup Error",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}
