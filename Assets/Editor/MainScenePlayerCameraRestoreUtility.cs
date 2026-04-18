using System.Collections.Generic;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MainScenePlayerCameraRestoreUtility
{
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string PlayerPrefabPath = "Assets/Prefabs/Generated/PlayerRoot.prefab";

    [MenuItem("Tools/ProjectChuOn/Restore MainScene Player Cameras")]
    static void RestoreFromMenu()
    {
        string details = RunInternal();
        EditorUtility.DisplayDialog("MainScene Camera Restore", details, "OK");
    }

    public static string RunFromFastMcp()
    {
        return RunInternal();
    }

    static string RunInternal()
    {
        var report = new StringBuilder();
        Scene scene = ResolveMainScene(report);
        if (!scene.IsValid() || !scene.isLoaded)
            return "MainScene could not be opened.";

        bool changed = RestoreScene(scene, report);
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        return report.ToString().Trim();
    }

    static Scene ResolveMainScene(StringBuilder report)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isLoaded && activeScene.name == "MainScene")
        {
            report.AppendLine("Using currently opened MainScene.");
            return activeScene;
        }

        report.AppendLine("Opening MainScene from asset path.");
        return EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
    }

    static bool RestoreScene(Scene scene, StringBuilder report)
    {
        bool changed = false;

        PlayerReferences playerReferences = FindInScene<PlayerReferences>(scene);
        if (playerReferences == null)
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                report.AppendLine("PlayerRoot prefab not found.");
                return false;
            }

            GameObject playerInstance = PrefabUtility.InstantiatePrefab(playerPrefab, scene) as GameObject;
            if (playerInstance == null)
            {
                report.AppendLine("PlayerRoot prefab instantiation failed.");
                return false;
            }

            playerReferences = playerInstance.GetComponent<PlayerReferences>();
            report.AppendLine("Instantiated PlayerRoot prefab into MainScene.");
            changed = true;
        }

        playerReferences.SyncSerializedReferences();

        Transform playerRoot = playerReferences.PlayerRoot != null ? playerReferences.PlayerRoot : playerReferences.transform;
        Transform lockPivot = playerReferences.LockPivot != null ? playerReferences.LockPivot : playerRoot.Find("LockPivot");
        PlayerLockOn playerLockOn = playerRoot.GetComponent<PlayerLockOn>();
        LockOnCameraManager cameraManager = FindInScene<LockOnCameraManager>(scene);
        FreeLookCamera freeLookDriver = FindInScene<FreeLookCamera>(scene);
        DynamicCamLookPivot camLookPivot = FindInScene<DynamicCamLookPivot>(scene);
        CinemachineVirtualCameraBase freeLookCam = FindVirtualCameraByName(scene, "FreeLook Camera");
        CinemachineVirtualCameraBase lockOnCam = FindVirtualCameraByName(scene, "CM_LockOnCam");
        CinemachineTargetGroup targetGroup = FindInScene<CinemachineTargetGroup>(scene);
        GameObject ultimateRoot = FindRoot(scene, "UltimateCinematicRoot");

        if (playerRoot.gameObject.tag != "Player")
        {
            playerRoot.gameObject.tag = "Player";
            changed = true;
        }

        if (camLookPivot != null && camLookPivot.target != playerRoot)
        {
            camLookPivot.target = playerRoot;
            EditorUtility.SetDirty(camLookPivot);
            report.AppendLine("Restored CamLookPivot target -> PlayerRoot.");
            changed = true;
        }

        if (freeLookDriver != null)
        {
            if (freeLookDriver.player != playerRoot)
            {
                freeLookDriver.player = playerRoot;
                changed = true;
            }

            if (freeLookDriver.playerLockOn != playerLockOn)
            {
                freeLookDriver.playerLockOn = playerLockOn;
                changed = true;
            }

            EditorUtility.SetDirty(freeLookDriver);
        }

        if (freeLookCam != null)
        {
            if (freeLookCam.Follow != playerRoot)
            {
                freeLookCam.Follow = playerRoot;
                changed = true;
            }

            Transform lookTarget = camLookPivot != null ? camLookPivot.transform : lockPivot != null ? lockPivot : playerRoot;
            if (freeLookCam.LookAt != lookTarget)
            {
                freeLookCam.LookAt = lookTarget;
                changed = true;
            }

            EditorUtility.SetDirty(freeLookCam);
            report.AppendLine("Restored FreeLook Camera Follow/LookAt.");
        }

        if (cameraManager != null)
        {
            if (cameraManager.playerPivot != lockPivot)
            {
                cameraManager.playerPivot = lockPivot;
                changed = true;
            }

            if (cameraManager.freeLookCam != freeLookCam)
            {
                cameraManager.freeLookCam = freeLookCam;
                changed = true;
            }

            if (cameraManager.freeLookDriver != freeLookDriver)
            {
                cameraManager.freeLookDriver = freeLookDriver;
                changed = true;
            }

            if (cameraManager.lockOnCam != lockOnCam)
            {
                cameraManager.lockOnCam = lockOnCam;
                changed = true;
            }

            if (cameraManager.targetGroup != targetGroup)
            {
                cameraManager.targetGroup = targetGroup;
                changed = true;
            }

            EditorUtility.SetDirty(cameraManager);
            report.AppendLine("Restored CameraManager references.");
        }

        if (lockOnCam != null)
        {
            Transform lockOnFollow = lockPivot != null ? lockPivot : playerRoot;
            if (lockOnCam.Follow != lockOnFollow)
            {
                lockOnCam.Follow = lockOnFollow;
                changed = true;
            }

            Transform lockOnLook = targetGroup != null ? targetGroup.transform : lockOnFollow;
            if (lockOnCam.LookAt != lockOnLook)
            {
                lockOnCam.LookAt = lockOnLook;
                changed = true;
            }

            EditorUtility.SetDirty(lockOnCam);
            report.AppendLine("Restored CM_LockOnCam Follow/LookAt.");
        }

        if (targetGroup != null && lockPivot != null)
        {
            var targets = new List<CinemachineTargetGroup.Target>();
            targets.Add(new CinemachineTargetGroup.Target
            {
                Object = lockPivot,
                Weight = 1.2f,
                Radius = 2f
            });

            BossController bossController = FindInScene<BossController>(scene);
            if (bossController != null)
            {
                targets.Add(new CinemachineTargetGroup.Target
                {
                    Object = bossController.transform,
                    Weight = 1f,
                    Radius = 2f
                });
            }

            targetGroup.Targets = targets;
            EditorUtility.SetDirty(targetGroup);
            changed = true;
            report.AppendLine("Rebuilt CM_TargetGroup with player/boss.");
        }

        if (ultimateRoot != null && ultimateRoot.activeSelf)
        {
            ultimateRoot.SetActive(false);
            EditorUtility.SetDirty(ultimateRoot);
            report.AppendLine("Disabled UltimateCinematicRoot to avoid camera conflicts.");
            changed = true;
        }

        return changed;
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null)
                return found;
        }

        return null;
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root;
        }

        return null;
    }

    static CinemachineVirtualCameraBase FindVirtualCameraByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (CinemachineVirtualCameraBase camera in root.GetComponentsInChildren<CinemachineVirtualCameraBase>(true))
            {
                if (camera != null && camera.gameObject.name == name)
                    return camera;
            }
        }

        return null;
    }
}
