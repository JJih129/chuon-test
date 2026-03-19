using System;
using System.Collections;
using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

public class PlayerUltimateController : MonoBehaviour
{
    [Header("Gauge")]
    [Tooltip("Maximum ultimate gauge value.")]
    public float gaugeMax = 100f;
    [Tooltip("Gauge gained from normal attack hits.")]
    public float gaugePerH = 3f;
    [Tooltip("Gauge gained from successful parries.")]
    public float gaugePerParry = 6f;
    [Tooltip("Gauge gained from successful perfect dodges.")]
    public float gaugePerPerfectDodge = 16f;

    [Header("Ultimate Activation")]
    [Tooltip("Optional Input System action for ultimate activation.")]
    public InputActionReference activateAction;
    [Tooltip("Allow legacy hotkey fallback when no action is bound.")]
    public bool useLegacyHotkey = true;
    [Tooltip("Legacy fallback key for ultimate activation.")]
    public KeyCode legacyHotkey = KeyCode.R;
    [Tooltip("Ignore activation input while a global input blocker is active.")]
    public bool ignoreActivationWhenBlocked = true;

    [Header("Activation Rules")]
    [Tooltip("Allow activation while airborne.")]
    public bool allowInAir = false;
    [Tooltip("Block activation while staggered.")]
    public bool blockWhenStaggered = true;
    [Tooltip("Block activation while attacking.")]
    public bool blockWhenAttacking = true;
    [Tooltip("Block activation while guarding.")]
    public bool blockWhenGuarding = true;
    [Tooltip("Block activation while dodging.")]
    public bool blockWhenDodging = true;

    [Header("Cinematic State")]
    [Tooltip("Enable invulnerability during the cinematic.")]
    public bool invulnerableDuringCinematic = true;
    [Tooltip("Block player input during the cinematic.")]
    public bool lockInputDuringCinematic = true;
    [Tooltip("Restore lock-on camera control after the cinematic.")]
    public bool restoreCameraAndLockOn = true;
    [Tooltip("Freeze gameplay world time while the cinematic runs.")]
    public bool freezeWorldTimeDuringCinematic = true;

    [Header("Damage")]
    [Tooltip("Fixed damage applied on the finisher hit.")]
    public int finisherFixedDamage = 1200;
    [Tooltip("Fixed damage applied on each multi-hit slash.")]
    public int multihitFixedDamage = 120;

    [Header("Timeline")]
    [Tooltip("PlayableDirector used for the ultimate cinematic.")]
    public PlayableDirector director;
    [Tooltip("Optional virtual cameras used by the sequence.")]
    public CinemachineVirtualCamera[] vCams;

    [Header("Screen Effects")]
    [Tooltip("Screen FX controller used during the ultimate.")]
    public UltimateScreenFX screenFX;
    [Tooltip("Optional crack effect spawned on the finisher.")]
    public GameObject glassCrackPrefab;

    [Header("Events")]
    public Action OnUltimateStarted;
    public Action OnUltimateEnded;

    public float Gauge { get; private set; }
    public bool IsCinematic => _isCinematic;

    bool _isCinematic;
    float _cachedTimeScale = 1f;
    float _cachedFixedDeltaTime = 0.02f;
    DirectorUpdateMode _cachedDirectorTimeUpdateMode = DirectorUpdateMode.GameTime;
    IInputBlocker _input;
    ILockOnController _lockOn;
    IInvulnerabilityToggle _invul;
    ICombatStateReader _combat;

    void Awake()
    {
        _input = GetComponent<IInputBlocker>();
        var playerLockOn = GetComponent<PlayerLockOn>();
        _lockOn = playerLockOn as ILockOnController ?? GetComponent<ILockOnController>();
        _invul = GetComponent<IInvulnerabilityToggle>();
        _combat = GetComponent<ICombatStateReader>();
    }

    void OnEnable()
    {
        if (activateAction?.action != null)
            activateAction.action.Enable();
    }

    void OnDisable()
    {
        if (activateAction?.action != null)
            activateAction.action.Disable();
    }

    void Update()
    {
        if (_isCinematic) return;
        if (!ShouldConsumeActivationInput()) return;

        TryActivate();
    }

    public void AddGauge(float amount)
    {
        if (_isCinematic) return;

        Gauge = Mathf.Clamp(Gauge + amount, 0f, gaugeMax);
        UI_UltimateGauge.UpdateValue(Gauge / gaugeMax);
    }

    public bool TryActivate()
    {
        if (_isCinematic) return false;
        if (ignoreActivationWhenBlocked && _input != null && _input.IsBlocked) return false;
        if (Gauge < gaugeMax) return false;
        if (_combat != null)
        {
            if (blockWhenStaggered && _combat.IsStaggered()) return false;
            if (!allowInAir && _combat.IsInAir()) return false;
            if (blockWhenAttacking && _combat.IsAttacking()) return false;
            if (blockWhenGuarding && _combat.IsGuarding()) return false;
            if (blockWhenDodging && _combat.IsDodging()) return false;
        }

        StartCoroutine(Co_Cinematic());
        return true;
    }

    bool ShouldConsumeActivationInput()
    {
        if (ignoreActivationWhenBlocked && _input != null && _input.IsBlocked)
            return false;

        if (activateAction != null && activateAction.action != null && activateAction.action.WasPressedThisFrame())
            return true;

        if (useLegacyHotkey && Input.GetKeyDown(legacyHotkey))
            return true;

        return false;
    }

    IEnumerator Co_Cinematic()
    {
        _isCinematic = true;
        Gauge = 0f;
        UI_UltimateGauge.UpdateValue(0f);

        if (lockInputDuringCinematic) _input?.BlockAll(true);
        if (invulnerableDuringCinematic) _invul?.SetInvulnerable(true);
        _lockOn?.GiveCameraControlToTimeline(true);

        OnUltimateStarted?.Invoke();
        screenFX?.PlayChargeIn();

        if (director != null)
            _cachedDirectorTimeUpdateMode = director.timeUpdateMode;

        if (freezeWorldTimeDuringCinematic)
        {
            _cachedTimeScale = Time.timeScale;
            _cachedFixedDeltaTime = Time.fixedDeltaTime;
            Time.timeScale = 0f;
            Time.fixedDeltaTime = 0f;
        }

        if (director != null)
        {
            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            director.time = 0d;
            director.Evaluate();
            director.Play();
        }

        while (director != null && director.state == PlayState.Playing)
            yield return null;

        screenFX?.PlayChargeOut();
        if (director != null)
            director.timeUpdateMode = _cachedDirectorTimeUpdateMode;
        if (freezeWorldTimeDuringCinematic)
        {
            Time.timeScale = _cachedTimeScale;
            Time.fixedDeltaTime = _cachedFixedDeltaTime;
        }
        if (restoreCameraAndLockOn) _lockOn?.GiveCameraControlToTimeline(false);
        if (invulnerableDuringCinematic) _invul?.SetInvulnerable(false);
        if (lockInputDuringCinematic) _input?.BlockAll(false);

        OnUltimateEnded?.Invoke();
        _isCinematic = false;
    }

    public void OnMultiHit()
    {
        ApplyBurstDamage(multihitFixedDamage);
        screenFX?.PulseMinor();
    }

    public void OnFinisher()
    {
        ApplyBurstDamage(finisherFixedDamage);
        if (glassCrackPrefab) Instantiate(glassCrackPrefab, Vector3.zero, Quaternion.identity);
        screenFX?.PulseMajor();
    }

    void ApplyBurstDamage(int damage)
    {
        var target = _lockOn?.GetCurrentTarget();
        var ultimateTarget = ResolveLockedUltimateTarget(target);
        if (ultimateTarget != null)
        {
            ultimateTarget.ApplyUltimateDamage(damage);
            return;
        }

        if (_lockOn != null && _lockOn.IsLockedOn())
        {
            Debug.LogWarning("[Ultimate] Locked target has no IUltimateTarget. Fallback skipped to keep lock-on target priority.", this);
            return;
        }

        var boss = GameObject.FindWithTag("Boss");
        if (boss != null && boss.TryGetComponent<IUltimateTarget>(out var fallbackTarget))
            fallbackTarget.ApplyUltimateDamage(damage);
    }

    IUltimateTarget ResolveLockedUltimateTarget(Transform target)
    {
        if (target == null)
            return null;

        if (target.TryGetComponent<IUltimateTarget>(out var direct))
            return direct;

        var parentTarget = target.GetComponentInParent<IUltimateTarget>();
        if (parentTarget != null)
            return parentTarget;

        if (target.root != null && target.root.TryGetComponent<IUltimateTarget>(out var rootTarget))
            return rootTarget;

        return null;
    }
}
