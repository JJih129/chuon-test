using UnityEngine;

public class BossUIBillboard : MonoBehaviour
{
    const float CameraResolveInterval = 0.5f;

    [SerializeField, Tooltip("빌보드 기준으로 사용할 카메라")]
    private Camera targetCamera;

    [SerializeField, Tooltip("수평(Y) 회전만 적용할지 여부")]
    private bool yOnly = true;

    [SerializeField, Min(0f), Tooltip("빌보드 회전 갱신 간격(초). 0이면 매 프레임")]
    private float refreshInterval = 1f / 30f;

    float _nextRefreshAt;
    float _nextCameraResolveAt;
    Transform _cachedTransform;

    void Awake()
    {
        _cachedTransform = transform;
        if (targetCamera == null)
            targetCamera = GameplaySceneCache.ResolveMainCamera();
    }

    void LateUpdate()
    {
        if (targetCamera == null && Time.unscaledTime >= _nextCameraResolveAt)
        {
            _nextCameraResolveAt = Time.unscaledTime + CameraResolveInterval;
            targetCamera = GameplaySceneCache.ResolveMainCamera();
        }

        float effectiveRefreshInterval = Mathf.Max(refreshInterval, 1f / 15f);
        if (effectiveRefreshInterval > 0f && Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + effectiveRefreshInterval;
        if (targetCamera == null)
            return;

        Vector3 dir = _cachedTransform.position - targetCamera.transform.position;
        if (yOnly)
            dir.y = 0f;

        if (dir.sqrMagnitude <= 0.0001f)
            return;

        _cachedTransform.rotation = Quaternion.LookRotation(dir);
    }
}
