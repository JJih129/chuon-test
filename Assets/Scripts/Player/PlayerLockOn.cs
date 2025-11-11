using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLockOn : MonoBehaviour
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("탐색 범위/시야")]
    [SerializeField] private float lockOnRange = 18f;                 // [조절값] 탐색 최대거리(m)
    [SerializeField, Range(10f,180f)] private float lockOnFOV = 70f;  // [조절값] 카메라 전방 기준 허용 시야각(도)
    [SerializeField] private LayerMask obstacleMask = 0;              // [조절값] 가림 체크 레이어

    [Header("타깃 피벗 생성/추적")]
    [SerializeField] private string pivotName = "LockPivot";          // [조절값] 타깃 루트 아래 생성/검색할 피벗 이름
    [SerializeField] private float defaultPivotY = 1.3f;              // [조절값] 렌더러 없을 때 기본 높이

    [Header("입력(레거시)")]
    [SerializeField] private bool useLegacyInput = true;              // [조절값] 레거시 입력 사용 여부
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;         // [조절값] 락온 토글 키
    [SerializeField] private KeyCode toggleKeyAlt = KeyCode.Mouse2;   // [조절값] 보조 토글 키

    [Header("카메라 연동")]
    [SerializeField] private LockOnCameraManager cameraMgr;           // [조절값] 시네머신 전환/타겟그룹 관리자

    // 현재 락온 타깃 피벗
    public Transform CurrentTarget { get; private set; }              // [읽기전용]
    // 신규 API
    public bool IsLocked => CurrentTarget != null;                    // [읽기전용] 락온 여부
    // 레거시 호환(API 변경으로 생긴 컴파일 에러 해결용)
    public bool IsLockOn => IsLocked;                                 // [읽기전용] 예전 코드 호환 속성

    void Awake()
    {
        // 플레이어 피벗 준비(카메라 매니저가 필요 시 사용)
        var playerPivot = EnsurePivot(transform, pivotName, defaultPivotY);
        cameraMgr?.SetPlayerPivot(playerPivot);
    }

    void Update()
    {
        if (!useLegacyInput) return;

        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt))
        {
            if (IsLocked) Unlock();
            else
            {
                var enemyRoot = FindBestTarget();
                if (enemyRoot) LockTo(enemyRoot);
            }
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

        float bestScore = float.MaxValue;
        Transform best = null;

        foreach (var e in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            Transform pivot = EnsurePivot(e.transform, pivotName, defaultPivotY);
            if (!pivot) continue;

            Vector3 to = pivot.position - cam.position;
            float dist = to.magnitude; if (dist > lockOnRange) continue;

            float ang = Vector3.Angle(cam.forward, to.normalized);
            if (ang > lockOnFOV * 0.5f) continue;

            if (obstacleMask.value != 0 &&
                Physics.Linecast(cam.position, pivot.position, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            float score = ang * 2f + dist; // 화면 중앙 가깝고 가까운 대상 우선
            if (score < bestScore) { bestScore = score; best = e.transform; }
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
