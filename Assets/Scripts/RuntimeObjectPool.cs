using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RuntimePooledObject : MonoBehaviour
{
    public int prefabKey;
}

public static class RuntimeObjectPool
{
    static readonly Dictionary<int, Stack<GameObject>> Pools = new Dictionary<int, Stack<GameObject>>();
    static Transform _poolRoot;

    public static GameObject Acquire(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null)
            return null;

        int key = prefab.GetInstanceID();
        GameObject instance = TryPop(key);
        if (instance == null)
        {
            instance = Object.Instantiate(prefab, position, rotation, parent);
            RuntimePooledObject marker = instance.GetComponent<RuntimePooledObject>();
            if (marker == null)
                marker = instance.AddComponent<RuntimePooledObject>();
            marker.prefabKey = key;
        }
        else
        {
            Transform tr = instance.transform;
            tr.SetParent(parent, false);
            tr.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
        }

        return instance;
    }

    public static void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0)
            return;

        int key = prefab.GetInstanceID();
        Stack<GameObject> pool = GetOrCreatePool(key);

        while (pool.Count < count)
        {
            GameObject instance = Object.Instantiate(prefab, EnsurePoolRoot());
            RuntimePooledObject marker = instance.GetComponent<RuntimePooledObject>();
            if (marker == null)
                marker = instance.AddComponent<RuntimePooledObject>();
            marker.prefabKey = key;
            instance.SetActive(false);
            pool.Push(instance);
        }
    }

    public static void Release(GameObject instance)
    {
        if (instance == null)
            return;

        RuntimePooledObject marker = instance.GetComponent<RuntimePooledObject>();
        if (marker == null || marker.prefabKey == 0)
        {
            Object.Destroy(instance);
            return;
        }

        Stack<GameObject> pool = GetOrCreatePool(marker.prefabKey);
        instance.transform.SetParent(EnsurePoolRoot(), false);
        instance.SetActive(false);
        pool.Push(instance);
    }

    static GameObject TryPop(int key)
    {
        if (!Pools.TryGetValue(key, out Stack<GameObject> pool))
            return null;

        while (pool.Count > 0)
        {
            GameObject instance = pool.Pop();
            if (instance != null)
                return instance;
        }

        return null;
    }

    static Stack<GameObject> GetOrCreatePool(int key)
    {
        if (!Pools.TryGetValue(key, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>();
            Pools[key] = pool;
        }

        return pool;
    }

    static Transform EnsurePoolRoot()
    {
        if (_poolRoot != null)
            return _poolRoot;

        GameObject root = new GameObject("__RuntimeObjectPool");
        root.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(root);
        _poolRoot = root.transform;
        return _poolRoot;
    }
}
