using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShackAssistant.Helpers.Audio.Bleeps;

/// <summary>
/// The emoji set of Bleeps (v1.0.736, PA5DJ: "ik wil een emoticon set toevoegen aan bleeps"; chosen: codes on the air, the
/// emoji on the screen, a picker plus typing, a big set). NOTHING here changes the protocol: the text stays UTF-8.
/// <para>On the screen (<see cref="Render"/>): a known <c>:code:</c> and a classic smiley (<c>:)</c> <c>;)</c> <c>&lt;3</c> ...,
/// only as a word of its own) become the emoji; an emoji that arrives as itself stays.</para>
/// <para>On the air (<see cref="ToWire"/>): every emoji of the set goes out in the CHEAPEST form, counted in varicode bits:
/// the classic smiley if it has one (22 bits against 52), otherwise <c>:code:</c> or the emoji itself, whichever is shorter
/// (a 3-byte emoji like ☕ is cheaper as itself than as <c>:cof:</c>). Every Bleeps receiver reads all three, and another
/// program that only shows UTF-8 still shows something readable.</para>
/// </summary>
public static class BleepsEmoji
{
    public sealed record Item(string Group, string Emoji, string Code, string[] Classic)
    {
        /// <summary>What the operator sees in a tooltip: ":code:" and the smileys that mean the same.</summary>
        public string Hint => Classic.Length == 0 ? $":{Code}:" : $":{Code}:   " + string.Join("  ", Classic);
    }

    private static Item I(string g, string e, string code, params string[] classic) => new(g, e, code, classic);

    /// <summary>The set, in the order of the picker. Codes: lowercase, digits, + - ? ! - at most 6 (short = cheap on the air).
    /// A new emoji is a row here; nothing else changes. Keep to emoji Windows 10 shows (no very new ones).</summary>
    public static readonly IReadOnlyList<Item> All = new[]
    {
        // faces
        I("Faces", "🙂", "smile", ":)", ":-)"), I("Faces", "😀", "grin", ":D", ":-D"), I("Faces", "😉", "wink", ";)", ";-)"),
        I("Faces", "🙁", "sad", ":(", ":-("), I("Faces", "😛", "tongue", ":P", ":-P", ":p"), I("Faces", "😜", "crazy", ";P"),
        I("Faces", "😮", "oh", ":O", ":o"), I("Faces", "😢", "cry", ":'("), I("Faces", "😐", "meh", ":|"),
        I("Faces", "😂", "lol"), I("Faces", "🤣", "rofl"), I("Faces", "😅", "phew"), I("Faces", "😊", "happy"),
        I("Faces", "😍", "love"), I("Faces", "😎", "cool"), I("Faces", "🤔", "hmm"), I("Faces", "😴", "zzz"),
        I("Faces", "😬", "eek"), I("Faces", "😡", "mad"), I("Faces", "😱", "omg"), I("Faces", "🤓", "nerd"),
        I("Faces", "😇", "halo"), I("Faces", "🥳", "yay"), I("Faces", "🤗", "hug"), I("Faces", "🥶", "cold"),
        I("Faces", "🥵", "hot"), I("Faces", "🤯", "wow"), I("Faces", "😷", "ill"), I("Faces", "🙃", "flip"), I("Faces", "🤐", "zip"),
        // gestures
        I("Gestures", "👍", "+1"), I("Gestures", "👎", "-1"), I("Gestures", "👋", "hi"), I("Gestures", "🙏", "tnx"),
        I("Gestures", "👌", "ok"), I("Gestures", "👏", "clap"), I("Gestures", "💪", "arm"), I("Gestures", "🤝", "deal"),
        I("Gestures", "✌", "peace"), I("Gestures", "🤞", "luck"), I("Gestures", "👀", "eyes"), I("Gestures", "🤷", "shrug"),
        I("Gestures", "✋", "hand"),
        // hearts and symbols
        I("Symbols", "❤", "heart", "<3"), I("Symbols", "💔", "broken", "</3"), I("Symbols", "⭐", "star"), I("Symbols", "✅", "yes"),
        I("Symbols", "❌", "no"), I("Symbols", "❓", "?"), I("Symbols", "❗", "!"), I("Symbols", "⚠", "warn"),
        I("Symbols", "💯", "100"), I("Symbols", "🔥", "fire"), I("Symbols", "✨", "shine"), I("Symbols", "🎉", "party"),
        I("Symbols", "🏆", "cup"), I("Symbols", "🎵", "note"), I("Symbols", "🔔", "bell"), I("Symbols", "🎂", "bday"),
        // radio and tech
        I("Radio", "📻", "rig"), I("Radio", "📡", "dish"), I("Radio", "🛰", "sat"), I("Radio", "📶", "sig"),
        I("Radio", "🎙", "mic"), I("Radio", "🎧", "hp"), I("Radio", "🔊", "loud"), I("Radio", "🔇", "mute"),
        I("Radio", "🔋", "bat"), I("Radio", "🔌", "plug"), I("Radio", "⚡", "zap"), I("Radio", "💻", "pc"),
        I("Radio", "📱", "tel"), I("Radio", "⌨", "kbd"), I("Radio", "🔧", "fix"), I("Radio", "🔭", "scope"),
        I("Radio", "💡", "idea"), I("Radio", "🌐", "www"), I("Radio", "✉", "mail"), I("Radio", "⏰", "time"),
        I("Radio", "⌛", "wait"), I("Radio", "🔍", "find"), I("Radio", "🧲", "mag"),
        // weather and sky
        I("Weather", "☀", "sun"), I("Weather", "🌤", "fair"), I("Weather", "☁", "cloud"), I("Weather", "🌧", "rain"),
        I("Weather", "⛈", "storm"), I("Weather", "❄", "snow"), I("Weather", "🌈", "rbow"), I("Weather", "🌙", "moon"),
        I("Weather", "🌍", "world"), I("Weather", "🌊", "sea"), I("Weather", "🌬", "wind"), I("Weather", "🌫", "fog"),
        I("Weather", "🌅", "dawn"),
        // food and drink
        I("Food", "☕", "cof"), I("Food", "🍺", "beer"), I("Food", "🍷", "wine"), I("Food", "🍕", "pizza"),
        I("Food", "🍔", "burg"), I("Food", "🍪", "cookie"), I("Food", "🍎", "apple"), I("Food", "🧀", "cheese"),
        I("Food", "🍫", "choc"), I("Food", "🍰", "cake"),
        // outdoors and travel (POTA, SOTA, IOTA)
        I("Outdoors", "🏠", "home"), I("Outdoors", "🚗", "car"), I("Outdoors", "✈", "plane"), I("Outdoors", "🚲", "bike"),
        I("Outdoors", "⛺", "camp"), I("Outdoors", "⛰", "mtn"), I("Outdoors", "🏝", "isle"), I("Outdoors", "🌳", "park"),
        I("Outdoors", "🗺", "map"), I("Outdoors", "🧭", "comp"), I("Outdoors", "⛵", "boat"), I("Outdoors", "🐕", "dog"),
        I("Outdoors", "🐈", "cat"), I("Outdoors", "🎁", "gift"), I("Outdoors", "📷", "cam"), I("Outdoors", "📖", "book"),
        I("Outdoors", "🎄", "xmas"),
    };

    /// <summary>The groups in picker order.</summary>
    public static IEnumerable<string> Groups => All.Select(i => i.Group).Distinct();

    private const string Vs16 = "️";
    private static readonly Dictionary<string, Item> _byCode = All.ToDictionary(i => i.Code, StringComparer.Ordinal);
    private static readonly Dictionary<string, Item> _byClassic = All.SelectMany(i => i.Classic.Select(c => (c, i))).ToDictionary(p => p.c, p => p.i, StringComparer.Ordinal);
    // longest first, so ":-)" wins over ":)" and "</3" over "<3"
    private static readonly Regex _code = new(@":([a-z0-9+\-?!]{1,6}):", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex _classic = new(@"(?<=^|\s)(" + string.Join("|", _byClassic.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?=$|\s|[.,!?])",
                                                 RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex _emoji = new("(" + string.Join("|", All.Select(i => i.Emoji).OrderByDescending(e => e.Length).Select(Regex.Escape)) + ")" + Vs16 + "?",
                                               RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The text as the operator sees it: codes and classic smileys become the emoji.</summary>
    public static string Render(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = _code.Replace(text, m => _byCode.TryGetValue(m.Groups[1].Value, out var it) ? it.Emoji : m.Value);
        return _classic.Replace(s, m => _byClassic[m.Value].Emoji);
    }

    /// <summary>
    /// While typing (v1.0.740, PA5DJ: "tijdens het typen als ik :rain: invul gelijk emoji van maken"): looks only at what stands
    /// just before the caret. A known <c>:code:</c> becomes its emoji the moment the closing colon is typed; a classic smiley
    /// when the space after it is typed (only then is it a word of its own - "(see:)" stays). Returns false if nothing changes.
    /// </summary>
    public static bool CompleteAt(string text, int caret, out string newText, out int newCaret)
    {
        newText = text; newCaret = caret;
        if (caret < 2 || caret > text.Length) return false;
        string before = text.Substring(0, caret);
        var m = Regex.Match(before, @":([a-z0-9+\-?!]{1,6}):$", RegexOptions.CultureInvariant);
        if (m.Success && _byCode.TryGetValue(m.Groups[1].Value, out var it))
        {
            newText = before.Substring(0, m.Index) + it.Emoji + text.Substring(caret);
            newCaret = m.Index + it.Emoji.Length;
            return true;
        }
        if (char.IsWhiteSpace(before[^1]))
        {
            string word = before.Substring(0, before.Length - 1);
            foreach (var c in _byClassic.Keys.OrderByDescending(k => k.Length))
            {
                if (!word.EndsWith(c, StringComparison.Ordinal)) continue;
                int at = word.Length - c.Length;
                if (at > 0 && !char.IsWhiteSpace(word[at - 1])) continue;
                var e = _byClassic[c].Emoji;
                newText = word.Substring(0, at) + e + before[^1] + text.Substring(caret);
                newCaret = at + e.Length + 1;
                return true;
            }
        }
        return false;
    }

    /// <summary>The bits a string costs on the air (varicode word + "00" per UTF-8 byte).</summary>
    public static int Bits(string s) => Encoding.UTF8.GetBytes(s).Sum(b => BleepsCode.Varicode[b].Length + 2);

    /// <summary>The cheapest way to send this emoji, and whether it is a classic smiley (that one needs a word of its own).</summary>
    public static (string Wire, bool Classic) WireOf(Item it)
    {
        if (it.Classic.Length > 0)
        {
            var c = it.Classic.OrderBy(Bits).First();
            if (Bits(c) <= Math.Min(Bits(":" + it.Code + ":"), Bits(it.Emoji))) return (c, true);
        }
        string code = ":" + it.Code + ":";
        return Bits(code) < Bits(it.Emoji) ? (code, false) : (it.Emoji, false);
    }

    /// <summary>What goes on the air: everything the operator typed or picked, each emoji of the set in its cheapest form.
    /// A classic smiley gets a space around it where it needs one, so the other side reads it as a smiley again.</summary>
    public static string ToWire(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var shown = Render(text);
        var sb = new StringBuilder();
        int at = 0;
        foreach (Match m in _emoji.Matches(shown))
        {
            sb.Append(shown, at, m.Index - at);
            var it = All.First(i => i.Emoji == m.Groups[1].Value);
            var (wire, classic) = WireOf(it);
            if (classic && sb.Length > 0 && !char.IsWhiteSpace(sb[^1])) sb.Append(' ');
            sb.Append(wire);
            int next = m.Index + m.Length;
            if (classic && next < shown.Length && !char.IsWhiteSpace(shown[next]) && ".,!?".IndexOf(shown[next]) < 0) sb.Append(' ');
            at = next;
        }
        sb.Append(shown, at, shown.Length - at);
        return sb.ToString();
    }

    // ── the pictures: Twemoji in colour (v1.0.737, PA5DJ: "Twemoji in kleur"; CC-BY 4.0, see Twemoji\LICENSE.txt) ──

    /// <summary>Who made the pictures - shown in About and the picker (CC-BY asks for it).</summary>
    public const string Credit = "Emoji graphics: Twemoji by Twitter, Inc and other contributors, CC-BY 4.0";

    private static readonly Dictionary<(string, int), Image?> _pics = new();

    /// <summary>The Twemoji file name of an emoji: its code points in hex, without the variation selector.</summary>
    public static string FileOf(string emoji) =>
        string.Join("-", Elements(emoji).SelectMany(s => EnumerateRunes(s)).Where(r => r.Value != 0xFE0F).Select(r => r.Value.ToString("x"))) + ".png";

    private static IEnumerable<Rune> EnumerateRunes(string s) { foreach (var r in s.EnumerateRunes()) yield return r; }

    /// <summary>The colour picture of an emoji of the set at <paramref name="px"/> x <paramref name="px"/>, scaled once and kept;
    /// null for an emoji outside the set or when the picture is missing (then the text is drawn as it is).</summary>
    public static Image? Picture(string emoji, int px)
    {
        px = Math.Clamp(px, 8, 144);
        string e = emoji.Replace(Vs16, "");
        lock (_pics)
        {
            if (_pics.TryGetValue((e, px), out var have)) return have;
            Image? img = null;
            try
            {
                if (All.Any(i => i.Emoji == e))
                {
                    using var s = typeof(BleepsEmoji).Assembly.GetManifestResourceStream("Twemoji." + FileOf(e));
                    if (s != null)
                    {
                        using var src = Image.FromStream(s);
                        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.SmoothingMode = SmoothingMode.HighQuality; g.CompositingQuality = CompositingQuality.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, px, px));
                        }
                        img = bmp;
                    }
                }
            }
            catch { img = null; }
            _pics[(e, px)] = img;
            return img;
        }
    }

    /// <summary>A shown text cut into pieces: plain text, and the emoji of the set (which get a picture).</summary>
    public static IEnumerable<(string Text, bool Emoji)> Segments(string shown)
    {
        int at = 0;
        foreach (Match m in _emoji.Matches(shown))
        {
            if (m.Index > at) yield return (shown.Substring(at, m.Index - at), false);
            yield return (m.Groups[1].Value, true);
            at = m.Index + m.Length;
        }
        if (at < shown.Length) yield return (shown.Substring(at), false);
    }

    /// <summary>The text elements of a string (an emoji is ONE, even as a surrogate pair) - for cutting a line by hand.</summary>
    public static IEnumerable<string> Elements(string s)
    {
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) yield return (string)e.Current;
    }
}
