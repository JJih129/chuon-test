// 파일명: PatternVisuals.cs
using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class PatternVisuals : MonoBehaviour
{
    [Header("비주얼 타깃 렌더러")]
    [Tooltip("보스 모델 중 색을 깜빡이게 할 Renderer. 비워두면 자식에서 자동으로 찾습니다.")]
    public Renderer visualPartRenderer;

    [Header("쉐이더 컬러 프로퍼티 이름")]
    [Tooltip("HDR Emission 컬러 프로퍼티 이름 (예: _EmissionColor, _BaseColor 등)")]
    public string emissionColorName = "_EmissionColor";

    [Header("색상 설정 (HDR 권장)")]
    [Tooltip("패링/가드 가능한 공격 색상")]
    [ColorUsage(true, true)]
    public Color parryColor = new Color(1f, 0.5f, 0f) * 5f;

    [Tooltip("패링/가드 불가(언가더블) 공격 색상")]
    [ColorUsage(true, true)]
    public Color nonParryColor = Color.red * 5f;

    [Tooltip("기본/아이들 상태 색상")]
    [ColorUsage(true, true)]
    public Color idleColor = Color.black;

    [Header("플래시 타이밍")]
    [Tooltip("한 번 깜빡이는 전체 시간(초)")]
    [Min(0.01f)]
    public float flashDuration = 0.5f;

    // 내부용
    Material _runtimeMaterial;
    Coroutine _flashRoutine;

    void Awake()
    {
        // 렌더러 자동 탐색 (지정 안 했을 때)
        if (!visualPartRenderer)
        {
            visualPartRenderer = GetComponentInChildren<Renderer>(true);
        }

        if (!visualPartRenderer)
        {
            Debug.LogError("[PatternVisuals] 타깃 Renderer가 설정되지 않았습니다.", this);
            enabled = false;
            return;
        }

        // 이 보스 전용 머터리얼 인스턴스 생성
        // (주의: material 사용은 인스턴스를 만들기 때문에, 런타임 생성 개수가 많으면 비용이 커질 수 있음)
        _runtimeMaterial = visualPartRenderer.material;

        // 시작 시 기본 색으로 초기화
        SetEmissionColor(idleColor);
    }

    void OnDisable()
    {
        // 컴포넌트가 꺼질 때 깜빡임 중이면 정리 + 기본색으로 복귀
        StopFlash();
        SetEmissionColor(idleColor);
    }

    // ======================================================================
    //  외부에서 호출하는 API
    //  BossController 에서 patternVisuals.SetParryable(pattern.isParryable)
    //  또는 StartVisualCue(true/false) 로 호출하면 됨.
    // ======================================================================

    /// <summary>
    /// BossController에서 패턴 진입 시 호출하는 진입점.
    /// 내부적으로 StartVisualCue를 호출한다.
    /// </summary>
    public void SetParryable(bool isParryable)
    {
        StartVisualCue(isParryable);
    }

    /// <summary>
    /// isParryable에 따라 색을 한 번 깜빡인다.
    /// </summary>
    public void StartVisualCue(bool isParryable)
    {
        if (!isActiveAndEnabled) return;

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);

        _flashRoutine = StartCoroutine(Co_FlashVisuals(isParryable));
    }

    /// <summary>
    /// 외부에서 강제로 기본 색으로 되돌리고 싶을 때 사용.
    /// (예: 공격 애니 끝 AnimationEvent에서 호출)
    /// </summary>
    public void ResetToIdle()
    {
        StopFlash();
        SetEmissionColor(idleColor);
    }

    // ======================================================================
    //  내부 구현
    // ======================================================================

    IEnumerator Co_FlashVisuals(bool isParryable)
    {
        Color targetColor = isParryable ? parryColor : nonParryColor;

        // 슬로우모션 영향을 받지 않게 unscaledDeltaTime 사용
        SetEmissionColor(targetColor);

        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        SetEmissionColor(idleColor);
        _flashRoutine = null;
    }

    void StopFlash()
    {
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
        }
    }

    void SetEmissionColor(Color color)
    {
        if (_runtimeMaterial == null) return;
        _runtimeMaterial.SetColor(emissionColorName, color);
    }

#if UNITY_EDITOR
    // 에디터에서 우클릭으로 바로 테스트할 수 있는 메뉴
    [ContextMenu("Test Parryable Flash")]
    void TestParryableFlash()
    {
        StartVisualCue(true);
    }

    [ContextMenu("Test Non-Parryable Flash")]
    void TestNonParryableFlash()
    {
        StartVisualCue(false);
    }

    [ContextMenu("Reset To Idle Color")]
    void TestResetIdle()
    {
        ResetToIdle();
    }
#endif
}
