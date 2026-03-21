using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BossUIController : MonoBehaviour
{
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

    IHealth boundHealth;
    float showSqr;
    float hideSqr;
    bool isVisible;
    Coroutine pollRoutine;
    Coroutine pendingShowRoutine;
    Coroutine pendingHideRoutine;

    void Awake()
    {
        AutoResolveReferences();
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
        if (pollRoutine != null)
            StopCoroutine(pollRoutine);
        pollRoutine = StartCoroutine(Poll());
    }

    void OnDisable()
    {
        if (pollRoutine != null)
            StopCoroutine(pollRoutine);
        pollRoutine = null;

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
        float baseInterval = Mathf.Max(0.5f, pollInterval);
        if (player == null)
            return Mathf.Max(baseInterval, 1f);

        float sqr = (player.position - transform.position).sqrMagnitude;
        if (!isVisible && sqr > hideSqr)
            return Mathf.Max(baseInterval, 0.9f);

        return baseInterval;
    }

    void Evaluate()
    {
        if (player == null)
        {
            AutoResolveReferences();
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

        if (player == null)
            yield break;

        if ((player.position - transform.position).sqrMagnitude <= showSqr)
            ShowHUD();
    }

    IEnumerator DelayedHide(float delay)
    {
        yield return new WaitForSeconds(delay);
        pendingHideRoutine = null;

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
        if (isVisible)
            return;

        NormalizeTopHudRootScale();
        if (hpHud == null || topHudRoot == null)
            return;

        if (boundHealth == null)
            boundHealth = healthBehaviour as IHealth;

        if (boundHealth != null)
            hpHud.Bind(boundHealth);

        if (breakHud != null && breakHud.hudRoot != null && !breakHud.hudRoot.activeSelf)
            breakHud.hudRoot.SetActive(true);

        if (!topHudRoot.activeSelf)
            topHudRoot.SetActive(true);

        isVisible = true;
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

    void AutoResolveReferences()
    {
        if (player == null)
        {
            PlayerLockOn playerLockOn = FindFirstObjectByType<PlayerLockOn>();
            if (playerLockOn != null)
                player = playerLockOn.transform;
            else
            {
                PlayerReferences playerReferences = FindFirstObjectByType<PlayerReferences>();
                if (playerReferences != null)
                    player = playerReferences.transform;
            }
        }

        if (topHudRoot == null && hpHud != null)
            topHudRoot = hpHud.transform.root.gameObject;
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
}
