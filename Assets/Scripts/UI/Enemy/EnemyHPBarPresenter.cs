using UnityEngine;

public class EnemyHPBarPresenter : MonoBehaviour
{
    public Transform pivot; // LockPivot (없으면 자동 생성)
    EnemyHealth health;
    EnemyHPBar bar;

    void Awake()
    {
        health = GetComponent<EnemyHealth>();
        if (!pivot)
        {
            var go = new GameObject("LockPivot");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 1.3f, 0); // 기본 머리 높이
            pivot = go.transform;
        }
    }

    public void Attach(EnemyHPBar hpBar)
    {
        bar = hpBar;
        bar.target = pivot;
        bar.Bind(health);
        bar.Show();
    }

    public void Detach()
    {
        if (bar != null)
        {
            bar.Hide();
            bar.Unbind();
            bar = null;
        }
    }
}
