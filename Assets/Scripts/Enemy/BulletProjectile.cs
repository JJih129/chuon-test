// BulletProjectile.cs
using UnityEngine;

[DisallowMultipleComponent]
public class BulletProjectile : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] bool debugLogs = false;

    [Header("발사체 기본")]
    [Tooltip("발사자(런타임에 Init에서 설정)")]
    public GameObject owner;
    [Tooltip("이동 속도 (m/s)")]
    public float speed = 12f;
    [Tooltip("데미지")]
    public int damage = 15;
    [Tooltip("수명(초)")]
    public float lifetime = 6f;

    float _lifeRemaining;
    bool _isActive;

    public void Init(GameObject owner, float speed, int damage)
    {
        this.owner = owner;
        this.speed = speed;
        this.damage = damage;

        _lifeRemaining = lifetime;
        _isActive = true;
        if (debugLogs)
            Debug.Log($"[BulletProjectile] Init owner={(owner? owner.name : "null")} speed={speed} dmg={damage}", this);
    }

    void OnEnable()
    {
        _lifeRemaining = lifetime;
        _isActive = true;
    }

    void OnDisable()
    {
        _isActive = false;
        owner = null;
    }

    void Update()
    {
        if (!_isActive)
            return;

        _lifeRemaining -= Time.deltaTime;
        if (_lifeRemaining <= 0f)
        {
            ReleaseSelf();
            return;
        }

        transform.position += transform.forward * speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform))) return;
        if (IsFriendlyOrNonTarget(other))
            return;

        if (debugLogs)
            Debug.Log($"[BulletProjectile] Collided with {other.gameObject.name}", this);

        var damageReceiver = other.GetComponentInParent<IDamageReceiver>();
        if (damageReceiver != null)
        {
            damageReceiver.ReceiveHit(CreateHitPayload());
            if (debugLogs)
                Debug.Log("[BulletProjectile] Delivered hit to IDamageReceiver on " + other.gameObject.name, this);
            ReleaseSelf();
            return;
        }

        var h = other.GetComponentInParent<IHealth>();
        if (h != null)
        {
            try
            {
                CombatHealthApplicationUtility.ApplyHitPayload(h, CreateHitPayload());
                if (debugLogs)
                    Debug.Log("[BulletProjectile] Applied hit payload via IHealth on " + other.gameObject.name, this);
            }
            catch
            {
                if (debugLogs)
                    Debug.LogWarning("[BulletProjectile] IHealth.TakeDamage threw or not supported on " + other.gameObject.name, this);
            }
            ReleaseSelf();
            return;
        }

        if (debugLogs)
            Debug.Log("[BulletProjectile] No damage target found for " + other.gameObject.name, this);
        ReleaseSelf();
    }

    bool IsFriendlyOrNonTarget(Collider other)
    {
        if (other == null)
            return true;

        if (other.isTrigger)
            return false;

        Transform hitRoot = other.transform.root;
        if (owner != null && hitRoot == owner.transform.root)
            return true;

        if (other.GetComponentInParent<DroneController>() != null)
            return true;

        if (other.GetComponentInParent<TrainingDummyController>() != null)
            return true;

        if (other.GetComponentInParent<PlayerHealth>() != null)
            return false;

        return !other.CompareTag("Player");
    }

    HitPayload CreateHitPayload()
    {
        return new HitPayload
        {
            attacker = owner ? owner.transform : null,
            damage = damage,
            hitPoint = transform.position,
            hitDirection = transform.forward,
            hitType = HitType.Normal,
            canParry = false,
            canPerfectDodge = true,
            canGuard = true,
            unblockable = false
        };
    }

    void ReleaseSelf()
    {
        _isActive = false;
        RuntimeObjectPool.Release(gameObject);
    }

    public void Reflect(GameObject newOwner)
    {
        owner = newOwner ?? owner;
        transform.forward = -transform.forward;
        if (debugLogs)
            Debug.Log("[BulletProjectile] Reflected. newOwner=" + (owner ? owner.name : "null"), this);
    }
}
