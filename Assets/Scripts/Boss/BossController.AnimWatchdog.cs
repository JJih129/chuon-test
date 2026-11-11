// [반복 22: 보스 애니메이터 감시견]
// QA I21 b_0.9_6: 애니메이터가 DownLoop → GetUp(Falldown_F_End) 전환 중에 50 s 동안 멈춰 있었다
// (TypeDOverhaul.DownLoopWatchdog 이 매 프레임 CrossFade 를 다시 걸어 전환이 계속 재시작). AI 는 패턴·큐를 계속 골랐지만
// 휘두르기/히트박스가 나오지 않았다. 원인은 고쳤고, 같은 종류의 고착이 다시 생겨도 전투가 굳지 않도록 여기서 감시한다.
//  - 비반복 상태가 자기 길이보다 2 s 넘게 머무르면 → Locomotion 으로 강제 복귀
//  - 같은 전환이 (전환 길이 + 2 s) 넘게 끝나지 않으면 → 목표 상태로 즉시 점프
// 브레이크·사망·처형·인트로·궁극기 피격 등 의도적으로 포즈를 붙잡는 구간은 제외한다.
using UnityEngine;

public partial class BossController
{
    const float AnimWatchdogOverstaySeconds = 2f;

    int _animWatchdogTransitionKey;
    float _animWatchdogTransitionElapsed;
    float _animWatchdogCooldownUntil;

    /// <summary>QA용: 감시견이 강제 복구한 횟수.</summary>
    public int QaAnimWatchdogRecoveries { get; private set; }

    bool IsAnimWatchdogSuspended()
    {
        if (bossAnimator == null || !bossAnimator.isActiveAndEnabled || bossAnimator.runtimeAnimatorController == null)
            return true;
        if (_isDead || _isUltimateVictim || _externalIntroPaused || IsOverhaulHeld)
            return true;
        if (currentState == BossState.IntroIdle || currentState == BossState.Break || currentState == BossState.Dead)
            return true;
        if (breakController != null && breakController.IsInBreak)
            return true;
        if (ExecutionDirector.IsExecuting)
            return true;
        // 경직/브레이크 동결(speed 0) 동안은 시간이 흐르지 않으므로 고착이 아니다
        return bossAnimator.speed < 0.01f || Time.deltaTime <= 0f;
    }

    void TickAnimatorWatchdog()
    {
        if (IsAnimWatchdogSuspended())
        {
            _animWatchdogTransitionKey = 0;
            _animWatchdogTransitionElapsed = 0f;
            return;
        }

        AnimatorStateInfo cur = bossAnimator.GetCurrentAnimatorStateInfo(0);
        if (bossAnimator.IsInTransition(0))
        {
            AnimatorStateInfo next = bossAnimator.GetNextAnimatorStateInfo(0);
            int key = cur.shortNameHash * 31 + next.shortNameHash;
            if (key != _animWatchdogTransitionKey)
            {
                _animWatchdogTransitionKey = key;
                _animWatchdogTransitionElapsed = 0f;
            }
            _animWatchdogTransitionElapsed += Time.deltaTime;

            AnimatorTransitionInfo ti = bossAnimator.GetAnimatorTransitionInfo(0);
            float duration = ti.durationUnit == DurationUnit.Fixed ? ti.duration : ti.duration * Mathf.Max(0.01f, cur.length);
            if (_animWatchdogTransitionElapsed > Mathf.Max(0.25f, duration) + AnimWatchdogOverstaySeconds && Time.time >= _animWatchdogCooldownUntil)
            {
                RecoverStuckAnimator($"transition {QaStateName(cur.shortNameHash)} -> {QaStateName(next.shortNameHash)} stuck {_animWatchdogTransitionElapsed:F1}s (dur {duration:F2}s)",
                    next.shortNameHash);
            }
            return;
        }

        _animWatchdogTransitionKey = 0;
        _animWatchdogTransitionElapsed = 0f;
        if (cur.loop || cur.shortNameHash == s_locomotionStateHash)
            return;
        float overstay = (cur.normalizedTime - 1f) * cur.length;
        if (overstay > AnimWatchdogOverstaySeconds && Time.time >= _animWatchdogCooldownUntil)
            RecoverStuckAnimator($"state {QaStateName(cur.shortNameHash)} held {overstay:F1}s past its length {cur.length:F2}s", s_locomotionStateHash);
    }

    void RecoverStuckAnimator(string reason, int targetHash)
    {
        _animWatchdogCooldownUntil = Time.time + 1f;
        _animWatchdogTransitionKey = 0;
        _animWatchdogTransitionElapsed = 0f;
        QaAnimWatchdogRecoveries++;
        if (!bossAnimator.HasState(0, targetHash))
            targetHash = s_locomotionStateHash;
        Debug.LogWarning($"[BossAnimWatchdog] force-recover: {reason} (bossState={currentState}, t={Time.time:F1})");
        bossAnimator.Play(targetHash, 0, 0f);
    }
}
