Shader "Hidden/Universal Render Pipeline/Ambient Occlusion"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    // The constant below determines the contrast of occlusion. This allows
    // users to control over/under occlusion. At the moment, this is not exposed
    // to the editor because it's rarely useful.
    static const float kContrast = 0.6;

    // The constant below controls the geometry-awareness of the bilateral
    // filter. The higher value, the more sensitive it is.
    static const float kGeometryCoeff = 0.8;

    // The constants below are used in the AO estimator. Beta is mainly used
    // for suppressing self-shadowing noise, and Epsilon is used to prevent
    // calculation underflow. See the paper (Morgan 2011 http://goo.gl/2iz3P)
    // for further details of these constants.
    static const float kBeta = 0.002;

    TEXTURE2D_X(_MainTex);
    TEXTURE2D(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);
    TEXTURE2D(_CameraDepthNormalsTexture);
    TEXTURE2D(_AONoise);

    float4 _MainTex_TexelSize;

    float4 _AOParams;
    float3 _AOColor;
    float3 _AOSampleKernel[16];
    float2 _AORenderTargetSize;

    // Sample count
    #if !defined(SHADER_API_GLES)
        #define SAMPLE_COUNT _AOParams.w
    #else
    // GLES2: In many cases, dynamic looping is not supported.
        #define SAMPLE_COUNT 3
    #endif

    // Other parameters
    #define INTENSITY _AOParams.x
    #define RADIUS _AOParams.y
    #define DOWNSAMPLE _AOParams.z
    #define NOISE_SCALE _AORenderTargetSize / 4.0

    // Encoding/decoding [0..1) floats into 8 bit/channel RG. Note that 1.0 will not be encoded properly.
    inline float2 EncodeFloatRG(float v)
    {
        float2 kEncodeMul = float2(1.0, 255.0);
        float kEncodeBit = 1.0 / 255.0;
        float2 enc = kEncodeMul * v;
        enc = frac(enc);
        enc.x -= enc.y * kEncodeBit;
        return enc;
    }
    inline float DecodeFloatRG(float2 enc)
    {
        float2 kDecodeDot = float2(1.0, 1 / 255.0);
        return dot(enc, kDecodeDot);
    }

    // Encoding/decoding view space normals into 2D 0..1 vector
    inline float2 EncodeViewNormalStereo(float3 n)
    {
        float kScale = 1.7777;
        float2 enc;
        enc = n.xy / (n.z + 1);
        enc /= kScale;
        enc = enc * 0.5 + 0.5;
        return enc;
    }
    inline float3 DecodeViewNormalStereo(float4 enc4)
    {
        float kScale = 1.7777;
        float3 nn = enc4.xyz * float3(2 * kScale, 2 * kScale, 0) + float3(-kScale, -kScale, 1);
        float g = 2.0 / dot(nn.xyz, nn.xyz);
        float3 n;
        n.xy = g * nn.xy;

#if defined(UNITY_REVERSED_Z)
        n.z = 1 - g;
#else
        n.z = g - 1;
#endif
        return n;
    }

    inline float4 EncodeDepthNormal(float depth, float3 normal)
    {
        float4 enc;
        enc.xy = EncodeViewNormalStereo(normal);
        enc.zw = EncodeFloatRG(depth);
        return enc;
    }

    inline void DecodeDepthNormal(float4 enc, out float depth, out float3 normal)
    {
        depth = DecodeFloatRG(enc.zw);
        normal = DecodeViewNormalStereo(enc);
    }

    // Boundary check for depth sampler
    // (returns a very large value if it lies out of bounds)
    float CheckBounds(float2 uv, float d)
    {
        float ob = any(uv < 0) + any(uv > 1);
#if defined(UNITY_REVERSED_Z)
        ob += (d <= 0.00001);
#else
        ob += (d >= 0.99999);
#endif
        return ob * 1e8;
    }

    // Depth/normal sampling functions
    float SampleDepth(float2 uv)
    {
        float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv).x;
        return LinearEyeDepth(depth, _ZBufferParams) + CheckBounds(uv, depth);
    }

    float3 SampleNormal(float2 uv)
    {
        float4 encodedDepthNormal = SAMPLE_TEXTURE2D(_CameraDepthNormalsTexture, sampler_LinearClamp, uv);

        float depth;
        float3 normal;
        DecodeDepthNormal(encodedDepthNormal, depth, normal);
        return normal;
    }

    float SampleDepthNormal(float2 uv, out float3 normal)
    {
        normal = SampleNormal(uv);
        return SampleDepth(uv);
    }

    // Check if the camera is perspective.
    // (returns 1.0 when orthographic)
    float CheckPerspective(float x)
    {
        return lerp(x, 1.0, unity_OrthoParams.w);
    }

    // Reconstruct view-space position from UV and depth.
    // p11_22 = (unity_CameraProjection._11, unity_CameraProjection._22)
    // p13_31 = (unity_CameraProjection._13, unity_CameraProjection._23)
    float3 ReconstructViewPos(float2 uv, float depth, float2 p11_22, float2 p13_31)
    {
        return float3((uv * 2.0 - 1.0 - p13_31) / p11_22 * CheckPerspective(depth), depth);
    }

    float2 ReconstructImagePos(float3 posViewSpace)
    {
        float4 posClipSpace = mul(unity_CameraProjection, float4(posViewSpace, 1.0));
        float2 posImageSpace = posClipSpace.xy / posClipSpace.w;
        posImageSpace = posImageSpace * 0.5 + 0.5;

#if UNITY_UV_STARTS_AT_TOP
        posImageSpace = 1.0 - posImageSpace;
#endif
        return posImageSpace;
    }

    inline float GetObscurance(int index, float3x3 tbn, float3 basePosVS)
    {
        float3 sampleOffsetVS = mul(_AOSampleKernel[index], tbn) * RADIUS;
        float3 samplePosVS = basePosVS + sampleOffsetVS;
        float2 sampleUV = ReconstructImagePos(samplePosVS);
        float sampleDepth = SampleDepth(sampleUV);

        float rangeCheck = smoothstep(0.0, 1.0, RADIUS / abs(sampleDepth - basePosVS.z));
        float obscurance = step(sampleDepth, samplePosVS.z);
        return obscurance * rangeCheck;
    }

    half4 FragObscurance(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        // Parameters used in coordinate conversion.
        float3x3 proj = (float3x3)unity_CameraProjection;
        float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
        float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);

        // Retrieve world-space normal, depth, and view-space position at this fragment.
        float3 normal;
        float depth = SampleDepthNormal(input.uv, normal);
        float3 viewPos = ReconstructViewPos(input.uv, depth, p11_22, p13_31);

        // Next construct basis randomly rotated around normal.
        float2 random = SAMPLE_TEXTURE2D(_AONoise, sampler_LinearRepeat, input.uv * NOISE_SCALE).xy;
        random = random * 2.0 - 1.0;

        float3 random3D = float3(random.xy, 0);
        float3 tangent = normalize(random3D - normal * dot(random3D, normal));
        float3 bitangent = cross(normal, tangent);

        float3x3 tbn = float3x3(tangent, bitangent, normal);

        // Now iterate over kernel, comparing depth.
        float obscurance = 0.0;

        [unroll]
        for (int s = 0; s < int(SAMPLE_COUNT); s++)
        {
            obscurance += GetObscurance(s, tbn, viewPos);
        }

        float ao = 1.0 - (obscurance / (float)SAMPLE_COUNT);

        return half4(ao, ao, ao, 1.0);
    }

    half4 FragBlurH(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float texelSize = _MainTex_TexelSize.x * 2.0;

        // 9-tap gaussian blur on the downsampled source
        half3 c0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 4.0, 0.0)).xyz;
        half3 c1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 3.0, 0.0)).xyz;
        half3 c2 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 2.0, 0.0)).xyz;
        half3 c3 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 1.0, 0.0)).xyz;
        half3 c4 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv                               ).xyz;
        half3 c5 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 1.0, 0.0)).xyz;
        half3 c6 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 2.0, 0.0)).xyz;
        half3 c7 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 3.0, 0.0)).xyz;
        half3 c8 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 4.0, 0.0)).xyz;

        half3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
                    + c4 * 0.22702703
                    + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

        return half4(color, 1.0);
    }

    half4 FragBlurV(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float texelSize = _MainTex_TexelSize.y;

        // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
        half3 c0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(0.0, texelSize * 3.23076923)).xyz;
        half3 c1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(0.0, texelSize * 1.38461538)).xyz;
        half3 c2 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv                                      ).xyz;
        half3 c3 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(0.0, texelSize * 1.38461538)).xyz;
        half3 c4 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(0.0, texelSize * 3.23076923)).xyz;

        half3 color = c0 * 0.07027027 + c1 * 0.31621622
                    + c2 * 0.22702703
                    + c3 * 0.31621622 + c4 * 0.07027027;

        return half4(color, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "AO Obscurance"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragObscurance
            ENDHLSL
        }

        Pass
        {
            Name "AO Blur Horizontal"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBlurH
            ENDHLSL
        }

        Pass
        {
            Name "AO Blur Vertical"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBlurV
            ENDHLSL
        }
    }
}
