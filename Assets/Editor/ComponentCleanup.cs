using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

public class ComponentCleanup
{
    [MenuItem("Tools/Cleanup Player Components")]
    static void CleanupPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogError("Player object not found!");
            return;
        }

        // MoveController 제거
        Component moveController = player.GetComponent("MoveController");
        if (moveController != null)
        {
            Object.DestroyImmediate(moveController);
            Debug.Log("Removed: MoveController");
        }

        // PlayerComboAttack 제거
        Component comboAttack = player.GetComponent("PlayerComboAttack");
        if (comboAttack != null)
        {
            Object.DestroyImmediate(comboAttack);
            Debug.Log("Removed: PlayerComboAttack");
        }

        // DevPlayerHitHotkeys 제거
        Component devHit = player.GetComponent("DevPlayerHitHotkeys");
        if (devHit != null)
        {
            Object.DestroyImmediate(devHit);
            Debug.Log("Removed: DevPlayerHitHotkeys");
        }

        // DevUltimateHotkeys 제거
        Component devUlt = player.GetComponent("DevUltimateHotkeys");
        if (devUlt != null)
        {
            Object.DestroyImmediate(devUlt);
            Debug.Log("Removed: DevUltimateHotkeys");
        }

        // SimpleLockOnController 중복 제거 (2개 → 1개)
        Component[] lockOns = player.GetComponents(typeof(Component));
        int lockOnCount = 0;
        foreach (var comp in lockOns)
        {
            if (comp.GetType().Name == "SimpleLockOnController")
            {
                lockOnCount++;
                if (lockOnCount > 1)
                {
                    Object.DestroyImmediate(comp);
                    Debug.Log("Removed duplicate: SimpleLockOnController");
                }
            }
        }

        // 씬 저장
        EditorUtility.SetDirty(player);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Player component cleanup completed!");
    }
}