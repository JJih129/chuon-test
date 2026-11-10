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
        if (characterController == null || !characterController.enabled || characterController.isGrounded)
            return false;
        // 반복 14A: CharacterController.isGrounded 는 마지막 Move 결과라, 제자리에 서 있거나(이동 잠금·공격 중) 적 콜라이더 모서리에
        // 걸친 프레임엔 false 로 남는다 → 튜토리얼 빌드에서 궁극기가 "Player is airborne" 로 수 초~계속 거부됨 (회복도 같은 판정).
        // 발밑 0.3 m 안에 지면이 있으면 지상으로 본다 (실제 낙하/공중은 그대로 공중).
        return !HasGroundBelow();
    }

    static readonly RaycastHit[] s_groundHits = new RaycastHit[8];
    int _groundProbeFrame = -1;
    bool _groundProbeResult;

    bool HasGroundBelow()
    {
        if (_groundProbeFrame == Time.frameCount)
            return _groundProbeResult;
        _groundProbeFrame = Time.frameCount;

        Transform t = characterController.transform;
        float radius = Mathf.Max(0.05f, characterController.radius * 0.9f);
        Vector3 center = t.TransformPoint(characterController.center);
        float halfHeight = Mathf.Max(radius, characterController.height * 0.5f * Mathf.Abs(t.lossyScale.y));
        Vector3 origin = center + Vector3.down * (halfHeight - radius - 0.05f);
        float distance = 0.1f + characterController.skinWidth + 0.3f;
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, s_groundHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        _groundProbeResult = false;
        for (int i = 0; i < count; i++)
        {
            Collider c = s_groundHits[i].collider;
            if (c == null || c == characterController || c.transform.IsChildOf(t.root))
                continue;
            _groundProbeResult = true;
            break;
        }
        return _groundProbeResult;
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
