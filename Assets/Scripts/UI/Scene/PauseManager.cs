using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance;

    [Header("UI View")]
    public PauseMenuView uiView;

    [Header("Settings")]
    public string titleSceneName = "TitleScene";

    bool isPaused;
    bool isSettingsOpen;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
    }

    void Start()
    {
        ResumeGame();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        if (isSettingsOpen)
            CloseSettings();
        else
            TogglePause();
    }

    public void TogglePause()
    {
        if (isPaused)
            ResumeGame();
        else
            PauseGame();
    }

    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
        SetSupplementalOverlayVisibility(false);

        if (uiView != null)
            uiView.ShowMenu();

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void ResumeGame()
    {
        isPaused = false;

        if (uiView == null)
        {
            RuntimeMenuSceneStateUtility.PrepareForGameplayScene();
            SetSupplementalOverlayVisibility(true);
            return;
        }

        uiView.HideMenu(() =>
        {
            RuntimeMenuSceneStateUtility.PrepareForGameplayScene();
            SetSupplementalOverlayVisibility(true);
        });
    }

    public void OnClick_Resume()
    {
        ResumeGame();
    }

    public void OnClick_Restart()
    {
        RuntimeMenuSceneStateUtility.PrepareForGameplayScene();

        if (SceneFader.Instance != null)
            SceneFader.Instance.FadeOutAndLoadScene(SceneManager.GetActiveScene().name);
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnClick_Settings()
    {
        isSettingsOpen = true;
        if (uiView != null)
            uiView.ToggleSettings(true);
    }

    public void CloseSettings()
    {
        isSettingsOpen = false;
        if (uiView != null)
            uiView.ToggleSettings(false);
    }

    public void OnClick_ToTitle()
    {
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();

        if (SceneFader.Instance != null)
            SceneFader.Instance.FadeOutAndLoadScene(titleSceneName);
        else
            SceneManager.LoadScene(titleSceneName);
    }

    public void OnClick_Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void SetSupplementalOverlayVisibility(bool visible)
    {
        TutorialHintUIBridge hintBridge = FindObjectOfType<TutorialHintUIBridge>(true);
        if (hintBridge != null)
            hintBridge.SetOverlayVisible(visible);

        SetOverlayRootActive("TutorialPresentationRuntime", visible);
        SetOverlayRootActive("TutorialDefenseFeedbackRuntime", visible);
        SetOverlayRootActive("TutorialSupportFeedbackRuntime", visible);
    }

    static void SetOverlayRootActive(string objectName, bool active)
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null || current.name != objectName)
                continue;

            current.gameObject.SetActive(active);
        }
    }
}
