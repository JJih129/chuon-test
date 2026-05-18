// 파일명: BossController.cs
// 보스 FSM + 이동 + 패턴 + 근접 백스텝 + 사망 시 루트 파괴
// 트리거는 PlayAnimTrigger() 하나로 관리해서 한 번에 하나만 켜지도록 설계.
// 공격 진입 전 플레이어 쪽으로 회전 보정(스냅/짧은 회전) 지원.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public enum BossState
{
    Recovery = -1,
    IntroIdle,   // 전투 시작 전 연출용 대기
    Detect,      // 플레이어 거리/상태 체크
    Move,        // 플레이어에게 이동
    Attack,      // 패턴 수행 중
    CombatIdle,  // 공격 후 잠깐 휴지
    Break,       // 브레이크(그로기)
    Dead         // 사망
}

public enum AttackTelegraphType
{
    Auto = 0,
    Parry = 1,
    Guard = 2,
    Dodge = 3,
    Danger = 4
}

public enum AttackTimingStyle
{
    Auto = 0,
    Standard = 1,
    Delayed = 2,
    FakeOut = 3
}

[System.Serializable]
public class AttackPattern
{
    [Header("기본 정보 (한글 설명)")]
    [Tooltip("패턴 이름(디버그/금지 패턴 조건에 사용)")]
    public string patternName;

    [Tooltip("애니메이터 트리거/스테이트 이름 (예: Attack_A, Attack_B 등)")]
    public string animTriggerName;

    [Header("Pattern Selector")]
    [Tooltip("Enum 기반 패턴 선택 데이터. None이면 기존 patternName/animTriggerName/range/weight/cooldown 값을 자동 미러링합니다.")]
    public BossPatternData selectorData = BossPatternData.CreateDefault(BossPatternId.None);

    [Header("Phase Modifiers")]
    [Tooltip("현재 페이즈에만 적용되는 패턴 변형값. patternId가 None이면 이 AttackPattern의 selectorData/자동 ID에 적용됩니다.")]
    public BossPhasePatternModifier[] phaseModifiers;

    [Tooltip("이 공격이 패링 가능한 공격인지 여부")]
    public bool isParryable;
    public bool canPerfectDodge = true;
    public bool canGuard = true;
    public bool isUnblockable = false;
    public bool causesGuardBreak = false;

    [Tooltip("이 패턴의 기본 데미지량")]
    public int damageAmount = 10;

    [Header("Telegraph")]
    [Tooltip("Auto면 기존 isParryable 값을 따라가고, 아니면 지정한 대응 타입을 사용합니다.")]
    public AttackTelegraphType telegraphType = AttackTelegraphType.Auto;

    [Tooltip("공격 시작 전에 읽을 수 있도록 주는 선행 신호 시간(초). 0이면 기본값 사용.")]
    public float telegraphLeadTime = 0.18f;

    [Tooltip("HUD에 표시할 라벨. 비워두면 타입 기반 기본 문구를 사용합니다.")]
    public string telegraphLabel;

    [Header("Timing Style")]
    [Tooltip("공격 타이밍 변주 방식. Auto면 대응 타입 기준 기본값 사용.")]
    public AttackTimingStyle timingStyle = AttackTimingStyle.Auto;

    [Tooltip("지연/페인트에 추가로 붙는 홀드 시간. 0 이하면 스타일 기본값 사용.")]
    public float extraHoldDelay = 0f;

    [Tooltip("두 번째 경고가 필요할 때 HUD에 붙는 접미사.")]
    public string secondaryTelegraphSuffix = "HOLD";

    [Header("Pre-Attack Pose")]
    [Tooltip("패링 불가 패턴일 때 선행 자세를 잠깐 잡은 뒤 공격합니다.")]
    public bool usePreAttackPoseWhenNotParryable = false;
    [Tooltip("선행 자세용 Animator 트리거. 비워두면 자세 없이 대기만 합니다.")]
    public string preAttackPoseTriggerName;
    [Tooltip("선행 자세 유지 시간(초). 0이면 사용하지 않습니다.")]
    public float preAttackPoseDuration = 0f;
    [Tooltip("선행 자세 중 HUD에 표시할 라벨. 비워두면 READY를 자동으로 붙입니다.")]
    public string preAttackPoseLabel;

    [Header("쿨타임 / 가중치 / 거리 조건 (한글 설명)")]
    [Tooltip("패턴 사용 후 다시 사용할 때까지의 쿨타임(초)")]
    public float cooldown = 2f;

    [HideInInspector] public float currentCooldown;
    [NonSerialized] public float cooldownReadyAt;

    [Tooltip("패턴 선택 시 랜덤 가중치 (값이 클수록 선택될 확률↑)")]
    public float weight = 1f;

    [Tooltip("플레이어와의 최소 거리 조건 (이보다 가까우면 사용 안 함)")]
    public float minRange = 0f;

    [Tooltip("플레이어와의 최대 거리 조건 (이보다 멀면 사용 안 함)")]
    public float maxRange = 5f;

    [Tooltip("이 패턴 직전에 금지할 패턴 이름 (없으면 빈 문자열)")]
    public string forbiddenAfter;

    [Header("Recovery / Punish")]
    [Tooltip("공격 후 빈틈 유지 시간. 0 이하면 텔레그래프 타입 기본값 사용.")]
    public float recoveryTime = 0f;

    [Tooltip("플레이어가 되받아칠 수 있는 유효 시간. 0 이하면 recoveryTime 사용.")]
    public float punishWindowDuration = 0f;

    [Tooltip("빈틈 시간 동안 받는 피해 배수. 1 이하면 텔레그래프 타입 기본값 사용.")]
    public float punishDamageMultiplier = 1f;

    [Header("Ranged Sword Wave")]
    public bool firesSwordWaveProjectile = false;
    [Min(0f)] public float swordWaveFireDelay = 0.03f;
    [Min(0.1f)] public float swordWaveSpeed = 12f;
    [Min(0.1f)] public float swordWaveLifeTime = 1.4f;
    [Min(0.1f)] public float swordWaveWidth = 1.15f;
    [Min(0.1f)] public float swordWaveHeight = 0.55f;
    [Min(0.2f)] public float swordWaveLength = 1.9f;

    [Header("Hitbox Tuning")]
    public float hitboxExpandedPadding = 0f;

    [Range(0f, 1f)]
    public float hitboxMeshPaddingScale = 0f;

    public float hitboxScanInterval = 0f;
    public float hitboxOneShotWindow = 0f;

    [Header("Follow Up")]
    [Tooltip("근거리에서 다음 공격으로 바로 잇는 연계를 허용")]
    public bool allowFollowUpChain = false;

    [Range(0f, 1f)]
    [Tooltip("연계 시도 확률. 0 이하면 타입 기준 기본값 사용.")]
    public float followUpChance = 0f;

    [Tooltip("이 거리 안에 플레이어가 있으면 연계 후보를 본다. 0 이하면 기본값 사용.")]
    public float followUpMaxDistance = 0f;

    [Tooltip("연계 공격 전 짧은 템포 지연. 0 이하면 기본값 사용.")]
    public float followUpDelay = 0f;

    [Tooltip("비워두면 자동 선택, 값이 있으면 해당 패턴명으로 고정 연계")]
    public string forcedFollowUpPattern = string.Empty;

    [Tooltip("후속타에서 우선적으로 노릴 대응 타입. Auto면 원본 패턴 기준 기본값 사용.")]
    public AttackTelegraphType preferredFollowUpTelegraph = AttackTelegraphType.Auto;

    [Header("Phase")]
    [Tooltip("0이면 자동, 1~3이면 해당 페이즈부터 사용 가능")]
    public int minPhase = 0;

    [Tooltip("0이면 제한 없음, 1~3이면 해당 페이즈까지만 사용 가능")]
    public int maxPhase = 0;

    public bool CanExecute(BossController boss, float distance, string lastPattern)
    {
        if (currentCooldown > 0f) return false;
        if (distance < minRange || distance > maxRange) return false;
        if (boss != null && !IsAvailableInPhase(boss.CurrentPhase)) return false;

        if (!string.IsNullOrEmpty(forbiddenAfter) &&
            lastPattern.Equals(forbiddenAfter, StringComparison.Ordinal))
            return false;

        if (boss.playerTracker == null) return true;

        // 필요하면 이름 기준으로 세부 조건 추가 가능
        return true;
    }

    public void SyncCooldown(float now)
    {
        if (cooldownReadyAt <= 0f)
        {
            if (currentCooldown < 0f)
                currentCooldown = 0f;
            return;
        }

        currentCooldown = Mathf.Max(0f, cooldownReadyAt - now);
        if (currentCooldown <= 0f)
            cooldownReadyAt = 0f;
    }

    public void StartCooldown(float now)
    {
        currentCooldown = Mathf.Max(0f, cooldown);
        cooldownReadyAt = currentCooldown > 0f ? now + currentCooldown : 0f;
    }

    public void RestoreCooldownAnchor(float now)
    {
        cooldownReadyAt = currentCooldown > 0f ? now + currentCooldown : 0f;
    }

    public AttackTelegraphType ResolveTelegraphType()
    {
        if (telegraphType != AttackTelegraphType.Auto)
            return telegraphType;

        if (isUnblockable || (!canGuard && causesGuardBreak))
            return AttackTelegraphType.Danger;

        if (isParryable)
            return AttackTelegraphType.Parry;

        if (canGuard)
            return AttackTelegraphType.Guard;

        return AttackTelegraphType.Dodge;
    }

    public float ResolveTelegraphLeadTime()
    {
        return telegraphLeadTime > 0.01f ? telegraphLeadTime : 0.18f;
    }

    public string ResolveTelegraphLabel()
    {
        if (!string.IsNullOrWhiteSpace(telegraphLabel))
            return telegraphLabel.Trim().ToUpperInvariant();

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return "PARRY";
            case AttackTelegraphType.Guard:
                return "GUARD";
            case AttackTelegraphType.Danger:
                return "DANGER";
            case AttackTelegraphType.Dodge:
            default:
                return "DODGE";
        }
    }

    public bool ResolveCanParry()
    {
        switch (telegraphType != AttackTelegraphType.Auto ? telegraphType : ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return isParryable;
            case AttackTelegraphType.Guard:
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Danger:
            default:
                return false;
        }
    }

    public bool ResolveCanPerfectDodge()
    {
        return canPerfectDodge;
    }

    public bool ResolveCanGuard()
    {
        return canGuard && !ResolveIsUnblockable();
    }

    public bool ResolveIsUnblockable()
    {
        return isUnblockable
            || telegraphType == AttackTelegraphType.Danger
            || (telegraphType == AttackTelegraphType.Auto && !canGuard && causesGuardBreak);
    }

    public bool ResolveCausesGuardBreak()
    {
        return causesGuardBreak;
    }

    public float ResolveRecoveryTime()
    {
        if (recoveryTime > 0.01f)
            return recoveryTime;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return 0.38f;
            case AttackTelegraphType.Guard:
                return 0.46f;
            case AttackTelegraphType.Dodge:
                return 0.64f;
            case AttackTelegraphType.Danger:
                return 0.92f;
            case AttackTelegraphType.Auto:
            default:
                return 0.42f;
        }
    }

    public float ResolvePunishWindowDuration()
    {
        if (punishWindowDuration > 0.01f)
            return punishWindowDuration;

        return ResolveRecoveryTime();
    }

    public float ResolvePunishDamageMultiplier()
    {
        if (punishDamageMultiplier > 1.01f)
            return punishDamageMultiplier;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return 1.12f;
            case AttackTelegraphType.Guard:
                return 1.05f;
            case AttackTelegraphType.Dodge:
                return 1.22f;
            case AttackTelegraphType.Danger:
                return 1.35f;
            case AttackTelegraphType.Auto:
            default:
                return 1.10f;
        }
    }

    public bool ResolveFiresSwordWaveProjectile()
    {
        return firesSwordWaveProjectile;
    }

    public float ResolveHitboxExpandedPadding(float fallback)
    {
        return hitboxExpandedPadding > 0.001f ? hitboxExpandedPadding : Mathf.Max(0f, fallback);
    }

    public float ResolveHitboxMeshPaddingScale(float fallback)
    {
        if (hitboxMeshPaddingScale > 0.001f)
            return Mathf.Clamp(hitboxMeshPaddingScale, 0.1f, 1f);

        return Mathf.Clamp(fallback, 0.1f, 1f);
    }

    public float ResolveHitboxScanInterval(float fallback)
    {
        return hitboxScanInterval > 0.001f ? hitboxScanInterval : Mathf.Max(0f, fallback);
    }

    public float ResolveHitboxOneShotWindow(float fallback)
    {
        return hitboxOneShotWindow > 0.001f ? hitboxOneShotWindow : Mathf.Max(0f, fallback);
    }

    public AttackTimingStyle ResolveTimingStyle()
    {
        if (timingStyle != AttackTimingStyle.Auto)
            return timingStyle;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Danger:
                return AttackTimingStyle.FakeOut;
            case AttackTelegraphType.Dodge:
                return AttackTimingStyle.Delayed;
            case AttackTelegraphType.Parry:
            case AttackTelegraphType.Guard:
            case AttackTelegraphType.Auto:
            default:
                return AttackTimingStyle.Standard;
        }
    }

    public float ResolveExtraHoldDelay()
    {
        if (extraHoldDelay > 0.01f)
            return extraHoldDelay;

        switch (ResolveTimingStyle())
        {
            case AttackTimingStyle.Delayed:
                return 0.14f;
            case AttackTimingStyle.FakeOut:
                return 0.22f;
            case AttackTimingStyle.Standard:
            case AttackTimingStyle.Auto:
            default:
                return 0f;
        }
    }

    public string ResolveSecondaryTelegraphLabel()
    {
        string suffix = string.IsNullOrWhiteSpace(secondaryTelegraphSuffix) ? "HOLD" : secondaryTelegraphSuffix.Trim().ToUpperInvariant();
        return $"{ResolveTelegraphLabel()} {suffix}";
    }

    public bool ResolveUsePreAttackPose()
    {
        return usePreAttackPoseWhenNotParryable
            && !ResolveCanParry()
            && preAttackPoseDuration > 0.01f;
    }

    public float ResolvePreAttackPoseDuration()
    {
        return ResolveUsePreAttackPose() ? Mathf.Max(0f, preAttackPoseDuration) : 0f;
    }

    public string ResolvePreAttackPoseTriggerName()
    {
        return string.IsNullOrWhiteSpace(preAttackPoseTriggerName)
            ? string.Empty
            : preAttackPoseTriggerName.Trim();
    }

    public string ResolvePreAttackPoseLabel()
    {
        if (!string.IsNullOrWhiteSpace(preAttackPoseLabel))
            return preAttackPoseLabel.Trim().ToUpperInvariant();

        return $"{ResolveTelegraphLabel()} READY";
    }

    public bool ResolveCanChainFollowUp()
    {
        if (allowFollowUpChain)
            return true;

        return ResolveTelegraphType() == AttackTelegraphType.Parry
            && damageAmount <= 20
            && maxRange <= 4.5f;
    }

    public float ResolveFollowUpChance()
    {
        if (!ResolveCanChainFollowUp())
            return 0f;

        if (followUpChance > 0.001f)
            return Mathf.Clamp01(followUpChance);

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return 0.42f;
            case AttackTelegraphType.Guard:
                return 0.26f;
            default:
                return 0f;
        }
    }

    public float ResolveFollowUpMaxDistance()
    {
        if (followUpMaxDistance > 0.01f)
            return followUpMaxDistance;

        return 2.85f;
    }

    public float ResolveFollowUpDelay()
    {
        if (followUpDelay > 0.01f)
            return followUpDelay;

        return 0.10f;
    }

    public int ResolveMinPhase()
    {
        if (minPhase > 0)
            return Mathf.Clamp(minPhase, 1, 3);

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Danger:
                return 3;
            case AttackTelegraphType.Dodge:
                return 2;
            case AttackTelegraphType.Guard:
                return damageAmount >= 20 ? 2 : 1;
            default:
                return 1;
        }
    }

    public int ResolveMaxPhase()
    {
        if (maxPhase > 0)
            return Mathf.Clamp(Mathf.Max(ResolveMinPhase(), maxPhase), 1, 3);

        return 3;
    }

    public bool IsAvailableInPhase(int phase)
    {
        int resolvedPhase = Mathf.Clamp(phase, 1, 3);
        return resolvedPhase >= ResolveMinPhase() && resolvedPhase <= ResolveMaxPhase();
    }

    public AttackTelegraphType ResolvePreferredFollowUpTelegraph()
    {
        if (preferredFollowUpTelegraph != AttackTelegraphType.Auto)
            return preferredFollowUpTelegraph;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
            case AttackTelegraphType.Guard:
                return AttackTelegraphType.Dodge;
            case AttackTelegraphType.Dodge:
                return AttackTelegraphType.Parry;
            case AttackTelegraphType.Danger:
            case AttackTelegraphType.Auto:
            default:
                return AttackTelegraphType.Auto;
        }
    }
}

public class BossController : MonoBehaviour, IUltimateVictimState, IParryReact, IPerfectDodgeReact
{
    const string FollowUpTelegraphPrefix = "CHAIN";
    const float FollowUpTelegraphLeadScale = 0.82f;
    const float CombatIdleAttackRequestDelay = 0.25f;
    const float MaxCombatIdleAttackRetryDelay = 0.95f;
    const float MaxCombatIdleRetreatPause = 0.18f;
    const float MaxReactiveBackstepDistance = 1.15f;
    const float MinReactiveBackstepCooldown = 5.0f;
    const int MaxConsecutiveMovementPostActions = 1;
    const float MinEffectiveSwordWaveRange = 7.0f;
    const float MaxEffectiveSwordWaveRange = 13.5f;
    const float MaxEffectiveSwordWaveWeight = 1.10f;

    [Header("참조 컴포넌트 (한글 설명)")]
    [Tooltip("보스 체력/사망 이벤트 담당 컴포넌트")]
    public BossHealth bossHealth;

    [Tooltip("브레이크(그로기) 상태 관리 컴포넌트")]
    public BossBreakController breakController;

    [Tooltip("보스 애니메이터 (Move/Break/Dead/Attack 파라미터 전달용)")]
    public Animator bossAnimator;

    [Tooltip("플레이어 위치 트래킹용 Transform")]
    public Transform playerTarget;

    [Tooltip("공격 패턴 비주얼(가드 가능/불가 표시 등)")]
    public PatternVisuals patternVisuals;
    [SerializeField] private BossAttackVfxPresenter attackVfxPresenter;
    [Tooltip("공격 선행 바닥 위험 구역 표시용")]
    [SerializeField] private BossGroundTelegraph groundTelegraph;
    [Tooltip("패링 불가 패턴 선행 자세에 사용할 기본 애니메이션 클립. 비워두면 트리거/대기만 사용합니다.")]
    [SerializeField] private AnimationClip defaultPreAttackPoseClip;
    [Tooltip("패링 불가 패턴이면 별도 설정이 없어도 기본 발도 자세를 사용합니다.")]
    [SerializeField] private bool useDefaultPreAttackPoseForNonParryable = true;
    [Tooltip("기본 발도 자세 유지 시간(초)")]
    [SerializeField, Min(0f)] private float defaultPreAttackPoseDuration = 0.42f;
    [Tooltip("기본 발도 자세 HUD 라벨")]
    [SerializeField] private string defaultPreAttackPoseLabel = "READY";

    [Header("물리 이동 설정 (한글 설명)")]
    [Tooltip("보스 이동에 사용할 Rigidbody (없으면 Transform 직접 이동)")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private LayerMask movementCollisionMask = ~0;
    [SerializeField] private float movementSkin = 0.03f;
    [SerializeField] private bool useCollisionAwareMovement = true;
    [SerializeField] private bool useCollisionAwareRootMotion = true;

    [Header("공격 히트박스 (공통 컴포넌트)")]
    [Tooltip("보스 무기/팔 등에 붙은 AttackHitbox")]
    public AttackHitbox attackHitbox;

    [Header("Damage")]
    [SerializeField, Range(0.1f, 2f)] private float outgoingDamageMultiplier = 0.5f;

    [Tooltip("플레이어 행동 기반 패턴 제어용 트래커(선택)")]
    public PlayerTracker playerTracker;

    [Header("FSM 기본/타이밍 설정 (한글 설명)")]
    [Tooltip("현재 보스 FSM 상태 (디버그 확인용)")]
    public BossState currentState = BossState.IntroIdle;

    [Tooltip("인트로 연출용 대기 시간 (초). 0이면 바로 전투 시작.")]
    public float introIdleDuration = 0.5f;

    [Tooltip("공격 후 CombatIdle 상태 유지 시간(초). 0이면 바로 다음 상태로 이동.")]
    public float combatIdleTime = 0.3f;
    [SerializeField, Min(0f)] private float minimumPostAttackIdleDuration = 5.0f;
    [SerializeField] private bool useCombatIdleStrafe = true;
    [SerializeField, Range(0.1f, 1f)] private float combatIdleStrafeSpeedMultiplier = 0.42f;
    [SerializeField, Range(0.1f, 1f)] private float combatIdleApproachSpeedMultiplier = 0.34f;
    [SerializeField] private float combatIdleDesiredDistance = 2.6f;
    [SerializeField] private float combatIdleApproachThreshold = 4.4f;
    [SerializeField] private float combatIdleDistanceTolerance = 0.45f;
    [SerializeField] private float combatIdleFacingTurnSpeed = 540f;
    [SerializeField, Range(0.1f, 1.5f)] private float combatIdleOrbitBias = 0.9f;
    [SerializeField, Range(0f, 1.5f)] private float combatIdleRadialCorrectionWeight = 0.65f;
    [SerializeField, Min(0.1f)] private float combatIdleStrafeSideHoldMin = 0.9f;
    [SerializeField, Min(0.1f)] private float combatIdleStrafeSideHoldMax = 1.8f;
    [SerializeField, Range(0f, 1f)] private float combatIdleStrafeSideSwapChance = 0.32f;
    [SerializeField, Min(0f)] private float combatIdlePreRetreatPause = 1.0f;
    [SerializeField, Min(0f)] private float combatIdlePostRetreatPause = 1.0f;
    [SerializeField] private bool useCombatIdleRetreatWhenTooClose = true;
    [SerializeField] private string combatIdleRetreatTriggerName = "Quickshift_B";
    [SerializeField] private float combatIdleRetreatTriggerDistance = 1.7f;
    [SerializeField] private float combatIdleRetreatDuration = 0.42f;
    [SerializeField] private float combatIdleRetreatSpeed = 4.4f;

    [Header("Attack Tempo Tuning")]
    [SerializeField, Min(0f)] private float globalPostAttackRecoveryPadding = 0.14f;
    [SerializeField, Min(0f)] private float globalFollowUpDelayPadding = 0.08f;

    [Tooltip("궁극기 victim 상태 해제 직후 AI가 다시 패턴을 잡기 전 쉬는 시간(초).")]
    [SerializeField] private float ultimateVictimRecoveryDuration = 1.2f;

    [Tooltip("공격 상태에서 애니메이션 종료를 기다리는 최대 시간 (초). 안전장치 역할")]
    public float maxAttackStateWaitTime = 2.0f;

    [Header("Phase Tuning")]
    [Range(0.15f, 0.95f)] [SerializeField] private float phaseTwoThresholdNormalized = 0.66f;
    [Range(0.05f, 0.75f)] [SerializeField] private float phaseThreeThresholdNormalized = 0.33f;
    [SerializeField] private bool useDefaultPhaseModifierFallback = true;
    [SerializeField] private float phaseTwoTelegraphScale = 0.96f;
    [SerializeField] private float phaseThreeTelegraphScale = 0.90f;
    [Range(0f, 0.5f)] [SerializeField] private float phaseTwoFollowUpChanceBonus = 0.08f;
    [Range(0f, 0.5f)] [SerializeField] private float phaseThreeFollowUpChanceBonus = 0.16f;
    [SerializeField] private int phaseThreeExtraFollowUpChains = 1;
    [SerializeField] private float phaseEntryPressureDuration = 5f;
    [SerializeField] private float phaseEntryUnlockedPatternWeight = 1.75f;
    [SerializeField, Min(0f)] private float phaseTransitionAttackLockDuration = 0.45f;

    [Header("Difficulty Tuning")]
    [SerializeField] private BossDifficultyTier difficultyTier = BossDifficultyTier.Normal;
    [SerializeField] private BossDifficultyProfile[] difficultyProfiles = BossDifficultyProfile.CreateDefaultSet();

    [Header("Tempo Tuning")]
    [SerializeField] private float minimumParryTelegraphLeadTime = 0.22f;
    [SerializeField] private float minimumGuardTelegraphLeadTime = 0.24f;
    [SerializeField] private float minimumDodgeTelegraphLeadTime = 0.30f;
    [SerializeField] private float minimumDangerTelegraphLeadTime = 0.42f;
    [SerializeField] private float minimumParryPunishWindow = 0.34f;
    [SerializeField] private float minimumGuardPunishWindow = 0.30f;
    [SerializeField] private float minimumDodgePunishWindow = 0.46f;
    [SerializeField] private float minimumDangerPunishWindow = 0.62f;
    [Header("Parry Reaction")]
    [SerializeField, Range(0.04f, 0.35f)] private float parryStunDuration = 0.22f;
    [SerializeField, Min(0.1f)] private float parryRecoveryDuration = 0.55f;
    [SerializeField, Min(0.1f)] private float parryPunishWindowDuration = 0.55f;
    [SerializeField, Range(1f, 3f)] private float parryPunishDamageMultiplier = 1.35f;
    [SerializeField] private string parryPunishSource = "PARRY";
    [SerializeField] private string parryStunTriggerName = "Stagger";
    [SerializeField] private string parryStunStateName = "Break";
    [SerializeField, Range(0f, 0.08f)] private float parryStunTransitionDuration = 0.03f;

    [Header("Combat Recovery")]
    [SerializeField] private BossCombatRecoverySettings combatRecoverySettings = BossCombatRecoverySettings.CreateDefault();

    [SerializeField] private float followUpRecoveryTax = 0.18f;
    [Range(0f, 1f)] [SerializeField] private float fakeOutFollowUpChanceMultiplier = 0.18f;
    [Range(0f, 1f)] [SerializeField] private float dangerFollowUpChanceMultiplier = 0.06f;
    [Range(0f, 1f)] [SerializeField] private float punishHeavyFollowUpChanceMultiplier = 0.24f;
    [Range(0.05f, 1f)] [SerializeField] private float immediateRepeatPatternWeightMultiplier = 0.22f;
    [Range(0.05f, 1f)] [SerializeField] private float recentRepeatPatternWeightMultiplier = 0.60f;
    [Range(0.25f, 1f)] [SerializeField] private float repeatedTelegraphWeightMultiplier = 0.82f;
    [Range(1, 4)] [SerializeField] private int recentPatternMemory = 3;

    [Header("Player Response Tuning")]
    [SerializeField] private bool usePlayerStatePatternBias = true;
    [SerializeField, Min(0f)] private float farPressureDistance = 7.5f;
    [SerializeField, Min(0f)] private float farPressureDelay = 1.25f;
    [SerializeField, Range(1f, 3f)] private float farPressureRangedWeightMultiplier = 1.55f;
    [SerializeField, Range(1f, 3f)] private float farPressureChaseWeightMultiplier = 1.25f;
    [SerializeField, Range(1f, 2.5f)] private float farPressureEngageSpeedMultiplier = 1.15f;
    [SerializeField, Min(0f)] private float closeLingerDistance = 2.2f;
    [SerializeField, Min(0f)] private float closeLingerDelay = 1.1f;
    [SerializeField, Range(1f, 3f)] private float closeLingerPunishWeightMultiplier = 1.35f;
    [SerializeField, Range(0.1f, 1f)] private float closeLingerRangedWeightMultiplier = 0.62f;
    [SerializeField, Min(0.1f)] private float playerDefenseResponseMemoryTime = 6f;
    [SerializeField, Range(1, 5)] private int repeatedParryResponseThreshold = 2;
    [SerializeField, Range(1, 5)] private int repeatedPerfectDodgeResponseThreshold = 2;
    [SerializeField, Range(0.1f, 1f)] private float repeatedParryParryableWeightMultiplier = 0.72f;
    [SerializeField, Range(1f, 3f)] private float repeatedParryDodgeOnlyWeightMultiplier = 1.22f;
    [SerializeField, Range(0.1f, 1f)] private float repeatedPerfectDodgeDodgeOnlyWeightMultiplier = 0.72f;
    [SerializeField, Range(1f, 3f)] private float repeatedPerfectDodgeParryableWeightMultiplier = 1.16f;
    [SerializeField, Min(0.01f)] private float playerObservationMoveSpeedThreshold = 0.35f;
    [SerializeField, Range(1f, 3f)] private float playerDodgingDelayedAttackWeightMultiplier = 1.18f;
    [SerializeField, Range(1f, 3f)] private float playerGuardingPunishWeightMultiplier = 1.20f;
    [SerializeField, Range(1f, 3f)] private float playerAttackingCounterWeightMultiplier = 1.18f;
    [SerializeField, Range(0.1f, 1f)] private float playerStunnedRangedWeightMultiplier = 0.55f;
    [SerializeField, Range(1f, 3f)] private float playerRecentlyHitBossCounterWeightMultiplier = 1.16f;
    [SerializeField, Min(0.5f)] private float bossMissedPressureDelay = 4.0f;
    [SerializeField, Range(1f, 3f)] private float bossMissedPressureWeightMultiplier = 1.18f;
    [SerializeField, Range(30f, 180f)] private float playerSideAngleThreshold = 75f;
    [SerializeField, Range(60f, 180f)] private float playerBackAngleThreshold = 125f;
    [SerializeField, Range(1f, 3f)] private float playerBackAngleCounterWeightMultiplier = 1.18f;
    [SerializeField, Range(0f, 0.6f)] private float playerStateReactionDelay = 0.18f;

    [Header("Spatial Response Tuning")]
    [SerializeField] private bool useSpatialPatternBias = true;
    [SerializeField, Min(0.05f)] private float spatialBiasProbeDistance = 1.35f;
    [SerializeField, Range(0.1f, 1f)] private float blockedBackstepWeightMultiplier = 0.45f;
    [SerializeField, Range(0.1f, 1f)] private float cornerRangedWeightMultiplier = 0.70f;
    [SerializeField, Range(1f, 3f)] private float cornerRecenterWeightMultiplier = 1.25f;
    [SerializeField] private BossArenaAwarenessSettings arenaAwarenessSettings = BossArenaAwarenessSettings.CreateDefault();

    [Header("Debug")]
    [SerializeField] private bool enableStateLogs = false;
    [SerializeField] private bool enablePatternDebugLog = false;
    [SerializeField] private bool enablePlayerObservationDebugLog = false;
    [SerializeField, Min(0.1f)] private float playerObservationDebugInterval = 0.75f;
    [SerializeField] private bool enablePatternTelemetryDebugLog = false;
    [SerializeField] private bool enableCombatRecoveryTelemetryDebugLog = false;
#if UNITY_EDITOR
    [SerializeField] private bool drawAttackRangeGizmos = true;
    [SerializeField] private bool drawEngageRangeGizmos = true;
    [SerializeField] private bool drawHitboxTimingGizmos = true;
    [SerializeField] private bool drawArenaAwarenessGizmos = true;
#endif

    private Coroutine _stateRoutine;
    private Coroutine _parryStunRoutine;
    private UltimateAnimatorClipSampler _preAttackPoseClipSampler;
    private bool _isPreAttackPoseActive;
    private float _preAttackPoseElapsed;
    private float _preAttackPoseDurationRuntime;
    private bool _isDead;
    public event Action<AttackTelegraphType, float, string> OnAttackTelegraph;
    public event Action<BossAttackTelegraphType, float, BossAttackTimingCueFlags> OnAttackTimingCue;
    public event Action<float, float, string> OnPunishWindowOpened;
    public event Action OnPunishWindowClosed;
    public event Action<int, float> OnBossPhaseChanged;
    public event Action<BossPatternTelemetrySample> OnPatternTelemetrySample;
    public event Action<BossCombatRecoveryTelemetrySample> OnCombatRecoveryTelemetrySample;

    [Header("이동 설정 (한글 설명)")]
    [Tooltip("실제 보스 이동 속도 (m/s)")]
    public float moveSpeed = 4.0f;

    [Tooltip("이 거리 이내로 들어오면 이동을 멈추고 공격 준비")]
    public float stoppingDistance = 1.0f;

    [Tooltip("공격 상태로 전환할 추가 거리 버퍼. 너무 멀리서 바로 공격으로 넘어가는 현상을 줄입니다.")]
    [SerializeField] private float attackCommitDistanceBuffer = 0.28f;

    [Tooltip("근거리에서 감속을 시작할 거리. stoppingDistance보다 커야 자연스럽게 접근합니다.")]
    [SerializeField] private float moveSlowdownDistance = 1.75f;
    [SerializeField] private bool enableVerboseCombatLogs = false;
    [SerializeField] private bool debugSwordWaveLogs = false;

    [Header("Pressure Bands")]
    [Tooltip("이 거리보다 가까우면 바로 직선 추적 대신 중립 압박/공전으로 전환합니다.")]
    [SerializeField] private float pressureEngageDistance = 4.6f;
    [Tooltip("중립 압박 상태에서 이 거리보다 멀어지면 다시 추적 이동으로 복귀합니다.")]
    [SerializeField] private float pressureReleaseDistance = 5.4f;
    [Tooltip("추적 이동 중 좌우 한쪽 성향을 유지하는 시간.")]
    [SerializeField] private Vector2 moveArcSideHoldRange = new Vector2(0.9f, 1.8f);
    [Tooltip("직선 추적 대신 호를 그리며 접근할 때 섞는 측면 비율.")]
    [Range(0f, 1f)] [SerializeField] private float moveArcStrength = 0.38f;

    [Tooltip("감속 구간에서도 유지할 최소 접근 속도 비율.")]
    [Range(0.15f, 1f)] [SerializeField] private float closeApproachSpeedMultiplier = 0.38f;

    [Header("Direct Sword Wave")]
    [SerializeField] private bool useDirectSwordWaveBranch = true;
    [SerializeField] private float swordWaveDirectMinRange = 3.2f;
    [SerializeField] private float swordWaveDirectMaxRange = 18f;
    [SerializeField] private float swordWaveDirectCooldown = 5.4f;
    [SerializeField] private float swordWaveDirectChargeTime = 1.15f;
    [SerializeField, Min(0f)] private float noPatternFallbackAttackDelay = 0.65f;
    [SerializeField, Min(0.01f)] private float attackSelectionRetryDelay = 0.12f;

    [Tooltip("일반 추적 중 회전 속도(도/초).")]
    [SerializeField] private float chaseTurnSpeed = 360f;

    [Tooltip("근거리에서 플레이어를 놓치지 않도록 쓰는 추가 회전 속도(도/초).")]
    [SerializeField] private float closeChaseTurnSpeed = 540f;

    [Tooltip("근거리 회전 보정이 강화되는 거리.")]
    [SerializeField] private float closeChaseTurnDistance = 1.8f;

    [Tooltip("Blend Tree로 전달할 MoveSpeed 보간 시간 (0에 가까울수록 즉각 반응)")]
    [Range(0.01f, 0.5f)]
    public float moveAnimDamp = 0.1f;

    [Header("Stop Motion")]
    [SerializeField] private bool useRunStartMotion = true;
    [SerializeField] private string runStartTriggerName = "RunStart";
    [SerializeField, Min(0.01f)] private float runStartMinMoveBlend = 0.25f;
    [SerializeField] private bool useTurnStartMotion = true;
    [SerializeField, Range(10f, 180f)] private float turnStartMinAngle = 55f;
    [SerializeField, Range(90f, 180f)] private float turnStart180Angle = 135f;
    [SerializeField, Min(0f)] private float turnStartCooldown = 0.2f;
    [SerializeField] private bool useRunStopMotion = false;
    [SerializeField] private string runStopTriggerName = "RunStop";
    [SerializeField, Min(0.01f)] private float runStopMinMoveBlend = 0.35f;
    [SerializeField, Min(0.05f)] private float runStopDuration = 0.45f;
    [SerializeField, Min(0f)] private float runStopCooldown = 0.18f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float obstacleProbeDistance = 2.2f;
    [SerializeField] private float obstacleProbeAngle = 35f;
    [SerializeField] private float obstacleSideProbeAngle = 65f;
    [SerializeField] private float detourCommitTime = 0.35f;
    [SerializeField] private float chaseDirectionRefreshInterval = 1f / 12f;

    private static readonly int AnimParam_MoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int AnimParam_MoveX = Animator.StringToHash("MoveX");
    private static readonly int AnimParam_MoveY = Animator.StringToHash("MoveY");
    private static readonly int AnimParam_IsCombatStrafing = Animator.StringToHash("IsCombatStrafing");
    private static readonly int AnimParam_IsBreak   = Animator.StringToHash("IsBreak");
    private static readonly int AnimParam_IsDead    = Animator.StringToHash("IsDead");
    private static readonly int AnimParam_QuickshiftB = Animator.StringToHash("Quickshift_B");
    private static readonly int AnimParam_RunStart = Animator.StringToHash("RunStart");
    private static readonly int AnimParam_RunStop = Animator.StringToHash("RunStop");
    private static readonly int AnimParam_TurnL90 = Animator.StringToHash("TurnL90");
    private static readonly int AnimParam_TurnR90 = Animator.StringToHash("TurnR90");
    private static readonly int AnimParam_TurnL180 = Animator.StringToHash("TurnL180");
    private static readonly int AnimParam_TurnR180 = Animator.StringToHash("TurnR180");

    private float _moveBlend; // 0~1
    private float _moveXBlend;
    private float _moveYBlend;
    private float _lastAppliedMoveBlend = float.NaN;
    private float _lastAppliedMoveX = float.NaN;
    private float _lastAppliedMoveY = float.NaN;
    private Vector3 _cachedDetourDirection;
    private float _cachedDetourUntil;
    private Vector3 _cachedChaseDesiredDirection;
    private Vector3 _cachedChaseDirection;
    private float _nextChaseDirectionRefreshAt;
    private bool _hasMoveXParam;
    private bool _hasMoveYParam;
    private bool _hasCombatStrafingParam;
    private bool _combatStrafeAnimActive;
    private bool _runStopActive;
    private bool _runStopCachedRootMotion;
    private bool _runStopHasCachedRootMotion;
    private float _runStopEndTime;
    private float _nextRunStopAllowedTime;
    private float _nextTurnStartAllowedTime;
    private bool _wasMovingForRunStart;
    private bool _wasMovingForRunStop;
    private float _lastRunStopMoveBlend;
    private float _moveArcSign = 1f;
    private float _moveArcSideUntil = float.NegativeInfinity;

    [Header("공격 패턴 목록 (한글 설명)")]
    [Tooltip("보스가 사용할 수 있는 모든 공격 패턴 리스트")]
    public List<AttackPattern> allPatterns;

    [Header("Pattern Selector")]
    [SerializeField] private bool usePatternSelector = true;
    [SerializeField] private bool useLegacyPatternSelectionFallback = false;
    [SerializeField] private BossPatternSelector patternSelector = new BossPatternSelector();
    [SerializeField] private BossPatternHistory patternSelectorHistory = new BossPatternHistory();

    [Header("Pattern Post Action")]
    [SerializeField] private BossPostActionSettings postActionSettings = BossPostActionSettings.CreateDefault();
    [SerializeField] private bool enablePostActionDebugLog = false;
    [SerializeField] private bool enableEngageDebugLog = false;

    [Header("Engage Safety")]
    [SerializeField, Min(0.05f)] private float engageStallCheckInterval = 0.25f;
    [SerializeField, Min(0.01f)] private float engageMinProgressDistance = 0.08f;
    [SerializeField, Min(0.05f)] private float engageBlockedAbortTime = 0.55f;

    [Header("Attack Timing")]
    [SerializeField] private BossAttackTimingData[] attackTimingData;
    [SerializeField] private bool useDefaultAttackTimingFallback = true;
    [SerializeField] private bool defaultAttackTimingControlsParryWindow = true;
    [SerializeField, Range(0.05f, 1f)] private float defaultAttackTimingTelegraphScale = 1f;
    [SerializeField, Range(0.05f, 1f)] private float defaultAttackTimingParryWindowScale = 0.65f;
    [SerializeField] private bool enableAttackTimingDebugLog = false;
    [SerializeField, Min(0f)] private float attackTimingCueShakeAmplitude = 0.045f;
    [SerializeField, Min(0f)] private float attackTimingCueShakeDuration = 0.14f;
    [SerializeField, Min(0f)] private float attackTimingCueShakeMinIntervalRealtime = 0.08f;
    [SerializeField] private AudioSource attackTimingCueAudioSource;
    [SerializeField] private AudioClip attackTimingWarningClip;
    [SerializeField, Range(0f, 1f)] private float attackTimingWarningVolume = 0.85f;
    [SerializeField, Min(0f)] private float attackTimingWarningMinIntervalRealtime = 0.08f;
    [SerializeField] private BossTelegraphVfxPresenter telegraphVfxPresenter;
    [SerializeField] private bool autoCreateRuntimeTelegraphVfxPresenter = true;
    [SerializeField] private BossAttackFeedbackPresenter attackFeedbackPresenter;
    [SerializeField] private bool autoCreateRuntimeAttackFeedbackPresenter = true;

    private string _lastExecutedPattern = string.Empty;
    private AttackPattern _currentPattern;
    private AttackPattern _queuedFollowUpPattern;
    private float _queuedFollowUpDelay;
    private int _followUpChainDepth;
    private int _currentPhase = 1;
    private float _farPressureStartedAt = float.NegativeInfinity;
    private float _closeLingerStartedAt = float.NegativeInfinity;
    private float _lastParryResponseAt = float.NegativeInfinity;
    private int _recentParryResponseCount;
    private float _lastPerfectDodgeResponseAt = float.NegativeInfinity;
    private int _recentPerfectDodgeResponseCount;
    private BossPlayerCombatObservation _playerObservation = BossPlayerCombatObservation.CreateInvalid();
    private BossPlayerCombatObservation _effectivePlayerObservation = BossPlayerCombatObservation.CreateInvalid();
    private BossPlayerCombatObservation _pendingPlayerReactionObservation = BossPlayerCombatObservation.CreateInvalid();
    private bool _hasPendingPlayerReactionObservation;
    private float _pendingPlayerReactionReadyAt = float.NegativeInfinity;
    private ICombatStateReader _cachedPlayerCombatStateReader;
    private PlayerHealth _cachedPlayerHealth;
    private Transform _lastObservedPlayerTarget;
    private Vector3 _lastObservedPlayerPosition;
    private float _lastPlayerObservationTime = float.NegativeInfinity;
    private float _lastPlayerAttackAt = float.NegativeInfinity;
    private float _lastBossHitPlayerAt = float.NegativeInfinity;
    private float _lastPlayerHitBossAt = float.NegativeInfinity;
    private float _nextPlayerObservationDebugLogAt = float.NegativeInfinity;
    private AttackPattern _lastDebugSelectedPattern;
    private string _lastDebugSelectionSource = string.Empty;
    private float _phaseEntryPressureUntilTime = float.NegativeInfinity;
    private float _phaseTransitionLockUntilTime = float.NegativeInfinity;
    private float _pendingCombatIdleDuration = -1f;
    private float _punishWindowUntilTime = float.NegativeInfinity;
    private float _punishDamageMultiplier = 1f;
    private string _punishSourcePattern = string.Empty;
    private bool _cachedAttackHitboxDefaults;
    private bool _defaultAttackHitboxUseOneShotWindow;
    private float _defaultAttackHitboxExpandedPadding;
    private float _defaultAttackHitboxMeshPaddingScale;
    private float _defaultAttackHitboxScanInterval;
    private float _defaultAttackHitboxOneShotWindow;
    private bool _isUltimateVictim;
    private bool _cachedUltimateVictimAnimatorState;
    private AnimatorUpdateMode _ultimateVictimAnimatorUpdateMode = AnimatorUpdateMode.Normal;
    private bool _ultimateVictimAnimatorApplyRootMotion;
    private float _ultimateVictimAnimatorSpeed = 1f;
    private bool _ultimateVictimRigidbodyWasKinematic;
    private RigidbodyConstraints _ultimateVictimRigidbodyConstraints;
    private Vector3 _ultimateVictimOriginalPosition;
    private Quaternion _ultimateVictimOriginalRotation = Quaternion.identity;
    private bool _hasUltimateVictimOriginalPose;
    private Transform _ultimateVictimVisualRoot;
    private Vector3 _ultimateVictimVisualLocalPosition;
    private Quaternion _ultimateVictimVisualLocalRotation = Quaternion.identity;
    private Vector3 _ultimateVictimVisualLocalScale = Vector3.one;
    private float _ultimateVictimVisualGroundOffsetY;
    private bool _hasUltimateVictimVisualPose;
    private Vector3 _ultimateVictimAnchorPosition;
    private Quaternion _ultimateVictimAnchorRotation = Quaternion.identity;
    private bool _hasUltimateVictimAnchor;
    private bool _delayDestroyUntilUltimateVictimEnds;
    private bool _isExecutingPostAction;
    private BossPatternPostActionType _lastExecutedPostActionType = BossPatternPostActionType.None;
    private int _consecutiveMovementPostActionCount;
    private BossPostActionExecutor _postActionExecutor = new BossPostActionExecutor();
    private BossAttackTimingController _attackTimingController = new BossAttackTimingController();
    private Coroutine _attackTimingRoutine;
    private bool _timingDataControlsHitbox;
    private bool _attackTimingParryWindowOpen;
    private bool _attackTimingControlsParryWindow;
    private bool _attackTimingCachedCanParry;
    private bool _attackTimingHasCachedCanParry;
    private BossAttackTimingData _activeAttackTimingData;
    private bool _hasActiveAttackTimingData;
    private string _attackTimingTelegraphLabel = string.Empty;
    private CameraShake _attackTimingCueCameraShake;
    private float _nextAttackTimingCueShakeRealtime;
    private float _nextAttackTimingWarningRealtime;
    private bool _engageSucceeded;
    private AttackPattern _engageResolvedPattern;
    private bool _isEngaging;
    private BossCombatRecoveryController _combatRecoveryController = new BossCombatRecoveryController();
    private Coroutine _combatRecoveryRoutine;
    private Vector3 _combatRecoveryAnchorPosition;
    private bool _combatRecoverySuperArmorActive;
    private float _combatRecoveryLockoutUntil = float.NegativeInfinity;
    private BossRecoveryReason _activeCombatRecoveryReason = BossRecoveryReason.Generic;
    private float _activeCombatRecoveryStartedAt = float.NegativeInfinity;
    private BossArenaAwareness _arenaAwareness = new BossArenaAwareness();
    private PlayerReferences _cachedGameplayPlayerReferences;
    private Transform _cachedGameplayPlayerTarget;
    private int _lastGameplayPlayerTargetResolveFrame = -1;
    private int _parryStunTriggerHash;
    private int _parryStunStateHash;
    private bool _hasParryStunTrigger;
    private bool _hasQuickshiftBTrigger;
    private bool _parryStunUsedAnimatorFreezeFallback;
    private float _parryStunCachedAnimatorSpeed = 1f;

    [Header("공격 전 회전 보정 (한글 설명)")]
    [Tooltip("Attack/백스텝 시작 전에 플레이어 방향으로 회전 보정을 할지 여부")]
    public bool snapRotationToPlayerOnAttack = true;

    [Tooltip("공격 전 회전 보정 시간(초). 0이면 한 프레임 안에 바로 스냅 회전")]
    public float preAttackRotateTime = 0.0f;

    [Tooltip("공격 전 회전 보정에 사용할 회전 속도(도/초). preAttackRotateTime > 0일 때 사용")]
    public float preAttackRotateSpeed = 720f;

    [Header("근접 회피(백스텝) 설정 (한글 설명)")]
    [Tooltip("플레이어와 너무 가까울 때 백스텝 애니메이션을 사용할지 여부")]
    public bool useBackstepWhenTooClose = true;

    [Tooltip("이 거리보다 가까워지면 백스텝을 시도 (m)")]
    public float backstepTriggerDistance = 1.0f;

    [Tooltip("지정 거리 안에서 이 시간 이상 압박을 받아야 백스텝을 허용합니다.")]
    [SerializeField] private float backstepPressureHoldTime = 0.12f;

    [Tooltip("백스텝 판단 거리를 공격 진입 거리보다 더 안쪽으로 좁히는 비율.")]
    [Range(0.1f, 1f)] [SerializeField] private float backstepInnerDistanceRatio = 0.72f;

    [Tooltip("플레이어가 보스 정면에 어느 정도 들어와 있을 때만 백스텝을 허용합니다.")]
    [Range(-1f, 1f)] [SerializeField] private float backstepFrontArcDot = 0.15f;

    [Tooltip("뒤로 빠질 공간이 이 비율보다 적으면 백스텝 대신 공격을 유지합니다.")]
    [Range(0f, 1f)] [SerializeField] private float minimumBackstepClearanceRatio = 0.45f;

    [Tooltip("백스텝 애니메이션 트리거 이름 (Animator 트리거 파라미터와 동일하게 설정)")]
    public string backstepAnimTriggerName = "Quickshift_B";

    [Tooltip("백스텝 중 뒤로 빠지는 시간(초). 루트 모션 사용 시 0으로 두고 애니메이션만 재생 가능")]
    public float backstepDuration = 0.6f;

    [Tooltip("백스텝 중 뒤로 빠지는 속도(m/s). 루트 모션을 쓴다면 0으로 두는 것을 권장")]
    public float backstepSpeed = 4.5f;

    [Tooltip("백스텝 재사용 쿨타임(초). 너무 자주 쓰지 않도록 제한")]
    public float backstepCooldown = 3.0f;

    private float _backstepCooldownTimer;
    private float _closePressureStartedAt = float.NegativeInfinity;
    private float _combatIdleStrafeSign = -1f;
    private float _combatIdleStrafeSideUntil = float.NegativeInfinity;
    private AttackPattern _pendingImmediateAttackPattern;
    private float _nextDirectSwordWaveAt;
    private float _directSwordWaveReadySince = float.NegativeInfinity;
    private float _noPatternFallbackReadySince = float.NegativeInfinity;
    private float _attackSelectionBlockedUntil = float.NegativeInfinity;
    readonly RaycastHit[] _movementSweepHits = new RaycastHit[16];
    readonly List<AttackPattern> _patternCandidatesCache = new List<AttackPattern>(16);
    readonly List<AttackPattern> _followUpCandidatesCache = new List<AttackPattern>(16);
    readonly Dictionary<string, AttackPattern> _patternLookup = new Dictionary<string, AttackPattern>(16, StringComparer.OrdinalIgnoreCase);
    readonly string[] _recentPatternNames = new string[4];
    readonly AttackTelegraphType[] _recentTelegraphTypes = new AttackTelegraphType[4];
    float[] _selectionWeightCache = new float[16];
    BossPatternData[] _selectorPatternData = new BossPatternData[0];
    BossPatternRuntimeState[] _selectorRuntimeStates = new BossPatternRuntimeState[0];
    AttackPattern[] _selectorPatternLookup = new AttackPattern[0];
    int _recentPatternWriteIndex;
    int _recentPatternRecordedCount;
    int _lastPatternCooldownSyncFrame = -1;
    BossReferences _bossReferences;

    [Header("사망시 오브젝트 정리 (한글 설명)")]
    [Tooltip("보스 사망 시 자동으로 오브젝트를 파괴할지 여부")]
    public bool autoDestroyOnDead = true;

    [Tooltip("사망 애니메이션 연출 후 실제 삭제까지 대기 시간(초)")]
    public float destroyDelayAfterDead = 1.0f;

    [Tooltip("비워두면 transform.root를 기준으로 파괴, 지정하면 해당 Transform 기준으로 전체 제거")]
    public Transform rootToDestroyOnDead;

    [Tooltip("보스 사망 시 함께 파괴할 추가 오브젝트들 (HP바, 락온 피벗, 사운드 오브젝트 등)")]
    public Transform[] extraObjectsToDestroyOnDead;

    private Coroutine _destroyRoutine;
    [SerializeField] private int maxFollowUpChainCount = 1;

    public int CurrentPhase => _currentPhase;
    public float BossHpNormalized => GetBossHpNormalized();
    public bool IsPunishWindowActive => Time.time < _punishWindowUntilTime;
    public float CurrentPunishDamageMultiplier => IsPunishWindowActive ? Mathf.Max(1f, _punishDamageMultiplier) : 1f;
    public float PunishWindowRemaining => IsPunishWindowActive ? Mathf.Max(0f, _punishWindowUntilTime - Time.time) : 0f;
    public string PunishSourcePattern => _punishSourcePattern;
    public bool IsAttackTimingParryWindowOpen => _attackTimingParryWindowOpen;
    public bool IsInUltimateVictimState => _isUltimateVictim;
    public string CurrentPatternName => _currentPattern != null ? _currentPattern.patternName : string.Empty;
    public string CurrentPatternTriggerName => _currentPattern != null ? _currentPattern.animTriggerName : string.Empty;
    public AttackTelegraphType CurrentPatternTelegraphType => _currentPattern != null ? _currentPattern.ResolveTelegraphType() : AttackTelegraphType.Auto;
    public bool CanProcessAttackAnimationEvents => !_isDead && !_isUltimateVictim && !_isExecutingPostAction && currentState == BossState.Attack && _currentPattern != null;
    public bool CanProcessAttackHitboxAnimationEvents => CanProcessAttackAnimationEvents && !_timingDataControlsHitbox;
    public bool IsTimingDataControllingHitbox => _timingDataControlsHitbox;
    public BossPlayerCombatObservation CurrentPlayerObservation => _playerObservation;
    public bool IsCombatRecoveryActive => _combatRecoveryController != null && _combatRecoveryController.IsRunning;
    public bool IsCombatRecoverySuperArmorActive => _combatRecoverySuperArmorActive ||
                                                   (_combatRecoveryController != null && _combatRecoveryController.IsSuperArmorActive(Time.time));
    public bool IsCombatRecoveryLockoutActive => IsCombatRecoveryActive || Time.time < _combatRecoveryLockoutUntil;
    public bool KeepDamageRewardDuringCombatRecoverySuperArmor => combatRecoverySettings.keepDamageRewardDuringSuperArmor;

    void Awake()
    {
        AssignDefaultPreAttackPoseClipIfNeeded();
        _bossReferences = GetComponent<BossReferences>();
        if (bossAnimator == null && _bossReferences != null && _bossReferences.MainAnimator != null)
            bossAnimator = _bossReferences.MainAnimator;
        if (attackHitbox == null && _bossReferences != null && _bossReferences.AttackHitbox != null)
            attackHitbox = _bossReferences.AttackHitbox;
        if (patternVisuals == null && _bossReferences != null && _bossReferences.PatternVisuals != null)
            patternVisuals = _bossReferences.PatternVisuals;
        if (telegraphVfxPresenter == null)
            telegraphVfxPresenter = GetComponentInChildren<BossTelegraphVfxPresenter>(true);
        if (attackFeedbackPresenter == null)
            attackFeedbackPresenter = GetComponentInChildren<BossAttackFeedbackPresenter>(true);
        if (attackVfxPresenter == null)
            attackVfxPresenter = GetComponentInChildren<BossAttackVfxPresenter>(true);
        if (groundTelegraph == null)
            groundTelegraph = GetComponentInChildren<BossGroundTelegraph>(true);
        if (groundTelegraph == null)
            groundTelegraph = CreateRuntimeGroundTelegraph();

        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider>();
        if (bossAnimator == null)
            bossAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        if (attackTimingCueAudioSource == null)
            attackTimingCueAudioSource = GetComponent<AudioSource>();
        if (attackHitbox != null)
            attackHitbox.HitApplied += HandleAttackHitboxHitApplied;
        _combatRecoveryController ??= new BossCombatRecoveryController();
        _arenaAwareness ??= new BossArenaAwareness();
        _combatRecoveryAnchorPosition = transform.position;
        CacheParryStunAnimatorHooks();
        _preAttackPoseClipSampler ??= new UltimateAnimatorClipSampler("BossPreAttackPoseSampler");

        if (attackHitbox != null)
        {
            CacheAttackHitboxDefaults();
            attackHitbox.DeactivateWindow();
        }

        if (bossHealth != null)
        {
            bossHealth.OnDied += OnBossDied;
            bossHealth.OnHPChanged += HandleBossHpChanged;
            bossHealth.OnStagger += HandleBossStagger;
        }

        if (breakController != null)
        {
            breakController.OnBreakEnter.AddListener(OnBreakEnter);
            breakController.OnBreakExit.AddListener(OnBreakExit);
        }

        EnsureDefaultSwordWavePattern();
        RebuildPatternLookup();
        RebuildPatternSelectorCache();
        RefreshPatternCooldownState();
        EnsureGameplayPlayerTarget(forceRefresh: true);
    }

    void OnValidate()
    {
        AssignDefaultPreAttackPoseClipIfNeeded();
        EnsureDefaultSwordWavePattern();
        RebuildPatternLookup();
        RebuildPatternSelectorCache();
    }

    void EnsureDefaultSwordWavePattern()
    {
        if (allPatterns == null)
            allPatterns = new List<AttackPattern>();

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern existing = allPatterns[i];
            if (existing == null)
                continue;

            if (existing.firesSwordWaveProjectile ||
                string.Equals(existing.patternName, "SwordWave", StringComparison.OrdinalIgnoreCase))
            {
                existing.patternName = "SwordWave";
                existing.animTriggerName = "Attack_F";
                existing.telegraphType = AttackTelegraphType.Dodge;
                existing.timingStyle = AttackTimingStyle.Delayed;
                existing.damageAmount = Mathf.Max(existing.damageAmount, 18);
                existing.cooldown = Mathf.Max(2.5f, existing.cooldown);
                existing.weight = Mathf.Max(2.25f, existing.weight);
                existing.minRange = 3.2f;
                existing.maxRange = Mathf.Max(18f, existing.maxRange);
                existing.canPerfectDodge = true;
                existing.canGuard = true;
                existing.isUnblockable = false;
                existing.causesGuardBreak = false;
                existing.recoveryTime = Mathf.Max(1.05f, existing.recoveryTime);
                existing.punishWindowDuration = Mathf.Max(0.95f, existing.punishWindowDuration);
                existing.punishDamageMultiplier = Mathf.Max(1.2f, existing.punishDamageMultiplier);
                existing.firesSwordWaveProjectile = true;
                existing.swordWaveFireDelay = 0.03f;
                existing.swordWaveSpeed = Mathf.Max(12.5f, existing.swordWaveSpeed);
                existing.swordWaveLifeTime = Mathf.Max(1.35f, existing.swordWaveLifeTime);
                existing.swordWaveWidth = Mathf.Max(1.1f, existing.swordWaveWidth);
                existing.swordWaveHeight = Mathf.Max(0.52f, existing.swordWaveHeight);
                existing.swordWaveLength = Mathf.Max(1.85f, existing.swordWaveLength);
                return;
            }
        }

        allPatterns.Add(new AttackPattern
        {
            patternName = "SwordWave",
            animTriggerName = "Attack_F",
            telegraphType = AttackTelegraphType.Dodge,
            timingStyle = AttackTimingStyle.Delayed,
            damageAmount = 18,
            cooldown = 3.4f,
            weight = 2.25f,
            minRange = 3.2f,
            maxRange = 18f,
            canPerfectDodge = true,
            canGuard = true,
            isUnblockable = false,
            causesGuardBreak = false,
            recoveryTime = 1.05f,
            punishWindowDuration = 0.95f,
            punishDamageMultiplier = 1.2f,
            firesSwordWaveProjectile = true,
            swordWaveFireDelay = 0.03f,
            swordWaveSpeed = 12.5f,
            swordWaveLifeTime = 1.35f,
            swordWaveWidth = 1.1f,
            swordWaveHeight = 0.52f,
            swordWaveLength = 1.85f
        });
    }

    void Start()
    {
        EnsureGameplayPlayerTarget(forceRefresh: true);
        RefreshPhaseState(true);
        SetState(BossState.IntroIdle);
    }

    void OnDestroy()
    {
        CleanupRuntimeCombatState();
        UnsubscribeBreakEvents();
        StopRunStopMotion();
        StopPreAttackPosePlayback();
        _preAttackPoseClipSampler?.Dispose();
        _preAttackPoseClipSampler = null;

        if (bossHealth != null)
        {
            bossHealth.OnDied -= OnBossDied;
            bossHealth.OnHPChanged -= HandleBossHpChanged;
            bossHealth.OnStagger -= HandleBossStagger;
        }

        if (attackHitbox != null)
            attackHitbox.HitApplied -= HandleAttackHitboxHitApplied;
    }

    void OnDisable()
    {
        CleanupRuntimeCombatState();
    }

    void CleanupRuntimeCombatState()
    {
        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        StopCombatRecovery();
        AbortAttackExecution(clearPunishWindow: true);
        if (_parryStunRoutine != null)
        {
            StopCoroutine(_parryStunRoutine);
            _parryStunRoutine = null;
        }

        ReleaseParryStunAnimationFallback();
        StopRunStopMotion();
        StopPreAttackPosePlayback();
        DeactivateHitbox();
    }

    void UnsubscribeBreakEvents()
    {
        if (breakController == null)
            return;

        breakController.OnBreakEnter.RemoveListener(OnBreakEnter);
        breakController.OnBreakExit.RemoveListener(OnBreakExit);
    }

    void Update()
    {
        if (_isUltimateVictim)
            return;

        UpdatePlayerStatePatternBias();
        // 패턴 쿨타임 감소
        

        // 백스텝 쿨타임 감소
    }

    void LateUpdate()
    {
        UpdatePreAttackPoseSampling();
        ApplyUltimateVictimAnchor();
    }

    void HandleBossHpChanged(int current, int max)
    {
        RefreshPhaseState(false);
    }

    void HandleBossStagger()
    {
        if (_isDead || _isUltimateVictim || IsCombatRecoveryLockoutActive)
            return;

        if (breakController != null && breakController.IsInBreak)
            return;

        TryStartCombatRecovery(BossRecoveryReason.Stagger, BossState.CombatIdle);
    }

    void RefreshPhaseState(bool silent)
    {
        int newPhase = ResolvePhaseFromHp(GetBossHpNormalized());
        if (newPhase == _currentPhase)
            return;

        _currentPhase = newPhase;
        _phaseEntryPressureUntilTime = Time.time + Mathf.Max(0.5f, phaseEntryPressureDuration);
        _phaseTransitionLockUntilTime = Time.time + Mathf.Max(0f, phaseTransitionAttackLockDuration);
        bool interruptActiveAttack = !silent && (currentState == BossState.Attack || _isExecutingPostAction);
        if (interruptActiveAttack)
        {
            AbortAttackExecution(clearPunishWindow: true);
        }
        else
        {
            ClearQueuedFollowUp();
            StopAttackTimingController();
            DeactivateHitbox();
        }

        if (silent)
            return;

        if (patternVisuals != null)
            patternVisuals.StartVisualCue(newPhase >= 3 ? AttackTelegraphType.Danger : AttackTelegraphType.Dodge, 0.28f);

        OnBossPhaseChanged?.Invoke(_currentPhase, GetBossHpNormalized());

        if (!_isDead && currentState != BossState.Dead && !_isUltimateVictim)
            TryStartCombatRecovery(BossRecoveryReason.PhaseTransition, BossState.CombatIdle);
    }

    float GetBossHpNormalized()
    {
        if (bossHealth == null || bossHealth.MaxHP <= 0)
            return 1f;

        return Mathf.Clamp01((float)bossHealth.CurrentHP / bossHealth.MaxHP);
    }

    BossDifficultyProfile ResolveDifficultyProfile()
    {
        if (difficultyProfiles != null)
        {
            for (int i = 0; i < difficultyProfiles.Length; i++)
            {
                if (difficultyProfiles[i].tier == difficultyTier)
                    return difficultyProfiles[i];
            }
        }

        return BossDifficultyProfile.CreateDefault(difficultyTier);
    }

    float ResolveDifficultyMultiplier(float value, float fallback, float min, float max)
    {
        float resolved = value > 0.001f ? value : fallback;
        return Mathf.Clamp(resolved, min, max);
    }

    float ResolveDifficultyDamageMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.damageMultiplier, defaults.damageMultiplier, 0.01f, 5f);
    }

    float ResolveDifficultyCooldownMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.cooldownMultiplier, defaults.cooldownMultiplier, 0.05f, 5f);
    }

    float ResolveDifficultyTelegraphDurationMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.telegraphDurationMultiplier, defaults.telegraphDurationMultiplier, 0.25f, 3f);
    }

    float ResolveDifficultyHitboxActiveDurationMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.hitboxActiveDurationMultiplier, defaults.hitboxActiveDurationMultiplier, 0.25f, 2f);
    }

    float ResolveDifficultyParryWindowMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.parryWindowMultiplier, defaults.parryWindowMultiplier, 0.25f, 3f);
    }

    float ResolveDifficultyPatternWeightMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.patternWeightMultiplier, defaults.patternWeightMultiplier, 0.05f, 5f);
    }

    float ResolveDifficultyEngageSpeedMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.engageSpeedMultiplier, defaults.engageSpeedMultiplier, 0.15f, 3f);
    }

    float ResolveDifficultyPostActionDurationMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        return ResolveDifficultyMultiplier(profile.postActionDurationMultiplier, defaults.postActionDurationMultiplier, 0.15f, 3f);
    }

    float ResolveDifficultyReactionDelay()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        float fallback = Mathf.Max(0f, defaults.reactionDelay);
        float configured = profile.reactionDelay >= 0f ? profile.reactionDelay : fallback;
        return Mathf.Clamp(configured, 0f, 1f);
    }

    float ResolveDifficultyPhaseTwoThreshold()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = BossDifficultyProfile.CreateDefault(difficultyTier);
        float fallback = defaults.phaseTransitionHpThreshold > 0.001f
            ? defaults.phaseTransitionHpThreshold
            : phaseTwoThresholdNormalized;
        float threshold = profile.phaseTransitionHpThreshold > 0.001f
            ? profile.phaseTransitionHpThreshold
            : fallback;
        return Mathf.Clamp(threshold, 0.05f, 0.95f);
    }

    float ResolveDifficultyPhaseThreeThreshold(float phaseTwoThreshold)
    {
        float basePhaseTwo = Mathf.Max(0.001f, phaseTwoThresholdNormalized);
        float baseRatio = Mathf.Clamp01(phaseThreeThresholdNormalized / basePhaseTwo);
        float threshold = phaseTwoThreshold * baseRatio;
        return Mathf.Clamp(threshold, 0.01f, Mathf.Max(0.01f, phaseTwoThreshold - 0.05f));
    }

    int ResolveDifficultyMaxFollowUpChains(int activeMax)
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        if (!profile.overrideMaxFollowUpCount)
            return Mathf.Max(0, activeMax);

        return Mathf.Max(0, profile.maxFollowUpCount);
    }

    int ResolvePhaseFromHp(float hpNormalized)
    {
        float phaseTwoThreshold = ResolveDifficultyPhaseTwoThreshold();
        float phaseThreeThreshold = ResolveDifficultyPhaseThreeThreshold(phaseTwoThreshold);

        if (hpNormalized <= phaseThreeThreshold)
            return 3;

        if (hpNormalized <= phaseTwoThreshold)
            return 2;

        return 1;
    }

    float ResolvePhaseTelegraphScale()
    {
        switch (_currentPhase)
        {
            case 2:
                return Mathf.Clamp(phaseTwoTelegraphScale, 0.6f, 1.1f);
            case 3:
                return Mathf.Clamp(phaseThreeTelegraphScale, 0.5f, 1.05f);
            default:
                return 1f;
        }
    }

    float ResolveMinimumTelegraphLeadTime(AttackPattern pattern, bool isQueuedFollowUp)
    {
        if (pattern == null)
            return 0.08f;

        float minimum;
        switch (pattern.ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                minimum = minimumParryTelegraphLeadTime;
                break;
            case AttackTelegraphType.Guard:
                minimum = minimumGuardTelegraphLeadTime;
                break;
            case AttackTelegraphType.Danger:
                minimum = minimumDangerTelegraphLeadTime;
                break;
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Auto:
            default:
                minimum = minimumDodgeTelegraphLeadTime;
                break;
        }

        if (pattern.ResolveTimingStyle() == AttackTimingStyle.FakeOut)
            minimum = Mathf.Max(minimum, 0.24f);
        else if (pattern.ResolveTimingStyle() == AttackTimingStyle.Delayed)
            minimum = Mathf.Max(minimum, 0.18f);

        if (isQueuedFollowUp)
            minimum = Mathf.Max(0.12f, minimum * 0.82f);

        return Mathf.Max(0.08f, minimum);
    }

    float ResolvePhaseFollowUpChanceBonus()
    {
        switch (_currentPhase)
        {
            case 2:
                return Mathf.Max(0f, phaseTwoFollowUpChanceBonus);
            case 3:
                return Mathf.Max(0f, phaseThreeFollowUpChanceBonus);
            default:
                return 0f;
        }
    }

    float ResolveFollowUpChance(AttackPattern sourcePattern)
    {
        if (sourcePattern == null)
            return 0f;

        float chance = Mathf.Clamp01(sourcePattern.ResolveFollowUpChance() + ResolvePhaseFollowUpChanceBonus());
        if (TryResolvePhasePatternModifier(sourcePattern, out BossPhasePatternModifier phaseModifier))
        {
            if (phaseModifier.overrideFollowUp && !phaseModifier.allowFollowUp)
                return 0f;

            if (phaseModifier.allowFollowUp)
                chance = Mathf.Max(chance, 0.35f);
        }

        if (chance <= 0.001f)
            return 0f;

        AttackTelegraphType sourceType = sourcePattern.ResolveTelegraphType();
        if (sourceType == AttackTelegraphType.Danger)
            chance *= dangerFollowUpChanceMultiplier;

        if (sourcePattern.ResolveTimingStyle() == AttackTimingStyle.FakeOut)
            chance *= fakeOutFollowUpChanceMultiplier;

        if (sourcePattern.ResolvePunishDamageMultiplier() >= 1.20f || sourcePattern.ResolvePunishWindowDuration() >= 0.60f)
            chance *= punishHeavyFollowUpChanceMultiplier;

        if (_followUpChainDepth > 0)
            chance *= 0.62f;

        return Mathf.Clamp01(chance);
    }

    int ResolveActiveMaxFollowUpChains(AttackPattern sourcePattern = null)
    {
        int activeMax = Mathf.Max(0, maxFollowUpChainCount);
        if (_currentPhase >= 3)
            activeMax += Mathf.Max(0, phaseThreeExtraFollowUpChains);

        if (sourcePattern != null &&
            TryResolvePhasePatternModifier(sourcePattern, out BossPhasePatternModifier phaseModifier) &&
            phaseModifier.maxFollowUpCount > 0)
        {
            activeMax = phaseModifier.maxFollowUpCount;
        }

        return ResolveDifficultyMaxFollowUpChains(activeMax);
    }

    float ResolvePhasePatternWeightMultiplier(AttackPattern pattern, bool isFollowUp)
    {
        if (pattern == null)
            return 1f;

        AttackTelegraphType type = pattern.ResolveTelegraphType();
        float multiplier = 1f;

        switch (_currentPhase)
        {
            case 2:
                switch (type)
                {
                    case AttackTelegraphType.Dodge:
                        multiplier = 1.32f;
                        break;
                    case AttackTelegraphType.Guard:
                        multiplier = 1.10f;
                        break;
                    case AttackTelegraphType.Parry:
                        multiplier = 0.96f;
                        break;
                    case AttackTelegraphType.Danger:
                        multiplier = 0.95f;
                        break;
                }
                break;

            case 3:
                switch (type)
                {
                    case AttackTelegraphType.Danger:
                        multiplier = 1.34f;
                        break;
                    case AttackTelegraphType.Dodge:
                        multiplier = 1.18f;
                        break;
                    case AttackTelegraphType.Guard:
                        multiplier = 1.08f;
                        break;
                    case AttackTelegraphType.Parry:
                        multiplier = 0.84f;
                        break;
                }
                break;
        }

        if (isFollowUp && _currentPhase >= 2 && (type == AttackTelegraphType.Dodge || type == AttackTelegraphType.Danger))
            multiplier *= 1.10f;

        if (Time.time < _phaseEntryPressureUntilTime && pattern.ResolveMinPhase() == _currentPhase)
            multiplier *= Mathf.Max(1f, phaseEntryUnlockedPatternWeight);

        if (TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier phaseModifier))
            multiplier *= BossPhasePatternModifier.ResolveMultiplier(phaseModifier.baseWeightMultiplier);

        multiplier *= ResolvePlayerStatePatternWeightMultiplier(pattern);
        multiplier *= ResolvePatternVarietyWeightMultiplier(pattern, isFollowUp);

        return multiplier;
    }

    float ResolvePatternVarietyWeightMultiplier(AttackPattern pattern, bool isFollowUp)
    {
        if (pattern == null)
            return 1f;

        float multiplier = 1f;
        if (IsImmediateRepeatPattern(pattern.patternName))
            multiplier *= immediateRepeatPatternWeightMultiplier;
        else if (WasPatternUsedRecently(pattern.patternName, 3))
            multiplier *= recentRepeatPatternWeightMultiplier;

        if (CountRecentTelegraphMatches(pattern.ResolveTelegraphType(), 2) >= 2)
            multiplier *= repeatedTelegraphWeightMultiplier;

        if (isFollowUp && IsImmediateRepeatPattern(pattern.patternName))
            multiplier *= 0.85f;

        return Mathf.Max(0.05f, multiplier);
    }

    void UpdatePlayerStatePatternBias()
    {
        if (!usePlayerStatePatternBias ||
            _isDead ||
            currentState == BossState.Dead ||
            playerTarget == null ||
            !IsValidGameplayPlayerTarget(playerTarget))
        {
            ResetPlayerCombatObservationState();
            _farPressureStartedAt = float.NegativeInfinity;
            _closeLingerStartedAt = float.NegativeInfinity;
            return;
        }

        RefreshPlayerCombatObservation();
        ApplyPlayerReactionDelay(_playerObservation, Time.time);
        BossPlayerCombatObservation observation = _playerObservation;
        float now = Time.time;

        if (observation.playerDistance >= farPressureDistance || observation.playerIsMovingAway)
        {
            if (float.IsNegativeInfinity(_farPressureStartedAt))
                _farPressureStartedAt = now;
        }
        else
        {
            _farPressureStartedAt = float.NegativeInfinity;
        }

        if (observation.playerDistance <= closeLingerDistance)
        {
            if (float.IsNegativeInfinity(_closeLingerStartedAt))
                _closeLingerStartedAt = now;
        }
        else
        {
            _closeLingerStartedAt = float.NegativeInfinity;
        }

        LogPlayerCombatObservation(now);
    }

    void RefreshPlayerCombatObservation()
    {
        if (playerTarget == null || !IsValidGameplayPlayerTarget(playerTarget))
        {
            ResetPlayerCombatObservationState();
            return;
        }

        float now = Time.time;
        Vector3 bossPos = transform.position;
        Vector3 playerPos = playerTarget.position;
        Vector3 toPlayer = playerPos - bossPos;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        float angle = 180f;
        if (toPlayer.sqrMagnitude > 0.0001f)
            angle = Mathf.Abs(Vector3.SignedAngle(transform.forward, toPlayer.normalized, Vector3.up));

        bool movingAway = false;
        bool approaching = false;
        if (_lastObservedPlayerTarget == playerTarget && !float.IsNegativeInfinity(_lastPlayerObservationTime))
        {
            float deltaTime = Mathf.Max(0.0001f, now - _lastPlayerObservationTime);
            Vector3 displacement = playerPos - _lastObservedPlayerPosition;
            displacement.y = 0f;
            Vector3 velocity = displacement / deltaTime;
            float speedThreshold = Mathf.Max(0.01f, playerObservationMoveSpeedThreshold);
            if (velocity.sqrMagnitude >= speedThreshold * speedThreshold && toPlayer.sqrMagnitude > 0.0001f)
            {
                Vector3 awayFromBoss = toPlayer.normalized;
                float radialSpeed = Vector3.Dot(velocity, awayFromBoss);
                movingAway = radialSpeed >= speedThreshold;
                approaching = radialSpeed <= -speedThreshold;
            }
        }

        ICombatStateReader combatState = ResolvePlayerCombatStateReader(playerTarget);
        PlayerHealth playerHealth = ResolvePlayerHealth(playerTarget);
        bool playerIsAttacking = combatState != null && combatState.IsAttacking();
        bool playerIsDodging = combatState != null && combatState.IsDodging();
        bool playerIsGuarding = combatState != null && combatState.IsGuarding();
        bool playerIsStunned = combatState != null && (combatState.IsStaggered() || combatState.IsInHitState());
        bool playerIsDowned = playerHealth != null && playerHealth.IsDead;

        if (playerIsAttacking)
            _lastPlayerAttackAt = now;

        float memoryTime = Mathf.Max(0.1f, playerDefenseResponseMemoryTime);
        _playerObservation = new BossPlayerCombatObservation
        {
            playerDistance = distance,
            playerAngle = angle,
            playerIsMovingAway = movingAway,
            playerIsApproaching = approaching,
            playerIsDodging = playerIsDodging,
            playerIsGuarding = playerIsGuarding,
            playerIsAttacking = playerIsAttacking,
            playerIsStunned = playerIsStunned,
            playerIsDowned = playerIsDowned,
            playerRecentlyParried = now - _lastParryResponseAt <= memoryTime,
            playerRecentlyDodged = now - _lastPerfectDodgeResponseAt <= memoryTime,
            playerRecentlyHitBoss = now - _lastPlayerHitBossAt <= memoryTime,
            timeSincePlayerLastAttack = ResolveElapsed(now, _lastPlayerAttackAt),
            timeSinceBossLastHitPlayer = ResolveElapsed(now, _lastBossHitPlayerAt)
        };

        _lastObservedPlayerTarget = playerTarget;
        _lastObservedPlayerPosition = playerPos;
        _lastPlayerObservationTime = now;
    }

    void ResetPlayerCombatObservationState()
    {
        _playerObservation = BossPlayerCombatObservation.CreateInvalid();
        _effectivePlayerObservation = BossPlayerCombatObservation.CreateInvalid();
        _pendingPlayerReactionObservation = BossPlayerCombatObservation.CreateInvalid();
        _hasPendingPlayerReactionObservation = false;
        _pendingPlayerReactionReadyAt = float.NegativeInfinity;
    }

    void ApplyPlayerReactionDelay(BossPlayerCombatObservation rawObservation, float now)
    {
        float reactionDelay = Mathf.Max(0f, ResolveDifficultyReactionDelay());
        if (reactionDelay <= 0f || float.IsInfinity(rawObservation.playerDistance))
        {
            _effectivePlayerObservation = rawObservation;
            _playerObservation = rawObservation;
            _hasPendingPlayerReactionObservation = false;
            _pendingPlayerReactionReadyAt = float.NegativeInfinity;
            return;
        }

        BossPlayerCombatObservation effective = _effectivePlayerObservation;
        if (float.IsInfinity(effective.playerDistance))
            effective = rawObservation;

        // Distance and movement pressure stay current. Reactive tells are delayed to avoid perfect-read AI.
        effective.playerDistance = rawObservation.playerDistance;
        effective.playerAngle = rawObservation.playerAngle;
        effective.playerIsMovingAway = rawObservation.playerIsMovingAway;
        effective.playerIsApproaching = rawObservation.playerIsApproaching;
        effective.playerIsStunned = rawObservation.playerIsStunned;
        effective.playerIsDowned = rawObservation.playerIsDowned;
        effective.timeSinceBossLastHitPlayer = rawObservation.timeSinceBossLastHitPlayer;

        if (HasReactiveObservationChanged(rawObservation, effective))
        {
            if (!_hasPendingPlayerReactionObservation)
            {
                _pendingPlayerReactionObservation = rawObservation;
                _pendingPlayerReactionReadyAt = now + reactionDelay;
                _hasPendingPlayerReactionObservation = true;
            }
            else
            {
                MergePendingReactiveObservation(rawObservation);
            }
        }
        else
        {
            _hasPendingPlayerReactionObservation = false;
            _pendingPlayerReactionReadyAt = float.NegativeInfinity;
        }

        if (_hasPendingPlayerReactionObservation && now >= _pendingPlayerReactionReadyAt)
        {
            CopyReactiveObservation(ref effective, _pendingPlayerReactionObservation);
            _hasPendingPlayerReactionObservation = false;
            _pendingPlayerReactionReadyAt = float.NegativeInfinity;
        }

        _effectivePlayerObservation = effective;
        _playerObservation = effective;
    }

    static bool HasReactiveObservationChanged(BossPlayerCombatObservation source, BossPlayerCombatObservation target)
    {
        return source.playerIsDodging != target.playerIsDodging ||
               source.playerIsGuarding != target.playerIsGuarding ||
               source.playerIsAttacking != target.playerIsAttacking ||
               source.playerRecentlyParried != target.playerRecentlyParried ||
               source.playerRecentlyDodged != target.playerRecentlyDodged ||
               source.playerRecentlyHitBoss != target.playerRecentlyHitBoss;
    }

    static void CopyReactiveObservation(ref BossPlayerCombatObservation target, BossPlayerCombatObservation source)
    {
        target.playerIsDodging = source.playerIsDodging;
        target.playerIsGuarding = source.playerIsGuarding;
        target.playerIsAttacking = source.playerIsAttacking;
        target.playerRecentlyParried = source.playerRecentlyParried;
        target.playerRecentlyDodged = source.playerRecentlyDodged;
        target.playerRecentlyHitBoss = source.playerRecentlyHitBoss;
        target.timeSincePlayerLastAttack = source.timeSincePlayerLastAttack;
    }

    void MergePendingReactiveObservation(BossPlayerCombatObservation source)
    {
        _pendingPlayerReactionObservation.playerIsDodging |= source.playerIsDodging;
        _pendingPlayerReactionObservation.playerIsGuarding |= source.playerIsGuarding;
        _pendingPlayerReactionObservation.playerIsAttacking |= source.playerIsAttacking;
        _pendingPlayerReactionObservation.playerRecentlyParried |= source.playerRecentlyParried;
        _pendingPlayerReactionObservation.playerRecentlyDodged |= source.playerRecentlyDodged;
        _pendingPlayerReactionObservation.playerRecentlyHitBoss |= source.playerRecentlyHitBoss;
        if (source.timeSincePlayerLastAttack < _pendingPlayerReactionObservation.timeSincePlayerLastAttack)
            _pendingPlayerReactionObservation.timeSincePlayerLastAttack = source.timeSincePlayerLastAttack;
    }

    ICombatStateReader ResolvePlayerCombatStateReader(Transform target)
    {
        if (target == null)
            return null;

        if (_cachedPlayerCombatStateReader is Component cachedComponent &&
            cachedComponent != null &&
            cachedComponent.transform != null &&
            cachedComponent.transform.IsChildOf(target.root))
        {
            return _cachedPlayerCombatStateReader;
        }

        _cachedPlayerCombatStateReader = target.GetComponentInParent<ICombatStateReader>();
        if (_cachedPlayerCombatStateReader == null)
            _cachedPlayerCombatStateReader = target.GetComponentInChildren<ICombatStateReader>();

        return _cachedPlayerCombatStateReader;
    }

    PlayerHealth ResolvePlayerHealth(Transform target)
    {
        if (target == null)
            return null;

        if (_cachedPlayerHealth != null &&
            _cachedPlayerHealth.transform != null &&
            _cachedPlayerHealth.transform.IsChildOf(target.root))
        {
            return _cachedPlayerHealth;
        }

        _cachedPlayerHealth = target.GetComponentInParent<PlayerHealth>();
        if (_cachedPlayerHealth == null)
            _cachedPlayerHealth = target.GetComponentInChildren<PlayerHealth>();

        return _cachedPlayerHealth;
    }

    static float ResolveElapsed(float now, float timestamp)
    {
        return float.IsNegativeInfinity(timestamp) ? float.PositiveInfinity : Mathf.Max(0f, now - timestamp);
    }

    public void NotifyPlayerHitBoss(Transform attacker)
    {
        if (!usePlayerStatePatternBias)
            return;

        if (attacker != null && playerTarget != null && !attacker.IsChildOf(playerTarget.root))
            return;

        _lastPlayerHitBossAt = Time.time;
    }

    public void NotifyBossHitPlayer(Transform target)
    {
        if (!usePlayerStatePatternBias)
            return;

        if (target != null && playerTarget != null && !target.IsChildOf(playerTarget.root))
            return;

        _lastBossHitPlayerAt = Time.time;
    }

    void LogPlayerCombatObservation(float now)
    {
        if (!enablePlayerObservationDebugLog)
            return;

        if (now < _nextPlayerObservationDebugLogAt)
            return;

        _nextPlayerObservationDebugLogAt = now + Mathf.Max(0.1f, playerObservationDebugInterval);
        Debug.Log(
            "[BossPlayerObservation] phase=" + _currentPhase +
            " dist=" + _playerObservation.playerDistance.ToString("0.00") +
            " angle=" + _playerObservation.playerAngle.ToString("0") +
            " away=" + _playerObservation.playerIsMovingAway +
            " approach=" + _playerObservation.playerIsApproaching +
            " dodge=" + _playerObservation.playerIsDodging +
            " guard=" + _playerObservation.playerIsGuarding +
            " attack=" + _playerObservation.playerIsAttacking +
            " stun=" + _playerObservation.playerIsStunned +
            " down=" + _playerObservation.playerIsDowned +
            " recentParry=" + _playerObservation.playerRecentlyParried +
            " recentDodge=" + _playerObservation.playerRecentlyDodged +
            " recentHitBoss=" + _playerObservation.playerRecentlyHitBoss +
            " sincePlayerAttack=" + FormatObservationTime(_playerObservation.timeSincePlayerLastAttack) +
            " sinceBossHitPlayer=" + FormatObservationTime(_playerObservation.timeSinceBossLastHitPlayer),
            this);
    }

    void PublishPatternTelemetry(
        AttackPattern pattern,
        float distanceToPlayer,
        float angleToPlayer,
        bool isFollowUp,
        bool usedEngage,
        BossPatternEngageMode usedEngageMode,
        BossAttackTelegraphType timingTelegraphType)
    {
        if (pattern == null)
            return;

        BossPatternData patternData = BuildSelectorData(pattern);
        BossPatternTelemetrySample sample = new BossPatternTelemetrySample
        {
            patternId = patternData.patternId,
            postActionType = patternData.postActionType,
            engageMode = usedEngage ? usedEngageMode : patternData.engageMode,
            telegraphType = timingTelegraphType,
            phase = _currentPhase,
            bossHpNormalized = ResolveBossHpNormalized(),
            distanceToPlayer = float.IsNaN(distanceToPlayer) ? float.PositiveInfinity : Mathf.Max(0f, distanceToPlayer),
            angleToPlayer = float.IsNaN(angleToPlayer) ? 180f : Mathf.Clamp(Mathf.Abs(angleToPlayer), 0f, 180f),
            isFollowUp = isFollowUp,
            usedEngage = usedEngage,
            playerIsMovingAway = _playerObservation.playerIsMovingAway,
            playerIsApproaching = _playerObservation.playerIsApproaching,
            playerIsDodging = _playerObservation.playerIsDodging,
            playerIsGuarding = _playerObservation.playerIsGuarding,
            playerIsAttacking = _playerObservation.playerIsAttacking,
            playerRecentlyParried = _playerObservation.playerRecentlyParried,
            playerRecentlyDodged = _playerObservation.playerRecentlyDodged,
            playerRecentlyHitBoss = _playerObservation.playerRecentlyHitBoss,
            time = Time.time
        };

        OnPatternTelemetrySample?.Invoke(sample);

        if (!enablePatternTelemetryDebugLog)
            return;

        Debug.Log(
            "[BossPatternTelemetry] phase=" + sample.phase +
            " pattern=" + sample.patternId +
            " dist=" + FormatObservationTime(sample.distanceToPlayer) +
            " angle=" + sample.angleToPlayer.ToString("0") +
            " hp=" + sample.bossHpNormalized.ToString("0.00") +
            " followUp=" + sample.isFollowUp +
            " engage=" + sample.usedEngage +
            " away=" + sample.playerIsMovingAway +
            " approach=" + sample.playerIsApproaching +
            " dodge=" + sample.playerIsDodging +
            " guard=" + sample.playerIsGuarding +
            " attack=" + sample.playerIsAttacking +
            " engageMode=" + sample.engageMode +
            " post=" + sample.postActionType +
            " telegraph=" + sample.telegraphType,
            this);
    }

    float ResolveBossHpNormalized()
    {
        if (bossHealth == null || bossHealth.MaxHP <= 0)
            return 1f;

        return Mathf.Clamp01((float)bossHealth.CurrentHP / bossHealth.MaxHP);
    }

    static string FormatObservationTime(float value)
    {
        return float.IsInfinity(value) ? "inf" : value.ToString("0.00");
    }

    bool IsFarPressureActive()
    {
        return usePlayerStatePatternBias &&
               !float.IsNegativeInfinity(_farPressureStartedAt) &&
               Time.time - _farPressureStartedAt >= Mathf.Max(0f, farPressureDelay);
    }

    bool IsCloseLingerActive()
    {
        return usePlayerStatePatternBias &&
               !float.IsNegativeInfinity(_closeLingerStartedAt) &&
               Time.time - _closeLingerStartedAt >= Mathf.Max(0f, closeLingerDelay);
    }

    float ResolvePlayerStatePatternWeightMultiplier(AttackPattern pattern)
    {
        if (pattern == null)
            return 1f;

        BossPatternId patternId = pattern.selectorData.patternId != BossPatternId.None
            ? pattern.selectorData.patternId
            : ResolvePatternId(pattern);

        float multiplier = ResolvePlayerStatePatternWeightMultiplier(patternId);
        AttackTelegraphType telegraphType = pattern.ResolveTelegraphType();

        if (IsRepeatedParryResponseActive())
        {
            if (pattern.ResolveCanParry() || telegraphType == AttackTelegraphType.Parry)
                multiplier *= repeatedParryParryableWeightMultiplier;
            else if (telegraphType == AttackTelegraphType.Dodge || telegraphType == AttackTelegraphType.Danger)
                multiplier *= repeatedParryDodgeOnlyWeightMultiplier;
        }

        if (IsRepeatedPerfectDodgeResponseActive())
        {
            if (telegraphType == AttackTelegraphType.Dodge)
                multiplier *= repeatedPerfectDodgeDodgeOnlyWeightMultiplier;
            else if (pattern.ResolveCanParry() || telegraphType == AttackTelegraphType.Parry || telegraphType == AttackTelegraphType.Guard)
                multiplier *= repeatedPerfectDodgeParryableWeightMultiplier;
        }

        if (_playerObservation.playerIsDodging || _playerObservation.playerRecentlyDodged)
        {
            AttackTimingStyle timingStyle = pattern.ResolveTimingStyle();
            if (timingStyle == AttackTimingStyle.Delayed || timingStyle == AttackTimingStyle.FakeOut || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, playerDodgingDelayedAttackWeightMultiplier);
            else if (patternId == BossPatternId.QuickSlash)
                multiplier *= 0.88f;
        }

        if (_playerObservation.playerIsGuarding)
        {
            if (patternId == BossPatternId.HeavySlash || patternId == BossPatternId.BackstepSlash)
                multiplier *= Mathf.Max(1f, playerGuardingPunishWeightMultiplier);
            else if (pattern.ResolveCanParry() && telegraphType == AttackTelegraphType.Parry)
                multiplier *= 0.90f;
        }

        if (_playerObservation.playerIsAttacking)
        {
            if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.DashSlash)
                multiplier *= Mathf.Max(1f, playerAttackingCounterWeightMultiplier);
        }

        if (_playerObservation.playerRecentlyHitBoss || _playerObservation.playerIsApproaching)
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, playerRecentlyHitBossCounterWeightMultiplier);
            else if (patternId == BossPatternId.SwordWave && _playerObservation.playerDistance <= closeLingerDistance + 1.0f)
                multiplier *= 0.82f;
        }

        if (IsPlayerAtBackAngle())
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, playerBackAngleCounterWeightMultiplier);
            else if (patternId == BossPatternId.SwordWave)
                multiplier *= 0.76f;
        }

        return Mathf.Max(0.01f, multiplier);
    }

    float ResolvePlayerStatePatternWeightMultiplier(BossPatternId patternId)
    {
        if (!usePlayerStatePatternBias)
            return 1f;

        float multiplier = 1f;

        if (IsFarPressureActive())
        {
            if (patternId == BossPatternId.SwordWave)
                multiplier *= Mathf.Max(1f, farPressureRangedWeightMultiplier);
            else if (patternId == BossPatternId.DashSlash || patternId == BossPatternId.QuickSlash)
                multiplier *= Mathf.Max(1f, farPressureChaseWeightMultiplier);
            else if (patternId == BossPatternId.HeavySlash || patternId == BossPatternId.BackstepSlash)
                multiplier *= 0.82f;
        }

        if (IsCloseLingerActive())
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, closeLingerPunishWeightMultiplier);
            else if (patternId == BossPatternId.SwordWave)
                multiplier *= Mathf.Clamp(closeLingerRangedWeightMultiplier, 0.1f, 1f);
        }

        if (_playerObservation.playerIsStunned || _playerObservation.playerIsDowned)
        {
            if (patternId == BossPatternId.SwordWave)
                multiplier *= Mathf.Clamp(playerStunnedRangedWeightMultiplier, 0.1f, 1f);
            else if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= 1.10f;
        }

        if (_playerObservation.timeSinceBossLastHitPlayer >= bossMissedPressureDelay)
        {
            if (patternId == BossPatternId.SwordWave || patternId == BossPatternId.DashSlash)
                multiplier *= Mathf.Max(1f, bossMissedPressureWeightMultiplier);
        }

        return Mathf.Max(0.01f, multiplier);
    }

    float ResolvePlayerStateEngageSpeedMultiplier(BossPatternId patternId)
    {
        if (!IsFarPressureActive() && !_playerObservation.playerIsMovingAway)
            return 1f;

        return patternId == BossPatternId.QuickSlash || patternId == BossPatternId.DashSlash
            ? Mathf.Max(1f, farPressureEngageSpeedMultiplier)
            : 1f;
    }

    bool IsPlayerAtSideAngle()
    {
        return _playerObservation.playerAngle >= Mathf.Clamp(playerSideAngleThreshold, 0f, 180f);
    }

    bool IsPlayerAtBackAngle()
    {
        return _playerObservation.playerAngle >= Mathf.Clamp(playerBackAngleThreshold, 0f, 180f);
    }

    bool IsRepeatedParryResponseActive()
    {
        return usePlayerStatePatternBias &&
               _recentParryResponseCount >= repeatedParryResponseThreshold &&
               Time.time - _lastParryResponseAt <= playerDefenseResponseMemoryTime;
    }

    bool IsRepeatedPerfectDodgeResponseActive()
    {
        return usePlayerStatePatternBias &&
               _recentPerfectDodgeResponseCount >= repeatedPerfectDodgeResponseThreshold &&
               Time.time - _lastPerfectDodgeResponseAt <= playerDefenseResponseMemoryTime;
    }

    void RecordPlayerDefenseResponse(bool parry)
    {
        if (!usePlayerStatePatternBias)
            return;

        float now = Time.time;
        float memoryTime = Mathf.Max(0.1f, playerDefenseResponseMemoryTime);

        if (parry)
        {
            if (now - _lastParryResponseAt > memoryTime)
                _recentParryResponseCount = 0;

            _recentParryResponseCount = Mathf.Min(
                Mathf.Max(1, repeatedParryResponseThreshold),
                _recentParryResponseCount + 1);
            _lastParryResponseAt = now;
        }
        else
        {
            if (now - _lastPerfectDodgeResponseAt > memoryTime)
                _recentPerfectDodgeResponseCount = 0;

            _recentPerfectDodgeResponseCount = Mathf.Min(
                Mathf.Max(1, repeatedPerfectDodgeResponseThreshold),
                _recentPerfectDodgeResponseCount + 1);
            _lastPerfectDodgeResponseAt = now;
        }
    }

    int ResolveRecentPatternCapacity()
    {
        return Mathf.Clamp(recentPatternMemory, 1, _recentPatternNames.Length);
    }

    void RecordPatternHistory(AttackPattern pattern)
    {
        if (pattern == null)
            return;

        int capacity = ResolveRecentPatternCapacity();
        _recentPatternNames[_recentPatternWriteIndex] = pattern.patternName ?? string.Empty;
        _recentTelegraphTypes[_recentPatternWriteIndex] = pattern.ResolveTelegraphType();
        _recentPatternWriteIndex = (_recentPatternWriteIndex + 1) % capacity;
        _recentPatternRecordedCount = Mathf.Min(capacity, _recentPatternRecordedCount + 1);
    }

    bool IsImmediateRepeatPattern(string patternName)
    {
        if (string.IsNullOrEmpty(patternName) || _recentPatternRecordedCount <= 0)
            return false;

        int capacity = ResolveRecentPatternCapacity();
        int lastIndex = (_recentPatternWriteIndex - 1 + capacity) % capacity;
        return string.Equals(_recentPatternNames[lastIndex], patternName, StringComparison.Ordinal);
    }

    bool WasPatternUsedRecently(string patternName, int lookback)
    {
        if (string.IsNullOrEmpty(patternName) || _recentPatternRecordedCount <= 0)
            return false;

        int capacity = ResolveRecentPatternCapacity();
        int count = Mathf.Min(_recentPatternRecordedCount, Mathf.Clamp(lookback, 1, capacity));
        for (int i = 0; i < count; i++)
        {
            int index = (_recentPatternWriteIndex - 1 - i + capacity) % capacity;
            if (string.Equals(_recentPatternNames[index], patternName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    int CountRecentTelegraphMatches(AttackTelegraphType telegraphType, int lookback)
    {
        if (_recentPatternRecordedCount <= 0)
            return 0;

        int capacity = ResolveRecentPatternCapacity();
        int count = Mathf.Min(_recentPatternRecordedCount, Mathf.Clamp(lookback, 1, capacity));
        int matches = 0;
        for (int i = 0; i < count; i++)
        {
            int index = (_recentPatternWriteIndex - 1 - i + capacity) % capacity;
            if (_recentTelegraphTypes[index] == telegraphType)
                matches++;
        }

        return matches;
    }

    string ResolvePhaseLabel()
    {
        switch (_currentPhase)
        {
            case 2:
                return "PHASE 2";
            case 3:
                return "BERSERK";
            default:
                return "PHASE 1";
        }
    }

    void OnAnimatorMove()
    {
        if (_isUltimateVictim || _isDead || bossAnimator == null || !bossAnimator.applyRootMotion)
            return;

        if (_runStopActive)
        {
            Vector3 stopDelta = bossAnimator.deltaPosition;
            stopDelta.y = 0f;
            if (stopDelta.sqrMagnitude > 0.000001f)
                ApplyMovementDelta(stopDelta);

            Quaternion stopDeltaRotation = bossAnimator.deltaRotation;
            if (stopDeltaRotation != Quaternion.identity)
                transform.rotation *= stopDeltaRotation;

            return;
        }

        if (!useCollisionAwareRootMotion || currentState != BossState.Attack)
            return;

        if (_currentPattern != null && _currentPattern.ResolveFiresSwordWaveProjectile())
            return;

        Vector3 delta = bossAnimator.deltaPosition;
        delta.y = 0f;

        if (delta.sqrMagnitude > 0.000001f)
            ApplyMovementDelta(delta);

        Quaternion deltaRotation = bossAnimator.deltaRotation;
        if (deltaRotation != Quaternion.identity)
            transform.rotation *= deltaRotation;
    }

    // ==================== FSM 전이 ====================

    public void SetState(BossState newState, bool forceRestart = false)
    {
        if (_isDead) return;
        if (_externalIntroPaused) return;
        if (IsCombatRecoveryActive && newState != BossState.Recovery && !forceRestart) return;
        if (_combatRecoveryRoutine != null && newState != BossState.Recovery && forceRestart)
            StopCombatRecovery();
        if (currentState == newState && !forceRestart) return;

        if (newState == BossState.Detect || newState == BossState.Move || newState == BossState.Attack)
            EnsureGameplayPlayerTarget();

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }
        StopPreAttackPosePlayback();

        if (_parryStunRoutine != null)
        {
            StopCoroutine(_parryStunRoutine);
            _parryStunRoutine = null;
        }
        ReleaseParryStunAnimationFallback();

        var old = currentState;
        currentState = newState;
        if (currentState != BossState.Attack)
        {
            _isExecutingPostAction = false;
            _isEngaging = false;
        }
        if (enableStateLogs)
            LogState($"[BossFSM] {old} → {newState}");

        if (currentState != BossState.Attack)
            HideGroundTelegraph();

        switch (currentState)
        {
            case BossState.IntroIdle:
                _stateRoutine = StartCoroutine(Co_HandleIntroIdle());
                break;
            case BossState.Detect:
                _stateRoutine = StartCoroutine(Co_HandleDetect());
                break;
            case BossState.Move:
                _stateRoutine = StartCoroutine(Co_HandleMove());
                break;
            case BossState.Attack:
                _stateRoutine = StartCoroutine(Co_PerformAttack());
                break;
            case BossState.CombatIdle:
                _stateRoutine = StartCoroutine(Co_CombatIdle());
                break;
            case BossState.Break:
                _stateRoutine = StartCoroutine(Co_HandleBreak());
                break;
            case BossState.Recovery:
                break;
            case BossState.Dead:
                // Dead 상태에서는 별도 코루틴 없음
                break;
        }
    }

    void LogState(string message)
    {
        if (!enableStateLogs)
            return;

        Debug.Log(message);
    }

    void LogStateWarning(string message)
    {
        if (!enableStateLogs)
            return;

        Debug.LogWarning(message);
    }

    bool _externalIntroPaused;

    public void SetExternalIntroPaused(bool paused)
    {
        if (_isDead || _externalIntroPaused == paused)
            return;

        _externalIntroPaused = paused;
        if (paused)
        {
            StopCombatRecovery();
            if (currentState == BossState.Recovery)
                currentState = BossState.IntroIdle;

            if (_stateRoutine != null)
            {
                StopCoroutine(_stateRoutine);
                _stateRoutine = null;
            }

            AbortAttackExecution(clearPunishWindow: true);
            StopRunStopMotion();
            UpdateMoveAnimation(0f);
            SetCombatStrafeAnimation(false);
            return;
        }

        SetState(BossState.Detect, true);
    }

    // ==================== 생존/브레이크 ====================

    void OnBossDied()
    {
        if (_isDead) return;
        _isDead = true;
        StopCombatRecovery();
        AbortAttackExecution(clearPunishWindow: true);

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
        ReleaseParryStunAnimationFallback();

        currentState = BossState.Dead;
        UpdateMoveAnimation(0f);

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsDead, true);

        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();

        LogState("[BossFSM] Boss Dead");

        if (_isUltimateVictim)
        {
            _delayDestroyUntilUltimateVictimEnds = autoDestroyOnDead;
            return;
        }

        if (autoDestroyOnDead && _destroyRoutine == null)
            _destroyRoutine = StartCoroutine(Co_DestroyHierarchyAfterDead());
    }

    void OnBreakEnter()
    {
        if (_isDead) return;
        if (_isUltimateVictim)
        {
            if (bossAnimator != null)
                bossAnimator.SetBool(AnimParam_IsBreak, true);
            return;
        }

        if (_parryStunRoutine != null)
        {
            StopCoroutine(_parryStunRoutine);
            _parryStunRoutine = null;
        }
        ReleaseParryStunAnimationFallback();
        AbortAttackExecution(clearPunishWindow: true);
        UpdateMoveAnimation(0f);

        if (bossAnimator != null)
        {
            bossAnimator.SetBool(AnimParam_IsBreak, true);
            bossAnimator.CrossFadeInFixedTime("Break", 0.05f, 0);
        }

        SetState(BossState.Break);
    }

    void OnBreakExit()
    {
        if (_isDead) return;

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsBreak, false);

        if (_isUltimateVictim)
            return;

        RefreshGameplayPlayerTargetAfterRecovery();
        TryStartCombatRecovery(BossRecoveryReason.Break, BossState.CombatIdle);
    }

    public void OnParried(GameObject parrier, float riposteDamage, float stunDuration)
    {
        RecordPlayerDefenseResponse(true);

        if (breakController != null && !_isDead && !_isUltimateVictim && !IsCombatRecoveryLockoutActive && !IsCombatRecoverySuperArmorActive && !breakController.IsInBreak)
            breakController.AddBreak(0f, BossBreakController.BreakSource.Parry);

        NotifyParried(stunDuration);
    }

    public void OnPerfectDodged(GameObject dodger)
    {
        RecordPlayerDefenseResponse(false);
    }

    public void NotifyParried()
    {
        NotifyParried(parryStunDuration);
    }

    public void RequestCombatRecovery(BossRecoveryReason reason, bool forceRestart = false)
    {
        if (forceRestart)
            StopCombatRecovery();

        TryStartCombatRecovery(reason, BossState.CombatIdle);
    }

    public void NotifyKnockbackEnded()
    {
        RequestCombatRecovery(BossRecoveryReason.Knockback);
    }

    public void NotifyDownStateEnded()
    {
        RequestCombatRecovery(BossRecoveryReason.Down);
    }

    public void NotifyCutsceneAttackEnded()
    {
        RequestCombatRecovery(BossRecoveryReason.CutsceneAttack);
    }

    public void NotifyTargetReacquired()
    {
        RequestCombatRecovery(BossRecoveryReason.TargetReacquire);
    }

    void NotifyParried(float stunDuration)
    {
        if (_isDead || _isUltimateVictim || IsCombatRecoveryLockoutActive)
            return;

        if (breakController != null && breakController.IsInBreak)
            return;

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
        ReleaseParryStunAnimationFallback();
        AbortAttackExecution(clearPunishWindow: true);
        currentState = BossState.CombatIdle;
        UpdateMoveAnimation(0f);
        PlayParryStunAnimation();
        _parryStunRoutine = StartCoroutine(Co_HandleParryStunInterrupt(Mathf.Max(0.04f, stunDuration)));
    }

    // ==================== 사망 후 파괴 처리 ====================

    IEnumerator Co_DestroyHierarchyAfterDead()
    {
        if (destroyDelayAfterDead > 0f)
            yield return new WaitForSeconds(destroyDelayAfterDead);

        FinalizeDeathAndDestroy();
    }

    IEnumerator Co_HandleParryStunInterrupt(float stunDuration)
    {
        float remaining = Mathf.Max(0.04f, stunDuration);
        LogState("[BossFSM] Parry stun interrupt");

        while (!_isDead && remaining > 0f)
        {
            remaining -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        _parryStunRoutine = null;
        ReleaseParryStunAnimationFallback();

        if (_isDead || _isUltimateVictim)
            yield break;

        if (breakController != null && breakController.IsInBreak)
        {
            SetState(BossState.Break, true);
            yield break;
        }

        StageAttackRecovery(
            string.IsNullOrWhiteSpace(parryPunishSource) ? "PARRY" : parryPunishSource.Trim().ToUpperInvariant(),
            Mathf.Max(combatIdleTime, parryRecoveryDuration),
            Mathf.Max(minimumParryPunishWindow, parryPunishWindowDuration),
            Mathf.Max(1f, parryPunishDamageMultiplier));
        TryStartCombatRecovery(BossRecoveryReason.ParryStun, BossState.CombatIdle, clearPunishWindow: false);
    }

    /// <summary>
    /// Dead 애니 끝에서 AnimationEvent로 직접 호출하고 싶을 때 사용 가능.
    /// </summary>
    public void FinalizeDeathAndDestroy()
    {
        if (!_isDead)
            return;

        if (extraObjectsToDestroyOnDead != null)
        {
            for (int i = 0; i < extraObjectsToDestroyOnDead.Length; ++i)
            {
                Transform t = extraObjectsToDestroyOnDead[i];
                if (t != null)
                    Destroy(t.gameObject);
            }
        }

        Transform root = rootToDestroyOnDead != null ? rootToDestroyOnDead : transform.root;
        if (root != null)
            Destroy(root.gameObject);
        else
            Destroy(gameObject);
    }

    // ==================== 애니메이션 이벤트용 ====================

    public void OnAnimationPatternEnd()
    {
        if (currentState != BossState.Attack)
            return;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        float distanceToPlayer = playerTarget != null
            ? Vector3.Distance(transform.position, playerTarget.position)
            : float.PositiveInfinity;

        if (_currentPattern != null && TryQueueFollowUp(_currentPattern, distanceToPlayer))
        {
            StopAttackTimingController();
            _currentPattern = null;
            SetState(BossState.Attack, true);
            return;
        }

        if (_currentPattern != null)
        {
            StopAttackTimingController();
            StageAttackRecovery(
                _currentPattern.patternName,
                _currentPattern.ResolveRecoveryTime(),
                _currentPattern.ResolvePunishWindowDuration(),
                _currentPattern.ResolvePunishDamageMultiplier());
        }

        EnterCombatIdleFromCurrentPattern();
    }

    public void ActivateHitbox()
    {
        if (_currentPattern != null && attackHitbox != null)
            attackHitbox.ActivateWindow();
    }

    public void DeactivateHitbox()
    {
        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();
    }

    void StartAttackTimingController(BossAttackTimingData timingData)
    {
        if (_attackTimingController == null)
            _attackTimingController = new BossAttackTimingController();

        StopAttackTimingController();

        timingData.Normalize();
        if (!timingData.HasAnyTiming)
            return;

        _activeAttackTimingData = timingData;
        _hasActiveAttackTimingData = true;
        _timingDataControlsHitbox = timingData.useTimingDataHitboxControl;
        _attackTimingControlsParryWindow = timingData.parryWindowEndTime > timingData.parryWindowStartTime + 0.001f;
        if (_attackTimingControlsParryWindow && attackHitbox != null)
        {
            _attackTimingCachedCanParry = attackHitbox.canParry;
            _attackTimingHasCachedCanParry = true;
            attackHitbox.canParry = false;
        }

        if (_timingDataControlsHitbox)
            CloseAttackTimingHitbox();

        _attackTimingRoutine = StartCoroutine(_attackTimingController.Execute(
            timingData,
            CanContinueAttackTiming,
            HandleAttackTimingTelegraph,
            OpenAttackTimingHitbox,
            CloseAttackTimingHitbox,
            OpenAttackTimingParryWindow,
            CloseAttackTimingParryWindow,
            enableAttackTimingDebugLog,
            this));
    }

    void OpenAttackTimingHitbox()
    {
        BossAttackFeedbackPresenter feedbackPresenter = ResolveAttackFeedbackPresenter();
        if (feedbackPresenter != null && _hasActiveAttackTimingData)
            feedbackPresenter.PlaySwingFeedback(_activeAttackTimingData.telegraphType, transform);

        if (attackVfxPresenter != null)
            attackVfxPresenter.PlayCurrentPatternSlash();

        ActivateHitbox();
    }

    void CloseAttackTimingHitbox()
    {
        if (attackVfxPresenter != null)
            attackVfxPresenter.StopCurrentPatternSlash();

        DeactivateHitbox();
    }

    void StopAttackTimingController()
    {
        if (_attackTimingRoutine != null)
        {
            StopCoroutine(_attackTimingRoutine);
            _attackTimingRoutine = null;
        }

        if (_timingDataControlsHitbox)
            CloseAttackTimingHitbox();

        CloseAttackTimingParryWindow();
        if (telegraphVfxPresenter != null)
            telegraphVfxPresenter.ResetCue();
        if (attackFeedbackPresenter != null)
            attackFeedbackPresenter.ResetFeedback();
        RestoreAttackTimingParryState();
        _hasActiveAttackTimingData = false;
        _activeAttackTimingData = default;
        _timingDataControlsHitbox = false;
        _attackTimingControlsParryWindow = false;
        _attackTimingTelegraphLabel = string.Empty;
    }

    bool CanContinueAttackTiming()
    {
        return !_isDead &&
               !_isUltimateVictim &&
               !IsCombatRecoveryActive &&
               !_externalIntroPaused &&
               currentState == BossState.Attack &&
               _currentPattern != null &&
               playerTarget != null &&
               IsValidGameplayPlayerTarget(playerTarget) &&
               _parryStunRoutine == null &&
               (bossHealth == null || !bossHealth.IsStaggered) &&
               (breakController == null || !breakController.IsInBreak);
    }

    void HandleAttackTimingTelegraph(BossAttackTelegraphType timingTelegraphType, float duration)
    {
        AttackTelegraphType telegraphType = ConvertAttackTimingTelegraph(timingTelegraphType);
        if (telegraphType == AttackTelegraphType.Auto)
            return;

        float resolvedDuration = Mathf.Max(0f, duration);
        BossAttackTimingCueFlags cueFlags = ResolveAttackTimingCueFlags(_activeAttackTimingData);
        BossAttackFeedbackPresenter feedbackPresenter = ResolveAttackFeedbackPresenter();
        BossAttackTimingCueFlags resolvedCueFlags = feedbackPresenter != null
            ? cueFlags | feedbackPresenter.ResolveProfileCueFlags(timingTelegraphType)
            : cueFlags;
        bool visualCueHandled = false;
        if (resolvedCueFlags != BossAttackTimingCueFlags.None)
        {
            OnAttackTimingCue?.Invoke(timingTelegraphType, resolvedDuration, resolvedCueFlags);
            BossTelegraphVfxPresenter presenter = ResolveTelegraphVfxPresenter();
            if (feedbackPresenter != null)
            {
                visualCueHandled = feedbackPresenter.PlayTelegraphFeedback(
                    timingTelegraphType,
                    resolvedDuration,
                    resolvedCueFlags,
                    presenter,
                    groundTelegraph,
                    attackHitbox != null ? attackHitbox.Collider : null,
                    transform,
                    playerTarget);
            }
            else
            {
                if (presenter != null)
                    visualCueHandled = presenter.PlayCue(timingTelegraphType, resolvedDuration, resolvedCueFlags);
                ApplyAttackTimingCueEffects(resolvedCueFlags);
            }
        }

        bool hasCueFlags = _hasActiveAttackTimingData && HasAttackTimingCueFlags(_activeAttackTimingData);
        bool useVisualCue = !hasCueFlags || _activeAttackTimingData.useWeaponFlash || _activeAttackTimingData.useBodyFlash;
        bool useHudCue = !hasCueFlags || _activeAttackTimingData.useWarningSound || _activeAttackTimingData.useCameraShake;

        if (useVisualCue && !visualCueHandled && patternVisuals != null)
            patternVisuals.StartVisualCue(telegraphType, resolvedDuration);

        if (useHudCue)
            OnAttackTelegraph?.Invoke(telegraphType, resolvedDuration, _attackTimingTelegraphLabel);
    }

    BossTelegraphVfxPresenter ResolveTelegraphVfxPresenter()
    {
        if (telegraphVfxPresenter != null)
            return telegraphVfxPresenter;

        telegraphVfxPresenter = GetComponentInChildren<BossTelegraphVfxPresenter>(true);
        if (telegraphVfxPresenter != null)
            return telegraphVfxPresenter;

        if (!autoCreateRuntimeTelegraphVfxPresenter || !Application.isPlaying)
            return null;

        GameObject host = patternVisuals != null ? patternVisuals.gameObject : gameObject;
        telegraphVfxPresenter = host.AddComponent<BossTelegraphVfxPresenter>();
        return telegraphVfxPresenter;
    }

    BossAttackFeedbackPresenter ResolveAttackFeedbackPresenter()
    {
        if (attackFeedbackPresenter != null)
            return attackFeedbackPresenter;

        attackFeedbackPresenter = GetComponentInChildren<BossAttackFeedbackPresenter>(true);
        if (attackFeedbackPresenter != null)
            return attackFeedbackPresenter;

        if (!autoCreateRuntimeAttackFeedbackPresenter || !Application.isPlaying)
            return null;

        GameObject host = patternVisuals != null ? patternVisuals.gameObject : gameObject;
        attackFeedbackPresenter = host.AddComponent<BossAttackFeedbackPresenter>();
        return attackFeedbackPresenter;
    }

    static BossAttackTimingCueFlags ResolveAttackTimingCueFlags(BossAttackTimingData timingData)
    {
        BossAttackTimingCueFlags flags = BossAttackTimingCueFlags.None;
        if (timingData.useWeaponFlash)
            flags |= BossAttackTimingCueFlags.WeaponFlash;
        if (timingData.useBodyFlash)
            flags |= BossAttackTimingCueFlags.BodyFlash;
        if (timingData.useWarningSound)
            flags |= BossAttackTimingCueFlags.WarningSound;
        if (timingData.useCameraShake)
            flags |= BossAttackTimingCueFlags.CameraShake;

        return flags;
    }

    void ApplyAttackTimingCueEffects(BossAttackTimingCueFlags cueFlags)
    {
        ApplyAttackTimingWarningSound(cueFlags);

        if ((cueFlags & BossAttackTimingCueFlags.CameraShake) == 0)
            return;

        float now = Time.unscaledTime;
        if (now < _nextAttackTimingCueShakeRealtime)
            return;

        _attackTimingCueCameraShake = GameplaySceneCache.ResolveMainCameraShake();
        if (_attackTimingCueCameraShake == null)
            return;

        float amplitude = Mathf.Max(0f, attackTimingCueShakeAmplitude);
        float duration = Mathf.Max(0f, attackTimingCueShakeDuration);
        if (amplitude <= 0f || duration <= 0f)
            return;

        _attackTimingCueCameraShake.Shake(amplitude, duration);
        _nextAttackTimingCueShakeRealtime = now + Mathf.Max(0f, attackTimingCueShakeMinIntervalRealtime);
    }

    void ApplyAttackTimingWarningSound(BossAttackTimingCueFlags cueFlags)
    {
        if ((cueFlags & BossAttackTimingCueFlags.WarningSound) == 0)
            return;

        if (attackTimingWarningClip == null)
            return;

        float now = Time.unscaledTime;
        if (now < _nextAttackTimingWarningRealtime)
            return;

        if (attackTimingCueAudioSource == null)
            return;

        attackTimingCueAudioSource.PlayOneShot(attackTimingWarningClip, Mathf.Clamp01(attackTimingWarningVolume));
        _nextAttackTimingWarningRealtime = now + Mathf.Max(0f, attackTimingWarningMinIntervalRealtime);
    }

    void HandleAttackHitboxHitApplied(HitPayload payload)
    {
        BossAttackFeedbackPresenter feedbackPresenter = ResolveAttackFeedbackPresenter();
        if (feedbackPresenter == null || !_hasActiveAttackTimingData)
            return;

        feedbackPresenter.PlayImpactFeedback(_activeAttackTimingData.telegraphType, payload.hitPoint, transform);
    }

    static bool HasAttackTimingCueFlags(BossAttackTimingData timingData)
    {
        return timingData.useWeaponFlash ||
               timingData.useBodyFlash ||
               timingData.useWarningSound ||
               timingData.useCameraShake;
    }

    void OpenAttackTimingParryWindow()
    {
        _attackTimingParryWindowOpen = true;
        if (_attackTimingControlsParryWindow && attackHitbox != null && _attackTimingHasCachedCanParry)
            attackHitbox.canParry = _attackTimingCachedCanParry;
    }

    void CloseAttackTimingParryWindow()
    {
        _attackTimingParryWindowOpen = false;
        if (_attackTimingControlsParryWindow && attackHitbox != null)
            attackHitbox.canParry = false;
    }

    void RestoreAttackTimingParryState()
    {
        if (_attackTimingHasCachedCanParry && attackHitbox != null)
            attackHitbox.canParry = _attackTimingCachedCanParry;

        _attackTimingHasCachedCanParry = false;
        _attackTimingCachedCanParry = false;
    }

    void StartPatternCooldown(AttackPattern pattern, float now)
    {
        if (pattern == null)
            return;

        float cooldown = Mathf.Max(0f, BuildSelectorData(pattern).cooldown);
        pattern.currentCooldown = cooldown;
        pattern.cooldownReadyAt = cooldown > 0f ? now + cooldown : 0f;
    }

    bool TryResolveAttackTimingData(AttackPattern pattern, out BossAttackTimingData timingData)
    {
        timingData = default;
        if (pattern == null)
            return false;

        BossPatternId patternId = BuildSelectorData(pattern).patternId;
        if (patternId == BossPatternId.None)
            return false;

        if (attackTimingData != null)
        {
            for (int i = 0; i < attackTimingData.Length; i++)
            {
                if (attackTimingData[i].patternId != patternId)
                    continue;

                timingData = attackTimingData[i];
                timingData.Normalize();
                return timingData.HasAnyTiming;
            }
        }

        return TryBuildDefaultAttackTimingData(pattern, patternId, out timingData);
    }

#if UNITY_EDITOR
    public bool TryGetResolvedAttackTimingDataForValidation(AttackPattern pattern, out BossAttackTimingData timingData)
    {
        return TryResolveAttackTimingData(pattern, out timingData);
    }
#endif

    bool TryBuildDefaultAttackTimingData(AttackPattern pattern, BossPatternId patternId, out BossAttackTimingData timingData)
    {
        timingData = default;
        if (!useDefaultAttackTimingFallback || pattern == null || patternId == BossPatternId.None)
            return false;

        timingData = CreateDefaultAttackTimingData(pattern, patternId);
        timingData.telegraphDuration *= defaultAttackTimingTelegraphScale;
        float telegraphDuration = Mathf.Max(0f, timingData.telegraphDuration);

        if (defaultAttackTimingControlsParryWindow && pattern.ResolveCanParry() && telegraphDuration > 0.001f)
        {
            float windowDuration = telegraphDuration * defaultAttackTimingParryWindowScale;
            timingData.parryWindowStartTime = Mathf.Max(0f, telegraphDuration - windowDuration);
            timingData.parryWindowEndTime = telegraphDuration;
        }

        timingData.Normalize();
        return timingData.HasAnyTiming;
    }

    [ContextMenu("Boss/Attack Timing/Rebuild Defaults From Patterns")]
    void RebuildDefaultAttackTimingDataFromPatterns()
    {
        if (allPatterns == null || allPatterns.Count == 0)
            return;

        BossAttackTimingData[] buffer = new BossAttackTimingData[Mathf.Min(allPatterns.Count, 8)];
        int count = 0;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            BossPatternId patternId = ResolvePatternId(pattern);
            if (pattern == null || patternId == BossPatternId.None || ContainsTimingPatternId(buffer, count, patternId))
                continue;

            if (count >= buffer.Length)
                Array.Resize(ref buffer, count + 4);

            buffer[count] = CreateDefaultAttackTimingData(pattern, patternId);
            buffer[count].Normalize();
            count++;
        }

        attackTimingData = new BossAttackTimingData[count];
        for (int i = 0; i < count; i++)
            attackTimingData[i] = buffer[i];
    }

    [ContextMenu("Boss/Combat Data/Rebuild Default Timing And Phase Data")]
    public void RebuildDefaultCombatDataFromPatterns()
    {
        RebuildDefaultAttackTimingDataFromPatterns();
        RebuildDefaultPhaseModifiersFromPatterns();
        RebuildPatternSelectorCache();
        RefreshPatternCooldownState();
    }

    [ContextMenu("Boss/Phase/Rebuild Default Phase Modifiers From Patterns")]
    void RebuildDefaultPhaseModifiersFromPatterns()
    {
        if (allPatterns == null || allPatterns.Count == 0)
            return;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            BossPatternId patternId = pattern.selectorData.patternId != BossPatternId.None
                ? pattern.selectorData.patternId
                : ResolvePatternId(pattern);

            if (patternId == BossPatternId.None)
                continue;

            pattern.phaseModifiers = CreateDefaultPhaseModifiers(patternId);
        }
    }

    static BossPhasePatternModifier[] CreateDefaultPhaseModifiers(BossPatternId patternId)
    {
        return new[]
        {
            CreateDefaultPhaseModifier(patternId, 2),
            CreateDefaultPhaseModifier(patternId, 3)
        };
    }

    static BossPhasePatternModifier CreateDefaultPhaseModifier(BossPatternId patternId, int phase)
    {
        BossPhasePatternModifier modifier = new BossPhasePatternModifier
        {
            patternId = patternId,
            phase = Mathf.Clamp(phase, 1, 3),
            baseWeightMultiplier = phase >= 3 ? 1.12f : 1.04f,
            cooldownMultiplier = phase >= 3 ? 0.82f : 0.92f,
            telegraphDurationMultiplier = phase >= 3 ? 0.88f : 0.96f,
            recoveryDurationMultiplier = phase >= 3 ? 0.84f : 0.92f,
            damageMultiplier = phase >= 3 ? 1.12f : 1f,
            engageMoveSpeedMultiplier = phase >= 3 ? 1.16f : 1.08f,
            overridePostAction = false,
            postActionOverride = BossPatternPostActionType.None,
            followUpPatternId = BossPatternId.None,
            overrideFollowUp = phase >= 2,
            allowFollowUp = phase >= 2,
            maxFollowUpCount = phase >= 3 ? 2 : 1
        };

        switch (patternId)
        {
            case BossPatternId.SwordWave:
                modifier.baseWeightMultiplier = phase >= 3 ? 1.35f : 1.22f;
                modifier.cooldownMultiplier = phase >= 3 ? 0.78f : 0.88f;
                modifier.overridePostAction = true;
                modifier.postActionOverride = BossPatternPostActionType.ChaseReposition;
                modifier.followUpPatternId = phase >= 3 ? BossPatternId.DashSlash : BossPatternId.None;
                break;

            case BossPatternId.DashSlash:
                modifier.baseWeightMultiplier = phase >= 3 ? 1.28f : 1.16f;
                modifier.engageMoveSpeedMultiplier = phase >= 3 ? 1.24f : 1.12f;
                modifier.overridePostAction = true;
                modifier.postActionOverride = phase >= 3
                    ? BossPatternPostActionType.Recenter
                    : BossPatternPostActionType.ChaseReposition;
                modifier.followUpPatternId = phase >= 3 ? BossPatternId.QuickSlash : BossPatternId.None;
                break;

            case BossPatternId.HeavySlash:
                modifier.telegraphDurationMultiplier = phase >= 3 ? 0.90f : 0.98f;
                modifier.recoveryDurationMultiplier = phase >= 3 ? 0.88f : 0.95f;
                modifier.damageMultiplier = phase >= 3 ? 1.18f : 1.08f;
                modifier.overridePostAction = true;
                modifier.postActionOverride = BossPatternPostActionType.Backstep;
                modifier.followUpPatternId = phase >= 3 ? BossPatternId.SwordWave : BossPatternId.None;
                break;

            case BossPatternId.BackstepSlash:
                modifier.baseWeightMultiplier = phase >= 3 ? 1.18f : 1.10f;
                modifier.cooldownMultiplier = phase >= 3 ? 0.84f : 0.92f;
                modifier.overridePostAction = true;
                modifier.postActionOverride = BossPatternPostActionType.Recenter;
                modifier.followUpPatternId = phase >= 3 ? BossPatternId.DashSlash : BossPatternId.None;
                break;

            case BossPatternId.QuickSlash:
            default:
                modifier.followUpPatternId = phase >= 3 ? BossPatternId.HeavySlash : BossPatternId.None;
                break;
        }

        return modifier;
    }

    static bool ContainsTimingPatternId(BossAttackTimingData[] data, int count, BossPatternId patternId)
    {
        for (int i = 0; i < count; i++)
        {
            if (data[i].patternId == patternId)
                return true;
        }

        return false;
    }

    BossAttackTimingData CreateDefaultAttackTimingData(AttackPattern pattern, BossPatternId patternId)
    {
        BossAttackTimingData timingData = CreateDefaultAttackTimingData(patternId);
        if (timingData.telegraphType == BossAttackTelegraphType.None && pattern != null)
            timingData.telegraphType = ConvertAttackTelegraphToTiming(pattern.ResolveTelegraphType());

        if (pattern != null && pattern.ResolveCanParry() && timingData.parryWindowEndTime <= timingData.parryWindowStartTime + 0.001f)
        {
            float telegraphDuration = Mathf.Max(0f, timingData.telegraphDuration);
            if (telegraphDuration > 0.001f)
            {
                timingData.parryWindowStartTime = telegraphDuration * 0.45f;
                timingData.parryWindowEndTime = telegraphDuration;
            }
        }

        return timingData;
    }

    static BossAttackTimingData CreateDefaultAttackTimingData(BossPatternId patternId)
    {
        BossAttackTimingData timingData = new BossAttackTimingData
        {
            patternId = patternId,
            telegraphStartTime = 0f,
            useTimingDataHitboxControl = false,
            useWeaponFlash = true
        };

        switch (patternId)
        {
            case BossPatternId.SwordWave:
                timingData.telegraphType = BossAttackTelegraphType.Ranged;
                timingData.telegraphDuration = 0.45f;
                timingData.hitboxOpenTime = 0.50f;
                timingData.hitboxCloseTime = 0.60f;
                timingData.recoveryStartTime = 0.70f;
                timingData.recoveryDuration = 0.45f;
                break;
            case BossPatternId.DashSlash:
                timingData.telegraphType = BossAttackTelegraphType.DodgeOnly;
                timingData.telegraphDuration = 0.35f;
                timingData.hitboxOpenTime = 0.40f;
                timingData.hitboxCloseTime = 0.65f;
                timingData.recoveryStartTime = 0.75f;
                timingData.recoveryDuration = 0.40f;
                break;
            case BossPatternId.HeavySlash:
                timingData.telegraphType = BossAttackTelegraphType.Heavy;
                timingData.telegraphDuration = 0.65f;
                timingData.hitboxOpenTime = 0.70f;
                timingData.hitboxCloseTime = 0.95f;
                timingData.parryWindowStartTime = 0.35f;
                timingData.parryWindowEndTime = 0.65f;
                timingData.recoveryStartTime = 1.00f;
                timingData.recoveryDuration = 0.70f;
                timingData.useCameraShake = true;
                break;
            case BossPatternId.BackstepSlash:
                timingData.telegraphType = BossAttackTelegraphType.Parryable;
                timingData.telegraphDuration = 0.25f;
                timingData.hitboxOpenTime = 0.30f;
                timingData.hitboxCloseTime = 0.52f;
                timingData.parryWindowStartTime = 0.12f;
                timingData.parryWindowEndTime = 0.30f;
                timingData.recoveryStartTime = 0.58f;
                timingData.recoveryDuration = 0.35f;
                break;
            case BossPatternId.QuickSlash:
            default:
                timingData.telegraphType = BossAttackTelegraphType.Parryable;
                timingData.telegraphDuration = 0.25f;
                timingData.hitboxOpenTime = 0.28f;
                timingData.hitboxCloseTime = 0.48f;
                timingData.parryWindowStartTime = 0.12f;
                timingData.parryWindowEndTime = 0.32f;
                timingData.recoveryStartTime = 0.55f;
                timingData.recoveryDuration = 0.35f;
                break;
        }

        timingData.Normalize();
        return timingData;
    }

    string ResolveAttackTimingTelegraphLabel(BossAttackTimingData timingData, string fallbackLabel)
    {
        switch (timingData.telegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                return "PARRY";
            case BossAttackTelegraphType.DodgeOnly:
                return "DODGE";
            case BossAttackTelegraphType.Unblockable:
                return "DANGER";
            case BossAttackTelegraphType.Heavy:
                return "HEAVY";
            case BossAttackTelegraphType.Ranged:
                return "RANGED";
            case BossAttackTelegraphType.Normal:
                return "ATTACK";
            case BossAttackTelegraphType.None:
            default:
                return string.IsNullOrWhiteSpace(fallbackLabel) ? "ATTACK" : fallbackLabel;
        }
    }

    AttackTelegraphType ConvertAttackTimingTelegraph(BossAttackTelegraphType timingTelegraphType)
    {
        switch (timingTelegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                return AttackTelegraphType.Parry;
            case BossAttackTelegraphType.DodgeOnly:
            case BossAttackTelegraphType.Ranged:
                return AttackTelegraphType.Dodge;
            case BossAttackTelegraphType.Unblockable:
            case BossAttackTelegraphType.Heavy:
                return AttackTelegraphType.Danger;
            case BossAttackTelegraphType.Normal:
                return AttackTelegraphType.Guard;
            case BossAttackTelegraphType.None:
            default:
                return AttackTelegraphType.Auto;
        }
    }

    // ==================== 트리거 통합 유틸 ====================

    /// <summary>
    /// 모든 공격/백스텝 트리거를 Reset한 뒤, 이번에 쓸 트리거 하나만 Set.
    /// 트리거가 겹치면서 애니 상태 꼬이는 문제를 예방하기 위한 유틸.
    /// </summary>
    static BossAttackTelegraphType ConvertAttackTelegraphToTiming(AttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case AttackTelegraphType.Parry:
                return BossAttackTelegraphType.Parryable;
            case AttackTelegraphType.Guard:
                return BossAttackTelegraphType.Normal;
            case AttackTelegraphType.Dodge:
                return BossAttackTelegraphType.DodgeOnly;
            case AttackTelegraphType.Danger:
                return BossAttackTelegraphType.Unblockable;
            case AttackTelegraphType.Auto:
            default:
                return BossAttackTelegraphType.None;
        }
    }

    void PlayAnimTrigger(string triggerName)
    {
        if (bossAnimator == null || string.IsNullOrEmpty(triggerName))
            return;

        StopRunStopMotion();
        SetCombatStrafeAnimation(false);

        // 1) 공격 패턴 트리거 전부 Reset
        if (allPatterns != null)
        {
            foreach (var p in allPatterns)
            {
                if (p != null && !string.IsNullOrEmpty(p.animTriggerName))
                    bossAnimator.ResetTrigger(p.animTriggerName);
            }
        }

        // 2) 백스텝 트리거 Reset
        if (!string.IsNullOrEmpty(backstepAnimTriggerName) &&
            backstepAnimTriggerName == "Quickshift_B" &&
            _hasQuickshiftBTrigger)
            bossAnimator.ResetTrigger(backstepAnimTriggerName);

        if (_hasQuickshiftBTrigger)
            bossAnimator.ResetTrigger("Quickshift_B");

        // 3) 이번에 쓸 트리거만 Set
        bossAnimator.SetTrigger(triggerName);
    }

    // ==================== 회전 보정 유틸 ====================

    /// <summary>
    /// 플레이어 방향으로 즉시 스냅 회전 (Y축만).
    /// </summary>
    void CacheParryStunAnimatorHooks()
    {
        _parryStunTriggerHash = string.IsNullOrWhiteSpace(parryStunTriggerName) ? 0 : Animator.StringToHash(parryStunTriggerName);
        _parryStunStateHash = string.IsNullOrWhiteSpace(parryStunStateName) ? 0 : Animator.StringToHash(parryStunStateName);
        _hasParryStunTrigger = false;
        _hasQuickshiftBTrigger = false;
        _hasMoveXParam = false;
        _hasMoveYParam = false;
        _hasCombatStrafingParam = false;

        if (bossAnimator == null || bossAnimator.runtimeAnimatorController == null)
            return;

        AnimatorControllerParameter[] parameters = bossAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Trigger)
            {
                if (_parryStunTriggerHash != 0 && parameter.nameHash == _parryStunTriggerHash)
                    _hasParryStunTrigger = true;

                if (parameter.nameHash == AnimParam_QuickshiftB)
                    _hasQuickshiftBTrigger = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Float)
            {
                if (parameter.nameHash == AnimParam_MoveX)
                    _hasMoveXParam = true;

                if (parameter.nameHash == AnimParam_MoveY)
                    _hasMoveYParam = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                if (parameter.nameHash == AnimParam_IsCombatStrafing)
                    _hasCombatStrafingParam = true;
            }

            if (_hasParryStunTrigger && _hasQuickshiftBTrigger && _hasMoveXParam && _hasMoveYParam && _hasCombatStrafingParam)
            {
                break;
            }
        }
    }

    void PlayParryStunAnimation()
    {
        if (bossAnimator == null)
            return;

        ReleaseParryStunAnimationFallback();

        if (_hasParryStunTrigger && !string.IsNullOrEmpty(parryStunTriggerName))
        {
            PlayAnimTrigger(parryStunTriggerName);
            return;
        }

        if (_parryStunStateHash != 0 && bossAnimator.HasState(0, _parryStunStateHash))
        {
            bossAnimator.CrossFadeInFixedTime(_parryStunStateHash, parryStunTransitionDuration, 0);
            return;
        }

        // Freeze the interrupted attack if no dedicated parry stun clip is configured.
        _parryStunCachedAnimatorSpeed = bossAnimator.speed;
        bossAnimator.speed = 0f;
        _parryStunUsedAnimatorFreezeFallback = true;
    }

    void ReleaseParryStunAnimationFallback()
    {
        if (!_parryStunUsedAnimatorFreezeFallback)
            return;

        _parryStunUsedAnimatorFreezeFallback = false;
        if (bossAnimator != null)
            bossAnimator.speed = _parryStunCachedAnimatorSpeed;
    }

    void FaceToPlayerInstant()
    {
        if (playerTarget == null) return;

        Vector3 to = playerTarget.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(to.normalized);
    }

    /// <summary>
    /// 일정 시간 동안 플레이어 방향으로 부드럽게 회전 (마지막에 한 번 더 스냅).
    /// </summary>
    IEnumerator Co_FacePlayerShort(float duration, float speedDegPerSec)
    {
        if (playerTarget == null || duration <= 0f)
        {
            FaceToPlayerInstant();
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && !_isDead)
        {
            RotateTowardsPlayer(Time.deltaTime, speedDegPerSec);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 마지막에 한 번 더 정확히 맞춰주기
        FaceToPlayerInstant();
    }

    /// <summary>
    /// 1프레임 동안 플레이어를 향해 회전 (RotateTowards).
    /// </summary>
    void RotateTowardsPlayer(float deltaTime, float speedDegPerSec)
    {
        if (playerTarget == null) return;

        Vector3 to = playerTarget.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(to.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRot, speedDegPerSec * deltaTime);
    }

    float ResolveAttackDecisionDistance()
    {
        return Mathf.Max(stoppingDistance, stoppingDistance + attackCommitDistanceBuffer);
    }

    float ResolvePressureEngageDistance()
    {
        return Mathf.Max(ResolveAttackDecisionDistance() + 0.35f, pressureEngageDistance);
    }

    float ResolvePressureReleaseDistance()
    {
        return Mathf.Max(ResolvePressureEngageDistance() + 0.25f, pressureReleaseDistance);
    }

    float ResolveApproachSpeedMultiplier(float distanceToPlayer)
    {
        float minSpeed = Mathf.Clamp(closeApproachSpeedMultiplier, 0.15f, 1f);
        float slowdownStart = Mathf.Max(stoppingDistance + 0.05f, moveSlowdownDistance);

        if (distanceToPlayer >= slowdownStart)
            return 1f;

        if (distanceToPlayer <= stoppingDistance)
            return minSpeed;

        float t = Mathf.InverseLerp(stoppingDistance, slowdownStart, distanceToPlayer);
        return Mathf.Lerp(minSpeed, 1f, t);
    }

    float ResolveChaseTurnSpeed(float distanceToPlayer)
    {
        float closeDistance = Mathf.Max(stoppingDistance + 0.1f, closeChaseTurnDistance);
        return distanceToPlayer <= closeDistance
            ? Mathf.Max(chaseTurnSpeed, closeChaseTurnSpeed)
            : Mathf.Max(60f, chaseTurnSpeed);
    }

    float ResolveBackstepDecisionDistance()
    {
        float innerRatio = Mathf.Clamp(backstepInnerDistanceRatio, 0.1f, 1f);
        float attackDistance = ResolveAttackDecisionDistance();
        float triggerDistance = backstepTriggerDistance > 0.01f ? backstepTriggerDistance : attackDistance * innerRatio;
        return Mathf.Min(triggerDistance, attackDistance * innerRatio, MaxReactiveBackstepDistance);
    }

    void UpdateClosePressureState(float distanceToPlayer)
    {
        if (playerTarget == null || distanceToPlayer > ResolveBackstepDecisionDistance())
        {
            _closePressureStartedAt = float.NegativeInfinity;
            return;
        }

        if (float.IsNegativeInfinity(_closePressureStartedAt))
            _closePressureStartedAt = Time.time;
    }

    bool HasSatisfiedBackstepPressureHold()
    {
        return backstepPressureHoldTime <= 0.001f ||
               (!float.IsNegativeInfinity(_closePressureStartedAt) &&
                Time.time - _closePressureStartedAt >= backstepPressureHoldTime);
    }

    bool IsPlayerInsideBackstepFrontArc()
    {
        if (playerTarget == null)
            return false;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.0001f)
            return false;

        return Vector3.Dot(transform.forward, toPlayer.normalized) >= backstepFrontArcDot;
    }

    bool HasBackstepClearance()
    {
        if (!useCollisionAwareMovement)
            return true;

        float requestedDistance = backstepSpeed > 0f && backstepDuration > 0.0001f
            ? backstepSpeed * backstepDuration
            : 0f;

        if (requestedDistance <= 0.05f)
            return true;

        Vector3 awayFromPlayer = transform.position - playerTarget.position;
        awayFromPlayer.y = 0f;
        if (awayFromPlayer.sqrMagnitude <= 0.0001f)
            awayFromPlayer = -transform.forward;
        else
            awayFromPlayer.Normalize();

        Vector3 currentPos = rb != null ? rb.position : transform.position;
        float clearance = MeasureMovementClearance(currentPos, awayFromPlayer, requestedDistance);
        return clearance >= requestedDistance * Mathf.Clamp01(minimumBackstepClearanceRatio);
    }

    bool ShouldTriggerBackstep(float distanceToPlayer)
    {
        if (!useBackstepWhenTooClose || playerTarget == null)
            return false;

        if (Time.time < _backstepCooldownTimer)
            return false;

        if (distanceToPlayer > ResolveBackstepDecisionDistance())
            return false;

        if (!HasSatisfiedBackstepPressureHold())
            return false;

        if (!IsPlayerInsideBackstepFrontArc())
            return false;

        return HasBackstepClearance();
    }

    void RefreshMoveArcDirection()
    {
        if (Time.time < _moveArcSideUntil)
            return;

        _moveArcSign = _moveArcSign >= 0f ? -1f : 1f;
        float holdMin = Mathf.Max(0.1f, moveArcSideHoldRange.x);
        float holdMax = Mathf.Max(holdMin, moveArcSideHoldRange.y);
        _moveArcSideUntil = Time.time + UnityEngine.Random.Range(holdMin, holdMax);
    }

    Vector3 ResolveArcApproachDirection(Vector3 toPlayer)
    {
        if (toPlayer.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        Vector3 forward = toPlayer.normalized;
        RefreshMoveArcDirection();
        Vector3 lateral = Vector3.Cross(Vector3.up, forward) * _moveArcSign;
        Vector3 desired = (forward * Mathf.Max(0.1f, 1f - moveArcStrength)) + (lateral * Mathf.Clamp01(moveArcStrength));
        desired.y = 0f;
        return desired.sqrMagnitude <= 0.0001f ? forward : desired.normalized;
    }

    // ==================== 상태별 코루틴 ====================

    IEnumerator Co_HandleIntroIdle()
    {
        float t = introIdleDuration;
        while (t > 0f && !_isDead)
        {
            t -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        if (!_isDead)
            SetState(BossState.Detect);
    }

    IEnumerator Co_HandleDetect()
    {
        yield return null;
        EnsureGameplayPlayerTarget();

        if (playerTarget == null)
        {
            UpdateMoveAnimation(0f);
            yield break;
        }

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        UpdateClosePressureState(distance);
        UpdateMoveAnimation(0f);

        float attackDistance = ResolveAttackDecisionDistance();
        float pressureDistance = ResolvePressureEngageDistance();
        if (TryStartDirectSwordWaveAttack(distance, "Detect"))
            yield break;

        bool canRequestAttack = CanRequestAttackSelection();
        bool hasPatternAtDistance = canRequestAttack && distance > attackDistance && HasAttackPlanAtDistance(distance);

        if (canRequestAttack && distance <= attackDistance)
            SetState(BossState.Attack);
        else if (canRequestAttack && hasPatternAtDistance)
            SetState(BossState.Attack);
        else if (canRequestAttack && ShouldForceNoPatternFallbackAttack(distance, hasPatternAtDistance))
            SetState(BossState.Attack);
        else if (distance <= pressureDistance)
            SetState(BossState.CombatIdle);
        else
            SetState(BossState.Move);
    }

    IEnumerator Co_HandleMove()
    {
        LogState("[BossFSM] Move: 플레이어에게 접근 시작");

        while (currentState == BossState.Move && !_isDead)
        {
            EnsureGameplayPlayerTarget();

            if (playerTarget == null)
            {
                UpdateMoveAnimation(0f);
                yield break;
            }

            Vector3 toPlayer = playerTarget.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            UpdateClosePressureState(distance);

            if (TryStartDirectSwordWaveAttack(distance, "Move"))
                yield break;

            bool canRequestAttack = CanRequestAttackSelection();
            float attackDecisionDistance = ResolveAttackDecisionDistance();
            if (canRequestAttack && distance <= attackDecisionDistance)
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            bool hasPatternAtDistance = canRequestAttack && HasAttackPlanAtDistance(distance);
            if (canRequestAttack && hasPatternAtDistance)
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            if (canRequestAttack && ShouldForceNoPatternFallbackAttack(distance, hasPatternAtDistance))
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            if (distance <= ResolvePressureEngageDistance())
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.CombatIdle);
                yield break;
            }

            Vector3 dir = GetChaseDirection(toPlayer);
            if (distance <= ResolvePressureReleaseDistance())
                dir = ResolveArcApproachDirection(toPlayer);
            if (RefreshArenaAwareness().bossFarFromCenter)
                dir = ResolveArenaCenterReturnDirection(dir);

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                float turnSpeed = ResolveChaseTurnSpeed(distance);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, turnSpeed * Time.deltaTime);
            }

            float moveSpeedMultiplier = ResolveApproachSpeedMultiplier(distance);
            Vector3 delta = dir * (moveSpeed * moveSpeedMultiplier * Time.deltaTime);
            ApplyMovementDelta(delta);

            UpdateMoveAnimation(moveSpeedMultiplier, dir, false);
            yield return null;
        }
    }

    IEnumerator Co_CombatIdle()
    {
        float t = _pendingCombatIdleDuration > 0.01f ? _pendingCombatIdleDuration : combatIdleTime;
        _pendingCombatIdleDuration = -1f;

        if (ShouldTriggerCombatIdleRetreat())
        {
            if (combatIdlePreRetreatPause > 0.01f)
                yield return StartCoroutine(Co_CombatIdlePause(Mathf.Min(combatIdlePreRetreatPause, MaxCombatIdleRetreatPause)));

            yield return StartCoroutine(Co_CombatIdleRetreat());

            if (combatIdlePostRetreatPause > 0.01f)
                yield return StartCoroutine(Co_CombatIdlePause(Mathf.Min(combatIdlePostRetreatPause, MaxCombatIdleRetreatPause)));
        }

        t = Mathf.Min(Mathf.Max(t, combatIdleTime), ResolveCombatIdleAttackRetryDelay());
        float elapsed = 0f;
        while (t > 0f && !_isDead)
        {
            EnsureGameplayPlayerTarget();
            if (playerTarget != null)
            {
                float distance = Vector3.Distance(transform.position, playerTarget.position);
                if (TryStartDirectSwordWaveAttack(distance, "CombatIdle"))
                    yield break;

                bool canRequestAttack = CanRequestAttackSelection();
                bool hasPatternAtDistance = false;
                if (canRequestAttack && elapsed >= CombatIdleAttackRequestDelay)
                    hasPatternAtDistance = HasAttackPlanAtDistance(distance);

                if (canRequestAttack && hasPatternAtDistance)
                {
                    UpdateMoveAnimation(0f);
                    SetState(BossState.Attack);
                    yield break;
                }

                if (canRequestAttack && ShouldForceNoPatternFallbackAttack(distance, hasPatternAtDistance))
                {
                    UpdateMoveAnimation(0f);
                    SetState(BossState.Attack);
                    yield break;
                }
            }

            float deltaTime = Time.deltaTime;
            t -= deltaTime;
            elapsed += deltaTime;
            if (!TryApplyCombatIdleStrafe())
                UpdateMoveAnimation(0f);
            yield return null;
        }

        ClearPunishWindow();

        if (!_isDead)
            SetState(BossState.Detect);
    }

    float ResolveCombatIdleAttackRetryDelay()
    {
        float configuredDelay = minimumPostAttackIdleDuration > 0.01f
            ? minimumPostAttackIdleDuration
            : combatIdleTime;

        float minimumDelay = Mathf.Min(Mathf.Max(0.15f, combatIdleTime), MaxCombatIdleAttackRetryDelay);
        return Mathf.Clamp(configuredDelay, minimumDelay, MaxCombatIdleAttackRetryDelay);
    }

    IEnumerator Co_CombatIdlePause(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && !_isDead)
        {
            RotateTowardsPlayer(Time.deltaTime, Mathf.Max(180f, combatIdleFacingTurnSpeed));
            UpdateMoveAnimation(0f);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator Co_HandleBreak()
    {
        LogState("[BossFSM] Break: 브레이크 상태 진입");
        while (currentState == BossState.Break && !_isDead)
        {
            UpdateMoveAnimation(0f);
            yield return null;
        }
    }

    /// <summary>
    /// 공격 상태 진입 시: (회전 보정) → 근접이면 백스텝, 아니면 일반 공격 패턴 수행
    /// </summary>
    IEnumerator Co_PerformAttack()
    {
        _noPatternFallbackReadySince = float.NegativeInfinity;

        float distance = 0f;
        EnsureGameplayPlayerTarget();

        if (playerTarget == null)
        {
            SetState(BossState.CombatIdle);
            yield break;
        }

        if (Time.time < _phaseTransitionLockUntilTime)
        {
            ClearQueuedFollowUp();
            UpdateMoveAnimation(0f);
            SetState(BossState.CombatIdle);
            yield break;
        }

        UpdateMoveAnimation(0f);
        bool isQueuedFollowUp = _queuedFollowUpPattern != null;

        if (isQueuedFollowUp && _queuedFollowUpDelay > 0.01f)
        {
            float wait = _queuedFollowUpDelay;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
        }

        // 0) 공격 시작 전에 플레이어 쪽으로 회전 보정
        distance = Vector3.Distance(transform.position, playerTarget.position);
        UpdateClosePressureState(distance);

        if (!isQueuedFollowUp && ShouldTriggerBackstep(distance))
        {
            _closePressureStartedAt = float.NegativeInfinity;
            yield return StartCoroutine(Co_Backstep());

            _backstepCooldownTimer = Time.time + Mathf.Max(backstepCooldown, MinReactiveBackstepCooldown);

            if (!_isDead)
                SetState(BossState.CombatIdle);

            yield break;
        }

        _closePressureStartedAt = float.NegativeInfinity;

        if (snapRotationToPlayerOnAttack)
        {
            if (preAttackRotateTime > 0f)
                yield return StartCoroutine(Co_FacePlayerShort(preAttackRotateTime, preAttackRotateSpeed));
            else
                FaceToPlayerInstant();
        }

        if (allPatterns == null || allPatterns.Count == 0)
        {
            SetState(BossState.CombatIdle);
            yield break;
        }

        AttackPattern pattern = _pendingImmediateAttackPattern != null
            ? _pendingImmediateAttackPattern
            : (_queuedFollowUpPattern != null ? _queuedFollowUpPattern : SelectPattern(distance));
        _pendingImmediateAttackPattern = null;
        _queuedFollowUpPattern = null;
        _queuedFollowUpDelay = 0f;
        bool usedEngage = false;
        BossPatternEngageMode usedEngageMode = BossPatternEngageMode.None;

        if (pattern == null &&
            !isQueuedFollowUp &&
            TrySelectEngagePattern(distance, out AttackPattern engagePattern, out BossPatternData engageData))
        {
            yield return StartCoroutine(Co_EngagePatternUntilInRange(engagePattern, engageData));
            if (_engageSucceeded)
            {
                pattern = _engageResolvedPattern;
                usedEngage = true;
                usedEngageMode = engageData.engageMode;
                distance = playerTarget != null
                    ? Vector3.Distance(transform.position, playerTarget.position)
                    : float.PositiveInfinity;
            }
        }

        if (pattern == null)
        {
            LogStateWarning("[BossFSM] 사용할 수 있는 패턴이 없음 → CombatIdle");
            BlockAttackSelectionRetry();
            SetState(distance > ResolveAttackDecisionDistance() ? BossState.Move : BossState.CombatIdle);
            yield break;
        }

        if (!isQueuedFollowUp)
            _followUpChainDepth = 0;

        _currentPattern         = pattern;
        _lastExecutedPattern    = pattern.patternName;
        RecordPatternHistory(pattern);
        StartPatternCooldown(pattern, Time.time);
        MarkPatternSelectorUsed(pattern, Time.time);
        AttackTelegraphType telegraphType = pattern.ResolveTelegraphType();
        float telegraphLeadTime = Mathf.Max(
            ResolveMinimumTelegraphLeadTime(pattern, isQueuedFollowUp),
            pattern.ResolveTelegraphLeadTime() * ResolvePhaseTelegraphScale() * ResolvePhaseTelegraphDurationMultiplier(pattern));
        string telegraphLabel = pattern.ResolveTelegraphLabel();
        AttackTimingStyle timingStyle = pattern.ResolveTimingStyle();
        float extraHoldDelay = pattern.ResolveExtraHoldDelay();
        string secondaryTelegraphLabel = pattern.ResolveSecondaryTelegraphLabel();
        bool canParry = pattern.ResolveCanParry();
        bool canPerfectDodge = pattern.ResolveCanPerfectDodge();
        bool canGuard = pattern.ResolveCanGuard();
        bool isUnblockable = pattern.ResolveIsUnblockable();
        bool causesGuardBreak = pattern.ResolveCausesGuardBreak();
        float recoveryTime = ResolvePatternRecoveryTime(pattern, isQueuedFollowUp);
        float punishWindowDuration = ResolvePatternPunishWindow(pattern, recoveryTime, isQueuedFollowUp);
        float punishDamageMultiplier = pattern.ResolvePunishDamageMultiplier();
        float preAttackPoseDuration = ResolvePatternPreAttackPoseDuration(pattern);
        string preAttackPoseTrigger = ResolvePatternPreAttackPoseTriggerName(pattern);
        string preAttackPoseLabel = ResolvePatternPreAttackPoseLabel(pattern);

        if (isQueuedFollowUp)
        {
            telegraphLeadTime = Mathf.Max(
                ResolveMinimumTelegraphLeadTime(pattern, true),
                telegraphLeadTime * FollowUpTelegraphLeadScale);
            telegraphLabel = $"{FollowUpTelegraphPrefix} {telegraphLabel}";
            secondaryTelegraphLabel = $"{FollowUpTelegraphPrefix} {secondaryTelegraphLabel}";
        }

        float totalTelegraphDuration = telegraphLeadTime;
        if ((timingStyle == AttackTimingStyle.Delayed || timingStyle == AttackTimingStyle.FakeOut) && extraHoldDelay > 0.01f)
            totalTelegraphDuration += extraHoldDelay;
        if (preAttackPoseDuration > 0.01f)
            totalTelegraphDuration += preAttackPoseDuration;

        if (groundTelegraph != null && attackHitbox != null && attackHitbox.Collider != null)
            groundTelegraph.ShowCue(attackHitbox.Collider, telegraphType, totalTelegraphDuration, transform);

        if (patternVisuals != null)
            patternVisuals.StartVisualCue(telegraphType, telegraphLeadTime);

        OnAttackTelegraph?.Invoke(telegraphType, telegraphLeadTime, telegraphLabel);

        if (telegraphLeadTime > 0.01f)
        {
            float wait = telegraphLeadTime;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
        }

        if (timingStyle == AttackTimingStyle.Delayed && extraHoldDelay > 0.01f)
        {
            float wait = extraHoldDelay;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
        }
        else if (timingStyle == AttackTimingStyle.FakeOut && extraHoldDelay > 0.01f)
        {
            if (patternVisuals != null)
                patternVisuals.StartVisualCue(telegraphType, extraHoldDelay);

            OnAttackTelegraph?.Invoke(telegraphType, extraHoldDelay, secondaryTelegraphLabel);
            float wait = extraHoldDelay;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
        }

        if (preAttackPoseDuration > 0.01f)
        {
            if (patternVisuals != null)
                patternVisuals.StartVisualCue(telegraphType, preAttackPoseDuration);

            OnAttackTelegraph?.Invoke(telegraphType, preAttackPoseDuration, preAttackPoseLabel);

            AnimationClip preAttackPoseClip = ResolvePreAttackPoseClip();
            if (preAttackPoseClip != null && bossAnimator != null)
            {
                yield return Co_PlayPreAttackPoseClip(preAttackPoseClip, preAttackPoseDuration);
            }
            else
            {
                if (!string.IsNullOrEmpty(preAttackPoseTrigger))
                    PlayAnimTrigger(preAttackPoseTrigger);

                float wait = preAttackPoseDuration;
                while (wait > 0f)
                {
                    wait -= Time.deltaTime;
                    yield return null;
                }
            }
        }

        HideGroundTelegraph();

        ApplyPatternHitboxTuning(pattern);

        if (attackHitbox != null)
        {
            float resolvedDamage = Mathf.Max(0f, pattern.damageAmount * Mathf.Max(0f, outgoingDamageMultiplier) * ResolvePhaseDamageMultiplier(pattern) * ResolveDifficultyDamageMultiplier());
            attackHitbox.canPerfectDodge = canPerfectDodge;
            attackHitbox.Configure(resolvedDamage, canParry, canGuard, isUnblockable, causesGuardBreak, transform);
        }

        bool hasTimingData = TryResolveAttackTimingData(pattern, out BossAttackTimingData timingData);
        if (hasTimingData)
        {
            ApplyPhaseAttackTimingModifiers(pattern, ref timingData);
            ApplyDifficultyAttackTimingModifiers(ref timingData);
        }

        PublishPatternTelemetry(
            pattern,
            distance,
            Mathf.Abs(ResolveAngleToPlayer()),
            isQueuedFollowUp,
            usedEngage,
            usedEngageMode,
            hasTimingData ? timingData.telegraphType : BossAttackTelegraphType.None);

        if (hasTimingData && timingData.recoveryDuration > 0.001f)
        {
            recoveryTime = timingData.recoveryDuration;
            punishWindowDuration = ResolvePatternPunishWindow(pattern, recoveryTime, isQueuedFollowUp);
        }

        _attackTimingTelegraphLabel = hasTimingData
            ? ResolveAttackTimingTelegraphLabel(timingData, telegraphLabel)
            : string.Empty;

        if (!string.IsNullOrEmpty(pattern.animTriggerName))
            PlayAnimTrigger(pattern.animTriggerName);

        if (hasTimingData)
            StartAttackTimingController(timingData);

        if (pattern.ResolveFiresSwordWaveProjectile())
        {
            float projectileDamage = Mathf.Max(0f, pattern.damageAmount * Mathf.Max(0f, outgoingDamageMultiplier) * ResolvePhaseDamageMultiplier(pattern) * ResolveDifficultyDamageMultiplier());
            StartCoroutine(Co_FireSwordWaveProjectile(pattern, projectileDamage, canParry, canPerfectDodge, canGuard, isUnblockable, causesGuardBreak));
        }

        if (enableStateLogs)
            LogState($"[BossFSM] Attack 패턴 실행: {pattern.patternName} ({pattern.animTriggerName})");

        // 이름 기반 스테이트 대기가 아니라,
        // "Attack 상태 + 최대 대기 시간" 기준으로만 기다리는 방식 (안전장치).
        float maxWait = Mathf.Max(0.1f, maxAttackStateWaitTime);
        float elapsed = 0f;

        while (elapsed < maxWait && currentState == BossState.Attack && !_isDead)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (currentState == BossState.Attack && !_isDead)
        {
            if (TryQueueFollowUp(pattern, playerTarget != null ? Vector3.Distance(transform.position, playerTarget.position) : float.PositiveInfinity))
            {
                StopAttackTimingController();
                _currentPattern = null;
                SetState(BossState.Attack, true);
            }
            else
            {
                StopAttackTimingController();
                StageAttackRecovery(pattern.patternName, recoveryTime, punishWindowDuration, punishDamageMultiplier);
                yield return StartCoroutine(Co_ExecutePatternPostAction(pattern));
                EnterCombatIdleFromCurrentPattern();
            }
        }
    }

    IEnumerator Co_ExecutePatternPostAction(AttackPattern pattern)
    {
        BossPatternPostActionType actionType = ResolvePostActionType(pattern);
        actionType = ResolveRhythmSafePostAction(actionType);
        if (actionType == BossPatternPostActionType.None)
            yield break;

        if (!CanRunPatternPostAction())
            yield break;

        if (_postActionExecutor == null)
            _postActionExecutor = new BossPostActionExecutor();

        _isExecutingPostAction = true;

        BossPostActionContext context = new BossPostActionContext
        {
            bossTransform = transform,
            targetTransform = playerTarget,
            currentDistance = playerTarget != null ? Vector3.Distance(transform.position, playerTarget.position) : float.PositiveInfinity,
            currentAngle = ResolveAngleToPlayer(),
            currentTime = Time.time,
            canMove = true,
            targetValid = IsValidGameplayPlayerTarget(playerTarget)
        };

        try
        {
            BossPostActionSettings difficultyPostActionSettings = ResolveDifficultyPostActionSettings(postActionSettings);
            yield return StartCoroutine(_postActionExecutor.Execute(
                actionType,
                context,
                difficultyPostActionSettings,
                ApplyPressureMovementDelta,
                UpdateMoveAnimation,
                CanContinuePatternPostAction,
                enablePostActionDebugLog,
                this));
        }
        finally
        {
            _isExecutingPostAction = false;
            UpdateMoveAnimation(0f);
            RecordExecutedPostAction(actionType);
        }
    }

    BossPatternPostActionType ResolvePostActionType(AttackPattern pattern)
    {
        if (pattern == null)
            return BossPatternPostActionType.None;

        BossPatternData data = BuildSelectorData(pattern);
        return data.postActionType;
    }

    BossPatternPostActionType ResolveRhythmSafePostAction(BossPatternPostActionType actionType)
    {
        if (!IsMovementPostAction(actionType))
            return actionType;

        if (_consecutiveMovementPostActionCount < MaxConsecutiveMovementPostActions)
            return actionType;

        return BossPatternPostActionType.Recenter;
    }

    BossPostActionSettings ResolveDifficultyPostActionSettings(BossPostActionSettings settings)
    {
        float multiplier = ResolveDifficultyPostActionDurationMultiplier();
        settings.combatIdleMinTime = Mathf.Max(0f, settings.combatIdleMinTime * multiplier);
        settings.combatIdleMaxTime = Mathf.Max(settings.combatIdleMinTime, settings.combatIdleMaxTime * multiplier);
        settings.backstepDuration = Mathf.Max(0.01f, settings.backstepDuration * multiplier);
        settings.strafeDuration = Mathf.Max(0.01f, settings.strafeDuration * multiplier);
        settings.chaseMaxDuration = Mathf.Max(0.05f, settings.chaseMaxDuration * multiplier);
        settings.recenterDuration = Mathf.Max(0.01f, settings.recenterDuration * multiplier);
        return settings;
    }

    void RecordExecutedPostAction(BossPatternPostActionType actionType)
    {
        if (IsMovementPostAction(actionType))
        {
            _consecutiveMovementPostActionCount = IsMovementPostAction(_lastExecutedPostActionType)
                ? _consecutiveMovementPostActionCount + 1
                : 1;
        }
        else
        {
            _consecutiveMovementPostActionCount = 0;
        }

        _lastExecutedPostActionType = actionType;
    }

    static bool IsMovementPostAction(BossPatternPostActionType actionType)
    {
        return actionType == BossPatternPostActionType.Backstep ||
               actionType == BossPatternPostActionType.StrafeLeft ||
               actionType == BossPatternPostActionType.StrafeRight ||
               actionType == BossPatternPostActionType.ChaseReposition;
    }

    bool CanRunPatternPostAction()
    {
        return !_isDead &&
               !_isUltimateVictim &&
               !IsCombatRecoveryActive &&
               !_externalIntroPaused &&
               currentState == BossState.Attack &&
               playerTarget != null &&
               IsValidGameplayPlayerTarget(playerTarget) &&
               _parryStunRoutine == null &&
               (bossHealth == null || !bossHealth.IsStaggered) &&
               (breakController == null || !breakController.IsInBreak);
    }

    bool CanContinuePatternPostAction()
    {
        return CanRunPatternPostAction();
    }

    bool TrySelectEngagePattern(float distanceToPlayer, out AttackPattern selectedPattern, out BossPatternData selectedData)
    {
        selectedPattern = null;
        selectedData = default;

        if (allPatterns == null || allPatterns.Count == 0)
            return false;

        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            BossPatternData data = BuildSelectorData(pattern);
            if (!CanConsiderPatternForEngage(pattern, data))
                continue;

            if (data.engageMode == BossPatternEngageMode.None ||
                data.engageMode == BossPatternEngageMode.RequireInRange)
                continue;

            float rangeGap = GetPatternRangeGap(distanceToPlayer, pattern);
            if (rangeGap <= 0.001f || distanceToPlayer < pattern.minRange)
                continue;

            if (distanceToPlayer > data.engageStartRange)
                continue;

            float score = rangeGap / Mathf.Max(0.01f, data.baseWeight);
            if (score >= bestScore)
                continue;

            bestScore = score;
            selectedPattern = pattern;
            selectedData = data;
        }

        if (selectedPattern != null)
            return true;

        if (TryGetExecutablePatternById(BossPatternId.SwordWave, distanceToPlayer, true, out selectedPattern))
        {
            selectedData = BuildSelectorData(selectedPattern);
            return true;
        }

        return false;
    }

    bool CanConsiderPatternForEngage(AttackPattern pattern, BossPatternData data)
    {
        if (pattern == null || pattern.currentCooldown > 0f)
            return false;

        if (data.patternId == BossPatternId.None)
            return false;

        if (!pattern.IsAvailableInPhase(CurrentPhase) ||
            CurrentPhase < data.minPhase ||
            CurrentPhase > data.maxPhase)
            return false;

        if (!string.IsNullOrEmpty(pattern.forbiddenAfter) &&
            _lastExecutedPattern.Equals(pattern.forbiddenAfter, StringComparison.Ordinal))
            return false;

        float angle = Mathf.Abs(ResolveAngleToPlayer());
        if (angle < data.minAngle || angle > data.maxAngle)
            return false;

        if (!data.canRepeat && IsPatternRecentlyBlocked(pattern, data))
            return false;

        return true;
    }

    IEnumerator Co_EngagePatternUntilInRange(AttackPattern pattern, BossPatternData data)
    {
        _engageSucceeded = false;
        _engageResolvedPattern = null;

        if (pattern == null || playerTarget == null || !CanContinueEngage())
            yield break;

        if (pattern.CanExecute(this, Vector3.Distance(transform.position, playerTarget.position), _lastExecutedPattern))
        {
            _engageSucceeded = true;
            _engageResolvedPattern = pattern;
            yield break;
        }

        if (TryResolveImmediateEngageFallback(data, out AttackPattern fallbackPattern))
        {
            _engageSucceeded = true;
            _engageResolvedPattern = fallbackPattern;
            yield break;
        }

        if (data.engageMode != BossPatternEngageMode.ChaseUntilInRange &&
            data.engageMode != BossPatternEngageMode.DashEngage)
            yield break;

        LogEngageStart(data.patternId);
        _isEngaging = true;

        float elapsed = 0f;
        float maxDuration = Mathf.Max(0.05f, data.engageMaxDuration);
        float stopRange = Mathf.Clamp(data.engageStopRange, pattern.minRange, pattern.maxRange);
        float speedMultiplier = Mathf.Clamp(data.engageMoveSpeedMultiplier, 0.15f, 2.5f);
        float progressCheckElapsed = 0f;
        float stalledElapsed = 0f;
        float stallCheckInterval = Mathf.Max(0.05f, engageStallCheckInterval);
        float minProgressSqr = Mathf.Max(0.01f, engageMinProgressDistance);
        minProgressSqr *= minProgressSqr;
        float abortStallTime = Mathf.Max(stallCheckInterval, engageBlockedAbortTime);
        Vector3 lastProgressPosition = transform.position;

        while (elapsed < maxDuration && CanContinueEngage())
        {
            float distance = Vector3.Distance(transform.position, playerTarget.position);
            if (pattern.CanExecute(this, distance, _lastExecutedPattern))
                break;

            if (distance <= stopRange || distance < pattern.minRange)
                break;

            Vector3 toTarget = playerTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
                break;

            Vector3 direction = GetChaseDirection(toTarget);
            if (direction.sqrMagnitude <= 0.0001f)
                break;

            Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                look,
                Mathf.Max(closeChaseTurnSpeed, chaseTurnSpeed) * Time.deltaTime);

            ApplyPressureMovementDelta(direction * (moveSpeed * speedMultiplier * Time.deltaTime));
            UpdateMoveAnimation(speedMultiplier, direction, false);

            progressCheckElapsed += Time.deltaTime;
            if (progressCheckElapsed >= stallCheckInterval)
            {
                Vector3 progress = transform.position - lastProgressPosition;
                progress.y = 0f;

                if (progress.sqrMagnitude < minProgressSqr)
                {
                    stalledElapsed += progressCheckElapsed;
                    if (stalledElapsed >= abortStallTime)
                        break;
                }
                else
                {
                    stalledElapsed = 0f;
                    lastProgressPosition = transform.position;
                }

                progressCheckElapsed = 0f;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        UpdateMoveAnimation(0f);
        _isEngaging = false;

        if (CanContinueEngage())
        {
            float finalDistance = Vector3.Distance(transform.position, playerTarget.position);
            if (pattern.CanExecute(this, finalDistance, _lastExecutedPattern))
            {
                _engageSucceeded = true;
                _engageResolvedPattern = pattern;
                LogEngageSuccess(data.patternId);
                yield break;
            }
        }

        LogEngageFail(data.patternId);
    }

    bool TryResolveImmediateEngageFallback(BossPatternData data, out AttackPattern pattern)
    {
        pattern = null;
        if (playerTarget == null)
            return false;

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        if (data.engageMode == BossPatternEngageMode.UseRangedFallback &&
            TryGetExecutablePatternById(data.rangedFallbackPatternId, distance, true, out pattern))
            return true;

        return data.engageMode == BossPatternEngageMode.DashEngage &&
               TryGetExecutablePatternById(data.dashFallbackPatternId, distance, true, out pattern);
    }

    bool TryGetExecutablePatternById(
        BossPatternId patternId,
        float distanceToPlayer,
        bool requireSelectorNonRangeRules,
        out AttackPattern pattern)
    {
        pattern = null;
        if (patternId == BossPatternId.None || allPatterns == null)
            return false;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern candidate = allPatterns[i];
            if (candidate == null)
                continue;

            BossPatternData data = BuildSelectorData(candidate);
            if (data.patternId != patternId)
                continue;

            if (requireSelectorNonRangeRules && !CanPassSelectorNonRangeRules(candidate, data))
                continue;

            if (!candidate.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                continue;

            pattern = candidate;
            return true;
        }

        return false;
    }

    bool CanPassSelectorNonRangeRules(AttackPattern pattern, BossPatternData data)
    {
        if (pattern == null || data.patternId == BossPatternId.None)
            return false;

        if (pattern.currentCooldown > 0f)
            return false;

        if (!pattern.IsAvailableInPhase(CurrentPhase) ||
            CurrentPhase < data.minPhase ||
            CurrentPhase > data.maxPhase)
            return false;

        if (!string.IsNullOrEmpty(pattern.forbiddenAfter) &&
            _lastExecutedPattern.Equals(pattern.forbiddenAfter, StringComparison.Ordinal))
            return false;

        float angle = Mathf.Abs(ResolveAngleToPlayer());
        if (angle < data.minAngle || angle > data.maxAngle)
            return false;

        return data.canRepeat || !IsPatternRecentlyBlocked(pattern, data);
    }

    bool IsPatternRecentlyBlocked(AttackPattern pattern, BossPatternData data)
    {
        int lookback = Mathf.Clamp(data.recentRepeatBlockCount, 1, 8);
        if (patternSelectorHistory != null &&
            patternSelectorHistory.WasUsedRecently(data.patternId, lookback))
            return true;

        return WasPatternUsedRecently(pattern != null ? pattern.patternName : null, lookback);
    }

    bool CanContinueEngage()
    {
        return !_isDead &&
               !_isUltimateVictim &&
               !IsCombatRecoveryActive &&
               !_externalIntroPaused &&
               currentState == BossState.Attack &&
               playerTarget != null &&
               IsValidGameplayPlayerTarget(playerTarget) &&
               _parryStunRoutine == null &&
               (bossHealth == null || !bossHealth.IsStaggered) &&
               (breakController == null || !breakController.IsInBreak);
    }

    void LogEngageStart(BossPatternId patternId)
    {
        if (!enableEngageDebugLog)
            return;

        Debug.Log("[BossEngage] start " + patternId, this);
    }

    void LogEngageSuccess(BossPatternId patternId)
    {
        if (!enableEngageDebugLog)
            return;

        Debug.Log("[BossEngage] success " + patternId, this);
    }

    void LogEngageFail(BossPatternId patternId)
    {
        if (!enableEngageDebugLog)
            return;

        Debug.Log("[BossEngage] fail " + patternId, this);
    }

    void StageAttackRecovery(string patternName, float recoveryTime, float punishWindowDuration, float punishDamageMultiplier)
    {
        _pendingCombatIdleDuration = Mathf.Max(0f, recoveryTime);
        _punishDamageMultiplier = Mathf.Max(1f, punishDamageMultiplier);
        _punishSourcePattern = patternName ?? string.Empty;

        float clampedPunishWindow = Mathf.Max(0f, punishWindowDuration);
        if (_punishDamageMultiplier <= 1.001f || clampedPunishWindow <= 0.01f)
        {
            _punishWindowUntilTime = float.NegativeInfinity;
            return;
        }

        float actualWindow = _pendingCombatIdleDuration > 0.01f
            ? Mathf.Min(_pendingCombatIdleDuration, clampedPunishWindow)
            : clampedPunishWindow;

        _punishWindowUntilTime = Time.time + actualWindow;

        if (patternVisuals != null)
            patternVisuals.StartPunishCue(actualWindow);

        OnPunishWindowOpened?.Invoke(actualWindow, _punishDamageMultiplier, _punishSourcePattern);
    }

    void EnterCombatIdleFromCurrentPattern()
    {
        _currentPattern = null;
        SetState(BossState.CombatIdle);
    }

    float ResolvePatternRecoveryTime(AttackPattern pattern, bool isQueuedFollowUp)
    {
        if (pattern == null)
            return Mathf.Max(combatIdleTime, minimumPostAttackIdleDuration);

        float recovery = Mathf.Max(0f, pattern.ResolveRecoveryTime());
        if (isQueuedFollowUp)
            recovery += Mathf.Max(0f, followUpRecoveryTax);

        recovery *= ResolvePhaseRecoveryDurationMultiplier(pattern);
        recovery += Mathf.Max(0f, globalPostAttackRecoveryPadding);
        return Mathf.Max(Mathf.Max(combatIdleTime, minimumPostAttackIdleDuration), recovery);
    }

    float ResolveMinimumPunishWindow(AttackPattern pattern, bool isQueuedFollowUp)
    {
        if (pattern == null)
            return 0f;

        float minimum;
        switch (pattern.ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                minimum = minimumParryPunishWindow;
                break;
            case AttackTelegraphType.Guard:
                minimum = minimumGuardPunishWindow;
                break;
            case AttackTelegraphType.Danger:
                minimum = minimumDangerPunishWindow;
                break;
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Auto:
            default:
                minimum = minimumDodgePunishWindow;
                break;
        }

        if (pattern.ResolveTimingStyle() == AttackTimingStyle.FakeOut)
            minimum = Mathf.Max(minimum, 0.34f);

        if (isQueuedFollowUp)
            minimum = Mathf.Max(minimum, 0.26f + (Mathf.Max(0f, followUpRecoveryTax) * 0.45f));

        return Mathf.Max(0f, minimum);
    }

    float ResolvePatternPunishWindow(AttackPattern pattern, float recoveryTime, bool isQueuedFollowUp)
    {
        if (pattern == null)
            return Mathf.Max(0f, recoveryTime);

        float punishWindow = Mathf.Max(pattern.ResolvePunishWindowDuration(), ResolveMinimumPunishWindow(pattern, isQueuedFollowUp));
        if (recoveryTime > 0.01f)
            return Mathf.Min(recoveryTime, punishWindow);

        return punishWindow;
    }

    bool TryApplyCombatIdleStrafe()
    {
        if (!useCombatIdleStrafe)
            return false;

        EnsureGameplayPlayerTarget();
        if (playerTarget == null)
            return false;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance <= 0.001f)
            return false;

        Vector3 forward = toPlayer / distance;
        Quaternion look = Quaternion.LookRotation(forward);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            look,
            Mathf.Max(60f, combatIdleFacingTurnSpeed) * Time.deltaTime);

        RefreshCombatIdleStrafeDirection();
        Vector3 lateral = transform.right * _combatIdleStrafeSign;
        float desiredDistance = Mathf.Max(0.5f, combatIdleDesiredDistance);
        float radialError = distance - desiredDistance;
        float tolerance = Mathf.Max(0.05f, combatIdleDistanceTolerance);
        float absRadialError = Mathf.Abs(radialError);
        Vector3 moveDir;
        float speed01;

        if (distance >= Mathf.Max(desiredDistance + tolerance, combatIdleApproachThreshold))
        {
            Vector3 arcApproach = (forward * 0.78f) + (lateral * 0.48f);
            moveDir = arcApproach;
            speed01 = Mathf.Clamp01(combatIdleApproachSpeedMultiplier);
        }
        else
        {
            Vector3 drift = Vector3.zero;
            if (absRadialError > tolerance)
            {
                float correction = Mathf.Clamp01((absRadialError - tolerance) / Mathf.Max(0.1f, desiredDistance));
                drift = -forward * Mathf.Sign(radialError) * correction * Mathf.Max(0f, combatIdleRadialCorrectionWeight);
            }

            moveDir = (lateral * Mathf.Max(0.1f, combatIdleOrbitBias)) + drift;
            speed01 = Mathf.Clamp01(combatIdleStrafeSpeedMultiplier);
        }

        moveDir.y = 0f;
        if (moveDir.sqrMagnitude <= 0.0001f)
        {
            UpdateMoveAnimation(0f);
            return true;
        }

        moveDir.Normalize();
        Vector3 delta = moveDir * (moveSpeed * speed01 * Time.deltaTime);
        ApplyMovementDelta(delta);
        UpdateMoveAnimation(speed01, moveDir, true);
        return true;
    }

    void RefreshCombatIdleStrafeDirection()
    {
        if (Time.time < _combatIdleStrafeSideUntil)
            return;

        bool shouldSwap = _combatIdleStrafeSign == 0f || UnityEngine.Random.value < combatIdleStrafeSideSwapChance;
        if (shouldSwap)
            _combatIdleStrafeSign = _combatIdleStrafeSign >= 0f ? -1f : 1f;

        float holdMin = Mathf.Max(0.1f, combatIdleStrafeSideHoldMin);
        float holdMax = Mathf.Max(holdMin, combatIdleStrafeSideHoldMax);
        _combatIdleStrafeSideUntil = Time.time + UnityEngine.Random.Range(holdMin, holdMax);
    }

    IEnumerator Co_FireSwordWaveProjectile(
        AttackPattern pattern,
        float damage,
        bool canParry,
        bool canPerfectDodge,
        bool canGuard,
        bool isUnblockable,
        bool causesGuardBreak)
    {
        if (pattern == null)
            yield break;

        float delay = Mathf.Max(0f, pattern.swordWaveFireDelay);
        if (delay > 0.001f)
        {
            float wait = delay;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
        }

        if (_externalIntroPaused || _isDead || currentState != BossState.Attack)
            yield break;

        Transform spawnAnchor = null;
        if (_bossReferences != null)
            spawnAnchor = _bossReferences.AttackHitboxSocket != null ? _bossReferences.AttackHitboxSocket : _bossReferences.VfxPivot;

        Vector3 origin = spawnAnchor != null
            ? spawnAnchor.position + (transform.forward * 0.35f)
            : transform.position + (transform.forward * 1.15f) + Vector3.up * 1.15f;

        Vector3 forward = playerTarget != null
            ? (playerTarget.position + Vector3.up * 0.9f) - origin
            : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = transform.forward;

        Quaternion rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        GameObject projectileObject = new GameObject("BossSwordWaveProjectile");
        projectileObject.transform.SetPositionAndRotation(origin, rotation);
        projectileObject.layer = gameObject.layer;

        if (ShouldLogSwordWave())
            Debug.Log($"[BossSwordWave] spawned origin={origin} forward={(rotation * Vector3.forward)} speed={pattern.swordWaveSpeed:0.00}", this);

        BossSwordWaveProjectile projectile = projectileObject.AddComponent<BossSwordWaveProjectile>();
        projectile.Configure(
            transform,
            damage,
            Mathf.Max(0.1f, pattern.swordWaveSpeed),
            Mathf.Max(0.1f, pattern.swordWaveLifeTime),
            Mathf.Max(0.1f, pattern.swordWaveWidth),
            Mathf.Max(0.1f, pattern.swordWaveHeight),
            Mathf.Max(0.2f, pattern.swordWaveLength),
            canParry,
            canPerfectDodge,
            canGuard,
            causesGuardBreak,
            isUnblockable);
    }

    bool ShouldTriggerCombatIdleRetreat()
    {
        if (!useCombatIdleRetreatWhenTooClose || playerTarget == null)
            return false;

        if (Time.time < _backstepCooldownTimer || !HasSatisfiedBackstepPressureHold())
            return false;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance > Mathf.Max(0.25f, combatIdleRetreatTriggerDistance))
            return false;

        if (!useCollisionAwareMovement)
            return true;

        float requestedDistance = Mathf.Max(0f, combatIdleRetreatSpeed) * Mathf.Max(0f, combatIdleRetreatDuration);
        if (requestedDistance <= 0.01f)
            return true;

        Vector3 away = -toPlayer.normalized;
        float clearance = MeasureMovementClearance(rb != null ? rb.position : transform.position, away, requestedDistance);
        return clearance >= requestedDistance * Mathf.Clamp01(minimumBackstepClearanceRatio);
    }

    IEnumerator Co_CombatIdleRetreat()
    {
        if (playerTarget == null)
            yield break;

        FaceToPlayerInstant();

        string retreatTrigger = ResolveBackstepTriggerName(combatIdleRetreatTriggerName);
        if (!string.IsNullOrEmpty(retreatTrigger))
            PlayAnimTrigger(retreatTrigger);

        float elapsed = 0f;
        Vector3 backDir = -transform.forward;
        float totalDistance = 0f;
        if (combatIdleRetreatSpeed > 0f && combatIdleRetreatDuration > 0.0001f)
        {
            float requestedDistance = combatIdleRetreatSpeed * combatIdleRetreatDuration;
            totalDistance = useCollisionAwareMovement
                ? MeasureMovementClearance(rb != null ? rb.position : transform.position, backDir, requestedDistance)
                : requestedDistance;
        }

        while (elapsed < combatIdleRetreatDuration && !_isDead)
        {
            float stepDeltaTime = Time.deltaTime;
            RotateTowardsPlayer(stepDeltaTime, Mathf.Max(180f, combatIdleFacingTurnSpeed));

            if (totalDistance > 0f && combatIdleRetreatDuration > 0.0001f)
            {
                float normalizedStep = Mathf.Clamp01(stepDeltaTime / combatIdleRetreatDuration);
                Vector3 delta = backDir * (totalDistance * normalizedStep);
                ApplyMovementDelta(delta);
            }

            UpdateMoveAnimation(0f);
            elapsed += stepDeltaTime;
            yield return null;
        }

        _backstepCooldownTimer = Time.time + Mathf.Max(backstepCooldown, MinReactiveBackstepCooldown);
    }

    void ClearPunishWindow()
    {
        bool wasActive = IsPunishWindowActive;
        _punishWindowUntilTime = float.NegativeInfinity;
        _punishDamageMultiplier = 1f;
        _punishSourcePattern = string.Empty;

        if (wasActive)
        {
            if (patternVisuals != null)
                patternVisuals.ResetToIdle();
            OnPunishWindowClosed?.Invoke();
        }
    }

    void ClearQueuedFollowUp()
    {
        _pendingImmediateAttackPattern = null;
        _queuedFollowUpPattern = null;
        _queuedFollowUpDelay = 0f;
        _followUpChainDepth = 0;
    }

    bool TryStartCombatRecovery(BossRecoveryReason reason, BossState nextState, bool clearPunishWindow = true)
    {
        if (_isDead || _externalIntroPaused)
            return false;

        if (_combatRecoveryRoutine != null)
            return true;

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

        ReleaseParryStunAnimationFallback();
        _combatRecoveryController ??= new BossCombatRecoveryController();
        _activeCombatRecoveryReason = reason;
        _activeCombatRecoveryStartedAt = Time.time;
        currentState = BossState.Recovery;
        _combatRecoveryRoutine = StartCoroutine(Co_RunCombatRecovery(reason, nextState, clearPunishWindow));
        return true;
    }

    IEnumerator Co_RunCombatRecovery(BossRecoveryReason reason, BossState nextState, bool clearPunishWindow)
    {
        EnsureGameplayPlayerTarget(forceRefresh: true);

        yield return _combatRecoveryController.Execute(
            reason,
            combatRecoverySettings,
            transform,
            playerTarget,
            _combatRecoveryAnchorPosition,
            ShouldAbortCombatRecovery,
            () => AbortAttackExecution(clearPunishWindow),
            GrantRecoveryInvulnerability,
            SetRecoverySuperArmor,
            ApplyPressureMovementDelta,
            UpdateMoveAnimation);

        _combatRecoveryRoutine = null;
        _combatRecoverySuperArmorActive = false;
        _combatRecoveryLockoutUntil = Time.time + Mathf.Max(0f, combatRecoverySettings.lockoutDuration);
        bool aborted = _isDead || _externalIntroPaused;
        PublishCombatRecoveryTelemetry(reason, !aborted, aborted, _activeCombatRecoveryStartedAt);
        _activeCombatRecoveryStartedAt = float.NegativeInfinity;

        if (_isDead)
            yield break;
        if (_externalIntroPaused)
        {
            currentState = BossState.IntroIdle;
            yield break;
        }

        EnsureGameplayPlayerTarget(forceRefresh: true);
        _pendingCombatIdleDuration = Mathf.Max(0f, combatRecoverySettings.postRecoveryCombatIdleDuration);

        if (breakController != null && breakController.IsInBreak)
        {
            SetState(BossState.Break, true);
            yield break;
        }

        if (playerTarget == null || !IsValidGameplayPlayerTarget(playerTarget))
        {
            SetState(BossState.Detect, true);
            yield break;
        }

        SetState(nextState, true);
    }

    bool ShouldAbortCombatRecovery()
    {
        return _isDead || _externalIntroPaused;
    }

    void StopCombatRecovery()
    {
        bool wasRecovering = _combatRecoveryRoutine != null ||
                             (_combatRecoveryController != null && _combatRecoveryController.IsRunning);

        if (wasRecovering)
            PublishCombatRecoveryTelemetry(_activeCombatRecoveryReason, false, true, _activeCombatRecoveryStartedAt);

        if (_combatRecoveryRoutine != null)
        {
            StopCoroutine(_combatRecoveryRoutine);
            _combatRecoveryRoutine = null;
        }

        _combatRecoveryController?.Cancel();
        _combatRecoverySuperArmorActive = false;
        _combatRecoveryLockoutUntil = float.NegativeInfinity;
        _activeCombatRecoveryStartedAt = float.NegativeInfinity;
    }

    void PublishCombatRecoveryTelemetry(
        BossRecoveryReason reason,
        bool completed,
        bool aborted,
        float startedAt)
    {
        BossCombatRecoveryTelemetrySample sample = new BossCombatRecoveryTelemetrySample
        {
            reason = reason,
            phase = _currentPhase,
            bossHpNormalized = ResolveBossHpNormalized(),
            duration = float.IsNegativeInfinity(startedAt) ? 0f : Mathf.Max(0f, Time.time - startedAt),
            completed = completed,
            aborted = aborted,
            targetValid = playerTarget != null && IsValidGameplayPlayerTarget(playerTarget),
            time = Time.time
        };

        OnCombatRecoveryTelemetrySample?.Invoke(sample);

        if (!enableCombatRecoveryTelemetryDebugLog)
            return;

        Debug.Log(
            "[BossCombatRecoveryTelemetry] phase=" + sample.phase +
            " reason=" + sample.reason +
            " duration=" + sample.duration.ToString("0.00") +
            " completed=" + sample.completed +
            " aborted=" + sample.aborted +
            " targetValid=" + sample.targetValid +
            " hp=" + sample.bossHpNormalized.ToString("0.00"),
            this);
    }

    void GrantRecoveryInvulnerability(float duration)
    {
        if (bossHealth != null && duration > 0f)
            bossHealth.SetInvincible(duration);
    }

    void SetRecoverySuperArmor(float duration)
    {
        _combatRecoverySuperArmorActive = duration > 0f;
    }

    void AbortAttackExecution(bool clearPunishWindow)
    {
        _isExecutingPostAction = false;
        _isEngaging = false;
        _engageSucceeded = false;
        _engageResolvedPattern = null;
        StopAttackTimingController();
        StopPreAttackPosePlayback();
        HideGroundTelegraph();
        UpdateMoveAnimation(0f);
        SetCombatStrafeAnimation(false);
        ClearQueuedFollowUp();
        _pendingCombatIdleDuration = -1f;

        if (clearPunishWindow)
            ClearPunishWindow();

        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();

        _currentPattern = null;
    }

    void RefreshPatternCooldownState()
    {
        if (allPatterns == null)
            return;

        float now = Time.time;
        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            pattern.RestoreCooldownAnchor(now);
            pattern.SyncCooldown(now);
        }

        _lastPatternCooldownSyncFrame = Time.frameCount;
    }

    void SyncPatternCooldowns()
    {
        if (allPatterns == null)
            return;

        int currentFrame = Time.frameCount;
        if (_lastPatternCooldownSyncFrame == currentFrame)
            return;

        _lastPatternCooldownSyncFrame = currentFrame;
        float now = Time.time;
        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            pattern.SyncCooldown(now);
        }
    }

    void RebuildPatternLookup()
    {
        _patternLookup.Clear();
        if (allPatterns == null)
            return;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null || string.IsNullOrWhiteSpace(pattern.patternName))
                continue;

            string key = pattern.patternName.Trim();
            if (!_patternLookup.ContainsKey(key))
                _patternLookup.Add(key, pattern);
        }
    }

    void RebuildPatternSelectorCache()
    {
        int count = allPatterns != null ? allPatterns.Count : 0;
        if (_selectorPatternData == null || _selectorPatternData.Length != count)
        {
            _selectorPatternData = new BossPatternData[count];
            _selectorRuntimeStates = new BossPatternRuntimeState[count];
            _selectorPatternLookup = new AttackPattern[count];
        }

        float now = Application.isPlaying ? Time.time : 0f;
        for (int i = 0; i < count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            _selectorPatternLookup[i] = pattern;

            BossPatternData data = BuildSelectorData(pattern);
            _selectorPatternData[i] = data;

            float lastUsedTime = -9999f;
            if (Application.isPlaying && pattern != null && pattern.currentCooldown > 0f && data.cooldown > 0f)
                lastUsedTime = now + pattern.currentCooldown - data.cooldown;

            _selectorRuntimeStates[i] = new BossPatternRuntimeState(data.patternId)
            {
                lastUsedTime = lastUsedTime
            };
        }

        if (patternSelector == null)
            patternSelector = new BossPatternSelector();
        if (patternSelectorHistory == null)
            patternSelectorHistory = new BossPatternHistory();

        patternSelector.Configure(_selectorPatternData, _selectorRuntimeStates, patternSelectorHistory, this);
    }

    void RefreshPatternSelectorCacheData(float now)
    {
        if (_selectorPatternData == null ||
            _selectorRuntimeStates == null ||
            _selectorPatternLookup == null ||
            allPatterns == null)
        {
            return;
        }

        int count = Mathf.Min(allPatterns.Count, _selectorPatternData.Length, _selectorRuntimeStates.Length, _selectorPatternLookup.Length);
        for (int i = 0; i < count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            _selectorPatternLookup[i] = pattern;

            BossPatternData data = BuildSelectorData(pattern);
            _selectorPatternData[i] = data;
            _selectorRuntimeStates[i].patternId = data.patternId;

            if (pattern != null && pattern.currentCooldown > 0f && data.cooldown > 0f)
                _selectorRuntimeStates[i].lastUsedTime = now + pattern.currentCooldown - data.cooldown;
        }
    }

    BossPatternData BuildSelectorData(AttackPattern pattern)
    {
        if (pattern == null)
            return BossPatternData.CreateDefault(BossPatternId.None);

        BossPatternData data = pattern.selectorData;
        bool usesAutoData = data.patternId == BossPatternId.None;

        if (usesAutoData)
            data.patternId = ResolvePatternId(pattern);

        data.minRange = usesAutoData || data.maxRange <= data.minRange ? Mathf.Max(0f, pattern.minRange) : Mathf.Max(0f, data.minRange);
        data.maxRange = usesAutoData || data.maxRange <= data.minRange ? Mathf.Max(data.minRange + 0.1f, pattern.maxRange) : Mathf.Max(data.minRange + 0.1f, data.maxRange);
        data.minAngle = Mathf.Clamp(data.minAngle, 0f, 180f);
        data.maxAngle = data.maxAngle <= data.minAngle ? 180f : Mathf.Clamp(data.maxAngle, data.minAngle, 180f);
        data.baseWeight = usesAutoData || data.baseWeight <= 0f ? Mathf.Max(0.01f, pattern.weight) : Mathf.Max(0.01f, data.baseWeight);
        data.cooldown = usesAutoData || data.cooldown <= 0f ? Mathf.Max(0f, pattern.cooldown) : Mathf.Max(0f, data.cooldown);
        data.minPhase = usesAutoData || data.minPhase <= 0 ? pattern.ResolveMinPhase() : Mathf.Clamp(data.minPhase, 1, 3);
        data.maxPhase = usesAutoData || data.maxPhase <= 0 ? pattern.ResolveMaxPhase() : Mathf.Clamp(data.maxPhase, data.minPhase, 3);
        data.recentRepeatBlockCount = data.recentRepeatBlockCount <= 0 ? 1 : Mathf.Clamp(data.recentRepeatBlockCount, 1, 8);

        if (usesAutoData)
        {
            data.canRepeat = AllowsPatternFamilyVariantRepeat(data.patternId);
            data.isFallback = true;
            data.fallbackPriority = ResolveFallbackPriority(data.patternId);
            data.postActionType = ResolveDefaultPostAction(data.patternId);
            data.engageMode = ResolveDefaultEngageMode(data.patternId);
            data.engageStartRange = ResolveDefaultEngageStartRange(data.patternId, data.maxRange);
            data.engageStopRange = ResolveDefaultEngageStopRange(data.patternId, data.minRange, data.maxRange);
            data.engageMaxDuration = ResolveDefaultEngageDuration(data.patternId);
            data.engageMoveSpeedMultiplier = ResolveDefaultEngageSpeed(data.patternId);
            data.rangedFallbackPatternId = BossPatternId.SwordWave;
            data.dashFallbackPatternId = BossPatternId.DashSlash;
        }

        data.engageStartRange = data.engageStartRange <= 0f ? ResolveDefaultEngageStartRange(data.patternId, data.maxRange) : Mathf.Max(data.maxRange, data.engageStartRange);
        data.engageStopRange = data.engageStopRange <= 0f ? ResolveDefaultEngageStopRange(data.patternId, data.minRange, data.maxRange) : Mathf.Clamp(data.engageStopRange, data.minRange, data.maxRange);
        data.engageMaxDuration = data.engageMaxDuration <= 0f ? ResolveDefaultEngageDuration(data.patternId) : Mathf.Max(0.05f, data.engageMaxDuration);
        data.engageMoveSpeedMultiplier = data.engageMoveSpeedMultiplier <= 0f ? ResolveDefaultEngageSpeed(data.patternId) : Mathf.Clamp(data.engageMoveSpeedMultiplier, 0.15f, 2.5f);
        ApplySwordWaveRuntimeTuning(ref data);

        if (TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier phaseModifier))
        {
            data.baseWeight *= BossPhasePatternModifier.ResolveMultiplier(phaseModifier.baseWeightMultiplier);
            data.cooldown *= BossPhasePatternModifier.ResolveMultiplier(phaseModifier.cooldownMultiplier);
            data.engageMoveSpeedMultiplier *= BossPhasePatternModifier.ResolveMultiplier(phaseModifier.engageMoveSpeedMultiplier);
            data.engageMoveSpeedMultiplier = Mathf.Clamp(data.engageMoveSpeedMultiplier, 0.15f, 2.5f);

            if (phaseModifier.overridePostAction)
                data.postActionType = phaseModifier.postActionOverride;
        }

        ApplyPlayerObservationPostActionBias(ref data);
        ApplySpatialPostActionBias(ref data);
        ApplyArenaEngageBias(ref data);
        ApplySwordWaveRuntimeTuning(ref data);

        data.baseWeight *= ResolvePlayerStatePatternWeightMultiplier(pattern);
        data.baseWeight *= ResolveSpatialPatternWeightMultiplier(data.patternId);
        data.baseWeight *= ResolveDifficultyPatternWeightMultiplier();
        data.cooldown *= ResolveDifficultyCooldownMultiplier();
        data.engageMoveSpeedMultiplier *= ResolvePlayerStateEngageSpeedMultiplier(data.patternId);
        data.engageMoveSpeedMultiplier *= ResolveDifficultyEngageSpeedMultiplier();
        data.cooldown = Mathf.Max(0f, data.cooldown);
        data.engageMoveSpeedMultiplier = Mathf.Clamp(data.engageMoveSpeedMultiplier, 0.15f, 2.5f);

        return data;
    }

    static bool AllowsPatternFamilyVariantRepeat(BossPatternId patternId)
    {
        return patternId == BossPatternId.QuickSlash ||
               patternId == BossPatternId.HeavySlash;
    }

    void ApplyPlayerObservationPostActionBias(ref BossPatternData data)
    {
        if (!usePlayerStatePatternBias)
            return;

        if (IsFarPressureActive() || _playerObservation.playerIsMovingAway)
        {
            if (data.patternId == BossPatternId.SwordWave || data.patternId == BossPatternId.DashSlash)
                data.postActionType = BossPatternPostActionType.ChaseReposition;
            return;
        }

        if (IsPlayerAtBackAngle())
        {
            data.postActionType = BossPatternPostActionType.Recenter;
            return;
        }

        if (IsPlayerAtSideAngle() && data.postActionType == BossPatternPostActionType.CombatIdle)
            data.postActionType = BossPatternPostActionType.Recenter;

        if (_playerObservation.playerRecentlyHitBoss || _playerObservation.playerIsApproaching)
        {
            if (data.patternId == BossPatternId.HeavySlash || data.patternId == BossPatternId.BackstepSlash)
                data.postActionType = BossPatternPostActionType.Backstep;
        }

        if (!IsCloseLingerActive())
            return;

        if (data.patternId == BossPatternId.HeavySlash || data.patternId == BossPatternId.BackstepSlash)
            data.postActionType = BossPatternPostActionType.Backstep;
        else if (data.patternId == BossPatternId.QuickSlash)
            data.postActionType = BossPatternPostActionType.Recenter;
    }

    void ApplySpatialPostActionBias(ref BossPatternData data)
    {
        if (!useSpatialPatternBias || !Application.isPlaying)
            return;

        RefreshArenaAwareness();
        data.postActionType = _arenaAwareness.ResolvePostAction(data.postActionType);

        bool backBlocked = IsSpatialDirectionBlocked(-transform.forward);
        bool leftBlocked = IsSpatialDirectionBlocked(-transform.right);
        bool rightBlocked = IsSpatialDirectionBlocked(transform.right);

        if (backBlocked && data.postActionType == BossPatternPostActionType.Backstep)
        {
            data.postActionType = BossPatternPostActionType.Recenter;
            return;
        }

        if (data.postActionType == BossPatternPostActionType.StrafeLeft && leftBlocked)
            data.postActionType = rightBlocked ? BossPatternPostActionType.Recenter : BossPatternPostActionType.StrafeRight;
        else if (data.postActionType == BossPatternPostActionType.StrafeRight && rightBlocked)
            data.postActionType = leftBlocked ? BossPatternPostActionType.Recenter : BossPatternPostActionType.StrafeLeft;
    }

    void ApplyArenaEngageBias(ref BossPatternData data)
    {
        if (!useSpatialPatternBias || !Application.isPlaying || _arenaAwareness == null)
            return;

        BossArenaAwarenessSnapshot snapshot = _arenaAwareness.Current;
        if (!snapshot.enabled)
            return;

        if (snapshot.pathToPlayerBlocked && data.rangedFallbackPatternId != BossPatternId.None)
            data.engageMode = BossPatternEngageMode.UseRangedFallback;

        if (snapshot.playerTooFar && data.patternId == BossPatternId.DashSlash)
        {
            data.engageMode = BossPatternEngageMode.DashEngage;
            data.engageStartRange = Mathf.Max(data.engageStartRange, snapshot.distanceToPlayer);
        }
    }

    float ResolveSpatialPatternWeightMultiplier(BossPatternId patternId)
    {
        if (!useSpatialPatternBias || !Application.isPlaying)
            return 1f;

        RefreshArenaAwareness();
        bool backBlocked = IsSpatialDirectionBlocked(-transform.forward);
        bool leftBlocked = IsSpatialDirectionBlocked(-transform.right);
        bool rightBlocked = IsSpatialDirectionBlocked(transform.right);

        float multiplier = _arenaAwareness != null
            ? _arenaAwareness.ResolvePatternWeightMultiplier(arenaAwarenessSettings, patternId)
            : 1f;
        if (backBlocked && patternId == BossPatternId.BackstepSlash)
            multiplier *= Mathf.Clamp(blockedBackstepWeightMultiplier, 0.1f, 1f);

        if (backBlocked && leftBlocked && rightBlocked)
        {
            if (patternId == BossPatternId.SwordWave)
                multiplier *= Mathf.Clamp(cornerRangedWeightMultiplier, 0.1f, 1f);
            else if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, cornerRecenterWeightMultiplier);
        }

        return Mathf.Max(0.01f, multiplier);
    }

    BossArenaAwarenessSnapshot RefreshArenaAwareness()
    {
        _arenaAwareness ??= new BossArenaAwareness();

        if (!useSpatialPatternBias || !Application.isPlaying || !arenaAwarenessSettings.enabled || playerTarget == null)
            return _arenaAwareness.Evaluate(default, 0f, 0f, 0f, false, false, false, false, false);

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        float angle = ResolveAngleToPlayer();
        bool backBlocked = IsSpatialDirectionBlocked(-transform.forward);
        bool leftBlocked = IsSpatialDirectionBlocked(-transform.right);
        bool rightBlocked = IsSpatialDirectionBlocked(transform.right);
        bool pathBlocked = false;
        bool playerNearWall = false;

        if (distance > 0.001f)
        {
            Vector3 toPlayerDirection = toPlayer / distance;
            pathBlocked = !HasMovementClearance(transform.position, toPlayerDirection, Mathf.Min(distance, Mathf.Max(0.05f, obstacleProbeDistance)));

            float playerWallProbe = Mathf.Max(0f, arenaAwarenessSettings.playerWallProbeDistance);
            if (playerWallProbe > 0.001f)
            {
                Vector3 awayFromBoss = toPlayerDirection;
                playerNearWall = !HasMovementClearance(playerTarget.position, awayFromBoss, playerWallProbe);
            }
        }

        Vector3 fromCenter = transform.position - _combatRecoveryAnchorPosition;
        fromCenter.y = 0f;
        return _arenaAwareness.Evaluate(
            arenaAwarenessSettings,
            distance,
            angle,
            fromCenter.magnitude,
            backBlocked,
            leftBlocked,
            rightBlocked,
            playerNearWall,
            pathBlocked);
    }

    Vector3 ResolveArenaCenterReturnDirection(Vector3 desiredDirection)
    {
        Vector3 toCenter = _combatRecoveryAnchorPosition - transform.position;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude <= 0.0001f)
            return desiredDirection;

        Vector3 centerDirection = toCenter.normalized;
        if (!HasMovementClearance(transform.position, centerDirection, Mathf.Max(0.05f, spatialBiasProbeDistance)))
            return desiredDirection;

        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return centerDirection;

        float blend = Mathf.Clamp01(arenaAwarenessSettings.centerReturnDirectionBlend);
        Vector3 blended = Vector3.Lerp(desiredDirection.normalized, centerDirection, blend);
        blended.y = 0f;
        return blended.sqrMagnitude <= 0.0001f ? centerDirection : blended.normalized;
    }

    bool IsSpatialDirectionBlocked(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f || bodyCollider == null || !bodyCollider.enabled)
            return false;

        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        float probeDistance = Mathf.Max(0.05f, spatialBiasProbeDistance);
        return !HasMovementClearance(transform.position, direction.normalized, probeDistance);
    }

    bool TryResolvePhasePatternModifier(AttackPattern pattern, out BossPhasePatternModifier modifier)
    {
        modifier = default;
        if (pattern == null)
            return false;

        BossPatternId patternId = pattern.selectorData.patternId != BossPatternId.None
            ? pattern.selectorData.patternId
            : ResolvePatternId(pattern);

        if (pattern.phaseModifiers != null)
        {
            for (int i = 0; i < pattern.phaseModifiers.Length; i++)
            {
                BossPhasePatternModifier candidate = pattern.phaseModifiers[i];
                if (candidate.phase != _currentPhase)
                    continue;

                if (candidate.patternId != BossPatternId.None && candidate.patternId != patternId)
                    continue;

                modifier = candidate;
                return true;
            }
        }

        if (!useDefaultPhaseModifierFallback || _currentPhase <= 1 || patternId == BossPatternId.None)
            return false;

        modifier = CreateDefaultPhaseModifier(patternId, _currentPhase);
        return true;
    }

#if UNITY_EDITOR
    public bool TryGetResolvedPhasePatternModifierForValidation(
        AttackPattern pattern,
        int phase,
        out BossPhasePatternModifier modifier)
    {
        modifier = default;
        if (pattern == null)
            return false;

        BossPatternId patternId = pattern.selectorData.patternId != BossPatternId.None
            ? pattern.selectorData.patternId
            : ResolvePatternId(pattern);

        if (pattern.phaseModifiers != null)
        {
            for (int i = 0; i < pattern.phaseModifiers.Length; i++)
            {
                BossPhasePatternModifier candidate = pattern.phaseModifiers[i];
                if (candidate.phase != phase)
                    continue;

                if (candidate.patternId != BossPatternId.None && candidate.patternId != patternId)
                    continue;

                modifier = candidate;
                return true;
            }
        }

        if (!useDefaultPhaseModifierFallback || phase <= 1 || patternId == BossPatternId.None)
            return false;

        modifier = CreateDefaultPhaseModifier(patternId, phase);
        return true;
    }
#endif

    float ResolvePhaseTelegraphDurationMultiplier(AttackPattern pattern)
    {
        return TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier)
            ? BossPhasePatternModifier.ResolveMultiplier(modifier.telegraphDurationMultiplier)
            : 1f;
    }

    float ResolvePhaseRecoveryDurationMultiplier(AttackPattern pattern)
    {
        return TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier)
            ? BossPhasePatternModifier.ResolveMultiplier(modifier.recoveryDurationMultiplier)
            : 1f;
    }

    float ResolvePhaseDamageMultiplier(AttackPattern pattern)
    {
        return TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier)
            ? BossPhasePatternModifier.ResolveMultiplier(modifier.damageMultiplier)
            : 1f;
    }

    void ApplyPhaseAttackTimingModifiers(AttackPattern pattern, ref BossAttackTimingData timingData)
    {
        if (!TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier))
            return;

        float telegraphMultiplier = BossPhasePatternModifier.ResolveMultiplier(modifier.telegraphDurationMultiplier);
        float recoveryMultiplier = BossPhasePatternModifier.ResolveMultiplier(modifier.recoveryDurationMultiplier);
        timingData.telegraphDuration *= telegraphMultiplier;
        if (timingData.useTimingDataHitboxControl)
        {
            ScaleTimingPointFromStart(ref timingData.hitboxOpenTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.hitboxCloseTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowStartTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowEndTime, timingData.telegraphStartTime, telegraphMultiplier);
        }

        timingData.recoveryDuration *= recoveryMultiplier;
        timingData.Normalize();
    }

    void ApplyDifficultyAttackTimingModifiers(ref BossAttackTimingData timingData)
    {
        float telegraphMultiplier = ResolveDifficultyTelegraphDurationMultiplier();
        float hitboxDurationMultiplier = ResolveDifficultyHitboxActiveDurationMultiplier();
        float parryWindowMultiplier = ResolveDifficultyParryWindowMultiplier();

        timingData.telegraphDuration *= telegraphMultiplier;
        if (timingData.useTimingDataHitboxControl)
        {
            ScaleTimingPointFromStart(ref timingData.hitboxOpenTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.hitboxCloseTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowStartTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowEndTime, timingData.telegraphStartTime, telegraphMultiplier);

            float hitboxDuration = Mathf.Max(0f, timingData.hitboxCloseTime - timingData.hitboxOpenTime);
            if (hitboxDuration > 0.001f)
                timingData.hitboxCloseTime = timingData.hitboxOpenTime + (hitboxDuration * hitboxDurationMultiplier);
        }

        float parryDuration = Mathf.Max(0f, timingData.parryWindowEndTime - timingData.parryWindowStartTime);
        if (parryDuration > 0.001f)
            timingData.parryWindowEndTime = timingData.parryWindowStartTime + (parryDuration * parryWindowMultiplier);

        timingData.Normalize();
    }

    static void ScaleTimingPointFromStart(ref float time, float startTime, float multiplier)
    {
        if (time <= startTime)
            return;

        time = startTime + ((time - startTime) * multiplier);
    }

    BossPatternId ResolvePatternId(AttackPattern pattern)
    {
        if (pattern == null)
            return BossPatternId.None;

        if (pattern.firesSwordWaveProjectile)
            return BossPatternId.SwordWave;

        string patternName = pattern.patternName ?? string.Empty;
        string triggerName = pattern.animTriggerName ?? string.Empty;

        if (ContainsPatternToken(patternName, "SwordWave"))
            return BossPatternId.SwordWave;
        if (ContainsPatternToken(patternName, "Backstep") ||
            ContainsPatternToken(patternName, "Pressure") ||
            ContainsPatternToken(triggerName, "Quickshift") ||
            string.Equals(triggerName, "Attack_E", StringComparison.Ordinal))
            return BossPatternId.BackstepSlash;
        if (ContainsPatternToken(patternName, "Dash") || string.Equals(triggerName, "Attack_D", StringComparison.Ordinal))
            return BossPatternId.DashSlash;
        if (ContainsPatternToken(patternName, "Heavy") ||
            ContainsPatternToken(patternName, "Crush") ||
            string.Equals(triggerName, "Attack_C", StringComparison.Ordinal))
            return BossPatternId.HeavySlash;

        return BossPatternId.QuickSlash;
    }

    float ResolveEffectiveSwordWaveMinRange()
    {
        return Mathf.Max(swordWaveDirectMinRange, MinEffectiveSwordWaveRange);
    }

    float ResolveEffectiveSwordWaveMaxRange(float minRange)
    {
        float configuredMax = Mathf.Max(minRange + 0.1f, swordWaveDirectMaxRange);
        return Mathf.Clamp(configuredMax, minRange + 0.1f, MaxEffectiveSwordWaveRange);
    }

    float ResolveEffectiveSwordWaveCooldown()
    {
        return Mathf.Max(swordWaveDirectCooldown, 6.25f);
    }

    void ApplySwordWaveRuntimeTuning(ref BossPatternData data)
    {
        if (data.patternId != BossPatternId.SwordWave)
            return;

        float minRange = ResolveEffectiveSwordWaveMinRange();
        float maxRange = ResolveEffectiveSwordWaveMaxRange(minRange);
        data.minRange = Mathf.Max(data.minRange, minRange);
        data.maxRange = Mathf.Min(Mathf.Max(data.minRange + 0.1f, data.maxRange), maxRange);
        data.baseWeight = Mathf.Min(data.baseWeight, MaxEffectiveSwordWaveWeight);
        data.engageMode = BossPatternEngageMode.None;
        data.engageStartRange = data.maxRange;
        data.engageStopRange = Mathf.Clamp(data.engageStopRange, data.minRange, data.maxRange);
    }

    static bool ContainsPatternToken(string value, string token)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int ResolveFallbackPriority(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.SwordWave:
                return 90;
            case BossPatternId.QuickSlash:
                return 80;
            case BossPatternId.BackstepSlash:
                return 75;
            case BossPatternId.DashSlash:
                return 70;
            case BossPatternId.HeavySlash:
                return 45;
            default:
                return 0;
        }
    }

    static BossPatternPostActionType ResolveDefaultPostAction(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.BackstepSlash:
                return BossPatternPostActionType.Backstep;
            case BossPatternId.SwordWave:
                return BossPatternPostActionType.ChaseReposition;
            default:
                return BossPatternPostActionType.CombatIdle;
        }
    }

    static BossPatternEngageMode ResolveDefaultEngageMode(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.QuickSlash:
                return BossPatternEngageMode.ChaseUntilInRange;
            case BossPatternId.DashSlash:
                return BossPatternEngageMode.DashEngage;
            case BossPatternId.HeavySlash:
            case BossPatternId.BackstepSlash:
                return BossPatternEngageMode.RequireInRange;
            default:
                return BossPatternEngageMode.None;
        }
    }

    static float ResolveDefaultEngageStartRange(BossPatternId patternId, float maxRange)
    {
        switch (patternId)
        {
            case BossPatternId.QuickSlash:
                return Mathf.Max(maxRange + 1.2f, 3.2f);
            case BossPatternId.DashSlash:
                return Mathf.Max(maxRange + 1.5f, 8.5f);
            default:
                return Mathf.Max(maxRange, 0f);
        }
    }

    static float ResolveDefaultEngageStopRange(BossPatternId patternId, float minRange, float maxRange)
    {
        float inset = patternId == BossPatternId.DashSlash ? 0.35f : 0.2f;
        return Mathf.Clamp(maxRange - inset, minRange, maxRange);
    }

    static float ResolveDefaultEngageDuration(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.DashSlash:
                return 0.8f;
            case BossPatternId.QuickSlash:
                return 0.55f;
            default:
                return 0.35f;
        }
    }

    static float ResolveDefaultEngageSpeed(BossPatternId patternId)
    {
        return patternId == BossPatternId.DashSlash ? 1.25f : 1.15f;
    }

    bool TryGetPatternByName(string patternName, out AttackPattern pattern)
    {
        pattern = null;
        if (string.IsNullOrWhiteSpace(patternName))
            return false;

        if (_patternLookup.Count == 0)
            RebuildPatternLookup();

        return _patternLookup.TryGetValue(patternName.Trim(), out pattern);
    }

    public void BeginUltimateVictimState(Transform attacker, float durationHint)
    {
        if (_isUltimateVictim)
        {
            Vector3 lookPoint = attacker != null ? attacker.position : transform.position + transform.forward;
            SetUltimateVictimAnchor(transform.position, lookPoint);
            return;
        }

        _isUltimateVictim = true;
        CacheUltimateVictimWorldPose();
        _delayDestroyUntilUltimateVictimEnds = false;
        AbortAttackExecution(clearPunishWindow: true);

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }
        ReleaseParryStunAnimationFallback();
        _pendingCombatIdleDuration = -1f;
        UpdateMoveAnimation(0f);

        if (rb != null)
        {
            _ultimateVictimRigidbodyWasKinematic = rb.isKinematic;
            _ultimateVictimRigidbodyConstraints = rb.constraints;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        if (bossAnimator != null)
        {
            _cachedUltimateVictimAnimatorState = true;
            _ultimateVictimAnimatorUpdateMode = bossAnimator.updateMode;
            _ultimateVictimAnimatorApplyRootMotion = bossAnimator.applyRootMotion;
            _ultimateVictimAnimatorSpeed = bossAnimator.speed;
            bossAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            bossAnimator.applyRootMotion = false;
            bossAnimator.speed = 0f;
        }

        if (!_isDead)
            currentState = BossState.CombatIdle;

        Vector3 initialLookPoint = attacker != null ? attacker.position : transform.position + transform.forward;
        SetUltimateVictimAnchor(transform.position, initialLookPoint);
    }

    public void SetUltimateVictimAnchor(Vector3 worldPosition, Vector3 lookTarget)
    {
        _ultimateVictimAnchorPosition = worldPosition;

        Vector3 facing = lookTarget - worldPosition;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.0001f)
        {
            _ultimateVictimAnchorRotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
        }
        else
        {
            Vector3 currentForward = transform.forward;
            currentForward.y = 0f;
            if (currentForward.sqrMagnitude < 0.0001f)
                currentForward = Vector3.forward;

            _ultimateVictimAnchorRotation = Quaternion.LookRotation(currentForward.normalized, Vector3.up);
        }

        _hasUltimateVictimAnchor = true;
        ApplyUltimateVictimAnchor();
    }

    public void EndUltimateVictimState()
    {
        if (this == null)
            return;

        if (!_isUltimateVictim)
            return;

        _isUltimateVictim = false;
        _hasUltimateVictimAnchor = false;
        RestoreUltimateVictimWorldPose();

        if (rb != null)
        {
            rb.constraints = _ultimateVictimRigidbodyConstraints;
            rb.isKinematic = _ultimateVictimRigidbodyWasKinematic;
            if (!_ultimateVictimRigidbodyWasKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (_cachedUltimateVictimAnimatorState && bossAnimator != null)
        {
            bossAnimator.updateMode = _ultimateVictimAnimatorUpdateMode;
            bossAnimator.applyRootMotion = _ultimateVictimAnimatorApplyRootMotion;
            bossAnimator.speed = _ultimateVictimAnimatorSpeed;
            _cachedUltimateVictimAnimatorState = false;
        }

        RefreshGameplayPlayerTargetAfterRecovery();

        if (_isDead)
        {
            if (_delayDestroyUntilUltimateVictimEnds && _destroyRoutine == null)
                _destroyRoutine = StartCoroutine(Co_DestroyHierarchyAfterDead());

            _delayDestroyUntilUltimateVictimEnds = false;
            return;
        }

        if (breakController != null && breakController.IsInBreak)
        {
            if (bossAnimator != null)
                bossAnimator.SetBool(AnimParam_IsBreak, true);

            SetState(BossState.Break, true);
            return;
        }

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsBreak, false);

        AbortAttackExecution(clearPunishWindow: true);
        UpdateMoveAnimation(0f);
        _pendingCombatIdleDuration = Mathf.Max(Mathf.Max(0f, combatIdleTime), Mathf.Max(0f, ultimateVictimRecoveryDuration));
        TryStartCombatRecovery(BossRecoveryReason.UltimateVictim, BossState.CombatIdle);
    }

    void RefreshGameplayPlayerTargetAfterRecovery()
    {
        EnsureGameplayPlayerTarget(forceRefresh: true);
        FaceToPlayerInstant();
    }

    void CacheUltimateVictimWorldPose()
    {
        if (rb != null)
        {
            _ultimateVictimOriginalPosition = rb.position;
            _ultimateVictimOriginalRotation = rb.rotation;
        }
        else
        {
            _ultimateVictimOriginalPosition = transform.position;
            _ultimateVictimOriginalRotation = transform.rotation;
        }

        _hasUltimateVictimOriginalPose = true;
        _ultimateVictimVisualRoot = ResolveUltimateVictimVisualRoot();
        _hasUltimateVictimVisualPose = false;
        if (_ultimateVictimVisualRoot != null && _ultimateVictimVisualRoot != transform)
        {
            _ultimateVictimVisualLocalPosition = _ultimateVictimVisualRoot.localPosition;
            _ultimateVictimVisualLocalRotation = _ultimateVictimVisualRoot.localRotation;
            _ultimateVictimVisualLocalScale = _ultimateVictimVisualRoot.localScale;
            _ultimateVictimVisualGroundOffsetY = _ultimateVictimVisualRoot.position.y - _ultimateVictimOriginalPosition.y;
            _hasUltimateVictimVisualPose = true;
        }
    }

    void RestoreUltimateVictimWorldPose()
    {
        if (this == null)
            return;

        if (!_hasUltimateVictimOriginalPose)
            return;

        transform.SetPositionAndRotation(_ultimateVictimOriginalPosition, _ultimateVictimOriginalRotation);

        if (rb != null)
        {
            rb.position = _ultimateVictimOriginalPosition;
            rb.rotation = _ultimateVictimOriginalRotation;
        }

        RestoreUltimateVictimVisualPose();
        _hasUltimateVictimOriginalPose = false;
    }

    void ApplyUltimateVictimAnchor()
    {
        if (this == null)
            return;

        if (!_isUltimateVictim || !_hasUltimateVictimAnchor)
            return;

        if (_hasUltimateVictimOriginalPose)
        {
            transform.SetPositionAndRotation(_ultimateVictimOriginalPosition, _ultimateVictimOriginalRotation);
            if (rb != null)
            {
                rb.position = _ultimateVictimOriginalPosition;
                rb.rotation = _ultimateVictimOriginalRotation;
            }
        }

        if (_ultimateVictimVisualRoot != null && _ultimateVictimVisualRoot != transform)
        {
            Vector3 visualPosition = _ultimateVictimAnchorPosition;
            visualPosition.y = _ultimateVictimOriginalPosition.y + _ultimateVictimVisualGroundOffsetY;
            _ultimateVictimVisualRoot.SetPositionAndRotation(visualPosition, _ultimateVictimAnchorRotation);
        }
        else
        {
            transform.SetPositionAndRotation(_ultimateVictimAnchorPosition, _ultimateVictimAnchorRotation);
            if (rb != null)
            {
                rb.position = _ultimateVictimAnchorPosition;
                rb.rotation = _ultimateVictimAnchorRotation;
            }
        }
    }

    Transform ResolveUltimateVictimVisualRoot()
    {
        if (_bossReferences != null && _bossReferences.VisualRoot != null)
            return _bossReferences.VisualRoot;

        Transform visual = transform.Find("VisualRoot");
        return visual != null ? visual : transform;
    }

    void RestoreUltimateVictimVisualPose()
    {
        if (!_hasUltimateVictimVisualPose || _ultimateVictimVisualRoot == null)
            return;

        _ultimateVictimVisualRoot.localPosition = _ultimateVictimVisualLocalPosition;
        _ultimateVictimVisualRoot.localRotation = _ultimateVictimVisualLocalRotation;
        _ultimateVictimVisualRoot.localScale = _ultimateVictimVisualLocalScale;
        _hasUltimateVictimVisualPose = false;
        _ultimateVictimVisualRoot = null;
    }

    void EnsureGameplayPlayerTarget(bool forceRefresh = false)
    {
        if (!forceRefresh && IsValidGameplayPlayerTarget(playerTarget))
        {
            _cachedGameplayPlayerTarget = playerTarget;
            return;
        }

        if (!forceRefresh && _lastGameplayPlayerTargetResolveFrame == Time.frameCount)
            return;

        _lastGameplayPlayerTargetResolveFrame = Time.frameCount;
        Transform resolved = ResolveGameplayPlayerTarget(forceRefresh);
        if (resolved != null)
            playerTarget = resolved;
    }

    Transform ResolveGameplayPlayerTarget(bool forceRefresh)
    {
        if (!forceRefresh && IsValidGameplayPlayerTarget(_cachedGameplayPlayerTarget))
            return _cachedGameplayPlayerTarget;

        if (_cachedGameplayPlayerReferences == null || !_cachedGameplayPlayerReferences)
            _cachedGameplayPlayerReferences = GameplaySceneCache.ResolvePlayerReferences();

        if ((_cachedGameplayPlayerReferences == null || !_cachedGameplayPlayerReferences) && Application.isPlaying)
            _cachedGameplayPlayerReferences = FindFirstObjectByType<PlayerReferences>();

        Transform resolved = _cachedGameplayPlayerReferences != null
            ? _cachedGameplayPlayerReferences.PlayerRoot
            : null;

        if (!IsValidGameplayPlayerTarget(resolved) && IsValidGameplayPlayerTarget(playerTarget))
            resolved = playerTarget;

        _cachedGameplayPlayerTarget = resolved;
        return resolved;
    }

    bool IsValidGameplayPlayerTarget(Transform target)
    {
        if (target == null)
            return false;

        GameObject targetObject = target.gameObject;
        if (targetObject == null || !targetObject.activeInHierarchy)
            return false;

        if (target.GetComponentInParent<UltimatePresentationClone>() != null)
            return false;

        if (target.GetComponentInParent<UltimateStageRuntime>() != null)
            return false;

        return true;
    }

    void CacheAttackHitboxDefaults()
    {
        if (_cachedAttackHitboxDefaults || attackHitbox == null)
            return;

        _defaultAttackHitboxUseOneShotWindow = attackHitbox.useOneShotWindow;
        _defaultAttackHitboxExpandedPadding = attackHitbox.expandedPadding;
        _defaultAttackHitboxMeshPaddingScale = attackHitbox.meshExpandedPaddingScale;
        _defaultAttackHitboxScanInterval = attackHitbox.expandedScanInterval;
        _defaultAttackHitboxOneShotWindow = attackHitbox.oneShotWindow;
        _cachedAttackHitboxDefaults = true;
    }

    void ApplyPatternHitboxTuning(AttackPattern pattern)
    {
        if (attackHitbox == null || pattern == null)
            return;

        CacheAttackHitboxDefaults();

        attackHitbox.expandedPadding = pattern.ResolveHitboxExpandedPadding(_defaultAttackHitboxExpandedPadding);
        attackHitbox.meshExpandedPaddingScale = pattern.ResolveHitboxMeshPaddingScale(_defaultAttackHitboxMeshPaddingScale);
        attackHitbox.expandedScanInterval = pattern.ResolveHitboxScanInterval(_defaultAttackHitboxScanInterval);
        attackHitbox.oneShotWindow = pattern.ResolveHitboxOneShotWindow(_defaultAttackHitboxOneShotWindow);
        attackHitbox.useOneShotWindow = _defaultAttackHitboxUseOneShotWindow && attackHitbox.oneShotWindow > 0.001f;
    }

    bool TryQueueFollowUp(AttackPattern sourcePattern, float distanceToPlayer)
    {
        if (sourcePattern == null || playerTarget == null)
            return false;

        if (_followUpChainDepth >= ResolveActiveMaxFollowUpChains(sourcePattern))
            return false;

        float chance = ResolveFollowUpChance(sourcePattern);
        if (chance <= 0.001f || UnityEngine.Random.value > chance)
            return false;

        float maxDistance = sourcePattern.ResolveFollowUpMaxDistance();
        if (distanceToPlayer > maxDistance)
            return false;

        AttackPattern followUp = SelectFollowUpPattern(sourcePattern, distanceToPlayer);
        if (followUp == null)
            return false;

        _queuedFollowUpPattern = followUp;
        _queuedFollowUpDelay = Mathf.Max(0f, sourcePattern.ResolveFollowUpDelay() + globalFollowUpDelayPadding);
        _followUpChainDepth++;
        return true;
    }

    AttackPattern SelectFollowUpPattern(AttackPattern sourcePattern, float distanceToPlayer)
    {
        SyncPatternCooldowns();

        if (allPatterns == null || allPatterns.Count == 0)
            return null;

        string forcedPattern = sourcePattern.forcedFollowUpPattern != null
            ? sourcePattern.forcedFollowUpPattern.Trim()
            : string.Empty;

        if (TryResolvePhasePatternModifier(sourcePattern, out BossPhasePatternModifier phaseModifier) &&
            phaseModifier.overrideFollowUp &&
            phaseModifier.allowFollowUp &&
            phaseModifier.followUpPatternId != BossPatternId.None &&
            TryGetExecutablePatternById(phaseModifier.followUpPatternId, distanceToPlayer, true, out AttackPattern phaseFollowUp))
        {
            return phaseFollowUp;
        }

        if (TryGetPatternByName(forcedPattern, out AttackPattern forcedFollowUp))
        {
            if (forcedFollowUp.ResolveTelegraphType() == AttackTelegraphType.Danger)
                return null;

            if (forcedFollowUp.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                return forcedFollowUp;

            return null;
        }

        List<AttackPattern> candidates = _followUpCandidatesCache;
        candidates.Clear();
        foreach (AttackPattern pattern in allPatterns)
        {
            if (pattern == null || pattern == sourcePattern)
                continue;

            if (pattern.ResolveTelegraphType() == AttackTelegraphType.Danger)
                continue;

            if (pattern.maxRange > sourcePattern.ResolveFollowUpMaxDistance() + 0.75f)
                continue;

            if (pattern.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                candidates.Add(pattern);
        }

        return SelectWeightedFollowUpPattern(candidates, sourcePattern);
    }

    AttackPattern SelectWeightedFollowUpPattern(List<AttackPattern> candidates, AttackPattern sourcePattern)
    {
        if (sourcePattern == null)
            return SelectWeightedPattern(candidates);

        if (candidates == null || candidates.Count == 0)
            return null;

        EnsureSelectionWeightCacheCapacity(candidates.Count);

        float totalWeight = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            AttackPattern candidate = candidates[i];
            float weight = Mathf.Max(0.01f, candidate.weight * ResolveFollowUpWeightMultiplier(sourcePattern, candidate) * ResolvePhasePatternWeightMultiplier(candidate, true));
            _selectionWeightCache[i] = weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0.01f)
            return candidates[0];

        float roll = UnityEngine.Random.value * totalWeight;
        float accumulated = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            accumulated += _selectionWeightCache[i];
            if (roll <= accumulated)
                return candidates[i];
        }

        return candidates[candidates.Count - 1];
    }

    float ResolveFollowUpWeightMultiplier(AttackPattern sourcePattern, AttackPattern candidate)
    {
        if (sourcePattern == null || candidate == null)
            return 1f;

        AttackTelegraphType sourceType = sourcePattern.ResolveTelegraphType();
        AttackTelegraphType candidateType = candidate.ResolveTelegraphType();
        AttackTelegraphType preferredType = sourcePattern.ResolvePreferredFollowUpTelegraph();
        float multiplier = 1f;

        if (preferredType != AttackTelegraphType.Auto)
        {
            if (candidateType == preferredType)
                multiplier *= 2.40f;
            else if (candidateType == sourceType)
                multiplier *= 0.55f;
            else
                multiplier *= 1.15f;
        }
        else if (candidateType != sourceType)
        {
            multiplier *= 1.15f;
        }

        if (candidate.maxRange <= sourcePattern.ResolveFollowUpMaxDistance())
            multiplier *= 1.12f;

        if (candidate.ResolveCanParry() == sourcePattern.ResolveCanParry())
            multiplier *= 0.92f;

        return multiplier;
    }

    AnimationClip ResolvePreAttackPoseClip()
    {
        return defaultPreAttackPoseClip;
    }

    float ResolvePatternPreAttackPoseDuration(AttackPattern pattern)
    {
        if (pattern == null)
            return 0f;

        float patternDuration = pattern.ResolvePreAttackPoseDuration();
        if (patternDuration > 0.01f)
            return patternDuration;

        if (!useDefaultPreAttackPoseForNonParryable)
            return 0f;

        if (pattern.ResolveCanParry())
            return 0f;

        return defaultPreAttackPoseClip != null ? Mathf.Max(0f, defaultPreAttackPoseDuration) : 0f;
    }

    string ResolvePatternPreAttackPoseTriggerName(AttackPattern pattern)
    {
        return pattern == null ? string.Empty : pattern.ResolvePreAttackPoseTriggerName();
    }

    string ResolvePatternPreAttackPoseLabel(AttackPattern pattern)
    {
        if (pattern == null)
            return defaultPreAttackPoseLabel;

        string patternLabel = pattern.ResolvePreAttackPoseLabel();
        if (pattern.ResolvePreAttackPoseDuration() > 0.01f)
            return patternLabel;

        if (!useDefaultPreAttackPoseForNonParryable || pattern.ResolveCanParry())
            return patternLabel;

        return string.IsNullOrWhiteSpace(defaultPreAttackPoseLabel)
            ? $"{pattern.ResolveTelegraphLabel()} READY"
            : defaultPreAttackPoseLabel.Trim().ToUpperInvariant();
    }

    IEnumerator Co_PlayPreAttackPoseClip(AnimationClip clip, float duration)
    {
        if (clip == null || bossAnimator == null)
        {
            float wait = Mathf.Max(0.01f, duration);
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            yield break;
        }

        _preAttackPoseClipSampler ??= new UltimateAnimatorClipSampler("BossPreAttackPoseSampler");
        if (!_preAttackPoseClipSampler.Begin(bossAnimator, clip))
        {
            float wait = Mathf.Max(0.01f, duration);
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            yield break;
        }

        float safeDuration = Mathf.Max(0.01f, duration);
        _isPreAttackPoseActive = true;
        _preAttackPoseElapsed = 0f;
        _preAttackPoseDurationRuntime = safeDuration;
        _preAttackPoseClipSampler.SampleNormalized(0f);

        while (_preAttackPoseElapsed < safeDuration && !_isDead && currentState == BossState.Attack)
        {
            _preAttackPoseElapsed += Time.deltaTime;
            yield return null;
        }

        _preAttackPoseClipSampler.SampleNormalized(1f);
        StopPreAttackPosePlayback();
    }

    void StopPreAttackPosePlayback()
    {
        _isPreAttackPoseActive = false;
        _preAttackPoseElapsed = 0f;
        _preAttackPoseDurationRuntime = 0f;
        _preAttackPoseClipSampler?.StopPlayback();
    }

    void UpdatePreAttackPoseSampling()
    {
        if (!_isPreAttackPoseActive || _preAttackPoseClipSampler == null || bossAnimator == null)
            return;

        float safeDuration = Mathf.Max(0.01f, _preAttackPoseDurationRuntime);
        float normalizedTime = Mathf.Clamp01(_preAttackPoseElapsed / safeDuration);
        _preAttackPoseClipSampler.SampleNormalized(normalizedTime);
    }

    void AssignDefaultPreAttackPoseClipIfNeeded()
    {
#if UNITY_EDITOR
        if (defaultPreAttackPoseClip != null)
            return;

        defaultPreAttackPoseClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/발도자세.anim");
#endif
    }

    /// <summary>
    /// 플레이어와 너무 가까울 때 실행되는 백스텝 전용 코루틴
    /// (루트 모션 백스텝이면 backstepSpeed=0으로 두는 걸 권장)
    /// </summary>
    IEnumerator Co_Backstep()
    {
        if (playerTarget == null)
            yield break;

        // 백스텝도 먼저 플레이어를 바라보고 시작
        FaceToPlayerInstant();

        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();

        string retreatTrigger = ResolveBackstepTriggerName(backstepAnimTriggerName);
        if (!string.IsNullOrEmpty(retreatTrigger))
            PlayAnimTrigger(retreatTrigger);

        float elapsed = 0f;
        Vector3 backDir = -transform.forward;
        float totalDistance = 0f;
        if (backstepSpeed > 0f && backstepDuration > 0.0001f)
        {
            float requestedDistance = backstepSpeed * backstepDuration;
            totalDistance = useCollisionAwareMovement
                ? MeasureMovementClearance(rb != null ? rb.position : transform.position, backDir, requestedDistance)
                : requestedDistance;
        }

        while (elapsed < backstepDuration && !_isDead)
        {
            float stepDeltaTime = Time.deltaTime;
            if (totalDistance > 0f && backstepDuration > 0.0001f)
            {
                float normalizedStep = Mathf.Clamp01(stepDeltaTime / backstepDuration);
                Vector3 delta = backDir * (totalDistance * normalizedStep);
                if (rb != null)
                    rb.MovePosition(rb.position + delta);
                else
                    transform.position += delta;
            }

            elapsed += stepDeltaTime;
            yield return null;
        }
    }

    string ResolveBackstepTriggerName(string configuredTrigger)
    {
        CacheParryStunAnimatorHooks();

        if (!string.IsNullOrWhiteSpace(configuredTrigger))
        {
            if (string.Equals(configuredTrigger, "Quickshift_B", StringComparison.OrdinalIgnoreCase) && _hasQuickshiftBTrigger)
                return configuredTrigger;

            if (!string.Equals(configuredTrigger, "Backstep", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(configuredTrigger, "Quickshift_B", StringComparison.OrdinalIgnoreCase))
                return configuredTrigger;
        }

        if (_hasQuickshiftBTrigger)
            return "Quickshift_B";

        if (!string.IsNullOrWhiteSpace(combatIdleRetreatTriggerName) &&
            !string.Equals(combatIdleRetreatTriggerName, "Backstep", StringComparison.OrdinalIgnoreCase))
            return combatIdleRetreatTriggerName;

        return string.IsNullOrWhiteSpace(configuredTrigger) ? "Quickshift_B" : configuredTrigger;
    }

    // ==================== 패턴 선택 ====================

    bool ShouldForceNoPatternFallbackAttack(float distanceToPlayer, bool hasPatternAtDistance)
    {
        if (_externalIntroPaused || _isDead || currentState == BossState.Attack)
        {
            _noPatternFallbackReadySince = float.NegativeInfinity;
            return false;
        }

        float attackDistance = ResolveAttackDecisionDistance();
        float pressureDistance = ResolvePressureEngageDistance();
        if (distanceToPlayer <= attackDistance || distanceToPlayer > pressureDistance || hasPatternAtDistance)
        {
            _noPatternFallbackReadySince = float.NegativeInfinity;
            return false;
        }

        if (_noPatternFallbackReadySince < 0f)
            _noPatternFallbackReadySince = Time.time;

        bool ready = Time.time >= _noPatternFallbackReadySince + Mathf.Max(0f, noPatternFallbackAttackDelay);
        if (ready && enableStateLogs)
            LogState($"[BossFSM] No-pattern fallback attack. distance={distanceToPlayer:0.00}");

        return ready;
    }

    bool CanRequestAttackSelection()
    {
        return Time.time >= _attackSelectionBlockedUntil && !IsPhaseTransitionAttackLocked();
    }

    void BlockAttackSelectionRetry()
    {
        _attackSelectionBlockedUntil = Time.time + Mathf.Max(0.01f, attackSelectionRetryDelay);
    }

    bool IsPhaseTransitionAttackLocked()
    {
        return Time.time < _phaseTransitionLockUntilTime;
    }

    AttackPattern SelectPattern(float distanceToPlayer)
    {
        SyncPatternCooldowns();

        if (allPatterns == null || allPatterns.Count == 0)
        {
            LogPatternSelectionDebug(distanceToPlayer, null, "none: no patterns");
            return null;
        }

        if (usePatternSelector && patternSelector != null)
        {
            if (TrySelectPatternWithSelector(distanceToPlayer, out AttackPattern selectorPattern))
            {
                LogPatternSelectionDebug(distanceToPlayer, selectorPattern, "selector");
                return selectorPattern;
            }

            if (!useLegacyPatternSelectionFallback)
            {
                LogPatternSelectionDebug(distanceToPlayer, null, "selector failed; legacy disabled");
                return null;
            }
        }

        AttackPattern legacyPattern = SelectPatternLegacy(distanceToPlayer);
        AttackPattern selected = legacyPattern != null && legacyPattern.CanExecute(this, distanceToPlayer, _lastExecutedPattern)
            ? legacyPattern
            : null;
        LogPatternSelectionDebug(distanceToPlayer, selected, selected != null ? "legacy" : "legacy failed");
        return selected;
    }

    AttackPattern SelectPatternLegacy(float distanceToPlayer)
    {
        if (TryGetExecutableSwordWavePattern(distanceToPlayer, out AttackPattern swordWavePattern))
            return swordWavePattern;

        List<AttackPattern> candidates = _patternCandidatesCache;
        candidates.Clear();

        foreach (var p in allPatterns)
        {
            if (p == null) continue;
            if (p.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                candidates.Add(p);
        }

        if (distanceToPlayer >= ResolvePressureEngageDistance())
        {
            AttackPattern rangedPriority = SelectPreferredRangedPattern(candidates);
            if (rangedPriority != null)
                return rangedPriority;
        }

        AttackPattern selected = SelectWeightedPattern(candidates);
        if (selected != null)
            return selected;

        return SelectFallbackPattern(distanceToPlayer);
    }

    bool TrySelectPatternWithSelector(float distanceToPlayer, out AttackPattern selectedPattern)
    {
        selectedPattern = null;
        if (!usePatternSelector || patternSelector == null)
            return false;

        if (_selectorPatternData == null ||
            _selectorRuntimeStates == null ||
            _selectorPatternLookup == null ||
            IsPatternSelectorCacheDirty())
        {
            RebuildPatternSelectorCache();
        }
        else
        {
            RefreshPatternSelectorCacheData(Time.time);
        }

        SyncSelectorRuntimeCooldowns(Time.time);

        BossPatternContext context = new BossPatternContext(
            distanceToPlayer,
            ResolveAngleToPlayer(),
            CurrentPhase,
            Time.time,
            _playerObservation);

        if (!patternSelector.TrySelect(context, out int selectedIndex, out _))
            return false;

        if (selectedIndex < 0 || selectedIndex >= _selectorPatternLookup.Length)
            return false;

        selectedPattern = _selectorPatternLookup[selectedIndex];
        return selectedPattern != null && selectedPattern.CanExecute(this, distanceToPlayer, _lastExecutedPattern);
    }

    void LogPatternSelectionDebug(float distanceToPlayer, AttackPattern selectedPattern, string source)
    {
        if (!enablePatternDebugLog)
            return;

        _lastDebugSelectedPattern = selectedPattern;
        _lastDebugSelectionSource = source ?? string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        builder.Append("[BossPatternDebug] source=").Append(_lastDebugSelectionSource);
        builder.Append(" selected=").Append(selectedPattern != null ? selectedPattern.patternName : "none");
        builder.Append(" state=").Append(currentState);
        builder.Append(" phase=").Append(CurrentPhase);
        builder.Append(" dist=").Append(distanceToPlayer.ToString("0.00"));
        builder.Append(" angle=").Append(Mathf.Abs(ResolveAngleToPlayer()).ToString("0"));
        builder.Append(" postAction=").Append(_lastExecutedPostActionType);
        builder.Append(" engaging=").Append(_isEngaging);
        builder.Append(" hitboxOpen=").Append(attackHitbox != null && attackHitbox.Collider != null && attackHitbox.Collider.enabled);
        builder.Append(" parryWindow=").Append(_attackTimingParryWindowOpen);
        AppendRecentPatternDebug(builder);

        if (allPatterns != null)
        {
            for (int i = 0; i < allPatterns.Count; i++)
            {
                AttackPattern pattern = allPatterns[i];
                AppendPatternCandidateDebug(builder, pattern, distanceToPlayer);
            }
        }

        Debug.Log(builder.ToString(), this);
    }

    void AppendRecentPatternDebug(System.Text.StringBuilder builder)
    {
        builder.Append(" recent[");
        int capacity = ResolveRecentPatternCapacity();
        int count = Mathf.Min(_recentPatternRecordedCount, capacity);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(',');

            int index = (_recentPatternWriteIndex - 1 - i + capacity) % capacity;
            builder.Append(_recentPatternNames[index]);
        }
        builder.Append(']');
    }

    void AppendPatternCandidateDebug(System.Text.StringBuilder builder, AttackPattern pattern, float distanceToPlayer)
    {
        builder.Append(" | ");
        if (pattern == null)
        {
            builder.Append("null:excluded(null)");
            return;
        }

        BossPatternData data = BuildSelectorData(pattern);
        builder.Append(pattern.patternName);
        builder.Append("(id=").Append(data.patternId);
        builder.Append(",cd=").Append(pattern.currentCooldown.ToString("0.00"));
        builder.Append(",range=").Append(pattern.minRange.ToString("0.0")).Append('-').Append(pattern.maxRange.ToString("0.0"));
        builder.Append(",post=").Append(data.postActionType);
        builder.Append(",engage=").Append(data.engageMode);
        builder.Append("):");

        string reason = ResolvePatternExclusionReason(pattern, data, distanceToPlayer);
        builder.Append(reason == null ? "candidate" : "excluded(" + reason + ")");
    }

    string ResolvePatternExclusionReason(AttackPattern pattern, BossPatternData data, float distanceToPlayer)
    {
        if (pattern == null)
            return "null";
        if (pattern.currentCooldown > 0f)
            return "cooldown";
        if (data.patternId == BossPatternId.None)
            return "no_pattern_id";
        if (!pattern.IsAvailableInPhase(CurrentPhase) || CurrentPhase < data.minPhase || CurrentPhase > data.maxPhase)
            return "phase";
        if (!string.IsNullOrEmpty(pattern.forbiddenAfter) &&
            _lastExecutedPattern.Equals(pattern.forbiddenAfter, StringComparison.Ordinal))
            return "forbidden_after";

        float angle = Mathf.Abs(ResolveAngleToPlayer());
        if (angle < data.minAngle || angle > data.maxAngle)
            return "angle";
        if (!data.canRepeat && IsPatternRecentlyBlocked(pattern, data))
            return "recent_repeat";
        if (distanceToPlayer < pattern.minRange)
            return "too_close";
        if (distanceToPlayer > pattern.maxRange)
        {
            if (data.engageMode == BossPatternEngageMode.ChaseUntilInRange ||
                data.engageMode == BossPatternEngageMode.DashEngage ||
                data.engageMode == BossPatternEngageMode.UseRangedFallback)
                return "engage_candidate";

            return "too_far";
        }

        return null;
    }

    bool IsPatternSelectorCacheDirty()
    {
        int count = allPatterns != null ? allPatterns.Count : 0;
        if (_selectorPatternData == null ||
            _selectorRuntimeStates == null ||
            _selectorPatternLookup == null ||
            _selectorPatternData.Length != count ||
            _selectorRuntimeStates.Length != count ||
            _selectorPatternLookup.Length != count)
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            if (!ReferenceEquals(_selectorPatternLookup[i], allPatterns[i]))
                return true;
        }

        return false;
    }

    void SyncSelectorRuntimeCooldowns(float now)
    {
        if (_selectorRuntimeStates == null || _selectorPatternData == null || _selectorPatternLookup == null)
            return;

        int count = Mathf.Min(_selectorRuntimeStates.Length, _selectorPatternLookup.Length);
        for (int i = 0; i < count; i++)
        {
            AttackPattern pattern = _selectorPatternLookup[i];
            if (pattern == null)
                continue;

            float cooldown = Mathf.Max(0f, _selectorPatternData[i].cooldown);
            if (cooldown <= 0f || pattern.currentCooldown <= 0f)
                continue;

            _selectorRuntimeStates[i].patternId = _selectorPatternData[i].patternId;
            _selectorRuntimeStates[i].lastUsedTime = now + pattern.currentCooldown - cooldown;
        }
    }

    void MarkPatternSelectorUsed(AttackPattern pattern, float now)
    {
        if (pattern == null || patternSelector == null || _selectorPatternLookup == null)
            return;

        for (int i = 0; i < _selectorPatternLookup.Length; i++)
        {
            if (!ReferenceEquals(_selectorPatternLookup[i], pattern))
                continue;

            patternSelector.MarkUsed(i, now);
            return;
        }
    }

    float ResolveAngleToPlayer()
    {
        if (playerTarget == null)
            return 0f;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.0001f)
            return 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            return 0f;

        return Vector3.SignedAngle(forward.normalized, toPlayer.normalized, Vector3.up);
    }

    bool TryGetExecutableSwordWavePattern(float distanceToPlayer, out AttackPattern swordWavePattern)
    {
        swordWavePattern = null;

        if (allPatterns == null || allPatterns.Count == 0)
            return false;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null || !pattern.firesSwordWaveProjectile)
                continue;

            float minRange = ResolveEffectiveSwordWaveMinRange();
            float maxRange = ResolveEffectiveSwordWaveMaxRange(minRange);
            if (distanceToPlayer < minRange || distanceToPlayer > maxRange)
                continue;

            if (!pattern.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                continue;

            swordWavePattern = pattern;
            if (ShouldLogSwordWave())
                Debug.Log($"[BossSwordWave] selected distance={distanceToPlayer:0.00} pattern={pattern.patternName} trigger={pattern.animTriggerName}", this);
            return true;
        }

        return false;
    }

    AttackPattern GetOrCreateSwordWavePattern()
    {
        if (allPatterns == null)
            allPatterns = new List<AttackPattern>();

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            if (pattern.firesSwordWaveProjectile ||
                string.Equals(pattern.patternName, "SwordWave", StringComparison.OrdinalIgnoreCase))
            {
                pattern.firesSwordWaveProjectile = true;
                pattern.patternName = "SwordWave";
                pattern.animTriggerName = "Attack_F";
                float minRange = ResolveEffectiveSwordWaveMinRange();
                pattern.minRange = Mathf.Max(pattern.minRange, minRange);
                pattern.maxRange = Mathf.Min(Mathf.Max(pattern.minRange + 0.1f, pattern.maxRange), ResolveEffectiveSwordWaveMaxRange(minRange));
                pattern.weight = Mathf.Min(pattern.weight, MaxEffectiveSwordWaveWeight);
                return pattern;
            }
        }

        EnsureDefaultSwordWavePattern();
        return allPatterns != null && allPatterns.Count > 0 ? allPatterns[allPatterns.Count - 1] : null;
    }

    bool TryStartDirectSwordWaveAttack(float distanceToPlayer, string sourceState)
    {
        if (!useDirectSwordWaveBranch || _externalIntroPaused || _isDead || currentState == BossState.Attack || IsPhaseTransitionAttackLocked())
            return false;

        float meleeCommitDistance = ResolveAttackDecisionDistance() + 0.2f;
        if (distanceToPlayer <= meleeCommitDistance)
        {
            _directSwordWaveReadySince = float.NegativeInfinity;
            return false;
        }

        float minRange = ResolveEffectiveSwordWaveMinRange();
        float maxRange = ResolveEffectiveSwordWaveMaxRange(minRange);
        if (distanceToPlayer < minRange || distanceToPlayer > maxRange)
        {
            _directSwordWaveReadySince = float.NegativeInfinity;
            return false;
        }

        bool canPressureCast =
            currentState == BossState.CombatIdle ||
            (currentState == BossState.Move && distanceToPlayer >= ResolvePressureReleaseDistance());

        if (!canPressureCast)
        {
            _directSwordWaveReadySince = float.NegativeInfinity;
            return false;
        }

        if (_directSwordWaveReadySince < 0f)
            _directSwordWaveReadySince = Time.time;

        if (Time.time < _nextDirectSwordWaveAt)
            return false;

        if (Time.time < _directSwordWaveReadySince + Mathf.Max(0f, swordWaveDirectChargeTime))
            return false;

        AttackPattern swordWavePattern = GetOrCreateSwordWavePattern();
        if (swordWavePattern == null)
            return false;

        _pendingImmediateAttackPattern = swordWavePattern;
        _nextDirectSwordWaveAt = Time.time + ResolveEffectiveSwordWaveCooldown();
        _directSwordWaveReadySince = float.NegativeInfinity;
        UpdateMoveAnimation(0f);

        if (ShouldLogSwordWave())
            Debug.Log($"[BossSwordWave] direct-start source={sourceState} distance={distanceToPlayer:0.00}", this);
        SetState(BossState.Attack);
        return true;
    }

    bool TryStartSwordWaveAttack(float distanceToPlayer, string sourceState)
    {
        if (_externalIntroPaused || IsPhaseTransitionAttackLocked())
            return false;

        if (!TryGetExecutableSwordWavePattern(distanceToPlayer, out AttackPattern swordWavePattern))
            return false;

        _pendingImmediateAttackPattern = swordWavePattern;
        UpdateMoveAnimation(0f);

        if (ShouldLogSwordWave())
            Debug.Log($"[BossSwordWave] force-attack source={sourceState} distance={distanceToPlayer:0.00}", this);

        SetState(BossState.Attack);
        return true;
    }

    bool ShouldLogSwordWave()
    {
        return enableVerboseCombatLogs && debugSwordWaveLogs;
    }

    bool HasExecutablePatternAtDistance(float distanceToPlayer)
    {
        SyncPatternCooldowns();

        if (allPatterns == null || allPatterns.Count == 0)
            return false;

        if (TryGetExecutableSwordWavePattern(distanceToPlayer, out _))
            return true;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            if (pattern.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                return true;
        }

        return false;
    }

    bool HasAttackPlanAtDistance(float distanceToPlayer)
    {
        if (HasExecutablePatternAtDistance(distanceToPlayer))
            return true;

        return TrySelectEngagePattern(distanceToPlayer, out _, out _);
    }

    AttackPattern SelectPreferredRangedPattern(List<AttackPattern> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        AttackPattern best = null;
        float bestWeight = float.MinValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            AttackPattern candidate = candidates[i];
            if (candidate == null || !candidate.firesSwordWaveProjectile)
                continue;

            float score = Mathf.Max(0.01f, candidate.weight * ResolvePhasePatternWeightMultiplier(candidate, false));
            if (score <= bestWeight)
                continue;

            bestWeight = score;
            best = candidate;
        }

        return best;
    }

    AttackPattern SelectFallbackPattern(float distanceToPlayer)
    {
        if (allPatterns == null || allPatterns.Count == 0)
            return null;

        AttackPattern best = SelectFallbackPattern(distanceToPlayer, true, false);
        if (best != null)
            return best;

        best = SelectFallbackPattern(distanceToPlayer, false, false);
        if (best != null)
            return best;

        return SelectFallbackPattern(distanceToPlayer, false, true);
    }

    AttackPattern SelectFallbackPattern(float distanceToPlayer, bool requireInRange, bool ignoreForbiddenAfter)
    {
        AttackPattern best = null;
        float bestScore = float.PositiveInfinity;

        foreach (var pattern in allPatterns)
        {
            if (pattern == null)
                continue;

            if (!pattern.IsAvailableInPhase(CurrentPhase))
                continue;

            float rangeGap = GetPatternRangeGap(distanceToPlayer, pattern);
            if (requireInRange && rangeGap > 0.001f)
                continue;

            if (!ignoreForbiddenAfter &&
                !string.IsNullOrEmpty(pattern.forbiddenAfter) &&
                _lastExecutedPattern.Equals(pattern.forbiddenAfter, StringComparison.Ordinal))
                continue;

            float cooldownScore = Mathf.Max(0f, pattern.currentCooldown);
            float phaseWeight = ResolvePhasePatternWeightMultiplier(pattern, false);
            float weightBias = Mathf.Max(0.01f, pattern.weight * phaseWeight);
            float score = (cooldownScore / weightBias) + (rangeGap * 4f);

            if (score < bestScore)
            {
                best = pattern;
                bestScore = score;
            }
        }

        return best;
    }

    float GetPatternRangeGap(float distanceToPlayer, AttackPattern pattern)
    {
        if (pattern == null)
            return float.PositiveInfinity;

        if (distanceToPlayer < pattern.minRange)
            return pattern.minRange - distanceToPlayer;

        if (distanceToPlayer > pattern.maxRange)
            return distanceToPlayer - pattern.maxRange;

        return 0f;
    }

    AttackPattern SelectWeightedPattern(List<AttackPattern> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        EnsureSelectionWeightCacheCapacity(candidates.Count);

        float totalWeight = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            AttackPattern candidate = candidates[i];
            float weight = Mathf.Max(0.01f, candidate.weight * ResolvePhasePatternWeightMultiplier(candidate, false));
            _selectionWeightCache[i] = weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
            return candidates[0];

        float r = UnityEngine.Random.value * totalWeight;
        float accum = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            accum += _selectionWeightCache[i];
            if (r <= accum)
                return candidates[i];
        }

        return candidates[candidates.Count - 1];
    }

    void EnsureSelectionWeightCacheCapacity(int count)
    {
        if (_selectionWeightCache != null && _selectionWeightCache.Length >= count)
            return;

        int newCapacity = Mathf.Max(16, count);
        if (_selectionWeightCache == null)
        {
            _selectionWeightCache = new float[newCapacity];
            return;
        }

        Array.Resize(ref _selectionWeightCache, newCapacity);
    }

    BossGroundTelegraph CreateRuntimeGroundTelegraph()
    {
        GameObject runtimeObject = new GameObject("RuntimeGroundTelegraph");
        runtimeObject.hideFlags = HideFlags.HideAndDontSave;
        runtimeObject.transform.SetParent(transform, false);
        return runtimeObject.AddComponent<BossGroundTelegraph>();
    }

    void HideGroundTelegraph()
    {
        if (groundTelegraph != null)
            groundTelegraph.HideCue();
    }

    // ==================== 이동 애니 (BlendTree) ====================

    void UpdateMoveAnimation(float target01)
    {
        UpdateMoveAnimation(target01, Vector3.zero, false);
    }

    void UpdateMoveAnimation(float target01, Vector3 worldMoveDirection)
    {
        UpdateMoveAnimation(target01, worldMoveDirection, true);
    }

    void UpdateMoveAnimation(float target01, Vector3 worldMoveDirection, bool useDirectionalBlend)
    {
        if (bossAnimator == null) return;

        target01 = Mathf.Clamp01(target01);
        bool useCombatStrafe = useDirectionalBlend && target01 > 0.0001f && worldMoveDirection.sqrMagnitude > 0.0001f;
        if (useCombatStrafe)
            _wasMovingForRunStart = target01 >= runStartMinMoveBlend;
        else
            UpdateRunStartMotion(target01, worldMoveDirection);
        SetCombatStrafeAnimation(useCombatStrafe);

        _moveBlend = Mathf.Lerp(
            _moveBlend,
            target01,
            Time.deltaTime / Mathf.Max(0.0001f, moveAnimDamp)
        );

        Vector2 targetDirectionalBlend = Vector2.zero;
        if (target01 > 0.0001f)
        {
            if (useCombatStrafe)
            {
                Vector3 localMoveDirection = transform.InverseTransformDirection(worldMoveDirection.normalized);
                Vector2 planarDirection = new Vector2(localMoveDirection.x, localMoveDirection.z);
                if (planarDirection.sqrMagnitude > 0.0001f)
                    targetDirectionalBlend = planarDirection.normalized * target01;
            }
            else
            {
                // 일반 추적 이동은 플레이어 일반 이동처럼 정면 이동 위주로 보이게 유지한다.
                targetDirectionalBlend = new Vector2(0f, target01);
            }
        }

        _moveXBlend = Mathf.Lerp(
            _moveXBlend,
            targetDirectionalBlend.x,
            Time.deltaTime / Mathf.Max(0.0001f, moveAnimDamp)
        );

        _moveYBlend = Mathf.Lerp(
            _moveYBlend,
            targetDirectionalBlend.y,
            Time.deltaTime / Mathf.Max(0.0001f, moveAnimDamp)
        );

        if (float.IsNaN(_lastAppliedMoveBlend) || Mathf.Abs(_lastAppliedMoveBlend - _moveBlend) > 0.0025f)
        {
            _lastAppliedMoveBlend = _moveBlend;
            bossAnimator.SetFloat(AnimParam_MoveSpeed, _moveBlend);
        }

        if (_hasMoveXParam && (float.IsNaN(_lastAppliedMoveX) || Mathf.Abs(_lastAppliedMoveX - _moveXBlend) > 0.0025f))
        {
            _lastAppliedMoveX = _moveXBlend;
            bossAnimator.SetFloat(AnimParam_MoveX, _moveXBlend);
        }

        if (_hasMoveYParam && (float.IsNaN(_lastAppliedMoveY) || Mathf.Abs(_lastAppliedMoveY - _moveYBlend) > 0.0025f))
        {
            _lastAppliedMoveY = _moveYBlend;
            bossAnimator.SetFloat(AnimParam_MoveY, _moveYBlend);
        }
    }

    void UpdateRunStopMotion(float target01)
    {
        if (!useRunStopMotion || bossAnimator == null || _isDead || _isUltimateVictim)
        {
            StopRunStopMotion();
            _wasMovingForRunStop = target01 >= runStopMinMoveBlend;
            _lastRunStopMoveBlend = target01;
            return;
        }

        if (_runStopActive)
        {
            if (Time.time >= _runStopEndTime || target01 >= runStopMinMoveBlend)
                StopRunStopMotion();
        }

        bool moving = target01 >= runStopMinMoveBlend;
        if (_wasMovingForRunStop && !moving && _lastRunStopMoveBlend >= runStopMinMoveBlend && Time.time >= _nextRunStopAllowedTime)
            PlayRunStopMotion();

        _wasMovingForRunStop = moving;
        _lastRunStopMoveBlend = target01;
    }

    void UpdateRunStartMotion(float target01, Vector3 worldMoveDirection)
    {
        bool moving = target01 >= runStartMinMoveBlend;
        if (useRunStartMotion && bossAnimator != null && !_isDead && !_isUltimateVictim && !_wasMovingForRunStart && moving)
            PlayRunStartMotion(worldMoveDirection);

        _wasMovingForRunStart = moving;
    }

    void PlayRunStartMotion(Vector3 worldMoveDirection)
    {
        if (bossAnimator == null || string.IsNullOrEmpty(runStartTriggerName))
            return;

        if (TryPlayTurnStartMotion(worldMoveDirection))
            return;

        bossAnimator.ResetTrigger(AnimParam_RunStart);
        bossAnimator.SetTrigger(AnimParam_RunStart);
    }

    bool TryPlayTurnStartMotion(Vector3 worldMoveDirection)
    {
        if (!useTurnStartMotion || bossAnimator == null || worldMoveDirection.sqrMagnitude <= 0.0004f || Time.time < _nextTurnStartAllowedTime)
            return false;

        float signedAngle = Vector3.SignedAngle(Flat(transform.forward), Flat(worldMoveDirection), Vector3.up);
        float absAngle = Mathf.Abs(signedAngle);
        if (absAngle < turnStartMinAngle)
            return false;

        int trigger = absAngle >= turnStart180Angle
            ? (signedAngle < 0f ? AnimParam_TurnL180 : AnimParam_TurnR180)
            : (signedAngle < 0f ? AnimParam_TurnL90 : AnimParam_TurnR90);

        bossAnimator.ResetTrigger(trigger);
        bossAnimator.SetTrigger(trigger);
        _nextTurnStartAllowedTime = Time.time + turnStartCooldown;
        return true;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    void PlayRunStopMotion()
    {
        if (bossAnimator == null || string.IsNullOrEmpty(runStopTriggerName))
            return;

        StopRunStopMotion();

        _runStopCachedRootMotion = bossAnimator.applyRootMotion;
        _runStopHasCachedRootMotion = true;
        bossAnimator.applyRootMotion = true;
        bossAnimator.ResetTrigger(AnimParam_RunStop);
        bossAnimator.SetTrigger(AnimParam_RunStop);

        _runStopActive = true;
        _runStopEndTime = Time.time + runStopDuration;
        _nextRunStopAllowedTime = Time.time + runStopCooldown;
    }

    void StopRunStopMotion()
    {
        _runStopActive = false;
        if (_runStopHasCachedRootMotion && bossAnimator != null)
            bossAnimator.applyRootMotion = _runStopCachedRootMotion;
        _runStopHasCachedRootMotion = false;
    }

    void SetCombatStrafeAnimation(bool active)
    {
        if (!_hasCombatStrafingParam || bossAnimator == null || _combatStrafeAnimActive == active)
            return;

        _combatStrafeAnimActive = active;
        bossAnimator.SetBool(AnimParam_IsCombatStrafing, active);
    }

    public void PlayStrafeFootstepLeft()
    {
    }

    public void PlayStrafeFootstepRight()
    {
    }

    Vector3 GetChaseDirection(Vector3 toPlayer)
    {
        Vector3 desired = toPlayer;
        desired.y = 0f;

        if (desired.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        desired.Normalize();

        float refreshInterval = Mathf.Max(0.02f, chaseDirectionRefreshInterval);
        if (_cachedChaseDesiredDirection.sqrMagnitude > 0.0001f &&
            Time.time < _nextChaseDirectionRefreshAt &&
            Vector3.Dot(_cachedChaseDesiredDirection, desired) > 0.985f)
        {
            return _cachedChaseDirection;
        }

        Vector3 resolvedDirection;
        if (_cachedDetourUntil > Time.time)
        {
            bool detourStillOpen = HasMovementClearance(transform.position, _cachedDetourDirection, obstacleProbeDistance * 0.85f);
            bool directPathStillBlocked = !HasMovementClearance(transform.position, desired, obstacleProbeDistance * 0.75f);
            if (detourStillOpen && directPathStillBlocked)
            {
                resolvedDirection = _cachedDetourDirection;
                CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
                return resolvedDirection;
            }
        }

        _cachedDetourUntil = 0f;

        if (HasMovementClearance(transform.position, desired, obstacleProbeDistance))
        {
            resolvedDirection = desired;
            CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
            return resolvedDirection;
        }

        Vector3 detour = FindDetourDirection(desired);
        if (detour.sqrMagnitude > 0.0001f)
        {
            _cachedDetourDirection = detour;
            _cachedDetourUntil = Time.time + detourCommitTime;
            resolvedDirection = detour;
            CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
            return resolvedDirection;
        }

        resolvedDirection = desired;
        CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
        return resolvedDirection;
    }

    void CacheResolvedChaseDirection(Vector3 desired, Vector3 resolved, float refreshInterval)
    {
        _cachedChaseDesiredDirection = desired;
        _cachedChaseDirection = resolved;
        _nextChaseDirectionRefreshAt = Time.time + refreshInterval;
    }

    Vector3 FindDetourDirection(Vector3 desired)
    {
        Vector3 bestDirection = Vector3.zero;
        float bestScore = float.MinValue;

        EvaluateDetourCandidate(desired, obstacleProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, -obstacleProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, obstacleSideProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, -obstacleSideProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, 90f, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, -90f, ref bestDirection, ref bestScore);

        return bestDirection;
    }

    void EvaluateDetourCandidate(Vector3 desired, float angle, ref Vector3 bestDirection, ref float bestScore)
    {
        Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * desired;
        float clearance = MeasureMovementClearance(transform.position, candidate, obstacleProbeDistance);
        if (clearance <= movementSkin)
            return;

        float alignment = Vector3.Dot(candidate, desired);
        float score = alignment + (clearance / Mathf.Max(0.01f, obstacleProbeDistance));

        if (score > bestScore)
        {
            bestScore = score;
            bestDirection = candidate.normalized;
        }
    }

    bool HasMovementClearance(Vector3 startPos, Vector3 direction, float distance)
    {
        return MeasureMovementClearance(startPos, direction, distance) >= distance - movementSkin;
    }

    float MeasureMovementClearance(Vector3 startPos, Vector3 direction, float distance)
    {
        if (bodyCollider == null || !bodyCollider.enabled)
            return distance;

        if (distance <= 0.0001f || direction.sqrMagnitude <= 0.0001f)
            return 0f;

        direction.y = 0f;
        direction.Normalize();

        GetCapsuleWorld(startPos, out Vector3 point1, out Vector3 point2, out float radius);
        int hitCount = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            radius,
            direction,
            _movementSweepHits,
            distance + movementSkin,
            movementCollisionMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = distance + movementSkin;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _movementSweepHits[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform.IsChildOf(transform))
                continue;
            if (playerTarget != null && hit.collider.transform.IsChildOf(playerTarget))
                continue;

            found = true;
            if (hit.distance < nearestDistance)
                nearestDistance = hit.distance;
        }

        return found ? Mathf.Max(0f, nearestDistance - movementSkin) : distance;
    }

    void ApplyPressureMovementDelta(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        if (!useCollisionAwareMovement)
        {
            ApplyMovementDelta(delta);
            return;
        }

        Vector3 horizontalDelta = delta;
        horizontalDelta.y = 0f;
        float distance = horizontalDelta.magnitude;
        if (distance <= 0.0001f)
        {
            ApplyMovementDelta(delta);
            return;
        }

        Vector3 startPos = rb != null ? rb.position : transform.position;
        Vector3 direction = horizontalDelta / distance;
        float probeDistance = Mathf.Min(Mathf.Max(distance + movementSkin, 0.01f), Mathf.Max(0.01f, obstacleProbeDistance));
        if (HasMovementClearance(startPos, direction, probeDistance))
        {
            ApplyMovementDelta(delta);
            return;
        }

        Vector3 detourDirection = FindDetourDirection(direction);
        if (detourDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 detourDelta = detourDirection * distance;
            detourDelta.y = delta.y;
            ApplyMovementDelta(detourDelta);
            return;
        }

        ApplyMovementDelta(delta);
    }

    void ApplyMovementDelta(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        Vector3 startPos = rb != null ? rb.position : transform.position;
        Vector3 resolved = useCollisionAwareMovement ? ResolveMovementDelta(startPos, delta) : delta;
        Vector3 targetPos = startPos + resolved;

        if (rb != null)
            rb.MovePosition(targetPos);
        else
            transform.position = targetPos;
    }

    Vector3 ResolveMovementDelta(Vector3 startPos, Vector3 delta)
    {
        if (bodyCollider == null || !bodyCollider.enabled)
            return delta;

        Vector3 currentPos = startPos;
        Vector3 remaining = delta;
        Vector3 moved = Vector3.zero;

        for (int i = 0; i < 2; i++)
        {
            if (remaining.sqrMagnitude <= 0.000001f)
                break;

            if (!TrySweep(currentPos, remaining, out RaycastHit hit))
            {
                moved += remaining;
                break;
            }

            float distance = remaining.magnitude;
            Vector3 dir = remaining / distance;
            float safeDistance = Mathf.Max(0f, hit.distance - movementSkin);
            Vector3 safeMove = dir * safeDistance;

            moved += safeMove;
            currentPos += safeMove;

            Vector3 leftover = remaining - dir * safeDistance;
            remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
            remaining.y = 0f;
        }

        return moved;
    }

    bool TrySweep(Vector3 currentPos, Vector3 delta, out RaycastHit nearestHit)
    {
        nearestHit = default;

        float distance = delta.magnitude;
        if (distance <= 0.000001f)
            return false;

        GetCapsuleWorld(currentPos, out Vector3 point1, out Vector3 point2, out float radius);

        int hitCount = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            radius,
            delta / distance,
            _movementSweepHits,
            distance + movementSkin,
            movementCollisionMask,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _movementSweepHits[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform.IsChildOf(transform))
                continue;

            if (!found || hit.distance < nearestDistance)
            {
                found = true;
                nearestDistance = hit.distance;
                nearestHit = hit;
            }
        }

        return found;
    }

    void GetCapsuleWorld(Vector3 currentPos, out Vector3 point1, out Vector3 point2, out float radius)
    {
        Vector3 lossy = transform.lossyScale;
        Vector3 scaledCenter = Vector3.Scale(bodyCollider.center, lossy);
        Vector3 center = currentPos + transform.rotation * scaledCenter;

        int direction = bodyCollider.direction;
        Vector3 axis = direction == 0 ? transform.right : (direction == 2 ? transform.forward : transform.up);

        float axisScale = direction == 0 ? Mathf.Abs(lossy.x) : (direction == 2 ? Mathf.Abs(lossy.z) : Mathf.Abs(lossy.y));
        float radiusScaleA = direction == 0 ? Mathf.Abs(lossy.y) : Mathf.Abs(lossy.x);
        float radiusScaleB = direction == 2 ? Mathf.Abs(lossy.y) : Mathf.Abs(lossy.z);
        radius = Mathf.Max(0.01f, bodyCollider.radius * Mathf.Max(radiusScaleA, radiusScaleB) - movementSkin);

        float height = Mathf.Max(bodyCollider.height * axisScale, radius * 2f);
        float half = Mathf.Max(0f, (height * 0.5f) - radius);

        point1 = center + axis * half;
        point2 = center - axis * half;
    }

    // ==================== (선택) 루트 콜라이더 충돌 ====================

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawAttackRangeGizmos &&
            !drawEngageRangeGizmos &&
            !drawHitboxTimingGizmos &&
            !drawArenaAwarenessGizmos)
        {
            return;
        }

        if (drawAttackRangeGizmos)
            DrawAttackRangeGizmos();
        if (drawEngageRangeGizmos)
            DrawEngageRangeGizmos();
        if (drawHitboxTimingGizmos)
            DrawHitboxTimingGizmos();
        if (drawArenaAwarenessGizmos)
            DrawArenaAwarenessGizmos();
    }

    void DrawAttackRangeGizmos()
    {
        if (allPatterns == null)
            return;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            Gizmos.color = ResolvePatternGizmoColor(ResolvePatternId(pattern), 0.25f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, pattern.maxRange));
        }
    }

    void DrawEngageRangeGizmos()
    {
        Gizmos.color = new Color(0.25f, 0.65f, 1f, 0.22f);
        Gizmos.DrawWireSphere(transform.position, ResolvePressureEngageDistance());
        Gizmos.color = new Color(0.15f, 1f, 0.7f, 0.18f);
        Gizmos.DrawWireSphere(transform.position, ResolvePressureReleaseDistance());
    }

    void DrawHitboxTimingGizmos()
    {
        if (attackHitbox == null || attackHitbox.Collider == null)
            return;

        Gizmos.color = _attackTimingParryWindowOpen
            ? new Color(1f, 0.65f, 0.05f, 0.45f)
            : new Color(0.2f, 0.8f, 1f, 0.25f);
        Bounds bounds = attackHitbox.Collider.bounds;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

    void DrawArenaAwarenessGizmos()
    {
        if (!arenaAwarenessSettings.enabled)
            return;

        Vector3 anchor = _combatRecoveryAnchorPosition == Vector3.zero ? transform.position : _combatRecoveryAnchorPosition;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.20f);
        Gizmos.DrawWireSphere(anchor, Mathf.Max(0f, arenaAwarenessSettings.centerReturnDistance));

        if (playerTarget != null)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.45f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.15f, playerTarget.position + Vector3.up * 0.15f);
        }
    }

    static Color ResolvePatternGizmoColor(BossPatternId patternId, float alpha)
    {
        switch (patternId)
        {
            case BossPatternId.QuickSlash:
                return new Color(0.35f, 1f, 0.6f, alpha);
            case BossPatternId.DashSlash:
                return new Color(0.2f, 0.65f, 1f, alpha);
            case BossPatternId.SwordWave:
                return new Color(0.25f, 1f, 1f, alpha);
            case BossPatternId.HeavySlash:
                return new Color(1f, 0.45f, 0.1f, alpha);
            case BossPatternId.BackstepSlash:
                return new Color(1f, 0.8f, 0.2f, alpha);
            default:
                return new Color(1f, 1f, 1f, alpha);
        }
    }
#endif

    void OnTriggerEnter(Collider other)
    {
        if (_currentPattern == null) return;
        // 필요하면 여기서 특수 처리 추가.
    }
}
