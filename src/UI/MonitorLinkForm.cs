using static AleVista.Theme;

namespace AleVista;

/// <summary>
/// „Bildschirme zuordnen“: Für jeden Monitor des anderen PCs festlegen, welcher Monitor hier derselbe ist
/// (weil er an beiden PCs hängt) – oder dass er nur dort angeschlossen ist.
/// </summary>
internal sealed class MonitorLinkForm : DarkForm
{
    sealed class Item
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public override string ToString() => Label;
    }

    readonly AppController _ctl;
    readonly SharePeer _peer;
    readonly List<(RemoteMonitor m, ComboBox box)> _rows = new();
    readonly Label _error;

    public static bool Edit(IWin32Window? owner, AppController ctl, SharePeer peer)
    {
        using var f = new MonitorLinkForm(ctl, peer);
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    MonitorLinkForm(AppController ctl, SharePeer peer)
    {
        _ctl = ctl;
        _peer = peer;
        Text = "Bildschirme zuordnen";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        int left = S(24), w = S(620), y = S(18);
        Controls.Add(MakeLabel($"Welcher Bildschirm von „{peer.Name}“ ist welcher hier?", Bold, Theme.Text, new Point(left, y), w));
        y += S(30);
        Controls.Add(MakeLabel(
            "Bildschirme, die an beiden PCs hängen, einmal zuordnen – dann erscheinen sie im Profil nur einmal und du wählst nur noch, welcher PC sie bekommt. " +
            "Gleiche Modelle unterscheidest du an der Seriennummer (SN, steht auch auf dem Aufkleber am Monitor) oder mit „Identifizieren“.",
            Small, TextDim, new Point(left, y), w, S(48)));
        y += S(58);

        int leftW = S(300), boxX = left + leftW + S(16), boxW = w - leftW - S(16);
        Controls.Add(MakeLabel(peer.Name.ToUpperInvariant(), Section, Peer, new Point(left, y), leftW));
        Controls.Add(MakeLabel("IST HIER", Section, Accent, new Point(boxX, y), boxW));
        y += S(24);

        var local = ctl.LocalScreenIds();
        var links = ctl.MonitorLinks(peer);
        foreach (var m in peer.Displays?.Monitors ?? new())
        {
            var sn = AppController.SerialText(m.PhysicalKey);
            var name = $"{m.Number}   {m.Name}";
            var sub = string.Join("  ·  ", new[] { m.Tech, sn }.Where(x => x.Length > 0));
            Controls.Add(MakeLabel(name, Body, Theme.Text, new Point(left, y), leftW));
            Controls.Add(MakeLabel(sub, Small, TextDim, new Point(left, y + S(22)), leftW));

            var box = MakeCombo(new Point(boxX, y + S(6)), boxW);
            box.Items.Add(new Item { Id = "", Label = $"– nur an „{peer.Name}“ angeschlossen –" });
            foreach (var id in local)
            {
                var lsn = AppController.SerialText(Edid.PhysicalKey(id));
                var tech = ctl.TechOf(id);
                var extra = string.Join(", ", new[] { tech, lsn }.Where(x => x.Length > 0));
                box.Items.Add(new Item { Id = id, Label = $"{ctl.NumberOf(id)}   {ctl.NameOf(id)}" + (extra.Length > 0 ? $"  ({extra})" : "") });
            }
            Select(box, links.TryGetValue(m.Id, out var l) ? l : "");
            Controls.Add(box);
            _rows.Add((m, box));
            y += S(52);
        }
        if (_rows.Count == 0)
        {
            Controls.Add(MakeLabel($"Von „{peer.Name}“ sind noch keine Bildschirme bekannt.", Body, TextDim, new Point(left, y), w));
            y += S(32);
        }

        _error = MakeLabel("", Small, Danger, new Point(left, y + S(4)), w);
        Controls.Add(_error);
        y += S(30);
        ClientSize = new Size(left + w + S(24), y + S(56));

        var auto = new Btn("Automatisch erkennen");
        var ok = new Btn("Speichern", primary: true) { Width = S(120) };
        var cancel = new Btn("Abbrechen") { Width = S(110) };
        ok.Location = new Point(ClientSize.Width - S(24) - ok.Width, ClientSize.Height - S(20) - ok.Height);
        cancel.Location = new Point(ok.Left - S(10) - cancel.Width, ok.Top);
        auto.Location = new Point(left, ok.Top);
        auto.Click += (_, _) =>
        {
            var a = ctl.MonitorLinks(peer, useManual: false);
            foreach (var (m, box) in _rows) Select(box, a.TryGetValue(m.Id, out var x) ? x : "");
            _error.Text = "";
        };
        ok.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange(new Control[] { auto, cancel, ok });
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    static void Select(ComboBox box, string id)
    {
        for (int i = 0; i < box.Items.Count; i++)
            if (string.Equals(((Item)box.Items[i]!).Id, id, StringComparison.OrdinalIgnoreCase)) { box.SelectedIndex = i; return; }
        box.SelectedIndex = 0;
    }

    void Save()
    {
        var chosen = _rows.Select(r => (r.m, id: ((Item?)r.box.SelectedItem)?.Id ?? "")).ToList();
        var dup = chosen.Where(c => c.id.Length > 0).GroupBy(c => c.id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            _error.Text = $"„{_ctl.NameOf(dup.Key)}“ ist mehreren Bildschirmen zugeordnet – jeder Bildschirm hier nur einmal.";
            return;
        }
        _peer.Links = chosen.ToDictionary(c => c.m.Id, c => c.id, StringComparer.OrdinalIgnoreCase);
        _ctl.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
