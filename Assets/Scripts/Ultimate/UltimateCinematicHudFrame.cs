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
    float _delayUntil;
    RectTransform _skipHint; // 반복 16 (E2): 키 글리프 상자 + 설명 (ChuonKeyHint)

    public bool IsActive => _active;

    public void Begin()
    {
        Begin(0f);
    }

    /// <summary>startDelay: HUD 페이드를 늦춘다 (궁극기 시작 시 페이즈 코어 플레어가 먼저 보이도록).</summary>
    public void Begin(float startDelay)
    {
        _delayUntil = Time.unscaledTime + Mathf.Max(0f, startDelay);
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

        if (_direction > 0 && Time.unscaledTime < _delayUntil)
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
        if (_skipHint != null)
            ChuonKeyHint.SetAlpha(_skipHint, 0.95f * eased);

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

        var root = new GameObject("UltimateLetterboxCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _overlay = root.GetComponent<Canvas>();
        _overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlay.overrideSorting = true;
        _overlay.sortingOrder = overlaySortingOrder;
        // 반복 16 (E2): 스케일러가 없어 힌트가 해상도와 무관하게 18 px 고정이었다 → 1920x1080 기준 스케일
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _top = CreateBar(root.transform, "LetterboxTop", new Vector2(0f, 1f), new Vector2(1f, 1f));
        _bottom = CreateBar(root.transform, "LetterboxBottom", new Vector2(0f, 0f), new Vector2(1f, 0f));

        // 건너뛰기 안내 (아래 레터박스 안, 오른쪽): [F] [Space] 건너뛰기 — 키 상자 + 24 px 설명 (1080p 기준)
        _skipHint = ChuonKeyHint.Create(_bottom, "SkipHint", ChuonKeyHint.SkipKeys(), "건너뛰기", 24);
        _skipHint.anchorMin = _skipHint.anchorMax = new Vector2(1f, 0.5f);
        _skipHint.pivot = new Vector2(1f, 0.5f);
        _skipHint.anchoredPosition = new Vector2(-40f, 0f);
        ChuonKeyHint.SetAlpha(_skipHint, 0f);
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
