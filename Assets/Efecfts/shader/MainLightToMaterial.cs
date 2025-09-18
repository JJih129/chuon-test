// MainLightToMaterial.cs
using UnityEngine;

[ExecuteAlways]
public class MainLightToMaterial : MonoBehaviour
{
    public Light mainDirectional;
    public Material targetMaterial;
    public string prop = "_LightDir"; // 셰이더 속성명

    void Update()
    {
        if (mainDirectional != null && targetMaterial != null)
            targetMaterial.SetVector(prop, -mainDirectional.transform.forward);
    }
}
