using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PerformanceDiagnosticsRuntime : MonoBehaviour
{
    const float HudRefreshInterval = 0.25f;
    const float PingRefreshInterval = 2f;
    const float PingTimeoutSeconds = 2.5f;
    const string DefaultPingAddress = "1.1.1.1";
    const int CanvasSortOrder = 5000;

    static readonly Color AccentColor = new Color(0.22f, 0.86f, 1f, 0.96f);
    static readonly Color BodyColor = new Color(0.90f, 0.97f, 1f, 0.98f);
    static readonly Color MutedColor = new Color(0.72f, 0.84f, 0.92f, 0.82f);
    static readonly Color WarningColor = new Color(1f, 0.78f, 0.36f, 0.98f);
    static readonly Color CriticalColor = new Color(1f, 0.46f, 0.46f, 0.98f);
    static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.55f);

    static PerformanceDiagnosticsRuntime _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        EnsureInstance();
    }

    static void EnsureInstance()
    {
        if (_instance != null)
            return;

        _instance = FindObjectOfType<PerformanceDiagnosticsRuntime>(true);
        if (_instance != null)
        {
            _instance.Initialize();
            return;
        }

        GameObject go = new GameObject("PerformanceDiagnosticsRuntime");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<PerformanceDiagnosticsRuntime>();
        _instance.Initialize();
    }

    enum PingState
    {
        Idle = 0,
        Measuring = 1,
        Ready = 2,
        Offline = 3,
        Timeout = 4,
        Error = 5
    }

    Canvas _canvas;
    CanvasScaler _canvasScaler;
    RectTransform _root;
    TextMeshProUGUI _fpsLabel;
    TextMeshProUGUI _fpsValue;
    TextMeshProUGUI _fpsDetail;
    TextMeshProUGUI _pingLabel;
    TextMeshProUGUI _pingValue;
    TextMeshProUGUI _pingDetail;
    TMP_FontAsset _fontAsset;
    [SerializeField] KeyCode toggleKey = KeyCode.F11;
    bool _visible = true;
    Ping _activePing;
    float _nextHudRefreshAt;
    float _nextPingRefreshAt;
    float _pingTimeoutAt;
    float _sampledFrameTime;
    int _sampledFrameCount;
    float _fps;
    float _frameMs;
    int _pingMs = -1;
    PingState _pingState = PingState.Idle;
    Rect _lastSafeArea;
    Vector2Int _lastScreenSize;
    bool _isInitialized;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Initialize();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void Initialize()
    {
        if (_isInitialized)
            return;

        EnsureHud();
        _nextHudRefreshAt = Time.unscaledTime + HudRefreshInterval;
        _nextPingRefreshAt = Time.unscaledTime;
        _isInitialized = true;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureHud();
        UpdateSafeAreaLayout(force: true);
        RefreshHud(force: true);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            _visible = !_visible;
            ApplyVisibility();
        }

        if (_canvas == null || _root == null)
            EnsureHud();

        if (!_visible)
            return;

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime > 0f)
        {
            _sampledFrameCount++;
            _sampledFrameTime += deltaTime;
        }

        float now = Time.unscaledTime;
        bool hudChanged = false;

        if (now >= _nextHudRefreshAt)
        {
            UpdateFrameStats();
            _nextHudRefreshAt = now + HudRefreshInterval;
            hudChanged = true;
        }

        if (UpdatePingState(now))
            hudChanged = true;

        UpdateSafeAreaLayout(force: false);

        if (hudChanged)
            RefreshHud(force: false);
    }

    void EnsureHud()
    {
        if (_canvas == null)
        {
            GameObject canvasObject = new GameObject("PerformanceDiagnosticsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortOrder;
            _canvas.pixelPerfect = false;

            _canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            _canvasScaler.matchWidthOrHeight = 0.5f;
        }

        if (_root == null)
        {
            GameObject rootObject = new GameObject("DiagnosticsRoot", typeof(RectTransform));
            _root = rootObject.GetComponent<RectTransform>();
            _root.SetParent(_canvas.transform, false);
            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.sizeDelta = new Vector2(320f, 84f);
        }

        if (_fontAsset == null)
            _fontAsset = ResolveFontAsset();

        if (_fpsLabel == null)
            _fpsLabel = CreateText("FpsLabel", new Vector2(0f, 0f), new Vector2(52f, 24f), 18f, FontStyles.Bold, AccentColor, TextAlignmentOptions.TopLeft);
        if (_fpsValue == null)
            _fpsValue = CreateText("FpsValue", new Vector2(58f, -2f), new Vector2(96f, 30f), 28f, FontStyles.Bold, BodyColor, TextAlignmentOptions.TopLeft);
        if (_fpsDetail == null)
            _fpsDetail = CreateText("FpsDetail", new Vector2(150f, 4f), new Vector2(148f, 22f), 18f, FontStyles.Normal, MutedColor, TextAlignmentOptions.TopLeft);
        if (_pingLabel == null)
            _pingLabel = CreateText("PingLabel", new Vector2(0f, -38f), new Vector2(52f, 24f), 18f, FontStyles.Bold, AccentColor, TextAlignmentOptions.TopLeft);
        if (_pingValue == null)
            _pingValue = CreateText("PingValue", new Vector2(58f, -40f), new Vector2(120f, 30f), 28f, FontStyles.Bold, BodyColor, TextAlignmentOptions.TopLeft);
        if (_pingDetail == null)
            _pingDetail = CreateText("PingDetail", new Vector2(150f, -34f), new Vector2(148f, 22f), 18f, FontStyles.Normal, MutedColor, TextAlignmentOptions.TopLeft);

        UpdateSafeAreaLayout(force: true);
        RefreshHud(force: true);
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        if (_canvas != null)
            _canvas.enabled = _visible;
    }

    TextMeshProUGUI CreateText(
        string objectName,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(Shadow));
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.SetParent(_root, false);
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.richText = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        if (_fontAsset != null)
            text.font = _fontAsset;

        Shadow shadow = textObject.GetComponent<Shadow>();
        shadow.effectColor = ShadowColor;
        shadow.effectDistance = new Vector2(1.2f, -1.2f);
        shadow.useGraphicAlpha = true;

        return text;
    }

    TMP_FontAsset ResolveFontAsset()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        TextMeshProUGUI existing = FindObjectOfType<TextMeshProUGUI>(true);
        return existing != null ? existing.font : null;
    }

    void UpdateFrameStats()
    {
        if (_sampledFrameCount <= 0 || _sampledFrameTime <= 0f)
            return;

        _fps = _sampledFrameCount / _sampledFrameTime;
        _frameMs = 1000f / Mathf.Max(_fps, 0.0001f);
        _sampledFrameCount = 0;
        _sampledFrameTime = 0f;
    }

    bool UpdatePingState(float now)
    {
        NetworkReachability reachability = Application.internetReachability;
        if (reachability == NetworkReachability.NotReachable)
        {
            if (_pingState != PingState.Offline)
            {
                _pingState = PingState.Offline;
                _pingMs = -1;
                _activePing = null;
                _nextPingRefreshAt = now + PingRefreshInterval;
                return true;
            }

            return false;
        }

        if (_activePing != null)
        {
            if (_activePing.isDone)
            {
                _pingMs = Mathf.Max(0, _activePing.time);
                _pingState = PingState.Ready;
                _activePing = null;
                _nextPingRefreshAt = now + PingRefreshInterval;
                return true;
            }

            if (now >= _pingTimeoutAt)
            {
                _pingState = PingState.Timeout;
                _pingMs = -1;
                _activePing = null;
                _nextPingRefreshAt = now + PingRefreshInterval;
                return true;
            }

            return false;
        }

        if (now < _nextPingRefreshAt)
            return false;

        try
        {
            _activePing = new Ping(DefaultPingAddress);
            _pingState = PingState.Measuring;
            _pingTimeoutAt = now + PingTimeoutSeconds;
            return true;
        }
        catch
        {
            _pingState = PingState.Error;
            _pingMs = -1;
            _activePing = null;
            _nextPingRefreshAt = now + PingRefreshInterval;
            return true;
        }
    }

    void UpdateSafeAreaLayout(bool force)
    {
        if (_root == null)
            return;

        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        Rect safeArea = Screen.safeArea;
        if (!force && screenSize == _lastScreenSize && safeArea == _lastSafeArea)
            return;

        _lastScreenSize = screenSize;
        _lastSafeArea = safeArea;

        float topInset = Mathf.Max(0f, Screen.height - safeArea.yMax);
        float leftInset = Mathf.Max(0f, safeArea.xMin);
        _root.anchoredPosition = new Vector2(leftInset + 24f, -(topInset + 18f));
    }

    void RefreshHud(bool force)
    {
        if (_fpsLabel == null || _fpsValue == null || _fpsDetail == null || _pingLabel == null || _pingValue == null || _pingDetail == null)
            return;

        _fpsLabel.text = "FPS";
        _pingLabel.text = "PING";

        int roundedFps = Mathf.Max(0, Mathf.RoundToInt(_fps));
        string fpsValue = roundedFps > 0 ? roundedFps.ToString() : "--";
        string fpsDetail = roundedFps > 0 ? _frameMs.ToString("0.0") + " ms" : "sampling";
        Color fpsColor = ResolveFpsColor(roundedFps);

        string pingValue;
        string pingDetail;
        Color pingColor;
        ResolvePingDisplay(out pingValue, out pingDetail, out pingColor);

        if (force || _fpsValue.text != fpsValue)
            _fpsValue.text = fpsValue;
        if (force || _fpsDetail.text != fpsDetail)
            _fpsDetail.text = fpsDetail;
        if (force || _pingValue.text != pingValue)
            _pingValue.text = pingValue;
        if (force || _pingDetail.text != pingDetail)
            _pingDetail.text = pingDetail;

        _fpsValue.color = fpsColor;
        _pingValue.color = pingColor;
        _fpsDetail.color = MutedColor;
        _pingDetail.color = MutedColor;
    }

    void ResolvePingDisplay(out string valueText, out string detailText, out Color valueColor)
    {
        switch (_pingState)
        {
            case PingState.Ready:
                valueText = _pingMs >= 0 ? _pingMs.ToString() : "--";
                detailText = _pingMs >= 0 ? "ms" : string.Empty;
                valueColor = ResolvePingColor(_pingMs);
                break;
            case PingState.Measuring:
                valueText = "...";
                detailText = "measuring";
                valueColor = AccentColor;
                break;
            case PingState.Offline:
                valueText = "--";
                detailText = "offline";
                valueColor = CriticalColor;
                break;
            case PingState.Timeout:
                valueText = "--";
                detailText = "timeout";
                valueColor = WarningColor;
                break;
            case PingState.Error:
                valueText = "--";
                detailText = "unavailable";
                valueColor = WarningColor;
                break;
            default:
                valueText = "--";
                detailText = "waiting";
                valueColor = BodyColor;
                break;
        }
    }

    static Color ResolveFpsColor(int fps)
    {
        if (fps <= 0)
            return BodyColor;
        if (fps < 30)
            return CriticalColor;
        if (fps < 55)
            return WarningColor;
        return BodyColor;
    }

    static Color ResolvePingColor(int pingMs)
    {
        if (pingMs < 0)
            return BodyColor;
        if (pingMs > 180)
            return CriticalColor;
        if (pingMs > 90)
            return WarningColor;
        return BodyColor;
    }
}
