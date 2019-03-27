using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    /// <summary>
    /// Copy an empty buffer to prime the pipeline with an unknown quantity of state.
    /// </summary>
    public class InitialBlitPass : ScriptableRenderPass
    {
        const string k_InitialBlitTag = "Initial Blit";

        private RenderTargetHandle source { get; set; }
        private RenderTargetHandle destination { get; set; }

        public void Setup(RenderTargetHandle source, RenderTargetHandle destination)
        {
            this.source = source;
            this.destination = destination;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderer renderer, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            CommandBuffer cmd = CommandBufferPool.Get(k_InitialBlitTag);

            RenderTextureDescriptor opaqueDesc = ScriptableRenderer.CreateRenderTextureDescriptor(ref renderingData.cameraData, 0.01f);
            RenderTargetIdentifier sourceRT = source.Identifier();
            RenderTargetIdentifier destRT = destination.Identifier();

            cmd.GetTemporaryRT(destination.id, opaqueDesc, FilterMode.Point);
            cmd.Blit(sourceRT, destRT);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException("cmd");
            }
        }
    }
}
