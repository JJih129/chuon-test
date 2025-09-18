using UnityEngine;

/// <summary>
/// 레이/스피어캐스트로 상호작용 대상 도달 여부를 로그하는 디버거
/// - Player 레이어가 있으면 자동으로 마스크에서 제외
/// - T 키: 즉시 한 번 검사
/// - continuousCheck=true: 히트 대상이 바뀔 때만 로그
/// </summary>
public class InteractionRaycastDebugger : MonoBehaviour
{
    // [헤더] 테스트에 사용할 카메라 트랜스폼 (비우면 Camera.main 사용)
    public Transform cameraTransform;

    // [헤더] 검사할 레이어 마스크(Interactable 레이어 포함)
    public LayerMask interactLayer = ~0; // 기본: Everything

    // [헤더] 최대 검사 거리(미터)
    public float maxDistance = 5f;

    // [헤더] 스피어캐스트 사용할지 여부(false면 Raycast)
    public bool useSphereCast = true;

    // [헤더] 스피어 반지름(스피어캐스트 사용 시)
    public float sphereRadius = 0.15f;

    // [헤더] 연속 검사 모드: true면 히트 대상이 바뀔 때만 로그
    public bool continuousCheck = true;

    // [헤더] 디버그 라인 지속 시간(초)
    public float debugLineDuration = 0.1f;

    Collider _prevHit;

    void Awake()
    {
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            DoCheck(logAlways: true);
        }

        if (continuousCheck)
        {
            DoCheck(logAlways: false);
        }
    }

    void DoCheck(bool logAlways)
    {
        if (!cameraTransform) return;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        // LayerMask -> int mask 변환
        int mask = interactLayer.value;

        // "Player" 레이어가 존재하면 자동으로 제외
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
        {
            mask &= ~(1 << playerLayer);
        }

        // 히트 체크
        if (useSphereCast)
        {
            if (Physics.SphereCast(ray, sphereRadius, out RaycastHit hit, maxDistance, mask, QueryTriggerInteraction.Collide))
            {
                Debug.DrawLine(ray.origin, hit.point, Color.cyan, debugLineDuration);
                LogHit(hit, logAlways);
                _prevHit = hit.collider;
                return;
            }
        }
        else
        {
            if (Physics.Raycast(ray, out RaycastHit hit2, maxDistance, mask, QueryTriggerInteraction.Collide))
            {
                Debug.DrawLine(ray.origin, hit2.point, Color.green, debugLineDuration);
                LogHit(hit2, logAlways);
                _prevHit = hit2.collider;
                return;
            }
        }

        // 미적중
        Debug.DrawLine(ray.origin, ray.origin + ray.direction * maxDistance, Color.red, debugLineDuration);
        if (logAlways || _prevHit != null)
        {
            _prevHit = null;
            Debug.Log("[RaycastDebugger] Miss (no hit within distance)");
        }
    }

    void LogHit(RaycastHit hit, bool logAlways)
    {
        var col = hit.collider;
        if (col == null) return;

        // 이전과 같으면 스킵(continuous 모드)
        if (!logAlways && col == _prevHit) return;

        // 레이어 이름 포함 로그
        string layerName = LayerMask.LayerToName(col.gameObject.layer);
        bool hasInteractable = false;

        // IInteractable 여부 검사 (부모 포함)
        var comps = col.GetComponentInParent<Transform>()?.GetComponentsInParent<MonoBehaviour>(true);
        if (comps != null)
        {
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c is IInteractable) { hasInteractable = true; break; }
            }
        }

        Debug.Log($"[RaycastDebugger] Hit: {col.name} | layer: {layerName} | distance: {hit.distance:F2}m | point: {hit.point} | hasIInteractable: {hasInteractable}");
    }
}
