using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerActionCoordinator : MonoBehaviour, ICombatStateReader
{
    [Header("Runtime Sources")]
    [SerializeField] CharacterController characterController;
    [SerializeField] PlayerCombatController combatController;
    [SerializeField] PlayerGuardController guardController;
    [SerializeField] PlayerDodgeController dodgeController;
    [SerializeField] PlayerHealth playerHealth;

    public bool IsInAir()
    {
        return characterController != null
            && characterController.enabled
            && !characterController.isGrounded;
    }

    public bool IsStaggered()
    {
        if (combatController != null && combatController.IsInHit)
            return true;

        if (guardController != null && guardController.IsGuardBroken)
            return true;

        if (playerHealth != null && (playerHealth.IsDead || playerHealth.IsStaggered))
            return true;

        return false;
    }

    public bool IsInHitState()
    {
        return combatController != null && combatController.IsInHit;
    }

    public bool IsAttacking()
    {
        if (combatController != null && combatController.IsAttacking)
            return true;

        if (guardController != null && guardController.IsAttacking)
            return true;

        return false;
    }

    public bool IsGuarding()
    {
        return guardController != null && guardController.IsGuarding;
    }

    public bool IsGuardMovementActive()
    {
        return guardController != null && guardController.IsGuardMovementActive;
    }

    public bool IsDodging()
    {
        return dodgeController != null && dodgeController.IsDodging;
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

    void AutoWire()
    {
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
