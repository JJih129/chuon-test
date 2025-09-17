using UnityEngine;
using UnityEngine.UI;

public class UI_UltimateGauge : MonoBehaviour
{
    // ========================= 변수 헤더(한글 설명) =========================
    [Header("① 채움 이미지 | 게이지 바(필수)")]
    [Tooltip("Image Type=Filled 인 채움 이미지")]
    public Image fill;

    [Header("② 준비 이펙트 | 100% 도달 시 켜질 FX(선택)")]
    [Tooltip("게이지가 가득 찰 때 활성화할 오브젝트")]
    public GameObject readyFx;

    [Header("③ 보간 속도 | 채움량 부드럽게 이동 속도")]
    [Tooltip("값이 높을수록 빠르게 따라간다")]
    public float lerpSpeed = 6f;

    // ========================= 내부 상태 =========================
    static UI_UltimateGauge _inst;   // 정적 접근(컨트롤러에서 호출)
    float _target;                   // 목표 채움 비율 0~1
    float _current;                  // 현재 채움 비율 0~1
    bool _ready;

    void Awake()
    {
        _inst = this;
        SetInternal(0f);
        SetReadyInternal(false);
    }

    void Update()
    {
        _current = Mathf.Lerp(_current, _target, Time.deltaTime * lerpSpeed);
        if (fill) fill.fillAmount = _current;
    }

    // ============ 공개 정적 API(컨트롤러/시스템에서 호출) ============
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

    // ========================= 내부 구현 =========================
    void SetInternal(float ratio01)
    {
        _target = ratio01;
        if (_target < 1f && readyFx) readyFx.SetActive(false);
    }

    void SetReadyInternal(bool ready)
    {
        _ready = ready;
        if (readyFx) readyFx.SetActive(_ready);
    }
}
