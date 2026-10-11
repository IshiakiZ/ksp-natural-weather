# Clouds of our own

Asked for on 2026-10-09, evening: "I want to do our own clouds with our own volumetrics and everything like we've been
doing", not EVE's. The bar is the user's for every effect: realistic, like the Volumetric Explosions (simulated, lit,
detailed), not smooth glowing shapes ("cartoony").

This page is the research behind them (brief, with sources) and how they are made. What was looked at in the game and
what it costs is at the end.

## How shipped games draw clouds in real time

Every game below draws clouds the same basic way: as a **volume stepped through by each pixel** ("ray marching"). A
pixel's line of sight is walked through a shell of air; at each step a density is worked out (where there is cloud and
how thick), and the light reaching that point is added, dimmed by everything in front. What differs is where the
density comes from, how the light is worked out, and how the cost is kept down.

* **Horizon Zero Dawn and Forbidden West** (Guerrilla Games, the "Nubis" system). Andrew Schneider's talks at SIGGRAPH's
  Advances in Real-Time Rendering are the reference everyone builds on:
  [2015](https://www.advances.realtimerendering.com/s2015/The%20Real-time%20Volumetric%20Cloudscapes%20of%20Horizon%20-%20Zero%20Dawn%20-%20ARTR.pdf),
  [2017](https://advances.realtimerendering.com/s2017/Nubis%20-%20Authoring%20Realtime%20Volumetric%20Cloudscapes%20with%20the%20Decima%20Engine%20-%20Final%20.pdf),
  [2022](https://advances.realtimerendering.com/s2022/SIGGRAPH2022-Advances-NubisEvolved-NoVideos.pdf),
  [2023](https://advances.realtimerendering.com/s2023/Nubis%20Cubed%20(Advances%202023).pdf).
  * *Where the clouds are*: a **weather map**, a picture over the world saying how much cloud, what kind and whether it
    rains. *What they look like*: a 128-cell cube of **Perlin-Worley noise** (smooth noise swelled by cellular noise,
    which gives rounded heaps) and a 32-cell cube of cellular noise for the edges, **eroded** by the cloud type's
    profile with height: flat sheets, heaps rounded on top, towers with anvils.
  * *Light*: **Beer's law** (light dies exponentially with the cloud it passes), a **"powder" darkening** of the edges
    facing the light (a cloud's surface has had little light scattered towards the eye yet; inner creases glow), a
    **Henyey-Greenstein** phase function for the **silver lining** (most light goes on nearly straight ahead), and six
    samples towards the sun in a widening cone plus one long one for each point.
  * *Cost*: drawn at a quarter of the size each way, a sixteenth of the pixels anew each frame, the rest carried over
    from earlier frames (**temporal reprojection**): about 2 ms on a PlayStation 4.
  * Forbidden West (2022, 2023) made the clouds a place to **fly through**, with voxel clouds and superstorms.
* **Frostbite** (EA; Sébastien Hillaire,
  [SIGGRAPH 2016](https://www.ea.com/frostbite/news/physically-based-sky-atmosphere-and-cloud-rendering)): an
  **energy-conserving** way of adding up the light over a step (exact for a step of uniform density, so the result does
  not change with the step length), and **multiple scattering approximated by octaves**: the sunlight is added again
  several times, each time dimmed less and spread more evenly, which is what makes thick clouds bright and soft instead
  of black. (The octaves are from Wrenninge, Kulla and Lundqvist, *Oz: The Great and Volumetric*, SIGGRAPH 2013.) A
  **shadow map of the clouds** darkens the ground and the fog under them. The atmosphere it sits in:
  [Hillaire, EGSR 2020](https://sebh.github.io/publications/egsr2020.pdf) (small precomputed tables of how sunlight
  comes through the air).
* **Unreal Engine** (Volumetric Cloud component, since 4.26:
  [documentation](https://dev.epicgames.com/documentation/unreal-engine/volumetric-cloud-component-in-unreal-engine),
  [properties](https://dev.epicgames.com/documentation/unreal-engine/volumetric-cloud-component-properties-in-unreal-engine)):
  a cloud layer round the planet with an altitude and a thickness, its shapes written as a material; ray-marched
  self-shadows, and "Beer shadow maps" for the shadow on the ground and on objects; drawn at a lower resolution and
  rebuilt over frames.
* **Red Dead Redemption 2** (Rockstar; Fabian Bauer, SIGGRAPH 2019,
  [course page](https://advances.realtimerendering.com/s2019/index.htm)): clouds, sky and fog as one system; ray-marched
  at low resolution and carried over frames; the same light also lights everything else through probes.
* **Microsoft Flight Simulator** (Asobo): volumetric clouds over the whole Earth from live forecasts (meteoblue). No
  technical talk was found; second-hand reports from the team's interviews speak of tens of layers and a view of
  hundreds of kilometres ([AVSIM thread](https://www.avsim.com/forums/topic/560392-avsim-podcast-with-msfs-team-help-me-list-all-the-info/),
  [80.lv](https://80.lv/articles/microsoft-flight-simulator-2020-redefining-game-production/)). Its players' complaints
  (Research.md) are the lesson: storms must stay storms.
* **Star Citizen** (Cloud Imperium): planet-wide volumetric clouds seen from the ground and from space, shown at
  CitizenCon 2021 and in its engine's demonstrations ([Wccftech](https://wccftech.com/star-citizen-gets-gorgeous-volumetric-clouds-but-squadron-42-is-still-mia/amp/),
  [Overclock3D](https://overclock3d.net/?p=265778)); no technical description was found. It is the closest case to
  KSP's: the same clouds have to hold up from a cockpit inside them to orbit.

## Cloud kinds and heights

The World Meteorological Organization's [International Cloud Atlas](https://cloudatlas.wmo.int/) sorts clouds by
height, in middle latitudes: **low**, from the ground to 2 km (stratus, the flat grey sheet; stratocumulus, a lumpy
layer; cumulus, the heaps; cumulonimbus, the storm, whose base is low but whose top reaches the top of the
troposphere); **middle**, 2 to 7 km (altostratus, altocumulus); **high**, 5 to 13 km (cirrus and its kin, thin and made
of ice). In the tropics the high level reaches 18 km. Cumulus bases are flat and all at one height on a fair day: the
height at which rising air starts to condense (Research.md, 4.1).

Kerbin's air thins faster with height than Earth's (its scale height is about 5.6 km against Earth's 8), so the heights
used here are a little lower than Earth's: fair-weather heaps from about 1 km to 2.5-3.5 km, towering heaps to 6 km,
storm towers to about 9-10 km, flat sheets from 0.6 to 2 km, wisps of high cloud at 8 km. They come from the weather
(CloudCell: base and top for each place), so the weather mod can change them.

## What makes clouds read as real

From the talks above and from looking at the sky:

1. **Light scattered many times.** Thick cloud is bright and soft, never black inside; its shaded side is grey, lit
   by the cloud round it.
2. **The silver lining.** With the sun behind a cloud its thin edges glow, because most light goes on nearly straight.
3. **The powder effect.** Lit from the viewer's side, a cloud's outermost edges are a little darker and its creases
   brighter.
4. **The sky's light and the ground's.** Shaded sides take the blue of the sky from above; undersides the colour of the
   ground below, faintly.
5. **Flat, darker bases**; thick and rain clouds dark underneath.
6. **Shadows on the ground**, and the dappled light of a field of heaps.
7. **Distance.** Far clouds fade into the haze of the air in front of them, and are lit by a sun reddened by the air.
8. **Sunset.** The tops stay lit, gold then pink, after the ground has gone into shadow; then the planet's own shadow
   climbs them.
9. **Movement.** Clouds drift with the wind and change shape slowly; they do not turn round the planet as a picture.
10. **Variety.** Heaps of many sizes in clusters and gaps, not a regular pattern.

## What was made

Natural Weather's clouds (`src/NaturalWeather/Clouds*.cs`; shaders `tools/shaderpack/clouds.glsl`, `cloudsover.glsl`,
`cloudshade.glsl`), built on what the talks above share:

* **Where the clouds are**: a weather map of the whole planet (256 by 128 dots, longitude across), made from the weather
  on another thread (`CloudMap`) when the clock has moved on half a minute, and at least every twenty seconds: the low
  and the middle layers (how much cloud, what kind, base, top, how wet), the high cloud and how stormy. The storms near
  the camera (up to six) are handed to the shader one by one, each a tower with an anvil, where the weather has it.
* **What they look like**: Perlin-Worley noise and cellular noise in two cubes (128 and 64 cells, repeating every 9 km
  and every kilometre), made once on the graphics card when the clouds are first drawn (no files); carved by each kind's
  profile with height and by the cover, the edges eaten by the small noise (a little at the flat base, cauliflower
  billows on the sides and top, at two sizes), the cells' own billows swelling the outline; a heap's density reaches
  its full a third of the way in from its edge (a real heap's edge is metres of mixing, not a hundred metres of mist);
  heaps lean with the wind. The heaps grow in clusters a few kilometres across with clear sky between, in wider patches
  of more and of less over tens of kilometres, and in rows along a steady wind of some strength (cloud streets, 3 km
  apart); a second reading of the noise at a scale that does not divide its repeat decides where the first one's heaps
  grow, and far off a third is taken in with it, so nothing repeats within 207 km (no lattice from orbit, no rows along
  the horizon). The noise drifts with the weather's wind and boils slowly. A thin sheet of high cloud in long fibres along
  the wind. Storm towers with larger billows than heaps' and less eaten by the small noise, an even anvil canopy
  spreading over the top and carried a little downwind; under a raining storm, its rain as grey curtains from the base
  to the ground.
* **Light**: the sun's light through the air to each point (a table per world worked out from the game's own air and
  the colours the game draws its sky with: a low sun comes through red, and the planet's own shadow ends it), through
  the cloud towards the sun (six steps, each longer, and one long look a few kilometres on), scattered once (mostly
  straight on: the silver lining) and a few times (octaves), and very many times over (diffusing: falling off by the
  two-stream rule, 1 / (1 + 0.11 depth), so a heap's shaded side is grey, its base darker, and a heap against the sun dark
  inside its rim), the powder darkening, the sky's blue from above (less on the flat base, greyed under a deck) and the
  ground's light from below; lightning from inside (below). The air between the camera and the cloud dims it and adds the light the air scatters, from the sun and from
  the whole sky (six steps). A soft shoulder keeps the brightest tops from clipping flat.
* **The game's two cameras**: KSP on OpenGL draws the far scene (from 400 m) with one camera and the near scene with
  another. The clouds are walked for each camera's own stretch, after the camera has drawn its own scene, so a craft
  stands in front of a cloud behind it and is hidden by one in front. The near walk is done only when a cloud can be
  within 400 m of the camera. (On Direct3D the game draws the whole scene with one camera; the clouds then walk it once.)
* **Cost kept down**: each walk is drawn at half the picture's size (a third for "lower"), with its steps begun at a
  different place each frame (each pixel's own, moved on by the golden ratio), and taken together with earlier frames
  (where each cloud was last frame is worked out from how far off it is; what is kept is held within this frame's
  neighbours, so nothing lingers); each step a hundredth of its distance from the camera, but at least two dozen across
  the shell (from orbit) and three at a time through clear air, going back when a long step finds cloud; where a step
  enters cloud, the edge found between it and the last clear one by halving twice and lit there (the visible edge of a
  dense heap is then where it is, not anywhere within a step of tens of metres); no further than
  90 km into the shell; the noise read more coarsely far off; the walk stops where the camera's own scene is in the way
  and once the cloud has hidden everything behind it.
* **Shadows**: a square of ground round the camera (50 km from the ground, more from high up) gets, for each of 512 by
  512 points, how much cloud lies towards the sun and between what heights. The sun itself is dimmed by the cloud above
  the craft (read back from that square at the start of each frame, eased over a second or so), so the craft and all
  round it are in the cloud's shade as a whole and its own shadows fade; then every pixel both cameras have drawn is
  put right for the cloud above its own place, darker or lighter than that, by the sun's share of the light on its face
  (none for a face turned from the sun). It is laid on straight after the solid things are drawn: Scatterer copies the
  picture after that and lays its haze on from the copy, which wiped a shadow laid on later out; its sea, drawn after the
  copy, takes no cloud shadow so.
* **EVE**: on a world where these clouds are drawn, EVE's cloud layer, its puffs and its cloud shadows are hidden (told
  not to draw, never destroyed; shown again when ours are off). With Scatterer installed EVE's shaders are named
  `Scatterer-EVE/Cloud`, `Scatterer-EVE/GeometryCloudVolumeParticle` and `Scatterer-EVE/ScreenSpaceCloudShadow`. Its city
  lights stay.
* **Windows**: the shaders are the mod's own. For Direct3D the same shaders are turned into HLSL
  (`tools/unityshaders/Assets/NaturalWeather/`, by `port.py`) and built into `PluginData/clouds-windows.bundle`, which
  the mod uses where it finds it: built on Windows on 2026-10-10 by `tools/unityshaders/windows.py` (Windows' own
  compiler, without the Unity editor), all three compiled for Direct3D 11 and taken by Direct3D itself (the clouds'
  walk is a program of 324,736 bytes). Seen in the game there the same day (KSP 1.12.5 on Direct3D 11, HarmonyKSP
  installed, a development build holding the weather): fair-weather heaps, a broken storm deck and an overcast, from
  the launch pad. Where the mod finds no shader it draws no clouds, says so on its page, and leaves EVE's clouds alone.
* **OpenGL with NVIDIA's driver** (Windows started with `-force-glcore`; Linux with an NVIDIA card is presumably the
  same): until 2026-10-10 the clouds drew nothing at all, and the game's log took `OpenGL Error: Invalid texture
  unit!` about six times a draw. The driver numbers a program's uniforms in the order of their names, and Unity
  cannot give a texture unit to a texture whose uniform comes 32nd or later: five of the clouds' maps (`_CloudMap`,
  B and C, `_CloudNow` and its depth) sort after most of the `_Cloud...` values, so they were left on unit 0 with
  the 3D noise, and OpenGL draws nothing where a 2D and a 3D texture share a unit. `tools/shaderpack/make_bundle.py`
  now gives every texture's uniform a location of its own where the driver takes that; packed again, the clouds draw
  there, with nothing in the log.

### Where the clouds come from

From `Keystone.Weather` (Keystone 0.7, the weather helper's interface: Natural Weather answers it), read on another thread
for the whole map: `At(body, latitude, longitude, ut)` gives three layers (low, middle, high: kind, cover, base, top, water),
rain, storm and how the low clouds drift. The low and middle layers are drawn as volumes (cumulus as heaps, towering when
tall; cumulonimbus as heaps building upward round the storms, kept short of towers so that the storms' own towers stand
out of them; stratocumulus and altocumulus as lumpy layers; stratus,
nimbostratus and altostratus as sheets); the high layer (cirrus) as the thin sheet of wisps; fog and dust are left to the
weather itself. `Cells(...)` gives the storms and showers near the camera, each drawn as a tower with an anvil where the
weather has it, so that the lightning comes out of a cloud that is there. The noise drifts with the low clouds' drift. With
no weather mod answering, a stand-in of the clouds' own (mostly fair weather, a few storms) is used instead, so that they
can be tried alone. (In the development build a stand-in weather can be held for pictures, and is then taken in place of
the weather mod's, for the clouds only.)

### Tuning, and what was found doing it

Most of the look was worked out with a small offline copy of the drawing (the shader itself, run with this Mac's OpenGL on
a plain sky, with the noise made exactly as the game makes it; a helper's scratch tool, not part of the mod), which can be
tried a hundred times while the one game is busy with the other helpers. What it showed:

* The noise's "heaps" channel lies between 0.74 and 0.92 nearly everywhere (measured over the whole cube), so a cover of
  0.4 meant cloud nearly everywhere: an overcast sheet. Spread to 0..1, a cover is a share of the sky again.
* Cover must not thin the cloud (as first written): a fair-weather sky then is thin smoke. Each heap is dense inside
  (light lost at 0.15 a metre in the thickest, reached a third of the way in from the edge), which also gives crisp edges.
* The small noise was read four times coarser than meant (a mistake in choosing its size by distance), and had little
  contrast: with both put right the heaps have cauliflower billows.
* Single and double scattering alone leave the base of a thick cloud black; light that has been scattered very many times
  falls off as one over the depth, not exponentially, and keeps bases grey.
* The air between camera and cloud must be lit by the sky as well as by the sun, or distant clouds go yellow (the sun's
  light having lost its blue on the way) instead of fading into the pale horizon.
* The 9 km noise repeats in rows seen from orbit: bent, and the cover made to wander, by the same noise read nine and
  twenty times coarser (not enough on its own: see the second pass below).
* Steps three times as long through clear air must step back when they find cloud, or the start of every cloud falls on
  a three-step lattice and shows from orbit as terraces round every storm.
* **On this Mac's OpenGL a texture of full (32-bit) floats with smoothing cannot be read: it reads as nothing, and slowly
  (55 ms for a walk that takes 3).** The air's table and the shadow's square are half floats.
* An OpenGL draw where a 2D and a 3D sampler share a texture unit draws nothing (Unity gives each its own unit; a test
  harness must too).

The second pass, for realism (after the first pictures read as smooth cotton balls), found:

* **Perlin's noise is nought at every point of its lattice.** Its four octaves, each twice the last's frequency with no
  offset, had their lattices on top of one another, so the sum was nought on the coarsest's lattice: a regular grid every
  2.25 km, which showed as rows of heaps along the horizon and as a lattice from orbit. Each octave is now moved off the
  others by a fraction of a cell.
* **A pattern repeating every 9 km still makes a lattice from orbit**, however its cover is varied over larger scales. A
  second reading of the noise at 20.7 km (a scale that does not divide 9) now decides where the first one's heaps grow,
  and beyond about 40 km a third at 10.35 km is mixed in: together they repeat only every 207 km.
* **The camera's place in the noise was wrapped round at each 9 km**, which nothing read at the noise's own scale notices,
  but everything read at other scales (the clusters, the bend that hides the repeat, the middle layer read 1.37 times
  finer, the rain's streaks, the high cloud's fibres) jumped whenever it wrapped: every 9 km in flight. It is now wrapped
  only every 207 lengths (1863 km), a whole number of every scale read; patterns read along the wind take a wind kept
  until it has turned more than 25 degrees (read hundreds of lengths from the start, the wind as it is would swing them
  about with every small turn).
* **Clusters read once every 1200 m along a line of sight must be smooth over that distance**: read from the full noise
  they jumped at each reading and the jumps showed as lines across the heaps; read from the noise's 16-cube they do not.
  Streets, 3 km apart, are read at every step.
* **More cover as a factor fills a layer from base to top in slabs** with flat tops and straight sides; as a power
  (cover to the 1/r) it thickens toward all of the sky but never to all of it.
* **Short steps at every cloud edge spend the steps a line may take**: lines that ran out before the far side of the
  heaps came out as horizontal stripes. Halving between the last clear step and the first cloudy one finds the edge as
  well for two looks at the noise.
* **Light diffusing through a heap**: with one over the depth falling off slowly (0.06 a unit of depth) heaps against
  the sun were pale and washed out; the two-stream rule's own figure (0.75 (1 - g), 0.11 for g 0.85) keeps them dark
  inside a bright rim, and the shaded sides grey.
* **Small noise across a storm's tower** at a heap's size makes it foam: a tower's billows are read at under half the size.

### Lightning

The weather mod decides when and where lightning strikes; the clouds only light up round it. Each flash it raises through
Keystone (`Keystone.Weather.Flash(WeatherFlash)`, heard here through the `Keystone.Weather.Flashed` event) lights the cloud
from inside for as long as it lasts: its place (latitude, longitude, the channel's bottom and top: lit from the cloud's
middle, or from its lower part for a flash to the ground), its brightness, and its strokes, each a bright instant dying
away in about 40 ms, about 70 ms apart (a little uneven, by the flash's seed). The brightest two are drawn at once. The
shader adds, at each step of cloud, the flash's light falling off with the distance (it spreads through the cloud by being
scattered over and over, `_CloudFlashA/B`, `_CloudFlashTint`). Nothing else is needed from the weather mod.
The channel itself is the weather mod's (Lightning.cs): seen from below the cloud's base, it is drawn when the clouds
call `Clouds.InFront` at the end of each camera's drawing of them, so that it stands in front of the cloud behind it
(drawn with everything else see-through, before the clouds, they were laid on over it).

## Looked at in the game, and what it costs

On the development build, from the test save's pad (late afternoon, mid-morning after 4 h 20 m of warp, sunrise after
3 h 19 m, night after 50 m), 3.6 km over the coast (paused), 1.9 km among the clouds, and a 110 km orbit, with the clouds'
stand-in weather (as it comes; heaps held; a storm held):

* **Mid-morning, from the pad**: fair-weather heaps over the space centre, the ground lit as it should be, distant heaps
  fading into Scatterer's hazy horizon.
* **From 3.6 km**: billowed heaps over the sea and the coast with their soft shadows on the shallow water and the sand; the
  land and mountains hazing away behind. (Scatterer's deep sea takes no shadow: it is drawn after the shadow is laid on.)
* **Among the clouds at 1.9 km**: heaps at eye level, their shadows on the sea below.
* **From orbit**: the ocean flecked with clusters of small heaps under Scatterer's glow.
* **A storm held**: a sky of towering heaps with grey bases.
* **Sunrise**: dark bodies with golden-lit edges, and Natural Light's sun shafts through the gaps.
* **Night**: the clouds are near-black shapes against the stars (no moonlight on them yet). With a flash of lightning held
  lit (a test switch: a real flash is gone in a tenth of a second, sooner than a picture is taken), the cloud masses
  across the sky light up grey from inside, with dark gaps between them.
* **Overcast held**: a grey deck, the craft read as having 0% of the sun, the scene without hard shadows. Its base came
  out darker and warmer than a real overcast; sheets were made thinner than heaps' cores after this (looked at offline
  only). Scatterer's distant haze stays clear-sky blue under it (Natural Weather's own grey light for the scene will help
  once the two are merged; a grey sky needs Scatterer itself told).
* **A live film** (4 s, the camera turning 15 degrees a second, heaps held): the clouds turn cleanly with the camera from
  frame to frame; no smearing or ghosts from the frames being taken together.
* **Cost**, the frame time with the clouds on and off, alternating (vsync lifted), six pairs of four seconds each, paused
  3.6 km up with both cameras' walks running, clouds filling much of the view and six storms near: **2.5 ms a frame**
  (25.1 against 22.7 ms). From the pad, unpaused, the game's own frame time wandered too much (43 to 64 ms) to measure
  anything finer than "a few milliseconds". Offline (the walk alone, this Mac, 640 by 360): 2 to 4 ms for a sky full of
  heaps, 1.6 ms for the near camera's walk inside a layer of cloud.

### After the second pass

Looked at again on 2026-10-10 in the same places, each view in a scene loaded afresh, with the clouds' stand-in weather held
as before (the weather mod's own taken in place of it for the clouds only where said), beside the pictures above:

* **Mid-morning, from the pad**: heaps of different sizes with flat grey bases and lumpy, ragged tops in place of the even
  white puffs; clear sky between clusters of them; the small heaps towards the horizon flat-based and uneven, no longer
  the same puff over and over in rows.
* **From 3.6 km**: a cluster of heaps with clear water round it, their tops billowed and shaded, their shadow on the sea
  and the sand; fewer and smaller heaps elsewhere.
* **Among the clouds at 1.9 km**: heaps at eye level with billowed sides and grey undersides, no longer smooth white
  blobs.
* **A storm held**: a grey sky of large masses, the scene in their shade (the craft read as having none of the sun); the
  storms' towers stood outside these views (the lab's look at a storm 12 km off shows the tower with its anvil canopy).
* **Sunrise**: the lit sides of the heaps golden with billows on them; the dark masses against the sun less dramatic than
  before (their bright rims are thinner now that the heaps' edges are sharp).
* **From orbit**: over land the heaps lie in irregular clusters and gaps of different sizes, with no lattice; with the
  weather mod's own weather, whole regions clear and bands of cloud elsewhere.
* **Cost**, measured as before (paused 3.6 km up, both cameras' walks, six storms near; six pairs of four seconds, the
  clouds on and off alternately, vsync lifted): **2.9 ms a frame** (19.9 against 17.0 ms; 2.4 ms leaving out one slow
  frame-time reading). Offline the walk alone costs about half as much again as before the pass (the edge search, a second
  reading of the noise, more cloud being drawn); the game's own measurement is the one to go by.

