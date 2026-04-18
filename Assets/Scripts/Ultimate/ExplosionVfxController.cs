using UnityEngine;

[DisallowMultipleComponent]
public sealed class ExplosionVfxController : MonoBehaviour
{
    [SerializeField] private UltimateVFXPresenter vfxPresenter;

    public void PlayFinalExplosion(Vector3 position)
    {
        vfxPresenter?.PlayFinalExplosion(position);
    }
}
