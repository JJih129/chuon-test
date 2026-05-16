using UnityEngine;

/// <summary>
/// Attack-specific sword slash VFX playback data.
/// Use this to tune an asset-store slash prefab per attack without hardcoding positions in combat code.
/// </summary>
[CreateAssetMenu(
    fileName = "AttackSlashProfile",
    menuName = "Game/VFX/Attack Slash Profile")]
public sealed class AttackSlashProfile : ScriptableObject
{
    [Header("VFX Prefab")]
    [Tooltip("Sword Slash VFX prefab imported from the Asset Store or authored in the project.")]
    [SerializeField] GameObject slashPrefab;

    [Header("Spawn Offset")]
    [Tooltip("Local position offset from the Slash Spawn Point.")]
    [SerializeField] Vector3 localPositionOffset;

    [Tooltip("Additional local rotation offset applied after the attack direction rotation.")]
    [SerializeField] Vector3 localEulerOffset;

    [Tooltip("Slash VFX local scale. Use this to size each attack independently.")]
    [SerializeField] Vector3 localScale = Vector3.one;

    [Header("Playback")]
    [Tooltip("Time before the slash VFX returns to the pool.")]
    [Min(0.05f)]
    [SerializeField] float lifetime = 0.75f;

    [Tooltip("If true, the slash follows the slash spawn point. If false, it remains fixed in world space.")]
    [SerializeField] bool followOwner;

    public GameObject SlashPrefab => slashPrefab;
    public Vector3 LocalPositionOffset => localPositionOffset;
    public Vector3 LocalEulerOffset => localEulerOffset;
    public Vector3 LocalScale => localScale;
    public float Lifetime => lifetime;
    public bool FollowOwner => followOwner;
}
