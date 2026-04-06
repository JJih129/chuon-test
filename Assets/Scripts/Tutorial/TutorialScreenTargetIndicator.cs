using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class TutorialScreenTargetIndicator : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Transform targetTransform;
    [SerializeField] private Vector3 targetWorldOffset = new Vector3(0f, 1.25f, 0f);

    [Header("Text")]
    [SerializeField] private string labelText = "\ud45c\uc801";
    [SerializeField] private Color indicatorColor = new Color(0.24f, 0.88f, 1f, 1f);
    [SerializeField] private float arrowFontSize = 42f;
    [SerializeField] private float labelFontSize = 22f;

    [Header("Layout")]
    [SerializeField] private Vector2 edgePadding = new Vector2(92f, 108f);
    [SerializeField] private float onScreenYOffset = 54f;
    [SerializeField] private float offScreenScale = 1.08f;
    [SerializeField] private float onScreenScale = 0.96f;
    [SerializeField, Range(0.01f, 0.3f)] private float screenMargin = 0.06f;

    [Header("Pulse")]
    [SerializeField, Min(0f)] private float pulseSpeed = 3.6f;
    [SerializeField, Min(0f)] private float scalePulseAmplitude = 0.06f;
    [SerializeField, Range(0f, 1f)] private float minimumPulseAlpha = 0.72f;

    RectTransform _rootRect;
    RectTransform _canvasRect;
    TextMeshProUGUI _arrowText;
    TextMeshProUGUI _labelText;

    public void ConfigureRuntime(Camera runtimeCamera, Canvas runtimeCanvas, Transform runtimeTarget, string runtimeLabel, Color runtimeColor)
    {
        targetCamera = runtimeCamera;
        targetCanvas = runtimeCanvas;
        targetTransform = runtimeTarget;
        labelText = string.IsNullOrWhiteSpace(runtimeLabel) ? labelText : runtimeLabel;
        indicatorColor = runtimeColor;

        EnsureBuilt();
        ApplyStyle();
    }

    public void SetRuntimeStyle(string runtimeLabel, Color runtimeColor, float runtimeOnScreenScale, float runtimeOffScreenScale)
    {
        if (!string.IsNullOrWhiteSpace(runtimeLabel))
            labelText = runtimeLabel;

        indicatorColor = runtimeColor;
        onScreenScale = runtimeOnScreenScale;
        offScreenScale = runtimeOffScreenScale;

        EnsureBuilt();
        ApplyStyle();
    }

    void Awake()
    {
        EnsureBuilt();
        ApplyStyle();
    }

    void OnEnable()
    {
        EnsureBuilt();
        ApplyStyle();
    }

    void Update()
    {
        if (_rootRect == null || _canvasRect == null || targetCamera == null || targetTransform == null)
            return;

        Vector3 worldPoint = targetTransform.position + targetWorldOffset;
        Vector3 viewport = targetCamera.WorldToViewportPoint(worldPoint);
        bool behind = viewport.z <= 0f;
        if (behind)
        {
            viewport.x = 1f - viewport.x;
            viewport.y = 1f - viewport.y;
        }

        bool onScreen = !behind &&
                        viewport.x >= screenMargin &&
                        viewport.x <= 1f - screenMargin &&
                        viewport.y >= screenMargin &&
                        viewport.y <= 1f - screenMargin;

        if (onScreen)
        {
            PositionOnScreen(worldPoint);
            ApplyPulse(onScreenScale);
            return;
        }

        PositionOffScreen(viewport);
        ApplyPulse(offScreenScale);
    }

    void EnsureBuilt()
    {
        if (_rootRect == null)
            _rootRect = GetComponent<RectTransform>();

        if (targetCanvas != null && transform.parent != targetCanvas.transform)
            transform.SetParent(targetCanvas.transform, false);

        if (_rootRect == null)
            _rootRect = gameObject.AddComponent<RectTransform>();

        _canvasRect = targetCanvas != null ? targetCanvas.GetComponent<RectTransform>() : null;

        Transform arrow = transform.Find("Arrow");
        if (arrow == null)
        {
            GameObject arrowObject = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            arrowObject.transform.SetParent(transform, false);
            arrow = arrowObject.transform;
        }

        _arrowText = arrow.GetComponent<TextMeshProUGUI>();

        Transform label = transform.Find("Label");
        if (label == null)
        {
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(transform, false);
            label = labelObject.transform;
        }

        _labelText = label.GetComponent<TextMeshProUGUI>();

        _rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        _rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        _rootRect.pivot = new Vector2(0.5f, 0.5f);
        _rootRect.sizeDelta = new Vector2(120f, 120f);

        RectTransform arrowRect = _arrowText.rectTransform;
        arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = new Vector2(0f, 12f);
        arrowRect.sizeDelta = new Vector2(72f, 72f);

        RectTransform labelRect = _labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(0f, -22f);
        labelRect.sizeDelta = new Vector2(160f, 44f);
    }

    void ApplyStyle()
    {
        if (_arrowText == null || _labelText == null)
            return;

        _arrowText.text = "\u25bc";
        _arrowText.fontSize = arrowFontSize;
        _arrowText.alignment = TextAlignmentOptions.Center;
        _arrowText.color = indicatorColor;
        _arrowText.raycastTarget = false;

        _labelText.text = labelText;
        _labelText.fontSize = labelFontSize;
        _labelText.alignment = TextAlignmentOptions.Center;
        _labelText.color = indicatorColor;
        _labelText.raycastTarget = false;
    }

    void PositionOnScreen(Vector3 worldPoint)
    {
        Camera eventCamera = targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            targetCamera.WorldToScreenPoint(worldPoint),
            eventCamera,
            out Vector2 localPoint);

        _rootRect.anchoredPosition = localPoint + Vector2.up * onScreenYOffset;
        _arrowText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
    }

    void PositionOffScreen(Vector3 viewport)
    {
        Vector2 direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.up;

        direction.Normalize();

        float halfWidth = (_canvasRect.rect.width * 0.5f) - edgePadding.x;
        float halfHeight = (_canvasRect.rect.height * 0.5f) - edgePadding.y;
        float scale = float.MaxValue;

        if (Mathf.Abs(direction.x) > 0.001f)
            scale = Mathf.Min(scale, halfWidth / Mathf.Abs(direction.x));
        if (Mathf.Abs(direction.y) > 0.001f)
            scale = Mathf.Min(scale, halfHeight / Mathf.Abs(direction.y));
        if (!float.IsFinite(scale))
            scale = 0f;

        _rootRect.anchoredPosition = direction * scale;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        _arrowText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    void ApplyPulse(float baseScale)
    {
        if (_rootRect == null || _arrowText == null || _labelText == null)
            return;

        float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.5f);
        float scale = baseScale + (scalePulseAmplitude * pulse);
        float alpha = Mathf.Lerp(minimumPulseAlpha, 1f, pulse);

        _rootRect.localScale = Vector3.one * scale;

        Color currentColor = indicatorColor;
        currentColor.a *= alpha;
        _arrowText.color = currentColor;
        _labelText.color = currentColor;
    }
}
