using StrideTracker.Configuration;

namespace StrideTracker.UI;

public sealed class SettingsForm : Form
{
    private readonly NumericUpDown _samplingIntervalInput = new();
    private readonly NumericUpDown _autosaveIntervalInput = new();
    private readonly CheckBox _startOnLaunchCheckBox = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();

    public SettingsForm(AppSettings currentSettings)
    {
        Text = "Stride Tracker Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Width = 420;
        Height = 250;

        InitializeLayout();
        FillFromSettings(currentSettings);
    }

    public AppSettings ResultSettings { get; private set; } = new();

    private void InitializeLayout()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12),
            AutoSize = true
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

        var samplingLabel = new Label
        {
            Text = "Sampling interval (seconds):",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _samplingIntervalInput.Minimum = 1;
        _samplingIntervalInput.Maximum = 30;
        _samplingIntervalInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var autosaveLabel = new Label
        {
            Text = "Autosave interval (seconds):",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _autosaveIntervalInput.Minimum = 5;
        _autosaveIntervalInput.Maximum = 300;
        _autosaveIntervalInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        _startOnLaunchCheckBox.Text = "Start tracking automatically on launch";
        _startOnLaunchCheckBox.AutoSize = true;
        _startOnLaunchCheckBox.Anchor = AnchorStyles.Left;

        var buttonsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };

        _saveButton.Text = "Save";
        _saveButton.Width = 90;
        _saveButton.Click += OnSaveClicked;

        _cancelButton.Text = "Cancel";
        _cancelButton.Width = 90;
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttonsPanel.Controls.Add(_saveButton);
        buttonsPanel.Controls.Add(_cancelButton);

        panel.Controls.Add(samplingLabel, 0, 0);
        panel.Controls.Add(_samplingIntervalInput, 1, 0);
        panel.Controls.Add(autosaveLabel, 0, 1);
        panel.Controls.Add(_autosaveIntervalInput, 1, 1);
        panel.Controls.Add(_startOnLaunchCheckBox, 0, 2);
        panel.SetColumnSpan(_startOnLaunchCheckBox, 2);
        panel.Controls.Add(buttonsPanel, 0, 3);
        panel.SetColumnSpan(buttonsPanel, 2);

        Controls.Add(panel);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void FillFromSettings(AppSettings currentSettings)
    {
        _samplingIntervalInput.Value = currentSettings.SamplingIntervalSeconds;
        _autosaveIntervalInput.Value = currentSettings.AutosaveIntervalSeconds;
        _startOnLaunchCheckBox.Checked = currentSettings.StartTrackingOnLaunch;
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        ResultSettings = new AppSettings
        {
            SamplingIntervalSeconds = (int)_samplingIntervalInput.Value,
            AutosaveIntervalSeconds = (int)_autosaveIntervalInput.Value,
            StartTrackingOnLaunch = _startOnLaunchCheckBox.Checked
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
