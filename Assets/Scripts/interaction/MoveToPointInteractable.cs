using UnityEngine;
using System.Collections;

// [헤더] 플레이어를 지정 지점으로 이동시키는 상호작용
// - targetPoint: 이동 목적지(Transform). 필수
// - faceTargetForward: 도착 후 targetPoint의 앞을 보게 회전
// - moveMode: 이동 방식(즉시 워프 / 부드럽게 이동)
// - moveDuration: 부드럽게 이동시 소요 시간(초)
// - useNavMeshAgentIfFound: 플레이어에 NavMeshAgent 있으면 SetDestination 사용
// - blockInputDuringMove: 이동 중 입력 차단(프로젝트 라우터 있으면 Block, 없으면 무시)
// - playSfx: SFX 재생 여부
// - sfx: AudioSource 참조
public class MoveToPointInteractable : BaseInteractable
{
    public Transform targetPoint;
    public bool faceTargetForward = true;

    public enum MoveMode { Warp, Smooth }
    public MoveMode moveMode = MoveMode.Smooth;

    public float moveDuration = 0.6f;
    public bool useNavMeshAgentIfFound = true;
    public bool blockInputDuringMove = true;

    public bool playSfx = false;
    public AudioSource sfx;

    public override bool TryInteract(object invoker = null)
    {
        if (!targetPoint) return false;

        // InteractionManager로부터 플레이어 획득
        Transform player = null;
        if (invoker is InteractionManager im) player = im.PlayerRoot;
        if (!player)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) player = go.transform;
        }
        if (!player) return false;

        StartCoroutine(CoMove(player));
        return true;
    }

    IEnumerator CoMove(Transform player)
    {
        if (playSfx && sfx && sfx.clip) sfx.PlayOneShot(sfx.clip, AudioOptionsRuntime.ScaleSfx(sfx.volume));

        var interactionManager = FindFirstObjectByType<InteractionManager>();
        var localInputBlocker = player.GetComponent<IInputBlocker>() ?? player.GetComponentInParent<IInputBlocker>();
        var inputBlocked = false;

        if (blockInputDuringMove)
            inputBlocked = TrySetInputBlocked(interactionManager, localInputBlocker, true);

        // NavMeshAgent 우선
        if (useNavMeshAgentIfFound)
        {
            var agent = player.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent && agent.isActiveAndEnabled)
            {
                agent.SetDestination(targetPoint.position);
                // 간단한 도착 대기
                float timeout = 3f;
                while (timeout > 0f && agent.pathPending) { timeout -= Time.deltaTime; yield return null; }
                while (agent.remainingDistance > agent.stoppingDistance + 0.05f) { yield return null; }
                if (faceTargetForward)
                    player.rotation = Quaternion.LookRotation(targetPoint.forward, Vector3.up);

                if (inputBlocked)
                    TrySetInputBlocked(interactionManager, localInputBlocker, false);
                yield break;
            }
        }

        if (moveMode == MoveMode.Warp)
        {
            player.position = targetPoint.position;
            if (faceTargetForward)
                player.rotation = Quaternion.LookRotation(targetPoint.forward, Vector3.up);
        }
        else // Smooth
        {
            Vector3 startPos = player.position;
            Quaternion startRot = player.rotation;
            Quaternion endRot = faceTargetForward ? Quaternion.LookRotation(targetPoint.forward, Vector3.up) : startRot;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.001f, moveDuration);
                player.position = Vector3.Lerp(startPos, targetPoint.position, t);
                player.rotation = Quaternion.Slerp(startRot, endRot, t);
                yield return null;
            }
        }

        if (inputBlocked)
            TrySetInputBlocked(interactionManager, localInputBlocker, false);
    }

    static bool TrySetInputBlocked(InteractionManager interactionManager, IInputBlocker inputBlocker, bool blocked)
    {
        if (interactionManager != null && interactionManager.SetInteractionInputBlocked(blocked))
            return true;

        if (inputBlocker != null)
        {
            inputBlocker.BlockAll(blocked);
            return true;
        }

        return false;
    }
}

