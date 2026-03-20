using UnityEngine;

[DisallowMultipleComponent]
public class PlayerVisualRig : MonoBehaviour
{
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
        if (!visualRoot)
            visualRoot = transform;

        if (!mainAnimator)
            mainAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (!HasValidObjects(attackHitboxes))
            attackHitboxes = GetComponentsInChildren<AttackHitbox>(true);

        attackHitboxes = FilterValidObjects(attackHitboxes);
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
