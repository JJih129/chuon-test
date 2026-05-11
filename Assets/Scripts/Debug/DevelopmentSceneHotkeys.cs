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

    static readonly SceneHotkey[] SceneHotkeys =
    {
        new SceneHotkey(KeyCode.F1, TitleSceneName, TitleScenePath),
        new SceneHotkey(KeyCode.F2, TutorialSceneName, TutorialScenePath),
        new SceneHotkey(KeyCode.F3, LobbySceneName, LobbyScenePath),
        new SceneHotkey(KeyCode.F4, MainSceneName, MainScenePath),
        new SceneHotkey(KeyCode.F5, OptionsSceneName, OptionsScenePath),
        new SceneHotkey(KeyCode.F6, CreditsSceneName, CreditsScenePath),
    };

    static DevelopmentSceneHotkeys _instance;
    bool _isLoading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInstance()
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
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
