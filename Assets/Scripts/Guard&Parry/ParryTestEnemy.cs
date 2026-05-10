using UnityEngine;

[DisallowMultipleComponent]
public class ParryTestEnemy : MonoBehaviour
{
    // 공격 주기(초)
    [Header("▶ 공격 주기(초)")]
    public float attackInterval = 2.0f;

    // 공격 데미지
    [Header("▶ 공격 데미지")]
    public float attackDamage = 10f;

    // 패링 가능 여부
    [Header("▶ 패링 가능 여부")]
    public bool attackIsParryable = true;

    // 플레이어 타겟
    [Header("▶ 플레이어 타겟")]
    public Transform target;

    // 플레이어 데미지 수신기
    [Header("▶ 플레이어 데미지 수신기")]
    public PlayerDamageReceiver damageReceiver;

    // 다음 공격 시간을 추적
    private float _nextAttackTime;

    void Start()
    {
        if (!target)
            target = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (!damageReceiver && target)
            damageReceiver = target.GetComponent<PlayerDamageReceiver>();

        _nextAttackTime = Time.time + attackInterval;
    }

    void Update()
    {
        if (!target)
            target = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (!damageReceiver && target)
            damageReceiver = target.GetComponent<PlayerDamageReceiver>();

        if (Time.time >= _nextAttackTime)
        {
            PerformAttack();
            _nextAttackTime = Time.time + attackInterval;
        }
    }

    void PerformAttack()
    {
        if (!target || !damageReceiver) return;

        damageReceiver.ReceiveHit(
            attackDamage,
            transform,
            transform.position + transform.forward,
            unblockable: false,
            allowPerfectDodge: true,
            allowParry: attackIsParryable);
    }
}

