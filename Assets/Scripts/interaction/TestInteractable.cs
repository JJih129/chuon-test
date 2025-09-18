using UnityEngine;

// [헤더] 테스트용 구체 상호작용 컴포넌트
// - BaseInteractable을 상속하여 TryInteract 구현
public class TestInteractable : BaseInteractable
{
    [Header("테스트 메시지")]
    public string message = "Interacted";

    public override bool TryInteract(object invoker = null)
    {
        Debug.Log($"{name}: {message}");
        return true;
    }
}
