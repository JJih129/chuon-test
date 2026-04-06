using UnityEngine;

public class UltimateGaugeUIAdapter : MonoBehaviour
{
    [Header("Source Controller")]
    public PlayerUltimateController source;

    float _lastRatio = -1f;
    bool _lastReady;
    bool _subscribed;

    void Awake()
    {
        ResolveSource();
        SyncImmediate();
    }

    void OnEnable()
    {
        ResolveSource();
        Subscribe();
        SyncImmediate();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void ResolveSource()
    {
        if (source == null)
            source = FindFirstObjectByType<PlayerUltimateController>();
    }

    void Subscribe()
    {
        if (_subscribed || source == null)
            return;

        source.OnGaugeChanged += HandleGaugeChanged;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed || source == null)
            return;

        source.OnGaugeChanged -= HandleGaugeChanged;
        _subscribed = false;
    }

    void HandleGaugeChanged(float gauge, float normalized, bool ready)
    {
        if (Mathf.Abs(normalized - _lastRatio) > 0.001f)
        {
            _lastRatio = normalized;
            UI_UltimateGauge.UpdateValue(normalized);
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

        HandleGaugeChanged(
            source.Gauge,
            source.gaugeMax > 0f ? Mathf.Clamp01(source.Gauge / source.gaugeMax) : 0f,
            source.IsGaugeReady);
    }
}
