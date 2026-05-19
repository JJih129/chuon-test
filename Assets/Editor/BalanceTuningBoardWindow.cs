using System.Collections.Generic;
using Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BalanceTuningBoardWindow : EditorWindow
{
    readonly List<BossController> bossControllers = new List<BossController>();
    readonly List<BossHealth> bossHealths = new List<BossHealth>();
    readonly List<BossBreakController> bossBreaks = new List<BossBreakController>();
    readonly List<DroneController> drones = new List<DroneController>();
    readonly List<AttackData> attackDataAssets = new List<AttackData>();

    readonly Dictionary<Object, Editor> editors = new Dictionary<Object, Editor>();
    readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

    Vector2 scroll;
    int tab;
    string search = string.Empty;
    bool includeSceneObjects = true;
    bool includePrefabAssets = true;
    bool includeProjectScenes = true;

    const string BossControllerGuid = "8ec8d36d63a55e04aa2124dec9e3faa0";
    const string BossHealthGuid = "d029b12f45eb9c347b95ae2ef097bf10";
    const string BossBreakGuid = "1626d5f61ee14c04b83b3c0c934be79d";

    static readonly string[] Tabs =
    {
        "보스 핵심",
        "보스 패턴",
        "플레이어 공격",
        "드론/기타"
    };

    [MenuItem("Window/ChuOn/밸런스 튜닝 보드")]
    public static void Open()
    {
        var window = GetWindow<BalanceTuningBoardWindow>("밸런스 튜닝 보드");
        window.minSize = new Vector2(760f, 560f);
        window.RefreshTargets();
    }

    void OnEnable()
    {
        RefreshTargets();
    }

    void OnDisable()
    {
        foreach (var editor in editors.Values)
            DestroyImmediate(editor);
        editors.Clear();
    }

    void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "기획자가 자주 조절하는 전투 밸런스 수치를 한 곳에 모아 보는 창입니다. 값은 원본 프리팹/씬 오브젝트/ScriptableObject에 바로 저장됩니다.",
            MessageType.Info);

        tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(30f));
        scroll = EditorGUILayout.BeginScrollView(scroll);

        switch (tab)
        {
            case 0:
                DrawBossCoreTab();
                break;
            case 1:
                DrawBossPatternTab();
                break;
            case 2:
                DrawPlayerAttackTab();
                break;
            case 3:
                DrawDroneTab();
                break;
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                RefreshTargets();

            includePrefabAssets = GUILayout.Toggle(includePrefabAssets, "프리팹 포함", EditorStyles.toolbarButton, GUILayout.Width(90f));
            includeSceneObjects = GUILayout.Toggle(includeSceneObjects, "현재 씬 포함", EditorStyles.toolbarButton, GUILayout.Width(90f));
            includeProjectScenes = GUILayout.Toggle(includeProjectScenes, "프로젝트 씬 포함", EditorStyles.toolbarButton, GUILayout.Width(110f));

            GUILayout.Space(12f);
            GUILayout.Label("검색", GUILayout.Width(32f));
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));

            GUILayout.FlexibleSpace();
            GUILayout.Label($"보스 {bossControllers.Count} / 공격데이터 {attackDataAssets.Count} / 드론 {drones.Count}");
        }
    }

    void RefreshTargets()
    {
        bossControllers.Clear();
        bossHealths.Clear();
        bossBreaks.Clear();
        drones.Clear();
        attackDataAssets.Clear();

        if (includePrefabAssets)
            CollectPrefabTargets();

        if (includeSceneObjects)
            CollectSceneTargets();

        if (includeProjectScenes)
            CollectProjectSceneTargets();

        CollectAttackDataAssets();
        Repaint();
    }

    void CollectPrefabTargets()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
                continue;

            AddComponents(root.GetComponentsInChildren<BossController>(true), bossControllers);
            AddComponents(root.GetComponentsInChildren<BossHealth>(true), bossHealths);
            AddComponents(root.GetComponentsInChildren<BossBreakController>(true), bossBreaks);
            AddComponents(root.GetComponentsInChildren<DroneController>(true), drones);
        }
    }

    void CollectSceneTargets()
    {
        AddComponents(FindSceneObjects<BossController>(), bossControllers);
        AddComponents(FindSceneObjects<BossHealth>(), bossHealths);
        AddComponents(FindSceneObjects<BossBreakController>(), bossBreaks);
        AddComponents(FindSceneObjects<DroneController>(), drones);
    }

    void CollectProjectSceneTargets()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
        foreach (string guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!SceneFileContainsBoss(path))
                continue;

            Scene scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AddComponents(root.GetComponentsInChildren<BossController>(true), bossControllers);
                AddComponents(root.GetComponentsInChildren<BossHealth>(true), bossHealths);
                AddComponents(root.GetComponentsInChildren<BossBreakController>(true), bossBreaks);
                AddComponents(root.GetComponentsInChildren<DroneController>(true), drones);
            }
        }
    }

    static bool SceneFileContainsBoss(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return false;

        string text = System.IO.File.ReadAllText(path);
        return text.Contains(BossControllerGuid) || text.Contains(BossHealthGuid) || text.Contains(BossBreakGuid);
    }

    void CollectAttackDataAssets()
    {
        string[] guids = AssetDatabase.FindAssets("t:AttackData", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
            if (data != null && PassesSearch(data))
                attackDataAssets.Add(data);
        }
    }

    static T[] FindSceneObjects<T>() where T : Component
    {
        var result = new List<T>();
        T[] all = Resources.FindObjectsOfTypeAll<T>();
        foreach (T item in all)
        {
            if (item == null)
                continue;
            if (EditorUtility.IsPersistent(item))
                continue;
            if (item.gameObject.scene.IsValid())
                result.Add(item);
        }
        return result.ToArray();
    }

    void AddComponents<T>(IEnumerable<T> components, List<T> list) where T : Object
    {
        foreach (T component in components)
        {
            if (component == null || list.Contains(component) || !PassesSearch(component))
                continue;
            list.Add(component);
        }
    }

    bool PassesSearch(Object target)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        string key = search.Trim().ToLowerInvariant();
        string name = target.name.ToLowerInvariant();
        string path = AssetDatabase.GetAssetPath(target).ToLowerInvariant();
        return name.Contains(key) || path.Contains(key);
    }

    void DrawBossCoreTab()
    {
        DrawKoreanHeader("보스 기본 체력", "BossHealth: 최대 체력, 시작 체력, 피격 후 무적 시간 등 생존 관련 수치입니다.");
        foreach (var health in bossHealths)
        {
            DrawTargetBox(health, () =>
            {
                var so = new SerializedObject(health);
                so.Update();
                DrawProp(so, "maxHP", "최대 체력");
                DrawProp(so, "startHP", "시작 체력 (0이면 최대 체력)");
                DrawProp(so, "invincibleOnStart", "시작 무적");
                DrawProp(so, "hitInvincibleDuration", "피격 후 무적 시간");
                DrawProp(so, "startStaggered", "시작 그로기 상태");
                DrawProp(so, "disableOnDeath", "사망 시 오브젝트 비활성화");
                so.ApplyModifiedProperties();
            });
        }

        DrawKoreanHeader("보스 브레이크/그로기", "BossBreakController: 브레이크 게이지와 패링/일반 공격이 주는 브레이크 피해입니다.");
        foreach (var breakController in bossBreaks)
        {
            DrawTargetBox(breakController, () =>
            {
                var so = new SerializedObject(breakController);
                so.Update();
                DrawProp(so, "maxBreak", "브레이크 최대치");
                DrawProp(so, "basicAttackBreakDamage", "일반 공격 브레이크 피해");
                DrawProp(so, "baseBreakPerHit", "패링 브레이크 피해");
                DrawProp(so, "breakDuration", "브레이크 지속 시간");
                DrawProp(so, "recoveryPerSecond", "브레이크 회복량(레거시)");
                so.ApplyModifiedProperties();
            });
        }

        DrawKoreanHeader("보스 전투 템포/이동", "BossController: 이동 속도, 페이즈 전환, 텔레그래프 최소 시간, 후속 공격 확률 등 전투 전체 감각입니다.");
        foreach (var boss in bossControllers)
        {
            DrawTargetBox(boss, () =>
            {
                var so = new SerializedObject(boss);
                so.Update();
                DrawProps(so, "이동", "moveSpeed", "stoppingDistance", "attackCommitDistanceBuffer", "moveSlowdownDistance");
                DrawProps(so, "기본 타이밍", "introIdleDuration", "combatIdleTime", "maxAttackStateWaitTime");
                DrawProps(so, "페이즈", "phaseTwoThresholdNormalized", "phaseThreeThresholdNormalized", "phaseTwoTelegraphScale", "phaseThreeTelegraphScale", "phaseTwoFollowUpChanceBonus", "phaseThreeFollowUpChanceBonus", "phaseThreeExtraFollowUpChains");
                DrawProps(so, "난이도", "difficultyTier", "difficultyProfiles");
                DrawProps(so, "최소 대응 시간", "minimumParryTelegraphLeadTime", "minimumGuardTelegraphLeadTime", "minimumDodgeTelegraphLeadTime", "minimumDangerTelegraphLeadTime", "minimumParryPunishWindow", "minimumGuardPunishWindow", "minimumDodgePunishWindow", "minimumDangerPunishWindow");
                DrawProps(so, "후속 공격/반복 억제", "maxFollowUpChainCount", "followUpRecoveryTax", "fakeOutFollowUpChanceMultiplier", "dangerFollowUpChanceMultiplier", "punishHeavyFollowUpChanceMultiplier", "immediateRepeatPatternWeightMultiplier", "recentRepeatPatternWeightMultiplier", "repeatedTelegraphWeightMultiplier", "recentPatternMemory");
                so.ApplyModifiedProperties();
            });
        }
    }

    void DrawBossPatternTab()
    {
        DrawKoreanHeader("보스 패턴 목록", "각 패턴의 데미지, 쿨타임, 선택 가중치, 거리 조건, 후딜/딜타임, 원거리 검기 수치를 한 번에 조절합니다.");
        foreach (var boss in bossControllers)
        {
            DrawTargetBox(boss, () =>
            {
                var so = new SerializedObject(boss);
                so.Update();

                DrawProps(so, "패턴 선택 시스템", "usePatternSelector", "useLegacyPatternSelectionFallback", "patternSelector", "postActionSettings");

                SerializedProperty patterns = so.FindProperty("allPatterns");
                if (patterns == null)
                {
                    EditorGUILayout.HelpBox("allPatterns 필드를 찾지 못했습니다. BossController 필드명이 바뀌었는지 확인하세요.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField("공격 패턴 배열", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    patterns.arraySize = EditorGUILayout.IntField("패턴 개수", patterns.arraySize);
                    for (int i = 0; i < patterns.arraySize; i++)
                        DrawPatternElement(patterns.GetArrayElementAtIndex(i), i);
                    EditorGUI.indentLevel--;
                }

                so.ApplyModifiedProperties();
            });
        }
    }

    void DrawPatternElement(SerializedProperty pattern, int index)
    {
        string name = GetChildString(pattern, "patternName");
        if (string.IsNullOrWhiteSpace(name))
            name = $"패턴 {index + 1}";

        string key = pattern.propertyPath;
        bool open = GetFoldout(key, index < 3);
        open = EditorGUILayout.Foldout(open, $"{index + 1}. {name}", true);
        SetFoldout(key, open);
        if (!open)
            return;

        EditorGUI.indentLevel++;
        DrawChild(pattern, "patternName", "패턴 이름");
        DrawChild(pattern, "animTriggerName", "애니메이션 트리거");
        DrawChild(pattern, "damageAmount", "공격 데미지");
        DrawChild(pattern, "weight", "선택 가중치");
        DrawChild(pattern, "cooldown", "쿨타임");
        DrawChild(pattern, "minRange", "최소 거리");
        DrawChild(pattern, "maxRange", "최대 거리");
        DrawChild(pattern, "minPhase", "사용 시작 페이즈");
        DrawChild(pattern, "maxPhase", "사용 종료 페이즈");
        DrawChild(pattern, "telegraphType", "대응 타입");
        DrawChild(pattern, "telegraphLeadTime", "공격 예고 시간");
        DrawChild(pattern, "recoveryTime", "공격 후 빈틈 시간");
        DrawChild(pattern, "punishWindowDuration", "딜타임 창");
        DrawChild(pattern, "punishDamageMultiplier", "딜타임 피해 배율");
        DrawChild(pattern, "allowFollowUpChain", "후속 공격 허용");
        DrawChild(pattern, "followUpChance", "후속 공격 확률");
        DrawChild(pattern, "followUpDelay", "후속 공격 지연");
        DrawChild(pattern, "selectorData", "패턴 선택 상세 데이터");
        DrawChild(pattern, "phaseModifiers", "페이즈별 보정");
        DrawChild(pattern, "firesSwordWaveProjectile", "검기 발사");
        DrawChild(pattern, "swordWaveSpeed", "검기 속도");
        DrawChild(pattern, "swordWaveLifeTime", "검기 수명");
        DrawChild(pattern, "swordWaveWidth", "검기 폭");
        DrawChild(pattern, "swordWaveHeight", "검기 높이");
        DrawChild(pattern, "swordWaveLength", "검기 길이");
        DrawChild(pattern, "hitboxExpandedPadding", "히트박스 확장");
        DrawChild(pattern, "hitboxMeshPaddingScale", "메시 기반 히트박스 여유");
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4f);
    }

    void DrawPlayerAttackTab()
    {
        DrawKoreanHeader("플레이어 공격 데이터", "AttackData ScriptableObject: 기본 데미지, 공격 배율, 히트 타이밍, 판정 방식 등을 조절합니다.");
        foreach (var attackData in attackDataAssets)
        {
            DrawTargetBox(attackData, () =>
            {
                var so = new SerializedObject(attackData);
                so.Update();
                DrawProps(so, "데미지", "baseDamage", "attackDamageMultiplier");
                DrawProps(so, "콤보 연결", "input", "next", "nextByInput");
                DrawProps(so, "히트 판정", "hitWindows", "hitDetectionMode");
                DrawDefaultInspectorFor(attackData);
                so.ApplyModifiedProperties();
            });
        }
    }

    void DrawDroneTab()
    {
        DrawKoreanHeader("드론/로비 적 밸런스", "DroneController: 체력, 이동 속도, 공격 데미지, 투사체 속도, 사격 쿨타임 등 로비 적 수치입니다.");
        foreach (var drone in drones)
        {
            DrawTargetBox(drone, () =>
            {
                var so = new SerializedObject(drone);
                so.Update();
                DrawProps(so, "체력/공격", "maxHP", "projectileDamage", "projectileSpeed", "fireCooldown", "fireDistance");
                DrawProps(so, "이동", "moveSpeed", "stopDistance", "turnSpeedDeg", "strafeSpeedMultiplier", "retreatSpeedMultiplier", "combatDistanceTolerance", "postShotPauseDuration", "postShotStrafeDuration");
                DrawProps(so, "공격 예고", "fireFacingAngleThreshold", "telegraphFacingAngleThreshold");
                so.ApplyModifiedProperties();
            });
        }
    }

    void DrawTargetBox(Object target, System.Action drawContent)
    {
        if (target == null)
            return;

        string path = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrEmpty(path) && target is Component component)
            path = component.gameObject.scene.path;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(target, target.GetType(), true);
                if (GUILayout.Button("선택", GUILayout.Width(52f)))
                    Selection.activeObject = target;
            }

            if (!string.IsNullOrEmpty(path))
                EditorGUILayout.LabelField("위치", path, EditorStyles.miniLabel);

            drawContent?.Invoke();
        }
    }

    void DrawKoreanHeader(string title, string description)
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(description, MessageType.None);
    }

    void DrawProps(SerializedObject so, string label, params string[] propertyNames)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        foreach (string propertyName in propertyNames)
            DrawProp(so, propertyName, ObjectNames.NicifyVariableName(propertyName));
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(3f);
    }

    void DrawProp(SerializedObject so, string propertyName, string label)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null)
            return;

        EditorGUILayout.PropertyField(property, new GUIContent(label), true);
    }

    void DrawChild(SerializedProperty parent, string childName, string label)
    {
        SerializedProperty child = parent.FindPropertyRelative(childName);
        if (child == null)
            return;

        EditorGUILayout.PropertyField(child, new GUIContent(label), true);
    }

    string GetChildString(SerializedProperty parent, string childName)
    {
        SerializedProperty child = parent.FindPropertyRelative(childName);
        return child != null && child.propertyType == SerializedPropertyType.String ? child.stringValue : string.Empty;
    }

    void DrawDefaultInspectorFor(Object target)
    {
        Editor editor = GetCachedEditor(target);
        if (editor == null)
            return;

        string key = target.GetInstanceID() + ".default";
        bool open = EditorGUILayout.Foldout(GetFoldout(key, false), "전체 원본 인스펙터 보기", true);
        SetFoldout(key, open);
        if (open)
            editor.OnInspectorGUI();
    }

    Editor GetCachedEditor(Object target)
    {
        if (target == null)
            return null;

        if (!editors.TryGetValue(target, out Editor editor) || editor == null)
        {
            Editor.CreateCachedEditor(target, null, ref editor);
            editors[target] = editor;
        }

        return editor;
    }

    bool GetFoldout(string key, bool defaultValue)
    {
        if (!foldouts.TryGetValue(key, out bool value))
        {
            value = defaultValue;
            foldouts[key] = value;
        }
        return value;
    }

    void SetFoldout(string key, bool value)
    {
        foldouts[key] = value;
    }
}
