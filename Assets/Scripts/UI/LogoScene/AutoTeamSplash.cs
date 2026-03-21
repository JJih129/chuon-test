using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class AutoTeamSplash : MonoBehaviour
{
    [Header("Logo UI")]
    public CanvasGroup logoCanvasGroup;
    public float fadeDuration = 1f;
    public float displayDuration = 2f;
    public float startDelay = 0.5f;

    [Header("Next Scene")]
    public string titleSceneName = "TitleScene";
    public int fallbackTitleBuildIndex = 1;

    void Awake()
    {
        if (Time.timeScale != 1f)
            Time.timeScale = 1f;

        if (logoCanvasGroup == null)
            logoCanvasGroup = GetComponentInChildren<CanvasGroup>(true);

        NormalizeCanvasScale();
    }

    void Start()
    {
        StartCoroutine(PlaySplashSequence());
    }

    IEnumerator PlaySplashSequence()
    {
        NormalizeCanvasScale();

        if (logoCanvasGroup == null)
        {
            yield return new WaitForSecondsRealtime(startDelay + displayDuration);
            LoadNextScene();
            yield break;
        }

        logoCanvasGroup.gameObject.SetActive(true);
        logoCanvasGroup.alpha = 0f;

        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);

        float safeFadeDuration = Mathf.Max(0.0001f, fadeDuration);
        float timer = 0f;
        while (timer < safeFadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            logoCanvasGroup.alpha = Mathf.Lerp(0f, 1f, Mathf.Clamp01(timer / safeFadeDuration));
            yield return null;
        }

        logoCanvasGroup.alpha = 1f;

        if (displayDuration > 0f)
            yield return new WaitForSecondsRealtime(displayDuration);

        timer = 0f;
        while (timer < safeFadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            logoCanvasGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(timer / safeFadeDuration));
            yield return null;
        }

        logoCanvasGroup.alpha = 0f;
        LoadNextScene();
    }

    void NormalizeCanvasScale()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.transform.localScale.sqrMagnitude < 0.0001f)
            canvas.transform.localScale = Vector3.one;
    }

    void LoadNextScene()
    {
        if (!string.IsNullOrWhiteSpace(titleSceneName))
        {
            try
            {
                SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
                return;
            }
            catch
            {
            }
        }

        if (fallbackTitleBuildIndex >= 0 && fallbackTitleBuildIndex < SceneManager.sceneCountInBuildSettings)
            SceneManager.LoadScene(fallbackTitleBuildIndex, LoadSceneMode.Single);
    }
}
