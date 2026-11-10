// 반복 17: 타임라인 애니메이션 트랙을 붙일 수 없는 샷 카메라(다른 컨트롤러가 재생하는 Type-D 리빌 v2 등)에 느린 돌리·크레인·줌을 준다.
// 카메라가 라이브가 되는 순간부터 unscaled 시간으로 기준 → 기준 + moveLocal (부모 로컬), FOV + fovDelta 로 이징. 라이브가 아니면 원위치.
// 일시정지 메뉴(GameTimeScale 0) 동안은 멈춘다. 프레임당 할당 없음.
using Unity.Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public sealed class CineCamDrift : MonoBehaviour
{
    [SerializeField] Vector3 moveLocal = new Vector3(0f, 0f, 0.3f);
    [SerializeField] float fovDelta = -2f;
    [SerializeField, Min(0.1f)] float duration = 2f;

    CinemachineCamera _cam;
    Vector3 _basePos;
    float _baseFov;
    float _elapsed;
    bool _live;
    bool _cached;

    public void Configure(Vector3 move, float fov, float seconds)
    {
        moveLocal = move;
        fovDelta = fov;
        duration = Mathf.Max(0.1f, seconds);
    }

    void Cache()
    {
        if (_cached)
            return;
        _cam = GetComponent<CinemachineCamera>();
        _basePos = transform.localPosition;
        _baseFov = _cam.Lens.FieldOfView;
        _cached = true;
    }

    void OnEnable() => Cache();

    void OnDisable()
    {
        if (_live)
            Apply(0f);
        _live = false;
    }

    void Update()
    {
        Cache();
        bool live = CinemachineCore.IsLive(_cam);
        if (!live)
        {
            if (_live)
                Apply(0f);
            _live = false;
            return;
        }
        if (!_live)
            _elapsed = 0f;
        _live = true;
        if (!GameTimeScale.IsPaused)
            _elapsed += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_elapsed / duration);
        Apply(k * k * (3f - 2f * k));
    }

    void Apply(float k)
    {
        transform.localPosition = _basePos + moveLocal * k;
        LensSettings lens = _cam.Lens;
        lens.FieldOfView = _baseFov + fovDelta * k;
        _cam.Lens = lens;
    }
}
