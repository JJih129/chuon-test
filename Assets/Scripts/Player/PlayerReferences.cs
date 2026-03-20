using UnityEngine;

[DisallowMultipleComponent]
public class PlayerReferences : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private PlayerVisualRig visualRig;
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
        AutoWire();
    }

    void Awake()
    {
        AutoWire();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
            AutoWire();
    }
#endif

    public void SyncSerializedReferences()
    {
        AutoWire();
    }

    void AutoWire()
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
