// Assets/Scripts/Boss/BossDamageReceiver.cs
using UnityEngine;

/// <summary>
/// 보스가 플레이어 공격(무기 AttackHitbox 등)을 맞을 때
/// 공통 인터페이스(IDamageReceiver)를 통해 데미지를 받는 컴포넌트
/// </summary>
[DisallowMultipleComponent]
public class BossDamageReceiver : MonoBehaviour, IDamageReceiver
{
    [Header("참조")]
    public BossHealth bossHealth;
    public Transform bossRoot;

    [Header("히트 타입 설정")]
    [Tooltip("히트 정보에서 HitType 을 무시하고 이 값으로 강제할지 여부")]
    public bool overrideHitType = false;

    [Tooltip("overrideHitType 이 true 일 때 사용할 기본 타입")]
    public HitType overrideType = HitType.Normal;

    void Awake()
    {
        if (bossHealth == null)
            bossHealth = GetComponentInParent<BossHealth>();

        if (bossRoot == null && bossHealth != null)
            bossRoot = bossHealth.transform;
    }

    public void ReceiveHit(HitPayload payload)
    {
        if (bossHealth == null)
            return;

        HitType type = overrideHitType ? overrideType : payload.hitType;
        int beforeHp = bossHealth.CurrentHP;

        // IHealth 구현 기반 BossHealth.TakeDamage(...)
        bossHealth.TakeDamage(
            Mathf.RoundToInt(payload.damage),
            type,
            payload.hitPoint
        );

        if (bossHealth.CurrentHP < beforeHp)
            TryGrantBasicAttackGauge(payload.attacker);
    }

    void TryGrantBasicAttackGauge(Transform attacker)
    {
        if (attacker == null)
            return;

        var ultimate = attacker.GetComponent<PlayerUltimateController>();
        if (ultimate == null)
            ultimate = attacker.GetComponentInParent<PlayerUltimateController>();

        if (ultimate != null && ultimate.gaugePerH > 0f)
            ultimate.AddGauge(ultimate.gaugePerH);
    }
}
