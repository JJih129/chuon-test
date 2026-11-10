using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// PV(홍보 영상) 촬영용 에디터 도우미. 메뉴/MCP 에서 직접 부를 때만 동작한다(로드 시 자동 실행 없음).
/// Play 모드에서 Unity Recorder 로 Game View 를 1920x1080 고정 60 fps(Constant, CapFrameRate)로
/// H.264 고비트레이트(+오디오) MP4 로 녹화해 프로젝트 루트 Recordings/PV/ (Assets 밖, 임포트 안 됨)에 저장한다.
/// 같은 이름의 _markers.txt 에 PvTakeMarkers 이벤트(패링·회피·연출·궁극기 등, 영상 시각)를 남긴다.
/// </summary>
public static class PvRecorder
{
    static RecorderController s_controller;
    static string s_take;

    public static string OutputDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "PV"));
    public static bool IsRecording => s_controller != null && s_controller.IsRecording();
    public static string CurrentTake => s_take;

    /// <summary>Play 모드에서 촬영 시작. 반환: 출력 파일 경로(확장자 제외) 또는 null.</summary>
    public static string StartTake(string take, bool audio = true, float fps = 60f, float mbps = 80f, int width = 1920, int height = 1080)
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[PV] StartTake: Play 모드에서만 녹화할 수 있다.");
            return null;
        }
        if (IsRecording)
            StopTake();

        Directory.CreateDirectory(OutputDir);
        string basePath = Path.Combine(OutputDir, take);

        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "PV_" + take;
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom,
            TargetBitRate = mbps,
            GopSize = 30,
            KeyframeDistance = 30,
            NumConsecutiveBFrames = 0,
            EncodingProfile = CoreEncoderSettings.H264EncodingProfile.High
        };
        movie.CaptureAudio = audio;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
        movie.OutputFile = basePath;

        controllerSettings.AddRecorderSettings(movie);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
        controllerSettings.FrameRate = fps;
        controllerSettings.CapFrameRate = true;

        s_controller = new RecorderController(controllerSettings);
        s_controller.PrepareRecording();
        if (!s_controller.StartRecording())
        {
            Debug.LogWarning("[PV] StartTake: Recorder 시작 실패");
            s_controller = null;
            return null;
        }
        s_take = take;
        PvTakeMarkers.Begin(basePath + "_markers.txt", fps);
        Debug.Log($"[PV] recording '{take}' -> {basePath}.mp4 ({width}x{height} @ {fps} fps, {mbps} Mbps, audio={audio})");
        return basePath;
    }

    public static string StopTake()
    {
        string take = s_take;
        PvTakeMarkers.End();
        if (s_controller != null)
        {
            if (s_controller.IsRecording())
                s_controller.StopRecording();
            s_controller = null;
        }
        s_take = null;
        Debug.Log($"[PV] stopped '{take}'");
        return take;
    }

    /// <summary>ffmpeg.exe (PATH 또는 winget Gyan.FFmpeg).</summary>
    public static string FindFfmpeg()
    {
        string pkgs = System.Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Packages");
        if (Directory.Exists(pkgs))
            foreach (string dir in Directory.GetDirectories(pkgs, "Gyan.FFmpeg*"))
                foreach (string exe in Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories))
                    return exe;
        return "ffmpeg";
    }

    /// <summary>
    /// 실시간 캡처(권장): 게임은 실시간 60 fps 목표로 돌고, 화면을 직접 읽어 ffmpeg(QSV)로 인코딩한다.
    /// 에디터가 60 fps 를 못 낼 때 Recorder 고정 프레임 모드에서 생기는 연출 가속(비스케일 시계) 문제가 없다.
    /// </summary>
    public static string StartRealtimeTake(string take, int quality = 17)
    {
        if (!EditorApplication.isPlaying)
            return null;
        string basePath = Path.Combine(OutputDir, take);
        PvFrameCapture.Begin(basePath, FindFfmpeg(), quality);
        s_take = take;
        return basePath;
    }

    public static string StopRealtimeTake()
    {
        s_take = null;
        return PvFrameCapture.End();
    }

    [MenuItem("Tools/Chuon/PV/Start Realtime Take (Play mode)")]
    static void MenuStartRt() => StartRealtimeTake("rt_" + System.DateTime.Now.ToString("HHmmss"));

    [MenuItem("Tools/Chuon/PV/Start Realtime Take (Play mode)", true)]
    static bool MenuStartRtValidate() => EditorApplication.isPlaying && PvFrameCapture.Active == null;

    [MenuItem("Tools/Chuon/PV/Stop Realtime Take")]
    static void MenuStopRt() => StopRealtimeTake();

    [MenuItem("Tools/Chuon/PV/Stop Realtime Take", true)]
    static bool MenuStopRtValidate() => PvFrameCapture.Active != null;

    [MenuItem("Tools/Chuon/PV/Start Take (Play mode)")]
    static void MenuStart() => StartTake("take_" + System.DateTime.Now.ToString("HHmmss"));

    [MenuItem("Tools/Chuon/PV/Start Take (Play mode)", true)]
    static bool MenuStartValidate() => EditorApplication.isPlaying && !IsRecording;

    [MenuItem("Tools/Chuon/PV/Stop Take")]
    static void MenuStop() => StopTake();

    [MenuItem("Tools/Chuon/PV/Stop Take", true)]
    static bool MenuStopValidate() => IsRecording;
}
