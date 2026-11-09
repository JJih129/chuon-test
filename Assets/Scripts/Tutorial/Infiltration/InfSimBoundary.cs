using UnityEngine;

// 시뮬레이션 경계 그리드 일렁임: 평소엔 거의 안 보이다가 플레이어가 가까이 오면 그리드가 떠오르고 흐른다.
// "진짜 도시처럼 보이되 가장자리에서만 시뮬레이션 티" (Docs/LevelDesign_Infiltration.md).
[DisallowMultipleComponent]
public sealed class InfSimBoundary : MonoBehaviour
{
    [SerializeField] Renderer[] grids = System.Array.Empty<Renderer>();
    [SerializeField, Min(1f)] float revealDistance = 14f;
    [SerializeField, Range(0f, 1f)] float idleAlpha = 0.015f;
    [SerializeField, Range(0f, 1f)] float nearAlpha = 0.55f;
    [SerializeField] Vector2 scroll = new Vector2(0f, 0.08f);
    [SerializeField] Color tint = new Color(0.45f, 0.85f, 1f, 1f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    MaterialPropertyBlock _mpb;
    Transform _player;
    float _nextFind;
    Vector4[] _st;

    public void Configure(Renderer[] renderers) => grids = renderers ?? System.Array.Empty<Renderer>();

    void Start()
    {
        _st = new Vector4[grids.Length];
        for (int i = 0; i < grids.Length; i++)
        {
            Renderer r = grids[i];
            Material m = r != null ? r.sharedMaterial : null;
            Vector4 st = m != null && m.HasProperty(BaseMapStId) ? m.GetVector(BaseMapStId) : new Vector4(1, 1, 0, 0);
            // 판 크기에 맞춰 그리드 칸이 2 m 가 되게
            if (r != null)
            {
                Vector3 s = r.transform.lossyScale;
                st.x = Mathf.Max(1f, s.x / 2f);
                st.y = Mathf.Max(1f, s.y / 2f);
            }
            _st[i] = st;
        }
    }

    void Update()
    {
        if (_player == null && Time.unscaledTime >= _nextFind)
        {
            _nextFind = Time.unscaledTime + 1f;
            GameObject p = GameObject.FindWithTag("Player");
            _player = p != null ? p.transform : null;
        }

        _mpb ??= new MaterialPropertyBlock();
        float t = Time.time;
        for (int i = 0; i < grids.Length; i++)
        {
            Renderer r = grids[i];
            if (r == null) continue;
            float d = _player != null ? DistanceToPlane(r.transform, _player.position) : 999f;
            float k = Mathf.Clamp01(1f - d / revealDistance);
            float shimmer = 0.75f + 0.25f * Mathf.PerlinNoise(t * 1.7f, i * 2.3f);
            float glitch = Mathf.PerlinNoise(t * 11f, i) > 0.82f ? 1.6f : 1f;
            Color c = tint;
            c.a = Mathf.Lerp(idleAlpha, nearAlpha, k * k) * shimmer * glitch;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            Vector4 st = _st != null && i < _st.Length ? _st[i] : new Vector4(1, 1, 0, 0);
            st.z = scroll.x * t;
            st.w = scroll.y * t;
            _mpb.SetVector(BaseMapStId, st);
            r.SetPropertyBlock(_mpb);
        }
    }

    static float DistanceToPlane(Transform plane, Vector3 p)
    {
        Vector3 local = plane.InverseTransformPoint(p);
        Vector3 s = plane.lossyScale;
        float dx = Mathf.Max(0f, Mathf.Abs(local.x) - 0.5f) * s.x;
        float dy = Mathf.Max(0f, Mathf.Abs(local.y) - 0.5f) * s.y;
        float dz = Mathf.Abs(local.z) * s.z;
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
