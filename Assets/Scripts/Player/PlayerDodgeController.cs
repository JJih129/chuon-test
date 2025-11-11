using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerDodgeController : MonoBehaviour
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("① 참조")]
    [SerializeField] Transform playerRoot;                 // [조절값] 진행 방향 기준
    [SerializeField] Transform cameraTransform;            // [조절값] 입력을 월드로 변환
    [SerializeField] PlayerMoveController move;            // [조절값] 이동 잠금 연동
    [SerializeField] PlayerLockOn playerLockOn;            // [조절값] 락온 시 방향 결정에 영향
    [SerializeField] Animator animator;                    // [조절값] 애니 동기화(선택)
    [SerializeField] PlayerUltimateController ultimate;    // [조절값] 궁극기 게이지(선택)

    [Header("② 회피 기본")]
    [SerializeField] KeyCode dodgeKey = KeyCode.LeftShift; // [조절값] 회피 키
    [SerializeField, Range(4f,28f)] float dodgeSpeed = 18f;// [조절값] 회피 속도(m/s)
    [SerializeField, Range(0.05f,0.6f)] float dodgeDuration = 0.25f; // [조절값] 회피 시간
    [SerializeField, Range(0f,1f)] float dodgeCooldown = 0f; // [조절값] 쿨타임(기획서: 0)

    [Header("③ 퍼펙트 회피")]
    [Tooltip("적 공격 예고 후, 이 시간(초) 안에 회피 시작 시 퍼펙트 판정")]
    [SerializeField, Range(0.03f,0.35f)] float perfectWindow = 0.16f; // [조절값] 패링급 윈도우
    [Tooltip("퍼펙트 회피 슬로모션 타임스케일")]
    [SerializeField, Range(0.1f,0.5f)] float slowTimeScale = 0.2f;    // [조절값]
    [Tooltip("퍼펙트 회피 슬로모션 지속시간(초)")]
    [SerializeField, Range(0.05f,0.6f)] float slowDuration = 0.3f;    // [조절값]
    [Tooltip("퍼펙트 회피 시 궁극기 게이지 증가량")]
    [SerializeField, Range(0f,50f)] float perfectGaugeBonus = 5f;     // [조절값]

    [Header("④ Animator 파라미터")]
    [SerializeField] string p_IsDodging = "IsDodging";     // [조절값] 회피 중 Bool
    [SerializeField] string p_PerfectDodge = "PerfectDodge"; // [조절값] 퍼펙트 트리거

    [Header("⑤ 이벤트")]
    public UnityEvent OnDodgeStart;
    public UnityEvent OnDodgeEnd;
    public UnityEvent OnPerfectDodge;

    // ===== 내부 =====
    CharacterController cc;
    float cdTimer;
    bool isDodging;
    float dodgeTimer;
    Vector3 dodgeDir;
    float lastTelegraphTime = -999f; // 가장 최근 “공격 예고” 시각

    void Reset()
    {
        if (!playerRoot) playerRoot = transform;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!move) move = GetComponent<PlayerMoveController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!ultimate) ultimate = GetComponent<PlayerUltimateController>();
    }
    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!playerRoot) playerRoot = transform;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!move) move = GetComponent<PlayerMoveController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!ultimate) ultimate = GetComponent<PlayerUltimateController>();
    }

    void Update()
    {
        cdTimer -= Time.unscaledDeltaTime;

        // 진행 중
        if (isDodging)
        {
            cc.Move(dodgeDir * dodgeSpeed * Time.deltaTime);
            dodgeTimer -= Time.deltaTime;
            if (dodgeTimer <= 0f) EndDodge();
            return;
        }

        // 입력
        if (Input.GetKeyDown(dodgeKey) && cdTimer <= 0f)
        {
            StartDodge();
        }
    }

    // ===== 퍼블릭: 적이 공격 “예고”할 때 호출(선택) =====
    // 예: 투사체 발사 직전, 근접 공격 판정 직전 등에서 playerDodge.RegisterTelegraph();
    public void RegisterTelegraph()
    {
        lastTelegraphTime = Time.time;
    }
    public void RegisterTelegraphWithLead(float leadSeconds)
    {
        // 리드타임이 있으면 예고 시각을 앞당겨 판정 폭 보정
        lastTelegraphTime = Time.time - Mathf.Max(0f, leadSeconds);
    }

    // ===== 로직 =====
    void StartDodge()
    {
        // 방향 결정: 입력 또는 전방
        Vector2 in2 = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        Transform basis = cameraTransform ? cameraTransform : playerRoot;
        Vector3 fwd = Flat(basis.forward), rgt = Flat(basis.right);
        Vector3 wish = (rgt * in2.x + fwd * in2.y);
        dodgeDir = (wish.sqrMagnitude > 0.01f) ? wish.normalized : Flat(playerRoot.forward);

        // 상태 전환
        isDodging = true; dodgeTimer = dodgeDuration; cdTimer = dodgeCooldown;
        move?.OnDodgeStart();
        if (animator && !string.IsNullOrEmpty(p_IsDodging)) animator.SetBool(p_IsDodging, true);
        OnDodgeStart?.Invoke();

        // 퍼펙트 판정
        bool perfect = (Time.time - lastTelegraphTime) <= perfectWindow;
        if (perfect)
        {
            // 궁게이지
            if (ultimate) ultimate.AddGauge(perfectGaugeBonus);

            // 애니 트리거
            if (animator && !string.IsNullOrEmpty(p_PerfectDodge)) animator.SetTrigger(p_PerfectDodge);

            // 슬로모션(내장)
            StartCoroutine(CoSlowmo(slowTimeScale, slowDuration));

            OnPerfectDodge?.Invoke();
        }
    }

    void EndDodge()
    {
        isDodging = false;
        move?.OnDodgeEnd();
        if (animator && !string.IsNullOrEmpty(p_IsDodging)) animator.SetBool(p_IsDodging, false);
        OnDodgeEnd?.Invoke();
    }

    IEnumerator CoSlowmo(float scale, float dur)
    {
        float prevScale = Time.timeScale;
        float prevFixed = Time.fixedDeltaTime;
        Time.timeScale = Mathf.Clamp(scale, 0.05f, 1f);
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime; // 슬로모션과 무관하게 흐름 유지
            yield return null;
        }
        Time.timeScale = prevScale;
        Time.fixedDeltaTime = prevFixed;
    }

    static Vector3 Flat(Vector3 v){ v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

#if UNITY_EDITOR
    [ContextMenu("Debug/Telegraph")]
    void DebugTele() => RegisterTelegraph();
#endif
}
