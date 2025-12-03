// 파일명: BossController.cs
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;
using UnityEngine.Events;

/// <summary>
/// 보스 FSM 상태 정의
/// </summary>
public enum BossState
{
    IntroIdle,   // 전투 시작 전 연출용 대기
    Detect,      // 플레이어 거리/상태 체크
    Move,        // 플레이어에게 이동
    Attack,      // 패턴 수행 중
    CombatIdle,  // 공격 후 잠깐 휴지
    Break,       // 브레이크(그로기)
    Dead         // 사망
}

[System.Serializable]
public class AttackPattern
{
    [Header("기본 정보")]
    public string patternName;
    public string animTriggerName; // 애니메이터 스테이트/트리거 이름 (Attack_A ~ Attack_E)
    public bool isParryable;
    public int damageAmount = 10;

    [Header("쿨타임/가중치/거리")]
    public float cooldown = 2f;
    [HideInInspector] public float currentCooldown;
    public float weight = 1f;
    public float minRange = 0f;
    public float maxRange = 5f;

    [Tooltip("이 패턴 직전에 금지할 패턴 이름 (없으면 빈 문자열)")]
    public string forbiddenAfter;

    public bool CanExecute(BossController boss, float distance, string lastPattern)
    {
        if (currentCooldown > 0f) return false;
        if (distance < minRange || distance > maxRange) return false;

        if (!string.IsNullOrEmpty(forbiddenAfter) &&
            lastPattern.Equals(forbiddenAfter, StringComparison.Ordinal))
            return false;

        if (boss.playerTracker == null) return true;

        // 필요하면 이름 기준으로 세부 조건 추가
        // IPlayerTracker tracker = boss.playerTracker;
        return true;
    }
}

/// <summary>
/// 보스 메인 컨트롤러 (FSM + 이동 + 패턴 재생)
/// </summary>
public class BossController : MonoBehaviour
{
    [Header("참조 컴포넌트")]
    public BossHealth bossHealth;
    public BossBreakController breakController;
    public Animator bossAnimator;
    public Transform playerTarget;
    public PatternVisuals patternVisuals;

    [Header("물리 이동")]
    [SerializeField] Rigidbody rb;
    
    [Header("공격 히트박스 (공통 컴포넌트)")]
    [Tooltip("보스 무기/팔 등에 붙은 AttackHitbox")]
    public AttackHitbox attackHitbox;

    [Tooltip("플레이어 행동 기반 패턴 제어용 트래커(선택)")]
    public PlayerTracker playerTracker;

    [Header("FSM 설정")]
    public BossState currentState = BossState.IntroIdle;
    public float combatIdleTime = 1.5f;
    public float introIdleDuration = 3.0f;

    Coroutine _stateRoutine;
    bool _isDead;

    [Header("이동 설정")]
    [Tooltip("실제 보스 이동 속도 (m/s)")]
    public float moveSpeed = 4.0f;

    [Tooltip("이 거리 이내로 들어오면 이동을 멈추고 공격 준비")]
    public float stoppingDistance = 1.0f;

    [Tooltip("Blend Tree로 전달할 MoveSpeed 보간 시간 (0에 가까울수록 즉각 반응)")]
    [Range(0.01f, 0.5f)]
    public float moveAnimDamp = 0.1f;

    static readonly int AnimParam_MoveSpeed = Animator.StringToHash("MoveSpeed");
    static readonly int AnimParam_IsBreak   = Animator.StringToHash("IsBreak");
    static readonly int AnimParam_IsDead    = Animator.StringToHash("IsDead");

    float _moveBlend; // 0~1

    [Header("공격 패턴 목록")]
    public List<AttackPattern> allPatterns;

    string _lastExecutedPattern = string.Empty;
    AttackPattern _currentPattern;

    void Awake()
    {
        // 히트박스는 기본적으로 비활성화 상태에서 시작
        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();

        if (bossHealth != null)
            bossHealth.OnDied += OnBossDied;

        if (breakController != null)
        {
            breakController.OnBreakEnter.AddListener(OnBreakEnter);
            breakController.OnBreakExit.AddListener(OnBreakExit);
        }
    }

    void Start()
    {
        SetState(BossState.IntroIdle);
    }

    void Update()
    {
        // 패턴 쿨타임 감소
        if (allPatterns != null)
        {
            float dt = Time.deltaTime;
            foreach (var p in allPatterns)
            {
                if (p.currentCooldown > 0f)
                    p.currentCooldown -= dt;
            }
        }
    }

    // ---------------- FSM 전이 ----------------

    public void SetState(BossState newState)
    {
        if (_isDead) return;
        if (currentState == newState) return;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        var old = currentState;
        currentState = newState;
        Debug.Log($"[BossFSM] {old} → {newState}");

        switch (currentState)
        {
            case BossState.IntroIdle:
                _stateRoutine = StartCoroutine(Co_HandleIntroIdle());
                break;
            case BossState.Detect:
                _stateRoutine = StartCoroutine(Co_HandleDetect());
                break;
            case BossState.Move:
                _stateRoutine = StartCoroutine(Co_HandleMove());
                break;
            case BossState.Attack:
                _stateRoutine = StartCoroutine(Co_PerformAttack());
                break;
            case BossState.CombatIdle:
                _stateRoutine = StartCoroutine(Co_CombatIdle());
                break;
            case BossState.Break:
                _stateRoutine = StartCoroutine(Co_HandleBreak());
                break;
            case BossState.Dead:
                break;
        }
    }

    // ---------------- 생존/브레이크 ----------------

    void OnBossDied()
    {
        if (_isDead) return;
        _isDead = true;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        currentState = BossState.Dead;
        UpdateMoveAnimation(0f);

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsDead, true);

        Debug.Log("[BossFSM] Boss Dead");
    }

    void OnBreakEnter()
    {
        if (_isDead) return;

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsBreak, true);

        SetState(BossState.Break);
    }

    void OnBreakExit()
    {
        if (_isDead) return;

        if (bossAnimator != null)
            bossAnimator.SetBool(AnimParam_IsBreak, false);

        SetState(BossState.Detect);
    }

    // ---------------- 애니메이션 이벤트용 ----------------

    /// <summary>
    /// 공격 클립 끝에서 AnimationEvent로 호출 (또는 BossAnimationEvents에서 호출)
    /// </summary>
    public void OnAnimationPatternEnd()
    {
        if (currentState != BossState.Attack)
            return;

        if (_stateRoutine != null)
        {
            StopCoroutine(_stateRoutine);
            _stateRoutine = null;
        }

        _currentPattern = null;
        SetState(BossState.CombatIdle);
    }

    /// <summary>
    /// 과거 방식(콜라이더 On/Off)을 썼던 이벤트를 위해 남겨둔 래퍼.
    /// 지금은 AttackHitbox의 Window를 열고 닫는 방식으로 동작.
    /// </summary>
    public void ActivateHitbox()
    {
        if (_currentPattern != null && attackHitbox != null)
            attackHitbox.ActivateWindow();
    }

    public void DeactivateHitbox()
    {
        if (attackHitbox != null)
            attackHitbox.DeactivateWindow();
    }

    // ---------------- 상태별 코루틴 ----------------

    IEnumerator Co_HandleIntroIdle()
    {
        float t = introIdleDuration;
        while (t > 0f && !_isDead)
        {
            t -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        if (!_isDead)
            SetState(BossState.Detect);
    }

    IEnumerator Co_HandleDetect()
    {
        yield return null;

        if (playerTarget == null)
        {
            UpdateMoveAnimation(0f);
            yield break;
        }

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        UpdateMoveAnimation(0f);

        if (distance > stoppingDistance + 1.0f)
            SetState(BossState.Move);
        else
            SetState(BossState.Attack);
    }

    IEnumerator Co_HandleMove()
    {
        Debug.Log("[BossFSM] Move: 플레이어에게 접근 시작");

        while (currentState == BossState.Move && !_isDead)
        {
            if (playerTarget == null)
            {
                UpdateMoveAnimation(0f);
                yield break;
            }

            Vector3 toPlayer = playerTarget.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (distance <= stoppingDistance)
            {
                UpdateMoveAnimation(0f);
                SetState(BossState.Attack);
                yield break;
            }

            Vector3 dir = toPlayer.normalized;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, look, Time.deltaTime * 5f);
            }

            transform.position += dir * moveSpeed * Time.deltaTime;
            UpdateMoveAnimation(1f);

            yield return null;
        }
    }

    IEnumerator Co_CombatIdle()
    {
        float t = combatIdleTime;
        while (t > 0f && !_isDead)
        {
            t -= Time.deltaTime;
            UpdateMoveAnimation(0f);
            yield return null;
        }

        if (!_isDead)
            SetState(BossState.Detect);
    }

    IEnumerator Co_HandleBreak()
    {
        Debug.Log("[BossFSM] Break: 브레이크 상태 진입");
        while (currentState == BossState.Break && !_isDead)
        {
            UpdateMoveAnimation(0f);
            yield return null;
        }
    }

    IEnumerator Co_PerformAttack()
    {
        if (playerTarget == null || allPatterns == null || allPatterns.Count == 0)
        {
            SetState(BossState.CombatIdle);
            yield break;
        }

        UpdateMoveAnimation(0f);

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        AttackPattern pattern = SelectPattern(distance);

        if (pattern == null)
        {
            Debug.LogWarning("[BossFSM] 사용할 수 있는 패턴이 없음 → CombatIdle");
            SetState(BossState.CombatIdle);
            yield break;
        }

        _currentPattern        = pattern;
        _lastExecutedPattern   = pattern.patternName;
        pattern.currentCooldown = pattern.cooldown;

        // ① 패턴 비주얼 (가드 가능/불가 색상)
        if (patternVisuals != null)
            patternVisuals.SetParryable(pattern.isParryable);

        // ② 히트박스 데미지/퍼펙트 회피 여부 세팅
        if (attackHitbox != null)
        {
            // HitType은 AttackHitbox 인스펙터 기본값 사용, 데미지만 패턴 값으로 덮어씀
            attackHitbox.Configure(pattern.damageAmount, pattern.isParryable, transform);
        }

        // ③ 애니메이션 트리거
        if (bossAnimator != null && !string.IsNullOrEmpty(pattern.animTriggerName))
        {
            bossAnimator.ResetTrigger(pattern.animTriggerName);
            bossAnimator.SetTrigger(pattern.animTriggerName);
        }

        Debug.Log($"[BossFSM] Attack 패턴 실행: {pattern.patternName} ({pattern.animTriggerName})");

        // ─────────────────────────────
        // 애니메이션 종료까지 기다리는 안전장치
        // (Attack_A ~ E 스테이트 이름을 animTriggerName과 동일하게 맞춰두는 전제)
        // ─────────────────────────────
        const float maxWait = 10f;
        float elapsed = 0f;

        bool enteredState = false;

        // 1) 해당 Attack 스테이트로 실제로 진입할 때까지 대기
        while (elapsed < maxWait && currentState == BossState.Attack && !_isDead)
        {
            if (bossAnimator == null) break;

            var info = bossAnimator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(pattern.animTriggerName))
            {
                enteredState = true;
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 2) 스테이트에 들어갔다면, 한 번 재생 끝날 때까지 (normalizedTime >= 0.99)
        elapsed = 0f;
        if (enteredState)
        {
            while (elapsed < maxWait && currentState == BossState.Attack && !_isDead)
            {
                if (bossAnimator == null) break;

                var info = bossAnimator.GetCurrentAnimatorStateInfo(0);
                if (!info.IsName(pattern.animTriggerName) || info.normalizedTime >= 0.99f)
                    break;

                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // AnimationEvent가 먼저 상태를 바꿨다면 여기서 이미 Attack이 아닐 수 있음
        if (currentState == BossState.Attack && !_isDead)
        {
            _currentPattern = null;
            SetState(BossState.CombatIdle);
        }
    }

    // ---------------- 패턴 선택 ----------------

    AttackPattern SelectPattern(float distanceToPlayer)
    {
        if (allPatterns == null || allPatterns.Count == 0)
            return null;

        List<AttackPattern> candidates = new List<AttackPattern>();

        foreach (var p in allPatterns)
        {
            if (p == null) continue;
            if (p.CanExecute(this, distanceToPlayer, _lastExecutedPattern))
                candidates.Add(p);
        }

        if (candidates.Count == 0)
            return null;

        float totalWeight = 0f;
        foreach (var p in candidates)
            totalWeight += Mathf.Max(0f, p.weight);

        if (totalWeight <= 0f)
            return candidates[0];

        float r = UnityEngine.Random.value * totalWeight;
        float accum = 0f;

        foreach (var p in candidates)
        {
            float w = Mathf.Max(0f, p.weight);
            accum += w;
            if (r <= accum)
                return p;
        }

        return candidates[candidates.Count - 1];
    }

    // ---------------- 이동 애니 (BlendTree) ----------------

    void UpdateMoveAnimation(float target01)
    {
        if (bossAnimator == null) return;

        target01 = Mathf.Clamp01(target01);

        _moveBlend = Mathf.Lerp(
            _moveBlend,
            target01,
            Time.deltaTime / Mathf.Max(0.0001f, moveAnimDamp)
        );

        bossAnimator.SetFloat(AnimParam_MoveSpeed, _moveBlend);
    }

    // ---------------- (선택) 루트 보스 콜라이더 충돌 ----------------
    // 실제 데미지 처리는 AttackHitbox가 담당하므로, 이 메서드는 비워둬도 무방.
    void OnTriggerEnter(Collider other)
    {
        if (_currentPattern == null) return;

        // 필요하면 여기서도 특수 처리 가능.
    }
}
