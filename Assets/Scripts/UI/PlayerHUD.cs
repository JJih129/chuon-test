using UnityEngine;
using UnityEngine.UI;
using System;

/// ==============================
/// ▼ 변수 헤더(한글 설명)
/// hpFillImage         : 체력바로 쓸 Image. Type=Filled, fillAmount 0~1 적용
/// ampouleSlots[5]     : 앰플 5칸 개별 아이콘 Image. 좌→우 순서로 드래그
/// maxAmpouleCount     : 최대 앰플 개수(표기는 5칸 고정)
/// Bind/Unbind         : IHealth/소모품 이벤트 연결 및 해제
/// UpdateHP/UpdateAmpoule : 외부에서 수치 갱신 시 직접 호출용
/// ==============================
public class PlayerHUD : MonoBehaviour
{
    // ── HP (Image.fillAmount) ─────────────────────────────────────
    [Header("HP Fill 이미지 | Type=Filled")]
    [Tooltip("체력바로 사용할 Image 컴포넌트")]
    public Image hpFillImage;

    // ── 앰플 (5칸 이미지) ────────────────────────────────────────
    [Header("앰플 슬롯 이미지 5칸 | 좌→우 순서")]
    [Tooltip("각 칸을 나타내는 개별 Image")]
    public Image[] ampouleSlots = new Image[5];

    [Header("최대 앰플 개수")]
    public int maxAmpouleCount = 5;

    // ── 바인딩 대상(옵션) ───────────────────────────────────────
    IHealth boundHealth;                // HP 이벤트 소스
    PlayerConsumables boundConsumables; // 앰플 이벤트 소스(있으면)

    // ── 공개 API ────────────────────────────────────────────────
    // [HP 업데이트] - 현재/최대 HP를 비율로 반영
    public void UpdateHP(int current, int max)
    {
        if (hpFillImage && max > 0)
            hpFillImage.fillAmount = Mathf.Clamp01((float)current / max);
        else if (hpFillImage)
            hpFillImage.fillAmount = 0f;
    }

    // [앰플 업데이트] - 현재 앰플 개수만큼 슬롯 on
    public void UpdateAmpoule(int count)
    {
        int cur = Mathf.Clamp(count, 0, Mathf.Min(maxAmpouleCount, ampouleSlots.Length));
        for (int i = 0; i < ampouleSlots.Length; i++)
            if (ampouleSlots[i]) ampouleSlots[i].enabled = (i < cur);
    }

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
            UpdateHP(boundHealth.CurrentHP, boundHealth.MaxHP);
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
    }

    // ── 내부 콜백 ───────────────────────────────────────────────
    void OnHPChanged(int cur, int max) => UpdateHP(cur, max);
    void OnAmpouleChanged(int cur, int max) => UpdateAmpoule(cur);
    void OnDied() { /* 필요 시 사망 연출 */ }
}
