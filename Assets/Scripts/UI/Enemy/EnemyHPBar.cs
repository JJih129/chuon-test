using UnityEngine;
using UnityEngine.UI;

public class EnemyHPBar : MonoBehaviour
{
    public Slider hpSlider;
    public Canvas canvas;      // World Space
    public Transform target;   // 머리 위 Pivot
    public Vector3 offset = new Vector3(0, 1.0f, 0);

    Camera cam;
    IHealth bound;

    void Awake()
    {
        cam = Camera.main;
        if (canvas) canvas.enabled = false;
    }

    public void Bind(IHealth health)
    {
        Unbind();
        bound = health;
        bound.OnHPChanged += OnHPChanged;
        bound.OnDied += OnDied;
        OnHPChanged(bound.CurrentHP, bound.MaxHP);
    }

    public void Unbind()
    {
        if (bound != null)
        {
            bound.OnHPChanged -= OnHPChanged;
            bound.OnDied -= OnDied;
            bound = null;
        }
    }

    void OnHPChanged(int cur, int max)
    {
        if (hpSlider) hpSlider.value = (float)cur / max;
    }

    void OnDied() => Hide();

    public void Show() { if (canvas) canvas.enabled = true; }
    public void Hide() { if (canvas) canvas.enabled = false; }

    void LateUpdate()
    {
        if (!canvas || !target || !cam || !canvas.enabled) return;
        transform.position = target.position + offset;
        transform.forward = cam.transform.forward;
    }
}
