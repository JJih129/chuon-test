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
    [SerializeField] private Color color = new Color(0.86f, 0.97f, 1f, 0.92f); // 중립 화이트/시안 (위험 신호 적색과 구분)
    [SerializeField, Min(8f)] private float size = 28f;
    [SerializeField, Min(0f)] private float pulseScale = 0.12f;
    [SerializeField, Min(0f)] private float pulseSpeed = 5.5f;

    RectTransform _rect;
    RectTransform _canvasRect;
    Image _image;

    // 다이아몬드(소울라이크 프로필) 스타일: 가슴 위치, 흰 다이아몬드 + 붉은 코어, 전환 시 스냅
    static Sprite _diamondSprite;
    bool _diamondStyle;
    float _chestRatio = 0.72f;
    float _snapTime = 0.1f;
    float _snapStartedAt = -10f;
    Image _coreImage;
    Color _accentColor = Color.red;

    public void Configure(Camera camera, Canvas canvas, Color indicatorColor, float indicatorSize, Vector3 offset)
    {
        targetCamera = camera;
        targetCanvas = canvas;
        color = indicatorColor;
        size = Mathf.Max(8f, indicatorSize);
        worldOffset = offset;
        _diamondStyle = false;
        EnsureBuilt();
        ApplyStyle();
    }

    public void ConfigureDiamond(Camera camera, Canvas canvas, Color mainColor, Color accentColor, float indicatorSize, float snapTime, float chestRatio)
    {
        targetCamera = camera;
        targetCanvas = canvas;
        color = mainColor;
        _accentColor = accentColor;
        size = Mathf.Max(8f, indicatorSize);
        worldOffset = Vector3.zero;
        _snapTime = Mathf.Max(0.01f, snapTime);
        _chestRatio = chestRatio;
        _diamondStyle = true;
        EnsureBuilt();
        ApplyStyle();
    }

    public void SetTarget(Transform newTarget)
    {
        if (newTarget != null && newTarget != target)
            _snapStartedAt = Time.unscaledTime;
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

        Vector3 world = _diamondStyle ? SoulsCameraRig.GetTargetChestPoint(target, _chestRatio) : target.position + worldOffset;
        Vector3 screen = targetCamera.WorldToScreenPoint(world);
        if (screen.z <= 0f)
        {
            _image.enabled = false;
            if (_coreImage != null) _coreImage.enabled = false;
            return;
        }

        _image.enabled = true;
        Camera eventCamera = targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, eventCamera, out Vector2 localPoint);
        _rect.anchoredPosition = localPoint;

        if (_diamondStyle)
        {
            // 전환 스냅: 크게/흐리게 시작해 snapTime 안에 제자리로
            float t = Mathf.Clamp01((Time.unscaledTime - _snapStartedAt) / _snapTime);
            float ease = 1f - (1f - t) * (1f - t);
            _rect.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, ease);
            Color c = color;
            c.a *= Mathf.Lerp(0.35f, 1f, ease);
            _image.color = c;
            if (_coreImage != null)
            {
                _coreImage.enabled = true;
                _coreImage.color = _accentColor;
            }
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseScale;
        _rect.localScale = Vector3.one * pulse;
    }

    void EnsureBuilt()
    {
        if (_rect == null)
            _rect = GetComponent<RectTransform>();

        if (_rect == null)
            _rect = gameObject.AddComponent<RectTransform>();

        // 전달된 캔버스가 꺼져 있거나(예: CombatDebugHudCanvas) 오버레이가 아니면 표식이 보이지 않는다 → 전용 오버레이 캔버스 사용
        if (!IsUsableCanvas(targetCanvas))
            targetCanvas = GetComponentInParent<Canvas>();

        if (!IsUsableCanvas(targetCanvas))
            targetCanvas = ResolveSharedCanvas();

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
        _image.sprite = _diamondStyle ? GetDiamondSprite() : GetTriangleSprite();
        _image.color = color;
        _image.raycastTarget = false;

        if (_diamondStyle && _coreImage == null)
        {
            GameObject core = new GameObject("Core", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            core.transform.SetParent(transform, false);
            RectTransform coreRect = core.GetComponent<RectTransform>();
            coreRect.anchorMin = coreRect.anchorMax = coreRect.pivot = new Vector2(0.5f, 0.5f);
            coreRect.localRotation = Quaternion.Euler(0f, 0f, 45f); // 단색 사각형을 45° 돌려 채워진 다이아몬드 코어
            _coreImage = core.GetComponent<Image>();
            _coreImage.raycastTarget = false;
        }

        if (_coreImage != null)
        {
            _coreImage.rectTransform.sizeDelta = new Vector2(size * 0.22f, size * 0.22f);
            _coreImage.color = _accentColor;
            _coreImage.gameObject.SetActive(_diamondStyle);
        }
    }

    static Sprite GetDiamondSprite()
    {
        if (_diamondSprite != null)
            return _diamondSprite;

        const int n = 40;
        const float c = 19.5f;
        Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
        texture.name = "RuntimeLockOnDiamond";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // 윤곽선 다이아몬드 (|x|+|y| 링) + 어두운 테두리: 밝은 보스 VFX 위에서도 읽히게. 코어는 별도 이미지
                float d = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                float ring = Mathf.Clamp01(17f - d) * Mathf.Clamp01(d - 11f);
                float edge = Mathf.Clamp01(19.2f - d) * Mathf.Clamp01(d - 8.8f);
                float a = Mathf.Max(ring, edge * 0.75f);
                float lum = a > 0.001f ? ring / a : 0f;
                texture.SetPixel(x, y, new Color(lum, lum, lum, a));
            }
        }
        texture.Apply(false, true);
        _diamondSprite = Sprite.Create(texture, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 40f);
        return _diamondSprite;
    }

    static Canvas s_sharedCanvas;

    static bool IsUsableCanvas(Canvas canvas)
    {
        return canvas != null && canvas.enabled && canvas.gameObject.activeInHierarchy && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
    }

    static Canvas ResolveSharedCanvas()
    {
        if (s_sharedCanvas == null)
            s_sharedCanvas = CreateCanvas();
        return s_sharedCanvas;
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
