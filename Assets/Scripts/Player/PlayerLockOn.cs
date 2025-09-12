using UnityEngine;

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
    [Tooltip("자동 생성 피벗의 기본 높이(전역 기본값)")]
    public float defaultPivotY = 1.3f; // ★ 기존 1.6f → 1.3f로 낮춤 (인스펙터에서 조절 가능)

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

        // 좌/우 전환: Q / E
        //if (IsLockOn && Input.GetKeyDown(KeyCode.Q)) SwitchTarget(-1);
        //if (IsLockOn && Input.GetKeyDown(KeyCode.E)) SwitchTarget(+1);
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

    // ==== 카메라 기준 좌/우 전환 (-1=왼쪽, +1=오른쪽) ====
    void SwitchTarget(int dir)
    {
        if (!IsLockOn || Camera.main == null) return;

        Transform cam = Camera.main.transform;
        Vector3 camRight = cam.right;

        Transform current = lockOnTarget;
        Transform best = null;
        float bestSide = -Mathf.Infinity;
        float bestDist = Mathf.Infinity;

        foreach (var e in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            Transform pivot = FindOrCreatePivot(e.transform, pivotName, defaultPivotY);
            if (pivot == current || pivot == null) continue;

            // 현재 타겟 기준 좌/우 판정
            Vector3 fromCur = (pivot.position - current.position).normalized;
            float side = Vector3.Dot(fromCur, camRight); // +오른쪽, -왼쪽

            if (dir < 0 && side >= 0) continue; // 왼쪽 찾는 중인데 오른쪽이면 패스
            if (dir > 0 && side <= 0) continue; // 오른쪽 찾는 중인데 왼쪽이면 패스

            float dist = Vector3.Distance(transform.position, pivot.position);
            float score = Mathf.Abs(side); // 더 측면에 크게 벌어진 것을 우선

            if (score > bestSide || (Mathf.Approximately(score, bestSide) && dist < bestDist))
            {
                bestSide = score;
                bestDist = dist;
                best = pivot;
            }
        }

        if (best != null)
        {
            lockOnTarget = best;
            cameraManager?.StartLockOn(lockOnTarget);
        }
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
        // 모델의 Renderer Bounds를 이용해 "머리쪽"에 더 가깝게 피벗을 배치
        float y = defaultY;
        var rend = root.GetComponentInChildren<Renderer>();
        if (rend)
        {
            // center ~ max 사이 60% 지점 정도(머리 쪽), 살짝 더 낮춤(-0.1f)
            y = Mathf.Lerp(rend.bounds.center.y, rend.bounds.max.y, 0.6f) - root.position.y;
            y -= 0.1f;
        }

        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0, y, 0);
        go.transform.localRotation = Quaternion.identity;
        return go.transform;
    }
}
