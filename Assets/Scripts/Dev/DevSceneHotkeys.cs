using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 개발용 씬 점프 핫키.
/// - F1: TitleScene
/// - F2: Tutorial
/// - F3: Lobby
/// - F4: MainScene
/// 빌드 릴리즈에는 포함하지 않도록 Editor/Development Build 에서만 활성화한다.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class DevSceneHotkeys : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const string TitleSceneName = "TitleScene";
    private const string TutorialSceneName = "Tutorial";
    private const string LobbySceneName = "Lobby";
    private const string MainBossSceneName = "MainScene";

    private static DevSceneHotkeys _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        var go = new GameObject("[Dev] Scene Hotkeys");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DevSceneHotkeys>();
    }

    private void Update()
    {
        if (IsSceneLoadKeyPressed(KeyCode.F1, Key.F1))
        {
            LoadScene(TitleSceneName);
            return;
        }

        if (IsSceneLoadKeyPressed(KeyCode.F2, Key.F2))
        {
            LoadScene(TutorialSceneName);
            return;
        }

        if (IsSceneLoadKeyPressed(KeyCode.F3, Key.F3))
        {
            LoadScene(LobbySceneName);
            return;
        }

        if (IsSceneLoadKeyPressed(KeyCode.F4, Key.F4))
        {
            LoadScene(MainBossSceneName);
        }
    }

    private static bool IsSceneLoadKeyPressed(KeyCode legacyKey, Key newInputKey)
    {
        bool pressed = Input.GetKeyDown(legacyKey);

#if ENABLE_INPUT_SYSTEM
        if (!pressed && Keyboard.current != null)
            pressed = Keyboard.current[newInputKey].wasPressedThisFrame;
#endif

        return pressed;
    }

    private static void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return;

        var activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.name == sceneName) return;

        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
#endif
}
