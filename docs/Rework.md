# The rework: towards clouds and weather that look real

Asked for on 2026-10-10: the weather mod reworked, "as realistic as possible", its volumetric clouds "super realistic"
and "super beautiful", after research into cloud patterns and how, why and where weather forms. This page is that
research and the plan it leads to, and it is added to as the work goes. What the mod is today is in
[How it works](How-it-works.md) and [Clouds](Clouds.md); the research it was first built on is in
[Research](Research.md).

## Where the clouds fall short today (looked at 2026-10-11, in the game and in the offline lab)

Pictures: `~/.ksp-ai-bridge/pictures/sky/axes/same_sheet.jpg` (the game, midday) and
`~/.ksp-ai-bridge/pictures/sky/rework/base_sheet.jpg` (the lab: four views).

1. **The heaps are pancakes.** A fair-weather heap here is two to five kilometres across and a few hundred metres
   thick. A real cumulus is about as tall as it is wide (humilis half as tall, mediocris as tall, congestus twice as
   tall). From the ground one therefore sees mostly grey base and a white rim, where a real one shows a brilliant
   sunlit flank over a grey base; from above they lie like pads of cotton wool, with no towers and no relief.
2. **Smooth lens shapes and confetti.** Large masses have smooth, featureless undersides and outlines; round about
   them float hard little crumbs. A real heap's growing top is crisp cauliflower (turrets 100 to 300 m across); its
   evaporating sides and base are ragged and soft; there are no hard specks.
3. **Flat light.** Lit and shaded parts differ too little: the sunlit side should be near white (a thick cloud throws
   back 70 to 90% of the sunlight), the shaded side a third to a fifth of that and bluish (lit by the sky and by what
   diffuses through), the base darker the thicker the cloud.
4. **No colour at a low sun.** With the sun seven degrees up the heaps are grey and white; real ones are cream, then
   gold, orange and pink as the sun's light crosses more air, with blue-grey shade.
5. **Few kinds of cloud.** Heaps, a sheet, a storm tower, a high veil. Missing: fields of stratocumulus (rolls and
   cells with gaps), altocumulus (small regular heaps in ranks, a "mackerel sky"), the grey layers of a front
   (altostratus, nimbostratus), cirrus in its kinds (hooks, streaks, a milky veil with a halo), a storm's shelf,
   mammatus and pileus, fog as a bank with a top.
6. **The weather's large shapes** are smooth blobs of moisture. Real weather is organised: fronts with their
   sequence of cloud, the comma of a depression seen from orbit, the band of storms along the equator, streets of
   heaps along the trade wind, decks of stratocumulus over cold seas, heaps over land by day and clear nights.

## What real clouds do (the research)

**Where the base is.** Air rising from the ground cools about 9.8 K a kilometre, its dew point about 1.8: it
condenses where the two meet, the lifting condensation level, about 125 m for each degree between the temperature
and the dew point at the ground (Espy's rule). All the heaps over one stretch of country therefore have their bases
at one height, flat, and that height rises through the day as the air dries (600 to 1200 m on a humid morning, 1500
to 2500 m on a dry afternoon; lower over the sea). [Stanford FAM book, ch. 9](https://web.stanford.edu/group/efmh/jacobson/FAMbook/Chap9.pdf)

**How a heap is built.** Below the base the rising air is a steady plume; above it, a succession of bubbles
(thermals), each a turret that rises, mixes with dry air at its top and sides (entrainment) and evaporates there
while the next rises through it. So: a heap is widest at its base or just above, narrows upward by lumps, has crisp
rounded turrets where air is rising and ragged, thinning edges where it has stopped; it leans downwind with height;
it lives ten to forty minutes. Drier air round it gives thinner, more ragged, shorter heaps; humid air, fat
towers. [Theory and simulation of shallow cumulus, arXiv 2004.09631](https://arxiv.org/pdf/2004.09631);
[ARM: plumes or thermals](https://arm.gov/research/highlights/1020)

**How big, how many.** Sizes follow a power law: very many small heaps, few large ones (the number falls off roughly
as the size to the power 1.7 to 2 up to a kilometre or so, faster beyond), not one typical size. They stand in
clusters and lines with gaps several heaps wide; along a steady wind of 5 m/s or more in rows two to four times the
depth of the stirred layer apart (cloud streets); over land they follow the sun (first at mid-morning, most and
tallest mid-afternoon, gone by dusk); over the sea they hardly change with the hour.

**The kinds, by what makes them.**
- *Heaps* (cumulus humilis, mediocris, congestus): bubbles from a warmed surface into unstable air; the depth of
  the unstable layer sets how tall. Capped by a warm layer they spread out under it into *stratocumulus*.
- *Storm cloud* (cumulonimbus): a heap that reaches the tropopause, where it spreads into a smooth anvil of ice
  tens to hundreds of kilometres across, carried downwind; hard cauliflower flanks while it grows, fibrous once it
  has turned to ice; under the anvil sometimes pouches (mammatus); ahead of the rain a low dark roll or shelf where
  the cold outflow lifts the warm air; a smooth cap (pileus) on a fast-growing turret.
- *Stratocumulus*: a shallow stirred layer under an inversion, a kilometre or two up: rolls or cells one to a few
  kilometres across, grey with pale thin places or gaps; over cold seas in sheets hundreds of kilometres wide,
  honeycombed (closed cells: cloud with clear rims; open cells: clear with cloudy rims, where it drizzles).
- *Altocumulus and cirrocumulus*: the same in a thin layer higher up (2 to 7 km; 5 to 12 km), so the cells look
  small: ranks and ripples, each element one to five degrees across, or under one degree for cirrocumulus.
- *Layer clouds of a front* (cirrostratus, altostratus, nimbostratus): air climbing a gentle slope over hundreds of
  kilometres: featureless sheets, thickening and lowering.
- *Cirrus*: ice falling from small generating heads into drier, slower air below: streaks and hooks tens of
  kilometres long, drawn out by the change of wind with height.
- *Fog and stratus*: air cooled from below (a clear calm night: radiation fog in hollows, a few to a hundred metres
  deep, gone by mid-morning; warm damp air over a cold sea: advection fog, in banks that come ashore).
- *Hill and wave clouds*: air lifted over high ground: a cap on the peak, smooth lens shapes downwind standing still
  while the wind blows through them.

**Fronts.** A warm front is a slope of about 1 in 150: its high cloud runs ahead of it by many hundreds of
kilometres (up to a thousand), so one sees hooks of cirrus half a day before, then a milky veil (a halo round the
sun), then a lowering grey sheet through which the sun shows as through ground glass, then thick rain cloud with
steady rain beginning some 300 km ahead of the front and lasting hours. A cold front is steep (1 in 50 to 1 in
100): a narrow band, tens of kilometres, of tall heaps and storms with short heavy rain, then quickly clearing sky
with bright heaps and showers in colder, gusty air. Seen from orbit the two wrap into a comma round the low, a
thousand to two thousand kilometres across, its tail the cold front. [UBC ATSC 113: stratiform
clouds](https://www.eoas.ubc.ca/courses/atsc113/flying/met_concepts/01-met_concepts/01a-clouds/stratiform.html)

**Where on a world.** A band of storms and towering heaps where the trade winds meet near the equator (the ITCZ),
moving with the sun; steady small heaps in the trades; clear, dry belts near 30 degrees with decks of stratocumulus
over the cold eastern sides of oceans; travelling lows and their fronts from 40 to 60 degrees; thin ice cloud and
low stratus near the poles. Coasts get a line of heaps a few kilometres inland by day (the sea breeze meeting the
land's air); mountains get heaps and storms first.

**What makes a picture of cloud read as real** (beyond the ten points in [Clouds](Clouds.md)): the sunlit side
nearly white against a sky two to three stops darker; hard detail where the cloud is growing and softness where it
is dying, in the same cloud; bases at one level receding to the horizon in perspective; many sizes; shadow of one
cloud on another and on the ground; haze between far clouds and the eye; colour from the sun's path through the air.

## How the best real-time clouds are drawn (what was added to the first research)

- Guerrilla's **Nubis** line of talks is still the reference: 2015 and 2017 (a weather map and height profiles,
  Perlin-Worley shapes eroded by Worley detail), 2022 "Nubis, Evolved" (2D fields of modelling instructions
  decompressed into a "dimensional profile": bottom, top and edge gradients multiplied; the base shape *remapped* by
  it, never multiplied, so the core keeps its density; detail that is wispy low down and billowy high up; about
  2 ms a frame), 2023 "Nubis cubed" (true voxel clouds one can fly through, 0.2 to 5 ms).
  [Nubis, Evolved](https://www.guerrilla-games.com/read/nubis-evolved);
  [Nubis cubed](https://www.guerrilla-games.com/read/nubis-cubed);
  [2017 slides](https://advances.realtimerendering.com/s2017/Nubis%20-%20Authoring%20Realtime%20Volumetric%20Cloudscapes%20with%20the%20Decima%20Engine%20-%20Final%20.pdf)
- **Light**: Hillaire's energy-conserving integration of each step; Wrenninge's octaves for light scattered more
  than once (each octave carries further, spreads more evenly, counts for less), as Frostbite and Unreal sum them;
  a two-lobed phase function (or Jendersie and d'Eon's fit to real droplets) for the silver lining and the glory;
  the sky's light as ambient, by height. Unreal's notes: one octave is what games afford; high contribution with
  low occlusion imitates more; the octaves lose a little energy, which is made up by a scale.
  [Unreal's volumetric cloud component](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-cloud-component-in-unreal-engine);
  [P. Ford, real-time clouds (University of Washington, 2024)](https://digital.lib.washington.edu/researchworks/bitstreams/fe31500e-8547-4688-95ee-f08e7e096d19/download)
- All of that the mod already does in outline (see Clouds). What it lacks is not a technique but **modelling**: the
  shapes fed to the technique are not the shapes of clouds.

## The plan

1. **Heaps that are heaps** (first): about as tall as wide, domed, built of turrets; sizes by a power law; one flat
   base height over a region; crisp where growing and soft where dying; no crumbs. Humilis, mediocris and congestus
   from one field ("how unstable"), which the weather already has (the sun's warming and the moisture).
2. **Light**: white sunlit flanks, blue-grey shade, bases darker with thickness; gold to pink at a low sun; the
   shadow of cloud on cloud; shafts of sunlight between heaps (Natural Light's sun shafts can take the clouds'
   shadow square).
3. **The other kinds**: stratocumulus cells and rolls; altocumulus ranks; front layers with the sun as through ground
   glass and a halo in cirrostratus; cirrus hooks; the storm's anvil of ice, shelf, mammatus, pileus; fog with a top.
4. **The weather's shapes**: fronts with their sequence across hundreds of kilometres and a comma seen from orbit;
   the equatorial band; trade-wind streets; decks over cold seas; the land's day; coasts and mountains.
5. **From high up and from orbit**: the same clouds, their shadows, the planet's limb.
6. Then the cost on this Mac and on Windows, the Direct3D build, and pictures of each kind beside photographs.

**Tools:** `tools/shaderpack/cloudlab.swift` draws the shader outside the game in two seconds a picture (views in
its header); the game for everything it lacks (the sky, the depth picture, the weather).

## The user's verdict, and the direction it sets (2026-10-11, about 00:40)

Shown the clouds as they were and the first changes: "the clouds currently don't look that realistic and look more
comical, like anime clouds, and they don't look that volumetric", and then: **"I don't think they look realistic
because of how they're made. Our volumetric explosions: the smoke looks almost real."**

That is right, and it decides the method. The explosions' smoke is a small simulation: thousands of puffs carried by
a model of the air (rising, rolling, mixing in clear air), gathered into a grid and lit through it, with a billow
pattern that travels with the smoke. Its shapes come from motion. The clouds are noise cut off at a threshold: their
shapes come from arithmetic, and tuning the arithmetic (below) softens the drawn look without removing its cause.

**So: clouds made as the smoke is made.**
- A heap near the camera (within some kilometres) is built of **thermals**: puffs released at the condensation
  level over a warm patch, rising, widening as they mix in dry air, evaporating at their edges, each turret a new
  bubble through the last. The explosions' engine already does all of that for smoke (patches of air, puffs, a
  64-cell grid a patch, the volume shader's light); Rocket Smoke shows another mod can feed it. What it needs for
  cloud: patches kilometres across (coarser cells, the shader's billows carrying the finer scales), white droplets
  in place of soot (no absorption, strong forward scattering, the many-times-scattered light that makes cloud white),
  evaporation in place of thinning, and a slow clock (a heap lives ten to forty minutes: its puffs can be moved a
  few times a second, on another thread).
- The noise clouds stay for what is far (beyond the simulated heaps, to the horizon) and from orbit, and for sheets
  and veils, which have no turrets; the two must match in colour and light where they meet.
- First trial to make: one heap of thermals over the pad in the explosions' development build, photographed beside
  the noise heap and beside a photograph. If it reads as real, the rest follows from it.

## Done so far

* 2026-10-11: the faults listed above; first trials in the lab of taller, smaller heaps (the large noise read 1.7
  times finer for heaps, the height profile `1 - h^2.2` in place of a straight ramp): the field becomes many small
  heaps with flanks; close large ones turn to smooth bubbles (they need turrets) and the crumbs remain. Not yet in
  the shader.
* 2026-10-11, later: in the shader and the mod (seen in the game on one held sky, before and after:
  `~/.ksp-ai-bridge/pictures/sky/rework/game_soft.jpg`): heaps read 1.45 times finer and full nearly to a domed top
  (`1 - h^2.6`), turrets from the finer cells, the crumbs let go; the cloud a metre takes 0.07 of the light where it
  took 0.15, its density full less abruptly (1.7 for 3.0); the light scattered a few times carrying most of the sun
  and falling off through the cloud, the diffused floor a third of what it was; the sky's light less in the dense
  middle; a stronger powder. The heaps have flanks and shade inside them now. Still wrong: large ones are smooth
  (no fine turrets), there is more cloud than the weather asks for (the fuller profile: the cover wants setting
  again), far heaps show level bands, an overcast and a storm were only glanced at (55, 54 and 46 frames a second
  for fair, overcast and storm at the pad). An interim look, installed as 0.2.3, until the thermals replace it.
* 2026-10-11, 00:45: **the first trial of cloud made as smoke** (`Air.Thermals`, a development switch in the
  explosions: seven sources of white "steam" 45 m wide hung 500 m over the pad, as things that smoulder; pictures
  `~/.ksp-ai-bridge/pictures/sky/rework/thermal_sheet.jpg`, at 25, 60 and 110 s). What came out: a cluster of soft
  round balls with fuzzy edges and gentle shade, standing still. As stuff it reads as real in a way the noise never
  has (the edges, the shading through it); as a cloud it is nothing yet, for nothing makes it rise, mix and boil,
  and its underside takes a brown from the smoke's own colouring. It cost too much as it was: 6,300 puffs at 25 s,
  22,600 at 110 s, the frame rate from 38 to 29.
  **What the engine needs for a heap** (the next piece of work): a source kind of its own, "thermal": puffs born
  over a disc at the base height, rising at 3 to 6 m/s and widening as they mix (a fifth of the height risen),
  slowed and spread at the top of the unstable layer, evaporating from the outside (a life of minutes, not a
  thinning into haze); far fewer and larger puffs (hundreds, each tens of metres), the shader's billows carrying
  the finer scales; cloud's light (white, nothing absorbed, the many-times-scattered light, the sky's blue in the
  shade, no warm tint); a patch of air a kilometre or two across. Then: where the weather says heaps, the nearest
  few are made so, and the noise draws the rest.
* 2026-10-11, 01:10: **a heap of thermals, and it reads as cloud.** The explosions' engine was given a source kind of
  its own (`ExhaustKind.Thermal`, Volumetric Explosions 0.6.3): puffs born over a disc at the base, thrown up at 2.5
  to 6 m/s and slowed so that they stall having climbed about the cloud's depth (the middle highest: a dome), wider
  by an eighth of how far they have climbed, given at birth the thickness they will need once widened (water goes on
  condensing in rising air), a few large puffs a second, gone after two and a half minutes. The trial
  (`Air.Thermals`: one thermal 110 m across and 380 m deep, four lesser round it, 450 m over the pad; pictures
  `~/.ksp-ai-bridge/pictures/sky/rework/thermal2_sheet.jpg`, at 25, 60 and 110 s): a soft dome that grows upward
  over two minutes, its top and rim bright in the sun, its body shading to the underside, with a real volume's
  edges. 540, 1,300 and 2,400 puffs; 61 to 63 frames a second throughout (the game's cap).
  **Wrong in it:** the underside is orange-brown (the smoke's light from the ground: a cloud's shade is the sky's
  blue-grey); its base is round, not flat (it wants a wide layer of puffs that do not climb, at the base height);
  faint upright streaks in its flank; one turret's worth of shape (a heap is several, rising in turn, the old ones
  evaporating from their edges); it stands still (no wind was set).
  **Next:** those four; then the weather's heaps near the camera made so, and the noise for the rest.
* 2026-10-11, 01:50: shown the first thermal heap, the user: **"that cloud looks so terrible. Are you telling me that
  looks like a real cloud?"** It did not: a smooth blob with an orange underside, which I had described as if it
  nearly did. What it lacked is what makes the explosions' smoke read as real: thousands of small puffs over one
  another. **Second trial** (`thermal3_sheet.jpg`): a thermal is now 6 to 24 small puffs a second (a tenth to a sixth
  of its width across), stalled at every height up to the dome, a third of them left in a level layer at the base;
  patches fed by a thermal are marked as cloud and given the sky's blue-grey as their light in shade. What came out:
  a cumulus's lumpy outline and a flatter base, 6,800 puffs at 110 s and 51 frames a second; but each puff shows as a
  ball ("popcorn"), and the underside is still orange. Not the scene's lamps (they light smoke only at night), and
  the change to the patch's sky light made no difference to it: where the orange comes from is not yet found (the
  grid's light from what is around it, perhaps, or the patch not being marked). Not good enough to show as a cloud.

## What Solas does (the Minecraft shader pack the user likes; studied 2026-10-11)

"If you look up and download Solas shaders from Minecraft, they have volumetric clouds, and I really like how they do
it." Solas Shader 3.7b by Septonious (Modrinth; all rights reserved: its method is described here in our own words
and none of its code is used). Its clouds, `lib/atmosphere/volumetricClouds.glsl`:

- **A cloud is a column, not a blob.** One flat noise (two sizes of it, with a curve for contrast) says where there
  is cloud; where that noise is strong the cloud's own top is higher. Up through the layer the cloud is thickest an
  eighth of the way up; below that it is cut off steeply (a flat base) and above it thinned gently to that local top
  (a dome). A little finer noise, read in slices stacked up the layer, roughens it.
- **Never solid at once.** What is left after the cover is taken off is squeezed through a soft curve
  (n / sqrt(n^2 + c)): the edge of a cloud is a gradient however dense its middle.
- **Light that is cheap and reads as volume.** One more reading of the cloud a step towards the sun dims the sun's
  light (and a share of light straight through near the sun's direction); a "powder" term darkens what is thin; and
  the light of the sky is simply **how high in the cloud the point is**: dark at the base, bright at the top. Lit and
  shaded parts are then two colours mixed, the sun's and the sky's, and the whole fades into the air's colour with
  distance.
- Why it pleases: soft edges, flat dark bases under bright rounded tops, warm light against cool shade, haze with
  distance. None of it is costly or exact.

**What to take:** the sky's light by height in the cloud (base to top), the soft curve in place of a hard limit,
tops that are higher where the cloud is bigger, the two-colour mix, and the fade into the air: for the noise clouds
now (they are the whole sky), and as the standard the thermal clouds' light must meet.

* 2026-10-11, 02:00: three of Solas's ideas put into the noise clouds (0.2.4, seen in the game on the same held sky as
  before, `~/.ksp-ai-bridge/pictures/sky/rework/game_024.jpg`, 59 frames a second): density through a soft curve in
  place of a limit; the sky's light by how high in the cloud (a fifth at the base, full from two fifths of the way
  up); what diffuses down through the cloud halved at its base. The heaps have darker bases under bright tops and
  more depth. They are still smooth lumps, and there is still more cloud than the weather asks for.
* 2026-10-11, 02:35: **the orange found, with the studio.** The cloud alone on a grey backdrop (Photo Studio's
  Exhibit), drawn through the smoke shader's test views: the sun's light alone, the sky's alone and the cloud's own
  colour were all neutral; what was left was the fire's light, a lamp every source of smoke is given, here inside the
  cloud. Thermals get none now. The underside measures 125, 133, 146 (blue-grey); against the sky and alone on blue
  from two sides (`~/.ksp-ai-bridge/pictures/studio/c4_sheet.jpg`) the heap reads as a small cumulus from a distance:
  white, lumpy, soft-edged, grey beneath. Close to, its puffs still show as balls, there is a dark patch at its base,
  and it ran at 37 frames a second with 5,900 puffs.
* 2026-10-11, 02:45: **billows the size of the puffs.** A patch's billow pattern repeated every 1.5 to 10 m (a
  flame's, a trail's): finer than a pixel from where a cloud is seen, so each puff showed as a ball. For a patch of
  cloud it now repeats every 0.9 of the thermal's width (20 to 160 m). The heap (`dist/progress/clouds-thermal-heap-3.jpg`:
  against the sky, and alone on blue from two sides) has an irregular billowy surface, wisps at its edges, turrets
  and a grey base, and no balls: the first that stands being looked at as a cloud. 5,900 puffs, 35 frames a second
  (too slow: fewer puffs, or moved less often, next); one heap only, standing still, not yet the weather's.
* 2026-10-11, 03:30: **the weather's own heaps made this way, as a trial (off unless turned on).** `Heaps.cs`: where
  the weather has fair-weather heaps, the three nearest the camera (squares of 1.1 km, a heap in a square or not by a
  number worked out from where it is, more of them with more cover; within 2.4 km) are each a main thermal and three
  lesser round it whose strength rises and falls in their own time, fed to Volumetric Explosions 0.6.3; the drawn
  clouds are cleared from where they stand (`_CloudHoleA/B/C`). What it took in the explosions' engine: a thermal says
  how far its patch of air reaches (`Exhaust.reach`), so one heap is one patch and one grid (the first run gave each
  of twelve thermals a patch of its own, more than there may be at once, so that patches were begun and let go at
  every frame: 10 frames a second and a few balls); a cloud's patch takes thermals only, pushes nothing else out and
  gives way first; and its grid is made three times a second, not twenty-five (handing grids to the graphics card was
  4 ms of every frame, now 0.6).
  **What it costs** (the same held sky, the camera held on a heap with `tools/studio.py watch`, off and on): from the
  pad 57.7 and 51.7 frames a second; a heap filling the view 60.6 and 46.0, and 50.7 after the grids were made less
  often; about 8,000 puffs in three patches.
  **What it looks like** (`dist/progress/clouds-weather-heaps-trial.jpg`: the heap in the middle is made this way, those
  round it are drawn): a real billowy surface, its own shade inside it, a grey underside; far nearer a cloud than the
  drawn ones beside it, which look like smooth clay. **What is wrong with it:** it is soft (the explosions' smoke is
  drawn at half the picture's size) and has a bright glow round its sunlit top; from underneath it is a rounded lump,
  not a level base; a level line of the drawn clouds' base runs across it and bits of drawn cloud show in front of it
  (two things drawing clouds each lay theirs over the other's, whichever is nearer); it takes two minutes to fill
  out; and the sky is then of two kinds, three clouds of one and the rest of the other. So it stays a trial.
  **What it taught, and the direction from here:** what makes that heap read as a cloud is (1) a shape built of lumps
  in three dimensions, lumps on lumps, with overhangs, where the drawn clouds are a height above each point of the
  ground; (2) billows eaten into it at the size of a tenth of the cloud, crisp, with their own shading; (3) thick
  enough to have a surface. All three can be had in the clouds' own shader for the whole sky at once: shapes made
  beforehand as that heap is made (lumps upon lumps, or a real simulation of rising moist air) and kept as a block of
  numbers the shader reads, in place of the height-above-the-ground shapes. That is the next step.
* 2026-10-11, 04:10: **heaps made beforehand, read by the clouds' own shader (0.3.0): the first sky that reads as
  a sky of cumulus.** What the smoke-made heap taught, done for the whole sky at once:
  - `tools/clouds/heapfield.swift` makes a square of sky that repeats (6.4 km a side, cells of 25 m, 64 levels from
    base to top): some three hundred heaps, many small and few large (numbers falling off as the size to the power
    -1.9), none nearer its neighbours than they are wide; each a few turrets on a level base, a turret a dome with
    bulge upon bulge above it, each smaller and a little to one side; lumps a third to a half as large on every
    bulge, and lumps on those. Kept as three numbers a cell: how far inside a heap's surface the cell is, less a
    number of the heap's own (so that the level the shader picks from the weather's cover decides which heaps there
    are and how far grown: cores first, then each to its full lumpy self, then its neighbours); the most that number
    reaches within two cells (for striding through clear air without stepping over a heap); and how much of the sky
    the cell sees (for the sky's light). `src/NaturalWeather/PluginData/heaps.field`, 14 MB.
  - The shader (`heapsAt` and the made part of `layerAt`) reads it for heaps of every depth from fair-weather ones to
    towering ones, by east and north over the ground (in bands of latitude, each with its own scale east-west), five
    sizes for layers of different depths so that heaps keep their proportions, lumps of 50, 25 and 12 m carved at the
    pixel, an edge of nine metres. Sheets and storms' towers are still carved from the noise.
  - **Faults found on the way, all of which were in the old clouds too or would have been:** the walk's long strides
    through clear air stepped over small heaps for whole rows of the picture (far heaps cut into level bands: the
    "level bands" noted of the old clouds; now the stride asks the field's second number and cannot miss); what
    changes over kilometres was held between readings and jumped at each (now taken between two readings); the
    light's steps towards the sun were always read at their middles, so a heap with a surface was shaded in flat
    bands like a drawing (each now read at its own place, a different one each pixel and frame); the graphics card's
    compiler worked out both ways of making heaps at every place, and the weather's nine readings at every step
    (each now in a loop that runs once or not at all: the walk went from 30 ms to 12 for a picture of 1280 by 720 in
    the lab); the packer's list of the shader's values did not have the `_CloudHole` values of the trial above, which
    were therefore never set (the checker now refuses a value the packer does not list); read into a texture of two
    numbers a cell the whole field came out as one slab (four numbers a cell now).
  - **Seen in the game** (`dist/progress/clouds-before-after-0.3.0.jpg`: the same held fair sky from the same place,
    each in a scene loaded afresh; `~/.ksp-ai-bridge/pictures/sky/field/g8_*.jpg`, `g9_kinds.jpg` for every kind of
    held sky): heaps with level grey bases and lumpy tops, of every size, back to the horizon; 60 frames a second
    from the pad and 52 looking up into them (the noise's heaps: 58 and 60).
  - **What is still wrong with them, plainly:** the lumps are all of a size, like popcorn, and the sunlit sides are
    nearly one white (a real heap has broad smooth faces with small crisp lumps at their edges, and shade inside
    its folds); edges are a little woolly; towards the horizon the heaps crowd into a speckled white band; the
    storm's tower and the sheets, still carved from noise, now look pale beside them (the sun's light on cloud was
    brought down by more than half for the heaps); nothing has been looked at from high up or from orbit, in
    motion, at sunrise, or on Windows (whose shader has yet to be built from this).
* 2026-10-11, 05:40: **the rest of that night's work on the heaps, in the order it was done** (all in the game,
  `dist/progress/clouds-0.3.0-*.jpg`):
  - *A surface shaded by the way it faces.* A heap's sunlit side was one flat white: the light at a place was worked
    out from the depth of cloud towards the sun alone, and every part the sun reached at all was as bright as every
    other. The field's own number falls by a metre for every metre straight out through the surface, so its change
    over the light's first step towards the sun says which way the surface faces; what is scattered many times is
    weighed by that (full sun facing it, a third turned away). The heaps have form: lit sides, shaded sides, lumps
    that catch the light.
  - *Cover as asked.* Measured from straight above over 37 km by 21 in the lab: 0.12, 0.29, 0.48 of the ground hidden
    for 0.1, 0.3, 0.5 asked (the noise's heaps gave 0.03 and 0.15 for 0.2 and 0.4).
  - *Shapes.* A turret was a dome on a straight column (tin cans, seen from beside or beneath); it is bulge upon
    bulge now, each smaller and off to one side. A layer's depth stretched the field (pillars in a deep layer): the
    field is read at five sizes, each 1.6 times the last, so that no heap is stretched by more than 15 hundredths or
    pressed by more than three tenths, the sizes giving way to one another over an eighth of their range.
  - *The storm.* Its tower and anvil were carved from noise, a smooth blue-grey mushroom. Now: a tower of rising air,
    widest at its foot, swelling and shrinking with the large noise, the field's heaps crowded on it and round it
    (flanking towers), lumps carved on the lot; an anvil thickest round the tower, hanging lower there, ragged at
    its rim. From 6 km up beside it and from the ground it is a thundercloud. Still too even a cone, and the anvil's
    underside is featureless.
  - *Layers.* The lumpy low layer and the middle clouds are the field's heaps too, pressed flat under a shallow top
    with most of the sky covered (the middle clouds read the field turned and moved over): a quilt of grey-based
    cells with blue between, and a sky of small cloudlets, where there were smooth smears.
  - *From orbit.* The field, 6.4 km laid side by side, showed as the weave of a cloth. From so far that a heap is
    smaller than a pixel (beyond 40 to 80 km from the ground; all of it from orbit) the noise's heaps stand in,
    asked for as much as gives the cover wanted (0.07, 0.15, 0.38, 0.69 measured from 110 km for 0.1, 0.2, 0.4, 0.6).
  - *The walk, faster and reaching further.* Every source of cloud says how near it can be to the place asked about
    (the field's number is a distance; a storm's tower and anvil; a layer above or below), and the walk strides
    through clear air by that, each stride of its own length. A line of sight that began inside the clouds' layer
    used to spend its 88 steps of 40 m on clear air and stop 3.5 km out (sky where a storm's anvil should have
    been). The six storms were written out one after another, six copies of a storm's code, which cost a sky with
    no storm in it two thirds of its time on this graphics card; they are walked in one loop. The light's walk
    leaves off where the cloud towards the sun is already deep. In the lab, a picture of 1280 by 720: fair sky 7.8 ms
    (12 before the storm's rebuild, 4.7 for the old noise heaps, which drew a third of the cloud), deep layer 11.6,
    with a storm 25. In the game, the camera held looking up into the clouds at midday: fair 57 frames a second,
    heaps 55, towering 54, storm 42, lumpy layer 59, middle clouds 48, overcast 53 (the old clouds: 60 for heaps).
  - *Natural Light's eye.* Under a storm with sunlit towers round it, and whenever one cloud's shadow crossed the
    pad, the eye opened as for an overcast ("the bright sky notwithstanding", as written) and every heap went to
    plain white. It also read the picture before the clouds were drawn into it. It reads it after them now, and
    opens under cloud only as far as the whole view can bear.
  - *Looked at:* round the pad at midday in every kind of held sky; from 2.4 km and 7 km up; through an evening
    (sun at 17, 7, 2 and 0 degrees, towards it and away); live frames with the camera still (mean change from frame
    to frame 0.55 of 255); from 8, 30, 45 and 110 km in the lab.
  - **What is still wrong, plainly.** Lumps of one size on every heap (popcorn), where a real heap has broad smooth
    faces and small crisp lumps at their edges; heaps far off are thin enough to show the ground through; the
    storm's tower too regular and its base mid-grey where a real one is slate; at sunset the heaps go khaki, not
    orange and pink; night clouds are flat grey shapes; the overcast and rain sheets are still the noise's; nothing
    on Windows (the Direct3D shader must be built from this on the PC; the field loads by plain file reading and a
    four-number texture, which should work there, untried); the trial of heaps made as smoke is unchanged and off.
