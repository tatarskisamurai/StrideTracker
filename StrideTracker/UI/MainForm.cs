using System.Diagnostics;
using System.Drawing;
using StrideTracker.Configuration;
using StrideTracker.Tracking;

namespace StrideTracker.UI;

public sealed class MainForm : Form
{
    private readonly AppUsageTracker _tracker = new();
    private readonly System.Windows.Forms.Timer _samplingTimer = new();
    private readonly int _selfProcessId = Environment.ProcessId;
    private readonly string _sessionStatePath = BuildSessionStatePath();
    private readonly string _settingsPath = BuildSettingsPath();
    private readonly AppSettingsStore _settingsStore = new();

    private readonly MenuStrip _menuStrip = new();
    private readonly ToolStripMenuItem _settingsMenuItem = new("Settings");
    private readonly ToolStripMenuItem _preferencesMenuItem = new("Preferences...");
    private readonly TableLayoutPanel _rootLayout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
    private readonly Panel _contentHostPanel = new() { Dock = DockStyle.Fill };
    private readonly Panel _listPagePanel = new() { Dock = DockStyle.Fill };
    private readonly Panel _detailsPagePanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly UsageListView _usageListView = new();
    private readonly ImageList _appIcons = new();
    private readonly Dictionary<string, string> _iconKeyByApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultIconKey = "default";
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _launchButton = new();
    private readonly Button _appPageButton = new();
    private readonly Button _detailsBackButton = new();
    private readonly Button _detailsLaunchButton = new();
    private readonly Label _detailsAppNameValue = new();
    private readonly Label _detailsLastLaunchValue = new();
    private readonly Label _detailsTimeSpentValue = new();

    private AppSettings _settings;
    private bool _isTracking;
    private int _secondsSinceLastAutosave;
    private string? _activeDetailsAppName;

    public MainForm()
    {
        _settings = _settingsStore.Load(_settingsPath);

        Text = "Stride Tracker";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }
        else
        {
            var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon is not null)
            {
                Icon = appIcon;
            }
        }

        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 420);

        InitializeLayout();
        BindEvents();
        ApplySettings(_settings);
        RestorePreviousState();
        RefreshUsageList();
        UpdateUiState();

        if (_settings.StartTrackingOnLaunch)
        {
            StartTracking();
        }
    }

    private void InitializeLayout()
    {
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _menuStrip.Items.Add(_settingsMenuItem);
        _settingsMenuItem.DropDownItems.Add(_preferencesMenuItem);
        MainMenuStrip = _menuStrip;
        _menuStrip.Dock = DockStyle.Fill;

        _appIcons.ColorDepth = ColorDepth.Depth32Bit;
        _appIcons.ImageSize = new Size(16, 16);
        _appIcons.Images.Add(_defaultIconKey, SystemIcons.Application.ToBitmap());

        _usageListView.Dock = DockStyle.Fill;
        _usageListView.View = View.Details;
        _usageListView.FullRowSelect = true;
        _usageListView.GridLines = true;
        _usageListView.SmallImageList = _appIcons;
        _usageListView.Columns.Add("Application", 420);
        _usageListView.Columns.Add("Time", 160);
        _usageListView.Columns.Add("Percent", 120);
        _listPagePanel.Controls.Add(_usageListView);

        _detailsBackButton.Text = "< Back";
        _detailsBackButton.Width = 100;
        _detailsLaunchButton.Text = "Launch";
        _detailsLaunchButton.Width = 100;

        var detailsHeaderPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 10, 12, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        detailsHeaderPanel.Controls.AddRange([_detailsBackButton, _detailsLaunchButton]);

        var detailsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 140,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12)
        };
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailsTable.Controls.Add(new Label { Text = "Application:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        detailsTable.Controls.Add(_detailsAppNameValue, 1, 0);
        detailsTable.Controls.Add(new Label { Text = "Last launch:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        detailsTable.Controls.Add(_detailsLastLaunchValue, 1, 1);
        detailsTable.Controls.Add(new Label { Text = "Time spent:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        detailsTable.Controls.Add(_detailsTimeSpentValue, 1, 2);
        _detailsAppNameValue.AutoSize = true;
        _detailsLastLaunchValue.AutoSize = true;
        _detailsTimeSpentValue.AutoSize = true;

        _detailsPagePanel.Controls.Add(detailsTable);
        _detailsPagePanel.Controls.Add(detailsHeaderPanel);

        var controlsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 10, 12, 10),
            WrapContents = false
        };

        _startButton.Text = "Start";
        _startButton.Width = 100;

        _stopButton.Text = "Stop";
        _stopButton.Width = 100;

        _launchButton.Text = "Launch app";
        _launchButton.Width = 120;

        _appPageButton.Text = "App page";
        _appPageButton.Width = 110;

        controlsPanel.Controls.AddRange([_startButton, _stopButton, _launchButton, _appPageButton]);
        _contentHostPanel.Controls.Add(_listPagePanel);
        _contentHostPanel.Controls.Add(_detailsPagePanel);

        _rootLayout.Controls.Add(_menuStrip, 0, 0);
        _rootLayout.Controls.Add(_contentHostPanel, 0, 1);
        _rootLayout.Controls.Add(controlsPanel, 0, 2);

        Controls.Add(_rootLayout);
    }

    private void BindEvents()
    {
        _samplingTimer.Tick += OnSamplingTick;
        _startButton.Click += (_, _) => StartTracking();
        _stopButton.Click += (_, _) => StopTracking();
        _launchButton.Click += (_, _) => LaunchApp();
        _appPageButton.Click += (_, _) => OpenAppPage();
        _detailsBackButton.Click += (_, _) => ShowListPage();
        _detailsLaunchButton.Click += (_, _) => LaunchFromDetailsPage();
        _preferencesMenuItem.Click += (_, _) => OpenSettingsDialog();
        _usageListView.DoubleClick += (_, _) => OpenAppPage();
        _usageListView.SelectedIndexChanged += (_, _) => UpdateUiState();
        FormClosing += OnFormClosing;
    }

    private void StartTracking()
    {
        if (_isTracking)
        {
            return;
        }

        _isTracking = true;
        _secondsSinceLastAutosave = 0;
        _samplingTimer.Start();
        UpdateUiState();
    }

    private void StopTracking()
    {
        if (!_isTracking)
        {
            return;
        }

        _samplingTimer.Stop();
        _tracker.Flush(DateTimeOffset.UtcNow);
        PersistState();
        _isTracking = false;
        UpdateUiState();
        RefreshUsageList();
    }

    private void OnSamplingTick(object? sender, EventArgs e)
    {
        var sample = AppUsageTracker.TryGetActiveWindowInfo();
        if (sample is null)
        {
            return;
        }

        if (sample.ProcessId == _selfProcessId)
        {
            // Stop attribution when our own UI is focused.
            _tracker.Flush(sample.CapturedAtUtc);
            RefreshUsageList();
            return;
        }

        _tracker.AddSample(sample);
        EnsureAppIcon(sample.ProcessName, sample.ProcessId, sample.ExecutablePath);
        _secondsSinceLastAutosave += _settings.SamplingIntervalSeconds;
        if (_secondsSinceLastAutosave >= _settings.AutosaveIntervalSeconds)
        {
            PersistState();
            _secondsSinceLastAutosave = 0;
        }

        RefreshUsageList();
    }

    private void RefreshUsageList()
    {
        _usageListView.BeginUpdate();

        var snapshot = _tracker.DurationsByApp
            .OrderByDescending(x => x.Value)
            .ToArray();

        var total = snapshot.Sum(x => x.Value.TotalSeconds);
        var existingByApp = _usageListView.Items
            .Cast<ListViewItem>()
            .Where(item => item.Tag is string)
            .ToDictionary(item => (string)item.Tag!, item => item, StringComparer.OrdinalIgnoreCase);
        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetIndex = 0;

        foreach (var (appName, duration) in snapshot)
        {
            seenApps.Add(appName);
            var percent = total > 0 ? duration.TotalSeconds / total * 100d : 0d;
            if (!existingByApp.TryGetValue(appName, out var item))
            {
                item = new ListViewItem(appName)
                {
                    Tag = appName
                };
                item.SubItems.Add(string.Empty);
                item.SubItems.Add(string.Empty);
                _usageListView.Items.Add(item);
            }

            item.Text = appName;
            EnsureAppIcon(appName, processId: null, executablePath: _tracker.GetKnownExecutablePath(appName));
            item.ImageKey = GetIconKeyForApp(appName);
            item.SubItems[1].Text = duration.ToString(@"hh\:mm\:ss");
            item.SubItems[2].Text = $"{percent:0.0}%";

            if (item.Index != targetIndex)
            {
                _usageListView.Items.RemoveAt(item.Index);
                _usageListView.Items.Insert(targetIndex, item);
            }

            targetIndex++;
        }

        for (var index = _usageListView.Items.Count - 1; index >= 0; index--)
        {
            if (_usageListView.Items[index].Tag is not string appName || seenApps.Contains(appName))
            {
                continue;
            }

            _usageListView.Items.RemoveAt(index);
        }

        _usageListView.EndUpdate();
        RefreshDetailsPage();
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        _startButton.Enabled = !_isTracking;
        _stopButton.Enabled = _isTracking;
        _launchButton.Enabled = _usageListView.Items.Count > 0;
        _appPageButton.Enabled = _listPagePanel.Visible && _usageListView.SelectedItems.Count == 1;
        _detailsLaunchButton.Enabled = _detailsPagePanel.Visible
            && !string.IsNullOrWhiteSpace(_activeDetailsAppName)
            && !string.IsNullOrWhiteSpace(_tracker.GetKnownExecutablePath(_activeDetailsAppName));
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isTracking)
        {
            StopTracking();
        }

        PersistState();
    }

    private string GetIconKeyForApp(string appName)
    {
        if (_iconKeyByApp.TryGetValue(appName, out var iconKey))
        {
            return iconKey;
        }

        return _defaultIconKey;
    }

    private void EnsureAppIcon(string appName, int? processId, string? executablePath)
    {
        if (_iconKeyByApp.TryGetValue(appName, out var iconKey) && iconKey != _defaultIconKey)
        {
            return;
        }

        _iconKeyByApp[appName] = TryLoadProcessIcon(appName, processId, executablePath) ?? _defaultIconKey;
    }

    private string? TryLoadProcessIcon(string appName, int? processId, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) && processId is int actualProcessId)
        {
            try
            {
                using var process = Process.GetProcessById(actualProcessId);
                executablePath = process.MainModule?.FileName;
            }
            catch
            {
                executablePath = null;
            }
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        Icon? icon;
        try
        {
            icon = Icon.ExtractAssociatedIcon(executablePath);
        }
        catch
        {
            return null;
        }

        if (icon is null)
        {
            return null;
        }

        using (icon)
        {
            var iconKey = $"app:{appName}";
            if (!_appIcons.Images.ContainsKey(iconKey))
            {
                _appIcons.Images.Add(iconKey, icon.ToBitmap());
            }

            return iconKey;
        }
    }

    private void RestorePreviousState()
    {
        try
        {
            _tracker.LoadState(_sessionStatePath);
        }
        catch
        {
            // Ignore corrupted cache and continue with empty state.
        }
    }

    private void PersistState()
    {
        try
        {
            _tracker.SaveState(_sessionStatePath);
        }
        catch
        {
            // Ignore save errors to avoid blocking app close.
        }
    }

    private static string BuildSessionStatePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "tracker-state.json");
    }

    private static string BuildSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "settings.json");
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = new AppSettings
        {
            SamplingIntervalSeconds = Math.Clamp(settings.SamplingIntervalSeconds, 1, 30),
            AutosaveIntervalSeconds = Math.Clamp(settings.AutosaveIntervalSeconds, 5, 300),
            StartTrackingOnLaunch = settings.StartTrackingOnLaunch
        };

        _samplingTimer.Interval = _settings.SamplingIntervalSeconds * 1000;
    }

    private void OpenSettingsDialog()
    {
        using var settingsForm = new SettingsForm(_settings);
        if (settingsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ApplySettings(settingsForm.ResultSettings);
        _settingsStore.Save(_settingsPath, _settings);

        if (_isTracking)
        {
            _samplingTimer.Stop();
            _samplingTimer.Start();
        }
    }

    private void LaunchApp()
    {
        if (TryLaunchSelectedTrackedApp())
        {
            return;
        }

        using var openDialog = new OpenFileDialog
        {
            Title = "Choose app to launch",
            Filter = "Applications (*.exe)|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            CheckFileExists = true,
            Multiselect = false
        };

        if (openDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _ = TryLaunchExecutable(openDialog.FileName, appNameOverride: null);
    }

    private bool TryLaunchSelectedTrackedApp()
    {
        if (_usageListView.SelectedItems.Count == 0)
        {
            return false;
        }

        var selectedItem = _usageListView.SelectedItems[0];
        if (selectedItem.Tag is not string appName)
        {
            return false;
        }

        var path = _tracker.GetKnownExecutablePath(appName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        _ = TryLaunchExecutable(path, appName);
        return true;
    }

    private bool TryLaunchExecutable(string executablePath, string? appNameOverride)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            };
            Process.Start(startInfo);
            var appName = string.IsNullOrWhiteSpace(appNameOverride)
                ? Path.GetFileNameWithoutExtension(executablePath)
                : appNameOverride;
            _tracker.MarkLaunched(appName, DateTimeOffset.UtcNow, executablePath);
            PersistState();
            RefreshUsageList();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Cannot launch application:\n{ex.Message}",
                "Stride Tracker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private void OpenAppPage()
    {
        if (_usageListView.SelectedItems.Count != 1)
        {
            return;
        }

        var selected = _usageListView.SelectedItems[0];
        if (selected.Tag is not string appName)
        {
            return;
        }

        if (!_tracker.TryGetAppDetails(appName, out var details))
        {
            return;
        }

        _activeDetailsAppName = details.AppName;
        ShowDetailsPage();
        RefreshDetailsPage();
    }

    private void ShowDetailsPage()
    {
        _listPagePanel.Visible = false;
        _detailsPagePanel.Visible = true;
        UpdateUiState();
    }

    private void ShowListPage()
    {
        _detailsPagePanel.Visible = false;
        _listPagePanel.Visible = true;
        UpdateUiState();
    }

    private void RefreshDetailsPage()
    {
        if (!_detailsPagePanel.Visible || string.IsNullOrWhiteSpace(_activeDetailsAppName))
        {
            return;
        }

        if (!_tracker.TryGetAppDetails(_activeDetailsAppName, out var details))
        {
            _detailsAppNameValue.Text = _activeDetailsAppName;
            _detailsLastLaunchValue.Text = "Unknown";
            _detailsTimeSpentValue.Text = "00:00:00";
            UpdateUiState();
            return;
        }

        _detailsAppNameValue.Text = details.AppName;
        _detailsLastLaunchValue.Text = details.LastLaunchUtc?.ToLocalTime().ToString("g") ?? "Never";
        _detailsTimeSpentValue.Text = details.Duration.ToString(@"hh\:mm\:ss");
        UpdateUiState();
    }

    private void LaunchFromDetailsPage()
    {
        if (string.IsNullOrWhiteSpace(_activeDetailsAppName))
        {
            return;
        }

        var path = _tracker.GetKnownExecutablePath(_activeDetailsAppName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(
                this,
                "Executable path is unknown for this app.",
                "Stride Tracker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _ = TryLaunchExecutable(path, _activeDetailsAppName);
    }
}
