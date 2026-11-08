# GrandStrategy

GrandStrategy is an ancient-era, real-time grand strategy game about founding a tribal society and growing it into an empire.

The setting is inspired by Gaul during the Roman period. Players begin in an unclaimed province, establish their own civilization, and expand through development, trade, diplomacy, cultural influence, and warfare. The long-term ambition is an empire comparable in scale to Rome.

The player represents the society itself. Individual characters, rulers, dynasties, marriages, and personal succession are outside the game’s scope.

## Project status

**Playable map prototype.** The world authoring tool, OpenGS import, and first Unity gameplay scene are working. Open `Assets/Scenes/PlayableMap.unity` and press **Play** to pan and zoom over the authored world, inspect provinces, and establish a tribe in a land province. The first campaign loop includes a paused start, Pop and ownership, peaceful integration, and local save and load. The campaign loop has been verified in Unity Play Mode.

**Next milestone.** Verify the terrain and forest presentation in Unity, then begin province development and economy. See the [development roadmap](#development-roadmap) for the order of work.

- **Engine** · Unity 6000.6.8f1
- **Rendering** · Universal Render Pipeline (URP)
- **Development automation** · Unity Pipeline package
- **Source control** · GitHub with Git LFS configured for binary assets

## World-generation tool

Open **Tools → GrandStrategy → World Generator** in Unity. Start with **1024 × 512**, choose a seed, and click **Generate**. The window also supports 2048 × 1024 and 3072 × 2048. Toggle **3D** to orbit the terrain, scroll to zoom, and adjust the Height slider. Saving the world or exporting it records the selected terrain height scale.

**LandAndWater** and the exported **land-mask.png** use OpenGS's exact data colours: ocean RGB (5,20,18), lake RGB (0,255,0), and white land. The optional naval mask adds full-width navigable rivers in the ocean colour. Boundary-connected water is ocean; enclosed below-sea-level water is lake. River generation uses the existing oceans and lakes as outlets.

Generate the base terrain, then select **Generate Rivers**. Saved worlds store the base terrain, river cuts, and flow directions. The game and export use the heightmap composed from the base and cuts. Generate climate to preview temperature and moisture and export both data layers. Generate biomes to preview land cover and export the OpenGS Terrain image paired with either land mask. Save the recipe JSON to rebuild the configured stages, or save the World asset to retain the exact terrain samples. Snow coverage is planned with the terrain presentation work. [World generation](docs/world-generation.md)

## Design pillars

- **Identity through deeds:** Meaningful accomplishments should unlock choices in society-wide passive progression.
- **Negotiated belonging:** Communities can join under commitments involving autonomy, tribute, protection, and cultural rights.
- **History on the landscape:** Development, fortification, devastation, and recovery should become visible in settlements.
- **Society over characters:** Build institutions, manage population, and shape a civilization across its history.
- **Geography matters:** Terrain, water, climate, and resources shape settlement, movement, trade, and expansion.
- **Several paths to expansion:** Conquest, diplomacy, cultural incorporation, and settlement all have a place.
- **Accessible economic management:** Resources create strategic choices without detailed production-chain or shipment management.
- **Visible development:** Settlements and their surroundings visibly change as population and buildings develop.
- **Growth brings administrative challenges:** Laws, integration, infrastructure, and military organization support the transition from tribe to empire.

Gameplay takes inspiration from Europa Universalis. Crusader Kings-style terrain and political-map presentation are visual references, rather than a character-simulation model. Civilization’s readable settlement growth and population development are additional references.

## Campaign and time

The initial approach uses **developer-authored worlds**, created through generation and refinement before release.

- The map is flat and bounded.
- The player chooses an unclaimed starting province and establishes a tribe.
- Time advances continuously.
- Players can pause and adjust simulation speed.
- Armies move between connected provinces, with travel time.
- Citizens are represented through an abstract population value, **Pop**, rather than individually controlled units.

Exact map dimensions, calendar rules, speed settings, and starting conditions remain to be defined. Player-generated campaign worlds are a possible future extension, not an initial requirement.

## World generation

The editor tool authors the physical world before provinces and societies are added. It generates the base heightmap, classifies oceans and lakes, carves rivers, and derives climate and biomes. It saves the World asset and exports aligned inputs for OpenGS.

### World-data principles

- Keep elevation, water, climate, biomes, snow, provinces, and political ownership as distinct data layers.
- Use a shared coordinate convention so exports, imports, terrain, and province selection align.
- Keep authoritative geographic data separate from visual materials.
- Base river generation on drainage and flow, using existing oceans and lakes as outlets.
- Regenerate geography explicitly once provinces or settlements depend on it.
- Set world-size and generation-time budgets before selecting production resolutions.

## Provinces and OpenGS MapTool

Province authoring uses [OpenGS MapTool](https://github.com/Thomas-Holtvedt/opengs-maptool).

1. Generate and approve the world in Unity.
2. Export the aligned land or naval mask and biome-based terrain image.
3. Create territories and provinces in OpenGS MapTool.
4. Import the map rasters and definitions in Unity, with territory history where available.
5. Save a Province Map asset and use it with its matching World asset in the playable scene.

The Unity importer indexes the province and territory rasters and derives province adjacency from shared edges. It also records biome counts and river and coast summaries. OpenGS assigns terrain using the province centre, so the game keeps the underlying biome and geography layers available for finer detail.

Province boundaries form the geographic gameplay structure. Ownership changes independently of those boundaries.

Proposed province information includes:

- Stable identity, extent, and neighbouring provinces.
- Terrain, biome, freshwater, and resource characteristics.
- Population and development.
- Main settlement and constructed buildings.
- Political ownership, military occupation, and settlement control.
- Movement connections, including relevant river crossings.

Exact mappings between OpenGS territories and in-game administrative regions remain undecided.

## Societies and independent communities

Players create their own civilization, including its name, flag, symbols, and colours.

An unclaimed province is outside an established civilization’s territory. It may contain wilderness and independent populations organized into local communities or tribes.

Interactions with independent societies include:

- Communication and diplomacy.
- Trade.
- Cultural influence and eventual incorporation.
- Military subjugation or conquest.
- Enslavement.

Independent communities are participants in expansion, rather than empty map decoration.

### Proposed expansion distinctions

| Outcome | Intended distinction |
| --- | --- |
| Settlement | Establish a presence where suitable land and local circumstances permit |
| Conquest | Acquire direct territorial control through warfare |
| Subjugation | Impose obligations while allowing some local autonomy |
| Cultural incorporation | Bring a community into society through sustained influence and acceptance |
| Enslavement | Impose a coercive population status with resistance and political consequences |

The detailed rules for settlement in inhabited provinces, diplomacy, acceptance, resistance, and enslaved population management remain open. Military occupation does not itself imply cultural acceptance.

## Population and development

Population is abstract. Players manage provinces and societies rather than individual citizens.

Player responsibilities include:

- Building construction and provincial development.
- Recruitment and army orders.
- Research.
- Diplomacy and trade policy.
- Government, military, and economic laws.
- Experience-based passive progression.
- Responses to random events.

The proposed population-growth model uses food availability alongside provincial capacity. Food surplus supports growth; shortages reduce growth and draw down reserves. Severe, sustained shortages can lead to famine and population loss.

Exact population categories, housing rules, growth formulas, and recruitment effects remain to be designed.

## Economy and automatic trade

The economy combines tax-derived coin with physical goods. It should support strategy without becoming a detailed commercial or supply-chain simulator.

### Initial resource roster

| Resource | Planned uses | Typical sources |
| --- | --- | --- |
| Coin | Construction, recruitment, army upkeep, administration | Taxes, trade duties, tribute |
| Food | Population support, growth, army supply | Agriculture, pasture, fishing |
| Timber | Construction and military equipment | Forest provinces |
| Iron | Advanced military equipment and some construction | Developed mineral deposits |
| Horses | Cavalry recruitment and replacements | Suitable grazing land and breeding |

The roster is an initial scope, not a final balance specification. Separate manufactured weapons, tools, and armour goods are deferred.

### Production and food

Basic food production should come from population working suitable land. Players should not need to construct individual farms repeatedly to prevent routine starvation.

Agricultural development, storage, and infrastructure improve output and resilience. Famine can arise from genuine shortages, potentially intensified by drought, war, or disrupted trade.

### Trade

Trade should occur automatically where goods, demand, access, and transport capacity permit.

The player influences trade through diplomacy, infrastructure, tariffs, and strategic priorities, rather than directing individual shipments.

The proposed model stores resources locally and presents a national summary. Local needs are met before accessible surpluses are exchanged. Roads, ports, blockades, and isolation can therefore matter without manual warehouse management.

Exact pricing, foreign-trade rules, transport limits, and stockpile behaviour remain open.

### Construction and recruitment

- Buildings generally require coin and timber.
- Some advanced construction may also require iron.
- Recruitment requires population, coin, and appropriate resources.
- Infantry equipment requirements vary by unit.
- Cavalry requires horses, with additional equipment depending on type.
- Armies require ongoing coin and food.
- Replacements require additional manpower and relevant resources.

## Armies, occupation, and sieges

Armies operate through province-based movement. Visual models may travel smoothly between locations while the simulation tracks province connections and arrival times.

**Walls protect the main settlement, rather than the entire province.** An invading army can occupy the countryside while a fortified settlement continues to resist.

| Stage | Intended behaviour |
| --- | --- |
| Entry contested | A field battle can occur |
| Countryside occupied | Production and trade can be disrupted |
| Fortified settlement resists | The attacker must conduct a siege to capture it |
| Settlement captured | The attacker holds the province’s settlement and countryside |
| War settlement | Agreements determine lasting ownership changes |

Political ownership, military occupation, and settlement control are separate states.

Unfortified settlements should be captured more quickly once local resistance is defeated. Siege details, garrison rules, battle resolution, supply calculations, and peace terms remain open.

Terrain and water should inform movement, defence, crossings, and supply. Their exact modifiers will be designed after geographic and province data are available.

## Research, laws, and passive skills

These systems serve different purposes:

| System | Purpose |
| --- | --- |
| Research | Unlock knowledge, buildings, equipment, and administrative capabilities |
| Laws | Define institutions, rights, obligations, and how society functions |
| Passive skill tree | Improve the effectiveness of the player’s chosen strategy |

Experience and skill points are part of the agreed progression direction. Exact rewards, costs, and trees remain open. The proposed approach rewards peaceful achievements alongside warfare.

### Laws

| Category | Example subjects |
| --- | --- |
| Government | Council authority, centralization, autonomy, citizenship, administration |
| Military | Clan levies, professional service, recruitment duties, service obligations |
| Economic | Taxation, land tenure, tariffs, tribute, slavery, emancipation |

Laws should have meaningful trade-offs. Proposed reforms require appropriate capabilities, support, and time, rather than unrestricted instant switching.

Different institutional paths may support imperial-scale societies. No character or dynasty system is planned.

## Civilization identity and visible settlements

Settlements begin with a simple tribal appearance and visibly develop.

- Walls appear around the main settlement.
- Towers appear in the village or its defences.
- Farms appear around suitable outskirts.
- Additional buildings add recognizable structures.
- Population growth increases the visible settlement’s scale and density.

The proposed first implementation uses one main settlement per province and automatic placement of visual structures. Village, town, and city stages are a proposed way to communicate growth.

Visual structures represent abstract development; each visible house or farm does not need to correspond to one exact population or production unit.

### Later cosmetic customization

Players should eventually be able to select:

- Architectural appearances.
- Soldier appearances.
- Statues and decorative features.

These options are deferred. Appearance should remain separate from gameplay rules so cosmetic changes do not alter building or unit behaviour.

## Events

Random events should create society-level opportunities and pressures.

Possible subjects include harvest conditions, famine, trade opportunities, cultural tensions, taxation disputes, demands for autonomy, and military obligations.

Events concern the society and its institutions. Individual-character stories are outside scope.

## Development roadmap

### 1. World authoring · working

- [x] Generate and preview an editable, seeded heightmap with adjustable sea level and height scale.
- [x] Classify oceans and lakes, carve rivers, and keep the base and cuts separate from the final heightmap.
- [x] Generate climate and biomes.
- [x] Save World assets and recipes, and export aligned OpenGS land, naval, and terrain inputs.

### 2. OpenGS import and playable map · working

- [x] Import provinces and territories, preview both in the editor, and save a Province Map asset.
- [x] Derive province neighbours, biome counts, and water and river summaries.
- [x] Open an authored map in Play Mode, pan and zoom, and select and inspect provinces.
- [x] Name a tribe, choose a colour, and place a starting settlement.

### 3. First campaign loop · working

1. **Found a society.** Turn the starting province into the capital. Give land provinces abstract Pop and track ownership in campaign state. Unclaimed land starts with independent populations. Keep campaign state separate from the reusable map assets.
2. **Start the clock.** Begin paused after founding. Add a date, pause and speed controls, and daily simulation updates.
3. **Integrate one neighbour.** Spend coin to establish ties with a neighbouring independent community. Show progress in its province panel. When it joins, update the province owner and map colour. Use a simple coin budget for this first pass.
4. **Save and resume.** Persist the tribe, ownership, Pop, date, and integration progress separately from world data.

**Completion target.** Found a tribe, bring one neighbour into it through peaceful integration, and resume that campaign from a save.

### 4. Map presentation · after the first campaign loop

Work through these in order so the camera and political overlays set the scale for terrain detail.

1. **2D to 3D zoom · implemented.** Full zoom-out fits the entire map in a straight overhead view. The camera gradually tilts into the terrain as the player zooms in.
2. **Crisp borders and map modes · implemented, Unity verification pending.** Trace each shared boundary once and preserve junctions. Render smoothed, anti-aliased outlines at a consistent screen width on the terrain. Selection, tribe colours, and province outlines share the same smoothed geometry. The import resolution limits source precision.

   | Map mode | Presentation |
   | --- | --- |
   | Terrain | Show geography without political borders or tribe colours. |
   | Territories | Show territory borders and tribe colours. When tribes share a territory, show each tribe's holdings inside its outer border. |
   | Provinces | Show individual province borders and tribe colours. |

3. **Biome materials and nature · Unity verification pending.** Use subdued ground colours, layered biome patterns, exposed rock, and snow. Paint distant forests from the same seeded groves that produce the close-up trees, with smaller props on suitable terrain.

**Completion target.** Inspect the whole map overhead, zoom into detailed terrain, and switch between geographic and political views while borders stay clear.

### 5. Development and economy

- [ ] Expand the prototype coin budget into taxation, goods production, and the initial resource roster.
- [ ] Add construction and basic buildings.
- [ ] Add food reserves and population growth.
- [ ] Add automatic resource exchange and a national summary.
- [ ] Add initial settlement visuals tied to development.
- [ ] Introduce shortage and famine behaviour.

**Completion target:** Develop provinces and sustain a growing society without manual shipment management.

### 6. Military and territorial control

- [ ] Add recruitment, upkeep, and province-based army movement.
- [ ] Add field battles and initial rival military behaviour.
- [ ] Separate ownership, countryside occupation, and settlement control.
- [ ] Add walls, towers, garrisons, and sieges.
- [ ] Add war resolution and territorial transfers.

**Completion target:** Conduct a campaign involving movement, battle, occupation, siege, and lasting territorial change.

### 7. Diplomacy, culture, and integration

- [ ] Expand the first integration action into diplomacy and agreements with independent and rival societies.
- [ ] Add foreign trade access.
- [ ] Add cultural influence and acceptance.
- [ ] Add incorporation, subjugation, and resistance.
- [ ] Define and implement coercive population statuses and their consequences.

**Completion target:** Expand and maintain relationships through meaningful military and non-military choices.

### 8. Institutions and long-term progression

- [ ] Add research.
- [ ] Add government, military, and economic laws.
- [ ] Add experience and the passive skill tree.
- [ ] Add society-level events.
- [ ] Develop administration and integration challenges at imperial scale.

**Completion target:** Support distinct development paths from tribal beginnings to a large, sustainable empire.

### 9. Presentation and cosmetics

- [ ] Expand settlement growth and building visuals.
- [ ] Expand flags and symbols.
- [ ] Add selectable building and soldier appearances.
- [ ] Add statues and decorative customization.

## Working with the project

Open the repository’s project folder through Unity Hub using **Unity 6000.6.8f1**. Open `Assets/Scenes/PlayableMap.unity` to try the current prototype.

Keep source assets and their `.meta` files, `Packages`, and `ProjectSettings` in version control. Generated folders such as `Library`, `Temp`, and `Logs` are excluded by the Unity `.gitignore`.

Binary asset types are configured for Git LFS through `.gitattributes`; contributors need Git LFS installed when working with those assets. Scripts and text-serialized Unity files remain in normal Git.

The pure C# heightmap checks run with `dotnet run -c Release` from `Tests/WorldGeneration` using .NET 8. A Unity batch smoke-check entry point is provided; see the [world-generation guide](docs/world-generation.md).

## Open design decisions

Before implementing their respective systems, define:

- World size, target hardware, map resolution, and performance budgets.
- Terrain detail and border rendering budgets, plus OpenGS compatibility as the authoring pipeline evolves.
- Population scale, starting conditions, and settlement rules.
- Economic formulas, storage, trade capacity, and pricing.
- Unit roster, battle resolution, sieges, and supply.
- Diplomatic agreements, cultural incorporation, and resistance.
- Research content, laws, experience rewards, and passive skills.
- Campaign objectives and victory conditions.

Multiplayer, naval warfare, seasonal simulation, and player-generated campaign worlds are not committed features of the initial scope.
