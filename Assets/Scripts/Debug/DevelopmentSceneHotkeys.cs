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

    const string TutorialSceneName = "Tutorial";
    const string LobbySceneName = "Lobby";
    const string MainSceneName = "MainScene";

    const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
    const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";

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

        if (Input.GetKeyDown(KeyCode.F1))
            LoadScene(TutorialSceneName, TutorialScenePath);
        else if (Input.GetKeyDown(KeyCode.F2))
            LoadScene(LobbySceneName, LobbyScenePath);
        else if (Input.GetKeyDown(KeyCode.F3))
            LoadScene(MainSceneName, MainScenePath);
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
}
