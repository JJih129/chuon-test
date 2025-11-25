// 파일명: BossController.cs
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;
using UnityEngine.Events;

// ==============================================================================
// 1. FSM 상태 정의
// ==============================================================================
public enum BossState
{
    IntroIdle, Detect, Move, Attack, CombatIdle, Break, Dead
}

// ==============================================================================
// 2. 공격 패턴 상세 데이터 구조 정의
// ==============================================================================
[System.Serializable]
public class AttackPattern
{
    public string patternName;
    public string animTriggerName;
    public bool isParryable;
    public int damageAmount = 10;

    [Header("발동 조건")]
    public float cooldown;
    [HideInInspector] public float currentCooldown;
    public float weight;
    public float minRange;
    public float maxRange;
    public string forbiddenAfter;

    public bool CanExecute(BossController boss, float distance, string lastPattern)
    {
        if (currentCooldown > 0f) return false;
        if (distance < minRange || distance > maxRange) return false;
        if (lastPattern.Equals(forbiddenAfter)) return false;

        if (boss.playerTracker == null) return true;

        IPlayerTracker tracker = boss.playerTracker;

        switch (patternName)
        {
            case "연속 베기":
                if (distance >= 1.5f && distance <= 3f)
                {
                    return tracker.IsAttackStoppedRecently(1.0f);
                }
                return false;
            case "엇박 베기":
                return distance < 1f && tracker.IsDodgingRecently();
            case "사선 베기":
                return distance >= 1f && distance <= 2.5f && tracker.IsDodgingRecently();
            case "대쉬 찌르기":
                return distance >= 2f && tracker.IsRetreating();
        }

        return true;
    }
}

// ==============================================================================
// 3. BossController 메인 로직
// ==============================================================================

public class BossController : MonoBehaviour
{
    [Header("▶ 핵심 컴포넌트 통합")]
    public BossHealth bossHealth;
    public BossBreakController breakController;
    public Animator bossAnimator;
    public Transform playerTarget;
    public PatternVisuals patternVisuals;
    public Collider attackHitbox;
    public PlayerTracker playerTracker;

    [Header("▶ FSM 설정")]
    // ★★★ 초기 상태를 Dead로 설정하여 첫 SetState 호출을 보장합니다. ★★★
    public BossState currentState = BossState.Dead;
    public float combatIdleTime = 1.5f;

    [Header("▶ 이동 설정")]
    public float moveSpeed = 4.0f;
    public float stoppingDistance = 1.0f; // 공격 진입 시 멈출 최소 거리 (1m)

    [Header("▶ 공격 패턴 데이터")]
    public List<AttackPattern> allPatterns;

    private string lastExecutedPattern = string.Empty;
    private Coroutine _stateRoutine;
    private AttackPattern _currentPattern;

    void Awake()
    {
        if (attackHitbox != null) attackHitbox.enabled = false;

        bossHealth.OnDied += OnBossDied;
        breakController.OnBreakEnter.AddListener(OnBreakEnter);
        breakController.OnBreakExit.AddListener(OnBreakExit);

        // 필수 컴포넌트 누락 시 스크립트 비활성화 방지 (디버깅 목적으로 제거)
    }

    void Start()
    {
        SetState(BossState.IntroIdle);
        Debug.Log("DEBUG: FSM Init - 초기 상태 설정 완료.");
    }

    void Update()
    {
        foreach (AttackPattern pattern in allPatterns)
        {
            if (pattern.currentCooldown > 0f)
            {
                pattern.currentCooldown -= Time.deltaTime;
            }
        }
    }

    // === 상태/이벤트 관리 ===

    public void SetState(BossState newState)
    {
        if (currentState == newState) return;
        if (currentState == BossState.Dead) return;

        if (_stateRoutine != null) StopCoroutine(_stateRoutine);

        // ★★★ 상태 변경 전 로그 (Old State 확인) ★★★
        Debug.Log($"[STATE DEBUG] OLD: {currentState} | NEW: {newState}");
        Debug.Log($"FSM Transition: {currentState} -> {newState}");

        // 실제로 상태 변수를 업데이트하는 코드
        currentState = newState;

        // ★★★ 상태 변경 후 로그 (New State 확인) ★★★
        Debug.Log($"[STATE DEBUG] State is now: {currentState}");

        switch (currentState)
        {
            case BossState.IntroIdle: _stateRoutine = StartCoroutine(Co_HandleIntroIdle()); break;
            case BossState.Detect: _stateRoutine = StartCoroutine(Co_HandleDetect()); break;
            case BossState.Move: _stateRoutine = StartCoroutine(Co_HandleMove()); break;
            case BossState.Attack: _stateRoutine = StartCoroutine(Co_PerformAttack()); break;
            case BossState.CombatIdle: _stateRoutine = StartCoroutine(Co_CombatIdle()); break;
            case BossState.Dead: break;
        }
    }

    private void OnBossDied() => SetState(BossState.Dead);
    private void OnBreakEnter()
    {
        SetState(BossState.Break);
        bossAnimator.SetBool("IsBreak", true);
    }
    private void OnBreakExit()
    {
        bossAnimator.SetBool("IsBreak", false);
        SetState(BossState.Detect);
    }

    // === 애니메이션 이벤트 수신 메소드 ===

    public void OnAnimationPatternEnd()
    {
        if (currentState == BossState.Attack)
        {
            _currentPattern = null;
            SetState(BossState.CombatIdle);
        }
    }

    public void ActivateHitbox()
    {
        if (_currentPattern != null && attackHitbox != null)
        {
            attackHitbox.enabled = true;
        }
    }

    public void DeactivateHitbox()
    {
        if (attackHitbox != null) attackHitbox.enabled = false;
    }

    // === FSM 코루틴 구현 (Detect 로직 개선) ===

    private IEnumerator Co_HandleIntroIdle()
    {
        Debug.Log("DEBUG: Co_HandleIntroIdle - 코루틴 시작.");
        yield return new WaitForSeconds(3.0f);
        Debug.Log("DEBUG: Co_HandleIntroIdle - 3초 대기 완료. Detect로 전환 시도.");
        SetState(BossState.Detect);
    }

    private IEnumerator Co_HandleDetect()
    {
        Debug.Log("DEBUG: Detect State - 진입. 플레이어 접근 대기 시작.");

        // 3m 초과 시 즉시 Move로 전환 (이동 시작)
        float distance = Vector3.Distance(transform.position, playerTarget.position);
        if (distance > 3f)
        {
            Debug.Log($"DEBUG: Detect State - 3m 이외({distance:F2}m). Move로 전환.");
            SetState(BossState.Move);
            yield break;
        }

        // 3m 이내라면 공격 준비 (Attack으로 전환)
        Debug.Log($"DEBUG: Detect State - 3m 이내({distance:F2}m). Attack으로 전환 시도.");
        SetState(BossState.Attack);
        yield break;
    }

    private IEnumerator Co_CombatIdle()
    {
        yield return new WaitForSeconds(combatIdleTime);
        SetState(BossState.Detect);
    }

    // [최종 수정] Move 상태: 정지 거리(stoppingDistance)를 사용한 추적 및 회전 로직
    private IEnumerator Co_HandleMove()
    {
        Debug.Log("DEBUG: Move State - 플레이어 추적 시작.");

        while (true)
        {
            float distance = Vector3.Distance(transform.position, playerTarget.position);

            // 1. 공격 범위 진입 조건: 3m + stoppingDistance보다 가까우면 멈추고 공격
            if (distance <= 3f + stoppingDistance)
            {
                Debug.Log("DEBUG: Move State - 공격 범위 진입. Attack으로 전환.");
                bossAnimator.SetFloat("Speed", 0f);
                SetState(BossState.Attack);
                yield break;
            }

            // 2. 방향 설정
            Vector3 direction = (playerTarget.position - transform.position).normalized;
            direction.y = 0f;

            // 3. 회전 (부드러운 Slerp 사용)
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);

            // 4. 이동
            transform.position += direction * moveSpeed * Time.deltaTime;

            // 5. 애니메이션 업데이트 
            bossAnimator.SetFloat("Speed", moveSpeed); // (방향 크기 대신 속도 값 직접 사용)

            yield return null;
        }
    }

    private IEnumerator Co_PerformAttack()
    {
        // ... (패턴 선택 로직) ...

        List<AttackPattern> candidates = new List<AttackPattern>();

        // 패턴 선택 로직 (패턴 데이터가 없거나 선택 실패 시 강제 CombatIdle)
        AttackPattern selectedPattern = (allPatterns.Count > 0) ? allPatterns[0] : null;

        if (selectedPattern != null)
        {
            // 패턴 실행 및 연출 호출 (생략)

            Debug.Log($"DEBUG: Attack State - 패턴 실행: {selectedPattern.patternName}. 애니메이션 이벤트 대기.");

            yield return new WaitUntil(() => currentState != BossState.Attack);
        }
        else
        {
            Debug.LogWarning("DEBUG: Attack State - 실행 가능한 패턴 없음. CombatIdle로 복귀.");
            SetState(BossState.CombatIdle);
        }
    }

    // === 충돌 처리 (Hitbox) ===
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && _currentPattern != null)
        {
            // 데미지 적용 로직
        }
    }
}