using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateCameraDirector : MonoBehaviour
{
    const float PositionUpdateThresholdSqr = 0.000001f;
    const float RotationUpdateThresholdDot = 0.999999f;
    const float FovUpdateThreshold = 0.001f;

    [Header("런타임 참조")]
    [SerializeField] private UltimateStageRuntime runtimeStage;
    [SerializeField] private Camera fallbackMainCamera;

    [Header("카메라 충돌")]
    [SerializeField] private LayerMask cameraObstacleMask = ~0;
    [SerializeField] private float collisionRadius = 0.18f;
    [SerializeField] private float collisionPadding = 0.08f;
    [SerializeField] private float minimumDistanceToFocus = 0.85f;

    readonly RaycastHit[] _occlusionHits = new RaycastHit[8];

    UltimateStageRuntime _activeStage;
    Camera _activeCamera;
    Transform _activeCameraTransform;
    CameraShake _cameraShake;
    UltimateTargetBinder _binder;
    UltimateSequenceData _data;
    UltimateSequenceData.CameraShotSettings _currentShot;
    UltimateCameraStage _currentStage = UltimateCameraStage.None;
    bool _sequenceActive;
    bool _hasCameraPose;
    bool _hasStaticFraming;
    bool _focusOnPlayerSword;
    float _stageElapsed;
    Vector3 _currentPosition;
    Quaternion _currentRotation;
    float _currentFieldOfView;
    Vector3 _staticPosition;
    Quaternion _staticRotation;
    float _staticFieldOfView;
    Vector3 _lastOcclusionFocusPoint;
    Vector3 _lastOcclusionDesiredPosition;
    Vector3 _lastOcclusionResolvedPosition;
    bool _hasOcclusionCache;

    const float OcclusionReuseThresholdSqr = 0.0025f;

    public Camera ActiveCamera => _activeCamera;
    public CameraShake ActiveCameraShake => _cameraShake;
    public UltimateCameraStage CurrentStage => _currentStage;
    public UltimateStageRuntime ActiveStage => _activeStage;

    public void BeginSequence(PlayerUltimateController owner, UltimateTargetBinder binder, UltimateSequenceData data)
    {
        _binder = binder;
        _data = data;
        _activeStage = runtimeStage != null ? runtimeStage : UltimateStageRuntime.GetOrCreate();
        _activeStage.EnsureRuntimeObjects();

        Camera sourceCamera = fallbackMainCamera != null ? fallbackMainCamera : Camera.main;
        _activeStage.BeginPresentationCapture(sourceCamera);
        _activeCamera = _activeStage.PresentationCamera;
        _activeCameraTransform = _activeCamera != null ? _activeCamera.transform : null;
        _cameraShake = _activeStage.SequenceCameraShake;
        _sequenceActive = _activeCamera != null;
        _hasCameraPose = false;
        _hasStaticFraming = false;
        _currentStage = UltimateCameraStage.None;
        _currentShot = null;
        _stageElapsed = 0f;
        _hasOcclusionCache = false;
        _focusOnPlayerSword = false;
    }

    public void EndSequence()
    {
        if (_activeStage != null)
            _activeStage.EndPresentationCapture();

        _sequenceActive = false;
        _hasCameraPose = false;
        _activeCamera = null;
        _activeCameraTransform = null;
        _cameraShake = null;
        _activeStage = null;
        _binder = null;
        _data = null;
        _currentShot = null;
        _currentStage = UltimateCameraStage.None;
        _hasStaticFraming = false;
        _stageElapsed = 0f;
        _hasOcclusionCache = false;
        _focusOnPlayerSword = false;
    }

    public void SetPhase(UltimateSequencePhase phase, UltimateSequenceData data, bool force = false)
    {
        if (data == null)
            return;

        SetShot(data.ResolveCameraStage(phase), data.ResolveCameraShot(phase), force, false);
    }

    public void SetShot(UltimateSequenceData.CameraShotSettings shot)
    {
        SetShot(UltimateCameraStage.None, shot, true, false);
    }

    public void SetShot(UltimateCameraStage stage, UltimateSequenceData.CameraShotSettings shot, bool force = false, bool focusOnPlayerSword = false)
    {
        if (shot == null)
            return;

        bool stageChanged = force || stage != _currentStage || !ReferenceEquals(shot, _currentShot);
        _currentStage = stage;
        _currentShot = shot;
        _focusOnPlayerSword = focusOnPlayerSword;
        if (_activeStage != null)
            _activeStage.SetPresentationIsolation(stage == UltimateCameraStage.Intro);

        if (stageChanged)
        {
            _hasCameraPose = false;
            _hasStaticFraming = false;
            _stageElapsed = 0f;
            _hasOcclusionCache = false;
        }
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (!_sequenceActive || _activeCamera == null || _activeCameraTransform == null || _binder == null || _currentShot == null)
            return;

        _stageElapsed += Mathf.Max(0f, unscaledDeltaTime);
        Vector3 playerAnchor;
        Vector3 focusPoint;
        Vector3 desiredPosition;
        Quaternion desiredRotation;

        if (_focusOnPlayerSword)
        {
            Vector3 swordLookOffset = _data != null
                ? _data.CinematicAnimation.introSwordLookLocalOffset
                : Vector3.zero;
            Vector3 swordFocusPoint = _binder.GetPlayerIntroSwordLookPoint(swordLookOffset);
            Vector3 playerUpperBodyPoint = _binder.GetPlayerCameraAnchor(_currentShot.playerAnchorLocalOffset);
            Vector3 chestFocusPoint = playerUpperBodyPoint + Vector3.up * (_currentShot.lookHeightOffset * 0.35f);
            focusPoint = Vector3.Lerp(chestFocusPoint, swordFocusPoint, 0.58f);
            Transform introShotAnchor = _activeStage != null ? _activeStage.OpenShotAnchor : null;
            Transform introLookAnchor = _activeStage != null ? _activeStage.OpenShotLookAnchor : null;
            if (_currentStage == UltimateCameraStage.Intro && introShotAnchor != null)
            {
                desiredPosition = introShotAnchor.position;
                Vector3 lockedFocusPoint = introLookAnchor != null ? introLookAnchor.position : focusPoint;
                Vector3 lookDirection = lockedFocusPoint - desiredPosition;
                desiredRotation = lookDirection.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                    : introShotAnchor.rotation;
                focusPoint = lockedFocusPoint;
            }
            else
            {
                Vector3 cameraPivotPoint = _binder.GetPlayerIntroSwordCameraPivotPoint(_currentShot.cameraLocalOffset);
                desiredPosition = cameraPivotPoint;
                Vector3 lookDirection = focusPoint - desiredPosition;
                if (lookDirection.sqrMagnitude <= 0.0001f)
                    lookDirection = _binder.PlayerRoot.forward;
                desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }
        }
        else
        {
            playerAnchor = _binder.GetPlayerCameraAnchor(_currentShot.playerAnchorLocalOffset);
            Vector3 targetAnchor = _binder.GetTargetAimPoint(_currentShot.targetBottomToCenterRatio, _currentShot.targetVerticalOffset);
            Vector3 flatDirection = targetAnchor - playerAnchor;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude <= 0.0001f)
                flatDirection = _binder.PlayerRoot.forward;
            flatDirection.Normalize();

            Quaternion basis = Quaternion.LookRotation(flatDirection, Vector3.up);
            if (_currentShot.orbitAroundTarget)
            {
                float orbitSign = _currentShot.orbitClockwise ? -1f : 1f;
                float orbitAngle = _stageElapsed * _currentShot.orbitDegreesPerSecond * orbitSign;
                basis = Quaternion.AngleAxis(orbitAngle, Vector3.up) * basis;
            }

            float focusBlend = Mathf.Clamp01(_currentShot.lookAtBlend);
            if (_currentShot.orbitAroundTarget)
                focusBlend = Mathf.Max(focusBlend, _currentShot.orbitFocusBias);

            focusPoint = Vector3.Lerp(playerAnchor, targetAnchor, focusBlend) + Vector3.up * _currentShot.lookHeightOffset;
            Vector3 cameraOrigin = _currentShot.anchorCameraOnTarget ? targetAnchor : playerAnchor;
            desiredPosition = cameraOrigin + basis * _currentShot.cameraLocalOffset;
            desiredPosition = ResolveOccludedCameraPosition(focusPoint, desiredPosition);
            desiredRotation = Quaternion.LookRotation((focusPoint - desiredPosition).normalized, Vector3.up);
        }

        float desiredFov = Mathf.Max(10f, _currentShot.fov);

        if (_currentShot.holdStaticFraming)
        {
            if (!_hasStaticFraming)
            {
                _staticPosition = desiredPosition;
                _staticRotation = desiredRotation;
                _staticFieldOfView = desiredFov;
                _hasStaticFraming = true;
            }

            desiredPosition = _staticPosition;
            desiredRotation = _staticRotation;
            desiredFov = _staticFieldOfView;
        }

        if (!_hasCameraPose || _currentShot.snapOnEnter)
        {
            _currentPosition = desiredPosition;
            _currentRotation = desiredRotation;
            _currentFieldOfView = desiredFov;
            _hasCameraPose = true;
        }
        else
        {
            float positionBlend = 1f - Mathf.Exp(-Mathf.Max(0.01f, _currentShot.positionSmoothing) * Mathf.Max(0f, unscaledDeltaTime));
            float rotationBlend = 1f - Mathf.Exp(-Mathf.Max(0.01f, _currentShot.rotationSmoothing) * Mathf.Max(0f, unscaledDeltaTime));
            _currentPosition = Vector3.Lerp(_currentPosition, desiredPosition, positionBlend);
            _currentRotation = Quaternion.Slerp(_currentRotation, desiredRotation, rotationBlend);
            _currentFieldOfView = Mathf.Lerp(_currentFieldOfView, desiredFov, positionBlend);
        }

        if ((_activeCameraTransform.position - _currentPosition).sqrMagnitude > PositionUpdateThresholdSqr ||
            Quaternion.Dot(_activeCameraTransform.rotation, _currentRotation) < RotationUpdateThresholdDot)
        {
            _activeCameraTransform.SetPositionAndRotation(_currentPosition, _currentRotation);
        }

        if (Mathf.Abs(_activeCamera.fieldOfView - _currentFieldOfView) > FovUpdateThreshold)
            _activeCamera.fieldOfView = _currentFieldOfView;
    }

    public void PlayShake(float amplitude, float duration)
    {
        if (_cameraShake == null || amplitude <= 0f || duration <= 0f)
            return;

        _cameraShake.Shake(amplitude, duration);
    }

    Vector3 ResolveOccludedCameraPosition(Vector3 focusPoint, Vector3 desiredPosition)
    {
        if (cameraObstacleMask.value == 0)
            return desiredPosition;

        if (_hasOcclusionCache &&
            (focusPoint - _lastOcclusionFocusPoint).sqrMagnitude <= OcclusionReuseThresholdSqr &&
            (desiredPosition - _lastOcclusionDesiredPosition).sqrMagnitude <= OcclusionReuseThresholdSqr)
        {
            return _lastOcclusionResolvedPosition;
        }

        Vector3 direction = desiredPosition - focusPoint;
        float distance = direction.magnitude;
        if (distance <= minimumDistanceToFocus || distance <= 0.0001f)
        {
            CacheOcclusionResult(focusPoint, desiredPosition, desiredPosition);
            return desiredPosition;
        }

        direction /= distance;
        int hitCount = Physics.SphereCastNonAlloc(
            focusPoint,
            collisionRadius,
            direction,
            _occlusionHits,
            distance,
            cameraObstacleMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
        {
            CacheOcclusionResult(focusPoint, desiredPosition, desiredPosition);
            return desiredPosition;
        }

        float nearestDistance = distance;
        Transform playerRoot = _binder != null ? _binder.PlayerRoot : null;
        Transform targetRoot = _binder != null && _binder.ActiveTarget != null ? _binder.ActiveTarget.TargetRoot : null;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _occlusionHits[i];
            if (hit.collider == null)
                continue;
            if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot))
                continue;
            if (targetRoot != null && hit.collider.transform.IsChildOf(targetRoot))
                continue;

            nearestDistance = Mathf.Min(nearestDistance, hit.distance);
        }

        if (nearestDistance >= distance)
        {
            CacheOcclusionResult(focusPoint, desiredPosition, desiredPosition);
            return desiredPosition;
        }

        float adjustedDistance = Mathf.Max(minimumDistanceToFocus, nearestDistance - collisionPadding);
        Vector3 resolvedPosition = focusPoint + direction * adjustedDistance;
        CacheOcclusionResult(focusPoint, desiredPosition, resolvedPosition);
        return resolvedPosition;
    }

    void CacheOcclusionResult(Vector3 focusPoint, Vector3 desiredPosition, Vector3 resolvedPosition)
    {
        _hasOcclusionCache = true;
        _lastOcclusionFocusPoint = focusPoint;
        _lastOcclusionDesiredPosition = desiredPosition;
        _lastOcclusionResolvedPosition = resolvedPosition;
    }
}
