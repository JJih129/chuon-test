using System.Collections;
using UnityEngine;

public class AnimationLockManager : MonoBehaviour
{
    public Animator animator;
    Coroutine[] blendCoroutines = new Coroutine[8];
    bool isLocked = false;
    PlayerReferences playerReferences;

    void Awake()
    {
        playerReferences = GetComponent<PlayerReferences>();
        if (animator == null)
            animator = playerReferences != null
                ? playerReferences.MainAnimator ?? GetComponentInChildren<Animator>()
                : GetComponentInChildren<Animator>();
    }

    // 부드럽게 레이어 가중치 요청
    public void RequestLayerWeight(int layerIndex, float targetWeight, float blendTime)
    {
        if (blendCoroutines[layerIndex] != null) StopCoroutine(blendCoroutines[layerIndex]);
        blendCoroutines[layerIndex] = StartCoroutine(BlendLayerWeight(layerIndex, targetWeight, blendTime));
    }

    IEnumerator BlendLayerWeight(int layerIndex, float target, float t)
    {
        float start = animator.GetLayerWeight(layerIndex);
        float elapsed = 0f;
        while (elapsed < t)
        {
            elapsed += Time.deltaTime;
            float w = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / t));
            animator.SetLayerWeight(layerIndex, w);
            yield return null;
        }
        animator.SetLayerWeight(layerIndex, target);
        blendCoroutines[layerIndex] = null;
    }

    // 즉시 강제 설정(긴급)
    public void ForceSetLayerWeight(int layerIndex, float weight)
    {
        if (blendCoroutines[layerIndex] != null)
        {
            StopCoroutine(blendCoroutines[layerIndex]);
            blendCoroutines[layerIndex] = null;
        }
        animator.SetLayerWeight(layerIndex, weight);
    }

    // 요청받은 시간동안 입력/애니 제어 잠금 플래그 (다른 시스템에서 체크 가능)
    public void RequestLockDuringSeconds(float seconds)
    {
        if (!isLocked) StartCoroutine(LockCoroutine(seconds));
    }

    IEnumerator LockCoroutine(float seconds)
    {
        isLocked = true;
        // 필요한 외부 API 호출 포인트: PlayerMovement.SetExternalControl(true) 등 가능
        yield return new WaitForSeconds(seconds);
        isLocked = false;
    }

    public bool IsLocked() => isLocked;
}
