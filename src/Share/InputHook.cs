using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using static AleVista.ShareNative;

namespace AleVista;

/// <summary>
/// Globale Low-Level-Hooks für Maus und Tastatur auf einem eigenen Thread (mit Nachrichtenschleife,
/// Per-Monitor-DPI → alle Koordinaten in physischen Pixeln). Die Handler müssen schnell sein,
/// sonst entfernt Windows den Hook stillschweigend.
/// </summary>
internal sealed class InputHook : IDisposable
{
    public delegate bool MouseHandler(int msg, in MSLLHOOKSTRUCT data);
    public delegate bool KeyHandler(in KBDLLHOOKSTRUCT data);

    const uint WM_RUN = WM_APP + 1;

    readonly MouseHandler _onMouse;
    readonly KeyHandler _onKey;
    readonly HookProc _mouseProc, _keyProc; // Referenzen halten, sonst sammelt der GC die Delegates ein
    readonly ConcurrentQueue<Action> _queue = new();
    readonly ManualResetEventSlim _ready = new();
    Thread? _thread;
    uint _threadId;
    IntPtr _mouseHook, _keyHook;

    public bool Installed { get; private set; }

    public InputHook(MouseHandler onMouse, KeyHandler onKey)
    {
        _onMouse = onMouse;
        _onKey = onKey;
        _mouseProc = MouseProc;
        _keyProc = KeyProc;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "AleVista.Share.Hook", Priority = ThreadPriority.Highest };
        _thread.Start();
        _ready.Wait(3000);
    }

    void Run()
    {
        EnterPhysical();
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // Nachrichtenwarteschlange anlegen
        var mod = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
        _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, mod, 0);
        Installed = _mouseHook != IntPtr.Zero && _keyHook != IntPtr.Zero;
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_RUN)
                while (_queue.TryDequeue(out var a))
                    try { a(); } catch { }
        }

        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
    }

    /// <summary>Führt eine Aktion auf dem Hook-Thread aus, nachdem der aktuelle Hook-Aufruf zurückgekehrt ist.</summary>
    public void Post(Action a)
    {
        _queue.Enqueue(a);
        if (_threadId == 0 || !PostThreadMessage(_threadId, WM_RUN, IntPtr.Zero, IntPtr.Zero))
            while (_queue.TryDequeue(out var x)) try { x(); } catch { }
    }

    IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var d = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (_onMouse(wParam.ToInt32(), in d)) return 1;
            }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    IntPtr KeyProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var d = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (_onKey(in d)) return 1;
            }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
        _ready.Dispose();
    }
}

/// <summary>Meldet Änderungen der Zwischenablage (unsichtbares Nachrichtenfenster, UI-Thread).</summary>
internal sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    readonly Action _changed;

    public ClipboardWatcher(Action changed)
    {
        _changed = changed;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        AddClipboardFormatListener(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE)
            try { _changed(); } catch { }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }
}
