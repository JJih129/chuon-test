// 파일명: ILockOnController.cs
// 역할: 락온 대상 조회 및 타임라인(컷씬) 동안 카메라 권한 위임

using UnityEngine;

public interface ILockOnController
{
    // ===== 변수 헤더(설명) =====
    // 현재 락온된 대상의 Transform(없으면 null)
    Transform GetCurrentTarget();

    // 타임라인이 카메라를 제어하도록 권한을 넘김/회수
    void GiveCameraControlToTimeline(bool give);
}