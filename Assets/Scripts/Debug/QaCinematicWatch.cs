#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 반복 14C (개발 전용): 연출(CinematicDirector) 시청 시간·프레임 기록. 스스로 실행되지 않는다 —
/// QaCommandLine 이 QA 실행에서 Ensure() 를 부르거나, Play 모드에서 MCP 로 QaCinematicWatch.Ensure() 호출.
/// 연출마다: 실제 길이(realtime), 건너뜀 여부, 프레임 수, 평균/1% 저점 FPS, 최악 프레임, 첫 0.5 s 최악 프레임(첫 프레임 히치),
/// 33 ms 초과 프레임 수. QaFrameSampler 와 달리 시작 직후 프레임도 센다.
/// </summary>
public sealed class QaCinematicWatch : MonoBehaviour
{
    sealed class Entry
    {
        public string id;
        public float start;
        public float seconds;
        public bool skipped;
        public bool finished;
        public readonly List<float> frames = new List<float>(2048);
        public float firstHalfSecondWorst;
    }

    static QaCinematicWatch s_instance;
    readonly List<Entry> _entries = new List<Entry>();
    Entry _current;

    public static int Count => s_instance != null ? s_instance._entries.Count : 0;

    public static void Ensure()
    {
        if (s_instance != null)
            return;
        var go = new GameObject("__QaCinematicWatch");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<QaCinematicWatch>();
        CinematicDirector.Started += s_instance.HandleStarted;
        CinematicDirector.Finished += s_instance.HandleFinished;
    }

    void OnDestroy()
    {
        CinematicDirector.Started -= HandleStarted;
        CinematicDirector.Finished -= HandleFinished;
        if (s_instance == this)
            s_instance = null;
    }

    void HandleStarted(string id)
    {
        _current = new Entry { id = id, start = Time.realtimeSinceStartup };
        _entries.Add(_current);
        Debug.Log($"[QA-CINE] start {id} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
    }

    void HandleFinished(string id, bool skipped)
    {
        if (_current == null || _current.id != id)
            return;
        _current.seconds = Time.realtimeSinceStartup - _current.start;
        _current.skipped = skipped;
        _current.finished = true;
        Debug.Log($"[QA-CINE] end {id} {_current.seconds:F1}s skipped={skipped} {Stats(_current)}");
        _current = null;
    }

    void Update()
    {
        if (_current == null)
            return;
        float dt = Time.unscaledDeltaTime;
        _current.frames.Add(dt);
        if (Time.realtimeSinceStartup - _current.start <= 0.5f)
            _current.firstHalfSecondWorst = Mathf.Max(_current.firstHalfSecondWorst, dt);
    }

    static string Stats(Entry e)
    {
        if (e.frames.Count == 0)
            return "frames=0";
        float total = 0f;
        int over33 = 0;
        for (int i = 0; i < e.frames.Count; i++)
        {
            total += e.frames[i];
            if (e.frames[i] > 0.0334f) over33++;
        }
        var sorted = new List<float>(e.frames);
        sorted.Sort();
        float p99 = sorted[Mathf.Clamp(Mathf.FloorToInt(sorted.Count * 0.99f), 0, sorted.Count - 1)];
        return $"frames={e.frames.Count} avg={e.frames.Count / Mathf.Max(0.0001f, total):F0}fps 1%low={1f / Mathf.Max(0.0001f, p99):F0}fps worst={sorted[sorted.Count - 1] * 1000f:F0}ms first0.5s={e.firstHalfSecondWorst * 1000f:F0}ms over33ms={over33}";
    }

    public static string Report()
    {
        if (s_instance == null || s_instance._entries.Count == 0)
            return "[QA-CINE] no cinematics";
        var sb = new StringBuilder();
        float watched = 0f;
        foreach (Entry e in s_instance._entries)
        {
            if (e.finished) watched += e.seconds;
            sb.Append("[QA-CINE] ").Append(e.id).Append(' ').Append(e.finished ? e.seconds.ToString("F1") + "s" : "unfinished")
              .Append(e.skipped ? " SKIPPED " : " ").Append(Stats(e)).Append('\n');
        }
        sb.Append("[QA-CINE] total watched ").Append(watched.ToString("F1")).Append("s over ").Append(s_instance._entries.Count).Append(" cinematics");
        return sb.ToString();
    }
}
#endif
