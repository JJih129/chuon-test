using System.Collections;
using UnityEngine;

public class ScreenFXSignalHooks : MonoBehaviour
{
    [SerializeField] UltimateScreenFX fx;
    [SerializeField] float defaultPulseStrength = 1f;
    [SerializeField] float defaultPulseDuration = 0.15f;

    public void Pulse()
    {
        if (fx != null) fx.Flash(defaultPulseStrength, defaultPulseDuration);
    }

    public void SetStrength(float v)
    {
        if (fx != null) fx.SetStrength(v);
    }

    public void FadeOutGray(float dur)
    {
        if (fx == null) return;
        StartCoroutine(FadeOut(dur));
    }

    IEnumerator FadeOut(float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            fx.SetStrength(Mathf.Lerp(1f, 0f, t / dur));
            yield return null;
        }
        fx.SetStrength(0f);
    }
}
