using UnityEngine;

public enum PlayerWeaponBladeAnchorKind
{
    Tip,
    Base,
    Center,
    Edge
}

public static class PlayerWeaponVisualUtility
{
    const string DefaultSwordObjectName = "Object002";

    public static Transform FindSwordVisualTransform(Transform root)
    {
        if (root == null)
            return null;

        Transform object002 = FindChildRecursive(root, DefaultSwordObjectName);
        if (object002 != null)
            return object002;

        string[] preferredNames =
        {
            "sword",
            "katana",
            "blade",
            "weapon_r",
            "weapon",
            "sword_holder",
            "9CG_Sword(Clone)"
        };

        for (int i = 0; i < preferredNames.Length; i++)
        {
            Transform candidate = FindChildRecursive(root, preferredNames[i]);
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    public static Transform GetOrCreateBladeAnchor(
        Transform swordTransform,
        string anchorName,
        PlayerWeaponBladeAnchorKind kind,
        Transform gripReference,
        float padding = 0f)
    {
        if (swordTransform == null)
            return null;

        if (string.IsNullOrWhiteSpace(anchorName))
            anchorName = $"RuntimeWeapon{kind}Anchor";

        Transform existing = swordTransform.Find(anchorName);
        if (existing != null)
            return existing;

        GameObject anchorObject = new GameObject(anchorName);
        Transform anchor = anchorObject.transform;
        anchor.SetParent(swordTransform, false);
        anchor.localPosition = ResolveLocalAnchorPosition(swordTransform, kind, gripReference, padding);
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }

    public static bool TryGetBladeWorldPose(Transform swordTransform, Transform gripReference, out Vector3 basePosition, out Vector3 tipPosition, out Vector3 edgePosition)
    {
        basePosition = Vector3.zero;
        tipPosition = Vector3.zero;
        edgePosition = Vector3.zero;

        if (!TryGetBladeLocalPose(swordTransform, gripReference, out Vector3 localBase, out Vector3 localTip, out Vector3 localEdge))
            return false;

        basePosition = swordTransform.TransformPoint(localBase);
        tipPosition = swordTransform.TransformPoint(localTip);
        edgePosition = swordTransform.TransformPoint(localEdge);
        return true;
    }

    static Vector3 ResolveLocalAnchorPosition(Transform swordTransform, PlayerWeaponBladeAnchorKind kind, Transform gripReference, float padding)
    {
        if (!TryGetBladeLocalPose(swordTransform, gripReference, out Vector3 localBase, out Vector3 localTip, out Vector3 localEdge))
            return Vector3.zero;

        switch (kind)
        {
            case PlayerWeaponBladeAnchorKind.Tip:
                return ExtendAlongAxis(localBase, localTip, Mathf.Max(0f, padding));
            case PlayerWeaponBladeAnchorKind.Base:
                return ExtendAlongAxis(localTip, localBase, Mathf.Max(0f, padding));
            case PlayerWeaponBladeAnchorKind.Edge:
                return localEdge;
            default:
                return Vector3.Lerp(localBase, localTip, 0.52f);
        }
    }

    static bool TryGetBladeLocalPose(Transform swordTransform, Transform gripReference, out Vector3 localBase, out Vector3 localTip, out Vector3 localEdge)
    {
        localBase = Vector3.zero;
        localTip = Vector3.zero;
        localEdge = Vector3.zero;

        if (!TryGetLocalMeshBounds(swordTransform, out Bounds bounds))
            return false;

        Vector3 size = bounds.size;
        int bladeAxis = ResolveMajorAxis(size);
        Vector3 minPoint = bounds.center;
        Vector3 maxPoint = bounds.center;
        SetAxis(ref minPoint, bladeAxis, GetAxis(bounds.min, bladeAxis));
        SetAxis(ref maxPoint, bladeAxis, GetAxis(bounds.max, bladeAxis));

        Vector3 gripWorld = gripReference != null
            ? gripReference.position
            : swordTransform.parent != null ? swordTransform.parent.position : swordTransform.position;
        Vector3 minWorld = swordTransform.TransformPoint(minPoint);
        Vector3 maxWorld = swordTransform.TransformPoint(maxPoint);
        bool maxIsTip = (maxWorld - gripWorld).sqrMagnitude >= (minWorld - gripWorld).sqrMagnitude;

        localTip = maxIsTip ? maxPoint : minPoint;
        localBase = maxIsTip ? minPoint : maxPoint;

        int edgeAxis = ResolveSecondaryAxis(size, bladeAxis);
        localEdge = bounds.center;
        SetAxis(ref localEdge, edgeAxis, GetAxis(bounds.max, edgeAxis));
        return true;
    }

    static Vector3 ExtendAlongAxis(Vector3 from, Vector3 to, float distance)
    {
        Vector3 direction = to - from;
        if (direction.sqrMagnitude <= 0.000001f || distance <= 0f)
            return to;

        return to + direction.normalized * distance;
    }

    static int ResolveMajorAxis(Vector3 size)
    {
        if (size.y > size.x && size.y >= size.z)
            return 1;
        if (size.z > size.x && size.z >= size.y)
            return 2;
        return 0;
    }

    static int ResolveSecondaryAxis(Vector3 size, int majorAxis)
    {
        int bestAxis = majorAxis == 0 ? 1 : 0;
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == majorAxis)
                continue;
            if (GetAxis(size, axis) > GetAxis(size, bestAxis))
                bestAxis = axis;
        }

        return bestAxis;
    }

    static bool TryGetLocalMeshBounds(Transform target, out Bounds bounds)
    {
        MeshFilter meshFilter = target != null ? target.GetComponent<MeshFilter>() : null;
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        SkinnedMeshRenderer skinned = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
        if (skinned != null && skinned.sharedMesh != null)
        {
            bounds = skinned.sharedMesh.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        if (string.Equals(root.name, name, System.StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 1: return value.y;
            case 2: return value.z;
            default: return value.x;
        }
    }

    static void SetAxis(ref Vector3 value, int axis, float axisValue)
    {
        switch (axis)
        {
            case 1:
                value.y = axisValue;
                break;
            case 2:
                value.z = axisValue;
                break;
            default:
                value.x = axisValue;
                break;
        }
    }
}
