using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHUD : MonoBehaviour
{
    [Header("HP UI")]
    public Slider hpBar;
    public Image damageFlash;
    public Color flashColor = new Color(1, 0, 0, 0.35f);
    public float flashFadeSpeed = 5f;

    [Header("Ampoule UI")]
    public TMP_Text ampouleText;

    IHealth boundHealth;
    PlayerConsumables boundConsumables;
    bool flash;

    void Update()
    {
        if (!damageFlash) return;
        if (flash) { damageFlash.color = flashColor; flash = false; }
        else damageFlash.color = Color.Lerp(damageFlash.color, Color.clear, flashFadeSpeed * Time.deltaTime);
    }

    public void Bind(IHealth health, PlayerConsumables consumables = null)
    {
        Unbind();

        boundHealth = health;
        if (boundHealth != null)
        {
            boundHealth.OnHPChanged += OnHPChanged;
            boundHealth.OnDamaged += _ => flash = true;
            boundHealth.OnDied += OnDied;
            OnHPChanged(boundHealth.CurrentHP, boundHealth.MaxHP);
        }

        boundConsumables = consumables;
        if (boundConsumables != null)
        {
            boundConsumables.OnAmpouleChanged += OnAmpouleChanged;
            OnAmpouleChanged(boundConsumables.CurrentAmpoule, boundConsumables.MaxAmpoule);
        }
    }

    public void Unbind()
    {
        if (boundHealth != null)
        {
            boundHealth.OnHPChanged -= OnHPChanged;
            boundHealth.OnDamaged -= _ => flash = true; // 람다 해제는 한계 → 간단 샘플. 실프로덕션은 핸들 저장 권장
            boundHealth.OnDied -= OnDied;
            boundHealth = null;
        }
        if (boundConsumables != null)
        {
            boundConsumables.OnAmpouleChanged -= OnAmpouleChanged;
            boundConsumables = null;
        }
    }

    void OnHPChanged(int cur, int max)
    {
        if (hpBar) hpBar.value = (float)cur / max;
    }

    void OnAmpouleChanged(int cur, int max)
    {
        if (ampouleText) ampouleText.text = $"x {cur}";
    }

    void OnDied()
    {
        // 사망 시 HUD 반응 필요하면 여기
    }
}
