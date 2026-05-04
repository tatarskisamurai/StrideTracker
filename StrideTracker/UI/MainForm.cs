using StrideTracker.Tracking;

namespace StrideTracker.UI;

public sealed class MainForm : Form
{
    private readonly AppUsageTracker _tracker = new();
    private readonly System.Windows.Forms.Timer _samplingTimer = new() { Interval = 1000 };
    private readonly int _selfProcessId = Environment.ProcessId;

    private readonly Label _statusLabel = new();
    private readonly Label _currentAppLabel = new();
    private readonly ListView _usageListView = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _saveButton = new();

    private bool _isTracking;
    private DateTimeOffset? _startedAt;

    public MainForm()
    {
        Text = "Stride Tracker";
        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 420);

        InitializeLayout();
        BindEvents();
        UpdateUiState();
    }

    private void InitializeLayout()
    {
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Padding = new Padding(12, 12, 12, 6);
        _statusLabel.Font = new Font(_statusLabel.Font, FontStyle.Bold);
        _statusLabel.Text = "Status: Stopped";

        _currentAppLabel.Dock = DockStyle.Top;
        _currentAppLabel.Padding = new Padding(12, 0, 12, 10);
        _currentAppLabel.Text = "Current app: -";

        _usageListView.Dock = DockStyle.Fill;
        _usageListView.View = View.Details;
        _usageListView.FullRowSelect = true;
        _usageListView.GridLines = true;
        _usageListView.Columns.Add("Application", 420);
        _usageListView.Columns.Add("Time", 160);
        _usageListView.Columns.Add("Percent", 120);

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

        _saveButton.Text = "Save report";
        _saveButton.Width = 120;

        controlsPanel.Controls.AddRange([_startButton, _stopButton, _saveButton]);
        Controls.Add(_usageListView);
        Controls.Add(controlsPanel);
        Controls.Add(_currentAppLabel);
        Controls.Add(_statusLabel);
    }

    private void BindEvents()
    {
        _samplingTimer.Tick += OnSamplingTick;
        _startButton.Click += (_, _) => StartTracking();
        _stopButton.Click += (_, _) => StopTracking();
        _saveButton.Click += async (_, _) => await SaveReportWithDialogAsync();
        FormClosing += OnFormClosing;
    }

    private void StartTracking()
    {
        if (_isTracking)
        {
            return;
        }

        _isTracking = true;
        _startedAt = DateTimeOffset.Now;
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
        _isTracking = false;
        _currentAppLabel.Text = "Current app: -";
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
            _currentAppLabel.Text = "Current app: (Stride Tracker UI is focused)";
            RefreshUsageList();
            return;
        }

        _tracker.AddSample(sample);
        var title = string.IsNullOrWhiteSpace(sample.WindowTitle) ? "(no title)" : sample.WindowTitle;
        _currentAppLabel.Text = $"Current app: {sample.ProcessName} | {title}";
        RefreshUsageList();
    }

    private async Task SaveReportWithDialogAsync()
    {
        using var saveDialog = new SaveFileDialog
        {
            Title = "Save usage report",
            Filter = "JSON files (*.json)|*.json",
            FileName = $"usage-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (saveDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (_isTracking)
        {
            var sample = AppUsageTracker.TryGetActiveWindowInfo();
            if (sample is not null && sample.ProcessId != _selfProcessId)
            {
                _tracker.AddSample(sample);
            }
            else if (sample is not null)
            {
                _tracker.Flush(sample.CapturedAtUtc);
            }
        }

        try
        {
            await _tracker.SaveJsonAsync(saveDialog.FileName);
            MessageBox.Show(this, $"Report saved to:\n{saveDialog.FileName}", "Stride Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            UpdateUiState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot save report:\n{ex.Message}", "Stride Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshUsageList()
    {
        _usageListView.BeginUpdate();
        _usageListView.Items.Clear();

        var snapshot = _tracker.DurationsByApp
            .OrderByDescending(x => x.Value)
            .ToArray();

        var total = snapshot.Sum(x => x.Value.TotalSeconds);

        foreach (var (appName, duration) in snapshot)
        {
            var percent = total > 0 ? duration.TotalSeconds / total * 100d : 0d;
            var item = new ListViewItem(appName);
            item.SubItems.Add(duration.ToString(@"hh\:mm\:ss"));
            item.SubItems.Add($"{percent:0.0}%");
            _usageListView.Items.Add(item);
        }

        _usageListView.EndUpdate();
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        _statusLabel.Text = _isTracking
            ? $"Status: Tracking (started {_startedAt:HH:mm:ss})"
            : "Status: Stopped";

        _startButton.Enabled = !_isTracking;
        _stopButton.Enabled = _isTracking;
        _saveButton.Enabled = _tracker.DurationsByApp.Count > 0 || _isTracking;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isTracking)
        {
            StopTracking();
        }
    }
}
