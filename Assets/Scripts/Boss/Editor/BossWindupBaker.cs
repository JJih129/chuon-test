using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// BossController 를 쓰는 AnimatorController 들의 "트리거 → 첫 히트 이벤트" 시간을 읽어
/// Assets/Resources/Boss/BossWindupBake.asset 에 저장한다. 수동 메뉴 + 빌드 직전(IPreprocessBuildWithReport) 실행.
/// (로드 시 자동 실행 없음)
/// </summary>
public sealed class BossWindupBaker : IPreprocessBuildWithReport
{
    const string AssetPath = "Assets/Resources/Boss/BossWindupBake.asset";
    static readonly string[] HitEventNames = { "Anim_ActivateHitbox", "ActivateHitbox" };

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        Bake(false);
    }

    [MenuItem("Tools/ChuOn/Bake Boss Wind-ups")]
    static void BakeMenu()
    {
        Debug.Log("[BossWindupBaker] " + Bake(true));
    }

    public static string Bake(bool verbose)
    {
        var entries = new List<BossWindupBakeData.Entry>();
        var controllers = new HashSet<AnimatorController>();
        // 프로젝트의 모든 AnimatorController (런타임 조회는 컨트롤러 이름 + 트리거 키라 다른 컨트롤러가 섞여도 무해)
        foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController"))
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            if (controller != null)
                controllers.Add(controller);
        }

        var sb = new System.Text.StringBuilder();
        foreach (AnimatorController controller in controllers)
        {
            var seen = new HashSet<string>();
            foreach (AnimatorControllerLayer layer in controller.layers)
                CollectStateMachine(controller, layer.stateMachine, entries, seen, sb, verbose);
        }

        var data = AssetDatabase.LoadAssetAtPath<BossWindupBakeData>(AssetPath);
        bool created = data == null;
        if (created)
        {
            data = ScriptableObject.CreateInstance<BossWindupBakeData>();
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Boss"))
                AssetDatabase.CreateFolder("Assets/Resources", "Boss");
        }

        bool changed = created || !SameEntries(data.entries, entries);
        if (changed)
        {
            data.entries = entries;
            if (created)
                AssetDatabase.CreateAsset(data, AssetPath);
            else
                EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        return $"{entries.Count} trigger wind-ups baked (changed={changed})\n{sb}";
    }

    static void CollectStateMachine(AnimatorController controller, AnimatorStateMachine machine, List<BossWindupBakeData.Entry> entries, HashSet<string> seen, System.Text.StringBuilder sb, bool verbose)
    {
        if (machine == null)
            return;

        foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
            CollectTransition(controller, transition, entries, seen, sb, verbose);

        foreach (ChildAnimatorState child in machine.states)
            foreach (AnimatorStateTransition transition in child.state.transitions)
                CollectTransition(controller, transition, entries, seen, sb, verbose);

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
            CollectStateMachine(controller, sub.stateMachine, entries, seen, sb, verbose);
    }

    static void CollectTransition(AnimatorController controller, AnimatorStateTransition transition, List<BossWindupBakeData.Entry> entries, HashSet<string> seen, System.Text.StringBuilder sb, bool verbose)
    {
        AnimatorState state = transition.destinationState;
        if (state == null || !(state.motion is AnimationClip clip))
            return;

        float firstHit = float.MaxValue;
        foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(clip))
        {
            if (System.Array.IndexOf(HitEventNames, e.functionName) >= 0 && e.time < firstHit)
                firstHit = e.time;
        }

        if (firstHit == float.MaxValue)
            return;

        float speed = Mathf.Max(0.05f, Mathf.Abs(state.speed));
        float startOffset = transition.offset * clip.length;
        float seconds = Mathf.Max(0f, firstHit - startOffset) / speed;
        foreach (AnimatorCondition condition in transition.conditions)
        {
            if (!IsTrigger(controller, condition.parameter) || !seen.Add(condition.parameter))
                continue;

            entries.Add(new BossWindupBakeData.Entry
            {
                controllerName = controller.name,
                triggerName = condition.parameter,
                stateName = state.name,
                clipName = clip.name,
                firstHitSeconds = seconds
            });
            if (verbose)
                sb.Append(controller.name).Append(' ').Append(condition.parameter).Append(" -> ").Append(state.name)
                  .Append(" (").Append(clip.name).Append(") ").Append(seconds.ToString("F3")).Append("s\n");
        }
    }

    static bool IsTrigger(AnimatorController controller, string parameter)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
            if (p.name == parameter)
                return p.type == AnimatorControllerParameterType.Trigger;
        return false;
    }

    static bool SameEntries(List<BossWindupBakeData.Entry> a, List<BossWindupBakeData.Entry> b)
    {
        if (a == null || a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].controllerName != b[i].controllerName || a[i].triggerName != b[i].triggerName
                || a[i].stateName != b[i].stateName || a[i].clipName != b[i].clipName
                || !Mathf.Approximately(a[i].firstHitSeconds, b[i].firstHitSeconds))
                return false;
        }
        return true;
    }
}
