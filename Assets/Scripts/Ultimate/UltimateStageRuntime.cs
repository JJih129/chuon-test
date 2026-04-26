using Unity.Cinemachine;
using UnityEngine;
#pragma warning disable CS0618

[DisallowMultipleComponent]
public class UltimateStageRuntime : MonoBehaviour
{
    const string RuntimeRootName = "__UltimateStageRuntime";
    const string CameraNodeName = "SequenceCamera";
    const string PlayerAnchorName = "PlayerAnchor";
    const string VictimAnchorName = "VictimAnchor";
    const string FocusAnchorName = "FocusAnchor";
    const string WalkOutAnchorName = "WalkOutAnchor";
    const string FloorVisualName = "StageFloor";
    const string BackdropVisualName = "StageBackdrop";
    const string KeyLightName = "StageKeyLight";
    const string RimLightName = "StageRimLight";
    const string ShotOpenName = "Shot_Open";
    const string ShotOpenLookName = "Shot_Open_Look";
    const string ShotWideName = "Shot_Wide";
    const string ShotSlashLeftName = "Shot_Slash_L";
    const string ShotSlashRightName = "Shot_Slash_R";
    const string ShotFinisherName = "Shot_Finisher";
    const string ShotWalkOutName = "Shot_WalkOut";

    [SerializeField] Transform playerAnchor;
    [SerializeField] Transform victimAnchor;
    [SerializeField] Transform focusAnchor;
    [SerializeField] Transform walkOutAnchor;
    [SerializeField] Transform floorVisual;
    [SerializeField] Transform backdropVisual;
    [SerializeField] Transform shotOpenAnchor;
    [SerializeField] Transform shotOpenLookAnchor;
    [SerializeField] Transform shotWideAnchor;
    [SerializeField] Transform shotSlashLeftAnchor;
    [SerializeField] Transform shotSlashRightAnchor;
    [SerializeField] Transform shotFinisherAnchor;
    [SerializeField] Transform shotWalkOutAnchor;
    [SerializeField] CinemachineCamera sequenceCamera;
    [SerializeField] CameraShake cameraShake;
    [SerializeField] Camera presentationCamera;
    [SerializeField] Light keyLight;
    [SerializeField] Light rimLight;
    [SerializeField] float stageHeightOffset = 160f;
    [SerializeField] int presentationLayer = 31;
    [SerializeField] bool showFloorVisualDuringPresentation = true;
    [SerializeField] bool showBackdropVisualDuringPresentation = true;

    Camera _previousMainCamera;
    bool _presentationCaptureActive;
    bool _cachedSourceCameraEnabled = true;
    int _cachedSourceCullingMask = ~0;
    CameraClearFlags _cachedSourceClearFlags = CameraClearFlags.Skybox;
    Color _cachedSourceBackgroundColor = Color.black;

    public Transform PlayerAnchor => playerAnchor;
    public Transform VictimAnchor => victimAnchor;
    public Transform FocusAnchor => focusAnchor;
    public Transform WalkOutAnchor => walkOutAnchor;
    public Transform OpenShotAnchor => shotOpenAnchor;
    public Transform OpenShotLookAnchor => shotOpenLookAnchor;
    public Transform WideShotAnchor => shotWideAnchor;
    public Transform SlashLeftShotAnchor => shotSlashLeftAnchor;
    public Transform SlashRightShotAnchor => shotSlashRightAnchor;
    public Transform FinisherShotAnchor => shotFinisherAnchor;
    public Transform WalkOutShotAnchor => shotWalkOutAnchor;
    public CinemachineCamera SequenceCamera => sequenceCamera;
    public CameraShake SequenceCameraShake => cameraShake;
    public Camera PresentationCamera => presentationCamera;
    public float StageHeightOffset => stageHeightOffset;
    public int PresentationLayer => Mathf.Clamp(presentationLayer, 0, 31);

    public static UltimateStageRuntime GetOrCreate()
    {
        UltimateStageRuntime existing = FindAnyObjectByType<UltimateStageRuntime>();
        if (existing != null)
        {
            existing.EnsureRuntimeObjects();
            return existing;
        }

        GameObject runtimeRoot = GameObject.Find(RuntimeRootName);
        if (runtimeRoot == null)
            runtimeRoot = new GameObject(RuntimeRootName);

        UltimateStageRuntime runtime = runtimeRoot.GetComponent<UltimateStageRuntime>();
        if (runtime == null)
            runtime = runtimeRoot.AddComponent<UltimateStageRuntime>();

        runtime.EnsureRuntimeObjects();
        return runtime;
    }

    void Awake()
    {
        EnsureRuntimeObjects();
    }

    public void EnsureRuntimeObjects()
    {
        playerAnchor = EnsureChild(playerAnchor, PlayerAnchorName);
        victimAnchor = EnsureChild(victimAnchor, VictimAnchorName);
        focusAnchor = EnsureChild(focusAnchor, FocusAnchorName);
        walkOutAnchor = EnsureChild(walkOutAnchor, WalkOutAnchorName);
        shotOpenAnchor = EnsureChild(shotOpenAnchor, ShotOpenName);
        shotOpenLookAnchor = EnsureChild(shotOpenLookAnchor, ShotOpenLookName);
        shotWideAnchor = EnsureChild(shotWideAnchor, ShotWideName);
        shotSlashLeftAnchor = EnsureChild(shotSlashLeftAnchor, ShotSlashLeftName);
        shotSlashRightAnchor = EnsureChild(shotSlashRightAnchor, ShotSlashRightName);
        shotFinisherAnchor = EnsureChild(shotFinisherAnchor, ShotFinisherName);
        shotWalkOutAnchor = EnsureChild(shotWalkOutAnchor, ShotWalkOutName);
        floorVisual = EnsurePrimitiveChild(floorVisual, FloorVisualName, PrimitiveType.Quad);
        backdropVisual = EnsurePrimitiveChild(backdropVisual, BackdropVisualName, PrimitiveType.Quad);

        Transform cameraNode = sequenceCamera != null
            ? sequenceCamera.transform
            : EnsureChild(null, CameraNodeName);

        if (sequenceCamera == null)
            sequenceCamera = cameraNode.GetComponent<CinemachineCamera>() ?? cameraNode.gameObject.AddComponent<CinemachineCamera>();

        sequenceCamera.enabled = false;
        cameraShake = cameraNode.GetComponent<CameraShake>() ?? cameraNode.gameObject.AddComponent<CameraShake>();
        presentationCamera = ResolveOrCreatePresentationCamera(cameraNode.gameObject);
        if (presentationCamera != null)
        {
            if (!_presentationCaptureActive)
                presentationCamera.enabled = false;
            presentationCamera.clearFlags = CameraClearFlags.SolidColor;
            presentationCamera.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
        }
        sequenceCamera.Follow = null;
        sequenceCamera.LookAt = null;
        keyLight = ResolveOrCreateLight(keyLight, KeyLightName, LightType.Directional);
        rimLight = ResolveOrCreateLight(rimLight, RimLightName, LightType.Directional);
        SetLayerRecursively(transform, PresentationLayer);
        ConfigureStageVisuals();
        SetPresentationVisualsActive(_presentationCaptureActive);
    }

    public void SyncLensFrom(Camera sourceCamera)
    {
        if (sequenceCamera == null || sourceCamera == null)
            return;

        sequenceCamera.Lens = LensSettings.FromCamera(sourceCamera);

        if (presentationCamera == null)
            return;

        presentationCamera.fieldOfView = sourceCamera.fieldOfView;
        presentationCamera.nearClipPlane = sourceCamera.nearClipPlane;
        presentationCamera.farClipPlane = sourceCamera.farClipPlane;
        presentationCamera.cullingMask = sourceCamera.cullingMask;
        presentationCamera.orthographic = sourceCamera.orthographic;
        presentationCamera.orthographicSize = sourceCamera.orthographicSize;
        presentationCamera.allowHDR = sourceCamera.allowHDR;
        presentationCamera.allowMSAA = sourceCamera.allowMSAA;
    }

    public void BeginPresentationCapture(Camera sourceCamera)
    {
        EnsureRuntimeObjects();
        showFloorVisualDuringPresentation = true;
        showBackdropVisualDuringPresentation = true;
        SyncLensFrom(sourceCamera);
        if (sourceCamera != null)
        {
            _cachedSourceCameraEnabled = sourceCamera.enabled;
            _cachedSourceCullingMask = sourceCamera.cullingMask;
            _cachedSourceClearFlags = sourceCamera.clearFlags;
            _cachedSourceBackgroundColor = sourceCamera.backgroundColor;
        }
        _presentationCaptureActive = true;
        SetPresentationVisualsActive(true);

        _previousMainCamera = sourceCamera;
        SetSourceCameraSuppressed(true);
        if (presentationCamera != null)
        {
            if (_previousMainCamera != null)
                presentationCamera.depth = _previousMainCamera.depth + 100f;
            SetPresentationIsolation(false);
            presentationCamera.enabled = true;
        }
    }

    public void EndPresentationCapture()
    {
        _presentationCaptureActive = false;
        if (presentationCamera != null)
            presentationCamera.enabled = false;

        SetSourceCameraSuppressed(false);
        SetPresentationVisualsActive(false);
        _previousMainCamera = null;
    }

    public void SetPresentationIsolation(bool isolated)
    {
        if (presentationCamera == null)
            return;

        if (isolated)
        {
            presentationCamera.clearFlags = CameraClearFlags.SolidColor;
            presentationCamera.backgroundColor = new Color(0.24f, 0.32f, 0.43f, 1f);
            presentationCamera.cullingMask = 1 << PresentationLayer;
            return;
        }

        presentationCamera.clearFlags = _cachedSourceClearFlags;
        presentationCamera.backgroundColor = _cachedSourceBackgroundColor;
        presentationCamera.cullingMask = _cachedSourceCullingMask;
    }

    void SetSourceCameraSuppressed(bool suppressed)
    {
        if (_previousMainCamera == null)
            return;

        if (!_cachedSourceCameraEnabled)
        {
            _previousMainCamera.enabled = false;
            return;
        }

        _previousMainCamera.enabled = true;
        if (suppressed)
        {
            _previousMainCamera.cullingMask = 0;
            _previousMainCamera.clearFlags = CameraClearFlags.Depth;
            return;
        }

        _previousMainCamera.cullingMask = _cachedSourceCullingMask;
        _previousMainCamera.clearFlags = _cachedSourceClearFlags;
        _previousMainCamera.backgroundColor = _cachedSourceBackgroundColor;
    }

    public void PositionStage(Vector3 worldOrigin, Vector3 facingDirection)
    {
        Vector3 flatForward = facingDirection;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;

        flatForward.Normalize();
        transform.SetPositionAndRotation(worldOrigin + Vector3.up * stageHeightOffset, Quaternion.LookRotation(flatForward, Vector3.up));
    }

    public void PositionStageFixed(Vector3 worldOrigin)
    {
        transform.SetPositionAndRotation(worldOrigin + Vector3.up * stageHeightOffset, Quaternion.identity);
    }

    public Vector3 StagePoint(Vector3 localOffset)
    {
        return transform.TransformPoint(localOffset);
    }

    public Vector3 ComputeVictimPosition(Vector3 playerWorld, Vector3 liveVictimWorld, Vector3 forward, float distance, float heightOffset)
    {
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;

        flatForward.Normalize();

        Vector3 victimWorld = playerWorld + flatForward * Mathf.Max(0.8f, distance);
        victimWorld.y = liveVictimWorld.y + heightOffset;
        return victimWorld;
    }

    public void UpdateAnchors(Vector3 playerWorld, Vector3 victimWorld, Vector3 walkOutWorld)
    {
        EnsureRuntimeObjects();

        playerAnchor.position = playerWorld;
        victimAnchor.position = victimWorld;
        focusAnchor.position = Vector3.Lerp(playerWorld, victimWorld, 0.6f) + Vector3.up * 1.1f;
        walkOutAnchor.position = walkOutWorld;
        UpdateShotAnchors(playerWorld, victimWorld, walkOutWorld);
    }

    public void UpdateShotAnchors(Vector3 playerWorld, Vector3 victimWorld, Vector3 walkOutWorld)
    {
        EnsureRuntimeObjects();

        Vector3 focusPoint = Vector3.Lerp(playerWorld, victimWorld, 0.62f) + Vector3.up * 1.15f;
        Vector3 stageForward = victimWorld - playerWorld;
        stageForward.y = 0f;
        if (stageForward.sqrMagnitude < 0.0001f)
            stageForward = transform.forward;
        stageForward.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, stageForward).normalized;
        Vector3 walkDirection = walkOutWorld - playerWorld;
        walkDirection.y = 0f;
        if (walkDirection.sqrMagnitude < 0.0001f)
            walkDirection = stageForward;
        walkDirection.Normalize();

        Vector3 introLookPoint = playerWorld + stageForward * 0.26f + side * 0.78f + Vector3.up * 0.8f;
        if (shotOpenLookAnchor != null)
            shotOpenLookAnchor.position = introLookPoint;

        SetShotPose(shotOpenAnchor, playerWorld + stageForward * 0.12f - side * 1.08f + Vector3.up * 0.98f, introLookPoint);
        SetShotPose(shotWideAnchor, focusPoint - stageForward * 5.2f + side * 2.15f + Vector3.up * 1.36f, focusPoint);
        SetShotPose(shotSlashLeftAnchor, focusPoint - stageForward * 3.55f - side * 2.05f + Vector3.up * 1.08f, focusPoint);
        SetShotPose(shotSlashRightAnchor, focusPoint - stageForward * 3.55f + side * 2.05f + Vector3.up * 1.08f, focusPoint);
        SetShotPose(shotFinisherAnchor, victimWorld - stageForward * 4.6f + side * 2.15f + Vector3.up * 1.9f, victimWorld + Vector3.up * 1.15f);
        SetShotPose(shotWalkOutAnchor, walkOutWorld - walkDirection * 2.7f + Vector3.up * 1.46f - side * 0.25f, walkOutWorld + Vector3.up * 1.22f);
    }

    Transform EnsureChild(Transform existing, string childName)
    {
        if (existing != null)
            return existing;

        Transform child = transform.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    Transform EnsurePrimitiveChild(Transform existing, string childName, PrimitiveType primitiveType)
    {
        if (existing != null)
            return existing;

        Transform child = transform.Find(childName);
        if (child != null)
            return child;

        GameObject go = GameObject.CreatePrimitive(primitiveType);
        go.name = childName;
        go.transform.SetParent(transform, false);
        Collider primitiveCollider = go.GetComponent<Collider>();
        if (primitiveCollider != null)
        {
            if (Application.isPlaying)
                Destroy(primitiveCollider);
            else
                DestroyImmediate(primitiveCollider);
        }
        return go.transform;
    }

    Camera ResolveOrCreatePresentationCamera(GameObject owner)
    {
        if (owner == null)
            return null;

        if (presentationCamera != null)
            return presentationCamera;

        if (owner.TryGetComponent(out Camera existingCamera))
            return existingCamera;

        return owner.AddComponent<Camera>();
    }

    Light ResolveOrCreateLight(Light existing, string lightName, LightType lightType)
    {
        if (existing != null)
            return existing;

        Transform child = transform.Find(lightName);
        if (child == null)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(transform, false);
            child = lightObject.transform;
        }

        Light light = child.GetComponent<Light>();
        if (light == null)
            light = child.gameObject.AddComponent<Light>();
        light.type = lightType;
        return light;
    }

    void ConfigureStageVisuals()
    {
        if (floorVisual != null)
        {
            floorVisual.localPosition = new Vector3(0f, -0.04f, 3.15f);
            floorVisual.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floorVisual.localScale = new Vector3(18f, 10.5f, 1f);
            ConfigurePrimitiveRenderer(floorVisual.GetComponent<Renderer>(), new Color(0.18f, 0.24f, 0.32f, 1f));
        }

        if (backdropVisual != null)
        {
            backdropVisual.localPosition = new Vector3(0f, 2.65f, 10.8f);
            backdropVisual.localRotation = Quaternion.identity;
            backdropVisual.localScale = new Vector3(26f, 12.5f, 1f);
            ConfigurePrimitiveRenderer(backdropVisual.GetComponent<Renderer>(), new Color(0.2f, 0.28f, 0.38f, 1f));
        }

        if (keyLight != null)
        {
            keyLight.transform.localPosition = new Vector3(-2f, 4.5f, -1.5f);
            keyLight.transform.localRotation = Quaternion.Euler(34f, 24f, 0f);
            keyLight.intensity = 1.05f;
            keyLight.color = new Color(0.92f, 0.95f, 1f, 1f);
            keyLight.shadows = LightShadows.None;
        }

        if (rimLight != null)
        {
            rimLight.transform.localPosition = new Vector3(2.2f, 3.4f, 4.2f);
            rimLight.transform.localRotation = Quaternion.Euler(28f, -152f, 0f);
            rimLight.intensity = 0.8f;
            rimLight.color = new Color(0.45f, 0.72f, 1f, 1f);
            rimLight.shadows = LightShadows.None;
        }
    }

    void SetPresentationVisualsActive(bool active)
    {
        SetRendererEnabled(floorVisual, active && showFloorVisualDuringPresentation);
        SetRendererEnabled(backdropVisual, active && showBackdropVisualDuringPresentation);

        if (keyLight != null)
            keyLight.enabled = active;
        if (rimLight != null)
            rimLight.enabled = active;
    }

    void SetRendererEnabled(Transform target, bool enabled)
    {
        if (target == null)
            return;

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
            renderer.enabled = enabled;
    }

    void ConfigurePrimitiveRenderer(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return;

        Material material = renderer.sharedMaterial;
        if (material == null || material.shader != shader)
        {
            material = new Material(shader);
            renderer.sharedMaterial = material;
        }

        if (material.HasProperty("_Color"))
            material.color = color;
    }

    void SetShotPose(Transform shotAnchor, Vector3 position, Vector3 lookTarget)
    {
        if (shotAnchor == null)
            return;

        Vector3 direction = lookTarget - position;
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;

        shotAnchor.SetPositionAndRotation(position, Quaternion.LookRotation(direction.normalized, Vector3.up));
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
