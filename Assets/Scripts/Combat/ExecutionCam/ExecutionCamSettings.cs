// 처형 연출 카메라 옵션 (PlayerPrefs). 옵션 화면 연결은 OptionsManagerAdvanced 쪽 1~3줄 훅 (iterX/hook.patch 참고).
//  - opt_exec_cam      (1 = 켬, 기본) : 끄면 처형 때 기존 카메라 (보스는 ExecutionCameraShot, 잡몹은 게임플레이 카메라 그대로)
//  - opt_reduce_motion (0 = 끔, 기본) : 켜면 궤도·휩·더치·FOV 펀치·흔들림·추가 슬로모 없이 고정 투샷 1개
// 흔들림은 CameraShake.GlobalStrength(옵션 '카메라 흔들림'), 흑백/섬광은 ActionColourDirector 가 접근성 슬라이더로 제한한다.
using UnityEngine;

public static class ExecutionCamSettings
{
    public const string EnabledKey = "opt_exec_cam";
    public const string ReduceMotionKey = "opt_reduce_motion";

    static bool s_loaded;
    static bool s_enabled = true;
    static bool s_reduceMotion;

    public static event System.Action Changed;

    public static bool Enabled
    {
        get { EnsureLoaded(); return s_enabled; }
    }

    public static bool ReduceMotion
    {
        get { EnsureLoaded(); return s_reduceMotion; }
    }

    public static void SetEnabled(bool value)
    {
        EnsureLoaded();
        s_enabled = value;
        PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
        Changed?.Invoke();
    }

    public static void SetReduceMotion(bool value)
    {
        EnsureLoaded();
        s_reduceMotion = value;
        PlayerPrefs.SetInt(ReduceMotionKey, value ? 1 : 0);
        Changed?.Invoke();
    }

    public static void RefreshFromPrefs()
    {
        s_loaded = true;
        s_enabled = PlayerPrefs.GetInt(EnabledKey, 1) != 0;
        s_reduceMotion = PlayerPrefs.GetInt(ReduceMotionKey, 0) != 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded = false;
    }

    static void EnsureLoaded()
    {
        if (!s_loaded)
            RefreshFromPrefs();
    }
}
