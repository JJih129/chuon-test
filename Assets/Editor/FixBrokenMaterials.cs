using UnityEngine;
using UnityEditor;

public class FixBrokenMaterials : EditorWindow
{
    [MenuItem("Tools/🧯 깨진 머티리얼 복구")]
    public static void FixMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        int fixedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat != null && mat.shader.name == "Standard")
            {
                // Standard Shader에서 깨져보이면 Unlit으로 임시 복구
                mat.shader = Shader.Find("Unlit/Texture");
                fixedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"🧯 복구 완료: {fixedCount}개 머티리얼을 'Unlit/Texture'로 전환함");
    }
}
