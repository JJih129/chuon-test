using UnityEngine;
using UnityEngine.UI;

public class UI_UltimateGauge : MonoBehaviour
{
    [Header("Fill Image")]
    [Tooltip("Image Type=Filled gauge image")]
    public Image fill;

    [Header("Ready Effect")]
    [Tooltip("Object to enable when the gauge is full")]
    public GameObject readyFx;

    [Header("Lerp Speed")]
    [Tooltip("How quickly the visible gauge follows the target value")]
    public float lerpSpeed = 6f;

    static UI_UltimateGauge _inst;
    float _target;
    float _current;
    bool _ready;
    float _nextRefreshAt;
    const float RefreshInterval = 1f / 15f;

    void Awake()
    {
        _inst = this;
        SetInternal(0f);
        SetReadyInternal(false);
        enabled = false;
    }

    void Update()
    {
        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshInterval;
        if (Mathf.Abs(_target - _current) <= 0.0005f)
        {
            enabled = false;
            return;
        }

        _current = Mathf.MoveTowards(_current, _target, Mathf.Max(0.01f, lerpSpeed) * RefreshInterval);
        if (fill != null && Mathf.Abs(fill.fillAmount - _current) > 0.001f)
            fill.fillAmount = _current;
    }

    public static void UpdateValue(float ratio)
    {
        if (_inst == null)
            return;

        _inst.SetInternal(Mathf.Clamp01(ratio));
    }

    public static void SetReady(bool ready)
    {
        if (_inst == null)
            return;

        _inst.SetReadyInternal(ready);
    }

    void SetInternal(float ratio01)
    {
        _target = ratio01;
        if (Mathf.Abs(_target - _current) > 0.0005f)
            enabled = true;

        if (_target < 1f && readyFx != null && readyFx.activeSelf)
            readyFx.SetActive(false);
    }

    void SetReadyInternal(bool ready)
    {
        if (_ready == ready)
            return;

        _ready = ready;
        if (readyFx != null)
            readyFx.SetActive(_ready);
    }
}
