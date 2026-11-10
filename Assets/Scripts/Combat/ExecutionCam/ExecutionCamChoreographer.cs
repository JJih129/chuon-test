// 처형 전용 카메라 연출기. "카메라는 플레이어를 놀라게 하지 않는다"(Docs/CameraDesign_Soulslike.md)는 게임플레이 규칙이고,
// 처형 1.2~3.8 s 동안만 짧은 샷 리스트(스냅 줌 → 저각 궤도 돌리 → 타격 슬로모 푸시 인 → 임팩트 프레임 → 등 뒤 복귀)를 재생한다.
//
// 연결 (ExecutionDirector 2~3줄, iterX/hook.patch):
//   bool choreo = ExecutionCamChoreographer.Play(this, kind, player, victim, focus, tm.strike, tm.duration, tm.slowScale, tm.slowRealtime);
//   if (bossKind && !choreo) ExecutionCameraShot.Begin(...);     // 옵션 끔 / 연출 중이면 기존 카메라
//   Cleanup(): ExecutionCamChoreographer.Stop(this);
// 타격 순간은 ExecutionDirector.Struck 이벤트로 받는다 (추가 훅 없음).
//
// 시간: 카메라는 실시간(unscaled) 기준. 타격 전 구간 = 예상 타격 시각, 타격 후 구간 = 남은 게임 시간 + 슬로모로 늘어난 실시간.
//       실제 타격이 일찍/늦게 와도 타격 이벤트에서 타격 후 샷으로 넘어간다 (임팩트 프레임이 이음매를 가린다).
// 속도 램프: GameTimeScale.Request (최솟값 결합) — 디렉터의 0.2 홀드가 끝나면 rampScale → 1 로 부드럽게 풀린다.
// 복귀: vcam 을 끄면 SoulsCameraRig 가 0.35 s EaseInOut 으로 플레이어 등 뒤(락온 중이었으면 락온 복원)로 돌아온다.
//       마지막 샷이 이미 그 구도 근처로 가 있으므로 복귀가 짧고 부드럽다.
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

[DefaultExecutionOrder(-40)]   // CinemachineBrain(LateUpdate) 보다 먼저 vcam 을 배치
public sealed class ExecutionCamChoreographer : MonoBehaviour
{
    const int ActivePriority = 150;   // ExecutionCameraShot(140)·궁극기(100) 위, 게임플레이(95) 위

    static ExecutionCamChoreographer s_instance;
    static readonly int[] s_lastVariant = { -1, -1, -1, -1, -1, -1 };
    static int s_playCount;
    static bool s_blendHookInstalled;
    static CinemachineCore.GetBlendOverrideDelegate s_prevBlendOverride;

    // QA
    public static bool IsActive => s_instance != null && s_instance._owner != null;
    public static int PlayCount { get; private set; }
    public static string LastSummary { get; private set; } = "none";
    public static ExecCamType LastType { get; private set; }
    public static string LastVariant { get; private set; } = "";
    /// <summary>QA/미리보기: 0 이상이면 다음 Play 에서 이 변형 번호를 강제.</summary>
    public static int QaForceVariant = -1;
    public static event System.Action<ExecCamType, string> Played;

    CinemachineCamera _vcam;
    readonly ExecCamTimeline _timeline = new ExecCamTimeline();
    object _owner;
    ExecCamTypeEntry _entry;
    ExecCamType _type;
    bool _calm;
    Transform _attacker;
    Transform _victim;
    Transform _focus;
    Vector3 _focusSmoothed;
    Vector3 _focusVelocity;

    float _preElapsed;
    float _preDuration;
    float _postElapsed;
    float _postDuration;
    bool _impacted;
    int _freezeFrames;
    float _impactAt;
    float _joltAmp;
    float _joltStart;
    float _fovKick;
    bool _rampActive;
    int _lastShot = -1;
    ExecCamOutput _out;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_instance = null;
        for (int i = 0; i < s_lastVariant.Length; i++)
            s_lastVariant[i] = -1;
        s_playCount = 0;
        PlayCount = 0;
        LastSummary = "none";
        QaForceVariant = -1;
        s_blendHookInstalled = false;
        s_prevBlendOverride = null;
    }

    // ======================================================================= 정적 API

    /// <summary>
    /// 처형 카메라 시작. false = 옵션 끔/연출 중/대상 없음 → 호출자가 기존 카메라를 쓴다.
    /// strike/duration 은 ExecutionDirector Timing(게임 시간), slowScale/slowRealtime 은 디렉터 타격 슬로모.
    /// </summary>
    public static bool Play(object owner, ExecutionKind kind, Transform attacker, Transform victim, Transform focus,
        float strike, float duration, float slowScale = 1f, float slowRealtime = 0f)
    {
        if (owner == null || attacker == null || victim == null || kind == ExecutionKind.None)
            return false;
        if (!ExecutionCamSettings.Enabled || CinematicDirector.IsPlaying)
            return false;
        ExecCamType type = ResolveType(kind, victim);
        ExecutionCamLibrary lib = ExecutionCamLibrary.Load();
        ExecCamTypeEntry entry = lib != null ? lib.Get(type) : null;
        if (entry == null)
            return false;
        Camera main = GameplaySceneCache.ResolveMainCamera();
        if (main == null || main.GetComponent<CinemachineBrain>() == null)
            return false;

        ExecutionCamChoreographer c = EnsureInstance();
        c.Begin(owner, type, entry, lib, attacker, victim, focus, strike, duration, slowScale, slowRealtime, main);
        return true;
    }

    /// <summary>처형 종료(또는 정리). 다른 소유자 호출은 무시.</summary>
    public static void Stop(object owner)
    {
        if (s_instance == null || s_instance._owner == null || (owner != null && s_instance._owner != owner))
            return;
        s_instance.EndInternal();
    }

    public static ExecCamType ResolveType(ExecutionKind kind, Transform victim)
    {
        switch (kind)
        {
            case ExecutionKind.BossFront: return ExecCamType.BossFront;
            case ExecutionKind.BossBack: return ExecCamType.BossBack;
            case ExecutionKind.BossFinal: return ExecCamType.BossFinal;
            case ExecutionKind.Takedown: return ExecCamType.Takedown;
            default:
                return victim != null && victim.GetComponentInParent<ShieldEnforcerBrain>() != null ? ExecCamType.Enforcer : ExecCamType.Android;
        }
    }

    static ExecutionCamChoreographer EnsureInstance()
    {
        if (s_instance != null)
            return s_instance;
        var go = new GameObject("ExecutionCamChoreographer");
        s_instance = go.AddComponent<ExecutionCamChoreographer>();
        s_instance._vcam = go.AddComponent<CinemachineCamera>();
        s_instance._vcam.Priority = 0;
        s_instance._vcam.enabled = false;
        InstallBlendHook();
        return s_instance;
    }

    // 브레인 → 이 vcam 진입은 컷 (스냅 줌이 진입을 대신한다. 움직임 줄이기는 내부 0.3 s 블렌드)
    static void InstallBlendHook()
    {
        if (s_blendHookInstalled)
            return;
        s_blendHookInstalled = true;
        s_prevBlendOverride = CinemachineCore.GetBlendOverride;
        CinemachineCore.GetBlendOverride = BlendOverride;
    }

    static CinemachineBlendDefinition BlendOverride(ICinemachineCamera from, ICinemachineCamera to, CinemachineBlendDefinition def, Object owner)
    {
        if (s_prevBlendOverride != null)
            def = s_prevBlendOverride(from, to, def, owner);
        if (s_instance != null && s_instance._vcam != null && ReferenceEquals(to, s_instance._vcam))
            return new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        return def;
    }

    // ======================================================================= 실행

    void Begin(object owner, ExecCamType type, ExecCamTypeEntry entry, ExecutionCamLibrary lib, Transform attacker, Transform victim, Transform focus,
        float strike, float duration, float slowScale, float slowRealtime, Camera main)
    {
        if (_owner != null)
            EndInternal();
        _owner = owner;
        _type = type;
        _entry = entry;
        _calm = ExecutionCamSettings.ReduceMotion;
        _attacker = attacker;
        _victim = victim;
        _focus = focus != null ? focus : victim;
        _impacted = false;
        _freezeFrames = 0;
        _preElapsed = 0f;
        _postElapsed = 0f;
        _joltAmp = 0f;
        _fovKick = 0f;
        _lastShot = -1;

        // 실시간 길이 예측
        float scaleNow = Mathf.Max(0.05f, Time.timeScale);
        _preDuration = Mathf.Max(0.2f, strike / scaleNow);
        _postDuration = PredictPostRealtime(entry, strike, duration, slowScale, slowRealtime, _calm);

        ExecCamContext ctx = BuildContext(lib, type, attacker, victim, _focus, main != null ? main.aspect : 16f / 9f);

        // 시작 화면 = 지금 메인 카메라 (움직임 줄이기의 진입 블렌드 기준)
        Transform mt = main.transform;
        var initial = new ExecCamOutput { position = mt.position, rotation = mt.rotation, look = mt.position + mt.forward * 4f, fov = main.fieldOfView, dutch = 0f, valid = true };

        ExecCamVariant variant;
        int variantIndex;
        if (_calm)
        {
            variant = entry.calm != null && entry.calm.shots != null && entry.calm.shots.Count > 0 ? entry.calm : entry.variants[0];
            variantIndex = -1;
        }
        else
            variant = PickVariant(lib, entry, ref ctx, out variantIndex);

        _timeline.Begin(lib, variant, ctx, initial);
        _focusSmoothed = ctx.impactPoint;
        _focusVelocity = Vector3.zero;

        if (variantIndex >= 0)
            s_lastVariant[(int)type] = variantIndex;
        s_playCount++;
        PlayCount++;
        LastType = type;
        LastVariant = variant != null ? variant.name : "";
        LastSummary = $"{type}/{LastVariant} side={(ctx.side >= 0f ? "R" : "L")} pre={_preDuration:0.00}s post={_postDuration:0.00}s calm={_calm}";

        CopyLens(main);
        _out = _timeline.Evaluate(ExecCamPhase.PreImpact, 0f, 0f);
        ApplyToVcam(_out, Vector3.zero, 0f);
        _vcam.enabled = true;
        _vcam.Priority = ActivePriority;

        ExecutionDirector.Struck -= HandleStruck;
        ExecutionDirector.Struck += HandleStruck;
        Played?.Invoke(type, LastVariant);
    }

    /// <summary>타격 후 실시간 길이 예측: 남은 게임 시간 + 디렉터 슬로모 + 이 연출기의 램프로 늘어난 시간.</summary>
    public static float PredictPostRealtime(ExecCamTypeEntry entry, float strike, float duration, float slowScale, float slowRealtime, bool calm)
    {
        float post = Mathf.Max(0.2f, duration - strike);
        if (slowScale < 0.999f && slowRealtime > 0f)
            post += slowRealtime * (1f - slowScale);
        if (!calm && entry != null && entry.rampScale < 0.999f)
        {
            float extraHold = Mathf.Max(0f, entry.rampHold - (slowScale < 0.999f ? slowRealtime : 0f));
            post += extraHold * (1f - entry.rampScale) + entry.rampOut * (1f - entry.rampScale) * 0.5f;
        }
        return post;
    }

    public static ExecCamContext BuildContext(ExecutionCamLibrary lib, ExecCamType type, Transform attacker, Transform victim, Transform focus, float aspect)
    {
        PlayerReferences refs = attacker.GetComponentInParent<PlayerReferences>();
        Transform attackerRoot = refs != null && refs.PlayerRoot != null ? refs.PlayerRoot : attacker;
        bool boss = type == ExecCamType.BossFront || type == ExecCamType.BossBack || type == ExecCamType.BossFinal;
        Bounds vb = MeasureBounds(victim, boss ? 3.2f : 1.8f);
        Bounds ab = MeasureBounds(attackerRoot, 1.75f);
        var ctx = new ExecCamContext
        {
            attackerFeet = attackerRoot.position,
            victimFeet = victim.position,
            attackerHeight = Mathf.Clamp(ab.size.y, 1.3f, 2.4f),
            victimHeight = Mathf.Clamp(vb.size.y, 1.3f, 7f),
            victimRadius = Mathf.Clamp(Mathf.Min(vb.extents.x, vb.extents.z), 0.3f, 1.6f),
            impactPoint = focus != null && focus != victim ? focus.position : victim.position + Vector3.up * Mathf.Clamp(vb.size.y * 0.62f, 1.0f, 3f),
            side = 1f,
            aspect = aspect > 0.1f ? aspect : 16f / 9f,
            victimSurvives = type == ExecCamType.BossFront || type == ExecCamType.BossBack,
            bigVictim = boss,
            collisionMask = SoulsCameraRig.ActiveProfile != null ? SoulsCameraRig.ActiveProfile.collisionMask : lib.collisionMask
        };
        ctx.ApplyProfile(SoulsCameraRig.ActiveProfile);
        return ctx;
    }

    /// <summary>가장 큰 스킨드/메시 렌더러 바운드 (피직스·파티클 제외). 실패 시 기본 높이.</summary>
    public static Bounds MeasureBounds(Transform root, float fallbackHeight)
    {
        Bounds best = new Bounds(root != null ? root.position + Vector3.up * fallbackHeight * 0.5f : Vector3.zero, new Vector3(0.7f, fallbackHeight, 0.7f));
        if (root == null)
            return best;
        float bestScore = -1f;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                continue;
            Vector3 s = r.bounds.size;
            float score = s.x * s.y * s.z + (r is SkinnedMeshRenderer ? 1000f : 0f);
            if (score > bestScore && s.y > 0.5f)
            {
                bestScore = score;
                best = r.bounds;
            }
        }
        return best;
    }

    public static ExecCamVariant PickVariant(ExecutionCamLibrary lib, ExecCamTypeEntry entry, ref ExecCamContext ctx, out int index)
    {
        int n = entry.variants.Count;
        index = 0;
        if (n == 0)
            return null;
        int last = s_lastVariant[(int)entry.type];
        if (QaForceVariant >= 0)
        {
            index = Mathf.Clamp(QaForceVariant, 0, n - 1);
            QaForceVariant = -1;
            ctx.side = PickSide(lib, entry.variants[index], ctx);
            return entry.variants[index];
        }
        int bestScore = int.MaxValue;
        float bestSide = 1f;
        int start = s_playCount % n;
        for (int r = 0; r < n; r++)
        {
            int i = (start + r) % n;
            ExecCamContext c = ctx;
            float side = PickSide(lib, entry.variants[i], c, out int score);
            if (i == last && n > 1)
                score += 3;   // 같은 변형 연속 금지 (공간이 압도적으로 좋을 때만 허용)
            if (score < bestScore)
            {
                bestScore = score;
                index = i;
                bestSide = side;
            }
        }
        ctx.side = bestSide;
        return entry.variants[index];
    }

    static float PickSide(ExecutionCamLibrary lib, ExecCamVariant v, ExecCamContext ctx) => PickSide(lib, v, ctx, out _);

    static float PickSide(ExecutionCamLibrary lib, ExecCamVariant v, ExecCamContext ctx, out int score)
    {
        // 기본은 오른쪽(게임플레이 숄더 쪽), 홀수 회차는 왼쪽 먼저 — 같은 점수면 번갈아
        float first = (s_playCount & 1) == 0 ? 1f : -1f;
        ctx.side = first;
        int a = ExecCamTimeline.ScoreVariant(lib, v, ctx);
        if (a == 0)
        {
            score = 0;
            return first;
        }
        ctx.side = -first;
        int b = ExecCamTimeline.ScoreVariant(lib, v, ctx);
        score = Mathf.Min(a, b);
        return b < a ? -first : first;
    }

    void CopyLens(Camera main)
    {
        SoulsCameraRig rig = SoulsCameraRig.Active;
        if (rig != null && rig.VirtualCamera != null)
            _vcam.Lens = rig.VirtualCamera.Lens;
        else if (main != null)
            _vcam.Lens = LensSettings.FromCamera(main);
        _vcam.Lens.NearClipPlane = Mathf.Min(_vcam.Lens.NearClipPlane > 0f ? _vcam.Lens.NearClipPlane : 0.1f, 0.08f);
    }

    void HandleStruck(ExecutionKind kind, Transform victim)
    {
        if (_owner == null || _impacted)
            return;
        _impacted = true;
        _impactAt = Time.unscaledTime;
        _postElapsed = 0f;
        // 예상보다 일찍 왔으면 타격 전 샷을 끝까지 당겨 둔다
        _preElapsed = Mathf.Max(_preElapsed, _preDuration);
        if (_calm)
            return;
        _freezeFrames = Mathf.Max(0, _entry.impactFreezeFrames);
        _joltAmp = _entry.impactJolt * CameraShake.GlobalStrength;
        _joltStart = Time.unscaledTime;
        _fovKick = _entry.impactFovKick;
        if (_entry.impactBwPulse > 0f && _victim != null)
        {
            // 흑백 임팩트 프레임 (흡수/펄스가 없는 종류만; 세기는 ActionColourDirector 가 섬광 슬라이더로 제한)
            ActionColourDirector.PulseAt(_focusSmoothed, _entry.impactBwPulse, 0.09f, new[] { _attacker, _victim });
        }
        if (_entry.rampScale < 0.999f)
        {
            _rampActive = true;
            GameTimeScale.Request(this, _entry.rampScale);
        }
    }

    void LateUpdate()
    {
        if (_owner == null)
            return;
        if (_victim == null || _attacker == null)
        {
            EndInternal();
            return;
        }
        // 일시정지 중에는 진행하지 않는다
        bool paused = Time.timeScale <= 0f;
        float dt = paused ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        UpdateRamp();

        if (_focus != null)
            _focusSmoothed = Vector3.SmoothDamp(_focusSmoothed, _focus.position, ref _focusVelocity, 0.06f, Mathf.Infinity, Mathf.Max(0.0001f, dt));
        _timeline.UpdateImpactPoint(_focusSmoothed);

        // 임팩트 프레임: 1~2 프레임 카메라 정지
        if (_freezeFrames > 0)
        {
            if (!paused)
                _freezeFrames--;
            ApplyToVcam(_out, Vector3.zero, _fovKick);
            return;
        }

        ExecCamOutput o;
        if (!_impacted)
        {
            _preElapsed += dt;
            o = _timeline.Evaluate(ExecCamPhase.PreImpact, Mathf.Clamp01(_preElapsed / _preDuration), dt);
        }
        else
        {
            _postElapsed += dt;
            o = _timeline.Evaluate(ExecCamPhase.PostImpact, Mathf.Clamp01(_postElapsed / _postDuration), dt);
        }
        if (_timeline.ShotIndex != _lastShot)
        {
            _lastShot = _timeline.ShotIndex;
            if (!_calm)
            {
                ExecCamShot shot = _timeline.CurrentShot;
                if (shot != null && shot.jolt > 0f)
                {
                    _joltAmp = Mathf.Max(_joltAmp, shot.jolt * CameraShake.GlobalStrength);
                    _joltStart = Time.unscaledTime;
                }
            }
        }
        _out = o;

        // 흔들림(위치 위주, 0.22 s 감쇠) + FOV 킥 복귀(0.18 s)
        Vector3 jolt = Vector3.zero;
        if (_joltAmp > 0f && !paused)
        {
            float age = Time.unscaledTime - _joltStart;
            float decay = Mathf.Clamp01(1f - age / 0.22f);
            if (decay <= 0f)
                _joltAmp = 0f;
            else
            {
                float a = _joltAmp * decay * decay;
                float tt = Time.unscaledTime * 38f;
                jolt = (o.rotation * new Vector3(Mathf.PerlinNoise(tt, 0.3f) - 0.5f, (Mathf.PerlinNoise(0.7f, tt) - 0.5f) * 0.6f, 0f)) * (2f * a);
            }
        }
        float kick = 0f;
        if (_impacted && _fovKick != 0f)
        {
            float age = Time.unscaledTime - _impactAt;
            kick = _fovKick * Mathf.Clamp01(1f - age / 0.18f);
        }
        ApplyToVcam(o, jolt, kick);
    }

    void UpdateRamp()
    {
        if (!_rampActive)
            return;
        float r = Time.unscaledTime - _impactAt;
        float hold = _entry.rampHold, outT = Mathf.Max(0.01f, _entry.rampOut);
        if (r < hold)
            GameTimeScale.Request(this, _entry.rampScale);
        else if (r < hold + outT)
            GameTimeScale.Request(this, Mathf.Lerp(_entry.rampScale, 1f, Mathf.SmoothStep(0f, 1f, (r - hold) / outT)));
        else
            ReleaseRamp();
    }

    void ReleaseRamp()
    {
        if (!_rampActive)
            return;
        _rampActive = false;
        GameTimeScale.Release(this);
    }

    void ApplyToVcam(ExecCamOutput o, Vector3 jolt, float fovKick)
    {
        if (!o.valid)
            return;
        transform.SetPositionAndRotation(o.position + jolt, o.rotation);
        _vcam.Lens.FieldOfView = Mathf.Clamp(o.fov + fovKick, 18f, 70f);
        _vcam.Lens.Dutch = o.dutch;
    }

    void EndInternal()
    {
        ExecutionDirector.Struck -= HandleStruck;
        ReleaseRamp();
        _owner = null;
        _attacker = null;
        _victim = null;
        _focus = null;
        if (_vcam != null)
        {
            _vcam.Priority = 0;
            _vcam.enabled = false;
        }
    }

    void OnDisable()
    {
        if (_owner != null)
            EndInternal();
    }

    void OnDestroy()
    {
        ExecutionDirector.Struck -= HandleStruck;
        ReleaseRamp();
        if (s_instance == this)
            s_instance = null;
    }

    // ======================================================================= QA

    public static string QaDescribe()
    {
        if (s_instance == null)
            return "no instance";
        ExecutionCamChoreographer c = s_instance;
        return $"{LastSummary} active={IsActive} shot={c._timeline.CurrentShotName} fallbacks={c._timeline.FallbackCount} safe={c._timeline.SafeShotCount} impacted={c._impacted}";
    }
}
