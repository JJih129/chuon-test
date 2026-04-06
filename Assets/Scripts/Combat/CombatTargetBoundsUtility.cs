using System.Collections.Generic;
using UnityEngine;

public static class CombatTargetBoundsUtility
{
    const float DefaultColliderRefreshInterval = 0.75f;
    const float MinColliderRefreshInterval = 0.1f;
    const int CacheSoftLimit = 64;

    struct CacheEntry
    {
        public Transform root;
        public Collider[] colliders;
        public float nextRefreshAt;
    }

    static readonly Dictionary<int, CacheEntry> Cache = new Dictionary<int, CacheEntry>(16);

    public static bool TryGetCombinedBounds(Transform root, out Bounds combinedBounds, float refreshInterval = DefaultColliderRefreshInterval)
    {
        combinedBounds = default;
        if (root == null)
            return false;

        Collider[] colliders = GetCachedColliders(root, refreshInterval);
        if (colliders == null || colliders.Length == 0)
            return false;

        bool hasBounds = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (!hasBounds)
            {
                combinedBounds = col.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(col.bounds);
            }
        }

        return hasBounds;
    }

    public static void Invalidate(Transform root)
    {
        if (root == null)
            return;

        Cache.Remove(root.GetInstanceID());
    }

    static Collider[] GetCachedColliders(Transform root, float refreshInterval)
    {
        int rootId = root.GetInstanceID();
        float now = Time.unscaledTime;

        if (Cache.TryGetValue(rootId, out CacheEntry entry))
        {
            if (entry.root == root && entry.colliders != null && now < entry.nextRefreshAt && !HasMissingColliders(entry.colliders))
                return entry.colliders;

            if (entry.root == null)
                Cache.Remove(rootId);
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        if (Cache.Count >= CacheSoftLimit)
            ClearDeadEntriesOrReset();

        Cache[rootId] = new CacheEntry
        {
            root = root,
            colliders = colliders,
            nextRefreshAt = now + Mathf.Max(MinColliderRefreshInterval, refreshInterval)
        };

        return colliders;
    }

    static bool HasMissingColliders(Collider[] colliders)
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                return true;
        }

        return false;
    }

    static void ClearDeadEntriesOrReset()
    {
        bool removedAny = false;
        List<int> deadKeys = null;

        foreach (KeyValuePair<int, CacheEntry> pair in Cache)
        {
            if (pair.Value.root != null)
                continue;

            if (deadKeys == null)
                deadKeys = new List<int>(4);

            deadKeys.Add(pair.Key);
        }

        if (deadKeys != null)
        {
            for (int i = 0; i < deadKeys.Count; i++)
                Cache.Remove(deadKeys[i]);

            removedAny = deadKeys.Count > 0;
        }

        if (!removedAny)
            Cache.Clear();
    }
}
