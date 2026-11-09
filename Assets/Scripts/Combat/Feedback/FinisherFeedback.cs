using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Finisher impact presentation: short slow-mo (via GameTimeScale), strong camera shake,
/// gamepad rumble and a distinct SFX. Embedded as a serialized settings block in BossBreakController;
/// coroutines run on the host MonoBehaviour. Call Stop() from the host's OnDisable/OnDestroy.
/// </summary>
[Serializable]
public class FinisherFeedback
{
    [Tooltip("Enable finisher impact presentation (slow-mo / shake / rumble / SFX).")]
    public bool useFeedback = true;

    [Header("Slow-mo")]
    [Tooltip("Time scale requested on finisher impact (GameTimeScale min-wins). 1 disables.")]
    [Range(0.01f, 1f)] public float slowMoTimeScale = 0.15f;

    [Tooltip("Realtime seconds the slow-mo is held before release.")]
    [Min(0f)] public float slowMoRealtimeDuration = 0.18f;

    [Header("Camera Shake")]
    [Tooltip("Multiplier applied to BossBreakController.finisherCameraShake amplitude on impact.")]
    [Min(0f)] public float shakeAmplitudeMultiplier = 1.6f;

    [Tooltip("Multiplier applied to BossBreakController.finisherCameraShake duration on impact.")]
    [Min(0f)] public float shakeDurationMultiplier = 1.25f;

    [Header("Rumble")]
    [Tooltip("Gamepad rumble on impact (only if a gamepad is connected).")]
    public bool useRumble = true;
    [Range(0f, 1f)] public float rumbleLowFrequency = 0.65f;
    [Range(0f, 1f)] public float rumbleHighFrequency = 0.85f;
    [Tooltip("Realtime rumble duration.")]
    [Min(0f)] public float rumbleDuration = 0.28f;

    [Header("SFX")]
    [Tooltip("Distinct finisher impact sound. Null = silent.")]
    public AudioClip impactClip;
    [Range(0f, 1f)] public float impactVolume = 1f;
    [Tooltip("Optional dedicated AudioSource. Null = shared OneShotAudio pool.")]
    public AudioSource audioSource;

    [NonSerialized] object _timeScaleKeyObj;
    object _timeScaleKey => _timeScaleKeyObj ??= new object();
    [NonSerialized] Coroutine _slowMoCo;
    [NonSerialized] Coroutine _rumbleCo;
    [NonSerialized] MonoBehaviour _host;

    public void PlayImpact(MonoBehaviour host, Vector3 hitPoint, Vector2 baseShake)
    {
        if (!useFeedback || host == null)
            return;

        _host = host;
        ActionColourDirector.NotifyFinisher(hitPoint); // 반복 11: 0.6 s 흑백 흡수
        // Coroutines keep running on a disabled behaviour; only an inactive GameObject blocks them.
        bool canRunCoroutines = host.gameObject.activeInHierarchy;

        // Slow-mo
        if (slowMoTimeScale < 1f && slowMoRealtimeDuration > 0f && canRunCoroutines)
        {
            if (_slowMoCo != null)
                host.StopCoroutine(_slowMoCo);
            GameTimeScale.Request(_timeScaleKey, slowMoTimeScale);
            _slowMoCo = host.StartCoroutine(CoReleaseSlowMo(slowMoRealtimeDuration));
        }

        // Camera shake (unscaled-time shaker, so it still reads during slow-mo)
        float amp = baseShake.x * shakeAmplitudeMultiplier;
        float dur = baseShake.y * shakeDurationMultiplier;
        if (amp > 0f && dur > 0f)
        {
            CameraShake shake = GameplaySceneCache.ResolveMainCameraShake();
            if (shake != null)
                shake.Shake(amp, dur);
        }

        // Rumble
        if (useRumble && rumbleDuration > 0f && canRunCoroutines)
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (_rumbleCo != null)
                    host.StopCoroutine(_rumbleCo);
                gamepad.SetMotorSpeeds(rumbleLowFrequency, rumbleHighFrequency);
                _rumbleCo = host.StartCoroutine(CoStopRumble(gamepad, rumbleDuration));
            }
        }

        // SFX
        if (impactClip != null)
        {
            if (audioSource != null)
                audioSource.PlayOneShot(impactClip, AudioOptionsRuntime.ScaleSfx(impactVolume));
            else
                OneShotAudio.PlayAt(impactClip, hitPoint, impactVolume, 1f, 0.35f);
        }
    }

    /// <summary>Release slow-mo and rumble immediately (host disabled/destroyed, finisher cancelled).</summary>
    public void Stop()
    {
        if (_host != null)
        {
            if (_slowMoCo != null) _host.StopCoroutine(_slowMoCo);
            if (_rumbleCo != null) _host.StopCoroutine(_rumbleCo);
        }

        _slowMoCo = null;
        GameTimeScale.Release(_timeScaleKey);

        if (_rumbleCo != null)
        {
            _rumbleCo = null;
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
                gamepad.SetMotorSpeeds(0f, 0f);
        }
    }

    IEnumerator CoReleaseSlowMo(float realtime)
    {
        yield return new WaitForSecondsRealtime(realtime);
        GameTimeScale.Release(_timeScaleKey);
        _slowMoCo = null;
    }

    IEnumerator CoStopRumble(Gamepad gamepad, float realtime)
    {
        yield return new WaitForSecondsRealtime(realtime);
        if (gamepad != null)
            gamepad.SetMotorSpeeds(0f, 0f);
        _rumbleCo = null;
    }
}
