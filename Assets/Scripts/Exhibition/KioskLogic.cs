// 파일명: KioskLogic.cs
// 키오스크 모드의 순수 로직 (Unity 오브젝트 의존 없음 → EditMode 테스트: Assets/Scripts/Exhibition/Editor/KioskLogicTests.cs).
using System;
using System.Globalization;

public enum KioskIdlePhase
{
    Active = 0,
    Countdown = 1,
    Expired = 2,
}

/// <summary>
/// 방치 타이머. 입력이 있으면 0으로, 임계 초를 넘으면 카운트다운, 카운트다운이 끝나면 Expired.
/// frozen(시네마틱 재생·로딩 중)이면 방치 시간을 쌓지 않는다 (이미 시작된 카운트다운은 계속).
/// </summary>
public sealed class KioskIdleTracker
{
    /// <summary>한 프레임에서 쌓을 최대 시간 (로딩 히치가 방치로 계산되지 않게).</summary>
    public const float MaxStep = 0.25f;

    float _threshold;
    float _countdown;

    public float IdleSeconds { get; private set; }
    public float CountdownRemaining { get; private set; }
    public KioskIdlePhase Phase { get; private set; }
    public float Threshold => _threshold;
    public float CountdownLength => _countdown;

    public KioskIdleTracker(float thresholdSeconds, float countdownSeconds)
    {
        Configure(thresholdSeconds, countdownSeconds);
        Reset();
    }

    public void Configure(float thresholdSeconds, float countdownSeconds)
    {
        _threshold = thresholdSeconds;
        _countdown = countdownSeconds < 0f ? 0f : countdownSeconds;
    }

    public void Reset()
    {
        IdleSeconds = 0f;
        CountdownRemaining = _countdown;
        Phase = KioskIdlePhase.Active;
    }

    /// <summary>카운트다운 표시용 정수 (10, 9, ... 1, 0).</summary>
    public int CountdownDisplay
    {
        get
        {
            int v = (int)Math.Ceiling(CountdownRemaining);
            return v < 0 ? 0 : v;
        }
    }

    /// <summary>카운트다운 남은 비율 (1 → 0).</summary>
    public float CountdownFraction => _countdown > 0f ? Clamp01(CountdownRemaining / _countdown) : 0f;

    public KioskIdlePhase Tick(float deltaTime, bool activity, bool frozen)
    {
        if (activity)
        {
            Reset();
            return Phase;
        }

        if (Phase == KioskIdlePhase.Expired || _threshold <= 0f)
            return Phase;

        float dt = deltaTime;
        if (dt < 0f || float.IsNaN(dt)) dt = 0f;
        if (dt > MaxStep) dt = MaxStep;

        if (Phase == KioskIdlePhase.Active)
        {
            if (frozen)
                return Phase;

            IdleSeconds += dt;
            if (IdleSeconds >= _threshold)
            {
                CountdownRemaining = _countdown;
                Phase = _countdown > 0f ? KioskIdlePhase.Countdown : KioskIdlePhase.Expired;
            }
        }
        else if (Phase == KioskIdlePhase.Countdown)
        {
            CountdownRemaining -= dt;
            if (CountdownRemaining <= 0f)
            {
                CountdownRemaining = 0f;
                Phase = KioskIdlePhase.Expired;
            }
        }

        return Phase;
    }

    static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}

public enum KioskSessionEvent
{
    None = 0,
    Warning = 1,
    CapReached = 2,
}

/// <summary>세션 시계: 상한 N초, 상한 W초 전 경고 1회, 상한 도달은 defer(시네마틱·전투) 동안 최대 maxDefer 초 미룬다.</summary>
public sealed class KioskSessionClock
{
    /// <summary>한 번에 쌓을 최대 시간 (일시정지·로딩도 실제 체험 시간이라 1 s 까지는 센다).</summary>
    public const float MaxStep = 1f;

    float _cap;
    float _warnBefore;
    float _maxDefer;
    bool _warned;
    bool _capped;

    public bool Running { get; private set; }
    public float Elapsed { get; private set; }
    public float CapSeconds => _cap;
    public bool Warned => _warned;
    public bool Capped => _capped;
    public float Remaining => _cap > 0f ? Math.Max(0f, _cap - Elapsed) : float.PositiveInfinity;

    public void Configure(float capSeconds, float warnBeforeSeconds, float maxDeferSeconds)
    {
        _cap = capSeconds < 0f ? 0f : capSeconds;
        _warnBefore = warnBeforeSeconds < 0f ? 0f : warnBeforeSeconds;
        _maxDefer = maxDeferSeconds < 0f ? 0f : maxDeferSeconds;
    }

    public void Start()
    {
        Running = true;
        Elapsed = 0f;
        _warned = false;
        _capped = false;
    }

    /// <summary>세션 종료. 경과 초를 돌려준다.</summary>
    public float Stop()
    {
        float e = Elapsed;
        Running = false;
        return e;
    }

    public KioskSessionEvent Tick(float deltaTime, bool deferCap)
    {
        if (!Running)
            return KioskSessionEvent.None;

        float dt = deltaTime;
        if (dt < 0f || float.IsNaN(dt)) dt = 0f;
        if (dt > MaxStep) dt = MaxStep;
        Elapsed += dt;

        if (_cap <= 0f || _capped)
            return KioskSessionEvent.None;

        if (!_warned && _warnBefore > 0f && Elapsed >= _cap - _warnBefore && Elapsed < _cap)
        {
            _warned = true;
            return KioskSessionEvent.Warning;
        }

        if (Elapsed >= _cap)
        {
            if (deferCap && Elapsed < _cap + _maxDefer)
                return KioskSessionEvent.None;
            _warned = true;
            _capped = true;
            return KioskSessionEvent.CapReached;
        }

        return KioskSessionEvent.None;
    }
}

/// <summary>리셋/세션 종료 사유 (통계 파일에 이 문자열로 기록).</summary>
public static class KioskResetReason
{
    public const string Idle = "idle";
    public const string SessionCap = "session_cap";
    public const string Staff = "staff";
    public const string Completed = "completed";
    public const string TitleMenu = "title_menu";

    public static string Korean(string reason)
    {
        switch (reason)
        {
            case Idle: return "방치";
            case SessionCap: return "시간 상한";
            case Staff: return "스태프 리셋";
            case Completed: return "완주(크레딧)";
            case TitleMenu: return "메뉴에서 타이틀";
            case null:
            case "": return "-";
            default: return reason;
        }
    }
}

/// <summary>스태프 패널 통계 (kiosk_stats.json). JsonUtility 직렬화용 public 필드.</summary>
[Serializable]
public sealed class KioskStatsData
{
    public int version = 1;
    public int sessionCount;
    public double totalSessionSeconds;
    public double longestSessionSeconds;
    public double lastSessionSeconds;
    public int completedCount;
    public int idleResetCount;
    public int sessionCapCount;
    public int staffResetCount;
    public int titleMenuCount;
    public int attractCount;
    public string lastResetReason = string.Empty;
    public string lastResetAt = string.Empty;
    public string firstSessionAt = string.Empty;
    public string lastSessionAt = string.Empty;

    public double AverageSessionSeconds => sessionCount > 0 ? totalSessionSeconds / sessionCount : 0d;

    /// <summary>세션 1회 기록 (사유별 카운트 포함).</summary>
    public void RecordSession(double seconds, string reason, DateTime now)
    {
        if (double.IsNaN(seconds) || seconds < 0d)
            seconds = 0d;

        sessionCount++;
        totalSessionSeconds += seconds;
        lastSessionSeconds = seconds;
        if (seconds > longestSessionSeconds)
            longestSessionSeconds = seconds;

        string stamp = Stamp(now);
        if (string.IsNullOrEmpty(firstSessionAt))
            firstSessionAt = stamp;
        lastSessionAt = stamp;

        CountReason(reason);
    }

    /// <summary>세션 없이 일어난 리셋 (예: 타이틀에서 스태프 강제 리셋).</summary>
    public void RecordReset(string reason, DateTime now)
    {
        lastResetReason = reason ?? string.Empty;
        lastResetAt = Stamp(now);
    }

    void CountReason(string reason)
    {
        switch (reason)
        {
            case KioskResetReason.Idle: idleResetCount++; break;
            case KioskResetReason.SessionCap: sessionCapCount++; break;
            case KioskResetReason.Staff: staffResetCount++; break;
            case KioskResetReason.Completed: completedCount++; break;
            case KioskResetReason.TitleMenu: titleMenuCount++; break;
        }
    }

    public static string Stamp(DateTime now) => now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>초 → "m:ss".</summary>
    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0d || double.IsInfinity(seconds))
            seconds = 0d;
        long total = (long)Math.Floor(seconds);
        return (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}
