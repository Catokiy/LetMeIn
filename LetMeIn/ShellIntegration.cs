using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LetMeIn;

/// <summary>Ярлык в меню «Пуск» (чтобы находилось в поиске Windows) и папка программы в PATH пользователя.</summary>
public static class ShellIntegration
{
    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LetMeIn.lnk");

    public static void Register(string exePath)
    {
        try { CreateShortcut(exePath); } catch { /* не критично */ }
        try { SetInUserPath(Path.GetDirectoryName(exePath)!, add: true); } catch { }
    }

    public static void Unregister(string exePath)
    {
        try { File.Delete(ShortcutPath); } catch { }
        try { SetInUserPath(Path.GetDirectoryName(exePath)!, add: false); } catch { }
    }

    private static void CreateShortcut(string exePath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = exePath;
            link.WorkingDirectory = Path.GetDirectoryName(exePath);
            link.Description = "Постучаться к соседу по локальной сети";
            link.IconLocation = exePath + ",0";
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void SetInUserPath(string dir, bool add)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        var current = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        bool present = parts.Any(p => string.Equals(p.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        if (add == present) return;
        if (add) parts.Add(dir);
        else parts.RemoveAll(p => string.Equals(p.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        key.SetValue("Path", string.Join(';', parts), RegistryValueKind.ExpandString);
        // Сообщаем Explorer'у, чтобы новые окна терминала увидели изменённый PATH.
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 1000, out _);
    }

    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
}
