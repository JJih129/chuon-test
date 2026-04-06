using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class TutorialZoneHighlightController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private TutorialZoneTrigger movementZone;
    [SerializeField] private TutorialZoneTrigger exitZone;
    [SerializeField] private Transform playerRoot;

    [Header("Layout")]
    [SerializeField] private Vector3 zoneOffset = new Vector3(0f, 0.035f, 0f);
    [SerializeField] private Vector2 zonePadding = new Vector2(0.45f, 0.45f);
    [SerializeField] private Vector3 fillThickness = new Vector3(1f, 0.010f, 1f);
    [SerializeField] private Vector3 rimThickness = new Vector3(1f, 0.014f, 1f);
    [SerializeField] private Vector3 rippleThickness = new Vector3(1f, 0.008f, 1f);

    [Header("Pulse")]
    [SerializeField, Min(0.1f)] private float pulseSpeed = 2.2f;
    [SerializeField, Min(0f)] private float fillScalePulse = 0.04f;
    [SerializeField, Min(0f)] private float rimScalePulse = 0.08f;
    [SerializeField, Range(0f, 1f)] private float fillAlpha = 0.12f;
    [SerializeField, Range(0f, 1f)] private float rimAlpha = 0.34f;
    [SerializeField, Range(0f, 1f)] private float rippleAlpha = 0.26f;
    [SerializeField, Min(0f)] private float rippleScaleBonus = 0.22f;
    [SerializeField, Min(0.1f)] private float proximityBoostDistance = 3.6f;
    [SerializeField, Min(0f)] private float proximityFillAlphaBonus = 0.08f;
    [SerializeField, Min(0f)] private float proximityRimAlphaBonus = 0.16f;
    [SerializeField, Min(0f)] private float proximityScaleBonus = 0.12f;
    [SerializeField, Min(0.05f)] private float zoneEnteredFlashDuration = 0.24f;
    [SerializeField, Min(1f)] private float exitPulseSpeedMultiplier = 1.22f;
    [SerializeField, Min(1f)] private float exitEmphasisMultiplier = 1.18f;
    [SerializeField, Min(1f)] private float exitAlphaMultiplier = 1.15f;
    [SerializeField, Min(0f)] private float exitColorWhiten = 0.18f;

    [Header("Colors")]
    [SerializeField] private Color movementColor = new Color(0.22f, 0.86f, 1f, 0.96f);
    [SerializeField] private Color exitColor = new Color(1.00f, 0.70f, 0.22f, 0.96f);

    TutorialZoneTrigger _activeZone;
    Collider _activeCollider;
    Color _activeColor;
    Transform _fillRoot;
    Transform _rimRoot;
    Transform _rippleRoot;
    Renderer _fillRenderer;
    Renderer _rimRenderer;
    Renderer _rippleRenderer;
    MaterialPropertyBlock _propertyBlock;
    bool _subscribed;
    float _zoneEnteredFlashEndRealtime;
    bool _isExitZone;

    static Material s_zoneMaterial;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        TutorialZoneTrigger runtimeMovementZone,
        TutorialZoneTrigger runtimeExitZone,
        Transform runtimePlayerRoot)
    {
        flowController = runtimeFlowController;
        movementZone = runtimeMovementZone;
        exitZone = runtimeExitZone;
        playerRoot = runtimePlayerRoot;

        EnsureBuilt();
        RefreshSubscriptions();
        SetActiveZone(null, default);
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
        SetActiveZone(null, default);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void LateUpdate()
    {
        if (_activeCollider == null)
            return;

        Bounds bounds = _activeCollider.bounds;
        Vector3 center = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + zoneOffset;
        Vector3 baseScale = new Vector3(
            Mathf.Max(0.8f, bounds.size.x + zonePadding.x),
            1f,
            Mathf.Max(0.8f, bounds.size.z + zonePadding.y));

        float effectivePulseSpeed = pulseSpeed * (_isExitZone ? exitPulseSpeedMultiplier : 1f);
        float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * effectivePulseSpeed) * 0.5f);
        float ripple = Mathf.Repeat(Time.unscaledTime * effectivePulseSpeed * 0.45f, 1f);
        float proximityFactor = ComputeProximityFactor(bounds);
        float flashFactor = ComputeFlashFactor();
        float emphasis = Mathf.Clamp01(Mathf.Max(proximityFactor, flashFactor));
        if (_isExitZone)
            emphasis = Mathf.Clamp01(emphasis * exitEmphasisMultiplier);
        float scaleBonus = proximityScaleBonus * emphasis;
        Color activeColor = _activeColor;
        if (_isExitZone && exitColorWhiten > 0f)
            activeColor = Color.Lerp(activeColor, Color.white, emphasis * exitColorWhiten);

        UpdateDisc(
            _fillRoot,
            _fillRenderer,
            center,
            Vector3.Scale(baseScale * (1f + (fillScalePulse * pulse) + scaleBonus), fillThickness),
            activeColor,
            Mathf.Clamp01((fillAlpha + (proximityFillAlphaBonus * emphasis)) * (_isExitZone ? exitAlphaMultiplier : 1f)));
        UpdateDisc(
            _rimRoot,
            _rimRenderer,
            center,
            Vector3.Scale(baseScale * (1f + (rimScalePulse * pulse) + (scaleBonus * 1.15f)), rimThickness),
            activeColor,
            Mathf.Clamp01((rimAlpha + (proximityRimAlphaBonus * emphasis)) * (_isExitZone ? exitAlphaMultiplier : 1f)));

        Color rippleColor = activeColor;
        rippleColor.a = Mathf.Clamp01((rippleAlpha + (proximityRimAlphaBonus * emphasis * 0.7f)) * (_isExitZone ? exitAlphaMultiplier : 1f) * (1f - ripple));
        UpdateDisc(
            _rippleRoot,
            _rippleRenderer,
            center,
            Vector3.Scale(baseScale * (1f + (ripple * (rippleScaleBonus + (scaleBonus * 1.4f)))), rippleThickness),
            rippleColor,
            rippleColor.a);
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (flowController == null)
            return;

        flowController.StepStarted += HandleStepStarted;
        flowController.StepCompleted += HandleStepCompleted;
        if (movementZone != null)
            movementZone.TriggerEntered += HandleZoneEntered;
        if (exitZone != null)
            exitZone.TriggerEntered += HandleZoneEntered;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed || flowController == null)
            return;

        flowController.StepStarted -= HandleStepStarted;
        flowController.StepCompleted -= HandleStepCompleted;
        if (movementZone != null)
            movementZone.TriggerEntered -= HandleZoneEntered;
        if (exitZone != null)
            exitZone.TriggerEntered -= HandleZoneEntered;
        _subscribed = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        if (step == null)
        {
            SetActiveZone(null, default);
            return;
        }

        switch (step.stepType)
        {
            case TutorialStepType.Movement:
                SetActiveZone(movementZone, movementColor);
                break;

            case TutorialStepType.Exit:
                SetActiveZone(exitZone, exitColor);
                break;

            default:
                SetActiveZone(null, default);
                break;
        }
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (step == null)
            return;

        if (step.stepType == TutorialStepType.Movement || step.stepType == TutorialStepType.Exit)
            SetActiveZone(null, default);
    }

    void SetActiveZone(TutorialZoneTrigger zone, Color color)
    {
        _activeZone = zone;
        _activeCollider = zone != null ? zone.GetComponent<Collider>() : null;
        _activeColor = color;
        _isExitZone = zone != null && ReferenceEquals(zone, exitZone);
        _zoneEnteredFlashEndRealtime = 0f;

        bool visible = _activeCollider != null;
        SetRendererVisible(_fillRenderer, visible);
        SetRendererVisible(_rimRenderer, visible);
        SetRendererVisible(_rippleRenderer, visible);

        if (visible)
            LateUpdate();
        else
            _isExitZone = false;
    }

    void HandleZoneEntered(TutorialZoneTrigger zone, Collider other)
    {
        if (zone == null || _activeZone == null || zone != _activeZone)
            return;

        if (playerRoot != null && other != null && other.transform.root != playerRoot.root)
            return;

        _zoneEnteredFlashEndRealtime = Time.realtimeSinceStartup + zoneEnteredFlashDuration;
    }

    void EnsureBuilt()
    {
        _propertyBlock ??= new MaterialPropertyBlock();
        if (_fillRoot == null)
            _fillRoot = EnsureDisc("ZoneFill", out _fillRenderer);
        if (_rimRoot == null)
            _rimRoot = EnsureDisc("ZoneRim", out _rimRenderer);
        if (_rippleRoot == null)
            _rippleRoot = EnsureDisc("ZoneRipple", out _rippleRenderer);

        SetRendererVisible(_fillRenderer, false);
        SetRendererVisible(_rimRenderer, false);
        SetRendererVisible(_rippleRenderer, false);
    }

    Transform EnsureDisc(string objectName, out Renderer renderer)
    {
        Transform existing = transform.Find(objectName);
        if (existing != null)
        {
            renderer = existing.GetComponent<Renderer>();
            ConfigureRenderer(renderer);
            return existing;
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        primitive.name = objectName;
        primitive.transform.SetParent(transform, false);

        Collider primitiveCollider = primitive.GetComponent<Collider>();
        if (primitiveCollider != null)
            Destroy(primitiveCollider);

        renderer = primitive.GetComponent<Renderer>();
        ConfigureRenderer(renderer);
        return primitive.transform;
    }

    void ConfigureRenderer(Renderer renderer)
    {
        if (renderer == null)
            return;

        renderer.sharedMaterial = GetZoneMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    void UpdateDisc(Transform disc, Renderer renderer, Vector3 position, Vector3 scale, Color color, float alpha)
    {
        if (disc == null || renderer == null)
            return;

        disc.position = position;
        disc.rotation = Quaternion.identity;
        disc.localScale = scale;

        Color resolvedColor = color;
        resolvedColor.a = alpha;
        _propertyBlock.Clear();
        _propertyBlock.SetColor("_Color", resolvedColor);
        _propertyBlock.SetColor("_BaseColor", resolvedColor);
        renderer.SetPropertyBlock(_propertyBlock);
    }

    static void SetRendererVisible(Renderer renderer, bool visible)
    {
        if (renderer == null)
            return;

        renderer.enabled = visible;
    }

    float ComputeProximityFactor(Bounds bounds)
    {
        if (playerRoot == null || proximityBoostDistance <= 0.01f)
            return 0f;

        Vector3 closestPoint = bounds.ClosestPoint(playerRoot.position);
        closestPoint.y = playerRoot.position.y;
        Vector3 playerPosition = playerRoot.position;
        playerPosition.y = closestPoint.y;
        float distance = Vector3.Distance(playerPosition, closestPoint);
        if (distance >= proximityBoostDistance)
            return 0f;

        float normalized = 1f - Mathf.Clamp01(distance / proximityBoostDistance);
        return normalized * normalized;
    }

    float ComputeFlashFactor()
    {
        if (_zoneEnteredFlashEndRealtime <= Time.realtimeSinceStartup || zoneEnteredFlashDuration <= 0.01f)
            return 0f;

        float remaining = _zoneEnteredFlashEndRealtime - Time.realtimeSinceStartup;
        return Mathf.Clamp01(remaining / zoneEnteredFlashDuration);
    }

    static Material GetZoneMaterial()
    {
        if (s_zoneMaterial != null)
            return s_zoneMaterial;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_zoneMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_zoneMaterial;
    }
}
