// [Type-D 특수 패턴 + 기획 반응형 조건]  (보스 기획서 v1.2 패턴 9종 중 기존 a~f/SwordWave 에 없던 것)
//  1) 충격파 방출 (Shockwave): 가드·패링 불가. 보스 중심 원형 텔레그래프(자홍 = 가드/패링 불가)가 차오르고
//     채움이 테두리에 닿는 순간 착지 충격파. 반경 밖으로 회피하거나 회피 무적으로 넘긴다.
//  2) 그림자 추적 도약 내려찍기 (Shadow Leap Slam): 회피 전용(빨강). 보스가 도약하면 그림자가 플레이어를 따라오다
//     착지 0.45초 전에 고정되고, 그 자리에 내려찍는다.
//  반응형 (기획: 반복 암기 최소화, 리액션 중심) — BossPlayerObservationTracker 신호에 추가로
//   - 플레이어 회복 사용 → 도약 내려찍기 가중치↑ (회복 직후 짧은 창, 쿨다운)
//   - 3초 안 회피 2회 → 사선 베기(diagonalSlashPatternNames) 가중치↑
//   - 플레이어 후퇴 지속 → 대쉬 찌르기(dashThrustPatternNames) 가중치↑
//  모두 기본 off. Level_Facility 의 FacilityCombatRules 가 켠다 (MainScene 단독 실행 동작은 그대로).
using System.Collections;
using UnityEngine;

public enum BossSpecialPatternKind
{
    None = 0,
    Shockwave = 1,
    ShadowLeapSlam = 2
}

public partial class BossController
{
    [Header("Special Patterns (충격파 / 그림자 도약)")]
    [SerializeField] private bool enableSpecialPatterns = false;
    [SerializeField] private string shockwaveStateName = "Attack_Shockwave";
    [SerializeField] private string leapStateName = "Attack_Leap";
    [SerializeField] private string specialSpeedParam = "SpecialSpeed";
    [Tooltip("특수 패턴 사이 최소 간격(초)")]
    [SerializeField, Min(0f)] private float specialGlobalCooldown = 7f;

    [Header("Shockwave")]
    [SerializeField, Min(0f)] private float shockwaveCooldown = 15f;
    [SerializeField, Min(0.5f)] private float shockwaveRadius = 5.2f;
    [SerializeField, Min(0f)] private float shockwaveMaxStartDistance = 4.6f;
    [SerializeField, Min(0f)] private int shockwaveDamage = 24;
    [SerializeField, Range(0f, 1f)] private float shockwaveBaseChance = 0.16f;
    [Tooltip("이 HP 비율 이하부터 사용 (1 = 처음부터)")]
    [SerializeField, Range(0f, 1f)] private float shockwaveUnlockHp = 0.8f;
    [Tooltip("클립 재생 속도 (JumpAttack03: 착지 1.28s / 속도)")]
    [SerializeField, Min(0.3f)] private float shockwaveClipSpeed = 0.9f;
    [SerializeField, Min(0.1f)] private float shockwaveClipImpactTime = 1.28f;
    [SerializeField, Min(0f)] private float shockwaveRecovery = 1.0f;

    [Header("Shadow Leap Slam")]
    [SerializeField, Min(0f)] private float leapCooldown = 12f;
    [SerializeField, Min(0.5f)] private float leapRadius = 2.4f;
    [SerializeField, Min(0f)] private float leapMinStartDistance = 3.8f;
    [SerializeField, Min(0f)] private float leapMaxStartDistance = 15f;
    [SerializeField, Min(1f)] private float leapMaxTravel = 14f;
    [SerializeField, Min(0f)] private int leapDamage = 22;
    [SerializeField, Range(0f, 1f)] private float leapBaseChance = 0.14f;
    [SerializeField, Min(0.3f)] private float leapClipSpeed = 0.75f;
    [SerializeField, Min(0.1f)] private float leapClipTakeoffTime = 0.15f;
    [SerializeField, Min(0.1f)] private float leapClipImpactTime = 1.08f;
    [Tooltip("착지 몇 초 전에 그림자를 고정할지 (정직한 큐: 고정 이후엔 낙하 지점이 바뀌지 않음)")]
    [SerializeField, Min(0.1f)] private float leapLockLeadTime = 0.45f;
    [SerializeField, Min(0f)] private float leapRecovery = 1.1f;
    [Tooltip("반복 10: 플레이어가 이 거리 밖에 이 시간 이상 머물면 도약 내려찍기를 확정으로 쓴다 (원거리 견제)")]
    [SerializeField, Min(0f)] private float leapFarSustainDistance = 6f;
    [SerializeField, Min(0f)] private float leapFarSustainTime = 2f;

    [Header("Reactive Conditions (기획 반응형)")]
    [SerializeField] private bool enablePlanReactiveBias = false;
    [Tooltip("회복 시작 후 이 시간 안에 공격을 고르면 도약 내려찍기 우선")]
    [SerializeField, Min(0f)] private float healReactionWindow = 3f;
    [SerializeField, Min(0f)] private float healReactionCooldown = 14f;
    [SerializeField, Min(1f)] private float healLeapWeightMultiplier = 6f;
    [SerializeField, Min(0f)] private float dodgeSpamWindow = 3f;
    [SerializeField, Min(2)] private int dodgeSpamCount = 2;
    [SerializeField, Min(1f)] private float dodgeSpamDiagonalMultiplier = 2.6f;
    [SerializeField, Min(0f)] private float dodgeSpamCooldown = 8f;
    [SerializeField] private string diagonalSlashPatternNames = "e";
    [SerializeField, Min(0f)] private float backOffSustainTime = 0.8f;
    [SerializeField, Min(0f)] private float backOffMinDistance = 4.2f;
    [SerializeField, Min(1f)] private float backOffDashMultiplier = 2.6f;
    [SerializeField, Min(0f)] private float backOffCooldown = 8f;
    [SerializeField] private string dashThrustPatternNames = "d";

    public bool EnableSpecialPatterns { get => enableSpecialPatterns; set => enableSpecialPatterns = value; }
    public bool EnablePlanReactiveBias { get => enablePlanReactiveBias; set { enablePlanReactiveBias = value; RefreshReactiveSubscriptions(); } }
    public int QaShockwaves { get; private set; }
    public int QaLeaps { get; private set; }
    public int QaHealReactions { get; private set; }
    public int QaDodgeSpamReactions { get; private set; }
    public int QaBackOffReactions { get; private set; }
    /// <summary>QA: '멀리 2초 이상' 규칙으로 나간 도약 수.</summary>
    public int QaFarLeaps { get; private set; }
    public BossSpecialPatternKind ActiveSpecialPattern { get; private set; }

    float _lastSpecialAt = float.NegativeInfinity;
    float _shockwaveReadyAt;
    float _leapReadyAt;
    float _lastPlayerHealAt = float.NegativeInfinity;
    float _healReactionReadyAt;
    readonly float[] _recentDodgeTimes = new float[8];
    int _recentDodgeHead;
    float _dodgeSpamActiveUntil = float.NegativeInfinity;
    float _dodgeSpamReadyAt;
    float _backOffSince = float.NegativeInfinity;
    float _backOffReadyAt;
    float _farSince = float.NegativeInfinity;

    // 원거리 유지 감시 (특수 패턴 탐색 때마다 갱신)
    void TickFarObservation(float distance)
    {
        if (distance >= leapFarSustainDistance && !float.IsInfinity(distance))
        {
            if (float.IsNegativeInfinity(_farSince))
                _farSince = Time.time;
        }
        else
        {
            _farSince = float.NegativeInfinity;
        }
    }

    bool IsFarSustainActive => !float.IsNegativeInfinity(_farSince) && Time.time - _farSince >= leapFarSustainTime;
    bool _reactiveSubscribed;
    AttackPattern _shockwavePatternStub;
    AttackPattern _leapPatternStub;
    int _specialSpeedHash;

    // ---------------- 반응형 신호 ----------------

    void RefreshReactiveSubscriptions()
    {
        bool want = enablePlanReactiveBias && isActiveAndEnabled;
        if (want == _reactiveSubscribed)
            return;
        _reactiveSubscribed = want;
        if (want)
        {
            PlayerConsumables.AnyHealStarted += HandlePlayerHealStarted;
            PlayerDodgeController.AnyDodgeStarted += HandlePlayerDodgeStarted;
        }
        else
        {
            PlayerConsumables.AnyHealStarted -= HandlePlayerHealStarted;
            PlayerDodgeController.AnyDodgeStarted -= HandlePlayerDodgeStarted;
        }
    }

    void HandlePlayerHealStarted(PlayerConsumables consumables)
    {
        _lastPlayerHealAt = Time.time;
    }

    void HandlePlayerDodgeStarted(PlayerDodgeController dodge)
    {
        float now = Time.time;
        _recentDodgeTimes[_recentDodgeHead] = now;
        _recentDodgeHead = (_recentDodgeHead + 1) % _recentDodgeTimes.Length;
        int count = 0;
        for (int i = 0; i < _recentDodgeTimes.Length; i++)
            if (_recentDodgeTimes[i] > 0f && now - _recentDodgeTimes[i] <= dodgeSpamWindow)
                count++;
        if (count >= dodgeSpamCount && now >= _dodgeSpamReadyAt)
            _dodgeSpamActiveUntil = now + dodgeSpamWindow;
    }

    void TickReactiveObservation()
    {
        if (!enablePlanReactiveBias)
            return;
        RefreshReactiveSubscriptions();
        BossPlayerCombatObservation o = ObservationTracker.Observation;
        bool backing = o.playerIsMovingAway && o.playerDistance >= backOffMinDistance * 0.75f && !float.IsInfinity(o.playerDistance);
        if (backing)
        {
            if (float.IsNegativeInfinity(_backOffSince))
                _backOffSince = Time.time;
        }
        else if (!o.playerIsMovingAway)
        {
            _backOffSince = float.NegativeInfinity;
        }
    }

    bool IsHealReactionActive => enablePlanReactiveBias && Time.time - _lastPlayerHealAt <= healReactionWindow && Time.time >= _healReactionReadyAt;
    bool IsDodgeSpamReactionActive => enablePlanReactiveBias && Time.time <= _dodgeSpamActiveUntil && Time.time >= _dodgeSpamReadyAt;
    bool IsBackOffReactionActive => enablePlanReactiveBias && !float.IsNegativeInfinity(_backOffSince) && Time.time - _backOffSince >= backOffSustainTime && Time.time >= _backOffReadyAt;

    static bool NameInList(string list, string name)
    {
        if (string.IsNullOrEmpty(list) || string.IsNullOrEmpty(name))
            return false;
        foreach (string token in list.Split(','))
            if (string.Equals(token.Trim(), name, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    float ResolveReactivePatternWeightMultiplier(AttackPattern pattern)
    {
        if (!enablePlanReactiveBias || pattern == null)
            return 1f;
        float m = 1f;
        if (IsDodgeSpamReactionActive && NameInList(diagonalSlashPatternNames, pattern.patternName))
            m *= dodgeSpamDiagonalMultiplier;
        if (IsBackOffReactionActive && NameInList(dashThrustPatternNames, pattern.patternName))
            m *= backOffDashMultiplier;
        return m;
    }

    void NotifyReactivePatternExecuted(AttackPattern pattern)
    {
        if (!enablePlanReactiveBias || pattern == null)
            return;
        if (IsDodgeSpamReactionActive && NameInList(diagonalSlashPatternNames, pattern.patternName))
        {
            QaDodgeSpamReactions++;
            _dodgeSpamReadyAt = Time.time + dodgeSpamCooldown;
            _dodgeSpamActiveUntil = float.NegativeInfinity;
        }
        if (IsBackOffReactionActive && NameInList(dashThrustPatternNames, pattern.patternName))
        {
            QaBackOffReactions++;
            _backOffReadyAt = Time.time + backOffCooldown;
            _backOffSince = float.NegativeInfinity;
        }
    }

    BossSpecialPatternKind _preChosenSpecial;
    float _nextSpecialProbeAt;
    float _preChosenAt;

    /// <summary>CombatIdle/Move 중에도 특수 패턴(회복 응징 도약, 원거리 도약, QA 요청)을 고르면 곧바로 Attack 으로 넘어간다.
    /// 확률 판정은 0.6초에 한 번만 (매 프레임 굴리지 않음). 결정은 Co_PerformAttack 의 TryChooseSpecialPattern 이 그대로 쓴다.</summary>
    bool ShouldInterruptIdleForReactiveSpecial(float distance, bool onlyUrgent = false)
    {
        if (!enableSpecialPatterns)
            return false;
        if (_preChosenSpecial != BossSpecialPatternKind.None)
            return true;
        TickReactiveObservation();
        TickFarObservation(distance);
        bool urgent = _qaRequestedSpecial != BossSpecialPatternKind.None || IsHealReactionActive || IsFarSustainActive;
        if (onlyUrgent && !urgent)
            return false;
        if (!urgent && (Time.time < _nextSpecialProbeAt || !CanRequestAttackSelection()))
            return false;
        _nextSpecialProbeAt = Time.time + 0.6f;
        if (TryChooseSpecialPattern(distance, out BossSpecialPatternKind kind, preChoose: true))
        {
            _preChosenSpecial = kind;
            _preChosenAt = Time.time;
            return true;
        }
        return false;
    }

    // ---------------- 선택 ----------------

    bool CanStartSpecialNow()
    {
        return !_isDead && !_isUltimateVictim && !IsCombatRecoveryActive && !_externalIntroPaused &&
               (breakController == null || !breakController.IsInBreak) &&
               Time.time - _lastSpecialAt >= specialGlobalCooldown &&
               bossAnimator != null && playerTarget != null;
    }

    bool CanStartShockwave(float distance)
    {
        return Time.time >= _shockwaveReadyAt && distance <= shockwaveMaxStartDistance &&
               GetBossHpNormalized() <= shockwaveUnlockHp + 0.0001f && HasAnimatorState(shockwaveStateName);
    }

    bool CanStartLeap(float distance, bool reactive)
    {
        float min = reactive ? 0f : leapMinStartDistance; // 회복 응징은 근접에서도 (그림자가 플레이어를 추적)
        return Time.time >= _leapReadyAt && distance >= min && distance <= leapMaxStartDistance && HasAnimatorState(leapStateName);
    }

    bool HasAnimatorState(string stateName)
    {
        return bossAnimator != null && !string.IsNullOrEmpty(stateName) && bossAnimator.HasState(0, Animator.StringToHash(stateName));
    }

    BossSpecialPatternKind _qaRequestedSpecial;

    /// <summary>QA/스크린샷: 다음 공격 결정에서 이 특수 패턴을 쓴다 (쿨다운 무시).</summary>
    public void QaRequestSpecial(BossSpecialPatternKind kind)
    {
        _qaRequestedSpecial = kind;
        _lastSpecialAt = float.NegativeInfinity;
    }

    bool TryChooseSpecialPattern(float distance, out BossSpecialPatternKind kind, bool preChoose = false)
    {
        kind = BossSpecialPatternKind.None;
        if (!enableSpecialPatterns)
            return false;
        if (!preChoose && _preChosenSpecial != BossSpecialPatternKind.None)
        {
            kind = _preChosenSpecial;
            _preChosenSpecial = BossSpecialPatternKind.None;
            return Time.time - _preChosenAt <= 1.5f && CanStartSpecialNow();
        }
        TickReactiveObservation();
        if (!CanStartSpecialNow())
            return false;

        if (_qaRequestedSpecial != BossSpecialPatternKind.None)
        {
            kind = _qaRequestedSpecial;
            _qaRequestedSpecial = BossSpecialPatternKind.None;
            return true;
        }

        if (IsHealReactionActive && CanStartLeap(distance, true))
        {
            QaHealReactions++;
            _healReactionReadyAt = Time.time + healReactionCooldown;
            kind = BossSpecialPatternKind.ShadowLeapSlam;
            return true;
        }

        // 반복 10: 플레이어가 6 m 밖에서 2초 이상 버티면 도약 (가까이 붙어 싸우는 상대에게는 회복 응징으로만 나온다)
        TickFarObservation(distance);
        if (IsFarSustainActive && CanStartLeap(distance, false))
        {
            QaFarLeaps++;
            _farSince = float.NegativeInfinity;
            kind = BossSpecialPatternKind.ShadowLeapSlam;
            return true;
        }

        float shock = CanStartShockwave(distance) ? shockwaveBaseChance : 0f;
        if (shock > 0f && ObservationTracker.IsCloseLingerActive())
            shock *= 1.6f;
        float leap = CanStartLeap(distance, false) ? leapBaseChance : 0f;
        if (leap > 0f && (ObservationTracker.IsFarPressureActive() || IsBackOffReactionActive))
            leap *= 1.5f;

        float total = shock + leap;
        if (total <= 0f)
            return false;
        float roll = Random.value;
        if (roll >= Mathf.Min(0.6f, total))
            return false;
        kind = Random.value * total < shock ? BossSpecialPatternKind.Shockwave : BossSpecialPatternKind.ShadowLeapSlam;
        return true;
    }

    // ---------------- 실행 ----------------

    AttackPattern ResolveSpecialStub(BossSpecialPatternKind kind)
    {
        if (kind == BossSpecialPatternKind.Shockwave)
            return _shockwavePatternStub ??= new AttackPattern { patternName = "Shockwave", animTriggerName = shockwaveStateName, damageAmount = shockwaveDamage, isUnblockable = true, canGuard = false, isParryable = false, cooldown = shockwaveCooldown };
        return _leapPatternStub ??= new AttackPattern { patternName = "ShadowLeap", animTriggerName = leapStateName, damageAmount = leapDamage, isParryable = false, canGuard = false, cooldown = leapCooldown };
    }

    bool CanContinueSpecial()
    {
        return !_isDead && !_isUltimateVictim && !_externalIntroPaused && currentState == BossState.Attack &&
               _parryStunRoutine == null && (breakController == null || !breakController.IsInBreak) &&
               (bossHealth == null || !bossHealth.IsStaggered) && playerTarget != null;
    }

    float ResolveSpecialTelegraphMultiplier()
    {
        float m = Tuning.ResolveDifficultyProfile().telegraphDurationMultiplier;
        return Mathf.Clamp(m > 0.01f ? m : 1f, 0.7f, 1.5f);
    }

    void PlaySpecialClip(string stateName, float speed)
    {
        if (_specialSpeedHash == 0)
            _specialSpeedHash = Animator.StringToHash(specialSpeedParam);
        foreach (AnimatorControllerParameter p in bossAnimator.parameters)
        {
            if (p.nameHash == _specialSpeedHash)
            {
                bossAnimator.SetFloat(_specialSpeedHash, speed);
                break;
            }
        }
        bossAnimator.CrossFadeInFixedTime(stateName, 0.08f, 0);
    }

    // 정직한 큐: 피격(착지)까지 남은 시간을 그대로 알린다. 색 언어 = 회피(빨강) / 가드·패링 불가(자홍).
    void EmitSpecialCue(BossAttackTelegraphType type, float secondsToHit, string label)
    {
        AttackTelegraphType legacy = type == BossAttackTelegraphType.Unblockable ? AttackTelegraphType.Danger : AttackTelegraphType.Dodge;
        BossAttackTimingCueFlags flags = BossAttackTimingCueFlags.WeaponFlash | BossAttackTimingCueFlags.BodyFlash | BossAttackTimingCueFlags.WarningSound;
        OnAttackTimingCue?.Invoke(type, secondsToHit, flags);
        BossTelegraphVfxPresenter presenter = ResolveTelegraphVfxPresenter();
        if (presenter != null)
            presenter.PlayCue(type, secondsToHit, flags);
        else if (patternVisuals != null)
            patternVisuals.StartVisualCue(legacy, secondsToHit);
        ApplyAttackTimingWarningSound(flags);
        // 손 텔레그래프 링도 타입 색(회피 빨강 / 가드·패링 불가 자홍)으로 — 금색(패링 가능)으로 오해하지 않게
        if (handPatternTelegraph == null)
            handPatternTelegraph = GetComponentInChildren<BossPatternHandTelegraph>(true);
        if (handPatternTelegraph != null && secondsToHit > 0.01f)
            handPatternTelegraph.Play(secondsToHit, presenter != null ? presenter.GetCueColor(type) : FallbackPerHitCueColor(type), 1f);
        OnAttackTelegraph?.Invoke(legacy, secondsToHit, label);
    }

    IEnumerator Co_RunSpecialPattern(BossSpecialPatternKind kind)
    {
        ActiveSpecialPattern = kind;
        _lastSpecialAt = Time.time;
        AttackPattern stub = ResolveSpecialStub(kind);
        _currentPattern = stub;
        _lastExecutedPattern = stub.patternName;
        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();

        IEnumerator body = kind == BossSpecialPatternKind.Shockwave ? Co_Shockwave() : Co_ShadowLeap();
        while (true)
        {
            bool more;
            try { more = body.MoveNext(); }
            catch (System.Exception e) { Debug.LogException(e, this); more = false; }
            if (!more)
                break;
            yield return body.Current;
        }

        ActiveSpecialPattern = BossSpecialPatternKind.None;
        if (groundTelegraph != null)
            groundTelegraph.HideCircle();
        if (!_isDead && currentState == BossState.Attack)
            EnterCombatIdleFromCurrentPattern();
    }

    IEnumerator Co_Shockwave()
    {
        QaShockwaves++;
        _shockwaveReadyAt = Time.time + shockwaveCooldown;
        FaceToPlayerInstant();
        float speed = shockwaveClipSpeed / ResolveSpecialTelegraphMultiplier();
        float impactIn = shockwaveClipImpactTime / Mathf.Max(0.1f, speed);
        PlaySpecialClip(shockwaveStateName, speed);
        Vector3 center = transform.position;
        if (groundTelegraph != null)
            groundTelegraph.ShowCircle(center, shockwaveRadius, AttackTelegraphType.Danger, impactIn);
        EmitSpecialCue(BossAttackTelegraphType.Unblockable, impactIn, "SHOCKWAVE");
        bool lateCueSent = false;

        float t = 0f;
        while (t < impactIn)
        {
            if (!CanContinueSpecial())
                yield break;
            if (!lateCueSent && impactIn - t <= 0.4f)
            {
                lateCueSent = true; // 기획: 0.4초 전 발광 (같은 착지 시점을 다시 알림)
                EmitSpecialCue(BossAttackTelegraphType.Unblockable, impactIn - t, "SHOCKWAVE");
            }
            t += Time.deltaTime;
            yield return null;
        }

        ApplySpecialAreaDamage(transform.position, shockwaveRadius, shockwaveDamage, unblockable: true);
        SpecialImpactFeedback(transform.position, shockwaveRadius * 1.08f, AttackTelegraphType.Danger, 0.55f);

        StageAttackRecovery("SHOCKWAVE", shockwaveRecovery, shockwaveRecovery, 1.2f);
        float rest = shockwaveRecovery;
        while (rest > 0f && CanContinueSpecial())
        {
            rest -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }
    }

    IEnumerator Co_ShadowLeap()
    {
        QaLeaps++;
        _leapReadyAt = Time.time + leapCooldown;
        FaceToPlayerInstant();
        float speed = leapClipSpeed / ResolveSpecialTelegraphMultiplier();
        float takeoff = leapClipTakeoffTime / speed;
        float impactIn = leapClipImpactTime / speed;
        float lockAt = Mathf.Max(takeoff, impactIn - leapLockLeadTime);
        PlaySpecialClip(leapStateName, speed);

        Transform target = playerTarget;
        Vector3 start = transform.position;
        Vector3 groundAnchor = new Vector3(target.position.x, start.y, target.position.z);
        if (groundTelegraph != null)
            groundTelegraph.ShowCircle(groundAnchor, leapRadius, AttackTelegraphType.Dodge, impactIn,
                () => target != null ? new Vector3(target.position.x, start.y, target.position.z) : transform.position, shadowStyle: true);
        EmitSpecialCue(BossAttackTelegraphType.DodgeOnly, impactIn, "LEAP");

        Vector3 landing = groundAnchor;
        bool locked = false;
        float t = 0f;
        while (t < impactIn)
        {
            if (!CanContinueSpecial())
                yield break;

            Vector3 shadow = groundTelegraph != null ? groundTelegraph.CircleCenter : (target != null ? target.position : landing);
            if (!locked && t >= lockAt)
            {
                locked = true;
                if (groundTelegraph != null)
                    shadow = groundTelegraph.LockCircle();
                EmitSpecialCue(BossAttackTelegraphType.DodgeOnly, impactIn - t, "LEAP");
            }
            landing = new Vector3(shadow.x, start.y, shadow.z);

            // 도약 중 수평 이동: 그림자 쪽으로 (착지점은 그림자 중심 바로 앞)
            if (t >= takeoff)
            {
                Vector3 to = landing - transform.position;
                to.y = 0f;
                float travel = to.magnitude;
                Vector3 dir = travel > 0.001f ? to / travel : transform.forward;
                Vector3 goal = landing - dir * 0.55f;
                Vector3 toGoal = goal - transform.position;
                toGoal.y = 0f;
                float remainingTime = Mathf.Max(Time.deltaTime, impactIn - t);
                float maxStep = Mathf.Min(leapMaxTravel / Mathf.Max(0.2f, impactIn - takeoff), 30f) * Time.deltaTime;
                Vector3 step = Vector3.ClampMagnitude(toGoal * Mathf.Clamp01(Time.deltaTime / remainingTime), maxStep);
                if (step.sqrMagnitude > 0.000001f)
                    Locomotion.ApplyMovementDelta(step);
                if (travel > 0.05f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 540f * Time.deltaTime);
            }

            t += Time.deltaTime;
            yield return null;
        }

        ApplySpecialAreaDamage(landing, leapRadius, leapDamage, unblockable: false);
        SpecialImpactFeedback(landing, leapRadius * 1.1f, AttackTelegraphType.Dodge, 0.45f);

        StageAttackRecovery("LEAP", leapRecovery, leapRecovery, 1.25f);
        float rest = leapRecovery;
        while (rest > 0f && CanContinueSpecial())
        {
            rest -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }
    }

    void ApplySpecialAreaDamage(Vector3 center, float radius, int baseDamage, bool unblockable)
    {
        if (playerTarget == null)
            return;
        Vector3 p = playerTarget.position;
        Vector3 d = p - center;
        float vertical = Mathf.Abs(d.y);
        d.y = 0f;
        if (d.magnitude > radius || vertical > 2.6f)
            return;

        IDamageReceiver receiver = playerTarget.GetComponentInParent<IDamageReceiver>();
        if (receiver == null)
            receiver = playerTarget.GetComponentInChildren<IDamageReceiver>();
        if (receiver == null)
            return;

        float damage = Mathf.Max(0f, baseDamage * Mathf.Max(0f, outgoingDamageMultiplier) * Tuning.ResolveDifficultyDamageMultiplier());
        receiver.ReceiveHit(new HitPayload
        {
            damage = damage,
            hitType = HitType.Heavy,
            hitPoint = p + Vector3.up * 0.9f,
            hitDirection = d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward,
            attacker = transform,
            canParry = false,
            canPerfectDodge = true,
            canGuard = false,
            causesGuardBreak = false,
            unblockable = true
        });
    }

    void SpecialImpactFeedback(Vector3 center, float radius, AttackTelegraphType type, float shake)
    {
        if (groundTelegraph != null)
            groundTelegraph.ShowCircle(center, radius, type, 0.16f); // 착지 충격 링 (빠르게 퍼짐)
        Camera cam = Camera.main;
        CameraShake cs = cam != null ? cam.GetComponentInParent<CameraShake>() : null;
        if (cs == null) cs = FindObjectOfType<CameraShake>();
        if (cs != null)
            cs.Shake(shake, 0.28f);
    }

    void OnDisableSpecialPatterns()
    {
        if (_reactiveSubscribed)
        {
            _reactiveSubscribed = false;
            PlayerConsumables.AnyHealStarted -= HandlePlayerHealStarted;
            PlayerDodgeController.AnyDodgeStarted -= HandlePlayerDodgeStarted;
        }
    }
}
