using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class TutorialFocusGuidanceController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private TutorialScreenTargetIndicator screenIndicator;
    [SerializeField] private Transform focusTarget;

    [Header("Layout")]
    [SerializeField] private Vector3 rootLocalOffset = new Vector3(0f, 0.04f, 0f);
    [SerializeField] private Vector3 upperRingPosition = new Vector3(0f, 1.15f, 0f);
    [SerializeField] private Vector3 upperRingScale = new Vector3(0.62f, 0.014f, 0.62f);

    [Header("Pulse")]
    [SerializeField, Min(0f)] private float ringPulseAmplitude = 0.12f;
    [SerializeField, Min(0f)] private float alphaPulseAmplitude = 0.18f;
    [SerializeField, Min(0.1f)] private float pulseSpeed = 3.8f;
    [SerializeField, Min(0f)] private float upperRingSpinSpeed = 56f;

    [Header("Camera Focus")]
    [SerializeField] private string cameraStepLabel = "\uc2dc\uc57c\u0020\ud655\ubcf4";
    [SerializeField] private Color cameraStepColor = new Color(0.24f, 0.88f, 1f, 0.92f);
    [SerializeField, Min(0.1f)] private float cameraOnScreenScale = 0.98f;
    [SerializeField, Min(0.1f)] private float cameraOffScreenScale = 1.16f;

    [Header("Lock On")]
    [SerializeField] private string lockOnStepLabel = "\ub77d\uc628";
    [SerializeField] private Color lockOnStepColor = new Color(1f, 0.78f, 0.18f, 0.94f);
    [SerializeField, Min(0.1f)] private float lockOnOnScreenScale = 1.04f;
    [SerializeField, Min(0.1f)] private float lockOnOffScreenScale = 1.2f;

    Transform _visualRoot;
    Transform _upperRing;
    Renderer _upperRingRenderer;
    MaterialPropertyBlock _propertyBlock;
    Color _activeColor;
    bool _active;
    bool _subscribed;

    static Material s_focusMaterial;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        TutorialScreenTargetIndicator runtimeScreenIndicator,
        Transform runtimeFocusTarget)
    {
        flowController = runtimeFlowController;
        screenIndicator = runtimeScreenIndicator;
        focusTarget = runtimeFocusTarget;

        EnsureBuilt();
        Reattach();
        RefreshSubscriptions();
        ApplyStep(null);
        if (flowController != null)
            ApplyStep(flowController.CurrentStep);
    }

    void Awake()
    {
        EnsureBuilt();
        Reattach();
    }

    void OnEnable()
    {
        EnsureBuilt();
        Reattach();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        SetVisible(false);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void LateUpdate()
    {
        if (!_active || _visualRoot == null)
            return;

        float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.5f);
        float ringScaleMultiplier = 1f + (ringPulseAmplitude * pulse);
        float alpha = Mathf.Clamp01(_activeColor.a - alphaPulseAmplitude + (alphaPulseAmplitude * pulse));

        if (_upperRing != null)
        {
            _upperRing.localScale = upperRingScale * (1f + (ringPulseAmplitude * 0.65f * (1f - pulse)));
            _upperRing.localRotation = Quaternion.Euler(0f, Time.unscaledTime * upperRingSpinSpeed, 0f);
        }

        Color currentColor = _activeColor;
        currentColor.a = alpha;
        ApplyColor(currentColor);
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (flowController == null)
            return;

        flowController.StepStarted += HandleStepChanged;
        flowController.StepCompleted += HandleStepCompleted;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed || flowController == null)
            return;

        flowController.StepStarted -= HandleStepChanged;
        flowController.StepCompleted -= HandleStepCompleted;
        _subscribed = false;
    }

    void HandleStepChanged(TutorialStepDefinition step)
    {
        ApplyStep(step);
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (step == null)
            return;

        if (step.stepType == TutorialStepType.CameraFocus || step.stepType == TutorialStepType.LockOn)
            SetVisible(false);
    }

    void ApplyStep(TutorialStepDefinition step)
    {
        if (step == null)
        {
            SetVisible(false);
            return;
        }

        switch (step.stepType)
        {
            case TutorialStepType.CameraFocus:
                ApplyStyle(cameraStepLabel, cameraStepColor, cameraOnScreenScale, cameraOffScreenScale);
                SetVisible(true);
                break;

            case TutorialStepType.LockOn:
                ApplyStyle(lockOnStepLabel, lockOnStepColor, lockOnOnScreenScale, lockOnOffScreenScale);
                SetVisible(true);
                break;

            default:
                SetVisible(false);
                break;
        }
    }

    void ApplyStyle(string label, Color color, float onScreenScale, float offScreenScale)
    {
        _activeColor = color;
        screenIndicator?.SetRuntimeStyle(label, color, onScreenScale, offScreenScale);
        ApplyColor(color);
    }

    void EnsureBuilt()
    {
        _propertyBlock ??= new MaterialPropertyBlock();
        if (_visualRoot != null)
            return;

        GameObject root = new GameObject("TutorialFocusVisualRoot");
        _visualRoot = root.transform;
        _visualRoot.SetParent(transform, false);

        _upperRing = CreatePrimitive("UpperRing", PrimitiveType.Cylinder, out _upperRingRenderer);

        _upperRing.localPosition = upperRingPosition;

        _visualRoot.localPosition = rootLocalOffset;
        _visualRoot.gameObject.SetActive(false);
    }

    void Reattach()
    {
        if (_visualRoot == null || focusTarget == null)
            return;

        if (_visualRoot.parent != focusTarget)
            _visualRoot.SetParent(focusTarget, false);

        _visualRoot.localPosition = rootLocalOffset;
        _visualRoot.localRotation = Quaternion.identity;
        _visualRoot.localScale = Vector3.one;

        if (_upperRing != null)
        {
            _upperRing.localPosition = upperRingPosition;
            _upperRing.localRotation = Quaternion.identity;
            _upperRing.localScale = upperRingScale;
        }
    }

    Transform CreatePrimitive(string objectName, PrimitiveType primitiveType, out Renderer renderer)
    {
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = objectName;
        primitive.transform.SetParent(_visualRoot, false);

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

        renderer.sharedMaterial = GetFocusMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    void ApplyColor(Color color)
    {
        ApplyRendererColor(_upperRingRenderer, color);
    }

    void ApplyRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        _propertyBlock.Clear();
        _propertyBlock.SetColor("_Color", color);
        _propertyBlock.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(_propertyBlock);
    }

    void SetVisible(bool visible)
    {
        _active = visible && focusTarget != null;
        if (_visualRoot != null)
            _visualRoot.gameObject.SetActive(_active);

        if (_active)
            LateUpdate();
    }

    static Material GetFocusMaterial()
    {
        if (s_focusMaterial != null)
            return s_focusMaterial;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_focusMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_focusMaterial;
    }
}
