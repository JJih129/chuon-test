using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TutorialDefenseFeedbackController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private TrainingDummyController defenseDummy;
    [SerializeField] private Canvas targetCanvas;

    [Header("Cue Panel")]
    [SerializeField] private Vector2 cueAnchoredPosition = new Vector2(0f, -118f);
    [SerializeField] private Vector2 cueSize = new Vector2(420f, 88f);

    [Header("Result Panel")]
    [SerializeField] private Vector2 resultAnchoredPosition = new Vector2(0f, 86f);
    [SerializeField] private Vector2 resultSize = new Vector2(360f, 72f);
    [SerializeField, Min(0.01f)] private float resultFadeInDuration = 0.08f;
    [SerializeField, Min(0.01f)] private float resultHoldDuration = 0.34f;
    [SerializeField, Min(0.01f)] private float resultFadeOutDuration = 0.22f;

    [Header("Colors")]
    [SerializeField] private Color guardColor = new Color(0.22f, 0.86f, 1f, 0.95f);
    [SerializeField] private Color parryColor = new Color(0.56f, 1f, 1f, 0.96f);
    [SerializeField] private Color dodgeColor = new Color(1f, 0.58f, 0.18f, 0.96f);
    [SerializeField] private Color perfectDodgeColor = new Color(1f, 0.34f, 0.16f, 0.98f);
    [SerializeField] private Color failureColor = new Color(1f, 0.34f, 0.22f, 0.96f);

    RectTransform _runtimeRoot;
    RectTransform _cueRoot;
    CanvasGroup _cueGroup;
    RawImage _cueBackground;
    RawImage _cueAccent;
    TextMeshProUGUI _cueTitle;
    TextMeshProUGUI _cueSubtitle;

    RectTransform _resultRoot;
    CanvasGroup _resultGroup;
    RawImage _resultBackground;
    RawImage _resultAccent;
    TextMeshProUGUI _resultText;
    Coroutine _resultRoutine;
    bool _subscribed;

    struct StepCueStyle
    {
        public string title;
        public string subtitle;
        public Color color;
    }

    struct ResultFeedback
    {
        public bool valid;
        public string text;
        public Color color;
    }

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        TrainingDummyController runtimeDefenseDummy,
        Canvas runtimeCanvas)
    {
        flowController = runtimeFlowController;
        defenseDummy = runtimeDefenseDummy;
        targetCanvas = runtimeCanvas;

        EnsureRuntimeVisuals();
        RefreshSubscriptions();
        ApplyCue(flowController != null ? flowController.CurrentStep : null);
    }

    void OnEnable()
    {
        EnsureRuntimeVisuals();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        HideImmediate();
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();

        if (flowController != null)
        {
            flowController.StepStarted += HandleStepStarted;
            flowController.StepCompleted += HandleStepCompleted;
            _subscribed = true;
        }

        if (defenseDummy != null)
            defenseDummy.AttackResolved += HandleAttackResolved;
    }

    void ReleaseSubscriptions()
    {
        if (_subscribed && flowController != null)
        {
            flowController.StepStarted -= HandleStepStarted;
            flowController.StepCompleted -= HandleStepCompleted;
        }

        if (defenseDummy != null)
            defenseDummy.AttackResolved -= HandleAttackResolved;

        _subscribed = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        ApplyCue(step);
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (!IsDefenseStep(step != null ? step.stepType : TutorialStepType.Movement))
            return;

        if (_cueGroup != null)
            _cueGroup.alpha = 0f;
    }

    void HandleAttackResolved(TrainingDummyController dummy, TrainingDummyAttackResult result)
    {
        if (flowController == null || flowController.CurrentStep == null)
            return;

        TutorialStepType stepType = flowController.CurrentStep.stepType;
        if (!IsDefenseStep(stepType))
            return;

        ResultFeedback feedback = ResolveFeedback(stepType, result);
        if (!feedback.valid)
            return;

        ShowResult(feedback);
    }

    void ApplyCue(TutorialStepDefinition step)
    {
        EnsureRuntimeVisuals();
        if (_cueGroup == null || _cueTitle == null || _cueSubtitle == null)
            return;

        if (step == null || !IsDefenseStep(step.stepType))
        {
            _cueGroup.alpha = 0f;
            return;
        }

        StepCueStyle style = ResolveCueStyle(step.stepType);
        _cueTitle.text = style.title;
        _cueSubtitle.text = style.subtitle;
        _cueTitle.color = style.color;
        _cueSubtitle.color = new Color(0.92f, 0.98f, 1f, 0.94f);
        _cueBackground.color = new Color(style.color.r, style.color.g, style.color.b, 0.14f);
        _cueAccent.color = style.color;
        _cueGroup.alpha = 1f;
        _cueRoot.SetAsLastSibling();
    }

    StepCueStyle ResolveCueStyle(TutorialStepType stepType)
    {
        switch (stepType)
        {
            case TutorialStepType.Guard:
                return new StepCueStyle
                {
                    title = "대응: 방어",
                    subtitle = "붉은 경고선을 받아내",
                    color = guardColor
                };

            case TutorialStepType.Parry:
                return new StepCueStyle
                {
                    title = "대응: 패링",
                    subtitle = "경고가 겹치는 순간 끊어내",
                    color = parryColor
                };

            case TutorialStepType.Dodge:
                return new StepCueStyle
                {
                    title = "대응: 회피",
                    subtitle = "경고선 방향에서 벗어나",
                    color = dodgeColor
                };

            case TutorialStepType.PerfectDodge:
                return new StepCueStyle
                {
                    title = "대응: 퍼펙트 회피",
                    subtitle = "발사 직전에 한 박자 늦게",
                    color = perfectDodgeColor
                };

            default:
                return default;
        }
    }

    ResultFeedback ResolveFeedback(TutorialStepType stepType, TrainingDummyAttackResult result)
    {
        switch (stepType)
        {
            case TutorialStepType.Guard:
                return ResolveGuardFeedback(result);

            case TutorialStepType.Parry:
                return ResolveParryFeedback(result);

            case TutorialStepType.Dodge:
                return ResolveDodgeFeedback(result);

            case TutorialStepType.PerfectDodge:
                return ResolvePerfectDodgeFeedback(result);

            default:
                return default;
        }
    }

    ResultFeedback ResolveGuardFeedback(TrainingDummyAttackResult result)
    {
        switch (result)
        {
            case TrainingDummyAttackResult.Guarded:
                return BuildFeedback("방어 성공", guardColor);

            case TrainingDummyAttackResult.Hit:
                return BuildFeedback("조금 더 일찍 방어", failureColor);

            case TrainingDummyAttackResult.Dodged:
            case TrainingDummyAttackResult.PerfectDodged:
                return BuildFeedback("이번엔 막아내는 감각을 확인해", failureColor);

            default:
                return default;
        }
    }

    ResultFeedback ResolveParryFeedback(TrainingDummyAttackResult result)
    {
        switch (result)
        {
            case TrainingDummyAttackResult.Parried:
                return BuildFeedback("패링 성공", parryColor);

            case TrainingDummyAttackResult.Guarded:
                return BuildFeedback("막기는 했어. 조금 더 늦게", failureColor);

            case TrainingDummyAttackResult.Hit:
                return BuildFeedback("경고가 겹칠 때 맞춰", failureColor);

            case TrainingDummyAttackResult.Dodged:
            case TrainingDummyAttackResult.PerfectDodged:
                return BuildFeedback("회피 말고 끊어내", failureColor);

            default:
                return default;
        }
    }

    ResultFeedback ResolveDodgeFeedback(TrainingDummyAttackResult result)
    {
        switch (result)
        {
            case TrainingDummyAttackResult.Dodged:
                return BuildFeedback("회피 성공", dodgeColor);

            case TrainingDummyAttackResult.PerfectDodged:
                return BuildFeedback("좋아. 더 여유롭게 흘렸어", perfectDodgeColor);

            case TrainingDummyAttackResult.Hit:
                return BuildFeedback("측면으로 빠져", failureColor);

            case TrainingDummyAttackResult.Guarded:
            case TrainingDummyAttackResult.Parried:
                return BuildFeedback("이번엔 막지 말고 벗어나", failureColor);

            default:
                return default;
        }
    }

    ResultFeedback ResolvePerfectDodgeFeedback(TrainingDummyAttackResult result)
    {
        switch (result)
        {
            case TrainingDummyAttackResult.PerfectDodged:
                return BuildFeedback("퍼펙트 회피", perfectDodgeColor);

            case TrainingDummyAttackResult.Dodged:
                return BuildFeedback("회피는 성공. 한 박자 늦게", failureColor);

            case TrainingDummyAttackResult.Hit:
                return BuildFeedback("발사 직전에 맞춰", failureColor);

            case TrainingDummyAttackResult.Guarded:
            case TrainingDummyAttackResult.Parried:
                return BuildFeedback("이번엔 회피 타이밍을 본다", failureColor);

            default:
                return default;
        }
    }

    ResultFeedback BuildFeedback(string text, Color color)
    {
        return new ResultFeedback
        {
            valid = !string.IsNullOrWhiteSpace(text),
            text = text,
            color = color
        };
    }

    void ShowResult(ResultFeedback feedback)
    {
        EnsureRuntimeVisuals();
        if (_resultGroup == null || _resultText == null)
            return;

        if (_resultRoutine != null)
            StopCoroutine(_resultRoutine);

        _resultText.text = feedback.text;
        _resultText.color = feedback.color;
        _resultBackground.color = new Color(feedback.color.r, feedback.color.g, feedback.color.b, 0.14f);
        _resultAccent.color = feedback.color;
        _resultRoot.SetAsLastSibling();
        _resultRoutine = StartCoroutine(CoShowResult());
    }

    IEnumerator CoShowResult()
    {
        _resultGroup.alpha = 0f;
        _resultRoot.localScale = new Vector3(0.96f, 0.96f, 1f);

        yield return FadeCanvasGroup(_resultGroup, 0f, 1f, resultFadeInDuration);

        float holdEnd = Time.unscaledTime + resultHoldDuration;
        while (Time.unscaledTime < holdEnd)
        {
            float normalized = resultHoldDuration <= 0.001f
                ? 1f
                : 1f - ((holdEnd - Time.unscaledTime) / resultHoldDuration);
            float scale = Mathf.LerpUnclamped(1.05f, 1f, EaseOutCubic(Mathf.Clamp01(normalized)));
            _resultRoot.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        _resultRoot.localScale = Vector3.one;
        yield return FadeCanvasGroup(_resultGroup, _resultGroup.alpha, 0f, resultFadeOutDuration);
        _resultRoutine = null;
    }

    IEnumerator FadeCanvasGroup(CanvasGroup canvasGroup, float from, float to, float duration)
    {
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);
        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            canvasGroup.alpha = Mathf.Lerp(from, to, EaseOutCubic(t));
            yield return null;
        }

        canvasGroup.alpha = to;
    }

    void EnsureRuntimeVisuals()
    {
        if (targetCanvas == null)
            return;

        if (_runtimeRoot == null)
        {
            Transform existing = targetCanvas.transform.Find("TutorialDefenseFeedbackRuntime");
            if (existing != null)
                _runtimeRoot = existing as RectTransform;

            if (_runtimeRoot == null)
            {
                GameObject rootObject = new GameObject("TutorialDefenseFeedbackRuntime", typeof(RectTransform));
                _runtimeRoot = rootObject.GetComponent<RectTransform>();
                _runtimeRoot.SetParent(targetCanvas.transform, false);
            }

            StretchToParent(_runtimeRoot);
            _runtimeRoot.SetAsLastSibling();
        }

        EnsureCuePanel();
        EnsureResultPanel();
    }

    void EnsureCuePanel()
    {
        if (_cueRoot == null)
        {
            Transform existing = _runtimeRoot.Find("CuePanel");
            if (existing != null)
                _cueRoot = existing as RectTransform;

            if (_cueRoot == null)
            {
                GameObject rootObject = new GameObject("CuePanel", typeof(RectTransform), typeof(CanvasGroup));
                _cueRoot = rootObject.GetComponent<RectTransform>();
                _cueRoot.SetParent(_runtimeRoot, false);
            }

            _cueRoot.anchorMin = new Vector2(0.5f, 1f);
            _cueRoot.anchorMax = new Vector2(0.5f, 1f);
            _cueRoot.pivot = new Vector2(0.5f, 1f);
            _cueRoot.anchoredPosition = cueAnchoredPosition;
            _cueRoot.sizeDelta = cueSize;

            _cueGroup = _cueRoot.GetComponent<CanvasGroup>();
            if (_cueGroup == null)
                _cueGroup = _cueRoot.gameObject.AddComponent<CanvasGroup>();
            _cueGroup.alpha = 0f;
        }

        _cueBackground = EnsurePanelImage(_cueRoot, "Background", out RectTransform cueBackgroundRect);
        StretchToParent(cueBackgroundRect);

        _cueAccent = EnsurePanelImage(_cueRoot, "Accent", out RectTransform cueAccentRect);
        cueAccentRect.anchorMin = new Vector2(0f, 0f);
        cueAccentRect.anchorMax = new Vector2(0f, 1f);
        cueAccentRect.pivot = new Vector2(0f, 0.5f);
        cueAccentRect.anchoredPosition = Vector2.zero;
        cueAccentRect.sizeDelta = new Vector2(8f, 0f);

        _cueTitle = EnsureText(_cueRoot, "Title");
        RectTransform cueTitleRect = _cueTitle.rectTransform;
        cueTitleRect.anchorMin = new Vector2(0f, 0.5f);
        cueTitleRect.anchorMax = new Vector2(1f, 1f);
        cueTitleRect.offsetMin = new Vector2(24f, 6f);
        cueTitleRect.offsetMax = new Vector2(-20f, -8f);
        _cueTitle.alignment = TextAlignmentOptions.Left;
        _cueTitle.fontSize = 28f;
        _cueTitle.fontStyle = FontStyles.Bold;
        _cueTitle.raycastTarget = false;

        _cueSubtitle = EnsureText(_cueRoot, "Subtitle");
        RectTransform cueSubtitleRect = _cueSubtitle.rectTransform;
        cueSubtitleRect.anchorMin = new Vector2(0f, 0f);
        cueSubtitleRect.anchorMax = new Vector2(1f, 0.58f);
        cueSubtitleRect.offsetMin = new Vector2(24f, 10f);
        cueSubtitleRect.offsetMax = new Vector2(-20f, -10f);
        _cueSubtitle.alignment = TextAlignmentOptions.Left;
        _cueSubtitle.fontSize = 18f;
        _cueSubtitle.raycastTarget = false;
    }

    void EnsureResultPanel()
    {
        if (_resultRoot == null)
        {
            Transform existing = _runtimeRoot.Find("ResultPanel");
            if (existing != null)
                _resultRoot = existing as RectTransform;

            if (_resultRoot == null)
            {
                GameObject rootObject = new GameObject("ResultPanel", typeof(RectTransform), typeof(CanvasGroup));
                _resultRoot = rootObject.GetComponent<RectTransform>();
                _resultRoot.SetParent(_runtimeRoot, false);
            }

            _resultRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _resultRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _resultRoot.pivot = new Vector2(0.5f, 0.5f);
            _resultRoot.anchoredPosition = resultAnchoredPosition;
            _resultRoot.sizeDelta = resultSize;

            _resultGroup = _resultRoot.GetComponent<CanvasGroup>();
            if (_resultGroup == null)
                _resultGroup = _resultRoot.gameObject.AddComponent<CanvasGroup>();
            _resultGroup.alpha = 0f;
        }

        _resultBackground = EnsurePanelImage(_resultRoot, "Background", out RectTransform resultBackgroundRect);
        StretchToParent(resultBackgroundRect);

        _resultAccent = EnsurePanelImage(_resultRoot, "Accent", out RectTransform resultAccentRect);
        resultAccentRect.anchorMin = new Vector2(0f, 0f);
        resultAccentRect.anchorMax = new Vector2(1f, 0f);
        resultAccentRect.pivot = new Vector2(0.5f, 0f);
        resultAccentRect.anchoredPosition = Vector2.zero;
        resultAccentRect.sizeDelta = new Vector2(0f, 6f);

        _resultText = EnsureText(_resultRoot, "Label");
        RectTransform resultTextRect = _resultText.rectTransform;
        resultTextRect.anchorMin = Vector2.zero;
        resultTextRect.anchorMax = Vector2.one;
        resultTextRect.offsetMin = new Vector2(18f, 12f);
        resultTextRect.offsetMax = new Vector2(-18f, -10f);
        _resultText.alignment = TextAlignmentOptions.Center;
        _resultText.fontSize = 28f;
        _resultText.fontStyle = FontStyles.Bold;
        _resultText.raycastTarget = false;
    }

    RawImage EnsurePanelImage(Transform parent, string objectName, out RectTransform rectTransform)
    {
        Transform existing = parent.Find(objectName);
        RawImage image = existing != null ? existing.GetComponent<RawImage>() : null;
        if (image == null)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageObject.transform.SetParent(parent, false);
            image = imageObject.GetComponent<RawImage>();
        }

        rectTransform = image.rectTransform;
        image.texture = Texture2D.whiteTexture;
        image.raycastTarget = false;
        return image;
    }

    TextMeshProUGUI EnsureText(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        TextMeshProUGUI text = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (text == null)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            text = textObject.GetComponent<TextMeshProUGUI>();
        }

        return text;
    }

    void HideImmediate()
    {
        if (_resultRoutine != null)
        {
            StopCoroutine(_resultRoutine);
            _resultRoutine = null;
        }

        if (_cueGroup != null)
            _cueGroup.alpha = 0f;
        if (_resultGroup != null)
            _resultGroup.alpha = 0f;
        if (_resultRoot != null)
            _resultRoot.localScale = Vector3.one;
    }

    static bool IsDefenseStep(TutorialStepType stepType)
    {
        return stepType == TutorialStepType.Guard ||
               stepType == TutorialStepType.Parry ||
               stepType == TutorialStepType.Dodge ||
               stepType == TutorialStepType.PerfectDodge;
    }

    static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t -= 1f;
        return t * t * t + 1f;
    }
}
