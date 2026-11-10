// 반복 14 시네마틱 에셋 빌더 (메뉴 실행 전용, 자동 실행 없음).
// 메뉴: Tools/Chuon/Cinematics/Rebuild Cinematic Assets
// 입력: Tools/ArtGen/cine_props_blender.py (SM_CINE_*.fbx), Tools/ArtGen/cine_audio.py (*.wav), 인필트레이션 키트(SM_INF_*, M_INF_*),
//       캐릭터 팩(PlayerVisual_Chuon, GhostSamurai_Animset, CombatGirls Humanoid_F) 애니메이션.
// 출력: Assets/Cinematics/Chuon/Timelines/TL_Cine_*.playable, Assets/Cinematics/Chuon/Prefabs/CINE_*.prefab,
//       Assets/Cinematics/Chuon/Resources/Cinematics/DialogueTable_Cine_KO.asset, Assets/Art/ChuonPolish/Cinematics/Materials/M_CINE_*.mat
// 리그는 프리뷰 씬에서 조립해 프리팹으로 저장한다 (열린 씬은 건드리지 않는다). 샷 문법·앵커: Docs/Cinematics_Shotlist.md
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

public static class CinematicsAssetBuilder
{
    public const string Root = "Assets/Cinematics/Chuon";
    public const string TimelineDir = Root + "/Timelines";
    public const string PrefabDir = Root + "/Prefabs";
    public const string ResDir = Root + "/Resources/Cinematics";
    public const string ArtDir = "Assets/Art/ChuonPolish/Cinematics";
    public const string MeshDir = ArtDir + "/Meshes";
    public const string MatDir = ArtDir + "/Materials";
    public const string AudioDir = ArtDir + "/Audio";
    const string InfMatDir = "Assets/Art/ChuonPolish/Tutorial/Materials";
    const string InfMeshDir = "Assets/Art/ChuonPolish/Tutorial/Meshes";
    const string GS = "Assets/GhostSamurai_Animset/Animation/katana";
    const string ChuonVisual = "Assets/Prefabs/Generated/PlayerVisual_Chuon.prefab";
    const string GhostModel = "Assets/GhostSamurai_Animset/Model/Model_Unity_Ver1.FBX";
    const string AndroidModel = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/Humanoid_Bot/Models/Humanoid_F.fbx";
    const string AndroidPrefab = "Assets/Prefabs/Mobs/Mob_SecurityAndroid.prefab";
    const string NoisePath = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";
    public const string TypeDOriginal = "Assets/Cinematics/MainScene/TL_MainScene_Intro.playable";

    // 시퀀스 id (CinematicDirector.Play 키) ↔ 프리팹 이름
    public const string IdOpening = "opening";
    public const string IdTutBoot = "tut_vr_boot";
    public const string IdTutExit = "tut_vr_exit";
    public const string IdLobbyAlarm = "lobby_alarm";
    public const string IdSkydeck = "skydeck_lookdown";
    public const string IdElevator = "elevator_descent";
    public const string IdEnding = "ending_tsukasa";

    static readonly StringBuilder Log = new StringBuilder();
    static Scene s_stage;
    static readonly Dictionary<string, Material> M = new Dictionary<string, Material>();

    [MenuItem("Tools/Chuon/Cinematics/Rebuild Cinematic Assets (timelines + rigs)")]
    public static void BuildAllMenu() => Debug.Log(BuildAll());

    public static string BuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "[CinematicsAssetBuilder] refused: play mode";
        Log.Clear();
        EnsureFolders();
        BuildMaterials();
        RemapModels();
        BuildDialogueTable();
        s_stage = EditorSceneManager.NewPreviewScene();
        try
        {
            BuildOpening();
            BuildTutBoot();
            BuildTutExit();
            BuildLobbyAlarm();
            BuildSkydeck();
            BuildElevator();
            BuildEnding();
            BuildTypeDRevealV2();
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(s_stage);
        }
        AssetDatabase.SaveAssets();
        return "[CinematicsAssetBuilder] " + Log;
    }

    // ------------------------------------------------------------------ setup

    static void EnsureFolders()
    {
        foreach (string p in new[] { TimelineDir, PrefabDir, ResDir, MatDir })
            if (!AssetDatabase.IsValidFolder(p))
            {
                Directory.CreateDirectory(p);
                AssetDatabase.Refresh();
            }
    }

    static Material LoadInf(string slot) => AssetDatabase.LoadAssetAtPath<Material>($"{InfMatDir}/M_INF_{slot}.mat");

    static void BuildMaterials()
    {
        M.Clear();
        foreach (string s in new[] { "Graphite", "Ceramic", "Concrete", "Glass", "Red", "Amber", "White", "Rubber", "Hazard", "FacadeA", "FacadeB", "Container", "Shutter", "Steel", "Asphalt", "Sky", "Skyline", "Emblem", "EmblemWhite" })
        {
            Material m = LoadInf(s);
            if (m != null) M[s] = m; else Log.Append($"missing M_INF_{s}; ");
        }

        M["Holo"] = EnsureMat("M_CINE_Hologram", "Chuon/CineHologram", m =>
        {
            m.SetColor("_Color", new Color(0.86f, 0.9f, 0.95f, 1f));
            m.SetColor("_RimColor", new Color(1f, 0.12f, 0.1f, 1f));
            m.SetFloat("_Alpha", 0.5f);
        });
        M["Steam"] = EnsureParticleMat("M_CINE_Steam", false);
        M["Embers"] = EnsureParticleMat("M_CINE_Embers", true);
        M["Screen"] = EnsureMat("M_CINE_Screen", "Universal Render Pipeline/Unlit", m =>
        {
            m.SetColor("_BaseColor", new Color(0.16f, 0.025f, 0.025f, 1f));
        });
        M["Laser"] = EnsureMat("M_CINE_Laser", "Universal Render Pipeline/Unlit", m =>
        {
            m.SetColor("_BaseColor", new Color(1f, 0.08f, 0.06f, 1f));
        });
    }

    static Material EnsureMat(string name, string shader, System.Action<Material> init)
    {
        string path = $"{MatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find(shader);
        if (m == null)
        {
            m = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        else if (sh != null && m.shader != sh)
        {
            m.shader = sh;
        }
        init?.Invoke(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material EnsureParticleMat(string name, bool additive)
    {
        return EnsureMat(name, "Universal Render Pipeline/Particles/Unlit", m =>
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetColor("_BaseColor", Color.white);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", additive ? (int)UnityEngine.Rendering.BlendMode.One : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        });
    }

    static void RemapModels()
    {
        string[] slots = { "Graphite", "Ceramic", "Concrete", "Glass", "Red", "Amber", "White", "Rubber", "Hazard", "FacadeA", "FacadeB", "Container", "Shutter", "Steel" };
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MeshDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            bool changed = false;
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> map = mi.GetExternalObjectMap();
            foreach (string s in slots)
            {
                if (!M.TryGetValue(s, out Material target)) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "INF_" + s);
                if (!map.TryGetValue(id, out Object cur) || cur != target) { mi.AddRemap(id, target); changed = true; }
            }
            var holoId = new AssetImporter.SourceAssetIdentifier(typeof(Material), "CINE_Holo");
            if (!map.TryGetValue(holoId, out Object hcur) || hcur != M["Holo"]) { mi.AddRemap(holoId, M["Holo"]); changed = true; }
            if (mi.importCameras || mi.importLights || mi.importAnimation) { mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; changed = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
            if (changed) { mi.SaveAndReimport(); n++; }
        }
        Log.Append($"remapped {n} models; ");
    }

    // ------------------------------------------------------------------ dialogue (반복 14 신규 대사 — 리드가 메인 테이블에 병합할 원본)

    public static readonly string[,] NewLines =
    {
        { "cine.card.night", "", "그날 밤" },
        { "cine.card.dawn", "", "새벽" },
        { "cine.open.0", "System", "거점 제압 완료. 생존자 없음.\n회수 대상… 확인 불가." },
        { "cine.open.1", "EGO", "추온. …추온, 눈 떠." },
        { "cine.open.2", "추온", "…다들." },
        { "cine.open.3", "EGO", "정보가 샜어.\n은신처도, 내일 경로도 전부." },
        { "cine.open.4", "EGO", "경로는 이미 털렸어.\n계획대로면 함정 한가운데야." },
        { "cine.open.5", "추온", "상관없어. 정면으로 간다." },
        { "cine.open.6", "EGO", "그 말 기다렸어. 가자, 추온." },
        { "cine.sky.0", "EGO", "유리 아래가 격납고야.\n붉은 격리장 안에… 실험체 하나." },
        { "cine.sky.1", "추온", "…나랑 같은 데서 나왔어." },
        { "cine.elev.0", "EGO", "버튼 하나짜리 엘리베이터라.\n편도 티켓이네." },
        { "cine.elev.1", "추온", "…돌아갈 생각 없어." },
        { "cine.elev.2", "EGO", "알아. 그러니까 끝까지\n같이 가는 거야, 추온." },
        { "cine.end.0", "EGO", "잠깐. 누가 회선에\n끼어들었어." },
    };

    static void BuildDialogueTable()
    {
        string path = $"{ResDir}/DialogueTable_Cine_KO.asset";
        var table = AssetDatabase.LoadAssetAtPath<DialogueTable>(path);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<DialogueTable>();
            AssetDatabase.CreateAsset(table, path);
        }
        var list = new List<DialogueTable.Entry>();
        for (int i = 0; i < NewLines.GetLength(0); i++)
            list.Add(new DialogueTable.Entry { key = NewLines[i, 0], speaker = NewLines[i, 1], text = NewLines[i, 2], duration = 0f });
        table.entries = list.ToArray();
        table.barkGroups = new DialogueTable.BarkGroup[0];
        table.Rebuild();
        EditorUtility.SetDirty(table);
        Log.Append($"dialogue {list.Count} lines; ");
    }

    // ------------------------------------------------------------------ rig builder

    sealed class Rig
    {
        public GameObject root;
        public PlayableDirector director;
        public TimelineAsset timeline;
        public CinematicSequence sequence;
        public string id;
        public Transform stage;
        public Transform cams;
        CinemachineTrack _camTrack;
        CineSubtitleTrack _subTrack;
        CineFadeTrack _fadeTrack;
        AudioTrack _audioTrack;
        readonly Dictionary<Animator, AnimationTrack> _animTracks = new Dictionary<Animator, AnimationTrack>();
        readonly Dictionary<Object, TrackAsset> _otherTracks = new Dictionary<Object, TrackAsset>();
        int _clipSerial;

        public Rig(string sequenceId, string prefabName, string hostScene)
        {
            id = sequenceId;
            root = new GameObject(prefabName);
            SceneManager.MoveGameObjectToScene(root, s_stage);
            director = root.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            director.extrapolationMode = DirectorWrapMode.None;
            root.AddComponent<SignalReceiver>();
            sequence = root.AddComponent<CinematicSequence>();
            stage = Child(root.transform, "Stage");
            cams = Child(stage, "Cams");
            sequence.SetStageRoot(stage.gameObject);

            string tlPath = $"{TimelineDir}/TL_Cine_{prefabName.Replace("CINE_", "")}.playable";
            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(tlPath) != null)
                AssetDatabase.DeleteAsset(tlPath);
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = Path.GetFileNameWithoutExtension(tlPath);
            AssetDatabase.CreateAsset(timeline, tlPath);
            timeline.editorSettings.frameRate = 60d;
            director.playableAsset = timeline;
            _camTrack = timeline.CreateTrack<CinemachineTrack>(null, "TRK_Cam_Shots");
            _subTrack = timeline.CreateTrack<CineSubtitleTrack>(null, "TRK_Subtitles");
            _fadeTrack = timeline.CreateTrack<CineFadeTrack>(null, "TRK_Fade");
            _audioTrack = timeline.CreateTrack<AudioTrack>(null, "TRK_Audio");
            _ = hostScene;
        }

        public Transform Sub(string name, CineAttach.Target target, string anchor = null, CineAttach.Target face = CineAttach.Target.None, string faceAnchor = null, bool follow = false, bool matchYaw = true, Transform parent = null)
        {
            Transform t = Child(parent != null ? parent : stage, name);
            var a = t.gameObject.AddComponent<CineAttach>();
            a.Configure(target, anchor, matchYaw, follow);
            if (face != CineAttach.Target.None)
                a.ConfigureFacing(face, faceAnchor);
            return t;
        }

        /// <summary>샷 카메라 (holder(Animator)/Cam(CinemachineCamera)). look 는 parent 로컬 좌표.</summary>
        public CinemachineCamera Cam(string name, Transform parent, Vector3 pos, Vector3 look, float fov, bool noise = true, Transform lookTarget = null)
        {
            Transform holder = Child(parent, "CamHolder_" + name);
            holder.gameObject.AddComponent<Animator>();
            Transform camT = Child(holder, "Cam");
            var cam = camT.gameObject.AddComponent<CinemachineCamera>();
            camT.localPosition = pos;
            Vector3 dir = look - pos;
            camT.localRotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
            LensSettings lens = LensSettings.Default;
            lens.FieldOfView = fov;
            lens.NearClipPlane = 0.05f;
            lens.FarClipPlane = 1200f;
            cam.Lens = lens;
            cam.Priority = -10;
            if (lookTarget != null)
            {
                camT.gameObject.AddComponent<CinemachineHardLookAt>();
                cam.Target.CustomLookAtTarget = true;
                cam.Target.LookAtTarget = lookTarget;
            }
            if (noise)
            {
                var perlin = camT.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
                perlin.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoisePath);
                perlin.AmplitudeGain = 0.3f;
                perlin.FrequencyGain = 0.55f;
            }
            return cam;
        }

        public void Shot(CinemachineCamera cam, double start, double dur, string label)
        {
            TimelineClip clip = _camTrack.CreateClip<CinemachineShot>();
            clip.start = start;
            clip.duration = dur;
            clip.displayName = label;
            var shot = (CinemachineShot)clip.asset;
            var exposed = new PropertyName($"{id}.{label}");
            shot.DisplayName = label;
            shot.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = exposed };
            director.SetReferenceValue(exposed, cam);
        }

        /// <summary>카메라 이동 (위치·시선·FOV, 스무스). 좌표는 holder 부모 로컬.</summary>
        public void Move(CinemachineCamera cam, double start, double dur, Vector3 p0, Vector3 l0, float f0, Vector3 p1, Vector3 l1, float f1, bool ease = true)
        {
            var clip = new AnimationClip { name = $"AC_{id}_{cam.transform.parent.name}_{_clipSerial++}", frameRate = 60f };
            float d = (float)dur;
            Quaternion q0 = Quaternion.LookRotation((l0 - p0).normalized, Vector3.up);
            Quaternion q1 = Quaternion.LookRotation((l1 - p1).normalized, Vector3.up);
            SetCurve(clip, "Cam", typeof(Transform), "m_LocalPosition.x", p0.x, p1.x, d, ease);
            SetCurve(clip, "Cam", typeof(Transform), "m_LocalPosition.y", p0.y, p1.y, d, ease);
            SetCurve(clip, "Cam", typeof(Transform), "m_LocalPosition.z", p0.z, p1.z, d, ease);
            Quaternion qm = Quaternion.Slerp(q0, q1, 0.5f);
            SetCurve3(clip, "Cam", typeof(Transform), "m_LocalRotation.x", q0.x, qm.x, q1.x, d, ease);
            SetCurve3(clip, "Cam", typeof(Transform), "m_LocalRotation.y", q0.y, qm.y, q1.y, d, ease);
            SetCurve3(clip, "Cam", typeof(Transform), "m_LocalRotation.z", q0.z, qm.z, q1.z, d, ease);
            SetCurve3(clip, "Cam", typeof(Transform), "m_LocalRotation.w", q0.w, qm.w, q1.w, d, ease);
            SetCurve(clip, "Cam", typeof(CinemachineCamera), "Lens.FieldOfView", f0, f1, d, ease);
            clip.EnsureQuaternionContinuity();
            AssetDatabase.AddObjectToAsset(clip, timeline);
            Animator holder = cam.transform.parent.GetComponent<Animator>();
            AnimationTrack track = AnimTrack(holder, "TRK_Cam_" + cam.transform.parent.name.Replace("CamHolder_", ""), false);
            TimelineClip tc = track.CreateClip(clip);
            tc.start = start;
            tc.duration = dur;
            cam.transform.localPosition = p0;
            cam.transform.localRotation = q0;
            LensSettings lens = cam.Lens; lens.FieldOfView = f0; cam.Lens = lens;
        }

        public AnimationTrack AnimTrack(Animator animator, string name, bool sceneOffsets)
        {
            if (_animTracks.TryGetValue(animator, out AnimationTrack t))
                return t;
            t = timeline.CreateTrack<AnimationTrack>(null, name);
            t.trackOffset = sceneOffsets ? TrackOffset.ApplySceneOffsets : TrackOffset.ApplyTransformOffsets;
            director.SetGenericBinding(t, animator);
            _animTracks[animator] = t;
            return t;
        }

        /// <summary>휴머노이드 퍼펫 클립. holdEnd = 마지막 프레임 자세 유지 (쓰러진 레지스탕스).</summary>
        public void Act(Animator puppet, AnimationClip clip, double start, double dur, bool holdEnd = false, double clipIn = 0d)
        {
            if (clip == null) { Log.Append($"missing clip for {puppet.name}; "); return; }
            AnimationTrack track = AnimTrack(puppet, "TRK_Act_" + puppet.transform.parent.name, true);
            TimelineClip tc = track.CreateClip(clip);
            tc.start = start;
            tc.duration = dur;
            tc.clipIn = holdEnd ? System.Math.Max(0d, clip.length - 0.04d) : clipIn;
            if (holdEnd)
                tc.timeScale = 0.0001d;
        }

        /// <summary>임의 오브젝트 키 애니메이션 (holder Animator 아래 path 대상).</summary>
        public void Keys(Animator holder, string trackName, double start, double dur, System.Action<AnimationClip> fill)
        {
            var clip = new AnimationClip { name = $"AC_{id}_{trackName}_{_clipSerial++}", frameRate = 60f };
            fill(clip);
            clip.EnsureQuaternionContinuity();
            AssetDatabase.AddObjectToAsset(clip, timeline);
            AnimationTrack track = AnimTrack(holder, trackName, false);
            TimelineClip tc = track.CreateClip(clip);
            tc.start = start;
            tc.duration = dur;
        }

        public void Line(double start, double dur, string key, string speaker, string fallback)
        {
            TimelineClip c = _subTrack.CreateClip<CineSubtitleClip>();
            c.start = start; c.duration = dur; c.displayName = key;
            var a = (CineSubtitleClip)c.asset;
            a.mode = CineSubtitleClip.Mode.Line; a.key = key; a.speaker = speaker; a.text = fallback;
        }

        public void Card(double start, double dur, string key, string title, string sub)
        {
            TimelineClip c = _subTrack.CreateClip<CineSubtitleClip>();
            c.start = start; c.duration = dur; c.displayName = "CARD " + title;
            var a = (CineSubtitleClip)c.asset;
            a.mode = CineSubtitleClip.Mode.Card; a.key = key; a.speaker = ""; a.text = title; a.cardSub = sub;
        }

        public void Fade(double start, double dur, float from, float to, Color? color = null, bool flash = false)
        {
            TimelineClip c = _fadeTrack.CreateClip<CineFadeClip>();
            c.start = start; c.duration = dur; c.displayName = $"Fade {from:0.#}->{to:0.#}";
            var a = (CineFadeClip)c.asset;
            a.fromAlpha = from; a.toAlpha = to; a.color = color ?? Color.black; a.isFlash = flash;
        }

        public void Sound(string wav, double start, double maxDur = 0d)
        {
            var clipAsset = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{wav}.wav");
            if (clipAsset == null) { Log.Append($"missing audio {wav}; "); return; }
            // 겹치는 소리는 같은 트랙에서 블렌드되므로 겹치면 보조 트랙을 쓴다
            AudioTrack track = _audioTrack;
            foreach (TimelineClip existing in track.GetClips())
                if (existing.start < start + clipAsset.length && start < existing.end)
                {
                    track = AltAudio();
                    break;
                }
            TimelineClip tc = track.CreateClip(clipAsset);
            tc.start = start;
            tc.duration = maxDur > 0d ? System.Math.Min(maxDur, clipAsset.length) : clipAsset.length;
        }

        AudioTrack _alt;
        AudioTrack AltAudio()
        {
            if (_alt == null) _alt = timeline.CreateTrack<AudioTrack>(null, "TRK_Audio_B");
            return _alt;
        }

        public void LightBeat(Light light, double start, double dur, float intensity, float flicker = 0f, float hz = 9f, Color? color = null)
        {
            if (!_otherTracks.TryGetValue(light, out TrackAsset t))
            {
                t = timeline.CreateTrack<CineLightTrack>(null, "TRK_Light_" + light.name);
                director.SetGenericBinding(t, light);
                _otherTracks[light] = t;
            }
            TimelineClip c = t.CreateClip<CineLightClip>();
            c.start = start; c.duration = dur;
            var a = (CineLightClip)c.asset;
            a.intensity = intensity; a.flicker = flicker; a.flickerHz = hz; a.seed = Random.Range(0f, 10f);
            if (color.HasValue) { a.overrideColor = true; a.color = color.Value; }
        }

        public void Text(TMP_Text text, double start, double dur, string value, float blinkHz = 0f, Color? color = null)
        {
            if (!_otherTracks.TryGetValue(text, out TrackAsset t))
            {
                t = timeline.CreateTrack<CineTextTrack>(null, "TRK_Text_" + text.name);
                director.SetGenericBinding(t, text);
                _otherTracks[text] = t;
            }
            TimelineClip c = t.CreateClip<CineTextClip>();
            c.start = start; c.duration = dur; c.displayName = value.Replace("\n", " ");
            var a = (CineTextClip)c.asset;
            a.text = value; a.blinkHz = blinkHz;
            if (color.HasValue) { a.overrideColor = true; a.color = color.Value; }
        }

        public void Active(GameObject go, double start, double dur)
        {
            if (!_otherTracks.TryGetValue(go, out TrackAsset t))
            {
                t = timeline.CreateTrack<ActivationTrack>(null, "TRK_Active_" + go.name);
                ((ActivationTrack)t).postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
                director.SetGenericBinding(t, go);
                _otherTracks[go] = t;
            }
            TimelineClip c = t.CreateDefaultClip();
            c.start = start; c.duration = dur;
        }

        public void Save(double duration)
        {
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = duration;
            EditorUtility.SetDirty(timeline);
            string path = $"{PrefabDir}/{root.name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Log.Append($"{root.name} {(ok ? "ok" : "FAILED")} {duration:0.#}s; ");
            Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------ scene object helpers

    static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        else SceneManager.MoveGameObjectToScene(go, s_stage);
        return go.transform;
    }

    static GameObject Model(string path, Transform parent, Vector3 pos, float yaw, float scale = 1f, string rename = null)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) { Log.Append($"MISSING {path}; "); return Child(parent, Path.GetFileNameWithoutExtension(path) + "_MISSING").gameObject; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.name = rename ?? asset.name;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbesAndSkybox;
        return go;
    }

    static GameObject Cine(string name, Transform parent, Vector3 pos, float yaw, float scale = 1f) => Model($"{MeshDir}/SM_CINE_{name}.fbx", parent, pos, yaw, scale, name);
    static GameObject Inf(string name, Transform parent, Vector3 pos, float yaw, float scale = 1f) => Model($"{InfMeshDir}/SM_INF_{name}.fbx", parent, pos, yaw, scale, name);

    static GameObject Quad(string name, Transform parent, Vector3 worldPos, float yaw, Vector2 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        SceneManager.MoveGameObjectToScene(go, s_stage);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static GameObject Beam(string name, Transform parent, Vector3 from, Vector3 to, float width, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(go, s_stage);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (from + to) * 0.5f;
        go.transform.localRotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
        go.transform.localScale = new Vector3(width, width, (to - from).magnitude);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static GameObject Plane(string name, Transform parent, Vector3 pos, float size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        SceneManager.MoveGameObjectToScene(go, s_stage);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Light MakeLight(string name, Transform parent, LightType type, Vector3 pos, Vector3 lookAt, Color color, float intensity, float range, float spot = 40f, bool shadows = false)
    {
        Transform t = Child(parent, name);
        t.localPosition = pos;
        Vector3 dir = lookAt - pos;
        if (dir.sqrMagnitude > 0.001f) t.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        var l = t.gameObject.AddComponent<Light>();
        l.type = type; l.color = color; l.intensity = intensity; l.range = range; l.spotAngle = spot;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        return l;
    }

    static TMP_Text WorldText(string name, Transform parent, Vector3 localPos, float yaw, Vector2 box, string value, Color color)
    {
        Transform t = Child(parent, name);
        t.localPosition = localPos;
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var tmp = t.gameObject.AddComponent<TextMeshPro>();
        tmp.text = value;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 3f;
        tmp.enableWordWrapping = true;
        tmp.fontStyle = FontStyles.Bold;
        tmp.rectTransform.sizeDelta = box;
        return tmp;
    }

    /// <summary>휴머노이드 퍼펫 (컨트롤러 없음, 타임라인이 구동).</summary>
    static Animator Puppet(string path, Transform parent, string name, Vector3 pos, float yaw)
    {
        Transform holder = Child(parent, name);
        holder.localPosition = pos;
        holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
        GameObject go = Model(path, holder, Vector3.zero, 0f, 1f, name + "_Model");
        Animator a = go.GetComponentInChildren<Animator>(true);
        if (a == null) a = go.AddComponent<Animator>();
        a.runtimeAnimatorController = null;
        a.applyRootMotion = true;
        a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && !(mb is PlayerVisualRig)) Object.DestroyImmediate(mb);
        return a;
    }

    static Animator Android(Transform parent, string name, Vector3 pos, float yaw)
    {
        Animator a = Puppet(AndroidModel, parent, name, pos, yaw);
        var mob = AssetDatabase.LoadAssetAtPath<GameObject>(AndroidPrefab);
        if (mob != null)
        {
            var src = mob.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer dst in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                SkinnedMeshRenderer match = src.FirstOrDefault(s => s.name == dst.name) ?? src.FirstOrDefault();
                if (match != null) dst.sharedMaterials = match.sharedMaterials;
            }
        }
        return a;
    }

    static AnimationClip Clip(string folder, string token)
    {
        string dir = $"{GS}/{folder}";
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetDirectoryName(p).Replace('\\', '/') != dir) continue;
            if (!Path.GetFileName(p).Contains(token)) continue;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(p))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                    return c;
        }
        Log.Append($"clip? {folder}/{token}; ");
        return null;
    }

    static void SetCurve(AnimationClip clip, string path, System.Type type, string prop, float a, float b, float d, bool ease)
    {
        AnimationCurve c = ease ? AnimationCurve.EaseInOut(0f, a, d, b) : AnimationCurve.Linear(0f, a, d, b);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, prop), c);
    }

    static void SetCurve3(AnimationClip clip, string path, System.Type type, string prop, float a, float m, float b, float d, bool ease)
    {
        float tm = d * 0.5f;
        float mm = ease ? m : m;
        var c = new AnimationCurve(new Keyframe(0f, a, 0f, 0f), new Keyframe(tm, mm), new Keyframe(d, b, 0f, 0f));
        if (!ease)
        {
            c = new AnimationCurve(new Keyframe(0f, a), new Keyframe(tm, mm), new Keyframe(d, b));
            for (int i = 0; i < c.length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear); }
        }
        else
        {
            AnimationUtility.SetKeyLeftTangentMode(c, 1, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(c, 1, AnimationUtility.TangentMode.ClampedAuto);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, prop), c);
    }

    static void Steps(AnimationClip clip, string path, System.Type type, string prop, params (float t, float v)[] keys)
    {
        var c = new AnimationCurve(keys.Select(k => new Keyframe(k.t, k.v)).ToArray());
        for (int i = 0; i < c.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, prop), c);
    }

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    // ------------------------------------------------------------------ 1. 오프닝 (Level_Facility, 무대 프리팹)

    static void BuildOpening()
    {
        var rig = new Rig(IdOpening, "CINE_Opening", "Level_Facility");
        rig.sequence.Configure(IdOpening, "Level_Facility", true, CinematicSettings.SeenScope.Run, true, false, false, true);
        Transform st = rig.stage;

        // --- 은신처 (밤, 습격 직후)
        Transform hide = Child(st, "Hideout");
        Cine("HideoutShell", hide, V(0, 0, 0), 0);
        Cine("MapTable", hide, V(1.6f, 0, 0.6f), 20);
        GameObject bank = Cine("MonitorBank", hide, V(3.6f, 0, -2.4f), 0);
        Cine("CrateStack", hide, V(-4.6f, 0, -3.0f), 12);
        Cine("Cot", hide, V(-4.5f, 0, 2.6f), 90);
        Cine("Barricade", hide, V(-1.6f, 0, -2.9f), 180);
        Cine("Rubble", hide, V(-2.2f, 0, -3.9f), 0);
        Cine("Rubble", hide, V(3.1f, 0, -3.7f), 70);
        Cine("Banner", hide, V(-5.75f, 0, 0.2f), 90);
        // 가운데 화면: 어두운 레드 발광 판 + 문구 (화면 앞면 z ≈ -0.059, forward +Z)
        GameObject screen = Quad("ScreenGlow", bank.transform, Vector3.zero, 0, V(0.58f, 0.34f, 0), M["Screen"]);
        screen.transform.localPosition = V(0, 1.2f, -0.052f);
        screen.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        TMP_Text monitor = WorldText("MonitorText", bank.transform, V(0, 1.2f, -0.046f), 180, V(0.54f, 0.3f, 0), "SECURE CHANNEL\nBREACHED", new Color(1f, 0.16f, 0.12f));
        // 츠카사 진압조 조준선 (파손된 출입구 → 방 안)
        Transform beams = Child(hide, "LaserSights");
        Beam("Laser1", beams, V(-2.0f, 1.45f, -5.3f), V(-0.4f, 0.25f, 1.1f), 0.012f, M["Laser"]);
        Beam("Laser2", beams, V(-2.6f, 1.5f, -5.6f), V(1.6f, 0.9f, -0.9f), 0.012f, M["Laser"]);
        Beam("Laser3", beams, V(-2.9f, 1.4f, -5.8f), V(-3.2f, 0.25f, 1.1f), 0.012f, M["Laser"]);
        Light red = MakeLight("EmergencyRed", hide, LightType.Point, V(0.5f, 3.5f, 0f), V(0.5f, 0f, 0f), new Color(1f, 0.12f, 0.08f), 6f, 12f);
        MakeLight("DoorSpot", hide, LightType.Spot, V(-2.3f, 2.2f, -6.0f), V(-0.5f, 0f, 1.5f), new Color(0.82f, 0.86f, 1f), 26f, 16f, 42f, true);
        MakeLight("MonitorGlow", hide, LightType.Point, V(3.6f, 2.1f, -1.2f), V(3.6f, 1.3f, -2.4f), new Color(1f, 0.3f, 0.25f), 0.8f, 3.5f);

        Animator chuon = Puppet(ChuonVisual, hide, "Chuon_Hideout", V(0.4f, 0, 1.2f), 200);
        Animator r1 = Puppet(GhostModel, hide, "Resistance_A", V(-1.0f, 0, -1.0f), 30);
        Animator r2 = Puppet(GhostModel, hide, "Resistance_B", V(2.4f, 0, -0.4f), -60);
        Animator r3 = Puppet(GhostModel, hide, "Resistance_C", V(-3.2f, 0, 1.2f), 140);
        Animator a1 = Android(hide, "Tsukasa_Android_A", V(-1.9f, 0, -5.4f), 0);
        Animator a2 = Android(hide, "Tsukasa_Android_B", V(-2.9f, 0, -5.9f), 10);

        // --- 츠카사 본사 앞 (새벽)
        Transform ext = Child(st, "Exterior");
        ext.localPosition = V(0, 0, 600);
        var env = ext.gameObject.AddComponent<CineRenderSettingsOverride>();
        env.Configure(M.TryGetValue("Sky", out Material sky) ? sky : null, 0.0016f, new Color(0.42f, 0.38f, 0.4f));
        Plane("Ground", ext, V(0, 0, 100), 700f, M.TryGetValue("Asphalt", out Material asphalt) ? asphalt : M["Concrete"]);
        Inf("HQPodium", ext, V(0, 0, 70), 180);
        GameObject tower = Inf("HQTower", ext, V(0, 0, 95), 180);
        Transform ea = tower.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "EmblemAnchor");
        if (ea != null && M.TryGetValue("Emblem", out Material emblem))
            Quad("TowerEmblemCrown", ext, ea.position + V(0, 0, -0.1f), 180, V(14, 14, 0), emblem);
        Inf("BlockA", ext, V(-45, 0, 40), 90);
        Inf("BlockC", ext, V(48, 0, 30), -90);
        Inf("BlockB", ext, V(-32, 0, -10), 90);
        Inf("BlockD", ext, V(34, 0, -15), -90);
        Inf("BlockC", ext, V(-62, 0, 115), 180);
        Inf("BlockA", ext, V(66, 0, 125), 180);
        for (int i = 0; i < 4; i++)
        {
            Inf("StreetLight", ext, V(-9, 0, -10 + i * 20), 90);
            Inf("StreetLight", ext, V(9, 0, -10 + i * 20), -90);
        }
        Inf("Jersey", ext, V(-4, 0, 38), 0);
        Inf("Jersey", ext, V(4, 0, 38), 0);
        Inf("Bollard", ext, V(-1.5f, 0, 41), 0);
        Inf("Bollard", ext, V(1.5f, 0, 41), 0);
        var skyNear = AssetDatabase.LoadAssetAtPath<Mesh>($"{InfMeshDir}/Generated/SM_INF_SkylineNear.asset");
        if (skyNear != null && M.TryGetValue("Skyline", out Material skyline))
        {
            Transform ring = Child(ext, "SkylineNear");
            ring.localPosition = V(0, 0, 150);
            ring.gameObject.AddComponent<MeshFilter>().sharedMesh = skyNear;
            var mr = ring.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = skyline; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Light dawn = MakeLight("DawnKey", ext, LightType.Directional, V(0, 50, 0), V(-0.3f, 50 - 0.21f, -1f), new Color(1f, 0.78f, 0.62f), 0.95f, 10f, 40f, true);
        dawn.transform.localRotation = Quaternion.Euler(12f, 200f, 0f);
        Animator chuonExt = Puppet(ChuonVisual, ext, "Chuon_Exterior", V(0, 0, 0), 0);

        // --- 샷 (stage 로컬 = 리그 루트 로컬; Exterior 는 z+600)
        Transform cams = rig.cams;
        Vector3 E = V(0, 0, 600);
        var s1 = rig.Cam("S1_Establish", cams, V(0.8f, 1.2f, 3.6f), V(-2.3f, 1.3f, -4.6f), 38f);
        rig.Move(s1, 2.8, 3.8, V(0.8f, 1.2f, 3.6f), V(-2.3f, 1.3f, -4.6f), 38f, V(0.5f, 1.1f, 2.9f), V(-2.1f, 1.2f, -4.6f), 36f);
        var s2 = rig.Cam("S2_MonitorInsert", cams, V(3.78f, 1.3f, -1.15f), V(3.6f, 1.2f, -2.45f), 26f);
        rig.Move(s2, 6.6, 2.4, V(3.78f, 1.3f, -1.15f), V(3.6f, 1.2f, -2.45f), 26f, V(3.7f, 1.26f, -1.4f), V(3.6f, 1.2f, -2.45f), 24f);
        // 반복 14C: 엎드린 추온의 머리·어깨 쪽 하이앵글 (Falldown 머리 ≈ (0.21,0.13,0.63), 골반 ≈ (0.36,0.13,1.08)). 뒤쪽 하이앵글은 골반만 잡혀 교체.
        var s3 = rig.Cam("S3_ChuonDown", cams, V(-0.8f, 2.0f, -0.2f), V(0.25f, 0.22f, 0.78f), 30f);
        rig.Move(s3, 9.0, 3.4, V(-0.8f, 2.0f, -0.2f), V(0.25f, 0.22f, 0.78f), 30f, V(-0.6f, 1.75f, -0.02f), V(0.25f, 0.2f, 0.75f), 28f);
        // 반복 14C: 정면 측면 로우앵글 (뒤쪽 로우앵글은 골반 중심 구도라 교체)
        var s4 = rig.Cam("S4_GetUp", cams, V(0.75f, 0.5f, -0.85f), V(0.3f, 0.8f, 0.8f), 34f, false);
        rig.Move(s4, 12.4, 3.8, V(0.75f, 0.5f, -0.85f), V(0.3f, 0.8f, 0.8f), 34f, V(0.7f, 0.55f, -0.9f), V(0.4f, 1.45f, 1.2f), 32f);
        var s5 = rig.Cam("S5_Fallen", cams, V(-0.6f, 1.6f, 2.8f), V(-1.0f, 0.3f, -1.0f), 32f);
        rig.Move(s5, 16.2, 3.4, V(-0.6f, 1.6f, 2.8f), V(-1.0f, 0.3f, -1.0f), 32f, V(-0.7f, 1.6f, 2.8f), V(-5.75f, 1.8f, 0.2f), 32f);
        var s6 = rig.Cam("S6_LookUp", cams, E + V(0.75f, 0.45f, -2.2f), E + V(0, 1.4f, 0), 40f, false);
        rig.Move(s6, 19.6, 5.4, E + V(0.75f, 0.45f, -2.2f), E + V(0, 1.4f, 0), 40f, E + V(0.6f, 0.35f, -2.5f), E + V(0, 125f, 95f), 34f);
        var s7 = rig.Cam("S7_Approach", cams, E + V(-1.2f, 1.6f, -5.0f), E + V(0, 8f, 40f), 40f);

        rig.Shot(s1, 2.8, 3.8, "S1_Establish");
        rig.Shot(s2, 6.6, 2.4, "S2_MonitorInsert");
        rig.Shot(s3, 9.0, 3.4, "S3_ChuonDown");
        rig.Shot(s4, 12.4, 3.8, "S4_GetUp");
        rig.Shot(s5, 16.2, 3.4, "S5_Fallen");
        rig.Shot(s6, 19.6, 5.4, "S6_LookUp");
        rig.Shot(s7, 25.0, 5.0, "S7_Approach");

        rig.Active(hide.gameObject, 0, 19.6);
        rig.Active(ext.gameObject, 19.6, 10.4);
        rig.Fade(0, 2.8, 1, 1);
        rig.Fade(2.8, 0.8, 1, 0);
        rig.Fade(18.6, 1.0, 0, 1);
        rig.Fade(19.6, 1.4, 1, 0);
        rig.Fade(29.2, 0.8, 0, 1);
        rig.Card(0.3, 2.4, "cine.card.night", "그날 밤", "레지스탕스 은신처");
        rig.Card(19.8, 2.8, "cine.card.dawn", "새벽", "츠카사 본사 앞");
        rig.Line(3.2, 3.2, "cine.open.0", "System", "거점 제압 완료. 생존자 없음.\n회수 대상… 확인 불가.");
        rig.Line(9.4, 2.8, "cine.open.1", "EGO", "추온. …추온, 눈 떠.");
        rig.Line(13.8, 2.0, "cine.open.2", "추온", "…다들.");
        rig.Line(16.3, 2.6, "cine.open.3", "EGO", "정보가 샜어.\n은신처도, 내일 경로도 전부.");
        rig.Line(22.8, 2.6, "cine.open.4", "EGO", "경로는 이미 털렸어.\n계획대로면 함정 한가운데야.");
        rig.Line(25.6, 1.9, "cine.open.5", "추온", "상관없어. 정면으로 간다.");
        rig.Line(27.6, 2.2, "cine.open.6", "EGO", "그 말 기다렸어. 가자, 추온.");
        rig.Text(monitor, 2.8, 4.4, "SECURE CHANNEL\nBREACHED", 0f);
        rig.Text(monitor, 7.2, 12.4, "HIDEOUT LOCATION\nLEAKED", 2.2f);
        rig.LightBeat(red, 2.8, 16.2, 1f, 0.55f, 2.6f);
        rig.Sound("SFX_Cine_RaidAlarm", 0.2);
        rig.Sound("SFX_Cine_RaidAlarm", 3.6);
        rig.Sound("MUS_Cine_Flashback", 4.0);
        rig.Sound("SFX_Cine_LowImpact", 18.9);
        rig.Sound("MUS_Cine_DawnSwell", 20.0);

        AnimationClip lie = Clip("APose/Hit/Inplace", "Falldown_F_Loop");
        AnimationClip getUp = Clip("APose/Hit/Inplace", "Falldown_F_End");
        AnimationClip idle = Clip("Common/Inplace", "Common_Idle");
        AnimationClip walk = Clip("Common/Root", "Common_Walk_Loop");
        rig.Act(chuon, lie, 0, 12.6);
        if (getUp != null)
        {
            rig.Act(chuon, getUp, 12.6, getUp.length);
            rig.Act(chuon, idle, 12.6 + getUp.length, System.Math.Max(0.5, 19.6 - 12.6 - getUp.length));
        }
        rig.Act(r1, Clip("APose/Die/Inplace", "Die01"), 0, 19.6, true);
        rig.Act(r2, Clip("APose/Die/Inplace", "Die04"), 0, 19.6, true);
        rig.Act(r3, Clip("APose/Die/Inplace", "Die07"), 0, 19.6, true);
        rig.Act(a1, idle, 0, 19.6);
        rig.Act(a2, idle, 0, 19.6);
        rig.Act(chuonExt, idle, 19.6, 6.0);
        rig.Act(chuonExt, walk, 25.6, 4.4);
        ext.gameObject.SetActive(false); // 활성화 트랙이 19.6 s 에 켠다 (하늘·안개 덮어쓰기 포함)
        rig.Save(30.0);
    }

    // ------------------------------------------------------------------ 2. 튜토리얼 VR 부팅 / 종료 (Tutorial)

    static void BuildTutBoot()
    {
        var rig = new Rig(IdTutBoot, "CINE_TutVrBoot", "Tutorial");
        rig.sequence.Configure(IdTutBoot, "Tutorial", true, CinematicSettings.SeenScope.Run, true, false, false, true);
        Transform atAnchor = rig.Sub("AtStart", CineAttach.Target.Anchor, "CineAnchor_TutStart");
        Transform atPlayer = rig.Sub("AtPlayer", CineAttach.Target.Player);
        Transform head = rig.Sub("PlayerHead", CineAttach.Target.PlayerHead, follow: true, matchYaw: false);
        var s1 = rig.Cam("S1_City", atAnchor, V(2.5f, 6f, -7f), V(0, 30f, 120f), 40f);
        rig.Move(s1, 0, 2.6, V(2.5f, 6f, -7f), V(0, 30f, 120f), 40f, V(1.6f, 2.2f, -4.5f), V(0, 1.4f, 4f), 44f);
        var s2 = rig.Cam("S2_Sync", atPlayer, V(0.45f, 1.6f, 0.6f), V(0, 1.5f, 0), 26f, true, head);
        var s3 = rig.Cam("S3_Behind", atPlayer, V(0.25f, 1.75f, -3.4f), V(0, 1.45f, 5f), 50f, false);
        rig.Shot(s1, 0, 2.6, "S1_City");
        rig.Shot(s2, 2.6, 1.8, "S2_Sync");
        rig.Shot(s3, 4.4, 2.1, "S3_Behind");
        rig.Fade(0, 0.6, 1, 0);
        rig.Fade(2.6, 0.35, 0.35f, 0, Color.white, true);
        rig.Sound("SFX_Cine_VrBoot", 0.0);
        rig.Save(6.5);
    }

    static void BuildTutExit()
    {
        var rig = new Rig(IdTutExit, "CINE_TutVrExit", "Tutorial");
        rig.sequence.Configure(IdTutExit, "Tutorial", true, CinematicSettings.SeenScope.None, true, false, false, true);
        Transform atDoor = rig.Sub("AtDoor", CineAttach.Target.Anchor, "CineAnchor_TutExitDoor");
        Transform atPlayer = rig.Sub("AtPlayer", CineAttach.Target.Player, face: CineAttach.Target.Anchor, faceAnchor: "CineAnchor_TutExitDoor");
        Transform head = rig.Sub("PlayerHead", CineAttach.Target.PlayerHead, follow: true, matchYaw: false);
        // 14C: 문 앞 와이드에 추온이 함께 들어오게 뒤로, 패널 클로즈는 문 전체가 읽히는 미디엄으로
        var s1 = rig.Cam("S1_Door", atDoor, V(-1.4f, 2.2f, -5.5f), V(0, 1.5f, 0.5f), 42f);
        var s2 = rig.Cam("S2_Panel", atDoor, V(0.9f, 1.6f, -2.2f), V(0, 1.4f, 0), 34f);
        var s3 = rig.Cam("S3_Chuon", atPlayer, V(0.6f, 1.62f, 1.3f), V(0, 1.55f, 0), 30f, true, head);
        rig.Shot(s1, 0, 1.8, "S1_Door");
        rig.Shot(s2, 1.8, 1.6, "S2_Panel");
        rig.Shot(s3, 3.4, 1.6, "S3_Chuon");
        rig.Fade(2.2, 0.4, 0.3f, 0, Color.white, true);
        rig.Fade(4.2, 0.8, 0, 1, Color.white);
        rig.Card(3.5, 1.5, null, "SIMULATION TERMINATED", "VR LINK RELEASED  //  현실 복귀");
        rig.Sound("SFX_Cine_VrRelease", 3.4);
        rig.Save(5.0);
    }

    // ------------------------------------------------------------------ 3. 로비 경보 + 드론 리빌 (Level_Facility)

    static void BuildLobbyAlarm()
    {
        var rig = new Rig(IdLobbyAlarm, "CINE_LobbyAlarm", "Level_Facility");
        rig.sequence.Configure(IdLobbyAlarm, "Level_Facility", true, CinematicSettings.SeenScope.Run, true, false, false, true);
        Transform desk = rig.Sub("AtFrontDesk", CineAttach.Target.Anchor, "CineAnchor_LobbyFrontDesk");
        Transform atPlayer = rig.Sub("AtPlayer", CineAttach.Target.Player, face: CineAttach.Target.Anchor, faceAnchor: "CineAnchor_LobbyFrontDesk");
        Transform head = rig.Sub("PlayerHead", CineAttach.Target.PlayerHead, follow: true, matchYaw: false);

        Cine("DroneHatch", desk, V(0, 0.01f, -3.0f), 0);
        Transform leaves = Child(desk, "HatchLeaves");
        leaves.gameObject.AddComponent<Animator>();
        GameObject leafL = Cine("DroneHatchLeaf", leaves, V(-1.2f, 0.01f, -3.0f), 0); leafL.name = "LeafL";
        GameObject leafR = Cine("DroneHatchLeaf", leaves, V(1.2f, 0.01f, -3.0f), 180); leafR.name = "LeafR";
        Transform drones = Child(desk, "DroneFlight");
        var droneAnim = drones.gameObject.AddComponent<Animator>();
        Vector3[] starts = { V(-0.6f, -1.2f, -3.0f), V(0, -1.3f, -3.0f), V(0.6f, -1.2f, -3.0f) };
        Vector3[] mids = { V(-0.8f, 2.4f, -3.2f), V(0, 2.8f, -3.3f), V(0.8f, 2.4f, -3.2f) };
        Vector3[] ends = { V(-5f, 7.5f, -7f), V(0.5f, 8.5f, -9f), V(5f, 7.5f, -7f) };
        for (int i = 0; i < 3; i++) { GameObject d = Inf("Drone", drones, starts[i], 0); d.name = "Drone" + (i + 1); }
        Light alarm = MakeLight("AlarmRed", desk, LightType.Spot, V(0, 6.5f, 0.8f), V(0, 0, -3f), new Color(1f, 0.1f, 0.08f), 18f, 22f, 70f); // 반복 14C: 해치를 비춘다

        var s1 = rig.Cam("S1_LobbyWide", desk, V(-8.5f, 6.2f, 9.5f), V(0, 1.2f, 0), 40f);
        var s2 = rig.Cam("S2_ChuonReact", atPlayer, V(-0.55f, 1.65f, 1.5f), V(0, 1.55f, 0), 30f, true, head);
        var s3 = rig.Cam("S3_HatchReveal", desk, V(2.0f, 0.55f, -0.9f), V(0, 1.0f, -3.0f), 36f, false);
        rig.Move(s3, 4.2, 3.0, V(2.0f, 0.55f, -0.9f), V(0, 1.0f, -3.0f), 36f, V(2.1f, 0.6f, -0.8f), V(0, 4.2f, -3.8f), 40f);
        var s4 = rig.Cam("S4_Return", atPlayer, V(0.5f, 1.75f, -2.8f), V(0, 1.4f, 4f), 48f, false);
        rig.Shot(s1, 0, 2.4, "S1_LobbyWide");
        rig.Shot(s2, 2.4, 1.8, "S2_ChuonReact");
        rig.Shot(s3, 4.2, 3.0, "S3_HatchReveal");
        rig.Shot(s4, 7.2, 2.3, "S4_Return");

        rig.Keys(leaves.GetComponent<Animator>(), "TRK_HatchLeaves", 4.2, 3.0, clip =>
        {
            foreach (var (path, yaw) in new[] { ("LeafL", 0f), ("LeafR", 180f) })
            {
                Quaternion closed = Quaternion.Euler(0, yaw, 0), open = Quaternion.Euler(0, yaw, 100f);
                Steps(clip, path, typeof(Transform), "m_LocalRotation.x", (0f, closed.x), (0.8f, open.x), (2.4f, open.x), (3.0f, closed.x));
                Steps(clip, path, typeof(Transform), "m_LocalRotation.y", (0f, closed.y), (0.8f, open.y), (2.4f, open.y), (3.0f, closed.y));
                Steps(clip, path, typeof(Transform), "m_LocalRotation.z", (0f, closed.z), (0.8f, open.z), (2.4f, open.z), (3.0f, closed.z));
                Steps(clip, path, typeof(Transform), "m_LocalRotation.w", (0f, closed.w), (0.8f, open.w), (2.4f, open.w), (3.0f, closed.w));
            }
        });
        rig.Keys(droneAnim, "TRK_DroneFlight", 4.2, 3.2, clip =>
        {
            for (int i = 0; i < 3; i++)
            {
                string p = "Drone" + (i + 1);
                float lag = i * 0.18f;
                Steps(clip, p, typeof(Transform), "m_LocalPosition.x", (0f, starts[i].x), (0.6f + lag, starts[i].x), (1.5f + lag, mids[i].x), (3.2f, ends[i].x));
                Steps(clip, p, typeof(Transform), "m_LocalPosition.y", (0f, starts[i].y), (0.6f + lag, starts[i].y), (1.5f + lag, mids[i].y), (3.2f, ends[i].y));
                Steps(clip, p, typeof(Transform), "m_LocalPosition.z", (0f, starts[i].z), (0.6f + lag, starts[i].z), (1.5f + lag, mids[i].z), (3.2f, ends[i].z));
            }
        });
        rig.Active(drones.gameObject, 4.2, 3.2);
        rig.LightBeat(alarm, 0, 9.5, 1f, 0.7f, 1.6f);
        rig.Line(0.2, 2.3, "lobby.alarm.0", "System", "침입자 감지.\n방어 프로토콜을 가동합니다.");
        rig.Line(2.5, 1.8, "lobby.alarm.1", "System", "임직원 여러분은\n업무를 계속해 주십시오.");
        rig.Line(4.4, 2.7, "lobby.drones.0", "EGO", "프론트 데스크 뒤에서 드론이 나와.\n손님 접대 한번 거칠다.");
        rig.Line(7.3, 1.9, "lobby.drones.1", "추온", "전부 부순다.");
        rig.Sound("SFX_Cine_DroneLaunch", 4.3);
        drones.gameObject.SetActive(false);
        rig.Save(9.5);
    }

    // ------------------------------------------------------------------ 4. 스카이덱 관측 (Level_Facility)

    static void BuildSkydeck()
    {
        var rig = new Rig(IdSkydeck, "CINE_SkydeckLookDown", "Level_Facility");
        rig.sequence.Configure(IdSkydeck, "Level_Facility", true, CinematicSettings.SeenScope.Run, true, false, false, true);
        Transform atPlayer = rig.Sub("AtPlayer", CineAttach.Target.Player, face: CineAttach.Target.Boss);
        Transform atGlass = rig.Sub("AtGlass", CineAttach.Target.Anchor, "CineAnchor_SkydeckGlass", CineAttach.Target.Boss);
        Transform atBoss = rig.Sub("AtBoss", CineAttach.Target.Boss, matchYaw: false);
        Transform bossLook = rig.Sub("BossLook", CineAttach.Target.Boss, follow: true, matchYaw: false);
        Transform bossLookPoint = Child(bossLook, "Chest"); bossLookPoint.localPosition = V(0, 1.4f, 0);

        // 반복 14C: 시선 대상은 가슴·머리 높이 (골반 높이 구도 금지), 어깨너머는 머리에서 1 m 이상 뒤
        // (다리는 좁은 유리 통로: 카메라는 통로 폭 안. S1 = 보스 쪽에서 본 정면 미디엄, S2 = 유리 아래를 내려다보는 POV)
        var s1 = rig.Cam("S1_StepOn", atPlayer, V(0.7f, 1.0f, 2.6f), V(0, 1.4f, 0), 40f);
        var s2 = rig.Cam("S2_LookDown", atPlayer, V(0.25f, 1.7f, 0.4f), V(0, -8f, 4f), 40f);
        var s3a = rig.Cam("S3a_ThroughGlass", atGlass, V(0, 0.9f, -1.2f), V(0, -10f, 2f), 26f, false, bossLookPoint);
        var s3b = rig.Cam("S3b_Crane", atBoss, V(4.5f, 14f, 5f), V(0, 1.4f, 0), 30f, false, bossLookPoint);
        rig.Move(s3b, 4.6, 3.0, V(4.5f, 14f, 5f), V(0, 1.4f, 0), 30f, V(3.6f, 11f, 4f), V(0, 1.4f, 0), 24f);
        rig.Shot(s1, 0, 2.2, "S1_StepOn");
        rig.Shot(s2, 2.2, 1.4, "S2_LookDown");
        rig.Shot(s3a, 3.6, 1.4, "S3a_ThroughGlass");
        rig.Shot(s3b, 4.6, 3.0, "S3b_Crane"); // 4.6~5.0 겹침 = 유리 위 → 사선 크레인 블렌드
        rig.Line(3.7, 2.3, "cine.sky.0", "EGO", "유리 아래가 격납고야.\n붉은 격리장 안에… 실험체 하나.");
        rig.Line(6.1, 1.5, "cine.sky.1", "추온", "…나랑 같은 데서 나왔어.");
        rig.Sound("SFX_Cine_LowImpact", 3.6);
        rig.Save(7.6);
    }

    // ------------------------------------------------------------------ 5. 엘리베이터 하강 (Level_Facility, 무대 프리팹)

    static void BuildElevator()
    {
        var rig = new Rig(IdElevator, "CINE_ElevatorDescent", "Level_Facility");
        rig.sequence.Configure(IdElevator, "Level_Facility", true, CinematicSettings.SeenScope.Run, true, false, false, true);
        Transform cab = Child(rig.stage, "Cab");
        Cine("ElevatorCab", cab, V(0, 0, 0), 0);
        GameObject ind = Cine("FloorIndicator", cab, V(0, 2.62f, 1.26f), 180);
        TMP_Text floor = WorldText("FloorText", ind.transform, V(0, 0, 0.058f), 180, V(0.56f, 0.16f, 0), "B1", new Color(1f, 0.18f, 0.14f));
        Light stripA = MakeLight("StripA", cab, LightType.Point, V(0.7f, 2.85f, 0), V(0.7f, 0, 0), new Color(0.95f, 0.97f, 1f), 2.2f, 5f);
        Light stripB = MakeLight("StripB", cab, LightType.Point, V(-0.7f, 2.85f, 0), V(-0.7f, 0, 0), new Color(0.95f, 0.97f, 1f), 2.2f, 5f);
        Light emer = MakeLight("EmergencyRed", cab, LightType.Point, V(0, 2.85f, 0), V(0, 0, 0), new Color(1f, 0.1f, 0.08f), 3f, 5f);
        Animator chuon = Puppet(ChuonVisual, cab, "Chuon_Cab", V(-0.25f, 0, -0.3f), 0);

        var s1 = rig.Cam("S1_CabWide", rig.cams, V(1.0f, 2.5f, -1.0f), V(-0.25f, 1.1f, 0.2f), 55f);
        var s2 = rig.Cam("S2_Indicator", rig.cams, V(0.15f, 2.2f, 0.2f), V(0, 2.62f, 1.26f), 24f);
        var s3 = rig.Cam("S3_ChuonMCU", rig.cams, V(-0.15f, 1.55f, 0.85f), V(-0.25f, 1.5f, -0.3f), 30f);
        var s4 = rig.Cam("S4_OTS", rig.cams, V(0.55f, 1.72f, -1.2f), V(-0.35f, 1.35f, 1.3f), 42f, false); // 반복 14C: 머리에 붙지 않게 오른쪽 어깨너머
        rig.Move(s4, 9.0, 2.6, V(0.55f, 1.72f, -1.2f), V(-0.35f, 1.35f, 1.3f), 42f, V(0.5f, 1.7f, -1.05f), V(-0.35f, 1.35f, 1.3f), 38f);
        var s5 = rig.Cam("S5_B4", rig.cams, V(0.1f, 2.25f, 0.25f), V(0, 2.62f, 1.26f), 30f);
        rig.Shot(s1, 0, 3.4, "S1_CabWide");
        rig.Shot(s2, 3.4, 2.6, "S2_Indicator");
        rig.Shot(s3, 6.0, 3.0, "S3_ChuonMCU");
        rig.Shot(s4, 9.0, 2.6, "S4_OTS");
        rig.Shot(s5, 11.6, 1.4, "S5_B4");
        rig.Text(floor, 0, 2.6, "B1");
        rig.Text(floor, 2.6, 2.4, "B2");
        rig.Text(floor, 5.0, 3.6, "B3");
        rig.Text(floor, 8.6, 3.0, "B4");
        rig.Text(floor, 11.6, 1.4, "B4  HANGAR", 3f);
        rig.LightBeat(stripA, 6.0, 1.8, 1f, 0.95f, 12f);
        rig.LightBeat(stripB, 6.0, 1.8, 1f, 0.95f, 13f);
        rig.LightBeat(stripA, 7.8, 5.2, 0.55f);
        rig.LightBeat(stripB, 7.8, 5.2, 0.55f);
        rig.Active(emer.gameObject, 6.2, 6.8);
        rig.LightBeat(emer, 6.2, 6.8, 1f, 0.35f, 1.2f);
        rig.Fade(0, 0.7, 1, 0);
        rig.Fade(12.3, 0.7, 0, 1);
        rig.Line(0.8, 2.5, "cine.elev.0", "EGO", "버튼 하나짜리 엘리베이터라.\n편도 티켓이네.");
        rig.Line(6.3, 2.4, "cine.elev.1", "추온", "…돌아갈 생각 없어.");
        rig.Line(9.2, 2.4, "cine.elev.2", "EGO", "알아. 그러니까 끝까지\n같이 가는 거야, 추온.");
        rig.Sound("SFX_Cine_ElevatorRide", 0);
        rig.Sound("SFX_Cine_LowImpact", 12.4);
        rig.Act(chuon, Clip("Common/Inplace", "Common_Idle"), 0, 13.0);
        rig.Save(13.0);
    }

    // ------------------------------------------------------------------ 9. 엔딩: Type-D 쓰러짐 → 츠카사 홀로그램 (Level_Facility)

    static void BuildEnding()
    {
        var rig = new Rig(IdEnding, "CINE_EndingTsukasa", "Level_Facility");
        rig.sequence.Configure(IdEnding, "Level_Facility", true, CinematicSettings.SeenScope.Run, true, false, false, true);
        Transform atBoss = rig.Sub("AtBoss", CineAttach.Target.Boss, face: CineAttach.Target.Player);
        atBoss.GetComponent<CineAttach>().SetClearBehind(6.2f); // 반복 14C: 홀로그램 무대(-Z 3.4 m, S6 카메라 -5.6 m)가 벽에 묻히지 않게
        Transform atPlayer = rig.Sub("AtPlayer", CineAttach.Target.Player, face: CineAttach.Target.Boss);
        Transform head = rig.Sub("PlayerHead", CineAttach.Target.PlayerHead, follow: true, matchYaw: false);
        Transform playerLook = Child(atPlayer, "PlayerChest"); playerLook.localPosition = V(0, 1.4f, 0);

        Transform holo = Child(atBoss, "Hologram");
        Transform projHolder = Child(holo, "ProjectorRise");
        var projAnim = projHolder.gameObject.AddComponent<Animator>();
        GameObject proj = Cine("HoloProjector", projHolder, V(0, -0.35f, -3.4f), 0); proj.name = "Projector";
        Light beam = MakeLight("ProjectorBeam", holo, LightType.Spot, V(0, 0.15f, -3.4f), V(0, 3f, -3.4f), new Color(1f, 0.9f, 0.9f), 6f, 6f, 50f);
        Light rim = MakeLight("HoloRedRim", holo, LightType.Point, V(0, 1.2f, -3.0f), V(0, 1.2f, -3.4f), new Color(1f, 0.15f, 0.1f), 2.5f, 5f);
        Transform figT = Child(holo, "TsukasaHologram");
        figT.localPosition = V(0, 0.24f, -3.4f);
        figT.localScale = Vector3.one * 1.15f;
        GameObject fig = Cine("TsukasaFigure", figT, V(0, 0, 0), 0);
        foreach (Renderer r in fig.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterial = M["Holo"];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        var figure = figT.gameObject.AddComponent<CineHologramFigure>();
        figure.materialize = 0f;
        var figAnim = figT.gameObject.AddComponent<Animator>();
        Transform holoHead = Child(figT, "HoloHead"); holoHead.localPosition = V(0, 1.74f, 0);

        // 반복 14C: 연출 시작 때 Type-D 는 아직 쓰러지는 중 → 전신이 들어오게 (머리 잘림 방지)
        var s1 = rig.Cam("S1_Fallen", atBoss, V(-4.5f, 1.0f, 3.0f), V(0, 1.1f, 0), 40f);
        rig.Move(s1, 0, 3.0, V(-4.5f, 1.0f, 3.0f), V(0, 1.1f, 0), 40f, V(-4.0f, 0.9f, 2.6f), V(0, 1.0f, 0), 38f);
        var s2 = rig.Cam("S2_ChuonReact", atPlayer, V(0.6f, 1.62f, 1.4f), V(0, 1.55f, 0), 30f, true, head);
        var s3 = rig.Cam("S3_ProjectorInsert", atBoss, V(0.9f, 0.4f, -2.0f), V(0, 0.3f, -3.4f), 30f, false);
        // 14C: 끝까지 프로젝터+발끝부터 맺히는 하체를 잡는다 (빈 벽으로 틸트 업하지 않게)
        rig.Move(s3, 5.2, 3.2, V(0.9f, 0.45f, -2.0f), V(0, 0.2f, -3.4f), 32f, V(1.25f, 0.6f, -1.7f), V(0, 0.75f, -3.4f), 34f);
        // 반복 14C: 어깨 높이 어깨너머 (낮은 앵글은 다리만 잡혀 교체)
        var s4a = rig.Cam("S4a_LowTwoShot", atPlayer, V(0.5f, 1.55f, -1.25f), V(0, 1.9f, 5f), 38f, true, holoHead);
        var s4b = rig.Cam("S4b_HoloMCU", atBoss, V(0.6f, 1.3f, -0.9f), V(0, 1.9f, -3.4f), 30f, true, holoHead); // 14C: 미디엄(머리+몸통)
        var s5 = rig.Cam("S5_ChuonClose", atPlayer, V(-0.45f, 1.6f, 1.15f), V(0, 1.55f, 0), 26f, true, head);
        var s6 = rig.Cam("S6_Reverse", atBoss, V(1.3f, 2.4f, -5.6f), V(0, 1.4f, 3f), 36f, true, playerLook);
        rig.Shot(s1, 0, 3.0, "S1_Fallen");
        rig.Shot(s2, 3.0, 2.2, "S2_ChuonReact");
        rig.Shot(s3, 5.2, 3.2, "S3_ProjectorInsert");
        rig.Shot(s4a, 8.4, 4.0, "S4a_LowTwoShot");
        rig.Shot(s4b, 12.4, 4.0, "S4b_HoloMCU");
        rig.Shot(s5, 16.4, 2.6, "S5_ChuonClose");
        rig.Shot(s6, 19.0, 3.0, "S6_Reverse");
        rig.Shot(s4a, 22.0, 2.0, "S7_GlitchOut");

        rig.Keys(projAnim, "TRK_ProjectorRise", 5.2, 18.8, clip =>
        {
            Steps(clip, "Projector", typeof(Transform), "m_LocalPosition.y", (0f, -0.35f), (1.2f, 0f), (18.8f, 0f));
            // 14C: x/z 도 키 — y 만 키하면 애니메이터가 x/z 를 0 으로 써 프로젝터가 보스 발밑으로 갔다
            Steps(clip, "Projector", typeof(Transform), "m_LocalPosition.x", (0f, 0f), (18.8f, 0f));
            Steps(clip, "Projector", typeof(Transform), "m_LocalPosition.z", (0f, -3.4f), (18.8f, -3.4f));
        });
        rig.Keys(figAnim, "TRK_HoloMaterialize", 7.6, 16.4, clip =>
        {
            Steps(clip, "", typeof(CineHologramFigure), "materialize", (0f, 0f), (2.0f, 1f), (14.4f, 1f), (15.8f, 0f), (16.4f, 0f));
            Steps(clip, "", typeof(CineHologramFigure), "glitch", (0f, 0.7f), (2.2f, 0.12f), (14.2f, 0.15f), (15.0f, 0.85f), (16.4f, 1f));
            Steps(clip, "", typeof(CineHologramFigure), "intensity", (0f, 1.6f), (2.0f, 1f), (14.4f, 1f), (16.4f, 0.4f));
        });
        rig.Active(holo.gameObject, 5.2, 18.8);
        rig.Active(figT.gameObject, 7.6, 16.4);
        rig.LightBeat(beam, 5.2, 2.4, 0.4f, 0.8f, 14f);
        rig.LightBeat(beam, 7.6, 14.4, 1f, 0.15f, 7f);
        rig.LightBeat(beam, 22.0, 2.0, 0.6f, 0.9f, 16f);
        rig.LightBeat(rim, 7.6, 16.4, 1f, 0.2f, 5f);
        rig.Line(3.1, 2.1, "cine.end.0", "EGO", "잠깐. 누가 회선에\n끼어들었어.");
        rig.Line(9.4, 6.6, "ending.tsukasa", TsukasaEndingPresenter.SpeakerName, TsukasaEndingPresenter.CanonLine);
        rig.Line(16.6, 2.3, "ending.reply.0", "추온", "그 궤도, 내가 끊어 주지.");
        rig.Line(19.2, 2.8, "ending.reply.1", "EGO", "설계자 양반, 날 버린 거 후회할걸.\n난 예측 불가능하거든.");
        rig.Sound("SFX_Cine_LowImpact", 0.1);
        rig.Sound("SFX_Cine_HoloGlitch", 5.4);
        rig.Sound("SFX_Cine_HoloGlitch", 22.0);
        holo.gameObject.SetActive(false);
        figT.gameObject.SetActive(false);
        rig.Save(24.0);
    }

    // ------------------------------------------------------------------ 6. Type-D 리빌 v2 (기존 TL_MainScene_Intro 신호 그대로 + 샷 7개)

    public const string TypeDV2Path = TimelineDir + "/TL_Cine_TypeDReveal_v2.playable";
    public static readonly string[] TypeDV2NewShots = { "DoorPOV", "HangarWide", "VisorInsert", "ChuonReaction", "Return" };

    static void BuildTypeDRevealV2()
    {
        var src = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TypeDOriginal);
        if (src == null) { Log.Append("TypeD original missing; "); return; }
        if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TypeDV2Path) != null)
            AssetDatabase.DeleteAsset(TypeDV2Path);
        var tl = ScriptableObject.CreateInstance<TimelineAsset>();
        tl.name = "TL_Cine_TypeDReveal_v2";
        AssetDatabase.CreateAsset(tl, TypeDV2Path);

        // 신호: 원본 시각·에셋·플래그 그대로 (MainSceneArrivalController IntroStep 0~9 순서 고정)
        var sig = tl.CreateTrack<SignalTrack>(null, "TRK_Signals_Intro");
        int copied = 0;
        foreach (TrackAsset t in src.GetOutputTracks())
        {
            if (!(t is SignalTrack)) continue;
            foreach (IMarker m in t.GetMarkers())
            {
                if (!(m is SignalEmitter e)) continue;
                SignalEmitter n = sig.CreateMarker<SignalEmitter>(e.time);
                n.asset = e.asset; n.emitOnce = e.emitOnce; n.retroactive = e.retroactive;
                copied++;
            }
        }

        var cam = tl.CreateTrack<CinemachineTrack>(null, "TRK_Camera_Shots");
        void ShotClip(double start, double dur, string label, string exposed)
        {
            TimelineClip c = cam.CreateClip<CinemachineShot>();
            c.start = start; c.duration = dur; c.displayName = label;
            var s = (CinemachineShot)c.asset;
            s.DisplayName = label;
            s.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = new PropertyName(exposed) };
        }
        ShotClip(0.00, 2.25, "SHOT_TDR_DoorPOV", "TDRv2.DoorPOV");
        ShotClip(2.25, 2.10, "SHOT_ElevatorExit", "MainIntro.SHOT_ElevatorExit.VCam_MainIntro_ElevatorExit");
        // 반복 14C: 실제 도착 연출은 보스 리빌 걸음이 끝나는 약 7.0 s 에 타임라인을 멈춘다(MainSceneArrivalController.endTimelineWhenBossRevealWalkEnds).
        // 그 앞에 와이드·바이저 인서트를 넣고 6.75 s 부터 게임플레이 구도(Return)로 끝내 복귀가 짧게 보이게 한다.
        ShotClip(4.35, 1.20, "SHOT_TDR_HangarWide", "TDRv2.HangarWide");
        ShotClip(5.55, 1.20, "SHOT_TDR_VisorInsert", "TDRv2.VisorInsert");
        ShotClip(6.75, src.duration - 6.75, "SHOT_TDR_Return", "TDRv2.Return");
        tl.CreateTrack<AnimationTrack>(null, "TRK_Player_Anim");
        tl.CreateTrack<AnimationTrack>(null, "TRK_Boss_Anim");
        tl.CreateTrack<ActivationTrack>(null, "TRK_VFX_Activation");
        var audio = tl.CreateTrack<AudioTrack>(null, "TRK_Audio");
        var impact = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/SFX_Cine_LowImpact.wav");
        if (impact != null) { TimelineClip ac = audio.CreateClip(impact); ac.start = 4.35; ac.duration = impact.length; }
        var box = tl.CreateTrack<CineLetterboxTrack>(null, "TRK_Letterbox");
        TimelineClip lb = box.CreateClip<CineLetterboxClip>();
        lb.start = 0.0; lb.duration = src.duration - 0.25; lb.easeInDuration = 0.35; lb.easeOutDuration = 0.3;
        tl.durationMode = TimelineAsset.DurationMode.FixedLength;
        tl.fixedDuration = src.duration;
        EditorUtility.SetDirty(tl);

        // 샷 카메라 프리팹 (설치기가 MainSceneIntroDirector 아래에 넣고 exposed 참조를 연결)
        var root = new GameObject("CINE_TypeDReveal_v2_Shots");
        SceneManager.MoveGameObjectToScene(root, s_stage);
        root.AddComponent<CineAttachOnDirectorPlay>();
        Transform AttachSub(string n, CineAttach.Target t, CineAttach.Target face, bool follow = false, bool yaw = true)
        {
            Transform x = Child(root.transform, n);
            var a = x.gameObject.AddComponent<CineAttach>();
            a.Configure(t, null, yaw, follow);
            if (face != CineAttach.Target.None) a.ConfigureFacing(face);
            return x;
        }
        Transform atPlayer = AttachSub("AtPlayer", CineAttach.Target.Player, CineAttach.Target.Boss);
        Transform atBoss = AttachSub("AtBoss", CineAttach.Target.Boss, CineAttach.Target.Player);
        Transform bossHead = AttachSub("BossHead", CineAttach.Target.BossHead, CineAttach.Target.None, false, false);
        Transform playerHead = AttachSub("PlayerHead", CineAttach.Target.PlayerHead, CineAttach.Target.None, false, false);
        CinemachineCamera MakeCam(string n, Transform parent, Vector3 pos, Vector3 look, float fov, Transform lookT, bool noise)
        {
            Transform t = Child(parent, "TDRv2_" + n);
            var c = t.gameObject.AddComponent<CinemachineCamera>();
            t.localPosition = pos;
            t.localRotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            LensSettings lens = LensSettings.Default; lens.FieldOfView = fov; lens.NearClipPlane = 0.05f; lens.FarClipPlane = 800f;
            c.Lens = lens; c.Priority = -10;
            if (lookT != null) { t.gameObject.AddComponent<CinemachineHardLookAt>(); c.Target.CustomLookAtTarget = true; c.Target.LookAtTarget = lookT; }
            if (noise)
            {
                var p = t.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
                p.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoisePath); p.AmplitudeGain = 0.25f; p.FrequencyGain = 0.5f;
            }
            return c;
        }
        MakeCam("DoorPOV", atPlayer, V(0.35f, 1.62f, -1.5f), V(0, 1.5f, 6f), 45f, null, true);
        MakeCam("HangarWide", atBoss, V(-5.0f, 6.0f, 14.0f), V(0, 1.8f, 0), 38f, null, true);
        MakeCam("VisorInsert", atBoss, V(0.28f, 2.05f, 1.25f), V(0, 2.0f, 0), 18f, bossHead, true);
        MakeCam("ChuonReaction", atPlayer, V(-0.5f, 1.6f, 1.4f), V(0, 1.55f, 0), 28f, playerHead, true);
        MakeCam("Return", atPlayer, V(0.3f, 1.75f, -3.4f), V(0, 1.4f, 5f), 50f, null, false);
        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/CINE_TypeDReveal_v2_Shots.prefab", out bool ok);
        Object.DestroyImmediate(root);
        Log.Append($"TypeD v2 signals={copied} shots prefab {(ok ? "ok" : "FAILED")}; ");
    }
}
