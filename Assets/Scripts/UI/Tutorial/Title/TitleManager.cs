using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    [Header("이동할 씬 이름")]
    public string nextSceneName = "BootScene";

    TitleSceneUIController _sceneController;

    void Awake()
    {
        _sceneController = FindObjectOfType<TitleSceneUIController>(true);
    }

    void OnEnable()
    {
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();
    }

    public void OnStartButtonClick()
    {
        if (_sceneController != null)
        {
            _sceneController.OnClick_StartGame();
            return;
        }

        RuntimeMenuSceneStateUtility.PrepareForGameplayScene();
        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeOutAndLoadScene(nextSceneName);
            return;
        }

        SceneManager.LoadScene(nextSceneName);
    }

    public void OnOptionsPressed()
    {
        EnsureController();
        if (_sceneController != null)
            _sceneController.OnClick_OpenSettings();
    }

    public void OnCreditsPressed()
    {
        EnsureController();
        if (_sceneController != null)
            _sceneController.OnClick_OpenCredits();
    }

    public void OnQuitPressed()
    {
        EnsureController();
        if (_sceneController != null)
        {
            _sceneController.OnClick_QuitGame();
            return;
        }

        Debug.Log("게임 종료");
        Application.Quit();
    }

    public void OnExitButtonClick()
    {
        OnQuitPressed();
    }

    void EnsureController()
    {
        if (_sceneController == null)
            _sceneController = FindObjectOfType<TitleSceneUIController>(true);
    }
}
