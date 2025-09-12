using UnityEngine;

public class LockOnUIBinder : MonoBehaviour
{
    public PlayerLockOn playerLockOn;
    public EnemyHPBarPool barPool;

    EnemyHPBar currentBar;
    EnemyHPBarPresenter currentPresenter;

    void Update()
    {
        var t = playerLockOn ? playerLockOn.lockOnTarget : null;
        if (!t) { Clear(); return; }

        var presenter = t.GetComponentInParent<EnemyHPBarPresenter>();
        if (presenter != currentPresenter)
        {
            Clear();
            if (presenter != null)
            {
                currentBar = barPool.Get();
                presenter.Attach(currentBar);
                currentPresenter = presenter;
            }
        }
    }

    void Clear()
    {
        if (currentPresenter != null) { currentPresenter.Detach(); currentPresenter = null; }
        if (currentBar != null) { barPool.Return(currentBar); currentBar = null; }
    }
}
