Shader "GrandStrategy/Playable Map"
{
    Properties
    {
        _MapColours ("Map colours", 2D) = "white" {}
        _ShowPolitics ("Political overlays", Float) = 1
        _ProvinceIndices ("Province indices", 2D) = "black" {}
        _ProvinceCorners ("Province corner offsets", 2D) = "black" {}
        _ProvinceGrid ("Province grid dimensions", Vector) = (1, 1, 0.5, 0.5)
        _Selected ("Selected province", Vector) = (0, 0, 0, 0)
        _Ownership ("Province ownership", 2D) = "black" {}
        _OwnershipSize ("Ownership texture size", Vector) = (1, 1, 1, 1)
        _TribeColour ("Tribe colour", Color) = (0.3, 0.7, 0.9, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MapColours);
            SAMPLER(sampler_MapColours);
            TEXTURE2D(_ProvinceIndices);
            SAMPLER(sampler_ProvinceIndices);
            TEXTURE2D(_ProvinceCorners);
            SAMPLER(sampler_ProvinceCorners);
            TEXTURE2D(_Ownership);
            SAMPLER(sampler_Ownership);
            CBUFFER_START(UnityPerMaterial)
                float4 _Selected;
                float4 _ProvinceGrid;
                float4 _OwnershipSize;
                float4 _TribeColour;
                float _ShowPolitics;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            float Matches(float3 province, float4 target)
            {
                float3 difference = abs(province - target.xyz);
                return target.w * step(max(difference.x, max(difference.y, difference.z)), 0.5 / 255.0);
            }

            float2 ProvinceCorner(float2 corner)
            {
                float2 uv = (corner + 0.5) * _ProvinceGrid.zw;
                float2 packed = round(SAMPLE_TEXTURE2D_LOD(_ProvinceCorners, sampler_ProvinceCorners, uv, 0).rg * 255.0);
                // Signed bytes in units of 1/256 of a cell, matching ProvinceShape.
                return corner + (packed - 256.0 * step(128.0, packed)) / 256.0;
            }

            bool InsideProvinceEdge(float2 start, float2 end, float2 samplePosition)
            {
                float2 edge = end - start;
                float2 delta = samplePosition - start;
                return edge.x * delta.y - edge.y * delta.x >= -0.000001;
            }

            bool ContainsProvincePoint(float2 cell, float2 samplePosition)
            {
                if (any(cell < 0.0) || any(cell >= _ProvinceGrid.xy)) return false;
                float2 a = ProvinceCorner(cell);
                float2 b = ProvinceCorner(cell + float2(1.0, 0.0));
                float2 c = ProvinceCorner(cell + 1.0);
                float2 d = ProvinceCorner(cell + float2(0.0, 1.0));
                return InsideProvinceEdge(a, b, samplePosition) && InsideProvinceEdge(b, c, samplePosition)
                    && InsideProvinceEdge(c, d, samplePosition) && InsideProvinceEdge(d, a, samplePosition);
            }

            float4 ProvinceIndexAt(float2 cell)
            {
                return SAMPLE_TEXTURE2D_LOD(_ProvinceIndices, sampler_ProvinceIndices, (cell + 0.5) / _ProvinceGrid.xy, 0);
            }

            float3 ProvinceAt(float2 uv)
            {
                float2 samplePosition = saturate(uv) * _ProvinceGrid.xy;
                float2 cell = min(floor(samplePosition), _ProvinceGrid.xy - 1.0);
                float4 province = ProvinceIndexAt(cell);
                // Only cells touching a moved border corner need geometric classification.
                if (province.a < 0.5 || ContainsProvincePoint(cell, samplePosition)) return province.rgb;
                float2 side = step(0.5, samplePosition - cell) * 2.0 - 1.0;
                float2 acrossX = cell + float2(side.x, 0.0);
                float2 acrossY = cell + float2(0.0, side.y);
                if (ContainsProvincePoint(acrossX, samplePosition)) return ProvinceIndexAt(acrossX).rgb;
                if (ContainsProvincePoint(acrossY, samplePosition)) return ProvinceIndexAt(acrossY).rgb;
                if (ContainsProvincePoint(cell + side, samplePosition)) return ProvinceIndexAt(cell + side).rgb;
                return province.rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 map = SAMPLE_TEXTURE2D(_MapColours, sampler_MapColours, input.uv);
                float3 province = ProvinceAt(input.uv);
                float index = dot(round(province * 255.0), float3(1.0, 256.0, 65536.0));
                float row = floor(index * _OwnershipSize.z);
                float column = index - row * _OwnershipSize.x;
                float2 ownershipUV = (float2(column, row) + 0.5) * _OwnershipSize.zw;
                float owned = SAMPLE_TEXTURE2D(_Ownership, sampler_Ownership, ownershipUV).r;
                float3 colour = lerp(map.rgb, _TribeColour.rgb, owned * 0.65 * _ShowPolitics);
                colour = lerp(colour, float3(1.0, 0.78, 0.26), Matches(province, _Selected) * 0.30 * _ShowPolitics);
                float light = saturate(dot(normalize(input.normalWS), normalize(float3(-0.5, 1.0, 0.35))));
                colour *= lerp(0.58, 1.05, light);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
