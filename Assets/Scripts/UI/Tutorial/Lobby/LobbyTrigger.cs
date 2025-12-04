using UnityEngine;

public class LobbyTrigger : MonoBehaviour
{
    public enum TriggerType { Approach, Board }
    public TriggerType type;

    private bool isTriggered = false;

    // 들어갈 때 감지
    void OnTriggerEnter(Collider other)
    {
        CheckTrigger(other);
    }

    // ★ [추가됨] 이미 들어가서 서 있을 때도 감지 (이게 있어야 미리 가도 작동함)
    void OnTriggerStay(Collider other)
    {
        CheckTrigger(other);
    }

    void CheckTrigger(Collider other)
    {
        // 이미 완료된 상태면 무시 (매니저가 변수를 초기화하면 다시 작동함)
        if (isTriggered) 
        {
            // 매니저 상태를 확인해서, 매니저가 '아직 안 밟았다(false)'고 생각하면 다시 신호 보냄
            if (LobbyManager.Instance != null)
            {
                if (type == TriggerType.Board && !LobbyManager.Instance.IsBoarded) 
                    isTriggered = false; // 다시 작동하도록 리셋
                else if (type == TriggerType.Approach && !LobbyManager.Instance.IsDoorReached)
                    isTriggered = false;
            }
            
            if(isTriggered) return; 
        }

        if (other.CompareTag("Player"))
        {
            Debug.Log($"[LobbyTrigger] 플레이어 감지됨 (Stay/Enter)! 타입: {type}");

            if (LobbyManager.Instance != null)
            {
                if (type == TriggerType.Approach)
                {
                    LobbyManager.Instance.OnReachElevator();
                    isTriggered = true;
                }
                else if (type == TriggerType.Board)
                {
                    LobbyManager.Instance.OnEnterElevator();
                    isTriggered = true;
                }
            }
        }
    }
}