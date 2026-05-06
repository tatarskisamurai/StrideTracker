using StrideTracker.Tracking;

namespace StrideTracker.UI;

public sealed class AppDetailsForm : Form
{
    private readonly Label _lastLaunchValue = new();
    private readonly Label _timeSpentValue = new();
    private readonly Label _pathValue = new();
    private readonly Button _launchButton = new();
    private readonly Func<string, bool> _launchAction;
    private readonly AppUsageTracker.AppDetails _details;

    public AppDetailsForm(AppUsageTracker.AppDetails details, Func<string, bool> launchAction)
    {
        _details = details;
        _launchAction = launchAction;

        Text = $"{details.AppName} - App Page";
        Width = 560;
        Height = 280;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 240);

        InitializeLayout();
        FillData();
    }

    private void InitializeLayout()
    {
        var contentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12)
        };
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var appNameLabel = new Label { Text = "Application:", AutoSize = true, Anchor = AnchorStyles.Left };
        var appNameValue = new Label { Text = _details.AppName, AutoSize = true, Anchor = AnchorStyles.Left };

        var lastLaunchLabel = new Label { Text = "Last launch:", AutoSize = true, Anchor = AnchorStyles.Left };
        _lastLaunchValue.AutoSize = true;
        _lastLaunchValue.Anchor = AnchorStyles.Left;

        var timeSpentLabel = new Label { Text = "Time spent:", AutoSize = true, Anchor = AnchorStyles.Left };
        _timeSpentValue.AutoSize = true;
        _timeSpentValue.Anchor = AnchorStyles.Left;

        var pathLabel = new Label { Text = "Executable path:", AutoSize = true, Anchor = AnchorStyles.Left };
        _pathValue.AutoSize = true;
        _pathValue.MaximumSize = new Size(340, 0);
        _pathValue.Anchor = AnchorStyles.Left;

        contentPanel.Controls.Add(appNameLabel, 0, 0);
        contentPanel.Controls.Add(appNameValue, 1, 0);
        contentPanel.Controls.Add(lastLaunchLabel, 0, 1);
        contentPanel.Controls.Add(_lastLaunchValue, 1, 1);
        contentPanel.Controls.Add(timeSpentLabel, 0, 2);
        contentPanel.Controls.Add(_timeSpentValue, 1, 2);
        contentPanel.Controls.Add(pathLabel, 0, 3);
        contentPanel.Controls.Add(_pathValue, 1, 3);

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 10)
        };

        _launchButton.Text = "Launch";
        _launchButton.Width = 100;
        _launchButton.Click += OnLaunchClicked;
        _launchButton.Enabled = !string.IsNullOrWhiteSpace(_details.ExecutablePath) && File.Exists(_details.ExecutablePath);

        buttonsPanel.Controls.Add(_launchButton);

        Controls.Add(contentPanel);
        Controls.Add(buttonsPanel);
    }

    private void FillData()
    {
        _lastLaunchValue.Text = _details.LastLaunchUtc?.ToLocalTime().ToString("g") ?? "Never";
        _timeSpentValue.Text = _details.Duration.ToString(@"hh\:mm\:ss");
        _pathValue.Text = string.IsNullOrWhiteSpace(_details.ExecutablePath) ? "Unknown" : _details.ExecutablePath;
    }

    private void OnLaunchClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_details.ExecutablePath))
        {
            return;
        }

        if (!_launchAction(_details.ExecutablePath))
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
