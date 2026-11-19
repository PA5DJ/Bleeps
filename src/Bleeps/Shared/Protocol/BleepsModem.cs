namespace ShackAssistant.Helpers.Audio.Bleeps;

/// <summary>
/// Bleeps on audio (PLAN-BLEEPS step 2): the GFSK modulator and the receiver for ONE signal whose start and frequency are
/// roughly known (the search over the whole passband - step 3 - hands those over). Public protocol (guides/bleeps.html#spec).
/// </summary>
public static class BleepsModem
{
    public const int N = BleepsCode.SamplesPerSymbol;          // 2304 at 48 kHz
    public const double Bt = 1.0;                              // Gaussian frequency pulse, as FT4

    // ═════════════════════════════════════ modulator ═════════════════════════════════════

    /// <summary>The frequency pulse: how much of a symbol's tone is in force at time t (in symbols, 0 = the middle of the
    /// symbol). The shifted pulses add up to exactly 1, so between two equal tones the frequency is that tone.</summary>
    public static double Pulse(double t)
    {
        double c = Math.PI * Math.Sqrt(2 / Math.Log(2)) * Bt;
        return 0.5 * (Erf(c * (t + 0.5)) - Erf(c * (t - 0.5)));
    }

    /// <summary>Abramowitz-Stegun 7.1.26 (error below 1.5e-7) - enough for a frequency pulse.</summary>
    public static double Erf(double x)
    {
        double s = Math.Sign(x); x = Math.Abs(x);
        double t = 1 / (1 + 0.3275911 * x);
        double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return s * y;
    }

    // the pulse sampled over 3 symbols, index 0 = 1.5 symbols before the middle - one table per symbol length (speed)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, double[]> _pulses = new();

    private static double[] PulseTable(int n) => _pulses.GetOrAdd(n, k =>
    {
        var p = new double[3 * k];
        for (int i = 0; i < p.Length; i++) p[i] = Pulse((i + 0.5) / k - 1.5);
        return p;
    });

    /// <summary>The instantaneous frequency (Hz) per sample, the first and last tone held through the half-symbol ramps.</summary>
    public static double[] Frequencies(IReadOnlyList<int> tones, double f0) => Frequencies(tones, f0, BleepsSpeed.Normal);

    public static double[] Frequencies(IReadOnlyList<int> tones, double f0, BleepsSpeed sp)
    {
        int N = sp.SamplesPerSymbol;
        var p = PulseTable(N);
        int L = tones.Count, total = (L + 1) * N;          // half a symbol of ramp at each end
        var f = new double[total];
        for (int n = 0; n < total; n++)
        {
            double t = (n + 0.5) / N - 0.5;                // in symbols from the start of symbol 0
            int k0 = (int)Math.Floor(t) - 1;
            double acc = 0;
            for (int k = k0; k <= k0 + 3; k++)
            {
                int i = (int)Math.Floor((t - (k + 0.5) + 1.5) * N);
                if (i < 0 || i >= p.Length) continue;
                int tone = tones[Math.Clamp(k, 0, L - 1)];
                acc += tone * p[i];
            }
            f[n] = f0 + acc * sp.ToneSpacing;
        }
        return f;
    }

    /// <summary>The message as audio at 48 kHz, peak <paramref name="amp"/>, starting with half a symbol of ramp; the first
    /// symbol starts N/2 samples in.</summary>
    public static float[] Modulate(IReadOnlyList<int> tones, double f0, double amp = 0.9) => Modulate(tones, f0, amp, BleepsSpeed.Normal);

    public static float[] Modulate(IReadOnlyList<int> tones, double f0, double amp, BleepsSpeed sp)
    {
        var (re, _) = ModulateIq(tones, f0, amp, sp);
        return re;
    }

    /// <summary>The analytic signal (cos, sin) - the real part is <see cref="Modulate"/>; the pair is for the fading
    /// simulator in the measurement.</summary>
    public static (float[] Re, float[] Im) ModulateIq(IReadOnlyList<int> tones, double f0, double amp = 0.9) => ModulateIq(tones, f0, amp, BleepsSpeed.Normal);

    public static (float[] Re, float[] Im) ModulateIq(IReadOnlyList<int> tones, double f0, double amp, BleepsSpeed sp)
    {
        int N = sp.SamplesPerSymbol;
        var f = Frequencies(tones, f0, sp);
        var re = new float[f.Length]; var im = new float[f.Length];
        double ph = 0;
        int ramp = N / 2;
        for (int n = 0; n < f.Length; n++)
        {
            double a = amp;
            if (n < ramp) a *= 0.5 - 0.5 * Math.Cos(Math.PI * (n + 0.5) / ramp);
            else if (n >= f.Length - ramp) a *= 0.5 - 0.5 * Math.Cos(Math.PI * (f.Length - n - 0.5) / ramp);
            re[n] = (float)(a * Math.Cos(ph)); im[n] = (float)(a * Math.Sin(ph));
            ph += 2 * Math.PI * f[n] / BleepsCode.SampleRate;
            if (ph > Math.PI) ph -= 2 * Math.PI;
        }
        return (re, im);
    }

    // ═════════════════════════════════════ one signal, whole audio at once ═════════════════════════════════════

    /// <summary>What the receiver found besides the message.</summary>
    public sealed record Found(BleepsCode.Decoded? Message, long StartSample, double FreqHz, double Ppm, double SnrDb,
                               int DataSymbols, bool EndSeen);

    /// <summary>Reads one message from a whole recording. <paramref name="approxStart"/> = the audio sample where the first
    /// symbol of the start sync begins (within <paramref name="startSlack"/> symbols), <paramref name="approxF0"/> = tone 0
    /// (within <paramref name="freqSlack"/> Hz). The same decoder as live (<see cref="BleepsDecoder"/>).</summary>
    public static Found Receive(IReadOnlyList<float> audio, long approxStart, double approxF0, double startSlack = 1, double freqSlack = 12,
                                int branches = -1, int step = -1, BleepsSpeed? speed = null)
    {
        var d = new BleepsDecoder(approxStart, approxF0, false, startSlack, freqSlack, branches, step, speed);
        d.Advance(new ArrayAudio(audio), final: true);
        return new Found(d.Snapshot(), d.StartSample, d.FreqHz, d.Ppm, d.SnrDb, d.DataSymbols, d.EndSeen);
    }
}
