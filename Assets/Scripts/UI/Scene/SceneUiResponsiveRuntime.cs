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

    readonly Dictionary<int, RectSnapshot> _snapshots = new Dictionary<int, RectSnapshot>(128);

    Vector2Int _lastScreenSize;
    Rect _lastSafeArea;
    float _nextRefreshAt;

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
        RefreshAll(force: true);
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Update()
    {
        var screenSize = new Vector2Int(Screen.width, Screen.height);
        var safeArea = Screen.safeArea;
        bool changed = screenSize != _lastScreenSize || safeArea != _lastSafeArea;
        bool periodic = Time.unscaledTime >= _nextRefreshAt;
        if (!changed && !periodic)
            return;

        RefreshAll(changed);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _snapshots.Clear();
        RefreshAll(force: true);
    }

    void RefreshAll(bool force)
    {
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        _lastSafeArea = Screen.safeArea;
        _nextRefreshAt = Time.unscaledTime + 1f;

        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
                continue;

            NormalizeCanvasScaler(canvas);
            ApplySafeAreaToCanvasChildren(canvas, force);
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

    void ApplySafeAreaToCanvasChildren(Canvas canvas, bool force)
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

            int key = child.GetInstanceID();
            if (force || !_snapshots.ContainsKey(key))
            {
                _snapshots[key] = new RectSnapshot
                {
                    offsetMin = child.offsetMin,
                    offsetMax = child.offsetMax
                };
            }

            var snapshot = _snapshots[key];
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

        string lower = name.ToLowerInvariant();
        return lower.Contains("background")
            || lower.Contains("_bg")
            || lower.EndsWith("bg")
            || lower.Contains("backdrop")
            || lower.Contains("fade")
            || lower.Contains("overlay")
            || lower.Contains("blocker")
            || lower.Contains("dim");
    }
}
