using UnityEngine;
using Cinemachine;

public class LockOnCameraManager : MonoBehaviour
{
    [Header("Cinemachine")]
    public CinemachineVirtualCamera lockOnCam;   // 락온용 VCam
    public CinemachineFreeLook freeLookCam;      // 자유시점 VCam(있으면)
    public MonoBehaviour freeLookDriver;         // 메인카메라 수동 스크립트(있으면)

    [Header("Target Group")]
    public CinemachineTargetGroup targetGroup;

    [Header("Refs")]
    public Transform playerPivot;  // Player/LockPivot

    public void SetPlayerPivot(Transform pivot)
    {
        playerPivot = pivot;

        // 그룹 초기화: 플레이어만
        if (targetGroup != null && playerPivot != null)
        {
            targetGroup.m_Targets = new CinemachineTargetGroup.Target[1];
            targetGroup.m_Targets[0].target = playerPivot;
            targetGroup.m_Targets[0].weight = 1.2f;
            targetGroup.m_Targets[0].radius = 2f;
        }

        // VCam Follow/LookAt 보강
        if (lockOnCam != null)
        {
            if (lockOnCam.Follow == null) lockOnCam.Follow = playerPivot;        // 숄더 고정(수직 흔들림 완화)
            if (lockOnCam.LookAt == null && targetGroup != null) lockOnCam.LookAt = targetGroup.transform;
        }

        SetPriority(freeLookHigh: true); // 시작은 자유시점
    }

    public void StartLockOn(Transform enemyPivot)
    {
        if (playerPivot == null || targetGroup == null || lockOnCam == null) return;

        // 그룹: 플레이어 + 적
        var arr = new CinemachineTargetGroup.Target[2];
        arr[0].target = playerPivot; arr[0].weight = 1.3f; arr[0].radius = 2f;
        arr[1].target = enemyPivot; arr[1].weight = 1.0f; arr[1].radius = 2f;
        targetGroup.m_Targets = arr;

        if (lockOnCam.LookAt == null) lockOnCam.LookAt = targetGroup.transform;
        if (lockOnCam.Follow == null) lockOnCam.Follow = playerPivot;

        if (freeLookDriver) freeLookDriver.enabled = false; // 충돌 방지
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

        if (freeLookDriver) freeLookDriver.enabled = true;
        SetPriority(freeLookHigh: true);
    }

    void SetPriority(bool freeLookHigh)
    {
        if (freeLookCam) freeLookCam.Priority = freeLookHigh ? 20 : 5;
        if (lockOnCam) lockOnCam.Priority = freeLookHigh ? 5 : 20;
    }
}
