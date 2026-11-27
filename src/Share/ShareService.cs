using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using static AleVista.ShareNative;
using static AleVista.ShareGeometry;

namespace AleVista;

/// <summary>
/// Maus &amp; Tastatur mit anderen PCs teilen (ähnlich „Mouse without Borders“).
///
/// Ablauf: Alle PCs mit demselben Schlüssel finden sich per UDP-Broadcast und verbinden sich per TCP
/// (verschlüsselt). Jeder PC kennt die Anordnung aller PCs („Welt“-Koordinaten in physischen Pixeln).
/// Stößt die Maus an eine Kante, hinter der ein anderer PC liegt, wird die lokale Eingabe abgefangen
/// (Low-Level-Hooks) und an den anderen PC geschickt, der sie per SendInput nachspielt.
///
/// Threads: Einstellungen werden nur auf dem UI-Thread geändert. Hook- und Netzwerk-Threads lesen
/// den unveränderlichen Schnappschuss <see cref="World"/>; der Steuerzustand ist durch <c>_lock</c> geschützt.
/// </summary>
internal sealed class ShareService : IDisposable
{
    public const int DiscoveryPort = 24861;
    const int CornerMargin = 40;

    const uint VK_F1 = 0x70, VK_F4 = 0x73, VK_F9 = 0x78;

    readonly AppController _ctl;
    readonly Action<Action> _ui;
    readonly object _lock = new();
    readonly Dictionary<string, PeerConnection> _conns = new();
    readonly HashSet<string> _connecting = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, string> _peerHosts = new();

    InputHook? _hook;
    TcpListener? _listener;
    UdpClient? _udp;
    System.Threading.Timer? _timer;
    ClipboardWatcher? _clip;
    byte[]? _master;
    string _fingerprint = "";
    volatile bool _running;
    volatile ShareWorld _world = ShareWorld.Empty;
    List<ShareScreen> _localScreens = new();
    string? _lastClipboard;
    string _myId;
    string _myName = Environment.MachineName;

    // --- Steuerzustand: dieser PC steuert einen anderen (unter _lock) ---
    string? _target;
    Point _vpos, _park;
    bool _parkPending, _cursorHidden;
    int _localButtons, _remoteButtons, _mods;
    readonly HashSet<uint> _localKeys = new(), _passKeys = new(), _swallowUps = new();

    // --- dieser PC wird von einem anderen gesteuert (unter _lock) ---
    string? _controlledBy;
    readonly HashSet<(ushort vk, ushort scan, bool ext)> _injectedKeys = new();
    int _injectedButtons;
    Point _lastInjected;
    long _lastTakeBack;

    public event Action? Changed;
    public event Action<string>? Notice;
    /// <summary>Monitore eines anderen PCs haben sich geändert (oder er ist on-/offline gegangen).</summary>
    public event Action? DisplaysChanged;

    string? _lastDisplaysJson;
    DisplaySnapshot? _beforeRemoteApply;

    public ShareService(AppController ctl, Action<Action> ui)
    {
        _ctl = ctl;
        _ui = ui;
        if (string.IsNullOrWhiteSpace(S.MachineId)) S.MachineId = Guid.NewGuid().ToString("N");
        _myId = S.MachineId;
        RestoreCursors(); // falls AleVista beim letzten Mal mit verstecktem Cursor abgestürzt ist
        RefreshLocalScreens(notify: false);
    }

    ShareSettings S => _ctl.Data.Share;

    // ================= Öffentliche Schnittstelle (UI-Thread) =================

    public bool Running => _running;
    public string? LastError { get; private set; }
    public ShareWorld World => _world;
    public string MachineName => _myName;

    public string? TargetId { get { lock (_lock) return _target; } }
    public string? ControlledById { get { lock (_lock) return _controlledBy; } }

    public bool IsConnected(string id) { lock (_lock) return _conns.TryGetValue(id, out var c) && !c.IsClosed; }
    public int ConnectedCount { get { lock (_lock) return _conns.Count; } }

    /// <summary>Wo ist die Maus gerade? (PC-ID und Position in dessen Koordinaten)</summary>
    public (string MachineId, Point Pos) CursorLocation()
    {
        lock (_lock)
            if (_target != null) return (_target, _vpos);
        return (_myId, GetCursorPhysical());
    }

    public void Start()
    {
        Stop(notify: false);
        LastError = null;
        _lastDisplaysJson = null;
        if (!S.Enabled) { RaiseChanged(); return; }

        var key = ShareCrypto.Normalize(S.Key);
        if (key.Length < 8)
        {
            LastError = "Bitte zuerst einen Verbindungsschlüssel festlegen.";
            RaiseChanged();
            return;
        }
        _myId = S.MachineId;
        _master = ShareCrypto.MasterKey(key);
        _fingerprint = ShareCrypto.Fingerprint(_master);
        RefreshLocalScreens(notify: false);

        try
        {
            _listener = new TcpListener(IPAddress.Any, S.Port);
            _listener.Start();
        }
        catch (Exception ex)
        {
            _listener = null;
            LastError = $"Port {S.Port} kann nicht geöffnet werden ({ex.Message}).";
            RaiseChanged();
            return;
        }

        _running = true;
        var listener = _listener;
        new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "AleVista.Share.Accept" }.Start();

        try
        {
            var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            udp.EnableBroadcast = true;
            _udp = udp;
            new Thread(() => DiscoveryLoop(udp)) { IsBackground = true, Name = "AleVista.Share.Discovery" }.Start();
        }
        catch { _udp = null; } // Suche im Netzwerk ist optional – manuelles Verbinden geht trotzdem

        _hook = new InputHook(OnMouse, OnKey);
        _hook.Start();
        if (!_hook.Installed) LastError = "Maus/Tastatur konnten nicht abgefangen werden – Weitergabe an andere PCs nicht möglich.";

        try { _clip = new ClipboardWatcher(OnLocalClipboard); } catch { _clip = null; }
        _timer = new System.Threading.Timer(_ => Tick(), null, 300, 3000);
        RaiseChanged();
    }

    public void Stop() => Stop(notify: true);

    void Stop(bool notify)
    {
        _running = false;
        _timer?.Dispose();
        _timer = null;
        lock (_lock)
        {
            if (_target != null) EndCaptureLocked(null, direct: true);
            ReleaseInjectedLocked();
            _controlledBy = null;
            _localKeys.Clear(); _passKeys.Clear(); _swallowUps.Clear();
            _localButtons = 0; _mods = 0;
        }
        _hook?.Dispose();
        _hook = null;
        _clip?.Dispose();
        _clip = null;
        try { _listener?.Stop(); } catch { }
        _listener = null;
        try { _udp?.Close(); } catch { }
        _udp = null;

        List<PeerConnection> all;
        lock (_lock) { all = _conns.Values.ToList(); _conns.Clear(); }
        foreach (var c in all) c.Close();
        if (_cursorHidden) { RestoreCursors(); _cursorHidden = false; }
        if (notify) { RebuildWorld(); Changed?.Invoke(); }
    }

    public void Dispose() => Stop(notify: false);

    /// <summary>Nach Änderungen an Bildschirmen (Profil gewechselt, Monitor ab-/angesteckt).</summary>
    public void RefreshLocalScreens(bool notify = true)
    {
        var screens = GetPhysicalScreens();
        bool changed = screens.Count != _localScreens.Count ||
                       screens.Zip(_localScreens).Any(t => t.First.Rect != t.Second.Rect || t.First.Primary != t.Second.Primary);
        _localScreens = screens;
        RebuildWorld();
        if (!changed) return;
        Broadcast(Proto.Screens(screens));
        if (notify) Changed?.Invoke();
    }

    public void ConnectManual(string host)
    {
        host = host.Trim();
        if (host.Length == 0) return;
        if (!_running) { Notice?.Invoke("Zuerst „Maus & Tastatur teilen“ einschalten."); return; }
        Notice?.Invoke($"Verbinde mit {host} …");
        ConnectTo(host, manual: true);
    }

    public void Reconnect(string id)
    {
        PeerConnection? c;
        lock (_lock) _conns.TryGetValue(id, out c);
        c?.Close();
        var p = S.Peers.FirstOrDefault(x => x.Id == id);
        if (p != null && p.Host.Length > 0 && _running) ConnectTo(p.Host, manual: true);
    }

    public void RemovePeer(string id)
    {
        var p = S.Peers.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        S.Peers.Remove(p);
        if (!S.IgnoredIds.Contains(id)) S.IgnoredIds.Add(id);
        PeerConnection? c;
        lock (_lock) { _conns.TryGetValue(id, out c); _conns.Remove(id); if (_target == id) EndCaptureLocked(null); }
        c?.Close();
        _ctl.Save();
        RebuildWorld();
        Changed?.Invoke();
    }

    /// <summary>Neue Position eines PCs in der Anordnung (nach dem Ziehen). Wird an alle verbundenen PCs verteilt.</summary>
    public void SetPeerOffset(string id, Point offset)
    {
        var p = S.Peers.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        p.X = offset.X;
        p.Y = offset.Y;
        _ctl.Save();
        RebuildWorld();
        SendArrangement();
        Changed?.Invoke();
    }

    /// <summary>Direkt zu einem PC wechseln (Maus landet in der Mitte seines Hauptbildschirms).</summary>
    public void SwitchTo(string id)
    {
        lock (_lock)
        {
            if (id == _myId) { if (_target != null) EndCaptureLocked(null); return; }
            var m = _world.Find(id);
            if (m == null || !_conns.ContainsKey(id)) return;
            if (_target == null) BeginCaptureLocked(m, m.Center, GetCursorPhysical());
            else if (_target != id) SwitchTargetLocked(m, m.Center);
        }
    }

    public void ReturnHome()
    {
        lock (_lock) if (_target != null) EndCaptureLocked(null);
    }

    // ================= Monitore über PCs hinweg (gemeinsame Profile) =================

    static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new();

    string DisplaysJson() => System.Text.Json.JsonSerializer.Serialize(_ctl.ExportDisplays(), JsonOpts);

    /// <summary>Eigene Monitore an alle verbundenen PCs schicken (nur bei Änderungen). UI-Thread.</summary>
    public void PublishDisplays()
    {
        if (!_running) return;
        var json = DisplaysJson();
        if (json == _lastDisplaysJson) return;
        _lastDisplaysJson = json;
        Broadcast(Proto.Displays(json));
    }

    /// <summary>Einen anderen PC bitten, genau diese Monitore einzuschalten. false = nicht verbunden.</summary>
    public bool RequestApply(string peerId, List<string> monitors, string? primary)
    {
        PeerConnection? c;
        lock (_lock) _conns.TryGetValue(peerId, out c);
        if (c == null || c.IsClosed) return false;
        c.Send(Proto.ApplyDisplays(monitors, primary));
        return true;
    }

    /// <summary>Anderen PC bitten, seine vorherige Anordnung wiederherzustellen.</summary>
    public void RequestRevert(string peerId) => SendToPeer(peerId, Proto.RevertDisplays);

    void SendToPeer(string id, byte[] payload)
    {
        PeerConnection? c;
        lock (_lock) _conns.TryGetValue(id, out c);
        c?.Send(payload);
    }

    void OnApplyRequest(PeerConnection c, List<string> monitors, string? primary)
    {
        if (!_running) return;
        try { _beforeRemoteApply = DisplayManager.CaptureActive(); } catch { _beforeRemoteApply = null; }
        var r = _ctl.ApplyMonitors(monitors, primary);
        c.Send(Proto.ApplyResult(r.Ok, r.Message));
        Notice?.Invoke(r.Ok ? $"Monitore von „{c.PeerName}“ aus umgeschaltet." : $"Umschalten von „{c.PeerName}“ aus fehlgeschlagen: {r.Message}");
    }

    void OnRevertRequest(PeerConnection c)
    {
        var before = _beforeRemoteApply;
        _beforeRemoteApply = null;
        if (before == null || !_running) return;
        var r = DisplayManager.Apply(before);
        Notice?.Invoke(r.Ok ? $"Vorherige Anordnung wiederhergestellt (auf Wunsch von „{c.PeerName}“)." : "Zurücksetzen fehlgeschlagen: " + r.Message);
    }

    // ================= Welt / Anordnung =================

    void RebuildWorld()
    {
        var list = new List<MachineView>
        {
            new(_myId, _myName, true, true, Point.Empty, _localScreens, 1, ""),
        };
        int n = 2;
        foreach (var p in S.Peers)
            list.Add(new MachineView(p.Id, p.Name, false, IsConnected(p.Id), new Point(p.X, p.Y), p.Screens, n++, p.Host));
        _world = new ShareWorld(list);

        _peerHosts.Clear();
        foreach (var p in S.Peers)
            if (p.Host.Length > 0) _peerHosts[p.Id] = p.Host;
    }

    void RaiseChanged() => _ui(() => { RebuildWorld(); Changed?.Invoke(); });
    void RaiseStateChanged() => _ui(() => Changed?.Invoke());
    void RaiseNotice(string text) => _ui(() => Notice?.Invoke(text));

    void SendArrangement()
    {
        var items = new List<(string, int, int)> { (_myId, 0, 0) };
        items.AddRange(S.Peers.Select(p => (p.Id, p.X, p.Y)));
        Broadcast(Proto.Arrangement(items));
    }

    /// <summary>Anordnung von einem anderen PC übernehmen (in eigene Koordinaten umrechnen).</summary>
    void ApplyArrangement(List<(string Id, int X, int Y)> items)
    {
        var me = items.FirstOrDefault(i => i.Id == _myId);
        if (me.Id == null) return;
        bool any = false;
        foreach (var (id, x, y) in items)
        {
            if (id == _myId) continue;
            var p = S.Peers.FirstOrDefault(q => q.Id == id);
            if (p == null) continue;
            p.X = x - me.X;
            p.Y = y - me.Y;
            any = true;
        }
        if (!any) return;
        _ctl.Save();
        RebuildWorld();
        Changed?.Invoke();
    }

    /// <summary>Neuen PC rechts neben die bisherige Anordnung setzen (oben bündig).</summary>
    Point DefaultPlacement(List<ShareScreen> screens)
    {
        var mv = new MachineView("", "", false, false, Point.Empty, screens, 0, "");
        int right = int.MinValue, top = 0;
        foreach (var m in _world.Machines)
        {
            var wb = m.WorldBounds;
            if (wb.Right > right) { right = wb.Right; top = wb.Top; }
        }
        if (right == int.MinValue) right = 0;
        return new Point(right - mv.Bounds.X, top - mv.Bounds.Y);
    }

    // ================= Netzwerk =================

    void Broadcast(byte[] payload)
    {
        List<PeerConnection> all;
        lock (_lock) all = _conns.Values.ToList();
        foreach (var c in all) c.Send(payload);
    }

    void SendTo(string id, byte[] payload)
    {
        if (_conns.TryGetValue(id, out var c)) c.Send(payload);
    }

    void AcceptLoop(TcpListener listener)
    {
        while (_running)
        {
            TcpClient c;
            try { c = listener.AcceptTcpClient(); }
            catch { return; }
            ThreadPool.QueueUserWorkItem(_ => Establish(c, initiator: false, host: null, manual: false));
        }
    }

    void ConnectTo(string host, bool manual)
    {
        lock (_lock)
            if (!_connecting.Add(host)) return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                string h = host;
                int port = S.Port;
                int colon = host.LastIndexOf(':');
                if (colon > 0 && host.IndexOf(':') == colon && int.TryParse(host[(colon + 1)..], out var pp))
                {
                    h = host[..colon];
                    port = pp;
                }
                var c = new TcpClient();
                try
                {
                    if (!c.ConnectAsync(h, port).Wait(4000)) throw new TimeoutException("Zeitüberschreitung");
                }
                catch
                {
                    c.Dispose();
                    throw;
                }
                Establish(c, initiator: true, host: host, manual: manual);
            }
            catch (Exception ex)
            {
                if (manual)
                {
                    var msg = (ex is AggregateException ae ? ae.InnerException ?? ex : ex).Message;
                    RaiseNotice($"Keine Verbindung zu {host}: {msg} – läuft AleVista dort mit eingeschaltetem Teilen? Firewall freigegeben?");
                }
            }
            finally
            {
                lock (_lock) _connecting.Remove(host);
            }
        });
    }

    void Establish(TcpClient tcp, bool initiator, string? host, bool manual)
    {
        var master = _master;
        if (master == null || !_running) { tcp.Dispose(); return; }
        string addr = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? host ?? "?";

        PeerConnection conn;
        try
        {
            conn = PeerConnection.Open(tcp, master, initiator, Proto.Hello(_myId, _myName, _localScreens));
        }
        catch (CryptographicException)
        {
            tcp.Dispose();
            RaiseNotice($"Verbindung von {addr} abgelehnt – der Schlüssel stimmt nicht überein.");
            return;
        }
        catch (Exception ex)
        {
            tcp.Dispose();
            if (manual) RaiseNotice($"Keine Verbindung zu {host ?? addr}: {ex.Message}. Stimmt der Schlüssel auf beiden PCs überein?");
            return;
        }

        if (conn.PeerId == _myId || string.IsNullOrEmpty(conn.PeerId)) { conn.Close(); return; }

        PeerConnection? old = null;
        bool keep = true;
        lock (_lock)
        {
            if (_conns.TryGetValue(conn.PeerId, out old) && !old.IsClosed)
            {
                // Doppelte Verbindung (beide PCs haben gleichzeitig verbunden): beide Seiten behalten dieselbe.
                string winner = string.CompareOrdinal(_myId, conn.PeerId) < 0 ? _myId : conn.PeerId;
                string newInit = conn.Initiator ? _myId : conn.PeerId;
                string oldInit = old.Initiator ? _myId : conn.PeerId;
                keep = newInit == oldInit || newInit == winner;
            }
            if (keep)
            {
                _conns[conn.PeerId] = conn;
                conn.Message += OnMessage;
                conn.Closed += OnClosed;
            }
        }
        if (!keep) { conn.Close(); return; }
        if (old != null && !ReferenceEquals(old, conn)) old.Close();
        conn.Start();

        _ui(() =>
        {
            if (S.IgnoredIds.Contains(conn.PeerId))
            {
                if (!manual) { lock (_lock) if (_conns.TryGetValue(conn.PeerId, out var cur) && ReferenceEquals(cur, conn)) _conns.Remove(conn.PeerId); conn.Close(); return; }
                S.IgnoredIds.Remove(conn.PeerId);
            }
            var p = S.Peers.FirstOrDefault(x => x.Id == conn.PeerId);
            if (p == null)
            {
                var pos = DefaultPlacement(conn.PeerScreens);
                p = new SharePeer { Id = conn.PeerId, X = pos.X, Y = pos.Y };
                S.Peers.Add(p);
            }
            p.Name = conn.PeerName;
            p.Screens = conn.PeerScreens;
            p.Host = host ?? conn.RemoteIp;
            p.LastSeen = DateTime.Now;
            _ctl.Save();
            RebuildWorld();
            // Beim Verbinden gibt der PC mit der kleineren ID die gemeinsame Anordnung vor (beide Seiten stimmen dann überein).
            if (string.CompareOrdinal(_myId, conn.PeerId) < 0) SendArrangement();
            Notice?.Invoke($"„{conn.PeerName}“ verbunden.");
            try { conn.Send(Proto.Displays(DisplaysJson())); } catch { }
            Changed?.Invoke();
            DisplaysChanged?.Invoke();
        });
    }

    void OnClosed(PeerConnection conn)
    {
        bool was;
        lock (_lock)
        {
            was = _conns.TryGetValue(conn.PeerId, out var cur) && ReferenceEquals(cur, conn);
            if (was)
            {
                _conns.Remove(conn.PeerId);
                if (_target == conn.PeerId) EndCaptureLocked(null);
                if (_controlledBy == conn.PeerId) { ReleaseInjectedLocked(); _controlledBy = null; }
            }
        }
        if (was && _running)
            _ui(() => { RebuildWorld(); Notice?.Invoke($"Verbindung zu „{conn.PeerName}“ getrennt."); Changed?.Invoke(); DisplaysChanged?.Invoke(); });
    }

    void OnMessage(PeerConnection c, MsgType t, BinaryReader r)
    {
        switch (t)
        {
            case MsgType.Screens:
            {
                var screens = Proto.ReadScreens(r);
                _ui(() =>
                {
                    var p = S.Peers.FirstOrDefault(x => x.Id == c.PeerId);
                    if (p == null) return;
                    p.Screens = screens;
                    _ctl.Save();
                    RebuildWorld();
                    Changed?.Invoke();
                });
                break;
            }
            case MsgType.Arrangement:
            {
                int n = r.ReadInt32();
                if (n < 0 || n > 64) return;
                var items = new List<(string, int, int)>();
                for (int i = 0; i < n; i++) items.Add((r.ReadString(), r.ReadInt32(), r.ReadInt32()));
                _ui(() => ApplyArrangement(items));
                break;
            }
            case MsgType.Displays:
            {
                var json = r.ReadString();
                RemoteDisplays? d = null;
                try { d = System.Text.Json.JsonSerializer.Deserialize<RemoteDisplays>(json, JsonOpts); } catch { }
                if (d == null) break;
                _ui(() =>
                {
                    var p = S.Peers.FirstOrDefault(x => x.Id == c.PeerId);
                    if (p == null) return;
                    p.Displays = d;
                    _ctl.Save();
                    DisplaysChanged?.Invoke();
                });
                break;
            }
            case MsgType.ApplyDisplays:
            {
                int n = r.ReadInt32();
                if (n < 0 || n > 64) break;
                var list = new List<string>();
                for (int i = 0; i < n; i++) list.Add(r.ReadString());
                var prim = r.ReadString();
                _ui(() => OnApplyRequest(c, list, prim.Length > 0 ? prim : null));
                break;
            }
            case MsgType.ApplyResult:
            {
                bool ok = r.ReadBoolean();
                var msg = r.ReadString();
                _ui(() => Notice?.Invoke(ok ? $"„{c.PeerName}“ umgeschaltet." : $"„{c.PeerName}“ konnte nicht umschalten: {msg}"));
                break;
            }
            case MsgType.RevertDisplays:
                _ui(() => OnRevertRequest(c));
                break;
            case MsgType.Clipboard:
            {
                var text = r.ReadString();
                _ui(() => OnRemoteClipboard(text));
                break;
            }
            case MsgType.Enter:
            {
                var p = new Point(r.ReadInt32(), r.ReadInt32());
                lock (_lock)
                {
                    if (_controlledBy != c.PeerId) ReleaseInjectedLocked();
                    _controlledBy = c.PeerId;
                    InjectMoveLocked(p);
                }
                RaiseStateChanged();
                break;
            }
            case MsgType.Leave:
            {
                bool changed = false;
                lock (_lock)
                    if (_controlledBy == c.PeerId) { ReleaseInjectedLocked(); _controlledBy = null; changed = true; }
                if (changed) RaiseStateChanged();
                break;
            }
            case MsgType.MouseMove:
            {
                var p = new Point(r.ReadInt32(), r.ReadInt32());
                lock (_lock) if (_controlledBy == c.PeerId) InjectMoveLocked(p);
                break;
            }
            case MsgType.MouseButton:
            {
                byte b = r.ReadByte();
                bool down = r.ReadBoolean();
                lock (_lock) if (_controlledBy == c.PeerId) InjectButtonLocked(b, down);
                break;
            }
            case MsgType.MouseWheel:
            {
                int delta = r.ReadInt32();
                bool horizontal = r.ReadBoolean();
                lock (_lock) if (_controlledBy == c.PeerId) MouseEvent(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, delta);
                break;
            }
            case MsgType.Key:
            {
                ushort vk = r.ReadUInt16(), scan = r.ReadUInt16();
                bool ext = r.ReadBoolean(), up = r.ReadBoolean();
                lock (_lock)
                {
                    if (_controlledBy != c.PeerId) break;
                    if (up) _injectedKeys.Remove((vk, scan, ext)); else _injectedKeys.Add((vk, scan, ext));
                    KeyEvent(vk, scan, ext, up);
                }
                break;
            }
            case MsgType.TakeBack:
            {
                bool changed = false;
                lock (_lock)
                    if (_target == c.PeerId) { EndCaptureLocked(null); changed = true; }
                if (changed) RaiseNotice($"„{c.PeerName}“ wird gerade direkt bedient – Maus ist zurück auf diesem PC.");
                break;
            }
        }
    }

    // ---------- Suche im Netzwerk + Wiederverbinden ----------

    void Tick()
    {
        if (!_running) return;
        try
        {
            var msg = Encoding.UTF8.GetBytes($"ALVD1\n{_fingerprint}\n{_myId}\n{S.Port}\n{_myName}");
            foreach (var ep in BroadcastAddresses())
                try { _udp?.Send(msg, msg.Length, new IPEndPoint(ep, DiscoveryPort)); } catch { }
        }
        catch { }

        foreach (var (id, host) in _peerHosts)
            if (!IsConnected(id)) ConnectTo(host, manual: false);
    }

    static IEnumerable<IPAddress> BroadcastAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    if (mask.All(b => b == 0)) continue;
                    var bc = new byte[4];
                    for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                    list.Add(new IPAddress(bc));
                }
            }
        }
        catch { }
        return list.Distinct();
    }

    void DiscoveryLoop(UdpClient udp)
    {
        while (_running)
        {
            try
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                var data = udp.Receive(ref from);
                var parts = Encoding.UTF8.GetString(data).Split('\n');
                if (parts.Length < 5 || parts[0] != "ALVD1" || parts[1] != _fingerprint) continue;
                string id = parts[2];
                if (id == _myId || !S.AutoDiscover || IsConnected(id)) continue;
                if (S.IgnoredIds.Contains(id)) continue;
                // Nur der PC mit der kleineren ID baut die Verbindung auf (vermeidet Doppelverbindungen).
                if (string.CompareOrdinal(_myId, id) > 0 && _peerHosts.ContainsKey(id)) continue;
                int port = int.TryParse(parts[3], out var pp) ? pp : S.Port;
                var ip = from.Address.MapToIPv4().ToString();
                ConnectTo(port == S.Port ? ip : $"{ip}:{port}", manual: false);
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (!_running) return; }
            catch { }
        }
    }

    // ---------- Zwischenablage (UI-Thread) ----------

    void OnLocalClipboard()
    {
        if (!S.ClipboardSync || !_running) return;
        string? text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
        if (string.IsNullOrEmpty(text) || text == _lastClipboard || text.Length > Proto.MaxClipboardChars) return;
        _lastClipboard = text;
        Broadcast(Proto.Clipboard(text));
    }

    void OnRemoteClipboard(string text)
    {
        if (!S.ClipboardSync || string.IsNullOrEmpty(text) || text == _lastClipboard) return;
        _lastClipboard = text;
        try { Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true, 5, 100); } catch { }
    }

    // ================= Eingaben abfangen (Hook-Thread) =================

    bool Ctrl => (_mods & 1) != 0;
    bool Alt => (_mods & 2) != 0;
    bool Shift => (_mods & 4) != 0;
    bool Win => (_mods & 8) != 0;

    void UpdateMods(uint vk, bool up)
    {
        int bit = vk switch
        {
            0xA2 or 0xA3 or 0x11 => 1,
            0xA4 or 0xA5 or 0x12 => 2,
            0xA0 or 0xA1 or 0x10 => 4,
            0x5B or 0x5C => 8,
            _ => 0,
        };
        if (bit == 0) return;
        if (up) _mods &= ~bit; else _mods |= bit;
    }

    static (byte button, int bit, bool down)? ButtonOf(int msg, uint mouseData) => msg switch
    {
        WM_LBUTTONDOWN => (1, 1, true),
        WM_LBUTTONUP => (1, 1, false),
        WM_RBUTTONDOWN => (2, 2, true),
        WM_RBUTTONUP => (2, 2, false),
        WM_MBUTTONDOWN => (3, 4, true),
        WM_MBUTTONUP => (3, 4, false),
        WM_XBUTTONDOWN => (mouseData >> 16) == 2 ? ((byte)5, 16, true) : ((byte)4, 8, true),
        WM_XBUTTONUP => (mouseData >> 16) == 2 ? ((byte)5, 16, false) : ((byte)4, 8, false),
        _ => null,
    };

    bool OnMouse(int msg, in MSLLHOOKSTRUCT d)
    {
        if ((d.flags & LLMHF_INJECTED) != 0) return false;
        var pt = new Point(d.pt.X, d.pt.Y);
        var btn = ButtonOf(msg, d.mouseData);

        lock (_lock)
        {
            if (_target == null)
            {
                if (btn is { } b0)
                {
                    if (b0.down) _localButtons |= b0.bit; else _localButtons &= ~b0.bit;
                }
                if (_controlledBy != null)
                {
                    bool real = msg != WM_MOUSEMOVE || Math.Abs(pt.X - _lastInjected.X) + Math.Abs(pt.Y - _lastInjected.Y) > 6;
                    if (real) TakeBackLocked();
                }
                return msg == WM_MOUSEMOVE && TryEdgeLocked(pt);
            }

            // --- Ein anderer PC wird gesteuert: alles abfangen und weiterschicken ---
            switch (msg)
            {
                case WM_MOUSEMOVE:
                {
                    if (_parkPending) return true;
                    int dx = pt.X - _park.X, dy = pt.Y - _park.Y;
                    if (dx != 0 || dy != 0) MoveVirtualLocked(dx, dy);
                    return true;
                }
                case WM_MOUSEWHEEL:
                case WM_MOUSEHWHEEL:
                    SendTo(_target, Proto.Wheel((short)(d.mouseData >> 16), msg == WM_MOUSEHWHEEL));
                    return true;
            }
            if (btn is { } b)
            {
                if (!b.down && (_remoteButtons & b.bit) == 0 && (_localButtons & b.bit) != 0)
                {
                    _localButtons &= ~b.bit; // Taste wurde vor dem Wechsel lokal gedrückt → lokal loslassen
                    return false;
                }
                if (b.down) _remoteButtons |= b.bit; else _remoteButtons &= ~b.bit;
                SendTo(_target, Proto.Button(b.button, b.down));
            }
            return true;
        }
    }

    bool OnKey(in KBDLLHOOKSTRUCT k)
    {
        if ((k.flags & LLKHF_INJECTED) != 0) return false;
        bool up = (k.flags & LLKHF_UP) != 0;
        uint vk = k.vkCode;

        lock (_lock)
        {
            UpdateMods(vk, up);
            if (up && _swallowUps.Remove(vk)) return true;

            // Tastenkürzel (funktionieren immer, egal welcher PC gerade gesteuert wird)
            if (!up && Ctrl && Alt && !Shift && !Win)
            {
                if (vk >= VK_F1 && vk <= VK_F4)
                {
                    _swallowUps.Add(vk);
                    HotkeyMachineLocked((int)(vk - VK_F1) + 1);
                    return true;
                }
                if (vk == VK_F9)
                {
                    _swallowUps.Add(vk);
                    _ui(() =>
                    {
                        S.EdgeSwitching = !S.EdgeSwitching;
                        _ctl.Save();
                        Notice?.Invoke(S.EdgeSwitching ? "Wechsel über Bildschirmkanten eingeschaltet." : "Wechsel über Bildschirmkanten ausgeschaltet – nur noch per Strg+Alt+F1–F4.");
                        Changed?.Invoke();
                    });
                    return true;
                }
            }

            if (_target == null)
            {
                if (_controlledBy != null && !up) TakeBackLocked();
                if (up) _localKeys.Remove(vk); else _localKeys.Add(vk);
                return false;
            }

            if (up && _passKeys.Remove(vk))
            {
                _localKeys.Remove(vk); // war vor dem Wechsel lokal gedrückt → lokal loslassen
                return false;
            }
            SendTo(_target, Proto.Key((ushort)vk, (ushort)k.scanCode, (k.flags & LLKHF_EXTENDED) != 0, up));
            return true;
        }
    }

    void HotkeyMachineLocked(int number)
    {
        if (number == 1)
        {
            if (_target != null) EndCaptureLocked(null);
            return;
        }
        var m = _world.Machines.FirstOrDefault(x => x.Number == number);
        if (m == null || !_conns.ContainsKey(m.Id))
        {
            RaiseNotice(m == null ? $"Es gibt keinen PC Nr. {number}." : $"„{m.Name}“ ist gerade nicht verbunden.");
            return;
        }
        if (_target == null) BeginCaptureLocked(m, m.Center, GetCursorPhysical());
        else if (_target != m.Id) SwitchTargetLocked(m, m.Center);
    }

    /// <summary>Maus steht lokal an einer Kante – liegt dahinter ein verbundener PC?</summary>
    bool TryEdgeLocked(Point pt)
    {
        if (!S.EdgeSwitching || _localButtons != 0 || _conns.Count == 0) return false;
        var w = _world;
        var local = w.Local;
        if (local == null) return false;

        var candidates = new List<Point>(4);
        if (Contains(local.Screens, pt))
        {
            foreach (var c in new[] { new Point(pt.X + 1, pt.Y), new Point(pt.X - 1, pt.Y), new Point(pt.X, pt.Y + 1), new Point(pt.X, pt.Y - 1) })
                if (!Contains(local.Screens, c)) candidates.Add(c);
            if (candidates.Count == 0) return false;
            if (S.BlockCorners && NearCorner(local.Screens, pt, CornerMargin)) return false;
        }
        else candidates.Add(pt);

        foreach (var c in candidates)
            foreach (var m in w.Machines)
            {
                if (m.Local || !_conns.ContainsKey(m.Id)) continue;
                var rp = new Point(c.X - m.Offset.X, c.Y - m.Offset.Y);
                if (!Contains(m.Screens, rp)) continue;
                BeginCaptureLocked(m, rp, pt);
                return true;
            }
        return false;
    }

    void BeginCaptureLocked(MachineView m, Point remotePos, Point localPos)
    {
        var w = _world;
        var screens = w.Local?.Screens ?? new[] { MachineView.DefaultScreen };
        var scr = ScreenAt(screens, localPos) ?? screens[0];
        _park = Center(scr);
        _target = m.Id;
        _vpos = remotePos;
        _remoteButtons = 0;
        _passKeys.Clear();
        foreach (var k in _localKeys) _passKeys.Add(k);
        _parkPending = true;
        SendTo(m.Id, Proto.Enter(remotePos));

        var park = _park;
        bool hide = S.HideCursor && !_cursorHidden;
        if (hide) _cursorHidden = true;
        RunOnHook(() =>
        {
            SetCursorPhysical(park);
            lock (_lock) _parkPending = false;
            if (hide) HideCursors();
        });
        RaiseStateChanged();
    }

    void SwitchTargetLocked(MachineView m, Point remotePos)
    {
        if (_target != null) SendTo(_target, Proto.Leave);
        _target = m.Id;
        _vpos = remotePos;
        _remoteButtons = 0;
        SendTo(m.Id, Proto.Enter(remotePos));
        RaiseStateChanged();
    }

    void EndCaptureLocked(Point? localPos, bool direct = false)
    {
        if (_target == null) return;
        SendTo(_target, Proto.Leave);
        _target = null;
        _remoteButtons = 0;
        _passKeys.Clear();
        _parkPending = false;
        var dest = localPos ?? _park;
        bool restore = _cursorHidden;
        _cursorHidden = false;
        void Act()
        {
            SetCursorPhysical(dest);
            if (restore) RestoreCursors();
        }
        if (direct || _hook == null) Act(); else RunOnHook(Act);
        RaiseStateChanged();
    }

    void RunOnHook(Action a)
    {
        var h = _hook;
        if (h != null) h.Post(a); else a();
    }

    /// <summary>Maus auf dem gesteuerten PC bewegen; an dessen Kanten zum nächsten PC (oder zurück) wechseln.</summary>
    void MoveVirtualLocked(int dx, int dy)
    {
        var w = _world;
        var m = w.Find(_target);
        if (m == null) { EndCaptureLocked(null); return; }

        var np = new Point(_vpos.X + dx, _vpos.Y + dy);
        if (Contains(m.Screens, np))
        {
            _vpos = np;
            SendTo(m.Id, Proto.Move(np));
            return;
        }

        if (S.EdgeSwitching && _remoteButtons == 0 && !(S.BlockCorners && NearCorner(m.Screens, _vpos, CornerMargin)))
        {
            var world = new Point(np.X + m.Offset.X, np.Y + m.Offset.Y);
            foreach (var o in w.Machines)
            {
                if (o.Id == m.Id) continue;
                var op = new Point(world.X - o.Offset.X, world.Y - o.Offset.Y);
                if (!Contains(o.Screens, op)) continue;
                if (o.Local) { EndCaptureLocked(op); return; }
                if (_conns.ContainsKey(o.Id)) { SwitchTargetLocked(o, op); return; }
            }
        }

        np = Clamp(m.Screens, _vpos, np);
        if (np == _vpos) return;
        _vpos = np;
        SendTo(m.Id, Proto.Move(np));
    }

    // ================= Eingaben nachspielen (dieser PC wird gesteuert) =================

    void InjectMoveLocked(Point p)
    {
        var w = _world;
        var screens = w.Local?.Screens;
        if (screens != null && screens.Count > 0 && !Contains(screens, p)) p = Clamp(screens, Center(screens[0]), p);
        _lastInjected = p;
        MouseMoveAbsolute(p, w.LocalDesk);
    }

    void InjectButtonLocked(byte button, bool down)
    {
        uint flags; int data = 0, bit;
        switch (button)
        {
            case 1: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; bit = 1; break;
            case 2: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; bit = 2; break;
            case 3: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; bit = 4; break;
            case 4: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = 1; bit = 8; break;
            case 5: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = 2; bit = 16; break;
            default: return;
        }
        if (down) _injectedButtons |= bit; else _injectedButtons &= ~bit;
        MouseEvent(flags, data);
    }

    /// <summary>Alle noch gedrückten Tasten/Maustasten loslassen (verhindert „hängende“ Strg-/Alt-Tasten).</summary>
    void ReleaseInjectedLocked()
    {
        foreach (var (vk, scan, ext) in _injectedKeys) KeyEvent(vk, scan, ext, up: true);
        _injectedKeys.Clear();
        for (byte b = 1; b <= 5; b++)
        {
            int bit = b switch { 1 => 1, 2 => 2, 3 => 4, 4 => 8, _ => 16 };
            if ((_injectedButtons & bit) != 0) InjectButtonLocked(b, false);
        }
        _injectedButtons = 0;
    }

    /// <summary>Jemand benutzt Maus/Tastatur direkt an diesem PC, während er ferngesteuert wird → Steuerung zurücknehmen.</summary>
    void TakeBackLocked()
    {
        if (_controlledBy == null) return;
        long now = Environment.TickCount64;
        if (now - _lastTakeBack < 500) return;
        _lastTakeBack = now;
        SendTo(_controlledBy, Proto.TakeBack);
        _controlledBy = null;
        // Läuft im Hook-Callback → Tasten erst danach loslassen (keine SendInput-Aufrufe aus dem Hook heraus)
        RunOnHook(() => { lock (_lock) if (_controlledBy == null) ReleaseInjectedLocked(); });
        RaiseStateChanged();
    }
}
