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
        const string k_DepthPrepassDefaultTag = "Depth Prepass";
        const string k_DepthPrepassFirstPersonTag = "Depth Prepass (First Person)";
        const string k_DepthPrepassThirdPersonTag = "Depth Prepass (Third Person)";

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
            {
                throw new ArgumentNullException("renderer");
            }

            // Pre-compute references used across all pass variants.
            var camera = renderingData.cameraData.camera;
            var drawSettings = CreateDrawRendererSettings(renderingData.cameraData.camera, renderingData.cameraData.defaultOpaqueSortFlags, RendererConfiguration.None, renderingData.supportsDynamicBatching);

            var renderFirstPerson = !renderingData.cameraData.isSceneViewCamera && renderingData.cameraData.supportsFirstPersonViewModelRendering;
            if (renderFirstPerson)
            {
                ExecuteRenderFirstPersonOnly(context, camera, drawSettings, ref renderingData);
                ExecuteRenderThirdPersonOnly(context, camera, drawSettings, ref renderingData);
            }
            else
            {
                ExecuteRenderDefault(context, camera, drawSettings, ref renderingData);

            }
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

        private void ExecuteRenderDefault(ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_DepthPrepassDefaultTag);
            using (new ProfilingSample(cmd, k_DepthPrepassDefaultTag))
            {
                // First get and set render target.
                AllocateAndSetRenderTarget(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter renderers.
                m_FilterSettings.renderingLayerMask = uint.MaxValue;

                // Then render all objects.
                RenderFiltered(context, camera, drawSettings, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderFirstPersonOnly(ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_DepthPrepassFirstPersonTag);
            using (new ProfilingSample(cmd, k_DepthPrepassFirstPersonTag))
            {
                // First setup render target.
                AllocateAndSetRenderTarget(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then filter to first person only.
                m_FilterSettings.renderingLayerMask = renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                // Set stencil, viewproj state.
                cmd.SetStencilState(2, CompareFunction.Always, StencilOp.Replace, StencilOp.Keep);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, renderingData.cameraData.firstPersonViewModelProjectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Finally render objects.
                RenderFiltered(context, camera, drawSettings, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void ExecuteRenderThirdPersonOnly(ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_DepthPrepassThirdPersonTag);
            using (new ProfilingSample(cmd, k_DepthPrepassThirdPersonTag))
            {
                // Setup third person rendering filter.
                m_FilterSettings.renderingLayerMask = uint.MaxValue & ~renderingData.cameraData.firstPersonViewModelRenderingLayerMask;

                // Setup stencil, viewproj stae.
                cmd.SetStencilState(2, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Then render third person objects.
                RenderFiltered(context, camera, drawSettings, ref renderingData);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Clear stencil state.
                cmd.SetStencilState(2, CompareFunction.Disabled, StencilOp.Keep, StencilOp.Keep);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void AllocateAndSetRenderTarget(CommandBuffer cmd)
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

        private void RenderFiltered(ScriptableRenderContext context, Camera camera, DrawRendererSettings drawSettings, ref RenderingData renderingData)
        {
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
        }
    }
}
