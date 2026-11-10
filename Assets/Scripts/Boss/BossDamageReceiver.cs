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

    /// <summary>반복 16(Type-D 개편): 해석된 피해를 바꾼다 (실명 측면 ×1.5, 과부하 ×1.3 등). null = 그대로.</summary>
    public static System.Func<BossDamageReceiver, HitPayload, ResolvedHitResult, ResolvedHitResult> HitFilter;
    /// <summary>반복 16: 적용 직후 (깎인 HP). 충격 누적·부위 파괴·균형 배율이 구독한다.</summary>
    public static event System.Action<BossDamageReceiver, HitPayload, ResolvedHitResult, int> HitApplied;

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

        if (HitFilter != null)
            resolvedHit = HitFilter(this, payload, resolvedHit);

        int hpBefore = bossHealth.CurrentHP;
        BossResolvedHitApplier.Apply(
            bossHealth,
            bossBreakController,
            resolvedHit,
            payload);

        HitApplied?.Invoke(this, payload, resolvedHit, hpBefore - bossHealth.CurrentHP);
    }
}
