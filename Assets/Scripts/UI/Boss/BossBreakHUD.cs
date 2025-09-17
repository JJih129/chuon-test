using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[DisallowMultipleComponent]
public class BossBreakHUD : MonoBehaviour
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("▶ 참조 (필수)")]
    [Tooltip("브레이크 게이지를 표시할 Image. Image Type = Filled로 설정하세요.")]
    public Image breakFillImage;

    [Tooltip("HUD 루트. 브레이크 진입 시 활성화/비활성화할 루트 오브젝트")]
    public GameObject hudRoot;

    [Tooltip("연동할 보스의 BossBreakController. 비워두면 부모에서 자동 탐색")]
    public BossBreakController breakController;

    [Header("▶ 동작/튜닝 값")]
    [Tooltip("fillAmount 보간 속도(클수록 더 빠르게 따라감)")]
    public float followSpeed = 8f;

    [Tooltip("브레이크 진입 시 HUD를 보이게 할지 여부")]
    public bool showOnBreakEnter = true;

    [Tooltip("브레이크 해제 시 숨길지 여부")]
    public bool hideOnBreakExit = true;

    [Tooltip("HUD 활성화/비활성시 페이드(채널) 사용 여부")]
    public bool useFade = false;

    [Tooltip("페이드용 CanvasGroup (useFade=true일 때 할당)")]
    public CanvasGroup canvasGroup;

    [Header("▶ 시각 효과")]
    [Tooltip("브레이크 도달시 깜빡임 지속시간")]
    public float hitFlashDuration = 0.35f;

    [Tooltip("브레이크 도달시 깜빡임 횟수")]
    public int hitFlashCount = 2;

    // 내부
    float currentFill = 0f;
    Coroutine flashRoutine;

    void Awake()
    {
        if (hudRoot != null) hudRoot.SetActive(false);
        if (breakController == null)
            breakController = GetComponentInParent<BossBreakController>();

        if (breakFillImage == null)
            Debug.LogWarning("[BossBreakHUD] breakFillImage 미할당.");

        // 이벤트 구독
        if (breakController != null)
        {
            breakController.OnBreakEnter.AddListener(OnBreakEnter);
            breakController.OnBreakExit.AddListener(OnBreakExit);
        }
    }

    void OnDestroy()
    {
        if (breakController != null)
        {
            breakController.OnBreakEnter.RemoveListener(OnBreakEnter);
            breakController.OnBreakExit.RemoveListener(OnBreakExit);
        }
    }

    void Update()
    {
        if (breakController == null || breakFillImage == null) return;

        float target = breakController.Get01();
        currentFill = Mathf.Lerp(currentFill, target, Mathf.Clamp01(Time.deltaTime * followSpeed));
        breakFillImage.fillAmount = currentFill;
    }

    void OnBreakEnter()
    {
        if (showOnBreakEnter && hudRoot != null) SetHUDVisible(true);
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashFill());
    }

    void OnBreakExit()
    {
        if (hideOnBreakExit && hudRoot != null) SetHUDVisible(false);
    }

    void SetHUDVisible(bool on)
    {
        if (useFade && canvasGroup != null)
        {
            StopAllCoroutines();
            StartCoroutine(FadeCanvas(canvasGroup, on ? 1f : 0f, 0.18f, on));
        }
        else
        {
            hudRoot.SetActive(on);
        }
    }

    IEnumerator FadeCanvas(CanvasGroup cg, float targetAlpha, float dur, bool ensureActive)
    {
        if (ensureActive && !cg.gameObject.activeSelf) cg.gameObject.SetActive(true);
        float start = cg.alpha;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(start, targetAlpha, t / dur);
            yield return null;
        }
        cg.alpha = targetAlpha;
        if (targetAlpha <= 0f) cg.gameObject.SetActive(false);
    }

    IEnumerator FlashFill()
    {
        if (breakFillImage == null) yield break;
        var orig = breakFillImage.color;
        for (int i = 0; i < hitFlashCount; i++)
        {
            // 밝은 색 순간 적용
            breakFillImage.color = Color.white;
            yield return new WaitForSeconds(hitFlashDuration * 0.5f);
            breakFillImage.color = orig;
            yield return new WaitForSeconds(hitFlashDuration * 0.5f);
        }
        flashRoutine = null;
    }
}
