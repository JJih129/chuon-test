// AnimationLockManager.cs
// 설명: Animator의 특정 레이어에서 상태를 재생할 때 레이어 가중치를 강제로 1로 올리고
// 재생 종료 후 원복. 재생 동안 PlayerLockManager를 통해 전역 입력/행동을 잠근다.
// 사용법: PlayerDamageReceiver 등에서 animationLockManager.PlayStateAndLock(layerName, stateName) 호출.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AnimationLockManager : MonoBehaviour
{
    [Header("Animator (비우면 자동 할당)")]
    public Animator animator;

    // 내부: 레이어별 원래 가중치 저장, 락 카운트
    Dictionary<int, float> _originalWeights = new Dictionary<int, float>();
    Dictionary<int, int> _layerLocks = new Dictionary<int, int>();

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true) ?? GetComponentInParent<Animator>(true) ?? GetComponent<Animator>();
        if (animator == null) Debug.LogWarning("[AnimationLockManager] animator 미할당. 자동 탐색 실패.", this);
    }

    // 스테이트 이름으로 재생하고 자동으로 lock/unlock 함
    public void PlayStateAndLock(string layerName, string stateName)
    {
        if (animator == null)
        {
            Debug.LogWarning("[AnimationLockManager] animator 없음. PlayStateAndLock 스킵.");
            return;
        }
        int layer = animator.GetLayerIndex(layerName);
        if (layer < 0) layer = 0;
        StartCoroutine(PlayAndLockCoroutine(layer, stateName));
    }

    IEnumerator PlayAndLockCoroutine(int layerIndex, string stateName)
    {
        // 레이어 원래 가중치 저장 및 카운트 증가
        if (!_originalWeights.ContainsKey(layerIndex))
            _originalWeights[layerIndex] = animator.GetLayerWeight(layerIndex);
        if (!_layerLocks.ContainsKey(layerIndex)) _layerLocks[layerIndex] = 0;
        _layerLocks[layerIndex]++;

        // 강제 가중치, 전역 락
        animator.SetLayerWeight(layerIndex, 1f);
        PlayerLockManager.Lock();

        // Play state
        int stateHash = Animator.StringToHash(stateName);
        animator.Play(stateHash, layerIndex, 0f);
        // 한 프레임 대기해서 상태가 반영되도록 함
        yield return null;

        // 대기: 스테이트가 나오고 정상적으로 끝날 때까지
        while (true)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layerIndex);
            // 상태가 정확히 매칭되고 재생이 진행 중인 경우(일반적으로 normalizedTime < 1)
            if (info.IsName(stateName))
            {
                // 만약 루프(Loop)면 normalizedTime 계속 증가; 루프이면 시간이 끝나지 않음.
                // 루프 클립인 경우 우리는 한 사이클(>=1) 완료를 기다림.
                if (info.length > 0f)
                {
                    if (info.normalizedTime < 1f)
                    {
                        yield return null;
                        continue;
                    }
                    else break;
                }
                else
                {
                    // 안전: 길이 정보 없으면 한 프레임 대기
                    yield return null;
                    break;
                }
            }
            else
            {
                // 아직 목적 상태에 진입하지 않았으면 기다림
                // 혹은 이미 다른 상태로 바뀌었다면 루프 탈출
                yield return null;
                // 제한 시간/안전장치: 만약 120프레임 넘으면 중단
                // (간단성 위해 생략)
            }
        }

        // 재생 끝. 락 카운트 감소 및 원복 처리
        _layerLocks[layerIndex]--;
        if (_layerLocks[layerIndex] <= 0)
        {
            _layerLocks[layerIndex] = 0;
            if (_originalWeights.TryGetValue(layerIndex, out float orig))
                animator.SetLayerWeight(layerIndex, orig);
            else
                animator.SetLayerWeight(layerIndex, 0f);
        }

        PlayerLockManager.Unlock();
        yield break;
    }
}
