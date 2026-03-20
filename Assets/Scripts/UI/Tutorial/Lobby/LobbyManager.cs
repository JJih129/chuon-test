using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    [Header("■ UI 요소")]
    public CanvasGroup dialogueGroup;
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI contentText;
    public Image alertOverlay;

    [Header("■ 퀘스트 UI")]
    public CanvasGroup questPanelGroup;
    public TextMeshProUGUI questTitleText;
    public TextMeshProUGUI questDescriptionText;

    [Header("■ 전투 설정")]
    public GameObject dronePrefab;
    public Transform[] spawnPoints;
    private int totalEnemyCount = 0;
    private int currentDeadEnemy = 0;

    [Header("■ 엘리베이터 상호작용")]
    public GameObject elevatorPanel;
    public string nextSceneName = "MainScene";

    [Header("■ 엘리베이터 문 설정")]
    public Transform doorLeft;
    public Transform doorRight;
    public float doorOpenHeight = 4.0f;
    public float doorOpenSpeed = 2.0f;

    [Header("■ 네비게이션 & 트리거")]
    public GuidePathSystem guideSystem;
    public Transform triggerApproach; // 문 앞 트리거
    public Transform triggerBoard;    // 안쪽 트리거
    
    [Tooltip("경유지 리스트")]
    public Transform[] pathWaypoints; 
    private List<Transform> activeWaypoints = new List<Transform>();

    [Header("■ 대화 설정")]
    [Range(0.01f, 0.2f)] public float textTypingSpeed = 0.05f;

    // 상태 변수 (외부 접근을 위해 프로퍼티 추가)
    private bool isDoorReached = false;
    public bool IsDoorReached => isDoorReached; // ★ 트리거에서 확인용

    private bool isBoarded = false;
    public bool IsBoarded => isBoarded;   
    
    // 내부 상태 변수 쪽에 추가하세요
    private bool isBranchSelected = false;      // ★ 트리거에서 확인용

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        if (dialogueGroup) dialogueGroup.alpha = 0;
        if (questPanelGroup) { questPanelGroup.alpha = 0; questPanelGroup.interactable = false; }
        if (alertOverlay) alertOverlay.color = new Color(1, 0, 0, 0); 
        if (elevatorPanel) elevatorPanel.SetActive(false); 

        StartCoroutine(Sequence_Arrival());
    }

    // ──────────────────────────────────────────────
    // 1. 도착 & 경보
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Arrival()
    {
        yield return StartCoroutine(PlayDialogue("추온", "여기가 놈들의 심장부…", 2.0f));
        if (alertOverlay) alertOverlay.DOFade(0.3f, 0.5f).SetLoops(6, LoopType.Yoyo);
        yield return StartCoroutine(PlayDialogue("시스템", "침입자 감지. 방어 프로토콜을 가동합니다.", 3.0f));
        yield return StartCoroutine(PlayDialogue("EGO", "다른 길은 없는 것 같아, 이 녀석들을 쓰러뜨려야 이동 할 수 있겠는데?", 2.5f));
        yield return StartCoroutine(PlayDialogue("추온", "좋아. 다 부숴주지.", 2.0f));
        if (dialogueGroup) dialogueGroup.DOFade(0, 0.5f);
        StartCoroutine(Sequence_Combat());
    }

    // ──────────────────────────────────────────────
    // 2. 전투
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Combat()
    {
        currentDeadEnemy = 0;
        UpdateQuestUI("현재 목표", "적들이 나타났습니다. 모두 처치하세요.");
        if (questPanelGroup) { questPanelGroup.DOFade(1, 0.5f); questPanelGroup.transform.DOPunchScale(Vector3.one * 0.1f, 0.3f); }

        if (dronePrefab && spawnPoints.Length > 0)
        {
            totalEnemyCount = spawnPoints.Length;
            Transform playerTr = GameObject.FindWithTag("Player").transform;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                GameObject drone = Instantiate(dronePrefab, spawnPoints[i].position, spawnPoints[i].rotation);
                var ai = drone.GetComponent<DroneController>();
                if (ai) { ai.target = playerTr; ai.enabled = true; }
                var tracker = drone.GetComponent<LobbyEnemy>();
                if (tracker == null) tracker = drone.AddComponent<LobbyEnemy>();
            }
        }
        else { totalEnemyCount = 1; OnEnemyKilled(); }

        UpdateQuestUI("전투 개시", $"적을 섬멸하세요. ({currentDeadEnemy} / {totalEnemyCount})");
        yield return new WaitUntil(() => currentDeadEnemy >= totalEnemyCount);
        yield return new WaitForSeconds(1.0f);
        StartCoroutine(Sequence_PostCombat());
    }

    public void OnEnemyKilled()
    {
        currentDeadEnemy++;
        UpdateQuestUI("전투 중", $"적을 섬멸하세요. ({currentDeadEnemy} / {totalEnemyCount})");
        if (questDescriptionText) questDescriptionText.transform.DOPunchScale(Vector3.one * 0.2f, 0.15f);
    }

    // ──────────────────────────────────────────────
    // 3. 전투 종료 & 이동 유도
    // ──────────────────────────────────────────────
    IEnumerator Sequence_PostCombat()
    {
        UpdateQuestUI("전투 완료", "모든 적을 처치했습니다.");
        isDoorReached = false; 

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(PlayDialogue("EGO", "좋아, 다 파괴한 것 같아. 지원 병력도 더 이상 오지 않고 있어.", 2.0f));
        // ... (중간 대사 생략 - 필요시 추가) ...
        yield return StartCoroutine(PlayDialogue("추온", "뭔가 유도 당하는 느낌이지만 어쩔 수 없지.", 1.5f));

        if (dialogueGroup) dialogueGroup.DOFade(0, 0.5f);
        UpdateQuestUI("이동", "프론트 뒤편 엘리베이터로 이동하세요.");

        if (guideSystem)
        {
            activeWaypoints.Clear();
            if (pathWaypoints != null)
            {
                foreach (var wp in pathWaypoints)
                {
                    if (wp != null) { wp.gameObject.SetActive(true); activeWaypoints.Add(wp); }
                }
            }
            UpdateNavigationPath();
        }

        // 문 앞 도착 대기 (미리 도착해도 LobbyTrigger가 Stay 감지해서 true로 바꿔줌)
        yield return new WaitUntil(() => isDoorReached == true);

        StartCoroutine(Sequence_AtElevator());
    }

    public void OnWaypointReached(Transform reachedPoint)
    {
        if (activeWaypoints.Contains(reachedPoint))
        {
            activeWaypoints.Remove(reachedPoint);
            UpdateNavigationPath();
        }
    }

    void UpdateNavigationPath()
    {
        if (guideSystem == null) return;
        List<Transform> pathList = new List<Transform>();
        pathList.AddRange(activeWaypoints);
        if (triggerApproach) pathList.Add(triggerApproach);
        guideSystem.ShowPath(pathList.ToArray());
    }

    public void OnReachElevator() 
    { 
        isDoorReached = true; 
        if(guideSystem) guideSystem.HidePath();
    }

    // ──────────────────────────────────────────────
    // 4. 엘리베이터 도착 (문 열림)
    // ──────────────────────────────────────────────
    IEnumerator Sequence_AtElevator()
    {
        if (doorLeft)
        {
            Collider col = doorLeft.GetComponent<Collider>();
            if (col) col.isTrigger = true; 
            doorLeft.transform.DOLocalMoveY(doorLeft.transform.localPosition.y + doorOpenHeight, doorOpenSpeed).SetEase(Ease.OutQuad);
        }
        if (doorRight) 
        {
            Collider col = doorRight.GetComponent<Collider>();
            if (col) col.isTrigger = true; 
            doorRight.transform.DOLocalMoveY(doorRight.transform.localPosition.y + doorOpenHeight, doorOpenSpeed).SetEase(Ease.OutQuad);
        }

        yield return StartCoroutine(PlayDialogue("추온", "아무리 봐도 정상적인 엘리베이터는 아닌 거 같은데?", 2.0f));
        yield return StartCoroutine(PlayDialogue("EGO", "그러게, 내 데이터베이스에도 이 엘리베이터에 대한 정보는 없어.", 2.0f));
        if (dialogueGroup) dialogueGroup.DOFade(0, 0.5f);
        
        UpdateQuestUI("이동", "엘리베이터에 탑승하세요.");

        if (guideSystem && triggerBoard) guideSystem.ShowPath(triggerBoard);

        isBoarded = false;
        // ★ 미리 들어가 있어도 LobbyTrigger의 OnTriggerStay가 isBoarded를 true로 만들어줌
        yield return new WaitUntil(() => isBoarded == true);

        if (guideSystem) guideSystem.HidePath();
        StartCoroutine(Sequence_Inside());
    }

    public void OnEnterElevator() { isBoarded = true; }

    // ──────────────────────────────────────────────
    // 5. 탑승 & 상호작용
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Inside()
    {
        yield return StartCoroutine(PlayDialogue("EGO", "이 엘리베이터도 경로가 제한돼 있어. 격납고 층으로만 연결되는 듯해.", 2.0f));
        yield return StartCoroutine(PlayDialogue("추온", "…놈들이 일부러 열어둔 길인가.", 2.0f));
        yield return StartCoroutine(PlayDialogue("EGO", "그럴 가능성이 높아. 조심해, 무언가가 우릴 기다리고 있어.", 2.0f));

        if (dialogueGroup) dialogueGroup.DOFade(0, 0.5f);

        UpdateQuestUI("목표", "패널을 조작하여 층을 이동하세요.");

        if (elevatorPanel)
        {
            elevatorPanel.SetActive(true);
            elevatorPanel.transform.DOPunchScale(Vector3.one * 0.2f, 0.5f);
        }
    }

    public void OnInteractElevator()
    {
        if (SceneFader.Instance) SceneFader.Instance.FadeOutAndLoadScene(nextSceneName);
        else UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
    }

    IEnumerator PlayDialogue(string speaker, string content, float waitTime)
    {
        dialogueGroup.alpha = 1;
        if (speakerText) speakerText.text = speaker;
        if (contentText)
        {
            contentText.text = "";
            foreach (char c in content)
            {
                contentText.text += c;
                yield return new WaitForSeconds(textTypingSpeed); 
            }
        }
        yield return new WaitForSeconds(waitTime);
    }

    // 갈림길 트리거(BranchTrigger)가 호출하는 함수
    public void OnBranchSelected()
    {
        if (!isBranchSelected)
        {
            isBranchSelected = true;
            Debug.Log(">> [LobbyManager] 갈림길 선택됨!");
        }
    }

    void UpdateQuestUI(string title, string desc)
    {
        if (questTitleText) questTitleText.text = title;
        if (questDescriptionText) questDescriptionText.text = desc;
    }
}
