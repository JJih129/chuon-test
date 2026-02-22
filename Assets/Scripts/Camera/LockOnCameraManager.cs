// LockOnCameraManager.cs
// ▶ 역할: FreeLook ↔ LockOn VCam 전환 + 락온 시 FollowOffset 보정/원복
// ▶ 주의: lockOnCam의 Body는 CinemachineTransposer 기준으로 작성됨.

using UnityEngine;
using Cinemachine;

public class LockOnCameraManager : MonoBehaviour
{
    [Header("Cinemachine 참조 (한글 설명)")]
    [Tooltip("락온 전용 CinemachineVirtualCamera (Body=Transposer 권장)")]
    public CinemachineVirtualCamera lockOnCam;      // [조절값]

    [Tooltip("기본 플레이어 추적용 CinemachineFreeLook 카메라")]
    public CinemachineFreeLook freeLookCam;         // [조절값]

    [Tooltip("기본 카메라 조작 스크립트 (예: FreeLookCamera)")]
    public MonoBehaviour freeLookDriver;            // [조절값]


    [Header("Target Group 설정 (한글 설명)")]
    [Tooltip("플레이어 + 적을 동시에 바라보는 CinemachineTargetGroup")]
    public CinemachineTargetGroup targetGroup;      // [조절값]


    [Header("플레이어 피벗 참조 (한글 설명)")]
    [Tooltip("플레이어 몸 중심 근처의 피벗 (PlayerLockOn에서 SetPlayerPivot으로 세팅됨)")]
    public Transform playerPivot;                   // [조절값]


    [Header("락온시 카메라 튜닝 (한글 설명)")]
    [Tooltip("락온 상태에서 FollowOffset.y에 더해질 값 (음수면 카메라가 더 낮아짐)")]
    public float lockOnFollowHeightOffset = -0.5f;  // [조절값]

    [Tooltip("락온 해제 시 FollowOffset을 반드시 원래 값으로 되돌릴지 여부")]
    public bool restoreFollowOffsetOnEnd = true;    // [조절값]


    [Header("디버그 상태 (읽기 전용)")]
    [SerializeField, Tooltip("현재 락온 VCam이 활성 상태인지 여부 (읽기 전용 디버그용)")]
    private bool isLockOnActive = false;

    // 내부 캐시: 원래 FollowOffset
    Vector3 _originalFollowOffset;
    bool _hasOriginalOffset = false;

    void Awake()
    {
        // 씬 시작 시점에 lockOnCam의 기본 FollowOffset을 캐싱해 둔다.
        CacheOriginalFollowOffset();
    }

    void OnValidate()
    {
        // 에디터에서 값이 바뀌었을 때도 기본값을 다시 캐싱해 두면 안전.
        CacheOriginalFollowOffset();
    }

    void CacheOriginalFollowOffset()
    {
        if (lockOnCam == null || _hasOriginalOffset) return;

        var transposer = lockOnCam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer == null) return;

        _originalFollowOffset = transposer.m_FollowOffset;
        _hasOriginalOffset = true;
    }

    // ===================== 외부에서 호출하는 API =====================

    /// <summary>
    /// 플레이어 피벗을 세팅하고, 기본 카메라 상태로 초기화.
    /// (보통 PlayerLockOn.Awake()에서 한 번 호출)
    /// </summary>
    public void SetPlayerPivot(Transform pivot)
    {
        playerPivot = pivot;

        // TargetGroup을 플레이어 1명만 바라보도록 초기화
        if (targetGroup != null && playerPivot != null)
        {
            targetGroup.m_Targets = new CinemachineTargetGroup.Target[1];
            targetGroup.m_Targets[0].target = playerPivot;
            targetGroup.m_Targets[0].weight = 1.2f;
            targetGroup.m_Targets[0].radius = 2f;
        }

        // 락온 VCam Follow / LookAt 기본 연결
        if (lockOnCam != null)
        {
            if (lockOnCam.Follow == null) lockOnCam.Follow = playerPivot;
            if (lockOnCam.LookAt == null && targetGroup != null)
                lockOnCam.LookAt = targetGroup.transform;
        }

        // 시작은 FreeLook 우선
        SetPriority(freeLookHigh: true);
        isLockOnActive = false;

        // 혹시 모를 FollowOffset 캐싱
        CacheOriginalFollowOffset();
    }

    /// <summary>
    /// 락온 시작: TargetGroup에 (플레이어+적) 등록, FollowOffset 보정, 우선순위 전환.
    /// </summary>
    public void StartLockOn(Transform enemyPivot)
    {
        if (playerPivot == null || targetGroup == null || lockOnCam == null || enemyPivot == null)
            return;

        // 1) TargetGroup에 플레이어 + 적 2개 타겟 세팅
        var arr = new CinemachineTargetGroup.Target[2];
        arr[0].target = playerPivot; arr[0].weight = 1.3f; arr[0].radius = 2f;
        arr[1].target = enemyPivot;  arr[1].weight = 1.0f; arr[1].radius = 2f;
        targetGroup.m_Targets = arr;

        if (lockOnCam.LookAt == null) lockOnCam.LookAt = targetGroup.transform;
        if (lockOnCam.Follow == null) lockOnCam.Follow = playerPivot;

        // 2) FollowOffset 보정 (Y만 추가로 내리거나 올리는 용도)
        CacheOriginalFollowOffset(); // 혹시 아직 캐싱이 안 되어 있으면 여기서 한 번 더

        var transposer = lockOnCam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer != null && _hasOriginalOffset)
        {
            var offset = _originalFollowOffset;
            offset.y += lockOnFollowHeightOffset;
            transposer.m_FollowOffset = offset;
        }

        // 3) FreeLook 입력 드라이버 끄고, 락온 VCam 우선순위 올리기
        if (freeLookDriver) freeLookDriver.enabled = false;
        SetPriority(freeLookHigh: false);

        isLockOnActive = true;
    }

    /// <summary>
    /// 락온 종료: TargetGroup을 플레이어만 바라보게 돌리고, FollowOffset 원복 + FreeLook 복귀.
    /// </summary>
    public void EndLockOn()
    {
        // 1) TargetGroup을 플레이어 1명만 바라보도록 복구
        if (targetGroup != null && playerPivot != null)
        {
            targetGroup.m_Targets = new CinemachineTargetGroup.Target[1];
            targetGroup.m_Targets[0].target = playerPivot;
            targetGroup.m_Targets[0].weight = 1.2f;
            targetGroup.m_Targets[0].radius = 2f;
        }

        // 2) FollowOffset 원래 값으로 복원 (옵션에 따라)
        if (restoreFollowOffsetOnEnd && lockOnCam != null && _hasOriginalOffset)
        {
            var transposer = lockOnCam.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer != null)
            {
                transposer.m_FollowOffset = _originalFollowOffset;
            }
        }

        // 3) FreeLook 입력 드라이버 다시 켜고, FreeLook 우선순위 회복
        if (freeLookDriver) freeLookDriver.enabled = true;
        SetPriority(freeLookHigh: true);

        isLockOnActive = false;
    }

    // =========================================================

    void SetPriority(bool freeLookHigh)
    {
        if (freeLookCam) freeLookCam.Priority = freeLookHigh ? 20 : 5;
        if (lockOnCam)  lockOnCam.Priority  = freeLookHigh ? 5  : 20;
    }
}
