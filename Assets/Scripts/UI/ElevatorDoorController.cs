using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ElevatorDoorController : MonoBehaviour
{
    [SerializeField] GameObject closedDoorVisual;
    [SerializeField] GameObject openDoorVisual;
    [SerializeField] Transform[] grillePanels;
    [SerializeField] Transform[] doorPanels;
    [SerializeField] bool usePanelMotion = true;
    [SerializeField, Min(0f)] float grilleOpenDuration = 0.85f;
    [SerializeField, Min(0f)] float doorOpenDelayAfterGrille = 0.12f;
    [SerializeField, Min(0f)] float doorOpenDuration = 1.1f;
    [SerializeField, Min(0f)] float doorOpenLift = 4.2f;

    Vector3[] _grilleClosedPositions;
    Vector3[] _doorClosedPositions;

    public void ConfigureLegacy(
        GameObject closedVisual,
        GameObject openVisual,
        Transform[] grilles,
        Transform[] doors,
        bool panelMotion,
        float grilleDuration,
        float delayAfterGrille,
        float doorDuration,
        float lift)
    {
        closedDoorVisual = closedDoorVisual != null ? closedDoorVisual : closedVisual;
        openDoorVisual = openDoorVisual != null ? openDoorVisual : openVisual;
        grillePanels = HasPanels(grillePanels) ? grillePanels : grilles;
        doorPanels = HasPanels(doorPanels) ? doorPanels : doors;
        usePanelMotion = panelMotion;
        grilleOpenDuration = Mathf.Max(0f, grilleDuration);
        doorOpenDelayAfterGrille = Mathf.Max(0f, delayAfterGrille);
        doorOpenDuration = Mathf.Max(0f, doorDuration);
        doorOpenLift = Mathf.Max(0f, lift);
        EnsurePanelCache();
    }

    public void SetOpenImmediate(bool open)
    {
        SetVisualState(open);
        if (!usePanelMotion)
            return;

        EnsurePanelCache();
        ApplyPanelOpenAmount(grillePanels, _grilleClosedPositions, open ? 1f : 0f);
        ApplyPanelOpenAmount(doorPanels, _doorClosedPositions, open ? 1f : 0f);
    }

    public IEnumerator CoSetOpen(bool open)
    {
        SetVisualState(open);
        if (!usePanelMotion)
            yield break;

        EnsurePanelCache();
        if (open)
        {
            yield return CoAnimatePanels(grillePanels, _grilleClosedPositions, grilleOpenDuration, true);
            if (doorOpenDelayAfterGrille > 0f)
                yield return new WaitForSeconds(doorOpenDelayAfterGrille);
            yield return CoAnimatePanels(doorPanels, _doorClosedPositions, doorOpenDuration, true);
        }
        else
        {
            yield return CoAnimatePanels(doorPanels, _doorClosedPositions, doorOpenDuration, false);
            yield return CoAnimatePanels(grillePanels, _grilleClosedPositions, grilleOpenDuration, false);
        }
    }

    public void AutoBindFallbackPanels(Transform elevatorRoot)
    {
        if (!usePanelMotion || elevatorRoot == null)
            return;

        if (!HasPanels(grillePanels))
            grillePanels = FindFallbackElevatorDoorPanels(elevatorRoot, true);
        if (!HasPanels(doorPanels))
            doorPanels = FindFallbackElevatorDoorPanels(elevatorRoot, false);

        EnsurePanelCache();
    }

    void SetVisualState(bool open)
    {
        if (closedDoorVisual != null)
            closedDoorVisual.SetActive(!open);
        if (openDoorVisual != null)
            openDoorVisual.SetActive(open);
    }

    void EnsurePanelCache()
    {
        _grilleClosedPositions = EnsurePanelClosedPositionCache(grillePanels, _grilleClosedPositions);
        _doorClosedPositions = EnsurePanelClosedPositionCache(doorPanels, _doorClosedPositions);
    }

    IEnumerator CoAnimatePanels(Transform[] panels, Vector3[] closedPositions, float duration, bool open)
    {
        if (duration <= 0f || !HasPanels(panels))
        {
            ApplyPanelOpenAmount(panels, closedPositions, open ? 1f : 0f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            ApplyPanelOpenAmount(panels, closedPositions, open ? eased : 1f - eased);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyPanelOpenAmount(panels, closedPositions, open ? 1f : 0f);
    }

    void ApplyPanelOpenAmount(Transform[] panels, Vector3[] closedPositions, float open01)
    {
        if (closedPositions == null || !HasPanels(panels))
            return;

        float lift = doorOpenLift * Mathf.Clamp01(open01);
        int count = Mathf.Min(panels.Length, closedPositions.Length);
        for (int i = 0; i < count; i++)
        {
            Transform panel = panels[i];
            if (panel != null)
                panel.position = closedPositions[i] + Vector3.up * lift;
        }
    }

    static bool HasPanels(Transform[] panels) => panels != null && panels.Length > 0;

    static Vector3[] EnsurePanelClosedPositionCache(Transform[] panels, Vector3[] cachedPositions)
    {
        if (!HasPanels(panels))
            return cachedPositions;
        if (cachedPositions != null && cachedPositions.Length == panels.Length)
            return cachedPositions;

        cachedPositions = new Vector3[panels.Length];
        for (int i = 0; i < panels.Length; i++)
            cachedPositions[i] = panels[i] != null ? panels[i].position : Vector3.zero;

        return cachedPositions;
    }

    static Transform[] FindFallbackElevatorDoorPanels(Transform elevatorRoot, bool grille)
    {
        if (elevatorRoot == null)
            return System.Array.Empty<Transform>();

        Renderer[] renderers = elevatorRoot.GetComponentsInChildren<Renderer>(true);
        var panels = new List<Transform>(24);
        var unique = new HashSet<Transform>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !IsFallbackElevatorDoorRenderer(renderer, grille))
                continue;

            AddUniqueDoorPanelTransform(panels, unique, renderer.transform);
            if (renderer is not SkinnedMeshRenderer skinned)
                continue;

            AddUniqueDoorPanelTransform(panels, unique, skinned.rootBone);
            Transform[] bones = skinned.bones;
            if (bones == null)
                continue;

            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                AddUniqueDoorPanelTransform(panels, unique, bones[boneIndex]);
        }

        panels.Sort((a, b) => a.position.y.CompareTo(b.position.y));
        return panels.ToArray();
    }

    static bool IsFallbackElevatorDoorRenderer(Renderer renderer, bool grille)
    {
        Transform child = renderer.transform;
        string objectName = child.name;
        bool knownDoorName =
            objectName.StartsWith("elevator002", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("elevator003", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("elevator004", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("elevator005", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("Dummy00", System.StringComparison.OrdinalIgnoreCase);

        if (!knownDoorName)
            return false;

        Bounds bounds = renderer.bounds;
        Vector3 size = bounds.size;
        if (size.x < 1.2f || size.y > 5.2f)
            return false;

        Vector3 localPosition = child.localPosition;
        bool frontDoorLayer = localPosition.z > 0.1f && localPosition.z < 0.8f;
        bool importedDoorLayer = localPosition.z < -10f && localPosition.y < -10f;
        if (!frontDoorLayer && !importedDoorLayer)
            return false;

        if (grille)
            return objectName.StartsWith("Dummy00", System.StringComparison.OrdinalIgnoreCase) ||
                   objectName.StartsWith("elevator002", System.StringComparison.OrdinalIgnoreCase) ||
                   size.z > 0.8f;

        return objectName.StartsWith("elevator003", System.StringComparison.OrdinalIgnoreCase) ||
               objectName.StartsWith("elevator004", System.StringComparison.OrdinalIgnoreCase) ||
               objectName.StartsWith("elevator005", System.StringComparison.OrdinalIgnoreCase);
    }

    static void AddUniqueDoorPanelTransform(List<Transform> panels, HashSet<Transform> unique, Transform target)
    {
        if (target != null && unique.Add(target))
            panels.Add(target);
    }
}
