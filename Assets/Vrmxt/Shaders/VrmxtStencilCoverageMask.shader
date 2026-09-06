Shader "Hidden/UniVRMXT/StencilCoverageMask"
{
    Properties
    {
        _StencilRef ("Stencil Reference", Range(0, 255)) = 1
        _StencilReadMask ("Stencil Read Mask", Range(0, 255)) = 255
        _StencilWriteMask ("Stencil Write Mask", Range(0, 255)) = 255
    }

    SubShader
    {
        Tags { "Queue" = "Geometry-1" "RenderType" = "Opaque" }

        Pass
        {
            Name "Coverage"
            ColorMask 0
            Cull Off
            ZTest Always
            ZWrite Off

            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilReadMask]
                WriteMask [_StencilWriteMask]
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                return fixed4(0.0, 0.0, 0.0, 0.0);
            }
            ENDCG
        }
    }
}
