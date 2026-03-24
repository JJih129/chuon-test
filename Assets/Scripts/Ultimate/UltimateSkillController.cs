using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// [궁극기 컷신 컨트롤러]
/// - 타임라인(PlayableDirector)로 궁극기 컷신을 재생.
/// - 재생 중 입력 차단(CC) + 무적(Invincible) 적용.
/// - 타임라인 종료(stopped) 시 원복.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayableDirector))]
public sealed class UltimateSkillController : MonoBehaviour
{
    [Header("타임라인 | 궁극기 컷신 재생기")]
    [Tooltip("궁극기 컷신을 재생할 PlayableDirector(보통 이 컴포넌트가 붙은 오브젝트의 Director).")]
    [SerializeField] private PlayableDirector director;

    [Tooltip("재생할 타임라인 에셋(PlayableAsset). 비어있으면 Director에 이미 할당된 에셋을 사용합니다.")]
    [SerializeField] private PlayableAsset ultimateTimelineAsset;

    [Tooltip("컷신 동안 Director를 UnscaledTime(타임스케일 무시)로 돌릴지 여부.\n- 타임스케일을 0으로 멈추는 연출이면 On 권장.")]
    [SerializeField] private bool useUnscaledDirectorTime = true;

    [Header("상태 제어 | 입력 차단(CC)")]
    [Tooltip("입력 차단 컴포넌트(IInputBlocker)를 가진 MonoBehaviour를 연결하세요.\n예) SimpleInputBlocker")]
    [SerializeField] private MonoBehaviour inputBlockerBehaviour;

    [Tooltip("컷신 재생 중 전면 입력 차단을 사용할지 여부.")]
    [SerializeField] private bool blockAllInputsDuringCutscene = true;

    [Header("상태 제어 | 무적(Invincible)")]
    [Tooltip("무적 토글 컴포넌트(IInvulnerabilityToggle)를 가진 MonoBehaviour를 연결하세요.\n예) UltimateInvulnerabilityAdapter")]
    [SerializeField] private MonoBehaviour invulnerabilityToggleBehaviour;

    [Tooltip("컷신 재생 중 무적을 켤지 여부.")]
    [SerializeField] private bool setInvulnerableDuringCutscene = true;

    [Header("연출 옵션 | 시간 정지(선택)")]
    [Tooltip("컷신 동안 Time.timeScale을 0으로 만들지 여부.\n- On이면 종료 시 원래 timeScale로 복구합니다.")]
    [SerializeField] private bool freezeTimeScaleDuringCutscene = false;

    [Header("테스트/디버그")]
    [Tooltip("테스트용: R키로 컷신 재생. 최종 빌드에서는 상위 스킬 시스템에서 TryPlayUltimate() 호출 권장.")]
    [SerializeField] private bool enableDebugHotkey = false;

    [Tooltip("테스트용 핫키(기본 R).")]
    [SerializeField] private KeyCode debugHotkey = KeyCode.R;

    [Tooltip("디버그 로그 출력 여부.")]
    [SerializeField] private bool debugLog = false;

    // 프로젝트에 이미 정의된 인터페이스를 캐시해서 사용(중복 선언 금지)
    private IInputBlocker inputBlocker;
    private IInvulnerabilityToggle invulnerabilityToggle;

    private bool isCutscenePlaying;
    private float cachedPrevTimeScale = 1f;

    public bool IsCutscenePlaying => isCutscenePlaying;

    private void Reset()
    {
        director = GetComponent<PlayableDirector>();
    }

    private void Awake()
    {
        if (director == null)
            director = GetComponent<PlayableDirector>();

        // 인터페이스 캐시(런타임에서 1회만 캐스팅)
        inputBlocker = inputBlockerBehaviour as IInputBlocker;
        invulnerabilityToggle = invulnerabilityToggleBehaviour as IInvulnerabilityToggle;

        if (ultimateTimelineAsset != null)
            director.playableAsset = ultimateTimelineAsset;

        director.timeUpdateMode = useUnscaledDirectorTime
            ? DirectorUpdateMode.UnscaledGameTime
            : DirectorUpdateMode.GameTime;
    }

    private void OnEnable()
    {
        if (director != null)
            director.stopped += HandleDirectorStopped;
    }

    private void OnDisable()
    {
        if (director != null)
            director.stopped -= HandleDirectorStopped;

        // 씬 전환/비활성화 등 비정상 종료 시 안전 복구
        if (isCutscenePlaying)
            EndCutscene(force: true);
    }

    private void Update()
    {
        if (!enableDebugHotkey) return;

        if (Input.GetKeyDown(debugHotkey))
            TryPlayUltimate();
    }

    public bool TryPlayUltimate()
    {
        if (director == null || director.playableAsset == null)
        {
            if (debugLog) Debug.LogWarning("[Ultimate] Director 또는 TimelineAsset이 비어있어 재생 불가", this);
            return false;
        }

        if (isCutscenePlaying)
            return false;

        BeginCutscene();
        return true;
    }

    private void BeginCutscene()
    {
        isCutscenePlaying = true;

        // 1) 입력 차단(CC)
        if (blockAllInputsDuringCutscene && inputBlocker != null)
        {
            inputBlocker.BlockAll(true);
        }
        else
        {
            // TODO: 프로젝트 입력이 여러 Update에 분산되어 있다면,
            // 모든 입력 진입점에서 IInputBlocker.IsBlocked를 체크하도록 통일 필요.
        }

        // 2) 무적 처리
        if (setInvulnerableDuringCutscene && invulnerabilityToggle != null)
        {
            invulnerabilityToggle.SetInvulnerable(true);
        }
        else
        {
            // TODO: PlayerHealth의 isInvincible 등 기존 피격 시스템과 충돌 방지 필요.
            // 궁극기 무적은 IInvulnerabilityToggle 어댑터로 단일 경로를 권장.
        }

        // 3) (선택) 타임스케일 정지
        if (freezeTimeScaleDuringCutscene)
        {
            cachedPrevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        // 4) 타임라인 재생
        director.time = 0d;
        director.Play();

        if (debugLog) Debug.Log("[Ultimate] Cutscene Begin", this);
    }

    private void HandleDirectorStopped(PlayableDirector _)
    {
        if (!isCutscenePlaying)
            return;

        EndCutscene(force: false);
    }

    private void EndCutscene(bool force)
    {
        if (force && director != null && director.state == PlayState.Playing)
            director.Stop();

        if (freezeTimeScaleDuringCutscene)
            Time.timeScale = cachedPrevTimeScale;

        if (setInvulnerableDuringCutscene && invulnerabilityToggle != null)
            invulnerabilityToggle.SetInvulnerable(false);

        if (blockAllInputsDuringCutscene && inputBlocker != null)
            inputBlocker.BlockAll(false);

        isCutscenePlaying = false;

        if (debugLog) Debug.Log("[Ultimate] Cutscene End", this);

        // TODO:
        // - 게이지 소모/재충전 처리
        // - 컷신 종료 후 FSM 상태 원복(있다면)
        // - 타임라인 Signal로 피니시 타격/이펙트 이벤트 연동 권장
    }
}