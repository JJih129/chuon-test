// 파일명: BossController.cs
// 보스 FSM + 이동 + 패턴 + 근접 백스텝 + 사망 시 루트 파괴
// 트리거는 PlayAnimTrigger() 하나로 관리해서 한 번에 하나만 켜지도록 설계.
// 공격 진입 전 플레이어 쪽으로 회전 보정(스냅/짧은 회전) 지원.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public partial class BossController : MonoBehaviour, IUltimateVictimState, IParryReact, IPerfectDodgeReact, IBossTuningContext, IBossPlayerObservationSettings, IBossLocomotionHost
{
    const string FollowUpTelegraphPrefix = "CHAIN";
    const float FollowUpTelegraphLeadScale = 0.82f;
    const float CombatIdleAttackRequestDelay = 0.25f;
    const float MaxCombatIdleAttackRetryDelay = 0.95f;
    const float MaxCombatIdleRetreatPause = 0.18f;
    const float MaxReactiveBackstepDistance = 2.2f;
    const float MinReactiveBackstepCooldown = 3.0f;
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
    [SerializeField] private BossPatternHandTelegraph handPatternTelegraph;
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
    [SerializeField] private bool useRuntimeForwardBoxAttackHitbox = true;
    [SerializeField] private bool disableOriginalAttackHitboxWhenUsingRuntimeBox = true;
    [SerializeField] private Vector3 runtimeForwardHitboxCenter = new Vector3(0f, 0.72f, 1.32f);
    [SerializeField] private Vector3 runtimeForwardHitboxSize = new Vector3(3.15f, 2.65f, 2.55f);
    [SerializeField] private bool gateMeleeAttackStartByRuntimeHitbox = true;
    [SerializeField, Min(0f)] private float meleeAttackStartRangePadding = 0.32f;

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
    [SerializeField, Min(0f)] private float minimumPostAttackIdleDuration = 3.0f;
    [SerializeField, Min(0f)] private float postPatternDelayAfterAnimation = 3.0f;
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
    [SerializeField, Min(0f)] private float minimumPatternCooldown = 3.0f;

    [Tooltip("궁극기 victim 상태 해제 직후 AI가 다시 패턴을 잡기 전 쉬는 시간(초).")]
    [SerializeField] private float ultimateVictimRecoveryDuration = 1.2f;

    [Tooltip("공격 상태에서 애니메이션 종료를 기다리는 최대 시간 (초). 안전장치 역할")]
    public float maxAttackStateWaitTime = 2.0f;
    [SerializeField] private bool waitForAttackAnimationCompletion = true;
    [SerializeField, Min(0.1f)] private float maxAttackAnimationWaitTime = 6.0f;
    [SerializeField, Min(0f)] private float attackAnimationEnterTimeout = 0.55f;
    [SerializeField, Range(0.75f, 1f)] private float attackAnimationCompletionNormalized = 0.98f;
    [SerializeField, Min(0f)] private float minimumFollowUpDelayAfterAttack = 1.15f;

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

    [Header("Arena Engage Gate")]
    [Tooltip("Level_Facility: 도착 연출이 끝나도 보스는 아레나 중앙에서 대기하고, 플레이어가 아레나 안으로 들어오거나(반경) 보스를 공격하면 교전을 시작한다. 엘리베이터 문간 전투를 막는다. MainScene 단독 실행에서는 꺼 둔다.")]
    [SerializeField] private bool holdAtArenaUntilPlayerEnters = false;
    [Tooltip("교전 시작 기준점. 비우면 대기를 시작한 순간의 보스 위치를 사용")]
    [SerializeField] private Transform arenaEngageCenter;
    [Tooltip("플레이어가 기준점으로부터 이 수평 거리 안으로 들어오면 교전 시작")]
    [SerializeField, Min(1f)] private float arenaEngageRadius = 14f;
    [Tooltip("교전 시작 직후 첫 공격 전 최소 대기(초). 플레이어가 자세를 잡을 시간")]
    [SerializeField, Min(0f)] private float arenaEngageFirstAttackDelay = 0.6f;

    [Header("Difficulty Tuning")]
    [SerializeField] private BossDifficultyTier difficultyTier = BossDifficultyTier.Normal;
    [SerializeField] private BossDifficultyProfile[] difficultyProfiles = BossDifficultyProfile.CreateDefaultSet();
    [Tooltip("켜면 타이틀/설정에서 고른 난이도(GameDifficulty, 기본 보통)를 따른다. 끄면 위 difficultyTier 고정.")]
    [SerializeField] private bool followGlobalDifficulty = true;
    public BossDifficultyTier EffectiveDifficultyTier => followGlobalDifficulty ? GameDifficulty.Current : difficultyTier;

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
    [SerializeField, Range(0.04f, 0.8f)] private float parryStunDuration = 0.38f;
    [SerializeField, Min(0.1f)] private float parryRecoveryDuration = 0.55f;
    [SerializeField, Min(0.1f)] private float parryPunishWindowDuration = 0.55f;
    [SerializeField, Range(1f, 3f)] private float parryPunishDamageMultiplier = 1.35f;
    [SerializeField] private string parryPunishSource = "PARRY";
    [SerializeField] private string parryStunTriggerName = "Stagger";
    [SerializeField] private string parryStunStateName = "Break";
    [SerializeField, Range(0f, 0.08f)] private float parryStunTransitionDuration = 0.03f;
    [SerializeField] private bool useBackstepAfterParry = true;
    [SerializeField, Min(0f)] private float parryBackstepDuration = 0.22f;
    [SerializeField, Min(0f)] private float parryBackstepDistance = 1.35f;
    [SerializeField, Min(0f)] private float parryPostAttackLockDuration = 3f;
    [SerializeField, Min(0f)] private float ultimateVictimPostAttackLockDuration = 5f;
    [SerializeField] private string ultimateVictimHitTriggerName = "Hit";
    [SerializeField] private string ultimateVictimHitStateName = "Hit_Combat_B";

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
    [SerializeField] private bool disableRangedPatterns = true;
    [SerializeField] private bool useDirectSwordWaveBranch = true;
    [SerializeField] private float swordWaveDirectMinRange = 3.2f;
    [SerializeField] private float swordWaveDirectMaxRange = 18f;
    [SerializeField] private float swordWaveDirectCooldown = 5.4f;
    [SerializeField] private float swordWaveDirectChargeTime = 1.15f;
    [SerializeField, Min(0f)] private float noPatternFallbackAttackDelay = 0.65f;
    [SerializeField, Min(0.01f)] private float attackSelectionRetryDelay = 0.12f;

    [Header("Gap Closer Chase Dash")]
    [SerializeField] private bool useChaseDashGapCloser = true;
    [SerializeField, Min(0.1f)] private float chaseDashTriggerDistance = 4.8f;
    [SerializeField, Min(0.1f)] private float chaseDashAttackEnterDistance = 2.6f;
    [SerializeField, Min(0.1f)] private float chaseDashStopDistance = 2.25f;
    [SerializeField, Min(0.1f)] private float chaseDashStepDistance = 2.4f;
    [SerializeField, Min(0.1f)] private float chaseDashStepSpeed = 11.5f;
    [SerializeField, Min(0.02f)] private float chaseDashStepDuration = 0.18f;
    [SerializeField, Min(0f)] private float chaseDashStepCooldown = 0.06f;
    [SerializeField, Min(0.1f)] private float maxChaseDashDuration = 1.6f;
    [SerializeField, Min(0.02f)] private float chaseDashRepathInterval = 0.1f;
    [SerializeField, Min(30f)] private float chaseDashRotationSpeed = 780f;
    [SerializeField, Range(0.1f, 2.5f)] private float chaseDashMoveAnimSpeed = 1f;
    [SerializeField, Min(0f)] private float chaseDashRetryCooldown = 0.18f;
    [SerializeField] private bool enableChaseDashAfterimage = true;
    [SerializeField, Min(0.05f)] private float chaseDashAfterimageDuration = 0.45f;
    [SerializeField] private PerfectDodgeAfterImageEffect chaseDashAfterimageEffect;
    [SerializeField] private bool enableChaseDashDebugLog = false;

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
    [SerializeField] private bool suppressRunStartDuringChase = true;
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

    private static readonly int AnimParam_IsBreak   = Animator.StringToHash("IsBreak");
    private static readonly int AnimParam_IsDead    = Animator.StringToHash("IsDead");
    private static readonly int AnimParam_QuickshiftB = Animator.StringToHash("Quickshift_B");

    private bool _hasMoveXParam;
    private bool _hasMoveYParam;
    private bool _hasCombatStrafingParam;
    private float _moveArcSign = 1f;
    private float _moveArcSideUntil = float.NegativeInfinity;
    private bool _isChaseDashGapClosing;
    private float _nextChaseDashAllowedAt;
    private Vector3 _chaseDashStepTarget;

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
    [SerializeField] private bool forceAllPatternsParryable = false;
    [SerializeField] private bool defaultAttackTimingControlsParryWindow = true;
    [SerializeField, Range(0.05f, 1f)] private float defaultAttackTimingTelegraphScale = 1f;
    [SerializeField, Range(0.05f, 1f)] private float defaultAttackTimingParryWindowScale = 1f;
    [SerializeField, Min(0f)] private float parryWindowEaseLeadTime = 0.18f;
    [SerializeField, Min(0f)] private float parryWindowEaseTailTime = 0.16f;
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
    private AttackPattern _lastDebugSelectedPattern;
    private string _lastDebugSelectionSource = string.Empty;
    private float _phaseEntryPressureUntilTime = float.NegativeInfinity;
    private float _phaseTransitionLockUntilTime = float.NegativeInfinity;
    private float _pendingCombatIdleDuration = -1f;
    private float _punishWindowUntilTime = float.NegativeInfinity;
    private float _punishDamageMultiplier = 1f;
    private string _punishSourcePattern = string.Empty;
    private AttackHitbox _originalAttackHitbox;
    private BoxCollider _runtimeForwardAttackBox;
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
    public float backstepTriggerDistance = 1.65f;

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
    readonly List<AttackPattern> _patternCandidatesCache = new List<AttackPattern>(16);
    readonly List<AttackPattern> _followUpCandidatesCache = new List<AttackPattern>(16);
    readonly Dictionary<string, AttackPattern> _patternLookup = new Dictionary<string, AttackPattern>(16, StringComparer.OrdinalIgnoreCase);
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

    [NonSerialized] private BossTuningResolver _tuning;
    BossTuningResolver Tuning => _tuning ??= new BossTuningResolver(this);

    [NonSerialized] private BossLocomotion _locomotion;
    BossLocomotion Locomotion => _locomotion ??= new BossLocomotion(this, transform);

    Rigidbody IBossLocomotionHost.Body => rb;
    CapsuleCollider IBossLocomotionHost.BodyCollider => bodyCollider;
    Animator IBossLocomotionHost.BossAnimator => bossAnimator;
    Transform IBossLocomotionHost.PlayerTarget => playerTarget;
    bool IBossLocomotionHost.HasMoveXParam => _hasMoveXParam;
    bool IBossLocomotionHost.HasMoveYParam => _hasMoveYParam;
    bool IBossLocomotionHost.HasCombatStrafingParam => _hasCombatStrafingParam;
    bool IBossLocomotionHost.IsDead => _isDead;
    bool IBossLocomotionHost.IsUltimateVictim => _isUltimateVictim;
    bool IBossLocomotionHost.IsChaseMovementActive => currentState == BossState.Move || _isEngaging;
    LayerMask IBossLocomotionHost.MovementCollisionMask => movementCollisionMask;
    float IBossLocomotionHost.MovementSkin => movementSkin;
    bool IBossLocomotionHost.UseCollisionAwareMovement => useCollisionAwareMovement;
    float IBossLocomotionHost.ObstacleProbeDistance => obstacleProbeDistance;
    float IBossLocomotionHost.ObstacleProbeAngle => obstacleProbeAngle;
    float IBossLocomotionHost.ObstacleSideProbeAngle => obstacleSideProbeAngle;
    float IBossLocomotionHost.DetourCommitTime => detourCommitTime;
    float IBossLocomotionHost.ChaseDirectionRefreshInterval => chaseDirectionRefreshInterval;
    float IBossLocomotionHost.MoveAnimDamp => moveAnimDamp;
    bool IBossLocomotionHost.UseRunStartMotion => useRunStartMotion;
    bool IBossLocomotionHost.SuppressRunStartDuringChase => suppressRunStartDuringChase;
    string IBossLocomotionHost.RunStartTriggerName => runStartTriggerName;
    float IBossLocomotionHost.RunStartMinMoveBlend => runStartMinMoveBlend;
    bool IBossLocomotionHost.UseTurnStartMotion => useTurnStartMotion;
    float IBossLocomotionHost.TurnStartMinAngle => turnStartMinAngle;
    float IBossLocomotionHost.TurnStart180Angle => turnStart180Angle;
    float IBossLocomotionHost.TurnStartCooldown => turnStartCooldown;
    bool IBossLocomotionHost.UseRunStopMotion => useRunStopMotion;
    string IBossLocomotionHost.RunStopTriggerName => runStopTriggerName;
    float IBossLocomotionHost.RunStopMinMoveBlend => runStopMinMoveBlend;
    float IBossLocomotionHost.RunStopDuration => runStopDuration;
    float IBossLocomotionHost.RunStopCooldown => runStopCooldown;

    [NonSerialized] private BossPlayerObservationTracker _observationTracker;
    BossPlayerObservationTracker ObservationTracker => _observationTracker ??= new BossPlayerObservationTracker(this);
    readonly BossRecentPatternMemory _recentPatterns = new BossRecentPatternMemory();
    readonly BossPatternSelectorCache _selectorCache = new BossPatternSelectorCache();
    readonly BossWeightedPatternPicker _weightedPicker = new BossWeightedPatternPicker();
    [NonSerialized] private Func<AttackPattern, BossPatternData> _buildSelectorDataFunc;
    Func<AttackPattern, BossPatternData> BuildSelectorDataFunc => _buildSelectorDataFunc ??= BuildSelectorData;

    static readonly Func<BossController, AttackPattern, float> s_phaseWeightOf =
        (boss, candidate) => Mathf.Max(0.01f, candidate.weight * boss.ResolvePhasePatternWeightMultiplier(candidate, false));
    static readonly Func<(BossController boss, AttackPattern source), AttackPattern, float> s_followUpWeightOf =
        (state, candidate) => Mathf.Max(0.01f, candidate.weight * state.boss.ResolveFollowUpWeightMultiplier(state.source, candidate) * state.boss.ResolvePhasePatternWeightMultiplier(candidate, true));

    bool IBossPlayerObservationSettings.UsePlayerStatePatternBias => usePlayerStatePatternBias;
    float IBossPlayerObservationSettings.FarPressureDistance => farPressureDistance;
    float IBossPlayerObservationSettings.FarPressureDelay => farPressureDelay;
    float IBossPlayerObservationSettings.FarPressureRangedWeightMultiplier => farPressureRangedWeightMultiplier;
    float IBossPlayerObservationSettings.FarPressureChaseWeightMultiplier => farPressureChaseWeightMultiplier;
    float IBossPlayerObservationSettings.FarPressureEngageSpeedMultiplier => farPressureEngageSpeedMultiplier;
    float IBossPlayerObservationSettings.CloseLingerDistance => closeLingerDistance;
    float IBossPlayerObservationSettings.CloseLingerDelay => closeLingerDelay;
    float IBossPlayerObservationSettings.CloseLingerPunishWeightMultiplier => closeLingerPunishWeightMultiplier;
    float IBossPlayerObservationSettings.CloseLingerRangedWeightMultiplier => closeLingerRangedWeightMultiplier;
    float IBossPlayerObservationSettings.PlayerDefenseResponseMemoryTime => playerDefenseResponseMemoryTime;
    int IBossPlayerObservationSettings.RepeatedParryResponseThreshold => repeatedParryResponseThreshold;
    int IBossPlayerObservationSettings.RepeatedPerfectDodgeResponseThreshold => repeatedPerfectDodgeResponseThreshold;
    float IBossPlayerObservationSettings.RepeatedParryParryableWeightMultiplier => repeatedParryParryableWeightMultiplier;
    float IBossPlayerObservationSettings.RepeatedParryDodgeOnlyWeightMultiplier => repeatedParryDodgeOnlyWeightMultiplier;
    float IBossPlayerObservationSettings.RepeatedPerfectDodgeDodgeOnlyWeightMultiplier => repeatedPerfectDodgeDodgeOnlyWeightMultiplier;
    float IBossPlayerObservationSettings.RepeatedPerfectDodgeParryableWeightMultiplier => repeatedPerfectDodgeParryableWeightMultiplier;
    float IBossPlayerObservationSettings.PlayerObservationMoveSpeedThreshold => playerObservationMoveSpeedThreshold;
    float IBossPlayerObservationSettings.PlayerDodgingDelayedAttackWeightMultiplier => playerDodgingDelayedAttackWeightMultiplier;
    float IBossPlayerObservationSettings.PlayerGuardingPunishWeightMultiplier => playerGuardingPunishWeightMultiplier;
    float IBossPlayerObservationSettings.PlayerAttackingCounterWeightMultiplier => playerAttackingCounterWeightMultiplier;
    float IBossPlayerObservationSettings.PlayerStunnedRangedWeightMultiplier => playerStunnedRangedWeightMultiplier;
    float IBossPlayerObservationSettings.PlayerRecentlyHitBossCounterWeightMultiplier => playerRecentlyHitBossCounterWeightMultiplier;
    float IBossPlayerObservationSettings.BossMissedPressureDelay => bossMissedPressureDelay;
    float IBossPlayerObservationSettings.BossMissedPressureWeightMultiplier => bossMissedPressureWeightMultiplier;
    float IBossPlayerObservationSettings.PlayerSideAngleThreshold => playerSideAngleThreshold;
    float IBossPlayerObservationSettings.PlayerBackAngleThreshold => playerBackAngleThreshold;
    float IBossPlayerObservationSettings.PlayerBackAngleCounterWeightMultiplier => playerBackAngleCounterWeightMultiplier;
    bool IBossPlayerObservationSettings.EnablePlayerObservationDebugLog => enablePlayerObservationDebugLog;
    float IBossPlayerObservationSettings.PlayerObservationDebugInterval => playerObservationDebugInterval;
    int IBossPlayerObservationSettings.ObservationPhase => _currentPhase;
    float IBossPlayerObservationSettings.ObservationReactionDelay => Tuning.ResolveDifficultyReactionDelay();
    BossPatternId IBossPlayerObservationSettings.ResolvePatternId(AttackPattern pattern) => ResolvePatternId(pattern);

    BossDifficultyTier IBossTuningContext.DifficultyTier => EffectiveDifficultyTier;
    BossDifficultyProfile[] IBossTuningContext.DifficultyProfiles => difficultyProfiles;
    float IBossTuningContext.PhaseTwoThresholdNormalized => phaseTwoThresholdNormalized;
    float IBossTuningContext.PhaseThreeThresholdNormalized => phaseThreeThresholdNormalized;
    float IBossTuningContext.PhaseTwoTelegraphScale => phaseTwoTelegraphScale;
    float IBossTuningContext.PhaseThreeTelegraphScale => phaseThreeTelegraphScale;
    float IBossTuningContext.PhaseTwoFollowUpChanceBonus => phaseTwoFollowUpChanceBonus;
    float IBossTuningContext.PhaseThreeFollowUpChanceBonus => phaseThreeFollowUpChanceBonus;
    int IBossTuningContext.CurrentPhase => _currentPhase;
    float IBossTuningContext.PhaseEntryPressureUntilTime => _phaseEntryPressureUntilTime;
    float IBossTuningContext.PhaseEntryUnlockedPatternWeight => phaseEntryUnlockedPatternWeight;
    bool IBossTuningContext.UseDefaultPhaseModifierFallback => useDefaultPhaseModifierFallback;
    float IBossTuningContext.ParryWindowEaseLeadTime => parryWindowEaseLeadTime;
    float IBossTuningContext.ParryWindowEaseTailTime => parryWindowEaseTailTime;
    BossPatternId IBossTuningContext.ResolvePatternId(AttackPattern pattern) => ResolvePatternId(pattern);
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
    public bool IsUsingRuntimeForwardAttackHitbox => useRuntimeForwardBoxAttackHitbox && _runtimeForwardAttackBox != null && attackHitbox == _runtimeForwardAttackBox.GetComponent<AttackHitbox>();
    public BossPlayerCombatObservation CurrentPlayerObservation => ObservationTracker.Observation;
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
        if (handPatternTelegraph == null)
            handPatternTelegraph = GetComponentInChildren<BossPatternHandTelegraph>(true);
        if (handPatternTelegraph == null)
            handPatternTelegraph = gameObject.AddComponent<BossPatternHandTelegraph>();

        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider>();
        if (bossAnimator == null)
            bossAnimator = GetComponent<Animator>().OrNull() ?? GetComponentInChildren<Animator>();
        if (bossAnimator != null && !_isUltimateVictim)
            bossAnimator.speed = 1f;
        if (attackTimingCueAudioSource == null)
            attackTimingCueAudioSource = GetComponent<AudioSource>();
        EnsureRuntimeForwardBoxAttackHitbox();
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
        NormalizeAllPatternsForParry();
        RebuildPatternLookup();
        RebuildPatternSelectorCache();
        RefreshPatternCooldownState();
        EnsureGameplayPlayerTarget(forceRefresh: true);
    }

    void OnValidate()
    {
        AssignDefaultPreAttackPoseClipIfNeeded();
        EnsureDefaultSwordWavePattern();
        NormalizeAllPatternsForParry();
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
                existing.telegraphType = AttackTelegraphType.Parry;
                existing.timingStyle = AttackTimingStyle.Delayed;
                existing.damageAmount = Mathf.Max(existing.damageAmount, 18);
                existing.cooldown = Mathf.Max(2.5f, existing.cooldown);
                existing.weight = Mathf.Max(2.25f, existing.weight);
                existing.minRange = 3.2f;
                existing.maxRange = Mathf.Max(18f, existing.maxRange);
                existing.canPerfectDodge = true;
                existing.canGuard = true;
                existing.isParryable = true;
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
            telegraphType = AttackTelegraphType.Parry,
            timingStyle = AttackTimingStyle.Delayed,
            damageAmount = 18,
            cooldown = 3.4f,
            weight = 2.25f,
            minRange = 3.2f,
            maxRange = 18f,
            canPerfectDodge = true,
            canGuard = true,
            isParryable = true,
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

    void NormalizeAllPatternsForParry()
    {
        if (!forceAllPatternsParryable)
            return;

        if (allPatterns != null)
        {
            for (int i = 0; i < allPatterns.Count; i++)
            {
                AttackPattern pattern = allPatterns[i];
                if (pattern == null)
                    continue;

                pattern.isParryable = true;
                pattern.canGuard = true;
                pattern.isUnblockable = false;
                pattern.causesGuardBreak = false;
                pattern.telegraphType = AttackTelegraphType.Parry;
                pattern.usePreAttackPoseWhenNotParryable = false;
            }
        }

        if (attackTimingData == null)
            return;

        for (int i = 0; i < attackTimingData.Length; i++)
            ForceTimingDataParryable(ref attackTimingData[i]);
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
        Locomotion.StopRunStopMotion();
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
        OnDisableSpecialPatterns(); // SpecialPatterns.cs
        CleanupRuntimeCombatState();
    }

    void CleanupRuntimeCombatState()
    {
        if (_stateRoutine != null)
        {
            StopChaseDashGapCloser();
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
        Locomotion.StopRunStopMotion();
        StopPreAttackPosePlayback();
        StopChaseDashGapCloser();
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
        int newPhase = Tuning.ResolvePhaseFromHp(GetBossHpNormalized());
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

    float ResolveFollowUpChance(AttackPattern sourcePattern)
    {
        if (sourcePattern == null)
            return 0f;

        float chance = Mathf.Clamp01(sourcePattern.ResolveFollowUpChance() + Tuning.ResolvePhaseFollowUpChanceBonus());
        if (Tuning.TryResolvePhasePatternModifier(sourcePattern, out BossPhasePatternModifier phaseModifier))
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
            Tuning.TryResolvePhasePatternModifier(sourcePattern, out BossPhasePatternModifier phaseModifier) &&
            phaseModifier.maxFollowUpCount > 0)
        {
            activeMax = phaseModifier.maxFollowUpCount;
        }

        return Tuning.ResolveDifficultyMaxFollowUpChains(activeMax);
    }

    float ResolvePhasePatternWeightMultiplier(AttackPattern pattern, bool isFollowUp)
    {
        if (pattern == null)
            return 1f;

        float multiplier = Tuning.ResolvePhasePatternWeightMultiplier(pattern, isFollowUp);
        multiplier *= ObservationTracker.ResolvePlayerStatePatternWeightMultiplier(pattern);
        multiplier *= ResolveReactivePatternWeightMultiplier(pattern); // SpecialPatterns.cs (기획 반응형 조건)
        multiplier *= ResolvePatternVarietyWeightMultiplier(pattern, isFollowUp);

        return multiplier;
    }

    float ResolvePatternVarietyWeightMultiplier(AttackPattern pattern, bool isFollowUp)
    {
        if (pattern == null)
            return 1f;

        float multiplier = 1f;
        if (_recentPatterns.IsImmediateRepeat(pattern.patternName, recentPatternMemory))
            multiplier *= immediateRepeatPatternWeightMultiplier;
        else if (_recentPatterns.WasUsedRecently(pattern.patternName, 3, recentPatternMemory))
            multiplier *= recentRepeatPatternWeightMultiplier;

        if (_recentPatterns.CountRecentTelegraphMatches(pattern.ResolveTelegraphType(), 2, recentPatternMemory) >= 2)
            multiplier *= repeatedTelegraphWeightMultiplier;

        if (isFollowUp && _recentPatterns.IsImmediateRepeat(pattern.patternName, recentPatternMemory))
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
            ObservationTracker.ResetInactive();
            return;
        }

        // 타겟 유효성은 위에서 이미 검사됨 (IsValidGameplayPlayerTarget은 순수 검사).
        ObservationTracker.Tick(transform, playerTarget, true, this);
    }

    public void NotifyPlayerHitBoss(Transform attacker)
    {
        if (!usePlayerStatePatternBias)
            return;

        if (attacker != null && playerTarget != null && !attacker.IsChildOf(playerTarget.root))
            return;

        ObservationTracker.RecordPlayerHitBoss(Time.time);
    }

    public void NotifyBossHitPlayer(Transform target)
    {
        if (!usePlayerStatePatternBias)
            return;

        if (target != null && playerTarget != null && !target.IsChildOf(playerTarget.root))
            return;

        ObservationTracker.RecordBossHitPlayer(Time.time);
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
            playerIsMovingAway = ObservationTracker.Observation.playerIsMovingAway,
            playerIsApproaching = ObservationTracker.Observation.playerIsApproaching,
            playerIsDodging = ObservationTracker.Observation.playerIsDodging,
            playerIsGuarding = ObservationTracker.Observation.playerIsGuarding,
            playerIsAttacking = ObservationTracker.Observation.playerIsAttacking,
            playerRecentlyParried = ObservationTracker.Observation.playerRecentlyParried,
            playerRecentlyDodged = ObservationTracker.Observation.playerRecentlyDodged,
            playerRecentlyHitBoss = ObservationTracker.Observation.playerRecentlyHitBoss,
            time = Time.time
        };

        OnPatternTelemetrySample?.Invoke(sample);

        if (!enablePatternTelemetryDebugLog)
            return;

        GameLog.Info(
            "[BossPatternTelemetry] phase=" + sample.phase +
            " pattern=" + sample.patternId +
            " dist=" + BossPlayerObservationTracker.FormatObservationTime(sample.distanceToPlayer) +
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

        if (Locomotion.IsRunStopActive)
        {
            Vector3 stopDelta = bossAnimator.deltaPosition;
            stopDelta.y = 0f;
            if (stopDelta.sqrMagnitude > 0.000001f)
                Locomotion.ApplyMovementDelta(stopDelta);

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
            Locomotion.ApplyMovementDelta(delta);

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

        GameLog.Info(message);
    }

    void LogStateWarning(string message)
    {
        if (!enableStateLogs)
            return;

        Debug.LogWarning(message);
    }

    bool _arenaEngageReleased;
    bool _arenaHoldCenterCaptured;
    Vector3 _arenaHoldCenter;

    /// <summary>True while the boss waits at the arena centre for the player to step in (Level_Facility).</summary>
    public bool IsHoldingForArenaEntry => holdAtArenaUntilPlayerEnters && !_arenaEngageReleased && !_isDead;

    /// <summary>Releases the arena hold immediately (e.g. scripted start).</summary>
    public void ReleaseArenaEngageGate() => _arenaEngageReleased = true;

    bool ShouldReleaseArenaHold()
    {
        if (!_arenaHoldCenterCaptured)
        {
            _arenaHoldCenter = arenaEngageCenter != null ? arenaEngageCenter.position : transform.position;
            _arenaHoldCenterCaptured = true;
        }

        if (bossHealth != null && bossHealth.CurrentHP < bossHealth.MaxHP)
            return true;

        if (playerTarget == null)
            return false;

        Vector3 offset = playerTarget.position - _arenaHoldCenter;
        offset.y = 0f;
        return offset.sqrMagnitude <= arenaEngageRadius * arenaEngageRadius;
    }

    IEnumerator Co_HoldForArenaEntry()
    {
        Locomotion.SetCombatStrafeAnimation(false);
        while (!_isDead && !_externalIntroPaused && !ShouldReleaseArenaHold())
        {
            UpdateMoveAnimation(0f);
            if (playerTarget != null)
            {
                Vector3 toPlayer = playerTarget.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toPlayer.normalized, Vector3.up), 90f * Time.deltaTime);
            }

            yield return null;
            EnsureGameplayPlayerTarget();
        }

        _arenaEngageReleased = true;
        if (arenaEngageFirstAttackDelay > 0f && !_isDead)
        {
            float wait = arenaEngageFirstAttackDelay;
            while (wait > 0f && !_isDead)
            {
                wait -= Time.deltaTime;
                UpdateMoveAnimation(0f);
                yield return null;
            }
        }
    }

    bool _externalIntroPaused;
    /// <summary>True while the arrival intro holds the boss (UI uses this to hide the boss HUD).</summary>
    public bool IsExternalIntroPaused => _externalIntroPaused;

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
            StopChaseDashGapCloser();
            Locomotion.StopRunStopMotion();
            UpdateMoveAnimation(0f);
            Locomotion.SetCombatStrafeAnimation(false);
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
        StopChaseDashGapCloser();

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
        ObservationTracker.RecordPlayerDefenseResponse(true);

        if (breakController != null && !_isDead && !_isUltimateVictim && !IsCombatRecoveryLockoutActive && !IsCombatRecoverySuperArmorActive && !breakController.IsInBreak)
            breakController.AddBreak(0f, BossBreakController.BreakSource.Parry);

        float resolvedStunDuration = stunDuration > 0.001f
            ? stunDuration
            : Mathf.Max(parryStunDuration, 0.38f);
        resolvedStunDuration = ResolveStringParryStun(resolvedStunDuration); // 3타 패링 → 짧은 그로기 (StringHitCues.cs)
        NotifyParried(resolvedStunDuration);
    }

    public void OnPerfectDodged(GameObject dodger)
    {
        ObservationTracker.RecordPlayerDefenseResponse(false);
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
        ApplyThirdHitGroggyPoseIfPending(); // StringHitCues.cs
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

        yield return StartCoroutine(Co_ParryBackstepRecovery());

        StageAttackRecovery(
            string.IsNullOrWhiteSpace(parryPunishSource) ? "PARRY" : parryPunishSource.Trim().ToUpperInvariant(),
            Mathf.Max(combatIdleTime, parryRecoveryDuration, parryPostAttackLockDuration),
            Mathf.Max(minimumParryPunishWindow, parryPunishWindowDuration),
            Mathf.Max(1f, parryPunishDamageMultiplier));
        BlockAttackSelectionFor(parryPostAttackLockDuration);
        TryStartCombatRecovery(BossRecoveryReason.ParryStun, BossState.CombatIdle, clearPunishWindow: false);
    }

    IEnumerator Co_ParryBackstepRecovery()
    {
        if (!useBackstepAfterParry || _isDead || _isUltimateVictim || parryBackstepDuration <= 0f)
            yield break;

        EnsureGameplayPlayerTarget();
        FaceToPlayerInstant();

        string retreatTrigger = ResolveBackstepTriggerName(backstepAnimTriggerName);
        if (!string.IsNullOrEmpty(retreatTrigger))
            PlayAnimTrigger(retreatTrigger);

        float elapsed = 0f;
        Vector3 backDir = -transform.forward;
        float distance = Mathf.Max(0f, parryBackstepDistance);
        if (useCollisionAwareMovement && distance > 0f)
            distance = Locomotion.MeasureMovementClearance(rb != null ? rb.position : transform.position, backDir, distance);

        while (!_isDead && elapsed < parryBackstepDuration)
        {
            float deltaTime = Time.deltaTime;
            if (distance > 0f)
                Locomotion.ApplyPressureMovementDelta(backDir * (distance * Mathf.Clamp01(deltaTime / parryBackstepDuration)));

            elapsed += deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }
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
        RefreshRuntimeForwardAttackHitboxPose();
        if (_currentPattern != null && attackHitbox != null)
            attackHitbox.ActivateWindow();
    }

    public void DeactivateHitbox()
    {
        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();
    }

    void EnsureRuntimeForwardBoxAttackHitbox()
    {
        if (!useRuntimeForwardBoxAttackHitbox)
            return;

        if (_runtimeForwardAttackBox != null)
        {
            RefreshRuntimeForwardAttackHitboxPose();
            attackHitbox = _runtimeForwardAttackBox.GetComponent<AttackHitbox>();
            return;
        }

        _originalAttackHitbox = attackHitbox;
        Transform existing = transform.Find("RuntimeForwardAttackHitbox");
        GameObject hitboxObject = existing != null ? existing.gameObject : new GameObject("RuntimeForwardAttackHitbox");
        hitboxObject.transform.SetParent(transform, false);
        ResetRuntimeForwardHitboxTransform(hitboxObject.transform);

        _runtimeForwardAttackBox = hitboxObject.GetComponent<BoxCollider>();
        if (_runtimeForwardAttackBox == null)
            _runtimeForwardAttackBox = hitboxObject.AddComponent<BoxCollider>();

        _runtimeForwardAttackBox.isTrigger = true;
        _runtimeForwardAttackBox.center = runtimeForwardHitboxCenter;
        _runtimeForwardAttackBox.size = runtimeForwardHitboxSize;
        _runtimeForwardAttackBox.enabled = false;

        AttackHitbox runtimeHitbox = hitboxObject.GetComponent<AttackHitbox>();
        if (runtimeHitbox == null)
            runtimeHitbox = hitboxObject.AddComponent<AttackHitbox>();

        if (_originalAttackHitbox != null && _originalAttackHitbox != runtimeHitbox)
        {
            runtimeHitbox.baseDamage = _originalAttackHitbox.baseDamage;
            runtimeHitbox.hitType = _originalAttackHitbox.hitType;
            runtimeHitbox.canParry = _originalAttackHitbox.canParry;
            runtimeHitbox.canPerfectDodge = _originalAttackHitbox.canPerfectDodge;
            runtimeHitbox.canGuard = _originalAttackHitbox.canGuard;
            runtimeHitbox.causesGuardBreak = _originalAttackHitbox.causesGuardBreak;
            runtimeHitbox.unblockable = _originalAttackHitbox.unblockable;
            runtimeHitbox.hitLayers = _originalAttackHitbox.hitLayers;
            runtimeHitbox.ignoreTriggerColliders = _originalAttackHitbox.ignoreTriggerColliders;
            runtimeHitbox.showRuntimeHitboxPreview = _originalAttackHitbox.showRuntimeHitboxPreview;
        }

        runtimeHitbox.attackerRoot = transform;
        runtimeHitbox.hitEachReceiverOncePerActivation = true;
        runtimeHitbox.useOneShotWindow = true;
        runtimeHitbox.useExpandedHitDetection = true;
        runtimeHitbox.expandedPadding = 0.2f;
        runtimeHitbox.expandedHitBufferSize = Mathf.Max(32, runtimeHitbox.expandedHitBufferSize);
        runtimeHitbox.expandedScanInterval = 0.04f;
        runtimeHitbox.useSweepHitDetection = true;
        runtimeHitbox.sweepStepDistance = 0.2f;
        runtimeHitbox.maxSweepSubsteps = 6;
        runtimeHitbox.DeactivateWindow();

        if (_originalAttackHitbox != null && _originalAttackHitbox != runtimeHitbox && disableOriginalAttackHitboxWhenUsingRuntimeBox)
        {
            _originalAttackHitbox.DeactivateWindow();
            _originalAttackHitbox.enabled = false;
        }

        attackHitbox = runtimeHitbox;
        _cachedAttackHitboxDefaults = false;
    }

    void RefreshRuntimeForwardAttackHitboxPose()
    {
        if (_runtimeForwardAttackBox == null)
            return;

        Transform hitboxTransform = _runtimeForwardAttackBox.transform;
        if (hitboxTransform.parent != transform)
            hitboxTransform.SetParent(transform, false);
        ResetRuntimeForwardHitboxTransform(hitboxTransform);
    }

    static void ResetRuntimeForwardHitboxTransform(Transform hitboxTransform)
    {
        if (hitboxTransform == null)
            return;

        hitboxTransform.localPosition = Vector3.zero;
        hitboxTransform.localRotation = Quaternion.identity;
        hitboxTransform.localScale = Vector3.one;
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
        _attackTimingControlsParryWindow = !forceAllPatternsParryable &&
            timingData.parryWindowEndTime > timingData.parryWindowStartTime + 0.001f;
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
        StopPerHitStringCues(); // BossController.StringHitCues.cs
        StopAlignedOpenerCue(); // BossController.HonestCues.cs
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
        if (handPatternTelegraph != null)
            handPatternTelegraph.Hide();
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
        if (TryDeferOpenerCueToHitbox(timingTelegraphType, duration)) // BossController.HonestCues.cs, 기본 off
            return;

        EmitAttackTimingTelegraph(timingTelegraphType, duration);
    }

    void EmitAttackTimingTelegraph(BossAttackTelegraphType timingTelegraphType, float duration)
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

        if (!TryPlayTintedOpenerHandTelegraph(timingTelegraphType, resolvedDuration)) // StringHitCues.cs, 기본 off
            PlayHandPatternTelegraph(resolvedDuration);

        if (useHudCue)
            OnAttackTelegraph?.Invoke(telegraphType, resolvedDuration, _attackTimingTelegraphLabel);
    }

    void PlayHandPatternTelegraph(float duration)
    {
        if (duration <= 0.01f)
            return;

        if (handPatternTelegraph == null)
            handPatternTelegraph = GetComponentInChildren<BossPatternHandTelegraph>(true);
        if (handPatternTelegraph != null)
            handPatternTelegraph.Play(duration);
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
        if (forceAllPatternsParryable && attackHitbox != null)
        {
            attackHitbox.canParry = true;
            attackHitbox.canGuard = true;
            attackHitbox.unblockable = false;
            attackHitbox.causesGuardBreak = false;
            return;
        }

        if (_attackTimingControlsParryWindow && attackHitbox != null)
            attackHitbox.canParry = false;
    }

    void RestoreAttackTimingParryState()
    {
        if (_attackTimingHasCachedCanParry && attackHitbox != null)
            attackHitbox.canParry = _attackTimingCachedCanParry;

        if (forceAllPatternsParryable && attackHitbox != null)
        {
            attackHitbox.canParry = true;
            attackHitbox.canGuard = true;
            attackHitbox.unblockable = false;
            attackHitbox.causesGuardBreak = false;
        }

        _attackTimingHasCachedCanParry = false;
        _attackTimingCachedCanParry = false;
    }

    void StartPatternCooldown(AttackPattern pattern, float now)
    {
        if (pattern == null)
            return;

        float cooldown = Mathf.Max(minimumPatternCooldown, BuildSelectorData(pattern).cooldown);
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
                ForceTimingDataParryable(ref timingData);
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

        ForceTimingDataParryable(ref timingData);
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

    internal static BossPhasePatternModifier CreateDefaultPhaseModifier(BossPatternId patternId, int phase)
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

        ForceTimingDataParryable(ref timingData);
        return timingData;
    }

    void ForceTimingDataParryable(ref BossAttackTimingData timingData)
    {
        if (!forceAllPatternsParryable)
            return;

        timingData.Normalize();
        timingData.telegraphType = BossAttackTelegraphType.Parryable;

        float attackOpen = timingData.hitboxOpenTime > 0.001f
            ? timingData.hitboxOpenTime
            : Mathf.Max(0.001f, timingData.telegraphDuration);
        float attackClose = timingData.hitboxCloseTime > attackOpen + 0.001f
            ? timingData.hitboxCloseTime
            : attackOpen + 0.28f;
        float lead = Mathf.Max(0.18f, parryWindowEaseLeadTime);
        float tail = Mathf.Max(0.18f, parryWindowEaseTailTime);
        float desiredStart = Mathf.Max(0f, attackOpen - lead);
        float desiredEnd = Mathf.Max(attackClose, attackOpen + 0.34f) + tail;

        if (timingData.parryWindowEndTime > timingData.parryWindowStartTime + 0.001f)
        {
            timingData.parryWindowStartTime = Mathf.Min(timingData.parryWindowStartTime, desiredStart);
            timingData.parryWindowEndTime = Mathf.Max(timingData.parryWindowEndTime, desiredEnd);
        }
        else
        {
            timingData.parryWindowStartTime = desiredStart;
            timingData.parryWindowEndTime = desiredEnd;
        }

        timingData.Normalize();
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

        if (!_isDead && !_isUltimateVictim)
            bossAnimator.speed = 1f;

        Locomotion.StopRunStopMotion();
        Locomotion.SetCombatStrafeAnimation(false);

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

    IEnumerator Co_WaitForAttackAnimationComplete(string stateName)
    {
        float maxWait = Mathf.Max(Mathf.Max(0.1f, maxAttackStateWaitTime), maxAttackAnimationWaitTime);
        if (!waitForAttackAnimationCompletion || bossAnimator == null || bossAnimator.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName))
        {
            yield return StartCoroutine(Co_WaitAttackStateSeconds(maxWait));
            yield break;
        }

        int stateHash = Animator.StringToHash(stateName);
        float elapsed = 0f;
        bool enteredState = false;

        while (elapsed < attackAnimationEnterTimeout && currentState == BossState.Attack && !_isDead)
        {
            if (IsAnimatorInAttackState(stateHash, stateName, out _))
            {
                enteredState = true;
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        while (elapsed < maxWait && currentState == BossState.Attack && !_isDead)
        {
            bool inState = IsAnimatorInAttackState(stateHash, stateName, out AnimatorStateInfo stateInfo);
            if (inState)
            {
                enteredState = true;
                if (!bossAnimator.IsInTransition(0) && stateInfo.normalizedTime >= attackAnimationCompletionNormalized)
                    break;
            }
            else if (enteredState && !bossAnimator.IsInTransition(0))
            {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator Co_WaitAttackStateSeconds(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && currentState == BossState.Attack && !_isDead)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    bool IsAnimatorInAttackState(int stateHash, string stateName, out AnimatorStateInfo stateInfo)
    {
        stateInfo = default;
        if (bossAnimator == null || bossAnimator.layerCount <= 0)
            return false;

        stateInfo = bossAnimator.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.shortNameHash == stateHash || stateInfo.fullPathHash == stateHash)
            return true;

        return !string.IsNullOrEmpty(stateName) && stateInfo.IsName(stateName);
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
                if (parameter.nameHash == BossLocomotion.AnimParam_MoveX)
                    _hasMoveXParam = true;

                if (parameter.nameHash == BossLocomotion.AnimParam_MoveY)
                    _hasMoveYParam = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                if (parameter.nameHash == BossLocomotion.AnimParam_IsCombatStrafing)
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

        if (_hasQuickshiftBTrigger)
        {
            PlayAnimTrigger("Quickshift_B");
            return;
        }

        if (bossAnimator.HasState(0, AnimParam_QuickshiftB))
        {
            bossAnimator.CrossFadeInFixedTime(AnimParam_QuickshiftB, parryStunTransitionDuration, 0);
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
        ReleaseThirdHitGroggyPose(); // StringHitCues.cs
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

    float ResolveCombatIdleEntryDistance()
    {
        float defaultHitReach = runtimeForwardHitboxCenter.z + Mathf.Max(0f, runtimeForwardHitboxSize.z) * 0.5f;
        float reliableDistance = Mathf.Max(ResolveAttackDecisionDistance(), defaultHitReach + meleeAttackStartRangePadding);
        return Mathf.Min(ResolvePressureEngageDistance(), reliableDistance + Mathf.Max(0f, combatIdleDistanceTolerance));
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
        float desiredDistance = Mathf.Max(triggerDistance, attackDistance * innerRatio);
        return Mathf.Min(desiredDistance, MaxReactiveBackstepDistance);
    }

    bool IsImmediateBackstepDistance(float distanceToPlayer)
    {
        return distanceToPlayer <= ResolveBackstepDecisionDistance() * 0.88f;
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
        float clearance = Locomotion.MeasureMovementClearance(currentPos, awayFromPlayer, requestedDistance);
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

        bool immediateBackstep = IsImmediateBackstepDistance(distanceToPlayer);

        if (!immediateBackstep && !HasSatisfiedBackstepPressureHold())
            return false;

        if (!immediateBackstep && !IsPlayerInsideBackstepFrontArc())
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

        if (IsHoldingForArenaEntry)
        {
            yield return Co_HoldForArenaEntry();
            if (_isDead || playerTarget == null)
                yield break;
        }

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        UpdateClosePressureState(distance);
        UpdateMoveAnimation(0f);

        float attackDistance = ResolveAttackDecisionDistance();
        if (TryStartDirectSwordWaveAttack(distance, "Detect"))
            yield break;

        if (ShouldStartChaseDashGapCloser(distance))
        {
            SetState(BossState.Move);
            yield break;
        }

        bool canRequestAttack = CanRequestAttackSelection();
        bool hasPatternAtDistance = canRequestAttack && distance > attackDistance && HasAttackPlanAtDistance(distance);

        if (canRequestAttack && distance <= attackDistance)
            SetState(BossState.Attack);
        else if (canRequestAttack && hasPatternAtDistance)
            SetState(BossState.Attack);
        else if (canRequestAttack && ShouldForceNoPatternFallbackAttack(distance, hasPatternAtDistance))
            SetState(BossState.Attack);
        else if (distance <= ResolveCombatIdleEntryDistance())
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

            if (ShouldInterruptIdleForReactiveSpecial(distance)) // SpecialPatterns.cs: 도약 내려찍기로 거리 좁히기 / 회복 응징
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            if (TryStartDirectSwordWaveAttack(distance, "Move"))
                yield break;

            bool canRequestAttack = CanRequestAttackSelection();
            float attackDecisionDistance = ResolveAttackDecisionDistance();
            if (ShouldStartChaseDashGapCloser(distance))
            {
                yield return StartCoroutine(Co_ChaseDashGapCloser());
                if (currentState == BossState.Move && !_isDead)
                {
                    float finalDistance = playerTarget != null
                        ? Vector3.Distance(transform.position, playerTarget.position)
                        : float.PositiveInfinity;
                    SetState(finalDistance <= ResolveAttackDecisionDistance() ? BossState.Attack : BossState.Move, true);
                }
                yield break;
            }

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

            if (distance <= ResolveCombatIdleEntryDistance())
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.CombatIdle);
                yield break;
            }

            Vector3 dir = Locomotion.GetChaseDirection(toPlayer);
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
            Locomotion.ApplyMovementDelta(delta);

            UpdateMoveAnimation(moveSpeedMultiplier, dir, false);
            yield return null;
        }
    }

    bool ShouldStartChaseDashGapCloser(float distanceToPlayer)
    {
        if (!useChaseDashGapCloser || _isDead || _externalIntroPaused || currentState == BossState.Attack)
            return false;
        if (playerTarget == null || !IsValidGameplayPlayerTarget(playerTarget))
            return false;
        if (Time.time < _nextChaseDashAllowedAt)
            return false;

        float enterDistance = Mathf.Max(ResolveAttackDecisionDistance(), chaseDashAttackEnterDistance);
        return distanceToPlayer >= Mathf.Max(chaseDashTriggerDistance, enterDistance + 0.15f);
    }

    IEnumerator Co_ChaseDashGapCloser()
    {
        _isChaseDashGapClosing = true;
        StartChaseDashAfterimage();
        if (enableChaseDashDebugLog)
            GameLog.Info("[BossGapCloser] ChaseDash started", this);

        float elapsed = 0f;
        float nextRepathAt = 0f;
        float stepElapsed = 0f;
        Vector3 stepDirection = Vector3.zero;
        _chaseDashStepTarget = transform.position;

        while (currentState == BossState.Move && !_isDead && elapsed < maxChaseDashDuration)
        {
            EnsureGameplayPlayerTarget();
            if (playerTarget == null || !IsValidGameplayPlayerTarget(playerTarget))
                break;

            Vector3 toPlayer = playerTarget.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            if (distance <= Mathf.Max(chaseDashStopDistance, chaseDashAttackEnterDistance))
                break;

            if (Time.time >= nextRepathAt || stepDirection.sqrMagnitude <= 0.0001f || stepElapsed >= chaseDashStepDuration)
            {
                stepDirection = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : transform.forward;
                float stepDistance = Mathf.Min(chaseDashStepDistance, Mathf.Max(0f, distance - chaseDashStopDistance));
                _chaseDashStepTarget = transform.position + stepDirection * stepDistance;
                nextRepathAt = Time.time + Mathf.Max(0.02f, chaseDashRepathInterval);
                stepElapsed = 0f;
            }

            if (stepDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(stepDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    look,
                    chaseDashRotationSpeed * Time.deltaTime);

                float moveThisFrame = chaseDashStepSpeed * Time.deltaTime;
                Vector3 remaining = _chaseDashStepTarget - transform.position;
                remaining.y = 0f;
                if (remaining.sqrMagnitude > 0.0001f)
                    moveThisFrame = Mathf.Min(moveThisFrame, remaining.magnitude);

                Locomotion.ApplyPressureMovementDelta(stepDirection * moveThisFrame);
                UpdateMoveAnimation(chaseDashMoveAnimSpeed, stepDirection, false);
            }

            stepElapsed += Time.deltaTime;
            elapsed += Time.deltaTime;

            if (stepElapsed >= chaseDashStepDuration && chaseDashStepCooldown > 0f)
            {
                float cooldown = 0f;
                while (cooldown < chaseDashStepCooldown && currentState == BossState.Move && !_isDead)
                {
                    cooldown += Time.deltaTime;
                    elapsed += Time.deltaTime;
                    UpdateMoveAnimation(chaseDashMoveAnimSpeed, stepDirection, false);
                    yield return null;
                }
            }

            yield return null;
        }

        StopChaseDashGapCloser();
        _nextChaseDashAllowedAt = Time.time + Mathf.Max(0f, chaseDashRetryCooldown);
        if (enableChaseDashDebugLog)
            GameLog.Info("[BossGapCloser] ChaseDash ended", this);
    }

    void StartChaseDashAfterimage()
    {
        if (!enableChaseDashAfterimage)
            return;

        PerfectDodgeAfterImageEffect effect = ResolveChaseDashAfterimageEffect();
        if (effect != null)
            effect.StartContinuousTrail(Mathf.Max(chaseDashAfterimageDuration, maxChaseDashDuration + 0.05f));
    }

    void StopChaseDashGapCloser()
    {
        if (!_isChaseDashGapClosing && chaseDashAfterimageEffect == null)
            return;

        _isChaseDashGapClosing = false;
        if (chaseDashAfterimageEffect != null)
            chaseDashAfterimageEffect.StopContinuousTrail();
        UpdateMoveAnimation(0f);
    }

    PerfectDodgeAfterImageEffect ResolveChaseDashAfterimageEffect()
    {
        if (chaseDashAfterimageEffect == null)
            chaseDashAfterimageEffect = GetComponent<PerfectDodgeAfterImageEffect>();
        if (chaseDashAfterimageEffect == null)
            chaseDashAfterimageEffect = gameObject.AddComponent<PerfectDodgeAfterImageEffect>();
        return chaseDashAfterimageEffect;
    }

    IEnumerator Co_CombatIdle()
    {
        bool hasForcedIdleDuration = _pendingCombatIdleDuration > 0.01f;
        float t = hasForcedIdleDuration ? _pendingCombatIdleDuration : combatIdleTime;
        _pendingCombatIdleDuration = -1f;

        if (ShouldTriggerCombatIdleRetreat())
        {
            if (combatIdlePreRetreatPause > 0.01f)
                yield return StartCoroutine(Co_CombatIdlePause(Mathf.Min(combatIdlePreRetreatPause, MaxCombatIdleRetreatPause)));

            yield return StartCoroutine(Co_CombatIdleRetreat());

            if (combatIdlePostRetreatPause > 0.01f)
                yield return StartCoroutine(Co_CombatIdlePause(Mathf.Min(combatIdlePostRetreatPause, MaxCombatIdleRetreatPause)));
        }

        t = hasForcedIdleDuration
            ? Mathf.Max(t, combatIdleTime)
            : Mathf.Min(Mathf.Max(t, combatIdleTime), ResolveCombatIdleAttackRetryDelay());
        float elapsed = 0f;
        while (t > 0f && !_isDead)
        {
            EnsureGameplayPlayerTarget();
            if (playerTarget != null)
            {
                float distance = Vector3.Distance(transform.position, playerTarget.position);
                if (!hasForcedIdleDuration && TryStartDirectSwordWaveAttack(distance, "CombatIdle"))
                    yield break;

                if (!hasForcedIdleDuration && ShouldStartChaseDashGapCloser(distance))
                {
                    SetState(BossState.Move);
                    yield break;
                }

                if (ShouldInterruptIdleForReactiveSpecial(distance, hasForcedIdleDuration)) // SpecialPatterns.cs: 회복 → 도약 (경직 후 대기 중에도 회복 응징은 허용)
                {
                    UpdateMoveAnimation(0f);
                    SetState(BossState.Attack);
                    yield break;
                }

                bool canRequestAttack = !hasForcedIdleDuration && CanRequestAttackSelection();
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

        // 특수 패턴 (충격파 / 그림자 추적 도약 내려찍기) — BossController.SpecialPatterns.cs, 기본 off
        if (!isQueuedFollowUp && _pendingImmediateAttackPattern == null && TryChooseSpecialPattern(distance, out BossSpecialPatternKind specialKind))
        {
            yield return Co_RunSpecialPattern(specialKind);
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
        NotifyReactivePatternExecuted(pattern); // SpecialPatterns.cs
        _recentPatterns.Record(pattern, recentPatternMemory);
        StartPatternCooldown(pattern, Time.time);
        _selectorCache.MarkUsed(patternSelector, pattern, Time.time);
        AttackTelegraphType telegraphType = pattern.ResolveTelegraphType();
        float telegraphLeadTime = Mathf.Max(
            ResolveMinimumTelegraphLeadTime(pattern, isQueuedFollowUp),
            pattern.ResolveTelegraphLeadTime() * Tuning.ResolvePhaseTelegraphScale() * Tuning.ResolvePhaseTelegraphDurationMultiplier(pattern));
        string telegraphLabel = pattern.ResolveTelegraphLabel();
        AttackTimingStyle timingStyle = pattern.ResolveTimingStyle();
        float extraHoldDelay = pattern.ResolveExtraHoldDelay();
        string secondaryTelegraphLabel = pattern.ResolveSecondaryTelegraphLabel();
        bool canParry = pattern.ResolveCanParry();
        bool canPerfectDodge = pattern.ResolveCanPerfectDodge();
        bool canGuard = pattern.ResolveCanGuard();
        bool isUnblockable = pattern.ResolveIsUnblockable();
        bool causesGuardBreak = pattern.ResolveCausesGuardBreak();
        if (forceAllPatternsParryable)
        {
            canParry = true;
            canGuard = true;
            isUnblockable = false;
            causesGuardBreak = false;
            telegraphType = AttackTelegraphType.Parry;
        }
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

        // 큐 정직성 (BossController.HonestCues.cs, 기본 off): 트리거 직전 마지막 텔레그래프가 실제 첫 히트 순간에 끝나게
        float announcedLeadTime = telegraphLeadTime;
        float announcedHoldTime = extraHoldDelay;
        float announcedPoseTime = preAttackPoseDuration;
        if (IsHonestCueTimingEnabled)
        {
            float clipLead = ResolveHonestPreTelegraphClipLead(pattern);
            totalTelegraphDuration += clipLead;
            if (preAttackPoseDuration > 0.01f)
                announcedPoseTime += clipLead;
            else if (timingStyle == AttackTimingStyle.FakeOut && extraHoldDelay > 0.01f)
                announcedHoldTime += clipLead;
            else
                announcedLeadTime += clipLead + (timingStyle == AttackTimingStyle.Delayed && extraHoldDelay > 0.01f ? extraHoldDelay : 0f);
        }

        if (groundTelegraph != null && attackHitbox != null && attackHitbox.Collider != null)
            groundTelegraph.ShowCue(attackHitbox.Collider, telegraphType, totalTelegraphDuration, transform);

        if (patternVisuals != null)
            patternVisuals.StartVisualCue(telegraphType, announcedLeadTime);

        PlayHandPatternTelegraph(announcedLeadTime);

        OnAttackTelegraph?.Invoke(telegraphType, announcedLeadTime, telegraphLabel);
        NoteHonestPreTelegraph(announcedLeadTime); // HonestCues.cs (QA/학습용 시각 기록)

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
                patternVisuals.StartVisualCue(telegraphType, announcedHoldTime);

            PlayHandPatternTelegraph(announcedHoldTime);

            OnAttackTelegraph?.Invoke(telegraphType, announcedHoldTime, secondaryTelegraphLabel);
            NoteHonestPreTelegraph(announcedHoldTime);
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
                patternVisuals.StartVisualCue(telegraphType, announcedPoseTime);

            PlayHandPatternTelegraph(announcedPoseTime);

            OnAttackTelegraph?.Invoke(telegraphType, announcedPoseTime, preAttackPoseLabel);
            NoteHonestPreTelegraph(announcedPoseTime);

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
            float resolvedDamage = Mathf.Max(0f, pattern.damageAmount * Mathf.Max(0f, outgoingDamageMultiplier) * Tuning.ResolvePhaseDamageMultiplier(pattern) * Tuning.ResolveDifficultyDamageMultiplier());
            attackHitbox.canPerfectDodge = canPerfectDodge;
            attackHitbox.Configure(resolvedDamage, canParry, canGuard, isUnblockable, causesGuardBreak, transform);
        }

        bool hasTimingData = TryResolveAttackTimingData(pattern, out BossAttackTimingData timingData);
        if (hasTimingData)
        {
            Tuning.ApplyPhaseAttackTimingModifiers(pattern, ref timingData);
            Tuning.ApplyDifficultyAttackTimingModifiers(ref timingData);
            Tuning.ApplyParryWindowEase(ref timingData);
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
        NoteHonestAttackTrigger(pattern); // HonestCues.cs: 트리거 시각 (트리거→첫 히트 학습 기준)

        if (hasTimingData)
            StartAttackTimingController(timingData);
        if (hasTimingData)
            StartPerHitStringCues(pattern); // 다단 후속타 개별 큐/패리 창 (BossController.StringHitCues.cs, 기본 off)

        if (pattern.ResolveFiresSwordWaveProjectile())
        {
            float projectileDamage = Mathf.Max(0f, pattern.damageAmount * Mathf.Max(0f, outgoingDamageMultiplier) * Tuning.ResolvePhaseDamageMultiplier(pattern) * Tuning.ResolveDifficultyDamageMultiplier());
            StartCoroutine(Co_FireSwordWaveProjectile(pattern, projectileDamage, canParry, canPerfectDodge, canGuard, isUnblockable, causesGuardBreak));
        }

        if (enableStateLogs)
            LogState($"[BossFSM] Attack 패턴 실행: {pattern.patternName} ({pattern.animTriggerName})");

        // 이름 기반 스테이트 대기가 아니라,
        // "Attack 상태 + 최대 대기 시간" 기준으로만 기다리는 방식 (안전장치).
        yield return StartCoroutine(Co_WaitForAttackAnimationComplete(pattern.animTriggerName));

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
            BossPostActionSettings difficultyPostActionSettings = Tuning.ResolveDifficultyPostActionSettings(postActionSettings);
            yield return StartCoroutine(_postActionExecutor.Execute(
                actionType,
                context,
                difficultyPostActionSettings,
                Locomotion.ApplyPressureMovementDelta,
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
            if (IsRangedPatternDisabled(pattern, data))
                continue;

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

        if (!disableRangedPatterns &&
            TryGetExecutablePatternById(BossPatternId.SwordWave, distanceToPlayer, true, out selectedPattern))
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
        float reliableStartDistance = gateMeleeAttackStartByRuntimeHitbox
            ? ResolveReliablePatternStartDistance(pattern)
            : pattern.maxRange;
        float stopRange = Mathf.Clamp(data.engageStopRange, pattern.minRange, Mathf.Min(pattern.maxRange, reliableStartDistance));
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

            Vector3 direction = Locomotion.GetChaseDirection(toTarget);
            if (direction.sqrMagnitude <= 0.0001f)
                break;

            Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                look,
                Mathf.Max(closeChaseTurnSpeed, chaseTurnSpeed) * Time.deltaTime);

            Locomotion.ApplyPressureMovementDelta(direction * (moveSpeed * speedMultiplier * Time.deltaTime));
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
        if (disableRangedPatterns && data.engageMode == BossPatternEngageMode.UseRangedFallback)
            return false;

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
            if (IsRangedPatternDisabled(candidate, data))
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

        return _recentPatterns.WasUsedRecently(pattern != null ? pattern.patternName : null, lookback, recentPatternMemory);
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

        GameLog.Info("[BossEngage] start " + patternId, this);
    }

    void LogEngageSuccess(BossPatternId patternId)
    {
        if (!enableEngageDebugLog)
            return;

        GameLog.Info("[BossEngage] success " + patternId, this);
    }

    void LogEngageFail(BossPatternId patternId)
    {
        if (!enableEngageDebugLog)
            return;

        GameLog.Info("[BossEngage] fail " + patternId, this);
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
            return Mathf.Max(combatIdleTime, minimumPostAttackIdleDuration, postPatternDelayAfterAnimation);

        float recovery = Mathf.Max(0f, pattern.ResolveRecoveryTime());
        if (isQueuedFollowUp)
            recovery += Mathf.Max(0f, followUpRecoveryTax);

        recovery *= Tuning.ResolvePhaseRecoveryDurationMultiplier(pattern);
        recovery += Mathf.Max(0f, globalPostAttackRecoveryPadding);
        return Mathf.Max(Mathf.Max(combatIdleTime, minimumPostAttackIdleDuration, postPatternDelayAfterAnimation), recovery);
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
        Locomotion.ApplyMovementDelta(delta);
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

        // 풀 재사용 (BossSwordWaveProjectile 내부 정적 풀)
        BossSwordWaveProjectile projectile = BossSwordWaveProjectile.Spawn(origin, rotation, gameObject.layer);

        if (ShouldLogSwordWave())
            GameLog.Info($"[BossSwordWave] spawned origin={origin} forward={(rotation * Vector3.forward)} speed={pattern.swordWaveSpeed:0.00}", this);

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
        float clearance = Locomotion.MeasureMovementClearance(rb != null ? rb.position : transform.position, away, requestedDistance);
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
                ? Locomotion.MeasureMovementClearance(rb != null ? rb.position : transform.position, backDir, requestedDistance)
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
                Locomotion.ApplyMovementDelta(delta);
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
            Locomotion.ApplyPressureMovementDelta,
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
        _pendingCombatIdleDuration = Mathf.Max(_pendingCombatIdleDuration, Mathf.Max(0f, combatRecoverySettings.postRecoveryCombatIdleDuration));
        if (reason == BossRecoveryReason.ParryStun)
            _pendingCombatIdleDuration = Mathf.Max(_pendingCombatIdleDuration, Mathf.Max(0f, parryPostAttackLockDuration));
        else if (reason == BossRecoveryReason.UltimateVictim)
            _pendingCombatIdleDuration = Mathf.Max(_pendingCombatIdleDuration, Mathf.Max(0f, ultimateVictimPostAttackLockDuration));

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

        GameLog.Info(
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
        Locomotion.SetCombatStrafeAnimation(false);
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
        if (patternSelector == null)
            patternSelector = new BossPatternSelector();
        if (patternSelectorHistory == null)
            patternSelectorHistory = new BossPatternHistory();

        _selectorCache.Rebuild(allPatterns, BuildSelectorDataFunc, patternSelector, patternSelectorHistory, this);
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

        if (Tuning.TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier phaseModifier))
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

        data.baseWeight *= ObservationTracker.ResolvePlayerStatePatternWeightMultiplier(pattern);
        data.baseWeight *= ResolveReactivePatternWeightMultiplier(pattern); // SpecialPatterns.cs
        data.baseWeight *= ResolveSpatialPatternWeightMultiplier(data.patternId);
        data.baseWeight *= Tuning.ResolveDifficultyPatternWeightMultiplier();
        data.cooldown *= Tuning.ResolveDifficultyCooldownMultiplier();
        data.engageMoveSpeedMultiplier *= ObservationTracker.ResolvePlayerStateEngageSpeedMultiplier(data.patternId);
        data.engageMoveSpeedMultiplier *= Tuning.ResolveDifficultyEngageSpeedMultiplier();
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

        if (ObservationTracker.IsFarPressureActive() || ObservationTracker.Observation.playerIsMovingAway)
        {
            if (data.patternId == BossPatternId.SwordWave || data.patternId == BossPatternId.DashSlash)
                data.postActionType = BossPatternPostActionType.ChaseReposition;
            return;
        }

        if (ObservationTracker.IsPlayerAtBackAngle())
        {
            data.postActionType = BossPatternPostActionType.Recenter;
            return;
        }

        if (ObservationTracker.IsPlayerAtSideAngle() && data.postActionType == BossPatternPostActionType.CombatIdle)
            data.postActionType = BossPatternPostActionType.Recenter;

        if (ObservationTracker.Observation.playerRecentlyHitBoss || ObservationTracker.Observation.playerIsApproaching)
        {
            if (data.patternId == BossPatternId.HeavySlash || data.patternId == BossPatternId.BackstepSlash)
                data.postActionType = BossPatternPostActionType.Backstep;
        }

        if (!ObservationTracker.IsCloseLingerActive())
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
            pathBlocked = !Locomotion.HasMovementClearance(transform.position, toPlayerDirection, Mathf.Min(distance, Mathf.Max(0.05f, obstacleProbeDistance)));

            float playerWallProbe = Mathf.Max(0f, arenaAwarenessSettings.playerWallProbeDistance);
            if (playerWallProbe > 0.001f)
            {
                Vector3 awayFromBoss = toPlayerDirection;
                playerNearWall = !Locomotion.HasMovementClearance(playerTarget.position, awayFromBoss, playerWallProbe);
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
        if (!Locomotion.HasMovementClearance(transform.position, centerDirection, Mathf.Max(0.05f, spatialBiasProbeDistance)))
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
        return !Locomotion.HasMovementClearance(transform.position, direction.normalized, probeDistance);
    }

#if UNITY_EDITOR
    public bool TryGetResolvedPhasePatternModifierForValidation(
        AttackPattern pattern,
        int phase,
        out BossPhasePatternModifier modifier)
    {
        return Tuning.TryResolvePhasePatternModifier(pattern, phase, out modifier);
    }
#endif

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
        PlayUltimateVictimHitReaction();
        _pendingCombatIdleDuration = Mathf.Max(
            Mathf.Max(0f, combatIdleTime),
            Mathf.Max(Mathf.Max(0f, ultimateVictimRecoveryDuration), Mathf.Max(0f, ultimateVictimPostAttackLockDuration)));
        BlockAttackSelectionFor(ultimateVictimPostAttackLockDuration);
        TryStartCombatRecovery(BossRecoveryReason.UltimateVictim, BossState.CombatIdle);
    }

    void RefreshGameplayPlayerTargetAfterRecovery()
    {
        EnsureGameplayPlayerTarget(forceRefresh: true);
        FaceToPlayerInstant();
    }

    void PlayUltimateVictimHitReaction()
    {
        if (bossAnimator == null || _isDead)
            return;

        if (HasAnimatorTrigger(ultimateVictimHitTriggerName))
        {
            PlayAnimTrigger(ultimateVictimHitTriggerName);
            return;
        }

        // 상태가 없는 컨트롤러에선 CrossFade 가 "Animator.GotoState: State could not be found" 경고만 남기므로 확인 후 호출
        if (!string.IsNullOrWhiteSpace(ultimateVictimHitStateName) &&
            bossAnimator.HasState(0, Animator.StringToHash(ultimateVictimHitStateName)))
            bossAnimator.CrossFadeInFixedTime(ultimateVictimHitStateName, 0.05f, 0);
    }

    bool HasAnimatorTrigger(string triggerName)
    {
        if (bossAnimator == null || string.IsNullOrWhiteSpace(triggerName))
            return false;

        AnimatorControllerParameter[] parameters = bossAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Trigger &&
                string.Equals(parameters[i].name, triggerName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

        ApplyRuntimeForwardHitboxShape(pattern);
    }

    void ApplyRuntimeForwardHitboxShape(AttackPattern pattern)
    {
        if (_runtimeForwardAttackBox == null || pattern == null)
            return;

        ResolveRuntimeForwardHitboxShape(pattern, out Vector3 center, out Vector3 size);
        _runtimeForwardAttackBox.center = center;
        _runtimeForwardAttackBox.size = size;
    }

    void ResolveRuntimeForwardHitboxShape(AttackPattern pattern, out Vector3 center, out Vector3 size)
    {
        center = runtimeForwardHitboxCenter;
        size = runtimeForwardHitboxSize;

        switch (ResolvePatternId(pattern))
        {
            case BossPatternId.DashSlash:
                center = new Vector3(0f, 0.7f, 1.7f);
                size = new Vector3(3.25f, 2.75f, 3.35f);
                break;
            case BossPatternId.HeavySlash:
                center = new Vector3(0f, 0.68f, 1.42f);
                size = new Vector3(3.85f, 2.85f, 2.85f);
                break;
            case BossPatternId.BackstepSlash:
                center = new Vector3(0f, 0.68f, 1.18f);
                size = new Vector3(3.35f, 2.7f, 2.45f);
                break;
            case BossPatternId.QuickSlash:
                center = new Vector3(0f, 0.68f, 1.2f);
                size = new Vector3(3.05f, 2.6f, 2.35f);
                break;
            case BossPatternId.SwordWave:
                center = new Vector3(0f, 0.68f, 1.05f);
                size = new Vector3(2.5f, 2.45f, 1.9f);
                break;
        }
    }

    public bool IsPatternReliablyInHitRange(AttackPattern pattern, float distanceToPlayer)
    {
        if (!gateMeleeAttackStartByRuntimeHitbox || pattern == null)
            return true;

        if (pattern.ResolveFiresSwordWaveProjectile() || ResolvePatternId(pattern) == BossPatternId.SwordWave)
            return true;

        return distanceToPlayer <= ResolveReliablePatternStartDistance(pattern);
    }

    float ResolveReliablePatternStartDistance(AttackPattern pattern)
    {
        if (pattern == null)
            return ResolveAttackDecisionDistance();

        if (pattern.ResolveFiresSwordWaveProjectile() || ResolvePatternId(pattern) == BossPatternId.SwordWave)
            return pattern.maxRange;

        ResolveRuntimeForwardHitboxShape(pattern, out Vector3 center, out Vector3 size);
        float forwardReach = Mathf.Max(0f, center.z) + Mathf.Max(0f, size.z) * 0.5f;
        float reliableReach = forwardReach + Mathf.Max(0f, meleeAttackStartRangePadding);
        return Mathf.Clamp(reliableReach, pattern.minRange, Mathf.Max(pattern.minRange, pattern.maxRange));
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
        _queuedFollowUpDelay = Mathf.Max(
            Mathf.Max(0f, minimumFollowUpDelayAfterAttack),
            Mathf.Max(0f, sourcePattern.ResolveFollowUpDelay() + globalFollowUpDelayPadding));
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

        if (Tuning.TryResolvePhasePatternModifier(sourcePattern, out BossPhasePatternModifier phaseModifier) &&
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

        return _weightedPicker.Pick(candidates, (this, sourcePattern), s_followUpWeightOf, 0.01f);
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
                ? Locomotion.MeasureMovementClearance(rb != null ? rb.position : transform.position, backDir, requestedDistance)
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
        float pressureDistance = ResolveCombatIdleEntryDistance();
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

    void BlockAttackSelectionFor(float duration)
    {
        if (duration <= 0f)
            return;

        _attackSelectionBlockedUntil = Mathf.Max(_attackSelectionBlockedUntil, Time.time + duration);
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
            if (IsRangedPatternDisabled(p))
                continue;
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

        if (!_selectorCache.HasData || _selectorCache.IsDirty(allPatterns))
        {
            RebuildPatternSelectorCache();
        }
        else
        {
            _selectorCache.RefreshData(allPatterns, BuildSelectorDataFunc, Time.time);
        }

        _selectorCache.SyncRuntimeCooldowns(Time.time);

        BossPatternContext context = new BossPatternContext(
            distanceToPlayer,
            ResolveAngleToPlayer(),
            CurrentPhase,
            Time.time,
            ObservationTracker.Observation);

        if (!patternSelector.TrySelect(context, out int selectedIndex, out _))
            return false;

        if (!_selectorCache.TryGetPattern(selectedIndex, out selectedPattern))
            return false;

        if (IsRangedPatternDisabled(selectedPattern))
            return false;

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
        _recentPatterns.AppendDebug(builder, recentPatternMemory);

        if (allPatterns != null)
        {
            for (int i = 0; i < allPatterns.Count; i++)
            {
                AttackPattern pattern = allPatterns[i];
                AppendPatternCandidateDebug(builder, pattern, distanceToPlayer);
            }
        }

        GameLog.Info(builder.ToString(), this);
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
        if (IsRangedPatternDisabled(pattern, data))
            return "ranged_disabled";
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
        if (disableRangedPatterns)
            return false;

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
                GameLog.Info($"[BossSwordWave] selected distance={distanceToPlayer:0.00} pattern={pattern.patternName} trigger={pattern.animTriggerName}", this);
            return true;
        }

        return false;
    }

    AttackPattern GetOrCreateSwordWavePattern()
    {
        if (disableRangedPatterns)
            return null;

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
        if (disableRangedPatterns)
            return false;

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
            GameLog.Info($"[BossSwordWave] direct-start source={sourceState} distance={distanceToPlayer:0.00}", this);
        SetState(BossState.Attack);
        return true;
    }

    bool TryStartSwordWaveAttack(float distanceToPlayer, string sourceState)
    {
        if (disableRangedPatterns)
            return false;

        if (_externalIntroPaused || IsPhaseTransitionAttackLocked())
            return false;

        if (!TryGetExecutableSwordWavePattern(distanceToPlayer, out AttackPattern swordWavePattern))
            return false;

        _pendingImmediateAttackPattern = swordWavePattern;
        UpdateMoveAnimation(0f);

        if (ShouldLogSwordWave())
            GameLog.Info($"[BossSwordWave] force-attack source={sourceState} distance={distanceToPlayer:0.00}", this);

        SetState(BossState.Attack);
        return true;
    }

    bool ShouldLogSwordWave()
    {
        return enableVerboseCombatLogs && debugSwordWaveLogs;
    }

    bool IsRangedPatternDisabled(AttackPattern pattern)
    {
        if (!disableRangedPatterns || pattern == null)
            return false;

        return pattern.firesSwordWaveProjectile ||
               pattern.ResolveFiresSwordWaveProjectile() ||
               ResolvePatternId(pattern) == BossPatternId.SwordWave ||
               string.Equals(pattern.patternName, "SwordWave", StringComparison.OrdinalIgnoreCase);
    }

    bool IsRangedPatternDisabled(AttackPattern pattern, BossPatternData data)
    {
        if (!disableRangedPatterns || pattern == null)
            return false;

        return data.patternId == BossPatternId.SwordWave ||
               pattern.firesSwordWaveProjectile ||
               pattern.ResolveFiresSwordWaveProjectile() ||
               string.Equals(pattern.patternName, "SwordWave", StringComparison.OrdinalIgnoreCase);
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
            if (IsRangedPatternDisabled(pattern))
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
        if (disableRangedPatterns)
            return null;

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
            if (IsRangedPatternDisabled(pattern))
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

        float maxRange = gateMeleeAttackStartByRuntimeHitbox
            ? ResolveReliablePatternStartDistance(pattern)
            : pattern.maxRange;
        if (distanceToPlayer > maxRange)
            return distanceToPlayer - maxRange;

        return 0f;
    }

    AttackPattern SelectWeightedPattern(List<AttackPattern> candidates)
    {
        return _weightedPicker.Pick(candidates, this, s_phaseWeightOf, 0f);
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

    // ==================== 이동 애니 (BlendTree) → BossLocomotion ====================

    void UpdateMoveAnimation(float target01) => Locomotion.UpdateMoveAnimation(target01);

    void UpdateMoveAnimation(float target01, Vector3 worldMoveDirection) => Locomotion.UpdateMoveAnimation(target01, worldMoveDirection);

    void UpdateMoveAnimation(float target01, Vector3 worldMoveDirection, bool useDirectionalBlend) => Locomotion.UpdateMoveAnimation(target01, worldMoveDirection, useDirectionalBlend);


    public void PlayStrafeFootstepLeft()
    {
    }

    public void PlayStrafeFootstepRight()
    {
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
