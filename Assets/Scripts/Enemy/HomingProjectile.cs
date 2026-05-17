using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HomingProjectile : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] bool debugLogs = false;

    [Header("Movement")]
    public Transform target;
    public float speed = 12f;
    public float turnSpeedDeg = 720f;
    public float lifeTime = 8f;
    public int damage = 15;

    [Header("Owner Grace")]
    public GameObject owner;
    public float graceDuration = 0.12f;

    [Header("Performance")]
    [SerializeField] bool useKinematicTransformMovement = true;

    Collider _col;
    Rigidbody _rb;
    float _spawnTime;
    float _lifeRemaining;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _rb = GetComponent<Rigidbody>();

        if (_rb != null)
        {
            _rb.useGravity = false;
            if (useKinematicTransformMovement)
                _rb.isKinematic = true;
        }
    }

    void OnEnable()
    {
        _lifeRemaining = lifeTime;
    }

    public void Init(Transform t, float initialSpeed, GameObject from, int dmg = -1)
    {
        target = t;
        speed = initialSpeed;
        owner = from;
        if (dmg > 0)
            damage = dmg;

        _spawnTime = Time.time;
        _lifeRemaining = lifeTime;

        if (debugLogs)
            Debug.Log($"[HomingProjectile] Init target={(t ? t.name : "null")} speed={initialSpeed} owner={(from ? from.name : "null")} dmg={damage}", this);
    }

    void Update()
    {
        _lifeRemaining -= Time.deltaTime;
        if (_lifeRemaining <= 0f)
        {
            ReleaseSelf();
            return;
        }

        if (target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            Vector3 toFlat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (toFlat.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(toFlat.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeedDeg * Time.deltaTime);
            }
        }

        if (_rb != null && !useKinematicTransformMovement)
            _rb.velocity = transform.forward * speed;
        else
            transform.position += transform.forward * speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform)))
        {
            if (Time.time - _spawnTime <= graceDuration)
                return;

            return;
        }
        if (IsFriendlyDroneHit(other))
            return;

        HitPayload payload = new HitPayload
        {
            attacker = owner != null ? owner.transform : transform,
            damage = damage,
            hitPoint = transform.position,
            hitDirection = transform.forward,
            hitType = HitType.Normal,
            canParry = false,
            canPerfectDodge = true,
            canGuard = true,
            unblockable = false
        };

        IDamageReceiver damageReceiver = other.GetComponentInParent<IDamageReceiver>();
        if (damageReceiver != null)
        {
            damageReceiver.ReceiveHit(payload);
        }
        else
        {
            IHealth health = other.GetComponentInParent<IHealth>();
            if (health != null)
            {
                try
                {
                    CombatHealthApplicationUtility.ApplyHitPayload(health, payload);
                }
                catch
                {
                }
            }
        }

        ReleaseSelf();
    }

    bool IsFriendlyDroneHit(Collider other)
    {
        if (owner == null || other == null)
            return false;

        DroneController ownerDrone = owner.GetComponentInParent<DroneController>();
        if (ownerDrone == null)
            return false;

        DroneController hitDrone = other.GetComponentInParent<DroneController>();
        return hitDrone != null && hitDrone != ownerDrone;
    }

    void OnDisable()
    {
        target = null;
        owner = null;
    }

    void ReleaseSelf()
    {
        RuntimeObjectPool.Release(gameObject);
    }
}
