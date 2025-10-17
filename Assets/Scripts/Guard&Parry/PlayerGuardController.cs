// PlayerGuardController.cs
// AAA 소울류 스타일 가드+패링 일체형 컨트롤러(기획서 v1.2 반영 확장판)
// - 각도/스태미나/칩/브레이크/패링 타이밍 + i프레임
// - 투사체 가드 튕김/패링 반사, AOE 각도 우회
// - 포이즈(브레이크) 누적 훅, 히트스톱/카메라셰이크 이벤트
// - 보스별 튜닝 ScriptableObject, 패링 가능 전역 오버라이드
// - 기존 ResolveIncomingAttack 시그니처 호환, OpenParryWindow 별칭 제공

using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Linq;

// ===== 이벤트 유틸(인스펙터에서 float 인자 전달용) =====
[System.Serializable] public class FloatEvent : UnityEvent<float> { }

[DisallowMultipleComponent]
public class PlayerGuardController : MonoBehaviour
{
    // =========================
    // ① 참조 | 외부 연동 객체들
    // =========================
    [Header("① 참조 | 전방/회전 기준 Transform (비우면 자기 자신)")]
    public Transform playerRoot;

    [Header("① 참조 | 전투상태 리더(ICombatStateReader) - 공격중 여부 판정")]
    public MonoBehaviour combatStateReaderBehaviour; // ICombatStateReader
    ICombatStateReader _stateReader;

    [Header("① 참조 | 입력 차단기(IInputBlocker) - 경직/브레이크 시 입력막기")]
    public MonoBehaviour inputBlockerBehaviour; // IInputBlocker
    IInputBlocker _inputBlocker;

    [Header("① 참조 | 무적 토글(IInvulnerabilityToggle) - 패링 i-프레임")]
    public MonoBehaviour invulnerabilityBehaviour; // IInvulnerabilityToggle
    IInvulnerabilityToggle _invul;

    [Header("① 참조 | 애니메이터(선택) - 공격중 가드 레이어 억제")]
    public Animator animator;

    [Header("① 참조 | 효과음 AudioSource(선택)")]
    public AudioSource sfx;

    // =========================
    // ② 입력 | 레거시 키 사용 여부
    // =========================
    [Header("② 입력 | 레거시 Input 사용 여부(true면 이 스크립트가 키 직접 읽음)")]
    public bool useLegacyInput = false;

    [Header("② 입력 | 가드(홀드) 키")]
    public KeyCode guardKey = KeyCode.E;

    [Header("② 입력 | 패링(탭) 키")]
    public KeyCode parryKey = KeyCode.Q;

    // =========================
    // ③ 공통 | 각도/정책/레이어
    // =========================
    [Header("③ 각도 | 정면 가드 허용 각도(도) (120~160 권장)")]
    [Range(0f, 180f)] public float guardConeAngle = 140f;

    [Header("③ 정책 | 공격 중 가드 입력 불가")]
    public bool disallowGuardDuringAttack = true;

    [Header("③ 정책 | 공격 시작 시 가드 즉시 해제")]
    public bool endGuardImmediatelyOnAttackStart = true;

    [Header("③ 정책 | 공격 중 지정 레이어 가중치 강제 0")]
    public bool enforceLayerSuppressDuringAttack = true;

    [Header("③ 레이어 | 공격 중 0으로 만들 레이어 이름들(예: GuardLayer)")]
    public string[] suppressLayerNamesDuringAttack;

    [Header("③ 정책 | 공격 종료 시 가드키 유지 중이면 자동 재가드")]
    public bool autoResumeGuardIfHolding = true;

    // =========================
    // ④ 가드 | 칩/스태미나/브레이크
    // =========================
    [Header("④ 가드 | 가드시 최종 피해 비율(0=완전방어, 1=감소없음)")]
    [Range(0f, 1f)] public float guardDamageMultiplier = 0.25f;

    [Header("④ 가드 | 칩데미지 하한(최소 피해)")]
    public float guardChipMin = 1f;

    [Header("④ 가드 | 1회 막기 기본 스태미나 소모")]
    public float guardStaminaCostBase = 10f;

    [Header("④ 가드 | (받은 원딜)×계수 만큼 추가 소모")]
    public float guardStaminaPerDamage = 0.5f;

    [Header("④ 가드 | 최대 스태미나")]
    public float staminaMax = 100f;

    [Header("④ 가드 | 초당 스태미나 회복(가드/패링 중 0 처리)")]
    public float staminaRegenPerSec = 15f;

    [Header("④ 가드 | 스태미나 임계값(이하로 떨어지면 가드브레이크)")]
    public float guardBreakThreshold = 0f;

    [Header("④ 가드 | 가드브레이크 경직 시간(입력 차단 시간)")]
    public float guardBreakStun = 1.2f;

    // =========================
    // ⑤ 패링 | 타이밍/i-프레임/반격
    // =========================
    [Header("⑤ 패링 | 스타트업(초) - 입력 후 유효 전 대기")]
    public float parryStartup = 0.05f;

    [Header("⑤ 패링 | 액티브(초) - 이 구간에 들어온 공격 패링 성공")]
    public float parryActive = 0.12f;

    [Header("⑤ 패링 | 리커버리(초) - 실패 시 가드불가/경직 구간")]
    public float parryRecovery = 0.35f;

    [Header("⑤ 패링 | 성공 시 i-프레임(초, 0=미사용)")]
    public float parryIFrame = 0.25f;

    [Header("⑤ 패링 | 각도 제한 적용(정면 콘 안에서만 패링 허용)")]
    public bool parryRequiresGuardAngle = true;

    [Header("⑤ 패링 | 성공 시 공격자 경직 시간(초) 전달")]
    public float parryStunToAttacker = 1.0f;

    // =========================
    // ⑥ 특수 | 투사체/광역/포이즈
    // =========================
    [Header("⑥ 투사체 | 가드시 투사체 튕김 허용")]
    public bool allowProjectileDeflect = true;

    [Header("⑥ 투사체 | 패링 성공 시 투사체 반사")]
    public bool reflectOnParry = true;

    [Header("⑥ 투사체 | 반사 속도 배수(리지드바디 velocity 스케일)")]
    public float reflectSpeedMultiplier = 1.1f;

    [Header("⑥ 투사체 | 가드시 투사체 피해 비율(0~1)")]
    [Range(0f, 1f)] public float projectileGuardDamageMultiplier = 0.5f;

    [Header("⑥ 광역 | AOE 공격은 각도 판정 우회(태그 'AOE' 또는 마커 컴포넌트)")]
    public bool treatAoeAsAngleBypass = true;

    [Header("⑥ 포이즈 | 가드 성공 시 적(공격자) 포이즈 누적량")]
    public float poiseOnGuard = 2f;

    [Header("⑥ 포이즈 | 패링 성공 시 적 포이즈 누적량")]
    public float poiseOnParrySuccess = 12f;

    // =========================
    // ⑦ 연출 | 히트스톱/카메라셰이크
    // =========================
    [Header("⑦ 연출 | 패링 성공 히트스톱(초, 0=없음)")]
    public float hitstopOnParry = 0.06f;

    [Header("⑦ 연출 | 패링 성공 카메라 셰이크 강도(0=없음)")]
    public float camShakeOnParry = 0.6f;

    [Header("⑦ 연출 | 가드브레이크 카메라 셰이크 강도(0=없음)")]
    public float camShakeOnGuardBreak = 0.9f;

    [Header("⑦ 연출 이벤트 | 히트스톱 요청(float=초) 리스너 없으면 내부 처리")]
    public FloatEvent OnRequestHitstop;

    [Header("⑦ 연출 이벤트 | 카메라셰이크 요청(float=강도)")]
    public FloatEvent OnRequestCamShake;

    // =========================
    // ⑧ 튜닝 | 보스별 설정 SO
    // =========================
    [Header("⑧ 튜닝 | 보스별 가드/패링 보정 테이블(SO)")]
    public GuardParryTuningByBoss guardParryTuningByBoss;

    // =========================
    // ⑨ 디버그 | 이벤트/기즈모/오버라이드
    // =========================
    [Header("⑨ 이벤트 | 가드/패링 상태 이벤트 훅")]
    public UnityEvent OnGuardStart;
    public UnityEvent OnGuardEnd;
    public UnityEvent OnGuardBlock;
    public UnityEvent OnGuardBreak;
    public UnityEvent OnParryStart;
    public UnityEvent OnParrySuccess;
    public UnityEvent OnParryFail;

    [Header("⑨ 디버그 | 각도/패링창 기즈모 표시")]
    public bool debugDraw = true;

    [Header("⑨ 디버그 | 전역 패링 가능 오버라이드(true=패링 허용, false=전부 불가)")]
    public bool parryabilityOverride = true;

    // ===== 내부 상태 =====
    float _stamina;
    bool  _guardHolding;
    bool  _isGuarding;
    float _parryTimer; // <0 비활성, [0,SU) 스타트업, [SU,SU+AC) 액티브, 그 후 리커버리
    bool  _wasAttackingPrev;
    bool  _parriedThisWindow;

    // 원본값 보관(보스 설정 적용/해제용)
    float _baseParryStartup, _baseParryActive, _baseParryRecovery;
    float _baseGuardDamageMultiplier, _baseStaminaCostBase, _baseStaminaPerDamage;

    void Reset() { _stamina = staminaMax; }
    void OnValidate()
    {
        guardDamageMultiplier = Mathf.Clamp01(guardDamageMultiplier);
        projectileGuardDamageMultiplier = Mathf.Clamp01(projectileGuardDamageMultiplier);
        parryStartup = Mathf.Max(0, parryStartup);
        parryActive = Mathf.Max(0, parryActive);
        parryRecovery = Mathf.Max(0, parryRecovery);
    }

    void Awake()
    {
        if (playerRoot == null) playerRoot = transform;
        _stateReader = combatStateReaderBehaviour as ICombatStateReader;
        _inputBlocker = inputBlockerBehaviour as IInputBlocker;
        _invul = invulnerabilityBehaviour as IInvulnerabilityToggle;

        _stamina = Mathf.Clamp(staminaMax, 0, staminaMax);
        _parryTimer = -1f;
        CacheBaseTuning();
    }

    void CacheBaseTuning()
    {
        _baseParryStartup = parryStartup;
        _baseParryActive = parryActive;
        _baseParryRecovery = parryRecovery;
        _baseGuardDamageMultiplier = guardDamageMultiplier;
        _baseStaminaCostBase = guardStaminaCostBase;
        _baseStaminaPerDamage = guardStaminaPerDamage;
    }

    void Update()
    {
        bool isAttacking = _stateReader != null && _stateReader.IsAttacking();

        // 레거시 입력
        if (useLegacyInput && (_inputBlocker == null || !_inputBlocker.IsBlocked))
        {
            if (Input.GetKeyDown(guardKey)) StartGuard();
            if (Input.GetKeyUp(guardKey))   EndGuard();
            if (Input.GetKeyDown(parryKey)) RequestParry();
        }

        // 공격 중 정책
        if (disallowGuardDuringAttack && isAttacking)
        {
            if (endGuardImmediatelyOnAttackStart && _isGuarding) ForceEndGuard();
            if (enforceLayerSuppressDuringAttack && animator != null && suppressLayerNamesDuringAttack != null)
                foreach (var name in suppressLayerNamesDuringAttack)
                {
                    int idx = animator.GetLayerIndex(name);
                    if (idx >= 0) animator.SetLayerWeight(idx, 0f);
                }
        }

        // 공격 종료 → 자동 재가드
        if (_wasAttackingPrev && !isAttacking)
            if (autoResumeGuardIfHolding && _guardHolding && !_isGuarding) StartGuard();
        _wasAttackingPrev = isAttacking;

        // 스태미나 회복
        if (!_isGuarding && _parryTimer < 0f)
            _stamina = Mathf.Min(staminaMax, _stamina + staminaRegenPerSec * Time.deltaTime);

        // 패링 타이머
        if (_parryTimer >= 0f)
        {
            _parryTimer += Time.deltaTime;
            float endAll = parryStartup + parryActive + parryRecovery;
            if (_parryTimer >= endAll)
            {
                _parryTimer = -1f;
                _parriedThisWindow = false;
                if (_inputBlocker != null) _inputBlocker.BlockAll(false);
            }
        }
    }

    // ===== 외부 제어 =====
    public void StartGuard()
    {
        if (_inputBlocker != null && _inputBlocker.IsBlocked) return;
        if (_stateReader != null && _stateReader.IsAttacking() && disallowGuardDuringAttack) return;

        _guardHolding = true;
        if (_isGuarding) return;
        _isGuarding = true;
        OnGuardStart?.Invoke();
    }

    public void EndGuard()
    {
        _guardHolding = false;
        if (!_isGuarding) return;
        _isGuarding = false;
        OnGuardEnd?.Invoke();
    }

    public void ForceEndGuard()
    {
        _guardHolding = false;
        if (_isGuarding)
        {
            _isGuarding = false;
            OnGuardEnd?.Invoke();
        }
    }

    public void RequestParry()
    {
        if (_inputBlocker != null && _inputBlocker.IsBlocked) return;
        _parryTimer = 0f;
        _parriedThisWindow = false;
        OnParryStart?.Invoke();
    }

    // 애니메이션 이벤트 호환(구 레거시 호출 지원)
    public void OpenParryWindow() => RequestParry();
    public void OpenParryWindowWithTimes(float startup, float active, float recovery)
        => StartCoroutine(Co_OpenParryWindowOnce(startup, active, recovery));
    IEnumerator Co_OpenParryWindowOnce(float startup, float active, float recovery)
    {
        float oS = parryStartup, oA = parryActive, oR = parryRecovery;
        parryStartup = startup; parryActive = active; parryRecovery = recovery;
        RequestParry();
        yield return new WaitForSeconds(startup + active + recovery);
        parryStartup = oS; parryActive = oA; parryRecovery = oR;
    }

    // ===== 히트 해결(핵심) =====
    // 기존 PlayerDamageReceiver 호환
    public float ResolveIncomingAttack(Vector3 attackerToPlayerDir, bool isParryable, float rawDamage)
        => ResolveIncomingAttack(attackerToPlayerDir, isParryable, rawDamage, null);

    // 확장: 공격자 GO와 히트포인트 전달 가능, 투사체/광역 식별에 활용
    public float ResolveIncomingAttack(Vector3 attackerToPlayerDir, bool isParryable, float rawDamage, GameObject attacker, Vector3? hitPoint = null)
    {
        // 공격 중 가드 금지 정책
        if (_stateReader != null && _stateReader.IsAttacking() && disallowGuardDuringAttack)
        {
            ForceEndGuard();
            return rawDamage;
        }

        // AOE 판정 시 각도 우회
        bool isAoE = IsAoE(attacker);
        bool withinAngle = isAoE && treatAoeAsAngleBypass
            ? true
            : IsWithinGuardAngle(attackerToPlayerDir);

        // 패링 활성
        bool parryActiveNow = IsParryActive();
        bool parryAllowed = parryabilityOverride && isParryable && (!parryRequiresGuardAngle || withinAngle);

        // 패링 성공(한 창당 1회만)
        if (parryActiveNow && parryAllowed && !_parriedThisWindow)
        {
            _parriedThisWindow = true;
            OnParrySuccess?.Invoke();

            if (_invul != null && parryIFrame > 0f) StartCoroutine(Co_IFrame(parryIFrame));
            if (hitstopOnParry > 0f) DoHitstop(hitstopOnParry);
            if (camShakeOnParry > 0f) OnRequestCamShake?.Invoke(camShakeOnParry);

            // 공격자 반응 + 포이즈 누적
            NotifyAttackerParried(attacker, parryStunToAttacker);
            AddPoiseToAttacker(attacker, poiseOnParrySuccess);

            // 투사체 반사
            if (reflectOnParry && allowProjectileDeflect) TryReflectProjectile(attacker);

            return 0f;
        }

        // 패링 실패 리커버리 구간 → 풀딜
        if (_parryTimer >= parryStartup + parryActive && _parryTimer < parryStartup + parryActive + parryRecovery)
        {
            OnParryFail?.Invoke();
            return rawDamage;
        }

        // 가드 처리
        if (_isGuarding && withinAngle)
        {
            float final = Mathf.Max(guardChipMin, rawDamage * guardDamageMultiplier);

            // 투사체 가드 감쇠
            if (allowProjectileDeflect && IsProjectile(attacker))
            {
                final = Mathf.Max(guardChipMin, rawDamage * projectileGuardDamageMultiplier);
                TryDeflectProjectile(attacker);
                AddPoiseToAttacker(attacker, poiseOnGuard);
            }

            // 스태미나
            float cost = guardStaminaCostBase + rawDamage * guardStaminaPerDamage;
            _stamina -= cost;

            OnGuardBlock?.Invoke();

            // 브레이크
            if (_stamina <= guardBreakThreshold)
            {
                OnGuardBreak?.Invoke();
                ForceEndGuard();
                if (camShakeOnGuardBreak > 0f) OnRequestCamShake?.Invoke(camShakeOnGuardBreak);
                StartCoroutine(Co_Stun(guardBreakStun));
                return rawDamage;
            }
            return final;
        }

        // 미가드/각도 밖
        return rawDamage;
    }

    // ===== 유틸 =====
    bool IsWithinGuardAngle(Vector3 attackerToPlayerDir)
    {
        Transform t = playerRoot != null ? playerRoot : transform;
        Vector3 fwd = t.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 ap  = attackerToPlayerDir; ap.y = 0f; ap.Normalize();
        float dot = Vector3.Dot(fwd, -ap);
        float deg = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        return deg <= guardConeAngle * 0.5f;
    }

    bool IsParryActive()
    {
        if (_parryTimer < 0f) return false;
        if (_parryTimer < parryStartup) return false;
        if (_parryTimer < parryStartup + parryActive) return true;
        return false;
    }

    bool IsProjectile(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("Projectile")) return true;
        // 이름/컴포넌트에 Projectile 포함 시 포착(유연성)
        if (go.name.IndexOf("Projectile", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (go.GetComponent<Rigidbody>() != null && go.GetComponents<MonoBehaviour>().Any(c => c && c.GetType().Name.Contains("Projectile"))) return true;
        return false;
    }

    bool IsAoE(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("AOE")) return true;
        var comp = go.GetComponents<MonoBehaviour>().FirstOrDefault(c => c && c.GetType().Name.Contains("AOE") || c.GetType().Name.Contains("Area"));
        return comp != null;
    }

    void TryDeflectProjectile(GameObject proj)
    {
        if (proj == null) return;
        proj.SendMessage("OnDeflected", this.gameObject, SendMessageOptions.DontRequireReceiver);
        // 단순 튕김: 진행방향 약간 틀기(소유권 변경은 반사에서 처리)
        var rb = proj.GetComponent<Rigidbody>();
        if (rb != null && rb.velocity.sqrMagnitude > 0.01f)
        {
            Vector3 v = rb.velocity;
            Vector3 n = (proj.transform.position - transform.position).normalized;
            Vector3 r = Vector3.Reflect(v, n).normalized;
            rb.velocity = r * v.magnitude; // 속도 유지
        }
    }

    void TryReflectProjectile(GameObject proj)
    {
        if (proj == null) return;
        var rb = proj.GetComponent<Rigidbody>();
        if (rb != null && rb.velocity.sqrMagnitude > 0.01f)
        {
            Vector3 dirToEnemy = (proj.transform.position - transform.position).normalized;
            rb.velocity = dirToEnemy * rb.velocity.magnitude * Mathf.Max(0.01f, reflectSpeedMultiplier);
        }
        // 소유권/레이어 전환 알림
        proj.SendMessage("SetOwner", this.gameObject, SendMessageOptions.DontRequireReceiver);
        proj.SendMessage("OnReflected", this.gameObject, SendMessageOptions.DontRequireReceiver);
        AddPoiseToAttacker(proj, poiseOnParrySuccess); // 투사체 발사체에도 훅
    }

    void NotifyAttackerParried(GameObject attacker, float stunSec)
    {
        if (attacker == null) return;
        attacker.SendMessage("OnParried", SendMessageOptions.DontRequireReceiver);
        foreach (var c in attacker.GetComponents<MonoBehaviour>())
        {
            var mi = c.GetType().GetMethod("ApplyParryStun", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (mi != null) { mi.Invoke(c, new object[] { stunSec }); break; }
        }
    }

    void AddPoiseToAttacker(GameObject attacker, float amount)
    {
        if (attacker == null || amount <= 0f) return;
        foreach (var c in attacker.GetComponents<MonoBehaviour>())
        {
            var mi = c.GetType().GetMethod("AddPoiseDamage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (mi != null) { mi.Invoke(c, new object[] { amount }); break; }
        }
    }

    void DoHitstop(float seconds)
    {
        if (OnRequestHitstop != null && OnRequestHitstop.GetPersistentEventCount() > 0)
        {
            OnRequestHitstop.Invoke(seconds);
        }
        else
        {
            // 내부 간이 히트스톱
            StartCoroutine(Co_Hitstop(seconds));
        }
    }

    IEnumerator Co_IFrame(float t)
    {
        _invul?.SetInvulnerable(true);
        yield return new WaitForSeconds(t);
        _invul?.SetInvulnerable(false);
    }

    IEnumerator Co_Stun(float t)
    {
        if (_inputBlocker != null) _inputBlocker.BlockAll(true);
        yield return new WaitForSeconds(t);
        if (_inputBlocker != null) _inputBlocker.BlockAll(false);
    }

    IEnumerator Co_Hitstop(float t)
    {
        float prev = Time.timeScale;
        Time.timeScale = 0f;
        // Unscaled 시간으로 대기
        float end = Time.unscaledTime + t;
        while (Time.unscaledTime < end) yield return null;
        Time.timeScale = prev;
    }

    void OnDrawGizmosSelected()
    {
        if (!debugDraw) return;
        Transform t = playerRoot != null ? playerRoot : transform;
        Vector3 pos = t.position + Vector3.up * 1.0f;
        float half = guardConeAngle * 0.5f;
        Vector3 fwd = t.forward;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(pos, Quaternion.AngleAxis(-half, Vector3.up) * fwd * 2f);
        Gizmos.DrawRay(pos, Quaternion.AngleAxis(half, Vector3.up) * fwd * 2f);
        Gizmos.DrawRay(pos, fwd * 2f);
        if (IsParryActive()) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(pos, 0.2f); }
    }

    // ===== 공개 API =====
    [ContextMenu("Revert Boss Tuning")]
    public void RevertBossTuning()
    {
        parryStartup = _baseParryStartup;
        parryActive  = _baseParryActive;
        parryRecovery= _baseParryRecovery;
        guardDamageMultiplier = _baseGuardDamageMultiplier;
        guardStaminaCostBase  = _baseStaminaCostBase;
        guardStaminaPerDamage = _baseStaminaPerDamage;
    }

    public void ConfigureForBoss(string bossId)
    {
        if (guardParryTuningByBoss == null) return;
        RevertBossTuning();
        var e = guardParryTuningByBoss.Find(bossId);
        if (e == null) return;

        parryStartup += e.parryStartupAdd;
        parryActive  += e.parryActiveAdd;
        parryRecovery+= e.parryRecoveryAdd;

        guardDamageMultiplier *= Mathf.Max(0.01f, e.guardDamageMultiplierScale);
        guardStaminaCostBase  *= Mathf.Max(0.01f, e.staminaCostMultiplier);
        guardStaminaPerDamage *= Mathf.Max(0.01f, e.staminaPerDamageMultiplier);
    }

    public void SetParryabilityOverride(bool enabled) => parryabilityOverride = enabled;
}

// ===============================================================
// 보스별 튜닝 테이블 ScriptableObject (같은 파일에 포함해도 무방)
// ===============================================================
[CreateAssetMenu(menuName = "Combat/GuardParryTuningByBoss")]
public class GuardParryTuningByBoss : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Header("보스 식별자(문자열)")]
        public string bossId = "Boss_01";
        [Header("패링 타이밍 보정(+가산, -감산)")]
        public float parryStartupAdd = 0f;
        public float parryActiveAdd = 0f;
        public float parryRecoveryAdd = 0f;
        [Header("가드 피해 비율 스케일(곱)")]
        public float guardDamageMultiplierScale = 1f;
        [Header("스태미나 코스트 스케일(곱)")]
        public float staminaCostMultiplier = 1f;
        public float staminaPerDamageMultiplier = 1f;
    }

    [Header("보스별 항목 리스트")]
    public Entry[] entries;

    public Entry Find(string id)
    {
        if (string.IsNullOrEmpty(id) || entries == null) return null;
        return entries.FirstOrDefault(e => e != null && e.bossId == id);
    }
}
