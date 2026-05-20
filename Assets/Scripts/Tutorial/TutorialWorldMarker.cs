using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class TutorialWorldMarker : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform attachTarget;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.04f, 0f);

    [Header("Layout")]
    [SerializeField] private Vector3 ringScale = new Vector3(0.85f, 0.025f, 0.85f);
    [SerializeField] private Vector3 beamLocalPosition = new Vector3(0f, 0.55f, 0f);
    [SerializeField] private Vector3 beamScale = new Vector3(0.08f, 0.28f, 0.08f);
    [SerializeField] private Vector3 capLocalPosition = new Vector3(0f, 0.98f, 0f);
    [SerializeField] private Vector3 capScale = new Vector3(0.24f, 0.10f, 0.24f);

    [Header("Style")]
    [SerializeField] private Color markerColor = new Color(0.18f, 0.85f, 1f, 0.9f);

    Transform _ringRoot;
    Transform _beamRoot;
    Transform _capRoot;
    Renderer _ringRenderer;
    Renderer _beamRenderer;
    Renderer _capRenderer;
    MaterialPropertyBlock _propertyBlock;

    static Material s_markerMaterial;

    void Awake()
    {
        EnsureState();
        Reattach();
        ApplyLayout();
        ApplyColor(markerColor);
    }

    public void ConfigureRuntime(
        Transform target,
        Color color,
        Vector3 runtimeRingScale,
        Vector3 runtimeBeamLocalPosition,
        Vector3 runtimeBeamScale,
        Vector3 runtimeCapLocalPosition,
        Vector3 runtimeCapScale)
    {
        attachTarget = target;
        markerColor = color;
        ringScale = runtimeRingScale;
        beamLocalPosition = runtimeBeamLocalPosition;
        beamScale = runtimeBeamScale;
        capLocalPosition = runtimeCapLocalPosition;
        capScale = runtimeCapScale;

        EnsureState();
        Reattach();
        ApplyLayout();
        ApplyColor(markerColor);
    }

    public void SetMarkerColor(Color color)
    {
        EnsureState();
        markerColor = color;
        ApplyColor(markerColor);
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf == visible)
            return;

        gameObject.SetActive(visible);
    }

    void EnsureBuilt()
    {
        if (_ringRoot == null)
            _ringRoot = EnsurePrimitive("Ring", PrimitiveType.Cylinder, out _ringRenderer);
        if (_beamRoot == null)
            _beamRoot = EnsurePrimitive("Beam", PrimitiveType.Cube, out _beamRenderer);
        if (_capRoot == null)
            _capRoot = EnsurePrimitive("Cap", PrimitiveType.Cube, out _capRenderer);
    }

    void EnsureState()
    {
        _propertyBlock ??= new MaterialPropertyBlock();
        EnsureBuilt();
    }

    Transform EnsurePrimitive(string objectName, PrimitiveType primitiveType, out Renderer renderer)
    {
        Transform existing = transform.Find(objectName);
        if (existing != null)
        {
            renderer = existing.GetComponent<Renderer>();
            ConfigureRenderer(renderer);
            return existing;
        }

        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = objectName;
        primitive.layer = gameObject.layer;
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

        renderer.sharedMaterial = GetMarkerMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    void Reattach()
    {
        if (attachTarget == null)
            return;

        if (transform.parent == attachTarget)
            return;

        transform.SetParent(attachTarget, false);
    }

    void ApplyLayout()
    {
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        if (_ringRoot != null)
        {
            _ringRoot.localPosition = Vector3.zero;
            _ringRoot.localRotation = Quaternion.identity;
            _ringRoot.localScale = ringScale;
        }

        if (_beamRoot != null)
        {
            _beamRoot.localPosition = beamLocalPosition;
            _beamRoot.localRotation = Quaternion.identity;
            _beamRoot.localScale = beamScale;
        }

        if (_capRoot != null)
        {
            _capRoot.localPosition = capLocalPosition;
            _capRoot.localRotation = Quaternion.Euler(0f, 45f, 0f);
            _capRoot.localScale = capScale;
        }
    }

    void ApplyColor(Color color)
    {
        ApplyRendererColor(_ringRenderer, color);
        ApplyRendererColor(_beamRenderer, color);
        ApplyRendererColor(_capRenderer, color);
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

    static Material GetMarkerMaterial()
    {
        if (s_markerMaterial != null)
            return s_markerMaterial;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_markerMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_markerMaterial;
    }
}
