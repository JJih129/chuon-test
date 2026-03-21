using UnityEngine;

// ===== 변수 헤더(한글 설명) =====
// sourceRenderer : 추적할 렌더러. 없으면 동작 안 함
// yOffset        : 렌더러 중심 대비 월드 Y 오프셋(고정)
[DisallowMultipleComponent]
public class LockPivotFollower : MonoBehaviour
{
    public Renderer sourceRenderer; // [조절값]
    public float yOffset = 0f;      // [조절값]
    [Min(0f)] public float refreshInterval = 1f / 30f;

    float _nextRefreshAt;

    void LateUpdate()
    {
        if (!sourceRenderer) return;
        float effectiveRefreshInterval = Mathf.Max(refreshInterval, 1f / 15f);
        if (effectiveRefreshInterval > 0f && Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + effectiveRefreshInterval;
        var c = sourceRenderer.bounds.center;
        transform.position = new Vector3(c.x, c.y + yOffset, c.z);
    }
}
