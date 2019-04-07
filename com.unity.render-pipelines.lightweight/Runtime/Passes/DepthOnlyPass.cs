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
    public class DepthOnlyPass : ScriptableRenderPass
    {
        const string k_DepthPrepassTag = "Depth Prepass";

        int kDepthBufferBits = 32;

        private RenderTargetHandle depthAttachmentHandle { get; set; }
        internal RenderTextureDescriptor descriptor { get; private set; }

        FilterRenderersSettings m_FilterSettings;

        /// <summary>
        /// Create the DepthOnlyPass
        /// </summary>
        public DepthOnlyPass()
        {
            RegisterShaderPassName("DepthOnly");

            m_FilterSettings = new FilterRenderersSettings(true)
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
            SampleCount samples)
        {
            this.depthAttachmentHandle = depthAttachmentHandle;
            baseDescriptor.colorFormat = RenderTextureFormat.Depth;
            baseDescriptor.depthBufferBits = kDepthBufferBits;

            if ((int)samples > 1)
            {
                baseDescriptor.bindMS = false;
                baseDescriptor.msaaSamples = (int)samples;
            }

            descriptor = baseDescriptor;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderer renderer, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderer == null)
                throw new ArgumentNullException("renderer");

            Camera camera = renderingData.cameraData.camera;

            CommandBuffer cmd = CommandBufferPool.Get(k_DepthPrepassTag);
            using (new ProfilingSample(cmd, k_DepthPrepassTag))
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

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // First render third person objects.
                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = CreateDrawRendererSettings(renderingData.cameraData.camera, sortFlags, RendererConfiguration.None, renderingData.supportsDynamicBatching);

                m_FilterSettings.renderingLayerMask = uint.MaxValue & ~renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                if (renderingData.cameraData.isStereoEnabled)
                {
                    context.StartMultiEye(camera);
                    context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                    context.StopMultiEye(camera);
                }
                else
                {
                    context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (!renderingData.cameraData.isSceneViewCamera)
                {
                    // Then render first person objects.
                    var viewMatrix = camera.worldToCameraMatrix;
                    cmd.SetViewProjectionMatrices(viewMatrix, renderingData.cameraData.firstPersonViewModelProjectionMatrix);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    m_FilterSettings.renderingLayerMask = renderingData.cameraData.firstPersonViewModelRenderingLayerMask;
                    if (renderingData.cameraData.isStereoEnabled)
                    {
                        context.StartMultiEye(camera);
                        context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                        context.StopMultiEye(camera);
                    }
                    else
                    {
                        context.DrawRenderers(renderingData.cullResults.visibleRenderers, ref drawSettings, m_FilterSettings);
                    }

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // Then reset view proj state.
                    cmd.SetViewProjectionMatrices(viewMatrix, camera.projectionMatrix);
                    context.ExecuteCommandBuffer(cmd);
                }
            }

            CommandBufferPool.Release(cmd);
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
    }
}
