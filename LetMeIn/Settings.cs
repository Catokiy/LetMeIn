using System.Text.Json;

namespace LetMeIn;

public sealed class Settings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = Environment.UserName;
    public string SharedKey { get; set; } = "";
    public int DiscoveryPort { get; set; } = 47820;
    public int MessagePort { get; set; } = 47821;
    public DateTime LastSentUtc { get; set; } = DateTime.MinValue;
    public bool FirstRunDone { get; set; }

    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LetMeIn");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _saveLock = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(SharedKey);

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // Повреждённый файл — начинаем с чистых настроек.
        }
        return new Settings();
    }

    public void Save()
    {
        lock (_saveLock)
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
