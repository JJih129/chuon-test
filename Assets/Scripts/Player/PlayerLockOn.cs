using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLockOn : MonoBehaviour, ILockOnController
{
    [Header("?먯깋 踰붿쐞/?쒖빞 (?쒓? ?ㅻ챸)")]
    [SerializeField, Min(0f)] private float lockOnRange = 18f;                // [議곗젅媛? ?쎌삩 ?먯깋 理쒕? 嫄곕━
    [SerializeField, Range(10f, 180f)] private float lockOnFOV = 70f;         // [議곗젅媛? 移대찓??以묒떖 湲곗? ?덉슜 ?쒖빞媛?
    [SerializeField] private LayerMask obstacleMask = 0;                      // [議곗젅媛? ?쒖빞瑜?媛由щ뒗 ?μ븷臾??덉씠??留덉뒪???놁쑝硫?0)
    [SerializeField] private LayerMask enemyMask = 1 << 9;                    // [議곗젅媛? ???덉씠??留덉뒪??(?꾨줈?앺듃??留욊쾶 議곗젙)

    [Header("?源??쇰쿁 ?앹꽦/異붿쟻 (?쒓? ?ㅻ챸)")]
    [SerializeField] private string pivotName = "LockPivot";                  // [議곗젅媛? ?源?猷⑦듃 ?섏쐞???앹꽦???쎌삩 湲곗????대쫫
    [SerializeField] private float defaultPivotY = 1.3f;                       // [議곗젅媛? Render媛 ?놁쓣 ??湲곕낯 ?믪씠

    [Header("?낅젰(?덇굅?? - Tab/MiddleMouse ?좉? (?쒓? ?ㅻ챸)")]
    [SerializeField] private bool useLegacyInput = true;                      // [議곗젅媛? 援?Input ?쒖뒪??Tab/???대┃) ?ъ슜 ?щ?
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;                 // [議곗젅媛? ?쎌삩 ?좉? ??1
    [SerializeField] private KeyCode toggleKeyAlt = KeyCode.Mouse2;           // [議곗젅媛? ?쎌삩 ?좉? ??2 (???대┃)

    [Header("移대찓???곕룞 (?쒓? ?ㅻ챸)")]
    [SerializeField] private LockOnCameraManager cameraMgr;                    // [議곗젅媛? ?쎌삩 移대찓??留ㅻ땲? 李몄“

    [Header("?먮룞 ?댁젣 ?듭뀡 (?쒓? ?ㅻ챸)")]
    [Tooltip("?쎌삩 以??源?Transform???뚭눼?섍굅??鍮꾪솢?깊솕?섎㈃ ?먮룞?쇰줈 ?쎌삩???댁젣?좎? ?щ?")]
    [SerializeField] private bool autoUnlockWhenTargetDisabled = true;        // [議곗젅媛?

    // ===== ?몃??먯꽌 李몄“?섎뒗 怨듦컻 ?곹깭/?ы띁 =====
    public Transform CurrentTarget { get; private set; }                       // ?꾩옱 ?源껋쓽 '?쇰쿁' Transform
    public bool IsLocked => CurrentTarget != null;                             // 湲곗〈 ?명솚??
    public bool IsLockOn => IsLocked;                                          // 湲곗〈 ?명솚??
    public bool HasTarget => CurrentTarget != null;                            // PlayerDodgeController ?명솚??
    public Transform Target => CurrentTarget;                                  // ?源??쇰쿁 吏곸젒 ?묎렐??
    public Vector3 TargetPosition => CurrentTarget ? CurrentTarget.position : transform.position;

    public Vector3 DirectionFrom(Vector3 origin)
        => (TargetPosition - origin).sqrMagnitude > 0.0001f
            ? (TargetPosition - origin).normalized
            : transform.forward;

    Transform _playerPivot;                                                    // ?뚮젅?댁뼱 履??쎌삩 ?쇰쿁
    PlayerReferences _playerReferences;
    Transform _cam;                                                            // Camera.main 罹먯떆
    LockOnFacingDriver _facingDriver;

    private bool _lockModeActive;
    bool _timelineOwnsCamera;
    void Awake()
    {
        // ?뚮젅?댁뼱 履??쇰쿁 ?뺣낫 ??移대찓??留ㅻ땲????꾨떖
        _playerReferences = GetComponent<PlayerReferences>();
        _playerPivot = _playerReferences != null && _playerReferences.LockPivot
            ? _playerReferences.LockPivot
            : EnsurePivot(transform, pivotName, defaultPivotY);
        if (!cameraMgr) cameraMgr = FindAnyObjectByType<LockOnCameraManager>();
        cameraMgr?.SetPlayerPivot(_playerPivot);
        _facingDriver = GetComponent<LockOnFacingDriver>();
        _facingDriver?.RefreshTickState();

        // 移대찓??罹먯떆(硫붿씤 移대찓?쇰뒗 ?고??꾩뿉 諛붾????덉뼱 Start?먯꽌 ?ы솗蹂?
        _cam = Camera.main ? Camera.main.transform : null;
    }

    void Start()
    {
        if (!_cam && Camera.main) _cam = Camera.main.transform;
    }

    void Update()
    {
        // 0) ?쎌삩 以묒씤???寃잛씠 二쎌뿀嫄곕굹 鍮꾪솢?깊솕??寃쎌슦 ?먮룞?쇰줈 移대찓???댁젣
        //    - CurrentTarget == null : Destroy ??寃쎌슦
        //    - activeInHierarchy == false : SetActive(false) ??寃쎌슦
        if (_lockModeActive && autoUnlockWhenTargetDisabled && (!CurrentTarget || !CurrentTarget.gameObject.activeInHierarchy))
        {
            _lockModeActive = false;
            CurrentTarget = null;
            _facingDriver?.RefreshTickState();

            if (!_timelineOwnsCamera)
                cameraMgr?.EndLockOn();   // freeLookDriver.enabled = true, 移대찓???곗꽑?쒖쐞 蹂듦뎄
        }

        // 1) ?낅젰?쇰줈 ?쎌삩 ?좉?(Tab, ???대┃ ??
        if (useLegacyInput && (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt)))
        {
            if (IsLocked)
            {
                Unlock();
            }
            else
            {
                var enemyRoot = FindBestTarget();
                if (enemyRoot) LockTo(enemyRoot);
            }
        }
    }


    // === ?몃? ?쒖뼱??API ===
    public void LockTo(Transform enemyRoot)
    {
        // ???猷⑦듃?먯꽌 ?쇰쿁???뺣낫(?놁쑝硫??앹꽦)
        CurrentTarget = EnsurePivot(enemyRoot, pivotName, defaultPivotY);

        _lockModeActive = CurrentTarget;           // ?쇰쿁???덉쑝硫??쎌삩 紐⑤뱶 ON
        _facingDriver?.RefreshTickState();
        if (CurrentTarget && !_timelineOwnsCamera)
            cameraMgr?.StartLockOn(CurrentTarget);
    }

    public void Unlock()
    {
        _lockModeActive = false;                   // ?쎌삩 紐⑤뱶 OFF
        _timelineOwnsCamera = false;
        CurrentTarget = null;
        _facingDriver?.RefreshTickState();
        cameraMgr?.EndLockOn();
    }

    public bool TryAutoLock()
    {
        var best = FindBestTarget();
        if (!best) return false;
        LockTo(best);
        return true;
    }

    public Transform GetCurrentTarget() => CurrentTarget;

    public bool IsLockedOn() => IsLocked;

    public void RestoreFreeLookAfterTimeline()
    {
        _timelineOwnsCamera = false;
        _lockModeActive = false;
        CurrentTarget = null;
        _facingDriver?.RefreshTickState();

        if (cameraMgr == null)
            return;

        cameraMgr.EndLockOn(false);
        cameraMgr.ForceRestoreGameplayFreeLook();
    }

    public void GiveCameraControlToTimeline(bool give)
    {
        _timelineOwnsCamera = give;
        _facingDriver?.RefreshTickState();

        if (cameraMgr == null)
            return;

        if (give)
        {
            cameraMgr.EndLockOn(false);
            cameraMgr.SetFreeLookDriverEnabled(false);
            return;
        }

        if (_lockModeActive && CurrentTarget && CurrentTarget.gameObject.activeInHierarchy)
        {
            cameraMgr.StartLockOn(CurrentTarget);
            return;
        }

        cameraMgr.EndLockOn(false);
        cameraMgr.ForceRestoreGameplayFreeLook();
    }

    // === ?대? 援ы쁽 ===

    // 移대찓???꾨갑 ?먮퓭(FOV) + ?μ븷臾??쇱씤罹먯뒪?몃줈 理쒖쟻 ?源??먯깋
    Transform FindBestTarget()
    {
        if (!_cam)
        {
            if (!Camera.main) return null;
            _cam = Camera.main.transform;
        }

        var cols = Physics.OverlapSphere(_cam.position, lockOnRange, enemyMask, QueryTriggerInteraction.Ignore);
        float bestScore = float.MaxValue;
        Transform bestRoot = null;

        for (int i = 0; i < cols.Length; ++i)
        {
            var root = cols[i].transform.root;
            var pivot = EnsurePivot(root, pivotName, defaultPivotY);
            if (!pivot) continue;

            Vector3 to = pivot.position - _cam.position;
            float dist = to.magnitude;
            if (dist > lockOnRange) continue;

            float ang = Vector3.Angle(_cam.forward, to / (dist > 0.0001f ? dist : 1f));
            if (ang > lockOnFOV * 0.5f) continue;

            if (obstacleMask.value != 0 &&
                Physics.Linecast(_cam.position, pivot.position, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            // 以묒븰+洹쇨굅由??곗꽑 媛以묒튂
            float score = ang * 2f + dist;
            if (score < bestScore)
            {
                bestScore = score;
                bestRoot = root;
            }
        }

        return bestRoot;
    }

    // ?源?猷⑦듃???쎌삩 ?쇰쿁???놁쑝硫??앹꽦?댁꽌 諛섑솚
    Transform EnsurePivot(Transform root, string name, float defaultY)
    {
        if (!root) return null;

        var t = root.Find(name);
        if (t) return t;

        // ?뚮뜑?ш? ?덉쑝硫?癒몃━ 洹쇱쿂濡? ?놁쑝硫?湲곕낯 ?믪씠濡?
        var rend = root.GetComponentInChildren<Renderer>();
        Vector3 worldPos;
        if (rend)
        {
            float cy = rend.bounds.center.y;
            float ty = rend.bounds.max.y;
            float y = Mathf.Lerp(cy, ty, 0.6f) - 0.1f; // ?댁쭩 ?꾨옒濡?
            worldPos = new Vector3(rend.bounds.center.x, y, rend.bounds.center.z);
        }
        else
        {
            worldPos = root.position + Vector3.up * defaultY;
        }

        var go = new GameObject(name);
        go.transform.SetParent(root, true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.identity;

        // ?源?紐⑤뜽???ㅼ???蹂??대룞???곕씪 Y瑜??곕씪媛?꾨줉 蹂댁“ 而댄룷?뚰듃
        var follower = go.AddComponent<LockPivotFollower>();
        follower.sourceRenderer = rend;
        follower.yOffset = rend ? go.transform.position.y - rend.bounds.center.y : defaultY;

        return go.transform;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // ?먯깋 援??쒖빞媛?媛?쒗솕(?먮뵒??
        var cam = Camera.main ? Camera.main.transform : null;
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        if (cam) Gizmos.DrawWireSphere(cam.position, lockOnRange);

        if (cam)
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
            Vector3 forward = cam.forward;
            Quaternion left = Quaternion.AngleAxis(-lockOnFOV * 0.5f, Vector3.up);
            Quaternion right = Quaternion.AngleAxis(lockOnFOV * 0.5f, Vector3.up);
            Gizmos.DrawLine(cam.position, cam.position + (left * forward).normalized * lockOnRange);
            Gizmos.DrawLine(cam.position, cam.position + (right * forward).normalized * lockOnRange);
        }
    }
#endif
}

