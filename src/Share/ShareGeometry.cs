namespace AleVista;

/// <summary>Ein PC in der gemeinsamen Anordnung. Bildschirme in eigenen Koordinaten, <see cref="Offset"/> verschiebt sie in die Anordnung.</summary>
public sealed class MachineView
{
    public static readonly Rectangle DefaultScreen = new(0, 0, 1920, 1080);

    public string Id { get; }
    public string Name { get; }
    public bool Local { get; }
    public bool Online { get; }
    public Point Offset { get; }
    public IReadOnlyList<Rectangle> Screens { get; }
    public Rectangle PrimaryScreen { get; }
    /// <summary>1 = dieser PC, 2… = weitere PCs (entspricht Strg+Alt+F1…).</summary>
    public int Number { get; }
    public string Address { get; }
    public Rectangle Bounds { get; }
    public Rectangle WorldBounds => new(Bounds.X + Offset.X, Bounds.Y + Offset.Y, Bounds.Width, Bounds.Height);

    public MachineView(string id, string name, bool local, bool online, Point offset, IReadOnlyList<ShareScreen> screens, int number, string address)
    {
        Id = id; Name = name; Local = local; Online = online; Offset = offset; Number = number; Address = address;
        var rects = screens.Select(s => s.Rect).Where(r => r.Width > 0 && r.Height > 0).ToList();
        if (rects.Count == 0) rects.Add(DefaultScreen);
        Screens = rects;
        var prim = screens.FirstOrDefault(s => s.Primary);
        PrimaryScreen = prim != null ? prim.Rect : rects[0];
        var b = rects[0];
        foreach (var r in rects) b = Rectangle.Union(b, r);
        Bounds = b;
    }

    public Point Center => new(PrimaryScreen.X + PrimaryScreen.Width / 2, PrimaryScreen.Y + PrimaryScreen.Height / 2);
}

public sealed class ShareWorld
{
    public static readonly ShareWorld Empty = new(new List<MachineView>());
    public IReadOnlyList<MachineView> Machines { get; }
    public MachineView? Local { get; }
    /// <summary>Virtueller Desktop dieses PCs (Vereinigung aller Bildschirme).</summary>
    public Rectangle LocalDesk { get; }

    public ShareWorld(IReadOnlyList<MachineView> machines)
    {
        Machines = machines;
        Local = machines.FirstOrDefault(m => m.Local);
        LocalDesk = Local?.Bounds ?? MachineView.DefaultScreen;
    }

    public MachineView? Find(string? id) => id == null ? null : Machines.FirstOrDefault(m => m.Id == id);
}

internal static class ShareGeometry
{
    public static bool Contains(IReadOnlyList<Rectangle> screens, Point p)
    {
        foreach (var r in screens) if (r.Contains(p)) return true;
        return false;
    }

    public static Rectangle? ScreenAt(IReadOnlyList<Rectangle> screens, Point p)
    {
        foreach (var r in screens) if (r.Contains(p)) return r;
        return null;
    }

    public static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>Hält einen Punkt auf dem Bildschirm, auf dem sich <paramref name="from"/> befindet.</summary>
    public static Point Clamp(IReadOnlyList<Rectangle> screens, Point from, Point to)
    {
        if (Contains(screens, to)) return to;
        var r = ScreenAt(screens, from) ?? screens[0];
        return new Point(Math.Clamp(to.X, r.Left, r.Right - 1), Math.Clamp(to.Y, r.Top, r.Bottom - 1));
    }

    /// <summary>Liegt der Punkt in einer Bildschirmecke? (Dort wird nicht gewechselt – z. B. Startknopf, Schließen-Kreuz.)</summary>
    public static bool NearCorner(IReadOnlyList<Rectangle> screens, Point p, int margin)
    {
        var r = ScreenAt(screens, p);
        if (r == null) return false;
        var s = r.Value;
        bool nearX = p.X - s.Left < margin || s.Right - 1 - p.X < margin;
        bool nearY = p.Y - s.Top < margin || s.Bottom - 1 - p.Y < margin;
        return nearX && nearY;
    }

    static bool Overlaps(Rectangle a, Rectangle b) => a.IntersectsWith(b) && Rectangle.Intersect(a, b) is { Width: > 0, Height: > 0 };

    static bool Touches(Rectangle a, Rectangle b)
    {
        bool vert = (a.Right == b.Left || b.Right == a.Left) && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > 0;
        bool horz = (a.Bottom == b.Top || b.Bottom == a.Top) && Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > 0;
        return vert || horz;
    }

    /// <summary>Lässt ein gezogenes Rechteck an Kanten der anderen einrasten (Abstand in Welt-Pixeln).</summary>
    public static Point Snap(Rectangle moving, IEnumerable<Rectangle> others, int threshold)
    {
        int bestDx = threshold + 1, bestDy = threshold + 1;
        int x = moving.X, y = moving.Y;
        foreach (var b in others)
        {
            foreach (var cx in new[] { b.Right, b.Left - moving.Width, b.Left, b.Right - moving.Width })
                if (Math.Abs(cx - moving.X) < Math.Abs(bestDx)) { bestDx = cx - moving.X; }
            foreach (var cy in new[] { b.Bottom, b.Top - moving.Height, b.Top, b.Bottom - moving.Height,
                                       b.Top + (b.Height - moving.Height) / 2 })
                if (Math.Abs(cy - moving.Y) < Math.Abs(bestDy)) { bestDy = cy - moving.Y; }
        }
        if (Math.Abs(bestDx) <= threshold) x += bestDx;
        if (Math.Abs(bestDy) <= threshold) y += bestDy;
        return new Point(x, y);
    }

    /// <summary>
    /// Endgültige Position nach dem Loslassen: keine Überlappung und an mindestens einen anderen PC angrenzend
    /// (sonst gäbe es keine Kante zum Hinüberwechseln). Liefert null, wenn keine Position passt.
    /// </summary>
    public static Point? Resolve(Rectangle moving, IReadOnlyList<Rectangle> others)
    {
        if (others.Count == 0) return moving.Location;
        if (!others.Any(o => Overlaps(o, moving)) && others.Any(o => Touches(o, moving))) return moving.Location;

        Point? best = null;
        double bestDist = double.MaxValue;
        foreach (var b in others)
        {
            int minV = Math.Max(1, Math.Min(moving.Height, b.Height) / 4);
            int minH = Math.Max(1, Math.Min(moving.Width, b.Width) / 4);
            int cy = Math.Clamp(moving.Y, b.Top - moving.Height + minV, b.Bottom - minV);
            int cx = Math.Clamp(moving.X, b.Left - moving.Width + minH, b.Right - minH);
            foreach (var c in new[]
            {
                new Point(b.Right, cy), new Point(b.Left - moving.Width, cy),
                new Point(cx, b.Bottom), new Point(cx, b.Top - moving.Height),
            })
            {
                var r = new Rectangle(c, moving.Size);
                if (others.Any(o => Overlaps(o, r))) continue;
                double d = Math.Pow(c.X - moving.X, 2) + Math.Pow(c.Y - moving.Y, 2);
                if (d < bestDist) { bestDist = d; best = c; }
            }
        }
        return best;
    }
}
