using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class TitleSceneUIController : MonoBehaviour
{
    const string DefaultSettingsPrefabResourcePath = "UI/Title/PauseOptionsRoot_Title";
    const string DefaultCreditsDataResourcePath = "UI/Title/TitleCredits_Default";

    [Header("Scene")]
    [SerializeField] string gameSceneName = "BootScene";
    [SerializeField] bool resetTimeScaleOnLoad = true;

    [Header("Runtime Resources")]
    [SerializeField] string settingsPrefabResourcePath = DefaultSettingsPrefabResourcePath;
    [SerializeField] string creditsDataResourcePath = DefaultCreditsDataResourcePath;

    Canvas _titleCanvas;
    Button _optionsButton;
    Button _creditsButton;
    Button _quitButton;
    GameObject _settingsPrefab;
    TitleCreditsData _creditsData;
    TitleSettingsOverlay _settingsOverlay;
    TitleCreditsOverlay _creditsOverlay;

    void Awake()
    {
        CacheCanvas();
        CacheButtons();
        CacheRuntimeResources();
        BindButtons();
    }

    void OnEnable()
    {
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();
    }

    void Update()
    {
        // 리바인드 중 ESC 는 리바인드 취소 (설정창을 닫지 않는다)
        if (ChuonInputRebinding.IsRebinding)
            return;

        // ESC 또는 패드 B / Circle = 뒤로
        if (!Input.GetKeyDown(KeyCode.Escape) && !ChuonInput.PadBackPressed())
            return;

        if (_settingsOverlay != null && _settingsOverlay.IsVisible)
        {
            _settingsOverlay.Hide();
            ChuonInputRuntime.SelectForGamepad(_optionsButton != null ? _optionsButton.gameObject : null);
            return;
        }

        if (_creditsOverlay != null && _creditsOverlay.IsVisible)
        {
            _creditsOverlay.Hide();
            ChuonInputRuntime.SelectForGamepad(_creditsButton != null ? _creditsButton.gameObject : null);
        }
    }

    public void OnClick_StartGame()
    {
        if (resetTimeScaleOnLoad)
            RuntimeMenuSceneStateUtility.PrepareForGameplayScene();

        if (string.IsNullOrEmpty(gameSceneName))
        {
            Debug.LogError("[TitleSceneUIController] gameSceneName is empty.", this);
            return;
        }

        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeOutAndLoadScene(gameSceneName);
            return;
        }

        SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    public void OnClick_OpenSettings()
    {
        if (!EnsureSettingsOverlay())
            return;

        if (_creditsOverlay != null && _creditsOverlay.IsVisible)
            _creditsOverlay.Hide();

        _settingsOverlay.Show();
    }

    public void OnClick_OpenCredits()
    {
        if (!EnsureCreditsOverlay())
            return;

        if (_settingsOverlay != null && _settingsOverlay.IsVisible)
            _settingsOverlay.Hide();

        _creditsOverlay.Show(_creditsData);
        // 반복 14A: 패드 포커스가 오버레이 뒤 크레딧 버튼에 남아 A 가 크레딧을 다시 여는 문제 → 닫기 버튼으로
        ChuonInputRuntime.SelectForGamepad(_creditsOverlay.CloseButton != null ? _creditsOverlay.CloseButton.gameObject : null);
    }

    public void OnClick_QuitGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void CacheCanvas()
    {
        _titleCanvas = GetComponentInChildren<Canvas>(true);
    }

    void CacheButtons()
    {
        var buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
                continue;

            switch (buttons[i].name)
            {
                case "Button_Options":
                    _optionsButton = buttons[i];
                    break;
                case "Button_Credits":
                    _creditsButton = buttons[i];
                    break;
                case "Button_Quit":
                    _quitButton = buttons[i];
                    break;
            }
        }
    }

    void CacheRuntimeResources()
    {
        _settingsPrefab = Resources.Load<GameObject>(settingsPrefabResourcePath);
        _creditsData = Resources.Load<TitleCreditsData>(creditsDataResourcePath);
        if (_creditsData == null)
            _creditsData = TitleCreditsData.CreateRuntimeFallback();
    }

    void BindButtons()
    {
        if (_optionsButton != null)
            RebindButton(_optionsButton, OnClick_OpenSettings);

        if (_creditsButton != null)
            RebindButton(_creditsButton, OnClick_OpenCredits);

        if (_quitButton != null)
            RebindButton(_quitButton, OnClick_QuitGame);
    }

    static void RebindButton(Button button, UnityAction action)
    {
        if (button == null || action == null)
            return;

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
    }

    bool EnsureSettingsOverlay()
    {
        if (_settingsOverlay != null)
            return true;

        if (_titleCanvas == null)
            CacheCanvas();

        if (_titleCanvas == null || _settingsPrefab == null)
        {
            Debug.LogError("[TitleSceneUIController] Failed to create settings overlay. Canvas or prefab is missing.", this);
            return false;
        }

        var overlayObject = new GameObject(
            "TitleSettingsOverlay",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(Image),
            typeof(Button),
            typeof(TitleSettingsOverlay));
        _settingsOverlay = overlayObject.GetComponent<TitleSettingsOverlay>();
        if (!_settingsOverlay.Initialize(_titleCanvas.transform, _settingsPrefab))
        {
            Destroy(overlayObject);
            _settingsOverlay = null;
            return false;
        }

        return true;
    }

    bool EnsureCreditsOverlay()
    {
        if (_creditsOverlay != null)
            return true;

        if (_titleCanvas == null)
            CacheCanvas();

        if (_titleCanvas == null)
        {
            Debug.LogError("[TitleSceneUIController] Failed to create credits overlay. Canvas is missing.", this);
            return false;
        }

        var overlayObject = new GameObject(
            "TitleCreditsOverlay",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(Image),
            typeof(Button),
            typeof(TitleCreditsOverlay));
        _creditsOverlay = overlayObject.GetComponent<TitleCreditsOverlay>();
        _creditsOverlay.Initialize(_titleCanvas.transform);
        return true;
    }
}
