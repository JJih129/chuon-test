// ============================== ▼ 변수 헤더(튜닝 가이드) ▼ ==============================
// [목적] Animator에 Guard 전용 레이어/스테이트/전환을 자동 구성 (기존 레이어/상태는 보존)
// [입력]
// controller        : 수정할 AnimatorController 자산
// guardLayerName    : 생성/보정할 Guard 전용 레이어 이름 (기본 "GuardLayer")
// useUpperBodyMask  : 상체 전용 가드면 true, 전신 가드면 false
// upperBodyMask     : 상체 마스크 (useUpperBodyMask가 true일 때 지정)
//
// guardIdle         : 가드 루프 클립(Loop ON)
// parrySuccess      : 패링 성공 클립(Loop OFF)
// guardBlockHit     : 가드로 막기/패링실패 리액션(Loop OFF)
// guardWalkF/B/L/R  : 가드 이동 4방향 클립(Loop ON)
//
// [파라미터(대소문자 중요)]
// Bool    IsGuarding
// Trigger Parry
// Trigger GuardBlock
// Float   speed   ← ★ 소문자 적용 (프로젝트 다른 스크립트와 통일)
// Float   MoveX
// Float   MoveY
//
// [블렌드트리]
// GuardMove : 2D Freeform Directional (MoveX, MoveY)
//   F(0,+1), B(0,-1), L(-1,0), R(+1,0)
//
// [전환]
// Any → ParrySuccess (조건: Parry, dur 0.03)
// Any → GuardBlockHit(조건: GuardBlock, dur 0.05)
// GuardIdle ↔ GuardMove (조건: speed > 0.1 / ≤ 0.1, dur 0.05)
//
// [주의]
// - Base Layer/기존 레이어는 수정/삭제하지 않음.
// - GuardLayer만 생성/보정.
// =========================================================================================

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

public class GuardAnimatorAutoSetup : EditorWindow
{
    AnimatorController controller;

    string guardLayerName = "GuardLayer";
    bool useUpperBodyMask = false;
    AvatarMask upperBodyMask;

    AnimationClip guardIdle;
    AnimationClip parrySuccess;
    AnimationClip guardBlockHit;
    AnimationClip guardWalkF, guardWalkB, guardStrafeL, guardStrafeR;

    const string PARAM_IS_GUARDING = "IsGuarding";
    const string PARAM_PARRY = "Parry";
    const string PARAM_BLOCK = "GuardBlock";
    const string PARAM_SPEED = "speed"; // ★ 소문자
    const string PARAM_MOVEX = "MoveX";
    const string PARAM_MOVEY = "MoveY";

    [MenuItem("Tools/Guard/Auto Setup Animator")]
    static void Open() => GetWindow<GuardAnimatorAutoSetup>("Guard Auto Setup");

    void OnGUI()
    {
        GUILayout.Label("Animator Controller", EditorStyles.boldLabel);
        controller = (AnimatorController)EditorGUILayout.ObjectField("Controller", controller, typeof(AnimatorController), false);

        GUILayout.Space(6);
        GUILayout.Label("Layer Settings", EditorStyles.boldLabel);
        guardLayerName = EditorGUILayout.TextField("Guard Layer Name", guardLayerName);
        useUpperBodyMask = EditorGUILayout.Toggle("Use Upper-Body Mask", useUpperBodyMask);
        if (useUpperBodyMask)
            upperBodyMask = (AvatarMask)EditorGUILayout.ObjectField("Upper-Body Mask", upperBodyMask, typeof(AvatarMask), false);

        GUILayout.Space(6);
        GUILayout.Label("Clips", EditorStyles.boldLabel);
        guardIdle = (AnimationClip)EditorGUILayout.ObjectField("GuardIdle", guardIdle, typeof(AnimationClip), false);
        parrySuccess = (AnimationClip)EditorGUILayout.ObjectField("ParrySuccess", parrySuccess, typeof(AnimationClip), false);
        guardBlockHit = (AnimationClip)EditorGUILayout.ObjectField("GuardBlockHit", guardBlockHit, typeof(AnimationClip), false);
        guardWalkF = (AnimationClip)EditorGUILayout.ObjectField("GuardWalkF", guardWalkF, typeof(AnimationClip), false);
        guardWalkB = (AnimationClip)EditorGUILayout.ObjectField("GuardWalkB", guardWalkB, typeof(AnimationClip), false);
        guardStrafeL = (AnimationClip)EditorGUILayout.ObjectField("GuardStrafeL", guardStrafeL, typeof(AnimationClip), false);
        guardStrafeR = (AnimationClip)EditorGUILayout.ObjectField("GuardStrafeR", guardStrafeR, typeof(AnimationClip), false);

        GUILayout.Space(10);
        GUI.enabled = controller && guardIdle && parrySuccess && guardBlockHit && guardWalkF && guardWalkB && guardStrafeL && guardStrafeR;
        if (GUILayout.Button("Set Up"))
            Setup();
        GUI.enabled = true;
    }

    void Setup()
    {
        if (!controller) { Debug.LogError("Controller is null."); return; }

        Undo.RegisterCompleteObjectUndo(controller, "Guard Auto Setup");

        // 1) 파라미터 보증
        EnsureParameter(PARAM_IS_GUARDING, AnimatorControllerParameterType.Bool);
        EnsureParameter(PARAM_PARRY, AnimatorControllerParameterType.Trigger);
        EnsureParameter(PARAM_BLOCK, AnimatorControllerParameterType.Trigger);
        EnsureParameter(PARAM_SPEED, AnimatorControllerParameterType.Float);
        EnsureParameter(PARAM_MOVEX, AnimatorControllerParameterType.Float);
        EnsureParameter(PARAM_MOVEY, AnimatorControllerParameterType.Float);

        // 2) 레이어 보증/세팅
        int layerIndex = GetOrCreateLayer(guardLayerName);
        var layer = controller.layers[layerIndex];
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 0f; // 시작은 0 (코드에서 0↔1 제어)
        if (useUpperBodyMask && upperBodyMask) layer.avatarMask = upperBodyMask;
        controller.layers[layerIndex] = layer;

        // 3) 스테이트머신 & 스테이트
        var sm = controller.layers[layerIndex].stateMachine;
        var guardIdleState = GetOrCreateState(sm, "GuardIdle", guardIdle);
        var parrySuccessState = GetOrCreateState(sm, "ParrySuccess", parrySuccess);
        var guardBlockHitState = GetOrCreateState(sm, "GuardBlockHit", guardBlockHit);

        // 4) GuardMove 블렌드트리(2D Freeform Directional)
        var guardMoveState = GetOrCreateBlendTree2DFD(
            sm, "GuardMove", PARAM_MOVEX, PARAM_MOVEY,
            new[] {
                (clip: guardWalkF,   pos: new Vector2( 0f,  1f)),
                (clip: guardWalkB,   pos: new Vector2( 0f, -1f)),
                (clip: guardStrafeL, pos: new Vector2(-1f,  0f)),
                (clip: guardStrafeR, pos: new Vector2( 1f,  0f)),
            });

        // 5) 전환
        EnsureAnyToState(sm, parrySuccessState, PARAM_PARRY, 0.03f);
        EnsureAnyToState(sm, guardBlockHitState, PARAM_BLOCK, 0.05f);
        EnsureStateToState(guardIdleState, guardMoveState, PARAM_SPEED, greaterThan: true, threshold: 0.1f, duration: 0.05f);
        EnsureStateToState(guardMoveState, guardIdleState, PARAM_SPEED, greaterThan: false, threshold: 0.1f, duration: 0.05f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[Guard Auto Setup] Completed. GuardLayer ready (speed=Float, lowercase).");
    }

    void EnsureParameter(string name, AnimatorControllerParameterType type)
    {
        if (!controller.parameters.Any(p => p.name == name))
            controller.AddParameter(name, type);
    }

    int GetOrCreateLayer(string name)
    {
        int idx = controller.layers.ToList().FindIndex(l => l.name == name);
        if (idx >= 0) return idx;

        var newLayer = new AnimatorControllerLayer
        {
            name = name,
            defaultWeight = 0f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            stateMachine = new AnimatorStateMachine() { name = name + "_SM" }
        };
        controller.AddLayer(newLayer);
        return controller.layers.Length - 1;
    }

    AnimatorState GetOrCreateState(AnimatorStateMachine sm, string stateName, AnimationClip clip)
    {
        var st = sm.states.FirstOrDefault(s => s.state.name == stateName).state;
        if (st == null) st = sm.AddState(stateName, new Vector3(300, 100, 0));
        st.motion = clip;
        st.writeDefaultValues = true;
        return st;
    }

    AnimatorState GetOrCreateBlendTree2DFD(AnimatorStateMachine sm, string stateName, string paramX, string paramY, (AnimationClip clip, Vector2 pos)[] motions)
    {
        var st = sm.states.FirstOrDefault(s => s.state.name == stateName).state;
        UnityEditor.Animations.BlendTree bt = null;
        if (st == null) st = sm.AddState(stateName, new Vector3(550, 100, 0));

        if (st.motion is UnityEditor.Animations.BlendTree)
        {
            bt = (UnityEditor.Animations.BlendTree)st.motion;
        }
        else
        {
            bt = new UnityEditor.Animations.BlendTree();
            AssetDatabase.AddObjectToAsset(bt, controller);
            st.motion = bt;
        }

        bt.name = stateName + "_BT";
        bt.hideFlags = HideFlags.HideInHierarchy;
        bt.blendType = UnityEditor.Animations.BlendTreeType.FreeformDirectional2D;
        bt.useAutomaticThresholds = false;
        bt.blendParameter = paramX;
        bt.blendParameterY = paramY;

        while (bt.children.Length > 0) bt.RemoveChild(0);
        foreach (var m in motions) bt.AddChild(m.clip, m.pos);

        EditorUtility.SetDirty(bt);
        return st;
    }

    void EnsureAnyToState(AnimatorStateMachine sm, AnimatorState target, string triggerParam, float duration)
    {
        var t = sm.anyStateTransitions.FirstOrDefault(x => x.destinationState == target);
        if (t == null) t = sm.AddAnyStateTransition(target);
        t.hasExitTime = false;
        t.hasFixedDuration = false;
        t.duration = duration;
        t.conditions = new[] { new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = triggerParam, threshold = 0 } };
    }

    void EnsureStateToState(AnimatorState from, AnimatorState to, string floatParam, bool greaterThan, float threshold, float duration)
    {
        var tr = from.transitions.FirstOrDefault(x => x.destinationState == to);
        if (tr == null) tr = from.AddTransition(to);
        tr.hasExitTime = false;
        tr.hasFixedDuration = false;
        tr.duration = duration;
        tr.conditions = new[] { new AnimatorCondition {
            parameter = floatParam,
            threshold = threshold,
            mode = greaterThan ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less
        }};
    }
}
