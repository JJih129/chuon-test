using UnityEngine;

// ===== 변수 헤더(한글 설명) =====
// sourceRenderer : 추적할 렌더러. 없으면 동작 안 함
// yOffset        : 렌더러 중심 대비 월드 Y 오프셋(고정)
[DisallowMultipleComponent]
public class LockPivotFollower : MonoBehaviour
{
    const float PositionEpsilonSqr = 0.000001f;
    public Renderer sourceRenderer; // [조절값]
    public float yOffset = 0f;      // [조절값]

    Transform _tr;

    void Awake()
    {
        _tr = transform;
    }

    void LateUpdate()
    {
        if (!sourceRenderer) return;
        var c = sourceRenderer.bounds.center;
        Vector3 targetPosition = new Vector3(c.x, c.y + yOffset, c.z);
        if ((_tr.position - targetPosition).sqrMagnitude > PositionEpsilonSqr)
            _tr.position = targetPosition;
    }
}
