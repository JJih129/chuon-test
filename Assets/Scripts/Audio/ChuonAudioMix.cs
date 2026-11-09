// 추온 오디오 믹스 (반복 11)
// Resources/Audio/ChuonMixer.mixer (Master ← Music / SFX / Voice / UI) 로 모든 AudioSource 를 라우팅한다.
//  - 옵션 볼륨은 기존대로 소스별 AudioOptionsRuntime.Scale* 로 곱한다 (믹서 그룹은 0 dB 고정) → 이중 적용 없음.
//  - 노출 파라미터: MusicDuck(dB, 음악 덕킹), SfxLowpass / MusicLowpass(Hz, 브레이크 시작 로우패스).
//  - Master 에 컴프레서(리미터 역할)를 둬서 겹치는 효과음이 클리핑되지 않게 한다.
// ChuonAudioRouter 가 씬 로드 시 + 주기적으로 출력 그룹이 비어 있는 소스를 이름/계층 기준으로 분류해 연결한다.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public static class ChuonAudioMix
{
    public enum Bus { Master, Music, Sfx, Voice, Ui }

    public const string MixerResourcePath = "Audio/ChuonMixer";
    public const string MusicDuckParam = "MusicDuck";
    public const string SfxLowpassParam = "SfxLowpass";
    public const string MusicLowpassParam = "MusicLowpass";
    public const float LowpassOpenHz = 22000f;

    static AudioMixer s_mixer;
    static readonly AudioMixerGroup[] s_groups = new AudioMixerGroup[5];
    static bool s_loaded;

    public static AudioMixer Mixer
    {
        get
        {
            Load();
            return s_mixer;
        }
    }

    public static AudioMixerGroup Group(Bus bus)
    {
        Load();
        return s_groups[(int)bus];
    }

    /// <summary>출력 그룹이 비어 있을 때만 연결한다 (씬/프리팹에서 이미 지정한 라우팅은 존중).</summary>
    public static void Route(AudioSource source, Bus bus)
    {
        if (source == null || source.outputAudioMixerGroup != null)
            return;
        AudioMixerGroup g = Group(bus);
        if (g != null)
            source.outputAudioMixerGroup = g;
    }

    /// <summary>브레이크 시작 등: SFX·음악을 잠깐 먹먹하게 (duration 초, 실시간).</summary>
    public static void LowpassDip(float duration)
    {
        ChuonAudioRouter.Ensure()?.StartLowpassDip(duration);
    }

    public static void SetFloatSafe(string param, float value)
    {
        AudioMixer m = Mixer;
        if (m != null)
            m.SetFloat(param, value);
    }

    static void Load()
    {
        if (s_loaded)
            return;
        s_loaded = true;
        s_mixer = Resources.Load<AudioMixer>(MixerResourcePath);
        if (s_mixer == null)
            return;
        s_groups[(int)Bus.Master] = Find("Master");
        s_groups[(int)Bus.Music] = Find("Music");
        s_groups[(int)Bus.Sfx] = Find("SFX");
        s_groups[(int)Bus.Voice] = Find("Voice");
        s_groups[(int)Bus.Ui] = Find("UI");
    }

    static AudioMixerGroup Find(string name)
    {
        AudioMixerGroup[] found = s_mixer.FindMatchingGroups(name);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && found[i].name == name)
                return found[i];
        }
        return found.Length > 0 ? found[0] : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded = false;
        s_mixer = null;
        for (int i = 0; i < s_groups.Length; i++)
            s_groups[i] = null;
    }
}

[DefaultExecutionOrder(-50)]
public sealed class ChuonAudioRouter : MonoBehaviour
{
    const float ScanInterval = 1.5f;
    const float DipFloorHz = 650f;

    static ChuonAudioRouter s_instance;

    float _nextScanAt;
    float _dipStart = -1f;
    float _dipDuration;
    bool _lowpassDirty = true;

    /// <summary>QA: 지금까지 라우팅한 소스 수 (버스별).</summary>
    public static readonly int[] RoutedCounts = new int[5];

    public static ChuonAudioRouter Ensure()
    {
        if (s_instance != null)
            return s_instance;
        if (!Application.isPlaying)
            return null;
        var go = new GameObject("__ChuonAudioRouter");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<ChuonAudioRouter>();
        return s_instance;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (ChuonAudioMix.Mixer != null)
            Ensure();
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
        _lowpassDirty = true;
    }

    public void StartLowpassDip(float duration)
    {
        _dipStart = Time.unscaledTime;
        _dipDuration = Mathf.Max(0.05f, duration);
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (now >= _nextScanAt)
        {
            _nextScanAt = now + ScanInterval;
            ScanAndRoute();
        }

        // 로우패스 딥: 빠르게 닫히고(40 ms) 남은 시간 동안 로그 스케일로 열린다
        if (_dipStart >= 0f)
        {
            float t = now - _dipStart;
            float hz;
            if (t >= _dipDuration)
            {
                hz = ChuonAudioMix.LowpassOpenHz;
                _dipStart = -1f;
            }
            else if (t < 0.04f)
            {
                hz = Mathf.Exp(Mathf.Lerp(Mathf.Log(ChuonAudioMix.LowpassOpenHz), Mathf.Log(DipFloorHz), t / 0.04f));
            }
            else
            {
                float k = (t - 0.04f) / Mathf.Max(0.01f, _dipDuration - 0.04f);
                k = k * k;
                hz = Mathf.Exp(Mathf.Lerp(Mathf.Log(DipFloorHz), Mathf.Log(ChuonAudioMix.LowpassOpenHz), k));
            }
            ChuonAudioMix.SetFloatSafe(ChuonAudioMix.SfxLowpassParam, hz);
            ChuonAudioMix.SetFloatSafe(ChuonAudioMix.MusicLowpassParam, Mathf.Max(hz, 1200f));
            _lowpassDirty = true;
        }
        else if (_lowpassDirty)
        {
            _lowpassDirty = false;
            ChuonAudioMix.SetFloatSafe(ChuonAudioMix.SfxLowpassParam, ChuonAudioMix.LowpassOpenHz);
            ChuonAudioMix.SetFloatSafe(ChuonAudioMix.MusicLowpassParam, ChuonAudioMix.LowpassOpenHz);
        }
    }

    static readonly List<Canvas> s_canvasBuffer = new List<Canvas>(2);

    public static void ScanAndRoute()
    {
        if (ChuonAudioMix.Mixer == null)
            return;
        AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource s = sources[i];
            if (s == null || s.outputAudioMixerGroup != null)
                continue;
            ChuonAudioMix.Bus bus = Classify(s);
            ChuonAudioMix.Route(s, bus);
            if (s.outputAudioMixerGroup != null)
                RoutedCounts[(int)bus]++;
        }
    }

    public static ChuonAudioMix.Bus Classify(AudioSource s)
    {
        string n = s.gameObject.name;
        if (Contains(n, "Music") || Contains(n, "BGM"))
            return ChuonAudioMix.Bus.Music;
        if (Contains(n, "Voice") || Contains(n, "Dialogue") || Contains(n, "Bark") || n.EndsWith("VO"))
            return ChuonAudioMix.Bus.Voice;
        s.GetComponentsInParent(true, s_canvasBuffer);
        bool underCanvas = s_canvasBuffer.Count > 0;
        s_canvasBuffer.Clear();
        if (underCanvas || Contains(n, "Button") || Contains(n, "Menu") || n.Contains("UI") || n.Contains("HUD"))
            return ChuonAudioMix.Bus.Ui;
        return ChuonAudioMix.Bus.Sfx;
    }

    static bool Contains(string s, string token)
    {
        return s.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
