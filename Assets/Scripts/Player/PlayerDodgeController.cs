using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerDodgeController : MonoBehaviour
{
    [Header("① 참조")]
    [SerializeField] Transform playerRoot;
    [SerializeField] Transform cameraTransform;
    [SerializeField] PlayerMoveController move;     // 일반 이동 컴포넌트(있으면)
    [SerializeField] PlayerLockOn playerLockOn;     // 락온 방향 보정(선택)
    [SerializeField] Animator animator;             // 애니(선택)
    [SerializeField] CombatMoveLocker moveLocker;   // ★ 이동 잠금 전담

    [Header("② 입력")]
    [SerializeField] KeyCode dodgeKey = KeyCode.LeftShift;

    [Header("③ 이동 조절(속도/거리 중 택1)")]
    [Tooltip("끄면 '속도 기반', 켜면 '거리 기반' 회피")]
    [SerializeField] bool useDistanceBased = false;
    [SerializeField, Range(4f, 28f)]  float dodgeSpeed = 18f;
    [SerializeField, Range(1f, 12f)]  float dodgeDistance = 5f;
    [SerializeField, Range(0.05f, .6f)] float dodgeDuration = 0.25f;
    [SerializeField, Range(0f, 1f)]   float dodgeCooldown = 0f;

    [Header("④ 속도 곡선(0~1)")]
    [Tooltip("시간 정규화 t(0~1)에 대한 속도 배율")]
    [SerializeField] AnimationCurve speedCurve = AnimationCurve.Linear(0,1, 1,1);

    [Header("⑤ 애니 파라미터")]
    [SerializeField] string p_IsDodging = "IsDodging";
    [SerializeField] string p_DodgeTrigger = "Dodge";

    [Header("⑥ 이동 잠금 옵션(대시 동안)")]
    [Tooltip("회피 진행 중 일반 이동 금지")]
    [SerializeField] bool lockMoveDuringDodge = true;
    [SerializeField] bool zeroVelocityOnDodge = true;
    [SerializeField] bool disableRootMotionOnDodge = true;

    [Header("⑦ 이벤트")]
    public UnityEvent OnDodgeStart;
    public UnityEvent OnDodgeEnd;

    // 내부
    CharacterController cc;
    float cdTimer;
    bool isDodging;
    float elapsed;
    Vector3 dodgeDir;
    float baseSpeed;

    void Reset()
    {
        if (!playerRoot) playerRoot = transform;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!move) move = GetComponent<PlayerMoveController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!moveLocker) moveLocker = GetComponent<CombatMoveLocker>();
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!playerRoot) playerRoot = transform;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!move) move = GetComponent<PlayerMoveController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!moveLocker) moveLocker = GetComponent<CombatMoveLocker>();
    }

    void Update()
    {
        cdTimer -= Time.unscaledDeltaTime;

        if (isDodging) { TickDodge(); return; }

        if (Input.GetKeyDown(dodgeKey) && cdTimer <= 0f)
            StartDodge();
    }

    void StartDodge()
    {
        // 방향 결정(락온시: 캐릭터 기준, 아니면 카메라 기준)
        Vector2 in2 = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        Transform basis = (playerLockOn && playerLockOn.HasTarget) ? playerRoot
                             : (cameraTransform ? cameraTransform : playerRoot);
        Vector3 fwd = Flat(basis.forward), rgt = Flat(basis.right);
        Vector3 wish = (rgt * in2.x + fwd * in2.y);
        dodgeDir = (wish.sqrMagnitude > 0.001f) ? wish.normalized : Flat(playerRoot.forward);

        baseSpeed = useDistanceBased
            ? Mathf.Max(0.01f, dodgeDistance / Mathf.Max(0.01f, dodgeDuration))
            : dodgeSpeed;

        isDodging = true; elapsed = 0f;
        cdTimer = dodgeCooldown;

        // ★ 대시 동안 일반 이동 잠금
        if (lockMoveDuringDodge && moveLocker)
            moveLocker.Lock("DODGE", dodgeDuration, zeroVelocityOnDodge, disableRootMotionOnDodge);

        if (animator)
        {
            if (!string.IsNullOrEmpty(p_IsDodging)) animator.SetBool(p_IsDodging, true);
            if (!string.IsNullOrEmpty(p_DodgeTrigger)) animator.SetTrigger(p_DodgeTrigger);
        }
        OnDodgeStart?.Invoke();
    }

    void TickDodge()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, dodgeDuration));
        float instSpeed = baseSpeed * Mathf.Max(0f, speedCurve.Evaluate(t));

        // 대시 이동(일반 이동 컴포넌트는 잠겨있어야 함)
        cc.Move(dodgeDir * instSpeed * Time.deltaTime);

        if (elapsed >= dodgeDuration) EndDodge();
    }

    void EndDodge()
    {
        isDodging = false;
        // 안전 해제(자동해제 타이머가 이미 끝나도 중복 호출 무해)
        if (lockMoveDuringDodge && moveLocker)
            moveLocker.Unlock("DODGE");

        if (animator && !string.IsNullOrEmpty(p_IsDodging)) animator.SetBool(p_IsDodging, false);
        OnDodgeEnd?.Invoke();
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f) return Vector3.forward;
        return v.normalized;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying && useDistanceBased)
        {
            Gizmos.color = new Color(0, 1, 1, 0.4f);
            var dir = playerRoot ? Flat(playerRoot.forward) : Vector3.forward;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.05f, dir * dodgeDistance);
        }
    }
#endif
}
