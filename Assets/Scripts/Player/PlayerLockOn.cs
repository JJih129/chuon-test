// PlayerLockOn.cs
using UnityEngine;

public class PlayerLockOn : MonoBehaviour
{
    public Transform lockOnTarget;
    public float lockOnRange = 20f;
    public LockOnCameraManager cameraManager;

    public bool IsLockOn => lockOnTarget != null;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (IsLockOn) ClearLockOnTarget();
            else SetLockOnTarget(FindLockOnTarget());
        }
    }

    public void SetLockOnTarget(Transform target)
    {
        lockOnTarget = target;
        if (cameraManager != null && target != null)
            cameraManager.StartLockOn(target);
        else if (cameraManager != null)
            cameraManager.EndLockOn();
    }

    public void ClearLockOnTarget()
    {
        lockOnTarget = null;
        if (cameraManager != null)
            cameraManager.EndLockOn();
    }

    Transform FindLockOnTarget()
    {
        Transform cam = Camera.main.transform;
        Vector3 origin = cam.position + cam.forward * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(origin, cam.forward, lockOnRange);
        float minDist = float.MaxValue;
        Transform nearestEnemy = null;
        foreach (var hit in hits)
        {
            if (hit.collider.CompareTag("Enemy") && hit.distance < minDist)
            {
                minDist = hit.distance;
                nearestEnemy = hit.collider.transform;
            }
        }
        return nearestEnemy;
    }
    // 락온 상태 스트레이프 이동 방향 계산 (PlayerMovement에서 호출)
    public Vector3 GetLockOnMoveDirection(float h, float v)
    {
        if (!IsLockOn || lockOnTarget == null) return Vector3.zero;
        Vector3 toTarget = lockOnTarget.position - transform.position;
        toTarget.y = 0f; // 평면 이동
        Vector3 targetDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
        Vector3 rightDir = Vector3.Cross(Vector3.up, targetDir);
        return (rightDir * h + targetDir * v).normalized;
    }

// 락온 상태에서 바라볼 방향 (회전)
    public Vector3 GetLookDirection()
    {
        if (!IsLockOn || lockOnTarget == null) return transform.forward;
        Vector3 lookDir = lockOnTarget.position - transform.position;
        lookDir.y = 0f;
        return lookDir.normalized;
    }

}