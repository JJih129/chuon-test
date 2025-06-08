using UnityEngine;
using Cinemachine;

public class LockOnCameraManager : MonoBehaviour
{
    public CinemachineTargetGroup targetGroup;
    public CinemachineVirtualCamera lockOnCam;
    public Transform player;

    public void StartLockOn(Transform enemy)
    {
        targetGroup.m_Targets = new CinemachineTargetGroup.Target[2];
        targetGroup.m_Targets[0].target = player;
        targetGroup.m_Targets[0].weight = 1;
        targetGroup.m_Targets[0].radius = 2;
        targetGroup.m_Targets[1].target = enemy;
        targetGroup.m_Targets[1].weight = 1;
        targetGroup.m_Targets[1].radius = 2;

        lockOnCam.Priority = 20; // 락온 카메라 활성화
    }

    public void EndLockOn()
    {
        targetGroup.m_Targets = new CinemachineTargetGroup.Target[1];
        targetGroup.m_Targets[0].target = player;
        targetGroup.m_Targets[0].weight = 1;
        targetGroup.m_Targets[0].radius = 2;

        lockOnCam.Priority = 0; // 락온 카메라 비활성화
    }
}