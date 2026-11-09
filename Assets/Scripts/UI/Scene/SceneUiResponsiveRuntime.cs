using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SceneUiResponsiveRuntime : MonoBehaviour
{
    struct RectSnapshot
    {
        public Vector2 offsetMin;
        public Vector2 offsetMax;
    }

    static SceneUiResponsiveRuntime s_instance;

    // 원본 오프셋 스냅샷 (RectTransform 기준). 이미 보정된 값을 다시 스냅샷하지 않도록 최초 1회만 기록.
    readonly Dictionary<RectTransform, RectSnapshot> _snapshots = new Dictionary<RectTransform, RectSnapshot>(128);
    readonly List<RectTransform> _deadKeys = new List<RectTransform>(16);

    Vector2Int _lastScreenSize;
    Rect _lastSafeArea;
    int _deferredRefreshFrame = -1;

    /// <summary>런타임에 생성된 Canvas 등, 다음 프레임에 1회 재적용을 요청.</summary>
    public static void RequestRefresh()
    {
        if (s_instance != null)
            s_instance._deferredRefreshFrame = Time.frameCount + 1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (s_instance != null)
            return;

        var root = new GameObject("SceneUiResponsiveRuntime");
        DontDestroyOnLoad(root);
        s_instance = root.AddComponent<SceneUiResponsiveRuntime>();
    }

    void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshAll();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // 주기 폴링 없음: 화면 크기/세이프 영역 변경, 씬 로드(+Start 생성분 대비 지연 1회), 명시 요청 시에만 갱신.
    void Update()
    {
        bool changed = Screen.width != _lastScreenSize.x
            || Screen.height != _lastScreenSize.y
            || Screen.safeArea != _lastSafeArea;
        bool deferred = _deferredRefreshFrame >= 0 && Time.frameCount >= _deferredRefreshFrame;
        if (!changed && !deferred)
            return;

        _deferredRefreshFrame = -1;
        RefreshAll();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PruneDeadSnapshots();
        RefreshAll();
        // Start()에서 생성되는 Canvas 대비 지연 1회 갱신
        _deferredRefreshFrame = Time.frameCount + 2;
    }

    void PruneDeadSnapshots()
    {
        _deadKeys.Clear();
        foreach (var key in _snapshots.Keys)
        {
            if (key == null)
                _deadKeys.Add(key);
        }

        for (int i = 0; i < _deadKeys.Count; i++)
            _snapshots.Remove(_deadKeys[i]);
        _deadKeys.Clear();
    }

    void RefreshAll()
    {
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        _lastSafeArea = Screen.safeArea;

        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
                continue;

            NormalizeCanvasScaler(canvas);
            ApplySafeAreaToCanvasChildren(canvas);
        }
    }

    static void NormalizeCanvasScaler(Canvas canvas)
    {
        if (canvas == null)
            return;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    void ApplySafeAreaToCanvasChildren(Canvas canvas)
    {
        if (canvas == null || canvas.transform is not RectTransform canvasRect)
            return;

        float width = Mathf.Max(1f, canvasRect.rect.width);
        float height = Mathf.Max(1f, canvasRect.rect.height);
        float left = Screen.safeArea.xMin / Mathf.Max(1f, Screen.width) * width;
        float right = (Screen.width - Screen.safeArea.xMax) / Mathf.Max(1f, Screen.width) * width;
        float bottom = Screen.safeArea.yMin / Mathf.Max(1f, Screen.height) * height;
        float top = (Screen.height - Screen.safeArea.yMax) / Mathf.Max(1f, Screen.height) * height;

        for (int i = 0; i < canvas.transform.childCount; i++)
        {
            if (canvas.transform.GetChild(i) is not RectTransform child)
                continue;

            if (ShouldSkipSafeArea(child))
                continue;

            if (!_snapshots.TryGetValue(child, out var snapshot))
            {
                snapshot = new RectSnapshot
                {
                    offsetMin = child.offsetMin,
                    offsetMax = child.offsetMax
                };
                _snapshots[child] = snapshot;
            }

            Vector2 offsetMin = snapshot.offsetMin;
            Vector2 offsetMax = snapshot.offsetMax;

            if (child.anchorMin.x <= 0.001f)
                offsetMin.x = snapshot.offsetMin.x + left;
            if (child.anchorMax.x >= 0.999f)
                offsetMax.x = snapshot.offsetMax.x - right;
            if (child.anchorMin.y <= 0.001f)
                offsetMin.y = snapshot.offsetMin.y + bottom;
            if (child.anchorMax.y >= 0.999f)
                offsetMax.y = snapshot.offsetMax.y - top;

            child.offsetMin = offsetMin;
            child.offsetMax = offsetMax;
        }
    }

    static bool ShouldSkipSafeArea(RectTransform rectTransform)
    {
        string name = rectTransform.name;
        if (string.IsNullOrEmpty(name))
            return false;

        return Has(name, "background")
            || Has(name, "_bg")
            || name.EndsWith("bg", StringComparison.OrdinalIgnoreCase)
            || Has(name, "backdrop")
            || Has(name, "fade")
            || Has(name, "overlay")
            || Has(name, "blocker")
            || Has(name, "dim")
            || Has(name, "letterbox");
    }

    static bool Has(string source, string value)
    {
        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
