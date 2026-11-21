using System.Collections.Concurrent;
using System.Globalization;
using ShackAssistant.Forms;
using ShackAssistant.Helpers;
using ShackAssistant.Helpers.Audio;
using ShackAssistant.Helpers.Audio.Bleeps;

namespace Bleeps;

/// <summary>
/// The Bleeps window: a WSJT-X-like radio bar (band list, dial, band buttons) on top, the chat (the shared BleepsPanel) in the
/// middle, the waterfall at the bottom with the TX drive beside it. The receiving/sending glue is ShackAssistant's
/// CwWindow.Bleeps.cs: the receiver on its OWN thread, a queue for sending, the chat log, the stations in the waterfall.
/// </summary>
internal sealed class MainForm : Form
{
    public const string Version = "v1.0.27";

    private readonly AppSettings _settings = new();
    private List<FrequencyEntry> _freqs = FrequencyList.Load();

    // radio bar
    private readonly ComboBox _band = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 110, Font = new Font("Segoe UI", 11) };
    private readonly Label _dial = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Consolas", 22, FontStyle.Bold),
                                           BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, ForeColor = Color.FromArgb(80, 255, 80), Size = new Size(250, 44) };
    private readonly Label _mode = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(8, 14, 0, 0) };
    // satellite mode: "SAT" and the downlink (MAIN) under it; the dial is then the uplink (SUB)
    private readonly Label _sat = new() { AutoSize = true, Font = new Font("Consolas", 10, FontStyle.Bold), Margin = new Padding(10, 6, 0, 0),
                                          ForeColor = AppTheme.IsDark ? Color.FromArgb(110, 180, 255) : Color.FromArgb(0, 120, 215), Visible = false };
    private readonly FlowLayoutPanel _bandButtons = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4, 0, 4, 4) };

    // chat + waterfall
    private readonly BleepsPanel _panel;
    private readonly WaterfallPanel _waterfall = new() { Dock = DockStyle.Fill, MinHz = 100, MaxHz = 3000 };
    private readonly TrackBar _drive = new() { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 450, TickFrequency = 50, Dock = DockStyle.Right, Width = 45 };
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000 };
    private Panel _wfHost = null!;

    // status
    private readonly ToolStripStatusLabel _stRig = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _stAudio = new() { AutoSize = false, Width = 130 };
    private readonly ToolStripStatusLabel _stTx = new() { AutoSize = false, Width = 60, Text = "RX" };

    // audio, radio
    private WaveIn? _capture;
    private BleepsWaterfall _wf = null!;   // the shared Bleeps waterfall (ShackAssistant's Forms\BleepsWaterfall.cs)
    private volatile float _inPeak;
    private RigControl? _rig;
    private RigState _rigState = new(false, 0, "", "No radio");
    private readonly System.Windows.Forms.Timer _ui = new() { Interval = 40 };

    // Bleeps
    private BleepsReceiver? _rx;
    private BlockingCollection<float[]>? _rxIn;
    private Thread? _rxThread;
    private readonly TxPlayer _tx = new();
    private readonly Queue<string> _out = new();
    private bool _stopped;
    private readonly Dictionary<BleepsReceiver.Message, (long Id, DateTime Utc, string Sig, bool Logged)> _seen = new();
    private long _nextId = 1;
    private int _active;
    private List<(double Hz, string? Call)> _stations = new();
    private List<(double Hz, Color Colour, string Label)> _marks = new();
    private (double LoHz, double HiHz, Color Colour)[] _bands = Array.Empty<(double, double, Color)>();
    private sealed record OwnSent(long Id, string Text, double Hz, DateTime Utc, double Seconds);
    private readonly List<OwnSent> _ownSent = new();
    private readonly object _ownLock = new();
    private readonly Dictionary<BleepsReceiver.Message, long> _echo = new();

    private bool Duplex => _settings.GetBool("bleeps", "full_duplex", false);
    private string MyCall => _settings.Get("station", "callsign", "");

    public MainForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Bleeps " + Version + Instance.Suffix;
        Font = new Font("Segoe UI", 9);
        MinimumSize = new Size(820, 560);
        Size = new Size(1000, 760);
        StartPosition = FormStartPosition.CenterScreen;

        _panel = new BleepsPanel(_settings) { Dock = DockStyle.Fill };
        _panel.SendRequested = Send;
        _panel.StopRequested = StopSending;
        _panel.RemoveQueued = RemoveQueued;
        _panel.FreeSpotSource = () => _rx?.FreeSpot(400, 2500, _panel.Speed);
        _panel.TxHzChanged = _ => UpdateMarkers();
        _panel.OptionsChanged = () => { if (_rx != null) _rx.Threads = _settings.GetInt("bleeps", "threads", 0); _settings.Save(); };
        _panel.PingRequested = SendPing;
        _tx.Finished += why =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                _panel.SetSending(false);
                string said = why == null ? "Sent" : why == "stopped" ? "Stopped" : "Sending failed: " + why;
                _panel.SetStatus(said);
                if (why == null && !_stopped) Next();
                else { _out.Clear(); _panel.SetQueued(Array.Empty<string>()); }
            });
        };

        // ── menu ──
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("&Settings…", null, (_, _) => OpenSettings());
        file.DropDownItems.Add("Open the &chat log folder", null, (_, _) => OpenFolder(ChatFolder));
        file.DropDownItems.Add("Open the &log folder", null, (_, _) => OpenFolder(Path.GetDirectoryName(Log.FilePath)!));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("Check for &updates…", null, (_, _) => CheckForUpdates(quiet: false));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add("&About Bleeps", null, (_, _) => About());
        menu.Items.Add(file); menu.Items.Add(help);
        MainMenuStrip = menu;

        // ── radio bar, like WSJT-X: band list + dial; the band buttons under it ──
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(4, 4, 4, 2) };
        _band.Margin = new Padding(3, 10, 6, 0);
        bar.Controls.Add(_band);
        bar.Controls.Add(_dial);
        bar.Controls.Add(_mode);
        bar.Controls.Add(_sat);
        _tips.SetToolTip(_sat, "Satellite mode: the dial is TX = SUB (uplink), RX = MAIN (downlink). The selected band is read at every poll; the other one every 2 s "
                             + "by selecting it for a moment (the band marker on the radio blinks) and selecting yours again - never while sending. "
                             + "Bleeps does not tune in satellite mode.");
        _tips.SetToolTip(_band, "The band, as in WSJT-X: pick a frequency from the list (Settings -> Frequencies), or type one in MHz (14.071) or a band (20m) and press Enter.");
        _tips.SetToolTip(_dial, "The dial frequency of the radio. Red: the radio does not answer.");
        _band.SelectionChangeCommitted += (_, _) => { if (_band.SelectedItem is FrequencyEntry e) Tune(e.MHz); };
        _band.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TuneTyped(_band.Text); } };
        _band.DropDown += (_, _) => FillBandList();

        // ── waterfall + TX drive ──
        var wfHost = _wfHost = new Panel { Dock = DockStyle.Bottom, Height = 262, Padding = new Padding(4, 2, 4, 2) };
        var split = new Splitter { Dock = DockStyle.Bottom, Height = 5, MinExtra = 200, MinSize = 100 };
        var driveHost = new Panel { Dock = DockStyle.Right, Width = 54 };
        var driveLabel = new Label { Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8) };
        _drive.Value = Math.Clamp(450 - _settings.GetInt("audio", "tx_atten", 0), 0, 450);
        void DriveText() => driveLabel.Text = _drive.Value == 0 ? "Pwr\nOff" : string.Create(CultureInfo.InvariantCulture, $"Pwr\n-{(450 - _drive.Value) / 10.0:0.0} dB");
        _drive.ValueChanged += (_, _) => { DriveText(); _settings.Set("audio", "tx_atten", 450 - _drive.Value); _tx.Volume = DriveGain; };
        DriveText();
        _tips.SetToolTip(_drive, "TX drive, as in WSJT-X: attenuation of the transmit audio in dB. Set it so the radio's ALC stays (almost) still. Turning it while sending works.");
        driveHost.Controls.Add(_drive);
        driveHost.Controls.Add(driveLabel);
        wfHost.Controls.Add(_waterfall);
        wfHost.Controls.Add(driveHost);
        _wf = new BleepsWaterfall(_waterfall, _settings, "waterfall", "", _tips);
        wfHost.Controls.Add(_wf.Bar);
        _waterfall.ShowCursor = false;
        _waterfall.CursorCommitted += hz => _panel.SetTxHz(hz - _panel.Speed.CenterOffset);   // a click: there we send
        _tips.SetToolTip(_waterfall, "Click where you want to send (the middle of your signal). The stations being read stand in their colour.");

        var status = new StatusStrip();
        status.Items.Add(_stRig); status.Items.Add(_stAudio); status.Items.Add(_stTx);

        Controls.Add(_panel);
        Controls.Add(split);
        Controls.Add(wfHost);
        Controls.Add(_bandButtons);
        Controls.Add(bar);
        Controls.Add(menu);
        Controls.Add(status);
        _panel.BringToFront();

        _ui.Tick += (_, _) => Tick();
        ResumeLayout(false);
        PerformLayout();   // the window scales everything above from 96 dpi to the screen HERE
        // the band buttons only now (Bleeps v1.0.25): they are sized in device pixels (they are built again after Settings), and
        // built before the scaling above they were scaled a second time - 232 x 104 px at 200 % at start, 116 x 52 after Settings
        BuildBandButtons();
        ShowRig(_rigState);
        SizeStatus();
        UITheme.Apply(this);
        RestoreWindow();
    }

    /// <summary>The fixed-width status fields, measured in the status bar's font (Bleeps v1.0.25): the window's scaling does not
    /// reach a ToolStrip item, so "Audio in -40 dBFS" was cut off at 130 px at 200 %.</summary>
    private void SizeStatus()
    {
        var font = _stAudio.Font;
        int pad = LogicalToDeviceUnits(12);
        _stAudio.Width = new[] { "Audio in -99 dBFS", "Audio input failed", "No audio input" }.Max(t => TextRenderer.MeasureText(t, font).Width) + pad;
        _stTx.Width = Math.Max(TextRenderer.MeasureText("TX", font).Width + pad, LogicalToDeviceUnits(60));
    }

    /// <summary>The chat keeps room (Bleeps v1.0.25): in a small window - or at 200 % when the band buttons wrap to a second row -
    /// the waterfall's fixed height took all of it and the chat was gone. The waterfall gives way down to a minimum.</summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_wfHost == null || _panel == null || WindowState == FormWindowState.Minimized) return;
        int need = LogicalToDeviceUnits(150) - _panel.Height;   // the TX lines + a few chat lines
        if (need <= 0) return;
        int h = Math.Max(LogicalToDeviceUnits(120), _wfHost.Height - need);
        if (h < _wfHost.Height) _wfHost.Height = h;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        SizeStatus();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _wf.SetActive(true);
        StartAudio();
        StartRig();
        StartRx();
        UpdateMarkers();   // the TX marker from the start - before, it came only with a TX change or the first decode
        _ui.Start();
        if (MyCall.Length == 0)
        {
            _panel.SetStatus("Set your callsign under File -> Settings -> Station");
            BeginInvoke(OpenSettings);
        }
        else if (_settings.GetBool("update", "check_at_start", true))
            CheckForUpdates(quiet: true);   // only a newer release says something; no internet stays quiet
    }

    private bool _updateBusy;

    /// <summary>
    /// Help -> Check for updates (quiet = false) and the check at start (quiet = true, v1.0.22). Quiet: only a newer release that
    /// the operator did not skip gets a message - no internet, an error or "you have the newest" stay in the log.
    /// </summary>
    private async void CheckForUpdates(bool quiet)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        try
        {
            if (!quiet) _panel.SetStatus("Looking for a newer version…");
            var (found, error) = await UpdateCheck.FetchAsync();
            if (IsDisposed) return;
            if (found == null)
            {
                Log.Info("Update check: " + error);
                if (!quiet)
                {
                    _panel.SetStatus("");
                    Dialogs.Show(this, "Could not reach " + UpdateCheck.Host.Replace("https://", "") + "." + Environment.NewLine + Environment.NewLine + error,
                                    "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            if (!UpdateCheck.IsNewer(Version, found.Version))
            {
                Log.Info("Update check: " + Version + " is current, the site has " + found.Label);
                if (!quiet)
                {
                    _panel.SetStatus("");
                    Dialogs.Show(this, UpdateCheck.UpToDateText(Version), "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            // "Do not remind me": at start a skipped release stays quiet; asked under Help the box comes anyway, with the tick set
            bool skipped = UpdateCheck.IsSkipped(_settings.Get("update", "skip_version", ""), found.Version);
            if (quiet && skipped) { Log.Info("Update check: " + found.Label + " is available, not reminded (skipped)"); return; }
            Log.Info("Update check: " + found.Label + " is available, running " + Version);
            _panel.SetStatus(found.Label + " is available - Help -> Check for updates");

            var yes = new TaskDialogButton("Download page");
            var page = new TaskDialogPage
            {
                Caption = "Bleeps", Heading = "A newer version of Bleeps is available",
                Text = UpdateCheck.Text(found, Version), Icon = TaskDialogIcon.Information,
                Buttons = { yes, TaskDialogButton.Close }, DefaultButton = yes,
                Verification = new TaskDialogVerificationCheckBox("Do not remind me about " + found.Label, skipped),
                AllowCancel = true,
            };
            var answer = TaskDialog.ShowDialog(this, page, TaskDialogStartupLocation.CenterOwner);   // over Bleeps, not the screen
            _settings.Set("update", "skip_version", page.Verification.Checked ? found.Version : "");
            _settings.Save();
            if (answer == yes) OpenUrl(UpdateCheck.PageUrl);
        }
        catch (Exception ex) { Log.Exception("Update check", ex); }
        finally { _updateBusy = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _ui.Stop();
        StopSending();
        SaveWindow();
        _settings.Save();
        StopRx();
        _capture?.Dispose(); _capture = null;
        _rig?.Dispose(); _rig = null;
        base.OnFormClosing(e);
    }

    // ── settings ──

    private void OpenSettings()
    {
        if (_tx.IsRunning) { Dialogs.Show(this, "Stop sending first.", "Settings"); return; }
        using var f = new SettingsForm(_settings, _freqs);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _freqs = f.Frequencies;
        FrequencyList.Save(_freqs);
        BuildBandButtons();
        StartAudio();
        StartRig();
        if (_rx != null) _rx.OwnCall = BleepsCode.CleanCall(MyCall, out _);
    }

    private void RestoreWindow()
    {
        var r = _settings.Get("window", "bounds", "").Split(',').Select(v => int.TryParse(v, out var i) ? i : int.MinValue).ToArray();
        if (r.Length == 4 && r.All(v => v != int.MinValue))
        {
            // the size is in pixels of the DPI it was saved at (Bleeps v1.0.25): after Windows' scaling went from 100 to 200 %
            // the window came back half as big. Older settings have no DPI and are taken as they are.
            int savedDpi = _settings.GetInt("window", "dpi", 0);
            if (savedDpi >= 48 && savedDpi != DeviceDpi)
            {
                r[2] = (int)Math.Round(r[2] * (double)DeviceDpi / savedDpi);
                r[3] = (int)Math.Round(r[3] * (double)DeviceDpi / savedDpi);
            }
            var b = new Rectangle(r[0], r[1], Math.Max(r[2], MinimumSize.Width), Math.Max(r[3], MinimumSize.Height));
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(b))) { StartPosition = FormStartPosition.Manual; Bounds = b; }
        }
        if (_settings.GetBool("window", "maximized", false)) WindowState = FormWindowState.Maximized;
    }

    private void SaveWindow()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.Set("window", "bounds", $"{b.X},{b.Y},{b.Width},{b.Height}");
        _settings.Set("window", "dpi", DeviceDpi);
        _settings.Set("window", "maximized", WindowState == FormWindowState.Maximized);
    }

    // ── the radio ──

    private void StartRig()
    {
        _rig?.Dispose();
        _rig = new RigControl(RigConfig.Load(_settings));
        _rig.StateChanged += s => { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => ShowRig(s)); };
        _rig.Start();
        ShowRig(_rig.State);
    }

    private void ShowRig(RigState s)
    {
        _rigState = s;
        var cfg = RigConfig.Load(_settings);
        bool rig = cfg.Model != RigConfig.NoRig;
        bool known = s.Connected && s.Hz > 0;
        _dial.Text = known ? Bands.FormatDial(s.Hz) : rig ? "-- . --- ---" : "no CAT";
        _dial.BackColor = !rig || s.Connected ? Color.Black : Color.FromArgb(150, 0, 0);
        _mode.Text = s.Connected ? (s.Sat ? "TX " : "") + s.Mode : "";
        _sat.Visible = s.Connected && s.Sat;
        if (_sat.Visible) _sat.Text = "SAT\nRX " + (s.RxHz > 0 ? Bands.FormatDial(s.RxHz) : "...");
        _stRig.Text = rig ? $"{_rig?.Describe()}: {s.Status}" : s.Status;
        if (known && !_band.DroppedDown && !_band.Focused)
        {
            _band.Text = Bands.Of(s.Hz);
        }
        HighlightBand(known ? Bands.Of(s.Hz) : "");
    }

    private void Tune(double mhz)
    {
        if (_tx.IsRunning) { _panel.SetStatus("Not while sending"); return; }
        if (_rig == null || RigConfig.Load(_settings).Model == RigConfig.NoRig)
        {
            _panel.SetStatus($"No radio (CAT) set: tune to {Bands.FormatMHz(mhz)} MHz by hand");
            return;
        }
        if (_rigState.Sat)
        {
            _panel.SetStatus($"Satellite mode: Bleeps does not tune - set the uplink (SUB) to {Bands.FormatMHz(mhz)} MHz on the radio");
            return;
        }
        _rig.SetFrequency(mhz * 1e6);
        _panel.SetStatus($"Tuning to {Bands.FormatMHz(mhz)} MHz ({Bands.Of(mhz * 1e6)})");
    }

    private void TuneTyped(string text)
    {
        text = text.Trim();
        var byBand = _freqs.FirstOrDefault(f => f.Band.Equals(text, StringComparison.OrdinalIgnoreCase)
                                             || f.Band.Equals(text + "m", StringComparison.OrdinalIgnoreCase));
        if (byBand != null) { Tune(byBand.MHz); return; }
        if (Bands.TryParseMHz(text, out var mhz)) Tune(mhz);
        else _panel.SetStatus("Type a frequency in MHz (14.071) or a band (20m)");
    }

    private void FillBandList()
    {
        _band.Items.Clear();
        foreach (var f in _freqs) _band.Items.Add(f);
        _band.DropDownWidth = Math.Max(_band.Width, LogicalToDeviceUnits(260));
    }

    /// <summary>One button per band of the frequency list; a band with more frequencies gets a little menu.</summary>
    private void BuildBandButtons()
    {
        _bandButtons.SuspendLayout();
        foreach (Control c in _bandButtons.Controls) c.Dispose();
        _bandButtons.Controls.Clear();
        foreach (var g in _freqs.GroupBy(f => f.Band).OrderBy(g => Bands.Order(g.Key)))
        {
            var list = g.OrderBy(f => f.MHz).ToList();
            var b = new Button { Text = g.Key, Tag = g.Key, AutoSize = false, Size = new Size(LogicalToDeviceUnits(58), LogicalToDeviceUnits(26)),
                                 FlatStyle = FlatStyle.Flat, Margin = new Padding(LogicalToDeviceUnits(2)), UseVisualStyleBackColor = false, BackColor = SystemColors.Control };
            b.FlatAppearance.BorderColor = Color.Silver;
            b.Click += (_, _) =>
            {
                if (list.Count == 1) { Tune(list[0].MHz); return; }
                var m = new ContextMenuStrip();
                foreach (var f in list) m.Items.Add($"{Bands.FormatMHz(f.MHz)}  {f.Description}", null, (_, _) => Tune(f.MHz));
                m.Show(b, new Point(0, b.Height));
            };
            _tips.SetToolTip(b, string.Join("\n", list.Select(f => $"{Bands.FormatMHz(f.MHz)} MHz  {f.Description}")));
            _bandButtons.Controls.Add(b);
        }
        _bandButtons.ResumeLayout();
        HighlightBand(_rigState.Connected ? Bands.Of(_rigState.Hz) : "");
    }

    private void HighlightBand(string band)
    {
        foreach (Control c in _bandButtons.Controls)
        {
            bool on = (c.Tag as string) == band;
            c.BackColor = on ? Color.FromArgb(255, 200, 60) : SystemColors.Control;
            c.ForeColor = on ? Color.Black : SystemColors.ControlText;   // dark text on the orange, also in dark mode
            if (c.Font.Bold != on) c.Font = new Font(Font, on ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    // ── audio in: waterfall + receiver ──

    private void StartAudio()
    {
        _capture?.Dispose();
        _capture = null;
        _wf.Reset();
        try
        {
            var ins = WaveIn.Devices();
            if (ins.Count == 0) { _stAudio.Text = "No audio input"; return; }
            int dev = WaveIn.Resolve(ins, _settings.Get("audio", "input_name", ""), _settings.GetInt("audio", "input", 0));
            Log.Info($"Audio input: {dev} '{ins[dev]}'");
            // settings from before v1.0.24 have only numbers: remember the names of the cards they point at now, so from
            // here on a card is found by name
            if (_settings.Get("audio", "input_name", "").Length == 0) _settings.Set("audio", "input_name", ins[dev]);
            var outsNow = WaveOut.Devices();
            if (outsNow.Count > 0 && _settings.Get("audio", "output_name", "").Length == 0)
                _settings.Set("audio", "output_name", outsNow[WaveIn.Resolve(outsNow, "", _settings.GetInt("audio", "output", 0))]);
            _capture = new WaveIn(dev);
            _capture.SamplesAvailable += OnSamples;
            _capture.Failed += m => BeginInvoke(() => _stAudio.Text = "Audio: " + m);
            _capture.Start();
        }
        catch (Exception ex)
        {
            Log.Exception("Audio input", ex);
            _capture?.Dispose(); _capture = null;
            _stAudio.Text = "Audio input failed";
            _panel.SetStatus(ex.Message);
        }
    }

    /// <summary>On the capture thread. While we send (half duplex) the receiver and the waterfall get nothing.</summary>
    private void OnSamples(float[] s)
    {
        float peak = 0;
        foreach (var v in s) { float a = Math.Abs(v); if (a > peak) peak = a; }
        _inPeak = Math.Max(_inPeak, peak);
        if (_tx.IsRunning && !Duplex) return;
        _wf.Add(s);
        var q = _rxIn;
        if (q == null || q.IsAddingCompleted) return;
        try { if (!q.TryAdd(s)) Log.Warn("Bleeps: receiver behind, audio dropped"); } catch (InvalidOperationException) { }
    }

    private void Tick()
    {
        // audio level and the TX state
        float p = _inPeak; _inPeak = 0;
        double dbfs = p > 0 ? 20 * Math.Log10(p) : -99;
        _stAudio.Text = _capture == null ? _stAudio.Text : $"Audio in {dbfs:0} dBFS";
        _stAudio.ForeColor = dbfs > -3 ? Color.Red : dbfs < -60 ? Color.DarkOrange : SystemColors.ControlText;
        _stTx.Text = _tx.IsRunning ? "TX" : "RX";
        _stTx.BackColor = _tx.IsRunning ? Color.Red : SystemColors.Control;
        _stTx.ForeColor = _tx.IsRunning ? Color.White : SystemColors.ControlText;
        PingTimeouts();
        // save what changed every 30 s (v1.0.24): before, everything set during a session - TX place, waterfall, drive,
        // speed - waited for a clean exit and was gone after a crash or a hard stop
        if (_settings.Dirty && (DateTime.UtcNow - _lastSave).TotalSeconds >= 30) { _lastSave = DateTime.UtcNow; _settings.Save(); }
        if (_tx.IsRunning) _panel.SetProgress(_tx.PlayedSeconds, _tx.TotalSeconds);
        if (_tx.IsRunning && !Duplex) return;       // the waterfall holds still while we send

        _wf.Step();
    }

    // ── receiving (CwWindow.Bleeps.cs) ──

    private void StartRx()
    {
        if (_rxThread != null) return;
        _rx = new BleepsReceiver { Threads = _settings.GetInt("bleeps", "threads", 0), OwnCall = BleepsCode.CleanCall(MyCall, out _) };
        _seen.Clear(); _echo.Clear();
        var q = _rxIn = new BlockingCollection<float[]>(boundedCapacity: 2000);
        var rx = _rx;
        _rxThread = new Thread(() => RxLoop(q, rx)) { IsBackground = true, Name = "Bleeps RX", Priority = ThreadPriority.BelowNormal };
        _rxThread.Start();
    }

    private void StopRx()
    {
        var q = _rxIn; _rxIn = null;
        q?.CompleteAdding();
        _rxThread = null;
        _rx = null;
    }

    private void RxLoop(BlockingCollection<float[]> q, BleepsReceiver rx)
    {
        var last = DateTime.UtcNow;
        try
        {
            foreach (var chunk in q.GetConsumingEnumerable())
            {
                rx.Feed(chunk);
                if ((DateTime.UtcNow - last).TotalMilliseconds >= 250 || q.Count == 0)
                {
                    last = DateTime.UtcNow;
                    Publish(rx);
                }
            }
        }
        catch (Exception ex) { Log.Exception("Bleeps receiver", ex); }
    }

    private long? OwnEcho(BleepsReceiver.Message m, DateTime firstSeen)
    {
        if (_echo.TryGetValue(m, out var known)) return known;
        var own = _rx?.OwnCall;
        if (m.Call == null || own == null || !string.Equals(m.Call, own, StringComparison.OrdinalIgnoreCase)) return null;
        lock (_ownLock)
            for (int i = _ownSent.Count - 1; i >= 0; i--)
            {
                var o = _ownSent[i];
                if (Math.Abs(o.Hz - m.CenterHz) <= 60 && firstSeen >= o.Utc.AddSeconds(-3) && firstSeen <= o.Utc.AddSeconds(o.Seconds + 15))
                {
                    _echo[m] = o.Id;
                    return o.Id;
                }
            }
        return null;
    }

    private void Publish(BleepsReceiver rx)
    {
        var changed = new List<ChatView.Entry>();
        var echoes = new List<(long Id, bool Finished, bool Complete, double Db)>();
        var active = new List<(double Hz, string? Call)>();
        var pings = new List<(string From, Ping.Parsed P, double SnrDb)>();
        foreach (var m in rx.Messages)
        {
            if (!m.Shown) continue;
            if (!_seen.TryGetValue(m, out var st)) st = (_nextId++, DateTime.UtcNow, "", false);
            string sig = $"{m.Call}|{m.Text.Length}|{m.Complete}|{m.Finished}|{m.Text.GetHashCode()}";
            if (OwnEcho(m, st.Utc) is { } ownId)
            {
                if (sig != st.Sig) echoes.Add((ownId, m.Finished, m.Complete, m.SnrDb));
                _seen[m] = (st.Id, st.Utc, sig, true);
                continue;
            }
            var e = new ChatView.Entry
            {
                Id = st.Id, Utc = st.Utc, Call = m.Call, Text = m.Text, Complete = m.Complete, Finished = m.Finished,
                Late = m.Late, Hz = m.CenterHz, SnrDb = m.SnrDb, Speed = m.Speed.Name,
                Length = TimeSpan.FromSeconds(Math.Max(0, m.Decoder.LastHeard - m.StartSample) / (double)BleepsCode.SampleRate),
            };
            // ping/pong stay out of the chat window (v1.0.26) - the status line and the message box say it, the log keeps it
            if (sig != st.Sig && !Ping.HideInChat(m.Text, m.Finished)) changed.Add(e);
            if (m.Finished && !st.Logged)
            {
                ChatLog(e, mine: false); st.Logged = true;
                if (m.Complete && m.Call != null && Ping.Parse(m.Text) is { } pp) pings.Add((m.Call, pp, m.SnrDb));
            }
            if (!m.Finished) active.Add((e.Hz, m.Call));
            _seen[m] = (st.Id, st.Utc, sig, st.Logged);
        }
        rx.Forget(120);
        foreach (var k in _seen.Keys.Where(k => !rx.Messages.Contains(k)).ToList()) _seen.Remove(k);
        foreach (var k in _echo.Keys.Where(k => !rx.Messages.Contains(k)).ToList()) _echo.Remove(k);
        if (pings.Count > 0 && !IsDisposed && IsHandleCreated) BeginInvoke(() => { foreach (var (from, p, db) in pings) OnPing(from, p, db); });
        if (changed.Count == 0 && echoes.Count == 0 && active.Count == _active) return;
        _active = active.Count;
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (changed.Count > 0) _panel.Show(changed);
            foreach (var (id, fin, ok, db) in echoes) _panel.HeardBack(id, fin, ok, db);
            _stations = active;
            UpdateMarkers();
        });
    }

    private void UpdateMarkers()
    {
        var list = _stations.Select(s => (s.Hz, _panel.ColourOf(s.Call), s.Call ?? "?")).ToList();
        var sp = _panel.Speed;
        double txMid = _panel.TxHz + sp.CenterOffset;
        list.Add((txMid, Color.OrangeRed, sp == BleepsSpeed.Fast ? "TX Fast" : "TX"));
        _marks = list;
        _bands = new[] { (txMid - sp.WidthHz / 2, txMid + sp.WidthHz / 2, Color.OrangeRed) };
        _waterfall.Bands = _bands;
        _waterfall.Markers = _marks;
    }

    // ── ping (v1.0.23): "PING CALL ID" out, "PONG MYCALL ID SNR" back - see Ping.cs ──

    private sealed record PendingPing(string Call, DateTime Utc);
    private readonly Dictionary<string, PendingPing> _pings = new();                    // by ID
    private readonly Dictionary<string, DateTime> _answered = new(StringComparer.OrdinalIgnoreCase);   // "CALL ID" we answered
    private const int PingTimeoutS = 120;
    private DateTime _lastSave = DateTime.UtcNow;

    /// <summary>Ping a call; null = ask which one (the Ping button - the call of the TX line's "CALL:" or the last one).</summary>
    private void SendPing(string? call)
    {
        if (call == null)
        {
            string suggest = _settings.Get("ping", "last_call", "");
            call = AskCall(suggest);
            if (call == null) return;
        }
        var clean = BleepsCode.CleanCall(call, out var why);
        if (clean == null) { _panel.SetStatus("Ping: the call must be " + why); return; }
        if (clean.Equals(BleepsCode.CleanCall(MyCall, out _), StringComparison.OrdinalIgnoreCase)) { _panel.SetStatus("Ping: that is your own call"); return; }
        _settings.Set("ping", "last_call", clean);
        string id;
        do id = Ping.NewId(); while (_pings.ContainsKey(id));
        _pings[id] = new PendingPing(clean, DateTime.UtcNow);
        Send(Ping.PingText(clean, id));
        _panel.SetStatus($"Ping {clean} ({id}) queued - waiting for the answer");
        Log.Info($"Ping {clean} id {id}");
    }

    private string? AskCall(string suggest)
    {
        using var f = new Form { Text = "Ping", FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                                 ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, AutoSize = true,
                                 AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) };
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        var box = new TextBox { Text = suggest, Width = 140, CharacterCasing = CharacterCasing.Upper };
        var ok = new Button { Text = "Ping", DialogResult = DialogResult.OK, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(75, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(75, 0) };
        t.Controls.Add(new Label { Text = "Callsign:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        t.Controls.Add(box, 1, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        t.Controls.Add(buttons, 0, 1); t.SetColumnSpan(buttons, 2);
        f.Controls.Add(t);
        f.AcceptButton = ok; f.CancelButton = cancel;
        UITheme.ScaleFromDesign(f);
        UITheme.Apply(f);
        return f.ShowDialog(this) == DialogResult.OK && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }

    // ── a ping to us: a countdown before the automatic answer (v1.0.27, PA5DJ: "when a user gets a ping, a popup with a timer
    // until the automatic answer; within that time the user can cancel") ──

    private readonly Queue<(string From, string Id, double SnrDb, int Delay)> _incoming = new();
    private bool _promptOpen;
    /// <summary>What sends a text - <see cref="Send"/>; a test puts its own here so nothing goes on the air.</summary>
    private Action<string>? _sendOverride;

    private void AnswerPing(string from, string id, double snrDb)
    {
        var text = Ping.PongText(from, id, snrDb);
        if (_sendOverride != null) _sendOverride(text); else Send(text);
        Log.Info($"Ping from {from} id {id} at {snrDb:0} dB - answering");
    }

    /// <summary>One countdown window at a time; pings that come in meanwhile get theirs after it (UI thread).</summary>
    private void PromptPings()
    {
        try
        {
            while (_incoming.Count > 0 && !IsDisposed)
            {
                var (from, id, db, delay) = _incoming.Dequeue();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                using var dlg = new PingAnswerDialog(from, id, Ping.FormatDb(db), delay);
                var answer = dlg.ShowDialog(this);
                if (answer == DialogResult.Yes)
                {
                    AnswerPing(from, id, db);
                    _panel.SetStatus($"Ping from {from} ({id}) at {Ping.FormatDb(db)} - answered{(dlg.ByTimer ? "" : " (Answer now)")}");
                }
                else
                {
                    _panel.SetStatus($"Ping from {from} ({id}) - not answered (cancelled)");
                    Log.Info($"Ping from {from} id {id} - not answered: cancelled by the operator");
                }
            }
        }
        finally { _promptOpen = false; }
    }

    /// <summary>"Ping from DL1ABC at -8 dB - answering automatically in 10 s": Answer now / Don't answer. Yes = answer.</summary>
    private sealed class PingAnswerDialog : Form
    {
        private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
        private readonly Label _count;
        private int _left;
        public bool ByTimer { get; private set; }

        public PingAnswerDialog(string from, string id, string db, int seconds)
        {
            _left = seconds;
            Text = "Bleeps - Ping"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(14); Font = new Font("Segoe UI", 9);
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            t.Controls.Add(new Label { Text = $"Ping from {from} ({id}), heard at {db}.", AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) });
            _count = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            t.Controls.Add(_count);
            var now = new Button { Text = "Answer now", DialogResult = DialogResult.Yes, AutoSize = true, MinimumSize = new Size(100, 0) };
            var no = new Button { Text = "Don't answer", DialogResult = DialogResult.No, AutoSize = true, MinimumSize = new Size(100, 0) };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0) };
            buttons.Controls.Add(no); buttons.Controls.Add(now);
            t.Controls.Add(buttons);
            Controls.Add(t);
            AcceptButton = now; CancelButton = no;                    // Esc or the X = don't answer
            Update1();
            _tick.Tick += (_, _) =>
            {
                if (--_left <= 0) { _tick.Stop(); ByTimer = true; DialogResult = DialogResult.Yes; return; }
                Update1();
            };
            Shown += (_, _) => _tick.Start();
            FormClosed += (_, _) => _tick.Dispose();
            UITheme.Apply(this);
            UITheme.ScaleFromDesign(this);
        }

        private void Update1() => _count.Text = $"Bleeps answers automatically in {_left} s - or click Don't answer.";
    }

    /// <summary>A complete ping or pong came in (UI thread).</summary>
    private void OnPing(string from, Ping.Parsed p, double snrDb)
    {
        var me = BleepsCode.CleanCall(MyCall, out _);
        if (me == null || !p.Call.Equals(me, StringComparison.OrdinalIgnoreCase)) return;   // not for us
        if (p.Kind == Ping.Kind.Ping)
        {
            foreach (var k in _answered.Where(kv => kv.Value < DateTime.UtcNow.AddMinutes(-10)).Select(kv => kv.Key).ToList()) _answered.Remove(k);
            var answered = new HashSet<string>(_answered.Keys, StringComparer.OrdinalIgnoreCase);
            switch (Ping.Decide(me, from, p, _settings.GetBool("ping", "auto_reply", true), PingBlacklist(_settings), answered))
            {
                case Ping.Reply.Answer:
                    _answered[from.ToUpperInvariant() + " " + p.Id] = DateTime.UtcNow;   // one prompt per ping, also if heard twice
                    int delay = Math.Clamp(_settings.GetInt("ping", "answer_delay", 10), 0, 120);
                    if (delay == 0)
                    {
                        AnswerPing(from, p.Id, snrDb);
                        PingNote($"Ping from {from} ({p.Id}) at {Ping.FormatDb(snrDb)} - answering");
                    }
                    else
                    {
                        _incoming.Enqueue((from, p.Id, snrDb, delay));
                        _panel.SetStatus($"Ping from {from} ({p.Id}) at {Ping.FormatDb(snrDb)} - answering in {delay} s unless you cancel");
                        if (!_promptOpen) { _promptOpen = true; BeginInvoke(PromptPings); }
                    }
                    break;
                case Ping.Reply.Blacklisted:                           // PA5DJ: "calls that may not ping you" - no answer, ever
                    PingNote($"Ping from {from} ignored (blacklist, Settings -> Ping)");
                    Log.Info($"Ping from {from} id {p.Id} ignored: on the blacklist");
                    break;
                case Ping.Reply.AnsweringOff:
                    PingNote($"Ping from {from} ({p.Id}) at {Ping.FormatDb(snrDb)} - answering pings is off (Settings -> Ping)");
                    break;
            }
            return;
        }
        // a pong: is it the answer to one of ours?
        if (!_pings.TryGetValue(p.Id, out var pend) || !pend.Call.Equals(from, StringComparison.OrdinalIgnoreCase)) return;
        _pings.Remove(p.Id);
        double s = (DateTime.UtcNow - pend.Utc).TotalSeconds;
        string them = p.SnrDb is int d ? Ping.FormatDb(d) : "?";
        PingNote($"Ping {from}: answered after {s:0} s - {from} heard you at {them}, you hear {from} at {Ping.FormatDb(snrDb)}");
        Log.Info($"Pong from {from} id {p.Id}: they heard us at {them}, we hear them at {snrDb:0} dB, {s:0} s");
    }

    /// <summary>The calls whose pings are never answered: <c>[ping] blacklist</c>, separated by spaces, commas or semicolons.</summary>
    internal static HashSet<string> PingBlacklist(AppSettings s) =>
        new(s.Get("ping", "blacklist", "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(c => c.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);

    private bool _noteOpen;
    private string? _notePending;

    /// <summary>
    /// A ping event: in the status line AND in a message box that stays until OK is clicked (v1.0.26, PA5DJ: "a window where
    /// you have to click OK to go on"). Shown AFTER the caller is done (BeginInvoke), so an automatic answer is queued first;
    /// while the box is open the window keeps receiving and answering (a message box runs the message loop). One box at a
    /// time: what comes in meanwhile is shown when OK is clicked (the newest).
    /// </summary>
    private void PingNote(string text)
    {
        _panel.SetStatus(text);
        if (IsDisposed || !IsHandleCreated) return;
        if (_noteOpen) { _notePending = text; return; }
        _noteOpen = true;
        BeginInvoke(() => ShowNote(text));
    }

    private void ShowNote(string text)
    {
        try
        {
            while (true)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Dialogs.Show(this, text, "Bleeps - Ping", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (_notePending == null || IsDisposed) break;
                text = _notePending; _notePending = null;
            }
        }
        finally { _noteOpen = false; }
    }

    private void PingTimeouts()
    {
        if (_pings.Count == 0) return;
        foreach (var (id, p) in _pings.Where(kv => (DateTime.UtcNow - kv.Value.Utc).TotalSeconds > PingTimeoutS).ToList())
        {
            _pings.Remove(id);
            PingNote($"Ping {p.Call} ({id}): no answer within {PingTimeoutS / 60} minutes");
        }
    }

    // ── sending ──

    private double DriveGain => _drive.Value == 0 ? 0 : Math.Pow(10, -((450 - _drive.Value) / 10.0) / 20);

    private void Send(string text)
    {
        _out.Enqueue(text);
        _stopped = false;
        if (!_tx.IsRunning) Next();
        _panel.SetQueued(_out.ToArray());
    }

    private void RemoveQueued(int index)
    {
        var rest = _out.ToList();
        if (index < 0 || index >= rest.Count) return;
        rest.RemoveAt(index);
        _out.Clear();
        foreach (var s in rest) _out.Enqueue(s);
        _panel.SetQueued(_out.ToArray());
    }

    private void StopSending()
    {
        _stopped = true;
        _out.Clear();
        _panel.SetQueued(Array.Empty<string>());
        if (_tx.IsRunning) _tx.Stop();
    }

    private void Next()
    {
        if (_out.Count == 0 || _tx.IsRunning) { _panel.SetQueued(_out.ToArray()); return; }
        var call = BleepsCode.CleanCall(MyCall, out var why);
        if (_rx != null) _rx.OwnCall = call;
        if (call == null)
        {
            _out.Clear(); _panel.SetQueued(Array.Empty<string>());
            _panel.SetStatus($"Cannot send: the callsign must be {why} (File -> Settings -> Station)");
            return;
        }
        var cfg = RigConfig.Load(_settings);
        if (cfg.UsePtt && (_rig == null || !_rig.IsOpen))
        {
            _out.Clear(); _panel.SetQueued(Array.Empty<string>());
            Dialogs.Show(this, $"PTT is set to {cfg.Ptt}, but the radio/port is not open: {_rigState.Status}\n\nCheck File -> Settings -> Radio.",
                            "Bleeps - PTT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var outs = WaveOut.Devices();
        if (outs.Count == 0) { _out.Clear(); _panel.SetQueued(Array.Empty<string>()); _panel.SetStatus("Cannot send: no audio output"); return; }
        bool oursThere;
        lock (_ownLock)
            oursThere = Duplex && _ownSent.Count > 0 && _ownSent[^1] is var last
                        && Math.Abs(last.Hz - (_panel.TxHz + _panel.Speed.CenterOffset)) < 1
                        && DateTime.UtcNow <= last.Utc.AddSeconds(last.Seconds + 60);
        if (_panel.AutoFreeSpot && _rx?.AutoSpot(_panel.TxHz, _panel.Speed, oursThere: oursThere) is { } spot && Math.Abs(spot - _panel.TxHz) >= 1)
        {
            _panel.SetTxHz(spot);
            Log.Info($"Auto free spot -> {spot:0} Hz");
        }
        string text = _out.Dequeue();
        _panel.SetQueued(_out.ToArray());
        short[] samples;
        var sp = _panel.Speed;
        try
        {
            var tones = BleepsCode.Encode(call, text, sp);
            var audio = BleepsModem.Modulate(tones, _panel.TxHz, 0.9, sp);
            samples = new short[audio.Length];
            for (int i = 0; i < audio.Length; i++) samples[i] = (short)Math.Clamp(audio[i] * 32767f, -32767f, 32767f);
        }
        catch (ArgumentException ex) { _panel.SetStatus("Cannot send: " + ex.Message); return; }
        int dev = WaveIn.Resolve(outs, _settings.Get("audio", "output_name", ""), _settings.GetInt("audio", "output", 0));
        double seconds = samples.Length / (double)BleepsCode.SampleRate;
        _tx.Volume = DriveGain;
        _panel.SetSending(true);
        var rig = _rig;
        Action<bool>? ptt = cfg.UsePtt && rig != null ? on => { if (!rig.SetPtt(on) && on) throw new InvalidOperationException("PTT did not key (" + cfg.Ptt + ")"); } : null;
        _tx.Start(samples, dev, ptt, cfg.TxDelayMs, cfg.TxTailMs);
        double txc = _panel.TxHz + sp.CenterOffset;
        // our own ping/pong is not put in the chat window either (v1.0.26); HeardBack finds no line for id 0 and does nothing
        long ownId = Ping.Parse(text) != null ? 0 : _panel.AddOwn(call, text, txc, sp.Name);
        lock (_ownLock)
        {
            _ownSent.Add(new OwnSent(ownId, text, txc, DateTime.UtcNow, seconds));
            if (_ownSent.Count > 20) _ownSent.RemoveAt(0);
        }
        ChatLog(new ChatView.Entry { Id = 0, Utc = DateTime.UtcNow, Call = call, Text = text, Complete = true, Finished = true, Mine = true,
                                     Hz = txc, SnrDb = double.NaN, Speed = sp.Name }, mine: true);
        _panel.SetStatus($"Sending {seconds:0} s on {_panel.TxHz:0} Hz ({sp.Name})");
        Log.Info($"Sending {text.Length} chars, {seconds:0.0} s, {_panel.TxHz:0} Hz, card {dev}, PTT {cfg.Ptt}");
    }

    // ── the chat log: Documents\Bleeps\chat\YYYY-MM-DD.txt (UTC) ──

    private static string ChatFolder => Path.Combine(Log.DataFolder, "chat");
    private static readonly object _chatLock = new();

    private static void ChatLog(ChatView.Entry e, bool mine)
    {
        try
        {
            Directory.CreateDirectory(ChatFolder);
            var path = Path.Combine(ChatFolder, e.Utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt");
            string mark = (mine ? "sent" : e.Late ? "late" : e.Complete ? "ok" : "partial") + (e.Speed == "Fast" ? " F" : "");
            string db = double.IsNaN(e.SnrDb) ? "" : $"{Math.Clamp(e.SnrDb, -30, 30):+0;-0} dB";
            var line = string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss}  {1,-10}  {2,4:0} Hz  {3,7}  {4,-7}  {5}",
                                     e.Utc, e.Call ?? "?", e.Hz, db, mark, e.Text.Replace("\r", " ").Replace("\n", " "));
            lock (_chatLock) File.AppendAllText(path, line + Environment.NewLine, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) { Log.Warn("Chat log: " + ex.Message); }
    }

    // ── small things ──

    private void OpenFolder(string dir)
    {
        try { Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
        catch (Exception ex) { Dialogs.Show(this, ex.Message, "Bleeps"); }
    }

    private void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Show(this, ex.Message, "Bleeps"); }
    }

    private void About()
    {
        string hamlib = HamlibNative.IsAvailable ? "Hamlib: " + HamlibNative.VersionString() : "Hamlib: not found";
        using var f = new Form
        {
            Text = "About Bleeps", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), Icon = Icon
        };
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        void Line(string s, bool bold = false) => t.Controls.Add(new Label
        {
            Text = s, AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 6),
            Font = bold ? new Font(Font, FontStyle.Bold) : Font
        });
        void Link(string url)
        {
            var l = new LinkLabel { Text = url.Replace("https://", ""), AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            l.LinkClicked += (_, _) => OpenUrl(url);
            t.Controls.Add(l);
        }
        Line($"Bleeps {Version}", bold: true);
        Line("Chat protocol for radio amateurs - by PA5DJ");
        Link(UpdateCheck.Host);   // the program's site: downloads, guide, protocol - and what the update check asks
        Link("https://www.ham4life.com");
        Link("https://www.pa5dj.nl");
        Line("\nThe same Bleeps as in ShackAssistant (protocol version 0).\n" + hamlib);
        Line(BleepsEmoji.Credit);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(75, 0) };
        t.Controls.Add(ok);
        f.AcceptButton = ok; f.CancelButton = ok;
        f.Controls.Add(t);
        UITheme.ScaleFromDesign(f);
        UITheme.Apply(f);
        f.ShowDialog(this);
    }
}
