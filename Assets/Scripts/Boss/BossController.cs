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

public class BossController : MonoBehaviour, IUltimateVictimState, IParryReact
{
    const string FollowUpTelegraphPrefix = "CHAIN";
    const float FollowUpTelegraphLeadScale = 0.82f;

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
    [SerializeField] private float phaseTwoTelegraphScale = 0.96f;
    [SerializeField] private float phaseThreeTelegraphScale = 0.90f;
    [Range(0f, 0.5f)] [SerializeField] private float phaseTwoFollowUpChanceBonus = 0.08f;
    [Range(0f, 0.5f)] [SerializeField] private float phaseThreeFollowUpChanceBonus = 0.16f;
    [SerializeField] private int phaseThreeExtraFollowUpChains = 1;
    [SerializeField] private float phaseEntryPressureDuration = 5f;
    [SerializeField] private float phaseEntryUnlockedPatternWeight = 1.75f;

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
    [SerializeField] private float followUpRecoveryTax = 0.18f;
    [Range(0f, 1f)] [SerializeField] private float fakeOutFollowUpChanceMultiplier = 0.18f;
    [Range(0f, 1f)] [SerializeField] private float dangerFollowUpChanceMultiplier = 0.06f;
    [Range(0f, 1f)] [SerializeField] private float punishHeavyFollowUpChanceMultiplier = 0.24f;
    [Range(0.05f, 1f)] [SerializeField] private float immediateRepeatPatternWeightMultiplier = 0.22f;
    [Range(0.05f, 1f)] [SerializeField] private float recentRepeatPatternWeightMultiplier = 0.60f;
    [Range(0.25f, 1f)] [SerializeField] private float repeatedTelegraphWeightMultiplier = 0.82f;
    [Range(1, 4)] [SerializeField] private int recentPatternMemory = 3;

    [Header("Debug")]
    [SerializeField] private bool enableStateLogs = false;

    private Coroutine _stateRoutine;
    private Coroutine _parryStunRoutine;
    private UltimateAnimatorClipSampler _preAttackPoseClipSampler;
    private bool _isPreAttackPoseActive;
    private float _preAttackPoseElapsed;
    private float _preAttackPoseDurationRuntime;
    private bool _isDead;
    public event Action<AttackTelegraphType, float, string> OnAttackTelegraph;
    public event Action<float, float, string> OnPunishWindowOpened;
    public event Action OnPunishWindowClosed;
    public event Action<int, float> OnBossPhaseChanged;

    [Header("이동 설정 (한글 설명)")]
    [Tooltip("실제 보스 이동 속도 (m/s)")]
    public float moveSpeed = 4.0f;

    [Tooltip("이 거리 이내로 들어오면 이동을 멈추고 공격 준비")]
    public float stoppingDistance = 1.0f;

    [Tooltip("공격 상태로 전환할 추가 거리 버퍼. 너무 멀리서 바로 공격으로 넘어가는 현상을 줄입니다.")]
    [SerializeField] private float attackCommitDistanceBuffer = 0.28f;

    [Tooltip("근거리에서 감속을 시작할 거리. stoppingDistance보다 커야 자연스럽게 접근합니다.")]
    [SerializeField] private float moveSlowdownDistance = 1.75f;
    [SerializeField] private bool debugSwordWaveLogs = true;

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
    private static readonly int AnimParam_DodgeBack = Animator.StringToHash("Dodge_Back");
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

    private string _lastExecutedPattern = string.Empty;
    private AttackPattern _currentPattern;
    private AttackPattern _queuedFollowUpPattern;
    private float _queuedFollowUpDelay;
    private int _followUpChainDepth;
    private int _currentPhase = 1;
    private float _phaseEntryPressureUntilTime = float.NegativeInfinity;
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
    private PlayerReferences _cachedGameplayPlayerReferences;
    private Transform _cachedGameplayPlayerTarget;
    private int _lastGameplayPlayerTargetResolveFrame = -1;
    private int _parryStunTriggerHash;
    private int _parryStunStateHash;
    private bool _hasParryStunTrigger;
    private bool _hasDodgeBackTrigger;
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
    readonly RaycastHit[] _movementSweepHits = new RaycastHit[16];
    readonly List<AttackPattern> _patternCandidatesCache = new List<AttackPattern>(16);
    readonly List<AttackPattern> _followUpCandidatesCache = new List<AttackPattern>(16);
    readonly Dictionary<string, AttackPattern> _patternLookup = new Dictionary<string, AttackPattern>(16, StringComparer.OrdinalIgnoreCase);
    readonly string[] _recentPatternNames = new string[4];
    readonly AttackTelegraphType[] _recentTelegraphTypes = new AttackTelegraphType[4];
    float[] _selectionWeightCache = new float[16];
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
    public bool IsInUltimateVictimState => _isUltimateVictim;
    public string CurrentPatternName => _currentPattern != null ? _currentPattern.patternName : string.Empty;
    public string CurrentPatternTriggerName => _currentPattern != null ? _currentPattern.animTriggerName : string.Empty;
    public AttackTelegraphType CurrentPatternTelegraphType => _currentPattern != null ? _currentPattern.ResolveTelegraphType() : AttackTelegraphType.Auto;
    public bool CanProcessAttackAnimationEvents => !_isDead && !_isUltimateVictim && currentState == BossState.Attack && _currentPattern != null;

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
        }

        if (breakController != null)
        {
            breakController.OnBreakEnter.AddListener(OnBreakEnter);
            breakController.OnBreakExit.AddListener(OnBreakExit);
        }

        EnsureDefaultSwordWavePattern();
        RebuildPatternLookup();
        RefreshPatternCooldownState();
        EnsureGameplayPlayerTarget(forceRefresh: true);
    }

    void OnValidate()
    {
        AssignDefaultPreAttackPoseClipIfNeeded();
        EnsureDefaultSwordWavePattern();
        RebuildPatternLookup();
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
        StopRunStopMotion();
        StopPreAttackPosePlayback();
        _preAttackPoseClipSampler?.Dispose();
        _preAttackPoseClipSampler = null;

        if (bossHealth != null)
        {
            bossHealth.OnDied -= OnBossDied;
            bossHealth.OnHPChanged -= HandleBossHpChanged;
        }
    }

    void Update()
    {
        if (_isUltimateVictim)
            return;
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

    void RefreshPhaseState(bool silent)
    {
        int newPhase = ResolvePhaseFromHp(GetBossHpNormalized());
        if (newPhase == _currentPhase)
            return;

        _currentPhase = newPhase;
        _phaseEntryPressureUntilTime = Time.time + Mathf.Max(0.5f, phaseEntryPressureDuration);
        ClearQueuedFollowUp();

        if (silent)
            return;

        if (patternVisuals != null)
            patternVisuals.StartVisualCue(newPhase >= 3 ? AttackTelegraphType.Danger : AttackTelegraphType.Dodge, 0.28f);

        OnBossPhaseChanged?.Invoke(_currentPhase, GetBossHpNormalized());
    }

    float GetBossHpNormalized()
    {
        if (bossHealth == null || bossHealth.MaxHP <= 0)
            return 1f;

        return Mathf.Clamp01((float)bossHealth.CurrentHP / bossHealth.MaxHP);
    }

    int ResolvePhaseFromHp(float hpNormalized)
    {
        if (hpNormalized <= phaseThreeThresholdNormalized)
            return 3;

        if (hpNormalized <= phaseTwoThresholdNormalized)
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

    int ResolveActiveMaxFollowUpChains()
    {
        int activeMax = Mathf.Max(0, maxFollowUpChainCount);
        if (_currentPhase >= 3)
            activeMax += Mathf.Max(0, phaseThreeExtraFollowUpChains);

        return activeMax;
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
            if (_stateRoutine != null)
            {
                StopCoroutine(_stateRoutine);
                _stateRoutine = null;
            }

            StopPreAttackPosePlayback();
            StopRunStopMotion();
            HideGroundTelegraph();
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
        SetState(BossState.Detect);
    }

    public void OnParried(GameObject parrier, float riposteDamage, float stunDuration)
    {
        if (breakController != null && !_isDead && !_isUltimateVictim && !breakController.IsInBreak)
            breakController.AddBreak(0f, BossBreakController.BreakSource.Parry);

        NotifyParried(stunDuration);
    }

    public void NotifyParried()
    {
        NotifyParried(parryStunDuration);
    }

    void NotifyParried(float stunDuration)
    {
        if (_isDead || _isUltimateVictim)
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
        SetState(BossState.CombatIdle, true);
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
            _currentPattern = null;
            SetState(BossState.Attack, true);
            return;
        }

        if (_currentPattern != null)
        {
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

    // ==================== 트리거 통합 유틸 ====================

    /// <summary>
    /// 모든 공격/백스텝 트리거를 Reset한 뒤, 이번에 쓸 트리거 하나만 Set.
    /// 트리거가 겹치면서 애니 상태 꼬이는 문제를 예방하기 위한 유틸.
    /// </summary>
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
        if (!string.IsNullOrEmpty(backstepAnimTriggerName))
        {
            if (backstepAnimTriggerName == "Dodge_Back" && _hasDodgeBackTrigger)
                bossAnimator.ResetTrigger(backstepAnimTriggerName);
            else if (backstepAnimTriggerName == "Quickshift_B" && _hasQuickshiftBTrigger)
                bossAnimator.ResetTrigger(backstepAnimTriggerName);
        }

        if (_hasDodgeBackTrigger)
            bossAnimator.ResetTrigger("Dodge_Back");

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
        _hasDodgeBackTrigger = false;
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

                if (parameter.nameHash == AnimParam_DodgeBack)
                    _hasDodgeBackTrigger = true;

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

            if (_hasParryStunTrigger && _hasDodgeBackTrigger && _hasQuickshiftBTrigger && _hasMoveXParam && _hasMoveYParam && _hasCombatStrafingParam)
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
        return Mathf.Min(triggerDistance, attackDistance * innerRatio);
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

        bool hasPatternAtDistance = distance > attackDistance && HasExecutablePatternAtDistance(distance);

        if (distance <= attackDistance)
            SetState(BossState.Attack);
        else if (hasPatternAtDistance)
            SetState(BossState.Attack);
        else if (ShouldForceNoPatternFallbackAttack(distance, hasPatternAtDistance))
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

            float attackDecisionDistance = ResolveAttackDecisionDistance();
            if (distance <= attackDecisionDistance)
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            bool hasPatternAtDistance = HasExecutablePatternAtDistance(distance);
            if (hasPatternAtDistance)
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            if (ShouldForceNoPatternFallbackAttack(distance, hasPatternAtDistance))
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
                yield return StartCoroutine(Co_CombatIdlePause(combatIdlePreRetreatPause));

            yield return StartCoroutine(Co_CombatIdleRetreat());

            if (combatIdlePostRetreatPause > 0.01f)
                yield return StartCoroutine(Co_CombatIdlePause(combatIdlePostRetreatPause));
        }

        t = Mathf.Max(t, minimumPostAttackIdleDuration);
        while (t > 0f && !_isDead)
        {
            EnsureGameplayPlayerTarget();
            if (playerTarget != null)
            {
                float distance = Vector3.Distance(transform.position, playerTarget.position);
                if (TryStartDirectSwordWaveAttack(distance, "CombatIdle"))
                    yield break;

                bool hasPatternAtDistance = HasExecutablePatternAtDistance(distance);
                if (ShouldForceNoPatternFallbackAttack(distance, hasPatternAtDistance))
                {
                    UpdateMoveAnimation(0f);
                    SetState(BossState.Attack);
                    yield break;
                }
            }

            t -= Time.deltaTime;
            if (!TryApplyCombatIdleStrafe())
                UpdateMoveAnimation(0f);
            yield return null;
        }

        ClearPunishWindow();

        if (!_isDead)
            SetState(BossState.Detect);
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

        UpdateMoveAnimation(0f);
        bool isQueuedFollowUp = _queuedFollowUpPattern != null;

        if (isQueuedFollowUp && _queuedFollowUpDelay > 0.01f)
            yield return new WaitForSeconds(_queuedFollowUpDelay);

        // 0) 공격 시작 전에 플레이어 쪽으로 회전 보정
        distance = Vector3.Distance(transform.position, playerTarget.position);
        UpdateClosePressureState(distance);

        if (!isQueuedFollowUp && ShouldTriggerBackstep(distance))
        {
            _closePressureStartedAt = float.NegativeInfinity;
            yield return StartCoroutine(Co_Backstep());

            _backstepCooldownTimer = Time.time + backstepCooldown;

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
        if (pattern == null)
        {
            LogStateWarning("[BossFSM] 사용할 수 있는 패턴이 없음 → CombatIdle");
            SetState(distance > ResolveAttackDecisionDistance() ? BossState.Move : BossState.CombatIdle);
            yield break;
        }

        if (!isQueuedFollowUp)
            _followUpChainDepth = 0;

        _currentPattern         = pattern;
        _lastExecutedPattern    = pattern.patternName;
        RecordPatternHistory(pattern);
        pattern.StartCooldown(Time.time);
        AttackTelegraphType telegraphType = pattern.ResolveTelegraphType();
        float telegraphLeadTime = Mathf.Max(
            ResolveMinimumTelegraphLeadTime(pattern, isQueuedFollowUp),
            pattern.ResolveTelegraphLeadTime() * ResolvePhaseTelegraphScale());
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
            yield return new WaitForSeconds(telegraphLeadTime);

        if (timingStyle == AttackTimingStyle.Delayed && extraHoldDelay > 0.01f)
        {
            yield return new WaitForSeconds(extraHoldDelay);
        }
        else if (timingStyle == AttackTimingStyle.FakeOut && extraHoldDelay > 0.01f)
        {
            if (patternVisuals != null)
                patternVisuals.StartVisualCue(telegraphType, extraHoldDelay);

            OnAttackTelegraph?.Invoke(telegraphType, extraHoldDelay, secondaryTelegraphLabel);
            yield return new WaitForSeconds(extraHoldDelay);
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

                yield return new WaitForSeconds(preAttackPoseDuration);
            }
        }

        HideGroundTelegraph();

        ApplyPatternHitboxTuning(pattern);

        if (attackHitbox != null)
        {
            float resolvedDamage = Mathf.Max(0f, pattern.damageAmount * Mathf.Max(0f, outgoingDamageMultiplier));
            attackHitbox.canPerfectDodge = canPerfectDodge;
            attackHitbox.Configure(resolvedDamage, canParry, canGuard, isUnblockable, causesGuardBreak, transform);
        }

        if (!string.IsNullOrEmpty(pattern.animTriggerName))
            PlayAnimTrigger(pattern.animTriggerName);

        if (pattern.ResolveFiresSwordWaveProjectile())
        {
            float projectileDamage = Mathf.Max(0f, pattern.damageAmount * Mathf.Max(0f, outgoingDamageMultiplier));
            StartCoroutine(Co_FireSwordWaveProjectile(pattern, projectileDamage, canParry, canPerfectDodge, canGuard, isUnblockable, causesGuardBreak));
        }

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
                _currentPattern = null;
                SetState(BossState.Attack, true);
            }
            else
            {
                StageAttackRecovery(pattern.patternName, recoveryTime, punishWindowDuration, punishDamageMultiplier);
                EnterCombatIdleFromCurrentPattern();
            }
        }
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
            yield return new WaitForSeconds(delay);

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

        if (debugSwordWaveLogs)
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

    void AbortAttackExecution(bool clearPunishWindow)
    {
        StopPreAttackPosePlayback();
        HideGroundTelegraph();
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
            if (pattern.currentCooldown > 0f)
                break;
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
        SetState(BossState.CombatIdle, true);
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

        if (_followUpChainDepth >= ResolveActiveMaxFollowUpChains())
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
            yield return new WaitForSeconds(Mathf.Max(0.01f, duration));
            yield break;
        }

        _preAttackPoseClipSampler ??= new UltimateAnimatorClipSampler("BossPreAttackPoseSampler");
        if (!_preAttackPoseClipSampler.Begin(bossAnimator, clip))
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, duration));
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

            if (string.Equals(configuredTrigger, "Dodge_Back", StringComparison.OrdinalIgnoreCase) && _hasDodgeBackTrigger)
                return configuredTrigger;

            if (!string.Equals(configuredTrigger, "Backstep", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(configuredTrigger, "Quickshift_B", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(configuredTrigger, "Dodge_Back", StringComparison.OrdinalIgnoreCase))
                return configuredTrigger;
        }

        if (_hasQuickshiftBTrigger)
            return "Quickshift_B";

        if (_hasDodgeBackTrigger)
            return "Dodge_Back";

        if (!string.IsNullOrWhiteSpace(combatIdleRetreatTriggerName) &&
            !string.Equals(combatIdleRetreatTriggerName, "Backstep", StringComparison.OrdinalIgnoreCase))
            return combatIdleRetreatTriggerName;

        return string.IsNullOrWhiteSpace(configuredTrigger) ? "Dodge_Back" : configuredTrigger;
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
        if (ready)
            LogState($"[BossFSM] No-pattern fallback attack. distance={distanceToPlayer:0.00}");

        return ready;
    }

    AttackPattern SelectPattern(float distanceToPlayer)
    {
        SyncPatternCooldowns();

        if (allPatterns == null || allPatterns.Count == 0)
            return null;

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

            if (!pattern.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                continue;

            swordWavePattern = pattern;
            if (debugSwordWaveLogs)
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
                pattern.minRange = Mathf.Min(pattern.minRange <= 0f ? swordWaveDirectMinRange : pattern.minRange, swordWaveDirectMinRange);
                pattern.maxRange = Mathf.Max(pattern.maxRange, swordWaveDirectMaxRange);
                return pattern;
            }
        }

        EnsureDefaultSwordWavePattern();
        return allPatterns != null && allPatterns.Count > 0 ? allPatterns[allPatterns.Count - 1] : null;
    }

    bool TryStartDirectSwordWaveAttack(float distanceToPlayer, string sourceState)
    {
        if (!useDirectSwordWaveBranch || _externalIntroPaused || _isDead || currentState == BossState.Attack)
            return false;

        float meleeCommitDistance = ResolveAttackDecisionDistance() + 0.2f;
        if (distanceToPlayer <= meleeCommitDistance)
        {
            _directSwordWaveReadySince = float.NegativeInfinity;
            return false;
        }

        float minRange = Mathf.Max(0f, swordWaveDirectMinRange);
        float maxRange = Mathf.Max(minRange + 0.1f, swordWaveDirectMaxRange);
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
        _nextDirectSwordWaveAt = Time.time + Mathf.Max(0.1f, swordWaveDirectCooldown);
        _directSwordWaveReadySince = float.NegativeInfinity;
        UpdateMoveAnimation(0f);

        Debug.Log($"[BossSwordWave] direct-start source={sourceState} distance={distanceToPlayer:0.00}", this);
        SetState(BossState.Attack);
        return true;
    }

    bool TryStartSwordWaveAttack(float distanceToPlayer, string sourceState)
    {
        if (_externalIntroPaused)
            return false;

        if (!TryGetExecutableSwordWavePattern(distanceToPlayer, out AttackPattern swordWavePattern))
            return false;

        _pendingImmediateAttackPattern = swordWavePattern;
        UpdateMoveAnimation(0f);

        if (debugSwordWaveLogs)
            Debug.Log($"[BossSwordWave] force-attack source={sourceState} distance={distanceToPlayer:0.00}", this);

        SetState(BossState.Attack);
        return true;
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
        UpdateRunStartMotion(target01, worldMoveDirection);
        bool useCombatStrafe = useDirectionalBlend && target01 > 0.0001f && worldMoveDirection.sqrMagnitude > 0.0001f;
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

    void OnTriggerEnter(Collider other)
    {
        if (_currentPattern == null) return;
        // 필요하면 여기서 특수 처리 추가.
    }
}
