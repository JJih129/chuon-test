using UnityEngine;

/// <summary>
/// Full VFX configuration for one attack.
/// Keeps weapon trail, sword slash, hit impact, and future feedback hooks out of combat logic.
/// </summary>
[CreateAssetMenu(
    fileName = "AttackVfxProfile",
    menuName = "Game/VFX/Attack VFX Profile")]
public sealed class AttackVfxProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Attack key called by Animation Events. Example: Attack_01, Attack_02.")]
    [SerializeField] string attackKey;

    [Header("Weapon Trail")]
    [Tooltip("Whether this attack should start the weapon trail when PlayAttackVfx is called.")]
    [SerializeField] bool useWeaponTrail = true;

    [Header("Sword Slash")]
    [Tooltip("Sword Slash playback profile for this attack.")]
    [SerializeField] AttackSlashProfile slashProfile;

    [Header("Hit Impact - Optional")]
    [Tooltip("Optional impact VFX prefab for a future hit-position hookup.")]
    [SerializeField] GameObject hitImpactPrefab;

    [Tooltip("Time before the impact VFX returns to the pool.")]
    [Min(0.05f)]
    [SerializeField] float hitImpactLifetime = 0.75f;

    [Header("Future Feedback Hooks")]
    [Tooltip("Future hook for a Hit Stop system. This profile only stores the request.")]
    [SerializeField] bool requestHitStop;

    [Tooltip("Future hook for a Camera Shake system. This profile only stores the request.")]
    [SerializeField] bool requestCameraShake;

    public string AttackKey => attackKey;
    public bool UseWeaponTrail => useWeaponTrail;
    public AttackSlashProfile SlashProfile => slashProfile;
    public GameObject HitImpactPrefab => hitImpactPrefab;
    public float HitImpactLifetime => hitImpactLifetime;
    public bool RequestHitStop => requestHitStop;
    public bool RequestCameraShake => requestCameraShake;
}
