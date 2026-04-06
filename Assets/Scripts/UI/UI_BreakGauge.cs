// 파일명: UI_BreakGauge.cs
// 역할: 보스 브레이크 게이지 UI(게이지 바/텍스트) 표시
using UnityEngine;
using UnityEngine.UI;

public class UI_BreakGauge : MonoBehaviour
{
    const float ResolveInterval = 0.5f;
    const float VisibilityRefreshInterval = 0.15f;

    [Header("표시 대상 보스(브레이크 컨트롤러)")]
    public BossBreakController boss;

    [Header("게이지 이미지(0~1 Fill)")]
    public Image fill;

    [Header("표시 토글(보스 근접 시만 표시 등)")]
    public CanvasGroup group;
    public bool alwaysOn = true;
    float _lastFill = -1f;
    float _nextRefreshAt;
    float _nextResolveAt;

    void OnEnable()
    {
        TryResolveBoss(true);
        BindIfNeeded();
        SyncFillImmediate();
        RefreshExecutionState();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Update()
    {
        if (!boss)
        {
            if (Time.unscaledTime >= _nextResolveAt)
            {
                _nextResolveAt = Time.unscaledTime + ResolveInterval;
                TryResolveBoss();
                BindIfNeeded();
                SyncFillImmediate();
            }

            RefreshExecutionState();
            return;
        }

        if (group && !alwaysOn)
        {
            if (Time.unscaledTime < _nextRefreshAt)
                return;

            _nextRefreshAt = Time.unscaledTime + VisibilityRefreshInterval;
            group.alpha = (boss.gameObject.activeInHierarchy ? 1f : 0f);
        }

        RefreshExecutionState();
    }

    void TryResolveBoss(bool force = false)
    {
        if (!force && boss)
            return;

        boss = GameplaySceneCache.ResolveBossBreakController();
    }

    void BindIfNeeded()
    {
        if (!boss)
            return;

        boss.OnBreakChanged -= HandleBreakChanged;
        boss.OnBreakChanged += HandleBreakChanged;
    }

    void Unbind()
    {
        if (!boss)
            return;

        boss.OnBreakChanged -= HandleBreakChanged;
    }

    void SyncFillImmediate()
    {
        if (!boss || !fill)
            return;

        float nextFill = boss.Get01();
        _lastFill = nextFill;
        fill.fillAmount = nextFill;
    }

    void HandleBreakChanged(float normalized, float current)
    {
        if (!fill)
            return;

        _lastFill = normalized;
        fill.fillAmount = normalized;
        if (!enabled)
            RefreshExecutionState();
    }

    void RefreshExecutionState()
    {
        bool shouldRun = !boss || (group && !alwaysOn);
        if (enabled != shouldRun)
            enabled = shouldRun;
    }
}
