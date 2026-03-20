using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class PerfectDodgeController : MonoBehaviour
{
    public bool IsWindowOpen => _windowOpen;
    public float RemainingWindow => _remain;

    [SerializeField] private PlayerReferences playerReferences;
    [SerializeField] private PlayerDodgeController dodgeController;

    [Header("Perfect Dodge Window")]
    [Min(0f)] public float minWindow = 0.08f;
    [Min(0f)] public float maxWindow = 0.50f;

    [Header("Perfect Dodge Time")]
    [Range(0.01f, 1f)] public float initialFreezeTimeScale = 0.04f;
    [Min(0f)] public float initialFreezeDuration = 0.03f;
    [Range(0.01f, 1f)] public float slowTimeScale = 0.28f;
    [Min(0.01f)] public float slowDuration = 0.12f;
    [SerializeField] private PlayerHealth playerHealth;
    [Min(0f)] public float perfectDodgeInvincibleRealtime = 0.50f;
    [Tooltip("Adjust fixedDeltaTime while the slow effect is active.")]
    public bool adjustFixedDelta = true;
    public AnimationCurve slowRecoverCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Animator Override")]
    [Tooltip("Force affected animators to scaled-time update while the slow effect is active.")]
    public bool forceAnimatorsToScaled = true;
    [Tooltip("Collect child animators automatically when the explicit list is empty.")]
    public bool includeChildAnimators = true;
    [Tooltip("Drive animator speed to match the active time scale.")]
    public bool overrideAnimatorSpeed = true;
    public List<Animator> animatorsToAffect = new List<Animator>();

    [Header("Movement Lock")]
    [SerializeField] private CombatMoveLocker moveLocker;
    public bool lockMoveWhileWindow = true;
    [Min(0f)] public float lockWindowFixedDuration = 0f;
    public bool zeroVelocityOnWindow = true;
    public bool disableRootMotionOnWindow = false;

    public bool lockMoveDuringSlow = true;
    public bool zeroVelocityOnSlow = false;
    public bool disableRootMotionOnSlow = false;

    [Header("Feedback")]
    public ParryFeedbackController feedback;
    public bool autoFeedback = true;
    public PlayerUltimateController ultimate;
    public float ultimateGainOnPerfect = 7f;
    public bool autoUltimateGain = true;

    [Header("Events")]
    public UnityEvent OnWindowOpened;
    public UnityEvent OnWindowClosed;
    public UnityEvent OnPerfectDodge;

    [Header("Debug")]
    public bool enableLogs = true;

    private float _remain;
    private bool _windowOpen;
    private float _initialFixedDelta;
    private Coroutine _coSlow;
    private Coroutine _coClose;
    private readonly Dictionary<Animator, (AnimatorUpdateMode mode, float speed)> _animatorBackup
        = new Dictionary<Animator, (AnimatorUpdateMode, float)>();

    void Awake()
    {
        if (!playerReferences) playerReferences = GetComponent<PlayerReferences>();
        if (!dodgeController) dodgeController = GetComponent<PlayerDodgeController>();
        if (!playerHealth) playerHealth = GetComponent<PlayerHealth>();
        if (!moveLocker) moveLocker = GetComponent<CombatMoveLocker>();

        _initialFixedDelta = Time.fixedDeltaTime;
        EnsureAnimatorList();

        if (slowRecoverCurve == null || slowRecoverCurve.length == 0)
            slowRecoverCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    public void PerfectDodgeWindow_Pulse(float seconds) => PerfectDodgeWindow_Open(seconds);
    public void PerfectDodgeWindow_Pulse() => PerfectDodgeWindow_Open(0.16f);

    public void PerfectDodgeWindow_Open(float seconds)
    {
        seconds = Mathf.Clamp(seconds, minWindow, maxWindow);
        _remain = _windowOpen ? Mathf.Max(_remain, seconds) : seconds;

        if (!_windowOpen)
        {
            _windowOpen = true;

            if (lockMoveWhileWindow && moveLocker)
            {
                if (lockWindowFixedDuration > 0f)
                    moveLocker.Lock("PD_WINDOW", lockWindowFixedDuration, zeroVelocityOnWindow, disableRootMotionOnWindow);
                else
                    moveLocker.Lock("PD_WINDOW", 0f, zeroVelocityOnWindow, disableRootMotionOnWindow);
            }

            OnWindowOpened?.Invoke();
        }

        StartCloseTimer(_remain);

        if (enableLogs)
            Debug.Log($"[PD] Window open +{seconds:0.000}s (remain={_remain:0.000}s)", this);
    }

    public void PerfectDodgeWindow_Close()
    {
        if (!_windowOpen) return;

        _windowOpen = false;
        _remain = 0f;

        if (lockMoveWhileWindow && moveLocker && lockWindowFixedDuration <= 0f)
            moveLocker.Unlock("PD_WINDOW");

        OnWindowClosed?.Invoke();

        if (enableLogs)
            Debug.Log("[PD] Window close", this);
    }

    public void PerfectDodge_EndSlow() => ForceEndSlow();

    void StartCloseTimer(float seconds)
    {
        if (_coClose != null)
        {
            StopCoroutine(_coClose);
            _coClose = null;
        }

        _coClose = StartCoroutine(CoCloseAfter(seconds));
    }

    IEnumerator CoCloseAfter(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
            yield return null;

        PerfectDodgeWindow_Close();
        _coClose = null;
    }

    public bool ResolvePerfectDodge(Vector3 hitPoint, Transform attacker)
    {
        if (!_windowOpen) return false;

        PerfectDodgeWindow_Close();
        if (dodgeController != null)
            dodgeController.ApplyPerfectDodgeSideStep(attacker);
        if (playerHealth != null && perfectDodgeInvincibleRealtime > 0f)
            playerHealth.SetInvincibleRealtime(perfectDodgeInvincibleRealtime);
        StartSlow(slowTimeScale, slowDuration);

        if (autoFeedback && feedback)
            feedback.PlayPerfectDodgeFeedback(hitPoint, attacker);

        if (autoUltimateGain && ultimate)
            ultimate.AddGauge(ultimateGainOnPerfect);

        OnPerfectDodge?.Invoke();

        if (enableLogs)
            Debug.Log(
                $"[PD] Perfect dodge freeze={initialFreezeTimeScale:0.000}/{initialFreezeDuration:0.000}s slow={slowTimeScale:0.000}/{slowDuration:0.000}s",
                this);

        return true;
    }

    void StartSlow(float scale, float duration)
    {
        ForceEndSlow();
        EnsureAnimatorList();

        if (forceAnimatorsToScaled)
            BeginAnimatorEnforcement();

        float totalDuration = Mathf.Max(0f, initialFreezeDuration) + Mathf.Max(0.01f, duration);
        if (lockMoveDuringSlow && moveLocker)
            moveLocker.Lock("PD_SLOW", totalDuration, zeroVelocityOnSlow, disableRootMotionOnSlow);

        _coSlow = StartCoroutine(CoPerfectDodgeTimeEffect(Mathf.Clamp(scale, 0.01f, 1f), Mathf.Max(0.01f, duration)));
    }

    IEnumerator CoPerfectDodgeTimeEffect(float slowScale, float recoverDuration)
    {
        float freezeDuration = Mathf.Max(0f, initialFreezeDuration);
        if (freezeDuration > 0f)
        {
            ApplyTimeScale(Mathf.Clamp(initialFreezeTimeScale, 0.01f, 1f));

            float freezeEnd = Time.realtimeSinceStartup + freezeDuration;
            while (Time.realtimeSinceStartup < freezeEnd)
                yield return null;
        }

        float elapsed = 0f;
        while (elapsed < recoverDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / recoverDuration);
            float eased = Mathf.Clamp01(slowRecoverCurve.Evaluate(t));
            float currentScale = Mathf.Lerp(slowScale, 1f, eased);
            ApplyTimeScale(currentScale);
            yield return null;
        }

        CompleteSlow(false);

        if (enableLogs)
            Debug.Log("[PD] Slow end (auto)", this);
    }

    [ContextMenu("Force end slow (debug)")]
    public void ForceEndSlow()
    {
        if (_coSlow != null)
        {
            StopCoroutine(_coSlow);
            _coSlow = null;
        }

        CompleteSlow(true);
    }

    void CompleteSlow(bool clearCoroutineReference)
    {
        ApplyTimeScale(1f);
        RestoreAnimators();

        if (lockMoveDuringSlow && moveLocker)
            moveLocker.Unlock("PD_SLOW");

        if (clearCoroutineReference)
            _coSlow = null;

        if (enableLogs)
            Debug.Log("[PD] Slow reset -> timeScale=1", this);
    }

    void ApplyTimeScale(float scale)
    {
        scale = Mathf.Clamp(scale, 0.0001f, 1f);
        Time.timeScale = scale;

        if (adjustFixedDelta)
            Time.fixedDeltaTime = _initialFixedDelta * scale;

        if (forceAnimatorsToScaled && overrideAnimatorSpeed)
            ApplyAnimatorSpeed(scale);
    }

    void EnsureAnimatorList()
    {
        if (animatorsToAffect.Count > 0) return;

        if (includeChildAnimators)
        {
            animatorsToAffect.AddRange(GetComponentsInChildren<Animator>(true));
            return;
        }

        Animator animator = playerReferences != null
            ? playerReferences.MainAnimator ?? GetComponent<Animator>()
            : GetComponent<Animator>();

        if (animator != null)
            animatorsToAffect.Add(animator);
    }

    void BeginAnimatorEnforcement()
    {
        _animatorBackup.Clear();

        foreach (Animator animator in animatorsToAffect)
        {
            if (!animator) continue;
            if (_animatorBackup.ContainsKey(animator)) continue;

            _animatorBackup.Add(animator, (animator.updateMode, animator.speed));
            animator.updateMode = AnimatorUpdateMode.Normal;
        }
    }

    void ApplyAnimatorSpeed(float scale)
    {
        foreach (Animator animator in animatorsToAffect)
        {
            if (!animator) continue;
            animator.speed = scale;
        }
    }

    void RestoreAnimators()
    {
        if (_animatorBackup.Count == 0) return;

        foreach (KeyValuePair<Animator, (AnimatorUpdateMode mode, float speed)> kv in _animatorBackup)
        {
            Animator animator = kv.Key;
            if (!animator) continue;

            animator.updateMode = kv.Value.mode;
            animator.speed = kv.Value.speed;
        }

        _animatorBackup.Clear();
    }
}
