using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerDodgeController : MonoBehaviour
{
    enum DodgeFeelProfile
    {
        Soulslike = 0,
        CustomCurve = 1
    }

    public bool IsDodging => isDodging;
    public float RemainingDodgeTime => isDodging ? Mathf.Max(0f, dodgeDuration - elapsed) : 0f;
    public float DodgeDuration => dodgeDuration;
    public bool HasBufferedDodge => HasBufferedDodgeRequest();
    public float BufferedDodgeRemaining => hasBufferedDodgeRequest ? Mathf.Max(0f, bufferedDodgeExpiresAt - Time.unscaledTime) : 0f;

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
    [SerializeField] bool enableBufferedDodge = true;
    [SerializeField, Range(0.05f, 0.35f)] float dodgeBufferTime = 0.18f;
    [SerializeField] bool allowAttackCancelIntoDodge = true;

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
    [SerializeField] DodgeFeelProfile feelProfile = DodgeFeelProfile.Soulslike;
    [SerializeField] AnimationCurve speedCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Soulslike Feel")]
    [SerializeField, Range(0.45f, 0.95f)] float movementEndNormalized = 0.74f;
    [SerializeField, Range(0.01f, 0.18f)] float commitNormalized = 0.07f;
    [SerializeField, Range(0.12f, 0.45f)] float burstPeakNormalized = 0.26f;
    [SerializeField, Range(0.02f, 0.28f)] float commitDistanceNormalized = 0.10f;
    [SerializeField, Range(0.30f, 0.95f)] float burstDistanceNormalized = 0.72f;
    [SerializeField, Range(0f, 0.35f)] float directionMemorySeconds = 0.18f;
    [SerializeField] bool rotateTowardDodgeDirection = true;
    [SerializeField] bool preserveLockOnFacing = true;
    [SerializeField] bool snapFacingOnDodgeStart = true;
    [SerializeField, Range(180f, 2160f)] float dodgeRotationSpeed = 1440f;
    [SerializeField, Range(0.05f, 0.45f)] float dodgeRotationWindowNormalized = 0.22f;

    [Header("Animator Params")]
    [SerializeField] string p_IsDodging = "IsDodging";
    [SerializeField] string p_DodgeTrigger = "";
    [SerializeField] bool forceImmediateDodgeState = true;
    [SerializeField] string dodgeStateName = "Base Layer.Dodge_Roll";
    [SerializeField, Range(0f, 0.08f)] float dodgeStateTransitionDuration = 0.02f;
    [SerializeField, Range(0f, 0.2f)] float dodgeStateStartNormalizedTime = 0f;

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
    float totalDodgeDistance;
    bool hasBufferedDodgeRequest;
    float bufferedDodgeExpiresAt = float.NegativeInfinity;
    IInputBlocker inputBlocker;
    ICombatStateReader combatStateReader;
    PlayerInputCommandBuffer inputCommandBuffer;
    PlayerReferences playerReferences;
    bool hasIsDodgingParam;
    bool hasDodgeTriggerParam;
    int dodgeStateHash;
    Vector3 lastCommittedMoveDirection;
    float lastCommittedMoveRealtime = float.NegativeInfinity;
    uint _lastConsumedDodgeCommandSequence;

    void Reset()
    {
        CacheReferences();
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        CacheReferences();
        combatStateReader = CombatStateReaderResolver.ResolveOrAttach(this);
        inputBlocker = GetComponent<IInputBlocker>();
        inputCommandBuffer = GetComponent<PlayerInputCommandBuffer>();
        if (inputCommandBuffer == null)
            inputCommandBuffer = gameObject.AddComponent<PlayerInputCommandBuffer>();
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
        UpdateDirectionMemory();

        if (IsInputBlocked())
        {
            ClearBufferedDodgeRequest();
            if (isDodging)
                EndDodge();
            return;
        }

        if (Input.GetKeyDown(dodgeKey))
            inputCommandBuffer?.RecordDodgePress();

        if (TryConsumeDodgePressCommand())
            HandleDodgeInputPressed();

        if (isDodging)
        {
            TickDodge();
            return;
        }

        TryConsumeBufferedDodgeRequest();
    }

    void StartDodge()
    {
        ClearBufferedDodgeRequest();
        dodgeDir = ResolveDodgeDirection();
        totalDodgeDistance = ResolveDodgeDistance();

        baseSpeed = useDistanceBased
            ? Mathf.Max(0.01f, dodgeDistance / Mathf.Max(0.01f, dodgeDuration))
            : dodgeSpeed;

        isDodging = true;
        elapsed = 0f;
        cdTimer = dodgeCooldown;

        if (lockMoveDuringDodge && moveLocker != null)
            moveLocker.Lock("DODGE", dodgeDuration, zeroVelocityOnDodge, disableRootMotionOnDodge);

        ApplyImmediateDodgeFacing();

        if (animator != null)
        {
            if (hasIsDodgingParam)
                animator.SetBool(p_IsDodging, true);
            if (hasDodgeTriggerParam)
                animator.SetTrigger(p_DodgeTrigger);
            PlayImmediateDodgeStateIfAvailable();
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
        float duration = Mathf.Max(0.01f, dodgeDuration);
        float previousElapsed = elapsed;
        elapsed += Time.deltaTime;
        float prevT = Mathf.Clamp01(previousElapsed / duration);
        float currentT = Mathf.Clamp01(elapsed / duration);

        ApplyDodgeFacing(currentT);

        if (feelProfile == DodgeFeelProfile.Soulslike)
        {
            float distanceStep = Mathf.Max(0f, EvaluateSoulslikeTravel01(currentT) - EvaluateSoulslikeTravel01(prevT)) * totalDodgeDistance;
            if (distanceStep > 0f)
                cc.Move(dodgeDir * distanceStep);
        }
        else
        {
            float instantSpeed = baseSpeed * Mathf.Max(0f, speedCurve.Evaluate(currentT));
            cc.Move(dodgeDir * instantSpeed * Time.deltaTime);
        }

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
        if (cameraTransform == null)
            cameraTransform = GameplaySceneCache.ResolveMainCameraTransform();
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
        if (combatStateReader == null)
            combatStateReader = CombatStateReaderResolver.ResolveExisting(this);

        RefreshAnimatorParameterCache();
    }

    bool CanStartDodge()
    {
        return CanStartDodge(false);
    }

    bool CanStartDodge(bool ignoreAttackGate)
    {
        if (!ignoreAttackGate && denyDodgeWhileAttacking && IsPlayerAttacking())
            return false;

        if (denyDodgeWhileGuarding && IsPlayerGuarding())
            return false;

        if (denyDodgeWhileInHit && IsPlayerInHitState())
            return false;

        return true;
    }

    void HandleDodgeInputPressed()
    {
        if (TryStartDodgeFromCurrentState())
            return;

        if (ShouldBufferDodgeRequest())
            BufferDodgeRequest();
    }

    bool TryStartDodgeFromCurrentState()
    {
        if (isDodging || IsInputBlocked())
            return false;

        if (cdTimer > 0f)
            return false;

        if (CanStartDodge())
        {
            StartDodge();
            return true;
        }

        if (allowAttackCancelIntoDodge &&
            denyDodgeWhileAttacking &&
            combatController != null &&
            IsPlayerAttacking() &&
            combatController.TryCancelIntoDodge() &&
            CanStartDodge(true))
        {
            StartDodge();
            return true;
        }

        return false;
    }

    bool ShouldBufferDodgeRequest()
    {
        if (!enableBufferedDodge || isDodging)
            return false;

        bool attackBlocked = allowAttackCancelIntoDodge &&
                             denyDodgeWhileAttacking &&
                             combatController != null &&
                             IsPlayerAttacking();

        bool cooldownRecoveringSoon = cdTimer > 0f && cdTimer <= dodgeBufferTime;
        return attackBlocked || cooldownRecoveringSoon;
    }

    void BufferDodgeRequest()
    {
        hasBufferedDodgeRequest = true;
        bufferedDodgeExpiresAt = Time.unscaledTime + Mathf.Max(0.01f, dodgeBufferTime);
        inputCommandBuffer?.RecordBufferedDodge();
    }

    void TryConsumeBufferedDodgeRequest()
    {
        if (!HasBufferedDodgeRequest())
            return;

        if (TryStartDodgeFromCurrentState())
            ClearBufferedDodgeRequest();
    }

    bool HasBufferedDodgeRequest()
    {
        if (!hasBufferedDodgeRequest)
            return false;

        if (Time.unscaledTime > bufferedDodgeExpiresAt)
        {
            ClearBufferedDodgeRequest();
            return false;
        }

        return true;
    }

    void ClearBufferedDodgeRequest()
    {
        hasBufferedDodgeRequest = false;
        bufferedDodgeExpiresAt = float.NegativeInfinity;
    }

    bool TryConsumeDodgePressCommand()
    {
        return inputCommandBuffer != null
            && inputCommandBuffer.TryConsumeLatest(
                PlayerInputCommandBuffer.CommandType.DodgePress,
                0.2f,
                ref _lastConsumedDodgeCommandSequence);
    }

    bool IsPlayerAttacking()
    {
        if (combatStateReader != null)
            return combatStateReader.IsAttacking();

        return combatController != null && combatController.IsAttacking;
    }

    bool IsPlayerGuarding()
    {
        if (combatStateReader != null)
            return combatStateReader.IsGuarding();

        return guardController != null && guardController.IsGuarding;
    }

    bool IsPlayerInHitState()
    {
        if (combatStateReader != null)
            return combatStateReader.IsInHitState();

        return combatController != null && combatController.IsInHit;
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
        dodgeStateHash = string.IsNullOrWhiteSpace(dodgeStateName) ? 0 : Animator.StringToHash(dodgeStateName);
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

    void PlayImmediateDodgeStateIfAvailable()
    {
        if (!forceImmediateDodgeState || animator == null || dodgeStateHash == 0)
            return;

        if (!animator.HasState(0, dodgeStateHash))
            return;

        animator.CrossFadeInFixedTime(
            dodgeStateHash,
            dodgeStateTransitionDuration,
            0,
            Mathf.Clamp01(dodgeStateStartNormalizedTime));
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

    void UpdateDirectionMemory()
    {
        if (isDodging)
            return;

        Vector3 candidate = Vector3.zero;

        if (move != null)
        {
            if (move.CurrentWishDirection.sqrMagnitude > 0.0004f)
                candidate = Flat(move.CurrentWishDirection);
            else if (move.CurrentPlanarVelocity.sqrMagnitude > 0.04f)
                candidate = Flat(move.CurrentPlanarVelocity);
        }

        if (candidate.sqrMagnitude <= 0.0004f)
        {
            Vector2 rawInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (rawInput.sqrMagnitude > 0.0001f)
            {
                Transform basis = GetDodgeBasis();
                Vector3 forward = Flat(basis.forward);
                Vector3 right = Flat(basis.right);
                candidate = Flat(right * rawInput.x + forward * rawInput.y);
            }
        }

        if (candidate.sqrMagnitude > 0.0004f)
        {
            lastCommittedMoveDirection = candidate;
            lastCommittedMoveRealtime = Time.unscaledTime;
        }
    }

    Vector3 ResolveDodgeDirection()
    {
        Vector3 wish = Vector3.zero;

        if (move != null)
        {
            if (move.CurrentWishDirection.sqrMagnitude > 0.0004f)
                wish = Flat(move.CurrentWishDirection);
            else if (move.CurrentPlanarVelocity.sqrMagnitude > 0.04f)
                wish = Flat(move.CurrentPlanarVelocity);
        }

        if (wish.sqrMagnitude <= 0.0004f)
        {
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 0.0001f)
            {
                Transform basis = GetDodgeBasis();
                Vector3 forward = Flat(basis.forward);
                Vector3 right = Flat(basis.right);
                wish = Flat(right * input.x + forward * input.y);
            }
        }

        if (wish.sqrMagnitude <= 0.0004f && Time.unscaledTime - lastCommittedMoveRealtime <= directionMemorySeconds)
            wish = lastCommittedMoveDirection;

        if (wish.sqrMagnitude <= 0.0004f && cc != null)
        {
            Vector3 planarVelocity = cc.velocity;
            planarVelocity.y = 0f;
            if (planarVelocity.sqrMagnitude > 0.04f)
                wish = Flat(planarVelocity);
        }

        if (wish.sqrMagnitude <= 0.0004f)
            wish = Flat(playerRoot != null ? playerRoot.forward : transform.forward);

        return wish.normalized;
    }

    float ResolveDodgeDistance()
    {
        if (useDistanceBased)
            return Mathf.Max(0.01f, dodgeDistance);

        return Mathf.Max(0.01f, dodgeSpeed * Mathf.Max(0.01f, dodgeDuration));
    }

    float EvaluateSoulslikeTravel01(float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        float commitT = Mathf.Clamp(commitNormalized, 0.01f, 0.3f);
        float peakT = Mathf.Clamp(burstPeakNormalized, commitT + 0.02f, 0.65f);
        float moveEndT = Mathf.Clamp(movementEndNormalized, peakT + 0.05f, 0.98f);
        float commitDist = Mathf.Clamp(commitDistanceNormalized, 0.01f, 0.4f);
        float peakDist = Mathf.Clamp(burstDistanceNormalized, commitDist + 0.1f, 0.95f);

        if (t <= commitT)
            return Mathf.SmoothStep(0f, commitDist, Mathf.InverseLerp(0f, commitT, t));

        if (t <= peakT)
        {
            float segment = Mathf.InverseLerp(commitT, peakT, t);
            segment = 1f - Mathf.Pow(1f - segment, 2.4f);
            return Mathf.Lerp(commitDist, peakDist, segment);
        }

        if (t <= moveEndT)
        {
            float segment = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(peakT, moveEndT, t));
            return Mathf.Lerp(peakDist, 1f, segment);
        }

        return 1f;
    }

    void ApplyImmediateDodgeFacing()
    {
        if (!CanRotateTowardDodgeDirection() || !snapFacingOnDodgeStart || playerRoot == null)
            return;

        playerRoot.rotation = Quaternion.LookRotation(dodgeDir, Vector3.up);
    }

    void ApplyDodgeFacing(float normalizedTime)
    {
        if (!CanRotateTowardDodgeDirection() || playerRoot == null)
            return;

        if (normalizedTime > dodgeRotationWindowNormalized)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(dodgeDir, Vector3.up);
        playerRoot.rotation = Quaternion.RotateTowards(
            playerRoot.rotation,
            targetRotation,
            dodgeRotationSpeed * Time.deltaTime);
    }

    bool CanRotateTowardDodgeDirection()
    {
        if (!rotateTowardDodgeDirection)
            return false;

        if (playerLockOn != null && playerLockOn.HasTarget && preserveLockOnFacing)
            return false;

        return dodgeDir.sqrMagnitude > 0.0004f;
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
