using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using static AleVista.Theme;

namespace AleVista;

internal sealed class MainForm : DarkForm
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const int HotkeyBase = 0x4100;

    readonly AppController _ctl;
    readonly EventWaitHandle? _showEvent;
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _trayMenu;
    readonly Panel _header, _current, _profilesHeader, _statusBar;
    readonly Btn _identify, _activateAll, _winSettings, _gear, _saveCurrent;
    readonly LayoutPreview _currentPreview;
    readonly FlowLayoutPanel _monitorList, _cards;
    readonly Label _status, _currentTitle;
    readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 900 };
    readonly List<int> _hotkeys = new();

    bool _allowShow, _exiting, _applying, _balloonShown;

    public MainForm(AppController ctl, bool startHidden, EventWaitHandle? showEvent)
    {
        _ctl = ctl;
        _showEvent = showEvent;
        _allowShow = !startHidden;

        Text = "AleVista";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(1040), S(720));
        MinimumSize = new Size(S(780), S(600));

        // ---------- Kopfzeile ----------
        _header = new Panel { Dock = DockStyle.Top, Height = S(84), BackColor = Bg };
        _header.Controls.Add(MakeLabel("AleVista", Title, Theme.Text, new Point(S(24), S(16)), S(300), S(36)));
        _header.Controls.Add(MakeLabel("Monitorprofile – Bildschirme per Klick an- und abschalten", Body, TextDim, new Point(S(26), S(52)), S(460)));
        _identify = new Btn("Identifizieren");
        _activateAll = new Btn("Alle aktivieren & einlesen");
        _winSettings = new Btn("Windows-Anzeige");
        _gear = new Btn("⚙") { Width = S(36) };
        _header.Controls.AddRange(new Control[] { _identify, _activateAll, _winSettings, _gear });
        _header.Resize += (_, _) => LayoutHeader();

        // ---------- Aktuell ----------
        _current = new Panel { Dock = DockStyle.Top, Height = S(262), BackColor = Bg };
        _currentTitle = MakeLabel("AKTUELLE ANORDNUNG", Section, TextDim, new Point(S(24), S(6)), S(400));
        _currentPreview = new LayoutPreview { BackColor = Bg };
        _monitorList = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Bg,
        };
        _current.Controls.AddRange(new Control[] { _currentTitle, _currentPreview, _monitorList });
        _current.Resize += (_, _) => LayoutCurrent();

        // ---------- Profile ----------
        var profilesArea = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        _profilesHeader = new Panel { Dock = DockStyle.Top, Height = S(48), BackColor = Bg };
        _profilesHeader.Controls.Add(MakeLabel("PROFILE", Section, TextDim, new Point(S(24), S(18)), S(200)));
        _saveCurrent = new Btn("Aktuelle Anordnung als Profil speichern");
        _profilesHeader.Controls.Add(_saveCurrent);
        _profilesHeader.Resize += (_, _) => _saveCurrent.Location = new Point(_profilesHeader.Width - S(24) - _saveCurrent.Width, S(8));

        _cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Bg,
            Padding = new Padding(S(16), S(4), S(16), S(16)),
        };
        profilesArea.Controls.Add(_cards);
        profilesArea.Controls.Add(_profilesHeader);

        // ---------- Statuszeile ----------
        _statusBar = new Panel { Dock = DockStyle.Bottom, Height = S(32), BackColor = Surface };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = TextDim,
            BackColor = Surface,
            Font = Small,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(S(20), 0, S(20), 0),
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        _statusBar.Controls.Add(_status);

        Controls.Add(profilesArea);
        Controls.Add(_statusBar);
        Controls.Add(_current);
        Controls.Add(_header);

        // ---------- Tray ----------
        _trayMenu = DarkMenu();
        _tray = new NotifyIcon
        {
            Text = "AleVista – Monitorprofile",
            Icon = this.Icon ?? SystemIcons.Application,
            ContextMenuStrip = _trayMenu,
            Visible = true,
        };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            // Linksklick öffnet ebenfalls das Profilmenü (schnelles Umschalten)
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_tray, null);
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        // ---------- Ereignisse ----------
        _identify.Click += (_, _) => Identify();
        _activateAll.Click += (_, _) => ActivateAllAndLearn();
        _winSettings.Click += (_, _) => OpenUri("ms-settings:display");
        _gear.Click += (_, _) => ShowSettingsMenu();
        _saveCurrent.Click += (_, _) => SaveCurrentAsProfile();

        _ctl.Changed += () =>
        {
            if (IsDisposed) return;
            if (IsHandleCreated) BeginInvoke(new Action(() => { if (!IsDisposed) RebuildAll(); }));
            else RebuildAll();
        };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); _ctl.Refresh(); };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (_showEvent != null)
        {
            var t = new Thread(() =>
            {
                while (!_exiting)
                {
                    try
                    {
                        if (_showEvent.WaitOne(500) && IsHandleCreated && !_exiting)
                            BeginInvoke(new Action(ShowFromTray));
                    }
                    catch { return; }
                }
            }) { IsBackground = true, Name = "AleVista.ShowListener" };
            t.Start();
        }

        Load += (_, _) => { LayoutHeader(); LayoutCurrent(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkScrollbars(_cards);
        DarkScrollbars(_monitorList);
        _ctl.Refresh();
        if (_ctl.LastError != null) SetStatus(_ctl.LastError);
        else SetStatus(_ctl.Master == null
            ? "Noch keine Vorlage. Klicke „Alle aktivieren & einlesen“."
            : "Bereit. Tipp: Profile lassen sich auch über das Tray-Symbol und Strg+Alt+1–9 umschalten.");
    }

    // ---------- Sichtbarkeit / Tray ----------

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    void ShowFromTray()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _ctl.Data.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_balloonShown)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(3000, "AleVista läuft weiter",
                    "Profile umschalten: Klick aufs Tray-Symbol oder Strg+Alt+1–9.", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    void ExitApp()
    {
        _exiting = true;
        _tray.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exiting = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            UnregisterHotkeys();
            _tray.Dispose();
            _refreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(() => { _refreshTimer.Stop(); _refreshTimer.Start(); }));
    }

    // ---------- Layout ----------

    void LayoutHeader()
    {
        int x = _header.Width - S(24);
        int y = S(26);
        foreach (var b in new[] { _gear, _winSettings, _activateAll, _identify })
        {
            x -= b.Width;
            b.Location = new Point(x, y);
            x -= S(8);
        }
    }

    void LayoutCurrent()
    {
        int listW = S(360);
        int top = S(30), h = _current.Height - top - S(10);
        _currentPreview.Location = new Point(S(24), top);
        _currentPreview.Size = new Size(Math.Max(S(200), _current.Width - S(48) - listW - S(16)), h);
        _monitorList.Location = new Point(_currentPreview.Right + S(16), top);
        _monitorList.Size = new Size(listW, h);
        RebuildMonitorList();
    }

    // ---------- Aufbau ----------

    void RebuildAll()
    {
        _currentPreview.Items = _ctl.PreviewCurrent();
        RebuildMonitorList();
        RebuildCards();
        RebuildTrayMenu();
        RegisterHotkeys();
    }

    void RebuildMonitorList()
    {
        _monitorList.SuspendLayout();
        foreach (Control c in _monitorList.Controls.Cast<Control>().ToList()) c.Dispose();
        _monitorList.Controls.Clear();
        int w = _monitorList.Width - S(4) - (_monitorList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
        foreach (var id in _ctl.OrderedMonitors())
        {
            var row = new MonitorRow(_ctl.NumberOf(id), _ctl.NameOf(id), _ctl.TechOf(id), _ctl.IsActive(id), _ctl.IsConnected(id), Math.Max(S(200), w));
            var monitorId = id;
            row.RenameClicked += () => RenameMonitor(monitorId);
            _monitorList.Controls.Add(row);
        }
        _monitorList.ResumeLayout();
    }

    void RebuildCards()
    {
        _cards.SuspendLayout();
        foreach (Control c in _cards.Controls.Cast<Control>().ToList()) c.Dispose();
        _cards.Controls.Clear();
        foreach (var p in _ctl.Data.Profiles)
        {
            var card = new ProfileCard(p, _ctl);
            card.ApplyClicked += c => ApplyProfileInteractive(c.Profile);
            card.EditRequested += c => EditProfile(c.Profile, isNew: false);
            card.MoreClicked += (c, anchor) => ShowCardMenu(c.Profile, anchor);
            _cards.Controls.Add(card);
        }
        var add = new AddCard();
        add.Click += (_, _) => NewProfile();
        _cards.Controls.Add(add);
        _cards.ResumeLayout();
    }

    void RebuildTrayMenu()
    {
        _trayMenu.Items.Clear();
        var head = new ToolStripMenuItem("Profil wählen") { Enabled = false, Font = Section };
        _trayMenu.Items.Add(head);
        if (_ctl.Data.Profiles.Count == 0)
            _trayMenu.Items.Add(new ToolStripMenuItem("(noch keine Profile)") { Enabled = false });
        foreach (var p in _ctl.Data.Profiles)
        {
            var item = new ToolStripMenuItem(p.Name.Replace("&", "&&")) { Checked = _ctl.IsProfileActive(p) };
            if (p.Hotkey is >= 1 and <= 9) item.ShortcutKeyDisplayString = $"Strg+Alt+{p.Hotkey}";
            var prof = p;
            item.Click += (_, _) => ApplyProfileInteractive(prof);
            _trayMenu.Items.Add(item);
        }
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Alle aktivieren && einlesen", null, (_, _) => ActivateAllAndLearn());
        _trayMenu.Items.Add("Fenster öffnen", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Beenden", null, (_, _) => ExitApp());
    }

    // ---------- Hotkeys ----------

    void UnregisterHotkeys()
    {
        if (!IsHandleCreated) return;
        foreach (var id in _hotkeys) Native.UnregisterHotKey(Handle, id);
        _hotkeys.Clear();
    }

    void RegisterHotkeys()
    {
        UnregisterHotkeys();
        if (!_ctl.Data.Settings.HotkeysEnabled || !IsHandleCreated) return;
        var failed = new List<string>();
        foreach (var p in _ctl.Data.Profiles)
        {
            if (p.Hotkey is < 1 or > 9) continue;
            int id = HotkeyBase + p.Hotkey;
            if (_hotkeys.Contains(id)) continue;
            if (Native.RegisterHotKey(Handle, id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)('0' + p.Hotkey)))
                _hotkeys.Add(id);
            else
                failed.Add($"Strg+Alt+{p.Hotkey}");
        }
        if (failed.Count > 0)
            SetStatus($"Tastenkürzel bereits von einem anderen Programm belegt: {string.Join(", ", failed)}");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            int digit = m.WParam.ToInt32() - HotkeyBase;
            var p = _ctl.Data.Profiles.FirstOrDefault(x => x.Hotkey == digit);
            if (p != null) BeginInvoke(new Action(() => ApplyProfileInteractive(p)));
            return;
        }
        base.WndProc(ref m);
    }

    // ---------- Aktionen ----------

    void ApplyProfileInteractive(Profile p)
    {
        if (_applying) return;
        _applying = true;
        try
        {
            DisplaySnapshot? before = null;
            try { before = DisplayManager.CaptureActive(); } catch { }

            SetStatus($"Schalte auf „{p.Name}“ …");
            Cursor = Cursors.WaitCursor;
            var r = _ctl.Apply(p);
            Cursor = Cursors.Default;
            if (!r.Ok)
            {
                ShowError($"„{p.Name}“ konnte nicht angewendet werden.\n\n{r.Message}");
                SetStatus(r.Message);
                return;
            }
            SetStatus($"„{p.Name}“ aktiv. {r.Message}");

            if (_ctl.Data.Settings.ConfirmAfterSwitch && before != null)
            {
                string after = "";
                try { after = DisplayManager.CaptureActive().LayoutSignature(); } catch { }
                if (after != before.LayoutSignature())
                {
                    using var dlg = new ConfirmForm(_ctl.Data.Settings.ConfirmSeconds);
                    if (dlg.ShowDialog() != DialogResult.OK)
                    {
                        var rr = DisplayManager.Apply(before);
                        SetStatus(rr.Ok ? "Vorherige Anordnung wiederhergestellt." : "Zurücksetzen fehlgeschlagen: " + rr.Message);
                    }
                }
            }
        }
        finally
        {
            Cursor = Cursors.Default;
            _applying = false;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }
    }

    void ActivateAllAndLearn()
    {
        SetStatus("Schalte alle angeschlossenen Monitore ein …");
        int err = DisplayManager.ActivateAll();
        if (err != 0)
        {
            ShowError($"Windows konnte nicht alle Monitore einschalten (Fehler {err}).");
            return;
        }
        var t = new System.Windows.Forms.Timer { Interval = 1800 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            _ctl.Refresh();
            _ctl.SetMasterFromCurrent();
            int n = _ctl.Master?.Count ?? 0;
            SetStatus($"Vorlage mit {n} Monitor{(n == 1 ? "" : "en")} eingelesen. Anordnung in „Windows-Anzeige“ anpassen – AleVista übernimmt sie automatisch.");
        };
        t.Start();
    }

    void Identify()
    {
        var cur = _ctl.Current;
        if (cur == null) return;
        var screens = Screen.AllScreens;
        for (int i = 0; i < cur.Count; i++)
        {
            var gdi = DisplayManager.GetGdiName(cur, i);
            var scr = screens.FirstOrDefault(s => string.Equals(s.DeviceName, gdi, StringComparison.OrdinalIgnoreCase));
            if (scr == null) continue;
            var id = cur.MonitorIds[i];
            new IdentifyForm(scr.Bounds, _ctl.NumberOf(id), _ctl.NameOf(id), _ctl.TechOf(id)).Show();
        }
    }

    void RenameMonitor(string id)
    {
        var k = _ctl.Known(id);
        if (k == null) return;
        var name = InputDialog.Ask(Visible ? this : null, "Monitor umbenennen",
            $"Name für Monitor {_ctl.NumberOf(id)} ({(string.IsNullOrEmpty(k.FriendlyName) ? "unbekannt" : k.FriendlyName)}) – leer lassen = Originalname:",
            k.Alias ?? k.FriendlyName);
        if (name == null) return;
        k.Alias = string.IsNullOrWhiteSpace(name) || name == k.FriendlyName ? null : name;
        _ctl.NotifyChanged();
    }

    void NewProfile()
    {
        if (_ctl.Master == null)
        {
            ShowError("Es gibt noch keine Vorlage. Klicke zuerst auf „Alle aktivieren & einlesen“.");
            return;
        }
        var p = new Profile
        {
            Name = $"Profil {_ctl.Data.Profiles.Count + 1}",
            Monitors = _ctl.Current?.MonitorIds.Where(_ctl.InMaster).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new(),
        };
        EditProfile(p, isNew: true);
    }

    void EditProfile(Profile p, bool isNew)
    {
        using var f = new ProfileEditorForm(_ctl, p, isNew);
        if (f.ShowDialog(Visible ? this : null) != DialogResult.OK) return;
        if (isNew) _ctl.Data.Profiles.Add(p);
        _ctl.NotifyChanged();
    }

    void SaveCurrentAsProfile()
    {
        var cur = _ctl.Current;
        if (cur == null || cur.Count == 0) return;
        var name = InputDialog.Ask(this, "Anordnung speichern", "Name für das neue Profil:", $"Profil {_ctl.Data.Profiles.Count + 1}");
        if (name == null) return;
        _ctl.Data.Profiles.Add(new Profile
        {
            Name = name,
            Monitors = cur.MonitorIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Fixed = cur.ToDto(),
        });
        _ctl.NotifyChanged();
        SetStatus($"„{name}“ gespeichert (exakte Anordnung inkl. Auflösungen und Positionen).");
    }

    void ShowCardMenu(Profile p, Control anchor)
    {
        var m = DarkMenu();
        m.Items.Add("Bearbeiten …", null, (_, _) => EditProfile(p, isNew: false));
        m.Items.Add("Duplizieren", null, (_, _) =>
        {
            var copy = p.Duplicate(p.Name + " (Kopie)");
            _ctl.Data.Profiles.Insert(_ctl.Data.Profiles.IndexOf(p) + 1, copy);
            _ctl.NotifyChanged();
        });
        var idx = _ctl.Data.Profiles.IndexOf(p);
        m.Items.Add(new ToolStripMenuItem("Nach links verschieben", null, (_, _) => MoveProfile(p, -1)) { Enabled = idx > 0 });
        m.Items.Add(new ToolStripMenuItem("Nach rechts verschieben", null, (_, _) => MoveProfile(p, +1)) { Enabled = idx < _ctl.Data.Profiles.Count - 1 });
        m.Items.Add("Desktop-Verknüpfung erstellen", null, (_, _) => CreateShortcut(p));
        m.Items.Add(new ToolStripSeparator());
        var del = new ToolStripMenuItem("Löschen", null, (_, _) =>
        {
            if (MessageBox.Show(this, $"Profil „{p.Name}“ löschen?", "AleVista", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _ctl.Data.Profiles.Remove(p);
            _ctl.NotifyChanged();
        }) { ForeColor = Danger };
        m.Items.Add(del);
        m.Closed += (_, _) => BeginInvoke(new Action(m.Dispose));
        m.Show(anchor, new Point(0, anchor.Height + S(4)));
    }

    void MoveProfile(Profile p, int delta)
    {
        var list = _ctl.Data.Profiles;
        int i = list.IndexOf(p), j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        _ctl.NotifyChanged();
    }

    void CreateShortcut(Profile p)
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var safe = string.Concat(p.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
            if (safe.Length == 0) safe = "Profil";
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"AleVista – {safe}.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell nicht verfügbar");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = exe;
            sc.Arguments = $"--profile \"{p.Name.Replace("\"", "")}\"";
            sc.WorkingDirectory = Path.GetDirectoryName(exe);
            sc.IconLocation = exe + ",0";
            sc.Description = $"Monitorprofil „{p.Name}“ aktivieren";
            sc.Save();
            SetStatus($"Verknüpfung auf dem Desktop erstellt: {Path.GetFileName(lnk)}");
        }
        catch (Exception ex)
        {
            ShowError("Verknüpfung konnte nicht erstellt werden: " + ex.Message);
        }
    }

    void ShowSettingsMenu()
    {
        var s = _ctl.Data.Settings;
        var m = DarkMenu();
        m.Items.Add(new ToolStripMenuItem($"Nach dem Umschalten bestätigen ({s.ConfirmSeconds} s)", null, (_, _) =>
            { s.ConfirmAfterSwitch = !s.ConfirmAfterSwitch; _ctl.Save(); }) { Checked = s.ConfirmAfterSwitch });
        m.Items.Add(new ToolStripMenuItem("Tastenkürzel Strg+Alt+1–9", null, (_, _) =>
            { s.HotkeysEnabled = !s.HotkeysEnabled; _ctl.Save(); RegisterHotkeys(); }) { Checked = s.HotkeysEnabled });
        m.Items.Add(new ToolStripMenuItem("Mit Windows starten (im Tray)", null, (_, _) => SetAutostart(!IsAutostart())) { Checked = IsAutostart() });
        m.Items.Add(new ToolStripMenuItem("Schließen = in den Tray minimieren", null, (_, _) =>
            { s.CloseToTray = !s.CloseToTray; _ctl.Save(); }) { Checked = s.CloseToTray });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Datenordner öffnen", null, (_, _) => { Directory.CreateDirectory(Store.Folder); OpenUri(Store.Folder); });
        m.Items.Add("Beenden", null, (_, _) => ExitApp());
        m.Closed += (_, _) => BeginInvoke(new Action(m.Dispose));
        m.Show(_gear, new Point(_gear.Width - m.PreferredSize.Width, _gear.Height + S(4)));
    }

    static bool IsAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("AleVista") != null;
        }
        catch { return false; }
    }

    void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue("AleVista", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("AleVista", false);
            SetStatus(on ? "AleVista startet jetzt mit Windows (im Tray)." : "Autostart deaktiviert.");
        }
        catch (Exception ex) { ShowError("Autostart konnte nicht geändert werden: " + ex.Message); }
    }

    // ---------- Hilfen ----------

    static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    void SetStatus(string text) => _status.Text = text;

    void ShowError(string text)
    {
        if (Visible)
            MessageBox.Show(this, text, "AleVista", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else
            _tray.ShowBalloonTip(5000, "AleVista", text, ToolTipIcon.Warning);
    }
}
