using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RuntimePauseMenuOverlay : MonoBehaviour
{
    const int SortingOrder = 7200;

    Canvas canvas;
    CanvasGroup group;
    Button resumeButton;
    PauseManager owner;
    readonly Button[] buttons = new Button[5];

    public void Show(PauseManager pauseManager)
    {
        owner = pauseManager;
        EnsureBuilt();

        gameObject.SetActive(true);
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        RuntimeUiInputUtility.EnsureEventSystem();
        RuntimeUiInputUtility.ForceMenuCursor();
        RuntimeUiInputUtility.BeginModalInput(canvas);

        if (EventSystem.current != null && resumeButton != null)
            EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
    }

    void Update()
    {
        if (group == null || !group.interactable || !Input.GetMouseButtonDown(0))
            return;

        Vector2 mousePosition = Input.mousePosition;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || !button.interactable)
                continue;

            RectTransform rect = button.transform as RectTransform;
            if (rect == null || !RectTransformUtility.RectangleContainsScreenPoint(rect, mousePosition, null))
                continue;

            Debug.Log("[PauseUI] DirectClick " + button.name);
            button.onClick.Invoke();
            return;
        }
    }

    public void Hide()
    {
        if (group != null)
        {
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;
        }

        RuntimeUiInputUtility.RestoreModalInput();
        gameObject.SetActive(false);
    }

    public void HideVisualOnly()
    {
        if (group != null)
        {
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;
        }

        gameObject.SetActive(false);
    }

    void EnsureBuilt()
    {
        if (canvas != null)
            return;

        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _ = gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        RectTransform root = gameObject.GetComponent<RectTransform>();
        if (root == null)
            root = gameObject.AddComponent<RectTransform>();
        Stretch(root);

        Image dim = CreateImage("Dim", root, new Color(0f, 0f, 0f, 0.42f));
        Stretch(dim.rectTransform);

        RectTransform panel = CreateImage("Panel", root, new Color(0f, 0f, 0f, 0.48f)).rectTransform;
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(420f, 420f);

        resumeButton = buttons[0] = CreateButton(panel, "계속하기", 128f, () => owner.OnClick_Resume());
        buttons[1] = CreateButton(panel, "재시작하기", 56f, () => owner.OnClick_Restart());
        buttons[2] = CreateButton(panel, "설정", -16f, () => owner.OnClick_Settings());
        buttons[3] = CreateButton(panel, "타이틀로 이동", -88f, () => owner.OnClick_ToTitle());
        buttons[4] = CreateButton(panel, "종료하기", -160f, () => owner.OnClick_Quit());

        gameObject.SetActive(false);
    }

    Button CreateButton(RectTransform parent, string label, float y, UnityEngine.Events.UnityAction callback)
    {
        GameObject buttonObject = new GameObject("Button_" + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(320f, 58f);
        rect.localScale = Vector3.one;

        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.62f, 0.62f, 0.62f, 0.96f);
        image.raycastTarget = true;

        var button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(callback);

        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        colors.pressedColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);
        button.colors = colors;

        Text text = CreateText("Label", rect, label, 24, Color.white);
        text.alignment = TextAnchor.MiddleCenter;
        Stretch(text.rectTransform);
        text.transform.SetAsLastSibling();

        return button;
    }

    static Image CreateImage(string objectName, RectTransform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        var image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    static Text CreateText(string objectName, RectTransform parent, string text, int fontSize, Color color)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text label = textObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
