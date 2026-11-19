namespace ShackAssistant.Helpers.Audio;

/// <summary>
/// Minimal radix-2 FFT plus the window and dB helpers around it. Deliberately dependency-free and
/// self-contained: this is settled maths that will not change, so it costs nothing to own it and it
/// keeps an extra library out of the installer.
/// </summary>
public static class Fft
{
    /// <summary>In-place complex FFT. <paramref name="re"/> and <paramref name="im"/> must be the same
    /// power-of-two length.</summary>
    public static void Forward(float[] re, float[] im)
    {
        int n = re.Length;
        if (n <= 1) return;
        if ((n & (n - 1)) != 0) throw new ArgumentException("FFT length must be a power of two.");

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang  = -2 * Math.PI / len;
            float  wRe  = (float)Math.Cos(ang);
            float  wIm  = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float curRe = 1f, curIm = 0f;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    float tRe = re[b] * curRe - im[b] * curIm;
                    float tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                    re[a] += tRe;        im[a] += tIm;
                    float nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }

    /// <summary>Hann window of the given length — smooth roll-off, so a carrier does not smear across bins.
    /// Kept for reference and for anything that only needs a cheap taper; the waterfall uses
    /// <see cref="BlackmanHarrisWindow"/>, see the note there.</summary>
    public static float[] HannWindow(int n)
    {
        var w = new float[n];
        for (int i = 0; i < n; i++) w[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1))));
        return w;
    }

    /// <summary>4-term Blackman-Harris window: sidelobes at -92 dB instead of Hann's -31 dB.
    ///
    /// This is what keeps a strong signal narrow on the waterfall. The colour scale sits about 2 dB above
    /// the noise, so every decibel a window leaks into neighbouring bins is painted in. A carrier 60 dB
    /// out of the noise leaked far enough through a Hann window to colour 291 px of a 1080 px display —
    /// a quarter of the band for a signal one bin wide. Measured on this same FFT, per tone level:
    ///
    ///     -60 dBFS   Hann  42 px    BH  51 px
    ///     -40 dBFS   Hann  84 px    BH  34 px
    ///     -20 dBFS   Hann 156 px    BH  42 px
    ///      -6 dBFS   Hann 291 px    BH  68 px
    ///
    /// The price is an honest one: the main lobe is wider, so the very weakest signals draw a few pixels
    /// broader. In exchange the width stops depending on how loud the station is.</summary>
    public static float[] BlackmanHarrisWindow(int n)
    {
        var w = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / (n - 1);
            w[i] = (float)(0.35875 - 0.48829 * Math.Cos(t) + 0.14128 * Math.Cos(2 * t) - 0.01168 * Math.Cos(3 * t));
        }
        return w;
    }

    /// <summary>Magnitude of one bin in dB (full scale = 0 dB), floored so silence cannot reach -infinity.</summary>
    public static float MagnitudeDb(float re, float im, int fftSize)
    {
        double mag = Math.Sqrt(re * re + im * im) * 2.0 / fftSize;
        return (float)(20.0 * Math.Log10(Math.Max(mag, 1e-7)));
    }
}
