namespace LetMeIn;

/// <summary>«Доступна новая версия»: что изменилось + кнопка «Обновить».</summary>
public sealed class UpdateForm : Form
{
    private readonly ReleaseInfo _release;
    private readonly Button _update = new() { Text = "Обновить", AutoSize = true };
    private readonly Button _later = new() { Text = "Позже", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText };

    public UpdateForm(ReleaseInfo release)
    {
        _release = release;

        Text = "Обновление LetMeIn";
        Icon = TrayContext.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 10f);

        var header = new Label
        {
            Text = $"Доступна версия {release.Tag} (у тебя {Updater.CurrentVersionText}).\nНастройки сохранятся.",
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 8),
        };

        var notes = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(420, 200),
            BackColor = SystemColors.Window,
            Text = string.IsNullOrWhiteSpace(release.Notes)
                ? "Описание изменений не указано."
                : release.Notes.ReplaceLineEndings("\r\n"),
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0),
        };
        buttons.Controls.Add(_later);
        buttons.Controls.Add(_update);

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        table.Controls.Add(header, 0, 0);
        table.SetColumnSpan(header, 2);
        var notesTitle = new Label { Text = "Что нового:", AutoSize = true };
        table.Controls.Add(notesTitle, 0, 1);
        table.SetColumnSpan(notesTitle, 2);
        table.Controls.Add(notes, 0, 2);
        table.SetColumnSpan(notes, 2);
        table.Controls.Add(_status, 0, 3);
        table.Controls.Add(buttons, 1, 3);
        Controls.Add(table);

        AcceptButton = _update;
        CancelButton = _later;
        _update.Click += async (_, _) => await UpdateAsync();
        Shown += (_, _) => _update.Focus();
    }

    private async Task UpdateAsync()
    {
        _update.Enabled = _later.Enabled = false;
        _status.Text = "Скачиваю…";
        try
        {
            await Updater.DownloadAndRunAsync(_release,
                new Progress<int>(p => _status.Text = $"Скачиваю… {p}%"));
            DialogResult = DialogResult.OK;
        }
        catch (Exception e)
        {
            _status.Text = "";
            _update.Enabled = _later.Enabled = true;
            MessageBox.Show(this, $"Не удалось скачать обновление:\n{e.Message}", "LetMeIn",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
