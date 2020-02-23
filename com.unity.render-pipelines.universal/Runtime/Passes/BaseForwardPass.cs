using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    public abstract class BaseForwardPass : ScriptableRenderPass
    {
        private const int FIRST_PERSON_MASK_STENCIL_REFERENCE = 255;

        protected List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        private FilteringSettings m_FilteringSettings;
        private bool m_IsOpaque;

        private RenderStateBlock m_DefaultRenderStateBlock;
        private RenderStateBlock m_FirstPersonRenderStateBlock;
        private RenderStateBlock m_ThirdPersonRenderStateBlock;

        private readonly string m_DefaultPassProfilerTag;
        private readonly string m_FirstPersonPassProfilerTag;
        private readonly string m_ThirdPersonPassProfilerTag;

        protected BaseForwardPass(string defaultPassTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
        {
            // Store additional readonly pass tags. 
            m_DefaultPassProfilerTag = defaultPassTag;
            m_FirstPersonPassProfilerTag = string.Format("{0} (First Person)", defaultPassTag);
            m_ThirdPersonPassProfilerTag = string.Format("{0} (Third Person)", defaultPassTag);

            // Assign filtering settings.
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            renderPassEvent = evt;
            m_IsOpaque = opaque;

            // Setup additional render state blocks for each possible render pass.
            m_DefaultRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);


            if (opaque)
            {
                m_FirstPersonRenderStateBlock = new RenderStateBlock(RenderStateMask.Stencil);
                m_FirstPersonRenderStateBlock.stencilReference = FIRST_PERSON_MASK_STENCIL_REFERENCE;
                m_FirstPersonRenderStateBlock.stencilState = new StencilState(
                    enabled: true,
                    readMask: 255,
                    writeMask: 255,
                    compareFunction: CompareFunction.Always,
                    passOperation: StencilOp.Replace,
                    failOperation: StencilOp.Keep
                );
            }
            else
            {
                m_FirstPersonRenderStateBlock = new RenderStateBlock(RenderStateMask.Depth | RenderStateMask.Stencil);
                m_FirstPersonRenderStateBlock.stencilReference = FIRST_PERSON_MASK_STENCIL_REFERENCE;
                m_FirstPersonRenderStateBlock.stencilState = new StencilState(
                    enabled: true,
                    readMask: 255,
                    writeMask: 255,
                    compareFunction: CompareFunction.NotEqual,
                    passOperation: StencilOp.Keep,
                    failOperation: StencilOp.Keep
                );

                m_FirstPersonRenderStateBlock.depthState = new DepthState(
                    writeEnabled: false,
                    compareFunction: CompareFunction.Always);
            }

            m_ThirdPersonRenderStateBlock = new RenderStateBlock(RenderStateMask.Stencil);
            m_ThirdPersonRenderStateBlock.stencilReference = FIRST_PERSON_MASK_STENCIL_REFERENCE;
            m_ThirdPersonRenderStateBlock.stencilState = new StencilState(
                enabled: true,
                readMask: 255,
                writeMask: 255,
                compareFunction: CompareFunction.NotEqual,
                passOperation: StencilOp.Keep,
                failOperation: StencilOp.Keep
            );
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Pre-compute references that are used in both first person and default render.
            var camera = renderingData.cameraData.camera;
            var sortFlags = (m_IsOpaque) ? renderingData.cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent;
            var drawSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortFlags);

            var renderFirstPerson = !renderingData.cameraData.isSceneViewCamera && renderingData.cameraData.supportsFirstPersonViewModelRendering;
            if (renderFirstPerson)
            {
                ExecuteRenderFirstPersonOnly(context, camera, ref renderingData, ref drawSettings);
                ExecuteRenderThirdPersonOnly(context, camera, ref renderingData, ref drawSettings);
            }
            else
            {
                ExecuteRenderDefault(context, camera, ref renderingData, ref drawSettings);
            }
        }

        private void ExecuteRenderDefault(ScriptableRenderContext context, Camera camera, ref RenderingData renderingData, ref DrawingSettings drawSettings)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_DefaultPassProfilerTag);
            using (new ProfilingSample(cmd, m_DefaultPassProfilerTag))
            {
                // First clear buffer.
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter renderers.
                m_FilteringSettings.renderingLayerMask = uint.MaxValue;

                // Then render world geometry.
                RenderFiltered(context, cmd, camera, ref renderingData, ref drawSettings, ref m_FilteringSettings, ref m_DefaultRenderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderFirstPersonOnly(ScriptableRenderContext context, Camera camera, ref RenderingData renderingData, ref DrawingSettings drawSettings)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_FirstPersonPassProfilerTag);
            using (new ProfilingSample(cmd, m_FirstPersonPassProfilerTag))
            {
                // First clear buffer.
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then set pipeline state.
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.FirstPersonDepth, true);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, renderingData.cameraData.firstPersonViewModelProjectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Render first person objects.
                m_FilteringSettings.renderingLayerMask = renderingData.cameraData.firstPersonViewModelRenderingLayerMask;
                RenderFiltered(context, cmd, camera, ref renderingData, ref drawSettings, ref m_FilteringSettings, ref m_FirstPersonRenderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderThirdPersonOnly(ScriptableRenderContext context, Camera camera, ref RenderingData renderingData, ref DrawingSettings drawSettings)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_ThirdPersonPassProfilerTag);
            using (new ProfilingSample(cmd, m_ThirdPersonPassProfilerTag))
            {
                // First set pipeline state.
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.FirstPersonDepth, false);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Draw third person renderers.
                m_FilteringSettings.renderingLayerMask = renderingData.cameraData.thirdPersonRenderingLayerMask;
                RenderFiltered(context, cmd, camera, ref renderingData, ref drawSettings, ref m_FilteringSettings, ref m_ThirdPersonRenderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        protected abstract void RenderFiltered(ScriptableRenderContext context, CommandBuffer cmd, Camera camera, ref RenderingData renderingData, ref DrawingSettings drawSettings, ref FilteringSettings filteringSettings, ref RenderStateBlock renderStateBlock);
    }
}
