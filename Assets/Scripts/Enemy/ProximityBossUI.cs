// ProximityBossUI.cs
using UnityEngine;

/// 적 프리팹에 붙여서 IntegratedBossUI에 자동 등록되게 하는 컴포넌트.
/// healthBehaviour : 프로젝트에서 사용하는 체력 스크립트(예: EnemyHealth)
public class ProximityBossUI : MonoBehaviour
{
    [Header("▶ 타겟 피벗 | 머리 위 위치 등")]
    [Tooltip("World-space HP바가 붙을 Transform. 비워두면 이 오브젝트 사용.")]
    public Transform pivot;

    [Header("▶ 체력 컴포넌트(연동)")]
    [Tooltip("IHealth를 구현한 컴포넌트를 드래그하세요. 인터페이스가 아니더라도 이름이 유사하면 Bind 시도합니다.")]
    public MonoBehaviour healthBehaviour;

    [Header("▶ 월드바 오프셋")]
    [Tooltip("월드 HP바의 로컬 오프셋(머리 위 마진 등)")]
    public Vector3 worldOffset = new Vector3(0f, 1.0f, 0f);

    void OnEnable()
    {
        if (pivot == null) pivot = this.transform;
        IntegratedBossUI.Register(this);
    }
    void OnDisable()
    {
        IntegratedBossUI.Unregister(this);
    }
}
