using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TutorialPresentationController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private Canvas targetCanvas;

    [Header("Step Flash")]
    [SerializeField] private string cameraFlashText = "\uc2dc\uc57c\u0020\ud655\ubcf4";
    [SerializeField] private string lockOnFlashText = "\ub77d\uc628\u0020\uc644\ub8cc";
    [SerializeField] private string basicAttackFlashText = "\uae30\ubcf8 \uacf5\uaca9";
    [SerializeField] private string comboFlashText = "\ucf64\ubcf4 \uc5f0\uacb0";
    [SerializeField] private string parryFlashText = "\ud328\ub9c1\u0020\uc131\uacf5";
    [SerializeField] private string perfectDodgeFlashText = "\ud37c\ud399\ud2b8\u0020\ud68c\ud53c";
    [SerializeField] private string healFlashText = "\ud68c\ubcf5\u0020\uc644\ub8cc";
    [SerializeField] private string ultimateFlashText = "\uad81\uadf9\uae30\u0020\ubc1c\ub3d9";
    [SerializeField] private string exitFlashText = "\ud604\uc2e4\u0020\ubcf5\uadc0";
    [SerializeField] private Color cameraFlashColor = new Color(0.24f, 0.88f, 1.00f, 1f);
    [SerializeField] private Color lockOnFlashColor = new Color(1.00f, 0.78f, 0.22f, 1f);
    [SerializeField] private Color basicAttackFlashColor = new Color(0.96f, 0.82f, 0.28f, 1f);
    [SerializeField] private Color comboFlashColor = new Color(1.00f, 0.62f, 0.18f, 1f);
    [SerializeField] private Color parryFlashColor = new Color(0.32f, 0.96f, 1.00f, 1f);
    [SerializeField] private Color perfectDodgeFlashColor = new Color(1.00f, 0.52f, 0.22f, 1f);
    [SerializeField] private Color healFlashColor = new Color(0.36f, 1.00f, 0.58f, 1f);
    [SerializeField] private Color ultimateFlashColor = new Color(1.00f, 0.78f, 0.22f, 1f);
    [SerializeField] private Color exitFlashColor = new Color(0.82f, 0.96f, 1.00f, 1f);
    [SerializeField] private Vector2 flashAnchoredPosition = new Vector2(0f, 118f);
    [SerializeField] private Vector2 flashSize = new Vector2(420f, 84f);
    [SerializeField, Min(0.01f)] private float flashFadeInDuration = 0.08f;
    [SerializeField, Min(0f)] private float flashHoldDuration = 0.16f;
    [SerializeField, Min(0.01f)] private float flashFadeOutDuration = 0.22f;

    [Header("Outro Overlay")]
    [SerializeField] private string outroTitle = "\uac00\uc0c1\ud6c8\ub828\u0020\ud574\uc81c";
    [SerializeField] private string outroSubtitle = "\ucd9c\uad6c\ub85c\u0020\uc774\ub3d9\ud574\u0020\ub2e4\uc74c\u0020\uad6c\uac04\uc73c\ub85c\u0020\uc9c4\uc785";
    [SerializeField] private Color outroOverlayColor = new Color(0.02f, 0.07f, 0.10f, 0.86f);
    [SerializeField] private Color outroAccentColor = new Color(0.20f, 0.88f, 1.00f, 1f);
    [SerializeField, Min(0.01f)] private float outroFadeInDuration = 0.16f;
    [SerializeField, Min(0f)] private float outroHoldDuration = 0.55f;
    [SerializeField, Min(0.01f)] private float outroFadeOutDuration = 0.28f;
    [SerializeField] private float outroScanlineTravel = 110f;

    [Header("Audio")]
    [SerializeField] private AudioSource effectAudioSource;
    [SerializeField, Range(0f, 1f)] private float successToneVolume = 0.32f;
    [SerializeField, Range(0f, 1f)] private float outroToneVolume = 0.42f;
    [SerializeField, Min(120f)] private float parryToneFrequency = 980f;
    [SerializeField, Min(120f)] private float perfectDodgeToneFrequency = 1120f;
    [SerializeField, Min(120f)] private float healToneFrequency = 720f;
    [SerializeField, Min(120f)] private float ultimateToneFrequency = 560f;
    [SerializeField, Min(120f)] private float outroToneFrequency = 420f;
    [SerializeField, Min(0.02f)] private float successToneDuration = 0.07f;
    [SerializeField, Min(0.02f)] private float outroToneDuration = 0.18f;

    RectTransform _runtimeRoot;
    RectTransform _flashRoot;
    CanvasGroup _flashGroup;
    RawImage _flashBackground;
    RawImage _flashAccent;
    TextMeshProUGUI _flashText;

    RectTransform _outroRoot;
    CanvasGroup _outroGroup;
    RawImage _outroBackground;
    RawImage _outroAccent;
    RawImage _outroScanTop;
    RawImage _outroScanBottom;
    TextMeshProUGUI _outroTitleText;
    TextMeshProUGUI _outroSubtitleText;

    AudioClip _parryTone;
    AudioClip _perfectDodgeTone;
    AudioClip _healTone;
    AudioClip _ultimateTone;
    AudioClip _outroTone;
    Coroutine _flashRoutine;
    Coroutine _outroRoutine;
    bool _subscribed;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        TutorialPlayerRuntimeBridge runtimePlayerBridge,
        Canvas runtimeCanvas)
    {
        flowController = runtimeFlowController;
        playerBridge = runtimePlayerBridge;
        targetCanvas = runtimeCanvas;
        EnsureRuntimeVisuals();
        EnsureRuntimeAudio();
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        EnsureRuntimeVisuals();
        EnsureRuntimeAudio();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        StopActiveCoroutines();
        HideRuntimeVisuals();
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
        DestroyRuntimeClip(_parryTone);
        DestroyRuntimeClip(_perfectDodgeTone);
        DestroyRuntimeClip(_healTone);
        DestroyRuntimeClip(_ultimateTone);
        DestroyRuntimeClip(_outroTone);
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();

        if (flowController == null || playerBridge == null)
            return;

        flowController.StepStarted += HandleStepStarted;
        flowController.StepCompleted += HandleStepCompleted;
        playerBridge.ParrySucceeded += HandleParrySucceeded;
        playerBridge.PerfectDodged += HandlePerfectDodged;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed)
            return;

        if (flowController != null)
        {
            flowController.StepStarted -= HandleStepStarted;
            flowController.StepCompleted -= HandleStepCompleted;
        }

        if (playerBridge != null)
        {
            playerBridge.ParrySucceeded -= HandleParrySucceeded;
            playerBridge.PerfectDodged -= HandlePerfectDodged;
        }

        _subscribed = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        if (step == null || step.stepType != TutorialStepType.Exit)
            return;

        PlayOutroOverlay(outroTitle, outroSubtitle);
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (step == null)
            return;

        switch (step.stepType)
        {
            case TutorialStepType.CameraFocus:
                PlayFlash(cameraFlashText, cameraFlashColor, _parryTone);
                break;

            case TutorialStepType.LockOn:
                PlayFlash(lockOnFlashText, lockOnFlashColor, _perfectDodgeTone);
                break;

            case TutorialStepType.BasicAttack:
                PlayFlash(basicAttackFlashText, basicAttackFlashColor, _healTone);
                break;

            case TutorialStepType.Combo:
                PlayFlash(comboFlashText, comboFlashColor, _ultimateTone);
                break;

            case TutorialStepType.Heal:
                PlayFlash(healFlashText, healFlashColor, _healTone);
                break;

            case TutorialStepType.Ultimate:
                PlayFlash(ultimateFlashText, ultimateFlashColor, _ultimateTone);
                break;

            case TutorialStepType.Exit:
                PlayFlash(exitFlashText, exitFlashColor, _outroTone);
                break;
        }
    }

    void HandleParrySucceeded()
    {
        if (flowController == null || flowController.CurrentStep == null)
            return;

        if (flowController.CurrentStep.stepType != TutorialStepType.Parry)
            return;

        PlayFlash(parryFlashText, parryFlashColor, _parryTone);
    }

    void HandlePerfectDodged()
    {
        if (flowController == null || flowController.CurrentStep == null)
            return;

        if (flowController.CurrentStep.stepType != TutorialStepType.PerfectDodge)
            return;

        PlayFlash(perfectDodgeFlashText, perfectDodgeFlashColor, _perfectDodgeTone);
    }

    void PlayFlash(string message, Color accentColor, AudioClip toneClip)
    {
        EnsureRuntimeVisuals();
        EnsureRuntimeAudio();
        if (_flashGroup == null || _flashText == null)
            return;

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);

        _flashText.text = message;
        _flashText.color = accentColor;
        _flashBackground.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.16f);
        _flashAccent.color = accentColor;
        _flashRoot.SetAsLastSibling();
        PlayTone(toneClip, successToneVolume);
        _flashRoutine = StartCoroutine(CoPlayFlash());
    }

    void PlayOutroOverlay(string title, string subtitle)
    {
        EnsureRuntimeVisuals();
        EnsureRuntimeAudio();
        if (_outroGroup == null || _outroTitleText == null || _outroSubtitleText == null)
            return;

        if (_outroRoutine != null)
            StopCoroutine(_outroRoutine);

        _outroTitleText.text = title;
        _outroSubtitleText.text = subtitle;
        _outroBackground.color = outroOverlayColor;
        _outroAccent.color = outroAccentColor;
        _outroRoot.SetAsLastSibling();
        PlayTone(_outroTone, outroToneVolume);
        _outroRoutine = StartCoroutine(CoPlayOutroOverlay());
    }

    IEnumerator CoPlayFlash()
    {
        _flashGroup.alpha = 0f;
        _flashRoot.localScale = new Vector3(0.96f, 0.96f, 1f);

        yield return FadeCanvasGroup(_flashGroup, 0f, 1f, Mathf.Max(0.01f, flashFadeInDuration), true);

        float holdDuration = Mathf.Max(0f, flashHoldDuration);
        float holdEnd = Time.unscaledTime + holdDuration;
        while (Time.unscaledTime < holdEnd)
        {
            float normalized = holdDuration <= 0.001f
                ? 1f
                : 1f - ((holdEnd - Time.unscaledTime) / holdDuration);
            float scale = Mathf.LerpUnclamped(1.06f, 1f, EaseOutCubic(Mathf.Clamp01(normalized)));
            _flashRoot.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        _flashRoot.localScale = Vector3.one;
        yield return FadeCanvasGroup(_flashGroup, _flashGroup.alpha, 0f, Mathf.Max(0.01f, flashFadeOutDuration), true);
        _flashRoutine = null;
    }

    IEnumerator CoPlayOutroOverlay()
    {
        _outroGroup.alpha = 0f;
        _outroRoot.localScale = new Vector3(0.985f, 0.985f, 1f);
        ApplyOutroScanlineState(0f, 0f);

        float fadeInDuration = Mathf.Max(0.01f, outroFadeInDuration);
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeInDuration);
            float eased = EaseOutCubic(t);
            _outroGroup.alpha = eased;
            _outroRoot.localScale = new Vector3(
                Mathf.LerpUnclamped(0.985f, 1f, eased),
                Mathf.LerpUnclamped(0.985f, 1f, eased),
                1f);
            ApplyOutroScanlineState(eased, eased);
            yield return null;
        }

        float holdDuration = Mathf.Max(0f, outroHoldDuration);
        float holdEnd = Time.unscaledTime + holdDuration;
        while (Time.unscaledTime < holdEnd)
        {
            float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * 14f) * 0.5f);
            ApplyOutroScanlineState(1f, pulse);
            _outroAccent.color = Color.Lerp(outroAccentColor * 0.72f, outroAccentColor, 0.45f + (pulse * 0.35f));
            yield return null;
        }

        float fadeOutDuration = Mathf.Max(0.01f, outroFadeOutDuration);
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeOutDuration);
            float eased = EaseOutCubic(t);
            _outroGroup.alpha = Mathf.Lerp(1f, 0f, eased);
            ApplyOutroScanlineState(1f - eased, 1f - eased);
            yield return null;
        }

        _outroGroup.alpha = 0f;
        _outroRoot.localScale = Vector3.one;
        ApplyOutroScanlineState(0f, 0f);
        _outroRoutine = null;
    }

    IEnumerator FadeCanvasGroup(CanvasGroup canvasGroup, float from, float to, float duration, bool animateFlashScale)
    {
        float safeDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            float eased = EaseOutCubic(t);
            canvasGroup.alpha = Mathf.Lerp(from, to, eased);

            if (animateFlashScale && _flashRoot != null)
            {
                float scale = Mathf.LerpUnclamped(0.96f, 1f, eased);
                _flashRoot.localScale = new Vector3(scale, scale, 1f);
            }

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
            Transform existing = targetCanvas.transform.Find("TutorialPresentationRuntime");
            if (existing != null)
                _runtimeRoot = existing as RectTransform;

            if (_runtimeRoot == null)
            {
                GameObject rootObject = new GameObject("TutorialPresentationRuntime", typeof(RectTransform));
                _runtimeRoot = rootObject.GetComponent<RectTransform>();
                _runtimeRoot.SetParent(targetCanvas.transform, false);
            }

            StretchToParent(_runtimeRoot);
            _runtimeRoot.SetAsLastSibling();
        }

        EnsureFlashVisuals();
        EnsureOutroVisuals();
    }

    void EnsureRuntimeAudio()
    {
        if (effectAudioSource == null)
        {
            effectAudioSource = GetComponent<AudioSource>();
            if (effectAudioSource == null)
                effectAudioSource = gameObject.AddComponent<AudioSource>();

            effectAudioSource.playOnAwake = false;
            effectAudioSource.loop = false;
            effectAudioSource.spatialBlend = 0f;
        }

        if (_parryTone == null)
            _parryTone = CreateToneClip("TutorialParryTone", parryToneFrequency, successToneDuration);
        if (_perfectDodgeTone == null)
            _perfectDodgeTone = CreateToneClip("TutorialPerfectDodgeTone", perfectDodgeToneFrequency, successToneDuration);
        if (_healTone == null)
            _healTone = CreateToneClip("TutorialHealTone", healToneFrequency, successToneDuration);
        if (_ultimateTone == null)
            _ultimateTone = CreateToneClip("TutorialUltimateTone", ultimateToneFrequency, successToneDuration);
        if (_outroTone == null)
            _outroTone = CreateToneClip("TutorialOutroTone", outroToneFrequency, outroToneDuration);
    }

    void EnsureFlashVisuals()
    {
        if (_flashRoot == null)
        {
            Transform existing = _runtimeRoot.Find("StepFlash");
            if (existing != null)
                _flashRoot = existing as RectTransform;

            if (_flashRoot == null)
            {
                GameObject rootObject = new GameObject("StepFlash", typeof(RectTransform), typeof(CanvasGroup));
                _flashRoot = rootObject.GetComponent<RectTransform>();
                _flashRoot.SetParent(_runtimeRoot, false);
            }

            _flashRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _flashRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _flashRoot.pivot = new Vector2(0.5f, 0.5f);
            _flashRoot.anchoredPosition = flashAnchoredPosition;
            _flashRoot.sizeDelta = flashSize;
            _flashRoot.localScale = Vector3.one;

            _flashGroup = _flashRoot.GetComponent<CanvasGroup>();
            if (_flashGroup == null)
                _flashGroup = _flashRoot.gameObject.AddComponent<CanvasGroup>();
            _flashGroup.alpha = 0f;
        }

        if (_flashBackground == null)
        {
            Transform existing = _flashRoot.Find("Background");
            if (existing != null)
                _flashBackground = existing.GetComponent<RawImage>();

            if (_flashBackground == null)
            {
                GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                backgroundObject.transform.SetParent(_flashRoot, false);
                _flashBackground = backgroundObject.GetComponent<RawImage>();
            }

            StretchToParent(_flashBackground.rectTransform);
            _flashBackground.texture = Texture2D.whiteTexture;
            _flashBackground.raycastTarget = false;
        }

        if (_flashAccent == null)
        {
            Transform existing = _flashRoot.Find("Accent");
            if (existing != null)
                _flashAccent = existing.GetComponent<RawImage>();

            if (_flashAccent == null)
            {
                GameObject accentObject = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                accentObject.transform.SetParent(_flashRoot, false);
                _flashAccent = accentObject.GetComponent<RawImage>();
            }

            RectTransform accentRect = _flashAccent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(10f, 0f);
            _flashAccent.texture = Texture2D.whiteTexture;
            _flashAccent.raycastTarget = false;
        }

        if (_flashText == null)
        {
            Transform existing = _flashRoot.Find("Label");
            if (existing != null)
                _flashText = existing.GetComponent<TextMeshProUGUI>();

            if (_flashText == null)
            {
                GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(_flashRoot, false);
                _flashText = labelObject.GetComponent<TextMeshProUGUI>();
            }

            RectTransform labelRect = _flashText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(22f, 0f);
            labelRect.offsetMax = new Vector2(-16f, 0f);
            _flashText.alignment = TextAlignmentOptions.Center;
            _flashText.fontSize = 34f;
            _flashText.fontStyle = FontStyles.Bold;
            _flashText.raycastTarget = false;
        }
    }

    void EnsureOutroVisuals()
    {
        if (_outroRoot == null)
        {
            Transform existing = _runtimeRoot.Find("OutroOverlay");
            if (existing != null)
                _outroRoot = existing as RectTransform;

            if (_outroRoot == null)
            {
                GameObject rootObject = new GameObject("OutroOverlay", typeof(RectTransform), typeof(CanvasGroup));
                _outroRoot = rootObject.GetComponent<RectTransform>();
                _outroRoot.SetParent(_runtimeRoot, false);
            }

            StretchToParent(_outroRoot);
            _outroGroup = _outroRoot.GetComponent<CanvasGroup>();
            if (_outroGroup == null)
                _outroGroup = _outroRoot.gameObject.AddComponent<CanvasGroup>();
            _outroGroup.alpha = 0f;
        }

        if (_outroBackground == null)
        {
            Transform existing = _outroRoot.Find("Background");
            if (existing != null)
                _outroBackground = existing.GetComponent<RawImage>();

            if (_outroBackground == null)
            {
                GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                backgroundObject.transform.SetParent(_outroRoot, false);
                _outroBackground = backgroundObject.GetComponent<RawImage>();
            }

            StretchToParent(_outroBackground.rectTransform);
            _outroBackground.texture = Texture2D.whiteTexture;
            _outroBackground.raycastTarget = false;
        }

        if (_outroAccent == null)
        {
            Transform existing = _outroRoot.Find("Accent");
            if (existing != null)
                _outroAccent = existing.GetComponent<RawImage>();

            if (_outroAccent == null)
            {
                GameObject accentObject = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                accentObject.transform.SetParent(_outroRoot, false);
                _outroAccent = accentObject.GetComponent<RawImage>();
            }

            RectTransform accentRect = _outroAccent.rectTransform;
            accentRect.anchorMin = new Vector2(0.5f, 0.5f);
            accentRect.anchorMax = new Vector2(0.5f, 0.5f);
            accentRect.pivot = new Vector2(0.5f, 0.5f);
            accentRect.anchoredPosition = new Vector2(0f, 8f);
            accentRect.sizeDelta = new Vector2(520f, 2f);
            _outroAccent.texture = Texture2D.whiteTexture;
            _outroAccent.raycastTarget = false;
        }

        if (_outroScanTop == null)
            _outroScanTop = EnsureScanline("ScanTop", 84f);
        if (_outroScanBottom == null)
            _outroScanBottom = EnsureScanline("ScanBottom", -68f);

        if (_outroTitleText == null)
        {
            Transform existing = _outroRoot.Find("Title");
            if (existing != null)
                _outroTitleText = existing.GetComponent<TextMeshProUGUI>();

            if (_outroTitleText == null)
            {
                GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                titleObject.transform.SetParent(_outroRoot, false);
                _outroTitleText = titleObject.GetComponent<TextMeshProUGUI>();
            }

            RectTransform titleRect = _outroTitleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 52f);
            titleRect.sizeDelta = new Vector2(560f, 56f);
            _outroTitleText.alignment = TextAlignmentOptions.Center;
            _outroTitleText.fontSize = 34f;
            _outroTitleText.fontStyle = FontStyles.Bold;
            _outroTitleText.color = outroAccentColor;
            _outroTitleText.raycastTarget = false;
        }

        if (_outroSubtitleText == null)
        {
            Transform existing = _outroRoot.Find("Subtitle");
            if (existing != null)
                _outroSubtitleText = existing.GetComponent<TextMeshProUGUI>();

            if (_outroSubtitleText == null)
            {
                GameObject subtitleObject = new GameObject("Subtitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                subtitleObject.transform.SetParent(_outroRoot, false);
                _outroSubtitleText = subtitleObject.GetComponent<TextMeshProUGUI>();
            }

            RectTransform subtitleRect = _outroSubtitleText.rectTransform;
            subtitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            subtitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            subtitleRect.pivot = new Vector2(0.5f, 0.5f);
            subtitleRect.anchoredPosition = new Vector2(0f, -26f);
            subtitleRect.sizeDelta = new Vector2(620f, 48f);
            _outroSubtitleText.alignment = TextAlignmentOptions.Center;
            _outroSubtitleText.fontSize = 24f;
            _outroSubtitleText.color = new Color(0.82f, 0.95f, 1.00f, 0.96f);
            _outroSubtitleText.raycastTarget = false;
        }
    }

    RawImage EnsureScanline(string objectName, float anchoredY)
    {
        Transform existing = _outroRoot.Find(objectName);
        RawImage scanline = existing != null ? existing.GetComponent<RawImage>() : null;
        if (scanline == null)
        {
            GameObject scanObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            scanObject.transform.SetParent(_outroRoot, false);
            scanline = scanObject.GetComponent<RawImage>();
        }

        RectTransform rect = scanline.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, anchoredY);
        rect.sizeDelta = new Vector2(560f, 4f);
        scanline.texture = Texture2D.whiteTexture;
        scanline.raycastTarget = false;
        return scanline;
    }

    void ApplyOutroScanlineState(float visibility, float pulse)
    {
        if (_outroScanTop == null || _outroScanBottom == null)
            return;

        float travel = outroScanlineTravel * (1f - Mathf.Clamp01(visibility));
        float lineAlpha = 0.12f + (0.20f * Mathf.Clamp01(pulse));
        Color lineColor = new Color(outroAccentColor.r, outroAccentColor.g, outroAccentColor.b, lineAlpha * Mathf.Clamp01(visibility));

        _outroScanTop.color = lineColor;
        _outroScanBottom.color = lineColor;
        _outroScanTop.rectTransform.anchoredPosition = new Vector2(0f, 84f + travel);
        _outroScanBottom.rectTransform.anchoredPosition = new Vector2(0f, -68f - travel);
    }

    void PlayTone(AudioClip clip, float volume)
    {
        if (effectAudioSource == null || clip == null)
            return;

        effectAudioSource.pitch = 1f;
        effectAudioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(volume));
    }

    void StopActiveCoroutines()
    {
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
        }

        if (_outroRoutine != null)
        {
            StopCoroutine(_outroRoutine);
            _outroRoutine = null;
        }
    }

    void HideRuntimeVisuals()
    {
        if (_flashGroup != null)
            _flashGroup.alpha = 0f;
        if (_outroGroup != null)
            _outroGroup.alpha = 0f;
        if (_flashRoot != null)
            _flashRoot.localScale = Vector3.one;
        if (_outroRoot != null)
            _outroRoot.localScale = Vector3.one;
        ApplyOutroScanlineState(0f, 0f);
    }

    static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    static AudioClip CreateToneClip(string clipName, float frequency, float duration)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * duration));
        float[] data = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = 1f - Mathf.Clamp01(i / (float)(sampleCount - 1));
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.16f * envelope;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static void DestroyRuntimeClip(AudioClip clip)
    {
        if (clip != null)
            Destroy(clip);
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t -= 1f;
        return t * t * t + 1f;
    }
}
