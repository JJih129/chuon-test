using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class VideoOptionsRuntime : MonoBehaviour
{
    const string BrightnessKey = "opt_brightness";
    const string MotionBlurKey = "opt_motionblur";

    static VideoOptionsRuntime _instance;

    Volume _volume;
    VolumeProfile _profile;
    ColorAdjustments _colorAdjustments;
    MotionBlur _motionBlur;
    bool _initialized;

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

        _motionBlur = _profile.Add<MotionBlur>(true);
        _motionBlur.active = true;
        _motionBlur.intensity.overrideState = true;
        _motionBlur.quality.overrideState = true;
        _motionBlur.quality.value = MotionBlurQuality.Low;

        _initialized = true;
        ApplyPrefs();
        ApplyToAllCameras();
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToAllCameras();
        ApplyPrefs();
    }

    void ApplyPrefs()
    {
        ApplyBrightness(PlayerPrefs.GetFloat(BrightnessKey, 1f));
        ApplyMotionBlur(PlayerPrefs.GetInt(MotionBlurKey, 1) == 1);
    }

    void ApplyBrightness(float value)
    {
        if (_colorAdjustments == null)
            return;

        value = Mathf.Clamp01(value);
        _colorAdjustments.postExposure.value = Mathf.Lerp(-1.6f, 0f, value);
    }

    void ApplyMotionBlur(bool enabled)
    {
        if (_motionBlur == null)
            return;

        _motionBlur.intensity.value = enabled ? 0.55f : 0f;
        ApplyToAllCameras();
    }

    void ApplyToAllCameras()
    {
        var cameras = FindObjectsOfType<Camera>(true);
        foreach (var cam in cameras)
        {
            if (cam == null)
                continue;

            var cameraData = cam.GetUniversalAdditionalCameraData();
            if (cameraData != null)
                cameraData.renderPostProcessing = true;
        }
    }
}
