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
        const string k_RenderOpaquesTag = "Render Opaques";
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
                throw new ArgumentNullException("renderer");

            var camera = renderingData.cameraData.camera;

            CommandBuffer cmd = CommandBufferPool.Get(k_RenderOpaquesTag);
            using (new ProfilingSample(cmd, k_RenderOpaquesTag))
            {
                // When ClearFlag.None that means this is not the first render pass to write to camera target.
                // In that case we set loadOp for both color and depth as RenderBufferLoadAction.Load
                RenderBufferLoadAction loadOp = clearFlag != ClearFlag.None ? RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load;
                RenderBufferStoreAction storeOp = RenderBufferStoreAction.Store;

                SetRenderTarget(cmd, colorAttachmentHandle.Identifier(), loadOp, storeOp,
                    depthAttachmentHandle.Identifier(), loadOp, storeOp, clearFlag, clearColor, descriptor.dimension);

                // TODO: We need a proper way to handle multiple camera/ camera stack. Issue is: multiple cameras can share a same RT
                // (e.g, split screen games). However devs have to be dilligent with it and know when to clear/preserve color.
                // For now we make it consistent by resolving viewport with a RT until we can have a proper camera management system
                //if (colorAttachmentHandle == -1 && !cameraData.isDefaultViewport)
                //    cmd.SetViewport(camera.pixelRect);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                XRUtils.DrawOcclusionMesh(cmd, camera, renderingData.cameraData.isStereoEnabled);

                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = CreateDrawRendererSettings(camera, sortFlags, rendererConfiguration, renderingData.supportsDynamicBatching);

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

            CommandBufferPool.Release(cmd);
        }
    }
}
