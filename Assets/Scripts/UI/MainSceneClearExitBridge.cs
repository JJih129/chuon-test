using System;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class MainSceneClearExitBridge : MonoBehaviour
{
    [SerializeField] private MainSceneArrivalController arrivalController;
    [SerializeField] private PlayerHUD playerHud;
    [SerializeField] private BossHealth bossHealth;
    [SerializeField] private Transform explicitExitRoot;
    [SerializeField] private Transform explicitAnchor;
    [SerializeField] private Transform explicitInteractionRoot;

    [Header("Exit Scene")]
    [SerializeField] private string nextSceneName = "CreditsScene";
    [SerializeField] private string promptText = "F: \ub2e4\uc74c \uad6c\uac04 \uc9c4\uc785";
    [SerializeField] private string readyMessage = "\ud558\uac15 \ub9c1\ud06c \uc900\ube44 \uc644\ub8cc";
    [SerializeField] private AttackTelegraphType readyTelegraphType = AttackTelegraphType.Guard;
    [SerializeField, Min(0.1f)] private float readyHold = 0.82f;
    [SerializeField] private int readyCuePriority = 18;

    [Header("Exit Target Search")]
    [SerializeField] private string[] fallbackExitNames = { "Elevator", "closedoor (1)", "opendoor (1)" };
    [SerializeField] private string anchorObjectName = "MainSceneExitAnchor";
    [SerializeField] private string interactionRootName = "MainSceneExitPrompt";
    [SerializeField] private float anchorHeightOffset = 0.18f;

    [Header("Interaction Trigger")]
    [SerializeField] private bool forceInteractableLayer = true;
    [SerializeField] private Vector3 triggerSize = new Vector3(1.6f, 2.1f, 1.6f);
    [SerializeField] private Vector3 triggerLocalOffset = new Vector3(0f, 1.05f, 0f);
    [SerializeField] private bool debugLogs = false;

    [Header("Exit Beacon")]
    [SerializeField] private bool showExitBeacon = true;
    [SerializeField] private float beaconHeight = 2.4f;
    [SerializeField] private float beaconWidth = 0.18f;
    [SerializeField] private float beaconVerticalOffset = 1.2f;
    [SerializeField] private Color beaconColor = new Color(0.35f, 0.92f, 1f, 0.9f);

    bool _fromLobbyTransition;
    bool _subscribed;
    bool _exitReady;
    Transform _resolvedExitRoot;
    Transform _resolvedAnchor;
    Transform _resolvedInteractionRoot;
    SceneLoadInteractable _exitInteractable;
    Collider _runtimeCollider;
    Transform _beaconRoot;

    public SceneLoadInteractable ActiveExitInteractable => _exitInteractable;
    public Transform ActiveExitTarget => _resolvedAnchor != null ? _resolvedAnchor : _resolvedExitRoot;

    public void ConfigureRuntime(
        MainSceneArrivalController runtimeArrivalController,
        PlayerHUD runtimeHud,
        BossHealth runtimeBossHealth)
    {
        arrivalController = runtimeArrivalController;
        playerHud = runtimeHud;
        bossHealth = runtimeBossHealth;

        ResolveReferences();
        _fromLobbyTransition = arrivalController != null && arrivalController.IsLobbyTransitionActive;
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        ResolveReferences();
        _fromLobbyTransition = arrivalController != null && arrivalController.IsLobbyTransitionActive;
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        SetBeaconVisible(false);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
        SetBeaconVisible(false);
    }

    void ResolveReferences()
    {
        if (arrivalController == null)
            arrivalController = GetComponent<MainSceneArrivalController>();
        if (playerHud == null)
            playerHud = FindObjectOfType<PlayerHUD>(true);
        if (bossHealth == null)
            bossHealth = FindObjectOfType<BossHealth>(true);
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (!_fromLobbyTransition || bossHealth == null || bossHealth.IsDead || _exitReady)
            return;

        bossHealth.OnDied += HandleBossDied;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed || bossHealth == null)
            return;

        bossHealth.OnDied -= HandleBossDied;
        _subscribed = false;
    }

    void HandleBossDied()
    {
        if (_exitReady)
            return;

        ReleaseSubscriptions();
        PrepareExitInteractable();
    }

    void PrepareExitInteractable()
    {
        _resolvedExitRoot = ResolveExitRoot();
        if (_resolvedExitRoot == null)
        {
            if (debugLogs)
                Debug.LogWarning("[MainSceneClearExitBridge] Exit root not found.", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogWarning($"[MainSceneClearExitBridge] Scene '{nextSceneName}' is not available in Build Settings.", this);
            return;
        }

        _resolvedAnchor = ResolveAnchor(_resolvedExitRoot);
        _resolvedInteractionRoot = ResolveInteractionRoot(_resolvedExitRoot, _resolvedAnchor);
        if (_resolvedInteractionRoot == null)
        {
            if (debugLogs)
                Debug.LogWarning("[MainSceneClearExitBridge] Interaction root not found.", this);
            return;
        }

        _exitInteractable = _resolvedInteractionRoot.GetComponent<SceneLoadInteractable>();
        if (_exitInteractable == null)
            _exitInteractable = _resolvedInteractionRoot.gameObject.AddComponent<FadedSceneLoadInteractable>();

        _exitInteractable.promptText = promptText;
        _exitInteractable.sceneName = nextSceneName;
        _exitInteractable.showLog = false;
        _exitInteractable.anchor = _resolvedAnchor != null ? _resolvedAnchor : _resolvedExitRoot;
        _exitInteractable.highlight = ResolveExistingHighlight(_resolvedExitRoot);

        _exitInteractable.BeforeSceneLoad -= HandleBeforeSceneLoad;
        _exitInteractable.BeforeSceneLoad += HandleBeforeSceneLoad;

        if (forceInteractableLayer)
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer >= 0)
                _resolvedInteractionRoot.gameObject.layer = interactableLayer;
        }

        _runtimeCollider = EnsureTriggerCollider(_resolvedInteractionRoot);
        EnsureBeaconVisuals();
        SetBeaconVisible(showExitBeacon);

        _exitReady = true;

        if (playerHud != null)
            playerHud.ShowRuntimeTelegraphMessage(readyMessage, readyTelegraphType, readyHold, false, readyCuePriority);

        if (debugLogs)
            Debug.Log($"[MainSceneClearExitBridge] Exit ready on '{_resolvedExitRoot.name}' -> {nextSceneName}", this);
    }

    void HandleBeforeSceneLoad(SceneLoadInteractable interactable, object invoker)
    {
        SetBeaconVisible(false);
        TutorialSceneTransitionState.MarkMainClearExit();
    }

    Transform ResolveExitRoot()
    {
        if (explicitExitRoot != null)
            return explicitExitRoot;

        if (fallbackExitNames == null || fallbackExitNames.Length == 0)
            return null;

        Transform[] transforms = FindObjectsOfType<Transform>(true);
        if (transforms == null || transforms.Length == 0)
            return null;

        for (int i = 0; i < fallbackExitNames.Length; i++)
        {
            string fallbackName = fallbackExitNames[i];
            if (string.IsNullOrWhiteSpace(fallbackName))
                continue;

            for (int j = 0; j < transforms.Length; j++)
            {
                Transform candidate = transforms[j];
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.name, fallbackName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }

        return null;
    }

    Transform ResolveAnchor(Transform exitRoot)
    {
        if (explicitAnchor != null)
            return explicitAnchor;

        Transform existingAnchor = exitRoot.Find(anchorObjectName);
        if (existingAnchor != null)
            return existingAnchor;

        Bounds bounds;
        if (!TryGetCombinedBounds(exitRoot, out bounds))
            return exitRoot;

        GameObject anchorObject = new GameObject(anchorObjectName);
        anchorObject.hideFlags = HideFlags.DontSave;

        Transform anchorTransform = anchorObject.transform;
        anchorTransform.SetParent(exitRoot, false);

        Vector3 worldPosition = bounds.center;
        worldPosition.y = bounds.max.y + anchorHeightOffset;
        anchorTransform.position = worldPosition;
        anchorTransform.rotation = exitRoot.rotation;
        return anchorTransform;
    }

    Transform ResolveInteractionRoot(Transform exitRoot, Transform anchor)
    {
        if (explicitInteractionRoot != null)
            return explicitInteractionRoot;

        Transform existingInteractionRoot = exitRoot.Find(interactionRootName);
        if (existingInteractionRoot != null)
            return existingInteractionRoot;

        GameObject interactionObject = new GameObject(interactionRootName);
        interactionObject.hideFlags = HideFlags.DontSave;

        Transform interactionTransform = interactionObject.transform;
        interactionTransform.SetParent(exitRoot, false);

        if (anchor != null)
        {
            interactionTransform.position = anchor.position;
            interactionTransform.rotation = anchor.rotation;
        }
        else
        {
            interactionTransform.position = exitRoot.position;
            interactionTransform.rotation = exitRoot.rotation;
        }

        interactionTransform.localPosition += triggerLocalOffset;
        return interactionTransform;
    }

    InteractionOutlineRenderer ResolveExistingHighlight(Transform exitRoot)
    {
        if (exitRoot == null)
            return null;

        InteractionOutlineRenderer highlight = exitRoot.GetComponent<InteractionOutlineRenderer>();
        if (highlight != null)
            return highlight;

        return exitRoot.GetComponentInChildren<InteractionOutlineRenderer>(true);
    }

    Collider EnsureTriggerCollider(Transform exitRoot)
    {
        Collider existingCollider = exitRoot.GetComponent<Collider>();
        if (existingCollider != null && existingCollider.isTrigger)
            return existingCollider;

        BoxCollider triggerCollider = exitRoot.gameObject.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.center = Vector3.zero;
        triggerCollider.size = new Vector3(
            Mathf.Max(0.2f, triggerSize.x),
            Mathf.Max(0.6f, triggerSize.y),
            Mathf.Max(0.2f, triggerSize.z));

        return triggerCollider;
    }

    void EnsureBeaconVisuals()
    {
        if (!showExitBeacon || _resolvedAnchor == null || _beaconRoot != null)
            return;

        GameObject beaconObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beaconObject.name = "MainSceneExitBeacon";
        beaconObject.hideFlags = HideFlags.DontSave;

        Collider collider = beaconObject.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        _beaconRoot = beaconObject.transform;
        _beaconRoot.SetParent(_resolvedExitRoot != null ? _resolvedExitRoot : transform, false);
        _beaconRoot.position = _resolvedAnchor.position + Vector3.up * beaconVerticalOffset;
        _beaconRoot.rotation = Quaternion.identity;
        _beaconRoot.localScale = new Vector3(beaconWidth, beaconHeight * 0.5f, beaconWidth);

        Renderer renderer = beaconObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;

            Material material = renderer.material;
            if (material != null)
            {
                material.color = beaconColor;
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", beaconColor * 0.35f);
            }
        }

        SetBeaconVisible(false);
    }

    void SetBeaconVisible(bool visible)
    {
        if (_beaconRoot == null)
            return;

        if (_beaconRoot.gameObject.activeSelf != visible)
            _beaconRoot.gameObject.SetActive(visible);
    }

    static bool TryGetCombinedBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (initialized)
            return true;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return initialized;
    }
}
