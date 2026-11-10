using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class IntroDialoguePresenter : MonoBehaviour
{
    // 화자별 이름 색 (반복 16 E7: EGO 시안 → 스틸 화이트 / 추온 세라믹 화이트 / 시스템·Type-D·츠카사 텍스트용 레드). 공용 팔레트 = ChuonUiPalette
    static readonly Color EgoColor = ChuonUiPalette.EgoSteel;
    static readonly Color ChuOnColor = ChuonUiPalette.CeramicWhite;
    static readonly Color HostileColor = ChuonUiPalette.TsukasaRedText;

    /// <summary>현재 대사/bark 가 화면에 떠 있는가 (bark 중복 방지용).</summary>
    public static bool AnyVisible { get; private set; }

    Coroutine _overlayRoutine;
    bool _blockingLineActive;
    float _blockingStartedAt;
    int _generation;

    [SerializeField] Vector2 anchor = new Vector2(0.5f, 0.18f);
    [SerializeField] Vector2 size = new Vector2(920f, 112f);
    [SerializeField] Color speakerColor = new Color(0.68f, 0.79f, 0.90f, 1f);
    [SerializeField] Color textColor = Color.white;
    [SerializeField, Min(0f)] float typingSpeed = 0.018f;
    [SerializeField] Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] Vector2 subtitleScaleRange = new Vector2(0.6f, 1.8f);

    const int SpeakerFontSize = 23;
    const int ContentFontSize = 27;

    // 반복 16 (플레이어 리뷰 #6): 조작 중 오버레이 줄(전투 bark, 리빌 뒤 남은 인트로 대사)은 화면 하단 중앙(캐릭터 하체·보스와 겹침)
    // 대신 오른쪽 아래 '통신 패널' 안전 영역에 작게 띄운다. 블로킹 대사(CoShow)와 연출 자막(cinematicHost)은 기존 하단 중앙 그대로.
    enum Layout : byte { None, Standard, Comms }
    // 반복 16 (가독성): 17/22 → 20/24, 패널 600x74 → 640x84 (1080p 기준)
    const int CommsSpeakerFontSize = 20;
    const int CommsContentFontSize = 24;
    static readonly Vector2 CommsSize = new Vector2(640f, 84f);
    static readonly Vector2 CommsMargin = new Vector2(48f, 44f);
    static readonly Color CommsBackground = new Color(0.035f, 0.035f, 0.045f, 0.7f);
    static readonly Color CommsAccent = new Color(1f, 0.16f, 0.12f, 0.95f);

    [Tooltip("연출 오버레이 캔버스의 자막 호스트: 오버레이 줄도 하단 중앙(연출 자막) 배치를 쓴다.")]
    [SerializeField] bool cinematicHost;

    readonly StringBuilder _builder = new StringBuilder(96);
    CanvasGroup _group;
    RectTransform _root;
    Image _background;
    Image _commsAccent;
    Text _speakerText;
    Text _contentText;
    WaitForSeconds _typingWait;
    float _typingWaitSeconds = -1f;
    Layout _layout = Layout.None;

    /// <summary>연출 자막 호스트로 지정 (CinematicOverlay). 오버레이 줄도 하단 중앙 자막 배치.</summary>
    public bool CinematicHost
    {
        get => cinematicHost;
        set => cinematicHost = value;
    }

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
        bool comms = _layout == Layout.Comms;
        if (_speakerText != null)
            _speakerText.fontSize = Mathf.RoundToInt((comms ? CommsSpeakerFontSize : SpeakerFontSize) * scale);
        if (_contentText != null)
            _contentText.fontSize = Mathf.RoundToInt((comms ? CommsContentFontSize : ContentFontSize) * scale);
        if (_root != null)
        {
            Vector2 baseSize = comms ? CommsSize : size;
            _root.sizeDelta = new Vector2(baseSize.x, baseSize.y * Mathf.Max(1f, scale));
        }
    }

    /// <summary>배치 전환 (값이 바뀔 때만). Standard = 기존 하단 중앙, Comms = 오른쪽 아래 통신 패널.</summary>
    void ApplyLayout(Layout layout)
    {
        if (_root == null || layout == _layout)
            return;
        _layout = layout;
        bool comms = layout == Layout.Comms;

        if (comms)
        {
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = new Vector2(-CommsMargin.x, CommsMargin.y);
        }
        else
        {
            _root.anchorMin = _root.anchorMax = anchor;
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, -size.y * 0.5f);
        }

        if (_background != null)
        {
            _background.color = comms ? CommsBackground : backgroundColor;
            RectTransform bg = _background.rectTransform;
            bg.offsetMin = comms ? new Vector2(-18f, -8f) : new Vector2(-24f, -10f);
            bg.offsetMax = comms ? new Vector2(14f, 8f) : new Vector2(24f, 10f);
        }

        if (comms && _commsAccent == null && _background != null)
        {
            // 오른쪽 끝 레드 헤어라인 (츠카사 통신 패널 문법)
            var accentObject = new GameObject("CommsAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform accent = accentObject.GetComponent<RectTransform>();
            accent.SetParent(_background.rectTransform, false);
            accent.anchorMin = new Vector2(1f, 0f);
            accent.anchorMax = new Vector2(1f, 1f);
            accent.pivot = new Vector2(1f, 0.5f);
            accent.sizeDelta = new Vector2(3f, -10f);
            accent.anchoredPosition = Vector2.zero;
            _commsAccent = accentObject.GetComponent<Image>();
            _commsAccent.color = CommsAccent;
            _commsAccent.raycastTarget = false;
        }
        if (_commsAccent != null)
            _commsAccent.enabled = comms;

        TextAnchor align = comms ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
        if (_speakerText != null)
        {
            _speakerText.alignment = align;
            RectTransform r = _speakerText.rectTransform;
            r.anchorMin = comms ? new Vector2(0f, 0.6f) : new Vector2(0f, 0.58f);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }
        if (_contentText != null)
        {
            _contentText.alignment = align;
            RectTransform r = _contentText.rectTransform;
            r.anchorMax = comms ? new Vector2(1f, 0.66f) : new Vector2(1f, 0.72f);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        ApplySubtitleOptions();
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

        ApplyLayout(Layout.Standard);
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

        ApplyLayout(cinematicHost ? Layout.Standard : Layout.Comms);
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
        _layout = Layout.Standard;
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
        text.font = RuntimeBuiltInFontUtility.GetDefaultFont(); // 반복 16 (E8): Arial+OS 대체 → Noto Sans KR Medium
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
