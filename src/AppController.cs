namespace AleVista;

public sealed record PreviewItem(Rectangle Bounds, string Label, int Number, bool Enabled, bool Primary);

/// <summary>Logik zwischen Windows-Anzeige-API, gespeicherten Daten und Oberfläche.</summary>
public sealed class AppController
{
    static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    public AppData Data { get; }
    public DisplaySnapshot? Master { get; private set; }
    public DisplaySnapshot? Current { get; private set; }
    public List<MonitorDesc> Connected { get; private set; } = new();
    public string? LastError { get; private set; }

    public event Action? Changed;

    public AppController(AppData data)
    {
        Data = data;
        try { Master = data.Master != null ? DisplaySnapshot.FromDto(data.Master) : null; }
        catch { Master = null; data.Master = null; }
    }

    public void Save() => Store.Save(Data);
    public void NotifyChanged() { Save(); Changed?.Invoke(); }

    /// <summary>Liest den aktuellen Zustand aus Windows und lernt die Vorlage dazu.</summary>
    public void Refresh()
    {
        try
        {
            Current = DisplayManager.CaptureActive();
            Connected = DisplayManager.GetConnectedMonitors();
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = "Anzeigen konnten nicht ausgelesen werden: " + ex.Message;
        }

        foreach (var m in Connected)
        {
            var k = Data.Monitors.FirstOrDefault(x => Cmp.Equals(x.Id, m.Id));
            if (k == null) { k = new KnownMonitor { Id = m.Id }; Data.Monitors.Add(k); }
            if (!string.IsNullOrWhiteSpace(m.FriendlyName)) k.FriendlyName = m.FriendlyName;
            k.OutputTechnology = m.OutputTechnology;
            k.ConnectorInstance = m.ConnectorInstance;
            k.LastSeen = DateTime.Now;
        }

        // Vorlage lernen: wenn gerade mindestens alle Monitore der Vorlage an sind, ist das die neue Vorlage
        // (so werden auch Umsortierungen in den Windows-Anzeigeeinstellungen übernommen).
        if (Current != null && Current.Count > 0)
        {
            if (Master == null || Current.MonitorSet().IsSupersetOf(Master.MonitorSet()))
            {
                if (Master == null || Master.LayoutSignature() != Current.LayoutSignature())
                {
                    Master = Current.Clone();
                    Data.Master = Master.ToDto();
                }
            }
        }

        if (!Data.FirstRunDone && Master != null)
        {
            Data.FirstRunDone = true;
            if (Data.Profiles.Count == 0)
                Data.Profiles.Add(new Profile { Name = "Alle Monitore", Monitors = Master.MonitorIds.Distinct(Cmp).ToList() });
        }

        Save();
        Changed?.Invoke();
    }

    /// <summary>Aktuelle Anordnung als Vorlage übernehmen (nach „Alle aktivieren“).</summary>
    public void SetMasterFromCurrent()
    {
        if (Current == null || Current.Count == 0) return;
        Master = Current.Clone();
        Data.Master = Master.ToDto();
        Save();
        Changed?.Invoke();
    }

    // ---------- Monitore ----------

    public KnownMonitor? Known(string id) => Data.Monitors.FirstOrDefault(m => Cmp.Equals(m.Id, id));

    public string NameOf(string id)
    {
        var k = Known(id);
        if (!string.IsNullOrWhiteSpace(k?.Alias)) return k!.Alias!;
        if (!string.IsNullOrWhiteSpace(k?.FriendlyName)) return k!.FriendlyName;
        return "Monitor";
    }

    public string TechOf(string id)
    {
        var k = Known(id);
        if (k == null) return "";
        var tech = DisplayManager.TechName(k.OutputTechnology);
        var sameTech = Data.Monitors.Count(m => m.OutputTechnology == k.OutputTechnology) > 1;
        return sameTech ? $"{tech} {k.ConnectorInstance + 1}" : tech;
    }

    public bool IsConnected(string id) => Connected.Any(m => Cmp.Equals(m.Id, id));
    public bool IsActive(string id) => Current?.IndexOf(id) >= 0;

    /// <summary>Monitore in fester Reihenfolge (Vorlage von links nach rechts, dann übrige angeschlossene).</summary>
    public List<string> OrderedMonitors()
    {
        var list = new List<string>();
        if (Master != null)
        {
            var order = Enumerable.Range(0, Master.Count)
                .Select(i => (id: Master.MonitorIds[i], r: Master.SourceRect(i) ?? Rectangle.Empty))
                .OrderBy(t => t.r.X).ThenBy(t => t.r.Y);
            foreach (var (id, _) in order)
                if (!list.Contains(id, Cmp)) list.Add(id);
        }
        if (Current != null)
            foreach (var id in Current.MonitorIds)
                if (!list.Contains(id, Cmp)) list.Add(id);
        foreach (var m in Connected)
            if (!list.Contains(m.Id, Cmp)) list.Add(m.Id);
        return list;
    }

    public bool InMaster(string id) => Master?.IndexOf(id) >= 0;

    public int NumberOf(string id)
    {
        var i = OrderedMonitors().FindIndex(x => Cmp.Equals(x, id));
        return i >= 0 ? i + 1 : 0;
    }

    // ---------- Profile ----------

    public DisplaySnapshot? BuildSnapshot(Profile p)
    {
        if (p.Fixed != null)
        {
            DisplaySnapshot s;
            try { s = DisplaySnapshot.FromDto(p.Fixed); } catch { return null; }
            if (!string.IsNullOrEmpty(p.Primary)) Layout.SetOrigin(s, p.Primary);
            return s;
        }
        if (Master == null) return null;
        var set = new HashSet<string>(p.Monitors, Cmp);
        var d = Master.Filter(set.Contains);
        if (d.Count == 0) return null;
        Layout.Normalize(d, p.Primary);
        return d;
    }

    public ApplyResult Apply(Profile p)
    {
        var s = BuildSnapshot(p);
        if (s == null)
            return ApplyResult.Fail("Für dieses Profil fehlt die Vorlage. Bitte einmal „Alle aktivieren & einlesen“ ausführen.");
        return DisplayManager.Apply(s);
    }

    public bool IsProfileActive(Profile p) =>
        Current != null && Current.Count > 0 && Current.MonitorSet().SetEquals(p.Monitors);

    public List<PreviewItem> PreviewFor(Profile p)
    {
        var items = new List<PreviewItem>();
        if (p.Fixed != null)
        {
            var s = BuildSnapshot(p);
            if (s == null) return items;
            var prim = s.PrimaryMonitorId();
            for (int i = 0; i < s.Count; i++)
            {
                var r = s.SourceRect(i);
                if (r == null) continue;
                var id = s.MonitorIds[i];
                items.Add(new PreviewItem(r.Value, NameOf(id), NumberOf(id), true, Cmp.Equals(id, prim)));
            }
            return items;
        }
        if (Master == null) return items;
        var built = BuildSnapshot(p);
        var primary = built?.PrimaryMonitorId();
        var set = new HashSet<string>(p.Monitors, Cmp);
        for (int i = 0; i < Master.Count; i++)
        {
            var r = Master.SourceRect(i);
            if (r == null) continue;
            var id = Master.MonitorIds[i];
            items.Add(new PreviewItem(r.Value, NameOf(id), NumberOf(id), set.Contains(id), Cmp.Equals(id, primary)));
        }
        return items;
    }

    public List<PreviewItem> PreviewCurrent()
    {
        var items = new List<PreviewItem>();
        if (Current == null) return items;
        var prim = Current.PrimaryMonitorId();
        var useMaster = Master != null && Master.MonitorSet().IsSupersetOf(Current.MonitorSet());
        var src = useMaster ? Master! : Current;
        var active = Current.MonitorSet();
        for (int i = 0; i < src.Count; i++)
        {
            var r = src.SourceRect(i);
            if (r == null) continue;
            var id = src.MonitorIds[i];
            items.Add(new PreviewItem(r.Value, NameOf(id), NumberOf(id), active.Contains(id), Cmp.Equals(id, prim)));
        }
        return items;
    }
}
