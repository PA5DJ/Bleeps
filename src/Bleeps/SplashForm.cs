using ShackAssistant.Helpers;

namespace Bleeps;

/// <summary>The splash: the Bleeps logo of the website, borderless, not in the taskbar, not TopMost (a message box at start
/// must stay reachable - the lesson of ShackAssistant's splash).</summary>
internal sealed class SplashForm : Form
{
    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        Image? logo = null;
        try
        {
            using var s = typeof(SplashForm).Assembly.GetManifestResourceStream("Splash.png");
            if (s != null) logo = Image.FromStream(s);
        }
        catch { }
        int w = LogicalToDeviceUnits(520);
        int h = logo == null ? LogicalToDeviceUnits(200) : w * logo.Height / logo.Width;
        var pic = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
        var line = new Label { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(24), TextAlign = ContentAlignment.MiddleCenter,
                               Text = $"Bleeps {MainForm.Version}{Instance.Suffix}  -  starting…", ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9) };
        // the credit (v1.0.21, PA5DJ: "put credits on the splash" - PA5DJ and the two websites, as in About)
        var credit = new Label { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(22), TextAlign = ContentAlignment.MiddleCenter,
                                 Text = "by PA5DJ  -  www.ham4life.com  -  www.pa5dj.nl", ForeColor = Color.FromArgb(60, 60, 60),
                                 Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        ClientSize = new Size(w, h + credit.Height + line.Height);
        Controls.Add(pic);
        Controls.Add(credit);
        Controls.Add(line);
        UITheme.Apply(this);
    }
}
