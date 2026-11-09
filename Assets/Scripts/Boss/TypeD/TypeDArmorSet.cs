using System;
using UnityEngine;

// Type-D 장갑 세트 데이터 (반복 10). 에디터 빌더(TypeDArmorBuilder)가 Blender 생성물에서 만든다.
// 각 파츠는 휴머노이드 뼈에 붙는 강체 메시이고, 로컬 포즈는 에디터 T-포즈에서 뼈 기준으로 구웠다.
[CreateAssetMenu(menuName = "ChuOn/Type-D Armor Set", fileName = "TypeD_ArmorSet")]
public sealed class TypeDArmorSet : ScriptableObject
{
    public enum PartRole
    {
        Plate = 0,
        Visor = 1,
        Core = 2
    }

    [Serializable]
    public sealed class Part
    {
        public string name;
        public Mesh mesh;
        public Material[] materials;
        public HumanBodyBones bone;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public PartRole role;
        [Tooltip("발광(인레이) 서브메시 인덱스, 없으면 -1")]
        public int glowSubmesh = -1;
        [Tooltip("바이저 앵커 (메시 로컬)")]
        public Vector3 anchorLocal;
        [Tooltip("어깨 장갑: 쇄골 뼈 포즈와 섞어서 팔 회전의 일부만 따라가게 (팔을 내려도 위를 덮고, 들어 올려도 관통하지 않게)")]
        public bool useBlendBone;
        public HumanBodyBones blendBone;
        public Vector3 blendLocalPosition;
        public Quaternion blendLocalRotation = Quaternion.identity;
        [Range(0f, 1f)] public float blendWeight = 0.5f;
        [Tooltip("블렌드 회전 중심 (메시 로컬, 어깨 관절)")]
        public Vector3 pivotLocal;
    }

    [Serializable]
    public sealed class MaterialOverride
    {
        public string rendererName;
        public Material[] materials;
    }

    public Part[] parts = Array.Empty<Part>();
    [Tooltip("기존 몸 렌더러 재질 교체 (무광 고무 슈트 + 회로 발광 마스크 + 림라이트)")]
    public MaterialOverride[] materialOverrides = Array.Empty<MaterialOverride>();

    [Header("Model check (auto install)")]
    [Tooltip("이 이름의 SkinnedMeshRenderer 정점 수가 같을 때만 자동 설치 (다른 보스 모델에 잘못 붙지 않게)")]
    public string requiredRendererName = "Plane009";
    public int requiredVertexCount;

    public bool MatchesModel(Transform root)
    {
        if (root == null)
            return false;
        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer r = renderers[i];
            if (r != null && r.name == requiredRendererName && r.sharedMesh != null)
                return requiredVertexCount <= 0 || r.sharedMesh.vertexCount == requiredVertexCount;
        }
        return false;
    }

    [Header("Energy palette")]
    [ColorUsage(false, true)] public Color coreColor = new Color(0.55f, 0.85f, 1.4f);
    [ColorUsage(false, true)] public Color glowColor = new Color(0.45f, 0.8f, 1.3f);
    [ColorUsage(false, true)] public Color visorIdleColor = new Color(0.35f, 0.7f, 1.1f);
    [ColorUsage(false, true)] public Color suitCircuitColor = new Color(0.3f, 0.65f, 1.0f);
}
