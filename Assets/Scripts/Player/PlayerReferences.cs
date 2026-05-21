using UnityEngine;

[DisallowMultipleComponent]
public class PlayerReferences : MonoBehaviour
{
    const string DefaultForwardHitboxName = "RuntimeForwardAttackHitbox";

    [Header("Root")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private PlayerVisualRig visualRig;
    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private Transform lockPivot;
    [SerializeField] private Transform ultimateSpawnRoot;
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform weaponSocket;
    [SerializeField] private Transform vfxRoot;
    [SerializeField] private Transform uiAnchor;

    [Header("Visual")]
    [SerializeField] private Animator mainAnimator;
    [SerializeField] private AttackHitbox[] attackHitboxes;

    public Transform PlayerRoot => playerRoot ? playerRoot : transform;
    public Transform VisualRoot
    {
        get
        {
            if (visualRoot)
                return visualRoot;

            var resolvedRig = VisualRig;
            if (resolvedRig != null)
            {
                visualRoot = resolvedRig.VisualRoot;
                return visualRoot;
            }

            return null;
        }
    }
    public PlayerVisualRig VisualRig
    {
        get
        {
            if (visualRig == null)
                visualRig = ResolvePreferredVisualRig();

            return visualRig;
        }
    }
    public GameObject VisualPrefab => visualPrefab;
    public Transform LockPivot => lockPivot;
    public Transform UltimateSpawnRoot => ultimateSpawnRoot;
    public Transform CameraPivot => cameraPivot;
    public Transform WeaponSocket => weaponSocket;
    public Transform VFXRoot => vfxRoot;
    public Transform UIAnchor => uiAnchor;
    public Animator MainAnimator => mainAnimator ? mainAnimator : VisualRig ? VisualRig.MainAnimator : null;
    public AttackHitbox[] AttackHitboxes
    {
        get
        {
            var activeConfiguredHitboxes = FilterActiveHitboxes(attackHitboxes);
            if (activeConfiguredHitboxes != null)
                return activeConfiguredHitboxes;

            var activeChildHitboxes = FilterActiveHitboxes(GetComponentsInChildren<AttackHitbox>(true));
            if (activeChildHitboxes != null)
            {
                attackHitboxes = activeChildHitboxes;
                return activeChildHitboxes;
            }

            var preferredHitboxes = GetPreferredAttackHitboxes();
            var activePreferredHitboxes = FilterActiveHitboxes(preferredHitboxes);
            if (activePreferredHitboxes != null)
                return activePreferredHitboxes;

            if (HasValidObjects(preferredHitboxes))
                return FilterValidObjects(preferredHitboxes);

            if (HasValidObjects(attackHitboxes))
                return FilterValidObjects(attackHitboxes);

            return VisualRig ? VisualRig.AttackHitboxes : null;
        }
    }
    public AttackHitbox PrimaryAttackHitbox
    {
        get
        {
            var hitboxes = AttackHitboxes;
            return hitboxes != null && hitboxes.Length > 0 ? hitboxes[0] : null;
        }
    }

    void Reset()
    {
        AutoWire(false);
    }

    void Awake()
    {
        AutoWire(true);

        if (Application.isPlaying)
        {
            EnsureRuntimeVisualPrefabHierarchy();
            AutoWire(true);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
            AutoWire(false);
    }
#endif

    public void SyncSerializedReferences()
    {
        AutoWire(Application.isPlaying);
    }

    void EnsureRuntimeVisualPrefabHierarchy()
    {
        if (visualRoot == null || visualPrefab == null)
            return;

        var currentVisualInstance = FindCurrentVisualInstance();
        RemoveDuplicateVisualInstances(currentVisualInstance, false);
        if (IsRuntimeVisualPrefabMatching(currentVisualInstance))
            return;

        if (currentVisualInstance != null)
            Destroy(currentVisualInstance.gameObject);

        var instance = Object.Instantiate((Object)visualPrefab, visualRoot, false) as GameObject;
        if (instance == null)
        {
            Debug.LogWarning("[PlayerReferences] visualPrefab could not be instantiated as a GameObject.", this);
            return;
        }

        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        instance.name = visualPrefab.name;

        var rig = instance.GetComponent<PlayerVisualRig>() ?? instance.GetComponentInChildren<PlayerVisualRig>(true);
        if (rig == null)
            rig = instance.AddComponent<PlayerVisualRig>();

        if (rig != null)
        {
            rig.SyncSerializedReferences();
            visualRig = rig;
            if (rig.MainAnimator != null)
                mainAnimator = rig.MainAnimator;

            var rigHitboxes = rig.AttackHitboxes;
            if (rigHitboxes != null && rigHitboxes.Length > 0)
                attackHitboxes = rigHitboxes;
        }
    }

    bool IsRuntimeVisualPrefabMatching(Transform child)
    {
        if (visualRoot == null || visualPrefab == null || child == null)
            return false;

        if (!string.Equals(child.name, visualPrefab.name, System.StringComparison.Ordinal))
            return false;

        return child.GetComponent<PlayerVisualRig>() != null || child.GetComponentInChildren<PlayerVisualRig>(true) != null;
    }

#if UNITY_EDITOR
    public void SyncVisualPrefabHierarchyForEditor()
    {
        AutoWire(false);
        SyncVisualPrefabHierarchy();
    }
#endif

    void AutoWire(bool allowRuntimeCreate)
    {
        if (!playerRoot)
            playerRoot = transform;

        var explicitVisualRoot = transform.Find("VisualRoot");

        if (!lockPivot)
            lockPivot = transform.Find("LockPivot");

        if (!ultimateSpawnRoot)
            ultimateSpawnRoot = transform.Find("UltimateSpawnRoot");

        if (!cameraPivot)
            cameraPivot = transform.Find("CameraPivot");

        if (!weaponSocket)
            weaponSocket = transform.Find("WeaponSocket");

        if (!vfxRoot)
            vfxRoot = transform.Find("VFXRoot");

        if (!uiAnchor)
            uiAnchor = transform.Find("UIAnchor");

        if (explicitVisualRoot)
            visualRoot = explicitVisualRoot;
        else if (!visualRoot)
            visualRoot = FindLikelyVisualRoot();

        var preferredVisualRig = ResolvePreferredVisualRig();
        if (preferredVisualRig)
            visualRig = preferredVisualRig;

        if (!visualRoot && visualRig)
            visualRoot = visualRig.VisualRoot;

        if (!mainAnimator)
            mainAnimator = GetComponent<Animator>() ?? (visualRig ? visualRig.MainAnimator : null) ?? GetComponentInChildren<Animator>(true);

        var rootAttackHitbox = allowRuntimeCreate ? EnsureDefaultForwardHitbox() : null;
        if (rootAttackHitbox != null)
        {
            attackHitboxes = new[] { rootAttackHitbox };
            attackHitboxes = FilterValidObjects(attackHitboxes);
#if UNITY_EDITOR
            EnsureEditorVisualPrefabFallback();
#endif
            return;
        }

        var activeChildHitboxes = FilterActiveHitboxes(GetComponentsInChildren<AttackHitbox>(true));
        var preferredHitboxes = GetPreferredAttackHitboxes();
        if (!HasUsableHitboxes(attackHitboxes) && activeChildHitboxes != null)
        {
            attackHitboxes = activeChildHitboxes;
        }
        else if (preferredHitboxes != null && !HasHitboxesFromRig(attackHitboxes, visualRig))
        {
            attackHitboxes = preferredHitboxes;
        }
        else if (!HasUsableHitboxes(attackHitboxes))
        {
            if (preferredHitboxes != null)
                attackHitboxes = preferredHitboxes;
            else
                attackHitboxes = GetComponentsInChildren<AttackHitbox>(true);
        }

        attackHitboxes = FilterValidObjects(attackHitboxes);

        if ((attackHitboxes == null || attackHitboxes.Length == 0) && visualRig && visualRig.PrimaryAttackHitbox)
            attackHitboxes = new[] { visualRig.PrimaryAttackHitbox };

#if UNITY_EDITOR
        EnsureEditorVisualPrefabFallback();
#endif
    }

    AttackHitbox EnsureDefaultForwardHitbox()
    {
        if (playerRoot == null)
            return null;

        Transform hitboxTransform = null;
        for (int i = playerRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = playerRoot.GetChild(i);
            if (!string.Equals(child.name, DefaultForwardHitboxName, System.StringComparison.Ordinal))
                continue;

            if (hitboxTransform == null)
            {
                hitboxTransform = child;
                continue;
            }

            if (Application.isPlaying)
                Object.Destroy(child.gameObject);
            else
                Object.DestroyImmediate(child.gameObject);
        }

        if (hitboxTransform == null)
        {
            var go = new GameObject(DefaultForwardHitboxName);
            go.layer = playerRoot.gameObject.layer;
            hitboxTransform = go.transform;
            hitboxTransform.SetParent(playerRoot, false);
        }

        hitboxTransform.localPosition = new Vector3(0f, 1.0f, 1.05f);
        hitboxTransform.localRotation = Quaternion.identity;
        hitboxTransform.localScale = Vector3.one;

        BoxCollider boxCollider = hitboxTransform.GetComponent<BoxCollider>();
        if (boxCollider == null)
            boxCollider = hitboxTransform.gameObject.AddComponent<BoxCollider>();

        boxCollider.isTrigger = true;
        boxCollider.enabled = false;
        boxCollider.center = Vector3.zero;
        boxCollider.size = new Vector3(1.25f, 1.4f, 1.6f);

        AttackHitbox hitbox = hitboxTransform.GetComponent<AttackHitbox>();
        if (hitbox == null)
            hitbox = hitboxTransform.gameObject.AddComponent<AttackHitbox>();

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        hitbox.baseDamage = 10f;
        hitbox.hitType = HitType.Normal;
        hitbox.canParry = true;
        hitbox.canPerfectDodge = true;
        hitbox.canGuard = true;
        hitbox.unblockable = false;
        hitbox.attackerRoot = playerRoot;
        hitbox.hitLayers = enemyLayer >= 0 ? (1 << enemyLayer) : ~0;
        hitbox.ignoreTriggerColliders = true;
        hitbox.useOneShotWindow = true;
        hitbox.oneShotWindow = 0.2f;
        hitbox.hitEachReceiverOncePerActivation = true;
        hitbox.useExpandedHitDetection = true;
        hitbox.useSweepHitDetection = false;
        hitbox.expandedPadding = 0.08f;
        hitbox.expandedScanInterval = 0.02f;
        return hitbox;
    }

#if UNITY_EDITOR
    const string DefaultVisualPrefabPath = "Assets/Prefabs/Generated/PlayerVisual_Chuon.prefab";
    static bool s_SyncingVisualPrefab;

    void EnsureEditorVisualPrefabFallback()
    {
        if (Application.isPlaying || visualRoot == null || visualPrefab != null)
            return;

        var fallback = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DefaultVisualPrefabPath);
        if (fallback == null)
            return;

        visualPrefab = fallback;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    void SyncVisualPrefabHierarchy()
    {
        if (Application.isPlaying || visualRoot == null)
            return;
        if (visualPrefab == null)
            return;
        if (s_SyncingVisualPrefab)
            return;

        s_SyncingVisualPrefab = true;
        try
        {
            var currentVisualInstance = FindCurrentVisualInstance();
            RemoveDuplicateVisualInstances(currentVisualInstance, true);
            if (IsCurrentVisualPrefabMatching(currentVisualInstance))
                return;

            ReplaceVisualHierarchy(currentVisualInstance);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }
        finally
        {
            s_SyncingVisualPrefab = false;
        }
    }

    bool IsCurrentVisualPrefabMatching(Transform child)
    {
        if (visualRoot == null || child == null)
            return false;

        var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) as GameObject;
        if (source != visualPrefab)
            return false;

        if (!string.Equals(child.name, visualPrefab.name, System.StringComparison.Ordinal))
            return false;

        return child.GetComponent<PlayerVisualRig>() != null || child.GetComponentInChildren<PlayerVisualRig>(true) != null;
    }

    void ReplaceVisualHierarchy(Transform currentVisualInstance)
    {
        if (currentVisualInstance != null)
            UnityEngine.Object.DestroyImmediate(currentVisualInstance.gameObject);

        var instance = InstantiateEditorVisualPrefabClone();
        if (instance == null)
            return;

        instance.transform.SetParent(visualRoot, false);

        var instanceTransform = instance.transform;
        instanceTransform.localPosition = Vector3.zero;
        instanceTransform.localRotation = Quaternion.identity;
        instanceTransform.localScale = Vector3.one;
        instance.name = visualPrefab.name;

        var rig = instance.GetComponent<PlayerVisualRig>() ?? instance.GetComponentInChildren<PlayerVisualRig>(true);
        if (rig == null)
            rig = instance.AddComponent<PlayerVisualRig>();

        if (rig != null)
        {
            rig.SyncSerializedReferences();
            visualRig = rig;
            if (rig.MainAnimator != null)
                mainAnimator = rig.MainAnimator;

            var rigRoot = rig.VisualRoot;
            if (rigRoot != null && visualRoot == null)
                visualRoot = rigRoot;

            var rigHitboxes = rig.AttackHitboxes;
            if (rigHitboxes != null && rigHitboxes.Length > 0)
                attackHitboxes = rigHitboxes;

            UnityEditor.EditorUtility.SetDirty(instance);
            UnityEditor.EditorUtility.SetDirty(rig);
        }
    }

    GameObject InstantiateEditorVisualPrefabClone()
    {
        if (visualPrefab == null)
            return null;

        var assetPath = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visualPrefab);
        if (string.IsNullOrWhiteSpace(assetPath))
            assetPath = UnityEditor.AssetDatabase.GetAssetPath(visualPrefab);

        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        var prefabContentsRoot = UnityEditor.PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            return UnityEngine.Object.Instantiate(prefabContentsRoot);
        }
        finally
        {
            UnityEditor.PrefabUtility.UnloadPrefabContents(prefabContentsRoot);
        }
    }
#endif

    Transform FindCurrentVisualInstance()
    {
        if (visualRoot == null)
            return null;

        var rigs = visualRoot.GetComponentsInChildren<PlayerVisualRig>(true);
        if (rigs != null)
        {
            foreach (var rig in rigs)
            {
                if (rig == null)
                    continue;

                if (rig.transform.parent == visualRoot)
                    return rig.transform;
            }
        }

        for (var i = 0; i < visualRoot.childCount; i++)
        {
            var child = visualRoot.GetChild(i);
            if (child == null)
                continue;

            if (child.GetComponent<Renderer>() != null || child.GetComponentInChildren<Renderer>(true) != null)
                return child;
        }

        return null;
    }

    void RemoveDuplicateVisualInstances(Transform keep, bool immediate)
    {
        if (visualRoot == null)
            return;

        for (int i = visualRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = visualRoot.GetChild(i);
            if (child == null || child == keep)
                continue;

            bool isVisualInstance =
                child.GetComponent<PlayerVisualRig>() != null ||
                child.GetComponentInChildren<PlayerVisualRig>(true) != null ||
                string.Equals(child.name, visualPrefab != null ? visualPrefab.name : string.Empty, System.StringComparison.Ordinal);

            if (!isVisualInstance)
                continue;

            if (immediate)
                Object.DestroyImmediate(child.gameObject);
            else
                Object.Destroy(child.gameObject);
        }
    }

    Transform FindLikelyVisualRoot()
    {
        foreach (Transform child in transform)
        {
            if (child == lockPivot || child == ultimateSpawnRoot)
                continue;

            if (child.GetComponent<Animator>() != null)
                return child;

            if (child.GetComponentInChildren<Renderer>(true) != null)
                return child;
        }

        return null;
    }

    PlayerVisualRig ResolvePreferredVisualRig()
    {
        if (!visualRoot)
            return visualRig && visualRig.gameObject.activeInHierarchy ? visualRig : null;

        var rigs = visualRoot.GetComponentsInChildren<PlayerVisualRig>(true);
        if (rigs == null || rigs.Length == 0)
            return visualRig && visualRig.gameObject.activeInHierarchy ? visualRig : null;

        foreach (var rig in rigs)
        {
            if (!rig)
                continue;

            if (rig.gameObject.activeInHierarchy)
                return rig;
        }

        return visualRig && visualRig.gameObject.activeInHierarchy ? visualRig : null;
    }

    AttackHitbox[] GetPreferredAttackHitboxes()
    {
        if (!visualRig)
            return null;

        var activeHitboxes = FilterActiveHitboxes(visualRig.AttackHitboxes);
        if (activeHitboxes != null)
            return activeHitboxes;

        return FilterValidObjects(visualRig.AttackHitboxes);
    }

    static bool HasValidObjects<T>(T[] objects) where T : Object
    {
        if (objects == null || objects.Length == 0)
            return false;

        foreach (var obj in objects)
        {
            if (obj)
                return true;
        }

        return false;
    }

    static bool HasUsableHitboxes(AttackHitbox[] hitboxes)
    {
        if (hitboxes == null || hitboxes.Length == 0)
            return false;

        foreach (var hitbox in hitboxes)
        {
            if (hitbox && hitbox.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    static bool HasHitboxesFromRig(AttackHitbox[] hitboxes, PlayerVisualRig rig)
    {
        if (rig == null || hitboxes == null || hitboxes.Length == 0)
            return false;

        foreach (var hitbox in hitboxes)
        {
            if (!hitbox || !hitbox.gameObject.activeInHierarchy)
                continue;

            if (hitbox.transform.IsChildOf(rig.transform))
                return true;
        }

        return false;
    }

    static AttackHitbox[] FilterActiveHitboxes(AttackHitbox[] hitboxes)
    {
        if (hitboxes == null || hitboxes.Length == 0)
            return null;

        var validCount = 0;
        foreach (var hitbox in hitboxes)
        {
            if (hitbox && hitbox.gameObject.activeInHierarchy)
                validCount++;
        }

        if (validCount == 0)
            return null;

        var filtered = new AttackHitbox[validCount];
        var index = 0;
        foreach (var hitbox in hitboxes)
        {
            if (!hitbox || !hitbox.gameObject.activeInHierarchy)
                continue;

            filtered[index++] = hitbox;
        }

        return filtered;
    }

    static T[] FilterValidObjects<T>(T[] objects) where T : Object
    {
        if (objects == null || objects.Length == 0)
            return null;

        var validCount = 0;
        foreach (var obj in objects)
        {
            if (obj)
                validCount++;
        }

        if (validCount == 0)
            return null;

        if (validCount == objects.Length)
            return objects;

        var filtered = new T[validCount];
        var index = 0;
        foreach (var obj in objects)
        {
            if (!obj)
                continue;

            filtered[index++] = obj;
        }

        return filtered;
    }
}
