using UnityEngine;

public class LobbyTrigger : MonoBehaviour
{
    // 인스펙터에서 설정: Approach(문앞), Board(안쪽)
    public enum TriggerType { Approach, Board }
    public TriggerType type;

    // 중복 작동 방지
    private bool isTriggered = false;

    void OnTriggerEnter(Collider other)
    {
        // 1. 무엇이 닿았는지 확인 (로그 출력)
        // Debug.Log($"[LobbyTrigger] 무언가 닿음: {other.name} (태그: {other.tag})");

        if (isTriggered) return;

        // 2. 플레이어 태그 확인
        if (other.CompareTag("Player"))
        {
            Debug.Log($"[LobbyTrigger] 플레이어 감지 성공! 타입: {type}");

            if (LobbyManager.Instance != null)
            {
                isTriggered = true; // 한 번만 작동하게 잠금

                if (type == TriggerType.Approach)
                {
                    Debug.Log(">> 매니저에게 '문 앞 도착' 신호 보냄");
                    LobbyManager.Instance.OnReachElevator();
                }
                else if (type == TriggerType.Board)
                {
                    Debug.Log(">> 매니저에게 '탑승 완료' 신호 보냄");
                    LobbyManager.Instance.OnEnterElevator();
                }
            }
            else
            {
                Debug.LogError("LobbyManager가 씬에 없습니다!");
            }
        }
    }
}