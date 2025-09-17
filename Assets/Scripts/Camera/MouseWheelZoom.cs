using UnityEngine;
using Cinemachine;

/// ==============================
/// ▼ 변수 헤더(한글 설명)
/// freeLook            : 수동 카메라 컨트롤러(FreeLookCamera). 락온 아닐 때만 적용
/// vcamProxy           : 락온 VCam용 Transposer 프록시. 있으면 offsetZ를 우선 조절
/// brain               : 활성 VCam 판별용 CinemachineBrain
/// onlyActiveVCam      : 현재 출력 중인 카메라만 조절
/// step                : 휠 1틱당 변화량(FOV 도수 또는 Z오프셋)
/// invertScroll        : 휠 방향 반전
/// smoothSnap          : 프록시 사용 시 즉시 스냅할지 여부
/// minFov/maxFov       : FreeLook(시네머신) FOV 한계
/// minZ/maxZ           : Transposer Z오프셋 한계(음수 구간 일반적)
/// ==============================
[DisallowMultipleComponent]
public class MouseWheelZoom : MonoBehaviour
{
    [Header("대상 참조")]
    public FreeLookCamera freeLook;       // 선택: 락온 아닐 때 사용  // ref: FreeLookCamera.cs
    public VCamTransposerProxy vcamProxy; // 선택: 락온 경로 우선    // ref: VCamTransposerProxy.cs
    public CinemachineBrain brain;        // Main Camera에 있는 Brain

    [Header("동작 옵션")]
    public bool onlyActiveVCam = true;
    public float step = 0.5f;
    public bool invertScroll = false;
    public bool smoothSnap = false;

    [Header("한계값")]
    public float minFov = 30f;
    public float maxFov = 70f;
    public float minZ = -10f;
    public float maxZ = -2f;

    void Awake()
    {
        if (!brain && Camera.main)
            brain = Camera.main.GetComponent<CinemachineBrain>();
    }

    void Update()
    {
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f) return;
        if (invertScroll) wheel = -wheel;

        // 1) 락온 경로: 프록시 있으면 우선 사용
        if (vcamProxy && vcamProxy.isActiveAndEnabled)
        {
            float dz = -wheel * step; // 휠↑=가까이
            vcamProxy.offsetZ = Mathf.Clamp(vcamProxy.offsetZ + dz, minZ, maxZ);
            if (!smoothSnap) vcamProxy.SnapNow();
            return;
        }

        // 2) 활성 VCam을 Brain에서 판별
        var active = brain ? brain.ActiveVirtualCamera : null;
        if (onlyActiveVCam && active == null) return;

        // FreeLook인지, 일반 VCam인지 분기
        CinemachineFreeLook fl = null;
        CinemachineVirtualCamera vcam = null;

        if (active != null)
        {
            var go = active.VirtualCameraGameObject;
            if (go)
            {
                fl = go.GetComponent<CinemachineFreeLook>();
                vcam = go.GetComponent<CinemachineVirtualCamera>();
            }
        }

        // 2-a) 자유시점 FreeLook → FOV로 줌
        if (fl != null)
        {
            var lens = fl.m_Lens;
            lens.FieldOfView = Mathf.Clamp(lens.FieldOfView - wheel * (step * 5f), minFov, maxFov);
            fl.m_Lens = lens;
            return;
        }

        // 2-b) 일반 VCam → Transposer Z로 줌
        if (vcam != null)
        {
            var transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer != null)
            {
                var off = transposer.m_FollowOffset;
                off.z = Mathf.Clamp(off.z - wheel * step, minZ, maxZ); // 휠↑=가까이
                transposer.m_FollowOffset = off;
                return;
            }
        }
    }
}
