using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Bleeps;

/// <summary>
/// Is there a newer release? The ANSWER comes from the site (bleeps.ham4life.com/api/latest.php, the same API that fills the
/// version badges on the pages - downloads/ IS the list, so uploading a setup publishes it); the RULES are here, apart from the
/// network, so they can be checked without internet. Ported from ShackAssistant's <c>UpdateCheck</c> (v1.0.22).
/// </summary>
public static class UpdateCheck
{
    /// <summary>Where the site really is (PA5DJ, 20 November 2026: bleeps.ham4life.com). If the site moves, this is the line.</summary>
    public const string Host = "https://bleeps.ham4life.com";

    public static string ApiUrl => Host + "/api/latest.php";

    /// <summary>The download page - not the file itself.</summary>
    public static string PageUrl => Host + "/download.php";

    /// <summary>Short: this is a convenience, not an action.</summary>
    public const int TimeoutSeconds = 8;

    /// <summary>A release as the site reports it. <c>Url</c> is RELATIVE to the site root.</summary>
    public sealed record Release(string Version, string Label, string File, string Url, long Size, string Date, string Kind);

    /// <summary>
    /// Reads the site's answer. <c>null</c> when there is nothing to report - <c>{"version": null}</c> (no release in downloads/)
    /// is a valid answer, not an error. Rubbish is never a release.
    /// </summary>
    public static Release? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return null;
            string? Str(string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var ver = Str("version");
            if (string.IsNullOrWhiteSpace(ver)) return null;
            ver = ver.Trim();
            long size = o.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : 0;
            return new Release(ver, Str("label") ?? "v" + ver, Str("file") ?? "", Str("url") ?? "", size, Str("date") ?? "", Str("kind") ?? "");
        }
        catch { return null; }
    }

    /// <summary>
    /// Is <paramref name="remote"/> NEWER than <paramref name="local"/>? Compared part by part, a missing part counts as zero, a
    /// leading <c>v</c> is ignored. Anything not provably newer is false - equal, older or unreadable: an older release on the
    /// site must never be offered as an "update".
    /// </summary>
    public static bool IsNewer(string? local, string? remote)
    {
        var a = Numbers(local);
        var b = Numbers(remote);
        if (a == null || b == null) return false;
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            long mine = i < a.Length ? a[i] : 0, theirs = i < b.Length ? b[i] : 0;
            if (theirs > mine) return true;
            if (theirs < mine) return false;
        }
        return false;
    }

    /// <summary>Did the operator ask not to be reminded of <paramref name="remote"/>? Only the SAME version; a newer one speaks again.</summary>
    public static bool IsSkipped(string? skipped, string? remote) =>
        Numbers(skipped) != null && Numbers(remote) != null && !IsNewer(skipped, remote) && !IsNewer(remote, skipped);

    private static long[]? Numbers(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t[1..];
        if (t.Length == 0) return null;
        var parts = t.Split('.');
        var got = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!long.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out got[i])) return null;
        return got;
    }

    /// <summary>What the message says, apart from showing it (a modal box cannot be checked; this half can be wrong).</summary>
    public static string Text(Release r, string? local)
    {
        var line = r.Label;
        if (r.Date.Length > 0) line += ", " + r.Date;
        if (r.Size > 0) line += ", " + SizeText(r.Size);
        return "You have:  " + (string.IsNullOrWhiteSpace(local) ? "unknown" : local.Trim()) + Environment.NewLine
             + "Available: " + line + Environment.NewLine + Environment.NewLine
             + "Open the download page?";
    }

    public static string UpToDateText(string? local) => "Bleeps " + (local ?? "").Trim() + " is the newest version.";

    private static string SizeText(long bytes) =>
        bytes >= 1024L * 1024L ? (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : Math.Max(1, bytes / 1024L) + " kB";

    /// <summary>
    /// Asks the site for the newest release. NEVER throws: no internet is an ordinary outcome here, and an update check must
    /// never get in the way. One HttpClient per check is fine: this runs once per start and on Help -> Check for updates.
    /// </summary>
    public static async Task<(Release? Found, string Error)> FetchAsync(string? url = null, int timeoutSeconds = TimeoutSeconds)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            http.DefaultRequestHeaders.Add("User-Agent", "Bleeps/" + MainForm.Version.TrimStart('v'));
            var body = await http.GetStringAsync(url ?? ApiUrl).ConfigureAwait(false);
            var got = Parse(body);
            return (got, got == null ? "the site reported no release" : "");
        }
        catch (Exception ex) { return (null, ex.Message); }
    }
}
