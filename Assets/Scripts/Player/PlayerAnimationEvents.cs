// Assets/Scripts/Player/PlayerAnimationEvents.cs
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerAnimationEvents : MonoBehaviour
{
    [Header("공격 히트박스들 (플레이어 무기)")]
    [SerializeField] private AttackHitbox[] attackHitboxes;

    // 안전하게 인덱스 체크
    private AttackHitbox GetHitbox(int index)
    {
        if (attackHitboxes == null || attackHitboxes.Length == 0) return null;
        if (index < 0 || index >= attackHitboxes.Length) return null;
        return attackHitboxes[index];
    }

    // 애니메이션 이벤트: 무기 충돌 시작 프레임
    public void ActivateHitbox(int index)
    {
        var hitbox = GetHitbox(index);
        if (hitbox == null) return;

        hitbox.ActivateWindow();
    }

    // 애니메이션 이벤트: 무기 충돌 종료 프레임
    public void DeactivateHitbox(int index)
    {
        var hitbox = GetHitbox(index);
        if (hitbox == null) return;

        hitbox.DeactivateWindow();
    }

    // 한 개만 쓰는 경우 편의용 (파라미터 없는 이벤트)
    public void ActivateHitbox()
    {
        ActivateHitbox(0);
    }

    public void DeactivateHitbox()
    {
        DeactivateHitbox(0);
    }
}