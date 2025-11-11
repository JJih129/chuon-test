using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class AnimatorParameterAdder
{
    [MenuItem("Tools/Add Missing Animator Parameters")]
    static void AddMissingParameters()
    {
        string controllerPath = "Assets/Anime/Animator/PlayerAnimatorController.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        
        if (controller == null)
        {
            Debug.LogError($"Controller not found at: {controllerPath}");
            return;
        }

        // 기존 파라미터 확인
        bool hasIsAttacking = false;
        bool hasBreakStagger = false;
        bool hasIsUltimate = false;

        foreach (var param in controller.parameters)
        {
            if (param.name == "IsAttacking") hasIsAttacking = true;
            if (param.name == "BreakStagger") hasBreakStagger = true;
            if (param.name == "IsUltimate") hasIsUltimate = true;
        }

        // 없는 파라미터 추가
        if (!hasIsAttacking)
        {
            controller.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);
            Debug.Log("Added parameter: IsAttacking (Bool)");
        }

        if (!hasBreakStagger)
        {
            controller.AddParameter("BreakStagger", AnimatorControllerParameterType.Trigger);
            Debug.Log("Added parameter: BreakStagger (Trigger)");
        }

        if (!hasIsUltimate)
        {
            controller.AddParameter("IsUltimate", AnimatorControllerParameterType.Bool);
            Debug.Log("Added parameter: IsUltimate (Bool)");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("Animator parameter update completed!");
    }
}