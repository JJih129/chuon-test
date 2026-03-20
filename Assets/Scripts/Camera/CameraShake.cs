using UnityEngine;
using System.Collections;

public class CameraShake : MonoBehaviour
{
    public static float GlobalStrength { get; private set; } = 1f;

    public static void SetGlobalStrength(float value)
    {
        GlobalStrength = Mathf.Clamp01(value);
    }

    public void Shake(float amplitude, float duration)
    {
        float scaledAmplitude = amplitude * GlobalStrength;
        if (scaledAmplitude <= 0f || duration <= 0f)
            return;

        StopAllCoroutines();
        StartCoroutine(ShakeCoroutine(scaledAmplitude, duration));
    }

    IEnumerator ShakeCoroutine(float amplitude, float duration)
    {
        Vector3 originalPos = transform.localPosition;
        float timer = 0f;
        while (timer < duration)
        {
            float x = Random.Range(-1f, 1f) * amplitude;
            float y = Random.Range(-1f, 1f) * amplitude;
            transform.localPosition = originalPos + new Vector3(x, y, 0);
            timer += Time.deltaTime;
            yield return null;
        }
        transform.localPosition = originalPos;
    }
}
