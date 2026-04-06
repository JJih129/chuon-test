using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class UltimateClipResourceBootstrap
{
    const string IntroPoseSourceAssetPath = "Assets/AnimeAsset/Sp_Idle.anim";
    const string IntroPoseOutputAssetPath = "Assets/Resources/Ultimate/Clips/Sp_Idle_IntroPose.anim";
    const float IntroPoseNormalizedTime = 0.205f;
    const string DashClipSourceAssetPath =
        "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Special/Sp_Skill3.fbx";
    const string DashClipSourceName = "Sp_Skill3";
    const string DashClipOutputAssetPath = "Assets/Resources/Ultimate/Clips/Sp_Skill3_Ultimate.anim";

    static bool _queued;

    static UltimateClipResourceBootstrap()
    {
        QueueEnsureRuntimeUltimateClips();
    }

    public static void EnsureRuntimeUltimateClipsFromMenu()
    {
        EnsureRuntimeUltimateClips();
    }

    static void QueueEnsureRuntimeUltimateClips()
    {
        if (_queued)
            return;

        _queued = true;
        EditorApplication.delayCall += EnsureRuntimeUltimateClips;
    }

    static void EnsureRuntimeUltimateClips()
    {
        _queued = false;
        EnsureStandalonePoseClip(IntroPoseSourceAssetPath, IntroPoseOutputAssetPath, IntroPoseNormalizedTime);
        EnsureStandaloneClip(DashClipSourceAssetPath, DashClipSourceName, DashClipOutputAssetPath);
    }

    static void EnsureStandalonePoseClip(string sourceAssetPath, string outputAssetPath, float normalizedTime)
    {
        AnimationClip sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourceAssetPath);
        if (sourceClip == null)
        {
            Debug.LogWarning($"[UltimateClipBootstrap] Source pose clip not found at '{sourceAssetPath}'.");
            return;
        }

        EnsureFolderExists(Path.GetDirectoryName(outputAssetPath));
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
            AssetDatabase.DeleteAsset(outputAssetPath);

        AnimationClip poseClip = CreatePoseClip(sourceClip, normalizedTime);
        poseClip.name = Path.GetFileNameWithoutExtension(outputAssetPath);
        AssetDatabase.CreateAsset(poseClip, outputAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);
    }

    static void EnsureStandaloneClip(string sourceAssetPath, string sourceClipName, string outputAssetPath)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
            return;

        AnimationClip sourceClip = LoadNamedClip(sourceAssetPath, sourceClipName);
        if (sourceClip == null)
        {
            Debug.LogWarning($"[UltimateClipBootstrap] Source clip '{sourceClipName}' not found at '{sourceAssetPath}'.");
            return;
        }

        EnsureFolderExists(Path.GetDirectoryName(outputAssetPath));

        AnimationClip standaloneClip = Object.Instantiate(sourceClip);
        standaloneClip.name = Path.GetFileNameWithoutExtension(outputAssetPath);
        AssetDatabase.CreateAsset(standaloneClip, outputAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);
    }

    static AnimationClip CreatePoseClip(AnimationClip sourceClip, float normalizedTime)
    {
        float sourceLength = Mathf.Max(1f / 60f, sourceClip.length);
        float sampleTime = Mathf.Clamp01(normalizedTime) * sourceLength;
        float frameRate = sourceClip.frameRate > 0f ? sourceClip.frameRate : 60f;
        float holdTime = 1f / frameRate;

        AnimationClip poseClip = new AnimationClip
        {
            frameRate = frameRate,
            wrapMode = WrapMode.ClampForever
        };

        EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(sourceClip);
        for (int i = 0; i < floatBindings.Length; i++)
        {
            EditorCurveBinding binding = floatBindings[i];
            AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (sourceCurve == null)
                continue;

            float sampledValue = sourceCurve.Evaluate(sampleTime);
            AnimationCurve poseCurve = new AnimationCurve(
                new Keyframe(0f, sampledValue),
                new Keyframe(holdTime, sampledValue));
            AnimationUtility.SetEditorCurve(poseClip, binding, poseCurve);
        }

        EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(sourceClip);
        for (int i = 0; i < objectBindings.Length; i++)
        {
            EditorCurveBinding binding = objectBindings[i];
            ObjectReferenceKeyframe[] sourceKeys = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
            if (sourceKeys == null || sourceKeys.Length == 0)
                continue;

            ObjectReferenceKeyframe selectedKey = sourceKeys[0];
            for (int keyIndex = 0; keyIndex < sourceKeys.Length; keyIndex++)
            {
                if (sourceKeys[keyIndex].time > sampleTime)
                    break;

                selectedKey = sourceKeys[keyIndex];
            }

            ObjectReferenceKeyframe[] poseKeys =
            {
                new ObjectReferenceKeyframe { time = 0f, value = selectedKey.value },
                new ObjectReferenceKeyframe { time = holdTime, value = selectedKey.value }
            };
            AnimationUtility.SetObjectReferenceCurve(poseClip, binding, poseKeys);
        }

        return poseClip;
    }

    static AnimationClip LoadNamedClip(string assetPath, string clipName)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is AnimationClip clip && clip.name == clipName && !clip.name.StartsWith("__preview__"))
                return clip;
        }

        return null;
    }

    static void EnsureFolderExists(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        string normalized = folderPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(normalized))
            return;

        string[] segments = normalized.Split('/');
        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = $"{current}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
    }
}
