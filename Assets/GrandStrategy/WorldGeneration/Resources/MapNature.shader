Shader "GrandStrategy/Map Nature"
{
    Properties
    {
        _Visibility ("Zoom visibility", Float) = 0
        _Capital ("Capital clearing", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Capital;
                float _Visibility;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 anchorOS : TEXCOORD0;
                float4 colour : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 colour : TEXCOORD0;
                float clearance : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 position = lerp(input.anchorOS, input.positionOS, _Visibility);
                output.positionCS = TransformObjectToHClip(position);
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                half light = saturate(dot(normal, normalize(float3(-0.54, 1.0, 0.42))));
                half3 colour = input.colour.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                colour = SRGBToLinear(colour);
                #endif
                half crownLight = lerp(0.73, 1.0, saturate((input.positionOS.y - input.anchorOS.y) * 750.0));
                output.colour = colour * lerp(0.52, 1.16, light) * crownLight;
                float2 delta = input.anchorOS.xz - _Capital.xy;
                output.clearance = dot(delta, delta) - _Capital.z * _Capital.z;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.clearance);
                return half4(input.colour, 1);
            }
            ENDHLSL
        }
    }
}
