using UnityEngine;

// 튜토리얼 궁극기 대상 어댑터: 잡몹(EnemyBrain + EnemyHealth)을 궁극기 타깃(IUltimateTarget)으로 만든다.
// 궁극기 확정 피해가 들어오면 그대로 처치한다 (시뮬레이션 레슨 1회용). AI/잡몹 코드는 건드리지 않는다.
[DisallowMultipleComponent]
public sealed class InfMobUltimateTarget : MonoBehaviour, IUltimateTarget
{
    public int DamageReceived { get; private set; }

    public void ApplyUltimateDamage(int fixedDamage)
    {
        DamageReceived += Mathf.Max(0, fixedDamage);
        EnemyHealth health = GetComponentInChildren<EnemyHealth>();
        if (health != null && !health.IsDead)
            health.ApplyDamage(Mathf.Max(fixedDamage, health.CurrentHP + 1));
    }
}
