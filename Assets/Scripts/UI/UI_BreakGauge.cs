using UnityEngine;
using UnityEngine.UI;

public class UI_BreakGauge : MonoBehaviour
{
    [Header("Source Boss")]
    public BossBreakController boss;

    [Header("Fill Image")]
    public Image fill;

    [Header("Visibility")]
    public CanvasGroup group;
    public bool alwaysOn = true;

    float _lastFill = -1f;
    float _lastAlpha = -1f;

    void Update()
    {
        if (!boss || !fill) return;

        float nextFill = boss.Get01();
        if (!Mathf.Approximately(_lastFill, nextFill))
        {
            fill.fillAmount = nextFill;
            _lastFill = nextFill;
        }

        if (group && !alwaysOn)
        {
            float nextAlpha = boss.gameObject.activeInHierarchy ? 1f : 0f;
            if (!Mathf.Approximately(_lastAlpha, nextAlpha))
            {
                group.alpha = nextAlpha;
                _lastAlpha = nextAlpha;
            }
        }
    }
}
