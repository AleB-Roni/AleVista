using static AleVista.Theme;

namespace AleVista;

/// <summary>
/// Profil anlegen/bearbeiten. Ohne andere PCs: Monitore an-/abhaken.
/// Mit verbundenen PCs (Maus &amp; Tastatur teilen): Liste aller Bildschirme, pro Bildschirm wählen, welcher PC ihn bekommt.
/// </summary>
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
    readonly Profile _identity; // das Original in der Profilliste (_profile ist eine Arbeitskopie)
    readonly TextBox _name;
    readonly List<(string id, CheckBox cb)> _checks = new();              // nur dieser PC
    readonly List<(ScreenGroup group, ScreenChoiceRow row)> _rows = new(); // mehrere PCs
    readonly List<SharePeer> _peers;
    readonly ComboBox _primary, _hotkey;
    readonly LayoutPreview _preview;
    readonly Label _note;
    readonly HashSet<string> _originalSet;
    bool _loading = true;

    bool MultiPc => _peers.Count > 0;

    public ProfileEditorForm(AppController ctl, Profile profile, bool isNew, Profile? identity = null)
    {
        _ctl = ctl;
        _profile = profile;
        _identity = identity ?? profile;
        _originalSet = new HashSet<string>(profile.Monitors, StringComparer.OrdinalIgnoreCase);
        _peers = ctl.PeersWithDisplays();

        Text = isNew ? "Neues Profil" : "Profil bearbeiten";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        int left = S(24), colW = MultiPc ? S(470) : S(320);
        int rightX = left + colW + S(28), rightW = S(400);
        int y = S(20);

        Controls.Add(MakeLabel("NAME", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _name = MakeTextBox(profile.Name, new Point(left, y), colW);
        Controls.Add(_name);
        y += S(46);

        if (MultiPc) y = BuildScreenRows(left, y, colW, isNew);
        else y = BuildChecks(left, y, colW);

        y += S(14);
        Controls.Add(MakeLabel(MultiPc ? "HAUPTMONITOR AUF DIESEM PC" : "HAUPTMONITOR", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _primary = MakeCombo(new Point(left, y), Math.Min(colW, S(320)));
        _primary.SelectedIndexChanged += (_, _) => { if (!_loading) UpdatePreview(); };
        Controls.Add(_primary);
        y += S(44);

        Controls.Add(MakeLabel("TASTENKÜRZEL", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _hotkey = MakeCombo(new Point(left, y), Math.Min(colW, S(320)));
        _hotkey.Items.Add(new Item { Id = "0", Label = "Keins" });
        for (int i = 1; i <= 9; i++)
        {
            var owner = ctl.Data.Profiles.FirstOrDefault(p => p != _identity && p.Hotkey == i);
            _hotkey.Items.Add(new Item { Id = i.ToString(), Label = $"Strg+Alt+{i}" + (owner != null ? $"  (von „{owner.Name}“ übernehmen)" : "") });
        }
        _hotkey.SelectedIndex = profile.Hotkey is >= 1 and <= 9 ? profile.Hotkey : 0;
        Controls.Add(_hotkey);
        y += S(48);

        // rechte Spalte: Vorschau
        Controls.Add(MakeLabel("VORSCHAU", Section, TextDim, new Point(rightX, S(20)), rightW));
        _preview = new LayoutPreview { Location = new Point(rightX, S(42)), Size = new Size(rightW, S(230)), BackColor = Bg };
        Controls.Add(_preview);
        _note = MakeLabel("", Small, TextDim, new Point(rightX, S(284)), rightW, S(170));
        Controls.Add(_note);

        int bottom = Math.Max(y, S(470));
        ClientSize = new Size(rightX + rightW + S(24), bottom + S(56));

        var save = new Btn("Speichern", primary: true) { Width = S(120) };
        var cancel = new Btn("Abbrechen") { Width = S(110) };
        save.Location = new Point(ClientSize.Width - S(24) - save.Width, ClientSize.Height - S(20) - save.Height);
        cancel.Location = new Point(save.Left - S(10) - cancel.Width, save.Top);
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(save);
        Controls.Add(cancel);

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        RebuildPrimary(profile.Primary);
        _loading = false;
        UpdatePreview();
        Shown += (_, _) => { if (isNew) { _name.Focus(); _name.SelectAll(); } };
    }

    // ---------- Nur dieser PC: Häkchen ----------

    int BuildChecks(int left, int y, int colW)
    {
        Controls.Add(MakeLabel("MONITORE IN DIESEM PROFIL", Section, TextDim, new Point(left, y), colW));
        y += S(24);

        var ids = _ctl.OrderedMonitors();
        foreach (var id in _profile.Monitors)
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase)) ids.Add(id);

        foreach (var id in ids)
        {
            bool usable = _ctl.InMaster(id) || _profile.Fixed != null && _originalSet.Contains(id);
            var tech = _ctl.TechOf(id);
            var label = $"{_ctl.NumberOf(id)}   {_ctl.NameOf(id)}" + (tech.Length > 0 ? $"  ({tech})" : "");
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
        if (_ctl.Data.Share.Peers.Count > 0)
        {
            y += S(6);
            Controls.Add(MakeLabel(
                "Andere PCs erscheinen hier, sobald sie über „Maus & Tastatur teilen“ verbunden sind und dieselbe AleVista-Version haben.",
                Small, TextDim, new Point(left, y), colW, S(36)));
            y += S(38);
        }
        return y;
    }

    // ---------- Mehrere PCs: Bildschirme verteilen ----------

    string PcName(string pcId) => pcId.Length == 0 ? "Dieser PC" : _peers.FirstOrDefault(p => p.Id == pcId)?.Name ?? "anderer PC";

    int BuildScreenRows(int left, int y, int colW, bool isNew)
    {
        Controls.Add(MakeLabel("BILDSCHIRME VERTEILEN", Section, TextDim, new Point(left, y + S(6)), colW));
        var link = new Btn("Zuordnung ändern …") { Height = S(28), Font = Small };
        link.FitWidth();
        link.Location = new Point(left + colW - link.Width, y);
        link.Click += (_, _) => ChangeLinks(link);
        Controls.Add(link);
        y += S(34);
        Controls.Add(MakeLabel("Wähle bei jedem Bildschirm, welcher PC ihn in diesem Profil bekommt – oder „Aus“. " +
            "Erscheint ein Bildschirm doppelt, über „Zuordnung ändern …“ festlegen, dass es derselbe ist.",
            Small, TextDim, new Point(left, y), colW, S(36)));
        y += S(42);

        foreach (var g in _ctl.ScreenGroups(_peers))
        {
            var options = new List<ScreenChoiceRow.Option>();
            foreach (var m in g.Members)
                options.Add(new ScreenChoiceRow.Option(PcName(m.PcId), m.PcId, m.Usable, m.PcId.Length == 0 ? Accent : Peer));
            options.Add(new ScreenChoiceRow.Option("Aus", null, true, Border));

            int selected = options.Count - 1;
            for (int i = 0; i < g.Members.Count; i++)
                if (IsOnInProfile(g.Members[i], isNew)) { selected = i; break; }

            var sub = new List<string>();
            if (g.Members.Count > 1) sub.Add($"hängt an {string.Join(" und ", g.Members.Select(m => PcName(m.PcId)))}");
            if (g.Members.Any(m => !m.Usable)) sub.Add("ausgegraut: dort erst „Alle aktivieren & einlesen“");

            var row = new ScreenChoiceRow(g.Name, string.Join("  ·  ", sub), options, selected, colW) { Location = new Point(left, y) };
            row.SelectionChanged += OnSelectionChanged;
            _rows.Add((g, row));
            Controls.Add(row);
            y += row.Height + S(6);
        }
        return y;
    }

    void ChangeLinks(Control anchor)
    {
        void Open(SharePeer peer)
        {
            if (!MonitorLinkForm.Edit(this, _ctl, peer)) return;
            // Bisherige Eingaben übernehmen und den Editor mit der neuen Zuordnung neu aufbauen
            ApplyDraft(Draft());
            DialogResult = DialogResult.Retry;
            Close();
        }
        if (_peers.Count == 1) { Open(_peers[0]); return; }
        var m = DarkMenu();
        foreach (var p in _peers) { var peer = p; m.Items.Add(peer.Name.Replace("&", "&&"), null, (_, _) => Open(peer)); }
        m.Closed += (_, _) => BeginInvoke(new Action(m.Dispose));
        m.Show(anchor, new Point(0, anchor.Height + S(4)));
    }

    void ApplyDraft(Profile d)
    {
        _profile.Name = d.Name;
        _profile.Monitors = d.Monitors;
        _profile.Primary = d.Primary;
        _profile.Fixed = d.Fixed;
        _profile.Hotkey = d.Hotkey;
        _profile.KeepLocal = d.KeepLocal;
        _profile.Remote = d.Remote;
    }

    bool IsOnInProfile(ScreenMember m, bool isNew)
    {
        if (m.PcId.Length == 0) return !_profile.KeepLocal && _originalSet.Contains(m.MonitorId);
        var sel = _profile.Remote.FirstOrDefault(r => r.PeerId == m.PcId);
        if (sel != null) return sel.Monitors.Contains(m.MonitorId, StringComparer.OrdinalIgnoreCase);
        return isNew && m.Active; // neues Profil: aktueller Zustand des anderen PCs
    }

    List<string> LocalSelection() => MultiPc
        ? _rows.Where(r => r.row.Value == "").Select(r => r.group.Members.First(m => m.PcId.Length == 0).MonitorId).ToList()
        : _checks.Where(c => c.cb.Checked).Select(c => c.id).ToList();

    List<RemoteSelection> RemoteSelection()
    {
        // Auswahlen für PCs, deren Bildschirme gerade nicht bekannt sind, unverändert übernehmen
        var list = _profile.Remote.Where(r => _peers.All(p => p.Id != r.PeerId)).Select(r => r.Clone()).ToList();
        if (!MultiPc) return list;
        foreach (var peer in _peers)
        {
            var mons = _rows.Where(r => r.row.Value == peer.Id)
                .Select(r => r.group.Members.First(m => m.PcId == peer.Id).MonitorId).ToList();
            if (mons.Count == 0) continue; // PC ohne Bildschirm → bleibt unverändert
            list.Add(new RemoteSelection
            {
                PeerId = peer.Id,
                Monitors = mons,
                Primary = _profile.Remote.FirstOrDefault(r => r.PeerId == peer.Id)?.Primary,
            });
        }
        return list;
    }

    // ---------- gemeinsam ----------

    void OnSelectionChanged()
    {
        if (_loading) return;
        var keep = (_primary.SelectedItem as Item)?.Id;
        RebuildPrimary(keep);
        UpdatePreview();
    }

    void RebuildPrimary(string? select)
    {
        bool wasLoading = _loading;
        _loading = true;
        _primary.Items.Clear();
        _primary.Items.Add(new Item { Id = null, Label = "Automatisch" });
        int sel = 0;
        foreach (var id in LocalSelection())
        {
            _primary.Items.Add(new Item { Id = id, Label = $"{_ctl.NumberOf(id)}   {_ctl.NameOf(id)}" });
            if (select != null && string.Equals(select, id, StringComparison.OrdinalIgnoreCase)) sel = _primary.Items.Count - 1;
        }
        _primary.SelectedIndex = sel;
        _primary.Enabled = _primary.Items.Count > 1;
        _loading = wasLoading;
    }

    Profile Draft()
    {
        var local = LocalSelection();
        var set = new HashSet<string>(local, StringComparer.OrdinalIgnoreCase);
        var fixedStillValid = _profile.Fixed != null && set.SetEquals(_originalSet);
        bool keepLocal = MultiPc ? local.Count == 0 : false;
        return new Profile
        {
            Id = _profile.Id,
            Name = _name.Text.Trim(),
            Monitors = keepLocal ? new List<string>(_profile.Monitors) : local,
            Primary = (_primary.SelectedItem as Item)?.Id,
            Fixed = fixedStillValid ? _profile.Fixed : null,
            Hotkey = int.TryParse((_hotkey.SelectedItem as Item)?.Id, out var h) ? h : 0,
            KeepLocal = keepLocal,
            Remote = RemoteSelection(),
        };
    }

    void UpdatePreview()
    {
        var d = Draft();
        _preview.Combined = _ctl.CombinedPreviewFor(d);
        var lines = new List<string>();
        if (MultiPc)
        {
            lines.Add("Grün = dieser PC, Blau = andere PCs. Beim Aktivieren werden alle PCs gleichzeitig umgeschaltet (sie müssen verbunden sein).");
            var idle = new List<string>();
            if (d.KeepLocal) idle.Add("dieser PC");
            idle.AddRange(_peers.Where(p => d.Remote.All(r => r.PeerId != p.Id)).Select(p => $"„{p.Name}“"));
            if (idle.Count > 0)
                lines.Add($"Ohne Bildschirm: {string.Join(", ", idle)} – wird nicht umgeschaltet (Windows braucht immer mindestens einen Bildschirm).");
            lines.Add("Hängt ein Bildschirm an beiden PCs, schaltet er auf den PC um, der ihm ein Bild liefert – dazu am Monitor die automatische Eingangswahl einschalten.");
        }
        else
        {
            if (_profile.Fixed != null)
                lines.Add(d.Fixed != null
                    ? "Dieses Profil nutzt die exakt gespeicherte Anordnung."
                    : "Monitorauswahl geändert: Die Anordnung wird jetzt aus der Vorlage (alle Monitore an) abgeleitet.");
            else
                lines.Add("Positionen stammen aus der Vorlage mit allen Monitoren. Lücken durch abgeschaltete Monitore werden automatisch geschlossen.");
            if (d.Monitors.Count == 0) lines.Add("Mindestens ein Monitor muss an sein.");
            lines.Add("Grüner Punkt = Hauptmonitor.");
        }
        _note.Text = string.Join("\n\n", lines);
    }

    void SaveAndClose()
    {
        var d = Draft();
        if (d.Name.Length == 0) { MessageBox.Show(this, "Bitte gib dem Profil einen Namen.", "AleVista"); _name.Focus(); return; }
        if (MultiPc)
        {
            if (d.KeepLocal && d.Remote.Count == 0) { MessageBox.Show(this, "Bitte mindestens einen Bildschirm einem PC zuordnen.", "AleVista"); return; }
        }
        else if (d.Monitors.Count == 0) { MessageBox.Show(this, "Mindestens ein Monitor muss in dem Profil an sein.", "AleVista"); return; }

        ApplyDraft(d);
        if (d.Hotkey > 0)
            foreach (var other in _ctl.Data.Profiles)
                if (other != _identity && other.Hotkey == d.Hotkey) other.Hotkey = 0;

        DialogResult = DialogResult.OK;
        Close();
    }
}
