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
    readonly HashSet<string> _originalSet;
    readonly CheckBox? _keepLocal;
    readonly List<(SharePeer peer, CheckBox keep, List<(string id, CheckBox cb)> checks)> _remote = new();
    bool _loading = true;

    public ProfileEditorForm(AppController ctl, Profile profile, bool isNew)
    {
        _ctl = ctl;
        _profile = profile;
        _originalSet = new HashSet<string>(profile.Monitors, StringComparer.OrdinalIgnoreCase);

        Text = isNew ? "Neues Profil" : "Profil bearbeiten";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        int left = S(24), colW = S(320), rightX = S(368), rightW = S(400);
        var peers = ctl.PeersWithDisplays();
        int y = S(20);

        Controls.Add(MakeLabel("NAME", Section, TextDim, new Point(left, y), colW));
        y += S(22);
        _name = MakeTextBox(profile.Name, new Point(left, y), colW);
        Controls.Add(_name);
        y += S(46);

        Controls.Add(MakeLabel(peers.Count > 0 ? "DIESER PC" : "MONITORE IN DIESEM PROFIL", Section, peers.Count > 0 ? Accent : TextDim, new Point(left, y), colW));
        y += S(24);
        if (peers.Count > 0)
        {
            _keepLocal = MakeCheck("Monitore dieses PCs nicht ändern", profile.KeepLocal, true, left, y, colW, TextDim);
            _keepLocal.CheckedChanged += (_, _) => OnSelectionChanged();
            Controls.Add(_keepLocal);
            y += S(30);
        }

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
            var cb = MakeCheck(label, _originalSet.Contains(id), usable, left, y, colW, usable ? Theme.Text : TextDim);
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

        // Andere PCs (Maus & Tastatur teilen): gemeinsames Profil
        foreach (var peer in peers)
        {
            y += S(16);
            Controls.Add(MakeLabel(peer.Name.ToUpperInvariant(), Section, Peer, new Point(left, y), colW));
            y += S(24);
            var sel = profile.Remote.FirstOrDefault(r => r.PeerId == peer.Id);
            var keep = MakeCheck("Monitore dieses PCs nicht ändern", sel == null, true, left, y, colW, TextDim);
            keep.CheckedChanged += (_, _) => OnSelectionChanged();
            Controls.Add(keep);
            y += S(30);
            var checks = new List<(string, CheckBox)>();
            foreach (var m in peer.Displays!.Monitors)
            {
                var label = $"{m.Number}   {m.Name}" + (m.Tech.Length > 0 ? $"  ({m.Tech})" : "");
                if (!m.InMaster) label += "  – dort „Alle aktivieren & einlesen“";
                bool on = sel != null ? sel.Monitors.Contains(m.Id, StringComparer.OrdinalIgnoreCase) : m.Active;
                var cb = MakeCheck(label, on && m.InMaster, m.InMaster, left, y, colW, m.InMaster ? Theme.Text : TextDim);
                cb.FlatAppearance.CheckedBackColor = Theme.Mix(Peer, Bg, .75f);
                cb.CheckedChanged += (_, _) => OnSelectionChanged();
                checks.Add((m.Id, cb));
                Controls.Add(cb);
                y += S(30);
            }
            _remote.Add((peer, keep, checks));
        }

        y += S(14);
        Controls.Add(MakeLabel(peers.Count > 0 ? "HAUPTMONITOR (DIESER PC)" : "HAUPTMONITOR", Section, TextDim, new Point(left, y), colW));
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

        // rechte Spalte: Vorschau
        Controls.Add(MakeLabel("VORSCHAU", Section, TextDim, new Point(rightX, S(20)), rightW));
        _preview = new LayoutPreview { Location = new Point(rightX, S(42)), Size = new Size(rightW, S(220)), BackColor = Bg };
        Controls.Add(_preview);
        _note = MakeLabel("", Small, TextDim, new Point(rightX, S(272)), rightW, S(150));
        Controls.Add(_note);

        int bottom = Math.Max(y, S(430));
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
        UpdateEnabled();
        _loading = false;
        UpdatePreview();
        Shown += (_, _) => { if (isNew) { _name.Focus(); _name.SelectAll(); } };
    }

    CheckBox MakeCheck(string text, bool on, bool enabled, int x, int y, int w, Color color)
    {
        var cb = new CheckBox
        {
            Text = text,
            Checked = on,
            Enabled = enabled,
            Location = new Point(x, y),
            Size = new Size(w, S(28)),
            ForeColor = color,
            BackColor = Bg,
            FlatStyle = FlatStyle.Flat,
            Font = Body,
            Cursor = Cursors.Hand,
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        cb.FlatAppearance.BorderColor = Border;
        cb.FlatAppearance.CheckedBackColor = AccentInk;
        return cb;
    }

    void UpdateEnabled()
    {
        bool keepLocal = _keepLocal?.Checked == true;
        foreach (var (id, cb) in _checks)
            cb.Enabled = !keepLocal && (_ctl.InMaster(id) || _profile.Fixed != null && _originalSet.Contains(id));
        _primary.Enabled = !keepLocal;
        foreach (var (peer, keep, checks) in _remote)
            foreach (var (id, cb) in checks)
                cb.Enabled = !keep.Checked && peer.Displays!.Monitors.Any(m => m.Id == id && m.InMaster);
    }

    HashSet<string> SelectedSet() =>
        new(_checks.Where(c => c.cb.Checked).Select(c => c.id), StringComparer.OrdinalIgnoreCase);

    void OnSelectionChanged()
    {
        if (_loading) return;
        UpdateEnabled();
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
            KeepLocal = _keepLocal?.Checked == true,
            Remote = RemoteDraft(),
        };
    }

    List<RemoteSelection> RemoteDraft()
    {
        // Auswahlen für PCs, deren Monitore gerade nicht bekannt sind, unverändert übernehmen
        var list = _profile.Remote.Where(r => _remote.All(x => x.peer.Id != r.PeerId)).Select(r => r.Clone()).ToList();
        foreach (var (peer, keep, checks) in _remote)
        {
            if (keep.Checked) continue;
            var old = _profile.Remote.FirstOrDefault(r => r.PeerId == peer.Id);
            list.Add(new RemoteSelection
            {
                PeerId = peer.Id,
                Monitors = checks.Where(c => c.cb.Checked).Select(c => c.id).ToList(),
                Primary = old?.Primary,
            });
        }
        return list;
    }

    void UpdatePreview()
    {
        var d = Draft();
        _preview.Combined = _ctl.CombinedPreviewFor(d);
        var lines = new List<string>();
        if (_profile.Fixed != null)
            lines.Add(d.Fixed != null
                ? "Dieses Profil nutzt die exakt gespeicherte Anordnung."
                : "Monitorauswahl geändert: Die Anordnung wird jetzt aus der Vorlage (alle Monitore an) abgeleitet.");
        else
            lines.Add("Positionen stammen aus der Vorlage mit allen Monitoren. Lücken durch abgeschaltete Monitore werden automatisch geschlossen.");
        if (!d.KeepLocal && d.Monitors.Count == 0) lines.Add("Mindestens ein Monitor muss an sein.");
        if (_remote.Count > 0)
            lines.Add("Gemeinsames Profil: Grün = dieser PC, Blau = andere PCs. Beim Aktivieren werden alle beteiligten PCs gleichzeitig umgeschaltet (sie müssen verbunden sein).");
        lines.Add("Punkt = Hauptmonitor.");
        _note.Text = string.Join("\n\n", lines);
    }

    void SaveAndClose()
    {
        var d = Draft();
        if (d.Name.Length == 0) { MessageBox.Show(this, "Bitte gib dem Profil einen Namen.", "AleVista"); _name.Focus(); return; }
        if (!d.KeepLocal && d.Monitors.Count == 0) { MessageBox.Show(this, "Mindestens ein Monitor dieses PCs muss an sein (oder „nicht ändern“ wählen).", "AleVista"); return; }
        var emptyPeer = d.Remote.FirstOrDefault(r => r.Monitors.Count == 0);
        if (emptyPeer != null)
        {
            MessageBox.Show(this, $"Auf „{_ctl.Peer(emptyPeer.PeerId)?.Name}“ muss mindestens ein Monitor an sein (oder „nicht ändern“ wählen).", "AleVista");
            return;
        }
        if (d.KeepLocal && d.Remote.Count == 0) { MessageBox.Show(this, "Das Profil ändert keinen PC.", "AleVista"); return; }

        _profile.Name = d.Name;
        _profile.Monitors = d.Monitors;
        _profile.Primary = d.Primary;
        _profile.Fixed = d.Fixed;
        _profile.Hotkey = d.Hotkey;
        _profile.KeepLocal = d.KeepLocal;
        _profile.Remote = d.Remote;
        if (d.Hotkey > 0)
            foreach (var other in _ctl.Data.Profiles)
                if (other != _profile && other.Hotkey == d.Hotkey) other.Hotkey = 0;

        DialogResult = DialogResult.OK;
        Close();
    }
}
