// [반복 16: Type-D 보스 개편 — BossController 쪽 훅] (Docs/Boss_TypeD_Phases.md)
//  1) 외부 반응(움찔/경직/넉백/다운/벽 박힘/과부하/전환 컷): 패링 경직과 같은 슬롯(_parryStunRoutine)을 써서
//     기존 가드(공격 진행·특수 패턴·연계 검사)가 그대로 '끊긴 상태'로 본다. 끝나면 전투 복구(BossCombatRecoveryController)로 돌아간다.
//  2) 외부 정지(처형): AI 루틴을 멈추고 애니메이션은 호출자가 직접 재생. 해제 시 브레이크면 Break, 아니면 복구.
//  3) 새 특수 패턴 3종(자홍 잡기 / 배기 AoE / 임계 폭주) — 정직한 큐(OverhaulCueLedger 로 공지 대비 실제 판정 시각 측정).
//  4) 페이즈 문턱(66 % / 30 %) 고정, 지연 공격(페이즈 2+), 파괴된 칼날 콤보 제거, 실명 시 조준 회전 끔.
// TypeDOverhaul 이 없으면(MainScene 등) 아무것도 바뀌지 않는다.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BossOverhaulReaction
{
    None = 0,
    Flinch = 1,
    Stagger = 2,
    Knockback = 3,
    Down = 4,
    WallSplat = 5,
    Overload = 6,
    Transition = 7
}

public partial class BossController
{
    // ---- TypeDOverhaulBuilder 가 BossAnimator 에 추가하는 상태 이름 (없으면 HasState 검사로 건너뜀)
    public const string OverhaulStateFlinch = "OH_Flinch";
    public const string OverhaulStateStagger = "OH_Stagger";
    public const string OverhaulStateKnockback = "OH_Knockback";
    public const string OverhaulStateWallSplat = "OH_WallSplat";
    public const string OverhaulStateDownStart = "OH_DownStart";
    public const string OverhaulStateDownLoop = "OH_DownLoop";
    public const string OverhaulStateGetUp = "OH_GetUp";
    public const string OverhaulStateRoar = "OH_Roar";
    public const string OverhaulStateGrab = "OH_Grab";
    public const string OverhaulStateVent = "OH_Vent";
    public const string OverhaulStateDespCharge = "OH_DespCharge";
    public const string OverhaulStateDespRelease = "OH_DespRelease";
    public const string OverhaulStateExecutedFront = "OH_ExecutedFront";
    public const string OverhaulStateExecutedBack = "OH_ExecutedBack";
    public const string OverhaulStateExecutedFinal = "OH_ExecutedFinal";

    /// <summary>TypeDOverhaul 이 Awake 에서 등록. null 이면 개편 기능 전부 꺼짐.</summary>
    public TypeDOverhaul Overhaul { get; internal set; }

    Coroutine _overhaulReactionRoutine;
    bool _overhaulReactionActive;
    readonly HashSet<object> _overhaulHoldOwners = new HashSet<object>();
    readonly HashSet<string> _overhaulRemovedPatterns = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    bool _overhaulAimDisabled;
    bool _overhaulSnapCached;
    bool _overhaulSnapOriginal;

    public bool IsOverhaulReactionActive => _overhaulReactionActive && _overhaulReactionRoutine != null && _parryStunRoutine == _overhaulReactionRoutine;
    public BossOverhaulReaction ActiveOverhaulReaction => IsOverhaulReactionActive ? _activeOverhaulReaction : BossOverhaulReaction.None;
    BossOverhaulReaction _activeOverhaulReaction;
    public bool IsOverhaulHeld => _overhaulHoldOwners.Count > 0;
    public bool IsDeadForOverhaul => _isDead;
    public bool IsUltimateVictimForOverhaul => _isUltimateVictim;
    public bool IsAttackStateActive => currentState == BossState.Attack;
    public bool IsInRecoveryForOverhaul => IsCombatRecoveryActive;
    public Rigidbody OverhaulBody => rb;

    /// <summary>마지막 특수 큐 색(자홍 = 가드·패링 불가)과 남은 판정 시각. 블링크 반격 판정용.</summary>
    public BossAttackTelegraphType LastSpecialCueType { get; private set; }
    public float LastSpecialCueHitAt { get; private set; } = float.NegativeInfinity;

    void NoteOverhaulCue(BossAttackTelegraphType type, float secondsToHit)
    {
        LastSpecialCueType = type;
        LastSpecialCueHitAt = Time.time + Mathf.Max(0f, secondsToHit);
    }

    /// <summary>지금(±0.35 s) 판정되는 공격이 자홍(가드·패링 불가)인지.</summary>
    public bool IsMagentaAttackLandingNow(float tolerance = 0.35f)
    {
        return LastSpecialCueType == BossAttackTelegraphType.Unblockable && Mathf.Abs(Time.time - LastSpecialCueHitAt) <= tolerance;
    }

    // ======================================================================= 페이즈 / 패턴 설정

    /// <summary>개편 페이즈 문턱을 난이도와 무관하게 고정 (페이즈 2 = 66 %, 페이즈 3 = 30 %).</summary>
    public void ConfigureOverhaulPhases(float phaseTwo, float phaseThree, float phaseTwoFollowUpBonus)
    {
        phaseTwoThresholdNormalized = Mathf.Clamp(phaseTwo, 0.1f, 0.95f);
        phaseThreeThresholdNormalized = Mathf.Clamp(phaseThree, 0.05f, phaseTwoThresholdNormalized - 0.05f);
        phaseTwoFollowUpChanceBonus = Mathf.Clamp(phaseTwoFollowUpBonus, 0f, 0.5f);
        if (difficultyProfiles != null)
        {
            for (int i = 0; i < difficultyProfiles.Length; i++)
            {
                BossDifficultyProfile p = difficultyProfiles[i];
                p.phaseTransitionHpThreshold = 0f; // 0 = 위 기본 문턱 사용
                difficultyProfiles[i] = p;
            }
        }
        RefreshPhaseState(true);
    }

    public float OverhaulHpNormalized => GetBossHpNormalized();

    /// <summary>파괴된 부위(팔 칼날)가 쓰던 콤보를 선택에서 영구 제거.</summary>
    public void RemovePatternForOverhaul(string patternName)
    {
        if (!string.IsNullOrWhiteSpace(patternName))
            _overhaulRemovedPatterns.Add(patternName.Trim());
    }

    public bool IsPatternRemovedForOverhaul(string patternName) => !string.IsNullOrEmpty(patternName) && _overhaulRemovedPatterns.Contains(patternName);

    float ResolveOverhaulPatternWeight(AttackPattern pattern)
    {
        if (pattern == null || _overhaulRemovedPatterns.Count == 0)
            return 1f;
        return _overhaulRemovedPatterns.Contains(pattern.patternName ?? string.Empty) ? 0f : 1f;
    }

    /// <summary>실명: 공격 직전 플레이어 쪽으로 몸을 돌리지 않는다 (옆으로 비키면 빗나간다).</summary>
    public void SetOverhaulAimDisabled(bool disabled)
    {
        if (_overhaulAimDisabled == disabled)
            return;
        _overhaulAimDisabled = disabled;
        if (disabled)
        {
            _overhaulSnapOriginal = snapRotationToPlayerOnAttack;
            _overhaulSnapCached = true;
            snapRotationToPlayerOnAttack = false;
        }
        else if (_overhaulSnapCached)
        {
            snapRotationToPlayerOnAttack = _overhaulSnapOriginal;
            _overhaulSnapCached = false;
        }
    }

    // 페이즈 2+ 지연 공격: 비연계 공격 일부를 Delayed 로 바꾼다. 홀드 시간은 사전 텔레그래프 공지에 포함되므로 정직하다.
    void ApplyOverhaulDelayedTiming(AttackPattern pattern, bool isQueuedFollowUp, ref AttackTimingStyle timingStyle, ref float extraHoldDelay)
    {
        if (Overhaul == null || pattern == null || isQueuedFollowUp || _currentPhase < 2)
            return;
        if (timingStyle != AttackTimingStyle.Standard && timingStyle != AttackTimingStyle.Auto)
            return;
        if (Random.value >= Overhaul.DelayedAttackChance)
            return;
        timingStyle = AttackTimingStyle.Delayed;
        extraHoldDelay = Mathf.Max(extraHoldDelay, Overhaul.DelayedAttackHold);
        Overhaul.QaDelayedAttacks++;
    }

    // ======================================================================= 외부 반응

    public bool CanBeginOverhaulReaction(BossOverhaulReaction kind)
    {
        if (_isDead || _isUltimateVictim || _externalIntroPaused || IsOverhaulHeld)
            return false;
        if (breakController != null && breakController.IsInBreak)
            return false;
        if (kind != BossOverhaulReaction.Transition && kind != BossOverhaulReaction.Overload)
        {
            if (IsCombatRecoveryActive || ActiveSpecialPattern != BossSpecialPatternKind.None)
                return false;
            if (IsOverhaulReactionActive && _activeOverhaulReaction >= kind)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 반응 시작. push = 수평 밀림(월드), pushTime 동안 나눠 이동. 밀리는 길에 벽이 있으면 벽 박힘(+wallStun) 으로 바뀌고 onWallSplat 호출.
    /// </summary>
    public bool TryBeginOverhaulReaction(BossOverhaulReaction kind, float duration, Vector3 push, float pushTime, float wallStun = 1.2f,
        System.Action<Vector3> onWallSplat = null, System.Action onFinished = null)
    {
        if (!CanBeginOverhaulReaction(kind))
            return false;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }
        if (_parryStunRoutine != null)
        {
            StopCoroutine(_parryStunRoutine);
            _parryStunRoutine = null;
        }
        if (kind == BossOverhaulReaction.Transition || kind == BossOverhaulReaction.Overload)
            StopCombatRecovery();

        ReleaseParryStunAnimationFallback();
        AbortAttackExecution(clearPunishWindow: kind == BossOverhaulReaction.Transition);
        currentState = BossState.CombatIdle;
        UpdateMoveAnimation(0f);
        Locomotion.SetCombatStrafeAnimation(false);

        _activeOverhaulReaction = kind;
        _overhaulReactionActive = true;
        _overhaulReactionRoutine = StartCoroutine(Co_OverhaulReaction(kind, Mathf.Max(0.1f, duration), push, Mathf.Max(0.05f, pushTime), wallStun, onWallSplat, onFinished));
        _parryStunRoutine = _overhaulReactionRoutine;
        return true;
    }

    void CrossFadeOverhaulState(string state, float fade = 0.06f)
    {
        if (bossAnimator == null || string.IsNullOrEmpty(state))
            return;
        int hash = Animator.StringToHash(state);
        if (bossAnimator.HasState(0, hash))
            bossAnimator.CrossFadeInFixedTime(hash, fade, 0, 0f);
    }

    public bool HasOverhaulState(string state) => HasAnimatorState(state);

    IEnumerator Co_OverhaulReaction(BossOverhaulReaction kind, float duration, Vector3 push, float pushTime, float wallStun,
        System.Action<Vector3> onWallSplat, System.Action onFinished)
    {
        Vector3 pushFlat = new Vector3(push.x, 0f, push.z);
        float pushDistance = pushFlat.magnitude;
        Vector3 pushDir = pushDistance > 0.001f ? pushFlat / pushDistance : Vector3.zero;

        // 넉백/다운은 밀리는 반대 방향(때린 쪽)을 본다
        if (pushDistance > 0.05f && (kind == BossOverhaulReaction.Knockback || kind == BossOverhaulReaction.Down))
            transform.rotation = Quaternion.LookRotation(-pushDir, Vector3.up);

        switch (kind)
        {
            case BossOverhaulReaction.Flinch: CrossFadeOverhaulState(OverhaulStateFlinch, 0.04f); break;
            case BossOverhaulReaction.Stagger: CrossFadeOverhaulState(OverhaulStateStagger, 0.05f); break;
            case BossOverhaulReaction.Knockback: CrossFadeOverhaulState(OverhaulStateKnockback, 0.05f); break;
            case BossOverhaulReaction.Down:
            case BossOverhaulReaction.Overload: CrossFadeOverhaulState(OverhaulStateDownStart, 0.06f); break;
            case BossOverhaulReaction.Transition: CrossFadeOverhaulState(OverhaulStateRoar, 0.12f); break;
        }

        float elapsed = 0f;
        float pushed = 0f;
        bool splatted = false;
        bool downLooping = false;
        bool gettingUp = false;
        const float getUpTime = 0.9f;
        float total = duration;

        while (!_isDead && elapsed < total)
        {
            float dt = Time.deltaTime;
            if (!splatted && pushDistance > 0.01f && pushed < pushDistance && elapsed < pushTime + 0.1f)
            {
                float step = Mathf.Min(pushDistance - pushed, pushDistance * dt / pushTime);
                Vector3 origin = rb != null ? rb.position : transform.position;
                float clearance = Locomotion.MeasureMovementClearance(origin, pushDir, step + 0.45f);
                if (clearance < step + 0.4f && kind >= BossOverhaulReaction.Knockback)
                {
                    splatted = true;
                    _activeOverhaulReaction = BossOverhaulReaction.WallSplat;
                    total = Mathf.Max(total, elapsed + wallStun + 0.6f);
                    CrossFadeOverhaulState(OverhaulStateWallSplat, 0.04f);
                    onWallSplat?.Invoke(origin + pushDir * Mathf.Max(0f, clearance) + Vector3.up * 1.3f);
                }
                else
                {
                    Locomotion.ApplyPressureMovementDelta(pushDir * step);
                    pushed += step;
                }
            }

            if ((kind == BossOverhaulReaction.Down || kind == BossOverhaulReaction.Overload) && !splatted)
            {
                if (!downLooping && elapsed >= 0.55f)
                {
                    downLooping = true;
                    CrossFadeOverhaulState(OverhaulStateDownLoop, 0.12f);
                }
                if (!gettingUp && elapsed >= total - getUpTime)
                {
                    gettingUp = true;
                    CrossFadeOverhaulState(OverhaulStateGetUp, 0.15f);
                }
            }
            else if (splatted && !gettingUp && elapsed >= total - 0.6f)
            {
                gettingUp = true;
                CrossFadeOverhaulState(OverhaulStateGetUp, 0.12f);
            }

            elapsed += dt;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        _overhaulReactionActive = false;
        _parryStunRoutine = null;
        _overhaulReactionRoutine = null;
        BossOverhaulReaction finished = _activeOverhaulReaction;
        _activeOverhaulReaction = BossOverhaulReaction.None;
        onFinished?.Invoke();

        if (_isDead || _isUltimateVictim || IsOverhaulHeld)
            yield break;

        if (breakController != null && breakController.IsInBreak)
        {
            SetState(BossState.Break, true);
            yield break;
        }

        // 반응 뒤 짧은 빈틈 (플레이어 이득) → 기존 전투 복구로 위치·무적 정리
        float punish = finished == BossOverhaulReaction.Flinch ? 0f : finished == BossOverhaulReaction.Stagger ? 0.45f : 0.7f;
        if (punish > 0f && finished != BossOverhaulReaction.Transition)
            StageAttackRecovery("IMPACT", Mathf.Max(combatIdleTime, punish), punish, 1.15f);
        BlockAttackSelectionFor(finished == BossOverhaulReaction.Flinch ? 0.2f : 0.6f);

        BossRecoveryReason reason = finished == BossOverhaulReaction.Down || finished == BossOverhaulReaction.Overload
            ? BossRecoveryReason.Down
            : finished == BossOverhaulReaction.Transition
                ? BossRecoveryReason.PhaseTransition
                : finished == BossOverhaulReaction.Flinch
                    ? BossRecoveryReason.Stagger
                    : BossRecoveryReason.Knockback;
        if (finished == BossOverhaulReaction.Flinch)
            SetState(BossState.CombatIdle, true);
        else
            TryStartCombatRecovery(reason, BossState.CombatIdle, clearPunishWindow: false);
    }

    // ======================================================================= 외부 정지 (처형)

    public void BeginOverhaulHold(object owner)
    {
        if (owner == null || !_overhaulHoldOwners.Add(owner) || _overhaulHoldOwners.Count > 1)
            return;

        StopCombatRecovery();
        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }
        if (_parryStunRoutine != null)
        {
            StopCoroutine(_parryStunRoutine);
            _parryStunRoutine = null;
        }
        _overhaulReactionActive = false;
        _overhaulReactionRoutine = null;
        ReleaseParryStunAnimationFallback();
        AbortAttackExecution(clearPunishWindow: true);
        StopChaseDashGapCloser();
        UpdateMoveAnimation(0f);
        Locomotion.SetCombatStrafeAnimation(false);
        if (!_isDead && currentState != BossState.Break)
            currentState = BossState.CombatIdle;
    }

    public void EndOverhaulHold(object owner)
    {
        if (owner == null || !_overhaulHoldOwners.Remove(owner) || _overhaulHoldOwners.Count > 0)
            return;
        if (_isDead || _isUltimateVictim)
            return;
        if (breakController != null && breakController.IsInBreak)
        {
            SetState(BossState.Break, true);
            return;
        }
        TryStartCombatRecovery(BossRecoveryReason.CutsceneAttack, BossState.CombatIdle);
    }

    public void PlayOverhaulState(string state, float fade = 0.08f) => CrossFadeOverhaulState(state, fade);

    public void ThrottleAttacksForOverhaul(float seconds) => BlockAttackSelectionFor(seconds);

    // 반복 18: 단계별(봉쇄 등) 보스 공격력 배율 — 씬 규칙의 ScaleOutgoingDamage 와 곱으로 적용된다.
    float _overhaulDamageScale = 1f;
    public float OverhaulDamageScale => _overhaulDamageScale;
    public void SetOverhaulDamageScale(float scale) => _overhaulDamageScale = Mathf.Clamp(scale, 0.1f, 2f);

    /// <summary>블링크 반격: 빈틈(피해 배수) + 공격 선택 차단.</summary>
    public void OpenOverhaulPunishWindow(string source, float duration, float damageMultiplier, float blockAttacksFor)
    {
        if (_isDead)
            return;
        StageAttackRecovery(source, Mathf.Max(_pendingCombatIdleDuration, duration), duration, damageMultiplier);
        BlockAttackSelectionFor(blockAttacksFor);
    }

    // ======================================================================= 새 특수 패턴

    bool _overhaulDesperationRequested;
    float _overhaulVentReadyAt;
    float _overhaulGrabReadyAt;
    public int QaGrabs { get; private set; }
    public int QaVents { get; private set; }
    public int QaDesperations { get; private set; }

    /// <summary>TypeDOverhaul: 임계 폭주(15 %)를 다음 결정에서 무조건 쓴다.</summary>
    public void RequestOverhaulDesperation()
    {
        _overhaulDesperationRequested = true;
    }

    /// <summary>QA: 다음 결정에서 이 개편 패턴을 쓴다.</summary>
    public void QaRequestOverhaulSpecial(BossSpecialPatternKind kind)
    {
        if (kind == BossSpecialPatternKind.Desperation)
            _overhaulDesperationRequested = true;
        else if (kind == BossSpecialPatternKind.HeatVent)
            _overhaulVentReadyAt = float.NegativeInfinity;
        else if (kind == BossSpecialPatternKind.MagentaGrab)
            _overhaulGrabReadyAt = float.NegativeInfinity;
        _qaOverhaulForced = kind;
    }

    BossSpecialPatternKind _qaOverhaulForced;

    bool IsOverhaulSpecialUrgent => Overhaul != null && (_overhaulDesperationRequested || _qaOverhaulForced != BossSpecialPatternKind.None || Overhaul.IsVentDue(_overhaulVentReadyAt));

    bool TryChooseOverhaulSpecial(float distance, out BossSpecialPatternKind kind)
    {
        kind = BossSpecialPatternKind.None;
        if (Overhaul == null || !Overhaul.AllowsBossSpecials)
            return false;
        if (_isDead || _isUltimateVictim || IsCombatRecoveryActive || _externalIntroPaused || IsOverhaulHeld || playerTarget == null || bossAnimator == null)
            return false;
        if (breakController != null && breakController.IsInBreak)
            return false;

        if (_overhaulDesperationRequested)
        {
            _overhaulDesperationRequested = false;
            _qaOverhaulForced = BossSpecialPatternKind.None;
            kind = BossSpecialPatternKind.Desperation;
            return true;
        }

        if (_qaOverhaulForced != BossSpecialPatternKind.None)
        {
            kind = _qaOverhaulForced;
            _qaOverhaulForced = BossSpecialPatternKind.None;
            return true;
        }

        if (Overhaul.IsVentDue(_overhaulVentReadyAt) && distance <= Overhaul.VentMaxStartDistance)
        {
            kind = BossSpecialPatternKind.HeatVent;
            return true;
        }

        if (Overhaul.AllowsGrab && Time.time >= _overhaulGrabReadyAt && distance >= 1.2f && distance <= Overhaul.GrabMaxStartDistance
            && Time.time - _lastSpecialAt >= 4f && Random.value < Overhaul.GrabChance)
        {
            kind = BossSpecialPatternKind.MagentaGrab;
            return true;
        }

        return false;
    }

    AttackPattern _grabStub, _ventStub, _despStub;

    AttackPattern ResolveOverhaulStub(BossSpecialPatternKind kind)
    {
        switch (kind)
        {
            case BossSpecialPatternKind.MagentaGrab:
                return _grabStub ??= new AttackPattern { patternName = "MagentaGrab", animTriggerName = OverhaulStateGrab, isUnblockable = true, canGuard = false, isParryable = false, cooldown = 9f };
            case BossSpecialPatternKind.HeatVent:
                return _ventStub ??= new AttackPattern { patternName = "HeatVent", animTriggerName = OverhaulStateVent, isParryable = false, canGuard = false, cooldown = 13f };
            default:
                return _despStub ??= new AttackPattern { patternName = "Desperation", animTriggerName = OverhaulStateDespCharge, isUnblockable = true, canGuard = false, isParryable = false, cooldown = 999f };
        }
    }

    IEnumerator ResolveOverhaulSpecialBody(BossSpecialPatternKind kind)
    {
        switch (kind)
        {
            case BossSpecialPatternKind.MagentaGrab: return Co_MagentaGrab();
            case BossSpecialPatternKind.HeatVent: return Co_HeatVent();
            case BossSpecialPatternKind.Desperation: return Co_Desperation();
            default: return null;
        }
    }

    // 개편 특수 패턴은 진행 중 반응/정지에 끊기지 않는다 (슈퍼아머). 사망·처형 정지·궁극기·브레이크만 끊는다.
    bool CanContinueOverhaulSpecial()
    {
        return !_isDead && !_isUltimateVictim && !_externalIntroPaused && !IsOverhaulHeld && currentState == BossState.Attack &&
               (breakController == null || !breakController.IsInBreak) && playerTarget != null;
    }

    // 정직한 판정: 공지한 시각에 가장 가까운 프레임에서 판정 (반 프레임 이내 오차). 프레임이 길면 한 프레임 늦는 대신 앞당긴다.
    static float OverhaulImpactSnap => Mathf.Min(0.02f, Time.smoothDeltaTime * 0.5f);

    void EmitOverhaulCue(BossAttackTelegraphType type, float secondsToHit, string label, int ledgerToken)
    {
        EmitSpecialCue(type, secondsToHit, label);
        OverhaulCueLedger.Announce(ledgerToken, secondsToHit);
    }

    // ---- 자홍 잡기: 0.95 s 리드(0.40 s 재공지). 판정 순간 보스 앞 2.4 m 통로 안이면 잡힘 (퍼펙트 회피 → 블링크)
    IEnumerator Co_MagentaGrab()
    {
        QaGrabs++;
        _overhaulGrabReadyAt = Time.time + Overhaul.GrabCooldown;
        FaceToPlayerInstant();
        float lead = Overhaul.GrabLead * ResolveSpecialTelegraphMultiplier();
        float lungeTime = 0.22f;
        float speed = Mathf.Clamp(1.15f / Mathf.Max(0.3f, lead + lungeTime), 0.6f, 1.6f);
        PlaySpecialClip(OverhaulStateGrab, speed);
        int token = OverhaulCueLedger.Begin("MagentaGrab");
        EmitOverhaulCue(BossAttackTelegraphType.Unblockable, lead, "GRAB", token);
        Overhaul.NotifyGrabWindup(lead);

        bool late = false;
        float t = 0f;
        Vector3 lungeDir = transform.forward;
        float lungeDistance = 0f;
        while (lead - t > OverhaulImpactSnap)
        {
            if (!CanContinueOverhaulSpecial())
            {
                OverhaulCueLedger.Cancel(token);
                yield break;
            }

            float remain = lead - t;
            if (remain > 0.45f && playerTarget != null)
            {
                // 큐 고정 전까지만 플레이어 쪽으로 천천히 조준 (판정 시각은 바뀌지 않는다)
                Vector3 to = playerTarget.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to.normalized), 220f * Time.deltaTime);
            }
            else if (!late)
            {
                late = true;
                lungeDir = transform.forward;
                Vector3 toPlayer = playerTarget != null ? playerTarget.position - transform.position : lungeDir * 3f;
                toPlayer.y = 0f;
                lungeDistance = Mathf.Clamp(Vector3.Dot(toPlayer, lungeDir) - 0.9f, 0f, 3.2f);
                if (useCollisionAwareMovement && lungeDistance > 0f)
                    lungeDistance = Mathf.Min(lungeDistance, Locomotion.MeasureMovementClearance(transform.position, lungeDir, lungeDistance));
                EmitOverhaulCue(BossAttackTelegraphType.Unblockable, remain, "GRAB", OverhaulCueLedger.Begin("MagentaGrab"));
            }

            // 판정 직전 0.22 s 돌진
            if (remain <= lungeTime && lungeDistance > 0f)
                Locomotion.ApplyPressureMovementDelta(lungeDir * (lungeDistance * Mathf.Min(1f, Time.deltaTime / lungeTime)));

            t += Time.deltaTime;
            yield return null;
        }

        OverhaulCueLedger.ImpactAll("MagentaGrab");
        bool caught = false;
        if (playerTarget != null)
        {
            Vector3 local = transform.InverseTransformPoint(playerTarget.position);
            caught = local.z > -0.4f && local.z < 2.4f && Mathf.Abs(local.x) < 1.15f && Mathf.Abs(local.y) < 2.2f;
        }
        if (caught)
            ApplyOverhaulHit(playerTarget.position + Vector3.up * 1.0f, Overhaul.GrabDamage, unblockable: true, canPerfectDodge: true, heavyLaunch: true, source: "GRAB");
        Overhaul.NotifyGrabResolved(caught);

        StageAttackRecovery("GRAB", Overhaul.GrabRecovery, Overhaul.GrabRecovery, 1.2f);
        float rest = Overhaul.GrabRecovery;
        while (rest > 0f && CanContinueOverhaulSpecial())
        {
            rest -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }
    }

    // ---- 배기 AoE: 빨강(회피 전용) 원형 4.6 m, 1.30 s 리드 → 3.2 s 정체(빈틈 ×1.35)
    IEnumerator Co_HeatVent()
    {
        QaVents++;
        _overhaulVentReadyAt = Time.time + Overhaul.VentInterval;
        float lead = Overhaul.VentLead * ResolveSpecialTelegraphMultiplier();
        PlaySpecialClip(OverhaulStateVent, 1f);
        Vector3 center = transform.position;
        float radius = Overhaul.VentRadius;
        if (groundTelegraph != null)
            groundTelegraph.ShowCircle(center, radius, AttackTelegraphType.Dodge, lead);
        int token = OverhaulCueLedger.Begin("HeatVent");
        EmitOverhaulCue(BossAttackTelegraphType.DodgeOnly, lead, "VENT", token);
        Overhaul.NotifyVentCharge(lead);

        bool late = false;
        float t = 0f;
        while (lead - t > OverhaulImpactSnap)
        {
            if (!CanContinueOverhaulSpecial())
            {
                OverhaulCueLedger.Cancel(token);
                Overhaul.NotifyVentAborted();
                yield break;
            }
            if (!late && lead - t <= 0.4f)
            {
                late = true;
                EmitOverhaulCue(BossAttackTelegraphType.DodgeOnly, lead - t, "VENT", OverhaulCueLedger.Begin("HeatVent"));
            }
            t += Time.deltaTime;
            yield return null;
        }

        OverhaulCueLedger.ImpactAll("HeatVent");
        ApplyOverhaulArea(center, radius, Overhaul.VentDamage, unblockable: true, heavyLaunch: true, source: "VENT");
        SpecialImpactFeedback(center, radius * 1.05f, AttackTelegraphType.Dodge, 0.42f);
        Overhaul.NotifyVentBlast(center, radius);

        // 배기 정체 = 큰 빈틈 (코어 노출, 부위 파괴 명중 인정)
        float stall = Overhaul.VentPunishWindow;
        StageAttackRecovery("VENT", stall, stall, Overhaul.VentPunishMultiplier);
        Overhaul.NotifyVentStall(stall);
        float rest = stall;
        while (rest > 0f && CanContinueOverhaulSpecial())
        {
            rest -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }
        Overhaul.NotifyVentStallEnded();
    }

    // ---- 임계 폭주: 중앙 이동 → 엄폐 블록 상승 → 5.0 s 충전(1.0 / 0.4 s 재공지) → 아레나 전체 폭발
    IEnumerator Co_Desperation()
    {
        QaDesperations++;
        Vector3 center = Overhaul.ArenaCenter;
        center.y = transform.position.y;
        PlaySpecialClip(OverhaulStateRoar, 1f);
        Overhaul.NotifyDesperationBegin();

        // 0.9 s 동안 중앙으로 미끄러지듯 이동 (공지 전 — 판정 시각과 무관)
        float move = 0f;
        Vector3 start = transform.position;
        while (move < 0.9f)
        {
            if (!CanContinueOverhaulSpecial())
            {
                Overhaul.NotifyDesperationResolved(false, false);
                yield break;
            }
            float k = Mathf.SmoothStep(0f, 1f, move / 0.9f);
            Vector3 goal = Vector3.Lerp(start, center, k);
            Vector3 delta = goal - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.000001f)
                Locomotion.ApplyMovementDelta(delta);
            move += Time.deltaTime;
            yield return null;
        }

        if (playerTarget != null)
            FaceToPlayerInstant();
        PlaySpecialClip(OverhaulStateDespCharge, 1f);
        float lead = Overhaul.DesperationLead;
        float radius = Overhaul.ArenaRadius;
        if (groundTelegraph != null)
            groundTelegraph.ShowCircle(transform.position, radius, AttackTelegraphType.Danger, lead);
        int token = OverhaulCueLedger.Begin("Desperation");
        EmitOverhaulCue(BossAttackTelegraphType.Unblockable, lead, "CRITICAL", token);
        Overhaul.NotifyDesperationCharge(lead);

        bool cue1 = false, cue2 = false;
        float t = 0f;
        while (lead - t > OverhaulImpactSnap)
        {
            if (!CanContinueOverhaulSpecial())
            {
                OverhaulCueLedger.Cancel(token);
                Overhaul.NotifyDesperationResolved(false, false);
                yield break;
            }
            float remain = lead - t;
            if (!cue1 && remain <= 1.0f)
            {
                cue1 = true;
                EmitOverhaulCue(BossAttackTelegraphType.Unblockable, remain, "CRITICAL", OverhaulCueLedger.Begin("Desperation"));
            }
            if (!cue2 && remain <= 0.4f)
            {
                cue2 = true;
                EmitOverhaulCue(BossAttackTelegraphType.Unblockable, remain, "CRITICAL", OverhaulCueLedger.Begin("Desperation"));
            }
            t += Time.deltaTime;
            yield return null;
        }

        OverhaulCueLedger.ImpactAll("Desperation");
        PlaySpecialClip(OverhaulStateDespRelease, 1f);
        bool covered = Overhaul.IsPlayerCoveredFrom(transform.position + Vector3.up * 1.4f);
        bool hit = false;
        if (!covered && playerTarget != null)
            hit = ApplyOverhaulHit(playerTarget.position + Vector3.up * 1.0f, Overhaul.DesperationDamage, unblockable: true, canPerfectDodge: true, heavyLaunch: true, source: "CRITICAL");
        SpecialImpactFeedback(transform.position, radius, AttackTelegraphType.Danger, 0.6f);
        Overhaul.NotifyDesperationResolved(true, covered || !hit);
    }

    // 플레이어 한 명에게 직접 판정 (퍼펙트 회피·회피 무적은 PlayerDamageReceiver 가 판정). true = 실제로 피해가 들어감.
    bool ApplyOverhaulHit(Vector3 hitPoint, int baseDamage, bool unblockable, bool canPerfectDodge, bool heavyLaunch, string source)
    {
        if (playerTarget == null)
            return false;
        IDamageReceiver receiver = playerTarget.GetComponentInParent<IDamageReceiver>() ?? playerTarget.GetComponentInChildren<IDamageReceiver>();
        if (receiver == null)
            return false;
        PlayerHealth health = playerTarget.GetComponentInParent<PlayerHealth>() ?? playerTarget.GetComponentInChildren<PlayerHealth>();
        int before = health != null ? health.CurrentHP : 0;
        Vector3 dir = playerTarget.position - transform.position;
        dir.y = 0f;
        float damage = Mathf.Max(0f, baseDamage * Mathf.Max(0f, outgoingDamageMultiplier * _overhaulDamageScale) * Tuning.ResolveDifficultyDamageMultiplier());
        if (heavyLaunch)
            PlayerOverhaulReactions.MarkNextHitLaunch(source);
        receiver.ReceiveHit(new HitPayload
        {
            damage = damage,
            hitType = HitType.Heavy,
            hitPoint = hitPoint,
            hitDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward,
            attacker = transform,
            canParry = false,
            canPerfectDodge = canPerfectDodge,
            canGuard = !unblockable,
            causesGuardBreak = false,
            unblockable = unblockable
        });
        PlayerOverhaulReactions.ClearLaunchMark();
        return health != null && health.CurrentHP < before;
    }

    void ApplyOverhaulArea(Vector3 center, float radius, int baseDamage, bool unblockable, bool heavyLaunch, string source)
    {
        if (playerTarget == null)
            return;
        Vector3 d = playerTarget.position - center;
        float vertical = Mathf.Abs(d.y);
        d.y = 0f;
        if (d.magnitude > radius || vertical > 2.6f)
            return;
        ApplyOverhaulHit(playerTarget.position + Vector3.up * 0.9f, baseDamage, unblockable, true, heavyLaunch, source);
    }
}
