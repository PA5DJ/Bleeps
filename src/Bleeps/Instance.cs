namespace Bleeps;

/// <summary>
/// More than one Bleeps side by side, as WSJT-X does with --rig-name (v1.0.8, PA5DJ: "I want to be able to start sessions side by side
/// with a CLI flag, as in WSJT-X with --rig IC9700 etc. each then gets its own appdata").
/// <c>Bleeps.exe --rig IC9700</c> (also <c>--rig=IC9700</c>, <c>--rig-name IC9700</c>, <c>-r IC9700</c>) gets its own settings
/// (<c>%APPDATA%\Bleeps - IC9700</c>), its own log and chat logs (<c>Documents\Bleeps - IC9700</c>), its own single-instance lock
/// and the name in its title. Without the flag nothing changes: <c>Bleeps</c>, as before.
/// </summary>
public static class Instance
{
    /// <summary>The name after --rig, cleaned for a folder name; "" = the ordinary Bleeps.</summary>
    public static string Name { get; private set; } = "";

    /// <summary>" - IC9700" for a named instance, "" otherwise - what goes behind "Bleeps" in folder names and titles.</summary>
    public static string Suffix => Name.Length == 0 ? "" : " - " + Name;

    /// <summary>The folder name: "Bleeps" or "Bleeps - IC9700".</summary>
    public static string FolderName => "Bleeps" + Suffix;

    /// <summary>Reads the command line once, before anything else touches a folder.</summary>
    public static void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? value = null;
            foreach (var flag in new[] { "--rig-name", "--rig", "-r" })
            {
                if (a.Equals(flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { value = args[++i]; break; }
                if (a.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase)) { value = a[(flag.Length + 1)..]; break; }
            }
            if (value != null) { Name = Clean(value); return; }
        }
    }

    /// <summary>Only what a folder name can carry; spaces stay, the rest becomes '-'.</summary>
    public static string Clean(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var c = new string(s.Trim().Select(ch => bad.Contains(ch) ? '-' : ch).ToArray()).Trim(' ', '.', '-');
        return c.Length > 40 ? c[..40] : c;
    }
}
