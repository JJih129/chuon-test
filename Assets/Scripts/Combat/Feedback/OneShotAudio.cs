using UnityEngine;

/// <summary>
/// Small shared one-shot SFX player (pooled 2D/3D AudioSources). Null-safe: a null clip is a no-op.
/// Volume is scaled by AudioOptionsRuntime.ScaleSfx so it respects the SFX option.
/// </summary>
public static class OneShotAudio
{
    const int PoolSize = 8;
    const float DefaultMinDistance = 2f;
    const float DefaultMaxDistance = 40f;

    static AudioSource[] s_pool;
    static int s_next;
    static GameObject s_root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_pool = null;
        s_next = 0;
        s_root = null;
    }

    /// <summary>Play a non-positional (2D) one-shot.</summary>
    public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        Play(clip, Vector3.zero, volume, pitch, 0f);
    }

    /// <summary>Play a one-shot at a world position. spatialBlend 0 = 2D, 1 = full 3D.</summary>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float spatialBlend = 0.6f)
    {
        Play(clip, position, volume, pitch, spatialBlend);
    }

    static void Play(AudioClip clip, Vector3 position, float volume, float pitch, float spatialBlend)
    {
        if (clip == null || !Application.isPlaying)
            return;

        AudioSource source = NextSource();
        if (source == null)
            return;

        source.transform.position = position;
        source.spatialBlend = Mathf.Clamp01(spatialBlend);
        source.pitch = Mathf.Approximately(pitch, 0f) ? 1f : pitch;
        source.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(Mathf.Clamp01(volume)));
    }

    static AudioSource NextSource()
    {
        if (s_root == null || s_pool == null)
            BuildPool();

        if (s_pool == null || s_pool.Length == 0)
            return null;

        // Prefer an idle source; otherwise round-robin steal.
        for (int i = 0; i < s_pool.Length; i++)
        {
            int index = (s_next + i) % s_pool.Length;
            AudioSource candidate = s_pool[index];
            if (candidate != null && !candidate.isPlaying)
            {
                s_next = (index + 1) % s_pool.Length;
                return candidate;
            }
        }

        AudioSource stolen = s_pool[s_next];
        s_next = (s_next + 1) % s_pool.Length;
        return stolen;
    }

    static void BuildPool()
    {
        s_root = new GameObject("OneShotAudioPool");
        Object.DontDestroyOnLoad(s_root);
        s_pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var child = new GameObject("OneShot_" + i);
            child.transform.SetParent(s_root.transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = DefaultMinDistance;
            source.maxDistance = DefaultMaxDistance;
            s_pool[i] = source;
        }
    }
}
