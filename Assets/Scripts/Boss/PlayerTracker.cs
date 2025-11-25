using UnityEngine;
using System;

// BossController와 Pattern.CanExecute에서 PlayerTracker의 데이터를 참조하기 위한 인터페이스
public interface IPlayerTracker
{
    bool IsDodgingRecently();
    bool IsAttackStoppedRecently(float duration);
    bool IsRetreating();
    void SignalPlayerAction(PlayerAction action);
}

// 플레이어 행동 정의
public enum PlayerAction { Attack, Dodge, Idle }

// 파일명: PlayerTracker.cs
public class PlayerTracker : MonoBehaviour, IPlayerTracker
{
    [Header("추적 시간 설정")]
    public float recentTimeThreshold = 3f; // 최근 행동을 판단하는 기준 시간 (3초)
    public int dodgeCountThreshold = 2; // 연속 회피 판단 횟수 (2회)
    public float retreatDurationThreshold = 2f; // 후퇴 지속 시간 (2초)

    private float _lastAttackTime = -10f;
    private float _lastDodgeTime = -10f;
    private int _recentDodgeCount = 0;
    private float _retreatStartTime = -10f;

    // --- IPlayerTracker 구현 ---

    // 사선 베기 조건: 플레이어가 최근 3초 내 회피를 2회 이상 연속 사용했는지
    public bool IsDodgingRecently()
    {
        return (Time.time - _lastDodgeTime <= recentTimeThreshold) && (_recentDodgeCount >= dodgeCountThreshold);
    }

    // 연속 베기 조건: 플레이어가 일정 시간 공격을 멈췄는지
    public bool IsAttackStoppedRecently(float duration)
    {
        return (Time.time - _lastAttackTime >= duration);
    }

    // 대쉬 찌르기 조건: 플레이어가 반복적으로 후퇴할 때 (2m 이상 거리에서 2초 이상)
    public bool IsRetreating()
    {
        return (Time.time - _retreatStartTime >= retreatDurationThreshold);
    }

    // (참고: 도약 내려찍기의 '회복 캡슐 사용' 조건은 외부 이벤트 시스템 연동 필요)

    public void SignalPlayerAction(PlayerAction action)
    {
        float currentTime = Time.time;

        switch (action)
        {
            case PlayerAction.Attack:
                _lastAttackTime = currentTime;
                _retreatStartTime = -10f;
                break;
            case PlayerAction.Dodge:
                if (currentTime - _lastDodgeTime <= 1.0f) // 짧은 시간 내 연속 회피 체크
                {
                    _recentDodgeCount++;
                }
                else
                {
                    _recentDodgeCount = 1;
                }
                _lastDodgeTime = currentTime;
                _retreatStartTime = -10f;
                break;
            case PlayerAction.Idle:
                // 플레이어가 후퇴(뒤로 이동)하기 시작했을 때 이 신호를 받도록 로직을 구성해야 합니다.
                if (_retreatStartTime < 0) _retreatStartTime = currentTime;
                break;
        }
    }
}