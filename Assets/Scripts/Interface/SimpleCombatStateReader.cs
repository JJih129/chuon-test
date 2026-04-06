using UnityEngine;

[DisallowMultipleComponent]
public class SimpleCombatStateReader : MonoBehaviour, ICombatStateReader
{
    [Header("Fallback State Flags")]
    public bool inAir;
    public bool staggered;
    public bool attacking;
    public bool guarding;
    public bool dodging;

    [Header("Runtime Sources")]
    [SerializeField] PlayerActionCoordinator actionCoordinator;
    [SerializeField] CharacterController characterController;
    [SerializeField] PlayerCombatController combatController;
    [SerializeField] PlayerGuardController guardController;
    [SerializeField] PlayerDodgeController dodgeController;
    [SerializeField] PlayerHealth playerHealth;

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

    public bool IsInAir()
    {
        if (actionCoordinator != null)
            return actionCoordinator.IsInAir();

        if (characterController != null && characterController.enabled)
            return !characterController.isGrounded;

        return inAir;
    }

    public bool IsStaggered()
    {
        if (actionCoordinator != null)
            return actionCoordinator.IsStaggered();

        if (combatController != null && combatController.IsInHit)
            return true;

        if (guardController != null && guardController.IsGuardBroken)
            return true;

        if (playerHealth != null && (playerHealth.IsDead || playerHealth.IsStaggered))
            return true;

        return staggered;
    }

    public bool IsInHitState()
    {
        if (actionCoordinator != null)
            return actionCoordinator.IsInHitState();

        return combatController != null && combatController.IsInHit;
    }

    public bool IsAttacking()
    {
        if (actionCoordinator != null)
            return actionCoordinator.IsAttacking();

        if (combatController != null && combatController.IsAttacking)
            return true;

        if (guardController != null && guardController.IsAttacking)
            return true;

        return attacking;
    }

    public bool IsGuarding()
    {
        if (actionCoordinator != null)
            return actionCoordinator.IsGuarding();

        if (guardController != null)
            return guardController.IsGuarding;

        return guarding;
    }

    public bool IsGuardMovementActive()
    {
        if (actionCoordinator != null)
            return actionCoordinator.IsGuardMovementActive();

        if (guardController != null)
            return guardController.IsGuardMovementActive;

        return guarding;
    }

    public bool IsDodging()
    {
        if (actionCoordinator != null)
            return actionCoordinator.IsDodging();

        if (dodgeController != null)
            return dodgeController.IsDodging;

        return dodging;
    }

    void AutoWire()
    {
        if (actionCoordinator == null)
            actionCoordinator = GetComponent<PlayerActionCoordinator>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (combatController == null)
            combatController = GetComponent<PlayerCombatController>();

        if (guardController == null)
            guardController = GetComponent<PlayerGuardController>();

        if (dodgeController == null)
            dodgeController = GetComponent<PlayerDodgeController>();

        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();
    }
}
