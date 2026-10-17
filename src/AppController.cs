namespace AleVista;

/// <param name="Mirror">Nummern der Monitore, die hiermit gespiegelt sind (z. B. „1“ oder „1, 3“); null = keine.</param>
public sealed record PreviewItem(Rectangle Bounds, string Label, int Number, bool Enabled, bool Primary, string? Mirror = null);

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
        LearnRefreshRates();

        // Vorlage lernen: wenn gerade mindestens alle Monitore der Vorlage an sind, ist das die neue Vorlage
        // (so werden auch Umsortierungen in den Windows-Anzeigeeinstellungen übernommen).
        // Gespiegelte Anordnungen taugen nicht als Vorlage – sonst wären alle abgeleiteten Profile auch gespiegelt.
        if (Current != null && Current.Count > 0 && !Current.HasClones())
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

    /// <summary>Merkt sich, welche Bildwiederholraten die aktiven Monitore können (nur solange sie an sind abfragbar).</summary>
    void LearnRefreshRates()
    {
        if (Current == null) return;
        var cloned = Current.CloneGroups().SelectMany(g => g).ToHashSet();
        for (int i = 0; i < Current.Count; i++)
        {
            // Bei gespiegelten Monitoren liefert Windows die Modi der gemeinsamen Quelle – nicht eindeutig.
            if (cloned.Contains(i)) continue;
            var k = Known(Current.MonitorIds[i]);
            if (k == null) continue;
            try
            {
                var gdi = DisplayManager.GetGdiName(Current, i);
                if (string.IsNullOrEmpty(gdi)) continue;
                var rates = DisplayManager.GetRefreshRates(gdi);
                if (rates.Count > 0) k.RefreshRates = rates;
            }
            catch { }
        }
    }

    /// <summary>Bekannte Bildwiederholraten eines Monitors bei einer Auflösung (leer = unbekannt).</summary>
    public List<int> RefreshRatesOf(string id, Size resolution) =>
        Known(id)?.RefreshRates.GetValueOrDefault(DisplayManager.ResolutionKey(resolution.Width, resolution.Height)) ?? new List<int>();

    /// <summary>Auflösung, die der Monitor in diesem Profil bekommt (null = unbekannt).</summary>
    public Size? ProfileResolutionOf(Profile p, string id)
    {
        var s = BuildSnapshot(p);
        int i = s?.IndexOf(id) ?? -1;
        return i >= 0 && s!.SourceRect(i) is { } r ? r.Size : null;
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

    /// <summary>Auflösung laut Vorlage (oder aktueller Anordnung), z. B. „2560 × 1440“.</summary>
    public string ResolutionOf(string id)
    {
        foreach (var s in new[] { Master, Current })
        {
            int i = s?.IndexOf(id) ?? -1;
            if (i >= 0 && s!.SourceRect(i) is { } r) return $"{r.Width} × {r.Height}";
        }
        return "";
    }

    /// <summary>Grafikkarte, an der der Monitor gerade hängt (null = nicht angeschlossen).</summary>
    public string? AdapterOf(string id)
    {
        var m = Connected.FirstOrDefault(x => Cmp.Equals(x.Id, id));
        return m == null ? null : DisplayManager.LuidKey(m.Adapter);
    }

    /// <summary>Wirksame Spiegel-Gruppen des Profils: nur Mitglieder, die im Profil an sind, mind. 2 je Gruppe.</summary>
    public List<(List<string> members, string owner)> MirrorGroupsOf(Profile p)
    {
        var set = new HashSet<string>(p.Monitors, Cmp);
        var list = new List<(List<string>, string)>();
        foreach (var g in p.Mirrors)
        {
            var present = g.Present(set.Contains);
            if (present.Count >= 2) list.Add((present, g.Owner(present)));
        }
        return list;
    }

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
            var mirrored = s.WithMirrors(p.Mirrors);
            if (mirrored != s) Layout.Normalize(mirrored, p.Primary ?? s.PrimaryMonitorId());
            else if (!string.IsNullOrEmpty(p.Primary)) Layout.SetOrigin(s, p.Primary);
            return mirrored;
        }
        if (Master == null) return null;
        var set = new HashSet<string>(p.Monitors, Cmp);
        var d = Master.Filter(set.Contains).WithMirrors(p.Mirrors);
        if (d.Count == 0) return null;
        Layout.Normalize(d, p.Primary);
        return d;
    }

    public ApplyResult Apply(Profile p)
    {
        var s = BuildSnapshot(p);
        if (s == null)
            return ApplyResult.Fail("Für dieses Profil fehlt die Vorlage. Bitte einmal „Alle aktivieren & einlesen“ ausführen.");
        var r = DisplayManager.Apply(s);
        if (!r.Ok || p.RefreshRates.Count == 0) return r;
        var on = new HashSet<string>(p.Monitors, Cmp);
        var rates = p.RefreshRates.Where(kv => on.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, Cmp);
        var note = DisplayManager.ApplyRefreshRates(rates);
        return note.Length == 0 ? r : new ApplyResult { Ok = true, Missing = r.Missing, Message = r.Message + note };
    }

    public bool IsProfileActive(Profile p)
    {
        if (Current == null || Current.Count == 0 || !Current.MonitorSet().SetEquals(p.Monitors)) return false;
        var expected = BuildSnapshot(p)?.CloneSignature() ?? "";
        return expected == Current.CloneSignature();
    }

    /// <summary>Vorschau direkt aus einem Snapshot; gespiegelte Monitore werden zu einem Feld zusammengefasst.</summary>
    List<PreviewItem> CollapsedPreview(DisplaySnapshot s)
    {
        var items = new List<PreviewItem>();
        var prim = s.PrimaryMonitorId();
        var groups = s.CloneGroups();
        var done = new HashSet<int>();
        for (int i = 0; i < s.Count; i++)
        {
            if (!done.Add(i)) continue;
            var r = s.SourceRect(i);
            if (r == null) continue;
            var members = groups.FirstOrDefault(g => g.Contains(i))?
                .OrderBy(j => NumberOf(s.MonitorIds[j])).ToList() ?? new List<int> { i };
            foreach (var j in members) done.Add(j);
            var ids = members.Select(j => s.MonitorIds[j]).ToList();
            var others = ids.Skip(1).Select(id => NumberOf(id).ToString()).ToList();
            items.Add(new PreviewItem(r.Value, string.Join(" + ", ids.Select(NameOf)), NumberOf(ids[0]), true,
                ids.Any(id => Cmp.Equals(id, prim)), others.Count > 0 ? string.Join(", ", others) : null));
        }
        return items;
    }

    public List<PreviewItem> PreviewFor(Profile p)
    {
        var items = new List<PreviewItem>();
        if (p.Fixed != null)
        {
            var s = BuildSnapshot(p);
            return s == null ? items : CollapsedPreview(s);
        }
        if (Master == null) return items;
        var built = BuildSnapshot(p);
        var primary = built?.PrimaryMonitorId();
        var set = new HashSet<string>(p.Monitors, Cmp);
        // Gespiegelte Monitore bleiben an ihrem Platz und zeigen, wessen Bild sie übernehmen.
        var mirrorOf = new Dictionary<string, string>(Cmp);
        foreach (var (members, owner) in MirrorGroupsOf(p))
            foreach (var id in members)
                if (!Cmp.Equals(id, owner)) mirrorOf[id] = NumberOf(owner).ToString();
        for (int i = 0; i < Master.Count; i++)
        {
            var r = Master.SourceRect(i);
            if (r == null) continue;
            var id = Master.MonitorIds[i];
            items.Add(new PreviewItem(r.Value, NameOf(id), NumberOf(id), set.Contains(id), Cmp.Equals(id, primary),
                mirrorOf.GetValueOrDefault(id)));
        }
        return items;
    }

    public List<PreviewItem> PreviewCurrent()
    {
        var items = new List<PreviewItem>();
        if (Current == null) return items;
        var prim = Current.PrimaryMonitorId();
        var useMaster = Master != null && Master.MonitorSet().IsSupersetOf(Current.MonitorSet());
        if (!useMaster) return CollapsedPreview(Current);
        var active = Current.MonitorSet();
        var mirrorOf = new Dictionary<string, string>(Cmp);
        foreach (var g in Current.CloneGroups())
        {
            var ids = g.Select(i => Current.MonitorIds[i]).OrderBy(NumberOf).ToList();
            foreach (var id in ids.Skip(1)) mirrorOf[id] = NumberOf(ids[0]).ToString();
        }
        for (int i = 0; i < Master!.Count; i++)
        {
            var r = Master.SourceRect(i);
            if (r == null) continue;
            var id = Master.MonitorIds[i];
            items.Add(new PreviewItem(r.Value, NameOf(id), NumberOf(id), active.Contains(id), Cmp.Equals(id, prim),
                mirrorOf.GetValueOrDefault(id)));
        }
        return items;
    }
}
