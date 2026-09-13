using UnityEngine;

[DisallowMultipleComponent]
public class BulletProjectile : MonoBehaviour
{
    [Header("Projectile")]
    public GameObject owner;
    public float speed = 12f;
    public int damage = 15;
    public float lifetime = 6f;
    [SerializeField] bool debugLog = false;

    Collider[] _myCols;
    Collider[] _ownerCols;
    bool _ignored;

    void Awake()
    {
        _myCols = GetComponentsInChildren<Collider>(true);
    }

    public void Init(GameObject owner, float speed, int damage)
    {
        this.owner = owner;
        this.speed = speed;
        this.damage = damage;

        if (owner != null)
        {
            _ownerCols = owner.GetComponentsInChildren<Collider>(true);
            if (_ownerCols != null && _myCols != null)
            {
                foreach (var oc in _ownerCols)
                {
                    if (oc == null) continue;
                    foreach (var mc in _myCols)
                    {
                        if (mc == null) continue;
                        Physics.IgnoreCollision(oc, mc, true);
                    }
                }
                _ignored = true;
            }
        }

        Destroy(gameObject, lifetime);

        if (debugLog)
            Debug.Log($"[BulletProjectile] Init owner={(owner ? owner.name : "null")} speed={speed} dmg={damage}", this);
    }

    void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform))) return;

        if (debugLog)
            Debug.Log($"[BulletProjectile] Collided with {other.gameObject.name}", this);

        var hitReceiver = other.GetComponentInParent<IHitReceiver>();
        if (hitReceiver != null)
        {
            hitReceiver.ReceiveHit(CreateHitData());
            if (debugLog)
                Debug.Log("[BulletProjectile] Delivered hit to IHitReceiver on " + other.gameObject.name, this);
            Destroy(gameObject);
            return;
        }

        var damageReceiver = other.GetComponentInParent<PlayerDamageReceiver>();
        if (damageReceiver != null)
        {
            damageReceiver.ReceiveHit(damage, owner ? owner.transform : null, transform.position, false);
            if (debugLog)
                Debug.Log("[BulletProjectile] Delivered hit to PlayerDamageReceiver on " + other.gameObject.name, this);
            Destroy(gameObject);
            return;
        }

        var health = other.GetComponentInParent<IHealth>();
        if (health != null)
        {
            try
            {
                health.ApplyDamage(damage);
                if (debugLog)
                    Debug.Log("[BulletProjectile] Applied damage via IHealth on " + other.gameObject.name, this);
            }
            catch
            {
                if (debugLog)
                    Debug.LogWarning("[BulletProjectile] IHealth.ApplyDamage threw or not supported on " + other.gameObject.name, this);
            }

            Destroy(gameObject);
            return;
        }

        if (debugLog)
            Debug.Log("[BulletProjectile] No damage target found for " + other.gameObject.name, this);
        Destroy(gameObject);
    }

    HitData CreateHitData()
    {
        var data = new HitData();
        try
        {
            data.attacker = owner;
            data.damage = damage;
            data.hitPoint = transform.position;
            data.hitType = HitType.Normal;
            data.isParryable = false;
        }
        catch
        {
        }

        return data;
    }

    void OnDestroy()
    {
        if (!_ignored || _ownerCols == null || _myCols == null) return;

        foreach (var oc in _ownerCols)
        {
            if (oc == null) continue;
            foreach (var mc in _myCols)
            {
                if (mc == null) continue;
                Physics.IgnoreCollision(oc, mc, false);
            }
        }
    }

    public void Reflect(GameObject newOwner)
    {
        owner = newOwner ?? owner;
        transform.forward = -transform.forward;

        if (debugLog)
            Debug.Log("[BulletProjectile] Reflected. newOwner=" + (owner ? owner.name : "null"), this);
    }
}
