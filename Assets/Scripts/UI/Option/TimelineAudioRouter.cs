// 타임라인 오디오 → 믹서 그룹 + 옵션 볼륨 (반복 13)
// 문제: 바인딩 없는 Timeline AudioTrack 은 AudioListener 로 직접 재생되어 ChuonMixer(Music/SFX/Voice 버스, 덕킹·로우패스)를 거치지 않고,
//       옵션의 배경음/효과음/보이스 볼륨도 적용되지 않았다 (마스터만 AudioListener.volume 으로 적용).
// 해결: 재생 중인 PlayableDirector 그래프의 AudioPlayableOutput 중 대상이 비어 있는 출력에 버스별 AudioSource 를 연결한다.
//       AudioSource.outputAudioMixerGroup = 버스 그룹, volume = AudioOptionsRuntime 버스 볼륨 (0.25s 마다 갱신).
//       이미 AudioSource 가 바인딩된 트랙은 건드리지 않는다 (ChuonAudioRouter 가 그 소스의 그룹을 라우팅).
// 분류: 트랙/클립 이름 (voice·vo·dialog·대사 → Voice, music·bgm·theme·음악 또는 20초 이상 클립 → Music, 그 외 → SFX).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

public sealed class TimelineAudioRouter : MonoBehaviour
{
    const float ScanInterval = 2f;
    const float VolumeInterval = 0.25f;

    static TimelineAudioRouter s_instance;

    readonly List<PlayableDirector> _directors = new List<PlayableDirector>(16);
    readonly Dictionary<int, AudioSource[]> _sources = new Dictionary<int, AudioSource[]>(8);
    readonly List<AudioSource> _allSources = new List<AudioSource>(16);
    readonly List<ChuonAudioMix.Bus> _allBuses = new List<ChuonAudioMix.Bus>(16);
    float _nextScanAt;
    float _nextVolumeAt;

    /// <summary>QA: 지금까지 연결한 타임라인 오디오 출력 수.</summary>
    public static int RoutedOutputs { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_instance = null;
        RoutedOutputs = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (s_instance != null || !Application.isPlaying)
            return;
        var go = new GameObject("__TimelineAudioRouter");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<TimelineAudioRouter>();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        _nextScanAt = 0f;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _nextScanAt = 0f;
    }

    void LateUpdate()
    {
        float now = Time.unscaledTime;
        if (now >= _nextScanAt)
        {
            _nextScanAt = now + ScanInterval;
            _directors.Clear();
            _directors.AddRange(FindObjectsByType<PlayableDirector>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            PruneSources();
        }

        for (int i = 0; i < _directors.Count; i++)
        {
            PlayableDirector d = _directors[i];
            if (d == null || !d.playableGraph.IsValid())
                continue;
            RouteGraph(d);
        }

        if (now >= _nextVolumeAt)
        {
            _nextVolumeAt = now + VolumeInterval;
            for (int i = 0; i < _allSources.Count; i++)
            {
                AudioSource s = _allSources[i];
                if (s != null)
                    s.volume = BusVolume(_allBuses[i]);
            }
        }
    }

    void RouteGraph(PlayableDirector director)
    {
        PlayableGraph graph = director.playableGraph;
        int count = graph.GetOutputCountByType<AudioPlayableOutput>();
        for (int i = 0; i < count; i++)
        {
            var output = (AudioPlayableOutput)graph.GetOutputByType<AudioPlayableOutput>(i);
            if (!output.IsOutputValid() || output.GetTarget() != null)
                continue;
            ChuonAudioMix.Bus bus = Classify(output.GetReferenceObject());
            AudioSource source = GetOrCreateSource(director, bus);
            if (source == null)
                continue;
            output.SetTarget(source);
            RoutedOutputs++;
        }
    }

    AudioSource GetOrCreateSource(PlayableDirector director, ChuonAudioMix.Bus bus)
    {
        int key = director.GetInstanceID();
        if (!_sources.TryGetValue(key, out AudioSource[] perBus) || perBus == null)
        {
            perBus = new AudioSource[5];
            _sources[key] = perBus;
        }

        AudioSource s = perBus[(int)bus];
        if (s != null)
            return s;

        var go = new GameObject("__TimelineAudio_" + bus);
        go.transform.SetParent(director.transform, false);
        s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.dopplerLevel = 0f;
        s.volume = BusVolume(bus);
        AudioMixerGroup group = ChuonAudioMix.Group(bus);
        if (group != null)
            s.outputAudioMixerGroup = group;
        perBus[(int)bus] = s;
        _allSources.Add(s);
        _allBuses.Add(bus);
        return s;
    }

    void PruneSources()
    {
        for (int i = _allSources.Count - 1; i >= 0; i--)
        {
            if (_allSources[i] != null)
                continue;
            _allSources.RemoveAt(i);
            _allBuses.RemoveAt(i);
        }
    }

    static float BusVolume(ChuonAudioMix.Bus bus)
    {
        switch (bus)
        {
            case ChuonAudioMix.Bus.Music: return AudioOptionsRuntime.MusicVolume;
            case ChuonAudioMix.Bus.Voice: return AudioOptionsRuntime.VoiceVolume;
            case ChuonAudioMix.Bus.Sfx: return AudioOptionsRuntime.SfxVolume;
            default: return 1f;
        }
    }

    public static ChuonAudioMix.Bus Classify(Object reference)
    {
        string name = reference != null ? reference.name : string.Empty;
        ChuonAudioMix.Bus? byName = ClassifyName(name);
        if (byName.HasValue)
            return byName.Value;

        if (reference is AudioTrack track)
        {
            foreach (TimelineClip clip in track.GetClips())
            {
                var asset = clip.asset as AudioPlayableAsset;
                AudioClip audio = asset != null ? asset.clip : null;
                if (audio == null)
                    continue;
                ChuonAudioMix.Bus? byClip = ClassifyName(audio.name);
                if (byClip.HasValue)
                    return byClip.Value;
                if (audio.length >= 20f)
                    return ChuonAudioMix.Bus.Music;
            }
        }
        return ChuonAudioMix.Bus.Sfx;
    }

    static ChuonAudioMix.Bus? ClassifyName(string n)
    {
        if (string.IsNullOrEmpty(n))
            return null;
        if (Has(n, "voice") || Has(n, "_vo") || Has(n, "dialog") || n.Contains("대사") || n.Contains("보이스"))
            return ChuonAudioMix.Bus.Voice;
        if (Has(n, "music") || Has(n, "bgm") || Has(n, "theme") || n.Contains("음악") || n.Contains("배경음"))
            return ChuonAudioMix.Bus.Music;
        return null;
    }

    static bool Has(string s, string token)
    {
        return s.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
