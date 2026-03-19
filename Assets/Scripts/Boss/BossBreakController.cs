using UnityEngine;
using UnityEngine.Events;

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

    [Tooltip("Default break gain per valid parry.")]
    [Min(0f)] public float baseBreakPerHit = 10f;

    [Tooltip("How long the boss stays in break state.")]
    [Min(0f)] public float breakDuration = 5f;

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

    [Header("04. Debug")]
    [Tooltip("Enable debug logging for break flow.")]
    public bool logDebug = false;

    private float _currentBreak;
    private float _breakTimer;
    private bool _isInBreak;
    private int _breakBoolHash;

    public float NormalizedBreak => Mathf.Clamp01(maxBreak > 0f ? _currentBreak / maxBreak : 0f);

    public float Get01() => NormalizedBreak;

    public bool IsInBreak => _isInBreak;

    private void Reset()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator) bossAnimator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator) bossAnimator = GetComponentInChildren<Animator>();

        if (bossHealth != null)
            bossHealth.OnDied += HandleBossDied;

        _breakBoolHash = Animator.StringToHash(breakBoolName);
    }

    private void OnDestroy()
    {
        if (bossHealth != null)
            bossHealth.OnDied -= HandleBossDied;
    }

    private void Update()
    {
        if (!_isInBreak)
            return;

        _breakTimer -= Time.deltaTime;
        if (_breakTimer <= 0f)
            ForceExitBreak();
    }

    // Legacy damage path intentionally left disabled.
    private void HandleBossDamaged(int damage)
    {
        if (damage <= 0) return;
        if (_isInBreak) return;

        float add = baseBreakPerHit;
        _currentBreak = Mathf.Min(maxBreak, _currentBreak + add);

        if (logDebug)
            Debug.Log($"[Break] Damage={damage}, +{add} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak >= maxBreak)
            ForceEnterBreak();
    }

    public void AddBreak(float amount, BreakSource source)
    {
        if (source != BreakSource.Parry) return;
        if (_isInBreak) return;

        float add = amount > 0f ? amount : baseBreakPerHit;
        if (add <= 0f) return;

        _currentBreak = Mathf.Min(maxBreak, _currentBreak + add);

        if (logDebug)
            Debug.Log($"[Break] Source={source}, +{add} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak >= maxBreak)
            ForceEnterBreak();
    }

    public void ForceEnterBreak()
    {
        if (_isInBreak) return;

        _isInBreak = true;
        _breakTimer = breakDuration;

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

        _currentBreak = 0f;
        OnBreakExit?.Invoke();

        if (logDebug)
            Debug.Log("[Break] EXIT", this);
    }

    public void ResetGauge()
    {
        _currentBreak = 0f;

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

    private void OnValidate()
    {
        maxBreak = Mathf.Max(1f, maxBreak);
        baseBreakPerHit = Mathf.Max(0f, baseBreakPerHit);
        breakDuration = Mathf.Max(0f, breakDuration);
        recoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
    }
}
