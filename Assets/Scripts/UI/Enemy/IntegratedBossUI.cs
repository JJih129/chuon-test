// (원본을 크게 변경하지 않고 안전성만 보강)
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IntegratedBossUI : MonoBehaviour
{
    struct CandidateEntry
    {
        public ProximityBossUI comp;
        public float sqr;

        public CandidateEntry(ProximityBossUI comp, float sqr)
        {
            this.comp = comp;
            this.sqr = sqr;
        }
    }

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
    List<ProximityBossUI> registered = new List<ProximityBossUI>();
    Dictionary<ProximityBossUI, EnemyHPBar> activeBars = new Dictionary<ProximityBossUI, EnemyHPBar>();
    readonly List<CandidateEntry> evaluationBuffer = new List<CandidateEntry>(8);

    ILockOnController lockOnController;
    float showSqr;
    float hideSqr;
    ProximityBossUI currentTopBoss;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;

        if (mainCamera == null) mainCamera = Camera.main;
        lockOnController = FindObjectOfType<SimpleLockOnController>() as ILockOnController;

        showSqr = showDistance * showDistance;
        float hideDist = showDistance + Mathf.Max(0f, hideHysteresis);
        hideSqr = hideDist * hideDist;

        if (topBossHUDRoot != null) topBossHUDRoot.SetActive(false);

        StartCoroutine(PollRoutine());
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public static void Register(ProximityBossUI comp)
    {
        if (_instance == null) return;
        if (!_instance.registered.Contains(comp)) _instance.registered.Add(comp);
    }
    public static void Unregister(ProximityBossUI comp)
    {
        if (_instance == null) return;
        _instance.registered.Remove(comp);
        if (_instance.currentTopBoss == comp)
            _instance.UnbindTopHUD();
        if (_instance.activeBars.TryGetValue(comp, out var bar))
        {
            if (_instance.hpBarPool != null)
            {
                bar.ResetForPool();
                _instance.hpBarPool.Return(bar);
            }
            _instance.activeBars.Remove(comp);
        }
    }

    IEnumerator PollRoutine()
    {
        var wait = (pollInterval > 0f) ? new WaitForSeconds(pollInterval) : null;
        while (true)
        {
            EvaluateAll();
            if (wait != null) yield return wait; else yield return null;
        }
    }

    void EvaluateAll()
    {
        if (player == null) return;

        evaluationBuffer.Clear();
        foreach (var c in registered)
        {
            if (c == null) continue;
            float sqr = (player.position - c.transform.position).sqrMagnitude;
            evaluationBuffer.Add(new CandidateEntry(c, sqr));
        }
        evaluationBuffer.Sort((a, b) => a.sqr.CompareTo(b.sqr));

        Transform currentLock = lockOnController != null ? lockOnController.GetCurrentTarget() : null;

        int shownWorld = 0;
        var toShowTop = evaluationBuffer.Count > 0 ? evaluationBuffer[0].comp : null;

        for (int i = 0; i < evaluationBuffer.Count; i++)
        {
            var entry = evaluationBuffer[i];
            if (entry.comp == null) continue;

            bool lockok = true;
            if (requireLockOn && currentLock != entry.comp.transform) lockok = false;

            bool withinShow = entry.sqr <= showSqr;
            bool withinHide = entry.sqr <= hideSqr;

            bool shouldShowWorld = lockok && withinShow && shownWorld < maxVisibleWorldBars;

            if (shouldShowWorld)
            {
                ShowWorldBar(entry.comp);
                shownWorld++;
            }
            else
            {
                if (activeBars.ContainsKey(entry.comp) && withinHide && lockok)
                {
                    // Keep the current bar active.
                }
                else
                {
                    HideWorldBar(entry.comp);
                }
            }
        }

        if (toShowTop != null)
        {
            bool lockok = true;
            if (requireLockOn && currentLock != toShowTop.transform) lockok = false;
            float dSqr = (player.position - toShowTop.transform.position).sqrMagnitude;
            if (lockok && dSqr <= showSqr)
            {
                BindTopHUD(toShowTop);
                return;
            }
        }
        UnbindTopHUD();
    }

    void ShowWorldBar(ProximityBossUI comp)
    {
        if (comp == null || activeBars.ContainsKey(comp)) return;
        if (hpBarPool == null) return;

        var bar = hpBarPool.Get();
        var ih = comp.healthBehaviour as IHealth;
        if (ih != null)
        {
            bar.Bind(ih);
        }
        else
        {
            // 바인딩 실패 시 안전하게 풀에 반환
            bar.ResetForPool();
            hpBarPool.Return(bar);
            return;
        }
        bar.target = comp.pivot ? comp.pivot : comp.transform;
        bar.offset = comp.worldOffset;
        bar.Show();
        activeBars[comp] = bar;
    }

    void HideWorldBar(ProximityBossUI comp)
    {
        if (comp == null) return;
        if (!activeBars.ContainsKey(comp)) return;
        var bar = activeBars[comp];
        hpBarPool?.Return(bar);
        activeBars.Remove(comp);
    }

    void BindTopHUD(ProximityBossUI comp)
    {
        if (topBossHUD == null || topBossHUDRoot == null) return;
        if (comp == currentTopBoss)
        {
            if (!topBossHUDRoot.activeSelf) topBossHUDRoot.SetActive(true);
            return;
        }

        var ih = comp.healthBehaviour as IHealth;
        if (ih == null) return;

        if (currentTopBoss != null)
            topBossHUD.Unbind();

        topBossHUD.Bind(ih);
        topBossHUDRoot.SetActive(true);
        currentTopBoss = comp;
    }

    void UnbindTopHUD()
    {
        if (topBossHUD == null || topBossHUDRoot == null) return;
        if (currentTopBoss != null)
        {
            topBossHUD.Unbind();
            currentTopBoss = null;
        }
        if (topBossHUDRoot.activeSelf) topBossHUDRoot.SetActive(false);
    }
}
