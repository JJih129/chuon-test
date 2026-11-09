using UnityEngine;

/// <summary>
/// 접근성 옵션 런타임 적용 (옵션 화면을 열지 않아도 게임 시작 시 저장값 적용).
/// - 카메라 흔들림 강도 (opt_cam_shake, 0~1): CameraShake.GlobalStrength + 패리 임펄스.
///   (이전에는 옵션 매니저가 Awake 될 때만 적용돼서, 재시작 후 옵션 화면을 열기 전까지 100% 흔들림이었다)
/// - 화면 번쩍임 강도 (opt_flash_intensity, 0~1): 궁극기 화면 플래시 / 경보 오버레이 점멸 알파 배율.
///   전투 판단용 텔레그래프(무기/손 링 색 신호)는 줄이지 않는다.
/// </summary>
public static class AccessibilityOptionsRuntime
{
    public const string CameraShakeKey = "opt_cam_shake";
    public const string FlashIntensityKey = "opt_flash_intensity";

    static float s_flashIntensity = 1f;
    static bool s_initialized;

    public static event System.Action<float> FlashIntensityChanged;

    /// <summary>화면 플래시/점멸 알파에 곱할 배율 (0~1).</summary>
    public static float FlashIntensity
    {
        get { EnsureInitialized(); return s_flashIntensity; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        s_initialized = false;
        RefreshFromPrefs();
    }

    public static void RefreshFromPrefs()
    {
        s_initialized = true;
        s_flashIntensity = Mathf.Clamp01(PlayerPrefs.GetFloat(FlashIntensityKey, 1f));
        CameraShake.SetGlobalStrength(PlayerPrefs.GetFloat(CameraShakeKey, 1f));
    }

    public static void SetCameraShakeStrength(float value)
    {
        EnsureInitialized();
        PlayerPrefs.SetFloat(CameraShakeKey, Mathf.Clamp01(value));
        CameraShake.SetGlobalStrength(value);
    }

    public static void SetFlashIntensity(float value)
    {
        EnsureInitialized();
        s_flashIntensity = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(FlashIntensityKey, s_flashIntensity);
        FlashIntensityChanged?.Invoke(s_flashIntensity);
    }

    public static float ScaleFlash(float alpha)
    {
        return alpha * FlashIntensity;
    }

    static void EnsureInitialized()
    {
        if (!s_initialized)
            RefreshFromPrefs();
    }
}
