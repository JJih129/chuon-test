using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerAttackSlashVfxSetup
{
    private const string PlayerRootPrefabPath = "Assets/Prefabs/Generated/PlayerRoot.prefab";

    [MenuItem("Tools/ChuOn/VFX/Apply Player Slash Attack VFX")]
    public static void Apply()
    {
        int updated = 0;
        PlayerAttackVfxPresenter[] presenters = Resources.FindObjectsOfTypeAll<PlayerAttackVfxPresenter>();
        for (int i = 0; i < presenters.Length; i++)
        {
            PlayerAttackVfxPresenter presenter = presenters[i];
            if (presenter == null)
                continue;

            GameObject owner = presenter.gameObject;
            if (EditorUtility.IsPersistent(owner) || !owner.scene.IsValid())
                continue;

            Undo.RecordObject(presenter, "Apply Player Slash Attack VFX");
            presenter.RefreshDefaultSlashVfxMappings();
            EditorUtility.SetDirty(presenter);
            updated++;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRootPrefabPath);
        if (prefab != null)
        {
            PlayerAttackVfxPresenter presenter = prefab.GetComponent<PlayerAttackVfxPresenter>();
            if (presenter != null)
            {
                presenter.RefreshDefaultSlashVfxMappings();
                EditorUtility.SetDirty(presenter);
                updated++;
            }
        }

        AssetDatabase.SaveAssets();

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        Debug.Log($"[PlayerAttackSlashVfxSetup] Applied slash VFX mappings to {updated} PlayerAttackVfxPresenter component(s).");
    }
}
