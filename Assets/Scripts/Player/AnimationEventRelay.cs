using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    [Header("Combat")]
    [Tooltip("PlayerCombatController가 붙은 부모 오브젝트")]
    public PlayerCombatController combatController;

    [Header("Footsteps")]
    [SerializeField] AudioSource footstepSource;
    [SerializeField] AudioClip[] walkFootstepClips;
    [SerializeField] AudioClip[] runFootstepClips;
    [SerializeField] AudioClip[] strafeFootstepClips;
    [SerializeField] bool useVelocityDrivenFootsteps = true;
    [SerializeField] bool requireMoveInputForFootsteps = true;
    [SerializeField, Range(0.05f, 1f)] float minMoveSpeedForFootsteps = 0.2f;
    [SerializeField, Range(0.05f, 1f)] float minStepInterval = 0.26f;
    [SerializeField, Range(0.1f, 1.5f)] float maxStepInterval = 0.46f;
    [SerializeField, Range(0.5f, 8f)] float maxSpeedForCadence = 4.5f;
    [SerializeField, Range(0.01f, 0.3f)] float globalFootstepCooldown = 0.08f;
    [SerializeField, Range(0.01f, 0.4f)] float sameFootCooldown = 0.14f;
    [SerializeField, Range(0f, 1f)] float footstepVolume = 0.7f;
    [SerializeField, Range(0f, 0.25f)] float footstepVolumeJitter = 0.08f;
    [SerializeField, Range(0f, 0.25f)] float footstepPitchJitter = 0.06f;
    [SerializeField, Range(0.5f, 1.5f)] float runVolumeMultiplier = 1.08f;
    [SerializeField, Range(0.5f, 1.5f)] float strafeVolumeMultiplier = 0.92f;

    PlayerMoveController _moveController;
    CharacterController _characterController;
    float _nextFootstepAt;
    float _lastFootstepAt = -999f;
    float _lastLeftFootAt = -999f;
    float _lastRightFootAt = -999f;
    int _lastWalkIndex = -1;
    int _lastRunIndex = -1;
    int _lastStrafeIndex = -1;

    enum FootstepMotion
    {
        Walk,
        Run,
        Strafe,
    }

    enum FootstepSide
    {
        Left,
        Right,
        Any,
    }

    public void EnableAttackHitbox()
    {
        if (combatController != null)
            combatController.EnableAttackHitbox();
    }

    public void DisableAttackHitbox()
    {
        if (combatController != null)
            combatController.DisableAttackHitbox();
    }

    public void PlayFootstep()
    {
        PlayFootstepInternal(FootstepMotion.Walk, FootstepSide.Any);
    }

    public void PlayWalkFootstepLeft() => PlayFootstepInternal(FootstepMotion.Walk, FootstepSide.Left);
    public void PlayWalkFootstepRight() => PlayFootstepInternal(FootstepMotion.Walk, FootstepSide.Right);
    public void PlayRunFootstepLeft() => PlayFootstepInternal(FootstepMotion.Run, FootstepSide.Left);
    public void PlayRunFootstepRight() => PlayFootstepInternal(FootstepMotion.Run, FootstepSide.Right);
    public void PlayStrafeFootstepLeft() => PlayFootstepInternal(FootstepMotion.Strafe, FootstepSide.Left);
    public void PlayStrafeFootstepRight() => PlayFootstepInternal(FootstepMotion.Strafe, FootstepSide.Right);

    void PlayFootstepInternal(FootstepMotion motion, FootstepSide side)
    {
        if (footstepSource == null)
            return;

        if (!CanPlayFootstep())
            return;

        float now = Time.time;
        if (now - _lastFootstepAt < globalFootstepCooldown)
            return;

        if (side == FootstepSide.Left && now - _lastLeftFootAt < sameFootCooldown)
            return;

        if (side == FootstepSide.Right && now - _lastRightFootAt < sameFootCooldown)
            return;

        AudioClip[] clips = ResolveBank(motion);
        int clipCount = clips != null ? clips.Length : 0;
        if (clipCount == 0)
            return;

        int index = ResolveNextClipIndex(clips, ref GetLastIndexRef(motion));
        if ((uint)index >= (uint)clipCount)
            return;

        AudioClip clip = clips[index];
        if (clip == null)
            return;

        float originalPitch = footstepSource.pitch;
        footstepSource.pitch = 1f + Random.Range(-footstepPitchJitter, footstepPitchJitter);

        float motionMul = 1f;
        if (motion == FootstepMotion.Run)
            motionMul = runVolumeMultiplier;
        else if (motion == FootstepMotion.Strafe)
            motionMul = strafeVolumeMultiplier;

        float volume = Mathf.Clamp01((footstepVolume + Random.Range(-footstepVolumeJitter, footstepVolumeJitter)) * motionMul);
        footstepSource.PlayOneShot(clip, volume);
        footstepSource.pitch = originalPitch;

        _lastFootstepAt = now;
        if (side == FootstepSide.Left)
            _lastLeftFootAt = now;
        else if (side == FootstepSide.Right)
            _lastRightFootAt = now;
    }

    AudioClip[] ResolveBank(FootstepMotion motion)
    {
        if (motion == FootstepMotion.Run && runFootstepClips != null && runFootstepClips.Length > 0)
            return runFootstepClips;

        if (motion == FootstepMotion.Strafe && strafeFootstepClips != null && strafeFootstepClips.Length > 0)
            return strafeFootstepClips;

        return walkFootstepClips;
    }

    ref int GetLastIndexRef(FootstepMotion motion)
    {
        if (motion == FootstepMotion.Run)
            return ref _lastRunIndex;

        if (motion == FootstepMotion.Strafe)
            return ref _lastStrafeIndex;

        return ref _lastWalkIndex;
    }

    int ResolveNextClipIndex(AudioClip[] clips, ref int lastIndex)
    {
        int clipCount = clips.Length;
        if (clipCount == 1)
        {
            lastIndex = 0;
            return 0;
        }

        int index = Random.Range(0, clipCount);
        if (index == lastIndex)
            index = (index + Random.Range(1, clipCount)) % clipCount;

        lastIndex = index;
        return index;
    }

    void Awake()
    {
        if (combatController == null)
            combatController = GetComponentInParent<PlayerCombatController>();

        if (footstepSource == null)
            footstepSource = GetComponent<AudioSource>();

        if (_moveController == null)
            _moveController = GetComponentInParent<PlayerMoveController>();

        if (_characterController == null)
            _characterController = GetComponentInParent<CharacterController>();
    }

    void Update()
    {
        if (!useVelocityDrivenFootsteps || footstepSource == null)
            return;

        AudioClip[] cadenceClips = ResolveBank(FootstepMotion.Walk);
        if (cadenceClips == null || cadenceClips.Length == 0)
            return;

        if (_characterController != null && !_characterController.isGrounded)
        {
            _nextFootstepAt = 0f;
            return;
        }

        if (_moveController == null)
            return;

        if (!CanPlayFootstep())
        {
            _nextFootstepAt = 0f;
            return;
        }

        float speed = _moveController.CurrentPlanarVelocity.magnitude;
        float now = Time.time;
        if (_nextFootstepAt > now)
            return;

        PlayFootstepInternal(FootstepMotion.Walk, FootstepSide.Any);

        float cadence01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, maxSpeedForCadence));
        float interval = Mathf.Lerp(maxStepInterval, minStepInterval, cadence01);
        _nextFootstepAt = now + interval;
    }

    bool CanPlayFootstep()
    {
        if (_characterController != null && !_characterController.isGrounded)
            return false;

        if (_moveController == null)
            return true;

        if (requireMoveInputForFootsteps && _moveController.CurrentMoveInput.sqrMagnitude <= 0.0004f)
            return false;

        return _moveController.CurrentPlanarVelocity.magnitude >= minMoveSpeedForFootsteps;
    }
}
