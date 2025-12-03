using UnityEngine;

public class ElevatorPanel : BaseInteractable
{
    public override bool TryInteract(object invoker = null)
    {
        if (LobbyManager.Instance != null)
        {
            // 상호작용 성공 -> 매니저가 씬 이동 처리
            LobbyManager.Instance.OnInteractElevator();
            return true;
        }
        return false;
    }
}