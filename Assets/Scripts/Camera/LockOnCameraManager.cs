// LockOnCameraManager.cs (수정분)
// ▶ 변수 헤더 (한글 설명)
// lockOnFollowHeightOffset : 락온 상태에서 Follow 오프셋의 Y를 얼마나 더할지 (음수 = 카메라 낮춤)
// =======================================================
using UnityEngine;
using Cinemachine;

public class LockOnCameraManager : MonoBehaviour
{
    [Header("Cinemachine")]
    public CinemachineVirtualCamera lockOnCam;
    public CinemachineFreeLook freeLookCam;
    public MonoBehaviour freeLookDriver;

    [Header("Target Group")]
    public CinemachineTargetGroup targetGroup;

    [Header("Refs")]
    public Transform playerPivot;

    [Header("튜닝: 락온 카메라 높이 보정 (한글설명)")]
    [Tooltip("락온 시 Follow 오프셋의 Y 값에 더해질 값 (음수면 카메라가 낮아짐)")]
    public float lockOnFollowHeightOffset = -0.5f;

    // 내부 원복용
    Vector3 originalFollowOffset;
    bool followOffsetSaved = false;

    public void SetPlayerPivot(Transform pivot)
    {
        playerPivot = pivot;
        if (targetGroup != null && playerPivot != null)
        {
            targetGroup.m_Targets = new CinemachineTargetGroup.Target[1];
            targetGroup.m_Targets[0].target = playerPivot;
            targetGroup.m_Targets[0].weight = 1.2f;
            targetGroup.m_Targets[0].radius = 2f;
        }

        if (lockOnCam != null)
        {
            if (lockOnCam.Follow == null) lockOnCam.Follow = playerPivot;
            if (lockOnCam.LookAt == null && targetGroup != null) lockOnCam.LookAt = targetGroup.transform;
        }

        SetPriority(freeLookHigh: true);
    }

    public void StartLockOn(Transform enemyPivot)
    {
        if (playerPivot == null || targetGroup == null || lockOnCam == null) return;

        var arr = new CinemachineTargetGroup.Target[2];
        arr[0].target = playerPivot; arr[0].weight = 1.3f; arr[0].radius = 2f;
        arr[1].target = enemyPivot; arr[1].weight = 1.0f; arr[1].radius = 2f;
        targetGroup.m_Targets = arr;

        if (lockOnCam.LookAt == null) lockOnCam.LookAt = targetGroup.transform;
        if (lockOnCam.Follow == null) lockOnCam.Follow = playerPivot;

        // --- 여기서 Follow 오프셋을 낮춤 ---
        var transposer = lockOnCam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer != null)
        {
            if (!followOffsetSaved)
            {
                originalFollowOffset = transposer.m_FollowOffset;
                followOffsetSaved = true;
            }
            transposer.m_FollowOffset = originalFollowOffset + new Vector3(0f, lockOnFollowHeightOffset, 0f);
        }
        // -------------------------------------

        if (freeLookDriver) freeLookDriver.enabled = false;
        SetPriority(freeLookHigh: false);
    }

    public void EndLockOn()
    {
        if (targetGroup != null && playerPivot != null)
        {
            targetGroup.m_Targets = new CinemachineTargetGroup.Target[1];
            targetGroup.m_Targets[0].target = playerPivot;
            targetGroup.m_Targets[0].weight = 1.2f;
            targetGroup.m_Targets[0].radius = 2f;
        }

        // --- 원래 Follow 오프셋 복원 ---
        var transposer = lockOnCam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer != null && followOffsetSaved)
        {
            transposer.m_FollowOffset = originalFollowOffset;
            followOffsetSaved = false;
        }
        // -----------------------------------

        if (freeLookDriver) freeLookDriver.enabled = true;
        SetPriority(freeLookHigh: true);
    }

    void SetPriority(bool freeLookHigh)
    {
        if (freeLookCam) freeLookCam.Priority = freeLookHigh ? 20 : 5;
        if (lockOnCam) lockOnCam.Priority = freeLookHigh ? 5 : 20;
    }
}
