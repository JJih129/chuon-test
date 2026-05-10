using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class RideableHoverObject : BaseInteractable
{
    static readonly string[] ImportedCameraNameTokens = { "cdp", "camera", "cam" };

    [Header("Vehicle Physics")]
    [SerializeField, Min(0f)] private float maxForwardSpeed = 22f;
    [SerializeField, Min(0f)] private float maxReverseSpeed = 8f;
    [SerializeField, Min(0f)] private float accelerationForce = 34f;
    [SerializeField, Min(0f)] private float brakeForce = 48f;
    [SerializeField, Min(0f)] private float rollingResistance = 7f;
    [SerializeField, Min(0f)] private float steeringDegreesPerSecond = 105f;
    [SerializeField, Range(0f, 1f)] private float lowSpeedSteerFactor = 0.25f;
    [SerializeField, Range(0f, 20f)] private float lateralGrip = 9f;

    [Header("Ride")]
    [SerializeField] private Transform rideSeat;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private BoxCollider interactionCollider;
    [SerializeField] private BoxCollider physicsCollider;
    [SerializeField] private string physicsLayerName = "Default";
    [SerializeField] private string interactionLayerName = "Interactable";
    [SerializeField] private bool autoConfigureInteractionCollider = false;
    [SerializeField] private Vector3 interactionSize = new Vector3(5.5f, 2.4f, 8f);
    [SerializeField] private Vector3 interactionCenter = new Vector3(0f, 1.1f, 0f);
    [SerializeField] private KeyCode dismountKey = KeyCode.F;
    [SerializeField] private bool blockPlayerInputWhileRiding = true;

    Transform _rider;
    Transform _previousParent;
    CharacterController _riderController;
    IInputBlocker _riderInputBlocker;
    Rigidbody _body;
    float _throttle;
    float _steer;
    float _ignoreDismountUntil;

    public override string GetPromptText()
    {
        return _rider == null ? promptText : "F: Dismount";
    }

    void Awake()
    {
        autoPickup = false;
        promptText = string.IsNullOrWhiteSpace(promptText) ? "F: Ride" : promptText;
        EnsureRuntimeSetup();
        DisableImportedVehicleCamerasInScene();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        DisableImportedVehicleCamerasInScene();
    }

    protected override void Reset()
    {
        base.Reset();
        autoPickup = false;
        promptText = "F: Ride";
        EnsureRuntimeSetup();
    }

    void Update()
    {
        if (_rider == null)
        {
            _throttle = 0f;
            _steer = 0f;
            return;
        }

        _throttle = Mathf.Clamp(Input.GetAxisRaw("Vertical"), -1f, 1f);
        _steer = Mathf.Clamp(Input.GetAxisRaw("Horizontal"), -1f, 1f);

        if (Time.unscaledTime >= _ignoreDismountUntil && Input.GetKeyDown(dismountKey))
            Dismount();
    }

    void FixedUpdate()
    {
        if (_body == null)
            return;

        Vector3 velocity = _body.velocity;
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        float forwardSpeed = Vector3.Dot(velocity, forward);

        if (_rider != null)
            ApplyDriveForces(forward, forwardSpeed);
        else
            ApplyResistance(forwardSpeed);

        ApplyLateralGrip(right);
        ApplySteering(forwardSpeed);
        ClampForwardSpeed();
    }

    public override bool TryInteract(object invoker = null)
    {
        if (_rider != null)
        {
            Dismount();
            return true;
        }

        Transform player = ResolvePlayer(invoker);
        if (player == null)
            return false;

        Mount(player);
        return true;
    }

    void ApplyDriveForces(Vector3 forward, float forwardSpeed)
    {
        bool braking = Mathf.Abs(_throttle) > 0.01f &&
                       Mathf.Abs(forwardSpeed) > 0.5f &&
                       Mathf.Sign(_throttle) != Mathf.Sign(forwardSpeed);

        float force = braking ? brakeForce : accelerationForce;
        if (Mathf.Abs(_throttle) > 0.01f)
            _body.AddForce(forward * (_throttle * force), ForceMode.Acceleration);
        else
            ApplyResistance(forwardSpeed);
    }

    void ApplyResistance(float forwardSpeed)
    {
        if (Mathf.Abs(forwardSpeed) < 0.05f)
            return;

        _body.AddForce(-transform.forward * (Mathf.Sign(forwardSpeed) * rollingResistance), ForceMode.Acceleration);
    }

    void ApplyLateralGrip(Vector3 right)
    {
        Vector3 lateralVelocity = right * Vector3.Dot(_body.velocity, right);
        _body.AddForce(-lateralVelocity * lateralGrip, ForceMode.Acceleration);
    }

    void ApplySteering(float forwardSpeed)
    {
        float absSpeed = Mathf.Abs(forwardSpeed);
        if (absSpeed < 0.1f || Mathf.Abs(_steer) < 0.01f)
            return;

        float speed01 = Mathf.InverseLerp(0f, maxForwardSpeed, absSpeed);
        float steerFactor = Mathf.Lerp(lowSpeedSteerFactor, 1f, speed01);
        float directionSign = forwardSpeed >= 0f ? 1f : -1f;
        float yaw = _steer * steeringDegreesPerSecond * steerFactor * directionSign * Time.fixedDeltaTime;
        _body.MoveRotation(_body.rotation * Quaternion.Euler(0f, yaw, 0f));
    }

    void ClampForwardSpeed()
    {
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 upVelocity = Vector3.Project(_body.velocity, Vector3.up);
        Vector3 lateralVelocity = right * Vector3.Dot(_body.velocity, right);
        float forwardSpeed = Mathf.Clamp(Vector3.Dot(_body.velocity, forward), -maxReverseSpeed, maxForwardSpeed);
        _body.velocity = forward * forwardSpeed + lateralVelocity + upVelocity;
    }

    void Mount(Transform player)
    {
        _rider = player;
        _previousParent = player.parent;
        _riderController = player.GetComponent<CharacterController>();
        _riderInputBlocker = player.GetComponent<IInputBlocker>() ?? player.GetComponentInParent<IInputBlocker>();

        if (_riderController != null)
            _riderController.enabled = false;

        if (blockPlayerInputWhileRiding && _riderInputBlocker != null)
            _riderInputBlocker.BlockAll(true);

        player.SetParent(rideSeat, false);
        player.localPosition = Vector3.zero;
        player.localRotation = Quaternion.identity;
        _ignoreDismountUntil = Time.unscaledTime + 0.25f;
    }

    void Dismount()
    {
        if (_rider == null)
            return;

        Transform rider = _rider;
        rider.SetParent(_previousParent, true);
        rider.SetPositionAndRotation(exitPoint.position, exitPoint.rotation);

        if (blockPlayerInputWhileRiding && _riderInputBlocker != null)
            _riderInputBlocker.BlockAll(false);

        if (_riderController != null)
            _riderController.enabled = true;

        _rider = null;
        _previousParent = null;
        _riderController = null;
        _riderInputBlocker = null;
        _ignoreDismountUntil = 0f;
    }

    void EnsureRuntimeSetup()
    {
        int physicsLayer = LayerMask.NameToLayer(physicsLayerName);
        if (physicsLayer >= 0)
            gameObject.layer = physicsLayer;

        _body = GetComponent<Rigidbody>();
        if (_body != null)
        {
            _body.useGravity = true;
            _body.mass = Mathf.Max(800f, _body.mass);
            _body.drag = 0.08f;
            _body.angularDrag = 4f;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            physicsCollider = box;
            physicsCollider.isTrigger = false;

            if (autoConfigureInteractionCollider && physicsCollider.size == Vector3.one)
            {
                physicsCollider.size = interactionSize;
                physicsCollider.center = interactionCenter;
            }
        }

        EnsureInteractionTrigger();

        if (rideSeat == null)
            rideSeat = EnsureChild("RideSeat", new Vector3(0f, 1.7f, -0.4f));

        if (exitPoint == null)
            exitPoint = EnsureChild("ExitPoint", new Vector3(-2.4f, 0f, -1.2f));

        if (anchor == null)
            anchor = rideSeat != null ? rideSeat : transform;
    }

    Transform EnsureChild(string childName, Vector3 localPosition)
    {
        Transform child = transform.Find(childName);
        if (child == null)
        {
            GameObject go = new GameObject(childName);
            child = go.transform;
            child.SetParent(transform, false);
        }

        child.localPosition = localPosition;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return child;
    }

    void EnsureInteractionTrigger()
    {
        if (interactionCollider != null)
        {
            interactionCollider.isTrigger = true;
            return;
        }

        Transform triggerRoot = transform.Find("RideInteractionTrigger");
        if (triggerRoot == null)
        {
            GameObject go = new GameObject("RideInteractionTrigger");
            triggerRoot = go.transform;
            triggerRoot.SetParent(transform, false);
        }

        triggerRoot.localPosition = Vector3.zero;
        triggerRoot.localRotation = Quaternion.identity;
        triggerRoot.localScale = Vector3.one;

        int interactableLayer = LayerMask.NameToLayer(interactionLayerName);
        if (interactableLayer >= 0)
            triggerRoot.gameObject.layer = interactableLayer;

        interactionCollider = triggerRoot.GetComponent<BoxCollider>();
        if (interactionCollider == null)
            interactionCollider = triggerRoot.gameObject.AddComponent<BoxCollider>();

        interactionCollider.isTrigger = true;
        interactionCollider.size = interactionSize;
        interactionCollider.center = interactionCenter;
    }

    static Transform ResolvePlayer(object invoker)
    {
        Transform candidate = null;
        if (invoker is InteractionManager interactionManager)
            candidate = interactionManager.PlayerRoot;

        if (candidate == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                candidate = playerObject.transform;
        }

        if (candidate == null)
            return null;

        PlayerReferences refs = candidate.GetComponent<PlayerReferences>() ?? candidate.GetComponentInParent<PlayerReferences>();
        return refs != null && refs.PlayerRoot != null ? refs.PlayerRoot : candidate;
    }

    void DisableImportedSceneComponents()
    {
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null)
                cameras[i].enabled = false;
        }

        AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null)
                listeners[i].enabled = false;
        }
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DisableImportedVehicleCamerasInScene();
    }

    static void DisableImportedVehicleCamerasInScene()
    {
        Camera mainCamera = Camera.main;
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera == mainCamera || camera.CompareTag("MainCamera"))
                continue;

            if (ShouldKeepCamera(camera))
                continue;

            camera.enabled = false;
        }

        AudioListener[] listeners = FindObjectsOfType<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null)
                continue;

            Camera ownerCamera = listener.GetComponent<Camera>();
            if (ownerCamera != null && (ownerCamera == mainCamera || ownerCamera.CompareTag("MainCamera")))
                continue;

            if (!ShouldKeepListener(listener))
                listener.enabled = false;
        }
    }

    static bool ShouldKeepCamera(Camera camera)
    {
        string path = GetHierarchyPath(camera.transform);
        if (path.Contains("MainCamera") || path.Contains("FreeLook Camera") || path.Contains("CM_"))
            return true;

        for (int i = 0; i < ImportedCameraNameTokens.Length; i++)
        {
            if (path.ToLowerInvariant().Contains(ImportedCameraNameTokens[i]))
                return false;
        }

        return camera.depth >= 10f;
    }

    static bool ShouldKeepListener(AudioListener listener)
    {
        string path = GetHierarchyPath(listener.transform);
        return path.Contains("MainCamera") || path.Contains("Audio");
    }

    static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        string path = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Dismount();
    }
}
