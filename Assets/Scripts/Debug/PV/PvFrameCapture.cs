#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// PV 촬영 전용(개발 전용) 실시간 캡처. 스스로 실행되지 않는다 — PvFrameCapture.Begin 을 부를 때만.
/// Unity Recorder 의 고정 프레임레이트(captureDeltaTime)는 에디터가 60 fps 를 못 낼 때 게임 시간만 느려지고
/// 비스케일/실시간 시계(연출 타임라인, 히트스톱, 봇 반응)는 그대로라 영상 속 연출이 빨라진다. 그래서 여기서는 게임을
/// 실시간(60 fps 목표)으로 두고, 매 프레임 화면을 AsyncGPUReadback 으로 읽어 ffmpeg(stdin, Intel QSV H.264)로 보낸다.
/// 출력은 실시간 기준 CFR 60 fps: 프레임이 늦으면 이전 프레임을 복제, 빠르면 건너뛴다(오디오와 같은 시계).
/// 오디오는 활성 AudioListener 에 PvAudioTap 을 붙여 같은 Stopwatch 기준 WAV 로 쓴다(리스너 교체 공백은 무음으로 메움).
/// </summary>
public sealed class PvFrameCapture : MonoBehaviour
{
    public static PvFrameCapture Active { get; private set; }
    public static readonly Stopwatch Clock = new Stopwatch();
    public static double Now => Clock.Elapsed.TotalSeconds;

    string _basePath;
    int _fps = 60, _w, _h;
    Process _ff;
    Stream _stdin;
    Thread _writer;
    readonly BlockingCollection<byte[]> _queue = new BlockingCollection<byte[]>(90);
    readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
    RenderTexture _rt;
    long _written;
    double _t0;
    int _prevTarget, _prevVsync;
    byte[] _last;
    public long FramesWritten => Interlocked.Read(ref _written);
    public int Duplicated { get; private set; }
    public int Skipped { get; private set; }
    public int Pending { get; private set; }

    /// <summary>현재 화면 크기로 녹화 시작. basePath + ".mp4"(영상) / "_audio.wav" / "_markers.txt".</summary>
    public static PvFrameCapture Begin(string basePath, string ffmpegExe, int quality = 17, int fps = 60)
    {
        End();
        var go = new GameObject("__PvFrameCapture");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        var c = go.AddComponent<PvFrameCapture>();
        c.Init(basePath, ffmpegExe, quality, fps);
        return c;
    }

    public static string End()
    {
        if (Active == null)
            return "not recording";
        string s = Active.Shutdown();
        Destroy(Active.gameObject);
        Active = null;
        return s;
    }

    void Init(string basePath, string ffmpegExe, int quality, int fps)
    {
        Active = this;
        _basePath = basePath;
        _fps = fps;
        _w = Screen.width & ~1;
        _h = Screen.height & ~1;
        Directory.CreateDirectory(Path.GetDirectoryName(basePath));
        _rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        _rt.Create();
        _prevTarget = Application.targetFrameRate;
        _prevVsync = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1; // 에디터의 60 캡은 페이싱이 거칠어 ~50 fps 가 된다 → 무제한으로 돌리고 슬롯마다 1장만 캡처

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgba -s {Screen.width}x{Screen.height} -r {fps} -i - " +
                        $"-vf crop={_w}:{_h}:0:0,format=nv12 -c:v h264_qsv -preset slow -global_quality {quality} -g {fps} " +
                        $"-movflags +faststart \"{basePath}.mp4\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        _ff = Process.Start(psi);
        string errPath = basePath + "_ffmpeg.txt";
        _ff.ErrorDataReceived += (s, e) => { if (e.Data != null) try { File.AppendAllText(errPath, e.Data + "\n"); } catch (IOException) { } };
        _ff.BeginErrorReadLine();
        _stdin = _ff.StandardInput.BaseStream;
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "PvFrameWriter" };
        _writer.Start();

        if (!Clock.IsRunning) Clock.Start();
        _t0 = Now;
        PvAudioTap.Begin(basePath + "_audio.wav", _t0);
        PvTakeMarkers.Begin(basePath + "_markers.txt", fps, () => (float)(Now - _t0));
        StartCoroutine(CaptureLoop());
        Debug.Log($"[PV] realtime capture -> {basePath}.mp4 {Screen.width}x{Screen.height}@{fps} QSV q{quality}");
    }

    void WriterLoop()
    {
        try
        {
            foreach (byte[] frame in _queue.GetConsumingEnumerable())
            {
                _stdin.Write(frame, 0, frame.Length);
                Interlocked.Increment(ref _written);
                _pool.Add(frame);
            }
        }
        catch (Exception e) { Debug.LogWarning("[PV] writer: " + e.Message); }
    }

    IEnumerator CaptureLoop()
    {
        var eof = new WaitForEndOfFrame();
        while (true)
        {
            yield return eof;
            if (Screen.width != _rt.width || Screen.height != _rt.height)
                continue; // 해상도 바뀌면 건너뜀(같은 크기로 돌아올 때까지)
            double t = Now - _t0;
            long slot = (long)Math.Floor(t * _fps);
            if (slot <= _lastSlot)
                continue; // 이 출력 슬롯은 이미 캡처함 (게임이 60 fps 보다 빠를 때)
            _lastSlot = slot;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);
            Pending++;
            AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, req => OnReadback(req, slot));
        }
    }

    long _lastSlot = -1;

    void OnReadback(AsyncGPUReadbackRequest req, long slot)
    {
        Pending--;
        if (req.hasError || _queue.IsAddingCompleted)
            return;
        NativeArray<byte> data = req.GetData<byte>();
        // 실시간 CFR: 이 프레임이 화면에 있어야 할 출력 프레임 수 = (t 기준 목표 개수) - (이미 보낸 개수)
        long due = slot + 1;
        long have = _sent;
        if (due <= have) { Skipped++; return; }
        long n = due - have;
        if (n > 1 && _last != null)
        {
            // 늦게 온 프레임: 빈 칸은 직전 프레임으로 채운다
            for (long k = 0; k < n - 1; k++) Enqueue(_last, false);
            Duplicated += (int)(n - 1);
        }
        if (!_pool.TryTake(out byte[] buf) || buf.Length != data.Length)
            buf = new byte[data.Length];
        data.CopyTo(buf);
        Enqueue(buf, true);
        _last = buf;
    }

    long _sent;

    void Enqueue(byte[] frame, bool owned)
    {
        byte[] f = frame;
        if (!owned)
        {
            if (!_pool.TryTake(out f) || f.Length != frame.Length) f = new byte[frame.Length];
            Buffer.BlockCopy(frame, 0, f, 0, frame.Length);
        }
        _queue.Add(f);
        _sent++;
    }

    string Shutdown()
    {
        StopAllCoroutines();
        AsyncGPUReadback.WaitAllRequests();
        PvTakeMarkers.End();
        string audio = PvAudioTap.End();
        _queue.CompleteAdding();
        _writer?.Join(20000);
        try { _stdin?.Flush(); _stdin?.Close(); } catch (IOException) { }
        bool exited = _ff != null && _ff.WaitForExit(60000);
        Application.targetFrameRate = _prevTarget;
        QualitySettings.vSyncCount = _prevVsync;
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
        string s = $"[PV] capture end {_basePath}.mp4 frames={_sent} ({_sent / (double)_fps:F1}s) dup={Duplicated} skip={Skipped} ffmpegExit={(exited ? _ff.ExitCode.ToString() : "timeout")} {audio}";
        Debug.Log(s);
        return s;
    }

    void OnDestroy()
    {
        if (Active == this)
        {
            Shutdown();
            Active = null;
        }
    }
}

/// <summary>활성 AudioListener 의 최종 믹스를 WAV(float32)로 기록. 리스너가 바뀌면(씬 로드) 새 리스너에 다시 붙고 공백은 무음.</summary>
public sealed class PvAudioTap : MonoBehaviour
{
    static readonly ConcurrentQueue<float[]> s_queue = new ConcurrentQueue<float[]>();
    static FileStream s_file;
    static BinaryWriter s_bw;
    static long s_samples; // per-channel frames written (file)
    static long s_received; // per-channel frames received (audio thread)
    static int s_channels, s_rate;
    static double s_t0, s_firstAt = -1;
    static PvAudioTap s_tap;
    static GameObject s_driver;
    static volatile bool s_on;

    public static void Begin(string path, double t0)
    {
        s_t0 = t0;
        s_rate = AudioSettings.outputSampleRate;
        s_channels = 0;
        s_samples = 0;
        s_received = 0;
        s_firstAt = -1;
        while (s_queue.TryDequeue(out _)) { }
        s_file = new FileStream(path, FileMode.Create, FileAccess.Write);
        s_bw = new BinaryWriter(s_file);
        s_bw.Write(new byte[44]); // header written at End
        s_on = true;
        s_driver = new GameObject("__PvAudioDriver");
        s_driver.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(s_driver);
        s_driver.AddComponent<PvAudioDriver>();
    }

    public static string End()
    {
        if (s_bw == null)
            return "audio: none";
        s_on = false;
        Drain();
        if (s_tap != null) Destroy(s_tap);
        if (s_driver != null) Destroy(s_driver);
        int ch = Math.Max(1, s_channels);
        long dataBytes = s_samples * ch * 4;
        s_bw.Seek(0, SeekOrigin.Begin);
        s_bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        s_bw.Write((int)(36 + dataBytes));
        s_bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        s_bw.Write(16); s_bw.Write((short)3); s_bw.Write((short)ch); s_bw.Write(s_rate);
        s_bw.Write(s_rate * ch * 4); s_bw.Write((short)(ch * 4)); s_bw.Write((short)32);
        s_bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        s_bw.Write((int)dataBytes);
        s_bw.Close();
        s_bw = null; s_file = null;
        return $"audio: {s_samples / (double)s_rate:F1}s {ch}ch {s_rate}Hz";
    }

    /// <summary>메인 스레드: 큐 비우기 + 리스너 추적 + 공백 메우기.</summary>
    internal static void Drain()
    {
        if (s_bw == null) return;
        while (s_queue.TryDequeue(out float[] buf))
        {
            for (int i = 0; i < buf.Length; i++) s_bw.Write(buf[i]);
            if (s_channels > 0) s_samples += buf.Length / s_channels;
        }
        // 리스너가 없거나 교체 중이면 실시간 기준으로 무음 채움(오디오 시계 = Stopwatch)
        if (s_channels > 0 && s_on)
        {
            long due = (long)((PvFrameCapture.Now - s_t0) * s_rate);
            long lag = due - (s_samples + 0);
            long inFlight = Interlocked.Read(ref s_received) - s_samples;
            if (lag - inFlight > s_rate / 5) // 0.2 s 넘게 비면 메움
            {
                long fill = lag - inFlight - s_rate / 20;
                var zeros = new byte[4 * s_channels];
                for (long k = 0; k < fill; k++) s_bw.Write(zeros);
                s_samples += fill;
                Interlocked.Add(ref s_received, fill);
            }
        }
    }

    internal static void Track()
    {
        AudioListener l = FindObjectOfType<AudioListener>();
        if (l == null || (s_tap != null && s_tap.gameObject == l.gameObject && s_tap.isActiveAndEnabled))
            return;
        if (s_tap != null) Destroy(s_tap);
        s_tap = l.gameObject.AddComponent<PvAudioTap>();
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!s_on || s_tap != this) return;
        if (s_channels == 0)
        {
            s_channels = channels;
            // 첫 버퍼: 앞부분 공백을 무음으로(이 버퍼는 지금 재생 중인 것)
            long lead = (long)((PvFrameCapture.Now - s_t0) * s_rate) - data.Length / channels;
            if (lead > 0) s_queue.Enqueue(new float[lead * channels]);
            Interlocked.Add(ref s_received, Math.Max(0, lead));
        }
        if (channels != s_channels) return;
        var copy = new float[data.Length];
        Array.Copy(data, copy, data.Length);
        s_queue.Enqueue(copy);
        Interlocked.Add(ref s_received, data.Length / channels);
    }
}

sealed class PvAudioDriver : MonoBehaviour
{
    void Update()
    {
        PvAudioTap.Track();
        PvAudioTap.Drain();
    }
}
#endif
