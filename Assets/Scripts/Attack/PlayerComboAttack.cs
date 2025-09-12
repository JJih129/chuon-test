using UnityEngine;

public class PlayerComboAttack : MonoBehaviour
{
    public Animator animator;
    public float[] attackDurations = { 0.7f, 0.8f, 0.85f, 0.9f };
    public float returnDuration = 1.0f;
    public float inputBufferTime = 0.5f;
    public float heavyAttackDuration = 1.2f;

    // ★ 무기에 달린 공격용 히트박스 오브젝트(콜라이더 오브젝트를 여기 연결!)
    public GameObject attackHitbox;

    private int comboStep = 0;
    private float comboTimer = 0f;
    private bool isAttacking = false;
    private bool isReturn = false;
    private bool canComboInput = false;
    private bool isHeavyAttacking = false;
    private float heavyAttackTimer = 0f;

    public bool IsAttacking => isAttacking || isReturn || isHeavyAttacking;

    void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (GetComponent<PlayerDash>()?.IsDashing ?? false) return;

        // === 강공격(우클릭) ===
        if (Input.GetMouseButtonDown(1) && !isHeavyAttacking && !isAttacking && !isReturn)
        {
            isHeavyAttacking = true;
            heavyAttackTimer = heavyAttackDuration;
            animator.SetTrigger("HeavyAttack");
        }

        // === 강공격 애니 종료 후 처리 ===
        if (isHeavyAttacking)
        {
            heavyAttackTimer -= Time.deltaTime;
            if (heavyAttackTimer <= 0f)
            {
                isHeavyAttacking = false;
                animator.ResetTrigger("HeavyAttack");
                EndAttackOrCombo();
            }
            return;
        }

        // === 콤보 시작(1타) ===
        if (Input.GetMouseButtonDown(0) && !isAttacking && !isReturn)
        {
            comboStep = 1;
            isAttacking = true;
            animator.SetTrigger("Attack1");
            comboTimer = attackDurations[0] + inputBufferTime;
            canComboInput = false;
        }
        // === 콤보 연계(2~4타) ===
        else if (Input.GetMouseButtonDown(0) && isAttacking && !isReturn && canComboInput && comboStep < 4)
        {
            comboStep++;
            animator.SetTrigger("Attack" + comboStep);
            comboTimer = attackDurations[comboStep - 1] + inputBufferTime;
            canComboInput = false;
        }

        // === 콤보 입력 유예 시간 관리 ===
        if (isAttacking && !isReturn)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f)
            {
                EndAttackOrCombo();
            }
        }
        else if (isReturn)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f)
            {
                isReturn = false;
                comboStep = 0;
                ResetAllTriggers();
            }
        }
    }

    // === 애니메이션 이벤트에서 호출 (히트박스 On/Off) ===
    public void EnableAttackHitbox()
    {
        if (attackHitbox != null)
            attackHitbox.SetActive(true); // 혹은 Collider.enabled = true
    }

    public void DisableAttackHitbox()
    {
        if (attackHitbox != null)
            attackHitbox.SetActive(false); // 혹은 Collider.enabled = false
    }

    // === 애니메이션 이벤트에서 호출 (콤보 입력창) ===
    public void EnableComboInput() => canComboInput = true;
    public void DisableComboInput() => canComboInput = false;

    // 공격/강공격 종료 시 이동 입력에 따라 Return 생략/실행
    void EndAttackOrCombo()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        bool isMoveInput = Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f;

        if (isMoveInput)
        {
            isAttacking = false;
            isReturn = false;
            comboStep = 0;
            ResetAllTriggers();

            float speed = new Vector3(h, 0, v).magnitude;
            if (animator) animator.SetFloat("speed", speed);

            var movement = GetComponent<PlayerMovement>();
            if (movement != null) movement.HandleMovementExternal();
        }
        else
        {
            animator.SetTrigger("Return");
            isReturn = true;
            isAttacking = false;
            comboTimer = returnDuration;
            canComboInput = false;
        }
    }

    void ResetAllTriggers()
    {
        animator.ResetTrigger("Attack1");
        animator.ResetTrigger("Attack2");
        animator.ResetTrigger("Attack3");
        animator.ResetTrigger("Attack4");
        animator.ResetTrigger("HeavyAttack");
        animator.ResetTrigger("Return");
    }
}
