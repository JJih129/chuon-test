using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float runSpeed = 7f;
    public float rotationSpeed = 10f;
    public Animator animator;
    public PlayerLockOn playerLockOn; // 연결(의존성)

    private CharacterController controller;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (!animator) animator = GetComponent<Animator>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
    }

    void Update()
    {
        if (GetComponent<PlayerComboAttack>()?.IsAttacking ?? false) return;
        if (GetComponent<PlayerDash>()?.IsDashing ?? false) return;

        HandleMovement();
    }

    void HandleMovement()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 inputDir = new Vector3(h, 0, v).normalized;
        float speed = inputDir.magnitude;
        animator.SetFloat("speed", speed);

        if (speed > 0.1f)
        {
            Vector3 moveDir;
            if (playerLockOn != null && playerLockOn.IsLockOn)
            {
                moveDir = playerLockOn.GetLockOnMoveDirection(h, v);
                controller.Move(moveDir * runSpeed * Time.deltaTime);

                // 락온 회전
                Vector3 lookDir = playerLockOn.GetLookDirection();
                if (lookDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * rotationSpeed);
            }
            else
            {
                Transform cam = Camera.main.transform;
                Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                Vector3 camRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                moveDir = camForward * v + camRight * h;
                controller.Move(moveDir * runSpeed * Time.deltaTime);

                if (moveDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * rotationSpeed);
            }
        }
    }
}
