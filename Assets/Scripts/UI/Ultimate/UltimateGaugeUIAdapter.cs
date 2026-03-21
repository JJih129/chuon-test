using UnityEngine;

public class UltimateGaugeUIAdapter : MonoBehaviour
{
    [Header("Source Controller")]
    public PlayerUltimateController source;

    float _lastRatio = -1f;
    bool _lastReady;
    float _nextRefreshAt;
    const float RefreshInterval = 1f / 10f;

    void Awake()
    {
        if (source == null)
            source = FindFirstObjectByType<PlayerUltimateController>();

        SyncImmediate();
        enabled = false;
    }

    void Update()
    {
        if (source == null)
            return;

        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshInterval;

        float ratio = source.Gauge / Mathf.Max(1f, source.gaugeMax);
        bool ready = ratio >= 1f - 0.0001f;

        if (Mathf.Abs(ratio - _lastRatio) > 0.001f)
        {
            _lastRatio = ratio;
            UI_UltimateGauge.UpdateValue(ratio);
        }

        if (ready != _lastReady)
        {
            _lastReady = ready;
            UI_UltimateGauge.SetReady(ready);
        }
    }

    void SyncImmediate()
    {
        if (source == null)
            return;

        float ratio = source.Gauge / Mathf.Max(1f, source.gaugeMax);
        bool ready = ratio >= 1f - 0.0001f;
        _lastRatio = ratio;
        _lastReady = ready;
        UI_UltimateGauge.UpdateValue(ratio);
        UI_UltimateGauge.SetReady(ready);
    }
}
