namespace UnityEngine.Rendering.Universal
{
    public class DepthNormalsPass : BaseForwardPass
    {
        int kDepthBufferBits = 32;

        private RenderTargetHandle depthAttachmentHandle { get; set; }
        internal RenderTextureDescriptor descriptor { get; private set; }

        private Material depthNormalsMaterial = null;

        public DepthNormalsPass(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, Material material)
                        : base("DepthNormals Prepass", true, evt, renderQueueRange, layerMask)
        {
            m_ShaderTagIdList.Add(new ShaderTagId("DepthOnly"));

            depthNormalsMaterial = material;
        }

        public void Setup(RenderTextureDescriptor baseDescriptor, RenderTargetHandle depthAttachmentHandle)
        {
            this.depthAttachmentHandle = depthAttachmentHandle;
            baseDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            baseDescriptor.depthBufferBits = kDepthBufferBits;
            descriptor = baseDescriptor;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            cmd.GetTemporaryRT(depthAttachmentHandle.id, descriptor, FilterMode.Point);
            ConfigureTarget(depthAttachmentHandle.Identifier());
            ConfigureClear(ClearFlag.All, Color.black);
        }

        protected override void RenderFiltered(ScriptableRenderContext context, CommandBuffer cmd, Camera camera, ref RenderingData renderingData, ref DrawingSettings drawSettings, ref FilteringSettings filteringSettings, ref RenderStateBlock renderStateBlock)
        {
            ref CameraData cameraData = ref renderingData.cameraData;
            if (cameraData.isStereoEnabled)
            {
                context.StartMultiEye(camera);
            }

            drawSettings.perObjectData = PerObjectData.None;
            drawSettings.overrideMaterial = depthNormalsMaterial;

            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings, ref renderStateBlock);

            cmd.SetGlobalTexture("_CameraDepthNormalsTexture", depthAttachmentHandle.id);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (depthAttachmentHandle != RenderTargetHandle.CameraTarget)
            {
                cmd.ReleaseTemporaryRT(depthAttachmentHandle.id);
                depthAttachmentHandle = RenderTargetHandle.CameraTarget;
            }
        }
    }
}
