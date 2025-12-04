using UnityEngine;

public class WaypointTrigger : MonoBehaviour
{
    private bool isReached = false;

    void OnTriggerEnter(Collider other)
    {
        if (isReached) return;

        if (other.CompareTag("Player"))
        {
            isReached = true;
            
            // 매니저에게 "나 도착했어!" 라고 알림
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnWaypointReached(this.transform);
            }

            // 자기 자신은 끄기 (사라짐)
            gameObject.SetActive(false);
        }
    }
}