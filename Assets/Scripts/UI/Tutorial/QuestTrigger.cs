using UnityEngine;

public class QuestTrigger : MonoBehaviour
{
    private bool isTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered) return;

        // 플레이어 태그 확인
        if (other.CompareTag("Player"))
        {
            isTriggered = true;
            Debug.Log("목표 지점 도착!");

            // 튜토리얼 매니저에게 알림
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.OnGoalReached();
            }
        }
    }
}