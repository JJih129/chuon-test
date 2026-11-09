// 파일명: BossDamageCracks.cs
// Type-D 피해 균열 (기획 구버전: "HP가 낮으면 피부가 떨어져 나가는 외형").
// HP ≤ 50% / ≤ 25% 에서 몸체 SkinnedMeshRenderer 위에 발광 균열 오버레이 재질(절차 생성 텍스처, Sprites/Default)을 덧그린다.
// 은은하게: 1단계는 옅은 주황-적색 균열, 2단계는 균열이 늘고 천천히 맥동한다. 기존 재질/키워드는 건드리지 않는다.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossDamageCracks : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float stage1Threshold = 0.5f;
    [SerializeField, Range(0f, 1f)] float stage2Threshold = 0.25f;
    [SerializeField] Color crackColor = new Color(1f, 0.32f, 0.12f, 1f);
    [SerializeField, Range(0f, 1f)] float stage1Alpha = 0.55f;
    [SerializeField, Range(0f, 1f)] float stage2Alpha = 0.85f;
    [SerializeField] string excludeRendererTokens = "sword,katana,weapon,blade,trail,scabbard,saya";

    BossHealth _health;
    readonly List<Renderer> _targets = new List<Renderer>();
    readonly List<Material[]> _originalMaterials = new List<Material[]>();
    Material _overlay;
    static Texture2D s_stage1Texture;
    static Texture2D s_stage2Texture;
    int _stage;

    public int Stage => _stage;
    public static BossDamageCracks Active { get; private set; }

    void Awake()
    {
        Active = this;
    }

    void OnEnable()
    {
        Bind();
    }

    void OnDisable()
    {
        if (_health != null)
            _health.OnHPChanged -= HandleHpChanged;
    }

    void OnDestroy()
    {
        RemoveOverlay();
        if (_overlay != null)
            Destroy(_overlay);
        if (Active == this)
            Active = null;
    }

    void Bind()
    {
        if (_health == null)
            _health = GetComponentInChildren<BossHealth>(true) ?? GetComponentInParent<BossHealth>();
        if (_health == null)
            return;
        _health.OnHPChanged -= HandleHpChanged;
        _health.OnHPChanged += HandleHpChanged;
        HandleHpChanged(_health.CurrentHP, _health.MaxHP);
    }

    void HandleHpChanged(int current, int max)
    {
        if (max <= 0)
            return;
        float n = current / (float)max;
        int stage = n <= stage2Threshold ? 2 : n <= stage1Threshold ? 1 : 0;
        if (_health != null && _health.IsDead)
            stage = Mathf.Max(stage, _stage);
        if (stage != _stage)
            SetStage(stage);
    }

    /// <summary>QA/스크린샷용 강제 단계.</summary>
    public void SetStage(int stage)
    {
        stage = Mathf.Clamp(stage, 0, 2);
        _stage = stage;
        if (stage == 0)
        {
            RemoveOverlay();
            return;
        }

        EnsureOverlay();
        if (_overlay == null)
            return;
        _overlay.mainTexture = stage >= 2 ? Stage2Texture : Stage1Texture;
        ApplyOverlay();
        Update();
    }

    void Update()
    {
        if (_stage == 0 || _overlay == null)
            return;
        float a = _stage >= 2 ? stage2Alpha : stage1Alpha;
        if (_stage >= 2)
            a *= 0.78f + 0.22f * Mathf.Sin(Time.time * 3.1f);
        Color c = crackColor;
        c.a = a;
        _overlay.color = c;
    }

    /// <summary>
    /// 균열 텍스처(512² RGBA 밉맵 ×2) + 오버레이 재질을 미리 만든다. 반복 12 측정: HP 50%/25% 첫 단계 진입 프레임이
    /// 텍스처 생성·업로드 + 1 MB 픽셀 배열 GC + Sprites/Default 첫 사용으로 49–102 ms 히치였다 → 하층 도착 암전 중 소진.
    /// 반환된 재질을 워밍업 카메라 앞 쿼드에 그리면 셰이더/텍스처 업로드까지 끝난다.
    /// </summary>
    public static Material PrewarmShared(out Texture2D stage2)
    {
        Texture2D stage1 = Stage1Texture;
        stage2 = Stage2Texture;
        if (Active != null)
        {
            Active.EnsureOverlay();
            if (Active._overlay != null)
            {
                if (Active._stage == 0)
                    Active._overlay.mainTexture = stage1;
                return Active._overlay;
            }
        }
        Shader shader = Shader.Find("Sprites/Default");
        return shader != null ? new Material(shader) { name = "_BossDamageCracks (prewarm)", hideFlags = HideFlags.DontSave, mainTexture = stage1 } : null;
    }

    void EnsureOverlay()
    {
        if (_overlay != null)
            return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return;
        _overlay = new Material(shader) { name = "_BossDamageCracks (runtime)", hideFlags = HideFlags.DontSave };
        _overlay.renderQueue = 3000;
    }

    void CollectTargets()
    {
        if (_targets.Count > 0)
            return;
        string[] tokens = excludeRendererTokens.Split(',');
        foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string n = r.name.ToLowerInvariant();
            bool skip = false;
            foreach (string t in tokens)
                if (t.Length > 0 && n.Contains(t.Trim())) { skip = true; break; }
            if (skip || r.sharedMesh == null)
                continue;
            _targets.Add(r);
            _originalMaterials.Add(r.sharedMaterials);
        }

        // 반복 10: Type-D 장갑판에도 균열 (코어/바이저 발광 파츠 제외). 오버레이는 마지막 서브메시(장갑 본체)에 그려진다.
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.name;
            if (!n.StartsWith("TypeD_") || n.Contains("Core") || n.Contains("Visor"))
                continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;
            _targets.Add(r);
            _originalMaterials.Add(r.sharedMaterials);
        }
    }

    void ApplyOverlay()
    {
        CollectTargets();
        for (int i = 0; i < _targets.Count; i++)
        {
            Renderer r = _targets[i];
            if (r == null)
                continue;
            Material[] original = _originalMaterials[i];
            var mats = new Material[original.Length + 1];
            original.CopyTo(mats, 0);
            mats[mats.Length - 1] = _overlay;
            r.sharedMaterials = mats;
        }
    }

    void RemoveOverlay()
    {
        for (int i = 0; i < _targets.Count; i++)
            if (_targets[i] != null)
                _targets[i].sharedMaterials = _originalMaterials[i];
    }

    static Texture2D Stage1Texture => s_stage1Texture != null ? s_stage1Texture : (s_stage1Texture = BuildCrackTexture(1731, 26));
    static Texture2D Stage2Texture => s_stage2Texture != null ? s_stage2Texture : (s_stage2Texture = BuildCrackTexture(1731, 58));

    // 균열: 같은 시드로 1단계 균열을 2단계가 포함하도록 (균열이 '늘어나는' 느낌)
    static Texture2D BuildCrackTexture(int seed, int crackCount)
    {
        const int size = 512;
        var pixels = new Color32[size * size];
        var rng = new System.Random(seed);
        for (int c = 0; c < crackCount; c++)
        {
            float x = (float)rng.NextDouble() * size;
            float y = (float)rng.NextDouble() * size;
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            int segments = 6 + rng.Next(10);
            float width = 1.2f + (float)rng.NextDouble() * 1.4f;
            for (int s = 0; s < segments; s++)
            {
                angle += ((float)rng.NextDouble() - 0.5f) * 1.3f;
                float len = 6f + (float)rng.NextDouble() * 16f;
                float nx = x + Mathf.Cos(angle) * len;
                float ny = y + Mathf.Sin(angle) * len;
                DrawLine(pixels, size, x, y, nx, ny, width);
                if (rng.NextDouble() < 0.25)
                {
                    float ba = angle + (rng.NextDouble() < 0.5 ? 1f : -1f) * (0.6f + (float)rng.NextDouble() * 0.6f);
                    float bl = 5f + (float)rng.NextDouble() * 10f;
                    DrawLine(pixels, size, nx, ny, nx + Mathf.Cos(ba) * bl, ny + Mathf.Sin(ba) * bl, width * 0.6f);
                }
                x = nx; y = ny;
                width *= 0.93f;
            }
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "_BossCracks", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        tex.SetPixels32(pixels);
        tex.Apply(true, true);
        return tex;
    }

    static void DrawLine(Color32[] px, int size, float x0, float y0, float x1, float y1, float width)
    {
        float len = Mathf.Max(1f, Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)));
        int steps = Mathf.CeilToInt(len * 1.5f);
        int r = Mathf.CeilToInt(width + 1.5f);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float cx = Mathf.Lerp(x0, x1, t);
            float cy = Mathf.Lerp(y0, y1, t);
            for (int oy = -r; oy <= r; oy++)
            for (int ox = -r; ox <= r; ox++)
            {
                int px0 = ((int)cx + ox) & (size - 1);
                int py0 = ((int)cy + oy) & (size - 1);
                float d = Mathf.Sqrt(ox * ox + oy * oy);
                float core = Mathf.Clamp01(1f - d / Mathf.Max(0.5f, width));      // 밝은 심
                float glow = Mathf.Clamp01(1f - d / (width + 1.5f)) * 0.35f;     // 약한 번짐
                byte a = (byte)(Mathf.Clamp01(Mathf.Max(core, glow)) * 255f);
                int idx = py0 * size + px0;
                if (a > px[idx].a)
                {
                    byte w = (byte)(core * 255f);
                    px[idx] = new Color32(255, (byte)Mathf.Max(150, w), (byte)Mathf.Max(120, w), a);
                }
            }
        }
    }
}
