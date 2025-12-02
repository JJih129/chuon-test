using UnityEngine;
using UnityEngine.SceneManagement; // 씬 이동 필수

// BaseInteractable을 상속받아 상호작용 기능 구현
public class SceneLoadInteractable : BaseInteractable
{
    [Header("■ 이동할 씬 설정")]
    [Tooltip("빌드 세팅(Build Settings)에 등록된 씬 이름이어야 합니다.")]
    public string sceneName = "Lobby";

    [Header("■ 옵션")]
    [Tooltip("상호작용 시 로그를 띄울까요?")]
    public bool showLog = true;

    // 상호작용 키(F)를 눌렀을 때 실행되는 함수
    public override bool TryInteract(object invoker = null)
    {
        if (showLog)
        {
            Debug.Log($"[SceneLoad] '{sceneName}' 씬으로 이동합니다.");
        }

        // 씬 이름이 비어있지 않은지 확인
        if (!string.IsNullOrEmpty(sceneName))
        {
            // 씬 로드
            SceneManager.LoadScene(sceneName);
            return true;
        }
        else
        {
            Debug.LogError("[SceneLoad] 이동할 씬 이름이 설정되지 않았습니다!");
            return false;
        }
    }
}