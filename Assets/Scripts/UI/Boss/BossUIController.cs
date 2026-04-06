using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BossUIController : MonoBehaviour
{
    const float AutoResolveInterval = 0.5f;

    [Header("References")]
    [Tooltip("Player transform used for distance checks.")]
    public Transform player;

    [Tooltip("Root object for the top boss HUD.")]
    public GameObject topHudRoot;

    [Tooltip("Top HP HUD component.")]
    public BossHUD hpHud;

    [Tooltip("Break HUD component.")]
    public BossBreakHUD breakHud;

    [Tooltip("Boss health component implementing IHealth.")]
    public MonoBehaviour healthBehaviour;

    [Tooltip("Boss break controller.")]
    public BossBreakController breakController;

    [Header("Presentation")]
    [Tooltip("Optional ultimate controller. Auto-resolved when empty.")]
    public PlayerUltimateController playerUltimateController;

    [Tooltip("Keep the boss HUD visible while the player's ultimate cinematic is active.")]
    public bool keepVisibleDuringUltimate = true;

    [Header("Distance / Timing")]
    [Tooltip("Maximum distance to show the boss HUD.")]
    public float showDistance = 18f;

    [Tooltip("Extra distance before hiding once shown.")]
    public float hideHysteresis = 2f;

    [Tooltip("Distance check interval in seconds. Set 0 to check every frame.")]
    public float pollInterval = 0.12f;

    [Tooltip("Delay before showing after entering range.")]
    public float showDelay = 0.05f;

    [Tooltip("Delay before hiding after leaving range.")]
    public float hideDelay = 0.12f;

    [Tooltip("Keep the boss HUD visible for the whole fight instead of hiding by distance.")]
    public bool alwaysVisibleWhileBossAlive = true;

    IHealth boundHealth;
    float showSqr;
    float hideSqr;
    bool isVisible;
    Coroutine pollRoutine;
    Coroutine pendingShowRoutine;
    Coroutine pendingHideRoutine;
    bool _presentationSubscribed;
    float _nextAutoResolveAt;

    void Awake()
    {
        TryAutoResolveReferences(true);
        NormalizeTopHudRootScale();

        showSqr = showDistance * showDistance;
        float hideDistance = showDistance + Mathf.Max(0f, hideHysteresis);
        hideSqr = hideDistance * hideDistance;

        if (topHudRoot != null)
            topHudRoot.SetActive(false);

        boundHealth = healthBehaviour as IHealth;
        if (breakController == null)
            breakController = GetComponent<BossBreakController>();
        if (hpHud == null && topHudRoot != null)
            hpHud = topHudRoot.GetComponentInChildren<BossHUD>(true);
        if (breakHud == null && topHudRoot != null)
            breakHud = topHudRoot.GetComponentInChildren<BossBreakHUD>(true);

        if (breakHud != null && breakController != null)
        {
            if (breakHud.hudRoot == topHudRoot)
            {
                breakHud.showOnBreakEnter = false;
                breakHud.hideOnBreakExit = false;
            }

            breakHud.BindBreakController(breakController);
        }

        if (boundHealth == null && healthBehaviour != null)
            Debug.LogWarning($"[BossUIController] healthBehaviour does not implement IHealth: {healthBehaviour.GetType().Name}", this);
    }

    void OnEnable()
    {
        TryAutoResolveReferences(true);
        RefreshPresentationSubscriptions();
        if (pollRoutine != null)
            StopCoroutine(pollRoutine);
        pollRoutine = StartCoroutine(Poll());
    }

    void OnDisable()
    {
        if (pollRoutine != null)
            StopCoroutine(pollRoutine);
        pollRoutine = null;

        ReleasePresentationSubscriptions();
        CancelPendingShow();
        CancelPendingHide();
        ForceHideImmediate();
    }

    IEnumerator Poll()
    {
        while (true)
        {
            Evaluate();
            float effectivePollInterval = ResolvePollInterval();
            if (effectivePollInterval > 0f)
                yield return new WaitForSeconds(effectivePollInterval);
            else
                yield return null;
        }
    }

    float ResolvePollInterval()
    {
        if (IsUltimateHudOverrideActive())
            return 0f;

        if (alwaysVisibleWhileBossAlive && IsBossAlive())
            return Mathf.Max(0.05f, pollInterval);

        float baseInterval = Mathf.Max(0.05f, pollInterval);
        if (player == null)
            return Mathf.Max(baseInterval, 1f);

        float sqr = (player.position - transform.position).sqrMagnitude;
        if (!isVisible && sqr > hideSqr)
            return Mathf.Max(baseInterval, 0.9f);

        return baseInterval;
    }

    void Evaluate()
    {
        if (keepVisibleDuringUltimate && playerUltimateController == null)
        {
            TryAutoResolveReferences();
            RefreshPresentationSubscriptions();
        }

        if (IsUltimateHudOverrideActive())
        {
            CancelPendingShow();
            CancelPendingHide();
            ShowHUD();
            return;
        }

        if (alwaysVisibleWhileBossAlive && IsBossAlive())
        {
            CancelPendingShow();
            CancelPendingHide();
            ShowHUD();
            return;
        }

        if (player == null)
        {
            TryAutoResolveReferences();
            if (player == null)
                return;
        }

        float sqr = (player.position - transform.position).sqrMagnitude;

        if (!isVisible && sqr <= showSqr)
        {
            CancelPendingHide();
            if (showDelay <= 0f)
                ShowHUD();
            else if (pendingShowRoutine == null)
                pendingShowRoutine = StartCoroutine(DelayedShow(showDelay));
            return;
        }

        if (isVisible && sqr > hideSqr)
        {
            CancelPendingShow();
            if (hideDelay <= 0f)
                HideHUD();
            else if (pendingHideRoutine == null)
                pendingHideRoutine = StartCoroutine(DelayedHide(hideDelay));
            return;
        }

        if (sqr > showSqr)
            CancelPendingShow();
        if (sqr <= hideSqr)
            CancelPendingHide();
    }

    IEnumerator DelayedShow(float delay)
    {
        yield return new WaitForSeconds(delay);
        pendingShowRoutine = null;

        if (IsUltimateHudOverrideActive())
        {
            ShowHUD();
            yield break;
        }

        if (player == null)
            yield break;

        if ((player.position - transform.position).sqrMagnitude <= showSqr)
            ShowHUD();
    }

    IEnumerator DelayedHide(float delay)
    {
        yield return new WaitForSeconds(delay);
        pendingHideRoutine = null;

        if (IsUltimateHudOverrideActive())
        {
            ShowHUD();
            yield break;
        }

        if (player == null)
        {
            HideHUD();
            yield break;
        }

        if ((player.position - transform.position).sqrMagnitude > hideSqr)
            HideHUD();
    }

    void ShowHUD()
    {
        NormalizeTopHudRootScale();
        if (hpHud == null || topHudRoot == null)
            return;

        if (boundHealth == null)
            boundHealth = healthBehaviour as IHealth;

        if (boundHealth != null)
            hpHud.Bind(boundHealth);
        else if (!hpHud.gameObject.activeSelf)
            hpHud.gameObject.SetActive(true);

        if (breakHud != null && breakHud.hudRoot != null && !breakHud.hudRoot.activeSelf)
            breakHud.hudRoot.SetActive(true);

        if (!topHudRoot.activeSelf)
            topHudRoot.SetActive(true);

        isVisible = topHudRoot.activeSelf;
    }

    void HideHUD()
    {
        if (!isVisible)
            return;

        if (hpHud != null)
            hpHud.Unbind();
        if (breakHud != null && breakHud.hudRoot != null && breakHud.hudRoot.activeSelf)
            breakHud.hudRoot.SetActive(false);
        if (topHudRoot != null && topHudRoot.activeSelf)
            topHudRoot.SetActive(false);

        isVisible = false;
    }

    public void ForceHideImmediate()
    {
        if (hpHud != null)
            hpHud.Unbind();
        if (breakHud != null && breakHud.hudRoot != null && breakHud.hudRoot.activeSelf)
            breakHud.hudRoot.SetActive(false);
        if (topHudRoot != null && topHudRoot.activeSelf)
            topHudRoot.SetActive(false);
        isVisible = false;
    }

    bool NeedsAutoResolve()
    {
        return player == null
            || (topHudRoot == null && hpHud != null)
            || playerUltimateController == null;
    }

    void TryAutoResolveReferences(bool force = false)
    {
        if (!force && !NeedsAutoResolve())
            return;

        if (!force && Time.unscaledTime < _nextAutoResolveAt)
            return;

        AutoResolveReferences();
        _nextAutoResolveAt = Time.unscaledTime + AutoResolveInterval;
    }

    void AutoResolveReferences()
    {
        if (player == null)
        {
            PlayerLockOn playerLockOn = GameplaySceneCache.ResolvePlayerLockOn();
            if (playerLockOn != null)
                player = playerLockOn.transform;
            else
            {
                PlayerReferences playerReferences = GameplaySceneCache.ResolvePlayerReferences();
                if (playerReferences != null)
                    player = playerReferences.transform;
            }
        }

        if (topHudRoot == null && hpHud != null)
            topHudRoot = hpHud.transform.root.gameObject;

        if (playerUltimateController == null)
            playerUltimateController = GameplaySceneCache.ResolvePlayerUltimateController();
    }

    void NormalizeTopHudRootScale()
    {
        if (topHudRoot == null)
            return;

        Transform hudTransform = topHudRoot.transform;
        if (hudTransform.localScale.sqrMagnitude < 0.0001f)
            hudTransform.localScale = Vector3.one;
    }

    void CancelPendingShow()
    {
        if (pendingShowRoutine == null)
            return;

        StopCoroutine(pendingShowRoutine);
        pendingShowRoutine = null;
    }

    void CancelPendingHide()
    {
        if (pendingHideRoutine == null)
            return;

        StopCoroutine(pendingHideRoutine);
        pendingHideRoutine = null;
    }

    void RefreshPresentationSubscriptions()
    {
        ReleasePresentationSubscriptions();

        if (!keepVisibleDuringUltimate)
            return;

        if (playerUltimateController == null)
            TryAutoResolveReferences();

        if (playerUltimateController == null)
            return;

        playerUltimateController.OnUltimateStarted += HandleUltimateStarted;
        playerUltimateController.OnUltimateEnded += HandleUltimateEnded;
        _presentationSubscribed = true;
    }

    void ReleasePresentationSubscriptions()
    {
        if (!_presentationSubscribed || playerUltimateController == null)
            return;

        playerUltimateController.OnUltimateStarted -= HandleUltimateStarted;
        playerUltimateController.OnUltimateEnded -= HandleUltimateEnded;
        _presentationSubscribed = false;
    }

    void HandleUltimateStarted()
    {
        if (!keepVisibleDuringUltimate)
            return;

        CancelPendingShow();
        CancelPendingHide();
        ShowHUD();
    }

    void HandleUltimateEnded()
    {
        if (!keepVisibleDuringUltimate)
            return;

        CancelPendingShow();
        CancelPendingHide();
        Evaluate();
    }

    bool IsUltimateHudOverrideActive()
    {
        return keepVisibleDuringUltimate
            && playerUltimateController != null
            && playerUltimateController.IsCinematic;
    }

    bool IsBossAlive()
    {
        if (boundHealth == null)
            boundHealth = healthBehaviour as IHealth;

        return boundHealth == null || boundHealth.CurrentHP > 0;
    }
}
