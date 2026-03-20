using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    [Header("이동할 씬 이름")]
    public string nextSceneName = "BootScene";

    public void OnStartButtonClick()
    {
        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeOutAndLoadScene(nextSceneName);
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    public void OnExitButtonClick()
    {
        Debug.Log("게임 종료");
        Application.Quit();
    }
}
