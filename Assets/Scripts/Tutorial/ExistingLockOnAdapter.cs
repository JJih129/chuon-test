using UnityEngine;

[DisallowMultipleComponent]
public class ExistingLockOnAdapter : MonoBehaviour, ILockOnStateReader
{
    [SerializeField] private PlayerLockOn playerLockOn;

    public Transform CurrentTarget => playerLockOn != null ? playerLockOn.GetCurrentTarget() : null;
    public bool IsLockedOn => playerLockOn != null && playerLockOn.IsLockedOn();

    public void ConfigureRuntime(PlayerLockOn runtimeLockOn)
    {
        playerLockOn = runtimeLockOn;
    }

    void Awake()
    {
        if (playerLockOn == null)
            playerLockOn = GetComponent<PlayerLockOn>();
    }
}
