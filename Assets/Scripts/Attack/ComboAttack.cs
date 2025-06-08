// ComboAttack.cs
using UnityEngine;

public class ComboAttack : MonoBehaviour
{
    public Animator animator;
    public float[] attackDurations = { 0.7f, 0.8f, 0.85f, 0.9f }; // 각 애니 길이(초)
    public float returnDuration = 1.0f; // Return 애니 길이(초)
    public float inputBufferTime = 0.5f; // 입력 유예(초)

    private int comboStep = 0;
    private float comboTimer = 0f;
    private bool isAttacking = false;
    private bool isReturn = false;

    public bool IsAttacking => isAttacking || isReturn;

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && !isAttacking && !isReturn)
        {
            comboStep = 1;
            isAttacking = true;
            animator.SetTrigger("Attack1");
            comboTimer = attackDurations[0] + inputBufferTime;
        }
        else if (Input.GetMouseButtonDown(0) && isAttacking && !isReturn)
        {
            if (comboTimer > 0f && comboStep < 4)
            {
                comboStep++;
                animator.SetTrigger("Attack" + comboStep);
                comboTimer = attackDurations[comboStep - 1] + inputBufferTime;
            }
        }

        if (isAttacking && !isReturn)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f)
            {
                animator.SetTrigger("Return");
                isReturn = true;
                isAttacking = false;
                comboTimer = returnDuration;
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

    void ResetAllTriggers()
    {
        animator.ResetTrigger("Attack1");
        animator.ResetTrigger("Attack2");
        animator.ResetTrigger("Attack3");
        animator.ResetTrigger("Attack4");
        animator.ResetTrigger("Return");
    }
}