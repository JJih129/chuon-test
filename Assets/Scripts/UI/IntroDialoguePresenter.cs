using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class IntroDialoguePresenter : MonoBehaviour
{
    // 화자별 이름 색 (EGO 시안 / 추온 흰색 / 시스템·Type-D 레드 / 츠카사 레드)
    static readonly Color EgoColor = new Color(0f, 0.85f, 1f, 1f);
    static readonly Color ChuOnColor = new Color(0.96f, 0.96f, 1f, 1f);
    static readonly Color HostileColor = new Color(1f, 0.28f, 0.22f, 1f);

    /// <summary>현재 대사/bark 가 화면에 떠 있는가 (bark 중복 방지용).</summary>
    public static bool AnyVisible { get; private set; }

    Coroutine _overlayRoutine;
    bool _blockingLineActive;
    float _blockingStartedAt;
    int _generation;

    [SerializeField] Vector2 anchor = new Vector2(0.5f, 0.18f);
    [SerializeField] Vector2 size = new Vector2(920f, 112f);
    [SerializeField] Color speakerColor = new Color(0f, 0.85f, 1f, 1f);
    [SerializeField] Color textColor = Color.white;
    [SerializeField, Min(0f)] float typingSpeed = 0.018f;
    [SerializeField] Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] Vector2 subtitleScaleRange = new Vector2(0.6f, 1.8f);

    const int SpeakerFontSize = 23;
    const int ContentFontSize = 27;

    readonly StringBuilder _builder = new StringBuilder(96);
    CanvasGroup _group;
    RectTransform _root;
    Image _background;
    Text _speakerText;
    Text _contentText;
    WaitForSeconds _typingWait;
    float _typingWaitSeconds = -1f;

    void OnEnable()
    {
        // 옵션 메뉴의 자막 크기/배경 설정 반영
        OptionsManagerAdvanced.SubtitleSizeChangedGlobal += ApplySubtitleSize;
        OptionsManagerAdvanced.SubtitleBackgroundChangedGlobal += ApplySubtitleBackground;
        ApplySubtitleOptions();
    }

    void OnDisable()
    {
        OptionsManagerAdvanced.SubtitleSizeChangedGlobal -= ApplySubtitleSize;
        OptionsManagerAdvanced.SubtitleBackgroundChangedGlobal -= ApplySubtitleBackground;
    }

    void ApplySubtitleOptions()
    {
        ApplySubtitleSize(OptionsManagerAdvanced.CurrentSubtitleSize);
        ApplySubtitleBackground(OptionsManagerAdvanced.CurrentSubtitleBackground);
    }

    void ApplySubtitleSize(float value)
    {
        float scale = Mathf.Clamp(value, subtitleScaleRange.x, subtitleScaleRange.y);
        if (_speakerText != null)
            _speakerText.fontSize = Mathf.RoundToInt(SpeakerFontSize * scale);
        if (_contentText != null)
            _contentText.fontSize = Mathf.RoundToInt(ContentFontSize * scale);
        if (_root != null)
            _root.sizeDelta = new Vector2(size.x, size.y * Mathf.Max(1f, scale));
    }

    void ApplySubtitleBackground(bool visible)
    {
        if (_background != null)
            _background.enabled = visible;
    }

    public IEnumerator CoShow(Canvas canvas, string speaker, string text, float hold, float delayAfter)
    {
        EnsureUi(canvas);
        if (_group == null)
            yield break;

        StopOverlay();
        int generation = ++_generation;
        _blockingLineActive = true;
        _blockingStartedAt = Time.unscaledTime;
        _group.alpha = 1f;
        AnyVisible = true;
        ApplySpeaker(speaker);

        bool skipped = false;
        if (_contentText != null)
        {
            _contentText.text = string.Empty;
            _builder.Clear();

            string content = text ?? string.Empty;
            for (int i = 0; i < content.Length; i++)
            {
                if (generation != _generation)
                    yield break; // Hide()/오버레이로 교체됨: 오래된 줄이 텍스트를 덮어쓰지 않게

                // Enter/Space: 타이핑 즉시 완료 (기존 건너뛰기 동작과 동일한 키)
                if (WasAdvancePressed())
                {
                    _contentText.text = content;
                    skipped = true;
                    break;
                }

                _builder.Append(content[i]);
                _contentText.text = _builder.ToString();

                if (typingSpeed > 0f)
                {
                    if (_typingWait == null || !Mathf.Approximately(_typingWaitSeconds, typingSpeed))
                    {
                        _typingWaitSeconds = typingSpeed;
                        _typingWait = new WaitForSeconds(typingSpeed);
                    }

                    yield return _typingWait;
                }
            }
        }

        // 유지 시간 동안 다시 누르면 다음 줄로 넘어간다.
        float wait = Mathf.Max(0.1f, hold) + Mathf.Max(0f, delayAfter);
        float elapsed = 0f;
        if (skipped)
            yield return null; // 같은 프레임 입력이 바로 다음 줄까지 넘기지 않게
        while (elapsed < wait)
        {
            if (generation != _generation)
                yield break;
            if (elapsed > 0.15f && WasAdvancePressed())
                break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        _blockingLineActive = false;
    }

    public void Hide()
    {
        _generation++;
        StopOverlay();
        _blockingLineActive = false;
        if (_group != null)
            _group.alpha = 0f;
        AnyVisible = false;
    }

    /// <summary>
    /// 조작을 막지 않는 자막 한 줄 (전투 bark, 타임라인 종료 후 남은 인트로 대사).
    /// 실시간 기준, 현재 overlay 는 교체한다. 블로킹 인트로 대사가 재생 중이면 무시.
    /// </summary>
    public bool ShowOverlayLine(Canvas canvas, string speaker, string text, float hold)
    {
        // 블로킹 코루틴이 외부에서 끊겨 플래그가 남은 경우를 대비해 오래된 플래그는 무시한다.
        if (_blockingLineActive && Time.unscaledTime - _blockingStartedAt < 12f)
            return false;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        _blockingLineActive = false;
        _generation++;

        EnsureUi(canvas);
        if (_group == null)
            return false;

        StopOverlay();
        _overlayRoutine = StartCoroutine(CoOverlayLine(speaker, text, hold));
        return true;
    }

    public bool IsOverlayPlaying => _overlayRoutine != null;
    public bool IsBlockingLinePlaying => _blockingLineActive;

    void StopOverlay()
    {
        if (_overlayRoutine == null)
            return;

        StopCoroutine(_overlayRoutine);
        _overlayRoutine = null;
    }

    IEnumerator CoOverlayLine(string speaker, string text, float hold)
    {
        ApplySpeaker(speaker);
        AnyVisible = true;
        if (_contentText != null)
            _contentText.text = string.Empty;

        // 짧은 페이드 인 + 빠른 타이핑
        string content = text ?? string.Empty;
        float typeTime = 0f;
        float charsPerSecond = typingSpeed > 0f ? Mathf.Max(30f, 1f / typingSpeed) : 999f;
        _builder.Clear();
        int shown = 0;
        while (shown < content.Length)
        {
            typeTime += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(typeTime / 0.12f);
            int target = Mathf.Min(content.Length, Mathf.FloorToInt(typeTime * charsPerSecond) + 1);
            if (target != shown)
            {
                shown = target;
                if (_contentText != null)
                    _contentText.text = content.Substring(0, shown);
            }

            yield return null;
        }

        _group.alpha = 1f;
        float elapsed = 0f;
        float total = Mathf.Max(0.6f, hold);
        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        const float fadeOut = 0.25f;
        float fade = 0f;
        while (fade < fadeOut)
        {
            fade += Time.unscaledDeltaTime;
            _group.alpha = 1f - Mathf.Clamp01(fade / fadeOut);
            yield return null;
        }

        _group.alpha = 0f;
        AnyVisible = false;
        _overlayRoutine = null;
    }

    void ApplySpeaker(string speaker)
    {
        if (_speakerText == null)
            return;

        _speakerText.text = speaker ?? string.Empty;
        _speakerText.color = ResolveSpeakerColor(speaker);
    }

    Color ResolveSpeakerColor(string speaker)
    {
        if (string.IsNullOrEmpty(speaker))
            return speakerColor;
        if (speaker == "EGO" || speaker == "E.G.O")
            return EgoColor;
        if (speaker == "추온" || speaker == "ChuOn")
            return ChuOnColor;
        if (speaker.StartsWith("Type-D") || speaker == "System" || speaker == "시스템" || speaker == "츠카사")
            return HostileColor;
        return speakerColor;
    }

    static bool WasAdvancePressed()
    {
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            || pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    /// <summary>캔버스 안의 presenter 를 찾거나 만든다 (bark/overlay 공용).</summary>
    public static IntroDialoguePresenter ResolveFor(Canvas canvas)
    {
        if (canvas == null)
            return null;

        IntroDialoguePresenter presenter = canvas.GetComponentInChildren<IntroDialoguePresenter>(true);
        if (presenter != null)
            return presenter;

        GameObject presenterObject = new GameObject("IntroDialoguePresenter");
        presenterObject.transform.SetParent(canvas.transform, false);
        return presenterObject.AddComponent<IntroDialoguePresenter>();
    }

    void OnDestroy()
    {
        if (_group != null && _group.alpha > 0.01f)
            AnyVisible = false;
    }

    void EnsureUi(Canvas canvas)
    {
        if (_group != null || canvas == null)
            return;

        GameObject rootObject = new GameObject("IntroDialogueRuntime", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.anchorMin = anchor;
        root.anchorMax = anchor;
        // 반복 10: 아래쪽 기준으로 위로만 커지게 (큰 자막이 하단 HP 게이지를 덮지 않게). 크기 1 에서는 기존 위치와 같다.
        root.pivot = new Vector2(0.5f, 0f);
        root.sizeDelta = size;
        root.anchoredPosition = new Vector2(0f, -size.y * 0.5f);
        _root = root;

        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.SetParent(root, false);
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = new Vector2(-24f, -10f);
        backgroundRect.offsetMax = new Vector2(24f, 10f);
        _background = backgroundObject.GetComponent<Image>();
        _background.color = backgroundColor;
        _background.raycastTarget = false;

        _group = rootObject.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        _speakerText = CreateText(root, "Speaker", SpeakerFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, speakerColor, new Vector2(0f, 0.58f), Vector2.one);
        _contentText = CreateText(root, "Content", ContentFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, textColor, Vector2.zero, new Vector2(1f, 0.72f));
        ApplySubtitleOptions();
    }

    static Text CreateText(RectTransform parent, string name, int fontSize, FontStyle style, TextAnchor alignment, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
