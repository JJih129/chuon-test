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

        if (damageAmount >= 30)
            return AttackTelegraphType.Danger;

        if (maxRange >= 6f)
            return AttackTelegraphType.Dodge;

        return isParryable ? AttackTelegraphType.Parry : AttackTelegraphType.Dodge;
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
        switch (ResolveTelegraphType())
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

    public bool ResolveIsUnblockable()
    {
        return ResolveTelegraphType() == AttackTelegraphType.Danger;
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

public class BossController : MonoBehaviour, IUltimateVictimState
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

    [Tooltip("플레이어 행동 기반 패턴 제어용 트래커(선택)")]
    public PlayerTracker playerTracker;

    [Header("FSM 기본/타이밍 설정 (한글 설명)")]
    [Tooltip("현재 보스 FSM 상태 (디버그 확인용)")]
    public BossState currentState = BossState.IntroIdle;

    [Tooltip("인트로 연출용 대기 시간 (초). 0이면 바로 전투 시작.")]
    public float introIdleDuration = 0.5f;

    [Tooltip("공격 후 CombatIdle 상태 유지 시간(초). 0이면 바로 다음 상태로 이동.")]
    public float combatIdleTime = 0.3f;

    [Tooltip("공격 상태에서 애니메이션 종료를 기다리는 최대 시간 (초). 안전장치 역할")]
    public float maxAttackStateWaitTime = 2.0f;

    [Header("Phase Tuning")]
    [Range(0.15f, 0.95f)] [SerializeField] private float phaseTwoThresholdNormalized = 0.66f;
    [Range(0.05f, 0.75f)] [SerializeField] private float phaseThreeThresholdNormalized = 0.33f;
    [SerializeField] private float phaseTwoTelegraphScale = 0.92f;
    [SerializeField] private float phaseThreeTelegraphScale = 0.82f;
    [Range(0f, 0.5f)] [SerializeField] private float phaseTwoFollowUpChanceBonus = 0.08f;
    [Range(0f, 0.5f)] [SerializeField] private float phaseThreeFollowUpChanceBonus = 0.16f;
    [SerializeField] private int phaseThreeExtraFollowUpChains = 1;
    [SerializeField] private float phaseEntryPressureDuration = 5f;
    [SerializeField] private float phaseEntryUnlockedPatternWeight = 1.75f;

    [Header("Debug")]
    [SerializeField] private bool enableStateLogs = false;

    private Coroutine _stateRoutine;
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

    [Tooltip("Blend Tree로 전달할 MoveSpeed 보간 시간 (0에 가까울수록 즉각 반응)")]
    [Range(0.01f, 0.5f)]
    public float moveAnimDamp = 0.1f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float obstacleProbeDistance = 2.2f;
    [SerializeField] private float obstacleProbeAngle = 35f;
    [SerializeField] private float obstacleSideProbeAngle = 65f;
    [SerializeField] private float detourCommitTime = 0.35f;
    [SerializeField] private float chaseDirectionRefreshInterval = 1f / 12f;

    private static readonly int AnimParam_MoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int AnimParam_IsBreak   = Animator.StringToHash("IsBreak");
    private static readonly int AnimParam_IsDead    = Animator.StringToHash("IsDead");

    private float _moveBlend; // 0~1
    private float _lastAppliedMoveBlend = float.NaN;
    private Vector3 _cachedDetourDirection;
    private float _cachedDetourUntil;
    private Vector3 _cachedChaseDesiredDirection;
    private Vector3 _cachedChaseDirection;
    private float _nextChaseDirectionRefreshAt;

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
    private bool _ultimateVictimRigidbodyWasKinematic;
    private RigidbodyConstraints _ultimateVictimRigidbodyConstraints;
    private Vector3 _ultimateVictimOriginalPosition;
    private Quaternion _ultimateVictimOriginalRotation = Quaternion.identity;
    private bool _hasUltimateVictimOriginalPose;
    private Vector3 _ultimateVictimAnchorPosition;
    private Quaternion _ultimateVictimAnchorRotation = Quaternion.identity;
    private bool _hasUltimateVictimAnchor;
    private bool _delayDestroyUntilUltimateVictimEnds;

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

    [Tooltip("백스텝 애니메이션 트리거 이름 (Animator 트리거 파라미터와 동일하게 설정)")]
    public string backstepAnimTriggerName = "Backstep";

    [Tooltip("백스텝 중 뒤로 빠지는 시간(초). 루트 모션 사용 시 0으로 두고 애니메이션만 재생 가능")]
    public float backstepDuration = 0.6f;

    [Tooltip("백스텝 중 뒤로 빠지는 속도(m/s). 루트 모션을 쓴다면 0으로 두는 것을 권장")]
    public float backstepSpeed = 4.5f;

    [Tooltip("백스텝 재사용 쿨타임(초). 너무 자주 쓰지 않도록 제한")]
    public float backstepCooldown = 3.0f;

    private float _backstepCooldownTimer;
    readonly RaycastHit[] _movementSweepHits = new RaycastHit[16];
    readonly List<AttackPattern> _patternCandidatesCache = new List<AttackPattern>(16);
    readonly List<AttackPattern> _followUpCandidatesCache = new List<AttackPattern>(16);

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

    void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider>();

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

        RefreshPatternCooldownState();
    }

    void Start()
    {
        RefreshPhaseState(true);
        SetState(BossState.IntroIdle);
    }

    void OnDestroy()
    {
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
                        multiplier = 1.42f;
                        break;
                    case AttackTelegraphType.Guard:
                        multiplier = 1.14f;
                        break;
                    case AttackTelegraphType.Parry:
                        multiplier = 0.90f;
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
                        multiplier = 1.55f;
                        break;
                    case AttackTelegraphType.Dodge:
                        multiplier = 1.26f;
                        break;
                    case AttackTelegraphType.Guard:
                        multiplier = 1.08f;
                        break;
                    case AttackTelegraphType.Parry:
                        multiplier = 0.72f;
                        break;
                }
                break;
        }

        if (isFollowUp && _currentPhase >= 2 && (type == AttackTelegraphType.Dodge || type == AttackTelegraphType.Danger))
            multiplier *= 1.10f;

        if (Time.time < _phaseEntryPressureUntilTime && pattern.ResolveMinPhase() == _currentPhase)
            multiplier *= Mathf.Max(1f, phaseEntryUnlockedPatternWeight);

        return multiplier;
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

        if (!useCollisionAwareRootMotion || currentState != BossState.Attack)
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
        if (currentState == newState && !forceRestart) return;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        var old = currentState;
        currentState = newState;
        LogState($"[BossFSM] {old} → {newState}");

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

    // ==================== 생존/브레이크 ====================

    void OnBossDied()
    {
        if (_isDead) return;
        _isDead = true;
        ClearPunishWindow();
        ClearQueuedFollowUp();

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

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
        ClearPunishWindow();
        ClearQueuedFollowUp();

        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();

        _currentPattern = null;
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

        SetState(BossState.Detect);
    }

    // ==================== 사망 후 파괴 처리 ====================

    IEnumerator Co_DestroyHierarchyAfterDead()
    {
        if (destroyDelayAfterDead > 0f)
            yield return new WaitForSeconds(destroyDelayAfterDead);

        FinalizeDeathAndDestroy();
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
            bossAnimator.ResetTrigger(backstepAnimTriggerName);

        // 3) 이번에 쓸 트리거만 Set
        bossAnimator.SetTrigger(triggerName);
    }

    // ==================== 회전 보정 유틸 ====================

    /// <summary>
    /// 플레이어 방향으로 즉시 스냅 회전 (Y축만).
    /// </summary>
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

        if (playerTarget == null)
        {
            UpdateMoveAnimation(0f);
            yield break;
        }

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        UpdateMoveAnimation(0f);

        if (distance > stoppingDistance + 1.0f)
            SetState(BossState.Move);
        else
            SetState(BossState.Attack);
    }

    IEnumerator Co_HandleMove()
    {
        LogState("[BossFSM] Move: 플레이어에게 접근 시작");

        while (currentState == BossState.Move && !_isDead)
        {
            if (playerTarget == null)
            {
                UpdateMoveAnimation(0f);
                yield break;
            }

            Vector3 toPlayer = playerTarget.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (distance <= stoppingDistance)
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            Vector3 dir = GetChaseDirection(toPlayer);
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, look, Time.deltaTime * 5f);
            }

            Vector3 delta = dir * moveSpeed * Time.deltaTime;
            ApplyMovementDelta(delta);

            UpdateMoveAnimation(1f);
            yield return null;
        }
    }

    IEnumerator Co_CombatIdle()
    {
        float t = _pendingCombatIdleDuration > 0.01f ? _pendingCombatIdleDuration : combatIdleTime;
        _pendingCombatIdleDuration = -1f;
        while (t > 0f && !_isDead)
        {
            t -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        ClearPunishWindow();

        if (!_isDead)
            SetState(BossState.Detect);
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
        float distance = 0f;

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
        if (snapRotationToPlayerOnAttack)
        {
            if (preAttackRotateTime > 0f)
                yield return StartCoroutine(Co_FacePlayerShort(preAttackRotateTime, preAttackRotateSpeed));
            else
                FaceToPlayerInstant();
        }

        // 1) 거리 다시 측정 후 백스텝 여부 결정
        distance = Vector3.Distance(transform.position, playerTarget.position);

        if (!isQueuedFollowUp &&
            useBackstepWhenTooClose &&
            distance < backstepTriggerDistance &&
            Time.time >= _backstepCooldownTimer)
        {
            yield return StartCoroutine(Co_Backstep());

            _backstepCooldownTimer = Time.time + backstepCooldown;

            if (!_isDead)
                SetState(BossState.CombatIdle);

            yield break;
        }

        // 2) 일반 공격 패턴
        if (allPatterns == null || allPatterns.Count == 0)
        {
            SetState(BossState.CombatIdle);
            yield break;
        }

        AttackPattern pattern = _queuedFollowUpPattern != null ? _queuedFollowUpPattern : SelectPattern(distance);
        _queuedFollowUpPattern = null;
        _queuedFollowUpDelay = 0f;
        if (pattern == null)
        {
            LogStateWarning("[BossFSM] 사용할 수 있는 패턴이 없음 → CombatIdle");
            SetState(distance > stoppingDistance + 0.35f ? BossState.Move : BossState.CombatIdle);
            yield break;
        }

        if (!isQueuedFollowUp)
            _followUpChainDepth = 0;

        _currentPattern         = pattern;
        _lastExecutedPattern    = pattern.patternName;
        pattern.StartCooldown(Time.time);
        AttackTelegraphType telegraphType = pattern.ResolveTelegraphType();
        float telegraphLeadTime = pattern.ResolveTelegraphLeadTime() * ResolvePhaseTelegraphScale();
        string telegraphLabel = pattern.ResolveTelegraphLabel();
        AttackTimingStyle timingStyle = pattern.ResolveTimingStyle();
        float extraHoldDelay = pattern.ResolveExtraHoldDelay();
        string secondaryTelegraphLabel = pattern.ResolveSecondaryTelegraphLabel();
        bool canParry = pattern.ResolveCanParry();
        bool canPerfectDodge = pattern.ResolveCanPerfectDodge();
        bool isUnblockable = pattern.ResolveIsUnblockable();
        float recoveryTime = pattern.ResolveRecoveryTime();
        float punishWindowDuration = pattern.ResolvePunishWindowDuration();
        float punishDamageMultiplier = pattern.ResolvePunishDamageMultiplier();

        if (isQueuedFollowUp)
        {
            telegraphLeadTime = Mathf.Max(0.08f, telegraphLeadTime * FollowUpTelegraphLeadScale);
            telegraphLabel = $"{FollowUpTelegraphPrefix} {telegraphLabel}";
            secondaryTelegraphLabel = $"{FollowUpTelegraphPrefix} {secondaryTelegraphLabel}";
        }

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

        ApplyPatternHitboxTuning(pattern);

        if (attackHitbox != null)
            attackHitbox.Configure(pattern.damageAmount, canParry, canPerfectDodge, isUnblockable, transform);

        if (!string.IsNullOrEmpty(pattern.animTriggerName))
            PlayAnimTrigger(pattern.animTriggerName);

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
        _queuedFollowUpPattern = null;
        _queuedFollowUpDelay = 0f;
        _followUpChainDepth = 0;
    }

    void RefreshPatternCooldownState()
    {
        if (allPatterns == null)
            return;

        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            pattern.RestoreCooldownAnchor(Time.time);
            pattern.SyncCooldown(Time.time);
            if (pattern.currentCooldown > 0f)
                break;
        }
    }

    void SyncPatternCooldowns()
    {
        if (allPatterns == null)
            return;

        float now = Time.time;
        for (int i = 0; i < allPatterns.Count; i++)
        {
            AttackPattern pattern = allPatterns[i];
            if (pattern == null)
                continue;

            pattern.SyncCooldown(now);
        }
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
        ClearPunishWindow();
        ClearQueuedFollowUp();

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        _currentPattern = null;
        _pendingCombatIdleDuration = -1f;
        UpdateMoveAnimation(0f);

        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();

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
            bossAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            bossAnimator.applyRootMotion = false;
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
            _cachedUltimateVictimAnimatorState = false;
        }

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

        SetState(BossState.Detect, true);
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
    }

    void RestoreUltimateVictimWorldPose()
    {
        if (!_hasUltimateVictimOriginalPose)
            return;

        transform.SetPositionAndRotation(_ultimateVictimOriginalPosition, _ultimateVictimOriginalRotation);

        if (rb != null)
        {
            rb.position = _ultimateVictimOriginalPosition;
            rb.rotation = _ultimateVictimOriginalRotation;
        }

        _hasUltimateVictimOriginalPose = false;
    }

    void ApplyUltimateVictimAnchor()
    {
        if (!_isUltimateVictim || !_hasUltimateVictimAnchor)
            return;

        transform.SetPositionAndRotation(_ultimateVictimAnchorPosition, _ultimateVictimAnchorRotation);

        if (rb != null)
        {
            rb.position = _ultimateVictimAnchorPosition;
            rb.rotation = _ultimateVictimAnchorRotation;
        }
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

        float chance = Mathf.Clamp01(sourcePattern.ResolveFollowUpChance() + ResolvePhaseFollowUpChanceBonus());
        if (chance <= 0.001f || UnityEngine.Random.value > chance)
            return false;

        float maxDistance = sourcePattern.ResolveFollowUpMaxDistance();
        if (distanceToPlayer > maxDistance)
            return false;

        AttackPattern followUp = SelectFollowUpPattern(sourcePattern, distanceToPlayer);
        if (followUp == null)
            return false;

        _queuedFollowUpPattern = followUp;
        _queuedFollowUpDelay = sourcePattern.ResolveFollowUpDelay();
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

        if (!string.IsNullOrEmpty(forcedPattern))
        {
            foreach (AttackPattern pattern in allPatterns)
            {
                if (pattern == null)
                    continue;

                if (!string.Equals(pattern.patternName, forcedPattern, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (pattern.ResolveTelegraphType() == AttackTelegraphType.Danger)
                    return null;

                if (pattern.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                    return pattern;

                return null;
            }
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

        float totalWeight = 0f;
        for (int i = 0; i < candidates.Count; i++)
            totalWeight += Mathf.Max(0.01f, candidates[i].weight * ResolveFollowUpWeightMultiplier(sourcePattern, candidates[i]) * ResolvePhasePatternWeightMultiplier(candidates[i], true));

        if (totalWeight <= 0.01f)
            return candidates[0];

        float roll = UnityEngine.Random.value * totalWeight;
        float accumulated = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            AttackPattern candidate = candidates[i];
            accumulated += Mathf.Max(0.01f, candidate.weight * ResolveFollowUpWeightMultiplier(sourcePattern, candidate) * ResolvePhasePatternWeightMultiplier(candidate, true));
            if (roll <= accumulated)
                return candidate;
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

        if (!string.IsNullOrEmpty(backstepAnimTriggerName))
            PlayAnimTrigger(backstepAnimTriggerName);

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

    // ==================== 패턴 선택 ====================

    AttackPattern SelectPattern(float distanceToPlayer)
    {
        SyncPatternCooldowns();

        if (allPatterns == null || allPatterns.Count == 0)
            return null;

        List<AttackPattern> candidates = _patternCandidatesCache;
        candidates.Clear();

        foreach (var p in allPatterns)
        {
            if (p == null) continue;
            if (p.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                candidates.Add(p);
        }

        AttackPattern selected = SelectWeightedPattern(candidates);
        if (selected != null)
            return selected;

        return SelectFallbackPattern(distanceToPlayer);
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
            float weightBias = Mathf.Max(0.01f, pattern.weight * ResolvePhasePatternWeightMultiplier(pattern, false));
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

        float totalWeight = 0f;
        foreach (var p in candidates)
            totalWeight += Mathf.Max(0.01f, p.weight * ResolvePhasePatternWeightMultiplier(p, false));

        if (totalWeight <= 0f)
            return candidates[0];

        float r = UnityEngine.Random.value * totalWeight;
        float accum = 0f;

        foreach (var p in candidates)
        {
            float w = Mathf.Max(0.01f, p.weight * ResolvePhasePatternWeightMultiplier(p, false));
            accum += w;
            if (r <= accum)
                return p;
        }

        return candidates[candidates.Count - 1];
    }

    // ==================== 이동 애니 (BlendTree) ====================

    void UpdateMoveAnimation(float target01)
    {
        if (bossAnimator == null) return;

        target01 = Mathf.Clamp01(target01);

        _moveBlend = Mathf.Lerp(
            _moveBlend,
            target01,
            Time.deltaTime / Mathf.Max(0.0001f, moveAnimDamp)
        );

        if (float.IsNaN(_lastAppliedMoveBlend) || Mathf.Abs(_lastAppliedMoveBlend - _moveBlend) > 0.0025f)
        {
            _lastAppliedMoveBlend = _moveBlend;
            bossAnimator.SetFloat(AnimParam_MoveSpeed, _moveBlend);
        }
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
