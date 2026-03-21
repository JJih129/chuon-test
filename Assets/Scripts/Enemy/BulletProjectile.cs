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

        if (debugLogs)
            Debug.Log($"[BulletProjectile] Collided with {other.gameObject.name}", this);

        var hitReceiver = other.GetComponentInParent<IHitReceiver>();
        if (hitReceiver != null)
        {
            var hd = CreateHitData();
            hitReceiver.ReceiveHit(hd);
            if (debugLogs)
                Debug.Log("[BulletProjectile] Delivered hit to IHitReceiver on " + other.gameObject.name, this);
            ReleaseSelf();
            return;
        }

        var pdr = other.GetComponentInParent<PlayerDamageReceiver>();
        if (pdr != null)
        {
            var hd = CreateHitData();
            pdr.ReceiveHit(damage, owner ? owner.transform : null, transform.position, /*isParryable*/ false);
            if (debugLogs)
                Debug.Log("[BulletProjectile] Delivered hit to PlayerDamageReceiver on " + other.gameObject.name, this);
            ReleaseSelf();
            return;
        }

        var h = other.GetComponentInParent<IHealth>();
        if (h != null)
        {
            try
            {
                h.ApplyDamage(damage);
                if (debugLogs)
                    Debug.Log("[BulletProjectile] Applied damage via IHealth on " + other.gameObject.name, this);
            }
            catch
            {
                if (debugLogs)
                    Debug.LogWarning("[BulletProjectile] IHealth.ApplyDamage threw or not supported on " + other.gameObject.name, this);
            }
            ReleaseSelf();
            return;
        }

        if (debugLogs)
            Debug.Log("[BulletProjectile] No damage target found for " + other.gameObject.name, this);
        ReleaseSelf();
    }

    HitData CreateHitData()
    {
        var hd = new HitData();
        try
        {
            hd.attacker = owner;
            hd.damage = damage;
            hd.hitPoint = transform.position;
            hd.hitType = HitType.Normal;
            hd.isParryable = false;
        }
        catch { }
        return hd;
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
