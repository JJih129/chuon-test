using UnityEngine;

[DisallowMultipleComponent]
public class FramePacingRuntime : MonoBehaviour
{
    const string VSyncKey = "opt_vsync";
    const string TargetFrameRateKey = "opt_target_fps";
    const int DefaultTargetFrameRate = 120;
    const float DefaultMaximumDeltaTime = 0.05f;
    const float DefaultShadowDistance = 16f;
    const int DefaultShadowCascades = 1;
    const int DefaultMaxQueuedFrames = 1;
    const float DefaultLodBias = 0.8f;

    static FramePacingRuntime _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        EnsureInstance();
        _instance.ApplyPrefs();
    }

    public static void RefreshFromPrefs()
    {
        EnsureInstance();
        _instance.ApplyPrefs();
    }

    static void EnsureInstance()
    {
        if (_instance != null)
            return;

        _instance = FindObjectOfType<FramePacingRuntime>(true);
        if (_instance != null)
        {
            _instance.Initialize();
            return;
        }

        GameObject go = new GameObject("FramePacingRuntime");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<FramePacingRuntime>();
        _instance.Initialize();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Initialize();
    }

    void Initialize()
    {
        Time.maximumDeltaTime = DefaultMaximumDeltaTime;
        ApplyPerformanceQualityDefaults();
    }

    void ApplyPrefs()
    {
        bool useVSync = PlayerPrefs.GetInt(VSyncKey, 0) == 1;
        int targetFrameRate = Mathf.Clamp(PlayerPrefs.GetInt(TargetFrameRateKey, DefaultTargetFrameRate), 30, 240);

        QualitySettings.vSyncCount = useVSync ? 1 : 0;
        Application.targetFrameRate = useVSync ? -1 : targetFrameRate;
        Time.maximumDeltaTime = DefaultMaximumDeltaTime;
        ApplyPerformanceQualityDefaults();
    }

    static void ApplyPerformanceQualityDefaults()
    {
        if (QualitySettings.shadowDistance <= 0f || QualitySettings.shadowDistance > DefaultShadowDistance)
            QualitySettings.shadowDistance = DefaultShadowDistance;

        if (QualitySettings.shadowCascades > DefaultShadowCascades)
            QualitySettings.shadowCascades = DefaultShadowCascades;

        if (QualitySettings.maxQueuedFrames <= 0 || QualitySettings.maxQueuedFrames > DefaultMaxQueuedFrames)
            QualitySettings.maxQueuedFrames = DefaultMaxQueuedFrames;

        if (QualitySettings.lodBias > DefaultLodBias)
            QualitySettings.lodBias = DefaultLodBias;

        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
    }
}
