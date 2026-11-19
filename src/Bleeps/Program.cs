namespace Bleeps;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // --rig NAME: a Bleeps of its own beside the others, as WSJT-X's --rig-name (v1.0.8) - BEFORE anything touches a folder
        Instance.Parse(args);
        using var single = new Mutex(true, "Bleeps-PA5DJ-single-instance" + (Instance.Name.Length > 0 ? "-" + Instance.Name.ToUpperInvariant() : ""), out bool first);
        if (!first)
        {
            MessageBox.Show(Instance.FolderName + " is already running." + (Instance.Name.Length == 0 ? "\n\nStart another one beside it with --rig NAME (e.g. Bleeps.exe --rig IC9700)." : ""),
                            "Bleeps", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        // Dark mode (v1.0.20, PA5DJ: "make dark mode in Bleeps"): WinForms' own colour mode, set before the first window.
        // [display] theme = System (follow Windows) / Light / Dark. A change takes effect at the next start.
        ShackAssistant.Helpers.AppTheme.ApplyAtStart(new ShackAssistant.Helpers.AppSettings().Get("display", "theme", "System"));
        Application.ThreadException += (_, e) => { Log.Exception("UI", e.Exception); MessageBox.Show(e.Exception.Message, "Bleeps", MessageBoxButtons.OK, MessageBoxIcon.Error); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled: " + e.ExceptionObject);
        Log.Info($"Bleeps {MainForm.Version} starting" + (Instance.Name.Length > 0 ? $" as '{Instance.Name}' (--rig)" : ""));

        // The splash stays SplashMs on its own before the main window appears (PA5DJ: it closed at once - the main window
        // opened straight over it). The main window is built meanwhile; it shows only when the time is up.
        const int SplashMs = 3000;
        var splash = new SplashForm();
        splash.Show();
        splash.Refresh();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var main = new MainForm();
        while (clock.ElapsedMilliseconds < SplashMs && !splash.IsDisposed)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
        main.Shown += (_, _) => { if (!splash.IsDisposed) splash.Close(); };
        Application.Run(main);
    }
}
