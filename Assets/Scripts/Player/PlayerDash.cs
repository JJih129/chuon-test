// ============================================================================
// 파일명: PlayerDash.cs (개선 버전)
// 역할: 회피 시스템 + 퍼펙트 회피 (패링 타이밍 일치 시 슬로모션)
// 의존성: ICombatStateReader, IInputBlocker, PlayerUltimateController
// 작성 기준: 전투 시스템 기획서 v1.2
// ============================================================================

using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
public class PlayerDash : MonoBehaviour
{
    // ========================= 인스펙터 파라미터 =========================
    [Header("회피 설정")]
    [Tooltip("회피 속도")]
    [SerializeField] float dashSpeed = 18f;
    
    [Tooltip("회피 지속 시간(초)")]
    [SerializeField] float dashDuration = 0.25f;
    
    [Tooltip("회피 쿨타임(초)")]
    [SerializeField] float dashCooldown = 0f; // 기획서: 쿨타임 없음

    [Header("퍼펙트 회피 설정")]
    [Tooltip("퍼펙트 회피 판정 윈도우 (패링 타이밍과 동일, 프레임 기준)")]
    [SerializeField] int perfectDodgeWindowFrames = 10; // 5~15프레임 중 중간값
    
    [Tooltip("퍼펙트 회피 슬로모션 지속시간(초)")]
    [SerializeField] float perfectDodgeSlowDuration = 0.3f;
    
    [Tooltip("퍼펙트 회피 시 타임스케일")]
    [Range(0.1f, 0.5f)]
    [SerializeField] float perfectDodgeTimeScale = 0.2f;
    
    [Tooltip("퍼펙트 회피 시 궁극기 게이지 증가량")]
    [SerializeField] float perfectDodgeGaugeBonus = 5f;

    [Header("Animator 파라미터")]
    [Tooltip("회피 중 Bool 파라미터")]
    [SerializeField] string dodgingParam = "IsDodging";
    
    [Tooltip("퍼펙트 회피 트리거 파라미터")]
    [SerializeField] string perfectDodgeTrigger = "PerfectDodge";

    [Header("이벤트")]
    public UnityEvent OnDashStarted;
    public UnityEvent OnPerfectDodge;

    // ========================= 내부 상태 =========================
    CharacterController _controller;
    Animator _animator;
    ICombatStateReader _combatState;
    IInputBlocker _inputBlocker;
    PlayerUltimateController _ultimate;
    
    Vector3 _dashDirection = Vector3.zero;
    float _dashTimer = 0f;
    float _dashCooldownTimer = 0f;
    
    bool _isDashing = false;
    int _framesSinceLastAttack = 999; // 퍼펙트 회피 판정용

    // ========================= 퍼블릭 API =========================
    public bool IsDashing => _isDashing;

    // ========================= 라이프사이클 =========================
    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
        _combatState = GetComponent<ICombatStateReader>();
        _inputBlocker = GetComponent<IInputBlocker>();
        _ultimate = GetComponent<PlayerUltimateController>();
    }

    void Update()
    {
        // 입력 차단 중이면 무시
        if (_inputBlocker != null && _inputBlocker.IsBlocked) return;

        // 경직 중 회피 금지
        if (_combatState != null && _combatState.IsStaggered()) return;

        _dashCooldownTimer -= Time.deltaTime;

        // 회피 중
        if (_isDashing)
        {
            _controller.Move(_dashDirection * dashSpeed * Time.deltaTime);
            _dashTimer -= Time.deltaTime;
            
            if (_dashTimer <= 0f)
            {
                EndDash();
            }
            return;
        }

        // 공격 중 회피 금지 (단, 프레임 카운트는 추적)
        if (_combatState != null && _combatState.IsAttacking())
        {
            _framesSinceLastAttack = 0;
            return;
        }
        else
        {
            _framesSinceLastAttack++;
        }

        // 회피 입력 (Shift)
        if (Input.GetKeyDown(KeyCode.LeftShift) && _dashCooldownTimer <= 0f)
        {
            StartDash();
        }
    }

    // ========================= 회피 로직 =========================
    void StartDash()
    {
        // 이동 방향 결정 (입력 방향 또는 전방)
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        
        Vector3 inputDir = new Vector3(h, 0, v);
        
        if (inputDir.sqrMagnitude > 0.1f)
        {
            // 카메라 기준 방향 변환
            Transform cam = Camera.main?.transform;
            if (cam != null)
            {
                Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                Vector3 camRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                _dashDirection = (camForward * v + camRight * h).normalized;
            }
            else
            {
                _dashDirection = inputDir.normalized;
            }
        }
        else
        {
            // 입력 없으면 전방으로
            _dashDirection = transform.forward;
        }

        _isDashing = true;
        _dashTimer = dashDuration;
        _dashCooldownTimer = dashCooldown;

        // Animator 동기화
        if (_animator != null)
        {
            _animator.SetBool(dodgingParam, true);
        }

        // 퍼펙트 회피 체크
        CheckPerfectDodge();

        OnDashStarted?.Invoke();
        Debug.Log($"[Dash] 회피 시작 (방향: {_dashDirection})");
    }

    void EndDash()
    {
        _isDashing = false;

        // Animator 동기화
        if (_animator != null)
        {
            _animator.SetBool(dodgingParam, false);
        }
    }

    void CheckPerfectDodge()
    {
        // 최근 공격 종료 후 N프레임 이내에 회피하면 퍼펙트 회피
        if (_framesSinceLastAttack <= perfectDodgeWindowFrames)
        {
            TriggerPerfectDodge();
        }
    }

    void TriggerPerfectDodge()
    {
        Debug.Log($"[Dash] 퍼펙트 회피! (프레임: {_framesSinceLastAttack})");

        // 슬로모션
        TimeScaleController.Instance?.SetSlowMotion(perfectDodgeTimeScale, perfectDodgeSlowDuration);

        // 궁극기 게이지 증가
        if (_ultimate != null)
        {
            _ultimate.AddGauge(perfectDodgeGaugeBonus);
        }

        // Animator 트리거
        if (_animator != null && !string.IsNullOrEmpty(perfectDodgeTrigger))
        {
            _animator.SetTrigger(perfectDodgeTrigger);
        }

        OnPerfectDodge?.Invoke();
    }

    // ========================= 디버그 =========================
#if UNITY_EDITOR
    [ContextMenu("Debug/Force Dash")]
    void DebugDash()
    {
        _dashDirection = transform.forward;
        StartDash();
    }

    [ContextMenu("Debug/Trigger Perfect Dodge")]
    void DebugPerfectDodge()
    {
        TriggerPerfectDodge();
    }
#endif
}