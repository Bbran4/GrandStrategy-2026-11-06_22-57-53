# World-generation tool: Old World heightmaps

The tool authors heightmaps, carves rivers, previews terrain in 3D, and exports an OpenGS land image. Rivers use a uniform-runoff approximation on the generated terrain.

## Quick start

1. Open the Unity project with **6000.6.8f1** and allow scripts to compile.
2. Choose **Tools → GrandStrategy → World Generator**.
3. Keep **1024 × 512 · Prototype** selected for initial exploration.
4. Enter a seed and click **Generate**, or use **New seed + generate**.
5. Inspect **Relief**, **Grayscale**, and **LandAndWater** views. Hover over the 2D map to inspect samples. **LandAndWater** uses the OpenGS data palette.
6. Select a recipe step to edit it. Enable/disable, reorder, add, or remove steps, then generate again.
7. Use **Save displayed world as asset…** to preserve the result. Select that asset in **Saved World** and choose **Load saved world** to reopen it.
8. Use **Export displayed world…** for height data and inspection images.

The Unity Editor execution path is provided but has not yet been verified interactively. See validation below before treating saving or exports as production-proven.

### Viewing the world in 3D

Click **3D** in the preview toolbar after generating or loading a world. Drag with the left mouse button to orbit, scroll to zoom, and use **Reset view** to return to the initial camera. The **Height** slider sets the world's terrain height scale and updates the preview immediately. Save a world asset to retain the setting. Loading older world assets uses the original default of 0.35. Toggle **3D** off to return to the top-down view.

The preview uses a private scene with URP materials and a downsampled mesh, at most 257 vertices on either axis. Normalized heightmap coordinates map west-to-east along +X and south-to-north along +Z. The longest raster axis determines mesh sampling density. Texture and mesh use the same orientation.

The preview displays existing water at sea level. Small features can be absent from the coarse preview mesh even though they remain present in the full-resolution heightmap and export. No preview objects are added to the open game scene, and preview resources are released when the window closes.

### Carving rivers

Generate or load terrain, then click **Generate Rivers**. Lower **Catchment (%)** to include smaller tributaries. **Channel depth** controls typical incision. **Channel width** scales both the visible water and the carved banks. Contributing area makes headwaters narrow and widens the river toward its mouth. Generate rivers again to apply changes. The **Rivers** view shows the resulting water footprint in 2D and 3D. Older river assets keep their centerline preview until regenerated. **Clear Rivers** restores the base. Generating rivers again always starts from that base.

Routing starts at existing water and uses an eight-neighbour adaptation of Priority-Flood. A temporary spill priority lets flow cross dry saddles while the base samples stay intact. Contributing area sets the channel threshold and width. A final pass grades channel beds toward their outlets, which can require deeper cuts through saddles. Flat bed sections can occur. Carving stays at or above sea level on land so existing oceans and lakes keep their footprints. [Priority-Flood paper](https://doi.org/10.1016/j.cageo.2013.04.024)

The world asset stores the original samples, cut elevations, flow directions, and river settings as separate layers. The preview, gameplay read, and export use the same composition rule. A zero cut keeps the original sample. A positive cut supplies the final normalized elevation at that sample, including the cut banks around a channel. Existing river assets recover their cut layer from the saved base and carved terrain on load. Saving a new world asset writes the separate layers.

Standalone Linux measurements for the initial river implementation used seed 271828, a fresh process per preset, and two passes per process. The baseline generated terrain and classified water. The river workflow also carved channels and classified the final terrain. CPU and peak memory cover both passes in each process. The separate cut layer, Editor rendering, and export costs are outside these measurements.

| Preset | Baseline first / repeat | River workflow first / repeat | Process CPU baseline / rivers | Process peak baseline / rivers |
| --- | --- | --- | --- | --- |
| 1024 x 512 | 0.523s / 0.482s | 0.553s / 0.475s | 1.070s / 1.088s | 47.7 / 61.5 MiB |
| 2048 x 1024 | 1.818s / 1.751s | 2.024s / 2.016s | 3.622s / 4.108s | 107.6 / 157.5 MiB |
| 3072 x 2048 | 5.509s / 5.925s | 6.104s / 6.058s | 11.499s / 12.201s | 247.9 / 295.5 MiB |

### Testing inland naval access

Generate rivers and click **Preview Naval Access**. The preview starts at mouths that reach the ocean and follows connected channels upstream. **Naval rise limit** is an authoring control in normalized elevation above sea level. The default is 0.08. This first pass estimates access by connection and elevation. Ship draft, width, rapids, ports, and fleet movement need gameplay rules.

Enable **Export naval mask** to include two optional files. The export manifest records the chosen rise limit.

| File | Contents |
| --- | --- |
| naval-land-mask.png | An alternate OpenGS Land Image. Accepted reaches use the full river water width and ocean RGB (5,20,18), with cardinal connections for diagonal reaches. Existing lakes keep their lake colour. |
| river-naval-access.raw | One byte per sample in heightmap order. Bit 0 marks a route cell, bit 1 marks a mask-only connector, and bit 2 marks the shared river water footprint. Route cells use the original river directions for movement. |

Use the alternate image in OpenGS to try generating ocean provinces along the river. OpenGS decides province boundaries and grouping, so inspect whether narrow reaches receive useful province shapes. Use the regular land mask when authoring land provinces and keep naval access as a separate gameplay layer if the same area needs land ownership and ship access. [OpenGS MapTool](https://github.com/Thomas-Holtvedt/opengs-maptool#readme)

## Resolution

| Preset | Samples | Height data alone |
| --- | ---: | ---: |
| 1024 × 512 | 524,288 | 2 MiB of float samples |
| 2048 × 1024 | 2,097,152 | 8 MiB of float samples |
| 3072 × 2048 | 6,291,456 | 24 MiB of float samples |

These are **heightmap sample dimensions**, not Unity world units, province counts, or physical distances. The large preset also changes aspect ratio from 2:1 to 3:2, so the same seed does not produce the same framing. Square pixels and normalized feature sizes relative to map height are used.

Generation uses additional coordinate and working arrays. Preview textures, managed colour buffers, saved assets, and the Unity Editor itself consume additional memory. Saving large float arrays as Unity text assets can produce substantially larger files than the raw sample buffer.

Rectangular heightmaps are not assigned directly to Unity Terrain: its square power-of-two-plus-one sampling requirements need a separate tiling/resampling decision. This increment deliberately preserves the requested raster dimensions.

## Old World approach

The authoring concept is inspired by [Azgaar’s heightmap template editor](https://github.com/Azgaar/Fantasy-Map-Generator/blob/master/docs/wiki/Heightmap-template-editor.md) and its [Old World recipe](https://github.com/Azgaar/Fantasy-Map-Generator/blob/master/src/data/heightmap-templates.ts).

This is an original C# raster implementation, not an Azgaar map importer or a port of its cell-based algorithms. The recipe is our own. Old World means a family of large landmasses, mountain belts, depressions, and channels; it does not reproduce Earth.

The default recipe lays out western and eastern continental uplifts, adds smaller features, lowers inland relief, introduces mountain belts and depressions, cuts a channel, and adds detail and slope relaxation. Seed and settings control the outcome. No particular coastline, navigable strait, or connected inland sea is guaranteed.

### Controls and operations

| Control / operation | Effect |
| --- | --- |
| Seed | Reproduces a recipe within this generator version |
| Sea level | Defines the normalized land/water threshold; affects generation as well as preview |
| Coast irregularity | Distorts feature coordinates at several scales |
| Ocean border | Lowers elevations at the bounded map edges |
| Hill | Raises a broad, irregular landform |
| Range | Adds an elongated, curved, textured mountain belt |
| Trough | Lowers an elongated region |
| Pit | Creates a local depression |
| Strait | Lowers terrain along a meandering horizontal or vertical channel |
| MultiplyLand | Scales elevations above sea level relative to that level |
| Detail | Adds multi-scale elevation variation |
| Smooth | Blends elevations with neighbouring samples |
| Erode | Transfers height from steeper samples to a lower neighbour; simple thermal-style relaxation |

For shaped features, count and strength are drawn within the selected ranges. For smoothing and erosion, count controls passes and minimum strength controls the effect. MultiplyLand and Detail apply once, using minimum strength. Radius and length are percentages of map height; placement limits are percentages of their respective map axes. Range orientation is seeded. A strait spans the map; its perpendicular placement limit sets its channel location.

Operations are ordered and can intentionally undo or soften earlier changes. Reordering changes the recipe. Disabling a step preserves the random stream assigned to later step indices; inserting or moving steps changes those indices.

The realism goal is more coherent relief, multi-scale coastlines, and less abrupt slopes. This is **not a physical tectonic, climate, or hydraulic simulation**. Dry depressions can feed channels through their lowest available outlet. In Relief, white high-elevation colours are an elevation palette, not a snow model, and green colours are not biome assignments. In LandAndWater, bright green specifically identifies a lake for OpenGS.

## Preservation and cancellation

- Generation snapshots the recipe before working and publishes only a completed result.
- Cancelling generation retains the previous completed world.
- Editing settings does not alter the displayed world. A warning indicates pending regeneration.
- Saving and exporting use the displayed result and its original recipe, even when current controls have changed.
- Saving a world creates a new asset; existing authored assets are not silently replaced.
- Recipe JSON stores settings, not elevation samples. Save the world asset to retain the exact terrain.
- The window's unsaved terrain preview is transient and can be lost on script/domain reload. Saved world assets can be reopened.
- Generator changes may change regenerated results. Preserve authored sample data rather than relying on a seed alone.

## Exports and coordinates

Each export creates a new uniquely named subfolder under the chosen parent. An incomplete export reports its location and is retained for inspection.

| File | Meaning |
| --- | --- |
| heightmap.raw | Final terrain, including any channel cuts, as headerless unsigned 16-bit little-endian samples |
| heightmap-base.raw | Original samples in the same format, included when rivers are present |
| river-cuts.f32 | One little-endian float32 per sample when rivers are present, aligned with the base and final rasters. Zero keeps the base value. Positive values give the final cut elevation before RAW16 quantization |
| river-directions.raw | One byte per sample when rivers are present. Zero means no channel. Values 1–8 run counterclockwise from east in 45-degree increments |
| river-widths.raw | One unsigned byte per sample when a width layer is present. Zero means off-channel. Positive values give full river width in raster samples |
| rivers.png | Inspection image of the channel network over carved relief, included when rivers are present |
| heightmap-preview.png | 8-bit grayscale inspection image, not the precision source |
| relief.png | Coloured elevation inspection image |
| land-mask.png | OpenGS Land Image with ocean, lakes, and land. River channels are land in this mask |
| manifest.json | Recipe, dimensions, sea level, terrain height scale, format version, encoding, and coordinate documentation |

Authoritative Unity asset samples are normalized floats from 0 to 1. They are not elevations in metres. RAW16 quantizes those floats to 0–65535. Values at or above sea level are land.

Data arrays and RAW16 are row-major: **X west to east, Y south to north, first row at the south edge**. PNGs show north at the top. Use the manifest when importing elsewhere to avoid vertical flips or incorrect byte order.

### OpenGS colours and water classification

Import **land-mask.png** into OpenGS MapTool's **Land Image** tab.

| Kind | Exact RGB |
| --- | --- |
| Ocean and included river water | (5, 20, 18) |
| Lake | (0, 255, 0) |
| Land | (255, 255, 255) |

Both OpenGS views and exports use the same palette. LandAndWater classifies ocean, lakes, and land. NavalAccess adds the full-width ship corridors in the ocean colour. Lake pixels always take priority over river markings. Data pixels are written directly as opaque bytes, without terrain shading, interpolation, or colour conversion. The data texture uses point filtering. Relief retains its illustrative elevation colours and is not the OpenGS input. On-screen graphics colour management is not a substitute for checking PNG bytes.

Classification uses four-neighbour raster connectivity: below-sea-level samples connected to any map boundary are ocean; other submerged samples are lakes. Diagonal-only contact does not open a sea passage. Classification is computed once for the displayed result and reused by previews and exports; it does not change elevation samples.

This is a topological classification, not completed hydrology. Inland seas without an ocean connection are currently classified as lakes, including tiny enclosed depressions. Rivers use these existing submerged basins as outlets. No minimum-area filter silently removes small lakes. The original world asset stays compatible because the water map is derived from its saved elevation and sea level.

The manifest includes `TerrainHeightScale` alongside the normalized RAW16 samples. The preview multiplies the normalized elevation above sea level by this scale, using the map's north-to-south span as one unit. A consumer of an older export can use 0.35 when the field is absent. Scale is recorded separately so the height samples and OpenGS classification stay consistent.

The manifest is version **2** to describe the OpenGS palette and connectivity rule. Version 1 exports used a black/white mask; export again to obtain the OpenGS image. Province export/import and terrain-image classification remain future work.

## Code ownership

- **HeightmapRecipe:** serializable settings, default recipe, and validation.
- **HeightmapGenerator:** Unity-independent seeded generation and completed results.
- **RiverGenerator:** drainage routing, channel selection, carving, and the base/final terrain pair.
- **HeightmapEncoding:** RAW16 serialization and documented byte/row order.
- **WaterMap / OpenGsLandPalette:** water topology, coverage counts, and the exact shared data colours.
- **WorldHeightmap:** Unity asset preservation and validation of saved samples.
- **Editor:** authoring window, elevation textures, private-scene 3D preview, export commands, and a batch smoke check.

Generation uses a private stable random implementation, not Unity's global random state. Work and dimensions are bounded before raster allocation. The generator currently uses four float arrays, plus small feature lists and the final result; consumers add their own buffers.

## Validation

### Standalone core checks

Install a compatible .NET 8 SDK, then run from **Tests/WorldGeneration**:

```powershell
dotnet run -c Release
dotnet run -c Release -- --benchmark
```

The harness has no external NuGet packages. It checks seeded reproduction, changed seeds, input snapshots, land/water coverage, bounded ocean edges, invalid inputs, late cancellation, progress monotonicity, disabled steps, and RAW16 byte order and orientation using asymmetric sentinel samples. It also checks boundary-connected oceans, enclosed lakes, diagonal contact, channel opening, all-land/all-water maps, exact palette bytes, and invalid classification inputs. **124 assertions passed** after adding water classification.

Benchmark output reports elapsed time, process CPU time, and cumulative process peak working set for first and repeat runs at all three preset sizes. These are standalone core measurements, not end-to-end Unity Editor costs. First runs follow smaller checks and therefore are not fully cold starts. There is no before-change implementation baseline. Agent-token use, process-family peak memory, Unity import, serialization, rendering, and interactive costs are unmeasured.

Measured on the development Windows machine with .NET 8, Release, seed 271828 (108 assertions passed):

| Resolution | First elapsed / CPU | Repeat elapsed / CPU | Cumulative process peak after repeat |
| --- | --- | --- | --- |
| 1024 × 512 | 0.422s / 0.422s | 0.391s / 0.406s | 43.5 MiB |
| 2048 × 1024 | 1.551s / 1.562s | 1.516s / 1.531s | 100.0 MiB |
| 3072 × 2048 | 4.603s / 4.625s | 4.534s / 4.531s | 268.1 MiB |

These peaks are cumulative within a single harness process and include retained allocations and runtime overhead; they are not isolated per-run allocation measurements. The large preset has a different aspect ratio, so it is a capacity check rather than an equivalent-view quality comparison.

After adding classification, the same standalone workflow and seed measured:

| Resolution | Generate first / repeat | Classify first / repeat | Classify CPU first / repeat | Cumulative process peak after repeat |
| --- | --- | --- | --- | --- |
| 1024 x 512 | 0.402s / 0.385s | 0.005s / 0.003s | <0.001s / <0.001s | 47.7 MiB |
| 2048 x 1024 | 1.487s / 1.563s | 0.013s / 0.010s | 0.016s / 0.016s | 110.3 MiB |
| 3072 x 2048 | 4.496s / 4.558s | 0.038s / 0.036s | 0.047s / 0.031s | 309.6 MiB |

Generation CPU first/repeat was 0.406s/0.391s, 1.484s/1.531s, and 4.500s/4.547s respectively. Classification times are additional work compared with the earlier workflow. Sub-millisecond reported CPU values reflect timer resolution, not zero cost. The classification retains one byte per sample and temporarily allocates one integer per submerged sample. This run retains classification work between measurements, so differences in cumulative process peaks are not isolated allocation deltas. 3D frame time, GPU memory, and total Editor memory remain unmeasured.

### Unity smoke check

```powershell
& 'PATH_TO_UNITY_EDITOR\Unity.exe' -batchmode -nographics -quit `
  -projectPath 'PATH_TO_PROJECT' `
  -executeMethod GrandStrategy.WorldGeneration.Editor.WorldGenerationSmokeChecks.Run `
  -logFile 'PATH_TO_LOG\worldgen-smoke.log'
```

The entry point is intended to verify recipe JSON regeneration, an actual world-asset disk round-trip, PNG generation for all preview modes, exact OpenGS RGB bytes after PNG decode, preview mesh dimensions/normals, and window creation. It creates a uniquely named temporary asset and removes it afterward. Successful execution logs **WORLDGEN_SMOKE_PASS**. The mesh checks do not exercise GPU rendering or orbit controls.

In the development session, the full source compiled against the installed Unity **6000.6.8f1** reference assemblies. The initial Unity batch launch stopped because the Editor could not connect to its local Package Manager server. A later isolated attempt with the documented -noUpm option progressed further but timed out waiting for licensing initialization; it was stopped. **No Unity runtime smoke-check pass, interactive 3D verification, or actual Unity save/export round-trip is claimed.** A separate image reconstructed from generated RAW16 was visually inspected in the earlier heightmap increment; that does not validate Unity preview rendering or PNG encoding.

### Manual checks still required

- Open the window, generate each preset, inspect all preview modes, and check hover coordinates.
- Toggle 3D, orbit, zoom, reset the camera, and change Height. Save and reopen the world to verify the slider restores, then check `TerrainHeightScale` in the export manifest.
- Inspect the OpenGS mask's exact decoded RGB values and import it into OpenGS's Land Image tab.
- Change settings and confirm the displayed result remains unchanged until regeneration.
- Cancel generation and confirm the previous completed result remains available.
- Save an asset, reload scripts, and reopen that asset.
- Save/load a recipe and confirm regenerated terrain matches.
- Export and independently read RAW16 plus its manifest; verify the PNG orientation against it.
- Inspect multiple seeds for excessive fragmentation, clipped peaks, or implausible features.

## Next increments

1. Verify the authoring workflow in the Editor and refine landforms from visual feedback.
2. Check channel density, channel depth, and the saved/exported river terrain in Unity.
3. Add climate, biomes, and permanent snow as separate data layers.
4. Add deliberate terrain rendering and shared map/world coordinates.
5. Add validated OpenGS inputs and province-map import.
