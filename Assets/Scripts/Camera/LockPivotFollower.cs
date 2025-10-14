using UnityEngine;

/// ===== 변수 헤더 (한글설명) =====
/// sourceRenderer : 피벗이 따라갈 대상 Renderer. 보통 적의 ModelRenderer(머리/몸).
/// yOffset        : Renderer.center.y 에 더해질 Y 오프셋(월드 단위). (피벗 높이 보정)
/// =================================
[DisallowMultipleComponent]
public class LockPivotFollower : MonoBehaviour
{
    [Tooltip("피벗이 따라갈 Renderer (자동 할당 가능)")]
    public Renderer sourceRenderer;

    [Tooltip("Renderer 중심에서 더 올릴 Y 오프셋 (월드 단위)")]
    public float yOffset = 0f;

    void LateUpdate()
    {
        if (sourceRenderer == null) return;
        // Renderer의 world 중심 + yOffset로 피벗을 고정
        Vector3 centre = sourceRenderer.bounds.center;
        transform.position = new Vector3(centre.x, centre.y + yOffset, centre.z);
        transform.rotation = Quaternion.identity;
    }
}
