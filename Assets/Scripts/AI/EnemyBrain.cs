using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyBrainState
{
    Idle = 0,
    Patrol = 1,
    Alert = 2,
    Approach = 3,
    Strafe = 4,
    Attack = 5,
    Recover = 6,
    Stagger = 7,
    Dead = 8
}

// 잡몹 공용 두뇌 (상태 기계 + NavMeshAgent + 인지 + 공격 토큰 + 포이즈/경직 + 패링 반응 + 사망)
//  Idle/Patrol ─(발견: 시야/청각/피격)─▶ Alert(짧은 '발견' 비프) ─▶ Approach ─▶ (토큰 있음) Attack ─▶ Recover ─▶ Strafe ...
//                                                          └─(토큰 없음) Strafe: 중거리 원형 대기, 주기적으로 토큰 요청
//  포이즈 0 → Stagger(리포스트 창, 받는 피해 증가). 금/호박 큐 공격을 패링당하면 Stagger, 일반 공격 패링은 짧은 튕김.
//  모든 시간은 Time.time/deltaTime (GameTimeScale 반영). Update 에서 할당/오브젝트 탐색 없음
//  (대상이 아직 없을 때만 2초 간격으로 플레이어를 찾는다).
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public abstract class EnemyBrain : MonoBehaviour, IDamageReceiver, IParryReact, IEnemyAlertable
{
    public const string BaseLayerPrefix = "Base Layer.";

    static readonly List<EnemyBrain> s_active = new List<EnemyBrain>(16);
    static EnemyBrain s_delivering;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_active.Clear();
        s_delivering = null;
    }

    public static IReadOnlyList<EnemyBrain> Active => s_active;

    public event Action<EnemyBrain, EnemyBrainState> StateChanged;
    public event Action<EnemyBrain> Died;
    /// <summary>QA: (적, 큐 타입, 공지 시간).</summary>
    public event Action<EnemyBrain, EnemyCueType, float> CueIssued;

    [Header("Target")]
    [SerializeField] protected Transform target;

    [Header("Poise / Stagger")]
    [SerializeField, Min(1f)] protected float maxPoise = 40f;
    [SerializeField, Min(0f)] protected float poiseRegenDelay = 2.5f;
    [SerializeField, Min(0f)] protected float poiseRegenPerSecond = 16f;
    [Tooltip("포이즈 붕괴 경직 (리포스트 창)")]
    [SerializeField, Min(0.1f)] protected float staggerDuration = 1.6f;
    [Tooltip("금/호박 큐 공격을 패링당했을 때 경직 (리포스트 창)")]
    [SerializeField, Min(0.1f)] protected float parryStaggerDuration = 2.2f;
    [Tooltip("일반 공격을 패링당했을 때 튕김")]
    [SerializeField, Min(0.1f)] protected float deflectRecoilDuration = 0.6f;
    [SerializeField, Min(0f)] protected float flinchDuration = 0.32f;
    [Tooltip("리포스트 창(경직) 동안 받는 피해 배율")]
    [SerializeField, Min(1f)] protected float riposteDamageMultiplier = 1.5f;
    [Tooltip("짧은 시간에 이만큼 경직되면 잠깐 경직 면역 (무한 경직 방지)")]
    [SerializeField, Min(1)] protected int flinchLockoutCount = 3;

    [Header("Movement")]
    [SerializeField] protected NavMeshAgent agent;
    [SerializeField, Min(0.1f)] protected float walkSpeed = 1.6f;
    [SerializeField, Min(0.1f)] protected float runSpeed = 3.8f;
    [SerializeField, Min(0.1f)] protected float strafeSpeed = 1.5f;
    [SerializeField, Min(10f)] protected float turnSpeedDeg = 420f;
    [SerializeField, Min(0.05f)] protected float repathInterval = 0.2f;

    [Header("Ranges")]
    [Tooltip("토큰 없이 대기할 원 반지름 (중거리)")]
    [SerializeField, Min(1f)] protected float ringRadius = 4.6f;
    [Tooltip("이 거리 안에서 근접 토큰을 요청")]
    [SerializeField, Min(1f)] protected float tokenRequestRange = 7.5f;
    [Tooltip("이보다 멀고 대상을 기억하지 못하면 경계 해제")]
    [SerializeField, Min(5f)] protected float leashRange = 28f;
    [SerializeField, Min(0f)] protected float targetRadius = 0.4f;
    [SerializeField, Min(0f)] protected float attackCooldownMin = 1.1f;
    [SerializeField, Min(0f)] protected float attackCooldownMax = 2.0f;
    [Tooltip("다른 교전 적과 최소 방위 간격 (도)")]
    [SerializeField, Range(10f, 120f)] protected float ringSpacingDeg = 50f;

    [Header("Patrol")]
    [SerializeField] protected Transform[] patrolPoints;
    [SerializeField, Min(0f)] protected float patrolWait = 2f;

    [Header("Alert")]
    [SerializeField, Min(0f)] protected float alertDuration = 0.65f;
    [SerializeField, Min(0f)] protected float alertShareRadius = 9f;
    [SerializeField] protected AudioClip alertBark;
    [SerializeField, Range(0f, 1f)] protected float alertBarkVolume = 0.8f;
    [SerializeField, Min(0.02f)] protected float perceptionInterval = 0.15f;

    [Header("Animator")]
    [SerializeField] protected Animator animator;
    [SerializeField] protected string locomotionState = "Locomotion";
    [SerializeField] protected string alertState = "Alert";
    [SerializeField] protected string flinchState = "Flinch";
    [SerializeField] protected string staggerState = "Stagger";
    [SerializeField] protected string parryRecoilState = "ParryRecoil";
    [SerializeField] protected string dieState = "Die";
    [SerializeField, Min(0f)] protected float crossFade = 0.08f;

    [Header("Feedback")]
    [SerializeField] protected GameObject hitVfx;
    [SerializeField] protected AudioClip hitSfx;
    [SerializeField] protected AudioClip swingSfx;
    [SerializeField, Min(0f)] protected float idleGlow = 0.6f;
    [SerializeField, Min(0f)] protected float alertGlow = 1.6f;

    [Header("UI / Lock-on")]
    [SerializeField] protected EnemyHPBarPresenter hpPresenter;
    [SerializeField] protected Transform lockPivot;
    [SerializeField] protected Vector3 hpBarOffset = new Vector3(0f, 0.75f, 0f);

    [Header("Death")]
    [SerializeField, Min(0f)] protected float deathSinkDelay = 2.4f;
    [SerializeField, Min(0.05f)] protected float deathSinkDuration = 1.1f;
    [SerializeField, Min(0f)] protected float deathSinkDepth = 0.9f;
    [SerializeField] protected bool deactivateOnDeath = true;

    [Header("Debug")]
    [SerializeField] protected bool debugLogs = false;

    protected EnemyHealth health;
    protected EnemyPerception perception;
    protected EnemyAttackCue cue;
    protected IDamageReceiver targetReceiver;
    protected PlayerCombatController targetCombat;
    protected PlayerHealth targetHealth;
    protected Transform targetAim;

    EnemyBrainState _state = EnemyBrainState.Idle;
    float _stateEnteredAt;
    float _poise;
    float _lastPoiseHitAt = float.NegativeInfinity;
    bool _alerted;
    bool _dead;
    float _deathAt;
    Vector3 _deathPos;
    float _nextPerceptionAt;
    float _nextTargetLookupAt;
    Transform _cachedTargetFor;
    float _alertUntil;
    float _recoverUntil;
    float _staggerUntil;
    bool _riposteWindow;
    int _patrolIndex;
    float _patrolWaitUntil;
    bool _agentReady;
    bool _agentWarned;
    Vector3 _lastDestination = new Vector3(float.MaxValue, 0f, 0f);
    float _nextRepathAt;
    Vector3 _fallbackVelocity;
    int _strafeSign = 1;
    float _nextStrafeFlipAt;
    float _nextTokenTryAt;
    float _nextAttackAllowedAt;
    float _approachTokenSince;
    int _flinchCount;
    float _flinchWindowStart;
    float _flinchImmuneUntil;

    // strike sequence
    EnemyStrike[] _sequence;
    int _sequenceIndex;
    EnemyStrike _strike;
    EnemyTokenKind _sequenceToken;
    float _hitAt;
    float _cueAt;
    float _strikeEndAt;
    bool _cueIssued;
    bool _hitDone;
    bool _abortSequence;
    float _cueTime;
    float _cueLead;
    int _attackSequenceId;
    bool _pendingParry;
    float _lungeRemaining;
    int _pendingAnimHash;
    float _pendingAnimSpeed = 1f;
    float _pendingAnimAt;

    Collider[] _colliders;
    GlowSlot[] _glow;
    MaterialPropertyBlock _glowBlock;
    int _locomotionHash, _alertHash, _flinchHash, _staggerHash, _parryRecoilHash, _dieHash;
    int _moveXHash, _moveZHash;
    bool _hasMoveParams;

    struct GlowSlot
    {
        public Renderer renderer;
        public int index;
    }

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    // ---------------------------------------------------------------- QA / public state
    public EnemyBrainState State => _state;
    public bool IsDead => _dead;
    public bool IsAlerted => _alerted;
    public float Poise => _poise;
    public float MaxPoise => maxPoise;
    public Transform Target => target;
    public Transform LockPivot => lockPivot;
    public bool InRiposteWindow => _state == EnemyBrainState.Stagger && _riposteWindow;
    public EnemyStrike CurrentStrike => _state == EnemyBrainState.Attack ? _strike : null;
    /// <summary>QA: 마지막 큐의 (실제 큐→판정 시간 − 공지 시간). 정직하면 |값| ≤ 1프레임.</summary>
    public float LastCueHonestyError { get; protected set; }
    public int CueCount { get; protected set; }

    protected void RaiseCueIssued(EnemyCueType type, float lead) => CueIssued?.Invoke(this, type, lead);
    protected float Now => Time.time;
    protected bool AgentReady => _agentReady;
    protected float TimeInState => Time.time - _stateEnteredAt;
    protected bool HasTarget => target != null && (targetHealth == null || !targetHealth.IsDead);

    // ---------------------------------------------------------------- lifecycle
    protected virtual void Awake()
    {
        health = GetComponent<EnemyHealth>();
        perception = GetComponent<EnemyPerception>();
        cue = GetComponent<EnemyAttackCue>();
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (hpPresenter == null)
            hpPresenter = GetComponent<EnemyHPBarPresenter>();
        if (lockPivot == null)
            lockPivot = transform.Find("LockPivot");

        _colliders = GetComponentsInChildren<Collider>(true);
        _glowBlock = new MaterialPropertyBlock();
        CacheGlowSlots();
        CacheAnimatorHashes();
        if (agent != null)
        {
            agent.updateRotation = false;
            agent.avoidancePriority = UnityEngine.Random.Range(35, 65);
        }

        health.OnDied += HandleHealthDied;
    }

    protected virtual void OnDestroy()
    {
        if (health != null)
            health.OnDied -= HandleHealthDied;
    }

    protected virtual void OnEnable()
    {
        if (!s_active.Contains(this))
            s_active.Add(this);
        EnemyAlertNetwork.Register(this);

        _dead = false;
        _alerted = false;
        _poise = maxPoise;
        _riposteWindow = false;
        _sequence = null;
        _strike = null;
        health.currentHP = health.maxHP;
        health.isInvincible = false;
        SetCollidersEnabled(true);
        if (agent != null)
            agent.enabled = true;
        _agentReady = false;
        SetGlow(idleGlow);
        SetState(patrolPoints != null && patrolPoints.Length > 0 ? EnemyBrainState.Patrol : EnemyBrainState.Idle);
    }

    protected virtual void OnDisable()
    {
        s_active.Remove(this);
        EnemyAlertNetwork.Unregister(this);
        ReleaseTokens();
        if (hpPresenter != null && _alerted)
            hpPresenter.Detach();
        _alerted = false;
        if (target != null)
            EnemyAttackTokenDirector.Disengage(target, transform);
        if (cue != null)
            cue.Cancel();
        if (s_delivering == this)
            s_delivering = null;
    }

    protected virtual void Start()
    {
        ResolveTarget();
        if (hpPresenter != null)
            hpPresenter.Detach();
        EnsureAgentOnNavMesh();
    }

    void EnsureAgentOnNavMesh()
    {
        if (agent == null || !agent.enabled)
        {
            _agentReady = false;
            return;
        }

        if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            agent.Warp(hit.position);

        _agentReady = agent.isOnNavMesh;
        if (!_agentReady)
        {
            agent.enabled = false;
            if (!_agentWarned)
            {
                _agentWarned = true;
                Debug.LogWarning($"[EnemyBrain] {name}: no NavMesh here — using direct movement.", this);
            }
        }
    }

    protected virtual void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        float now = Time.time;
        if (_dead)
        {
            TickDeath(now);
            return;
        }

        if (target == null || _cachedTargetFor != target)
            ResolveTarget();

        if (now >= _nextPerceptionAt)
        {
            _nextPerceptionAt = now + perceptionInterval;
            TickPerception();
        }

        TickPoise(now, dt);

        switch (_state)
        {
            case EnemyBrainState.Idle: TickIdle(now, dt); break;
            case EnemyBrainState.Patrol: TickPatrol(now, dt); break;
            case EnemyBrainState.Alert: TickAlert(now, dt); break;
            case EnemyBrainState.Approach: TickApproach(now, dt); break;
            case EnemyBrainState.Strafe: TickStrafe(now, dt); break;
            case EnemyBrainState.Attack: TickAttack(now, dt); break;
            case EnemyBrainState.Recover: TickRecover(now, dt); break;
            case EnemyBrainState.Stagger: TickStagger(now, dt); break;
        }

        UpdateLocomotionAnimator(dt);
    }

    // ---------------------------------------------------------------- target
    protected void ResolveTarget()
    {
        if (target == null)
        {
            if (Time.time < _nextTargetLookupAt)
                return;
            _nextTargetLookupAt = Time.time + 2f;
            PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
            if (refs != null)
                target = refs.PlayerRoot;
            if (target == null)
            {
                GameObject tagged = GameObject.FindWithTag("Player");
                if (tagged != null)
                    target = tagged.transform;
            }
            if (target == null)
                return;
        }

        if (_cachedTargetFor == target)
            return;

        _cachedTargetFor = target;
        targetReceiver = target.GetComponentInChildren<PlayerDamageReceiver>();
        if (targetReceiver == null)
            targetReceiver = target.GetComponentInParent<IDamageReceiver>();
        targetCombat = target.GetComponentInChildren<PlayerCombatController>();
        targetHealth = target.GetComponentInChildren<PlayerHealth>();
        PlayerReferences references = target.GetComponentInParent<PlayerReferences>();
        if (references == null)
            references = target.GetComponentInChildren<PlayerReferences>();
        targetAim = references != null && references.LockPivot != null ? references.LockPivot : null;
        if (perception != null)
            perception.SetTarget(target, targetAim, targetCombat);
    }

    protected Vector3 TargetAimPoint => targetAim != null ? targetAim.position : target.position + Vector3.up * 1.2f;

    protected float FlatDistanceToTarget()
    {
        if (target == null)
            return float.PositiveInfinity;
        Vector3 d = target.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // ---------------------------------------------------------------- perception / alert
    void TickPerception()
    {
        if (target == null)
            return;

        if (perception == null)
        {
            if (!_alerted && HasTarget && FlatDistanceToTarget() < 10f)
                BecomeAlerted(true);
            return;
        }

        bool noticed = perception.Evaluate();
        if (!_alerted)
        {
            if (noticed && HasTarget)
                BecomeAlerted(true);
            return;
        }

        // 경계 해제: 대상 사망 또는 멀리서 놓침
        if (!HasTarget || (!perception.RemembersTarget && FlatDistanceToTarget() > leashRange))
            CalmDown();
    }

    public void BecomeAlerted(bool propagate)
    {
        if (_alerted || _dead)
            return;

        _alerted = true;
        if (target != null)
            EnemyAttackTokenDirector.Engage(target, transform);
        if (hpPresenter != null)
        {
            hpPresenter.Attach(health, lockPivot != null ? lockPivot : transform, hpBarOffset);
            if (hpPresenter.bar != null)
                hpPresenter.bar.OnHPChangedCallback(health.CurrentHP, health.MaxHP);
        }
        SetGlow(alertGlow);
        if (alertBark != null)
            OneShotAudio.PlayAt(alertBark, transform.position + Vector3.up * 1.6f, alertBarkVolume, 1f, 0.85f);

        if (propagate)
            AlertNearby();

        OnAlerted();
        if (_state == EnemyBrainState.Idle || _state == EnemyBrainState.Patrol)
        {
            _alertUntil = Time.time + alertDuration;
            SetState(EnemyBrainState.Alert);
            PlayAction(_alertHash, 1f);
        }
    }

    void AlertNearby()
    {
        if (alertShareRadius > 0f)
            EnemyAlertNetwork.Broadcast(this, transform.position, alertShareRadius, target != null ? target.position : transform.position);
    }

    // IEnemyAlertable
    public Vector3 AlertPosition => transform.position;
    public bool IsAlertedOrDead => _alerted || _dead;

    public void ReceiveAlert(Vector3 knownTargetPosition)
    {
        if (_alerted || _dead)
            return;
        if (perception != null)
            perception.NotifyAlerted(knownTargetPosition);
        BecomeAlerted(false);
    }

    void CalmDown()
    {
        _alerted = false;
        ReleaseTokens();
        if (target != null)
            EnemyAttackTokenDirector.Disengage(target, transform);
        if (hpPresenter != null)
            hpPresenter.Detach();
        SetGlow(idleGlow);
        if (_state != EnemyBrainState.Stagger)
            SetState(patrolPoints != null && patrolPoints.Length > 0 ? EnemyBrainState.Patrol : EnemyBrainState.Idle);
    }

    protected virtual void OnAlerted() { }

    // ---------------------------------------------------------------- states
    protected void SetState(EnemyBrainState next)
    {
        if (_state == next && _stateEnteredAt > 0f)
            return;
        EnemyBrainState prev = _state;
        _state = next;
        _stateEnteredAt = Time.time;
        if (next != EnemyBrainState.Attack && prev == EnemyBrainState.Attack && cue != null && !_hitDone)
            cue.Cancel();
        if (debugLogs)
            Debug.Log($"[EnemyBrain] {name}: {prev} -> {next}", this);
        OnStateEntered(prev, next);
        StateChanged?.Invoke(this, next);
    }

    protected virtual void OnStateEntered(EnemyBrainState previous, EnemyBrainState next) { }

    protected virtual void TickIdle(float now, float dt)
    {
        StopMoving();
        if (_alerted)
        {
            SetState(EnemyBrainState.Approach);
            return;
        }
        if (patrolPoints != null && patrolPoints.Length > 0)
            SetState(EnemyBrainState.Patrol);
    }

    protected virtual void TickPatrol(float now, float dt)
    {
        if (_alerted)
        {
            SetState(EnemyBrainState.Approach);
            return;
        }
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            SetState(EnemyBrainState.Idle);
            return;
        }

        if (now < _patrolWaitUntil)
        {
            StopMoving();
            return;
        }

        Transform point = patrolPoints[_patrolIndex % patrolPoints.Length];
        if (point == null)
        {
            _patrolIndex++;
            return;
        }

        Vector3 to = point.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.36f)
        {
            _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
            _patrolWaitUntil = now + patrolWait;
            StopMoving();
            return;
        }

        MoveTo(point.position, walkSpeed, dt);
        FaceTowards(transform.position + CurrentVelocity(), turnSpeedDeg * 0.5f, dt);
    }

    protected virtual void TickAlert(float now, float dt)
    {
        StopMoving();
        if (target != null)
            FaceTowards(target.position, turnSpeedDeg, dt);
        if (now >= _alertUntil)
        {
            PlayLocomotion();
            SetState(EnemyBrainState.Approach);
        }
    }

    protected virtual void TickApproach(float now, float dt)
    {
        if (!HasTarget)
        {
            StopMoving();
            return;
        }

        float dist = FlatDistanceToTarget();
        bool holds = EnemyAttackTokenDirector.Holds(target, this, EnemyTokenKind.Melee);
        if (!holds && dist <= tokenRequestRange && now >= _nextAttackAllowedAt && now >= _nextTokenTryAt)
        {
            _nextTokenTryAt = now + 0.35f;
            holds = TryAcquireToken(EnemyTokenKind.Melee, 4f);
            if (holds)
                _approachTokenSince = now;
        }

        if (holds)
        {
            EnemyStrike[] chosen = ChooseAttack(dist);
            if (chosen != null && chosen.Length > 0)
            {
                BeginSequence(chosen, EnemyTokenKind.Melee);
                return;
            }

            if (now - _approachTokenSince > 3.5f)
            {
                // 너무 오래 못 닿으면 양보
                ReleaseTokens();
                _nextAttackAllowedAt = now + 0.8f;
                SetState(EnemyBrainState.Strafe);
                return;
            }

            EnemyAttackTokenDirector.TryAcquire(target, this, EnemyTokenKind.Melee, 4f);
            MoveTo(target.position, runSpeed, dt);
            FaceTowards(target.position, turnSpeedDeg, dt);
            return;
        }

        if (dist <= ringRadius + 0.75f)
        {
            SetState(EnemyBrainState.Strafe);
            return;
        }

        MoveTo(target.position, runSpeed, dt);
        FaceTowards(dist < ringRadius * 2f ? target.position : transform.position + CurrentVelocity(), turnSpeedDeg, dt);
    }

    protected virtual void TickStrafe(float now, float dt)
    {
        if (!HasTarget)
        {
            StopMoving();
            return;
        }

        float dist = FlatDistanceToTarget();
        if (now >= _nextAttackAllowedAt && now >= _nextTokenTryAt)
        {
            _nextTokenTryAt = now + UnityEngine.Random.Range(0.3f, 0.55f);
            if (dist <= tokenRequestRange && TryAcquireToken(EnemyTokenKind.Melee, 4f))
            {
                _approachTokenSince = now;
                SetState(EnemyBrainState.Approach);
                return;
            }
        }

        if (dist > ringRadius + 2.5f)
        {
            SetState(EnemyBrainState.Approach);
            return;
        }

        Vector3 c = target.position;
        Vector3 from = transform.position - c;
        from.y = 0f;
        float bearing = from.sqrMagnitude > 0.0001f ? Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg : transform.eulerAngles.y + 180f;

        float nearest = EnemyAttackTokenDirector.NearestEngagedBearingDelta(target, transform);
        if (Mathf.Abs(nearest) < ringSpacingDeg)
            _strafeSign = nearest > 0f ? -1 : 1;
        else if (now >= _nextStrafeFlipAt)
        {
            _strafeSign = UnityEngine.Random.value < 0.5f ? -1 : 1;
            _nextStrafeFlipAt = now + UnityEngine.Random.Range(1.2f, 2.4f);
        }

        float desired = (bearing + _strafeSign * 32f) * Mathf.Deg2Rad;
        Vector3 dest = c + new Vector3(Mathf.Sin(desired), 0f, Mathf.Cos(desired)) * ringRadius;
        dest.y = transform.position.y;
        MoveTo(dest, strafeSpeed, dt);
        FaceTowards(c, turnSpeedDeg, dt);
    }

    protected virtual void TickRecover(float now, float dt)
    {
        StopMoving();
        if (target != null)
            FaceTowards(target.position, turnSpeedDeg * 0.5f, dt);
        if (now >= _recoverUntil)
        {
            PlayLocomotion();
            SetState(_alerted ? EnemyBrainState.Strafe : EnemyBrainState.Idle);
        }
    }

    protected virtual void TickStagger(float now, float dt)
    {
        StopMoving();
        if (now >= _staggerUntil)
        {
            _riposteWindow = false;
            _poise = maxPoise;
            PlayLocomotion();
            _nextAttackAllowedAt = Mathf.Max(_nextAttackAllowedAt, now + 0.6f);
            SetState(_alerted ? EnemyBrainState.Strafe : EnemyBrainState.Idle);
        }
    }

    /// <summary>근접 토큰을 쥔 상태에서 거리별 공격 선택. 아직 거리가 안 맞으면 null.</summary>
    protected abstract EnemyStrike[] ChooseAttack(float distance);

    // ---------------------------------------------------------------- attack sequence
    protected bool TryAcquireToken(EnemyTokenKind kind, float hold)
    {
        return target == null || EnemyAttackTokenDirector.TryAcquire(target, this, kind, hold);
    }

    protected void ReleaseTokens()
    {
        if (target != null)
            EnemyAttackTokenDirector.ReleaseAll(target, this);
    }

    protected void BeginSequence(EnemyStrike[] sequence, EnemyTokenKind token)
    {
        _sequence = sequence;
        _sequenceIndex = 0;
        _sequenceToken = token;
        _abortSequence = false;
        SetState(EnemyBrainState.Attack);
        StartStrike(sequence[0]);
    }

    void StartStrike(EnemyStrike s)
    {
        float now = Time.time;
        _strike = s;
        _hitAt = now + s.windup;
        float lead = s.EffectiveCueLead;
        _cueAt = lead > 0f && s.cueType != EnemyCueType.None ? _hitAt - lead : float.PositiveInfinity;
        _cueIssued = false;
        _hitDone = false;
        _strikeEndAt = float.PositiveInfinity;
        _lungeRemaining = s.lungeDistance;
        _attackSequenceId++;
        // 연타 전체 시간만큼 토큰 유지
        if (target != null)
            EnemyAttackTokenDirector.TryAcquire(target, this, _sequenceToken, s.windup + s.recovery + 1f);

        StopMoving();
        if (s.animHash == 0 && !string.IsNullOrEmpty(s.animState))
            s.animHash = Animator.StringToHash(s.animState);
        // 클립의 '닿는 순간'을 판정 시각에 맞춘다: 재생 속도 조절, 그래도 클립이 너무 빠르면 시작을 늦춘다
        float speed = s.clipImpactTime > 0.01f ? Mathf.Clamp(s.clipImpactTime / s.windup, 0.7f, 1.9f) : 1f;
        float delay = s.clipImpactTime > 0.01f ? s.windup - s.clipImpactTime / speed : 0f;
        if (delay > 0.03f)
        {
            _pendingAnimHash = s.animHash;
            _pendingAnimSpeed = speed;
            _pendingAnimAt = now + delay;
        }
        else
        {
            _pendingAnimHash = 0;
            PlayAction(s.animHash, speed);
        }
        OnStrikeStarted(s);
    }

    protected virtual void OnStrikeStarted(EnemyStrike s) { }

    protected virtual void TickAttack(float now, float dt)
    {
        EnemyStrike s = _strike;
        if (s == null)
        {
            FinishSequence();
            return;
        }

        if (_pendingAnimHash != 0 && now >= _pendingAnimAt)
        {
            PlayAction(_pendingAnimHash, _pendingAnimSpeed);
            _pendingAnimHash = 0;
        }

        if (!_hitDone && target != null && now < _hitAt - s.trackLockBeforeHit)
            FaceTowards(target.position, s.trackTurnRate, dt);

        if (!_hitDone && _lungeRemaining > 0f && now >= _hitAt - s.lungeDuration)
        {
            float step = Mathf.Min(_lungeRemaining, s.lungeDistance * dt / Mathf.Max(0.05f, s.lungeDuration));
            _lungeRemaining -= step;
            if (target == null || FlatDistanceToTarget() > targetRadius + 0.55f)
                MoveDirect(transform.forward * step);
        }

        if (!_cueIssued && now >= _cueAt)
        {
            _cueIssued = true;
            float lead = _hitAt - now;
            if (lead > 0.02f)
            {
                _cueTime = now;
                _cueLead = lead;
                CueCount++;
                if (cue != null)
                    cue.Issue(s.cueType, lead, _lungeRemaining + s.range + targetRadius, s.width, transform);
                CueIssued?.Invoke(this, s.cueType, lead);
            }
        }

        if (!_hitDone && now >= _hitAt)
        {
            _hitDone = true;
            if (_cueIssued && _cueLead > 0f)
                LastCueHonestyError = (now - _cueTime) - _cueLead;
            if (swingSfx != null)
                OneShotAudio.PlayAt(swingSfx, transform.position + Vector3.up * 1.2f, 0.7f, UnityEngine.Random.Range(0.95f, 1.08f), 0.8f);
            DeliverStrike(s);
            if (_state != EnemyBrainState.Attack || _strike != s)
                return;
            _strikeEndAt = now + s.recovery;
        }

        if (_hitDone && now >= _strikeEndAt)
        {
            _sequenceIndex++;
            if (!_abortSequence && _sequence != null && _sequenceIndex < _sequence.Length)
                StartStrike(_sequence[_sequenceIndex]);
            else
                FinishSequence();
        }
    }

    protected void FinishSequence()
    {
        float now = Time.time;
        ReleaseTokens();
        _sequence = null;
        _strike = null;
        _nextAttackAllowedAt = now + UnityEngine.Random.Range(attackCooldownMin, attackCooldownMax);
        _recoverUntil = now + 0.15f;
        PlayLocomotion();
        SetState(EnemyBrainState.Recover);
        OnSequenceFinished();
    }

    protected virtual void OnSequenceFinished() { }

    protected void AbortSequence()
    {
        _pendingAnimHash = 0;
        _abortSequence = true;
        _sequence = null;
        _strike = null;
        ReleaseTokens();
        if (cue != null)
            cue.Cancel();
    }

    /// <summary>판정 사각형 [0, range] × width (소유자 전방) 안에 대상이 있는가.</summary>
    protected bool IsTargetInStrikeVolume(Transform frame, float range, float width)
    {
        if (target == null)
            return false;
        Vector3 local = frame.InverseTransformPoint(target.position);
        if (Mathf.Abs(local.y) > 2.2f)
            return false;
        return local.z >= -targetRadius && local.z <= range + targetRadius && Mathf.Abs(local.x) <= width * 0.5f + targetRadius;
    }

    protected virtual void DeliverStrike(EnemyStrike s)
    {
        if (targetReceiver == null || !IsTargetInStrikeVolume(transform, s.range, s.width))
            return;

        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        HitPayload payload = new HitPayload
        {
            damage = s.damage,
            hitType = s.hitType,
            hitPoint = TargetAimPoint,
            hitDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward,
            attacker = transform,
            attackSequenceId = _attackSequenceId
        };
        EnemyCuePalette.ApplyDefenseRules(ref payload, s.cueType);
        DeliverPayload(payload, s);
    }

    /// <summary>플레이어에게 히트 전달. 패링 통지는 이 호출 안에서 동기적으로 온다.</summary>
    protected void DeliverPayload(HitPayload payload, EnemyStrike s)
    {
        if (targetReceiver == null)
            return;
        _pendingParry = false;
        s_delivering = this;
        try
        {
            targetReceiver.ReceiveHit(payload);
        }
        finally
        {
            s_delivering = null;
        }

        if (_pendingParry)
        {
            _pendingParry = false;
            HandleParried(s);
        }
    }

    // ---------------------------------------------------------------- parry
    public void OnParried(GameObject parrier, float riposteDamage, float stunDuration)
    {
        // CombatRewardUtility 는 공격자 루트 아래 첫 IParryReact 를 고른다 → 실제 전달 중인 적에게 넘긴다.
        if (s_delivering != null && s_delivering != this)
        {
            s_delivering.OnParried(parrier, riposteDamage, stunDuration);
            return;
        }
        if (s_delivering == this)
        {
            _pendingParry = true;
            return;
        }
        if (_state == EnemyBrainState.Attack && _strike != null)
            HandleParried(_strike);
    }

    protected virtual void HandleParried(EnemyStrike s)
    {
        if (_dead)
            return;
        AbortSequence();
        bool full = s != null && (s.cueType == EnemyCueType.Parryable || s.cueType == EnemyCueType.Heavy);
        if (full)
        {
            _poise = 0f;
            EnterStagger(parryStaggerDuration, true, _parryRecoilHash);
        }
        else
        {
            EnterStagger(deflectRecoilDuration, false, _parryRecoilHash);
        }
    }

    protected void EnterStagger(float duration, bool riposteWindow, int animHash)
    {
        if (_dead)
            return;
        if (_state == EnemyBrainState.Attack)
            AbortSequence();
        ReleaseTokens();
        _riposteWindow = riposteWindow;
        _staggerUntil = Time.time + duration;
        StopMoving();
        PlayAction(animHash != 0 ? animHash : _staggerHash, 1f);
        SetState(EnemyBrainState.Stagger);
        OnStaggered(duration, riposteWindow);
    }

    protected virtual void OnStaggered(float duration, bool riposteWindow) { }

    protected int StaggerHash => _staggerHash;
    protected int FlinchHash => _flinchHash;

    // ---------------------------------------------------------------- damage
    public void ReceiveHit(HitPayload payload)
    {
        if (_dead)
            return;

        float damageMul = 1f;
        float poiseMul = 1f;
        if (InRiposteWindow)
            damageMul *= riposteDamageMultiplier;

        bool landed = ModifyIncomingHit(ref payload, ref damageMul, ref poiseMul);
        if (!_alerted)
        {
            if (perception != null && target != null)
                perception.NotifyAlerted(target.position);
            BecomeAlerted(true);
        }
        if (!landed)
            return;

        int amount = Mathf.Max(1, Mathf.RoundToInt(payload.damage * damageMul));
        int before = health.CurrentHP;
        health.TakeDamage(amount, payload.hitType, payload.hitPoint);
        if (health.CurrentHP < before)
            CombatRewardUtility.TryGrantBasicAttackGauge(payload.attacker);

        SpawnHitFeedback(payload);
        if (_dead || health.IsDead)
            return;

        ApplyPoiseDamage(PoiseDamageFor(payload.hitType) * poiseMul);
    }

    /// <summary>false = 피해 무효 (방패 막기 등). damage/poise 배율 조정 가능.</summary>
    protected virtual bool ModifyIncomingHit(ref HitPayload payload, ref float damageMultiplier, ref float poiseMultiplier) => true;

    protected virtual float PoiseDamageFor(HitType type)
    {
        switch (type)
        {
            case HitType.Strong: return 18f;
            case HitType.Heavy: return 26f;
            case HitType.Force: return 36f;
            case HitType.Parried: return 40f;
            case HitType.Guarded: return 4f;
            default: return 10f;
        }
    }

    protected void ApplyPoiseDamage(float amount)
    {
        float now = Time.time;
        _lastPoiseHitAt = now;
        if (_state == EnemyBrainState.Stagger)
            return;

        _poise -= Mathf.Max(0f, amount);
        if (_poise <= 0f)
        {
            _poise = 0f;
            EnterStagger(staggerDuration, true, _staggerHash);
            return;
        }

        bool armored = _state == EnemyBrainState.Attack && _strike != null && _strike.hyperArmor && !_hitDone;
        if (armored || now < _flinchImmuneUntil || flinchDuration <= 0f)
            return;

        if (now - _flinchWindowStart > 1.6f)
        {
            _flinchWindowStart = now;
            _flinchCount = 0;
        }
        _flinchCount++;
        if (_flinchCount >= flinchLockoutCount)
        {
            _flinchImmuneUntil = now + 1.8f;
            _flinchCount = 0;
            if (OnFlinchLockout())
                return;
        }

        if (_state == EnemyBrainState.Attack)
            AbortSequence();
        _recoverUntil = now + flinchDuration;
        PlayAction(_flinchHash, 1f);
        SetState(EnemyBrainState.Recover);
    }

    /// <summary>연속 경직 한도 도달. true 면 하위 클래스가 반응(백스텝 등)을 처리.</summary>
    protected virtual bool OnFlinchLockout() => false;

    protected void SpawnHitFeedback(HitPayload payload)
    {
        if (hitVfx != null)
        {
            Quaternion rot = payload.hitDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(-payload.hitDirection.normalized, Vector3.up)
                : Quaternion.identity;
            TransientVfxPool.Spawn(hitVfx, payload.hitPoint, rot, null, 1f);
        }
        if (hitSfx != null)
            OneShotAudio.PlayAt(hitSfx, payload.hitPoint, 0.85f, UnityEngine.Random.Range(0.92f, 1.08f), 0.7f);
    }

    void TickPoise(float now, float dt)
    {
        if (_state == EnemyBrainState.Stagger || _poise >= maxPoise)
            return;
        if (now - _lastPoiseHitAt < poiseRegenDelay)
            return;
        _poise = Mathf.Min(maxPoise, _poise + poiseRegenPerSecond * dt);
    }

    // ---------------------------------------------------------------- death
    void HandleHealthDied()
    {
        Die();
    }

    protected void Die()
    {
        if (_dead)
            return;
        _dead = true;
        _deathAt = Time.time;
        _deathPos = transform.position;
        AbortSequence();
        if (target != null)
            EnemyAttackTokenDirector.Disengage(target, transform);
        StopMoving();
        if (agent != null)
            agent.enabled = false;
        _agentReady = false;
        SetCollidersEnabled(false);
        if (hpPresenter != null)
            hpPresenter.Detach();
        SetGlow(0f);
        PlayAction(_dieHash, 1f);
        SetState(EnemyBrainState.Dead);
        OnDied();
        Died?.Invoke(this);
    }

    protected virtual void OnDied() { }

    void TickDeath(float now)
    {
        if (deathSinkDepth <= 0f && !deactivateOnDeath)
            return;
        float t = (now - _deathAt - deathSinkDelay) / Mathf.Max(0.05f, deathSinkDuration);
        if (t <= 0f)
            return;
        if (deathSinkDepth > 0f)
            transform.position = _deathPos - Vector3.up * (deathSinkDepth * Mathf.Clamp01(t));
        if (t >= 1f && deactivateOnDeath)
            gameObject.SetActive(false);
    }

    void SetCollidersEnabled(bool on)
    {
        if (_colliders == null)
            return;
        for (int i = 0; i < _colliders.Length; i++)
        {
            Collider c = _colliders[i];
            if (c == null || (c.isTrigger && !c.enabled && on))
                continue;
            if (c.name == "CueFootprint")
                continue;
            c.enabled = on;
        }
    }

    // ---------------------------------------------------------------- movement
    protected Vector3 CurrentVelocity()
    {
        return _agentReady && agent != null ? agent.velocity : _fallbackVelocity;
    }

    protected void MoveTo(Vector3 destination, float speed, float dt)
    {
        if (agent != null && agent.enabled && !_agentReady)
            EnsureAgentOnNavMesh();

        if (_agentReady)
        {
            agent.isStopped = false;
            agent.speed = speed;
            float now = Time.time;
            if (now >= _nextRepathAt || (destination - _lastDestination).sqrMagnitude > 1f)
            {
                agent.SetDestination(destination);
                _lastDestination = destination;
                _nextRepathAt = now + repathInterval;
            }
            return;
        }

        Vector3 to = destination - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.1f)
        {
            _fallbackVelocity = Vector3.zero;
            return;
        }
        Vector3 v = to / dist * speed;
        _fallbackVelocity = v;
        transform.position += v * Mathf.Min(dt, dist / Mathf.Max(0.01f, speed));
    }

    protected void StopMoving()
    {
        _fallbackVelocity = Vector3.zero;
        if (_agentReady && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            if (!agent.isStopped)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
        }
    }

    /// <summary>경로 무시 직접 이동 (돌진/백스텝). NavMesh 위면 Agent.Move 로 가장자리 유지.</summary>
    protected void MoveDirect(Vector3 delta)
    {
        delta.y = 0f;
        if (_agentReady && agent != null && agent.isOnNavMesh)
            agent.Move(delta);
        else
            transform.position += delta;
    }

    protected void FaceTowards(Vector3 point, float degPerSecond, float dt)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0004f)
            return;
        Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degPerSecond * dt);
    }

    // ---------------------------------------------------------------- animator
    void CacheAnimatorHashes()
    {
        _locomotionHash = Animator.StringToHash(locomotionState);
        _alertHash = Animator.StringToHash(alertState);
        _flinchHash = Animator.StringToHash(flinchState);
        _staggerHash = Animator.StringToHash(staggerState);
        _parryRecoilHash = Animator.StringToHash(parryRecoilState);
        _dieHash = Animator.StringToHash(dieState);
        _moveXHash = Animator.StringToHash("MoveX");
        _moveZHash = Animator.StringToHash("MoveZ");
        _hasMoveParams = false;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimatorControllerParameter[] ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].nameHash == _moveZHash)
                    _hasMoveParams = true;
            }
        }
    }

    protected int HashOf(string stateName) => string.IsNullOrEmpty(stateName) ? 0 : Animator.StringToHash(stateName);

    protected void PlayAction(int stateHash, float speed)
    {
        if (animator == null || stateHash == 0 || !animator.isActiveAndEnabled)
            return;
        if (!animator.HasState(0, stateHash))
            return;
        animator.speed = Mathf.Max(0.05f, speed);
        animator.CrossFadeInFixedTime(stateHash, crossFade, 0, 0f);
    }

    protected void PlayLocomotion()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;
        animator.speed = 1f;
        if (animator.HasState(0, _locomotionHash))
            animator.CrossFadeInFixedTime(_locomotionHash, 0.15f, 0, 0f);
    }

    void UpdateLocomotionAnimator(float dt)
    {
        if (!_hasMoveParams || animator == null || !animator.isActiveAndEnabled)
            return;
        Vector3 v = CurrentVelocity();
        Vector3 local = transform.InverseTransformDirection(v);
        float inv = 1f / Mathf.Max(0.1f, runSpeed);
        animator.SetFloat(_moveXHash, local.x * inv, 0.12f, dt);
        animator.SetFloat(_moveZHash, local.z * inv, 0.12f, dt);
    }

    // ---------------------------------------------------------------- glow
    void CacheGlowSlots()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] mats = renderers[r].sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null || !mats[m].HasProperty(IntensityId) || mats[m].name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (pass == 1)
                        _glow[count] = new GlowSlot { renderer = renderers[r], index = m };
                    count++;
                }
            }
            if (pass == 0)
            {
                _glow = new GlowSlot[count];
                count = 0;
            }
        }
    }

    protected void SetGlow(float intensity)
    {
        if (_glow == null)
            return;
        for (int i = 0; i < _glow.Length; i++)
        {
            Renderer r = _glow[i].renderer;
            if (r == null)
                continue;
            r.GetPropertyBlock(_glowBlock, _glow[i].index);
            _glowBlock.SetFloat(IntensityId, intensity);
            r.SetPropertyBlock(_glowBlock, _glow[i].index);
        }
    }
}
