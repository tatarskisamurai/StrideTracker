using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
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
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Button _launch = new();
    private readonly Button _settings = new();
    private readonly Button _tasksView = new();

    private readonly Panel _appsPage = new();
    private readonly Panel _tasksPage = new();
    private readonly TreeView _tasksTree = new();
    private readonly Label _activeTaskLabel = new();
    private readonly Label _taskSummaryLabel = new();
    private readonly Button _taskStartButton = new();
    private readonly Button _taskStopButton = new();

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
        EnableAntiFlicker();
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
        sidebar.Controls.Add(sidebarHeader);
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
        content.Controls.Add(hero);
        content.Controls.Add(_sessions);

        main.Controls.Add(header, 0, 0);
        main.Controls.Add(content, 0, 1);

        _appsPage.Dock = DockStyle.Fill;
        _appsPage.Controls.Add(main);

        BuildTasksPage();
        _tasksPage.Visible = false;

        var mainHost = new Panel { Dock = DockStyle.Fill };
        mainHost.Controls.Add(_appsPage);
        mainHost.Controls.Add(_tasksPage);

        root.Controls.Add(sidebar, 0, 0);
        root.Controls.Add(mainHost, 1, 0);
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
        _tasksView.Click += (_, _) => ShowTasksPage();
        _taskStartButton.Click += (_, _) => StartSelectedTask();
        _taskStopButton.Click += (_, _) => StopTask();
        _tasksTree.AfterSelect += (_, _) => UpdateTaskSummary();
        FormClosing += (_, _) =>
        {
            if (_isTracking) StopTracking();
            PersistState();
        };
    }

    private void BuildTasksPage()
    {
        _tasksPage.Dock = DockStyle.Fill;
        _tasksPage.BackColor = Color.FromArgb(14, 24, 34);

        var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.FromArgb(14, 24, 34), Padding = new Padding(16, 12, 16, 12) };
        var backButton = new Button
        {
            Text = "← Apps",
            Width = 90,
            Height = 30,
            BackColor = Color.FromArgb(26, 44, 60),
            ForeColor = Color.FromArgb(230, 237, 243),
            FlatStyle = FlatStyle.Flat
        };
        backButton.FlatAppearance.BorderSize = 0;
        backButton.Click += (_, _) => ShowAppsPage();

        var title = new Label
        {
            Text = "Task Tracker",
            ForeColor = Color.FromArgb(230, 237, 243),
            AutoSize = true,
            Left = 110,
            Top = 18,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        header.Controls.Add(backButton);
        header.Controls.Add(title);

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

        right.Controls.Add(_activeTaskLabel);
        right.Controls.Add(_taskSummaryLabel);
        right.Controls.Add(taskButtons);
        body.Panel2.Controls.Add(right);

        _tasksPage.Controls.Add(body);
        _tasksPage.Controls.Add(header);
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
        RefreshTasksTree();
        UpdateUi();
    }

    private void RefreshTasksTree()
    {
        var selectedId = _tasksTree.SelectedNode?.Name;
        _tasksTree.BeginUpdate();
        _tasksTree.Nodes.Clear();

        foreach (var rootNode in _taskTracker.GetChildren(null))
        {
            _tasksTree.Nodes.Add(BuildTaskTreeNode(rootNode));
        }

        _tasksTree.ExpandAll();
        _tasksTree.EndUpdate();

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var match = FindTreeNodeById(_tasksTree.Nodes, selectedId);
            if (match is not null)
            {
                _tasksTree.SelectedNode = match;
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

    private void RefreshDetails()
    {
        if (string.IsNullOrWhiteSpace(_selectedApp) || !_tracker.TryGetAppDetails(_selectedApp, out var d))
        {
            _title.Text = "Select an app";
            _appName.Text = "No app selected";
            _appPath.Text = "-";
            _lastLaunch.Text = "Last launch: Never";
            _today.Text = "0h 00m";
            _week.Text = "0h 00m";
            _total.Text = "0h 00m";
            _heroIcon.Image = GetHeroIconImage("default", null);
            _sessions.SuspendLayout();
            _sessions.Controls.Clear();
            _sessions.Controls.Add(new Label { Text = "No sessions yet", AutoSize = true, ForeColor = Color.FromArgb(85, 113, 136) });
            _sessions.ResumeLayout();
            return;
        }

        _title.Text = d.AppName;
        _appName.Text = d.AppName;
        _appPath.Text = d.ExecutablePath ?? "Unknown path";
        _lastLaunch.Text = $"Last launch: {d.LastLaunchUtc?.ToLocalTime().ToString("g") ?? "Never"}";
        var todayValue = FormatDuration(d.TodayDuration);
        var weekValue = FormatDuration(d.WeekDuration);
        var totalValue = FormatDuration(d.TotalDuration);
        _today.Text = todayValue;
        _week.Text = weekValue;
        _total.Text = totalValue;
        _heroIcon.Image = GetHeroIconImage(d.AppName, d.ExecutablePath);

        _sessions.SuspendLayout();
        _sessions.Controls.Clear();
        foreach (var entry in BuildSessionRows(d.RecentDailyDurations))
        {
            _sessions.Controls.Add(new Label
            {
                Text = $"{entry.Label}  {FormatDuration(entry.Duration)}",
                AutoSize = true,
                ForeColor = Color.FromArgb(155, 178, 199),
                Margin = new Padding(3, 4, 3, 4)
            });
        }
        _sessions.ResumeLayout();
    }

    private void ShowTasksPage()
    {
        _appsPage.Visible = false;
        _tasksPage.Visible = true;
        RefreshTasksTree();
    }

    private void ShowAppsPage()
    {
        _tasksPage.Visible = false;
        _appsPage.Visible = true;
    }

    private void UpdateTaskSummary()
    {
        var active = _taskTracker.ActiveNodeId;
        if (!string.IsNullOrWhiteSpace(active))
        {
            var activeNode = _taskTracker.GetNode(active);
            _activeTaskLabel.Text = $"Active: {activeNode?.Name ?? active}";
        }
        else
        {
            _activeTaskLabel.Text = "Active: —";
        }

        if (_tasksTree.SelectedNode is null)
        {
            _taskSummaryLabel.Text = "Select a task";
            return;
        }

        var selectedId = _tasksTree.SelectedNode.Name;
        var node = _taskTracker.GetNode(selectedId);
        if (node is null)
        {
            _taskSummaryLabel.Text = "Select a task";
            return;
        }

        var own = FormatDuration(_taskTracker.GetOwnDuration(selectedId));
        var total = FormatDuration(_taskTracker.GetTotalDuration(selectedId));
        _taskSummaryLabel.Text = $"Selected: {node.Name}\nOwn: {own} | Total: {total}";
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

    private static (string Label, TimeSpan Duration)[] BuildSessionRows(AppUsageTracker.DailyUsageEntry[] dailyDurations)
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
                var label = entry.Date == today
                    ? "Today"
                    : entry.Date == today.AddDays(-1)
                        ? "Yesterday"
                        : entry.Date.ToString("MMM d");
                return (Label: label, Duration: entry.Duration);
            })
            .ToArray();
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

    private static string BuildTasksPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "tasks-state.json");
    }

    private static void ApplyHeaderButtonStyle(Button button)
    {
        button.BackColor = Color.FromArgb(26, 44, 60);
        button.ForeColor = Color.FromArgb(230, 237, 243);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(44, 66, 84);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 58, 78);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 38, 54);
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
}
