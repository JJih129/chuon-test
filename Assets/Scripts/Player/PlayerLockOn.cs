using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLockOn : MonoBehaviour
{
    [Header("탐색 범위/시야")]
    [SerializeField] private float lockOnRange = 18f;
    [SerializeField, Range(10f,180f)] private float lockOnFOV = 70f;
    [SerializeField] private LayerMask obstacleMask = 0;
    [SerializeField] private LayerMask enemyMask = 1 << 9; // Enemy 레이어 가정 [조절값]

    [Header("타깃 피벗 생성/추적")]
    [SerializeField] private string pivotName = "LockPivot";
    [SerializeField] private float defaultPivotY = 1.3f;

    [Header("입력(레거시)")]
    [SerializeField] private bool useLegacyInput = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private KeyCode toggleKeyAlt = KeyCode.Mouse2;

    [Header("카메라 연동")]
    [SerializeField] private LockOnCameraManager cameraMgr;

    public Transform CurrentTarget { get; private set; }
    public bool IsLocked => CurrentTarget != null;
    public bool IsLockOn => IsLocked; // 호환

    Transform _playerPivot;

    void Awake()
    {
        _playerPivot = EnsurePivot(transform, pivotName, defaultPivotY);
        cameraMgr?.SetPlayerPivot(_playerPivot);
    }

    void Update()
    {
        if (!useLegacyInput) return;
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt))
        {
            if (IsLocked) Unlock();
            else { var enemyRoot = FindBestTarget(); if (enemyRoot) LockTo(enemyRoot); }
        }
    }

    public void LockTo(Transform enemyRoot)
    {
        CurrentTarget = EnsurePivot(enemyRoot, pivotName, defaultPivotY);
        cameraMgr?.StartLockOn(CurrentTarget);
    }

    public void Unlock()
    {
        CurrentTarget = null;
        cameraMgr?.EndLockOn();
    }

    Transform FindBestTarget()
    {
        if (!Camera.main) return null;
        Transform cam = Camera.main.transform;

        // 물리기반 근접 후보 탐색
        var cols = Physics.OverlapSphere(cam.position, lockOnRange, enemyMask, QueryTriggerInteraction.Ignore);
        float bestScore = float.MaxValue;
        Transform best = null;

        for (int i = 0; i < cols.Length; ++i)
        {
            var root = cols[i].transform.root;
            var pivot = EnsurePivot(root, pivotName, defaultPivotY);
            if (!pivot) continue;

            Vector3 to = pivot.position - cam.position;
            float dist = to.magnitude; if (dist > lockOnRange) continue;

            float ang = Vector3.Angle(cam.forward, to.normalized);
            if (ang > lockOnFOV * 0.5f) continue;

            if (obstacleMask.value != 0 &&
                Physics.Linecast(cam.position, pivot.position, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            float score = ang * 2f + dist; // 중앙+가까움 우선
            if (score < bestScore) { bestScore = score; best = root; }
        }
        return best;
    }

    Transform EnsurePivot(Transform root, string name, float defaultY)
    {
        var t = root.Find(name);
        if (t) return t;

        var rend = root.GetComponentInChildren<Renderer>();
        Vector3 worldPos;
        if (rend)
        {
            float cy = rend.bounds.center.y;
            float ty = rend.bounds.max.y;
            float y  = Mathf.Lerp(cy, ty, 0.6f) - 0.1f;
            worldPos = new Vector3(rend.bounds.center.x, y, rend.bounds.center.z);
        }
        else worldPos = root.position + Vector3.up * defaultY;

        var go = new GameObject(name);
        go.transform.SetParent(root, true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.identity;

        var follower = go.AddComponent<LockPivotFollower>();
        follower.sourceRenderer = rend;
        follower.yOffset = rend ? go.transform.position.y - rend.bounds.center.y : 0f;

        return go.transform;
    }
}
