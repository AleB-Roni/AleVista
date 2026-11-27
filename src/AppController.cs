namespace AleVista;

/// <param name="Group">0 = dieser PC, 1… = andere PCs (werden in der Vorschau anders eingefärbt).</param>
public sealed record PreviewItem(Rectangle Bounds, string Label, int Number, bool Enabled, bool Primary, int Group = 0);

/// <summary>Vorschau, ggf. aus mehreren PCs zusammengesetzt (Gruppenname je PC).</summary>
public sealed class CombinedPreview
{
    public List<PreviewItem> Items { get; } = new();
    public Dictionary<int, string> Groups { get; } = new();
}

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

    /// <summary>Ist ein anderer PC gerade verbunden? (wird vom Hauptfenster gesetzt)</summary>
    public Func<string, bool> IsPeerOnline { get; set; } = _ => false;

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
        if (p.KeepLocal)
            return ApplyResult.Fail("Dieses Profil schaltet nur andere PCs um – das geht nur über das laufende AleVista (Fenster, Tray oder Tastenkürzel).");
        var s = BuildSnapshot(p);
        if (s == null)
            return ApplyResult.Fail("Für dieses Profil fehlt die Vorlage. Bitte einmal „Alle aktivieren & einlesen“ ausführen.");
        return DisplayManager.Apply(s);
    }

    public bool IsProfileActive(Profile p)
    {
        bool any = false;
        if (!p.KeepLocal)
        {
            if (Current == null || Current.Count == 0 || !Current.MonitorSet().SetEquals(p.Monitors)) return false;
            any = true;
        }
        foreach (var r in p.Remote)
        {
            var peer = Peer(r.PeerId);
            if (peer?.Displays == null || !IsPeerOnline(r.PeerId)) return false;
            var active = new HashSet<string>(peer.Displays.Monitors.Where(m => m.Active).Select(m => m.Id), Cmp);
            if (!active.SetEquals(r.Monitors)) return false;
            any = true;
        }
        return any;
    }

    // ---------- Andere PCs (gemeinsame Profile) ----------

    public SharePeer? Peer(string id) => Data.Share.Peers.FirstOrDefault(p => p.Id == id);

    /// <summary>Andere PCs, deren Monitore bekannt sind.</summary>
    public List<SharePeer> PeersWithDisplays() => Data.Share.Peers.Where(p => p.Displays != null && p.Displays.Monitors.Count > 0).ToList();

    /// <summary>Monitore dieses PCs für andere PCs beschreiben.</summary>
    public RemoteDisplays ExportDisplays()
    {
        var d = new RemoteDisplays { HasMaster = Master != null };
        var cur = CurrentLayout().ToDictionary(c => c.Id, c => c, Cmp);
        foreach (var id in OrderedMonitors())
        {
            var m = new RemoteMonitor
            {
                Id = id,
                Name = NameOf(id),
                Tech = TechOf(id),
                Number = NumberOf(id),
                Active = IsActive(id),
                Connected = IsConnected(id),
            };
            int mi = Master?.IndexOf(id) ?? -1;
            var mr = mi >= 0 ? Master!.SourceRect(mi) : null;
            if (mr != null)
            {
                m.InMaster = true;
                m.X = mr.Value.X; m.Y = mr.Value.Y; m.W = mr.Value.Width; m.H = mr.Value.Height;
            }
            if (cur.TryGetValue(id, out var c))
            {
                m.InCurrent = true;
                m.CX = c.Rect.X; m.CY = c.Rect.Y; m.CW = c.Rect.Width; m.CH = c.Rect.Height;
                m.CurrentPrimary = c.Primary;
            }
            d.Monitors.Add(m);
        }
        return d;
    }

    /// <summary>Auftrag eines anderen PCs: genau diese Monitore einschalten (aus der Vorlage abgeleitet).</summary>
    public ApplyResult ApplyMonitors(List<string> monitors, string? primary)
    {
        var known = monitors.Where(InMaster).Distinct(Cmp).ToList();
        if (known.Count == 0) return ApplyResult.Fail("Keiner der gewünschten Monitore ist hier in der Vorlage. Bitte dort einmal „Alle aktivieren & einlesen“.");
        return Apply(new Profile { Name = "Gemeinsames Profil", Monitors = known, Primary = primary });
    }

    static List<PreviewItem> RemoteItems(RemoteDisplays d, HashSet<string>? on, int group)
    {
        var items = new List<PreviewItem>();
        foreach (var m in d.Monitors)
        {
            if (on == null)
            {
                if (m.InCurrent && m.CW > 0)
                    items.Add(new PreviewItem(m.CurrentRect, m.Name, m.Number, m.Active, m.CurrentPrimary, group));
            }
            else if (m.InMaster && m.W > 0)
                items.Add(new PreviewItem(m.MasterRect, m.Name, m.Number, on.Contains(m.Id), false, group));
        }
        return items;
    }

    /// <summary>Setzt die Gruppen nebeneinander (andere PCs je nach Anordnung links oder rechts von diesem PC).</summary>
    static void Append(CombinedPreview cp, List<PreviewItem> items, int group, string name, bool left)
    {
        if (items.Count == 0) return;
        cp.Groups[group] = name;
        if (cp.Items.Count == 0) { cp.Items.AddRange(items); return; }
        var u = Union(cp.Items);
        var r = Union(items);
        int gap = Math.Max(160, Math.Max(u.Height, r.Height) / 5);
        int dx = left ? u.Left - gap - r.Right : u.Right + gap - r.Left;
        int dy = u.Top - r.Top;
        foreach (var it in items)
        {
            var b = it.Bounds;
            b.Offset(dx, dy);
            cp.Items.Add(it with { Bounds = b });
        }
    }

    static Rectangle Union(List<PreviewItem> items)
    {
        var u = items[0].Bounds;
        foreach (var i in items) u = Rectangle.Union(u, i.Bounds);
        return u;
    }

    /// <summary>Vorschau eines (ggf. gemeinsamen) Profils über alle PCs.</summary>
    public CombinedPreview CombinedPreviewFor(Profile p)
    {
        var cp = new CombinedPreview();
        var local = PreviewFor(p);
        if (p.KeepLocal) local = local.Select(i => i with { Enabled = false, Primary = false }).ToList();
        int g = 1;
        bool remote = false;
        var parts = new List<(List<PreviewItem>, int, string, bool)>();
        foreach (var r in p.Remote)
        {
            var peer = Peer(r.PeerId);
            if (peer?.Displays == null) continue;
            var items = RemoteItems(peer.Displays, new HashSet<string>(r.Monitors, Cmp), g);
            parts.Add((items, g, peer.Name, peer.X < 0));
            remote = true;
            g++;
        }
        Append(cp, local, 0, p.KeepLocal ? "Dieser PC (unverändert)" : "Dieser PC", false);
        foreach (var (items, grp, name, left) in parts) Append(cp, items, grp, name, left);
        if (!remote) cp.Groups.Clear();
        return cp;
    }

    /// <summary>Aktuelle Anordnung dieses PCs und aller verbundenen PCs.</summary>
    public CombinedPreview CombinedCurrent()
    {
        var cp = new CombinedPreview();
        Append(cp, PreviewCurrent(), 0, "Dieser PC", false);
        int g = 1;
        foreach (var peer in PeersWithDisplays())
        {
            if (!IsPeerOnline(peer.Id)) continue;
            Append(cp, RemoteItems(peer.Displays!, null, g), g, peer.Name, peer.X < 0);
            g++;
        }
        if (g == 1) cp.Groups.Clear();
        return cp;
    }

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

    public List<PreviewItem> PreviewCurrent() =>
        CurrentLayout().Select(c => new PreviewItem(c.Rect, NameOf(c.Id), NumberOf(c.Id), c.Enabled, c.Primary)).ToList();

    List<(string Id, Rectangle Rect, bool Enabled, bool Primary)> CurrentLayout()
    {
        var items = new List<(string, Rectangle, bool, bool)>();
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
            if (items.Any(x => Cmp.Equals(x.Item1, id))) continue;
            items.Add((id, r.Value, active.Contains(id), Cmp.Equals(id, prim)));
        }
        return items;
    }
}
