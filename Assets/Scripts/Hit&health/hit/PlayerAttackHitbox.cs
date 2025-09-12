using UnityEngine;

public class PlayerAttackHitbox : MonoBehaviour
{
    public int attackDamage = 20;
    public GameObject owner; // 공격자(플레이어 오브젝트)

    private Collider hitboxCollider;

    void Awake()
    {
        // 이 오브젝트에 붙은 콜라이더를 찾아놓음
        hitboxCollider = GetComponent<Collider>();
    }

    // === 히트박스 켜기 (애니메이션 이벤트에서 호출) ===
    public void EnableHitbox()
    {
        if (hitboxCollider != null)
            hitboxCollider.enabled = true;
    }

    // === 히트박스 끄기 (애니메이션 이벤트에서 호출) ===
    public void DisableHitbox()
    {
        if (hitboxCollider != null)
            hitboxCollider.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 공격자 자신은 무시
        if (other.gameObject == owner) return;

        // 피격 인터페이스 가진 대상만 타격
        IHitReceiver receiver = other.GetComponent<IHitReceiver>();
        if (receiver != null)
        {
            // 공격 정보 패키징
            HitData hit = new HitData
            {
                damage = attackDamage,
                hitPoint = other.ClosestPoint(transform.position),
                hitDirection = (other.transform.position - owner.transform.position).normalized,
                attacker = owner
            };
            receiver.ReceiveHit(hit);
        }
    }
}
