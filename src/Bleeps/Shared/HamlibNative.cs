using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ShackAssistant.Helpers;

public record RigEntry(int Model, string Name)
{
    public override string ToString() => Name;
}

/// <summary>P/Invoke wrapper for libhamlib-4.dll loaded from %ProgramData%\ShackAssistant\hamlib\.</summary>
public static class HamlibNative
{
    public static readonly string HamlibFolder = LocateFolder();

    private static string LocateFolder()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "hamlib");
        if (File.Exists(Path.Combine(local, "libhamlib-4.dll"))) return local;

        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                  "ShackAssistant", "hamlib");
        var user   = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "ShackAssistant", "hamlib");
        // The per-user folder only exists when an update could not be written to ProgramData. Take whichever
        // holds the NEWER library: a user-side update has to win over an older shared install, but a fresh
        // installer must never be shadowed by a stale user copy.
        return NewerOf(shared, user);
    }

    private static string NewerOf(string a, string b)
    {
        var va = FileVersionOf(a);
        var vb = FileVersionOf(b);
        if (vb == null) return a;
        if (va == null) return b;
        return vb > va ? b : a;
    }

    private static Version? FileVersionOf(string folder)
    {
        try
        {
            var dll = Path.Combine(folder, "libhamlib-4.dll");
            if (!File.Exists(dll)) return null;
            var fi = FileVersionInfo.GetVersionInfo(dll);
            return new Version(Math.Max(fi.FileMajorPart, 0), Math.Max(fi.FileMinorPart, 0),
                               Math.Max(fi.FileBuildPart, 0), Math.Max(fi.FilePrivatePart, 0));
        }
        catch { return null; }
    }

    public static bool IsAvailable =>
        File.Exists(Path.Combine(HamlibFolder, "libhamlib-4.dll"));

    static HamlibNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(HamlibNative).Assembly, Resolver);
    }

    private static IntPtr Resolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "libhamlib-4") return IntPtr.Zero;
        var path = Path.Combine(HamlibFolder, "libhamlib-4.dll");
        if (!File.Exists(path)) return IntPtr.Zero;
        SetDllDirectory(HamlibFolder);
        return NativeLibrary.Load(path);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    // ── Constants ────────────────────────────────────────────────────────
    public const int RIG_VFO_A      = 1 << 0;
    public const int RIG_VFO_B      = 1 << 1;
    public const int RIG_VFO_CURR   = 1 << 29;
    public const int RIG_OK         = 0;
    public const int ModelDummy     = 1;   // RIG_MODEL_DUMMY — software simulation, no hardware needed
    public const int ModelNetRigCtl = 2;   // RIG_MODEL_NETRIGCTL — connects to rigctld over TCP

    // ── Version ──────────────────────────────────────────────────────────

    /// <summary>Full path of the DLL the resolver will actually load (may not exist).</summary>
    public static string DllPath => Path.Combine(HamlibFolder, "libhamlib-4.dll");

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr rig_version();

    /// <summary>Version of the installed Hamlib, e.g. "Hamlib 4.6.2". Asks the library itself first; falls
    /// back to the file version so a DLL that will not load still tells you which one is sitting there.
    /// Empty when nothing is installed.</summary>
    public static string VersionString()
    {
        if (!IsAvailable) return "";
        try
        {
            var p = rig_version();
            var v = p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : null;
            if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        }
        catch { /* missing export or unloadable DLL — fall through to the file version */ }
        try
        {
            var fv = FileVersionInfo.GetVersionInfo(DllPath).FileVersion;
            if (!string.IsNullOrWhiteSpace(fv)) return "Hamlib " + fv!.Trim();
        }
        catch { }
        return "unknown";
    }

    // ── Core API ─────────────────────────────────────────────────────────

    [DllImport("libhamlib-4", EntryPoint = "rig_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr rig_init_native(int rig_model);

    /// <summary>
    /// rig_init, but never at the same time as Hamlib loading its backends (SA v1.0.750, Bleeps app: "stopt iedere keer na
    /// splash"). In Hamlib 4.7.2 <c>rig_register</c> ends the WHOLE PROCESS - <c>printf("Hash collision!!! Fatal error!!");
    /// exit(1);</c> - when a backend registers twice. <c>rig_load_all_backends</c> (the model list, <see cref="RigModels"/>)
    /// empties the table and registers every backend, and <c>rig_init</c> on another thread loads its own backend when the
    /// table has no caps for it: the two together registered Icom twice (seen on stderr: "initrigs4_icom: _init called" twice)
    /// and the app was gone with exit code 1, no exception, no log line. So: the model list is loaded FIRST (once, before any
    /// rig exists - loading it later would also empty the table under an open rig), and rig_init runs under the same lock.
    /// </summary>
    public static IntPtr rig_init(int rig_model)
    {
        _ = RigModels;
        lock (_rigModelsLock) return rig_init_native(rig_model);
    }

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_open(IntPtr rig);

    /// <summary>What a Hamlib error code means (enum rig_errcode_e). NOT rigerror(): in this Hamlib it
    /// returns a buffer that keeps every earlier message too, so the third failure read as three.</summary>
    public static string ErrorText(int code) => code switch
    {
        -1 => "invalid parameter", -2 => "invalid configuration", -3 => "out of memory",
        -4 => "not implemented", -5 => "communication timed out", -6 => "IO error (port busy or gone?)",
        -7 => "internal Hamlib error", -8 => "protocol error", -9 => "command rejected by the radio",
        -10 => "answer truncated", -11 => "function not available", -12 => "VFO not targetable",
        -13 => "bus error (CI-V collision)", -14 => "bus busy", -15 => "invalid argument",
        -16 => "invalid VFO", -17 => "argument out of domain",
        // Newer Hamlib (4.6). -22 was the first one met for real (PA5DJ, IC-705 over Bluetooth): the
        // Web Logbook in the browser had the same COM port open - a port takes one program at a time.
        -18 => "function deprecated", -19 => "security error", -20 => "the radio is switched off",
        -21 => "limit exceeded", -22 => "port in use by another program (access denied)",
        _ => $"Hamlib error {code}",
    };

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_close(IntPtr rig);

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_cleanup(IntPtr rig);

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_set_freq(IntPtr rig, int vfo, double freq);

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_get_freq(IntPtr rig, int vfo, out double freq);

    // rmode_t = uint64_t → ulong;  pbwidth_t = signed long (32-bit on Windows x64) → int
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_set_mode(IntPtr rig, int vfo, ulong mode, int width);

    /// <summary>Hamlib's pbwidth_t for "leave the filter as it is" (RIG_PASSBAND_NOCHANGE). 0 is RIG_PASSBAND_NORMAL: the
    /// rig's normal width for the mode, which SETS the filter.</summary>
    public const int RIG_PASSBAND_NOCHANGE = -1;

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_get_mode(IntPtr rig, int vfo, out ulong mode, out int width);

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_set_ptt(IntPtr rig, int vfo, int ptt);

    // ptt_t: 0=RX, 1=TX, 2=TX_MIC, 3=TX_DATA
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_get_ptt(IntPtr rig, int vfo, out int ptt);

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_get_vfo(IntPtr rig, out int vfo);

    // setting_t = uint64_t -> ulong. Hamlib checks the rig's caps first: a rig without the function gets -RIG_ENAVAIL and
    // nothing goes over the line (v1.0.741).
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_get_func(IntPtr rig, int vfo, ulong func, out int status);

    // hamlib_token_t = long = 32-bit on Windows x64 (as rig_set_conf). Hamlib writes the value as text, e.g. civaddr "162".
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int rig_get_conf2(IntPtr rig, int token, System.Text.StringBuilder val, int valLen);

    /// <summary>RIG_FUNC_SATMODE (rig.h 4.x: bit 26) - satellite mode on/off.</summary>
    public const ulong RIG_FUNC_SATMODE = 1UL << 26;
    /// <summary>-RIG_ERJCTED: the rig answered NG (it refused the command) - not "no answer".</summary>
    public const int RIG_ERJCTED = -9;

    // hamlib_token_t = long in C = 32-bit on Windows x64
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_token_lookup(IntPtr rig,
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_set_conf(IntPtr rig, int token,
        [MarshalAs(UnmanagedType.LPStr)] string val);

    // hctype 0 = HAMLIB_CACHE_ALL; ms 0 = disable cache (always query rig).
    // Available since Hamlib 4.0 — may throw EntryPointNotFoundException on older builds.
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern void rig_set_cache_timeout_ms(IntPtr rig, int hctype, int ms);

    /// <summary>
    /// Send bytes straight at the radio and, with a buffer, read what comes back. Exported by the
    /// Hamlib that ships here (4.7.2) and probed before this was written; an older DLL may not have it,
    /// so every caller catches <see cref="EntryPointNotFoundException"/> — the same care
    /// <c>rig_set_cache_timeout_ms</c> already needs.
    ///
    /// <c>term</c> is the byte to read up to. A null reply buffer makes it fire-and-forget.
    /// </summary>
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_send_raw(IntPtr rig, byte[] send, int sendLen,
                                          byte[]? reply, int replyLen, byte[]? term);

    /// <summary>
    /// Stop the voice memory the radio is playing. The BACKEND knows which command that is for the rig
    /// that is configured — <c>RX;</c> for an Elecraft, a CI-V frame for an Icom, the DVK stop for a
    /// Kenwood — which is exactly how N1MM decides it on the Esc key.
    ///
    /// A backend that does not implement it returns a negative code rather than doing nothing quietly.
    /// </summary>
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_stop_voice_mem(IntPtr rig, int vfo);

    /// <summary>
    /// Split on or off, and which VFO transmits. The BACKEND knows how this radio does it - the CI-V
    /// frame for an Icom, the command for a Kenwood, the sub-receiver on a rig that has one - so a
    /// macro that asks for split works on any of them without knowing which is connected.
    /// </summary>
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_set_split_vfo(IntPtr rig, int rxVfo, int split, int txVfo);

    /// <summary>The transmit frequency while split is on. <c>freq_t</c> is a double in Hamlib.</summary>
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rig_set_split_freq(IntPtr rig, int vfo, double txFreq);

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    private static extern int rig_load_all_backends();

    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    private static extern int rig_list_foreach_model(RigListCallback cfunc, IntPtr data);

    // rig_get_caps_int: rig_caps_int_e values: RIG_CAPS_RIG_MODEL=1, RIG_CAPS_PORT_TYPE=2
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong rig_get_caps_int(uint rigModel, int caps);

    // rig_get_caps_cptr: rig_caps_cptr_e values: RIG_CAPS_MFG_NAME_CPTR=1, RIG_CAPS_MODEL_NAME_CPTR=2
    [DllImport("libhamlib-4", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr rig_get_caps_cptr(uint rigModel, int caps);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RigListCallback(uint rigModel, IntPtr data);

    // ── Rig model list ────────────────────────────────────────────────────

    private static RigEntry[]? _rigModels;
    private static readonly object _rigModelsLock = new();

    public static RigEntry[] RigModels
    {
        get
        {
            if (_rigModels != null) return _rigModels;
            lock (_rigModelsLock)
            {
                return _rigModels ??= LoadRigModels();
            }
        }
    }

    private static RigEntry[] LoadRigModels()
    {
        if (!IsAvailable) return FallbackRigModels;
        try
        {
            rig_load_all_backends();
            var list = new List<RigEntry>();
            var cb = new RigListCallback((model, _) =>
            {
                try
                {
                    // port_type: SERIAL=1, NETWORK=2, USB=9  (rig_port_e)
                    var port = (int)rig_get_caps_int(model, 2);
                    if (port != 1 && port != 2 && port != 9) return 1;
                    var mfg  = Marshal.PtrToStringAnsi(rig_get_caps_cptr(model, 1)) ?? "";
                    var name = Marshal.PtrToStringAnsi(rig_get_caps_cptr(model, 2)) ?? "";
                    if (string.IsNullOrWhiteSpace(name)) return 1;
                    var display = string.IsNullOrWhiteSpace(mfg)
                        ? name.Trim()
                        : $"{mfg.Trim()} {name.Trim()}";
                    list.Add(new RigEntry((int)model, display));
                }
                catch { }
                return 1; // continue
            });
            rig_list_foreach_model(cb, IntPtr.Zero);
            GC.KeepAlive(cb);

            // Ensure virtual rigs that have RIG_PORT_NONE are always present
            foreach (var special in new[] { new RigEntry(1, "Hamlib Dummy (no hardware)"),
                                            new RigEntry(4, "FLRig (FLRig CAT server)") })
                if (!list.Any(r => r.Model == special.Model))
                    list.Add(special);

            var sorted = list
                .Where(r => r.Model is not (1 or 2 or 4))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Concat(list.Where(r => r.Model is 1 or 2 or 4).OrderBy(r => r.Model))
                .ToArray();

            return sorted.Length > 0 ? sorted : FallbackRigModels;
        }
        catch
        {
            return FallbackRigModels;
        }
    }

    // Fallback list used when libhamlib-4.dll is not installed
    private static readonly RigEntry[] FallbackRigModels =
    [
        // ── Icom ────────────────────────────────────────────────────────
        new(3009, "Icom IC-706"),
        new(3010, "Icom IC-706MkII"),
        new(3011, "Icom IC-706MkIIG"),
        new(3013, "Icom IC-718"),
        new(3055, "Icom IC-703"),
        new(3023, "Icom IC-746"),
        new(3046, "Icom IC-746Pro"),
        new(3027, "Icom IC-756Pro"),
        new(3047, "Icom IC-756ProII"),
        new(3057, "Icom IC-756ProIII"),
        new(3044, "Icom IC-910H"),
        new(3060, "Icom IC-7000"),
        new(3070, "Icom IC-7100"),
        new(3061, "Icom IC-7200"),
        new(3073, "Icom IC-7300"),
        new(3094, "Icom IC-7300 MK2"),
        new(3067, "Icom IC-7410"),
        new(3063, "Icom IC-7600"),
        new(3078, "Icom IC-7610"),
        new(3062, "Icom IC-7700"),
        new(3056, "Icom IC-7800"),
        new(3092, "Icom IC-7760"),
        new(3085, "Icom IC-705"),
        new(3090, "Icom IC-905"),
        new(3068, "Icom IC-9100"),
        new(3081, "Icom IC-9700"),
        new(3079, "Icom IC-R8600"),
        // ── Yaesu ───────────────────────────────────────────────────────
        new(1021, "Yaesu FT-100(D)"),
        new(1027, "Yaesu FT-450"),
        new(1046, "Yaesu FT-450D"),
        new(1020, "Yaesu FT-817(ND)"),
        new(1041, "Yaesu FT-818(ND)"),
        new(1001, "Yaesu FT-847"),
        new(1011, "Yaesu FT-840"),
        new(1015, "Yaesu FT-890"),
        new(1022, "Yaesu FT-857(D)"),
        new(1036, "Yaesu FT-891"),
        new(1023, "Yaesu FT-897(D)"),
        new(1013, "Yaesu FT-900"),
        new(1014, "Yaesu FT-920"),
        new(1028, "Yaesu FT-950"),
        new(1016, "Yaesu FT-990"),
        new(1035, "Yaesu FT-991(A)"),
        new(1029, "Yaesu FT-2000"),
        new(1049, "Yaesu FT-710"),
        new(1030, "Yaesu FT-9000"),
        new(1003, "Yaesu FT-1000D"),
        new(1024, "Yaesu FT-1000MP"),
        new(1004, "Yaesu FT-1000MP MkV"),
        new(1034, "Yaesu FTDX-1200"),
        new(1037, "Yaesu FTDX-3000"),
        new(1032, "Yaesu FTDX-5000"),
        new(1042, "Yaesu FTDX-10"),
        new(1040, "Yaesu FTDX-101D"),
        new(1044, "Yaesu FTDX-101MP"),
        // ── Kenwood ─────────────────────────────────────────────────────
        new(2001, "Kenwood TS-50S"),
        new(2002, "Kenwood TS-440S"),
        new(2003, "Kenwood TS-450S"),
        new(2004, "Kenwood TS-570D"),
        new(2016, "Kenwood TS-570S"),
        new(2005, "Kenwood TS-690S"),
        new(2009, "Kenwood TS-850"),
        new(2010, "Kenwood TS-870S"),
        new(2011, "Kenwood TS-940S"),
        new(2013, "Kenwood TS-950SDX"),
        new(2014, "Kenwood TS-2000"),
        new(2028, "Kenwood TS-480"),
        new(2031, "Kenwood TS-590S"),
        new(2037, "Kenwood TS-590SG"),
        new(2039, "Kenwood TS-990S"),
        new(2041, "Kenwood TS-890S"),
        // ── Elecraft ────────────────────────────────────────────────────
        new(2021, "Elecraft K2"),
        new(2029, "Elecraft K3"),
        new(2043, "Elecraft K3S"),
        new(2047, "Elecraft K4"),
        new(2044, "Elecraft KX2"),
        new(2045, "Elecraft KX3"),
        // ── Software / Virtual ──────────────────────────────────────────
        new(1,    "Hamlib Dummy (no hardware)"),
        new(4,    "FLRig (FLRig CAT server)"),
        new(2,    "Network rig (rigctld proxy)"),
        new(2048, "PowerSDR / HPSDR"),
        new(2054, "Thetis / Apache Labs ANAN"),
        new(2055, "TruSDX"),
        new(2050, "Lab599 TX-500"),
    ];
}
