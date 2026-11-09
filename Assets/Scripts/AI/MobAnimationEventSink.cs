using UnityEngine;

// 잡몹 Animator(Visual) 용 애니메이션 이벤트 수신기.
// GhostSamurai 이동 클립(보스와 공용)에 박힌 발소리 이벤트가 "no receiver" 경고를 매 걸음 쏟지 않게 받는다.
// footstep 클립을 지정하면 아주 작게 재생 (기본 비움 = 무음).
[DisallowMultipleComponent]
public sealed class MobAnimationEventSink : MonoBehaviour
{
    [SerializeField] AudioClip footstep;
    [SerializeField, Range(0f, 1f)] float footstepVolume = 0.25f;

    public void PlayStrafeFootstepLeft() => Step();
    public void PlayStrafeFootstepRight() => Step();

    void Step()
    {
        if (footstep != null)
            OneShotAudio.PlayAt(footstep, transform.position, footstepVolume, Random.Range(0.94f, 1.06f), 0.85f);
    }
}
