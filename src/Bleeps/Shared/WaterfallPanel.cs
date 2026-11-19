using System.Drawing.Imaging;
using ShackAssistant.Helpers;

namespace ShackAssistant.Forms;

/// <summary>
/// Scrolling waterfall with a draggable tuning cursor and a shaded pass-band around it, plus a
/// millimetre-style Hz scale along the bottom.
///
/// Drawing only: it is handed one spectrum frame at a time and owns no audio. The cursor and bandwidth
/// live here because that is where you set them with the mouse; whoever decodes reads them back through
/// <see cref="CursorHz"/> / <see cref="BandwidthHz"/> and listens to <see cref="CursorChanged"/>.
/// </summary>
public sealed class WaterfallPanel : Panel
{
    /// <summary>Lowest frequency shown. Not zero by default: below about a hundred hertz there is nothing
    /// to hear and everything to clutter — mains hum, rumble, and whatever offset the sound card carries.
    /// Cutting it off also spreads the useful part over more pixels.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double MinHz { get => _minHz; set { if (value != _minHz) { _minHz = value; ResetZoom(); } } }
    private double _minHz = 100;

    /// <summary>Highest frequency shown. CW lives well under 3 kHz, so the detail goes where it matters.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double MaxHz { get => _maxHz; set { if (value != _maxHz) { _maxHz = value; ResetZoom(); } } }
    private double _maxHz = 3000;

    /// <summary>Width of the displayed range, never zero.</summary>
    private double SpanHz => Math.Max(1, MaxHz - MinHz);

    // ── zoom (lying down) ─────────────────────────────────────────────────
    // Lying down the mouse wheel zooms the FREQUENCY axis in and out around the point under the mouse
    // (PD5DJ), so a signal can be looked at closely. It used to tune in steps of 10 Hz, which a click does
    // better. Standing, the wheel still tunes. The zoomed range lives only here, inside MinHz..MaxHz, and
    // is not saved: a new range from the settings or a change of orientation shows the whole band again.
    private double _viewLo = double.NaN, _viewHi = double.NaN;

    /// <summary>The narrowest the view can be zoomed to. A 43 ms window resolves about 12 Hz a bin, so
    /// below this there is nothing more to see, only bigger blocks.</summary>
    internal const double MinZoomSpanHz = 150;

    /// <summary>The frequency range on screen: the zoomed view lying down, else the whole range.</summary>
    private double VLo => _horizontal && !double.IsNaN(_viewLo) ? _viewLo : MinHz;
    private double VHi => _horizontal && !double.IsNaN(_viewHi) ? _viewHi : MaxHz;
    private double VSpan => Math.Max(1, VHi - VLo);

    /// <summary>True while lying down and zoomed in.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Zoomed => _horizontal && !double.IsNaN(_viewLo);

    /// <summary>Show the whole range again.</summary>
    public void ResetZoom()
    {
        if (double.IsNaN(_viewLo)) return;
        ZoomTo(MinHz, MaxHz);
    }

    /// <summary>
    /// Zoom by <paramref name="factor"/> (under 1 = in) keeping <paramref name="anchorHz"/> where it is on
    /// screen. The picture already there is REMAPPED row by row, so the history stays readable at the new
    /// scale: rows that come into view from outside the old range are black until new columns fill them.
    /// </summary>
    internal void Zoom(double factor, double anchorHz)
    {
        double lo = VLo, hi = VHi, span = VSpan;
        double full = SpanHz;
        double nSpan = Math.Clamp(span * factor, Math.Min(MinZoomSpanHz, full), full);
        double frac = (hi - anchorHz) / span;                 // where the anchor sits, from the top
        double nHi = anchorHz + frac * nSpan;
        double nLo = nHi - nSpan;
        if (nLo < MinHz) { nLo = MinHz; nHi = nLo + nSpan; }
        if (nHi > MaxHz) { nHi = MaxHz; nLo = nHi - nSpan; }
        ZoomTo(nLo, nHi);
    }

    private void ZoomTo(double nLo, double nHi)
    {
        bool full = nLo <= MinHz + 0.01 && nHi >= MaxHz - 0.01;
        _viewLo = full ? double.NaN : nLo;
        _viewHi = full ? double.NaN : nHi;
        // Nothing is lost: lying down the picture is worked out from the full-band bins, so a zoom just
        // redraws it at the new scale on the next paint, and zooming out again brings everything back.
        _rowMapOk = false;
        Invalidate();
    }

    /// <summary>Tuning cursor — the frequency a decoder should listen on.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double CursorHz
    {
        get => _cursorHz;
        set
        {
            double v = Math.Clamp(value, MinHz, MaxHz);
            if (Math.Abs(v - _cursorHz) < 0.01) return;
            _cursorHz = v;
            CursorChanged?.Invoke(_cursorHz);
            Invalidate();
        }
    }

    /// <summary>Total width of the pass-band around the cursor, in Hz.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double BandwidthHz
    {
        get => _bandwidthHz;
        set { _bandwidthHz = Math.Clamp(value, 20, 1000); Invalidate(); }
    }

    /// <summary>
    /// Distance to a second tone, in Hz, or zero for none.
    ///
    /// RTTY is tuned as a PAIR, and with this set the CURSOR MEANS THE MIDDLE OF IT: the two tones are
    /// drawn half a shift either side, each with its own pass-band and its own reading. That is what the
    /// operator points at — the traces are symmetric and the eye goes to the gap between them — and it
    /// is why changing the shift opens them symmetrically rather than dragging one tone across the band.
    ///
    /// The middle itself gets a thin dotted line: it is what you aim, but there is nothing there to hear,
    /// and a third solid line reads as a third station.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double PairShiftHz
    {
        get => _pairShiftHz;
        set { _pairShiftHz = Math.Max(0, value); Invalidate(); }
    }

    private double _pairShiftHz;

    /// <summary>
    /// Where the two tones will LAND once a click has tuned the radio: the MARK pitch the rig keys FSK
    /// at, with space a shift above it. 0 hides both markers.
    ///
    /// <para>
    /// It is a fixed pair, not a moving one, and that is the whole point: with click-to-tune on the
    /// operator does not bring the cursor to the station, the RADIO brings the station here. Without a
    /// mark on the glass that target is a number in a settings window.
    /// </para>
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double TuneMarkHz
    {
        get => _tuneMarkHz;
        set { _tuneMarkHz = Math.Max(0, value); Invalidate(); }
    }

    private double _tuneMarkHz;

    /// <summary>
    /// The TX tone when it is split off the RX tone (CW-AFSK, v1.0.407-408): the RED line, with a triangle on the
    /// edge; the RX cursor is then drawn green (PA5DJ: "TX moet de rode lijn zijn. niet rx"). 0 hides it.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double TxMarkHz
    {
        get => _txMarkHz;
        set { double v = Math.Max(0, value); if (v == _txMarkHz) return; _txMarkHz = v; Invalidate(); }
    }

    private double _txMarkHz;

    /// <summary>
    /// Fixed lines with a label, for a mode that lives at known tones (v1.0.612, SSTV; PA5DJ: "ik mis sync markers over het
    /// audio spectrum"): sync 1200, black 1500, VIS leader 1900, white 2300. Null = none.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<(double Hz, Color Colour, string Label)>? Markers
    {
        get => _markers;
        set { _markers = value; Invalidate(); }
    }
    private IReadOnlyList<(double Hz, Color Colour, string Label)>? _markers;

    /// <summary>
    /// Frequency STRIPS (v1.0.725, PA5DJ: "geef TX marker ook de breedte mee van het TX signaal"): a see-through band with
    /// thin edges from LoHz to HiHz, under the <see cref="Markers"/>. Bleeps: where our signal will be. Null = none.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<(double LoHz, double HiHz, Color Colour)>? Bands
    {
        get => _bands;
        set { _bands = value; Invalidate(); }
    }
    private IReadOnlyList<(double LoHz, double HiHz, Color Colour)>? _bands;

    /// <summary>The tuning cursor and its pass-band. Off for a mode without one (SSTV listens to the whole band).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowCursor
    {
        get => _showCursor;
        set { if (value == _showCursor) return; _showCursor = value; Invalidate(); }
    }
    private bool _showCursor = true;

    /// <summary>
    /// TX split off RX (v1.0.408; the buttons swapped in v1.0.410, PA5DJ: "rechtermuis klik moet TX zijn en links
    /// RX"): a RIGHT click or drag moves only the TX tone (<see cref="TxMarkHz"/>, reported by
    /// <see cref="TxCommitted"/>), a LEFT click tunes the RX cursor as it always did. The wheel, standing,
    /// fine-tunes RX.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool SplitTx { get; set; }

    /// <summary>The TX tone a left click or drag settled on, while <see cref="SplitTx"/> is on.</summary>
    public event Action<double>? TxCommitted;

    /// <summary>The colour of the RX cursor while TX is split off - not red, red is TX.</summary>
    public static readonly Color RxSplitColour = Color.FromArgb(60, 220, 110);

    private bool _txDragging;

    /// <summary>Bottom of the colour scale (dB). Everything at or below this is black.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float FloorDb { get; set; } = -95f;
    /// <summary>Top of the colour scale (dB).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float CeilingDb { get; set; } = -25f;

    /// <summary>
    /// LYING DOWN, the CW Skimmer way: time runs left to right with "now" at the right edge, and the
    /// frequency - the whole <see cref="MinHz"/>..<see cref="MaxHz"/> range - runs top (high) to bottom
    /// (low). See PLAN-WATERFALL.
    ///
    /// <para>
    /// Same rectangle as standing up, and that is the point: the panel is far wider than it is high, so
    /// giving the WIDTH to time is what buys a long history at a speed where every dit is its own mark.
    /// Everything that is tuned with the mouse turns with it - cursor, pass-bands, the RTTY pair, the
    /// tuning marks, the aiming ghost - so there is one set of rules and a different axis, not two sets.
    /// </para>
    /// <para>
    /// Switching throws the picture away. The two layouts share no pixels (one row per frame against one
    /// column per 10 ms), so there is nothing to carry across.
    /// </para>
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Horizontal
    {
        get => _horizontal;
        set
        {
            if (_horizontal == value) return;
            _horizontal = value;
            _viewLo = _viewHi = double.NaN;
            _pixels = Array.Empty<byte>();
            _bins = Array.Empty<byte>(); _nb = 0;
            _wfW = _wfH = 0;
            _colHead = 0;
            Invalidate();
        }
    }

    private bool _horizontal;

    /// <summary>Lying down: the next column to be written. The picture is a ring so that adding a column
    /// is one column of writes instead of shifting the whole picture a pixel - at 100 columns a second
    /// on a 1000 x 200 panel that would be twenty million copies a second for nothing.</summary>
    private int _colHead;

    /// <summary>
    /// Lying down: what is STORED - per column, a place on the colour ramp for every bin of the whole range
    /// (MinHz..MaxHz), column after column in a ring. The picture on screen is worked out from this on
    /// every paint, for whatever part of the band is in view. That is what makes zooming free: zoom in and
    /// the history is there at the new scale, zoom out again and nothing outside the zoomed part was lost -
    /// which is exactly what happened while the picture itself was the store.
    /// </summary>
    private byte[] _bins = Array.Empty<byte>();
    private int    _nb;               // bins per column
    private int    _binLo;            // the spectrum bin that is _bins[..*_nb + 0]
    private double _binHz;            // hertz per bin of what is stored

    /// <summary>Lying down: a pixel row takes the MEAN of its bins instead of the strongest. Measurement
    /// knob for now; see AddColumn.</summary>
    internal bool RowMean;

    public event Action<double>? CursorChanged;

    /// <summary>The operator has CHOSEN a frequency: a click, or a wheel notch. Dragging deliberately does
    /// not count — that is searching, and a listener that re-decodes ten seconds for every station you
    /// slide past would stutter and fill the screen with fragments. <see cref="CursorChanged"/> fires on
    /// every pixel of a drag and is the one to use for a status line.</summary>
    public event Action<double>? CursorCommitted;


    private double _cursorHz    = 700;
    private double _bandwidthHz = 240;

    // One byte per pixel: a place on the colour ramp, 0 (black) .. 255 (brightest). The COLOUR is only
    // decided when it is drawn, through the palette of an 8-bit bitmap - so choosing another palette
    // recolours everything already on screen at once, not only what arrives after.
    private byte[]  _pixels = Array.Empty<byte>();
    private Color[] _lut    = BuildLut("Classic");
    private int     _wfW, _wfH;
    private Bitmap? _bmp;
    private bool _dragging, _dragged;
    private int  _pressPos;
    /// <summary>Where the mouse hovers, for the RTTY aiming ghost. NaN = not over the panel.</summary>
    private double _hoverHz = double.NaN;

    // Everything this control draws itself has to be scaled by hand. AutoScale only touches control
    // BOUNDS, so a hardcoded pixel inside OnPaint stays the same size on a 4K screen while the text
    // beside it grows — the axis strip was 22 px with a label that needs 30 at 200%.
    private float DpiScale        => DeviceDpi / 96f;
    private int   Px(int logical)   => (int)Math.Round(logical * DpiScale);
    private float Pxf(float logical) => logical * DpiScale;

    /// <summary>Room for the Hz scale at the bottom (standing).</summary>
    private int AxisH => Px(22);

    /// <summary>Room for the Hz scale at the right edge (lying down) - the edge where "now" is, so the
    /// eye reading what just arrived has the scale beside it.</summary>
    private int AxisW => Px(36);

    /// <summary>The picture itself, without the scale.</summary>
    private int PicW => Math.Max(0, ClientSize.Width  - (_horizontal ? AxisW : 0));
    private int PicH => Math.Max(0, ClientSize.Height - (_horizontal ? 0 : AxisH));

    public WaterfallPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw   = true;
        BackColor      = Color.Black;
        Cursor         = Cursors.Hand;
    }

    /// <summary>Push one spectrum frame (dB per bin); it scrolls in at the top. Standing only.</summary>
    public void AddFrame(float[] db, double hzPerBin)
    {
        if (_horizontal) return;
        int w = PicW, h = PicH;
        if (w <= 0 || h <= 0) return;      // minimised: no room to scroll into, and nothing to throw away
        EnsureSize(w, h);

        // Scroll down one row, then paint the newest frame into row 0.
        Array.Copy(_pixels, 0, _pixels, w, (h - 1) * w);

        for (int x = 0; x < w; x++)
        {
            double hz  = MinHz + SpanHz * x / w;
            int    bin = (int)Math.Round(hz / hzPerBin);
            float  v   = (bin >= 0 && bin < db.Length) ? db[bin] : FloorDb;
            // DEZELFDE kromme als liggend (PD5DJ, v1.0.183). Het was een lineaire helling van vloer tot
            // plafond, en daarmee deden Background en Curve staand niets - gemeten 0 van 84000 pixels
            // verschil bij elke stand van die twee. Nu is er EEN beschrijving van de kleuren, en de drie
            // knoppen eronder - Noise, Background, Curve - gelden in beide standen en in alle modes.
            //
            // CeilingDb wordt hierdoor door de waterval niet meer gelezen; hij voedt nog wel de strook
            // van de skimmer, die zijn eigen lineaire Heat-ramp heeft.
            _pixels[x] = Skim(v);
        }
        Invalidate();
    }

    /// <summary>
    /// Push one spectrum column (dB per bin); it arrives at the RIGHT edge. Lying down only.
    ///
    /// Does not repaint: a caller adds several columns per UI tick and invalidates once.
    ///
    /// Each pixel takes the STRONGEST bin that falls inside it, not the nearest one. Lying down, one pixel
    /// covers several bins (the whole band over a couple of hundred pixels), and a narrow trace that falls
    /// between two sampled bins would otherwise simply not be drawn.
    /// </summary>
    public void AddColumn(float[] db, double hzPerBin)
    {
        if (!_horizontal) return;
        int w = PicW, h = PicH;
        if (w <= 0 || h <= 0) return;
        EnsureSize(w, h);
        EnsureBins(hzPerBin, db.Length);
        if (_nb == 0) return;

        int at = _colHead * _nb;
        for (int i = 0; i < _nb; i++)
        {
            int b = _binLo + i;
            _bins[at + i] = b < db.Length ? Skim(db[b]) : (byte)0;
        }
        if (_rowMapOk) RenderColumn(_colHead, w, h);     // else the next paint redraws the lot
        _colHead = (_colHead + 1) % w;
    }

    /// <summary>Make the bin store fit this spectrum and this range. Only a change of bin size or of the
    /// range starts it again; everything else keeps what is stored.</summary>
    private void EnsureBins(double hzPerBin, int binCount)
    {
        int lo = Math.Max(0, (int)Math.Floor(MinHz / hzPerBin));
        int hi = Math.Min(binCount - 1, (int)Math.Ceiling(MaxHz / hzPerBin));
        int nb = Math.Max(0, hi - lo + 1);
        if (nb == _nb && lo == _binLo && Math.Abs(hzPerBin - _binHz) < 1e-9 && _bins.Length == _wfW * nb) return;
        _nb = nb; _binLo = lo; _binHz = hzPerBin;
        _bins = new byte[_wfW * nb];
        _colHead = 0;
        _rowMapOk = false;
    }

    public void Clear()
    {
        if (_pixels.Length > 0) Array.Clear(_pixels, 0, _pixels.Length);
        if (_bins.Length > 0) Array.Clear(_bins, 0, _bins.Length);
        _rowMapOk = false;
        Invalidate();
    }

    /// <summary>Resize the waterfall while KEEPING what is already on it.
    ///
    /// It used to allocate a fresh buffer, so every drag of the window edge — and every minimise, which
    /// arrives as a size of zero — wiped the history. That is the one thing a waterfall must not do: the
    /// minutes already on screen are the reason to have one.
    ///
    /// Standing, the x axis is a frequency scale, so on a width change the same hertz has to land on the
    /// new pixel; rows are time and keep their place. Lying down it is the other way round: rows are
    /// frequency and are rescaled, columns are time and stay RIGHT-aligned, so "now" stays at the edge
    /// and a narrower panel drops the oldest.</summary>
    private void EnsureSize(int w, int h)
    {
        if (w <= 0 || h <= 0) return;                       // minimised: keep everything, draw nothing
        if (_wfW == w && _wfH == h && _pixels.Length == w * h) return;

        var old = _pixels;
        int ow = _wfW, oh = _wfH;
        var fresh = new byte[w * h];

        if (old.Length == ow * oh && ow > 0 && oh > 0)
        {
            if (!_horizontal)
            {
                int rows = Math.Min(oh, h);
                for (int y = 0; y < rows; y++)
                {
                    int src = y * ow, dst = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int sx = ow == w ? x : (int)((long)x * ow / w);   // same frequency, new pixel
                        fresh[dst + x] = old[src + Math.Min(sx, ow - 1)];
                    }
                }
            }
        }

        // Lying down the store is the bins, and only the WIDTH (the number of columns) matters to it: the
        // height is just how the bins are drawn. Columns stay RIGHT-aligned so "now" stays at the edge.
        if (_horizontal && _nb > 0 && ow != w)
        {
            var bins = new byte[w * _nb];
            if (_bins.Length == ow * _nb && ow > 0)
                for (int c = 0; c < ow; c++)                   // logical c: 0 = oldest, at (head + c) % ow
                {
                    int x = c + (w - ow);
                    if (x < 0 || x >= w) continue;
                    Array.Copy(_bins, ((_colHead + c) % ow) * _nb, bins, x * _nb, _nb);
                }
            _bins = bins;
            _colHead = 0;
        }
        else if (_horizontal && ow != w) _colHead = 0;

        _pixels = fresh;
        _wfW = w; _wfH = h;
        _rowMapOk = false;
        if (!_horizontal) _colHead = 0;
        _bmp?.Dispose();
        _bmp = new Bitmap(w, h, PixelFormat.Format8bppIndexed);
        ApplyPalette();
    }

    /// <summary>The palettes on offer. The names are what the INI stores, so they stay as they are.</summary>
    public static readonly string[] PaletteNames = { "Skimmer cyan", "Classic", "Grey", "Green", "Fire", "Ice" };

    /// <summary>The colour ramp, by name - see <see cref="PaletteNames"/>. An unknown name is Classic.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Palette
    {
        get => _palette;
        set
        {
            string v = Array.IndexOf(PaletteNames, value) >= 0 ? value : "Classic";
            if (v == _palette) return;
            _palette = v;
            _lut = BuildLut(v);
            ApplyPalette();
            Invalidate();
        }
    }
    private string _palette = "Classic";

    /// <summary>
    /// Lying down: over how many decibels the colours climb from the dark ground to the bright end. Small
    /// is the steep CW Skimmer look - a signal is either there or it is not; large is gentle, so how
    /// strong a signal is shows as how bright it is. 11 is the curve the PLAN-WATERFALL prototype chose.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float CurveDb { get; set; } = 11f;

    /// <summary>The filter edges. Set from Key Master Settings -> Appearance.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color EdgeColour { get; set; } = Color.FromArgb(60, 255, 60);

    /// <summary>The centre line (the tuning cursor; in RTTY the tone lines and the dotted middle).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color CentreColour { get; set; } = Color.FromArgb(255, 80, 80);

    /// <summary>
    /// Lying down: how bright the noise itself may get, as a share of the ramp (0 .. 0.5). The noise is the
    /// ground under the curve; at 0.08 it is a dark speckle, higher lets the texture of the band come up
    /// WITHOUT the noise turning the colour of a signal - which is what winding the Noise slider down does.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Background { get; set; } = 0.08f;

    /// <summary>
    /// Gradient stops per palette, 0..1. De eerste stop voorbij 0 zit op 0.08: daaronder is de donkere
    /// grond waar de ruis in spikkelt, daarboven het signaal. Dat is dezelfde 0.08 als de standaardstand
    /// van <see cref="Background"/>, en dat is geen toeval - die twee horen bij elkaar.
    ///
    /// <para>
    /// Die stop zat eerst alleen in de paletten die voor de LIGGENDE waterval gemaakt zijn, want alleen
    /// die ging door <see cref="Skim"/>; staand liep een lineaire helling. Sinds v1.0.183 gaan ze
    /// allebei door Skim, en dus heeft Classic hem ook nodig: zonder die stop landt de grond op een
    /// kwart van de weg naar blauw en wordt een lege band een BLAUWE MUUR - precies wat v2.7.118 met de
    /// kleurbodem heeft weggewerkt. Gevonden door het beeld te RENDEREN; alle getallen stonden groen.
    /// </para>
    /// </summary>
    private static (double t, Color c)[] Stops(string name) => name switch
    {
        "Skimmer cyan" => new[] { (0.0, Color.Black), (0.08, Color.FromArgb(0, 18, 26)), (1.0, Color.FromArgb(25, 225, 235)) },
        "Grey"         => new[] { (0.0, Color.Black), (1.0, Color.White) },
        "Green"        => new[] { (0.0, Color.Black), (0.08, Color.FromArgb(0, 22, 0)), (1.0, Color.FromArgb(90, 255, 110)) },
        "Fire"         => new[] { (0.0, Color.Black), (0.08, Color.FromArgb(30, 0, 0)), (0.4, Color.FromArgb(200, 30, 0)),
                                  (0.7, Color.FromArgb(255, 150, 0)), (1.0, Color.FromArgb(255, 255, 210)) },
        "Ice"          => new[] { (0.0, Color.Black), (0.08, Color.FromArgb(0, 10, 35)), (0.5, Color.FromArgb(40, 110, 230)),
                                  (1.0, Color.FromArgb(235, 245, 255)) },
        // Black -> blue -> cyan -> yellow -> white, the classic waterfall ramp.
        _              => new[] { (0.0, Color.Black), (0.08, Color.FromArgb(0, 0, 28)), (0.25, Color.Blue),
                                  (0.5, Color.Cyan), (0.75, Color.Yellow), (1.0, Color.White) },
    };

    /// <summary>The 256 colours of a palette, interpolated between its stops.</summary>
    internal static Color[] BuildLut(string name)
    {
        var st  = Stops(name);
        var lut = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            int k = 1;
            while (k < st.Length - 1 && st[k].t < t) k++;
            var (t0, c0) = st[k - 1];
            var (t1, c1) = st[k];
            double u = t1 > t0 ? Math.Clamp((t - t0) / (t1 - t0), 0, 1) : 0;
            lut[i] = Color.FromArgb((int)Math.Round(c0.R + (c1.R - c0.R) * u),
                                    (int)Math.Round(c0.G + (c1.G - c0.G) * u),
                                    (int)Math.Round(c0.B + (c1.B - c0.B) * u));
        }
        return lut;
    }

    /// <summary>Give the bitmap the current palette. 256 entries and nothing redrawn, so it is cheap.</summary>
    private void ApplyPalette()
    {
        if (_bmp == null) return;
        var pal = _bmp.Palette;
        for (int i = 0; i < 256 && i < pal.Entries.Length; i++) pal.Entries[i] = _lut[i];
        _bmp.Palette = pal;                 // a ColorPalette is handed out as a copy: it has to go back
    }

    /// <summary>A place on the ramp for 0..1.</summary>
    private static byte Level(double t) => (byte)Math.Round(Math.Clamp(t, 0, 1) * 255);

    /// <summary>
    /// The lying-down curve, CW Skimmer style: the noise stays a dark, fine speckle and everything that
    /// really stands out of it goes to the bright end over an S-curve. The long standing ramp draws every
    /// keying sideband as a halo around the trace; this one leaves a clean mark per element.
    ///
    /// <para>
    /// Where it starts follows the Noise slider (floor - 7 dB: at the default +10 that is 3 dB above the
    /// noise); how steep it is, is <see cref="CurveDb"/>. The bottom <see cref="Background"/> of the ramp is the ground, which
    /// the noise itself speckles over the ten dB under the curve.
    /// </para>
    /// </summary>
    internal byte Skim(float v)
    {
        float lo = FloorDb - 7f;
        float hi = lo + Math.Max(2f, CurveDb);
        if (v <= lo)
        {
            if (v == float.MinValue) return 0;
            double gnd = Math.Clamp((v - (lo - 10f)) / 10.0, 0, 1);
            return Level(Math.Clamp(Background, 0f, 0.5f) * gnd);
        }
        double t = Math.Clamp((v - lo) / (hi - lo), 0, 1);
        t = t * t * (3 - 2 * t);                                      // smoothstep
        float g = Math.Clamp(Background, 0f, 0.5f);
        return Level(g + (1 - g) * t);
    }

    /// <summary>Where a frequency sits along the frequency axis: x standing, y lying down (high on top).</summary>
    private double HzToPos(double hz) => _horizontal
        ? (VHi - hz) / VSpan * PicH
        : (hz - MinHz) / SpanHz * ClientSize.Width;

    /// <summary>The frequency under a mouse position.</summary>
    private double PosToHz(Point p) => _horizontal
        ? Math.Clamp(VHi - (double)p.Y / Math.Max(1, PicH) * VSpan, VLo, VHi)
        : Math.Clamp(MinHz + (double)p.X / Math.Max(1, ClientSize.Width) * SpanHz, MinHz, MaxHz);

    /// <summary>The position along the frequency axis of a mouse point - for telling a click from a drag.</summary>
    private int AlongFreq(Point p) => _horizontal ? p.Y : p.X;

    // Lying down, the picture (_pixels) is a ring of columns like the bin store, worked out from the
    // bins ONE COLUMN AT A TIME as each arrives - measured, redoing the whole picture on every paint cost
    // 10 ms a frame, two thirds of a core at 64 frames a second. Only a zoom, a new size or a new bin
    // store redraws all of it, once (_rowMapOk = false).
    //
    // The row map says, per pixel row, which bins it shows: the strongest of i0..i1, or - zoomed in so far
    // that a row is narrower than a bin - an interpolation between bin k and k+1 at fraction t, so the
    // picture is a smooth trace and not a staircase of blocks.
    private int[]   _rowI0 = Array.Empty<int>(), _rowI1 = Array.Empty<int>();
    private float[] _rowT  = Array.Empty<float>();          // < 0: take the strongest of i0..i1
    private bool    _rowMapOk;

    private void BuildRowMap(int h)
    {
        if (_rowI0.Length != h) { _rowI0 = new int[h]; _rowI1 = new int[h]; _rowT = new float[h]; }
        for (int y = 0; y < h; y++)
        {
            double hzTop = VHi - VSpan * y / h;
            double hzBot = VHi - VSpan * (y + 1) / h;
            if (hzTop - hzBot < _binHz * 0.75)
            {
                double f = (hzTop + hzBot) / 2 / _binHz - _binLo;
                int k = Math.Clamp((int)Math.Floor(f), 0, _nb - 1);
                _rowI0[y] = k;
                _rowI1[y] = Math.Min(k + 1, _nb - 1);
                _rowT[y]  = (float)Math.Clamp(f - k, 0, 1);
            }
            else
            {
                int i0 = (int)Math.Floor(hzBot / _binHz) - _binLo;
                int i1 = (int)Math.Ceiling(hzTop / _binHz) - _binLo;
                if (i1 < i0) i1 = i0;
                _rowI0[y] = Math.Clamp(i0, 0, _nb - 1);
                _rowI1[y] = Math.Clamp(i1, 0, _nb - 1);
                _rowT[y]  = -1;
            }
        }
        _rowMapOk = true;
    }

    /// <summary>Work one stored column (physical index <paramref name="c"/>) into the picture.</summary>
    private void RenderColumn(int c, int w, int h)
    {
        int at = c * _nb;
        for (int y = 0; y < h; y++)
        {
            int i0 = _rowI0[y], i1 = _rowI1[y];
            float t = _rowT[y];
            int v;
            if (t >= 0) v = (int)(_bins[at + i0] + (_bins[at + i1] - _bins[at + i0]) * t);
            else if (!RowMean) { v = 0; for (int i = i0; i <= i1; i++) { int b = _bins[at + i]; if (b > v) v = b; } }
            else { int sum = 0; for (int i = i0; i <= i1; i++) sum += _bins[at + i]; v = sum / (i1 - i0 + 1); }
            _pixels[y * w + c] = (byte)v;
        }
    }

    /// <summary>Redraw the whole lying-down picture from the bins: after a zoom, a resize or a new store.</summary>
    private void RenderLying(int w, int h)
    {
        if (_pixels.Length != w * h) return;
        if (_nb == 0 || _bins.Length != w * _nb) { Array.Clear(_pixels, 0, _pixels.Length); _rowMapOk = false; return; }
        BuildRowMap(h);
        for (int c = 0; c < w; c++) RenderColumn(c, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = PicW, h = PicH;
        if (w <= 0 || h <= 0) return;
        EnsureSize(w, h);

        if (_bmp != null)
        {
            var data = _bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            // Row by row: an 8-bit bitmap pads each row to four bytes, so its stride is not always w.
            if (!_horizontal)
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(_pixels, y * w, data.Scan0 + y * data.Stride, w);
            else
            {
                if (!_rowMapOk) RenderLying(w, h);
                // Unroll the ring: oldest column first, so the newest lands at the right edge.
                int head = _colHead;
                for (int y = 0; y < h; y++)
                {
                    var row = data.Scan0 + y * data.Stride;
                    int tail = w - head;
                    System.Runtime.InteropServices.Marshal.Copy(_pixels, y * w + head, row, tail);
                    if (head > 0)
                        System.Runtime.InteropServices.Marshal.Copy(_pixels, y * w, row + tail, head);
                }
            }
            _bmp.UnlockBits(data);
            g.DrawImageUnscaled(_bmp, 0, 0);
        }

        // A line ACROSS the picture at a frequency: vertical standing, horizontal lying down.
        void Line(Pen pen, double hz)
        {
            float p = (float)HzToPos(hz);
            if (_horizontal) g.DrawLine(pen, 0, p, w, p);
            else             g.DrawLine(pen, p, 0, p, h);
        }

        // Strips (Bleeps TX, v1.0.725): see-through, with thin edges - the line and label of a marker stay on top
        if (_bands != null)
            foreach (var (loHz, hiHz, colour) in _bands)
            {
                float a = (float)HzToPos(loHz), b2 = (float)HzToPos(hiHz);
                float lo = Math.Min(a, b2), hi = Math.Max(a, b2);
                using var fill = new SolidBrush(Color.FromArgb(_horizontal ? 40 : 60, colour));
                using var ep = new Pen(Color.FromArgb(200, colour), Pxf(1f));
                if (_horizontal) g.FillRectangle(fill, 0, lo, w, Math.Max(1, hi - lo));
                else             g.FillRectangle(fill, lo, 0, Math.Max(1, hi - lo), h);
                Line(ep, loHz); Line(ep, hiHz);
            }

        // Fixed markers (SSTV): a dashed line and its label at the edge where the frequency scale is not.
        if (_markers != null)
            using (var lf = new Font("Arial", 7.5f, FontStyle.Bold))
                foreach (var (hz, colour, label) in _markers)
                {
                    using var mp = new Pen(Color.FromArgb(220, colour), Pxf(1.5f)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                    Line(mp, hz);
                    float mpos = (float)HzToPos(hz);
                    var sz = g.MeasureString(label, lf);
                    using var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
                    using var tb = new SolidBrush(colour);
                    float lx = _horizontal ? w - sz.Width - Pxf(3) : Math.Min(Math.Max(mpos + Pxf(3), 0), w - sz.Width);
                    float ly = _horizontal ? Math.Min(Math.Max(mpos - sz.Height - Pxf(1), 0), h - sz.Height) : Pxf(2);
                    g.FillRectangle(bg, lx - 1, ly, sz.Width + 2, sz.Height);
                    g.DrawString(label, lf, tb, lx, ly);
                }
        if (_showCursor) {      // the cursor, its pass-band and the split TX tone - not in SSTV (v1.0.612)

        // Pass-band: what the decoder actually listens to. In RTTY there are two of them, one per tone.
        // Lying down it is fainter: it covers the whole history, and there the traces are the point.
        float half = (float)(BandwidthHz / 2);
        using (var band = new SolidBrush(Color.FromArgb(_horizontal ? 36 : 62, 120, 200, 255)))
        // The filter EDGES are bold and bright (PD5DJ): lime, 2 px, opaque - it has to stand out against
        // every palette and not be taken for the red cursor or the yellow RTTY aiming ghost.
        using (var edge = new Pen(Color.FromArgb(255, EdgeColour), Pxf(2f)))
        {
            void Band(double loHz, double hiHz)
            {
                float a = (float)HzToPos(loHz), b = (float)HzToPos(hiHz);
                float lo = Math.Min(a, b), hi = Math.Max(a, b);
                if (_horizontal) g.FillRectangle(band, 0, lo, w, Math.Max(1, hi - lo));
                else             g.FillRectangle(band, lo, 0, Math.Max(1, hi - lo), h);
                Line(edge, loHz);
                Line(edge, hiHz);
            }
            if (PairShiftHz > 0)
            {
                // RTTY: the cursor is the MIDDLE of the pair, so the two pass-bands sit either side of
                // it. That is what makes clicking between the two traces do the obvious thing — and it
                // is why changing the shift opens them symmetrically instead of dragging one tone across
                // the band while the other stays put.
                double hHz = PairShiftHz / 2;
                Band(CursorHz - hHz - half, CursorHz - hHz + half);
                Band(CursorHz + hHz - half, CursorHz + hHz + half);
            }
            else Band(CursorHz - half, CursorHz + half);
        }

        // The width itself, next to the band — it moved off its own slider, so it has to be readable here.
        using (var f  = new Font("Arial", 8f))
        using (var br = new SolidBrush(Color.FromArgb(200, 170, 220, 255)))
        {
            string bw = $"{BandwidthHz:0} Hz";
            var sz = g.MeasureString(bw, f);
            if (_horizontal)
            {
                // Lying down: just under the band, against the right edge where "now" is.
                float by = (float)HzToPos(CursorHz - half) + Pxf(2);
                by = Math.Min(Math.Max(by, Pxf(1)), h - sz.Height - Pxf(1));
                g.DrawString(bw, f, br, w - sz.Width - Pxf(4), by);
            }
            else
            {
                float x1 = (float)HzToPos(CursorHz + half);
                float bx = Math.Min(Math.Max(x1 + Pxf(6), Pxf(2)), w - sz.Width - Pxf(2));
                g.DrawString(bw, f, br, bx, h - sz.Height - Pxf(3));
            }
        }

        // Tuning cursor. In RTTY it marks the MIDDLE of the pair, with the two tones either side of it.
        bool splitShown = TxMarkHz > 0 && PairShiftHz <= 0;
        using (var pen = new Pen(Color.FromArgb(235, splitShown ? RxSplitColour : CentreColour), Pxf(1.5f)))
        {
            if (PairShiftHz > 0)
            {
                // The two tones get the solid lines: those are where the signal has to land. The middle
                // gets a thin dashed one — it is what you aim at, but there is nothing there to hear, and
                // a third solid line reads as a third station.
                double hHz = PairShiftHz / 2;
                Line(pen, CursorHz - hHz);
                Line(pen, CursorHz + hHz);

                using var mid = new Pen(Color.FromArgb(150, CentreColour), Pxf(1f))
                                    { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                Line(mid, CursorHz);
            }
            else Line(pen, CursorHz);
        }

        // The TX tone when it is split off (CW-AFSK): the red line, drawn over the green RX cursor, with a
        // triangle on the edge so it stays findable when the two sit on top of each other.
        if (splitShown)
        {
            var tx = CentreColour;
            using (var txPen = new Pen(Color.FromArgb(235, tx), Pxf(1.5f)))
                Line(txPen, TxMarkHz);
            float tp = (float)HzToPos(TxMarkHz), aw = Pxf(6f), ah = Pxf(9f);
            if (tp >= -aw && tp <= (_horizontal ? h : w) + aw)
            {
                using var txBr = new SolidBrush(Color.FromArgb(240, tx));
                if (_horizontal)
                    g.FillPolygon(txBr, new[] { new PointF(w, tp - aw), new PointF(w, tp + aw), new PointF(w - ah, tp) });
                else
                    g.FillPolygon(txBr, new[] { new PointF(tp - aw, 0), new PointF(tp + aw, 0), new PointF(tp, ah) });
            }
        }
        }
        // No frequency tags on the cursor lines (removed v2.7.235, PD5DJ): they sat on top of the very
        // traces being tuned, and the status line already reports the tuning on every move. The Hz
        // scale gives the reading for anything else.

        // A small triangle on the edge that is not a frequency axis - the top standing, the right lying
        // down (where "now" is) - pointing into the picture.
        void Arrow(Brush br, double hz, float aw, float ah)
        {
            float p = (float)HzToPos(hz);
            if (_horizontal)
                g.FillPolygon(br, new[] { new PointF(w, p - aw), new PointF(w, p + aw), new PointF(w - ah, p) });
            else
                g.FillPolygon(br, new[] { new PointF(p - aw, 0), new PointF(p + aw, 0), new PointF(p, ah) });
        }

        // Where the pair will END UP once a click has tuned the rig: the mark pitch and a shift above
        // it. Red triangles pointing at the two tracks the traces should sit in.
        //
        // Triangles and no lines: a line across the whole picture would run through every trace on the
        // band and read as two more stations. The eye only needs the target, and the traces come to it.
        if (TuneMarkHz > 0 && PairShiftHz > 0)
        {
            using var aim = new SolidBrush(Color.FromArgb(235, 235, 60, 60));
            float aw = Pxf(6f), ah = Pxf(9f);
            int along = _horizontal ? h : w;
            foreach (var thz in new[] { TuneMarkHz, TuneMarkHz + PairShiftHz })
            {
                float tp = (float)HzToPos(thz);
                if (tp < -aw || tp > along + aw) continue;       // off the shown span: draw nothing
                Arrow(aim, thz, aw, ah);
            }
        }

        // The aiming ghost: the pair as it WOULD land, following the mouse. Dashed and yellow so it
        // cannot be mistaken for the committed red pair, with a small arrow marking the middle — the
        // point that is aimed, exactly as the click will take it.
        if (PairShiftHz > 0 && !double.IsNaN(_hoverHz) && !_dragging)
        {
            double gHz = PairShiftHz / 2;
            using (var ghost = new Pen(Color.FromArgb(180, 255, 230, 90), Pxf(1f))
                                   { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            {
                Line(ghost, _hoverHz - gHz);
                Line(ghost, _hoverHz + gHz);
            }
            using (var br = new SolidBrush(Color.FromArgb(220, 255, 230, 90)))
                Arrow(br, _hoverHz, Pxf(5f), Pxf(7f));
        }

        // Hz scale: along the bottom standing, down the right edge lying down.
        using (var axisBg = new SolidBrush(Color.FromArgb(28, 28, 32)))
        {
            if (_horizontal) g.FillRectangle(axisBg, w, 0, AxisW, ClientSize.Height);
            else             g.FillRectangle(axisBg, 0, h, w, AxisH);
        }
        using (var tick = new Pen(Color.FromArgb(120, 120, 130)))
        using (var f = new Font("Arial", 7.5f))
        using (var br = new SolidBrush(Color.FromArgb(190, 190, 200)))
        {
            // Tick and label steps that suit the scale: a zoomed-in view of 200 Hz needs a label every
            // 50, the whole band one every 500 - the same rule for both, from the pixels per hertz.
            double pxPerHz = (_horizontal ? h : w) / VSpan;
            // Standing keeps what it always had: a tick per 100 Hz, a label per 500.
            int major = !_horizontal ? 500 : new[] { 20, 50, 100, 200, 250, 500, 1000 }.FirstOrDefault(v => v * pxPerHz >= Pxf(34), 1000);
            int minor = !_horizontal ? 100 : new[] { 5, 10, 20, 25, 50, 100 }.FirstOrDefault(v => major % v == 0 && v * pxPerHz >= Pxf(5), major);
            for (int hz = (int)Math.Ceiling(VLo / minor) * minor; hz <= VHi; hz += minor)
            {
                float p = (float)HzToPos(hz);
                bool major_ = hz % major == 0;
                var s = hz.ToString();
                if (_horizontal)
                {
                    g.DrawLine(tick, w, p, w + (major_ ? Pxf(7) : Pxf(4)), p);
                    if (major_)
                    {
                        var sz = g.MeasureString(s, f);
                        g.DrawString(s, f, br, w + Pxf(7), Math.Min(Math.Max(p - sz.Height / 2, 0), h - sz.Height));
                    }
                }
                else
                {
                    g.DrawLine(tick, p, h, p, h + (major_ ? Pxf(7) : Pxf(4)));
                    if (major_)
                    {
                        var sz = g.MeasureString(s, f);
                        g.DrawString(s, f, br, Math.Min(Math.Max(p - sz.Width / 2, 0), w - sz.Width), h + Pxf(7));
                    }
                }
            }
        }
    }

    // ── Mouse ──────────────────────────────────────────────────────────────
    // Tuning only. The filter width used to be set by dragging the edges of the shaded band; it has its
    // own slider now, and one number with two ways to change it is one too many. Lying down it is the
    // HEIGHT of a click that tunes: a trace that came in ten seconds ago is on the same frequency as now.

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right && SplitTx)
        {
            _txDragging = true;
            TxMarkHz = Math.Clamp(PosToHz(e.Location), MinHz, MaxHz);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _pressPos = AlongFreq(e.Location);
        _dragged  = false;
        CursorHz  = PosToHz(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_txDragging) { TxMarkHz = Math.Clamp(PosToHz(e.Location), MinHz, MaxHz); return; }
        if (!_dragging)
        {
            // The aiming ghost (DM780 style): in RTTY the pair-to-be follows the mouse, so the two
            // lines are laid over the station's own traces BEFORE the click. Only in pair mode — in
            // CW the single red cursor line says everything already.
            if (PairShiftHz > 0)
            {
                _hoverHz = PosToHz(e.Location);
                Invalidate();
            }
            return;
        }
        // A couple of pixels of wobble while pressing is still a click, not a drag.
        if (Math.Abs(AlongFreq(e.Location) - _pressPos) > 2) _dragged = true;
        _hoverHz = double.NaN;              // while dragging the cursor itself follows the mouse
        CursorHz = PosToHz(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!double.IsNaN(_hoverHz)) { _hoverHz = double.NaN; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_txDragging && e.Button == MouseButtons.Right)
        {
            _txDragging = false;
            TxCommitted?.Invoke(TxMarkHz);
            return;
        }
        if (_dragging && !_dragged && e.Button == MouseButtons.Left) CursorCommitted?.Invoke(_cursorHz);
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_horizontal)
        {
            // Lying down the wheel zooms, around the frequency under the mouse. Up is in.
            double notches = e.Delta / 120.0;
            Zoom(Math.Pow(0.8, notches), PosToHz(e.Location));
            return;
        }
        CursorHz += Math.Sign(e.Delta) * 10;      // standing: 10 Hz per notch
        CursorCommitted?.Invoke(_cursorHz);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bmp?.Dispose();
        base.Dispose(disposing);
    }
}
