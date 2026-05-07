using StrideTracker.Configuration;

namespace StrideTracker.UI;

public sealed class SettingsForm : Form
{
    private readonly NumericUpDown _samplingIntervalInput = new();
    private readonly NumericUpDown _autosaveIntervalInput = new();
    private readonly CheckBox _startOnLaunchCheckBox = new();
    private readonly ComboBox _languageInput = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();
    private readonly bool _isRussian;

    public SettingsForm(AppSettings currentSettings)
    {
        _isRussian = string.Equals(currentSettings.Language, "ru", StringComparison.OrdinalIgnoreCase);
        Text = T("Настройки Stride Tracker", "Stride Tracker Settings");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Width = 420;
        Height = 290;

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
            RowCount = 5,
            Padding = new Padding(12),
            AutoSize = true
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

        var samplingLabel = new Label
        {
            Text = T("Интервал сэмплинга (сек):", "Sampling interval (seconds):"),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _samplingIntervalInput.Minimum = 1;
        _samplingIntervalInput.Maximum = 30;
        _samplingIntervalInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var autosaveLabel = new Label
        {
            Text = T("Интервал автосохранения (сек):", "Autosave interval (seconds):"),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _autosaveIntervalInput.Minimum = 5;
        _autosaveIntervalInput.Maximum = 300;
        _autosaveIntervalInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var languageLabel = new Label
        {
            Text = T("Язык интерфейса:", "Interface language:"),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _languageInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _languageInput.DisplayMember = nameof(LanguageOption.Label);
        _languageInput.ValueMember = nameof(LanguageOption.Code);
        _languageInput.Items.Add(new LanguageOption("ru", "Русский"));
        _languageInput.Items.Add(new LanguageOption("en", "English"));

        _startOnLaunchCheckBox.Text = T("Запускать трекинг автоматически при старте", "Start tracking automatically on launch");
        _startOnLaunchCheckBox.AutoSize = true;
        _startOnLaunchCheckBox.Anchor = AnchorStyles.Left;

        var buttonsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };

        _saveButton.Text = T("Сохранить", "Save");
        _saveButton.Width = 90;
        _saveButton.Click += OnSaveClicked;

        _cancelButton.Text = T("Отмена", "Cancel");
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
        panel.Controls.Add(languageLabel, 0, 2);
        panel.Controls.Add(_languageInput, 1, 2);
        panel.Controls.Add(_startOnLaunchCheckBox, 0, 3);
        panel.SetColumnSpan(_startOnLaunchCheckBox, 2);
        panel.Controls.Add(buttonsPanel, 0, 4);
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
        SelectLanguage(currentSettings.Language);
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        ResultSettings = new AppSettings
        {
            SamplingIntervalSeconds = (int)_samplingIntervalInput.Value,
            AutosaveIntervalSeconds = (int)_autosaveIntervalInput.Value,
            StartTrackingOnLaunch = _startOnLaunchCheckBox.Checked,
            Language = (_languageInput.SelectedItem as LanguageOption)?.Code ?? "ru"
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private string T(string ru, string en) => _isRussian ? ru : en;

    private void SelectLanguage(string? code)
    {
        var normalized = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
        foreach (var item in _languageInput.Items)
        {
            if (item is LanguageOption option && string.Equals(option.Code, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _languageInput.SelectedItem = item;
                return;
            }
        }

        if (_languageInput.Items.Count > 0)
        {
            _languageInput.SelectedIndex = 0;
        }
    }

    private sealed record LanguageOption(string Code, string Label);
}
