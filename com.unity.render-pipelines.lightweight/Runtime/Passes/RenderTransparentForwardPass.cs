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
    public class RenderTransparentForwardPass : RenderForwardPass
    {
        RenderTargetHandle colorAttachmentHandle { get; set; }
        RenderTargetHandle depthAttachmentHandle { get; set; }
        RenderTextureDescriptor descriptor { get; set; }

        public RenderTransparentForwardPass() : base("Render Transparents")
        {
            RegisterShaderPassName("LightweightForward");
            RegisterShaderPassName("SRPDefaultUnlit");

            filterSettings = new FilterRenderersSettings(true)
            {
                renderQueueRange = RenderQueueRange.transparent,
                renderingLayerMask = uint.MaxValue
            };

            sortFlags = SortFlags.CommonTransparent;
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

        protected override void SetRenderTarget(CommandBuffer cmd)
        {
            RenderBufferLoadAction loadOp = RenderBufferLoadAction.Load;
            RenderBufferStoreAction storeOp = RenderBufferStoreAction.Store;

            SetRenderTarget(cmd,
                colorAttachmentHandle.Identifier(), loadOp, storeOp,
                depthAttachmentHandle.Identifier(), loadOp, storeOp,
                ClearFlag.None, Color.black,
                descriptor.dimension);
        }

        protected override void RenderFiltered(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData, ref RenderStateBlock renderStateBlock)
        {
            context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, filterSettings, renderStateBlock);
            renderer.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, SortFlags.None);
        }
    }
}
