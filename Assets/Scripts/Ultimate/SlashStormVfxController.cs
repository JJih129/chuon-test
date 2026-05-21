using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SlashStormVfxController : MonoBehaviour
{
    [SerializeField] private UltimateVFXPresenter vfxPresenter;
    [SerializeField] private bool debugLog;

    Coroutine _stormRoutine;

    public void ConfigureRuntime(UltimateVFXPresenter presenter)
    {
        vfxPresenter = presenter;
    }

    public void PlayIntroPose()
    {
        vfxPresenter?.PlayIntroPose();
    }

    public void PlayDrawRelease(Vector3 position, Vector3 lookTarget)
    {
        vfxPresenter?.PlayDashSlash(position, lookTarget);
    }

    public void BeginStorm(UltimateSequenceData data, UltimateTargetBinder binder)
    {
        StopStorm(false);

        if (data == null || binder == null || vfxPresenter == null)
            return;

        _stormRoutine = StartCoroutine(CoStorm(data, binder));
    }

    public void StopStorm(bool keepResidue)
    {
        if (_stormRoutine != null)
        {
            StopCoroutine(_stormRoutine);
            _stormRoutine = null;
        }

        if (!keepResidue)
            return;

    }

    IEnumerator CoStorm(UltimateSequenceData data, UltimateTargetBinder binder)
    {
        Vector3 lookTarget = ResolveTargetLookPoint(binder);
        Quaternion stormRotation = binder.HasCinematicFrame
            ? binder.CinematicFrame.Rotation
            : Quaternion.identity;

        if (data.Vfx.replaceMultiSlashWithAoe && vfxPresenter.PlaySlashStormAoe(lookTarget, stormRotation))
        {
            _stormRoutine = null;
            yield break;
        }

        int slashCount = Mathf.Max(1, data.SlashCount);
        for (int i = 0; i < slashCount; i++)
        {
            UltimateSequenceData.SlashStepData step = data.GetSlashStep(i);
            Vector3 slashPosition = binder.GetSlashPosition(step);
            vfxPresenter.PlayMultiSlash(i, slashCount, step, slashPosition, lookTarget);

            float delay = Mathf.Max(0.01f, step.delay > 0f ? step.delay : data.Timings.defaultMultiSlashInterval);
            yield return new WaitForSecondsRealtime(delay);
        }

        _stormRoutine = null;
    }

    static Vector3 ResolveTargetLookPoint(UltimateTargetBinder binder)
    {
        if (binder != null && binder.HasCinematicFrame)
            return binder.CinematicFrame.TargetCenter;

        if (binder != null && binder.ActiveTarget != null && binder.ActiveTarget.TargetRoot != null)
            return binder.ActiveTarget.TargetRoot.position;

        return binder != null ? binder.PlayerRoot.position + binder.PlayerRoot.forward * 2f : Vector3.zero;
    }
}
