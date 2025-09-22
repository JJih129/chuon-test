// Assets/Scripts/TitleManager.cs (디버그용 패치본)
// 수정: 버튼 클릭 로그, 씬 이름 출력, 에디터에서 Build Settings 검사, LoadSceneAsync 사용
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

[DisallowMultipleComponent]
public class TitleManager : MonoBehaviour
{
    [Header("▶ 씬 연결")]
    [Tooltip("로딩할 메인 게임 씬 이름 (씬 파일 이름과 정확히 일치)")]
    public string mainSceneName = "MainScene";

    [Header("▶ UI 참조")]
    public Button startButton;
    public Button optionsButton;
    public Button creditsButton;
    public Button quitButton;
    public Animator logoAnimator;
    public CanvasGroup fadeCanvasGroup;
    public Text versionText;

    [Header("▶ 오디오")]
    public AudioSource bgmSource;
    public AudioClip startSfx;

    [Header("▶ 전환 / 튜닝")]
    public float fadeDuration = 0.6f;
    public bool blockInputDuringTransition = true;

    IInputBlocker _inputBlocker;

    void Awake()
    {
        // 안전하게 바인딩 (중복방지)
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(OnStartPressed);
            startButton.onClick.AddListener(OnStartPressed);
        }
        if (optionsButton != null)
        {
            optionsButton.onClick.RemoveListener(OnOptionsPressed);
            optionsButton.onClick.AddListener(OnOptionsPressed);
        }
        if (creditsButton != null)
        {
            creditsButton.onClick.RemoveListener(OnCreditsPressed);
            creditsButton.onClick.AddListener(OnCreditsPressed);
        }
        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(OnQuitPressed);
            quitButton.onClick.AddListener(OnQuitPressed);
        }

        _inputBlocker = GetComponent<IInputBlocker>() ?? FindObjectOfType<SimpleInputBlocker>() as IInputBlocker;

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false;
        }

        if (versionText != null) versionText.text = "v" + Application.version;

        Debug.Log($"[TitleManager] Awake. mainSceneName='{mainSceneName}' startButton={(startButton!=null)}");
    }

    void Start()
    {
        if (bgmSource != null && !bgmSource.isPlaying) bgmSource.Play();
        if (logoAnimator != null) logoAnimator.SetTrigger("Intro");
    }

    // public methods for Button OnClick
    public void OnStartPressed()
    {
        Debug.Log("[TitleManager] OnStartPressed called.");
        if (string.IsNullOrEmpty(mainSceneName))
        {
            Debug.LogError("[TitleManager] mainSceneName is empty. Set the target scene name in the inspector.");
            return;
        }

#if UNITY_EDITOR
        // 에디터에서 Build Settings에 씬이 존재하는지 검사
        bool found = false;
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (!s.enabled) continue;
            var name = Path.GetFileNameWithoutExtension(s.path);
            if (name == mainSceneName) { found = true; break; }
        }
        Debug.Log($"[TitleManager] Editor BuildSettings contains '{mainSceneName}': {found}");
        if (!found)
        {
            Debug.LogWarning("[TitleManager] 해당 씬이 Build Settings에 없습니다. Build Settings에 씬을 추가하세요. (File > Build Settings)");
        }
#endif

        StartCoroutine(StartGameRoutine());
    }

    public void OnOptionsPressed()
    {
        Debug.Log("[TitleManager] Options pressed - implement options popup.");
    }

    public void OnCreditsPressed()
    {
        Debug.Log("[TitleManager] Credits pressed - implement credits flow.");
    }

    public void OnQuitPressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator StartGameRoutine()
    {
        if (blockInputDuringTransition && _inputBlocker != null) _inputBlocker.BlockAll(true);

        if (fadeCanvasGroup != null) fadeCanvasGroup.blocksRaycasts = true;

        if (startSfx != null)
        {
            var pos = (Camera.main != null) ? Camera.main.transform.position : Vector3.zero;
            AudioSource.PlayClipAtPoint(startSfx, pos);
        }

        if (logoAnimator != null) logoAnimator.SetTrigger("Start");

        if (fadeCanvasGroup != null)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeCanvasGroup.alpha = Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            fadeCanvasGroup.alpha = 1f;
        }

        Debug.Log($"[TitleManager] Loading scene '{mainSceneName}'...");
        var op = SceneManager.LoadSceneAsync(mainSceneName);
        if (op == null)
        {
            Debug.LogError("[TitleManager] SceneManager.LoadSceneAsync returned null. Check scene name and Build Settings.");
            yield break;
        }
        while (!op.isDone) yield return null;
    }
}
