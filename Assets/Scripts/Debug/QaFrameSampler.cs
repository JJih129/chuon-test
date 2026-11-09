#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// Dev-only frame-time sampler for QA passes. Never runs by itself: call QaFrameSampler.Mark("section")
/// (e.g. via Unity MCP execute_code) to start/switch a named section, then QaFrameSampler.Report().
/// Records per section: frames, average FPS, 1% low FPS, worst frame and the spikes (&gt; spikeMs).
/// Spike attribution: each spike lists the profiler markers (GC / shader / loading / audio / upload ...) that took
/// &gt; 1 ms in that frame plus GC collections, read with ProfilerRecorder (works in development players).
/// </summary>
public sealed class QaFrameSampler : MonoBehaviour
{
    sealed class Section
    {
        public string name;
        public readonly List<float> frames = new List<float>(4096);
        public readonly List<string> spikes = new List<string>();
        public readonly List<string> events = new List<string>();
        public float startTime;
    }

    public float spikeMs = 25f;
    [Tooltip("Frames right after Mark() are skipped (the MCP call that marked the section stalls the editor).")]
    public float ignoreFirstSeconds = 0.5f;
    static QaFrameSampler s_instance;
    readonly List<Section> _sections = new List<Section>();
    Section _current;

    static readonly string[] AttributionTokens =
    {
        "GC.Collect", "GC.Alloc", "Shader", "GPUProgram", "CreateGPU", "Audio", "Loading.", "Upload", "Instantiate",
        "AwakeFromLoad", "Texture", "Mesh.", "UnloadUnused", "Resources.Load", "Canvas.", "TextMeshPro", "Font",
        "Physics.Simulate", "Animator", "Gfx.WaitForPresent", "Gfx.WaitForRenderThread", "WaitForTargetFPS",
        "Scripting", "Behaviour", "Coroutine", "PlayerLoop", "Render", "Inl_", "Semaphore"
    };

    readonly List<(string name, ProfilerRecorder recorder)> _recorders = new List<(string, ProfilerRecorder)>();
    int _lastGcCount;

    void StartRecorders()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (ProfilerRecorderHandle handle in handles)
        {
            ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(handle);
            if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || _recorders.Count >= 400)
                continue;
            string n = d.Name;
            bool match = false;
            for (int i = 0; i < AttributionTokens.Length && !match; i++)
                match = n.Contains(AttributionTokens[i]);
            if (!match)
                continue;
            _recorders.Add((n, ProfilerRecorder.StartNew(d.Category, n, 1)));
        }
        _lastGcCount = System.GC.CollectionCount(0);
    }

    void OnDestroy()
    {
        foreach (var r in _recorders) r.recorder.Dispose();
        _recorders.Clear();
    }

    readonly List<(string name, double ms)> _top = new List<(string, double)>(16);

    string Attribute()
    {
        _top.Clear();
        foreach (var r in _recorders)
        {
            if (!r.recorder.Valid) continue;
            double ms = r.recorder.LastValue * 1e-6;
            if (ms >= 1.0 && r.name != "PlayerLoop") _top.Add((r.name, ms));
        }
        _top.Sort((a, b) => b.ms.CompareTo(a.ms));
        var sb = new StringBuilder();
        int gc = System.GC.CollectionCount(0);
        if (gc != _lastGcCount) sb.Append(" GC0x").Append(gc - _lastGcCount);
        for (int i = 0; i < _top.Count && i < 6; i++)
            sb.Append(' ').Append(_top[i].name).Append('=').Append(_top[i].ms.ToString("F0"));
        return sb.ToString();
    }

    public static void Mark(string section)
    {
        if (s_instance == null)
        {
            var go = new GameObject("__QaFrameSampler");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<QaFrameSampler>();
            s_instance.StartRecorders();
        }

        s_instance._current = new Section { name = section, startTime = Time.realtimeSinceStartup };
        s_instance._sections.Add(s_instance._current);
    }

    /// <summary>Timestamped note in the current section (e.g. "drone spawned"), printed with the report.</summary>
    public static void Event(string note)
    {
        if (s_instance != null && s_instance._current != null)
            s_instance._current.events.Add($"{Time.realtimeSinceStartup - s_instance._current.startTime:F1}s:{note}");
    }

    public static void Clear()
    {
        if (s_instance != null)
        {
            s_instance._sections.Clear();
            s_instance._current = null;
        }
    }

    public static string Report()
    {
        if (s_instance == null)
            return "no sampler";

        var sb = new StringBuilder();
        foreach (Section s in s_instance._sections)
        {
            if (s.frames.Count == 0)
            {
                sb.Append(s.name).Append(": no frames").Append('\n');
                continue;
            }

            float total = 0f;
            for (int i = 0; i < s.frames.Count; i++)
                total += s.frames[i];
            var sorted = new List<float>(s.frames);
            sorted.Sort();
            float p99 = sorted[Mathf.Clamp(Mathf.FloorToInt(sorted.Count * 0.99f), 0, sorted.Count - 1)];
            float worst = sorted[sorted.Count - 1];
            sb.Append(s.name).Append(": n=").Append(s.frames.Count)
              .Append(" avg=").Append((s.frames.Count / Mathf.Max(0.0001f, total)).ToString("F0")).Append("fps")
              .Append(" 1%low=").Append((1f / Mathf.Max(0.0001f, p99)).ToString("F0")).Append("fps")
              .Append(" worst=").Append((worst * 1000f).ToString("F0")).Append("ms")
              .Append(" spikes=").Append(s.spikes.Count);
            for (int i = 0; i < s.spikes.Count && i < 12; i++)
                sb.Append(i == 0 ? " [" : ", ").Append(s.spikes[i]);
            if (s.spikes.Count > 0)
                sb.Append("]");
            if (s.events.Count > 0)
                sb.Append(" events={").Append(string.Join(", ", s.events)).Append("}");
            sb.Append('\n');
        }

        return sb.ToString();
    }

    void Update()
    {
        if (_current == null)
            return;

        if (Time.realtimeSinceStartup - _current.startTime < ignoreFirstSeconds)
            return;

        float dt = Time.unscaledDeltaTime;
        _current.frames.Add(dt);
        if (dt * 1000f > spikeMs)
            _current.spikes.Add($"{Time.realtimeSinceStartup - _current.startTime:F1}s:{dt * 1000f:F0}ms{Attribute()}");
        _lastGcCount = System.GC.CollectionCount(0);
    }
}
#endif
