using System.Diagnostics;

namespace LetMeIn;

/// <summary>Правила фаервола через netsh: входящие только из локальной подсети и только в «Частной» сети.</summary>
public static class Firewall
{
    private const string RuleName = "LetMeIn";

    public static bool RulesExist() => Netsh($"advfirewall firewall show rule name=\"{RuleName}\"") == 0;

    /// <summary>Требует прав администратора.</summary>
    public static void AddRules(string exePath, int udpPort, int tcpPort)
    {
        RemoveRules();
        foreach (var (proto, port) in new[] { ("UDP", udpPort), ("TCP", tcpPort) })
        {
            Netsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
                  $"program=\"{exePath}\" protocol={proto} localport={port} " +
                  "remoteip=localsubnet profile=private");
        }
    }

    /// <summary>Требует прав администратора.</summary>
    public static void RemoveRules() => Netsh($"advfirewall firewall delete rule name=\"{RuleName}\"");

    private static int Netsh(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("netsh.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
