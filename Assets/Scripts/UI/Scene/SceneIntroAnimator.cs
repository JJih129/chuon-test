using System.Collections;
using UnityEngine;

/// <summary>
/// 메인 씬 시작 시, 설정한 애니메이션들을
/// 순서대로 일정 딜레이를 두고 재생해주는 컴포넌트
/// </summary>
public class SceneIntroAnimator : MonoBehaviour
{
    [Header("애니메이션이 재생될 대상 애니메이터 (플레이어 / 카메라 / UI 등)")]
    [SerializeField] private Animator targetAnimator;

    [System.Serializable]
    public class IntroAnimation
    {
        [Header("재생할 애니메이션 상태/트리거 이름 (Animator 내부 이름)")]
        [Tooltip("Animator의 State 이름 또는 Trigger 이름을 입력합니다.")]
        public string stateOrTriggerName;

        [Header("트리거 사용 여부 (true = SetTrigger, false = Play로 직접 상태 진입)")]
        [Tooltip("true면 Animator.SetTrigger, false면 Animator.Play를 사용합니다.")]
        public bool useTrigger = true;

        [Header("다음 애니메이션까지 대기 시간 (초 단위)")]
        [Tooltip("이 애니메이션을 실행한 뒤 다음 단계까지 기다릴 시간(초)")]
        [Min(0f)]
        public float delayToNext = 1f;
    }

    [Header("인트로 애니메이션 시퀀스 (위에서 아래 순서대로 재생)")]
    [Tooltip("씬 시작 시 순서대로 재생할 애니메이션 목록입니다.")]
    [SerializeField] private IntroAnimation[] sequence;

    [Header("씬 시작 시 자동 재생 여부")]
    [Tooltip("true면 Start()에서 자동으로 시퀀스를 재생합니다.")]
    [SerializeField] private bool playOnStart = true;

    // 현재 재생 중인 코루틴 핸들 (중간에 정지/재시작 용도)
    private Coroutine sequenceRoutine;

    private void Start()
    {
        // 메인 씬 시작과 동시에 자동 재생 옵션이 켜져 있다면 시퀀스 시작
        if (playOnStart)
        {
            PlaySequence();
        }
    }

    /// <summary>
    /// 인트로 애니메이션 시퀀스를 외부에서 수동으로 시작할 때 호출
    /// (예: 다른 매니저에서 컷씬 이후 호출)
    /// </summary>
    [ContextMenu("Play Sequence (Test)")]
    public void PlaySequence()
    {
        if (targetAnimator == null)
        {
            Debug.LogWarning("[SceneIntroAnimator] Target Animator가 설정되어 있지 않습니다.");
            return;
        }

        // 이전에 돌고 있던 시퀀스가 있다면 정지
        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
        }

        // 새로운 시퀀스 코루틴 시작
        sequenceRoutine = StartCoroutine(PlaySequenceRoutine());
    }

    /// <summary>
    /// 인트로 시퀀스를 순서대로 실행하는 실제 코루틴
    /// </summary>
    private IEnumerator PlaySequenceRoutine()
    {
        // 배열에 설정된 순서대로 애니메이션 실행
        for (int i = 0; i < sequence.Length; i++)
        {
            IntroAnimation step = sequence[i];

            // 이름이 비어 있으면 스킵 (실수 방지용)
            if (string.IsNullOrEmpty(step.stateOrTriggerName))
                continue;

            // 트리거 방식 vs 직접 Play 방식 선택
            if (step.useTrigger)
            {
                // Animator Controller에서 같은 이름의 Trigger 파라미터가 있어야 함
                // 존재하지 않는 파라미터 이름으로 SetTrigger를 계속 호출하면
                // 런타임 에러는 아니지만, 디버깅이 어렵고 성능 낭비가 될 수 있음.
                targetAnimator.SetTrigger(step.stateOrTriggerName);
            }
            else
            {
                // State 이름으로 직접 진입
                // 이 경우 Layer, NormalizedTime 등을 세부 조정하고 싶으면
                // 오버로드(Play(string stateName, int layer, float time)) 사용 가능
                targetAnimator.Play(step.stateOrTriggerName);
            }

            // 다음 단계까지 대기
            if (step.delayToNext > 0f)
            {
                yield return new WaitForSeconds(step.delayToNext);
            }
            else
            {
                // 0 이하일 경우 한 프레임은 넘기고 바로 다음 단계로
                yield return null;
            }
        }

        // 시퀀스가 끝났으므로 코루틴 핸들 정리
        sequenceRoutine = null;
    }

    /// <summary>
    /// 재생 중인 시퀀스를 즉시 중단하고 싶을 때 사용
    /// (예: 플레이어가 스킵 버튼을 눌렀을 때)
    /// </summary>
    public void StopSequence()
    {
        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }
    }
}
