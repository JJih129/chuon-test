using UnityEngine;

[DisallowMultipleComponent]
public class BossDamageReceiver : MonoBehaviour, IDamageReceiver
{
    [Header("References")]
    public BossHealth bossHealth;
    public Transform bossRoot;
    public BossBreakController bossBreakController;
    public BossController bossController;

    [Header("Hit Settings")]
    [Tooltip("Ignore payload hit type and force the override type instead.")]
    public bool overrideHitType = false;

    [Tooltip("Used when overrideHitType is enabled.")]
    public HitType overrideType = HitType.Normal;

    [Header("Parry Counter")]
    [Tooltip("Consume the parry counter window on the first valid hit.")]
    public bool consumeParryCounterBonus = true;
    [Tooltip("Grant extra break when the counter hit lands.")]
    public bool grantBreakBonusOnParryCounter = true;

    [Header("Punish Window")]
    [Tooltip("보스 공격 후 열린 빈틈 시간 동안 추가 피해 배수를 적용.")]
    public bool applyPunishWindowBonus = true;

    void Awake()
    {
        if (bossHealth == null)
            bossHealth = GetComponentInParent<BossHealth>();

        if (bossRoot == null && bossHealth != null)
            bossRoot = bossHealth.transform;

        if (bossBreakController == null)
            bossBreakController = GetComponentInParent<BossBreakController>();

        if (bossController == null)
            bossController = GetComponentInParent<BossController>();
    }

    public void ReceiveHit(HitPayload payload)
    {
        if (bossHealth == null)
            return;

        HitType type = overrideHitType ? overrideType : payload.hitType;
        float damage = payload.damage;
        ApplyParryCounterBonus(payload.attacker, ref damage, ref type);
        ApplyPunishWindowBonus(ref damage);

        int beforeHp = bossHealth.CurrentHP;
        bossHealth.TakeDamage(
            Mathf.RoundToInt(damage),
            type,
            payload.hitPoint);

        if (bossHealth.CurrentHP < beforeHp)
            TryGrantBasicAttackGauge(payload.attacker);
    }

    void ApplyParryCounterBonus(Transform attacker, ref float damage, ref HitType type)
    {
        if (!consumeParryCounterBonus || attacker == null)
            return;

        PlayerGuardController guard = attacker.GetComponent<PlayerGuardController>();
        if (guard == null)
            guard = attacker.GetComponentInParent<PlayerGuardController>();

        if (guard == null)
            return;

        if (!guard.TryConsumeParryCounter(out float damageMultiplier, out HitType counterHitType, out float breakBonus))
            return;

        damage *= damageMultiplier;

        if (!overrideHitType)
            type = counterHitType;

        if (!grantBreakBonusOnParryCounter || breakBonus <= 0f)
            return;

        if (bossBreakController == null)
            bossBreakController = GetComponentInParent<BossBreakController>();

        if (bossBreakController != null)
            bossBreakController.AddBreak(breakBonus, BossBreakController.BreakSource.Parry);
    }

    void ApplyPunishWindowBonus(ref float damage)
    {
        if (!applyPunishWindowBonus)
            return;

        if (bossController == null)
            bossController = GetComponentInParent<BossController>();

        if (bossController == null || !bossController.IsPunishWindowActive)
            return;

        damage *= bossController.CurrentPunishDamageMultiplier;
    }

    void TryGrantBasicAttackGauge(Transform attacker)
    {
        if (attacker == null)
            return;

        PlayerUltimateController ultimate = attacker.GetComponent<PlayerUltimateController>();
        if (ultimate == null)
            ultimate = attacker.GetComponentInParent<PlayerUltimateController>();

        if (ultimate != null && ultimate.gaugePerH > 0f)
            ultimate.AddGauge(ultimate.gaugePerH);
    }
}
