using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    [Header("Combat")]
    [Tooltip("PlayerCombatController가 붙은 부모 오브젝트")]
    public PlayerCombatController combatController;

    [Header("Footsteps")]
    [SerializeField] AudioSource footstepSource;
    [SerializeField] AudioClip[] footstepClips;
    [SerializeField] bool useVelocityDrivenFootsteps = true;
    [SerializeField, Range(0.05f, 1f)] float minMoveSpeedForFootsteps = 0.2f;
    [SerializeField, Range(0.05f, 1f)] float minStepInterval = 0.26f;
    [SerializeField, Range(0.1f, 1.5f)] float maxStepInterval = 0.46f;
    [SerializeField, Range(0.5f, 8f)] float maxSpeedForCadence = 4.5f;
    [SerializeField, Range(0f, 1f)] float footstepVolume = 0.7f;
    [SerializeField, Range(0f, 0.25f)] float footstepVolumeJitter = 0.08f;
    [SerializeField, Range(0f, 0.25f)] float footstepPitchJitter = 0.06f;

    PlayerMoveController _moveController;
    CharacterController _characterController;
    float _nextFootstepAt;
    int _lastFootstepIndex = -1;

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
        if (footstepSource == null || footstepClips == null || footstepClips.Length == 0)
            return;

        AudioClip clip = ResolveNextFootstepClip();
        if (clip == null)
            return;

        float originalPitch = footstepSource.pitch;
        footstepSource.pitch = 1f + Random.Range(-footstepPitchJitter, footstepPitchJitter);
        float volume = Mathf.Clamp01(footstepVolume + Random.Range(-footstepVolumeJitter, footstepVolumeJitter));
        footstepSource.PlayOneShot(clip, volume);
        footstepSource.pitch = originalPitch;
    }

    AudioClip ResolveNextFootstepClip()
    {
        int clipCount = footstepClips.Length;
        if (clipCount == 0)
            return null;

        if (clipCount == 1)
            return footstepClips[0];

        int index = Random.Range(0, clipCount);
        if (index == _lastFootstepIndex)
            index = (index + Random.Range(1, clipCount)) % clipCount;

        _lastFootstepIndex = index;
        return footstepClips[index];
    }

    void Awake()
    {
        if (combatController == null)
            combatController = GetComponentInParent<PlayerCombatController>();

        if (footstepSource == null)
            footstepSource = GetComponent<AudioSource>();

        if (_moveController == null)
            _moveController = GetComponent<PlayerMoveController>();

        if (_characterController == null)
            _characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (!useVelocityDrivenFootsteps || footstepSource == null || footstepClips == null || footstepClips.Length == 0)
            return;

        if (_characterController != null && !_characterController.isGrounded)
        {
            _nextFootstepAt = 0f;
            return;
        }

        if (_moveController == null)
            return;

        float speed = _moveController.CurrentPlanarVelocity.magnitude;
        if (speed < minMoveSpeedForFootsteps)
        {
            _nextFootstepAt = 0f;
            return;
        }

        float now = Time.time;
        if (_nextFootstepAt > now)
            return;

        PlayFootstep();

        float cadence01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, maxSpeedForCadence));
        float interval = Mathf.Lerp(maxStepInterval, minStepInterval, cadence01);
        _nextFootstepAt = now + interval;
    }
}
