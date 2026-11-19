using System.Globalization;
using System.Text.Json;
using ShackAssistant.Helpers;

namespace Bleeps;

/// <summary>One working frequency, like a row in WSJT-X's Settings -> Frequencies.</summary>
public sealed class FrequencyEntry
{
    public double MHz { get; set; }
    public string Region { get; set; } = "All";
    public string Description { get; set; } = "";
    public string Band => Bands.Of(MHz * 1e6);
    public override string ToString() => $"{Bands.FormatMHz(MHz)}  {Band}";
}

/// <summary>The amateur bands (IARU, all regions together) and the frequency text everywhere: always with a POINT.</summary>
public static class Bands
{
    public static readonly (string Name, double LoMHz, double HiMHz)[] All =
    {
        ("2200m", 0.1357, 0.1378), ("630m", 0.472, 0.479), ("160m", 1.8, 2.0), ("80m", 3.5, 4.0), ("60m", 5.06, 5.45),
        ("40m", 7.0, 7.3), ("30m", 10.1, 10.15), ("20m", 14.0, 14.35), ("17m", 18.068, 18.168), ("15m", 21.0, 21.45),
        ("12m", 24.89, 24.99), ("10m", 28.0, 29.7), ("6m", 50.0, 54.0), ("4m", 70.0, 71.0), ("2m", 144.0, 148.0),
        ("1.25m", 222.0, 225.0), ("70cm", 420.0, 450.0), ("23cm", 1240.0, 1300.0), ("13cm", 2300.0, 2450.0),
        ("9cm", 3300.0, 3500.0), ("6cm", 5650.0, 5925.0), ("3cm", 10000.0, 10500.0),
    };

    public static string Of(double hz)
    {
        double mhz = hz / 1e6;
        foreach (var b in All) if (mhz >= b.LoMHz && mhz <= b.HiMHz) return b.Name;
        return "OOB";
    }

    public static int Order(string band) { int i = Array.FindIndex(All, b => b.Name == band); return i < 0 ? 999 : i; }

    /// <summary>"14.071 000" - MHz with the Hz grouped in threes, as WSJT-X shows the dial.</summary>
    public static string FormatDial(double hz)
    {
        long h = (long)Math.Round(hz);
        long mhz = h / 1_000_000, rest = h % 1_000_000;
        return string.Create(CultureInfo.InvariantCulture, $"{mhz}.{rest / 1000:000} {rest % 1000:000}");
    }

    public static string FormatMHz(double mhz) => mhz.ToString("0.000###", CultureInfo.InvariantCulture);

    /// <summary>A typed frequency: MHz with a point or comma ("14.071", "14,071"), or kHz ("14071"), or Hz.</summary>
    public static bool TryParseMHz(string text, out double mhz)
    {
        mhz = 0;
        var t = text.Trim().Replace(',', '.').Replace(" ", "");
        if (t.EndsWith("mhz", StringComparison.OrdinalIgnoreCase)) t = t[..^3];
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0) return false;
        if (t.Contains('.')) mhz = v;            // with a decimal point: MHz
        else if (v >= 1_000_000) mhz = v / 1e6;  // Hz
        else if (v >= 1000) mhz = v / 1e3;       // kHz
        else mhz = v;
        return mhz > 0.1 && mhz < 300_000;
    }
}

/// <summary>
/// The frequency list (Settings -> Frequencies), stored as frequencies.json in the settings folder. Starts with the
/// Bleeps proposals: in a digimode/MGM segment of the IARU Region 1 band plan, next to FT8/FT4, outside CW, phone and RTTY.
/// </summary>
public static class FrequencyList
{
    public static string FilePath => Path.Combine(AppSettings.Folder, "frequencies.json");

    public static List<FrequencyEntry> Defaults() => new()
    {
        new() { MHz = 3.570, Description = "digimodes, under FT8" },
        new() { MHz = 5.360, Description = "all-mode, above FT8" },
        new() { MHz = 7.051, Description = "digimodes, above FT4" },
        new() { MHz = 10.143, Description = "digimodes, above FT4" },
        new() { MHz = 14.071, Description = "digimodes, under FT8" },
        new() { MHz = 18.097, Description = "digimodes, under FT8" },
        new() { MHz = 21.071, Description = "digimodes, under FT8" },
        new() { MHz = 24.926, Description = "digimodes, above FT4/JS8" },
        new() { MHz = 28.071, Description = "digimodes, under FT8" },
        new() { MHz = 50.385, Description = "MGM, above meteor scatter" },
        new() { MHz = 70.270, Description = "MGM centre of activity" },
        new() { MHz = 144.605, Description = "data centre of activity" },
        new() { MHz = 432.090, Description = "MGM, under SSB" },
        new() { MHz = 2400.060, Description = "QO-100 uplink; downlink 10489.560 (digimodes 540-580)" },
    };

    public static List<FrequencyEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<FrequencyEntry>>(File.ReadAllText(FilePath));
                if (list != null) return Sorted(list);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("frequencies.json unreadable, kept aside: " + ex.Message);
            try { File.Move(FilePath, FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
        }
        return Defaults();
    }

    public static void Save(List<FrequencyEntry> list)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Sorted(list), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Warn("Saving frequencies.json: " + ex.Message); }
    }

    public static List<FrequencyEntry> Sorted(IEnumerable<FrequencyEntry> list) => list.OrderBy(e => e.MHz).ToList();
}
