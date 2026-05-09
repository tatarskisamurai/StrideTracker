using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;
using StrideTracker.Configuration;
using StrideTracker.Tracking;

namespace StrideTracker.UI;

public sealed class MainForm : Form
{
    private readonly AppUsageTracker _tracker = new();
    private readonly ManualTaskTracker _taskTracker = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly System.Windows.Forms.Timer _samplingTimer = new();
    private readonly ImageList _icons = new();
    private readonly Dictionary<string, string> _iconKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> _heroIconCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly int _selfProcessId = Environment.ProcessId;
    private readonly string _sessionPath = BuildSessionPath();
    private readonly string _settingsPath = BuildSettingsPath();
    private readonly string _tasksPath = BuildTasksPath();

    private readonly UsageListView _appsList = new();
    private readonly TextBox _search = new();
    private readonly Label _sidebarHeader = new();
    private readonly Label _title = new();
    private readonly Label _appName = new();
    private readonly Label _appPath = new();
    private readonly Label _lastLaunch = new();
    private readonly Label _today = new();
    private readonly Label _week = new();
    private readonly Label _total = new();
    private readonly PictureBox _heroIcon = new();
    private readonly Panel _heroDivider = new();
    private readonly Label _todayLabel = new();
    private readonly Label _weekLabel = new();
    private readonly Label _totalLabel = new();
    private readonly FlowLayoutPanel _sessions = new();
    private readonly BufferedPanel _dailyChart = new();
    private readonly Label _dailyChartEmptyLabel = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Button _launch = new();
    private readonly Button _settings = new();
    private readonly Button _tasksView = new();

    private readonly Panel _appsPage = new();
    private readonly Panel _tasksPage = new();
    private readonly Panel _settingsPage = new();
    private readonly TreeView _tasksTree = new();
    private readonly Button _tasksBackButton = new();
    private readonly Label _tasksPageTitle = new();
    private readonly Label _activeTaskLabel = new();
    private readonly Label _taskSummaryLabel = new();
    private readonly Button _taskStartButton = new();
    private readonly Button _taskStopButton = new();
    private readonly Button _taskAddButton = new();
    private readonly Button _taskAddFolderButton = new();
    private readonly Button _taskRenameButton = new();
    private readonly Button _taskDeleteButton = new();
    private readonly Button _settingsBackButton = new();
    private readonly Label _settingsPageTitle = new();
    private readonly Label _settingsSamplingLabel = new();
    private readonly Label _settingsAutosaveLabel = new();
    private readonly Label _settingsLanguageLabel = new();
    private readonly Label _settingsThemeLabel = new();
    private readonly Label _settingsTrackingModeLabel = new();
    private readonly NumericUpDown _settingsSamplingInput = new();
    private readonly NumericUpDown _settingsAutosaveInput = new();
    private readonly ComboBox _settingsLanguageInput = new();
    private readonly ComboBox _settingsThemeInput = new();
    private readonly ComboBox _settingsTrackingModeInput = new();
    private readonly Label _settingsInstalledAppsLabel = new();
    private readonly TextBox _settingsInstalledAppsSearch = new();
    private readonly UsageListView _settingsInstalledAppsInput = new();
    private readonly CheckBox _settingsStartOnLaunchCheckBox = new();
    private readonly Button _settingsSaveButton = new();

    private AppSettings _appSettings;
    private bool _isTracking;
    private int _secondsSinceAutosave;
    private string? _selectedApp;
    private bool _tasksTreeInitialized;
    private bool _appsListInitialized;
    private bool _isRefreshingApps;
    private bool _isLoadingInstalledApps;
    private string _lastAppsFilter = string.Empty;
    private List<InstalledAppCandidate> _installedAppCandidates = new();
    private (string Label, TimeSpan Duration)[] _dailyChartRows = Array.Empty<(string Label, TimeSpan Duration)>();
    private const string ExplorerThemeName = "Explorer";
    private const string DarkExplorerThemeName = "DarkMode_Explorer";

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

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
        EnableAntiFlicker();
        BindEvents();
        ApplySettings(_appSettings);
        ApplyLanguage();
        RestoreState();
        RefreshApps(force: true);
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
        _sidebarHeader.Text = "Stride\nTRACKED APPS";
        _sidebarHeader.Dock = DockStyle.Top;
        _sidebarHeader.Height = 82;
        _sidebarHeader.ForeColor = Color.FromArgb(77, 184, 240);
        _sidebarHeader.Padding = new Padding(16, 14, 16, 8);
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
        _settings.Height = 36;
        _settings.FlatStyle = FlatStyle.Flat;
        _settings.FlatAppearance.BorderSize = 0;
        _settings.BackColor = Color.FromArgb(12, 24, 36);
        _settings.ForeColor = Color.FromArgb(230, 237, 243);
        _settings.TextAlign = ContentAlignment.MiddleLeft;
        _settings.Padding = new Padding(16, 0, 0, 0);

        _tasksView.Text = "Tasks";
        _tasksView.Dock = DockStyle.Bottom;
        _tasksView.Height = 36;
        _tasksView.FlatStyle = FlatStyle.Flat;
        _tasksView.FlatAppearance.BorderSize = 0;
        _tasksView.BackColor = Color.FromArgb(12, 24, 36);
        _tasksView.ForeColor = Color.FromArgb(230, 237, 243);
        _tasksView.TextAlign = ContentAlignment.MiddleLeft;
        _tasksView.Padding = new Padding(16, 0, 0, 0);

        sidebar.Controls.Add(_appsList);
        sidebar.Controls.Add(_search);
        sidebar.Controls.Add(_sidebarHeader);
        sidebar.Controls.Add(_tasksView);
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
        _stop.Text = "Stop";
        _stop.Width = 88;
        ApplyHeaderButtonStyle(_start);
        ApplyHeaderButtonStyle(_stop);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 210,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        actions.Controls.Add(_start);
        actions.Controls.Add(_stop);
        header.Controls.Add(_title);
        header.Controls.Add(actions);

        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
        var hero = new Panel { Width = 780, Height = 210, BackColor = Color.FromArgb(24, 40, 54), Padding = new Padding(16) };
        _heroIcon.Left = 20;
        _heroIcon.Top = 20;
        _heroIcon.Width = 56;
        _heroIcon.Height = 56;
        _heroIcon.SizeMode = PictureBoxSizeMode.Zoom;
        _heroIcon.BackColor = Color.FromArgb(32, 52, 70);

        _appName.Text = "No app selected";
        _appName.ForeColor = Color.FromArgb(230, 237, 243);
        _appName.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        _appName.AutoSize = true;
        _appName.Left = 92;
        _appName.Top = 22;
        _appPath.Left = 92;
        _appPath.Top = 48;
        _appPath.Width = 540;
        _appPath.ForeColor = Color.FromArgb(155, 178, 199);
        _lastLaunch.Left = 92;
        _lastLaunch.Top = 68;
        _lastLaunch.Width = 540;
        _lastLaunch.ForeColor = Color.FromArgb(155, 178, 199);
        _lastLaunch.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        _lastLaunch.Text = "Last launch: Never";

        _heroDivider.Left = 20;
        _heroDivider.Top = 98;
        _heroDivider.Width = 620;
        _heroDivider.Height = 1;
        _heroDivider.BackColor = Color.White;

        var statsGrid = new TableLayoutPanel
        {
            Left = 20,
            Top = 110,
            Width = 560,
            Height = 62,
            ColumnCount = 3,
            RowCount = 2
        };
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
        statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));

        _todayLabel.Text = "Today";
        _weekLabel.Text = "This Week";
        _totalLabel.Text = "Total";
        _todayLabel.ForeColor = _weekLabel.ForeColor = _totalLabel.ForeColor = Color.White;
        _todayLabel.Font = _weekLabel.Font = _totalLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _todayLabel.Dock = _weekLabel.Dock = _totalLabel.Dock = DockStyle.Fill;
        _todayLabel.TextAlign = _weekLabel.TextAlign = _totalLabel.TextAlign = ContentAlignment.MiddleLeft;

        _today.Text = "0h 00m";
        _week.Text = "0h 00m";
        _total.Text = "0h 00m";
        _today.ForeColor = _week.ForeColor = _total.ForeColor = Color.White;
        _today.Font = _week.Font = _total.Font = new Font("Consolas", 14F, FontStyle.Bold);
        _today.Dock = _week.Dock = _total.Dock = DockStyle.Fill;
        _today.TextAlign = _week.TextAlign = _total.TextAlign = ContentAlignment.MiddleLeft;

        statsGrid.Controls.Add(_todayLabel, 0, 0);
        statsGrid.Controls.Add(_weekLabel, 1, 0);
        statsGrid.Controls.Add(_totalLabel, 2, 0);
        statsGrid.Controls.Add(_today, 0, 1);
        statsGrid.Controls.Add(_week, 1, 1);
        statsGrid.Controls.Add(_total, 2, 1);

        _launch.Text = "Launch";
        _launch.Width = 100;
        _launch.Left = 660;
        _launch.Top = 86;
        _launch.BackColor = Color.FromArgb(46, 125, 50);
        _launch.ForeColor = Color.White;
        _launch.FlatStyle = FlatStyle.Flat;
        _launch.FlatAppearance.BorderSize = 0;
        hero.Controls.Add(_heroIcon);
        hero.Controls.Add(_appName);
        hero.Controls.Add(_appPath);
        hero.Controls.Add(_lastLaunch);
        hero.Controls.Add(_heroDivider);
        hero.Controls.Add(statsGrid);
        hero.Controls.Add(_launch);

        _sessions.Top = 230;
        _sessions.Width = 780;
        _sessions.Height = 260;
        _sessions.FlowDirection = FlowDirection.TopDown;
        _sessions.WrapContents = false;
        _dailyChartEmptyLabel.AutoSize = true;
        _dailyChartEmptyLabel.Margin = new Padding(3, 4, 3, 4);
        _dailyChart.Height = 210;
        _dailyChart.Margin = new Padding(0, 0, 0, 8);
        _dailyChart.Paint += (_, e) => PaintDailyChart(e.Graphics, _dailyChart.ClientRectangle);
        _sessions.SizeChanged += (_, _) =>
        {
            if (_dailyChartRows.Length == 0 || !_sessions.Controls.Contains(_dailyChart))
            {
                return;
            }

            _dailyChart.Width = Math.Max(680, _sessions.ClientSize.Width - 16);
            _dailyChart.Invalidate();
        };
        content.Controls.Add(hero);
        content.Controls.Add(_sessions);

        main.Controls.Add(header, 0, 0);
        main.Controls.Add(content, 0, 1);

        _appsPage.Dock = DockStyle.Fill;
        _appsPage.Controls.Add(main);

        BuildTasksPage();
        _tasksPage.Visible = false;
        BuildSettingsPage();
        _settingsPage.Visible = false;

        var mainHost = new Panel { Dock = DockStyle.Fill };
        mainHost.Controls.Add(_appsPage);
        mainHost.Controls.Add(_tasksPage);
        mainHost.Controls.Add(_settingsPage);

        root.Controls.Add(sidebar, 0, 0);
        root.Controls.Add(mainHost, 1, 0);
    }

    private void BindEvents()
    {
        _samplingTimer.Tick += OnSamplingTick;
        _appsList.SelectedIndexChanged += (_, _) =>
        {
            if (_isRefreshingApps)
            {
                return;
            }

            _selectedApp = _appsList.SelectedItems.Count > 0 ? _appsList.SelectedItems[0].Tag as string : null;
            RefreshDetails();
            UpdateUi();
        };
        _appsList.DoubleClick += (_, _) => LaunchCurrentOrPick();
        _search.TextChanged += (_, _) => RefreshApps(force: true);
        _start.Click += (_, _) => StartTracking();
        _stop.Click += (_, _) => StopTracking();
        _launch.Click += (_, _) => LaunchCurrentOrPick();
        _settings.Click += (_, _) => ShowSettingsPage();
        _tasksView.Click += (_, _) => ShowTasksPage();
        _settingsBackButton.Click += (_, _) => ShowAppsPage();
        _settingsSaveButton.Click += (_, _) => SaveSettingsFromPage();
        _taskStartButton.Click += (_, _) => StartSelectedTask();
        _taskStopButton.Click += (_, _) => StopTask();
        _taskAddButton.Click += (_, _) => AddTaskNode(isGroup: false);
        _taskAddFolderButton.Click += (_, _) => AddTaskNode(isGroup: true);
        _taskRenameButton.Click += (_, _) => RenameSelectedTask();
        _taskDeleteButton.Click += (_, _) => DeleteSelectedTask();
        _tasksTree.AfterSelect += (_, _) => UpdateTaskSummary();
        FormClosing += (_, _) =>
        {
            if (_isTracking) StopTracking();
            PersistState();
        };
        Shown += (_, _) => ApplyScrollbarsThemeRecursive(this);
    }

    private void BuildTasksPage()
    {
        _tasksPage.Dock = DockStyle.Fill;
        _tasksPage.BackColor = Color.FromArgb(14, 24, 34);

        var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.FromArgb(14, 24, 34), Padding = new Padding(16, 12, 16, 12) };
        _tasksBackButton.Text = "← Apps";
        _tasksBackButton.Width = 90;
        _tasksBackButton.Height = 30;
        _tasksBackButton.BackColor = Color.FromArgb(26, 44, 60);
        _tasksBackButton.ForeColor = Color.FromArgb(230, 237, 243);
        _tasksBackButton.FlatStyle = FlatStyle.Flat;
        _tasksBackButton.FlatAppearance.BorderSize = 0;
        _tasksBackButton.Click += (_, _) => ShowAppsPage();

        _tasksPageTitle.Text = "Task Tracker";
        _tasksPageTitle.ForeColor = Color.FromArgb(230, 237, 243);
        _tasksPageTitle.AutoSize = true;
        _tasksPageTitle.Left = 110;
        _tasksPageTitle.Top = 18;
        _tasksPageTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        header.Controls.Add(_tasksBackButton);
        header.Controls.Add(_tasksPageTitle);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(10, 20, 28)
        };
        const int tasksPanel1MinSize = 260;
        const int tasksPanel2MinSize = 300;

        void ApplySafeSplitterDistance()
        {
            var requiredMinWidth = tasksPanel1MinSize + tasksPanel2MinSize;
            if (body.Width <= requiredMinWidth)
            {
                if (body.Panel1MinSize != 0) body.Panel1MinSize = 0;
                if (body.Panel2MinSize != 0) body.Panel2MinSize = 0;
                return;
            }

            if (body.Panel1MinSize != tasksPanel1MinSize) body.Panel1MinSize = tasksPanel1MinSize;
            if (body.Panel2MinSize != tasksPanel2MinSize) body.Panel2MinSize = tasksPanel2MinSize;

            var maxPanel1Width = body.Width - body.Panel2MinSize;
            var target = Math.Clamp(360, body.Panel1MinSize, maxPanel1Width);
            body.SplitterDistance = target;
        }

        body.SizeChanged += (_, _) => ApplySafeSplitterDistance();
        body.HandleCreated += (_, _) => ApplySafeSplitterDistance();

        _tasksTree.Dock = DockStyle.Fill;
        _tasksTree.BackColor = Color.FromArgb(12, 24, 36);
        _tasksTree.ForeColor = Color.FromArgb(230, 237, 243);
        _tasksTree.BorderStyle = BorderStyle.None;
        body.Panel1.Controls.Add(_tasksTree);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Color.FromArgb(24, 40, 54), AutoScroll = true };
        _activeTaskLabel.Text = "Active: —";
        _activeTaskLabel.AutoSize = true;
        _activeTaskLabel.ForeColor = Color.FromArgb(230, 237, 243);
        _activeTaskLabel.Top = 12;
        _taskSummaryLabel.Text = "Select a task";
        _taskSummaryLabel.AutoSize = true;
        _taskSummaryLabel.MaximumSize = new Size(320, 0);
        _taskSummaryLabel.ForeColor = Color.FromArgb(155, 178, 199);
        _taskSummaryLabel.Top = 42;

        _taskStartButton.Text = "Start selected";
        _taskStartButton.Width = 140;
        _taskStartButton.BackColor = Color.FromArgb(46, 125, 50);
        _taskStartButton.ForeColor = Color.White;
        _taskStartButton.FlatStyle = FlatStyle.Flat;
        _taskStartButton.FlatAppearance.BorderSize = 0;

        _taskStopButton.Text = "Stop";
        _taskStopButton.Width = 100;
        _taskStopButton.BackColor = Color.FromArgb(183, 28, 28);
        _taskStopButton.ForeColor = Color.White;
        _taskStopButton.FlatStyle = FlatStyle.Flat;
        _taskStopButton.FlatAppearance.BorderSize = 0;

        _taskAddButton.Text = "+ Task";
        _taskAddButton.Width = 100;
        ApplyTaskButtonStyle(_taskAddButton);

        _taskAddFolderButton.Text = "+ Folder";
        _taskAddFolderButton.Width = 100;
        ApplyTaskButtonStyle(_taskAddFolderButton);

        _taskRenameButton.Text = "Rename";
        _taskRenameButton.Width = 100;
        ApplyTaskButtonStyle(_taskRenameButton);

        _taskDeleteButton.Text = "Delete";
        _taskDeleteButton.Width = 100;
        _taskDeleteButton.BackColor = Color.FromArgb(123, 31, 31);
        _taskDeleteButton.ForeColor = Color.White;
        _taskDeleteButton.FlatStyle = FlatStyle.Flat;
        _taskDeleteButton.FlatAppearance.BorderSize = 0;

        var taskButtons = new FlowLayoutPanel
        {
            Left = 0,
            Top = 86,
            Width = 330,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        taskButtons.Controls.Add(_taskStartButton);
        taskButtons.Controls.Add(_taskStopButton);

        var managementButtons = new FlowLayoutPanel
        {
            Left = 0,
            Top = 136,
            Width = 330,
            Height = 82,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        managementButtons.Controls.Add(_taskAddButton);
        managementButtons.Controls.Add(_taskAddFolderButton);
        managementButtons.Controls.Add(_taskRenameButton);
        managementButtons.Controls.Add(_taskDeleteButton);

        right.Controls.Add(_activeTaskLabel);
        right.Controls.Add(_taskSummaryLabel);
        right.Controls.Add(taskButtons);
        right.Controls.Add(managementButtons);
        body.Panel2.Controls.Add(right);

        _tasksPage.Controls.Add(body);
        _tasksPage.Controls.Add(header);
    }

    private void BuildSettingsPage()
    {
        _settingsPage.Dock = DockStyle.Fill;
        _settingsPage.BackColor = Color.FromArgb(14, 24, 34);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = Color.FromArgb(14, 24, 34),
            Padding = new Padding(16, 12, 16, 12)
        };

        _settingsBackButton.Width = 90;
        _settingsBackButton.Height = 30;
        _settingsBackButton.BackColor = Color.FromArgb(26, 44, 60);
        _settingsBackButton.ForeColor = Color.FromArgb(230, 237, 243);
        _settingsBackButton.FlatStyle = FlatStyle.Flat;
        _settingsBackButton.FlatAppearance.BorderSize = 0;

        _settingsPageTitle.ForeColor = Color.FromArgb(230, 237, 243);
        _settingsPageTitle.AutoSize = true;
        _settingsPageTitle.Left = 110;
        _settingsPageTitle.Top = 18;
        _settingsPageTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);

        header.Controls.Add(_settingsBackButton);
        header.Controls.Add(_settingsPageTitle);

        var contentWrap = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            AutoScroll = true,
            BackColor = Color.FromArgb(10, 20, 28)
        };

        var card = new Panel
        {
            Width = 760,
            Height = 700,
            BackColor = Color.FromArgb(24, 40, 54),
            Padding = new Padding(18)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 288,
            ColumnCount = 2,
            RowCount = 6
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        ConfigureSettingsLabel(_settingsSamplingLabel);
        ConfigureSettingsLabel(_settingsAutosaveLabel);
        ConfigureSettingsLabel(_settingsLanguageLabel);
        ConfigureSettingsLabel(_settingsThemeLabel);
        ConfigureSettingsLabel(_settingsTrackingModeLabel);
        ConfigureSettingsLabel(_settingsInstalledAppsLabel);

        _settingsSamplingInput.Minimum = 1;
        _settingsSamplingInput.Maximum = 30;
        _settingsSamplingInput.Dock = DockStyle.Fill;

        _settingsAutosaveInput.Minimum = 5;
        _settingsAutosaveInput.Maximum = 300;
        _settingsAutosaveInput.Dock = DockStyle.Fill;

        _settingsLanguageInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _settingsLanguageInput.Dock = DockStyle.Fill;
        _settingsLanguageInput.DisplayMember = nameof(LanguageOption.Label);
        _settingsLanguageInput.ValueMember = nameof(LanguageOption.Code);
        _settingsLanguageInput.Items.Add(new LanguageOption("ru", "Русский"));
        _settingsLanguageInput.Items.Add(new LanguageOption("en", "English"));

        _settingsThemeInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _settingsThemeInput.Dock = DockStyle.Fill;
        _settingsThemeInput.DisplayMember = nameof(ThemeOption.Label);
        _settingsThemeInput.ValueMember = nameof(ThemeOption.Code);
        _settingsThemeInput.Items.Add(new ThemeOption("dark", T("Темная", "Dark")));
        _settingsThemeInput.Items.Add(new ThemeOption("light", T("Светло-голубая", "Light Blue")));

        _settingsTrackingModeInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _settingsTrackingModeInput.Dock = DockStyle.Fill;
        _settingsTrackingModeInput.DisplayMember = nameof(TrackingModeOption.Label);
        _settingsTrackingModeInput.ValueMember = nameof(TrackingModeOption.Code);

        _settingsInstalledAppsSearch.Dock = DockStyle.Top;
        _settingsInstalledAppsSearch.Height = 28;
        _settingsInstalledAppsSearch.Margin = new Padding(0, 0, 0, 6);


        _settingsInstalledAppsInput.Dock = DockStyle.Fill;
        _settingsInstalledAppsInput.View = View.Details;
        _settingsInstalledAppsInput.HeaderStyle = ColumnHeaderStyle.None;
        _settingsInstalledAppsInput.CheckBoxes = true;
        _settingsInstalledAppsInput.FullRowSelect = true;
        _settingsInstalledAppsInput.MultiSelect = false;
        _settingsInstalledAppsInput.SmallImageList = _icons;
        _settingsInstalledAppsInput.Columns.Add("InstalledApp", 240);

        _settingsStartOnLaunchCheckBox.ForeColor = Color.FromArgb(230, 237, 243);
        _settingsStartOnLaunchCheckBox.AutoSize = true;
        _settingsStartOnLaunchCheckBox.Dock = DockStyle.Fill;

        _settingsTrackingModeInput.SelectedIndexChanged += (_, _) =>
        {
            UpdateTrackedAppsSelectorState();
        };
        _settingsInstalledAppsInput.SizeChanged += (_, _) =>
        {
            if (_settingsInstalledAppsInput.Columns.Count > 0)
            {
                _settingsInstalledAppsInput.Columns[0].Width = Math.Max(120, _settingsInstalledAppsInput.ClientSize.Width - 6);
            }
        };
        _settingsInstalledAppsSearch.TextChanged += (_, _) => RefreshInstalledAppsList();
        _settingsInstalledAppsInput.DoubleClick += (_, _) =>
        {
            if (_settingsInstalledAppsInput.SelectedItems.Count == 0)
            {
                return;
            }

            var item = _settingsInstalledAppsInput.SelectedItems[0];
            item.Checked = !item.Checked;
        };

        grid.Controls.Add(_settingsSamplingLabel, 0, 0);
        grid.Controls.Add(_settingsSamplingInput, 1, 0);
        grid.Controls.Add(_settingsAutosaveLabel, 0, 1);
        grid.Controls.Add(_settingsAutosaveInput, 1, 1);
        grid.Controls.Add(_settingsLanguageLabel, 0, 2);
        grid.Controls.Add(_settingsLanguageInput, 1, 2);
        grid.Controls.Add(_settingsThemeLabel, 0, 3);
        grid.Controls.Add(_settingsThemeInput, 1, 3);
        grid.Controls.Add(_settingsTrackingModeLabel, 0, 4);
        grid.Controls.Add(_settingsTrackingModeInput, 1, 4);
        grid.Controls.Add(_settingsStartOnLaunchCheckBox, 0, 5);
        grid.SetColumnSpan(_settingsStartOnLaunchCheckBox, 2);

        var trackedAppsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            AutoScroll = true
        };

        var installedHeader = new Panel { Dock = DockStyle.Top, Height = 24 };
        _settingsInstalledAppsLabel.Dock = DockStyle.Top;
        _settingsInstalledAppsLabel.Height = 24;
        installedHeader.Controls.Add(_settingsInstalledAppsLabel);

        var installedListHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        installedListHost.Controls.Add(_settingsInstalledAppsInput);
        installedListHost.Controls.Add(_settingsInstalledAppsSearch);
        installedListHost.Controls.Add(installedHeader);
        trackedAppsPanel.Controls.Add(installedListHost);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        _settingsSaveButton.Width = 120;
        _settingsSaveButton.Height = 34;
        _settingsSaveButton.BackColor = Color.FromArgb(46, 125, 50);
        _settingsSaveButton.ForeColor = Color.White;
        _settingsSaveButton.FlatStyle = FlatStyle.Flat;
        _settingsSaveButton.FlatAppearance.BorderSize = 0;
        actions.Controls.Add(_settingsSaveButton);

        card.Controls.Add(actions);
        card.Controls.Add(trackedAppsPanel);
        card.Controls.Add(grid);
        contentWrap.Controls.Add(card);

        _settingsPage.Controls.Add(contentWrap);
        _settingsPage.Controls.Add(header);
    }

    private void OnSamplingTick(object? sender, EventArgs e)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        _taskTracker.Tick(nowUtc);

        var sample = AppUsageTracker.TryGetActiveWindowInfo();
        if (sample is null) return;
        if (sample.ProcessId == _selfProcessId)
        {
            _tracker.Flush(sample.CapturedAtUtc);
            RefreshApps();
            return;
        }

        if (!ShouldTrackApp(sample.ProcessName))
        {
            // Remember discovered apps even when they are not selected for tracking yet.
            _tracker.MarkLaunched(sample.ProcessName, sample.CapturedAtUtc, sample.ExecutablePath);
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
        RefreshApps(force: true);
        UpdateUi();
    }

    private void RefreshApps(bool force = false)
    {
        var selected = _selectedApp;
        var q = _search.Text.Trim();
        var durations = _tracker.DurationsByApp
            .Where(x => ShouldTrackApp(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        // In "selected only" mode show chosen apps in sidebar even before first sample.
        if (!IsTrackAllMode)
        {
            foreach (var appName in _appSettings.SelectedTrackedApps)
            {
                if (string.IsNullOrWhiteSpace(appName) || durations.ContainsKey(appName))
                {
                    continue;
                }

                durations[appName] = TimeSpan.Zero;
            }
        }

        var data = durations
            .Where(x => string.IsNullOrWhiteSpace(q) || x.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var filterChanged = !string.Equals(_lastAppsFilter, q, StringComparison.Ordinal);
        var shouldRebuildList = force || filterChanged || !_appsListInitialized;

        _isRefreshingApps = true;
        _appsList.BeginUpdate();
        if (shouldRebuildList)
        {
            _appsList.Items.Clear();
            foreach (var (name, span) in data)
            {
                EnsureIcon(name, null, _tracker.GetKnownExecutablePath(name));
                var item = new ListViewItem(name) { Tag = name, ImageKey = GetIconKey(name) };
                item.SubItems.Add(FormatDuration(span));
                _appsList.Items.Add(item);
            }
        }
        else
        {
            var existingByName = _appsList.Items
                .Cast<ListViewItem>()
                .Where(i => i.Tag is string)
                .ToDictionary(i => (string)i.Tag!, StringComparer.OrdinalIgnoreCase);

            foreach (var (name, span) in data)
            {
                EnsureIcon(name, null, _tracker.GetKnownExecutablePath(name));
                if (!existingByName.TryGetValue(name, out var item))
                {
                    item = new ListViewItem(name) { Tag = name, ImageKey = GetIconKey(name) };
                    item.SubItems.Add(FormatDuration(span));
                    _appsList.Items.Add(item);
                    continue;
                }

                item.ImageKey = GetIconKey(name);
                var todayText = FormatDuration(span);
                if (item.SubItems.Count < 2)
                {
                    item.SubItems.Add(todayText);
                }
                else if (!string.Equals(item.SubItems[1].Text, todayText, StringComparison.Ordinal))
                {
                    item.SubItems[1].Text = todayText;
                }
            }
        }

        var shouldRestoreSelection = shouldRebuildList && !(_appsList.Focused || _appsList.Capture);
        if (!string.IsNullOrWhiteSpace(selected) && shouldRestoreSelection)
        {
            var item = _appsList.Items.Cast<ListViewItem>().FirstOrDefault(i => string.Equals(i.Tag as string, selected, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                item.Selected = true;
                _appsListInitialized = true;
            }
        }

        if (!_appsListInitialized && shouldRestoreSelection && _appsList.SelectedItems.Count == 0 && _appsList.Items.Count > 0)
        {
            _appsList.Items[0].Selected = true;
            _appsListInitialized = true;
        }

        _appsList.EndUpdate();
        _isRefreshingApps = false;
        _selectedApp = _appsList.SelectedItems.Count > 0 ? _appsList.SelectedItems[0].Tag as string : null;
        _lastAppsFilter = q;

        if (_appsPage.Visible || force)
        {
            RefreshDetails();
        }
        if (_tasksPage.Visible)
        {
            RefreshTasksTree();
        }
        UpdateUi();
    }

    private void RefreshTasksTree()
    {
        var selectedId = _tasksTree.SelectedNode?.Name;
        var topNodeId = _tasksTree.TopNode?.Name;
        var expandedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpandedNodeIds(_tasksTree.Nodes, expandedIds);

        _tasksTree.BeginUpdate();
        _tasksTree.Nodes.Clear();

        foreach (var rootNode in _taskTracker.GetChildren(null))
        {
            _tasksTree.Nodes.Add(BuildTaskTreeNode(rootNode));
        }

        if (!_tasksTreeInitialized)
        {
            _tasksTree.ExpandAll();
            _tasksTreeInitialized = true;
        }
        else
        {
            RestoreExpandedNodeState(_tasksTree.Nodes, expandedIds);
        }

        _tasksTree.EndUpdate();

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var match = FindTreeNodeById(_tasksTree.Nodes, selectedId);
            if (match is not null)
            {
                _tasksTree.SelectedNode = match;
            }
        }

        if (!string.IsNullOrWhiteSpace(topNodeId))
        {
            var topNode = FindTreeNodeById(_tasksTree.Nodes, topNodeId);
            if (topNode is not null)
            {
                _tasksTree.TopNode = topNode;
            }
        }

        UpdateTaskSummary();
    }

    private TreeNode BuildTaskTreeNode(ManualTaskTracker.TaskNode node)
    {
        var icon = node.IsGroup ? "📁" : "🎯";
        var total = FormatDuration(_taskTracker.GetTotalDuration(node.Id));
        var treeNode = new TreeNode($"{icon} {node.Name} ({total})")
        {
            Name = node.Id
        };

        foreach (var child in _taskTracker.GetChildren(node.Id))
        {
            treeNode.Nodes.Add(BuildTaskTreeNode(child));
        }

        return treeNode;
    }

    private static TreeNode? FindTreeNodeById(TreeNodeCollection nodes, string id)
    {
        foreach (TreeNode node in nodes)
        {
            if (string.Equals(node.Name, id, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var child = FindTreeNodeById(node.Nodes, id);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static void CollectExpandedNodeIds(TreeNodeCollection nodes, ISet<string> expandedIds)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.IsExpanded)
            {
                expandedIds.Add(node.Name);
            }

            if (node.Nodes.Count > 0)
            {
                CollectExpandedNodeIds(node.Nodes, expandedIds);
            }
        }
    }

    private static void RestoreExpandedNodeState(TreeNodeCollection nodes, ISet<string> expandedIds)
    {
        foreach (TreeNode node in nodes)
        {
            if (expandedIds.Contains(node.Name))
            {
                node.Expand();
            }

            if (node.Nodes.Count > 0)
            {
                RestoreExpandedNodeState(node.Nodes, expandedIds);
            }
        }
    }

    private void RefreshDetails()
    {
        if (string.IsNullOrWhiteSpace(_selectedApp) || !_tracker.TryGetAppDetails(_selectedApp, out var d))
        {
            _title.Text = T("Выберите приложение", "Select an app");
            _appName.Text = T("Приложение не выбрано", "No app selected");
            _appPath.Text = "-";
            _lastLaunch.Text = $"{T("Последний запуск", "Last launch")}: {T("Никогда", "Never")}";
            _today.Text = "0h 00m";
            _week.Text = "0h 00m";
            _total.Text = "0h 00m";
            _heroIcon.Image = GetHeroIconImage("default", null);
            RenderDailyChart(Array.Empty<(string Label, TimeSpan Duration)>());
            return;
        }

        _title.Text = d.AppName;
        _appName.Text = d.AppName;
        _appPath.Text = d.ExecutablePath ?? T("Путь неизвестен", "Unknown path");
        _lastLaunch.Text = $"{T("Последний запуск", "Last launch")}: {d.LastLaunchUtc?.ToLocalTime().ToString("g") ?? T("Никогда", "Never")}";
        var todayValue = FormatDuration(d.TodayDuration);
        var weekValue = FormatDuration(d.WeekDuration);
        var totalValue = FormatDuration(d.TotalDuration);
        _today.Text = todayValue;
        _week.Text = weekValue;
        _total.Text = totalValue;
        _heroIcon.Image = GetHeroIconImage(d.AppName, d.ExecutablePath);
        RenderDailyChart(BuildSessionRows(d.RecentDailyDurations));
    }

    private void ShowTasksPage()
    {
        _appsPage.Visible = false;
        _settingsPage.Visible = false;
        _tasksPage.Visible = true;
        RefreshTasksTree();
    }

    private void ShowAppsPage()
    {
        _tasksPage.Visible = false;
        _settingsPage.Visible = false;
        _appsPage.Visible = true;
    }

    private void ShowSettingsPage()
    {
        LoadSettingsToPage();
        _appsPage.Visible = false;
        _tasksPage.Visible = false;
        _settingsPage.Visible = true;
    }

    private void UpdateTaskSummary()
    {
        var active = _taskTracker.ActiveNodeId;
        if (!string.IsNullOrWhiteSpace(active))
        {
            var activeNode = _taskTracker.GetNode(active);
            _activeTaskLabel.Text = $"{T("Активная", "Active")}: {activeNode?.Name ?? active}";
        }
        else
        {
            _activeTaskLabel.Text = $"{T("Активная", "Active")}: —";
        }

        if (_tasksTree.SelectedNode is null)
        {
            _taskSummaryLabel.Text = T("Выберите задачу", "Select a task");
            return;
        }

        var selectedId = _tasksTree.SelectedNode.Name;
        var node = _taskTracker.GetNode(selectedId);
        if (node is null)
        {
            _taskSummaryLabel.Text = T("Выберите задачу", "Select a task");
            return;
        }

        var own = FormatDuration(_taskTracker.GetOwnDuration(selectedId));
        var total = FormatDuration(_taskTracker.GetTotalDuration(selectedId));
        _taskSummaryLabel.Text = $"{T("Выбрано", "Selected")}: {node.Name}\n{T("Личное", "Own")}: {own} | {T("Всего", "Total")}: {total}";
    }

    private void StartSelectedTask()
    {
        if (_tasksTree.SelectedNode is null)
        {
            return;
        }

        _taskTracker.Start(_tasksTree.SelectedNode.Name, DateTimeOffset.UtcNow);
        UpdateTaskSummary();
        RefreshTasksTree();
        PersistState();
    }

    private void StopTask()
    {
        _taskTracker.Stop(DateTimeOffset.UtcNow);
        UpdateTaskSummary();
        RefreshTasksTree();
        PersistState();
    }

    private void AddTaskNode(bool isGroup)
    {
        var parentId = GetParentIdForCreate();
        var promptTitle = isGroup ? T("Создать папку", "Create folder") : T("Создать задачу", "Create task");
        if (!TryPromptTaskName(promptTitle, string.Empty, out var name))
        {
            return;
        }

        var created = _taskTracker.CreateNode(name, parentId, isGroup);
        if (created is null)
        {
            MessageBox.Show(this, T("Не удалось создать задачу.", "Cannot create task node."), "Task Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RefreshTasksTree();
        SelectTaskNode(created.Id);
        PersistState();
    }

    private void RenameSelectedTask()
    {
        if (_tasksTree.SelectedNode is null)
        {
            MessageBox.Show(this, T("Сначала выберите задачу.", "Select a task first."), "Task Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedId = _tasksTree.SelectedNode.Name;
        var node = _taskTracker.GetNode(selectedId);
        if (node is null)
        {
            return;
        }

        if (!TryPromptTaskName(T("Переименовать задачу", "Rename task"), node.Name, out var newName))
        {
            return;
        }

        if (!_taskTracker.RenameNode(selectedId, newName))
        {
            MessageBox.Show(this, T("Не удалось переименовать задачу.", "Cannot rename task."), "Task Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RefreshTasksTree();
        SelectTaskNode(selectedId);
        PersistState();
    }

    private void DeleteSelectedTask()
    {
        if (_tasksTree.SelectedNode is null)
        {
            MessageBox.Show(this, T("Сначала выберите задачу.", "Select a task first."), "Task Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedId = _tasksTree.SelectedNode.Name;
        var node = _taskTracker.GetNode(selectedId);
        if (node is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            T($"Удалить \"{node.Name}\" и все подзадачи?", $"Delete \"{node.Name}\" and all subtasks?"),
            "Task Tracker",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        if (!_taskTracker.DeleteNode(selectedId))
        {
            MessageBox.Show(this, T("Не удалось удалить задачу.", "Cannot delete task."), "Task Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RefreshTasksTree();
        PersistState();
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
            RefreshApps(force: true);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{T("Не удалось запустить приложение", "Cannot launch application")}:\n{ex.Message}", "Stride Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void LoadSettingsToPage()
    {
        _settingsSamplingInput.Value = Math.Clamp(_appSettings.SamplingIntervalSeconds, (int)_settingsSamplingInput.Minimum, (int)_settingsSamplingInput.Maximum);
        _settingsAutosaveInput.Value = Math.Clamp(_appSettings.AutosaveIntervalSeconds, (int)_settingsAutosaveInput.Minimum, (int)_settingsAutosaveInput.Maximum);
        _settingsStartOnLaunchCheckBox.Checked = _appSettings.StartTrackingOnLaunch;
        SelectSettingsLanguage(_appSettings.Language);
        SelectSettingsTheme(_appSettings.Theme);
        PopulateTrackingModeOptions();
        SelectTrackingMode(_appSettings.TrackingMode);
        EnsureInstalledAppsLoaded();
        RefreshInstalledAppsList();
        UpdateTrackedAppsSelectorState();
    }

    private void SaveSettingsFromPage()
    {
        var newSettings = new AppSettings
        {
            SamplingIntervalSeconds = (int)_settingsSamplingInput.Value,
            AutosaveIntervalSeconds = (int)_settingsAutosaveInput.Value,
            StartTrackingOnLaunch = _settingsStartOnLaunchCheckBox.Checked,
            Language = (_settingsLanguageInput.SelectedItem as LanguageOption)?.Code ?? "ru",
            Theme = (_settingsThemeInput.SelectedItem as ThemeOption)?.Code ?? "dark",
            TrackingMode = (_settingsTrackingModeInput.SelectedItem as TrackingModeOption)?.Code ?? "all",
            SelectedTrackedApps = GetCheckedInstalledAppNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        ApplySettings(newSettings);
        _settingsStore.Save(_settingsPath, _appSettings);

        if (_isTracking)
        {
            _samplingTimer.Stop();
            _samplingTimer.Start();
        }

        RefreshApps(force: true);
        RefreshDetails();
        MessageBox.Show(
            this,
            T("Настройки сохранены.", "Settings saved."),
            "Stride Tracker",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ApplySettings(AppSettings settings)
    {
        _appSettings = new AppSettings
        {
            SamplingIntervalSeconds = Math.Clamp(settings.SamplingIntervalSeconds, 1, 30),
            AutosaveIntervalSeconds = Math.Clamp(settings.AutosaveIntervalSeconds, 5, 300),
            StartTrackingOnLaunch = settings.StartTrackingOnLaunch,
            Language = string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru",
            Theme = string.Equals(settings.Theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark",
            TrackingMode = string.Equals(settings.TrackingMode, "selected", StringComparison.OrdinalIgnoreCase) ? "selected" : "all",
            SelectedTrackedApps = (settings.SelectedTrackedApps ?? new List<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        _samplingTimer.Interval = _appSettings.SamplingIntervalSeconds * 1000;
        ApplyTheme();
        ApplyLanguage();
        LoadSettingsToPage();
        RefreshDetails();
        RefreshTasksTree();
    }

    private void ApplyLanguage()
    {
        _sidebarHeader.Text = "Stride\nTRACKED APPS";
        _search.PlaceholderText = T("Фильтр приложений...", "Filter apps...");
        _settings.Text = T("Настройки", "Settings");
        _tasksView.Text = T("Задачи", "Tasks");
        _start.Text = T("Старт", "Start");
        _stop.Text = T("Стоп", "Stop");
        _launch.Text = T("Запустить", "Launch");
        _todayLabel.Text = T("Сегодня", "Today");
        _weekLabel.Text = T("Неделя", "This Week");
        _totalLabel.Text = T("Всего", "Total");
        _tasksBackButton.Text = T("← Приложения", "← Apps");
        _tasksPageTitle.Text = T("Трекер задач", "Task Tracker");
        _taskStartButton.Text = T("Старт выбранной", "Start selected");
        _taskStopButton.Text = T("Стоп", "Stop");
        _taskAddButton.Text = T("+ Задача", "+ Task");
        _taskAddFolderButton.Text = T("+ Папка", "+ Folder");
        _taskRenameButton.Text = T("Переименовать", "Rename");
        _taskDeleteButton.Text = T("Удалить", "Delete");
        _settingsBackButton.Text = T("← Приложения", "← Apps");
        _settingsPageTitle.Text = T("Настройки", "Settings");
        _settingsSamplingLabel.Text = T("Интервал сэмплинга (сек):", "Sampling interval (seconds):");
        _settingsAutosaveLabel.Text = T("Интервал автосохранения (сек):", "Autosave interval (seconds):");
        _settingsLanguageLabel.Text = T("Язык интерфейса:", "Interface language:");
        _settingsThemeLabel.Text = T("Тема:", "Theme:");
        _settingsTrackingModeLabel.Text = T("Режим трекинга приложений:", "App tracking mode:");
        _settingsInstalledAppsLabel.Text = T("Установленные приложения (отмечайте галочками):", "Installed applications (check to track):");
        _settingsInstalledAppsSearch.PlaceholderText = T("Поиск по установленным приложениям...", "Search installed applications...");
        _settingsStartOnLaunchCheckBox.Text = T("Запускать трекинг автоматически при старте", "Start tracking automatically on launch");
        _settingsSaveButton.Text = T("Сохранить", "Save");
        PopulateThemeOptions();
        SelectSettingsTheme(_appSettings.Theme);
        PopulateTrackingModeOptions();
        SelectTrackingMode(_appSettings.TrackingMode);
    }

    private void ApplyTheme()
    {
        var page = IsLightTheme ? Color.FromArgb(223, 241, 252) : Color.FromArgb(10, 20, 28);
        var sidebar = IsLightTheme ? Color.FromArgb(197, 226, 245) : Color.FromArgb(12, 24, 36);
        var surface = IsLightTheme ? Color.White : Color.FromArgb(24, 40, 54);
        var surfaceAlt = IsLightTheme ? Color.FromArgb(236, 247, 255) : Color.FromArgb(14, 24, 34);
        var input = IsLightTheme ? Color.White : Color.FromArgb(12, 24, 36);
        var text = IsLightTheme ? Color.FromArgb(26, 57, 82) : Color.FromArgb(230, 237, 243);
        var mutedText = IsLightTheme ? Color.FromArgb(78, 114, 143) : Color.FromArgb(155, 178, 199);
        var accent = IsLightTheme ? Color.FromArgb(42, 127, 184) : Color.FromArgb(77, 184, 240);
        var border = IsLightTheme ? Color.FromArgb(182, 212, 233) : Color.FromArgb(44, 66, 84);

        BackColor = page;
        _appsPage.BackColor = page;
        _tasksPage.BackColor = page;
        _settingsPage.BackColor = page;

        _sidebarHeader.ForeColor = accent;
        _search.BackColor = input;
        _search.ForeColor = text;

        _appsList.BackColor = sidebar;
        _appsList.ForeColor = text;

        _title.ForeColor = text;
        _appName.ForeColor = text;
        _todayLabel.ForeColor = text;
        _weekLabel.ForeColor = text;
        _totalLabel.ForeColor = text;
        _today.ForeColor = text;
        _week.ForeColor = text;
        _total.ForeColor = text;
        _heroDivider.BackColor = border;
        _heroIcon.BackColor = surfaceAlt;

        _tasksTree.BackColor = sidebar;
        _tasksTree.ForeColor = text;
        _activeTaskLabel.ForeColor = text;

        _settingsSamplingLabel.ForeColor = text;
        _settingsAutosaveLabel.ForeColor = text;
        _settingsLanguageLabel.ForeColor = text;
        _settingsThemeLabel.ForeColor = text;
        _settingsTrackingModeLabel.ForeColor = text;
        _settingsStartOnLaunchCheckBox.ForeColor = text;
        _settingsSamplingInput.BackColor = input;
        _settingsSamplingInput.ForeColor = text;
        _settingsAutosaveInput.BackColor = input;
        _settingsAutosaveInput.ForeColor = text;
        _settingsLanguageInput.BackColor = input;
        _settingsLanguageInput.ForeColor = text;
        _settingsThemeInput.BackColor = input;
        _settingsThemeInput.ForeColor = text;
        _settingsTrackingModeInput.BackColor = input;
        _settingsTrackingModeInput.ForeColor = text;
        _settingsInstalledAppsLabel.ForeColor = text;
        _settingsInstalledAppsSearch.BackColor = input;
        _settingsInstalledAppsSearch.ForeColor = text;
        _settingsInstalledAppsInput.BackColor = input;
        _settingsInstalledAppsInput.ForeColor = text;

        ApplyControlThemeRecursive(this, page, sidebar, surface, surfaceAlt, text);

        ApplyHeaderButtonStyle(_start);
        ApplyHeaderButtonStyle(_stop);
        ApplyHeaderButtonStyle(_tasksBackButton);
        ApplyHeaderButtonStyle(_settingsBackButton);
        ApplyTaskButtonStyle(_taskAddButton);
        ApplyTaskButtonStyle(_taskAddFolderButton);
        ApplyTaskButtonStyle(_taskRenameButton);

        _settings.BackColor = sidebar;
        _settings.ForeColor = text;
        _tasksView.BackColor = sidebar;
        _tasksView.ForeColor = text;
        _taskStartButton.BackColor = Color.FromArgb(46, 125, 50);
        _taskStartButton.ForeColor = Color.White;
        _taskStopButton.BackColor = Color.FromArgb(183, 28, 28);
        _taskStopButton.ForeColor = Color.White;
        _taskDeleteButton.BackColor = Color.FromArgb(123, 31, 31);
        _taskDeleteButton.ForeColor = Color.White;
        _launch.BackColor = Color.FromArgb(46, 125, 50);
        _launch.ForeColor = Color.White;
        _settingsSaveButton.BackColor = Color.FromArgb(46, 125, 50);
        _settingsSaveButton.ForeColor = Color.White;

        _appPath.ForeColor = mutedText;
        _lastLaunch.ForeColor = mutedText;
        _taskSummaryLabel.ForeColor = mutedText;
        _tasksPageTitle.ForeColor = text;
        _settingsPageTitle.ForeColor = text;
        ApplyTrackingButtonsState();
        _dailyChart.BackColor = IsLightTheme ? Color.FromArgb(239, 248, 255) : Color.FromArgb(16, 29, 40);
        _dailyChart.Invalidate();
        ApplyScrollbarsThemeRecursive(this);
    }

    private static void ApplyControlThemeRecursive(Control root, Color page, Color sidebar, Color surface, Color surfaceAlt, Color text)
    {
        foreach (Control child in root.Controls)
        {
            switch (child)
            {
                case SplitContainer:
                    child.BackColor = page;
                    break;
                case TableLayoutPanel:
                case FlowLayoutPanel:
                    child.BackColor = Color.Transparent;
                    break;
                case Panel:
                    child.BackColor = child.Height <= 60 ? surfaceAlt : surface;
                    break;
                case Label label:
                    if (label.ForeColor.A > 0)
                    {
                        label.ForeColor = text;
                    }
                    break;
            }

            ApplyControlThemeRecursive(child, page, sidebar, surface, surfaceAlt, text);
        }
    }

    private void ApplyScrollbarsThemeRecursive(Control root)
    {
        ApplyScrollbarTheme(root);
        foreach (Control child in root.Controls)
        {
            ApplyScrollbarsThemeRecursive(child);
        }
    }

    private void ApplyScrollbarTheme(Control control)
    {
        if (!control.IsHandleCreated || !NeedsScrollbarTheme(control))
        {
            return;
        }

        var themeName = IsLightTheme ? ExplorerThemeName : DarkExplorerThemeName;
        _ = SetWindowTheme(control.Handle, themeName, null);
    }

    private static bool NeedsScrollbarTheme(Control control) =>
        control is ListView
        || control is TreeView
        || (control is ScrollableControl scrollable && scrollable.AutoScroll);

    private string T(string ru, string en) => IsRussian ? ru : en;

    private bool IsRussian => !string.Equals(_appSettings.Language, "en", StringComparison.OrdinalIgnoreCase);
    private bool IsLightTheme => string.Equals(_appSettings.Theme, "light", StringComparison.OrdinalIgnoreCase);
    private bool IsTrackAllMode => !string.Equals(_appSettings.TrackingMode, "selected", StringComparison.OrdinalIgnoreCase);

    private sealed record LanguageOption(string Code, string Label);
    private sealed record ThemeOption(string Code, string Label);
    private sealed record TrackingModeOption(string Code, string Label);

    private void UpdateUi()
    {
        _start.Enabled = true;
        _stop.Enabled = true;
        _launch.Enabled = _appsList.Items.Count > 0;
        ApplyTrackingButtonsState();
    }

    private void ApplyTrackingButtonsState()
    {
        var activeFore = Color.White;
        var startActiveBack = Color.FromArgb(46, 125, 50);
        var stopActiveBack = Color.FromArgb(183, 28, 28);
        var activeBorder = Color.FromArgb(0, 0, 0);

        var inactiveBack = IsLightTheme ? Color.FromArgb(235, 243, 250) : Color.FromArgb(26, 44, 60);
        var inactiveFore = IsLightTheme ? Color.FromArgb(104, 128, 146) : Color.FromArgb(155, 178, 199);
        var inactiveBorder = IsLightTheme ? Color.FromArgb(192, 213, 230) : Color.FromArgb(44, 66, 84);

        ApplyTrackingButtonState(_start, !_isTracking, startActiveBack, activeFore, activeBorder, inactiveBack, inactiveFore, inactiveBorder);
        ApplyTrackingButtonState(_stop, _isTracking, stopActiveBack, activeFore, activeBorder, inactiveBack, inactiveFore, inactiveBorder);
    }

    private static void ApplyTrackingButtonState(
        Button button,
        bool isActive,
        Color activeBack,
        Color activeFore,
        Color activeBorder,
        Color inactiveBack,
        Color inactiveFore,
        Color inactiveBorder)
    {
        if (isActive)
        {
            button.BackColor = activeBack;
            button.ForeColor = activeFore;
            button.FlatAppearance.BorderColor = activeBorder;
            button.FlatAppearance.MouseOverBackColor = activeBack;
            button.FlatAppearance.MouseDownBackColor = activeBack;
            return;
        }

        button.BackColor = inactiveBack;
        button.ForeColor = inactiveFore;
        button.FlatAppearance.BorderColor = inactiveBorder;
        button.FlatAppearance.MouseOverBackColor = inactiveBack;
        button.FlatAppearance.MouseDownBackColor = inactiveBack;
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

    private Image GetHeroIconImage(string appName, string? executablePath)
    {
        if (_heroIconCache.TryGetValue(appName, out var cached))
        {
            return cached;
        }

        Image iconImage;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            iconImage = ResizeImage(SystemIcons.Application.ToBitmap(), 56, 56);
        }
        else
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(executablePath);
                iconImage = icon is null
                    ? ResizeImage(SystemIcons.Application.ToBitmap(), 56, 56)
                    : ResizeImage(icon.ToBitmap(), 56, 56);
            }
            catch
            {
                iconImage = ResizeImage(SystemIcons.Application.ToBitmap(), 56, 56);
            }
        }

        _heroIconCache[appName] = iconImage;
        return iconImage;
    }

    private static Image ResizeImage(Image source, int width, int height)
    {
        var target = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(target);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        return target;
    }

    private void RestoreState()
    {
        try
        {
            _tracker.LoadState(_sessionPath);
            _taskTracker.LoadState(_tasksPath);
        }
        catch
        {
        }
    }

    private void PersistState()
    {
        try
        {
            _tracker.SaveState(_sessionPath);
            _taskTracker.SaveState(_tasksPath);
        }
        catch
        {
        }
    }

    private (string Label, TimeSpan Duration)[] BuildSessionRows(AppUsageTracker.DailyUsageEntry[] dailyDurations)
    {
        if (dailyDurations.Length == 0)
        {
            return Array.Empty<(string Label, TimeSpan Duration)>();
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        return dailyDurations
            .Take(5)
            .Select(entry =>
            {
                var daysAgo = today.DayNumber - entry.Date.DayNumber;
                var label = daysAgo switch
                {
                    0 => T("Сегодня", "Today"),
                    1 => T("Вчера", "Yesterday"),
                    _ => entry.Date.ToString(IsRussian ? "dd.MM" : "MMM d")
                };
                return (Label: label, Duration: entry.Duration);
            })
            .Reverse()
            .ToArray();
    }

    private void RenderDailyChart((string Label, TimeSpan Duration)[] rows)
    {
        _sessions.SuspendLayout();
        var hasRows = rows.Length > 0;

        if (hasRows)
        {
            _dailyChartRows = rows;
            if (_sessions.Controls.Contains(_dailyChartEmptyLabel))
            {
                _sessions.Controls.Remove(_dailyChartEmptyLabel);
            }

            if (!_sessions.Controls.Contains(_dailyChart))
            {
                _sessions.Controls.Add(_dailyChart);
            }

            _dailyChart.Width = Math.Max(680, _sessions.ClientSize.Width - 16);
            _dailyChart.Invalidate();
        }
        else
        {
            _dailyChartRows = Array.Empty<(string Label, TimeSpan Duration)>();
            if (_sessions.Controls.Contains(_dailyChart))
            {
                _sessions.Controls.Remove(_dailyChart);
            }

            _dailyChartEmptyLabel.Text = T("Пока нет данных по дням", "No daily usage data yet");
            _dailyChartEmptyLabel.ForeColor = IsLightTheme ? Color.FromArgb(110, 136, 155) : Color.FromArgb(85, 113, 136);
            if (!_sessions.Controls.Contains(_dailyChartEmptyLabel))
            {
                _sessions.Controls.Add(_dailyChartEmptyLabel);
            }
        }

        _sessions.ResumeLayout();
    }

    private void PaintDailyChart(Graphics graphics, Rectangle bounds)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(_dailyChart.BackColor);

        if (_dailyChartRows.Length == 0 || bounds.Width <= 80 || bounds.Height <= 80)
        {
            return;
        }

        var chartArea = new Rectangle(22, 18, bounds.Width - 44, bounds.Height - 54);
        if (chartArea.Width <= 0 || chartArea.Height <= 0)
        {
            return;
        }

        var axisColor = IsLightTheme ? Color.FromArgb(169, 201, 225) : Color.FromArgb(55, 78, 97);
        var barColor = IsLightTheme ? Color.FromArgb(42, 127, 184) : Color.FromArgb(77, 184, 240);
        var labelColor = IsLightTheme ? Color.FromArgb(46, 80, 105) : Color.FromArgb(190, 210, 226);
        var valueColor = IsLightTheme ? Color.FromArgb(22, 56, 81) : Color.FromArgb(230, 237, 243);

        using var axisPen = new Pen(axisColor, 1.2f);
        graphics.DrawLine(axisPen, chartArea.Left, chartArea.Bottom, chartArea.Right, chartArea.Bottom);

        var maxMinutes = Math.Max(1.0, _dailyChartRows.Max(x => x.Duration.TotalMinutes));
        var slotWidth = (float)chartArea.Width / _dailyChartRows.Length;
        var barWidth = Math.Max(20f, slotWidth * 0.45f);

        using var barBrush = new SolidBrush(barColor);
        using var valueBrush = new SolidBrush(valueColor);
        using var labelBrush = new SolidBrush(labelColor);
        using var valueFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (var i = 0; i < _dailyChartRows.Length; i++)
        {
            var entry = _dailyChartRows[i];
            var ratio = (float)(entry.Duration.TotalMinutes / maxMinutes);
            var barHeight = Math.Max(3f, ratio * (chartArea.Height - 36));
            var x = chartArea.Left + (i * slotWidth) + ((slotWidth - barWidth) / 2f);
            var y = chartArea.Bottom - barHeight;
            var barRect = new RectangleF(x, y, barWidth, barHeight);
            using (var barPath = BuildRoundedRectanglePath(barRect, 6f))
            {
                graphics.FillPath(barBrush, barPath);
            }

            var valueText = FormatDurationChart(entry.Duration);
            graphics.DrawString(valueText, valueFont, valueBrush, new PointF(x + (barWidth / 2f), y - 10), centered);

            var labelRect = new RectangleF(chartArea.Left + (i * slotWidth), chartArea.Bottom + 8, slotWidth, 20);
            graphics.DrawString(entry.Label, labelFont, labelBrush, labelRect, centered);
        }
    }

    private string FormatDurationChart(TimeSpan duration)
    {
        var h = (int)duration.TotalHours;
        var m = duration.Minutes;
        var s = Math.Max(1, duration.Seconds);

        if (IsRussian)
        {
            if (h > 0) return $"{h}ч {m}м";
            if (m > 0) return $"{m}м";
            return $"{s}с";
        }

        if (h > 0) return $"{h}h {m}m";
        if (m > 0) return $"{m}m";
        return $"{s}s";
    }

    private static GraphicsPath BuildRoundedRectanglePath(RectangleF rect, float radius)
    {
        var diameter = radius * 2f;
        var path = new GraphicsPath();

        if (diameter <= 0f)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ApplyTaskButtonStyle(Button button)
    {
        button.BackColor = IsLightTheme ? Color.White : Color.FromArgb(35, 56, 74);
        button.ForeColor = IsLightTheme ? Color.FromArgb(26, 57, 82) : Color.FromArgb(230, 237, 243);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = IsLightTheme ? 1 : 0;
        button.FlatAppearance.BorderColor = IsLightTheme ? Color.FromArgb(182, 212, 233) : Color.FromArgb(44, 66, 84);
    }

    private static void ConfigureSettingsLabel(Label label)
    {
        label.AutoSize = true;
        label.ForeColor = Color.FromArgb(230, 237, 243);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Dock = DockStyle.Fill;
    }

    private void SelectSettingsLanguage(string? languageCode)
    {
        var normalized = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
        foreach (var item in _settingsLanguageInput.Items)
        {
            if (item is LanguageOption option && string.Equals(option.Code, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _settingsLanguageInput.SelectedItem = item;
                return;
            }
        }

        if (_settingsLanguageInput.Items.Count > 0)
        {
            _settingsLanguageInput.SelectedIndex = 0;
        }
    }

    private void PopulateThemeOptions()
    {
        var current = (_settingsThemeInput.SelectedItem as ThemeOption)?.Code ?? _appSettings.Theme;
        _settingsThemeInput.Items.Clear();
        _settingsThemeInput.Items.Add(new ThemeOption("dark", T("Темная", "Dark")));
        _settingsThemeInput.Items.Add(new ThemeOption("light", T("Светло-голубая", "Light Blue")));
        SelectSettingsTheme(current);
    }

    private void SelectSettingsTheme(string? themeCode)
    {
        var normalized = string.Equals(themeCode, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        foreach (var item in _settingsThemeInput.Items)
        {
            if (item is ThemeOption option && string.Equals(option.Code, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _settingsThemeInput.SelectedItem = item;
                return;
            }
        }

        if (_settingsThemeInput.Items.Count > 0)
        {
            _settingsThemeInput.SelectedIndex = 0;
        }
    }

    private void PopulateTrackingModeOptions()
    {
        var current = (_settingsTrackingModeInput.SelectedItem as TrackingModeOption)?.Code ?? _appSettings.TrackingMode;
        _settingsTrackingModeInput.Items.Clear();
        _settingsTrackingModeInput.Items.Add(new TrackingModeOption("all", T("Все приложения", "All applications")));
        _settingsTrackingModeInput.Items.Add(new TrackingModeOption("selected", T("Только выбранные", "Selected only")));
        SelectTrackingMode(current);
    }

    private void SelectTrackingMode(string? modeCode)
    {
        var normalized = string.Equals(modeCode, "selected", StringComparison.OrdinalIgnoreCase) ? "selected" : "all";
        foreach (var item in _settingsTrackingModeInput.Items)
        {
            if (item is TrackingModeOption option && string.Equals(option.Code, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _settingsTrackingModeInput.SelectedItem = item;
                return;
            }
        }

        if (_settingsTrackingModeInput.Items.Count > 0)
        {
            _settingsTrackingModeInput.SelectedIndex = 0;
        }
    }

    private void UpdateTrackedAppsSelectorState()
    {
        var selectedMode = string.Equals((_settingsTrackingModeInput.SelectedItem as TrackingModeOption)?.Code, "selected", StringComparison.OrdinalIgnoreCase);
        _settingsInstalledAppsLabel.Enabled = selectedMode;
        _settingsInstalledAppsSearch.Enabled = selectedMode;
        _settingsInstalledAppsInput.Enabled = selectedMode;
    }

    private IEnumerable<string> GetCheckedInstalledAppNames()
    {
        foreach (ListViewItem item in _settingsInstalledAppsInput.Items)
        {
            if (!item.Checked || item.Tag is not InstalledAppCandidate candidate)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.ProcessName))
            {
                yield return candidate.ProcessName;
            }
        }
    }

    private void EnsureInstalledAppsLoaded()
    {
        if (_isLoadingInstalledApps)
        {
            return;
        }

        if (_installedAppCandidates.Count == 0)
        {
            _isLoadingInstalledApps = true;
            try
            {
                _installedAppCandidates = LoadInstalledAppCandidates();
            }
            finally
            {
                _isLoadingInstalledApps = false;
            }
        }

        MergeKnownAppsIntoInstalledCandidates();

        if (_installedAppCandidates.Count == 0)
        {
            _settingsInstalledAppsInput.Items.Clear();
            return;
        }
    }

    private void MergeKnownAppsIntoInstalledCandidates()
    {
        var knownNames = new HashSet<string>(_appSettings.SelectedTrackedApps ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        knownNames.UnionWith(_tracker.KnownAppNames);

        foreach (var processName in knownNames)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            var knownPath = _tracker.GetKnownExecutablePath(processName);
            var existingIndex = _installedAppCandidates.FindIndex(x => string.Equals(x.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                _installedAppCandidates.Add(new InstalledAppCandidate(processName, processName, knownPath));
                continue;
            }

            var existing = _installedAppCandidates[existingIndex];
            if (string.IsNullOrWhiteSpace(existing.ExecutablePath) && !string.IsNullOrWhiteSpace(knownPath))
            {
                _installedAppCandidates[existingIndex] = existing with { ExecutablePath = knownPath };
            }
        }

        _installedAppCandidates = _installedAppCandidates
            .GroupBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderBy(x => string.IsNullOrWhiteSpace(x.ExecutablePath) ? 1 : 0)
                .ThenBy(x => x.DisplayName.Length)
                .First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RefreshInstalledAppsList()
    {
        var selected = new HashSet<string>(_appSettings.SelectedTrackedApps ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        selected.UnionWith(GetCheckedInstalledAppNames());
        var query = _settingsInstalledAppsSearch.Text.Trim();
        var filtered = _installedAppCandidates
            .Where(c => string.IsNullOrWhiteSpace(query)
                || c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(600)
            .ToArray();

        _settingsInstalledAppsInput.BeginUpdate();
        _settingsInstalledAppsInput.Items.Clear();
        foreach (var candidate in filtered)
        {
            EnsureIcon(candidate.ProcessName, null, candidate.ExecutablePath);
            var item = new ListViewItem(candidate.DisplayName)
            {
                Tag = candidate,
                ImageKey = GetIconKey(candidate.ProcessName),
                Checked = selected.Contains(candidate.ProcessName)
            };
            item.SubItems.Add(candidate.ProcessName);
            _settingsInstalledAppsInput.Items.Add(item);
        }
        if (filtered.Length == 0)
        {
            _settingsInstalledAppsInput.Items.Add(new ListViewItem(T("Ничего не найдено", "No matches found")) { Tag = null, ImageKey = "default" });
        }
        _settingsInstalledAppsInput.EndUpdate();
        if (_settingsInstalledAppsInput.Columns.Count > 0)
        {
            _settingsInstalledAppsInput.Columns[0].Width = Math.Max(120, _settingsInstalledAppsInput.ClientSize.Width - 6);
        }
    }

    private static List<InstalledAppCandidate> LoadInstalledAppCandidates()
    {
        var cursor = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            var raw = ReadUninstallApps(RegistryHive.LocalMachine, RegistryView.Registry64)
                .Concat(ReadUninstallApps(RegistryHive.LocalMachine, RegistryView.Registry32))
                .Concat(ReadUninstallApps(RegistryHive.CurrentUser, RegistryView.Registry64))
                .Concat(ReadUninstallApps(RegistryHive.CurrentUser, RegistryView.Registry32))
                .ToArray();

            return raw
                .GroupBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderBy(x => x.DisplayName.Length)
                    .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            Cursor.Current = cursor;
        }
    }

    private static List<InstalledAppCandidate> ReadUninstallApps(RegistryHive hive, RegistryView view)
    {
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var result = new List<InstalledAppCandidate>();
        RegistryKey? baseKey = null;
        RegistryKey? uninstallRoot = null;
        try
        {
            baseKey = RegistryKey.OpenBaseKey(hive, view);
            uninstallRoot = baseKey.OpenSubKey(uninstallPath);
            if (uninstallRoot is null)
            {
                return result;
            }

            foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
            {
                using var subKey = uninstallRoot.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var displayName = (subKey.GetValue("DisplayName") as string)?.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var displayIcon = subKey.GetValue("DisplayIcon") as string;
                var installLocation = subKey.GetValue("InstallLocation") as string;
                var uninstallString = subKey.GetValue("UninstallString") as string;
                var executablePath = TryExtractExecutablePath(displayIcon)
                    ?? TryExtractExecutablePath(uninstallString)
                    ?? TryFindExecutableInInstallLocation(installLocation);

                var processName = TryGetProcessNameFromPath(displayIcon)
                    ?? TryGetProcessNameFromPath(installLocation)
                    ?? TryGetProcessNameFromPath(uninstallString)
                    ?? TryGetProcessNameFromDisplayName(displayName);

                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                result.Add(new InstalledAppCandidate(displayName, processName, executablePath));
            }
        }
        catch
        {
            return result;
        }
        finally
        {
            uninstallRoot?.Dispose();
            baseKey?.Dispose();
        }

        return result;
    }

    private static string? TryGetProcessNameFromPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex < 0)
        {
            return null;
        }

        var pathEnd = exeIndex + 4;
        var candidatePath = value[..pathEnd].Trim().Trim('"');
        if (candidatePath.Contains(','))
        {
            candidatePath = candidatePath[..candidatePath.IndexOf(',')].Trim().Trim('"');
        }

        var fileName = Path.GetFileNameWithoutExtension(candidatePath);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static string? TryExtractExecutablePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex < 0)
        {
            return null;
        }

        var candidate = value[..(exeIndex + 4)].Trim().Trim('"');
        if (candidate.Contains(','))
        {
            candidate = candidate[..candidate.IndexOf(',')].Trim().Trim('"');
        }

        return candidate;
    }

    private static string? TryFindExecutableInInstallLocation(string? rawInstallLocation)
    {
        if (string.IsNullOrWhiteSpace(rawInstallLocation))
        {
            return null;
        }

        try
        {
            var path = Environment.ExpandEnvironmentVariables(rawInstallLocation.Trim().Trim('"'));
            if (!Directory.Exists(path))
            {
                return null;
            }

            return Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetProcessNameFromDisplayName(string displayName)
    {
        var normalized = new string(displayName
            .Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '_' or '-')
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var compact = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(compact) ? normalized : compact;
    }

    private sealed record InstalledAppCandidate(string DisplayName, string ProcessName, string? ExecutablePath);

    private bool ShouldTrackApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        if (IsTrackAllMode)
        {
            return true;
        }

        return _appSettings.SelectedTrackedApps.Any(name => string.Equals(name, appName, StringComparison.OrdinalIgnoreCase));
    }

    private string? GetParentIdForCreate()
    {
        if (_tasksTree.SelectedNode is null)
        {
            return null;
        }

        var selected = _taskTracker.GetNode(_tasksTree.SelectedNode.Name);
        if (selected is null)
        {
            return null;
        }

        return selected.IsGroup ? selected.Id : selected.ParentId;
    }

    private void SelectTaskNode(string nodeId)
    {
        var node = FindTreeNodeById(_tasksTree.Nodes, nodeId);
        if (node is null)
        {
            return;
        }

        _tasksTree.SelectedNode = node;
        node.EnsureVisible();
    }

    private bool TryPromptTaskName(string title, string initialValue, out string value)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 380,
            Height = 170,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var nameLabel = new Label
        {
            Text = T("Название:", "Name:"),
            Left = 14,
            Top = 18,
            Width = 60
        };
        var nameInput = new TextBox
        {
            Left = 14,
            Top = 40,
            Width = 334,
            Text = initialValue
        };
        var ok = new Button
        {
            Text = T("ОК", "OK"),
            Left = 192,
            Top = 78,
            Width = 75,
            DialogResult = DialogResult.OK
        };
        var cancel = new Button
        {
            Text = T("Отмена", "Cancel"),
            Left = 273,
            Top = 78,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        dialog.Controls.Add(nameLabel);
        dialog.Controls.Add(nameInput);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            value = string.Empty;
            return false;
        }

        value = nameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(this, T("Название задачи не может быть пустым.", "Task name cannot be empty."), "Task Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        return true;
    }

    private string FormatDuration(TimeSpan duration)
    {
        var h = (int)duration.TotalHours;
        var m = duration.Minutes;
        var s = Math.Max(1, duration.Seconds);

        if (IsRussian)
        {
            if (h > 0)
            {
                return m > 0
                    ? $"{h} {RuPlural(h, "час", "часа", "часов")} {m} {RuPlural(m, "минута", "минуты", "минут")}"
                    : $"{h} {RuPlural(h, "час", "часа", "часов")}";
            }

            if (m > 0)
            {
                return $"{m} {RuPlural(m, "минута", "минуты", "минут")}";
            }

            return $"{s} {RuPlural(s, "секунда", "секунды", "секунд")}";
        }

        if (h > 0) return $"{h}h {m}m";
        if (m > 0) return $"{m}m";
        return $"{s}s";
    }

    private static string RuPlural(int value, string one, string few, string many)
    {
        var abs = Math.Abs(value) % 100;
        var last = abs % 10;
        if (abs is >= 11 and <= 14) return many;
        if (last == 1) return one;
        if (last is >= 2 and <= 4) return few;
        return many;
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

    private static string BuildTasksPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "tasks-state.json");
    }

    private void ApplyHeaderButtonStyle(Button button)
    {
        button.BackColor = IsLightTheme ? Color.White : Color.FromArgb(26, 44, 60);
        button.ForeColor = IsLightTheme ? Color.FromArgb(26, 57, 82) : Color.FromArgb(230, 237, 243);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = IsLightTheme ? Color.FromArgb(182, 212, 233) : Color.FromArgb(44, 66, 84);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = IsLightTheme ? Color.FromArgb(236, 247, 255) : Color.FromArgb(34, 58, 78);
        button.FlatAppearance.MouseDownBackColor = IsLightTheme ? Color.FromArgb(223, 241, 252) : Color.FromArgb(20, 38, 54);
        button.Margin = new Padding(6, 0, 0, 0);
    }

    private void EnableAntiFlicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
        EnableDoubleBufferRecursive(this);
    }

    private static void EnableDoubleBufferRecursive(Control control)
    {
        typeof(Control)
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(control, true, null);

        foreach (Control child in control.Controls)
        {
            EnableDoubleBufferRecursive(child);
        }
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
    }
}
