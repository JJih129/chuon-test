using UnityEngine;

// UI 바인딩: 락온된 적의 IHealth를 받아서 EnemyHPBarPresenter에 연결/해제
public class LockOnUIBinder : MonoBehaviour
{
    [Header("Presenter")]
    [SerializeField] private EnemyHPBarPresenter hpPresenter;

    [Header("Default offset")]
    [SerializeField] private Vector3 defaultOffset = new Vector3(0f, 2f, 0f);

    // 현재 바인드된 대상 정보 (optional, 디버깅/관리용)
    private IHealth boundHealth;
    private Transform boundTransform;
    private Vector3 boundOffset;

    private void Reset()
    {
        // 가능하면 에디터에서 Presenter를 끌어다 놓도록 유도
        hpPresenter = GetComponentInChildren<EnemyHPBarPresenter>();
    }

    /// <summary>
    /// 타겟을 바인드해서 HP 바를 연결합니다.
    /// 반드시 targetTransform(월드 위치 계산에 쓸 Transform)을 함께 전달하세요.
    /// </summary>
    public void AttachTarget(IHealth targetHealth, Transform targetTransform, Vector3? offset = null)
    {
        if (hpPresenter == null)
        {
            Debug.LogWarning("[LockOnUIBinder] hpPresenter is null. Assign in inspector.");
            return;
        }

        if (targetHealth == null || targetTransform == null)
        {
            Debug.LogWarning("[LockOnUIBinder] AttachTarget called with null arguments.");
            return;
        }

        // 실제 Attach 시그니처에 맞춰 호출
        Vector3 useOffset = offset ?? defaultOffset;
        hpPresenter.Attach(targetHealth, targetTransform, useOffset);

        // 로컬 상태 저장
        boundHealth = targetHealth;
        boundTransform = targetTransform;
        boundOffset = useOffset;
    }

    /// <summary>
    /// 현재 바인드 해제
    /// </summary>
    public void DetachCurrent()
    {
        if (hpPresenter == null) return;

        hpPresenter.Detach();
        boundHealth = null;
        boundTransform = null;
    }

    // 예시: 외부에서 호출하는 방식
    // PlayerLockOn 같은 곳에서 락온 시작 시:
    //    lockOnUIBinder.AttachTarget(targetHealthComponent, targetGameObject.transform);
    // 락온 해제 시:
    //    lockOnUIBinder.DetachCurrent();
}
