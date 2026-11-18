using System.Runtime.InteropServices;

namespace AleVista;

/// <summary>P/Invoke für das Teilen von Maus &amp; Tastatur: Low-Level-Hooks, SendInput, Cursor, Bildschirme, Zwischenablage.</summary>
internal static class ShareNative
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    public const int WM_QUIT = 0x0012;
    public const int WM_APP = 0x8000;
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    public const int WM_MOUSEHWHEEL = 0x020E;

    public const uint LLMHF_INJECTED = 0x01;
    public const uint LLKHF_EXTENDED = 0x01;
    public const uint LLKHF_INJECTED = 0x10;
    public const uint LLKHF_UP = 0x80;

    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100;
    public const uint MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;

    const uint SPI_SETCURSORS = 0x0057;
    const uint MONITORINFOF_PRIMARY = 1;
    static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    static readonly uint[] SystemCursorIds =
        { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651 };

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    static extern IntPtr CreateCursor(IntPtr hInst, int xHot, int yHot, int w, int h, byte[] andMask, byte[] xorMask);

    [DllImport("user32.dll")]
    static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll")]
    static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);

    [DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // ---------- DPI: alle Koordinaten beim Teilen sind physische Pixel ----------

    /// <summary>Schaltet den aktuellen Thread auf „Per-Monitor-DPI v2“ (physische Koordinaten). Liefert den alten Kontext.</summary>
    public static IntPtr EnterPhysical()
    {
        try { return SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch { return IntPtr.Zero; }
    }

    public static void LeavePhysical(IntPtr old)
    {
        if (old == IntPtr.Zero) return;
        try { SetThreadDpiAwarenessContext(old); } catch { }
    }

    public static Point GetCursorPhysical()
    {
        var old = EnterPhysical();
        try { return GetCursorPos(out var p) ? new Point(p.X, p.Y) : Point.Empty; }
        finally { LeavePhysical(old); }
    }

    public static void SetCursorPhysical(Point p)
    {
        var old = EnterPhysical();
        try { SetCursorPos(p.X, p.Y); }
        finally { LeavePhysical(old); }
    }

    /// <summary>Alle Bildschirme in physischen Pixeln (Hauptbildschirm beginnt bei 0,0).</summary>
    public static List<ShareScreen> GetPhysicalScreens()
    {
        var list = new List<ShareScreen>();
        var old = EnterPhysical();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
                if (GetMonitorInfo(h, ref mi))
                {
                    var r = mi.rcMonitor;
                    list.Add(new ShareScreen
                    {
                        X = r.Left, Y = r.Top, W = r.Right - r.Left, H = r.Bottom - r.Top,
                        Primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    });
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        finally { LeavePhysical(old); }

        if (list.Count == 0)
            foreach (var s in Screen.AllScreens)
                list.Add(new ShareScreen { X = s.Bounds.X, Y = s.Bounds.Y, W = s.Bounds.Width, H = s.Bounds.Height, Primary = s.Primary });
        return list;
    }

    // ---------- Eingaben erzeugen ----------

    static void Send(INPUT input)
    {
        try { SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()); } catch { }
    }

    /// <summary>Bewegt die Maus absolut (normalisiert auf den gesamten virtuellen Desktop).</summary>
    public static void MouseMoveAbsolute(Point p, Rectangle virtualDesk)
    {
        int nx = (int)Math.Round((p.X - virtualDesk.Left) * 65535.0 / Math.Max(1, virtualDesk.Width - 1));
        int ny = (int)Math.Round((p.Y - virtualDesk.Top) * 65535.0 / Math.Max(1, virtualDesk.Height - 1));
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION { mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } },
        });
    }

    public static void MouseEvent(uint flags, int data = 0) => Send(new INPUT
    {
        type = INPUT_MOUSE,
        u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } },
    });

    public static void KeyEvent(ushort vk, ushort scan, bool extended, bool up) => Send(new INPUT
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = scan,
                dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0),
            },
        },
    });

    // ---------- Cursor verstecken (solange ein anderer PC gesteuert wird) ----------

    public static void HideCursors()
    {
        try
        {
            var and = Enumerable.Repeat((byte)0xFF, 128).ToArray();
            var xor = new byte[128];
            var inst = GetModuleHandle(null);
            foreach (var id in SystemCursorIds)
            {
                var c = CreateCursor(inst, 0, 0, 32, 32, and, xor);
                if (c != IntPtr.Zero) SetSystemCursor(c, id); // SetSystemCursor übernimmt das Handle
            }
        }
        catch { }
    }

    /// <summary>Lädt das Cursor-Schema des Benutzers neu (macht HideCursors rückgängig).</summary>
    public static void RestoreCursors()
    {
        try { SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0); } catch { }
    }
}
