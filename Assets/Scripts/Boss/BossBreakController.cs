// 파일명: BossBreakController.cs
// 역할: 보스 브레이크 게이지 관리(0~100). 100 도달 시 무력화(딜타임) 이벤트.
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class BossBreakController : MonoBehaviour
{
    [Header("브레이크 게이지 | 0~100")]
    [Range(0,100)] public float breakGauge = 0f;
    [Tooltip("브레이크 도달 값(%)")] public float breakThreshold = 100f;

    [Header("브레이크 유지 시간(초)")]
    public float breakDuration = 5f;

    [Header("이벤트 | 브레이크 진입/해제")]
    public UnityEvent OnBreakEnter;
    public UnityEvent OnBreakExit;

    float _remain;
    bool  _isBreak;

    void Update()
    {
        if (_isBreak)
        {
            _remain -= Time.deltaTime;
            if (_remain <= 0f)
                ExitBreak();
        }
    }

    public void AddBreak(float amount)
    {
        if (_isBreak) return;
        breakGauge = Mathf.Clamp(breakGauge + amount, 0f, breakThreshold);
        if (breakGauge >= breakThreshold)
            EnterBreak();
    }

    public void ResetBreakGauge() => breakGauge = 0f;

    void EnterBreak()
    {
        _isBreak = true;
        _remain = breakDuration;
        OnBreakEnter?.Invoke();
        // 보스에 'isBreak=true' 알려주기(IUltimateTarget 구현체 등)
        var ult = GetComponent<IUltimateTarget>();
        if (ult is UltimateTargetSimple uts) uts.SetBreak(true);
    }

    void ExitBreak()
    {
        _isBreak = false;
        breakGauge = 0f;
        OnBreakExit?.Invoke();
        var ult = GetComponent<IUltimateTarget>();
        if (ult is UltimateTargetSimple uts) uts.SetBreak(false);
    }

    public bool IsBreak() => _isBreak;
    public float Get01() => Mathf.Clamp01(breakGauge / breakThreshold);
}