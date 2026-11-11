#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 반복 22 (개발 전용): 최종 출력 라우드니스 계측. 스스로 실행되지 않는다 — QaCommandLine 의 -qaAudioMeter 또는 MCP 에서 Ensure().
/// 매 프레임 AudioListener.GetOutputData(L/R, 1024 샘플)로 RMS·피크를 모아 1 s 마다 한 줄씩 Temp/qa/audio_meter.txt 에 쓴다:
///   t  scene  section  rmsDb  peakDb  music=트랙/상태/덕킹
/// section = 연출 ID(재생 중) / 일시정지 / 그 외 "play". 마지막에 Report() 가 클리핑(피크 > -1 dBFS)·장면 간 6 dB 이상 점프·
/// 3 s 이상 무음(-50 dBFS 미만) 구간을 요약한다.
/// </summary>
public sealed class QaAudioMeter : MonoBehaviour
{
    const int N = 1024;
    const float SilentDb = -50f;
    static QaAudioMeter s_instance;

    readonly float[] _l = new float[N];
    readonly float[] _r = new float[N];
    double _sumSq;
    long _count;
    float _peak;
    float _secondStart;
    readonly StringBuilder _lines = new StringBuilder();
    readonly StringBuilder _issues = new StringBuilder();
    // 장면별 평균 (활성 구간만, 무음 제외)
    string _sceneKey;
    double _sceneSum;
    int _sceneN;
    readonly StringBuilder _sceneAvg = new StringBuilder();
    float _silentSince = -1f;
    string _silentLabel;
    int _clipSeconds, _silentRuns, _jumps;
    float _prevSceneAvgDb = float.NaN;
    string _prevSceneKey;

    public static string OutputPath => System.IO.Path.Combine(QaCombatBot.OutputDir, "audio_meter.txt");

    public static QaAudioMeter Ensure()
    {
        if (s_instance != null)
            return s_instance;
        var go = new GameObject("__QaAudioMeter");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<QaAudioMeter>();
        return s_instance;
    }

    public static string ReportNow() => s_instance != null ? s_instance.Report() : "[QA-AUDIO] meter not running";

    void Start()
    {
        _secondStart = Time.realtimeSinceStartup;
        _lines.AppendLine("t\tscene\tsection\trmsDb\tpeakDb\tmusic");
    }

    static float Db(double linear) => linear <= 1e-7 ? -140f : 20f * (float)System.Math.Log10(linear);

    void Update()
    {
        AudioListener.GetOutputData(_l, 0);
        AudioListener.GetOutputData(_r, 1);
        // 프레임 간 창이 겹치므로(1024 샘플 ≈ 21 ms > 16.7 ms) 프레임 길이만큼의 최신 샘플만 더해 이중 계산을 줄인다
        int fresh = Mathf.Clamp(Mathf.RoundToInt(Time.unscaledDeltaTime * AudioSettings.outputSampleRate), 64, N);
        for (int i = N - fresh; i < N; i++)
        {
            float a = _l[i], b = _r[i];
            _sumSq += a * a + b * b;
            float m = Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
            if (m > _peak) _peak = m;
        }
        _count += 2 * fresh;

        float now = Time.realtimeSinceStartup;
        if (now - _secondStart >= 1f)
            FlushSecond(now);
    }

    string Section()
    {
        if (CinematicDirector.IsPlaying) return "cine:" + CinematicDirector.CurrentId;
        PauseManager pm = PauseManager.Instance;
        if (pm != null && pm.IsPaused) return "paused";
        return "play";
    }

    void FlushSecond(float now)
    {
        float rms = _count > 0 ? Db(System.Math.Sqrt(_sumSq / _count)) : -140f;
        float peak = Db(_peak);
        string scene = SceneManager.GetActiveScene().name;
        string section = Section();
        float t = now - (s_startAt < 0f ? (s_startAt = now) : s_startAt);
        _lines.Append(t.ToString("F0")).Append('\t').Append(scene).Append('\t').Append(section).Append('\t')
              .Append(rms.ToString("F1")).Append('\t').Append(peak.ToString("F1")).Append('\t')
              .Append(MusicDirector.CurrentTrack).Append('/').Append(MusicDirector.CurrentState).Append('/').Append(MusicDirector.CurrentDuckDb.ToString("F0")).Append('\n');

        string label = $"{scene}/{section}";
        if (peak > -1f)
        {
            _clipSeconds++;
            if (_clipSeconds <= 30) _issues.AppendLine($"  CLIP t={t:F0}s {label} peak={peak:F1} dBFS rms={rms:F1}");
        }
        if (rms < SilentDb)
        {
            if (_silentSince < 0f) { _silentSince = t; _silentLabel = label; }
        }
        else
            EndSilence(t);

        // 장면(씬+섹션) 평균: 섹션이 바뀌면 이전 평균을 기록하고 점프 검사
        string key = label;
        if (key != _sceneKey)
        {
            CloseSceneAvg();
            _sceneKey = key;
        }
        if (rms >= SilentDb) { _sceneSum += System.Math.Pow(10.0, rms / 10.0); _sceneN++; }

        _sumSq = 0d;
        _count = 0;
        _peak = 0f;
        _secondStart = now;
    }
    static float s_startAt = -1f;

    void EndSilence(float t)
    {
        if (_silentSince < 0f) return;
        float len = t - _silentSince;
        if (len > 3f)
        {
            _silentRuns++;
            _issues.AppendLine($"  SILENT {len:F0}s from t={_silentSince:F0}s at {_silentLabel}");
        }
        _silentSince = -1f;
    }

    void CloseSceneAvg()
    {
        if (_sceneKey == null || _sceneN == 0) { _sceneSum = 0d; _sceneN = 0; return; }
        float avg = 10f * (float)System.Math.Log10(_sceneSum / _sceneN);
        _sceneAvg.AppendLine($"  {_sceneKey,-46} n={_sceneN,3}s avg={avg:F1} dB");
        if (!float.IsNaN(_prevSceneAvgDb) && _sceneN >= 3 && Mathf.Abs(avg - _prevSceneAvgDb) > 6f)
        {
            _jumps++;
            _issues.AppendLine($"  JUMP {_prevSceneKey} ({_prevSceneAvgDb:F1}) -> {_sceneKey} ({avg:F1}) = {avg - _prevSceneAvgDb:+0.0;-0.0} dB");
        }
        if (_sceneN >= 3) { _prevSceneAvgDb = avg; _prevSceneKey = _sceneKey; }
        _sceneSum = 0d;
        _sceneN = 0;
    }

    public string Report()
    {
        float now = Time.realtimeSinceStartup;
        EndSilence(now - (s_startAt < 0f ? now : s_startAt));
        CloseSceneAvg();
        _sceneKey = null;
        var sb = new StringBuilder();
        sb.AppendLine($"[QA-AUDIO] clipSeconds={_clipSeconds} silentRuns={_silentRuns} jumps={_jumps} (limits: peak>-1 dBFS, |Δavg|>6 dB, silence<{SilentDb} dBFS >3 s)");
        sb.AppendLine("[QA-AUDIO] sections (energy-average rms):");
        sb.Append(_sceneAvg);
        if (_issues.Length > 0) { sb.AppendLine("[QA-AUDIO] issues:"); sb.Append(_issues); }
        try
        {
            System.IO.Directory.CreateDirectory(QaCombatBot.OutputDir);
            System.IO.File.WriteAllText(OutputPath, _lines.ToString() + "\n" + sb);
        }
        catch (System.Exception e) { sb.AppendLine("[QA-AUDIO] write failed: " + e.Message); }
        return sb.ToString();
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }
}
#endif
