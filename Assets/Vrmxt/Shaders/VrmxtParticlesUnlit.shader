Shader "VRMXT/Particles Unlit"
{
    Properties
    {
        [MainTexture] _MainTex ("Albedo", 2D) = "white" {}
        _BaseMap ("Albedo (alias)", 2D) = "white" {}
        [MainColor] _Color ("Color", Color) = (1,1,1,1)
        _BaseColor ("Color (alias)", Color) = (1,1,1,1)
    }

    // First-party unlit transparent particle shader. No URP/HDRP package includes —
    // works under Built-in and URP/HDRP when those pipelines still draw ShaderLab passes.
    // Shipped so consumer builds keep a usable particle shader without Always Included lists.
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "RenderPipeline" = ""
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            Name "VRMXTParticlesUnlit"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _BaseMap;
            fixed4 _Color;
            fixed4 _BaseColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color * _BaseColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.texcoord) * i.color;
            }
            ENDCG
        }
    }

    FallBack Off
}
