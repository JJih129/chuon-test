using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LobbyCombatCoachController : MonoBehaviour
{
    [SerializeField] private LobbyManager lobbyManager;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private LobbyPresentationController presentationController;

    [Header("Cues")]
    [SerializeField] private string firstFireTitle = "\uc804\uc220 \ud53c\ub4dc\ubc31";
    [SerializeField] private string firstFireDescription = "\uc6d0\uac70\ub9ac \uacf5\uaca9\uc774\ub2e4. \uac00\ub4dc\ub85c \ubc1b\uc544\ub0b4\uac70\ub098 \ud68c\ud53c\ub85c \uac01\uc744 \ub04a\uc5b4.";
    [SerializeField] private string firstDamageTitle = "\ud53c\uaca9";
    [SerializeField] private string firstDamageDescription = "\uc2dc\uc57c\ub97c \uc5f4\uc5b4 \ub450. \ud0c4\ub3c4\ub294 \uc77d\uc744 \uc218 \uc788\uc5b4.";
    [SerializeField] private string firstKillTitle = "\uc804\uc9c4";
    [SerializeField] private string firstKillDescription = "\uc88b\uc544. \ud55c \uae30 \uc815\ub9ac\ud588\ub2e4. \ub2e4\uc74c \ud45c\uc801\uc744 \ubc14\ub85c \uc804\ud658\ud574.";
    [SerializeField] private string repeatTelegraphTitle = "\uad50\uc804 \ubcf4\uc870";
    [SerializeField] private string repeatTelegraphDescription = "\uacbd\uace0\uc120\uc774 \uc0ac\uaca9 \uac01\uc774\ub2e4. \ubc1b\uc544\ub0b4\uac70\ub098 \uce21\uba74\uc73c\ub85c \ube60\uc838.";
    [SerializeField, Min(0.3f)] private float firstFireCueDuration = 1.8f;
    [SerializeField, Min(0.3f)] private float firstDamageCueDuration = 1.6f;
    [SerializeField, Min(0.3f)] private float firstKillCueDuration = 1.5f;
    [SerializeField, Min(0.3f)] private float repeatTelegraphCueDuration = 1.2f;
    [SerializeField, Min(1)] private int guidedTelegraphCount = 3;
    [SerializeField, Min(0)] private int guidedTelegraphKillWindow = 1;
    [SerializeField, Min(0.05f)] private float telegraphAssistCooldown = 0.55f;

    [Header("Telegraph Difficulty Curve")]
    [SerializeField, Min(0f)] private float reducedTelegraphLeadTime = 0.28f;
    [SerializeField, Min(0f)] private float recoveryTelegraphLeadTime = 0.46f;
    [SerializeField, Min(0.1f)] private float recoveryTelegraphDuration = 4f;

    readonly HashSet<DroneController> _trackedDrones = new HashSet<DroneController>();

    bool _combatActive;
    bool _firstFireCueShown;
    bool _firstDamageCueShown;
    bool _firstKillCueShown;
    bool _repeatTelegraphCueShown;
    int _telegraphAssistCount;
    int _enemiesDefeated;
    float _lastTelegraphAssistTime = float.NegativeInfinity;
    float _activeTelegraphLeadTime;
    bool _reducedTelegraphMode;
    Coroutine _telegraphRecoveryRoutine;

    public void ConfigureRuntime(
        LobbyManager runtimeLobbyManager,
        PlayerHealth runtimePlayerHealth,
        LobbyPresentationController runtimePresentationController)
    {
        ReleaseSubscriptions();

        lobbyManager = runtimeLobbyManager;
        playerHealth = runtimePlayerHealth;
        presentationController = runtimePresentationController;

        RefreshSubscriptions();
    }

    void OnEnable()
    {
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void RefreshSubscriptions()
    {
        if (lobbyManager == null)
            lobbyManager = GetComponent<LobbyManager>();

        if (presentationController == null)
            presentationController = GetComponent<LobbyPresentationController>();

        if (playerHealth == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                playerHealth = player.GetComponent<PlayerHealth>();
        }

        if (lobbyManager != null)
        {
            lobbyManager.CombatStarted -= HandleCombatStarted;
            lobbyManager.CombatStarted += HandleCombatStarted;
            lobbyManager.DroneSpawned -= HandleDroneSpawned;
            lobbyManager.DroneSpawned += HandleDroneSpawned;
            lobbyManager.EnemyProgressUpdated -= HandleEnemyProgressUpdated;
            lobbyManager.EnemyProgressUpdated += HandleEnemyProgressUpdated;
            lobbyManager.CombatCompleted -= HandleCombatCompleted;
            lobbyManager.CombatCompleted += HandleCombatCompleted;
        }

        if (playerHealth != null)
        {
            playerHealth.OnDamaged -= HandlePlayerDamaged;
            playerHealth.OnDamaged += HandlePlayerDamaged;
        }
    }

    void ReleaseSubscriptions()
    {
        if (lobbyManager != null)
        {
            lobbyManager.CombatStarted -= HandleCombatStarted;
            lobbyManager.DroneSpawned -= HandleDroneSpawned;
            lobbyManager.EnemyProgressUpdated -= HandleEnemyProgressUpdated;
            lobbyManager.CombatCompleted -= HandleCombatCompleted;
        }

        if (playerHealth != null)
            playerHealth.OnDamaged -= HandlePlayerDamaged;

        foreach (DroneController drone in _trackedDrones)
        {
            if (drone == null)
                continue;

            drone.AttackTelegraphed -= HandleDroneAttackTelegraphed;
            drone.Died -= HandleDroneDied;
        }

        _trackedDrones.Clear();
        _combatActive = false;
        StopTelegraphRecovery();
    }

    void HandleCombatStarted()
    {
        _combatActive = true;
        _firstFireCueShown = false;
        _firstDamageCueShown = false;
        _firstKillCueShown = false;
        _repeatTelegraphCueShown = false;
        _telegraphAssistCount = 0;
        _enemiesDefeated = 0;
        _lastTelegraphAssistTime = float.NegativeInfinity;
        _reducedTelegraphMode = false;
        _activeTelegraphLeadTime = GetInitialTelegraphLeadTime();
        StopTelegraphRecovery();
        ApplyTelegraphLeadTime(_activeTelegraphLeadTime);
    }

    void HandleDroneSpawned(DroneController drone)
    {
        if (drone == null || _trackedDrones.Contains(drone))
            return;

        // Combat cues stay event-driven; no per-frame polling is added for lobby coaching.
        _trackedDrones.Add(drone);
        drone.AttackTelegraphed += HandleDroneAttackTelegraphed;
        drone.Died += HandleDroneDied;
        drone.SetAttackTelegraph(_activeTelegraphLeadTime);
    }

    void HandleDroneAttackTelegraphed(DroneController drone)
    {
        if (!_combatActive || lobbyManager == null)
            return;

        if (!ShouldShowTelegraphAssist())
            return;

        Transform origin = drone != null && drone.fireOrigin != null ? drone.fireOrigin : (drone != null ? drone.transform : null);
        Vector3 targetPoint = playerHealth != null ? playerHealth.transform.position : (drone != null && drone.target != null ? drone.target.position : Vector3.zero);

        _telegraphAssistCount++;
        _lastTelegraphAssistTime = Time.unscaledTime;
        presentationController?.ShowShotTelegraph(origin, targetPoint, GetTelegraphCueDuration());

        if (!_firstFireCueShown)
        {
            _firstFireCueShown = true;
            lobbyManager.ShowTransientQuestCue(firstFireTitle, firstFireDescription, firstFireCueDuration, true);
            return;
        }

        if (_repeatTelegraphCueShown)
            return;

        _repeatTelegraphCueShown = true;
        lobbyManager.ShowTransientQuestCue(repeatTelegraphTitle, repeatTelegraphDescription, repeatTelegraphCueDuration, false);
    }

    void HandlePlayerDamaged(int damage)
    {
        if (!_combatActive || _firstDamageCueShown || lobbyManager == null)
        {
            if (_combatActive && _reducedTelegraphMode)
                StartRecoveryTelegraphWindow();

            return;
        }

        _firstDamageCueShown = true;
        lobbyManager.ShowTransientQuestCue(firstDamageTitle, firstDamageDescription, firstDamageCueDuration, true);

        if (_reducedTelegraphMode)
            StartRecoveryTelegraphWindow();
    }

    void HandleEnemyProgressUpdated(int current, int total)
    {
        _enemiesDefeated = Mathf.Max(0, current);

        if (_combatActive && !_reducedTelegraphMode && _enemiesDefeated > 0)
            ApplyReducedTelegraphMode();

        if (!_combatActive || _firstKillCueShown || current <= 0 || lobbyManager == null)
            return;

        _firstKillCueShown = true;
        lobbyManager.ShowTransientQuestCue(firstKillTitle, firstKillDescription, firstKillCueDuration, false);
    }

    void HandleCombatCompleted()
    {
        _combatActive = false;
        StopTelegraphRecovery();
    }

    void HandleDroneDied(DroneController drone)
    {
        if (drone == null || !_trackedDrones.Remove(drone))
            return;

        drone.AttackTelegraphed -= HandleDroneAttackTelegraphed;
        drone.Died -= HandleDroneDied;
    }

    void ApplyTelegraphLeadTime(float leadTime)
    {
        _activeTelegraphLeadTime = Mathf.Max(0f, leadTime);

        foreach (DroneController drone in _trackedDrones)
        {
            if (drone == null)
                continue;

            drone.SetAttackTelegraph(_activeTelegraphLeadTime);
        }
    }

    void ApplyReducedTelegraphMode()
    {
        _reducedTelegraphMode = true;
        StopTelegraphRecovery();
        ApplyTelegraphLeadTime(reducedTelegraphLeadTime);
    }

    void StartRecoveryTelegraphWindow()
    {
        if (!_combatActive)
            return;

        if (recoveryTelegraphLeadTime <= _activeTelegraphLeadTime)
            return;

        ApplyTelegraphLeadTime(recoveryTelegraphLeadTime);

        if (_telegraphRecoveryRoutine != null)
            StopCoroutine(_telegraphRecoveryRoutine);

        _telegraphRecoveryRoutine = StartCoroutine(CoRecoverTelegraphLeadTime());
    }

    IEnumerator CoRecoverTelegraphLeadTime()
    {
        yield return new WaitForSecondsRealtime(recoveryTelegraphDuration);

        _telegraphRecoveryRoutine = null;

        if (!_combatActive)
            yield break;

        if (_reducedTelegraphMode)
            ApplyTelegraphLeadTime(reducedTelegraphLeadTime);
        else
            ApplyTelegraphLeadTime(GetInitialTelegraphLeadTime());
    }

    void StopTelegraphRecovery()
    {
        if (_telegraphRecoveryRoutine == null)
            return;

        StopCoroutine(_telegraphRecoveryRoutine);
        _telegraphRecoveryRoutine = null;
    }

    bool ShouldShowTelegraphAssist()
    {
        if (guidedTelegraphCount <= 0)
            return false;

        if (_telegraphAssistCount >= guidedTelegraphCount)
            return false;

        if (_enemiesDefeated > guidedTelegraphKillWindow)
            return false;

        if (Time.unscaledTime - _lastTelegraphAssistTime < telegraphAssistCooldown)
            return false;

        return true;
    }

    float GetTelegraphCueDuration()
    {
        if (!_firstFireCueShown)
            return firstFireCueDuration;

        return repeatTelegraphCueDuration;
    }

    float GetInitialTelegraphLeadTime()
    {
        if (lobbyManager == null)
            return recoveryTelegraphLeadTime;

        return Mathf.Max(0f, lobbyManager.droneAttackTelegraphLeadTime);
    }
}
