using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static float GlobalStrength { get; private set; } = 1f;

    Vector3 _restLocalPosition;
    float _remainingDuration;
    float _currentAmplitude;
    bool _initialized;

    public static void SetGlobalStrength(float value)
    {
        GlobalStrength = Mathf.Clamp01(value);
    }

    public void Shake(float amplitude, float duration)
    {
        float scaledAmplitude = amplitude * GlobalStrength;
        if (scaledAmplitude <= 0f || duration <= 0f)
            return;

        EnsureInitialized();
        enabled = true;

        if (_remainingDuration <= 0f || scaledAmplitude >= _currentAmplitude)
        {
            _currentAmplitude = scaledAmplitude;
            _remainingDuration = duration;
        }
        else
        {
            _remainingDuration = Mathf.Max(_remainingDuration, duration);
        }
    }

    void Awake()
    {
        EnsureInitialized();
        enabled = false;
    }

    void LateUpdate()
    {
        EnsureInitialized();

        if (_remainingDuration <= 0f)
        {
            if (transform.localPosition != _restLocalPosition)
                transform.localPosition = _restLocalPosition;
            enabled = false;
            return;
        }

        _remainingDuration -= Time.unscaledDeltaTime;

        float x = Random.Range(-1f, 1f) * _currentAmplitude;
        float y = Random.Range(-1f, 1f) * _currentAmplitude;
        transform.localPosition = _restLocalPosition + new Vector3(x, y, 0f);

        if (_remainingDuration <= 0f)
        {
            _remainingDuration = 0f;
            _currentAmplitude = 0f;
            transform.localPosition = _restLocalPosition;
        }
    }

    void EnsureInitialized()
    {
        if (_initialized)
            return;

        _restLocalPosition = transform.localPosition;
        _initialized = true;
    }
}
