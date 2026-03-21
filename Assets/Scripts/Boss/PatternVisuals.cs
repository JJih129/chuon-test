using UnityEngine;

[DisallowMultipleComponent]
public class PatternVisuals : MonoBehaviour
{
    [Header("Renderer")]
    [Tooltip("텔레그래프를 보여줄 렌더러. 비우면 자식에서 자동 탐색합니다.")]
    public Renderer visualPartRenderer;

    [Header("Material Property")]
    [Tooltip("Emission이나 BaseColor 같은 색상 프로퍼티 이름")]
    public string emissionColorName = "_EmissionColor";

    [Header("Telegraph Colors")]
    [ColorUsage(true, true)] public Color parryColor = new Color(1.00f, 0.66f, 0.08f) * 5f;
    [ColorUsage(true, true)] public Color guardColor = new Color(0.22f, 0.78f, 1.00f) * 4.5f;
    [ColorUsage(true, true)] public Color dodgeColor = new Color(1.00f, 0.18f, 0.18f) * 5f;
    [ColorUsage(true, true)] public Color dangerColor = new Color(1.00f, 0.12f, 0.78f) * 6f;
    [ColorUsage(true, true)] public Color punishColor = new Color(0.36f, 1.00f, 0.78f) * 4.5f;
    [ColorUsage(true, true)] public Color idleColor = Color.black;

    [Header("Timing")]
    [Min(0.06f)] public float flashDuration = 0.45f;
    [Range(1, 4)] public int pulseCount = 2;
    [Range(0.1f, 1f)] public float punishHoldIntensity = 0.42f;

    MaterialPropertyBlock _propertyBlock;
    int _emissionColorId;
    Color _lastEmissionColor = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
    bool _telegraphCueActive;
    AttackTelegraphType _telegraphCueType;
    float _telegraphCueStartTime;
    float _telegraphCueDuration;
    bool _punishCueActive;
    float _punishCueStartTime;
    float _punishCueDuration;

    void Awake()
    {
        if (visualPartRenderer == null)
            visualPartRenderer = GetComponentInChildren<Renderer>(true);

        if (visualPartRenderer == null)
        {
            Debug.LogError("[PatternVisuals] Target renderer is missing.", this);
            enabled = false;
            return;
        }

        _propertyBlock = new MaterialPropertyBlock();
        _emissionColorId = Shader.PropertyToID(emissionColorName);
        SetEmissionColor(idleColor);
        enabled = false;
    }

    void Update()
    {
        UpdatePunishCue();
    }

    void OnDisable()
    {
        StopFlash();
        SetEmissionColor(idleColor);
    }

    public void SetParryable(bool isParryable)
    {
        StartVisualCue(isParryable ? AttackTelegraphType.Parry : AttackTelegraphType.Dodge, flashDuration);
    }

    public void StartVisualCue(bool isParryable)
    {
        StartVisualCue(isParryable ? AttackTelegraphType.Parry : AttackTelegraphType.Dodge, flashDuration);
    }

    public void StartVisualCue(AttackTelegraphType telegraphType, float duration)
    {
        if (!gameObject.activeInHierarchy)
            return;

        enabled = true;
        StopFlash();
        _telegraphCueActive = true;
        _telegraphCueType = telegraphType;
        _telegraphCueStartTime = Time.time;
        _telegraphCueDuration = Mathf.Max(0.06f, duration);
        SetEmissionColor(idleColor);
    }

    public void ResetToIdle()
    {
        StopFlash();
        SetEmissionColor(idleColor);
        enabled = false;
    }

    public void StartPunishCue(float duration)
    {
        if (!gameObject.activeInHierarchy)
            return;

        enabled = true;
        StopFlash();
        _punishCueActive = true;
        _punishCueStartTime = Time.unscaledTime;
        _punishCueDuration = Mathf.Max(0.08f, duration);
        SetEmissionColor(idleColor);
    }

    void StopFlash()
    {
        _telegraphCueActive = false;
        _punishCueActive = false;
    }

    Color ResolveColor(AttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case AttackTelegraphType.Parry:
                return parryColor;
            case AttackTelegraphType.Guard:
                return guardColor;
            case AttackTelegraphType.Danger:
                return dangerColor;
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Auto:
            default:
                return dodgeColor;
        }
    }

    void SetEmissionColor(Color color)
    {
        if (visualPartRenderer == null)
            return;
        if (_lastEmissionColor.Equals(color))
            return;

        _propertyBlock.SetColor(_emissionColorId, color);
        visualPartRenderer.SetPropertyBlock(_propertyBlock);
        _lastEmissionColor = color;
    }

    void UpdatePunishCue()
    {
        if (_telegraphCueActive)
        {
            UpdateTelegraphCue();
            return;
        }

        if (!_punishCueActive)
            return;

        float duration = Mathf.Max(0.08f, _punishCueDuration);
        float pulseIn = Mathf.Min(0.10f, duration * 0.22f);
        float pulseOut = Mathf.Min(0.12f, duration * 0.24f);
        float settle = 0.08f;
        float hold = Mathf.Max(0f, duration - pulseIn - pulseOut - settle);
        Color holdColor = Color.Lerp(idleColor, punishColor, Mathf.Clamp01(punishHoldIntensity));
        float elapsed = Time.unscaledTime - _punishCueStartTime;

        if (elapsed >= duration)
        {
            _punishCueActive = false;
            SetEmissionColor(idleColor);
            enabled = false;
            return;
        }

        if (elapsed <= pulseIn)
        {
            SetEmissionColor(Color.Lerp(idleColor, punishColor, Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, pulseIn))));
            return;
        }

        elapsed -= pulseIn;
        if (elapsed <= pulseOut)
        {
            SetEmissionColor(Color.Lerp(punishColor, holdColor, Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, pulseOut))));
            return;
        }

        elapsed -= pulseOut;
        if (elapsed <= hold)
        {
            SetEmissionColor(holdColor);
            return;
        }

        elapsed -= hold;
        SetEmissionColor(Color.Lerp(holdColor, idleColor, Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, settle))));
    }

    void UpdateTelegraphCue()
    {
        float duration = Mathf.Max(0.06f, _telegraphCueDuration);
        float elapsed = Time.time - _telegraphCueStartTime;
        if (elapsed >= duration)
        {
            _telegraphCueActive = false;
            SetEmissionColor(idleColor);
            enabled = false;
            return;
        }

        Color targetColor = ResolveColor(_telegraphCueType);
        int totalPulses = Mathf.Max(1, _telegraphCueType == AttackTelegraphType.Danger ? pulseCount + 1 : pulseCount);
        float pulseDuration = Mathf.Max(0.0001f, duration / totalPulses);
        float pulseElapsed = Mathf.Repeat(elapsed, pulseDuration);
        float pulseT = pulseElapsed / pulseDuration;
        float fadeInPortion = 0.32f;

        if (pulseT <= fadeInPortion)
        {
            float t = pulseT / Mathf.Max(0.0001f, fadeInPortion);
            SetEmissionColor(Color.Lerp(idleColor, targetColor, t));
            return;
        }

        float fadeOutT = (pulseT - fadeInPortion) / Mathf.Max(0.0001f, 1f - fadeInPortion);
        SetEmissionColor(Color.Lerp(targetColor, idleColor, fadeOutT));
    }

#if UNITY_EDITOR
    [ContextMenu("Test Parry Telegraph")]
    void TestParryTelegraph() => StartVisualCue(AttackTelegraphType.Parry, flashDuration);

    [ContextMenu("Test Guard Telegraph")]
    void TestGuardTelegraph() => StartVisualCue(AttackTelegraphType.Guard, flashDuration);

    [ContextMenu("Test Dodge Telegraph")]
    void TestDodgeTelegraph() => StartVisualCue(AttackTelegraphType.Dodge, flashDuration);

    [ContextMenu("Test Danger Telegraph")]
    void TestDangerTelegraph() => StartVisualCue(AttackTelegraphType.Danger, flashDuration);

    [ContextMenu("Reset To Idle")]
    void TestResetIdle() => ResetToIdle();
#endif
}
