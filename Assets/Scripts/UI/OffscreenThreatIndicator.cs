using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 밖 위협 표시 (Docs/CameraDesign_Soulslike.md §5, 스텔라 블레이드 관례).
/// 락온하지 않은 적(또는 락온하지 않은 보스)이 화면 밖에서 공격을 시작하면 화면 가장자리에 방향 화살표를 띄운다.
/// 색 언어는 텔레그래프와 동일: 패리 가능=금색, 회피 전용=빨강, 가드 불가=마젠타, 강공격=앰버.
/// SoulsCameraRig 가 프로필 enableOffscreenThreats 일 때 붙인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class OffscreenThreatIndicator : MonoBehaviour
{
    const int PoolSize = 6;

    sealed class Arrow
    {
        public RectTransform Rect;
        public Image Image;
        public Transform Attacker;
        public Color Color;
        public float StartAt;
        public float Until;
    }

    readonly List<Arrow> _arrows = new List<Arrow>(PoolSize);
    readonly HashSet<BossController> _hookedBosses = new HashSet<BossController>();
    readonly HashSet<DroneController> _hookedDrones = new HashSet<DroneController>();
    readonly Dictionary<BossController, System.Action<BossAttackTelegraphType, float, BossAttackTimingCueFlags>> _cueHandlers = new Dictionary<BossController, System.Action<BossAttackTelegraphType, float, BossAttackTimingCueFlags>>();
    readonly Dictionary<BossController, System.Action<AttackTelegraphType, float, string>> _telegraphHandlers = new Dictionary<BossController, System.Action<AttackTelegraphType, float, string>>();
    Canvas _canvas;
    RectTransform _canvasRect;
    float _nextScanAt;
    static Sprite s_arrowSprite;

    /// <summary>QA/지표용: 지금까지 띄운 화살표 수.</summary>
    public static int ShownCount { get; private set; }

    SoulsCameraProfile Profile => SoulsCameraRig.ActiveProfile;

    void Start()
    {
        var go = new GameObject("OffscreenThreatCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 82;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        _canvasRect = go.GetComponent<RectTransform>();

        for (int i = 0; i < PoolSize; i++)
        {
            var a = new GameObject("ThreatArrow" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            a.transform.SetParent(_canvasRect, false);
            var arrow = new Arrow { Rect = a.GetComponent<RectTransform>(), Image = a.GetComponent<Image>() };
            arrow.Image.sprite = ArrowSprite();
            arrow.Image.raycastTarget = false;
            arrow.Rect.sizeDelta = new Vector2(46f, 46f);
            a.SetActive(false);
            _arrows.Add(arrow);
        }
    }

    void OnDestroy()
    {
        foreach (var kv in _cueHandlers)
            if (kv.Key != null) kv.Key.OnAttackTimingCue -= kv.Value;
        foreach (var kv in _telegraphHandlers)
            if (kv.Key != null) kv.Key.OnAttackTelegraph -= kv.Value;
        foreach (DroneController d in _hookedDrones)
            if (d != null) d.AttackTelegraphed -= HandleDroneTelegraph;
    }

    void Update()
    {
        if (Time.unscaledTime >= _nextScanAt)
        {
            _nextScanAt = Time.unscaledTime + 1f;
            Scan();
        }
    }

    void Scan()
    {
        foreach (BossController boss in FindObjectsOfType<BossController>())
        {
            if (boss == null || !_hookedBosses.Add(boss))
                continue;
            BossController captured = boss;
            System.Action<BossAttackTelegraphType, float, BossAttackTimingCueFlags> cue = (type, lead, flags) => Raise(captured.transform, ColorFor(type), lead);
            System.Action<AttackTelegraphType, float, string> tele = (type, lead, label) => Raise(captured.transform, ColorFor(type), lead);
            boss.OnAttackTimingCue += cue;
            boss.OnAttackTelegraph += tele;
            _cueHandlers[boss] = cue;
            _telegraphHandlers[boss] = tele;
        }

        foreach (DroneController drone in FindObjectsOfType<DroneController>())
        {
            if (drone != null && _hookedDrones.Add(drone))
                drone.AttackTelegraphed += HandleDroneTelegraph;
        }
    }

    void HandleDroneTelegraph(DroneController drone)
    {
        // 드론 탄은 패리 불가(회피 전용) → 빨강
        SoulsCameraProfile p = Profile;
        if (drone != null && p != null)
            Raise(drone.transform, p.threatDodgeColor, drone.AttackTelegraphLeadTime);
    }

    Color ColorFor(BossAttackTelegraphType type)
    {
        SoulsCameraProfile p = Profile;
        if (p == null)
            return Color.white;
        switch (type)
        {
            case BossAttackTelegraphType.DodgeOnly:
            case BossAttackTelegraphType.Ranged: return p.threatDodgeColor;
            case BossAttackTelegraphType.Unblockable: return p.threatUnblockableColor;
            case BossAttackTelegraphType.Heavy: return p.threatHeavyColor;
            default: return p.threatParryableColor;
        }
    }

    Color ColorFor(AttackTelegraphType type)
    {
        SoulsCameraProfile p = Profile;
        if (p == null)
            return Color.white;
        switch (type)
        {
            case AttackTelegraphType.Dodge: return p.threatDodgeColor;
            case AttackTelegraphType.Danger: return p.threatUnblockableColor;
            default: return p.threatParryableColor;
        }
    }

    void Raise(Transform attacker, Color color, float lead)
    {
        SoulsCameraProfile p = Profile;
        Camera cam = GameplaySceneCache.ResolveMainCamera();
        if (p == null || cam == null || attacker == null || !isActiveAndEnabled)
            return;

        // 락온 중인 타깃은 이미 화면에 프레이밍되므로 제외
        PlayerLockOn lockOn = GameplaySceneCache.ResolvePlayerLockOn();
        if (lockOn != null && lockOn.IsLockedOn() && lockOn.CurrentTarget != null && lockOn.CurrentTarget.root == attacker.root)
            return;

        if (lockOn != null && Vector3.Distance(lockOn.transform.position, attacker.position) > p.threatMaxDistance)
            return;

        Vector3 point = SoulsCameraRig.GetTargetChestPoint(attacker, p.chestHeightRatio);
        Vector3 v = cam.WorldToViewportPoint(point);
        if (v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f)
            return; // 화면 안

        Arrow arrow = null;
        for (int i = 0; i < _arrows.Count; i++)
            if (_arrows[i].Attacker == attacker && Time.unscaledTime < _arrows[i].Until) { arrow = _arrows[i]; break; }
        if (arrow == null)
        {
            float oldest = float.MaxValue;
            foreach (Arrow a in _arrows)
            {
                if (Time.unscaledTime >= a.Until) { arrow = a; break; }
                if (a.StartAt < oldest) { oldest = a.StartAt; arrow = a; }
            }
        }
        if (arrow == null)
            return;

        arrow.Attacker = attacker;
        arrow.Color = color;
        arrow.StartAt = Time.unscaledTime;
        arrow.Until = Time.unscaledTime + Mathf.Clamp(lead + 0.25f, p.threatMinDisplay, p.threatMaxDisplay);
        arrow.Rect.gameObject.SetActive(true);
        ShownCount++;
    }

    void LateUpdate()
    {
        Camera cam = GameplaySceneCache.ResolveMainCamera();
        SoulsCameraProfile p = Profile;
        if (cam == null || _canvasRect == null)
            return;

        Vector2 size = _canvasRect.rect.size;
        float margin = p != null ? p.threatEdgeMargin : 70f;
        float now = Time.unscaledTime;
        foreach (Arrow a in _arrows)
        {
            if (!a.Rect.gameObject.activeSelf)
                continue;
            if (a.Attacker == null || now >= a.Until || p == null)
            {
                a.Rect.gameObject.SetActive(false);
                continue;
            }

            Vector3 world = SoulsCameraRig.GetTargetChestPoint(a.Attacker, p.chestHeightRatio);
            Vector3 local = cam.transform.InverseTransformPoint(world);
            Vector3 v = cam.WorldToViewportPoint(world);
            bool onScreen = v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f;

            // 카메라 평면 기준 방향 (뒤쪽이면 x/y 를 그대로 써 가장자리로 밀어낸다)
            Vector2 dir = new Vector2(local.x, local.y);
            if (local.z < 0f && dir.sqrMagnitude < 0.01f)
                dir = Vector2.down;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector2.up;
            dir.Normalize();

            Vector2 half = size * 0.5f - new Vector2(margin, margin);
            float scale = Mathf.Min(half.x / Mathf.Max(0.0001f, Mathf.Abs(dir.x)), half.y / Mathf.Max(0.0001f, Mathf.Abs(dir.y)));
            a.Rect.anchoredPosition = dir * scale;
            a.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);

            float life = Mathf.Clamp01((now - a.StartAt) / 0.08f);
            float fade = Mathf.Clamp01((a.Until - now) / 0.2f);
            float pulse = 1f + 0.12f * Mathf.Sin((now - a.StartAt) * 18f);
            a.Rect.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, life) * pulse;
            Color c = a.Color;
            c.a = (onScreen ? 0.25f : 1f) * life * fade;
            a.Image.color = c;
        }
    }

    static Sprite ArrowSprite()
    {
        if (s_arrowSprite != null)
            return s_arrowSprite;

        const int n = 48;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "RuntimeThreatArrow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // 위쪽을 가리키는 쐐기형 화살표 (안쪽이 파인 셰브런)
                float fx = (x + 0.5f) / n - 0.5f;
                float fy = (y + 0.5f) / n;
                float outer = 0.5f * (1f - fy);          // 바깥 삼각형 반폭
                float inner = 0.5f * (0.62f - fy);       // 안쪽 파임
                bool inside = fy > 0.08f && fy < 0.96f && Mathf.Abs(fx) < outer && !(Mathf.Abs(fx) < inner && fy < 0.62f);
                px[y * n + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s_arrowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return s_arrowSprite;
    }
}
