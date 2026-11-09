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
    [SerializeField] private Color targetIndicatorColor = new Color(0.86f, 0.97f, 1f, 0.92f);
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

        SoulsCameraProfile souls = SoulsCameraRig.ActiveProfile;
        if (_lockModeActive)
        {
            if (souls != null)
                TickLockedTargetSouls(souls);
            else
                TickLockedTarget();
        }

        if (useLegacyInput)
            HandleLockInput();

        if (souls != null && _lockModeActive && CurrentTarget != null && !_timelineOwnsCamera)
            HandleSoulsSwitchInput(souls);
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
        _soulsLosLostSince = float.NegativeInfinity;
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
        SoulsCameraProfile souls = SoulsCameraRig.ActiveProfile;
        Transform best = souls != null ? FindBestTargetSouls(souls, null) : FindBestTarget(null, true, lockOnFOV);
        if (!best)
            return false;

        LockTo(best);
        return true;
    }

    public Transform GetCurrentTarget() => CurrentTarget;

    public bool IsLockedOn() => IsLocked;

    public bool IsTimelineOwningCamera => _timelineOwnsCamera;

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
        SoulsCameraProfile souls = SoulsCameraRig.ActiveProfile;
        if (souls != null)
        {
            // 소울류: 락온 중 탭 = 해제, 타깃 없이 탭 = 카메라 리셋(등 뒤로)
            if (IsLocked)
            {
                if (souls.tapUnlocks || !TrySwitchTarget())
                    Unlock();
                return;
            }

            Transform best = FindBestTargetSouls(souls, null);
            if (best)
                LockTo(best);
            else
                SoulsCameraRig.Active?.RequestRecenter();
            return;
        }

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
        return ChuonInput.PressedOr(ChuonAction.LockOn, toggleKey, toggleKeyAlt); // 패드 R3
    }

    bool IsLockInputHeld()
    {
        return ChuonInput.HeldOr(ChuonAction.LockOn, toggleKey, toggleKeyAlt);
    }

    bool IsLockInputUp()
    {
        return ChuonInput.ReleasedOr(ChuonAction.LockOn, toggleKey, toggleKeyAlt);
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

        SoulsCameraProfile souls = SoulsCameraRig.ActiveProfile;
        if (souls != null)
        {
            // 아라사카 톤: 가슴(약점) 위치의 작은 흰 다이아몬드 + 붉은 코어, 전환 시 0.1 s 스냅
            _targetIndicator.ConfigureDiamond(
                GameplaySceneCache.ResolveMainCamera(),
                GameplaySceneCache.ResolveCanvas(),
                souls.markerColor,
                souls.markerAccentColor,
                souls.markerSize,
                souls.markerSnapTime,
                souls.chestHeightRatio);
            _targetIndicator.SetTarget(CurrentTarget);
            return;
        }

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

    // ------------------------------------------------------------------
    // 소울라이크 락온 규칙 (SoulsCameraRig + SoulsCameraProfile 이 있는 씬 전용, Docs/CameraDesign_Soulslike.md §2)
    //  선택: 플레이어 기준 range 안, 카메라 정면 원뿔 안, 가림 없음 → 화면 중앙에 가장 가까운 적 (보스 보너스)
    //  전환: 마우스 이동량 / 오른쪽 스틱 튕김 방향의 다음 적, 쿨다운
    //  해제: 거리 초과, 가림 grace 초과, 사망(근처 적이 있으면 자동 재락온)
    // ------------------------------------------------------------------

    float _soulsLosLostSince = float.NegativeInfinity;
    float _soulsNextLosCheckAt;
    float _soulsNextSwitchAt;
    Vector2 _soulsMouseAccum;
    float _soulsMouseWindowStart;
    bool _soulsStickArmed = true;
    readonly RaycastHit[] _soulsLosHits = new RaycastHit[16];

    public static bool IsRootDead(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy)
            return true;
        BossHealth bossHealth = root.GetComponentInChildren<BossHealth>();
        if (bossHealth != null && bossHealth.IsDead)
            return true;
        EnemyHealth enemyHealth = root.GetComponentInChildren<EnemyHealth>();
        if (enemyHealth != null && enemyHealth.IsDead)
            return true;
        DroneController drone = root.GetComponentInChildren<DroneController>();
        if (drone != null && drone.IsDead)
            return true;
        EnemyBrain brain = root.GetComponentInChildren<EnemyBrain>();
        return brain != null && brain.IsDead;
    }

    Vector3 SoulsOrigin => _playerPivot != null ? _playerPivot.position : transform.position + Vector3.up * defaultPivotY;

    bool SoulsHasLineOfSight(Vector3 from, Vector3 to, Transform targetRoot, LayerMask mask)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.01f)
            return true;

        int n = Physics.RaycastNonAlloc(from, delta / length, _soulsLosHits, length, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = _soulsLosHits[i].collider;
            if (c == null)
                continue;
            Transform r = c.transform.root;
            if (r == targetRoot || r == transform.root || SoulsCameraRig.IsCharacterCollider(c))
                continue;
            return false;
        }
        return true;
    }

    int CollectSoulsCandidates(SoulsCameraProfile p, float range)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(SoulsOrigin, range, _overlapHits, ResolveTargetSearchMask(), QueryTriggerInteraction.Collide);
        int uniqueCount = 0;
        for (int i = 0; i < hitCount; i++)
        {
            Transform root = ResolveLockOnTargetRoot(_overlapHits[i]);
            if (!root || !root.gameObject.activeInHierarchy)
                continue;
            bool duplicate = false;
            for (int j = 0; j < uniqueCount; j++)
                if (_uniqueRoots[j] == root) { duplicate = true; break; }
            if (duplicate || uniqueCount >= _uniqueRoots.Length)
                continue;
            _uniqueRoots[uniqueCount++] = root;
        }
        return uniqueCount;
    }

    Transform FindBestTargetSouls(SoulsCameraProfile p, Transform excludedRoot)
    {
        Camera cam = GameplaySceneCache.ResolveMainCamera();
        if (cam == null)
            return null;

        Transform ct = cam.transform;
        Vector3 origin = SoulsOrigin;
        float halfCone = Mathf.Clamp(p.lockSearchCone, 1f, 360f) * 0.5f;
        int count = CollectSoulsCandidates(p, p.lockSearchRange);
        float bestScore = float.MaxValue;
        Transform best = null;
        for (int i = 0; i < count; i++)
        {
            Transform root = _uniqueRoots[i];
            if (root == excludedRoot || IsRootDead(root))
                continue;
            Vector3 aim = SoulsCameraRig.GetTargetChestPoint(root, p.chestHeightRatio);
            float distance = Vector3.Distance(origin, aim);
            if (distance > p.lockSearchRange)
                continue;
            if (Vector3.Angle(ct.forward, aim - ct.position) > halfCone)
                continue;
            if (!SoulsHasLineOfSight(ct.position, aim, root, p.lineOfSightMask))
                continue;

            Vector3 vp = cam.WorldToViewportPoint(aim);
            float score = new Vector2((vp.x - 0.5f) * cam.aspect, vp.y - 0.5f).magnitude + distance * 0.002f;
            if (root.GetComponentInChildren<BossController>() != null)
                score -= p.bossPriorityBonus;
            if (score < bestScore)
            {
                bestScore = score;
                best = root;
            }
        }
        return best;
    }

    Transform FindNearestTargetSouls(SoulsCameraProfile p, Transform excludedRoot)
    {
        Vector3 origin = SoulsOrigin;
        int count = CollectSoulsCandidates(p, p.lockSearchRange);
        float best = float.MaxValue;
        Transform bestRoot = null;
        for (int i = 0; i < count; i++)
        {
            Transform root = _uniqueRoots[i];
            if (root == excludedRoot || IsRootDead(root))
                continue;
            Vector3 aim = SoulsCameraRig.GetTargetChestPoint(root, p.chestHeightRatio);
            float distance = Vector3.Distance(origin, aim);
            if (distance > p.lockSearchRange || distance >= best)
                continue;
            if (!SoulsHasLineOfSight(origin, aim, root, p.lineOfSightMask))
                continue;
            best = distance;
            bestRoot = root;
        }
        return bestRoot;
    }

    Transform FindSwitchTargetSouls(SoulsCameraProfile p, Vector2 screenDirection)
    {
        Camera cam = GameplaySceneCache.ResolveMainCamera();
        if (cam == null || _currentTargetRoot == null)
            return null;

        Vector3 currentVp = cam.WorldToViewportPoint(SoulsCameraRig.GetTargetChestPoint(_currentTargetRoot, p.chestHeightRatio));
        Vector2 current = currentVp.z > 0f ? new Vector2(currentVp.x * cam.aspect, currentVp.y) : new Vector2(0.5f * cam.aspect, 0.5f);
        int count = CollectSoulsCandidates(p, p.lockSearchRange);
        float bestScore = float.MaxValue;
        Transform best = null;
        for (int i = 0; i < count; i++)
        {
            Transform root = _uniqueRoots[i];
            if (root == _currentTargetRoot || IsRootDead(root))
                continue;
            Vector3 aim = SoulsCameraRig.GetTargetChestPoint(root, p.chestHeightRatio);
            if (Vector3.Distance(SoulsOrigin, aim) > p.lockSearchRange)
                continue;
            Vector3 vp = cam.WorldToViewportPoint(aim);
            if (vp.z <= 0f)
                continue;
            if (!SoulsHasLineOfSight(cam.transform.position, aim, root, p.lineOfSightMask))
                continue;
            Vector2 delta = new Vector2(vp.x * cam.aspect, vp.y) - current;
            float length = delta.magnitude;
            if (length < 0.01f)
                continue;
            float cos = Vector2.Dot(delta / length, screenDirection);
            if (cos < 0.35f)
                continue;
            float score = length * (2f - cos);
            if (score < bestScore)
            {
                bestScore = score;
                best = root;
            }
        }
        return best;
    }

    void HandleSoulsSwitchInput(SoulsCameraProfile p)
    {
        float now = Time.unscaledTime;
        Vector2 flick = Vector2.zero;

        UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = mouse.delta.ReadValue() * (1080f / Mathf.Max(1f, Screen.height));
            if (now - _soulsMouseWindowStart > p.mouseSwitchWindow)
            {
                _soulsMouseAccum = Vector2.zero;
                _soulsMouseWindowStart = now;
            }
            _soulsMouseAccum += delta;
            if (_soulsMouseAccum.magnitude >= p.mouseSwitchThreshold)
            {
                flick = _soulsMouseAccum;
                _soulsMouseAccum = Vector2.zero;
            }
        }

        UnityEngine.InputSystem.Gamepad pad = UnityEngine.InputSystem.Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.rightStick.ReadValue();
            float m = stick.magnitude;
            if (_soulsStickArmed && m >= p.stickFlickThreshold)
            {
                flick = stick;
                _soulsStickArmed = false;
            }
            else if (m <= p.stickResetThreshold)
            {
                _soulsStickArmed = true;
            }
        }

        if (flick.sqrMagnitude < 0.0001f || now < _soulsNextSwitchAt)
            return;

        _soulsNextSwitchAt = now + p.switchCooldown;
        Transform next = FindSwitchTargetSouls(p, flick.normalized);
        if (next)
            LockTo(next);
    }

    void TickLockedTargetSouls(SoulsCameraProfile p)
    {
        if (!CurrentTarget || !_currentTargetRoot || IsRootDead(_currentTargetRoot))
        {
            Transform replacement = autoRetargetOnLost ? FindNearestTargetSouls(p, _currentTargetRoot) : null;
            if (replacement)
                LockTo(replacement);
            else
                Unlock();
            return;
        }

        Vector3 aim = SoulsCameraRig.GetTargetChestPoint(_currentTargetRoot, p.chestHeightRatio);
        if (Vector3.Distance(SoulsOrigin, aim) > p.lockBreakDistance)
        {
            Unlock();
            return;
        }

        float now = Time.unscaledTime;
        if (now >= _soulsNextLosCheckAt)
        {
            _soulsNextLosCheckAt = now + 0.1f;
            bool visible = SoulsHasLineOfSight(SoulsOrigin, aim, _currentTargetRoot, p.lineOfSightMask)
                || (_cam != null && SoulsHasLineOfSight(_cam.position, aim, _currentTargetRoot, p.lineOfSightMask));
            if (visible)
                _soulsLosLostSince = float.NegativeInfinity;
            else if (float.IsNegativeInfinity(_soulsLosLostSince))
                _soulsLosLostSince = now;
        }

        if (!float.IsNegativeInfinity(_soulsLosLostSince) && now - _soulsLosLostSince > p.lockLineOfSightGrace)
            Unlock();
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
