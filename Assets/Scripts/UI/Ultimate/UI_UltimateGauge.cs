using UnityEngine;
using UnityEngine.UI;

public class UI_UltimateGauge : MonoBehaviour
{
    [Header("Fill")]
    public Image fill;

    [Header("Ready FX")]
    public GameObject readyFx;

    [Header("Lerp")]
    public float lerpSpeed = 6f;

    const float SnapEpsilon = 0.0001f;

    static UI_UltimateGauge _inst;
    float _target;
    float _current;
    bool _ready;

    void Awake()
    {
        _inst = this;
        SetInternal(0f);
        SetReadyInternal(false);
    }

    void Update()
    {
        if (Mathf.Abs(_current - _target) <= SnapEpsilon)
        {
            _current = _target;
            if (fill && !Mathf.Approximately(fill.fillAmount, _current))
                fill.fillAmount = _current;
            return;
        }

        _current = Mathf.Lerp(_current, _target, Time.deltaTime * lerpSpeed);
        if (fill && !Mathf.Approximately(fill.fillAmount, _current))
            fill.fillAmount = _current;
    }

    public static void UpdateValue(float ratio)
    {
        if (_inst == null) return;
        _inst.SetInternal(Mathf.Clamp01(ratio));
    }

    public static void SetReady(bool ready)
    {
        if (_inst == null) return;
        _inst.SetReadyInternal(ready);
    }

    void SetInternal(float ratio01)
    {
        if (Mathf.Approximately(_target, ratio01))
        {
            if (ratio01 < 1f && _ready)
                SetReadyInternal(false);
            return;
        }

        _target = ratio01;
        if (_target < 1f)
            SetReadyInternal(false);
    }

    void SetReadyInternal(bool ready)
    {
        if (_ready == ready) return;

        _ready = ready;
        if (readyFx && readyFx.activeSelf != _ready)
            readyFx.SetActive(_ready);
    }
}
