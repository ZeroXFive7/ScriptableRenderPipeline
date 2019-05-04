using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    /// <summary>
    /// Render all opaque forward objects into the given color and depth target 
    ///
    /// You can use this pass to render objects that have a material and/or shader
    /// with the pass names LightweightForward or SRPDefaultUnlit. The pass only
    /// renders objects in the rendering queue range of Opaque objects.
    /// </summary>
    public class RenderOpaqueForwardPass : ScriptableRenderPass
    {
        const string k_RenderOpaquesDefaultTag = "Render Opaques";
        const string k_RenderOpaquesFirstPersonTag = "Render Opaques (First Person)";
        const string k_RenderOpaquesThirdPersonTag = "Render Opaques (Third Person)";

        FilterRenderersSettings m_FilterSettings;

        RenderTargetHandle colorAttachmentHandle { get; set; }
        RenderTargetHandle depthAttachmentHandle { get; set; }
        RenderTextureDescriptor descriptor { get; set; }
        ClearFlag clearFlag { get; set; }
        Color clearColor { get; set; }

        RendererConfiguration rendererConfiguration;

        public RenderOpaqueForwardPass()
        {
            RegisterShaderPassName("LightweightForward");
            RegisterShaderPassName("SRPDefaultUnlit");

            m_FilterSettings = new FilterRenderersSettings(true)
            {
                renderQueueRange = RenderQueueRange.opaque,
                renderingLayerMask = uint.MaxValue
            };
        }

        /// <summary>
        /// Configure the pass before execution
        /// </summary>
        /// <param name="baseDescriptor">Current target descriptor</param>
        /// <param name="colorAttachmentHandle">Color attachment to render into</param>
        /// <param name="depthAttachmentHandle">Depth attachment to render into</param>
        /// <param name="clearFlag">Camera clear flag</param>
        /// <param name="clearColor">Camera clear color</param>
        /// <param name="configuration">Specific render configuration</param>
        public void Setup(
            RenderTextureDescriptor baseDescriptor,
            RenderTargetHandle colorAttachmentHandle,
            RenderTargetHandle depthAttachmentHandle,
            ClearFlag clearFlag,
            Color clearColor,
            RendererConfiguration configuration)
        {
            this.colorAttachmentHandle = colorAttachmentHandle;
            this.depthAttachmentHandle = depthAttachmentHandle;
            this.clearColor = CoreUtils.ConvertSRGBToActiveColorSpace(clearColor);
            this.clearFlag = clearFlag;
            descriptor = baseDescriptor;
            this.rendererConfiguration = configuration;
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
            var drawSettings = CreateDrawRendererSettings(camera, renderingData.cameraData.defaultOpaqueSortFlags, rendererConfiguration, renderingData.supportsDynamicBatching);

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
            CommandBuffer cmd = CommandBufferPool.Get(k_RenderOpaquesDefaultTag);
            using (new ProfilingSample(cmd, k_RenderOpaquesDefaultTag))
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
            CommandBuffer cmd = CommandBufferPool.Get(k_RenderOpaquesFirstPersonTag);
            using (new ProfilingSample(cmd, k_RenderOpaquesFirstPersonTag))
            {
                // First set the render target.
                SetRenderTarget(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter to only first person renderers.
                m_FilterSettings.renderingLayerMask = renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                // Then set stencil, viewproj state.
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
            CommandBuffer cmd = CommandBufferPool.Get(k_RenderOpaquesThirdPersonTag);
            using (new ProfilingSample(cmd, k_RenderOpaquesThirdPersonTag))
            {
                // Setup third person filtering.
                m_FilterSettings.renderingLayerMask = uint.MaxValue & ~renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                // Setup stencil and view proj state
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
            // When ClearFlag.None that means this is not the first render pass to write to camera target.
            // In that case we set loadOp for both color and depth as RenderBufferLoadAction.Load
            RenderBufferLoadAction loadOp = clearFlag != ClearFlag.None ? RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load;
            RenderBufferStoreAction storeOp = RenderBufferStoreAction.Store;

            SetRenderTarget(cmd,
                colorAttachmentHandle.Identifier(), loadOp, storeOp,
                depthAttachmentHandle.Identifier(), loadOp, storeOp,
                clearFlag, clearColor,
                descriptor.dimension);
        }
    }
}
