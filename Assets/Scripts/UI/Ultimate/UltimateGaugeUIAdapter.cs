using UnityEngine;
using TMPro;

public class UltimateGaugeUIAdapter : MonoBehaviour
{
    [Header("Source Controller")]
    public PlayerUltimateController source;

    [Header("Activation Feedback")]
    [SerializeField] private bool showActivationRejectedFeedback = true;
    [SerializeField] private bool showPhaseFeedback;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private CanvasGroup feedbackGroup;
    [SerializeField, Min(0.1f)] private float feedbackHoldDuration = 0.9f;
    [SerializeField, Min(0.05f)] private float feedbackFadeDuration = 0.18f;
    [SerializeField] private Color rejectedColor = new Color(1f, 0.34f, 0.24f, 1f);
    [SerializeField] private Color phaseColor = new Color(0.55f, 0.95f, 1f, 1f);

    float _lastRatio = -1f;
    bool _lastReady;
    bool _subscribed;
    float _feedbackVisibleUntil;
    float _feedbackFadeStart;
    bool _feedbackActive;

    void Awake()
    {
        ResolveSource();
        EnsureFeedbackView();
        SyncImmediate();
    }

    void OnEnable()
    {
        ResolveSource();
        EnsureFeedbackView();
        Subscribe();
        SyncImmediate();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Update()
    {
        TickFeedback();
    }

    void ResolveSource()
    {
        if (source == null)
            source = FindFirstObjectByType<PlayerUltimateController>();
    }

    void Subscribe()
    {
        if (_subscribed || source == null)
            return;

        source.OnGaugeChanged += HandleGaugeChanged;
        source.OnActivationRejected += HandleActivationRejected;
        source.OnUltimatePhaseChanged += HandleUltimatePhaseChanged;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed || source == null)
            return;

        source.OnGaugeChanged -= HandleGaugeChanged;
        source.OnActivationRejected -= HandleActivationRejected;
        source.OnUltimatePhaseChanged -= HandleUltimatePhaseChanged;
        _subscribed = false;
    }

    void HandleGaugeChanged(float gauge, float normalized, bool ready)
    {
        if (Mathf.Abs(normalized - _lastRatio) > 0.001f)
        {
            _lastRatio = normalized;
            UI_UltimateGauge.UpdateValue(normalized);
        }

        if (ready != _lastReady)
        {
            _lastReady = ready;
            UI_UltimateGauge.SetReady(ready);
        }
    }

    void SyncImmediate()
    {
        if (source == null)
            return;

        HandleGaugeChanged(
            source.Gauge,
            source.gaugeMax > 0f ? Mathf.Clamp01(source.Gauge / source.gaugeMax) : 0f,
            source.IsGaugeReady);
    }

    void HandleActivationRejected(UltimateActivationBlockReason reason, string message)
    {
        if (!showActivationRejectedFeedback)
            return;

        ShowFeedback(ResolveRejectedMessage(reason, message), rejectedColor);
    }

    void HandleUltimatePhaseChanged(UltimateSequencePhase phase, string context)
    {
        if (!showPhaseFeedback)
            return;

        string message = phase switch
        {
            UltimateSequencePhase.PreCast => "ULTIMATE",
            UltimateSequencePhase.FinalExplosion => "FINISH",
            UltimateSequencePhase.Failed => "CANCELLED",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(message))
            ShowFeedback(message, phaseColor);
    }

    string ResolveRejectedMessage(UltimateActivationBlockReason reason, string fallback)
    {
        return reason switch
        {
            UltimateActivationBlockReason.GaugeNotReady => "궁극기 게이지 부족",
            UltimateActivationBlockReason.TargetUnavailable => "대상 없음",
            UltimateActivationBlockReason.SequenceStartFailed => "궁극기 발동 실패",
            UltimateActivationBlockReason.Attacking => "공격 중 사용 불가",
            UltimateActivationBlockReason.Guarding => "가드 중 사용 불가",
            UltimateActivationBlockReason.Dodging => "회피 중 사용 불가",
            UltimateActivationBlockReason.Airborne => "공중 사용 불가",
            UltimateActivationBlockReason.Staggered => "피격 중 사용 불가",
            UltimateActivationBlockReason.InputBlocked => "입력 잠금",
            UltimateActivationBlockReason.AlreadyRunning => "이미 발동 중",
            _ => string.IsNullOrWhiteSpace(fallback) ? "궁극기 사용 불가" : fallback
        };
    }

    void ShowFeedback(string message, Color color)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        EnsureFeedbackView();
        if (feedbackText == null || feedbackGroup == null)
            return;

        feedbackText.text = message;
        feedbackText.color = color;
        feedbackGroup.alpha = 1f;
        feedbackGroup.gameObject.SetActive(true);
        _feedbackVisibleUntil = Time.unscaledTime + Mathf.Max(0.1f, feedbackHoldDuration);
        _feedbackFadeStart = _feedbackVisibleUntil;
        _feedbackActive = true;
        enabled = true;
    }

    void TickFeedback()
    {
        if (!_feedbackActive || feedbackGroup == null)
            return;

        float now = Time.unscaledTime;
        if (now <= _feedbackVisibleUntil)
            return;

        float fadeT = Mathf.Clamp01((now - _feedbackFadeStart) / Mathf.Max(0.05f, feedbackFadeDuration));
        feedbackGroup.alpha = 1f - fadeT;
        if (fadeT < 1f)
            return;

        feedbackGroup.alpha = 0f;
        feedbackGroup.gameObject.SetActive(false);
        _feedbackActive = false;
    }

    void EnsureFeedbackView()
    {
        if (feedbackText != null && feedbackGroup != null)
            return;

        Transform existing = transform.Find("UltimateActivationFeedback");
        if (existing == null)
        {
            GameObject feedbackObject = new GameObject("UltimateActivationFeedback", typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
            feedbackObject.transform.SetParent(transform, false);
            RectTransform rect = feedbackObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -34f);
            rect.sizeDelta = new Vector2(360f, 36f);
            existing = feedbackObject.transform;
        }

        feedbackGroup = existing.GetComponent<CanvasGroup>();
        if (feedbackGroup == null)
            feedbackGroup = existing.gameObject.AddComponent<CanvasGroup>();

        feedbackText = existing.GetComponent<TMP_Text>();
        if (feedbackText == null)
            feedbackText = existing.gameObject.AddComponent<TextMeshProUGUI>();

        feedbackText.alignment = TextAlignmentOptions.Center;
        feedbackText.fontSize = 16f;
        feedbackText.raycastTarget = false;
        feedbackText.enableWordWrapping = false;
        feedbackText.overflowMode = TextOverflowModes.Ellipsis;
        feedbackGroup.alpha = 0f;
        feedbackGroup.gameObject.SetActive(false);
    }
}
