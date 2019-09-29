using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    [GenerateHLSL(PackingRules.Exact, false)]
    public struct LightVolume
    {
        public int active;
        public Vector3 position;
        public float range;
        public uint lightType;
    }

#if ENABLE_RAYTRACING
    public class HDRaytracingLightCluster
    {
        // External data
        RenderPipelineResources m_RenderPipelineResources = null;
        HDRenderPipelineRayTracingResources m_RenderPipelineRayTracingResources = null;
        HDRenderPipeline m_RenderPipeline = null;

        // Light data
        LightVolume[] m_LightVolumesCPUArray = null;
        ComputeBuffer m_LightVolumeGPUArray = null;
        ComputeBuffer m_LightCullResult = null;
        ComputeBuffer m_LightCluster = null;
        LightData[] m_LightDataCPUArray = null;
        ComputeBuffer m_LightDataGPUArray = null;

        // Env Light data
        List<EnvLightData> m_EnvLightDataCPUArray = new List<EnvLightData>();
        ComputeBuffer m_EnvLightDataGPUArray = null;

        RTHandle m_DebugLightClusterTexture = null;

        // Light cluster debug material
        Material m_DebugMaterial = null;
        MaterialPropertyBlock m_DebugMaterialProperties = new MaterialPropertyBlock();

        // String values
        const string m_LightClusterKernelName = "RaytracingLightCluster";
        const string m_LightCullKernelName = "RaytracingLightCull";

        public static readonly int _RaytracingLightCluster = Shader.PropertyToID("_RaytracingLightCluster");
        public static readonly int _MinClusterPos = Shader.PropertyToID("_MinClusterPos");
        public static readonly int _MaxClusterPos = Shader.PropertyToID("_MaxClusterPos");
        public static readonly int _ClusterCellSize = Shader.PropertyToID("_ClusterCellSize");
        public static readonly int _LightPerCellCount = Shader.PropertyToID("_LightPerCellCount");
        public static readonly int _LightVolumes = Shader.PropertyToID("_LightVolumes");
        public static readonly int _LightVolumeCount = Shader.PropertyToID("_LightVolumeCount");
        public static readonly int _DebugColorGradientTexture = Shader.PropertyToID("_DebugColorGradientTexture");
        public static readonly int _DebutLightClusterTexture = Shader.PropertyToID("_DebutLightClusterTexture");
        public static readonly int _RaytracingLightCullResult = Shader.PropertyToID("_RaytracingLightCullResult");
        public static readonly int _ClusterCenterPosition = Shader.PropertyToID("_ClusterCenterPosition");
        public static readonly int _ClusterDimension = Shader.PropertyToID("_ClusterDimension");
        
        // Temporary variables
        Vector3 minClusterPos = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 maxClusterPos = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 clusterCellSize = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 clusterCenter = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 clusterDimension = new Vector3(0.0f, 0.0f, 0.0f);
        int punctualLightCount = 0;
        int areaLightCount = 0;

        public HDRaytracingLightCluster()
        {

        }

        public void Initialize(HDRenderPipeline renderPipeline)
        {
            // Keep track of the external buffers
            m_RenderPipelineResources = renderPipeline.asset.renderPipelineResources;
            m_RenderPipelineRayTracingResources = renderPipeline.asset.renderPipelineRayTracingResources;

            // Keep track of the lightloop
            m_LightLoop = lightLoop;

            // Texture used to output debug information
            m_DebugLightClusterTexture = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, useDynamicScale: true, useMipMap: false, name: "DebugLightClusterTexture");

            // Pre allocate the cluster with a dummy size
            m_LightCluster = new ComputeBuffer(1, sizeof(uint));
            m_LightDataGPUArray = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf(typeof(LightData)));
            m_EnvLightDataGPUArray = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf(typeof(EnvLightData)));

            // Create the material required for debug
            m_DebugMaterial = CoreUtils.CreateEngineMaterial(m_RenderPipelineRayTracingResources.lightClusterDebugS);
        }

        public void ReleaseResources()
        {
            m_DebugLightClusterTexture.Release();

            if (m_LightVolumeGPUArray != null)
            {
                CoreUtils.SafeRelease(m_LightVolumeGPUArray);
                m_LightVolumeGPUArray = null;
            }

            if (m_LightCluster != null)
            {
                CoreUtils.SafeRelease(m_LightCluster);
                m_LightCluster = null;
            }

            if (m_LightCullResult != null)
            {
                CoreUtils.SafeRelease(m_LightCullResult);
                m_LightCullResult = null;
            }

            if (m_LightDataGPUArray != null)
            {
                CoreUtils.SafeRelease(m_LightDataGPUArray);
                m_LightDataGPUArray = null;
            }

            if (m_EnvLightDataGPUArray != null)
            {
                CoreUtils.SafeRelease(m_EnvLightDataGPUArray);
                m_EnvLightDataGPUArray = null;
            }

            if (m_DebugMaterial != null)
            {
                CoreUtils.Destroy(m_DebugMaterial);
                m_DebugMaterial = null;
            }
        }

        void ResizeClusterBuffer(int bufferSize)
        {
            // Release the previous buffer
            if (m_LightCluster != null)
            {
                CoreUtils.SafeRelease(m_LightCluster);
                m_LightCluster = null;
            }

            // Allocate the next buffer buffer
            if (bufferSize > 0)
            {
                m_LightCluster = new ComputeBuffer(bufferSize, sizeof(uint));
            }
        }

        void ResizeCullResultBuffer(int numLights)
        {
            // Release the previous buffer
            if (m_LightCullResult != null)
            {
                CoreUtils.SafeRelease(m_LightCullResult);
                m_LightCullResult = null;
            }

            // Allocate the next buffer buffer
            if (numLights > 0)
            {
                m_LightCullResult = new ComputeBuffer(numLights, sizeof(uint));
            }
        }

        void ResizeVolumeBuffer(int numLights)
        {
            // Release the previous buffer
            if (m_LightVolumeGPUArray != null)
            {
                CoreUtils.SafeRelease(m_LightVolumeGPUArray);
                m_LightVolumeGPUArray = null;
            }

            // Allocate the next buffer buffer
            if (numLights > 0)
            {
                m_LightVolumesCPUArray = new LightVolume[numLights];
                m_LightVolumeGPUArray = new ComputeBuffer(numLights, System.Runtime.InteropServices.Marshal.SizeOf(typeof(LightVolume)));
            }
        }

        void ResizeLightDataBuffer(int numLights)
        {
            // Release the previous buffer
            if (m_LightDataGPUArray != null)
            {
                CoreUtils.SafeRelease(m_LightDataGPUArray);
                m_LightDataGPUArray = null;
            }

            // Allocate the next buffer buffer
            if (numLights > 0)
            {
                m_LightDataCPUArray = new LightData[numLights];
                m_LightDataGPUArray = new ComputeBuffer(numLights, System.Runtime.InteropServices.Marshal.SizeOf(typeof(LightData)));
            }
        }

        void BuildGPULightVolumes(List<HDAdditionalLightData> lightArray)
        {
            // Release the previous buffer
            if (m_EnvLightDataGPUArray != null)
            {
                CoreUtils.SafeRelease(m_EnvLightDataGPUArray);
                m_EnvLightDataGPUArray = null;
            }

            // Allocate the next buffer buffer
            if (numEnvLights > 0)
            {
                m_EnvLightDataGPUArray = new ComputeBuffer(numEnvLights, System.Runtime.InteropServices.Marshal.SizeOf(typeof(EnvLightData)));
            }
        }

        void BuildGPULightVolumes(HDRayTracingLights rayTracingLights)
        {
            int totalNumLights = rayTracingLights.lightCount;

            // Make sure the light volume buffer has the right size
            if (m_LightVolumesCPUArray == null || lightArray.Count != m_LightVolumesCPUArray.Length)
            {
                ResizeVolumeBuffer(lightArray.Count);
            }

            // Set Light volume data to the CPU buffer
            punctualLightCount = 0;
            areaLightCount = 0;
            envLightCount = 0;
            totalLightCount = 0;

            int realIndex = 0;
            for (int lightIdx = 0; lightIdx < rayTracingLights.hdLightArray.Count; ++lightIdx)
            {
                HDAdditionalLightData currentLight = rayTracingLights.hdLightArray[lightIdx];

                // When the user deletes a light source in the editor, there is a single frame where the light is null before the collection of light in the scene is triggered
                // the workaround for this is simply to not add it if it is null for that invalid frame
                if (currentLight != null)
                {
                    float lightRange = currentLight.gameObject.GetComponent<Light>().range;
                    m_LightVolumesCPUArray[lightIdx].range = lightRange;
                    m_LightVolumesCPUArray[lightIdx].position = currentLight.gameObject.transform.position;
                    m_LightVolumesCPUArray[lightIdx].active = (currentLight.gameObject.activeInHierarchy ? 1 : 0);
                    if (currentLight.lightTypeExtent == LightTypeExtent.Punctual)
                    {
                        m_LightVolumesCPUArray[lightIdx].lightType = 0;
                        punctualLightCount++;
                    }
                    else
                    {
                        m_LightVolumesCPUArray[lightIdx].lightType = 1;
                        areaLightCount++;
                    }
                    realIndex++;
                }
            }

            int indexOffset = realIndex;

            // Set Env Light volume data to the CPU buffer
            for (int lightIdx = 0; lightIdx < rayTracingLights.reflectionProbeArray.Count; ++lightIdx)
            {
                HDProbe currentEnvLight = rayTracingLights.reflectionProbeArray[lightIdx];
                if (currentEnvLight != null)
                {
                    if(currentEnvLight.influenceVolume.shape == InfluenceShape.Sphere)
                    {
                        m_LightVolumesCPUArray[lightIdx + indexOffset].shape = 0;
                        m_LightVolumesCPUArray[lightIdx + indexOffset].range = new Vector3(currentEnvLight.influenceVolume.sphereRadius, currentEnvLight.influenceVolume.sphereRadius, currentEnvLight.influenceVolume.sphereRadius);
                        m_LightVolumesCPUArray[lightIdx + indexOffset].position = currentEnvLight.influenceToWorld.GetColumn(3);
                    }
                    else
                    {
                        m_LightVolumesCPUArray[lightIdx + indexOffset].shape = 1;
                        m_LightVolumesCPUArray[lightIdx + indexOffset].range = new Vector3(currentEnvLight.influenceVolume.boxSize.x / 2.0f, currentEnvLight.influenceVolume.boxSize.y / 2.0f, currentEnvLight.influenceVolume.boxSize.z / 2.0f);
                        m_LightVolumesCPUArray[lightIdx + indexOffset].position = currentEnvLight.influenceToWorld.GetColumn(3);
                    }
                    m_LightVolumesCPUArray[lightIdx + indexOffset].active = (currentEnvLight.gameObject.activeInHierarchy ? 1 : 0);
                    m_LightVolumesCPUArray[lightIdx + indexOffset].lightIndex = (uint)lightIdx;
                    m_LightVolumesCPUArray[lightIdx + indexOffset].lightType = 2;
                    envLightCount++;
                }
            }

            // Push the light volumes to the GPU
            m_LightVolumeGPUArray.SetData(m_LightVolumesCPUArray);
        }

        void EvaluateClusterVolume(HDCamera hdCamera)
        {
            var settings = VolumeManager.instance.stack.GetComponent<LightCluster>();

        void EvaluateClusterVolume(HDRaytracingEnvironment currentEnv, HDCamera hdCamera, int numLights)
        {
            clusterCenter = hdCamera.camera.gameObject.transform.position;
            minClusterPos.Set(float.MaxValue, float.MaxValue, float.MaxValue);
            maxClusterPos.Set(-float.MaxValue, -float.MaxValue, -float.MaxValue);

            for (int lightIdx = 0; lightIdx < numLights; ++lightIdx)
            {
                minClusterPos.x = Mathf.Min(m_LightVolumesCPUArray[lightIdx].position.x - m_LightVolumesCPUArray[lightIdx].range, minClusterPos.x);
                minClusterPos.y = Mathf.Min(m_LightVolumesCPUArray[lightIdx].position.y - m_LightVolumesCPUArray[lightIdx].range, minClusterPos.y);
                minClusterPos.z = Mathf.Min(m_LightVolumesCPUArray[lightIdx].position.z - m_LightVolumesCPUArray[lightIdx].range, minClusterPos.z);

                maxClusterPos.x = Mathf.Max(m_LightVolumesCPUArray[lightIdx].position.x + m_LightVolumesCPUArray[lightIdx].range, maxClusterPos.x);
                maxClusterPos.y = Mathf.Max(m_LightVolumesCPUArray[lightIdx].position.y + m_LightVolumesCPUArray[lightIdx].range, maxClusterPos.y);
                maxClusterPos.z = Mathf.Max(m_LightVolumesCPUArray[lightIdx].position.z + m_LightVolumesCPUArray[lightIdx].range, maxClusterPos.z);
            }

            minClusterPos.x = minClusterPos.x < clusterCenter.x - currentEnv.cameraClusterRange ? clusterCenter.x - currentEnv.cameraClusterRange : minClusterPos.x;
            minClusterPos.y = minClusterPos.y < clusterCenter.y - currentEnv.cameraClusterRange ? clusterCenter.y - currentEnv.cameraClusterRange : minClusterPos.y;
            minClusterPos.z = minClusterPos.z < clusterCenter.z - currentEnv.cameraClusterRange ? clusterCenter.z - currentEnv.cameraClusterRange : minClusterPos.z;

            maxClusterPos.x = maxClusterPos.x > clusterCenter.x + currentEnv.cameraClusterRange ? clusterCenter.x + currentEnv.cameraClusterRange : maxClusterPos.x;
            maxClusterPos.y = maxClusterPos.y > clusterCenter.y + currentEnv.cameraClusterRange ? clusterCenter.y + currentEnv.cameraClusterRange : maxClusterPos.y;
            maxClusterPos.z = maxClusterPos.z > clusterCenter.z + currentEnv.cameraClusterRange ? clusterCenter.z + currentEnv.cameraClusterRange : maxClusterPos.z;

            // Compute the cell size per dimension
            clusterCellSize = (maxClusterPos - minClusterPos);
            clusterCellSize.x /= 64.0f;
            clusterCellSize.y /= 64.0f;
            clusterCellSize.z /= 32.0f;

            // Compute the bounds of the cluster volume3
            clusterCenter = (maxClusterPos + minClusterPos) / 2.0f;
            clusterDimension = (maxClusterPos - minClusterPos);
        }

        void CullLights(CommandBuffer cmd)
        {
            using (new ProfilingSample(cmd, "Cull Light Cluster", CustomSamplerId.RaytracingCullLights.GetSampler()))
            {
                // Make sure the culling buffer has the right size
                if (m_LightCullResult == null || m_LightCullResult.count != numLights)
                {
                    ResizeCullResultBuffer(numLights);
                }

                ComputeShader lightClusterCS = m_RenderPipelineRayTracingResources.lightClusterBuildCS;

                // Grab the kernel
                int lightClusterCullKernel = lightClusterCS.FindKernel(m_LightCullKernelName);

                // Inject all the parameters
                cmd.SetComputeVectorParam(lightClusterCS, _ClusterCenterPosition, clusterCenter);
                cmd.SetComputeVectorParam(lightClusterCS, _ClusterDimension, clusterDimension);
                cmd.SetComputeFloatParam(lightClusterCS, _LightVolumeCount, HDShadowUtils.Asfloat(numLights));

                cmd.SetComputeBufferParam(lightClusterCS, lightClusterCullKernel, _LightVolumes, m_LightVolumeGPUArray);
                cmd.SetComputeBufferParam(lightClusterCS, lightClusterCullKernel, _RaytracingLightCullResult, m_LightCullResult);

                // Dispatch a compute
                int numLightGroups = (numLights / 16 + 1);
                cmd.DispatchCompute(lightClusterCS, lightClusterCullKernel, numLightGroups, 1, 1);
            }
        }

        void BuildLightCluster(CommandBuffer cmd)
        {
            using (new ProfilingSample(cmd, "Build Light Cluster", CustomSamplerId.RaytracingBuildCluster.GetSampler()))
            {
                // Make sure the Cluster buffer has the right size
                int bufferSize = 64 * 64 * 32 * (currentEnv.maxNumLightsPercell + 3);
                if (m_LightCluster == null || m_LightCluster.count != bufferSize)
                {
                    ResizeClusterBuffer(bufferSize);
                }

                // Grab the kernel
                ComputeShader lightClusterCS = m_RenderPipelineRayTracingResources.lightClusterBuildCS;
                int lightClusterKernel = lightClusterCS.FindKernel(m_LightClusterKernelName);

                // Inject all the parameters
                cmd.SetComputeBufferParam(lightClusterCS, lightClusterKernel, _RaytracingLightCluster, m_LightCluster);
                cmd.SetComputeVectorParam(lightClusterCS, _MinClusterPos, minClusterPos);
                cmd.SetComputeVectorParam(lightClusterCS, _MaxClusterPos, maxClusterPos);
                cmd.SetComputeVectorParam(lightClusterCS, _ClusterCellSize, clusterCellSize);
                cmd.SetComputeFloatParam(lightClusterCS, _LightPerCellCount, HDShadowUtils.Asfloat(currentEnv.maxNumLightsPercell));

                cmd.SetComputeBufferParam(lightClusterCS, lightClusterKernel, _LightVolumes, m_LightVolumeGPUArray);
                cmd.SetComputeFloatParam(lightClusterCS, _LightVolumeCount, HDShadowUtils.Asfloat(numLights));
                cmd.SetComputeBufferParam(lightClusterCS, lightClusterKernel, _RaytracingLightCullResult, m_LightCullResult);

                // Dispatch a compute
                int numGroupsX = 8;
                int numGroupsY = 8;
                int numGroupsZ = 4;
                cmd.DispatchCompute(lightClusterCS, lightClusterKernel, numGroupsX, numGroupsY, numGroupsZ);
            }
        }

        void BuildLightData(CommandBuffer cmd, HDCamera hdCamera, HDRayTracingLights rayTracingLights)
        {
            // If no lights, exit
            if (rayTracingLights.lightCount == 0)
            {
                ResizeLightDataBuffer(1);
                return;
            }

            // Also we need to build the light list data
            if (m_LightDataGPUArray == null || m_LightDataGPUArray.count != rayTracingLights.lightCount)
            {
                ResizeLightDataBuffer(rayTracingLights.lightCount);
            }

            // Build the data for every light
            for (int lightIdx = 0; lightIdx < rayTracingLights.hdLightArray.Count; ++lightIdx)
            {
                var lightData = new LightData();

                HDAdditionalLightData additionalLightData = rayTracingLights.hdLightArray[lightIdx];
                // When the user deletes a light source in the editor, there is a single frame where the light is null before the collection of light in the scene is triggered
                // the workaround for this is simply to add an invalid light for that frame
                if(additionalLightData == null)
                {
                    m_LightDataCPUArray[lightIdx] = lightData;
                    continue;
                }
                Light light = additionalLightData.gameObject.GetComponent<Light>();

                // Both of these positions are non-camera-relative.
                float distanceToCamera = (light.gameObject.transform.position - hdCamera.camera.transform.position).magnitude;
                float lightDistanceFade = HDUtils.ComputeLinearDistanceFade(distanceToCamera, additionalLightData.fadeDistance);

                bool contributesToLighting = ((additionalLightData.lightDimmer > 0) && (additionalLightData.affectDiffuse || additionalLightData.affectSpecular)) || (additionalLightData.volumetricDimmer > 0);
                contributesToLighting = contributesToLighting && (lightDistanceFade > 0);

                if (!contributesToLighting)
                    continue;

                lightData.lightLayers = additionalLightData.GetLightLayers();
                LightCategory lightCategory = LightCategory.Count;
                GPULightType gpuLightType = GPULightType.Point;
                LightVolumeType lightVolumeType = LightVolumeType.Count;
                HDRenderPipeline.EvaluateGPULightType(light.type, additionalLightData.lightTypeExtent, additionalLightData.spotLightShape, ref lightCategory, ref gpuLightType, ref lightVolumeType);

                lightData.lightType = gpuLightType;

                lightData.positionRWS = light.gameObject.transform.position - hdCamera.camera.transform.position;

                bool applyRangeAttenuation = additionalLightData.applyRangeAttenuation && (gpuLightType != GPULightType.ProjectorBox);

                lightData.range = light.range;

                if (applyRangeAttenuation)
                {
                    lightData.rangeAttenuationScale = 1.0f / (light.range * light.range);
                    lightData.rangeAttenuationBias = 1.0f;

                    if (lightData.lightType == GPULightType.Rectangle)
                    {
                        // Rect lights are currently a special case because they use the normalized
                        // [0, 1] attenuation range rather than the regular [0, r] one.
                        lightData.rangeAttenuationScale = 1.0f;
                    }
                }
                else // Don't apply any attenuation but do a 'step' at range
                {
                    // Solve f(x) = b - (a * x)^2 where x = (d/r)^2.
                    // f(0) = huge -> b = huge.
                    // f(1) = 0    -> huge - a^2 = 0 -> a = sqrt(huge).
                    const float hugeValue = 16777216.0f;
                    const float sqrtHuge = 4096.0f;
                    lightData.rangeAttenuationScale = sqrtHuge / (light.range * light.range);
                    lightData.rangeAttenuationBias = hugeValue;

                    if (lightData.lightType == GPULightType.Rectangle)
                    {
                        // Rect lights are currently a special case because they use the normalized
                        // [0, 1] attenuation range rather than the regular [0, r] one.
                        lightData.rangeAttenuationScale = sqrtHuge;
                    }
                }

                Color finalColor = Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
                GlobalIllumination.LinearColor converted = GlobalIllumination.LinearColor.Convert(light.color, light.intensity);
                finalColor.r *= converted.red;
                finalColor.g *= converted.green;
                finalColor.b *= converted.blue;
                lightData.color = new Vector3(finalColor.r, finalColor.g, finalColor.b) * light.intensity;

                lightData.forward = light.transform.forward;
                lightData.up = light.transform.up;
                lightData.right = light.transform.right;

                if (lightData.lightType == GPULightType.ProjectorBox)
                {
                    // Rescale for cookies and windowing.
                    lightData.right *= 2.0f / Mathf.Max(additionalLightData.shapeWidth, 0.001f);
                    lightData.up *= 2.0f / Mathf.Max(additionalLightData.shapeHeight, 0.001f);
                }
                else if (lightData.lightType == GPULightType.ProjectorPyramid)
                {
                    // Get width and height for the current frustum
                    var spotAngle = light.spotAngle;

                    float frustumWidth, frustumHeight;

                    if (additionalLightData.aspectRatio >= 1.0f)
                    {
                        frustumHeight = 2.0f * Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad);
                        frustumWidth = frustumHeight * additionalLightData.aspectRatio;
                    }
                    else
                    {
                        frustumWidth = 2.0f * Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad);
                        frustumHeight = frustumWidth / additionalLightData.aspectRatio;
                    }

                    // Rescale for cookies and windowing.
                    lightData.right *= 2.0f / frustumWidth;
                    lightData.up *= 2.0f / frustumHeight;
                }

                if (lightData.lightType == GPULightType.Spot)
                {
                    var spotAngle = light.spotAngle;

                    var innerConePercent = additionalLightData.GetInnerSpotPercent01();
                    var cosSpotOuterHalfAngle = Mathf.Clamp(Mathf.Cos(spotAngle * 0.5f * Mathf.Deg2Rad), 0.0f, 1.0f);
                    var sinSpotOuterHalfAngle = Mathf.Sqrt(1.0f - cosSpotOuterHalfAngle * cosSpotOuterHalfAngle);
                    var cosSpotInnerHalfAngle = Mathf.Clamp(Mathf.Cos(spotAngle * 0.5f * innerConePercent * Mathf.Deg2Rad), 0.0f, 1.0f); // inner cone

                    var val = Mathf.Max(0.0001f, (cosSpotInnerHalfAngle - cosSpotOuterHalfAngle));
                    lightData.angleScale = 1.0f / val;
                    lightData.angleOffset = -cosSpotOuterHalfAngle * lightData.angleScale;

                    // Rescale for cookies and windowing.
                    float cotOuterHalfAngle = cosSpotOuterHalfAngle / sinSpotOuterHalfAngle;
                    lightData.up *= cotOuterHalfAngle;
                    lightData.right *= cotOuterHalfAngle;
                }
                else
                {
                    // These are the neutral values allowing GetAngleAnttenuation in shader code to return 1.0
                    lightData.angleScale = 0.0f;
                    lightData.angleOffset = 1.0f;
                }

                if (lightData.lightType != GPULightType.Directional && lightData.lightType != GPULightType.ProjectorBox)
                {
                    // Store the squared radius of the light to simulate a fill light.
                    lightData.size = new Vector2(additionalLightData.shapeRadius * additionalLightData.shapeRadius, 0);
                }

                if (lightData.lightType == GPULightType.Rectangle || lightData.lightType == GPULightType.Tube)
                {
                    lightData.size = new Vector2(additionalLightData.shapeWidth, additionalLightData.shapeHeight);
                }

                lightData.lightDimmer = lightDistanceFade * (additionalLightData.lightDimmer);
                lightData.diffuseDimmer = lightDistanceFade * (additionalLightData.affectDiffuse ? additionalLightData.lightDimmer : 0);
                lightData.specularDimmer = lightDistanceFade * (additionalLightData.affectSpecular ? additionalLightData.lightDimmer * hdCamera.frameSettings.specularGlobalDimmer : 0);
                lightData.volumetricLightDimmer = lightDistanceFade * (additionalLightData.volumetricDimmer);

                lightData.cookieIndex = -1;
                lightData.shadowIndex = -1;

                if (light != null && light.cookie != null)
                {
                    // TODO: add texture atlas support for cookie textures.
                    switch (light.type)
                    {
                        case LightType.Spot:
                            lightData.cookieIndex = m_LightLoop.cookieTexArray.FetchSlice(cmd, light.cookie);
                            break;
                        case LightType.Point:
                            lightData.cookieIndex = m_LightLoop.cubeCookieTexArray.FetchSlice(cmd, light.cookie);
                            break;
                    }
                }
                else if (light.type == LightType.Spot && additionalLightData.spotLightShape != SpotLightShape.Cone)
                {
                    // Projectors lights must always have a cookie texture.
                    // As long as the cache is a texture array and not an atlas, the 4x4 white texture will be rescaled to 128
                    lightData.cookieIndex = m_LightLoop.cookieTexArray.FetchSlice(cmd, Texture2D.whiteTexture);
                }
                else if (lightData.lightType == GPULightType.Rectangle && additionalLightData.areaLightCookie != null)
                {
                    lightData.cookieIndex = m_LightLoop.areaLightCookieManager.FetchSlice(cmd, additionalLightData.areaLightCookie);
                }

                {
                    lightData.shadowDimmer = 1.0f;
                    lightData.volumetricShadowDimmer = 1.0f;
                }

                {
                    // fix up shadow information
                    lightData.shadowIndex = additionalLightData.shadowIndex;
                }

                // Value of max smoothness is from artists point of view, need to convert from perceptual smoothness to roughness
                lightData.minRoughness = (1.0f - additionalLightData.maxSmoothness) * (1.0f - additionalLightData.maxSmoothness);

                // No usage for the shadow masks
                lightData.shadowMaskSelector = Vector4.zero;
                {
                    // use -1 to say that we don't use shadow mask
                    lightData.shadowMaskSelector.x = -1.0f;
                    lightData.nonLightMappedOnly = 0;
                }

                // No contact shadows for us
                lightData.contactShadowIndex = -1;

                // Set the data for this light
                m_LightDataCPUArray[lightIdx]= lightData;
            }

            //Push the data to the GPU
            m_LightDataGPUArray.SetData(m_LightDataCPUArray);
        }

        void BuildEnvLightData(CommandBuffer cmd, HDCamera hdCamera, HDRayTracingLights lights)
        {
            int totalReflectionProbes = lights.reflectionProbeArray.Count;
            if (totalReflectionProbes == 0)
            {
                ResizeEnvLightDataBuffer(1);
                return;
            }

            // Also we need to build the light list data
            if (m_EnvLightDataCPUArray == null || m_EnvLightDataGPUArray == null || m_EnvLightDataGPUArray.count != totalReflectionProbes)
            {
                ResizeEnvLightDataBuffer(totalReflectionProbes);
            }

            // Make sure the Cpu list is empty
            m_EnvLightDataCPUArray.Clear();

            // Build the data for every light
            for (int lightIdx = 0; lightIdx < lights.reflectionProbeArray.Count; ++lightIdx)
            {
                HDProbe probeData = lights.reflectionProbeArray[lightIdx];
                var envLightData = new EnvLightData();
                m_RenderPipeline.GetEnvLightData(cmd, hdCamera, probeData, m_RenderPipeline.m_CurrentDebugDisplaySettings, ref envLightData);

                // We make the light position camera-relative as late as possible in order
                // to allow the preceding code to work with the absolute world space coordinates.
                Vector3 camPosWS = hdCamera.mainViewConstants.worldSpaceCameraPos;
                m_RenderPipeline.UpdateEnvLighCameraRelativetData(ref envLightData, camPosWS);

                m_EnvLightDataCPUArray.Add(envLightData);
            }

            // Push the data to the GPU
            m_EnvLightDataGPUArray.SetData(m_EnvLightDataCPUArray);
        }

        public void EvaluateClusterDebugView(CommandBuffer cmd, HDCamera hdCamera)
        {
            ComputeShader lightClusterDebugCS = m_RenderPipelineResources.shaders.lightClusterDebugCS;
            if (lightClusterDebugCS == null) return;

            // Bind the output texture
            CoreUtils.SetRenderTarget(cmd, m_DebugLightClusterTexture, m_RenderPipeline.sharedRTManager.GetDepthStencilBuffer(), clearFlag: ClearFlag.Color, clearColor: Color.black);

            // Grab the kernel
            int m_LightClusterDebugKernel = lightClusterDebugCS.FindKernel("DebugLightCluster");

            // Inject all the parameters to the debug compute
            cmd.SetComputeBufferParam(lightClusterDebugCS, m_LightClusterDebugKernel, _RaytracingLightCluster, m_LightCluster);
            cmd.SetComputeVectorParam(lightClusterDebugCS, _MinClusterPos, minClusterPos);
            cmd.SetComputeVectorParam(lightClusterDebugCS, _MaxClusterPos, maxClusterPos);
            cmd.SetComputeVectorParam(lightClusterDebugCS, _ClusterCellSize, clusterCellSize);
            cmd.SetComputeIntParam(lightClusterDebugCS, HDShaderIDs._LightPerCellCount, numLightsPerCell);
            cmd.SetComputeTextureParam(lightClusterDebugCS, m_LightClusterDebugKernel, HDShaderIDs._CameraDepthTexture, m_RenderPipeline.sharedRTManager.GetDepthStencilBuffer());

            // Target output texture
            cmd.SetComputeTextureParam(lightClusterDebugCS, m_LightClusterDebugKernel, _DebutLightClusterTexture, m_DebugLightClusterTexture);

            // Texture dimensions
            int texWidth = hdCamera.actualWidth;
            int texHeight = hdCamera.actualHeight;

            // Dispatch the compute
            int lightVolumesTileSize = 8;
            int numTilesX = (texWidth + (lightVolumesTileSize - 1)) / lightVolumesTileSize;
            int numTilesY = (texHeight + (lightVolumesTileSize - 1)) / lightVolumesTileSize;

            cmd.DispatchCompute(lightClusterDebugCS, m_LightClusterDebugKernel, numTilesX, numTilesY, 1);

            // Bind the parameters
            m_DebugMaterialProperties.SetBuffer(HDShaderIDs._RaytracingLightCluster, m_LightCluster);
            m_DebugMaterialProperties.SetVector(HDShaderIDs._MinClusterPos, minClusterPos);
            m_DebugMaterialProperties.SetVector(HDShaderIDs._MaxClusterPos, maxClusterPos);
            m_DebugMaterialProperties.SetVector(_ClusterCellSize, clusterCellSize);
            m_DebugMaterialProperties.SetInt(HDShaderIDs._LightPerCellCount, numLightsPerCell);
            m_DebugMaterialProperties.SetTexture(HDShaderIDs._CameraDepthTexture, m_RenderPipeline.sharedRTManager.GetDepthTexture());

            // Draw the faces
            cmd.DrawProcedural(Matrix4x4.identity, m_DebugMaterial, 1, MeshTopology.Lines, 48, 64 * 64 * 32, m_DebugMaterialProperties);
            cmd.DrawProcedural(Matrix4x4.identity, m_DebugMaterial, 0, MeshTopology.Triangles, 36, 64 * 64 * 32, m_DebugMaterialProperties);

            // Bind the result
            (RenderPipelineManager.currentPipeline as HDRenderPipeline).PushFullScreenDebugTexture(hdCamera, cmd, m_DebugLightClusterTexture, FullScreenDebugMode.LightCluster);
        }

        public ComputeBuffer GetCluster()
        {
            return m_LightCluster;
        }
        public ComputeBuffer GetLightDatas()
        {
            return m_LightDataGPUArray;
        }

        public Vector3 GetMinClusterPos()
        {
            return minClusterPos;
        }

        public Vector3 GetMaxClusterPos()
        {
            return maxClusterPos;
        }

        public Vector3 GetClusterCellSize()
        {
            return clusterCellSize;
        }

        public int GetPunctualLightCount()
        {
            return punctualLightCount;
        }

        public int GetAreaLightCount()
        {
            return areaLightCount;
        }

        public void EvaluateLightClusters(CommandBuffer cmd, HDCamera hdCamera, List<HDAdditionalLightData> lightArray)
        {
            // If there is no area light to process, nothing to do here
            if (lightArray.Count == 0)
                return;

        public void EvaluateLightClusters(CommandBuffer cmd, HDCamera hdCamera, HDRayTracingLights rayTracingLights)
        {
            // If there is no lights to process or no environment not the shader is missing
            if (rayTracingLights.lightCount == 0 || !m_RenderPipeline.GetRayTracingState())
            {
                InvalidateCluster();
                return;
            }

            ComputeShader lightClusterCS = m_RenderPipelineResources.shaders.lightClusterBuildCS;
            if (lightClusterCS == null) return;

            // Build the Light volumes
            BuildGPULightVolumes(lightArray);

            // Evaluate the volume of the cluster
            EvaluateClusterVolume(hdCamera);

            // Cull the lights within the evaluated cluster range
            CullLights(cmd);

            // Build the light Cluster
            BuildLightCluster(cmd);

            // Build the light data
            BuildLightData(cmd, hdCamera, rayTracingLights);

            // Build the light data
            BuildEnvLightData(cmd, hdCamera, rayTracingLights);
        }
    }
#endif
}
