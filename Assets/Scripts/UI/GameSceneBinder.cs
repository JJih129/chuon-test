using UnityEngine;

public class GameSceneBinder : MonoBehaviour
{
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
        if (hud && ph) hud.Bind(ph, pc, pg, boss);

        var arrivalController = GetComponent<MainSceneArrivalController>();
        if (arrivalController == null)
            arrivalController = gameObject.AddComponent<MainSceneArrivalController>();

        arrivalController.ConfigureRuntime(hud, boss, ph, bossBreak, ultimate);

        var bossStatusController = GetComponent<MainSceneBossStatusController>();
        if (bossStatusController == null)
            bossStatusController = gameObject.AddComponent<MainSceneBossStatusController>();

        var bossUiController = FindObjectOfType<BossUIController>(true);
        bossStatusController.ConfigureRuntime(bossUiController, boss, bossBreak, ultimate);

        var bossObjectiveController = GetComponent<MainSceneObjectivePanelController>();
        if (bossObjectiveController == null)
            bossObjectiveController = gameObject.AddComponent<MainSceneObjectivePanelController>();

        bossObjectiveController.ConfigureRuntime(bossUiController, boss, bossBreak, ultimate);

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

        var postClearGuideController = GetComponent<MainScenePostClearGuideController>();
        if (postClearGuideController == null)
            postClearGuideController = gameObject.AddComponent<MainScenePostClearGuideController>();

        Transform playerRoot = ph != null ? ph.transform : null;
        postClearGuideController.ConfigureRuntime(arrivalController, hud, bossHealth, playerRoot, clearExitBridge);

        var deathPresentationController = GetComponent<MainSceneDeathPresentationController>();
        if (deathPresentationController == null)
            deathPresentationController = gameObject.AddComponent<MainSceneDeathPresentationController>();

        deathPresentationController.ConfigureRuntime(hud, ph, bossUiController);
    }
}
