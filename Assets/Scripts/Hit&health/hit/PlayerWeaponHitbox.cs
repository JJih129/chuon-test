// PlayerWeaponHitbox.cs (새 파일)
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PlayerWeaponHitbox : MonoBehaviour
{
    public int damage = 12;
    public GameObject owner; // Player 루트
    Collider col;

    void Awake()
    {
        col = GetComponent<Collider>();
        col.isTrigger = true;
        if (!owner) owner = transform.root.gameObject;
        gameObject.SetActive(false); // 기본 꺼두고 애니 이벤트로만 켜기
    }

    void OnTriggerEnter(Collider other)
    {
        var recv = other.GetComponentInParent<IHitReceiver>();
        if (recv != null)
        {
            var hit = new HitData{
                damage = damage,
                hitPoint = other.ClosestPoint(transform.position),
                hitDirection = (other.transform.position - owner.transform.position).normalized,
                attacker = owner
            };
            recv.ReceiveHit(hit);
        }
    }
}