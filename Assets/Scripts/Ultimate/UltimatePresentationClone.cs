using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class UltimatePresentationClone : MonoBehaviour
{
    [SerializeField] Transform sourceRoot;
    [SerializeField] Transform cloneRoot;
    [SerializeField] Animator cloneAnimator;

    Renderer[] _sourceRenderers;
    bool[] _sourceRendererStates;

    public Transform CloneRoot => cloneRoot != null ? cloneRoot : transform;
    public Animator CloneAnimator => cloneAnimator;

    // ── 사전 생성 풀 ─────────────────────────────────────────────
    // 궁극기 시작 프레임에 플레이어+보스 비주얼을 Instantiate 하면 ~80ms 스파이크가 난다.
    // 도착 연출/로딩 중에 비활성 상태로 미리 만들어 두고(Prewarm), 사용 후에는 파괴 대신 풀로 되돌린다.
    sealed class PooledClone
    {
        public GameObject instance;
        public Animator animator;
        public HashSet<Transform> owned;
        public bool inUse;
        public bool usedOnce;
    }

    static readonly Dictionary<Transform, PooledClone> s_pool = new Dictionary<Transform, PooledClone>();
    static Transform s_poolHolder;
    PooledClone _pooled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPoolStatics()
    {
        s_pool.Clear();
        s_poolHolder = null;
    }

    static Transform PoolHolder
    {
        get
        {
            if (s_poolHolder == null)
            {
                GameObject holder = new GameObject("__UltimateClonePool");
                holder.SetActive(false);
                s_poolHolder = holder.transform;
            }

            return s_poolHolder;
        }
    }

    /// <summary>QA용: 풀에서 재사용/사전 생성된 클론으로 궁극기를 시작한 횟수.</summary>
    public static int PooledTakeCount { get; private set; }
    /// <summary>QA용: 현재 풀에 사전 생성된 사본 수.</summary>
    public static int PrewarmedCount => s_pool.Count;

    /// <summary>
    /// source 비주얼의 프레젠테이션 사본을 비활성 상태로 미리 만든다 (Awake/OnEnable 없음).
    /// 이미 있으면 그대로 반환. 반환값은 WarmRender 에 넘길 수 있는 인스턴스.
    /// </summary>
    public static GameObject Prewarm(Transform source)
    {
        if (source == null || !Application.isPlaying)
            return null;

        PruneDeadPoolEntries();
        if (s_pool.TryGetValue(source, out PooledClone existing) && existing.instance != null)
            return existing.instance;

        GameObject instance = Instantiate(source.gameObject, PoolHolder);
        instance.name = $"{source.gameObject.name}_Presentation";
        Transform root = instance.transform;
        Animator animator = ResolvePreferredAnimator(root, false);
        SanitizeHierarchy(root, animator);

        PooledClone pooled = new PooledClone
        {
            instance = instance,
            animator = animator,
            owned = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true))
        };
        s_pool[source] = pooled;
        return instance;
    }

    /// <summary>
    /// 사전 생성된 사본을 한 프레임 동안 source 와 같은 자세/위치로 활성화해서 GPU 리소스/Awake 를 미리 소진시킨다.
    /// 호출자가 다음 프레임에 EndWarmRender 를 불러야 한다.
    /// </summary>
    public static bool BeginWarmRender(Transform source, Transform warmParent)
    {
        if (source == null || warmParent == null || !s_pool.TryGetValue(source, out PooledClone pooled) || pooled.instance == null || pooled.inUse)
            return false;

        Transform root = pooled.instance.transform;
        root.SetParent(warmParent, false);
        root.SetPositionAndRotation(source.position, source.rotation);
        root.localScale = source.lossyScale;
        SetLayerRecursively(root, source.gameObject.layer);
        SyncHierarchyStateFromSource(source, root);
        Animator sourceAnimator = ResolvePreferredAnimator(source, true);
        if (pooled.animator != null && sourceAnimator != null)
            SyncAnimator(pooled.animator, sourceAnimator);
        return true;
    }

    public static void EndWarmRender(Transform source)
    {
        if (source == null || !s_pool.TryGetValue(source, out PooledClone pooled) || pooled.instance == null || pooled.inUse)
            return;

        pooled.instance.transform.SetParent(PoolHolder, false);
    }

    static void PruneDeadPoolEntries()
    {
        if (s_pool.Count == 0)
            return;

        List<Transform> dead = null;
        foreach (KeyValuePair<Transform, PooledClone> pair in s_pool)
        {
            if (pair.Key == null || pair.Value.instance == null)
                (dead ??= new List<Transform>()).Add(pair.Key);
        }

        if (dead == null)
            return;

        for (int i = 0; i < dead.Count; i++)
            s_pool.Remove(dead[i]);
    }

    // 사본을 만들 때와 지금의 source 가 달라졌을 수 있다(무기 표시 토글 등) → 활성/렌더러/머티리얼만 맞춘다.
    // 자식 인덱스+이름으로 병렬 순회. 런타임에 source 에 붙은 추가 자식(VFX 등)은 건너뛴다 (예전 Instantiate 경로와 달리 복제하지 않음).
    // 루트 이름이 다르면 false (다른 원본).
    static bool SyncHierarchyStateFromSource(Transform source, Transform clone)
    {
        if (source == null || clone == null)
            return false;

        SyncNode(source, clone, true);
        return true;
    }

    static void SyncNode(Transform src, Transform dst, bool isRoot)
    {
        if (!isRoot && dst.gameObject.activeSelf != src.gameObject.activeSelf)
            dst.gameObject.SetActive(src.gameObject.activeSelf);

        Renderer sr = src.GetComponent<Renderer>();
        Renderer dr = sr != null ? dst.GetComponent<Renderer>() : null;
        if (sr != null && dr != null)
        {
            dr.enabled = sr.enabled;
            Material[] sm = sr.sharedMaterials;
            Material[] dm = dr.sharedMaterials;
            bool same = sm.Length == dm.Length;
            for (int m = 0; same && m < sm.Length; m++)
                same = sm[m] == dm[m];
            if (!same)
                dr.sharedMaterials = sm;
        }

        int dstIndex = 0;
        for (int i = 0; i < src.childCount && dstIndex < dst.childCount; i++)
        {
            Transform sc = src.GetChild(i);
            Transform dc = dst.GetChild(dstIndex);
            if (sc.name != dc.name)
                continue; // source 에만 있는 런타임 자식

            SyncNode(sc, dc, false);
            dstIndex++;
        }
    }

    GameObject TakePooledInstance(Transform source)
    {
        if (!Application.isPlaying || source == null || !s_pool.TryGetValue(source, out PooledClone pooled) || pooled.instance == null || pooled.inUse)
            return null;

        Transform root = pooled.instance.transform;
        root.SetParent(transform, false);
        if (!SyncHierarchyStateFromSource(source, root))
        {
            // 구조가 달라졌다 → 풀 폐기 후 일반 경로
            s_pool.Remove(source);
            Destroy(pooled.instance);
            return null;
        }

        if (pooled.usedOnce && pooled.animator != null)
            pooled.animator.Rebind();

        pooled.inUse = true;
        pooled.usedOnce = true;
        _pooled = pooled;
        PooledTakeCount++;
        return pooled.instance;
    }

    void ReturnPooledInstance()
    {
        PooledClone pooled = _pooled;
        _pooled = null;
        if (pooled == null || pooled.instance == null)
            return;

        // 연출 중 사본 뼈에 붙은 외부 오브젝트(VFX 등)는 예전처럼 사본과 함께 정리한다.
        Transform[] all = pooled.instance.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && !pooled.owned.Contains(t) && t.parent != null && pooled.owned.Contains(t.parent))
            {
                t.gameObject.SetActive(false);
                Destroy(t.gameObject);
            }
        }

        pooled.instance.transform.SetParent(PoolHolder, false);
        pooled.inUse = false;
    }

    public void BuildFromSource(Transform source, Transform anchor, string cloneName, bool preserveSourceLocalPose = false)
    {
        ClearClone();
        if (source == null)
            return;

        sourceRoot = source;
        name = cloneName;
        transform.SetParent(anchor, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        GameObject instance = TakePooledInstance(source);
        bool fromPool = instance != null;
        if (!fromPool)
        {
            instance = Instantiate(source.gameObject, transform);
            instance.name = $"{source.gameObject.name}_Presentation";
        }
        cloneRoot = instance.transform;
        if (preserveSourceLocalPose)
        {
            ApplySourcePoseRelativeToAnchor(source, anchor, cloneRoot);
        }
        else
        {
            cloneRoot.localPosition = Vector3.zero;
            cloneRoot.localRotation = Quaternion.identity;
            cloneRoot.localScale = Vector3.one;
        }

        SetLayerRecursively(cloneRoot, anchor.gameObject.layer);

        if (fromPool)
        {
            // 이미 정리(Sanitize)된 사본. 애니메이터만 다시 설정.
            cloneAnimator = _pooled.animator != null ? _pooled.animator : ResolvePreferredAnimator(cloneRoot, true);
            ConfigureCloneAnimator(cloneAnimator);
        }
        else
        {
            cloneAnimator = ResolvePreferredAnimator(cloneRoot, true);
            SanitizeCloneHierarchy();
        }
        CacheAndHideSourceRenderers();
    }

    public void ClearClone()
    {
        RestoreSourceVisibility();

        if (cloneRoot != null && _pooled != null)
        {
            ReturnPooledInstance();
        }
        else if (cloneRoot != null)
        {
            cloneRoot.gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(cloneRoot.gameObject);
            else
                DestroyImmediate(cloneRoot.gameObject);
        }

        cloneRoot = null;
        cloneAnimator = null;
        sourceRoot = null;
    }

    public void RestoreSourceVisibility()
    {
        if (_sourceRenderers == null || _sourceRendererStates == null)
            return;

        for (int i = 0; i < _sourceRenderers.Length; i++)
        {
            if (_sourceRenderers[i] == null)
                continue;

            _sourceRenderers[i].enabled = i < _sourceRendererStates.Length && _sourceRendererStates[i];
        }

        _sourceRenderers = null;
        _sourceRendererStates = null;
    }

    public void SyncAnimatorFrom(Animator sourceAnimator)
    {
        SyncAnimator(cloneAnimator, sourceAnimator);
    }

    static void SyncAnimator(Animator cloneAnimator, Animator sourceAnimator)
    {
        if (cloneAnimator == null || sourceAnimator == null)
            return;

        if (cloneAnimator.runtimeAnimatorController == null || sourceAnimator.runtimeAnimatorController == null)
            return;

        AnimatorControllerParameter[] parameters = sourceAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (!HasParameter(cloneAnimator, parameter.name, parameter.type))
                continue;

            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                    cloneAnimator.SetBool(parameter.name, sourceAnimator.GetBool(parameter.name));
                    break;
                case AnimatorControllerParameterType.Float:
                    cloneAnimator.SetFloat(parameter.name, sourceAnimator.GetFloat(parameter.name));
                    break;
                case AnimatorControllerParameterType.Int:
                    cloneAnimator.SetInteger(parameter.name, sourceAnimator.GetInteger(parameter.name));
                    break;
            }
        }

        int layerCount = Mathf.Min(cloneAnimator.layerCount, sourceAnimator.layerCount);
        for (int layer = 0; layer < layerCount; layer++)
        {
            AnimatorStateInfo state = sourceAnimator.GetCurrentAnimatorStateInfo(layer);
            cloneAnimator.Play(state.fullPathHash, layer, Mathf.Repeat(state.normalizedTime, 1f));
        }

        cloneAnimator.Update(0f);
    }

    public bool TrySetTrigger(string triggerName)
    {
        if (cloneAnimator == null || string.IsNullOrWhiteSpace(triggerName))
            return false;

        if (!HasParameter(cloneAnimator, triggerName, AnimatorControllerParameterType.Trigger))
            return false;

        cloneAnimator.ResetTrigger(triggerName);
        cloneAnimator.SetTrigger(triggerName);
        return true;
    }

    public bool TryCrossFadeState(string statePath, float duration)
    {
        if (cloneAnimator == null || string.IsNullOrWhiteSpace(statePath) || cloneAnimator.runtimeAnimatorController == null)
            return false;

        if (!TryResolveState(cloneAnimator, statePath, out int layerIndex, out int stateHash))
            return false;

        cloneAnimator.CrossFadeInFixedTime(stateHash, Mathf.Max(0.01f, duration), layerIndex, 0f);
        return true;
    }

    public Transform ResolveMappedTransform(Transform sourceTransform)
    {
        if (cloneRoot == null || sourceRoot == null || sourceTransform == null)
            return cloneRoot;

        if (sourceTransform == sourceRoot)
            return cloneRoot;

        string relativePath = GetRelativePath(sourceRoot, sourceTransform);
        if (string.IsNullOrEmpty(relativePath))
            return cloneRoot;

        Transform mapped = cloneRoot.Find(relativePath);
        return mapped != null ? mapped : cloneRoot;
    }

    void CacheAndHideSourceRenderers()
    {
        if (sourceRoot == null)
            return;

        Renderer[] renderers = sourceRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        int sourceRendererCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (cloneRoot != null && renderer.transform.IsChildOf(cloneRoot))
                continue;

            sourceRendererCount++;
        }

        if (sourceRendererCount == 0)
            return;

        _sourceRenderers = new Renderer[sourceRendererCount];
        _sourceRendererStates = new bool[sourceRendererCount];
        int writeIndex = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (cloneRoot != null && renderer.transform.IsChildOf(cloneRoot))
                continue;

            _sourceRenderers[writeIndex] = renderer;
            _sourceRendererStates[writeIndex] = renderer.enabled;
            renderer.enabled = false;
            writeIndex++;
        }
    }

    void SanitizeCloneHierarchy()
    {
        SanitizeHierarchy(cloneRoot, cloneAnimator);
    }

    static void SanitizeHierarchy(Transform cloneRoot, Animator cloneAnimator)
    {
        if (cloneRoot == null)
            return;

        foreach (Animator animator in cloneRoot.GetComponentsInChildren<Animator>(true))
        {
            if (animator == null || animator == cloneAnimator)
                continue;

            animator.enabled = false;
        }

        foreach (MonoBehaviour runtimeScript in cloneRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (runtimeScript == null)
                continue;

            runtimeScript.enabled = false;
        }

        foreach (Behaviour behaviour in cloneRoot.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null || behaviour is Animator)
                continue;

            behaviour.enabled = false;
        }

        foreach (Collider collider in cloneRoot.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null)
                continue;

            collider.enabled = false;
        }

        foreach (CharacterController characterController in cloneRoot.GetComponentsInChildren<CharacterController>(true))
        {
            if (characterController == null)
                continue;

            characterController.enabled = false;
        }

        foreach (Rigidbody rigidbody in cloneRoot.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rigidbody == null)
                continue;

            rigidbody.detectCollisions = false;
            rigidbody.isKinematic = true;
        }

        ConfigureCloneAnimator(cloneAnimator);
    }

    static void ConfigureCloneAnimator(Animator cloneAnimator)
    {
        if (cloneAnimator == null)
            return;

        cloneAnimator.enabled = true;
        cloneAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        cloneAnimator.applyRootMotion = false;
        cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    static Animator ResolvePreferredAnimator(Transform root, bool requireActiveInHierarchy)
    {
        if (root == null)
            return null;

        Animator directAnimator = root.GetComponent<Animator>();
        if (IsUsableAnimator(directAnimator, root, requireActiveInHierarchy))
            return directAnimator;

        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        if (animators == null || animators.Length == 0)
            return directAnimator;

        Animator fallbackAnimator = directAnimator;
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null)
                continue;

            if (IsUsableAnimator(animator, root, requireActiveInHierarchy))
                return animator;

            if (fallbackAnimator == null)
                fallbackAnimator = animator;
        }

        return fallbackAnimator;
    }

    static bool IsUsableAnimator(Animator animator, Transform root, bool requireActiveInHierarchy)
    {
        if (animator == null || animator.avatar == null || animator.runtimeAnimatorController == null)
            return false;

        if (requireActiveInHierarchy)
            return animator.gameObject.activeInHierarchy;

        // 비활성 풀 아래에서 만든 사본: root 까지의 activeSelf 체인으로 판정
        for (Transform t = animator.transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf)
                return false;
            if (t == root)
                break;
        }

        return true;
    }

    static void ApplySourcePoseRelativeToAnchor(Transform source, Transform anchor, Transform target)
    {
        if (source == null || anchor == null || target == null)
            return;

        if (source.parent == anchor)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            return;
        }

        target.position = source.position;
        target.rotation = source.rotation;
        target.localScale = source.lossyScale;
    }

    static bool HasParameter(Animator cloneAnimator, string name, AnimatorControllerParameterType type)
    {
        if (cloneAnimator == null)
            return false;

        AnimatorControllerParameter[] parameters = cloneAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == type && parameter.name == name)
                return true;
        }

        return false;
    }

    static bool TryResolveState(Animator animator, string statePath, out int layerIndex, out int stateHash)
    {
        layerIndex = 0;
        stateHash = 0;
        if (animator == null || string.IsNullOrWhiteSpace(statePath))
            return false;

        string trimmed = statePath.Trim();
        int separator = trimmed.IndexOf('.');
        if (separator > 0)
        {
            string layerName = trimmed.Substring(0, separator);
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetLayerName(i) != layerName)
                    continue;

                int fullPathHash = Animator.StringToHash(trimmed);
                if (!animator.HasState(i, fullPathHash))
                    return false;

                layerIndex = i;
                stateHash = fullPathHash;
                return true;
            }
        }

        for (int i = 0; i < animator.layerCount; i++)
        {
            string fullPath = $"{animator.GetLayerName(i)}.{trimmed}";
            int fullPathHash = Animator.StringToHash(fullPath);
            if (!animator.HasState(i, fullPathHash))
                continue;

            layerIndex = i;
            stateHash = fullPathHash;
            return true;
        }

        return false;
    }

    static string GetRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return string.Empty;

        if (root == target)
            return string.Empty;

        List<string> segments = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            segments.Add(current.name);
            current = current.parent;
        }

        if (current != root)
            return string.Empty;

        segments.Reverse();
        return string.Join("/", segments);
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
