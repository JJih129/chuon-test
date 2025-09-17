using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 통합 스크립트: 근접 기반 보스 UI 관리기
// - 적 프리팹에 ProximityBossUI를 붙이면 자동 등록됩니다.
// - World-space HP 바는 EnemyHPBarPool에서 가져와 재사용합니다.
// - 상단 고정 보스 HUD는 BossHUD.Bind(IHealth)를 사용해 바인딩합니다.
// 주의: 이 파일에서는 ILockOnController, IHealth 등의 인터페이스를 재정의하지 않습니다.

public class IntegratedBossUI : MonoBehaviour
{
    // ===================== 변수 헤더(한글 설명) =====================
    [Header("▶ 공용 참조 | 프로젝트에 이미 있는 컴포넌트들을 연결하세요")]
    [Tooltip("플레이어 Transform. 거리 계산 기준입니다.")]
    public Transform player;

    [Tooltip("메인 카메라(빌보드 정렬용). 비워두면 Camera.main 사용.")]
    public Camera mainCamera;

    [Tooltip("EnemyHPBarPool 인스턴스. World-space HP바를 가져올 풀.")]
    public EnemyHPBarPool hpBarPool;

    [Tooltip("상단 고정 Boss HUD 루트(예: Canvas에 있는 HUD Root)")]
    public GameObject topBossHUDRoot;

    [Tooltip("상단 BossHUD 컴포넌트. topBossHUDRoot에 할당된 컴포넌트.")]
    public BossHUD topBossHUD;

    [Header("▶ 거리/노출 설정")]
    [Tooltip("World-space UI나 상단 HUD가 표시되는 최대 거리(미터)")]
    public float showDistance = 18f;

    [Tooltip("숨김 임계에 더해줄 여유거리(미터). hideDistance = showDistance + hideHysteresis")]
    public float hideHysteresis = 2f;

    [Tooltip("거리 체크 폴링 주기(초). 0이면 매 프레임 체크(성능 주의)")]
    public float pollInterval = 0.12f;

    [Tooltip("락온 상태일 때만 표시하려면 true로 설정. false면 거리만으로 판정.")]
    public bool requireLockOn = false;

    [Tooltip("동시에 보일 수 있는 World-space HP바의 최대 개수. 성능/가독성 조절용.")]
    public int maxVisibleWorldBars = 3;

    // ===================== 내부 상태 =====================
    static IntegratedBossUI _instance;
    List<ProximityBossUI> registered = new List<ProximityBossUI>();
    Dictionary<ProximityBossUI, EnemyHPBar> activeBars = new Dictionary<ProximityBossUI, EnemyHPBar>();

    ILockOnController lockOnController;
    float showSqr;
    float hideSqr;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;

        if (mainCamera == null) mainCamera = Camera.main;
        // using 없음 추가 필요 없음
        lockOnController = FindObjectOfType<SimpleLockOnController>() as ILockOnController;


        showSqr = showDistance * showDistance;
        hideSqr = (showDistance + Mathf.Max(0f, hideHysteresis));
        hideSqr *= hideSqr;

        if (topBossHUDRoot != null) topBossHUDRoot.SetActive(false);

        StartCoroutine(PollRoutine());
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // 등록 API. 적 프리팹의 ProximityBossUI가 호출.
    public static void Register(ProximityBossUI comp)
    {
        if (_instance == null) return;
        if (!_instance.registered.Contains(comp)) _instance.registered.Add(comp);
    }
    public static void Unregister(ProximityBossUI comp)
    {
        if (_instance == null) return;
        _instance.registered.Remove(comp);
        // 바 반환
        if (_instance.activeBars.TryGetValue(comp, out var bar))
        {
            _instance.hpBarPool?.Return(bar);
            _instance.activeBars.Remove(comp);
        }
    }

    IEnumerator PollRoutine()
    {
        var wait = (pollInterval > 0f) ? new WaitForSeconds(pollInterval) : null;
        while (true)
        {
            EvaluateAll();
            if (wait != null) yield return wait; else yield return null;
        }
    }

    void EvaluateAll()
    {
        if (player == null) return;

        // 후보 정렬: 거리 기준
        var list = new List<(ProximityBossUI comp, float sqr, float dist)>();
        foreach (var c in registered)
        {
            if (c == null) continue;
            float sqr = (player.position - c.transform.position).sqrMagnitude;
            float dist = Mathf.Sqrt(sqr);
            list.Add((c, sqr, dist));
        }
        list.Sort((a, b) => a.sqr.CompareTo(b.sqr));

        // 락온 상태 확인
        Transform currentLock = lockOnController != null ? lockOnController.GetCurrentTarget() : null;

        // World bars: 상위 N개 노출
        int shownWorld = 0;
        var toShowTop = list.Count > 0 ? list[0].comp : null; // 최우선 타겟

        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            bool lockok = true;
            if (requireLockOn && currentLock != entry.comp.transform) lockok = false;

            bool withinShow = entry.sqr <= showSqr;
            bool withinHide = entry.sqr <= hideSqr;

            // 노출 결정: 락온 조건 통과 && 거리 조건
            bool shouldShowWorld = lockok && withinShow && shownWorld < maxVisibleWorldBars;

            if (shouldShowWorld)
            {
                ShowWorldBar(entry.comp);
                shownWorld++;
            }
            else
            {
                // 히스테리시스: 이미 보이는 바는 hideSqr 기준으로 유지
                if (activeBars.ContainsKey(entry.comp) && withinHide && lockok)
                {
                    // 유지
                }
                else
                {
                    HideWorldBar(entry.comp);
                }
            }
        }

        // Top HUD: 최우선 타겟만 바인딩
        if (toShowTop != null)
        {
            bool lockok = true;
            if (requireLockOn && currentLock != toShowTop.transform) lockok = false;
            float dSqr = (player.position - toShowTop.transform.position).sqrMagnitude;
            if (lockok && dSqr <= showSqr)
            {
                BindTopHUD(toShowTop);
                return;
            }
        }
        // 조건 미충족이면 숨김
        UnbindTopHUD();
    }

    void ShowWorldBar(ProximityBossUI comp)
    {
        if (activeBars.ContainsKey(comp)) return; // 이미 보임
        if (hpBarPool == null) return;

        var bar = hpBarPool.Get();
        // 바인딩: IHealth 기대
        var ih = comp.healthBehaviour as IHealth;
        if (ih != null)
        {
            bar.Bind(ih);
        }
        else
        {
            // 시그니처가 다르면 유연하게 처리 시도 (예: component에 ApplyDamage 등)
            // 바인딩이 실패하면 반환
        }
        bar.target = comp.pivot ? comp.pivot : comp.transform;
        bar.offset = comp.worldOffset;
        bar.Show();

        activeBars[comp] = bar;
    }

    void HideWorldBar(ProximityBossUI comp)
    {
        if (!activeBars.ContainsKey(comp)) return;
        var bar = activeBars[comp];
        hpBarPool?.Return(bar);
        activeBars.Remove(comp);
    }

    void BindTopHUD(ProximityBossUI comp)
    {
        if (topBossHUD == null || topBossHUDRoot == null) return;
        var ih = comp.healthBehaviour as IHealth;
        if (ih == null) return;
        topBossHUD.Bind(ih);
        topBossHUDRoot.SetActive(true);
    }

    void UnbindTopHUD()
    {
        if (topBossHUD == null || topBossHUDRoot == null) return;
        topBossHUD.Unbind();
        topBossHUDRoot.SetActive(false);
    }
}


// -------------------- 적 프리팹에 붙이는 컴포넌트 --------------------
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
