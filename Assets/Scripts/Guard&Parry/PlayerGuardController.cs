using UnityEngine;
using UnityEngine.Events;
using System.Collections;

[DisallowMultipleComponent]
public class PlayerGuardController : MonoBehaviour
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("① 전방 기준(각도 판정 기준)")]
    [SerializeField] private Transform playerRoot;         // [조절값] 전방 벡터 기준. 비우면 transform

    [Header("① 애니메이터")]
    [SerializeField] private Animator animator;            // [조절값] 가드 파라미터/트리거 대상

    [Header("② 입력")]
    [SerializeField] private bool useLegacyInput = true;   // [조절값] 레거시 입력 사용 여부
    [SerializeField] private KeyCode guardKey = KeyCode.E; // [조절값] 가드(홀드) 키

    [Header("③ 애니 파라미터 이름")]
    [SerializeField] private string p_IsGuarding   = "IsGuarding";   // [조절값] Bool
    [SerializeField] private string p_ParrySuccess = "ParrySuccess"; // [조절값] Trigger
    [SerializeField] private string p_GuardBlock   = "GuardBlock";   // [조절값] Trigger
    [SerializeField] private string guardLayerName = "GuardLayer";   // [조절값] 있으면 가중치 제어

    [Header("④ 전투 정책")]
    [SerializeField, Range(30f,180f)] private float guardConeAngle = 140f; // [조절값] 정면 허용 각(도)
    [SerializeField] private bool disallowGuardWhileAttacking = false;     // [조절값] 공격 중 가드 금지
    [SerializeField] private string attackingBoolParam = "IsAttacking";    // [조절값] 공격 중 판단용 Bool(없으면 무시)

    [Header("⑤ 저스트가드(패링키 없음)")]
    [SerializeField, Range(0.03f,0.35f)] private float justGuardWindow = 0.15f; // [조절값] 가드 올린 직후 패링 윈도우
    [SerializeField] private bool requireAngleForJustGuard = true;             // [조절값] 패링도 각도 요구 여부
    [SerializeField, Range(0f,0.5f)] private float animEventParryWindow = 0.15f; // [조절값] 애니 이벤트로 연 패링창

    [Header("⑥ 가드 피해 조정")]
    [SerializeField, Range(0f,1f)] private float guardDamageMultiplier = 0.0f;   // [조절값] 칩 대미지 비율(0=완전막기)

    [Header("⑦ 연출(옵션)")]
    [SerializeField, Range(0f,0.4f)] private float parryIFrame = 0.25f;     // [조절값] 패링 성공 i-프레임
    [SerializeField, Range(0f,0.2f)] private float hitstopOnParry = 0.06f;  // [조절값] 패링 히트스톱
    public UnityEvent<float> OnRequestHitstop;                               // [이벤트] 외부 히트스톱 시스템 연동
    public UnityEvent OnGuardStart, OnGuardEnd, OnParrySuccess, OnGuardBlock;// [이벤트] UI/사운드 등 연동

    [Header("⑧ 디버그")]
    [SerializeField] private bool debugDraw = false; // [조절값] 기즈모 표시

    // 내부 캐시
    int hIsGuarding, hParry, hBlock, guardLayerIndex = -1;
    bool isGuarding;
    bool guardHolding;
    float justGuardUntil = -1f;

    void Reset()
    {
        if (!playerRoot) playerRoot = transform;
        if (!animator)   animator   = GetComponentInChildren<Animator>();
    }

    void Awake()
    {
        if (!playerRoot) playerRoot = transform;
        if (!animator)   animator   = GetComponentInChildren<Animator>();

        hIsGuarding = string.IsNullOrEmpty(p_IsGuarding)   ? 0 : Animator.StringToHash(p_IsGuarding);
        hParry      = string.IsNullOrEmpty(p_ParrySuccess) ? 0 : Animator.StringToHash(p_ParrySuccess);
        hBlock      = string.IsNullOrEmpty(p_GuardBlock)   ? 0 : Animator.StringToHash(p_GuardBlock);
        guardLayerIndex = string.IsNullOrEmpty(guardLayerName) ? -1 : animator.GetLayerIndex(guardLayerName);
        if (guardLayerIndex >= 0) animator.SetLayerWeight(guardLayerIndex, 0f);
    }

    void Update()
    {
        if (useLegacyInput)
        {
            if (Input.GetKeyDown(guardKey)) StartGuard();
            if (Input.GetKeyUp(guardKey))   EndGuard();
        }
        // 새 InputSystem이면 외부에서 StartGuard/EndGuard 호출
    }

    // 외부 제어용
    public void StartGuard()
    {
        if (IsAttacking() && disallowGuardWhileAttacking) return;

        guardHolding = true;
        if (isGuarding) return;

        isGuarding     = true;
        justGuardUntil = Time.time + justGuardWindow;

        if (hIsGuarding != 0) animator.SetBool(hIsGuarding, true);
        if (guardLayerIndex >= 0) animator.SetLayerWeight(guardLayerIndex, 1f);
        OnGuardStart?.Invoke();
    }

    public void EndGuard()
    {
        guardHolding = false;
        if (!isGuarding) return;

        isGuarding     = false;
        justGuardUntil = -1f;

        if (hIsGuarding != 0) animator.SetBool(hIsGuarding, false);
        if (guardLayerIndex >= 0) animator.SetLayerWeight(guardLayerIndex, 0f);
        OnGuardEnd?.Invoke();
    }

    public void ForceEndGuard() => EndGuard();

    // 애니메이션 이벤트 연동용(선택)
    public void OpenParryWindow()
    {
        float until = Time.time + Mathf.Max(0f, animEventParryWindow);
        if (until > justGuardUntil) justGuardUntil = until;
    }
    public void OpenParryWindowWithTimes(float startup, float active, float recovery)
    {
        StartCoroutine(CoOpenParry(startup, active));
    }
    IEnumerator CoOpenParry(float startup, float active)
    {
        if (startup > 0f) yield return new WaitForSeconds(startup);
        float until = Time.time + Mathf.Max(0f, active);
        if (until > justGuardUntil) justGuardUntil = until;
    }

    // ===== 대미지 해결 진입점(피격 시스템에서 호출) =====
    // attackerPos: 공격자 월드 위치, isParryable: 패링 가능 타격인지, baseDamage: 원대미지
    public float ResolveIncomingAttack(Vector3 attackerPos, bool isParryable, float baseDamage, GameObject attacker = null, Vector3? hitPoint = null)
    {
        // 1) 공격 중 금지 옵션이면 즉시 실패
        if (IsAttacking() && disallowGuardWhileAttacking)
        {
            ForceEndGuard();
            return baseDamage;
        }

        // 2) 각도 판정
        Vector3 toAttacker = attackerPos - playerRoot.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude < 0.0001f) return baseDamage;

        float angle = Vector3.Angle(playerRoot.forward, toAttacker);
        bool withinAngle = angle <= guardConeAngle * 0.5f;

        // 3) 패링 윈도우
        bool inJust = Time.time <= justGuardUntil;
        if (isGuarding && isParryable && inJust && (!requireAngleForJustGuard || withinAngle))
        {
            if (hParry != 0) animator.SetTrigger(hParry);
            OnParrySuccess?.Invoke();
            if (parryIFrame > 0f) StartCoroutine(CoIFrame(parryIFrame));
            DoHitstop(hitstopOnParry);
            return 0f;
        }

        // 4) 일반 가드 블록
        if (isGuarding && withinAngle)
        {
            if (hBlock != 0) animator.SetTrigger(hBlock);
            OnGuardBlock?.Invoke();
            return Mathf.Max(0f, baseDamage * guardDamageMultiplier);
        }

        // 5) 가드 실패
        return baseDamage;
    }

    // ===== 유틸 =====
    bool IsAttacking()
    {
        if (!animator || string.IsNullOrEmpty(attackingBoolParam)) return false;
        int h = Animator.StringToHash(attackingBoolParam);
        // 파라미터가 없어도 예외 없이 false 처리
        try { return animator.GetBool(h); }
        catch { return false; }
    }

    IEnumerator CoIFrame(float t)
    {
        // 프로젝트에 무적 토글이 없으니 여기서는 타임스케일/레이어 조작 없이 대기만.
        yield return new WaitForSeconds(t);
    }

    void DoHitstop(float seconds)
    {
        if (seconds <= 0f) return;
        if (OnRequestHitstop != null && OnRequestHitstop.GetPersistentEventCount() > 0)
        {
            OnRequestHitstop.Invoke(seconds);
            return;
        }
        StartCoroutine(CoHitstop(seconds));
    }

    IEnumerator CoHitstop(float t)
    {
        float prev = Time.timeScale;
        Time.timeScale = 0f;
        float end = Time.unscaledTime + t;
        while (Time.unscaledTime < end) yield return null;
        Time.timeScale = prev;
    }

    void OnDrawGizmosSelected()
    {
        if (!debugDraw) return;
        Transform t = playerRoot ? playerRoot : transform;
        Vector3 pos = t.position + Vector3.up * 1.0f;
        float half = guardConeAngle * 0.5f;
        Vector3 fwd = t.forward;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(pos, Quaternion.AngleAxis(-half, Vector3.up) * fwd * 2f);
        Gizmos.DrawRay(pos, Quaternion.AngleAxis(half, Vector3.up) * fwd * 2f);
        Gizmos.DrawRay(pos, fwd * 2f);
    }
}
