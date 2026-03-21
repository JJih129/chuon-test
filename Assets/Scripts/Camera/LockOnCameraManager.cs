using Unity.Cinemachine;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;

#pragma warning disable CS0618

public class LockOnCameraManager : MonoBehaviour
{
    [Header("Cinemachine Refs")]
    [Tooltip("Lock-on virtual camera.")]
    public CinemachineVirtualCameraBase lockOnCam;

    [Tooltip("Default free-look virtual camera.")]
    public CinemachineVirtualCameraBase freeLookCam;

    [Tooltip("Driver script that controls the free-look orbit. If a wrong component is assigned, it is fixed at runtime.")]
    public MonoBehaviour freeLookDriver;

    [Header("Target Group")]
    [Tooltip("Target group used while lock-on is active.")]
    public CinemachineTargetGroup targetGroup;

    [Header("Player Pivot")]
    [Tooltip("Player lock pivot.")]
    public Transform playerPivot;

    [Header("Lock-On Framing")]
    [Tooltip("Additional Y offset applied to the lock-on camera transposer.")]
    public float lockOnFollowHeightOffset = -0.5f;

    [Tooltip("Restore the original follow offset when lock-on ends.")]
    public bool restoreFollowOffsetOnEnd = true;

    [Header("Debug")]
    [SerializeField] bool isLockOnActive = false;
    public bool IsLockOnActive => isLockOnActive;

    Vector3 _originalFollowOffset;
    bool _hasOriginalOffset;

    void Awake()
    {
        ResolveFreeLookDriver();
        CacheOriginalFollowOffset();
    }

    IEnumerator Start()
    {
        yield return null;

        if (!isLockOnActive)
            ForceRestoreGameplayFreeLook();
    }

    void OnValidate()
    {
        ResolveFreeLookDriver();
        CacheOriginalFollowOffset();
    }

    void CacheOriginalFollowOffset()
    {
        if (lockOnCam == null || _hasOriginalOffset)
            return;

        if (!CinemachineCompat.TryGetBodyFollowOffset(lockOnCam, out _originalFollowOffset))
            return;
        _hasOriginalOffset = true;
    }

    MonoBehaviour ResolveFreeLookDriver()
    {
        if (freeLookDriver is FreeLookCamera assignedDriver)
        {
            assignedDriver.AutoResolveReferences();
            if (assignedDriver.HasValidReferences)
                return freeLookDriver;
        }

        if (freeLookCam != null)
        {
            var attachedDriver = freeLookCam.GetComponent<FreeLookCamera>();
            if (attachedDriver != null)
            {
                attachedDriver.AutoResolveReferences();
                freeLookDriver = attachedDriver;
                if (attachedDriver.HasValidReferences)
                    return freeLookDriver;
            }
        }

        var foundDrivers = FindObjectsByType<FreeLookCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < foundDrivers.Length; i++)
        {
            var foundDriver = foundDrivers[i];
            if (foundDriver == null)
                continue;

            foundDriver.AutoResolveReferences();
            if (!foundDriver.HasValidReferences)
                continue;

            freeLookDriver = foundDriver;
            return freeLookDriver;
        }

        return freeLookDriver;
    }

    public void SetFreeLookDriverEnabled(bool enabled)
    {
        var driver = ResolveFreeLookDriver();
        if (driver != null)
            driver.enabled = enabled;
    }

    public void RestoreFreeLookManualControl()
    {
        var driver = ResolveFreeLookDriver();
        if (driver is FreeLookCamera freeLookCamera)
        {
            freeLookCamera.RestoreManualControl();
            return;
        }

        if (driver != null)
            driver.enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ForceRestoreGameplayFreeLook()
    {
        ResolveFreeLookDriver();

        if (freeLookCam != null)
        {
            freeLookCam.gameObject.SetActive(true);
            freeLookCam.enabled = true;
            freeLookCam.Priority = 100;
        }

        if (lockOnCam != null)
        {
            lockOnCam.gameObject.SetActive(true);
            lockOnCam.enabled = true;
            lockOnCam.Priority = 5;
        }

        RestoreFreeLookManualControl();
        isLockOnActive = false;
    }

    public string BuildDebugSummary()
    {
        var driver = ResolveFreeLookDriver();
        string driverType = driver != null ? driver.GetType().Name : "<null>";
        string driverEnabled = driver != null ? driver.enabled.ToString() : "<null>";
        string driverActive = driver != null ? driver.gameObject.activeInHierarchy.ToString() : "<null>";

        string freeLookEnabled = freeLookCam != null ? freeLookCam.enabled.ToString() : "<null>";
        string freeLookActive = freeLookCam != null ? freeLookCam.gameObject.activeInHierarchy.ToString() : "<null>";
        string freeLookPriority = freeLookCam != null ? freeLookCam.Priority.ToString() : "<null>";

        string lockOnEnabled = lockOnCam != null ? lockOnCam.enabled.ToString() : "<null>";
        string lockOnActive = lockOnCam != null ? lockOnCam.gameObject.activeInHierarchy.ToString() : "<null>";
        string lockOnPriority = lockOnCam != null ? lockOnCam.Priority.ToString() : "<null>";

        return $"freeLookCam(enabled={freeLookEnabled},active={freeLookActive},priority={freeLookPriority}) "
             + $"lockOnCam(enabled={lockOnEnabled},active={lockOnActive},priority={lockOnPriority}) "
             + $"driver(type={driverType},enabled={driverEnabled},active={driverActive}) "
             + $"isLockOnActive={isLockOnActive}";
    }

    public void SetPlayerPivot(Transform pivot)
    {
        playerPivot = pivot;

        if (targetGroup != null && playerPivot != null)
        {
            targetGroup.Targets = new List<CinemachineTargetGroup.Target>
            {
                new() { Object = playerPivot, Weight = 1.2f, Radius = 2f }
            };
        }

        if (lockOnCam != null)
        {
            if (lockOnCam.Follow == null)
                lockOnCam.Follow = playerPivot;

            if (lockOnCam.LookAt == null && targetGroup != null)
                lockOnCam.LookAt = targetGroup.transform;
        }

        SetPriority(freeLookHigh: true);
        isLockOnActive = false;
        CacheOriginalFollowOffset();
    }

    public void StartLockOn(Transform enemyPivot)
    {
        if (playerPivot == null || targetGroup == null || lockOnCam == null || enemyPivot == null)
            return;

        targetGroup.Targets = new List<CinemachineTargetGroup.Target>
        {
            new() { Object = playerPivot, Weight = 1.3f, Radius = 2f },
            new() { Object = enemyPivot, Weight = 1.0f, Radius = 2f }
        };

        if (lockOnCam.LookAt == null)
            lockOnCam.LookAt = targetGroup.transform;
        if (lockOnCam.Follow == null)
            lockOnCam.Follow = playerPivot;

        CacheOriginalFollowOffset();
        if (_hasOriginalOffset)
        {
            var offset = _originalFollowOffset;
            offset.y += lockOnFollowHeightOffset;
            CinemachineCompat.TrySetBodyFollowOffset(lockOnCam, offset);
        }

        SetFreeLookDriverEnabled(false);

        SetPriority(freeLookHigh: false);
        isLockOnActive = true;
    }

    public void EndLockOn()
    {
        EndLockOn(true);
    }

    public void EndLockOn(bool snapFreeLookCamera)
    {
        if (targetGroup != null && playerPivot != null)
        {
            targetGroup.Targets = new List<CinemachineTargetGroup.Target>
            {
                new() { Object = playerPivot, Weight = 1.2f, Radius = 2f }
            };
        }

        if (restoreFollowOffsetOnEnd && lockOnCam != null && _hasOriginalOffset)
        {
            CinemachineCompat.TrySetBodyFollowOffset(lockOnCam, _originalFollowOffset);
        }

        var driver = ResolveFreeLookDriver();
        SetFreeLookDriverEnabled(true);

        SetPriority(freeLookHigh: true);
        if (snapFreeLookCamera && driver is FreeLookCamera freeLookCamera)
            freeLookCamera.SnapBehindPlayer();

        isLockOnActive = false;
    }

    void SetPriority(bool freeLookHigh)
    {
        if (freeLookCam != null)
            freeLookCam.Priority = freeLookHigh ? 20 : 5;

        if (lockOnCam != null)
            lockOnCam.Priority = freeLookHigh ? 5 : 20;
    }
}
