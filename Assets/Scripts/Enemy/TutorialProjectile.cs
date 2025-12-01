using UnityEngine;

public class TutorialProjectile : MonoBehaviour
{
    [Header("설정")]
    public float speed = 10.0f;     // 총알 속도
    public float damage = 10.0f;    // 데미지
    public float lifeTime = 5.0f;   // 수명 (초)

    [Header("타격 설정")]
    // 패링 가능 여부 (튜토리얼이니까 true)
    public bool canBeParried = true; 

    // 발사한 주인 (드론) - 패링 시 방향 판정을 위해 필요
    [HideInInspector] public Transform owner; 

    void Start()
    {
        // 일정 시간 지나면 자동 삭제
        Destroy(gameObject, lifeTime);
    }

    void Update()
    {
        // 앞으로 이동
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        // 플레이어인지 확인
        if (other.CompareTag("Player"))
        {
            // 플레이어의 데미지 처리 컴포넌트 찾기
            var receiver = other.GetComponent<IDamageReceiver>();
            if (receiver != null)
            {
                // 공격 정보 생성
                HitPayload payload = new HitPayload();
                payload.damage = damage;
                payload.hitPoint = transform.position;
                payload.hitDirection = transform.forward;
                payload.attacker = owner; // 공격자(드론)를 알려줘야 패링 방향 계산 가능
                payload.hitType = HitType.Normal; // 일반 공격

                // 데미지 전달
                receiver.ReceiveHit(payload);
            }

            // 총알 삭제 (맞았으니까)
            Destroy(gameObject);
        }
        else if (!other.CompareTag("Enemy") && !other.isTrigger) 
        {
            // 벽이나 바닥에 닿아도 삭제 (드론 자신은 제외)
            Destroy(gameObject);
        }
    }
}