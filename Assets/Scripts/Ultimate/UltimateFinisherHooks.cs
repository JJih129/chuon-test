using UnityEngine;

public class UltimateFinisherHooks : MonoBehaviour
{
    [Header("Anim & FX")]
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private string finisherTrigger = "Ultimate_Finish";
    [SerializeField] private GameObject explodePrefab;
    [SerializeField] private Transform explodePoint;

    [Header("Optional: Time & Screen FX")]
    [SerializeField] private TimeScaleController timeCtrl;     // 없어도 동작
    [SerializeField] private ScreenFXSignalHooks screenFX;     // 없어도 동작

    // 타임라인 Signal에서 이 메서드만 호출하면 됨
    public void DoFinisher()
    {
        if (playerAnimator && !string.IsNullOrEmpty(finisherTrigger))
            playerAnimator.SetTrigger(finisherTrigger);

        if (explodePrefab && explodePoint)
            Instantiate(explodePrefab, explodePoint.position, explodePoint.rotation);

        if (screenFX) screenFX.FadeOutGray(0.3f);
        if (timeCtrl) timeCtrl.Restore(0.3f);
    }
}
