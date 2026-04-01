using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerDodgeController : MonoBehaviour
{
    [Header("① 참조 (한글 설명)")]
    [SerializeField] Transform playerRoot;          // [조절값] 플레이어 루트 트랜스폼 (없으면 자기 자신)
    [SerializeField] Transform cameraTransform;     // [조절값] 회피 방향 계산에 사용할 카메라 트랜스폼
    [SerializeField] PlayerMoveController move;     // [조절값] 일반 이동 컴포넌트 참조(있으면 사용)
    [SerializeField] PlayerLockOn playerLockOn;     // [조절값] 락온 상태에서 방향 기준용
    [SerializeField] Animator animator;             // [조절값] 회피 애니메이션 재생용 애니메이터
    [SerializeField] CombatMoveLocker moveLocker;   // [조절값] 회피 중 이동 잠금 전담 컴포넌트

    [Header("② 입력 (한글 설명)")]
    [SerializeField] KeyCode dodgeKey = KeyCode.LeftShift; // [조절값] 회피 입력 키

    [Header("③ 이동 조절(속도/거리 중 택1) (한글 설명)")]
    [Tooltip("끄면 '속도 기반', 켜면 '거리 기반' 회피")]
    [SerializeField] bool useDistanceBased = false;            // [조절값] true=거리 기반, false=속도 기반
    [SerializeField, Range(4f, 28f)]  float dodgeSpeed = 18f;  // [조절값] 속도 기반 회피시 이동 속도(m/s)
    [SerializeField, Range(1f, 12f)]  float dodgeDistance = 5f;// [조절값] 거리 기반 회피시 총 이동 거리(m)
    [SerializeField, Range(0.05f, .6f)] float dodgeDuration = 0.25f; // [조절값] 회피에 걸리는 시간(초)
    [SerializeField, Range(0f, 1f)]   float dodgeCooldown = 0f;      // [조절값] 회피 쿨타임(초)

    [Header("④ 속도 곡선(0~1) (한글 설명)")]
    [Tooltip("시간 정규화 t(0~1)에 대한 속도 배율")]
    [SerializeField] AnimationCurve speedCurve = AnimationCurve.Linear(0, 1, 1, 1); // [조절값] 회피 속도 곡선

    [Header("⑤ 애니 파라미터 (한글 설명)")]
    [SerializeField] string p_IsDodging = "IsDodging";  // [조절값] 회피 중 여부 Bool 파라미터 이름
    [SerializeField] string p_DodgeTrigger = "Dodge";   // [조절값] 회피 트리거 파라미터 이름

    [Header("⑥ 이동 잠금 옵션(대시 동안) (한글 설명)")]
    [Tooltip("회피 진행 중 일반 이동 금지")]
    [SerializeField] bool lockMoveDuringDodge = true;   // [조절값] 회피 중 이동 잠금 여부
    [SerializeField] bool zeroVelocityOnDodge = true;   // [조절값] 회피 시작 시 속도를 0으로 만들지 여부
    [SerializeField] bool disableRootMotionOnDodge = true; // [조절값] 회피 중 루트모션 비활성화 여부

    [Header("⑦ 이벤트 (한글 설명)")]
    public UnityEvent OnDodgeStart; // [조절값] 회피 시작 시 호출되는 이벤트(외부 연동용)
    public UnityEvent OnDodgeEnd;   // [조절값] 회피 종료 시 호출되는 이벤트(외부 연동용)

    [Header("⑧ 회피 사운드 설정 (한글 설명)")]
    [Tooltip("회피 시작 시 재생할 사운드를 출력할 AudioSource (비워두면 자동 생성/검색)")]
    [SerializeField] AudioSource dodgeAudioSource;          // [조절값] 회피 사운드용 오디오 소스

    [Tooltip("회피 시작 시 재생할 사운드 클립들 (여러 개 넣으면 랜덤 재생)")]
    [SerializeField] AudioClip[] dodgeStartClips;           // [조절값] 회피 시작 SFX 리스트

    [Tooltip("회피 종료 시 재생할 사운드 클립들 (선택, 비워두면 사용 안 함)")]
    [SerializeField] AudioClip[] dodgeEndClips;             // [조절값] 회피 종료 SFX 리스트

    [Tooltip("회피 사운드 전체 볼륨 (0~1)")]
    [SerializeField, Range(0f, 1f)] float dodgeVolume = 1f; // [조절값] 회피 사운드 볼륨

    [Tooltip("같은 사운드를 반복 재생할 때 단조로움을 줄이기 위한 랜덤 피치 범위 (x=최소, y=최대). 둘 다 1이면 고정 피치.")]
    [SerializeField] Vector2 dodgePitchRandomRange = new Vector2(0.95f, 1.05f); // [조절값] 피치 랜덤 범위

    [Tooltip("회피 종료 시에도 사운드를 재생할지 여부 (true면 종료 SFX 사용)")]
    [SerializeField] bool playEndSound = false;             // [조절값] 회피 종료 사운드 사용 여부

    // 내부
    CharacterController cc;
    float cdTimer;
    bool isDodging;
    float elapsed;
    Vector3 dodgeDir;
    float baseSpeed;

    public bool IsDodging => isDodging;

    void Reset()
    {
        if (!playerRoot) playerRoot = transform;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!move) move = GetComponent<PlayerMoveController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!moveLocker) moveLocker = GetComponent<CombatMoveLocker>();
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!playerRoot) playerRoot = transform;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!move) move = GetComponent<PlayerMoveController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!moveLocker) moveLocker = GetComponent<CombatMoveLocker>();

        EnsureDodgeAudioSource();
    }

    void Update()
    {
        cdTimer -= Time.unscaledDeltaTime;

        if (isDodging)
        {
            TickDodge();
            return;
        }

        if (Input.GetKeyDown(dodgeKey) && cdTimer <= 0f)
            StartDodge();
    }

    void StartDodge()
    {
        // 방향 결정(락온시: 캐릭터 기준, 아니면 카메라 기준)
        Vector2 in2 = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        Transform basis = (playerLockOn && playerLockOn.HasTarget) ? playerRoot
                             : (cameraTransform ? cameraTransform : playerRoot);
        Vector3 fwd = Flat(basis.forward), rgt = Flat(basis.right);
        Vector3 wish = (rgt * in2.x + fwd * in2.y);
        dodgeDir = (wish.sqrMagnitude > 0.001f) ? wish.normalized : Flat(playerRoot.forward);

        baseSpeed = useDistanceBased
            ? Mathf.Max(0.01f, dodgeDistance / Mathf.Max(0.01f, dodgeDuration))
            : dodgeSpeed;

        isDodging = true;
        elapsed = 0f;
        cdTimer = dodgeCooldown;

        // ★ 대시 동안 일반 이동 잠금
        if (lockMoveDuringDodge && moveLocker)
            moveLocker.Lock("DODGE", dodgeDuration, zeroVelocityOnDodge, disableRootMotionOnDodge);

        if (animator)
        {
            if (!string.IsNullOrEmpty(p_IsDodging)) animator.SetBool(p_IsDodging, true);
            if (!string.IsNullOrEmpty(p_DodgeTrigger)) animator.SetTrigger(p_DodgeTrigger);
        }

        // 회피 시작 사운드 재생
        PlayDodgeStartSound();

        OnDodgeStart?.Invoke();
    }

    void TickDodge()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, dodgeDuration));
        float instSpeed = baseSpeed * Mathf.Max(0f, speedCurve.Evaluate(t));

        // 대시 이동(일반 이동 컴포넌트는 잠겨있어야 함)
        cc.Move(dodgeDir * instSpeed * Time.deltaTime);

        if (elapsed >= dodgeDuration) EndDodge();
    }

    void EndDodge()
    {
        isDodging = false;
        // 안전 해제(자동해제 타이머가 이미 끝나도 중복 호출 무해)
        if (lockMoveDuringDodge && moveLocker)
            moveLocker.Unlock("DODGE");

        if (animator && !string.IsNullOrEmpty(p_IsDodging)) animator.SetBool(p_IsDodging, false);

        // 회피 종료 사운드 (옵션)
        if (playEndSound)
            PlayDodgeEndSound();

        OnDodgeEnd?.Invoke();
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f) return Vector3.forward;
        return v.normalized;
    }

    // ==================== 회피 사운드 유틸 ====================

    void EnsureDodgeAudioSource()
    {
        if (dodgeAudioSource != null) return;

        // 1) 자기 오브젝트에서 AudioSource 검색
        dodgeAudioSource = GetComponent<AudioSource>();
        if (dodgeAudioSource != null) return;

        // 2) 없으면 새로 추가 (3D 사운드 기본값)
        dodgeAudioSource = gameObject.AddComponent<AudioSource>();
        dodgeAudioSource.playOnAwake  = false;
        dodgeAudioSource.spatialBlend = 1.0f;                       // 3D 사운드
        dodgeAudioSource.rolloffMode  = AudioRolloffMode.Linear;
        dodgeAudioSource.maxDistance  = 30f;
    }

    void PlayDodgeStartSound()
    {
        if (dodgeAudioSource == null) return;

        AudioClip clip = GetRandomClip(dodgeStartClips);
        if (clip == null) return;

        float pitch = 1f;
        if (dodgePitchRandomRange.y >= dodgePitchRandomRange.x && dodgePitchRandomRange.y > 0f)
        {
            pitch = Random.Range(dodgePitchRandomRange.x, dodgePitchRandomRange.y);
        }

        dodgeAudioSource.pitch = pitch;
        dodgeAudioSource.PlayOneShot(clip, dodgeVolume);
    }

    void PlayDodgeEndSound()
    {
        if (dodgeAudioSource == null) return;

        AudioClip clip = GetRandomClip(dodgeEndClips);
        if (clip == null) return;

        float pitch = 1f;
        if (dodgePitchRandomRange.y >= dodgePitchRandomRange.x && dodgePitchRandomRange.y > 0f)
        {
            pitch = Random.Range(dodgePitchRandomRange.x, dodgePitchRandomRange.y);
        }

        dodgeAudioSource.pitch = pitch;
        dodgeAudioSource.PlayOneShot(clip, dodgeVolume);
    }

    AudioClip GetRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;

        int idx = Random.Range(0, clips.Length);
        return clips[idx];
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying && useDistanceBased)
        {
            Gizmos.color = new Color(0, 1, 1, 0.4f);
            var dir = playerRoot ? Flat(playerRoot.forward) : Vector3.forward;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.05f, dir * dodgeDistance);
        }
    }
#endif
}
