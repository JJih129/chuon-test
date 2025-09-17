using System.Collections;
using UnityEngine;

// BossUIController.cs
// 설명: 플레이어와 보스 거리 기반으로 상단 HP(이미지)와 브레이크 HUD를 함께 바인딩/해제.
// - BossHUDImage (Image형 HP) 와 BossBreakHUD(Image형 브레이크)를 함께 사용하도록 설계.

public class BossUIController : MonoBehaviour
{
    [Header("▶ 참조 (필수)")]
    [Tooltip("플레이어 Transform. 거리 계산 기준.")]
    public Transform player;

    [Tooltip("상단 HUD 루트(GameObject). 기본적으로 비활성화 해놓음.")]
    public GameObject topHudRoot;

    [Tooltip("Image형 HP HUD 컴포넌트 (TopHudRoot에 붙여놓음).")]
    public BossHUD hpHud;

    [Tooltip("브레이크 HUD 컴포넌트 (Image 기반).")]
    public BossBreakHUD breakHud;

    [Tooltip("보스의 체력 컴포넌트 (IHealth 구현체).")]
    public MonoBehaviour healthBehaviour;

    [Tooltip("보스의 브레이크 컨트롤러 (없으면 자동 탐색).")]
    public BossBreakController breakController;

    [Header("▶ 거리/타이밍 (튜닝)")]
    [Tooltip("보스 HUD가 표시될 최대 거리(미터).")]
    public float showDistance = 18f;

    [Tooltip("숨김 히스테리시스(미터). showDistance + 값이 hide 기준.")]
    public float hideHysteresis = 2f;

    [Tooltip("거리 체크 주기(초). 0이면 매 프레임 체크.")]
    public float pollInterval = 0.12f;

    [Tooltip("근접 후 HUD가 켜지기 전 딜레이(초).")]
    public float showDelay = 0.05f;

    [Tooltip("범위 이탈 후 HUD가 꺼지기 전 딜레이(초).")]
    public float hideDelay = 0.12f;

    // 내부
    IHealth boundHealth;
    float showSqr;
    float hideSqr;
    bool isVisible;
    Coroutine pollRoutine;

    void Awake()
    {
        showSqr = showDistance * showDistance;
        hideSqr = (showDistance + Mathf.Max(0f, hideHysteresis));
        hideSqr *= hideSqr;

        if (topHudRoot != null) topHudRoot.SetActive(false);

        // 캐스팅/자동탐색
        boundHealth = healthBehaviour as IHealth;
        if (breakController == null)
            breakController = GetComponent<BossBreakController>();
        if (breakHud == null && topHudRoot != null)
            breakHud = topHudRoot.GetComponentInChildren<BossBreakHUD>();

        if (boundHealth == null)
            Debug.LogWarning($"[BossUIController] healthBehaviour이 IHealth를 구현하지 않습니다: {healthBehaviour?.GetType().Name}");
    }

    void OnEnable()
    {
        if (pollRoutine != null) StopCoroutine(pollRoutine);
        pollRoutine = StartCoroutine(Poll());
    }

    void OnDisable()
    {
        if (pollRoutine != null) StopCoroutine(pollRoutine);
        ForceHideImmediate();
    }

    IEnumerator Poll()
    {
        var wait = (pollInterval > 0f) ? new WaitForSeconds(pollInterval) : null;
        while (true)
        {
            Evaluate();
            if (wait != null) yield return wait;
            else yield return null;
        }
    }

    void Evaluate()
    {
        if (player == null) return;
        float sqr = (player.position - transform.position).sqrMagnitude;

        if (!isVisible && sqr <= showSqr)
        {
            if (showDelay <= 0f) ShowHUD();
            else StartCoroutine(DelayedShow(showDelay));
        }
        else if (isVisible && sqr > hideSqr)
        {
            if (hideDelay <= 0f) HideHUD();
            else StartCoroutine(DelayedHide(hideDelay));
        }
    }

    IEnumerator DelayedShow(float delay)
    {
        yield return new WaitForSeconds(delay);
        if ((player.position - transform.position).sqrMagnitude <= showSqr)
            ShowHUD();
    }

    IEnumerator DelayedHide(float delay)
    {
        yield return new WaitForSeconds(delay);
        if ((player.position - transform.position).sqrMagnitude > hideSqr)
            HideHUD();
    }

    void ShowHUD()
    {
        if (isVisible) return;
        if (hpHud == null || topHudRoot == null)
        {
            Debug.LogWarning("[BossUIController] hpHud 또는 topHudRoot 미할당.");
            return;
        }

        if (boundHealth == null && healthBehaviour is IHealth ih2)
            boundHealth = ih2;

        if (boundHealth != null)
            hpHud.Bind(boundHealth);

        // 브레이크 HUD 루트 활성화(브레이크 이미지는 breakHud가 자체적으로 갱신)
        if (breakHud != null && breakHud.hudRoot != null)
            breakHud.hudRoot.SetActive(true);

        topHudRoot.SetActive(true);
        isVisible = true;
    }

    void HideHUD()
    {
        if (!isVisible) return;
        if (hpHud != null) hpHud.Unbind();
        if (breakHud != null && breakHud.hudRoot != null) breakHud.hudRoot.SetActive(false);
        if (topHudRoot != null) topHudRoot.SetActive(false);
        isVisible = false;
    }

    // 강제 숨김 즉시
    public void ForceHideImmediate()
    {
        if (hpHud != null) hpHud.Unbind();
        if (breakHud != null && breakHud.hudRoot != null) breakHud.hudRoot.SetActive(false);
        if (topHudRoot != null) topHudRoot.SetActive(false);
        isVisible = false;
    }
}
