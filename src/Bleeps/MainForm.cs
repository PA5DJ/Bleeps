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
    public const string Version = "v1.0.2";

    private readonly AppSettings _settings = new();
    private List<FrequencyEntry> _freqs = FrequencyList.Load();

    // radio bar
    private readonly ComboBox _band = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 110, Font = new Font("Segoe UI", 11) };
    private readonly Label _dial = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Consolas", 22, FontStyle.Bold),
                                           BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, ForeColor = Color.FromArgb(80, 255, 80), Size = new Size(250, 44) };
    private readonly Label _mode = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(8, 14, 0, 0) };
    private readonly FlowLayoutPanel _bandButtons = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4, 0, 4, 4) };

    // chat + waterfall
    private readonly BleepsPanel _panel;
    private readonly WaterfallPanel _waterfall = new() { Dock = DockStyle.Fill, MinHz = 100, MaxHz = 3000 };
    private readonly TrackBar _drive = new() { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 450, TickFrequency = 50, Dock = DockStyle.Right, Width = 45 };
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000 };

    // status
    private readonly ToolStripStatusLabel _stRig = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _stAudio = new() { AutoSize = false, Width = 130 };
    private readonly ToolStripStatusLabel _stTx = new() { AutoSize = false, Width = 60, Text = "RX" };

    // audio, radio
    private WaveIn? _capture;
    private SpectrumAnalyzer? _spectrum;
    private volatile float _inPeak;
    private RigControl? _rig;
    private RigState _rigState = new(false, 0, "", "No radio");
    private readonly System.Windows.Forms.Timer _ui = new() { Interval = 40 };
    private bool _floorSet;

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
        Text = "Bleeps " + Version;
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
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("Bleeps on the web", null, (_, _) => OpenUrl("https://shackassistant.com/guides/bleeps.html"));
        help.DropDownItems.Add("Open the &log folder", null, (_, _) => OpenFolder(Path.GetDirectoryName(Log.FilePath)!));
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
        _tips.SetToolTip(_band, "The band, as in WSJT-X: pick a frequency from the list (Settings -> Frequencies), or type one in MHz (14.071) or a band (20m) and press Enter.");
        _tips.SetToolTip(_dial, "The dial frequency of the radio. Red: the radio does not answer.");
        _band.SelectionChangeCommitted += (_, _) => { if (_band.SelectedItem is FrequencyEntry e) Tune(e.MHz); };
        _band.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TuneTyped(_band.Text); } };
        _band.DropDown += (_, _) => FillBandList();

        // ── waterfall + TX drive ──
        var wfHost = new Panel { Dock = DockStyle.Bottom, Height = 262, Padding = new Padding(4, 2, 4, 2) };
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
        wfHost.Controls.Add(BuildWaterfallBar());
        _waterfall.Resize += (_, _) => ApplyWfRange(clear: false);
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

        BuildBandButtons();
        ShowRig(_rigState);
        _ui.Tick += (_, _) => Tick();
        ResumeLayout(false);
        PerformLayout();
        UITheme.Apply(this);
        RestoreWindow();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyWfRange(clear: false);
        StartAudio();
        StartRig();
        StartRx();
        _ui.Start();
        if (MyCall.Length == 0)
        {
            _panel.SetStatus("Set your callsign under File -> Settings -> Station");
            BeginInvoke(OpenSettings);
        }
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
        if (_tx.IsRunning) { MessageBox.Show(this, "Stop sending first.", "Settings"); return; }
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
            var b = new Rectangle(r[0], r[1], Math.Max(r[2], MinimumSize.Width), Math.Max(r[3], MinimumSize.Height));
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(b))) { StartPosition = FormStartPosition.Manual; Bounds = b; }
        }
        if (_settings.GetBool("window", "maximized", false)) WindowState = FormWindowState.Maximized;
    }

    private void SaveWindow()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.Set("window", "bounds", $"{b.X},{b.Y},{b.Width},{b.Height}");
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
        _dial.Text = s.Connected ? Bands.FormatDial(s.Hz) : rig ? "-- . --- ---" : "no CAT";
        _dial.BackColor = !rig || s.Connected ? Color.Black : Color.FromArgb(150, 0, 0);
        _mode.Text = s.Connected ? s.Mode : "";
        _stRig.Text = rig ? $"{_rig?.Describe()}: {s.Status}" : s.Status;
        if (s.Connected && !_band.DroppedDown && !_band.Focused)
        {
            _band.Text = Bands.Of(s.Hz);
        }
        HighlightBand(s.Connected ? Bands.Of(s.Hz) : "");
    }

    private void Tune(double mhz)
    {
        if (_tx.IsRunning) { _panel.SetStatus("Not while sending"); return; }
        if (_rig == null || RigConfig.Load(_settings).Model == RigConfig.NoRig)
        {
            _panel.SetStatus($"No radio (CAT) set: tune to {Bands.FormatMHz(mhz)} MHz by hand");
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
                                 FlatStyle = FlatStyle.Flat, Margin = new Padding(2), UseVisualStyleBackColor = false, BackColor = SystemColors.Control };
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
            c.Font = new Font(Font, on ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    // ── audio in: waterfall + receiver ──

    private void StartAudio()
    {
        _capture?.Dispose();
        _capture = null;
        _spectrum = new SpectrumAnalyzer(WaveIn.SampleRate, WfFft, 1, historySeconds: 4);
        _wfDone = 0; _wfCount = 0; _floorSet = false;
        try
        {
            if (WaveIn.Devices().Count == 0) { _stAudio.Text = "No audio input"; return; }
            int dev = Math.Clamp(_settings.GetInt("audio", "input", 0), 0, WaveIn.Devices().Count - 1);
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
        _spectrum?.Add(s);
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
        if (_tx.IsRunning) _panel.SetProgress(_tx.PlayedSeconds, _tx.TotalSeconds);
        if (_tx.IsRunning && !Duplex) return;       // the waterfall holds still while we send

        WaterfallStep();
    }

    // ── the waterfall, the way WSJT-X's Wide Graph builds it ──
    // A LONG window: 65536 samples at 48 kHz = 1.37 s and 0.73 Hz a bin - the same as WSJT-X (16384 at 12 kHz). The four tones
    // of Bleeps (21 Hz apart) then stand as four thin lines instead of one blob (4096 + Blackman-Harris smeared ~45 Hz).
    // A new spectrum every 80 ms of audio (WSJT-X: every half symbol), WfAvg of them averaged in POWER into one row (WSJT-X
    // "N Avg", default 5), and per pixel the power of ALL bins in it is added up (WSJT-X "Bins/Pixel") - never one bin picked.
    internal const int WfFft = 65536, WfHop = 3840;
    private int WfAvg => (int)_wfAvg.Value;
    private long _wfDone;
    private int _wfCount;
    private double[] _wfAcc = Array.Empty<double>();
    private double _wfPxHz;

    /// <summary>From the timer: one spectrum per 80 ms of new audio; every WfAvg spectra a row.</summary>
    internal void WaterfallStep()
    {
        var sp = _spectrum;
        if (sp == null || !sp.HasFrame) return;
        long total = sp.Total;
        if (total - _wfDone < WfHop) return;
        _wfDone = total;
        int w = Math.Max(50, _waterfall.ClientSize.Width);
        double pxHz = (_waterfall.MaxHz - _waterfall.MinHz) / w;
        int n = (int)(_waterfall.MaxHz / pxHz) + 2;
        if (_wfAcc.Length != n || Math.Abs(_wfPxHz - pxHz) > 1e-9) { _wfAcc = new double[n]; _wfCount = 0; _wfPxHz = pxHz; }

        var db = sp.Frame();
        double df = sp.HzPerBin;
        for (int k = 0; k < n; k++)
        {
            int b0 = Math.Max(1, (int)Math.Ceiling((k - 0.5) * pxHz / df));
            int b1 = Math.Min(db.Length - 1, (int)Math.Floor((k + 0.5) * pxHz / df));
            double p = 0;
            for (int b = b0; b <= b1; b++) p += Math.Pow(10, db[b] / 10.0);
            _wfAcc[k] += p;
        }
        if (++_wfCount < WfAvg) return;

        var row = new float[n];
        for (int k = 0; k < n; k++) row[k] = (float)(10 * Math.Log10(Math.Max(_wfAcc[k] / _wfCount, 1e-20)));
        Array.Clear(_wfAcc);
        _wfCount = 0;
        int last = n - 1;
        NormaliseToLocalNoise(row, last, pxHz);
        int lo = Math.Max(0, (int)(_waterfall.MinHz / pxHz));
        var slice = row.AsSpan(lo, last - lo + 1).ToArray();
        Array.Sort(slice);
        float noise = slice[slice.Length / 2];
        // The colours climb over WfSpanDb from just above the noise, like WSJT-X (Gain/Zero give it ~30 dB). The panel's own
        // Skim curve starts 7 dB under FloorDb and runs CurveDb up; ShackAssistant's 11 dB made every Bleeps station white.
        float wantFloor = noise + 7f + FloorAbove;
        // the first row sets the floor outright: easing in from the panel's -95 dB painted the first seconds white
        if (!_floorSet) { _waterfall.FloorDb = wantFloor; _floorSet = true; }
        else _waterfall.FloorDb += (wantFloor - _waterfall.FloorDb) * 0.2f;
        _waterfall.CurveDb = SpanDb;
        _waterfall.AddFrame(row, pxHz);
    }

    /// <summary>Fields so a harness can set the colours directly; NaN = the Gain/Zero sliders.</summary>
    internal float WfFloorAbove = float.NaN, WfSpanDb = float.NaN;
    /// <summary>Zero, like WSJT-X: right = brighter. -50..50 moves where the colours start from +10 to -10 dB above the noise.</summary>
    private float FloorAbove => float.IsNaN(WfFloorAbove) ? -_wfZero.Value * 0.2f : WfFloorAbove;
    /// <summary>Gain, like WSJT-X: right = more contrast. Every step multiplies by 10^0.015 (WSJT-X's own step); 0 = 30 dB.</summary>
    private float SpanDb => float.IsNaN(WfSpanDb) ? (float)Math.Clamp(30 / Math.Pow(10, 0.015 * _wfGain.Value), 5, 80) : WfSpanDb;

    // ── the waterfall controls, as on WSJT-X's Wide Graph: Bins/Pixel, Start, N Avg (the speed), Gain, Zero ──
    private readonly NumericUpDown _wfBpp = new() { Minimum = 1, Maximum = 10, Width = 48 };
    private readonly NumericUpDown _wfStart = new() { Minimum = 0, Maximum = 4000, Increment = 50, Width = 64 };
    private readonly NumericUpDown _wfAvg = new() { Minimum = 1, Maximum = 20, Width = 48 };
    private readonly TrackBar _wfGain = new() { Minimum = -50, Maximum = 50, TickFrequency = 10, Width = 120, AutoSize = false, Height = 26 };
    private readonly TrackBar _wfZero = new() { Minimum = -50, Maximum = 50, TickFrequency = 10, Width = 120, AutoSize = false, Height = 26 };
    private const double BinHz = 48000.0 / WfFft;     // 0.732 Hz, WSJT-X's df3

    private Control BuildWaterfallBar()
    {
        _wfBpp.Value = Math.Clamp(_settings.GetInt("waterfall", "bpp", 4), 1, 10);
        _wfStart.Value = Math.Clamp(_settings.GetInt("waterfall", "start", 100), 0, 4000);
        _wfAvg.Value = Math.Clamp(_settings.GetInt("waterfall", "avg", 5), 1, 20);
        _wfGain.Value = Math.Clamp(_settings.GetInt("waterfall", "gain", 0), -50, 50);
        _wfZero.Value = Math.Clamp(_settings.GetInt("waterfall", "zero", 0), -50, 50);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(8, 6, 2, 0) };
        void Add(string label, Control c, string tip)
        {
            var l = L(label);
            c.Margin = new Padding(0, 2, 4, 0);
            bar.Controls.Add(l); bar.Controls.Add(c);
            _tips.SetToolTip(l, tip); _tips.SetToolTip(c, tip);
        }
        Add("Bins/Pixel", _wfBpp, "How many 0.73 Hz bins go into one pixel (WSJT-X: Bins/Pixel). 1 = the finest; higher shows more of the band. The width of the window decides how far it reaches.");
        Add("Start", _wfStart, "The lowest audio frequency on the left, in Hz (WSJT-X: Start).");
        Add("N Avg", _wfAvg, "The speed of the waterfall (WSJT-X: N Avg): how many spectra (one per 80 ms) are averaged into one line. Lower = faster and more speckle; higher = slower and calmer. 5 = a line every 0.4 s.");
        Add("Gain", _wfGain, "Contrast (WSJT-X: Gain): to the right the colours climb faster, so weaker signals already look bright.");
        Add("Zero", _wfZero, "Brightness (WSJT-X: Zero): to the right the whole picture gets brighter, the noise too.");
        _wfBpp.ValueChanged += (_, _) => { _settings.Set("waterfall", "bpp", (int)_wfBpp.Value); ApplyWfRange(clear: true); };
        _wfStart.ValueChanged += (_, _) => { _settings.Set("waterfall", "start", (int)_wfStart.Value); ApplyWfRange(clear: true); };
        _wfAvg.ValueChanged += (_, _) => { _settings.Set("waterfall", "avg", (int)_wfAvg.Value); Array.Clear(_wfAcc); _wfCount = 0; };
        _wfGain.ValueChanged += (_, _) => _settings.Set("waterfall", "gain", _wfGain.Value);
        _wfZero.ValueChanged += (_, _) => _settings.Set("waterfall", "zero", _wfZero.Value);
        _wfLabel = new Label { AutoSize = true, Margin = new Padding(10, 6, 0, 0), ForeColor = Color.DimGray };
        bar.Controls.Add(_wfLabel);
        return bar;
    }
    private Label? _wfLabel;

    /// <summary>The range the waterfall shows: Start up to Start + width x Bins/Pixel x 0.73 Hz (at most 6 kHz), as in WSJT-X.</summary>
    private void ApplyWfRange(bool clear)
    {
        int w = Math.Max(50, _waterfall.ClientSize.Width);
        double lo = (double)_wfStart.Value;
        double hi = Math.Min(6000, lo + w * (double)_wfBpp.Value * BinHz);
        if (hi <= lo + 50) hi = lo + 50;
        if (lo >= _waterfall.MaxHz) { _waterfall.MaxHz = hi; _waterfall.MinHz = lo; }
        else { _waterfall.MinHz = lo; _waterfall.MaxHz = hi; }
        if (clear) _waterfall.Clear();
        if (_wfLabel != null) _wfLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{lo:0}-{hi:0} Hz, {(hi - lo) / w:0.00} Hz/px");
    }

    private float[] _profDb = new float[32], _chunk = new float[256];
    private int[] _profAt = new int[32];

    /// <summary>The noise ACROSS frequency taken off (ShackAssistant's CwWindow): "dB above the noise right here".</summary>
    private void NormaliseToLocalNoise(float[] db, int lastBin, double hzPerBin)
    {
        int n = lastBin + 1;
        if (n < 64) return;
        int per = Math.Max(16, (int)(250.0 / Math.Max(0.5, hzPerBin)));
        int count = (n + per - 1) / per;
        if (count < 2) return;
        if (_profDb.Length < count) { _profDb = new float[count]; _profAt = new int[count]; }
        if (_chunk.Length < per) _chunk = new float[per];
        for (int c = 0, a = 0; a < n; c++, a += per)
        {
            int len = Math.Min(per, n - a);
            Array.Copy(db, a, _chunk, 0, len);
            Array.Sort(_chunk, 0, len);
            _profDb[c] = _chunk[len / 2];
            _profAt[c] = a + len / 2;
        }
        for (int i = 0; i < n; i++)
        {
            float f;
            if (i <= _profAt[0]) f = _profDb[0];
            else if (i >= _profAt[count - 1]) f = _profDb[count - 1];
            else
            {
                int k = 1;
                while (k < count - 1 && _profAt[k] < i) k++;
                int x0 = _profAt[k - 1], x1 = _profAt[k];
                float t = x1 == x0 ? 0f : (float)(i - x0) / (x1 - x0);
                f = _profDb[k - 1] + (_profDb[k] - _profDb[k - 1]) * t;
            }
            db[i] -= f;
        }
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
            if (sig != st.Sig) changed.Add(e);
            if (m.Finished && !st.Logged) { ChatLog(e, mine: false); st.Logged = true; }
            if (!m.Finished) active.Add((e.Hz, m.Call));
            _seen[m] = (st.Id, st.Utc, sig, st.Logged);
        }
        rx.Forget(120);
        foreach (var k in _seen.Keys.Where(k => !rx.Messages.Contains(k)).ToList()) _seen.Remove(k);
        foreach (var k in _echo.Keys.Where(k => !rx.Messages.Contains(k)).ToList()) _echo.Remove(k);
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
            MessageBox.Show(this, $"PTT is set to {cfg.Ptt}, but the radio/port is not open: {_rigState.Status}\n\nCheck File -> Settings -> Radio.",
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
        int dev = Math.Clamp(_settings.GetInt("audio", "output", 0), 0, outs.Count - 1);
        double seconds = samples.Length / (double)BleepsCode.SampleRate;
        _tx.Volume = DriveGain;
        _panel.SetSending(true);
        var rig = _rig;
        Action<bool>? ptt = cfg.UsePtt && rig != null ? on => { if (!rig.SetPtt(on) && on) throw new InvalidOperationException("PTT did not key (" + cfg.Ptt + ")"); } : null;
        _tx.Start(samples, dev, ptt, cfg.TxDelayMs, cfg.TxTailMs);
        double txc = _panel.TxHz + sp.CenterOffset;
        long ownId = _panel.AddOwn(call, text, txc, sp.Name);
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
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Bleeps"); }
    }

    private void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Bleeps"); }
    }

    private void About()
    {
        string hamlib = HamlibNative.IsAvailable ? "Hamlib: " + HamlibNative.VersionString() : "Hamlib: not found";
        MessageBox.Show(this,
            $"Bleeps {Version}\nChat protocol for radio amateurs - by PA5DJ\n\nThe same Bleeps as in ShackAssistant (protocol version 0).\n{hamlib}\n\n{BleepsEmoji.Credit}",
            "About Bleeps", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
