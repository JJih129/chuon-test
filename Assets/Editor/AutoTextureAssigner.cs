using UnityEngine;
using UnityEditor;
using System.IO;

public class AutoTextureAssigner : EditorWindow
{
    [MenuItem("Tools/?? 텍스처 자동 연결기")]
    public static void ShowWindow()
    {
        GetWindow<AutoTextureAssigner>("?? 텍스처 자동 연결기");
    }

    private void OnGUI()
    {
        GUILayout.Label("머티리얼 이름과 텍스처 이름이 일치해야 작동합니다", EditorStyles.wordWrappedLabel);
        if (GUILayout.Button("모든 머티리얼에 텍스처 자동 연결"))
        {
            AssignTextures();
        }
    }

    static void AssignTextures()
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        int assignCount = 0;

        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            // 텍스처 찾기: 머티리얼 이름과 동일한 텍스처
            string matName = mat.name.ToLower();
            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D " + matName);

            foreach (string texGuid in textureGuids)
            {
                string texPath = AssetDatabase.GUIDToAssetPath(texGuid);
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

                if (tex != null)
                {
                    Undo.RecordObject(mat, "Assign Albedo Texture");
                    mat.shader = Shader.Find("Standard"); // 강제로 Standard로 설정
                    mat.SetTexture("_MainTex", tex); // Albedo에 연결
                    EditorUtility.SetDirty(mat);
                    assignCount++;
                    Debug.Log($"? 머티리얼 '{mat.name}' 에 텍스처 '{tex.name}' 연결됨");
                    break;
                }
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"?? 텍스처 자동 연결 완료: 총 {assignCount}개 머티리얼 수정됨");
    }
}
