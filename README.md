# GrandStrategy

GrandStrategy is an ancient-era, real-time grand strategy game about founding a tribal society and growing it into an empire.

The setting is inspired by Gaul during the Roman period. Players begin in an unclaimed province, establish their own civilization, and expand through development, trade, diplomacy, cultural influence, and warfare. The long-term ambition is an empire comparable in scale to Rome.

The player represents the society itself. Individual characters, rulers, dynasties, marriages, and personal succession are outside the game’s scope.

## Project status

**World-generation prototype.** The project now includes an Old World heightmap authoring window with seeded generation, editable recipe steps, elevation previews, asset saving, and RAW16 export. Gameplay systems described below remain planned. Unity Editor interaction and asset round-tripping still require verification; see the [world-generation guide](docs/world-generation.md).

- **Engine:** Unity 6000.6.8f1
- **Rendering:** Universal Render Pipeline (URP)
- **Development automation:** Unity Pipeline package
- **Source control:** GitHub with Git LFS configured for binary assets
- **First implementation priority:** World-generation tool

This README records the initial agreed direction. Proposed implementation details and unresolved decisions are identified separately.

## World-generation tool

Open **Tools → GrandStrategy → World Generator** in Unity. Start with **1024 × 512**, choose a seed, and click **Generate**. The window also supports 2048 × 1024 and 3072 × 2048. Toggle **3D** to orbit the terrain, scroll to zoom, and adjust the Height slider. Saving the world or exporting it records the selected terrain height scale.

**LandAndWater** and the exported **land-mask.png** use OpenGS's exact data colours: ocean RGB (5,20,18), lake RGB (0,255,0), and white land. The optional naval mask adds full-width navigable rivers in the ocean colour. Boundary-connected water is ocean; enclosed below-sea-level water is lake. River generation uses the existing oceans and lakes as outlets.

Generate the base terrain, then select **Generate Rivers**. Saved worlds store the base terrain, river cuts, and flow directions. The game and export use the heightmap composed from the base and cuts. Generate climate to preview temperature and moisture and export both data layers. Generate biomes to preview land cover and export the OpenGS Terrain image paired with either land mask. Snow is a future step. [World generation](docs/world-generation.md)

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

The first milestone is a tool for creating the physical world before provinces and societies are added.

### Required world features

| Feature | Purpose |
| --- | --- |
| Heightmap | Define continents, mountains, valleys, and seabed |
| Sea level | Establish coastlines and ocean coverage |
| Rivers | Represent drainage routes and strategically significant waterways |
| Lakes | Represent inland water bodies |
| Climate | Provide temperature and moisture information |
| Biomes | Classify environments such as forests, grasslands, deserts, and tundra |
| Snow | Represent cold and high-altitude snow coverage |

### Proposed authoring workflow

1. Choose a seed and world settings.
2. Generate landforms and elevation.
3. Establish sea level, drainage, and inland water.
4. Derive climate, river flow, biomes, and snow through appropriate generation stages.
5. Inspect the world using separate preview layers.
6. Refine and save an approved world.
7. Export aligned inputs for province authoring.

The proposed interface is a Unity Editor window with generation controls and previews. Seeded generation, manual refinement, and saved world data should support repeatable development. Exact algorithms, formats, resolution, and terrain rendering implementation remain open.

Climate and hydrology will use practical approximations appropriate to a strategy map. Seasonal snow and weather simulation are not required for the first milestone; permanent snow coverage comes first.

### World-data principles

- Keep elevation, water, climate, biomes, snow, provinces, and political ownership as distinct data layers.
- Use a shared coordinate convention so exports, imports, terrain, and province selection align.
- Store authoritative geographic information separately from visual materials.
- Use existing lake basins as outlets for rivers.
- Base river generation on drainage and water flow.
- Treat geography regeneration as an explicit operation once provinces or settlements depend on it.
- Set world-size and generation-time budgets before selecting production resolutions.

These are proposed engineering requirements, not claims about existing functionality.

## Provinces and OpenGS MapTool

Province authoring will use [OpenGS MapTool](https://github.com/Thomas-Holtvedt/opengs-maptool).

The intended first workflow is:

1. Generate and approve geography in Unity.
2. Export compatible land/lake masks and other useful map inputs.
3. Create territories and provinces in OpenGS MapTool.
4. Export province maps and associated data.
5. Import and validate those outputs in Unity.
6. Establish province selection, adjacency, and movement connections.

OpenGS is an external authoring tool. Unity importers and game systems still need to be implemented. Its documented terrain assignment samples a province’s centre, so detailed biome and geographic data should remain available independently.

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

Milestone 1 has an initial heightmap implementation. Later milestones remain planned; unchecked items include unfinished work and required verification.

### 1. World-generation foundation — first priority

- [ ] Define world dimensions, coordinates, saved data, and performance budgets.
- [x] Create a Unity Editor authoring window with seed and settings (compiled; interactive verification pending).
- [x] Implement seeded elevation generation and elevation previews with adjustable sea level (visual Editor verification pending).
- [ ] Verify drainage and carved rivers in the Unity Editor. Rivers use existing lakes.
- [ ] Derive climate and biomes.
- [ ] Add permanent snow coverage.
- [ ] Verify saving and reloading authored worlds in the Unity Editor (asset support implemented).
- [ ] Provide a terrain preview and aligned OpenGS-compatible exports.

**Completion target:** Generate, inspect, save, reload, and export a coherent world with aligned geographic layers.

### 2. Provinces and map interaction

- [ ] Establish a repeatable OpenGS authoring workflow.
- [ ] Import province maps and metadata.
- [ ] Validate identities, dimensions, alignment, and coverage.
- [ ] Build and validate province adjacency and movement connections.
- [ ] Add map navigation, selection, borders, and province inspection.

**Completion target:** Navigate an authored world and select connected provinces accurately.

### 3. Campaign foundation and societies

- [ ] Add the simulation clock, pause, and speed controls.
- [ ] Allow starting-province selection and tribe establishment.
- [ ] Represent independent communities and rival societies.
- [ ] Add population and province ownership.
- [ ] Add basic civilization name, colour, and banner identity.
- [ ] Save and reload campaign state.

**Completion target:** Start and resume a campaign with persistent societies, population, and advancing time.

### 4. Development and economy

- [ ] Add the initial resource roster and production.
- [ ] Add taxation, construction, and basic buildings.
- [ ] Add food reserves and population growth.
- [ ] Add automatic resource exchange and a national summary.
- [ ] Add initial settlement visuals tied to development.
- [ ] Introduce shortage and famine behaviour.

**Completion target:** Develop provinces and sustain a growing society without manual shipment management.

### 5. Military and territorial control

- [ ] Add recruitment, upkeep, and province-based army movement.
- [ ] Add field battles and initial rival military behaviour.
- [ ] Separate ownership, countryside occupation, and settlement control.
- [ ] Add walls, towers, garrisons, and sieges.
- [ ] Add war resolution and territorial transfers.

**Completion target:** Conduct a campaign involving movement, battle, occupation, siege, and lasting territorial change.

### 6. Diplomacy, culture, and integration

- [ ] Add communication and agreements with independent and rival societies.
- [ ] Add foreign trade access.
- [ ] Add cultural influence and acceptance.
- [ ] Add incorporation, subjugation, and resistance.
- [ ] Define and implement coercive population statuses and their consequences.

**Completion target:** Expand and maintain relationships through meaningful military and non-military choices.

### 7. Institutions and long-term progression

- [ ] Add research.
- [ ] Add government, military, and economic laws.
- [ ] Add experience and the passive skill tree.
- [ ] Add society-level events.
- [ ] Develop administration and integration challenges at imperial scale.

**Completion target:** Support distinct development paths from tribal beginnings to a large, sustainable empire.

### 8. Presentation and cosmetics

- [ ] Expand settlement growth and building visuals.
- [ ] Expand flags and symbols.
- [ ] Add selectable building and soldier appearances.
- [ ] Add statues and decorative customization.
- [ ] Refine terrain and political-map presentation.

## Working with the project

Open the repository’s project folder through Unity Hub using **Unity 6000.6.8f1**.

Keep source assets and their `.meta` files, `Packages`, and `ProjectSettings` in version control. Generated folders such as `Library`, `Temp`, and `Logs` are excluded by the Unity `.gitignore`.

Binary asset types are configured for Git LFS through `.gitattributes`; contributors need Git LFS installed when working with those assets. Scripts and text-serialized Unity files remain in normal Git.

The pure C# heightmap checks run with `dotnet run -c Release` from `Tests/WorldGeneration` using .NET 8. A Unity batch smoke-check entry point is provided; see the [world-generation guide](docs/world-generation.md).

## Open design decisions

Before implementing their respective systems, define:

- World size, target hardware, map resolution, and performance budgets.
- Terrain rendering, authoring controls, and generation algorithms.
- Geographic export/import formats and OpenGS version compatibility.
- Population scale, starting conditions, and settlement rules.
- Economic formulas, storage, trade capacity, and pricing.
- Unit roster, battle resolution, sieges, and supply.
- Diplomatic agreements, cultural incorporation, and resistance.
- Research content, laws, experience rewards, and passive skills.
- Campaign objectives and victory conditions.

Multiplayer, naval warfare, seasonal simulation, and player-generated campaign worlds are not committed features of the initial scope.
