using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class AutoTeamSplash : MonoBehaviour
{
    [Header("Logo UI")]
    public CanvasGroup logoCanvasGroup;
    public Image logoImage;
    public Image backgroundImage;
    public Color logoColor = new Color(0.92f, 0.92f, 0.92f, 1f);
    public Color backgroundColor = new Color(0.137f, 0.122f, 0.125f, 1f);
    public float fadeDuration = 1f;
    public float displayDuration = 2f;
    public float startDelay = 0.5f;

    [Header("Next Scene")]
    public string titleSceneName = "TitleScene";
    public int fallbackTitleBuildIndex = 1;

    void Awake()
    {
        RuntimeMenuSceneStateUtility.PrepareForMenuScene(false);

        if (logoCanvasGroup == null)
            logoCanvasGroup = GetComponentInChildren<CanvasGroup>(true);

        NormalizeCanvasScale();
        ApplyUnityPersonalStyle();
    }

    void Start()
    {
        StartCoroutine(PlaySplashSequence());
    }

    IEnumerator PlaySplashSequence()
    {
        NormalizeCanvasScale();
        ApplyUnityPersonalStyle();

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

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
    }

    void ApplyUnityPersonalStyle()
    {
        if (logoImage == null && logoCanvasGroup != null)
            logoImage = logoCanvasGroup.GetComponent<Image>();

        if (backgroundImage == null)
            backgroundImage = FindBackgroundImage();

        if (backgroundImage != null)
            backgroundImage.color = backgroundColor;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            mainCamera.backgroundColor = backgroundColor;

        if (logoImage == null)
            return;

        logoImage.color = logoColor;
        logoImage.preserveAspect = true;
        logoImage.raycastTarget = false;

        RectTransform logoRect = logoImage.rectTransform;
        logoRect.anchorMin = new Vector2(0.5f, 0.5f);
        logoRect.anchorMax = new Vector2(0.5f, 0.5f);
        logoRect.pivot = new Vector2(0.5f, 0.5f);
        logoRect.anchoredPosition = Vector2.zero;
        logoRect.sizeDelta = new Vector2(720f, 360f);
    }

    Image FindBackgroundImage()
    {
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image != null && image.gameObject.name.Contains("Background"))
                return image;
        }

        return null;
    }

    void LoadNextScene()
    {
        if (!string.IsNullOrWhiteSpace(titleSceneName) && Application.CanStreamedLevelBeLoaded(titleSceneName))
        {
            SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
            return;
        }

        if (fallbackTitleBuildIndex >= 0 && fallbackTitleBuildIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(fallbackTitleBuildIndex, LoadSceneMode.Single);
            return;
        }

        Debug.LogError($"[AutoTeamSplash] Next scene is not available. name={titleSceneName}, fallbackIndex={fallbackTitleBuildIndex}", this);
    }
}
