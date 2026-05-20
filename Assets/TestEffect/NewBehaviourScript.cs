using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    public float scrollSpeedY = 1.0f;
    public string textureProperty = "_MainTex";

    readonly List<Material> materials = new List<Material>();
    float offsetY;

    void Start()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material material = renderer.material;
            if (material != null)
                materials.Add(material);
        }
    }

    void Update()
    {
        if (materials.Count == 0)
            return;

        offsetY += scrollSpeedY * Time.deltaTime;
        Vector2 offset = new Vector2(0f, offsetY);

        for (int i = 0; i < materials.Count; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            if (material.HasProperty(textureProperty))
            {
                material.SetTextureOffset(textureProperty, offset);
                continue;
            }

            if (material.HasProperty("_BaseMap"))
                material.SetTextureOffset("_BaseMap", offset);
        }
    }

    void OnDestroy()
    {
        for (int i = 0; i < materials.Count; i++)
        {
            if (materials[i] != null)
                Destroy(materials[i]);
        }

        materials.Clear();
    }
}
