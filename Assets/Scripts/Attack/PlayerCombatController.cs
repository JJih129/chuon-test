using System.Collections.Generic;
using UnityEngine;
using Combat; // AttackData / AttackInput / ComboNextEntry

[RequireComponent(typeof(Animator))]
public class PlayerCombatController : MonoBehaviour
{
    // ───────────────── 변수 헤더(인스펙터 한글 설명) ─────────────────
    [Header("▶ 입력 버퍼 허용 시간(초): 이 시간 내의 클릭은 다음 타로 연결")]
    [SerializeField] private float inputBufferTime = 0.25f;

    [Header("▶ 콤보 리셋 시간(초): 마지막 타 종료 후 이 시간 지나면 첫타로 초기화")]
    [SerializeField] private float comboResetTime = 0.6f;

    [Header("▶ 과입력 방지 최소 간격(초): 너무 빠른 중복 클릭 방지")]
    [SerializeField] private float minClickInterval = 0.05f;

    [Header("▶ 첫 타(대기/이동 상태에서 시작할 공격 데이터: 약/강)")]
    [SerializeField] private AttackData firstLight;
    [SerializeField] private AttackData firstHeavy;

    [Header("▶ 사용할 Animator (플레이어 루트 컴포넌트)")]
    [SerializeField] private Animator animator;

    [Header("▶ 액션(공격) 레이어 인덱스 (Animator Layers에서 Action의 Index)")]
    [SerializeField] private int actionLayerIndex = 1;

    [Header("▶ 히트(피격) 레이어 인덱스 (Animator Layers에서 Hit의 Index)")]
    [SerializeField] private int hitLayerIndex = 2;

    [Header("▶ 공격 중 이동 잠금 여부(TRUE면 공격 내내 이동 불가)")]
    [SerializeField] private bool lockMovementWhileAttacking = true;

    [Header("▶ Base 레이어 BlendTree 파라미터명(런/아이들용) - 예: speed 또는 Speed")]
    [SerializeField] private string speedParam = "speed";

    [Header("▶ 공격 중에도 Base 로코모션을 구동할지 (풀바디 공격이면 FALSE 권장)")]
    [SerializeField] private bool locomotionDuringAttack = false;

    [Header("▶ 공격 비상 타임아웃(초): 루프/이상상태 대비 강제 종료(0=미사용)")]
    [SerializeField] private float endAttackIfStuckSeconds = 2.0f;

    // ── 피격 관련 (히트/다운/사망) ───────────────────────────────────
    [Header("▶ 경미 피격 경직 시간(초): Hit_Light 유지 시간(0이면 애니 종료로 판정)")]
    [SerializeField] private float lightHitSeconds = 0.35f;

    [Header("▶ 강 피격 경직 시간(초): Hit_Heavy 유지 시간")]
    [SerializeField] private float heavyHitSeconds = 0.7f;

    [Header("▶ 다운 무적 시간(초): Knockdown 동안 무적(옵션)")]
    [SerializeField] private float knockdownInvulnSeconds = 1.2f;

    [Header("▶ 피격 상태 비상 타임아웃(초): 루프/이상상태 대비 강제 종료(0=미사용)")]
    [SerializeField] private float endHitIfStuckSeconds = 3.0f;

    [Header("▶ Hit 레이어 스테이트명 (Animator의 State 이름과 1:1)")]
    [SerializeField] private string hitLightState = "Hit_Light";
    [SerializeField] private string hitHeavyState = "Hit_Heavy";
    [SerializeField] private string knockdownState = "Knockdown";
    [SerializeField] private string deathState = "Death";
    // ────────────────────────────────────────────────────────────────

    // 외부에서 읽기용(이동/가드 등에서 사용)
    public bool IsAttacking => inAttack;
    public bool IsInHit => inHit;

    private struct BufferedInput { public float time; public AttackInput type; }
    private readonly Queue<BufferedInput> inputQueue = new Queue<BufferedInput>();
    private float lastClickTime = -999f;
    private float lastAttackEndRT = -999f;

    private AttackData current;
    private bool inAttack = false;
    private bool movementLocked = false;

    // 비상 타임아웃 타임스탬프
    private float attackStartRealtime = 0f;

    // 피격 상태
    private bool inHit = false;
    private bool invulnerable = false;
    private float hitStateEndRealtime = 0f;
    private float hitStartRealtime = 0f;

    // ───────────────── 라이프사이클 ─────────────────
    void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
        // 여기서는 레이어 가중치 건들지 않음 (컨트롤러 바인딩 전일 수 있음)
    }

    void Start()
    {
        TryInitLayerWeights(); // 컨트롤러가 바인딩된 뒤 안전 시점에 초기화
    }

    void OnDisable()
    {
        // 컨트롤러/레이어 없으면 조용히 종료
        if (!animator || animator.runtimeAnimatorController == null || animator.layerCount == 0)
            return;

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
        // 컨트롤러가 없거나 레이어가 아직 세팅 전이면 대기
        if (!animator || animator.runtimeAnimatorController == null || animator.layerCount == 0)
            return;

        // 0) 피격 중이면 모든 공격/로코모션 로직 차단
        if (inHit)
        {
            UpdateHitState();
            return;
        }

        // 1) 입력 버퍼
        if (Input.GetMouseButtonDown(0) && Time.time - lastClickTime > minClickInterval)
        { lastClickTime = Time.time; inputQueue.Enqueue(new BufferedInput { time = Time.time, type = AttackInput.Light }); }
        if (Input.GetMouseButtonDown(1) && Time.time - lastClickTime > minClickInterval)
        { lastClickTime = Time.time; inputQueue.Enqueue(new BufferedInput { time = Time.time, type = AttackInput.Heavy }); }

        // 2) 로코모션(원하면만) — 풀바디 공격이면 공격 중엔 꺼두는 걸 권장
        if (!(inAttack && locomotionDuringAttack == false))
            DriveBaseLocomotion();

        // 3) 콤보 진행
        StepCombo();

        // 4) 공격 비상 타임아웃
        if (inAttack && endAttackIfStuckSeconds > 0f && (Time.realtimeSinceStartup - attackStartRealtime) > endAttackIfStuckSeconds)
        {
            Debug.LogWarning("[Combat] Attack forced to end by timeout failsafe.");
            EndAttack();
        }
    }

    private void TryInitLayerWeights()
    {
        if (!animator || animator.runtimeAnimatorController == null || animator.layerCount == 0)
            return;

        SafeSetLayerWeight(actionLayerIndex, 0f); // 평상시 Base만 보이게
        SafeSetLayerWeight(hitLayerIndex, 0f);    // 피격 레이어도 기본 0
    }

    // ───────────────── 콤보(공격) 루프 ─────────────────
    private void StepCombo()
    {
        int layer = current ? current.animatorLayer : actionLayerIndex;
        var st = animator.GetCurrentAnimatorStateInfo(layer);
        float t = st.normalizedTime % 1f;

        // A) 대기/이동 → 첫 타
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

        // B) 공격 중: 캔슬창 입력 시 즉시 분기
        if (current)
        {
            if (t >= current.cancelStart && t <= current.cancelEnd)
            {
                if (TryConsume(out var inp))
                {
                    var nx = FindNext(current, inp.type);
                    if (nx) { Play(nx, 0.05f); return; }
                }
            }
            // C) 모션 종료 시 명확히 끝냄
            if (t >= 0.99f)
                EndAttack();
        }
    }

    private void Play(AttackData data, float fade)
    {
        current = data;
        inAttack = true;
        attackStartRealtime = Time.realtimeSinceStartup;

        // ▶ 공격 진입: 액션 레이어 ON(=1)
        SafeSetLayerWeight(actionLayerIndex, 1f);

        // ▶ 이동 잠금 + Base Speed 0으로 눌러주기
        if (lockMovementWhileAttacking && !movementLocked)
            SetMoveLock(true);

        animator.speed = Mathf.Max(0.01f, data.playSpeed);
        if (fade <= 0f) animator.Play(data.stateName, data.animatorLayer, 0f);
        else            animator.CrossFadeInFixedTime(data.stateName, fade, data.animatorLayer, 0f);
    }

    private void EndAttack()
    {
        inAttack = false;
        animator.speed = 1f;
        lastAttackEndRT = Time.time;

        // ▶ 공격 종료: 액션 레이어 OFF(=0)
        SafeSetLayerWeight(actionLayerIndex, 0f);

        // ▶ 이동 잠금 해제 → PlayerMovement가 다시 speed를 밀어 자연 복귀
        if (movementLocked) SetMoveLock(false);
    }

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

    // === 애니메이션 이벤트(타격 프레임)에서 호출 ===
    public void AE_Hit()
    {
        // 무기 히트/데미지/VFX/SFX/게이지 수급 위치
        // GetComponentInChildren<WeaponHitbox>()?.DoHit(current ? current.baseDamage : 0f);
    }

    // ───────────────── 피격 시스템 ─────────────────
    // 외부(피해 처리)에서 호출: 경미/강 피격
    public void ApplyHit(bool heavy)
    {
        if (invulnerable) return;

        // 공격 중이면 끊음
        if (inAttack) EndAttack();

        inHit = true;
        hitStartRealtime = Time.realtimeSinceStartup;

        // ▶ 레이어 가중치: Hit=1, Action=0 (피격이 최우선 표시)
        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);

        // ▶ 이동 잠금
        SetMoveLock(true);

        string state = heavy ? hitHeavyState : hitLightState;
        float keepSec = heavy ? heavyHitSeconds : lightHitSeconds;

        // 재생
        animator.CrossFadeInFixedTime(state, 0.05f, hitLayerIndex, 0f);

        // 유지 시간 세팅(0이면 애니 정상 종료 판단으로 넘김)
        hitStateEndRealtime = (keepSec > 0f) ? (Time.realtimeSinceStartup + keepSec) : 0f;
    }

    // 외부에서 호출: 다운
    public void ApplyKnockdown()
    {
        if (invulnerable) return;
        if (inAttack) EndAttack();

        inHit = true;
        invulnerable = true; // 다운 동안 무적(옵션)
        hitStartRealtime = Time.realtimeSinceStartup;
        hitStateEndRealtime = Time.realtimeSinceStartup + knockdownInvulnSeconds;

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        animator.CrossFadeInFixedTime(knockdownState, 0.05f, hitLayerIndex, 0f);
    }

    // 외부에서 호출: 사망
    public void ApplyDeath()
    {
        // 한 번만
        if (inHit && animator.GetCurrentAnimatorStateInfo(hitLayerIndex).IsName(deathState)) return;

        inHit = true;
        inAttack = false;
        invulnerable = true;

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        animator.speed = 1f;
        animator.CrossFadeInFixedTime(deathState, 0.05f, hitLayerIndex, 0f);

        // 사망은 유지(EndHit 호출 X)
        hitStateEndRealtime = 0f;
    }

    private void UpdateHitState()
    {
        // 비상 타임아웃
        if (endHitIfStuckSeconds > 0f && (Time.realtimeSinceStartup - hitStartRealtime) > endHitIfStuckSeconds)
        {
            Debug.LogWarning("[Combat] Hit state forced to end by timeout failsafe.");
            EndHit();
            return;
        }

        // 1) 유지 시간이 지정된 경우(경직/다운)
        if (hitStateEndRealtime > 0f && Time.realtimeSinceStartup >= hitStateEndRealtime)
        {
            EndHit();
            return;
        }

        // 2) 유지 시간 미지정이면 애니 정규화 시간으로 감시(루프 OFF 전제)
        var st = animator.GetCurrentAnimatorStateInfo(hitLayerIndex);
        float t = st.normalizedTime % 1f;

        // Death는 유지
        if (st.IsName(deathState)) return;

        // Hit_Light/Hit_Heavy/Knockdown이 끝났으면 복귀
        if (t >= 0.99f && !animator.IsInTransition(hitLayerIndex))
        {
            EndHit();
        }
    }

    private void EndHit()
    {
        inHit = false;
        invulnerable = false;

        // ▶ Hit 레이어 OFF, 이동 잠금 해제
        SafeSetLayerWeight(hitLayerIndex, 0f);
        SetMoveLock(false);
    }

    // ───────────────── 유틸/브리지 ─────────────────
    private void DriveBaseLocomotion()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        float mag01 = Mathf.Clamp01(new Vector2(h, v).magnitude);

        if (inAttack && lockMovementWhileAttacking) mag01 = 0f;
        if (inHit) mag01 = 0f;

        if (animator.HasParameterOfType(speedParam, AnimatorControllerParameterType.Float))
        {
            int sp = Animator.StringToHash(speedParam);
            animator.SetFloat(sp, mag01, 0.1f, Time.deltaTime);
        }
        // 없으면 조용히 무시(해시 에러 방지)
    }

    private void SetMoveLock(bool locked)
    {
        movementLocked = locked;

        var mv = GetComponent<PlayerMovement>();
        if (mv) mv.SetExternalControl(locked);

        // Base BlendTree 즉시 0으로 눌러 화면상 미끄러짐 방지
        if (locked && animator.HasParameterOfType(speedParam, AnimatorControllerParameterType.Float))
        {
            int sp = Animator.StringToHash(speedParam);
            float damp = 0.05f;
            float dt = Time.deltaTime > 0 ? Time.deltaTime : 0.016f;
            animator.SetFloat(sp, 0f, damp, dt);
        }
    }

    private void SafeSetLayerWeight(int layerIndex, float weight01)
    {
        if (!animator) return;
        if (animator.runtimeAnimatorController == null) return;
        int count = animator.layerCount;
        if (count <= 0) return;
        if (layerIndex < 0 || layerIndex >= count) return;

        animator.SetLayerWeight(layerIndex, Mathf.Clamp01(weight01));
    }
}
