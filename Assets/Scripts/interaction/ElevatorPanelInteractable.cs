using UnityEngine;

// [헤더] 엘리베이터 호출/층 선택 상호작용
// - elevator: 제어할 ElevatorController
// - targetStopIndex: 이동할 정차 인덱스(0부터)
// - usePlayerAsRider: 상호작용한 플레이어를 탑승 처리
// - playSfx: SFX 재생
// - sfx: AudioSource
public class ElevatorPanelInteractable : BaseInteractable
{
    public ElevatorController elevator;
    public int targetStopIndex = 0;
    public bool usePlayerAsRider = true;
    public bool playSfx = false;
    public AudioSource sfx;

    public override string GetPromptText()
    {
        return string.IsNullOrEmpty(promptText) ? $"F: {targetStopIndex}층 호출" : promptText;
    }

    public override bool TryInteract(object invoker = null)
    {
        if (!elevator || elevator.IsBusy) return false;

        Transform rider = null;
        if (usePlayerAsRider)
        {
            if (invoker is InteractionManager im) rider = im.PlayerRoot;
            if (!rider)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go) rider = go.transform;
            }
        }

        if (playSfx && sfx && sfx.clip) sfx.PlayOneShot(sfx.clip, AudioOptionsRuntime.ScaleSfx(sfx.volume));
        elevator.CallTo(targetStopIndex, rider);
        return true;
    }
}
