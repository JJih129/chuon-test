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

    void OnDisable()
    {
        // 일시정지 상태로 비활성화되면 정지 요청이 남지 않도록 해제.
        GameTimeScale.Release(this);
    }

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
        // 일시정지 메뉴에서 C = 설정 > 콤보 탭 바로 열기
        if (isPaused && !isSettingsOpen && Input.GetKeyDown(KeyCode.C))
        {
            OpenComboGuide();
            return;
        }

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
        GameTimeScale.Request(this, 0f);
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
            RuntimeMenuSceneStateUtility.ResumeFromPause(this);
            SetSupplementalOverlayVisibility(true);
            return;
        }

        uiView.HideMenu(() =>
        {
            RuntimeMenuSceneStateUtility.ResumeFromPause(this);
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

    /// <summary>설정창을 '콤보' 탭으로 연다 (일시정지 중 C 키).</summary>
    public void OpenComboGuide()
    {
        isSettingsOpen = true;
        if (uiView != null)
            uiView.OpenSettingsComboTab();
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
        if (_cachedHintBridge == null && Time.unscaledTime >= _nextHintBridgeLookupAt)
        {
            _cachedHintBridge = FindObjectOfType<TutorialHintUIBridge>(true);
            if (_cachedHintBridge == null)
                _nextHintBridgeLookupAt = Time.unscaledTime + FailedLookupCooldown;
        }

        if (_cachedHintBridge != null)
            _cachedHintBridge.SetOverlayVisible(visible);

        EnsureOverlayRootsResolved();
        for (int i = 0; i < _overlayRoots.Count; i++)
        {
            if (_overlayRoots[i] != null)
                _overlayRoots[i].gameObject.SetActive(visible);
        }
    }

    // 오버레이 루트는 한 번의 씬 스캔으로 캐시 (파괴/누락 시에만 쿨다운 후 재탐색)
    static readonly string[] OverlayRootNames =
    {
        "TutorialPresentationRuntime",
        "TutorialDefenseFeedbackRuntime",
        "TutorialSupportFeedbackRuntime"
    };

    const float FailedLookupCooldown = 0.5f;
    readonly System.Collections.Generic.List<Transform> _overlayRoots = new System.Collections.Generic.List<Transform>(4);
    readonly bool[] _overlayRootFound = new bool[3];
    bool _overlayRootsResolved;
    float _nextOverlayLookupAt;
    TutorialHintUIBridge _cachedHintBridge;
    float _nextHintBridgeLookupAt;

    void EnsureOverlayRootsResolved()
    {
        bool needsResolve = !_overlayRootsResolved;
        bool missingAny = false;
        if (!needsResolve)
        {
            for (int i = 0; i < _overlayRoots.Count; i++)
            {
                if (_overlayRoots[i] == null)
                {
                    needsResolve = true;
                    break;
                }
            }

            for (int i = 0; i < _overlayRootFound.Length; i++)
                missingAny |= !_overlayRootFound[i];
        }

        if (!needsResolve && !(missingAny && Time.unscaledTime >= _nextOverlayLookupAt))
            return;

        _overlayRootsResolved = true;
        _overlayRoots.Clear();
        for (int i = 0; i < _overlayRootFound.Length; i++)
            _overlayRootFound[i] = false;

        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null)
                continue;

            string currentName = current.name;
            for (int n = 0; n < OverlayRootNames.Length; n++)
            {
                if (currentName != OverlayRootNames[n])
                    continue;

                _overlayRoots.Add(current);
                _overlayRootFound[n] = true;
                break;
            }
        }

        for (int i = 0; i < _overlayRootFound.Length; i++)
        {
            if (!_overlayRootFound[i])
            {
                _nextOverlayLookupAt = Time.unscaledTime + FailedLookupCooldown;
                break;
            }
        }
    }
}
