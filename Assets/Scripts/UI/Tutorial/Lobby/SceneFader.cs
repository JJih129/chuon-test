using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class SceneFader : MonoBehaviour
{
    public static SceneFader Instance;

    [Header("UI Link")]
    public CanvasGroup fadeCanvasGroup;
    public float fadeDuration = 1.0f;
    [SerializeField] bool fadeInOnSceneStart = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            HideImmediate();
        }
        else
        {
            if (Instance.fadeCanvasGroup == null && fadeCanvasGroup != null)
                Instance.fadeCanvasGroup = fadeCanvasGroup;

            Destroy(gameObject);
            return;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        if (fadeInOnSceneStart)
            FadeIn();
        else
            HideImmediate();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (fadeInOnSceneStart)
            FadeIn();
        else
            HideImmediate();
    }

    void FadeIn()
    {
        if (!fadeCanvasGroup)
            return;

        SetFadeObjectActive(true);
        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 1f;
        fadeCanvasGroup.blocksRaycasts = true;
        fadeCanvasGroup.interactable = true;

        fadeCanvasGroup.DOFade(0f, fadeDuration)
            .SetUpdate(true)
            .SetEase(Ease.InOutQuad)
            .OnComplete(() =>
            {
                fadeCanvasGroup.blocksRaycasts = false;
                fadeCanvasGroup.interactable = false;
                SetFadeObjectActive(false);
            });
    }

    public void FadeOutAndLoadScene(string sceneName)
    {
        FadeOut(() => SceneManager.LoadScene(sceneName));
    }

    public Tween FadeOut(System.Action onComplete)
    {
        if (!fadeCanvasGroup)
        {
            onComplete?.Invoke();
            return null;
        }

        SetFadeObjectActive(true);
        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = true;
        fadeCanvasGroup.interactable = true;

        return fadeCanvasGroup.DOFade(1f, fadeDuration)
            .SetUpdate(true)
            .SetEase(Ease.InOutQuad)
            .OnComplete(() => onComplete?.Invoke());
    }

    public void HideImmediate()
    {
        if (!fadeCanvasGroup)
            return;

        fadeCanvasGroup.DOKill();
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
        fadeCanvasGroup.interactable = false;
        SetFadeObjectActive(false);
    }

    void SetFadeObjectActive(bool active)
    {
        if (!fadeCanvasGroup || fadeCanvasGroup.gameObject == gameObject)
            return;

        fadeCanvasGroup.gameObject.SetActive(active);
    }
}
