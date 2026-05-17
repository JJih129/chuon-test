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
    public bool grantBreakBonusOnParryCounter = false;

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

        ResolvedHitResult resolvedHit = BossIncomingHitResolver.Resolve(
            payload,
            overrideHitType,
            overrideType,
            consumeParryCounterBonus,
            grantBreakBonusOnParryCounter,
            applyPunishWindowBonus,
            bossController);

        if (bossBreakController == null)
            bossBreakController = GetComponentInParent<BossBreakController>();

        int beforeHp = bossHealth.CurrentHP;
        BossResolvedHitApplier.Apply(
            bossHealth,
            bossBreakController,
            resolvedHit,
            payload,
            bossController != null && (bossController.IsCombatRecoverySuperArmorActive || bossController.IsCombatRecoveryLockoutActive),
            bossController != null &&
            bossController.IsCombatRecoverySuperArmorActive &&
            !bossController.KeepDamageRewardDuringCombatRecoverySuperArmor);

        if (bossController != null && bossHealth.CurrentHP < beforeHp)
            bossController.NotifyPlayerHitBoss(payload.attacker);
    }
}
