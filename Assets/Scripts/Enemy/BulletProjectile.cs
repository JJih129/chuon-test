// BulletProjectile.cs
// 설명: 발사자 충돌 무시, 충돌 시 IHitReceiver 우선 호출, 다음으로 PlayerDamageReceiver 직접 호출, 그 다음 IHealth 호출.
// 변수 헤더 한글 설명 포함.
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

        // 소유자와의 충돌 무시
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform))) return;

        Debug.Log($"[BulletProjectile] Collided with {other.gameObject.name}", this);

        // 1) IHitReceiver 직접 탐색 및 호출 (부모쪽 우선)
        var hitReceiver = other.GetComponentInParent<IHitReceiver>();
        if (hitReceiver != null)
        {
            var hd = CreateHitData();
            hitReceiver.ReceiveHit(hd);
            Debug.Log("[BulletProjectile] Delivered hit to IHitReceiver on " + other.gameObject.name, this);
            Destroy(gameObject);
            return;
        }

        // 2) PlayerDamageReceiver 같은 구체 타입 직접 검색 (프로젝트에 PlayerDamageReceiver가 있을 경우)
        var pdr = other.GetComponentInParent<PlayerDamageReceiver>();
        if (pdr != null)
        {
            var hd = CreateHitData();
            // PlayerDamageReceiver.ReceiveHit expects HitData
            pdr.ReceiveHit(hd);
            Debug.Log("[BulletProjectile] Delivered hit to PlayerDamageReceiver on " + other.gameObject.name, this);
            Destroy(gameObject);
            return;
        }

        // 3) IHealth fallback (직접 ApplyDamage)
        var h = other.GetComponentInParent<IHealth>();
        if (h != null)
        {
            // IHealth may have ApplyDamage(int) or ApplyDamage(float)
            try
            {
                h.ApplyDamage(damage);
                Debug.Log("[BulletProjectile] Applied damage via IHealth on " + other.gameObject.name, this);
            }
            catch
            {
                // best-effort
                Debug.LogWarning("[BulletProjectile] IHealth.ApplyDamage threw or not supported on " + other.gameObject.name, this);
            }
            Destroy(gameObject);
            return;
        }

        // 4) 아무 대상도 아님: 그냥 파괴
        Debug.Log("[BulletProjectile] No damage target found for " + other.gameObject.name, this);
        Destroy(gameObject);
    }

    HitData CreateHitData()
    {
        // 안전하게 HitData를 구성. 기존 프로젝트 HitData 구조에 맞춰 필요한 필드만 채움.
        var hd = new HitData();
        // common expected fields: attacker (GameObject), damage (float/int), hitPoint (Vector3), hitType, isParryable
        try
        {
            // best-effort assignments; if HitData uses different names these will fail at compile time.
            hd.attacker = owner;
            hd.damage = damage;
            hd.hitPoint = transform.position;
            hd.hitType = HitType.Normal;
            hd.isParryable = false;
        }
        catch
        {
            // if HitData's fields differ, fallback to what exists (silently).
        }
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
}
