// 파일명: UI_BreakGauge.cs
// 역할: 보스 브레이크 게이지 UI(게이지 바/텍스트) 표시
using UnityEngine;
using UnityEngine.UI;

public class UI_BreakGauge : MonoBehaviour
{
    [Header("표시 대상 보스(브레이크 컨트롤러)")]
    public BossBreakController boss;

    [Header("게이지 이미지(0~1 Fill)")]
    public Image fill;

    [Header("표시 토글(보스 근접 시만 표시 등)")]
    public CanvasGroup group;
    public bool alwaysOn = true;
    float _lastFill = -1f;
    float _nextRefreshAt;
    const float RefreshInterval = 0.15f;

    void Update()
    {
        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshInterval;

        if (!boss || !fill) return;

        float nextFill = boss.Get01();
        if (!Mathf.Approximately(_lastFill, nextFill))
        {
            _lastFill = nextFill;
            fill.fillAmount = nextFill;
        }

        if (group && !alwaysOn)
        {
            group.alpha = (boss.gameObject.activeInHierarchy ? 1f : 0f);
        }
    }
}
