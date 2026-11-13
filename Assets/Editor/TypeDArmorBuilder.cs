#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Type-D 장갑 아트 파이프라인 (메뉴로만 실행, 로드 시 자동 실행 없음 — EditorAutoFixupGate 규칙)
//  0) Tools/ChuOn/Type-D/0. Regenerate All : 아래 1→2→3 을 한 번에 (Blender 경로: EditorPrefs "ChuOn.BlenderExe" 또는 env BLENDER_EXE)
//  1) Tools/ChuOn/Type-D/1. Export Reference : 편집 모드 T-포즈 보스(Body/Object012/Plane009)를 Blender 참조용 OBJ 로 내보냄
//  2) Blender: Tools/ArtGen/typed_armor_blender.py -> typed_armor.json + 아틀라스 (albedo/normal/osm png, 결정적 생성)
//  3) Tools/ChuOn/Type-D/2. Build From Json : 메시/아틀라스 텍스처/재질/TypeD_ArmorSet(Resources) 생성, 뼈 기준 로컬 포즈 굽기
//  + Tools/ChuOn/VFX/Build Iteration 10 Assets : 검기·텔레그래프 재질(Resources/VFX), 텔레그래프 큐 효과음(Resources/Audio)
// 반복 11 (품질 패스): 그라파이트 금속 + 세라믹(명도 ~0.55) 악센트, ASP StylizePBR + 노멀/OSM 아틀라스, 가는 츠카사 레드 발광선.
public static class TypeDArmorBuilder
{
    const string Root = "Assets/Art/ChuonPolish/TypeD";
    const string SetPath = Root + "/Resources/TypeD/TypeD_ArmorSet.asset";
    const string VfxResources = "Assets/Art/ChuonPolish/VFX/Resources/VFX";
    const string AudioResources = "Assets/Art/ChuonPolish/Audio/Resources/Audio";
    static readonly string DefaultWorkDir = Path.Combine(Path.GetFullPath("Temp"), "TypeDArmor");
    const string BlenderPrefKey = "ChuOn.BlenderExe";
    const string DefaultBlender = "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe";
    const string GeneratorScript = "Tools/ArtGen/typed_armor_blender.py";
    // 츠카사 레드 (UI 와 같은 색) — 장갑 발광선 평상시 색, HDR ~1.5 (텔레그래프 램프 시 큐 색으로 덮인다)
    static readonly Color TsukasaRed = new Color(0.878f, 0.188f, 0.227f);
    const float AccentHdr = 1.5f;
    static Color AccentColor() => new Color(TsukasaRed.r * AccentHdr, TsukasaRed.g * AccentHdr, TsukasaRed.b * AccentHdr, 1f);

    // ───────────────────────── JSON 형식 (typed_armor_blender.py export) ─────────────────────────
    [Serializable] class JSub { public string material; public int[] indices; }
    [Serializable] class JPart { public string name; public string bone; public string role; public float[] positions; public float[] normals; public float[] uvs; public JSub[] submeshes; public float[] anchor; }
    [Serializable] class JRoot { public JPart[] parts; }

    // ───────────────────────── 0. 전체 재생성 ─────────────────────────
    [MenuItem("Tools/ChuOn/Type-D/0. Regenerate All (export + Blender + build)")]
    public static void RegenerateAllMenu() => Debug.Log(RegenerateAll(DefaultWorkDir));

    public static string RegenerateAll(string dir)
    {
        if (Application.isPlaying)
            return "stop play mode first";
        string export = ExportReference(dir);
        if (!export.StartsWith("exported"))
            return export;
        string blender = RunBlender(dir);
        if (blender != null)
            return blender;
        return Build(Path.Combine(dir, "typed_armor.json"));
    }

    static string RunBlender(string dir)
    {
        string exe = EditorPrefs.GetString(BlenderPrefKey, Environment.GetEnvironmentVariable("BLENDER_EXE") ?? DefaultBlender);
        if (!File.Exists(exe))
            return "Blender not found: " + exe + " (set EditorPrefs '" + BlenderPrefKey + "' or env BLENDER_EXE)";
        string script = Path.GetFullPath(GeneratorScript);
        string json = Path.Combine(dir, "typed_armor.json");
        string args = "-b --factory-startup --python \"" + script + "\" -- \"" + dir + "\" \"" + json + "\" \"" + Path.Combine(dir, "preview") + "\"";
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var proc = System.Diagnostics.Process.Start(psi))
        {
            var err = proc.StandardError.ReadToEndAsync();
            string stdout = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(15 * 60 * 1000))
                return "Blender timed out";
            File.WriteAllText(Path.Combine(dir, "blender_log.txt"), stdout + "\n" + err.Result);
            if (proc.ExitCode != 0 || !stdout.Contains("DONE"))
                return "Blender failed (see " + Path.Combine(dir, "blender_log.txt") + ")";
        }
        return null;
    }

    // ───────────────────────── 1. 참조 내보내기 ─────────────────────────
    [MenuItem("Tools/ChuOn/Type-D/1. Export Reference (edit-mode T-pose)")]
    public static void ExportReferenceMenu() => Debug.Log(ExportReference(DefaultWorkDir));

    public static string ExportReference(string dir)
    {
        BossController boss = UnityEngine.Object.FindObjectOfType<BossController>(true);
        if (boss == null)
            return "no boss";
        Directory.CreateDirectory(dir);
        Transform root = boss.transform;
        Quaternion inv = Quaternion.Inverse(root.rotation);
        Vector3 rp = root.position;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        Func<Vector3, string> fmt = u => { Vector3 p = inv * (u - rp); return p.x.ToString("F5", ci) + " " + p.z.ToString("F5", ci) + " " + p.y.ToString("F5", ci); };
        var report = new StringBuilder();
        foreach (string name in new[] { "Body", "Object012", "Plane009" })
        {
            SkinnedMeshRenderer smr = FindSkinned(root, name);
            if (smr == null)
                continue;
            var m = new Mesh();
            smr.BakeMesh(m, true);
            Vector3[] v = m.vertices;
            BoneWeight[] bw = smr.sharedMesh.boneWeights;
            Transform[] bones = smr.bones;
            var obj = new StringBuilder();
            var vb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                obj.Append("v ").AppendLine(fmt(smr.transform.TransformPoint(v[i])));
                vb.AppendLine(bones[bw[i].boneIndex0].name);
            }
            int[] t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
                obj.Append("f ").Append(t[i] + 1).Append(' ').Append(t[i + 2] + 1).Append(' ').Append(t[i + 1] + 1).AppendLine();
            File.WriteAllText(Path.Combine(dir, name + ".obj"), obj.ToString());
            File.WriteAllText(Path.Combine(dir, name + "_bones.txt"), vb.ToString());
            UnityEngine.Object.DestroyImmediate(m);
            report.Append(name).Append(' ').Append(v.Length).Append("; ");
        }

        Animator anim = FindHumanoid(root);
        var bonesTxt = new StringBuilder();
        foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (hb == HumanBodyBones.LastBone)
                continue;
            Transform tr = anim != null ? anim.GetBoneTransform(hb) : null;
            if (tr != null)
                bonesTxt.Append(hb).Append(' ').Append(tr.name).Append(' ').AppendLine(fmt(tr.position));
        }
        File.WriteAllText(Path.Combine(dir, "bones.txt"), bonesTxt.ToString());
        return "exported " + report + "-> " + dir;
    }

    // ───────────────────────── 3. 빌드 ─────────────────────────
    [MenuItem("Tools/ChuOn/Type-D/2. Build From Json")]
    public static void BuildMenu() => Debug.Log(Build(Path.Combine(DefaultWorkDir, "typed_armor.json")));

    public static string Build(string jsonPath)
    {
        BossController boss = UnityEngine.Object.FindObjectOfType<BossController>(true);
        if (boss == null)
            return "no boss in open scene";
        if (Application.isPlaying)
            return "stop play mode first (bone poses are baked from the edit-mode T-pose)";
        Animator anim = FindHumanoid(boss.transform);
        if (anim == null)
            return "no humanoid";

        EnsureFolder(Root + "/Meshes");
        EnsureFolder(Root + "/Textures");
        EnsureFolder(Root + "/Materials");
        EnsureFolder(Root + "/Resources/TypeD");

        var report = new StringBuilder();
        Dictionary<string, Material> mats = BuildMaterials(boss.transform, Path.GetDirectoryName(Path.GetFullPath(jsonPath)), report);

        JRoot data = JsonUtility.FromJson<JRoot>(File.ReadAllText(jsonPath));
        Transform root = boss.transform;
        var parts = new List<TypeDArmorSet.Part>();
        foreach (JPart jp in data.parts)
        {
            if (!Enum.TryParse(jp.bone, out HumanBodyBones hb))
                continue;
            Transform bone = anim.GetBoneTransform(hb);
            if (bone == null)
                continue;

            // 기존 에셋은 제자리에서 다시 채운다 (CopySerialized 는 GPU 버퍼를 갱신하지 않아 에디터 세션에서 옛 메시가 그려졌다)
            string meshPath = Root + "/Meshes/TypeD_" + jp.name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool created = mesh == null;
            if (created)
                mesh = new Mesh();
            List<string> subMats = FillMesh(mesh, jp);
            if (created)
                AssetDatabase.CreateAsset(mesh, meshPath);
            else
                EditorUtility.SetDirty(mesh);

            var materials = new Material[subMats.Count];
            int glow = -1;
            for (int i = 0; i < subMats.Count; i++)
            {
                string key = jp.role == "Visor" ? "VisorGlow" : subMats[i];
                materials[i] = mats.TryGetValue(key, out Material m) ? m : mats["Metal"];
                if (subMats[i] == "Glow")
                    glow = i;
            }

            // 뼈 기준 로컬 포즈: 메시는 '보스 루트 기준(회전만, 미터)' 좌표 → 같은 월드 포즈가 되도록 뼈 로컬로 변환
            float s = bone.lossyScale.x;
            var part = new TypeDArmorSet.Part
            {
                name = jp.name,
                mesh = mesh,
                materials = materials,
                bone = hb,
                localPosition = bone.InverseTransformPoint(root.position),
                localRotation = Quaternion.Inverse(bone.rotation) * root.rotation,
                localScale = Vector3.one / Mathf.Max(0.0001f, s),
                role = jp.role == "Visor" ? TypeDArmorSet.PartRole.Visor : jp.role == "Core" ? TypeDArmorSet.PartRole.Core : TypeDArmorSet.PartRole.Plate,
                glowSubmesh = jp.role == "Plate" ? glow : -1,
                anchorLocal = jp.anchor != null && jp.anchor.Length == 3 ? new Vector3(jp.anchor[0], jp.anchor[1], jp.anchor[2]) : Vector3.zero
            };
            // 어깨 장갑: 쇄골과 50% 블렌드 (팔 회전의 절반만 따라감)
            HumanBodyBones blend = hb == HumanBodyBones.LeftUpperArm ? HumanBodyBones.LeftShoulder : hb == HumanBodyBones.RightUpperArm ? HumanBodyBones.RightShoulder : HumanBodyBones.LastBone;
            Transform blendBone = blend != HumanBodyBones.LastBone ? anim.GetBoneTransform(blend) : null;
            if (blendBone != null)
            {
                part.useBlendBone = true;
                part.blendBone = blend;
                part.blendLocalPosition = blendBone.InverseTransformPoint(root.position);
                part.blendLocalRotation = Quaternion.Inverse(blendBone.rotation) * root.rotation;
                part.blendWeight = 0.5f;
                part.pivotLocal = Quaternion.Inverse(root.rotation) * (bone.position - root.position);
            }
            parts.Add(part);
            report.Append(jp.name).Append('@').Append(hb).Append(' ');
        }

        TypeDArmorSet set = AssetDatabase.LoadAssetAtPath<TypeDArmorSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<TypeDArmorSet>();
            AssetDatabase.CreateAsset(set, SetPath);
        }
        set.parts = parts.ToArray();
        SkinnedMeshRenderer check = FindSkinned(root, "Plane009");
        set.requiredRendererName = "Plane009";
        set.requiredVertexCount = check != null && check.sharedMesh != null ? check.sharedMesh.vertexCount : 0;
        set.materialOverrides = new[]
        {
            Override("Body", mats, "Suit_Body"),
            Override("Object012", mats, "Suit_Legs"),
            Override("Plane009", mats, "Suit_Outer"),
        };
        set.glowColor = AccentColor();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        return "built " + parts.Count + " parts: " + report;
    }

    static TypeDArmorSet.MaterialOverride Override(string renderer, Dictionary<string, Material> mats, string key)
    {
        return new TypeDArmorSet.MaterialOverride { rendererName = renderer, materials = mats.TryGetValue(key, out Material m) ? new[] { m } : Array.Empty<Material>() };
    }

    static List<string> FillMesh(Mesh mesh, JPart jp)
    {
        int n = jp.positions.Length / 3;
        var v = new Vector3[n];
        var nr = new Vector3[n];
        var uv = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            v[i] = new Vector3(jp.positions[i * 3], jp.positions[i * 3 + 1], jp.positions[i * 3 + 2]);
            nr[i] = new Vector3(jp.normals[i * 3], jp.normals[i * 3 + 1], jp.normals[i * 3 + 2]);
            uv[i] = new Vector2(jp.uvs[i * 2], jp.uvs[i * 2 + 1]);
        }

        // 서브메시 순서: 발광/코어/바이저 먼저, 장갑 본체(Metal/Ceramic) 마지막 → 균열 오버레이(추가 재질)가 본체에 그려진다
        var order = new List<JSub>(jp.submeshes);
        order.Sort((a, b) => Rank(a.material).CompareTo(Rank(b.material)));
        var subMats = new List<string>();
        mesh.Clear();
        mesh.name = "TypeD_" + jp.name;
        mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = v;
        mesh.normals = nr;
        mesh.uv = uv;
        mesh.subMeshCount = order.Count;
        for (int i = 0; i < order.Count; i++)
        {
            mesh.SetTriangles(order[i].indices, i);
            subMats.Add(order[i].material);
        }
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return subMats;
    }

    static int Rank(string m)
    {
        switch (m)
        {
            case "Glow": return 0;
            case "Core": return 1;
            case "Visor": return 2;
            case "Metal": return 3;
            default: return 4; // Ceramic
        }
    }

    // ───────────────────────── 재질/텍스처 ─────────────────────────
    // Blender 가 굽는 공용 아틀라스 (Metal/Ceramic 서브메시가 같은 UV 아틀라스를 공유)
    struct ArmorAtlas
    {
        public Texture2D albedo, normal, osm;
    }

    static ArmorAtlas ImportAtlas(string workDir, StringBuilder report)
    {
        return new ArmorAtlas
        {
            albedo = ImportAtlasTexture(workDir, "typed_armor_albedo.png", "T_TypeD_Armor_Albedo", TextureImporterType.Default, true, report),
            normal = ImportAtlasTexture(workDir, "typed_armor_normal.png", "T_TypeD_Armor_Normal", TextureImporterType.NormalMap, false, report),
            osm = ImportAtlasTexture(workDir, "typed_armor_osm.png", "T_TypeD_Armor_OSM", TextureImporterType.Default, false, report),
        };
    }

    static Texture2D ImportAtlasTexture(string workDir, string file, string assetName, TextureImporterType type, bool srgb, StringBuilder report)
    {
        string src = Path.Combine(workDir, file);
        string path = Root + "/Textures/" + assetName + ".png";
        if (File.Exists(src))
        {
            File.Copy(src, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        else
        {
            report.Append("[missing ").Append(file).Append(", keeping existing] ");
        }
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null)
            return null;
        imp.textureType = type;
        imp.sRGBTexture = srgb;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.anisoLevel = 4;
        imp.maxTextureSize = 2048;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Dictionary<string, Material> BuildMaterials(Transform boss, string workDir, StringBuilder report)
    {
        var d = new Dictionary<string, Material>();
        ArmorAtlas atlas = ImportAtlas(workDir, report);

        Material bodySrc = FindSkinned(boss, "Body")?.sharedMaterial;
        Material legsSrc = FindSkinned(boss, "Object012")?.sharedMaterial;
        Material outerSrc = FindSkinned(boss, "Plane009")?.sharedMaterial;
        // 이미 재질이 교체돼 있으면(재빌드) 원본은 ASP 데모 재질
        bodySrc = Original(bodySrc, "Assets/ThirdParty/ASP/Demo/body 5.mat");
        legsSrc = Original(legsSrc, "Assets/ThirdParty/ASP/Demo/body 4.mat");
        outerSrc = Original(outerSrc, "Assets/ThirdParty/ASP/Demo/body 3.mat");

        d["Suit_Body"] = SuitMaterial("M_TypeD_Suit_Body", bodySrc, "boss2", report);
        d["Suit_Legs"] = SuitMaterial("M_TypeD_Suit_Legs", legsSrc, "boss3", report);
        d["Suit_Outer"] = SuitMaterial("M_TypeD_Suit_Outer", outerSrc, "boss1", report);

        // 아틀라스가 색/거칠기/금속도를 모두 담는다 (BaseColor 흰색). 재질 차이는 PBR 영향도/림만.
        d["Metal"] = PbrArmorMaterial("M_TypeD_ArmorMetal", bodySrc, atlas, 0.45f, 0.14f);
        d["Ceramic"] = PbrArmorMaterial("M_TypeD_ArmorCeramic", bodySrc, atlas, 0.25f, 0.12f);
        d["Visor"] = ArmorMaterial("M_TypeD_Visor", bodySrc, null, new Color(0.035f, 0.04f, 0.05f), 0.06f, new Color(1.6f, 1.7f, 1.9f), 0.2f, 0.95f);

        Shader energy = Shader.Find("ChuOn/EnergyEmissive");
        d["Glow"] = EnergyMaterial("M_TypeD_Glow", energy, AccentColor(), 0.15f);
        d["Core"] = EnergyMaterial("M_TypeD_Core", energy, new Color(0.55f, 0.85f, 1.4f), 0.5f);
        d["VisorGlow"] = EnergyMaterial("M_TypeD_VisorGlow", energy, new Color(0.35f, 0.7f, 1.1f), 0.1f);
        return d;
    }

    static Material Original(Material current, string fallbackPath)
    {
        if (current == null || current.name.StartsWith("M_TypeD_"))
            return AssetDatabase.LoadAssetAtPath<Material>(fallbackPath) ?? current;
        return current;
    }

    static Material LoadOrCreate(string path, Material template, Shader shader)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = template != null ? new Material(template) : new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (template != null)
        {
            m.shader = template.shader;
            m.CopyPropertiesFromMaterial(template);
            m.shaderKeywords = template.shaderKeywords;
        }
        return m;
    }

    // 무광 고무 슈트: 노랑/주황 장식 → 기업 백색 세라믹(텔레그래프 금/호박과 겹치지 않게), 회로 발광 마스크, 림라이트
    static Material SuitMaterial(string name, Material src, string texName, StringBuilder report)
    {
        if (src == null)
            return null;
        Material m = LoadOrCreate(Root + "/Materials/" + name + ".mat", src, null);
        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/ASP/Demo/" + texName + ".psd");
        if (source != null)
        {
            Texture2D readable = ReadableCopy(source, 2048);
            Texture2D recolor = Recolor(readable, out Texture2D circuitMask);
            m.SetTexture("_BaseMap", SaveTexture(recolor, Root + "/Textures/T_TypeD_" + texName + "_Albedo.png", true));
            Texture2D circuit = SaveTexture(circuitMask, Root + "/Textures/T_TypeD_" + texName + "_Circuit.png", false);
            m.SetTexture("_EmissionMap", circuit);
            UnityEngine.Object.DestroyImmediate(readable);
        }
        bool circuits = texName != "boss1"; // 회로는 안쪽 바디슈트에만 (헬멧/건틀릿 외피 제외)
        m.SetFloat("_EmissionToggle", circuits ? 1f : 0f);
        if (circuits)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.3f, 0.65f, 1f) * 0.55f);
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
        }
        m.SetFloat("_RimLightOn", 1f);
        m.EnableKeyword("_RIMLIGHTING_ON");
        m.SetColor("_RimLightColor", new Color(0.5f, 0.68f, 0.95f));
        m.SetFloat("_RimLightStrength", 0.28f);
        m.SetFloat("_RimLightSmoothness", 0.3f);
        m.SetFloat("_SpecularHighlights", 0f);
        m.DisableKeyword("_SPECULAR_LIGHTING_ON");
        m.SetFloat("_PBRSmoothness", 0.08f);
        EditorUtility.SetDirty(m);
        report.Append(name).Append(' ');
        return m;
    }

    // 하드서피스 장갑: ASP 'StylizePBR' 스타일 (금속이 환경 반사로 읽히게) + 아틀라스 노멀/OSM(R 차폐, G 매끈함, B 금속도).
    // 외곽선/셀 램프/림은 몸 재질(템플릿)과 같은 계열 → 슈트와 톤이 이어진다. 큰 면은 발광 없음(블룸은 발광선/코어/바이저만).
    static Material PbrArmorMaterial(string name, Material template, ArmorAtlas atlas, float pbrInfluence, float rim)
    {
        Material m = LoadOrCreate(Root + "/Materials/" + name + ".mat", template, null);
        foreach (string k in new[] { "_STYLE_EYE", "_EYESTYLE_FLATLIGHTING", "_USEPUPILMASK", "_ALPHATEST_ON", "_STYLE_CELSHADING", "_STYLE_FACE",
                                     "_FACESHADOW", "_HAIRMAP", "_SSSMAP", "_RAMP_OCCLUSION_MAP", "_ENVIRONMENTREFLECTIONS_OFF" })
            m.DisableKeyword(k);
        m.SetFloat("_style", 0f);
        m.EnableKeyword("_STYLE_STYLIZEPBR");
        m.SetFloat("_AlphaClip", 0f);
        m.SetTexture("_BaseMap", atlas.albedo);
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BumpMap", atlas.normal);
        m.SetFloat("_BumpScale", 1f);
        SetKeyword(m, "_NORMALMAP", atlas.normal != null);
        m.SetTexture("_OSMTexture", atlas.osm);
        SetKeyword(m, "_METALLICSPECGLOSSMAP", atlas.osm != null);
        m.SetFloat("_IsUsingOSMTexture", atlas.osm != null ? 1f : 0f);
        m.SetFloat("_PBROcclusionStrength", 1f);
        m.SetFloat("_PBRSmoothness", 1f);   // OSM.g 가 절대값 (금속 0.56 = 거칠기 0.44, 마모 모서리 0.7)
        m.SetFloat("_PBRMetallic", 0.8f);   // OSM 없을 때만 사용
        m.SetFloat("_PBRInfluenceDirectLighting", pbrInfluence);
        m.SetFloat("_SpecularHighlights", 1f);
        m.EnableKeyword("_SPECULAR_LIGHTING_ON");
        m.SetFloat("_EnvironmentReflections", 1f);
        m.SetFloat("_RimLightOn", 1f);
        m.EnableKeyword("_RIMLIGHTING_ON");
        m.SetColor("_RimLightColor", new Color(0.62f, 0.72f, 0.9f));
        m.SetFloat("_RimLightStrength", rim);
        m.SetFloat("_EmissionToggle", 0f);
        m.DisableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void SetKeyword(Material m, string keyword, bool on)
    {
        if (on)
            m.EnableKeyword(keyword);
        else
            m.DisableKeyword(keyword);
    }

    // 바이저 유리 띠 (셀 셰이딩 + 강한 스펙큘러)
    static Material ArmorMaterial(string name, Material template, Texture2D baseMap, Color baseColor, float specSize, Color specColor, float metallic, float smoothness)
    {
        Material m = LoadOrCreate(Root + "/Materials/" + name + ".mat", template, null);
        // 데모 재질의 얼굴/눈 전용 키워드 제거, 셀 셰이딩 유지 (몸과 같은 톤 + 외곽선)
        m.DisableKeyword("_STYLE_EYE");
        m.DisableKeyword("_EYESTYLE_FLATLIGHTING");
        m.DisableKeyword("_USEPUPILMASK");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetFloat("_AlphaClip", 0f);
        m.SetTexture("_BaseMap", baseMap);
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_SpecularHighlights", 1f);
        m.EnableKeyword("_SPECULAR_LIGHTING_ON");
        m.SetColor("_SpecularColor", specColor);
        m.SetColor("_SpecularFallOffColor", specColor * 0.25f);
        m.SetFloat("_SpecularSize", specSize);
        m.SetFloat("_SpecularFalloff", 0.35f);
        m.SetFloat("_PBRMetallic", metallic);
        m.SetFloat("_PBRSmoothness", smoothness);
        m.SetFloat("_EnvironmentReflections", 1f);
        m.SetFloat("_RimLightOn", 1f);
        m.EnableKeyword("_RIMLIGHTING_ON");
        m.SetColor("_RimLightColor", new Color(0.62f, 0.72f, 0.9f));
        m.SetFloat("_RimLightStrength", 0.22f);
        m.SetFloat("_EmissionToggle", 0f);
        m.DisableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material EnergyMaterial(string name, Shader shader, Color color, float flow)
    {
        string path = Root + "/Materials/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        m.SetColor("_Color", color);
        m.SetColor("_RimColor", color * 1.4f);
        m.SetFloat("_Intensity", 1f);
        m.SetFloat("_FlowAmount", flow);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ───────────────────────── 텍스처 생성 ─────────────────────────
    static Texture2D ReadableCopy(Texture2D src, int size)
    {
        var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        t.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        t.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return t;
    }

    static Texture2D Recolor(Texture2D src, out Texture2D circuitMask)
    {
        int w = src.width, h = src.height;
        Color32[] p = src.GetPixels32();
        var outPx = new Color32[p.Length];
        var suit = new bool[p.Length];
        for (int i = 0; i < p.Length; i++)
        {
            Color c = p[i];
            Color.RGBToHSV(c, out float hh, out float ss, out float vv);
            float hueW = Mathf.Clamp01(1f - Mathf.Abs(hh * 360f - 36f) / 26f);
            float w0 = hueW * Smooth(0.25f, 0.5f, ss) * Smooth(0.22f, 0.42f, vv);
            // 세라믹: 원래 명암을 살리고 채도를 빼서 차가운 회백색으로 (반복 11: 명도 ~0.55 상한 — 허벅지/정강이가 하얗게 뜨지 않게)
            float lum = 0.3f + 0.27f * vv;
            Color ceramic = new Color(lum * 0.96f, lum * 0.98f, lum, c.a);
            Color o = Color.Lerp(c, ceramic, w0);
            outPx[i] = o;
            // 슈트(회색, 저채도, 중간 명도) 영역 = 회로 후보
            suit[i] = ss < 0.18f && vv > 0.16f && vv < 0.55f;
        }
        var result = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
        result.SetPixels32(outPx);
        result.Apply();

        // 회로 마스크 (1024): 격자 위 맨해튼/45도 경로 + 끝 패드, 슈트 영역 내부(가장자리 여백)만
        int cs = 1024;
        float scale = (float)w / cs;
        var mask = new byte[cs * cs];
        Func<int, int, bool> inside = (x, y) =>
        {
            if (x < 6 || y < 6 || x >= cs - 6 || y >= cs - 6)
                return false;
            for (int oy = -6; oy <= 6; oy += 6)
            for (int ox = -6; ox <= 6; ox += 6)
            {
                int sx = Mathf.Clamp((int)((x + ox) * scale), 0, w - 1);
                int sy = Mathf.Clamp((int)((y + oy) * scale), 0, h - 1);
                if (!suit[sy * w + sx])
                    return false;
            }
            return true;
        };
        var rng = new System.Random(4207);
        int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };
        for (int trace = 0; trace < 300; trace++)
        {
            int x = rng.Next(cs), y = rng.Next(cs);
            if (!inside(x, y))
                continue;
            int dir = rng.Next(4) * 2;
            int len = 40 + rng.Next(140);
            int seg = 0;
            Pad(mask, cs, x, y, 3);
            for (int s = 0; s < len; s++)
            {
                if (seg > 12 && rng.NextDouble() < 0.05)
                {
                    dir = (dir + (rng.NextDouble() < 0.5 ? 1 : 7)) % 8; // 45도 꺾임
                    seg = 0;
                }
                int nx = x + dx[dir], ny = y + dy[dir];
                if (!inside(nx, ny))
                    break;
                x = nx; y = ny; seg++;
                mask[y * cs + x] = 255;
                mask[y * cs + Mathf.Min(cs - 1, x + (dir % 4 == 2 ? 1 : 0))] = 255;
                mask[Mathf.Min(cs - 1, y + (dir % 4 == 0 ? 1 : 0)) * cs + x] = 255;
            }
            Pad(mask, cs, x, y, 3);
        }
        var mt = new Texture2D(cs, cs, TextureFormat.RGBA32, true, true);
        var mp = new Color32[cs * cs];
        for (int i = 0; i < mp.Length; i++)
            mp[i] = new Color32(mask[i], mask[i], mask[i], 255);
        mt.SetPixels32(mp);
        mt.Apply();
        circuitMask = mt;
        return result;
    }

    // GLSL smoothstep (Mathf.SmoothStep 은 from..to 사이 보간이라 0..1 마스크가 안 된다)
    static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    static void Pad(byte[] m, int cs, int cx, int cy, int r)
    {
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
        {
            int px = cx + x, py = cy + y;
            if (px < 0 || py < 0 || px >= cs || py >= cs)
                continue;
            float d = Mathf.Sqrt(x * x + y * y);
            if (d <= r && d >= r - 1.6f)
                m[py * cs + px] = 255;
        }
    }

    static Texture2D SaveTexture(Texture2D tex, string path, bool srgb)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.sRGBTexture = srgb;
        imp.mipmapEnabled = true;
        imp.maxTextureSize = Mathf.Max(tex.width, 512);
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        UnityEngine.Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ───────────────────────── 반복 10 VFX/오디오 리소스 ─────────────────────────
    [MenuItem("Tools/ChuOn/VFX/Build Iteration 10 Assets")]
    public static void BuildVfxMenu() => Debug.Log(BuildVfxAndAudio());

    public static string BuildVfxAndAudio()
    {
        EnsureFolder(VfxResources);
        EnsureFolder(AudioResources);
        var sb = new StringBuilder();
        sb.Append(ShaderMaterial(VfxResources + "/ChuonBladeTrail.mat", "ChuOn/BladeTrail")).Append(' ');
        sb.Append(ShaderMaterial(VfxResources + "/ChuonTelegraphGround.mat", "ChuOn/TelegraphGround")).Append(' ');
        // Type-D 무기 궤적: 같은 리본 셰이더, 차가운 백색 코어 + 시안 가장자리 (BossSwordTrailRibbon 은 u=칼날/v=나이)
        Material bossTrail = AssetDatabase.LoadAssetAtPath<Material>(VfxResources + "/ChuonBossBladeTrail.mat");
        if (bossTrail == null)
        {
            bossTrail = new Material(Shader.Find("ChuOn/BladeTrail"));
            AssetDatabase.CreateAsset(bossTrail, VfxResources + "/ChuonBossBladeTrail.mat");
        }
        bossTrail.SetColor("_CoreColor", new Color(2.3f, 2.8f, 3.2f));
        bossTrail.SetColor("_EdgeColor", new Color(0.15f, 0.75f, 1.5f));
        bossTrail.SetFloat("_SwapUV", 1f);
        bossTrail.SetFloat("_HiltFade", 0.5f);
        bossTrail.SetFloat("_CoreWidth", 0.4f);
        EditorUtility.SetDirty(bossTrail);
        sb.Append("ChuonBossBladeTrail ");
        sb.Append(ShaderMaterial(VfxResources + "/ChuonTelegraphFlare.mat", "ChuOn/TelegraphFlare")).Append(' ');
        sb.Append(ShaderMaterial(VfxResources + "/ChuonWeaponTelegraph.mat", "ChuOn/WeaponTelegraph")).Append(' ');
        sb.Append(TelegraphCueAudio.BuildAll(AudioResources));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    static string ShaderMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            return "missing shader " + shaderName;
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        EditorUtility.SetDirty(m);
        return Path.GetFileNameWithoutExtension(path);
    }

    // ───────────────────────── util ─────────────────────────
    static SkinnedMeshRenderer FindSkinned(Transform root, string name)
    {
        foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name == name)
                return r;
        return null;
    }

    static Animator FindHumanoid(Transform root)
    {
        foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
            if (a.isHuman && a.avatar != null && a.GetBoneTransform(HumanBodyBones.Head) != null)
                return a;
        return null;
    }

    static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}

// 텔레그래프 큐 효과음: Assets/Art/Audio 의 기존 클립을 다듬어(앞 무음 제거, 피치, 레이어, 엔벨로프) 타입별로 구별되는 짧은 큐를 만든다.
// 큐의 어택이 0 ms 에 오도록 해서 '정직한 큐' 시각과 소리가 맞는다.
public static class TelegraphCueAudio
{
    const int Rate = 44100;

    public static string BuildAll(string dir)
    {
        float[] parry = Load("Assets/Art/Audio/Parry.wav");
        float[] clash = Load("Assets/Art/Audio/sword-clashhit-393837.mp3");
        float[] armor = Load("Assets/Art/Audio/armor-impact-from-sword-393843.mp3");
        float[] slice = Load("Assets/Art/Audio/violent-sword-slice-2-393841.mp3");
        float[] blade = Load("Assets/Art/Audio/검_5.mp3");
        if (parry == null || clash == null || armor == null || slice == null || blade == null)
            return "audio sources missing";

        var sb = new StringBuilder();
        // 패링 가능: 밝고 짧은 '팅' (패링 소리 + 2.6 kHz 핑)
        float[] p = Mix(Len(0.42f), Pitch(Trim(parry), 1.22f), 0, 0.85f);
        p = Mix(p, Sine(2600f, 0.3f, 14f), 0, 0.22f);
        sb.Append(Save(dir, "TelegraphCue_Parryable", Finish(p, 0.06f)));
        // 회피 전용: 거친 '챙-챙' 2연타
        float[] cl = Trim(clash);
        float[] d = Mix(Len(0.48f), Pitch(cl, 0.9f), 0, 0.85f);
        d = Mix(d, Pitch(cl, 1.05f), (int)(0.085f * Rate), 0.7f);
        sb.Append(Save(dir, "TelegraphCue_DodgeOnly", Finish(d, 0.08f)));
        // 가드·패링 불가: 낮고 무거운 금속 굉음 + 65 Hz 저음
        float[] u = Mix(Len(0.8f), Pitch(Trim(armor), 0.62f), 0, 0.9f);
        u = Mix(u, Sine(65f, 0.7f, 4.5f), 0, 0.45f);
        sb.Append(Save(dir, "TelegraphCue_Unblockable", Finish(u, 0.15f)));
        // 강공(호박): 묵직한 칼 울림
        float[] hv = Mix(Len(0.6f), Pitch(Trim(blade), 0.72f), 0, 0.85f);
        hv = Mix(hv, Pitch(Trim(armor), 0.82f), 0, 0.4f);
        sb.Append(Save(dir, "TelegraphCue_Heavy", Finish(hv, 0.12f)));
        // 원거리: 빠르게 긁는 '슉'
        float[] r = Mix(Len(0.4f), Pitch(Trim(slice), 1.4f), 0, 0.9f);
        sb.Append(Save(dir, "TelegraphCue_Ranged", Finish(HighPass(r), 0.08f)));
        return sb.ToString();
    }

    static float[] Load(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            return null;
        if (clip.loadState != AudioDataLoadState.Loaded)
            clip.LoadAudioData();
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0))
            return null;
        // 모노 + 44.1k 로
        var mono = new float[clip.samples];
        for (int i = 0; i < clip.samples; i++)
        {
            float s = 0f;
            for (int c = 0; c < clip.channels; c++)
                s += data[i * clip.channels + c];
            mono[i] = s / clip.channels;
        }
        if (clip.frequency != Rate)
            mono = Pitch(mono, clip.frequency / (float)Rate);
        return mono;
    }

    static float[] Trim(float[] x)
    {
        float peak = 0f;
        for (int i = 0; i < x.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(x[i]));
        int start = 0;
        for (int i = 0; i < x.Length; i++)
            if (Mathf.Abs(x[i]) > peak * 0.06f) { start = Mathf.Max(0, i - (int)(0.002f * Rate)); break; }
        var o = new float[x.Length - start];
        Array.Copy(x, start, o, 0, o.Length);
        return o;
    }

    static float[] Pitch(float[] x, float ratio)
    {
        int n = Mathf.Max(1, (int)(x.Length / ratio));
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            float f = i * ratio;
            int a = (int)f;
            float t = f - a;
            float s0 = a < x.Length ? x[a] : 0f;
            float s1 = a + 1 < x.Length ? x[a + 1] : 0f;
            o[i] = Mathf.Lerp(s0, s1, t);
        }
        return o;
    }

    static float[] Len(float seconds) => new float[(int)(seconds * Rate)];

    static float[] Mix(float[] dst, float[] src, int offset, float gain)
    {
        for (int i = 0; i < src.Length && i + offset < dst.Length; i++)
            dst[i + offset] += src[i] * gain;
        return dst;
    }

    static float[] Sine(float hz, float seconds, float decay)
    {
        var o = Len(seconds);
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)Rate;
            o[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-decay * t) * Mathf.Clamp01(t / 0.002f);
        }
        return o;
    }

    static float[] HighPass(float[] x)
    {
        var o = new float[x.Length];
        float prevIn = 0f, prevOut = 0f;
        const float a = 0.92f;
        for (int i = 0; i < x.Length; i++)
        {
            prevOut = a * (prevOut + x[i] - prevIn);
            prevIn = x[i];
            o[i] = prevOut;
        }
        return o;
    }

    static float[] Finish(float[] x, float fadeOut)
    {
        int f = Mathf.Min(x.Length, (int)(fadeOut * Rate));
        for (int i = 0; i < f; i++)
            x[x.Length - 1 - i] *= i / (float)f;
        float peak = 0f;
        for (int i = 0; i < x.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(x[i]));
        float g = peak > 0.0001f ? 0.89f / peak : 1f;
        for (int i = 0; i < x.Length; i++)
            x[i] *= g;
        return x;
    }

    static string Save(string dir, string name, float[] x)
    {
        string path = dir + "/" + name + ".wav";
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int bytes = x.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            for (int i = 0; i < x.Length; i++)
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(x[i] * 32767f), -32768, 32767));
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (AudioImporter)AssetImporter.GetAtPath(path);
        var settings = imp.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.ADPCM;
        imp.defaultSampleSettings = settings;
        imp.SaveAndReimport();
        return name + "(" + (x.Length / (float)Rate).ToString("F2") + "s) ";
    }
}
#endif
