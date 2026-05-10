using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public class GameSceneBinder : MonoBehaviour
{
    const string MainSceneName = "MainScene";

    [Header("Main Scene Elevator Intro Camera")]
    [SerializeField] Vector3 elevatorIntroCameraLocalOffset = new Vector3(-3.2f, 1.25f, 9.0f);
    [SerializeField] Vector3 elevatorIntroCameraLookOffset = new Vector3(0f, 1.25f, 1.2f);

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

        if (SceneManager.GetActiveScene().name != MainSceneName)
            yield break;

        var arrivalController = GetComponent<MainSceneArrivalController>();
        if (arrivalController == null)
            arrivalController = gameObject.AddComponent<MainSceneArrivalController>();

        arrivalController.ConfigureElevatorIntroCamera(elevatorIntroCameraLocalOffset, elevatorIntroCameraLookOffset);
        arrivalController.ConfigureBossIntroDirector(FindBossIntroDirector());
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

    static PlayableDirector FindBossIntroDirector()
    {
        PlayableDirector[] directors = FindObjectsOfType<PlayableDirector>(true);
        for (int i = 0; i < directors.Length; i++)
        {
            PlayableDirector director = directors[i];
            if (director != null && director.name.Contains("MainSceneIntro"))
                return director;
        }

        return null;
    }
}
