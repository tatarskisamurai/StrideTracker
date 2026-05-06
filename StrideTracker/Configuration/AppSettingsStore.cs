using System.Text.Json;

namespace StrideTracker.Configuration;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string path, AppSettings settings)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}
