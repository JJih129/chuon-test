using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// [다단 콤보 후속타 텔레그래프 / 패리 창]
// BossAttackTimingController 는 패턴(= 공격 클립)당 텔레그래프 1회와 패리 창 1개만 제공한다.
// 그래서 클립 안의 2번째 이후 Anim_ActivateHitbox 히트는 신호 없이 들어오고 패리도 불가능했다
// (QA 봇 로그상 패턴 b/e/d 후속타가 피해의 대부분).
// 이 파트는 재생 중인 공격 클립의 애니메이션 이벤트 시각을 읽어서, 각 후속타 직전에
//   1) 오프너보다 작고 짧은 손 텔레그래프 링 + 무기 플래시 (색 언어: 패리 금색 / 회피 빨강 / 가드불가 마젠타)
//   2) 오프너보다 약간 좁은 패리 창 (attackHitbox.canParry 를 패턴 원래 값으로 잠깐 복원)
// 을 준다. 패턴이 원래 패리 불가면 canParry 는 건드리지 않는다 (회피 전용/가드불가 유지).
// 기본값 off → 기존 씬(MainScene 등) 동작은 그대로이며, 씬의 보스에서 enablePerHitStringCues 를 켠다.
// BossController.cs 쪽 변경은 훅 3줄(partial 선언, Start/Stop 호출)뿐이다.
public partial class BossController
{
    [Header("Per-Hit String Cues (다단 후속타 개별 신호)")]
    [Tooltip("다단 콤보의 2타 이후에도 개별 플래시 + 패리 창을 준다.")]
    [SerializeField] private bool enablePerHitStringCues = false;
    [Tooltip("후속타 몇 초 전에 플래시(큐)를 시작할지.")]
    [SerializeField, Min(0.05f)] private float perHitCueLeadTime = 0.24f;
    [Tooltip("후속타 플래시 길이 (오프너보다 짧게).")]
    [SerializeField, Min(0.04f)] private float perHitCueFlashDuration = 0.14f;
    [Tooltip("후속타 손 텔레그래프(링) 크기 배율 (오프너 = 1).")]
    [SerializeField, Range(0.2f, 1f)] private float perHitCueRingScale = 0.6f;
    [Tooltip("후속타 패리 창: 히트 몇 초 전부터 canParry 를 연다.")]
    [SerializeField, Min(0f)] private float perHitParryLeadTime = 0.12f;
    [Tooltip("후속타 패리 창: 히트 후 몇 초까지 유지.")]
    [SerializeField, Min(0f)] private float perHitParryTailTime = 0.08f;
    [SerializeField] private string perHitActivateEventName = "Anim_ActivateHitbox";
    [Tooltip("오프너의 손 텔레그래프 링도 타입 색(패리 금색/회피 빨강/가드불가 마젠타)으로 칠한다.")]
    [SerializeField] private bool tintOpenerHandTelegraphByType = false;

    const float PerHitClipResolveTimeout = 0.45f;
    static readonly Dictionary<AnimationClip, float[]> s_perHitClipTimes = new Dictionary<AnimationClip, float[]>();
    readonly List<AnimatorClipInfo> _perHitClipInfos = new List<AnimatorClipInfo>(4);
    Coroutine _perHitCueRoutine;
    bool _perHitParryWindowOpen;
    int _perHitOpenHitIndex = -1;   // 지금 패리 창이 열린 후속타 번호 (0 = 오프너)
    int _perHitTotalHits;
    float _perHitOpenChangedAt;

    [Header("String Third-Hit Parry (기획: 삼격참 3타 패링 → 즉시 무력화)")]
    [Tooltip("3타 이상 연속기의 3번째 타를 패링하면 짧은 그로기 (경직 시간 대체)")]
    [SerializeField] private bool enableThirdHitParryStagger = false;
    [SerializeField, Min(0.2f)] private float thirdHitParryStaggerDuration = 1.6f;
    [SerializeField, Min(0f)] private float thirdHitParryBreakBonus = 12.5f;
    public bool EnableThirdHitParryStagger { get => enableThirdHitParryStagger; set => enableThirdHitParryStagger = value; }
    public int QaThirdHitStaggers { get; private set; }
    public static event System.Action<BossController> ThirdHitParryStaggered;

    /// <summary>지금 막 패링된 타가 3타 이상 연속기의 3번째 타인지 (패리 창 기준).</summary>
    bool IsThirdHitOfStringParryWindow()
    {
        if (!enableThirdHitParryStagger || _perHitTotalHits < 3)
            return false;
        // 패링 판정은 창이 닫힌 직후 프레임에 들어올 수 있어 짧게 여유를 둔다
        bool recentlyOpen = _perHitParryWindowOpen || Time.time - _perHitOpenChangedAt <= 0.1f;
        return recentlyOpen && _perHitOpenHitIndex == 2;
    }

    bool _thirdHitGroggyPending;
    bool _thirdHitGroggyActive;
    static readonly int AnimParam_IsBreakForGroggy = Animator.StringToHash("IsBreak");

    // 3타 패링 그로기: 경직 동안 브레이크(무릎) 포즈를 짧게 빌려 쓴다. 경직이 끝나거나 상태가 바뀌면 해제.
    void ApplyThirdHitGroggyPoseIfPending()
    {
        if (!_thirdHitGroggyPending)
            return;
        _thirdHitGroggyPending = false;
        if (bossAnimator == null)
            return;
        bossAnimator.SetBool(AnimParam_IsBreakForGroggy, true);
        _thirdHitGroggyActive = true;
    }

    void ReleaseThirdHitGroggyPose()
    {
        if (!_thirdHitGroggyActive)
            return;
        _thirdHitGroggyActive = false;
        if (bossAnimator != null && (breakController == null || !breakController.IsInBreak))
            bossAnimator.SetBool(AnimParam_IsBreakForGroggy, false);
    }

    float ResolveStringParryStun(float baseStun)
    {
        _thirdHitGroggyPending = false;
        if (!IsThirdHitOfStringParryWindow())
            return baseStun;
        QaThirdHitStaggers++;
        if (breakController != null && !breakController.IsInBreak && thirdHitParryBreakBonus > 0f)
            breakController.AddBreak(thirdHitParryBreakBonus, BossBreakController.BreakSource.Parry);
        ThirdHitParryStaggered?.Invoke(this);
        _thirdHitGroggyPending = true;
        PlayerHUD hud = FindObjectOfType<PlayerHUD>();
        if (hud != null) hud.ShowRuntimeTelegraphMessage("STAGGER // 3타 패링 · 무력화", AttackTelegraphType.Parry, 1.1f, false, 5);
        _perHitOpenHitIndex = -1;
        return Mathf.Max(baseStun, thirdHitParryStaggerDuration);
    }

    /// <summary>QA/디버그용: 지금까지 낸 후속타 큐 수.</summary>
    public int PerHitCueCount { get; private set; }

    void StartPerHitStringCues(AttackPattern pattern)
    {
        StopPerHitStringCues();
        _perHitOpenHitIndex = -1;
        _perHitTotalHits = 0;
        if (!enablePerHitStringCues || !_hasActiveAttackTimingData || bossAnimator == null || pattern == null)
            return;

        _perHitCueRoutine = StartCoroutine(Co_PerHitStringCues(pattern, _activeAttackTimingData.telegraphType));
    }

    // StopAttackTimingController 에서 호출. (StopCoroutine 은 finally 를 보장하지 않으므로 여기서 정리)
    void StopPerHitStringCues()
    {
        if (_perHitCueRoutine != null)
        {
            StopCoroutine(_perHitCueRoutine);
            _perHitCueRoutine = null;
        }

        SetPerHitParryWindow(false);
    }

    bool IsPerHitPatternActive(AttackPattern pattern)
    {
        return _currentPattern == pattern && _hasActiveAttackTimingData && CanContinueAttackTiming();
    }

    IEnumerator Co_PerHitStringCues(AttackPattern pattern, BossAttackTelegraphType telegraphType)
    {
        // 트리거가 애니메이터에 반영될 때까지 1프레임 대기 (그 전엔 이전 상태의 클립이 잡힌다)
        yield return null;

        AnimationClip clip = null;
        float[] hits = null;
        float waited = 0f;
        while (!TryResolvePerHitClip(out clip, out hits))
        {
            if (!IsPerHitPatternActive(pattern) || waited > PerHitClipResolveTimeout)
            {
                _perHitCueRoutine = null;
                yield break;
            }

            waited += Time.deltaTime;
            yield return null;
        }

        _perHitTotalHits = hits != null ? hits.Length : 0;
        int cueIndex = 1; // 0번 히트(오프너)는 BossAttackTimingController 담당
        while (IsPerHitPatternActive(pattern) && TrySamplePerHitClip(clip, out float clipTime, out float rate))
        {
            while (cueIndex < hits.Length && (hits[cueIndex] - clipTime) / rate <= perHitCueLeadTime)
            {
                float secondsToHit = (hits[cueIndex] - clipTime) / rate;
                if (secondsToHit > -0.02f) // 프레임 드랍으로 이미 지나간 히트에는 큐를 내지 않는다
                    PlayPerHitCue(telegraphType, Mathf.Max(0f, secondsToHit), cueIndex);
                cueIndex++;
            }

            bool wantOpen = false;
            for (int i = 1; i < hits.Length; i++)
            {
                float s = (hits[i] - clipTime) / rate;
                if (s <= perHitParryLeadTime && s >= -perHitParryTailTime)
                {
                    wantOpen = true;
                    if (_perHitOpenHitIndex != i)
                    {
                        _perHitOpenHitIndex = i;
                        _perHitOpenChangedAt = Time.time;
                    }
                    break;
                }
            }

            SetPerHitParryWindow(wantOpen);

            if (cueIndex >= hits.Length && (hits[hits.Length - 1] - clipTime) / rate < -perHitParryTailTime)
                break;

            yield return null;
        }

        SetPerHitParryWindow(false);
        _perHitCueRoutine = null;
    }

    void PlayPerHitCue(BossAttackTelegraphType telegraphType, float secondsToHit, int hitIndex)
    {
        PerHitCueCount++;

        // 약한 신호: 무기 플래시만 (몸 플래시/경고음/카메라 흔들림/HUD 라벨 없음)
        const BossAttackTimingCueFlags flags = BossAttackTimingCueFlags.WeaponFlash;
        OnAttackTimingCue?.Invoke(telegraphType, secondsToHit, flags);

        float flash = Mathf.Min(perHitCueFlashDuration, Mathf.Max(0.06f, secondsToHit + 0.04f));
        BossTelegraphVfxPresenter presenter = ResolveTelegraphVfxPresenter();
        bool handled = presenter != null && presenter.PlayCue(telegraphType, flash, flags, true);
        if (!handled && patternVisuals != null)
        {
            AttackTelegraphType legacyType = ConvertAttackTimingTelegraph(telegraphType);
            if (legacyType != AttackTelegraphType.Auto)
                patternVisuals.StartVisualCue(legacyType, flash);
        }

        // 가장 잘 보이는 신호는 손 텔레그래프(링). 후속타는 작게 + 짧게 + 색 언어대로 (오프너는 기본 금색/크기).
        if (handPatternTelegraph == null)
            handPatternTelegraph = GetComponentInChildren<BossPatternHandTelegraph>(true);
        if (handPatternTelegraph != null)
        {
            Color tint = presenter != null ? presenter.GetCueColor(telegraphType) : FallbackPerHitCueColor(telegraphType);
            handPatternTelegraph.Play(flash, tint, perHitCueRingScale);
        }

        if (enableAttackTimingDebugLog)
            Debug.Log($"[BossAttackTiming] per-hit cue #{hitIndex} {telegraphType} in {secondsToHit:F2}s", this);
    }

    // 오프너 손 링도 색 언어대로 (기존: 타입과 무관하게 항상 금색 → 회피 전용 공격이 패리 가능처럼 보였다). 기본 off.
    bool TryPlayTintedOpenerHandTelegraph(BossAttackTelegraphType telegraphType, float duration)
    {
        if (!tintOpenerHandTelegraphByType || duration <= 0.01f)
            return false;

        if (handPatternTelegraph == null)
            handPatternTelegraph = GetComponentInChildren<BossPatternHandTelegraph>(true);
        if (handPatternTelegraph == null)
            return false;

        BossTelegraphVfxPresenter presenter = ResolveTelegraphVfxPresenter();
        Color tint = presenter != null ? presenter.GetCueColor(telegraphType) : FallbackPerHitCueColor(telegraphType);
        handPatternTelegraph.Play(duration, tint, 1f);
        return true;
    }

    static Color FallbackPerHitCueColor(BossAttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case BossAttackTelegraphType.DodgeOnly:
            case BossAttackTelegraphType.Ranged:
                return new Color(1f, 0.16f, 0.12f);  // red: 회피 전용
            case BossAttackTelegraphType.Unblockable:
                return new Color(1f, 0.2f, 0.85f);   // magenta: 가드 불가
            case BossAttackTelegraphType.Heavy:
                return new Color(1f, 0.6f, 0.04f);   // amber: 강공격 (회피 전용 red 와 구분)
            default:
                return new Color(1f, 0.78f, 0.12f);  // gold: 패리 가능
        }
    }

    void SetPerHitParryWindow(bool open)
    {
        if (open)
        {
            // 오프너 창을 타이밍 데이터가 관리하는 경우에만 (= 패턴 원래 canParry 값을 캐시해 둔 경우)
            if (!_attackTimingControlsParryWindow || !_attackTimingHasCachedCanParry || attackHitbox == null)
                return;

            _perHitParryWindowOpen = true;
            OpenAttackTimingParryWindow(); // 매 프레임 재확인 (오프너 창 종료 시점과 겹쳐도 유지)
            return;
        }

        if (!_perHitParryWindowOpen)
            return;

        _perHitParryWindowOpen = false;
        _perHitOpenChangedAt = Time.time;
        CloseAttackTimingParryWindow();
    }

    bool TryResolvePerHitClip(out AnimationClip clip, out float[] hits)
    {
        clip = null;
        hits = null;
        if (bossAnimator == null || !bossAnimator.isActiveAndEnabled)
            return false;

        // 전환 중이면 목적지(공격) 상태, 아니면 현재 상태
        if (bossAnimator.IsInTransition(0))
        {
            bossAnimator.GetNextAnimatorClipInfo(0, _perHitClipInfos);
            if (PickPerHitClip(out clip, out hits))
                return true;
        }

        bossAnimator.GetCurrentAnimatorClipInfo(0, _perHitClipInfos);
        if (!PickPerHitClip(out clip, out hits))
            return false;

        // 이미 후속타 구간을 지난 클립(이전 패턴의 잔상)이면 아직 새 클립이 아니다
        if (TrySamplePerHitClip(clip, out float clipTime, out _) && clipTime > hits[1])
        {
            clip = null;
            hits = null;
            return false;
        }

        return true;
    }

    bool PickPerHitClip(out AnimationClip clip, out float[] hits)
    {
        clip = null;
        hits = null;
        float bestWeight = 0f;
        for (int i = 0; i < _perHitClipInfos.Count; i++)
        {
            AnimationClip candidate = _perHitClipInfos[i].clip;
            float[] candidateHits = GetPerHitTimes(candidate);
            if (candidateHits.Length < 2 || _perHitClipInfos[i].weight < bestWeight)
                continue;

            bestWeight = _perHitClipInfos[i].weight;
            clip = candidate;
            hits = candidateHits;
        }

        return clip != null;
    }

    bool TrySamplePerHitClip(AnimationClip clip, out float clipTime, out float rate)
    {
        clipTime = 0f;
        rate = 1f;
        if (clip == null || bossAnimator == null)
            return false;

        AnimatorStateInfo info;
        if (bossAnimator.IsInTransition(0) && ClipInfosContain(bossAnimator, clip, true))
            info = bossAnimator.GetNextAnimatorStateInfo(0);
        else if (ClipInfosContain(bossAnimator, clip, false))
            info = bossAnimator.GetCurrentAnimatorStateInfo(0);
        else
            return false; // 공격 클립을 벗어남

        clipTime = info.normalizedTime * clip.length;
        rate = Mathf.Max(0.05f, Mathf.Abs(info.speed * info.speedMultiplier * bossAnimator.speed));
        return true;
    }

    bool ClipInfosContain(Animator animator, AnimationClip clip, bool next)
    {
        if (next)
            animator.GetNextAnimatorClipInfo(0, _perHitClipInfos);
        else
            animator.GetCurrentAnimatorClipInfo(0, _perHitClipInfos);

        for (int i = 0; i < _perHitClipInfos.Count; i++)
        {
            if (_perHitClipInfos[i].clip == clip)
                return true;
        }

        return false;
    }

    float[] GetPerHitTimes(AnimationClip clip)
    {
        if (clip == null)
            return System.Array.Empty<float>();

        if (s_perHitClipTimes.TryGetValue(clip, out float[] cached))
            return cached;

        AnimationEvent[] events = clip.events; // 할당이 있으므로 클립당 1회만
        List<float> times = new List<float>(8);
        for (int i = 0; i < events.Length; i++)
        {
            // 일부 클립(Attack_F)은 BossController.ActivateHitbox 를 직접 호출하는 "ActivateHitbox" 이벤트를 쓴다
            if (events[i].functionName == perHitActivateEventName || events[i].functionName == "ActivateHitbox")
                times.Add(events[i].time);
        }

        times.Sort();
        float[] result = times.ToArray();
        s_perHitClipTimes[clip] = result;
        return result;
    }
}
