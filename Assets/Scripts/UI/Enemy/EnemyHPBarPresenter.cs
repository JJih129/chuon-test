using UnityEngine;

public class EnemyHPBarPresenter : MonoBehaviour
{
    public EnemyHPBar bar;
    IHealth boundHealth;

    // 기존 단일 인자
    public void Attach(IHealth health)
    {
        Attach(health, null, Vector3.up * 2f);
    }

    // 추가된 오버로드: 타겟 Transform과 오프셋을 함께 지정 가능
    public void Attach(IHealth health, Transform targetTransform, Vector3 offset)
    {
        if (health == null) return;
        boundHealth = health;
        // bar 구성
        if (bar != null)
        {
            bar.target = targetTransform;
            bar.offset = offset;
            bar.Show();
        }

        health.OnHPChanged += OnHPChanged;
        health.OnHealthChanged += OnHPChanged;
    }

    public void Detach()
    {
        if (boundHealth != null)
        {
            boundHealth.OnHPChanged -= OnHPChanged;
            boundHealth.OnHealthChanged -= OnHPChanged;
            boundHealth = null;
        }
        if (bar != null) bar.Hide();
    }

    void OnHPChanged(int cur, int max)
    {
        if (bar != null) bar.OnHPChangedCallback(cur, max);
    }
}
