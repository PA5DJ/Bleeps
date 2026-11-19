namespace ShackAssistant.Helpers.Audio;

/// <summary>
/// Turns the raw sample stream into spectrum frames for the waterfall, and keeps the recent audio in a
/// ring buffer so other consumers (a decoder, a level meter) can read the same samples without a second
/// capture. Display-agnostic on purpose: it hands out dB per bin and the bin↔Hz conversions, and knows
/// nothing about how any of that is drawn.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public int SampleRate { get; }

    /// <summary>Samples of audio that go into one frame — the TIME resolution, and the only thing that
    /// decides whether two dits can be told apart.</summary>
    public int FftSize    { get; }

    /// <summary>Length actually transformed: the window, zero-filled to this many points.</summary>
    public int TransformSize { get; }

    /// <summary>Frequency step between two bins — of the padded transform, which is what the display
    /// gets.</summary>
    public double HzPerBin => (double)SampleRate / TransformSize;

    /// <summary>Usable bins: 0 .. Nyquist.</summary>
    public int BinCount => TransformSize / 2;

    private readonly float[] _ring;
    private int   _write;
    private long  _total;
    private readonly object _lock = new();

    private readonly float[] _window;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly float[] _db;

    /// <summary>
    /// <paramref name="fftSize"/> is the window in samples: 4096 at 48 kHz is 85 ms, comfortably shorter
    /// than a 60 ms dit at 20 WPM, and that is what fixes it.
    ///
    /// <paramref name="padFactor"/> then transforms that same window in a buffer this many times longer,
    /// the rest filled with zeros. It adds no information — two tones 5 Hz apart still cannot be told
    /// apart — but it INTERPOLATES the spectrum, and that is exactly what a waterfall needs. Without it
    /// one bin is 11.7 Hz, which on a 1900 px display of 0-3000 Hz is a 7 px block per bin: a carrier
    /// comes out as a fat rectangle. At four times padded it is 2.9 Hz and under 2 px, so the same line
    /// is drawn as the narrow peak it really is. Raising the window instead would give real resolution
    /// but a 341 ms window, which smears one dit into the next.
    /// </summary>
    public SpectrumAnalyzer(int sampleRate, int fftSize = 4096, int padFactor = 4, int historySeconds = 10)
    {
        SampleRate    = sampleRate;
        FftSize       = fftSize;
        TransformSize = fftSize * Math.Max(1, padFactor);
        // Ten seconds of history, not one. It costs under two megabytes and it is what lets a decoder be
        // handed the audio that has ALREADY gone by when the operator clicks on a signal: text appears at
        // once instead of after the second or so it takes to learn the speed from scratch.
        _ring         = new float[Math.Max(fftSize * 4, sampleRate * Math.Max(1, historySeconds))];
        _window       = Fft.BlackmanHarrisWindow(fftSize);
        _re = new float[TransformSize];
        _im = new float[TransformSize];
        _db = new float[TransformSize / 2];
    }

    /// <summary>Feed captured samples. Safe to call from the capture thread.</summary>
    public void Add(float[] samples)
    {
        lock (_lock)
        {
            foreach (var s in samples)
            {
                _ring[_write] = s;
                _write = (_write + 1) % _ring.Length;
            }
            _total += samples.Length;
        }
    }

    /// <summary>True once enough audio has arrived for a full transform.</summary>
    public bool HasFrame => _total >= FftSize;

    /// <summary>
    /// Spectrum of the most recent <see cref="FftSize"/> samples, in dB per bin. The returned array is
    /// reused between calls — copy it if you need to keep it.
    /// </summary>
    public float[] Frame()
    {
        lock (_lock)
        {
            int start = _write - FftSize;
            if (start < 0) start += _ring.Length;
            // Take the offset out first. A sound card that sits a fraction away from zero puts all of
            // that in bin 0, and the window smears it over the first few bins, so the waterfall shows a
            // permanent stripe at 0 Hz that looks like a station and never goes away. Measured on a real
            // recording (offset -65 dBFS): bin 0 drops 13 dB and bin 1 by 12, while nothing above 35 Hz
            // moves at all. Cheap, and it can only remove something that carries no signal anyway.
            double dc = 0;
            for (int i = 0; i < FftSize; i++) dc += _ring[(start + i) % _ring.Length];
            float mean = (float)(dc / FftSize);

            for (int i = 0; i < FftSize; i++)
            {
                _re[i] = (_ring[(start + i) % _ring.Length] - mean) * _window[i];
                _im[i] = 0f;
            }
            // The padding has to be cleared every time: the previous frame left its transform in here.
            Array.Clear(_re, FftSize, TransformSize - FftSize);
            Array.Clear(_im, FftSize, TransformSize - FftSize);
        }
        Fft.Forward(_re, _im);
        // Normalised by the WINDOW length, not the padded length: the zeros carry no signal, so scaling
        // by them would drop every level by 20*log10(padFactor) for no reason.
        for (int b = 0; b < _db.Length; b++) _db[b] = Fft.MagnitudeDb(_re[b], _im[b], FftSize);
        return _db;
    }

    /// <summary>Peak level (dB) of the newest samples — drives the level meter next to the threshold.</summary>
    public float PeakDb(int samples = 2048)
    {
        float peak = 0f;
        lock (_lock)
        {
            int n = Math.Min(samples, _ring.Length);
            int start = _write - n;
            if (start < 0) start += _ring.Length;
            for (int i = 0; i < n; i++)
            {
                float v = Math.Abs(_ring[(start + i) % _ring.Length]);
                if (v > peak) peak = v;
            }
        }
        return (float)(20.0 * Math.Log10(Math.Max(peak, 1e-7)));
    }

    /// <summary>How many samples of history are really available — less than the ring while it fills.</summary>
    public int Available => (int)Math.Min(_total, _ring.Length);

    /// <summary>Copy the most recent <paramref name="count"/> samples — the hand-off point for a decoder.</summary>
    public float[] Recent(int count)
    {
        var outp = new float[count];
        lock (_lock)
        {
            int start = _write - count;
            if (start < 0) start += _ring.Length;
            for (int i = 0; i < count; i++) outp[i] = _ring[(start + i) % _ring.Length];
        }
        return outp;
    }

    public double BinToHz(int bin) => bin * HzPerBin;
    public int    HzToBin(double hz) => (int)Math.Round(hz / HzPerBin);

    /// <summary>Samples received since the start - the clock a <see cref="SpectrumTap"/> steps along.</summary>
    public long Total { get { lock (_lock) return _total; } }

    /// <summary>
    /// Copy the <paramref name="dest"/>.Length samples that END at sample number <paramref name="endTotal"/>.
    /// False when that stretch is not (or no longer) in the ring.
    /// </summary>
    internal bool CopyEnding(long endTotal, float[] dest)
    {
        lock (_lock)
        {
            int n = dest.Length;
            if (endTotal > _total || endTotal - n < _total - _ring.Length || endTotal - n < 0) return false;
            long back = _total - endTotal;                       // how far behind the newest sample it ends
            int start = (int)(((_write - back - n) % _ring.Length + _ring.Length) % _ring.Length);
            for (int i = 0; i < n; i++) dest[i] = _ring[(start + i) % _ring.Length];
            return true;
        }
    }
}

/// <summary>
/// A second, SHORT look at the same audio: its own window length, taken at any moment still in the ring
/// rather than only at "now".
///
/// It exists for the horizontal waterfall. That one draws a column per 10 ms and wants a window about as
/// long as a CW element (~43 ms), where the main analyser uses up to 341 ms for a thin trace. The two are
/// different questions, so they are two taps on one ring rather than one setting that has to lose.
/// <see cref="SpectrumAnalyzer.Frame"/> only ever looks at the newest samples; with ten columns per UI
/// tick each column needs a window that ends at ITS OWN moment, which is what <see cref="FrameEndingAt"/>
/// gives.
/// </summary>
public sealed class SpectrumTap
{
    private readonly SpectrumAnalyzer _src;
    private readonly int _n, _size;
    private readonly float[] _raw, _window, _re, _im, _db;

    public double HzPerBin { get; }

    public SpectrumTap(SpectrumAnalyzer src, int fftSize, int padFactor)
    {
        _src     = src;
        _n       = fftSize;
        _size    = fftSize * Math.Max(1, padFactor);
        _raw     = new float[fftSize];
        _window  = Fft.BlackmanHarrisWindow(fftSize);
        _re      = new float[_size];
        _im      = new float[_size];
        _db      = new float[_size / 2];
        HzPerBin = (double)src.SampleRate / _size;
    }

    public int HzToBin(double hz) => (int)Math.Round(hz / HzPerBin);

    /// <summary>The last bin worth turning into dB; the ones above it are left as they were. The waterfall
    /// shows up to 3 kHz of a 24 kHz spectrum, and the logarithm per bin is most of the cost - measured,
    /// half of a column at 8x padding. 0 = all of them.</summary>
    public int LastBin { get; set; }

    /// <summary>dB per bin of the window ending at sample <paramref name="endTotal"/>, or null when that
    /// audio is not in the ring. The array is REUSED - copy it to keep it.</summary>
    public float[]? FrameEndingAt(long endTotal)
    {
        if (!_src.CopyEnding(endTotal, _raw)) return null;
        double dc = 0;
        for (int i = 0; i < _n; i++) dc += _raw[i];
        float mean = (float)(dc / _n);
        for (int i = 0; i < _n; i++) { _re[i] = (_raw[i] - mean) * _window[i]; _im[i] = 0f; }
        Array.Clear(_re, _n, _size - _n);
        Array.Clear(_im, _n, _size - _n);
        Fft.Forward(_re, _im);
        int top = LastBin > 0 ? Math.Min(_db.Length, LastBin + 1) : _db.Length;
        for (int b = 0; b < top; b++) _db[b] = Fft.MagnitudeDb(_re[b], _im[b], _n);
        return _db;
    }
}
