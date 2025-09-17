using UnityEngine;
using Cinemachine;

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
    [Header("대상 참조")]
    public CinemachineFreeLook freeLook;

    [Header("동작 옵션")]
    public float step = 0.5f;
    public bool invertScroll = false;

    [Header("반경 한계")]
    public float minRadius = 2f;
    public float maxRadius = 8f;

    void Reset()
    {
        if (!freeLook)
            freeLook = GetComponent<CinemachineFreeLook>();
    }

    void Update()
    {
        if (!freeLook) return;

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f) return;
        if (invertScroll) wheel = -wheel;

        // 세 오빗 모두 같은 반경 변화 적용
        for (int i = 0; i < 3; i++)
        {
            var orbit = freeLook.m_Orbits[i];
            orbit.m_Radius = Mathf.Clamp(orbit.m_Radius - wheel * step, minRadius, maxRadius);
            freeLook.m_Orbits[i] = orbit;
        }
    }
}
