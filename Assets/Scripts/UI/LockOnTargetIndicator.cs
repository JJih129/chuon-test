using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LockOnTargetIndicator : MonoBehaviour
{
    static Sprite _triangleSprite;

    [SerializeField] private Camera targetCamera;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.55f, 0f);
    [SerializeField] private Color color = new Color(1f, 0.05f, 0.03f, 0.95f);
    [SerializeField, Min(8f)] private float size = 28f;
    [SerializeField, Min(0f)] private float pulseScale = 0.12f;
    [SerializeField, Min(0f)] private float pulseSpeed = 5.5f;

    RectTransform _rect;
    RectTransform _canvasRect;
    Image _image;

    public void Configure(Camera camera, Canvas canvas, Color indicatorColor, float indicatorSize, Vector3 offset)
    {
        targetCamera = camera;
        targetCanvas = canvas;
        color = indicatorColor;
        size = Mathf.Max(8f, indicatorSize);
        worldOffset = offset;
        EnsureBuilt();
        ApplyStyle();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        gameObject.SetActive(target != null);
    }

    void Awake()
    {
        EnsureBuilt();
        ApplyStyle();
    }

    void LateUpdate()
    {
        if (target == null)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || _rect == null || _canvasRect == null)
            return;

        Vector3 screen = targetCamera.WorldToScreenPoint(target.position + worldOffset);
        if (screen.z <= 0f)
        {
            _image.enabled = false;
            return;
        }

        _image.enabled = true;
        Camera eventCamera = targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, eventCamera, out Vector2 localPoint);
        _rect.anchoredPosition = localPoint;

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseScale;
        _rect.localScale = Vector3.one * pulse;
    }

    void EnsureBuilt()
    {
        if (_rect == null)
            _rect = GetComponent<RectTransform>();

        if (_rect == null)
            _rect = gameObject.AddComponent<RectTransform>();

        if (targetCanvas == null)
            targetCanvas = GetComponentInParent<Canvas>();

        if (targetCanvas == null)
            targetCanvas = CreateCanvas();

        if (transform.parent != targetCanvas.transform)
            transform.SetParent(targetCanvas.transform, false);

        _canvasRect = targetCanvas.GetComponent<RectTransform>();

        if (_image == null)
            _image = GetComponent<Image>();

        if (_image == null)
            _image = gameObject.AddComponent<Image>();

        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = new Vector2(size, size);
        _rect.localRotation = Quaternion.identity;
    }

    void ApplyStyle()
    {
        if (_image == null)
            return;

        _rect.sizeDelta = new Vector2(size, size);
        _image.sprite = GetTriangleSprite();
        _image.color = color;
        _image.raycastTarget = false;
    }

    static Canvas CreateCanvas()
    {
        GameObject canvasObject = new GameObject("RuntimeLockOnIndicatorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    static Sprite GetTriangleSprite()
    {
        if (_triangleSprite == null)
            _triangleSprite = CreateTriangleSprite();

        return _triangleSprite;
    }

    static Sprite CreateTriangleSprite()
    {
        Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.name = "RuntimeLockOnTriangle";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color clear = new Color(1f, 1f, 1f, 0f);
        Color white = Color.white;
        for (int y = 0; y < 32; y++)
        {
            float halfWidth = y * 0.5f;
            for (int x = 0; x < 32; x++)
            {
                bool inside = Mathf.Abs(x - 15.5f) <= halfWidth && y >= 3 && y <= 28;
                texture.SetPixel(x, y, inside ? white : clear);
            }
        }

        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
    }
}
