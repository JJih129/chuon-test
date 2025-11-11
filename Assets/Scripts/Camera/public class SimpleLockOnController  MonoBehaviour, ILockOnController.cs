// 파일명: SimpleLockOnController.cs
// 목적: 레거시 참조(IntegratedBossUI 등) 호환용 어댑터
// 동작: 내부적으로 PlayerLockOn과 동기화하여 currentTarget을 제공
// 삭제 금지: 오래된 코드가 이 타입을 직접 참조하므로 유지 필요

using UnityEngine;

public class SimpleLockOnController : MonoBehaviour, ILockOnController
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("① 참조")]
    [Tooltip("새 락온 시스템. 비워두면 GetComponent로 자동 탐색")]
    [SerializeField] private PlayerLockOn playerLockOn; // [조절값]

    [Header("② 현재 락온 대상(읽기 전용처럼 사용)")]
    [Tooltip("레거시 호환용. 내부적으로 PlayerLockOn.CurrentTarget과 동기화됨")]
    public Transform currentTarget; // [호환값]

    [Header("③ 타임라인 카메라 권한 플래그(호환)")]
    [Tooltip("타임라인이 카메라를 가져가면 true로 설정(필요 시 이벤트 연결)")]
    public bool timelineOwnsCamera = false; // [호환값]

    void Reset()
    {
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
    }

    void Awake()
    {
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
    }

    void LateUpdate()
    {
        // 매 프레임 PlayerLockOn과 동기화
        if (playerLockOn)
            currentTarget = playerLockOn.CurrentTarget;
    }

    // ===== ILockOnController 호환 구현 =====
    public Transform GetCurrentTarget()
    {
        return playerLockOn ? playerLockOn.CurrentTarget : currentTarget;
    }

    public void GiveCameraControlToTimeline(bool give)
    {
        timelineOwnsCamera = give;
        // 필요하면 여기서 카메라 우선순위/입력 비활성화를 연결
        // 예: LockOnCameraManager에 위임
    }

    // 레거시 코드에서 IsLockOn(bool) 형태를 호출할 수 있어 보조 API 제공
    public bool IsLockOn()
    {
        return playerLockOn && playerLockOn.IsLocked;
    }
}
