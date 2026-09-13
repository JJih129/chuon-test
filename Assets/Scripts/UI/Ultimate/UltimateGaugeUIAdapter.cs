using UnityEngine;

public class UltimateGaugeUIAdapter : MonoBehaviour
{
    [Header("Source Controller")]
    public PlayerUltimateController source;

    float _lastRatio = -1f;
    bool _lastReady;
    bool _hasLastState;

    void Update()
    {
        if (source == null) return;

        float ratio = source.Gauge / Mathf.Max(1f, source.gaugeMax);
        bool ready = ratio >= 1f - 0.0001f;

        if (_hasLastState && Mathf.Approximately(_lastRatio, ratio) && _lastReady == ready)
            return;

        UI_UltimateGauge.UpdateValue(ratio);
        UI_UltimateGauge.SetReady(ready);

        _lastRatio = ratio;
        _lastReady = ready;
        _hasLastState = true;
    }
}
