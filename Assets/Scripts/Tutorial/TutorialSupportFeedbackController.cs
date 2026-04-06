using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TutorialSupportFeedbackController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Transform movementTarget;
    [SerializeField] private Transform ultimateTarget;
    [SerializeField] private Transform exitTarget;

    [Header("Cue Panel")]
    [SerializeField] private Vector2 cueAnchoredPosition = new Vector2(0f, -26f);
    [SerializeField] private Vector2 cueSize = new Vector2(420f, 76f);

    [Header("Support Status")]
    [SerializeField] private Vector2 statusAnchoredPosition = new Vector2(0f, -104f);
    [SerializeField] private Vector2 statusSize = new Vector2(420f, 72f);
    [SerializeField, Min(0.05f)] private float statusRefreshInterval = 0.12f;
    [SerializeField, Min(0.1f)] private float exitArriveDistance = 1.2f;
    [SerializeField, Range(0.05f, 0.95f)] private float exitMilestoneOneThreshold = 0.38f;
    [SerializeField, Range(0.05f, 0.98f)] private float exitMilestoneTwoThreshold = 0.72f;

    [Header("Result Panel")]
    [SerializeField] private Vector2 resultAnchoredPosition = new Vector2(0f, 148f);
    [SerializeField] private Vector2 resultSize = new Vector2(380f, 76f);
    [SerializeField, Min(0.01f)] private float resultFadeInDuration = 0.08f;
    [SerializeField, Min(0.01f)] private float resultHoldDuration = 0.34f;
    [SerializeField, Min(0.01f)] private float resultFadeOutDuration = 0.24f;

    [Header("World Pulse")]
    [SerializeField] private Vector3 completionPulseOffset = new Vector3(0f, 0.04f, 0f);
    [SerializeField] private Vector3 completionPulseStartScale = new Vector3(0.7f, 0.01f, 0.7f);
    [SerializeField] private Vector3 completionPulseEndScale = new Vector3(1.8f, 0.01f, 1.8f);
    [SerializeField, Min(0.05f)] private float completionPulseDuration = 0.42f;

    [Header("Colors")]
    [SerializeField] private Color healColor = new Color(0.36f, 1f, 0.58f, 0.96f);
    [SerializeField] private Color ultimateColor = new Color(1f, 0.78f, 0.20f, 0.96f);
    [SerializeField] private Color exitColor = new Color(0.24f, 0.88f, 1f, 0.96f);
    [SerializeField] private Color movementCompleteColor = new Color(0.24f, 0.88f, 1f, 0.96f);
    [SerializeField] private Color statusTrackColor = new Color(0.16f, 0.24f, 0.30f, 0.76f);
    [SerializeField] private Color statusPendingColor = new Color(0.42f, 0.52f, 0.60f, 0.88f);
    [SerializeField] private Color healthLowColor = new Color(1f, 0.38f, 0.28f, 0.96f);
    [SerializeField] private Color bodyTextColor = new Color(0.90f, 0.97f, 1f, 0.96f);

    RectTransform _runtimeRoot;
    RectTransform _cueRoot;
    CanvasGroup _cueGroup;
    RawImage _cueBackground;
    RawImage _cueAccent;
    TextMeshProUGUI _cueText;

    RectTransform _statusRoot;
    CanvasGroup _statusGroup;
    RawImage _statusBackground;
    RawImage _statusAccent;
    TextMeshProUGUI _statusTitle;
    RectTransform _statusBadgeRoot;
    RawImage _statusBadgeBackground;
    TextMeshProUGUI _statusBadgeText;
    TextMeshProUGUI _statusPrimary;
    TextMeshProUGUI _statusSecondary;
    RawImage _statusTrack;
    RawImage _statusFill;
    RectTransform _statusFillRect;

    RectTransform _resultRoot;
    CanvasGroup _resultGroup;
    RawImage _resultBackground;
    RawImage _resultAccent;
    TextMeshProUGUI _resultText;

    Transform _completionPulse;
    Renderer _completionPulseRenderer;
    MaterialPropertyBlock _pulsePropertyBlock;
    Coroutine _resultRoutine;
    Coroutine _completionPulseRoutine;
    Coroutine _statusRoutine;
    bool _subscribed;
    TutorialStepType _activeStatusStep = TutorialStepType.Movement;
    int _lastStatusHP = -1;
    int _lastStatusMaxHP = -1;
    int _lastStatusAmpoule = -1;
    int _lastStatusMaxAmpoule = -1;
    int _lastStatusUltPercent = -1;
    bool _lastStatusReady;
    int _lastStatusExitDistanceTenths = -1;
    bool _lastStatusExitArrived;
    int _lastExitMilestoneStage;
    string _lastStatusBadgeLabel = string.Empty;
    Color _lastStatusBadgeColor = Color.clear;
    float _exitReferenceDistance = 1f;

    static Material s_pulseMaterial;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        TutorialPlayerRuntimeBridge runtimePlayerBridge,
        Canvas runtimeCanvas,
        Transform runtimeMovementTarget,
        Transform runtimeUltimateTarget,
        Transform runtimeExitTarget)
    {
        flowController = runtimeFlowController;
        playerBridge = runtimePlayerBridge;
        targetCanvas = runtimeCanvas;
        movementTarget = runtimeMovementTarget;
        ultimateTarget = runtimeUltimateTarget;
        exitTarget = runtimeExitTarget;

        EnsureRuntimeVisuals();
        EnsureCompletionPulse();
        RefreshSubscriptions();
        ApplyCue(flowController != null ? flowController.CurrentStep : null);
    }

    void OnEnable()
    {
        EnsureRuntimeVisuals();
        EnsureCompletionPulse();
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

        if (playerBridge != null)
        {
            playerBridge.AmpouleUsed += HandleAmpouleUsed;
            playerBridge.UltimateStarted += HandleUltimateStarted;
            playerBridge.HealthChanged += HandleHealthChanged;
            playerBridge.AmpouleChanged += HandleAmpouleChanged;
        }
    }

    void ReleaseSubscriptions()
    {
        if (_subscribed && flowController != null)
        {
            flowController.StepStarted -= HandleStepStarted;
            flowController.StepCompleted -= HandleStepCompleted;
        }

        if (playerBridge != null)
        {
            playerBridge.AmpouleUsed -= HandleAmpouleUsed;
            playerBridge.UltimateStarted -= HandleUltimateStarted;
            playerBridge.HealthChanged -= HandleHealthChanged;
            playerBridge.AmpouleChanged -= HandleAmpouleChanged;
        }

        _subscribed = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        if (step != null && step.stepType == TutorialStepType.Exit)
        {
            _exitReferenceDistance = Mathf.Max(exitArriveDistance + 0.1f, GetExitDistance());
            _lastExitMilestoneStage = 0;
        }

        ApplyCue(step);
        RefreshSupportStatus(step);
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (step == null)
            return;

        switch (step.stepType)
        {
            case TutorialStepType.Movement:
                PlayCompletionPulse(movementTarget, movementCompleteColor);
                break;

            case TutorialStepType.Heal:
                PlayCompletionPulse(GetPlayerPulseTarget(), healColor);
                ShowResult("\ud68c\ubcf5\u0020\uc548\uc815\ud654", healColor);
                break;

            case TutorialStepType.Ultimate:
                PlayCompletionPulse(GetUltimatePulseTarget(), ultimateColor);
                ShowResult("\uc804\ud22c\u0020\ub9ac\ub4ec\u0020\uc804\ud658", ultimateColor);
                break;

            case TutorialStepType.Exit:
                PlayCompletionPulse(exitTarget, exitColor);
                ShowResult("\ub2e4\uc74c\u0020\uad6c\uac04\u0020\uc5f0\uacb0", exitColor);
                break;
        }
    }

    void HandleAmpouleUsed()
    {
        if (flowController == null || flowController.CurrentStep == null)
            return;

        if (flowController.CurrentStep.stepType != TutorialStepType.Heal)
            return;

        PlayCompletionPulse(GetPlayerPulseTarget(), healColor);
        ShowResult("\uc570\ud50c\u0020\ud22c\uc785", healColor);
        UpdateSupportStatusImmediate(TutorialStepType.Heal);
    }

    void HandleUltimateStarted()
    {
        if (flowController == null || flowController.CurrentStep == null)
            return;

        if (flowController.CurrentStep.stepType != TutorialStepType.Ultimate)
            return;

        ShowResult("\uad81\uadf9\uae30\u0020\ubc1c\ub3d9", ultimateColor);
        PlayCompletionPulse(GetUltimatePulseTarget(), ultimateColor);
        UpdateSupportStatusImmediate(TutorialStepType.Ultimate);
    }

    void HandleHealthChanged(int current, int max)
    {
        if (_activeStatusStep == TutorialStepType.Heal)
            UpdateSupportStatusImmediate(TutorialStepType.Heal);
    }

    void HandleAmpouleChanged(int current, int max)
    {
        if (_activeStatusStep == TutorialStepType.Heal)
            UpdateSupportStatusImmediate(TutorialStepType.Heal);
    }

    void ApplyCue(TutorialStepDefinition step)
    {
        EnsureRuntimeVisuals();
        if (_cueGroup == null || _cueText == null)
            return;

        if (step == null)
        {
            _cueGroup.alpha = 0f;
            return;
        }

        string cueText;
        Color cueColor;
        switch (step.stepType)
        {
            case TutorialStepType.Heal:
                cueText = "\uc9c0\uc6d0\u0020\ub2e8\uacc4\u0020\u00b7\u0020\uc570\ud50c\ub85c\u0020\ucee8\ub514\uc158\uc744\u0020\ud68c\ubcf5\ud574";
                cueColor = healColor;
                break;

            case TutorialStepType.Ultimate:
                cueText = "\uc9c0\uc6d0\u0020\ub2e8\uacc4\u0020\u00b7\u0020\uad81\uadf9\uae30\ub85c\u0020\uc804\ud22c\u0020\ub9ac\ub4ec\uc744\u0020\ub04a\uc5b4";
                cueColor = ultimateColor;
                break;

            case TutorialStepType.Exit:
                cueText = "\uc9c0\uc6d0\u0020\ub2e8\uacc4\u0020\u00b7\u0020\ucd9c\uad6c\ub85c\u0020\uc774\ub3d9\ud574\u0020\ud604\uc2e4\ub85c\u0020\ubcf5\uadc0\ud574";
                cueColor = exitColor;
                break;

            default:
                _cueGroup.alpha = 0f;
                return;
        }

        _cueText.text = cueText;
        _cueText.color = cueColor;
        _cueBackground.color = new Color(cueColor.r, cueColor.g, cueColor.b, 0.12f);
        _cueAccent.color = cueColor;
        _cueGroup.alpha = 1f;
        _cueRoot.SetAsLastSibling();
    }

    void RefreshSupportStatus(TutorialStepDefinition step)
    {
        StopStatusRoutine();

        if (step == null || (step.stepType != TutorialStepType.Heal && step.stepType != TutorialStepType.Ultimate && step.stepType != TutorialStepType.Exit))
        {
            _activeStatusStep = TutorialStepType.Movement;
            if (_statusGroup != null)
                _statusGroup.alpha = 0f;
            return;
        }

        _activeStatusStep = step.stepType;
        EnsureRuntimeVisuals();
        ResetStatusCache();
        UpdateSupportStatusImmediate(step.stepType);
        _statusRoutine = StartCoroutine(CoRefreshSupportStatus(step));
    }

    IEnumerator CoRefreshSupportStatus(TutorialStepDefinition step)
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(Mathf.Max(0.05f, statusRefreshInterval));
        while (flowController != null && ReferenceEquals(flowController.CurrentStep, step))
        {
            UpdateSupportStatusImmediate(step.stepType);
            yield return wait;
        }

        _statusRoutine = null;
    }

    void UpdateSupportStatusImmediate(TutorialStepType stepType)
    {
        EnsureRuntimeVisuals();
        if (_statusGroup == null || _statusTitle == null || _statusPrimary == null || _statusSecondary == null || _statusFill == null || playerBridge == null)
            return;

        _statusGroup.alpha = 1f;
        _statusRoot.SetAsLastSibling();

        switch (stepType)
        {
            case TutorialStepType.Heal:
                UpdateHealStatus();
                break;

            case TutorialStepType.Ultimate:
                UpdateUltimateStatus();
                break;

            case TutorialStepType.Exit:
                UpdateExitStatus();
                break;
        }
    }

    void UpdateHealStatus()
    {
        int hp = playerBridge.CurrentHP;
        int maxHP = Mathf.Max(1, playerBridge.MaxHP);
        int ampoule = playerBridge.CurrentAmpouleCount;
        int maxAmpoule = Mathf.Max(1, playerBridge.MaxAmpouleCount);
        if (_lastStatusHP == hp &&
            _lastStatusMaxHP == maxHP &&
            _lastStatusAmpoule == ampoule &&
            _lastStatusMaxAmpoule == maxAmpoule &&
            _activeStatusStep == TutorialStepType.Heal)
            return;

        _activeStatusStep = TutorialStepType.Heal;
        _lastStatusHP = hp;
        _lastStatusMaxHP = maxHP;
        _lastStatusAmpoule = ampoule;
        _lastStatusMaxAmpoule = maxAmpoule;

        float normalizedHealth = playerBridge.NormalizedHealth;
        Color resolvedColor = Color.Lerp(healthLowColor, healColor, normalizedHealth);

        _statusTitle.text = "\uc9c0\uc6d0\u0020\uc0c1\ud0dc";
        _statusTitle.color = healColor;
        _statusPrimary.text = "HP " + hp + " / " + maxHP;
        _statusPrimary.color = bodyTextColor;
        _statusSecondary.text = "\uc570\ud50c\u0020" + ampoule + " / " + maxAmpoule;
        _statusSecondary.color = resolvedColor;
        _statusBackground.color = new Color(healColor.r, healColor.g, healColor.b, 0.12f);
        _statusAccent.color = healColor;
        _statusTrack.color = statusTrackColor;
        _statusFill.color = resolvedColor;
        ApplyStatusBadge(
            normalizedHealth <= 0.45f ? "\uc704\ud5d8" : (ampoule > 0 ? "\ud68c\ubcf5" : "\uc548\uc815"),
            normalizedHealth <= 0.45f ? healthLowColor : healColor);
        SetStatusFillAmount(normalizedHealth);
    }

    void UpdateUltimateStatus()
    {
        int ultPercent = Mathf.RoundToInt(playerBridge.UltimateGaugeNormalized * 100f);
        bool ready = playerBridge.IsUltimateReady;
        if (_lastStatusUltPercent == ultPercent &&
            _lastStatusReady == ready &&
            _activeStatusStep == TutorialStepType.Ultimate)
            return;

        _activeStatusStep = TutorialStepType.Ultimate;
        _lastStatusUltPercent = ultPercent;
        _lastStatusReady = ready;

        Color resolvedColor = ready ? ultimateColor : Color.Lerp(statusPendingColor, ultimateColor, playerBridge.UltimateGaugeNormalized);

        _statusTitle.text = "\uc9c0\uc6d0\u0020\uc0c1\ud0dc";
        _statusTitle.color = ultimateColor;
        _statusPrimary.text = "ULT " + ultPercent + "%";
        _statusPrimary.color = bodyTextColor;
        _statusSecondary.text = ready ? "\uc900\ube44\u0020\uc644\ub8cc" : "\ucda9\uc804\u0020\uc911";
        _statusSecondary.color = resolvedColor;
        _statusBackground.color = new Color(ultimateColor.r, ultimateColor.g, ultimateColor.b, 0.12f);
        _statusAccent.color = ultimateColor;
        _statusTrack.color = statusTrackColor;
        _statusFill.color = resolvedColor;
        ApplyStatusBadge(ready ? "\uc900\ube44\u0020\uc644\ub8cc" : "\ucda9\uc804\u0020\uc911", resolvedColor);
        SetStatusFillAmount(playerBridge.UltimateGaugeNormalized);
    }

    void UpdateExitStatus()
    {
        Transform playerTransform = playerBridge != null ? playerBridge.PlayerTransform : null;
        if (playerTransform == null || exitTarget == null)
            return;

        float distance = GetExitDistance();
        int distanceTenths = Mathf.RoundToInt(distance * 10f);
        bool arrived = distance <= exitArriveDistance;
        if (_lastStatusExitDistanceTenths == distanceTenths &&
            _lastStatusExitArrived == arrived &&
            _activeStatusStep == TutorialStepType.Exit)
            return;

        _activeStatusStep = TutorialStepType.Exit;
        _lastStatusExitDistanceTenths = distanceTenths;
        _lastStatusExitArrived = arrived;

        float normalized = Mathf.Clamp01(1f - Mathf.InverseLerp(exitArriveDistance, Mathf.Max(exitArriveDistance + 0.1f, _exitReferenceDistance), distance));
        int milestoneStage = 0;
        if (normalized >= exitMilestoneTwoThreshold)
            milestoneStage = 2;
        else if (normalized >= exitMilestoneOneThreshold)
            milestoneStage = 1;

        if (!arrived && milestoneStage > _lastExitMilestoneStage)
        {
            _lastExitMilestoneStage = milestoneStage;
            PlayCompletionPulse(exitTarget, exitColor);
            ShowResult(
                milestoneStage == 1
                    ? "\ubcf5\uadc0\u0020\uacbd\ub85c\u0020\ud655\ubcf4"
                    : "\ucd9c\uad6c\u0020\uc811\uadfc",
                exitColor);
        }

        Color resolvedColor = arrived ? Color.Lerp(exitColor, Color.white, 0.18f) : Color.Lerp(statusPendingColor, exitColor, normalized);
        string secondaryText = arrived
            ? "\ucd9c\uad6c\u0020\ub3c4\ucc29"
            : (milestoneStage >= 2 ? "\ucd9c\uad6c\u0020\ub3c4\ub2ec\u0020\uc9c1\uc804" : "\ucd9c\uad6c\uae4c\uc9c0\u0020\uc811\uadfc\u0020\uc911");
        string badgeText = arrived
            ? "\ub3c4\ucc29\u0020\uc784\ubc15"
            : (milestoneStage >= 2 ? "\uc9c4\uc785\u0020\uc900\ube44" : "\ubcf5\uadc0\u0020\uc911");

        _statusTitle.text = "\ubcf5\uadc0\u0020\uc808\ucc28";
        _statusTitle.color = exitColor;
        _statusPrimary.text = "EXIT " + distance.ToString("0.0") + "m";
        _statusPrimary.color = bodyTextColor;
        _statusSecondary.text = secondaryText;
        _statusSecondary.color = resolvedColor;
        _statusBackground.color = new Color(exitColor.r, exitColor.g, exitColor.b, 0.12f);
        _statusAccent.color = exitColor;
        _statusTrack.color = statusTrackColor;
        _statusFill.color = resolvedColor;
        ApplyStatusBadge(badgeText, resolvedColor);
        SetStatusFillAmount(normalized);
    }

    void ShowResult(string text, Color color)
    {
        EnsureRuntimeVisuals();
        if (_resultGroup == null || _resultText == null || string.IsNullOrWhiteSpace(text))
            return;

        if (_resultRoutine != null)
            StopCoroutine(_resultRoutine);

        _resultText.text = text;
        _resultText.color = color;
        _resultBackground.color = new Color(color.r, color.g, color.b, 0.14f);
        _resultAccent.color = color;
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
        float safeDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            canvasGroup.alpha = Mathf.Lerp(from, to, EaseOutCubic(t));
            yield return null;
        }

        canvasGroup.alpha = to;
    }

    void PlayCompletionPulse(Transform target, Color color)
    {
        if (target == null)
            return;

        EnsureCompletionPulse();
        if (_completionPulse == null || _completionPulseRenderer == null)
            return;

        if (_completionPulseRoutine != null)
            StopCoroutine(_completionPulseRoutine);

        _completionPulse.position = target.position + completionPulseOffset;
        _completionPulse.rotation = Quaternion.identity;
        _completionPulseRenderer.enabled = true;
        _completionPulseRoutine = StartCoroutine(CoPlayCompletionPulse(color));
    }

    IEnumerator CoPlayCompletionPulse(Color color)
    {
        float duration = Mathf.Max(0.05f, completionPulseDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutCubic(t);

            _completionPulse.localScale = Vector3.LerpUnclamped(completionPulseStartScale, completionPulseEndScale, eased);
            Color pulseColor = color;
            pulseColor.a *= 1f - eased;
            _pulsePropertyBlock.Clear();
            _pulsePropertyBlock.SetColor("_Color", pulseColor);
            _pulsePropertyBlock.SetColor("_BaseColor", pulseColor);
            _completionPulseRenderer.SetPropertyBlock(_pulsePropertyBlock);
            yield return null;
        }

        _completionPulseRenderer.enabled = false;
        _completionPulseRoutine = null;
    }

    void EnsureRuntimeVisuals()
    {
        if (targetCanvas == null)
            return;

        if (_runtimeRoot == null)
        {
            Transform existing = targetCanvas.transform.Find("TutorialSupportFeedbackRuntime");
            if (existing != null)
                _runtimeRoot = existing as RectTransform;

            if (_runtimeRoot == null)
            {
                GameObject rootObject = new GameObject("TutorialSupportFeedbackRuntime", typeof(RectTransform));
                _runtimeRoot = rootObject.GetComponent<RectTransform>();
                _runtimeRoot.SetParent(targetCanvas.transform, false);
            }

            StretchToParent(_runtimeRoot);
        }

        EnsureCuePanel();
        EnsureStatusPanel();
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

        _cueText = EnsureText(_cueRoot, "Label");
        RectTransform cueTextRect = _cueText.rectTransform;
        cueTextRect.anchorMin = Vector2.zero;
        cueTextRect.anchorMax = Vector2.one;
        cueTextRect.offsetMin = new Vector2(24f, 10f);
        cueTextRect.offsetMax = new Vector2(-20f, -10f);
        _cueText.alignment = TextAlignmentOptions.Center;
        _cueText.fontSize = 24f;
        _cueText.fontStyle = FontStyles.Bold;
        _cueText.raycastTarget = false;
    }

    void EnsureStatusPanel()
    {
        if (_statusRoot == null)
        {
            Transform existing = _runtimeRoot.Find("StatusPanel");
            if (existing != null)
                _statusRoot = existing as RectTransform;

            if (_statusRoot == null)
            {
                GameObject rootObject = new GameObject("StatusPanel", typeof(RectTransform), typeof(CanvasGroup));
                _statusRoot = rootObject.GetComponent<RectTransform>();
                _statusRoot.SetParent(_runtimeRoot, false);
            }

            _statusRoot.anchorMin = new Vector2(0.5f, 1f);
            _statusRoot.anchorMax = new Vector2(0.5f, 1f);
            _statusRoot.pivot = new Vector2(0.5f, 1f);
            _statusRoot.anchoredPosition = statusAnchoredPosition;
            _statusRoot.sizeDelta = statusSize;

            _statusGroup = _statusRoot.GetComponent<CanvasGroup>();
            if (_statusGroup == null)
                _statusGroup = _statusRoot.gameObject.AddComponent<CanvasGroup>();
            _statusGroup.alpha = 0f;
        }

        _statusBackground = EnsurePanelImage(_statusRoot, "Background", out RectTransform statusBackgroundRect);
        StretchToParent(statusBackgroundRect);

        _statusAccent = EnsurePanelImage(_statusRoot, "Accent", out RectTransform statusAccentRect);
        statusAccentRect.anchorMin = new Vector2(0f, 0f);
        statusAccentRect.anchorMax = new Vector2(0f, 1f);
        statusAccentRect.pivot = new Vector2(0f, 0.5f);
        statusAccentRect.anchoredPosition = Vector2.zero;
        statusAccentRect.sizeDelta = new Vector2(8f, 0f);

        _statusTitle = EnsureText(_statusRoot, "Title");
        RectTransform titleRect = _statusTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(22f, -10f);
        titleRect.sizeDelta = new Vector2(120f, 18f);
        _statusTitle.alignment = TextAlignmentOptions.Left;
        _statusTitle.fontSize = 16f;
        _statusTitle.fontStyle = FontStyles.Bold;
        _statusTitle.raycastTarget = false;

        if (_statusBadgeRoot == null)
        {
            Transform existing = _statusRoot.Find("Badge");
            if (existing != null)
                _statusBadgeRoot = existing as RectTransform;

            if (_statusBadgeRoot == null)
            {
                GameObject badgeObject = new GameObject("Badge", typeof(RectTransform));
                _statusBadgeRoot = badgeObject.GetComponent<RectTransform>();
                _statusBadgeRoot.SetParent(_statusRoot, false);
            }
        }

        _statusBadgeRoot.anchorMin = new Vector2(1f, 1f);
        _statusBadgeRoot.anchorMax = new Vector2(1f, 1f);
        _statusBadgeRoot.pivot = new Vector2(1f, 1f);
        _statusBadgeRoot.anchoredPosition = new Vector2(-18f, -10f);
        _statusBadgeRoot.sizeDelta = new Vector2(116f, 18f);

        _statusBadgeBackground = EnsurePanelImage(_statusBadgeRoot, "Background", out RectTransform badgeRect);
        StretchToParent(badgeRect);

        _statusBadgeText = EnsureText(_statusBadgeRoot, "Label");
        RectTransform badgeTextRect = _statusBadgeText.rectTransform;
        StretchToParent(badgeTextRect);
        badgeTextRect.offsetMin = new Vector2(6f, 1f);
        badgeTextRect.offsetMax = new Vector2(-6f, -1f);
        _statusBadgeText.alignment = TextAlignmentOptions.Center;
        _statusBadgeText.fontSize = 12f;
        _statusBadgeText.fontStyle = FontStyles.Bold;
        _statusBadgeText.raycastTarget = false;

        _statusPrimary = EnsureText(_statusRoot, "Primary");
        RectTransform primaryRect = _statusPrimary.rectTransform;
        primaryRect.anchorMin = new Vector2(0f, 1f);
        primaryRect.anchorMax = new Vector2(0f, 1f);
        primaryRect.pivot = new Vector2(0f, 1f);
        primaryRect.anchoredPosition = new Vector2(22f, -30f);
        primaryRect.sizeDelta = new Vector2(180f, 20f);
        _statusPrimary.alignment = TextAlignmentOptions.Left;
        _statusPrimary.fontSize = 20f;
        _statusPrimary.fontStyle = FontStyles.Bold;
        _statusPrimary.raycastTarget = false;

        _statusSecondary = EnsureText(_statusRoot, "Secondary");
        RectTransform secondaryRect = _statusSecondary.rectTransform;
        secondaryRect.anchorMin = new Vector2(1f, 1f);
        secondaryRect.anchorMax = new Vector2(1f, 1f);
        secondaryRect.pivot = new Vector2(1f, 1f);
        secondaryRect.anchoredPosition = new Vector2(-18f, -30f);
        secondaryRect.sizeDelta = new Vector2(150f, 20f);
        _statusSecondary.alignment = TextAlignmentOptions.Right;
        _statusSecondary.fontSize = 18f;
        _statusSecondary.fontStyle = FontStyles.Bold;
        _statusSecondary.raycastTarget = false;

        _statusTrack = EnsurePanelImage(_statusRoot, "Track", out RectTransform trackRect);
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.offsetMin = new Vector2(22f, 12f);
        trackRect.offsetMax = new Vector2(-18f, 22f);

        _statusFill = EnsurePanelImage(_statusTrack.rectTransform, "Fill", out RectTransform fillRect);
        _statusFillRect = fillRect;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        SetStatusFillAmount(1f);
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

    void EnsureCompletionPulse()
    {
        _pulsePropertyBlock ??= new MaterialPropertyBlock();
        if (_completionPulse != null)
            return;

        Transform existing = transform.Find("TutorialCompletionPulse");
        if (existing != null)
        {
            _completionPulse = existing;
            _completionPulseRenderer = existing.GetComponent<Renderer>();
            if (_completionPulseRenderer != null)
                _completionPulseRenderer.sharedMaterial = GetPulseMaterial();
            return;
        }

        GameObject pulseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pulseObject.name = "TutorialCompletionPulse";
        pulseObject.transform.SetParent(transform, false);

        Collider pulseCollider = pulseObject.GetComponent<Collider>();
        if (pulseCollider != null)
            Destroy(pulseCollider);

        _completionPulse = pulseObject.transform;
        _completionPulseRenderer = pulseObject.GetComponent<Renderer>();
        if (_completionPulseRenderer != null)
        {
            _completionPulseRenderer.sharedMaterial = GetPulseMaterial();
            _completionPulseRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _completionPulseRenderer.receiveShadows = false;
            _completionPulseRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _completionPulseRenderer.enabled = false;
        }
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

        if (_completionPulseRoutine != null)
        {
            StopCoroutine(_completionPulseRoutine);
            _completionPulseRoutine = null;
        }

        StopStatusRoutine();

        if (_cueGroup != null)
            _cueGroup.alpha = 0f;
        if (_statusGroup != null)
            _statusGroup.alpha = 0f;
        if (_resultGroup != null)
            _resultGroup.alpha = 0f;
        if (_completionPulseRenderer != null)
            _completionPulseRenderer.enabled = false;
    }

    void StopStatusRoutine()
    {
        if (_statusRoutine != null)
        {
            StopCoroutine(_statusRoutine);
            _statusRoutine = null;
        }
    }

    void SetStatusFillAmount(float normalized)
    {
        if (_statusFillRect == null)
            return;

        Vector2 anchorMax = _statusFillRect.anchorMax;
        anchorMax.x = Mathf.Clamp01(normalized);
        _statusFillRect.anchorMax = anchorMax;
    }

    void ResetStatusCache()
    {
        _lastStatusHP = -1;
        _lastStatusMaxHP = -1;
        _lastStatusAmpoule = -1;
        _lastStatusMaxAmpoule = -1;
        _lastStatusUltPercent = -1;
        _lastStatusReady = false;
        _lastStatusExitDistanceTenths = -1;
        _lastStatusExitArrived = false;
        _lastExitMilestoneStage = 0;
        _lastStatusBadgeLabel = string.Empty;
        _lastStatusBadgeColor = Color.clear;
    }

    void ApplyStatusBadge(string label, Color color)
    {
        if (_statusBadgeText == null || _statusBadgeBackground == null)
            return;

        if (_lastStatusBadgeLabel == label && _lastStatusBadgeColor == color)
            return;

        _lastStatusBadgeLabel = label;
        _lastStatusBadgeColor = color;
        _statusBadgeText.text = label;
        _statusBadgeText.color = color;
        _statusBadgeBackground.color = new Color(color.r, color.g, color.b, 0.14f);
    }

    Transform GetPlayerPulseTarget()
    {
        return playerBridge != null ? playerBridge.PlayerTransform : null;
    }

    Transform GetUltimatePulseTarget()
    {
        if (ultimateTarget != null)
            return ultimateTarget;

        return GetPlayerPulseTarget();
    }

    float GetExitDistance()
    {
        Transform playerTransform = playerBridge != null ? playerBridge.PlayerTransform : null;
        if (playerTransform == null || exitTarget == null)
            return 0f;

        return Vector3.Distance(playerTransform.position, exitTarget.position);
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

    static Material GetPulseMaterial()
    {
        if (s_pulseMaterial != null)
            return s_pulseMaterial;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_pulseMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_pulseMaterial;
    }
}
