using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    [Header("Dialogue UI")]
    public CanvasGroup dialogueGroup;
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI contentText;
    public Image alertOverlay;

    [Header("Quest UI")]
    public CanvasGroup questPanelGroup;
    public TextMeshProUGUI questTitleText;
    public TextMeshProUGUI questDescriptionText;

    [Header("Combat")]
    public GameObject dronePrefab;
    public Transform[] spawnPoints;
    [Range(0f, 0.3f)] public float droneSpawnInterval = 0.08f;
    [Range(0f, 1f)] public float droneInitialFireStagger = 0.12f;
    [Range(0f, 2f)] public float droneInitialFireBaseDelay = 0.75f;
    public bool disableLobbyDroneWorldUi = true;
    public bool disableLobbyDroneShadows = true;
    public bool simplifyLobbyDroneVisual = true;
    public bool useLightweightLobbyDroneSimulation = true;
    [Range(0.016f, 0.12f)] public float lightweightLobbyDroneTickInterval = 0.05f;
    int _totalEnemyCount;
    int _currentDeadEnemy;

    [Header("Elevator")]
    public GameObject elevatorPanel;
    public string nextSceneName = "MainScene";

    [Header("Elevator Door")]
    public Transform doorLeft;
    public Transform doorRight;
    public float doorOpenHeight = 4f;
    public float doorOpenSpeed = 2f;

    [Header("Guide / Triggers")]
    public GuidePathSystem guideSystem;
    public Transform triggerApproach;
    public Transform triggerBoard;
    public Transform[] pathWaypoints;

    [Header("Typing")]
    [Range(0.01f, 0.2f)] public float textTypingSpeed = 0.05f;
    [SerializeField] bool debugLogs = false;

    readonly List<Transform> _activeWaypoints = new List<Transform>();
    bool _isDoorReached;
    bool _isBoarded;
    bool _isBranchSelected;

    public bool IsDoorReached => _isDoorReached;
    public bool IsBoarded => _isBoarded;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
    }

    void Start()
    {
        if (dialogueGroup != null)
            dialogueGroup.alpha = 0f;

        if (questPanelGroup != null)
        {
            questPanelGroup.alpha = 0f;
            questPanelGroup.interactable = false;
        }

        if (alertOverlay != null)
            alertOverlay.color = new Color(1f, 0f, 0f, 0f);

        if (elevatorPanel != null)
            elevatorPanel.SetActive(false);

        StartCoroutine(SequenceArrival());
    }

    IEnumerator SequenceArrival()
    {
        yield return StartCoroutine(PlayDialogue("ChuOn", "This is the breach point.", 2f));

        if (alertOverlay != null)
            alertOverlay.DOFade(0.3f, 0.5f).SetLoops(6, LoopType.Yoyo);

        yield return StartCoroutine(PlayDialogue("System", "Intruders detected. Combat protocol engaged.", 3f));
        yield return StartCoroutine(PlayDialogue("EGO", "No other route. We clear them first, then move.", 2.5f));
        yield return StartCoroutine(PlayDialogue("ChuOn", "Fine. Let's move.", 2f));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        StartCoroutine(SequenceCombat());
    }

    IEnumerator SequenceCombat()
    {
        _currentDeadEnemy = 0;
        UpdateQuestUI("Current Objective", "Eliminate all hostile targets.");
        ShowQuestPanel();

        if (dronePrefab != null && spawnPoints != null && spawnPoints.Length > 0)
        {
            _totalEnemyCount = spawnPoints.Length;
            GameObject player = GameObject.FindWithTag("Player");
            Transform playerTransform = player != null ? player.transform : null;

            PrewarmLobbyProjectilePool();
            yield return StartCoroutine(SpawnLobbyDrones(playerTransform));
        }
        else
        {
            _totalEnemyCount = 1;
            OnEnemyKilled();
        }

        UpdateQuestUI("Combat Start", $"Eliminate enemies ({_currentDeadEnemy} / {_totalEnemyCount})");
        yield return new WaitUntil(() => _currentDeadEnemy >= _totalEnemyCount);
        yield return new WaitForSeconds(1f);
        StartCoroutine(SequencePostCombat());
    }

    public void OnEnemyKilled()
    {
        _currentDeadEnemy++;
        UpdateQuestUI("Combat", $"Eliminate enemies ({_currentDeadEnemy} / {_totalEnemyCount})");
        if (questDescriptionText != null)
            questDescriptionText.transform.DOPunchScale(Vector3.one * 0.2f, 0.15f);
    }

    void PrewarmLobbyProjectilePool()
    {
        if (dronePrefab == null || spawnPoints == null)
            return;

        DroneController droneTemplate = dronePrefab.GetComponent<DroneController>();
        if (droneTemplate == null || droneTemplate.projectilePrefab == null)
            return;

        int prewarmCount = Mathf.Max(6, spawnPoints.Length * 2);
        RuntimeObjectPool.Prewarm(droneTemplate.projectilePrefab, prewarmCount);
    }

    IEnumerator SpawnLobbyDrones(Transform playerTransform)
    {
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Transform spawnPoint = spawnPoints[i];
            if (spawnPoint == null)
                continue;

            GameObject drone = Instantiate(dronePrefab, spawnPoint.position, spawnPoint.rotation);
            PrepareLobbyDrone(drone);
            DroneController controller = drone.GetComponent<DroneController>();
            if (controller != null)
            {
                controller.target = playerTransform;
                controller.SetLightweightSimulation(useLightweightLobbyDroneSimulation, lightweightLobbyDroneTickInterval);
                controller.enabled = true;
                controller.SetInitialFireDelay(droneInitialFireBaseDelay + (droneInitialFireStagger * i));
            }

            if (drone.GetComponent<LobbyEnemy>() == null)
                drone.AddComponent<LobbyEnemy>();

            if (droneSpawnInterval > 0f && i < spawnPoints.Length - 1)
                yield return new WaitForSeconds(droneSpawnInterval);
        }
    }

    void PrepareLobbyDrone(GameObject drone)
    {
        if (drone == null)
            return;

        if (disableLobbyDroneWorldUi)
        {
            ProximityBossUI proximityUi = drone.GetComponent<ProximityBossUI>();
            if (proximityUi != null && proximityUi.enabled)
                proximityUi.enabled = false;
        }

        if (simplifyLobbyDroneVisual)
            SimplifyLobbyDroneVisual(drone);

        if (!disableLobbyDroneShadows)
            return;

        Renderer[] renderers = drone.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }

    void SimplifyLobbyDroneVisual(GameObject drone)
    {
        Transform[] children = drone.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null || child == drone.transform)
                continue;

            if (!child.name.Equals("drone"))
                continue;

            child.gameObject.SetActive(false);

            Animator childAnimator = child.GetComponentInChildren<Animator>(true);
            if (childAnimator != null)
                childAnimator.enabled = false;

            break;
        }

        Transform simpleVisual = drone.transform.Find("__SimpleDroneVisual");
        if (simpleVisual == null)
        {
            GameObject simpleVisualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            simpleVisualGo.name = "__SimpleDroneVisual";
            simpleVisualGo.layer = drone.layer;

            Transform simpleTransform = simpleVisualGo.transform;
            simpleTransform.SetParent(drone.transform, false);
            simpleTransform.localPosition = new Vector3(0f, 0.3f, 0f);
            simpleTransform.localRotation = Quaternion.identity;
            simpleTransform.localScale = new Vector3(0.7f, 0.22f, 0.7f);

            Collider simpleCollider = simpleVisualGo.GetComponent<Collider>();
            if (simpleCollider != null)
                Destroy(simpleCollider);

            Renderer simpleRenderer = simpleVisualGo.GetComponent<Renderer>();
            if (simpleRenderer != null)
            {
                simpleRenderer.shadowCastingMode = ShadowCastingMode.Off;
                simpleRenderer.receiveShadows = false;
                simpleRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
        }
    }

    IEnumerator SequencePostCombat()
    {
        UpdateQuestUI("Combat Complete", "All enemies defeated.");
        _isDoorReached = false;

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(PlayDialogue("EGO", "Good. The route is open. Head to the elevator.", 2f));
        yield return StartCoroutine(PlayDialogue("ChuOn", "Stay sharp until we're out.", 1.5f));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        UpdateQuestUI("Move", "Go to the elevator at the end of the corridor.");

        if (guideSystem != null)
        {
            _activeWaypoints.Clear();
            if (pathWaypoints != null)
            {
                for (int i = 0; i < pathWaypoints.Length; i++)
                {
                    Transform waypoint = pathWaypoints[i];
                    if (waypoint == null)
                        continue;

                    waypoint.gameObject.SetActive(true);
                    _activeWaypoints.Add(waypoint);
                }
            }

            UpdateNavigationPath();
        }

        yield return new WaitUntil(() => _isDoorReached);
        StartCoroutine(SequenceAtElevator());
    }

    public void OnWaypointReached(Transform reachedPoint)
    {
        if (!_activeWaypoints.Contains(reachedPoint))
            return;

        _activeWaypoints.Remove(reachedPoint);
        UpdateNavigationPath();
    }

    void UpdateNavigationPath()
    {
        if (guideSystem == null)
            return;

        List<Transform> pathList = new List<Transform>(_activeWaypoints.Count + 1);
        pathList.AddRange(_activeWaypoints);
        if (triggerApproach != null)
            pathList.Add(triggerApproach);
        guideSystem.ShowPath(pathList.ToArray());
    }

    public void OnReachElevator()
    {
        _isDoorReached = true;
        if (guideSystem != null)
            guideSystem.HidePath();
    }

    IEnumerator SequenceAtElevator()
    {
        OpenDoor(doorLeft);
        OpenDoor(doorRight);

        yield return StartCoroutine(PlayDialogue("ChuOn", "This does not look like a normal elevator.", 2f));
        yield return StartCoroutine(PlayDialogue("EGO", "It is still the only route forward. Get inside.", 2f));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        UpdateQuestUI("Move", "Board the elevator.");

        if (guideSystem != null && triggerBoard != null)
            guideSystem.ShowPath(triggerBoard);

        _isBoarded = false;
        yield return new WaitUntil(() => _isBoarded);

        if (guideSystem != null)
            guideSystem.HidePath();

        StartCoroutine(SequenceInside());
    }

    void OpenDoor(Transform door)
    {
        if (door == null)
            return;

        Collider collider = door.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = true;

        Vector3 localPosition = door.localPosition;
        door.DOLocalMoveY(localPosition.y + doorOpenHeight, doorOpenSpeed).SetEase(Ease.OutQuad);
    }

    public void OnEnterElevator()
    {
        _isBoarded = true;
    }

    IEnumerator SequenceInside()
    {
        yield return StartCoroutine(PlayDialogue("EGO", "This lift only links emergency routes.", 2f));
        yield return StartCoroutine(PlayDialogue("ChuOn", "Then we keep moving.", 2f));
        yield return StartCoroutine(PlayDialogue("EGO", "Once activated, it should connect to the next floor.", 2f));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        UpdateQuestUI("Objective", "Operate the elevator and move to the next floor.");

        if (elevatorPanel != null)
        {
            elevatorPanel.SetActive(true);
            elevatorPanel.transform.DOPunchScale(Vector3.one * 0.2f, 0.5f);
        }
    }

    public void OnInteractElevator()
    {
        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeOutAndLoadScene(nextSceneName);
            return;
        }

        SceneManager.LoadScene(nextSceneName);
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
            for (int i = 0; i < content.Length; i++)
            {
                contentText.text += content[i];
                yield return new WaitForSeconds(textTypingSpeed);
            }
        }

        yield return new WaitForSeconds(waitTime);
    }

    public void OnBranchSelected()
    {
        if (_isBranchSelected)
            return;

        _isBranchSelected = true;
        if (debugLogs)
            Debug.Log(">> [LobbyManager] Branch selected.", this);
    }

    void UpdateQuestUI(string title, string desc)
    {
        if (questTitleText != null)
            questTitleText.text = title;
        if (questDescriptionText != null)
            questDescriptionText.text = desc;
    }

    void ShowQuestPanel()
    {
        if (questPanelGroup == null)
            return;

        questPanelGroup.DOFade(1f, 0.5f);
        questPanelGroup.transform.DOPunchScale(Vector3.one * 0.1f, 0.3f);
    }
}
