using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    [Header("이동할 씬 이름")]
    public string nextSceneName = "LobbyScene"; // 혹은 "TutorialScene"

    // 시작 버튼에 연결할 함수
    public void OnStartButtonClick()
    {
        // 씬 페이더가 있으면 페이드 효과와 함께 이동
        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeOutAndLoadScene(nextSceneName);
        }
        else
        {
            // 없으면 그냥 이동 (비상용)
            SceneManager.LoadScene(nextSceneName);
        }
    }
    
    // (선택) 종료 버튼
    public void OnExitButtonClick()
    {
        Debug.Log("게임 종료");
        Application.Quit();
    }
}