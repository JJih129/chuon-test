public interface ICombatStateReader
{
    bool IsInAir();
    bool IsStaggered();
    bool IsInHitState();
    bool IsAttacking();
    bool IsGuarding();
    bool IsGuardMovementActive();
    bool IsDodging();
}
