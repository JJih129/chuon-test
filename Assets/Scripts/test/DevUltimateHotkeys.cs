using UnityEngine;
using System.Reflection;

// [개발 핫키] 궁극기 게이지/연출 테스트용(컨트롤러 전용)
public class DevUltimateHotkeys : MonoBehaviour
{
    // ── 대상 ─────────────────────────────
    [Header("대상")]
    public PlayerUltimateController player; // 비워두면 자동탐색

    // ── 키 바인딩 ────────────────────────
    [Header("핫키")]
    public KeyCode addKey = KeyCode.G;  // +X%
    public KeyCode subKey = KeyCode.H;  // -X%
    public KeyCode fullKey = KeyCode.F;  // 100%
    public KeyCode clearKey = KeyCode.C;  // 0%
    public KeyCode activateKey = KeyCode.R;  // 발동 시도
    public KeyCode printKey = KeyCode.P;  // 현재% 로그

    // ── 수치 ─────────────────────────────
    [Header("증감량(%)")]
    [Range(1f, 50f)] public float stepPercent = 12.5f;

    // ── 리플렉션 캐시 ────────────────────
    MethodInfo mAddGauge;    // AddGauge(float/int)
    PropertyInfo pGauge;     // Gauge
    FieldInfo fGaugeMax;     // gaugeMax

    void Awake()
    {
        if (!player) player = FindObjectOfType<PlayerUltimateController>();
        if (player)
        {
            var t = player.GetType();
            mAddGauge = t.GetMethod("AddGauge", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            pGauge = t.GetProperty("Gauge", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fGaugeMax = t.GetField("gaugeMax", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(addKey)) AddPercent(+stepPercent);
        if (Input.GetKeyDown(subKey)) AddPercent(-stepPercent);
        if (Input.GetKeyDown(fullKey)) SetPercent(100f);
        if (Input.GetKeyDown(clearKey)) SetPercent(0f);
        if (Input.GetKeyDown(activateKey)) player?.TryActivate();
        if (Input.GetKeyDown(printKey)) Debug.Log($"[DEV] Ultimate Gauge = {GetPercent01() * 100f:0}%");
    }

    // ── 구현 ─────────────────────────────
    void AddPercent(float percent)
    {
        if (player == null || mAddGauge == null) return;

        // AddGauge가 %기반인 현재 구현과 호환 (float/int 모두 수용)
        var paramType = mAddGauge.GetParameters()[0].ParameterType;
        object arg = paramType == typeof(int) ? (object)Mathf.RoundToInt(percent) : (object)percent;
        mAddGauge.Invoke(player, new object[] { arg });

        // UI 동기화
        float r01 = GetPercent01();
        UI_UltimateGauge.UpdateValue(r01);
        UI_UltimateGauge.SetReady(r01 >= 0.999f);
    }

    void SetPercent(float percent)
    {
        percent = Mathf.Clamp(percent, 0f, 100f);
        float max = GetMax();
        float cur = max * (percent / 100f);
        float now = GetCurrent();
        AddPercent(((cur - now) / Mathf.Max(1f, max)) * 100f);
    }

    float GetPercent01()
    {
        float max = GetMax();
        float cur = GetCurrent();
        return max > 0 ? Mathf.Clamp01(cur / max) : 0f;
    }

    float GetMax()
    {
        if (player != null && fGaugeMax != null)
        {
            object v = fGaugeMax.GetValue(player);
            if (v is float f) return f;
            if (v is int i) return i;
        }
        return 100f;
    }

    float GetCurrent()
    {
        if (player != null && pGauge != null)
        {
            object v = pGauge.GetValue(player);
            if (v is float f) return f;
            if (v is int i) return i;
        }
        return 0f;
    }
}
