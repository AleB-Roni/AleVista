namespace AleVista;

internal static class Program
{
    const string MutexName = "AleVista.SingleInstance.7c1f3a52";
    const string ShowEventName = "AleVista.ShowWindow.7c1f3a52";

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Theme.Init();

        // Kommandozeile:  AleVista.exe --profile "Name"  → Profil anwenden und beenden (für Verknüpfungen / Stream Deck)
        int pi = Array.FindIndex(args, a => a.Equals("--profile", StringComparison.OrdinalIgnoreCase));
        if (pi >= 0)
        {
            if (pi + 1 >= args.Length)
            {
                MessageBox.Show("Verwendung: AleVista.exe --profile \"Profilname\"", "AleVista");
                return 2;
            }
            return RunProfile(args[pi + 1]);
        }

        bool startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
            return 0;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var ctl = new AppController(Store.Load());
        using var form = new MainForm(ctl, startHidden, showEvent);
        Application.Run(form);
        GC.KeepAlive(mutex);
        return 0;
    }

    static int RunProfile(string name)
    {
        var ctl = new AppController(Store.Load());
        ctl.Refresh();
        var p = ctl.Data.Profiles.FirstOrDefault(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (p == null)
        {
            MessageBox.Show($"Profil „{name}“ nicht gefunden.", "AleVista", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        var r = ctl.Apply(p);
        if (!r.Ok)
        {
            MessageBox.Show(r.Message, "AleVista", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        return 0;
    }
}
