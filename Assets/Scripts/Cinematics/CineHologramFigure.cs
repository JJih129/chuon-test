using UnityEngine;

/// <summary>
/// 홀로그램 실루엣 (츠카사 엔딩, 은신처 작전 지도). 셰이더 "Chuon/CineHologram" 의 값을 MaterialPropertyBlock 으로 갱신한다.
/// materialize / intensity / glitch 는 타임라인 애니메이션 트랙(이 컴포넌트 필드 커브)으로 움직인다.
/// 값이 바뀔 때만 블록을 다시 쓴다 (프레임당 할당 없음). 비스케일 시간 기준.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class CineHologramFigure : MonoBehaviour
{
    static readonly int MaterializeId = Shader.PropertyToID("_Materialize");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int GlitchId = Shader.PropertyToID("_Glitch");
    static readonly int HeightMinId = Shader.PropertyToID("_HeightMin");
    static readonly int HeightMaxId = Shader.PropertyToID("_HeightMax");
    static readonly int TimeId = Shader.PropertyToID("_HoloTime");

    [Range(0f, 1f)] public float materialize = 1f;
    [Range(0f, 2f)] public float intensity = 1f;
    [Range(0f, 1f)] public float glitch = 0.1f;
    [Tooltip("자체 미세 깜빡임 (0 = 없음)")]
    [Range(0f, 1f)] public float idleFlicker = 0.12f;
    [SerializeField] Renderer[] renderers;

    MaterialPropertyBlock _block;
    float _lastMat = -1f, _lastInt = -1f, _lastGlitch = -1f;
    float _minY, _maxY;

    void OnEnable()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        _block ??= new MaterialPropertyBlock();
        _lastMat = _lastInt = _lastGlitch = -1f;
        RecalcBounds();
    }

    void RecalcBounds()
    {
        bool any = false;
        Bounds b = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        _minY = any ? b.min.y : transform.position.y;
        _maxY = any ? b.max.y : transform.position.y + 2f;
    }

    void LateUpdate()
    {
        if (renderers == null)
            return;
        float now = Application.isPlaying ? Time.unscaledTime : (float)(System.DateTime.Now.TimeOfDay.TotalSeconds % 1000d);
        float flick = idleFlicker > 0f ? 1f - idleFlicker * Mathf.Clamp01(Mathf.PerlinNoise(now * 11f, 0.31f) * 1.6f - 0.6f) : 1f;
        float inten = intensity * flick;

        // 위치가 움직이는 홀로그램(상승 연출)은 높이 범위를 매 프레임 갱신 (bounds 접근은 할당 없음)
        if (transform.hasChanged)
        {
            transform.hasChanged = false;
            RecalcBounds();
            _lastMat = -1f;
        }

        if (Mathf.Abs(materialize - _lastMat) < 0.0005f && Mathf.Abs(inten - _lastInt) < 0.002f && Mathf.Abs(glitch - _lastGlitch) < 0.0005f && !Application.isPlaying)
            return;

        _lastMat = materialize; _lastInt = inten; _lastGlitch = glitch;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_block);
            _block.SetFloat(MaterializeId, materialize);
            _block.SetFloat(IntensityId, inten);
            _block.SetFloat(GlitchId, glitch);
            _block.SetFloat(HeightMinId, _minY);
            _block.SetFloat(HeightMaxId, _maxY);
            _block.SetFloat(TimeId, now);
            r.SetPropertyBlock(_block);
        }
    }
}
