using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace LetMeIn;

public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string DownloadUrl);

/// <summary>Обновление из последнего релиза на GitHub. Настройки лежат в %APPDATA% и не затрагиваются.</summary>
public static class Updater
{
    private const string Repo = "Catokiy/LetMeIn";
    private const string AssetName = "LetMeIn.exe";

    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "LetMeIn-update");

    public static Version CurrentVersion => Normalize(Assembly.GetEntryAssembly()!.GetName().Version!);

    public static string CurrentVersionText => $"v{CurrentVersion.ToString(3)}";

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LetMeIn", CurrentVersion.ToString(3)));
        return http;
    }

    /// <summary>Последний релиз, если он новее текущей версии; иначе null.</summary>
    public static async Task<ReleaseInfo?> CheckAsync()
    {
        using var http = CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var doc = JsonDocument.Parse(
            await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = Normalize(version);
        if (version <= CurrentVersion) return null;

        var url = root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == AssetName)
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault();
        if (url is null) return null;

        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new ReleaseInfo(version, tag, notes.Trim(), url);
    }

    /// <summary>Скачивает новый exe и запускает его: он сам заменит установленную копию и перезапустится.</summary>
    public static async Task DownloadAndRunAsync(ReleaseInfo release, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(TempDir);
        var target = Path.Combine(TempDir, AssetName);

        using var http = CreateClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        using var resp = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;

        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var dst = File.Create(target))
        {
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((int)(done * 100 / total.Value));
            }
        }

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    /// <summary>Удаляет скачанный установщик после обновления.</summary>
    public static void CleanupTemp()
    {
        try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true); }
        catch { /* установщик ещё может быть запущен — удалим в следующий раз */ }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
