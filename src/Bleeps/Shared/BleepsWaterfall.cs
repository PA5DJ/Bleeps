using System.Globalization;
using ShackAssistant.Helpers;
using ShackAssistant.Helpers.Audio;

namespace ShackAssistant.Forms;

/// <summary>
/// The Bleeps waterfall, built the way WSJT-X's Wide Graph builds it (PA5DJ: "it really has to go towards the resolution of WSJT-X",
/// and "for Bleeps in SA, use the same waterfall system"). ONE copy for Mode Master in Bleeps and for the
/// stand-alone Bleeps program (which links this file).
/// <list type="bullet">
/// <item>A LONG window: 65536 samples at 48 kHz = 1.37 s and 0.73 Hz a bin - WSJT-X's 16384 at 12 kHz. The four tones of
/// Bleeps stand as a sharp block instead of a 45 Hz smear.</item>
/// <item>A spectrum every 80 ms of audio (WSJT-X: every half symbol); N Avg of them averaged in POWER into one row.</item>
/// <item>Per pixel the power of ALL bins in it is added up (WSJT-X: Bins/Pixel) - never one bin picked.</item>
/// <item>The colours: the panel's Skim curve starts at the noise (+ Zero) and climbs over 30 dB (Gain) - with CW's 11 dB
/// every Bleeps station was white.</item>
/// </list>
/// Not for CW/RTTY: a 1.37 s window smears every dit. Mode Master keeps its own fast waterfall there.
/// </summary>
internal sealed class BleepsWaterfall
{
    public const int Fft = 65536, Hop = 3840;
    public const double BinHz = 48000.0 / Fft;          // 0.732 Hz, WSJT-X's df3

    private readonly WaterfallPanel _wf;
    private readonly AppSettings _s;
    private readonly string _section, _prefix;
    private volatile SpectrumAnalyzer? _sp;

    private readonly NumericUpDown _bpp = new() { Minimum = 1, Maximum = 10, Width = 48 };
    private readonly NumericUpDown _start = new() { Minimum = 0, Maximum = 4000, Increment = 50, Width = 64 };
    private readonly NumericUpDown _avg = new() { Minimum = 1, Maximum = 20, Width = 48 };
    private readonly TrackBar _gain = new() { Minimum = -50, Maximum = 50, TickFrequency = 10, Width = 120, AutoSize = false, Height = 26 };
    private readonly TrackBar _zero = new() { Minimum = -50, Maximum = 50, TickFrequency = 10, Width = 120, AutoSize = false, Height = 26 };
    private readonly Label _range = new() { AutoSize = true, Margin = new Padding(10, 6, 0, 0), ForeColor = Color.DimGray };

    private long _done;
    private int _count;
    private double[] _acc = Array.Empty<double>();
    private double _pxHz;
    private bool _floorSet;
    private float[] _profDb = new float[32], _chunk = new float[256];
    private int[] _profAt = new int[32];

    /// <summary>For a harness: the colours directly (NaN = the Gain/Zero sliders).</summary>
    internal float FloorOverride = float.NaN, SpanOverride = float.NaN;

    /// <summary>The control row: Bins/Pixel, Start, N Avg (the speed), Gain, Zero and the range shown. Design units (96 dpi):
    /// the window it is put in scales it.</summary>
    public FlowLayoutPanel Bar { get; }

    /// <summary>While active it owns the panel's range and colours; Mode Master switches it off outside Bleeps.</summary>
    public bool Active { get; private set; }

    /// <param name="section">INI section, <paramref name="prefix"/> before each key ("bpp", "start", "avg", "gain", "zero").</param>
    public BleepsWaterfall(WaterfallPanel wf, AppSettings s, string section, string prefix, ToolTip tips)
    {
        _wf = wf; _s = s; _section = section; _prefix = prefix;
        _bpp.Value = Math.Clamp(s.GetInt(section, prefix + "bpp", 4), 1, 10);
        _start.Value = Math.Clamp(s.GetInt(section, prefix + "start", 100), 0, 4000);
        _avg.Value = Math.Clamp(s.GetInt(section, prefix + "avg", 1), 1, 20);   // default 1 since v1.0.24 (PA5DJ: "set N Avg default to 1")
        _gain.Value = Math.Clamp(s.GetInt(section, prefix + "gain", 0), -50, 50);
        _zero.Value = Math.Clamp(s.GetInt(section, prefix + "zero", 0), -50, 50);

        Bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        void Add(string label, Control c, string tip)
        {
            var l = new Label { Text = label, AutoSize = true, Margin = new Padding(8, 6, 2, 0) };
            c.Margin = new Padding(0, 2, 4, 0);
            Bar.Controls.Add(l); Bar.Controls.Add(c);
            tips.SetToolTip(l, tip); tips.SetToolTip(c, tip);
        }
        Add("Bins/Pixel", _bpp, "How many 0.73 Hz bins go into one pixel (WSJT-X: Bins/Pixel). 1 = the finest; higher shows more of the band. The width of the window decides how far it reaches.");
        Add("Start", _start, "The lowest audio frequency on the left, in Hz (WSJT-X: Start).");
        Add("N Avg", _avg, "The speed of the waterfall (WSJT-X: N Avg): how many spectra (one per 80 ms) are averaged into one line. Lower = faster and more speckle; higher = slower and calmer. 1 (the default) = a line every 80 ms, 5 = every 0.4 s.");
        Add("Gain", _gain, "Contrast (WSJT-X: Gain): to the right the colours climb faster, so weaker signals already look bright.");
        Add("Zero", _zero, "Brightness (WSJT-X: Zero): to the right the whole picture gets brighter, the noise too.");
        Bar.Controls.Add(_range);

        _bpp.ValueChanged += (_, _) => { _s.Set(_section, _prefix + "bpp", (int)_bpp.Value); ApplyRange(clear: true); };
        _start.ValueChanged += (_, _) => { _s.Set(_section, _prefix + "start", (int)_start.Value); ApplyRange(clear: true); };
        _avg.ValueChanged += (_, _) => { _s.Set(_section, _prefix + "avg", (int)_avg.Value); Array.Clear(_acc); _count = 0; };
        _gain.ValueChanged += (_, _) => _s.Set(_section, _prefix + "gain", _gain.Value);
        _zero.ValueChanged += (_, _) => _s.Set(_section, _prefix + "zero", _zero.Value);
        _wf.Resize += (_, _) => { if (Active) ApplyRange(clear: false); };
    }

    /// <summary>Start (or stop) owning the panel. Off: the caller puts its own range and colours back.</summary>
    public void SetActive(bool on)
    {
        Active = on;
        Bar.Visible = on;
        Reset();
        if (on) ApplyRange(clear: true);
    }

    /// <summary>Forget the audio: a new spectrum, a new average, the floor set afresh.</summary>
    public void Reset()
    {
        _sp = Active ? new SpectrumAnalyzer(48000, Fft, 1, historySeconds: 4) : null;
        _done = 0; _count = 0; _floorSet = false;
        Array.Clear(_acc);
    }

    /// <summary>From the capture thread (48 kHz mono). Nothing while not active.</summary>
    public void Add(float[] samples) => _sp?.Add(samples);

    private int Avg => (int)_avg.Value;
    /// <summary>Zero, like WSJT-X: right = brighter. -50..50 moves where the colours start from +10 to -10 dB above the noise.</summary>
    private float FloorAbove => float.IsNaN(FloorOverride) ? -_zero.Value * 0.2f : FloorOverride;
    /// <summary>Gain, like WSJT-X: right = more contrast; every step 10^0.015 (WSJT-X's own step); 0 = 30 dB.</summary>
    private float SpanDb => float.IsNaN(SpanOverride) ? (float)Math.Clamp(30 / Math.Pow(10, 0.015 * _gain.Value), 5, 80) : SpanOverride;

    /// <summary>The range: Start up to Start + width x Bins/Pixel x 0.73 Hz (at most 6 kHz), as in WSJT-X.</summary>
    public void ApplyRange(bool clear)
    {
        if (!Active) return;
        int w = Math.Max(50, _wf.ClientSize.Width);
        double lo = (double)_start.Value;
        double hi = Math.Min(6000, lo + w * (double)_bpp.Value * BinHz);
        if (hi <= lo + 50) hi = lo + 50;
        if (lo >= _wf.MaxHz) { _wf.MaxHz = hi; _wf.MinHz = lo; }
        else { _wf.MinHz = lo; _wf.MaxHz = hi; }
        if (clear) _wf.Clear();
        _range.Text = string.Create(CultureInfo.InvariantCulture, $"{lo:0}-{hi:0} Hz, {(hi - lo) / w:0.00} Hz/px");
    }

    /// <summary>From the UI timer: one spectrum per 80 ms of new audio; every N Avg spectra a row.</summary>
    public void Step()
    {
        var sp = _sp;
        if (!Active || sp == null || !sp.HasFrame) return;
        long total = sp.Total;
        if (total - _done < Hop) return;
        _done = total;
        int w = Math.Max(50, _wf.ClientSize.Width);
        double pxHz = (_wf.MaxHz - _wf.MinHz) / w;
        int n = (int)(_wf.MaxHz / pxHz) + 2;
        if (_acc.Length != n || Math.Abs(_pxHz - pxHz) > 1e-9) { _acc = new double[n]; _count = 0; _pxHz = pxHz; }

        var db = sp.Frame();
        double df = sp.HzPerBin;
        for (int k = 0; k < n; k++)
        {
            int b0 = Math.Max(1, (int)Math.Ceiling((k - 0.5) * pxHz / df));
            int b1 = Math.Min(db.Length - 1, (int)Math.Floor((k + 0.5) * pxHz / df));
            double p = 0;
            for (int b = b0; b <= b1; b++) p += Math.Pow(10, db[b] / 10.0);
            _acc[k] += p;
        }
        if (++_count < Avg) return;

        var row = new float[n];
        for (int k = 0; k < n; k++) row[k] = (float)(10 * Math.Log10(Math.Max(_acc[k] / _count, 1e-20)));
        Array.Clear(_acc);
        _count = 0;
        int last = n - 1;
        NormaliseToLocalNoise(row, last, pxHz);
        int lo = Math.Clamp((int)(_wf.MinHz / pxHz), 0, last);
        var slice = row.AsSpan(lo, last - lo + 1).ToArray();
        Array.Sort(slice);
        float noise = slice[slice.Length / 2];
        // the panel's Skim curve starts 7 dB under FloorDb and climbs CurveDb
        float want = noise + 7f + FloorAbove;
        // the first row sets the floor outright: easing in from the panel's -95 dB painted the first seconds white
        if (!_floorSet) { _wf.FloorDb = want; _floorSet = true; }
        else _wf.FloorDb += (want - _wf.FloorDb) * 0.2f;
        _wf.CurveDb = SpanDb;
        _wf.AddFrame(row, pxHz);
    }

    /// <summary>The noise ACROSS frequency taken off (Mode Master's own): "dB above the noise right here".</summary>
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
}
