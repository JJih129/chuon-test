// Assets/Scripts/Combat/ParryFeedbackController.cs
using System.Collections;
using UnityEngine;
using Cinemachine;

/// 패링/블록/퍼펙트회피 연출 총괄(히트스톱 + 카메라 임펄스 + VFX + SFX)
/// - 모든 강도/지속시간은 인스펙터에서 조절
/// - Cinemachine Impulse Source가 연결되면 우선 사용, 없으면 간이 쉐이크(Fallback) 사용
[DisallowMultipleComponent]
public class ParryFeedbackController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // ① 히트스톱(Time.timeScale)
    [Header("① 히트스톱(전역 Time.timeScale)")]
    [SerializeField] bool useHitStop = true;

    [Tooltip("패링 히트스톱 타임스케일"), Range(0f, 1f)]
    public float parryTimeScale = 0.05f;

    [Tooltip("패링 히트스톱 지속(Realtime)")]
    public float parryStopDuration = 0.08f;

    [Tooltip("블록 히트스톱 타임스케일"), Range(0f, 1f)]
    public float blockTimeScale = 0.25f;

    [Tooltip("블록 히트스톱 지속(Realtime)")]
    public float blockStopDuration = 0.05f;

    [Tooltip("퍼펙트 회피 히트스톱 타임스케일"), Range(0f, 1f)]
    public float dodgeTimeScale = 0.12f;

    [Tooltip("퍼펙트 회피 히트스톱 지속(Realtime)")]
    public float dodgeStopDuration = 0.12f;

    [Tooltip("히트스톱 동안 fixedDeltaTime도 함께 스케일링")]
    [SerializeField] bool scaleFixedDeltaTime = true;

    // ─────────────────────────────────────────────────────────────────────────────
    // ② 카메라 쉐이크(Cinemachine)
    [Header("② 카메라 쉐이크(Cinemachine)")]
    [SerializeField] CinemachineImpulseSource impulseSource; // Player 쪽에 추가한 소스

    [Tooltip("패링 시 임펄스 방향/세기(velocity * force)")]
    public Vector3 parryVelocity = new(0f, -1f, 0f);
    [Range(0f, 3f)] public float parryForce = 1.5f;

    [Tooltip("블록 시 임펄스 방향/세기")]
    public Vector3 blockVelocity = new(0f, -0.6f, 0f);
    [Range(0f, 3f)] public float blockForce = 0.8f;

    [Tooltip("퍼펙트 회피 시 임펄스 방향/세기(조금 길고 가볍게)")]
    public Vector3 dodgeVelocity = new(0f, -0.8f, 0f);
    [Range(0f, 3f)] public float dodgeForce = 1.2f;

    // Fallback 단순 쉐이크(임펄스 미사용 시)
    [SerializeField] float fallbackShakeAmplitude = 0.2f;
    [SerializeField] float fallbackShakeDuration = 0.12f;
    [SerializeField] Transform fallbackCamera;

    // ─────────────────────────────────────────────────────────────────────────────
    // ③ VFX
    [Header("③ VFX")]
    [SerializeField] GameObject parryVfxPrefab;
    [SerializeField] GameObject blockVfxPrefab;
    [SerializeField] GameObject dodgeVfxPrefab;
    [SerializeField] float vfxYOffset = 0.1f;
    [SerializeField] bool parentVfxToPlayer = false;

    // ─────────────────────────────────────────────────────────────────────────────
    // ④ SFX
    [Header("④ SFX")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip parryClip;
    [SerializeField] AudioClip blockClip;
    [SerializeField] AudioClip dodgeClip;
    [Range(0f, 1f)] [SerializeField] float sfxVolume = 0.9f;

    // ─────────────────────────────────────────────────────────────────────────────
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

    public void PlayPerfectDodgeFeedback(Vector3 hitPoint, Transform attacker)
    {
        if (debugLog) Debug.Log("[ParryFX] Perfect Dodge", this);
        if (useHitStop) StartHitStop(dodgeTimeScale, dodgeStopDuration);
        ShakeCamera(dodgeVelocity, dodgeForce);
        SpawnVfx(dodgeVfxPrefab, hitPoint);
        PlaySfx(dodgeClip);
    }

    // ★ PlayerDamageReceiver에서 쓰는 이름을 위한 래퍼
    public void PlayGuardBlockFeedback(Vector3 hitPoint, Transform attacker)
        => PlayBlockFeedback(hitPoint, attacker);

    // ===== 외부 호출 API(무인자, 인스펙터 바인딩용) =====
    public void PlayParryFeedback()        => PlayParryFeedback(transform.position, transform);
    public void PlayBlockFeedback()        => PlayBlockFeedback(transform.position, transform);
    public void PlayPerfectDodgeFeedback() => PlayPerfectDodgeFeedback(transform.position, transform);
    public void PlayGuardBlockFeedback()   => PlayGuardBlockFeedback(transform.position, transform);

    // ─────────────────────────────────────────────────────────────────────────────
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
        Vector3 pos = hitPoint;
        pos.y += vfxYOffset;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        if (parentVfxToPlayer) go.transform.SetParent(transform);
    }

    void PlaySfx(AudioClip clip)
    {
        if (!clip || !audioSource) return;
        audioSource.PlayOneShot(clip, sfxVolume);
    }
}
