using UnityEngine;
using UnityEngine.UI;

// BossHUDImage.cs
// 설명: 상단 고정 보스 HP를 Image(Filled)로 표시. Slider 대신 Image 사용.
// 사용: HUD 루트(비활성) 위에 붙이고 hpFillImage에 Filled 타입 Image 할당.

public class BossHUD : MonoBehaviour
{
    [Header("▶ HP 이미지 (필수)")]
    [Tooltip("Image Type = Filled. 채워지는 부분에 사용할 이미지.")]
    public Image hpFillImage;

    // 내부 바인딩 대상
    IHealth bound;

    // 바인딩: IHealth를 받아 이벤트 구독
    public void Bind(IHealth boss)
    {
        Unbind();
        if (boss == null) return;
        bound = boss;
        UpdateHPImage(boss.CurrentHP, boss.MaxHP);
        boss.OnHPChanged += UpdateHPImage;
        boss.OnDied += OnDied;
        gameObject.SetActive(true);
    }

    public void Unbind()
    {
        if (bound != null)
        {
            bound.OnHPChanged -= UpdateHPImage;
            bound.OnDied -= OnDied;
            bound = null;
        }
        gameObject.SetActive(false);
    }

    void UpdateHPImage(int cur, int max)
    {
        if (hpFillImage == null) return;
        hpFillImage.fillAmount = (max > 0) ? (float)cur / max : 0f;
    }

    void OnDied() => Unbind();
}
