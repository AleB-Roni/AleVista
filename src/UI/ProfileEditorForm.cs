using static AleVista.Theme;

namespace AleVista;

internal sealed class ProfileEditorForm : DarkForm
{
    sealed class Item
    {
        public string? Id { get; init; }
        public string Label { get; init; } = "";
        public override string ToString() => Label;
    }

    readonly AppController _ctl;
    readonly Profile _profile;
    readonly TextBox _name;
    readonly List<(string id, CheckBox cb)> _checks = new();
    readonly ComboBox _primary, _hotkey;
    readonly LayoutPreview _preview;
    readonly Label _note;
    readonly Panel _mirrorPanel, _hzPanel;
    readonly Btn _save, _cancel;
    readonly HashSet<string> _originalSet;
    /// <summary>Monitor → Monitor, dessen Bild er zeigen soll.</summary>
    readonly Dictionary<string, string> _mirrorPick = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Monitore, deren Auflösung in ihrer Spiegel-Gruppe gelten soll.</summary>
    readonly HashSet<string> _resOwners = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gewünschte Hz je Monitor (bei Spiegel-Gruppen am Auflösungs-Monitor).</summary>
    readonly Dictionary<string, int> _hz = new(StringComparer.OrdinalIgnoreCase);
    bool _loading = true;

    public ProfileEditorForm(AppController ctl, Profile profile, bool isNew)
    {
        _ctl = ctl;
        _profile = profile;
        _originalSet = new HashSet<string>(profile.Monitors, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, hz) in profile.RefreshRates) _hz[id] = hz;
        foreach (var g in profile.Mirrors)
        {
            if (g.Monitors.Count < 2) continue;
            var owner = g.Owner(g.Monitors);
            _resOwners.Add(owner);
            foreach (var id in g.Monitors)
                if (!string.Equals(id, owner, StringComparison.OrdinalIgnoreCase)) _mirrorPick[id] = owner;
        }

        Text = isNew ? "Neues Profil" : "Profil bearbeiten";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        int left = S(24), colW = S(300), rightX = S(348), rightW = S(330);
        int y = S(20);

        Controls.Add(MakeLabel("NAME", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _name = MakeTextBox(profile.Name, new Point(left, y), colW);
        Controls.Add(_name);
        y += S(46);

        Controls.Add(MakeLabel("MONITORE IN DIESEM PROFIL", Section, TextDim, new Point(left, y), colW));
        y += S(24);

        var ids = ctl.OrderedMonitors();
        foreach (var id in profile.Monitors)
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase)) ids.Add(id);

        foreach (var id in ids)
        {
            bool inMaster = ctl.InMaster(id);
            bool usable = inMaster || profile.Fixed != null && _originalSet.Contains(id);
            var tech = ctl.TechOf(id);
            var label = $"{ctl.NumberOf(id)}   {ctl.NameOf(id)}" + (tech.Length > 0 ? $"  ({tech})" : "");
            if (!usable) label += "  – erst „Alle aktivieren & einlesen“";
            var cb = new CheckBox
            {
                Text = label,
                Checked = _originalSet.Contains(id),
                Enabled = usable,
                Location = new Point(left, y),
                Size = new Size(colW, S(28)),
                ForeColor = usable ? Theme.Text : TextDim,
                BackColor = Bg,
                FlatStyle = FlatStyle.Flat,
                Font = Body,
                Cursor = Cursors.Hand,
                AutoEllipsis = true,
                UseMnemonic = false,
            };
            cb.FlatAppearance.BorderColor = Border;
            cb.FlatAppearance.CheckedBackColor = AccentInk;
            cb.CheckedChanged += (_, _) => OnSelectionChanged();
            _checks.Add((id, cb));
            Controls.Add(cb);
            y += S(30);
        }
        if (ids.Count == 0)
        {
            Controls.Add(MakeLabel("Keine Monitore erkannt.", Body, TextDim, new Point(left, y), colW));
            y += S(30);
        }

        y += S(14);
        Controls.Add(MakeLabel("HAUPTMONITOR", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _primary = MakeCombo(new Point(left, y), colW);
        _primary.SelectedIndexChanged += (_, _) => { if (!_loading) UpdatePreview(); };
        Controls.Add(_primary);
        y += S(44);

        Controls.Add(MakeLabel("TASTENKÜRZEL", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _hotkey = MakeCombo(new Point(left, y), colW);
        _hotkey.Items.Add(new Item { Id = "0", Label = "Keins" });
        for (int i = 1; i <= 9; i++)
        {
            var owner = ctl.Data.Profiles.FirstOrDefault(p => p != profile && p.Hotkey == i);
            _hotkey.Items.Add(new Item { Id = i.ToString(), Label = $"Strg+Alt+{i}" + (owner != null ? $"  (von „{owner.Name}“ übernehmen)" : "") });
        }
        _hotkey.SelectedIndex = profile.Hotkey is >= 1 and <= 9 ? profile.Hotkey : 0;
        Controls.Add(_hotkey);
        y += S(48);

        Controls.Add(MakeLabel("SPIEGELN", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _mirrorPanel = new Panel { Location = new Point(left, y), Size = new Size(colW, 0), BackColor = Bg };
        Controls.Add(_mirrorPanel);

        // rechte Spalte: Vorschau
        Controls.Add(MakeLabel("VORSCHAU", Section, TextDim, new Point(rightX, S(20)), rightW));
        _preview = new LayoutPreview { Location = new Point(rightX, S(42)), Size = new Size(rightW, S(200)), BackColor = Bg };
        Controls.Add(_preview);
        _note = MakeLabel("", Small, TextDim, new Point(rightX, S(252)), rightW, S(170));
        Controls.Add(_note);
        _hzPanel = new Panel { Location = new Point(rightX, _note.Bottom + S(10)), Size = new Size(rightW, 0), BackColor = Bg };
        Controls.Add(_hzPanel);

        _save = new Btn("Speichern", primary: true) { Width = S(120) };
        _cancel = new Btn("Abbrechen") { Width = S(110) };
        _save.Click += (_, _) => SaveAndClose();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_save);
        Controls.Add(_cancel);

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        RebuildPrimary(profile.Primary);
        RebuildMirrors();
        _loading = false;
        UpdatePreview();
        Shown += (_, _) => { if (isNew) { _name.Focus(); _name.SelectAll(); } };
    }

    HashSet<string> SelectedSet() =>
        new(_checks.Where(c => c.cb.Checked).Select(c => c.id), StringComparer.OrdinalIgnoreCase);

    void OnSelectionChanged()
    {
        if (_loading) return;
        var keep = (_primary.SelectedItem as Item)?.Id;
        RebuildPrimary(keep);
        RebuildMirrors();
        UpdatePreview();
    }

    List<string> CheckedIds() => _checks.Where(c => c.cb.Checked).Select(c => c.id).ToList();

    string MonitorLabel(string id) => $"{_ctl.NumberOf(id)}   {_ctl.NameOf(id)}";

    /// <summary>Spiegel-Gruppen aus den Auswahlfeldern (nur eingeschaltete Monitore), jeweils mit Auflösungs-Monitor.</summary>
    List<MirrorGroup> MirrorGroups()
    {
        var on = CheckedIds();
        var onSet = new HashSet<string>(on, StringComparer.OrdinalIgnoreCase);
        var root = on.ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);
        string Find(string x) { while (!string.Equals(root[x], x, StringComparison.OrdinalIgnoreCase)) x = root[x]; return x; }
        foreach (var id in on)
            if (_mirrorPick.TryGetValue(id, out var target) && onSet.Contains(target))
                root[Find(id)] = Find(target);

        var groups = new List<MirrorGroup>();
        foreach (var members in on.GroupBy(Find, StringComparer.OrdinalIgnoreCase).Select(g => g.ToList()).Where(g => g.Count > 1))
        {
            // Standard: der Monitor, der gespiegelt wird (selbst keinen anderen spiegelt).
            var owner = members.FirstOrDefault(_resOwners.Contains)
                        ?? members.FirstOrDefault(m => !_mirrorPick.TryGetValue(m, out var t) || !onSet.Contains(t))
                        ?? members[0];
            groups.Add(new MirrorGroup { Monitors = members, ResolutionFrom = owner });
        }
        return groups;
    }

    void RebuildMirrors()
    {
        bool wasLoading = _loading;
        _loading = true;
        SuspendLayout();
        foreach (Control c in _mirrorPanel.Controls.Cast<Control>().ToList()) c.Dispose();
        _mirrorPanel.Controls.Clear();

        int w = _mirrorPanel.Width, labelW = S(118), y = 0;
        var on = CheckedIds();
        if (on.Count < 2)
        {
            _mirrorPanel.Controls.Add(MakeLabel("Zum Spiegeln mindestens zwei Monitore einschalten.", Small, TextDim, new Point(0, y), w));
            y += S(26);
        }
        else
        {
            foreach (var id in on)
            {
                _mirrorPanel.Controls.Add(MakeLabel(MonitorLabel(id), Body, Theme.Text, new Point(0, y + S(3)), labelW - S(6)));
                var cb = MakeCombo(new Point(labelW, y), w - labelW);
                cb.Items.Add(new Item { Id = null, Label = "Eigenes Bild" });
                int sel = 0;
                foreach (var other in on)
                {
                    if (string.Equals(other, id, StringComparison.OrdinalIgnoreCase)) continue;
                    cb.Items.Add(new Item { Id = other, Label = "spiegelt " + MonitorLabel(other) });
                    if (_mirrorPick.TryGetValue(id, out var t) && string.Equals(t, other, StringComparison.OrdinalIgnoreCase))
                        sel = cb.Items.Count - 1;
                }
                cb.SelectedIndex = sel;
                var monitor = id;
                cb.SelectedIndexChanged += (_, _) =>
                {
                    if (_loading) return;
                    if ((cb.SelectedItem as Item)?.Id is { } target) _mirrorPick[monitor] = target;
                    else _mirrorPick.Remove(monitor);
                    // Nicht im eigenen Ereignis neu aufbauen (die ComboBox wird dabei entsorgt).
                    BeginInvoke(new Action(() => { RebuildMirrors(); UpdatePreview(); }));
                };
                _mirrorPanel.Controls.Add(cb);
                y += S(34);
            }

            foreach (var g in MirrorGroups())
            {
                y += S(8);
                var nums = string.Join(" + ", g.Monitors.Select(m => _ctl.NumberOf(m)));
                _mirrorPanel.Controls.Add(MakeLabel($"Auflösung {nums}", Body, Theme.Text, new Point(0, y + S(3)), labelW - S(6)));
                var cb = MakeCombo(new Point(labelW, y), w - labelW);
                foreach (var m in g.Monitors)
                {
                    var res = _ctl.ResolutionOf(m);
                    cb.Items.Add(new Item { Id = m, Label = "von " + MonitorLabel(m) + (res.Length > 0 ? $"  ({res})" : "") });
                    if (string.Equals(m, g.ResolutionFrom, StringComparison.OrdinalIgnoreCase)) cb.SelectedIndex = cb.Items.Count - 1;
                }
                var members = g.Monitors;
                cb.SelectedIndexChanged += (_, _) =>
                {
                    if (_loading) return;
                    foreach (var m in members) _resOwners.Remove(m);
                    if ((cb.SelectedItem as Item)?.Id is { } owner) _resOwners.Add(owner);
                    BeginInvoke(new Action(() => { RebuildMirrors(); UpdatePreview(); }));
                };
                _mirrorPanel.Controls.Add(cb);
                y += S(34);
            }
        }
        _mirrorPanel.Height = y;
        RebuildRefreshRows();
        LayoutBottom();
        ResumeLayout();
        _loading = wasLoading;
    }

    /// <summary>Monitore mit eigenem Bild: einzelne Monitore und je Spiegel-Gruppe der Auflösungs-Monitor.</summary>
    List<(string id, List<string> members)> OwnImages()
    {
        var groups = MirrorGroups();
        var list = new List<(string, List<string>)>();
        foreach (var id in CheckedIds())
        {
            var g = groups.FirstOrDefault(x => x.Monitors.Contains(id, StringComparer.OrdinalIgnoreCase));
            if (g == null) list.Add((id, new List<string> { id }));
            else if (string.Equals(g.ResolutionFrom, id, StringComparison.OrdinalIgnoreCase)) list.Add((id, g.Monitors));
        }
        return list;
    }

    void RebuildRefreshRows()
    {
        foreach (Control c in _hzPanel.Controls.Cast<Control>().ToList()) c.Dispose();
        _hzPanel.Controls.Clear();
        int w = _hzPanel.Width, labelW = S(118), y = 0;
        _hzPanel.Controls.Add(MakeLabel("BILDWIEDERHOLRATE", Section, TextDim, new Point(0, y), w));
        y += S(22);
        var draft = Draft();
        foreach (var (id, members) in OwnImages())
        {
            // Hz-Wahl wandert mit, wenn in der Gruppe ein anderer Monitor die Auflösung vorgibt.
            if (!_hz.ContainsKey(id))
                foreach (var m in members)
                    if (_hz.Remove(m, out var old)) { _hz[id] = old; break; }
            foreach (var m in members)
                if (!string.Equals(m, id, StringComparison.OrdinalIgnoreCase)) _hz.Remove(m);

            var title = members.Count > 1 ? string.Join(" + ", members.Select(m => _ctl.NumberOf(m))) : MonitorLabel(id);
            _hzPanel.Controls.Add(MakeLabel(title, Body, Theme.Text, new Point(0, y + S(3)), labelW - S(6)));

            var res = _ctl.ProfileResolutionOf(draft, id);
            var rates = res is { } r ? _ctl.RefreshRatesOf(id, r) : new List<int>();
            var cb = MakeCombo(new Point(labelW, y), w - labelW);
            cb.Items.Add(new Item { Id = null, Label = rates.Count > 0 ? "Automatisch" : "Automatisch (Hz unbekannt – Monitor einmal einschalten)" });
            var shown = new List<int>(rates);
            if (_hz.TryGetValue(id, out var cur) && !shown.Contains(cur)) shown.Add(cur);
            shown.Sort();
            int sel = 0;
            foreach (var hz in shown.AsEnumerable().Reverse())
            {
                cb.Items.Add(new Item { Id = hz.ToString(), Label = $"{hz} Hz" + (rates.Contains(hz) ? "" : "  (nicht bestätigt)") });
                if (hz == cur && _hz.ContainsKey(id)) sel = cb.Items.Count - 1;
            }
            cb.SelectedIndex = sel;
            var monitor = id;
            cb.SelectedIndexChanged += (_, _) =>
            {
                if (_loading) return;
                if (int.TryParse((cb.SelectedItem as Item)?.Id, out var hz)) _hz[monitor] = hz;
                else _hz.Remove(monitor);
                UpdatePreview();
            };
            _hzPanel.Controls.Add(cb);
            y += S(34);
        }
        _hzPanel.Height = y;
    }

    void LayoutBottom()
    {
        int bottom = Math.Max(Math.Max(_mirrorPanel.Bottom, _hzPanel.Bottom) + S(10), S(440));
        ClientSize = new Size(_preview.Right + S(24), bottom + S(56));
        _save.Location = new Point(ClientSize.Width - S(24) - _save.Width, ClientSize.Height - S(20) - _save.Height);
        _cancel.Location = new Point(_save.Left - S(10) - _cancel.Width, _save.Top);
    }

    void RebuildPrimary(string? select)
    {
        bool wasLoading = _loading;
        _loading = true;
        _primary.Items.Clear();
        _primary.Items.Add(new Item { Id = null, Label = "Automatisch" });
        int sel = 0;
        foreach (var (id, cb) in _checks)
        {
            if (!cb.Checked) continue;
            _primary.Items.Add(new Item { Id = id, Label = $"{_ctl.NumberOf(id)}   {_ctl.NameOf(id)}" });
            if (select != null && string.Equals(select, id, StringComparison.OrdinalIgnoreCase)) sel = _primary.Items.Count - 1;
        }
        _primary.SelectedIndex = sel;
        _loading = wasLoading;
    }

    Profile Draft()
    {
        var set = SelectedSet();
        var fixedStillValid = _profile.Fixed != null && set.SetEquals(_originalSet);
        return new Profile
        {
            Id = _profile.Id,
            Name = _name.Text.Trim(),
            Monitors = _checks.Where(c => c.cb.Checked).Select(c => c.id).ToList(),
            Primary = (_primary.SelectedItem as Item)?.Id,
            Fixed = fixedStillValid ? _profile.Fixed : null,
            Hotkey = int.TryParse((_hotkey.SelectedItem as Item)?.Id, out var h) ? h : 0,
            Mirrors = MirrorGroups(),
            RefreshRates = RefreshSelection(),
        };
    }

    /// <summary>Hz-Auswahl nur für eingeschaltete Monitore mit eigenem Bild.</summary>
    Dictionary<string, int> RefreshSelection()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, _) in OwnImages())
            if (_hz.TryGetValue(id, out var hz)) result[id] = hz;
        return result;
    }

    void UpdatePreview()
    {
        var d = Draft();
        _preview.Items = _ctl.PreviewFor(d);
        var lines = new List<string>();
        if (_profile.Fixed != null)
            lines.Add(d.Fixed != null
                ? "Dieses Profil nutzt die exakt gespeicherte Anordnung."
                : "Monitorauswahl geändert: Die Anordnung wird jetzt aus der Vorlage (alle Monitore an) abgeleitet.");
        else
            lines.Add("Positionen stammen aus der Vorlage mit allen Monitoren. Lücken durch abgeschaltete Monitore werden automatisch geschlossen.");
        if (d.Monitors.Count == 0) lines.Add("Mindestens ein Monitor muss an sein.");
        foreach (var g in d.Mirrors)
        {
            var owner = g.ResolutionFrom ?? g.Monitors[0];
            lines.Add($"{string.Join(" + ", g.Monitors.Select(m => _ctl.NumberOf(m)))} zeigen dasselbe Bild mit der Auflösung von {_ctl.NumberOf(owner)} {_ctl.NameOf(owner)}."
                + (g.Monitors.Select(_ctl.AdapterOf).Where(a => a != null).Distinct().Count() > 1
                    ? " Achtung: Die Monitore hängen an verschiedenen Grafikkarten – das kann Windows evtl. nicht spiegeln."
                    : ""));
        }
        if (d.RefreshRates.Count > 0)
            lines.Add("Bildwiederholrate: " + string.Join(", ", d.RefreshRates.Select(kv => $"{_ctl.NumberOf(kv.Key)} → {kv.Value} Hz")) + ".");
        lines.Add("Grüner Punkt = Hauptmonitor. „3 = 1“ = Monitor 3 spiegelt Monitor 1.");
        _note.Text = string.Join("\n\n", lines);
    }

    void SaveAndClose()
    {
        var d = Draft();
        if (d.Name.Length == 0) { MessageBox.Show(this, "Bitte gib dem Profil einen Namen.", "AleVista"); _name.Focus(); return; }
        if (d.Monitors.Count == 0) { MessageBox.Show(this, "Mindestens ein Monitor muss in dem Profil an sein.", "AleVista"); return; }

        _profile.Name = d.Name;
        _profile.Monitors = d.Monitors;
        _profile.Primary = d.Primary;
        _profile.Fixed = d.Fixed;
        _profile.Hotkey = d.Hotkey;
        _profile.Mirrors = d.Mirrors;
        _profile.RefreshRates = d.RefreshRates;
        if (d.Hotkey > 0)
            foreach (var other in _ctl.Data.Profiles)
                if (other != _profile && other.Hotkey == d.Hotkey) other.Hotkey = 0;

        DialogResult = DialogResult.OK;
        Close();
    }
}
