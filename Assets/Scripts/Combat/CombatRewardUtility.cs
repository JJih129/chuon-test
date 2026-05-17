using System.Collections.Generic;
using UnityEngine;

public static class CombatRewardUtility
{
    static readonly Dictionary<int, PlayerUltimateController> UltimateByRootId = new Dictionary<int, PlayerUltimateController>(8);
    static readonly Dictionary<int, MonoBehaviour> ParryReactByRootId = new Dictionary<int, MonoBehaviour>(8);
    static readonly Dictionary<int, MonoBehaviour> PerfectDodgeReactByRootId = new Dictionary<int, MonoBehaviour>(8);

    public static void TryGrantBasicAttackGauge(Transform attacker)
    {
        PlayerUltimateController ultimate = ResolveUltimateController(attacker);
        if (ultimate != null && ultimate.gaugePerH > 0f)
            ultimate.AddGauge(ultimate.gaugePerH);
    }

    public static void TryNotifyParryBreak(Transform attacker, GameObject parrier = null)
    {
        IParryReact parryReact = ResolveParryReact(attacker);
        if (parryReact != null)
            parryReact.OnParried(parrier, 0f, 0f);
    }

    public static void TryNotifyPerfectDodge(Transform attacker, GameObject dodger = null)
    {
        IPerfectDodgeReact perfectDodgeReact = ResolvePerfectDodgeReact(attacker);
        if (perfectDodgeReact != null)
            perfectDodgeReact.OnPerfectDodged(dodger);
    }

    static PlayerUltimateController ResolveUltimateController(Transform source)
    {
        if (source == null)
            return null;

        Transform root = source.root != null ? source.root : source;
        int rootId = root.GetInstanceID();
        if (UltimateByRootId.TryGetValue(rootId, out PlayerUltimateController cachedUltimate) && cachedUltimate != null)
            return cachedUltimate;

        PlayerUltimateController resolvedUltimate = source.GetComponentInParent<PlayerUltimateController>();
        if (resolvedUltimate == null && root != null)
            resolvedUltimate = root.GetComponentInChildren<PlayerUltimateController>(true);

        if (resolvedUltimate != null)
            UltimateByRootId[rootId] = resolvedUltimate;
        else
            UltimateByRootId.Remove(rootId);

        return resolvedUltimate;
    }

    static IParryReact ResolveParryReact(Transform source)
    {
        if (source == null)
            return null;

        Transform root = source.root != null ? source.root : source;
        int rootId = root.GetInstanceID();
        if (ParryReactByRootId.TryGetValue(rootId, out MonoBehaviour cachedReaction) && cachedReaction != null)
            return cachedReaction as IParryReact;

        MonoBehaviour resolvedReaction = null;
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IParryReact)
            {
                resolvedReaction = behaviours[i];
                break;
            }
        }

        if (resolvedReaction != null)
            ParryReactByRootId[rootId] = resolvedReaction;
        else
            ParryReactByRootId.Remove(rootId);

        return resolvedReaction as IParryReact;
    }

    static IPerfectDodgeReact ResolvePerfectDodgeReact(Transform source)
    {
        if (source == null)
            return null;

        Transform root = source.root != null ? source.root : source;
        int rootId = root.GetInstanceID();
        if (PerfectDodgeReactByRootId.TryGetValue(rootId, out MonoBehaviour cachedReaction) && cachedReaction != null)
            return cachedReaction as IPerfectDodgeReact;

        MonoBehaviour resolvedReaction = null;
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPerfectDodgeReact)
            {
                resolvedReaction = behaviours[i];
                break;
            }
        }

        if (resolvedReaction != null)
            PerfectDodgeReactByRootId[rootId] = resolvedReaction;
        else
            PerfectDodgeReactByRootId.Remove(rootId);

        return resolvedReaction as IPerfectDodgeReact;
    }
}
