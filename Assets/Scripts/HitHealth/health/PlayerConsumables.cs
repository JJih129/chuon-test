// Assets/Scripts/HitHealth/health/PlayerConsumables.cs
// 회복 앰플 (기획: 최대 HP 35% 회복, 캡슐을 깨는 짧은 동작, 슈퍼아머 없음 — 동작 중 피격/회피/공격으로 끊기면
// 회복은 취소되고 앰플은 이미 소모된 상태로 남는다).
using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerConsumables : MonoBehaviour
{
    [Header("Ampoule")]
    public int maxAmpoule = 4;
    public int currentAmpoule = 4;
    [Range(0f, 1f)] public float healRatio = 0.35f;
    public bool allowUseAtFullHP = true;

    [Header("Heal Motion (캡슐 깨기)")]
    [Tooltip("끄면 예전처럼 즉시 회복")]
    public bool useHealMotion = true;
    [Tooltip("동작 전체 길이(초)")]
    [Min(0.1f)] public float healMotionDuration = 0.7f;
    [Tooltip("캡슐이 깨지며 회복이 들어가는 시점 (동작 길이 대비 0..1). 이 전에 끊기면 회복 취소")]
    [Range(0.1f, 1f)] public float healApplyNormalized = 0.6f;
    [Tooltip("동작 중 이동 잠금")]
    public bool lockMovementDuringHeal = true;

    public int MaxAmpoule => maxAmpoule;
    public int CurrentAmpoule => currentAmpoule;
    public bool IsHealing => _healRoutine != null;
    /// <summary>현재 회복 동작 진행도 0..1 (동작 중이 아니면 0).</summary>
    public float HealMotionProgress { get; private set; }

    public event Action<int, int> OnAmpouleChanged; // current, max
    public event Action OnHealStarted;
    public event Action<int> OnHealApplied;          // healed amount
    public event Action OnHealCancelled;

    /// <summary>모든 플레이어 회복 시작 알림 (보스 반응형 패턴 등).</summary>
    public static event Action<PlayerConsumables> AnyHealStarted;

    const string MoveLockReason = "AmpouleHeal";

    PlayerHealth health;
    ICombatStateReader _combatState;
    IInputBlocker _inputBlocker;
    CombatMoveLocker _moveLocker;
    Coroutine _healRoutine;
    bool _interrupted;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        _combatState = GetComponent<ICombatStateReader>();
        _inputBlocker = GetComponent<IInputBlocker>();
        _moveLocker = GetComponent<CombatMoveLocker>();
        if (useHealMotion && GetComponent<PlayerHealMotion>() == null)
            gameObject.AddComponent<PlayerHealMotion>();
    }

    void OnEnable()
    {
        if (health == null) health = GetComponent<PlayerHealth>();
        if (health != null) health.OnDamaged += HandleDamaged;
    }

    void OnDisable()
    {
        if (health != null) health.OnDamaged -= HandleDamaged;
    }

    void OnDestroy()
    {
        if (health != null) health.OnDamaged -= HandleDamaged;
        ReleaseMoveLock();
    }

    void Start()
    {
        OnAmpouleChanged?.Invoke(currentAmpoule, maxAmpoule);
        RefreshInputPollingState();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
            TryUseAmpoule();
    }

    /// <summary>앰플 사용 시도. 동작이 시작되면(또는 즉시 회복되면) true.</summary>
    public bool TryUseAmpoule()
    {
        if (currentAmpoule <= 0) { GameLog.Info("[Consumables] No ampoule"); return false; }
        if (health.IsDead) { GameLog.Info("[Consumables] Player dead"); return false; }
        if (IsHealing) return false;
        if (!allowUseAtFullHP && health.CurrentHP >= health.MaxHP) { GameLog.Info("[Consumables] HP full"); return false; }
        if (_inputBlocker != null && _inputBlocker.IsBlocked) return false;
        if (useHealMotion && IsBusy()) return false;

        currentAmpoule--;
        OnAmpouleChanged?.Invoke(currentAmpoule, maxAmpoule);

        if (!useHealMotion)
        {
            ApplyHeal();
            RefreshInputPollingState();
            return true;
        }

        _healRoutine = StartCoroutine(Co_HealMotion());
        return true;
    }

    bool IsBusy()
    {
        if (_combatState == null)
            return false;
        return _combatState.IsAttacking() || _combatState.IsDodging() || _combatState.IsStaggered() || _combatState.IsInAir();
    }

    IEnumerator Co_HealMotion()
    {
        _interrupted = false;
        HealMotionProgress = 0f;
        if (lockMovementDuringHeal && _moveLocker != null)
            _moveLocker.Lock(MoveLockReason, 0f, true, null);

        OnHealStarted?.Invoke();
        AnyHealStarted?.Invoke(this);

        float duration = Mathf.Max(0.1f, healMotionDuration);
        float applyAt = duration * Mathf.Clamp01(healApplyNormalized);
        bool applied = false;
        float t = 0f;
        while (t < duration)
        {
            if (!applied && (_interrupted || health.IsDead || (_combatState != null && (_combatState.IsDodging() || _combatState.IsAttacking() || _combatState.IsInHitState()))))
            {
                CancelHeal();
                yield break;
            }

            if (!applied && t >= applyAt)
            {
                applied = true;
                ApplyHeal();
                // 회복 이후 남은 동작은 회수 동작: 이동 잠금만 먼저 풀어 준다 (리스크는 회복 전 구간)
                ReleaseMoveLock();
            }

            t += Time.deltaTime;
            HealMotionProgress = Mathf.Clamp01(t / duration);
            yield return null;
        }

        if (!applied)
            ApplyHeal();

        FinishMotion();
    }

    void ApplyHeal()
    {
        int amount = Mathf.RoundToInt(health.MaxHP * healRatio);
        health.Heal(amount);
        OnHealApplied?.Invoke(amount);
        HealFeedbackHUD.ShowApplied(healRatio);
    }

    void CancelHeal()
    {
        FinishMotion();
        OnHealCancelled?.Invoke();
        HealFeedbackHUD.ShowCancelled();
    }

    void FinishMotion()
    {
        ReleaseMoveLock();
        _healRoutine = null;
        _interrupted = false;
        HealMotionProgress = 0f;
        RefreshInputPollingState();
    }

    void ReleaseMoveLock()
    {
        if (_moveLocker != null)
            _moveLocker.Unlock(MoveLockReason);
    }

    void HandleDamaged(int amount)
    {
        if (IsHealing && amount > 0)
            _interrupted = true; // 슈퍼아머 없음: 회복 전에 맞으면 취소 (앰플은 이미 소모)
    }

    /// <summary>진행 중인 회복 동작을 즉시 끊는다 (컷신/리스폰 등). 회복 전이면 취소 처리.</summary>
    public void InterruptHeal()
    {
        if (!IsHealing)
            return;
        StopCoroutine(_healRoutine);
        CancelHeal();
    }

    public void Refill()
    {
        currentAmpoule = maxAmpoule;
        OnAmpouleChanged?.Invoke(currentAmpoule, maxAmpoule);
        RefreshInputPollingState();
    }

    void RefreshInputPollingState()
    {
        // 동작 중에는 코루틴이 끝날 때까지 켜 둔다 (Update 폴링만 영향)
        enabled = currentAmpoule > 0 || IsHealing;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => AnyHealStarted = null;
}
