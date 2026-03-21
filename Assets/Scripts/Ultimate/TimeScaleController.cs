using System.Collections;
using UnityEngine;

/// <summary>
/// 전역 타임스케일 관리(슬로모/프리즈/복원). PlayerDash 등에서 싱글톤으로 접근.
/// </summary>
public sealed class TimeScaleController : MonoBehaviour
{
    // ===================== [조절값 헤더] =====================
    [Header("타임스케일 기본 보정")]
    [Tooltip("기준 fixedDeltaTime(초). 타임스케일 변경 시 보정 기준. 보통 0.02")]
    [SerializeField] float baseFixedDeltaTime = 0.02f;

    [Header("슬로모 기본값")]
    [Tooltip("기본 슬로모 배율(0.1~1.0)")]
    [Range(0.01f, 1f)] [SerializeField] float defaultSlowScale = 0.2f;
    [Tooltip("기본 슬로모 지속시간(초)")]
    [SerializeField] float defaultSlowDuration = 0.3f;
    [Tooltip("슬로모 진입 블렌드 시간(초)")]
    [SerializeField] float defaultBlendIn = 0.06f;
    [Tooltip("슬로모 복원 블렌드 시간(초)")]
    [SerializeField] float defaultBlendOut = 0.12f;

    [Header("프리즈 프레임")]
    [Tooltip("프리즈 프레임 기본 지속(초). 아주 짧게 사용 권장")]
    [SerializeField] float defaultFreezeDuration = 0.05f;

    // ===================== [상태값] =====================
    [Tooltip("현재 적용 중 타임스케일(읽기전용)")]
    public float CurrentScale { get; private set; } = 1f;

    public static TimeScaleController Instance { get; private set; }

    Coroutine _lerpCo;
    float _restoreAtUnscaled = 0f;
    bool _pendingAutoRestore = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        baseFixedDeltaTime = Mathf.Max(0.001f, baseFixedDeltaTime);
        ApplyScale(1f);
        RefreshTickState();
    }

    void Update()
    {
        if (_pendingAutoRestore && Time.unscaledTime >= _restoreAtUnscaled)
        {
            _pendingAutoRestore = false;
            _restoreAtUnscaled = 0f;
            // 부드럽게 원복
            Restore(defaultBlendOut);
        }
    }

    // ===================== [외부 API] =====================

    /// <summary>
    /// PlayerDash 호환용. 지정 배율로 슬로모 진입 후 duration 뒤 자동 복원.
    /// </summary>
    public void SetSlowMotion(float toScale, float duration)
    {
        toScale = Mathf.Clamp(toScale, 0.01f, 1f);
        // 진입
        SetTimeScale(toScale, defaultBlendIn);
        // 예약 복원
        _pendingAutoRestore = true;
        _restoreAtUnscaled = Time.unscaledTime + Mathf.Max(0f, duration);
        RefreshTickState();
    }

    /// <summary>
    /// 기본값으로 슬로모 실행.
    /// </summary>
    public void SlowMoDefault()
        => SetSlowMotion(defaultSlowScale, defaultSlowDuration);

    /// <summary>
    /// 프리즈 프레임(완전 정지 후 자동 복원).
    /// </summary>
    public void FreezeFrame(float duration = -1f)
    {
        if (duration <= 0f) duration = defaultFreezeDuration;
        // 즉시 0으로
        if (_lerpCo != null) StopCoroutine(_lerpCo);
        ApplyScale(0f);
        _pendingAutoRestore = true;
        _restoreAtUnscaled = Time.unscaledTime + duration;
        RefreshTickState();
    }

    /// <summary>
    /// 즉시 또는 블렌드로 원복.
    /// </summary>
    public void ResetTimeScale(float blendOut = -1f)
    {
        if (blendOut < 0f) blendOut = defaultBlendOut;
        _pendingAutoRestore = false;
        _restoreAtUnscaled = 0f;
        Restore(blendOut);
        RefreshTickState();
    }

    // ===================== [기존 호환 API] =====================

    /// <summary>
    /// 원하는 배율로 블렌드 진입.
    /// </summary>
    public void SetTimeScale(float toScale, float blendDuration = 0.1f)
    {
        if (_lerpCo != null) StopCoroutine(_lerpCo);
        _lerpCo = StartCoroutine(LerpTimeScale(toScale, blendDuration));
        RefreshTickState();
    }

    /// <summary>
    /// 1.0으로 블렌드 복원.
    /// </summary>
    public void Restore(float blendDuration = 0.2f)
    {
        if (_lerpCo != null) StopCoroutine(_lerpCo);
        _lerpCo = StartCoroutine(LerpTimeScale(1f, blendDuration));
        RefreshTickState();
    }

    // ===================== [내부 구현] =====================

    IEnumerator LerpTimeScale(float target, float dur)
    {
        float start = Time.timeScale;
        float t = 0f;
        dur = Mathf.Max(0f, dur);
        if (dur == 0f)
        {
            ApplyScale(target);
            _lerpCo = null;
            RefreshTickState();
            yield break;
        }

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            ApplyScale(Mathf.Lerp(start, target, k));
            yield return null;
        }
        ApplyScale(target);
        _lerpCo = null;
        RefreshTickState();
    }

    void ApplyScale(float scale)
    {
        CurrentScale = scale;
        Time.timeScale = scale;
        Time.fixedDeltaTime = baseFixedDeltaTime * Mathf.Max(scale, 0.0001f);
    }

    void RefreshTickState()
    {
        enabled = _pendingAutoRestore || _lerpCo != null;
    }
}
