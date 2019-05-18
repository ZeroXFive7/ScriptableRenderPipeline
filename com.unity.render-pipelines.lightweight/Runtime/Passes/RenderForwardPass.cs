using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    public abstract class RenderForwardPass : ScriptableRenderPass
    {
        private const int FIRST_PERSON_MASK_STENCIL_REFERENCE = 255;

        protected FilterRenderersSettings filterSettings;
        protected SortFlags sortFlags;

        protected RendererConfiguration rendererConfiguration;

        protected RenderStateBlock defaultRenderStateBlock;
        protected RenderStateBlock firstPersonRenderStateBlock;
        protected RenderStateBlock thirdPersonRenderStateBlock;

        private readonly string renderDefaultPassTag;
        private readonly string renderFirstPersonPassTag;
        private readonly string renderThirdPersonPassTag;

        protected RenderForwardPass(string defaultPassTag)
        {
            renderDefaultPassTag = defaultPassTag;
            renderFirstPersonPassTag = string.Format("{0} (First Person)", defaultPassTag);
            renderThirdPersonPassTag = string.Format("{0} (Third Person)", defaultPassTag);

            defaultRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

            firstPersonRenderStateBlock = new RenderStateBlock(RenderStateMask.Stencil);
            firstPersonRenderStateBlock.stencilReference = FIRST_PERSON_MASK_STENCIL_REFERENCE;
            firstPersonRenderStateBlock.stencilState = new StencilState()
            {
                enabled = true,
                readMask = 255,
                writeMask = 255,
                compareFunction = CompareFunction.Always,
                passOperation = StencilOp.Replace,
                failOperation = StencilOp.Keep
            };

            thirdPersonRenderStateBlock = new RenderStateBlock(RenderStateMask.Stencil);
            thirdPersonRenderStateBlock.stencilReference = FIRST_PERSON_MASK_STENCIL_REFERENCE;
            thirdPersonRenderStateBlock.stencilState = new StencilState()
            {
                enabled = true,
                readMask = 255,
                writeMask = 255,
                compareFunction = CompareFunction.NotEqual,
                passOperation = StencilOp.Keep,
                failOperation = StencilOp.Keep
            };
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderer renderer, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            // Pre-compute references that are used in both first person and default render.
            var camera = renderingData.cameraData.camera;
            var drawSettings = CreateDrawRendererSettings(camera, sortFlags, rendererConfiguration, renderingData.supportsDynamicBatching);

            var renderFirstPerson = !renderingData.cameraData.isSceneViewCamera && renderingData.cameraData.supportsFirstPersonViewModelRendering;
            if (renderFirstPerson)
            {
                ExecuteRenderFirstPersonOnly(renderer, context, camera, drawSettings, ref renderingData);
                ExecuteRenderThirdPersonOnly(renderer, context, camera, drawSettings, ref renderingData);
            }
            else
            {
                ExecuteRenderDefault(renderer, context, camera, drawSettings, ref renderingData);
            }
        }

        private void ExecuteRenderDefault(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(renderDefaultPassTag);
            using (new ProfilingSample(cmd, renderDefaultPassTag))
            {
                // First set render target.
                SetRenderTarget(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter renderers.
                filterSettings.renderingLayerMask = uint.MaxValue;

                // Then render world geometry.
                RenderFiltered(renderer, context, camera, drawSettings, ref renderingData, ref defaultRenderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderFirstPersonOnly(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(renderFirstPersonPassTag);
            using (new ProfilingSample(cmd, renderFirstPersonPassTag))
            {
                // First set the render target.
                SetRenderTarget(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter to only first person renderers.
                filterSettings.renderingLayerMask = renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                // Set pipeline state.
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.FirstPersonDepth, true);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, renderingData.cameraData.firstPersonViewModelProjectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Render first person objects.
                RenderFiltered(renderer, context, camera, drawSettings, ref renderingData, ref firstPersonRenderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderThirdPersonOnly(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(renderThirdPersonPassTag);
            using (new ProfilingSample(cmd, renderThirdPersonPassTag))
            {
                // Setup third person filtering.
                filterSettings.renderingLayerMask = renderingData.cameraData.thirdPersonRenderingLayerMask;

                // Set pipeline state.
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.FirstPersonDepth, false);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Draw third person renderers.
                RenderFiltered(renderer, context, camera, drawSettings, ref renderingData, ref thirdPersonRenderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        protected abstract void SetRenderTarget(CommandBuffer cmd);
        protected abstract void RenderFiltered(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData, ref RenderStateBlock renderStateBlock);
    }
}
