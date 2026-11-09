using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 궁극기 연출 동안 게임플레이 HUD 페이드아웃 + 레터박스 바 표시. 종료/취소/비활성화 시 원상 복구.
[DisallowMultipleComponent]
public sealed class UltimateCinematicHudFrame : MonoBehaviour
{
    struct HudEntry
    {
        public CanvasGroup Group;
        public float Alpha;
        public bool Interactable;
        public bool BlocksRaycasts;
        public bool Added;
    }

    [SerializeField, Range(0f, 0.25f)] private float letterboxHeight = 0.1f;
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.28f;
    [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.4f;
    [SerializeField] private int overlaySortingOrder = 900;
    [Tooltip("보스 HUD도 페이드할지 여부. BossUIController.keepVisibleDuringUltimate(피해 확인용)와 충돌하므로 기본 off")]
    [SerializeField] private bool fadeBossHud = true;

    readonly List<HudEntry> _hud = new List<HudEntry>(8);
    readonly List<Canvas> _canvasBuffer = new List<Canvas>(8);
    Canvas _overlay;
    RectTransform _top;
    RectTransform _bottom;
    float _progress;
    int _direction;
    bool _active;

    public bool IsActive => _active;

    public void Begin()
    {
        if (!_active)
        {
            CollectHud();
            _active = true;
        }

        EnsureOverlay();
        _overlay.gameObject.SetActive(true);
        _direction = 1;
        enabled = true;
        Apply();
    }

    public void End(bool immediate)
    {
        if (!_active)
            return;

        if (immediate || !gameObject.activeInHierarchy)
        {
            _progress = 0f;
            Finish();
            return;
        }

        _direction = -1;
        enabled = true;
    }

    void Update()
    {
        // Idle while holding the faded state. Do NOT toggle `enabled` here:
        // OnDisable treats disabling as a cancel and would restore the HUD mid-cinematic.
        if (_direction == 0)
            return;

        float duration = _direction > 0 ? fadeInDuration : fadeOutDuration;
        _progress = Mathf.Clamp01(_progress + _direction * Time.unscaledDeltaTime / duration);
        Apply();

        if (_direction > 0 && _progress >= 1f)
        {
            _direction = 0;
        }
        else if (_direction < 0 && _progress <= 0f)
        {
            Finish();
        }
    }

    void OnDisable()
    {
        if (_active)
        {
            _progress = 0f;
            Finish();
        }
    }

    void OnDestroy()
    {
        if (_active)
        {
            _progress = 0f;
            Finish();
        }

        if (_overlay != null)
            Destroy(_overlay.gameObject);
    }

    void Apply()
    {
        float eased = Mathf.SmoothStep(0f, 1f, _progress);
        float barHeight = letterboxHeight * eased;
        if (_top != null)
            _top.anchorMin = new Vector2(0f, 1f - barHeight);
        if (_bottom != null)
            _bottom.anchorMax = new Vector2(1f, barHeight);

        for (int i = 0; i < _hud.Count; i++)
        {
            CanvasGroup group = _hud[i].Group;
            if (group != null)
                group.alpha = _hud[i].Alpha * (1f - eased);
        }
    }

    void Finish()
    {
        Apply();
        for (int i = 0; i < _hud.Count; i++)
        {
            HudEntry entry = _hud[i];
            if (entry.Group == null)
                continue;

            if (entry.Added)
            {
                Destroy(entry.Group);
                continue;
            }

            entry.Group.alpha = entry.Alpha;
            entry.Group.interactable = entry.Interactable;
            entry.Group.blocksRaycasts = entry.BlocksRaycasts;
        }

        _hud.Clear();
        _active = false;
        _direction = 0;
        if (_overlay != null)
            _overlay.gameObject.SetActive(false);
        enabled = false;
    }

    void CollectHud()
    {
        _hud.Clear();
        _canvasBuffer.Clear();
        // 이름 검색 대신 HUD 컴포넌트 타입 기준으로 루트 캔버스 수집 (연출 시작 시 1회)
        AddRootCanvases(FindObjectsByType<PlayerHUD>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        if (fadeBossHud)
        {
            AddRootCanvases(FindObjectsByType<BossHUD>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            AddRootCanvases(FindObjectsByType<BossBreakHUD>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        for (int i = 0; i < _canvasBuffer.Count; i++)
        {
            Canvas canvas = _canvasBuffer[i];
            CanvasGroup group = canvas.GetComponent<CanvasGroup>();
            bool added = group == null;
            if (added)
                group = canvas.gameObject.AddComponent<CanvasGroup>();

            _hud.Add(new HudEntry
            {
                Group = group,
                Alpha = added ? 1f : group.alpha,
                Interactable = group.interactable,
                BlocksRaycasts = group.blocksRaycasts,
                Added = added
            });
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        _canvasBuffer.Clear();
    }

    void AddRootCanvases<T>(T[] components) where T : Component
    {
        for (int i = 0; i < components.Length; i++)
        {
            Canvas canvas = components[i] != null ? components[i].GetComponentInParent<Canvas>() : null;
            if (canvas == null)
                continue;

            canvas = canvas.rootCanvas;
            if (canvas == null || canvas == _overlay || canvas.renderMode == RenderMode.WorldSpace)
                continue;

            if (!_canvasBuffer.Contains(canvas))
                _canvasBuffer.Add(canvas);
        }
    }

    void EnsureOverlay()
    {
        if (_overlay != null)
            return;

        var root = new GameObject("UltimateLetterboxCanvas", typeof(RectTransform), typeof(Canvas));
        _overlay = root.GetComponent<Canvas>();
        _overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlay.overrideSorting = true;
        _overlay.sortingOrder = overlaySortingOrder;

        _top = CreateBar(root.transform, "LetterboxTop", new Vector2(0f, 1f), new Vector2(1f, 1f));
        _bottom = CreateBar(root.transform, "LetterboxBottom", new Vector2(0f, 0f), new Vector2(1f, 0f));
        root.SetActive(false);
    }

    static RectTransform CreateBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }
}
