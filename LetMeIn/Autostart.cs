using Microsoft.Win32;

namespace LetMeIn;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LetMeIn";

    private static string Command => Quote(Environment.ProcessPath!);
    private static string Quote(string path) => $"\"{path}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled, string? exePath = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, exePath is null ? Command : Quote(exePath));
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Если exe переехал, обновляем путь в автозапуске.</summary>
    public static void RefreshPathIfEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && current != Command)
            key.SetValue(ValueName, Command);
    }
}
