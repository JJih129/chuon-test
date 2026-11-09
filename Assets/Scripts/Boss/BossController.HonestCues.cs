using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// [큐 시간 정직성 / Honest cue timing]
// 기존 큐는 "N초 후 판정"이라고 알리지만 실제 히트박스는 공격 클립의 Anim_ActivateHitbox 이벤트에서 열린다.
// QA 측정(QaCombatBot 큐→히트박스 ON): 사전 텔레그래프 0.48s 공지 → 실제 0.69s, 오프너 큐 0.25s → 0.17~0.55s.
// 반응해서 누르는 플레이어는 공지된 시간을 믿으므로, 이 파트는
//   1) 오프너 타이밍 큐: 공격 클립의 첫 히트 이벤트 시각을 읽어서, 실제 남은 시간이 공지 시간 이하가 되는 순간에
//      "실제 남은 시간"으로 큐를 낸다 (늦게 맞는 공격은 큐 시작을 늦추고, 빨리 맞는 공격은 공지 시간을 줄인다).
//   2) 사전 텔레그래프(트리거 전 대기): 공지 시간에 "트리거→첫 히트" 시간(패턴별로 실측해 학습)을 더해
//      링/플래시/HUD 가 실제 판정 순간에 끝나게 한다.
// 기본값 off → 기존 씬(MainScene 등) 동작은 그대로. Level_Facility / Tutorial 보스에서 alignCuesToHitbox 를 켠다.
public partial class BossController
{
    [Header("Honest Cue Timing (큐 시간 = 실제 판정 시작)")]
    [Tooltip("큐/텔레그래프 공지 시간을 실제 히트박스 활성 시각(공격 클립 Anim_ActivateHitbox)에 맞춘다.")]
    [SerializeField] private bool alignCuesToHitbox = false;
    [Tooltip("패턴의 트리거→첫 히트 시간을 아직 측정하지 못했을 때 사전 텔레그래프에 더할 값.")]
    [SerializeField, Min(0f)] private float honestCueFallbackClipLead = 0.2f;
    [Tooltip("오프너 큐 최소 공지 시간 (이보다 짧으면 이 값으로 공지; 실제 판정은 그대로).")]
    [SerializeField, Min(0.02f)] private float honestCueMinDuration = 0.05f;

    Coroutine _alignedOpenerCueRoutine;
    // 패턴 이름 기준 static: 사망 후 씬 재시작에도 학습값 유지 (첫 등장 1회만 폴백 값)
    static readonly Dictionary<string, float> s_learnedTriggerToFirstHit = new Dictionary<string, float>();

    /// <summary>QA/디버그용: 패턴별 학습된 트리거→첫 히트 시간 (초).</summary>
    public string QaLearnedTriggerToHit
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in s_learnedTriggerToFirstHit)
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("F3")).Append(' ');
            return sb.ToString();
        }
    }

    bool IsHonestCueTimingEnabled => alignCuesToHitbox;

    // 트리거 시각(실측 학습 기준) + 마지막 사전 텔레그래프 공지 (QA 로그용)
    float _honestAttackTriggerTime = -1f;
    float _honestPreTelegraphAt = -1f;
    float _honestPreTelegraphAnnounced;
    string _honestLeadSource = "";

    /// <summary>QA용: 공격별 "사전 텔레그래프 공지 / 텔레그래프→트리거 / 리드 출처" 기록.</summary>
    public static readonly System.Text.StringBuilder QaWindupLog = new System.Text.StringBuilder();

    /// <summary>QA용: 학습값 초기화 (첫 등장 정직성 측정).</summary>
    public static void QaResetLearnedWindups()
    {
        s_learnedTriggerToFirstHit.Clear();
        QaWindupLog.Length = 0;
    }

    void NoteHonestPreTelegraph(float announced)
    {
        _honestPreTelegraphAt = Time.time;
        _honestPreTelegraphAnnounced = announced;
    }

    static readonly int s_locomotionStateHash = Animator.StringToHash("Locomotion");

    /// <summary>QA용: 트리거가 대기 상태(턴/런스톱 등)에 막혀 직접 크로스페이드로 시작한 공격 수.</summary>
    public int QaForcedAttackStarts { get; private set; }

    void NoteHonestAttackTrigger(AttackPattern pattern)
    {
        _honestAttackTriggerTime = Time.time;
        if (!alignCuesToHitbox || pattern == null)
            return;

        ForceAttackStateIfTriggerBlocked(pattern.animTriggerName);

        if (QaWindupLog.Length > 20000)
            return;

        float telToTrig = _honestPreTelegraphAt >= 0f ? Time.time - _honestPreTelegraphAt : -1f;
        QaWindupLog.Append('[').Append(Time.time.ToString("F2")).Append("] pat=").Append(pattern.patternName)
            .Append(" announced=").Append(_honestPreTelegraphAnnounced.ToString("F3"))
            .Append(" tel->trig=").Append(telToTrig.ToString("F3"))
            .Append(" lead=").Append(_honestLeadSource).AppendLine();
        _honestPreTelegraphAt = -1f;
    }

    // HandleAttackTimingTelegraph 에서 호출: true 면 큐를 지금 내지 않고 실제 판정 시각에 맞춰 낸다.
    bool TryDeferOpenerCueToHitbox(BossAttackTelegraphType telegraphType, float duration)
    {
        if (!alignCuesToHitbox || _timingDataControlsHitbox || bossAnimator == null || _currentPattern == null)
            return false;

        StopAlignedOpenerCue();
        float alreadyElapsed = _hasActiveAttackTimingData ? _activeAttackTimingData.telegraphStartTime : 0f;
        _alignedOpenerCueRoutine = StartCoroutine(Co_AlignedOpenerCue(_currentPattern, telegraphType, duration, alreadyElapsed));
        return true;
    }

    void StopAlignedOpenerCue()
    {
        if (_alignedOpenerCueRoutine == null)
            return;

        StopCoroutine(_alignedOpenerCueRoutine);
        _alignedOpenerCueRoutine = null;
    }

    IEnumerator Co_AlignedOpenerCue(AttackPattern pattern, BossAttackTelegraphType telegraphType, float designedDuration, float elapsedBeforeCue)
    {
        // 실제 트리거 시각 (없으면 타이밍 컨트롤러 경과 시간으로 추정)
        float triggerTime = _honestAttackTriggerTime >= 0f && Time.time - _honestAttackTriggerTime < 3f
            ? _honestAttackTriggerTime
            : Time.time - elapsedBeforeCue;
        yield return null; // 트리거가 애니메이터에 반영될 때까지

        AnimationClip clip;
        float[] hits;
        float waited = 0f;
        while (!TryResolveOpenerClip(out clip, out hits))
        {
            if (!IsPerHitPatternActive(pattern))
            {
                _alignedOpenerCueRoutine = null;
                yield break;
            }

            if (waited > PerHitClipResolveTimeout)
            {
                // 클립을 못 찾으면 기존 방식(설계 시간)으로 낸다
                _alignedOpenerCueRoutine = null;
                EmitAttackTimingTelegraph(telegraphType, designedDuration);
                yield break;
            }

            waited += Time.deltaTime;
            yield return null;
        }

        while (IsPerHitPatternActive(pattern) && TrySamplePerHitClip(clip, out float clipTime, out float rate))
        {
            float toHit = (hits[0] - clipTime) / rate;
            if (toHit < -0.02f)
                break; // 이미 지나감 (프레임 드랍)

            if (toHit <= designedDuration + 0.005f)
            {
                LearnTriggerToFirstHit(pattern, Time.time - triggerTime + Mathf.Max(0f, toHit));
                _alignedOpenerCueRoutine = null;
                EmitAttackTimingTelegraph(telegraphType, Mathf.Max(honestCueMinDuration, toHit));
                yield break;
            }

            yield return null;
        }

        _alignedOpenerCueRoutine = null;
    }

    // 공격 트리거 전이는 Locomotion / CombatStrafe 에서만 있다. 턴(TurnL90 등)/RunStop/RunStart/백스텝 중에 트리거하면
    // 그 클립이 끝날 때까지 공격이 0.1~0.4s 늦게 시작해서 사전 텔레그래프가 거짓말이 된다 (QA: 같은 패턴 slack +0.0~+0.42s).
    // → 그런 상태면 공격 스테이트로 바로 크로스페이드하고 트리거는 지운다 (나중에 다시 발동하지 않게).
    void ForceAttackStateIfTriggerBlocked(string triggerName)
    {
        if (bossAnimator == null || !bossAnimator.isActiveAndEnabled || string.IsNullOrEmpty(triggerName) || bossAnimator.runtimeAnimatorController == null)
            return;

        AnimatorStateInfo current = bossAnimator.GetCurrentAnimatorStateInfo(0);
        // CombatStrafe 는 PlayAnimTrigger 가 IsCombatStrafing 을 끄면서 Locomotion 전이(0.06s)가 먼저 걸리므로 막힌 것으로 본다.
        bool canTakeTrigger = !bossAnimator.IsInTransition(0) && current.shortNameHash == s_locomotionStateHash;
        if (canTakeTrigger)
            return;

        int fullPathHash = Animator.StringToHash(bossAnimator.GetLayerName(0) + "." + triggerName);
        if (!bossAnimator.HasState(0, fullPathHash))
            return;

        bossAnimator.ResetTrigger(triggerName);
        bossAnimator.CrossFadeInFixedTime(fullPathHash, 0.05f, 0, 0f);
        QaForcedAttackStarts++;
    }

    void LearnTriggerToFirstHit(AttackPattern pattern, float seconds)
    {
        if (pattern == null || seconds <= 0f || seconds > 3f)
            return;

        string key = LearnKey(pattern);
        s_learnedTriggerToFirstHit[key] = s_learnedTriggerToFirstHit.TryGetValue(key, out float previous)
            ? Mathf.Lerp(previous, seconds, 0.5f)
            : seconds;
    }

    // 사전 텔레그래프(트리거 직전 대기)에 더할 "트리거→첫 히트" 시간. off 면 0.
    // 우선순위: 실측 학습값 → 에디터 베이크값(BossWindupBakeData, 첫 등장도 정직) → 폴백.
    float ResolveHonestPreTelegraphClipLead(AttackPattern pattern)
    {
        if (!alignCuesToHitbox || pattern == null)
            return 0f;

        if (s_learnedTriggerToFirstHit.TryGetValue(LearnKey(pattern), out float learned))
        {
            _honestLeadSource = "learned " + learned.ToString("F3");
            return learned;
        }

        if (TryGetBakedTriggerToFirstHit(pattern, out float baked))
        {
            _honestLeadSource = "baked " + baked.ToString("F3");
            return baked;
        }

        _honestLeadSource = "fallback";
        return honestCueFallbackClipLead;
    }

    bool TryGetBakedTriggerToFirstHit(AttackPattern pattern, out float seconds)
    {
        seconds = 0f;
        BossWindupBakeData data = BossWindupBakeData.Instance;
        if (data == null || bossAnimator == null || !data.TryGet(bossAnimator.runtimeAnimatorController, pattern.animTriggerName, out float clipSeconds))
            return false;

        // 트리거는 다음 애니메이터 업데이트에 반영 → 1프레임 추가. 애니메이터 속도 반영.
        float animSpeed = bossAnimator.speed > 0.05f ? bossAnimator.speed : 1f;
        seconds = clipSeconds / animSpeed + Mathf.Clamp(Time.smoothDeltaTime, 0f, 0.05f);
        return true;
    }

    static string LearnKey(AttackPattern pattern) => pattern.patternName + "|" + pattern.animTriggerName;

    bool TryResolveOpenerClip(out AnimationClip clip, out float[] hits)
    {
        clip = null;
        hits = null;
        if (bossAnimator == null || !bossAnimator.isActiveAndEnabled)
            return false;

        if (bossAnimator.IsInTransition(0))
        {
            bossAnimator.GetNextAnimatorClipInfo(0, _perHitClipInfos);
            if (PickClipWithHits(out clip, out hits))
                return true;
        }

        bossAnimator.GetCurrentAnimatorClipInfo(0, _perHitClipInfos);
        if (!PickClipWithHits(out clip, out hits))
            return false;

        // 첫 히트를 이미 지난 클립(이전 패턴의 잔상)이면 아직 새 클립이 아니다
        if (TrySamplePerHitClip(clip, out float clipTime, out _) && clipTime > hits[0])
        {
            clip = null;
            hits = null;
            return false;
        }

        return true;
    }

    bool PickClipWithHits(out AnimationClip clip, out float[] hits)
    {
        clip = null;
        hits = null;
        float bestWeight = 0f;
        for (int i = 0; i < _perHitClipInfos.Count; i++)
        {
            AnimationClip candidate = _perHitClipInfos[i].clip;
            float[] candidateHits = GetPerHitTimes(candidate);
            if (candidateHits.Length < 1 || _perHitClipInfos[i].weight < bestWeight)
                continue;

            bestWeight = _perHitClipInfos[i].weight;
            clip = candidate;
            hits = candidateHits;
        }

        return clip != null;
    }
}
