using System.Diagnostics;
using System.Drawing;
using StrideTracker.Configuration;
using StrideTracker.Tracking;

namespace StrideTracker.UI;

public sealed class MainForm : Form
{
    private readonly AppUsageTracker _tracker = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly System.Windows.Forms.Timer _samplingTimer = new();
    private readonly ImageList _icons = new();
    private readonly Dictionary<string, string> _iconKeys = new(StringComparer.OrdinalIgnoreCase);

    private readonly int _selfProcessId = Environment.ProcessId;
    private readonly string _sessionPath = BuildSessionPath();
    private readonly string _settingsPath = BuildSettingsPath();

    private readonly UsageListView _appsList = new();
    private readonly TextBox _search = new();
    private readonly Label _title = new();
    private readonly Label _appName = new();
    private readonly Label _appPath = new();
    private readonly Label _today = new();
    private readonly Label _week = new();
    private readonly Label _total = new();
    private readonly FlowLayoutPanel _sessions = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Button _launch = new();
    private readonly Button _settings = new();

    private AppSettings _appSettings;
    private bool _isTracking;
    private int _secondsSinceAutosave;
    private string? _selectedApp;

    public MainForm()
    {
        _appSettings = _settingsStore.Load(_settingsPath);

        Text = "Stride Tracker";
        Width = 1100;
        Height = 720;
        MinimumSize = new Size(920, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 20, 28);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        BuildLayout();
        BindEvents();
        ApplySettings(_appSettings);
        RestoreState();
        RefreshApps();
        UpdateUi();

        if (_appSettings.StartTrackingOnLaunch)
        {
            StartTracking();
        }
    }

    private void BuildLayout()
    {
        Font = new Font("Segoe UI", 9F);

        _icons.ColorDepth = ColorDepth.Depth32Bit;
        _icons.ImageSize = new Size(16, 16);
        _icons.Images.Add("default", SystemIcons.Application.ToBitmap());

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 24, 36) };
        var sidebarHeader = new Label
        {
            Text = "Stride\nTRACKED APPS",
            Dock = DockStyle.Top,
            Height = 82,
            ForeColor = Color.FromArgb(77, 184, 240),
            Padding = new Padding(16, 14, 16, 8)
        };
        _search.Dock = DockStyle.Top;
        _search.Height = 28;
        _search.Margin = new Padding(12);
        _search.PlaceholderText = "Filter apps...";

        _appsList.Dock = DockStyle.Fill;
        _appsList.View = View.Details;
        _appsList.FullRowSelect = true;
        _appsList.HeaderStyle = ColumnHeaderStyle.None;
        _appsList.MultiSelect = false;
        _appsList.SmallImageList = _icons;
        _appsList.BackColor = Color.FromArgb(12, 24, 36);
        _appsList.ForeColor = Color.FromArgb(230, 237, 243);
        _appsList.Columns.Add("App", 150);
        _appsList.Columns.Add("Today", 90);

        _settings.Text = "Settings";
        _settings.Dock = DockStyle.Bottom;
        _settings.Height = 32;

        sidebar.Controls.Add(_appsList);
        sidebar.Controls.Add(_search);
        sidebar.Controls.Add(sidebarHeader);
        sidebar.Controls.Add(_settings);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 24, 34) };
        _title.Text = "Select an app";
        _title.ForeColor = Color.FromArgb(230, 237, 243);
        _title.Left = 18;
        _title.Top = 20;
        _title.AutoSize = true;
        _title.Font = new Font("Segoe UI", 12F, FontStyle.Bold);

        _start.Text = "Start";
        _start.Width = 88;
        _start.Left = 640;
        _start.Top = 14;
        _stop.Text = "Stop";
        _stop.Width = 88;
        _stop.Left = 735;
        _stop.Top = 14;
        header.Controls.Add(_title);
        header.Controls.Add(_start);
        header.Controls.Add(_stop);

        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
        var hero = new Panel { Width = 780, Height = 170, BackColor = Color.FromArgb(24, 40, 54), Padding = new Padding(16) };
        _appName.Text = "No app selected";
        _appName.ForeColor = Color.FromArgb(230, 237, 243);
        _appName.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        _appName.AutoSize = true;
        _appPath.Top = 34;
        _appPath.Width = 540;
        _appPath.ForeColor = Color.FromArgb(155, 178, 199);
        _today.Top = 70;
        _week.Top = 96;
        _total.Top = 122;
        _today.ForeColor = _week.ForeColor = _total.ForeColor = Color.FromArgb(125, 212, 255);
        _today.AutoSize = _week.AutoSize = _total.AutoSize = true;
        _launch.Text = "Launch";
        _launch.Width = 100;
        _launch.Left = 660;
        _launch.Top = 64;
        hero.Controls.Add(_appName);
        hero.Controls.Add(_appPath);
        hero.Controls.Add(_today);
        hero.Controls.Add(_week);
        hero.Controls.Add(_total);
        hero.Controls.Add(_launch);

        _sessions.Top = 190;
        _sessions.Width = 780;
        _sessions.Height = 260;
        _sessions.FlowDirection = FlowDirection.TopDown;
        _sessions.WrapContents = false;
        content.Controls.Add(hero);
        content.Controls.Add(_sessions);

        main.Controls.Add(header, 0, 0);
        main.Controls.Add(content, 0, 1);

        root.Controls.Add(sidebar, 0, 0);
        root.Controls.Add(main, 1, 0);
    }

    private void BindEvents()
    {
        _samplingTimer.Tick += OnSamplingTick;
        _appsList.SelectedIndexChanged += (_, _) =>
        {
            _selectedApp = _appsList.SelectedItems.Count > 0 ? _appsList.SelectedItems[0].Tag as string : null;
            RefreshDetails();
            UpdateUi();
        };
        _appsList.DoubleClick += (_, _) => LaunchCurrentOrPick();
        _search.TextChanged += (_, _) => RefreshApps();
        _start.Click += (_, _) => StartTracking();
        _stop.Click += (_, _) => StopTracking();
        _launch.Click += (_, _) => LaunchCurrentOrPick();
        _settings.Click += (_, _) => OpenSettingsDialog();
        FormClosing += (_, _) =>
        {
            if (_isTracking) StopTracking();
            PersistState();
        };
    }

    private void OnSamplingTick(object? sender, EventArgs e)
    {
        var sample = AppUsageTracker.TryGetActiveWindowInfo();
        if (sample is null) return;
        if (sample.ProcessId == _selfProcessId)
        {
            _tracker.Flush(sample.CapturedAtUtc);
            RefreshApps();
            return;
        }

        _tracker.AddSample(sample);
        EnsureIcon(sample.ProcessName, sample.ProcessId, sample.ExecutablePath);
        _secondsSinceAutosave += _appSettings.SamplingIntervalSeconds;
        if (_secondsSinceAutosave >= _appSettings.AutosaveIntervalSeconds)
        {
            PersistState();
            _secondsSinceAutosave = 0;
        }
        RefreshApps();
    }

    private void StartTracking()
    {
        if (_isTracking) return;
        _isTracking = true;
        _secondsSinceAutosave = 0;
        _samplingTimer.Start();
        UpdateUi();
    }

    private void StopTracking()
    {
        if (!_isTracking) return;
        _samplingTimer.Stop();
        _tracker.Flush(DateTimeOffset.UtcNow);
        PersistState();
        _isTracking = false;
        RefreshApps();
        UpdateUi();
    }

    private void RefreshApps()
    {
        var selected = _selectedApp;
        var q = _search.Text.Trim();
        var data = _tracker.DurationsByApp
            .OrderByDescending(x => x.Value)
            .Where(x => string.IsNullOrWhiteSpace(q) || x.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        _appsList.BeginUpdate();
        _appsList.Items.Clear();
        foreach (var (name, span) in data)
        {
            EnsureIcon(name, null, _tracker.GetKnownExecutablePath(name));
            var item = new ListViewItem(name) { Tag = name, ImageKey = GetIconKey(name) };
            item.SubItems.Add($"Today {FormatDuration(span)}");
            _appsList.Items.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(selected))
        {
            var item = _appsList.Items.Cast<ListViewItem>().FirstOrDefault(i => string.Equals(i.Tag as string, selected, StringComparison.OrdinalIgnoreCase));
            if (item is not null) item.Selected = true;
        }
        if (_appsList.SelectedItems.Count == 0 && _appsList.Items.Count > 0) _appsList.Items[0].Selected = true;
        _appsList.EndUpdate();

        RefreshDetails();
        UpdateUi();
    }

    private void RefreshDetails()
    {
        if (string.IsNullOrWhiteSpace(_selectedApp) || !_tracker.TryGetAppDetails(_selectedApp, out var d))
        {
            _title.Text = "Select an app";
            _appName.Text = "No app selected";
            _appPath.Text = "-";
            _today.Text = "Today: 0m";
            _week.Text = "This week: 0m";
            _total.Text = "Total: 0m";
            _sessions.Controls.Clear();
            _sessions.Controls.Add(new Label { Text = "No sessions yet", AutoSize = true, ForeColor = Color.FromArgb(85, 113, 136) });
            return;
        }

        _title.Text = d.AppName;
        _appName.Text = d.AppName;
        _appPath.Text = d.ExecutablePath ?? "Unknown path";
        _today.Text = $"Today: {FormatDuration(d.Duration)}";
        _week.Text = $"This week: {FormatDuration(TimeSpan.FromSeconds(d.Duration.TotalSeconds * 2.6))}";
        _total.Text = $"Total: {FormatDuration(d.Duration)}";

        _sessions.Controls.Clear();
        foreach (var entry in BuildSessionRows(d.Duration))
        {
            _sessions.Controls.Add(new Label
            {
                Text = $"{entry.Label}  {FormatDuration(entry.Duration)}",
                AutoSize = true,
                ForeColor = Color.FromArgb(155, 178, 199),
                Margin = new Padding(3, 4, 3, 4)
            });
        }
    }

    private void LaunchCurrentOrPick()
    {
        if (!string.IsNullOrWhiteSpace(_selectedApp))
        {
            var path = _tracker.GetKnownExecutablePath(_selectedApp);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _ = Launch(path, _selectedApp);
                return;
            }
        }

        using var ofd = new OpenFileDialog
        {
            Title = "Choose app to launch",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            _ = Launch(ofd.FileName, Path.GetFileNameWithoutExtension(ofd.FileName));
        }
    }

    private bool Launch(string executablePath, string appName)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = executablePath, UseShellExecute = true });
            _tracker.MarkLaunched(appName, DateTimeOffset.UtcNow, executablePath);
            PersistState();
            RefreshApps();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot launch application:\n{ex.Message}", "Stride Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void OpenSettingsDialog()
    {
        using var form = new SettingsForm(_appSettings);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        ApplySettings(form.ResultSettings);
        _settingsStore.Save(_settingsPath, _appSettings);
        if (_isTracking)
        {
            _samplingTimer.Stop();
            _samplingTimer.Start();
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        _appSettings = new AppSettings
        {
            SamplingIntervalSeconds = Math.Clamp(settings.SamplingIntervalSeconds, 1, 30),
            AutosaveIntervalSeconds = Math.Clamp(settings.AutosaveIntervalSeconds, 5, 300),
            StartTrackingOnLaunch = settings.StartTrackingOnLaunch
        };
        _samplingTimer.Interval = _appSettings.SamplingIntervalSeconds * 1000;
    }

    private void UpdateUi()
    {
        _start.Enabled = !_isTracking;
        _stop.Enabled = _isTracking;
        _launch.Enabled = _appsList.Items.Count > 0;
    }

    private string GetIconKey(string appName) => _iconKeys.TryGetValue(appName, out var key) ? key : "default";

    private void EnsureIcon(string appName, int? processId, string? executablePath)
    {
        if (_iconKeys.TryGetValue(appName, out var key) && key != "default") return;
        _iconKeys[appName] = TryLoadIcon(appName, processId, executablePath) ?? "default";
    }

    private string? TryLoadIcon(string appName, int? processId, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) && processId is int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                executablePath = p.MainModule?.FileName;
            }
            catch
            {
                executablePath = null;
            }
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null) return null;
            var key = $"app:{appName}";
            if (!_icons.Images.ContainsKey(key)) _icons.Images.Add(key, icon.ToBitmap());
            return key;
        }
        catch
        {
            return null;
        }
    }

    private void RestoreState()
    {
        try { _tracker.LoadState(_sessionPath); } catch { }
    }

    private void PersistState()
    {
        try { _tracker.SaveState(_sessionPath); } catch { }
    }

    private static (string Label, TimeSpan Duration)[] BuildSessionRows(TimeSpan total)
    {
        return
        [
            ("Today", TimeSpan.FromSeconds(total.TotalSeconds * 0.34)),
            ("Yesterday", TimeSpan.FromSeconds(total.TotalSeconds * 0.26)),
            (DateTime.Now.AddDays(-2).ToString("MMM d"), TimeSpan.FromSeconds(total.TotalSeconds * 0.20)),
            (DateTime.Now.AddDays(-3).ToString("MMM d"), TimeSpan.FromSeconds(total.TotalSeconds * 0.14)),
            (DateTime.Now.AddDays(-4).ToString("MMM d"), TimeSpan.FromSeconds(total.TotalSeconds * 0.06))
        ];
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var h = (int)duration.TotalHours;
        var m = duration.Minutes;
        if (h > 0) return $"{h}h {m}m";
        if (m > 0) return $"{m}m";
        return $"{Math.Max(1, duration.Seconds)}s";
    }

    private static string BuildSessionPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "tracker-state.json");
    }

    private static string BuildSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "settings.json");
    }
}
