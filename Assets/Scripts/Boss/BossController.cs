// 파일명: BossController.cs
// 보스 FSM + 이동 + 패턴 + 근접 백스텝 + 사망 시 루트 파괴
// 트리거는 PlayAnimTrigger() 하나로 관리해서 한 번에 하나만 켜지도록 설계.
// 공격 진입 전 플레이어 쪽으로 회전 보정(스냅/짧은 회전) 지원.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public enum BossAttackTelegraphStyle
{
    Auto,
    Parry,
    Guard,
    Dodge,
    Danger
}

public enum BossState
{
    IntroIdle,   // 전투 시작 전 연출용 대기
    Detect,      // 플레이어 거리/상태 체크
    Move,        // 플레이어에게 이동
    Attack,      // 패턴 수행 중
    CombatIdle,  // 공격 후 잠깐 휴지
    ParryStagger,// 패링당해 잠깐 경직
    Break,       // 브레이크(그로기)
    Dead         // 사망
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
    public bool canGuard = true;
    public bool isUnblockable = false;
    public bool causesGuardBreak = false;
    public BossAttackTelegraphStyle telegraphStyle = BossAttackTelegraphStyle.Auto;

    [Tooltip("이 패턴의 기본 데미지량")]
    public int damageAmount = 10;

    [Header("쿨타임 / 가중치 / 거리 조건 (한글 설명)")]
    [Tooltip("패턴 사용 후 다시 사용할 때까지의 쿨타임(초)")]
    public float cooldown = 2f;

    [HideInInspector] public float currentCooldown;

    [Tooltip("패턴 선택 시 랜덤 가중치 (값이 클수록 선택될 확률↑)")]
    public float weight = 1f;

    [Tooltip("플레이어와의 최소 거리 조건 (이보다 가까우면 사용 안 함)")]
    public float minRange = 0f;

    [Tooltip("플레이어와의 최대 거리 조건 (이보다 멀면 사용 안 함)")]
    public float maxRange = 5f;

    [Tooltip("이 패턴 직전에 금지할 패턴 이름 (없으면 빈 문자열)")]
    public string forbiddenAfter;

    public bool CanParry => isParryable && !isUnblockable;

    public bool CanExecute(BossController boss, float distance, string lastPattern)
    {
        if (currentCooldown > 0f) return false;
        if (distance < minRange || distance > maxRange) return false;

        if (!string.IsNullOrEmpty(forbiddenAfter) &&
            lastPattern.Equals(forbiddenAfter, StringComparison.Ordinal))
            return false;

        if (boss.playerTracker == null) return true;

        // 필요하면 이름 기준으로 세부 조건 추가 가능
        return true;
    }
}

public class BossController : MonoBehaviour, IParryReact
{
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

    [Header("패링 경직 설정 (한글 설명)")]
    [Tooltip("플레이어가 보스 공격을 패링하면 현재 패턴을 끊고 추가 경직을 줄지 여부")]
    public bool interruptPatternOnParry = true;

    [Tooltip("패링 성공 시 다음 패턴 전까지 보스가 쉬는 추가 시간(초)")]
    public float parryStaggerDuration = 0.75f;

    [Tooltip("패링 처리 후 다음 공격을 다시 고르기 전 반드시 쉬는 최소 시간(초)")]
    public float postParryAttackGap = 0.6f;

    [Tooltip("공격 상태에서 애니메이션 종료를 기다리는 최대 시간 (초). 안전장치 역할")]
    public float maxAttackStateWaitTime = 2.0f;

    private Coroutine _stateRoutine;
    private bool _isDead;

    [Header("이동 설정 (한글 설명)")]
    [Tooltip("실제 보스 이동 속도 (m/s)")]
    public float moveSpeed = 4.0f;

    [Tooltip("이 거리 이내로 들어오면 이동을 멈추고 공격 준비")]
    public float stoppingDistance = 1.0f;

    [Tooltip("Blend Tree로 전달할 MoveSpeed 보간 시간 (0에 가까울수록 즉각 반응)")]
    [Range(0.01f, 0.5f)]
    public float moveAnimDamp = 0.1f;

    private static readonly int AnimParam_MoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int AnimParam_IsBreak   = Animator.StringToHash("IsBreak");
    private static readonly int AnimParam_IsDead    = Animator.StringToHash("IsDead");
    private static readonly int AnimTrigger_Parried = Animator.StringToHash("Parried");
    private static readonly int AnimState_AttackA = Animator.StringToHash("Attack_A");
    private static readonly int AnimState_AttackB = Animator.StringToHash("Attack_B");
    private static readonly int AnimState_AttackC = Animator.StringToHash("Attack_C");
    private static readonly int AnimState_AttackD = Animator.StringToHash("Attack_D");
    private static readonly int AnimState_AttackE = Animator.StringToHash("Attack_E");
    private static readonly int AnimState_AttackF = Animator.StringToHash("Attack_F");

    private float _moveBlend; // 0~1
    private float _lastAppliedMoveBlend = float.NaN;
    private float _stoppingDistanceSq;
    private float _detectMoveThresholdSq;
    private float _backstepTriggerDistanceSq;

    [Header("공격 패턴 목록 (한글 설명)")]
    [Tooltip("보스가 사용할 수 있는 모든 공격 패턴 리스트")]
    public List<AttackPattern> allPatterns;

    private string _lastExecutedPattern = string.Empty;
    private AttackPattern _currentPattern;
    private readonly List<AttackPattern> _patternCandidates = new List<AttackPattern>(8);

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

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool parryDebugLogs = true;

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
    private WaitForSeconds _cachedDestroyDelayWait;
    private float _cachedDestroyDelayWaitSeconds = -1f;
    private AttackHitbox[] _cachedAttackHitboxes;
    private float _combatIdleDurationOverride = -1f;
    private float _parryStaggerDurationOverride = -1f;
    private float _attackLockedUntil = -1f;
    private float _pendingPostParryGap = -1f;

    void Awake()
    {
        CacheDerivedValues();
        CacheAttackHitboxes();
        DeactivateAllAttackHitboxes();
        PlayerGuardController.GlobalParrySuccess += HandleGlobalParrySuccess;

        if (bossHealth != null)
            bossHealth.OnDied += OnBossDied;

        if (breakController != null)
        {
            breakController.OnBreakEnter.AddListener(OnBreakEnter);
            breakController.OnBreakExit.AddListener(OnBreakExit);
        }
    }

    void OnValidate()
    {
        CacheDerivedValues();
        CacheAttackHitboxes();
    }

    void Start()
    {
        SetState(BossState.IntroIdle);
    }

    void OnDestroy()
    {
        PlayerGuardController.GlobalParrySuccess -= HandleGlobalParrySuccess;
    }

    void Update()
    {
        bool hasPatterns = allPatterns != null && allPatterns.Count > 0;
        if (!hasPatterns && _backstepCooldownTimer <= 0f)
            return;

        // 패턴 쿨타임 감소
        if (hasPatterns)
        {
            float dt = Time.deltaTime;
            foreach (var p in allPatterns)
            {
                if (p.currentCooldown > 0f)
                    p.currentCooldown -= dt;
            }
        }

        // 백스텝 쿨타임 감소
        if (_backstepCooldownTimer > 0f)
            _backstepCooldownTimer -= Time.deltaTime;
    }

    // ==================== FSM 전이 ====================

    public void SetState(BossState newState)
    {
        if (_isDead) return;
        if (currentState == newState) return;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        var old = currentState;
        currentState = newState;
        if (debugLogs)
            Debug.Log($"[BossFSM] {old} -> {newState}");

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
            case BossState.ParryStagger:
                _stateRoutine = StartCoroutine(Co_HandleParryStagger());
                break;
            case BossState.Break:
                _stateRoutine = StartCoroutine(Co_HandleBreak());
                break;
            case BossState.Dead:
                // Dead 상태에서는 별도 코루틴 없음
                break;
        }
    }

    // ==================== 생존/브레이크 ====================

    void OnBossDied()
    {
        if (_isDead) return;
        _isDead = true;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        currentState = BossState.Dead;
        UpdateMoveAnimation(0f);

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsDead, true);

        DeactivateAllAttackHitboxes();

        if (debugLogs)
            Debug.Log("[BossFSM] Boss Dead");

        if (autoDestroyOnDead && _destroyRoutine == null)
            _destroyRoutine = StartCoroutine(Co_DestroyHierarchyAfterDead());
    }

    void OnBreakEnter()
    {
        if (_isDead) return;

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsBreak, true);

        SetState(BossState.Break);
    }

    void OnBreakExit()
    {
        if (_isDead) return;

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsBreak, false);

        SetState(BossState.Detect);
    }

    // ==================== 사망 후 파괴 처리 ====================

    IEnumerator Co_DestroyHierarchyAfterDead()
    {
        if (destroyDelayAfterDead > 0f)
            yield return GetDestroyDelayWait();

        FinalizeDeathAndDestroy();
    }

    private WaitForSeconds GetDestroyDelayWait()
    {
        float seconds = Mathf.Max(0f, destroyDelayAfterDead);
        if (seconds <= 0f) return null;

        if (_cachedDestroyDelayWait == null || !Mathf.Approximately(_cachedDestroyDelayWaitSeconds, seconds))
        {
            _cachedDestroyDelayWaitSeconds = seconds;
            _cachedDestroyDelayWait = new WaitForSeconds(seconds);
        }

        return _cachedDestroyDelayWait;
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

        _currentPattern = null;
        ApplyPendingPostParryGap();
        SetState(BossState.CombatIdle);
    }

    public void OnParried(GameObject parrier, float riposteDamage, float stunDuration)
    {
        if (parryDebugLogs)
            Debug.Log($"[ParryDebug][Boss] OnParried enter frame={Time.frameCount} time={Time.time:0.000} state={currentState} pattern={_currentPattern?.patternName ?? "null"} lockUntil={_attackLockedUntil:0.000} parrier={(parrier ? parrier.name : "null")}", this);

        if (_isDead || currentState == BossState.Dead || currentState == BossState.Break)
        {
            if (parryDebugLogs)
                Debug.Log($"[ParryDebug][Boss] OnParried ignored by dead/break state={currentState}", this);
            return;
        }

        if (!interruptPatternOnParry)
        {
            if (parryDebugLogs)
                Debug.Log("[ParryDebug][Boss] OnParried ignored because interruptPatternOnParry is false", this);
            return;
        }

        float staggerDuration = stunDuration > 0f ? stunDuration : parryStaggerDuration;
        _parryStaggerDurationOverride = Mathf.Max(0.05f, staggerDuration);
        _pendingPostParryGap = Mathf.Max(_pendingPostParryGap, Mathf.Max(0f, postParryAttackGap));
        float lockDuration = Mathf.Max(_parryStaggerDurationOverride, postParryAttackGap);
        _attackLockedUntil = Mathf.Max(_attackLockedUntil, Time.time + lockDuration);

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        _currentPattern = null;
        DeactivateAllAttackHitboxes();
        UpdateMoveAnimation(0f);

        ResetAnimationTriggers();
        if (bossAnimator != null)
            bossAnimator.SetTrigger(AnimTrigger_Parried);
        SetState(BossState.ParryStagger);

        if (debugLogs)
            Debug.Log($"[BossFSM] Parried by {parrier?.name ?? "Unknown"} -> ParryStagger({_parryStaggerDurationOverride:0.00}s)");

        if (parryDebugLogs)
            Debug.Log($"[ParryDebug][Boss] OnParried applied stagger={_parryStaggerDurationOverride:0.000} gap={_pendingPostParryGap:0.000} lockUntil={_attackLockedUntil:0.000}", this);
    }

    public void ActivateHitbox()
    {
        if (_currentPattern == null)
            return;

        if (_cachedAttackHitboxes == null || _cachedAttackHitboxes.Length == 0)
            CacheAttackHitboxes();

        if (_cachedAttackHitboxes == null)
            return;

        for (int i = 0; i < _cachedAttackHitboxes.Length; i++)
        {
            if (_cachedAttackHitboxes[i] != null)
                _cachedAttackHitboxes[i].ActivateWindow();
        }
    }

    public void DeactivateHitbox()
    {
        DeactivateAllAttackHitboxes();
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

        ResetAnimationTriggers();
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

        if (TryEnterAttackLockIdle())
            yield break;

        float distanceSq = (playerTarget.position - transform.position).sqrMagnitude;
        UpdateMoveAnimation(0f);

        if (distanceSq > _detectMoveThresholdSq)
            SetState(BossState.Move);
        else
            SetState(BossState.Attack);
    }

    IEnumerator Co_HandleMove()
    {
        if (debugLogs)
            Debug.Log("[BossFSM] Move: approaching player");

        while (currentState == BossState.Move && !_isDead)
        {
            if (playerTarget == null)
            {
                UpdateMoveAnimation(0f);
                yield break;
            }

            if (TryEnterAttackLockIdle())
                yield break;

            Vector3 toPlayer = playerTarget.position - transform.position;
            toPlayer.y = 0f;
            float distanceSq = toPlayer.sqrMagnitude;

            if (distanceSq <= _stoppingDistanceSq)
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            Vector3 dir = toPlayer.normalized;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, look, Time.deltaTime * 5f);
            }

            Vector3 delta = dir * moveSpeed * Time.deltaTime;
            if (rb != null)
                rb.MovePosition(rb.position + delta);
            else
                transform.position += delta;

            UpdateMoveAnimation(1f);
            yield return null;
        }
    }

    IEnumerator Co_CombatIdle()
    {
        float t = ConsumeCombatIdleDuration();
        while (t > 0f && !_isDead)
        {
            t -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        if (!_isDead)
            SetState(BossState.Detect);
    }

    IEnumerator Co_HandleParryStagger()
    {
        float t = ConsumeParryStaggerDuration();
        while (t > 0f && !_isDead)
        {
            t -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        if (_isDead)
            yield break;

        float gap = ConsumePendingPostParryGap();
        if (gap > 0f)
            _attackLockedUntil = Mathf.Max(_attackLockedUntil, Time.time + gap);

        _combatIdleDurationOverride = Mathf.Max(_combatIdleDurationOverride, gap);
        SetState(BossState.CombatIdle);
    }

    IEnumerator Co_HandleBreak()
    {
        if (debugLogs)
            Debug.Log("[BossFSM] Break: entered break state");

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
        if (TryEnterAttackLockIdle())
            yield break;

        if (playerTarget == null)
        {
            SetState(BossState.CombatIdle);
            yield break;
        }

        UpdateMoveAnimation(0f);

        // 0) 공격 시작 전에 플레이어 쪽으로 회전 보정
        if (snapRotationToPlayerOnAttack)
        {
            if (preAttackRotateTime > 0f)
                yield return StartCoroutine(Co_FacePlayerShort(preAttackRotateTime, preAttackRotateSpeed));
            else
                FaceToPlayerInstant();
        }

        // 1) 거리 다시 측정 후 백스텝 여부 결정
        float distanceSq = (playerTarget.position - transform.position).sqrMagnitude;

        if (useBackstepWhenTooClose &&
            distanceSq < _backstepTriggerDistanceSq &&
            _backstepCooldownTimer <= 0f)
        {
            yield return StartCoroutine(Co_Backstep());

            _backstepCooldownTimer = backstepCooldown;

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

        AttackPattern pattern = SelectPattern(Mathf.Sqrt(distanceSq));
        if (pattern == null)
        {
            if (debugLogs)
                Debug.LogWarning("[BossFSM] No usable attack pattern -> CombatIdle");

            SetState(BossState.CombatIdle);
            yield break;
        }

        _currentPattern         = pattern;
        _lastExecutedPattern    = pattern.patternName;
        pattern.currentCooldown = pattern.cooldown;

        if (patternVisuals != null)
            patternVisuals.SetParryable(pattern.CanParry);

        ConfigureAttackHitboxesForPattern(pattern);

        if (!string.IsNullOrEmpty(pattern.animTriggerName))
            PlayAnimTrigger(pattern.animTriggerName);

        if (debugLogs)
            Debug.Log($"[BossFSM] Attack pattern: {pattern.patternName} ({pattern.animTriggerName})");

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
            _currentPattern = null;
            ApplyPendingPostParryGap();
            SetState(BossState.CombatIdle);
        }
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

        while (elapsed < backstepDuration && !_isDead)
        {
            if (backstepSpeed > 0f)
            {
                Vector3 delta = backDir * backstepSpeed * Time.deltaTime;
                if (rb != null)
                    rb.MovePosition(rb.position + delta);
                else
                    transform.position += delta;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ==================== 패턴 선택 ====================

    AttackPattern SelectPattern(float distanceToPlayer)
    {
        if (allPatterns == null || allPatterns.Count == 0)
            return null;

        _patternCandidates.Clear();

        foreach (var p in allPatterns)
        {
            if (p == null) continue;
            if (p.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                _patternCandidates.Add(p);
        }

        if (_patternCandidates.Count == 0)
            return null;

        float totalWeight = 0f;
        foreach (var p in _patternCandidates)
            totalWeight += Mathf.Max(0f, p.weight);

        if (totalWeight <= 0f)
            return _patternCandidates[0];

        float r = UnityEngine.Random.value * totalWeight;
        float accum = 0f;

        foreach (var p in _patternCandidates)
        {
            float w = Mathf.Max(0f, p.weight);
            accum += w;
            if (r <= accum)
                return p;
        }

        return _patternCandidates[_patternCandidates.Count - 1];
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

        if (Mathf.Approximately(_lastAppliedMoveBlend, _moveBlend))
            return;

        _lastAppliedMoveBlend = _moveBlend;
        bossAnimator.SetFloat(AnimParam_MoveSpeed, _moveBlend);
    }

    void CacheDerivedValues()
    {
        float stopping = Mathf.Max(0f, stoppingDistance);
        _stoppingDistanceSq = stopping * stopping;

        float detectMoveThreshold = stopping + 1.0f;
        _detectMoveThresholdSq = detectMoveThreshold * detectMoveThreshold;

        float backstep = Mathf.Max(0f, backstepTriggerDistance);
        _backstepTriggerDistanceSq = backstep * backstep;
    }

    void CacheAttackHitboxes()
    {
        _cachedAttackHitboxes = GetComponentsInChildren<AttackHitbox>(true);
        if ((_cachedAttackHitboxes == null || _cachedAttackHitboxes.Length == 0) && attackHitbox != null)
            _cachedAttackHitboxes = new[] { attackHitbox };
    }

    void DeactivateAllAttackHitboxes()
    {
        if (_cachedAttackHitboxes == null || _cachedAttackHitboxes.Length == 0)
            CacheAttackHitboxes();

        if (_cachedAttackHitboxes == null)
            return;

        for (int i = 0; i < _cachedAttackHitboxes.Length; i++)
        {
            if (_cachedAttackHitboxes[i] != null)
                _cachedAttackHitboxes[i].DeactivateWindow();
        }
    }

    void ConfigureAttackHitboxesForPattern(AttackPattern pattern)
    {
        if (pattern == null)
            return;

        if (_cachedAttackHitboxes == null || _cachedAttackHitboxes.Length == 0)
            CacheAttackHitboxes();

        if (_cachedAttackHitboxes == null)
            return;

        for (int i = 0; i < _cachedAttackHitboxes.Length; i++)
        {
            var hitbox = _cachedAttackHitboxes[i];
            if (hitbox == null)
                continue;

            hitbox.Configure(
                pattern.damageAmount,
                pattern.CanParry,
                pattern.canGuard,
                pattern.isUnblockable,
                pattern.causesGuardBreak,
                transform);
            hitbox.ResetParrySequence(pattern.CanParry ? 1 : 0);
        }
    }

    // ==================== (선택) 루트 콜라이더 충돌 ====================

    void OnTriggerEnter(Collider other)
    {
        if (_currentPattern == null) return;
        // 필요하면 여기서 특수 처리 추가.
    }

    float ConsumeCombatIdleDuration()
    {
        float duration = _combatIdleDurationOverride >= 0f ? _combatIdleDurationOverride : combatIdleTime;
        _combatIdleDurationOverride = -1f;
        return Mathf.Max(0f, duration);
    }

    float ConsumeParryStaggerDuration()
    {
        float duration = _parryStaggerDurationOverride >= 0f ? _parryStaggerDurationOverride : parryStaggerDuration;
        _parryStaggerDurationOverride = -1f;
        return Mathf.Max(0.05f, duration);
    }

    float ConsumePendingPostParryGap()
    {
        float gap = _pendingPostParryGap >= 0f ? _pendingPostParryGap : 0f;
        _pendingPostParryGap = -1f;
        return Mathf.Max(0f, gap);
    }

    void ApplyPendingPostParryGap()
    {
        float gap = ConsumePendingPostParryGap();
        if (gap <= 0f)
            return;

        _attackLockedUntil = Mathf.Max(_attackLockedUntil, Time.time + gap);
        _combatIdleDurationOverride = Mathf.Max(_combatIdleDurationOverride, gap);
    }

    bool TryEnterAttackLockIdle()
    {
        float remaining = _attackLockedUntil - Time.time;
        if (remaining <= 0f)
            return false;

        if (parryDebugLogs)
            Debug.Log($"[ParryDebug][Boss] Attack lock active -> CombatIdle remaining={remaining:0.000} frame={Time.frameCount} state={currentState}", this);

        _combatIdleDurationOverride = Mathf.Max(_combatIdleDurationOverride, remaining);
        SetState(BossState.CombatIdle);
        return true;
    }

    void ResetAnimationTriggers()
    {
        if (bossAnimator == null)
            return;

        if (allPatterns != null)
        {
            foreach (var p in allPatterns)
            {
                if (p != null && !string.IsNullOrEmpty(p.animTriggerName))
                    bossAnimator.ResetTrigger(p.animTriggerName);
            }
        }

        if (!string.IsNullOrEmpty(backstepAnimTriggerName))
            bossAnimator.ResetTrigger(backstepAnimTriggerName);

        bossAnimator.ResetTrigger(AnimTrigger_Parried);
    }

    void HandleGlobalParrySuccess(PlayerGuardController parryOwner)
    {
        if (parryDebugLogs)
            Debug.Log($"[ParryDebug][Boss] HandleGlobalParrySuccess frame={Time.frameCount} time={Time.time:0.000} state={currentState} pattern={_currentPattern?.patternName ?? "null"} guard={(parryOwner ? parryOwner.name : "null")}", this);

        if (parryOwner == null || _isDead || currentState == BossState.Dead || currentState == BossState.Break)
            return;

        if (playerTarget != null)
        {
            Transform parryTransform = parryOwner.transform;
            if (parryTransform != playerTarget && parryTransform.root != playerTarget.root)
                return;
        }

        if (!_currentPatternIsLikelyActive() && Time.time >= _attackLockedUntil)
        {
            if (parryDebugLogs)
                Debug.Log("[ParryDebug][Boss] Global parry ignored because no active/likely attack state", this);
            return;
        }

        OnParried(parryOwner.gameObject, 0f, 0f);
    }

    bool _currentPatternIsLikelyActive()
    {
        if (_currentPattern != null || currentState == BossState.Attack)
            return true;

        if (bossAnimator == null)
            return false;

        AnimatorStateInfo state = bossAnimator.GetCurrentAnimatorStateInfo(0);
        int shortHash = state.shortNameHash;
        return shortHash == AnimState_AttackA
            || shortHash == AnimState_AttackB
            || shortHash == AnimState_AttackC
            || shortHash == AnimState_AttackD
            || shortHash == AnimState_AttackE
            || shortHash == AnimState_AttackF;
    }

}
