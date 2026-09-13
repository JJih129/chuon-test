using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("HP Fill Image | Type=Filled")]
    [Tooltip("Image component used for the HP bar fill.")]
    public Image hpFillImage;

    [Header("DOTween Settings")]
    [Tooltip("Duration for HP fill tween updates.")]
    public float dotweenDuration = 0.3f;

    [Header("Ampoule Slot Images")]
    [Tooltip("Array of images that represent ampoule count.")]
    public Image[] ampouleSlots = new Image[5];

    IHealth boundHealth;
    PlayerConsumables boundConsumables;
    float lastHpFill = -1f;
    int lastAmpouleCount = -1;

    public void UpdateHP(int current, int max)
    {
        if (!hpFillImage) return;

        float targetFillAmount = max > 0
            ? Mathf.Clamp01((float)current / max)
            : 0f;

        if (Mathf.Approximately(lastHpFill, targetFillAmount) &&
            Mathf.Approximately(hpFillImage.fillAmount, targetFillAmount))
            return;

        hpFillImage.DOKill();
        hpFillImage.DOFillAmount(targetFillAmount, dotweenDuration)
            .SetEase(Ease.OutSine);
        lastHpFill = targetFillAmount;
    }

    public void UpdateAmpoule(int count)
    {
        int maxDisplayCount = ampouleSlots.Length;
        int cur = Mathf.Clamp(count, 0, maxDisplayCount);
        if (lastAmpouleCount == cur)
            return;

        for (int i = 0; i < ampouleSlots.Length; i++)
        {
            if (ampouleSlots[i])
                ampouleSlots[i].enabled = (i < cur);
        }

        lastAmpouleCount = cur;
    }

    public void Bind(IHealth health, PlayerConsumables consumables = null)
    {
        Unbind();

        boundHealth = health;
        if (boundHealth != null)
        {
            boundHealth.OnHPChanged += OnHPChanged;
            boundHealth.OnDied += OnDied;

            if (hpFillImage && boundHealth.MaxHP > 0)
            {
                lastHpFill = Mathf.Clamp01((float)boundHealth.CurrentHP / boundHealth.MaxHP);
                hpFillImage.fillAmount = lastHpFill;
            }
            else if (hpFillImage)
            {
                lastHpFill = 0f;
                hpFillImage.fillAmount = 0f;
            }
        }

        boundConsumables = consumables;
        if (boundConsumables != null)
        {
            boundConsumables.OnAmpouleChanged += OnAmpouleChanged;
            UpdateAmpoule(boundConsumables.CurrentAmpoule);
        }
        else
        {
            lastAmpouleCount = -1;
            UpdateAmpoule(0);
        }
    }

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

        hpFillImage?.DOKill();
        lastHpFill = -1f;
        lastAmpouleCount = -1;
    }

    void OnHPChanged(int cur, int max) => UpdateHP(cur, max);
    void OnAmpouleChanged(int cur, int max) => UpdateAmpoule(cur);
    void OnDied() { }
}
