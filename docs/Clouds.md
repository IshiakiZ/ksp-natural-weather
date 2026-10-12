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
  Since 0.2.2 the map is asked of the weather's atlas of the world (below): about a third of a second of that thread
  for Kerbin, where it was 3.4 seconds and more, with no clouds in the sky until it was done.
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
  straight on: the silver lining, for which the light that goes straight on is dimmed by a tenth of the cloud it
  passes: see 0.2.2 below) and a few times (octaves), and very many times over (diffusing: falling off by the
  two-stream rule, 1 / (1 + 0.11 depth), so a heap's shaded side is grey, its base darker, and a heap against the sun dark
  inside its rim), the powder darkening, the sky's blue from above (less on the flat base, greyed under a deck) and the
  ground's light from below; lightning from inside (below). The air between the camera and the cloud dims it and adds the light the air scatters, from the sun and from
  the whole sky (six steps). A soft shoulder keeps the brightest tops from clipping flat.
* **The game's two cameras**: KSP on OpenGL draws the far scene (from 400 m) with one camera and the near scene with
  another. The clouds are walked for each camera's own stretch, after the camera has drawn its own scene, so a craft
  stands in front of a cloud behind it and is hidden by one in front. The near walk is done only when a cloud can be
  within 400 m of the camera. (On Direct3D the game draws the whole scene with one camera; the clouds then walk it once.)
* **What the clouds are laid over, and what over them** (2026-10-11, from Windows). The clouds know what stands in
  front of them from the camera's picture of distances, and not everything is in that: nothing see-through is (smoke,
  flames, spray, rain's drops), nor is Natural Ocean's sea. With two cameras that mostly comes right by itself: the
  near camera draws its stretch of sea and its smoke after the far camera's clouds. With one camera the clouds, laid
  on after everything see-through, were laid over all of it: a rocket's smoke column stood behind the clouds of the
  horizon, a crash's smoke had the sky's heaps drawn across it, and the sea's nearest waves and (from under water)
  the underside of its surface had clouds painted on them. Two things are done about it:
  * The sheet the clouds are laid on with is put as far from the camera as the nearest cloud or rain can be (under
    everything that can hold cloud: as far as that is above the camera; and no further than the nearest raining
    storm's curtain), and is hidden by whatever the camera has drawn nearer than that (`cloudsover` is drawn
    "nearer or as near", no longer "always"; `Clouds.SheetClear`). On every system.
  * Where one camera draws the whole scene, and while it is below everything that can hold cloud, the clouds are
    laid on before the see-through things, not after (`Clouds.LaidFirst`). From below, everything lower than the
    clouds is then in front of them, the sea among it.
  Still wrong: with the camera among the clouds or above them the old order is kept (a sea drawn after the clouds
  would be drawn over them seen from above), so near smoke has far clouds over it there; a trail that has climbed
  above the clouds shows in front of them from below; and where Scatterer is installed the old order is kept
  altogether (it lays its haze on from a copy of the picture taken at that same moment, and which of the two comes
  first has not been tried). On OpenGL, smoke further off than the near camera reaches (some 400 m) is drawn
  before the far camera's clouds, as it always was.
* **Cost kept down**: each walk is drawn at half the picture's size (a third for "lower"), with its steps begun at a
  different place each frame (each pixel's own, moved on by the golden ratio), and taken together with earlier frames
  (where each cloud was last frame is worked out from how far off it is; what is kept is held within this frame's
  neighbours, so nothing lingers); each step a hundredth of its distance from the camera (on the lower setting, with 56
  steps to the middle one's 88, as much longer as that is fewer: the same reach, more coarsely), but at least two dozen
  across the shell (from orbit) and three at a time through clear air, going back when a long step finds cloud; where a
  step enters cloud, the edge found between it and the last clear one by halving twice, and the cloud lit from no more
  than ten metres past the last clear place (the visible edge of a dense heap is then where it is, and lit as its
  outside is, not as its inside a step of tens or hundreds of metres in; out to 40 km from in or under the clouds, to
  15 from high above them); no further than
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
  the launch pad. Built again on 2026-10-11 from 0.3.0's remade clouds (the walk now 341,080 bytes, nine minutes of
  the compiler's time) and seen the same day: fair, mostly cloudy and a storm, each from the ground, from 2,500 m
  and from 6 km, picture for picture as on OpenGL on the same computer. Where the mod finds no shader it draws no
  clouds, says so on its page, and leaves EVE's clouds alone.
* **OpenGL with NVIDIA's driver** (Windows started with `-force-glcore`; Linux with an NVIDIA card is presumably the
  same): until 2026-10-10 the clouds drew nothing at all, and the game's log took `OpenGL Error: Invalid texture
  unit!` about six times a draw. The driver numbers a program's uniforms in the order of their names, and Unity
  cannot give a texture unit to a texture whose uniform comes 32nd or later: five of the clouds' maps (`_CloudMap`,
  B and C, `_CloudNow` and its depth) sort after most of the `_Cloud...` values, so they were left on unit 0 with
  the 3D noise, and OpenGL draws nothing where a 2D and a 3D texture share a unit. `tools/shaderpack/make_bundle.py`
  now gives every texture's uniform a location of its own where the driver takes that; packed again, the clouds draw
  there, with nothing in the log.

### Where the clouds come from

(On a world with no ground under its air, Jool, these clouds are not drawn at all since 0.2.2: see the end of this page.)

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
a plain sky, with the noise made exactly as the game makes it; a helper's scratch tool at first, kept in the workspace
since 0.2.2 as `tools/shaderpack/cloudlab.swift`, whose first lines say how to use it), which can be
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

### 0.2.2: the faults the second pass left

The second pass left four faults in its notes: silver linings thinner than before it, a slight smear beyond 15 km, tall
storm clouds cut off on the lower setting, and a first map that took 3.4 s at the pad and 15 s from orbit, with no clouds
until it was made. Worked on in the lab (the shader run offline, as above, now with the game's own roll-off of the
brightest values) and then in the game on 2026-10-10, on the development build from the test save's pad. What was seen in
the game and what only in the lab is said for each.

* **The silver lining.** A cloud drop throws most of what it scatters on within a few degrees of the way the light was
  going, and light scattered so is as good as not scattered: looked at from near the line to the sun it is still
  sunlight. So the light that goes straight on is dimmed by only the rest of what the cloud takes: 1 - g squared of it,
  0.28 for g 0.85 (the delta-Eddington rule). The shader had dimmed it by all of it, and since the second pass a heap is
  dense right up to its edge (0.15 of the light lost in each metre, three times a real heap's core, so that its shaded
  parts read dark in the game's picture): the sunlight got seven metres in, which is less than a pixel of rim at any
  distance. That is why the linings went thin when the edges were made sharp. Now what goes straight on is dimmed by a
  tenth of the cloud it passes (0.28 of a real heap's 0.03 to 0.05 a metre, counted against the 0.15 here): it comes
  through the outermost sixty metres or so, and lights a heap's billows and thin places from behind. The rule the light
  goes on by (Henyey and Greenstein's) is what keeps it to clouds near the sun: seventy times an even scattering
  straight towards the sun, twenty at ten degrees off, four at twenty, a half at forty-five. Nothing is added to the
  cloud's stuff: no mist round the heaps, which is what made the first cut's wide glows and its cotton wool.
  *In the lab*, the same heaps with 0.2.1's shader and with this one, the sun 7 and 24 degrees up behind them: grey
  bodies with a hair of rim before; after, a bright rim round the billows nearest the sun, wider on the small ones, the
  bodies as grey as before; nothing changed looking away from the sun, from above or from orbit. Tried at 0.28 (hardly a
  difference at these densities), 0.12, 0.1 and 0.07 (a small heap in front of the sun glows all through and loses its
  shape). *In the game*, sunrise at the pad with heaps held, looking at the sun: the clouds in front of it white-gold at
  their edges and through their thin parts, with Natural Light's shafts coming from behind them, the larger ones dark
  with lit rims; before, dark mauve shapes with no rim. (The two pictures are of different heaps: see the trap below.)
  Rims within a few degrees of a low sun come out white rather than gold: the far camera's picture holds nothing brighter
  than white, and each colour is rolled off by itself.
* **Heaps beyond 15 km a flat grey.** The lab's far view (22 degrees wide, on the horizon) against the same view walked
  very finely (steps a fifth as long, 4,000 of them) showed two causes. First, where a step enters cloud its edge was
  searched for only within 15 km, and beyond that the cloud was lit at the step's own place: anywhere up to a step's
  length inside it, 150 m at 15 km and 400 at 40, where no sunlight is. A heap's sunlit side came out as its inside:
  flat grey beside the nearer ones. Now the edge is searched for out to 40 km (from in or under the clouds; from high
  above them a step is less than a pixel), and at any distance the cloud is lit from no more than ten metres past the
  last place known to be clear, since the search still leaves the edge known only to a quarter of a step. To pay for
  it, a cloud of which eight tenths is already hidden by cloud in front is not searched for at all. Second, the long
  steps through clear air (three at a time) step over a far heap in most frames, so that taken over frames it is a
  ghost of itself; that is still so (two at a time between 12 and 60 km was tried in the lab and made no difference worth
  its steps). *In the lab*: the far heaps' sunlit sides white where they were grey, their shapes as before; still
  fainter and softer than in the finely walked picture. *In the game*: not set side by side on the same heaps, and it
  cannot be by loading twice (below: no two loads have the same sky). Two loads of the pad with heaps held, the clock
  the same and the noise held still, 0.2.1's shader in the one and this one in the other, six ways round by day and at
  sunrise: heaps down to the horizon in both; the farthest band reads as separate heaps with white tops and grey bases
  with this one, and as a softer lilac-grey band with 0.2.1's, but they are different heaps, and that is as much as
  the pictures can say.
* **No storm towers on the lower setting.** The lower setting had the middle one's steps and fewer of them (56 to 88), so a
  line of sight simply ran out sooner: in the lab a storm's tower 12 km off, with its anvil, was not drawn at all (most
  of a line's steps go on the clear air between the camera and the tower). Now each step is as much longer as there are
  fewer of them (1.57 times), and a line reaches as far on the lower setting as on the middle one, more coarsely. *In the
  lab*: the tower and its anvil on the lower setting as on the middle one. *In the game*: a storm held at the pad by
  day, four ways round, on the middle and the lower setting: the same masses and towers on both (with 0.2.1's shader the
  lower setting had the tall ones thinner and broken, and fewer far off).
* **The first map.** The map of the planet (32,768 places) was asked of the weather one place at a time through Keystone,
  which works out the wind over each place as well and finds the storms about each afresh (some seven hundred boxes of
  the storms' lattice looked into, and the weather asked where each storm found stands): 0.22 ms a place in the game
  that evening, seven seconds of a thread for the whole map (3.4 s and 15 s when the second pass measured it), every
  time a flight began, and no clouds in the sky until it was done; after the first, every other place each way was
  asked, to keep later ones under two seconds. Now, where the weather is Natural Weather's own, the map is asked of the
  model's atlas of the world (`Model.Atlas`): every storm on the world found once (some 500 to 800 on Kerbin as the
  weather comes, 10,000 with a storm held), sorted into squares of latitude and longitude 36 km across, and each place
  given the storms of the squares round it and its own large fields, no wind. *In the game*: the first map of a flight
  0.32 s, where 0.2.1's a few minutes before had taken 1.95 s for a quarter of the places. Later that night, a flight
  loaded, the installed copy's own first map waited out, then the mods started afresh with the frame limit lifted and
  the clouds' page asked every quarter second: with 0.2.1 at the pad, no clouds within the twelve seconds watched; with
  0.2.2 the clouds drawn 0.82 s after the mods started at the pad (the map 0.57 s of it) and 1.12 s after from a 250 km
  orbit (0.83 s). The frame meanwhile, at the pad, a quarter second at a time: 18.7 ms before; 22 to 76 ms over the
  first three seconds (the longest single frame 148 ms); 24 to 31 ms from the fourth second with one second of 40,
  24 ms by the twelfth. With 0.2.1, which was making no clouds in that time: 18.0 ms before, 24 to 52 ms over the first
  three seconds (longest frame 108 ms), 23 to 32 ms after. So the long frames of the first seconds are the starting of
  all the mods' development copies together, with or without clouds; the clouds' own start shows in them as two
  quarter seconds of 70 and 76 ms where 0.2.1's worst was 52, and no more can be told apart. From orbit: 17.2 ms before, 25 to 90 ms over the first three seconds (longest 150 ms), 16 to 23 ms
  after. The atlas itself, asked in the game (`Probe.Atlas`): made in 184 ms at the pad for 314 storms and 155 ms from
  orbit for 393; 6,000 places each time the same as asked one by one, none differing; a place 9.3 and 7.8 µs by the
  atlas against 323 and 288 µs by itself (a map of the planet 0.49 and 0.41 s against 10.6 and 9.4 s). *Outside the game* (the model's own files
  compiled by themselves): at 126,000 places on six worlds, with the weather as it comes and a storm and rain held,
  near the poles and across the line where longitude wraps, every answer the same as asking place by place, to the last
  figure. The map is also made once more as soon as the weather knows the world's land and seas (it reads them from the
  game a few dozen places a frame in the first seconds of a session's first flight, and takes the world for land until
  then): before, the sky changed by itself up to twenty seconds later. A world too large for the atlas (more than about
  1,400 km in radius: none of the game's own but Jool, which has no clouds of these) is asked place by place, without
  the wind.
* **Jool.** Looked at for the first time, from a 260 km orbit by day: the weather's deck showed as a flat pale grey
  disc under the craft, with a fuzzy round edge, on a planet otherwise plain dark green: the disc is as far out as a
  line of sight reaches the deck within the 90 km of air it is walked through, and the planet was plain because EVE's own
  clouds of Jool had been told to hide while these drew. These clouds are made for white heaps and sheets in the lowest
  sixteen kilometres of air over a ground (the map keeps a middle layer's base only up to 12 km and its top to 16;
  Jool's deck is given as 20 to 90). A world with no ground under its air is cloud all the way down, in its own colours,
  and what the game and EVE draw of it is its clouds: there these are not drawn any more, the page says so, and EVE's are
  left alone. *In the game*, after the change, from the same orbit on the day and the night side, six ways round each:
  Jool plain green with its bright limb, no grey disc; no map of it made; the clouds' page reads "No clouds of the
  mod's own on Jool: it has no ground under its air, and keeps the clouds the game draws it with."; EVE's layer of
  Jool listed and not hidden; with a storm held, every flash the weather made was within cloud (0 to the ground of 14,
  then of 43), where three in ten had been drawn as strokes down to a ground that is not there.
* **Cost.** In the game, paused at the pad by day with heaps held and the frame limit lifted, the clouds switched off
  and on four times for each: with 0.2.1's packed shader and with this one put in by turns in the one scene (a new
  build each time), the frame with clouds 14.05, 14.13, 13.91, 13.94, then 14.12, 14.00, 14.06 ms with the old (one
  reading of 15.37 left out) and 14.18, 14.00, 14.00, 13.99, then 14.05, 14.21, 13.98, 14.10 ms with the new: 14.03
  against 14.06 on average, three hundredths of a millisecond more; the frame without clouds wandered between 12.84 and
  13.13 ms over the same minutes (two readings of 14.4 and 14.7 left out), so that the clouds' own cost comes out at
  0.93 and 1.17 ms with the old shader and 1.13 and 1.07 with the new, the same within what the frame itself wanders.
  In the lab the walk alone (640 by 360) came out between 9% less and 9% more over seven views, and 15 to 17% more
  looking along the horizon through a narrow lens, where every line of sight has far heaps in it. Among the heaps
  1.9 km up, paused, both cameras' walks running (the camera beside a heap, its shadow on the shallows below), the two
  shaders by turns twice: the frame with clouds 17.2 and 15.6 ms with the old (four readings each) and 16.4 and 16.1
  with the new; without clouds 12.1 to 15.8 ms over the same minutes; the clouds' own cost 3.2 and 2.7 ms with the old
  and 1.8 and 3.0 with the new. No dearer there either; the readings wander by more than the two differ.
* **Found: the clouds did not sit on the planet. Mended by the code, and that mend not yet looked at in the game.** The
  clouds worked in what their code called the planet's own axes, taken (until this mend) from
  `CelestialBody.GetRelSurfaceNVector` at latitude 0 longitude 0, at longitude 90 and at the pole.
  That function answers in the planet's own frame, not in the scene's: the three come out as the scene's x, z and y
  however the planet stands. And the scene stands turned about the pole against the planet by
  `CelestialBody.directRotAngle`, which is whatever the planet's turning and the scene before left it at (the game's
  own code adds it to a longitude before asking that function for a place in the scene); above some 100 km it runs on
  with the planet's turning. *In the game*, two loads of the same quicksave with the clock set the same (9,325,010 s)
  and heaps held: the wind over the pad, 7.0 m/s to the east and 1.5 to the north in both, came out in "the planet's
  axes" as -0.60, -6.97, 1.50 in the one and 6.99, 0.41, 1.50 in the other: by those axes the pad stood at longitude
  175.1 in the one and -86.6 in the other. It is at -74.6. What followed, by the code (none of it looked for in the
  game):
  * the weather's map is read at the scene's longitude, so the sky over the craft is the weather of a place
    `directRotAngle` further round the planet on the same latitude (110 and 12 degrees in those two loads), not the
    weather the mod gives the craft its rain, wind and seeing from; it is cut to the heights of the clouds round the
    craft's true place, and not drawn at all if there are none there. With the weather held it is the same everywhere
    and nothing shows, which is how every picture of these clouds so far was taken;
  * the storms near the craft (their towers) and the light of a flash within the clouds are worked out from true
    latitudes and longitudes and then taken for the scene's: they are placed that same angle away, out of sight. In
    the film's storm shot each flash lights the ground and the sky above it stays black;
  * the heaps are in other places after every load, whatever is held (the trap below);
  * from orbit the clouds stay with the scene while the planet turns under them (175 m/s at Kerbin's equator).

  The mend is a few lines: the axes from `GetSurfaceNVector`, the storms' and the flashes' places from
  `GetWorldSurfacePosition`. Its arithmetic was checked by hand against the game's own (`PlanetaryFrame(0, 90, turn)`
  puts a place of longitude L at the scene's longitude L + turn; with the new axes the shader's own sum gives back L and
  the latitude exactly), and it compiles; it has **not been run**. Below the height where the planet starts turning in
  the scene it turns the whole of the clouds about the pole by one fixed angle and nothing else; what it adds is the
  weather's own sky over the craft, and storms' towers and flashes' light where they belong, which no picture has shown
  yet. To check it: the clouds' development readout gives the camera's longitude by the game and by the clouds' axes
  and how far apart they are (`Clouds.Stats`: nought with the mend, whatever the load); the sky after two loads with
  the weather and the noise held should be the same; a flash to the ground at night should light the cloud over it.
* **Still wrong.** Far small heaps are fainter than near ones (above). Seen close, a heap's lit faces show the noise's
  own cells as flat facets and boxy lumps, and now and then a smooth bright lens shape hangs among the heaps (both there
  in 0.2.1 as well). The Direct3D build of the shaders (`clouds-windows.bundle`) was 0.2.1's until it was built again on
  the Windows PC on 2026-10-11, from 0.3.0.
* **A trap for pictures of before and after.** Two loads of the same quicksave do not have the same heaps in the same
  places, whatever is done: tried with the weather held first and the mods started again after the warp (the wind's
  carrying then starts from the clock alone), and with the noise held still besides (`Clouds.Still on`, development
  build): different skies both times, because the noise is laid out in the scene's axes, which stand differently
  against the planet after every load (above; with the mend, not yet tried, two loads should agree). Until that is
  seen, the same sky twice is to be had only within one flight: a new build published into the running game (`tools/dev/reload.sh`) loads a newly packed shader and leaves
  the scene as it stands. The two settings side by side were taken so; the old and the new shader's far heaps were not.

