using UnityEngine;

[DisallowMultipleComponent]
public sealed class ExplosionVfxController : MonoBehaviour
{
    [SerializeField] private UltimateVFXPresenter vfxPresenter;

    public void ConfigureRuntime(UltimateVFXPresenter presenter)
    {
        vfxPresenter = presenter;
    }

    public void PlayFinalExplosion(Vector3 position)
    {
        vfxPresenter?.PlayFinalExplosion(position);
    }
}
