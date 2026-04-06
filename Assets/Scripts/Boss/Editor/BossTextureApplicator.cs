using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChuOn.Editor
{
    public static class BossTextureApplicator
    {
        private const string AutoApplyVersionKey = "ChuOn.BossTextureApplicator.AutoApply.v3";
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";
        private const string BossRootName = "boss";
        private const string BossVisualPath = "VisualRoot/BossVisual";
        private const string OutputMaterialFolder = "Assets/Materials/Boss";

        private const string Boss1TexturePath = "Assets/Texture/Boss/boss1.psd";
        private const string Boss2TexturePath = "Assets/Texture/Boss/boss2.psd";
        private const string Boss3TexturePath = "Assets/Texture/Boss/boss3.psd";
        private const string BossHairTexturePath = "Assets/Texture/Boss/bosshair.psd";

        private const string DumpOutputPath = "Assets/__boss_texture_dump.txt";

        [InitializeOnLoadMethod]
        private static void AutoApplyOnceOnEditorLoad()
        {
            EditorApplication.delayCall += delegate
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                if (EditorPrefs.GetBool(AutoApplyVersionKey, false))
                {
                    return;
                }

                try
                {
                    DumpBossVisualRenderers();
                    ApplyBossTexturesToMainScene();
                    EditorPrefs.SetBool(AutoApplyVersionKey, true);
                    Debug.Log("[BossTextureApplicator] Auto apply completed.");
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        [MenuItem("Tools/Boss/Dump Boss Visual Renderers")]
        public static void DumpBossVisualRenderers()
        {
            Transform visualRoot = LoadBossVisualRoot();
            if (visualRoot == null)
            {
                Debug.LogError("[BossTextureApplicator] BossVisual not found.");
                return;
            }

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true)
                .OrderBy(r => GetHierarchyPath(r.transform))
                .ToArray();

            List<string> lines = new List<string>();
            lines.Add("Boss visual root: " + GetHierarchyPath(visualRoot.transform));
            lines.Add("Renderer count: " + renderers.Length);
            lines.Add(string.Empty);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] mats = renderer.sharedMaterials;
                string materialNames = "(none)";
                if (mats != null && mats.Length > 0)
                {
                    List<string> names = new List<string>();
                    for (int j = 0; j < mats.Length; j++)
                    {
                        string materialName = "null";
                        if (mats[j] != null)
                            materialName = mats[j].name;
                        names.Add(materialName);
                    }

                    materialNames = string.Join(", ", names.ToArray());
                }

                lines.Add(renderer.GetType().Name + ": " + GetHierarchyPath(renderer.transform));
                lines.Add("  materials: " + materialNames);

                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null)
                {
                    lines.Add("  mesh: " + skinned.sharedMesh.name);
                    lines.Add("  subMeshes: " + skinned.sharedMesh.subMeshCount);
                }
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    string meshName = "(none)";
                    if (filter != null && filter.sharedMesh != null)
                    {
                        meshName = filter.sharedMesh.name;
                    }

                    lines.Add("  mesh: " + meshName);
                }

                lines.Add(string.Empty);
            }

            string dumpDirectory = Path.GetDirectoryName(DumpOutputPath);
            if (!string.IsNullOrEmpty(dumpDirectory))
            {
                Directory.CreateDirectory(dumpDirectory);
            }

            File.WriteAllLines(DumpOutputPath, lines.ToArray());
            AssetDatabase.Refresh();
            Debug.Log("[BossTextureApplicator] Dump written to " + DumpOutputPath);
        }

        [MenuItem("Tools/Boss/Apply Boss Textures To MainScene")]
        public static void ApplyBossTexturesToMainScene()
        {
            Transform visualRoot = LoadBossVisualRoot();
            if (visualRoot == null)
            {
                Debug.LogError("[BossTextureApplicator] BossVisual not found.");
                return;
            }

            EnsureFolder(OutputMaterialFolder);

            Texture2D boss1 = LoadTexture(Boss1TexturePath);
            Texture2D boss2 = LoadTexture(Boss2TexturePath);
            Texture2D boss3 = LoadTexture(Boss3TexturePath);
            Texture2D bossHair = LoadTexture(BossHairTexturePath);
            if (boss1 == null || boss2 == null || boss3 == null || bossHair == null)
            {
                Debug.LogError("[BossTextureApplicator] One or more boss textures could not be loaded.");
                return;
            }

            Renderer referenceRenderer = visualRoot.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
            if (referenceRenderer == null)
            {
                Debug.LogError("[BossTextureApplicator] No renderers found under BossVisual.");
                return;
            }

            Material referenceMaterial = referenceRenderer.sharedMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (referenceMaterial != null)
                shader = referenceMaterial.shader;
            if (shader == null)
            {
                Debug.LogError("[BossTextureApplicator] Could not resolve a shader for boss materials.");
                return;
            }

            Material body1 = CreateOrUpdateMaterial("Boss_Body_01", shader, referenceMaterial, boss1);
            Material body2 = CreateOrUpdateMaterial("Boss_Body_02", shader, referenceMaterial, boss2);
            Material body3 = CreateOrUpdateMaterial("Boss_Body_03", shader, referenceMaterial, boss3);
            Material hair = CreateOrUpdateMaterial("Boss_Hair", shader, referenceMaterial, bossHair);
            Material sword = CreateOrUpdateMaterial("Boss_Sword", shader, referenceMaterial, null);
            ConfigureSwordMaterial(sword);

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true)
                .OrderBy(r => GetHierarchyPath(r.transform))
                .ToArray();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    continue;
                }

                Material assignedMaterial = ResolveAssignedMaterial(renderer, body1, body2, body3, hair, sword);
                if (assignedMaterial == null)
                {
                    continue;
                }

                for (int j = 0; j < sharedMaterials.Length; j++)
                {
                    sharedMaterials[j] = assignedMaterial;
                }

                renderer.sharedMaterials = sharedMaterials;
                EditorUtility.SetDirty(renderer);

                Debug.Log("[BossTextureApplicator] Applied " + assignedMaterial.name + " to " + GetHierarchyPath(renderer.transform));
            }

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BossTextureApplicator] Boss textures applied to MainScene.");
        }

        private static Transform LoadBossVisualRoot()
        {
            var scene = EditorSceneManager.GetSceneByPath(MainScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            }

            GameObject bossRoot = GameObject.Find(BossRootName);
            if (bossRoot == null)
            {
                return null;
            }

            return bossRoot.transform.Find(BossVisualPath);
        }

        private static Texture2D LoadTexture(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material CreateOrUpdateMaterial(string assetName, Shader shader, Material referenceMaterial, Texture2D texture)
        {
            string materialPath = OutputMaterialFolder + "/" + assetName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            if (referenceMaterial != null)
            {
                EditorUtility.CopySerialized(referenceMaterial, material);
                material.shader = shader;
            }

            material.name = assetName;
            SetMainTexture(material, texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetMainTexture(Material material, Texture texture)
        {
            if (material == null)
            {
                return;
            }

            string[] candidates =
            {
                "_BaseMap",
                "_BaseColorMap",
                "_MainTex",
                "_BaseColorTexture"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string propertyName = candidates[i];
                if (material.HasProperty(propertyName))
                {
                    material.SetTexture(propertyName, texture);
                }
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
        }

        private static bool IsHairRenderer(Renderer renderer)
        {
            string path = GetHierarchyPath(renderer.transform).ToLowerInvariant();
            if (path.Contains("hair") || path.Contains("plane009") || path.Contains("plane033"))
            {
                return true;
            }

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material != null && material.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static Material ResolveAssignedMaterial(Renderer renderer, Material body1, Material body2, Material body3, Material hair, Material sword)
        {
            string path = GetHierarchyPath(renderer.transform).ToLowerInvariant();
            string name = renderer.name.ToLowerInvariant();

            if (IsHairRenderer(renderer))
            {
                return hair;
            }

            if (name.Contains("body") || path.EndsWith("/body"))
            {
                return body3;
            }

            if (name.Contains("object012"))
            {
                return body1;
            }

            if (name.Contains("object002"))
            {
                return sword;
            }

            return body1;
        }

        private static void ConfigureSwordMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", new Color(0.18f, 0.19f, 0.22f, 1f));
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", new Color(0.18f, 0.19f, 0.22f, 1f));
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.55f);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.72f);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.72f);
            }
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            Stack<string> stack = new Stack<string>();
            Transform current = target;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack.ToArray());
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
