using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("ChuOn/Combat/CombatMoveLocker")]
[DisallowMultipleComponent]
public class CombatMoveLocker : MonoBehaviour
{
    [Header("Targets to disable while locked")]
    [Tooltip("잠금 동안 비활성화할 이동/입력 컴포넌트들(예: PlayerMoveController, PlayerInputReader 등)")]
    [SerializeField] Behaviour[] moveBehaviours;

    [Tooltip("루트모션을 잠글 때 대상 애니메이터(비워두면 자식에서 자동 탐색)")]
    [SerializeField] Animator rootMotionAnimator;

    [Tooltip("속도 0으로 만들 대상(있으면 권장)")]
    [SerializeField] Rigidbody rigidbodyRef;

    [Header("Defaults on Lock")]
    [Tooltip("잠금 시 기본으로 속도를 0으로 만들지 여부(개별 Lock 호출에서 재정의 가능)")]
    [SerializeField] bool defaultZeroVelocity = true;

    [Tooltip("잠금 시 기본으로 루트모션 비활성화(개별 Lock 호출에서 재정의 가능)")]
    [SerializeField] bool defaultDisableRootMotion = true;

    [Header("Debug")]
    [SerializeField] bool log = false;

    struct LockOptions
    {
        public bool zeroVelocity;
        public bool disableRootMotion;
        public float expireAtRealtime; // <= 0이면 수동 해제
        public Coroutine timer;
    }

    readonly Dictionary<string, LockOptions> _reasons = new Dictionary<string, LockOptions>();
    bool _applied; // 현재 잠금 적용 상태
    Animator _cachedAnimator;

    void Awake()
    {
        if (!rootMotionAnimator)
            _cachedAnimator = GetComponentInChildren<Animator>(true);
        else
            _cachedAnimator = rootMotionAnimator;
    }

    public bool IsLocked => _reasons.Count > 0;

    /// <summary>
    /// 이동/입력 잠금. 같은 reason으로 중복 호출하면 옵션만 갱신.
    /// seconds==0이면 수동 해제 필요, >0이면 자동 해제(Realtime).
    /// </summary>
    public void Lock(string reason, float seconds = 0f, bool? zeroVelocity = null, bool? disableRootMotion = null)
    {
        if (string.IsNullOrEmpty(reason)) reason = "DEFAULT";

        // 기존 옵션 가져오거나 기본 옵션으로 초기화
        var opt = _reasons.ContainsKey(reason) ? _reasons[reason] : new LockOptions
        {
            zeroVelocity = defaultZeroVelocity,
            disableRootMotion = defaultDisableRootMotion,
            expireAtRealtime = 0f,
            timer = null
        };

        // 옵션 갱신
        if (zeroVelocity.HasValue)     opt.zeroVelocity      = zeroVelocity.Value;
        if (disableRootMotion.HasValue) opt.disableRootMotion = disableRootMotion.Value;

        // 타이머 갱신
        if (opt.timer != null) { StopCoroutine(opt.timer); opt.timer = null; }
        if (seconds > 0f)
        {
            opt.expireAtRealtime = Time.realtimeSinceStartup + seconds;
            opt.timer = StartCoroutine(CoAutoUnlock(reason, seconds));
        }
        else opt.expireAtRealtime = 0f;

        _reasons[reason] = opt;

        ApplyLockIfNeeded();
        if (log) Debug.Log($"[Locker] LOCK '{reason}' (count={_reasons.Count})", this);
    }

    public void Unlock(string reason)
    {
        if (string.IsNullOrEmpty(reason)) reason = "DEFAULT";
        if (!_reasons.TryGetValue(reason, out var opt)) return;

        if (opt.timer != null) StopCoroutine(opt.timer);
        _reasons.Remove(reason);
        if (log) Debug.Log($"[Locker] UNLOCK '{reason}' (count={_reasons.Count})", this);

        if (_reasons.Count == 0) ClearLock();
        else ApplyLockIfNeeded(); // 남은 리즌들의 옵션에 맞춰 재적용(집계)
    }

    public void ForceUnlockAll()
    {
        foreach (var kv in _reasons)
            if (kv.Value.timer != null) StopCoroutine(kv.Value.timer);
        _reasons.Clear();
        ClearLock();
        if (log) Debug.Log("[Locker] FORCE UNLOCK ALL", this);
    }

    IEnumerator CoAutoUnlock(string reason, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        Unlock(reason);
    }

    void ApplyLockIfNeeded()
    {
        // 어떤 리즌이라도 존재하면 잠금 ON, 옵션은 OR/집계 방식으로 결정
        bool wantZeroVel = false;
        bool wantDisableRM = false;

        foreach (var kv in _reasons)
        {
            wantZeroVel   |= kv.Value.zeroVelocity;
            wantDisableRM |= kv.Value.disableRootMotion;
        }

        // 최초 적용
        if (!_applied)
        {
            _applied = true;
            foreach (var b in moveBehaviours)
                if (b) b.enabled = false;
        }

        // 매 프레임(호출 시점) 보정
        if (rigidbodyRef && wantZeroVel)
        {
            rigidbodyRef.velocity        = Vector3.zero;
            rigidbodyRef.angularVelocity = Vector3.zero;
        }

        if (_cachedAnimator && _cachedAnimator.enabled)
            _cachedAnimator.applyRootMotion = !wantDisableRM;
    }

    void ClearLock()
    {
        if (!_applied) return;
        _applied = false;

        foreach (var b in moveBehaviours)
            if (b) b.enabled = true;

        if (_cachedAnimator) _cachedAnimator.applyRootMotion = true;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!rootMotionAnimator)
            _cachedAnimator = GetComponentInChildren<Animator>(true);
        else
            _cachedAnimator = rootMotionAnimator;
    }
#endif
}
