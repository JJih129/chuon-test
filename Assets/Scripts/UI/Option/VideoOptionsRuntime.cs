using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class VideoOptionsRuntime : MonoBehaviour
{
    const string BrightnessKey = "opt_brightness";
    const string MotionBlurKey = "opt_motionblur";
    const float DefaultBrightness = 1f;
    const int DefaultMotionBlur = 0;

    static VideoOptionsRuntime _instance;

    Volume _volume;
    VolumeProfile _profile;
    ColorAdjustments _colorAdjustments;
    MotionBlur _motionBlur;
    bool _initialized;

    // Base cinematic look (always on). Keep values subtle; per-scene volumes can override with higher priority.
    // Threshold slightly above 1 so the bright (near-white) floor does not bloom; only true HDR highlights do.
    const float BaseBloomThreshold = 1.1f;
    const float BaseBloomIntensity = 0.65f;
    const float BaseBloomScatter = 0.68f;
    const float BaseVignetteIntensity = 0.17f;

    // Base grade: counter the flat / washed-out look. Neutral tonemapping is kept on purpose
    // (ACES darkens and hue-shifts the anime character shading).
    // 반복 16 (플레이어 리뷰 #8): 튜토리얼 골목·로비·엘리베이터 복도·아레나 외곽이 전반적으로 어두웠다.
    // 원인 = 이 전역 볼륨(우선순위 10000)이 씬 볼륨 위에서 -0.2 EV + 그림자 -0.04 + 비네트 0.2 를 항상 깔았음.
    // → +0.15 EV(순 +0.35 EV), 미드톤 +0.05, 그림자 눌림 완화, 대비 12 → 14 로 올려 콘트라스트(그래파이트/화이트 분리)는 유지.
    // 하이라이트는 -0.08 로 조금 더 눌러 세라믹 화이트가 뉴트럴 톤매핑에서 날아가지 않게 한다.
    const float BaseContrast = 14f;      // ColorAdjustments.contrast (-100..100)
    const float BaseSaturation = 6f;     // ColorAdjustments.saturation (-100..100)
    const float BaseExposureOffset = 0.15f; // added to the brightness option's postExposure mapping (EV)
    const float BrightnessMinExposure = -1.6f;
    const float BrightnessMaxExposure = 0f;
    // Subtle shadows/midtones/highlights: slightly richer, cooler shadows and tamed highlights.
    // Vector4 = (r, g, b, w); w is the luminance offset, rgb is the tint (1 = neutral).
    static readonly Vector4 BaseShadows = new Vector4(0.98f, 0.99f, 1.02f, -0.015f);
    static readonly Vector4 BaseMidtones = new Vector4(1f, 1f, 1f, 0.05f);
    static readonly Vector4 BaseHighlights = new Vector4(1f, 1f, 1f, -0.08f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        EnsureInstance();
        RefreshFromPrefs();
    }

    public static void RefreshFromPrefs()
    {
        EnsureInstance();
        _instance.ApplyPrefs();
    }

    public static void SetBrightness(float value)
    {
        EnsureInstance();
        _instance.ApplyBrightness(value);
    }

    public static void SetMotionBlurEnabled(bool enabled)
    {
        EnsureInstance();
        _instance.ApplyMotionBlur(enabled);
    }

    static void EnsureInstance()
    {
        if (_instance != null)
            return;

        _instance = FindObjectOfType<VideoOptionsRuntime>(true);
        if (_instance != null)
        {
            _instance.Initialize();
            return;
        }

        var go = new GameObject("VideoOptionsRuntime");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<VideoOptionsRuntime>();
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

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void Initialize()
    {
        if (_initialized)
            return;

        _volume = GetComponent<Volume>();
        if (_volume == null)
            _volume = gameObject.AddComponent<Volume>();

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "RuntimeVideoOptionsProfile";
        _profile.hideFlags = HideFlags.HideAndDontSave;

        _volume.isGlobal = true;
        _volume.priority = 10000f;
        _volume.weight = 1f;
        _volume.sharedProfile = _profile;

        _colorAdjustments = _profile.Add<ColorAdjustments>(true);
        _colorAdjustments.active = true;
        _colorAdjustments.postExposure.overrideState = true;
        _colorAdjustments.contrast.Override(BaseContrast);
        _colorAdjustments.saturation.Override(BaseSaturation);

        _motionBlur = _profile.Add<MotionBlur>(true);
        _motionBlur.active = true;
        _motionBlur.intensity.overrideState = true;
        _motionBlur.quality.overrideState = true;
        _motionBlur.quality.value = MotionBlurQuality.Low;

        AddBaseLook(_profile);

        _initialized = true;
        ApplyPrefs();
        ApplyToAllCameras();
    }

    static void AddBaseLook(VolumeProfile profile)
    {
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = BaseBloomThreshold;
        bloom.intensity.value = BaseBloomIntensity;
        bloom.scatter.value = BaseBloomScatter;
        bloom.highQualityFiltering.value = false;

        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.value = TonemappingMode.Neutral;

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.value = BaseVignetteIntensity;
        vignette.smoothness.value = 0.45f;

        var smh = profile.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.Override(BaseShadows);
        smh.midtones.Override(BaseMidtones);
        smh.highlights.Override(BaseHighlights);
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToAllCameras();
        ApplyPrefs();
    }

    void ApplyPrefs()
    {
        ApplyBrightness(PlayerPrefs.GetFloat(BrightnessKey, DefaultBrightness));
        ApplyMotionBlur(PlayerPrefs.GetInt(MotionBlurKey, DefaultMotionBlur) == 1);
    }

    void ApplyBrightness(float value)
    {
        if (_colorAdjustments == null)
            return;

        value = Mathf.Clamp01(value);
        _colorAdjustments.postExposure.value = Mathf.Lerp(BrightnessMinExposure, BrightnessMaxExposure, value) + BaseExposureOffset;
        ApplyVolumeState();
        ApplyToAllCameras();
    }

    void ApplyMotionBlur(bool enabled)
    {
        if (_motionBlur == null)
            return;

        _motionBlur.intensity.value = enabled ? 0.32f : 0f;
        ApplyVolumeState();
        ApplyToAllCameras();
    }

    void ApplyToAllCameras()
    {
        bool enablePostProcessing = ShouldEnablePostProcessing();
        var cameras = FindObjectsOfType<Camera>(true);
        foreach (var cam in cameras)
        {
            if (cam == null)
                continue;

            var cameraData = cam.GetUniversalAdditionalCameraData();
            if (cameraData != null)
                cameraData.renderPostProcessing = enablePostProcessing;
        }
    }

    void ApplyVolumeState()
    {
        if (_volume == null)
            return;

        _volume.weight = ShouldEnablePostProcessing() ? 1f : 0f;
    }

    bool ShouldEnablePostProcessing()
    {
        // The base look (bloom / tonemapping / vignette) is always active.
        return true;
    }

}
