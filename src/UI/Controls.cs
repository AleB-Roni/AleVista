using System.Drawing.Drawing2D;
using static AleVista.Theme;

namespace AleVista;

/// <summary>Zeichnet eine Monitor-Anordnung als Miniatur.</summary>
internal sealed class LayoutPreview : Control
{
    List<PreviewItem> _items = new();
    public List<PreviewItem> Items { get => _items; set { _items = value ?? new(); Invalidate(); } }
    public string EmptyText { get; set; } = "Keine Monitore erkannt";

    public LayoutPreview()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var well = Round(new RectangleF(0, 0, Width - 1, Height - 1), Sf(9)))
        using (var wb = new SolidBrush(Well))
            g.FillPath(wb, well);

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, Small, ClientRectangle, TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var union = _items[0].Bounds;
        foreach (var it in _items) union = Rectangle.Union(union, it.Bounds);
        if (union.Width <= 0 || union.Height <= 0) return;

        float pad = Sf(12);
        float availW = Width - pad * 2, availH = Height - pad * 2;
        float scale = Math.Min(availW / union.Width, availH / union.Height);
        float ox = pad + (availW - union.Width * scale) / 2f;
        float oy = pad + (availH - union.Height * scale) / 2f;
        float gap = Math.Max(1.5f, Sf(2));

        foreach (var it in _items.OrderBy(i => i.Enabled))
        {
            var rf = new RectangleF(
                ox + (it.Bounds.X - union.X) * scale + gap,
                oy + (it.Bounds.Y - union.Y) * scale + gap,
                it.Bounds.Width * scale - gap * 2,
                it.Bounds.Height * scale - gap * 2);
            if (rf.Width < 2 || rf.Height < 2) continue;

            using var path = Round(rf, Sf(5));
            if (it.Enabled)
            {
                using var fill = new SolidBrush(Color.FromArgb(58, Accent));
                using var pen = new Pen(Accent, Sf(1.5f));
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            else
            {
                using var fill = new SolidBrush(Color.FromArgb(120, Surface2));
                using var pen = new Pen(Border, Sf(1.2f)) { DashStyle = DashStyle.Dash };
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            var col = it.Enabled ? Theme.Text : TextDim;
            var rect = Rectangle.Round(rf);
            bool roomy = rf.Height >= Sf(46) && rf.Width >= Sf(60);
            var numText = it.Number > 0 ? it.Number.ToString() : "";
            if (it.Mirror != null && it.Enabled) numText += " = " + it.Mirror;
            if (roomy)
            {
                var top = new Rectangle(rect.X, rect.Y + (int)(rect.Height * 0.16f), rect.Width, (int)(rect.Height * 0.45f));
                var bottom = new Rectangle(rect.X + S(4), top.Bottom, rect.Width - S(8), rect.Bottom - top.Bottom - S(2));
                TextRenderer.DrawText(g, numText, Bold, top, col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, it.Enabled ? it.Label : it.Label + " · aus", Small, bottom, col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, numText, Small, rect, col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            if (it.Primary && it.Enabled && rf.Width > Sf(22))
            {
                float d = Sf(7);
                using var b = new SolidBrush(Accent);
                g.FillEllipse(b, rf.Right - d - Sf(5), rf.Y + Sf(5), d, d);
            }
        }
    }
}

/// <summary>Karte für ein Profil.</summary>
internal sealed class ProfileCard : Control
{
    public Profile Profile { get; }
    readonly LayoutPreview _preview;
    readonly Btn _apply, _more;
    readonly string _summary;
    readonly bool _active;

    public event Action<ProfileCard>? ApplyClicked;
    public event Action<ProfileCard, Control>? MoreClicked;
    public event Action<ProfileCard>? EditRequested;

    public ProfileCard(Profile p, AppController ctl)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Profile = p;
        BackColor = Bg;
        Size = new Size(S(272), S(232));
        Margin = new Padding(S(8));
        _active = ctl.IsProfileActive(p);

        int on = p.Monitors.Count;
        int total = Math.Max(ctl.Master?.MonitorSet().Count ?? on, on);
        var parts = new List<string> { p.Fixed != null ? $"{on} Monitor{(on == 1 ? "" : "e")} · feste Anordnung" : $"{on} von {total} Monitoren an" };
        if (ctl.MirrorGroupsOf(p).Count > 0) parts.Add("gespiegelt");
        if (p.Hotkey is >= 1 and <= 9) parts.Add($"Strg+Alt+{p.Hotkey}");
        _summary = string.Join("  ·  ", parts);

        _preview = new LayoutPreview { Location = new Point(S(12), S(12)), Size = new Size(Width - S(24), S(118)), Items = ctl.PreviewFor(p) };
        _preview.DoubleClick += (_, _) => EditRequested?.Invoke(this);
        _preview.Click += (_, _) => Focus();

        _apply = new Btn(_active ? "Erneut anwenden" : "Aktivieren", primary: !_active, back: Surface)
        {
            Location = new Point(S(12), Height - S(12) - S(32)),
        };
        _apply.Width = Width - S(24) - S(40);
        _apply.Click += (_, _) => ApplyClicked?.Invoke(this);

        _more = new Btn("⋯", back: Surface) { Width = S(34), Location = new Point(Width - S(12) - S(34), Height - S(12) - S(32)) };
        _more.Click += (_, _) => MoreClicked?.Invoke(this, _more);

        Controls.Add(_preview);
        Controls.Add(_apply);
        Controls.Add(_more);
    }

    protected override void OnDoubleClick(EventArgs e) { EditRequested?.Invoke(this); base.OnDoubleClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Round(r, Sf(12)))
        using (var b = new SolidBrush(Surface))
        using (var pen = new Pen(_active ? Accent : Border, _active ? Sf(1.6f) : 1f))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }

        int y = S(138);
        int pillW = 0;
        if (_active)
        {
            const string pill = "AKTIV";
            var sz = TextRenderer.MeasureText(pill, Section);
            pillW = sz.Width + S(12);
            var pr = new RectangleF(Width - S(12) - pillW, y + S(2), pillW, S(19));
            using var pp = Round(pr, Sf(9));
            using var pb = new SolidBrush(Color.FromArgb(48, Accent));
            g.FillPath(pb, pp);
            TextRenderer.DrawText(g, pill, Section, Rectangle.Round(pr), Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            pillW += S(8);
        }
        TextRenderer.DrawText(g, Profile.Name, Bold, new Rectangle(S(14), y, Width - S(28) - pillW, S(24)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _summary, Small, new Rectangle(S(14), y + S(24), Width - S(28), S(20)), TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Gestrichelte „+ Neues Profil“-Karte.</summary>
internal sealed class AddCard : Control
{
    bool _hover;

    public AddCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
        Size = new Size(S(272), S(232));
        Margin = new Padding(S(8));
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(Sf(1), Sf(1), Width - Sf(2.5f), Height - Sf(2.5f));
        using (var path = Round(r, Sf(12)))
        using (var pen = new Pen(_hover ? Accent : Border, Sf(1.4f)) { DashStyle = DashStyle.Dash })
        {
            if (_hover) { using var b = new SolidBrush(Color.FromArgb(14, Accent)); g.FillPath(b, path); }
            g.DrawPath(pen, path);
        }
        var c = _hover ? Accent : TextDim;
        var mid = Height / 2;
        TextRenderer.DrawText(g, "+", Title, new Rectangle(0, mid - S(46), Width, S(44)), c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
        TextRenderer.DrawText(g, "Neues Profil", Bold, new Rectangle(0, mid, Width, S(24)), c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        TextRenderer.DrawText(g, "Monitore an-/abwählen", Small, new Rectangle(0, mid + S(24), Width, S(20)), TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
    }
}

/// <summary>Zeile in der Monitorliste.</summary>
internal sealed class MonitorRow : Control
{
    readonly int _number;
    readonly string _name, _sub;
    readonly bool _active, _connected;
    readonly Btn _rename;
    public event Action? RenameClicked;

    public MonitorRow(int number, string name, string sub, bool active, bool connected, int width)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
        _number = number; _name = name; _sub = sub; _active = active; _connected = connected;
        Size = new Size(width, S(46));
        Margin = new Padding(0, 0, 0, S(6));
        _rename = new Btn("Umbenennen", back: Surface) { Height = S(26), Font = Small };
        _rename.FitWidth();
        _rename.Location = new Point(Width - _rename.Width - S(10), (Height - _rename.Height) / 2);
        _rename.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _rename.Click += (_, _) => RenameClicked?.Invoke();
        Controls.Add(_rename);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Sf(9)))
        using (var b = new SolidBrush(Surface))
        using (var pen = new Pen(Border))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }
        float d = Sf(26);
        var cr = new RectangleF(Sf(10), (Height - d) / 2, d, d);
        using (var cb = new SolidBrush(_active ? Color.FromArgb(60, Accent) : Surface2))
            g.FillEllipse(cb, cr);
        TextRenderer.DrawText(g, _number.ToString(), Bold, Rectangle.Round(cr), _active ? Accent : TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        int x = S(46);
        int w = _rename.Left - x - S(8);
        TextRenderer.DrawText(g, _name, Body, new Rectangle(x, S(5), w, S(20)), _connected ? Theme.Text : TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var state = _active ? "an" : _connected ? "aus" : "nicht angeschlossen";
        var stateCol = _active ? Accent : TextDim;
        var subText = string.IsNullOrEmpty(_sub) ? state : _sub + "  ·  " + state;
        TextRenderer.DrawText(g, subText, Small, new Rectangle(x, S(24), w, S(18)), stateCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}
