using UnityEngine;
using UnityEngine.UI;

public class UI_UltimateGauge : MonoBehaviour
{
    // ===== 변수 헤더(한글) =====
    [Header("게이지 표시 | 0~1 비율로 채움/회전/색상단계")]
    public Image fill;
    public RectTransform rotor;
    public Gradient colorByPercent;

    static UI_UltimateGauge _instance;

    void Awake() => _instance = this;

    public static void UpdateValue(float t)
    {
        if (_instance == null) return;
        if (_instance.fill)  _instance.fill.fillAmount = t;
        if (_instance.rotor) _instance.rotor.localEulerAngles = new Vector3(0,0, Mathf.Lerp(0, 720f, t));
        if (_instance.fill)  _instance.fill.color = _instance.colorByPercent.Evaluate(t);
    }
}