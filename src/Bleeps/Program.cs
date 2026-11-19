namespace Bleeps;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, "Bleeps-PA5DJ-single-instance", out bool first);
        if (!first)
        {
            MessageBox.Show("Bleeps is already running.", "Bleeps", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { Log.Exception("UI", e.Exception); MessageBox.Show(e.Exception.Message, "Bleeps", MessageBoxButtons.OK, MessageBoxIcon.Error); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled: " + e.ExceptionObject);
        Log.Info($"Bleeps {MainForm.Version} starting");

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
