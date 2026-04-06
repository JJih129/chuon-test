using UnityEngine;

/// <summary>
/// Shared payload for melee, boss attacks, and projectiles.
/// </summary>
public struct HitPayload
{
    public float damage;
    public HitType hitType;
    public Vector3 hitPoint;
    public Vector3 hitDirection;
    public Transform attacker;
    public int attackSequenceId;
    public bool canParry;
    public bool canPerfectDodge;
    public bool canGuard;
    public bool causesGuardBreak;
    public bool unblockable;
}

/// <summary>
/// Minimal damage receiver interface used by hitboxes and projectiles.
/// </summary>
public interface IDamageReceiver
{
    void ReceiveHit(HitPayload payload);
}
