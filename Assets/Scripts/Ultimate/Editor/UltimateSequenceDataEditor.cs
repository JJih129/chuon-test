using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UltimateSequenceData))]
[CanEditMultipleObjects]
public sealed class UltimateSequenceDataEditor : Editor
{
    SerializedProperty _activation;
    SerializedProperty _timings;
    SerializedProperty _movement;
    SerializedProperty _damage;
    SerializedProperty _timeFx;
    SerializedProperty _impact;
    SerializedProperty _cinematicAnimation;
    SerializedProperty _vfx;
    SerializedProperty _useThreeStageCameraSplit;
    SerializedProperty _introStageCamera;
    SerializedProperty _assaultStageCamera;
    SerializedProperty _finishStageCamera;
    SerializedProperty _preCastCamera;
    SerializedProperty _introCamera;
    SerializedProperty _dashCamera;
    SerializedProperty _multiSlashCamera;
    SerializedProperty _crackCamera;
    SerializedProperty _finalExplosionCamera;
    SerializedProperty _walkoutCamera;
    SerializedProperty _introTrigger;
    SerializedProperty _dashTrigger;
    SerializedProperty _multiSlashTrigger;
    SerializedProperty _finalExplosionTrigger;
    SerializedProperty _walkoutTrigger;
    SerializedProperty _slashSteps;
    SerializedProperty _debugLog;

    bool _showCore = true;
    bool _showCamera = true;
    bool _showLegacyCamera;
    bool _showVfx = true;
    bool _showCinematicAnimation = true;
    bool _showAnimator = true;
    bool _showSlash = true;

    void OnEnable()
    {
        _activation = serializedObject.FindProperty("activation");
        _timings = serializedObject.FindProperty("timings");
        _movement = serializedObject.FindProperty("movement");
        _damage = serializedObject.FindProperty("damage");
        _timeFx = serializedObject.FindProperty("timeFx");
        _impact = serializedObject.FindProperty("impact");
        _cinematicAnimation = serializedObject.FindProperty("cinematicAnimation");
        _vfx = serializedObject.FindProperty("vfx");
        _useThreeStageCameraSplit = serializedObject.FindProperty("useThreeStageCameraSplit");
        _introStageCamera = serializedObject.FindProperty("introStageCamera");
        _assaultStageCamera = serializedObject.FindProperty("assaultStageCamera");
        _finishStageCamera = serializedObject.FindProperty("finishStageCamera");
        _preCastCamera = serializedObject.FindProperty("preCastCamera");
        _introCamera = serializedObject.FindProperty("introCamera");
        _dashCamera = serializedObject.FindProperty("dashCamera");
        _multiSlashCamera = serializedObject.FindProperty("multiSlashCamera");
        _crackCamera = serializedObject.FindProperty("crackCamera");
        _finalExplosionCamera = serializedObject.FindProperty("finalExplosionCamera");
        _walkoutCamera = serializedObject.FindProperty("walkoutCamera");
        _introTrigger = serializedObject.FindProperty("introTrigger");
        _dashTrigger = serializedObject.FindProperty("dashTrigger");
        _multiSlashTrigger = serializedObject.FindProperty("multiSlashTrigger");
        _finalExplosionTrigger = serializedObject.FindProperty("finalExplosionTrigger");
        _walkoutTrigger = serializedObject.FindProperty("walkoutTrigger");
        _slashSteps = serializedObject.FindProperty("slashSteps");
        _debugLog = serializedObject.FindProperty("debugLog");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawScriptField();
        DrawHeaderSummary();
        DrawWarnings();

        _showCore = DrawFoldout(_showCore, "핵심 연출 설정");
        if (_showCore)
        {
            DrawPropertyBox(_activation, "발동 조건 / 상태 차단");
            DrawPropertyBox(_timings, "페이즈 시간");
            DrawPropertyBox(_movement, "정렬 / 이동");
            DrawPropertyBox(_damage, "데미지");
            DrawPropertyBox(_timeFx, "슬로우 / 히트스톱");
            DrawPropertyBox(_impact, "카메라 임팩트");
        }

        _showCamera = DrawFoldout(_showCamera, "카메라 3단 구성");
        if (_showCamera)
        {
            DrawProperty(_useThreeStageCameraSplit, "3단 카메라 분리 사용", "인트로 / 난무 / 피니시 세 카메라 군으로 운용합니다.");
            DrawPropertyBox(_introStageCamera, "인트로 샷", "발동 시작과 자세 잡는 샷입니다.");
            DrawPropertyBox(_assaultStageCamera, "난무 샷", "돌진, 난무, 균열을 하나의 전투 샷으로 묶습니다.");
            DrawPropertyBox(_finishStageCamera, "피니시 샷", "폭발과 걸어나오는 마무리 샷입니다.");
        }

        _showLegacyCamera = DrawFoldout(_showLegacyCamera, "레거시 개별 샷(고급)");
        if (_showLegacyCamera)
        {
            EditorGUILayout.HelpBox("현재는 3단 카메라 분리 사용이 기본입니다. 세부 phase별 샷을 따로 쓰고 싶을 때만 수정합니다.", MessageType.Info);
            DrawPropertyBox(_preCastCamera, "PreCast 샷");
            DrawPropertyBox(_introCamera, "Intro 샷");
            DrawPropertyBox(_dashCamera, "Dash 샷");
            DrawPropertyBox(_multiSlashCamera, "MultiSlash 샷");
            DrawPropertyBox(_crackCamera, "Crack 샷");
            DrawPropertyBox(_finalExplosionCamera, "Final Explosion 샷");
            DrawPropertyBox(_walkoutCamera, "Walkout 샷");
        }

        _showVfx = DrawFoldout(_showVfx, "VFX 슬롯");
        if (_showVfx)
        {
            EditorGUILayout.HelpBox("지금 단계에서 사용자가 교체하면 되는 핵심 슬롯입니다. 프리팹만 바꾸면 런타임 코드 수정 없이 교체됩니다.", MessageType.None);
            DrawPropertyBox(_vfx, "궁극기 VFX 프리셋");
        }

        _showCinematicAnimation = DrawFoldout(_showCinematicAnimation, "인트로 / 워크아웃 클립");
        if (_showCinematicAnimation)
        {
            EditorGUILayout.HelpBox("발도자세와 워크아웃은 Animator 상태 없이 AnimationClip을 직접 넣어 재생할 수 있습니다. 인트로 구간은 검 클로즈업 샷을 별도로 조절합니다.", MessageType.Info);
            DrawPropertyBox(_cinematicAnimation, "시네마틱 클립 / 검 클로즈업");
        }

        _showAnimator = DrawFoldout(_showAnimator, "애니메이터 트리거");
        if (_showAnimator)
        {
            EditorGUILayout.HelpBox("트리거가 있으면 우선 사용하고, 없으면 폴백 상태명으로 크로스페이드합니다. 상태명은 'Base Layer.H3_D'처럼 전체 경로를 쓰는 것이 안전합니다.", MessageType.Info);
            DrawProperty(_introTrigger, "인트로 트리거");
            DrawProperty(serializedObject.FindProperty("introFallbackState"), "인트로 폴백 상태");
            DrawProperty(_dashTrigger, "돌진 트리거");
            DrawProperty(serializedObject.FindProperty("dashFallbackState"), "돌진 폴백 상태");
            DrawProperty(_multiSlashTrigger, "난무 트리거");
            DrawProperty(serializedObject.FindProperty("multiSlashFallbackState"), "난무 폴백 상태");
            DrawProperty(_finalExplosionTrigger, "피니시 트리거");
            DrawProperty(serializedObject.FindProperty("finalExplosionFallbackState"), "피니시 폴백 상태");
            DrawProperty(_walkoutTrigger, "워크아웃 트리거");
            DrawProperty(serializedObject.FindProperty("walkoutFallbackState"), "워크아웃 폴백 상태");
            DrawProperty(serializedObject.FindProperty("animatorCrossFadeDuration"), "폴백 크로스페이드 시간");
        }

        _showSlash = DrawFoldout(_showSlash, "난무 단계");
        if (_showSlash)
        {
            DrawPropertyBox(_slashSteps, "난무 step 목록");
        }

        DrawSection("디버그");
        DrawProperty(_debugLog, "디버그 로그");

        serializedObject.ApplyModifiedProperties();
    }

    void DrawHeaderSummary()
    {
        UltimateSequenceData data = target as UltimateSequenceData;
        if (data == null)
            return;

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(
            $"예상 총 길이: {data.EstimateTotalDuration():0.00}s\n" +
            $"카메라 모드: {(data.UseThreeStageCameraSplit ? "3단 카메라 분리" : "개별 phase 샷")}\n" +
            $"난무 횟수: {data.SlashCount}",
            MessageType.None);
    }

    void DrawWarnings()
    {
        SerializedProperty dashSlashVfxPrefab = _vfx.FindPropertyRelative("dashSlashVfxPrefab");
        SerializedProperty crackWorldVfxPrefab = _vfx.FindPropertyRelative("crackWorldVfxPrefab");
        SerializedProperty crackScreenVfxPrefab = _vfx.FindPropertyRelative("crackScreenVfxPrefab");
        SerializedProperty explosionVfxPrefab = _vfx.FindPropertyRelative("explosionVfxPrefab");
        SerializedProperty useScreenSpaceCrack = _vfx.FindPropertyRelative("useScreenSpaceCrack");

        bool missingDash = dashSlashVfxPrefab.objectReferenceValue == null;
        bool missingExplosion = explosionVfxPrefab.objectReferenceValue == null;
        bool missingCrack = useScreenSpaceCrack.boolValue
            ? crackScreenVfxPrefab.objectReferenceValue == null
            : crackWorldVfxPrefab.objectReferenceValue == null;

        if (missingDash || missingExplosion || missingCrack)
        {
            string message = "누락 슬롯:";
            if (missingDash) message += " DashSlash VFX";
            if (missingCrack) message += " Crack VFX";
            if (missingExplosion) message += " Explosion VFX";
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        if (string.IsNullOrWhiteSpace(_introTrigger.stringValue) ||
            string.IsNullOrWhiteSpace(_dashTrigger.stringValue) ||
            string.IsNullOrWhiteSpace(_multiSlashTrigger.stringValue) ||
            string.IsNullOrWhiteSpace(_finalExplosionTrigger.stringValue) ||
            string.IsNullOrWhiteSpace(_walkoutTrigger.stringValue))
        {
            EditorGUILayout.HelpBox("애니메이터 트리거 이름이 비어 있습니다. 트리거를 안 쓸 계획이면 비워둬도 되지만, 연출 애니메이션을 붙일 때는 여기부터 맞추면 됩니다.", MessageType.Info);
        }

        SerializedProperty introFallbackState = serializedObject.FindProperty("introFallbackState");
        SerializedProperty dashFallbackState = serializedObject.FindProperty("dashFallbackState");
        SerializedProperty multiSlashFallbackState = serializedObject.FindProperty("multiSlashFallbackState");
        SerializedProperty finalExplosionFallbackState = serializedObject.FindProperty("finalExplosionFallbackState");
        SerializedProperty walkoutFallbackState = serializedObject.FindProperty("walkoutFallbackState");
        if ((string.IsNullOrWhiteSpace(_introTrigger.stringValue) && string.IsNullOrWhiteSpace(introFallbackState.stringValue)) ||
            (string.IsNullOrWhiteSpace(_dashTrigger.stringValue) && string.IsNullOrWhiteSpace(dashFallbackState.stringValue)) ||
            (string.IsNullOrWhiteSpace(_multiSlashTrigger.stringValue) && string.IsNullOrWhiteSpace(multiSlashFallbackState.stringValue)) ||
            (string.IsNullOrWhiteSpace(_finalExplosionTrigger.stringValue) && string.IsNullOrWhiteSpace(finalExplosionFallbackState.stringValue)) ||
            (string.IsNullOrWhiteSpace(_walkoutTrigger.stringValue) && string.IsNullOrWhiteSpace(walkoutFallbackState.stringValue)))
        {
            EditorGUILayout.HelpBox("트리거가 없으면 폴백 상태로 크로스페이드합니다. 둘 다 비어 있는 phase는 해당 애니메이션이 재생되지 않습니다.", MessageType.Warning);
        }
    }

    bool DrawFoldout(bool current, string title)
    {
        EditorGUILayout.Space(6f);
        return EditorGUILayout.Foldout(current, title, true, EditorStyles.foldoutHeader);
    }

    void DrawSection(string title)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void DrawProperty(SerializedProperty property, string label, string tooltip = null)
    {
        if (property == null)
            return;

        EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
    }

    void DrawPropertyBox(SerializedProperty property, string label, string tooltip = null)
    {
        if (property == null)
            return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
        }
    }

    void DrawScriptField()
    {
        using (new EditorGUI.DisabledScope(true))
        {
            MonoScript script = MonoScript.FromScriptableObject((UltimateSequenceData)target);
            EditorGUILayout.ObjectField("스크립트", script, typeof(MonoScript), false);
        }
    }
}
