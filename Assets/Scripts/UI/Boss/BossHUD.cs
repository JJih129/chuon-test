using UnityEngine;
using UnityEngine.UI;

public class BossHUD : MonoBehaviour
{
    public Slider hpBar;
    IHealth bound;

    public void Bind(IHealth boss) {
        Unbind();
        bound = boss;
        OnHPChanged(boss.CurrentHP, boss.MaxHP);
        boss.OnHPChanged += OnHPChanged;
        boss.OnDied += OnDied;
        gameObject.SetActive(true);
    }
    public void Unbind() {
        if (bound != null) {
            bound.OnHPChanged -= OnHPChanged;
            bound.OnDied -= OnDied;
            bound = null;
        }
        gameObject.SetActive(false);
    }
    void OnHPChanged(int cur, int max) => hpBar.value = (float)cur / max;
    void OnDied() => Unbind();
}