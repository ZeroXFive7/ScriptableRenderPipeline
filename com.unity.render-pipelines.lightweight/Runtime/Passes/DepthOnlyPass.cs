using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    /// <summary>
    /// Render all objects that have a 'DepthOnly' pass into the given depth buffer.
    ///
    /// You can use this pass to prime a depth buffer for subsequent rendering.
    /// Use it as a z-prepass, or use it to generate a depth buffer.
    /// </summary>
    public class DepthOnlyPass : RenderForwardPass
    {
        private const int DEPTH_BUFFER_BITS = 32;

        private RenderTargetHandle depthAttachmentHandle { get; set; }
        internal RenderTextureDescriptor descriptor { get; private set; }

        /// <summary>
        /// Create the DepthOnlyPass
        /// </summary>
        public DepthOnlyPass() : base("Depth Prepass")
        {
            RegisterShaderPassName("DepthOnly");

            filterSettings = new FilterRenderersSettings(true)
            {
                renderQueueRange = RenderQueueRange.opaque,
                renderingLayerMask = uint.MaxValue
            };
        }
        
        /// <summary>
        /// Configure the pass
        /// </summary>
        public void Setup(
            RenderTextureDescriptor baseDescriptor,
            RenderTargetHandle depthAttachmentHandle,
            SampleCount samples,
            SortFlags sortFlags)
        {
            this.depthAttachmentHandle = depthAttachmentHandle;
            baseDescriptor.colorFormat = RenderTextureFormat.Depth;
            baseDescriptor.depthBufferBits = DEPTH_BUFFER_BITS;

            this.rendererConfiguration = RendererConfiguration.None;
            this.sortFlags = sortFlags;

            if ((int)samples > 1)
            {
                baseDescriptor.bindMS = false;
                baseDescriptor.msaaSamples = (int)samples;
            }

            descriptor = baseDescriptor;
        }

        /// <inheritdoc/>
        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");
            
            if (depthAttachmentHandle != RenderTargetHandle.CameraTarget)
            {
                cmd.ReleaseTemporaryRT(depthAttachmentHandle.id);
                depthAttachmentHandle = RenderTargetHandle.CameraTarget;
            }
        }

        protected override void SetRenderTarget(CommandBuffer cmd)
        {
            cmd.GetTemporaryRT(depthAttachmentHandle.id, descriptor, FilterMode.Point);

            SetRenderTarget(
                cmd,
                depthAttachmentHandle.Identifier(),
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store,
                ClearFlag.Depth,
                Color.black,
                descriptor.dimension);
        }

        protected override void RenderFiltered(ScriptableRenderer renderer, ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData, ref RenderStateBlock renderStateBlock)
        {
            if (renderingData.cameraData.isStereoEnabled)
            {
                context.StartMultiEye(camera);
                context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, filterSettings, renderStateBlock);
                context.StopMultiEye(camera);
            }
            else
            {
                context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, filterSettings, renderStateBlock);
            }
        }
    }
}
