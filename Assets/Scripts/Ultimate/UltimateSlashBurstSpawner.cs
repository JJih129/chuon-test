using UnityEngine;
using System;
using System.Collections;

public class UltimateSlashBurstSpawner : MonoBehaviour
{
    [Header("Anchors")]
    [SerializeField] PlayerReferences playerReferences;
    [SerializeField] Transform player;     // 플레이어 루트
    [SerializeField] Transform spawnRoot;  // Player/UltimateSpawnRoot

    [Header("Prefab & Look")]
    [SerializeField] GameObject slashPrefab;
    [SerializeField] float forwardOffset = 3.0f;
    [SerializeField] float heightOffset = 1.4f;
    [SerializeField] float length = 6.0f;
    [SerializeField] float thickness = 0.08f;
    [SerializeField] float coneAngle = 25f;

    [Header("Material props (optional)")]
    [SerializeField] string propIntensity = "_Intensity";
    [SerializeField] string propScroll = "_ScrollSpeed";
    [SerializeField] string propAlpha = "_Alpha";
    [SerializeField] float baseIntensity = 1.2f;
    [SerializeField] float baseScroll = 2.0f;
    [SerializeField] Transform presentationPlayerOverride;
    [SerializeField] Transform presentationSpawnRootOverride;

    const float Golden = 137.507764f; // 분포용
    bool _warnedMissingSlashPrefab;

    public void PushPresentationOverride(Transform playerOverride, Transform spawnRootOverride)
    {
        presentationPlayerOverride = playerOverride;
        presentationSpawnRootOverride = spawnRootOverride;
    }

    public void ClearPresentationOverride()
    {
        presentationPlayerOverride = null;
        presentationSpawnRootOverride = null;
    }

    void EnsureSpawnRoot()
    {
        if (!playerReferences) playerReferences = GetComponent<PlayerReferences>();
        if (!player && playerReferences != null) player = playerReferences.PlayerRoot;
        if (!spawnRoot && playerReferences != null) spawnRoot = playerReferences.UltimateSpawnRoot;
        if (!player) player = transform;
        if (spawnRoot) return;
        var go = new GameObject("UltimateSpawnRoot");
        spawnRoot = go.transform;
        spawnRoot.SetParent(player, false);
        spawnRoot.localPosition = new Vector3(0f, heightOffset, forwardOffset);
        spawnRoot.localRotation = Quaternion.identity;
    }

    // ⬇️ “한 줄”만 생성 (index에 따라 항상 같은 방향/모양)
    public void EmitOneSlash(int index, int totalCount, int patternSeed, float life = 0.7f)
    {
        if (!playerReferences) playerReferences = GetComponent<PlayerReferences>();
        if (!player) player = playerReferences != null ? playerReferences.PlayerRoot : transform;
        EnsureSpawnRoot();

        Transform activePlayer = presentationPlayerOverride != null ? presentationPlayerOverride : player;
        Transform activeSpawnRoot = presentationSpawnRootOverride != null ? presentationSpawnRootOverride : spawnRoot;
        if (activePlayer == null)
            activePlayer = transform;
        if (activeSpawnRoot == null)
            activeSpawnRoot = activePlayer;

        if (!slashPrefab)
        {
            if (!_warnedMissingSlashPrefab)
            {
                Debug.LogWarning("[Ultimate] UltimateSlashBurstSpawner.slashPrefab is not assigned. Slash burst VFX will be skipped.", this);
                _warnedMissingSlashPrefab = true;
            }
            return;
        }

        // 고정 시드 + 인덱스로 안정적인 난수 시퀀스
        var rng = new System.Random(patternSeed + index * 9973);

        // 플레이어 정면 기준 회전
        Quaternion faceFwd = Quaternion.LookRotation(activePlayer.forward, Vector3.up);

        // 분포: 골든앵글 기반 + 약간의 지터
        float yaw = index * Golden + (float)(rng.NextDouble() * 10f - 5f);
        float pitch = (float)((rng.NextDouble() * 2 - 1) * coneAngle);

        Quaternion rot = faceFwd
                       * Quaternion.AngleAxis(yaw, Vector3.up)
                       * Quaternion.AngleAxis(pitch, Vector3.right);

        Vector3 pos = activeSpawnRoot.position;

        var go = TransientVfxPool.Spawn(slashPrefab, pos, rot, null, life);
        if (go == null)
            return;
        go.transform.localScale = new Vector3(thickness, thickness, length);
        ConfigureSpawnedFxForUnscaledPlayback(go);

        var r = go.GetComponentInChildren<Renderer>();
        if (r)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (!string.IsNullOrEmpty(propIntensity)) mpb.SetFloat(propIntensity, baseIntensity);
            if (!string.IsNullOrEmpty(propScroll)) mpb.SetFloat(propScroll, baseScroll);
            if (!string.IsNullOrEmpty(propAlpha)) mpb.SetFloat(propAlpha, 1f);
            r.SetPropertyBlock(mpb);
        }
    }

    void ConfigureSpawnedFxForUnscaledPlayback(GameObject go)
    {
        if (go == null)
            return;

        var animators = go.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].updateMode = AnimatorUpdateMode.UnscaledTime;

        var particleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            var main = particleSystems[i].main;
            main.useUnscaledTime = true;
        }
    }
}
