// SlashSequenceClip.cs
using UnityEngine;
using UnityEngine.Playables;

[System.Serializable]
public class SlashSequenceClip : PlayableAsset
{
    public ExposedReference<UltimateSlashBurstSpawner> spawner;

    [Min(1)] public int total = 15;
    [Min(0f)] public float interval = 0.05f;  // 각 타 사이 간격(초)
    public int patternSeed = 13579;
    public float slashLife = 0.7f;

    // 타임라인에 올렸을 때 클립 길이를 자동으로 보정하면 편함
    public override double duration => Mathf.Max(0.0001f, total * interval);

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<SlashSequenceBehaviour>.Create(graph);
        var bhv = playable.GetBehaviour();
        bhv.spawner = spawner.Resolve(graph.GetResolver());
        bhv.total = total;
        bhv.interval = interval;
        bhv.patternSeed = patternSeed;
        bhv.slashLife = slashLife;
        return playable;
    }
}

// SlashSequenceBehaviour.cs
