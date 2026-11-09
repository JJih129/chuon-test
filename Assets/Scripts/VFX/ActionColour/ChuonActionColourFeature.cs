// 추온 액션 컬러 렌더러 피처 (반복 11)
// URP_Main_Renderer 에 등록된다. 효과가 꺼져 있으면 패스를 하나도 큐잉하지 않으므로 평상시 비용은 0.
//  - KeepMaskPass   : 렌더링 레이어 'KeepColour'(비트 ActionColourState.KeepColourLayerBit) 불투명 렌더러 → R8 마스크
//  - ColourPass     : 흑백 흡수 스윕 + 색수차/비네트/채도 딥/틴트 (포스트 처리 직전, HDR)
//  - ShatterPass    : 포스트 처리 후 프레임 캡처 → 보로노이 파편 메시로 덮고 폭발 (화면 갈라짐)
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class ChuonActionColourFeature : ScriptableRendererFeature
{
    [SerializeField] Shader colourShader;
    [SerializeField] Shader maskShader;
    [SerializeField] Shader shatterShader;
    [SerializeField] Mesh shatterMesh;

    Material _colourMaterial;
    Material _maskMaterial;
    Material _shatterMaterial;
    KeepMaskPass _maskPass;
    ColourPass _colourPass;
    ShatterPass _shatterPass;

    public override void Create()
    {
        if (colourShader == null) colourShader = Shader.Find("Hidden/ChuOn/ActionColour");
        if (maskShader == null) maskShader = Shader.Find("Hidden/ChuOn/KeepColourMask");
        if (shatterShader == null) shatterShader = Shader.Find("Hidden/ChuOn/ScreenShatter");

        _colourMaterial = colourShader != null ? CoreUtils.CreateEngineMaterial(colourShader) : null;
        _maskMaterial = maskShader != null ? CoreUtils.CreateEngineMaterial(maskShader) : null;
        _shatterMaterial = shatterShader != null ? CoreUtils.CreateEngineMaterial(shatterShader) : null;

        _maskPass = new KeepMaskPass(_maskMaterial) { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        _colourPass = new ColourPass(_colourMaterial) { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        _shatterPass = new ShatterPass(_shatterMaterial) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    static bool IsEligibleCamera(ref RenderingData renderingData)
    {
        ref CameraData cameraData = ref renderingData.cameraData;
        if (cameraData.cameraType != CameraType.Game)
            return false;
        if (cameraData.renderType != CameraRenderType.Base)
            return false;
        Camera cam = cameraData.camera;
        // 메인(게임플레이) 카메라만 — 미리보기/렌더텍스처 카메라 제외
        return cam != null && cam.targetTexture == null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!IsEligibleCamera(ref renderingData))
            return;

        bool colourActive = ActionColourState.ColourActive && _colourMaterial != null;
        bool maskActive = colourActive && ActionColourState.UseKeepMask && _maskMaterial != null;
        bool shatterActive = ActionColourState.ShatterActive && _shatterMaterial != null && ResolveShatterMesh() != null;

        if (maskActive)
        {
            _maskPass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_maskPass);
        }

        if (colourActive)
        {
            _colourPass.UseMask = maskActive;
            renderer.EnqueuePass(_colourPass);
        }

        if (shatterActive)
        {
            _shatterPass.Mesh = ResolveShatterMesh();
            renderer.EnqueuePass(_shatterPass);
        }
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;
        _colourPass.SetTarget(renderer.cameraColorTargetHandle);
        _shatterPass.SetTarget(renderer.cameraColorTargetHandle);
    }

    Mesh ResolveShatterMesh()
    {
        if (shatterMesh == null)
            shatterMesh = ActionColourState.ShatterMesh;
        return shatterMesh;
    }

    protected override void Dispose(bool disposing)
    {
        _maskPass?.Dispose();
        _colourPass?.Dispose();
        _shatterPass?.Dispose();
        CoreUtils.Destroy(_colourMaterial);
        CoreUtils.Destroy(_maskMaterial);
        CoreUtils.Destroy(_shatterMaterial);
    }

    // ------------------------------------------------------------------
    sealed class KeepMaskPass : ScriptableRenderPass
    {
        static readonly ShaderTagId[] s_tags =
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("Universal2D")
        };
        static readonly System.Collections.Generic.List<ShaderTagId> s_tagList = new System.Collections.Generic.List<ShaderTagId>(s_tags);

        readonly Material _material;
        readonly ProfilingSampler _sampler = new ProfilingSampler("ChuOn KeepColour Mask");
        RTHandle _mask;
        FilteringSettings _filtering;

        public KeepMaskPass(Material material)
        {
            _material = material;
            _filtering = new FilteringSettings(RenderQueueRange.opaque, ~0, 1u << ActionColourState.KeepColourLayerBit);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref _mask, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ChuonKeepMaskTex");
            ConfigureTarget(_mask);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _mask == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _sampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                DrawingSettings drawing = CreateDrawingSettings(s_tagList, ref renderingData, SortingCriteria.CommonOpaque);
                drawing.overrideMaterial = _material;
                drawing.overrideMaterialPassIndex = 0;
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref _filtering);
                cmd.SetGlobalTexture("_ChuonKeepMaskTex", _mask);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _mask?.Release();
            _mask = null;
        }
    }

    // ------------------------------------------------------------------
    sealed class ColourPass : ScriptableRenderPass
    {
        static readonly int SweepId = Shader.PropertyToID("_AC_Sweep");
        static readonly int CenterId = Shader.PropertyToID("_AC_Center");
        static readonly int PostId = Shader.PropertyToID("_AC_Post");
        static readonly int KeepId = Shader.PropertyToID("_AC_Keep");
        static readonly int VignetteColorId = Shader.PropertyToID("_AC_VignetteColor");
        static readonly int TintId = Shader.PropertyToID("_AC_Tint");
        static readonly int RingColorId = Shader.PropertyToID("_AC_RingColor");

        readonly Material _material;
        readonly ProfilingSampler _sampler = new ProfilingSampler("ChuOn ActionColour");
        RTHandle _target;
        RTHandle _temp;
        public bool UseMask;

        public ColourPass(Material material)
        {
            _material = material;
        }

        public void SetTarget(RTHandle target) => _target = target;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ChuonActionColourTemp");
            ConfigureTarget(_target);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _target == null || _temp == null)
                return;

            Camera cam = renderingData.cameraData.camera;
            float aspect = cam != null && cam.pixelHeight > 0 ? (float)cam.pixelWidth / cam.pixelHeight : 16f / 9f;
            ActionColourState.Snapshot s = ActionColourState.Current;

            _material.SetVector(SweepId, new Vector4(s.Desaturation, s.SweepRadius, s.SweepSoftness, s.RingIntensity));
            _material.SetVector(CenterId, new Vector4(s.Center.x, s.Center.y, aspect, UseMask ? 1f : 0f));
            _material.SetVector(PostId, new Vector4(s.ChromaticAberration, s.VignetteAmount, s.TintAmount, s.SaturationDip));
            _material.SetVector(KeepId, new Vector4(s.KeepLumaStart, s.KeepLumaEnd, s.BwContrast, s.BwBrightness));
            _material.SetColor(VignetteColorId, s.VignetteColor);
            _material.SetColor(TintId, s.Tint);
            _material.SetColor(RingColorId, s.RingColor);

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _sampler))
            {
                Blitter.BlitCameraTexture(cmd, _target, _temp, _material, 0);
                Blitter.BlitCameraTexture(cmd, _temp, _target);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _temp?.Release();
            _temp = null;
        }
    }

    // ------------------------------------------------------------------
    sealed class ShatterPass : ScriptableRenderPass
    {
        static readonly int TexId = Shader.PropertyToID("_ShatterTex");
        static readonly int ImpactId = Shader.PropertyToID("_ShatterImpact");
        static readonly int StateId = Shader.PropertyToID("_ShatterState");
        static readonly int CrackColorId = Shader.PropertyToID("_ShatterCrackColor");

        readonly Material _material;
        readonly ProfilingSampler _sampler = new ProfilingSampler("ChuOn ScreenShatter");
        RTHandle _target;
        RTHandle _capture;
        public Mesh Mesh;

        public ShatterPass(Material material)
        {
            _material = material;
        }

        public void SetTarget(RTHandle target) => _target = target;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            if (RenderingUtils.ReAllocateIfNeeded(ref _capture, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ChuonShatterCapture"))
                ActionColourState.NotifyShatterCaptureLost();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 포스트 처리 뒤에는 URP 가 활성 컬러 버퍼를 afterPostProcess 버퍼로 바꾸므로 실행 시점 핸들을 쓴다
            RTHandle active = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (active != null)
                _target = active;
            if (_material == null || _target == null || _capture == null || Mesh == null)
                return;

            Camera cam = renderingData.cameraData.camera;
            float aspect = cam != null && cam.pixelHeight > 0 ? (float)cam.pixelWidth / cam.pixelHeight : 16f / 9f;
            ActionColourState.ShatterSnapshot s = ActionColourState.Shatter;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _sampler))
            {
                if (ActionColourState.ConsumeShatterCaptureRequest())
                    Blitter.BlitCameraTexture(cmd, _target, _capture);

                // 렌더 텍스처에 그릴 때 D3D 계열은 클립 y 가 뒤집힌다 (URP 중간 버퍼는 항상 RT)
                float flip = SystemInfo.graphicsUVStartsAtTop ? -1f : 1f;
                _material.SetTexture(TexId, _capture);
                _material.SetVector(ImpactId, new Vector4(s.Impact.x, s.Impact.y, aspect, flip));
                _material.SetVector(StateId, new Vector4(s.Crack, s.Burst, s.Alpha, s.ChromaticAberration));
                _material.SetColor(CrackColorId, s.CrackColor);
                CoreUtils.SetRenderTarget(cmd, _target);
                cmd.DrawMesh(Mesh, Matrix4x4.identity, _material, 0, 0);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _capture?.Release();
            _capture = null;
        }
    }
}
