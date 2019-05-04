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
    public class RenderOpaqueForwardPass : RenderForwardPass
    {
        RenderTargetHandle colorAttachmentHandle { get; set; }
        RenderTargetHandle depthAttachmentHandle { get; set; }
        RenderTextureDescriptor descriptor { get; set; }
        ClearFlag clearFlag { get; set; }
        Color clearColor { get; set; }

        public RenderOpaqueForwardPass() : base("Render Opaques")
        {
            RegisterShaderPassName("LightweightForward");
            RegisterShaderPassName("SRPDefaultUnlit");

            filterSettings = new FilterRenderersSettings(true)
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
            SortFlags sortFlags,
            RendererConfiguration configuration)
        {
            this.colorAttachmentHandle = colorAttachmentHandle;
            this.depthAttachmentHandle = depthAttachmentHandle;
            this.clearColor = CoreUtils.ConvertSRGBToActiveColorSpace(clearColor);
            this.clearFlag = clearFlag;
            descriptor = baseDescriptor;
            this.sortFlags = sortFlags;
            this.rendererConfiguration = configuration;
        }

        protected override void SetRenderTarget(CommandBuffer cmd)
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

        protected override void RenderFiltered(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData, ref RenderStateBlock renderStateBlock)
        {
            context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, filterSettings, renderStateBlock);
            renderer.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, SortFlags.None);
        }
    }
}
