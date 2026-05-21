using UnityEngine;
using Unity.Cinemachine;
#pragma warning disable CS0618

/// ==============================
/// ▼ 변수 헤더(한글 설명)
/// freeLook       : 조절할 CinemachineFreeLook 컴포넌트
/// step           : 휠 1틱당 반지름 변화량
/// invertScroll   : 휠 방향 반전 여부
/// minRadius      : 카메라 거리 최소 반지름
/// maxRadius      : 카메라 거리 최대 반지름
/// ==============================
[DisallowMultipleComponent]
public class MouseWheelZoom_FreeLookOnly : MonoBehaviour
{
    [Header("전시 빌드 고정")]
    [SerializeField] bool allowMouseWheelZoom = false;

    [Header("대상 참조")]
    public CinemachineVirtualCameraBase freeLook;

    [Header("동작 옵션")]
    public float step = 0.5f;
    public bool invertScroll = false;

    [Header("반경 한계")]
    public float minRadius = 2f;
    public float maxRadius = 8f;

    void Reset()
    {
        if (!freeLook)
            freeLook = GetComponent<CinemachineVirtualCameraBase>();
    }

    void Update()
    {
        if (!allowMouseWheelZoom)
            return;

        if (!freeLook) return;

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f) return;
        if (invertScroll) wheel = -wheel;

        CinemachineCompat.TryAdjustLegacyFreeLookOrbitsRadius(freeLook, -wheel * step, minRadius, maxRadius);
    }
}
