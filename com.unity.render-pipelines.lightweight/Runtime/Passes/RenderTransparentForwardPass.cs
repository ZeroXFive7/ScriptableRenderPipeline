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
        const string k_RenderTransparentsTag = "Render Transparents";

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
                throw new ArgumentNullException("renderer");
            
            CommandBuffer cmd = CommandBufferPool.Get(k_RenderTransparentsTag);
            using (new ProfilingSample(cmd, k_RenderTransparentsTag))
            {
                RenderBufferLoadAction loadOp = RenderBufferLoadAction.Load;
                RenderBufferStoreAction storeOp = RenderBufferStoreAction.Store;
                SetRenderTarget(cmd, colorAttachmentHandle.Identifier(), loadOp, storeOp,
                    depthAttachmentHandle.Identifier(), loadOp, storeOp, ClearFlag.None, Color.black, descriptor.dimension);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                Camera camera = renderingData.cameraData.camera;
                var drawSettings = CreateDrawRendererSettings(camera, SortFlags.CommonTransparent, rendererConfiguration, renderingData.supportsDynamicBatching);

                m_FilterSettings.renderingLayerMask = uint.MaxValue;

                // First attempt to render first person view models.
                var renderFirstPerson = !renderingData.cameraData.isSceneViewCamera && renderingData.cameraData.supportsFirstPersonViewModelRendering;
                if (renderFirstPerson)
                {
                    cmd.SetStencilState(2, CompareFunction.Always, StencilOp.Replace, StencilOp.Keep);

                    var viewMatrix = camera.worldToCameraMatrix;
                    cmd.SetViewProjectionMatrices(viewMatrix, renderingData.cameraData.firstPersonViewModelProjectionMatrix);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // Render first person objects.
                    m_FilterSettings.renderingLayerMask = renderingData.cameraData.firstPersonViewModelRenderingLayerMask;
                    context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                    renderer.RenderObjectsWithError(context, ref renderingData.cullResults, camera, m_FilterSettings, SortFlags.None);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // Then reset view proj state.
                    cmd.SetStencilState(2, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep);
                    cmd.SetViewProjectionMatrices(viewMatrix, camera.projectionMatrix);
                    context.ExecuteCommandBuffer(cmd);

                    // Reset filter settings for world geometry.
                    m_FilterSettings.renderingLayerMask = uint.MaxValue & ~renderingData.cameraData.firstPersonViewModelRenderingLayerMask;
                }

                // Then render world geometry.
                context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);

                // Render objects that did not match any shader pass with error shader
                renderer.RenderObjectsWithError(context, ref renderingData.cullResults, camera, m_FilterSettings, SortFlags.None);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (renderFirstPerson)
                {
                    cmd.SetStencilState(2, CompareFunction.Disabled, StencilOp.Keep, StencilOp.Keep);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
