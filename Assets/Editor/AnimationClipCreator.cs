using UnityEngine;
using UnityEditor;

public class AnimationClipCreator
{
    [MenuItem("Tools/Create Missing Animation Clips")]
    static void CreateMissingClips()
    {
        string[] clipNames = new string[]
        {
            "Idle_Ultimate",
            "Ultimate_Prepare",
            "Ultimate_Action",
            "Ultimate_Finisher",
            "Ultimate_End",
            "Break_Start",
            "Break_Loop",
            "Break_End"
        };

        string outputPath = "Assets/Anime/Clips/Generated";
        
        if (!AssetDatabase.IsValidFolder(outputPath))
        {
            string[] folders = outputPath.Split('/');
            string currentPath = folders[0];
            
            for (int i = 1; i < folders.Length; i++)
            {
                string nextPath = currentPath + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = nextPath;
            }
        }

        foreach (string clipName in clipNames)
        {
            string clipPath = $"{outputPath}/{clipName}.anim";
            
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) == null)
            {
                AnimationClip clip = new AnimationClip();
                clip.name = clipName;
                
                // 1초 길이의 빈 클립 생성
                AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 0f);
                clip.SetCurve("", typeof(Transform), "localPosition.x", curve);
                
                AssetDatabase.CreateAsset(clip, clipPath);
                Debug.Log($"Created: {clipPath}");
            }
            else
            {
                Debug.Log($"Already exists: {clipPath}");
            }
        }
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Animation clip creation completed!");
    }
}