using System.Collections.Generic;
using UnityEngine;
using Combat; // AttackData 등이 들어있는 네임스페이스 (없으면 지우세요)

[RequireComponent(typeof(Animator))]
public class PlayerCombatController : MonoBehaviour
{
    // ───────────────── 변수 헤더 ─────────────────
    [Header("▶ 입력 설정")]
    [SerializeField] private float inputBufferTime = 0.25f;
    [SerializeField] private float comboResetTime = 0.6f;
    [SerializeField] private float minClickInterval = 0.05f;

    [Header("▶ 콤보 데이터")]
    [SerializeField] private AttackData firstLight;
    [SerializeField] private AttackData firstHeavy;

    [Header("▶ 애니메이터 설정")]
    [SerializeField] private Animator animator;
    [SerializeField] private int actionLayerIndex = 1;
    [SerializeField] private int hitLayerIndex = 2;

    [Header("▶ 이동 제어")]
    [SerializeField] private bool lockMovementWhileAttacking = true;
    [SerializeField] private bool locomotionDuringAttack = false;
    [SerializeField] private string speedParam = "speed";

    [Header("▶ 안전장치")]
    [SerializeField] private float endAttackIfStuckSeconds = 2.0f;

    // ── 피격 관련 ───────────────────────────────────
    [Header("▶ 피격 설정")]
    [SerializeField] private float lightHitSeconds = 0.35f;
    [SerializeField] private float heavyHitSeconds = 0.7f;
    [SerializeField] private float knockdownInvulnSeconds = 1.2f;
    [SerializeField] private float endHitIfStuckSeconds = 3.0f;

    [Header("▶ 스테이트 이름")]
    [SerializeField] private string hitLightState = "Hit_Light";
    [SerializeField] private string hitHeavyState = "Hit_Heavy";
    [SerializeField] private string knockdownState = "Knockdown";
    [SerializeField] private string deathState = "Death";

    // ── 외부 확인용 프로퍼티 ─────────────────────────
    public bool IsAttacking => inAttack;
    public bool IsInHit => inHit;

    // ── 내부 변수 ───────────────────────────────────
    private struct BufferedInput { public float time; public AttackInput type; }
    private readonly Queue<BufferedInput> inputQueue = new Queue<BufferedInput>();
    
    private float lastClickTime = -999f;
    private float lastAttackEndRT = -999f;

    private AttackData current;
    private bool inAttack = false;
    private bool movementLocked = false;

    // 타임아웃 체크용 (Time.time 기준)
    private float attackStartTime = 0f;

    // 피격 상태 변수
    private bool inHit = false;
    private bool invulnerable = false;
    private float hitStateEndTime = 0f;
    private float hitStartTime = 0f;

    // 참조
    private PlayerMoveController moveController;

    // ───────────────── 라이프사이클 ─────────────────
    void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
        moveController = GetComponent<PlayerMoveController>();
    }

    void Start()
    {
        TryInitLayerWeights();
        movementLocked = false;
        
        // 시작 시 애니메이션 루트 모션 끄기 (이동 스크립트와 충돌 방지)
        if (animator) animator.applyRootMotion = false;
    }

    void OnDisable()
    {
        if (!animator) return;

        if (movementLocked) SetMoveLock(false);
        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 0f);
        animator.speed = 1f;

        inAttack = false;
        inHit = false;
        invulnerable = false;
    }

    void Update()
    {
        // ★ [핵심] 일시정지 중이면 업데이트 중단 (타임아웃 방지)
        if (Time.timeScale == 0f) return;

        if (!animator) return;

        // 0) 피격 중이면 로직 차단
        if (inHit)
        {
            UpdateHitState();
            return;
        }

        // 1) 입력 버퍼링
        if (Input.GetMouseButtonDown(0) && Time.time - lastClickTime > minClickInterval)
        { 
            lastClickTime = Time.time; 
            inputQueue.Enqueue(new BufferedInput { time = Time.time, type = AttackInput.Light }); 
        }
        if (Input.GetMouseButtonDown(1) && Time.time - lastClickTime > minClickInterval)
        { 
            lastClickTime = Time.time; 
            inputQueue.Enqueue(new BufferedInput { time = Time.time, type = AttackInput.Heavy }); 
        }

        // 2) 로코모션 (공격 중 아닐 때만)
        if (!(inAttack && locomotionDuringAttack == false))
            DriveBaseLocomotion();

        // 3) 콤보 진행
        StepCombo();

        // 4) 공격 비상 타임아웃 (Time.time 기준)
        if (inAttack && endAttackIfStuckSeconds > 0f && (Time.time - attackStartTime) > endAttackIfStuckSeconds)
        {
            Debug.LogWarning("[Combat] Attack forced to end by timeout failsafe.");
            EndAttack();
        }
    }

    private void TryInitLayerWeights()
    {
        if (!animator) return;
        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 0f);
    }

    // ───────────────── 콤보 로직 ─────────────────
    private void StepCombo()
    {
        int layer = current ? current.animatorLayer : actionLayerIndex;
        var st = animator.GetCurrentAnimatorStateInfo(layer);
        float t = st.normalizedTime % 1f;

        // A) 대기 상태 -> 첫 타
        if (!inAttack)
        {
            if (Time.time - lastAttackEndRT > comboResetTime) current = null;

            if (TryConsume(out var inp))
            {
                var start = (inp.type == AttackInput.Light) ? firstLight : firstHeavy;
                if (start) Play(start, 0f);
            }
            return;
        }

        // B) 공격 중 (콤보 연결)
        if (current)
        {
            // 캔슬 구간 확인
            if (t >= current.cancelStart && t <= current.cancelEnd)
            {
                if (TryConsume(out var inp))
                {
                    var nx = FindNext(current, inp.type);
                    if (nx) { Play(nx, 0.05f); return; }
                }
            }
            // C) 모션 종료 확인
            if (t >= 0.99f)
                EndAttack();
        }
    }

    private void Play(AttackData data, float fade)
    {
        current = data;
        inAttack = true;
        
        // ★ 게임 시간 기준으로 기록 (일시정지 문제 해결)
        attackStartTime = Time.time;

        // 레이어 활성화
        SafeSetLayerWeight(actionLayerIndex, 1f);

        // 이동 잠금
        if (lockMovementWhileAttacking && !movementLocked)
            SetMoveLock(true);

        // 애니메이션 재생
        animator.speed = Mathf.Max(0.01f, data.playSpeed);
        if (fade <= 0f) animator.Play(data.stateName, data.animatorLayer, 0f);
        else            animator.CrossFadeInFixedTime(data.stateName, fade, data.animatorLayer, 0f);
        
        // ★ 공격 시 루트 모션 켜기 (전진 공격 등을 위해)
        animator.applyRootMotion = true;
    }

    private void EndAttack()
    {
        inAttack = false;
        animator.speed = 1f;
        lastAttackEndRT = Time.time;

        SafeSetLayerWeight(actionLayerIndex, 0f);

        if (movementLocked) SetMoveLock(false);
        
        // ★ 공격 끝나면 루트 모션 끄기 (이동 스크립트 복귀)
        animator.applyRootMotion = false;
    }

    // ───────────────── 입력 처리 ─────────────────
    private bool TryConsume(out BufferedInput input)
    {
        while (inputQueue.Count > 0 && Time.time - inputQueue.Peek().time > inputBufferTime)
            inputQueue.Dequeue();

        if (inputQueue.Count > 0) { input = inputQueue.Dequeue(); return true; }
        input = default; return false;
    }

    private AttackData FindNext(AttackData from, AttackInput input)
    {
        if (from.nextByInput != null)
            for (int i = 0; i < from.nextByInput.Length; i++)
                if (from.nextByInput[i].input == input) return from.nextByInput[i].next;
        return null;
    }

    // ───────────────── 애니메이션 이벤트 ─────────────────
    [Header("▶ 무기 연결 (필수)")]
    [SerializeField] private AttackHitbox weaponHitbox; 

    // 애니메이션에서 호출 (공격 판정 켜기)
    public void EnableAttackHitbox()
    {
        if (weaponHitbox != null) weaponHitbox.ActivateWindow();
    }

    // 애니메이션에서 호출 (공격 판정 끄기)
    public void DisableAttackHitbox()
    {
        if (weaponHitbox != null) weaponHitbox.DeactivateWindow();
    }

    public void EnableComboInput() { /* 필요시 구현 */ }
    public void DisableComboInput() { /* 필요시 구현 */ }
    public void AE_Hit() { /* 타격음 등 필요시 구현 */ }

    // ───────────────── 피격 시스템 ─────────────────
    public void ApplyHit(bool heavy)
    {
        if (invulnerable) return;
        if (inAttack) EndAttack();

        inHit = true;
        hitStartTime = Time.time;

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        string state = heavy ? hitHeavyState : hitLightState;
        float keepSec = heavy ? heavyHitSeconds : lightHitSeconds;

        animator.CrossFadeInFixedTime(state, 0.05f, hitLayerIndex, 0f);
        hitStateEndTime = (keepSec > 0f) ? (Time.time + keepSec) : 0f;
    }

    public void ApplyKnockdown()
    {
        if (invulnerable) return;
        if (inAttack) EndAttack();

        inHit = true;
        invulnerable = true;
        hitStartTime = Time.time;
        hitStateEndTime = Time.time + knockdownInvulnSeconds;

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        animator.CrossFadeInFixedTime(knockdownState, 0.05f, hitLayerIndex, 0f);
    }

    public void ApplyDeath()
    {
        if (inHit && animator.GetCurrentAnimatorStateInfo(hitLayerIndex).IsName(deathState)) return;

        inHit = true;
        inAttack = false;
        invulnerable = true;

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        animator.speed = 1f;
        animator.CrossFadeInFixedTime(deathState, 0.05f, hitLayerIndex, 0f);
        hitStateEndTime = 0f;
    }

    private void UpdateHitState()
    {
        // 타임아웃 체크 (Time.time 기준)
        if (endHitIfStuckSeconds > 0f && (Time.time - hitStartTime) > endHitIfStuckSeconds)
        {
            Debug.LogWarning("[Combat] Hit state forced to end by timeout failsafe.");
            EndHit();
            return;
        }

        if (hitStateEndTime > 0f && Time.time >= hitStateEndTime)
        {
            EndHit();
            return;
        }

        var st = animator.GetCurrentAnimatorStateInfo(hitLayerIndex);
        if (st.IsName(deathState)) return;

        if (st.normalizedTime >= 0.99f && !animator.IsInTransition(hitLayerIndex))
        {
            EndHit();
        }
    }

    private void EndHit()
    {
        inHit = false;
        invulnerable = false;
        SafeSetLayerWeight(hitLayerIndex, 0f);
        SetMoveLock(false);
    }

    // ───────────────── 유틸리티 ─────────────────
    private void DriveBaseLocomotion()
    {
        // 간단한 블렌드 트리 제어 (PlayerMoveController와 별개로 애니메이션만 동기화)
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        float mag01 = Mathf.Clamp01(new Vector2(h, v).magnitude);

        if (inAttack && lockMovementWhileAttacking) mag01 = 0f;
        if (inHit) mag01 = 0f;

        animator.SetFloat(speedParam, mag01, 0.1f, Time.deltaTime);
    }

    private void SetMoveLock(bool locked)
    {
        movementLocked = locked;

        // ★ PlayerMoveController의 SetExternalControl 호출
        if (moveController == null) moveController = GetComponent<PlayerMoveController>();
        if (moveController != null) 
        {
            moveController.SetExternalControl(locked);
        }

        if (locked) animator.SetFloat(speedParam, 0f);
    }

    private void SafeSetLayerWeight(int layerIndex, float weight01)
    {
        if (!animator) return;
        if (layerIndex < 0 || layerIndex >= animator.layerCount) return;
        animator.SetLayerWeight(layerIndex, Mathf.Clamp01(weight01));
    }
}