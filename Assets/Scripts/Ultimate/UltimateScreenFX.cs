using UnityEngine;

[ExecuteAlways]
public class UltimateScreenFX : MonoBehaviour
{
    enum PlaybackMode
    {
        None,
        Tween,
        PulseUp,
        PulseDown
    }

    [Header("Assign the Quad's Renderer")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string propStrength = "_Strength";

    [Header("Curves & Timings")]
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Pulse Settings")]
    [SerializeField] private float pulseMinorStrength = 0.25f;
    [SerializeField] private float pulseMajorStrength = 0.6f;
    [SerializeField] private float pulseDuration = 0.25f;

    [Header("Charge Settings")]
    [SerializeField] private float chargeMaxStrength = 0.5f;
    [SerializeField] private float chargeInTime = 0.35f;
    [SerializeField] private float chargeOutTime = 0.25f;

    MaterialPropertyBlock _mpb;
    int _propStrengthId = -1;
    float _strength;
    float _lastAppliedStrength = float.MinValue;

    PlaybackMode _mode;
    float _fromStrength;
    float _targetStrength;
    float _phaseElapsed;
    float _phaseDuration;
    float _pulsePeak;
    float _pulseHalfDuration;

    void Awake()
    {
        ResolveReferences();
        ApplyStrengthImmediate(0f, force: true);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ResolveReferences();
        _propStrengthId = -1;
    }
#endif

    void Update()
    {
        if (_mode == PlaybackMode.None)
            return;

        float deltaTime = Application.isPlaying ? Time.unscaledDeltaTime : (1f / 60f);
        AdvancePlayback(deltaTime);
    }

    public void PlayChargeIn()
    {
        BeginTween(chargeMaxStrength, chargeInTime);
    }

    public void PlayChargeOut()
    {
        BeginTween(0f, chargeOutTime);
    }

    public void PulseMinor()
    {
        BeginPulse(pulseMinorStrength, pulseDuration);
    }

    public void PulseMajor()
    {
        BeginPulse(pulseMajorStrength, pulseDuration);
    }

    public void SetStrength(float value)
    {
        CancelPlayback();
        ApplyStrengthImmediate(Mathf.Clamp01(value), force: true);
    }

    public void Flash(float strength = 1f, float duration = 0.15f)
    {
        BeginPulse(Mathf.Clamp01(strength), Mathf.Max(0.01f, duration));
    }

    void ResolveReferences()
    {
        if (!targetRenderer)
            targetRenderer = GetComponentInChildren<Renderer>(true);
        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();
    }

    void BeginTween(float target, float duration)
    {
        _mode = PlaybackMode.Tween;
        _fromStrength = _strength;
        _targetStrength = Mathf.Clamp01(target);
        _phaseElapsed = 0f;
        _phaseDuration = Mathf.Max(0.01f, duration);
    }

    void BeginPulse(float peak, float duration)
    {
        _pulsePeak = Mathf.Clamp01(peak);
        _pulseHalfDuration = Mathf.Max(0.01f, duration * 0.5f);
        _fromStrength = _strength;
        _phaseElapsed = 0f;
        _phaseDuration = _pulseHalfDuration;
        _mode = PlaybackMode.PulseUp;
    }

    void CancelPlayback()
    {
        _mode = PlaybackMode.None;
        _phaseElapsed = 0f;
        _phaseDuration = 0f;
    }

    void AdvancePlayback(float deltaTime)
    {
        _phaseElapsed += Mathf.Max(0f, deltaTime);
        float normalized = Mathf.Clamp01(_phaseElapsed / Mathf.Max(0.0001f, _phaseDuration));
        float eased = ease != null ? ease.Evaluate(normalized) : normalized;

        switch (_mode)
        {
            case PlaybackMode.Tween:
                ApplyStrengthImmediate(Mathf.Lerp(_fromStrength, _targetStrength, eased));
                if (_phaseElapsed >= _phaseDuration)
                {
                    ApplyStrengthImmediate(_targetStrength, force: true);
                    CancelPlayback();
                }
                break;

            case PlaybackMode.PulseUp:
                ApplyStrengthImmediate(Mathf.Lerp(_fromStrength, _pulsePeak, eased));
                if (_phaseElapsed >= _phaseDuration)
                {
                    _fromStrength = _pulsePeak;
                    _targetStrength = 0f;
                    _phaseElapsed = 0f;
                    _phaseDuration = _pulseHalfDuration;
                    _mode = PlaybackMode.PulseDown;
                }
                break;

            case PlaybackMode.PulseDown:
                ApplyStrengthImmediate(Mathf.Lerp(_fromStrength, 0f, eased));
                if (_phaseElapsed >= _phaseDuration)
                {
                    ApplyStrengthImmediate(0f, force: true);
                    CancelPlayback();
                }
                break;
        }
    }

    void ApplyStrengthImmediate(float value, bool force = false)
    {
        _strength = Mathf.Clamp01(value);

        if (!targetRenderer)
            return;

        if (!force && Mathf.Abs(_lastAppliedStrength - _strength) < 0.0001f)
            return;

        ResolvePropertyId();
        _mpb.Clear();
        if (_propStrengthId != -1)
            _mpb.SetFloat(_propStrengthId, _strength);
        targetRenderer.SetPropertyBlock(_mpb);
        _lastAppliedStrength = _strength;
    }

    void ResolvePropertyId()
    {
        if (_propStrengthId == -1 && !string.IsNullOrEmpty(propStrength))
            _propStrengthId = Shader.PropertyToID(propStrength);
    }
}
