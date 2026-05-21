using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TitleCreditsOverlay : MonoBehaviour
{
    const float DefaultFadeDuration = 0.18f;
    const float DefaultPixelsPerSecond = 40f;

    static readonly Color OverlayColor = new Color(0.01f, 0.02f, 0.05f, 0.92f);
    static readonly Color FrameColor = new Color(0.04f, 0.06f, 0.11f, 0.97f);
    static readonly Color FrameLineColor = new Color(0.18f, 0.92f, 1f, 0.80f);
    static readonly Color AccentColor = new Color(1f, 0.74f, 0.34f, 0.98f);
    static readonly Color TextColor = new Color(0.95f, 0.98f, 1f, 0.98f);
    static readonly Color MutedTextColor = new Color(0.72f, 0.84f, 0.92f, 0.94f);
    static readonly Color CardColor = new Color(0.06f, 0.10f, 0.16f, 0.95f);

    CanvasGroup _canvasGroup;
    RectTransform _rootRect;
    RectTransform _frameRect;
    RectTransform _viewportRect;
    RectTransform _contentRect;
    Button _closeButton;
    Button _quitButton;
    Text _titleText;
    Text _subtitleText;
    TitleCreditsData _boundData;
    Coroutine _fadeRoutine;
    bool _isVisible;
    bool _structureBuilt;
    float _manualPauseTimer;
    float _scrollPixelsPerSecond;
    UnityAction _quitAction;

    public bool IsVisible => _isVisible;

    public void Initialize(Transform parent)
    {
        if (_structureBuilt)
            return;

        transform.SetParent(parent, false);
        BuildStructure();
        _structureBuilt = true;
        gameObject.SetActive(false);
    }

    public void ConfigureQuitButton(bool visible, string label, UnityAction action)
    {
        _quitAction = action;
        if (!_structureBuilt || _quitButton == null)
            return;

        _quitButton.gameObject.SetActive(visible);
        _quitButton.onClick.RemoveAllListeners();
        if (visible && _quitAction != null)
            _quitButton.onClick.AddListener(_quitAction);

        Text labelText = _quitButton.GetComponentInChildren<Text>(true);
        if (labelText != null)
            labelText.text = string.IsNullOrWhiteSpace(label) ? "게임 종료" : label;
    }

    public void Show(TitleCreditsData data)
    {
        if (!_structureBuilt)
            return;

        BindData(data);
        gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        ResetScrollPosition();
        _manualPauseTimer = 0f;
        _isVisible = true;
        FadeTo(1f, true);
    }

    public void Hide()
    {
        if (!_structureBuilt || !_isVisible)
            return;

        _isVisible = false;
        FadeTo(0f, false);
    }

    void Update()
    {
        if (!_isVisible || _contentRect == null || _viewportRect == null)
            return;

        if (Input.mouseScrollDelta.sqrMagnitude > 0.0001f)
            _manualPauseTimer = 2f;

        if (_manualPauseTimer > 0f)
        {
            _manualPauseTimer -= Time.unscaledDeltaTime;
            return;
        }

        float hiddenHeight = Mathf.Max(0f, _contentRect.rect.height - _viewportRect.rect.height);
        if (hiddenHeight <= 1f)
            return;

        Vector2 anchored = _contentRect.anchoredPosition;
        anchored.y += _scrollPixelsPerSecond * Time.unscaledDeltaTime;
        if (anchored.y > hiddenHeight + 100f)
            anchored.y = -_viewportRect.rect.height * 0.35f;
        _contentRect.anchoredPosition = anchored;
    }

    void BuildStructure()
    {
        _rootRect = GetOrAdd<RectTransform>(gameObject);
        Stretch(_rootRect);

        Image overlay = GetOrAdd<Image>(gameObject);
        overlay.color = OverlayColor;
        overlay.raycastTarget = true;

        Button rootButton = GetComponent<Button>();
        if (rootButton != null)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.enabled = false;
        }

        _canvasGroup = GetOrAdd<CanvasGroup>(gameObject);
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        _frameRect = CreateRect("CreditsFrame", transform);
        _frameRect.anchorMin = new Vector2(0.04f, 0.04f);
        _frameRect.anchorMax = new Vector2(0.96f, 0.96f);
        _frameRect.offsetMin = Vector2.zero;
        _frameRect.offsetMax = Vector2.zero;
        _frameRect.gameObject.AddComponent<Image>().color = FrameColor;

        CreateLine("FrameTop", _frameRect, new Vector2(0.01f, 0.985f), new Vector2(0.99f, 0.988f), FrameLineColor);
        CreateLine("FrameBottom", _frameRect, new Vector2(0.01f, 0.012f), new Vector2(0.99f, 0.015f), FrameLineColor);
        CreateLine("FrameLeft", _frameRect, new Vector2(0.01f, 0.02f), new Vector2(0.0125f, 0.98f), new Color(FrameLineColor.r, FrameLineColor.g, FrameLineColor.b, 0.45f));
        CreateLine("FrameRight", _frameRect, new Vector2(0.9875f, 0.02f), new Vector2(0.99f, 0.98f), new Color(FrameLineColor.r, FrameLineColor.g, FrameLineColor.b, 0.45f));
        CreateLine("HeaderAccent", _frameRect, new Vector2(0.04f, 0.92f), new Vector2(0.22f, 0.925f), AccentColor);

        _titleText = CreateText("Title", _frameRect, 36, FontStyle.Bold, TextAnchor.UpperLeft);
        _titleText.text = "\uAC1C\uBC1C\uC9C4";
        _titleText.color = TextColor;
        ConfigureRect(_titleText.rectTransform, new Vector2(0.04f, 0.92f), new Vector2(0.55f, 0.98f));

        _subtitleText = CreateText("Subtitle", _frameRect, 15, FontStyle.Bold, TextAnchor.UpperLeft);
        _subtitleText.color = MutedTextColor;
        ConfigureRect(_subtitleText.rectTransform, new Vector2(0.04f, 0.84f), new Vector2(0.72f, 0.90f));

        _closeButton = CreateButton("BackButton", _frameRect, "\uB4A4\uB85C");
        ConfigureRect(_closeButton.transform as RectTransform, new Vector2(0.84f, 0.92f), new Vector2(0.95f, 0.97f));
        _closeButton.onClick.AddListener(Hide);

        _quitButton = CreateButton("QuitButton", _frameRect, "게임 종료");
        ConfigureRect(_quitButton.transform as RectTransform, new Vector2(0.70f, 0.92f), new Vector2(0.83f, 0.97f));
        _quitButton.gameObject.SetActive(false);

        _viewportRect = CreateRect("Viewport", _frameRect);
        ConfigureRect(_viewportRect, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.80f));
        Image viewportImage = _viewportRect.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
        Mask mask = _viewportRect.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        _contentRect = CreateRect("CreditsContent", _viewportRect);
        _contentRect.anchorMin = new Vector2(0.5f, 1f);
        _contentRect.anchorMax = new Vector2(0.5f, 1f);
        _contentRect.pivot = new Vector2(0.5f, 1f);
        _contentRect.anchoredPosition = Vector2.zero;
        _contentRect.sizeDelta = new Vector2(_viewportRect.rect.width, 0f);

        VerticalLayoutGroup layout = _contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 56f;
        layout.padding = new RectOffset(40, 40, 24, 120);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = _contentRect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        EventTrigger trigger = _viewportRect.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.BeginDrag, () => _manualPauseTimer = 3f);
        AddTrigger(trigger, EventTriggerType.PointerEnter, () => _manualPauseTimer = 1.5f);
    }

    void BindData(TitleCreditsData data)
    {
        if (data == null)
            data = TitleCreditsData.CreateRuntimeFallback();

        _boundData = data;
        _scrollPixelsPerSecond = Mathf.Max(DefaultPixelsPerSecond, data.autoScrollNormalizedPerSecond * 2500f);
        _titleText.text = data.title;
        _subtitleText.text = data.subtitle;

        for (int i = _contentRect.childCount - 1; i >= 0; i--)
            Destroy(_contentRect.GetChild(i).gameObject);

        CreateIntroBlock();

        if (data.sections != null)
        {
            for (int i = 0; i < data.sections.Length; i++)
                BuildSection(data.sections[i], i);
        }

        CreateOutroBlock();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);
    }

    void CreateIntroBlock()
    {
        RectTransform block = CreateRect("IntroBlock", _contentRect);
        LayoutElement element = block.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 260f;

        VerticalLayoutGroup layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Text bigTitle = CreateText("BigTitle", block, 52, FontStyle.Bold, TextAnchor.MiddleCenter);
        bigTitle.text = _boundData != null ? _boundData.title : "\uAC1C\uBC1C\uC9C4";
        bigTitle.color = TextColor;
        FitPreferredHeight(bigTitle.rectTransform, 64f);

        Text subTitle = CreateText("BigSubtitle", block, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        subTitle.text = _boundData != null ? _boundData.subtitle : "\uD504\uB85C\uC81D\uD2B8 \uCD94\uC628";
        subTitle.color = MutedTextColor;
        FitPreferredHeight(subTitle.rectTransform, 72f);
    }

    void BuildSection(TitleCreditsData.Section section, int index)
    {
        RectTransform sectionRoot = CreateRect("Section_" + index.ToString("00"), _contentRect);
        LayoutElement sectionElement = sectionRoot.gameObject.AddComponent<LayoutElement>();
        sectionElement.preferredHeight = 360f;

        HorizontalLayoutGroup rowLayout = sectionRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 40f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.padding = new RectOffset(24, 24, 8, 8);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        RectTransform textColumn = CreateRect("TextColumn", sectionRoot);
        LayoutElement textColumnElement = textColumn.gameObject.AddComponent<LayoutElement>();
        textColumnElement.flexibleWidth = 1f;
        textColumnElement.preferredWidth = 680f;

        VerticalLayoutGroup textLayout = textColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        textLayout.spacing = 14f;
        textLayout.childAlignment = TextAnchor.MiddleCenter;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;

        Text heading = CreateText("Heading", textColumn, 34, FontStyle.Bold, TextAnchor.MiddleCenter);
        heading.text = section.heading;
        heading.color = AccentColor;
        FitPreferredHeight(heading.rectTransform, 48f);

        Text responsibilities = CreateText("Responsibilities", textColumn, 17, FontStyle.Normal, TextAnchor.MiddleCenter);
        responsibilities.text = section.responsibilities;
        responsibilities.color = MutedTextColor;
        FitPreferredHeight(responsibilities.rectTransform, 92f);

        Text members = CreateText("Members", textColumn, 25, FontStyle.Bold, TextAnchor.MiddleCenter);
        members.text = BuildMembersText(section);
        members.color = TextColor;
        FitPreferredHeight(members.rectTransform, 160f);

        RectTransform photoColumn = CreateRect("PhotoColumn", sectionRoot);
        LayoutElement photoColumnElement = photoColumn.gameObject.AddComponent<LayoutElement>();
        photoColumnElement.preferredWidth = 340f;
        photoColumnElement.minWidth = 300f;

        VerticalLayoutGroup photoLayout = photoColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        photoLayout.spacing = 18f;
        photoLayout.childAlignment = TextAnchor.MiddleCenter;
        photoLayout.childControlWidth = true;
        photoLayout.childControlHeight = true;
        photoLayout.childForceExpandWidth = true;
        photoLayout.childForceExpandHeight = false;

        TitleCreditsData.PhotoEntry[] photos = section.photos;
        if (photos == null || photos.Length == 0)
        {
            CreatePhotoCard(photoColumn, null, "\uAC1C\uBC1C \uACFC\uC815");
        }
        else
        {
            for (int i = 0; i < photos.Length; i++)
            {
                Sprite sprite = photos[i] != null ? photos[i].sprite : null;
                string caption = photos[i] != null ? photos[i].caption : "\uAC1C\uBC1C \uACFC\uC815";
                CreatePhotoCard(photoColumn, sprite, caption);
            }
        }
    }

    void CreateOutroBlock()
    {
        RectTransform block = CreateRect("OutroBlock", _contentRect);
        LayoutElement element = block.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 320f;

        VerticalLayoutGroup layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 16f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Text thankYou = CreateText("ThankYou", block, 34, FontStyle.Bold, TextAnchor.MiddleCenter);
        thankYou.text = "\uD50C\uB808\uC774\uD574 \uC8FC\uC154\uC11C \uAC10\uC0AC\uD569\uB2C8\uB2E4";
        thankYou.color = TextColor;
        FitPreferredHeight(thankYou.rectTransform, 44f);

        Text footer = CreateText("Footer", block, 18, FontStyle.Bold, TextAnchor.MiddleCenter);
        footer.text = "\uD504\uB85C\uC81D\uD2B8 \uCD94\uC628 / \uAC1C\uBC1C \uD06C\uB808\uB515";
        footer.color = MutedTextColor;
        FitPreferredHeight(footer.rectTransform, 42f);
    }

    void CreatePhotoCard(RectTransform parent, Sprite sprite, string caption)
    {
        RectTransform card = CreateRect("PhotoCard", parent);
        LayoutElement element = card.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 138f;

        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = CardColor;
        cardImage.raycastTarget = false;

        CreateLine("CardTop", card, new Vector2(0.04f, 0.96f), new Vector2(0.96f, 0.98f), new Color(FrameLineColor.r, FrameLineColor.g, FrameLineColor.b, 0.80f));
        CreateLine("CardBottom", card, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.04f), new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.40f));

        Image photo = CreateImage("Photo", card, sprite != null ? Color.white : new Color(0.08f, 0.12f, 0.18f, 1f));
        photo.sprite = sprite;
        photo.preserveAspect = true;
        photo.rectTransform.anchorMin = new Vector2(0.06f, 0.16f);
        photo.rectTransform.anchorMax = new Vector2(0.94f, 0.90f);
        photo.rectTransform.offsetMin = Vector2.zero;
        photo.rectTransform.offsetMax = Vector2.zero;

        Text captionText = CreateText("Caption", card, 13, FontStyle.Bold, TextAnchor.MiddleCenter);
        captionText.text = string.IsNullOrWhiteSpace(caption) ? "\uAC1C\uBC1C \uACFC\uC815" : caption;
        captionText.color = MutedTextColor;
        ConfigureRect(captionText.rectTransform, new Vector2(0.06f, 0.03f), new Vector2(0.94f, 0.14f));

        if (sprite == null)
        {
            Text placeholder = CreateText("Placeholder", card, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
            placeholder.text = "\uC0AC\uC9C4";
            placeholder.color = new Color(0.38f, 0.78f, 0.92f, 0.88f);
            placeholder.rectTransform.anchorMin = new Vector2(0.06f, 0.16f);
            placeholder.rectTransform.anchorMax = new Vector2(0.94f, 0.90f);
            placeholder.rectTransform.offsetMin = Vector2.zero;
            placeholder.rectTransform.offsetMax = Vector2.zero;
        }
    }

    void ResetScrollPosition()
    {
        if (_contentRect == null || _viewportRect == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);
        _contentRect.anchoredPosition = new Vector2(0f, -_viewportRect.rect.height * 0.35f);
    }

    string BuildMembersText(TitleCreditsData.Section section)
    {
        if (section.members == null || section.members.Length == 0)
            return "\uC774\uB984 \uC785\uB825";

        StringBuilder builder = new StringBuilder(128);
        for (int i = 0; i < section.members.Length; i++)
        {
            string member = section.members[i];
            if (string.IsNullOrWhiteSpace(member))
                continue;

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(member);
        }

        return builder.Length > 0 ? builder.ToString() : "\uC774\uB984 \uC785\uB825";
    }

    void FadeTo(float targetAlpha, bool show)
    {
        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(CoFade(targetAlpha, show));
    }

    IEnumerator CoFade(float targetAlpha, bool show)
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.interactable = true;

        while (elapsed < DefaultFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / DefaultFadeDuration));
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;
        _fadeRoutine = null;

        if (show)
            yield break;

        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;
        gameObject.SetActive(false);
    }

    static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    static Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text CreateText(string objectName, Transform parent, int fontSize, FontStyle fontStyle, TextAnchor anchor)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        Text text = go.GetComponent<Text>();
        text.font = RuntimeBuiltInFontUtility.GetDefaultFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        return text;
    }

    static Button CreateButton(string objectName, Transform parent, string label)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.09f, 0.16f, 0.24f, 0.96f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;

        Text labelText = CreateText("Label", rect, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
        labelText.color = TextColor;
        labelText.text = label;
        Stretch(labelText.rectTransform);
        return button;
    }

    static void ConfigureRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void FitPreferredHeight(RectTransform rectTransform, float minHeight)
    {
        ContentSizeFitter fitter = rectTransform.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement layoutElement = rectTransform.gameObject.AddComponent<LayoutElement>();
        layoutElement.minHeight = minHeight;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
            component = go.AddComponent<T>();
        return component;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    static void CreateLine(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
}
