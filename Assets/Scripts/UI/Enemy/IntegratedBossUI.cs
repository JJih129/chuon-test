using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IntegratedBossUI : MonoBehaviour
{
    struct BossDistanceEntry
    {
        public ProximityBossUI comp;
        public float sqr;
    }

    static readonly Comparison<BossDistanceEntry> DistanceEntryComparer = CompareDistanceEntries;

    public Transform player;
    public Camera mainCamera;
    public EnemyHPBarPool hpBarPool;
    public GameObject topBossHUDRoot;
    public BossHUD topBossHUD;

    public float showDistance = 18f;
    public float hideHysteresis = 2f;
    public float pollInterval = 0.12f;
    public bool requireLockOn = false;
    public int maxVisibleWorldBars = 3;

    static IntegratedBossUI _instance;
    readonly List<ProximityBossUI> registered = new List<ProximityBossUI>();
    readonly Dictionary<ProximityBossUI, EnemyHPBar> activeBars = new Dictionary<ProximityBossUI, EnemyHPBar>();
    readonly List<BossDistanceEntry> distanceEntries = new List<BossDistanceEntry>(16);

    ILockOnController lockOnController;
    float showSqr;
    float hideSqr;
    ProximityBossUI currentTopBoss;
    IHealth currentTopBossHealth;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;

        if (mainCamera == null)
            mainCamera = Camera.main;
        ResolveLockOnController();

        showSqr = showDistance * showDistance;
        float hideDist = showDistance + Mathf.Max(0f, hideHysteresis);
        hideSqr = hideDist * hideDist;

        if (topBossHUDRoot != null)
            topBossHUDRoot.SetActive(false);

        StartCoroutine(PollRoutine());
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    public static void Register(ProximityBossUI comp)
    {
        if (_instance == null || comp == null)
            return;

        if (!_instance.registered.Contains(comp))
            _instance.registered.Add(comp);
    }

    public static void Unregister(ProximityBossUI comp)
    {
        if (_instance == null || comp == null)
            return;

        _instance.registered.Remove(comp);

        if (_instance.activeBars.TryGetValue(comp, out EnemyHPBar bar))
        {
            if (_instance.hpBarPool != null)
            {
                bar.ResetForPool();
                _instance.hpBarPool.Return(bar);
            }
            _instance.activeBars.Remove(comp);
        }

        if (_instance.currentTopBoss == comp)
            _instance.UnbindTopHUD();
    }

    IEnumerator PollRoutine()
    {
        while (true)
        {
            EvaluateAll();
            float effectivePollInterval = ResolvePollInterval();
            if (effectivePollInterval > 0f)
                yield return new WaitForSeconds(effectivePollInterval);
            else
                yield return null;
        }
    }

    float ResolvePollInterval()
    {
        float baseInterval = Mathf.Max(0.33f, pollInterval);
        if (player == null || registered.Count == 0)
            return Mathf.Max(baseInterval, 0.75f);

        if (currentTopBoss == null && activeBars.Count == 0)
            return Mathf.Max(baseInterval, 0.5f);

        return baseInterval;
    }

    void EvaluateAll()
    {
        if (player == null)
            return;

        if (lockOnController == null)
            ResolveLockOnController();

        distanceEntries.Clear();
        for (int i = 0; i < registered.Count; i++)
        {
            ProximityBossUI comp = registered[i];
            if (comp == null)
                continue;

            distanceEntries.Add(new BossDistanceEntry
            {
                comp = comp,
                sqr = (player.position - comp.transform.position).sqrMagnitude
            });
        }

        if (distanceEntries.Count > 1)
            distanceEntries.Sort(DistanceEntryComparer);

        Transform currentLock = lockOnController != null && lockOnController.IsLockedOn()
            ? lockOnController.GetCurrentTarget()
            : null;

        int shownWorld = 0;
        ProximityBossUI toShowTop = distanceEntries.Count > 0 ? distanceEntries[0].comp : null;

        for (int i = 0; i < distanceEntries.Count; i++)
        {
            BossDistanceEntry entry = distanceEntries[i];
            if (entry.comp == null)
                continue;

            bool lockOk = !requireLockOn || IsSameLockTarget(currentLock, entry.comp);
            bool withinShow = entry.sqr <= showSqr;
            bool withinHide = entry.sqr <= hideSqr;
            bool shouldShowWorld = lockOk && withinShow && shownWorld < maxVisibleWorldBars;

            if (shouldShowWorld)
            {
                ShowWorldBar(entry.comp);
                shownWorld++;
            }
            else if (!(activeBars.ContainsKey(entry.comp) && withinHide && lockOk))
            {
                HideWorldBar(entry.comp);
            }
        }

        if (toShowTop != null)
        {
            bool lockOk = !requireLockOn || IsSameLockTarget(currentLock, toShowTop);
            float sqr = (player.position - toShowTop.transform.position).sqrMagnitude;
            if (lockOk && sqr <= showSqr)
            {
                BindTopHUD(toShowTop);
                return;
            }
        }

        UnbindTopHUD();
    }

    void ResolveLockOnController()
    {
        if (player != null)
        {
            PlayerLockOn playerLockOn = player.GetComponent<PlayerLockOn>();
            if (playerLockOn != null)
            {
                lockOnController = playerLockOn;
                return;
            }

            lockOnController = player.GetComponent<ILockOnController>();
            if (lockOnController != null)
                return;
        }

        PlayerLockOn fallbackPlayerLockOn = FindFirstObjectByType<PlayerLockOn>();
        lockOnController = fallbackPlayerLockOn;
    }

    bool IsSameLockTarget(Transform currentLock, ProximityBossUI comp)
    {
        if (currentLock == null || comp == null)
            return false;

        if (currentLock == comp.transform)
            return true;

        if (comp.pivot != null && currentLock == comp.pivot)
            return true;

        return currentLock.root == comp.transform.root;
    }

    void ShowWorldBar(ProximityBossUI comp)
    {
        if (comp == null || activeBars.ContainsKey(comp) || hpBarPool == null)
            return;

        EnemyHPBar bar = hpBarPool.Get();
        IHealth health = comp.healthBehaviour as IHealth;
        if (health == null)
        {
            bar.ResetForPool();
            hpBarPool.Return(bar);
            return;
        }

        bar.Bind(health);
        bar.target = comp.pivot ? comp.pivot : comp.transform;
        bar.offset = comp.worldOffset;
        bar.Show();
        activeBars[comp] = bar;
    }

    void HideWorldBar(ProximityBossUI comp)
    {
        if (comp == null)
            return;

        if (!activeBars.TryGetValue(comp, out EnemyHPBar bar))
            return;

        hpBarPool?.Return(bar);
        activeBars.Remove(comp);
    }

    void BindTopHUD(ProximityBossUI comp)
    {
        if (topBossHUD == null || topBossHUDRoot == null || comp == null)
            return;

        IHealth health = comp.healthBehaviour as IHealth;
        if (health == null)
            return;

        if (ReferenceEquals(currentTopBoss, comp) && ReferenceEquals(currentTopBossHealth, health))
        {
            if (!topBossHUDRoot.activeSelf)
                topBossHUDRoot.SetActive(true);
            return;
        }

        currentTopBoss = comp;
        currentTopBossHealth = health;
        topBossHUD.Bind(health);
        if (!topBossHUDRoot.activeSelf)
            topBossHUDRoot.SetActive(true);
    }

    void UnbindTopHUD()
    {
        if (currentTopBoss == null && currentTopBossHealth == null)
            return;

        currentTopBoss = null;
        currentTopBossHealth = null;

        if (topBossHUD != null)
            topBossHUD.Unbind();
        if (topBossHUDRoot != null && topBossHUDRoot.activeSelf)
            topBossHUDRoot.SetActive(false);
    }

    static int CompareDistanceEntries(BossDistanceEntry a, BossDistanceEntry b)
    {
        return a.sqr.CompareTo(b.sqr);
    }
}
