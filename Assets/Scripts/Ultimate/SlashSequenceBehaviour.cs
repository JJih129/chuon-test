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
        if (spawner == null || emitted >= total)
            return;

        float t = (float)playable.GetTime();
        Transform originRoot = spawner.transform;
        Vector3 origin = originRoot.position;
        Vector3 focusPoint = origin + originRoot.forward * 2f;

        while (emitted < total && t >= nextTime)
        {
            spawner.EmitOneSlash(emitted, total, patternSeed, origin, focusPoint, slashLife);
            emitted++;
            nextTime += interval;
        }
    }
}