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

        // rechte Spalte: Vorschau
        Controls.Add(MakeLabel("VORSCHAU", Section, TextDim, new Point(rightX, S(20)), rightW));
        _preview = new LayoutPreview { Location = new Point(rightX, S(42)), Size = new Size(rightW, S(200)), BackColor = Bg };
        Controls.Add(_preview);
        _note = MakeLabel("", Small, TextDim, new Point(rightX, S(252)), rightW, S(110));
        Controls.Add(_note);

        int bottom = Math.Max(y, S(380));
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

    HashSet<string> SelectedSet() =>
        new(_checks.Where(c => c.cb.Checked).Select(c => c.id), StringComparer.OrdinalIgnoreCase);

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
        };
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
        lines.Add("Grüner Punkt = Hauptmonitor.");
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
        if (d.Hotkey > 0)
            foreach (var other in _ctl.Data.Profiles)
                if (other != _profile && other.Hotkey == d.Hotkey) other.Hotkey = 0;

        DialogResult = DialogResult.OK;
        Close();
    }
}
