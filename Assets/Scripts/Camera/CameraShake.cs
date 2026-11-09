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

        // 소울라이크 카메라 리그가 있는 씬: 메인 카메라 흔들림은 리그가 위치 노이즈(누적 상한)로 처리한다.
        // (Brain 이 매 프레임 메인 카메라 Transform 을 덮어쓰므로 여기서 localPosition 을 흔들면 순서에 따라 묻힌다)
        if (_isMainCamera && SoulsCameraRig.TryRouteShake(scaledAmplitude, duration))
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

    bool _isMainCamera;

    void Awake()
    {
        EnsureInitialized();
        _isMainCamera = GetComponent<Unity.Cinemachine.CinemachineBrain>() != null;
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
