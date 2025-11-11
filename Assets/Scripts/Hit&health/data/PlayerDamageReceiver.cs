using UnityEngine;

/// 대미지 입력 → 가드 판정 → HP 반영
[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour
{
    [Header("참조")]
    public PlayerHealth health;                 // 자동 탐색
    public PlayerGuardController guardCtrl;     // 자동 탐색
    public Animator animator;                   // 선택

    [Header("디버그")]
    public bool verbose = true;

    void Awake()
    {
        if (!health)    health    = GetComponent<PlayerHealth>() ?? GetComponentInParent<PlayerHealth>();
        if (!guardCtrl) guardCtrl = GetComponent<PlayerGuardController>() ?? GetComponentInParent<PlayerGuardController>();
        if (!animator)  animator  = GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>();
    }

    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool projectile)
    {
        Apply(baseDamage, attacker, hitPoint, projectile);
    }

    public void Apply(float baseDamage, Transform attacker, Vector3 hitPoint, bool projectile)
    {
        if (!health)
        {
            if (verbose) Debug.LogWarning("[PDR] PlayerHealth=null", this);
            return;
        }

        bool parry = false, block = false;
        float chipMul = 0f;
        bool defended = false;

        if (guardCtrl)
            defended = guardCtrl.EvaluateDefense(hitPoint, out parry, out block, out chipMul);

        if (defended && parry)
        {
            if (verbose) Debug.Log($"[PDR] base={baseDamage} → parry success | action=NoDamage", this);
            guardCtrl?.PlayParrySuccess();
            return;
        }

        if (defended && block)
        {
            int chip = Mathf.CeilToInt(baseDamage * Mathf.Clamp01(chipMul));
            if (verbose) Debug.Log($"[PDR] base={baseDamage} → chip={chip} | block=True | action=ApplyChip", this);
            if (chip > 0) health.ApplyChipDamage(chip);   // ← 히트 리액션 없음
            guardCtrl?.PlayBlockReaction();
            return;
        }

        if (verbose) Debug.Log($"[PDR] base={baseDamage} → final={baseDamage} | block=False parry=False | action=ApplyDamage", this);
        health.ApplyDamage(Mathf.RoundToInt(baseDamage)); // ← 일반 히트만 리액션
    }
}
