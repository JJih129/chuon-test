using UnityEngine;

/// <summary>
/// AnimationEvent에서 호출될 수 있는 간단 receiver.
/// 애니메이션 이벤트에서 ParryWindow_Pulse 라는 이벤트를 넣어두면 이 컴포넌트가 PlayerGuardController.OpenParryWindow 를 호출.
/// 반드시 이 컴포넌트를 같은 루트(플레이어) 혹은 Animator가 연결된 오브젝트에 붙이고 AnimationEvent에 함수명 연결.
/// </summary>
public class AnimationEventReceiver : MonoBehaviour
{
    // ===== 변수 헤더(설명) =====
    [Tooltip("같은 오브젝트(또는 부모)에 있는 PlayerGuardController 참조. 비어있으면 자동 검색합니다.")]
    public PlayerGuardController guard;

    void Awake()
    {
        if (guard == null) guard = GetComponentInParent<PlayerGuardController>();
    }

    // 애니메이션 이벤트에서 호출
    public void ParryWindow_Pulse()
    {
        if (guard != null) guard.OpenParryWindow();
    }
}
