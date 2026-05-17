using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PauseMenuView : MonoBehaviour
{
#if UNITY_EDITOR
    const string SettingsContentPrefabPath = "Assets/Prefabs/Generated/PauseOptionsRoot.prefab";
#endif
    const string UnifiedSettingsPrefabResourcePath = "UI/Title/PauseOptionsRoot_Title";
    const string SettingsContentHostName = "_SettingsContentHost";

    [Header("UI 연결")]
    public GameObject menuRoot;
    public CanvasGroup backgroundGroup;
    public RectTransform menuContainer;

    [Header("설정창 연결")]
    public GameObject settingsPanel;
    public CanvasGroup settingsGroup;
    public RectTransform settingsContentHostOverride;
    public GameObject settingsContentPrefab;

    [Header("Unified Settings Overlay")]
    [SerializeField] bool useUnifiedSettingsOverlay = true;
    [SerializeField] string unifiedSettingsPrefabResourcePath = UnifiedSettingsPrefabResourcePath;
    [SerializeField] bool debugLog = true;

    GameObject settingsContentInstance;
    RectTransform settingsContentVisualRoot;
    RectTransform settingsContentHost;
    PauseSettingsOverlayStyler settingsOverlayStyler;
    RectTransform settingsOverlayHostRect;
    TitleSettingsOverlay unifiedSettingsOverlay;
    GameObject unifiedSettingsPrefab;
    Tween hideTween;

    void Awake()
    {
        EnsureUnifiedSettingsOverlay();
        ApplyHiddenState();

        if (useUnifiedSettingsOverlay)
            return;

        EnsureSettingsPanelReferences();
        EnsureSettingsContent();
        ApplyHiddenState();
    }

    void ApplyHiddenState()
    {
        if (menuRoot != null)
            menuRoot.SetActive(false);

        RuntimeUiInputUtility.RestoreModalInput();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (backgroundGroup != null)
        {
            backgroundGroup.DOKill();
            backgroundGroup.alpha = 0f;
            backgroundGroup.interactable = false;
            backgroundGroup.blocksRaycasts = false;
        }

        if (menuContainer != null)
        {
            menuContainer.DOKill();
            menuContainer.localScale = Vector3.one;

            var menuCanvasGroup = menuContainer.GetComponent<CanvasGroup>();
            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.DOKill();
                menuCanvasGroup.alpha = 0f;
                menuCanvasGroup.interactable = false;
                menuCanvasGroup.blocksRaycasts = false;
            }
        }
    }

    public void ShowMenu()
    {
        if (menuRoot == null || backgroundGroup == null || menuContainer == null)
            return;

        KillHideTween();
        Canvas pauseCanvas = PromoteCanvasForMenu();
        RuntimeUiInputUtility.BeginModalInput(pauseCanvas);

        if (unifiedSettingsOverlay != null)
            unifiedSettingsOverlay.Hide();

        if (!useUnifiedSettingsOverlay)
        {
            EnsureSettingsPanelReferences();
            EnsureSettingsContent();
        }

        menuRoot.SetActive(true);
        menuContainer.gameObject.SetActive(true);
        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        backgroundGroup.alpha = 0f;
        backgroundGroup.interactable = true;
        backgroundGroup.blocksRaycasts = true;
        backgroundGroup.DOFade(1f, 0.3f).SetUpdate(true);

        menuContainer.localScale = Vector3.one * 0.8f;
        menuContainer.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);

        var menuCanvasGroup = menuContainer.GetComponent<CanvasGroup>();
        if (menuCanvasGroup != null)
        {
            menuCanvasGroup.alpha = 0f;
            menuCanvasGroup.interactable = true;
            menuCanvasGroup.blocksRaycasts = true;
            menuCanvasGroup.DOFade(1f, 0.3f).SetUpdate(true);
        }

        if (debugLog)
        {
            string menuGroupAlpha = menuCanvasGroup != null ? menuCanvasGroup.alpha.ToString("0.00") : "none";
            string canvasName = pauseCanvas != null ? pauseCanvas.name : "null";
            int canvasSort = pauseCanvas != null ? pauseCanvas.sortingOrder : -1;
            Debug.Log(
                $"[PauseUI] Show menuRoot={menuRoot.activeInHierarchy} bg={backgroundGroup.alpha:0.00}/{backgroundGroup.interactable}/{backgroundGroup.blocksRaycasts} " +
                $"menuGroup={menuGroupAlpha}/{(menuCanvasGroup != null && menuCanvasGroup.interactable)}/{(menuCanvasGroup != null && menuCanvasGroup.blocksRaycasts)} " +
                $"canvas={canvasName} sort={canvasSort} cursor={Cursor.visible}/{Cursor.lockState}");
        }
    }

    public void HideMenu(System.Action onComplete = null)
    {
        if (menuRoot == null || backgroundGroup == null || menuContainer == null)
        {
            onComplete?.Invoke();
            return;
        }

        KillHideTween();

        if (unifiedSettingsOverlay != null)
            unifiedSettingsOverlay.Hide();

        backgroundGroup.DOFade(0f, 0.2f).SetUpdate(true);
        menuContainer.DOScale(0.8f, 0.2f).SetEase(Ease.InQuad).SetUpdate(true);

        var menuCanvasGroup = menuContainer.GetComponent<CanvasGroup>();
        if (menuCanvasGroup != null)
            menuCanvasGroup.DOFade(0f, 0.2f).SetUpdate(true);

        hideTween = DOVirtual.DelayedCall(0.25f, () =>
        {
            hideTween = null;
            ApplyHiddenState();
            onComplete?.Invoke();
        }).SetUpdate(true);
    }

    public void HideMenuImmediate()
    {
        KillHideTween();
        ApplyHiddenState();
    }

    void KillHideTween()
    {
        if (hideTween == null)
            return;

        hideTween.Kill();
        hideTween = null;
    }

    Canvas PromoteCanvasForMenu()
    {
        Canvas canvas = GetComponentInParent<Canvas>(true);
        if (canvas == null)
            return null;

        canvas.overrideSorting = true;
        canvas.sortingOrder = 6500;
        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.enabled = true;

        return canvas;
    }

    public void ToggleSettings(bool isOpen)
    {
        if (useUnifiedSettingsOverlay && EnsureUnifiedSettingsOverlay())
        {
            if (menuContainer != null)
                menuContainer.gameObject.SetActive(!isOpen);

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            if (isOpen)
                unifiedSettingsOverlay.Show();
            else
                unifiedSettingsOverlay.Hide();

            return;
        }

        EnsureSettingsPanelReferences();
        EnsureSettingsContent();

        if (settingsPanel == null || settingsGroup == null || menuContainer == null)
        {
            if (menuContainer != null)
                menuContainer.gameObject.SetActive(!isOpen);
            return;
        }

        if (isOpen)
        {
            settingsPanel.SetActive(true);
            settingsGroup.alpha = 0f;
            settingsGroup.DOFade(1f, 0.3f).SetUpdate(true);
            menuContainer.gameObject.SetActive(false);
        }
        else
        {
            settingsGroup.DOFade(0f, 0.2f).SetUpdate(true).OnComplete(() =>
            {
                settingsPanel.SetActive(false);
                menuContainer.gameObject.SetActive(true);
                menuContainer.localScale = Vector3.one * 0.9f;
                menuContainer.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetUpdate(true);
            });
        }
    }

    bool EnsureUnifiedSettingsOverlay()
    {
        if (!useUnifiedSettingsOverlay)
            return false;

        if (unifiedSettingsOverlay != null)
            return true;

        if (string.IsNullOrWhiteSpace(unifiedSettingsPrefabResourcePath))
            return false;

        if (unifiedSettingsPrefab == null)
            unifiedSettingsPrefab = Resources.Load<GameObject>(unifiedSettingsPrefabResourcePath);
        if (unifiedSettingsPrefab == null)
            return false;

        var parent = menuRoot != null ? menuRoot.transform.parent : transform;
        if (parent == null)
            return false;

        var overlayObject = new GameObject(
            "PauseUnifiedSettingsOverlay",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(Image),
            typeof(TitleSettingsOverlay));
        overlayObject.layer = gameObject.layer;
        overlayObject.transform.SetParent(parent, false);

        unifiedSettingsOverlay = overlayObject.GetComponent<TitleSettingsOverlay>();
        if (!unifiedSettingsOverlay.Initialize(parent, unifiedSettingsPrefab))
        {
            Destroy(overlayObject);
            unifiedSettingsOverlay = null;
            return false;
        }

        return true;
    }

    void EnsureSettingsPanelReferences()
    {
        if (settingsPanel != null && settingsGroup != null)
        {
            EnsureSettingsPanelHost();
            return;
        }

        if (settingsPanel != null)
        {
            settingsGroup = settingsPanel.GetComponent<CanvasGroup>();
            if (settingsGroup == null)
                settingsGroup = settingsPanel.AddComponent<CanvasGroup>();
            EnsureSettingsPanelHost();
            return;
        }

        if (settingsGroup != null)
        {
            settingsPanel = settingsGroup.gameObject;
            EnsureSettingsPanelHost();
            return;
        }

        CreateFallbackSettingsPanel();
    }

    void CreateFallbackSettingsPanel()
    {
        var parent = menuContainer != null
            ? menuContainer.parent as RectTransform
            : (menuRoot != null ? menuRoot.transform as RectTransform : transform as RectTransform);
        if (parent == null)
            return;

        var panelObject = new GameObject(
            "GeneratedSettingsPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(CanvasGroup));
        panelObject.layer = menuRoot != null ? menuRoot.layer : gameObject.layer;
        panelObject.transform.SetParent(parent, false);

        var panelRect = panelObject.GetComponent<RectTransform>();
        if (menuContainer != null)
        {
            panelRect.anchorMin = menuContainer.anchorMin;
            panelRect.anchorMax = menuContainer.anchorMax;
            panelRect.anchoredPosition = menuContainer.anchoredPosition;
            panelRect.sizeDelta = menuContainer.sizeDelta;
            panelRect.pivot = menuContainer.pivot;
        }
        else
        {
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(600f, 700f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
        }

        var backgroundImage = panelObject.GetComponent<Image>();
        backgroundImage.color = new Color(0.1f, 0.14f, 0.18f, 0.95f);
        backgroundImage.raycastTarget = false;

        settingsPanel = panelObject;
        settingsGroup = panelObject.GetComponent<CanvasGroup>();
        settingsGroup.alpha = 0f;
        settingsGroup.interactable = true;
        settingsGroup.blocksRaycasts = true;

        EnsureSettingsPanelHost();
    }

    void EnsureSettingsContent()
    {
        EnsureSettingsContentPrefabReference();
        EnsureSettingsContentHost();

        if (settingsPanel == null || settingsContentHost == null || settingsContentPrefab == null)
            return;

        if (settingsContentInstance == null)
        {
            var instantiatedObject = Instantiate((Object)settingsContentPrefab, settingsContentHost, false);
            settingsContentInstance = instantiatedObject as GameObject;
            if (settingsContentInstance == null && instantiatedObject is Component component)
                settingsContentInstance = component.gameObject;
            if (settingsContentInstance == null)
                return;

            settingsContentInstance.name = settingsContentPrefab.name;
            PrepareSettingsVisualRoot();
            BindSettingsButtons(settingsContentVisualRoot != null ? settingsContentVisualRoot.gameObject : settingsContentInstance);
            settingsOverlayStyler = null;
            settingsOverlayHostRect = null;
        }

        ApplySettingsOverlayStyle();
    }

    void EnsureSettingsContentPrefabReference()
    {
        if (settingsContentPrefab != null)
            return;

#if UNITY_EDITOR
        settingsContentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsContentPrefabPath);
#endif
    }

    void BindSettingsButtons(GameObject root)
    {
        if (root == null)
            return;

        var buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button == null)
                continue;

            if (button.name == "Button_Cancel")
            {
                button.onClick.AddListener(HandleSettingsCancel);
            }
        }
    }

    void HandleSettingsCancel()
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.CloseSettings();
            return;
        }

        ToggleSettings(false);
    }

    void ApplySettingsOverlayStyle()
    {
        if (settingsContentInstance == null || settingsPanel == null)
            return;

        PrepareSettingsVisualRoot();
        EnsureSettingsContentHost();

        var styleTarget = settingsContentVisualRoot != null ? settingsContentVisualRoot.gameObject : settingsContentInstance;
        if (styleTarget == null)
            return;

        var settingsRect = settingsPanel.GetComponent<RectTransform>();
        if (settingsRect == null)
            return;

        if (settingsOverlayStyler == null || settingsOverlayStyler.gameObject != styleTarget)
            settingsOverlayStyler = styleTarget.GetComponent<PauseSettingsOverlayStyler>();
        if (settingsOverlayStyler == null)
            settingsOverlayStyler = styleTarget.AddComponent<PauseSettingsOverlayStyler>();

        if (settingsOverlayHostRect != settingsRect)
        {
            settingsOverlayHostRect = settingsRect;
            settingsOverlayStyler.Apply(settingsRect);
            return;
        }

        settingsOverlayStyler.RefreshRuntimeState();
    }

    void PrepareSettingsVisualRoot()
    {
        if (settingsContentInstance == null || settingsPanel == null)
            return;

        EnsureSettingsPanelHost();
        EnsureSettingsContentHost();

        if (settingsContentVisualRoot == null)
        {
            settingsContentVisualRoot = settingsContentInstance.transform as RectTransform;
            if (settingsContentVisualRoot == null)
                settingsContentVisualRoot = settingsContentInstance.GetComponentInChildren<RectTransform>(true);
        }

        if (settingsContentVisualRoot == null)
            return;

        if (settingsContentHost != null)
        {
            if (settingsContentVisualRoot.parent != settingsContentHost)
                settingsContentVisualRoot.SetParent(settingsContentHost, false);
        }
        else if (settingsContentVisualRoot.parent != settingsPanel.transform)
        {
            settingsContentVisualRoot.SetParent(settingsPanel.transform, false);
        }

        settingsContentVisualRoot.SetAsLastSibling();
        settingsContentVisualRoot.anchorMin = Vector2.zero;
        settingsContentVisualRoot.anchorMax = Vector2.one;
        settingsContentVisualRoot.offsetMin = Vector2.zero;
        settingsContentVisualRoot.offsetMax = Vector2.zero;
        settingsContentVisualRoot.localScale = Vector3.one;

        var nestedCanvas = settingsContentVisualRoot.GetComponent<Canvas>();
        if (nestedCanvas != null)
        {
            nestedCanvas.enabled = true;
            nestedCanvas.overrideSorting = true;
            var parentCanvas = settingsPanel.GetComponentInParent<Canvas>();
            nestedCanvas.sortingOrder = parentCanvas != null ? parentCanvas.sortingOrder + 1 : 223;
        }

        var canvasScaler = settingsContentVisualRoot.GetComponent<CanvasScaler>();
        if (canvasScaler != null)
            canvasScaler.enabled = true;

        var raycaster = settingsContentVisualRoot.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.enabled = true;
    }

    void EnsureSettingsContentHost()
    {
        if (settingsPanel == null)
            return;

        if (settingsContentHost == null)
        {
            if (settingsContentHostOverride != null)
                settingsContentHost = settingsContentHostOverride;
            else
                settingsContentHost = settingsPanel.transform.Find(SettingsContentHostName) as RectTransform;
            if (settingsContentHost == null)
            {
                var hostGo = new GameObject(SettingsContentHostName, typeof(RectTransform));
                hostGo.layer = settingsPanel.layer;
                hostGo.transform.SetParent(settingsPanel.transform, false);
                settingsContentHost = hostGo.GetComponent<RectTransform>();
            }
        }

        if (settingsContentHost == null)
            return;

        if (settingsContentHostOverride == null)
            settingsContentHostOverride = settingsContentHost;

        settingsContentHost.SetAsLastSibling();
        settingsContentHost.anchorMin = Vector2.zero;
        settingsContentHost.anchorMax = Vector2.one;
        settingsContentHost.offsetMin = Vector2.zero;
        settingsContentHost.offsetMax = Vector2.zero;
        settingsContentHost.localScale = Vector3.one;
        settingsContentHost.localRotation = Quaternion.identity;
        settingsContentHost.localPosition = Vector3.zero;
    }

    void EnsureSettingsPanelHost()
    {
        if (settingsPanel == null)
            return;

        RectTransform hostParent = null;
        if (menuRoot != null && menuRoot.transform.parent is RectTransform rootParent)
            hostParent = rootParent;
        else if (transform.parent is RectTransform fallbackParent)
            hostParent = fallbackParent;

        var panelRect = settingsPanel.GetComponent<RectTransform>();
        if (hostParent == null || panelRect == null)
            return;

        if (panelRect.parent != hostParent)
            panelRect.SetParent(hostParent, false);

        panelRect.SetAsLastSibling();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelRect.localScale = Vector3.one;
    }
}
