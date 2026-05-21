using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HudFocusCueController : MonoBehaviour
{
    const string RuntimeRootName = "_RuntimeHudFocusCue";
    const float DefaultDuration = 8f;

    static readonly Vector3[] s_Corners = new Vector3[4];

    [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.58f;
    [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.58f);
    [SerializeField] private Color focusColor = new Color(0.15f, 0.92f, 1f, 0.95f);
    [SerializeField, Min(0f)] private float padding = 18f;
    [SerializeField, Min(0f)] private float borderThickness = 5f;
    [SerializeField, Min(0.01f)] private float pulseSpeed = 4.8f;
    [SerializeField, Range(0f, 0.35f)] private float pulseScale = 0.09f;

    RectTransform _canvasRect;
    Canvas _canvas;
    Camera _eventCamera;
    RectTransform _root;
    Image[] _dimPanels;
    Image[] _borderPanels;
    RectTransform _target;
    float _hideAt;
    bool _visible;

    public static void ShowGlobal(HudFocusCueId cueId, float duration = DefaultDuration)
    {
        RectTransform target = ResolveTarget(cueId);
        if (target == null)
            return;

        Canvas canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        HudFocusCueController controller = canvas.GetComponentInChildren<HudFocusCueController>(true);
        if (controller == null)
        {
            GameObject controllerObject = new GameObject(RuntimeRootName, typeof(RectTransform), typeof(HudFocusCueController));
            RectTransform rect = controllerObject.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            controller = controllerObject.GetComponent<HudFocusCueController>();
        }

        controller.Show(target, duration);
    }

    public static void HideGlobal()
    {
        HudFocusCueController[] controllers = FindObjectsOfType<HudFocusCueController>(true);
        for (int i = 0; i < controllers.Length; i++)
            controllers[i].Hide();
    }

    public void Show(RectTransform target, float duration = DefaultDuration)
    {
        if (target == null)
            return;

        _target = target;
        _hideAt = Time.unscaledTime + Mathf.Max(0.15f, duration);
        _visible = true;
        EnsureVisuals();
        if (_root != null)
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
        }
    }

    public void Hide()
    {
        _visible = false;
        _target = null;
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (!_visible)
            return;

        if (_target == null || Time.unscaledTime >= _hideAt)
        {
            Hide();
            return;
        }

        EnsureVisuals();
        UpdateVisuals();
    }

    void EnsureVisuals()
    {
        if (_canvas == null)
            _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
            return;

        _canvasRect = _canvas.transform as RectTransform;
        _eventCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        if (_root == null)
        {
            _root = transform as RectTransform;
            if (_root == null)
                _root = gameObject.AddComponent<RectTransform>();
        }

        Stretch(_root);

        if (_dimPanels == null || _dimPanels.Length != 4)
        {
            _dimPanels = new Image[4];
            for (int i = 0; i < _dimPanels.Length; i++)
                _dimPanels[i] = CreateImage("Dim_" + i, _root, dimColor);
        }

        if (_borderPanels == null || _borderPanels.Length != 4)
        {
            _borderPanels = new Image[4];
            for (int i = 0; i < _borderPanels.Length; i++)
                _borderPanels[i] = CreateImage("Border_" + i, _root, focusColor);
        }
    }

    void UpdateVisuals()
    {
        if (_canvasRect == null || _target == null)
            return;

        if (!TryGetTargetRect(out Rect targetRect))
            return;

        Rect canvasRect = _canvasRect.rect;
        float pad = padding;
        float left = Mathf.Clamp(targetRect.xMin - pad, canvasRect.xMin, canvasRect.xMax);
        float right = Mathf.Clamp(targetRect.xMax + pad, canvasRect.xMin, canvasRect.xMax);
        float bottom = Mathf.Clamp(targetRect.yMin - pad, canvasRect.yMin, canvasRect.yMax);
        float top = Mathf.Clamp(targetRect.yMax + pad, canvasRect.yMin, canvasRect.yMax);

        Color dim = dimColor;
        dim.a = dimAlpha;
        for (int i = 0; i < _dimPanels.Length; i++)
            _dimPanels[i].color = dim;

        SetPanel(_dimPanels[0].rectTransform, canvasRect.xMin, left, canvasRect.yMin, canvasRect.yMax);
        SetPanel(_dimPanels[1].rectTransform, right, canvasRect.xMax, canvasRect.yMin, canvasRect.yMax);
        SetPanel(_dimPanels[2].rectTransform, left, right, top, canvasRect.yMax);
        SetPanel(_dimPanels[3].rectTransform, left, right, canvasRect.yMin, bottom);

        float pulse = 1f + ((Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.5f + 0.5f) * pulseScale);
        float expand = (pulse - 1f) * Mathf.Max(right - left, top - bottom);
        float x0 = Mathf.Max(canvasRect.xMin, left - expand);
        float x1 = Mathf.Min(canvasRect.xMax, right + expand);
        float y0 = Mathf.Max(canvasRect.yMin, bottom - expand);
        float y1 = Mathf.Min(canvasRect.yMax, top + expand);
        float thickness = Mathf.Max(1f, borderThickness);

        Color border = focusColor;
        border.a = Mathf.Lerp(0.55f, focusColor.a, pulse);
        for (int i = 0; i < _borderPanels.Length; i++)
            _borderPanels[i].color = border;

        SetPanel(_borderPanels[0].rectTransform, x0, x1, y1 - thickness, y1);
        SetPanel(_borderPanels[1].rectTransform, x0, x1, y0, y0 + thickness);
        SetPanel(_borderPanels[2].rectTransform, x0, x0 + thickness, y0, y1);
        SetPanel(_borderPanels[3].rectTransform, x1 - thickness, x1, y0, y1);
    }

    bool TryGetTargetRect(out Rect result)
    {
        result = default;
        _target.GetWorldCorners(s_Corners);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < s_Corners.Length; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(_eventCamera, s_Corners[i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, _eventCamera, out Vector2 local))
                return false;

            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        if (max.x <= min.x || max.y <= min.y)
            return false;

        result = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    static RectTransform ResolveTarget(HudFocusCueId cueId)
    {
        HudFocusTarget[] explicitTargets = FindObjectsOfType<HudFocusTarget>(true);
        for (int i = 0; i < explicitTargets.Length; i++)
        {
            if (explicitTargets[i] != null && explicitTargets[i].CueId == cueId && explicitTargets[i].RectTransform != null)
                return explicitTargets[i].RectTransform;
        }

        PlayerHUD hud = FindObjectOfType<PlayerHUD>(true);
        if (hud == null)
            return null;

        return cueId switch
        {
            HudFocusCueId.Ampoule => hud.GetAmpouleFocusTarget(),
            HudFocusCueId.UltimateGauge => hud.GetUltimateGaugeFocusTarget(),
            _ => null
        };
    }

    static Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static void SetPanel(RectTransform rect, float xMin, float xMax, float yMin, float yMax)
    {
        float width = Mathf.Max(0f, xMax - xMin);
        float height = Mathf.Max(0f, yMax - yMin);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
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
