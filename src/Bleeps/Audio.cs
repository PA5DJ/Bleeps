using System.Runtime.InteropServices;

namespace Bleeps;

// Sound in and out through WinMM - the same code as ShackAssistant's WaveInCapture / WaveOutPlayer / SstvTransmitter, without
// the radio's network audio (Icom/Yaesu LAN). No extra audio library and no extra DLL in the installer.

/// <summary>Sound-card input: mono float samples (-1..+1), 48 kHz, raised on the capture thread.</summary>
public sealed class WaveIn : IDisposable
{
    public const int SampleRate = 48000;
    public event Action<float[]>? SamplesAvailable;
    public event Action<string>? Failed;
    public bool IsRunning { get; private set; }

    private readonly int _device;
    private const int BufferSamples = 4096, BufferCount = 4;
    private IntPtr _h, _event;
    private Thread? _worker;
    private volatile bool _stop;
    private IntPtr[] _headers = Array.Empty<IntPtr>(), _buffers = Array.Empty<IntPtr>();
    private int _channels = 1;

    public WaveIn(int device) { _device = device; }

    /// <summary>
    /// Which device of <paramref name="devices"/> is meant (v1.0.24): the one with the saved NAME, else the saved number.
    /// WinMM numbers the cards in the order Windows found them, so a number alone moves when a USB radio (IC-705, IC-9700 ...)
    /// is plugged in or out - the cause of "settings not loaded right". Windows keeps two equal cards apart in the name
    /// ("2- USB Audio CODEC"), so the name is the steadier of the two.
    /// </summary>
    public static int Resolve(IReadOnlyList<string> devices, string savedName, int savedIndex)
    {
        if (devices.Count == 0) return -1;
        if (savedName.Length > 0)
        {
            int byName = devices.ToList().FindIndex(d => d.Equals(savedName, StringComparison.OrdinalIgnoreCase));
            if (byName >= 0) return byName;
        }
        return Math.Clamp(savedIndex, 0, devices.Count - 1);
    }

    public static List<string> Devices()
    {
        var list = new List<string>();
        int n = waveInGetNumDevs();
        for (int i = 0; i < n; i++)
        {
            var caps = new WAVEINCAPS();
            list.Add(waveInGetDevCapsW((IntPtr)i, ref caps, Marshal.SizeOf<WAVEINCAPS>()) == 0 ? caps.szPname : $"Device {i}");
        }
        return list;
    }

    public void Start()
    {
        if (IsRunning) return;
        if (!TryOpen(1) && !TryOpen(2))
            throw new InvalidOperationException("Could not open the audio input at 48 kHz. Another program may be using it.");
        _headers = new IntPtr[BufferCount];
        _buffers = new IntPtr[BufferCount];
        int bytes = BufferSamples * _channels * 2;
        for (int i = 0; i < BufferCount; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(bytes);
            var hdr = new WAVEHDR { lpData = _buffers[i], dwBufferLength = (uint)bytes };
            _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
            Marshal.StructureToPtr(hdr, _headers[i], false);
            waveInPrepareHeader(_h, _headers[i], Marshal.SizeOf<WAVEHDR>());
            waveInAddBuffer(_h, _headers[i], Marshal.SizeOf<WAVEHDR>());
        }
        _stop = false;
        IsRunning = true;
        _worker = new Thread(Drain) { IsBackground = true, Name = "Bleeps audio in", Priority = ThreadPriority.AboveNormal };
        _worker.Start();
        waveInStart(_h);
        Log.Info($"Audio input started: device {_device}, {_channels} ch");
    }

    private bool TryOpen(int channels)
    {
        var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = (ushort)channels, nSamplesPerSec = SampleRate, wBitsPerSample = 16,
                                     nBlockAlign = (ushort)(channels * 2), nAvgBytesPerSec = (uint)(SampleRate * channels * 2) };
        if (_event == IntPtr.Zero) _event = CreateEvent(IntPtr.Zero, false, false, null);
        if (waveInOpen(out _h, (IntPtr)_device, ref fmt, _event, IntPtr.Zero, 0x00050000) != 0) { _h = IntPtr.Zero; return false; }
        _channels = channels;
        return true;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _stop = true;
        if (_event != IntPtr.Zero) SetEvent(_event);
        try { if (_h != IntPtr.Zero) { waveInStop(_h); waveInReset(_h); } } catch { }
        try { _worker?.Join(1000); } catch { }
        _worker = null;
        for (int i = 0; i < _headers.Length; i++)
        {
            if (_headers[i] == IntPtr.Zero) continue;
            try { waveInUnprepareHeader(_h, _headers[i], Marshal.SizeOf<WAVEHDR>()); } catch { }
            Marshal.FreeHGlobal(_headers[i]);
            if (_buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_buffers[i]);
        }
        _headers = Array.Empty<IntPtr>(); _buffers = Array.Empty<IntPtr>();
        if (_h != IntPtr.Zero) { try { waveInClose(_h); } catch { } _h = IntPtr.Zero; }
        if (_event != IntPtr.Zero) { CloseHandle(_event); _event = IntPtr.Zero; }
    }

    /// <summary>The buffers are handed on in the order they were queued (ShackAssistant v1.0.725: out of order made the
    /// waterfall sway).</summary>
    private void Drain()
    {
        int hdrSize = Marshal.SizeOf<WAVEHDR>();
        int next = 0;
        try
        {
            while (!_stop)
            {
                WaitForSingleObject(_event, 200);
                if (_stop) break;
                for (int n = 0; n < _headers.Length && !_stop; n++)
                {
                    int i = next;
                    var hdr = Marshal.PtrToStructure<WAVEHDR>(_headers[i]);
                    if ((hdr.dwFlags & 1) == 0) break;
                    next = (next + 1) % _headers.Length;
                    int frames = (int)hdr.dwBytesRecorded / (2 * _channels);
                    if (frames > 0)
                    {
                        var slice = new float[frames];
                        unsafe
                        {
                            short* p = (short*)hdr.lpData;
                            if (_channels == 1) for (int s = 0; s < frames; s++) slice[s] = p[s] / 32768f;
                            else for (int s = 0; s < frames; s++) slice[s] = (p[s * 2] + p[s * 2 + 1]) / 65536f;
                        }
                        try { SamplesAvailable?.Invoke(slice); } catch (Exception ex) { Log.Exception("Audio consumer", ex); }
                    }
                    hdr.dwFlags &= ~1u;
                    hdr.dwBytesRecorded = 0;
                    Marshal.StructureToPtr(hdr, _headers[i], false);
                    if (!_stop) waveInAddBuffer(_h, _headers[i], hdrSize);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Exception("Audio input", ex);
            IsRunning = false;
            try { Failed?.Invoke(ex.Message); } catch { }
        }
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    internal struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEINCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwFormats; public ushort wChannels, wReserved1; }

    [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int waveInGetDevCapsW(IntPtr device, ref WAVEINCAPS caps, int size);
    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr h, IntPtr device, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveInStop(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr CreateEvent(IntPtr a, bool manual, bool initial, string? name);
    [DllImport("kernel32.dll")] internal static extern bool SetEvent(IntPtr h);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr h, uint ms);
}

/// <summary>Sound-card output that PULLS: the card's clock does the timing.</summary>
public sealed class WaveOut : IDisposable
{
    public const int SampleRate = 48000;
    public delegate int FillCallback(short[] buffer, int count);
    public event Action<string>? Failed;
    public bool IsRunning { get; private set; }

    private const int BufferCount = 4;
    private readonly int _device, _bufferSamples;
    private IntPtr _h, _event;
    private Thread? _worker;
    private volatile bool _stop;
    private FillCallback? _fill;
    private readonly IntPtr[] _headers = new IntPtr[BufferCount], _data = new IntPtr[BufferCount];
    private readonly object _lock = new();

    public WaveOut(int device, int bufferMs = 100)
    {
        _device = device;
        _bufferSamples = Math.Max(256, SampleRate * bufferMs / 1000);
    }

    /// <summary>How much audio the card holds at most: how long PTT still has to stay down after the last hand-over.</summary>
    public int BufferedMs => (int)(1000L * BufferCount * _bufferSamples / SampleRate);

    public static List<string> Devices()
    {
        var list = new List<string>();
        int n = waveOutGetNumDevs();
        for (int i = 0; i < n; i++)
        {
            var caps = new WAVEOUTCAPS();
            list.Add(waveOutGetDevCapsW(i, ref caps, Marshal.SizeOf<WAVEOUTCAPS>()) == 0 ? caps.szPname : $"Device {i}");
        }
        return list;
    }

    /// <summary>How much the card has PLAYED (its own clock), in ms; -1 when it will not say.</summary>
    public int PlayedMs
    {
        get
        {
            lock (_lock)
            {
                if (_h == IntPtr.Zero) return -1;
                var t = new MMTIME { wType = 2 };
                if (waveOutGetPosition(_h, ref t, Marshal.SizeOf<MMTIME>()) != 0) return -1;
                long samples = t.wType switch { 2 => t.u, 4 => t.u / 2, _ => -1 };
                return samples < 0 ? -1 : (int)(1000L * samples / SampleRate);
            }
        }
    }

    public void Start(FillCallback fill)
    {
        if (IsRunning) return;
        _fill = fill;
        _stop = false;
        var fmt = new WaveIn.WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = SampleRate * 2 };
        _event = WaveIn.CreateEvent(IntPtr.Zero, false, false, null);
        int r = waveOutOpen(out _h, _device, ref fmt, _event, IntPtr.Zero, 0x00050000);
        if (r != 0)
        {
            WaveIn.CloseHandle(_event); _event = IntPtr.Zero;
            var all = Devices();
            string name = _device >= 0 && _device < all.Count ? $"\"{all[_device]}\"" : $"card {_device}";
            throw new InvalidOperationException(r switch
            {
                2 => $"Cannot open {name}: that card is not there.",
                4 => $"Cannot open {name}: another program has it.",
                32 => $"Cannot open {name}: it will not take 48 kHz mono.",
                _ => $"Cannot open {name} (waveOutOpen returned {r}).",
            });
        }
        int bytes = _bufferSamples * 2;
        for (int i = 0; i < BufferCount; i++)
        {
            _data[i] = Marshal.AllocHGlobal(bytes);
            _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WaveIn.WAVEHDR>());
            Marshal.StructureToPtr(new WaveIn.WAVEHDR { lpData = _data[i], dwBufferLength = (uint)bytes }, _headers[i], false);
            waveOutPrepareHeader(_h, _headers[i], Marshal.SizeOf<WaveIn.WAVEHDR>());
        }
        IsRunning = true;
        _worker = new Thread(Pump) { IsBackground = true, Name = "Bleeps audio out", Priority = ThreadPriority.AboveNormal };
        _worker.Start();
    }

    public void Stop()
    {
        if (!IsRunning && _h == IntPtr.Zero) return;
        _stop = true;
        if (_event != IntPtr.Zero) WaveIn.SetEvent(_event);
        var w = _worker; _worker = null;
        try { if (w != Thread.CurrentThread) w?.Join(500); } catch { }
        lock (_lock)
        {
            if (_h != IntPtr.Zero)
            {
                waveOutReset(_h);
                for (int i = 0; i < BufferCount; i++)
                {
                    if (_headers[i] == IntPtr.Zero) continue;
                    waveOutUnprepareHeader(_h, _headers[i], Marshal.SizeOf<WaveIn.WAVEHDR>());
                    Marshal.FreeHGlobal(_headers[i]); _headers[i] = IntPtr.Zero;
                    Marshal.FreeHGlobal(_data[i]); _data[i] = IntPtr.Zero;
                }
                waveOutClose(_h);
                _h = IntPtr.Zero;
            }
        }
        if (_event != IntPtr.Zero) { WaveIn.CloseHandle(_event); _event = IntPtr.Zero; }
        IsRunning = false;
    }

    private void Pump()
    {
        var scratch = new short[_bufferSamples];
        var busy = new bool[BufferCount];
        try
        {
            for (int i = 0; i < BufferCount; i++) busy[i] = Write(i, scratch);
            while (!_stop)
            {
                WaveIn.WaitForSingleObject(_event, 100);
                if (_stop) break;
                bool any = false;
                for (int i = 0; i < BufferCount; i++)
                {
                    if (!busy[i]) continue;
                    var hdr = Marshal.PtrToStructure<WaveIn.WAVEHDR>(_headers[i]);
                    if ((hdr.dwFlags & 1) == 0) { any = true; continue; }
                    busy[i] = Write(i, scratch);
                    any |= busy[i];
                }
                if (!any) break;
            }
        }
        catch (Exception ex) { Failed?.Invoke(ex.Message); }
    }

    private bool Write(int i, short[] scratch)
    {
        int got = _fill?.Invoke(scratch, scratch.Length) ?? 0;
        if (got <= 0) return false;
        for (int k = got; k < scratch.Length; k++) scratch[k] = 0;
        lock (_lock)
        {
            if (_h == IntPtr.Zero || _headers[i] == IntPtr.Zero) return false;
            Marshal.Copy(scratch, 0, _data[i], scratch.Length);
            var hdr = Marshal.PtrToStructure<WaveIn.WAVEHDR>(_headers[i]);
            hdr.dwFlags &= ~1u;
            hdr.dwBufferLength = (uint)(scratch.Length * 2);
            Marshal.StructureToPtr(hdr, _headers[i], false);
            return waveOutWrite(_h, _headers[i], Marshal.SizeOf<WaveIn.WAVEHDR>()) == 0;
        }
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)] private struct MMTIME { public uint wType, u, pad1; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEOUTCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwFormats; public ushort wChannels, wReserved1; public uint dwSupport; }

    [DllImport("winmm.dll")] private static extern int waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int waveOutGetDevCapsW(int id, ref WAVEOUTCAPS caps, int size);
    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr h, int device, ref WaveIn.WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveOutGetPosition(IntPtr h, ref MMTIME t, int size);
}

/// <summary>
/// Plays one message with PTT around it, on its own thread; the card's clock does the timing and PTT is ALWAYS let go in
/// the finally - also on Stop and on a failure (ShackAssistant's SstvTransmitter).
/// </summary>
public sealed class TxPlayer : IDisposable
{
    /// <summary>On the worker thread when it ends: null = ran out, else why it stopped.</summary>
    public event Action<string?>? Finished;
    public bool IsRunning => _thread != null;
    public double TotalSeconds { get; private set; }
    public double PlayedSeconds => Math.Min(TotalSeconds, Math.Max(0, _player?.PlayedMs ?? 0) / 1000.0);
    /// <summary>The TX drive, 0..1, read for every buffer - turning the slider while sending works.</summary>
    public double Volume { get => _volume; set => _volume = (float)Math.Clamp(value, 0, 1); }
    public double OutputPeak => _peak;

    private Thread? _thread;
    private WaveOut? _player;
    private volatile bool _abort;
    private volatile float _volume = 1f, _peak;

    public void Start(short[] samples, int device, Action<bool>? ptt, int leadMs, int tailMs)
    {
        if (IsRunning) return;
        _abort = false;
        TotalSeconds = samples.Length / (double)WaveOut.SampleRate;
        _thread = new Thread(() => Run(samples, device, ptt, leadMs, tailMs)) { IsBackground = true, Name = "Bleeps TX" };
        _thread.Start();
    }

    public void Stop() => _abort = true;

    private void Run(short[] samples, int device, Action<bool>? ptt, int leadMs, int tailMs)
    {
        string? why = null;
        try
        {
            ptt?.Invoke(true);
            if (leadMs > 0) Thread.Sleep(leadMs);
            using var player = new WaveOut(device, 100);
            _player = player;
            var done = new ManualResetEventSlim();
            player.Failed += m => { why = m; done.Set(); };
            int at = 0;
            player.Start((buf, count) =>
            {
                if (_abort || at >= samples.Length) { done.Set(); return 0; }
                int n = Math.Min(count, samples.Length - at);
                float v = _volume; int peak = 0;
                for (int i = 0; i < n; i++)
                {
                    int s = (int)Math.Round(samples[at + i] * v);
                    buf[i] = (short)Math.Clamp(s, short.MinValue, short.MaxValue);
                    if (Math.Abs(s) > peak) peak = Math.Abs(s);
                }
                _peak = peak / 32767f;
                if (n < count) Array.Clear(buf, n, count - n);
                at += n;
                return count;
            });
            while (!done.Wait(50)) { if (_abort) break; }
            if (!_abort) Thread.Sleep(player.BufferedMs + 50);
            player.Stop();
            if (_abort && why == null) why = "stopped";
            if (tailMs > 0 && !_abort) Thread.Sleep(tailMs);
        }
        catch (Exception ex)
        {
            Log.Exception("Transmit", ex);
            why = ex.Message;
        }
        finally
        {
            try { ptt?.Invoke(false); } catch (Exception ex) { Log.Exception("PTT off", ex); }
            _peak = 0;
            _player = null;
            _thread = null;
            Finished?.Invoke(why);
        }
    }

    public void Dispose() => Stop();
}
