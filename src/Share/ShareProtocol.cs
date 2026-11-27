using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AleVista;

internal enum MsgType : byte
{
    Hello = 1,
    Screens = 2,
    MouseMove = 3,
    MouseButton = 4,
    MouseWheel = 5,
    Key = 6,
    Enter = 7,
    Leave = 8,
    TakeBack = 9,
    Clipboard = 10,
    Ping = 11,
    Arrangement = 12,
    Displays = 13,
    ApplyDisplays = 14,
    ApplyResult = 15,
    RevertDisplays = 16,
}

/// <summary>Bausteine der Nachrichten (Typ-Byte + Inhalt, mit BinaryWriter kodiert).</summary>
internal static class Proto
{
    public const int Version = 1;
    public const int MaxClipboardChars = 1_000_000;

    public static byte[] Build(MsgType t, Action<BinaryWriter>? body = null)
    {
        var ms = new MemoryStream(16);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)t);
            body?.Invoke(w);
        }
        return ms.ToArray();
    }

    public static void WriteScreens(BinaryWriter w, IReadOnlyList<ShareScreen> screens)
    {
        w.Write(screens.Count);
        foreach (var s in screens)
        {
            w.Write(s.X); w.Write(s.Y); w.Write(s.W); w.Write(s.H); w.Write(s.Primary);
        }
    }

    public static List<ShareScreen> ReadScreens(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n < 0 || n > 32) throw new InvalidDataException("Ungültige Bildschirmliste");
        var list = new List<ShareScreen>(n);
        for (int i = 0; i < n; i++)
        {
            var s = new ShareScreen { X = r.ReadInt32(), Y = r.ReadInt32(), W = r.ReadInt32(), H = r.ReadInt32(), Primary = r.ReadBoolean() };
            if (s.W > 0 && s.H > 0 && s.W <= 32768 && s.H <= 32768) list.Add(s);
        }
        return list;
    }

    public static byte[] Hello(string id, string name, IReadOnlyList<ShareScreen> screens) => Build(MsgType.Hello, w =>
    {
        w.Write(Version); w.Write(id); w.Write(name); WriteScreens(w, screens);
    });

    public static byte[] Screens(IReadOnlyList<ShareScreen> screens) => Build(MsgType.Screens, w => WriteScreens(w, screens));
    public static byte[] Move(Point p) => Build(MsgType.MouseMove, w => { w.Write(p.X); w.Write(p.Y); });
    public static byte[] Enter(Point p) => Build(MsgType.Enter, w => { w.Write(p.X); w.Write(p.Y); });
    public static byte[] Button(byte button, bool down) => Build(MsgType.MouseButton, w => { w.Write(button); w.Write(down); });
    public static byte[] Wheel(int delta, bool horizontal) => Build(MsgType.MouseWheel, w => { w.Write(delta); w.Write(horizontal); });
    public static byte[] Key(ushort vk, ushort scan, bool extended, bool up) =>
        Build(MsgType.Key, w => { w.Write(vk); w.Write(scan); w.Write(extended); w.Write(up); });
    public static byte[] Clipboard(string text) => Build(MsgType.Clipboard, w => w.Write(text));
    public static readonly byte[] Leave = Build(MsgType.Leave);
    public static readonly byte[] TakeBack = Build(MsgType.TakeBack);
    public static readonly byte[] Ping = Build(MsgType.Ping);

    public static byte[] Displays(string json) => Build(MsgType.Displays, w => w.Write(json));
    public static byte[] ApplyDisplays(IReadOnlyList<string> monitors, string? primary) => Build(MsgType.ApplyDisplays, w =>
    {
        w.Write(monitors.Count);
        foreach (var m in monitors) w.Write(m);
        w.Write(primary ?? "");
    });
    public static byte[] ApplyResult(bool ok, string message) => Build(MsgType.ApplyResult, w => { w.Write(ok); w.Write(message); });
    public static readonly byte[] RevertDisplays = Build(MsgType.RevertDisplays);

    public static byte[] Arrangement(IEnumerable<(string Id, int X, int Y)> items) => Build(MsgType.Arrangement, w =>
    {
        var list = items.ToList();
        w.Write(list.Count);
        foreach (var (id, x, y) in list) { w.Write(id); w.Write(x); w.Write(y); }
    });
}

/// <summary>Schlüsselableitung aus dem gemeinsamen Verbindungsschlüssel.</summary>
internal static class ShareCrypto
{
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Großbuchstaben, ohne Leerzeichen/Bindestriche – „abcd-efgh“ und „ABCDEFGH“ sind gleich.</summary>
    public static string Normalize(string key) =>
        new(key.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Generate()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return sb.ToString();
    }

    public static byte[] MasterKey(string normalizedKey) => Rfc2898DeriveBytes.Pbkdf2(
        Encoding.UTF8.GetBytes(normalizedKey), Encoding.UTF8.GetBytes("AleVista.Share.v1"), 150_000, HashAlgorithmName.SHA256, 32);

    /// <summary>Kurzer Fingerabdruck, damit sich nur PCs mit demselben Schlüssel gegenseitig finden.</summary>
    public static string Fingerprint(byte[] master) =>
        Convert.ToHexString(HMACSHA256.HashData(master, Encoding.UTF8.GetBytes("AleVista.Share.Fingerprint")))[..16];
}

/// <summary>
/// Verschlüsselter Kanal über TCP: Beide Seiten tauschen Zufallswerte aus, daraus und aus dem gemeinsamen
/// Schlüssel entsteht ein Sitzungsschlüssel. Jede Nachricht ist mit AES-GCM verschlüsselt und authentifiziert;
/// ein falscher Schlüssel fällt bei der ersten Nachricht auf.
/// </summary>
internal sealed class SecureChannel : IDisposable
{
    static readonly byte[] Magic = "ALV1"u8.ToArray();
    const int MaxFrame = 8 * 1024 * 1024;

    readonly NetworkStream _stream;
    readonly AesGcm _enc, _dec;
    readonly byte _sendDir, _recvDir;
    ulong _sendCounter, _recvCounter;

    SecureChannel(NetworkStream stream, byte[] key, bool initiator)
    {
        _stream = stream;
        _enc = new AesGcm(key, 16);
        _dec = new AesGcm(key, 16);
        _sendDir = initiator ? (byte)1 : (byte)2;
        _recvDir = initiator ? (byte)2 : (byte)1;
    }

    public static SecureChannel Handshake(TcpClient client, byte[] master, bool initiator)
    {
        var s = client.GetStream();
        s.ReadTimeout = 5000;
        s.WriteTimeout = 5000;
        var mine = RandomNumberGenerator.GetBytes(32);
        s.Write(Magic.Concat(mine).ToArray());
        var buf = new byte[36];
        s.ReadExactly(buf);
        if (!buf.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("Kein AleVista auf der Gegenseite");
        var theirs = buf[4..];
        var (a, b) = initiator ? (mine, theirs) : (theirs, mine);
        var key = HMACSHA256.HashData(master, "session"u8.ToArray().Concat(a).Concat(b).ToArray());
        return new SecureChannel(s, key, initiator);
    }

    public int ReadTimeout { set => _stream.ReadTimeout = value; }

    static byte[] Nonce(byte dir, ulong counter)
    {
        var n = new byte[12];
        n[0] = dir;
        BinaryPrimitives.WriteUInt64LittleEndian(n.AsSpan(4), counter);
        return n;
    }

    /// <summary>Nur von einem Thread gleichzeitig aufrufen.</summary>
    public void Send(byte[] plain)
    {
        var frame = new byte[4 + plain.Length + 16];
        BinaryPrimitives.WriteInt32LittleEndian(frame, plain.Length + 16);
        _enc.Encrypt(Nonce(_sendDir, _sendCounter++), plain,
            frame.AsSpan(4, plain.Length), frame.AsSpan(4 + plain.Length, 16));
        _stream.Write(frame);
    }

    /// <summary>Nur von einem Thread gleichzeitig aufrufen. Wirft bei falschem Schlüssel/manipulierten Daten.</summary>
    public byte[] Receive()
    {
        var lenBuf = new byte[4];
        _stream.ReadExactly(lenBuf);
        int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len < 17 || len > MaxFrame) throw new InvalidDataException("Ungültige Nachrichtenlänge");
        var data = new byte[len];
        _stream.ReadExactly(data);
        var plain = new byte[len - 16];
        _dec.Decrypt(Nonce(_recvDir, _recvCounter++), data.AsSpan(0, len - 16), data.AsSpan(len - 16), plain);
        return plain;
    }

    public void Dispose()
    {
        _enc.Dispose();
        _dec.Dispose();
    }
}

/// <summary>Eine bestehende Verbindung zu einem anderen PC (eigener Lese- und Schreib-Thread).</summary>
internal sealed class PeerConnection
{
    readonly TcpClient _tcp;
    readonly SecureChannel _ch;
    readonly BlockingCollection<byte[]> _out = new(new ConcurrentQueue<byte[]>(), 4096);
    int _closed;

    public string PeerId { get; private init; } = "";
    public string PeerName { get; private init; } = "";
    public List<ShareScreen> PeerScreens { get; private init; } = new();
    public bool Initiator { get; private init; }
    public string RemoteIp { get; private init; } = "";
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public event Action<PeerConnection, MsgType, BinaryReader>? Message;
    public event Action<PeerConnection>? Closed;

    PeerConnection(TcpClient tcp, SecureChannel ch) { _tcp = tcp; _ch = ch; }

    /// <summary>Handshake + Begrüßung austauschen. Wirft bei Fehlern (z. B. falscher Schlüssel).</summary>
    public static PeerConnection Open(TcpClient tcp, byte[] master, bool initiator, byte[] hello)
    {
        tcp.NoDelay = true;
        var ch = SecureChannel.Handshake(tcp, master, initiator);
        try
        {
            byte[] theirs;
            if (initiator) { ch.Send(hello); theirs = ch.Receive(); }
            else { theirs = ch.Receive(); ch.Send(hello); }

            using var r = new BinaryReader(new MemoryStream(theirs), Encoding.UTF8);
            if ((MsgType)r.ReadByte() != MsgType.Hello) throw new InvalidDataException("Unerwartete Begrüßung");
            int version = r.ReadInt32();
            if (version < 1) throw new InvalidDataException("Unbekannte Protokollversion");
            var conn = new PeerConnection(tcp, ch)
            {
                PeerId = r.ReadString(),
                PeerName = r.ReadString(),
                PeerScreens = Proto.ReadScreens(r),
                Initiator = initiator,
                RemoteIp = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? "",
            };
            ch.ReadTimeout = 15000; // Gegenseite schickt spätestens alle 2 s ein Ping
            return conn;
        }
        catch
        {
            ch.Dispose();
            throw;
        }
    }

    public void Start()
    {
        new Thread(ReadLoop) { IsBackground = true, Name = "AleVista.Share.Read" }.Start();
        new Thread(WriteLoop) { IsBackground = true, Name = "AleVista.Share.Write" }.Start();
    }

    /// <summary>Nicht blockierend – darf aus dem Hook heraus aufgerufen werden.</summary>
    public void Send(byte[] payload)
    {
        if (IsClosed) return;
        bool ok;
        try { ok = _out.TryAdd(payload); } catch { ok = false; }
        if (!ok) Close(); // Gegenseite hängt
    }

    void WriteLoop()
    {
        try
        {
            while (!IsClosed)
            {
                if (_out.TryTake(out var p, 2000)) _ch.Send(p);
                else _ch.Send(Proto.Ping);
            }
        }
        catch { }
        Close();
    }

    void ReadLoop()
    {
        try
        {
            while (!IsClosed)
            {
                var p = _ch.Receive();
                using var r = new BinaryReader(new MemoryStream(p), Encoding.UTF8);
                var t = (MsgType)r.ReadByte();
                if (t == MsgType.Ping) continue;
                Message?.Invoke(this, t, r);
            }
        }
        catch { }
        Close();
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try { _tcp.Close(); } catch { }
        try { Closed?.Invoke(this); } catch { }
    }
}
