// BulletProjectile.cs
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BulletProjectile : MonoBehaviour
{
    [Header("발사체 기본")]
    [Tooltip("발사자(런타임에 Init에서 설정)")]
    public GameObject owner;
    [Tooltip("이동 속도 (m/s)")]
    public float speed = 12f;
    [Tooltip("데미지")]
    public int damage = 15;
    [Tooltip("수명(초)")]
    public float lifetime = 6f;

    Collider[] _myCols;
    Collider[] _ownerCols;
    bool _ignored = false;

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
                foreach (var oc in _ownerCols) if (oc != null)
                    foreach (var mc in _myCols) if (mc != null)
                        Physics.IgnoreCollision(oc, mc, true);
                _ignored = true;
            }
        }

        StartCoroutine(Life());
        Debug.Log($"[BulletProjectile] Init owner={(owner? owner.name : "null")} speed={speed} dmg={damage}", this);
    }

    IEnumerator Life()
    {
        yield return new WaitForSeconds(lifetime);
        Destroy(gameObject);
    }

    void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform))) return;

        Debug.Log($"[BulletProjectile] Collided with {other.gameObject.name}", this);

        var hitReceiver = other.GetComponentInParent<IHitReceiver>();
        if (hitReceiver != null)
        {
            var hd = CreateHitData();
            hitReceiver.ReceiveHit(hd);
            Debug.Log("[BulletProjectile] Delivered hit to IHitReceiver on " + other.gameObject.name, this);
            Destroy(gameObject);
            return;
        }

        var pdr = other.GetComponentInParent<PlayerDamageReceiver>();
        if (pdr != null)
        {
            var hd = CreateHitData();
            pdr.ReceiveHit(hd);
            Debug.Log("[BulletProjectile] Delivered hit to PlayerDamageReceiver on " + other.gameObject.name, this);
            Destroy(gameObject);
            return;
        }

        var h = other.GetComponentInParent<IHealth>();
        if (h != null)
        {
            try
            {
                h.ApplyDamage(damage);
                Debug.Log("[BulletProjectile] Applied damage via IHealth on " + other.gameObject.name, this);
            }
            catch
            {
                Debug.LogWarning("[BulletProjectile] IHealth.ApplyDamage threw or not supported on " + other.gameObject.name, this);
            }
            Destroy(gameObject);
            return;
        }

        Debug.Log("[BulletProjectile] No damage target found for " + other.gameObject.name, this);
        Destroy(gameObject);
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

    void OnDestroy()
    {
        if (_ignored && _ownerCols != null && _myCols != null)
        {
            foreach (var oc in _ownerCols) if (oc != null)
                foreach (var mc in _myCols) if (mc != null)
                    Physics.IgnoreCollision(oc, mc, false);
        }
    }

    public void Reflect(GameObject newOwner)
    {
        owner = newOwner ?? owner;
        transform.forward = -transform.forward;
        Debug.Log("[BulletProjectile] Reflected. newOwner=" + (owner ? owner.name : "null"), this);
    }
}
