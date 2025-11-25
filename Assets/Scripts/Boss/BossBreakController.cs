// Assets/Scripts/Boss/BossBreakController.cs
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class BossBreakController : MonoBehaviour
{
    [Header("01. 브레이크 게이지 설정")]
    [Tooltip("브레이크 최대치(게이지 1.0에 해당)")]
    [Min(1f)] public float maxBreak = 100f;

    [Tooltip("한 번 피격당 기본으로 쌓이는 브레이크량")]
    [Min(0f)] public float baseBreakPerHit = 10f;

    [Tooltip("브레이크 상태 유지 시간(초)")]
    [Min(0f)] public float breakDuration = 5f;

    [Tooltip("브레이크 상태가 아닐 때 초당 회복량")]
    [Min(0f)] public float recoveryPerSecond = 15f;

    [Header("02. 참조")]
    [Tooltip("보스 체력/스태거 관리 스크립트")]
    [SerializeField] private BossHealth bossHealth;

    [Tooltip("보스 애니메이터 (IsBreak bool 사용)")]
    [SerializeField] private Animator bossAnimator;

    [Tooltip("브레이크 상태를 나타내는 Animator Bool 파라미터 이름")]
    [SerializeField] private string breakBoolName = "IsBreak";

    [Header("03. 이벤트 (외부 UI/연출용)")]
    [Tooltip("브레이크 상태에 진입했을 때 호출되는 이벤트")]
    public UnityEvent OnBreakEnter = new UnityEvent();

    [Tooltip("브레이크 상태에서 빠져나갈 때 호출되는 이벤트")]
    public UnityEvent OnBreakExit = new UnityEvent();

    [Header("04. 디버그")]
    [Tooltip("브레이크 관련 로그 출력 여부")]
    public bool logDebug = false;

    // 내부 상태값 ---------------------------------------------

    // 현재 브레이크 양 (0 ~ maxBreak)
    private float _currentBreak;

    // 현재 브레이크 상태 유지 타이머
    private float _breakTimer;

    // 브레이크 상태 여부
    private bool _isInBreak;

    // Animator Bool 해시
    private int _breakBoolHash;

    /// <summary>
    /// 0~1로 정규화된 브레이크 게이지 값 (UI용)
    /// </summary>
    public float NormalizedBreak => Mathf.Clamp01(maxBreak > 0f ? _currentBreak / maxBreak : 0f);

    /// <summary>
    /// UI에서 기존에 쓰던 Get01() 시그니처 유지용.
    /// </summary>
    public float Get01() => NormalizedBreak;

    /// <summary>
    /// 현재 브레이크 상태인지 여부 (보스 FSM 등에서 조회용)
    /// </summary>
    public bool IsInBreak => _isInBreak;

    // ---------------------------------------------------------

    private void Reset()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator) bossAnimator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator) bossAnimator = GetComponentInChildren<Animator>();

        if (bossHealth != null)
        {
            // BossHealth의 OnDamaged, OnDied 이벤트에 연결
            bossHealth.OnDamaged += HandleBossDamaged;
            bossHealth.OnDied += HandleBossDied;
        }

        _breakBoolHash = Animator.StringToHash(breakBoolName);
    }

    private void OnDestroy()
    {
        if (bossHealth != null)
        {
            bossHealth.OnDamaged -= HandleBossDamaged;
            bossHealth.OnDied -= HandleBossDied;
        }
    }

    private void Update()
    {
        if (_isInBreak)
        {
            // 브레이크 상태 유지 시간 감소
            _breakTimer -= Time.deltaTime;
            if (_breakTimer <= 0f)
            {
                ForceExitBreak();
            }
        }
        else
        {
            // 브레이크 상태가 아닐 때 서서히 회복
            if (_currentBreak > 0f && recoveryPerSecond > 0f)
            {
                _currentBreak = Mathf.Max(0f, _currentBreak - recoveryPerSecond * Time.deltaTime);
            }
        }
    }

    // BossHealth.OnDamaged 에 연결되는 콜백 -------------------
    private void HandleBossDamaged(int damage)
    {
        if (damage <= 0) return;
        if (_isInBreak) return; // 브레이크중에는 추가 축적 안 함 (필요하면 옵션으로 분리 가능)

        float add = baseBreakPerHit;
        _currentBreak = Mathf.Min(maxBreak, _currentBreak + add);

        if (logDebug)
        {
            Debug.Log(
                $"[Break] Damage={damage}, +{add} → {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})",
                this
            );
        }

        // 게이지가 최대에 도달하면 브레이크 진입
        if (_currentBreak >= maxBreak)
        {
            ForceEnterBreak();
        }
    }

    // 브레이크 상태 강제 진입 ---------------------------------
    public void ForceEnterBreak()
    {
        if (_isInBreak) return;

        _isInBreak = true;
        _breakTimer = breakDuration;

        // 스태거 On
        if (bossHealth != null)
        {
            // BossHealth.SetStaggered(bool) 는 public 이어야 함
            bossHealth.SetStaggered(true);
        }

        // 애니메이터 bool On
        if (bossAnimator != null && _breakBoolHash != 0)
        {
            bossAnimator.SetBool(_breakBoolHash, true);
        }

        // 이벤트 호출 (HUD, 카메라 연출 등)
        OnBreakEnter?.Invoke();

        if (logDebug)
        {
            Debug.Log("[Break] ENTER", this);
        }
    }

    // 브레이크 상태 강제 종료 ---------------------------------
    public void ForceExitBreak()
    {
        if (!_isInBreak) return;

        _isInBreak = false;
        _breakTimer = 0f;

        // 스태거 Off
        if (bossHealth != null)
        {
            bossHealth.SetStaggered(false);
        }

        // 애니메이터 bool Off
        if (bossAnimator != null && _breakBoolHash != 0)
        {
            bossAnimator.SetBool(_breakBoolHash, false);
        }

        // 게이지 클리어
        _currentBreak = 0f;

        // 이벤트 호출
        OnBreakExit?.Invoke();

        if (logDebug)
        {
            Debug.Log("[Break] EXIT", this);
        }
    }

    /// <summary>
    /// 게이지만 0으로 리셋 (상태는 유지)
    /// </summary>
    public void ResetGauge()
    {
        _currentBreak = 0f;

        if (logDebug)
        {
            Debug.Log("[Break] ResetGauge()", this);
        }
    }

    // 보스가 죽었을 때 처리 -----------------------------------
    private void HandleBossDied()
    {
        // 죽으면 브레이크 종료 + 게이지 리셋
        ForceExitBreak();
        ResetGauge();

        // 더 이상 이벤트 받을 필요 없음
        if (bossHealth != null)
        {
            bossHealth.OnDamaged -= HandleBossDamaged;
            bossHealth.OnDied -= HandleBossDied;
        }
    }

    private void OnValidate()
    {
        maxBreak = Mathf.Max(1f, maxBreak);
        baseBreakPerHit = Mathf.Max(0f, baseBreakPerHit);
        breakDuration = Mathf.Max(0f, breakDuration);
        recoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
    }
}
