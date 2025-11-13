using UnityEngine;

namespace Combat
{
    // ▶ 입력 종류(약/강) — 기획서 입력 스킴과 동일
    public enum AttackInput { Light, Heavy }

    // ▶ 분기 테이블 엔트리 (입력 타입 -> 다음 공격 데이터)
    [System.Serializable]
    public struct ComboNextEntry
    {
        [Header("▶ 어떤 입력으로 다음 타로 갈지 (약/강)")]
        public AttackInput input;

        [Header("▶ 해당 입력 시 이어질 다음 공격 데이터")]
        public AttackData next;
    }

    [CreateAssetMenu(menuName = "Combat/Attack Data", fileName = "AttackData")]
    public class AttackData : ScriptableObject
    {
        [Header("▶ 애니메이터 스테이트명 (Action 레이어의 State 이름과 일치)")]
        public string stateName;

        [Header("▶ 재생 레이어 인덱스 (공격 레이어, 보통 1)")]
        public int animatorLayer = 1;

        [Header("▶ 모션 재생 속도 배수 (1=기본, 1.1~1.2로 체감 가속)")]
        public float playSpeed = 1.0f;

        [Header("▶ 히트 타이밍(정규화 0~1) — AE_Hit() 동기화용")]
        [Range(0f, 1f)] public float hitTime = 0.35f;

        [Header("▶ 캔슬 시작/종료(정규화 0~1) — 이 구간 입력 시 다음 타로 전이")]
        [Range(0f, 1f)] public float cancelStart = 0.5f;
        [Range(0f, 1f)] public float cancelEnd   = 0.9f;

        [Header("▶ 기본 데미지 (밸런싱용)")]
        public float baseDamage = 10f;

        [Header("▶ 디버그 라벨 (예: L1, L2_in_H, H4_in_LLLHH 등)")]
        public string variantTag;

        [Header("▶ 입력별 다음 타(분기 테이블)")]
        public ComboNextEntry[] nextByInput;
    }
}