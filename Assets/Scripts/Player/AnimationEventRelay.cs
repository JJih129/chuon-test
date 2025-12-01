using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    [Header("연결할 부모 컴포넌트")]
    [Tooltip("PlayerCombatController가 붙은 부모 오브젝트를 연결하세요.")]
    public PlayerCombatController combatController;

    // 1. 애니메이션 이벤트에서 호출할 함수 (공격 시작)
    public void EnableAttackHitbox()
    {
        if (combatController != null)
        {
            combatController.EnableAttackHitbox();
        }
    }

    // 2. 애니메이션 이벤트에서 호출할 함수 (공격 끝)
    public void DisableAttackHitbox()
    {
        if (combatController != null)
        {
            combatController.DisableAttackHitbox();
        }
    }
    
    // (자동 연결 시도)
    private void Awake()
    {
        if (combatController == null)
        {
            combatController = GetComponentInParent<PlayerCombatController>();
        }
    }
}