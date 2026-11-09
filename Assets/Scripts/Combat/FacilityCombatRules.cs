// 파일명: FacilityCombatRules.cs
// Level_Facility(본편 보스전) 전용 전투 규칙 프로필. 씬에 하나 두면 시작 시 플레이어/보스에 기획 수치를 적용한다.
// MainScene/Lobby 단독 실행은 이 컴포넌트가 없으므로 기존 동작 그대로.
//  - 궁극기: 전투당 1회 (사용 후 코어 소진), 수급 명중 +3 / 패링 +6 / 퍼펙트 회피 +6 (전투 설계 기획서)
//  - 엘리베이터 하강(하층 도착) = 회복 앰플 4개 충전 + 궁극기 +30% (레벨 디자인 v1.0).
//    보스전 사망 → 기존 체크포인트(격납고 입구에서 재시작, 인트로 생략)로 다시 로드되면 같은 이벤트로 앰플 4 + 코어 30% 상태가 된다.
//  - 연속 회피: 짧은 시간 안 3번째 회피부터 무적 0.24 → 0.12초
//  - 보스: 브레이크 5초(피니셔 창은 그 안), 3타 패링 무력화, 특수 패턴(충격파/그림자 도약), 반응형 조건, 피해 균열
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FacilityCombatRules : MonoBehaviour
{
    [Header("Ultimate")]
    [SerializeField] bool ultimateOncePerFight = true;
    [Tooltip("반복 10: 3 → 2.5 (약한 플레이어가 궁극기 한 방으로 이기던 문제)")]
    [SerializeField] float gaugePerHit = 2.5f;
    [SerializeField] float gaugePerParry = 6f;
    [SerializeField] float gaugePerPerfectDodge = 6f;
    [Tooltip("궁극기 총 피해 = 보스 최대 HP 의 이 비율 (처형 보너스 별도)")]
    [SerializeField, Range(0f, 1f)] float ultimateDamagePercentOfMaxHP = 0.25f; // 반복 10: 0.3 → 0.25 (0.4 봇이 궁극기로 이기던 문제)

    [Header("Elevator / Respawn")]
    [SerializeField] bool refillAmpoulesOnLowerFloor = true;
    [SerializeField, Range(0f, 100f)] float elevatorUltimateGain = 30f;

    [Header("Dodge")]
    [SerializeField] bool rapidDodgeIFrameDecay = true;
    [SerializeField, Range(2, 6)] int rapidDodgeThreshold = 3;
    [SerializeField, Range(0.4f, 3f)] float rapidDodgeWindow = 1.4f;
    [SerializeField, Range(0f, 0.6f)] float rapidDodgeIFrames = 0.12f;

    [Header("Boss")]
    [SerializeField, Min(0.5f)] float breakDuration = 5f;
    [SerializeField, Min(0.5f)] float finisherWindow = 4.2f;
    [SerializeField] bool thirdHitParryStagger = true;
    [SerializeField] bool specialPatterns = true;
    [SerializeField] bool reactivePatterns = true;
    [SerializeField] bool damageCracks = true;
    [Tooltip("반복 10: 보스전 보스 공격력 배율 (봇 0.4 대부분 패배 / 0.6 위협 / 0.9 승리 기준으로 조정)")]
    [SerializeField, Range(0.5f, 2f)] float bossDamageScale = 1.2f;

    public static FacilityCombatRules Active { get; private set; }
    public int LowerFloorRefills { get; private set; }

    bool _playerApplied;
    bool _bossApplied;

    void Awake()
    {
        Active = this;
        // Update 는 적용 후 꺼지므로 이벤트 구독은 Awake/OnDestroy 에서 관리
        LobbyManager.LowerFloorReached += HandleLowerFloorReached;
    }

    void OnDestroy()
    {
        LobbyManager.LowerFloorReached -= HandleLowerFloorReached;
        if (Active == this)
            Active = null;
    }

    void Start()
    {
        ApplyAll();
    }

    void Update()
    {
        // 플레이어/보스가 늦게 생성·활성화되는 경우 대비 (적용되면 끝)
        if (!_playerApplied || !_bossApplied)
            ApplyAll();
        else
            enabled = false;
    }

    void ApplyAll()
    {
        if (!_playerApplied)
            _playerApplied = ApplyPlayer();
        if (!_bossApplied)
            _bossApplied = ApplyBoss();
    }

    bool ApplyPlayer()
    {
        PlayerReferences refs = FindObjectOfType<PlayerReferences>();
        if (refs == null)
            return false;

        PlayerUltimateController ultimate = refs.GetComponentInChildren<PlayerUltimateController>(true);
        if (ultimate != null)
        {
            ultimate.OncePerFight = ultimateOncePerFight;
            ultimate.gaugePerH = gaugePerHit;
        }

        UltimateHitProcessor hitProcessor = refs.GetComponentInChildren<UltimateHitProcessor>(true);
        if (hitProcessor != null)
            hitProcessor.BaseDamagePercentOfMaxHP = ultimateDamagePercentOfMaxHP;

        PlayerGuardController guard = refs.GetComponentInChildren<PlayerGuardController>(true);
        if (guard != null)
            guard.ultimateGainOnParry = gaugePerParry;

        PerfectDodgeController perfect = refs.GetComponentInChildren<PerfectDodgeController>(true);
        if (perfect != null)
            perfect.ultimateGainOnPerfect = gaugePerPerfectDodge;

        PlayerDodgeController dodge = refs.GetComponentInChildren<PlayerDodgeController>(true);
        if (dodge != null)
        {
            dodge.ReduceIFramesOnRapidChain = rapidDodgeIFrameDecay;
            dodge.RapidChainThreshold = rapidDodgeThreshold;
            dodge.RapidChainWindow = rapidDodgeWindow;
            dodge.RapidChainInvulnerabilityDuration = rapidDodgeIFrames;
        }

        return true;
    }

    bool ApplyBoss()
    {
        BossController boss = FindObjectOfType<BossController>(true);
        if (boss == null)
            return false;

        boss.EnableSpecialPatterns = specialPatterns;
        boss.EnablePlanReactiveBias = reactivePatterns;
        boss.EnableThirdHitParryStagger = thirdHitParryStagger;
        boss.ScaleOutgoingDamage(bossDamageScale);

        BossBreakController breakController = boss.GetComponentInChildren<BossBreakController>(true) ?? boss.GetComponentInParent<BossBreakController>();
        if (breakController != null)
        {
            breakController.breakDuration = breakDuration;
            breakController.finisherWindowSeconds = Mathf.Min(finisherWindow, breakDuration);
        }

        if (damageCracks && boss.GetComponent<BossDamageCracks>() == null)
            boss.gameObject.AddComponent<BossDamageCracks>();

        return true;
    }

    void HandleLowerFloorReached(LobbyManager lobby)
    {
        ApplyAll();
        PlayerReferences refs = FindObjectOfType<PlayerReferences>();
        if (refs == null)
            return;

        LowerFloorRefills++;
        if (refillAmpoulesOnLowerFloor)
        {
            PlayerConsumables consumables = refs.GetComponentInChildren<PlayerConsumables>(true);
            if (consumables != null)
                consumables.Refill();
        }

        PlayerUltimateController ultimate = refs.GetComponentInChildren<PlayerUltimateController>(true);
        if (ultimate != null && elevatorUltimateGain > 0f)
        {
            // 새 전투(보스전) 시작: 소진 해제 + 상층에서 모은 게이지에 30% 추가. 체크포인트 재시작이면 0 → 30%.
            float target = Mathf.Min(ultimate.gaugeMax, ultimate.Gauge + ultimate.gaugeMax * elevatorUltimateGain / 100f);
            ultimate.ResetForNewFight(target);
        }

        HealFeedbackHUD.ShowNotice("MAINTENANCE // RECHARGE", (refillAmpoulesOnLowerFloor ? "회복 앰플 충전 · " : "") + "페이즈 코어 +" + Mathf.RoundToInt(elevatorUltimateGain) + "%");
    }
}
