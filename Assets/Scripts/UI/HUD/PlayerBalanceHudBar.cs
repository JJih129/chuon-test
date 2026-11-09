using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 밸런스(체간) 바 — 화면 하단 중앙 위, 가운데에서 양옆으로 차오름.
/// - 게이지가 0에 가까우면 숨김, 오르면 페이드 인
/// - 호박색 → 빨강(만충 근접), 브레이크 시 점멸
/// - PlayerHUD의 루트 캔버스 아래에 런타임 생성(PlayerBalance가 자동 호출)
/// - 모든 애니메이션은 unscaled time 사용
/// </summary>
[DisallowMultipleComponent]
public class PlayerBalanceHudBar : MonoBehaviour
{
    public const string RuntimeRootName = "_RuntimeBalanceBar";

    [Header("레이아웃")]
    public Vector2 size = new Vector2(360f, 7f);
    [Tooltip("하단 중앙 기준 Y 오프셋 (좌하단 플레이어 HP 프레임 상단(~195)과 겹치지 않도록 그 위에 배치)")]
    public float bottomOffset = 214f;

    [Header("표시")]
    [Tooltip("이 비율 이하면 숨김")]
    public float hideThreshold = 0.01f;
    public float fadeInSpeed = 6f;
    public float fadeOutSpeed = 2.2f;
    public float riseSharpness = 18f;
    public float fallSharpness = 8f;

    [Header("색상")]
    public Color trackColor = new Color(0.05f, 0.05f, 0.06f, 0.78f);
    public Color amberColor = new Color(1.00f, 0.72f, 0.22f, 1f);
    public Color redColor = new Color(1.00f, 0.18f, 0.10f, 1f);
    [Range(0f, 1f)] public float dangerStart = 0.55f;
    [Range(0f, 1f)] public float dangerFull = 0.92f;

    [Header("브레이크 점멸")]
    public float brokenFlashDuration = 0.55f;
    public float brokenFlashFrequency = 14f;
    public float brokenPunchScale = 1.12f;

    PlayerBalance _balance;
    CanvasGroup _group;
    RectTransform _root;
    RectTransform _fill;
    Image _fillImage;
    Image _flashImage;
    float _displayed;
    float _flashRemaining;

    public PlayerBalance Balance => _balance;

    /// <summary>PlayerHUD 캔버스를 찾아 바를 생성/재사용. 아직 HUD가 없으면 null.</summary>
    public static PlayerBalanceHudBar EnsureFor(PlayerBalance balance)
    {
        if (balance == null)
            return null;

        Canvas canvas = ResolveHudCanvas(balance);
        if (canvas == null)
            return null;

        Transform existing = canvas.transform.Find(RuntimeRootName);
        PlayerBalanceHudBar bar = existing != null ? existing.GetComponent<PlayerBalanceHudBar>() : null;
        if (bar == null)
            bar = Build(canvas.transform);

        if (bar._balance == null || !bar._balance.isActiveAndEnabled || bar._balance == balance)
        {
            bar.Bind(balance);
            return bar;
        }

        // 다른 활성 플레이어가 이미 바를 사용 중
        return bar;
    }

    static Canvas ResolveHudCanvas(PlayerBalance balance)
    {
        PlayerHUD[] huds = FindObjectsOfType<PlayerHUD>();
        PlayerHUD pick = null;
        for (int i = 0; i < huds.Length; i++)
        {
            if (huds[i] == null) continue;
            if (pick == null) pick = huds[i];
            if (huds[i].gameObject.scene == balance.gameObject.scene) { pick = huds[i]; break; }
        }

        if (pick == null)
            return null;

        Canvas canvas = pick.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = pick.GetComponentInChildren<Canvas>(true);

        return canvas != null ? canvas.rootCanvas : null;
    }

    static PlayerBalanceHudBar Build(Transform canvasTransform)
    {
        var go = new GameObject(RuntimeRootName, typeof(RectTransform), typeof(CanvasGroup));
        go.layer = canvasTransform.gameObject.layer;
        var root = (RectTransform)go.transform;
        root.SetParent(canvasTransform, false);
        root.SetAsLastSibling();

        var bar = go.AddComponent<PlayerBalanceHudBar>();
        bar._root = root;
        bar._group = go.GetComponent<CanvasGroup>();
        bar._group.alpha = 0f;
        bar._group.interactable = false;
        bar._group.blocksRaycasts = false;

        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = bar.size;
        root.anchoredPosition = new Vector2(0f, bar.bottomOffset);

        Image track = CreateImage("Track", root, bar.trackColor);
        Stretch(track.rectTransform, new Vector2(-2f, -2f));

        bar._fillImage = CreateImage("Fill", root, bar.amberColor);
        bar._fill = bar._fillImage.rectTransform;
        bar._fill.offsetMin = bar._fill.offsetMax = Vector2.zero;
        SetFillAnchors(bar._fill, 0f);

        Image center = CreateImage("CenterTick", root, new Color(1f, 1f, 1f, 0.55f));
        RectTransform c = center.rectTransform;
        c.anchorMin = new Vector2(0.5f, 0f);
        c.anchorMax = new Vector2(0.5f, 1f);
        c.sizeDelta = new Vector2(2f, 4f);
        c.anchoredPosition = Vector2.zero;

        bar._flashImage = CreateImage("Flash", root, new Color(1f, 1f, 1f, 0f));
        Stretch(bar._flashImage.rectTransform, new Vector2(-4f, -4f));

        return bar;
    }

    static Image CreateImage(string name, RectTransform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform rt, Vector2 outset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = outset;
        rt.offsetMax = -outset;
    }

    static void SetFillAnchors(RectTransform fill, float normalized)
    {
        float half = Mathf.Clamp01(normalized) * 0.5f;
        fill.anchorMin = new Vector2(0.5f - half, 0f);
        fill.anchorMax = new Vector2(0.5f + half, 1f);
    }

    public void Bind(PlayerBalance balance)
    {
        if (_balance == balance)
            return;

        Unbind();
        _balance = balance;
        if (_balance != null)
        {
            _balance.Broken += HandleBroken;
            _displayed = _balance.Normalized;
        }
    }

    void Unbind()
    {
        if (_balance != null)
            _balance.Broken -= HandleBroken;
        _balance = null;
    }

    void OnDestroy()
    {
        Unbind();
    }

    void HandleBroken()
    {
        _flashRemaining = Mathf.Max(0.05f, brokenFlashDuration);
    }

    void Update()
    {
        if (_group == null || _fill == null || _fillImage == null)
            return;

        float dt = Time.unscaledDeltaTime;
        float target = _balance != null && _balance.isActiveAndEnabled ? _balance.Normalized : 0f;

        float sharpness = target > _displayed ? riseSharpness : fallSharpness;
        _displayed = Mathf.Lerp(_displayed, target, 1f - Mathf.Exp(-Mathf.Max(0.01f, sharpness) * dt));
        if (Mathf.Abs(_displayed - target) < 0.0005f) _displayed = target;
        SetFillAnchors(_fill, _displayed);

        float danger = Mathf.InverseLerp(dangerStart, Mathf.Max(dangerStart + 0.001f, dangerFull), _displayed);
        Color fillColor = Color.Lerp(amberColor, redColor, danger);

        float flashAlpha = 0f;
        float scale = 1f;
        bool flashing = _flashRemaining > 0f;
        if (flashing)
        {
            _flashRemaining = Mathf.Max(0f, _flashRemaining - dt);
            float t = 1f - (_flashRemaining / Mathf.Max(0.05f, brokenFlashDuration));
            float pulse = 0.5f + 0.5f * Mathf.Cos(t * brokenFlashFrequency * Mathf.PI);
            flashAlpha = pulse * (1f - t);
            fillColor = Color.Lerp(fillColor, Color.white, flashAlpha * 0.8f);
            scale = Mathf.Lerp(Mathf.Max(1f, brokenPunchScale), 1f, t);
        }
        else if (danger >= 1f)
        {
            // 만충 직전: 약하게 맥동
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f);
            fillColor = Color.Lerp(fillColor, Color.white, pulse * 0.2f);
        }

        _fillImage.color = fillColor;
        if (_flashImage != null)
        {
            Color fc = redColor;
            fc.a = flashAlpha * 0.65f;
            _flashImage.color = fc;
        }

        if (_root != null)
            _root.localScale = new Vector3(scale, scale, 1f);

        bool show = flashing || target > hideThreshold || _displayed > hideThreshold;
        float targetAlpha = show ? 1f : 0f;
        float speed = show ? fadeInSpeed : fadeOutSpeed;
        _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, Mathf.Max(0.01f, speed) * dt);
    }
}
