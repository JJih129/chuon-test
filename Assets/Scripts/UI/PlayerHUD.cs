using UnityEngine;
using UnityEngine.UI;
using DG.Tweening; // 🚩 DOTween 사용을 위한 필수 using

/// ==============================
/// ▼ 변수 헤더(한글 설명)
/// (중략)
/// hpFillImage         : 체력바로 쓸 Image. Type=Filled, fillAmount 0~1 적용
/// (추가) dotweenDuration : HP 애니메이션이 걸리는 시간
/// (중략)
/// ==============================
public class PlayerHUD : MonoBehaviour
{
    // ── HP (Image.fillAmount) ─────────────────────────────────────
    [Header("HP Fill 이미지 | Type=Filled")]
    [Tooltip("체력바로 사용할 Image 컴포넌트")]
    public Image hpFillImage;

    // 🚩 DOTween 애니메이션 설정 추가
    [Header("DOTween 설정")]
    [Tooltip("HP Fill Amount 애니메이션에 걸리는 시간 (초)")]
    public float dotweenDuration = 0.3f; // 현업에서 자주 쓰는 짧은 시간

    // ── 앰플 (5칸 이미지) ────────────────────────────────────────
    [Header("앰플 슬롯 이미지 5칸 | 좌→우 순서")]
    [Tooltip("각 칸을 나타내는 개별 Image")]
    public Image[] ampouleSlots = new Image[5];

    // [개선 제안 적용] maxAmpouleCount 대신 배열 길이를 사용하도록 제거
    // [Header("최대 앰플 개수")]
    // public int maxAmpouleCount = 5; 

    // ── 바인딩 대상(옵션) ───────────────────────────────────────
    IHealth boundHealth;                // HP 이벤트 소스
    PlayerConsumables boundConsumables; // 앰플 이벤트 소스(있으면)

    // ── 공개 API ────────────────────────────────────────────────

    // [HP 업데이트] - 현재/최대 HP를 비율로 반영 (🚩 DOTween 적용)
    public void UpdateHP(int current, int max)
    {
        if (!hpFillImage) return;

        float targetFillAmount;

        if (max > 0)
            targetFillAmount = Mathf.Clamp01((float)current / max);
        else
            targetFillAmount = 0f;

        // 🚩 기존 fillAmount 직접 대입 대신 DOTween 사용
        // DOTween.To() 또는 DOFillAmount() 사용
        hpFillImage.DOKill(); // 기존에 진행 중이던 트윈이 있다면 캔슬하고 시작
        hpFillImage.DOFillAmount(targetFillAmount, dotweenDuration)
                   .SetEase(Ease.OutSine); // OutSine 등의 이징(Easing) 함수로 부드러움을 극대화
    }


    // [앰플 업데이트] - 현재 앰플 개수만큼 슬롯 on
    public void UpdateAmpoule(int count)
    {
        int maxDisplayCount = ampouleSlots.Length; // 배열 길이 사용
        int cur = Mathf.Clamp(count, 0, maxDisplayCount);
        
        for (int i = 0; i < ampouleSlots.Length; i++)
            if (ampouleSlots[i]) ampouleSlots[i].enabled = (i < cur);
    }
    
    // (중략 - Bind, Unbind, 내부 콜백 로직은 기존과 동일)

    // [Bind] - 외부 시스템(IHealth, PlayerConsumables)과 연결
    public void Bind(IHealth health, PlayerConsumables consumables = null)
    {
        Unbind();

        // HP 바인딩
        boundHealth = health;
        if (boundHealth != null)
        {
            boundHealth.OnHPChanged += OnHPChanged;
            boundHealth.OnDied += OnDied;
            // 🚩 초기 바인딩 시점에는 애니메이션 없이 즉시 반영 (선택 사항)
            if (hpFillImage && boundHealth.MaxHP > 0)
                hpFillImage.fillAmount = Mathf.Clamp01((float)boundHealth.CurrentHP / boundHealth.MaxHP);
            else if (hpFillImage)
                hpFillImage.fillAmount = 0f;
            // UpdateHP(boundHealth.CurrentHP, boundHealth.MaxHP); // 초기에는 부드러운 애니메이션 생략
        }

        // 앰플 바인딩(있을 때만)
        boundConsumables = consumables;
        if (boundConsumables != null)
        {
            boundConsumables.OnAmpouleChanged += OnAmpouleChanged;
            UpdateAmpoule(boundConsumables.CurrentAmpoule);
        }
        else
        {
            UpdateAmpoule(0);
        }
    }

    // [Unbind] - 이벤트 해제
    public void Unbind()
    {
        if (boundHealth != null)
        {
            boundHealth.OnHPChanged -= OnHPChanged;
            boundHealth.OnDied -= OnDied;
            boundHealth = null;
        }
        if (boundConsumables != null)
        {
            boundConsumables.OnAmpouleChanged -= OnAmpouleChanged;
            boundConsumables = null;
        }
        hpFillImage?.DOKill(); // 🚩 언바인드 시 혹시 모를 DOTween도 킬
    }

    // ── 내부 콜백 ───────────────────────────────────────────────
    void OnHPChanged(int cur, int max) => UpdateHP(cur, max);
    void OnAmpouleChanged(int cur, int max) => UpdateAmpoule(cur);
    void OnDied() { /* 필요 시 사망 연출 */ }
}