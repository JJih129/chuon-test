// ============================== �� ���� ���(Ʃ�� ���̵�) �� ==============================
// [����] Animator�� Guard ���� ���̾�/������Ʈ/��ȯ�� �ڵ� ���� (���� ���̾�/���´� ����)
// [�Է�]
// controller        : ������ AnimatorController �ڻ�
// guardLayerName    : ����/������ Guard ���� ���̾� �̸� (�⺻ "GuardLayer")
// useUpperBodyMask  : ��ü ���� ����� true, ���� ����� false
// upperBodyMask     : ��ü ����ũ (useUpperBodyMask�� true�� �� ����)
//
// guardIdle         : ���� ���� Ŭ��(Loop ON)
// parrySuccess      : �и� ���� Ŭ��(Loop OFF)
// guardBlockHit     : ����� ����/�и����� ���׼�(Loop OFF)
// guardWalkF/B/L/R  : ���� �̵� 4���� Ŭ��(Loop ON)
//
// [�Ķ����(��ҹ��� �߿�)]
// Bool    IsGuarding
// Trigger Parry
// Trigger GuardBlock
// Float   speed   �� �� �ҹ��� ���� (������Ʈ �ٸ� ��ũ��Ʈ�� ����)
// Float   MoveX
// Float   MoveY
//
// [������Ʈ��]
// GuardMove : 2D Freeform Directional (MoveX, MoveY)
//   F(0,+1), B(0,-1), L(-1,0), R(+1,0)
//
// [��ȯ]
// Any �� ParrySuccess (����: Parry, dur 0.03)
// Any �� GuardBlockHit(����: GuardBlock, dur 0.05)
// GuardIdle �� GuardMove (����: speed > 0.1 / �� 0.1, dur 0.05)
//
// [����]
// - Base Layer/���� ���̾�� ����/�������� ����.
// - GuardLayer�� ����/����.
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
    const string PARAM_SPEED = "speed"; // �� �ҹ���
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

        // 1) �Ķ���� ����
        EnsureParameter(PARAM_IS_GUARDING, AnimatorControllerParameterType.Bool);
        EnsureParameter(PARAM_PARRY, AnimatorControllerParameterType.Trigger);
        EnsureParameter(PARAM_BLOCK, AnimatorControllerParameterType.Trigger);
        EnsureParameter(PARAM_SPEED, AnimatorControllerParameterType.Float);
        EnsureParameter(PARAM_MOVEX, AnimatorControllerParameterType.Float);
        EnsureParameter(PARAM_MOVEY, AnimatorControllerParameterType.Float);

        // 2) ���̾� ����/����
        int layerIndex = GetOrCreateLayer(guardLayerName);
        var layer = controller.layers[layerIndex];
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 0f; // ������ 0 (�ڵ忡�� 0��1 ����)
        if (useUpperBodyMask && upperBodyMask) layer.avatarMask = upperBodyMask;
        controller.layers[layerIndex] = layer;

        // 3) ������Ʈ�ӽ� & ������Ʈ
        var sm = controller.layers[layerIndex].stateMachine;
        var guardIdleState = GetOrCreateState(sm, "GuardIdle", guardIdle);
        var parrySuccessState = GetOrCreateState(sm, "ParrySuccess", parrySuccess);
        var guardBlockHitState = GetOrCreateState(sm, "GuardBlockHit", guardBlockHit);

        // 4) GuardMove ������Ʈ��(2D Freeform Directional)
        var guardMoveState = GetOrCreateBlendTree2DFD(
            sm, "GuardMove", PARAM_MOVEX, PARAM_MOVEY,
            new[] {
                (clip: guardWalkF,   pos: new Vector2( 0f,  1f)),
                (clip: guardWalkB,   pos: new Vector2( 0f, -1f)),
                (clip: guardStrafeL, pos: new Vector2(-1f,  0f)),
                (clip: guardStrafeR, pos: new Vector2( 1f,  0f)),
            });

        // 5) ��ȯ
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
