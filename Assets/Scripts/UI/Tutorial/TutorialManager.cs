using UnityEngine;
using UnityEngine.UI;
using TMPro;          
using DG.Tweening;
using System.Collections;

public enum TutorialStep
{
    None, Movement, Attack, Defense, Heal, Complete
}

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance;

    [Header("■ 대화창 UI")]
    public CanvasGroup dialogueGroup;
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI contentText;

    [Header("■ 퀘스트 UI")]
    public CanvasGroup questPanelGroup;     
    public Image questPanelBg;              
    public TextMeshProUGUI questTitleText;  
    public TextMeshProUGUI questDescriptionText; 
    public GameObject comboGuidePanel;      

    [Header("■ 게임 오브젝트")]
    public GameObject movementGoal;    
    public GameObject attackDummy;     
    public GameObject droneEnemy;      
    public PlayerHealth playerHealth;  

    [Header("■ 대화 시간 설정 (초)")]
    public float introDelay = 2.0f;    // 인트로 대사 간격
    public float moveDelay = 2.0f;     // 이동 단계 대사 간격
    public float attackDelay = 2.0f;   // 공격 단계 대사 간격
    public float defenseDelay = 2.5f;  // 방어 단계 대사 간격
    public float healDelay = 2.0f;     // 회복 단계 대사 간격
    public float completeDelay = 3.0f; // 완료 대사 간격

    [Header("■ 훈련 설정")]
    public int targetDodgeCount = 2;   
    public int targetGuardCount = 3;   
    public int targetParryCount = 1;   

    // 내부 변수
    private TutorialStep currentStep = TutorialStep.None;
    private int currentDodgeCount = 0;
    private bool isGoalReached = false;
    private int currentAttackCount = 0;
    private int maxAttackCount = 4;
    private int currentGuardCount = 0;
    private int currentParryCount = 0;

    // 색상
    private Color originalColor = new Color(0, 0, 0, 0.5f); 
    private Color highlightColor = new Color(1f, 0.8f, 0f, 0.5f); 
    private Color combatColor = new Color(1f, 0.2f, 0.2f, 0.5f); 
    private Color defenseColor = new Color(0.2f, 0.5f, 1f, 0.5f); 
    private Color healColor = new Color(0.2f, 1f, 0.5f, 0.5f);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        // 초기화
        if (dialogueGroup) dialogueGroup.alpha = 0;
        if (questPanelGroup) { questPanelGroup.alpha = 0; questPanelGroup.interactable = false; }
        if (questPanelBg) questPanelBg.color = originalColor;
        if (comboGuidePanel) comboGuidePanel.SetActive(false);

        if (movementGoal) movementGoal.SetActive(false);
        if (attackDummy) attackDummy.SetActive(false);
        if (droneEnemy) droneEnemy.SetActive(false);

        StartCoroutine(Sequence_Intro());
    }

    void Update()
    {
        if (currentStep == TutorialStep.Movement)
        {
            if (Input.GetKeyDown(KeyCode.LeftShift)) OnDodgeAction();
        }
        if (currentStep == TutorialStep.Heal)
        {
            if (Input.GetKeyDown(KeyCode.Q)) OnHealAction();
        }
    }

    // ──────────────────────────────────────────────
    // 1. 인트로 (시간 변수 적용)
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Intro()
    {
        yield return StartCoroutine(PlayDialogue("EGO", "가상현실 부팅 완료.", introDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "내일 싸우러 가기 전 마지막 훈련을 진행해 보자고.", introDelay));
        yield return StartCoroutine(PlayDialogue("추온", "알겠어, 대비는 철저히 하는게 좋겠지.", introDelay));

        
        StartCoroutine(Sequence_Movement());
    }

    // ──────────────────────────────────────────────
    // 2. 이동 (시간 변수 적용)
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Movement()
    {
        yield return StartCoroutine(PlayDialogue("EGO", "자 우선 몸 좀 풀어볼까? WASD로 움직여볼래?", moveDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "좋아, 이번엔 마우스를 움직여 주위를 둘러봐.", moveDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "한 녀석을 집중해서 보려면 마우스 휠을 입력해 고정 할 수 있어.", moveDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "이제 발판으로 이동해보자.", moveDelay));
        
        if (dialogueGroup) dialogueGroup.DOFade(0, 1f);

        currentStep = TutorialStep.Movement;
        if (movementGoal) movementGoal.SetActive(true);

        ShowQuestPanel();
        UpdateMovementUI();
    }

    void OnDodgeAction()
    {
        if (currentDodgeCount < targetDodgeCount)
        {
            currentDodgeCount++;
            PunchEffect();
            UpdateMovementUI();
            CheckMovementComplete();
        }
    }

    public void OnGoalReached()
    {
        if (isGoalReached) return;
        isGoalReached = true;
        PunchEffect();
        UpdateMovementUI();
        CheckMovementComplete();
    }

    void UpdateMovementUI()
    {
        string goalStr = isGoalReached ? "<color=green>(완료)</color>" : "(미완료)";
        UpdateQuestUI("기초 기동", $"1. 지정된 위치로 이동 {goalStr}\n2. Shift 회피 ({currentDodgeCount}/{targetDodgeCount})");
    }

    void CheckMovementComplete()
    {
        if (isGoalReached && currentDodgeCount >= targetDodgeCount)
        {
            if (movementGoal) movementGoal.SetActive(false);
            StartCoroutine(Sequence_Attack());
        }
    }

    // ──────────────────────────────────────────────
    // 3. 공격 (시간 변수 적용)
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Attack()
    {
        currentStep = TutorialStep.None;

        yield return StartCoroutine(PlayDialogue("EGO", "몸이 좀 풀린 것 같네! 이제 검을 휘둘러보자.", attackDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "마우스 좌클릭으로 약공격, 우클릭으로 강공격을 할 수 있어.", attackDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "오케이 이번엔 콤보를 사용해보자. 약공격을 4번 연속 입력하면 돼.", attackDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "좌측 상단 콤보를 참고해서 공격해봐.", attackDelay));
        if (dialogueGroup) dialogueGroup.DOFade(0, 2f);

        if (comboGuidePanel)
        {
            comboGuidePanel.SetActive(true);
            comboGuidePanel.transform.DOPunchScale(Vector3.one * 0.1f, 0.3f);
        }

        currentStep = TutorialStep.Attack;
        if (attackDummy) attackDummy.SetActive(true);

        FlashPanel(combatColor);
        UpdateQuestUI("전투 훈련", $"허수아비를 공격하세요.\n({currentAttackCount} / {maxAttackCount})");
    }

    public void OnEnemyHit()
    {
        if (currentStep != TutorialStep.Attack) return;

        currentAttackCount++;
        UpdateQuestUI("전투 훈련", $"허수아비를 공격하세요.\n({currentAttackCount} / {maxAttackCount})");
        PunchEffect();

        if (currentAttackCount >= maxAttackCount)
        {
            if (attackDummy) attackDummy.SetActive(false);
            //if (comboGuidePanel) comboGuidePanel.SetActive(false);
            StartCoroutine(Sequence_MidTalk());
        }
    }

    IEnumerator Sequence_MidTalk()
    {
        currentStep = TutorialStep.None;
        yield return StartCoroutine(PlayDialogue("추온", "오늘은 컨디션이 좋네, 검이 가벼워.", attackDelay));
        StartCoroutine(Sequence_Defense());
    }

    // ──────────────────────────────────────────────
    // 4. 방어 (시간 변수 적용)
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Defense()
    {
        yield return StartCoroutine(PlayDialogue("EGO", "이번엔 방어를 연습해보자. E를 홀드해 가드 할 수 있어.", defenseDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "항상 방어만 할 순 없지! 타이밍에 맞춰 가드 하면 패링이 발동돼.", defenseDelay));
        
        if (droneEnemy) droneEnemy.SetActive(true);
        
        yield return StartCoroutine(PlayDialogue("EGO", "간단하게 몇 번 연습해보자고.", defenseDelay));
        if (dialogueGroup) dialogueGroup.DOFade(0, 1f);

        currentStep = TutorialStep.Defense;
        FlashPanel(defenseColor);
        UpdateDefenseUI();
    }

    public void OnPlayerGuardSuccess()
    {
        if (currentStep != TutorialStep.Defense) return;
        if (currentGuardCount < targetGuardCount)
        {
            currentGuardCount++;
            PunchEffect();
            UpdateDefenseUI();
            CheckDefenseComplete();
        }
    }

    public void OnPlayerParrySuccess()
    {
        if (currentStep != TutorialStep.Defense) return;
        if (currentParryCount < targetParryCount)
        {
            currentParryCount++;
            PunchEffect();
            UpdateDefenseUI();
            CheckDefenseComplete();
        }
    }

    void UpdateDefenseUI()
    {
        UpdateQuestUI("방어 훈련", $"드론 공격 방어\n1. 가드 ({currentGuardCount}/{targetGuardCount})\n2. 패링 ({currentParryCount}/{targetParryCount})");
    }

    void CheckDefenseComplete()
    {
        if (currentGuardCount >= targetGuardCount && currentParryCount >= targetParryCount)
        {
            if (droneEnemy) droneEnemy.SetActive(false);
            StartCoroutine(Sequence_Heal());
        }
    }

    // ──────────────────────────────────────────────
    // 5. 회복 (시간 변수 적용)
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Heal()
    {
        currentStep = TutorialStep.None;
        yield return StartCoroutine(PlayDialogue("추온", "실패하면 위험하겠어, 이 감각을 잊지말자.", healDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "체력 회복은 Q를 입력해 회복 앰플을 사용하여 회복 할 수 있어.", healDelay));
        
        if (dialogueGroup) dialogueGroup.DOFade(0, 2f);

        currentStep = TutorialStep.Heal;
        if (playerHealth) playerHealth.ApplyDamage(50);

        FlashPanel(healColor);
        UpdateQuestUI("회복 훈련", "체력이 손실되었습니다.\n'Q' 키를 눌러 앰플을 사용하세요.");
    }

    void OnHealAction()
    {
        if (playerHealth) playerHealth.Heal(50);
        PunchEffect();
        StartCoroutine(Sequence_Complete());
    }

    // ──────────────────────────────────────────────
    // 6. 완료 (시간 변수 적용)
    // ──────────────────────────────────────────────
    IEnumerator Sequence_Complete()
    {
        currentStep = TutorialStep.Complete;
        yield return StartCoroutine(PlayDialogue("EGO", "좋아! 훈련은 모두 끝났어. 이제 실전이다.", completeDelay));
        if (dialogueGroup) dialogueGroup.DOFade(0, 2f);

        UpdateQuestUI("튜토리얼 완료", "모든 훈련을 마쳤습니다.큐브와 상호작용하여 이동하세요\n수고하셨습니다.");
        
        questPanelBg.DOColor(highlightColor, 0.2f).SetLoops(4, LoopType.Yoyo)
            .OnComplete(() => questPanelBg.color = originalColor);
    }

    // ──────────────────────────────────────────────
    // 헬퍼 함수
    // ──────────────────────────────────────────────
    IEnumerator PlayDialogue(string speaker, string content, float waitTime)
    {
        if (dialogueGroup) dialogueGroup.alpha = 1;
        if (speakerText) speakerText.text = speaker;
        
        if (contentText)
        {
            contentText.text = "";
            foreach (char c in content)
            {
                contentText.text += c;
                yield return new WaitForSeconds(0.05f); 
            }
        }
        
        yield return new WaitForSeconds(waitTime); // 설정된 시간만큼 대기
    }

    void ShowQuestPanel()
    {
        if (questPanelGroup)
        {
            questPanelGroup.alpha = 1;
            questPanelGroup.interactable = true;
            questPanelGroup.transform.DOPunchScale(Vector3.one * 0.15f, 0.5f);
        }
    }

    void UpdateQuestUI(string title, string desc)
    {
        if (questTitleText) questTitleText.text = title;
        if (questDescriptionText) questDescriptionText.text = desc;
    }

    void FlashPanel(Color color)
    {
        if (questPanelBg)
        {
            Sequence seq = DOTween.Sequence();
            seq.Append(questPanelBg.DOColor(color, 0.2f).SetLoops(4, LoopType.Yoyo));
            seq.AppendCallback(() => questPanelBg.color = originalColor);
            questPanelBg.transform.DOPunchScale(Vector3.one * 0.1f, 0.3f);
        }
    }

    void PunchEffect()
    {
        if (questDescriptionText)
            questDescriptionText.transform.DOPunchScale(Vector3.one * 0.2f, 0.15f);
    }
}