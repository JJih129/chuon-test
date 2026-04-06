using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateTargetBinder : MonoBehaviour
{
    [Serializable]
    public sealed class BoundTarget
    {
        public Transform TargetRoot;
        public Transform ResolvedTargetTransform;
        public IUltimateTarget UltimateTarget;
        public IUltimateVictimState VictimState;
        public BossBreakController BreakController;
        public IHealth Health;
    }

    [Header("플레이어 참조")]
    [SerializeField] private PlayerReferences playerReferences;
    [SerializeField] private PlayerMoveController moveController;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Animator playerAnimator;

    [Header("이동 보정")]
    [SerializeField] private LayerMask movementCollisionMask = ~0;
    [SerializeField] private float collisionSkin = 0.03f;
    [SerializeField] private float fallbackTargetAimHeight = 1.1f;
    [SerializeField] private bool useCollisionAwareMove = true;
    [SerializeField] private bool debugLog;

    readonly RaycastHit[] _movementHits = new RaycastHit[8];

    PlayerUltimateController _owner;
    UltimateSequenceData _data;
    BoundTarget _activeTarget;
    Vector3 _cachedVictimAnchorPosition;
    bool _sequenceActive;
    bool _victimReleasedForFinalImpact;
    Transform _cachedAimTargetRoot;
    Renderer _cachedAimTargetRenderer;
    bool _hasCachedAimTargetRenderer;
    float _cachedAimBottomToCenterRatio = -1f;
    float _cachedAimVerticalOffset;
    Vector3 _cachedAimTargetRootPosition;
    Vector3 _cachedAimPoint;
    bool _hasCachedAimPoint;
    Transform _cachedPlayerRoot;
    Transform _cachedPlayerVfxRoot;
    Animator _cachedPlayerAnimator;
    Animator _cachedPlayerPresentationAnimator;
    Transform _presentationPlayerRootOverride;
    Transform _presentationVictimRootOverride;
    Quaternion _cachedControllerRotation = Quaternion.identity;
    Vector3 _cachedControllerLossyScale = Vector3.one;
    Vector3 _cachedControllerCenter = Vector3.zero;
    float _cachedControllerRadius = -1f;
    float _cachedControllerHeight = -1f;
    Vector3 _cachedControllerCenterOffset;
    Vector3 _cachedControllerUpAxis = Vector3.up;
    float _cachedControllerWorldRadius = 0.05f;
    float _cachedControllerHalfHeight;

    public BoundTarget ActiveTarget => _activeTarget;
    public bool IsSequenceActive => _sequenceActive;
    public bool HasActiveTarget => _activeTarget != null && _activeTarget.TargetRoot != null;
    public bool IsTargetAlive => _activeTarget != null && _activeTarget.Health != null && !_activeTarget.Health.IsDead;
    public Transform PlayerRoot => _cachedPlayerRoot != null ? _cachedPlayerRoot : transform;
    public Transform PlayerVfxRoot => _cachedPlayerVfxRoot != null ? _cachedPlayerVfxRoot : PlayerRoot;
    public Animator PlayerAnimator => _cachedPlayerAnimator;
    public Animator PlayerPresentationAnimator
    {
        get
        {
            if (_owner != null && _owner.PlayerPresentationClone != null && _owner.PlayerPresentationClone.CloneAnimator != null)
                return _owner.PlayerPresentationClone.CloneAnimator;

            return _cachedPlayerPresentationAnimator != null ? _cachedPlayerPresentationAnimator : _cachedPlayerAnimator;
        }
    }
    public UltimatePresentationClone PlayerPresentationClone => _owner != null ? _owner.PlayerPresentationClone : null;

    Transform ActivePlayerAnchorRoot => _presentationPlayerRootOverride != null ? _presentationPlayerRootOverride : PlayerRoot;
    Transform ActiveVictimAnchorRoot => _presentationVictimRootOverride != null
        ? _presentationVictimRootOverride
        : _activeTarget != null
            ? _activeTarget.TargetRoot
            : null;

    void Awake()
    {
        ResolveReferences();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ResolveReferences();
    }
#endif

    public bool TryBind(PlayerUltimateController owner, UltimateSequenceData data, out BoundTarget boundTarget, out string failureReason)
    {
        ResolveReferences();

        boundTarget = null;
        failureReason = string.Empty;

        if (owner == null)
        {
            failureReason = "Ultimate owner is missing.";
            return false;
        }

        if (!owner.TryGetUltimateTarget(out IUltimateTarget target, out Transform targetTransform) || target == null || targetTransform == null)
        {
            failureReason = "No valid ultimate target.";
            return false;
        }

        Transform targetRoot = targetTransform.root != null ? targetTransform.root : targetTransform;
        Transform playerRoot = PlayerRoot;
        Vector3 targetAimPoint = GetTargetAimPoint(targetRoot, 0.8f, 0f);
        Vector3 flatToTarget = targetAimPoint - playerRoot.position;
        flatToTarget.y = 0f;
        float distance = flatToTarget.magnitude;
        if (distance > data.Activation.maxDistance)
        {
            failureReason = "Target is out of range.";
            return false;
        }

        if (data.Activation.maxAngle < 179.5f)
        {
            Vector3 forward = playerRoot.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                float angle = Vector3.Angle(forward.normalized, flatToTarget.sqrMagnitude > 0.0001f ? flatToTarget.normalized : forward.normalized);
                if (angle > data.Activation.maxAngle)
                {
                    failureReason = "Target angle is invalid.";
                    return false;
                }
            }
        }

        boundTarget = new BoundTarget
        {
            TargetRoot = targetRoot,
            ResolvedTargetTransform = targetTransform,
            UltimateTarget = target,
            VictimState = owner.GetUltimateVictimState(targetTransform),
            BreakController = owner.GetUltimateBreakController(targetTransform),
            Health = ResolveHealth(targetTransform)
        };
        return true;
    }

    public void BeginSequence(PlayerUltimateController owner, UltimateSequenceData data, BoundTarget boundTarget)
    {
        ResolveReferences();

        _owner = owner;
        _data = data;
        _activeTarget = boundTarget;
        _sequenceActive = true;
        _victimReleasedForFinalImpact = false;
        _cachedVictimAnchorPosition = boundTarget != null && boundTarget.TargetRoot != null
            ? boundTarget.TargetRoot.position
            : PlayerRoot.position + PlayerRoot.forward * 2f;
        InvalidateTargetAimCache();

        moveController?.SetExternalControl(true);

        if (_activeTarget != null && _activeTarget.VictimState != null && data.Activation.keepTargetRootedDuringSequence)
        {
            _activeTarget.VictimState.BeginUltimateVictimState(PlayerRoot, data.EstimateTotalDuration());
            _activeTarget.VictimState.SetUltimateVictimAnchor(_cachedVictimAnchorPosition, PlayerRoot.position);
        }
    }

    public void EndSequence()
    {
        if (!_victimReleasedForFinalImpact && _activeTarget != null && IsVictimStateAlive(_activeTarget.VictimState))
            _activeTarget.VictimState.EndUltimateVictimState();

        moveController?.SetExternalControl(false);
        ClearPresentationAnchorOverrides();

        _owner = null;
        _data = null;
        _activeTarget = null;
        _sequenceActive = false;
        _victimReleasedForFinalImpact = false;
        InvalidateTargetAimCache();
    }

    public void RefreshVictimAnchor(Vector3 lookTarget)
    {
        if (!_sequenceActive || _victimReleasedForFinalImpact || _activeTarget == null || _activeTarget.VictimState == null || _data == null || !_data.Activation.keepTargetRootedDuringSequence)
            return;

        _activeTarget.VictimState.SetUltimateVictimAnchor(_cachedVictimAnchorPosition, lookTarget);
    }

    public void ReleaseVictimStateForFinalImpact()
    {
        if (_victimReleasedForFinalImpact || _activeTarget == null || !IsVictimStateAlive(_activeTarget.VictimState))
            return;

        _activeTarget.VictimState.EndUltimateVictimState();
        _victimReleasedForFinalImpact = true;
    }

    static bool IsVictimStateAlive(IUltimateVictimState victimState)
    {
        if (victimState == null)
            return false;

        Behaviour victimBehaviour = victimState as Behaviour;
        if (victimBehaviour == null)
            return false;

        if (!victimBehaviour)
            return false;

        return true;
    }

    public void SetPresentationAnchorOverrides(Transform playerRootOverride, Transform victimRootOverride)
    {
        _presentationPlayerRootOverride = playerRootOverride;
        _presentationVictimRootOverride = victimRootOverride;
        InvalidateTargetAimCache();
    }

    public void ClearPresentationAnchorOverrides()
    {
        _presentationPlayerRootOverride = null;
        _presentationVictimRootOverride = null;
        InvalidateTargetAimCache();
    }

    public Vector3 GetPlayerCameraAnchor(Vector3 localOffset)
    {
        Transform root = ActivePlayerAnchorRoot;
        Quaternion yawRotation = Quaternion.Euler(0f, root.eulerAngles.y, 0f);
        return root.position + yawRotation * localOffset;
    }

    public Vector3 GetPlayerSwordFocusPoint(Vector3 localOffset)
    {
        Transform focusTransform = ResolvePlayerSwordFocusTransform();
        if (focusTransform == null)
            return GetPlayerCameraAnchor(localOffset);

        Transform mappedFocus = PlayerPresentationClone != null
            ? PlayerPresentationClone.ResolveMappedTransform(focusTransform)
            : null;
        Transform activeFocus = mappedFocus != null ? mappedFocus : focusTransform;
        Transform activeRoot = ActivePlayerAnchorRoot;
        Quaternion yawRotation = Quaternion.Euler(0f, activeRoot.eulerAngles.y, 0f);
        return activeFocus.position + yawRotation * localOffset;
    }

    public Vector3 GetPlayerIntroSwordCameraPivotPoint(Vector3 localOffset)
    {
        Transform pivot = ResolvePlayerIntroSwordCameraPivotTransform();
        if (pivot != null)
        {
            Transform mappedPivot = PlayerPresentationClone != null
                ? PlayerPresentationClone.ResolveMappedTransform(pivot)
                : null;
            return mappedPivot != null ? mappedPivot.position : pivot.position;
        }

        Transform root = ActivePlayerAnchorRoot;
        if (root == null)
            return GetPlayerSwordFocusPoint(localOffset);

        Vector3 upperBodyAnchor = GetPlayerCameraAnchor(_data != null
            ? _data.CinematicAnimation.introSwordCloseupCamera.playerAnchorLocalOffset
            : Vector3.zero);
        Quaternion yawRotation = Quaternion.Euler(0f, root.eulerAngles.y, 0f);
        return upperBodyAnchor + yawRotation * localOffset;
    }

    public Vector3 GetPlayerIntroSwordLookPoint(Vector3 localOffset)
    {
        Transform lookTransform = ResolvePlayerIntroSwordLookTargetTransform();
        if (lookTransform == null)
            return GetPlayerSwordFocusPoint(localOffset);

        Transform mappedLook = PlayerPresentationClone != null
            ? PlayerPresentationClone.ResolveMappedTransform(lookTransform)
            : null;
        Transform activeLook = mappedLook != null ? mappedLook : lookTransform;
        Transform activeRoot = ActivePlayerAnchorRoot;
        Quaternion yawRotation = Quaternion.Euler(0f, activeRoot.eulerAngles.y, 0f);
        return activeLook.position + yawRotation * localOffset;
    }

    public Transform GetPlayerIntroSwordEffectAnchor()
    {
        Transform lookTransform = ResolvePlayerIntroSwordLookTargetTransform();
        if (lookTransform == null)
            lookTransform = ResolvePlayerSwordFocusTransform();

        if (lookTransform == null)
            return ActivePlayerAnchorRoot;

        Transform mappedLook = PlayerPresentationClone != null
            ? PlayerPresentationClone.ResolveMappedTransform(lookTransform)
            : null;

        return mappedLook != null ? mappedLook : lookTransform;
    }

    public Vector3 GetTargetAimPoint(float bottomToCenterRatio, float verticalOffset)
    {
        return GetTargetAimPoint(ActiveVictimAnchorRoot, bottomToCenterRatio, verticalOffset);
    }

    public Vector3 GetTargetAimPoint(Transform targetRoot, float bottomToCenterRatio, float verticalOffset)
    {
        if (targetRoot == null)
            return PlayerRoot.position + PlayerRoot.forward * 2f + Vector3.up * fallbackTargetAimHeight;

        Vector3 targetRootPosition = targetRoot.position;
        if (_hasCachedAimPoint &&
            _cachedAimTargetRoot == targetRoot &&
            Mathf.Approximately(_cachedAimBottomToCenterRatio, bottomToCenterRatio) &&
            Mathf.Approximately(_cachedAimVerticalOffset, verticalOffset) &&
            (_cachedAimTargetRootPosition - targetRootPosition).sqrMagnitude <= 0.0001f)
        {
            return _cachedAimPoint;
        }

        Renderer renderer = ResolveTargetAimRenderer(targetRoot);
        Vector3 aimPoint;
        if (renderer != null)
        {
            Bounds bounds = renderer.bounds;
            float y = Mathf.Lerp(bounds.min.y, bounds.center.y, Mathf.Clamp01(bottomToCenterRatio)) + verticalOffset;
            aimPoint = new Vector3(bounds.center.x, y, bounds.center.z);
        }
        else
        {
            aimPoint = targetRoot.position + Vector3.up * (fallbackTargetAimHeight + verticalOffset);
        }

        _cachedAimTargetRoot = targetRoot;
        _cachedAimBottomToCenterRatio = bottomToCenterRatio;
        _cachedAimVerticalOffset = verticalOffset;
        _cachedAimTargetRootPosition = targetRootPosition;
        _cachedAimPoint = aimPoint;
        _hasCachedAimPoint = true;
        return aimPoint;
    }

    public Vector3 GetFlattenedDirectionToTarget(Vector3 fallbackForward)
    {
        Vector3 targetAimPoint = GetTargetAimPoint(0.8f, 0f);
        Vector3 direction = targetAimPoint - PlayerRoot.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        fallbackForward.y = 0f;
        if (fallbackForward.sqrMagnitude > 0.0001f)
            return fallbackForward.normalized;

        return Vector3.forward;
    }

    public Vector3 GetIntroPosition(float distance, float sideOffset)
    {
        Vector3 aimPoint = GetTargetAimPoint(0.8f, 0f);
        Vector3 awayDirection = GetFlattenedDirection(PlayerRoot.position - aimPoint, -PlayerRoot.forward);
        Vector3 right = Vector3.Cross(Vector3.up, awayDirection).normalized;
        Vector3 position = aimPoint + awayDirection * Mathf.Max(0.4f, distance) + right * sideOffset;
        position.y = PlayerRoot.position.y;
        return position;
    }

    public Vector3 GetDashDestination(float distance, float sideOffset)
    {
        Vector3 aimPoint = GetTargetAimPoint(0.82f, 0f);
        Vector3 awayDirection = GetFlattenedDirection(PlayerRoot.position - aimPoint, -PlayerRoot.forward);
        Vector3 throughDirection = -awayDirection;
        Vector3 right = Vector3.Cross(Vector3.up, throughDirection).normalized;
        float passThroughDistance = Mathf.Max(0.25f, distance) + GetTargetPassThroughPadding();
        Vector3 position = aimPoint + throughDirection * passThroughDistance + right * sideOffset;
        position.y = PlayerRoot.position.y;
        return position;
    }

    public Vector3 GetSlashPosition(UltimateSequenceData.SlashStepData step)
    {
        Vector3 aimPoint = GetTargetAimPoint(0.82f, 0f);
        Vector3 awayDirection = GetFlattenedDirection(PlayerRoot.position - aimPoint, -PlayerRoot.forward);
        Vector3 orbitDirection = Quaternion.AngleAxis(step.angle, Vector3.up) * awayDirection;
        Vector3 right = Vector3.Cross(Vector3.up, orbitDirection).normalized;
        Vector3 position = aimPoint + orbitDirection * Mathf.Max(0.2f, step.distance) + right * step.sideOffset;
        position.y = PlayerRoot.position.y + step.heightOffset;
        return position;
    }

    public Vector3 GetWalkoutPosition(float distance, float sideOffset)
    {
        Vector3 aimPoint = GetTargetAimPoint(0.8f, 0f);
        Vector3 awayDirection = GetFlattenedDirection(PlayerRoot.position - aimPoint, -PlayerRoot.forward);
        Vector3 right = Vector3.Cross(Vector3.up, awayDirection).normalized;
        Vector3 position = aimPoint + awayDirection * Mathf.Max(0.4f, distance) + right * sideOffset;
        position.y = PlayerRoot.position.y;
        return position;
    }

    public void SnapPlayerTo(Vector3 worldPosition, Vector3 lookTarget)
    {
        MovePlayerTo(worldPosition, lookTarget, true);
    }

    public void MovePlayerLinear(Vector3 startPosition, Vector3 endPosition, Vector3 lookTarget, float normalizedT)
    {
        Vector3 desiredPosition = Vector3.Lerp(startPosition, endPosition, Mathf.Clamp01(normalizedT));
        MovePlayerTo(desiredPosition, lookTarget, false);
    }

    public Vector3 GetWalkoutLookTarget(Vector3 walkoutPosition)
    {
        Vector3 targetAimPoint = GetTargetAimPoint(0.8f, 0f);
        Vector3 direction = (_data != null && _data.Movement.faceAwayOnWalkout)
            ? walkoutPosition + (walkoutPosition - targetAimPoint).normalized * 4f
            : targetAimPoint;
        return direction;
    }

    public void FacePlayerTowardsImmediate(Vector3 lookTarget)
    {
        FacePlayerTowards(lookTarget);
    }

    void MovePlayerTo(Vector3 desiredPosition, Vector3 lookTarget, bool forceSnap)
    {
        Transform root = PlayerRoot;

        if (!forceSnap && characterController != null && characterController.enabled)
        {
            Vector3 delta = desiredPosition - root.position;
            if (useCollisionAwareMove)
                delta = ResolveCollisionAwareDelta(root.position, delta);

            characterController.Move(delta);
        }
        else
        {
            bool hadController = characterController != null && characterController.enabled;
            if (hadController)
                characterController.enabled = false;

            root.position = desiredPosition;

            if (hadController && characterController != null)
                characterController.enabled = true;
        }

        FacePlayerTowards(lookTarget);
    }

    Vector3 ResolveCollisionAwareDelta(Vector3 currentPosition, Vector3 delta)
    {
        if (characterController == null || !characterController.enabled || movementCollisionMask.value == 0 || delta.sqrMagnitude <= 0.000001f)
            return delta;

        float distance = delta.magnitude;
        Vector3 direction = delta / distance;

        GetCharacterControllerCapsuleWorld(currentPosition, out Vector3 point1, out Vector3 point2, out float radius);

        int hitCount = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            radius,
            direction,
            _movementHits,
            distance + collisionSkin,
            movementCollisionMask,
            QueryTriggerInteraction.Ignore);

        float safeDistance = distance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _movementHits[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform.IsChildOf(transform))
                continue;
            if (_activeTarget != null && _activeTarget.TargetRoot != null && hit.collider.transform.IsChildOf(_activeTarget.TargetRoot))
                continue;

            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0f, hit.distance - collisionSkin));
        }

        return direction * safeDistance;
    }

    void GetCharacterControllerCapsuleWorld(Vector3 currentPosition, out Vector3 point1, out Vector3 point2, out float radius)
    {
        RefreshCharacterControllerGeometryCache();

        Vector3 center = currentPosition + _cachedControllerCenterOffset;
        Vector3 axis = _cachedControllerUpAxis;
        radius = _cachedControllerWorldRadius;
        point1 = center + axis * _cachedControllerHalfHeight;
        point2 = center - axis * _cachedControllerHalfHeight;
    }

    void FacePlayerTowards(Vector3 lookTarget)
    {
        Transform root = PlayerRoot;
        Vector3 lookDirection = lookTarget - root.position;
        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude <= 0.0001f)
            return;

        root.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    static Vector3 GetFlattenedDirection(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        fallback.y = 0f;
        if (fallback.sqrMagnitude > 0.0001f)
            return fallback.normalized;

        return Vector3.forward;
    }

    Transform ResolvePlayerSwordFocusTransform()
    {
        if (_owner != null)
        {
            Transform ownerFocus = _owner.GetUltimateSwordFocusTransform();
            if (ownerFocus != null)
                return ownerFocus;
        }

        if (playerReferences != null && playerReferences.UltimateSpawnRoot != null)
            return playerReferences.UltimateSpawnRoot;

        return PlayerRoot;
    }

    Transform ResolvePlayerIntroSwordCameraPivotTransform()
    {
        return _owner != null ? _owner.GetUltimateIntroSwordCameraPivotTransform() : null;
    }

    Transform ResolvePlayerIntroSwordLookTargetTransform()
    {
        return _owner != null ? _owner.GetUltimateIntroSwordLookTargetTransform() : null;
    }

    static IHealth ResolveHealth(Transform targetTransform)
    {
        if (targetTransform == null)
            return null;

        if (targetTransform.TryGetComponent<IHealth>(out var direct))
            return direct;

        IHealth parent = targetTransform.GetComponentInParent<IHealth>();
        if (parent != null)
            return parent;

        if (targetTransform.root != null && targetTransform.root.TryGetComponent<IHealth>(out var rootHealth))
            return rootHealth;

        return null;
    }

    void ResolveReferences()
    {
        if (playerReferences == null)
            playerReferences = GetComponent<PlayerReferences>();
        if (moveController == null)
            moveController = GetComponent<PlayerMoveController>();
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
        Animator preferredAnimator = playerReferences != null ? playerReferences.MainAnimator : null;
        if (preferredAnimator != null)
        {
            bool currentAnimatorInvalid = playerAnimator == null
                || playerAnimator.avatar == null
                || !playerAnimator.gameObject.activeInHierarchy;
            if (currentAnimatorInvalid)
                playerAnimator = preferredAnimator;
        }

        _cachedPlayerRoot = playerReferences != null && playerReferences.PlayerRoot != null
            ? playerReferences.PlayerRoot
            : transform;
        _cachedPlayerVfxRoot = playerReferences != null && playerReferences.UltimateSpawnRoot != null
            ? playerReferences.UltimateSpawnRoot
            : playerReferences != null && playerReferences.VFXRoot != null
                ? playerReferences.VFXRoot
                : _cachedPlayerRoot;
        _cachedPlayerAnimator = playerAnimator != null
            ? playerAnimator
            : playerReferences != null
                ? playerReferences.MainAnimator
                : null;
        _cachedPlayerPresentationAnimator = ResolvePlayerPresentationAnimator();
    }

    Animator ResolvePlayerPresentationAnimator()
    {
        Animator preferredAnimator = playerReferences != null ? playerReferences.MainAnimator : playerAnimator;
        if (IsUsablePresentationAnimator(preferredAnimator))
            return preferredAnimator;

        Transform visualRoot = playerReferences != null ? playerReferences.VisualRoot : null;
        if (visualRoot != null)
        {
            BossVisualRig bossStyleVisualRig = visualRoot.GetComponentInChildren<BossVisualRig>(true);
            if (bossStyleVisualRig != null && IsUsablePresentationAnimator(bossStyleVisualRig.SourceAnimator))
                return bossStyleVisualRig.SourceAnimator;

            PlayerVisualRig playerVisualRig = playerReferences != null ? playerReferences.VisualRig : visualRoot.GetComponentInChildren<PlayerVisualRig>(true);
            if (playerVisualRig != null && IsUsablePresentationAnimator(playerVisualRig.MainAnimator))
                return playerVisualRig.MainAnimator;

            Animator[] visualAnimators = visualRoot.GetComponentsInChildren<Animator>(true);
            if (visualAnimators != null)
            {
                for (int i = 0; i < visualAnimators.Length; i++)
                {
                    Animator visualAnimator = visualAnimators[i];
                    if (IsUsablePresentationAnimator(visualAnimator))
                        return visualAnimator;
                }
            }
        }

        return _cachedPlayerAnimator;
    }

    static bool IsUsablePresentationAnimator(Animator animator)
    {
        return animator != null
            && animator.avatar != null
            && animator.runtimeAnimatorController != null
            && animator.gameObject.activeInHierarchy;
    }

    void RefreshCharacterControllerGeometryCache()
    {
        if (characterController == null)
            return;

        Transform root = PlayerRoot;
        Quaternion rotation = root.rotation;
        Vector3 lossyScale = root.lossyScale;
        Vector3 center = characterController.center;
        float radius = characterController.radius;
        float height = characterController.height;

        if (_cachedControllerRotation == rotation &&
            _cachedControllerLossyScale == lossyScale &&
            _cachedControllerCenter == center &&
            Mathf.Approximately(_cachedControllerRadius, radius) &&
            Mathf.Approximately(_cachedControllerHeight, height))
        {
            return;
        }

        _cachedControllerRotation = rotation;
        _cachedControllerLossyScale = lossyScale;
        _cachedControllerCenter = center;
        _cachedControllerRadius = radius;
        _cachedControllerHeight = height;

        _cachedControllerCenterOffset = rotation * Vector3.Scale(center, lossyScale);
        _cachedControllerUpAxis = rotation * Vector3.up;

        float radiusScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
        _cachedControllerWorldRadius = Mathf.Max(0.05f, radius * radiusScale - collisionSkin);

        float scaledHeight = Mathf.Max(height * Mathf.Abs(lossyScale.y), _cachedControllerWorldRadius * 2f);
        _cachedControllerHalfHeight = Mathf.Max(0f, (scaledHeight * 0.5f) - _cachedControllerWorldRadius);
    }

    Renderer ResolveTargetAimRenderer(Transform targetRoot)
    {
        if (targetRoot == null)
            return null;

        if (_cachedAimTargetRoot == targetRoot && _hasCachedAimTargetRenderer)
            return _cachedAimTargetRenderer;

        _cachedAimTargetRoot = targetRoot;
        _cachedAimTargetRenderer = targetRoot.GetComponentInChildren<Renderer>();
        _hasCachedAimTargetRenderer = true;
        return _cachedAimTargetRenderer;
    }

    float GetTargetPassThroughPadding()
    {
        Transform targetRoot = _activeTarget != null ? _activeTarget.TargetRoot : null;
        Renderer renderer = ResolveTargetAimRenderer(targetRoot);
        if (renderer == null)
            return 2.4f;

        Bounds bounds = renderer.bounds;
        float horizontalExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
        return Mathf.Max(2.4f, horizontalExtent * 4f);
    }

    void InvalidateTargetAimCache()
    {
        _cachedAimTargetRoot = null;
        _cachedAimTargetRenderer = null;
        _hasCachedAimTargetRenderer = false;
        _cachedAimBottomToCenterRatio = -1f;
        _cachedAimVerticalOffset = 0f;
        _cachedAimTargetRootPosition = Vector3.zero;
        _cachedAimPoint = Vector3.zero;
        _hasCachedAimPoint = false;
    }
}
