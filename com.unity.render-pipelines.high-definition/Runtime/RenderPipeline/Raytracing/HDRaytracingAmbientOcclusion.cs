using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
#if ENABLE_RAYTRACING
    public class HDRaytracingAmbientOcclusion
    {
        // External structures
        RenderPipelineResources m_PipelineResources = null;
        RenderPipelineSettings m_PipelineSettings;
        HDRenderPipeline m_RenderPipeline = null;

        // The target denoising kernel
        static int m_KernelFilter;

        // Intermediate buffer that stores the ambient occlusion pre-denoising
        RTHandleSystem.RTHandle m_IntermediateBuffer = null;
        RTHandleSystem.RTHandle m_HitDistanceBuffer = null;
        RTHandleSystem.RTHandle m_ViewSpaceNormalBuffer = null;

        // String values
        const string m_RayGenShaderName = "RayGenAmbientOcclusion";
        const string m_MissShaderName = "MissShaderAmbientOcclusion";
        const string m_ClosestHitShaderName = "ClosestHitMain";

        // Shader Identifiers
        public static readonly int _DenoiseRadius = Shader.PropertyToID("_DenoiseRadius");
        public static readonly int _GaussianSigma = Shader.PropertyToID("_GaussianSigma");

        public HDRaytracingAmbientOcclusion()
        {
        }

        public void Init(HDRenderPipeline renderPipeline)
        {
            // Keep track of the pipeline asset
            m_PipelineSettings = renderPipeline.currentPlatformRenderPipelineSettings;
            m_PipelineResources = renderPipeline.asset.renderPipelineResources;
            m_PipelineRayTracingResources = renderPipeline.asset.renderPipelineRayTracingResources;

            // keep track of the render pipeline
            m_RenderPipeline = renderPipeline;

            // Allocate the intermediate textures
            m_AOIntermediateBuffer0 = RTHandles.Alloc(Vector2.one, TextureXR.slices, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureXR.dimension, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, autoGenerateMips: false, name: "AOIntermediateBuffer0");
            m_AOIntermediateBuffer1 = RTHandles.Alloc(Vector2.one, TextureXR.slices, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureXR.dimension, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, autoGenerateMips: false, name: "AOIntermediateBuffer1");
        }

            // Buffer that holds the average distance of the rays
            m_HitDistanceBuffer = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "HitDistanceBuffer");

            // Buffer that holds the uncompressed normal buffer
            m_ViewSpaceNormalBuffer = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "ViewSpaceNormalBuffer");
        }

        public void Release()
        {
            RTHandles.Release(m_ViewSpaceNormalBuffer);
            RTHandles.Release(m_HitDistanceBuffer);
            RTHandles.Release(m_IntermediateBuffer);
        }

        public void SetDefaultAmbientOcclusionTexture(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(HDShaderIDs._AmbientOcclusionTexture, Texture2D.blackTexture);
            cmd.SetGlobalVector(HDShaderIDs._AmbientOcclusionParam, Vector4.zero);
        }

        public void RenderAO(HDCamera hdCamera, CommandBuffer cmd, RTHandleSystem.RTHandle outputTexture, ScriptableRenderContext renderContext, uint frameCount)
        {
            // If any of the previous requirements is missing, the effect is not requested or no acceleration structure, set the default one and leave right away
            if (!m_RenderPipeline.GetRayTracingState())
            {
                SetDefaultAmbientOcclusionTexture(cmd);
                return;
            }

            RayTracingShader aoShader = m_PipelineRayTracingResources.aoRaytracing;
            var aoSettings = VolumeManager.instance.stack.GetComponent<AmbientOcclusion>();
            RayTracingSettings rayTracingSettings = VolumeManager.instance.stack.GetComponent<RayTracingSettings>();

            using (new ProfilingSample(cmd, "Ray Trace Ambient Occlusion", CustomSamplerId.RaytracingAmbientOcclusion.GetSampler()))
            {
                // Grab the acceleration structure for the target camera
                RayTracingAccelerationStructure accelerationStructure = m_RenderPipeline.RequestAccelerationStructure();

                // Define the shader pass to use for the reflection pass
                cmd.SetRayTracingShaderPass(aoShader, "VisibilityDXR");

                // Set the acceleration structure for the pass
                cmd.SetRayTracingAccelerationStructure(aoShader, HDShaderIDs._RaytracingAccelerationStructureName, accelerationStructure);

                // Inject the ray generation data
                cmd.SetRayTracingFloatParams(aoShader, HDShaderIDs._RaytracingRayBias, rayTracingSettings.rayBias.value);
                cmd.SetRayTracingFloatParams(aoShader, HDShaderIDs._RaytracingRayMaxLength, aoSettings.rayLength.value);
                cmd.SetRayTracingIntParams(aoShader, HDShaderIDs._RaytracingNumSamples, aoSettings.sampleCount.value);

                // Set the data for the ray generation
                cmd.SetRayTracingTextureParam(aoShader, HDShaderIDs._DepthTexture, m_RenderPipeline.sharedRTManager.GetDepthStencilBuffer());
                cmd.SetRayTracingTextureParam(aoShader, HDShaderIDs._NormalBufferTexture, m_RenderPipeline.sharedRTManager.GetNormalBuffer());
                int frameIndex = hdCamera.IsTAAEnabled() ? hdCamera.taaFrameIndex : (int)frameCount % 8;
                cmd.SetGlobalInt(HDShaderIDs._RaytracingFrameIndex, frameIndex);

                // Inject the ray-tracing sampling data
                BlueNoise blueNoise = m_RenderPipeline.GetBlueNoiseManager();
                blueNoise.BindDitheredRNGData8SPP(cmd);
                
                // Value used to scale the ao intensity
                cmd.SetRayTracingFloatParam(aoShader, HDShaderIDs._RaytracingAOIntensity, aoSettings.intensity.value);

                RayCountManager rayCountManager = m_RenderPipeline.GetRayCountManager();
                cmd.SetRayTracingIntParam(aoShader, HDShaderIDs._RayCountEnabled, rayCountManager.RayCountIsEnabled());
                cmd.SetRayTracingTextureParam(aoShader, HDShaderIDs._RayCountTexture, rayCountManager.GetRayCountTexture());

                // Set the output textures
                cmd.SetRayTracingTextureParam(aoShader, HDShaderIDs._AmbientOcclusionTextureRW, m_AOIntermediateBuffer0);

                // Run the computation
                cmd.DispatchRays(aoShader, m_RayGenShaderName, (uint)hdCamera.actualWidth, (uint)hdCamera.actualHeight, (uint)hdCamera.viewCount);
            }

            using (new ProfilingSample(cmd, "Filter Reflection", CustomSamplerId.RaytracingAmbientOcclusion.GetSampler()))
            {
                if(aoSettings.denoise.value)
                {
                    // Grab the history buffer
                    RTHandle ambientOcclusionHistory = hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.RaytracedAmbientOcclusion)
                        ?? hdCamera.AllocHistoryFrameRT((int)HDCameraFrameHistoryType.RaytracedAmbientOcclusion, AmbientOcclusionHistoryBufferAllocatorFunction, 1);

                    // Apply the temporal denoiser
                    HDTemporalFilter temporalFilter = m_RenderPipeline.GetTemporalFilter();
                    temporalFilter.DenoiseBuffer(cmd, hdCamera, m_AOIntermediateBuffer0, ambientOcclusionHistory, m_AOIntermediateBuffer1);

                    // Apply the diffuse denoiser
                    HDDiffuseDenoiser diffuseDenoiser = m_RenderPipeline.GetDiffuseDenoiser();
                    diffuseDenoiser.DenoiseBuffer(cmd, hdCamera, m_AOIntermediateBuffer1, outputTexture, aoSettings.denoiserRadius.value);
                }
                else
                {
                    case HDRaytracingEnvironment.AOFilterMode.Nvidia:
                    {
                        cmd.DenoiseAmbientOcclusionTexture(m_IntermediateBuffer, m_HitDistanceBuffer, m_SharedRTManager.GetDepthStencilBuffer(), m_ViewSpaceNormalBuffer, outputTexture, hdCamera.viewMatrix, hdCamera.projMatrix, (uint)rtEnvironement.maxFilterWidthInPixels, rtEnvironement.filterRadiusInMeters, rtEnvironement.normalSharpness, 1.0f, 0.0f);
                    }
                    break;
                    case HDRaytracingEnvironment.AOFilterMode.Bilateral:
                    {
                        m_KernelFilter = bilateralFilter.FindKernel("JointBilateralFilter");
                        // Inject all the parameters for the compute
                        cmd.SetComputeIntParam(bilateralFilter, _DenoiseRadius, rtEnvironement.aoBilateralRadius);
                        cmd.SetComputeFloatParam(bilateralFilter, _GaussianSigma, rtEnvironement.aoBilateralSigma);
                        cmd.SetComputeTextureParam(bilateralFilter, m_KernelFilter, "_SourceTexture", m_IntermediateBuffer);
                        cmd.SetComputeTextureParam(bilateralFilter, m_KernelFilter, HDShaderIDs._DepthTexture, m_SharedRTManager.GetDepthStencilBuffer());
                        cmd.SetComputeTextureParam(bilateralFilter, m_KernelFilter, HDShaderIDs._NormalBufferTexture, m_SharedRTManager.GetNormalBuffer());

                        // Set the output slot
                        cmd.SetComputeTextureParam(bilateralFilter, m_KernelFilter, "_OutputTexture", outputTexture);

                        // Texture dimensions
                        int texWidth = outputTexture.rt.width;
                        int texHeight = outputTexture.rt.width;

                        // Evaluate the dispatch parameters
                        int areaTileSize = 8;
                        int numTilesX = (texWidth + (areaTileSize - 1)) / areaTileSize;
                        int numTilesY = (texHeight + (areaTileSize - 1)) / areaTileSize;

                        // Compute the texture
                        cmd.DispatchCompute(bilateralFilter, m_KernelFilter, numTilesX, numTilesY, 1);
                    }
                    break;
                    case HDRaytracingEnvironment.AOFilterMode.None:
                    {
                        HDUtils.BlitCameraTexture(cmd, hdCamera, m_IntermediateBuffer, outputTexture);
                    }
                    break;
                }
            }

            // Bind the textures and the params
            cmd.SetGlobalTexture(HDShaderIDs._AmbientOcclusionTexture, outputTexture);
            cmd.SetGlobalVector(HDShaderIDs._AmbientOcclusionParam, new Vector4(0f, 0f, 0f, VolumeManager.instance.stack.GetComponent<AmbientOcclusion>().directLightingStrength.value));

            // TODO: All the push-debug stuff should be centralized somewhere
            (RenderPipelineManager.currentPipeline as HDRenderPipeline).PushFullScreenDebugTexture(hdCamera, cmd, outputTexture, FullScreenDebugMode.SSAO);
        }
    }
#endif
}
