public interface ICombatStateReader
{
    bool IsInAir();
    bool IsStaggered();
    bool IsAttacking();
    bool IsGuarding();
    bool IsDodging();
}
