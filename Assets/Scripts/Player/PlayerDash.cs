using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    public float dashSpeed = 18f;
    public float dashDuration = 0.25f;
    public float dashCooldown = 1.0f;

    private CharacterController controller;
    private Vector3 dashDirection = Vector3.zero;
    private float dashTimer = 0f;
    private float dashCooldownTimer = 0f;
    public bool IsDashing { get; private set; }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        dashCooldownTimer -= Time.deltaTime;
        if (IsDashing)
        {
            controller.Move(dashDirection * dashSpeed * Time.deltaTime);
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f)
                IsDashing = false;
            return;
        }

        // 대시는 이동/공격 중엔 X
        if ((GetComponent<PlayerComboAttack>()?.IsAttacking ?? false)) return;

        if (Input.GetKeyDown(KeyCode.LeftShift) && dashCooldownTimer <= 0f)
        {
            Vector3 lastMove = GetComponent<PlayerMovement>()?.transform.forward ?? transform.forward;
            dashDirection = lastMove;
            IsDashing = true;
            dashTimer = dashDuration;
            dashCooldownTimer = dashCooldown;
        }
    }
}