Shader "GrandStrategy/Map Nature"
{
    Properties
    {
        _Visibility ("Zoom visibility", Float) = 0
        _Capital ("Capital clearing", Vector) = (0, 0, 0, 0)
        _TerrainShadow ("Terrain sun shadow", 2D) = "black" {}
        _MapSun ("Sun direction and inverse aspect", Vector) = (-1, 1, 1, 1)
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

            TEXTURE2D(_TerrainShadow);
            SAMPLER(sampler_TerrainShadow);
            CBUFFER_START(UnityPerMaterial)
                float4 _Capital;
                float4 _MapSun;
                float4 _TerrainShadow_TexelSize;
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
                float2 uv = input.anchorOS.xz * float2(_MapSun.w, 1.0) + 0.5;
                float2 texel = _TerrainShadow_TexelSize.xy;
                float2 shadowUV = uv * (1.0 - texel) + 0.5 * texel;
                float shade = SAMPLE_TEXTURE2D_LOD(_TerrainShadow, sampler_TerrainShadow, shadowUV, 0).r;
                half light = saturate(dot(normal, normalize(_MapSun.xyz))) * (1.0 - 0.89 * shade);
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
