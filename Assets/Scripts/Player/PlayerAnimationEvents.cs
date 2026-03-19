using UnityEngine;

[DisallowMultipleComponent]
public class PlayerAnimationEvents : MonoBehaviour
{
    [Header("Attack hitboxes driven by animation events")]
    [SerializeField] private AttackHitbox[] attackHitboxes;
    [SerializeField] private PlayerReferences playerReferences;

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

    void AutoWire()
    {
        if (!playerReferences)
            playerReferences = GetComponent<PlayerReferences>() ?? GetComponentInParent<PlayerReferences>(true);

        if (HasUsableHitboxes(attackHitboxes))
            return;

        if (playerReferences != null && playerReferences.AttackHitboxes != null && playerReferences.AttackHitboxes.Length > 0)
            attackHitboxes = playerReferences.AttackHitboxes;
    }

    AttackHitbox GetHitbox(int index)
    {
        RefreshHitboxes();

        if (attackHitboxes == null || attackHitboxes.Length == 0)
            return null;

        if (index < 0 || index >= attackHitboxes.Length)
            return null;

        return attackHitboxes[index];
    }

    public void ActivateHitbox(int index)
    {
        var hitbox = GetHitbox(index);
        if (hitbox == null)
            return;

        hitbox.ActivateWindow();
    }

    public void DeactivateHitbox(int index)
    {
        var hitbox = GetHitbox(index);
        if (hitbox == null)
            return;

        hitbox.DeactivateWindow();
    }

    public void ActivateHitbox()
    {
        ActivateHitbox(0);
    }

    public void DeactivateHitbox()
    {
        DeactivateHitbox(0);
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

    void RefreshHitboxes()
    {
        if (!playerReferences)
            playerReferences = GetComponent<PlayerReferences>() ?? GetComponentInParent<PlayerReferences>(true);

        if (playerReferences == null)
            return;

        var preferredHitboxes = playerReferences.AttackHitboxes;
        if (HasUsableHitboxes(preferredHitboxes))
            attackHitboxes = preferredHitboxes;
    }
}
