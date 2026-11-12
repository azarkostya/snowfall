using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Snowfall;

internal enum Intensity { Light, Normal, Blizzard }

internal enum GarlandMode { Twinkle, Chase, Steady }

internal sealed class Settings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Snowfall";

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snowfall");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public bool Enabled { get; set; } = true;
    /// <summary>Flakes are falling. Off = no snowfall, but piles and decorations stay.</summary>
    public bool Falling { get; set; } = true;
    public bool PilesEnabled { get; set; } = true;
    public Intensity Intensity { get; set; } = Intensity.Normal;
    public bool Garland { get; set; } = true;
    public GarlandMode GarlandMode { get; set; } = GarlandMode.Twinkle;
    public bool Snowman { get; set; } = true;
    public bool Tree { get; set; } = true;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch
        {
            // Corrupt or unreadable settings: fall back to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Settings are a convenience; never crash over them.
        }
    }

    [JsonIgnore]
    public static bool Autostart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
