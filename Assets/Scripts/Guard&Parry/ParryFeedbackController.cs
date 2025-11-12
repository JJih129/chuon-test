using System.Collections;
using UnityEngine;
using Cinemachine;

/// 패링/블록 연출 총괄(히트스톱 + 카메라 쉐이크 + VFX + SFX)
[DisallowMultipleComponent]
public class ParryFeedbackController : MonoBehaviour
{
    // ① 히트스톱(Time.timeScale)
    [Header("① 히트스톱(전역 Time.timeScale)")]
    [SerializeField] bool useHitStop = true;                 // 사용 여부
    [Range(0f,1f)] [SerializeField] float parryTimeScale = 0.05f;
    [SerializeField] float parryStopDuration = 0.08f;
    [Range(0f,1f)] [SerializeField] float blockTimeScale = 0.25f;
    [SerializeField] float blockStopDuration = 0.05f;
    [SerializeField] bool scaleFixedDeltaTime = true;

    // ② 카메라 쉐이크(Cinemachine)
    [Header("② 카메라 쉐이크(Cinemachine)")]
    [SerializeField] CinemachineImpulseSource impulseSource; // Player 쪽에 추가한 소스
    [SerializeField] Vector3 parryVelocity = new(0f, -1f, 0f);
    [SerializeField] Vector3 blockVelocity = new(0f, -0.6f, 0f);
    [Range(0f, 3f)] [SerializeField] float parryForce = 1.5f; // 힘 = velocity * force
    [Range(0f, 3f)] [SerializeField] float blockForce = 0.8f;

    // Fallback 단순 쉐이크(임펄스 미사용 시)
    [SerializeField] float fallbackShakeAmplitude = 0.2f;
    [SerializeField] float fallbackShakeDuration = 0.12f;
    [SerializeField] Transform fallbackCamera;

    // ③ VFX
    [Header("③ VFX")]
    [SerializeField] GameObject parryVfxPrefab;
    [SerializeField] GameObject blockVfxPrefab;
    [SerializeField] float vfxYOffset = 0.1f;
    [SerializeField] bool parentVfxToPlayer = false;

    // ④ SFX
    [Header("④ SFX")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip parryClip;
    [SerializeField] AudioClip blockClip;
    [Range(0f,1f)] [SerializeField] float sfxVolume = 0.9f;

    // ⑤ 디버그
    [Header("⑤ 디버그")]
    [SerializeField] bool debugLog = false;

    float _defaultFixedDelta;
    Coroutine _hitStopCo;

    void Awake()
    {
        _defaultFixedDelta = Time.fixedDeltaTime;
        if (!fallbackCamera && Camera.main) fallbackCamera = Camera.main.transform;
    }

    // ===== 외부 호출 API(유인자) =====
    public void PlayParryFeedback(Vector3 hitPoint, Transform attacker)
    {
        if (debugLog) Debug.Log("[ParryFX] Parry", this);
        if (useHitStop) StartHitStop(parryTimeScale, parryStopDuration);
        ShakeCamera(parryVelocity, parryForce);
        SpawnVfx(parryVfxPrefab, hitPoint);
        PlaySfx(parryClip);
    }

    public void PlayBlockFeedback(Vector3 hitPoint, Transform attacker)
    {
        if (debugLog) Debug.Log("[ParryFX] Block", this);
        if (useHitStop) StartHitStop(blockTimeScale, blockStopDuration);
        ShakeCamera(blockVelocity, blockForce);
        SpawnVfx(blockVfxPrefab, hitPoint);
        PlaySfx(blockClip);
    }

    // ===== 외부 호출 API(무인자, 인스펙터 바인딩용) =====
    // 이벤트가 UnityEvent(매개변수 없음)인 경우 이걸 선택하라.
    public void PlayParryFeedback()  => PlayParryFeedback(transform.position, transform);
    public void PlayBlockFeedback()  => PlayBlockFeedback(transform.position, transform);

    // ----- 내부 -----
    void StartHitStop(float scale, float duration)
    {
        if (_hitStopCo != null) StopCoroutine(_hitStopCo);
        _hitStopCo = StartCoroutine(CoHitStop(Mathf.Clamp01(scale), Mathf.Max(0f, duration)));
    }

    IEnumerator CoHitStop(float scale, float duration)
    {
        float prevScale = Time.timeScale;
        float prevFixed = Time.fixedDeltaTime;

        Time.timeScale = Mathf.Max(0.0001f, scale);
        if (scaleFixedDeltaTime) Time.fixedDeltaTime = _defaultFixedDelta * Time.timeScale;

        yield return new WaitForSecondsRealtime(duration);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = scaleFixedDeltaTime ? _defaultFixedDelta : prevFixed;
        _hitStopCo = null;
    }

    void ShakeCamera(Vector3 dir, float force)
    {
        if (impulseSource != null)
        {
            impulseSource.GenerateImpulse(dir * Mathf.Max(0f, force));
            return;
        }
        if (!fallbackCamera) return;
        StartCoroutine(CoSimpleShake(fallbackCamera, fallbackShakeAmplitude, fallbackShakeDuration));
    }

    IEnumerator CoSimpleShake(Transform cam, float amp, float dur)
    {
        Vector3 origin = cam.localPosition;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            cam.localPosition = origin + (Vector3)Random.insideUnitCircle * amp;
            yield return null;
        }
        cam.localPosition = origin;
    }

    void SpawnVfx(GameObject prefab, Vector3 hitPoint)
    {
        if (!prefab) return;
        Vector3 pos = hitPoint; pos.y += vfxYOffset;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        if (parentVfxToPlayer) go.transform.SetParent(transform);
    }

    void PlaySfx(AudioClip clip)
    {
        if (!clip || !audioSource) return;
        audioSource.PlayOneShot(clip, sfxVolume);
    }
}
