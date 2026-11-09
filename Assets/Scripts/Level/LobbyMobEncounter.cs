using System;
using UnityEngine;

// Level_Facility 상층(츠카사 본사 로비) 잡몹 교전 — 가르치기 → 시험 → 비틀기 (Docs/Mobs_Design.md §4)
//  ① 가르치기: 스카이덱을 지나 프론트 데스크 앞 경비 안드로이드 1 (순찰). 관측 연출이 끝나고 전투 게이트를 넘기 전엔
//     플레이어를 인지하지 않는다. 처치해야 드론 웨이브(LobbyManager 전투 게이트)가 시작된다.
//  ② 기존 드론 웨이브 (LobbyManager 그대로)
//  ③ 시험 + 비틀기: 드론 전멸(CombatCompleted) → 엘리베이터 홀에서 안드로이드 + 방패 집행관 출현, 센트리 터렛이 접근로를 덮는다.
//     guardGroup 전멸 전에는 엘리베이터 문이 열리지 않는다(LobbyManager.SequencePostCombat). 전멸하면 지원(터렛)은 정지.
//  하층 도착(LowerFloorReached) 시 남은 상층 적은 모두 비활성 (보스전 락온/토큰/HP 바에 끼지 않게).
[DisallowMultipleComponent]
public sealed class LobbyMobEncounter : MonoBehaviour
{
    [Tooltip("시작부터 활성. 전멸해야 드론 웨이브 시작")]
    [SerializeField] EnemyBrain[] teachGroup = Array.Empty<EnemyBrain>();
    [Tooltip("드론 전멸 후 활성. 전멸해야 엘리베이터 문이 열린다")]
    [SerializeField] EnemyBrain[] guardGroup = Array.Empty<EnemyBrain>();
    [Tooltip("guardGroup 과 함께 활성 (터렛 등). 게이트 조건 아님 — guardGroup 전멸 시 정지")]
    [SerializeField] EnemyBrain[] guardSupport = Array.Empty<EnemyBrain>();
    [Tooltip("활성화 직후 플레이어를 인지한 상태로 시작 (엘리베이터에서 쏟아져 나오는 연출)")]
    [SerializeField] bool alertGuardsOnActivate = true;
    [SerializeField, Min(0f)] float guardActivateDelay = 0.6f;
    [SerializeField, Min(0f)] float supportShutdownDelay = 0.8f;
    [Tooltip("가르치기 적은 플레이어가 이 게이트 평면을 앞방향으로 넘고(비우면 LobbyManager 전투 게이트) 관측 연출이 끝난 뒤에야 인지한다 — 아트리움 스카이덱 카메라 연출 중 기습 방지")]
    [SerializeField] Transform teachWakeGate;

    public static LobbyMobEncounter Active { get; private set; }

    LobbyManager _lobby;
    bool _guardsActivated;
    float _guardActivateAt = -1f;
    float _supportShutdownAt = -1f;
    bool _supportShutdown;
    bool _handedOff;
    int _alertGuardsAtFrame = -1;
    bool _teachAwake;
    float _nextWakeCheckAt;

    public bool TeachCleared => AllDead(teachGroup);
    public bool GuardsActivated => _guardsActivated;
    public bool GuardsPendingOrActive => _guardsActivated || _guardActivateAt >= 0f;
    public bool GuardsCleared => _guardsActivated && AllDead(guardGroup);
    public int GuardTotal => CountValid(guardGroup);
    public int GuardDead => GuardTotal - CountAlive(guardGroup);
    public EnemyBrain[] TeachGroup => teachGroup;
    public EnemyBrain[] GuardGroup => guardGroup;
    public EnemyBrain[] GuardSupport => guardSupport;

    void Awake()
    {
        Active = this;
        SetGroupActive(guardGroup, false);
        SetGroupActive(guardSupport, false);
        LobbyManager.LowerFloorReached += HandleLowerFloorReached;
    }

    void OnDestroy()
    {
        LobbyManager.LowerFloorReached -= HandleLowerFloorReached;
        if (_lobby != null)
            _lobby.CombatCompleted -= HandleCombatCompleted;
        if (Active == this)
            Active = null;
    }

    void Start()
    {
        _lobby = LobbyManager.Instance != null ? LobbyManager.Instance : FindObjectOfType<LobbyManager>();
        if (_lobby != null)
            _lobby.CombatCompleted += HandleCombatCompleted;
        if (teachWakeGate == null && _lobby != null)
            teachWakeGate = _lobby.CombatStartGate;
        SetTeachSuppressed(teachWakeGate != null);
        _teachAwake = teachWakeGate == null;
    }

    void SetTeachSuppressed(bool suppressed)
    {
        for (int i = 0; i < teachGroup.Length; i++)
        {
            if (teachGroup[i] != null)
                teachGroup[i].PerceptionSuppressed = suppressed;
        }
    }

    void UpdateTeachWake()
    {
        if (_teachAwake || Time.time < _nextWakeCheckAt)
            return;
        _nextWakeCheckAt = Time.time + 0.2f;
        if (LevelFacilityObservationBeat.IsPlaying)
            return;
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null || Vector3.Dot(player.transform.position - teachWakeGate.position, teachWakeGate.forward) <= 0f)
            return;
        _teachAwake = true;
        SetTeachSuppressed(false);
    }

    void Update()
    {
        float now = Time.time;
        UpdateTeachWake();
        if (!_guardsActivated && _guardActivateAt >= 0f && now >= _guardActivateAt)
            ActivateGuards();

        // 활성화 다음 프레임(두뇌 Start 로 대상/에이전트가 준비된 뒤)에 플레이어 위치를 알려 경계 시작
        if (_alertGuardsAtFrame >= 0 && Time.frameCount >= _alertGuardsAtFrame)
        {
            _alertGuardsAtFrame = -1;
            GameObject player = GameObject.FindWithTag("Player");
            Vector3 known = player != null ? player.transform.position : transform.position;
            for (int i = 0; i < guardGroup.Length; i++)
            {
                if (guardGroup[i] != null && guardGroup[i].isActiveAndEnabled)
                    guardGroup[i].ReceiveAlert(known);
            }
        }

        if (_guardsActivated && !_supportShutdown && AllDead(guardGroup))
        {
            if (_supportShutdownAt < 0f)
                _supportShutdownAt = now + supportShutdownDelay;
            else if (now >= _supportShutdownAt)
            {
                _supportShutdown = true;
                PowerDownSupport();
            }
        }
    }

    void HandleCombatCompleted()
    {
        if (_handedOff || _guardsActivated)
            return;
        _guardActivateAt = Time.time + guardActivateDelay;
    }

    /// <summary>QA/디버그: 즉시 경비 웨이브 활성.</summary>
    public void ActivateGuards()
    {
        if (_guardsActivated || _handedOff)
            return;
        _guardsActivated = true;
        SetGroupActive(guardGroup, true);
        SetGroupActive(guardSupport, true);
        if (alertGuardsOnActivate)
            _alertGuardsAtFrame = Time.frameCount + 1;
    }

    // 경비 해제: 터렛은 잔해로 남기고(헤드 처짐), 그 밖의 지원 병력은 비활성
    void PowerDownSupport()
    {
        for (int i = 0; i < guardSupport.Length; i++)
        {
            EnemyBrain b = guardSupport[i];
            if (b == null || b.IsDead)
                continue;
            if (b is SentryTurretBrain turret && b.isActiveAndEnabled)
                turret.PowerDown();
            else
                b.gameObject.SetActive(false);
        }
    }

    void HandleLowerFloorReached(LobbyManager lobby)
    {
        _handedOff = true;
        SetGroupActive(teachGroup, false);
        SetGroupActive(guardGroup, false);
        SetGroupActive(guardSupport, false);
    }

    static void SetGroupActive(EnemyBrain[] group, bool on)
    {
        if (group == null)
            return;
        for (int i = 0; i < group.Length; i++)
        {
            if (group[i] != null && group[i].gameObject.activeSelf != on)
                group[i].gameObject.SetActive(on);
        }
    }

    static bool AllDead(EnemyBrain[] group) => CountAlive(group) == 0;

    static int CountAlive(EnemyBrain[] group)
    {
        int n = 0;
        if (group == null)
            return 0;
        for (int i = 0; i < group.Length; i++)
        {
            if (group[i] != null && !group[i].IsDead)
                n++;
        }
        return n;
    }

    static int CountValid(EnemyBrain[] group)
    {
        int n = 0;
        if (group == null)
            return 0;
        for (int i = 0; i < group.Length; i++)
        {
            if (group[i] != null)
                n++;
        }
        return n;
    }
}
