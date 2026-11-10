using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 연출 무대 전용 환경(스카이박스·안개·앰비언트). 이 오브젝트가 켜질 때 적용하고 꺼질 때 원래 값으로 되돌린다.
/// 오프닝 새벽 외경처럼 호스트 씬(실내 Level_Facility)과 다른 하늘이 필요한 샷에 쓴다. 타임라인 활성화 트랙으로 켜고 끈다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CineRenderSettingsOverride : MonoBehaviour
{
    [SerializeField] Material skybox;
    [SerializeField] bool fog = true;
    [SerializeField] FogMode fogMode = FogMode.ExponentialSquared;
    [SerializeField] float fogDensity = 0.0022f;
    [SerializeField] Color fogColor = new Color(0.36f, 0.33f, 0.36f);
    [SerializeField] Color ambientSky = new Color(0.42f, 0.42f, 0.5f);
    [SerializeField] Color ambientEquator = new Color(0.38f, 0.3f, 0.3f);
    [SerializeField] Color ambientGround = new Color(0.1f, 0.1f, 0.11f);

    Material _skybox;
    bool _fog;
    FogMode _fogMode;
    float _fogDensity;
    Color _fogColor;
    AmbientMode _ambientMode;
    Color _sky, _equator, _ground;
    bool _applied;

    public void Configure(Material sky, float density, Color fogTint)
    {
        skybox = sky;
        fogDensity = density;
        fogColor = fogTint;
    }

    void OnEnable()
    {
        if (!Application.isPlaying)
            return;
        _skybox = RenderSettings.skybox;
        _fog = RenderSettings.fog;
        _fogMode = RenderSettings.fogMode;
        _fogDensity = RenderSettings.fogDensity;
        _fogColor = RenderSettings.fogColor;
        _ambientMode = RenderSettings.ambientMode;
        _sky = RenderSettings.ambientSkyColor;
        _equator = RenderSettings.ambientEquatorColor;
        _ground = RenderSettings.ambientGroundColor;
        _applied = true;

        if (skybox != null)
            RenderSettings.skybox = skybox;
        RenderSettings.fog = fog;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogColor = fogColor;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
    }

    void OnDisable()
    {
        if (!_applied)
            return;
        _applied = false;
        RenderSettings.skybox = _skybox;
        RenderSettings.fog = _fog;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogDensity = _fogDensity;
        RenderSettings.fogColor = _fogColor;
        RenderSettings.ambientMode = _ambientMode;
        RenderSettings.ambientSkyColor = _sky;
        RenderSettings.ambientEquatorColor = _equator;
        RenderSettings.ambientGroundColor = _ground;
    }
}
