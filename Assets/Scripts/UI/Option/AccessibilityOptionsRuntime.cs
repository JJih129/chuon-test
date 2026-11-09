using UnityEngine;

/// <summary>
/// 접근성 옵션 런타임 적용 (옵션 화면을 열지 않아도 게임 시작 시 저장값 적용).
/// - 카메라 흔들림 강도 (opt_cam_shake, 0~1): CameraShake.GlobalStrength + 패리 임펄스.
///   (이전에는 옵션 매니저가 Awake 될 때만 적용돼서, 재시작 후 옵션 화면을 열기 전까지 100% 흔들림이었다)
/// - 화면 번쩍임 강도 (opt_flash_intensity, 0~1): 궁극기 화면 플래시 / 경보 오버레이 점멸 알파 배율.
///   0 이면 화면 갈라짐은 파편 비산 대신 정지 균열 → 크로스페이드로 대체된다 (ActionColourDirector).
/// - 흑백 흡수 (opt_bw_absorb, 0/1): 궁극기·패링 흑백(채도 제거) 연출 사용 여부.
///   전투 판단용 텔레그래프(무기/손 링 색 신호)는 줄이지 않는다.
/// </summary>
public static class AccessibilityOptionsRuntime
{
    public const string CameraShakeKey = "opt_cam_shake";
    public const string FlashIntensityKey = "opt_flash_intensity";
    /// <summary>궁극기 흑백 흡수(화면 채도 제거) 사용 여부 (1 = 켬, 기본). ActionColourDirector 가 적용.</summary>
    public const string BwAbsorbKey = "opt_bw_absorb";

    static float s_flashIntensity = 1f;
    static bool s_bwAbsorb = true;
    static bool s_initialized;

    public static event System.Action<float> FlashIntensityChanged;
    public static event System.Action<bool> BwAbsorbChanged;

    /// <summary>흑백 흡수 연출(채도 제거) 허용 여부. 끄면 궁극기/패링 흑백이 색 그대로 진행된다.</summary>
    public static bool BwAbsorbEnabled
    {
        get { EnsureInitialized(); return s_bwAbsorb; }
    }

    public static void SetBwAbsorbEnabled(bool value)
    {
        EnsureInitialized();
        s_bwAbsorb = value;
        PlayerPrefs.SetInt(BwAbsorbKey, value ? 1 : 0);
        BwAbsorbChanged?.Invoke(value);
    }

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
        s_bwAbsorb = PlayerPrefs.GetInt(BwAbsorbKey, 1) != 0;
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
