using UnityEngine;
using UnityEngine.Events;
using System;

[DisallowMultipleComponent]
public class BossBreakController : MonoBehaviour
{
    public enum BreakSource
    {
        Generic = 0,
        Parry = 1
    }

    [Header("01. Break Gauge")]
    [Tooltip("Maximum break gauge value.")]
    [Min(1f)] public float maxBreak = 100f;

    [Tooltip("Break damage applied by a regular successful player hit.")]
    [Min(0f)] public float basicAttackBreakDamage = 1f;

    [Tooltip("Break damage applied by a successful parry.")]
    [Min(0f)] public float baseBreakPerHit = 12.5f;

    [Tooltip("How long the boss stays in break state.")]
    [Min(0f)] public float breakDuration = 8f;

    [Tooltip("Legacy recovery value. Kept for inspector compatibility.")]
    [Min(0f)] public float recoveryPerSecond = 15f;

    [Header("02. References")]
    [Tooltip("Boss health/stagger controller.")]
    [SerializeField] private BossHealth bossHealth;

    [Tooltip("Boss animator that uses the break bool.")]
    [SerializeField] private Animator bossAnimator;

    [Tooltip("Animator bool parameter name for break state.")]
    [SerializeField] private string breakBoolName = "IsBreak";

    [Header("03. Events")]
    [Tooltip("Invoked when break state starts.")]
    public UnityEvent OnBreakEnter = new UnityEvent();

    [Tooltip("Invoked when break state ends.")]
    public UnityEvent OnBreakExit = new UnityEvent();

    public event Action<float, float> OnBreakChanged;

    [Header("04. Debug")]
    [Tooltip("Enable debug logging for break flow.")]
    public bool logDebug = false;

    private float _currentBreak;
    private float _breakTimer;
    private bool _isInBreak;
    private int _breakBoolHash;
    private PlayerUltimateController _ultimateController;
    private bool _hasUltimatePresentationSnapshot;
    private float _ultimatePresentationBreakSnapshot;
    private float _ultimatePresentationBreakTimerSnapshot;
    private bool _ultimatePresentationWasInBreak;

    public float NormalizedBreak => Mathf.Clamp01(maxBreak > 0f ? _currentBreak / maxBreak : 0f);
    public float CurrentBreak => _currentBreak;

    public float Get01() => NormalizedBreak;

    public bool IsInBreak => _isInBreak;
    public float RemainingBreakTime => _breakTimer;

    private void Reset()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator)
        {
            BossReferences bossReferences = GetComponent<BossReferences>();
            if (bossReferences != null && bossReferences.MainAnimator != null)
                bossAnimator = bossReferences.MainAnimator;
            if (!bossAnimator) bossAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        }
    }

    private void Awake()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator)
        {
            BossReferences bossReferences = GetComponent<BossReferences>();
            if (bossReferences != null && bossReferences.MainAnimator != null)
                bossAnimator = bossReferences.MainAnimator;
            if (!bossAnimator) bossAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        }

        if (bossHealth != null)
            bossHealth.OnDied += HandleBossDied;

        _ultimateController = GameplaySceneCache.ResolvePlayerUltimateController();
        if (_ultimateController != null)
        {
            _ultimateController.OnUltimateStarted += HandleUltimateStarted;
            _ultimateController.OnUltimateEnded += HandleUltimateEnded;
        }

        _breakBoolHash = Animator.StringToHash(breakBoolName);
        ResetGauge();
        enabled = false;
    }

    private void OnDestroy()
    {
        if (bossHealth != null)
            bossHealth.OnDied -= HandleBossDied;

        if (_ultimateController != null)
        {
            _ultimateController.OnUltimateStarted -= HandleUltimateStarted;
            _ultimateController.OnUltimateEnded -= HandleUltimateEnded;
        }
    }

    private void Update()
    {
        if (!_isInBreak)
            return;

        _breakTimer -= Time.deltaTime;
        if (_breakTimer <= 0f)
            ForceExitBreak();
    }

    public void NotifyBossDamaged(int damage)
    {
        if (damage <= 0) return;
        if (_isInBreak) return;

        float remove = basicAttackBreakDamage;
        _currentBreak = Mathf.Max(0f, _currentBreak - remove);
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] Damage={damage}, -{remove} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak <= 0f)
            ForceEnterBreak();
    }

    public void AddBreak(float amount, BreakSource source)
    {
        if (_isInBreak) return;

        float remove = amount > 0f
            ? amount
            : source == BreakSource.Parry
                ? baseBreakPerHit
                : basicAttackBreakDamage;
        if (remove <= 0f) return;

        _currentBreak = Mathf.Max(0f, _currentBreak - remove);
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] Source={source}, -{remove} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak <= 0f)
            ForceEnterBreak();
    }

    public void ForceEnterBreak()
    {
        if (_isInBreak) return;

        _currentBreak = 0f;
        RaiseBreakChanged();
        _isInBreak = true;
        _breakTimer = breakDuration;
        enabled = true;

        if (bossHealth != null)
            bossHealth.SetStaggered(true);

        if (bossAnimator != null && _breakBoolHash != 0)
            bossAnimator.SetBool(_breakBoolHash, true);

        OnBreakEnter?.Invoke();

        if (logDebug)
            Debug.Log("[Break] ENTER", this);
    }

    public void ForceExitBreak()
    {
        if (!_isInBreak) return;

        _isInBreak = false;
        _breakTimer = 0f;

        if (bossHealth != null)
            bossHealth.SetStaggered(false);

        if (bossAnimator != null && _breakBoolHash != 0)
            bossAnimator.SetBool(_breakBoolHash, false);

        _currentBreak = maxBreak;
        RaiseBreakChanged();
        OnBreakExit?.Invoke();
        enabled = false;

        if (logDebug)
            Debug.Log("[Break] EXIT", this);
    }

    public void RestoreStateAfterPresentation(float preservedBreak, bool preservedInBreak, float preservedBreakTimer)
    {
        if (bossHealth != null && bossHealth.IsDead)
            return;

        if (preservedInBreak)
        {
            bool wasInBreak = _isInBreak;
            _currentBreak = 0f;
            _isInBreak = true;
            _breakTimer = Mathf.Max(_breakTimer, Mathf.Max(0f, preservedBreakTimer));
            enabled = true;

            if (bossHealth != null)
                bossHealth.SetStaggered(true);

            if (bossAnimator != null && _breakBoolHash != 0)
                bossAnimator.SetBool(_breakBoolHash, true);

            RaiseBreakChanged();

            if (!wasInBreak)
                OnBreakEnter?.Invoke();

            return;
        }

        if (_isInBreak)
            return;

        float clampedBreak = Mathf.Clamp(preservedBreak, 0f, maxBreak);
        if (_currentBreak <= clampedBreak + 0.001f)
            return;

        _currentBreak = clampedBreak;
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] RestoreAfterPresentation -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);
    }

    public void ResetGauge()
    {
        _currentBreak = maxBreak;
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log("[Break] ResetGauge()", this);
    }

    private void HandleBossDied()
    {
        ForceExitBreak();
        ResetGauge();

        if (bossHealth != null)
            bossHealth.OnDied -= HandleBossDied;
    }

    private void HandleUltimateStarted()
    {
        _ultimatePresentationBreakSnapshot = _currentBreak;
        _ultimatePresentationBreakTimerSnapshot = _breakTimer;
        _ultimatePresentationWasInBreak = _isInBreak;
        _hasUltimatePresentationSnapshot = true;

        if (logDebug)
            Debug.Log($"[Break] SnapshotForUltimate -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00}) inBreak={_isInBreak}", this);
    }

    private void HandleUltimateEnded()
    {
        if (!_hasUltimatePresentationSnapshot)
            return;

        _hasUltimatePresentationSnapshot = false;
        RestoreStateAfterPresentation(
            _ultimatePresentationBreakSnapshot,
            _ultimatePresentationWasInBreak,
            _ultimatePresentationBreakTimerSnapshot);
    }

    void RaiseBreakChanged()
    {
        OnBreakChanged?.Invoke(NormalizedBreak, _currentBreak);
    }

    private void OnValidate()
    {
        maxBreak = Mathf.Max(1f, maxBreak);
        basicAttackBreakDamage = Mathf.Max(0f, basicAttackBreakDamage);
        baseBreakPerHit = Mathf.Max(0f, baseBreakPerHit);
        breakDuration = Mathf.Max(0f, breakDuration);
        recoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
    }
}
