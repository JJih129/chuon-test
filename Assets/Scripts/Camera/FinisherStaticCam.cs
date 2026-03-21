// FinisherStaticCam.cs
using UnityEngine;
using UnityEngine.Playables;
using Unity.Cinemachine;
#pragma warning disable CS0618

[DisallowMultipleComponent]
public class FinisherStaticCam : MonoBehaviour
{
    // ===== Player refs =====
    [Header("Player References")]
    [Tooltip("Root transform of the player using the finisher camera.")]
    public Transform player;
    [Tooltip("Optional head transform. Uses player position plus headHeight when null.")]
    public Transform headTransform;
    [Tooltip("Fallback head height when headTransform is not assigned.")]
    public float headHeight = 1.6f;

    [Header("Camera Placement")]
    [Tooltip("Distance from the player forward direction.")]
    public float forwardDistance = 3f;
    [Tooltip("World height offset for the camera.")]
    public float cameraHeight = 0.25f;
    [Tooltip("Left/right offset for the camera.")]
    public float lateralOffset = 0f;

    [Header("Cinemachine")]
    [Tooltip("Cinemachine virtual camera controlled by this component.")]
    public CinemachineVirtualCameraBase vCam;
    [Tooltip("Optional playable director reference.")]
    public PlayableDirector director;
    [Tooltip("Priority to use while the finisher camera is active.")]
    public int overridePriority = 100;
    [Tooltip("Priority restored when the finisher camera is released.")]
    public int restorePriority = 10;

    [Header("Player Mover")]
    [Tooltip("Moves the player toward the finisher anchor.")]
    public PlayerFinisherMover playerMover;

    // Runtime state
    Transform _anchor;
    int _oldPriority;

    void Awake()
    {
        if (vCam == null) vCam = GetComponent<CinemachineVirtualCameraBase>();
        _oldPriority = vCam != null ? vCam.Priority : 0;
    }

    // activationPosition is the world position where the finisher started
    public void LockAtActivation(Vector3 activationPosition)
    {
        if (player == null || vCam == null)
        {
            Debug.LogWarning("[FinisherStaticCam] player or vCam is not assigned.");
            return;
        }

        // Build the camera position from the player's forward at activation time
        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 camPos = activationPosition + forward * forwardDistance + Vector3.up * cameraHeight + player.right * lateralOffset;

        // Keep the vcam fixed in place and set a look target
        vCam.Follow = null;
        _anchor = new GameObject("FinisherAnchor").transform;
        _anchor.position = camPos + vCam.transform.forward * -1.5f;
        _anchor.rotation = Quaternion.LookRotation((player.position + Vector3.up * headHeight) - _anchor.position, Vector3.up);

        // Set camera position and rotation
        vCam.transform.position = camPos;
        if (headTransform != null) vCam.LookAt = headTransform;
        else
        {
            // Fall back to a simple look-at point above the player
            Vector3 lookTarget = player.position + Vector3.up * headHeight;
            vCam.transform.rotation = Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up);
        }

        // Raise priority so this vcam becomes active
        _oldPriority = vCam.Priority;
        vCam.Priority = overridePriority;
    }

    // Called by timeline signal to begin player movement
    // duration is movement time in seconds
    public void TriggerMoveToAnchor(float duration)
    {
        if (_anchor == null || playerMover == null)
        {
            Debug.LogWarning("[FinisherStaticCam] Anchor or playerMover is missing.");
            return;
        }
        playerMover.StartMoveToAnchor(_anchor, duration);
    }

    // Called when the finisher sequence ends
    public void Release()
    {
        if (vCam != null) vCam.Priority = _oldPriority;
        if (_anchor != null) { Destroy(_anchor.gameObject); _anchor = null; }
    }

    // Expose anchor when external scripts need it
    public Transform GetAnchor() => _anchor;
}
