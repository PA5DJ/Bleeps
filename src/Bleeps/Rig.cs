using System.Collections.Concurrent;
using System.Globalization;
using ShackAssistant.Helpers;

namespace Bleeps;

/// <summary>The radio, as set in Settings -> Radio. ONE radio, no profiles.</summary>
public sealed class RigConfig
{
    public const int NoRig = 0;

    /// <summary>Hamlib model number; 0 = no radio (PTT on RTS/DTR or VOX still works).</summary>
    public int Model;
    /// <summary>COM port, or host:port for a network rig (rigctld = model 2, FLRig = model 4).</summary>
    public string Port = "";
    public int Baud = 9600;
    public string DataBits = "Default", StopBits = "Default", Handshake = "Default", Dtr = "Default", Rts = "Default";
    /// <summary>Icom CI-V address in hex ("94"), empty = the backend's own.</summary>
    public string CivAddress = "";
    /// <summary>CAT, RTS, DTR, VOX.</summary>
    public string Ptt = "VOX";
    /// <summary>Port for RTS/DTR PTT; empty = the CAT port.</summary>
    public string PttPort = "";
    /// <summary>What the radio is put in when tuning: None (leave it), USB, Data (PKTUSB).</summary>
    public string RigMode = "Data";
    public int PollMs = 1000;
    public int TxDelayMs = 120, TxTailMs = 300;

    public static bool IsNetworkModel(int model) => model is 2 or 4;

    public static RigConfig Load(AppSettings s) => new()
    {
        Model = s.GetInt("radio", "model", NoRig),
        Port = s.Get("radio", "port", ""),
        Baud = s.GetInt("radio", "baud", 9600),
        DataBits = s.Get("radio", "data_bits", "Default"),
        StopBits = s.Get("radio", "stop_bits", "Default"),
        Handshake = s.Get("radio", "handshake", "Default"),
        Dtr = s.Get("radio", "dtr", "Default"),
        Rts = s.Get("radio", "rts", "Default"),
        CivAddress = s.Get("radio", "civ_address", ""),
        Ptt = s.Get("radio", "ptt", "VOX"),
        PttPort = s.Get("radio", "ptt_port", ""),
        RigMode = s.Get("radio", "rig_mode", "Data"),
        PollMs = Math.Clamp(s.GetInt("radio", "poll_ms", 1000), 250, 10000),
        TxDelayMs = Math.Clamp(s.GetInt("radio", "tx_delay_ms", 120), 0, 1000),
        TxTailMs = Math.Clamp(s.GetInt("radio", "tx_tail_ms", 300), 0, 3000),
    };

    public void Save(AppSettings s)
    {
        s.Set("radio", "model", Model); s.Set("radio", "port", Port); s.Set("radio", "baud", Baud);
        s.Set("radio", "data_bits", DataBits); s.Set("radio", "stop_bits", StopBits); s.Set("radio", "handshake", Handshake);
        s.Set("radio", "dtr", Dtr); s.Set("radio", "rts", Rts); s.Set("radio", "civ_address", CivAddress);
        s.Set("radio", "ptt", Ptt); s.Set("radio", "ptt_port", PttPort); s.Set("radio", "rig_mode", RigMode);
        s.Set("radio", "poll_ms", PollMs); s.Set("radio", "tx_delay_ms", TxDelayMs); s.Set("radio", "tx_tail_ms", TxTailMs);
    }

    /// <summary>Does this setup need Hamlib at all? A radio, or PTT on a serial line.</summary>
    public bool NeedsHamlib => Model != NoRig || Ptt is "RTS" or "DTR";
    public bool UsePtt => Ptt is "CAT" or "RTS" or "DTR";
}

/// <summary>What the radio said at the last poll.</summary>
public sealed record RigState(bool Connected, double Hz, string Mode, string Status);

/// <summary>
/// Hamlib on ONE thread of its own: Hamlib is not thread-safe, so every call - open, poll, set, PTT - goes through the
/// queue of this thread. Model 2 is rigctld (host:port), 4 is FLRig; every other model opens a COM port (or host:port for
/// a network radio). No radio + PTT on RTS/DTR uses Hamlib's dummy rig with only its PTT port.
/// </summary>
public sealed class RigControl : IDisposable
{
    public event Action<RigState>? StateChanged;
    public RigState State { get; private set; } = new(false, 0, "", "No radio");

    private readonly RigConfig _cfg;
    private readonly BlockingCollection<Action> _queue = new();
    private Thread? _thread;
    private volatile bool _stop;
    private IntPtr _rig;
    private bool _pttOn;

    public RigControl(RigConfig cfg) { _cfg = cfg; }

    public string Describe() => _cfg.Model == RigConfig.NoRig ? "no radio"
        : $"{HamlibNative.RigModels.FirstOrDefault(r => r.Model == _cfg.Model)?.Name ?? "model " + _cfg.Model} on {_cfg.Port}";

    public void Start()
    {
        if (!_cfg.NeedsHamlib) { Publish(new RigState(false, 0, "", "No radio")); return; }
        if (!HamlibNative.IsAvailable) { Publish(new RigState(false, 0, "", "Hamlib not found (hamlib\\libhamlib-4.dll)")); return; }
        _thread = new Thread(Loop) { IsBackground = true, Name = "Bleeps Hamlib" };
        _thread.Start();
    }

    /// <summary>Tune: frequency in Hz, then the mode of Settings (None = leave the mode alone).</summary>
    public void SetFrequency(double hz) => _queue.TryAdd(() =>
    {
        if (_rig == IntPtr.Zero || _cfg.Model == RigConfig.NoRig) return;
        int r = HamlibNative.rig_set_freq(_rig, HamlibNative.RIG_VFO_CURR, hz);
        if (r != HamlibNative.RIG_OK) Log.Warn($"rig_set_freq {hz:0}: {HamlibNative.ErrorText(r)}");
        ulong mode = _cfg.RigMode switch { "USB" => 1UL << 2, "Data" => 1UL << 11, _ => 0 };
        if (mode != 0)
        {
            r = HamlibNative.rig_set_mode(_rig, HamlibNative.RIG_VFO_CURR, mode, HamlibNative.RIG_PASSBAND_NOCHANGE);
            if (r != HamlibNative.RIG_OK) Log.Warn($"rig_set_mode {_cfg.RigMode}: {HamlibNative.ErrorText(r)}");
        }
        Poll();
    });

    /// <summary>Key or unkey - WAITS until it is done (or 3 s), so the audio starts after PTT. True when it worked.</summary>
    public bool SetPtt(bool on)
    {
        if (!_cfg.UsePtt) return true;
        if (_thread == null) return false;
        bool ok = false;
        using var done = new ManualResetEventSlim();
        if (!_queue.TryAdd(() =>
        {
            try
            {
                if (_rig == IntPtr.Zero) return;
                int r = HamlibNative.rig_set_ptt(_rig, HamlibNative.RIG_VFO_CURR, on ? 1 : 0);
                ok = r == HamlibNative.RIG_OK;
                if (ok) _pttOn = on; else Log.Warn($"PTT {(on ? "on" : "off")}: {HamlibNative.ErrorText(r)}");
            }
            finally { try { done.Set(); } catch (ObjectDisposedException) { } }
        })) return false;
        done.Wait(3000);
        return ok;
    }

    public bool IsOpen => _rig != IntPtr.Zero;

    private void Loop()
    {
        var nextPoll = DateTime.MinValue;
        var nextOpen = DateTime.MinValue;
        while (!_stop)
        {
            try
            {
                if (_rig == IntPtr.Zero && DateTime.UtcNow >= nextOpen)
                {
                    if (!Open()) { nextOpen = DateTime.UtcNow.AddSeconds(5); }
                    else nextPoll = DateTime.MinValue;
                }
                int wait = Math.Max(10, (int)(nextPoll - DateTime.UtcNow).TotalMilliseconds);
                if (_queue.TryTake(out var job, Math.Min(wait, 250)))
                {
                    try { job(); } catch (Exception ex) { Log.Exception("Hamlib command", ex); }
                    continue;
                }
                if (_rig != IntPtr.Zero && _cfg.Model != RigConfig.NoRig && DateTime.UtcNow >= nextPoll)
                {
                    nextPoll = DateTime.UtcNow.AddMilliseconds(_cfg.PollMs);
                    if (!_pttOn) Poll();
                }
            }
            catch (Exception ex) { Log.Exception("Hamlib loop", ex); Thread.Sleep(500); }
        }
        // drain what is left (a PTT-off waits for it), then close
        while (_queue.TryTake(out var job)) { try { job(); } catch { } }
        Close();
    }

    private int _fails;

    private void Poll()
    {
        if (_rig == IntPtr.Zero) return;
        int r = HamlibNative.rig_get_freq(_rig, HamlibNative.RIG_VFO_CURR, out double hz);
        if (r != HamlibNative.RIG_OK)
        {
            if (++_fails >= 3)
            {
                Log.Warn($"Radio not answering ({HamlibNative.ErrorText(r)}) - reconnecting");
                Close();
                Publish(new RigState(false, 0, "", "Radio not answering: " + HamlibNative.ErrorText(r)));
            }
            return;
        }
        _fails = 0;
        string mode = State.Mode;
        if (HamlibNative.rig_get_mode(_rig, HamlibNative.RIG_VFO_CURR, out ulong m, out _) == HamlibNative.RIG_OK) mode = ModeName(m);
        Publish(new RigState(true, hz, mode, "Connected"));
    }

    private static void Conf(IntPtr rig, string key, string val)
    {
        int tok = HamlibNative.rig_token_lookup(rig, key);
        if (tok != 0) HamlibNative.rig_set_conf(rig, tok, val);
        else Log.Warn($"Hamlib does not know the setting '{key}'");
    }

    private static string PortPath(string port)
    {
        var p = port.Trim();
        if (p.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(p[3..], out int n) && n >= 10) return @"\\.\" + p.ToUpperInvariant();
        return p;
    }

    private bool Open()
    {
        int model = _cfg.Model == RigConfig.NoRig ? HamlibNative.ModelDummy : _cfg.Model;
        _rig = HamlibNative.rig_init(model);
        if (_rig == IntPtr.Zero) { Publish(new RigState(false, 0, "", $"Hamlib does not know model {model}")); return false; }
        bool serial = _cfg.Model != RigConfig.NoRig && !RigConfig.IsNetworkModel(_cfg.Model) && !_cfg.Port.Contains(':');
        if (_cfg.Model != RigConfig.NoRig)
        {
            Conf(_rig, "rig_pathname", PortPath(_cfg.Port));
            if (serial)
            {
                Conf(_rig, "serial_speed", _cfg.Baud.ToString(CultureInfo.InvariantCulture));
                if (_cfg.DataBits != "Default") Conf(_rig, "data_bits", _cfg.DataBits);
                if (_cfg.StopBits != "Default") Conf(_rig, "stop_bits", _cfg.StopBits);
                if (_cfg.Handshake != "Default") Conf(_rig, "serial_handshake", _cfg.Handshake);
                if (_cfg.Dtr != "Default") Conf(_rig, "dtr_state", _cfg.Dtr == "High" ? "ON" : "OFF");
                if (_cfg.Rts != "Default") Conf(_rig, "rts_state", _cfg.Rts == "High" ? "ON" : "OFF");
            }
            if (_cfg.CivAddress.Trim().Length > 0) Conf(_rig, "civaddr", "0x" + _cfg.CivAddress.Trim().TrimStart('0', 'x', 'X').PadLeft(2, '0'));
            Conf(_rig, "timeout", "3000");
            Conf(_rig, "retry", "2");
        }
        // PTT: CAT = the rig's own; RTS/DTR on the CAT port or a port of its own; VOX = none
        if (_cfg.Ptt is "RTS" or "DTR")
        {
            Conf(_rig, "ptt_type", _cfg.Ptt);
            string pp = _cfg.PttPort.Trim().Length > 0 ? _cfg.PttPort : _cfg.Port;
            if (pp.Trim().Length > 0) Conf(_rig, "ptt_pathname", PortPath(pp));
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int r = HamlibNative.rig_open(_rig);
        if (r != HamlibNative.RIG_OK)
        {
            string why = $"{HamlibNative.ErrorText(r)} after {sw.Elapsed.TotalSeconds:0.0} s";
            Log.Warn($"rig_open ({Describe()}) failed: {why}");
            HamlibNative.rig_cleanup(_rig);
            _rig = IntPtr.Zero;
            Publish(new RigState(false, 0, "", "Cannot open the radio: " + why));
            return false;
        }
        Log.Info($"rig_open ({Describe()}, PTT {_cfg.Ptt}) took {sw.Elapsed.TotalSeconds:0.0} s");
        Conf(_rig, "timeout", "1500");
        Conf(_rig, "retry", "0");
        try { HamlibNative.rig_set_cache_timeout_ms(_rig, 0, 0); } catch (EntryPointNotFoundException) { }
        _fails = 0;
        if (_cfg.Model == RigConfig.NoRig) Publish(new RigState(false, 0, "", $"No radio, PTT on {_cfg.Ptt}"));
        else Poll();
        return true;
    }

    private void Close()
    {
        if (_rig == IntPtr.Zero) return;
        try
        {
            if (_pttOn) HamlibNative.rig_set_ptt(_rig, HamlibNative.RIG_VFO_CURR, 0);
            HamlibNative.rig_close(_rig);
            HamlibNative.rig_cleanup(_rig);
        }
        catch (Exception ex) { Log.Exception("rig_close", ex); }
        _rig = IntPtr.Zero;
        _pttOn = false;
    }

    private void Publish(RigState s)
    {
        State = s;
        try { StateChanged?.Invoke(s); } catch (Exception ex) { Log.Exception("Rig state", ex); }
    }

    public static string ModeName(ulong m)
    {
        string[] names = { "AM", "CW", "USB", "LSB", "RTTY", "FM", "WFM", "CWR", "RTTYR", "AMS", "PKTLSB", "PKTUSB", "PKTFM" };
        for (int i = 0; i < names.Length; i++) if ((m & (1UL << i)) != 0) return names[i];
        return m == 0 ? "" : "0x" + m.ToString("X");
    }

    public void Dispose()
    {
        _stop = true;
        var t = _thread; _thread = null;
        try { t?.Join(4000); } catch { }
        _queue.Dispose();
    }
}
