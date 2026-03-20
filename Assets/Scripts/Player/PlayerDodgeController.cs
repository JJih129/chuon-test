using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerDodgeController : MonoBehaviour
{
    public bool IsDodging => isDodging;

    [Header("References")]
    [SerializeField] Transform playerRoot;
    [SerializeField] Transform cameraTransform;
    [SerializeField] PlayerMoveController move;
    [SerializeField] PlayerLockOn playerLockOn;
    [SerializeField] PlayerCombatController combatController;
    [SerializeField] PlayerGuardController guardController;
    [SerializeField] PerfectDodgeController perfectDodgeController;
    [SerializeField] Animator animator;
    [SerializeField] CombatMoveLocker moveLocker;

    [Header("Input")]
    [SerializeField] KeyCode dodgeKey = KeyCode.LeftShift;

    [Header("Start Gate")]
    [SerializeField] bool denyDodgeWhileAttacking = true;
    [SerializeField] bool denyDodgeWhileGuarding = true;
    [SerializeField] bool denyDodgeWhileInHit = true;

    [Header("Movement")]
    [Tooltip("False = speed based, true = distance based")]
    [SerializeField] bool useDistanceBased = false;
    [SerializeField, Range(4f, 28f)] float dodgeSpeed = 18f;
    [SerializeField, Range(1f, 12f)] float dodgeDistance = 5f;
    [SerializeField, Range(0.05f, 0.6f)] float dodgeDuration = 0.25f;
    [SerializeField, Range(0f, 1f)] float dodgeCooldown = 0f;

    [Header("Perfect Dodge Redirect")]
    [SerializeField] bool sideStepOnPerfectDodge = true;
    [SerializeField, Range(0f, 1f)] float perfectDodgeRewindNormalizedTime = 0.28f;
    [SerializeField] bool openPerfectDodgeWindowOnDodgeStart = true;
    [SerializeField, Range(0.05f, 0.35f)] float perfectDodgeStartWindow = 0.22f;

    [Header("Speed Curve")]
    [SerializeField] AnimationCurve speedCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Animator Params")]
    [SerializeField] string p_IsDodging = "IsDodging";
    [SerializeField] string p_DodgeTrigger = "";

    [Header("Move Lock")]
    [SerializeField] bool lockMoveDuringDodge = true;
    [SerializeField] bool zeroVelocityOnDodge = true;
    [SerializeField] bool disableRootMotionOnDodge = true;

    [Header("Events")]
    public UnityEvent OnDodgeStart;
    public UnityEvent OnDodgeEnd;

    [Header("Audio")]
    [SerializeField] AudioSource dodgeAudioSource;
    [SerializeField] AudioClip[] dodgeStartClips;
    [SerializeField] AudioClip[] dodgeEndClips;
    [SerializeField, Range(0f, 1f)] float dodgeVolume = 1f;
    [SerializeField] Vector2 dodgePitchRandomRange = new Vector2(0.95f, 1.05f);
    [SerializeField] bool playEndSound = false;

    CharacterController cc;
    float cdTimer;
    bool isDodging;
    float elapsed;
    Vector3 dodgeDir;
    float baseSpeed;
    IInputBlocker inputBlocker;
    PlayerReferences playerReferences;
    bool hasIsDodgingParam;
    bool hasDodgeTriggerParam;

    void Reset()
    {
        CacheReferences();
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        CacheReferences();
        inputBlocker = GetComponent<IInputBlocker>();
        EnsureDodgeAudioSource();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Runtime animator cache is initialized in Awake().
    }
#endif

    void Update()
    {
        cdTimer -= Time.unscaledDeltaTime;

        if (IsInputBlocked())
        {
            if (isDodging)
                EndDodge();
            return;
        }

        if (isDodging)
        {
            TickDodge();
            return;
        }

        if (Input.GetKeyDown(dodgeKey) && cdTimer <= 0f && CanStartDodge())
            StartDodge();
    }

    void StartDodge()
    {
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        Transform basis = GetDodgeBasis();
        Vector3 forward = Flat(basis.forward);
        Vector3 right = Flat(basis.right);
        Vector3 wish = right * input.x + forward * input.y;
        dodgeDir = wish.sqrMagnitude > 0.001f ? wish.normalized : Flat(playerRoot.forward);

        baseSpeed = useDistanceBased
            ? Mathf.Max(0.01f, dodgeDistance / Mathf.Max(0.01f, dodgeDuration))
            : dodgeSpeed;

        isDodging = true;
        elapsed = 0f;
        cdTimer = dodgeCooldown;

        if (lockMoveDuringDodge && moveLocker != null)
            moveLocker.Lock("DODGE", dodgeDuration, zeroVelocityOnDodge, disableRootMotionOnDodge);

        if (animator != null)
        {
            if (hasIsDodgingParam)
                animator.SetBool(p_IsDodging, true);
            if (hasDodgeTriggerParam)
                animator.SetTrigger(p_DodgeTrigger);
        }

        if (openPerfectDodgeWindowOnDodgeStart && perfectDodgeController != null)
            perfectDodgeController.PerfectDodgeWindow_Pulse(perfectDodgeStartWindow);

        PlayDodgeStartSound();
        OnDodgeStart?.Invoke();
    }

    public void ApplyPerfectDodgeSideStep(Transform attacker)
    {
        if (!sideStepOnPerfectDodge || !isDodging)
            return;

        dodgeDir = GetPerfectDodgeSideDirection(attacker);

        float rewindTime = Mathf.Clamp01(perfectDodgeRewindNormalizedTime) * Mathf.Max(0.01f, dodgeDuration);
        if (elapsed > rewindTime)
            elapsed = rewindTime;
    }

    void TickDodge()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, dodgeDuration));
        float instantSpeed = baseSpeed * Mathf.Max(0f, speedCurve.Evaluate(t));

        cc.Move(dodgeDir * instantSpeed * Time.deltaTime);

        if (elapsed >= dodgeDuration)
            EndDodge();
    }

    void EndDodge()
    {
        isDodging = false;

        if (lockMoveDuringDodge && moveLocker != null)
            moveLocker.Unlock("DODGE");

        if (animator != null && hasIsDodgingParam)
            animator.SetBool(p_IsDodging, false);

        if (playEndSound)
            PlayDodgeEndSound();

        OnDodgeEnd?.Invoke();
    }

    void CacheReferences()
    {
        playerReferences = GetComponent<PlayerReferences>();

        if (playerRoot == null)
            playerRoot = playerReferences != null ? playerReferences.PlayerRoot : transform;
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (animator == null)
        {
            animator = playerReferences != null && playerReferences.MainAnimator != null
                ? playerReferences.MainAnimator
                : GetComponentInChildren<Animator>();
        }
        if (move == null)
            move = GetComponent<PlayerMoveController>();
        if (playerLockOn == null)
            playerLockOn = GetComponent<PlayerLockOn>();
        if (combatController == null)
            combatController = GetComponent<PlayerCombatController>();
        if (guardController == null)
            guardController = GetComponent<PlayerGuardController>();
        if (perfectDodgeController == null)
            perfectDodgeController = GetComponent<PerfectDodgeController>();
        if (moveLocker == null)
            moveLocker = GetComponent<CombatMoveLocker>();

        RefreshAnimatorParameterCache();
    }

    bool CanStartDodge()
    {
        if (denyDodgeWhileAttacking && combatController != null && combatController.IsAttacking)
            return false;

        if (denyDodgeWhileGuarding && guardController != null && guardController.IsGuarding)
            return false;

        if (denyDodgeWhileInHit && combatController != null && combatController.IsInHit)
            return false;

        return true;
    }

    static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        if (value.sqrMagnitude < 0.0001f)
            return Vector3.forward;
        return value.normalized;
    }

    Transform GetDodgeBasis()
    {
        if (playerLockOn != null && playerLockOn.HasTarget && playerRoot != null)
            return playerRoot;

        if (cameraTransform != null)
            return cameraTransform;

        return playerRoot != null ? playerRoot : transform;
    }

    void RefreshAnimatorParameterCache()
    {
        hasIsDodgingParam = HasAnimatorParameter(animator, p_IsDodging, AnimatorControllerParameterType.Bool);
        hasDodgeTriggerParam = HasAnimatorParameter(animator, p_DodgeTrigger, AnimatorControllerParameterType.Trigger);
    }

    static bool HasAnimatorParameter(Animator targetAnimator, string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (targetAnimator == null || string.IsNullOrWhiteSpace(parameterName))
            return false;

        if (targetAnimator.runtimeAnimatorController == null)
            return false;

        foreach (var parameter in targetAnimator.parameters)
        {
            if (parameter.type == expectedType && parameter.name == parameterName)
                return true;
        }

        return false;
    }

    Vector3 GetPerfectDodgeSideDirection(Transform attacker)
    {
        Transform basis = GetDodgeBasis();
        Vector3 right = Flat(basis.right);
        float horizontalInput = Input.GetAxisRaw("Horizontal");

        if (Mathf.Abs(horizontalInput) > 0.1f)
            return right * Mathf.Sign(horizontalInput);

        if (attacker != null)
        {
            Vector3 toAttacker = Flat(attacker.position - transform.position);
            float attackerSide = Vector3.Dot(right, toAttacker);
            if (Mathf.Abs(attackerSide) > 0.01f)
                return attackerSide > 0f ? -right : right;
        }

        return Random.value < 0.5f ? -right : right;
    }

    void EnsureDodgeAudioSource()
    {
        if (dodgeAudioSource != null)
            return;

        dodgeAudioSource = GetComponent<AudioSource>();
        if (dodgeAudioSource != null)
            return;

        dodgeAudioSource = gameObject.AddComponent<AudioSource>();
        dodgeAudioSource.playOnAwake = false;
        dodgeAudioSource.spatialBlend = 1f;
        dodgeAudioSource.rolloffMode = AudioRolloffMode.Linear;
        dodgeAudioSource.maxDistance = 30f;
    }

    void PlayDodgeStartSound()
    {
        if (dodgeAudioSource == null)
            return;

        AudioClip clip = GetRandomClip(dodgeStartClips);
        if (clip == null)
            return;

        dodgeAudioSource.pitch = GetRandomPitch();
        dodgeAudioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(dodgeVolume));
    }

    void PlayDodgeEndSound()
    {
        if (dodgeAudioSource == null)
            return;

        AudioClip clip = GetRandomClip(dodgeEndClips);
        if (clip == null)
            return;

        dodgeAudioSource.pitch = GetRandomPitch();
        dodgeAudioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(dodgeVolume));
    }

    float GetRandomPitch()
    {
        if (dodgePitchRandomRange.y >= dodgePitchRandomRange.x && dodgePitchRandomRange.y > 0f)
            return Random.Range(dodgePitchRandomRange.x, dodgePitchRandomRange.y);

        return 1f;
    }

    AudioClip GetRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;

        int index = Random.Range(0, clips.Length);
        return clips[index];
    }

    bool IsInputBlocked()
    {
        return inputBlocker != null && inputBlocker.IsBlocked;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying && useDistanceBased)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
            Vector3 dir = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.05f, dir * dodgeDistance);
        }
    }
#endif
}
