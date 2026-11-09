// 추온 오디오 믹서 생성기 (반복 11) — 메뉴에서 수동 실행 (자동 실행 없음)
// Assets/Art/ChuonPolish/Audio/Resources/Audio/ChuonMixer.mixer
//   Master (Compressor: 클리핑 방지)
//     ├ Music (Lowpass Simple, 볼륨 노출 'MusicDuck', 컷오프 노출 'MusicLowpass')
//     ├ SFX   (Lowpass Simple, 컷오프 노출 'SfxLowpass')
//     ├ Voice
//     └ UI
// Unity 에 믹서 생성 공개 API 가 없어 에디터 내부 AudioMixerController 를 리플렉션으로 사용한다.
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class ChuonAudioMixerBuilder
{
    public const string MixerPath = "Assets/Art/ChuonPolish/Audio/Resources/Audio/ChuonMixer.mixer";
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("ChuOn/Audio/Build Audio Mixer")]
    static void BuildMenu()
    {
        Debug.Log(Build(false));
    }

    public static string Build(bool overwrite)
    {
        var log = new StringBuilder();
        if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null)
        {
            if (!overwrite)
                return "mixer exists: " + MixerPath;
            AssetDatabase.DeleteAsset(MixerPath);
        }

        Assembly asm = typeof(EditorWindow).Assembly;
        Type ctrlType = asm.GetType("UnityEditor.Audio.AudioMixerController");
        Type groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
        Type effectType = asm.GetType("UnityEditor.Audio.AudioMixerEffectController");
        Type groupPathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
        Type effectPathType = asm.GetType("UnityEditor.Audio.AudioEffectParameterPath");
        Type exposedType = asm.GetType("UnityEditor.Audio.ExposedAudioParameter");
        if (ctrlType == null || groupType == null || effectType == null || groupPathType == null || effectPathType == null || exposedType == null)
            return "reflection types missing";

        object ctrl = ctrlType.GetMethod("CreateMixerControllerAtPath", All).Invoke(null, new object[] { MixerPath });
        object master = ctrlType.GetProperty("masterGroup", All).GetValue(ctrl);
        object snapshot = ctrlType.GetProperty("TargetSnapshot", All)?.GetValue(ctrl)
                          ?? ctrlType.GetProperty("startSnapshot", All)?.GetValue(ctrl);

        object NewGroup(string name)
        {
            object g = Call(ctrlType, ctrl, "CreateNewGroup", name, false);
            Call(ctrlType, ctrl, "AddChildToParent", g, master);
            try { Call(ctrlType, ctrl, "AddGroupToCurrentView", g); }
            catch (Exception e) { log.Append("view(").Append(name).Append("): ").Append(e.InnerException != null ? e.InnerException.Message : e.Message).Append("; "); }
            return g;
        }

        object music = NewGroup("Music");
        object sfx = NewGroup("SFX");
        object voice = NewGroup("Voice");
        object ui = NewGroup("UI");
        log.Append("groups ok; ");

        object AddEffect(object group, string effectName)
        {
            object effect = Activator.CreateInstance(effectType, All, null, new object[] { effectName }, null);
            Call(effectType, effect, "PreallocateGUIDs");
            Call(ctrlType, ctrl, "AddNewSubAsset", effect, false);
            object[] effects = (object[])((Array)groupType.GetProperty("effects", All).GetValue(group)).Cast<object>().ToArray();
            Call(groupType, group, "InsertEffect", effect, effects.Length);
            return effect;
        }

        GUID EffectParam(object effect, string param) => (GUID)Call(effectType, effect, "GetGUIDForParameter", param);

        void SetSnapshot(GUID guid, float value)
        {
            if (snapshot != null)
                Call(snapshot.GetType(), snapshot, "SetValue", guid, value);
        }

        void Expose(object pathObj, GUID guid, string name)
        {
            Call(ctrlType, ctrl, "AddExposedParameter", pathObj);
            PropertyInfo prop = ctrlType.GetProperty("exposedParameters", All);
            Array arr = (Array)prop.GetValue(ctrl);
            FieldInfo guidField = exposedType.GetField("guid", All);
            FieldInfo nameField = exposedType.GetField("name", All);
            for (int i = 0; i < arr.Length; i++)
            {
                object p = arr.GetValue(i);
                if ((GUID)guidField.GetValue(p) == guid)
                {
                    nameField.SetValue(p, name);
                    arr.SetValue(p, i);
                }
            }
            prop.SetValue(ctrl, arr);
        }

        // Music: 덕킹용 볼륨 노출 + 로우패스
        GUID musicVol = (GUID)Call(groupType, music, "GetGUIDForVolume");
        Expose(Activator.CreateInstance(groupPathType, new object[] { music, musicVol }), musicVol, ChuonAudioMix.MusicDuckParam);
        object musicLp = AddEffect(music, "Lowpass Simple");
        GUID musicLpGuid = EffectParam(musicLp, "Cutoff freq");
        SetSnapshot(musicLpGuid, 22000f);
        Expose(Activator.CreateInstance(effectPathType, new object[] { music, musicLp, musicLpGuid }), musicLpGuid, ChuonAudioMix.MusicLowpassParam);

        // SFX: 로우패스
        object sfxLp = AddEffect(sfx, "Lowpass Simple");
        GUID sfxLpGuid = EffectParam(sfxLp, "Cutoff freq");
        SetSnapshot(sfxLpGuid, 22000f);
        Expose(Activator.CreateInstance(effectPathType, new object[] { sfx, sfxLp, sfxLpGuid }), sfxLpGuid, ChuonAudioMix.SfxLowpassParam);
        log.Append("lowpass ok; ");

        // SFX: 컴프레서로 효과음 간 음량 차(측정 RMS -14.5 ~ -24.5 dBFS)를 줄인다
        object sfxComp = AddEffect(sfx, "Compressor");
        SetSnapshot(EffectParam(sfxComp, "Threshold"), -12f);
        SetSnapshot(EffectParam(sfxComp, "Attack"), 4f);
        SetSnapshot(EffectParam(sfxComp, "Release"), 90f);
        SetSnapshot(EffectParam(sfxComp, "Make up gain"), 2f);

        // Master: 컴프레서 (리미터처럼: 빠른 어택, -3 dB 임계)
        object comp = AddEffect(master, "Compressor");
        SetSnapshot(EffectParam(comp, "Threshold"), -3f);
        SetSnapshot(EffectParam(comp, "Attack"), 2f);
        SetSnapshot(EffectParam(comp, "Release"), 120f);
        SetSnapshot(EffectParam(comp, "Make up gain"), 0f);
        log.Append("compressor ok; ");

        Call(ctrlType, ctrl, "OnSubAssetChanged");
        EditorUtility.SetDirty((UnityEngine.Object)ctrl);
        AssetDatabase.SaveAssets();

        // 새 믹서에는 그룹 뷰가 없어 믹서 창에서 그룹이 안 보이므로 기본 'View' 를 YAML 에 추가한다
        string full = System.IO.Path.GetFullPath(MixerPath);
        string text = System.IO.File.ReadAllText(full);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        if (text.Contains("m_AudioMixerGroupViews: []"))
        {
            var ids = System.Text.RegularExpressions.Regex.Matches(text, @"m_GroupID: ([0-9a-f]{32})");
            var view = new StringBuilder("m_AudioMixerGroupViews:").Append(nl).Append("  - guids:").Append(nl);
            foreach (System.Text.RegularExpressions.Match m in ids)
                view.Append("    - ").Append(m.Groups[1].Value).Append(nl);
            view.Append("    name: View");
            text = text.Replace("m_AudioMixerGroupViews: []", view.ToString());
            System.IO.File.WriteAllText(full, text);
            log.Append("view ok; ");
        }
        AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);

        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        log.Append("saved: ").Append(mixer != null).Append(" groups=");
        if (mixer != null)
            log.Append(string.Join(",", mixer.FindMatchingGroups("").Select(g => g.name)));
        return log.ToString();
    }

    /// <summary>QA: 믹서 그룹/노출 파라미터/스냅샷 값 덤프.</summary>
    public static string Dump()
    {
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null)
            return "no mixer";
        var sb = new StringBuilder();
        sb.Append("groups: ").Append(string.Join(", ", mixer.FindMatchingGroups("").Select(g => g.name + (g.audioMixer == mixer ? "" : "?")))).Append('\n');
        foreach (string p in new[] { ChuonAudioMix.MusicDuckParam, ChuonAudioMix.SfxLowpassParam, ChuonAudioMix.MusicLowpassParam })
            sb.Append(p).Append('=').Append(mixer.GetFloat(p, out float v) ? v.ToString("F1") : "missing").Append('\n');
        return sb.ToString();
    }

    static object Call(Type type, object target, string method, params object[] args)
    {
        MethodInfo m = type.GetMethods(All).FirstOrDefault(mi => mi.Name == method && mi.GetParameters().Length == args.Length);
        if (m == null)
            throw new MissingMethodException(type.Name, method + "/" + args.Length);
        return m.Invoke(target, args);
    }
}
