using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;

public static class CinemachineCompat
{
    static readonly Type LegacyFreeLookType = Type.GetType("Unity.Cinemachine.CinemachineFreeLook, Unity.Cinemachine");

    static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Dictionary<int, CinemachineComponentBase> BodyComponentCache = new Dictionary<int, CinemachineComponentBase>(16);
    static readonly Dictionary<Type, BodyOffsetAccessor> BodyOffsetAccessorCache = new Dictionary<Type, BodyOffsetAccessor>(8);
    static readonly Dictionary<Type, LegacyFreeLookAccessor> LegacyFreeLookAccessorCache = new Dictionary<Type, LegacyFreeLookAccessor>(4);

    sealed class BodyOffsetAccessor
    {
        public PropertyInfo FollowOffsetProperty;
        public FieldInfo LegacyFollowOffsetField;
    }

    sealed class AxisAccessor
    {
        public FieldInfo AxisField;
        public PropertyInfo ValueProperty;
        public FieldInfo InputAxisValueField;
    }

    sealed class LegacyFreeLookAccessor
    {
        public AxisAccessor XAxis;
        public AxisAccessor YAxis;
        public FieldInfo LensField;
        public FieldInfo OrbitsField;
        public FieldInfo OrbitRadiusField;
    }

    public static bool IsLegacyFreeLook(Component component)
    {
        return component != null
            && LegacyFreeLookType != null
            && LegacyFreeLookType.IsAssignableFrom(component.GetType());
    }

    public static CinemachineVirtualCameraBase FindLegacyFreeLookCamera()
    {
        CinemachineVirtualCameraBase[] cameras = UnityEngine.Object.FindObjectsByType<CinemachineVirtualCameraBase>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < cameras.Length; i++)
        {
            if (IsLegacyFreeLook(cameras[i]))
                return cameras[i];
        }

        return null;
    }

    public static bool TryGetBodyFollowOffset(CinemachineVirtualCameraBase camera, out Vector3 offset)
    {
        offset = default;
        CinemachineComponentBase body = ResolveBodyComponent(camera);
        if (body == null)
            return false;

        BodyOffsetAccessor accessor = ResolveBodyOffsetAccessor(body.GetType());
        if (accessor.FollowOffsetProperty != null)
        {
            offset = (Vector3)accessor.FollowOffsetProperty.GetValue(body);
            return true;
        }

        if (accessor.LegacyFollowOffsetField != null)
        {
            offset = (Vector3)accessor.LegacyFollowOffsetField.GetValue(body);
            return true;
        }

        return false;
    }

    public static bool TrySetBodyFollowOffset(CinemachineVirtualCameraBase camera, Vector3 offset)
    {
        CinemachineComponentBase body = ResolveBodyComponent(camera);
        if (body == null)
            return false;

        BodyOffsetAccessor accessor = ResolveBodyOffsetAccessor(body.GetType());
        if (accessor.FollowOffsetProperty != null)
        {
            accessor.FollowOffsetProperty.SetValue(body, offset);
            return true;
        }

        if (accessor.LegacyFollowOffsetField != null)
        {
            accessor.LegacyFollowOffsetField.SetValue(body, offset);
            return true;
        }

        return false;
    }

    public static bool TryCopyLensFromUnityCamera(CinemachineVirtualCameraBase camera, Camera sourceCamera)
    {
        if (camera == null || sourceCamera == null)
            return false;

        if (camera is CinemachineCamera cmCamera)
        {
            cmCamera.Lens = LensSettings.FromCamera(sourceCamera);
            return true;
        }

        FieldInfo legacyLensField = camera.GetType().GetField("m_Lens", AnyInstance);
        if (legacyLensField != null && legacyLensField.FieldType == typeof(LensSettings))
        {
            LensSettings lens = LensSettings.FromCamera(sourceCamera);
            legacyLensField.SetValue(camera, lens);
            return true;
        }

        return false;
    }

    public static bool TryGetLegacyFreeLookAxes(Component freeLook, out float xAxis, out float yAxis)
    {
        xAxis = 0f;
        yAxis = 0f;
        if (!IsLegacyFreeLook(freeLook))
            return false;

        LegacyFreeLookAccessor accessor = ResolveLegacyFreeLookAccessor(freeLook.GetType());
        if (!TryGetLegacyAxisValue(freeLook, accessor.XAxis, out xAxis))
            return false;

        if (!TryGetLegacyAxisValue(freeLook, accessor.YAxis, out yAxis))
            return false;

        return true;
    }

    public static bool TrySetLegacyFreeLookAxes(Component freeLook, float xAxis, float yAxis, bool clearInput = true)
    {
        if (!IsLegacyFreeLook(freeLook))
            return false;

        LegacyFreeLookAccessor accessor = ResolveLegacyFreeLookAccessor(freeLook.GetType());
        bool wroteX = TrySetLegacyAxisValue(freeLook, accessor.XAxis, xAxis, clearInput);
        bool wroteY = TrySetLegacyAxisValue(freeLook, accessor.YAxis, yAxis, clearInput);
        return wroteX && wroteY;
    }

    public static bool TryAdjustLegacyFreeLookLens(Component freeLook, float deltaFov, float minFov, float maxFov)
    {
        if (!IsLegacyFreeLook(freeLook))
            return false;

        LegacyFreeLookAccessor accessor = ResolveLegacyFreeLookAccessor(freeLook.GetType());
        if (accessor.LensField == null)
            return false;

        LensSettings lens = (LensSettings)accessor.LensField.GetValue(freeLook);
        lens.FieldOfView = Mathf.Clamp(lens.FieldOfView + deltaFov, minFov, maxFov);
        accessor.LensField.SetValue(freeLook, lens);
        return true;
    }

    public static bool TryAdjustLegacyFreeLookOrbitsRadius(Component freeLook, float deltaRadius, float minRadius, float maxRadius)
    {
        if (!IsLegacyFreeLook(freeLook))
            return false;

        LegacyFreeLookAccessor accessor = ResolveLegacyFreeLookAccessor(freeLook.GetType());
        if (accessor.OrbitsField == null || accessor.OrbitRadiusField == null)
            return false;

        object orbitArrayObject = accessor.OrbitsField.GetValue(freeLook);
        if (orbitArrayObject is not Array orbits || orbits.Length == 0)
            return false;

        for (int i = 0; i < orbits.Length; i++)
        {
            object orbit = orbits.GetValue(i);
            if (orbit == null)
                continue;

            float radius = (float)accessor.OrbitRadiusField.GetValue(orbit);
            radius = Mathf.Clamp(radius + deltaRadius, minRadius, maxRadius);
            accessor.OrbitRadiusField.SetValue(orbit, radius);
            orbits.SetValue(orbit, i);
        }

        accessor.OrbitsField.SetValue(freeLook, orbits);
        return true;
    }

    static CinemachineComponentBase ResolveBodyComponent(CinemachineVirtualCameraBase camera)
    {
        if (camera == null)
            return null;

        int instanceId = camera.GetInstanceID();
        if (BodyComponentCache.TryGetValue(instanceId, out CinemachineComponentBase cachedBody) && cachedBody != null)
            return cachedBody;

        CinemachineComponentBase body = camera.GetCinemachineComponent(CinemachineCore.Stage.Body);
        if (body != null)
            BodyComponentCache[instanceId] = body;
        else
            BodyComponentCache.Remove(instanceId);

        return body;
    }

    static BodyOffsetAccessor ResolveBodyOffsetAccessor(Type bodyType)
    {
        if (bodyType == null)
            return null;

        if (BodyOffsetAccessorCache.TryGetValue(bodyType, out BodyOffsetAccessor cachedAccessor))
            return cachedAccessor;

        cachedAccessor = new BodyOffsetAccessor();

        PropertyInfo followOffsetProperty = bodyType.GetProperty("FollowOffset", AnyInstance);
        if (followOffsetProperty != null
            && followOffsetProperty.PropertyType == typeof(Vector3)
            && followOffsetProperty.CanRead
            && followOffsetProperty.CanWrite)
        {
            cachedAccessor.FollowOffsetProperty = followOffsetProperty;
        }

        FieldInfo legacyFollowOffsetField = bodyType.GetField("m_FollowOffset", AnyInstance);
        if (legacyFollowOffsetField != null && legacyFollowOffsetField.FieldType == typeof(Vector3))
            cachedAccessor.LegacyFollowOffsetField = legacyFollowOffsetField;

        BodyOffsetAccessorCache[bodyType] = cachedAccessor;
        return cachedAccessor;
    }

    static LegacyFreeLookAccessor ResolveLegacyFreeLookAccessor(Type freeLookType)
    {
        if (freeLookType == null)
            return null;

        if (LegacyFreeLookAccessorCache.TryGetValue(freeLookType, out LegacyFreeLookAccessor cachedAccessor))
            return cachedAccessor;

        cachedAccessor = new LegacyFreeLookAccessor
        {
            XAxis = ResolveAxisAccessor(freeLookType, "m_XAxis"),
            YAxis = ResolveAxisAccessor(freeLookType, "m_YAxis"),
            LensField = ResolveLensField(freeLookType),
            OrbitsField = ResolveOrbitsField(freeLookType)
        };

        if (cachedAccessor.OrbitsField != null)
        {
            Type orbitType = cachedAccessor.OrbitsField.FieldType.IsArray
                ? cachedAccessor.OrbitsField.FieldType.GetElementType()
                : null;

            if (orbitType != null)
            {
                FieldInfo radiusField = orbitType.GetField("m_Radius", AnyInstance);
                if (radiusField != null && radiusField.FieldType == typeof(float))
                    cachedAccessor.OrbitRadiusField = radiusField;
            }
        }

        LegacyFreeLookAccessorCache[freeLookType] = cachedAccessor;
        return cachedAccessor;
    }

    static AxisAccessor ResolveAxisAccessor(Type freeLookType, string fieldName)
    {
        FieldInfo axisField = freeLookType.GetField(fieldName, AnyInstance);
        if (axisField == null)
            return null;

        Type axisType = axisField.FieldType;
        PropertyInfo valueProperty = axisType.GetProperty("Value", AnyInstance);
        if (valueProperty == null || valueProperty.PropertyType != typeof(float))
            return null;

        return new AxisAccessor
        {
            AxisField = axisField,
            ValueProperty = valueProperty,
            InputAxisValueField = axisType.GetField("m_InputAxisValue", AnyInstance)
        };
    }

    static FieldInfo ResolveLensField(Type freeLookType)
    {
        FieldInfo lensField = freeLookType.GetField("m_Lens", AnyInstance);
        return lensField != null && lensField.FieldType == typeof(LensSettings) ? lensField : null;
    }

    static FieldInfo ResolveOrbitsField(Type freeLookType)
    {
        FieldInfo orbitsField = freeLookType.GetField("m_Orbits", AnyInstance);
        return orbitsField != null && orbitsField.FieldType.IsArray ? orbitsField : null;
    }

    static bool TryGetLegacyAxisValue(Component freeLook, AxisAccessor accessor, out float value)
    {
        value = 0f;
        if (freeLook == null || accessor == null || accessor.AxisField == null || accessor.ValueProperty == null)
            return false;

        object axisState = accessor.AxisField.GetValue(freeLook);
        if (axisState == null)
            return false;

        value = (float)accessor.ValueProperty.GetValue(axisState);
        return true;
    }

    static bool TrySetLegacyAxisValue(Component freeLook, AxisAccessor accessor, float value, bool clearInput)
    {
        if (freeLook == null || accessor == null || accessor.AxisField == null || accessor.ValueProperty == null || !accessor.ValueProperty.CanWrite)
            return false;

        object axisState = accessor.AxisField.GetValue(freeLook);
        if (axisState == null)
            return false;

        accessor.ValueProperty.SetValue(axisState, value);

        if (clearInput && accessor.InputAxisValueField != null && accessor.InputAxisValueField.FieldType == typeof(float))
        {
            accessor.InputAxisValueField.SetValue(axisState, 0f);
        }

        accessor.AxisField.SetValue(freeLook, axisState);
        return true;
    }
}
