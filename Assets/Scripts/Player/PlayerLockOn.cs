// Assets/Scripts/Player/PlayerLockOn.cs
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLockOn : MonoBehaviour
{
    [Header("탐색 범위/시야")]
    [SerializeField, Min(0f)] private float lockOnRange = 18f;
    [SerializeField, Range(10f, 180f)] private float lockOnFOV = 70f;
    [SerializeField] private LayerMask obstacleMask = 0;          // 시야를 가리는 장애물 마스크(없으면 0)
    [SerializeField] private LayerMask enemyMask = 1 << 9;        // Enemy 레이어 가정(프로젝트에 맞게 조정)

    [Header("타깃 피벗 생성/추적")]
    [SerializeField] private string pivotName = "LockPivot";       // 타깃 루트 하위에 잠금 기준점 생성/사용
    [SerializeField] private float defaultPivotY = 1.3f;           // 렌더러 없을 때의 기본 높이

    [Header("입력(레거시) - Tab/MiddleMouse 토글")]
    [SerializeField] private bool useLegacyInput = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private KeyCode toggleKeyAlt = KeyCode.Mouse2;

    [Header("카메라 연동")]
    [SerializeField] private LockOnCameraManager cameraMgr;        // 락온 카메라 매니저(선택)

    // ===== 외부에서 참조하는 공개 상태/헬퍼 =====
    public Transform CurrentTarget { get; private set; }           // 현재 타깃의 "피벗" 트랜스폼
    public bool IsLocked => CurrentTarget != null;                 // 기존 호환
    public bool IsLockOn => IsLocked;                              // 기존 호환
    public bool HasTarget => CurrentTarget != null;                // ▼ PlayerDodgeController가 기대하던 프로퍼티
    public Transform Target => CurrentTarget;                      // 타깃 피벗 직접 접근용
    public Vector3 TargetPosition => CurrentTarget ? CurrentTarget.position : transform.position;
    public Vector3 DirectionFrom(Vector3 origin)
        => (TargetPosition - origin).sqrMagnitude > 0.0001f ? (TargetPosition - origin).normalized : transform.forward;

    Transform _playerPivot;                                        // 플레이어 쪽 피벗(카메라 기준)
    Transform _cam;                                                // Camera.main 캐시

    void Awake()
    {
        // 플레이어 쪽 피벗 확보 후 카메라 매니저에 전달
        _playerPivot = EnsurePivot(transform, pivotName, defaultPivotY);
        if (!cameraMgr) cameraMgr = FindAnyObjectByType<LockOnCameraManager>();
        cameraMgr?.SetPlayerPivot(_playerPivot);

        // 카메라 캐시(메인 카메라는 런타임에 바뀔 수 있어 Start에서 재확보)
        _cam = Camera.main ? Camera.main.transform : null;
    }

    void Start()
    {
        if (!_cam && Camera.main) _cam = Camera.main.transform;
    }

    void Update()
    {
        if (useLegacyInput && (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt)))
        {
            if (IsLocked) Unlock();
            else
            {
                var enemyRoot = FindBestTarget();
                if (enemyRoot) LockTo(enemyRoot);
            }
        }
    }

    // === 외부 제어용 API ===
    public void LockTo(Transform enemyRoot)
    {
        // 대상 루트에서 피벗을 확보(없으면 생성)
        CurrentTarget = EnsurePivot(enemyRoot, pivotName, defaultPivotY);
        cameraMgr?.StartLockOn(CurrentTarget);
    }

    public void Unlock()
    {
        CurrentTarget = null;
        cameraMgr?.EndLockOn();
    }

    public bool TryAutoLock()
    {
        var best = FindBestTarget();
        if (!best) return false;
        LockTo(best);
        return true;
    }

    // === 내부 구현 ===

    // 카메라 전방 원뿔(FOV) + 장애물 라인캐스트로 최적 타깃 탐색
    Transform FindBestTarget()
    {
        if (!_cam)
        {
            if (!Camera.main) return null;
            _cam = Camera.main.transform;
        }

        var cols = Physics.OverlapSphere(_cam.position, lockOnRange, enemyMask, QueryTriggerInteraction.Ignore);
        float bestScore = float.MaxValue;
        Transform bestRoot = null;

        for (int i = 0; i < cols.Length; ++i)
        {
            var root = cols[i].transform.root;
            var pivot = EnsurePivot(root, pivotName, defaultPivotY);
            if (!pivot) continue;

            Vector3 to = pivot.position - _cam.position;
            float dist = to.magnitude;
            if (dist > lockOnRange) continue;

            float ang = Vector3.Angle(_cam.forward, to / (dist > 0.0001f ? dist : 1f));
            if (ang > lockOnFOV * 0.5f) continue;

            if (obstacleMask.value != 0 &&
                Physics.Linecast(_cam.position, pivot.position, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            // 중앙+근거리 우선 가중치
            float score = ang * 2f + dist;
            if (score < bestScore)
            {
                bestScore = score;
                bestRoot = root;
            }
        }

        return bestRoot;
    }

    // 타깃 루트에 락온 피벗이 없으면 생성해서 반환
    Transform EnsurePivot(Transform root, string name, float defaultY)
    {
        if (!root) return null;

        var t = root.Find(name);
        if (t) return t;

        // 렌더러가 있으면 머리 근처로, 없으면 기본 높이로
        var rend = root.GetComponentInChildren<Renderer>();
        Vector3 worldPos;
        if (rend)
        {
            float cy = rend.bounds.center.y;
            float ty = rend.bounds.max.y;
            float y = Mathf.Lerp(cy, ty, 0.6f) - 0.1f; // 살짝 아래로
            worldPos = new Vector3(rend.bounds.center.x, y, rend.bounds.center.z);
        }
        else
        {
            worldPos = root.position + Vector3.up * defaultY;
        }

        var go = new GameObject(name);
        go.transform.SetParent(root, true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.identity;

        // 선택: 타깃 모델의 스케일/본 이동에 따라 Y를 따라가도록 보조 컴포넌트
        var follower = go.AddComponent<LockPivotFollower>();
        follower.sourceRenderer = rend;
        follower.yOffset = rend ? go.transform.position.y - rend.bounds.center.y : defaultY;

        return go.transform;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 탐색 구/시야각 가시화(에디터)
        var cam = Camera.main ? Camera.main.transform : null;
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        if (cam) Gizmos.DrawWireSphere(cam.position, lockOnRange);

        if (cam)
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
            Vector3 forward = cam.forward;
            Quaternion left = Quaternion.AngleAxis(-lockOnFOV * 0.5f, Vector3.up);
            Quaternion right = Quaternion.AngleAxis(lockOnFOV * 0.5f, Vector3.up);
            Gizmos.DrawLine(cam.position, cam.position + (left * forward).normalized * lockOnRange);
            Gizmos.DrawLine(cam.position, cam.position + (right * forward).normalized * lockOnRange);
        }
    }
#endif
}
