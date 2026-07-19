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

    // URP-slot test material for VRMXT_materials_override (selected via unity variant=urp).
    // Same property slots as Built-in sample; fragment outputs sample × _Color.
    // No URP package includes. Use SRPDefaultUnlit so Scriptable RPs draw this CG pass
    // (UniversalForward without URP Core.hlsl pinks under Warudo URP).
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = ""
        }

        Pass
        {
            Name "VRMXTTestOverrideURPUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma shader_feature_local _USE_RIM_LIGHT
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _ShadeColor;
            sampler2D _ShadeTex;
            float4 _ShadeTex_ST;
            float _ShadingShiftFactor;
            sampler2D _ShadingShiftTex;
            float4 _ShadingShiftTex_ST;
            float _ShadingShiftTexScale;
            float _ShadingToonyFactor;
            float _GiEqualizationFactor;
            float _OutlineWidth;
            float _UseRimLight;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Keep binding targets live so Unity does not strip unused uniforms.
                float sink = _ShadeColor.r
                    + tex2D(_ShadeTex, i.uv).r
                    + _ShadingShiftFactor
                    + tex2D(_ShadingShiftTex, i.uv).r * _ShadingShiftTexScale
                    + _ShadingToonyFactor
                    + _GiEqualizationFactor
                    + _OutlineWidth
                    + _UseRimLight;
#if defined(_USE_RIM_LIGHT)
                sink += 0.0001;
#endif
                sink *= 0.0;

                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                return albedo + sink;
            }
            ENDCG
        }
    }

    FallBack Off
}
