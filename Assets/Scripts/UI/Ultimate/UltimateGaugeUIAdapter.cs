using UnityEngine;

// [UI 어댑터] - PlayerUltimateController의 게이지를 UI로 전달
public class UltimateGaugeUIAdapter : MonoBehaviour
{
    [Header("소스 컨트롤러")]
    public PlayerUltimateController source;

    void Update()
    {
        if (source == null) return;
        float ratio = source.Gauge / Mathf.Max(1f, source.gaugeMax);
        UI_UltimateGauge.UpdateValue(ratio);
        UI_UltimateGauge.SetReady(ratio >= 1f - 0.0001f);
    }
}
