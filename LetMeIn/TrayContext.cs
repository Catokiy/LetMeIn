using System.Net.Sockets;
using LetMeIn.Net;

namespace LetMeIn;

public sealed class TrayContext : ApplicationContext
{
    public static readonly Icon AppIcon =
        Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;

    private readonly Settings _settings;
    private readonly bool _debugLoopback;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly SynchronizationContext _ui;
    private SendForm? _sendForm;
    private PeerDiscovery? _discovery;
    private MessageServer? _server;

    public TrayContext(Settings settings, bool debugLoopback)
    {
        _settings = settings;
        _debugLoopback = debugLoopback;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _autostartItem = new ToolStripMenuItem("Запускать вместе с Windows", null, (_, _) => ToggleAutostart());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Постучаться…", null, (_, _) => ShowSendForm()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add("Настройки…", null, (_, _) => EditSettings(firstRun: false));
        menu.Items.Add("Тестовое уведомление", null, (_, _) => Notifier.ShowKnock(_settings.Name, "Это тест — так будет выглядеть стук."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Удалить LetMeIn…", null, (_, _) => UninstallApp());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => _autostartItem.Checked = Autostart.IsEnabled;

        _tray = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "LetMeIn",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSendForm(); };

        if (!_settings.FirstRunDone)
        {
            if (!EditSettings(firstRun: true))
            {
                // Без имени и ключа работать нельзя.
                _tray.Visible = false;
                Environment.Exit(0);
            }
            if (Installer.IsRunningInstalledCopy) Autostart.Set(true);
            _settings.FirstRunDone = true;
            _settings.Save();
        }
        else
        {
            Autostart.RefreshPathIfEnabled();
            StartNetwork();
        }
    }

    public void ShowSendForm()
    {
        _sendForm ??= new SendForm(_settings, () => _discovery?.Peers ?? []);
        _sendForm.ShowNearTray();
    }

    /// <summary>Вызывается из другого потока, когда пользователь запускает exe повторно.</summary>
    public void ShowSendFormFromAnyThread() => _ui.Post(_ => ShowSendForm(), null);

    public void ExitFromAnyThread() => _ui.Post(_ => ExitThread(), null);

    private void UninstallApp()
    {
        var answer = MessageBox.Show(
            "Удалить LetMeIn с этого компьютера?\nПрограмма уберётся из автозапуска, фаервола и папки установки.",
            "LetMeIn", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        StopNetwork();
        Installer.Uninstall();
        ExitThread();
    }

    private bool EditSettings(bool firstRun)
    {
        using var form = new SettingsForm(_settings, firstRun);
        if (form.ShowDialog() != DialogResult.OK) return false;

        bool changed = form.UserName != _settings.Name || form.SharedKey != _settings.SharedKey;
        _settings.Name = form.UserName;
        _settings.SharedKey = form.SharedKey;
        _settings.Save();
        if (changed || _discovery is null) StartNetwork();
        return true;
    }

    private void StartNetwork()
    {
        StopNetwork();
        try
        {
            _server = new MessageServer(_settings);
            _server.MessageReceived += (from, text) => Notifier.ShowKnock(from, text);
            _server.Start();

            _discovery = new PeerDiscovery(_settings, _debugLoopback);
            _discovery.PeersChanged += () => _ui.Post(_ => OnPeersChanged(), null);
            _discovery.Start();
            OnPeersChanged();
        }
        catch (SocketException e)
        {
            StopNetwork();
            MessageBox.Show(
                $"Не удалось открыть сетевые порты {_settings.DiscoveryPort}/{_settings.MessagePort}.\n" +
                $"Возможно, их занимает другая программа.\n\n{e.Message}",
                "LetMeIn", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _tray.Text = "LetMeIn — сеть не работает";
        }
    }

    private void StopNetwork()
    {
        _server?.Dispose();
        _discovery?.Dispose();
        _server = null;
        _discovery = null;
    }

    private void OnPeersChanged()
    {
        var peers = _discovery?.Peers ?? [];
        var text = peers.Count == 0
            ? "LetMeIn — никого в сети"
            : "LetMeIn — в сети: " + string.Join(", ", peers.Select(p => p.Name));
        _tray.Text = text.Length > 127 ? text[..124] + "..." : text;
        if (_sendForm is { Visible: true }) _sendForm.RefreshPeers();
    }

    private static void ToggleAutostart() => Autostart.Set(!Autostart.IsEnabled);

    protected override void ExitThreadCore()
    {
        StopNetwork();
        _tray.Visible = false;
        _tray.Dispose();
        _sendForm?.Dispose();
        base.ExitThreadCore();
    }
}
