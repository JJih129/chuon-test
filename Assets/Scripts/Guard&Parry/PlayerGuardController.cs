using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// 가드/패링 컨트롤러(소울라이크 표준)
/// - 애니 파라미터 존재 여부를 검사해 안전하게 동작
/// - 패링/블록 리액션 동안 이동 잠금
/// - 패링 성공 시 궁극기 게이지 수급
/// - 애니메이션 이벤트로 패링 윈도우 열기 지원
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("ChuOn/Combat/Player Guard Controller")]
public class PlayerGuardController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    // ① 입력 설정
    // ─────────────────────────────────────────────────────────────────────
    [Header("① 입력 설정(신규 Input System)")]
    [Tooltip("가드 키(InputActionReference). 비워두면 코드/애니에서만 제어")]
    public InputActionReference guardAction;

    // ─────────────────────────────────────────────────────────────────────
    // ② 애니메이터 파라미터 이름
    // ─────────────────────────────────────────────────────────────────────
    [Header("② 애니메이터 파라미터명")]
    [Tooltip("가드 여부 Bool")]
    public string guardBoolParam = "IsGuarding";
    [Tooltip("공격 중 Bool")]
    public string isAttackingBoolParam = "IsAttacking";
    [Tooltip("블록 리액션 Trigger")]
    public string guardBlockTrigger = "GuardBlock";
    [Tooltip("패링 성공 Trigger")]
    public string parrySuccessTrigger = "ParrySuccess";
    [Tooltip("가드 브레이크 Trigger")]
    public string guardBreakTrigger = "GuardBreak";

    // ─────────────────────────────────────────────────────────────────────
    // ③ 가드 전방 기준/판정
    // ─────────────────────────────────────────────────────────────────────
    public enum ForwardMode { ModelForward, CameraForward }

    [Header("③ 가드 전방 기준/판정")]
    [Tooltip("전방 벡터 기준 선택")]
    public ForwardMode forwardMode = ForwardMode.ModelForward;
    [Tooltip("모델 Transform(미설정 시 this.transform 사용)")]
    public Transform modelForwardSource;
    [Tooltip("카메라 Transform(ForwardMode가 CameraForward일 때 사용)")]
    public Transform cameraTransform;
    [Tooltip("전방 반전")]
    public bool invertForward = false;
    [Range(10f, 360f)]
    [Tooltip("정면 허용각(전체각). 예: 100이면 ±50°")]
    public float frontArcDegrees = 100f;
    [Tooltip("수평면에서 각도 판정(높낮이 무시)")]
    public bool flattenToGround = true;

    // ─────────────────────────────────────────────────────────────────────
    // ④ 정책(공격 중 가드 차단 등)
    // ─────────────────────────────────────────────────────────────────────
    [Header("④ 정책(공격 중 가드 차단 등)")]
    [Tooltip("공격 중엔 가드 입력 무시")]
    public bool denyGuardWhileAttacking = true;
    [Tooltip("가드키 유지 중 공격 종료 시 자동 재가드")]
    public bool autoResumeGuardIfHolding = true;
    [Tooltip("탭은 패링, 홀드는 가드로 입력을 분리")]
    public bool splitTapParryAndHoldGuard = true;
    [Tooltip("홀드로 가드 진입하기까지 필요한 시간(초)")]
    public float guardHoldDelay = 0.18f;
    [Tooltip("가드 비주얼이 최소한으로 유지되는 시간(짧은 탭에서만 연타 재시작 방지)")]
    public float guardVisualReleaseGrace = 0.12f;

    // ─────────────────────────────────────────────────────────────────────
    // ⑤ 대미지/패링
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑤ 대미지/패링")]
    [Range(0f, 1f)]
    [Tooltip("블록 시 칩 대미지 비율(0=무시, 0.1=10%)")]
    public float chipDamageOnBlock = 0.1f;
    [Tooltip("퍼펙트 가드 사용")]
    public bool enablePerfectGuard = true;
    [Tooltip("패링 기본 창(초). 이벤트에서 길이 미지정 시 사용")]
    public float perfectGuardWindow = 0.12f;
    [Tooltip("패링 입력 후 실제 판정이 열리기까지의 스타트업 시간(초, 실시간 기준)")]
    public float parryStartupDuration = 0.045f;
    [Tooltip("패링 실패 후 다음 패링 시도까지 필요한 회복 시간(초, 실시간 기준)")]
    public float failedParryRecovery = 0.2f;
    [Tooltip("이 시간 안의 연속 패링 시도는 연타로 간주")]
    public float parrySpamChainWindow = 0.55f;
    [Range(0f, 0.4f)]
    [Tooltip("연타 스택당 줄어드는 패링 창 비율")]
    public float parrySpamWindowDecayPerStack = 0.18f;
    [Tooltip("연타 스택당 추가되는 패링 실패 회복 시간(초)")]
    public float parrySpamRecoveryPenaltyPerStack = 0.08f;
    [Tooltip("연타 페널티 최대 스택")]
    public int maxParrySpamPenaltyStacks = 3;
    [Tooltip("가드 입력 직후 이 시간 안에 들어온 패링 이벤트만 허용")]
    public float parryEventAcceptWindow = 0.35f;
    [Tooltip("패링 창은 실시간 기준으로 닫힘(슬로우/히트스톱 영향 없음)")]
    public bool useRealtimeParryWindow = true;

    [Header("⑤-2 가드 안정도")]
    [Tooltip("가드를 계속 올리고 있으면 안정도가 쌓이고, 다 차면 가드 브레이크")]
    public bool enableGuardStrain = true;
    [Tooltip("최대 가드 안정도")]
    public float maxGuardStrain = 100f;
    [Tooltip("막은 피해 1당 누적되는 안정도")]
    public float guardStrainPerBlockedDamage = 1.0f;
    [Tooltip("막을 때마다 고정으로 누적되는 안정도")]
    public float guardStrainFlatPerBlock = 6f;
    [Tooltip("안정도 회복 시작 전 대기 시간")]
    public float guardStrainRecoverDelay = 1.15f;
    [Tooltip("초당 안정도 회복량")]
    public float guardStrainRecoverPerSecond = 26f;
    [Tooltip("패링 성공 시 회복되는 안정도")]
    public float parryStrainRecover = 24f;
    [Tooltip("가드 브레이크 지속 시간")]
    public float guardBreakDuration = 0.9f;
    [Tooltip("활성화 시에만 가드 브레이크를 허용")]
    public bool enableGuardBreak = false;

    // ─────────────────────────────────────────────────────────────────────
    // ⑥ 리액션 중 이동 잠금
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑥ 이동 잠금(리액션 중 이동 금지)")]
    [Tooltip("블록 리액션 동안 이동 잠금")]
    public bool lockMoveOnBlock = true;
    [Tooltip("블록 리액션 이동 잠금 시간(초)")]
    public float blockMoveLock = 0.35f;
    [Tooltip("패링 리액션 동안 이동 잠금")]
    public bool lockMoveOnParry = true;
    [Tooltip("패링 리액션 이동 잠금 시간(초)")]
    public float parryMoveLock = 0.45f;
    [Tooltip("잠금 시 즉시 속도 0으로 정지")]
    public bool stopVelocityOnLock = true;
    [Tooltip("잠금 동안 Animator RootMotion 비활성화")]
    public bool disableRootMotionDuringLock = false;

    [Tooltip("같은 오브젝트의 이동 컨트롤러 참조")]
    public PlayerMoveController move;
    [Tooltip("속도 0 처리를 위한 Rigidbody(선택)")]
    public Rigidbody rb;

    // ─────────────────────────────────────────────────────────────────────
    // ⑦ 패링 성공 → 궁극기 게이지 수급
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑦ 패링 성공 → 궁극기 게이지 수급")]
    [Tooltip("패링 성공 시 게이지 지급 활성화")]
    public bool grantUltimateOnParry = true;
    [Tooltip("패링 1회당 추가 게이지 양")]
    public float ultimateGainOnParry = 10f;
    [Tooltip("플레이어 궁극기 컨트롤러")]
    public PlayerUltimateController ultimate;

    [Header("⑦-2 패링 카운터 보상")]
    [Tooltip("패링 성공 후 일정 시간 안의 첫 공격을 강화")]
    public bool enableParryCounterWindow = true;
    [Tooltip("카운터 보너스가 유지되는 시간(초, 실시간 기준)")]
    public float parryCounterWindow = 1.15f;
    [Tooltip("카운터 공격 피해 배수")]
    public float parryCounterDamageMultiplier = 1.85f;
    [Tooltip("카운터 공격 적중 시 추가 브레이크")]
    public float parryCounterBreakBonus = 20f;
    [Tooltip("카운터 공격에 덮어쓸 히트 타입")]
    public HitType parryCounterHitType = HitType.Heavy;

    // ─────────────────────────────────────────────────────────────────────
    // ⑧ 이벤트(필요 시 에디터에서 바인딩)
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑧ 이벤트(옵션)")]
    public UnityEvent OnParrySuccess;
    public UnityEvent OnGuardBlock;
    public UnityEvent OnGuardBreak;

    // ─────────────────────────────────────────────────────────────────────
    // ⑨ 디버그
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑨ 디버그")]
    [Tooltip("애니메이터 참조(비워두면 자동 탐색)")]
    public Animator animator;
    [Tooltip("전방·각도 기즈모")]
    public bool debugDraw = false;
    [Tooltip("파라미터 미존재 경고 로그")]
    public bool warnMissingParams = true;

    // 내부 상태
    public bool IsGuarding  { get; private set; }
    public bool IsAttacking { get; private set; }
    public bool IsParryWindowOpen => _parryOpen;
    public bool IsParryRecovering => Time.realtimeSinceStartup < _parryRecoveryUntilRealtime;
    public bool IsGuardBroken => Time.realtimeSinceStartup < _guardBrokenUntilRealtime;
    public bool IsGuardMovementActive => IsGuarding || (_guardHoldPending && !IsGuardBroken && !IsGuardActionBlocked());
    public bool IsMoveLockActive => _moveLockedByTimer && Time.time < _moveUnlockAt;
    public float GuardStrainNormalized => !enableGuardStrain || maxGuardStrain <= 0f ? 0f : Mathf.Clamp01(_guardStrain / maxGuardStrain);
    public bool IsParryCounterReady => enableParryCounterWindow && Time.realtimeSinceStartup < _parryCounterUntilRealtime;
    public float ParryCounterRemaining => IsParryCounterReady ? Mathf.Max(0f, _parryCounterUntilRealtime - Time.realtimeSinceStartup) : 0f;
    public float ParryRecoveryRemaining => IsParryRecovering ? Mathf.Max(0f, _parryRecoveryUntilRealtime - Time.realtimeSinceStartup) : 0f;
    public float GuardBreakRemaining => IsGuardBroken ? Mathf.Max(0f, _guardBrokenUntilRealtime - Time.realtimeSinceStartup) : 0f;

    // 해시 및 존재 여부 캐시
    int _hashGuardBool, _hashAttackBool, _hashBlockTrig, _hashParryTrig, _hashGuardBreakTrig;
    bool _hasGuardBool, _hasAttackBool, _hasBlockTrig, _hasParryTrig, _hasGuardBreakTrig;

    // 패링 창
    bool _parryOpen;
    bool _parryAttemptActive;
    bool _parrySuccessPendingClose;
    float _parryActivateAt = float.NegativeInfinity;
    int _parrySpamPenaltyStacks;
    float _lastParryAttemptStartedRealtime = float.NegativeInfinity;
    float _lastGuardPressedRealtime = float.NegativeInfinity;
    bool _parryAvailableThisGuard;
    bool _guardHoldPending;
    bool _guardVisualRaised;
    bool _requireGuardReleaseAfterBreak;
    float _guardStrain;
    float _guardStrainRecoverAllowedRealtime;
    float _guardBrokenUntilRealtime = float.NegativeInfinity;
    float _guardVisualMinHoldUntilRealtime = float.NegativeInfinity;
    float _guardReactionVisualUntilRealtime = float.NegativeInfinity;
    float _guardRearmBlockedUntilRealtime = float.NegativeInfinity;
    float _parryRecoveryUntilRealtime = float.NegativeInfinity;
    float _parryCounterUntilRealtime = float.NegativeInfinity;
    float _parryCloseAt = float.NegativeInfinity;
    bool _parryUsesRealtimeClock;

    // 이동 잠금
    bool _prevRootMotion;
    bool _moveLockedByTimer;
    float _moveUnlockAt = float.NegativeInfinity;
    IInputBlocker _inputBlocker;
    PlayerCombatController _combatController;
    PlayerReferences _playerReferences;
    PlayerInputCommandBuffer _inputCommandBuffer;

    void Awake()
    {
        _playerReferences = GetComponent<PlayerReferences>();
        if (!animator) animator = _playerReferences != null && _playerReferences.MainAnimator != null
            ? _playerReferences.MainAnimator
            : GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>();
        if (!move)     move     = GetComponent<PlayerMoveController>();
        if (!rb)       rb       = GetComponent<Rigidbody>();
        if (!ultimate) ultimate = GetComponent<PlayerUltimateController>();
        _inputBlocker = GetComponent<IInputBlocker>();
        _combatController = GetComponent<PlayerCombatController>();
        _inputCommandBuffer = GetComponent<PlayerInputCommandBuffer>();
        if (_inputCommandBuffer == null)
            _inputCommandBuffer = gameObject.AddComponent<PlayerInputCommandBuffer>();

        CacheAnimatorParams();

        if (guardAction) guardAction.action.Enable();
    }

    void Update()
    {
        UpdateTimedStates();

        IsAttacking = ResolveAttackStateActive();

        RecoverGuardStrain();

        if (IsInputBlocked())
        {
            if (IsGuarding) SetGuarding(false);
            if (_parryOpen || _parryAttemptActive) CancelParryWindow();
            _guardHoldPending = false;
            _parryCounterUntilRealtime = float.NegativeInfinity;
            ClearGuardVisualImmediate();
            return;
        }

        // 1) 입력 처리
        bool hasGuardAction = guardAction && guardAction.action != null;
        if (hasGuardAction || ShouldUseLegacyGuardFallback())
        {
            bool pressedThisFrame = hasGuardAction
                ? guardAction.action.WasPressedThisFrame()
                : Input.GetKeyDown(KeyCode.E);
            bool releasedThisFrame = hasGuardAction
                ? guardAction.action.WasReleasedThisFrame()
                : Input.GetKeyUp(KeyCode.E);
            bool holding = hasGuardAction
                ? guardAction.action.IsPressed()
                : Input.GetKey(KeyCode.E);

            if (pressedThisFrame)
                _inputCommandBuffer?.RecordGuardPress();

            if (releasedThisFrame)
                _inputCommandBuffer?.RecordGuardRelease();

            if (_requireGuardReleaseAfterBreak)
            {
                if (!holding)
                    _requireGuardReleaseAfterBreak = false;

                pressedThisFrame = false;
                if (IsGuarding)
                    SetGuarding(false);
            }

            bool guardActionBlocked = IsGuardActionBlocked();
            if (guardActionBlocked)
            {
                pressedThisFrame = false;
                if (IsGuarding)
                    SetGuarding(false);

                if (_parryOpen || _parryAttemptActive)
                    CancelParryWindow();

                if (splitTapParryAndHoldGuard && autoResumeGuardIfHolding && holding)
                {
                    if (!_guardHoldPending)
                        _lastGuardPressedRealtime = Time.realtimeSinceStartup;

                    _guardHoldPending = true;
                }
                else if (!holding)
                {
                    _guardHoldPending = false;
                }
            }

            if (splitTapParryAndHoldGuard)
            {
                HandleSplitGuardInput(pressedThisFrame, releasedThisFrame, holding);
            }
            else
            {
                if (pressedThisFrame && !guardActionBlocked)
                    ArmParryFromFreshPress();

                if (autoResumeGuardIfHolding && holding && !guardActionBlocked) SetGuarding(true);
                else if (!holding || guardActionBlocked) SetGuarding(false);
            }
        }

    }

    bool ShouldUseLegacyGuardFallback()
    {
        return guardAction == null || guardAction.action == null;
    }

    // ───────────────────── 외부 제어 API(하위 호환) ─────────────────────
    public void StartGuard() => SetGuarding(true);
    public void EndGuard()   => SetGuarding(false);

    public void SetGuarding(bool on)
    {
        bool changed = IsGuarding != on;
        IsGuarding = on;

        if (on)
            RaiseGuardVisualImmediate();

        if (!changed) return;

        if (on) return;

        _lastGuardPressedRealtime = float.NegativeInfinity;
        _parryAvailableThisGuard = false;
        _guardHoldPending = false;
        CloseParryWindow();
        RequestGuardVisualRelease();
    }

    // ───────────────────── 패링 창(애니 이벤트용) ─────────────────────
    public void OpenParryWindow()                          { TryOpenParryWindow(perfectGuardWindow); }
    public void OpenParryWindow(float seconds)             { TryOpenParryWindow(Mathf.Max(0f, seconds)); }
    public void OpenParryWindow(int frames)                { TryOpenParryWindow(Mathf.Max(0, frames) / 60f); }
    public void SetParryWindow(bool open)
    {
        if (!enablePerfectGuard)
            return;

        if (open)
        {
            if (!CanStartParryAttempt())
                return;

            OpenParryWindowImmediate(float.PositiveInfinity);
            return;
        }

        CloseParryWindow();
    }

    public void CloseParryWindow()
    {
        CloseParryWindowInternal(countAsFailure: true);
    }

    public void CancelParryWindow()
    {
        CloseParryWindowInternal(countAsFailure: false);
    }

    public void RegisterParrySuccess()
    {
        _parryAttemptActive = false;
        _parrySuccessPendingClose = true;
        _parryActivateAt = float.NegativeInfinity;
        _parrySpamPenaltyStacks = 0;
        _lastParryAttemptStartedRealtime = float.NegativeInfinity;
        _parryRecoveryUntilRealtime = float.NegativeInfinity;
        _guardRearmBlockedUntilRealtime = Mathf.Max(
            _guardRearmBlockedUntilRealtime,
            Time.realtimeSinceStartup + Mathf.Max(0.05f, parryMoveLock));
    }

    void CloseParryWindowInternal(bool countAsFailure)
    {
        bool shouldEnterRecovery = countAsFailure && _parryAttemptActive && !_parrySuccessPendingClose;
        _parryOpen = false;
        _parryActivateAt = float.NegativeInfinity;
        _parryCloseAt = float.NegativeInfinity;
        _parryAttemptActive = false;
        _parrySuccessPendingClose = false;

        if (shouldEnterRecovery)
            EnterFailedParryRecovery();
    }

    void TryOpenParryWindow(float seconds)
    {
        if (splitTapParryAndHoldGuard) return;
        if (!CanAcceptParryEvent()) return;

        _parryAvailableThisGuard = false;
        OpenParryWindowImmediate(seconds);
    }

    void ArmParryFromFreshPress()
    {
        _lastGuardPressedRealtime = Time.realtimeSinceStartup;
        _parryAvailableThisGuard = enablePerfectGuard;
    }

    void HandleSplitGuardInput(bool pressedThisFrame, bool releasedThisFrame, bool holding)
    {
        if (pressedThisFrame)
        {
            _lastGuardPressedRealtime = Time.realtimeSinceStartup;
            _guardHoldPending = true;
            RaiseGuardVisual(guardVisualReleaseGrace);

            if (CanStartParryAttempt())
            {
                _parryAvailableThisGuard = false;
                BeginParryAttempt(perfectGuardWindow);
            }
        }

        if (releasedThisFrame || !holding)
        {
            _guardHoldPending = false;
            if (IsGuarding) SetGuarding(false);
            else RequestGuardVisualRelease();
            return;
        }

        if (!autoResumeGuardIfHolding || !_guardHoldPending || IsGuarding)
            return;

        if (IsGuardActionBlocked())
            return;

        if (IsParryRecovering || _parryAttemptActive || _parryOpen)
            return;

        if (Time.realtimeSinceStartup < _guardRearmBlockedUntilRealtime)
            return;

        if (Time.realtimeSinceStartup - _lastGuardPressedRealtime >= Mathf.Max(0f, guardHoldDelay))
            SetGuarding(true);
    }

    void BeginParryAttempt(float seconds)
    {
        if (!CanStartParryAttempt())
            return;

        float now = ResolveParryClockTime();
        float startup = Mathf.Max(0f, parryStartupDuration);
        RegisterParryAttemptStart();
        float activeDuration = ResolveParryActiveDuration(seconds);
        _parryAttemptActive = true;
        _parrySuccessPendingClose = false;
        _parryUsesRealtimeClock = useRealtimeParryWindow;
        _parryActivateAt = now + startup;
        _parryOpen = startup <= 0f;
        _parryCloseAt = _parryActivateAt + activeDuration;
    }

    void OpenParryWindowImmediate(float seconds)
    {
        if (!CanStartParryAttempt())
            return;

        float now = ResolveParryClockTime();
        RegisterParryAttemptStart();
        float activeDuration = ResolveParryActiveDuration(seconds);
        _parryAttemptActive = true;
        _parrySuccessPendingClose = false;
        _parryUsesRealtimeClock = useRealtimeParryWindow;
        _parryActivateAt = now;
        _parryOpen = true;
        _parryCloseAt = now + activeDuration;
    }

    void RegisterParryAttemptStart()
    {
        float now = Time.realtimeSinceStartup;
        float chainWindow = Mathf.Max(0.05f, parrySpamChainWindow);

        if (!float.IsFinite(_lastParryAttemptStartedRealtime) || now - _lastParryAttemptStartedRealtime > chainWindow)
            _parrySpamPenaltyStacks = 0;
        else
            _parrySpamPenaltyStacks = Mathf.Min(Mathf.Max(0, maxParrySpamPenaltyStacks), _parrySpamPenaltyStacks + 1);

        _lastParryAttemptStartedRealtime = now;
    }

    float ResolveParryActiveDuration(float requestedDuration)
    {
        float duration = Mathf.Max(0.01f, requestedDuration);
        if (_parrySpamPenaltyStacks <= 0)
            return duration;

        float decay = Mathf.Clamp01(parrySpamWindowDecayPerStack) * _parrySpamPenaltyStacks;
        float scale = Mathf.Max(0.35f, 1f - decay);
        return Mathf.Max(0.03f, duration * scale);
    }

    bool CanStartParryAttempt()
    {
        return enablePerfectGuard
            && !_parryAttemptActive
            && !_parryOpen
            && !IsGuardActionBlocked()
            && !IsGuardBroken
            && !IsInputBlocked()
            && !IsParryRecovering;
    }

    bool CanAcceptParryEvent()
    {
        if (!enablePerfectGuard || !IsGuarding || !_parryAvailableThisGuard || IsParryRecovering || IsGuardActionBlocked())
            return false;

        if (!float.IsFinite(_lastGuardPressedRealtime))
            return false;

        return Time.realtimeSinceStartup - _lastGuardPressedRealtime <= Mathf.Max(0f, parryEventAcceptWindow);
    }

    // ───────────────────── 리액션 트리거(이벤트 포함) ─────────────────────
    public void PlayBlockReaction()
    {
        RaiseGuardVisualImmediate();
        _guardReactionVisualUntilRealtime = Mathf.Max(
            _guardReactionVisualUntilRealtime,
            Time.realtimeSinceStartup + Mathf.Max(0.05f, blockMoveLock));
        if (_hasBlockTrig && animator)
        {
            animator.ResetTrigger(_hashBlockTrig);
            animator.SetTrigger(_hashBlockTrig);
        }
        if (lockMoveOnBlock) LockMoveFor(blockMoveLock);
        OnGuardBlock?.Invoke();
    }

    public void PlayParrySuccess()
    {
        _guardReactionVisualUntilRealtime = float.NegativeInfinity;
        ClearGuardVisualImmediate();
        if (_hasParryTrig && animator)
        {
            animator.ResetTrigger(_hashParryTrig);
            animator.SetTrigger(_hashParryTrig);
        }
        RecoverGuardStrainFromParry();
        if (lockMoveOnParry) LockMoveFor(parryMoveLock);
        if (grantUltimateOnParry && ultimate) ultimate.AddGauge(ultimateGainOnParry);
        OpenParryCounterWindow();
        OnParrySuccess?.Invoke();
    }

    public void OpenParryCounterWindow()
    {
        if (!enableParryCounterWindow)
        {
            _parryCounterUntilRealtime = float.NegativeInfinity;
            return;
        }

        float duration = Mathf.Max(0.05f, parryCounterWindow);
        _parryCounterUntilRealtime = Mathf.Max(_parryCounterUntilRealtime, Time.realtimeSinceStartup + duration);
    }

    public bool TryConsumeParryCounter(out float damageMultiplier, out HitType hitType, out float breakBonus)
    {
        damageMultiplier = 1f;
        hitType = parryCounterHitType;
        breakBonus = 0f;

        if (!IsParryCounterReady)
            return false;

        _parryCounterUntilRealtime = float.NegativeInfinity;
        damageMultiplier = Mathf.Max(1f, parryCounterDamageMultiplier);
        breakBonus = Mathf.Max(0f, parryCounterBreakBonus);
        return true;
    }

    public bool ApplyGuardStrainFromBlock(float blockedDamage)
    {
        if (!enableGuardStrain || maxGuardStrain <= 0f)
            return false;

        float added = Mathf.Max(0f, guardStrainFlatPerBlock + (Mathf.Max(0f, blockedDamage) * guardStrainPerBlockedDamage));
        if (added <= 0f)
            return false;

        _guardStrain = Mathf.Min(maxGuardStrain, _guardStrain + added);
        _guardStrainRecoverAllowedRealtime = Time.realtimeSinceStartup + Mathf.Max(0f, guardStrainRecoverDelay);

        if (!enableGuardBreak)
            return false;

        if (_guardStrain + 0.001f < maxGuardStrain)
            return false;

        TriggerGuardBreak();
        return true;
    }

    public bool ForceGuardBreakFromAttack()
    {
        if (!enableGuardBreak)
            return false;

        if (IsGuardBroken)
            return true;

        TriggerGuardBreak();
        return true;
    }

    public void RecoverGuardStrainFromParry()
    {
        if (!enableGuardStrain || parryStrainRecover <= 0f)
            return;

        _guardStrain = Mathf.Max(0f, _guardStrain - parryStrainRecover);
    }

    // ───────────────────── 가드 판정(대미지 리시버에서 호출) ─────────────────────
    /// <summary>
    /// 히트 포인트 기준 가드/패링 판정
    /// </summary>
    public bool EvaluateDefense(Vector3 hitPoint, out bool isParry, out bool isBlock, out float chipMul)
    {
        isParry = false; isBlock = false; chipMul = chipDamageOnBlock;

        if (!CanResolveGuardBlock() && !CanResolveParryDefense()) return false;

        Vector3 fwd = GetGuardForward().normalized;
        Vector3 dir = (hitPoint - GetGuardOrigin()).normalized;

        if (flattenToGround) { fwd.y = 0f; dir.y = 0f; fwd.Normalize(); dir.Normalize(); }
        if (invertForward) fwd = -fwd;

        float angle = Vector3.Angle(fwd, dir);
        bool inFront = angle <= (frontArcDegrees * 0.5f);
        if (!inFront) return false;

        if (CanResolveParryDefense()) { isParry = true;  return true; }
        if (CanResolveGuardBlock()) { isBlock = true;  return true; }
        return false;
    }

    public Vector3 GetGuardForward()
    {
        if (forwardMode == ForwardMode.CameraForward && cameraTransform) return cameraTransform.forward;
        if (modelForwardSource) return modelForwardSource.forward;
        return transform.forward;
    }
    public Vector3 GetGuardOrigin()
    {
        if (modelForwardSource) return modelForwardSource.position;
        return transform.position;
    }

    void RecoverGuardStrain()
    {
        if (!enableGuardStrain || _guardStrain <= 0f)
            return;

        if (IsGuarding || IsGuardBroken)
            return;

        if (Time.realtimeSinceStartup < _guardStrainRecoverAllowedRealtime)
            return;

        _guardStrain = Mathf.Max(0f, _guardStrain - (Mathf.Max(0f, guardStrainRecoverPerSecond) * Time.unscaledDeltaTime));
    }

    void TriggerGuardBreak()
    {
        _guardBrokenUntilRealtime = Mathf.Max(_guardBrokenUntilRealtime, Time.realtimeSinceStartup + Mathf.Max(0.01f, guardBreakDuration));
        _guardStrainRecoverAllowedRealtime = _guardBrokenUntilRealtime + Mathf.Max(0f, guardStrainRecoverDelay);
        _guardHoldPending = autoResumeGuardIfHolding && IsGuardInputHeldNow();
        _parryAvailableThisGuard = false;
        _parryRecoveryUntilRealtime = float.NegativeInfinity;
        _parrySpamPenaltyStacks = 0;
        _lastParryAttemptStartedRealtime = float.NegativeInfinity;
        _guardRearmBlockedUntilRealtime = float.NegativeInfinity;
        _parryCounterUntilRealtime = float.NegativeInfinity;
        _requireGuardReleaseAfterBreak = false;
        _guardReactionVisualUntilRealtime = float.NegativeInfinity;
        ClearGuardVisualImmediate();

        if (IsGuarding) SetGuarding(false);
        else CloseParryWindow();

        if (_hasGuardBreakTrig && animator)
        {
            animator.ResetTrigger(_hashGuardBreakTrig);
            animator.SetTrigger(_hashGuardBreakTrig);
        }
        LockMoveFor(guardBreakDuration);
        OnGuardBreak?.Invoke();
    }

    bool IsGuardInputHeldNow()
    {
        if (guardAction && guardAction.action != null)
            return guardAction.action.IsPressed();

        if (ShouldUseLegacyGuardFallback())
            return Input.GetKey(KeyCode.E);

        return false;
    }

    // ───────────────────── 이동 잠금 로직 ─────────────────────
    void LockMoveFor(float seconds)
    {
        float duration = Mathf.Max(0f, seconds);

        if (!_moveLockedByTimer)
        {
            if (move) move.SetExternalControl(true);

            if (disableRootMotionDuringLock && animator)
            {
                _prevRootMotion = animator.applyRootMotion;
                animator.applyRootMotion = false;
            }
        }

        _moveLockedByTimer = true;
        _moveUnlockAt = Mathf.Max(_moveUnlockAt, Time.time + duration);

        if (stopVelocityOnLock && rb != null && !rb.isKinematic)
            rb.velocity = Vector3.zero;
    }

    // ───────────────────── 애니 파라미터 캐시/검증 ─────────────────────
    void CacheAnimatorParams()
    {
        _hashGuardBool  = Animator.StringToHash(guardBoolParam);
        _hashAttackBool = Animator.StringToHash(isAttackingBoolParam);
        _hashBlockTrig  = Animator.StringToHash(guardBlockTrigger);
        _hashParryTrig  = Animator.StringToHash(parrySuccessTrigger);
        _hashGuardBreakTrig = Animator.StringToHash(guardBreakTrigger);

        if (!animator) return;

        // 존재 여부 스캔
        _hasGuardBool  = false;
        _hasAttackBool = false;
        _hasBlockTrig  = false;
        _hasParryTrig  = false;
        _hasGuardBreakTrig = false;

        foreach (var p in animator.parameters)
        {
            if (p.name == guardBoolParam && p.type == AnimatorControllerParameterType.Bool)    _hasGuardBool = true;
            if (p.name == isAttackingBoolParam && p.type == AnimatorControllerParameterType.Bool) _hasAttackBool = true;
            if (p.name == guardBlockTrigger && p.type == AnimatorControllerParameterType.Trigger) _hasBlockTrig = true;
            if (p.name == parrySuccessTrigger && p.type == AnimatorControllerParameterType.Trigger) _hasParryTrig = true;
            if (p.name == guardBreakTrigger && p.type == AnimatorControllerParameterType.Trigger) _hasGuardBreakTrig = true;
        }

        if (warnMissingParams)
        {
            if (!_hasGuardBool)  Debug.LogWarning($"[Guard] Animator Bool '{guardBoolParam}' 없음", this);
            if (!_hasAttackBool) Debug.LogWarning($"[Guard] Animator Bool '{isAttackingBoolParam}' 없음", this);
            if (!_hasBlockTrig)  Debug.LogWarning($"[Guard] Animator Trigger '{guardBlockTrigger}' 없음", this);
            if (!_hasParryTrig)  Debug.LogWarning($"[Guard] Animator Trigger '{parrySuccessTrigger}' 없음", this);
        }
    }

    // ───────────────────── 기즈모 ─────────────────────
    void OnDrawGizmosSelected()
    {
        if (!debugDraw) return;
        Vector3 pos = Application.isPlaying ? GetGuardOrigin() : (modelForwardSource ? modelForwardSource.position : transform.position);
        Vector3 fwd = Application.isPlaying ? GetGuardForward() : (modelForwardSource ? modelForwardSource.forward : transform.forward);
        if (invertForward) fwd = -fwd;
        if (flattenToGround) fwd.y = 0f;
        fwd.Normalize();

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pos, pos + fwd * 1.5f);

        // 정면 원뿔 시각화(간략)
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Quaternion left  = Quaternion.AngleAxis(+frontArcDegrees * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(-frontArcDegrees * 0.5f, Vector3.up);
        Vector3 l = left * fwd; Vector3 r = right * fwd;
        Gizmos.DrawLine(pos, pos + l * 1.2f);
        Gizmos.DrawLine(pos, pos + r * 1.2f);
    }

    bool IsInputBlocked()
    {
        return _inputBlocker != null && _inputBlocker.IsBlocked;
    }

    void UpdateTimedStates()
    {
        if (_parryAttemptActive && !_parryOpen && float.IsFinite(_parryActivateAt) && ResolveParryClockTime() >= _parryActivateAt)
            _parryOpen = true;

        if (_parryOpen && float.IsFinite(_parryCloseAt) && ResolveParryClockTime() >= _parryCloseAt)
            CloseParryWindow();

        if (_guardVisualRaised && !ShouldKeepGuardVisualRaised() && Time.realtimeSinceStartup >= ResolveGuardVisualReleaseTime())
            ClearGuardVisualImmediate();

        if (_moveLockedByTimer && Time.time >= _moveUnlockAt)
            ReleaseMoveLock();
    }

    float ResolveParryClockTime()
    {
        return _parryUsesRealtimeClock ? Time.realtimeSinceStartup : Time.time;
    }

    public float GetParryStartupRemaining()
    {
        if (!_parryAttemptActive || _parryOpen || !float.IsFinite(_parryActivateAt))
            return 0f;

        return Mathf.Max(0f, _parryActivateAt - ResolveParryClockTime());
    }

    public float GetParryWindowRemaining()
    {
        if (!_parryOpen || !float.IsFinite(_parryCloseAt))
            return 0f;

        return Mathf.Max(0f, _parryCloseAt - ResolveParryClockTime());
    }

    void EnterFailedParryRecovery()
    {
        float recovery = Mathf.Max(0f, failedParryRecovery)
            + (Mathf.Max(0f, parrySpamRecoveryPenaltyPerStack) * _parrySpamPenaltyStacks);
        if (recovery <= 0f)
            return;

        _parryRecoveryUntilRealtime = Mathf.Max(_parryRecoveryUntilRealtime, Time.realtimeSinceStartup + recovery);
    }

    void RaiseGuardVisualImmediate()
    {
        RaiseGuardVisual(0f);
    }

    void RaiseGuardVisual(float minHoldDuration)
    {
        float holdUntil = Time.realtimeSinceStartup + Mathf.Max(0f, minHoldDuration);
        _guardVisualMinHoldUntilRealtime = Mathf.Max(_guardVisualMinHoldUntilRealtime, holdUntil);

        if (_guardVisualRaised)
            return;

        _guardVisualRaised = true;
        if (_hasGuardBool && animator)
            animator.SetBool(_hashGuardBool, true);
    }

    void RequestGuardVisualRelease()
    {
        if (ShouldKeepGuardVisualRaised())
            return;

        if (Time.realtimeSinceStartup >= ResolveGuardVisualReleaseTime())
            ClearGuardVisualImmediate();
    }

    void ClearGuardVisualImmediate()
    {
        _guardVisualMinHoldUntilRealtime = float.NegativeInfinity;
        _guardReactionVisualUntilRealtime = float.NegativeInfinity;
        if (!_guardVisualRaised)
            return;

        _guardVisualRaised = false;
        if (_hasGuardBool && animator)
            animator.SetBool(_hashGuardBool, false);
    }

    bool ShouldKeepGuardVisualRaised()
    {
        return IsGuarding
            || _guardHoldPending
            || _parryAttemptActive
            || _parryOpen
            || Time.realtimeSinceStartup < _guardReactionVisualUntilRealtime;
    }

    float ResolveGuardVisualReleaseTime()
    {
        return Mathf.Max(_guardVisualMinHoldUntilRealtime, _guardReactionVisualUntilRealtime);
    }

    bool IsGuardActionBlocked()
    {
        if (IsGuardBroken)
            return true;

        if (_combatController != null && _combatController.IsInHit)
            return true;

        return denyGuardWhileAttacking && ResolveAttackStateActive();
    }

    bool ResolveAttackStateActive()
    {
        if (_combatController != null && _combatController.IsAttacking)
            return true;

        return animator != null && _hasAttackBool && animator.GetBool(_hashAttackBool);
    }

    public bool CanResolveParryDefense()
    {
        return _parryOpen && !IsGuardActionBlocked();
    }

    public bool CanResolveGuardBlock()
    {
        return IsGuarding && !IsGuardActionBlocked();
    }

    void ReleaseMoveLock()
    {
        _moveLockedByTimer = false;
        _moveUnlockAt = float.NegativeInfinity;

        if (move)
            move.SetExternalControl(false);

        if (disableRootMotionDuringLock && animator)
            animator.applyRootMotion = _prevRootMotion;
    }
}
