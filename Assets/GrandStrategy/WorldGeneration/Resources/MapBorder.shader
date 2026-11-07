Shader "GrandStrategy/Map Border"
{
    Properties
    {
        _BorderColour ("Border colour", Color) = (0.06, 0.08, 0.09, 1)
        _WidthPixels ("Width in pixels", Float) = 1.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BorderColour;
                float _WidthPixels;
            CBUFFER_END

            struct Attributes
            {
                float3 startOS : POSITION;
                float2 corner : TEXCOORD0;
                float3 endOS : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 segment : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 start = TransformObjectToHClip(input.startOS);
                float4 end = TransformObjectToHClip(input.endOS);
                float2 halfScreen = _ScaledScreenParams.xy * 0.5;
                float2 delta = (end.xy / end.w - start.xy / start.w) * halfScreen;
                float len = length(delta);
                float2 tangent = len > 0.001 ? delta / len : float2(1.0, 0.0);
                float2 normal = float2(-tangent.y, tangent.x);
                float expansion = _WidthPixels * 0.5 + 0.6;
                float cap = (input.corner.x * 2.0 - 1.0) * expansion;
                float across = input.corner.y * expansion;

                float4 position = lerp(start, end, input.corner.x);
                position.xy += (cap * tangent + across * normal) / halfScreen * position.w;
                output.positionCS = position;
                output.segment = float3(input.corner.x * len + cap, across, len);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float outside = max(0.0, max(-input.segment.x, input.segment.x - input.segment.z));
                float distanceFromLine = length(float2(outside, input.segment.y));
                float radius = _WidthPixels * 0.5;
                float alpha = 1.0 - smoothstep(radius - 0.5, radius + 0.5, distanceFromLine);
                return half4(_BorderColour.rgb, alpha * _BorderColour.a);
            }
            ENDHLSL
        }
    }
}
