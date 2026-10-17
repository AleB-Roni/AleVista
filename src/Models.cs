using System.Text.Json;

namespace AleVista;

public sealed class SnapshotDto
{
    public string Paths { get; set; } = "";
    public string Modes { get; set; } = "";
    public List<string> MonitorIds { get; set; } = new();
    public Dictionary<string, string> Adapters { get; set; } = new();

    public SnapshotDto Clone() => new()
    {
        Paths = Paths,
        Modes = Modes,
        MonitorIds = new List<string>(MonitorIds),
        Adapters = new Dictionary<string, string>(Adapters),
    };
}

/// <summary>Monitore, die dasselbe Bild zeigen (Spiegeln/Duplizieren).</summary>
public sealed class MirrorGroup
{
    public List<string> Monitors { get; set; } = new();
    /// <summary>Monitor, dessen Auflösung (und Position) die Gruppe übernimmt. null = erster der Gruppe.</summary>
    public string? ResolutionFrom { get; set; }

    public MirrorGroup Clone() => new() { Monitors = new List<string>(Monitors), ResolutionFrom = ResolutionFrom };

    /// <summary>Die Mitglieder, die gerade verfügbar sind (Reihenfolge bleibt).</summary>
    public List<string> Present(Func<string, bool> has) =>
        Monitors.Where(has).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Monitor, dessen Auflösung gilt – unter den verfügbaren Mitgliedern.</summary>
    public string Owner(List<string> present) =>
        present.FirstOrDefault(id => string.Equals(id, ResolutionFrom, StringComparison.OrdinalIgnoreCase)) ?? present[0];
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>IDs der Monitore, die in diesem Profil an sind.</summary>
    public List<string> Monitors { get; set; } = new();
    /// <summary>Gewünschter Hauptmonitor (null = automatisch).</summary>
    public string? Primary { get; set; }
    /// <summary>Exakt gespeicherte Anordnung („Aktuelle Anordnung speichern“). null = aus der Vorlage abgeleitet.</summary>
    public SnapshotDto? Fixed { get; set; }
    /// <summary>0 = kein Kürzel, 1–9 = Strg+Alt+Ziffer.</summary>
    public int Hotkey { get; set; }
    /// <summary>Gespiegelte Monitore in diesem Profil.</summary>
    public List<MirrorGroup> Mirrors { get; set; } = new();
    /// <summary>Gewünschte Bildwiederholrate (Hz) je Monitor-ID; fehlt ein Monitor = automatisch.
    /// Bei Spiegel-Gruppen steht nur der Auflösungs-Monitor drin.</summary>
    public Dictionary<string, int> RefreshRates { get; set; } = new();

    public Profile Duplicate(string newName) => new()
    {
        Name = newName,
        Monitors = new List<string>(Monitors),
        Primary = Primary,
        Fixed = Fixed?.Clone(),
        Hotkey = 0,
        Mirrors = Mirrors.Select(m => m.Clone()).ToList(),
        RefreshRates = new Dictionary<string, int>(RefreshRates, StringComparer.OrdinalIgnoreCase),
    };
}

public sealed class KnownMonitor
{
    public string Id { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public uint OutputTechnology { get; set; }
    public uint ConnectorInstance { get; set; }
    public string? Alias { get; set; }
    public DateTime LastSeen { get; set; }
    /// <summary>Unterstützte Bildwiederholraten je Auflösung („1920x1080“ → [60, 144]), gelernt, solange der Monitor an war.</summary>
    public Dictionary<string, List<int>> RefreshRates { get; set; } = new();
}

public sealed class AppSettings
{
    public bool ConfirmAfterSwitch { get; set; } = true;
    public int ConfirmSeconds { get; set; } = 15;
    public bool HotkeysEnabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
}

public sealed class AppData
{
    public List<Profile> Profiles { get; set; } = new();
    public List<KnownMonitor> Monitors { get; set; } = new();
    /// <summary>Vorlage: Anordnung mit allen Monitoren an. Daraus werden Profile abgeleitet.</summary>
    public SnapshotDto? Master { get; set; }
    public AppSettings Settings { get; set; } = new();
    public bool FirstRunDone { get; set; }
}

public static class Store
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AleVista");
    static string FilePath => Path.Combine(Folder, "data.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options) ?? new AppData();
        }
        catch
        {
            try { File.Copy(FilePath, FilePath + ".defekt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
        }
        return new AppData();
    }

    public static void Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* Speichern ist best effort */ }
    }
}
