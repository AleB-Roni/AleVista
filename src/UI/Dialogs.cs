using static AleVista.Theme;

namespace AleVista;

internal class DarkForm : Form
{
    public DarkForm()
    {
        BackColor = Bg;
        ForeColor = Theme.Text;
        Font = Body;
        AutoScaleMode = AutoScaleMode.None;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar(this);
    }

    protected static Label MakeLabel(string text, Font font, Color color, Point loc, int width, int height = 0)
    {
        return new Label
        {
            Text = text,
            Font = font,
            ForeColor = color,
            BackColor = Bg,
            Location = loc,
            AutoSize = false,
            UseMnemonic = false,
            Size = new Size(width, height > 0 ? height : TextRenderer.MeasureText("Ag", font).Height + S(4)),
        };
    }

    protected static TextBox MakeTextBox(string text, Point loc, int width) => new()
    {
        Text = text,
        Location = loc,
        Width = width,
        BackColor = Surface2,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 11f),
    };

    protected static ComboBox MakeCombo(Point loc, int width) => new()
    {
        Location = loc,
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Surface2,
        ForeColor = Theme.Text,
        Font = Body,
    };
}

internal static class InputDialog
{
    public static string? Ask(IWin32Window? owner, string title, string prompt, string initial)
    {
        using var f = new InputForm(title, prompt, initial);
        return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
    }

    sealed class InputForm : DarkForm
    {
        readonly TextBox _box;
        public string Value => _box.Text.Trim();

        public InputForm(string title, string prompt, string initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(420), S(160));

            Controls.Add(MakeLabel(prompt, Body, TextDim, new Point(S(20), S(18)), S(380)));
            _box = MakeTextBox(initial, new Point(S(20), S(46)), S(380));
            Controls.Add(_box);

            var ok = new Btn("OK", primary: true) { Width = S(100) };
            var cancel = new Btn("Abbrechen") { Width = S(100) };
            ok.Location = new Point(ClientSize.Width - S(20) - ok.Width, ClientSize.Height - S(20) - ok.Height);
            cancel.Location = new Point(ok.Left - S(10) - cancel.Width, ok.Top);
            ok.Click += (_, _) => { if (Value.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
            cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);

            _box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && Value.Length > 0) { e.SuppressKeyPress = true; DialogResult = DialogResult.OK; Close(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
            };
            Shown += (_, _) => { _box.Focus(); _box.SelectAll(); };
        }
    }
}

/// <summary>„Anzeige beibehalten?“ mit Countdown – setzt automatisch zurück, falls man nichts mehr sieht.</summary>
internal sealed class ConfirmForm : DarkForm
{
    int _left;
    readonly Label _count;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public ConfirmForm(int seconds)
    {
        _left = Math.Max(5, seconds);
        Text = "AleVista";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(440), S(170));

        Controls.Add(MakeLabel("Anzeige beibehalten?", Bold, Theme.Text, new Point(S(22), S(20)), S(400)));
        _count = MakeLabel("", Body, TextDim, new Point(S(22), S(52)), S(400), S(44));
        Controls.Add(_count);

        var keep = new Btn("Beibehalten", primary: true) { Width = S(130) };
        var revert = new Btn("Zurücksetzen") { Width = S(130) };
        keep.Location = new Point(ClientSize.Width - S(22) - keep.Width, ClientSize.Height - S(20) - keep.Height);
        revert.Location = new Point(keep.Left - S(10) - revert.Width, keep.Top);
        keep.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        revert.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(keep);
        Controls.Add(revert);

        AcceptButtonKeys();
        UpdateText();
        _timer.Tick += (_, _) =>
        {
            _left--;
            if (_left <= 0) { _timer.Stop(); DialogResult = DialogResult.Cancel; Close(); return; }
            UpdateText();
        };
        Shown += (_, _) => { Activate(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Dispose();
    }

    void AcceptButtonKeys()
    {
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
    }

    void UpdateText() =>
        _count.Text = $"Die vorherige Anordnung wird in {_left} Sekunde{(_left == 1 ? "" : "n")} wiederhergestellt, falls du nicht bestätigst.";
}

/// <summary>Große Nummer auf jedem Monitor, um sie zuzuordnen.</summary>
internal sealed class IdentifyForm : Form
{
    readonly int _number;
    readonly string _name, _sub;

    public IdentifyForm(Rectangle screen, int number, string name, string sub)
    {
        _number = number; _name = name; _sub = sub;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Surface;
        AutoScaleMode = AutoScaleMode.None;
        var size = new Size(S(320), S(200));
        Bounds = new Rectangle(screen.X + (screen.Width - size.Width) / 2, screen.Y + (screen.Height - size.Height) / 2, size.Width, size.Height);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        var t = new System.Windows.Forms.Timer { Interval = 3500 };
        t.Tick += (_, _) => { t.Dispose(); Close(); };
        Shown += (_, _) => t.Start();
        Click += (_, _) => Close();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var pen = new Pen(Accent, Sf(3)))
            g.DrawRectangle(pen, Sf(1.5f), Sf(1.5f), Width - Sf(3), Height - Sf(3));
        TextRenderer.DrawText(g, _number.ToString(), Big, new Rectangle(0, S(16), Width, S(110)), Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, _name, Bold, new Rectangle(S(10), S(126), Width - S(20), S(26)), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _sub, Small, new Rectangle(S(10), S(152), Width - S(20), S(22)), TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
