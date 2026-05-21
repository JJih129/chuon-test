using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialEnemyVisualRig : MonoBehaviour
{
    const string DefaultVisualRootName = "VisualRoot";
    const string DefaultLockPivotName = "LockPivot";
    const string DefaultLockOnProxyName = "LockOnProxy";
    const string DefaultRuntimeVisualName = "RuntimeVisual";

    [SerializeField] Transform visualRoot;
    [SerializeField] GameObject visualPrefab;
    [SerializeField] Renderer primaryRenderer;
    [SerializeField] Transform lockPivot;
    [SerializeField] Transform firePoint;
    [SerializeField] Transform lockOnProxy;
    [SerializeField] bool instantiateVisualPrefabAtRuntime = true;
    [SerializeField] bool disableRootRenderersWhenUsingVisualPrefab = true;
    [SerializeField, Range(0f, 1f)] float lockPivotHeightBias = 0.6f;
    [SerializeField] bool createLockOnProxyCollider = true;
    [SerializeField, Min(0.2f)] float lockOnProxyRadius = 1.15f;
    [SerializeField] bool alignRuntimeVisualBottomToRoot = true;
    [SerializeField] float runtimeVisualGroundOffset = 0.02f;
    [SerializeField] Vector3 visualLocalPosition = Vector3.zero;
    [SerializeField] Vector3 visualLocalEuler = Vector3.zero;
    [SerializeField] Vector3 visualLocalScale = Vector3.one;

    GameObject _runtimeVisualInstance;

    public Transform VisualRoot => visualRoot;
    public Renderer PrimaryRenderer => primaryRenderer;
    public Transform LockPivot => lockPivot;
    public Transform FirePoint => firePoint;

    public void ConfigureRuntimeVisual(GameObject prefab, Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
    {
        if (prefab == null)
            return;

        if (visualPrefab != prefab && _runtimeVisualInstance != null)
        {
            if (Application.isPlaying)
                Destroy(_runtimeVisualInstance);
            else
                DestroyImmediate(_runtimeVisualInstance);

            _runtimeVisualInstance = null;
            primaryRenderer = null;
        }

        visualPrefab = prefab;
        instantiateVisualPrefabAtRuntime = true;
        disableRootRenderersWhenUsingVisualPrefab = true;
        visualLocalPosition = localPosition;
        visualLocalEuler = localEuler;
        visualLocalScale = localScale;
        primaryRenderer = null;
        EnsureSetup();
    }

    void Awake()
    {
        EnsureSetup();
    }

    void OnEnable()
    {
        EnsureSetup();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
            EnsureReferencesOnly();
    }
#endif

    public void EnsureSetup()
    {
        EnsureReferencesOnly();
        EnsureVisualInstance();
        ResolvePrimaryRenderer();
        AlignRuntimeVisualToGround();
        EnsureLockPivot();
        EnsureLockOnProxy();
    }

    void EnsureReferencesOnly()
    {
        if (visualRoot == null)
        {
            Transform existing = transform.Find(DefaultVisualRootName);
            if (existing != null)
                visualRoot = existing;
        }

        if (visualRoot == null)
            visualRoot = transform;

        if (firePoint == null)
        {
            Transform existingFirePoint = FindChildRecursive(transform, "FirePoint");
            if (existingFirePoint != null)
                firePoint = existingFirePoint;
        }

        if (lockPivot == null)
        {
            Transform existingLockPivot = transform.Find(DefaultLockPivotName);
            if (existingLockPivot != null)
                lockPivot = existingLockPivot;
        }

        if (lockOnProxy == null)
        {
            Transform existingLockOnProxy = transform.Find(DefaultLockOnProxyName);
            if (existingLockOnProxy != null)
                lockOnProxy = existingLockOnProxy;
        }

        ResolvePrimaryRenderer();
    }

    void EnsureVisualInstance()
    {
        if (!instantiateVisualPrefabAtRuntime || visualPrefab == null)
            return;

        if (_runtimeVisualInstance == null)
        {
            Transform existing = visualRoot.Find(DefaultRuntimeVisualName);
            if (existing != null)
                _runtimeVisualInstance = existing.gameObject;
        }

        if (_runtimeVisualInstance == null)
        {
            _runtimeVisualInstance = Instantiate(visualPrefab, visualRoot, false);
            _runtimeVisualInstance.name = DefaultRuntimeVisualName;
        }

        _runtimeVisualInstance.transform.localPosition = visualLocalPosition;
        _runtimeVisualInstance.transform.localRotation = Quaternion.Euler(visualLocalEuler);
        _runtimeVisualInstance.transform.localScale = visualLocalScale;
        ApplyLayerRecursively(_runtimeVisualInstance.transform, gameObject.layer);

        if (disableRootRenderersWhenUsingVisualPrefab && visualRoot == transform)
        {
            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && !renderer.transform.IsChildOf(_runtimeVisualInstance.transform))
                    renderer.enabled = false;
            }
        }
    }

    void AlignRuntimeVisualToGround()
    {
        if (!alignRuntimeVisualBottomToRoot || _runtimeVisualInstance == null)
            return;

        Renderer[] renderers = _runtimeVisualInstance.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds bounds = default;
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
            return;

        float targetBottomY = transform.position.y + runtimeVisualGroundOffset;
        float deltaY = targetBottomY - bounds.min.y;
        if (Mathf.Abs(deltaY) > 0.001f)
            _runtimeVisualInstance.transform.position += Vector3.up * deltaY;
    }

    void ResolvePrimaryRenderer()
    {
        if (_runtimeVisualInstance != null)
        {
            Renderer runtimeRenderer = _runtimeVisualInstance.GetComponentInChildren<Renderer>(true);
            if (runtimeRenderer != null)
            {
                primaryRenderer = runtimeRenderer;
                return;
            }
        }

        if (primaryRenderer != null)
            return;

        if (_runtimeVisualInstance != null)
            primaryRenderer = _runtimeVisualInstance.GetComponentInChildren<Renderer>(true);

        if (primaryRenderer == null && visualRoot != null)
            primaryRenderer = visualRoot.GetComponentInChildren<Renderer>(true);

        if (primaryRenderer == null)
            primaryRenderer = GetComponentInChildren<Renderer>(true);
    }

    void EnsureLockPivot()
    {
        if (lockPivot == null)
        {
            GameObject pivotObject = new GameObject(DefaultLockPivotName);
            lockPivot = pivotObject.transform;
            lockPivot.SetParent(transform, false);
        }

        Vector3 pivotPosition = transform.position + Vector3.up * 1.1f;
        if (primaryRenderer != null)
        {
            Bounds bounds = primaryRenderer.bounds;
            float y = Mathf.Lerp(bounds.center.y, bounds.max.y, lockPivotHeightBias);
            pivotPosition = new Vector3(bounds.center.x, y, bounds.center.z);
        }

        lockPivot.position = pivotPosition;
        lockPivot.rotation = Quaternion.identity;
    }

    void EnsureLockOnProxy()
    {
        if (!createLockOnProxyCollider)
            return;

        if (lockOnProxy == null)
        {
            GameObject proxyObject = new GameObject(DefaultLockOnProxyName);
            lockOnProxy = proxyObject.transform;
            lockOnProxy.SetParent(transform, false);
        }

        if (lockPivot != null)
            lockOnProxy.position = lockPivot.position;
        else
            lockOnProxy.position = transform.position + Vector3.up * 1.1f;

        lockOnProxy.rotation = Quaternion.identity;
        lockOnProxy.localScale = Vector3.one;
        lockOnProxy.gameObject.layer = gameObject.layer;

        SphereCollider proxyCollider = lockOnProxy.GetComponent<SphereCollider>();
        if (proxyCollider == null)
            proxyCollider = lockOnProxy.gameObject.AddComponent<SphereCollider>();

        proxyCollider.isTrigger = true;
        proxyCollider.radius = lockOnProxyRadius;
        proxyCollider.center = Vector3.zero;
    }

    static Transform FindChildRecursive(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == targetName)
                return children[i];
        }

        return null;
    }

    static void ApplyLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            ApplyLayerRecursively(root.GetChild(i), layer);
    }
}
