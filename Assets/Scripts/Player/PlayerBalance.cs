using System;
using UnityEngine;

/// <summary>
/// 플레이어 밸런스(체간) 게이지 — 세키로/스텔라 블레이드식.
/// - 막기/피격으로 쌓이고, 퍼펙트 패링으로 깎이며, 일정 시간 증가가 없으면 자연 회복
/// - 가득 차면 가드 브레이크(기존 PlayerGuardController 경직 경로 재사용)
/// - 브레이크 판정/연출은 PlayerGuardController가 담당하고, 이 컴포넌트는 수치/이벤트만 관리
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("ChuOn/Combat/Player Balance")]
public class PlayerBalance : MonoBehaviour
{
    [Header("① 기본")]
    [Tooltip("밸런스 게이지 사용")]
    public bool enableBalance = true;
    [Tooltip("최대 밸런스")]
    public float maxBalance = 100f;
    [Tooltip("가득 차면 가드 브레이크(경직) 발생")]
    public bool enableBreak = true;
    [Range(0f, 1f)]
    [Tooltip("브레이크 후 게이지가 되돌아가는 비율(최대치 대비)")]
    public float brokenResetNormalized = 0.35f;

    [Header("② 증가")]
    [Tooltip("막을 때마다 고정으로 쌓이는 양")]
    public float blockFlatGain = 6f;
    [Tooltip("막은 피해 1당 쌓이는 양")]
    public float blockDamageGainMul = 1f;
    [Tooltip("가드 없이 맞았을 때 피해 1당 쌓이는 양")]
    public float unguardedHitDamageMul = 0.3f;
    [Tooltip("가드 없이 맞았을 때 고정으로 쌓이는 양")]
    public float unguardedHitFlatGain = 0f;
    [Tooltip("강공격(Strong/Heavy/Force) 또는 가드불가 공격 배수")]
    public float heavyHitMultiplier = 1.5f;

    [Header("③ 감소/회복")]
    [Tooltip("퍼펙트 패링 성공 시 깎이는 양")]
    public float perfectParryDrain = 15f;
    [Tooltip("마지막 증가 후 회복 시작까지 대기(초, 실시간)")]
    public float regenDelay = 1.2f;
    [Tooltip("초당 회복량")]
    public float regenPerSecond = 22f;
    [Tooltip("가드 중 회복 배수(0이면 가드 중 회복 없음)")]
    public float guardingRegenMultiplier = 1.5f;

    [Header("④ HUD")]
    [Tooltip("플레이어 HUD 캔버스 아래에 밸런스 바를 자동 생성")]
    public bool autoCreateHudBar = true;

    /// <summary>(current, max)</summary>
    public event Action<float, float> Changed;
    public event Action Broken;
    public event Action Recovered;

    public float Current => _current;
    public float Max => Mathf.Max(0.0001f, maxBalance);
    public float Normalized => !enableBalance || maxBalance <= 0f ? 0f : Mathf.Clamp01(_current / maxBalance);
    public bool IsEnabled => enableBalance && maxBalance > 0f;
    public bool IsBroken => _broken;

    float _current;
    bool _broken;
    float _brokenUntilRealtime = float.NegativeInfinity;
    float _regenAllowedRealtime = float.NegativeInfinity;
    float _nextHudAttemptRealtime;
    PlayerGuardController _guard;
    PlayerHealth _health;
    PlayerBalanceHudBar _hudBar;

    void Awake()
    {
        _guard = GetComponent<PlayerGuardController>();
        _health = GetComponent<PlayerHealth>();
    }

    void OnEnable()
    {
        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_health != null) _health.OnDied += HandleDied;
    }

    void OnDisable()
    {
        if (_health != null) _health.OnDied -= HandleDied;
    }

    void Update()
    {
        float now = Time.realtimeSinceStartup;

        if (_broken && now >= _brokenUntilRealtime)
        {
            _broken = false;
            _regenAllowedRealtime = now + Mathf.Max(0f, regenDelay);
            Recovered?.Invoke();
        }

        TickRegen(now);
        TryEnsureHudBar(now);
    }

    void TickRegen(float now)
    {
        if (!IsEnabled || _broken || _current <= 0f || now < _regenAllowedRealtime)
            return;

        float rate = Mathf.Max(0f, regenPerSecond);
        if (_guard == null) _guard = GetComponent<PlayerGuardController>();
        if (_guard != null && _guard.IsGuarding)
            rate *= Mathf.Max(0f, guardingRegenMultiplier);

        if (rate <= 0f)
            return;

        SetCurrent(_current - (rate * Time.unscaledDeltaTime));
    }

    void TryEnsureHudBar(float now)
    {
        if (!autoCreateHudBar || _hudBar != null || now < _nextHudAttemptRealtime)
            return;

        _nextHudAttemptRealtime = now + 0.5f;
        _hudBar = PlayerBalanceHudBar.EnsureFor(this);
    }

    // ───────────────────── 증가/감소 API ─────────────────────
    public static bool IsHeavyHit(HitType hitType, bool unblockable)
    {
        return unblockable || hitType == HitType.Strong || hitType == HitType.Heavy || hitType == HitType.Force;
    }

    /// <summary>막기 1회. 가득 찼으면 true(브레이크 처리는 호출자 담당).</summary>
    public bool AddFromBlock(float blockedDamage, bool heavy)
    {
        float amount = blockFlatGain + (Mathf.Max(0f, blockedDamage) * blockDamageGainMul);
        return Add(amount * (heavy ? Mathf.Max(0f, heavyHitMultiplier) : 1f));
    }

    /// <summary>가드 없이 피격. 가득 찼으면 true.</summary>
    public bool AddFromUnguardedHit(float damage, bool heavy)
    {
        float amount = unguardedHitFlatGain + (Mathf.Max(0f, damage) * unguardedHitDamageMul);
        return Add(amount * (heavy ? Mathf.Max(0f, heavyHitMultiplier) : 1f));
    }

    /// <summary>임의 증가. 브레이크 중에는 무시. 가득 찼으면 true.</summary>
    public bool Add(float amount)
    {
        if (!IsEnabled || _broken || amount <= 0f)
            return false;

        _regenAllowedRealtime = Time.realtimeSinceStartup + Mathf.Max(0f, regenDelay);
        SetCurrent(_current + amount);
        return _current + 0.001f >= maxBalance;
    }

    public void Drain(float amount)
    {
        if (!IsEnabled || amount <= 0f)
            return;

        SetCurrent(_current - amount);
    }

    public void DrainFromPerfectParry() => Drain(perfectParryDrain);

    /// <summary>브레이크 진입(PlayerGuardController.TriggerGuardBreak에서 호출).</summary>
    public void EnterBroken(float duration)
    {
        float now = Time.realtimeSinceStartup;
        _brokenUntilRealtime = Mathf.Max(_brokenUntilRealtime, now + Mathf.Max(0.01f, duration));
        _regenAllowedRealtime = _brokenUntilRealtime + Mathf.Max(0f, regenDelay);

        bool wasBroken = _broken;
        _broken = true;
        if (IsEnabled)
            SetCurrent(maxBalance * Mathf.Clamp01(brokenResetNormalized));

        if (!wasBroken)
            Broken?.Invoke();
    }

    public void ResetBalance()
    {
        bool wasBroken = _broken;
        _broken = false;
        _brokenUntilRealtime = float.NegativeInfinity;
        _regenAllowedRealtime = float.NegativeInfinity;
        SetCurrent(0f);
        if (wasBroken)
            Recovered?.Invoke();
    }

    void HandleDied() => ResetBalance();

    void SetCurrent(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, Mathf.Max(0f, maxBalance));
        if (Mathf.Approximately(clamped, _current))
            return;

        _current = clamped;
        Changed?.Invoke(_current, Max);
    }

    /// <summary>레거시 가드 안정도 값 복사(런타임 자동 추가 시 프리팹 튜닝 유지용).</summary>
    public void ApplyLegacyGuardStrainTuning(
        bool enabled,
        float max,
        float perBlockedDamage,
        float flatPerBlock,
        float recoverDelay,
        float recoverPerSecond,
        float parryRecover)
    {
        enableBalance = enabled;
        maxBalance = max;
        blockDamageGainMul = perBlockedDamage;
        blockFlatGain = flatPerBlock;
        regenDelay = recoverDelay;
        regenPerSecond = recoverPerSecond;
        perfectParryDrain = parryRecover;
    }
}
