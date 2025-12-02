using UnityEngine;

public class LobbyEnemy : MonoBehaviour
{
    // 드론이 Destroy되거나 꺼질 때 호출됨
    void OnDisable()
    {
        // 게임이 종료되는 중이 아닐 때만 체크
        if (gameObject.scene.isLoaded && LobbyManager.Instance != null)
        {
            // 체력이 다 해서 죽은 건지 확인하려면 DroneController의 HP를 체크해도 됨
            // 여기서는 간단하게 "사라지면 죽은 것"으로 처리
            LobbyManager.Instance.OnEnemyKilled();
        }
    }
}