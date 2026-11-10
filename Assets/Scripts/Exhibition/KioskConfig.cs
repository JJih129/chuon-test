// 파일명: KioskConfig.cs
// 전시(PlayX4 2026) 키오스크 모드 설정. 기본 꺼짐.
// 켜는 방법: 명령줄 "-kiosk" (이번 실행만) 또는 스태프 패널 토글(PlayerPrefs "chuon.kiosk.enabled", 영구).
// 끄는 방법: 명령줄 "-noKiosk" 또는 스태프 패널 토글. QA/PV 명령줄(-qa*, -pv*)·배치 모드에서는 -kiosk 가 없으면 항상 꺼짐.
// 자세한 내용: Docs/Exhibition_Kiosk.md
using System;
using System.Globalization;
using UnityEngine;

/// <summary>키오스크 타이밍·옵션 (명령줄로 덮어쓸 수 있다). 순수 데이터라 EditMode 테스트 대상.</summary>
public sealed class KioskSettings
{
    /// <summary>null = 명령줄 지정 없음(저장된 스태프 토글을 따름), true = -kiosk, false = -noKiosk.</summary>
    public bool? CommandLineEnabled;
    /// <summary>QA/PV 자동화 실행 (명령줄 -qa*, -pv*). -kiosk 가 없으면 키오스크를 켜지 않는다.</summary>
    public bool AutomationRun;

    /// <summary>게임플레이 방치 → "계속하시겠습니까?" 카운트다운 시작까지 (초).</summary>
    public float GameplayIdleSeconds = 90f;
    /// <summary>카운트다운 길이 (초). 끝나면 타이틀로.</summary>
    public float CountdownSeconds = 10f;
    /// <summary>타이틀 방치 → 어트랙트(PV) 재생까지 (초).</summary>
    public float AttractIdleSeconds = 60f;
    /// <summary>세션 상한 (초). 0 이하 = 끔.</summary>
    public float SessionCapSeconds = 600f;
    /// <summary>상한 몇 초 전에 경고 토스트를 띄울지.</summary>
    public float SessionWarningSeconds = 60f;
    /// <summary>상한 도달 시 시네마틱/전투 중이면 최대 이만큼 미룬다 (초).</summary>
    public float SessionMaxDeferSeconds = 90f;
    /// <summary>엔드 카드 표시 시간 (초). 최소 표시 시간 이후 아무 입력으로 넘길 수 있다.</summary>
    public float EndCardSeconds = 12f;
    public float EndCardMinSeconds = 3f;
    /// <summary>어트랙트 영상 경로 (명령줄 -kioskVideo). 비우면 자동 탐색.</summary>
    public string VideoPath = string.Empty;
    /// <summary>창 모드 허용 (명령줄 -kioskAllowWindowed). 기본은 전체화면 강제.</summary>
    public bool AllowWindowed;
    /// <summary>볼륨 하한 (부스에서 소리가 완전히 꺼지지 않게).</summary>
    public float MinMasterVolume = 0.35f;
    public float MinChannelVolume = 0.15f;
    /// <summary>커서 자동 숨김 (마우스 정지 후 초).</summary>
    public float CursorHideSeconds = 3f;

    public static KioskSettings Parse(string[] args)
    {
        var s = new KioskSettings();
        if (args == null)
            return s;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (string.IsNullOrEmpty(a) || a[0] != '-')
                continue;

            string next = i + 1 < args.Length ? args[i + 1] : null;
            if (Is(a, "-kiosk")) s.CommandLineEnabled = true;
            else if (Is(a, "-noKiosk")) s.CommandLineEnabled = false;
            else if (Is(a, "-kioskAllowWindowed")) s.AllowWindowed = true;
            else if (Is(a, "-kioskIdle")) { if (TryFloat(next, out float v)) { s.GameplayIdleSeconds = Mathf.Max(5f, v); i++; } }
            else if (Is(a, "-kioskCountdown")) { if (TryFloat(next, out float v)) { s.CountdownSeconds = Mathf.Clamp(v, 1f, 60f); i++; } }
            else if (Is(a, "-kioskAttractIdle")) { if (TryFloat(next, out float v)) { s.AttractIdleSeconds = Mathf.Max(5f, v); i++; } }
            else if (Is(a, "-kioskSessionCap")) { if (TryFloat(next, out float v)) { s.SessionCapSeconds = Mathf.Max(0f, v); i++; } }
            else if (Is(a, "-kioskVideo")) { if (!string.IsNullOrEmpty(next) && next[0] != '-') { s.VideoPath = next; i++; } }
            else if (a.StartsWith("-qa", StringComparison.OrdinalIgnoreCase) || a.StartsWith("-pv", StringComparison.OrdinalIgnoreCase))
                s.AutomationRun = true;
        }

        if (s.SessionCapSeconds > 0f)
            s.SessionWarningSeconds = Mathf.Clamp(s.SessionWarningSeconds, 0f, s.SessionCapSeconds * 0.5f);
        return s;
    }

    static bool Is(string a, string flag) => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase);

    static bool TryFloat(string text, out float value)
    {
        value = 0f;
        return !string.IsNullOrEmpty(text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

public static class KioskConfig
{
    public const string EnabledPrefKey = "chuon.kiosk.enabled";
    public const string SessionCapPrefKey = "chuon.kiosk.sessionCap";
    public const string TitleSceneName = "TitleScene";
    public const string CreditsSceneName = "CreditsScene";
    public const string BootSceneName = "BootScene";
    public const string TeamLogoSceneName = "TeamLogoScene";

    static KioskSettings s_settings;
    static int s_runtimeEnabled = -1;   // 스태프 토글 (이번 실행에서 명령줄보다 우선)
    static int s_sessionCapPref = -1;
    static int s_enabledPref = -1;      // PlayerPrefs 캐시 (매 프레임 읽지 않게)

    public static event Action EnabledChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_settings = null;
        s_runtimeEnabled = -1;
        s_sessionCapPref = -1;
        s_enabledPref = -1;
        EnabledChanged = null;
    }

    public static KioskSettings Settings
    {
        get
        {
            if (s_settings == null)
            {
                string[] args;
                try { args = Environment.GetCommandLineArgs(); }
                catch { args = Array.Empty<string>(); }
                s_settings = KioskSettings.Parse(args);
            }
            return s_settings;
        }
    }

    /// <summary>키오스크 모드가 지금 켜져 있는지.</summary>
    public static bool Enabled
    {
        get
        {
            if (s_runtimeEnabled >= 0)
                return s_runtimeEnabled == 1;
            KioskSettings s = Settings;
            if (s.CommandLineEnabled.HasValue)
                return s.CommandLineEnabled.Value;
            if (s.AutomationRun || Application.isBatchMode)
                return false;
            if (s_enabledPref < 0)
                s_enabledPref = PlayerPrefs.GetInt(EnabledPrefKey, 0) == 1 ? 1 : 0;
            return s_enabledPref == 1;
        }
    }

    /// <summary>명령줄로 켜진 상태인지 (스태프 패널 표시용).</summary>
    public static bool EnabledByCommandLine => s_runtimeEnabled < 0 && Settings.CommandLineEnabled == true;

    /// <summary>스태프 토글: 이번 실행에 즉시 반영 + PlayerPrefs 에 영구 저장.</summary>
    public static void SetEnabled(bool enabled)
    {
        bool before = Enabled;
        s_runtimeEnabled = enabled ? 1 : 0;
        s_enabledPref = s_runtimeEnabled;
        PlayerPrefs.SetInt(EnabledPrefKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
        if (before != enabled)
            EnabledChanged?.Invoke();
    }

    /// <summary>세션 상한 사용 여부 (스태프 토글, 기본 켬). 명령줄 -kioskSessionCap 0 이면 항상 끔.</summary>
    public static bool SessionCapEnabled
    {
        get
        {
            if (Settings.SessionCapSeconds <= 0f)
                return false;
            if (s_sessionCapPref < 0)
                s_sessionCapPref = PlayerPrefs.GetInt(SessionCapPrefKey, 1);
            return s_sessionCapPref != 0;
        }
    }

    public static void SetSessionCapEnabled(bool enabled)
    {
        s_sessionCapPref = enabled ? 1 : 0;
        PlayerPrefs.SetInt(SessionCapPrefKey, s_sessionCapPref);
        PlayerPrefs.Save();
    }

    public static float EffectiveSessionCapSeconds => SessionCapEnabled ? Settings.SessionCapSeconds : 0f;

    public static bool IsTitleScene(string sceneName) => string.Equals(sceneName, TitleSceneName, StringComparison.Ordinal);

    /// <summary>방치 타이머·세션 시계가 도는 플레이 씬 (로고/타이틀/부트/크레딧 제외).</summary>
    public static bool IsGameplayScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return false;
        return !string.Equals(sceneName, TitleSceneName, StringComparison.Ordinal)
            && !string.Equals(sceneName, TeamLogoSceneName, StringComparison.Ordinal)
            && !string.Equals(sceneName, BootSceneName, StringComparison.Ordinal)
            && !string.Equals(sceneName, CreditsSceneName, StringComparison.Ordinal);
    }

    /// <summary>세션이 시작되는 씬 (부트 로딩부터 체험 시간으로 친다).</summary>
    public static bool StartsSession(string sceneName)
        => IsGameplayScene(sceneName) || string.Equals(sceneName, BootSceneName, StringComparison.Ordinal);
}
