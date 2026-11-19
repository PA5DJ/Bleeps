using System.Text;

namespace ShackAssistant.Helpers.Audio.Bleeps;

/// <summary>
/// The bit layer of Bleeps (PLAN-BLEEPS, PA5DJ 16 November 2026): ShackAssistant's own free-running text chat in the audio
/// passband. PUBLIC since 18 November 2026 (PA5DJ: "I want both on the Bleeps page. The goal is for Bleeps to become popular") -
/// written out on the website (guides/bleeps.html#spec, with test vectors). This file and <c>tools/bleeps/bleeps_ref.py</c> are
/// the specification; the two must send and read the same symbols bit for bit (measured in the scratchpad harness `bleeps`).
/// A change here that changes what goes on the air: a new Version AND the website's description.
/// <para>
/// A message, in symbols (4 tones, 2 bits each, Gray):
/// <code>
///   START = Costas x3 | DATA ... (a Costas after every 48 data symbols, if more data follows) | END = reversed Costas x2
/// </code>
/// DATA = the interleaved, convolutionally coded info bits:
/// <code>
///   version(4) call(10 x 6) CRC-12(over those 64) tail(6) | per UTF-8 byte: varicode + "00" | EOT varicode + "00"
///   | CRC-16 over the UTF-8 bytes | tail(6)
/// </code>
/// </para>
/// </summary>
public static class BleepsCode
{
    // ── the signal (PLAN-BLEEPS §2): FT4's raster ──
    public const int SampleRate = 48000, SamplesPerSymbol = 2304;      // 20.833 baud
    public const double Baud = (double)SampleRate / SamplesPerSymbol, ToneSpacing = Baud;
    public const int Tones = 4;

    // ── the frame (§3) ──
    /// <summary>Order-4 Costas arrays that FT4 does NOT use (FT4 has 0132, 1023, 2310, 3201 - an FT4 signal would sit
    /// right at the threshold of the search). Each matches an FT4 array on at most 2 of 4 tones; start and end differ on
    /// every symbol. (v1.0.715; v1.0.713-714 had 0132 / 2310.)</summary>
    public static readonly int[] Costas = { 0, 2, 3, 1 };
    public static readonly int[] CostasEnd = { 1, 3, 2, 0 };
    public const int StartSyncs = 3, EndSyncs = 2, SyncEvery = 48;
    public const int Version = 0, MaxCallChars = 10, MaxTextBytes = 250;
    public const string CallChars = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789/";   // 6 bits each, space = padding
    public const int HeaderBits = 4 + 6 * MaxCallChars + 12;                      // 76
    public const int Tail = 6;
    public const byte Eot = 0x04;

    // ── the interleaver (§4): convolutional (Forney), 8 branches, step 2 -> latency 112 coded bits (56 symbols, 2.7 s) ──
    public const int IlBranches = 8, IlStep = 2, IlLatency = (IlBranches - 1) * IlStep * IlBranches;

    /// <summary>Gray: tone of a 2-bit value (b0 b1, b0 first) and the 2-bit value of a tone.</summary>
    public static readonly int[] ToneOf = { 0, 1, 3, 2 }, ValueOf = { 0, 1, 3, 2 };

    // ── the code: K=7, rate 1/2, the same masks as KG-STV ──
    private const int G0 = 0x6D, G1 = 0x4F;
    private static int Parity(int x) => System.Numerics.BitOperations.PopCount((uint)x) & 1;

    // ═════════════════════════════════════ varicode ═════════════════════════════════════
    // Our OWN table, built by a rule both sides can repeat: every word starts and ends with 1 and has no "00" inside;
    // the words in order of length, then of value; the byte values in order of how common they are in chat text.

    /// <summary>The byte values from most to least common; every value 0..255 exactly once.</summary>
    public static readonly byte[] Rank = BuildRank();

    /// <summary>Varicode word per byte value (as a string of '0'/'1').</summary>
    public static readonly string[] Varicode = BuildVaricode();

    private static readonly Dictionary<string, byte> _fromWord = Enumerable.Range(0, 256).ToDictionary(b => Varicode[b], b => (byte)b);

    private static byte[] BuildRank()
    {
        const string common = " etaoinsrhldcumfpgwybvkxjqz.,ETAOINSRHLDCUMFPGWYBVKXJQZ0123456789?!'-/:;()\"@#&+=*%$_<>[]{}|\\~^`";
        var order = new List<byte>();
        foreach (char c in common) order.Add((byte)c);
        order.Add(Eot); order.Add(0xC3); order.Add(0xE2); order.Add((byte)'\n');   // EOT; UTF-8 leads of é/ü and €
        for (int b = 0; b < 256; b++) if (!order.Contains((byte)b)) order.Add((byte)b);
        return order.ToArray();
    }

    private static string[] BuildVaricode()
    {
        var words = new List<string>();
        for (int len = 1; words.Count < 256; len++)
        {
            for (long v = 0; v < (1L << len) && words.Count < 256; v++)
            {
                var s = Convert.ToString(v, 2).PadLeft(len, '0');
                if (s[0] == '1' && s[^1] == '1' && !s.Contains("00")) words.Add(s);
            }
        }
        var table = new string[256];
        for (int i = 0; i < 256; i++) table[Rank[i]] = words[i];
        return table;
    }

    // ═════════════════════════════════════ CRCs ═════════════════════════════════════

    /// <summary>CRC-12, poly 0x80F, init 0, over bits, most significant first.</summary>
    public static int Crc12(IReadOnlyList<byte> bits, int start, int count)
    {
        int c = 0;
        for (int i = start; i < start + count; i++)
        {
            int fb = ((c >> 11) & 1) ^ bits[i];
            c = (c << 1) & 0xFFF;
            if (fb != 0) c ^= 0x80F;
        }
        return c;
    }

    /// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, MSB first, no reflection) over bytes.</summary>
    public static int Crc16(IReadOnlyList<byte> bytes)
    {
        int c = 0xFFFF;
        foreach (var by in bytes)
            for (int i = 7; i >= 0; i--)
            {
                int fb = ((c >> 15) & 1) ^ ((by >> i) & 1);
                c = (c << 1) & 0xFFFF;
                if (fb != 0) c ^= 0x1021;
            }
        return c;
    }

    // ═════════════════════════════════════ the call ═════════════════════════════════════

    /// <summary>The call as it goes out (upper case, trimmed), or null with a reason when it cannot.</summary>
    public static string? CleanCall(string? call, out string problem)
    {
        var c = (call ?? "").Trim().ToUpperInvariant();
        problem = "";
        if (c.Length == 0) { problem = "no callsign"; return null; }
        if (c.Length > MaxCallChars) { problem = $"a callsign of at most {MaxCallChars} characters"; return null; }
        if (c.Any(ch => ch == ' ' || CallChars.IndexOf(ch) < 0)) { problem = "a callsign of letters, digits and /"; return null; }
        return c;
    }

    // ═════════════════════════════════════ encode ═════════════════════════════════════

    private static void Put(List<byte> bits, int value, int n) { for (int i = n - 1; i >= 0; i--) bits.Add((byte)((value >> i) & 1)); }

    /// <summary>The info bits of a message (before the code). Throws on a call or text that cannot go out.</summary>
    public static List<byte> InfoBits(string call, string text)
    {
        var c = CleanCall(call, out var why) ?? throw new ArgumentException(why);
        var bytes = Encoding.UTF8.GetBytes(text ?? "");
        if (bytes.Length > MaxTextBytes) throw new ArgumentException($"at most {MaxTextBytes} bytes of text ({bytes.Length})");
        if (bytes.Contains(Eot)) throw new ArgumentException("the text contains character 4 (end of message)");
        var bits = new List<byte>();
        Put(bits, Version, 4);
        foreach (char ch in c.PadRight(MaxCallChars)) Put(bits, CallChars.IndexOf(ch), 6);
        Put(bits, Crc12(bits, 0, bits.Count), 12);
        Put(bits, 0, Tail);
        foreach (var by in bytes) { foreach (char b in Varicode[by]) bits.Add((byte)(b - '0')); bits.Add(0); bits.Add(0); }
        foreach (char b in Varicode[Eot]) bits.Add((byte)(b - '0'));
        bits.Add(0); bits.Add(0);
        Put(bits, Crc16(bytes), 16);
        Put(bits, 0, Tail);
        return bits;
    }

    /// <summary>The rate-1/2 code from state 0: two output bits per input bit.</summary>
    public static byte[] ConvEncode(IReadOnlyList<byte> input)
    {
        var o = new byte[2 * input.Count];
        int st = 0;
        for (int k = 0; k < input.Count; k++)
        {
            st = ((st << 1) | input[k]) & 0x7F;
            o[2 * k] = (byte)Parity(st & G0);
            o[2 * k + 1] = (byte)Parity(st & G1);
        }
        return o;
    }

    /// <summary>Convolutional interleaver: y[n] = x[n - (n mod 8) * 16], x before 0 = 0. The input gets
    /// <see cref="IlLatency"/> zero bits appended so everything leaves.</summary>
    /// <remarks><paramref name="branches"/>/<paramref name="step"/> other than the spec only for the measurement of the depth.</remarks>
    public static byte[] Interleave(IReadOnlyList<byte> x, int branches = IlBranches, int step = IlStep)
    {
        int lat = (branches - 1) * step * branches;
        int n = x.Count + lat;
        var y = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int src = i - (i % branches) * step * branches;
            y[i] = src >= 0 && src < x.Count ? x[src] : (byte)0;
        }
        return y;
    }

    /// <summary>The other side, on soft values: z[n] = y[n - (7 - n mod 8) * 16] = x[n - 112]; the first 112 dropped.</summary>
    public static float[] Deinterleave(IReadOnlyList<float> y, int branches = IlBranches, int step = IlStep)
    {
        int lat = (branches - 1) * step * branches;
        int n = y.Count - lat;
        if (n <= 0) return Array.Empty<float>();
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            int k = i + lat;
            int src = k - (branches - 1 - k % branches) * step * branches;
            z[i] = src >= 0 && src < y.Count ? y[src] : 0f;
        }
        return z;
    }

    /// <summary>The data symbols (tones 0..3) of the interleaved coded bits, two bits per symbol, b0 first.</summary>
    public static int[] DataSymbols(IReadOnlyList<byte> coded)
    {
        var s = new int[coded.Count / 2];
        for (int i = 0; i < s.Length; i++) s[i] = ToneOf[coded[2 * i] * 2 + coded[2 * i + 1]];
        return s;
    }

    /// <summary>How many mid syncs a run of <paramref name="data"/> data symbols carries.</summary>
    public static int MidSyncs(int data) => data <= 0 ? 0 : (data - 1) / SyncEvery;

    /// <summary>The whole message as tones 0..3: start sync, data with its syncs, end sync.</summary>
    public static int[] Frame(IReadOnlyList<int> data) => Frame(data, BleepsSpeed.Normal);

    /// <summary>The frame with the sync patterns of a speed (Fast has its own - that is how the two are told apart).</summary>
    public static int[] Frame(IReadOnlyList<int> data, BleepsSpeed sp)
    {
        var Costas = sp.Costas; var CostasEnd = sp.CostasEnd;
        var o = new List<int>();
        for (int i = 0; i < StartSyncs; i++) o.AddRange(Costas);
        for (int i = 0; i < data.Count; i++)
        {
            if (i > 0 && i % SyncEvery == 0) o.AddRange(Costas);
            o.Add(data[i]);
        }
        for (int i = 0; i < EndSyncs; i++) o.AddRange(CostasEnd);
        return o.ToArray();
    }

    /// <summary>Call + text -> the tones of the whole message.</summary>
    public static int[] Encode(string call, string text, int branches = IlBranches, int step = IlStep) =>
        Frame(DataSymbols(Interleave(ConvEncode(InfoBits(call, text)), branches, step)));

    /// <summary>Call + text -> the tones of the whole message at a speed (its own interleaver and sync patterns).</summary>
    public static int[] Encode(string call, string text, BleepsSpeed sp) =>
        Frame(DataSymbols(Interleave(ConvEncode(InfoBits(call, text)), sp.IlBranches, sp.IlStep)), sp);

    /// <summary>Seconds on the air for a message.</summary>
    public static double Seconds(string call, string text) => Encode(call, text).Length / Baud;
    public static double Seconds(string call, string text, BleepsSpeed sp) => Encode(call, text, sp).Length / sp.Baud;

    // ═════════════════════════════════════ decode ═════════════════════════════════════

    /// <summary>The data symbols of a complete frame (syncs at their known places removed), or null when the frame
    /// does not have the length of one.</summary>
    public static int[]? StripFrame(IReadOnlyList<int> frame) => StripFrame(frame, BleepsSpeed.Normal);

    public static int[]? StripFrame(IReadOnlyList<int> frame, BleepsSpeed sp)
    {
        int lead = StartSyncs * 4, end = EndSyncs * 4;
        int body = frame.Count - lead - end;
        if (body <= 0) return null;
        int data = body;
        while (data > 0 && data + 4 * MidSyncs(data) > body) data--;
        if (data + 4 * MidSyncs(data) != body) return null;
        var o = new int[data];
        int p = lead;
        for (int i = 0; i < data; i++)
        {
            if (i > 0 && i % SyncEvery == 0) p += 4;
            o[i] = frame[p++];
        }
        return o;
    }

    /// <summary>Soft bits from the energies of the four tones of each symbol (max-log): positive = 1.</summary>
    public static float[] SoftBits(IReadOnlyList<float[]> energies)
    {
        var o = new float[energies.Count * 2];
        for (int i = 0; i < energies.Count; i++)
        {
            var e = energies[i];
            float b0One = float.MinValue, b0Zero = float.MinValue, b1One = float.MinValue, b1Zero = float.MinValue;
            for (int t = 0; t < Tones; t++)
            {
                int v = ValueOf[t];
                if ((v & 2) != 0) b0One = Math.Max(b0One, e[t]); else b0Zero = Math.Max(b0Zero, e[t]);
                if ((v & 1) != 0) b1One = Math.Max(b1One, e[t]); else b1Zero = Math.Max(b1Zero, e[t]);
            }
            o[2 * i] = b0One - b0Zero;
            o[2 * i + 1] = b1One - b1Zero;
        }
        return o;
    }

    /// <summary>Hard tones as energies (1 on the tone, 0 elsewhere) - for the bit-exact measurements.</summary>
    public static List<float[]> HardEnergies(IEnumerable<int> tones) =>
        tones.Select(t => { var e = new float[Tones]; e[t] = 1; return e; }).ToList();

    /// <summary>Soft-decision Viterbi from state 0 over soft bits (positive = 1); the best end state wins.</summary>
    /// <param name="anyStart">Start in any state (a receiver that came in halfway: the register is unknown).</param>
    public static byte[] Viterbi(IReadOnlyList<float> r, bool anyStart = false)
    {
        int n = r.Count / 2;
        var pm = new float[64]; var npm = new float[64];
        Array.Fill(pm, anyStart ? 0 : float.NegativeInfinity); pm[0] = 0;
        var from = new byte[n, 64]; var bit = new byte[n, 64];
        for (int k = 0; k < n; k++)
        {
            Array.Fill(npm, float.NegativeInfinity);
            float r0 = r[2 * k], r1 = r[2 * k + 1];
            for (int st = 0; st < 64; st++)
            {
                if (float.IsNegativeInfinity(pm[st])) continue;
                for (int b = 0; b < 2; b++)
                {
                    int full = ((st << 1) | b) & 0x7F, ns = full & 0x3F;
                    float d = pm[st] + (Parity(full & G0) == 1 ? r0 : -r0) + (Parity(full & G1) == 1 ? r1 : -r1);
                    if (d > npm[ns]) { npm[ns] = d; from[k, ns] = (byte)st; bit[k, ns] = (byte)b; }
                }
            }
            (pm, npm) = (npm, pm);
        }
        int best = 0;
        for (int s = 1; s < 64; s++) if (pm[s] > pm[best]) best = s;
        var o = new byte[n];
        for (int k = n - 1; k >= 0; k--) { o[k] = bit[k, best]; best = from[k, best]; }
        return o;
    }

    /// <summary>What came out of a message.</summary>
    /// <param name="Call">null when the header failed its CRC (then nothing else is trusted).</param>
    /// <param name="Complete">EOT seen and the CRC-16 over the text is right.</param>
    public sealed record Decoded(string? Call, string Text, bool Complete, int Version);

    /// <summary>The call from the 76 header bits, or null when the CRC-12 or a character is wrong.</summary>
    public static (string? Call, int Version) ParseHeader(IReadOnlyList<byte> u)
    {
        if (u.Count < HeaderBits) return (null, 0);
        int Num(int a, int n) { int v = 0; for (int i = a; i < a + n; i++) v = (v << 1) | u[i]; return v; }
        if (Crc12(u, 0, 64) != Num(64, 12)) return (null, 0);
        var sb = new StringBuilder();
        for (int i = 0; i < MaxCallChars; i++)
        {
            int v = Num(4 + 6 * i, 6);
            if (v >= CallChars.Length) return (null, 0);
            sb.Append(CallChars[v]);
        }
        var call = sb.ToString().TrimEnd();
        return call.Length == 0 || call.Contains(' ') ? (null, 0) : (call, Num(0, 4));
    }

    /// <summary>Reads varicode from <paramref name="start"/>: the bytes up to EOT, where EOT ended (the bit after its
    /// "00"), or -1 when no EOT came. An unknown word is skipped (one character lost, the "00" puts it right again).</summary>
    public static (List<byte> Bytes, int AfterEot) ReadVaricode(IReadOnlyList<byte> u, int start)
    {
        var bytes = new List<byte>();
        var word = new StringBuilder();
        int zeros = 0;
        for (int i = start; i < u.Count; i++)
        {
            if (u[i] == 1)
            {
                if (zeros == 1) word.Append('0');
                zeros = 0;
                word.Append('1');
                continue;
            }
            zeros++;
            if (zeros == 2 && word.Length > 0)
            {
                if (_fromWord.TryGetValue(word.ToString(), out var by))
                {
                    if (by == Eot) return (bytes, i + 1);
                    bytes.Add(by);
                }
                word.Clear();
            }
        }
        return (bytes, -1);
    }

    /// <summary>A complete frame of tones -> the message (bit-exact path; the receiver uses <see cref="DecodeSoft"/>).</summary>
    public static Decoded? Decode(IReadOnlyList<int> frame)
    {
        var data = StripFrame(frame);
        return data == null ? null : DecodeSoft(SoftBits(HardEnergies(data)));
    }

    /// <summary>Soft bits of the data symbols (interleaved, in air order) -> the message.</summary>
    public static Decoded DecodeSoft(IReadOnlyList<float> soft, int branches = IlBranches, int step = IlStep)
    {
        return DecodeFromInfo(Viterbi(Deinterleave(soft, branches, step)));
    }

    /// <summary>The decoded info bits of a whole message -> the message.</summary>
    public static Decoded DecodeFromInfo(IReadOnlyList<byte> u)
    {
        var (call, ver) = ParseHeader(u);
        if (call == null) return new Decoded(null, "", false, 0);
        var (bytes, after) = ReadVaricode(u, HeaderBits + Tail);
        bool ok = false;
        if (after >= 0 && after + 16 <= u.Count)
        {
            int crc = 0;
            for (int i = 0; i < 16; i++) crc = (crc << 1) | u[after + i];
            ok = crc == Crc16(bytes);
        }
        return new Decoded(call, Utf8(bytes), ok, ver);
    }

    /// <summary>UTF-8 that may be cut or damaged: what can be read, a replacement mark for the rest.</summary>
    public static string Utf8(IReadOnlyList<byte> bytes) => new UTF8Encoding(false, false).GetString(bytes.ToArray());
}

/// <summary>
/// A speed of Bleeps (PLAN-BLEEPS §10, v1.0.717). Everything above the signal is the same - header, varicode, CRCs, code,
/// framing; a speed is its symbol length (and so its tone spacing and width), its own sync patterns (how the receiver
/// tells them apart) and its interleaver (the same protection in SECONDS). A new speed = a row here + its search.
/// </summary>
public sealed record BleepsSpeed(string Name, int SamplesPerSymbol, int[] Costas, int[] CostasEnd, int IlBranches, int IlStep, char Tag)
{
    public double Baud => (double)BleepsCode.SampleRate / SamplesPerSymbol;
    public double ToneSpacing => Baud;
    /// <summary>What the signal occupies (99 % of the power): ~4 tone steps.</summary>
    public double WidthHz => 4 * ToneSpacing;
    /// <summary>The middle of the four tones above tone 0.</summary>
    public double CenterOffset => 1.5 * ToneSpacing;

    /// <summary>20.833 baud, ~83 Hz - the robust one (v1.0.713-).</summary>
    public static readonly BleepsSpeed Normal = new("Normal", 2304, new[] { 0, 2, 3, 1 }, new[] { 1, 3, 2, 0 }, 8, 2, 'N');
    /// <summary>41.667 baud, ~166 Hz, twice as fast (PA5DJ: "add a fast variant", ~2x FT4 allowed). Order-4 Costas
    /// arrays that neither FT4 nor Normal use. Interleaver 8 x 4: the same ~2.7 s as Normal's 8 x 2 (measured in step 2 style).</summary>
    public static readonly BleepsSpeed Fast = new("Fast", 1152, new[] { 0, 3, 1, 2 }, new[] { 2, 1, 3, 0 }, 8, 4, 'F');

    public static readonly BleepsSpeed[] All = { Normal, Fast };
    public static BleepsSpeed ByName(string? n) => All.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) ?? Normal;
}
