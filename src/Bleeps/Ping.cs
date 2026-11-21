using System.Globalization;
using System.Text.RegularExpressions;

namespace Bleeps;

/// <summary>
/// Ping (v1.0.23, PA5DJ: "a Ping option: it sends a message to a callsign, the other station answers with a short
/// confirmation; a short ID is enough"). A CONVENTION in the text, not a protocol change - Bleeps version 0 stays as it is,
/// and a program that does not know pings (ShackAssistant) simply shows them as text:
/// <code>
///   ping dl1abc 7k          from PA5DJ: "DL1ABC, do you hear me?"   (7k = a 2-character ID)
///   pong pa5dj 7k -12       from DL1ABC: "PA5DJ, I heard ping 7k at -12 dB"
/// </code>
/// Only a COMPLETE message (CRC-16 right) counts; case does not matter. On the air: a ping 14.7 s Normal / 8.8 s Fast, a
/// pong 16.6 / 9.7 s (any message is at least 9.8 / 6.3 s: the header).
/// </summary>
public static class Ping
{
    private const string IdChars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";   // no 0/O, 1/I: easy to read back

    public enum Kind { Ping, Pong }

    /// <summary>What a ping or pong text says. <see cref="SnrDb"/> only in a pong: how the pinged station heard the ping.</summary>
    public sealed record Parsed(Kind Kind, string Call, string Id, int? SnrDb);

    private static readonly Regex Rx = new(@"^\s*(PING|PONG)\s+([A-Z0-9/]{3,10})\s+([A-Z0-9]{2})(?:\s+([+-]?\d{1,2}))?\s*$",
                                           RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string NewId() => string.Concat(Enumerable.Range(0, 2).Select(_ => IdChars[Random.Shared.Next(IdChars.Length)]));

    // sent in LOWER case: Bleeps' varicode gives lower-case letters the short words, so "ping pa5dj 7k" is 1.3 s shorter than
    // "PING PA5DJ 7K" (14.7 s against 16.0 s Normal; 9.8 s of any message is the header). Reading ignores case.
    public static string PingText(string call, string id) => $"ping {call.ToLowerInvariant()} {id.ToLowerInvariant()}";

    public static string PongText(string call, string id, double snrDb) =>
        $"pong {call.ToLowerInvariant()} {id.ToLowerInvariant()} {Math.Clamp((int)Math.Round(double.IsNaN(snrDb) ? 0 : snrDb), -30, 30).ToString("+0;-0;+0", CultureInfo.InvariantCulture)}";

    /// <summary>A ping or a pong, or null when the text is something else.</summary>
    public static Parsed? Parse(string? text)
    {
        var m = Rx.Match(text ?? "");
        if (!m.Success) return null;
        var kind = m.Groups[1].Value.Equals("PING", StringComparison.OrdinalIgnoreCase) ? Kind.Ping : Kind.Pong;
        int? snr = m.Groups[4].Success && int.TryParse(m.Groups[4].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int s) ? s : null;
        if (kind == Kind.Ping && snr != null) return null;   // a ping carries no report
        return new Parsed(kind, m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value.ToUpperInvariant(), snr);
    }

    public enum Reply { NotForUs, Answer, AlreadyAnswered, Blacklisted, AnsweringOff }

    /// <summary>What to do with a complete PING from <paramref name="from"/>: answer it or not, and why not.</summary>
    public static Reply Decide(string? myCall, string from, Parsed p, bool autoReply, ISet<string> blacklist, ISet<string> answered)
    {
        if (p.Kind != Kind.Ping || myCall == null || !p.Call.Equals(myCall, StringComparison.OrdinalIgnoreCase)
            || from.Equals(myCall, StringComparison.OrdinalIgnoreCase)) return Reply.NotForUs;
        if (answered.Contains(from.ToUpperInvariant() + " " + p.Id)) return Reply.AlreadyAnswered;   // the same ping heard twice
        if (blacklist.Contains(from)) return Reply.Blacklisted;
        if (!autoReply) return Reply.AnsweringOff;
        return Reply.Answer;
    }

    public static string FormatDb(double db) => double.IsNaN(db) ? "?" : ((int)Math.Round(db)).ToString("+0;-0;+0", CultureInfo.InvariantCulture) + " dB";
}
