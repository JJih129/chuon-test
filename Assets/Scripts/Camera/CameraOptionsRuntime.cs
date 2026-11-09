using UnityEngine;

/// <summary>
/// 카메라 옵션 (PlayerPrefs 저장, 즉시 적용). SoulsCameraRig 가 매 프레임 읽는다.
/// - 마우스/패드 감도 배율 (0.2~3.0), 상하 반전, 자동 재정렬(기본 끔), 수직 FOV (45~60)
/// </summary>
public static class CameraOptionsRuntime
{
    public const string MouseSensitivityKey = "opt_cam_sens_mouse";
    public const string PadSensitivityKey = "opt_cam_sens_pad";
    public const string InvertYKey = "opt_cam_invert_y";
    public const string AutoRecenterKey = "opt_cam_auto_recenter";
    public const string FieldOfViewKey = "opt_cam_fov";

    public const float MinSensitivity = 0.2f;
    public const float MaxSensitivity = 3f;
    public const float MinFov = 45f;
    public const float MaxFov = 60f;
    public const float DefaultFov = 50f;

    static bool s_initialized;
    static float s_mouse = 1f, s_pad = 1f, s_fov = DefaultFov;
    static bool s_invertY, s_autoRecenter;

    public static event System.Action Changed;

    public static float MouseSensitivity { get { Ensure(); return s_mouse; } }
    public static float PadSensitivity { get { Ensure(); return s_pad; } }
    public static bool InvertY { get { Ensure(); return s_invertY; } }
    public static bool AutoRecenter { get { Ensure(); return s_autoRecenter; } }
    public static float FieldOfView { get { Ensure(); return s_fov; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        s_initialized = false;
        RefreshFromPrefs();
    }

    public static void RefreshFromPrefs()
    {
        s_initialized = true;
        s_mouse = Mathf.Clamp(PlayerPrefs.GetFloat(MouseSensitivityKey, 1f), MinSensitivity, MaxSensitivity);
        s_pad = Mathf.Clamp(PlayerPrefs.GetFloat(PadSensitivityKey, 1f), MinSensitivity, MaxSensitivity);
        s_invertY = PlayerPrefs.GetInt(InvertYKey, 0) == 1;
        s_autoRecenter = PlayerPrefs.GetInt(AutoRecenterKey, 0) == 1;
        s_fov = Mathf.Clamp(PlayerPrefs.GetFloat(FieldOfViewKey, DefaultFov), MinFov, MaxFov);
    }

    public static void SetMouseSensitivity(float value)
    {
        Ensure();
        s_mouse = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(MouseSensitivityKey, s_mouse);
        Changed?.Invoke();
    }

    public static void SetPadSensitivity(float value)
    {
        Ensure();
        s_pad = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(PadSensitivityKey, s_pad);
        Changed?.Invoke();
    }

    public static void SetInvertY(bool value)
    {
        Ensure();
        s_invertY = value;
        PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0);
        Changed?.Invoke();
    }

    public static void SetAutoRecenter(bool value)
    {
        Ensure();
        s_autoRecenter = value;
        PlayerPrefs.SetInt(AutoRecenterKey, value ? 1 : 0);
        Changed?.Invoke();
    }

    public static void SetFieldOfView(float value)
    {
        Ensure();
        s_fov = Mathf.Clamp(value, MinFov, MaxFov);
        PlayerPrefs.SetFloat(FieldOfViewKey, s_fov);
        Changed?.Invoke();
    }

    static void Ensure()
    {
        if (!s_initialized)
            RefreshFromPrefs();
    }
}
