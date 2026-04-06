using System;
using UnityEngine;

[DisallowMultipleComponent]
public class BossVisualRig : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator sourceAnimator;
    [SerializeField] private Avatar avatar;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private bool treatPlaneNamedMeshesAsHelpers = true;

    public Transform VisualRoot => visualRoot ? visualRoot : transform;
    public Animator SourceAnimator => sourceAnimator;
    public Avatar Avatar => avatar != null ? avatar : ResolveFallbackAvatar();
    public Renderer[] Renderers => FilterValidObjects(renderers);
    public bool TreatPlaneNamedMeshesAsHelpers => treatPlaneNamedMeshesAsHelpers;

    void Reset()
    {
        AutoWire();
        ApplyRendererRuntimeSettings();
    }

    void Awake()
    {
        AutoWire();
        ApplyRendererRuntimeSettings();
    }

    void OnEnable()
    {
        ApplyRendererRuntimeSettings();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            AutoWire();
            ApplyRendererRuntimeSettings();
        }
    }
#endif

    public void SyncSerializedReferences()
    {
        AutoWire();
        ApplyRendererRuntimeSettings();
    }

    public void SetAvatarForSync(Avatar syncedAvatar)
    {
        avatar = syncedAvatar;
    }

    public void SetSourceAnimatorForSync(Animator syncedAnimator)
    {
        sourceAnimator = syncedAnimator;
    }

    public void SetRenderersForSync(Renderer[] syncedRenderers)
    {
        renderers = FilterValidObjects(syncedRenderers);
        ApplyRendererRuntimeSettings();
    }

    public void SetTreatPlaneNamedMeshesAsHelpersForSync(bool shouldTreatAsHelpers)
    {
        treatPlaneNamedMeshesAsHelpers = shouldTreatAsHelpers;
    }

    void AutoWire()
    {
        if (!visualRoot)
            visualRoot = transform;

        if (!sourceAnimator)
            sourceAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (!avatar && sourceAnimator != null)
            avatar = sourceAnimator.avatar;

        if (!HasValidObjects(renderers))
            renderers = GetComponentsInChildren<Renderer>(true);

        renderers = FilterValidObjects(renderers);
    }

    Avatar ResolveFallbackAvatar()
    {
        if (sourceAnimator != null && sourceAnimator.avatar != null)
            return sourceAnimator.avatar;

        Animator parentAnimator = GetComponentInParent<Animator>();
        if (parentAnimator != null)
            return parentAnimator.avatar;

        return null;
    }

    void ApplyRendererRuntimeSettings()
    {
        Transform root = VisualRoot;
        if (root != null)
            NormalizeVisibleHierarchy(root);

        Renderer[] currentRenderers = Renderers;
        if (currentRenderers == null)
            return;

        for (int i = 0; i < currentRenderers.Length; i++)
        {
            Renderer renderer = currentRenderers[i];
            if (renderer == null)
                continue;

            if (IsHelperPlane(renderer.transform))
            {
                if (renderer.enabled)
                    renderer.enabled = false;
                continue;
            }

            if (!renderer.gameObject.activeSelf)
                renderer.gameObject.SetActive(true);

            if (!renderer.enabled)
                renderer.enabled = true;

            SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            if (skinnedMeshRenderer != null)
                skinnedMeshRenderer.updateWhenOffscreen = true;
        }
    }

    void NormalizeVisibleHierarchy(Transform root)
    {
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            bool helperPlane = IsHelperPlane(child);
            if (helperPlane)
            {
                if (child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }
            else
            {
                if (!child.gameObject.activeSelf)
                    child.gameObject.SetActive(true);

                NormalizeVisibleHierarchy(child);
            }
        }
    }

    bool IsHelperPlane(Transform target)
    {
        return treatPlaneNamedMeshesAsHelpers
            && target != null
            && target.name.StartsWith("Plane", System.StringComparison.OrdinalIgnoreCase);
    }

    static bool HasValidObjects<T>(T[] objects) where T : UnityEngine.Object
    {
        if (objects == null || objects.Length == 0)
            return false;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i])
                return true;
        }

        return false;
    }

    static T[] FilterValidObjects<T>(T[] objects) where T : UnityEngine.Object
    {
        if (objects == null || objects.Length == 0)
            return null;

        int validCount = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i])
                validCount++;
        }

        if (validCount == 0)
            return null;

        if (validCount == objects.Length)
            return objects;

        T[] filtered = new T[validCount];
        int index = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            if (!objects[i])
                continue;

            filtered[index++] = objects[i];
        }

        return filtered;
    }
}
