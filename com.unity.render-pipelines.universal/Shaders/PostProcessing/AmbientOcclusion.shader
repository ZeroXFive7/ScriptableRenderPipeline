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
    TEXTURE2D_X_FLOAT(_CameraDepthTexture);
    TEXTURE2D(_CameraDepthNormalsTexture);

    float4 _MainTex_TexelSize;

    float4 _AOParams;
    float3 _AOColor;

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

    // Accessors for packed AO/normal buffer
    half4 PackAONormal(half ao, half3 n)
    {
        return half4(ao, n * 0.5 + 0.5);
    }

    half GetPackedAO(half4 p)
    {
        return p.r;
    }

    half3 GetPackedNormal(half4 p)
    {
        return p.gba * 2.0 - 1.0;
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
        float depth = LOAD_TEXTURE2D_X(_CameraDepthTexture, uv).x;
        depth = LinearEyeDepth(depth, _ZBufferParams);
        return depth * _ProjectionParams.z + CheckBounds(uv, depth);
    }

    float3 SampleNormal(float2 uv)
    {
        float4 cdn = SAMPLE_TEXTURE2D(_CameraDepthNormalsTexture, sampler_LinearClamp, uv);
        return cdn * float3(1.0, 1.0, -1.0);
    }

    float SampleDepthNormal(float2 uv, out float3 normal)
    {
        normal = SampleNormal(UnityStereoTransformScreenSpaceTex(uv));
        return SampleDepth(uv);
    }

    // Normal vector comparer (for geometry-aware weighting)
    half CompareNormal(half3 d1, half3 d2)
    {
        return smoothstep(kGeometryCoeff, 1.0, dot(d1, d2));
    }

    // Trigonometric function utility
    float2 CosSin(float theta)
    {
        float sn, cs;
        sincos(theta, sn, cs);
        return float2(cs, sn);
    }

    // Pseudo random number generator with 2D coordinates
    float UVRandom(float u, float v)
    {
        float f = dot(float2(12.9898, 78.233), float2(u, v));
        return frac(43758.5453 * sin(f));
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

    // Sample point picker
    float3 PickSamplePoint(float2 uv, float index)
    {
        // Uniformaly distributed points on a unit sphere
        // http://mathworld.wolfram.com/SpherePointPicking.html
#if defined(FIX_SAMPLING_PATTERN)
        float gn = GradientNoise(uv * DOWNSAMPLE);
        // FIXEME: This was added to avoid a NVIDIA driver issue.
        //                                   vvvvvvvvvvvv
        float u = frac(UVRandom(0.0, index + uv.x * 1e-10) + gn) * 2.0 - 1.0;
        float theta = (UVRandom(1.0, index + uv.x * 1e-10) + gn) * TWO_PI;
#else
        float u = UVRandom(uv.x + _Time.x, uv.y + index) * 2.0 - 1.0;
        float theta = UVRandom(-uv.x - _Time.x, uv.y + index) * TWO_PI;
#endif
        float3 v = float3(CosSin(theta) * sqrt(1.0 - u * u), u);
        // Make them distributed between [0, _Radius]
        float l = sqrt((index + 1.0) / SAMPLE_COUNT) * RADIUS;
        return v * l;
    }

    half4 FragObscurance(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.uv;

        // Parameters used in coordinate conversion
        float3x3 proj = (float3x3)unity_CameraProjection;
        float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
        float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);

        // View space normal and depth
        float3 norm_o;
        float depth_o = SampleDepthNormal(uv, norm_o);

        // Reconstruct the view-space position.
        float3 vpos_o = ReconstructViewPos(uv, depth_o, p11_22, p13_31);

        float ao = 0.0;

        for (int s = 0; s < int(SAMPLE_COUNT); s++)
        {
            // Sample point
#if defined(SHADER_API_D3D11)
            // This 'floor(1.0001 * s)' operation is needed to avoid a NVidia shader issue. This issue
            // is only observed on DX11.
            float3 v_s1 = PickSamplePoint(uv, floor(1.0001 * s));
#else
            float3 v_s1 = PickSamplePoint(uv, s);
#endif

            v_s1 = faceforward(v_s1, -norm_o, v_s1);
            float3 vpos_s1 = vpos_o + v_s1;

            // Reproject the sample point
            float3 spos_s1 = mul(proj, vpos_s1);
            float2 uv_s1_01 = (spos_s1.xy / CheckPerspective(vpos_s1.z) + 1.0) * 0.5;

            // Depth at the sample point
            float depth_s1 = SampleDepth(uv_s1_01);

            // Relative position of the sample point
            float3 vpos_s2 = ReconstructViewPos(uv_s1_01, depth_s1, p11_22, p13_31);
            float3 v_s2 = vpos_s2 - vpos_o;

            // Estimate the obscurance value
            float a1 = max(dot(v_s2, norm_o) - kBeta * depth_o, 0.0);
            float a2 = dot(v_s2, v_s2) + 0.00001;
            ao += a1 / a2;
        }

        ao *= RADIUS; // Intensity normalization

        // Apply other parameters.
        ao = PositivePow(ao * INTENSITY / SAMPLE_COUNT, kContrast);

        return PackAONormal(ao, norm_o);
    }

        half4 FragBlur(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    
        #if defined(BLUR_HORIZONTAL)
        // Horizontal pass: Always use 2 texels interval to match to
        // the dither pattern.
        float2 delta = float2(_MainTex_TexelSize.x * 2.0, 0.0);
    #else
        // Vertical pass: Apply _Downsample to match to the dither
        // pattern in the original occlusion buffer.
        float2 delta = float2(0.0, _MainTex_TexelSize.y / DOWNSAMPLE * 2.0);
    #endif
    
    #if defined(BLUR_HIGH_QUALITY)
    
        // High quality 7-tap Gaussian with adaptive sampling
    
        half4 p0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv);
        half4 p1a = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - delta);
        half4 p1b = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + delta);
        half4 p2a = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - delta * 2.0);
        half4 p2b = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + delta * 2.0);
        half4 p3a = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - delta * 3.2307692308);
        half4 p3b = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + delta * 3.2307692308);
    
    #if defined(BLUR_SAMPLE_CENTER_NORMAL)
        half3 n0 = SampleNormal(input.uv);
    #else
        half3 n0 = GetPackedNormal(p0);
    #endif
    
        half w0 = 0.37004405286;
        half w1a = CompareNormal(n0, GetPackedNormal(p1a)) * 0.31718061674;
        half w1b = CompareNormal(n0, GetPackedNormal(p1b)) * 0.31718061674;
        half w2a = CompareNormal(n0, GetPackedNormal(p2a)) * 0.19823788546;
        half w2b = CompareNormal(n0, GetPackedNormal(p2b)) * 0.19823788546;
        half w3a = CompareNormal(n0, GetPackedNormal(p3a)) * 0.11453744493;
        half w3b = CompareNormal(n0, GetPackedNormal(p3b)) * 0.11453744493;
    
        half s;
        s = GetPackedAO(p0) * w0;
        s += GetPackedAO(p1a) * w1a;
        s += GetPackedAO(p1b) * w1b;
        s += GetPackedAO(p2a) * w2a;
        s += GetPackedAO(p2b) * w2b;
        s += GetPackedAO(p3a) * w3a;
        s += GetPackedAO(p3b) * w3b;
    
        s /= w0 + w1a + w1b + w2a + w2b + w3a + w3b;
    
    #else
    
        // Fater 5-tap Gaussian with linear sampling
        half4 p0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv);
        half4 p1a = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - delta * 1.3846153846);
        half4 p1b = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + delta * 1.3846153846);
        half4 p2a = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - delta * 3.2307692308);
        half4 p2b = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + delta * 3.2307692308);
    
    #if defined(BLUR_SAMPLE_CENTER_NORMAL)
        half3 n0 = SampleNormal(input.uv);
    #else
        half3 n0 = GetPackedNormal(p0);
    #endif
    
        half w0 = 0.2270270270;
        half w1a = CompareNormal(n0, GetPackedNormal(p1a)) * 0.3162162162;
        half w1b = CompareNormal(n0, GetPackedNormal(p1b)) * 0.3162162162;
        half w2a = CompareNormal(n0, GetPackedNormal(p2a)) * 0.0702702703;
        half w2b = CompareNormal(n0, GetPackedNormal(p2b)) * 0.0702702703;
    
        half s;
        s = GetPackedAO(p0) * w0;
        s += GetPackedAO(p1a) * w1a;
        s += GetPackedAO(p1b) * w1b;
        s += GetPackedAO(p2a) * w2a;
        s += GetPackedAO(p2b) * w2b;
    
        s /= w0 + w1a + w1b + w2a + w2b;
    
    #endif
    
        return PackAONormal(s, n0);
    }

    half4 FragBlurH(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float texelSize = _MainTex_TexelSize.x * 2.0;

        // 9-tap gaussian blur on the downsampled source
        half3 c0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 4.0, 0.0));
        half3 c1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 3.0, 0.0));
        half3 c2 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 2.0, 0.0));
        half3 c3 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(texelSize * 1.0, 0.0));
        half3 c4 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv                               );
        half3 c5 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 1.0, 0.0));
        half3 c6 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 2.0, 0.0));
        half3 c7 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 3.0, 0.0));
        half3 c8 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(texelSize * 4.0, 0.0));

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
        half3 c0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(0.0, texelSize * 3.23076923));
        half3 c1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv - float2(0.0, texelSize * 1.38461538));
        half3 c2 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv                                      );
        half3 c3 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(0.0, texelSize * 1.38461538));
        half3 c4 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, input.uv + float2(0.0, texelSize * 3.23076923));

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
