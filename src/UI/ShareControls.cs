using System.Drawing.Drawing2D;
using static AleVista.Theme;

namespace AleVista;

/// <summary>Schalter mit Titel und Beschreibung.</summary>
internal sealed class Toggle : Control
{
    bool _checked, _hover;
    string _description;
    readonly Font _titleFont;

    public event Action? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); }
    }

    public string Description
    {
        get => _description;
        set { if (_description == value) return; _description = value ?? ""; FitHeight(); Invalidate(); }
    }

    public Toggle(string title, string description, bool big = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = title;
        _description = description;
        _titleFont = big ? Bold : Body;
        BackColor = Bg;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 0, S(4));
        Width = S(300);
        FitHeight();
    }

    int TextLeft => S(52);

    public void FitHeight()
    {
        int w = Math.Max(S(60), Width - TextLeft - S(6));
        int th = TextRenderer.MeasureText("Ag", _titleFont).Height;
        int dh = string.IsNullOrEmpty(_description) ? 0 :
            TextRenderer.MeasureText(_description, Small, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        Height = S(8) + th + (dh > 0 ? S(1) + dh : 0) + S(8);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        int old = Height;
        FitHeight();
        if (old != Height) Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled) return;
        _checked = !_checked;
        Invalidate();
        CheckedChanged?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int th = TextRenderer.MeasureText("Ag", _titleFont).Height;
        var sw = new RectangleF(Sf(2), S(8) + (th - Sf(20)) / 2f, Sf(38), Sf(20));
        var on = _checked && Enabled;
        Color track = on ? Accent : _hover && Enabled ? Theme.Mix(Surface2, Border, .7f) : Surface2;
        using (var p = Round(sw, sw.Height / 2))
        using (var b = new SolidBrush(_checked && !Enabled ? Theme.Mix(Accent, Bg, .6f) : track))
        using (var pen = new Pen(on ? Accent : Border))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        float d = sw.Height - Sf(6);
        float kx = _checked ? sw.Right - Sf(3) - d : sw.X + Sf(3);
        using (var kb = new SolidBrush(_checked ? AccentInk : Enabled ? Theme.Text : TextDim))
            g.FillEllipse(kb, kx, sw.Y + Sf(3), d, d);

        int x = TextLeft, w = Width - x - S(6);
        TextRenderer.DrawText(g, Text, _titleFont, new Rectangle(x, S(8), w, th), Enabled ? Theme.Text : TextDim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(_description))
            TextRenderer.DrawText(g, _description, Small, new Rectangle(x, S(8) + th + S(1), w, Height - th - S(9)), TextDim,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Panel mit abgerundetem Karten-Hintergrund.</summary>
internal class CardPanel : Panel
{
    public Padding Inset { get; set; }

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
    }

    public Rectangle CardRect => new(Inset.Left, Inset.Top, Width - Inset.Horizontal, Height - Inset.Vertical);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = CardRect;
        using var path = Round(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1.5f, r.Height - 1.5f), Sf(12));
        using var b = new SolidBrush(Surface);
        using var pen = new Pen(Border);
        g.FillPath(b, path);
        g.DrawPath(pen, path);
    }
}

/// <summary>Zeile in der PC-Liste.</summary>
internal sealed class PeerRow : Control
{
    readonly int _number;
    readonly string _name, _sub;
    readonly Color _dot;
    readonly bool _online;
    readonly Btn _more;
    public string PeerId { get; }
    public event Action<PeerRow, Control>? MoreClicked;

    public PeerRow(string id, int number, string name, string sub, bool online, Color dot, int width)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
        PeerId = id;
        _number = number; _name = name; _sub = sub; _online = online; _dot = dot;
        Size = new Size(width, S(50));
        Margin = new Padding(0, 0, 0, S(6));
        _more = new Btn("⋯", back: Surface) { Width = S(34), Height = S(28) };
        _more.Location = new Point(Width - _more.Width - S(10), (Height - _more.Height) / 2);
        _more.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _more.Click += (_, _) => MoreClicked?.Invoke(this, _more);
        Controls.Add(_more);
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
        float d = Sf(28);
        var cr = new RectangleF(Sf(10), (Height - d) / 2, d, d);
        using (var cb = new SolidBrush(_online ? Color.FromArgb(60, _dot) : Surface2))
            g.FillEllipse(cb, cr);
        TextRenderer.DrawText(g, _number.ToString(), Bold, Rectangle.Round(cr), _online ? _dot : TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        int x = S(48);
        int w = _more.Left - x - S(8);
        TextRenderer.DrawText(g, _name, Body, new Rectangle(x, S(6), w, S(20)), _online ? Theme.Text : TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _sub, Small, new Rectangle(x, S(26), w, S(18)), _online ? _dot : TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Liste von Tastenkürzeln mit gezeichneten Tasten.</summary>
internal sealed class HotkeyCard : Control
{
    readonly List<(string[] Keys, string Text)> _rows;

    public HotkeyCard(List<(string[] Keys, string Text)> rows, int width)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
        _rows = rows;
        Size = new Size(width, S(12) + rows.Count * S(32) + S(8));
        Margin = new Padding(0, 0, 0, S(8));
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

        int keysW = S(148);
        int y = S(10);
        foreach (var (keys, text) in _rows)
        {
            int x = S(12);
            foreach (var k in keys)
            {
                var sz = TextRenderer.MeasureText(k, Small, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                var kr = new RectangleF(x, y + S(3), sz.Width + S(10), S(22));
                using (var kp = Round(kr, Sf(5)))
                using (var kb = new SolidBrush(Surface2))
                using (var pen = new Pen(Border))
                {
                    g.FillPath(kb, kp);
                    g.DrawPath(pen, kp);
                }
                TextRenderer.DrawText(g, k, Small, Rectangle.Round(kr), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                x = (int)kr.Right + S(4);
            }
            int tx = Math.Max(S(12) + keysW, x + S(6));
            TextRenderer.DrawText(g, text, Small, new Rectangle(tx, y, Width - tx - S(10), S(28)), TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            y += S(32);
        }
    }
}

/// <summary>
/// Zeigt alle PCs mit ihren Bildschirmen so an, wie sie nebeneinander stehen.
/// Andere PCs lassen sich mit der Maus an die gewünschte Kante ziehen; die Mausposition wird live angezeigt.
/// </summary>
internal sealed class MachineCanvas : Control
{
    ShareWorld _world = ShareWorld.Empty;
    ShareWorld? _pending;
    string? _cursorMachine, _activeMachine, _controlledBy;
    Point _cursorPos;
    bool _running;

    // Darstellung (während des Ziehens eingefroren, damit nichts springt)
    float _scale = 1f;
    PointF _origin;
    Rectangle _union;
    bool _frozen;

    // Ziehen
    MachineView? _dragMachine;
    Point _dragMouseStart;
    Rectangle _dragStartWorld, _dragCurrent;
    bool _dragMoved;
    Rectangle? _placeholder;

    public event Action<string, Point>? PeerMoved;
    public event Action<string>? MachineActivated;
    public event Action<string, Point>? MachineMenu;
    public event Action? AddRequested;

    public MachineCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
    }

    public void SetWorld(ShareWorld world, bool running, string? activeMachine, string? controlledBy)
    {
        _running = running;
        _activeMachine = activeMachine;
        _controlledBy = controlledBy;
        if (_dragMachine != null) { _pending = world; return; }
        _world = world;
        Invalidate();
    }

    public void SetCursor(string? machine, Point pos)
    {
        if (machine == _cursorMachine && pos == _cursorPos) return;
        _cursorMachine = machine;
        _cursorPos = pos;
        Invalidate();
    }

    static Color ColorOf(MachineView m) => m.Local ? Accent : m.Online ? Peer : TextDim;

    void ComputeTransform()
    {
        if (_frozen) return;
        var rects = _world.Machines.Select(m => m.WorldBounds).ToList();
        _placeholder = null;
        if (_world.Machines.Count == 1 && _world.Local != null)
        {
            var lb = _world.Local.WorldBounds;
            var ps = _world.Local.PrimaryScreen;
            _placeholder = new Rectangle(lb.Right, lb.Top, ps.Width, ps.Height);
            rects.Add(_placeholder.Value);
        }
        if (rects.Count == 0) { _union = Rectangle.Empty; return; }
        var u = rects[0];
        foreach (var r in rects) u = Rectangle.Union(u, r);
        // etwas Luft, damit gezogene PCs auch außen andocken können
        int mx = Math.Max(u.Width / 6, 400), my = Math.Max(u.Height / 5, 300);
        u.Inflate(mx, my);
        _union = u;
        float pad = Sf(14), bottom = Sf(26);
        float aw = Width - pad * 2, ah = Height - pad - bottom;
        if (aw <= 0 || ah <= 0 || u.Width <= 0 || u.Height <= 0) { _scale = 0; return; }
        _scale = Math.Min(aw / u.Width, ah / u.Height);
        _origin = new PointF(pad + (aw - u.Width * _scale) / 2f, pad + (ah - u.Height * _scale) / 2f);
    }

    RectangleF ToScreen(Rectangle w) => new(
        _origin.X + (w.X - _union.X) * _scale, _origin.Y + (w.Y - _union.Y) * _scale, w.Width * _scale, w.Height * _scale);

    PointF ToScreen(Point p) => new(_origin.X + (p.X - _union.X) * _scale, _origin.Y + (p.Y - _union.Y) * _scale);

    Rectangle WorldBoundsOf(MachineView m) => _dragMachine != null && m.Id == _dragMachine.Id ? _dragCurrent : m.WorldBounds;

    MachineView? HitTest(Point p)
    {
        if (_scale <= 0) return null;
        foreach (var m in _world.Machines.Reverse())
        {
            var r = ToScreen(WorldBoundsOf(m));
            r.Inflate(Sf(4), Sf(4));
            if (r.Contains(p)) return m;
        }
        return null;
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var m = HitTest(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (m != null) MachineMenu?.Invoke(m.Id, e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (m == null)
        {
            if (_placeholder != null && ToScreen(_placeholder.Value).Contains(e.Location)) AddRequested?.Invoke();
            return;
        }
        if (m.Local) return;
        _dragMachine = m;
        _dragMouseStart = e.Location;
        _dragStartWorld = m.WorldBounds;
        _dragCurrent = _dragStartWorld;
        _dragMoved = false;
        _frozen = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragMachine == null)
        {
            var m = HitTest(e.Location);
            bool onPlaceholder = _placeholder != null && _scale > 0 && ToScreen(_placeholder.Value).Contains(e.Location);
            Cursor = m != null && !m.Local ? Cursors.SizeAll : onPlaceholder ? Cursors.Hand : Cursors.Default;
            return;
        }
        int dxs = e.X - _dragMouseStart.X, dys = e.Y - _dragMouseStart.Y;
        if (!_dragMoved && Math.Abs(dxs) + Math.Abs(dys) < S(4)) return;
        _dragMoved = true;
        if (_scale <= 0) return;
        var proposed = _dragStartWorld;
        proposed.Offset((int)(dxs / _scale), (int)(dys / _scale));
        var others = _world.Machines.Where(x => x.Id != _dragMachine.Id).Select(x => x.WorldBounds).ToList();
        var snapped = ShareGeometry.Snap(proposed, others, (int)(S(14) / _scale));
        _dragCurrent = new Rectangle(snapped, proposed.Size);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMachine == null) return;
        var m = _dragMachine;
        Capture = false;
        if (_dragMoved)
        {
            var others = _world.Machines.Where(x => x.Id != m.Id).Select(x => x.WorldBounds).ToList();
            var final = ShareGeometry.Resolve(_dragCurrent, others);
            if (final != null)
            {
                var offset = new Point(final.Value.X - m.Bounds.X, final.Value.Y - m.Bounds.Y);
                _dragMachine = null;
                _frozen = false;
                if (_pending != null) { _world = _pending; _pending = null; }
                PeerMoved?.Invoke(m.Id, offset);
                Invalidate();
                return;
            }
        }
        _dragMachine = null;
        _frozen = false;
        if (_pending != null) { _world = _pending; _pending = null; }
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        var m = HitTest(e.Location);
        if (m != null) MachineActivated?.Invoke(m.Id);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var well = Round(new RectangleF(0, 0, Width - 1, Height - 1), Sf(12)))
        using (var wb = new SolidBrush(Well))
        using (var wp = new Pen(Border))
        {
            g.FillPath(wb, well);
            g.DrawPath(wp, well);
        }

        ComputeTransform();
        if (_world.Machines.Count == 0 || _scale <= 0) return;

        if (_placeholder != null) DrawPlaceholder(g, ToScreen(_placeholder.Value));

        var order = _world.Machines.Where(m => _dragMachine == null || m.Id != _dragMachine.Id).ToList();
        if (_dragMachine != null)
        {
            var dm = _world.Machines.FirstOrDefault(m => m.Id == _dragMachine.Id);
            if (dm != null) order.Add(dm);
        }
        foreach (var m in order) DrawMachine(g, m);

        // Mauszeiger
        var cm = _world.Find(_cursorMachine);
        if (cm != null && _running)
        {
            var wb = WorldBoundsOf(cm);
            var wp = new Point(_cursorPos.X - cm.Bounds.X + wb.X, _cursorPos.Y - cm.Bounds.Y + wb.Y);
            var sp = ToScreen(wp);
            float r1 = Sf(9), r2 = Sf(4.5f);
            using (var halo = new SolidBrush(Color.FromArgb(70, ColorOf(cm))))
                g.FillEllipse(halo, sp.X - r1, sp.Y - r1, r1 * 2, r1 * 2);
            using (var dot = new SolidBrush(Theme.Text))
                g.FillEllipse(dot, sp.X - r2, sp.Y - r2, r2 * 2, r2 * 2);
        }

        var hint = _world.Machines.Count > 1
            ? "PCs mit der Maus an die gewünschte Kante ziehen  ·  Doppelklick: dorthin wechseln  ·  Rechtsklick: Menü"
            : "Noch keine anderen PCs – AleVista dort starten, Teilen einschalten, denselben Schlüssel eintragen.";
        TextRenderer.DrawText(g, hint, Small, new Rectangle(S(14), Height - S(26), Width - S(28), S(20)), TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    void DrawPlaceholder(Graphics g, RectangleF r)
    {
        r.Inflate(-Sf(6), -Sf(6));
        using (var p = Round(r, Sf(8)))
        using (var pen = new Pen(Border, Sf(1.4f)) { DashStyle = DashStyle.Dash })
            g.DrawPath(pen, p);
        var rr = Rectangle.Round(r);
        int mid = rr.Y + rr.Height / 2;
        TextRenderer.DrawText(g, "+", Title, new Rectangle(rr.X, mid - S(42), rr.Width, S(40)), TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, "Weiterer PC", Bold, new Rectangle(rr.X, mid, rr.Width, S(22)), TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "erscheint hier automatisch", Small, new Rectangle(rr.X, mid + S(22), rr.Width, S(18)), TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    void DrawMachine(Graphics g, MachineView m)
    {
        var col = ColorOf(m);
        bool live = m.Local || m.Online;
        bool dragging = _dragMachine != null && m.Id == _dragMachine.Id;
        var wb = WorldBoundsOf(m);
        int ox = wb.X - m.Bounds.X, oy = wb.Y - m.Bounds.Y;

        var frame = ToScreen(wb);
        frame.Inflate(Sf(4), Sf(4));
        bool active = _running && m.Id == _activeMachine;
        using (var fp = Round(frame, Sf(9)))
        {
            using (var fb = new SolidBrush(Color.FromArgb(dragging ? 40 : active ? 30 : 14, col)))
                g.FillPath(fb, fp);
            using var pen = new Pen(live ? Color.FromArgb(active || dragging ? 255 : 150, col) : Border, active || dragging ? Sf(2f) : Sf(1.2f));
            if (!live) pen.DashStyle = DashStyle.Dash;
            g.DrawPath(pen, fp);
        }

        float gap = Math.Max(1.5f, Sf(2));
        foreach (var s in m.Screens)
        {
            var sr = ToScreen(new Rectangle(s.X + ox, s.Y + oy, s.Width, s.Height));
            sr.Inflate(-gap, -gap);
            if (sr.Width < 2 || sr.Height < 2) continue;
            using var sp = Round(sr, Sf(4));
            using var sb = new SolidBrush(live ? Color.FromArgb(46, col) : Color.FromArgb(110, Surface2));
            using var spen = new Pen(live ? Color.FromArgb(200, col) : Border, Sf(1.1f));
            g.FillPath(sb, sp);
            g.DrawPath(spen, sp);
        }

        // Beschriftung im Hauptbildschirm
        var ps = m.PrimaryScreen;
        var pr = Rectangle.Round(ToScreen(new Rectangle(ps.X + ox, ps.Y + oy, ps.Width, ps.Height)));
        pr.Inflate(-S(6), -S(4));
        var textCol = live ? Theme.Text : TextDim;
        string status = m.Local
            ? (_controlledBy != null ? "wird ferngesteuert" : "Dieser PC")
            : m.Online ? "verbunden" : "offline";
        if (pr.Height >= S(64) && pr.Width >= S(80))
        {
            int mid = pr.Y + pr.Height / 2;
            TextRenderer.DrawText(g, m.Number.ToString(), Title, new Rectangle(pr.X, mid - S(44), pr.Width, S(34)), live ? col : TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, m.Name, Bold, new Rectangle(pr.X, mid - S(8), pr.Width, S(22)), textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, status, Small, new Rectangle(pr.X, mid + S(14), pr.Width, S(18)), live ? col : TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else
        {
            TextRenderer.DrawText(g, $"{m.Number} {m.Name}", Small, pr, textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (active)
        {
            const string pill = "MAUS HIER";
            var sz = TextRenderer.MeasureText(pill, Section);
            var r = new RectangleF(frame.Right - sz.Width - S(14) - Sf(6), frame.Y + Sf(6), sz.Width + S(14), S(18));
            using var pp = Round(r, Sf(9));
            using var pb = new SolidBrush(col);
            g.FillPath(pb, pp);
            TextRenderer.DrawText(g, pill, Section, Rectangle.Round(r), m.Local ? AccentInk : Bg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>Reiter-Leiste („Monitorprofile“ | „Maus &amp; Tastatur teilen“).</summary>
internal sealed class TabStrip : Control
{
    readonly string[] _tabs;
    int _selected, _hover = -1;
    readonly List<Rectangle> _rects = new();

    public event Action<int>? SelectedChanged;

    public int Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); SelectedChanged?.Invoke(value); }
    }

    public TabStrip(params string[] tabs)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _tabs = tabs;
        BackColor = Bg;
        Height = S(42);
        Cursor = Cursors.Hand;
    }

    void Measure()
    {
        _rects.Clear();
        int x = S(16);
        foreach (var t in _tabs)
        {
            int w = TextRenderer.MeasureText(t, Bold, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + S(20);
            _rects.Add(new Rectangle(x, 0, w, Height));
            x += w + S(6);
        }
    }

    int IndexAt(Point p)
    {
        Measure();
        return _rects.FindIndex(r => r.Contains(p));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = IndexAt(e.Location);
        if (i >= 0) Selected = i;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Measure();
        using (var line = new Pen(Border))
            g.DrawLine(line, S(24), Height - 1, Width - S(24), Height - 1);
        for (int i = 0; i < _tabs.Length; i++)
        {
            var r = _rects[i];
            bool sel = i == _selected;
            var col = sel ? Theme.Text : i == _hover ? Theme.Mix(TextDim, Theme.Text, .5f) : TextDim;
            TextRenderer.DrawText(g, _tabs[i], Bold, new Rectangle(r.X, 0, r.Width, Height - S(4)), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (sel)
            {
                using var b = new SolidBrush(Accent);
                using var p = Round(new RectangleF(r.X + S(8), Height - S(3), r.Width - S(16), S(3)), Sf(1.5f));
                g.FillPath(b, p);
            }
        }
    }
}
