using UnityEngine;

/// ===== 변수 헤더 (한글설명) =====
/// lockOnRange     : 락온 가능한 최대 거리 (미터)
/// lockOnFOV       : 카메라 전방 기준 허용 시야각(도)
/// obstacleMask    : 카메라-타겟 사이 라인캐스트에 사용할 장애물 레이어
/// pivotName       : 적 루트 아래에 생성할 피벗 오브젝트 이름
/// defaultPivotY   : 렌더러가 없을 때 루트 기준 기본 피벗 높이 (월드 단위 오프셋)
/// lockOnTarget    : 현재 락온 타겟(피벗 Transform)
/// cameraManager   : Cinemachine 연동용 카메라 매니저
/// playerPivot     : 플레이어용 피벗 (없으면 생성)
/// =====================================
public class PlayerLockOn : MonoBehaviour
{
    [Header("탐색")]
    [Tooltip("락온 가능한 최대 거리")]
    public float lockOnRange = 18f;
    [Tooltip("카메라 전방 기준 시야각(도)")]
    [Range(10f, 180f)] public float lockOnFOV = 70f;
    [Tooltip("카메라-타겟 사이 라인캐스트에 사용할 장애물 레이어")]
    public LayerMask obstacleMask;
    [Tooltip("적/플레이어 루트 아래에 생성(또는 검색)할 피벗 오브젝트 이름")]
    public string pivotName = "LockPivot";

    [Header("Pivot")]
    [Tooltip("자동 생성 피벗의 기본 높이(월드 단위). 렌더러가 없을 때 사용")]
    public float defaultPivotY = 1.3f;

    [Header("상태")]
    public Transform lockOnTarget;                 // 현재 타겟 Pivot(=적의 LockPivot)
    public bool IsLockOn => lockOnTarget != null;

    [Header("연동")]
    public LockOnCameraManager cameraManager;      // 시네머신 카메라 매니저
    public Transform playerPivot;                  // Player/LockPivot (없으면 자동 생성)

    void Awake()
    {
        // 플레이어 피벗 준비(없으면 생성)
        if (playerPivot == null)
            playerPivot = FindOrCreatePivot(transform, pivotName, defaultPivotY);

        // 카메라 매니저에 플레이어 피벗 전달
        cameraManager?.SetPlayerPivot(playerPivot);
    }

    void Update()
    {
        // 락온 토글: 마우스 휠 클릭 또는 Tab
        if (Input.GetMouseButtonDown(2) || Input.GetKeyDown(KeyCode.Tab))
        {
            if (IsLockOn) ClearLockOnTarget();
            else
            {
                Transform enemyRoot = FindBestTargetNearestToCenter();
                if (enemyRoot != null) StartLockOn(enemyRoot);
            }
        }
    }

    // ==== 락온 시작/해제 ====
    public void StartLockOn(Transform enemyRoot)
    {
        // 적 루트에서 LockPivot을 찾거나 생성
        lockOnTarget = FindOrCreatePivot(enemyRoot, pivotName, defaultPivotY);
        cameraManager?.StartLockOn(lockOnTarget);
    }

    public void ClearLockOnTarget()
    {
        lockOnTarget = null;
        cameraManager?.EndLockOn();
    }

    // ==== 화면 중앙 + 거리 가중치로 베스트 타겟 찾기 ====
    Transform FindBestTargetNearestToCenter()
    {
        if (Camera.main == null) return null;

        Transform cam = Camera.main.transform;
        Vector3 camPos = cam.position;
        Vector3 camFwd = cam.forward;

        float bestScore = float.MaxValue;
        Transform bestRoot = null;

        // Tag=Enemy 대상 검색
        foreach (var e in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            Transform pivot = FindOrCreatePivot(e.transform, pivotName, defaultPivotY);
            if (pivot == null) continue;

            Vector3 to = pivot.position - camPos;
            float dist = to.magnitude;
            if (dist > lockOnRange) continue;

            float ang = Vector3.Angle(camFwd, to.normalized);
            if (ang > lockOnFOV * 0.5f) continue;

            // 카메라-타겟 사이 장애물 체크(옵션)
            if (obstacleMask.value != 0 && Physics.Linecast(camPos, pivot.position, obstacleMask))
                continue;

            // 점수: 각도 가중치 + 거리
            float score = ang * 2f + dist;
            if (score < bestScore)
            {
                bestScore = score;
                bestRoot = e.transform;
            }
        }
        return bestRoot;
    }

    // ==== PlayerMovement가 쓰는 헬퍼 ====
    public Vector3 GetLockOnMoveDirection(float h, float v)
    {
        if (!IsLockOn || lockOnTarget == null)
            return Vector3.zero;

        Vector3 toTarget = lockOnTarget.position - transform.position;
        toTarget.y = 0f;

        Vector3 fwd = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        return (right * h + fwd * v).normalized;
    }

    public Vector3 GetLookDirection()
    {
        if (!IsLockOn || lockOnTarget == null)
            return transform.forward;

        Vector3 look = lockOnTarget.position - transform.position;
        look.y = 0f;
        return look.sqrMagnitude > 0.0001f ? look.normalized : transform.forward;
    }

    // ==== Pivot 유틸 ====
    Transform FindOrCreatePivot(Transform root, string name, float defaultY)
    {
        // 루트 아래에 동일 이름 오브젝트가 있으면 그걸 우선 사용
        var t = root.Find(name);
        if (t) return t;
        return CreatePivot(root, name, defaultY);
    }

    Transform CreatePivot(Transform root, string name, float defaultY)
    {
        // 1) 렌더러 기반 world Y 계산
        var rend = root.GetComponentInChildren<Renderer>();
        Vector3 desiredWorldPos;

        if (rend != null)
        {
            // 렌더러 중심과 상단 사이에서 적절한 높이 선택 (월드 Y)
            float centreY = rend.bounds.center.y;
            float topY = rend.bounds.max.y;
            float chosenY = Mathf.Lerp(centreY, topY, 0.6f) - 0.1f;
            // pivot의 xz는 렌더러 중심을 따름
            desiredWorldPos = new Vector3(rend.bounds.center.x, chosenY, rend.bounds.center.z);
        }
        else
        {
            // 렌더러 없으면 루트 위치 + 기본 오프셋(월드 단위)
            desiredWorldPos = root.position + Vector3.up * defaultY;
        }

        // GameObject 생성 및 부모 설정. 월드 위치를 의도대로 유지하도록 설정
        var go = new GameObject(name);
        go.transform.SetParent(root, true); // true: worldPosition stays
        go.transform.position = desiredWorldPos;
        go.transform.rotation = Quaternion.identity;

        // 피벗 팔로워 붙이기 및 yOffset 계산 (월드 기준)
        var follower = go.AddComponent<LockPivotFollower>();
        if (rend != null)
        {
            follower.sourceRenderer = rend;
            // yOffset = pivotWorldY - rendererCenterY (월드 단위)
            follower.yOffset = go.transform.position.y - rend.bounds.center.y;
        }
        else
        {
            follower.sourceRenderer = null;
            follower.yOffset = 0f;
        }

        return go.transform;
    }
}
