using UnityEngine;
using System.Collections;

public class CameraShake : MonoBehaviour
{
    public void Shake(float amplitude, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(ShakeCoroutine(amplitude, duration));
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
