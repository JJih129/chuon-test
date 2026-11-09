using Combat;
using UnityEngine;

// 잡몹 인지: 시야(원뿔 + 가림 레이) + 청각(플레이어 공격 시작 이벤트, 반경 내).
// Update 없음 — EnemyBrain 이 정해진 간격(기본 0.15s)으로 Evaluate 를 호출한다. 레이캐스트는 NonAlloc.
[DisallowMultipleComponent]
public sealed class EnemyPerception : MonoBehaviour
{
    [Header("Sight")]
    [SerializeField, Min(1f)] float viewDistance = 16f;
    [SerializeField, Range(10f, 360f)] float viewAngle = 120f;
    [Tooltip("이 거리 안이면 시야각과 무관하게 감지 (등 뒤 근접)")]
    [SerializeField, Min(0f)] float proximityRadius = 2.2f;
    [SerializeField] Transform eye;
    [SerializeField, Min(0f)] float eyeHeight = 1.6f;
    [Tooltip("시야를 가리는 레이어. 0 이면 Default + obstacle.")]
    [SerializeField] LayerMask occluderMask = 0;

    [Header("Hearing")]
    [Tooltip("플레이어가 이 반경 안에서 공격을 시작하면 들린다.")]
    [SerializeField, Min(0f)] float hearingRadius = 11f;

    [Header("Memory")]
    [SerializeField, Min(0f)] float memorySeconds = 6f;

    readonly RaycastHit[] _hits = new RaycastHit[8];
    Transform _target;
    Transform _targetAim;
    PlayerCombatController _subscribedCombat;
    float _heardAt = float.NegativeInfinity;

    public bool CanSeeTarget { get; private set; }
    public float LastSeenTime { get; private set; } = float.NegativeInfinity;
    public Vector3 LastKnownPosition { get; private set; }
    public bool HeardRecently => Time.time - _heardAt < 0.5f;
    public bool RemembersTarget => Time.time - Mathf.Max(LastSeenTime, _heardAt) < memorySeconds;
    public float ViewDistance => viewDistance;
    public float HearingRadius => hearingRadius;

    public void Configure(float distance, float angle, float hearing)
    {
        viewDistance = Mathf.Max(1f, distance);
        viewAngle = Mathf.Clamp(angle, 10f, 360f);
        hearingRadius = Mathf.Max(0f, hearing);
    }

    void Awake()
    {
        if (occluderMask.value == 0)
            occluderMask = LayerMask.GetMask("Default", "obstacle");
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    public void SetTarget(Transform target, Transform aim, PlayerCombatController combat)
    {
        _target = target;
        _targetAim = aim != null ? aim : target;
        if (combat == _subscribedCombat)
            return;
        Unsubscribe();
        _subscribedCombat = combat;
        if (_subscribedCombat != null)
            _subscribedCombat.OnAttackStarted += HandleTargetAttackStarted;
    }

    void Unsubscribe()
    {
        if (_subscribedCombat != null)
            _subscribedCombat.OnAttackStarted -= HandleTargetAttackStarted;
        _subscribedCombat = null;
    }

    void HandleTargetAttackStarted(AttackInput input, AttackData data, int depth)
    {
        if (_target == null || !isActiveAndEnabled)
            return;
        if ((_target.position - transform.position).sqrMagnitude > hearingRadius * hearingRadius)
            return;
        _heardAt = Time.time;
        LastKnownPosition = _target.position;
    }

    /// <summary>시야 판정 갱신. 보이거나 막 들렸으면 true.</summary>
    public bool Evaluate()
    {
        CanSeeTarget = false;
        if (_target == null)
            return false;

        Vector3 eyePos = eye != null ? eye.position : transform.position + Vector3.up * eyeHeight;
        Vector3 aim = _targetAim != null ? _targetAim.position : _target.position + Vector3.up * 1.2f;
        Vector3 to = aim - eyePos;
        float dist = to.magnitude;
        if (dist <= viewDistance)
        {
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            Vector3 fwd = eye != null ? eye.forward : transform.forward;
            fwd.y = 0f;
            bool inCone = dist <= proximityRadius || viewAngle >= 359f
                || (flat.sqrMagnitude > 0.0001f && fwd.sqrMagnitude > 0.0001f && Vector3.Angle(fwd, flat) <= viewAngle * 0.5f);
            if (inCone && HasLineOfSight(eyePos, to, dist))
            {
                CanSeeTarget = true;
                LastSeenTime = Time.time;
                LastKnownPosition = _target.position;
            }
        }

        return CanSeeTarget || HeardRecently;
    }

    bool HasLineOfSight(Vector3 from, Vector3 to, float dist)
    {
        if (dist <= 0.05f)
            return true;
        int count = Physics.RaycastNonAlloc(from, to / dist, _hits, dist, occluderMask, QueryTriggerInteraction.Ignore);
        Transform selfRoot = transform.root;
        Transform targetRoot = _target.root;
        for (int i = 0; i < count; i++)
        {
            Collider c = _hits[i].collider;
            if (c == null)
                continue;
            Transform r = c.transform.root;
            if (r == selfRoot || r == targetRoot)
                continue;
            return false;
        }
        return true;
    }

    /// <summary>외부 알림(동료 경보, 피격)으로 대상 위치를 안다.</summary>
    public void NotifyAlerted(Vector3 position)
    {
        _heardAt = Time.time;
        LastKnownPosition = position;
    }
}
