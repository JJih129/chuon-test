// Assets/Scripts/Ultimate/UltimateScreenFX.cs
using System.Collections;
using UnityEngine;

[ExecuteAlways]
public class UltimateScreenFX : MonoBehaviour
{
    [Header("Assign the Quad's Renderer")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string propStrength = "_Strength"; // 셰이더 프로퍼티명

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

    MaterialPropertyBlock mpb;
    Coroutine running;          // 현재 실행 중인 트윈/펄스
    float strength;             // 현재 강도(백킹 필드)

    void Awake()
    {
        if (!targetRenderer) targetRenderer = GetComponentInChildren<Renderer>(true);
        if (mpb == null) mpb = new MaterialPropertyBlock();
        ApplyStrength(0f);
    }

    // ---------------------------
    // 외부 API (PlayerUltimateController가 호출)
    // ---------------------------

    public void PlayChargeIn()
    {
        // 0 -> chargeMaxStrength로 부드럽게
        StartRoutine(TweenStrength(chargeMaxStrength, chargeInTime));
    }

    public void PlayChargeOut()
    {
        // 현재값 -> 0으로 부드럽게
        StartRoutine(TweenStrength(0f, chargeOutTime));
    }

    public void PulseMinor()
    {
        StartRoutine(Pulse(pulseMinorStrength, pulseDuration));
    }

    public void PulseMajor()
    {
        StartRoutine(Pulse(pulseMajorStrength, pulseDuration));
    }

    // ---------------------------
    // 호환용(기존에 쓰던 코드 대응)
    // ---------------------------

    public void SetStrength(float v)        // 즉시 세기 설정
    {
        ApplyStrength(Mathf.Clamp01(v));
    }

    public void Flash(float strength = 1f, float duration = 0.15f)   // 짧게 번쩍
    {
        StartRoutine(Pulse(Mathf.Clamp01(strength), Mathf.Max(0.01f, duration)));
    }

    // ---------------------------
    // 내부 구현
    // ---------------------------

    void StartRoutine(IEnumerator co)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(co);
    }

    IEnumerator TweenStrength(float target, float time)
    {
        float from = strength;
        float t = 0f;
        float dur = Mathf.Max(0.01f, time);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / dur);
            ApplyStrength(Mathf.Lerp(from, target, ease.Evaluate(x)));
            yield return null;
        }
        ApplyStrength(target);
        running = null;
    }

    IEnumerator Pulse(float peak, float dur)
    {
        // up
        float from = strength;
        float half = Mathf.Max(0.01f, dur * 0.5f);
        float t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / half);
            ApplyStrength(Mathf.Lerp(from, peak, ease.Evaluate(x)));
            yield return null;
        }

        // down (peak -> 0)
        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / half);
            ApplyStrength(Mathf.Lerp(peak, 0f, ease.Evaluate(x)));
            yield return null;
        }

        ApplyStrength(0f);
        running = null;
    }

    void ApplyStrength(float v)
    {
        strength = v;
        if (!targetRenderer) return;

        if (mpb == null) mpb = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat(propStrength, strength);
        targetRenderer.SetPropertyBlock(mpb);
    }
}
