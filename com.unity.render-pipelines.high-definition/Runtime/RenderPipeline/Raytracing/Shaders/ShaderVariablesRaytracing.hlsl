#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/HDRayTracingManager.cs.hlsl"

// The target acceleration acceleration structure should only be defined for non compute shaders
#ifndef SHADER_STAGE_COMPUTE
RaytracingAccelerationStructure         _RaytracingAccelerationStructure;
float                                   _RaytracingRayBias;
float                                   _RaytracingRayMaxLength;
int                                     _RaytracingNumSamples;
int                                     _RaytracingMaxRecursion;
float                                   _RaytracingIntensityClamp;
float                                   _RaytracingReflectionMaxDistance;
float                                   _RaytracingReflectionMinSmoothness;
int                                     _RaytracingFrameIndex;
float                                   _RaytracingPixelSpreadAngle;
int                                     _RayCountEnabled;
float                                   _RaytracingCameraNearPlane;
uint                                    _RaytracingDiffuseRay;
int                                     _RaytracingPreExposition;
RW_TEXTURE2D_X(uint4,                   _RayCountTexture);
