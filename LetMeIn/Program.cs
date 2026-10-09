namespace LetMeIn;

internal static class Program
{
    private const string MutexName = @"Local\LetMeIn-SingleInstance";
    private const string ShowEventName = @"Local\LetMeIn-ShowSendForm";
    private const string ExitEventName = @"Local\LetMeIn-Exit";

    [STAThread]
    private static void Main(string[] args)
    {
        // Служебные запуски с правами админа (из Installer).
        if (args.Contains(Installer.SetupFirewallArg))
        {
            var s = Settings.Load();
            Firewall.AddRules(Installer.InstalledExe, s.DiscoveryPort, s.MessagePort);
            return;
        }
        if (args.Contains(Installer.RemoveFirewallArg))
        {
            Firewall.RemoveRules();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Запуск скачанного exe из любого места = установка/обновление.
        bool noInstall = args.Contains("--no-install");
#if DEBUG
        noInstall = true;
#endif
        if (!noInstall && !Installer.IsRunningInstalledCopy)
        {
            Installer.Install();
            return;
        }

        using var mutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // Уже запущено. Если это не клик по уведомлению — просим открыть окно отправки.
            if (!args.Any(a => a.Contains("ToastActivated", StringComparison.OrdinalIgnoreCase))
                && EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
            {
                ev.Set();
                ev.Dispose();
            }
            return;
        }

        bool debugLoopback = args.Contains("--debug-loopback");
        var settings = Settings.Load();
        var context = new TrayContext(settings, debugLoopback);

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        var listener = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    if (WaitHandle.WaitAny([showEvent, exitEvent]) == 0)
                        context.ShowSendFormFromAnyThread();
                    else
                    {
                        context.ExitFromAnyThread();
                        return;
                    }
                }
            }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true };
        listener.Start();

        Application.Run(context);
        mutex.ReleaseMutex();
    }

    /// <summary>Просит уже запущенный экземпляр закрыться (для обновления exe).</summary>
    public static void SignalRunningInstanceToExit()
    {
        if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var ev)) return;
        using (ev) ev.Set();

        // Ждём, пока старый экземпляр отпустит мьютекс (до 5 сек).
        using var m = new Mutex(false, MutexName);
        bool acquired;
        try { acquired = m.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (acquired) m.ReleaseMutex();
    }
}
