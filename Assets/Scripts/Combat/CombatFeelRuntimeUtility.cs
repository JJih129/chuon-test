using System.Collections;
using UnityEngine;

public static class CombatFeelRuntimeUtility
{
    const float MinimumTimeScale = 0.0001f;
    const float DefaultFixedDeltaTime = 0.02f;
    static MonoBehaviour _activeHitStopOwner;
    static Coroutine _activeHitStopCoroutine;
    static int _activeHitStopSessionId;
    static bool _isHitStopActive;
    static float _currentAppliedTimeScale = 1f;
    static float _currentAppliedFixedDeltaTime = DefaultFixedDeltaTime;
    static readonly object HitStopTimeKey = new object();

    public static bool IsHitStopActive => _isHitStopActive;

    public static CameraShake ResolveCameraShake(CameraShake current)
    {
        if (current != null)
            return current;

        return GameplaySceneCache.ResolveMainCameraShake();
    }

    public static bool TryApplyCameraShake(
        ref CameraShake cameraShake,
        CombatFeelPreset preset,
        float minIntervalRealtime,
        ref float lastRealtime)
    {
        if (preset.CameraShakeAmplitude <= 0f || preset.CameraShakeDuration <= 0f)
            return false;

        float now = Time.unscaledTime;
        if (now - lastRealtime < Mathf.Max(0f, minIntervalRealtime))
            return false;

        cameraShake = ResolveCameraShake(cameraShake);
        if (cameraShake == null)
            return false;

        lastRealtime = now;
        cameraShake.Shake(preset.CameraShakeAmplitude, preset.CameraShakeDuration);
        return true;
    }

    public static void StartHitStop(
        MonoBehaviour owner,
        ref Coroutine handle,
        CombatFeelPreset preset,
        bool scaleFixedDeltaTime,
        float minIntervalRealtime,
        ref float lastRealtime)
    {
        StartHitStop(
            owner,
            ref handle,
            preset.TimeScale,
            preset.StopDuration,
            scaleFixedDeltaTime,
            minIntervalRealtime,
            ref lastRealtime);
    }

    public static void StartHitStop(
        MonoBehaviour owner,
        ref Coroutine handle,
        float timeScale,
        float duration,
        bool scaleFixedDeltaTime,
        float minIntervalRealtime,
        ref float lastRealtime)
    {
        if (owner == null || !owner.isActiveAndEnabled)
            return;

        if (duration <= 0f)
            return;

        float now = Time.unscaledTime;
        if (now - lastRealtime < Mathf.Max(0f, minIntervalRealtime))
            return;

        lastRealtime = now;

        float appliedScale = Mathf.Clamp(timeScale, MinimumTimeScale, 1f);
        float appliedFixedDeltaTime = scaleFixedDeltaTime
            ? DefaultFixedDeltaTime * appliedScale
            : Time.fixedDeltaTime;

        if (_isHitStopActive)
            StopActiveHitStopCoroutine();

        _isHitStopActive = true;
        _currentAppliedTimeScale = appliedScale;
        _currentAppliedFixedDeltaTime = appliedFixedDeltaTime;
        _activeHitStopOwner = owner;
        int sessionId = ++_activeHitStopSessionId;
        handle = owner.StartCoroutine(CoHitStop(sessionId, appliedScale, duration, scaleFixedDeltaTime));
        _activeHitStopCoroutine = handle;
    }

    static IEnumerator CoHitStop(
        int sessionId,
        float appliedScale,
        float duration,
        bool scaleFixedDeltaTime)
    {
        // GameTimeScale owns Time.timeScale/fixedDeltaTime; overlapping effects resolve to the minimum.
        GameTimeScale.Request(HitStopTimeKey, appliedScale);

        yield return new WaitForSecondsRealtime(duration);

        if (sessionId != _activeHitStopSessionId)
            yield break;

        RestoreActiveHitStopState();
    }

    public static void ForceRestoreActiveHitStop(MonoBehaviour owner, ref Coroutine handle)
    {
        if (owner == null)
            return;

        if (_activeHitStopOwner != owner)
        {
            handle = null;
            return;
        }

        StopActiveHitStopCoroutine();
        RestoreActiveHitStopState();
        handle = null;
    }

    static void StopActiveHitStopCoroutine()
    {
        if (_activeHitStopOwner != null && _activeHitStopCoroutine != null)
            _activeHitStopOwner.StopCoroutine(_activeHitStopCoroutine);

        _activeHitStopCoroutine = null;
        _activeHitStopOwner = null;
    }

    static void RestoreActiveHitStopState()
    {
        GameTimeScale.Release(HitStopTimeKey);

        _isHitStopActive = false;
        _activeHitStopCoroutine = null;
        _activeHitStopOwner = null;
        _currentAppliedTimeScale = 1f;
        _currentAppliedFixedDeltaTime = DefaultFixedDeltaTime;
    }
}
