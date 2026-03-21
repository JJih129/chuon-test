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
    [SerializeField] private float comboChainBufferTime = 0.45f;
    [SerializeField, Range(0f, 0.25f)] private float comboLateGraceNormalized = 0.08f;
    [SerializeField] private float attackTransitionGraceSeconds = 0.08f;

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
    [SerializeField] private Transform playerRoot;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerLockOn playerLockOn;
    [SerializeField] private PerfectDodgeController perfectDodgeController;

    [Header("Perfect Dodge Attack Assist")]
    [SerializeField] private bool usePerfectDodgeAttackAssist = true;
    [SerializeField, Range(0f, 2f)] private float perfectDodgeAssistStopDistance = 0.2f;
    [SerializeField, Range(0f, 1f)] private float perfectDodgeAssistMinRange = 0f;
    [SerializeField] private bool snapFacingToPerfectDodgeTarget = true;

    [Header("▶ 안전장치")]
    [SerializeField] private float endAttackIfStuckSeconds = 2.0f;
    [SerializeField] private bool logAttackTimeoutFailsafe = false;

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
    private bool hasQueuedComboInput;
    private BufferedInput queuedComboInput;
    
    private float lastClickTime = -999f;
    private float lastAttackEndRT = -999f;

    private AttackData current;
    private bool inAttack = false;
    private bool movementLocked = false;

    // 타임아웃 체크용 (Time.time 기준)
    private float attackStartTime = 0f;
    private float lastAttackPlayTime = -999f;

    // 피격 상태 변수
    private bool inHit = false;
    private bool invulnerable = false;
    private float hitStateEndTime = 0f;
    private float hitStartTime = 0f;

    // 참조
    private PlayerMoveController moveController;
    private IInputBlocker inputBlocker;
    private PlayerReferences playerReferences;

    // ───────────────── 라이프사이클 ─────────────────
    void Awake()
    {
        playerReferences = GetComponent<PlayerReferences>();
        if (!animator) animator = playerReferences != null && playerReferences.MainAnimator != null
            ? playerReferences.MainAnimator
            : GetComponent<Animator>();
        if (!playerRoot) playerRoot = playerReferences != null && playerReferences.PlayerRoot != null
            ? playerReferences.PlayerRoot
            : transform;
        if (!characterController) characterController = GetComponent<CharacterController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!perfectDodgeController) perfectDodgeController = GetComponent<PerfectDodgeController>();
        RefreshWeaponHitbox();
        moveController = GetComponent<PlayerMoveController>();
        inputBlocker = GetComponent<IInputBlocker>();
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

        ClearBufferedInputs();

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

        if (IsInputBlocked())
        {
            ClearBufferedInputs();

            if (inAttack)
            {
                DisableAttackHitbox();
                EndAttack();
            }

            if (!string.IsNullOrEmpty(speedParam))
                animator.SetFloat(speedParam, 0f, 0.1f, Time.deltaTime);

            return;
        }

        // 1) 입력 버퍼링
        if (Input.GetMouseButtonDown(0) && Time.time - lastClickTime > minClickInterval)
        {
            lastClickTime = Time.time;
            BufferAttackInput(AttackInput.Light);
        }
        if (Input.GetMouseButtonDown(1) && Time.time - lastClickTime > minClickInterval)
        {
            lastClickTime = Time.time;
            BufferAttackInput(AttackInput.Heavy);
        }

        // 2) 로코모션 (공격 중 아닐 때만)
        if (!(inAttack && locomotionDuringAttack == false))
            DriveBaseLocomotion();

        // 3) 콤보 진행
        StepCombo();

        // 4) 공격 비상 타임아웃 (Time.time 기준)
        if (inAttack && endAttackIfStuckSeconds > 0f && (Time.time - attackStartTime) > endAttackIfStuckSeconds)
        {
            if (logAttackTimeoutFailsafe)
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

        if (!inAttack)
        {
            if (Time.time - lastAttackEndRT > comboResetTime) current = null;

            if (TryConsumeAnyBufferedInput(out var inp))
            {
                var start = (inp.type == AttackInput.Light) ? firstLight : firstHeavy;
                if (start) Play(start, 0f);
            }
            return;
        }

        if (!current)
            return;

        bool matchedAttackState = TryGetAttackProgress(layer, current, out float t);
        if (!matchedAttackState)
        {
            if (Time.time - lastAttackPlayTime < attackTransitionGraceSeconds || animator.IsInTransition(layer))
                return;

            EndAttack();
            return;
        }

        float comboWindowEnd = Mathf.Min(1.05f, Mathf.Max(current.cancelEnd, 0.95f) + comboLateGraceNormalized);
        if (t >= current.cancelStart && t <= comboWindowEnd)
        {
            if (TryPeekAnyBufferedInput(out var inp))
            {
                var nx = FindNext(current, inp.type);
                if (nx)
                {
                    ConsumePeekedBufferedInput();
                    Play(nx, 0.03f);
                    return;
                }
            }
        }

        if (t >= 0.99f && !animator.IsInTransition(layer) &&
            (Time.time - lastAttackPlayTime) >= attackTransitionGraceSeconds)
        {
            EndAttack();
        }
    }

    private void Play(AttackData data, float fade)
    {
        TryApplyPerfectDodgeAttackAssist();

        current = data;
        inAttack = true;

        attackStartTime = Time.time;
        lastAttackPlayTime = Time.time;

        SafeSetLayerWeight(actionLayerIndex, 1f);

        if (lockMovementWhileAttacking && !movementLocked)
            SetMoveLock(true);

        animator.speed = Mathf.Max(0.01f, data.playSpeed);
        if (fade <= 0f) animator.Play(data.stateName, data.animatorLayer, 0f);
        else            animator.CrossFadeInFixedTime(data.stateName, fade, data.animatorLayer, 0f);

        animator.applyRootMotion = true;
    }

    private void EndAttack()
    {
        inAttack = false;
        animator.speed = 1f;
        lastAttackEndRT = Time.time;

        SafeSetLayerWeight(actionLayerIndex, 0f);

        if (movementLocked) SetMoveLock(false);
        animator.applyRootMotion = false;
    }

    private void TryApplyPerfectDodgeAttackAssist()
    {
        if (!usePerfectDodgeAttackAssist || perfectDodgeController == null)
            return;

        if (!perfectDodgeController.TryConsumeAttackFollowUpTarget(out Transform target))
            return;

        target = ResolvePerfectDodgeAttackAssistTarget(target);
        if (target == null)
            return;

        Transform facingRoot = playerRoot != null ? playerRoot : transform;
        Vector3 targetPoint = GetPerfectDodgeAttackAssistTargetPoint(target);
        Vector3 direction = GetPerfectDodgeAttackAssistApproachDirection(target, targetPoint, facingRoot);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 destinationAnchor = GetPerfectDodgeAttackAssistFrontSurfacePoint(target, targetPoint, direction);
        Vector3 destination = destinationAnchor - direction * perfectDodgeAssistStopDistance;
        destination.y = transform.position.y;

        Vector3 moveDelta = destination - transform.position;
        moveDelta.y = 0f;
        if (moveDelta.sqrMagnitude > perfectDodgeAssistMinRange * perfectDodgeAssistMinRange)
            SnapPlayerToPerfectDodgeAssistDestination(destination);

        if (snapFacingToPerfectDodgeTarget)
        {
            Vector3 facingDirection = targetPoint - facingRoot.position;
            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude <= 0.0001f)
                facingDirection = direction;

            if (facingDirection.sqrMagnitude > 0.0001f)
                facingRoot.rotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
        }
    }

    private Transform ResolvePerfectDodgeAttackAssistTarget(Transform fallbackTarget)
    {
        if (fallbackTarget != null)
            return fallbackTarget;

        if (playerLockOn != null && playerLockOn.HasTarget)
            return playerLockOn.GetCurrentTarget();

        return null;
    }

    private Vector3 GetPerfectDodgeAttackAssistTargetPoint(Transform target)
    {
        Bounds combinedBounds = default;
        bool hasBounds = false;
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (!hasBounds)
            {
                combinedBounds = col.bounds;
                hasBounds = true;
                continue;
            }

            combinedBounds.Encapsulate(col.bounds);
        }

        return hasBounds ? combinedBounds.center : target.position;
    }

    private Vector3 GetPerfectDodgeAttackAssistApproachDirection(Transform target, Vector3 targetPoint, Transform facingRoot)
    {
        Vector3 direction = -target.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        direction = targetPoint - facingRoot.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        return GetPerfectDodgeAttackAssistFallbackDirection(target, facingRoot);
    }

    private Vector3 GetPerfectDodgeAttackAssistFrontSurfacePoint(Transform target, Vector3 targetPoint, Vector3 approachDirection)
    {
        Collider bestCollider = null;
        float bestSqrDistance = float.MaxValue;
        Vector3 sampleOrigin = targetPoint - approachDirection.normalized * 4f;
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            Vector3 surfacePoint = col.ClosestPoint(sampleOrigin);
            float sqrDistance = (surfacePoint - sampleOrigin).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                bestCollider = col;
            }
        }

        if (bestCollider != null)
        {
            Vector3 surfacePoint = bestCollider.ClosestPoint(sampleOrigin);
            if ((surfacePoint - sampleOrigin).sqrMagnitude > 0.0001f)
                return surfacePoint;

            return bestCollider.bounds.center;
        }

        return targetPoint;
    }

    private Vector3 GetPerfectDodgeAttackAssistFallbackDirection(Transform target, Transform facingRoot)
    {
        Vector3 direction = target.position - facingRoot.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        direction = target.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        direction = facingRoot.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        return Vector3.forward;
    }

    private void SnapPlayerToPerfectDodgeAssistDestination(Vector3 destination)
    {
        if (characterController != null && characterController.enabled)
        {
            characterController.enabled = false;
            transform.position = destination;
            characterController.enabled = true;
            return;
        }

        transform.position = destination;
    }

    private void BufferAttackInput(AttackInput inputType)
    {
        var buffered = new BufferedInput { time = Time.time, type = inputType };

        if (inAttack)
        {
            queuedComboInput = buffered;
            hasQueuedComboInput = true;
            return;
        }

        inputQueue.Enqueue(buffered);
    }

    private bool TryPeekAnyBufferedInput(out BufferedInput input)
    {
        PruneExpiredBufferedInputs();

        if (hasQueuedComboInput)
        {
            input = queuedComboInput;
            return true;
        }

        if (inputQueue.Count > 0)
        {
            input = inputQueue.Peek();
            return true;
        }

        input = default;
        return false;
    }

    private bool TryConsumeAnyBufferedInput(out BufferedInput input)
    {
        if (!TryPeekAnyBufferedInput(out input))
            return false;

        ConsumePeekedBufferedInput();
        return true;
    }

    private void ConsumePeekedBufferedInput()
    {
        if (hasQueuedComboInput)
        {
            hasQueuedComboInput = false;
            return;
        }

        if (inputQueue.Count > 0)
            inputQueue.Dequeue();
    }

    private void PruneExpiredBufferedInputs()
    {
        while (inputQueue.Count > 0 && Time.time - inputQueue.Peek().time > inputBufferTime)
            inputQueue.Dequeue();

        float comboBufferLifetime = Mathf.Max(inputBufferTime, comboChainBufferTime);
        if (hasQueuedComboInput && Time.time - queuedComboInput.time > comboBufferLifetime)
            hasQueuedComboInput = false;
    }

    private bool TryGetAttackProgress(int layer, AttackData attack, out float normalizedTime)
    {
        normalizedTime = 0f;
        if (!animator || attack == null)
            return false;

        var currentState = animator.GetCurrentAnimatorStateInfo(layer);
        if (currentState.IsName(attack.stateName))
        {
            normalizedTime = currentState.normalizedTime % 1f;
            return true;
        }

        if (animator.IsInTransition(layer))
        {
            var nextState = animator.GetNextAnimatorStateInfo(layer);
            if (nextState.IsName(attack.stateName))
            {
                normalizedTime = nextState.normalizedTime % 1f;
                return true;
            }
        }

        normalizedTime = currentState.normalizedTime % 1f;
        return false;
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
        RefreshWeaponHitbox();
        if (weaponHitbox != null) weaponHitbox.ActivateWindow();
    }

    // 애니메이션에서 호출 (공격 판정 끄기)
    public void DisableAttackHitbox()
    {
        RefreshWeaponHitbox();
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
        ClearBufferedInputs();

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
        ClearBufferedInputs();

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
        ClearBufferedInputs();

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

    private void ClearBufferedInputs()
    {
        inputQueue.Clear();
        hasQueuedComboInput = false;
    }

    private bool IsInputBlocked()
    {
        return inputBlocker != null && inputBlocker.IsBlocked;
    }

    private void RefreshWeaponHitbox()
    {
        if (playerReferences == null)
            playerReferences = GetComponent<PlayerReferences>();

        if (playerReferences == null)
            return;

        var preferredHitbox = playerReferences.PrimaryAttackHitbox;
        if (preferredHitbox != null && preferredHitbox.gameObject.activeInHierarchy)
            weaponHitbox = preferredHitbox;
    }
}
