namespace LetMeIn;

/// <summary>Имя, которое увидит сосед, и общий ключ (должен совпадать на обоих ПК).</summary>
public sealed class SettingsForm : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, MaxLength = Net.Protocol.MaxNameLength };
    private readonly TextBox _key = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 128 };

    public string UserName => _name.Text.Trim();
    public string SharedKey => _key.Text.Trim();

    public SettingsForm(Settings settings, bool firstRun)
    {
        Text = firstRun ? "LetMeIn — первый запуск" : "LetMeIn — настройки";
        Icon = TrayContext.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 10f);

        _name.Text = settings.Name;
        _key.Text = settings.SharedKey;

        var showKey = new CheckBox { Text = "Показать", AutoSize = true, Anchor = AnchorStyles.Left };
        showKey.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !showKey.Checked;

        var hint = new Label
        {
            Text = "Ключ — любое секретное слово. Впишите одинаковый на обоих компьютерах:\n" +
                   "без него чужие устройства в сети не смогут слать вам сообщения.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 8),
        };

        var ok = new Button { Text = "Сохранить", AutoSize = true, Anchor = AnchorStyles.Right };
        ok.Click += (_, _) =>
        {
            if (UserName.Length == 0 || SharedKey.Length == 0)
            {
                MessageBox.Show(this, "Заполни имя и ключ.", "LetMeIn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        AcceptButton = ok;

        var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        table.Controls.Add(new Label { Text = "Твоё имя:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_name, 1, 0);
        table.SetColumnSpan(_name, 2);
        table.Controls.Add(new Label { Text = "Общий ключ:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_key, 1, 1);
        table.Controls.Add(showKey, 2, 1);
        table.Controls.Add(hint, 0, 2);
        table.SetColumnSpan(hint, 3);
        table.Controls.Add(ok, 0, 3);
        table.SetColumnSpan(ok, 3);

        Controls.Add(table);
    }
}
