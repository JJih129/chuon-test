using UnityEngine;

public class BranchTrigger : MonoBehaviour
{
    private bool isTriggered = false;

    void OnTriggerEnter(Collider other)
    {
        if (isTriggered) return;

        if (other.CompareTag("Player"))
        {
            isTriggered = true;
            
            // 매니저에게 "갈림길 선택 완료" 신호 보냄
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnBranchSelected();
            }

            // (선택 사항) 밟은 트리거는 끄기
            gameObject.SetActive(false);
        }
    }
}