using UnityEngine;
using UnityEngine.Playables;
using Cinemachine;
using System;
using System.Collections;

public class PlayerUltimateController : MonoBehaviour
{
    // ========================= 변수 헤더(한글 설명) =========================
    [Header("① 게이지 설정 | 궁극기 발동까지 필요한 총량과 수급량")]
    [Tooltip("페이즈 코어 최대치(%)")]
    public float gaugeMax = 100f;
    [Tooltip("약/강 공격 명중 시 게이지 증가량(%)")]
    public float gaugePerH= 3f;
    [Tooltip("패링 성공 시 게이지 증가량(%)")]
    public float gaugePerParry =6f;
    [Tooltip("퍼펙트 회피 성공 시 게이지 증가량(%)")]
    public float gaugePerPerfectDodge = 16;

    [Header("② 발동 조건 | 발동 전 체크할 조건들 (이동, 경직, 공중 등)")]
    [Tooltip("점프 중 발동 허용 여부")]
    public bool allowInAir = false;
    [Tooltip("피격 경직 중 발동 금지")]
    public bool blockWhenStaggered = true;

    [Header("③ 연출/상태 | 연출 중 무적, 조작 잠금, 락온/카메라 처리")]
    [Tooltip("연출 중 무적 적용 여부")]
    public bool invulnerableDuringCinematic = true;
    [Tooltip("연출 중 플레이어 조작 잠금")]
    public bool lockInputDuringCinematic = true;
    [Tooltip("연출 종료 후 락온/카메라를 원위치 시킬지 여부")]
    public bool restoreCameraAndLockOn = true;

    [Header("④ 데미지 설정 | 보스 고정 데미지 및 보정치")]
    [Tooltip("피니시 일섬 고정 데미지")]
    public int finisherFixedDamage = 1200;
    [Tooltip("다단 참격 1히트당 고정 데미지")]
    public int multihitFixedDamage = 120;

    [Header("⑤ 타임라인/레퍼런스 | 컷씬 및 카메라 바인딩")]
    [Tooltip("궁극기 컷씬용 PlayableDirector")]
    public PlayableDirector director;
    [Tooltip("컷씬 동안 사용할 시네머신 카메라들(순서대로 블렌드)")]
    public CinemachineVirtualCamera[] vCams;

    [Header("⑥ 포스트/전체이펙트 | 흑백, 비네팅, 화면균열 등 토글")]
    [Tooltip("풀스크린 흑백 흡입 셰이더 컨트롤러")]
    public UltimateScreenFX screenFX;
    [Tooltip("피니시 직후 화면 균열 이펙트")]
    public GameObject glassCrackPrefab;

    [Header("⑦ 훅/이벤트 | 외부 시스템과의 연동 이벤트")]
    public Action OnUltimateStarted;
    public Action OnUltimateEnded;

    // ========================= 내부 상태 =========================
    public float Gauge { get; private set; }
    bool _isCinematic;

    IInputBlocker _input;                // 프로젝트 공용 입력차단 인터페이스
    ILockOnController _lockOn;           // 락온 컨트롤러
    IInvulnerabilityToggle _invul;       // 무적 토글(어댑터/인터페이스)
    ICombatStateReader _combat;          // 경직/공격중 여부 등 상태 조회

    void Awake()
    {
        _input  = GetComponent<IInputBlocker>();
        _lockOn = GetComponent<ILockOnController>();
        _invul  = GetComponent<IInvulnerabilityToggle>();
        _combat = GetComponent<ICombatStateReader>();
    }

    // 게이지 수급(공격/패링/퍼펙트회피에서 호출)
    public void AddGauge(float amount)
    {
        if (_isCinematic) return;
        Gauge = Mathf.Clamp(Gauge + amount, 0f, gaugeMax);
        // UI 갱신 요청 (페이즈 코어 회전/색상 단계)
        UI_UltimateGauge.UpdateValue(Gauge / gaugeMax);
    }

    // R 입력에서 호출
    public bool TryActivate()
    {
        if (_isCinematic) return false;
        if (Gauge < gaugeMax) return false;
        if (blockWhenStaggered && _combat != null && _combat.IsStaggered()) return false;
        if (!allowInAir && _combat != null && _combat.IsInAir()) return false;

        StartCoroutine(Co_Cinematic());
        return true;
    }

    IEnumerator Co_Cinematic()
    {
        _isCinematic = true;
        Gauge = 0f; // 1회용 소모
        UI_UltimateGauge.UpdateValue(0);

        // 입력/무적/카메라 권한
        if (lockInputDuringCinematic) _input?.BlockAll(true);
        if (invulnerableDuringCinematic) _invul?.SetInvulnerable(true);
        _lockOn?.GiveCameraControlToTimeline(true);

        OnUltimateStarted?.Invoke();

        // 0) 프리롤: 코어 폭주 시작(화면 흑백 흡입 이펙트 0→1)
        screenFX?.PlayChargeIn();

        // 1) 타임라인 플레이 (애니/카메라/오디오/VFX 통합)
        if (director != null)
        {
            director.time = 0;
            director.Evaluate();
            director.Play();
        }

        // 타임라인 Signal에서 아래 훅을 호출하도록 배선:
        //  - Signal “MultiHit” → OnMultiHit()
        //  - Signal “Finisher” → OnFinisher()
        //  - Signal “CutsceneEnd” → (아래 종료 처리)

        // 대기: 타임라인 종료까지
        while (director != null && director.state == PlayState.Playing)
            yield return null;

        // 종료 처리
        screenFX?.PlayChargeOut();
        if (restoreCameraAndLockOn) _lockOn?.GiveCameraControlToTimeline(false);
        if (invulnerableDuringCinematic) _invul?.SetInvulnerable(false);
        if (lockInputDuringCinematic) _input?.BlockAll(false);

        OnUltimateEnded?.Invoke();
        _isCinematic = false;
    }

    // --- 타임라인 Signal 훅 ---
    // 다단참격 타이밍들에서 이벤트로 호출
    public void OnMultiHit()
    {
        ApplyBurstDamage(multihitFixedDamage);
        screenFX?.PulseMinor(); // 약한 화면 펄스
    }

    // 피니시 일섬 타이밍에서 이벤트로 호출
    public void OnFinisher()
    {
        ApplyBurstDamage(finisherFixedDamage);
        if (glassCrackPrefab) Instantiate(glassCrackPrefab, Vector3.zero, Quaternion.identity);
        screenFX?.PulseMajor(); // 강한 화면 펄스
    }

    void ApplyBurstDamage(int damage)
    {
        // 현재 락온 대상에 우선 적용
        var target = _lockOn?.GetCurrentTarget();
        if (target != null && target.TryGetComponent<IUltimateTarget>(out var ult))
            ult.ApplyUltimateDamage(damage);
        else
        {
            // 보스 태그 서치(락온이 없을 때의 폴백)
            var boss = GameObject.FindWithTag("Boss");
            if (boss != null && boss.TryGetComponent<IUltimateTarget>(out var ult2))
                ult2.ApplyUltimateDamage(damage);
        }
    }
}
