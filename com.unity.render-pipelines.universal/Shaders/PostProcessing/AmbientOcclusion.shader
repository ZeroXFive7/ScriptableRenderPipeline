Shader "Hidden/Universal Render Pipeline/Ambient Occlusion"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
    }

    HLSLINCLUDE

    #pragma exclude_renderers psp2
    #pragma target 3.0

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
                #pragma fragment FragAO
                #pragma multi_compile _ APPLY_FORWARD_FOG
                #pragma multi_compile _ FOG_LINEAR FOG_EXP FOG_EXP2
                #define SOURCE_DEPTH
                #include "ScalableAO.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "AO Blur Horizontal"

            HLSLPROGRAM

                #pragma vertex Vert
                #pragma fragment FragBlur
                #define SOURCE_DEPTHNORMALS
                #define BLUR_HORIZONTAL
                #define BLUR_SAMPLE_CENTER_NORMAL
                #include "ScalableAO.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "AO Blur Vertical"

            HLSLPROGRAM

                #pragma vertex Vert
                #pragma fragment FragBlur
                #define BLUR_VERTICAL
                #include "ScalableAO.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "Final Upsample"

            HLSLPROGRAM

                #pragma vertex Vert
                #pragma fragment FragUpsample
                #include "ScalableAO.hlsl"

            ENDHLSL
        }
    }
}
