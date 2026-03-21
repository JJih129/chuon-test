using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

[DisallowMultipleComponent]
public class FreeLookCamera : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;
    public PlayerLockOn playerLockOn;

    [Header("Orbit")]
    public float distance = 4f;
    public float height = 1.5f;
    public float mouseSensitivity = 3f;
    public float minY = -30f;
    public float maxY = 60f;

    [Header("Auto-Align")]
    public bool autoAlignEnabled = false;
    public float autoAlignSpeed = 2.5f;
    public float alignDelay = 1.0f;
    public bool suppressAutoAlignWhileMoving = true;
    public float movingSpeedThreshold = 0.15f;
    public float mouseInputDeadzone = 0.01f;

    [Header("Zoom")]
    public float zoomStep = 5f;
    public float minFov = 30f;
    public float maxFov = 70f;

    float noInputTimer;
    float yaw;
    float pitch = 10f;
    bool orbitInitialized;
    float _nextReferenceResolveAt;
    const float ReferenceResolveInterval = 0.5f;

    CharacterController playerCharacterController;
    PlayerReferences playerReferences;
    CinemachineVirtualCameraBase legacyFreeLookCamera;

    public bool HasValidReferences => player != null;

    void Awake()
    {
        AutoResolveReferences();
        ResolveLegacyFreeLookCamera();
    }

    void OnEnable()
    {
        AutoResolveReferences();
        RestoreCursorLock();
    }

    void Start()
    {
        AutoResolveReferences();
        ResolveLegacyFreeLookCamera();
        InitializeOrbitFromTransform();
        RestoreCursorLock();
    }

    void LateUpdate()
    {
        if (Time.timeScale == 0f)
            return;

        if (!HasValidReferences)
            TryAutoResolveReferences();

        if (player == null)
            return;

        if (!orbitInitialized)
            InitializeOrbitFromTransform();

        if (playerLockOn != null && playerLockOn.IsLockedOn())
            return;

        bool hasLookInput = TryReadLookInput(out float mouseX, out float mouseY);
        if (IsUsingLegacyFreeLook())
        {
            SyncFromLegacyFreeLook();
            UpdateAutoAlign(hasLookInput);
            return;
        }

        if (hasLookInput)
        {
            ApplyManualMouseOrbit(mouseX, mouseY);
        }
        else
        {
            UpdateAutoAlign(false);
        }

        ApplyPose();
    }

    public void AutoResolveReferences()
    {
        if (playerLockOn == null)
            playerLockOn = GetComponentInParent<PlayerLockOn>() ?? FindAnyObjectByType<PlayerLockOn>();

        if (player == null)
        {
            playerReferences = GetComponentInParent<PlayerReferences>();
            if (playerReferences != null)
                player = playerReferences.PlayerRoot;

            if (player == null && playerLockOn != null)
                player = playerLockOn.transform;
        }

        if (player != null && (playerCharacterController == null || playerCharacterController.transform != player))
        {
            playerCharacterController = player.GetComponent<CharacterController>();
            if (playerCharacterController == null)
                playerCharacterController = player.GetComponentInParent<CharacterController>();
        }
    }

    void TryAutoResolveReferences()
    {
        if (Time.unscaledTime < _nextReferenceResolveAt)
            return;

        _nextReferenceResolveAt = Time.unscaledTime + ReferenceResolveInterval;
        AutoResolveReferences();
    }

    public void CaptureOrbitState(out float capturedYaw, out float capturedPitch)
    {
        if (IsUsingLegacyFreeLook())
        {
            if (!CinemachineCompat.TryGetLegacyFreeLookAxes(legacyFreeLookCamera, out capturedYaw, out capturedPitch))
            {
                capturedYaw = 0f;
                capturedPitch = 0.5f;
            }
            return;
        }

        if (!orbitInitialized)
            InitializeOrbitFromTransform();

        capturedYaw = yaw;
        capturedPitch = pitch;
    }

    public void RestoreOrbitState(float restoredYaw, float restoredPitch, bool applyImmediately = true)
    {
        if (IsUsingLegacyFreeLook())
        {
            CinemachineCompat.TrySetLegacyFreeLookAxes(legacyFreeLookCamera, restoredYaw, restoredPitch);
            yaw = NormalizeAngle(restoredYaw);
            pitch = ConvertLegacyFreeLookYToPitch(restoredPitch);
            noInputTimer = 0f;
            orbitInitialized = true;
            RestoreCursorLock();
            return;
        }

        yaw = NormalizeAngle(restoredYaw);
        pitch = Mathf.Clamp(restoredPitch, minY, maxY);
        noInputTimer = 0f;
        orbitInitialized = true;
        RestoreCursorLock();

        if (applyImmediately)
            ApplyPose();
    }

    public void RestoreManualControl(bool applyImmediately = true)
    {
        enabled = true;
        AutoResolveReferences();
        ResolveLegacyFreeLookCamera();
        noInputTimer = 0f;
        RestoreCursorLock();

        if (!orbitInitialized)
            InitializeOrbitFromTransform();

        if (applyImmediately && !IsUsingLegacyFreeLook())
            ApplyPose();
    }

    public void SnapBehindPlayer(float? overridePitch = null)
    {
        AutoResolveReferences();
        if (player == null)
            return;

        float snappedYaw = NormalizeAngle(player.eulerAngles.y);
        float snappedPitch = overridePitch.HasValue ? Mathf.Clamp(overridePitch.Value, minY, maxY) : pitch;

        if (IsUsingLegacyFreeLook())
        {
            float legacyY = ConvertPitchToLegacyFreeLookY(snappedPitch);
            CinemachineCompat.TrySetLegacyFreeLookAxes(legacyFreeLookCamera, snappedYaw, legacyY);
            yaw = snappedYaw;
            pitch = snappedPitch;
            orbitInitialized = true;
            noInputTimer = 0f;
            return;
        }

        yaw = snappedYaw;
        pitch = snappedPitch;

        orbitInitialized = true;
        noInputTimer = 0f;
        ApplyPose();
    }

    void InitializeOrbitFromTransform()
    {
        if (IsUsingLegacyFreeLook())
        {
            if (CinemachineCompat.TryGetLegacyFreeLookAxes(legacyFreeLookCamera, out float freeLookYaw, out float freeLookY))
            {
                yaw = NormalizeAngle(freeLookYaw);
                pitch = ConvertLegacyFreeLookYToPitch(freeLookY);
                orbitInitialized = true;
                return;
            }
        }

        Vector3 euler = transform.eulerAngles;
        yaw = NormalizeAngle(euler.y);
        pitch = Mathf.Clamp(NormalizePitch(euler.x), minY, maxY);
        orbitInitialized = true;
    }

    bool TryReadLookInput(out float mouseX, out float mouseY)
    {
        mouseX = 0f;
        mouseY = 0f;

        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            mouseX = delta.x * 0.02f;
            mouseY = delta.y * 0.02f;
        }
        else
        {
            mouseX = Input.GetAxisRaw("Mouse X");
            mouseY = Input.GetAxisRaw("Mouse Y");
        }

        return Mathf.Abs(mouseX) > mouseInputDeadzone || Mathf.Abs(mouseY) > mouseInputDeadzone;
    }

    bool IsPlayerMoving()
    {
        if (playerCharacterController != null)
        {
            Vector3 planarVelocity = playerCharacterController.velocity;
            planarVelocity.y = 0f;
            if (planarVelocity.magnitude > movingSpeedThreshold)
                return true;
        }

        if (Keyboard.current == null)
            return false;

        return Keyboard.current.wKey.isPressed
            || Keyboard.current.aKey.isPressed
            || Keyboard.current.sKey.isPressed
            || Keyboard.current.dKey.isPressed;
    }

    void ApplyPose()
    {
        if (player == null)
            return;

        Vector3 focusPoint = player.position + Vector3.up;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 orbitOffset = rotation * new Vector3(0f, 0f, -distance);
        Vector3 worldOffset = orbitOffset + Vector3.up * height;

        transform.position = player.position + worldOffset;
        transform.rotation = Quaternion.LookRotation(focusPoint - transform.position, Vector3.up);
    }

    void ApplyManualMouseOrbit(float mouseX, float mouseY)
    {
        yaw += mouseX * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - mouseY * mouseSensitivity, minY, maxY);
        noInputTimer = 0f;
    }

    void UpdateAutoAlign(bool hasLookInput)
    {
        if (hasLookInput)
        {
            noInputTimer = 0f;
            return;
        }

        if (suppressAutoAlignWhileMoving && IsPlayerMoving())
        {
            noInputTimer = 0f;
            return;
        }

        noInputTimer += Time.deltaTime;
        if (!autoAlignEnabled || noInputTimer <= alignDelay || player == null)
            return;

        float targetYaw = NormalizeAngle(player.eulerAngles.y);
        if (IsUsingLegacyFreeLook())
        {
            if (!CinemachineCompat.TryGetLegacyFreeLookAxes(legacyFreeLookCamera, out float currentYaw, out float currentY))
                return;

            float nextYaw = Mathf.LerpAngle(currentYaw, targetYaw, Time.deltaTime * autoAlignSpeed);
            CinemachineCompat.TrySetLegacyFreeLookAxes(legacyFreeLookCamera, nextYaw, currentY);
            yaw = NormalizeAngle(nextYaw);
            pitch = ConvertLegacyFreeLookYToPitch(currentY);
            orbitInitialized = true;
            return;
        }

        yaw = Mathf.LerpAngle(yaw, targetYaw, Time.deltaTime * autoAlignSpeed);
    }

    void SyncFromLegacyFreeLook()
    {
        if (!IsUsingLegacyFreeLook())
            return;

        if (!CinemachineCompat.TryGetLegacyFreeLookAxes(legacyFreeLookCamera, out float currentYaw, out float currentY))
            return;

        yaw = NormalizeAngle(currentYaw);
        pitch = ConvertLegacyFreeLookYToPitch(currentY);
        orbitInitialized = true;
    }

    void ResolveLegacyFreeLookCamera()
    {
        if (legacyFreeLookCamera != null)
            return;

        legacyFreeLookCamera = GetComponent<CinemachineVirtualCameraBase>();
        if (legacyFreeLookCamera != null && !CinemachineCompat.IsLegacyFreeLook(legacyFreeLookCamera))
            legacyFreeLookCamera = null;
    }

    bool IsUsingLegacyFreeLook()
    {
        if (legacyFreeLookCamera == null)
            ResolveLegacyFreeLookCamera();

        return legacyFreeLookCamera != null;
    }

    void RestoreCursorLock()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f)
            angle += 360f;
        return angle;
    }

    static float NormalizePitch(float angle)
    {
        if (angle > 180f)
            angle -= 360f;
        return angle;
    }

    float ConvertLegacyFreeLookYToPitch(float yAxisValue)
    {
        return Mathf.Lerp(minY, maxY, Mathf.Clamp01(yAxisValue));
    }

    float ConvertPitchToLegacyFreeLookY(float pitchAngle)
    {
        if (Mathf.Approximately(maxY, minY))
            return 0.5f;

        return Mathf.InverseLerp(minY, maxY, pitchAngle);
    }
}
