Shader "GrandStrategy/Playable Map"
{
    Properties
    {
        _MapColours ("Base colours and land coverage", 2D) = "white" {}
        _LandTint ("Filtered land colours and coverage", 2D) = "black" {}
        _LandMaterialsLow ("Filtered wetland, desert, grass and temperate forest", 2D) = "black" {}
        _LandMaterialsHigh ("Filtered tropical forest, boreal forest, tundra and alpine", 2D) = "black" {}
        [NoScaleOffset] _GrassColour ("Short grass colour", 2D) = "gray" {}
        [NoScaleOffset] _GrassNormal ("Short grass normal", 2D) = "bump" {}
        [NoScaleOffset] _SparseGrassColour ("Sparse grass colour", 2D) = "gray" {}
        [NoScaleOffset] _SparseGrassNormal ("Sparse grass normal", 2D) = "bump" {}
        [NoScaleOffset] _LeafMudColour ("Leaf-strewn mud colour", 2D) = "gray" {}
        [NoScaleOffset] _LeafMudNormal ("Leaf-strewn mud normal", 2D) = "bump" {}
        [NoScaleOffset] _LeafLitterColour ("Forest leaf litter colour", 2D) = "gray" {}
        [NoScaleOffset] _LeafLitterNormal ("Forest leaf litter normal", 2D) = "bump" {}
        [NoScaleOffset] _DesertColour ("Desert Colour", 2D) = "gray" {}
        [NoScaleOffset] _DesertNormal ("Desert Normal", 2D) = "bump" {}
        [NoScaleOffset] _TropicalColour ("Tropical Colour", 2D) = "gray" {}
        [NoScaleOffset] _TropicalNormal ("Tropical Normal", 2D) = "bump" {}
        [NoScaleOffset] _BorealColour ("Boreal Colour", 2D) = "gray" {}
        [NoScaleOffset] _BorealNormal ("Boreal Normal", 2D) = "bump" {}
        [NoScaleOffset] _TundraColour ("Tundra Colour", 2D) = "gray" {}
        [NoScaleOffset] _TundraNormal ("Tundra Normal", 2D) = "bump" {}
        [NoScaleOffset] _AlpineColour ("Alpine Colour", 2D) = "gray" {}
        [NoScaleOffset] _AlpineNormal ("Alpine Normal", 2D) = "bump" {}
        [NoScaleOffset] _SnowColour ("Snow Colour", 2D) = "gray" {}
        [NoScaleOffset] _SnowNormal ("Snow Normal", 2D) = "bump" {}
        [NoScaleOffset] _ShallowsColour ("Shallows Colour", 2D) = "gray" {}
        [NoScaleOffset] _ShallowsNormal ("Shallows Normal", 2D) = "bump" {}
        [NoScaleOffset] _FreshwaterColour ("Freshwater Colour", 2D) = "gray" {}
        [NoScaleOffset] _FreshwaterNormal ("Freshwater Normal", 2D) = "bump" {}
        _GroundTileFrequency ("Ground tile frequency", Float) = 3
        _GroundGrid ("Ground grid and inverse aspect", Vector) = (1024, 512, 0.5, 0)
        _BiomeSurface ("Biome IDs, water beds and snow", 2D) = "black" {}
        _GroundPatterns ("Ground patterns", 2D) = "gray" {}
        _ForestCanopy ("Forest canopy and shadow", 2D) = "black" {}
        _TerrainShadow ("Terrain sun shadow", 2D) = "black" {}
        _MapSun ("Map sun direction", Vector) = (-1, 1, 1, 0)
        _GroundDetail ("Ground detail", Float) = 0
        _CapitalClearing ("Capital clearing", Vector) = (0, 0, 0, 0)
        _ShowPolitics ("Political overlays", Float) = 1
        _RealmFill ("Political fill opacity", Float) = 0.41
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            TEXTURE2D(_MapColours);
            SAMPLER(sampler_MapColours);
            TEXTURE2D(_LandTint);
            SAMPLER(sampler_LandTint);
            TEXTURE2D(_LandMaterialsLow);
            TEXTURE2D(_LandMaterialsHigh);
            TEXTURE2D(_GrassColour);
            TEXTURE2D(_GrassNormal);
            TEXTURE2D(_SparseGrassColour);
            TEXTURE2D(_SparseGrassNormal);
            TEXTURE2D(_LeafMudColour);
            TEXTURE2D(_LeafMudNormal);
            TEXTURE2D(_LeafLitterColour);
            TEXTURE2D(_LeafLitterNormal);
            TEXTURE2D(_DesertColour);
            TEXTURE2D(_DesertNormal);
            TEXTURE2D(_TropicalColour);
            TEXTURE2D(_TropicalNormal);
            TEXTURE2D(_BorealColour);
            TEXTURE2D(_BorealNormal);
            TEXTURE2D(_TundraColour);
            TEXTURE2D(_TundraNormal);
            TEXTURE2D(_AlpineColour);
            TEXTURE2D(_AlpineNormal);
            TEXTURE2D(_SnowColour);
            TEXTURE2D(_SnowNormal);
            TEXTURE2D(_ShallowsColour);
            TEXTURE2D(_ShallowsNormal);
            TEXTURE2D(_FreshwaterColour);
            TEXTURE2D(_FreshwaterNormal);
            TEXTURE2D(_BiomeSurface);
            SAMPLER(sampler_BiomeSurface);
            TEXTURE2D(_GroundPatterns);
            SAMPLER(sampler_GroundPatterns);
            TEXTURE2D(_ForestCanopy);
            SAMPLER(sampler_ForestCanopy);
            TEXTURE2D(_TerrainShadow);
            SAMPLER(sampler_TerrainShadow);
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
                float4 _CapitalClearing;
                float4 _GroundGrid;
                float4 _TerrainShadow_TexelSize;
                float4 _MapSun;
                float _ShowPolitics;
                float _RealmFill;
                float _GroundDetail;
                float _GroundTileFrequency;
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
                float2 groundPosition : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.groundPosition = input.positionOS.xz;
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

            float3 VariantWeights(float2 field)
            {
                float warm = smoothstep(0.29, 0.70, field.x);
                float dense = smoothstep(0.31, 0.72, field.y);
                return float3((1.0 - warm) * (1.0 - dense), warm * (1.0 - dense), dense);
            }

            struct GroundSample
            {
                float3 colour;
                float2 normalXY;
                float weight;
            };

            struct GroundCoordinates
            {
                float2 uv;
                float2 dx;
                float2 dy;
            };

            void AccumulateGround(TEXTURE2D_PARAM(colourMap, colourSampler),
                TEXTURE2D_PARAM(normalMap, normalSampler), GroundCoordinates ground, float frequency,
                float weight, inout GroundSample result)
            {
                UNITY_BRANCH
                if (weight > 0.002)
                {
                    float2 uv = ground.uv * frequency;
                    float2 dx = ground.dx * frequency;
                    float2 dy = ground.dy * frequency;
                    result.colour += SAMPLE_TEXTURE2D_GRAD(colourMap, colourSampler, uv, dx, dy).rgb * weight;
                    result.normalXY += UnpackNormal(SAMPLE_TEXTURE2D_GRAD(normalMap, normalSampler, uv, dx, dy)).xy * weight;
                    result.weight += weight;
                }
            }

            // All tile textures share the same repeat sampler.
            #define GROUND_LAYER(colourMap, normalMap, coords, frequency, weight, result) \
                AccumulateGround(TEXTURE2D_ARGS(colourMap, sampler_GroundPatterns), \
                    TEXTURE2D_ARGS(normalMap, sampler_GroundPatterns), coords, frequency, weight, result)

            float3 ShadeGround(Varyings input, float3 normal)
            {
                float4 map = SAMPLE_TEXTURE2D(_MapColours, sampler_MapColours, input.uv);
                float4 biomeSurface = SAMPLE_TEXTURE2D(_BiomeSurface, sampler_BiomeSurface, input.uv);
                float2 ground = input.groundPosition;
                float4 broad = SAMPLE_TEXTURE2D(_GroundPatterns, sampler_GroundPatterns, ground * 3.7);
                float4 patches = SAMPLE_TEXTURE2D(_GroundPatterns, sampler_GroundPatterns, ground * 17.0);
                // Warp the prefiltered visual fields. The actual terrain and province masks stay aligned.
                float2 warp = ((broad.rb - 0.5) * 0.054 + (patches.ag - 0.5) * 0.022)
                    * float2(_GroundGrid.z, 1.0);
                float2 materialUV = input.uv + warp;
                float4 palette = SAMPLE_TEXTURE2D(_LandTint, sampler_LandTint, materialUV);
                float4 low = SAMPLE_TEXTURE2D(_LandMaterialsLow, sampler_LandTint, materialUV);
                float4 high = SAMPLE_TEXTURE2D(_LandMaterialsHigh, sampler_LandTint, materialUV);
                float total = dot(low + high, float4(1.0, 1.0, 1.0, 1.0));
                float coverage = smoothstep(0.06, 0.30, min(palette.a, total));

                // Read the unwarped biome at a texel centre when the blurred field has little land coverage.
                float2 cell = min(floor(saturate(input.uv) * _GroundGrid.xy), _GroundGrid.xy - 1.0);
                float2 centre = (cell + 0.5) / _GroundGrid.xy;
                float biomeId = SAMPLE_TEXTURE2D_LOD(_BiomeSurface, sampler_BiomeSurface, centre, 0).r * 255.0;
                // Match the eight consecutive land IDs in BiomeKind and MapGroundBlend.
                float4 rawLow = 1.0 - step(0.5, abs(biomeId - float4(4.0, 5.0, 6.0, 7.0)));
                float4 rawHigh = 1.0 - step(0.5, abs(biomeId - float4(8.0, 9.0, 10.0, 11.0)));
                low = lerp(rawLow, low / max(total, 0.0001), coverage);
                high = lerp(rawHigh, high / max(total, 0.0001), coverage);
                float3 landColour = lerp(map.rgb, palette.rgb / max(palette.a, 0.0001), coverage);
                float wetlandWeight = low.x;
                float sandWeight = low.y;
                float plainsWeight = low.z;
                float forestWeight = low.w + high.x + high.y;
                float4 weights = float4(forestWeight + plainsWeight, high.z + high.w, sandWeight, wetlandWeight);
                float tone = dot(broad - 0.5, weights) * 0.40 + dot(patches - 0.5, weights) * 0.30;
                float3 land = landColour * (1.0 + tone);
                float sand = smoothstep(0.23, 0.77, broad.b * 0.58 + patches.b * 0.42) * sandWeight;
                land = lerp(land, float3(0.52, 0.36, 0.19), sand * 0.42);

                float3 plainsMix = VariantWeights(lerp(broad.rg, patches.rb, 0.40));
                float3 forestMix = VariantWeights(lerp(broad.ag, patches.gr, 0.44));
                float3 plainsTint = float3(0.96, 1.02, 0.89) * plainsMix.x
                    + float3(1.12, 1.02, 0.78) * plainsMix.y
                    + float3(1.01, 1.08, 0.93) * plainsMix.z;
                float3 forestTint = float3(0.90, 1.09, 0.84) * forestMix.x
                    + float3(1.04, 0.88, 0.75) * forestMix.y
                    + float3(0.94, 1.04, 0.93) * forestMix.z;
                land *= 1.0 + (plainsTint - 1.0) * plainsWeight + (forestTint - 1.0) * forestWeight;

                float exposed = smoothstep(0.04, 0.32, 1.0 - normal.y + (broad.g - 0.5) * 0.085) * map.a;
                float strata = SAMPLE_TEXTURE2D(_GroundPatterns, sampler_GroundPatterns, ground * float2(33.0, 10.0)).g;
                float stratum = smoothstep(0.28, 0.74, patches.g * 0.52 + strata * 0.31 + broad.g * 0.17);
                float3 rock = lerp(float3(0.115, 0.112, 0.101), float3(0.33, 0.31, 0.27), stratum);
                land = lerp(land, rock, exposed * 0.84);

                float snow = smoothstep(0.12, 0.90, biomeSurface.a + (patches.g - 0.48) * 0.60 * biomeSurface.a);
                snow *= lerp(1.0, 0.57, exposed);
                land = lerp(land, lerp(float3(0.47, 0.52, 0.53), float3(0.84, 0.86, 0.84), patches.g),
                    snow * 0.94);

                float2 groundDx = ddx(ground);
                float2 groundDy = ddy(ground);
                GroundCoordinates tile;
                tile.uv = ground * _GroundTileFrequency;
                tile.dx = groundDx * _GroundTileFrequency;
                tile.dy = groundDy * _GroundTileFrequency;
                GroundSample landDetail = (GroundSample)0;
                UNITY_BRANCH
                if (_GroundDetail > 0.0 && map.a > 0.0)
                {
                    float4 grain = SAMPLE_TEXTURE2D_GRAD(_GroundPatterns, sampler_GroundPatterns, ground * 76.0,
                        groundDx * 76.0, groundDy * 76.0);
                    land *= 1.0 + (grain.g - 0.5) * 0.16 * _GroundDetail;

                    float vegetated = plainsWeight + forestWeight;
                    float forestShare = forestWeight / max(vegetated, 0.0001);
                    float edge = 4.0 * forestShare * (1.0 - forestShare);
                    float sparse = saturate(plainsMix.y * 0.46 + edge * (0.27 + patches.r * 0.33));
                    float leafMud = saturate(forestMix.y * 0.40 + edge * (0.32 + patches.a * 0.36));

                    float rockCover = exposed * 0.84;
                    float baseWeight = (1.0 - snow) * (1.0 - rockCover);
                    low *= baseWeight;
                    high *= baseWeight;
                    float mudWeight = low.x + (low.w + high.x + high.y) * leafMud;
                    float rockWeight = high.w + rockCover * (1.0 - snow);
                    GROUND_LAYER(_GrassColour, _GrassNormal, tile, 34.0, low.z * (1.0 - sparse), landDetail);
                    GROUND_LAYER(_SparseGrassColour, _SparseGrassNormal, tile, 39.0, low.z * sparse, landDetail);
                    GROUND_LAYER(_LeafMudColour, _LeafMudNormal, tile, 37.0, mudWeight, landDetail);
                    GROUND_LAYER(_LeafLitterColour, _LeafLitterNormal, tile, 43.0, low.w * (1.0 - leafMud), landDetail);
                    GROUND_LAYER(_TropicalColour, _TropicalNormal, tile, 42.0, high.x * (1.0 - leafMud), landDetail);
                    GROUND_LAYER(_BorealColour, _BorealNormal, tile, 40.0, high.y * (1.0 - leafMud), landDetail);
                    GROUND_LAYER(_DesertColour, _DesertNormal, tile, 36.0, low.y, landDetail);
                    GROUND_LAYER(_TundraColour, _TundraNormal, tile, 41.0, high.z, landDetail);
                    GROUND_LAYER(_AlpineColour, _AlpineNormal, tile, 31.0, rockWeight, landDetail);
                    GROUND_LAYER(_SnowColour, _SnowNormal, tile, 35.0, snow, landDetail);
                    land += (landDetail.colour - land * landDetail.weight) * (_GroundDetail * 0.38);
                }

                float3 canopy = SAMPLE_TEXTURE2D(_ForestCanopy, sampler_ForestCanopy, input.uv).rgb;
                float clearing = _CapitalClearing.z > 0.0
                    ? smoothstep(_CapitalClearing.z * 0.7, _CapitalClearing.z, distance(ground, _CapitalClearing.xy))
                    : 1.0;
                float cover = smoothstep(0.04, 0.66, canopy.r) * map.a * clearing;
                float3 forest = lerp(float3(0.055, 0.077, 0.038), float3(0.18, 0.22, 0.102), canopy.g);
                forest = lerp(forest, float3(0.27, 0.34, 0.30), snow * 0.75);
                land = lerp(land, forest, cover * lerp(0.90, 0.76, _GroundDetail));

                float2 texel = _TerrainShadow_TexelSize.xy;
                float2 shadowUV = input.uv * (1.0 - texel) + 0.5 * texel;
                float terrainShade = SAMPLE_TEXTURE2D(_TerrainShadow, sampler_TerrainShadow, shadowUV).r;
                float treeShade = canopy.b * clearing * lerp(0.46, 0.82, _GroundDetail);
                float shade = max(terrainShade, treeShade);
                // Ground UV increases along world +X and +Z on the heightfield.
                float3 tangent = normalize(float3(normal.y, -normal.x, 0.0));
                float3 bitangent = cross(tangent, normal);
                float3 relief = (landDetail.normalXY.x * tangent + landDetail.normalXY.y * bitangent)
                    * (_GroundDetail * 0.45 * (1.0 - cover));
                float3 sun = normalize(_MapSun.xyz);
                float diffuse = saturate(dot(normalize(normal + relief), sun));
                land *= 0.55 + 0.66 * diffuse * (1.0 - shade * 0.89);
                float ripple = (patches.g - 0.5) * 0.13 + (broad.a - 0.5) * 0.11;
                float2 screenX = ddx(input.uv) * _GroundGrid.xy;
                float2 screenY = ddy(input.uv) * _GroundGrid.xy;
                float waterLod = log2(max(max(length(screenX), length(screenY)), max(1.0, _GroundGrid.y * 0.011)));
                float4 waterField = SAMPLE_TEXTURE2D_LOD(_MapColours, sampler_MapColours, input.uv, waterLod);
                float wideWater = 1.0 - waterField.a;
                float3 water = lerp(map.rgb, waterField.rgb, smoothstep(0.92, 0.999, wideWater)) * (1.0 + ripple);
                float ice = biomeSurface.a * lerp(0.65, 0.96, patches.g);
                UNITY_BRANCH
                if (_GroundDetail > 0.0 && map.a < 1.0 && biomeSurface.g + biomeSurface.b > 0.002)
                {
                    GroundSample bed = (GroundSample)0;
                    GROUND_LAYER(_ShallowsColour, _ShallowsNormal, tile, 32.0, biomeSurface.g, bed);
                    GROUND_LAYER(_FreshwaterColour, _FreshwaterNormal, tile, 38.0, biomeSurface.b, bed);
                    float bedTone = dot(bed.colour, float3(0.2126, 0.7152, 0.0722)) - bed.weight * 0.18;
                    water *= 1.0 + (bedTone * 0.65 + dot(bed.normalXY, sun.xz) * 0.12) * _GroundDetail;
                }
                water = lerp(water, float3(0.54, 0.66, 0.69), ice);
                // Water is drawn on the same mesh. Keep its surface dark and the banks soft.
                return lerp(water * (1.0 - 0.20 * terrainShade), land, map.a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 ground = ShadeGround(input, normalize(input.normalWS));
                if (_ShowPolitics <= 0.0) return half4(ground, 1);

                float3 province = ProvinceAt(input.uv);
                float index = dot(round(province * 255.0), float3(1.0, 256.0, 65536.0));
                float row = floor(index * _OwnershipSize.z);
                float column = index - row * _OwnershipSize.x;
                float2 ownershipUV = (float2(column, row) + 0.5) * _OwnershipSize.zw;
                float owned = SAMPLE_TEXTURE2D(_Ownership, sampler_Ownership, ownershipUV).r;
                float3 colour = lerp(ground, _TribeColour.rgb, owned * _RealmFill * _ShowPolitics);
                colour = lerp(colour, float3(1.0, 0.78, 0.26), Matches(province, _Selected) * 0.26 * _ShowPolitics);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
