namespace ShackAssistant.Helpers.Audio.Bleeps;

/// <summary>Audio by ABSOLUTE sample number (48 kHz since the receiver started); outside [Begin, End) = 0.</summary>
public interface IAudioHistory
{
    long Begin { get; }
    long End { get; }
    float this[long i] { get; }
}

public sealed class ArrayAudio(IReadOnlyList<float> a) : IAudioHistory
{
    public long Begin => 0;
    public long End => a.Count;
    public float this[long i] => i >= 0 && i < a.Count ? a[(int)i] : 0f;
}

/// <summary>
/// One signal at baseband, built as the audio comes in: mixed down around <see cref="CenterHz"/> (the middle of the four
/// tones), filtered and decimated in two stages (48 kHz -> 2 kHz -> 500 Hz). Sample m is audio sample <see cref="Start"/> +
/// 96 m. PLAN-BLEEPS step 3.
/// </summary>
public sealed class BbStream
{
    public const int D1 = 24, D2 = 4, Decim = D1 * D2;                     // 96: 500 Hz, 24 samples per symbol
    public const int Rate = BleepsCode.SampleRate / Decim;
    /// <summary>Samples per symbol at 500 Hz: 24 (Normal), 12 (Fast).</summary>
    public readonly int PerSymbol;
    /// <summary>Cut-off of the second filter: what this decoder still hears around its centre. Normal is +-42 Hz; a
    /// neighbour 100 Hz away starts at +-58. Measured in `bleeps busy`. Fast is twice as wide.</summary>
    public static double CutHz = 60, CutHzFast = 110;
    public readonly BleepsSpeed Speed;
    private const int L1 = 241, L2 = 401;

    public readonly long Start;
    public readonly double CenterHz;
    public readonly List<float> Re = new(), Im = new();
    private readonly List<float> _r1 = new(), _i1 = new();                 // stage 1, 2 kHz
    private static float[]? _h1;
    private static readonly Dictionary<double, float[]> _h2 = new();
    private static readonly object _tables = new();

    public BbStream(long start, double centerHz, BleepsSpeed? speed = null)
    {
        Start = start; CenterHz = centerHz; Speed = speed ?? BleepsSpeed.Normal;
        PerSymbol = Speed.SamplesPerSymbol / Decim;
    }
    public int Length => Re.Count;

    private static float[] Lowpass(int len, double cut, double rate)
    {
        var h = new float[len];
        double sum = 0, wc = 2 * Math.PI * cut / rate;
        for (int i = 0; i < len; i++)
        {
            int m = i - len / 2;
            double sinc = m == 0 ? wc / Math.PI : Math.Sin(wc * m) / (Math.PI * m);
            double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (len - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (len - 1));
            h[i] = (float)(sinc * w); sum += h[i];
        }
        for (int i = 0; i < len; i++) h[i] = (float)(h[i] / sum);
        return h;
    }

    /// <summary>Everything the audio allows now. <paramref name="final"/>: the audio has ended - fill up with silence.</summary>
    public void Extend(IAudioHistory a, bool final = false)
    {
        float[] h1, h2;
        lock (_tables)
        {
            h1 = _h1 ??= Lowpass(L1, 300, BleepsCode.SampleRate);
            double cut = Speed == BleepsSpeed.Fast ? CutHzFast : CutHz;
            if (!_h2.TryGetValue(cut, out h2!)) _h2[cut] = h2 = Lowpass(L2, cut, BleepsCode.SampleRate / (double)D1);
        }
        double w = 2 * Math.PI * CenterHz / BleepsCode.SampleRate;
        double sr = Math.Cos(w), si = -Math.Sin(w);
        long limit = final ? a.End + L1 + (long)L2 * D1 : a.End;
        while (true)
        {
            long c = Start + (long)_r1.Count * D1;
            if (c + L1 / 2 >= limit) break;
            long n0 = c - L1 / 2;
            double ph = -w * n0;
            double pr = Math.Cos(ph), pi = Math.Sin(ph);
            double accR = 0, accI = 0;
            for (int k = 0; k < L1; k++)
            {
                float x = a[n0 + k] * h1[k];
                accR += x * pr; accI += x * pi;
                double t = pr * sr - pi * si; pi = pr * si + pi * sr; pr = t;
            }
            _r1.Add((float)accR); _i1.Add((float)accI);
        }
        while (true)
        {
            int c = Re.Count * D2;
            if (c + L2 / 2 >= _r1.Count) break;
            double ar = 0, ai = 0;
            for (int k = 0; k < L2; k++)
            {
                int j = c - L2 / 2 + k;
                if (j < 0) continue;
                ar += h2[k] * _r1[j]; ai += h2[k] * _i1[j];
            }
            Re.Add((float)ar); Im.Add((float)ai);
        }
    }

    /// <summary>Energy of tone t (shifted df Hz) over the symbol starting at baseband sample s; 0 outside.</summary>
    public float Energy(int s, int tone, double df)
    {
        if (s < 0 || s + PerSymbol > Re.Count) return 0;
        double f = (tone - 1.5) * Speed.ToneSpacing + df;
        double w = -2 * Math.PI * f / Rate;
        double cr = 1, ci = 0, ar = 0, ai = 0, stepR = Math.Cos(w), stepI = Math.Sin(w);
        for (int n = 0; n < PerSymbol; n++)
        {
            float xr = Re[s + n], xi = Im[s + n];
            ar += xr * cr - xi * ci; ai += xr * ci + xi * cr;
            double t = cr * stepR - ci * stepI; ci = cr * stepI + ci * stepR; cr = t;
        }
        return (float)(ar * ar + ai * ai);
    }

    public float[] Energies(int s, double df)
    {
        var e = new float[BleepsCode.Tones];
        for (int t = 0; t < e.Length; t++) e[t] = Energy(s, t, df);
        return e;
    }

    /// <summary>In how many symbols of a pattern the expected tone is the strongest of the four.</summary>
    public int Count(double s, IReadOnlyList<int> pattern, int count, double df, double per, int spacing = 1)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            int at = (int)Math.Round(s + (i / 4 * spacing * 4 + i % 4) * per);
            if (at < 0 || at + PerSymbol > Re.Count) return 0;
            var e = Energies(at, df);
            int x = pattern[i % pattern.Count];
            if (e[x] >= e[0] && e[x] >= e[1] && e[x] >= e[2] && e[x] >= e[3]) n++;
        }
        return n;
    }

    /// <summary>The fraction of energy on the expected tones of a pattern (0.25 = random).</summary>
    public double Fit(double s, IReadOnlyList<int> pattern, int count, double df, double per, int spacing = 1)
    {
        double hit = 0, all = 0;
        for (int i = 0; i < count; i++)
        {
            int at = (int)Math.Round(s + (i / 4 * spacing * 4 + i % 4) * per);
            if (at < 0 || at + PerSymbol > Re.Count) return -1;
            var e = Energies(at, df);
            hit += e[pattern[i % pattern.Count]]; all += e[0] + e[1] + e[2] + e[3];
        }
        return all <= 0 ? 0 : hit / all;
    }
}

/// <summary>
/// The receiver of ONE Bleeps signal, fed as the audio comes in (PLAN-BLEEPS step 3). Gets a rough start and frequency
/// from the search, pins them on the start sync (or, coming in halfway, on three mid syncs), then reads segment by
/// segment: 48 data symbols, then a mid sync (follow clock and frequency) or the end sync. The text is available while it
/// comes in (<see cref="Snapshot"/>), from the bits that can no longer change.
/// </summary>
public sealed class BleepsDecoder
{
    public enum State { Acquiring, Reading, Done, Failed }

    public State Now { get; private set; } = State.Acquiring;
    /// <summary>Came in halfway: no header, the call stays unknown and the text cannot be checked.</summary>
    public bool Late { get; private set; }
    public long StartSample { get; private set; }
    public double FreqHz { get; private set; }
    public bool EndSeen { get; private set; }
    public int DataSymbols => _soft.Count;
    public long LastHeard => _bb.Start + (long)Math.Round(_segStart) * BbStream.Decim;

    private readonly BbStream _bb;
    private readonly int _s0, _ss;
    private readonly double _freqSlack;
    private readonly int _branches, _step;
    private readonly List<float[]> _soft = new();
    private double _per, _df, _segStart;
    private readonly BleepsSpeed _sp;
    public BleepsSpeed Speed => _sp;
    private int _lost, _segs, _confirmed;
    private bool _headerOk;
    private int HeaderSymbols => ((BleepsCode.HeaderBits + BleepsCode.Tail) * 2 + (_branches - 1) * _step * _branches) / 2 + 2;
    private double _sig, _others, _hits; private int _sigN;
    private readonly int _maxSegs = (BleepsCode.MaxTextBytes * 14 * 2 + 400) / 96 + 6;

    /// <param name="approxStart">Audio sample of the first sync symbol (the start sync, or for <paramref name="late"/> the first
    /// of the three mid syncs).</param>
    public BleepsDecoder(long approxStart, double approxF0, bool late = false, double startSlack = 1, double freqSlack = 12,
                         int branches = -1, int step = -1, BleepsSpeed? speed = null)
    {
        _sp = speed ?? BleepsSpeed.Normal;
        if (branches <= 0) branches = _sp.IlBranches;
        if (step <= 0) step = _sp.IlStep;
        Late = late;
        int slack = (int)Math.Ceiling(startSlack * _sp.SamplesPerSymbol);
        long from = Math.Max(0, approxStart - slack);
        _bb = new BbStream(from, approxF0 + 1.5 * _sp.ToneSpacing, _sp);
        _per = _bb.PerSymbol;
        _s0 = (int)((approxStart - from) / BbStream.Decim);
        _ss = (int)Math.Ceiling(startSlack * _bb.PerSymbol);
        _freqSlack = freqSlack; _branches = branches; _step = step;
        StartSample = approxStart; FreqHz = approxF0;
    }

    /// <summary>Takes what the audio has now. <paramref name="final"/> = there will be no more audio.</summary>
    public void Advance(IAudioHistory audio, bool final = false)
    {
        Extend(audio, final);
        Work(final);
    }

    /// <summary>The cheap part, every round: new audio to baseband.</summary>
    public void Extend(IAudioHistory audio, bool final = false)
    {
        if (Now is State.Done or State.Failed) return;
        _bb.Extend(audio, final);
    }

    /// <summary>Whether <see cref="Work"/> has something to do now (acquire, or a whole segment is in) - the heavy part.</summary>
    public bool Ready(bool final)
    {
        if (Now is State.Done or State.Failed) return false;
        if (final) return true;
        if (Now == State.Acquiring) return _bb.Length >= AcquireNeed;
        int ahead = (int)Math.Ceiling((BleepsCode.SyncEvery + 8) * _per) + 8 + _bb.PerSymbol;
        return _segStart + ahead <= _bb.Length;
    }

    /// <summary>The heavy part: acquire and read whole segments, as far as the baseband goes.</summary>
    public void Work(bool final = false)
    {
        if (Now is State.Done or State.Failed) return;
        if (Now == State.Acquiring && !Acquire(final)) return;
        while (Now == State.Reading && Segment(final)) { }
        if (final && Now == State.Reading) Now = State.Done;
    }

    private int AcquireNeed => _s0 + _ss + (Late ? (2 * Span + 4) : 12) * _bb.PerSymbol + 2;

    private const int Span = 52;   // symbols from one mid sync to the next

    private bool Acquire(bool final)
    {
        // the start sync = three Costas back to back; coming in late = three mid syncs, 52 symbols apart
        if (_bb.Length < AcquireNeed) { if (final) Now = State.Failed; return false; }
        int count = 12, spacing = Late ? Span / 4 : 1;
        double best = -1, bestDf = 0; int bestS = _s0;
        for (double df = -_freqSlack; df <= _freqSlack + 1e-9; df += 0.5)
            for (int s = Math.Max(0, _s0 - _ss); s <= _s0 + _ss; s++)
            {
                double fit = _bb.Fit(s, _sp.Costas, count, df, _bb.PerSymbol, spacing);
                if (fit > best) { best = fit; bestS = s; bestDf = df; }
            }
        for (double df = bestDf - 0.5; df <= bestDf + 0.5 + 1e-9; df += 0.125)
        {
            double fit = _bb.Fit(bestS, _sp.Costas, count, df, _bb.PerSymbol, spacing);
            if (fit > best) { best = fit; bestDf = df; }
        }
        // convincing, or no decoder: noise gives ~0.46 at best over this search, a real start at -13 dB ~0.7 with 10-11 (9 of 12 since v1.0.717: two speeds doubled the false starts)
        // of 12 symbols right (a false start used to read a few segments of noise first - most of the CPU in a quiet band)
        if (best < 0.5 || _bb.Count(bestS, _sp.Costas, count, bestDf, _bb.PerSymbol, spacing) < 9) { Now = State.Failed; return false; }
        _df = bestDf;
        StartSample = _bb.Start + (long)bestS * BbStream.Decim;
        FreqHz = _bb.CenterHz + bestDf - 1.5 * _sp.ToneSpacing;
        Measure(bestS, _sp.Costas, 12, _df, spacing);
        _segStart = bestS + (Late ? 4 : 12) * _per;
        Now = State.Reading;
        return true;
    }

    private void Measure(double at, IReadOnlyList<int> pat, int count, double df, int spacing = 1)
    {
        for (int i = 0; i < count; i++)
        {
            int s = (int)Math.Round(at + (i / 4 * spacing * 4 + i % 4) * _per);
            var e = _bb.Energies(s, df);
            int x = pat[i % pat.Count];
            double others = (e[0] + e[1] + e[2] + e[3] - e[x]) / 3;
            _sig += e[x]; _others += others; _sigN++;
        }
    }

    /// <summary>One segment, if the audio for it (and for the search after it) is there. False = wait or stop.</summary>
    private bool Segment(bool final)
    {
        int ahead = (int)Math.Ceiling((BleepsCode.SyncEvery + 8) * _per) + 8 + _bb.PerSymbol;
        if (_segStart + ahead > _bb.Length && !final) return false;
        if (++_segs > _maxSegs) { Now = State.Done; return false; }

        // the end sync: first every place at the expected timing and frequency (cheap), then the best place finer -
        // within one segment the clock and frequency move far less than one step of the fine search (25x less work
        // than searching everything finely: a false start reads a few segments before it gives up)
        double bestEnd = -1, endDdf = 0, endDt = 0; int q = -1;
        for (int k = 1; k <= BleepsCode.SyncEvery; k++)
        {
            double fit = _bb.Fit(_segStart + k * _per, _sp.CostasEnd, 8, _df, _per);
            if (fit > bestEnd) { bestEnd = fit; q = k; }
        }
        if (q > 0 && bestEnd >= 0.3)
        {
            int qc = q;
            for (int k = Math.Max(1, qc - 1); k <= Math.Min(BleepsCode.SyncEvery, qc + 1); k++)
                for (double ddf = -1; ddf <= 1 + 1e-9; ddf += 0.5)
                    for (int dt = -2; dt <= 2; dt++)
                    {
                        double at = _segStart + k * _per + dt * k / (double)BleepsCode.SyncEvery;
                        double fit = _bb.Fit(at, _sp.CostasEnd, 8, _df + ddf, _per);
                        if (fit > bestEnd) { bestEnd = fit; q = k; endDdf = ddf; endDt = dt; }
                    }
        }
        double bestMid = -1, midDdf = 0; int midDt = 0;
        for (double ddf = -1.5; ddf <= 1.5 + 1e-9; ddf += 0.25)
            for (int dt = -4; dt <= 4; dt++)
            {
                double fit = _bb.Fit(_segStart + BleepsCode.SyncEvery * _per + dt, _sp.Costas, 4, _df + ddf, _per);
                if (fit > bestMid) { bestMid = fit; midDdf = ddf; midDt = dt; }
            }
        // End only on clear evidence: reading on past the real end only adds noise AFTER the message (harmless), ending
        // too early loses it. Two segments in a row without a mid sync = the signal is gone.
        bool end = bestMid < 0 || (q > 0 && bestEnd > 0.45 && bestEnd > bestMid + 0.1);
        if (!end && bestMid < 0.35) { if (++_lost >= 2) { Now = State.Done; return false; } } else _lost = 0;
        if (!end && bestMid >= 0.65) _confirmed++;
        if (end && q < 0) { Now = State.Done; return false; }
        int count = end ? q : BleepsCode.SyncEvery;
        double dt2 = end ? endDt * BleepsCode.SyncEvery / (double)Math.Max(1, q) : midDt;
        double ddf2 = end ? endDdf : midDdf;
        for (int k = 0; k < count; k++)
        {
            double frac = (k + 0.5) / BleepsCode.SyncEvery;
            _soft.Add(_bb.Energies((int)Math.Round(_segStart + k * _per + dt2 * frac), _df + ddf2 * frac));
        }
        if (!Late && !_headerOk && _soft.Count >= HeaderSymbols)
        {
            // the header is final once its coded bits and the interleaver behind them are in (138 symbols): a header
            // that fails its CRC is no message - stop now instead of reading noise to the end
            var hb = BleepsCode.SoftBits(_soft.Take(HeaderSymbols).ToList());
            if (BleepsCode.ParseHeader(BleepsCode.Viterbi(BleepsCode.Deinterleave(hb, _branches, _step))).Call == null)
            {
                // No header. But a mid sync + a few data symbols can look like a start sync INSIDE a running message:
                // then this decoder sits on a real signal, on its segment grid (its mid syncs keep coming). That is a
                // late join - go on as one (call '?') instead of stopping. Noise never confirms two mid syncs.
                if (_confirmed >= 2) Late = true;
                else { Now = State.Failed; return false; }
            }
            _headerOk = true;
        }
        if (end)
        {
            Measure(_segStart + q * _per + endDt, _sp.CostasEnd, 8, _df + endDdf);
            EndSeen = true; Now = State.Done;
            return false;
        }
        Measure(_segStart + BleepsCode.SyncEvery * _per + midDt, _sp.Costas, 4, _df + midDdf);
        _per += 0.5 * midDt / (BleepsCode.SyncEvery + 4);
        _df += 0.5 * midDdf;
        _segStart += (BleepsCode.SyncEvery + 4) * _per + midDt;
        FreqHz = _bb.CenterHz + _df - 1.5 * _sp.ToneSpacing;
        return true;
    }

    /// <summary>Mid syncs found clearly (fit >= 0.5) - a late join is shown only after two.</summary>
    public int Confirmed => _confirmed;

    /// <summary>SNR in 2500 Hz from the sync symbols. The GFSK leaks ~0.4 % of a tone into its neighbours; that is
    /// taken off the noise, else a strong signal reads as ~3 dB (measured in step 2).</summary>
    public double SnrDb
    {
        get
        {
            if (_sigN == 0) return -99;
            double sig = _sig / _sigN, oth = _others / _sigN;
            double noise = Math.Max(oth - 0.004 * sig, sig * 1e-4);
            double s = sig - noise;
            return s <= 0 ? -30 : 10 * Math.Log10(s / noise) + 10 * Math.Log10(_sp.Baud / 2500);
        }
    }

    public double Ppm => (_bb.PerSymbol / _per - 1) * 1e6;

    /// <summary>What is known now. While reading, the text holds only the bits that can no longer change.</summary>
    public BleepsCode.Decoded? Snapshot()
    {
        if (_soft.Count == 0) return null;
        double noise = _sigN > 0 ? Math.Max(1e-12, _others / _sigN) : 1;
        var bits = BleepsCode.SoftBits(_soft.Select(e => e.Select(v => (float)(v / noise)).ToArray()).ToList());
        bool done = Now == State.Done;
        if (!done)
        {
            // pad with "don't know" so the deinterleaver releases what it can; keep only stable info bits
            var padded = bits.ToList();
            padded.AddRange(new float[(_branches - 1) * _step * _branches]);
            bits = padded.ToArray();
        }
        var z = BleepsCode.Deinterleave(bits, _branches, _step);
        var u = BleepsCode.Viterbi(z, Late);
        int stable = done ? u.Length : Math.Max(0, (_soft.Count * 2 - (_branches - 1) * _step * _branches) / 2 - 35);
        if (Late)
        {
            var (bytes, after) = BleepsCode.ReadVaricode(u.Take(stable).ToArray(), 40);
            return new BleepsCode.Decoded(null, BleepsCode.Utf8(bytes), false, 0);
        }
        var (call, ver) = BleepsCode.ParseHeader(u);
        if (call == null || stable < BleepsCode.HeaderBits + BleepsCode.Tail) return new BleepsCode.Decoded(null, "", false, 0);
        if (done) return BleepsCode.DecodeFromInfo(u);
        var (b2, _) = BleepsCode.ReadVaricode(u.Take(stable).ToArray(), BleepsCode.HeaderBits + BleepsCode.Tail);
        return new BleepsCode.Decoded(call, BleepsCode.Utf8(b2), false, ver);
    }
}

/// <summary>
/// The whole passband: finds Bleeps signals (a start sync, or three mid syncs of a signal that was already running) and
/// gives each one its own decoder (PLAN-BLEEPS step 3). Since v1.0.717 for BOTH speeds at once (§10): a search per speed,
/// each on its own symbol grid and with its own sync patterns, and every signal gets a decoder of its own speed.
/// Feed it audio as it comes; read <see cref="Messages"/>.
/// </summary>
public sealed class BleepsReceiver : IAudioHistory
{
    /// <summary>A message as the operator sees it, growing while it comes in.</summary>
    public sealed class Message
    {
        public required BleepsDecoder Decoder;
        public string? Call;
        public string Text = "";
        public bool Complete, Finished;
        public bool Shown;                      // passed the bar for being shown at all
        public double FreqHz => Decoder.FreqHz;
        public double SnrDb => Decoder.SnrDb;
        public long StartSample => Decoder.StartSample;
        public bool Late => Decoder.Late;
        public BleepsSpeed Speed => Decoder.Speed;
        /// <summary>The middle of the four tones.</summary>
        public double CenterHz => Decoder.FreqHz + Decoder.Speed.CenterOffset;
    }

    public double MinHz = 150, MaxHz = 2950;
    /// <summary>Our own call (v1.0.719, full duplex): our own signal heard back (QO-100) never makes a place "taken" for
    /// Free spot / Auto free spot - otherwise the next message would jump away from where we just were.</summary>
    public volatile string? OwnCall;
    /// <summary>Decoder threads (PA5DJ: "like WSJT-X"/JTDX): 0 = Auto = cores - 1 (one stays free for the waterfall and the
    /// rest of the program), else that many. The decoders are independent, so the result is the same either way
    /// (measured in `bleeps busy threads`).</summary>
    public int Threads = 0;
    /// <summary>Where the time goes (wall seconds): spectrogram, search, decoders.</summary>
    public double FrameSeconds, SearchSeconds, DecodeSeconds;
    public int ThreadsInUse => Threads > 0 ? Threads : Math.Max(1, Environment.ProcessorCount - 1);
    /// <summary>Fraction of the energy on the Costas tones over 12 symbols (0.25 = noise) for a candidate.</summary>
    public static double Threshold = 0.45;
    /// <summary>Which speeds are searched (a harness may switch one off).</summary>
    public BleepsSpeed[] Speeds = BleepsSpeed.All;

    /// <summary>The spectrogram and the search of ONE speed: frames of one symbol, a quarter symbol apart, zero-padded so
    /// a tone step is ~3.6 bins at either speed.</summary>
    private sealed class Search
    {
        public required BleepsSpeed Sp;
        public int Fn => Sp.SamplesPerSymbol;
        public int Hop => Fn / 4;
        public int Nfft => Fn > 2048 ? 8192 : 4096;
        public double BinHz => (double)BleepsCode.SampleRate / Nfft;
        public readonly Dictionary<long, float[]> Frames = new();
        public long NextFrame, SearchedStart, SearchedLate;
    }
    private readonly Search[] _search;

    private readonly float[] _ring;
    private long _end;
    private readonly List<Message> _all = new();

    public BleepsReceiver(double seconds = 60)
    {
        _ring = new float[(int)(seconds * BleepsCode.SampleRate)];
        _search = BleepsSpeed.All.Select(s => new Search { Sp = s }).ToArray();
    }

    public long Begin => Math.Max(0, _end - _ring.Length);
    public long End => _end;
    public float this[long i] => i >= Begin && i < _end ? _ring[i % _ring.Length] : 0f;

    /// <summary>Every message seen (shown or not); <see cref="Message.Shown"/> says which the operator gets.</summary>
    public IReadOnlyList<Message> Messages => _all;

    public void Feed(IReadOnlyList<float> chunk)
    {
        foreach (var x in chunk) { _ring[_end % _ring.Length] = x; _end++; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var se in _search)
        {
            if (!Speeds.Contains(se.Sp)) continue;
            // two frames per FFT (one in the real part, one in the imaginary part): half the FFT work (v1.0.717)
            while ((se.NextFrame + 2) * se.Hop + se.Fn <= _end)
            {
                var (a, b) = FramePair(se, se.NextFrame);
                foreach (var fr in new[] { a, b })
                {
                    se.Frames[se.NextFrame++] = fr;
                    // the slow average for Free spot (~30 s), from the finer (Normal) spectrogram
                    if (se.Sp == BleepsSpeed.Normal)
                    {
                        if (_avg == null || _avg.Length != fr.Length) _avg = (float[])fr.Clone();
                        else for (int i = 0; i < fr.Length; i++) _avg[i] += (fr[i] - _avg[i]) * AvgAlpha;
                    }
                }
            }
        }
        FrameSeconds += sw.Elapsed.TotalSeconds; sw.Restart();
        foreach (var se in _search) if (Speeds.Contains(se.Sp)) SearchOne(se);
        SearchSeconds += sw.Elapsed.TotalSeconds; sw.Restart();
        StepAll(false);
        DecodeSeconds += sw.Elapsed.TotalSeconds;
        // forget frames older than the late-join search can use (3 mid syncs = 156 symbols)
        foreach (var se in _search)
        {
            long old = se.NextFrame - 4 * (2 * 52 + 20) - 50;
            foreach (var k in se.Frames.Keys.Where(k => k < old).ToList()) se.Frames.Remove(k);
        }
    }

    /// <summary>The audio has ended (a file): read what is left.</summary>
    public void Flush() => StepAll(true);

    private void StepAll(bool final)
    {
        var live = _all.Where(m => !m.Finished).ToList();
        // the cheap part for all, on this thread
        foreach (var m in live) m.Decoder.Extend(this, final);
        // the heavy part over the threads - only when at least two have it: spreading little work costs more CPU (and
        // energy) than it saves (measured: 15 threads on small steps used 3x the CPU of one)
        var heavy = live.Where(m => m.Decoder.Ready(final)).ToList();
        int n = ThreadsInUse;
        if (n <= 1 || heavy.Count < 2) foreach (var m in heavy) Work(m, final);
        else Parallel.ForEach(heavy, new ParallelOptions { MaxDegreeOfParallelism = n }, m => Work(m, final));
        // then, in a fixed order, what is shown - never dependent on which thread finished first
        foreach (var m in live) Judge(m);
    }

    private static void Work(Message m, bool final)
    {
        int had = m.Decoder.DataSymbols;
        m.Decoder.Work(final);
        bool over = m.Decoder.Now is BleepsDecoder.State.Done or BleepsDecoder.State.Failed;
        if (m.Decoder.DataSymbols == had && !over) return;          // nothing new: no Viterbi for nothing
        var d = m.Decoder.Snapshot();
        if (d != null) { m.Call = d.Call; m.Text = d.Text; m.Complete = d.Complete; }
    }

    private void Judge(Message m)
    {
        // shown: a header that passed its CRC; a late join after three clear mid syncs and readable text, and never a
        // signal that a decoder with a call already reads
        if (!m.Shown && m.Late && Covered(m)) { m.Finished = true; return; }
        if (!m.Shown) m.Shown = m.Late ? m.Decoder.Confirmed >= 3 && Readable(m.Text) : m.Call != null;
        if (m.Decoder.Now is BleepsDecoder.State.Done or BleepsDecoder.State.Failed) m.Finished = true;
    }

    /// <summary>Two signals overlap in frequency: their middles closer than a quarter of their widths added (Normal next
    /// to Normal: ~42 Hz, as before; Fast next to Fast: ~83 Hz).</summary>
    private static bool Overlap(double c1, BleepsSpeed s1, double c2, BleepsSpeed s2) => Math.Abs(c1 - c2) <= (s1.WidthHz + s2.WidthHz) / 4;

    /// <summary>A late join (or a false start that turned into one) on a signal that a decoder WITH a call already
    /// reads: same place, overlapping in time. Never shown twice.</summary>
    private bool Covered(Message late)
    {
        foreach (var o in _all)
            if (!ReferenceEquals(o, late) && !o.Late && o.Call != null && Overlap(o.CenterHz, o.Speed, late.CenterHz, late.Speed)
                && o.StartSample <= late.Decoder.LastHeard && o.Decoder.LastHeard + 60L * o.Speed.SamplesPerSymbol >= late.StartSample)
                return true;
        return false;
    }

    /// <summary>Text from a decoder that came in halfway has no CRC to vouch for it: show it only when it reads like
    /// text - at least 8 characters, hardly any broken UTF-8 or control characters.</summary>
    public static bool Readable(string s)
    {
        if (s.Length < 8) return false;
        int badc = s.Count(c => c == '�' || (char.IsControl(c) && c != '\n' && c != '\t'));
        return badc * 10 <= s.Length;
    }

    /// <summary>Frames f and f+1 from one complex FFT: x in the real part, y in the imaginary part;
    /// X[k] = (Z[k] + conj Z[N-k]) / 2, Y[k] = (Z[k] - conj Z[N-k]) / 2i - only the powers are kept.</summary>
    private (float[] A, float[] B) FramePair(Search se, long f)
    {
        int nfft = se.Nfft;
        var re = new float[nfft]; var im = new float[nfft];
        long s0 = f * se.Hop, s1 = (f + 1) * se.Hop;
        long lo0 = Begin;
        for (int i = 0; i < se.Fn; i++) { re[i] = this[s0 + i]; im[i] = this[s1 + i]; }
        Fft.Forward(re, im);
        int lo = (int)(MinHz / se.BinHz), hi = (int)((MaxHz + 4 * se.Sp.ToneSpacing) / se.BinHz) + 8;
        var pa = new float[hi + 1]; var pb = new float[hi + 1];
        for (int k = lo; k <= hi && k < nfft / 2; k++)
        {
            int m = (nfft - k) % nfft;
            float zr = re[k], zi = im[k], cr = re[m], ci = -im[m];          // Z[k], conj Z[N-k]
            float xr = (zr + cr) / 2, xi = (zi + ci) / 2;                     // X[k]
            float yr = (zi - ci) / 2, yi = -(zr - cr) / 2;                    // Y[k] = (Z - conj)/2i
            pa[k] = xr * xr + xi * xi; pb[k] = yr * yr + yi * yi;
        }
        return (pa, pb);
    }

    private float[] Frame(Search se, long f)
    {
        int nfft = se.Nfft;
        var re = new float[nfft]; var im = new float[nfft];
        long s = f * se.Hop;
        for (int i = 0; i < se.Fn; i++) re[i] = this[s + i];
        Fft.Forward(re, im);
        int lo = (int)(MinHz / se.BinHz), hi = (int)((MaxHz + 4 * se.Sp.ToneSpacing) / se.BinHz) + 8;
        var p = new float[hi + 1];
        for (int b = lo; b <= hi && b < nfft / 2; b++) p[b] = re[b] * re[b] + im[b] * im[b];
        return p;
    }

    /// <summary>Score of a pattern of 12 sync symbols starting at frame f, tone 0 at bin b: the fraction of the energy on
    /// the expected tones (0.25 = noise), and in how many of the 12 symbols the expected tone is the strongest of the four.
    /// The count is what keeps random data out: a strong signal's own data matches a Costas array on a few symbols by
    /// chance and then the FRACTION alone runs high (measured: 17 false starts in 30 s next to one signal).</summary>
    private static (double Fraction, int Count) Score(Search se, long f, int b, int spacing, double tonesPerBin)
    {
        double hit = 0, all = 0; int count = 0;
        Span<float> v = stackalloc float[4];
        var costas = se.Sp.Costas;
        for (int i = 0; i < 12; i++)
        {
            long fr = f + 4L * (i / 4 * spacing * 4 + i % 4);
            if (!se.Frames.TryGetValue(fr, out var p)) return (0, 0);
            int x = costas[i % 4];
            for (int t = 0; t < 4; t++)
            {
                int bin = b + (int)Math.Round(t * tonesPerBin);
                v[t] = (bin < p.Length ? p[bin] : 0) + (bin + 1 < p.Length ? p[bin + 1] : 0);
                all += v[t];
            }
            hit += v[x];
            if (v[x] >= v[0] && v[x] >= v[1] && v[x] >= v[2] && v[x] >= v[3]) count++;
        }
        return (all <= 0 ? 0 : hit / all, count);
    }

    private void SearchOne(Search se)
    {
        double tpb = se.Sp.ToneSpacing / se.BinHz;                // bins per tone step (~3.6 at both speeds)
        int lo = (int)(MinHz / se.BinHz), hi = (int)(MaxHz / se.BinHz);
        int threads = ThreadsInUse;
        foreach (var (late, spacing, span) in new[] { (false, 1, 12), (true, Span / 4, 2 * Span + 4) })
        {
            long last = se.NextFrame - 4L * span - 1;             // the newest start whose 12 symbols are all in
            long first = Math.Max(late ? se.SearchedLate : se.SearchedStart, last - 120);
            if (last < first) continue;
            // every (frame, bin) over the threshold - the bins are independent, so this part goes over the threads
            var hits = new System.Collections.Concurrent.ConcurrentBag<(long F, int B, double Sc)>();
            void Scan(int b0, int b1)
            {
                for (long f = Math.Max(0, first); f <= last; f++)
                    for (int b = b0; b < b1; b++)
                    {
                        if (!Promising(se, f, b, tpb)) continue;
                        var (sc, cnt) = Score(se, f, b, spacing, tpb);
                        if (sc >= Threshold && cnt >= (late ? 10 : 9)) hits.Add((f, b, sc));
                    }
            }
            if (threads <= 1 || last - first < 40) Scan(lo, hi + 1);     // a few frames (live): not worth spreading
            else
            {
                int chunk = Math.Max(16, (hi - lo + 1) / threads + 1);
                Parallel.For(0, (hi - lo + 1 + chunk - 1) / chunk, new ParallelOptions { MaxDegreeOfParallelism = threads },
                    k => Scan(lo + k * chunk, Math.Min(hi + 1, lo + (k + 1) * chunk)));
            }
            // then, in a fixed order (the same result with 1 or N threads): local maxima, not already covered
            foreach (var (f, b, sc) in hits.OrderBy(h => h.F).ThenBy(h => h.B))
            {
                bool max = true;
                for (long g = f - 2; g <= f + 2 && max; g++)
                    for (int c = b - 1; c <= b + 1 && max; c++)
                        if ((g != f || c != b) && Score(se, g, c, spacing, tpb).Fraction > sc) max = false;
                if (!max) continue;
                long start = f * se.Hop;
                double f0 = b * se.BinHz + se.BinHz / 2;
                if (Busy(se.Sp, f0, start, late)) continue;
                // the search knows the place to a quarter symbol (hop) and half a bin: the decoder only refines
                lock (_sync) _all.Add(new Message { Decoder = new BleepsDecoder(start, f0, late, 0.25, Math.Max(4, se.BinHz * 0.7), speed: se.Sp) });
            }
            if (late) se.SearchedLate = last + 1; else se.SearchedStart = last + 1;
        }
    }

    /// <summary>A quick look at the first 4 sync symbols before the full 12: most (frame, bin) pairs end here.</summary>
    private static bool Promising(Search se, long f, int b, double tpb)
    {
        double hit = 0, all = 0; int count = 0;
        var costas = se.Sp.Costas;
        for (int i = 0; i < 4; i++)
        {
            if (!se.Frames.TryGetValue(f + 4L * i, out var p)) return false;
            int x = costas[i];
            double vx = 0, vmax = 0;
            for (int t = 0; t < 4; t++)
            {
                int bin = b + (int)Math.Round(t * tpb);
                double v = (bin < p.Length ? p[bin] : 0) + (bin + 1 < p.Length ? p[bin + 1] : 0);
                all += v; if (t == x) vx = v; if (v > vmax) vmax = v;
            }
            hit += vx; if (vx >= vmax) count++;
        }
        return all > 0 && hit / all >= 0.35 && count >= 2;
    }

    private const int Span = 52;

    /// <summary>A decoder already covers this place: overlapping in frequency (by both widths) and (for a late join) any
    /// time while it lives or since it started; a new start sync is only blocked by one that is still reading.</summary>
    private bool Busy(BleepsSpeed sp, double f0, long start, bool late)
    {
        double c = f0 + sp.CenterOffset;
        foreach (var m in _all)
        {
            // the same candidate again (overlapping search windows), at the same speed
            if (m.Speed == sp && Math.Abs(m.CenterHz - c) < 10 && Math.Abs(start - m.StartSample) < sp.SamplesPerSymbol && m.Late == late) return true;
            if (!Overlap(m.CenterHz, m.Speed, c, sp)) continue;
            // only a decoder that has PROVEN itself holds the place - an unproven one (maybe a false start) never blocks
            // the real start sync (measured: a false late join at the same frequency hid the real message)
            bool proven = m.Late ? m.Shown : m.Call != null;
            if (!m.Finished && (proven || late)) return true;   // a late join never competes with a running decoder
            if (!m.Finished) continue;
            int sps = m.Speed.SamplesPerSymbol;
            if (late && start >= m.StartSample - 2L * sps && start <= m.Decoder.LastHeard + 60L * sps) return true;
            if (!late && m.Speed == sp && Math.Abs(start - m.StartSample) < sps) return true;
        }
        return false;
    }

    private float[]? _avg;
    /// <summary>The list of messages changes on the receiving thread; Free spot reads it from the window.</summary>
    private readonly object _sync = new();
    private const float AvgAlpha = 1f / (83 * 30);
    private double AvgBinHz => _search[0].BinHz;

    /// <summary>
    /// Free spot (PLAN-BLEEPS §6): tone 0 of the quietest place between <paramref name="lo"/> and <paramref name="hi"/> for
    /// a signal of speed <paramref name="sp"/>, averaged over ~30 s - the signal's own band counts fully, and a strong
    /// station up to 150 Hz beyond its edges counts too (measured in step 3: +20 dB at 120-150 Hz is fine, +30 dB is not).
    /// Never overlapping a station that is being read (with room). Null while too little has been heard.
    /// </summary>
    public double? FreeSpot(double lo = 400, double hi = 2500, BleepsSpeed? sp = null) => Spot(lo, hi, sp, null);

    /// <summary>
    /// Auto free spot (PA5DJ, 16 November 2026: "as soon as you click Send the program quickly picks an empty frequency, and then
    /// transmits"): the place to send at now. The CURRENT place stays as long as it is still free - nobody being read there
    /// and hardly busier than the quietest place - so a chat partner does not have to look for you at every message.
    /// Otherwise the quietest place. Null while too little has been heard (then the current place is used).
    /// </summary>
    /// <param name="oursThere">We sent at <paramref name="current"/> just now (full duplex): the energy there is our OWN signal
    /// heard back, so only another station being read there makes it taken (v1.0.719 - measured: it jumped to 400 Hz).</param>
    public double? AutoSpot(double current, BleepsSpeed sp, double lo = 400, double hi = 2500, bool oursThere = false) => Spot(lo, hi, sp, current, oursThere);

    private double? Spot(double lo, double hi, BleepsSpeed? sp, double? current, bool oursThere = false)
    {
        sp ??= BleepsSpeed.Normal;
        var avg = _avg;
        if (avg == null || _search[0].NextFrame < 83 * 3) return null;
        double bin = AvgBinHz;
        double Sum(double a, double b)
        {
            int i0 = Math.Max(0, (int)(a / bin)), i1 = Math.Min(avg.Length - 1, (int)(b / bin));
            double s = 0; for (int i = i0; i <= i1; i++) s += avg[i]; return s;
        }
        List<(double C, BleepsSpeed S)> busy;
        var own = OwnCall;
        lock (_sync) busy = _all.Where(m => !m.Finished && m.Shown && (own == null || !string.Equals(m.Call, own, StringComparison.OrdinalIgnoreCase)))
                                .Select(m => (m.CenterHz, m.Speed)).ToList();
        double edge = sp.WidthHz / 2 + 20;                     // our half width + a little
        bool Taken(double c) => busy.Any(b => Math.Abs(b.C - c) < (b.S.WidthHz + sp.WidthHz) / 2 + 30);
        // the energy there, and a little for being far from the middle: in a quiet band every place is as good, and then the
        // middle wins - the edges of the passband sit in the rig's SSB filter (Auto first picked 300 Hz, measured)
        double Score(double c) => (Sum(c - edge, c + edge) + 0.1 * Sum(c - edge - 150, c + edge + 150)) * (1 + Math.Abs(c - 1500) / 5000);
        double best = double.MaxValue, at = double.NaN;
        for (double f = lo; f + 3 * sp.ToneSpacing <= hi; f += 5)
        {
            double c = f + sp.CenterOffset;
            if (Taken(c)) continue;
            double score = Score(c);
            if (score < best) { best = score; at = f; }
        }
        if (double.IsNaN(at)) return null;
        // stay where we are while that is still free: not taken, and at most twice as busy as the quietest place
        if (current is { } cur && cur >= lo && cur + 3 * sp.ToneSpacing <= hi)
        {
            double cc = cur + sp.CenterOffset;
            if (!Taken(cc) && (oursThere || Score(cc) <= 2 * best + 1e-9)) return cur;
        }
        return at;
    }

    /// <summary>Messages that are done and older than <paramref name="seconds"/> leave the list.</summary>
    public void Forget(double seconds)
    {
        long cut = _end - (long)(seconds * BleepsCode.SampleRate);
        lock (_sync) _all.RemoveAll(m => m.Finished && m.Decoder.LastHeard < cut);
    }
}
