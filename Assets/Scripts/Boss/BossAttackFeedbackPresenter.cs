using System;
using System.Collections;
using UnityEngine;

[Serializable]
public struct BossAttackFeedbackProfile
{
    public BossAttackTelegraphType telegraphType;
    public bool weaponFlash;
    public bool bodyFlash;
    public bool warningSound;
    public bool chargeSound;
    public bool swingSound;
    public bool impactSound;
    public bool cameraShake;
    public bool timeScaleHitStop;
    public bool trailEffect;
    public bool groundWarning;
    public bool projectileWarning;
    public AudioClip warningClip;
    public AudioClip chargeClip;
    public AudioClip swingClip;
    public AudioClip impactClip;
    [Range(0f, 1f)] public float volume;
    [Min(0f)] public float cameraShakeAmplitude;
    [Min(0f)] public float cameraShakeDuration;
    [Range(0.01f, 1f)] public float hitStopTimeScale;
    [Min(0f)] public float hitStopDuration;
    public GameObject trailEffectPrefab;
    [Min(0f)] public float trailEffectLifetime;

    public static BossAttackFeedbackProfile Create(
        BossAttackTelegraphType telegraphType,
        bool weaponFlash,
        bool bodyFlash,
        bool warningSound,
        bool chargeSound,
        bool swingSound,
        bool impactSound,
        bool cameraShake,
        bool timeScaleHitStop,
        bool trailEffect,
        bool groundWarning,
        bool projectileWarning,
        float cameraShakeAmplitude,
        float cameraShakeDuration,
        float hitStopTimeScale,
        float hitStopDuration)
    {
        return new BossAttackFeedbackProfile
        {
            telegraphType = telegraphType,
            weaponFlash = weaponFlash,
            bodyFlash = bodyFlash,
            warningSound = warningSound,
            chargeSound = chargeSound,
            swingSound = swingSound,
            impactSound = impactSound,
            cameraShake = cameraShake,
            timeScaleHitStop = timeScaleHitStop,
            trailEffect = trailEffect,
            groundWarning = groundWarning,
            projectileWarning = projectileWarning,
            volume = 0.85f,
            cameraShakeAmplitude = cameraShakeAmplitude,
            cameraShakeDuration = cameraShakeDuration,
            hitStopTimeScale = hitStopTimeScale,
            hitStopDuration = hitStopDuration,
            trailEffectLifetime = 0.6f
        };
    }
}

[DisallowMultipleComponent]
public sealed class BossAttackFeedbackPresenter : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private BossAttackFeedbackProfile[] profiles =
    {
        BossAttackFeedbackProfile.Create(BossAttackTelegraphType.Parryable, true, false, true, false, true, true, false, false, true, false, false, 0f, 0f, 1f, 0f),
        BossAttackFeedbackProfile.Create(BossAttackTelegraphType.DodgeOnly, true, false, true, false, true, true, false, false, true, true, false, 0f, 0f, 1f, 0f),
        BossAttackFeedbackProfile.Create(BossAttackTelegraphType.Unblockable, true, true, true, true, true, true, true, true, true, true, false, 0.055f, 0.16f, 0.82f, 0.035f),
        BossAttackFeedbackProfile.Create(BossAttackTelegraphType.Heavy, true, true, false, true, true, true, true, true, true, true, false, 0.04f, 0.18f, 0.86f, 0.03f),
        BossAttackFeedbackProfile.Create(BossAttackTelegraphType.Ranged, true, false, true, true, true, true, false, false, true, false, true, 0f, 0f, 1f, 0f),
        BossAttackFeedbackProfile.Create(BossAttackTelegraphType.Normal, true, false, false, false, true, true, false, false, true, false, false, 0f, 0f, 1f, 0f)
    };

    [Header("Global Limits")]
    [SerializeField, Min(0f)] private float soundMinIntervalRealtime = 0.04f;
    [SerializeField, Min(0f)] private float cameraShakeMinIntervalRealtime = 0.08f;
    [SerializeField, Min(0f)] private float hitStopMinIntervalRealtime = 0.08f;

    [Header("Projectile Warning")]
    [SerializeField] private LineRenderer projectileWarningLine;
    [SerializeField] private Material projectileWarningMaterial;
    [SerializeField] private Color projectileWarningColor = new Color(0.2f, 0.85f, 1f, 0.86f);
    [SerializeField, Min(0.01f)] private float projectileWarningWidth = 0.08f;
    [SerializeField, Min(0f)] private float projectileWarningStartYOffset = 0.7f;
    [SerializeField, Min(0f)] private float projectileWarningEndYOffset = 0.35f;

    CameraShake _cameraShake;
    Coroutine _projectileWarningRoutine;
    Coroutine _hitStopRoutine;
    float _lastSoundRealtime = float.NegativeInfinity;
    float _lastCameraShakeRealtime = float.NegativeInfinity;
    float _lastHitStopRealtime = float.NegativeInfinity;

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    void OnDisable()
    {
        ResetFeedback();
    }

    public BossAttackTimingCueFlags ResolveProfileCueFlags(BossAttackTelegraphType telegraphType)
    {
        BossAttackFeedbackProfile profile = ResolveProfile(telegraphType);
        BossAttackTimingCueFlags flags = BossAttackTimingCueFlags.None;
        if (profile.weaponFlash) flags |= BossAttackTimingCueFlags.WeaponFlash;
        if (profile.bodyFlash) flags |= BossAttackTimingCueFlags.BodyFlash;
        if (profile.warningSound) flags |= BossAttackTimingCueFlags.WarningSound;
        if (profile.chargeSound) flags |= BossAttackTimingCueFlags.ChargeSound;
        if (profile.swingSound) flags |= BossAttackTimingCueFlags.SwingSound;
        if (profile.impactSound) flags |= BossAttackTimingCueFlags.ImpactSound;
        if (profile.cameraShake) flags |= BossAttackTimingCueFlags.CameraShake;
        if (profile.timeScaleHitStop) flags |= BossAttackTimingCueFlags.TimeScaleHitStop;
        if (profile.trailEffect) flags |= BossAttackTimingCueFlags.TrailEffect;
        if (profile.groundWarning) flags |= BossAttackTimingCueFlags.GroundWarning;
        if (profile.projectileWarning) flags |= BossAttackTimingCueFlags.ProjectileWarning;
        return flags;
    }

    public bool PlayTelegraphFeedback(
        BossAttackTelegraphType telegraphType,
        float duration,
        BossAttackTimingCueFlags cueFlags,
        BossTelegraphVfxPresenter telegraphVfx,
        BossGroundTelegraph groundTelegraph,
        Collider attackCollider,
        Transform owner,
        Transform target)
    {
        BossAttackFeedbackProfile profile = ResolveProfile(telegraphType);
        BossAttackTimingCueFlags profileFlags = ResolveProfileCueFlags(telegraphType);
        BossAttackTimingCueFlags resolvedFlags = cueFlags | profileFlags;
        bool visualHandled = false;

        BossAttackTimingCueFlags flashFlags = resolvedFlags & (BossAttackTimingCueFlags.WeaponFlash | BossAttackTimingCueFlags.BodyFlash);
        if (flashFlags != BossAttackTimingCueFlags.None && telegraphVfx != null)
            visualHandled = telegraphVfx.PlayCue(telegraphType, duration, flashFlags);

        if ((resolvedFlags & BossAttackTimingCueFlags.WarningSound) != 0)
            PlayOneShot(profile.warningClip, profile.volume);
        if ((resolvedFlags & BossAttackTimingCueFlags.ChargeSound) != 0)
            PlayOneShot(profile.chargeClip, profile.volume);
        if ((resolvedFlags & BossAttackTimingCueFlags.CameraShake) != 0)
            ApplyCameraShake(profile);
        if ((resolvedFlags & BossAttackTimingCueFlags.GroundWarning) != 0 && groundTelegraph != null && attackCollider != null)
            groundTelegraph.ShowCue(attackCollider, ConvertToGroundTelegraph(telegraphType), duration, owner);
        if ((resolvedFlags & BossAttackTimingCueFlags.ProjectileWarning) != 0)
            PlayProjectileWarning(owner, target, duration);

        return visualHandled;
    }

    public void PlaySwingFeedback(BossAttackTelegraphType telegraphType, Transform owner)
    {
        BossAttackFeedbackProfile profile = ResolveProfile(telegraphType);
        if (profile.swingSound)
            PlayOneShot(profile.swingClip, profile.volume);
        if (profile.trailEffect && profile.trailEffectPrefab != null && owner != null)
        {
            TransientVfxPool.Spawn(
                profile.trailEffectPrefab,
                owner.position,
                owner.rotation,
                null,
                Mathf.Max(0.05f, profile.trailEffectLifetime));
        }
    }

    public void PlayImpactFeedback(BossAttackTelegraphType telegraphType, Vector3 hitPoint, Transform owner)
    {
        BossAttackFeedbackProfile profile = ResolveProfile(telegraphType);
        if (profile.impactSound)
            PlayOneShot(profile.impactClip, profile.volume);
        if (profile.timeScaleHitStop)
        {
            CombatFeelRuntimeUtility.StartHitStop(
                this,
                ref _hitStopRoutine,
                profile.hitStopTimeScale,
                profile.hitStopDuration,
                true,
                hitStopMinIntervalRealtime,
                ref _lastHitStopRealtime);
        }
    }

    public void ResetFeedback()
    {
        if (_projectileWarningRoutine != null)
        {
            StopCoroutine(_projectileWarningRoutine);
            _projectileWarningRoutine = null;
        }

        if (projectileWarningLine != null)
            projectileWarningLine.gameObject.SetActive(false);

        CombatFeelRuntimeUtility.ForceRestoreActiveHitStop(this, ref _hitStopRoutine);
    }

    BossAttackFeedbackProfile ResolveProfile(BossAttackTelegraphType telegraphType)
    {
        if (profiles != null)
        {
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i].telegraphType == telegraphType)
                    return profiles[i];
            }
        }

        return BossAttackFeedbackProfile.Create(telegraphType, true, false, false, false, true, false, false, false, true, false, false, 0f, 0f, 1f, 0f);
    }

    void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip == null || audioSource == null)
            return;

        float now = Time.unscaledTime;
        if (now - _lastSoundRealtime < Mathf.Max(0f, soundMinIntervalRealtime))
            return;

        _lastSoundRealtime = now;
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    void ApplyCameraShake(BossAttackFeedbackProfile profile)
    {
        if (profile.cameraShakeAmplitude <= 0f || profile.cameraShakeDuration <= 0f)
            return;

        float now = Time.unscaledTime;
        if (now - _lastCameraShakeRealtime < Mathf.Max(0f, cameraShakeMinIntervalRealtime))
            return;

        _cameraShake = CombatFeelRuntimeUtility.ResolveCameraShake(_cameraShake);
        if (_cameraShake == null)
            return;

        _lastCameraShakeRealtime = now;
        _cameraShake.Shake(profile.cameraShakeAmplitude, profile.cameraShakeDuration);
    }

    void PlayProjectileWarning(Transform owner, Transform target, float duration)
    {
        if (owner == null || target == null)
            return;

        EnsureProjectileWarningLine();
        if (projectileWarningLine == null)
            return;

        if (_projectileWarningRoutine != null)
            StopCoroutine(_projectileWarningRoutine);

        _projectileWarningRoutine = StartCoroutine(CoProjectileWarning(owner, target, Mathf.Max(0.05f, duration)));
    }

    void EnsureProjectileWarningLine()
    {
        if (projectileWarningLine != null)
            return;

        GameObject lineObject = new GameObject("BossProjectileWarningLine");
        lineObject.transform.SetParent(transform, false);
        projectileWarningLine = lineObject.AddComponent<LineRenderer>();
        projectileWarningLine.positionCount = 2;
        projectileWarningLine.useWorldSpace = true;
        projectileWarningLine.numCapVertices = 4;
        projectileWarningLine.numCornerVertices = 2;
        projectileWarningLine.textureMode = LineTextureMode.Stretch;
        projectileWarningLine.sharedMaterial = projectileWarningMaterial != null
            ? projectileWarningMaterial
            : CreateDefaultLineMaterial();
        projectileWarningLine.gameObject.SetActive(false);
    }

    IEnumerator CoProjectileWarning(Transform owner, Transform target, float duration)
    {
        projectileWarningLine.gameObject.SetActive(true);
        projectileWarningLine.enabled = true;
        projectileWarningLine.startWidth = projectileWarningWidth;
        projectileWarningLine.endWidth = projectileWarningWidth;
        projectileWarningLine.startColor = projectileWarningColor;
        projectileWarningLine.endColor = projectileWarningColor;

        float elapsed = 0f;
        while (elapsed < duration && owner != null && target != null)
        {
            projectileWarningLine.SetPosition(0, owner.position + Vector3.up * projectileWarningStartYOffset);
            projectileWarningLine.SetPosition(1, target.position + Vector3.up * projectileWarningEndYOffset);
            elapsed += Time.deltaTime;
            yield return null;
        }

        projectileWarningLine.enabled = false;
        projectileWarningLine.gameObject.SetActive(false);
        _projectileWarningRoutine = null;
    }

    static Material CreateDefaultLineMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        return shader != null ? new Material(shader) : null;
    }

    static AttackTelegraphType ConvertToGroundTelegraph(BossAttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                return AttackTelegraphType.Parry;
            case BossAttackTelegraphType.DodgeOnly:
            case BossAttackTelegraphType.Ranged:
                return AttackTelegraphType.Dodge;
            case BossAttackTelegraphType.Unblockable:
            case BossAttackTelegraphType.Heavy:
                return AttackTelegraphType.Danger;
            case BossAttackTelegraphType.Normal:
                return AttackTelegraphType.Guard;
            default:
                return AttackTelegraphType.Auto;
        }
    }
}
