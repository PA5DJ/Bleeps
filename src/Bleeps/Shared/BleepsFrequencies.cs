using System.Globalization;

namespace ShackAssistant.Helpers.Audio.Bleeps;

/// <summary>
/// Where Bleeps is worked: one dial frequency per band (PA5DJ, 19 November 2026), in a digimode/MGM segment of the IARU
/// Region 1 band plan, next to FT8/FT4, outside CW, phone and RTTY. A PROPOSAL, not an agreement. Shared by Mode Master's
/// band buttons and the stand-alone Bleeps program (its default frequency list). QO-100 is the UPLINK the radio tunes.
/// </summary>
public static class BleepsFrequencies
{
    public sealed record Entry(string Band, double MHz, string Note);

    public static readonly IReadOnlyList<Entry> All = new Entry[]
    {
        new("80m", 3.570, "digimodes, under FT8"),
        new("60m", 5.360, "all-mode, above FT8"),
        new("40m", 7.051, "digimodes, above FT4"),
        new("30m", 10.143, "digimodes, above FT4"),
        new("20m", 14.071, "digimodes, under FT8"),
        new("17m", 18.097, "digimodes, under FT8"),
        new("15m", 21.071, "digimodes, under FT8"),
        new("12m", 24.926, "digimodes, above FT4/JS8"),
        new("10m", 28.071, "digimodes, under FT8"),
        new("6m", 50.385, "MGM, above meteor scatter"),
        new("4m", 70.270, "MGM centre of activity"),
        new("2m", 144.605, "data centre of activity"),
        new("70cm", 432.090, "MGM, under SSB"),
        new("13cm", 2400.060, "QO-100 uplink; downlink 10489.560 (digimodes 540-580)"),
    };

    /// <summary>The amateur bands (all regions together), for "which band is the radio on".</summary>
    public static readonly (string Name, double LoMHz, double HiMHz)[] Bands =
    {
        ("2200m", 0.1357, 0.1378), ("630m", 0.472, 0.479), ("160m", 1.8, 2.0), ("80m", 3.5, 4.0), ("60m", 5.06, 5.45),
        ("40m", 7.0, 7.3), ("30m", 10.1, 10.15), ("20m", 14.0, 14.35), ("17m", 18.068, 18.168), ("15m", 21.0, 21.45),
        ("12m", 24.89, 24.99), ("10m", 28.0, 29.7), ("6m", 50.0, 54.0), ("4m", 70.0, 71.0), ("2m", 144.0, 148.0),
        ("1.25m", 222.0, 225.0), ("70cm", 420.0, 450.0), ("23cm", 1240.0, 1300.0), ("13cm", 2300.0, 2450.0),
        ("9cm", 3300.0, 3500.0), ("6cm", 5650.0, 5925.0), ("3cm", 10000.0, 10500.0),
    };

    /// <summary>The band of a frequency in Hz, "" outside the amateur bands.</summary>
    public static string BandOf(double hz)
    {
        double mhz = hz / 1e6;
        foreach (var b in Bands) if (mhz >= b.LoMHz && mhz <= b.HiMHz) return b.Name;
        return "";
    }

    /// <summary>"14.071 000" - MHz with the Hz grouped in threes, as WSJT-X shows the dial. Always a point.</summary>
    public static string FormatDial(double hz)
    {
        long h = (long)Math.Round(hz);
        long mhz = h / 1_000_000, rest = h % 1_000_000;
        return string.Create(CultureInfo.InvariantCulture, $"{mhz}.{rest / 1000:000} {rest % 1000:000}");
    }
}
