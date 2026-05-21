using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLockOn : MonoBehaviour, ILockOnController
{
    const int OverlapBufferSize = 64;
    const int UniqueRootBufferSize = 64;

    [Header("Search")]
    [SerializeField, Min(0f)] private float lockOnRange = 18f;
    [SerializeField, Range(10f, 180f)] private float lockOnFOV = 70f;
    [SerializeField] private LayerMask obstacleMask = 0;
    [SerializeField] private LayerMask enemyMask = 1 << 9;
    [SerializeField] private LayerMask additionalTargetMask = 0;
    [SerializeField] private bool includeDamageReceiverTargets = true;

    [Header("Pivot")]
    [SerializeField] private string pivotName = "LockPivot";
    [SerializeField] private float defaultPivotY = 1.3f;
    [SerializeField, Range(0f, 1f)] private float autoPivotBottomToCenterRatio = 0.78f;
    [SerializeField] private float autoPivotVerticalOffset = 0f;

    [Header("Input")]
    [SerializeField] private bool useLegacyInput = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private KeyCode toggleKeyAlt = KeyCode.Mouse2;
    [SerializeField, Range(10f, 180f)] private float targetSwitchFOV = 160f;
    [SerializeField, Min(0.05f)] private float holdToUnlockDuration = 0.35f;

    [Header("Camera")]
    [SerializeField] private LockOnCameraManager cameraMgr;

    [Header("Target Indicator")]
    [SerializeField] private bool showTargetIndicator = true;
    [SerializeField] private Color targetIndicatorColor = new Color(1f, 0.04f, 0.02f, 0.95f);
    [SerializeField, Min(8f)] private float targetIndicatorSize = 24f;
    [SerializeField] private Vector3 targetIndicatorOffset = new Vector3(0f, 0.55f, 0f);

    [Header("Retention")]
    [SerializeField] private bool autoUnlockWhenTargetDisabled = true;
    [SerializeField, Min(0f)] private float targetLostGraceTime = 0.22f;
    [SerializeField] private bool autoRetargetOnLost = true;
    [SerializeField, Min(0f)] private float maintainRangeMultiplier = 1.25f;
    [SerializeField, Range(0f, 180f)] private float autoRetargetFOV = 100f;
    [SerializeField, Min(0f)] private float stickyTargetBias = 12f;
    [SerializeField, Min(1f / 120f)] private float maintainCheckInterval = 1f / 30f;
    [SerializeField, Min(0f)] private float maintainCheckCameraMoveThreshold = 0.06f;
    [SerializeField, Min(0f)] private float maintainCheckTargetMoveThreshold = 0.08f;

    public Transform CurrentTarget { get; private set; }
    public bool IsLocked => CurrentTarget != null;
    public bool IsLockOn => IsLocked;
    public bool HasTarget => CurrentTarget != null;
    public Transform Target => CurrentTarget;
    public Vector3 TargetPosition => CurrentTarget ? CurrentTarget.position : transform.position;

    public Vector3 DirectionFrom(Vector3 origin)
    {
        Vector3 delta = TargetPosition - origin;
        return delta.sqrMagnitude > 0.0001f ? delta.normalized : transform.forward;
    }

    Transform _playerPivot;
    PlayerReferences _playerReferences;
    Transform _cam;
    LockOnFacingDriver _facingDriver;
    Transform _currentTargetRoot;
    bool _lockModeActive;
    bool _timelineOwnsCamera;
    float _targetInvalidSince = float.NegativeInfinity;
    float _nextMaintainCheckAt;
    Vector3 _lastMaintainCheckCameraPos;
    Vector3 _lastMaintainCheckTargetPos;
    bool _cachedMaintainable = true;
    bool _hasMaintainCheckSample;
    LockOnTargetIndicator _targetIndicator;
    float _lockInputPressedAt;
    bool _lockInputTracking;
    bool _lockInputHoldConsumed;

    readonly Collider[] _overlapHits = new Collider[OverlapBufferSize];
    readonly Transform[] _uniqueRoots = new Transform[UniqueRootBufferSize];
    readonly System.Collections.Generic.Dictionary<int, bool> _validTargetCache = new System.Collections.Generic.Dictionary<int, bool>(64);
    readonly System.Collections.Generic.Dictionary<int, Renderer> _rootRendererCache = new System.Collections.Generic.Dictionary<int, Renderer>(64);
    readonly System.Collections.Generic.Dictionary<int, Transform> _pivotCache = new System.Collections.Generic.Dictionary<int, Transform>(64);
    readonly System.Collections.Generic.Dictionary<int, LockPivotFollower> _pivotFollowerCache = new System.Collections.Generic.Dictionary<int, LockPivotFollower>(32);

    void Awake()
    {
        _playerReferences = GetComponent<PlayerReferences>();
        _playerPivot = _playerReferences != null && _playerReferences.LockPivot
            ? _playerReferences.LockPivot
            : EnsurePivot(transform, pivotName, defaultPivotY, false);

        EnsureCameraManagerResolved();

        _facingDriver = GetComponent<LockOnFacingDriver>();
        if (_facingDriver == null)
            _facingDriver = gameObject.AddComponent<LockOnFacingDriver>();

        PlayerMoveController movement = GetComponent<PlayerMoveController>();
        Transform facingRoot = movement != null && movement.FacingRoot != null
            ? movement.FacingRoot
            : _playerReferences != null ? _playerReferences.VisualRoot : transform;
        _facingDriver.ConfigureRuntime(this, facingRoot);

        _cam = GameplaySceneCache.ResolveMainCameraTransform();
    }

    void Start()
    {
        if (!_cam)
            _cam = GameplaySceneCache.ResolveMainCameraTransform();

        EnsureCameraManagerResolved();
    }

    void OnDestroy()
    {
        if (_targetIndicator != null)
            Destroy(_targetIndicator.gameObject);
    }

    void Update()
    {
        if (!_cam)
            _cam = GameplaySceneCache.ResolveMainCameraTransform();

        if (_lockModeActive)
            TickLockedTarget();

        if (useLegacyInput)
            HandleLockInput();
    }

    public void LockTo(Transform enemyRoot)
    {
        EnsureCameraManagerResolved();
        if (!enemyRoot)
        {
            Unlock();
            return;
        }

        Transform root = enemyRoot.root != null ? enemyRoot.root : enemyRoot;
        Transform pivot = EnsurePivot(root, pivotName, defaultPivotY, true);
        if (!pivot)
        {
            Unlock();
            return;
        }

        _currentTargetRoot = root;
        CurrentTarget = pivot;
        _lockModeActive = true;
        _targetInvalidSince = float.NegativeInfinity;
        _hasMaintainCheckSample = false;
        _cachedMaintainable = true;
        _facingDriver?.RefreshTickState();
        _facingDriver?.FaceCurrentTargetImmediate();

        if (!_timelineOwnsCamera)
            RefreshCameraTarget();

        RefreshTargetIndicator();
    }

    public void Unlock()
    {
        EnsureCameraManagerResolved();
        _lockModeActive = false;
        _timelineOwnsCamera = false;
        _targetInvalidSince = float.NegativeInfinity;
        _currentTargetRoot = null;
        CurrentTarget = null;
        _hasMaintainCheckSample = false;
        _cachedMaintainable = true;
        _facingDriver?.RefreshTickState();
        cameraMgr?.EndLockOn();
        RefreshTargetIndicator();
    }

    public bool TryAutoLock()
    {
        Transform best = FindBestTarget(null, true, lockOnFOV);
        if (!best)
            return false;

        LockTo(best);
        return true;
    }

    public Transform GetCurrentTarget() => CurrentTarget;

    public bool IsLockedOn() => IsLocked;

    public string BuildDebugSummary()
    {
        string targetName = CurrentTarget != null ? CurrentTarget.name : "<null>";
        string rootName = _currentTargetRoot != null ? _currentTargetRoot.name : "<null>";
        return $"lockMode={_lockModeActive} timelineOwnsCamera={_timelineOwnsCamera} target={targetName} root={rootName} cachedMaintainable={_cachedMaintainable}";
    }

    public void RestoreFreeLookAfterTimeline()
    {
        EnsureCameraManagerResolved();
        _timelineOwnsCamera = false;
        _lockModeActive = false;
        _targetInvalidSince = float.NegativeInfinity;
        _currentTargetRoot = null;
        CurrentTarget = null;
        _hasMaintainCheckSample = false;
        _cachedMaintainable = true;
        _facingDriver?.RefreshTickState();
        RefreshTargetIndicator();

        if (cameraMgr == null)
            return;

        cameraMgr.EndLockOn(false);
        cameraMgr.ForceRestoreGameplayFreeLook();
    }

    public void RestoreLockOnAfterTimeline(Transform targetOverride = null)
    {
        EnsureCameraManagerResolved();
        _timelineOwnsCamera = false;
        _targetInvalidSince = float.NegativeInfinity;
        _hasMaintainCheckSample = false;
        _cachedMaintainable = true;
        _currentTargetRoot = null;
        CurrentTarget = null;
        _lockModeActive = false;

        if (cameraMgr != null)
            cameraMgr.EndLockOn(false);

        if (targetOverride != null)
        {
            Transform root = targetOverride.root != null ? targetOverride.root : targetOverride;
            Transform pivot = EnsurePivot(root, pivotName, defaultPivotY, true);
            if (pivot != null)
            {
                _currentTargetRoot = root;
                CurrentTarget = pivot;
                _lockModeActive = true;
                _facingDriver?.RefreshTickState();
                _facingDriver?.FaceCurrentTargetImmediate();
            }
        }

        _facingDriver?.RefreshTickState();
        RefreshTargetIndicator();

        if (cameraMgr == null)
            return;

        if (_lockModeActive && CurrentTarget && CurrentTarget.gameObject.activeInHierarchy)
        {
            RefreshCameraTarget();
            return;
        }

        cameraMgr.EndLockOn(false);
    }

    bool TrySwitchTarget()
    {
        Transform nextRoot = FindNearestSwitchTarget(_currentTargetRoot);
        if (!nextRoot)
            nextRoot = FindBestTarget(_currentTargetRoot, false, targetSwitchFOV);

        if (!nextRoot || nextRoot == _currentTargetRoot)
            return false;

        LockTo(nextRoot);
        return true;
    }

    void HandleLockInput()
    {
        if (IsLockInputDown())
        {
            _lockInputTracking = true;
            _lockInputHoldConsumed = false;
            _lockInputPressedAt = Time.unscaledTime;
        }

        if (_lockInputTracking && !_lockInputHoldConsumed && IsLockInputHeld())
        {
            if (Time.unscaledTime - _lockInputPressedAt >= holdToUnlockDuration)
            {
                _lockInputHoldConsumed = true;
                if (IsLocked)
                    Unlock();
            }
        }

        if (!_lockInputTracking)
            return;

        if (IsLockInputUp())
        {
            float heldDuration = Time.unscaledTime - _lockInputPressedAt;
            if (!_lockInputHoldConsumed && heldDuration >= holdToUnlockDuration)
            {
                _lockInputHoldConsumed = true;
                if (IsLocked)
                    Unlock();
            }

            bool wasTap = !_lockInputHoldConsumed && heldDuration < holdToUnlockDuration;
            _lockInputTracking = false;

            if (wasTap)
                HandleLockTap();
        }
        else if (!IsLockInputHeld())
        {
            _lockInputTracking = false;
        }
    }

    void HandleLockTap()
    {
        if (IsLocked)
        {
            if (!TrySwitchTarget())
                Unlock();
            return;
        }

        Transform enemyRoot = FindBestTarget(null, true, lockOnFOV);
        if (enemyRoot)
            LockTo(enemyRoot);
    }

    bool IsLockInputDown()
    {
        return Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt);
    }

    bool IsLockInputHeld()
    {
        return Input.GetKey(toggleKey) || Input.GetKey(toggleKeyAlt);
    }

    bool IsLockInputUp()
    {
        return Input.GetKeyUp(toggleKey) || Input.GetKeyUp(toggleKeyAlt);
    }

    public void GiveCameraControlToTimeline(bool give)
    {
        EnsureCameraManagerResolved();
        _timelineOwnsCamera = give;
        _facingDriver?.RefreshTickState();

        if (cameraMgr == null)
            return;

        if (give)
        {
            cameraMgr.EndLockOn(false);
            cameraMgr.SetFreeLookDriverEnabled(false);
            return;
        }

        if (_lockModeActive && CurrentTarget && CurrentTarget.gameObject.activeInHierarchy)
        {
            RefreshCameraTarget();
            return;
        }

        cameraMgr.EndLockOn(false);
        cameraMgr.ForceRestoreGameplayFreeLook();
    }

    void TickLockedTarget()
    {
        if (!CurrentTarget || !_currentTargetRoot || !_currentTargetRoot.gameObject.activeInHierarchy)
        {
            HandleLostTarget();
            return;
        }

        if (!autoUnlockWhenTargetDisabled)
        {
            _targetInvalidSince = float.NegativeInfinity;
            return;
        }

        if (IsCurrentTargetMaintainable())
        {
            _targetInvalidSince = float.NegativeInfinity;
            return;
        }

        HandleLostTarget();
    }

    void HandleLostTarget()
    {
        if (float.IsNegativeInfinity(_targetInvalidSince))
            _targetInvalidSince = Time.time;

        if (Time.time - _targetInvalidSince < targetLostGraceTime)
            return;

        if (autoRetargetOnLost)
        {
            Transform replacement = FindNearestSwitchTarget(_currentTargetRoot);
            if (!replacement)
                replacement = FindBestTarget(_currentTargetRoot, false, autoRetargetFOV);

            if (replacement)
            {
                LockTo(replacement);
                return;
            }
        }

        Unlock();
    }

    bool IsCurrentTargetMaintainable()
    {
        if (!_cam || !CurrentTarget)
            return true;

        Vector3 targetPos = CurrentTarget.position;
        Vector3 toTarget = targetPos - _cam.position;
        float distance = toTarget.magnitude;
        float maxMaintainDistance = Mathf.Max(lockOnRange + 2f, lockOnRange * Mathf.Max(1f, maintainRangeMultiplier));
        if (distance > maxMaintainDistance)
            return false;

        if (obstacleMask.value == 0)
            return true;

        Vector3 cameraPosition = _cam.position;
        if (!ShouldRefreshMaintainability(cameraPosition, targetPos))
            return _cachedMaintainable;

        _cachedMaintainable = !Physics.Linecast(cameraPosition, targetPos, obstacleMask, QueryTriggerInteraction.Ignore);
        _lastMaintainCheckCameraPos = cameraPosition;
        _lastMaintainCheckTargetPos = targetPos;
        _nextMaintainCheckAt = Time.unscaledTime + Mathf.Max(1f / 120f, maintainCheckInterval);
        _hasMaintainCheckSample = true;

        return _cachedMaintainable;
    }

    bool ShouldRefreshMaintainability(Vector3 cameraPosition, Vector3 targetPosition)
    {
        if (!_hasMaintainCheckSample)
            return true;

        float cameraThreshold = Mathf.Max(0f, maintainCheckCameraMoveThreshold);
        if ((cameraPosition - _lastMaintainCheckCameraPos).sqrMagnitude >= cameraThreshold * cameraThreshold)
            return true;

        float targetThreshold = Mathf.Max(0f, maintainCheckTargetMoveThreshold);
        if ((targetPosition - _lastMaintainCheckTargetPos).sqrMagnitude >= targetThreshold * targetThreshold)
            return true;

        return Time.unscaledTime >= _nextMaintainCheckAt;
    }

    void RefreshCameraTarget()
    {
        EnsureCameraManagerResolved();
        if (cameraMgr == null || CurrentTarget == null)
            return;

        cameraMgr.RefreshLockOnTarget(CurrentTarget);
        cameraMgr.StartLockOn(CurrentTarget);
    }

    void RefreshTargetIndicator()
    {
        if (!showTargetIndicator || CurrentTarget == null)
        {
            if (_targetIndicator != null)
                _targetIndicator.SetTarget(null);
            return;
        }

        EnsureTargetIndicator();
        if (_targetIndicator == null)
            return;

        _targetIndicator.Configure(
            GameplaySceneCache.ResolveMainCamera(),
            GameplaySceneCache.ResolveCanvas(),
            targetIndicatorColor,
            targetIndicatorSize,
            targetIndicatorOffset);
        _targetIndicator.SetTarget(CurrentTarget);
    }

    void EnsureTargetIndicator()
    {
        if (_targetIndicator != null)
            return;

        GameObject indicatorObject = new GameObject("RuntimeLockOnTargetIndicator", typeof(RectTransform));
        _targetIndicator = indicatorObject.AddComponent<LockOnTargetIndicator>();
    }

    void EnsureCameraManagerResolved()
    {
        if (!cameraMgr)
            cameraMgr = GameplaySceneCache.ResolveLockOnCameraManager();

        cameraMgr?.SetPlayerPivot(_playerPivot);
    }

    Transform FindBestTarget(Transform excludedRoot, bool applyStickyBias, float searchFov)
    {
        if (!_cam)
        {
            _cam = GameplaySceneCache.ResolveMainCameraTransform();
            if (_cam == null)
                return null;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(
            _cam.position,
            lockOnRange,
            _overlapHits,
            ResolveTargetSearchMask(),
            QueryTriggerInteraction.Collide);

        float bestScore = float.MaxValue;
        Transform bestRoot = null;
        int uniqueCount = 0;
        float halfFov = Mathf.Clamp(searchFov, 1f, 180f) * 0.5f;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapHits[i];
            if (col == null)
                continue;

            Transform root = ResolveLockOnTargetRoot(col);
            if (!root || root == excludedRoot || !root.gameObject.activeInHierarchy)
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

            Vector3 aimPoint = GetLockAimPoint(root);
            Vector3 to = aimPoint - _cam.position;
            float dist = to.magnitude;
            if (dist > lockOnRange || dist <= 0.0001f)
                continue;

            float ang = Vector3.Angle(_cam.forward, to / dist);
            if (ang > halfFov)
                continue;

            if (obstacleMask.value != 0 &&
                Physics.Linecast(_cam.position, aimPoint, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            float score = ang * 2f + dist;
            if (applyStickyBias && root == _currentTargetRoot)
                score -= stickyTargetBias;

            if (score < bestScore)
            {
                bestScore = score;
                bestRoot = root;
            }
        }

        return bestRoot;
    }

    Transform FindNearestSwitchTarget(Transform excludedRoot)
    {
        Vector3 origin = _playerPivot != null ? _playerPivot.position : transform.position;
        int hitCount = Physics.OverlapSphereNonAlloc(
            origin,
            lockOnRange,
            _overlapHits,
            ResolveTargetSearchMask(),
            QueryTriggerInteraction.Collide);

        float bestSqrDistance = float.MaxValue;
        Transform bestRoot = null;
        int uniqueCount = 0;
        float maxSqrDistance = lockOnRange * lockOnRange;
        Transform cameraTransform = _cam != null ? _cam : GameplaySceneCache.ResolveMainCameraTransform();

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapHits[i];
            if (col == null)
                continue;

            Transform root = ResolveLockOnTargetRoot(col);
            if (!root || root == excludedRoot || !root.gameObject.activeInHierarchy)
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

            Vector3 aimPoint = GetLockAimPoint(root);
            float sqrDistance = (aimPoint - origin).sqrMagnitude;
            if (sqrDistance > maxSqrDistance)
                continue;

            if (obstacleMask.value != 0)
            {
                Vector3 lineStart = cameraTransform != null ? cameraTransform.position : origin;
                if (Physics.Linecast(lineStart, aimPoint, obstacleMask, QueryTriggerInteraction.Ignore))
                    continue;
            }

            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                bestRoot = root;
            }
        }

        return bestRoot;
    }

    LayerMask ResolveTargetSearchMask()
    {
        LayerMask searchMask = enemyMask;
        if (additionalTargetMask.value != 0 && additionalTargetMask.value != ~0)
            searchMask |= additionalTargetMask;

        if (searchMask.value == 0 && includeDamageReceiverTargets)
            searchMask = ~0;

        return searchMask;
    }

    Transform ResolveLockOnTargetRoot(Collider col)
    {
        if (!col)
            return null;

        Transform candidate = col.transform;
        Transform playerRoot = transform.root;
        while (candidate != null)
        {
            if (candidate == playerRoot)
                return null;

            if (IsValidLockOnTarget(candidate))
                return candidate;

            candidate = candidate.parent;
        }

        return null;
    }

    bool IsValidLockOnTarget(Transform candidate)
    {
        if (!candidate || !candidate.gameObject.activeInHierarchy)
            return false;

        int key = candidate.GetInstanceID();
        if (_validTargetCache.TryGetValue(key, out bool cachedResult))
            return cachedResult;

        bool isValid;
        if (candidate.CompareTag("Enemy"))
        {
            isValid = true;
        }
        else if (ResolveExistingPivot(candidate) != null)
        {
            isValid = true;
        }
        else if (!includeDamageReceiverTargets)
        {
            isValid = false;
        }
        else
        {
            isValid = candidate.GetComponent<IDamageReceiver>() != null
                || candidate.GetComponent<IHealth>() != null;
        }

        _validTargetCache[key] = isValid;
        return isValid;
    }

    Vector3 GetLockAimPoint(Transform root)
    {
        if (!root)
            return transform.position;

        Transform existingPivot = ResolveExistingPivot(root);
        if (existingPivot && ShouldUseExistingPivot(root, existingPivot))
            return existingPivot.position;

        Renderer rend = ResolveRootRenderer(root);
        if (rend)
            return GetAutoLockAimPoint(rend);

        return root.position + Vector3.up * defaultPivotY;
    }

    Transform EnsurePivot(Transform root, string name, float fallbackY, bool allowAutoCalibrateExisting)
    {
        if (!root)
            return null;

        Transform existing = ResolveExistingPivot(root);
        if (existing)
        {
            if (allowAutoCalibrateExisting)
                AutoCalibrateExistingPivot(root, existing, fallbackY);
            return existing;
        }

        Renderer rend = ResolveRootRenderer(root);
        Vector3 worldPos = rend ? GetAutoLockAimPoint(rend) : root.position + Vector3.up * fallbackY;
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.identity;

        if (rend)
        {
            LockPivotFollower follower = go.AddComponent<LockPivotFollower>();
            follower.sourceRenderer = rend;
            follower.yOffset = go.transform.position.y - rend.bounds.center.y;
            _pivotFollowerCache[go.transform.GetInstanceID()] = follower;
        }

        CacheRootPivot(root, go.transform);
        return go.transform;
    }

    Vector3 GetAutoLockAimPoint(Renderer rend)
    {
        float by = rend.bounds.min.y;
        float cy = rend.bounds.center.y;
        float y = Mathf.Lerp(by, cy, autoPivotBottomToCenterRatio) + autoPivotVerticalOffset;
        return new Vector3(rend.bounds.center.x, y, rend.bounds.center.z);
    }

    bool ShouldUseExistingPivot(Transform root, Transform existingPivot)
    {
        if (!existingPivot)
            return false;

        if (ResolvePivotFollower(existingPivot) != null)
            return true;

        Renderer rend = ResolveRootRenderer(root);
        if (!rend)
            return true;

        Vector3 autoPoint = GetAutoLockAimPoint(rend);
        return Mathf.Abs(existingPivot.position.y - autoPoint.y) <= 0.25f;
    }

    void AutoCalibrateExistingPivot(Transform root, Transform existingPivot, float fallbackY)
    {
        if (!existingPivot || root == transform.root)
            return;

        Renderer rend = ResolveRootRenderer(root);
        if (!rend)
            return;

        Vector3 autoPoint = GetAutoLockAimPoint(rend);
        if (Mathf.Abs(existingPivot.position.y - autoPoint.y) <= 0.25f)
            return;

        existingPivot.position = autoPoint;
        existingPivot.rotation = Quaternion.identity;

        LockPivotFollower follower = ResolvePivotFollower(existingPivot);
        if (follower == null)
            follower = existingPivot.gameObject.AddComponent<LockPivotFollower>();

        follower.sourceRenderer = rend;
        follower.yOffset = autoPoint.y - rend.bounds.center.y;
        follower.refreshInterval = 1f / 30f;
        _pivotFollowerCache[existingPivot.GetInstanceID()] = follower;
    }

    Transform ResolveExistingPivot(Transform root)
    {
        if (!root)
            return null;

        int key = root.GetInstanceID();
        if (_pivotCache.TryGetValue(key, out Transform cachedPivot))
        {
            if (cachedPivot != null)
                return cachedPivot;

            _pivotCache.Remove(key);
        }

        Transform pivot = root.Find(pivotName);
        if (pivot != null)
            _pivotCache[key] = pivot;

        return pivot;
    }

    void CacheRootPivot(Transform root, Transform pivot)
    {
        if (!root)
            return;

        _pivotCache[root.GetInstanceID()] = pivot;
    }

    Renderer ResolveRootRenderer(Transform root)
    {
        if (!root)
            return null;

        int key = root.GetInstanceID();
        if (_rootRendererCache.TryGetValue(key, out Renderer cachedRenderer))
        {
            if (cachedRenderer != null)
                return cachedRenderer;

            _rootRendererCache.Remove(key);
        }

        Renderer renderer = root.GetComponentInChildren<Renderer>();
        if (renderer != null)
            _rootRendererCache[key] = renderer;

        return renderer;
    }

    LockPivotFollower ResolvePivotFollower(Transform pivot)
    {
        if (!pivot)
            return null;

        int key = pivot.GetInstanceID();
        if (_pivotFollowerCache.TryGetValue(key, out LockPivotFollower cachedFollower))
        {
            if (cachedFollower != null)
                return cachedFollower;

            _pivotFollowerCache.Remove(key);
        }

        LockPivotFollower follower = pivot.GetComponent<LockPivotFollower>();
        if (follower != null)
            _pivotFollowerCache[key] = follower;

        return follower;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Transform cam = Camera.main ? Camera.main.transform : null;
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        if (cam)
            Gizmos.DrawWireSphere(cam.position, lockOnRange);

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
