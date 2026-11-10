// 반복 16: 처형 연출기 (Stellar Blade / Sekiro / Lies of P 계열). Docs/Boss_TypeD_Phases.md §3
//  보스: 브레이크 중 공격 입력 → 위치로 정면/후면 변형, HP 0 → 최종(코어 절단) → 사망 → 기존 엔딩 연출.
//  잡몹(경비 안드로이드·방패 집행관): 경직(리포스트 창) 중 1.2 s 짧은 처형. 경계 전 적 등 뒤: 잠입 암살 1.5 s.
//  공통: 흑백 흡수(두 인물 색 유지) + 타격 순간 짧은 슬로모(GameTimeScale) + 화면 베기(ScreenCut), 플레이어 무적·입력 차단.
// 애니메이션은 TypeDOverhaulBuilder 가 컨트롤러에 넣은 OH_* 상태 (GhostSamurai Execution/Executed·Ambush/Ambushed 짝, 휴머노이드).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum ExecutionKind
{
    None = 0,
    BossFront = 1,
    BossBack = 2,
    BossFinal = 3,
    Mob = 4,
    Takedown = 5
}

[DisallowMultipleComponent]
public sealed class ExecutionDirector : MonoBehaviour
{
    // 플레이어 컨트롤러(Base Layer)에 추가되는 상태
    public const string PlayerExecFront = "OH_ExecFront";
    public const string PlayerExecBack = "OH_ExecBack";
    public const string PlayerExecFinal = "OH_ExecFinal";
    public const string PlayerExecMob = "OH_ExecMob";
    public const string PlayerTakedown = "OH_Takedown";
    // 잡몹 컨트롤러(Base Layer)
    public const string MobExecuted = "OH_Executed";
    public const string MobAmbushed = "OH_Ambushed";

    [System.Serializable]
    public struct Timing
    {
        public float duration;
        public float strike;
        public float distance;   // 플레이어 → 대상 거리 (대상 정렬)
        public float victimYaw;  // 대상이 플레이어를 기준으로 도는 각 (180 = 마주 봄, 0 = 같은 방향/등)
        public float slowScale;
        public float slowRealtime;
    }

    [SerializeField] Timing bossFront = new Timing { duration = 2.5f, strike = 1.15f, distance = 1.55f, victimYaw = 180f, slowScale = 0.2f, slowRealtime = 0.32f };
    [SerializeField] Timing bossBack = new Timing { duration = 2.3f, strike = 1.0f, distance = 1.35f, victimYaw = 0f, slowScale = 0.2f, slowRealtime = 0.3f };
    [SerializeField] Timing bossFinal = new Timing { duration = 3.4f, strike = 1.6f, distance = 1.5f, victimYaw = 180f, slowScale = 0.15f, slowRealtime = 0.45f };
    [SerializeField] Timing mob = new Timing { duration = 1.2f, strike = 0.7f, distance = 1.2f, victimYaw = 180f, slowScale = 0.25f, slowRealtime = 0.12f };
    [SerializeField] Timing takedown = new Timing { duration = 1.5f, strike = 1.0f, distance = 0.9f, victimYaw = 0f, slowScale = 1f, slowRealtime = 0f };
    [SerializeField, Min(0.5f)] float mobExecutionRange = 2.6f;
    [SerializeField, Min(0.5f)] float takedownRange = 2.2f;
    [SerializeField, Range(60f, 180f)] float takedownBehindAngle = 120f;
    [SerializeField, Range(90f, 180f)] float bossBackAngle = 110f;

    static ExecutionDirector s_instance;
    public static bool IsExecuting => s_instance != null && s_instance._running;
    public static ExecutionKind CurrentKind => s_instance != null && s_instance._running ? s_instance._kind : ExecutionKind.None;
    public static int QaBossFront { get; private set; }
    public static int QaBossBack { get; private set; }
    public static int QaBossFinal { get; private set; }
    public static int QaMob { get; private set; }
    public static int QaTakedowns { get; private set; }
    /// <summary>씬 규칙이 잡몹 처형/암살을 막을 때 (튜토리얼 레슨 중 등). false = 이 대상에는 처형 프롬프트·처형 없음.</summary>
    public static System.Func<EnemyBrain, ExecutionKind, bool> MobFilter;
    public static event System.Action<ExecutionKind, Transform> Started;
    public static event System.Action<ExecutionKind, Transform> Struck;
    public static event System.Action<ExecutionKind, Transform> Finished;

    bool _running;
    ExecutionKind _kind;
    readonly List<Transform> _keep = new List<Transform>(2);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_instance = null;
        QaBossFront = QaBossBack = QaBossFinal = QaMob = QaTakedowns = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryEnsureForScene();
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => TryEnsureForScene();

    // 게임플레이 씬(플레이어가 있는 씬)이면 하나 만든다. 처형 대상이 없으면 아무것도 하지 않는다.
    static void TryEnsureForScene()
    {
        if (s_instance == null && Object.FindObjectOfType<PlayerReferences>() != null)
            Ensure();
    }

    public static ExecutionDirector Ensure()
    {
        if (s_instance != null)
            return s_instance;
        var go = new GameObject("ExecutionDirector");
        s_instance = go.AddComponent<ExecutionDirector>();
        go.AddComponent<ExecutionPromptHUD>();
        return s_instance;
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
        if (_running)
            Cleanup();
    }

    // ======================================================================= 입력 / 대상 판정

    /// <summary>PlayerCombatController 공격 입력. true = 처형 시작(또는 진행 중이라 입력 소비).</summary>
    public static bool TryBeginFromAttackInput(Transform player)
    {
        if (s_instance == null || player == null)
            return false;
        if (s_instance._running)
            return true;
        if (!s_instance.TryResolveTarget(player, out ExecutionKind kind, out Component target))
            return false;
        s_instance.StartCoroutine(s_instance.Co_Run(player, kind, target));
        return true;
    }

    /// <summary>HUD 프롬프트용: 지금 공격하면 시작될 처형.</summary>
    public static bool TryGetPrompt(Transform player, out ExecutionKind kind, out Transform target)
    {
        kind = ExecutionKind.None;
        target = null;
        if (s_instance == null || s_instance._running || player == null)
            return false;
        if (!s_instance.TryResolveTarget(player, out kind, out Component c))
            return false;
        target = c.transform;
        return true;
    }

    /// <summary>표식용: 처형 가능한 보스(거리 무관).</summary>
    public static TypeDOverhaul MarkedBoss
    {
        get
        {
            TypeDOverhaul oh = TypeDOverhaul.Active;
            return oh != null && oh.IsExecutionAvailable ? oh : null;
        }
    }

    bool TryResolveTarget(Transform player, out ExecutionKind kind, out Component target)
    {
        kind = ExecutionKind.None;
        target = null;
        PlayerHealth hp = player.GetComponentInChildren<PlayerHealth>();
        if (hp != null && hp.IsDead)
            return false;
        PlayerCombatController combat = player.GetComponentInChildren<PlayerCombatController>();
        if (combat != null && combat.IsInHit)
            return false;
        if (CinematicDirector.IsPlaying)
            return false;

        Vector3 pp = player.position;
        TypeDOverhaul oh = TypeDOverhaul.Active;
        if (oh != null && oh.IsExecutionAvailable)
        {
            Vector3 d = oh.Boss.transform.position - pp;
            d.y = 0f;
            if (d.magnitude <= oh.ExecutionRange)
            {
                if (oh.IsFinalExecutionPending)
                    kind = ExecutionKind.BossFinal;
                else
                {
                    Vector3 fromBoss = -d;
                    float angle = Vector3.Angle(Flat(oh.Boss.transform.forward), fromBoss);
                    kind = angle >= bossBackAngle ? ExecutionKind.BossBack : ExecutionKind.BossFront;
                }
                target = oh;
                return true;
            }
        }

        float best = float.PositiveInfinity;
        IReadOnlyList<EnemyBrain> mobs = EnemyBrain.Active;
        for (int i = 0; i < mobs.Count; i++)
        {
            EnemyBrain b = mobs[i];
            if (b == null || !b.isActiveAndEnabled || !(b is SecurityAndroidBrain || b is ShieldEnforcerBrain))
                continue;
            Vector3 d = b.transform.position - pp;
            float vertical = Mathf.Abs(d.y);
            d.y = 0f;
            float dist = d.magnitude;
            if (vertical > 1.2f || dist >= best)
                continue;
            if (b.CanBeExecuted && dist <= mobExecutionRange && (MobFilter == null || MobFilter(b, ExecutionKind.Mob)))
            {
                best = dist;
                kind = ExecutionKind.Mob;
                target = b;
            }
            else if (b.CanBeAmbushed && dist <= takedownRange && (MobFilter == null || MobFilter(b, ExecutionKind.Takedown)))
            {
                float behind = Vector3.Angle(Flat(b.transform.forward), -d);
                if (behind >= takedownBehindAngle)
                {
                    best = dist;
                    kind = ExecutionKind.Takedown;
                    target = b;
                }
            }
        }
        return target != null;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    // ======================================================================= 실행

    PlayerReferences _refs;
    Animator _playerAnimator;
    CharacterController _cc;
    PlayerMoveController _move;
    IInputBlocker _blocker;
    bool _blocked;
    bool _slowRequested;
    Transform _player;

    Timing ResolveTiming(ExecutionKind k)
    {
        switch (k)
        {
            case ExecutionKind.BossFront: return bossFront;
            case ExecutionKind.BossBack: return bossBack;
            case ExecutionKind.BossFinal: return bossFinal;
            case ExecutionKind.Mob: return mob;
            default: return takedown;
        }
    }

    IEnumerator Co_Run(Transform player, ExecutionKind kind, Component targetComponent)
    {
        _running = true;
        _kind = kind;
        _player = player;
        Timing tm = ResolveTiming(kind);
        TypeDOverhaul oh = targetComponent as TypeDOverhaul;
        EnemyBrain brain = targetComponent as EnemyBrain;
        Transform victim = oh != null ? oh.Boss.transform : brain.transform;
        bool bossKind = oh != null;

        _refs = player.GetComponentInParent<PlayerReferences>() ?? player.GetComponentInChildren<PlayerReferences>();
        _playerAnimator = _refs != null ? _refs.MainAnimator : player.GetComponentInChildren<Animator>();
        _cc = player.GetComponentInChildren<CharacterController>();
        _move = player.GetComponentInChildren<PlayerMoveController>();
        _blocker = player.GetComponent<IInputBlocker>() ?? player.GetComponentInChildren<IInputBlocker>();
        if (_blocker != null)
        {
            _blocker.BlockAll(true);
            _blocked = true;
        }
        if (_move != null)
            _move.SetExternalControl(true);
        PlayerHealth hp = player.GetComponentInChildren<PlayerHealth>();
        if (hp != null)
            hp.SetInvincibleRealtime(tm.duration + 0.6f);

        // 정렬: 보스는 제자리, 플레이어가 마주 선다. 잡몹/암살은 대상이 플레이어 앞으로 온다.
        Vector3 victimPos = victim.position;
        if (bossKind)
        {
            Vector3 toPlayer = Flat(player.position - victimPos);
            if (kind == ExecutionKind.BossBack)
                toPlayer = Flat(Vector3.Slerp(toPlayer, -Flat(victim.forward), 0.65f));
            else
                toPlayer = Flat(Vector3.Slerp(toPlayer, Flat(victim.forward), 0.65f));
            Vector3 playerPos = victimPos + toPlayer * (tm.distance + oh.ExecutionDistanceBonus);
            playerPos.y = player.position.y;
            PlacePlayer(playerPos, Quaternion.LookRotation(-toPlayer, Vector3.up));
            victim.rotation = Quaternion.LookRotation(kind == ExecutionKind.BossBack ? -toPlayer : toPlayer, Vector3.up);
        }
        else
        {
            // 잡몹은 NavMeshAgent 위치를 건드리지 않게 제자리에 두고, 플레이어가 정해진 거리로 선다
            Vector3 fwd = Flat(victimPos - player.position);
            Vector3 playerPos = victimPos - fwd * tm.distance;
            playerPos.y = player.position.y;
            PlacePlayer(playerPos, Quaternion.LookRotation(fwd, Vector3.up));
            victim.rotation = Quaternion.LookRotation(tm.victimYaw > 90f ? -fwd : fwd, Vector3.up);
        }

        // 애니메이션
        string playerState = kind == ExecutionKind.BossFront ? PlayerExecFront
            : kind == ExecutionKind.BossBack ? PlayerExecBack
            : kind == ExecutionKind.BossFinal ? PlayerExecFinal
            : kind == ExecutionKind.Mob ? PlayerExecMob : PlayerTakedown;
        CrossFadePlayer(playerState, 0.08f);
        if (bossKind)
        {
            oh.BeginExecution(kind, this);
            oh.Boss.PlayOverhaulState(kind == ExecutionKind.BossBack ? BossController.OverhaulStateExecutedBack
                : kind == ExecutionKind.BossFinal ? BossController.OverhaulStateExecutedFinal : BossController.OverhaulStateExecutedFront, 0.08f);
        }
        else
        {
            brain.BeginExecuted(kind == ExecutionKind.Takedown ? MobAmbushed : MobExecuted);
        }

        // 흑백 흡수 (두 인물 색 유지) + 카메라
        _keep.Clear();
        _keep.Add(_refs != null ? _refs.transform : player);
        _keep.Add(victim);
        Transform focus = bossKind && oh.CoreAnchor != null ? oh.CoreAnchor : victim;
        bool absorb = kind != ExecutionKind.Takedown && kind != ExecutionKind.Mob;
        if (absorb)
            ActionColourDirector.BeginAbsorb(this, focus, kind == ExecutionKind.BossFinal ? 0.6f : 0.35f, _keep);
        // 처형 전용 카메라 연출 (모든 처형 종류, 단일 시스템). 옵션 끔/컷신 중이면 false → 게임플레이 카메라 유지.
        // 타격 순간은 Struck 이벤트로, 종료/중단은 Stop(this) 로 넘긴다. 복귀는 SoulsCameraRig 0.35 s 블렌드.
        ExecutionCamChoreographer.Play(this, kind, player, victim, focus, tm.strike, tm.duration, tm.slowScale, tm.slowRealtime);

        switch (kind)
        {
            case ExecutionKind.BossFront: QaBossFront++; break;
            case ExecutionKind.BossBack: QaBossBack++; break;
            case ExecutionKind.BossFinal: QaBossFinal++; break;
            case ExecutionKind.Mob: QaMob++; break;
            case ExecutionKind.Takedown: QaTakedowns++; break;
        }
        Started?.Invoke(kind, victim);
        TypeDOverhaulAssets assets = TypeDOverhaulAssets.Load();

        float t = 0f;
        bool struck = false;
        while (t < tm.duration)
        {
            if (!struck && t >= tm.strike)
            {
                struck = true;
                Strike(kind, tm, oh, brain, victim, focus, assets);
            }
            if (bossKind ? oh.Boss == null : brain == null)
                break;
            t += Time.deltaTime;
            yield return null;
        }
        if (!struck && (bossKind ? oh != null && oh.Boss != null : brain != null))
            Strike(kind, tm, oh, brain, victim, focus, assets);

        if (absorb)
            ActionColourDirector.EndAbsorb(this, kind == ExecutionKind.BossFinal ? 0.25f : 0.45f);
        ExecutionCamChoreographer.Stop(this);
        if (bossKind && oh != null)
            oh.EndExecution(kind, this);
        CrossFadePlayer("Locomotion", 0.2f);
        Finished?.Invoke(kind, victim);
        Cleanup();
    }

    void Strike(ExecutionKind kind, Timing tm, TypeDOverhaul oh, EnemyBrain brain, Transform victim, Transform focus, TypeDOverhaulAssets assets)
    {
        Vector3 point = focus != null ? focus.position : victim.position + Vector3.up * 1.2f;
        if (tm.slowScale < 0.999f && tm.slowRealtime > 0f)
            StartCoroutine(Co_Slow(tm.slowScale, tm.slowRealtime));

        CameraShake cs = GameplaySceneCache.ResolveMainCameraShake();
        switch (kind)
        {
            case ExecutionKind.BossFront:
            case ExecutionKind.BossBack:
                ScreenCut.Play(BossCutPattern(ViewportOf(point), kind == ExecutionKind.BossBack ? 2 : 1));
                ActionColourDirector.NotifyFinisher(point);
                if (assets != null) TypeDOverhaulAssets.PlayAt(assets.execSlash, point, 1f, kind == ExecutionKind.BossBack ? 0.94f : 1f);
                if (cs != null) cs.Shake(0.4f, 0.26f);
                oh.ApplyExecutionStrike(kind);
                break;
            case ExecutionKind.BossFinal:
                ScreenCut.PlayUltimate(ViewportOf(point));
                if (assets != null)
                {
                    TypeDOverhaulAssets.PlayAt(assets.execFinal, point, 1f, 1f);
                    if (assets.coreBurstVfx != null) TransientVfxPool.Spawn(assets.coreBurstVfx, point, Quaternion.identity, null, 1.6f);
                }
                if (cs != null) cs.Shake(0.55f, 0.4f);
                oh.ApplyExecutionStrike(kind);
                break;
            case ExecutionKind.Mob:
                ActionColourDirector.PulseAt(point, 0.85f, 0.35f, _keep);
                if (assets != null)
                {
                    TypeDOverhaulAssets.PlayAt(assets.execSlash, point, 0.85f, 1.12f);
                    if (assets.sparkVfx != null) TransientVfxPool.Spawn(assets.sparkVfx, point, Quaternion.identity, null, 1f);
                }
                if (cs != null) cs.Shake(0.22f, 0.16f);
                brain.FinishExecuted(_player, keepVictimPose: true);
                break;
            case ExecutionKind.Takedown:
                if (assets != null) TypeDOverhaulAssets.PlayAt(assets.execSlash, point, 0.45f, 1.3f);
                brain.FinishExecuted(_player, keepVictimPose: true);
                break;
        }
        Struck?.Invoke(kind, victim);
        if (kind != ExecutionKind.Takedown)
            CombatBarkDirector.TryBark(kind == ExecutionKind.BossFinal ? "oh.final_exec" : CombatBarkDirector.GroupFinisher, kind == ExecutionKind.BossFinal);
    }

    IEnumerator Co_Slow(float scale, float realtime)
    {
        _slowRequested = true;
        GameTimeScale.Request(this, scale);
        yield return new WaitForSecondsRealtime(realtime);
        GameTimeScale.Release(this);
        _slowRequested = false;
    }

    static ScreenCutPattern BossCutPattern(Vector2 impact, int lines)
    {
        ScreenCutPattern p = new ScreenCutPattern
        {
            Count = lines,
            A = new ScreenCutLine { Through = impact + new Vector2(0f, 0.01f), AngleDeg = -32f, Start = 0f, Duration = 0.07f, Slide = 0.06f, Push = 0.02f, RotationDeg = 0.4f },
            B = new ScreenCutLine { Through = impact + new Vector2(0.03f, -0.04f), AngleDeg = 214f, Start = 0.09f, Duration = 0.07f, Slide = 0.05f, Push = 0.018f, RotationDeg = -0.5f },
            Hold = 0.14f,
            SplitDuration = 0.34f,
            Dim = 0.18f,
            SpeedLines = 0.7f,
            Streak = 0.8f,
            GapFlash = 0.7f,
            Sfx = true
        };
        return p;
    }

    static Vector2 ViewportOf(Vector3 world)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return new Vector2(0.5f, 0.5f);
        Vector3 v = cam.WorldToViewportPoint(world);
        if (v.z <= 0f)
            return new Vector2(0.5f, 0.5f);
        return new Vector2(Mathf.Clamp(v.x, 0.2f, 0.8f), Mathf.Clamp(v.y, 0.25f, 0.75f));
    }

    void PlacePlayer(Vector3 position, Quaternion rotation)
    {
        Transform root = _refs != null ? _refs.PlayerRoot : _player;
        bool ccWasEnabled = _cc != null && _cc.enabled;
        if (ccWasEnabled)
            _cc.enabled = false;
        root.SetPositionAndRotation(position, rotation);
        if (ccWasEnabled)
            _cc.enabled = true;
    }

    void CrossFadePlayer(string state, float fade)
    {
        if (_playerAnimator == null)
            return;
        int hash = Animator.StringToHash(state);
        if (_playerAnimator.HasState(0, hash))
            _playerAnimator.CrossFadeInFixedTime(hash, fade, 0, 0f);
    }

    void Cleanup()
    {
        ExecutionCamChoreographer.Stop(this);
        if (_slowRequested)
        {
            GameTimeScale.Release(this);
            _slowRequested = false;
        }
        if (_blocked && _blocker != null && (_blocker as Object) != null)
            _blocker.BlockAll(false);
        _blocked = false;
        if (_move != null)
            _move.SetExternalControl(false);
        _running = false;
        _kind = ExecutionKind.None;
    }
}
