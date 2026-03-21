// Assets/Scripts/Combat/ParryFeedbackController.cs
using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
#pragma warning disable CS0618

/// Handles parry, block, and perfect-dodge feedback.
/// Uses hit stop, camera impulse, VFX, and SFX.
[DisallowMultipleComponent]
public class ParryFeedbackController : MonoBehaviour
{
    [Header("Hit Stop")]
    [SerializeField] bool useHitStop = true;

    [Tooltip("Time scale used for parry hit stop.")]
    public float parryTimeScale = 0.05f;

    [Tooltip("Realtime duration for parry hit stop.")]
    public float parryStopDuration = 0.08f;

    [Tooltip("Time scale used for block hit stop.")]
    public float blockTimeScale = 0.25f;

    [Tooltip("Realtime duration for block hit stop.")]
    public float blockStopDuration = 0.05f;

    [Tooltip("Time scale used for perfect-dodge hit stop.")]
    public float dodgeTimeScale = 0.12f;

    [Tooltip("Realtime duration for perfect-dodge hit stop.")]
    public float dodgeStopDuration = 0.12f;
    [SerializeField] bool usePerfectDodgeHitStop = false;

    [Tooltip("Scale fixedDeltaTime during hit stop.")]
    [SerializeField] bool scaleFixedDeltaTime = true;

    [Header("Camera Shake")]
    [SerializeField] CinemachineImpulseSource impulseSource;

    [Tooltip("Parry impulse direction and strength.")]
    public Vector3 parryVelocity = new(0f, -1f, 0f);
    [Range(0f, 3f)] public float parryForce = 1.5f;

    [Tooltip("Block impulse direction and strength.")]
    public Vector3 blockVelocity = new(0f, -0.6f, 0f);
    [Range(0f, 3f)] public float blockForce = 0.8f;

    [Tooltip("Perfect-dodge impulse direction and strength.")]
    public Vector3 dodgeVelocity = new(0f, -0.8f, 0f);
    [Range(0f, 3f)] public float dodgeForce = 1.2f;

    // Fallback camera shake when no impulse source is assigned
    [SerializeField] float fallbackShakeAmplitude = 0.2f;
    [SerializeField] float fallbackShakeDuration = 0.12f;
    [SerializeField] Transform fallbackCamera;

    [Header("VFX")]
    [SerializeField] GameObject parryVfxPrefab;
    [SerializeField] GameObject blockVfxPrefab;
    [SerializeField] GameObject dodgeVfxPrefab;
    [SerializeField] float vfxYOffset = 0.1f;
    [SerializeField] bool parentVfxToPlayer = false;

    [Header("SFX")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip parryClip;
    [SerializeField] AudioClip blockClip;
    [SerializeField] AudioClip dodgeClip;
    [Range(0f, 1f)] [SerializeField] float sfxVolume = 0.9f;

    [Header("Policy")]
    [SerializeField] bool useCombatFeelPolicy = true;

    [Header("Debug")]
    [SerializeField] bool debugLog = false;

    float _defaultFixedDelta;
    Coroutine _hitStopCo;

    void Awake()
    {
        _defaultFixedDelta = Time.fixedDeltaTime;
        if (!fallbackCamera && Camera.main) fallbackCamera = Camera.main.transform;
    }

    // ===== External API =====
    public void PlayParryFeedback(Vector3 hitPoint, Transform attacker)
    {
        CombatFeelPreset preset = ResolveDefensePreset(DefenseFeelKind.Parry);
        if (debugLog) Debug.Log("[ParryFX] Parry", this);
        if (useHitStop) StartHitStop(preset.TimeScale, preset.StopDuration);
        ShakeCamera(parryVelocity, preset.CameraImpulseForce, preset.CameraShakeAmplitude, preset.CameraShakeDuration);
        SpawnVfx(parryVfxPrefab, hitPoint);
        PlaySfx(parryClip, preset);
    }

    public void PlayBlockFeedback(Vector3 hitPoint, Transform attacker)
    {
        CombatFeelPreset preset = ResolveDefensePreset(DefenseFeelKind.Block);
        if (debugLog) Debug.Log("[ParryFX] Block", this);
        if (useHitStop) StartHitStop(preset.TimeScale, preset.StopDuration);
        ShakeCamera(blockVelocity, preset.CameraImpulseForce, preset.CameraShakeAmplitude, preset.CameraShakeDuration);
        SpawnVfx(blockVfxPrefab, hitPoint);
        PlaySfx(blockClip, preset);
    }

    public void PlayPerfectDodgeFeedback(Vector3 hitPoint, Transform attacker)
    {
        CombatFeelPreset preset = ResolveDefensePreset(DefenseFeelKind.PerfectDodge);
        if (debugLog) Debug.Log("[ParryFX] Perfect Dodge", this);
        if (useHitStop && usePerfectDodgeHitStop) StartHitStop(preset.TimeScale, preset.StopDuration);
        ShakeCamera(dodgeVelocity, preset.CameraImpulseForce, preset.CameraShakeAmplitude, preset.CameraShakeDuration);
        SpawnVfx(dodgeVfxPrefab, hitPoint);
        PlaySfx(dodgeClip, preset);
    }

    // Alias used by PlayerDamageReceiver
    public void PlayGuardBlockFeedback(Vector3 hitPoint, Transform attacker)
        => PlayBlockFeedback(hitPoint, attacker);

    // Overloads for inspector binding
    public void PlayParryFeedback()        => PlayParryFeedback(transform.position, transform);
    public void PlayBlockFeedback()        => PlayBlockFeedback(transform.position, transform);
    public void PlayPerfectDodgeFeedback() => PlayPerfectDodgeFeedback(transform.position, transform);
    public void PlayGuardBlockFeedback()   => PlayGuardBlockFeedback(transform.position, transform);

    void StartHitStop(float scale, float duration)
    {
        if (_hitStopCo != null) StopCoroutine(_hitStopCo);
        _hitStopCo = StartCoroutine(CoHitStop(Mathf.Clamp01(scale), Mathf.Max(0f, duration)));
    }

    IEnumerator CoHitStop(float scale, float duration)
    {
        float prevScale = Time.timeScale;
        float prevFixed = Time.fixedDeltaTime;

        Time.timeScale = Mathf.Max(0.0001f, scale);
        if (scaleFixedDeltaTime) Time.fixedDeltaTime = _defaultFixedDelta * Time.timeScale;

        yield return new WaitForSecondsRealtime(duration);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = scaleFixedDeltaTime ? _defaultFixedDelta : prevFixed;
        _hitStopCo = null;
    }

    CombatFeelPreset ResolveDefensePreset(DefenseFeelKind kind)
    {
        if (useCombatFeelPolicy)
            return CombatFeelPolicy.GetDefensePreset(kind);

        switch (kind)
        {
            case DefenseFeelKind.Parry:
                return new CombatFeelPreset(parryTimeScale, parryStopDuration, parryForce, fallbackShakeAmplitude, fallbackShakeDuration, 1f, 1f, 1f);

            case DefenseFeelKind.PerfectDodge:
                return new CombatFeelPreset(dodgeTimeScale, dodgeStopDuration, dodgeForce, fallbackShakeAmplitude, fallbackShakeDuration, 1f, 1f, 1f);

            case DefenseFeelKind.Block:
            default:
                return new CombatFeelPreset(blockTimeScale, blockStopDuration, blockForce, fallbackShakeAmplitude, fallbackShakeDuration, 1f, 1f, 1f);
        }
    }

    void ShakeCamera(Vector3 dir, float force, float shakeAmplitude, float shakeDuration)
    {
        if (impulseSource != null)
        {
            impulseSource.GenerateImpulse(dir * Mathf.Max(0f, force));
            return;
        }
        if (!fallbackCamera) return;
        StartCoroutine(CoSimpleShake(fallbackCamera, shakeAmplitude, shakeDuration));
    }

    IEnumerator CoSimpleShake(Transform cam, float amp, float dur)
    {
        Vector3 origin = cam.localPosition;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            cam.localPosition = origin + (Vector3)Random.insideUnitCircle * amp;
            yield return null;
        }
        cam.localPosition = origin;
    }

    void SpawnVfx(GameObject prefab, Vector3 hitPoint)
    {
        if (!prefab) return;
        Vector3 pos = hitPoint;
        pos.y += vfxYOffset;
        var go = TransientVfxPool.Spawn(prefab, pos, Quaternion.identity);
        if (go != null && parentVfxToPlayer) go.transform.SetParent(transform);
    }

    void PlaySfx(AudioClip clip, CombatFeelPreset preset)
    {
        if (!clip || !audioSource) return;

        float minPitch = Mathf.Min(preset.AudioPitchMin, preset.AudioPitchMax);
        float maxPitch = Mathf.Max(preset.AudioPitchMin, preset.AudioPitchMax);
        audioSource.pitch = Mathf.Approximately(minPitch, maxPitch)
            ? minPitch
            : Random.Range(minPitch, maxPitch);

        float scaledVolume = Mathf.Clamp01(sfxVolume * preset.AudioVolumeMultiplier);
        audioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(scaledVolume));
    }
}
