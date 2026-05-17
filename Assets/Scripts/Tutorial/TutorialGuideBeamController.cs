using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class TutorialGuideBeamController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform movementTarget;
    [SerializeField] private Transform attackTarget;
    [SerializeField] private Transform guardTarget;
    [SerializeField] private Transform exitTarget;
    [SerializeField] private TutorialWorldMarker movementMarker;
    [SerializeField] private TutorialWorldMarker attackMarker;
    [SerializeField] private TutorialWorldMarker guardMarker;
    [SerializeField] private TutorialWorldMarker exitMarker;

    [Header("Layout")]
    [SerializeField] private Vector3 startOffset = new Vector3(0f, 0.04f, 0f);
    [SerializeField] private Vector3 endOffset = new Vector3(0f, 0.04f, 0f);
    [SerializeField, Min(0f)] private float floorLift = 0.045f;
    [SerializeField, Range(0.1f, 0.5f)] private float firstSegmentT = 0.34f;
    [SerializeField, Range(0.5f, 0.9f)] private float secondSegmentT = 0.68f;
    [SerializeField, Min(0.01f)] private float beamWidth = 0.11f;
    [SerializeField, Min(0f)] private float endWidthMultiplier = 1.18f;
    [SerializeField] private Vector3 startDiscScale = new Vector3(0.58f, 0.012f, 0.58f);
    [SerializeField] private Vector3 endDiscScale = new Vector3(0.82f, 0.014f, 0.82f);
    [SerializeField] private Vector3 endRippleScale = new Vector3(1.08f, 0.009f, 1.08f);
    [SerializeField, Range(0f, 1f)] private float endRippleAlpha = 0.34f;
    [SerializeField, Min(0f)] private float endRippleScaleBonus = 0.42f;

    [Header("Breadcrumbs")]
    [SerializeField, Range(2, 8)] private int breadcrumbCount = 4;
    [SerializeField] private Vector3 breadcrumbScale = new Vector3(0.22f, 0.01f, 0.22f);
    [SerializeField, Range(0f, 1f)] private float breadcrumbAlpha = 0.58f;
    [SerializeField, Min(0f)] private float breadcrumbScalePulseAmplitude = 0.08f;
    [SerializeField, Min(0.05f)] private float breadcrumbFlowSpeed = 0.55f;
    [SerializeField, Range(0f, 1f)] private float breadcrumbFlowAlphaBoost = 0.34f;

    [Header("Ground Snap")]
    [SerializeField] private bool snapToGround = true;
    [SerializeField, Min(0.1f)] private float groundProbeHeight = 1.75f;
    [SerializeField, Min(0.2f)] private float groundProbeDistance = 4f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("NavMesh Path")]
    [SerializeField] private bool preferNavMeshPath = true;
    [SerializeField, Min(0.05f)] private float navMeshRefreshInterval = 0.2f;
    [SerializeField, Min(0.05f)] private float navMeshSampleDistance = 2.25f;
    [SerializeField, Min(0.05f)] private float navMeshRepathDistance = 0.45f;
    [SerializeField, Range(4, 16)] private int maxPathPoints = 10;

    [Header("Pulse")]
    [SerializeField, Min(0f)] private float widthPulseAmplitude = 0.018f;
    [SerializeField, Min(0f)] private float alphaPulseAmplitude = 0.16f;
    [SerializeField, Min(0.1f)] private float pulseSpeed = 3.2f;
    [SerializeField, Min(0.1f)] private float arrivalHighlightDistance = 2.8f;
    [SerializeField, Min(0f)] private float arrivalScaleBonus = 0.24f;
    [SerializeField, Min(0f)] private float arrivalAlphaBonus = 0.12f;
    [SerializeField, Min(1f)] private float exitPulseSpeedMultiplier = 1.24f;
    [SerializeField, Min(1f)] private float exitArrivalScaleMultiplier = 1.18f;
    [SerializeField, Min(1f)] private float exitArrivalAlphaMultiplier = 1.15f;
    [SerializeField, Min(0f)] private float exitBreadcrumbBoost = 0.12f;
    [SerializeField, Min(0f)] private float exitColorWhiten = 0.18f;

    [Header("Colors")]
    [SerializeField] private Color movementColor = new Color(0.18f, 0.85f, 1f, 0.9f);
    [SerializeField] private Color attackColor = new Color(0.24f, 0.88f, 1f, 0.92f);
    [SerializeField] private Color guardColor = new Color(1.00f, 0.45f, 0.18f, 0.92f);
    [SerializeField] private Color exitColor = new Color(1.00f, 0.68f, 0.18f, 0.92f);

    LineRenderer _lineRenderer;
    Transform _activeTarget;
    Color _activeColor;
    bool _subscribed;
    readonly RaycastHit[] _groundHits = new RaycastHit[6];
    Transform _startDisc;
    Transform _endDisc;
    Transform _endRippleDisc;
    Renderer _startDiscRenderer;
    Renderer _endDiscRenderer;
    Renderer _endRippleDiscRenderer;
    MaterialPropertyBlock _discPropertyBlock;
    Transform[] _breadcrumbs;
    Renderer[] _breadcrumbRenderers;
    NavMeshPath _navMeshPath;
    Vector3[] _cachedPathPoints;
    Vector3[] _renderPathPoints;
    int _cachedPathPointCount;
    int _renderPathPointCount;
    float _nextNavMeshRefreshRealtime;
    Vector3 _lastNavMeshStart;
    Vector3 _lastNavMeshEnd;
    bool _hasCachedNavMeshPath;
    bool _isExitGuide;

    static Material s_lineMaterial;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        Transform runtimePlayerRoot,
        Transform runtimeMovementTarget,
        Transform runtimeExitTarget,
        TutorialWorldMarker runtimeMovementMarker,
        TutorialWorldMarker runtimeExitMarker)
    {
        ConfigureRuntime(
            runtimeFlowController,
            runtimePlayerRoot,
            runtimeMovementTarget,
            null,
            null,
            runtimeExitTarget,
            runtimeMovementMarker,
            null,
            null,
            runtimeExitMarker);
    }

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        Transform runtimePlayerRoot,
        Transform runtimeMovementTarget,
        Transform runtimeAttackTarget,
        Transform runtimeGuardTarget,
        Transform runtimeExitTarget,
        TutorialWorldMarker runtimeMovementMarker,
        TutorialWorldMarker runtimeAttackMarker,
        TutorialWorldMarker runtimeGuardMarker,
        TutorialWorldMarker runtimeExitMarker)
    {
        flowController = runtimeFlowController;
        playerRoot = runtimePlayerRoot;
        movementTarget = runtimeMovementTarget;
        attackTarget = runtimeAttackTarget;
        guardTarget = runtimeGuardTarget;
        exitTarget = runtimeExitTarget;
        movementMarker = runtimeMovementMarker;
        attackMarker = runtimeAttackMarker;
        guardMarker = runtimeGuardMarker;
        exitMarker = runtimeExitMarker;

        EnsureBuilt();
        RefreshSubscriptions();
        SetActiveGuide(null, default);
        HandleStepStarted(flowController != null ? flowController.CurrentStep : null);
    }

    void Awake()
    {
        EnsureBuilt();
    }

    void OnEnable()
    {
        EnsureBuilt();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        SetActiveGuide(null, default);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void LateUpdate()
    {
        if (_activeTarget == null || playerRoot == null || _lineRenderer == null)
            return;

        Vector3 start = ResolveGuidePoint(playerRoot, startOffset);
        Vector3 end = ResolveGuidePoint(_activeTarget, endOffset);
        ApplyPathPositions(start, end);

        float effectivePulseSpeed = pulseSpeed * (_isExitGuide ? exitPulseSpeedMultiplier : 1f);
        float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * effectivePulseSpeed) * 0.5f);
        float width = beamWidth + (widthPulseAmplitude * pulse);
        float alpha = Mathf.Clamp01(_activeColor.a - alphaPulseAmplitude + (alphaPulseAmplitude * pulse));
        float arrivalFactor = ComputeArrivalFactor(start, end);
        float arrivalScaleMultiplier = _isExitGuide
            ? 1f + ((exitArrivalScaleMultiplier - 1f) * arrivalFactor)
            : 1f;
        float arrivalAlphaMultiplier = _isExitGuide
            ? 1f + ((exitArrivalAlphaMultiplier - 1f) * arrivalFactor)
            : 1f;

        Color currentColor = _activeColor;
        if (_isExitGuide && exitColorWhiten > 0f)
            currentColor = Color.Lerp(currentColor, Color.white, arrivalFactor * exitColorWhiten);
        currentColor.a = Mathf.Clamp01(alpha + (arrivalAlphaBonus * arrivalFactor * arrivalAlphaMultiplier));

        _lineRenderer.startWidth = width;
        _lineRenderer.endWidth = width * endWidthMultiplier;
        _lineRenderer.startColor = currentColor;
        _lineRenderer.endColor = currentColor;

        UpdateDisc(_startDisc, _startDiscRenderer, start, startDiscScale, currentColor, pulse);
        UpdateDisc(_endDisc, _endDiscRenderer, end, endDiscScale * (1f + (arrivalScaleBonus * arrivalFactor)) * arrivalScaleMultiplier, currentColor, 1f - pulse);
        UpdateRippleDisc(end, currentColor, arrivalFactor, effectivePulseSpeed);
        UpdateBreadcrumbs(currentColor, arrivalFactor, effectivePulseSpeed);
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (flowController == null)
            return;

        flowController.StepStarted += HandleStepStarted;
        flowController.StepCompleted += HandleStepCompleted;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed || flowController == null)
            return;

        flowController.StepStarted -= HandleStepStarted;
        flowController.StepCompleted -= HandleStepCompleted;
        _subscribed = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        if (step == null)
        {
            SetActiveGuide(null, default);
            return;
        }

        switch (step.stepType)
        {
            case TutorialStepType.Movement:
                SetActiveGuide(movementTarget, movementColor);
                break;

            case TutorialStepType.CameraFocus:
            case TutorialStepType.LockOn:
            case TutorialStepType.BasicAttack:
            case TutorialStepType.Combo:
            case TutorialStepType.Ultimate:
                SetActiveGuide(attackTarget, attackColor);
                break;

            case TutorialStepType.Guard:
            case TutorialStepType.Parry:
            case TutorialStepType.Dodge:
            case TutorialStepType.PerfectDodge:
                SetActiveGuide(guardTarget, guardColor);
                break;

            case TutorialStepType.Exit:
                SetActiveGuide(exitTarget, exitColor);
                break;

            default:
                SetActiveGuide(null, default);
                break;
        }
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (step == null)
            return;

        if (_activeTarget != null)
            SetActiveGuide(null, default);
    }

    void SetActiveGuide(Transform target, Color color)
    {
        _activeTarget = target;
        _activeColor = color;
        _isExitGuide = target != null && ReferenceEquals(target, exitTarget);

        bool shouldShow = _activeTarget != null && playerRoot != null && _lineRenderer != null;
        if (_lineRenderer != null)
            _lineRenderer.enabled = shouldShow;
        SetDiscVisible(_startDiscRenderer, shouldShow);
        SetDiscVisible(_endDiscRenderer, shouldShow);
        SetDiscVisible(_endRippleDiscRenderer, shouldShow);
        SetBreadcrumbVisible(shouldShow);

        SetMarkerVisible(movementMarker, ReferenceEquals(_activeTarget, movementTarget));
        SetMarkerVisible(attackMarker, ReferenceEquals(_activeTarget, attackTarget));
        SetMarkerVisible(guardMarker, ReferenceEquals(_activeTarget, guardTarget));
        SetMarkerVisible(exitMarker, ReferenceEquals(_activeTarget, exitTarget));

        if (!shouldShow)
        {
            _isExitGuide = false;
            ClearCachedPath();
        }

        if (shouldShow)
            LateUpdate();
    }

    void EnsureBuilt()
    {
        if (_lineRenderer != null)
            return;

        _lineRenderer = gameObject.GetComponent<LineRenderer>();
        if (_lineRenderer == null)
            _lineRenderer = gameObject.AddComponent<LineRenderer>();

        _lineRenderer.positionCount = 4;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.loop = false;
        _lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _lineRenderer.receiveShadows = false;
        _lineRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _lineRenderer.textureMode = LineTextureMode.Stretch;
        _lineRenderer.alignment = LineAlignment.View;
        _lineRenderer.numCornerVertices = 3;
        _lineRenderer.numCapVertices = 4;
        _lineRenderer.sharedMaterial = GetLineMaterial();
        _lineRenderer.enabled = false;
        _lineRenderer.positionCount = 4;

        if (_startDisc == null)
            _startDisc = EnsureDisc("GuideStartDisc", out _startDiscRenderer);
        if (_endDisc == null)
            _endDisc = EnsureDisc("GuideEndDisc", out _endDiscRenderer);
        if (_endRippleDisc == null)
            _endRippleDisc = EnsureDisc("GuideEndRipple", out _endRippleDiscRenderer);
        EnsureBreadcrumbs();

        SetDiscVisible(_startDiscRenderer, false);
        SetDiscVisible(_endDiscRenderer, false);
        SetDiscVisible(_endRippleDiscRenderer, false);
        SetBreadcrumbVisible(false);
        _discPropertyBlock ??= new MaterialPropertyBlock();
        _navMeshPath ??= new NavMeshPath();
        if (_cachedPathPoints == null || _cachedPathPoints.Length != maxPathPoints)
            _cachedPathPoints = new Vector3[maxPathPoints];
        EnsureRenderPathBuffer();
    }

    void SetMarkerVisible(TutorialWorldMarker marker, bool visible)
    {
        if (marker == null)
            return;

        marker.SetVisible(visible);
    }

    Vector3 ResolveGuidePoint(Transform anchor, Vector3 offset)
    {
        if (anchor == null)
            return Vector3.zero;

        Vector3 basePoint = anchor.position + offset;
        if (!snapToGround)
            return basePoint;

        Vector3 probeOrigin = basePoint + Vector3.up * groundProbeHeight;
        float probeLength = groundProbeHeight + groundProbeDistance;
        int hitCount = Physics.RaycastNonAlloc(
            probeOrigin,
            Vector3.down,
            _groundHits,
            probeLength,
            groundMask,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.PositiveInfinity;
        Vector3 bestPoint = basePoint;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _groundHits[i];
            if (hit.collider == null || IsIgnoredHit(hit.transform, anchor))
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestPoint = hit.point;
        }

        bestPoint.y += floorLift;
        return bestPoint;
    }

    bool IsIgnoredHit(Transform hitTransform, Transform ignoreRoot)
    {
        if (hitTransform == null || ignoreRoot == null)
            return false;

        return hitTransform == ignoreRoot || hitTransform.IsChildOf(ignoreRoot);
    }

    Transform EnsureDisc(string objectName, out Renderer renderer)
    {
        Transform existing = transform.Find(objectName);
        if (existing != null)
        {
            renderer = existing.GetComponent<Renderer>();
            ConfigureDiscRenderer(renderer);
            return existing;
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        primitive.name = objectName;
        primitive.transform.SetParent(transform, false);

        Collider primitiveCollider = primitive.GetComponent<Collider>();
        if (primitiveCollider != null)
            Destroy(primitiveCollider);

        renderer = primitive.GetComponent<Renderer>();
        ConfigureDiscRenderer(renderer);
        return primitive.transform;
    }

    void ConfigureDiscRenderer(Renderer renderer)
    {
        if (renderer == null)
            return;

        renderer.sharedMaterial = GetLineMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    void UpdateDisc(Transform disc, Renderer renderer, Vector3 position, Vector3 baseScale, Color color, float pulse)
    {
        if (disc == null || renderer == null)
            return;

        disc.position = position;
        disc.rotation = Quaternion.identity;
        disc.localScale = baseScale * (0.94f + (0.12f * pulse));

        _discPropertyBlock.Clear();
        _discPropertyBlock.SetColor("_Color", color);
        _discPropertyBlock.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(_discPropertyBlock);
    }

    void UpdateRippleDisc(Vector3 position, Color color, float arrivalFactor, float effectivePulseSpeed)
    {
        if (_endRippleDisc == null || _endRippleDiscRenderer == null)
            return;

        float ripple = Mathf.Repeat(Time.unscaledTime * (effectivePulseSpeed * 0.35f), 1f);
        float scaleMultiplier = 1f + (ripple * endRippleScaleBonus * (1f + (arrivalFactor * 0.8f)));
        Color rippleColor = color;
        rippleColor.a *= endRippleAlpha * (1f - ripple) * (0.72f + (arrivalFactor * 0.55f));

        _endRippleDisc.position = position;
        _endRippleDisc.rotation = Quaternion.identity;
        _endRippleDisc.localScale = endRippleScale * scaleMultiplier;

        _discPropertyBlock.Clear();
        _discPropertyBlock.SetColor("_Color", rippleColor);
        _discPropertyBlock.SetColor("_BaseColor", rippleColor);
        _endRippleDiscRenderer.SetPropertyBlock(_discPropertyBlock);
    }

    void SetDiscVisible(Renderer renderer, bool visible)
    {
        if (renderer == null)
            return;

        renderer.enabled = visible;
    }

    void ApplyPathPositions(Vector3 start, Vector3 end)
    {
        if (TryBuildNavMeshPath(start, end))
        {
            if (_cachedPathPointCount < 2)
                _cachedPathPointCount = 2;

            _cachedPathPoints[0] = start;
            _cachedPathPoints[_cachedPathPointCount - 1] = end;

            if (_lineRenderer.positionCount != _cachedPathPointCount)
                _lineRenderer.positionCount = _cachedPathPointCount;

            EnsureRenderPathBuffer();
            _renderPathPointCount = _cachedPathPointCount;
            for (int i = 0; i < _cachedPathPointCount; i++)
            {
                _lineRenderer.SetPosition(i, _cachedPathPoints[i]);
                _renderPathPoints[i] = _cachedPathPoints[i];
            }

            return;
        }

        Vector3 first = Vector3.Lerp(start, end, firstSegmentT);
        Vector3 second = Vector3.Lerp(start, end, secondSegmentT);
        if (_lineRenderer.positionCount != 4)
            _lineRenderer.positionCount = 4;
        _lineRenderer.SetPosition(0, start);
        _lineRenderer.SetPosition(1, first);
        _lineRenderer.SetPosition(2, second);
        _lineRenderer.SetPosition(3, end);
        EnsureRenderPathBuffer();
        _renderPathPointCount = 4;
        _renderPathPoints[0] = start;
        _renderPathPoints[1] = first;
        _renderPathPoints[2] = second;
        _renderPathPoints[3] = end;
    }

    bool TryBuildNavMeshPath(Vector3 start, Vector3 end)
    {
        if (!preferNavMeshPath || maxPathPoints < 4)
            return false;

        if (_navMeshPath == null)
            _navMeshPath = new NavMeshPath();
        if (_cachedPathPoints == null || _cachedPathPoints.Length != maxPathPoints)
            _cachedPathPoints = new Vector3[maxPathPoints];
        EnsureRenderPathBuffer();

        bool shouldRefresh = !_hasCachedNavMeshPath ||
                             Time.unscaledTime >= _nextNavMeshRefreshRealtime ||
                             FlatDistance(_lastNavMeshStart, start) >= navMeshRepathDistance ||
                             FlatDistance(_lastNavMeshEnd, end) >= navMeshRepathDistance;

        if (shouldRefresh)
        {
            _nextNavMeshRefreshRealtime = Time.unscaledTime + navMeshRefreshInterval;
            _lastNavMeshStart = start;
            _lastNavMeshEnd = end;
            _hasCachedNavMeshPath = RebuildNavMeshPath(start, end);
        }

        return _hasCachedNavMeshPath && _cachedPathPointCount >= 2;
    }

    bool RebuildNavMeshPath(Vector3 start, Vector3 end)
    {
        if (!NavMesh.SamplePosition(start, out NavMeshHit startHit, navMeshSampleDistance, NavMesh.AllAreas))
            return false;
        if (!NavMesh.SamplePosition(end, out NavMeshHit endHit, navMeshSampleDistance, NavMesh.AllAreas))
            return false;
        if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, _navMeshPath))
            return false;
        if (_navMeshPath.status == NavMeshPathStatus.PathInvalid || _navMeshPath.corners == null || _navMeshPath.corners.Length < 2)
            return false;

        int writeIndex = 0;
        _cachedPathPoints[writeIndex++] = start;

        int lastCornerIndex = _navMeshPath.corners.Length - 1;
        for (int i = 1; i < lastCornerIndex && writeIndex < maxPathPoints - 1; i++)
        {
            _cachedPathPoints[writeIndex++] = ResolveGuidePoint(_navMeshPath.corners[i]);
        }

        _cachedPathPoints[writeIndex++] = end;
        _cachedPathPointCount = writeIndex;
        return _cachedPathPointCount >= 2;
    }

    Vector3 ResolveGuidePoint(Vector3 worldPoint)
    {
        if (!snapToGround)
            return worldPoint;

        Vector3 probeOrigin = worldPoint + Vector3.up * groundProbeHeight;
        float probeLength = groundProbeHeight + groundProbeDistance;
        int hitCount = Physics.RaycastNonAlloc(
            probeOrigin,
            Vector3.down,
            _groundHits,
            probeLength,
            groundMask,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.PositiveInfinity;
        Vector3 bestPoint = worldPoint;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _groundHits[i];
            if (hit.collider == null)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestPoint = hit.point;
        }

        bestPoint.y += floorLift;
        return bestPoint;
    }

    void ClearCachedPath()
    {
        _hasCachedNavMeshPath = false;
        _cachedPathPointCount = 0;
        _renderPathPointCount = 0;
        _nextNavMeshRefreshRealtime = 0f;
    }

    float ComputeArrivalFactor(Vector3 start, Vector3 end)
    {
        float distance = FlatDistance(start, end);
        if (distance >= arrivalHighlightDistance)
            return 0f;

        float normalized = 1f - Mathf.Clamp01(distance / arrivalHighlightDistance);
        return normalized * normalized;
    }

    void EnsureRenderPathBuffer()
    {
        int requiredLength = Mathf.Max(4, maxPathPoints);
        if (_renderPathPoints == null || _renderPathPoints.Length != requiredLength)
            _renderPathPoints = new Vector3[requiredLength];
    }

    void EnsureBreadcrumbs()
    {
        if (_breadcrumbs != null &&
            _breadcrumbRenderers != null &&
            _breadcrumbs.Length == breadcrumbCount &&
            _breadcrumbRenderers.Length == breadcrumbCount)
            return;

        _breadcrumbs = new Transform[breadcrumbCount];
        _breadcrumbRenderers = new Renderer[breadcrumbCount];
        for (int i = 0; i < breadcrumbCount; i++)
            _breadcrumbs[i] = EnsureDisc("GuideBreadcrumb_" + i, out _breadcrumbRenderers[i]);
    }

    void UpdateBreadcrumbs(Color color, float arrivalFactor, float effectivePulseSpeed)
    {
        if (_breadcrumbs == null || _breadcrumbRenderers == null || _renderPathPointCount < 2)
            return;

        float totalLength = 0f;
        for (int i = 1; i < _renderPathPointCount; i++)
            totalLength += Vector3.Distance(_renderPathPoints[i - 1], _renderPathPoints[i]);

        if (totalLength <= 0.001f)
        {
            SetBreadcrumbVisible(false);
            return;
        }

        SetBreadcrumbVisible(true);
        float spacing = totalLength / (_breadcrumbs.Length + 1);
        Color breadcrumbColor = color;
        if (_isExitGuide && exitColorWhiten > 0f)
            breadcrumbColor = Color.Lerp(breadcrumbColor, Color.white, arrivalFactor * (exitColorWhiten * 0.8f));
        breadcrumbColor.a *= breadcrumbAlpha;
        float flowHead = Mathf.Repeat(Time.unscaledTime * breadcrumbFlowSpeed * (_isExitGuide ? exitPulseSpeedMultiplier : 1f), 1f);

        for (int i = 0; i < _breadcrumbs.Length; i++)
        {
            Transform breadcrumb = _breadcrumbs[i];
            Renderer breadcrumbRenderer = _breadcrumbRenderers[i];
            if (breadcrumb == null || breadcrumbRenderer == null)
                continue;

            Vector3 point = SamplePointOnPath(spacing * (i + 1));
            float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * effectivePulseSpeed + (i * 0.55f)) * 0.5f);
            float progress = (i + 1f) / (_breadcrumbs.Length + 1f);
            float flowDelta = Mathf.Abs(flowHead - progress);
            flowDelta = Mathf.Min(flowDelta, 1f - flowDelta);
            float flowHighlight = 1f - Mathf.Clamp01(flowDelta * 4.5f);
            if (_isExitGuide)
                flowHighlight = Mathf.Clamp01(flowHighlight + (arrivalFactor * exitBreadcrumbBoost));

            float scaleMultiplier = 0.96f + (breadcrumbScalePulseAmplitude * pulse) + (flowHighlight * 0.12f) + (_isExitGuide ? arrivalFactor * 0.08f : 0f);
            Color dynamicColor = breadcrumbColor;
            dynamicColor.a *= 0.68f + (flowHighlight * breadcrumbFlowAlphaBoost);

            breadcrumb.position = point;
            breadcrumb.rotation = Quaternion.identity;
            breadcrumb.localScale = breadcrumbScale * scaleMultiplier;

            _discPropertyBlock.Clear();
            _discPropertyBlock.SetColor("_Color", dynamicColor);
            _discPropertyBlock.SetColor("_BaseColor", dynamicColor);
            breadcrumbRenderer.SetPropertyBlock(_discPropertyBlock);
        }
    }

    Vector3 SamplePointOnPath(float targetDistance)
    {
        if (_renderPathPointCount <= 1)
            return Vector3.zero;

        float remainingDistance = Mathf.Max(0f, targetDistance);
        for (int i = 1; i < _renderPathPointCount; i++)
        {
            Vector3 segmentStart = _renderPathPoints[i - 1];
            Vector3 segmentEnd = _renderPathPoints[i];
            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            if (segmentLength <= 0.0001f)
                continue;

            if (remainingDistance <= segmentLength)
                return Vector3.Lerp(segmentStart, segmentEnd, remainingDistance / segmentLength);

            remainingDistance -= segmentLength;
        }

        return _renderPathPoints[_renderPathPointCount - 1];
    }

    void SetBreadcrumbVisible(bool visible)
    {
        if (_breadcrumbRenderers == null)
            return;

        for (int i = 0; i < _breadcrumbRenderers.Length; i++)
        {
            if (_breadcrumbRenderers[i] != null)
                _breadcrumbRenderers[i].enabled = visible;
        }
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static Material GetLineMaterial()
    {
        if (s_lineMaterial != null)
            return s_lineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        s_lineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_lineMaterial;
    }
}
