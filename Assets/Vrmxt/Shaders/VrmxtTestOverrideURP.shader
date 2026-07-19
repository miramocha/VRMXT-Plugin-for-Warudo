Shader "VRMXT/Samples/TestOverrideURP"
{
    Properties
    {
        // Visible unlit albedo: sample × _Color (default yellow tint).
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Main Color", Color) = (1, 1, 0, 1)

        // Binding targets (Unity profile / VRMC_materials_mtoon sources).
        _ShadeColor ("Shade Color", Color) = (0.8, 0.8, 0.8, 1)
        _ShadeTex ("Shade Multiply", 2D) = "white" {}
        _ShadingShiftFactor ("Shading Shift", Range(-1, 1)) = 0
        _ShadingShiftTex ("Shading Shift Map", 2D) = "black" {}
        _ShadingShiftTexScale ("Shading Shift Map Scale", Float) = 1
        _ShadingToonyFactor ("Shading Toony", Range(0, 1)) = 0.9
        _GiEqualizationFactor ("GI Equalization", Range(0, 1)) = 0.9

        // Unbound sample property + keyword for properties[].shaderFeature.
        _OutlineWidth ("Outline Width", Float) = 0.02
        [Toggle(_USE_RIM_LIGHT)] _UseRimLight ("Use Rim Light", Float) = 0
    }

    // URP test material for VRMXT_materials_override.
    // Same property slots as the Built-in sample; fragment outputs sample × _Color.
    SubShader
    {
        // Skip this SubShader when URP is not installed (e.g. Built-in-only projects).
        // Must be first inside SubShader/Pass — Shader-root PackageRequirements is a parse error.
        PackageRequirements
        {
            "com.unity.render-pipelines.universal": "12.0"
        }

        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VRMXTTestOverrideURPUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma shader_feature_local _USE_RIM_LIGHT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _ShadeColor;
                float4 _ShadeTex_ST;
                float4 _ShadingShiftTex_ST;
                float _ShadingShiftFactor;
                float _ShadingShiftTexScale;
                float _ShadingToonyFactor;
                float _GiEqualizationFactor;
                float _OutlineWidth;
                float _UseRimLight;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShadeTex);
            SAMPLER(sampler_ShadeTex);
            TEXTURE2D(_ShadingShiftTex);
            SAMPLER(sampler_ShadingShiftTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float sink = _ShadeColor.r
                    + SAMPLE_TEXTURE2D(_ShadeTex, sampler_ShadeTex, input.uv).r
                    + _ShadingShiftFactor
                    + SAMPLE_TEXTURE2D(_ShadingShiftTex, sampler_ShadingShiftTex, input.uv).r * _ShadingShiftTexScale
                    + _ShadingToonyFactor
                    + _GiEqualizationFactor
                    + _OutlineWidth
                    + _UseRimLight;
#if defined(_USE_RIM_LIGHT)
                sink += 0.0001;
#endif
                sink *= 0.0;

                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * (half4)_Color;
                return albedo + sink;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
