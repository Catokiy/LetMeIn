using LetMeIn.Net;

namespace LetMeIn;

/// <summary>Маленькое окно у трея: кому, текст, «Отправить» с кулдауном.</summary>
public sealed class SendForm : Form
{
    public static readonly TimeSpan SendCooldown = TimeSpan.FromMinutes(1);
    private static readonly string[] Templates = ["Можно зайти?", "Выйди на минутку", "Еда готова"];

    private const string NoPeersHint = "Никого не найдено. Сеть «Частная»? Ключи совпадают?";

    private readonly Settings _settings;
    private readonly Func<IReadOnlyList<Peer>> _getPeers;
    private readonly ComboBox _peers = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _text = new() { Dock = DockStyle.Fill, MaxLength = Protocol.MaxTextLength };
    private readonly Button _send = new() { Text = "Отправить", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private bool _sending;

    public SendForm(Settings settings, Func<IReadOnlyList<Peer>> getPeers)
    {
        _settings = settings;
        _getPeers = getPeers;

        Text = "LetMeIn — постучаться";
        Icon = TrayContext.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;
        Padding = new Padding(10);
        Font = new Font("Segoe UI", 10f);

        var templates = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
        foreach (var t in Templates)
        {
            var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.System };
            b.Click += (_, _) => { _text.Text = t; _text.Focus(); _text.SelectAll(); };
            templates.Controls.Add(b);
        }

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        table.Controls.Add(new Label { Text = "Кому:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_peers, 1, 0);
        table.Controls.Add(new Label { Text = "Текст:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_text, 1, 1);
        table.Controls.Add(templates, 0, 2);
        table.SetColumnSpan(templates, 2);
        table.Controls.Add(_status, 0, 3);
        table.Controls.Add(_send, 1, 3);
        Controls.Add(table);

        AcceptButton = _send;
        _send.Click += async (_, _) => await SendAsync();
        _text.TextChanged += (_, _) => UpdateState();
        _peers.SelectedIndexChanged += (_, _) => UpdateState();
        _tick.Tick += (_, _) => UpdateState();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        Deactivate += (_, _) => { if (!_sending) Hide(); };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    public void ShowNearTray()
    {
        RefreshPeers();
        _status.Text = "";
        UpdateState();

        if (!Visible)
        {
            Show();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        }
        _tick.Start();
        Activate();
        _text.Focus();
        _text.SelectAll();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _tick.Stop();
    }

    public void RefreshPeers()
    {
        var selectedId = (_peers.SelectedItem as Peer)?.Id;
        var peers = _getPeers();

        _peers.BeginUpdate();
        _peers.Items.Clear();
        foreach (var p in peers) _peers.Items.Add(p);
        _peers.EndUpdate();

        if (peers.Count == 0)
            return;
        var idx = peers.ToList().FindIndex(p => p.Id == selectedId);
        _peers.SelectedIndex = idx >= 0 ? idx : 0;
        UpdateState();
    }

    private TimeSpan CooldownLeft
    {
        get
        {
            var left = _settings.LastSentUtc + SendCooldown - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    private void UpdateState()
    {
        var left = CooldownLeft;
        if (left > TimeSpan.Zero)
        {
            _send.Text = $"Подожди {left:m\\:ss}";
            _send.Enabled = false;
            return;
        }

        _send.Text = _sending ? "Отправляю…" : "Отправить";
        _send.Enabled = !_sending && _peers.SelectedItem is Peer && _text.Text.Trim().Length > 0;

        if (_peers.Items.Count == 0 && !_sending)
            _status.Text = NoPeersHint;
        else if (_status.Text == NoPeersHint)
            _status.Text = "";
    }

    private async Task SendAsync()
    {
        if (_peers.SelectedItem is not Peer peer || CooldownLeft > TimeSpan.Zero || _sending) return;
        var text = _text.Text.Trim();
        if (text.Length == 0) return;

        _sending = true;
        _status.Text = "";
        UpdateState();
        try
        {
            var result = await MessageClient.SendAsync(_settings, peer, text);
            switch (result.Status)
            {
                case SendStatus.Delivered:
                    _settings.LastSentUtc = DateTime.UtcNow;
                    _settings.Save();
                    _text.Clear();
                    _status.Text = $"Доставлено ✓ {peer.Name}";
                    break;
                case SendStatus.Cooldown:
                    // Получатель считает, что мы писали недавно — синхронизируем таймер.
                    _settings.LastSentUtc = DateTime.UtcNow - SendCooldown + TimeSpan.FromSeconds(result.RetryAfterSec);
                    _settings.Save();
                    _status.Text = "Слишком часто — подожди";
                    break;
                case SendStatus.Offline:
                    _status.Text = "Сосед не в сети";
                    break;
                default:
                    _status.Text = "Отклонено (разные ключи?)";
                    break;
            }
        }
        finally
        {
            _sending = false;
            UpdateState();
        }
    }
}
