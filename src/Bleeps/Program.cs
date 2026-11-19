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

        var splash = new SplashForm();
        splash.Show();
        splash.Refresh();
        var main = new MainForm();
        main.Shown += (_, _) => splash.CloseAfter(1500);
        Application.Run(main);
    }
}
