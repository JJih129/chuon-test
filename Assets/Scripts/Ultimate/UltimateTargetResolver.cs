using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateTargetResolver : MonoBehaviour
{
    const int MaxCandidates = 64;

    [Header("Targeting")]
    [SerializeField, Min(0.1f)] private float maxTargetDistance = 24f;
    [SerializeField, Range(1f, 180f)] private float cameraAngleLimit = 85f;
    [SerializeField, Range(1f, 180f)] private float playerAngleLimit = 110f;
    [SerializeField] private LayerMask targetLayerMask = 1 << 12;
    [SerializeField] private LayerMask lineOfSightMask = 0;
    [SerializeField] private bool requireLineOfSight = false;
    [SerializeField] private bool requireCameraAngleWhenUnlocked = true;
    [SerializeField] private bool allowClosestFallbackOutsideView = false;
    [SerializeField, Min(0f)] private float targetAimHeightFallback = 1.15f;

    [Header("Debug")]
    [SerializeField] private bool debugLog;
    [SerializeField] private bool showDebugGizmos;

    readonly Collider[] _overlapHits = new Collider[MaxCandidates];
    readonly RaycastHit[] _losHits = new RaycastHit[8];
    readonly Transform[] _uniqueRoots = new Transform[MaxCandidates];
    readonly Candidate[] _candidates = new Candidate[MaxCandidates];

    UltimateCinematicFrame _lastFrame;
    bool _hasLastFrame;

    struct Candidate
    {
        public Transform Root;
        public Transform TargetTransform;
        public IUltimateTarget Target;
        public Vector3 AimPoint;
        public float DistanceSq;
        public float CameraAngle;
        public float PlayerAngle;
        public int Priority;
    }

    public bool TryResolve(PlayerUltimateController owner, out IUltimateTarget target, out Transform targetTransform)
    {
        target = null;
        targetTransform = null;
        _hasLastFrame = false;

        if (owner == null)
            return false;

        Transform playerRoot = owner.GetUltimatePlayerRoot();
        if (playerRoot == null)
            playerRoot = owner.transform;

        Camera mainCamera = Camera.main;
        Transform cameraTransform = mainCamera != null ? mainCamera.transform : null;
        ILockOnController lockOn = owner.GetComponent<ILockOnController>();

        Transform lockedTarget = lockOn != null && lockOn.IsLockedOn() ? lockOn.GetCurrentTarget() : null;
        if (TryResolveSpecificTarget(lockedTarget, playerRoot, cameraTransform, out Candidate lockedCandidate))
        {
            ApplyResolved(owner, lockedCandidate, out target, out targetTransform);
            return true;
        }

        int candidateCount = CollectCandidates(playerRoot, cameraTransform);
        if (candidateCount <= 0)
            return false;

        if (TrySelectByCamera(candidateCount, out Candidate cameraCandidate) ||
            TrySelectByPlayer(candidateCount, out cameraCandidate) ||
            (allowClosestFallbackOutsideView && TrySelectClosest(candidateCount, out cameraCandidate)))
        {
            ApplyResolved(owner, cameraCandidate, out target, out targetTransform);
            return true;
        }

        return false;
    }

    public bool TryCreateFrame(PlayerUltimateController owner, Transform targetRoot, out UltimateCinematicFrame frame)
    {
        Transform playerRoot = owner != null ? owner.GetUltimatePlayerRoot() : transform;
        Vector3 playerPoint = playerRoot != null ? playerRoot.position : transform.position;
        Vector3 targetCenter = ResolveAimPoint(targetRoot);
        Vector3 fallbackForward = playerRoot != null ? playerRoot.forward : transform.forward;
        frame = UltimateCinematicFrame.Create(playerPoint, targetCenter, fallbackForward);
        _lastFrame = frame;
        _hasLastFrame = true;
        return frame.IsValid;
    }

    void ApplyResolved(PlayerUltimateController owner, Candidate candidate, out IUltimateTarget target, out Transform targetTransform)
    {
        target = candidate.Target;
        targetTransform = candidate.TargetTransform != null ? candidate.TargetTransform : candidate.Root;
        TryCreateFrame(owner, candidate.Root, out _);

        if (debugLog)
            Debug.Log($"[UltimateTargetResolver] target={targetTransform.name} dist={Mathf.Sqrt(candidate.DistanceSq):0.##}", this);
    }

    bool TryResolveSpecificTarget(Transform source, Transform playerRoot, Transform cameraTransform, out Candidate candidate)
    {
        candidate = default;
        if (source == null || !source.gameObject.activeInHierarchy)
            return false;

        Transform root = ResolveTargetRoot(source);
        if (root == null)
            return false;

        IUltimateTarget ultimateTarget = ResolveUltimateTarget(root);
        if (ultimateTarget == null)
            return false;

        Vector3 aimPoint = ResolveAimPoint(root);
        float distanceSq = (aimPoint - playerRoot.position).sqrMagnitude;
        float maxDistance = Mathf.Max(0.1f, maxTargetDistance);
        if (distanceSq > maxDistance * maxDistance)
            return false;

        if (!HasLineOfSight(playerRoot, cameraTransform, root, aimPoint))
            return false;

        candidate = BuildCandidate(root, source, ultimateTarget, aimPoint, distanceSq, playerRoot, cameraTransform);
        return true;
    }

    int CollectCandidates(Transform playerRoot, Transform cameraTransform)
    {
        ClearUniqueRoots();

        int hitCount = Physics.OverlapSphereNonAlloc(
            playerRoot.position,
            maxTargetDistance,
            _overlapHits,
            targetLayerMask,
            QueryTriggerInteraction.Collide);

        int uniqueCount = 0;
        int candidateCount = 0;
        for (int i = 0; i < hitCount && candidateCount < _candidates.Length; i++)
        {
            Collider hit = _overlapHits[i];
            if (hit == null)
                continue;

            Transform root = ResolveTargetRoot(hit.transform);
            if (root == null || !root.gameObject.activeInHierarchy || root == playerRoot || root.IsChildOf(playerRoot))
                continue;

            bool duplicate = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (_uniqueRoots[j] == root)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
                continue;

            if (uniqueCount < _uniqueRoots.Length)
                _uniqueRoots[uniqueCount++] = root;

            IUltimateTarget ultimateTarget = ResolveUltimateTarget(root);
            if (ultimateTarget == null)
                continue;

            Vector3 aimPoint = ResolveAimPoint(root);
            if (!HasLineOfSight(playerRoot, cameraTransform, root, aimPoint))
                continue;

            float distanceSq = (aimPoint - playerRoot.position).sqrMagnitude;
            _candidates[candidateCount++] = BuildCandidate(root, hit.transform, ultimateTarget, aimPoint, distanceSq, playerRoot, cameraTransform);
        }

        return candidateCount;
    }

    Candidate BuildCandidate(Transform root, Transform targetTransform, IUltimateTarget target, Vector3 aimPoint, float distanceSq, Transform playerRoot, Transform cameraTransform)
    {
        Vector3 playerForward = playerRoot.forward;
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude <= 0.0001f)
            playerForward = Vector3.forward;
        playerForward.Normalize();

        Vector3 toTargetFromPlayer = aimPoint - playerRoot.position;
        toTargetFromPlayer.y = 0f;
        Vector3 flatPlayerDir = toTargetFromPlayer.sqrMagnitude > 0.0001f ? toTargetFromPlayer.normalized : playerForward;

        float playerAngle = Vector3.Angle(playerForward, flatPlayerDir);
        float cameraAngle = 180f;
        if (cameraTransform != null)
        {
            Vector3 cameraForward = cameraTransform.forward;
            cameraForward.y = 0f;
            Vector3 toTargetFromCamera = aimPoint - cameraTransform.position;
            toTargetFromCamera.y = 0f;
            if (cameraForward.sqrMagnitude > 0.0001f && toTargetFromCamera.sqrMagnitude > 0.0001f)
                cameraAngle = Vector3.Angle(cameraForward.normalized, toTargetFromCamera.normalized);
        }

        return new Candidate
        {
            Root = root,
            TargetTransform = targetTransform,
            Target = target,
            AimPoint = aimPoint,
            DistanceSq = distanceSq,
            CameraAngle = cameraAngle,
            PlayerAngle = playerAngle,
            Priority = ResolvePriority(root)
        };
    }

    bool TrySelectByCamera(int count, out Candidate best)
    {
        return TrySelectByAngle(count, cameraAngleLimit, true, out best);
    }

    bool TrySelectByPlayer(int count, out Candidate best)
    {
        return TrySelectByAngle(count, playerAngleLimit, false, out best);
    }

    bool TrySelectByAngle(int count, float angleLimit, bool useCameraAngle, out Candidate best)
    {
        best = default;
        float bestScore = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Candidate candidate = _candidates[i];
            float angle = useCameraAngle ? candidate.CameraAngle : candidate.PlayerAngle;
            if (angle > angleLimit)
                continue;
            if (!useCameraAngle && requireCameraAngleWhenUnlocked && candidate.CameraAngle > cameraAngleLimit)
                continue;

            float score = angle * 3f + Mathf.Sqrt(candidate.DistanceSq) - candidate.Priority * 100f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            best = candidate;
            found = true;
        }

        return found;
    }

    bool TrySelectClosest(int count, out Candidate best)
    {
        best = default;
        float bestScore = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Candidate candidate = _candidates[i];
            float score = candidate.DistanceSq - candidate.Priority * 10000f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            best = candidate;
            found = true;
        }

        return found;
    }

    bool HasLineOfSight(Transform playerRoot, Transform cameraTransform, Transform targetRoot, Vector3 aimPoint)
    {
        if (!requireLineOfSight || lineOfSightMask.value == 0)
            return true;

        Vector3 origin = cameraTransform != null ? cameraTransform.position : playerRoot.position + Vector3.up * 1.2f;
        Vector3 delta = aimPoint - origin;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
            return true;

        int hitCount = Physics.RaycastNonAlloc(origin, delta / distance, _losHits, distance, lineOfSightMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = _losHits[i].collider;
            if (collider == null)
                continue;

            Transform hitTransform = collider.transform;
            if (hitTransform == null || hitTransform == targetRoot || hitTransform.IsChildOf(targetRoot) || hitTransform == playerRoot || hitTransform.IsChildOf(playerRoot))
                continue;

            return false;
        }

        return true;
    }

    Transform ResolveTargetRoot(Transform source)
    {
        if (source == null)
            return null;

        IUltimateTarget target = source.GetComponentInParent<IUltimateTarget>();
        if (target is Component targetComponent)
            return targetComponent.transform.root != null ? targetComponent.transform.root : targetComponent.transform;

        BossController boss = source.GetComponentInParent<BossController>();
        if (boss != null)
            return boss.transform;

        Transform root = source.root != null ? source.root : source;
        return ResolveUltimateTarget(root) != null ? root : null;
    }

    static IUltimateTarget ResolveUltimateTarget(Transform root)
    {
        if (root == null)
            return null;

        if (root.TryGetComponent<IUltimateTarget>(out var direct))
            return direct;

        IUltimateTarget parent = root.GetComponentInParent<IUltimateTarget>();
        if (parent != null)
            return parent;

        return root.GetComponentInChildren<IUltimateTarget>(true);
    }

    Vector3 ResolveAimPoint(Transform root)
    {
        if (root != null && CombatTargetBoundsUtility.TryGetCombinedBounds(root, out Bounds bounds))
            return bounds.center;

        return root != null ? root.position + Vector3.up * targetAimHeightFallback : transform.position + transform.forward * 2f;
    }

    static int ResolvePriority(Transform root)
    {
        if (root == null)
            return 0;

        if (root.GetComponentInChildren<BossController>(true) != null ||
            root.GetComponentInChildren<BossHealth>(true) != null ||
            string.Equals(root.tag, "Boss", System.StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(root.tag, "Elite", System.StringComparison.OrdinalIgnoreCase))
            return 1;

        return 0;
    }

    void ClearUniqueRoots()
    {
        for (int i = 0; i < _uniqueRoots.Length; i++)
            _uniqueRoots[i] = null;
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || !_hasLastFrame)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(_lastFrame.TargetCenter, 0.25f);
        Gizmos.DrawLine(_lastFrame.TargetCenter, _lastFrame.TargetCenter + _lastFrame.Forward * 2f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(_lastFrame.TargetCenter, _lastFrame.TargetCenter + _lastFrame.Right * 1.25f);
    }
}
