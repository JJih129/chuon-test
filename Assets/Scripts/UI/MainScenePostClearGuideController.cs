using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class MainScenePostClearGuideController : MonoBehaviour
{
    [SerializeField] private MainSceneArrivalController arrivalController;
    [SerializeField] private PlayerHUD playerHud;
    [SerializeField] private BossHealth bossHealth;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private MainSceneClearExitBridge clearExitBridge;
    [SerializeField] private BaseInteractable explicitGuideInteractable;
    [SerializeField] private Transform explicitGuideTarget;

    [Header("Guide Cue")]
    [SerializeField] private string interactableGuideMessage = "\uacbd\ub85c \ud655\ubcf4. \ub2e4\uc74c \uc9c0\uc810\uc5d0 \uc811\uadfc\ud574";
    [SerializeField] private string sceneLoadGuideMessage = "\ucd9c\uad6c\ub97c \ud655\uc778\ud574. \uc0c1\ud638\uc791\uc6a9\uc73c\ub85c \uc804\uc9c4\ud574";
    [SerializeField] private string movePointGuideMessage = "\uc774\ub3d9 \ud3ec\uc778\ud2b8\ub85c \uc811\uadfc\ud574";
    [SerializeField] private string fallbackGuideMessage = "\uc804\ud22c \uc885\ub8cc. \uc8fc\ubcc0\uc744 \uc815\ube44\ud558\uace0 \uc804\uc9c4\ud574";
    [SerializeField] private AttackTelegraphType guideTelegraphType = AttackTelegraphType.Guard;
    [SerializeField, Min(0.1f)] private float guideDelay = 1.45f;
    [SerializeField, Min(0.1f)] private float guideHold = 0.84f;
    [SerializeField] private int guideCuePriority = 12;

    [Header("Fallback Search")]
    [SerializeField] private string[] fallbackTargetNames = { "Elevator", "closedoor (1)", "opendoor (1)" };

    [Header("Highlight Pulse")]
    [SerializeField, Min(1)] private int highlightPulseCount = 2;
    [SerializeField, Min(0.05f)] private float highlightPulseOnDuration = 0.22f;
    [SerializeField, Min(0.05f)] private float highlightPulseOffDuration = 0.12f;

    [Header("World Beacon")]
    [SerializeField] private bool showWorldBeacon = true;
    [SerializeField, Min(0.5f)] private float beaconHeight = 2.6f;
    [SerializeField, Min(0.05f)] private float beaconWidth = 0.22f;
    [SerializeField, Min(0.2f)] private float beaconHoldDuration = 6f;
    [SerializeField, Min(0.1f)] private float beaconArrivalRadius = 2.2f;
    [SerializeField, Min(0.05f)] private float beaconCheckInterval = 0.12f;
    [SerializeField] private Color beaconColor = new Color(0.36f, 0.92f, 1f, 0.92f);

    bool _fromLobbyTransition;
    bool _subscribed;
    bool _guideShown;
    Coroutine _guideRoutine;
    Coroutine _beaconRoutine;
    InteractionOutlineRenderer _activeOutline;
    Transform _beaconRoot;
    Renderer _beaconRenderer;

    public void ConfigureRuntime(
        MainSceneArrivalController runtimeArrivalController,
        PlayerHUD runtimeHud,
        BossHealth runtimeBossHealth,
        Transform runtimePlayerRoot,
        MainSceneClearExitBridge runtimeClearExitBridge = null)
    {
        arrivalController = runtimeArrivalController;
        playerHud = runtimeHud;
        bossHealth = runtimeBossHealth;
        playerRoot = runtimePlayerRoot;
        clearExitBridge = runtimeClearExitBridge;

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
        StopGuideRoutine();
        StopBeaconRoutine();
        SetBeaconVisible(false);
        SetOutlineActive(false);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
        StopGuideRoutine();
        StopBeaconRoutine();
        SetBeaconVisible(false);
        SetOutlineActive(false);
    }

    void ResolveReferences()
    {
        if (arrivalController == null)
            arrivalController = GetComponent<MainSceneArrivalController>();
        if (playerHud == null)
            playerHud = FindObjectOfType<PlayerHUD>(true);
        if (bossHealth == null)
            bossHealth = FindObjectOfType<BossHealth>(true);
        if (clearExitBridge == null)
            clearExitBridge = GetComponent<MainSceneClearExitBridge>();

        if (playerRoot == null)
        {
            PlayerHealth playerHealth = FindObjectOfType<PlayerHealth>(true);
            if (playerHealth != null)
                playerRoot = playerHealth.transform;
        }

        if (playerRoot == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                playerRoot = playerObject.transform;
        }
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (!_fromLobbyTransition || bossHealth == null || bossHealth.IsDead || _guideShown)
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
        if (_guideShown)
            return;

        _guideShown = true;
        ReleaseSubscriptions();
        StopGuideRoutine();
        _guideRoutine = StartCoroutine(CoShowPostClearGuide());
    }

    IEnumerator CoShowPostClearGuide()
    {
        if (guideDelay > 0f)
            yield return new WaitForSecondsRealtime(guideDelay);

        BaseInteractable guideInteractable = ResolveGuideInteractable();
        Transform guideTarget = ResolveGuideTarget(guideInteractable);
        InteractionOutlineRenderer outline = ResolveGuideOutline(guideInteractable);

        if (playerHud != null)
            playerHud.ShowRuntimeTelegraphMessage(ResolveGuideMessage(guideInteractable), guideTelegraphType, guideHold, false, guideCuePriority);

        if (showWorldBeacon && guideTarget != null)
        {
            StopBeaconRoutine();
            _beaconRoutine = StartCoroutine(CoShowBeacon(guideTarget));
        }

        if (outline != null)
            yield return CoPulseOutline(outline);

        _guideRoutine = null;
    }

    BaseInteractable ResolveGuideInteractable()
    {
        if (explicitGuideInteractable != null)
            return explicitGuideInteractable;

        if (clearExitBridge != null && clearExitBridge.ActiveExitInteractable != null)
            return clearExitBridge.ActiveExitInteractable;

        BaseInteractable[] interactables = FindObjectsOfType<BaseInteractable>(true);
        if (interactables == null || interactables.Length == 0)
            return null;

        Vector3 origin = playerRoot != null ? playerRoot.position : Vector3.zero;
        BaseInteractable best = null;
        int bestPriority = int.MaxValue;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < interactables.Length; i++)
        {
            BaseInteractable candidate = interactables[i];
            if (candidate == null)
                continue;

            int priority = ResolvePriority(candidate);
            float distance = playerRoot != null
                ? (candidate.transform.position - origin).sqrMagnitude
                : 0f;

            if (priority > bestPriority)
                continue;

            if (priority == bestPriority && distance >= bestDistance)
                continue;

            best = candidate;
            bestPriority = priority;
            bestDistance = distance;
        }

        return best;
    }

    InteractionOutlineRenderer ResolveGuideOutline(BaseInteractable guideInteractable)
    {
        if (guideInteractable != null)
        {
            if (guideInteractable.highlight != null)
                return guideInteractable.highlight;

            InteractionOutlineRenderer interactableOutline = guideInteractable.GetComponentInChildren<InteractionOutlineRenderer>(true);
            if (interactableOutline != null)
                return interactableOutline;
        }

        Transform fallbackTarget = ResolveFallbackTarget();
        if (fallbackTarget == null)
            return null;

        return fallbackTarget.GetComponentInChildren<InteractionOutlineRenderer>(true);
    }

    Transform ResolveGuideTarget(BaseInteractable guideInteractable)
    {
        if (guideInteractable != null)
            return guideInteractable.anchor != null ? guideInteractable.anchor : guideInteractable.transform;

        if (clearExitBridge != null && clearExitBridge.ActiveExitTarget != null)
            return clearExitBridge.ActiveExitTarget;

        return ResolveFallbackTarget();
    }

    Transform ResolveFallbackTarget()
    {
        if (explicitGuideTarget != null)
            return explicitGuideTarget;

        if (fallbackTargetNames == null || fallbackTargetNames.Length == 0)
            return null;

        Transform[] transforms = FindObjectsOfType<Transform>(true);
        if (transforms == null || transforms.Length == 0)
            return null;

        Vector3 origin = playerRoot != null ? playerRoot.position : Vector3.zero;
        Transform best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null)
                continue;

            if (!MatchesFallbackName(candidate.name))
                continue;

            float distance = playerRoot != null
                ? (candidate.position - origin).sqrMagnitude
                : 0f;

            if (distance >= bestDistance)
                continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    string ResolveGuideMessage(BaseInteractable guideInteractable)
    {
        if (guideInteractable is SceneLoadInteractable)
            return sceneLoadGuideMessage;

        if (guideInteractable is MoveToPointInteractable)
            return movePointGuideMessage;

        if (guideInteractable != null)
            return interactableGuideMessage;

        return fallbackGuideMessage;
    }

    IEnumerator CoPulseOutline(InteractionOutlineRenderer outline)
    {
        _activeOutline = outline;
        int pulseCount = Mathf.Max(1, highlightPulseCount);
        float onDuration = Mathf.Max(0.05f, highlightPulseOnDuration);
        float offDuration = Mathf.Max(0.05f, highlightPulseOffDuration);

        for (int i = 0; i < pulseCount; i++)
        {
            SetOutlineActive(true);
            yield return new WaitForSecondsRealtime(onDuration);
            SetOutlineActive(false);

            if (i < pulseCount - 1)
                yield return new WaitForSecondsRealtime(offDuration);
        }
    }

    void SetOutlineActive(bool active)
    {
        if (_activeOutline == null)
            return;

        _activeOutline.SetActive(active);
    }

    void StopGuideRoutine()
    {
        if (_guideRoutine == null)
            return;

        StopCoroutine(_guideRoutine);
        _guideRoutine = null;
    }

    IEnumerator CoShowBeacon(Transform target)
    {
        EnsureBeaconVisuals();
        if (_beaconRoot == null || target == null)
            yield break;

        float endTime = Time.unscaledTime + Mathf.Max(0.2f, beaconHoldDuration);
        float arrivalRadiusSqr = beaconArrivalRadius * beaconArrivalRadius;
        float checkInterval = Mathf.Max(0.05f, beaconCheckInterval);

        SetBeaconVisible(true);
        UpdateBeaconTransform(target);

        while (Time.unscaledTime < endTime)
        {
            if (target == null)
                break;

            UpdateBeaconTransform(target);

            if (playerRoot != null)
            {
                Vector3 offset = target.position - playerRoot.position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= arrivalRadiusSqr)
                    break;
            }

            yield return new WaitForSecondsRealtime(checkInterval);
        }

        SetBeaconVisible(false);
        _beaconRoutine = null;
    }

    void EnsureBeaconVisuals()
    {
        if (_beaconRoot != null)
            return;

        GameObject beaconObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beaconObject.name = "PostClearGuideBeacon";
        beaconObject.hideFlags = HideFlags.DontSave;
        Destroy(beaconObject.GetComponent<Collider>());

        _beaconRoot = beaconObject.transform;
        _beaconRoot.SetParent(transform, false);
        _beaconRoot.localScale = new Vector3(beaconWidth, beaconHeight * 0.5f, beaconWidth);

        _beaconRenderer = beaconObject.GetComponent<Renderer>();
        if (_beaconRenderer != null)
        {
            _beaconRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _beaconRenderer.receiveShadows = false;
            _beaconRenderer.lightProbeUsage = LightProbeUsage.Off;
            _beaconRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _beaconRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _beaconRenderer.allowOcclusionWhenDynamic = false;

            Material material = _beaconRenderer.material;
            if (material != null)
            {
                material.color = beaconColor;
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", beaconColor * 0.45f);
            }
        }

        SetBeaconVisible(false);
    }

    void UpdateBeaconTransform(Transform target)
    {
        if (_beaconRoot == null || target == null)
            return;

        Vector3 position = target.position;
        position.y += beaconHeight * 0.5f;
        _beaconRoot.position = position;
        _beaconRoot.localScale = new Vector3(beaconWidth, beaconHeight * 0.5f, beaconWidth);
    }

    void SetBeaconVisible(bool visible)
    {
        if (_beaconRoot == null)
            return;

        if (_beaconRoot.gameObject.activeSelf != visible)
            _beaconRoot.gameObject.SetActive(visible);
    }

    void StopBeaconRoutine()
    {
        if (_beaconRoutine == null)
            return;

        StopCoroutine(_beaconRoutine);
        _beaconRoutine = null;
    }

    bool MatchesFallbackName(string candidateName)
    {
        if (string.IsNullOrWhiteSpace(candidateName) || fallbackTargetNames == null)
            return false;

        for (int i = 0; i < fallbackTargetNames.Length; i++)
        {
            string fallbackName = fallbackTargetNames[i];
            if (string.IsNullOrWhiteSpace(fallbackName))
                continue;

            if (string.Equals(candidateName, fallbackName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static int ResolvePriority(BaseInteractable interactable)
    {
        if (interactable is SceneLoadInteractable)
            return 0;

        if (interactable is MoveToPointInteractable)
            return 1;

        return 2;
    }
}
