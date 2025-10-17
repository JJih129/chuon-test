using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    // ========== 변수 헤더 (한글 설명) ==========
    // runSpeed        : 이동 속도
    // rotationSpeed   : 회전 보간 속도
    // animator        : 플레이어 애니메이터 (Move 파라미터 사용)
    // playerLockOn    : 락온 상태에서의 이동 보조 (있으면 연결)
    // ===========================================
    public float runSpeed = 7f;
    public float rotationSpeed = 10f;
    public Animator animator;
    public MonoBehaviour playerLockOn; // PlayerLockOn 타입이 프로젝트에 있을 경우 컴포넌트로 연결

    private CharacterController controller;
    private bool externalControlLocked = false; // 외부에서 이동 잠금용 플래그

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (!animator) animator = GetComponent<Animator>();
        if (playerLockOn == null)
        {
            // PlayerLockOn이 존재하면 자동 할당 시도 (안전성)
            var pl = GetComponentInChildren<MonoBehaviour>();
            playerLockOn = pl;
        }
    }

    void Update()
    {
        // 외부에서 이동 잠금되어 있으면 입력 무시
        if (externalControlLocked) return;

        // 공격(콤보 or 강공격) 중이거나 대시 중엔 이동 불가 처리는
        // 기존 프로젝트의 다른 스크립트에서 판단하므로 여기서는 기본 동작 유지.
        HandleMovement();
    }

    void HandleMovement()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 inputDir = new Vector3(h, 0, v).normalized;
        float speed = inputDir.magnitude;
        if (animator) animator.SetFloat("speed", speed);

        if (speed > 0.1f)
        {
            Vector3 moveDir;
            // 락온 컴포넌트이름이 다르면 프로젝트에 맞게 연결하세요.
            var lockOnComp = playerLockOn;
            if (lockOnComp != null && lockOnComp.GetType().Name == "PlayerLockOn")
            {
                // PlayerLockOn의 API 이름이 다를 수 있음. 프로젝트 함수명에 맞춰 수정 필요.
                // 여기서는 원래 코드의 의도대로 'GetLockOnMoveDirection'와 'GetLookDirection' 호출을 가정.
                var methodMove = lockOnComp.GetType().GetMethod("GetLockOnMoveDirection");
                var methodLook = lockOnComp.GetType().GetMethod("GetLookDirection");
                if (methodMove != null && methodLook != null)
                {
                    object moveDirObj = methodMove.Invoke(lockOnComp, new object[] { h, v });
                    if (moveDirObj is Vector3 md) moveDir = md;
                    else moveDir = new Vector3(h, 0, v);
                    controller.Move(moveDir * runSpeed * Time.deltaTime);

                    object lookDirObj = methodLook.Invoke(lockOnComp, null);
                    if (lookDirObj is Vector3 lookDir && lookDir.sqrMagnitude > 0.01f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * rotationSpeed);
                    }
                }
                else
                {
                    // 안전 fallback
                    Transform cam = Camera.main.transform;
                    Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                    Vector3 camRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                    moveDir = camForward * v + camRight * h;
                    controller.Move(moveDir * runSpeed * Time.deltaTime);
                    if (moveDir.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * rotationSpeed);
                }
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

    // 기존에 프로젝트에서 호출하던 시그니처(무난한 no-arg)를 유지.
    public void HandleMovementExternal()
    {
        // 기존 코드에서 사용하던 외부 호출 호환용 자리. 필요하면 프로젝트 호출 형태에 맞게 확장.
        // 현재는 입력 기반 이동이 기본이므로 여기는 비워둠.
    }

    // 외부(애니/가드 등)에서 이동을 잠그거나 해제할 수 있도록 API 제공.
    public void SetExternalControl(bool locked)
    {
        externalControlLocked = locked;
        if (locked && animator) animator.SetFloat("speed", 0f);
    }
}
