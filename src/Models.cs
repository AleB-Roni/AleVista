using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// <summary>Gemeinsames Profil: Monitore dieses PCs nicht ändern (nur die anderen PCs umschalten).</summary>
    public bool KeepLocal { get; set; }
    /// <summary>Gemeinsames Profil: welche Monitore auf anderen PCs (Maus &amp; Tastatur teilen) an sein sollen.</summary>
    public List<RemoteSelection> Remote { get; set; } = new();

    /// <summary>Arbeitskopie (gleiche ID) für den Editor.</summary>
    public Profile Copy()
    {
        var c = Duplicate(Name);
        c.Id = Id;
        c.Hotkey = Hotkey;
        return c;
    }

    public void CopyFrom(Profile o)
    {
        Name = o.Name;
        Monitors = new List<string>(o.Monitors);
        Primary = o.Primary;
        Fixed = o.Fixed?.Clone();
        Hotkey = o.Hotkey;
        KeepLocal = o.KeepLocal;
        Remote = o.Remote.Select(r => r.Clone()).ToList();
    }

    public Profile Duplicate(string newName) => new()
    {
        Name = newName,
        Monitors = new List<string>(Monitors),
        Primary = Primary,
        Fixed = Fixed?.Clone(),
        Hotkey = 0,
        KeepLocal = KeepLocal,
        Remote = Remote.Select(r => r.Clone()).ToList(),
    };
}

/// <summary>Monitorauswahl für einen anderen PC innerhalb eines gemeinsamen Profils.</summary>
public sealed class RemoteSelection
{
    public string PeerId { get; set; } = "";
    public List<string> Monitors { get; set; } = new();
    public string? Primary { get; set; }

    public RemoteSelection Clone() => new() { PeerId = PeerId, Monitors = new List<string>(Monitors), Primary = Primary };
}

/// <summary>Monitor eines anderen PCs (von dort übermittelt, für Anzeige und gemeinsame Profile).</summary>
public sealed class RemoteMonitor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tech { get; set; } = "";
    public int Number { get; set; }
    public bool Active { get; set; }
    public bool Connected { get; set; }
    /// <summary>In der Vorlage (alle Monitore an) enthalten → kann in Profilen genutzt werden.</summary>
    public bool InMaster { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    /// <summary>Teil der aktuellen Anzeige-Vorschau (CX..CH = Position darin).</summary>
    public bool InCurrent { get; set; }
    public int CX { get; set; }
    public int CY { get; set; }
    public int CW { get; set; }
    public int CH { get; set; }
    public bool CurrentPrimary { get; set; }
    /// <summary>Hersteller/Modell/Seriennummer aus der EDID – gleich auf allen PCs, an denen der Bildschirm hängt.</summary>
    public string? PhysicalKey { get; set; }

    [JsonIgnore] public Rectangle MasterRect => new(X, Y, W, H);
    [JsonIgnore] public Rectangle CurrentRect => new(CX, CY, CW, CH);
}

public sealed class RemoteDisplays
{
    public List<RemoteMonitor> Monitors { get; set; } = new();
    public bool HasMaster { get; set; }
}

public sealed class KnownMonitor
{
    public string Id { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public uint OutputTechnology { get; set; }
    public uint ConnectorInstance { get; set; }
    public string? Alias { get; set; }
    public DateTime LastSeen { get; set; }
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
    /// <summary>Maus &amp; Tastatur mit anderen PCs teilen.</summary>
    public ShareSettings Share { get; set; } = new();
}

/// <summary>Ein Bildschirm in physischen Desktop-Pixeln (Koordinaten des jeweiligen PCs).</summary>
public sealed class ShareScreen
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public bool Primary { get; set; }

    [JsonIgnore] public Rectangle Rect => new(X, Y, W, H);
}

/// <summary>Ein anderer PC, mit dem Maus und Tastatur geteilt werden.</summary>
public sealed class SharePeer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Zuletzt erfolgreich genutzte Adresse (Name oder IP).</summary>
    public string Host { get; set; } = "";
    /// <summary>Position des PCs in der gemeinsamen Anordnung (relativ zu diesem PC, in Pixeln).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public List<ShareScreen> Screens { get; set; } = new();
    public DateTime LastSeen { get; set; }
    /// <summary>Zuletzt übermittelte Monitore dieses PCs (bleibt gespeichert, auch wenn er offline ist).</summary>
    public RemoteDisplays? Displays { get; set; }
    /// <summary>Manuelle Zuordnung: Monitor-ID dort → Monitor-ID hier ("" = eigener Bildschirm, nur dort).</summary>
    public Dictionary<string, string> Links { get; set; } = new();
}

public sealed class ShareSettings
{
    public bool Enabled { get; set; }
    /// <summary>Gemeinsamer Verbindungsschlüssel – muss auf allen PCs gleich sein.</summary>
    public string Key { get; set; } = "";
    public string MachineId { get; set; } = Guid.NewGuid().ToString("N");
    public int Port { get; set; } = 24860;
    public bool EdgeSwitching { get; set; } = true;
    public bool BlockCorners { get; set; } = true;
    public bool ClipboardSync { get; set; } = true;
    public bool HideCursor { get; set; } = true;
    public bool AutoDiscover { get; set; } = true;
    public List<SharePeer> Peers { get; set; } = new();
    /// <summary>Entfernte PCs, die nicht automatisch wieder hinzugefügt werden.</summary>
    public List<string> IgnoredIds { get; set; } = new();
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
