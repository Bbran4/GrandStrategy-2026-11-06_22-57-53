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
        [NoScaleOffset] _DenseLitterColour ("Dense leaf litter colour", 2D) = "gray" {}
        [NoScaleOffset] _DenseLitterNormal ("Dense leaf litter normal", 2D) = "bump" {}
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
        [NoScaleOffset] _MeadowColour ("Worn meadow colour", 2D) = "gray" {}
        [NoScaleOffset] _MeadowNormal ("Worn meadow normal", 2D) = "bump" {}
        [NoScaleOffset] _TundraSoilColour ("Tundra soil colour", 2D) = "gray" {}
        [NoScaleOffset] _TundraSoilNormal ("Tundra soil normal", 2D) = "bump" {}
        _GroundTileFrequency ("Ground tile frequency", Float) = 3
        [HideInInspector] _InverseReliefHeight ("Inverse maximum relief height", Float) = 1
        [HideInInspector] _CoastalWaterColour ("Coastal water colour", Vector) = (0.07, 0.18, 0.18, 1)
        _GroundGrid ("Ground grid and inverse aspect", Vector) = (1024, 512, 0.5, 0)
        [NoScaleOffset] _RiverFlow ("River direction and coverage", 2D) = "black" {}
        _BiomeSurface ("Biome IDs, ocean shoals, freshwater and snow", 2D) = "black" {}
        _GroundPatterns ("Ground patterns", 2D) = "gray" {}
        _ForestCanopy ("Forest canopy and shadow", 2D) = "black" {}
        _TerrainShadow ("Terrain sun shadow", 2D) = "black" {}
        _MapSun ("Map sun direction", Vector) = (-1, 1, 1, 0)
        _GroundDetail ("Ground detail", Float) = 0.25
        _CapitalClearing ("Capital clearing", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ShowBiomes ("Show biome colours", Float) = 0
        _ShowPolitics ("Political overlays", Float) = 1
        _ProvinceIndices ("Province indices", 2D) = "black" {}
        _ProvinceCorners ("Province corner offsets", 2D) = "black" {}
        _ProvinceGrid ("Province grid dimensions", Vector) = (1, 1, 0.5, 0.5)
        [HideInInspector] _OwnershipMask ("Active campaign ownership", 2D) = "black" {}
        [HideInInspector] _OwnershipGrid ("Ownership grid", Vector) = (1, 1, 0, 0)
        [HideInInspector] _OwnershipColour ("Ownership colour", Vector) = (0, 0, 0, 1)
        [HideInInspector] _ShowOwnership ("Show ownership", Float) = 0
        _Selected ("Selected province", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            // The first Play Mode frame needs the terrain pass instead of the Editor's placeholder.
            #pragma editor_sync_compilation
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
            TEXTURE2D(_DenseLitterColour);
            TEXTURE2D(_DenseLitterNormal);
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
            TEXTURE2D(_MeadowColour);
            TEXTURE2D(_MeadowNormal);
            TEXTURE2D(_TundraSoilColour);
            TEXTURE2D(_TundraSoilNormal);
            TEXTURE2D(_BiomeSurface);
            SAMPLER(sampler_BiomeSurface);
            TEXTURE2D(_RiverFlow);
            TEXTURE2D(_GroundPatterns);
            SAMPLER(sampler_GroundPatterns);
            TEXTURE2D(_ForestCanopy);
            SAMPLER(sampler_ForestCanopy);
            TEXTURE2D(_TerrainShadow);
            SAMPLER(sampler_TerrainShadow);
            TEXTURE2D(_ProvinceIndices);
            SAMPLER(sampler_ProvinceIndices);
            TEXTURE2D(_OwnershipMask);
            TEXTURE2D(_ProvinceCorners);
            SAMPLER(sampler_ProvinceCorners);
            CBUFFER_START(UnityPerMaterial)
                float4 _Selected;
                float4 _OwnershipGrid;
                float4 _OwnershipColour;
                float _ShowOwnership;
                float4 _ProvinceGrid;
                float4 _CapitalClearing;
                float4 _GroundGrid;
                float4 _CoastalWaterColour;
                float _InverseReliefHeight;
                float4 _TerrainShadow_TexelSize;
                float4 _MapSun;
                float _ShowPolitics;
                float _ShowBiomes;
                float4 _BiomeViewPalette[8];
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
                float3 groundPosition : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.groundPosition = input.positionOS;
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
                float2 tangent;
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
                    float2 normalXY = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(normalMap, normalSampler, uv, dx, dy)).xy;
                    float2 bitangent = float2(-ground.tangent.y, ground.tangent.x);
                    result.normalXY += (ground.tangent * normalXY.x + bitangent * normalXY.y) * weight;
                    result.weight += weight;
                }
            }

            // All tile textures share the same repeat sampler.
            #define GROUND_LAYER(colourMap, normalMap, coords, frequency, weight, result) \
                AccumulateGround(TEXTURE2D_ARGS(colourMap, sampler_GroundPatterns), \
                    TEXTURE2D_ARGS(normalMap, sampler_GroundPatterns), coords, frequency, weight, result)

            struct TerrainPatches
            {
                float2 origin;
                float2 corner;
                float3 weights;
                float strength;
            };

            TerrainPatches BuildTerrainPatches(float2 ground)
            {
                TerrainPatches patches = (TerrainPatches)0;
                patches.strength = smoothstep(0.52, 0.88, _GroundDetail);
                UNITY_BRANCH
                if (patches.strength <= 0.0) return patches;

                float2 grid = float2(ground.x + ground.y * 0.5, ground.y * 0.8660254) * 57.0;
                patches.origin = floor(grid);
                float2 f = frac(grid);
                if (f.x + f.y < 1.0)
                {
                    patches.corner = 0.0;
                    patches.weights = float3(1.0 - f.x - f.y, f.x, f.y);
                }
                else
                {
                    patches.corner = 1.0;
                    patches.weights = float3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x);
                }
                patches.weights = smoothstep(0.0, 1.0, patches.weights);
                patches.weights /= dot(patches.weights, float3(1.0, 1.0, 1.0));
                return patches;
            }

            float2 TerrainPatchOffset(float2 cell)
            {
                float3 p = frac(cell.xyx * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yzx + 33.33);
                return frac((p.xx + p.yz) * p.zy);
            }

            GroundCoordinates PatchCoordinates(GroundCoordinates ground, float frequency, float2 cell)
            {
                float2 offset = TerrainPatchOffset(cell);
                float turn = offset.x < 0.5 ? -1.0 : 1.0;
                // Rotate the matching normals and sample gradients with the texture.
                ground.uv = ground.uv * turn + offset / frequency;
                ground.dx *= turn;
                ground.dy *= turn;
                ground.tangent *= turn;
                return ground;
            }

            void AccumulateStochasticGround(TEXTURE2D_PARAM(colourMap, colourSampler),
                TEXTURE2D_PARAM(normalMap, normalSampler), GroundCoordinates ground, float frequency,
                float weight, TerrainPatches patches, inout GroundSample result)
            {
                AccumulateGround(TEXTURE2D_ARGS(colourMap, colourSampler), TEXTURE2D_ARGS(normalMap, normalSampler),
                    ground, frequency, weight * (1.0 - patches.strength), result);
                float varied = weight * patches.strength;
                UNITY_BRANCH
                if (varied > 0.002)
                {
                    AccumulateGround(TEXTURE2D_ARGS(colourMap, colourSampler), TEXTURE2D_ARGS(normalMap, normalSampler),
                        PatchCoordinates(ground, frequency, patches.origin + patches.corner), frequency,
                        varied * patches.weights.x, result);
                    AccumulateGround(TEXTURE2D_ARGS(colourMap, colourSampler), TEXTURE2D_ARGS(normalMap, normalSampler),
                        PatchCoordinates(ground, frequency, patches.origin + float2(1.0, 0.0)), frequency,
                        varied * patches.weights.y, result);
                    AccumulateGround(TEXTURE2D_ARGS(colourMap, colourSampler), TEXTURE2D_ARGS(normalMap, normalSampler),
                        PatchCoordinates(ground, frequency, patches.origin + float2(0.0, 1.0)), frequency,
                        varied * patches.weights.z, result);
                }
            }

            #define STOCHASTIC_LAYER(colourMap, normalMap, coords, frequency, weight, patches, result) \
                AccumulateStochasticGround(TEXTURE2D_ARGS(colourMap, sampler_GroundPatterns), \
                    TEXTURE2D_ARGS(normalMap, sampler_GroundPatterns), coords, frequency, weight, patches, result)

            // Paint the first overlay onto the base, then apply the second overlay on top.
            // The weights sum to one, so rules cannot change a biome's overall coverage.
            float3 PaintWeights(float first, float second)
            {
                first = saturate(first);
                second = saturate(second);
                return float3((1.0 - first) * (1.0 - second), first * (1.0 - second), second);
            }

            struct BiomePaint
            {
                float3 wetland;
                float3 desert;
                float3 plains;
                float3 temperate;
                float3 tropical;
                float3 boreal;
                float3 tundra;
                float3 alpine;
            };

            BiomePaint EvaluateBiomePaint(float slope, float elevation, float bank, float forestEdge,
                float4 broad, float4 patches, float3 plainsMix, float3 forestMix)
            {
                float slopeRock = smoothstep(0.025, 0.24, slope + (patches.g - 0.5) * 0.08);
                float upland = smoothstep(0.22, 0.76, elevation + (broad.g - 0.5) * 0.16);
                float bankMud = bank * (1.0 - slopeRock);
                float litter = smoothstep(0.25, 0.75, broad.a * 0.52 + patches.b * 0.48);
                float patchRock = smoothstep(0.29, 0.71, broad.g * 0.56 + patches.r * 0.44);

                BiomePaint paint;
                // Mud, grassy margins, pebbles at the water's edge.
                paint.wetland = PaintWeights((1.0 - bankMud) * (1.0 - slopeRock) * plainsMix.z * 0.65,
                    bank * patchRock * 0.43);
                // Sand, firm soil, stony slopes.
                paint.desert = PaintWeights(patchRock * (0.24 + 0.42 * slopeRock), slopeRock * upland * 0.46);
                // Short grass, meadow, sparse grass on slopes and at woodland edges.
                paint.plains = PaintWeights(plainsMix.z * (1.0 - slopeRock) * 0.78,
                    plainsMix.y * 0.40 + slopeRock * 0.32 + forestEdge * (0.27 + patches.r * 0.33));
                // Leaf litter, dense litter, muddy patches.
                paint.temperate = PaintWeights(litter * (1.0 - slopeRock * 0.30),
                    forestMix.y * 0.30 + bankMud * 0.26 + forestEdge * (0.32 + patches.a * 0.36));
                // Dense litter, upland litter, low and sheltered mud.
                paint.tropical = PaintWeights(upland * litter * 0.75,
                    (1.0 - upland) * forestMix.y * 0.40 + bankMud * 0.38 + forestEdge * 0.28);
                // Dark earth, dense litter on sheltered ground, mud near water.
                paint.boreal = PaintWeights(litter * (1.0 - slopeRock * 0.7) * 0.75,
                    forestMix.y * 0.27 + bankMud * 0.40 + forestEdge * 0.24);
                // Stone, grey soil, sparse grass on lower and gentler ground.
                paint.tundra = PaintWeights(patchRock * (1.0 - slopeRock * 0.65),
                    (1.0 - upland) * (1.0 - slopeRock) * plainsMix.z * 0.64);
                // Bedrock, scree below high ridges, soil on gentle shelves.
                paint.alpine = PaintWeights(patchRock * (1.0 - upland * 0.45),
                    (1.0 - slopeRock) * (1.0 - upland) * litter * 0.48);
                return paint;
            }

            float3 ShadeOcean(float3 water, float3 position, float2 groundDx, float2 groundDy,
                float3 sun, float shoal, float landCoverage, float2 surfaceNoise)
            {
                float3 view = GetWorldSpaceNormalizeViewDir(TransformObjectToWorld(position));
                float grazing = 1.0 - saturate(view.y);
                water *= 1.0 + (surfaceNoise.x - 0.5) * 0.052;
                water += float3(0.071, 0.111, 0.140) * grazing * grazing * 0.48;

                float footprint = max(length(groundDx), length(groundDy));
                float detail = smoothstep(0.25, 0.94, _GroundDetail) * (1.0 - smoothstep(0.055, 0.31, footprint * 31.0));
                UNITY_BRANCH
                if (detail > 0.001)
                {
                    // Scroll two bands of the existing periodic noise as tangent-space slopes.
                    float time = _Time.y;
                    float2 ground = position.xz;
                    float2 swellUV = ground * 9.0 + time * float2(-0.052, 0.027);
                    float2 rippleUV = ground * 31.0 + time * float2(0.081, 0.046);
                    float2 swell = SAMPLE_TEXTURE2D_GRAD(_GroundPatterns, sampler_GroundPatterns,
                        swellUV, groundDx * 9.0, groundDy * 9.0).rg - 0.5;
                    float2 ripple = SAMPLE_TEXTURE2D_GRAD(_GroundPatterns, sampler_GroundPatterns,
                        rippleUV, groundDx * 31.0, groundDy * 31.0).ba - 0.5;
                    float2 slope = (swell * 0.33 + ripple * 0.19) * detail;
                    float3 waveNormal = normalize(float3(slope.x, 1.0, slope.y));
                    float reflectedLight = dot(waveNormal - float3(0.0, 1.0, 0.0), sun);
                    water *= 1.0 + reflectedLight * 0.45;

                    float3 halfVector = normalize(sun + view);
                    float glint = smoothstep(0.79, 0.99, dot(waveNormal, halfVector));
                    water += float3(0.071, 0.104, 0.119) * (glint * detail * 0.21);

                    // A broken, low-contrast crest in shallow water close to the actual shoreline.
                    float coast = smoothstep(0.035, 0.17, landCoverage)
                        * (1.0 - smoothstep(0.35, 0.64, landCoverage)) * smoothstep(0.38, 0.90, shoal);
                    float crest = 0.5 + 0.5 * sin(landCoverage * 14.0 + time * 0.86 + surfaceNoise.y * 4.0);
                    float foam = coast * smoothstep(0.56, 0.98, crest) * (0.65 + swell.y) * detail;
                    water += float3(0.20, 0.25, 0.24) * (foam * 0.19);
                }
                return water;
            }

            float4 FilterWaterline(TEXTURE2D_PARAM(field, fieldSampler), float2 uv, float2 texel,
                float lod, float4 centre)
            {
                float4 filtered = SAMPLE_TEXTURE2D_LOD(field, fieldSampler, uv + float2(texel.x, 0.0), lod)
                    + SAMPLE_TEXTURE2D_LOD(field, fieldSampler, uv - float2(texel.x, 0.0), lod)
                    + SAMPLE_TEXTURE2D_LOD(field, fieldSampler, uv + float2(0.0, texel.y), lod)
                    + SAMPLE_TEXTURE2D_LOD(field, fieldSampler, uv - float2(0.0, texel.y), lod);
                return centre * 0.5 + filtered * 0.125;
            }

            float3 ShadeGround(Varyings input, float3 normal, float4 map)
            {
                float4 biomeSurface = SAMPLE_TEXTURE2D(_BiomeSurface, sampler_BiomeSurface, input.uv);
                float3 position = input.groundPosition;
                float3 positionDx = ddx(position);
                float3 positionDy = ddy(position);
                float2 ground = position.xz;
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
                UNITY_BRANCH
                if (_ShowBiomes > 0.5)
                {
                    float3 biomeColour = 0.0;
                    [unroll]
                    for (int i = 0; i < 4; i++)
                        biomeColour += low[i] * _BiomeViewPalette[i].rgb + high[i] * _BiomeViewPalette[i + 4].rgb;
                    float weight = dot(low + high, float4(1.0, 1.0, 1.0, 1.0));
                    float3 painted = weight > 0.001 ? biomeColour / weight : landColour;
                    return lerp(map.rgb, painted * lerp(0.72, 1.0, saturate(normal.y)), map.a);
                }
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

                float snowOrIce = (biomeSurface.a * 255.0 - 128.0) / 127.0;
                float snowCoverage = max(snowOrIce, 0.0);
                float snow = smoothstep(0.12, 0.90, snowCoverage * (1.0 + (patches.g - 0.48) * 0.60));
                snow *= lerp(1.0, 0.57, exposed);
                land = lerp(land, lerp(float3(0.47, 0.52, 0.53), float3(0.84, 0.86, 0.84), patches.g),
                    snow * 0.94);

                float2 groundDx = positionDx.xz;
                float2 groundDy = positionDy.xz;
                float2 screenX = ddx(input.uv) * _GroundGrid.xy;
                float2 screenY = ddy(input.uv) * _GroundGrid.xy;
                float pixelFootprint = max(length(screenX), length(screenY));
                float coastLod = log2(max(pixelFootprint, max(1.0, _GroundGrid.y * 0.011)));
                float4 waterField = SAMPLE_TEXTURE2D_LOD(_MapColours, sampler_MapColours, input.uv, coastLod);
                float2 nearbyWater = SAMPLE_TEXTURE2D_LOD(_BiomeSurface, sampler_BiomeSurface, input.uv, coastLod).gb;
                float oceanEdge = smoothstep(0.012, 0.17, nearbyWater.x);
                float edgeLod = log2(max(pixelFootprint, 1.0));
                float2 shoreTexel = 0.75 / _GroundGrid.xy;
                float coast = oceanEdge * (1.0 - smoothstep(0.015, 0.16, nearbyWater.y))
                    * smoothstep(0.04, 0.26, waterField.a) * (1.0 - smoothstep(0.80, 0.98, waterField.a));
                float coastalLand = map.a;
                UNITY_BRANCH
                if (coast > 0.001)
                {
                    float filteredLand = FilterWaterline(TEXTURE2D_ARGS(_MapColours, sampler_MapColours),
                        input.uv, shoreTexel * 1.6, edgeLod, map).a;
                    coastalLand = lerp(map.a, smoothstep(0.08, 0.92, filteredLand), coast * 0.92);
                }
                float rawFreshwater = biomeSurface.b;
                float softFreshwater = rawFreshwater;
                float riverBank = 0.0;
                UNITY_BRANCH
                if (nearbyWater.y > 0.002 || rawFreshwater > 0.0)
                {
                    float filtered = FilterWaterline(TEXTURE2D_ARGS(_BiomeSurface, sampler_BiomeSurface),
                        input.uv, shoreTexel, edgeLod, biomeSurface).b;
                    // Round raster corners without erasing streams that are only one cell wide.
                    softFreshwater = lerp(rawFreshwater, smoothstep(0.09, 0.74, filtered), 0.80);

                    float4 nearbyFlow = SAMPLE_TEXTURE2D_LOD(_RiverFlow, sampler_BiomeSurface, input.uv, max(1.35, edgeLod));
                    float navigable = min(nearbyFlow.a, nearbyFlow.b);
                    riverBank = smoothstep(0.035, 0.35, navigable)
                        * (1.0 - smoothstep(0.025, 0.23, 1.0 - normal.y));
                }
                float coastElevation = saturate(position.y * _InverseReliefHeight);
                float beach = oceanEdge * (1.0 - smoothstep(0.018, 0.075, coastElevation))
                    * (1.0 - smoothstep(0.035, 0.23, 1.0 - normal.y))
                    * (1.0 - smoothstep(0.10, 0.66, high.z));
                beach = max(beach, riverBank * 0.72) * lerp(0.72, 1.0, patches.r);
                land = lerp(land, float3(0.45, 0.35, 0.23), beach * (1.0 - snow) * 0.80);
                GroundCoordinates tile;
                tile.uv = ground * _GroundTileFrequency;
                tile.dx = groundDx * _GroundTileFrequency;
                tile.dy = groundDy * _GroundTileFrequency;
                tile.tangent = float2(1.0, 0.0);

                // Break up the temperate litter tiles without shifting biome boundaries.
                float2 litterUV = tile.uv + (broad.rb - 0.5) * 0.012 + (patches.ag - 0.5) * 0.004;
                const float2x2 litterRotation = float2x2(0.8, 0.6, -0.6, 0.8);
                GroundCoordinates litter;
                litter.uv = mul(litterRotation, litterUV) + float2(0.37, 0.61);
                litter.dx = ddx(litter.uv);
                litter.dy = ddy(litter.uv);
                litter.tangent = litterRotation[0];

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
                    float bank = 1.0 - smoothstep(0.52, 0.98, waterField.a);
                    float elevation = saturate(position.y * _InverseReliefHeight);
                    BiomePaint paint = EvaluateBiomePaint(1.0 - normal.y, elevation, bank, edge,
                        broad, patches, plainsMix, forestMix);

                    float rockCover = exposed * 0.84;
                    float baseWeight = (1.0 - snow) * (1.0 - beach) * (1.0 - rockCover);
                    low *= baseWeight;
                    high *= baseWeight;
                    float rockWeight = high.w * paint.alpine.x + rockCover * (1.0 - snow) * (1.0 - beach);
                    float sparseWeight = low.x * paint.wetland.y + low.z * paint.plains.z + high.z * paint.tundra.z;
                    float mudWeight = low.x * paint.wetland.x + low.w * paint.temperate.z
                        + high.x * paint.tropical.z + high.y * paint.boreal.z;
                    float litterWeight = low.w * paint.temperate.x + high.x * paint.tropical.y;
                    float denseWeight = low.w * paint.temperate.y + high.x * paint.tropical.x + high.y * paint.boreal.y;
                    float stoneWeight = low.y * paint.desert.z + high.z * paint.tundra.x + high.w * paint.alpine.y;
                    float soilWeight = low.y * paint.desert.y + high.z * paint.tundra.y + high.w * paint.alpine.z;

                    TerrainPatches tiling = BuildTerrainPatches(ground);
                    STOCHASTIC_LAYER(_GrassColour, _GrassNormal, tile, 34.0, low.z * paint.plains.x, tiling, landDetail);
                    STOCHASTIC_LAYER(_MeadowColour, _MeadowNormal, tile, 32.0, low.z * paint.plains.y, tiling, landDetail);
                    STOCHASTIC_LAYER(_SparseGrassColour, _SparseGrassNormal, tile, 39.0, sparseWeight, tiling, landDetail);
                    GROUND_LAYER(_LeafMudColour, _LeafMudNormal, tile, 37.0, mudWeight, landDetail);
                    STOCHASTIC_LAYER(_LeafLitterColour, _LeafLitterNormal, litter, 43.0, litterWeight, tiling, landDetail);
                    STOCHASTIC_LAYER(_DenseLitterColour, _DenseLitterNormal, tile, 42.0, denseWeight, tiling, landDetail);
                    GROUND_LAYER(_BorealColour, _BorealNormal, tile, 40.0, high.y * paint.boreal.x, landDetail);
                    GROUND_LAYER(_DesertColour, _DesertNormal, tile, 36.0, low.y * paint.desert.x, landDetail);
                    GROUND_LAYER(_TundraColour, _TundraNormal, tile, 41.0, stoneWeight, landDetail);
                    GROUND_LAYER(_TundraSoilColour, _TundraSoilNormal, tile, 36.0, soilWeight, landDetail);
                    GROUND_LAYER(_FreshwaterColour, _FreshwaterNormal, tile, 26.0, low.x * paint.wetland.z, landDetail);
                    GROUND_LAYER(_ShallowsColour, _ShallowsNormal, tile, 32.0, beach * (1.0 - snow), landDetail);
                    // Steep banks need vertical texture projection. Keep the established top projection inland.
                    float cliff = smoothstep(0.16, 0.46, 1.0 - normal.y)
                        * (1.0 - smoothstep(0.55, 0.97, waterField.a));
                    float cliffWeight = rockWeight * cliff;
                    GROUND_LAYER(_AlpineColour, _AlpineNormal, tile, 31.0, rockWeight * (1.0 - cliff), landDetail);
                    UNITY_BRANCH
                    if (cliffWeight > 0.002)
                    {
                        float2 side = abs(normal.xz);
                        side *= side;
                        side /= max(side.x + side.y, 0.0001);
                        float frequency = _GroundTileFrequency * 31.0;
                        float3 xFace = SAMPLE_TEXTURE2D_GRAD(_AlpineColour, sampler_GroundPatterns,
                            position.zy * frequency, positionDx.zy * frequency, positionDy.zy * frequency).rgb;
                        float3 zFace = SAMPLE_TEXTURE2D_GRAD(_AlpineColour, sampler_GroundPatterns,
                            position.xy * frequency, positionDx.xy * frequency, positionDy.xy * frequency).rgb;
                        landDetail.colour += (xFace * side.x + zFace * side.y) * cliffWeight;
                        landDetail.weight += cliffWeight;
                    }
                    GROUND_LAYER(_SnowColour, _SnowNormal, tile, 35.0, snow, landDetail);
                    land += (landDetail.colour - land * landDetail.weight) * (_GroundDetail * 0.50);
                }

                float3 canopy = SAMPLE_TEXTURE2D(_ForestCanopy, sampler_ForestCanopy, input.uv).rgb;
                float clearing = _CapitalClearing.z > 0.0
                    ? smoothstep(_CapitalClearing.z * 0.7, _CapitalClearing.z, distance(ground, _CapitalClearing.xy))
                    : 1.0;
                float cover = smoothstep(0.04, 0.66, canopy.r) * map.a * clearing * (1.0 - beach);
                float3 forest = lerp(float3(0.055, 0.077, 0.038), float3(0.18, 0.22, 0.102), canopy.g);
                forest = lerp(forest, float3(0.27, 0.34, 0.30), snow * 0.75);
                // Let the textured floor show through the canopy colour at closer zooms.
                land = lerp(land, forest, cover * lerp(0.90, 0.56, _GroundDetail));

                float2 texel = _TerrainShadow_TexelSize.xy;
                float2 shadowUV = input.uv * (1.0 - texel) + 0.5 * texel;
                float terrainShade = SAMPLE_TEXTURE2D(_TerrainShadow, sampler_TerrainShadow, shadowUV).r;
                float treeShade = canopy.b * clearing * lerp(0.46, 0.82, _GroundDetail);
                float shade = max(terrainShade, treeShade);
                // Ground UV increases along world +X and +Z on the heightfield.
                float3 tangent = normalize(float3(normal.y, -normal.x, 0.0));
                float3 bitangent = cross(tangent, normal);
                float3 relief = (landDetail.normalXY.x * tangent + landDetail.normalXY.y * bitangent)
                    * (smoothstep(0.25, 1.0, _GroundDetail) * 0.45 * (1.0 - cover));
                float3 sun = normalize(_MapSun.xyz);
                float diffuse = saturate(dot(normalize(normal + relief), sun));
                land *= 0.55 + 0.66 * diffuse * (1.0 - shade * 0.89);
                UNITY_BRANCH
                if (coastalLand >= 0.999 && map.a >= 0.999 && softFreshwater <= 0.001) return land;

                float wideWater = 1.0 - waterField.a;
                float3 water = lerp(map.rgb, waterField.rgb, smoothstep(0.92, 0.999, wideWater));
                water = lerp(water, _CoastalWaterColour.rgb, coast * smoothstep(0.04, 0.85, map.a));
                float waterCoverage = max(1.0 - map.a, 0.0001);
                float shoal = saturate(biomeSurface.g / waterCoverage);
                float freshwater = rawFreshwater > 0.0001 ? saturate(rawFreshwater / waterCoverage)
                    : (map.a >= 0.999 && softFreshwater > 0.001 ? 1.0 : 0.0);
                water = lerp(water, float3(0.065, 0.125, 0.153),
                    freshwater * (1.0 - smoothstep(0.0, 0.66, rawFreshwater)) * 0.68);
                float3 flow = SAMPLE_TEXTURE2D_GRAD(_RiverFlow, sampler_BiomeSurface, input.uv,
                    screenX / _GroundGrid.xy, screenY / _GroundGrid.xy).rgb;
                float channel = min(freshwater, flow.b / waterCoverage);
                float lake = freshwater - channel;
                float3 channelTint = float3(0.058, 0.128, 0.192);
                water = lerp(water, channelTint, channel * 0.78);
                float ice = min(lake, max(-snowOrIce, 0.0) / waterCoverage) * lerp(0.65, 0.96, patches.g);
                UNITY_BRANCH
                if (_GroundDetail > 0.0 && shoal + freshwater > 0.002)
                {
                    GroundSample bed = (GroundSample)0;
                    GROUND_LAYER(_ShallowsColour, _ShallowsNormal, tile, 32.0, shoal + channel, bed);
                    GROUND_LAYER(_FreshwaterColour, _FreshwaterNormal, tile, 26.0, lake, bed);
                    float bedTone = dot(bed.colour, float3(0.2126, 0.7152, 0.0722)) - bed.weight * 0.18;
                    float3 submergedSand = lerp(channelTint, bed.colour / max(bed.weight, 0.001), 0.38);
                    water = lerp(water, submergedSand, channel * 0.90 * _GroundDetail);
                    water *= 1.0 + (bedTone * 0.65 + dot(bed.normalXY, sun.xz) * 0.12) * _GroundDetail;
                }

                float3 waterDelta = 0.0;
                UNITY_BRANCH
                if (freshwater < 0.999)
                {
                    float3 ocean = ShadeOcean(water, position, groundDx, groundDy, sun, shoal, waterField.a, broad.gb);
                    waterDelta += (ocean - water) * (1.0 - freshwater);
                }
                UNITY_BRANCH
                if (channel > 0.001)
                {
                    // The terrain raster stores a filtered downstream vector across the wetted channel.
                    float2 velocity = (flow.rg * 255.0 - 128.0) / max(127.0 * flow.b, 1.0);
                    float2 uv = ground * 39.0 - velocity * (_Time.y * 0.21);
                    float2 ripple = SAMPLE_TEXTURE2D_GRAD(_GroundPatterns, sampler_GroundPatterns,
                        uv, groundDx * 39.0, groundDy * 39.0).rg;
                    float detail = 1.0 - smoothstep(0.085, 0.30, max(length(groundDx), length(groundDy)) * 39.0);
                    float movingLight = (ripple.x - 0.5) * 0.28 + (ripple.y - 0.5) * 0.12;
                    float sheen = smoothstep(0.46, 0.78, ripple.x) * 0.055;
                    waterDelta += (water * movingLight + float3(0.15, 0.22, 0.28) * sheen) * (channel * detail);
                }
                UNITY_BRANCH
                if (lake > 0.001)
                {
                    float time = _Time.y;
                    float2 wind = float2(0.82, 0.57);
                    float2 crossWind = float2(-0.43, 0.90);
                    float phase = dot(ground, wind) * 295.0 - time * 2.1 + broad.r * 3.1;
                    float crossPhase = dot(ground, crossWind) * 418.0 + time * 1.24 + patches.a * 2.2;
                    float swell = sin(dot(ground, wind) * 71.0 - time * 0.60 + broad.b * 2.8);
                    float footprint = max(length(groundDx), length(groundDy));
                    float waveVisibility = 1.0 - smoothstep(0.55, 1.8, footprint * 418.0);
                    float waves = (sin(phase) * 0.64 + sin(crossPhase) * 0.36) * waveVisibility;
                    float sheen = (0.5 + 0.5 * sin(phase + sin(crossPhase) * 0.18)) * waveVisibility;
                    sheen *= 0.55 + 0.45 * broad.g;
                    waterDelta += (water * (swell * 0.037 + waves * 0.061)
                        + float3(0.18, 0.19, 0.17) * (sheen * 0.045)) * lake;
                }
                water += waterDelta;
                water = lerp(water, float3(0.54, 0.66, 0.69), ice);

                float waterline = saturate(1.0 - coastalLand + softFreshwater - rawFreshwater);
                float visibleWater = saturate(waterline * (1.0 + channel * 0.30));
                return lerp(land, water * (1.0 - 0.20 * terrainShade), visibleWater);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 map = SAMPLE_TEXTURE2D(_MapColours, sampler_MapColours, input.uv);
                float3 ground = ShadeGround(input, normalize(input.normalWS), map);
                float politicalCoverage = _ShowPolitics * smoothstep(0.52, 0.95, map.a);
                if (politicalCoverage <= 0.0) return half4(ground, 1);

                float3 province = ProvinceAt(input.uv);
                UNITY_BRANCH
                if (_ShowOwnership > 0.5)
                {
                    float id = dot(round(province * 255.0), float3(1.0, 256.0, 65536.0));
                    if (id >= 1.0 && id <= _OwnershipGrid.z)
                    {
                        float row = floor(id * _OwnershipGrid.y);
                        float2 cell = float2(id - row * _OwnershipGrid.x, row);
                        float2 uv = (cell + 0.5) * _OwnershipGrid.y;
                        float owned = SAMPLE_TEXTURE2D_LOD(_OwnershipMask, sampler_ProvinceIndices, uv, 0).r;
                        ground = lerp(ground, _OwnershipColour.rgb, owned * politicalCoverage * 0.48);
                    }
                }
                float3 colour = lerp(ground, float3(1.0, 0.78, 0.26),
                    Matches(province, _Selected) * 0.26 * politicalCoverage);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
