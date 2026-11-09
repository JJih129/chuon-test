#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 개발/QA 전용: 릴리스 빌드에서는 컴파일되지 않는다.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
public sealed class DevelopmentSceneHotkeys : MonoBehaviour
{
    const string ObjectName = "DevelopmentSceneHotkeys";

    const string TitleSceneName = "TitleScene";
    const string TutorialSceneName = "Tutorial";
    const string LobbySceneName = "Lobby";
    const string MainSceneName = "MainScene";
    const string OptionsSceneName = "OptionsScene";
    const string CreditsSceneName = "CreditsScene";

    const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
    const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string OptionsScenePath = "Assets/Scenes/OptionsScene.unity";
    const string CreditsScenePath = "Assets/Scenes/CreditsScene.unity";
    const string LevelFacilitySceneName = "Level_Facility";
    const string LevelFacilityScenePath = "Assets/Scenes/Level_Facility.unity";

    static readonly SceneHotkey[] SceneHotkeys =
    {
        new SceneHotkey(KeyCode.F1, TutorialSceneName, TutorialScenePath),
        new SceneHotkey(KeyCode.F2, LobbySceneName, LobbyScenePath),
        new SceneHotkey(KeyCode.F3, MainSceneName, MainScenePath),
        new SceneHotkey(KeyCode.F4, TitleSceneName, TitleScenePath),
        new SceneHotkey(KeyCode.F5, OptionsSceneName, OptionsScenePath),
        new SceneHotkey(KeyCode.F6, CreditsSceneName, CreditsScenePath),
        new SceneHotkey(KeyCode.F7, LevelFacilitySceneName, LevelFacilityScenePath),
    };

    static DevelopmentSceneHotkeys _instance;
    bool _isLoading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInstance()
    {
        // Scene-skip cheats must never ship in release builds.
        if (!Debug.isDebugBuild)
            return;

        if (_instance != null)
            return;

        GameObject root = new GameObject(ObjectName);
        _instance = root.AddComponent<DevelopmentSceneHotkeys>();
        DontDestroyOnLoad(root);
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (_isLoading || IsTypingInUiInput())
            return;

        for (int i = 0; i < SceneHotkeys.Length; i++)
        {
            SceneHotkey hotkey = SceneHotkeys[i];
            if (Input.GetKeyDown(hotkey.Key))
            {
                LoadScene(hotkey.SceneName, hotkey.ScenePath);
                return;
            }
        }
    }

    void LoadScene(string sceneName, string scenePath)
    {
        if (SceneManager.GetActiveScene().name == sceneName)
            return;

        TutorialSceneTransitionState.ClearAll();
        GameTimeScale.ReleaseAll();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _isLoading = true;

#if UNITY_EDITOR
        if (Application.isEditor)
        {
            EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            _isLoading = false;
            return;
        }
#endif

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        _isLoading = false;
    }

    static bool IsTypingInUiInput()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected == null)
            return false;

        return selected.GetComponent<InputField>() != null || selected.GetComponent("TMP_InputField") != null;
    }

    readonly struct SceneHotkey
    {
        public readonly KeyCode Key;
        public readonly string SceneName;
        public readonly string ScenePath;

        public SceneHotkey(KeyCode key, string sceneName, string scenePath)
        {
            Key = key;
            SceneName = sceneName;
            ScenePath = scenePath;
        }
    }
}
#endif
