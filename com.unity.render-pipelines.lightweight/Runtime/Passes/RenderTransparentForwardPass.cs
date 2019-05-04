using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    /// <summary>
    /// Render all transparent forward objects into the given color and depth target 
    ///
    /// You can use this pass to render objects that have a material and/or shader
    /// with the pass names LightweightForward or SRPDefaultUnlit. The pass only renders
    /// objects in the rendering queue range of Transparent objects.
    /// </summary>
    public class RenderTransparentForwardPass : ScriptableRenderPass
    {
        const string k_RenderTransparentsDefaultTag = "Render Transparents";
        const string k_RenderTransparentsFirstPersonTag = "Render Transparents (First Person)";
        const string k_RenderTransparentsThirdPersonTag = "Render Transparents (Third Person)";

        FilterRenderersSettings m_FilterSettings;

        RenderTargetHandle colorAttachmentHandle { get; set; }
        RenderTargetHandle depthAttachmentHandle { get; set; }
        RenderTextureDescriptor descriptor { get; set; }
        RendererConfiguration rendererConfiguration;

        public RenderTransparentForwardPass()
        {
            RegisterShaderPassName("LightweightForward");
            RegisterShaderPassName("SRPDefaultUnlit");

            m_FilterSettings = new FilterRenderersSettings(true)
            {
                renderQueueRange = RenderQueueRange.transparent,
                renderingLayerMask = uint.MaxValue
            };
        }

        /// <summary>
        /// Configure the pass before execution
        /// </summary>
        /// <param name="baseDescriptor">Current target descriptor</param>
        /// <param name="colorAttachmentHandle">Color attachment to render into</param>
        /// <param name="depthAttachmentHandle">Depth attachment to render into</param>
        /// <param name="configuration">Specific render configuration</param>
        public void Setup(
            RenderTextureDescriptor baseDescriptor,
            RenderTargetHandle colorAttachmentHandle,
            RenderTargetHandle depthAttachmentHandle,
            RendererConfiguration configuration)
        {
            this.colorAttachmentHandle = colorAttachmentHandle;
            this.depthAttachmentHandle = depthAttachmentHandle;
            descriptor = baseDescriptor;
            rendererConfiguration = configuration;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderer renderer, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            // Pre-compute references that are used in both first person and default render.
            Camera camera = renderingData.cameraData.camera;
            var drawSettings = CreateDrawRendererSettings(camera, SortFlags.CommonTransparent, rendererConfiguration, renderingData.supportsDynamicBatching);

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
            CommandBuffer cmd = CommandBufferPool.Get(k_RenderTransparentsDefaultTag);
            using (new ProfilingSample(cmd, k_RenderTransparentsDefaultTag))
            {
                // First set render target.
                SetRenderTarget(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter renderers.
                m_FilterSettings.renderingLayerMask = uint.MaxValue;

                // Then render world geometry.
                XRUtils.DrawOcclusionMesh(cmd, camera, renderingData.cameraData.isStereoEnabled);
                context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                renderer.RenderObjectsWithError(context, ref renderingData.cullResults, camera, m_FilterSettings, SortFlags.None);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderFirstPersonOnly(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_RenderTransparentsFirstPersonTag);
            using (new ProfilingSample(cmd, k_RenderTransparentsFirstPersonTag))
            {
                // First set the render target.
                SetRenderTarget(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter to only first person renderers.
                m_FilterSettings.renderingLayerMask = renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                // Set pipeline state.
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.FirstPersonDepth, true);
                cmd.SetStencilState(2, CompareFunction.Always, StencilOp.Replace, StencilOp.Keep);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, renderingData.cameraData.firstPersonViewModelProjectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Render first person objects.
                context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                renderer.RenderObjectsWithError(context, ref renderingData.cullResults, camera, m_FilterSettings, SortFlags.None);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderThirdPersonOnly(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_RenderTransparentsThirdPersonTag);
            using (new ProfilingSample(cmd, k_RenderTransparentsThirdPersonTag))
            {
                // Setup third person filtering.
                m_FilterSettings.renderingLayerMask = uint.MaxValue & ~renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                // Set pipeline state.
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.FirstPersonDepth, false);
                cmd.SetStencilState(2, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Draw third person renderers.
                XRUtils.DrawOcclusionMesh(cmd, camera, renderingData.cameraData.isStereoEnabled);
                context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                renderer.RenderObjectsWithError(context, ref renderingData.cullResults, camera, m_FilterSettings, SortFlags.None);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Clear stencil state.
                cmd.SetStencilState(2, CompareFunction.Disabled, StencilOp.Keep, StencilOp.Keep);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        private void SetRenderTarget(CommandBuffer cmd)
        {
            RenderBufferLoadAction loadOp = RenderBufferLoadAction.Load;
            RenderBufferStoreAction storeOp = RenderBufferStoreAction.Store;

            SetRenderTarget(cmd,
                colorAttachmentHandle.Identifier(), loadOp, storeOp,
                depthAttachmentHandle.Identifier(), loadOp, storeOp,
                ClearFlag.None, Color.black,
                descriptor.dimension);

        }
    }
}
