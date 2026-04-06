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

        GameObject instance = Instantiate(source.gameObject, transform);
        instance.name = $"{source.gameObject.name}_Presentation";
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

        cloneAnimator = ResolvePreferredAnimator(cloneRoot);
        SanitizeCloneHierarchy();
        CacheAndHideSourceRenderers();
    }

    public void ClearClone()
    {
        RestoreSourceVisibility();

        if (cloneRoot != null)
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
        if (cloneAnimator == null || sourceAnimator == null)
            return;

        if (cloneAnimator.runtimeAnimatorController == null || sourceAnimator.runtimeAnimatorController == null)
            return;

        AnimatorControllerParameter[] parameters = sourceAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (!HasParameter(parameter.name, parameter.type))
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

        if (!HasParameter(triggerName, AnimatorControllerParameterType.Trigger))
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

        if (cloneAnimator == null)
            return;

        cloneAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        cloneAnimator.applyRootMotion = false;
        cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    static Animator ResolvePreferredAnimator(Transform root)
    {
        if (root == null)
            return null;

        Animator directAnimator = root.GetComponent<Animator>();
        if (IsUsableAnimator(directAnimator))
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

            if (IsUsableAnimator(animator))
                return animator;

            if (fallbackAnimator == null)
                fallbackAnimator = animator;
        }

        return fallbackAnimator;
    }

    static bool IsUsableAnimator(Animator animator)
    {
        return animator != null
            && animator.avatar != null
            && animator.runtimeAnimatorController != null
            && animator.gameObject.activeInHierarchy;
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

    bool HasParameter(string name, AnimatorControllerParameterType type)
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
