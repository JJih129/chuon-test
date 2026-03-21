using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class BossHUD : MonoBehaviour
{
    [Header("Top Boss HP")]
    [Tooltip("Filled image used for the HP bar.")]
    public Image hpFillImage;

    IHealth bound;

    public void Bind(IHealth boss)
    {
        if (boss == null)
            return;

        if (ReferenceEquals(bound, boss))
        {
            UpdateHPImage(boss.CurrentHP, boss.MaxHP);
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            return;
        }

        Unbind();
        bound = boss;
        UpdateHPImage(boss.CurrentHP, boss.MaxHP);
        boss.OnHPChanged += UpdateHPImage;
        boss.OnDied += OnDied;

        if (!gameObject.activeSelf)
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

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void UpdateHPImage(int current, int max)
    {
        if (hpFillImage == null)
            return;

        hpFillImage.fillAmount = max > 0 ? (float)current / max : 0f;
    }

    void OnDied()
    {
        Unbind();
    }
}
