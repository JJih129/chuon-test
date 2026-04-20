using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat; // AttackData 등이 들어있는 네임스페이스 (없으면 지우세요)

[RequireComponent(typeof(Animator))]
public class PlayerCombatController : MonoBehaviour
{
    // ───────────────── 변수 헤더 ─────────────────
    [Header("▶ 입력 설정")]
    [SerializeField] private float inputBufferTime = 0.25f;
    [SerializeField] private float comboResetTime = 0.45f;
    [SerializeField] private float minClickInterval = 0.05f;
    [SerializeField] private float comboChainBufferTime = 0.28f;
    [SerializeField, Range(0f, 0.25f)] private float comboLateGraceNormalized = 0.08f;
    [SerializeField] private float attackTransitionGraceSeconds = 0.05f;
    [SerializeField, Range(0f, 0.2f)] private float dodgeCancelEarlyBufferNormalized = 0.04f;

    [Header("▶ 데이터 기반 히트 윈도우")]
    [SerializeField] private bool useDataDrivenHitWindows = true;
    [SerializeField] private bool useFallbackWindowFromHitTime = true;
    [SerializeField, Range(0f, 0.2f)] private float fallbackHitWindowLeadNormalized = 0.04f;
    [SerializeField, Range(0f, 0.3f)] private float fallbackHitWindowTailNormalized = 0.12f;

    [Header("▶ 콤보 데이터")]
    [SerializeField] private AttackData firstLight;
    [SerializeField] private AttackData firstHeavy;
    [SerializeField] private float fallbackAttackBaseDamage = 10f;
    [SerializeField] private float lightAttackDamageMultiplier = 1f;
    [SerializeField] private float heavyAttackDamageMultiplier = 1.35f;
    [SerializeField] private float lightComboStepBonus = 0.07f;
    [SerializeField] private float heavyComboStepBonus = 0.09f;

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
    [SerializeField, Range(0f, 2f)] private float perfectDodgeAssistSideOffset = 0.55f;
    [SerializeField] private bool dashPerfectDodgeAttackAssist = true;
    [SerializeField, Range(0.04f, 0.35f)] private float perfectDodgeAssistDashDurationRealtime = 0.14f;
    [SerializeField, Range(180f, 2160f)] private float perfectDodgeAssistDashRotationSpeed = 1440f;
    [SerializeField] private bool leaveAfterImageDuringPerfectDodgeAssistDash = true;
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
    [SerializeField] private bool useAnimationDrivenHitRecovery = true;
    [SerializeField, Range(0.01f, 0.2f)] private float hitStateForcePlayDelay = 0.08f;

    [Header("▶ 스테이트 이름")]
    [SerializeField] private string hitLightState = "Hit_Light";
    [SerializeField] private string hitHeavyState = "Hit_Heavy";
    [SerializeField] private string knockdownState = "Knockdown";
    [SerializeField] private string deathState = "Death";

    // ── 외부 확인용 프로퍼티 ─────────────────────────
    public bool IsAttacking => inAttack;
    public bool IsInHit => inHit;
    public int CurrentComboDepth => _currentComboDepth;
    public AttackInput? CurrentAttackInput => _hasCurrentAttackInput ? _currentAttackInput : null;

    public event Action<AttackInput, AttackData, int> OnAttackStarted;
    public event Action<AttackInput, AttackData, int> OnAttackEnded;

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
    private AttackInput _currentAttackInput;
    private bool _hasCurrentAttackInput;
    private int _currentComboDepth;
    private int _currentAttackSequenceId;
    private int _activeAttackHitWindowIndex = NoActiveAttackHitWindow;

    private const int NoActiveAttackHitWindow = -1;
    private const int FallbackAttackHitWindowIndex = -2;

    // 타임아웃 체크용 (Time.time 기준)
    private float attackStartTime = 0f;
    private float lastAttackPlayTime = -999f;

    // 피격 상태 변수
    private bool inHit = false;
    private bool invulnerable = false;
    private float hitStateEndTime = 0f;
    private float hitStartTime = 0f;
    private string activeHitStateName = string.Empty;
    private int activeHitStateHash;
    private int activeHitStateShortNameHash;
    private bool waitingForHitStateEntry = false;
    private float hitStateRequestedAt = 0f;
    private bool forcedHitStatePlay = false;
    private bool deathLocked = false;

    // 참조
    private PlayerMoveController moveController;
    private IInputBlocker inputBlocker;
    private PlayerReferences playerReferences;
    private PlayerInputCommandBuffer inputCommandBuffer;
    private PerfectDodgeAfterImageEffect perfectDodgeAfterImageEffect;
    private PlayerAttackVfxPresenter attackVfxPresenter;
    private Coroutine perfectDodgeAssistDashRoutine;
    private AttackHitbox _cachedWeaponHitboxDefaultsSource;
    private bool _cachedWeaponHitboxDefaults;
    private float _defaultWeaponHitboxDamage;
    private HitType _defaultWeaponHitboxType;
    private int _defaultWeaponHitboxAttackSequenceId;
    private bool _defaultWeaponHitboxCanParry;
    private bool _defaultWeaponHitboxCanPerfectDodge;
    private bool _defaultWeaponHitboxUnblockable;
    private Transform _defaultWeaponHitboxAttackerRoot;
    private bool _defaultWeaponHitboxUseOneShotWindow;
    private bool _defaultWeaponHitboxUseExpandedDetection;
    private bool _defaultWeaponHitboxUseSweepDetection;
    private float _defaultWeaponHitboxExpandedPadding;
    private float _defaultWeaponHitboxMeshPaddingScale;
    private float _defaultWeaponHitboxScanInterval;
    private float _defaultWeaponHitboxOneShotWindow;
    private static int s_nextAttackSequenceId = 1;
    private uint _lastConsumedLightAttackCommandSequence;
    private uint _lastConsumedHeavyAttackCommandSequence;

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
        if (!perfectDodgeAfterImageEffect) perfectDodgeAfterImageEffect = GetComponent<PerfectDodgeAfterImageEffect>();
        RefreshWeaponHitbox();
        attackVfxPresenter = GetComponent<PlayerAttackVfxPresenter>();
        if (!attackVfxPresenter)
            attackVfxPresenter = gameObject.AddComponent<PlayerAttackVfxPresenter>();
        attackVfxPresenter.Initialize(playerReferences, this);
        moveController = GetComponent<PlayerMoveController>();
        inputBlocker = GetComponent<IInputBlocker>();
        inputCommandBuffer = GetComponent<PlayerInputCommandBuffer>();
        if (inputCommandBuffer == null)
            inputCommandBuffer = gameObject.AddComponent<PlayerInputCommandBuffer>();
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
        DeactivateWeaponHitboxImmediate();
        ResetAttackHitboxWindowState();
        RestoreWeaponHitboxDefaults();
        attackVfxPresenter?.NotifyAttackEnded();

        if (movementLocked) SetMoveLock(false);
        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 0f);
        animator.speed = 1f;
        animator.applyRootMotion = false;
        StopPerfectDodgeAttackAssistDash(false);

        inAttack = false;
        inHit = false;
        invulnerable = false;
        activeHitStateName = string.Empty;
        activeHitStateHash = 0;
        activeHitStateShortNameHash = 0;
        waitingForHitStateEntry = false;
        hitStateRequestedAt = 0f;
        forcedHitStatePlay = false;
        deathLocked = false;
    }

    void Update()
    {
        // ★ [핵심] 일시정지 중이면 업데이트 중단 (타임아웃 방지)
        if (Time.timeScale == 0f) return;

        if (!animator) return;

        // Locomotion is code-driven. If another system leaves root motion enabled,
        // force it back off outside of active attack states.
        if (!inAttack && animator.applyRootMotion)
            animator.applyRootMotion = false;

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
                DeactivateWeaponHitboxImmediate();
                ResetAttackHitboxWindowState();
                EndAttack();
            }

            if (!string.IsNullOrEmpty(speedParam))
                animator.SetFloat(speedParam, 0f, 0.1f, Time.deltaTime);

            return;
        }

        // 1) 입력 버퍼링
        if (Input.GetMouseButtonDown(0))
            inputCommandBuffer?.RecordAttackLightPress();
        if (Input.GetMouseButtonDown(1))
            inputCommandBuffer?.RecordAttackHeavyPress();

        TryConsumeAttackCommand(
            PlayerInputCommandBuffer.CommandType.AttackLight,
            AttackInput.Light,
            ref _lastConsumedLightAttackCommandSequence);
        TryConsumeAttackCommand(
            PlayerInputCommandBuffer.CommandType.AttackHeavy,
            AttackInput.Heavy,
            ref _lastConsumedHeavyAttackCommandSequence);

        // PlayerMoveController owns the locomotion speed parameter in normal gameplay.
        // Keep this fallback only for scenes where the move controller is absent.
        if (moveController == null && !(inAttack && locomotionDuringAttack == false))
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
                if (start) Play(start, 0f, inp.type, 1);
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

        UpdateDataDrivenAttackHitboxWindow(t);

        float comboWindowEnd = ResolveAttackCancelWindowEnd(current);
        if (t >= current.cancelStart && t <= comboWindowEnd)
        {
            if (TryPeekAnyBufferedInput(out var inp))
            {
                var nx = FindNext(current, inp.type);
                if (nx)
                {
                    ConsumePeekedBufferedInput();
                    Play(nx, 0.03f, inp.type, _currentComboDepth + 1);
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

    private void Play(AttackData data, float fade, AttackInput sourceInput, int comboDepth)
    {
        DeactivateWeaponHitboxImmediate();
        ResetAttackHitboxWindowState();
        RestoreWeaponHitboxDefaults();

        current = data;
        inAttack = true;
        _currentAttackInput = sourceInput;
        _hasCurrentAttackInput = true;
        _currentComboDepth = Mathf.Max(1, comboDepth);
        _currentAttackSequenceId = s_nextAttackSequenceId++;
        if (s_nextAttackSequenceId == int.MaxValue)
            s_nextAttackSequenceId = 1;

        attackStartTime = Time.time;
        lastAttackPlayTime = Time.time;

        SafeSetLayerWeight(actionLayerIndex, 1f);

        if (lockMovementWhileAttacking && !movementLocked)
            SetMoveLock(true);

        animator.speed = Mathf.Max(0.01f, data.playSpeed);
        if (fade <= 0f) animator.Play(data.stateName, data.animatorLayer, 0f);
        else            animator.CrossFadeInFixedTime(data.stateName, fade, data.animatorLayer, 0f);

        animator.applyRootMotion = true;
        ApplyCurrentAttackHitboxSettings();
        attackVfxPresenter?.NotifyAttackStarted(data, _currentComboDepth, sourceInput);
        TryApplyPerfectDodgeAttackAssist();
        OnAttackStarted?.Invoke(sourceInput, data, _currentComboDepth);
    }

    private void EndAttack()
    {
        AttackData endedData = current;
        AttackInput endedInput = _currentAttackInput;
        int endedDepth = _currentComboDepth;

        DeactivateWeaponHitboxImmediate();
        ResetAttackHitboxWindowState();
        RestoreWeaponHitboxDefaults();
        inAttack = false;
        animator.speed = 1f;
        lastAttackEndRT = Time.time;

        SafeSetLayerWeight(actionLayerIndex, 0f);

        if (movementLocked) SetMoveLock(false);
        StopPerfectDodgeAttackAssistDash(false);
        animator.applyRootMotion = false;
        current = null;
        attackVfxPresenter?.NotifyAttackEnded();
        _hasCurrentAttackInput = false;
        _currentComboDepth = 0;
        _currentAttackSequenceId = 0;

        if (endedData != null)
            OnAttackEnded?.Invoke(endedInput, endedData, endedDepth);
    }

    public bool TryGetCurrentAttackInput(out AttackInput input)
    {
        input = _currentAttackInput;
        return _hasCurrentAttackInput;
    }

    public bool CanCancelIntoDodgeNow()
    {
        return TryGetCancelableAttackProgress(out _);
    }

    public bool TryCancelIntoDodge()
    {
        if (!TryGetCancelableAttackProgress(out _))
            return false;

        EndAttack();
        return true;
    }

    public bool TryGetAttackDebugWindow(out float normalizedTime, out float cancelStart, out float cancelEnd)
    {
        normalizedTime = 0f;
        cancelStart = 0f;
        cancelEnd = 0f;

        if (!inAttack || current == null || inHit || animator == null)
            return false;

        int layer = current.animatorLayer;
        if (!TryGetAttackProgress(layer, current, out float t))
            return false;

        normalizedTime = t;
        cancelStart = Mathf.Max(0f, current.cancelStart - dodgeCancelEarlyBufferNormalized);
        cancelEnd = ResolveAttackCancelWindowEnd(current);
        return true;
    }

    public bool TryGetHitDebugState(out float layerWeight, out bool waitingForEntry, out bool active, out float normalizedTime)
    {
        layerWeight = 0f;
        waitingForEntry = waitingForHitStateEntry;
        active = false;
        normalizedTime = 0f;

        if (animator == null || hitLayerIndex < 0 || hitLayerIndex >= animator.layerCount)
            return false;

        layerWeight = animator.GetLayerWeight(hitLayerIndex);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(hitLayerIndex);
        normalizedTime = Mathf.Repeat(stateInfo.normalizedTime, 1f);
        active = IsMatchingHitState(stateInfo);
        return inHit || layerWeight > 0.001f || active || waitingForEntry;
    }

    private bool TryGetCancelableAttackProgress(out float normalizedTime)
    {
        normalizedTime = 0f;
        if (!inAttack || current == null || inHit || animator == null)
            return false;

        int layer = current.animatorLayer;
        if (!TryGetAttackProgress(layer, current, out float t))
            return false;

        float cancelStart = Mathf.Max(0f, current.cancelStart - dodgeCancelEarlyBufferNormalized);
        float cancelEnd = ResolveAttackCancelWindowEnd(current);
        if (t < cancelStart || t > cancelEnd)
            return false;

        normalizedTime = t;
        return true;
    }

    private void UpdateDataDrivenAttackHitboxWindow(float normalizedTime)
    {
        if (!TryResolveCurrentAttackHitWindow(normalizedTime, out AttackHitWindow hitWindow, out int hitWindowIndex))
        {
            if (_activeAttackHitWindowIndex != NoActiveAttackHitWindow)
            {
                DeactivateWeaponHitboxImmediate();
                RestoreWeaponHitboxDefaults();
                _activeAttackHitWindowIndex = NoActiveAttackHitWindow;
            }

            return;
        }

        if (_activeAttackHitWindowIndex == hitWindowIndex)
            return;

        DeactivateWeaponHitboxImmediate();
        ApplyCurrentAttackHitboxSettings(hitWindow);
        ActivateWeaponHitboxImmediate();
        attackVfxPresenter?.PlayAttackWindow(current, weaponHitbox, hitWindow, _currentComboDepth, _currentAttackInput);
        _activeAttackHitWindowIndex = hitWindowIndex;
    }

    private bool TryResolveCurrentAttackHitWindow(float normalizedTime, out AttackHitWindow hitWindow, out int hitWindowIndex)
    {
        hitWindow = default;
        hitWindowIndex = NoActiveAttackHitWindow;

        if (!useDataDrivenHitWindows || current == null)
            return false;

        if (current.TryGetActiveHitWindow(normalizedTime, out hitWindow, out int definedIndex))
        {
            hitWindowIndex = definedIndex;
            return true;
        }

        if (!useFallbackWindowFromHitTime || current.HasDefinedHitWindows)
            return false;

        if (!current.TryBuildFallbackHitWindow(fallbackHitWindowLeadNormalized, fallbackHitWindowTailNormalized, out hitWindow))
            return false;

        if (!hitWindow.Contains(normalizedTime))
            return false;

        hitWindowIndex = FallbackAttackHitWindowIndex;
        return true;
    }

    private bool ShouldDriveHitboxByAttackData()
    {
        if (!useDataDrivenHitWindows || current == null)
            return false;

        if (current.HasDefinedHitWindows)
            return true;

        return useFallbackWindowFromHitTime
            && current.TryBuildFallbackHitWindow(fallbackHitWindowLeadNormalized, fallbackHitWindowTailNormalized, out _);
    }

    private void ResetAttackHitboxWindowState()
    {
        _activeAttackHitWindowIndex = NoActiveAttackHitWindow;
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
        Vector3 sideDirection = GetPerfectDodgeAttackAssistSideDirection(target, targetPoint, facingRoot);
        if (sideDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 destination = GetPerfectDodgeAttackAssistSideDestination(target, targetPoint, sideDirection);
        destination.y = transform.position.y;

        Vector3 moveDelta = destination - transform.position;
        moveDelta.y = 0f;
        if (moveDelta.sqrMagnitude > perfectDodgeAssistMinRange * perfectDodgeAssistMinRange)
        {
            if (dashPerfectDodgeAttackAssist)
                StartPerfectDodgeAttackAssistDash(destination, targetPoint);
            else
                SnapPlayerToPerfectDodgeAssistDestination(destination);
        }

        if (snapFacingToPerfectDodgeTarget && !dashPerfectDodgeAttackAssist)
        {
            Vector3 facingDirection = targetPoint - facingRoot.position;
            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude <= 0.0001f)
                facingDirection = sideDirection;

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
        return CombatTargetBoundsUtility.TryGetCombinedBounds(target, out Bounds combinedBounds)
            ? combinedBounds.center
            : target.position;
    }

    private Vector3 GetPerfectDodgeAttackAssistSideDirection(Transform target, Vector3 targetPoint, Transform facingRoot)
    {
        Vector3 right = target.right;
        right.y = 0f;
        if (right.sqrMagnitude <= 0.0001f)
            right = Vector3.Cross(Vector3.up, GetPerfectDodgeAttackAssistFallbackDirection(target, facingRoot));

        if (right.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        right.Normalize();

        float side = Vector3.Dot(right, facingRoot.position - targetPoint);
        if (Mathf.Abs(side) <= 0.05f && playerRoot != null)
            side = Vector3.Dot(right, playerRoot.right);
        if (Mathf.Abs(side) <= 0.05f)
            side = 1f;

        return side >= 0f ? right : -right;
    }

    private Vector3 GetPerfectDodgeAttackAssistSideDestination(Transform target, Vector3 targetPoint, Vector3 sideDirection)
    {
        float targetRadius = GetPerfectDodgeAttackAssistTargetRadius(target, targetPoint);
        float sideDistance = Mathf.Max(0.05f, targetRadius + perfectDodgeAssistStopDistance + perfectDodgeAssistSideOffset);
        return targetPoint + sideDirection.normalized * sideDistance;
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

    private float GetPerfectDodgeAttackAssistTargetRadius(Transform target, Vector3 targetPoint)
    {
        if (!CombatTargetBoundsUtility.TryGetCombinedBounds(target, out Bounds bounds))
            return 0.45f;

        Vector3 extents = bounds.extents;
        extents.y = 0f;
        float planarRadius = Mathf.Max(extents.x, extents.z);
        if (planarRadius > 0.01f)
            return planarRadius;

        Vector3 planarOffset = bounds.center - targetPoint;
        planarOffset.y = 0f;
        return Mathf.Max(0.45f, planarOffset.magnitude);
    }

    private void StartPerfectDodgeAttackAssistDash(Vector3 destination, Vector3 facePoint)
    {
        StopPerfectDodgeAttackAssistDash(true);
        perfectDodgeAssistDashRoutine = StartCoroutine(CoPerfectDodgeAttackAssistDash(destination, facePoint));
    }

    private IEnumerator CoPerfectDodgeAttackAssistDash(Vector3 destination, Vector3 facePoint)
    {
        Transform facingRoot = playerRoot != null ? playerRoot : transform;
        Vector3 startPosition = transform.position;
        destination.y = startPosition.y;
        float duration = Mathf.Max(0.04f, perfectDodgeAssistDashDurationRealtime);
        bool restoreRootMotion = animator != null && animator.applyRootMotion;

        if (animator != null)
            animator.applyRootMotion = false;

        PerfectDodgeAfterImageEffect afterImage = ResolvePerfectDodgeAssistAfterImageEffect();
        if (leaveAfterImageDuringPerfectDodgeAssistDash && afterImage != null)
            afterImage.StartContinuousTrail(duration);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            Vector3 desiredPosition = Vector3.Lerp(startPosition, destination, eased);
            MovePerfectDodgeAttackAssistStep(desiredPosition - transform.position);
            RotatePerfectDodgeAttackAssistFacing(facingRoot, facePoint, Time.unscaledDeltaTime);
            yield return null;
        }

        MovePerfectDodgeAttackAssistStep(destination - transform.position);
        RotatePerfectDodgeAttackAssistFacing(facingRoot, facePoint, 1f);

        if (leaveAfterImageDuringPerfectDodgeAssistDash && afterImage != null)
            afterImage.StopContinuousTrail();

        if (animator != null && inAttack)
            animator.applyRootMotion = restoreRootMotion;

        perfectDodgeAssistDashRoutine = null;
    }

    private void StopPerfectDodgeAttackAssistDash(bool restoreRootMotion)
    {
        if (perfectDodgeAssistDashRoutine != null)
        {
            StopCoroutine(perfectDodgeAssistDashRoutine);
            perfectDodgeAssistDashRoutine = null;
        }

        if (leaveAfterImageDuringPerfectDodgeAssistDash)
            ResolvePerfectDodgeAssistAfterImageEffect()?.StopContinuousTrail();

        if (restoreRootMotion && animator != null && inAttack)
            animator.applyRootMotion = true;
    }

    private PerfectDodgeAfterImageEffect ResolvePerfectDodgeAssistAfterImageEffect()
    {
        if (perfectDodgeAfterImageEffect == null)
            perfectDodgeAfterImageEffect = GetComponent<PerfectDodgeAfterImageEffect>();

        return perfectDodgeAfterImageEffect;
    }

    void OnAnimatorMove()
    {
        if (!inAttack || inHit || animator == null || !animator.applyRootMotion)
            return;

        if (perfectDodgeAssistDashRoutine != null)
            return;

        Vector3 delta = animator.deltaPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        if (characterController != null && characterController.enabled)
        {
            characterController.Move(delta);
            return;
        }

        transform.position += delta;
    }

    private void MovePerfectDodgeAttackAssistStep(Vector3 delta)
    {
        delta.y = 0f;
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        if (characterController != null && characterController.enabled)
        {
            characterController.Move(delta);
            return;
        }

        transform.position += delta;
    }

    private void RotatePerfectDodgeAttackAssistFacing(Transform facingRoot, Vector3 facePoint, float deltaTime)
    {
        if (!snapFacingToPerfectDodgeTarget || facingRoot == null)
            return;

        Vector3 facingDirection = facePoint - facingRoot.position;
        facingDirection.y = 0f;
        if (facingDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
        float step = deltaTime >= 1f
            ? 360f
            : perfectDodgeAssistDashRotationSpeed * Mathf.Max(0.0001f, deltaTime);
        facingRoot.rotation = Quaternion.RotateTowards(facingRoot.rotation, targetRotation, step);
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

    private void TryConsumeAttackCommand(
        PlayerInputCommandBuffer.CommandType commandType,
        AttackInput attackInput,
        ref uint lastConsumedSequence)
    {
        if (inputCommandBuffer == null)
            return;

        if (!inputCommandBuffer.TryConsumeLatest(commandType, Mathf.Max(0.05f, inputBufferTime), ref lastConsumedSequence))
            return;

        if (Time.time - lastClickTime <= minClickInterval)
            return;

        lastClickTime = Time.time;
        BufferAttackInput(attackInput);
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

    private float ResolveAttackCancelWindowEnd(AttackData attack)
    {
        if (attack == null)
            return 0f;

        return Mathf.Min(1.05f, Mathf.Max(attack.cancelEnd, 0.95f) + comboLateGraceNormalized);
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
        if (ShouldDriveHitboxByAttackData())
            return;

        ApplyCurrentAttackHitboxSettings();
        ActivateWeaponHitboxImmediate();
        attackVfxPresenter?.PlayAttackWindow(current, weaponHitbox, null, _currentComboDepth, _currentAttackInput);
    }

    // 애니메이션에서 호출 (공격 판정 끄기)
    public void DisableAttackHitbox()
    {
        if (ShouldDriveHitboxByAttackData())
            return;

        DeactivateWeaponHitboxImmediate();
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
        deathLocked = false;
        hitStartTime = Time.time;

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        string state = heavy ? hitHeavyState : hitLightState;
        float keepSec = heavy ? heavyHitSeconds : lightHitSeconds;

        PlayHitState(state);
        activeHitStateName = state;
        hitStateEndTime = useAnimationDrivenHitRecovery
            ? 0f
            : ((keepSec > 0f) ? (Time.time + keepSec) : 0f);
    }

    public void ApplyKnockdown()
    {
        if (invulnerable) return;
        if (inAttack) EndAttack();
        ClearBufferedInputs();

        inHit = true;
        deathLocked = false;
        invulnerable = true;
        hitStartTime = Time.time;
        hitStateEndTime = Time.time + knockdownInvulnSeconds;

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        PlayHitState(knockdownState);
        activeHitStateName = knockdownState;
    }

    public void ApplyDeath()
    {
        if (deathLocked)
            return;

        if (inAttack)
            EndAttack();
        else
        {
            DeactivateWeaponHitboxImmediate();
            ResetAttackHitboxWindowState();
            RestoreWeaponHitboxDefaults();
            attackVfxPresenter?.NotifyAttackEnded();
        }

        inHit = true;
        inAttack = false;
        deathLocked = true;
        invulnerable = true;
        hitStartTime = Time.time;
        ClearBufferedInputs();

        SafeSetLayerWeight(actionLayerIndex, 0f);
        SafeSetLayerWeight(hitLayerIndex, 1f);
        SetMoveLock(true);

        animator.speed = 1f;
        PlayHitState(deathState);
        activeHitStateName = deathState;
        hitStateEndTime = 0f;
    }

    private void UpdateHitState()
    {
        if (deathLocked)
        {
            UpdateDeathState();
            return;
        }
        // 타임아웃 체크 (Time.time 기준)
        if (endHitIfStuckSeconds > 0f && (Time.time - hitStartTime) > endHitIfStuckSeconds)
        {
            Debug.LogWarning("[Combat] Hit state forced to end by timeout failsafe.");
            EndHit();
            return;
        }

        if (animator == null)
        {
            EndHit();
            return;
        }

        if (waitingForHitStateEntry)
        {
            if (IsHitStateQueuedOrActive())
            {
                waitingForHitStateEntry = false;
            }
            else
            {
                if (!forcedHitStatePlay &&
                    activeHitStateHash != 0 &&
                    Time.time - hitStateRequestedAt >= hitStateForcePlayDelay)
                {
                    animator.Play(activeHitStateHash, hitLayerIndex, 0f);
                    forcedHitStatePlay = true;
                }
                return;
            }
        }

        if (hitStateEndTime > 0f && Time.time >= hitStateEndTime)
        {
            EndHit();
            return;
        }

        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(hitLayerIndex);
        bool isTransitioning = animator.IsInTransition(hitLayerIndex);
        if (MatchesState(st, deathState, Animator.StringToHash(deathState)) ||
            (isTransitioning && MatchesState(animator.GetNextAnimatorStateInfo(hitLayerIndex), deathState, Animator.StringToHash(deathState))))
            return;

        if (string.IsNullOrEmpty(activeHitStateName))
            return;

        if (isTransitioning)
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(hitLayerIndex);
            if (IsMatchingHitState(nextState))
                return;
        }

        if (IsMatchingHitState(st) && st.normalizedTime >= 0.99f && !isTransitioning)
        {
            EndHit();
            return;
        }

        if (!IsMatchingHitState(st) && !isTransitioning && hitStateEndTime <= 0f)
            EndHit();
    }

    private void EndHit()
    {
        if (deathLocked)
            return;

        inHit = false;
        invulnerable = false;
        activeHitStateName = string.Empty;
        activeHitStateHash = 0;
        activeHitStateShortNameHash = 0;
        waitingForHitStateEntry = false;
        hitStateRequestedAt = 0f;
        forcedHitStatePlay = false;
        SafeSetLayerWeight(hitLayerIndex, 0f);
        SetMoveLock(false);
    }

    private void UpdateDeathState()
    {
        if (animator == null)
            return;

        SafeSetLayerWeight(hitLayerIndex, 1f);
        if (!movementLocked)
            SetMoveLock(true);

        invulnerable = true;

        if (waitingForHitStateEntry)
        {
            if (IsHitStateQueuedOrActive())
            {
                waitingForHitStateEntry = false;
                return;
            }

            if (!forcedHitStatePlay &&
                activeHitStateHash != 0 &&
                Time.time - hitStateRequestedAt >= hitStateForcePlayDelay)
            {
                animator.Play(activeHitStateHash, hitLayerIndex, 0f);
                forcedHitStatePlay = true;
            }

            return;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(hitLayerIndex);
        int deathShortHash = Animator.StringToHash(deathState);
        if (MatchesState(stateInfo, deathState, deathShortHash))
            return;

        if (animator.IsInTransition(hitLayerIndex))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(hitLayerIndex);
            if (MatchesState(nextState, deathState, deathShortHash))
                return;
        }

        if (activeHitStateHash != 0)
            animator.Play(activeHitStateHash, hitLayerIndex, 0f);
        else
            animator.Play(deathState, hitLayerIndex, 0f);
    }

    private void PlayHitState(string stateName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
            return;

        activeHitStateHash = ResolveStateHashForLayer(hitLayerIndex, stateName);
        activeHitStateShortNameHash = Animator.StringToHash(stateName);
        hitStateRequestedAt = Time.time;
        waitingForHitStateEntry = true;
        forcedHitStatePlay = false;

        if (activeHitStateHash != 0)
        {
            animator.CrossFadeInFixedTime(activeHitStateHash, 0.05f, hitLayerIndex, 0f);
            return;
        }

        animator.CrossFadeInFixedTime(stateName, 0.05f, hitLayerIndex, 0f);
    }

    private bool IsHitStateQueuedOrActive()
    {
        if (animator == null)
            return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(hitLayerIndex);
        if (IsMatchingHitState(currentState))
            return true;

        if (!animator.IsInTransition(hitLayerIndex))
            return false;

        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(hitLayerIndex);
        return IsMatchingHitState(nextState);
    }

    private bool IsMatchingHitState(AnimatorStateInfo stateInfo)
    {
        if (string.IsNullOrEmpty(activeHitStateName))
            return false;

        if (stateInfo.shortNameHash == activeHitStateShortNameHash)
            return true;

        if (activeHitStateHash != 0 && stateInfo.fullPathHash == activeHitStateHash)
            return true;

        return stateInfo.IsName(activeHitStateName);
    }

    private bool MatchesState(AnimatorStateInfo stateInfo, string stateName, int shortNameHash)
    {
        if (string.IsNullOrEmpty(stateName))
            return false;

        if (stateInfo.shortNameHash == shortNameHash)
            return true;

        return stateInfo.IsName(stateName);
    }

    private int ResolveStateHashForLayer(int layerIndex, string stateName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
            return 0;

        int shortHash = Animator.StringToHash(stateName);
        if (animator.HasState(layerIndex, shortHash))
            return shortHash;

        string layerName = animator.GetLayerName(layerIndex);
        if (!string.IsNullOrEmpty(layerName))
        {
            int directLayerHash = Animator.StringToHash(layerName + "." + stateName);
            if (animator.HasState(layerIndex, directLayerHash))
                return directLayerHash;

            int hitSubStateHash = Animator.StringToHash(layerName + ".Hit." + stateName);
            if (animator.HasState(layerIndex, hitSubStateHash))
                return hitSubStateHash;
        }

        return 0;
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

        if (playerReferences != null)
        {
            var preferredHitbox = playerReferences.PrimaryAttackHitbox;
            if (preferredHitbox != null && preferredHitbox.gameObject.activeInHierarchy)
            {
                weaponHitbox = preferredHitbox;
                return;
            }
        }

        if (weaponHitbox != null && weaponHitbox.gameObject.activeInHierarchy)
            return;

        var fallbackHitbox = GetComponentInChildren<AttackHitbox>(true);
        if (fallbackHitbox != null && fallbackHitbox.gameObject.activeInHierarchy)
            weaponHitbox = fallbackHitbox;
    }

    private void ActivateWeaponHitboxImmediate()
    {
        RefreshWeaponHitbox();
        if (weaponHitbox != null)
            weaponHitbox.ActivateWindow();
    }

    private void DeactivateWeaponHitboxImmediate()
    {
        RefreshWeaponHitbox();
        if (weaponHitbox != null)
            weaponHitbox.DeactivateWindow();

        attackVfxPresenter?.StopAttackWindow();
    }

    private void ApplyCurrentAttackHitboxSettings()
    {
        ApplyCurrentAttackHitboxSettings(null);
    }

    private void ApplyCurrentAttackHitboxSettings(AttackHitWindow? hitWindowOverride)
    {
        RefreshWeaponHitbox();
        if (weaponHitbox == null || current == null)
            return;

        CacheWeaponHitboxDefaults(weaponHitbox);
        float baseAttackDamage = ResolveCurrentAttackBaseDamage();
        float resolvedDamage = hitWindowOverride.HasValue
            ? hitWindowOverride.Value.ResolveDamage(baseAttackDamage)
            : baseAttackDamage;
        HitType resolvedHitType = hitWindowOverride.HasValue
            ? hitWindowOverride.Value.ResolveHitType(current.ResolveHitType(_defaultWeaponHitboxType))
            : current.ResolveHitType(_defaultWeaponHitboxType);

        weaponHitbox.Configure(
            resolvedDamage,
            resolvedHitType,
            _defaultWeaponHitboxCanParry,
            _defaultWeaponHitboxCanPerfectDodge,
            _defaultWeaponHitboxUnblockable,
            playerRoot != null ? playerRoot : transform);
        weaponHitbox.attackSequenceId = _currentAttackSequenceId;

        weaponHitbox.useExpandedHitDetection = current.ResolveUseExpandedHitDetection(_defaultWeaponHitboxUseExpandedDetection);
        weaponHitbox.useSweepHitDetection = current.ResolveUseSweepHitDetection(_defaultWeaponHitboxUseSweepDetection);
        weaponHitbox.expandedPadding = current.ResolveHitboxExpandedPadding(_defaultWeaponHitboxExpandedPadding);
        weaponHitbox.meshExpandedPaddingScale = current.ResolveHitboxMeshPaddingScale(_defaultWeaponHitboxMeshPaddingScale);
        weaponHitbox.expandedScanInterval = current.ResolveHitboxScanInterval(_defaultWeaponHitboxScanInterval);
        weaponHitbox.oneShotWindow = current.ResolveHitboxOneShotWindow(_defaultWeaponHitboxOneShotWindow);
        weaponHitbox.useOneShotWindow = _defaultWeaponHitboxUseOneShotWindow && weaponHitbox.oneShotWindow > 0.001f;
    }

    private float ResolveCurrentAttackBaseDamage()
    {
        if (current == null)
            return fallbackAttackBaseDamage;

        float attackBaseDamage = current.ResolveAttackBaseDamage(fallbackAttackBaseDamage);
        float attackTypeMultiplier = _currentAttackInput == AttackInput.Heavy
            ? Mathf.Max(0f, heavyAttackDamageMultiplier)
            : Mathf.Max(0f, lightAttackDamageMultiplier);

        int comboDepth = Mathf.Max(1, _currentComboDepth);
        float comboBonusPerStep = _currentAttackInput == AttackInput.Heavy
            ? heavyComboStepBonus
            : lightComboStepBonus;
        float comboMultiplier = 1f + Mathf.Max(0f, comboBonusPerStep) * Mathf.Max(0, comboDepth - 1);

        return Mathf.Max(0f, attackBaseDamage * attackTypeMultiplier * comboMultiplier);
    }

    private void CacheWeaponHitboxDefaults(AttackHitbox hitbox)
    {
        if (hitbox == null)
            return;

        if (_cachedWeaponHitboxDefaults && _cachedWeaponHitboxDefaultsSource == hitbox)
            return;

        _cachedWeaponHitboxDefaultsSource = hitbox;
        _cachedWeaponHitboxDefaults = true;
        _defaultWeaponHitboxDamage = hitbox.baseDamage;
        _defaultWeaponHitboxType = hitbox.hitType;
        _defaultWeaponHitboxAttackSequenceId = hitbox.attackSequenceId;
        _defaultWeaponHitboxCanParry = hitbox.canParry;
        _defaultWeaponHitboxCanPerfectDodge = hitbox.canPerfectDodge;
        _defaultWeaponHitboxUnblockable = hitbox.unblockable;
        _defaultWeaponHitboxAttackerRoot = hitbox.attackerRoot;
        _defaultWeaponHitboxUseOneShotWindow = hitbox.useOneShotWindow;
        _defaultWeaponHitboxUseExpandedDetection = hitbox.useExpandedHitDetection;
        _defaultWeaponHitboxUseSweepDetection = hitbox.useSweepHitDetection;
        _defaultWeaponHitboxExpandedPadding = hitbox.expandedPadding;
        _defaultWeaponHitboxMeshPaddingScale = hitbox.meshExpandedPaddingScale;
        _defaultWeaponHitboxScanInterval = hitbox.expandedScanInterval;
        _defaultWeaponHitboxOneShotWindow = hitbox.oneShotWindow;
    }

    private void RestoreWeaponHitboxDefaults()
    {
        if (!_cachedWeaponHitboxDefaults || _cachedWeaponHitboxDefaultsSource == null)
            return;

        _cachedWeaponHitboxDefaultsSource.Configure(
            _defaultWeaponHitboxDamage,
            _defaultWeaponHitboxType,
            _defaultWeaponHitboxCanParry,
            _defaultWeaponHitboxCanPerfectDodge,
            _defaultWeaponHitboxUnblockable,
            _defaultWeaponHitboxAttackerRoot);
        _cachedWeaponHitboxDefaultsSource.attackSequenceId = _defaultWeaponHitboxAttackSequenceId;

        _cachedWeaponHitboxDefaultsSource.useOneShotWindow = _defaultWeaponHitboxUseOneShotWindow;
        _cachedWeaponHitboxDefaultsSource.useExpandedHitDetection = _defaultWeaponHitboxUseExpandedDetection;
        _cachedWeaponHitboxDefaultsSource.useSweepHitDetection = _defaultWeaponHitboxUseSweepDetection;
        _cachedWeaponHitboxDefaultsSource.expandedPadding = _defaultWeaponHitboxExpandedPadding;
        _cachedWeaponHitboxDefaultsSource.meshExpandedPaddingScale = _defaultWeaponHitboxMeshPaddingScale;
        _cachedWeaponHitboxDefaultsSource.expandedScanInterval = _defaultWeaponHitboxScanInterval;
        _cachedWeaponHitboxDefaultsSource.oneShotWindow = _defaultWeaponHitboxOneShotWindow;
    }
}
