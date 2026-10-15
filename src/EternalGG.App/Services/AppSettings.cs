using System.IO;
using System.Text.Json;

namespace EternalGG.App.Services;

public sealed class AppSettings
{
    public string SelfNickname { get; set; } = "";
    public int Season { get; set; }
    public bool Ranked { get; set; }
    public bool AutoDetect { get; set; } = true;
    public string LogPath { get; set; } = LogMonitor.DefaultPath;
    public List<NameRegion> SelectionRegions { get; set; } = [new(0.562, 0.86, 0.071, 0.024), new(0.710, 0.86, 0.071, 0.024), new(0.858, 0.86, 0.071, 0.024)];
    public List<NameRegion> GameRegions { get; set; } = [new(0.244, 0.157, 0.086, 0.021), new(0.244, 0.209, 0.086, 0.021), new(0.244, 0.261, 0.086, 0.021)];
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EternalGG", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
