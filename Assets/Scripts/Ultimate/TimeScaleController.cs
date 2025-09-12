using System.Collections;
using UnityEngine;

public class TimeScaleController : MonoBehaviour
{
    Coroutine co;

    public void SetTimeScale(float toScale, float blendDuration = 0.1f)
    {
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(LerpTimeScale(toScale, blendDuration));
    }

    public void Restore(float blendDuration = 0.2f)
    {
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(LerpTimeScale(1f, blendDuration));
    }

    IEnumerator LerpTimeScale(float target, float dur)
    {
        float start = Time.timeScale;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        Time.timeScale = target;
        co = null;
    }
}
