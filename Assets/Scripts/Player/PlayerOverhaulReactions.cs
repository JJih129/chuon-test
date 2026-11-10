// 반복 16: 플레이어 쪽 개편 반응 (Docs/Boss_TypeD_Phases.md §2, §5). FacilityCombatRules 가 플레이어 루트에 붙인다.
//  - 보스 중공 피격 → 띄워짐(넉다운 + 2.6 m 밀림). 0.45 s 뒤 회피 입력 = 구르기 복귀(기존 피격 회피 복귀 경로).
//  - 중공을 가드로 막음 → 가드 밀림 0.9 m.
//  - 금색 패링 1.8 s 간격 연쇄 3회 → 강패링 (보스 넉백 + 균형 35).
//  - 자홍 공격 퍼펙트 회피 → 블링크 (보스 등 뒤 1.7 m 순간이동 + 반격 창).
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerOverhaulReactions : MonoBehaviour
{
    [SerializeField] float launchDistance = 2.6f;
    [SerializeField] float launchTime = 0.32f;
    [SerializeField] int launchDamageThreshold = 22;
    [SerializeField] float guardPushDistance = 0.9f;
    [SerializeField] float guardPushTime = 0.18f;
    [SerializeField] float guardPushDamageThreshold = 14f;
    [SerializeField] float parryChainGap = 1.8f;
    [SerializeField, Min(2)] int strongParryChain = 3;
    [SerializeField] float blinkBehindDistance = 1.7f;

    static string s_launchMark;
    public static void MarkNextHitLaunch(string source) => s_launchMark = source;
    public static void ClearLaunchMark() => s_launchMark = null;

    public int QaLaunches { get; private set; }
    public int QaGuardPushes { get; private set; }
    public int QaStrongParries { get; private set; }
    public int QaBlinks { get; private set; }
    public int ParryChain => _chain;
    public static PlayerOverhaulReactions Instance { get; private set; }

    PlayerReferences _refs;
    CharacterController _cc;
    PlayerCombatController _combat;
    PlayerHealth _health;
    int _chain;
    float _lastParryAt = float.NegativeInfinity;
    Coroutine _push;
    Coroutine _hitStop;
    float _lastHitStopAt = float.NegativeInfinity;

    void Awake()
    {
        Instance = this;
        _refs = GetComponent<PlayerReferences>();
        _cc = GetComponentInChildren<CharacterController>();
        _combat = GetComponentInChildren<PlayerCombatController>();
        _health = GetComponentInChildren<PlayerHealth>();
    }

    void OnEnable() => PlayerDamageReceiver.AnyHitResolved += HandleHit;

    void OnDisable()
    {
        PlayerDamageReceiver.AnyHitResolved -= HandleHit;
        CombatFeelRuntimeUtility.ForceRestoreActiveHitStop(this, ref _hitStop);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    Transform Root => _refs != null ? _refs.PlayerRoot : transform;

    void HandleHit(PlayerDamageReceiver receiver, HitPayload payload, ResolvedHitResult hit, int hpLost)
    {
        if (receiver == null || receiver.transform.root != transform.root)
            return;
        BossController boss = payload.attacker != null ? payload.attacker.GetComponentInParent<BossController>() : null;
        if (boss == null || boss.Overhaul == null)
            return;

        if (hit.IsParry)
        {
            HandleParry(boss, payload);
            return;
        }

        if (hit.IsPerfectDodge)
        {
            if (boss.IsMagentaAttackLandingNow())
                Blink(boss);
            return;
        }

        if (hit.IsGuardBlock)
        {
            if (payload.damage >= guardPushDamageThreshold || payload.hitType == HitType.Heavy)
                Push(Flat(payload.hitDirection, Root.position - boss.transform.position) * guardPushDistance, guardPushTime, 0.12f, false);
            return;
        }

        if (!hit.IsDamage || hpLost <= 0 || _health == null || _health.IsDead)
            return;
        bool heavy = s_launchMark != null || payload.hitType == HitType.Heavy || hpLost >= launchDamageThreshold;
        if (!heavy)
            return;
        QaLaunches++;
        if (_combat != null)
            _combat.ApplyKnockdown();
        Push(Flat(Root.position - boss.transform.position, payload.hitDirection) * launchDistance, launchTime, 0.3f, true);
    }

    static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f)
        {
            v = fallback;
            v.y = 0f;
        }
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.back;
    }

    void Push(Vector3 delta, float time, float shake, bool launch)
    {
        if (!launch)
            QaGuardPushes++;
        CameraShake cs = GameplaySceneCache.ResolveMainCameraShake();
        if (cs != null)
            cs.Shake(shake, launch ? 0.2f : 0.1f);
        if (_push != null)
            StopCoroutine(_push);
        _push = StartCoroutine(Co_Push(delta, time, launch));
    }

    IEnumerator Co_Push(Vector3 delta, float time, bool launch)
    {
        float t = 0f;
        Vector3 moved = Vector3.zero;
        while (t < time && _cc != null)
        {
            float dt = Time.deltaTime;
            t += dt;
            // 감속 곡선: 처음에 세게 밀리고 끝에서 멈춤
            float k = 1f - (1f - Mathf.Clamp01(t / time)) * (1f - Mathf.Clamp01(t / time));
            Vector3 target = delta * k;
            Vector3 step = target - moved;
            moved = target;
            if (_cc.enabled)
                _cc.Move(step + (launch ? Vector3.down * 0.02f : Vector3.zero));
            yield return null;
        }
        _push = null;
    }

    void HandleParry(BossController boss, HitPayload payload)
    {
        float now = Time.time;
        _chain = now - _lastParryAt <= parryChainGap ? _chain + 1 : 1;
        _lastParryAt = now;
        boss.Overhaul.NotifyParry(_chain, Root);
        if (_chain < strongParryChain)
            return;

        _chain = 0;
        QaStrongParries++;
        boss.Overhaul.NotifyStrongParry(Root);
        Vector3 p = payload.hitPoint;
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.strongParry, p, 1f, 1f);
            if (a.sparkVfx != null) TransientVfxPool.Spawn(a.sparkVfx, p, Quaternion.identity, null, 1.6f);
        }
        ActionColourDirector.NotifyParry(p);
        CameraShake cs = GameplaySceneCache.ResolveMainCameraShake();
        if (cs != null)
            cs.Shake(0.32f, 0.2f);
        SoulsCameraRig.NotifyParry();
        if (!CombatFeelRuntimeUtility.IsHitStopActive)
            CombatFeelRuntimeUtility.StartHitStop(this, ref _hitStop, 0.05f, 0.1f, true, 0f, ref _lastHitStopAt);
    }

    void Blink(BossController boss)
    {
        Transform root = Root;
        Vector3 bossPos = boss.transform.position;
        Vector3 back = -boss.transform.forward;
        back.y = 0f;
        back = back.sqrMagnitude > 0.001f ? back.normalized : -Vector3.forward;
        Vector3 from = root.position;
        Vector3 dest = from;
        bool found = false;
        float radius = _cc != null ? _cc.radius : 0.35f;
        float height = _cc != null ? _cc.height : 1.8f;
        float[] yaw = { 0f, 35f, -35f, 70f, -70f };
        for (int i = 0; i < yaw.Length && !found; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, yaw[i], 0f) * back;
            Vector3 candidate = bossPos + dir * blinkBehindDistance;
            candidate.y = from.y;
            Vector3 bottom = candidate + Vector3.up * (radius + 0.05f);
            Vector3 top = candidate + Vector3.up * (height - radius);
            bool blocked = false;
            foreach (Collider c in Physics.OverlapCapsule(bottom, top, radius * 0.9f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null || c.transform.root == root.root || c.GetComponentInParent<BossController>() == boss)
                    continue;
                blocked = true;
                break;
            }
            if (blocked)
                continue;
            bool hitSomething = Physics.Linecast(bossPos + Vector3.up * 1.2f, candidate + Vector3.up * 1.2f, out RaycastHit h, ~0, QueryTriggerInteraction.Ignore);
            if (!hitSomething || (h.collider != null && (h.collider.GetComponentInParent<BossController>() == boss || h.collider.transform.root == root.root)))
            {
                dest = candidate;
                found = true;
            }
        }
        if (!found)
            return;

        QaBlinks++;
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.blink, from + Vector3.up, 0.9f, 1f);
            if (a.blinkVfx != null)
            {
                TransientVfxPool.Spawn(a.blinkVfx, from + Vector3.up, Quaternion.identity, null, 1f);
                TransientVfxPool.Spawn(a.blinkVfx, dest + Vector3.up, Quaternion.identity, null, 1f);
            }
        }
        bool cc = _cc != null && _cc.enabled;
        if (cc) _cc.enabled = false;
        Vector3 face = bossPos - dest;
        face.y = 0f;
        root.SetPositionAndRotation(dest, face.sqrMagnitude > 0.001f ? Quaternion.LookRotation(face.normalized, Vector3.up) : root.rotation);
        if (cc) _cc.enabled = true;
        ActionColourDirector.PulseAt(dest + Vector3.up * 1.2f, 0.7f, 0.3f);
        boss.Overhaul.NotifyBlink();
    }
}
