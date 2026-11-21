using System.Globalization;
using System.Text;

// The few ShackAssistant helpers that the SHARED files (BleepsPanel, BleepsSettingsForm) call, rebuilt for this program:
// the same names and the same members, so those files compile here unchanged. Nothing else of ShackAssistant comes along.
namespace ShackAssistant.Helpers;

/// <summary>A small INI file: %APPDATA%\Bleeps\bleeps.ini. Same Get/Set as ShackAssistant's AppSettings.</summary>
public class AppSettings
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Bleeps.Instance.FolderName);   // "Bleeps - IC9700" with --rig (v1.0.8)
    public static string ConfigFile => Path.Combine(Folder, "bleeps.ini");

    private readonly Dictionary<string, Dictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    public bool Dirty { get; private set; }

    public AppSettings() { Load(); }

    /// <summary>The file was there but could not be read: this session runs on defaults and will NOT write them over the
    /// real file (v1.0.24 - before, a read that failed once, e.g. a virus scanner or OneDrive holding the file, wiped the
    /// settings at the next exit).</summary>
    public bool LoadFailed { get; private set; }

    private void Load()
    {
        if (!File.Exists(ConfigFile)) return;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var lines = File.ReadAllLines(ConfigFile, Encoding.UTF8);
                string section = "";
                foreach (var raw in lines)
                {
                    var line = raw.Trim().TrimStart('﻿');                 // a BOM from another editor
                    if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                    if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    Sec(section)[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
                return;
            }
            catch (Exception ex) when (attempt < 5) { Bleeps.Log.Warn($"Reading {ConfigFile} (attempt {attempt}): {ex.Message}"); Thread.Sleep(200); }
            catch (Exception ex)
            {
                LoadFailed = true;
                Bleeps.Log.Error($"Reading {ConfigFile} failed: {ex.Message} - running on defaults, the file is left as it is");
                return;
            }
        }
    }

    private Dictionary<string, string> Sec(string s)
    {
        if (!_data.TryGetValue(s, out var d)) _data[s] = d = new(StringComparer.OrdinalIgnoreCase);
        return d;
    }

    public string Get(string section, string key, string defaultValue = "")
    {
        lock (_lock) return _data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public bool GetBool(string section, string key, bool defaultValue = false)
    {
        var v = Get(section, key, "");
        if (v.Length == 0) return defaultValue;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public int GetInt(string section, string key, int defaultValue = 0)
        => int.TryParse(Get(section, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : defaultValue;

    public double GetDouble(string section, string key, double defaultValue = 0)
        => double.TryParse(Get(section, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : defaultValue;

    public void Set(string section, string key, string value)
    {
        value = (value ?? "").Replace("\r", " ").Replace("\n", " ");
        lock (_lock)
        {
            var d = Sec(section);
            if (d.TryGetValue(key, out var old) && old == value) return;
            d[key] = value;
            Dirty = true;
        }
    }
    public void Set(string section, string key, bool value) => Set(section, key, value ? "True" : "False");
    public void Set(string section, string key, int value) => Set(section, key, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string section, string key, double value) => Set(section, key, value.ToString("R", CultureInfo.InvariantCulture));

    public void Save()
    {
        if (LoadFailed) { Bleeps.Log.Warn("Settings not saved: the file could not be read at start, so it is not overwritten"); return; }
        try
        {
            Directory.CreateDirectory(Folder);
            // keep the previous version next to it (bleeps.ini.bak) - one bad write never costs everything
            if (File.Exists(ConfigFile)) { try { File.Copy(ConfigFile, ConfigFile + ".bak", overwrite: true); } catch { } }
            var sb = new StringBuilder();
            lock (_lock)
            {
                foreach (var (name, d) in _data.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sb.Append('[').Append(name).AppendLine("]");
                    foreach (var (k, v) in d.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) sb.Append(k).Append(" = ").AppendLine(v);
                    sb.AppendLine();
                }
                Dirty = false;
            }
            var tmp = ConfigFile + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            File.Move(tmp, ConfigFile, overwrite: true);
        }
        catch (Exception ex) { Bleeps.Log.Warn("Saving the settings: " + ex.Message); }
    }
}

/// <summary>
/// Light or dark (v1.0.20): WinForms' own colour mode (<c>Application.SetColorMode</c>), so SystemColors - and every control
/// that uses them - turn dark by themselves; the shared chat panel asks <see cref="IsDark"/> for its own colours.
/// </summary>
public static class AppTheme
{
    public static readonly string[] Choices = { "System", "Light", "Dark" };

    /// <summary>Call once, before the first window: "System" follows Windows (Settings -> Personalisation -> Colours).</summary>
    public static void ApplyAtStart(string choice)
    {
#pragma warning disable WFO5001   // the colour mode API is marked experimental
        var mode = choice.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? SystemColorMode.Dark
                 : choice.Equals("Light", StringComparison.OrdinalIgnoreCase) ? SystemColorMode.Classic
                 : SystemColorMode.System;
        try { Application.SetColorMode(mode); }
        catch (Exception ex) { Bleeps.Log.Warn("Colour mode: " + ex.Message); }
#pragma warning restore WFO5001
    }

#pragma warning disable WFO5001
    public static bool IsDark => Application.IsDarkModeEnabled;
#pragma warning restore WFO5001
    public static Color Field => SystemColors.Window;
    public static Color Face => SystemColors.Control;
}

/// <summary>What the shared forms call at the end of their constructor: here only the icon.</summary>
public static class UITheme
{
    private static Icon? _icon;
    private static bool _tried;

    public static Icon? AppIcon
    {
        get
        {
            if (_tried) return _icon;
            _tried = true;
            try
            {
                using var s = typeof(UITheme).Assembly.GetManifestResourceStream("Bleeps.ico");
                if (s != null) _icon = new Icon(s);
            }
            catch { }
            return _icon;
        }
    }

    public static void Apply(Control c)
    {
        if (c is Form f && AppIcon != null) f.Icon = AppIcon;
    }

    /// <summary>
    /// For a small dialog built in code (Ping, About, Insert a frequency): its sizes are written for 96 dpi, and this scales them
    /// to the screen once, the way the big windows do it (Bleeps v1.0.25 - a 140 px call box was 70 px wide at 200 %, the About
    /// text wrapped at half its width). Call it once, after the controls are added; never on a window that already scales.
    /// </summary>
    public static void ScaleFromDesign(Form f)
    {
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.PerformLayout();
    }
}
