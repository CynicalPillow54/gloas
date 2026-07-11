using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDisplaySwitcher;

public sealed class AppSettings
{
    public string GamingMonitorId { get; set; } = "";
    public string GamingMonitorLabel { get; set; } = "";
    public bool StartWithWindows { get; set; } = true;
    public SequenceSettings GamingSequence { get; set; } = new()
    {
        Steps = [new() { Buttons = ["View", "Menu"] }, new() { Buttons = ["A"] }]
    };
    public SequenceSettings DesktopSequence { get; set; } = new()
    {
        Steps = [new() { Buttons = ["View", "Menu"] }, new() { Buttons = ["B"] }]
    };
}

public sealed class SequenceSettings
{
    public int MaxGapMs { get; set; } = 1500;
    public List<SequenceStep> Steps { get; set; } = [];
}

public sealed class SequenceStep
{
    public List<string> Buttons { get; set; } = [];
    [JsonIgnore] public string Display => string.Join(" + ", Buttons);
}

public sealed class MonitorInfo
{
    public string Name { get; init; } = "";
    public string MonitorId { get; init; } = "";
    public string Serial { get; init; } = "";
    public string MonitorString { get; init; } = "";
    public bool Active { get; init; }
    public string StableId => !string.IsNullOrWhiteSpace(Serial) ? Serial :
        !string.IsNullOrWhiteSpace(MonitorId) ? MonitorId : Name;
    public string Label => $"{Name} — {(string.IsNullOrWhiteSpace(MonitorString) ? StableId : MonitorString)}";
    public override string ToString() => Label;
}

public static class SettingsStore
{
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameDisplaySwitcher");
    public static readonly string SettingsPath = Path.Combine(DataFolder, "settings.json");
    public static readonly string DesktopProfilePath = Path.Combine(DataFolder, "desktop.cfg");
    public static readonly string LogPath = Path.Combine(DataFolder, "app.log");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(DataFolder);
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions()) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions()));
    }

    public static void Log(string message)
    {
        Directory.CreateDirectory(DataFolder);
        File.AppendAllText(LogPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
