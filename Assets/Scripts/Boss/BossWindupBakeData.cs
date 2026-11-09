using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 공격 트리거 → 첫 히트(Anim_ActivateHitbox) 시간의 사전 계산값 (에디터 베이크).
/// BossController.HonestCues 가 패턴을 처음 볼 때도 사전 텔레그래프를 정직하게 내도록 학습값 대신 시드로 쓴다.
/// 생성: Tools/ChuOn/Bake Boss Wind-ups (빌드 전에도 자동 갱신) → Assets/Resources/Boss/BossWindupBake.asset
/// </summary>
public sealed class BossWindupBakeData : ScriptableObject
{
    public const string ResourcePath = "Boss/BossWindupBake";

    [System.Serializable]
    public struct Entry
    {
        public string controllerName;
        public string triggerName;
        public string stateName;
        public string clipName;
        [Tooltip("트리거 반영 후 첫 히트 이벤트까지 (클립 시간 / 스테이트 속도, 애니메이터 속도 1 기준, 초).")]
        public float firstHitSeconds;
    }

    public List<Entry> entries = new List<Entry>();

    static BossWindupBakeData s_cached;
    static bool s_loaded;

    public static BossWindupBakeData Instance
    {
        get
        {
            if (!s_loaded)
            {
                s_loaded = true;
                s_cached = Resources.Load<BossWindupBakeData>(ResourcePath);
            }

            return s_cached;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_cached = null;
        s_loaded = false;
    }

    public bool TryGet(RuntimeAnimatorController controller, string trigger, out float seconds)
    {
        seconds = 0f;
        if (controller == null || string.IsNullOrEmpty(trigger))
            return false;

        string name = controller.name;
        string baseName = controller is AnimatorOverrideController overrideController && overrideController.runtimeAnimatorController != null
            ? overrideController.runtimeAnimatorController.name
            : name;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.triggerName == trigger && (e.controllerName == name || e.controllerName == baseName))
            {
                seconds = e.firstHitSeconds;
                return true;
            }
        }

        return false;
    }
}
