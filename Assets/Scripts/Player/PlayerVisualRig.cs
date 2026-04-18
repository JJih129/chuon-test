using UnityEngine;

[DisallowMultipleComponent]
public class PlayerVisualRig : MonoBehaviour
{
    const string DefaultSwordObjectName = "Object002";
    const string DefaultHitboxName = "AttackHitboxProxy";

    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator mainAnimator;
    [SerializeField] private AttackHitbox[] attackHitboxes;

    public Transform VisualRoot => visualRoot ? visualRoot : transform;
    public Animator MainAnimator => mainAnimator;
    public AttackHitbox[] AttackHitboxes => FilterValidObjects(attackHitboxes);
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
        AutoWire(false);
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
        AutoWire(false);
    }

    void AutoWire(bool createMissingHitbox)
    {
        if (!visualRoot)
            visualRoot = transform;

        if (!mainAnimator)
            mainAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (createMissingHitbox)
            EnsureDefaultSwordHitbox();

        if (!HasValidObjects(attackHitboxes))
            attackHitboxes = GetComponentsInChildren<AttackHitbox>(true);

        attackHitboxes = FilterValidObjects(attackHitboxes);
    }

    void EnsureDefaultSwordHitbox()
    {
        Transform swordRoot = FindDeepChildByName(transform, DefaultSwordObjectName);
        if (swordRoot == null)
            return;

        Transform hitboxTransform = swordRoot.Find(DefaultHitboxName);
        if (hitboxTransform == null)
        {
            GameObject hitboxObject = new GameObject(DefaultHitboxName);
            hitboxObject.layer = swordRoot.gameObject.layer;
            hitboxTransform = hitboxObject.transform;
            hitboxTransform.SetParent(swordRoot, false);
        }

        hitboxTransform.localPosition = Vector3.zero;
        hitboxTransform.localRotation = Quaternion.identity;
        hitboxTransform.localScale = Vector3.one;

        BoxCollider boxCollider = hitboxTransform.GetComponent<BoxCollider>();
        if (boxCollider == null)
            boxCollider = hitboxTransform.gameObject.AddComponent<BoxCollider>();

        boxCollider.isTrigger = true;
        boxCollider.enabled = false;

        AttackHitbox hitbox = hitboxTransform.GetComponent<AttackHitbox>();
        if (hitbox == null)
            hitbox = hitboxTransform.gameObject.AddComponent<AttackHitbox>();

        ConfigureDefaultSwordHitbox(hitbox, boxCollider, swordRoot);
    }

    void ConfigureDefaultSwordHitbox(AttackHitbox hitbox, BoxCollider boxCollider, Transform swordRoot)
    {
        if (hitbox == null || boxCollider == null || swordRoot == null)
            return;

        Bounds localBounds;
        if (TryGetLocalMeshBounds(swordRoot, out localBounds))
        {
            Vector3 size = localBounds.size;
            Vector3 center = localBounds.center;
            int majorAxis = 0;
            if (size.y > size.x && size.y >= size.z)
                majorAxis = 1;
            else if (size.z > size.x && size.z >= size.y)
                majorAxis = 2;

            const float thicknessMin = 0.08f;
            const float lengthPadding = 0.08f;
            switch (majorAxis)
            {
                case 1:
                    size.x = Mathf.Max(size.x * 0.55f, thicknessMin);
                    size.z = Mathf.Max(size.z * 0.55f, thicknessMin);
                    size.y = Mathf.Max(size.y + lengthPadding, 0.18f);
                    break;
                case 2:
                    size.x = Mathf.Max(size.x * 0.55f, thicknessMin);
                    size.y = Mathf.Max(size.y * 0.55f, thicknessMin);
                    size.z = Mathf.Max(size.z + lengthPadding, 0.18f);
                    break;
                default:
                    size.y = Mathf.Max(size.y * 0.55f, thicknessMin);
                    size.z = Mathf.Max(size.z * 0.55f, thicknessMin);
                    size.x = Mathf.Max(size.x + lengthPadding, 0.18f);
                    break;
            }

            boxCollider.center = center;
            boxCollider.size = size;
        }
        else
        {
            boxCollider.center = new Vector3(0f, 0f, 0.28f);
            boxCollider.size = new Vector3(0.12f, 0.12f, 0.95f);
        }

        hitbox.baseDamage = 10f;
        hitbox.hitType = HitType.Normal;
        hitbox.canParry = true;
        hitbox.canPerfectDodge = true;
        hitbox.canGuard = true;
        hitbox.unblockable = false;
        hitbox.attackerRoot = transform;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        hitbox.hitLayers = enemyLayer >= 0 ? (1 << enemyLayer) : ~0;
        hitbox.ignoreTriggerColliders = true;
        hitbox.useOneShotWindow = true;
        hitbox.oneShotWindow = 0.2f;
        hitbox.hitEachReceiverOncePerActivation = true;
        hitbox.useExpandedHitDetection = true;
        hitbox.expandedPadding = 0.12f;
        hitbox.meshExpandedPaddingScale = 0.35f;
        hitbox.expandedScanInterval = 0.02f;
        hitbox.useSweepHitDetection = true;
        hitbox.sweepStepDistance = 0.12f;
        hitbox.maxSweepSubsteps = 6;
    }

    static Transform FindDeepChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child;

            Transform nested = FindDeepChildByName(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    static bool TryGetLocalMeshBounds(Transform target, out Bounds bounds)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            bounds = skinned.sharedMesh.bounds;
            return true;
        }

        bounds = default;
        return false;
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
