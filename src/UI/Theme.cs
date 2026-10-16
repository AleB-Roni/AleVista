using System.Drawing.Drawing2D;

namespace AleVista;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(17, 19, 23);
    public static readonly Color Surface = Color.FromArgb(27, 30, 36);
    public static readonly Color Surface2 = Color.FromArgb(37, 41, 49);
    public static readonly Color Well = Color.FromArgb(21, 23, 28);
    public static readonly Color Border = Color.FromArgb(50, 55, 66);
    public static readonly Color Text = Color.FromArgb(234, 236, 240);
    public static readonly Color TextDim = Color.FromArgb(146, 153, 166);
    public static readonly Color Accent = Color.FromArgb(56, 201, 170);
    public static readonly Color AccentHover = Color.FromArgb(88, 220, 192);
    public static readonly Color AccentInk = Color.FromArgb(10, 34, 29);
    public static readonly Color Danger = Color.FromArgb(240, 112, 112);

    public static float Scale { get; private set; } = 1f;

    public static Font Body = null!, Small = null!, Bold = null!, Title = null!, Section = null!, Big = null!;

    public static void Init()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            Scale = Math.Max(1f, g.DpiX / 96f);
        }
        catch { Scale = 1f; }

        Body = new Font("Segoe UI", 9.75f);
        Small = new Font("Segoe UI", 8.5f);
        Bold = new Font("Segoe UI Semibold", 10.5f);
        Title = new Font("Segoe UI Semibold", 17f);
        Section = new Font("Segoe UI Semibold", 8.25f);
        Big = new Font("Segoe UI Semibold", 40f);
    }

    public static int S(int v) => (int)Math.Round(v * Scale);
    public static float Sf(float v) => v * Scale;

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void DarkTitleBar(Form f)
    {
        try
        {
            int on = 1;
            if (Native.DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(f.Handle, 19, ref on, sizeof(int));
        }
        catch { }
    }

    public static void DarkScrollbars(Control c)
    {
        try { Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }

    public static ContextMenuStrip DarkMenu()
    {
        var m = new ContextMenuStrip
        {
            Renderer = new DarkRenderer(),
            BackColor = Surface,
            ForeColor = Text,
            Font = Body,
            ShowImageMargin = true,
        };
        return m;
    }
}

internal sealed class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemBorder => Theme.Surface2;
    public override Color MenuItemSelected => Theme.Surface2;
    public override Color MenuItemSelectedGradientBegin => Theme.Surface2;
    public override Color MenuItemSelectedGradientEnd => Theme.Surface2;
    public override Color MenuItemPressedGradientBegin => Theme.Surface2;
    public override Color MenuItemPressedGradientEnd => Theme.Surface2;
    public override Color ImageMarginGradientBegin => Theme.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Surface;
    public override Color ImageMarginGradientEnd => Theme.Surface;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Surface;
    public override Color CheckBackground => Theme.Surface;
    public override Color CheckSelectedBackground => Theme.Surface2;
    public override Color CheckPressedBackground => Theme.Surface2;
}

internal sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.TextDim : e.Item.ForeColor == Theme.Danger ? Theme.Danger : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, "✓", Theme.Bold, e.ImageRectangle, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Flacher, abgerundeter Button.</summary>
internal sealed class Btn : Control
{
    bool _hover, _down;
    public bool Primary { get; set; }

    public Btn(string text, bool primary = false, Color? back = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Primary = primary;
        Font = Theme.Body;
        BackColor = back ?? Theme.Bg;
        Cursor = Cursors.Hand;
        Height = Theme.S(32);
        FitWidth();
    }

    public Btn FitWidth()
    {
        Width = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + Theme.S(28);
        return this;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill, text, border;
        if (Primary)
        {
            fill = !Enabled ? Theme.Mix(Theme.Accent, Theme.Bg, .6f) : _down ? Theme.Accent : _hover ? Theme.AccentHover : Theme.Accent;
            text = Theme.AccentInk;
            border = fill;
        }
        else
        {
            fill = _down ? Theme.Border : _hover ? Theme.Mix(Theme.Surface2, Theme.Border, .6f) : Theme.Surface2;
            text = Enabled ? Theme.Text : Theme.TextDim;
            border = Theme.Border;
        }
        using (var path = Theme.Round(r, Theme.Sf(7)))
        using (var b = new SolidBrush(fill))
        using (var p = new Pen(border))
        {
            g.FillPath(b, path);
            g.DrawPath(p, path);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}
