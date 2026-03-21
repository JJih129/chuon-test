using UnityEngine;

public class BossUIBillboard : MonoBehaviour
{
    [SerializeField, Tooltip("빌보드 대상으로 사용할 카메라")]
    private Camera targetCamera;

    [SerializeField, Tooltip("수평(Y) 회전만 적용할지 여부")]
    private bool yOnly = true;

    [SerializeField, Min(0f), Tooltip("빌보드 회전 갱신 간격(초). 0이면 매 프레임.")]
    private float refreshInterval = 1f / 30f;

    float _nextRefreshAt;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    void LateUpdate()
    {
        float effectiveRefreshInterval = Mathf.Max(refreshInterval, 1f / 15f);
        if (effectiveRefreshInterval > 0f && Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + effectiveRefreshInterval;
        if (targetCamera == null) return;
        // 카메라를 향해 앞면이 보이도록 설정 (정면 항상 카메라를 바라봄)
        Vector3 dir = transform.position - targetCamera.transform.position;
        if (yOnly) dir.y = 0f;
        if (dir.sqrMagnitude <= 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(dir);
    }
}
