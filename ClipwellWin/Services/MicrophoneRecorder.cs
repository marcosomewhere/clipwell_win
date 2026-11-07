using System.Runtime.InteropServices;

namespace ClipwellWin.Services;

/// <summary>
/// Captures the default microphone as 16 kHz mono PCM via the waveIn API; the wave mapper resamples,
/// so the samples can go to Whisper without further conversion. Uses event notification plus a capture
/// thread because waveIn functions must not be called from inside a waveIn callback.
/// </summary>
public sealed class MicrophoneRecorder : IDisposable
{
    public const int SampleRate = 16000;
    private const int BufferCount = 4;
    private const int BufferMilliseconds = 100;
    private const int BytesPerSample = 2;
    private const int BufferBytes = SampleRate * BytesPerSample * BufferMilliseconds / 1000;
    private const uint WAVE_MAPPER = unchecked((uint)-1);
    private const uint CALLBACK_EVENT = 0x00050000;
    private const uint WHDR_DONE = 0x00000001;
    private const int MaxSeconds = 30 * 60;

    private readonly object _lock = new();
    private readonly List<float> _samples = [];
    private IntPtr _handle;
    private IntPtr[] _headers = [];
    private IntPtr[] _buffers = [];
    private AutoResetEvent? _dataReady;
    private Thread? _thread;
    private volatile bool _recording;

    public bool IsRecording => _recording;

    public TimeSpan Elapsed
    {
        get { lock (_lock) return TimeSpan.FromSeconds((double)_samples.Count / SampleRate); }
    }

    public static bool HasInputDevice() => waveInGetNumDevs() > 0;

    public void Start()
    {
        if (_recording) return;
        lock (_lock) _samples.Clear();

        var format = new WaveFormatEx
        {
            wFormatTag = 1,
            nChannels = 1,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = BytesPerSample,
            nAvgBytesPerSec = SampleRate * BytesPerSample,
            cbSize = 0,
        };
        _dataReady = new AutoResetEvent(false);
        var opened = waveInOpen(out _handle, WAVE_MAPPER, ref format,
            _dataReady.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
        if (opened != 0)
        {
            _handle = IntPtr.Zero;
            _dataReady.Dispose();
            _dataReady = null;
            Check(opened, "waveInOpen");
        }

        try
        {
            _headers = new IntPtr[BufferCount];
            _buffers = new IntPtr[BufferCount];
            var headerSize = Marshal.SizeOf<WaveHdr>();
            for (var i = 0; i < BufferCount; i++)
            {
                _buffers[i] = Marshal.AllocHGlobal(BufferBytes);
                _headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHdr { lpData = _buffers[i], dwBufferLength = BufferBytes }, _headers[i], false);
                Check(waveInPrepareHeader(_handle, _headers[i], headerSize), "waveInPrepareHeader");
                Check(waveInAddBuffer(_handle, _headers[i], headerSize), "waveInAddBuffer");
            }
            Check(waveInStart(_handle), "waveInStart");
        }
        catch
        {
            waveInReset(_handle);
            Release();
            throw;
        }

        _recording = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Clipwell microphone" };
        _thread.Start();
    }

    /// <summary>Stops capturing and returns the recorded samples as floats in -1..1.</summary>
    public float[] Stop()
    {
        if (!_recording) return [];
        _recording = false;
        _dataReady?.Set();
        _thread?.Join();
        _thread = null;

        waveInReset(_handle);
        CollectDoneBuffers(requeue: false);
        Release();
        lock (_lock)
        {
            var result = _samples.ToArray();
            _samples.Clear();
            return result;
        }
    }

    private void CaptureLoop()
    {
        while (_recording)
        {
            _dataReady?.WaitOne(200);
            if (!_recording) break;
            CollectDoneBuffers(requeue: true);
        }
    }

    private void CollectDoneBuffers(bool requeue)
    {
        var headerSize = Marshal.SizeOf<WaveHdr>();
        foreach (var ptr in _headers)
        {
            if (ptr == IntPtr.Zero) continue;
            var header = Marshal.PtrToStructure<WaveHdr>(ptr);
            if ((header.dwFlags & WHDR_DONE) == 0) continue;

            Append(header.lpData, (int)header.dwBytesRecorded / BytesPerSample);
            header.dwFlags &= ~WHDR_DONE;
            header.dwBytesRecorded = 0;
            Marshal.StructureToPtr(header, ptr, false);
            if (requeue) waveInAddBuffer(_handle, ptr, headerSize);
        }
    }

    private void Append(IntPtr data, int count)
    {
        if (count <= 0) return;
        var raw = new short[count];
        Marshal.Copy(data, raw, 0, count);
        lock (_lock)
        {
            if (_samples.Count >= SampleRate * MaxSeconds) return;
            foreach (var s in raw) _samples.Add(s / 32768f);
        }
    }

    private void Release()
    {
        var headerSize = Marshal.SizeOf<WaveHdr>();
        for (var i = 0; i < _headers.Length; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                waveInUnprepareHeader(_handle, _headers[i], headerSize);
                Marshal.FreeHGlobal(_headers[i]);
            }
            if (_buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_buffers[i]);
        }
        _headers = [];
        _buffers = [];
        if (_handle != IntPtr.Zero)
        {
            waveInClose(_handle);
            _handle = IntPtr.Zero;
        }
        _dataReady?.Dispose();
        _dataReady = null;
    }

    private static void Check(uint result, string call)
    {
        if (result != 0)
            throw new InvalidOperationException(
                $"{call} fehlgeschlagen (MMRESULT {result}). Ist ein Mikrofon angeschlossen und für Desktop-Apps freigegeben?");
    }

    public void Dispose()
    {
        if (_recording) Stop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr phwi, uint uDeviceID, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr hwi);
}
