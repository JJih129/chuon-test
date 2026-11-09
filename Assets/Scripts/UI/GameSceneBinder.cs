using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSceneBinder : MonoBehaviour
{
    const string MainSceneName = "MainScene";

    // 씬 이름 대신 직렬화 플래그로 보스 레벨 바인딩 여부 결정 (MainScene은 이름으로도 계속 동작)
    [SerializeField] bool bindAsMainLevel = false;

    bool ShouldBindAsMainLevel => bindAsMainLevel || SceneManager.GetActiveScene().name == MainSceneName;

    System.Collections.IEnumerator Start()
    {
        yield return null;

        var hud = FindObjectOfType<PlayerHUD>(true);
        var ph = FindObjectOfType<PlayerHealth>(true);
        var pc = FindObjectOfType<PlayerConsumables>(true);
        var pg = FindObjectOfType<PlayerGuardController>(true);
        var boss = FindObjectOfType<BossController>(true);
        var bossBreak = FindObjectOfType<BossBreakController>(true);
        var ultimate = FindObjectOfType<PlayerUltimateController>(true);
        if (hud && ph) hud.Bind(ph, pc, pg, boss, ultimate);

        if (!ShouldBindAsMainLevel)
            yield break;

        var arrivalController = GetComponent<MainSceneArrivalController>();
        if (arrivalController == null)
            arrivalController = gameObject.AddComponent<MainSceneArrivalController>();

        arrivalController.ConfigureRuntime(hud, boss, ph, bossBreak, ultimate);

        var bossUiController = FindObjectOfType<BossUIController>(true);
        MainSceneBossStatusController bossStatusController = null;
        if (ExhibitionPrototypePresentationPolicy.RuntimeBossStatusPanelEnabled)
        {
            bossStatusController = GetComponent<MainSceneBossStatusController>();
            if (bossStatusController == null)
                bossStatusController = gameObject.AddComponent<MainSceneBossStatusController>();

            bossStatusController.ConfigureRuntime(bossUiController, boss, bossBreak, ultimate);
        }
        else
        {
            bossStatusController = GetComponent<MainSceneBossStatusController>();
            if (bossStatusController != null)
                bossStatusController.enabled = false;
            RemoveRuntimeBossStatusPanel();
            bossStatusController = null;
        }

        MainSceneObjectivePanelController bossObjectiveController = null;
        if (ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
        {
            bossObjectiveController = GetComponent<MainSceneObjectivePanelController>();
            if (bossObjectiveController == null)
                bossObjectiveController = gameObject.AddComponent<MainSceneObjectivePanelController>();

            bossObjectiveController.ConfigureRuntime(bossUiController, boss, bossBreak, ultimate);
        }
        else
        {
            bossObjectiveController = GetComponent<MainSceneObjectivePanelController>();
            if (bossObjectiveController != null)
                bossObjectiveController.enabled = false;
            RemoveRuntimeBossObjectivePanel();
        }

        // 씬 배치 퀘스트 패널(QuastBG) 채우기: 런타임 프로토타입 패널이 꺼져 있어 아무도 바인딩하지 않던 문제
        var questPanelPresenter = GetComponent<MainSceneQuestPanelPresenter>();
        if (questPanelPresenter == null)
            questPanelPresenter = gameObject.AddComponent<MainSceneQuestPanelPresenter>();
        questPanelPresenter.ConfigureRuntime(hud, boss, bossBreak, ultimate);

        var consumables = FindObjectOfType<PlayerConsumables>(true);
        var combatAssistController = GetComponent<MainSceneCombatAssistController>();
        if (combatAssistController == null)
            combatAssistController = gameObject.AddComponent<MainSceneCombatAssistController>();

        combatAssistController.ConfigureRuntime(arrivalController, hud, ph, consumables, bossBreak, ultimate);

        var bossHealth = boss != null ? boss.bossHealth : FindObjectOfType<BossHealth>(true);
        var clearPresentationController = GetComponent<MainSceneClearPresentationController>();
        if (clearPresentationController == null)
            clearPresentationController = gameObject.AddComponent<MainSceneClearPresentationController>();

        clearPresentationController.ConfigureRuntime(arrivalController, hud, bossHealth, bossStatusController, bossObjectiveController);

        var clearExitBridge = GetComponent<MainSceneClearExitBridge>();
        if (clearExitBridge == null)
            clearExitBridge = gameObject.AddComponent<MainSceneClearExitBridge>();

        clearExitBridge.ConfigureRuntime(arrivalController, hud, bossHealth);

        Transform playerRoot = ph != null ? ph.transform : null;
        if (ExhibitionPrototypePresentationPolicy.RuntimePrototypeGuidesEnabled)
        {
            var postClearGuideController = GetComponent<MainScenePostClearGuideController>();
            if (postClearGuideController == null)
                postClearGuideController = gameObject.AddComponent<MainScenePostClearGuideController>();

            postClearGuideController.ConfigureRuntime(arrivalController, hud, bossHealth, playerRoot, clearExitBridge);
        }
        else
        {
            var postClearGuideController = GetComponent<MainScenePostClearGuideController>();
            if (postClearGuideController != null)
                postClearGuideController.enabled = false;
        }

        var deathPresentationController = GetComponent<MainSceneDeathPresentationController>();
        if (deathPresentationController == null)
            deathPresentationController = gameObject.AddComponent<MainSceneDeathPresentationController>();

        deathPresentationController.ConfigureRuntime(hud, ph, bossUiController);
    }

    static void RemoveRuntimeBossObjectivePanel()
    {
        RectTransform[] rects = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == "RuntimeBossObjective")
                Destroy(rect.gameObject);
        }
    }

    static void RemoveRuntimeBossStatusPanel()
    {
        RectTransform[] rects = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == "RuntimeBossStatus")
                Destroy(rect.gameObject);
        }
    }
}
