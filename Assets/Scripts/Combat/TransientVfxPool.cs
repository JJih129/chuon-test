using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TransientVfxPool : MonoBehaviour
{
    struct ActiveLease
    {
        public GameObject Prefab;
        public PooledInstance Instance;
        public int LeaseId;
        public float RecycleAt;
    }

    sealed class PooledInstance : MonoBehaviour
    {
        public GameObject Prefab;
        public int LeaseId;
        public ParticleSystem[] ParticleSystems = Array.Empty<ParticleSystem>();
        public TrailRenderer[] TrailRenderers = Array.Empty<TrailRenderer>();
        public Animator[] Animators = Array.Empty<Animator>();
        public AudioSource[] AudioSources = Array.Empty<AudioSource>();
        public float CachedLifetime = -1f;

        public void CacheComponents()
        {
            ParticleSystems = GetComponentsInChildren<ParticleSystem>(true);
            TrailRenderers = GetComponentsInChildren<TrailRenderer>(true);
            Animators = GetComponentsInChildren<Animator>(true);
            AudioSources = GetComponentsInChildren<AudioSource>(true);
        }
    }

    static TransientVfxPool _instance;

    readonly Dictionary<GameObject, Queue<PooledInstance>> _pool = new();
    readonly Dictionary<GameObject, Transform> _poolRoots = new();
    readonly List<ActiveLease> _activeLeases = new(64);

    void Awake()
    {
        enabled = false;
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null,
        float fallbackLifetime = 1.25f)
    {
        if (prefab == null)
            return null;

        return Instance.SpawnInternal(prefab, position, rotation, parent, fallbackLifetime);
    }

    static TransientVfxPool Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            GameObject root = new GameObject("__TransientVfxPool");
            root.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<TransientVfxPool>();
            return _instance;
        }
    }

    GameObject SpawnInternal(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent,
        float fallbackLifetime)
    {
        PooledInstance pooled = GetPooledInstance(prefab, parent);
        if (pooled == null)
            return null;

        pooled.LeaseId++;
        Transform pooledTransform = pooled.transform;
        pooledTransform.SetParent(parent, false);
        pooledTransform.SetPositionAndRotation(position, rotation);
        pooledTransform.localScale = prefab.transform.localScale;
        pooled.gameObject.SetActive(true);

        RestartInstance(pooled);

        float lifetime = Mathf.Max(0.05f, ResolveLifetime(pooled, fallbackLifetime));
        _activeLeases.Add(new ActiveLease
        {
            Prefab = prefab,
            Instance = pooled,
            LeaseId = pooled.LeaseId,
            RecycleAt = Time.time + lifetime
        });

        if (!enabled)
            enabled = true;

        return pooled.gameObject;
    }

    PooledInstance GetPooledInstance(GameObject prefab, Transform parent)
    {
        if (!_pool.TryGetValue(prefab, out Queue<PooledInstance> queue))
        {
            queue = new Queue<PooledInstance>();
            _pool[prefab] = queue;
        }

        while (queue.Count > 0)
        {
            PooledInstance pooled = queue.Dequeue();
            if (pooled != null)
                return pooled;
        }

        GameObject instance = Instantiate(prefab, parent);
        instance.name = $"{prefab.name}_Pooled";
        PooledInstance marker = instance.GetComponent<PooledInstance>();
        if (marker == null)
            marker = instance.AddComponent<PooledInstance>();
        marker.Prefab = prefab;
        marker.CacheComponents();
        return marker;
    }

    void Recycle(GameObject prefab, PooledInstance pooled)
    {
        if (pooled == null)
            return;

        Transform poolRoot = GetOrCreatePoolRoot(prefab);
        pooled.gameObject.SetActive(false);
        pooled.transform.SetParent(poolRoot, false);

        if (!_pool.TryGetValue(prefab, out Queue<PooledInstance> queue))
        {
            queue = new Queue<PooledInstance>();
            _pool[prefab] = queue;
        }

        queue.Enqueue(pooled);
    }

    void Update()
    {
        if (_activeLeases.Count == 0)
        {
            enabled = false;
            return;
        }

        float now = Time.time;
        for (int i = _activeLeases.Count - 1; i >= 0; i--)
        {
            ActiveLease lease = _activeLeases[i];
            if (lease.Instance == null)
            {
                _activeLeases.RemoveAt(i);
                continue;
            }

            if (lease.Instance.LeaseId != lease.LeaseId)
            {
                _activeLeases.RemoveAt(i);
                continue;
            }

            if (now < lease.RecycleAt)
                continue;

            Recycle(lease.Prefab, lease.Instance);
            _activeLeases.RemoveAt(i);
        }

        if (_activeLeases.Count == 0)
            enabled = false;
    }

    Transform GetOrCreatePoolRoot(GameObject prefab)
    {
        if (_poolRoots.TryGetValue(prefab, out Transform existing) && existing != null)
            return existing;

        GameObject root = new GameObject($"{prefab.name}_Pool");
        root.hideFlags = HideFlags.HideInHierarchy;
        root.transform.SetParent(transform, false);
        _poolRoots[prefab] = root.transform;
        return root.transform;
    }

    static void RestartInstance(PooledInstance pooled)
    {
        if (pooled == null)
            return;

        for (int i = 0; i < pooled.ParticleSystems.Length; i++)
        {
            ParticleSystem particle = pooled.ParticleSystems[i];
            particle.Clear(true);
            particle.Play(true);
        }

        for (int i = 0; i < pooled.TrailRenderers.Length; i++)
            pooled.TrailRenderers[i].Clear();

        for (int i = 0; i < pooled.Animators.Length; i++)
        {
            Animator animator = pooled.Animators[i];
            animator.Rebind();
            animator.Update(0f);
        }

        for (int i = 0; i < pooled.AudioSources.Length; i++)
        {
            AudioSource audioSource = pooled.AudioSources[i];
            if (audioSource.playOnAwake)
            {
                audioSource.Stop();
                audioSource.Play();
            }
        }
    }

    static float ResolveLifetime(PooledInstance pooled, float fallbackLifetime)
    {
        if (pooled == null)
            return fallbackLifetime;

        if (pooled.CachedLifetime > 0f)
            return pooled.CachedLifetime;

        float lifetime = 0f;

        for (int i = 0; i < pooled.ParticleSystems.Length; i++)
        {
            ParticleSystem.MainModule main = pooled.ParticleSystems[i].main;
            float duration = main.duration;
            float startLifetime = main.startLifetime.constantMax;
            lifetime = Mathf.Max(lifetime, duration + startLifetime);
        }

        for (int i = 0; i < pooled.AudioSources.Length; i++)
        {
            if (pooled.AudioSources[i].clip != null && pooled.AudioSources[i].playOnAwake)
                lifetime = Mathf.Max(lifetime, pooled.AudioSources[i].clip.length);
        }

        for (int i = 0; i < pooled.Animators.Length; i++)
        {
            RuntimeAnimatorController controller = pooled.Animators[i].runtimeAnimatorController;
            if (controller == null)
                continue;

            AnimationClip[] clips = controller.animationClips;
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                if (clips[clipIndex] != null)
                    lifetime = Mathf.Max(lifetime, clips[clipIndex].length);
            }
        }

        pooled.CachedLifetime = lifetime > 0f ? lifetime : fallbackLifetime;
        return pooled.CachedLifetime;
    }
}
