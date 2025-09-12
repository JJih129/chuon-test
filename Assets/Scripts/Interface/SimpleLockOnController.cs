// 파일명: SimpleLockOnController.cs
// 역할: 최소 기능의 락온 컨트롤러(프로토용)
// 실제 락온 시스템이 있으면 이 구현을 삭제하고, 해당 시스템에서 인터페이스만 구현하세요.

using UnityEngine;

public class SimpleLockOnController : MonoBehaviour, ILockOnController
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("현재 락온 대상 | 없으면 null")]
    public Transform currentTarget;

    [Header("타임라인 카메라 권한 | true면 타임라인이 카메라 제어")]
    public bool timelineOwnsCamera = false;

    public Transform GetCurrentTarget() => currentTarget;

    public void GiveCameraControlToTimeline(bool give)
    {
        timelineOwnsCamera = give;
        // 실제 프로젝트에서는 여기서:
        // - Cinemachine Brain/VCam 우선순위 변경
        // - 플레이어 카메라 입력 비활성화
        // 등을 수행하도록 연결하세요.
    }
}