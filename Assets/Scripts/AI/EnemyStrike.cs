using System;
using UnityEngine;

// 잡몹 근접 일격 데이터. 모든 시간은 게임 시간(Time.time, GameTimeScale 반영) 기준 초.
//  공격 시작 ──(windup)──▶ 판정 ──(recovery)──▶ 다음 일격/종료
//  큐(cueLead > 0)는 판정 정확히 cueLead 초 전에 발행된다 (EnemyBrain 이 같은 시계로 예약 → 정직).
//  판정 범위 = 소유자 전방 사각형 [0, range] × width (바닥 데칼과 동일한 모양).
[Serializable]
public class EnemyStrike
{
    public string name = "Strike";
    [Tooltip("Animator 스테이트 이름 (Base Layer)")]
    public string animState = "Atk1";
    [Min(0.05f)] public float windup = 0.55f;
    [Tooltip("0 = 큐 없음. 아니면 판정 전 이 시간에 큐 (windup 이하로 잘림).")]
    [Min(0f)] public float cueLead = 0f;
    public EnemyCueType cueType = EnemyCueType.Normal;
    [Min(0f)] public float damage = 10f;
    public HitType hitType = HitType.Normal;
    [Min(0.3f)] public float range = 2.2f;
    [Min(0.3f)] public float width = 2.2f;
    [Tooltip("판정 이 시간 전부터 방향 고정 (옆으로 피할 여지)")]
    [Min(0f)] public float trackLockBeforeHit = 0.25f;
    [Min(0f)] public float trackTurnRate = 300f;
    [Tooltip("판정 직전 전진 거리 (m)")]
    [Min(0f)] public float lungeDistance = 0f;
    [Min(0.05f)] public float lungeDuration = 0.25f;
    [Tooltip("판정 후 다음 행동까지 (빈틈)")]
    [Min(0f)] public float recovery = 0.6f;
    [Tooltip("클립 안에서 칼이 닿는 시각 (빌더가 손 속도 피크로 베이크). 0 이면 재생 속도 1.")]
    [Min(0f)] public float clipImpactTime = 0f;
    [Tooltip("windup 동안 경직 저항 (맞아도 끊기지 않음)")]
    public bool hyperArmor = false;

    [NonSerialized] public int animHash;

    public float EffectiveCueLead => cueLead <= 0f ? 0f : Mathf.Min(cueLead, windup);
}
