using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance;

    [Header("UI View")]
    public PauseMenuView uiView;

    [Header("Settings")]
    public string titleSceneName = "TitleScene";
    [SerializeField] bool useRuntimePauseOverlay = true;
    [SerializeField] bool debugLog = true;

    bool isPaused;
    bool isSettingsOpen;
    RuntimePauseMenuOverlay runtimeOverlay;

    public bool IsPaused => isPaused;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
    }

    void Start()
    {
        isPaused = false;
        isSettingsOpen = false;

        if (uiView != null)
            uiView.HideMenuImmediate();

        if (useRuntimePauseOverlay)
            EnsureRuntimeOverlay().Hide();

        RuntimeMenuSceneStateUtility.PrepareForGameplayScene();
        SetSupplementalOverlayVisibility(true);
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

    void LateUpdate()
    {
        if (!isPaused && !isSettingsOpen)
            return;

        RuntimeUiInputUtility.EnsureEventSystem();
        RuntimeUiInputUtility.ForceMenuCursor();
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
        RuntimeUiInputUtility.EnsureEventSystem();

        if (useRuntimePauseOverlay)
        {
            if (uiView != null)
                uiView.HideMenuImmediate();
            EnsureRuntimeOverlay().Show(this);
        }
        else if (uiView != null)
        {
            uiView.ShowMenu();
        }

        RuntimeUiInputUtility.ForceMenuCursor();

        if (debugLog)
            Debug.Log($"[PauseUI] PauseGame uiView={(uiView != null)} timeScale={Time.timeScale} cursor={Cursor.visible}/{Cursor.lockState}");
    }

    public void ResumeGame()
    {
        isPaused = false;
        RuntimeUiInputUtility.RestoreModalInput();

        if (runtimeOverlay != null)
            runtimeOverlay.Hide();

        if (useRuntimePauseOverlay || uiView == null)
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
        if (debugLog)
            Debug.Log("[PauseUI] Click Resume");

        ResumeGame();
    }

    public void OnClick_Restart()
    {
        if (debugLog)
            Debug.Log("[PauseUI] Click Restart");

        RuntimeUiInputUtility.RestoreModalInput();
        RuntimeMenuSceneStateUtility.PrepareForGameplayScene();

        if (SceneFader.Instance != null)
            SceneFader.Instance.FadeOutAndLoadScene(SceneManager.GetActiveScene().name);
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnClick_Settings()
    {
        if (debugLog)
            Debug.Log("[PauseUI] Click Settings");

        isSettingsOpen = true;
        if (runtimeOverlay != null)
            runtimeOverlay.HideVisualOnly();
        RuntimeUiInputUtility.RestoreModalInput();

        if (uiView != null)
            uiView.ToggleSettings(true);
    }

    public void CloseSettings()
    {
        isSettingsOpen = false;
        if (uiView != null)
            uiView.ToggleSettings(false);

        if (isPaused && useRuntimePauseOverlay)
            EnsureRuntimeOverlay().Show(this);
    }

    public void OnClick_ToTitle()
    {
        if (debugLog)
            Debug.Log("[PauseUI] Click ToTitle");

        RuntimeUiInputUtility.RestoreModalInput();
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();

        if (SceneFader.Instance != null)
            SceneFader.Instance.FadeOutAndLoadScene(titleSceneName);
        else
            SceneManager.LoadScene(titleSceneName);
    }

    public void OnClick_Quit()
    {
        if (debugLog)
            Debug.Log("[PauseUI] Click Quit");

        RuntimeUiInputUtility.RestoreModalInput();
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

    RuntimePauseMenuOverlay EnsureRuntimeOverlay()
    {
        if (runtimeOverlay != null)
            return runtimeOverlay;

        GameObject overlayObject = new GameObject("RuntimePauseMenuOverlay", typeof(RectTransform), typeof(RuntimePauseMenuOverlay));
        overlayObject.transform.SetParent(transform, false);
        runtimeOverlay = overlayObject.GetComponent<RuntimePauseMenuOverlay>();
        return runtimeOverlay;
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
