#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 반복 16: Type-D 개편 QA/스크린샷 훅 (개발 전용, Unity MCP execute_code 에서 호출).
using UnityEngine;

public static class OverhaulQa
{
    static Transform Player()
    {
        PlayerReferences r = Object.FindObjectOfType<PlayerReferences>();
        return r != null ? r.PlayerRoot : null;
    }

    static void Place(Transform t, Vector3 pos, Vector3 face)
    {
        if (t == null)
            return;
        CharacterController cc = t.GetComponentInChildren<CharacterController>();
        bool was = cc != null && cc.enabled;
        if (was) cc.enabled = false;
        face.y = 0f;
        t.SetPositionAndRotation(pos, face.sqrMagnitude > 0.001f ? Quaternion.LookRotation(face.normalized) : t.rotation);
        if (was) cc.enabled = true;
    }

    /// <summary>보스를 가장 가까운 벽 3 m 앞에 두고 플레이어를 반대편에 세운 뒤 벽 쪽으로 넉백.</summary>
    public static string ForceWallSplat()
    {
        TypeDOverhaul oh = TypeDOverhaul.Active;
        if (oh == null) return "no overhaul";
        Vector3 c = oh.ArenaCenter + Vector3.up * 1.4f;
        float best = float.PositiveInfinity; Vector3 dir = Vector3.forward; Vector3 hitPoint = c;
        for (int i = 0; i < 24; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (Physics.Raycast(c, d, out RaycastHit h, 40f, ~0, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<BossController>() == null && h.collider.GetComponentInParent<PlayerReferences>() == null && h.distance < best)
            { best = h.distance; dir = d; hitPoint = h.point; }
        }
        Vector3 bossPos = hitPoint - dir * 3.0f; bossPos.y = oh.Boss.transform.position.y;
        Place(oh.Boss.transform, bossPos, -dir);
        Transform p = Player();
        Vector3 pp = bossPos - dir * 2.2f; pp.y = p.position.y;
        Place(p, pp, dir);
        oh.Impact.AddImpact(110f, dir, BossOverhaulReaction.Knockback);
        return "wall at " + best.ToString("F1") + " react=" + oh.Boss.ActiveOverhaulReaction;
    }

    /// <summary>플레이어를 보스 뒤(back=true) 또는 앞에 세우고 브레이크 → 처형 시작.</summary>
    public static string ForceBossExecution(bool back)
    {
        TypeDOverhaul oh = TypeDOverhaul.Active;
        if (oh == null) return "no overhaul";
        if (!oh.Break.IsInBreak) oh.Break.ForceEnterBreak();
        Transform b = oh.Boss.transform;
        Transform p = Player();
        Vector3 f = b.forward; f.y = 0f; f.Normalize();
        Vector3 pos = b.position + (back ? -f : f) * 2.2f; pos.y = p.position.y;
        Place(p, pos, b.position - pos);
        bool ok = ExecutionDirector.TryBeginFromAttackInput(p);
        return "exec started=" + ok + " kind=" + ExecutionDirector.CurrentKind;
    }

    public static string KillNodes()
    {
        TypeDOverhaul oh = TypeDOverhaul.Active;
        if (oh == null || oh.Lockdown == null) return "none";
        int n = 0;
        foreach (BossPowerNode node in oh.Lockdown.Nodes.ToArraySafe())
            if (node != null && !node.IsDead) { node.Kill(); n++; }
        return "killed " + n;
    }

    static BossPowerNode[] ToArraySafe(this System.Collections.Generic.IReadOnlyList<BossPowerNode> l)
    {
        var a = new BossPowerNode[l.Count];
        for (int i = 0; i < a.Length; i++) a[i] = l[i];
        return a;
    }

    static EnemyBrain NearestMob(bool alive)
    {
        Transform p = Player();
        EnemyBrain best = null; float bd = float.PositiveInfinity;
        foreach (EnemyBrain b in EnemyBrain.Active)
        {
            if (b == null || b.IsDead || !(b is SecurityAndroidBrain || b is ShieldEnforcerBrain)) continue;
            float d = (b.transform.position - p.position).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    /// <summary>가장 가까운 안드로이드/집행관을 경직(리포스트 창)시키고 앞에 서서 처형.</summary>
    public static string ForceMobExecution()
    {
        EnemyBrain m = NearestMob(true);
        Transform p = Player();
        if (m == null || p == null) return "no mob";
        Vector3 f = m.transform.forward; f.y = 0f;
        Vector3 pos = m.transform.position + f.normalized * 1.8f; pos.y = p.position.y;
        Place(p, pos, m.transform.position - pos);
        for (int i = 0; i < 6 && !m.InRiposteWindow; i++)
            m.ReceiveHit(new HitPayload { damage = 1f, hitType = HitType.Parried, hitPoint = m.transform.position + Vector3.up, hitDirection = f, attacker = p, canParry = false, canGuard = false });
        bool ok = ExecutionDirector.TryBeginFromAttackInput(p);
        return m.name + " riposte=" + m.InRiposteWindow + " exec=" + ok + " kind=" + ExecutionDirector.CurrentKind;
    }

    /// <summary>경계 전 가장 가까운 안드로이드 등 뒤에 서서 암살.</summary>
    public static string ForceTakedown()
    {
        EnemyBrain m = NearestMob(true);
        Transform p = Player();
        if (m == null || p == null) return "no mob";
        Vector3 f = m.transform.forward; f.y = 0f; f.Normalize();
        Vector3 pos = m.transform.position - f * 1.4f; pos.y = p.position.y;
        Place(p, pos, f);
        bool ok = ExecutionDirector.TryBeginFromAttackInput(p);
        return m.name + " ambushable=" + m.CanBeAmbushed + " exec=" + ok + " kind=" + ExecutionDirector.CurrentKind;
    }

    public static string State()
    {
        TypeDOverhaul oh = TypeDOverhaul.Active;
        if (oh == null) return "no overhaul";
        return $"stage={oh.CurrentStage} hp={oh.Health.CurrentHP}/{oh.Health.MaxHP} floor={oh.Health.HpFloor} react={oh.Boss.ActiveOverhaulReaction} special={oh.Boss.ActiveSpecialPattern} break={oh.Break.IsInBreak} exec={ExecutionDirector.CurrentKind} impact={oh.Impact.Impact:F0} banner={oh.Hud.LastBanner} prompt={ExecutionPromptHUD.LastPrompt} marker={ExecutionPromptHUD.MarkerVisible} last={oh.QaLastEvent}";
    }
}
#endif
