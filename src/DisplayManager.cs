using System.ComponentModel;
using System.Runtime.InteropServices;
using static AleVista.Native;

namespace AleVista;

/// <summary>Ein angeschlossener Monitor (Ziel/Target) mit stabiler ID.</summary>
public sealed class MonitorDesc
{
    public string Id { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public uint OutputTechnology { get; init; }
    public uint ConnectorInstance { get; init; }
    internal LUID Adapter { get; init; }
    public uint TargetId { get; init; }
}

public sealed class ApplyResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public List<string> Missing { get; init; } = new();

    public static ApplyResult Fail(string msg, List<string>? missing = null) =>
        new() { Ok = false, Message = msg, Missing = missing ?? new() };
}

/// <summary>Gespeicherte Anzeige-Konfiguration (Pfade + Modi) wie von QueryDisplayConfig geliefert.</summary>
public sealed class DisplaySnapshot
{
    internal DISPLAYCONFIG_PATH_INFO[] Paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
    internal DISPLAYCONFIG_MODE_INFO[] Modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();
    /// <summary>Monitor-ID je Pfad (parallel zu Paths).</summary>
    public string[] MonitorIds = Array.Empty<string>();
    /// <summary>LUID-Schlüssel → Geräte-Pfad des Grafikadapters (LUIDs ändern sich nach Neustart).</summary>
    public Dictionary<string, string> Adapters = new(StringComparer.OrdinalIgnoreCase);

    public int Count => Paths.Length;

    public DisplaySnapshot Clone() => new()
    {
        Paths = (DISPLAYCONFIG_PATH_INFO[])Paths.Clone(),
        Modes = (DISPLAYCONFIG_MODE_INFO[])Modes.Clone(),
        MonitorIds = (string[])MonitorIds.Clone(),
        Adapters = new Dictionary<string, string>(Adapters, StringComparer.OrdinalIgnoreCase),
    };

    public HashSet<string> MonitorSet() => new(MonitorIds, StringComparer.OrdinalIgnoreCase);

    internal uint SourceModeIndex(int pathIndex)
    {
        var idx = Paths[pathIndex].sourceInfo.modeInfoIdx;
        if (idx >= (uint)Modes.Length) return DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        return Modes[idx].infoType == MODE_INFO_TYPE_SOURCE ? idx : DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
    }

    public Rectangle? SourceRect(int pathIndex)
    {
        var idx = SourceModeIndex(pathIndex);
        if (idx == DISPLAYCONFIG_PATH_MODE_IDX_INVALID) return null;
        var s = Modes[idx].sourceMode;
        return new Rectangle(s.position.x, s.position.y, (int)s.width, (int)s.height);
    }

    public string? PrimaryMonitorId()
    {
        for (int i = 0; i < Paths.Length; i++)
        {
            var r = SourceRect(i);
            if (r is { X: 0, Y: 0 }) return MonitorIds[i];
        }
        return null;
    }

    public int IndexOf(string monitorId)
    {
        for (int i = 0; i < MonitorIds.Length; i++)
            if (string.Equals(MonitorIds[i], monitorId, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Neuer Snapshot, der nur die Pfade der gewünschten Monitore enthält (Modus-Indizes werden neu nummeriert).</summary>
    public DisplaySnapshot Filter(Func<string, bool> keep)
    {
        var paths = new List<DISPLAYCONFIG_PATH_INFO>();
        var ids = new List<string>();
        var modes = new List<DISPLAYCONFIG_MODE_INFO>();
        var map = new Dictionary<uint, uint>();

        uint Remap(uint idx)
        {
            if (idx >= (uint)Modes.Length) return DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            if (!map.TryGetValue(idx, out var n))
            {
                n = (uint)modes.Count;
                modes.Add(Modes[idx]);
                map[idx] = n;
            }
            return n;
        }

        for (int i = 0; i < Paths.Length; i++)
        {
            if (!keep(MonitorIds[i])) continue;
            var p = Paths[i];
            p.sourceInfo.modeInfoIdx = Remap(p.sourceInfo.modeInfoIdx);
            p.targetInfo.modeInfoIdx = Remap(p.targetInfo.modeInfoIdx);
            paths.Add(p);
            ids.Add(MonitorIds[i]);
        }

        return new DisplaySnapshot
        {
            Paths = paths.ToArray(),
            Modes = modes.ToArray(),
            MonitorIds = ids.ToArray(),
            Adapters = new Dictionary<string, string>(Adapters, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Kurzer Text, der Monitore + Positionen beschreibt (zum Vergleichen).</summary>
    public string LayoutSignature()
    {
        var parts = new List<string>();
        for (int i = 0; i < Paths.Length; i++)
        {
            var r = SourceRect(i);
            parts.Add($"{MonitorIds[i].ToLowerInvariant()}@{r?.X},{r?.Y},{r?.Width}x{r?.Height}");
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    public SnapshotDto ToDto() => new()
    {
        Paths = Convert.ToBase64String(MemoryMarshal.AsBytes(Paths.AsSpan())),
        Modes = Convert.ToBase64String(MemoryMarshal.AsBytes(Modes.AsSpan())),
        MonitorIds = MonitorIds.ToList(),
        Adapters = new Dictionary<string, string>(Adapters),
    };

    public static DisplaySnapshot FromDto(SnapshotDto dto)
    {
        var s = new DisplaySnapshot
        {
            Paths = MemoryMarshal.Cast<byte, DISPLAYCONFIG_PATH_INFO>(Convert.FromBase64String(dto.Paths).AsSpan()).ToArray(),
            Modes = MemoryMarshal.Cast<byte, DISPLAYCONFIG_MODE_INFO>(Convert.FromBase64String(dto.Modes).AsSpan()).ToArray(),
            MonitorIds = dto.MonitorIds.ToArray(),
            Adapters = new Dictionary<string, string>(dto.Adapters ?? new(), StringComparer.OrdinalIgnoreCase),
        };
        if (s.MonitorIds.Length != s.Paths.Length)
            throw new InvalidDataException("Gespeicherte Anordnung ist beschädigt.");
        return s;
    }
}

/// <summary>Layout-Hilfen: Lücken schließen und Hauptmonitor auf (0,0) legen.</summary>
public static class Layout
{
    public static void Normalize(DisplaySnapshot s, string? primaryId)
    {
        Compact(s, horizontal: true);
        Compact(s, horizontal: false);
        SetOrigin(s, primaryId);
    }

    static List<uint> SourceModes(DisplaySnapshot s)
    {
        var list = new List<uint>();
        for (int i = 0; i < s.Count; i++)
        {
            var idx = s.SourceModeIndex(i);
            if (idx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && !list.Contains(idx)) list.Add(idx);
        }
        return list;
    }

    /// <summary>Entfernt Lücken, die durch abgeschaltete Monitore entstehen (z. B. mittlerer Monitor aus).</summary>
    static void Compact(DisplaySnapshot s, bool horizontal)
    {
        var idxs = SourceModes(s);
        if (idxs.Count < 2) return;

        int Pos(uint i) => horizontal ? s.Modes[i].sourceMode.position.x : s.Modes[i].sourceMode.position.y;
        int Len(uint i) => (int)(horizontal ? s.Modes[i].sourceMode.width : s.Modes[i].sourceMode.height);

        var ordered = idxs.OrderBy(Pos).ToList();
        long maxEnd = long.MinValue;
        long shift = 0;
        var newPos = new Dictionary<uint, int>();
        foreach (var i in ordered)
        {
            long p = Pos(i);
            if (maxEnd != long.MinValue && p > maxEnd) shift += p - maxEnd;
            newPos[i] = (int)(p - shift);
            maxEnd = Math.Max(maxEnd, p + Len(i));
        }
        foreach (var (i, v) in newPos)
        {
            if (horizontal) s.Modes[i].sourceMode.position.x = v;
            else s.Modes[i].sourceMode.position.y = v;
        }
    }

    /// <summary>Verschiebt alles so, dass der gewünschte (oder ein sinnvoller) Monitor bei (0,0) liegt = Hauptmonitor.</summary>
    public static void SetOrigin(DisplaySnapshot s, string? primaryId)
    {
        var idxs = SourceModes(s);
        if (idxs.Count == 0) return;

        uint? target = null;
        if (!string.IsNullOrEmpty(primaryId))
        {
            int pi = s.IndexOf(primaryId);
            if (pi >= 0)
            {
                var idx = s.SourceModeIndex(pi);
                if (idx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID) target = idx;
            }
        }
        if (target == null)
        {
            if (idxs.Any(i => s.Modes[i].sourceMode.position.x == 0 && s.Modes[i].sourceMode.position.y == 0)) return;
            target = idxs.OrderBy(i => Math.Abs((long)s.Modes[i].sourceMode.position.x) + Math.Abs((long)s.Modes[i].sourceMode.position.y)).First();
        }

        int dx = s.Modes[target.Value].sourceMode.position.x;
        int dy = s.Modes[target.Value].sourceMode.position.y;
        if (dx == 0 && dy == 0) return;
        foreach (var i in idxs)
        {
            s.Modes[i].sourceMode.position.x -= dx;
            s.Modes[i].sourceMode.position.y -= dy;
        }
    }
}

public static class DisplayManager
{
    static (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes) Query(uint flags)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            int err = GetDisplayConfigBufferSizes(flags, out uint np, out uint nm);
            if (err != ERROR_SUCCESS) throw new Win32Exception(err);
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            err = QueryDisplayConfig(flags, ref np, paths, ref nm, modes, IntPtr.Zero);
            if (err == ERROR_INSUFFICIENT_BUFFER) continue;
            if (err != ERROR_SUCCESS) throw new Win32Exception(err);
            Array.Resize(ref paths, (int)np);
            Array.Resize(ref modes, (int)nm);
            return (paths, modes);
        }
        throw new Win32Exception(ERROR_INSUFFICIENT_BUFFER);
    }

    internal static string LuidKey(LUID l) => $"{(uint)l.HighPart:X8}{l.LowPart:X8}";
    static bool Same(LUID a, LUID b) => a.LowPart == b.LowPart && a.HighPart == b.HighPart;

    static DISPLAYCONFIG_TARGET_DEVICE_NAME? GetTargetName(LUID adapter, uint targetId)
    {
        var req = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapter,
                id = targetId,
            },
            monitorFriendlyDeviceName = "",
            monitorDevicePath = "",
        };
        return DisplayConfigGetDeviceInfo(ref req) == ERROR_SUCCESS ? req : null;
    }

    static string? GetAdapterPath(LUID adapter)
    {
        var req = new DISPLAYCONFIG_ADAPTER_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DEVICE_INFO_GET_ADAPTER_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_ADAPTER_NAME>(),
                adapterId = adapter,
            },
            adapterDevicePath = "",
        };
        return DisplayConfigGetDeviceInfo(ref req) == ERROR_SUCCESS && !string.IsNullOrEmpty(req.adapterDevicePath)
            ? req.adapterDevicePath : null;
    }

    /// <summary>GDI-Gerätename (z. B. \\.\DISPLAY2) eines aktiven Pfads – passt zu Screen.DeviceName.</summary>
    public static string? GetGdiName(DisplaySnapshot s, int pathIndex)
    {
        var src = s.Paths[pathIndex].sourceInfo;
        var req = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DEVICE_INFO_GET_SOURCE_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = src.adapterId,
                id = src.id,
            },
            viewGdiDeviceName = "",
        };
        return DisplayConfigGetDeviceInfo(ref req) == ERROR_SUCCESS ? req.viewGdiDeviceName : null;
    }

    static string MonitorId(LUID adapter, uint targetId, DISPLAYCONFIG_TARGET_DEVICE_NAME? name)
    {
        var path = name?.monitorDevicePath;
        return string.IsNullOrWhiteSpace(path) ? $"target:{LuidKey(adapter)}:{targetId}" : path!;
    }

    /// <summary>Aktuell aktive Anzeige-Konfiguration auslesen.</summary>
    public static DisplaySnapshot CaptureActive()
    {
        var (paths, modes) = Query(QDC_ONLY_ACTIVE_PATHS);
        var ids = new string[paths.Length];
        var adapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddAdapter(LUID l)
        {
            var key = LuidKey(l);
            if (adapters.ContainsKey(key)) return;
            var p = GetAdapterPath(l);
            if (p != null) adapters[key] = p;
        }

        for (int i = 0; i < paths.Length; i++)
        {
            var t = paths[i].targetInfo;
            ids[i] = MonitorId(t.adapterId, t.id, GetTargetName(t.adapterId, t.id));
            AddAdapter(t.adapterId);
            AddAdapter(paths[i].sourceInfo.adapterId);
        }
        foreach (var m in modes) AddAdapter(m.adapterId);

        return new DisplaySnapshot { Paths = paths, Modes = modes, MonitorIds = ids, Adapters = adapters };
    }

    /// <summary>Alle physisch angeschlossenen Monitore (aktiv oder nicht).</summary>
    public static List<MonitorDesc> GetConnectedMonitors()
    {
        var (paths, _) = Query(QDC_ALL_PATHS);
        var result = new List<MonitorDesc>();
        var seen = new HashSet<string>();
        foreach (var p in paths)
        {
            var t = p.targetInfo;
            if (t.targetAvailable == 0) continue;
            var key = LuidKey(t.adapterId) + ":" + t.id;
            if (!seen.Add(key)) continue;
            var name = GetTargetName(t.adapterId, t.id);
            var id = MonitorId(t.adapterId, t.id, name);
            if (result.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new MonitorDesc
            {
                Id = id,
                FriendlyName = name?.monitorFriendlyDeviceName?.Trim() ?? "",
                OutputTechnology = name?.outputTechnology ?? t.outputTechnology,
                ConnectorInstance = name?.connectorInstance ?? 0,
                Adapter = t.adapterId,
                TargetId = t.id,
            });
        }
        return result;
    }

    /// <summary>Schaltet alle angeschlossenen Monitore im Modus „Erweitern“ ein.</summary>
    public static int ActivateAll() =>
        SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_EXTEND | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);

    /// <summary>Wendet eine gespeicherte Anordnung an. Nicht angeschlossene Monitore werden übersprungen.</summary>
    public static ApplyResult Apply(DisplaySnapshot snapshot)
    {
        List<MonitorDesc> connected;
        try { connected = GetConnectedMonitors(); }
        catch (Exception ex) { return ApplyResult.Fail("Monitore konnten nicht abgefragt werden: " + ex.Message); }

        var byId = new Dictionary<string, MonitorDesc>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in connected) byId.TryAdd(m.Id, m);

        var missing = snapshot.MonitorIds.Where(id => !byId.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var s = missing.Count == 0 ? snapshot.Clone() : snapshot.Filter(id => byId.ContainsKey(id));
        if (s.Count == 0)
            return ApplyResult.Fail("Keiner der Monitore aus diesem Profil ist gerade angeschlossen.", missing);
        if (missing.Count > 0)
        {
            var prim = snapshot.PrimaryMonitorId();
            Layout.Normalize(s, prim != null && byId.ContainsKey(prim) ? prim : null);
        }

        // Grafikadapter-LUIDs auf die aktuellen Werte umbiegen (ändern sich bei jedem Neustart).
        var currentAdapters = new Dictionary<string, LUID>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in connected)
        {
            var p = GetAdapterPath(m.Adapter);
            if (p != null) currentAdapters.TryAdd(p, m.Adapter);
        }
        LUID MapLuid(LUID old) =>
            s.Adapters.TryGetValue(LuidKey(old), out var devPath) && currentAdapters.TryGetValue(devPath, out var nl) ? nl : old;

        var touchedModes = new HashSet<uint>();
        for (int i = 0; i < s.Count; i++)
        {
            ref var p = ref s.Paths[i];
            var mon = byId[s.MonitorIds[i]];
            bool sourceOnTargetAdapter = Same(p.sourceInfo.adapterId, p.targetInfo.adapterId);

            p.sourceInfo.adapterId = sourceOnTargetAdapter ? mon.Adapter : MapLuid(p.sourceInfo.adapterId);
            p.targetInfo.adapterId = mon.Adapter;
            p.targetInfo.id = mon.TargetId;
            p.flags |= DISPLAYCONFIG_PATH_ACTIVE;

            var ti = p.targetInfo.modeInfoIdx;
            if (ti < (uint)s.Modes.Length && s.Modes[ti].infoType == MODE_INFO_TYPE_TARGET)
            {
                s.Modes[ti].adapterId = mon.Adapter;
                s.Modes[ti].id = mon.TargetId;
                touchedModes.Add(ti);
            }
            var si = p.sourceInfo.modeInfoIdx;
            if (si < (uint)s.Modes.Length && s.Modes[si].infoType == MODE_INFO_TYPE_SOURCE)
            {
                s.Modes[si].adapterId = p.sourceInfo.adapterId;
                touchedModes.Add(si);
            }
        }
        for (uint m = 0; m < s.Modes.Length; m++)
            if (!touchedModes.Contains(m)) s.Modes[m].adapterId = MapLuid(s.Modes[m].adapterId);

        int err = SetDisplayConfig((uint)s.Paths.Length, s.Paths, (uint)s.Modes.Length, s.Modes,
            SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES);

        if (err != ERROR_SUCCESS)
        {
            // Fallback: nur die Topologie vorgeben, Auflösungen/Positionen nimmt Windows aus seiner Datenbank.
            var topo = (DISPLAYCONFIG_PATH_INFO[])s.Paths.Clone();
            for (int i = 0; i < topo.Length; i++)
            {
                topo[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                topo[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            }
            int err2 = SetDisplayConfig((uint)topo.Length, topo, 0, null,
                SDC_APPLY | SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES);
            if (err2 != ERROR_SUCCESS)
                return ApplyResult.Fail(ErrorText(err), missing);
            return new ApplyResult
            {
                Ok = true,
                Missing = missing,
                Message = "Umgeschaltet (Anordnung von Windows übernommen)." + MissingNote(missing.Count),
            };
        }

        return new ApplyResult { Ok = true, Missing = missing, Message = "Umgeschaltet." + MissingNote(missing.Count) };
    }

    static string MissingNote(int n) => n switch
    {
        0 => "",
        1 => " Ein Monitor aus dem Profil ist nicht angeschlossen und wurde übersprungen.",
        _ => $" {n} Monitore aus dem Profil sind nicht angeschlossen und wurden übersprungen.",
    };

    static string ErrorText(int err) => err switch
    {
        5 => "Windows verweigert die Änderung (Fehler 5). In einer Remotedesktop-Sitzung oder bei gesperrtem Bildschirm lassen sich Monitore nicht umschalten.",
        87 => "Windows hat die Anordnung als ungültig abgelehnt (Fehler 87). Tipp: „Alle aktivieren & einlesen“ ausführen und das Profil neu speichern.",
        1610 => "Windows kann diese Kombination nicht darstellen (Fehler 1610) – evtl. unterstützt die Grafikkarte nicht so viele Monitore gleichzeitig.",
        _ => $"Windows hat die Anordnung abgelehnt (Fehler {err}).",
    };

    public static string TechName(uint tech) => tech switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        10 => "DisplayPort",
        11 => "DisplayPort (intern)",
        12 or 13 => "UDI",
        15 => "Miracast",
        16 or 17 => "Indirekt",
        18 => "USB-C / DisplayPort",
        0x80000000 => "Intern",
        _ => "Anschluss",
    };
}
