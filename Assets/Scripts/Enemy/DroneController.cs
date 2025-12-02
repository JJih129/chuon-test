using UnityEngine;
using DG.Tweening; // 피격 시 색깔 깜빡임용

// ★ IDamageReceiver 필수 (플레이어 공격 인식용)
[DisallowMultipleComponent]
public class DroneController : MonoBehaviour, IDamageReceiver
{
    [Header("▶ 타겟")]
    public Transform target;

    [Header("▶ 이동 파라미터")]
    public float moveSpeed = 3.5f;
    public float stopDistance = 8f;
    public float turnSpeedDeg = 360f;

    [Header("▶ 사격 파라미터")]
    public GameObject projectilePrefab;
    public Transform fireOrigin;
    public float fireDistance = 15f;
    public float fireCooldown = 2.0f;
    public float projectileSpeed = 15f;
    public int projectileDamage = 10;

    [Header("▶ 체력 & 이펙트 설정")]
    public int maxHP = 30;          // 최대 체력
    private int currentHP;
    
    [Tooltip("피격 시 생성될 이펙트 (폭발, 스파크 등)")]
    public GameObject hitVFX;       
    
    [Tooltip("파괴 시 생성될 이펙트 (큰 폭발)")]
    public GameObject deathVFX;

    [Tooltip("피격 시 깜빡일 렌더러 (드론 몸통)")]
    public Renderer droneRenderer; 

    private float nextFireTime;
    private Rigidbody rb;
    private Color originalColor; // 원래 색 저장용

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        currentHP = maxHP;

        // 렌더러 자동 찾기 (없으면 수동 할당 필요)
        if (droneRenderer == null) droneRenderer = GetComponentInChildren<Renderer>();
        if (droneRenderer != null) originalColor = droneRenderer.material.color;

        // 타겟(플레이어) 자동 검색
        if (target == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p) target = p.transform;
        }
    }

    void OnEnable()
    {
        currentHP = maxHP; // 되살아날 때 체력 초기화
    }

    void FixedUpdate()
    {
        if (target == null) return;

        // 1. 타겟 바라보기
        Vector3 direction = (target.position - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.fixedDeltaTime * 5f);
        }

        // 2. 거리 체크 및 이동
        float distance = Vector3.Distance(transform.position, target.position);
        
        if (distance > stopDistance)
        {
            Vector3 movePos = transform.position + direction * moveSpeed * Time.fixedDeltaTime;
            if (rb) rb.MovePosition(movePos);
            else transform.position = movePos;
        }

        // 3. 공격
        if (distance <= fireDistance && Time.time >= nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + fireCooldown;
        }
    }

    void Fire()
    {
        if (projectilePrefab && fireOrigin)
        {
            GameObject bullet = Instantiate(projectilePrefab, fireOrigin.position, fireOrigin.rotation);
            
            // 총알 종류에 따라 초기화 (BulletProjectile 또는 TutorialProjectile)
            var bp = bullet.GetComponent<BulletProjectile>();
            if (bp) bp.Init(this.gameObject, projectileSpeed, projectileDamage);
            
            var tp = bullet.GetComponent<TutorialProjectile>();
            if (tp) tp.owner = this.transform;
        }
    }

    // ======================================================================
    // ★ IDamageReceiver 구현 (플레이어 공격을 받는 부분)
    // ======================================================================
    public void ReceiveHit(HitPayload payload)
    {
        // 1. 데미지 적용
        TakeDamage((int)payload.damage, payload.hitPoint, payload.hitDirection);
    }

    public void TakeDamage(int amount, Vector3 hitPoint, Vector3 hitDir)
    {
        if (currentHP <= 0) return; // 이미 죽었으면 무시

        currentHP -= amount;
        Debug.Log($"[드론] 피격! 남은 체력: {currentHP}/{maxHP}");

        // 2. 히트 이펙트 생성 (타격 지점에)
        if (hitVFX != null)
        {
            // 타격 방향 반대로 이펙트가 튀게 회전 설정
            Quaternion rot = Quaternion.LookRotation(-hitDir); 
            GameObject vfx = Instantiate(hitVFX, hitPoint, rot);
            Destroy(vfx, 1.0f); // 1초 뒤 삭제
        }

        // 3. 피격 반응 (빨간색 깜빡임)
        if (droneRenderer != null)
        {
            // 기존 트윈 멈추고 새로 시작 (연속 피격 시 꼬임 방지)
            droneRenderer.material.DOKill(); 
            droneRenderer.material.color = Color.red;
            droneRenderer.material.DOColor(originalColor, 0.2f);
        }

        // 4. 넉백 (밀려남) 효과
        transform.DOPunchPosition(hitDir.normalized * 0.5f, 0.2f);

        // 5. 사망 체크
        if (currentHP <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log("[드론] 파괴됨!");

        // 파괴 이펙트 생성
        if (deathVFX != null)
        {
            GameObject vfx = Instantiate(deathVFX, transform.position, Quaternion.identity);
            Destroy(vfx, 2.0f);
        }

        // 중요: LobbyEnemy가 사망을 감지할 수 있도록 오브젝트 파괴
        // (LobbyEnemy의 OnDisable이나 OnDestroy가 호출됨)
        Destroy(gameObject);
    }
}