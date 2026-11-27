using static AleVista.Theme;

namespace AleVista;

/// <summary>Reiter „Maus &amp; Tastatur teilen“.</summary>
internal sealed class SharePage : Panel
{
    readonly ShareService _svc;
    readonly AppController _ctl;
    readonly Form _owner;
    readonly Action<string> _status;

    readonly CardPanel _top;
    readonly Toggle _enable;
    readonly Label _pcTitle, _pcName, _keyTitle, _keyValue;
    readonly Btn _keyBtn;
    readonly Label _arrTitle;
    readonly MachineCanvas _canvas;
    readonly FlowLayoutPanel _side, _peerList;
    readonly Btn _addPeer;
    readonly Toggle _optEdge, _optCorners, _optClip, _optCursor, _optDiscover;
    readonly HotkeyCard _hotkeys;
    readonly System.Windows.Forms.Timer _cursorTimer = new() { Interval = 50 };
    readonly List<Control> _sideItems = new();
    string _peerSignature = "";
    bool _showKey, _active;

    ShareSettings S => _ctl.Data.Share;

    public SharePage(ShareService svc, AppController ctl, Form owner, Action<string> status)
    {
        _svc = svc;
        _ctl = ctl;
        _owner = owner;
        _status = status;
        BackColor = Bg;
        DoubleBuffered = true;

        // ---------- Statuskarte oben ----------
        _top = new CardPanel { Dock = DockStyle.Top, Height = S(104), Inset = new Padding(S(24), S(8), S(24), S(8)) };
        _enable = new Toggle("Maus & Tastatur teilen", "", big: true) { BackColor = Surface };
        _enable.CheckedChanged += () => SetEnabled(_enable.Checked);
        _pcTitle = SmallLabel("DIESER PC");
        _pcName = new Label { Font = Bold, ForeColor = Theme.Text, BackColor = Surface, AutoSize = false, UseMnemonic = false, AutoEllipsis = true };
        _keyTitle = SmallLabel("SCHLÜSSEL");
        _keyValue = new Label { Font = Body, ForeColor = Theme.Text, BackColor = Surface, AutoSize = false, UseMnemonic = false, AutoEllipsis = true };
        _keyBtn = new Btn("Schlüssel …", back: Surface);
        _keyBtn.Click += (_, _) => ShowKeyMenu();
        _top.Controls.AddRange(new Control[] { _enable, _pcTitle, _pcName, _keyTitle, _keyValue, _keyBtn });
        _top.Resize += (_, _) => LayoutTop();

        // ---------- Anordnung ----------
        _arrTitle = new Label
        {
            Text = "ANORDNUNG DER PCS",
            Font = Section, ForeColor = TextDim, BackColor = Bg, AutoSize = false, UseMnemonic = false,
        };
        _canvas = new MachineCanvas();
        _canvas.PeerMoved += (id, off) => _svc.SetPeerOffset(id, off);
        _canvas.MachineActivated += id => _svc.SwitchTo(id);
        _canvas.MachineMenu += (id, p) => ShowPeerMenu(id, _canvas, p);
        _canvas.AddRequested += AddPeer;

        // ---------- Seitenleiste ----------
        _side = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Bg,
        };
        _peerList = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Bg,
            Margin = Padding.Empty,
        };
        _addPeer = new Btn("+  PC per Name oder IP hinzufügen …") { Margin = new Padding(0, S(2), 0, S(18)) };
        _addPeer.Click += (_, _) => AddPeer();

        _optEdge = Option("Wechsel über Bildschirmkanten", "Maus an den Rand schieben, hinter dem ein anderer PC liegt (Strg+Alt+F9).",
            () => S.EdgeSwitching, v => S.EdgeSwitching = v);
        _optCorners = Option("Ecken sperren", "In den Bildschirmecken nicht wechseln (Startknopf, Schließen-Kreuz).",
            () => S.BlockCorners, v => S.BlockCorners = v);
        _optClip = Option("Zwischenablage teilen", "Kopierter Text ist auch auf den anderen PCs verfügbar.",
            () => S.ClipboardSync, v => S.ClipboardSync = v);
        _optCursor = Option("Mauszeiger hier ausblenden", "Solange ein anderer PC gesteuert wird.",
            () => S.HideCursor, v => S.HideCursor = v);
        _optDiscover = Option("PCs automatisch finden", "Im lokalen Netzwerk nach PCs mit demselben Schlüssel suchen.",
            () => S.AutoDiscover, v => S.AutoDiscover = v);

        _hotkeys = new HotkeyCard(new List<(string[], string)>
        {
            (new[] { "Strg", "Alt", "F1" }, "Zurück zu diesem PC"),
            (new[] { "Strg", "Alt", "F2…F4" }, "Zu PC 2 – 4 wechseln"),
            (new[] { "Strg", "Alt", "F9" }, "Kantenwechsel an / aus"),
            (new[] { "Maus an die Kante" }, "Zum PC dahinter wechseln"),
            (new[] { "Doppelklick" }, "In der Anordnung: dorthin wechseln"),
        }, S(340));

        _sideItems.AddRange(new Control[]
        {
            SectionLabel("PCS"), _peerList, _addPeer,
            SectionLabel("OPTIONEN"), _optEdge, _optCorners, _optClip, _optCursor, _optDiscover,
            SectionLabel("TASTENKÜRZEL"), _hotkeys,
        });
        _sideItems[3].Margin = new Padding(0, S(4), 0, S(6));
        _sideItems[9].Margin = new Padding(0, S(14), 0, S(6));
        _side.Controls.AddRange(_sideItems.ToArray());

        Controls.Add(_arrTitle);
        Controls.Add(_canvas);
        Controls.Add(_side);
        Controls.Add(_top);
        Resize += (_, _) => LayoutBody();

        _cursorTimer.Tick += (_, _) =>
        {
            var (id, pos) = _svc.CursorLocation();
            _canvas.SetCursor(id, pos);
        };
        HandleCreated += (_, _) => Theme.DarkScrollbars(_side);
        RefreshAll();
    }

    static Label SmallLabel(string text) => new()
    {
        Text = text, Font = Section, ForeColor = TextDim, BackColor = Surface, AutoSize = false, UseMnemonic = false,
    };

    Label SectionLabel(string text) => new()
    {
        Text = text, Font = Section, ForeColor = TextDim, BackColor = Bg, AutoSize = false, UseMnemonic = false,
        Height = S(22), Margin = new Padding(0, 0, 0, S(6)),
    };

    Toggle Option(string title, string desc, Func<bool> get, Action<bool> set)
    {
        var t = new Toggle(title, desc) { Checked = get() };
        t.CheckedChanged += () => { set(t.Checked); _ctl.Save(); };
        return t;
    }

    // ---------- Layout ----------

    void LayoutTop()
    {
        var r = _top.CardRect;
        int right = r.Right - S(18);
        _keyBtn.Location = new Point(right - _keyBtn.Width, r.Y + (r.Height - _keyBtn.Height) / 2);
        int colW = S(190);
        int keyX = _keyBtn.Left - S(16) - colW;
        int pcX = keyX - S(12) - colW;
        int lh = TextRenderer.MeasureText("Ag", Section).Height;
        int y1 = r.Y + S(22);
        _pcTitle.SetBounds(pcX, y1, colW, lh);
        _pcName.SetBounds(pcX, y1 + lh + S(2), colW, S(24));
        _keyTitle.SetBounds(keyX, y1, colW, lh);
        _keyValue.SetBounds(keyX, y1 + lh + S(4), colW, S(22));
        bool roomy = pcX - r.X > S(360);
        _pcTitle.Visible = _pcName.Visible = roomy;
        int toggleRight = roomy ? pcX - S(16) : keyX - S(16);
        _enable.Location = new Point(r.X + S(18), r.Y + S(12));
        _enable.Width = Math.Max(S(220), toggleRight - _enable.Left);
        _enable.Top = r.Y + Math.Max(S(6), (r.Height - _enable.Height) / 2);
    }

    void LayoutBody()
    {
        int top = _top.Bottom + S(6);
        int sideW = S(360);
        int bottom = Height - S(16);
        _side.SetBounds(Width - S(24) - sideW, top, sideW, Math.Max(S(100), bottom - top));
        int sw = _side.ClientSize.Width - S(4) - (_side.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
        foreach (var c in _sideItems.Where(c => c != _peerList)) c.Width = sw;
        foreach (Control c in _peerList.Controls) c.Width = sw;
        _hotkeys.Width = sw;

        int cw = Math.Max(S(240), _side.Left - S(24) - S(16));
        _arrTitle.SetBounds(S(24), top, cw, S(22));
        _canvas.SetBounds(S(24), top + S(28), cw, Math.Max(S(160), bottom - top - S(28)));
        LayoutTop();
    }

    // ---------- Aktualisieren ----------

    public void SetActive(bool active)
    {
        _active = active;
        UpdateTimer();
    }

    void UpdateTimer()
    {
        if (_active && _svc.Running) _cursorTimer.Start(); else _cursorTimer.Stop();
    }

    public void RefreshAll()
    {
        _enable.Checked = S.Enabled;
        _enable.Description = StatusText();
        _pcName.Text = _svc.MachineName;
        var key = S.Key;
        _keyValue.Text = string.IsNullOrEmpty(ShareCrypto.Normalize(key)) ? "– noch keiner –"
            : _showKey ? key : string.Concat(key.Select(c => c == '-' ? '-' : '•'));
        _optEdge.Checked = S.EdgeSwitching;
        _optCorners.Checked = S.BlockCorners;
        _optClip.Checked = S.ClipboardSync;
        _optCursor.Checked = S.HideCursor;
        _optDiscover.Checked = S.AutoDiscover;

        var target = _svc.TargetId;
        _canvas.SetWorld(_svc.World, _svc.Running, target ?? (_svc.Running ? S.MachineId : null), _svc.ControlledById);
        var (cid, cpos) = _svc.CursorLocation();
        _canvas.SetCursor(cid, cpos);
        RebuildPeers();
        UpdateTimer();
        LayoutTop();
    }

    string NameOf(string? id) => _svc.World.Find(id)?.Name ?? "anderer PC";

    string StatusText()
    {
        if (!S.Enabled) return "Aus. Einschalten, um mit einer Maus und Tastatur auch andere PCs zu bedienen, auf denen AleVista läuft.";
        if (_svc.LastError != null) return _svc.LastError;
        if (!_svc.Running) return "Startet …";
        var target = _svc.TargetId;
        if (target != null) return $"Du steuerst gerade „{NameOf(target)}“. Strg+Alt+F1 holt Maus und Tastatur zurück.";
        var by = _svc.ControlledById;
        if (by != null) return $"Dieser PC wird gerade von „{NameOf(by)}“ aus bedient.";
        int n = _svc.ConnectedCount;
        return n == 0
            ? "Bereit – warte auf andere PCs mit demselben Schlüssel …"
            : $"Bereit – {n} PC{(n == 1 ? "" : "s")} verbunden. Maus an die Kante schieben, um hinüberzuwechseln.";
    }

    void RebuildPeers()
    {
        var w = _svc.World;
        var peers = w.Machines.Where(m => !m.Local).ToList();
        var sig = string.Join("|", peers.Select(p => $"{p.Id}:{p.Name}:{p.Online}:{p.Address}:{p.Number}"));
        if (sig == _peerSignature && _peerList.Controls.Count > 0) return;
        _peerSignature = sig;

        _peerList.SuspendLayout();
        foreach (Control c in _peerList.Controls.Cast<Control>().ToList()) c.Dispose();
        _peerList.Controls.Clear();
        int width = Math.Max(S(200), _side.ClientSize.Width - S(4) - (_side.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        if (peers.Count == 0)
        {
            _peerList.Controls.Add(new Label
            {
                Text = S.Enabled
                    ? "Noch keine anderen PCs. Auf dem anderen PC AleVista starten, „Maus & Tastatur teilen“ einschalten und denselben Schlüssel eintragen – er erscheint dann automatisch."
                    : "Schalte oben „Maus & Tastatur teilen“ ein.",
                Font = Small, ForeColor = TextDim, BackColor = Bg, UseMnemonic = false,
                AutoSize = false, Width = width, Height = S(64), Margin = new Padding(0, 0, 0, S(6)),
            });
        }
        foreach (var p in peers)
        {
            string sub = p.Online ? "verbunden" : "offline";
            if (p.Address.Length > 0) sub += "  ·  " + p.Address;
            if (p.Number <= 4) sub += $"  ·  Strg+Alt+F{p.Number}";
            var row = new PeerRow(p.Id, p.Number, p.Name, sub, p.Online, Peer, width);
            row.MoreClicked += (r, anchor) => ShowPeerMenu(r.PeerId, anchor, new Point(0, anchor.Height + S(4)));
            row.DoubleClick += (_, _) => _svc.SwitchTo(row.PeerId);
            _peerList.Controls.Add(row);
        }
        _peerList.ResumeLayout();
    }

    // ---------- Aktionen ----------

    /// <summary>Teilen ein-/ausschalten (auch aus dem Tray-Menü).</summary>
    public void SetEnabled(bool on)
    {
        if (on && ShareCrypto.Normalize(S.Key).Length < 8)
        {
            var key = ShareKeyForm.Ask(_owner.Visible ? _owner : null, S.Key);
            if (key == null) { _enable.Checked = false; return; }
            S.Key = key;
        }
        S.Enabled = on;
        _ctl.Save();
        if (on)
        {
            _svc.Start();
            if (_svc.Running) _status("Teilen eingeschaltet. Falls Windows nach der Firewall fragt: Zugriff im privaten Netzwerk erlauben.");
        }
        else
        {
            _svc.Stop();
            _status("Teilen ausgeschaltet.");
        }
        RefreshAll();
    }

    void ShowKeyMenu()
    {
        var m = DarkMenu();
        m.Items.Add(_showKey ? "Schlüssel verbergen" : "Schlüssel anzeigen", null, (_, _) => { _showKey = !_showKey; RefreshAll(); });
        m.Items.Add(new ToolStripMenuItem("Schlüssel kopieren", null, (_, _) =>
        {
            try { Clipboard.SetText(S.Key); _status("Schlüssel in die Zwischenablage kopiert."); } catch { }
        }) { Enabled = S.Key.Length > 0 });
        m.Items.Add("Schlüssel ändern …", null, (_, _) => ChangeKey());
        m.Closed += (_, _) => BeginInvoke(new Action(m.Dispose));
        m.Show(_keyBtn, new Point(_keyBtn.Width - m.PreferredSize.Width, _keyBtn.Height + S(4)));
    }

    void ChangeKey()
    {
        var key = ShareKeyForm.Ask(_owner, S.Key);
        if (key == null || key == S.Key) return;
        S.Key = key;
        _ctl.Save();
        if (S.Enabled) _svc.Start();
        _status("Neuer Schlüssel gespeichert. Auf allen anderen PCs denselben Schlüssel eintragen.");
        RefreshAll();
    }

    void AddPeer()
    {
        if (!S.Enabled) { SetEnabled(true); if (!S.Enabled) return; }
        var host = InputDialog.Ask(_owner, "PC hinzufügen",
            "Name oder IP-Adresse des anderen PCs (dort muss AleVista mit demselben Schlüssel laufen):", "");
        if (host != null) _svc.ConnectManual(host);
    }

    void ShowPeerMenu(string id, Control anchor, Point at)
    {
        var w = _svc.World;
        var p = w.Find(id);
        if (p == null) return;
        var m = DarkMenu();
        if (p.Local)
        {
            m.Items.Add(new ToolStripMenuItem("Maus hierher zurückholen", null, (_, _) => _svc.ReturnHome())
                { Enabled = _svc.TargetId != null, ShortcutKeyDisplayString = "Strg+Alt+F1" });
        }
        else
        {
            var go = new ToolStripMenuItem("Zu diesem PC wechseln", null, (_, _) => _svc.SwitchTo(id)) { Enabled = p.Online };
            if (p.Number <= 4) go.ShortcutKeyDisplayString = $"Strg+Alt+F{p.Number}";
            m.Items.Add(go);
            m.Items.Add(new ToolStripMenuItem("Neu verbinden", null, (_, _) => _svc.Reconnect(id)) { Enabled = S.Enabled && p.Address.Length > 0 });
            var peer = S.Peers.FirstOrDefault(x => x.Id == id);
            m.Items.Add(new ToolStripMenuItem("Bildschirme zuordnen …", null, (_, _) =>
            {
                if (peer != null && MonitorLinkForm.Edit(_owner, _ctl, peer)) _ctl.NotifyChanged();
            }) { Enabled = peer?.Displays != null });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Entfernen", null, (_, _) =>
            {
                if (MessageBox.Show(_owner, $"„{p.Name}“ aus der Liste entfernen?\n\nEr wird nicht mehr automatisch verbunden. Über „PC hinzufügen“ lässt er sich wieder aufnehmen.",
                        "AleVista", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _svc.RemovePeer(id);
            }) { ForeColor = Danger });
        }
        m.Closed += (_, _) => BeginInvoke(new Action(m.Dispose));
        m.Show(anchor, at);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cursorTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Verbindungsschlüssel festlegen – muss auf allen PCs gleich sein.</summary>
internal sealed class ShareKeyForm : DarkForm
{
    readonly TextBox _box;
    public string Value => _box.Text.Trim().ToUpperInvariant();

    public static string? Ask(IWin32Window? owner, string current)
    {
        using var f = new ShareKeyForm(current);
        return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
    }

    ShareKeyForm(string current)
    {
        Text = "Verbindungsschlüssel";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(S(480), S(258));

        Controls.Add(MakeLabel("Verbindungsschlüssel", Bold, Theme.Text, new Point(S(22), S(18)), S(430)));
        Controls.Add(MakeLabel(
            "Alle PCs, die Maus und Tastatur teilen sollen, brauchen denselben Schlüssel. " +
            "Auf dem ersten PC den vorgeschlagenen Schlüssel übernehmen, auf den anderen PCs genau diesen Schlüssel eintippen. " +
            "Die Verbindung ist damit verschlüsselt.",
            Small, TextDim, new Point(S(22), S(48)), S(436), S(66)));

        var initial = ShareCrypto.Normalize(current).Length >= 8 ? current : ShareCrypto.Generate();
        _box = MakeTextBox(initial, new Point(S(22), S(124)), S(436));
        _box.Font = new Font("Consolas", 14f);
        _box.CharacterCasing = CharacterCasing.Upper;
        Controls.Add(_box);

        var hint = MakeLabel("", Small, Danger, new Point(S(22), S(164)), S(436));
        Controls.Add(hint);

        var regen = new Btn("Neu erzeugen");
        var ok = new Btn("Speichern", primary: true) { Width = S(110) };
        var cancel = new Btn("Abbrechen") { Width = S(110) };
        ok.Location = new Point(ClientSize.Width - S(22) - ok.Width, ClientSize.Height - S(20) - ok.Height);
        cancel.Location = new Point(ok.Left - S(10) - cancel.Width, ok.Top);
        regen.Location = new Point(S(22), ok.Top);
        void Accept()
        {
            if (ShareCrypto.Normalize(Value).Length < 8) { hint.Text = "Mindestens 8 Buchstaben/Ziffern."; return; }
            DialogResult = DialogResult.OK;
            Close();
        }
        regen.Click += (_, _) => { _box.Text = ShareCrypto.Generate(); hint.Text = ""; };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange(new Control[] { regen, cancel, ok });

        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Accept(); }
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
        };
        Shown += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
}
