using UnityEngine;

// 순찰 드론 두뇌: 기존 DroneController 전투(스트레이프/텔레그래프/사격/피격/사망)는 그대로 두고,
// 그 앞에 Idle/Patrol → Alert 단계만 얹는다.
//  - 경계 전: DroneController.target = null (기존 코드는 대상이 없으면 아무것도 안 함) + 순찰 지점 사이 저속 비행.
//  - 발견(시야/청각/피격/동료 경보) → '발견' 비프 + HP 바 + target 복원 → 이후는 기존 드론 그대로.
//  - 공격 토큰: DroneController.useAttackTokens (프리팹에서 켬) → 대상당 원거리 1기.
[DisallowMultipleComponent]
[RequireComponent(typeof(DroneController))]
public sealed class PatrolDroneBrain : MonoBehaviour, IEnemyAlertable
{
    [SerializeField] Transform[] patrolPoints;
    [SerializeField, Min(0.1f)] float patrolSpeed = 1.8f;
    [SerializeField, Min(0f)] float patrolWait = 1.5f;
    [SerializeField, Min(10f)] float patrolTurnSpeed = 120f;
    [SerializeField] bool startAlerted = false;

    [Header("Alert")]
    [SerializeField] AudioClip alertBark;
    [SerializeField, Range(0f, 1f)] float alertBarkVolume = 0.8f;
    [SerializeField, Min(0f)] float alertShareRadius = 9f;
    [SerializeField, Min(0.02f)] float perceptionInterval = 0.15f;

    [Header("UI")]
    [SerializeField] EnemyHPBarPresenter hpPresenter;
    [SerializeField] Transform lockPivot;
    [SerializeField] Vector3 hpBarOffset = new Vector3(0f, 0.6f, 0f);

    DroneController _drone;
    EnemyPerception _perception;
    EnemyHealth _health;
    Rigidbody _rb;
    Transform _player;
    bool _alerted;
    int _patrolIndex;
    float _waitUntil;
    float _nextPerceptionAt;
    float _nextLookupAt;
    Vector3 _pendingMove;
    Transform _perceptionTarget;

    public bool IsAlerted => _alerted;
    public Vector3 AlertPosition => transform.position;
    public bool IsAlertedOrDead => _alerted || (_drone != null && _drone.IsDead);

    void Awake()
    {
        _drone = GetComponent<DroneController>();
        _perception = GetComponent<EnemyPerception>();
        _health = GetComponent<EnemyHealth>();
        _rb = GetComponent<Rigidbody>();
        if (hpPresenter == null)
            hpPresenter = GetComponent<EnemyHPBarPresenter>();
        if (lockPivot == null)
            lockPivot = transform.Find("LockPivot");
    }

    void OnEnable()
    {
        EnemyAlertNetwork.Register(this);
        _drone.Damaged += HandleDamaged;
        _drone.Died += HandleDied;
    }

    void OnDisable()
    {
        EnemyAlertNetwork.Unregister(this);
        _drone.Damaged -= HandleDamaged;
        _drone.Died -= HandleDied;
        if (hpPresenter != null && _alerted)
            hpPresenter.Detach();
        _alerted = false;
    }

    void Start()
    {
        // DroneController.Awake 가 플레이어를 이미 찾아 두었다 → 경계 전까지 보관
        _player = _drone.target;
        ResolvePlayer();
        if (hpPresenter != null)
            hpPresenter.Detach();
        if (startAlerted)
            Alert(true);
        else
            _drone.target = null;
    }

    void ResolvePlayer()
    {
        if (_player == null && Time.time >= _nextLookupAt)
        {
            _nextLookupAt = Time.time + 2f;
            PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
            if (refs != null)
                _player = refs.PlayerRoot;
            if (_player == null)
            {
                GameObject tagged = GameObject.FindWithTag("Player");
                if (tagged != null)
                    _player = tagged.transform;
            }
        }

        if (_player != null && _perception != null && _perceptionTarget != _player)
        {
            _perceptionTarget = _player;
            PlayerReferences r = _player.GetComponentInParent<PlayerReferences>();
            _perception.SetTarget(_player, r != null ? r.LockPivot : null, _player.GetComponentInChildren<PlayerCombatController>());
        }
    }

    void Update()
    {
        if (_alerted || _drone.IsDead || Time.deltaTime <= 0f)
            return;

        if (_player == null)
            ResolvePlayer();

        if (_perception != null && _player != null && Time.time >= _nextPerceptionAt)
        {
            _nextPerceptionAt = Time.time + perceptionInterval;
            if (_perception.Evaluate())
            {
                Alert(true);
                return;
            }
        }

        TickPatrol(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (_pendingMove.sqrMagnitude <= 0f)
            return;
        if (_rb != null && !_rb.isKinematic)
            _rb.MovePosition(_rb.position + _pendingMove);
        else
            transform.position += _pendingMove;
        _pendingMove = Vector3.zero;
    }

    void TickPatrol(float dt)
    {
        if (patrolPoints == null || patrolPoints.Length == 0 || Time.time < _waitUntil)
            return;

        Transform p = patrolPoints[_patrolIndex % patrolPoints.Length];
        if (p == null)
        {
            _patrolIndex++;
            return;
        }

        Vector3 to = p.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.4f)
        {
            _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
            _waitUntil = Time.time + patrolWait;
            return;
        }

        Vector3 dir = to / dist;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), patrolTurnSpeed * dt);
        _pendingMove += dir * Mathf.Min(dist, patrolSpeed * dt);
    }

    void HandleDamaged(DroneController drone, int amount)
    {
        if (!_alerted)
            Alert(true);
    }

    void HandleDied(DroneController drone)
    {
        if (hpPresenter != null)
            hpPresenter.Detach();
    }

    public void ReceiveAlert(Vector3 knownTargetPosition)
    {
        if (_perception != null)
            _perception.NotifyAlerted(knownTargetPosition);
        Alert(false);
    }

    void Alert(bool propagate)
    {
        if (_alerted || _drone.IsDead)
            return;
        _alerted = true;
        _pendingMove = Vector3.zero;
        if (_player == null)
            ResolvePlayer();
        _drone.target = _player;
        if (alertBark != null)
            OneShotAudio.PlayAt(alertBark, transform.position, alertBarkVolume, 1.15f, 0.85f);
        if (hpPresenter != null && _health != null)
        {
            hpPresenter.Attach(_health, lockPivot != null ? lockPivot : transform, hpBarOffset);
            if (hpPresenter.bar != null)
                hpPresenter.bar.OnHPChangedCallback(_health.CurrentHP, _health.MaxHP);
        }
        if (propagate)
            EnemyAlertNetwork.Broadcast(this, transform.position, alertShareRadius, _player != null ? _player.position : transform.position);
    }
}
