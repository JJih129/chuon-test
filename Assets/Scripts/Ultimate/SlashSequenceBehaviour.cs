using UnityEngine;
using UnityEngine.Playables;

public class SlashSequenceBehaviour : PlayableBehaviour
{
    public UltimateSlashBurstSpawner spawner;
    public int total = 15;
    public float interval = 0.05f;
    public int patternSeed = 13579;
    public float slashLife = 0.7f;

    int emitted;
    float nextTime;

    public override void OnGraphStart(Playable playable)
    {
        emitted = 0;
        nextTime = 0f;
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (spawner == null || emitted >= total) return;

        // playable.GetTime()는 클립 시작 기준 시간(초)
        float t = (float)playable.GetTime();

        while (emitted < total && t >= nextTime)
        {
            spawner.EmitOneSlash(emitted, total, patternSeed, slashLife);
            emitted++;
            nextTime += interval;
        }
    }
}
