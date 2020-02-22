using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.Universal
{
    public class AmbientOcclusionPass : ScriptableRenderPass
    {
        private RenderTextureDescriptor m_Descriptor;
        private Material m_Material;

        const string m_ProfilerTag = "Render Ambient Occlusion Texture";

        public float intensity { get; set; }
        public float sampleRadius { get; set; }
        public float downsample { get; set; }
        public float sampleCount { get; set; }

        public AmbientOcclusionPass()
        {
            return;
        }

        public void Setup(Material material, ref RenderingData renderingData)
        {
            m_Material = material;
            m_Descriptor = renderingData.cameraData.cameraTargetDescriptor;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var finalDesc = GetStereoCompatibleDescriptor(m_Descriptor.width, m_Descriptor.height, GraphicsFormat.R16_SFloat);
            cmd.GetTemporaryRT(ShaderConstants._AmbientOcclusionTexture, finalDesc, FilterMode.Bilinear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);

            using (new ProfilingSample(cmd, m_ProfilerTag))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Start at half-res
                int tw = m_Descriptor.width >> 1;
                int th = m_Descriptor.height >> 1;

                // Prefilter
                var aoParams = new Vector4(intensity,
                    sampleRadius,
                    downsample,
                    sampleCount);

                m_Material.SetVector(ShaderConstants._AOParams, aoParams);

                var intermediateDesc = GetStereoCompatibleDescriptor(tw, th, GraphicsFormat.R16G16B16A16_SFloat);
                cmd.GetTemporaryRT(ShaderConstants._AmbientOcclusionMipDown, intermediateDesc, FilterMode.Bilinear);
                cmd.GetTemporaryRT(ShaderConstants._AmbientOcclusionMipUp, intermediateDesc, FilterMode.Bilinear);
                cmd.Blit(RenderTargetHandle.CameraTarget.id, ShaderConstants._AmbientOcclusionMipDown, m_Material, 0);

                // Classic two pass gaussian blur - use mipUp as a temporary target
                //   First pass does 2x downsampling + 9-tap gaussian
                //   Second pass does 9-tap gaussian using a 5-tap filter + bilinear filtering
                cmd.Blit(ShaderConstants._AmbientOcclusionMipDown, ShaderConstants._AmbientOcclusionMipUp, m_Material, 1);
                cmd.Blit(ShaderConstants._AmbientOcclusionMipUp, ShaderConstants._AmbientOcclusionMipDown, m_Material, 2);

                // Final texture is single-channel.
                cmd.Blit(ShaderConstants._AmbientOcclusionMipDown, ShaderConstants._AmbientOcclusionTexture, m_Material, 3);

                // Cleanup
                cmd.ReleaseTemporaryRT(ShaderConstants._AmbientOcclusionMipUp);
                cmd.ReleaseTemporaryRT(ShaderConstants._AmbientOcclusionMipDown);

                // Setup ambient occlusion on uber
                cmd.SetGlobalTexture(ShaderConstants._AmbientOcclusionTexture, ShaderConstants._AmbientOcclusionTexture);

                cmd.EnableShaderKeyword("_AMBIENT_OCCLUSION");
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(ShaderConstants._AmbientOcclusionTexture);

            cmd.DisableShaderKeyword("_AMBIENT_OCCLUSION");
        }

        private RenderTextureDescriptor GetStereoCompatibleDescriptor(int width, int height, GraphicsFormat format)
        {
            // Inherit the VR setup from the camera descriptor
            var desc = m_Descriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.width = width;
            desc.height = height;
            desc.graphicsFormat = format;
            return desc;
        }

        static class ShaderConstants
        {
            public static readonly int _AOParams = Shader.PropertyToID("_AOParams");

            public static readonly int _AmbientOcclusionTexture = Shader.PropertyToID("_AmbientOcclusionTexture");
            public static readonly int _AmbientOcclusionMipDown = Shader.PropertyToID("_AmbientOcclusionMipDown");
            public static readonly int _AmbientOcclusionMipUp = Shader.PropertyToID("_AmbientOcclusionMipUp");
        }
    }
}
