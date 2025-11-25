using UnityEngine;

// 파일명: BossAnimationEvents.cs
public class BossAnimationEvents : MonoBehaviour
{
    private BossController bossController;

    void Awake()
    {
        // var 대신 형을 명시합니다. (사용자 요청 사항)
        BossController controller = GetComponentInParent<BossController>();
        if (controller != null)
        {
            bossController = controller;
        }
        else
        {
            Debug.LogError("BossAnimationEvents: 상위/현재 오브젝트에서 BossController 컴포넌트를 찾을 수 없습니다.");
            enabled = false;
        }
    }

    // === 애니메이션 이벤트 함수 (Unity Animator에서 호출) ===

    public void SignalPatternEnd()
    {
        if (bossController != null)
        {
            bossController.OnAnimationPatternEnd();
        }
    }

    /// <summary>
    /// 애니메이션 클립의 공격 판정 시작 시점에 추가됩니다.
    /// </summary>
    public void ActivateHitbox()
    {
        if (bossController != null)
        {
            bossController.ActivateHitbox();
        }
    }

    /// <summary>
    /// 애니메이션 클립의 공격 판정 종료 시점에 추가됩니다.
    /// </summary>
    public void DeactivateHitbox()
    {
        if (bossController != null)
        {
            bossController.DeactivateHitbox();
        }
    }
}