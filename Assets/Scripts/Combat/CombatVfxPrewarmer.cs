using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스전 VFX 워밍업 (Level_Facility 전용, 씬에 배치된 경우에만 동작).
/// 상층 → 하층 핸드오프(LobbyManager.LowerFloorReached: 엘리베이터 하강 암전 / 체크포인트 스킵) 시점에
/// 보스 검기·피격·소드웨이브·궁극기·플레이어 피격 VFX 를 TransientVfxPool 에 미리 생성하고,
/// 플레이어 시야 밖에서 전용 카메라로 1회 렌더해 첫 사용 시 Instantiate / 셰이더·텍스처 업로드 히치를 없앤다.
/// (드론 프리웜 LobbyManager.CoPrewarmLobbyDrones 와 같은 방식)
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatVfxPrewarmer : MonoBehaviour
{
    const string DefaultUltimateSequencePath = "Ultimate/UltimateSequence_Default";

    [SerializeField] bool prewarmOnLowerFloorReached = true;
    [Tooltip("프리팹당 풀에 미리 만들어 둘 인스턴스 수")]
    [SerializeField, Min(1)] int instancesPerPrefab = 2;
    [Tooltip("한 프레임에 처리할 프리팹 수 (히치 분산)")]
    [SerializeField, Min(1)] int prefabsPerFrame = 4;
    [SerializeField, Min(1)] int warmupRenderFrames = 2;
    [SerializeField] bool includeUltimateVfx = true;
    [SerializeField] GameObject[] extraPrefabs = System.Array.Empty<GameObject>();

    bool _done;

    public bool HasPrewarmed => _done;

    void OnEnable()
    {
        LobbyManager.LowerFloorReached += HandleLowerFloorReached;
    }

    void OnDisable()
    {
        LobbyManager.LowerFloorReached -= HandleLowerFloorReached;
    }

    void HandleLowerFloorReached(LobbyManager lobby)
    {
        if (prewarmOnLowerFloorReached)
            Prewarm();
    }

    public void Prewarm()
    {
        if (_done || !isActiveAndEnabled)
            return;

        _done = true;
        StartCoroutine(CoPrewarm());
    }

    List<GameObject> CollectPrefabs()
    {
        var list = new List<GameObject>(32);
        foreach (BossAttackVfxPresenter presenter in FindObjectsOfType<BossAttackVfxPresenter>(true))
            presenter.CollectPrewarmPrefabs(list);
        foreach (BossAttackFeedbackPresenter presenter in FindObjectsOfType<BossAttackFeedbackPresenter>(true))
            presenter.CollectPrewarmPrefabs(list);
        foreach (BossHitReaction reaction in FindObjectsOfType<BossHitReaction>(true))
        {
            list.Add(reaction.normalHitVfx);
            list.Add(reaction.heavyHitVfx);
        }
        foreach (PlayerHealth health in FindObjectsOfType<PlayerHealth>(true))
            list.Add(health.HitEffectPrefab);
        foreach (UltimateTargetSimple target in FindObjectsOfType<UltimateTargetSimple>(true))
            list.Add(target.hitVFXPrefab);

        if (includeUltimateVfx)
        {
            UltimateSequenceData data = Resources.Load<UltimateSequenceData>(DefaultUltimateSequencePath);
            if (data != null && data.Vfx != null)
            {
                UltimateSequenceData.VfxSettings vfx = data.Vfx;
                list.Add(vfx.introPoseVfxPrefab);
                list.Add(vfx.dashSlashVfxPrefab);
                list.Add(vfx.lightBeamVfxPrefab);
                list.Add(vfx.crackWorldVfxPrefab);
                list.Add(vfx.explosionVfxPrefab);
                list.Add(vfx.shardVfxPrefab);
                list.Add(vfx.walkoutVfxPrefab);
                list.Add(vfx.slashStormAoeVfxPrefab);
            }
        }

        if (extraPrefabs != null)
            list.AddRange(extraPrefabs);

        var unique = new HashSet<GameObject>();
        list.RemoveAll(prefab => prefab == null || !unique.Add(prefab));
        return list;
    }

    IEnumerator CoPrewarm()
    {
        yield return null; // 핸드오프 프레임은 건드리지 않는다

        List<GameObject> prefabs = CollectPrefabs();
        BossController boss = FindObjectOfType<BossController>();
        Vector3 origin = boss != null ? boss.transform.position : transform.position;
        Vector3 basePosition = origin + Vector3.up * 150f;

        var cameraObject = new GameObject("__CombatVfxWarmupCamera");
        cameraObject.transform.SetPositionAndRotation(basePosition + new Vector3(0f, 0.5f, -6f), Quaternion.identity);
        Camera warmupCamera = cameraObject.AddComponent<Camera>();
        RenderTexture warmupTarget = RenderTexture.GetTemporary(256, 256, 16);
        warmupCamera.targetTexture = warmupTarget;
        warmupCamera.depth = -100f;
        warmupCamera.nearClipPlane = 0.1f;
        warmupCamera.farClipPlane = 40f;
        warmupCamera.fieldOfView = 70f;
        // 아레나와 같은 셰이더 변형(_ADDITIONAL_LIGHTS)을 컴파일하도록 임시 포인트 라이트
        var lightObject = new GameObject("__CombatVfxWarmupLight");
        lightObject.transform.position = basePosition + new Vector3(0f, 2f, -2f);
        Light warmupLight = lightObject.AddComponent<Light>();
        warmupLight.type = LightType.Point;
        warmupLight.range = 12f;
        warmupLight.shadows = LightShadows.None;

        int processed = 0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            TransientVfxPool.Prewarm(prefab, instancesPerPrefab);
            // 카메라 앞 격자에 1개씩 재생 → 렌더 1회 (셰이더/텍스처 업로드). 수명이 끝나면 풀로 자동 회수.
            Vector3 position = basePosition + new Vector3(((i % 5) - 2) * 1.4f, (i / 5 % 3 - 1) * 1.2f, 1.5f);
            GameObject spawned = TransientVfxPool.Spawn(prefab, position, Quaternion.identity, null, 0.5f);
            MuteAudio(spawned);

            if (++processed % prefabsPerFrame == 0)
                yield return null;
        }

        // 소드웨이브 투사체 (정적 풀 + Resources 비주얼): 무해한 설정으로 1발 생성 → 수명 후 풀로 복귀
        BossSwordWaveProjectile wave = BossSwordWaveProjectile.Spawn(basePosition + new Vector3(0f, -1.5f, 2f), Quaternion.identity, boss != null ? boss.gameObject.layer : gameObject.layer);
        if (wave != null)
            wave.Configure(null, 0f, 0.1f, 0.3f, 2f, 1f, 1f, false, false, false, false, false);

        for (int frame = 0; frame < warmupRenderFrames; frame++)
            yield return null;

        warmupCamera.targetTexture = null;
        RenderTexture.ReleaseTemporary(warmupTarget);
        Destroy(cameraObject);
        Destroy(lightObject);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[CombatVfxPrewarmer] Prewarmed {prefabs.Count} VFX prefabs (+ sword wave).", this);
#endif
    }

    static void MuteAudio(GameObject spawned)
    {
        if (spawned == null)
            return;

        AudioSource[] sources = spawned.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
            sources[i].Stop();
    }
}
