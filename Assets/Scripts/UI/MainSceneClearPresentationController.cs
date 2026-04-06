using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MainSceneClearPresentationController : MonoBehaviour
{
    [SerializeField] private MainSceneArrivalController arrivalController;
    [SerializeField] private PlayerHUD playerHud;
    [SerializeField] private BossHealth bossHealth;
    [SerializeField] private MainSceneBossStatusController bossStatusController;
    [SerializeField] private MainSceneObjectivePanelController objectivePanelController;
    [SerializeField] private MainSceneCombatAssistController combatAssistController;

    [Header("Clear Cues")]
    [SerializeField] private string clearMessage = "\uc704\ud611 \ubc18\uc751 \uc18c\uba78";
    [SerializeField] private string syncMessage = "\uc2e4\uc804 \uac80\uc99d \uc644\ub8cc";
    [SerializeField, Min(0.1f)] private float clearHold = 0.92f;
    [SerializeField, Min(0.1f)] private float syncHold = 0.74f;
    [SerializeField, Min(0f)] private float syncDelay = 0.32f;
    [SerializeField] private int clearCuePriority = 30;
    [SerializeField] private AttackTelegraphType clearTelegraphType = AttackTelegraphType.Parry;
    [SerializeField] private AttackTelegraphType syncTelegraphType = AttackTelegraphType.Guard;

    [Header("Overlay")]
    [SerializeField] private string overlayTitle = "CLEAR";
    [SerializeField] private string overlaySubtitle = "\uc2e4\uc804 \uac80\uc99d \uc644\ub8cc";
    [SerializeField] private Vector2 overlaySize = new Vector2(540f, 132f);
    [SerializeField] private Vector2 overlayAnchoredPosition = new Vector2(0f, -48f);
    [SerializeField] private int overlayTitleFontSize = 42;
    [SerializeField] private int overlaySubtitleFontSize = 20;
    [SerializeField, Min(0.1f)] private float overlayFadeInDuration = 0.18f;
    [SerializeField, Min(0.1f)] private float overlayHoldDuration = 0.78f;
    [SerializeField, Min(0.1f)] private float overlayFadeOutDuration = 0.26f;
    [SerializeField] private Color overlayPanelColor = new Color(0.03f, 0.08f, 0.12f, 0.88f);
    [SerializeField] private Color overlayAccentColor = new Color(0.74f, 1f, 0.84f, 0.98f);
    [SerializeField] private Color overlayBodyColor = new Color(0.90f, 0.97f, 1f, 0.96f);

    bool _subscribed;
    bool _fromLobbyTransition;
    bool _clearSequenceStarted;
    Coroutine _clearRoutine;
    CanvasGroup _overlayCanvasGroup;
    RectTransform _overlayRoot;
    Image _overlayPanel;
    Image _overlayAccent;
    Text _overlayTitleText;
    Text _overlaySubtitleText;

    public void ConfigureRuntime(
        MainSceneArrivalController runtimeArrivalController,
        PlayerHUD runtimeHud,
        BossHealth runtimeBossHealth,
        MainSceneBossStatusController runtimeBossStatusController,
        MainSceneObjectivePanelController runtimeObjectivePanelController)
    {
        arrivalController = runtimeArrivalController;
        playerHud = runtimeHud;
        bossHealth = runtimeBossHealth;
        bossStatusController = runtimeBossStatusController;
        objectivePanelController = runtimeObjectivePanelController;

        ResolveReferences();
        _fromLobbyTransition = arrivalController != null && arrivalController.IsLobbyTransitionActive;
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        ResolveReferences();
        _fromLobbyTransition = arrivalController != null && arrivalController.IsLobbyTransitionActive;
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        StopClearRoutine();
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
        StopClearRoutine();
    }

    void ResolveReferences()
    {
        if (arrivalController == null)
            arrivalController = GetComponent<MainSceneArrivalController>();
        if (playerHud == null)
            playerHud = FindObjectOfType<PlayerHUD>(true);
        if (bossHealth == null)
            bossHealth = FindObjectOfType<BossHealth>(true);
        if (bossStatusController == null)
            bossStatusController = GetComponent<MainSceneBossStatusController>();
        if (objectivePanelController == null)
            objectivePanelController = GetComponent<MainSceneObjectivePanelController>();
        if (combatAssistController == null)
            combatAssistController = GetComponent<MainSceneCombatAssistController>();

        EnsureOverlayVisuals();
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (!_fromLobbyTransition || bossHealth == null || bossHealth.IsDead)
            return;

        bossHealth.OnDied += HandleBossDied;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed || bossHealth == null)
            return;

        bossHealth.OnDied -= HandleBossDied;
        _subscribed = false;
    }

    void HandleBossDied()
    {
        if (_clearSequenceStarted)
            return;

        _clearSequenceStarted = true;
        ReleaseSubscriptions();

        if (combatAssistController != null && combatAssistController.enabled)
            combatAssistController.enabled = false;
        if (arrivalController != null && arrivalController.enabled)
            arrivalController.enabled = false;

        bossStatusController?.ShowClearState();
        objectivePanelController?.ShowClearState();

        StopClearRoutine();
        _clearRoutine = StartCoroutine(CoPlayClearSequence());
    }

    IEnumerator CoPlayClearSequence()
    {
        PlayOverlay();

        if (playerHud != null)
            playerHud.ShowRuntimeTelegraphMessage(clearMessage, clearTelegraphType, clearHold, false, clearCuePriority);

        if (syncDelay > 0f)
            yield return new WaitForSecondsRealtime(syncDelay);

        if (playerHud != null)
            playerHud.ShowRuntimeTelegraphMessage(syncMessage, syncTelegraphType, syncHold, false, clearCuePriority);

        _clearRoutine = null;
    }

    void StopClearRoutine()
    {
        if (_clearRoutine == null)
            return;

        StopCoroutine(_clearRoutine);
        _clearRoutine = null;
    }

    void EnsureOverlayVisuals()
    {
        if (playerHud == null || playerHud.hpFillImage == null || playerHud.hpFillImage.canvas == null)
            return;

        RectTransform parent = playerHud.hpFillImage.canvas.transform as RectTransform;
        if (parent == null)
            return;

        if (_overlayRoot == null)
        {
            Transform existing = parent.Find("MainSceneClearOverlay");
            if (existing != null)
                _overlayRoot = existing as RectTransform;

            if (_overlayRoot == null)
            {
                GameObject rootObject = new GameObject("MainSceneClearOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
                _overlayRoot = rootObject.GetComponent<RectTransform>();
                _overlayRoot.SetParent(parent, false);
            }
        }

        _overlayRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _overlayRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _overlayRoot.pivot = new Vector2(0.5f, 0.5f);
        _overlayRoot.anchoredPosition = overlayAnchoredPosition;
        _overlayRoot.sizeDelta = overlaySize;
        _overlayRoot.localScale = Vector3.one;
        _overlayRoot.SetAsLastSibling();

        _overlayCanvasGroup = _overlayRoot.GetComponent<CanvasGroup>();
        if (_overlayCanvasGroup != null)
            _overlayCanvasGroup.alpha = 0f;

        _overlayPanel = EnsureImage(_overlayRoot, "Panel", out RectTransform panelRect);
        StretchToParent(panelRect);
        _overlayPanel.color = overlayPanelColor;

        _overlayAccent = EnsureImage(_overlayRoot, "Accent", out RectTransform accentRect);
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, 8f);
        _overlayAccent.color = overlayAccentColor;

        _overlayTitleText = EnsureText(_overlayRoot, "Title");
        RectTransform titleRect = _overlayTitleText.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 18f);
        titleRect.sizeDelta = new Vector2(overlaySize.x - 48f, 48f);
        _overlayTitleText.alignment = TextAnchor.MiddleCenter;
        _overlayTitleText.fontSize = overlayTitleFontSize;
        _overlayTitleText.fontStyle = FontStyle.Bold;
        _overlayTitleText.color = overlayAccentColor;
        _overlayTitleText.text = overlayTitle;

        _overlaySubtitleText = EnsureText(_overlayRoot, "Subtitle");
        RectTransform subtitleRect = _overlaySubtitleText.rectTransform;
        subtitleRect.anchorMin = new Vector2(0.5f, 0.5f);
        subtitleRect.anchorMax = new Vector2(0.5f, 0.5f);
        subtitleRect.pivot = new Vector2(0.5f, 0.5f);
        subtitleRect.anchoredPosition = new Vector2(0f, -28f);
        subtitleRect.sizeDelta = new Vector2(overlaySize.x - 48f, 28f);
        _overlaySubtitleText.alignment = TextAnchor.MiddleCenter;
        _overlaySubtitleText.fontSize = overlaySubtitleFontSize;
        _overlaySubtitleText.fontStyle = FontStyle.Normal;
        _overlaySubtitleText.color = overlayBodyColor;
        _overlaySubtitleText.text = overlaySubtitle;
    }

    void PlayOverlay()
    {
        EnsureOverlayVisuals();
        if (_overlayCanvasGroup == null)
            return;

        StopCoroutine(nameof(CoPlayOverlay));
        StartCoroutine(CoPlayOverlay());
    }

    IEnumerator CoPlayOverlay()
    {
        _overlayCanvasGroup.alpha = 0f;

        float fadeIn = Mathf.Max(0.05f, overlayFadeInDuration);
        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.unscaledDeltaTime;
            _overlayCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeIn);
            yield return null;
        }

        _overlayCanvasGroup.alpha = 1f;

        if (overlayHoldDuration > 0f)
            yield return new WaitForSecondsRealtime(overlayHoldDuration);

        float fadeOut = Mathf.Max(0.05f, overlayFadeOutDuration);
        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.unscaledDeltaTime;
            _overlayCanvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeOut);
            yield return null;
        }

        _overlayCanvasGroup.alpha = 0f;
    }

    static Image EnsureImage(Transform parent, string objectName, out RectTransform rectTransform)
    {
        Transform existing = parent.Find(objectName);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            image = imageObject.GetComponent<Image>();
        }

        rectTransform = image.rectTransform;
        image.raycastTarget = false;
        return image;
    }

    static Text EnsureText(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        Text text = existing != null ? existing.GetComponent<Text>() : null;
        if (text == null)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            text = textObject.GetComponent<Text>();
        }

        text.font = RuntimeBuiltInFontUtility.GetDefaultFont();
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }
}
