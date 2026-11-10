// 반복 16: 70 % 「봉쇄 프로토콜」 기믹 + 15 % 임계 폭주 엄폐 블록. Docs/Boss_TypeD_Phases.md §1
//  셔터 6장이 아레나 벽을 따라 내려오고, 벽 센트리 터렛 1 + 순찰 드론 1(공용 공격 토큰 AI)이 붙는다.
//  벽 전원 노드 3개(TSUKASA 레드 발광, 보스에 붉은 전원선)가 보스 보호막(HP 바닥 70 %)을 유지한다.
//  노드 3개 파괴 → 터렛·드론 전원 차단 → TypeDOverhaul.NotifyNodesDestroyed (보스 과부하 6 s) → 셔터 상승.
// 배치는 런타임 레이캐스트(아레나 중심 → 벽)로 정한다: 씬 파일을 고치지 않는다.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossLockdownGimmick : MonoBehaviour
{
    [SerializeField, Min(1)] int nodeCount = 3;
    [SerializeField, Min(1)] int nodeIntegrity = 3;
    [SerializeField] float nodeHeight = 1.25f;
    [SerializeField, Min(0)] int shutterCount = 6;
    [SerializeField] float shutterWidth = 5.2f;
    [SerializeField] float shutterHeight = 6.5f;
    [SerializeField] float shutterDropTime = 1.4f;
    [SerializeField] bool spawnTurret = true;
    [SerializeField] bool spawnDrone = false; // iter18: 봉쇄는 "노드를 부숴라" — 드론까지 겹치면 과부하

    TypeDOverhaul _overhaul;
    bool _active;
    readonly List<BossPowerNode> _nodes = new List<BossPowerNode>();
    readonly List<GameObject> _shutters = new List<GameObject>();
    readonly List<GameObject> _mobs = new List<GameObject>();
    readonly List<LineRenderer> _tethers = new List<LineRenderer>();
    readonly List<GameObject> _covers = new List<GameObject>();
    readonly HashSet<Collider> _coverColliders = new HashSet<Collider>();
    readonly RaycastHit[] _hits = new RaycastHit[16];
    Material _tetherMat;
    float _lastShieldFxAt = float.NegativeInfinity;
    int _enemyLayer = -1;

    public bool IsActive => _active;
    public int NodesAlive { get; private set; }
    public int NodesTotal => _nodes.Count;
    public IReadOnlyList<BossPowerNode> Nodes => _nodes;
    public IReadOnlyList<GameObject> Covers => _covers;
    public bool CoverRaised => _covers.Count > 0;

    public void Bind(TypeDOverhaul overhaul) => _overhaul = overhaul;

    int EnemyLayer
    {
        get
        {
            if (_enemyLayer < 0)
            {
                BossDamageReceiver r = _overhaul.Boss.GetComponentInChildren<BossDamageReceiver>(true);
                Collider c = r != null ? r.GetComponent<Collider>() ?? r.GetComponentInChildren<Collider>() : null;
                _enemyLayer = c != null ? c.gameObject.layer : LayerMask.NameToLayer("Enemy");
                if (_enemyLayer < 0)
                    _enemyLayer = 0;
            }
            return _enemyLayer;
        }
    }

    // ======================================================================= lockdown

    public void Begin()
    {
        if (_active)
            return;
        _active = true;
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        Vector3 c = _overhaul.ArenaCenter;
        float floorY = c.y;
        Transform player = GameplaySceneCache.ResolvePlayerReferences() != null ? GameplaySceneCache.ResolvePlayerReferences().PlayerRoot : null;

        // 첫 노드는 플레이어 쪽 벽(찾기 쉽게), 나머지는 120° 간격
        float baseYaw = 0f;
        if (player != null)
        {
            Vector3 d = player.position - c;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f)
                baseYaw = Quaternion.LookRotation(d.normalized).eulerAngles.y;
        }

        NodesAlive = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            if (!TryReachableWallPoint(c, baseYaw + i * (360f / nodeCount), out Vector3 p, out Vector3 n))
                continue;
            // 벽에서 1.6 m 떨어진 독립 콘솔(뒤판 = 벽 방향)로 세운다: 아레나 중앙 비움 구역(반경 11 m) 안이라 항상 걸어서 닿는다
            Vector3 flatToWall = new Vector3(p.x - c.x, 0f, p.z - c.z);
            float wallDist = flatToWall.magnitude;
            Vector3 dirOut = wallDist > 0.01f ? flatToWall / wallDist : -n;
            float standDist = Mathf.Clamp(wallDist - 1.6f, 5f, 11f);
            Vector3 pos = new Vector3(c.x, floorY + nodeHeight, c.z) + dirOut * standDist;
            n = -dirOut;
            BossPowerNode node = BossPowerNode.Create(pos, Quaternion.LookRotation(n, Vector3.up), nodeIntegrity, EnemyLayer, a, this);
            _nodes.Add(node);
            NodesAlive++;
            _tethers.Add(MakeTether(node.transform));
        }

        for (int i = 0; i < shutterCount; i++)
        {
            if (!TryWallPoint(c, baseYaw + 30f + i * (360f / Mathf.Max(1, shutterCount)), out Vector3 p, out Vector3 n))
                continue;
            GameObject s = MakeShutter(a, new Vector3(p.x, floorY, p.z) + n * 0.15f, Quaternion.LookRotation(n, Vector3.up));
            _shutters.Add(s);
            StartCoroutine(Co_Slide(s.transform, s.transform.position + Vector3.up * shutterHeight, s.transform.position, shutterDropTime, i * 0.08f));
        }

        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.lockdownAlarm, c + Vector3.up * 3f, 0.8f, 1f);
            TypeDOverhaulAssets.PlayAt(a.shutterDrop, c + Vector3.up * 2f, 0.9f, 1f);
        }

        if (spawnTurret && a != null && a.sentryTurretPrefab != null && TryWallPoint(c, baseYaw + 180f + 60f, out Vector3 tp, out Vector3 tn))
            SpawnMob(a.sentryTurretPrefab, new Vector3(tp.x, floorY + 2.7f, tp.z) + tn * 0.25f, Quaternion.LookRotation(tn, Vector3.up), player);
        if (spawnDrone && a != null && a.patrolDronePrefab != null)
        {
            Vector3 dp = c + Quaternion.Euler(0f, baseYaw + 180f - 60f, 0f) * Vector3.forward * (_overhaul.ArenaRadius * 0.55f) + Vector3.up * 3.6f;
            SpawnMob(a.patrolDronePrefab, dp, Quaternion.LookRotation(c - dp, Vector3.up), player);
        }

        _overhaul.Hud.SetObjective($"전원 노드 파괴  {NodesTotal - NodesAlive}/{NodesTotal}");
        if (NodesAlive == 0)
            _overhaul.NotifyNodesDestroyed(); // 벽을 못 찾은 아레나: 기믹 건너뜀 (소프트락 방지)
    }

    // 노드는 아레나 중앙에서 걸어서 닿는 벽에만 (문틈·난간 뒤·복도 안쪽 제외): 중앙→벽 앞 1 m 까지 캡슐이 막히지 않는 각도를 ±75° 안에서 찾는다
    bool TryReachableWallPoint(Vector3 center, float yaw, out Vector3 point, out Vector3 normal)
    {
        float[] offsets = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 75f, -75f };
        for (int k = 0; k < offsets.Length; k++)
        {
            if (!TryWallPoint(center, yaw + offsets[k], out point, out normal))
                continue;
            Vector3 flat = point - center;
            flat.y = 0f;
            float dist = flat.magnitude;
            if (dist < 5f || dist > Mathf.Max(12f, _overhaul.ArenaRadius + 3f))
                continue;
            Vector3 dir = flat / dist;
            Vector3 bottom = center + Vector3.up * 0.55f;
            Vector3 top = center + Vector3.up * 1.5f;
            bool blocked = false;
            int n = Physics.CapsuleCastNonAlloc(bottom, top, 0.4f, dir, _hits, dist - 1.1f, ~0, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < n && !blocked; h++)
            {
                Collider col = _hits[h].collider;
                if (col == null || col.GetComponentInParent<BossController>() != null || col.GetComponentInParent<PlayerReferences>() != null || col.GetComponentInParent<EnemyBrain>() != null || col.GetComponentInParent<BossPowerNode>() != null)
                    continue;
                blocked = true;
            }
            if (!blocked)
                return true;
        }
        return TryWallPoint(center, yaw, out point, out normal);
    }

    bool TryWallPoint(Vector3 center, float yaw, out Vector3 point, out Vector3 normal)
    {
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 origin = center + Vector3.up * 1.4f;
        int n = Physics.RaycastNonAlloc(origin, dir, _hits, 40f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        point = Vector3.zero;
        normal = -dir;
        for (int i = 0; i < n; i++)
        {
            Collider col = _hits[i].collider;
            if (col == null || _hits[i].distance < 4f || _hits[i].distance >= best)
                continue;
            if (col.GetComponentInParent<BossController>() != null || col.GetComponentInParent<PlayerReferences>() != null || col.GetComponentInParent<EnemyBrain>() != null || col.GetComponentInParent<BossPowerNode>() != null)
                continue;
            if (Mathf.Abs(_hits[i].normal.y) > 0.5f)
                continue;
            best = _hits[i].distance;
            point = _hits[i].point;
            normal = _hits[i].normal;
            normal.y = 0f;
            normal = normal.sqrMagnitude > 0.001f ? normal.normalized : -dir;
        }
        if (float.IsPositiveInfinity(best))
        {
            // 벽이 너무 멀면 반경 위에 독립 기둥형으로 세운다
            point = center + dir * Mathf.Min(_overhaul.ArenaRadius, 14f);
            normal = -dir;
            return true;
        }
        return true;
    }

    GameObject MakeShutter(TypeDOverhaulAssets a, Vector3 basePos, Quaternion rot)
    {
        GameObject go;
        if (a != null && a.shutterModel != null)
        {
            go = Instantiate(a.shutterModel, basePos, rot);
            go.transform.localScale = new Vector3(shutterWidth / 5f, shutterHeight / 6.5f, 1f);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetPositionAndRotation(basePos + Vector3.up * shutterHeight * 0.5f, rot);
            go.transform.localScale = new Vector3(shutterWidth, shutterHeight, 0.25f);
            Destroy(go.GetComponent<Collider>());
        }
        go.name = "OH_Shutter";
        foreach (Collider col in go.GetComponentsInChildren<Collider>())
            Destroy(col);
        return go;
    }

    void SpawnMob(GameObject prefab, Vector3 pos, Quaternion rot, Transform player)
    {
        GameObject m = Instantiate(prefab, pos, rot);
        m.name = "OH_Lockdown_" + prefab.name;
        _mobs.Add(m);
        foreach (IEnemyAlertable al in m.GetComponentsInChildren<IEnemyAlertable>())
            if (player != null)
                al.ReceiveAlert(player.position);
    }

    LineRenderer MakeTether(Transform node)
    {
        if (_tetherMat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            _tetherMat = sh != null ? new Material(sh) : null;
        }
        var go = new GameObject("OH_NodeTether");
        go.transform.SetParent(node, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = _tetherMat;
        lr.positionCount = 2;
        lr.widthMultiplier = 0.045f;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        Color red = new Color(0.95f, 0.12f, 0.1f, 0.85f);
        lr.startColor = red;
        lr.endColor = new Color(1f, 0.85f, 0.8f, 0.6f);
        return lr;
    }

    void LateUpdate()
    {
        if (!_active || _overhaul == null || _overhaul.CoreAnchor == null)
            return;
        Vector3 core = _overhaul.CoreAnchor.position;
        float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 6f, 0.3f);
        for (int i = 0; i < _tethers.Count; i++)
        {
            LineRenderer lr = _tethers[i];
            if (lr == null)
                continue;
            lr.SetPosition(0, lr.transform.position);
            lr.SetPosition(1, core);
            lr.widthMultiplier = 0.045f * flicker;
        }
    }

    public void NotifyNodeDestroyed(BossPowerNode node)
    {
        int i = _nodes.IndexOf(node);
        if (i >= 0 && i < _tethers.Count && _tethers[i] != null)
            Destroy(_tethers[i].gameObject);
        NodesAlive = Mathf.Max(0, NodesAlive - 1);
        _overhaul.Hud.SetObjective(NodesAlive > 0 ? $"전원 노드 파괴  {NodesTotal - NodesAlive}/{NodesTotal}" : "");
        CombatBarkDirector.TryBark(NodesAlive > 0 ? "oh.node" : "oh.nodes_done", NodesAlive == 0);
        if (NodesAlive == 0 && _active)
        {
            KillMobs();
            _overhaul.NotifyNodesDestroyed();
        }
    }

    public void NotifyShieldHit()
    {
        if (Time.unscaledTime - _lastShieldFxAt < 0.18f || _overhaul.CoreAnchor == null)
            return;
        _lastShieldFxAt = Time.unscaledTime;
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        Vector3 p = _overhaul.CoreAnchor.position;
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.nodeHit, p, 0.55f, 0.8f);
            if (a.sparkVfx != null) TransientVfxPool.Spawn(a.sparkVfx, p, Quaternion.identity, null, 0.7f);
        }
        _overhaul.Hud.PulseObjective();
    }

    void KillMobs()
    {
        for (int i = 0; i < _mobs.Count; i++)
        {
            GameObject m = _mobs[i];
            if (m == null)
                continue;
            EnemyHealth eh = m.GetComponentInChildren<EnemyHealth>();
            if (eh != null && !eh.IsDead)
                eh.TakeDamage(eh.CurrentHP + 999, HitType.Force, m.transform.position);
            DroneController dc = m.GetComponentInChildren<DroneController>();
            if (dc != null && !dc.IsDead)
                dc.TakeDamage(9999, m.transform.position, Vector3.down);
        }
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
            TypeDOverhaulAssets.PlayAt(a.nodeBreak, _overhaul.ArenaCenter + Vector3.up * 3f, 0.7f, 0.7f);
    }

    /// <summary>과부하가 끝나면 셔터를 올리고 정리.</summary>
    public void End()
    {
        if (!_active)
            return;
        _active = false;
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
            TypeDOverhaulAssets.PlayAt(a.shutterRise, _overhaul.ArenaCenter + Vector3.up * 2f, 0.8f, 1f);
        for (int i = 0; i < _shutters.Count; i++)
        {
            if (_shutters[i] == null)
                continue;
            Transform t = _shutters[i].transform;
            StartCoroutine(Co_Slide(t, t.position, t.position + Vector3.up * shutterHeight, 1.8f, i * 0.06f));
            Destroy(_shutters[i], 2.6f + i * 0.06f);
        }
        _shutters.Clear();
        CleanupNodesAndMobs(2f);
        _overhaul.Hud.SetObjective("");
    }

    public void ForceEnd()
    {
        _active = false;
        for (int i = 0; i < _shutters.Count; i++)
            if (_shutters[i] != null)
                Destroy(_shutters[i]);
        _shutters.Clear();
        KillMobs();
        CleanupNodesAndMobs(0f);
        LowerCover(0f);
        if (_overhaul != null && _overhaul.Hud != null)
            _overhaul.Hud.SetObjective("");
    }

    void CleanupNodesAndMobs(float delay)
    {
        for (int i = 0; i < _tethers.Count; i++)
            if (_tethers[i] != null)
                Destroy(_tethers[i].gameObject);
        _tethers.Clear();
        for (int i = 0; i < _nodes.Count; i++)
            if (_nodes[i] != null)
                Destroy(_nodes[i].gameObject, delay + 4f);
        _nodes.Clear();
        for (int i = 0; i < _mobs.Count; i++)
            if (_mobs[i] != null)
                Destroy(_mobs[i], delay + 5f);
        _mobs.Clear();
        NodesAlive = 0;
    }

    IEnumerator Co_Slide(Transform t, Vector3 from, Vector3 to, float duration, float delay)
    {
        if (t == null)
            yield break;
        t.position = from;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        float e = 0f;
        while (e < duration && t != null)
        {
            e += Time.deltaTime;
            float k = e / duration;
            // 셔터: 끝에서 살짝 튀는 감속 (무거운 금속)
            float s = k < 0.85f ? Mathf.SmoothStep(0f, 1f, k / 0.85f) * 1.02f : 1.02f - 0.02f * ((k - 0.85f) / 0.15f);
            t.position = Vector3.LerpUnclamped(from, to, Mathf.Min(1.02f, s));
            yield return null;
        }
        if (t != null)
            t.position = to;
    }

    // ======================================================================= desperation cover

    public void RaiseCover(Vector3 center, float radius, Transform player)
    {
        LowerCover(0f);
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        float baseYaw = 0f;
        if (player != null)
        {
            Vector3 d = player.position - center;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f)
                baseYaw = Quaternion.LookRotation(d.normalized).eulerAngles.y;
        }
        for (int i = 0; i < 3; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, baseYaw + i * 120f, 0f) * Vector3.forward;
            Vector3 pos = center + dir * Mathf.Clamp(radius * 0.5f, 5f, 9f);
            Quaternion rot = Quaternion.LookRotation(-dir, Vector3.up);
            GameObject go;
            if (a != null && a.coverModel != null)
                go = Instantiate(a.coverModel, pos, rot);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetPositionAndRotation(pos + Vector3.up * 1.3f, rot);
                go.transform.localScale = new Vector3(2.4f, 2.6f, 0.9f);
                Destroy(go.GetComponent<Collider>());
            }
            go.name = "OH_Cover";
            foreach (Collider col in go.GetComponentsInChildren<Collider>())
                Destroy(col);
            var box = new GameObject("OH_CoverCollider").AddComponent<BoxCollider>();
            box.transform.SetParent(go.transform, false);
            box.transform.SetPositionAndRotation(pos + Vector3.up * 1.3f, rot);
            box.transform.localScale = Vector3.one;
            box.size = new Vector3(2.4f / go.transform.lossyScale.x, 2.6f / go.transform.lossyScale.y, 0.9f / go.transform.lossyScale.z);
            _coverColliders.Add(box);
            _covers.Add(go);
            Transform t = go.transform;
            StartCoroutine(Co_Slide(t, t.position - Vector3.up * 2.8f, t.position, 0.8f, 0.1f * i));
        }
        if (a != null)
            TypeDOverhaulAssets.PlayAt(a.coverRise, center + Vector3.up, 0.9f, 1f);
    }

    public void LowerCover(float delay)
    {
        for (int i = 0; i < _covers.Count; i++)
        {
            GameObject go = _covers[i];
            if (go == null)
                continue;
            if (delay <= 0f)
            {
                Destroy(go);
                continue;
            }
            Transform t = go.transform;
            StartCoroutine(Co_Lower(t, delay + i * 0.08f));
        }
        _covers.Clear();
        _coverColliders.Clear();
    }

    IEnumerator Co_Lower(Transform t, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (t == null)
            yield break;
        yield return Co_Slide(t, t.position, t.position - Vector3.up * 2.9f, 0.9f, 0f);
        if (t != null)
            Destroy(t.gameObject);
    }

    /// <summary>origin → target 선분이 엄폐 블록에 막히는가.</summary>
    public bool IsCovered(Vector3 origin, Vector3 target)
    {
        if (_coverColliders.Count == 0)
            return false;
        Vector3 d = target - origin;
        float len = d.magnitude;
        if (len < 0.01f)
            return false;
        int n = Physics.RaycastNonAlloc(origin, d / len, _hits, len, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
            if (_hits[i].collider != null && _coverColliders.Contains(_hits[i].collider))
                return true;
        return false;
    }

    /// <summary>QA 봇: 보스 기준 가장 가까운 엄폐 뒤 지점.</summary>
    public bool TryGetCoverSpot(Vector3 from, Vector3 bossPos, out Vector3 spot)
    {
        spot = Vector3.zero;
        float best = float.PositiveInfinity;
        foreach (Collider c in _coverColliders)
        {
            if (c == null)
                continue;
            Vector3 cp = c.transform.position;
            Vector3 away = cp - bossPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                continue;
            Vector3 s = cp + away.normalized * 1.3f;
            s.y = from.y;
            float dd = (s - from).sqrMagnitude;
            if (dd < best)
            {
                best = dd;
                spot = s;
            }
        }
        return !float.IsPositiveInfinity(best);
    }

    void OnDestroy()
    {
        if (_tetherMat != null)
            Destroy(_tetherMat);
    }
}
