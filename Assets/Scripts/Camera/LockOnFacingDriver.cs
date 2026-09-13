using UnityEngine;

// ===== 변수 헤더(한글 설명) =====
// lockOn : PlayerLockOn 참조(타겟 읽기)
// playerRoot : 회전시킬 루트
// yawSpeedDeg : 초당 회전 속도(도)
// autoUnlockWhenFar : 너무 멀면 자동 해제할지
// maxDistance : 자동 해제 거리
[DisallowMultipleComponent]
public class LockOnFacingDriver : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerLockOn lockOn;      // [조절값]
    [SerializeField] private Transform playerRoot;     // [조절값]

    [Header("튜닝")]
    [SerializeField, Range(90f,1080f)] private float yawSpeedDeg = 540f; // [조절값]
    [SerializeField] private bool autoUnlockWhenFar = true;              // [조절값]
    [SerializeField] private float maxDistance = 30f;                    // [조절값]

    float maxDistanceSq;

    void Reset()
    {
        if (!playerRoot) playerRoot = transform;
        if (!lockOn) lockOn = GetComponent<PlayerLockOn>();
        CacheDerivedValues();
    }

    void Awake()
    {
        CacheDerivedValues();
    }

    void OnValidate()
    {
        CacheDerivedValues();
    }

    void Update()
    {
        if (lockOn == null || !lockOn.IsLocked || playerRoot == null) return;

        var t = lockOn.CurrentTarget;
        if (!t)
        {
            lockOn.Unlock();
            return;
        }

        Vector3 dir = t.position - playerRoot.position;

        if (autoUnlockWhenFar && dir.sqrMagnitude > maxDistanceSq)
        {
            lockOn.Unlock();
            return;
        }

        dir.y = 0f; if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
        playerRoot.rotation = Quaternion.RotateTowards(playerRoot.rotation, target, yawSpeedDeg * Time.deltaTime);
    }

    void CacheDerivedValues()
    {
        float distance = Mathf.Max(0f, maxDistance);
        maxDistanceSq = distance * distance;
    }
}
