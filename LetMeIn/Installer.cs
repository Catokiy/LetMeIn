using System.ComponentModel;
using System.Diagnostics;

namespace LetMeIn;

/// <summary>
/// Самоустановка: exe, запущенный не из папки установки, копирует себя в %LOCALAPPDATA%\LetMeIn,
/// настраивает фаервол (один запрос UAC), автозапуск и запускает установленную копию.
/// </summary>
public static class Installer
{
    public const string SetupFirewallArg = "--setup-firewall";
    public const string RemoveFirewallArg = "--remove-firewall";

    public static readonly string InstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LetMeIn");

    public static readonly string InstalledExe = Path.Combine(InstallDir, "LetMeIn.exe");

    public static bool IsRunningInstalledCopy =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(InstalledExe),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Установить/обновить и запустить установленную копию.</summary>
    public static void Install()
    {
        bool wasInstalled = File.Exists(InstalledExe);

        // Если уже запущена старая версия — просим её закрыться, иначе exe не перезаписать.
        Program.SignalRunningInstanceToExit();

        Directory.CreateDirectory(InstallDir);
        if (!CopyWithRetry(Environment.ProcessPath!, InstalledExe))
        {
            MessageBox.Show($"Не удалось скопировать программу в\n{InstallDir}\n\nЗакрой LetMeIn и попробуй ещё раз.",
                "LetMeIn", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        // Убираем пометку «скачано из интернета», чтобы автозапуск не спотыкался о SmartScreen.
        try { File.Delete(InstalledExe + ":Zone.Identifier"); } catch { }

        if (!Firewall.RulesExist())
            RunElevated(SetupFirewallArg);

        Autostart.Set(true, InstalledExe);
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });

        if (wasInstalled)
            Notifier.ShowInfo("LetMeIn обновлён", "Программа в трее, всё работает как раньше.");
    }

    /// <summary>Удалить: автозапуск, фаервол, папку с exe (после выхода). Настройки остаются.</summary>
    public static void Uninstall()
    {
        Autostart.Set(false);
        RunElevated(RemoveFirewallArg);
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static bool CopyWithRetry(string from, string to)
    {
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
            return true;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return true;
            }
            catch (IOException) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { Thread.Sleep(250); }
        }
        return false;
    }

    /// <summary>Запускает установленный exe с правами админа и ждёт. false — пользователь отказал в UAC.</summary>
    private static bool RunElevated(string arg)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(InstalledExe, arg)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            p?.WaitForExit(30_000);
            return true;
        }
        catch (Win32Exception)
        {
            // Отказ в UAC: Windows сама спросит про фаервол при первом запуске.
            return false;
        }
    }
}
