using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class PerfectDodgeController : MonoBehaviour
{
    [Header("① 퍼펙트 회피 창(초)")]
    [Tooltip("Pulse 호출 시 열리는 기본 윈도우 길이의 하한/상한")]
    [Min(0f)] public float minWindow = 0.08f;
    [Min(0f)] public float maxWindow = 0.50f;

    [Header("② 슬로우모션")]
    [Range(0.01f, 1f)] public float slowTimeScale = 0.12f;
    [Min(0.01f)] public float slowDuration = 0.10f;
    [Tooltip("물리까지 느리게(권장 On)")] public bool adjustFixedDelta = true;

    [Header("③ 애니메이터 강제 스케일 적용")]
    [Tooltip("슬로우 동안 Animator.UpdateMode = Normal 강제")]
    public bool forceAnimatorsToScaled = true;
    [Tooltip("비워두면 자식 포함 자동 수집")]
    public bool includeChildAnimators = true;
    [Tooltip("일부 Unscaled 에셋 대응: Animator.speed = timeScale 강제")]
    public bool overrideAnimatorSpeed = true;
    public List<Animator> animatorsToAffect = new List<Animator>();

    [Header("④ 이동 잠금(CombatMoveLocker 연동)")]
    [SerializeField] CombatMoveLocker moveLocker;
    [Tooltip("윈도우가 열려있는 동안 일반 이동 금지")]
    public bool lockMoveWhileWindow = true;
    [Tooltip("0이면 '윈도우가 닫힐 때까지' 잠금, >0이면 고정 시간만 잠금")]
    [Min(0f)] public float lockWindowFixedDuration = 0f;
    [Tooltip("윈도우 잠금 시 속도 0으로")] public bool zeroVelocityOnWindow = true;
    [Tooltip("윈도우 잠금 시 루트모션 OFF")] public bool disableRootMotionOnWindow = false;

    [Tooltip("슬로우 연출 동안 일반 이동 금지")]
    public bool lockMoveDuringSlow = true;
    [Tooltip("슬로우 잠금 시 속도 0")] public bool zeroVelocityOnSlow = false;
    [Tooltip("슬로우 잠금 시 루트모션 OFF")] public bool disableRootMotionOnSlow = false;

    [Header("⑤ 피드백/게이지(선택)")]
    public ParryFeedbackController feedback;
    public bool autoFeedback = true;
    public PlayerUltimateController ultimate;
    public float ultimateGainOnPerfect = 7f;
    public bool autoUltimateGain = true;

    [Header("⑥ UnityEvent (인스펙터 연결)")]
    public UnityEvent OnWindowOpened;
    public UnityEvent OnWindowClosed;
    public UnityEvent OnPerfectDodge;

    [Header("⑦ 디버그")]
    public bool enableLogs = true;

    // 내부 상태
    float _remain;
    bool _windowOpen;
    float _initialFixedDelta;
    Coroutine _coSlow;
    Coroutine _coClose;
    readonly Dictionary<Animator, (AnimatorUpdateMode mode, float speed)> _animatorBackup
        = new Dictionary<Animator, (AnimatorUpdateMode, float)>();

    void Awake()
    {
        _initialFixedDelta = Time.fixedDeltaTime;

        if (animatorsToAffect.Count == 0)
        {
            if (includeChildAnimators)
                animatorsToAffect.AddRange(GetComponentsInChildren<Animator>(true));
            else
            {
                var a = GetComponent<Animator>();
                if (a != null) animatorsToAffect.Add(a);
            }
        }
    }

    // ===== 애니 이벤트 진입점 =====
    // PerfectDodgeWindow_Pulse(0.16)
    public void PerfectDodgeWindow_Pulse(float seconds) => PerfectDodgeWindow_Open(seconds);
    // PerfectDodgeWindow_Pulse()
    public void PerfectDodgeWindow_Pulse() => PerfectDodgeWindow_Open(0.16f);

    public void PerfectDodgeWindow_Open(float seconds)
    {
        seconds = Mathf.Clamp(seconds, minWindow, maxWindow);
        _remain += seconds;

        if (!_windowOpen)
        {
            _windowOpen = true;

            // 이동 잠금(윈도우)
            if (lockMoveWhileWindow && moveLocker)
            {
                if (lockWindowFixedDuration > 0f)
                    moveLocker.Lock("PD_WINDOW", lockWindowFixedDuration, zeroVelocityOnWindow, disableRootMotionOnWindow);
                else
                    moveLocker.Lock("PD_WINDOW", 0f, zeroVelocityOnWindow, disableRootMotionOnWindow); // 닫힐 때 수동 해제
            }

            OnWindowOpened?.Invoke();
        }

        StartCloseTimer(_remain);

        if (enableLogs)
            Debug.Log($"[PD] Window OPEN (+{seconds:0.000}s) | remain={_remain:0.000}s", this);
    }

    public void PerfectDodgeWindow_Close()
    {
        if (!_windowOpen) return;
        _windowOpen = false;
        _remain = 0f;

        if (lockMoveWhileWindow && moveLocker && lockWindowFixedDuration <= 0f)
            moveLocker.Unlock("PD_WINDOW");

        OnWindowClosed?.Invoke();
        if (enableLogs) Debug.Log("[PD] Window CLOSE", this);
    }

    // (선택) 클립 말미에 달면 슬로우 즉시 종료
    public void PerfectDodge_EndSlow() => ForceEndSlow();

    void StartCloseTimer(float seconds)
    {
        if (_coClose != null) { StopCoroutine(_coClose); _coClose = null; }
        _coClose = StartCoroutine(CoCloseAfter(seconds));
    }

    IEnumerator CoCloseAfter(float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
            yield return null;

        PerfectDodgeWindow_Close();
        _coClose = null;
    }

    // PlayerDamageReceiver에서 일반피해 직전 분기:
    // if (perfectDodge && perfectDodge.ResolvePerfectDodge(hitPoint, attacker)) return;
    public bool ResolvePerfectDodge(Vector3 hitPoint, Transform attacker)
    {
        if (!_windowOpen) return false;

        PerfectDodgeWindow_Close();           // 창 닫기
        StartSlow(slowTimeScale, slowDuration); // 슬로우 시작

        if (autoFeedback && feedback) feedback.PlayPerfectDodgeFeedback();
        if (autoUltimateGain && ultimate) ultimate.AddGauge(ultimateGainOnPerfect);
        OnPerfectDodge?.Invoke();

        if (enableLogs)
            Debug.Log($"[PD] PERFECT! slow={slowTimeScale:0.000} for {slowDuration:0.000}s", this);

        return true;
    }

    // ===== 슬로우 =====
    void StartSlow(float scale, float duration)
    {
        ForceEndSlow(); // 중복 슬로우 정리

        if (forceAnimatorsToScaled) ApplyAnimatorEnforcement(scale);

        Time.timeScale = Mathf.Clamp(scale, 0.01f, 1f);
        if (adjustFixedDelta) Time.fixedDeltaTime = _initialFixedDelta * Time.timeScale;

        // 이동 잠금(슬로우)
        if (lockMoveDuringSlow && moveLocker)
            moveLocker.Lock("PD_SLOW", duration, zeroVelocityOnSlow, disableRootMotionOnSlow);

        _coSlow = StartCoroutine(CoSlowMotion(duration));
    }

    IEnumerator CoSlowMotion(float duration)
    {
        float end = Time.realtimeSinceStartup + duration;
        while (Time.realtimeSinceStartup < end)
            yield return null;

        ForceEndSlow();
        if (enableLogs) Debug.Log("[PD] Slow END(Auto)", this);
    }

    [ContextMenu("Force end slow (debug)")]
    public void ForceEndSlow()
    {
        if (_coSlow != null) { StopCoroutine(_coSlow); _coSlow = null; }

        if (Time.timeScale != 1f) Time.timeScale = 1f;
        if (adjustFixedDelta && Mathf.Abs(Time.fixedDeltaTime - _initialFixedDelta) > 0.0001f)
            Time.fixedDeltaTime = _initialFixedDelta;

        RestoreAnimators();

        // 슬로우용 잠금 해제
        if (lockMoveDuringSlow && moveLocker)
            moveLocker.Unlock("PD_SLOW");

        if (enableLogs) Debug.Log("[PD] Slow END → timeScale=1", this);
    }

    // ===== 애니메이터 강제/원복 =====
    void ApplyAnimatorEnforcement(float scale)
    {
        _animatorBackup.Clear();
        foreach (var a in animatorsToAffect)
        {
            if (!a) continue;
            if (!_animatorBackup.ContainsKey(a))
                _animatorBackup.Add(a, (a.updateMode, a.speed));
            a.updateMode = AnimatorUpdateMode.Normal;
            if (overrideAnimatorSpeed) a.speed = scale;
        }
    }

    void RestoreAnimators()
    {
        if (_animatorBackup.Count == 0) return;
        foreach (var kv in _animatorBackup)
        {
            var a = kv.Key;
            if (!a) continue;
            a.updateMode = kv.Value.mode;
            a.speed = kv.Value.speed;
        }
        _animatorBackup.Clear();
    }
}
