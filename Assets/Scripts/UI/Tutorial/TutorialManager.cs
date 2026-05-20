using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum TutorialStep
{
    None,
    Movement,
    Attack,
    Defense,
    Heal,
    Complete
}

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance;

    [Header("Dialogue UI")]
    public TextMeshProUGUI centerTitleText;
    public CanvasGroup dialogueGroup;
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI contentText;

    [Header("Quest UI")]
    public CanvasGroup questPanelGroup;
    public Image questPanelBg;
    public TextMeshProUGUI questTitleText;
    public TextMeshProUGUI questDescriptionText;
    public GameObject comboGuidePanel;
    public Image tutorialMouseReferenceCue;
    public Sprite tutorialMouseReferenceSprite;

    [Header("Tutorial Objects")]
    public GameObject movementGoal;
    public GameObject attackDummy;
    public GameObject droneEnemy;
    public PlayerHealth playerHealth;

    [Header("Timing")]
    public float introDelay = 2.0f;
    public float moveDelay = 2.0f;
    public float attackDelay = 2.0f;
    public float defenseDelay = 2.5f;
    public float healDelay = 2.0f;
    public float completeDelay = 3.0f;
    [Range(0.01f, 0.2f)] public float textTypingSpeed = 0.05f;

    [Header("Training Counts")]
    public int targetDodgeCount = 5;
    public int targetGuardCount = 5;
    public int targetParryCount = 5;

    TutorialStep currentStep = TutorialStep.None;
    int currentDodgeCount;
    bool isGoalReached;
    int currentAttackCount;
    int maxAttackCount = 10;
    int currentGuardCount;
    int currentParryCount;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    void Start()
    {
        if (centerTitleText != null)
        {
            centerTitleText.text = string.Empty;
            centerTitleText.gameObject.SetActive(true);
        }

        if (dialogueGroup != null)
            dialogueGroup.alpha = 0f;

        if (questPanelGroup != null)
        {
            questPanelGroup.interactable = false;
            questPanelGroup.blocksRaycasts = false;
        }

        if (comboGuidePanel != null)
            comboGuidePanel.SetActive(false);

        EnsureMouseReferenceCue();
        SetMouseReferenceCueVisible(false);

        if (movementGoal != null) movementGoal.SetActive(false);
        if (attackDummy != null) attackDummy.SetActive(false);
        if (droneEnemy != null) droneEnemy.SetActive(false);

        StartCoroutine(Sequence_Intro());
    }

    void Update()
    {
        if (currentStep == TutorialStep.Movement && Input.GetKeyDown(KeyCode.LeftShift))
            OnDodgeAction();

        if (currentStep == TutorialStep.Heal && Input.GetKeyDown(KeyCode.Q))
            OnHealAction();
    }

    IEnumerator Sequence_Intro()
    {
        if (centerTitleText != null)
        {
            centerTitleText.text = "가상현실 부트 완료";
            centerTitleText.DOFade(1f, 0.5f);
        }

        yield return StartCoroutine(PlayDialogue("EGO", "가상현실 부트 완료.", introDelay));

        if (centerTitleText != null)
            centerTitleText.DOFade(0f, 0.5f);

        yield return StartCoroutine(PlayDialogue("EGO", "실전에 들어가기 전 마지막 훈련을 진행하자.", introDelay));
        yield return StartCoroutine(PlayDialogue("추온", "장비는 체크됐어. 바로 시작하자.", introDelay));

        StartCoroutine(Sequence_Movement());
    }

    IEnumerator Sequence_Movement()
    {
        yield return StartCoroutine(PlayDialogue("EGO", "먼저 WASD로 지정 위치까지 이동해.", moveDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "시야는 마우스로 조작하고, Shift로 회피할 수 있어.", moveDelay));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        currentStep = TutorialStep.Movement;
        if (movementGoal != null)
            movementGoal.SetActive(true);

        PunchEffect();
        UpdateMovementUI();
    }

    void OnDodgeAction()
    {
        if (currentDodgeCount >= targetDodgeCount)
            return;

        currentDodgeCount++;
        PunchEffect();
        UpdateMovementUI();
        CheckMovementComplete();
    }

    public void OnGoalReached()
    {
        if (isGoalReached)
            return;

        isGoalReached = true;
        PunchEffect();
        UpdateMovementUI();
        CheckMovementComplete();
    }

    void UpdateMovementUI()
    {
        string goalState = isGoalReached ? "<color=green>(완료)</color>" : "(진행중)";
        UpdateQuestUI("기초 이동", $"WASD로 지정 위치로 이동 {goalState}\nShift로 회피 ({currentDodgeCount}/{targetDodgeCount})");
    }

    void CheckMovementComplete()
    {
        if (!isGoalReached || currentDodgeCount < targetDodgeCount)
            return;

        if (movementGoal != null)
            movementGoal.SetActive(false);

        StartCoroutine(Sequence_Attack());
    }

    IEnumerator Sequence_Attack()
    {
        currentStep = TutorialStep.None;
        yield return StartCoroutine(PlayDialogue("EGO", "이제 기본 공격을 확인하자.", attackDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "마우스 좌클릭으로 약공격, 우클릭으로 강공격을 사용할 수 있어.", attackDelay));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        if (comboGuidePanel != null)
        {
            comboGuidePanel.SetActive(true);
            comboGuidePanel.transform.DOPunchScale(Vector3.one * 0.1f, 0.3f);
        }

        SetMouseReferenceCueVisible(true);
        currentStep = TutorialStep.Attack;
        if (attackDummy != null)
            attackDummy.SetActive(true);

        PunchEffect();
        UpdateAttackUI();
    }

    public void OnEnemyHit()
    {
        if (currentStep != TutorialStep.Attack || currentAttackCount >= maxAttackCount)
            return;

        currentAttackCount++;
        UpdateAttackUI();
        PunchEffect();

        if (currentAttackCount < maxAttackCount)
            return;

        if (attackDummy != null)
            attackDummy.SetActive(false);
        if (comboGuidePanel != null)
            comboGuidePanel.SetActive(false);

        SetMouseReferenceCueVisible(false);
        StartCoroutine(Sequence_MidTalk());
    }

    void UpdateAttackUI()
    {
        UpdateQuestUI("기본 공격", $"마우스 좌클릭으로 공격해\n허수아비 공격 ({currentAttackCount}/{maxAttackCount})");
    }

    IEnumerator Sequence_MidTalk()
    {
        currentStep = TutorialStep.None;
        yield return StartCoroutine(PlayDialogue("추온", "컨디션은 괜찮아. 다음 훈련으로 가자.", attackDelay));
        StartCoroutine(Sequence_Defense());
    }

    IEnumerator Sequence_Defense()
    {
        yield return StartCoroutine(PlayDialogue("EGO", "이번에는 방어와 패링을 연습해.", defenseDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "E키로 방어하고, 공격 타이밍에 맞추면 패링이 발동돼.", defenseDelay));

        if (droneEnemy != null)
            droneEnemy.SetActive(true);

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        currentStep = TutorialStep.Defense;
        PunchEffect();
        UpdateDefenseUI();
    }

    public void OnPlayerGuardSuccess()
    {
        if (currentStep != TutorialStep.Defense || currentGuardCount >= targetGuardCount)
            return;

        currentGuardCount++;
        PunchEffect();
        UpdateDefenseUI();
        CheckDefenseComplete();
    }

    public void OnPlayerParrySuccess()
    {
        if (currentStep != TutorialStep.Defense || currentParryCount >= targetParryCount)
            return;

        currentParryCount++;
        PunchEffect();
        UpdateDefenseUI();
        CheckDefenseComplete();
    }

    void UpdateDefenseUI()
    {
        UpdateQuestUI("방어 훈련", $"E키로 방어 ({currentGuardCount}/{targetGuardCount})\n공격 타이밍에 E키로 패링 ({currentParryCount}/{targetParryCount})");
    }

    void CheckDefenseComplete()
    {
        if (currentGuardCount < targetGuardCount || currentParryCount < targetParryCount)
            return;

        if (droneEnemy != null)
            droneEnemy.SetActive(false);

        StartCoroutine(Sequence_Heal());
    }

    IEnumerator Sequence_Heal()
    {
        currentStep = TutorialStep.None;
        yield return StartCoroutine(PlayDialogue("추온", "실패하면 바로 회복하는 감각도 중요하지.", healDelay));
        yield return StartCoroutine(PlayDialogue("EGO", "Q키로 앰플을 사용해 체력을 회복할 수 있어.", healDelay));

        currentStep = TutorialStep.Heal;
        if (playerHealth != null)
            playerHealth.ApplyDamage(50);

        PunchEffect();
        UpdateQuestUI("회복 훈련", "Q키로 앰플을 사용해\n체력을 회복해");
    }

    void OnHealAction()
    {
        if (playerHealth != null)
            playerHealth.Heal(50);

        PunchEffect();
        StartCoroutine(Sequence_Complete());
    }

    IEnumerator Sequence_Complete()
    {
        currentStep = TutorialStep.Complete;
        yield return StartCoroutine(PlayDialogue("EGO", "좋아. 기본 훈련은 끝났어.", completeDelay));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        SetMouseReferenceCueVisible(true);
        UpdateQuestUI("튜토리얼 완료", "마우스 좌클릭/우클릭으로 공격해\nE 방어, Shift 회피, Q 회복");
        PunchEffect();
    }

    IEnumerator PlayDialogue(string speaker, string content, float waitTime)
    {
        if (dialogueGroup != null)
            dialogueGroup.alpha = 1f;
        if (speakerText != null)
            speakerText.text = speaker;

        if (contentText != null)
        {
            contentText.text = string.Empty;
            foreach (char c in content)
            {
                contentText.text += c;
                yield return new WaitForSeconds(textTypingSpeed);
            }
        }

        yield return new WaitForSeconds(waitTime);
    }

    void ShowQuestPanel()
    {
        if (questPanelGroup == null)
            return;

        questPanelGroup.alpha = 1f;
        questPanelGroup.interactable = true;
        questPanelGroup.transform.DOPunchScale(Vector3.one * 0.15f, 0.5f);
    }

    void UpdateQuestUI(string title, string desc)
    {
        ShowQuestPanel();
        if (questTitleText != null)
            questTitleText.text = title;
        if (questDescriptionText != null)
            questDescriptionText.text = desc;
    }

    void EnsureMouseReferenceCue()
    {
        if (tutorialMouseReferenceCue != null)
            return;

        Sprite sprite = tutorialMouseReferenceSprite;
#if UNITY_EDITOR
        if (sprite == null)
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/마우스.png");
#endif
        if (sprite == null || questPanelGroup == null)
            return;

        Canvas canvas = questPanelGroup.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        GameObject cueObject = new GameObject("TutorialMouseReferenceCue", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        cueObject.transform.SetParent(canvas.transform, false);

        tutorialMouseReferenceCue = cueObject.GetComponent<Image>();
        tutorialMouseReferenceCue.sprite = sprite;
        tutorialMouseReferenceCue.preserveAspect = true;
        tutorialMouseReferenceCue.raycastTarget = false;

        RectTransform rect = tutorialMouseReferenceCue.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -18f);
        rect.sizeDelta = new Vector2(520f, 520f);
    }

    void SetMouseReferenceCueVisible(bool visible)
    {
        EnsureMouseReferenceCue();
        if (tutorialMouseReferenceCue == null)
            return;

        tutorialMouseReferenceCue.gameObject.SetActive(visible);
        if (visible)
            tutorialMouseReferenceCue.transform.DOPunchScale(Vector3.one * 0.06f, 0.25f);
    }

    void PunchEffect()
    {
        if (questPanelGroup != null)
            questPanelGroup.transform.DOPunchScale(Vector3.one * 0.05f, 0.2f);
    }
}
