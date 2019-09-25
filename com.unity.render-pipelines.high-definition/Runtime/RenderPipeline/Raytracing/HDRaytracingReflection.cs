using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
#if ENABLE_RAYTRACING
    public class HDRaytracingReflections
    {
        // External structures
        HDRenderPipelineAsset m_PipelineAsset = null;
        RenderPipelineResources m_PipelineResources = null;
        SkyManager m_SkyManager = null;
        HDRaytracingManager m_RaytracingManager = null;
        SharedRTManager m_SharedRTManager = null;
        GBufferManager m_GbufferManager = null;

        // Intermediate buffer that stores the reflection pre-denoising
        RTHandleSystem.RTHandle m_LightingTexture = null;
        RTHandleSystem.RTHandle m_HitPdfTexture = null;
        RTHandleSystem.RTHandle m_VarianceBuffer = null;
        RTHandleSystem.RTHandle m_MinBoundBuffer = null;
        RTHandleSystem.RTHandle m_MaxBoundBuffer = null;

        // String values
        const string m_RayGenHalfResName = "RayGenHalfRes";
        const string m_RayGenIntegrationName = "RayGenIntegration";
        const string m_MissShaderName = "MissShaderReflections";
        const string m_ClosestHitShaderName = "ClosestHitMain";

        public HDRaytracingReflections()
        {
        }

        public void Init(HDRenderPipelineAsset asset, SkyManager skyManager, HDRaytracingManager raytracingManager, SharedRTManager sharedRTManager, GBufferManager gbufferManager)
        {
            // Keep track of the pipeline asset
            m_PipelineAsset = asset;
            m_PipelineResources = asset.renderPipelineResources;

            // Keep track of the sky manager
            m_SkyManager = skyManager;

            // keep track of the ray tracing manager
            m_RaytracingManager = raytracingManager;

            // Keep track of the shared rt manager
            m_SharedRTManager = sharedRTManager;
            m_GbufferManager = gbufferManager;

            m_LightingTexture = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "LightingBuffer");
            m_HitPdfTexture = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "HitPdfBuffer");
            m_VarianceBuffer = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "VarianceBuffer");
            m_MinBoundBuffer = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "MinBoundBuffer");
            m_MaxBoundBuffer = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "MaxBoundBuffer");
        }

       static RTHandleSystem.RTHandle ReflectionHistoryBufferAllocatorFunction(string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            return rtHandleSystem.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                                        enableRandomWrite: true, useMipMap: true, autoGenerateMips: false,
                                        name: string.Format("ReflectionHistoryBuffer{0}", frameIndex));
        }

        public void Release()
        {
            RTHandles.Release(m_MinBoundBuffer);
            RTHandles.Release(m_MaxBoundBuffer);
            RTHandles.Release(m_VarianceBuffer);
            RTHandles.Release(m_HitPdfTexture);
            RTHandles.Release(m_LightingTexture);
        }

        public void RenderReflections(HDCamera hdCamera, CommandBuffer cmd, RTHandleSystem.RTHandle outputTexture, ScriptableRenderContext renderContext, uint frameCount)
        {
            // First thing to check is: Do we have a valid ray-tracing environment?
            HDRaytracingEnvironment rtEnvironement = m_RaytracingManager.CurrentEnvironment();
            BlueNoise blueNoise = m_RaytracingManager.GetBlueNoiseManager();
            ComputeShader bilateralFilter = m_PipelineAsset.renderPipelineResources.shaders.reflectionBilateralFilterCS;
            RaytracingShader reflectionShader = m_PipelineAsset.renderPipelineResources.shaders.reflectionRaytracing;

            bool invalidState = rtEnvironement == null || blueNoise == null
                || bilateralFilter == null || reflectionShader == null 
                || m_PipelineResources.textures.owenScrambledTex == null || m_PipelineResources.textures.scramblingTex == null;

            // If no acceleration structure available, end it now
            if (invalidState)
                return;

            // Grab the acceleration structures and the light cluster to use
            RaytracingAccelerationStructure accelerationStructure = m_RaytracingManager.RequestAccelerationStructure(rtEnvironement.reflLayerMask);
            HDRaytracingLightCluster lightCluster = m_RaytracingManager.RequestLightCluster(rtEnvironement.reflLayerMask);

            // Compute the actual resolution that is needed base on the quality
            string targetRayGen = "";
            switch (rtEnvironement.reflQualityMode)
            {
                case HDRaytracingEnvironment.ReflectionsQuality.QuarterRes:
                {
                    targetRayGen = m_RayGenHalfResName;
                };
                break;
                case HDRaytracingEnvironment.ReflectionsQuality.Integration:
                {
                    targetRayGen = m_RayGenIntegrationName;
                };
                break;
            }

            // Define the shader pass to use for the reflection pass
            cmd.SetRaytracingShaderPass(reflectionShader, "ReflectionDXR");

            // Set the acceleration structure for the pass
            cmd.SetRaytracingAccelerationStructure(reflectionShader, HDShaderIDs._RaytracingAccelerationStructureName, accelerationStructure);

            // Inject the ray-tracing sampling data
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._OwenScrambledTexture, m_PipelineResources.textures.owenScrambledTex);
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._ScramblingTexture, m_PipelineResources.textures.scramblingTex);

            // Global reflection parameters
            cmd.SetRaytracingFloatParams(reflectionShader, HDShaderIDs._RaytracingIntensityClamp, rtEnvironement.reflClampValue);
            cmd.SetRaytracingFloatParams(reflectionShader, HDShaderIDs._RaytracingReflectionMinSmoothness, rtEnvironement.reflMinSmoothness);
            cmd.SetRaytracingFloatParams(reflectionShader, HDShaderIDs._RaytracingReflectionMaxDistance, rtEnvironement.reflBlendDistance);

            // Inject the ray generation data
            cmd.SetGlobalFloat(HDShaderIDs._RaytracingRayBias, rtEnvironement.rayBias);
            cmd.SetGlobalFloat(HDShaderIDs._RaytracingRayMaxLength, rtEnvironement.reflRayLength);
            cmd.SetRaytracingIntParams(reflectionShader, HDShaderIDs._RaytracingNumSamples, rtEnvironement.reflNumMaxSamples);
            int frameIndex = hdCamera.IsTAAEnabled() ? hdCamera.taaFrameIndex : (int)frameCount % 8;
            cmd.SetGlobalInt(HDShaderIDs._RaytracingFrameIndex, frameIndex);

            // Set the data for the ray generation
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._SsrLightingTextureRW, m_LightingTexture);
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._SsrHitPointTexture, m_HitPdfTexture);
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._DepthTexture, m_SharedRTManager.GetDepthStencilBuffer());
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._NormalBufferTexture, m_SharedRTManager.GetNormalBuffer());

            // Set ray count tex
            cmd.SetRaytracingIntParam(reflectionShader, HDShaderIDs._RayCountEnabled, m_RaytracingManager.rayCountManager.RayCountIsEnabled());
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._RayCountTexture, m_RaytracingManager.rayCountManager.rayCountTexture);

            // Compute the pixel spread value
            float pixelSpreadAngle = Mathf.Atan(2.0f * Mathf.Tan(hdCamera.camera.fieldOfView * Mathf.PI / 360.0f) / Mathf.Min(hdCamera.actualWidth, hdCamera.actualHeight));
            cmd.SetRaytracingFloatParam(reflectionShader, HDShaderIDs._RaytracingPixelSpreadAngle, pixelSpreadAngle);

            // LightLoop data
            cmd.SetGlobalBuffer(HDShaderIDs._RaytracingLightCluster, lightCluster.GetCluster());
            cmd.SetGlobalBuffer(HDShaderIDs._LightDatasRT, lightCluster.GetLightDatas());
            cmd.SetGlobalVector(HDShaderIDs._MinClusterPos, lightCluster.GetMinClusterPos());
            cmd.SetGlobalVector(HDShaderIDs._MaxClusterPos, lightCluster.GetMaxClusterPos());
            cmd.SetGlobalInt(HDShaderIDs._LightPerCellCount, rtEnvironement.maxNumLightsPercell);
            cmd.SetGlobalInt(HDShaderIDs._PunctualLightCountRT, lightCluster.GetPunctualLightCount());
            cmd.SetGlobalInt(HDShaderIDs._AreaLightCountRT, lightCluster.GetAreaLightCount());

            // Evaluate the clear coat mask texture based on the lit shader mode
            RenderTargetIdentifier clearCoatMaskTexture = hdCamera.frameSettings.litShaderMode == LitShaderMode.Deferred ? m_GbufferManager.GetBuffersRTI()[2] : Texture2D.blackTexture;
            cmd.SetRaytracingTextureParam(reflectionShader, targetRayGen, HDShaderIDs._SsrClearCoatMaskTexture, clearCoatMaskTexture);

            // Set the data for the ray miss
            cmd.SetRaytracingTextureParam(reflectionShader, m_MissShaderName, HDShaderIDs._SkyTexture, m_SkyManager.skyReflection);

        DeferredLightingRTParameters PrepareReflectionDeferredLightingRTParameters(HDCamera hdCamera, HDRaytracingEnvironment rtEnv)
        {
            DeferredLightingRTParameters deferredParameters = new DeferredLightingRTParameters();

            // Fetch the GI volume component
            var settings = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();

            // Make sure the binning buffer has the right size
            CheckBinningBuffersSize(hdCamera);

            // Generic attributes
            deferredParameters.rayBinning = settings.rayBinning.value;
            deferredParameters.layerMask = rtEnv.reflLayerMask;
            deferredParameters.maxRayLength = settings.rayLength.value;
            deferredParameters.clampValue = settings.clampValue.value;
            deferredParameters.includeSky = settings.reflectSky.value;
            deferredParameters.diffuseLightingOnly = false;
            deferredParameters.halfResolution = !settings.fullResolution.value;
            deferredParameters.rtEnv = rtEnv;
            deferredParameters.rayCountFlag = m_RayTracingManager.rayCountManager.RayCountIsEnabled();
            deferredParameters.preExpose = false;

            // Camera data
            deferredParameters.width = hdCamera.actualWidth;
            deferredParameters.height = hdCamera.actualHeight;
            deferredParameters.viewCount = hdCamera.viewCount;
            deferredParameters.fov = hdCamera.camera.fieldOfView;

            // Compute buffers
            deferredParameters.rayBinResult = m_RayBinResult;
            deferredParameters.rayBinSizeResult = m_RayBinSizeResult;
            deferredParameters.accelerationStructure = m_RayTracingManager.RequestAccelerationStructure(rtEnv.reflLayerMask);
            deferredParameters.lightCluster = m_RayTracingManager.RequestLightCluster(rtEnv.reflLayerMask);
             
            // Shaders
            deferredParameters.gBufferRaytracingRT = m_Asset.renderPipelineRayTracingResources.gBufferRaytracingRT;
            deferredParameters.deferredRaytracingCS = m_Asset.renderPipelineRayTracingResources.deferredRaytracingCS;
            deferredParameters.rayBinningCS = m_Asset.renderPipelineRayTracingResources.rayBinningCS;

            // XRTODO: add ray binning support for single-pass
            if (deferredParameters.viewCount > 1 && deferredParameters.rayBinning)
            {
                deferredParameters.rayBinning = false;
                Debug.LogWarning("Ray binning is not supported with XR single-pass rendering!");
            }

            return deferredParameters;
        }

        public void RenderReflectionsT1(HDCamera hdCamera, CommandBuffer cmd, RTHandle outputTexture, ScriptableRenderContext renderContext, int frameCount)
        {
            // Fetch the required resources
            HDRaytracingEnvironment rtEnvironment = m_RayTracingManager.CurrentEnvironment();
            BlueNoise blueNoise = m_RayTracingManager.GetBlueNoiseManager();
            RayTracingShader reflectionShaderRT = m_Asset.renderPipelineRayTracingResources.reflectionRaytracingRT;
            ComputeShader reflectionShaderCS = m_Asset.renderPipelineRayTracingResources.reflectionRaytracingCS;
            ComputeShader reflectionFilter = m_Asset.renderPipelineRayTracingResources.reflectionBilateralFilterCS;

            // Fetch all the settings
            var settings = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            LightCluster lightClusterSettings = VolumeManager.instance.stack.GetComponent<LightCluster>();

            if (settings.deferredMode.value)
            {
                case HDRaytracingEnvironment.ReflectionsQuality.QuarterRes:
                {
                    // Evaluate the dispatch parameters
                    int areaTileSize = 8;
                    int numTilesXHR = (texWidth + (areaTileSize - 1)) / areaTileSize;
                    int numTilesYHR = (texHeight + (areaTileSize - 1)) / areaTileSize;

                    // Compute the directions
                    cmd.DispatchCompute(reflectionShaderCS, currentKernel, numTilesXHR, numTilesYHR, hdCamera.viewCount);
                }
                else
                {
                    widthResolution = (uint)hdCamera.actualWidth;
                    heightResolution = (uint)hdCamera.actualHeight;
                };
                break;
            }

                    // Compute the directions
                    cmd.DispatchCompute(reflectionShaderCS, currentKernel, numTilesXHR, numTilesYHR, hdCamera.viewCount);
                }
                // Prepare the components for the deferred lighting
                DeferredLightingRTParameters deferredParamters = PrepareReflectionDeferredLightingRTParameters(hdCamera, rtEnvironment);
                DeferredLightingRTResources deferredResources = PrepareDeferredLightingRTResources(m_ReflIntermediateTexture1, m_ReflIntermediateTexture0);

            // Run the calculus
            cmd.DispatchRays(reflectionShader, targetRayGen, widthResolution, heightResolution, 1);

                // Run the computation
                if (settings.fullResolution.value)
                {
                    cmd.DispatchRays(reflectionShaderRT, m_RayGenReflectionFullResName, (uint)hdCamera.actualWidth, (uint)hdCamera.actualHeight, (uint)hdCamera.viewCount);
                }
                else
                {
                    // Run the computation
                    cmd.DispatchRays(reflectionShaderRT, m_RayGenReflectionHalfResName, (uint)(hdCamera.actualWidth / 2), (uint)(hdCamera.actualHeight / 2), (uint)hdCamera.viewCount);
                }
            }

            using (new ProfilingSample(cmd, "Filter Reflection", CustomSamplerId.RaytracingFilterReflection.GetSampler()))
            {
                switch (rtEnvironement.reflQualityMode)
                {
                    currentKernel = reflectionFilter.FindKernel("ReflectionIntegrationUpscaleFullRes");
                }
                else
                {
                    currentKernel = reflectionFilter.FindKernel("ReflectionIntegrationUpscaleHalfRes");
                }

                // Inject all the parameters for the compute
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, HDShaderIDs._SsrLightingTextureRW, m_ReflIntermediateTexture0);
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, HDShaderIDs._SsrHitPointTexture, m_ReflIntermediateTexture1);
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, HDShaderIDs._DepthTexture, m_SharedRTManager.GetDepthStencilBuffer());
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, HDShaderIDs._NormalBufferTexture, m_SharedRTManager.GetNormalBuffer());
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, HDShaderIDs._BlueNoiseTexture, blueNoise.textureArray16RGB);
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, "_RaytracingReflectionTexture", outputTexture);
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, HDShaderIDs._ScramblingTexture, m_Asset.renderPipelineResources.textures.scramblingTex);
                cmd.SetComputeIntParam(reflectionFilter, HDShaderIDs._SpatialFilterRadius, settings.upscaleRadius.value);
                cmd.SetComputeIntParam(reflectionFilter, HDShaderIDs._RaytracingDenoiseRadius, settings.denoise.value ? settings.denoiserRadius.value : 0);
                cmd.SetComputeFloatParam(reflectionFilter, HDShaderIDs._RaytracingReflectionMinSmoothness, settings.minSmoothness.value);

                // Texture dimensions
                int texWidth = hdCamera.actualWidth;
                int texHeight = hdCamera.actualHeight;

                // Evaluate the dispatch parameters
                int areaTileSize = 8;
                int numTilesXHR = (texWidth  + (areaTileSize - 1)) / areaTileSize;
                int numTilesYHR = (texHeight + (areaTileSize - 1)) / areaTileSize;

                // Bind the right texture for clear coat support
                RenderTargetIdentifier clearCoatMaskTexture = hdCamera.frameSettings.litShaderMode == LitShaderMode.Deferred ? m_GbufferManager.GetBuffersRTI()[2] : TextureXR.GetBlackTexture();
                cmd.SetComputeTextureParam(reflectionFilter, currentKernel, HDShaderIDs._SsrClearCoatMaskTexture, clearCoatMaskTexture);

                // Compute the texture
                cmd.DispatchCompute(reflectionFilter, currentKernel, numTilesXHR, numTilesYHR, hdCamera.viewCount);

                using (new ProfilingSample(cmd, "Filter Reflection", CustomSamplerId.RaytracingFilterReflection.GetSampler()))
                {
                    if (settings.denoise.value)
                    {
                        // Fetch the right filter to use
                        int currentKernel = bilateralFilter.FindKernel("RaytracingReflectionFilter");

                        // Inject all the parameters for the compute
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._SsrLightingTextureRW, m_LightingTexture);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._SsrHitPointTexture, m_HitPdfTexture);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._DepthTexture, m_SharedRTManager.GetDepthStencilBuffer());
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._NormalBufferTexture, m_SharedRTManager.GetNormalBuffer());
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, "_NoiseTexture", blueNoise.textureArray16RGB);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, "_VarianceTexture", m_VarianceBuffer);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, "_MinColorRangeTexture", m_MinBoundBuffer);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, "_MaxColorRangeTexture", m_MaxBoundBuffer);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, "_RaytracingReflectionTexture", outputTexture);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._ScramblingTexture, m_PipelineResources.textures.scramblingTex);
                        cmd.SetComputeIntParam(bilateralFilter, HDShaderIDs._SpatialFilterRadius, rtEnvironement.reflSpatialFilterRadius);

                        // Texture dimensions
                        int texWidth = outputTexture.rt.width ;
                        int texHeight = outputTexture.rt.width;

                        // Evaluate the dispatch parameters
                        int areaTileSize = 8;
                        int numTilesXHR = (texWidth / 2 + (areaTileSize - 1)) / areaTileSize;
                        int numTilesYHR = (texHeight / 2 + (areaTileSize - 1)) / areaTileSize;

                        // Bind the right texture for clear coat support
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._SsrClearCoatMaskTexture, clearCoatMaskTexture);
                        
                        // Compute the texture
                        cmd.DispatchCompute(bilateralFilter, currentKernel, numTilesXHR, numTilesYHR, 1);
                        
                        int numTilesXFR = (texWidth + (areaTileSize - 1)) / areaTileSize;
                        int numTilesYFR = (texHeight + (areaTileSize - 1)) / areaTileSize;

                        RTHandleSystem.RTHandle history = hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.RaytracedReflection)
                            ?? hdCamera.AllocHistoryFrameRT((int)HDCameraFrameHistoryType.RaytracedReflection, ReflectionHistoryBufferAllocatorFunction, 1);
                        
                        // Fetch the right filter to use
                        currentKernel = bilateralFilter.FindKernel("TemporalAccumulationFilter");
                        cmd.SetComputeFloatParam(bilateralFilter, HDShaderIDs._TemporalAccumuationWeight, rtEnvironement.reflTemporalAccumulationWeight);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._AccumulatedFrameTexture, history);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, HDShaderIDs._CurrentFrameTexture, outputTexture);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, "_MinColorRangeTexture", m_MinBoundBuffer);
                        cmd.SetComputeTextureParam(bilateralFilter, currentKernel, "_MaxColorRangeTexture", m_MaxBoundBuffer);
                        cmd.DispatchCompute(bilateralFilter, currentKernel, numTilesXFR, numTilesYFR, 1);
                    }
                }
            }
        }

        void RenderReflectionsT2(HDCamera hdCamera, CommandBuffer cmd, RTHandle outputTexture, ScriptableRenderContext renderContext, int frameCount)
        {
            // Fetch the shaders that we will be using
            HDRaytracingEnvironment rtEnvironment = m_RayTracingManager.CurrentEnvironment();
            ComputeShader reflectionFilter = m_Asset.renderPipelineRayTracingResources.reflectionBilateralFilterCS;
            RayTracingShader reflectionShader = m_Asset.renderPipelineRayTracingResources.reflectionRaytracingRT;

            var settings = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            LightCluster lightClusterSettings = VolumeManager.instance.stack.GetComponent<LightCluster>();

            // Bind all the required data for ray tracing
            BindRayTracedReflectionData(cmd, hdCamera, rtEnvironment, reflectionShader, settings, lightClusterSettings);

            // Only use the shader variant that has multi bounce if the bounce count > 1
            CoreUtils.SetKeyword(cmd, "MULTI_BOUNCE_INDIRECT", settings.bounceCount.value > 1);

            // We are not in the diffuse only case
            CoreUtils.SetKeyword(cmd, "DIFFUSE_LIGHTING_ONLY", false);

            // Run the computation
            cmd.DispatchRays(reflectionShader, m_RayGenIntegrationName, (uint)hdCamera.actualWidth, (uint)hdCamera.actualHeight, (uint)hdCamera.viewCount);

            // Disable multi-bounce
            CoreUtils.SetKeyword(cmd, "MULTI_BOUNCE_INDIRECT", false);

            using (new ProfilingSample(cmd, "Filter Reflection", CustomSamplerId.RaytracingFilterReflection.GetSampler()))
            {
                if (settings.denoise.value)
                {
                    // Grab the history buffer
                    RTHandle reflectionHistory = hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.RaytracedReflection)
                        ?? hdCamera.AllocHistoryFrameRT((int)HDCameraFrameHistoryType.RaytracedReflection, ReflectionHistoryBufferAllocatorFunction, 1);

                    HDSimpleDenoiser simpleDenoiser = m_RayTracingManager.GetSimpleDenoiser();
                    simpleDenoiser.DenoiseBuffer(cmd, hdCamera, m_ReflIntermediateTexture0, reflectionHistory, outputTexture, settings.denoiserRadius.value, singleChannel: false);
                }
                else
                {
                    HDUtils.BlitCameraTexture(cmd, m_ReflIntermediateTexture0, outputTexture);
                }
            }
        }
    }
#endif
}
