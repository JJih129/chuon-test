using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    public float scrollSpeedY = 1.0f;
    public string textureProperty = "_MainTex"; // HDRP/URP라면 "_BaseMap"

    private Material material;
    private float offsetY = 0f;

    void Start()
    {
        // 머티리얼 인스턴스 생성 (원본 영향을 안 주게)
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            material = renderer.material;
        }
    }

    void Update()
    {
        if (material == null) return;

        offsetY += scrollSpeedY * Time.deltaTime;
        material.SetTextureOffset(textureProperty, new Vector2(0, offsetY));
    }
}