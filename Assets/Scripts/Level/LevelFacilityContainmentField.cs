using System.Collections;
using UnityEngine;

// Level_Facility: 하층 격리장치 연출. 상층에 있는 동안 붉은 격리 필드가 맥동하고,
// 엘리베이터 하강(또는 체크포인트 재시작)으로 하층에 도달하면 격리가 해제된다.
[DisallowMultipleComponent]
public sealed class LevelFacilityContainmentField : MonoBehaviour
{
    [SerializeField] GameObject[] disableOnRelease;
    [SerializeField] Light[] pulseLights;
    [SerializeField, Min(0f)] float pulseSpeed = 1.4f;
    [SerializeField, Range(0f, 1f)] float pulseDepth = 0.35f;

    float[] _baseIntensities;
    bool _released;

    void OnEnable()
    {
        LobbyManager.LowerFloorReached += HandleLowerFloorReached;
    }

    void OnDisable()
    {
        LobbyManager.LowerFloorReached -= HandleLowerFloorReached;
    }

    IEnumerator Start()
    {
        CacheIntensities();
        yield return null;
        LobbyManager lobby = LobbyManager.Instance;
        if (lobby != null && lobby.HasHandedOffToLowerFloor)
            Release();
    }

    void HandleLowerFloorReached(LobbyManager lobby)
    {
        Release();
    }

    void CacheIntensities()
    {
        if (pulseLights == null)
            return;

        _baseIntensities = new float[pulseLights.Length];
        for (int i = 0; i < pulseLights.Length; i++)
            _baseIntensities[i] = pulseLights[i] != null ? pulseLights[i].intensity : 0f;
    }

    void Release()
    {
        if (_released)
            return;

        _released = true;
        if (disableOnRelease == null)
            return;

        for (int i = 0; i < disableOnRelease.Length; i++)
        {
            if (disableOnRelease[i] != null)
                disableOnRelease[i].SetActive(false);
        }
    }

    void Update()
    {
        if (_released || pulseLights == null || _baseIntensities == null)
            return;

        float wave = 1f - pulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
        for (int i = 0; i < pulseLights.Length && i < _baseIntensities.Length; i++)
        {
            if (pulseLights[i] != null)
                pulseLights[i].intensity = _baseIntensities[i] * wave;
        }
    }
}
