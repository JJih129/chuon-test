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
    [SerializeField] Vector3 visualLocalPosition = Vector3.zero;
    [SerializeField] Vector3 visualLocalEuler = Vector3.zero;
    [SerializeField] Vector3 visualLocalScale = Vector3.one;

    GameObject _runtimeVisualInstance;

    public Transform VisualRoot => visualRoot;
    public Renderer PrimaryRenderer => primaryRenderer;
    public Transform LockPivot => lockPivot;
    public Transform FirePoint => firePoint;

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
            _runtimeVisualInstance.transform.localPosition = visualLocalPosition;
            _runtimeVisualInstance.transform.localRotation = Quaternion.Euler(visualLocalEuler);
            _runtimeVisualInstance.transform.localScale = visualLocalScale;
        }

        if (disableRootRenderersWhenUsingVisualPrefab && visualRoot == transform)
        {
            Renderer[] renderers = GetComponents<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }
        }
    }

    void ResolvePrimaryRenderer()
    {
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
}
