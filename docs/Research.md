# Weather: research

Written 2026-10-09, before anything was built, to answer three questions: what weather KSP 1 already has
(from the game and from other people's mods), how other games make weather look and feel real, and how real
weather works, here and on the worlds Kerbin's neighbours are modelled on. The design that follows from it
is on the next page, [Design](Design.md).

Nothing here was measured in the game: this was reading only. Where a number comes from somewhere, the
link is beside it. "Not confirmed" means just that.

---

## 1. What the game has, and what is installed here

**The game itself has no weather.** No wind, no rain, no fog, no lightning. Every part of a craft works out
how fast the air is going past it from the craft's own movement and nothing else: in the game's code the
line is `part.dragVector = rigidbody.velocity + Krakensbane.GetFrameVelocity()` (in `FlightIntegrator`,
the part of the game that does drag), and wings and control surfaces each do the same sum for themselves
(`ModuleLiftingSurface`, `ModuleControlSurface`). So the air is always still. There is one sun and one flat
grey "ambient" light (see [Natural Light](https://github.com/IshiakiZ/ksp-natural-light/wiki/How-it-works)).

**The explosions mod already makes up a wind** for its smoke. For each place and half hour it draws one at
random: still air about a third of the time, otherwise a light air to a fresh breeze (1 to 6.5 m/s at ten
metres, stronger on Duna and Jool, weaker on Eve), stronger with height as the ground holds less of it
back, with gusts that travel down the wind, a slow swing from side to side and eddies at three sizes
(`Site.Wind`, `Site.Aloft` in `src/VolumetricExplosions/Site.cs`). Rocket Smoke and Engine Flames put their
smoke into that same air. The wind touches nothing but smoke.

**Installed in this game, not ours** (checked in its `GameData` folder):

| Mod | Version here | What it gives this game |
| --- | --- | --- |
| EVE Redux (Environmental Visual Enhancements) | 1.11.7.2, the free one | clouds and city lights from the stock "BoulderCo" config that EVE ships with: a cloud layer at 4 km on Kerbin, 6 km on Laythe, 8 km on Duna, five layers from 4 to 12 km on Eve, and Jool. Each layer is a flat picture wrapped round the planet plus small "puffs" drawn near the camera where the picture is cloudy. Kerbin's layer is given no speed in this config, so it takes EVE's default (`speed = 0,30,0` in EVE's `CloudsObject`): the whole picture turns round the planet at 30 m/s, the same pattern coming round again about every 35 hours of game time. Eve's layers turn at 60 to 90 m/s |
| Scatterer | 0.0878 (log line `[Scatterer][Info] Version:0.0878p`) with the stock configs | the sky's colour, haze with distance, the sea, the sun's glare |
| TUFX | | the film (post-processing), temporal smoothing on the near camera |

Measured in this game on 2026-10-07: Scatterer, EVE, TUFX and Waterfall together took it from about 115
frames a second to about 79 ([What it costs](https://github.com/IshiakiZ/ksp-volumetric-explosions/wiki/What-it-costs)).

---

## 2. Weather mods for KSP 1

Two families exist, and they hardly meet: mods that **draw** weather, and mods that **push craft** with
wind. Most of the second family needs one of two doors into the game's physics: **ModularFlightIntegrator**
(a small library that lets one mod replace the game's drag sums; for each sum the first mod to ask gets it,
any later one is refused with the log line "UpdateAerodynamics already has an override",
[source](https://raw.githubusercontent.com/sarbian/ModularFlightIntegrator/master/ModularFlightIntegrator.cs)),
or **Ferram Aerospace Research** (FAR, a replacement for the game's whole aerodynamics, GPL), which has one
slot for a wind: `FARAtmosphere.SetWindFunction(Func<CelestialBody, Part, Vector3, Vector3>)`, metres a
second, the last mod to set it wins
([source](https://raw.githubusercontent.com/dkavolis/Ferram-Aerospace-Research/master/FerramAerospaceResearch/FARAtmosphere.cs)).

### Mods that draw weather

| Mod | What it does | How | Licence, price | Systems | State |
| --- | --- | --- | --- | --- | --- |
| **EVE Redux** (blackrack, Ghassen Lahmar: LGhassen on GitHub) | cloud layers, city lights, cloud shadows | flat cloud pictures on spheres, and particle "puffs" placed round the camera and drawn by a geometry shader (`EVE/GeometryCloudVolumeParticle`) | MIT, free ([source](https://github.com/LGhassen/EnvironmentalVisualEnhancements)) | Windows, Mac, Linux | maintained; installed here |
| **True Volumetric Clouds** (the same author; a version of EVE) | clouds drawn as real volumes by ray marching (stepping along each pixel's line of sight through a 3D density); rain, snow, lightning and volumetric Jool since release 2 (2023); drops on cockpit windows that streak with speed and vanish past the speed of sound, with their own sound, release 3 (August 2023); a new lighting model and light shafts, release 4 (December 2023); clouds reworked to look less flat and their shadows made part of the lighting, release 5 (September 2025), whose fixes in 2026 mention puddles | its own shaders | paid: downloads are sent to patrons of blackrack's Patreon ([release 2](https://www.patreon.com/posts/true-volumetric-83345531), [release 4](https://www.patreon.com/posts/true-volumetric-94613561), [release 5](https://www.patreon.com/blackrack/posts/true-volumetric-139879553)) | Windows; release 5's notes say screen-space reflections do not work on OpenGL (Mac, Linux). Whether the clouds run on a Mac is **not confirmed** | maintained, the most admired visual mod for KSP; reported heavy on frame rate (no figure found) |
| **Scatterer** (the same author) | sky, haze, sea with waves, sun glare, light shafts | its own shaders | GPL 3 ([source](https://github.com/LGhassen/Scatterer)) | all three | maintained; newer builds on the author's Patreon; installed here |
| **Astronomer's Visual Pack** (AVP) | a config pack for EVE and Scatterer: clouds, **sandstorms and surface dust and lightning on Duna**, auroras | EVE's own effects | | all three | last for KSP 1.12.3, July 2022; does not mix with Spectra or EVE's stock config ([forum](https://forum.kerbalspaceprogram.com/topic/160878-ksp-1123-astronomers-visual-pack-beyond-v413-july-17th-2022)) |
| Spectra, Stock Visual Enhancements, and configs for blackrack's clouds (GEP-Volumetrics, JNSQVolumetrics, KSRSS and OPM volumetric clouds) | looks for the planets | configs only | | | various |
| **Weather Driven Solar Panel** (Aebestach) | solar panels give less under cloud, rain, snow, dust storms and volcanic ash, and wear faster | reads the weather from blackrack's clouds | GPL 3 ([SpaceDock](https://spacedock.info/mod/3556/WeatherDrivenSolarPanel)) | | 1.12.5 |

### Mods that push craft with wind

| Mod | What it does | How | Licence | State |
| --- | --- | --- | --- | --- |
| **Kerbal Weather Project** (KWP, cmet24 / cmac994) | wind, temperature and pressure from a real weather model (NCAR's MPAS) run for Kerbin: an hourly climatology that varies by place and time of day, and five years of weather that changes day to day at the launch sites. Raises Kerbin's scale height from 5.6 to 8.5 km, so re-entry and spaceplanes get harder. A text panel; no pictures. Works with FAR, but FAR then ignores its pressure and temperature | ModularFlightIntegrator | MIT ([GitHub](https://github.com/cmac994/KerbalWeatherProject), [SpaceDock](https://spacedock.info/mod/2607/Kerbal%20Weather%20Project)) | last release 1.0.7, January 2022, for KSP 1.12.3; 84 MB (13 MB "Lite") |
| **Advanced Atmosphere Tools** (CashnipLeaf; once "Modular Climate & Weather Systems") | wind, temperature, pressure, the gas's weight and heat ratio per place, from maps and curves in configs; wind-corrected prograde marks on the navball; an interface for other mods; hands its numbers to FAR when FAR is there | ModularFlightIntegrator and Harmony (a library for changing the game's code as it runs) | MIT ([SpaceDock](https://spacedock.info/mod/3597/Advanced%20Atmosphere%20Tools), [GitHub](https://github.com/CashnipLeaf/AdvancedAtmosphereTools)) | 1.12.5 |
| **Configurable Planetary Wind Effects** (CPWE, same author) | prevailing winds and patterns for any world, set in configs; no pictures | ModularFlightIntegrator | | alpha, v0.8.6, March 2024 ([forum](https://forum.kerbalspaceprogram.com/topic/223284-112x-wip-cpwe-configurable-planetary-wind-effects-v086-alpha)) |
| **Kerbal Wind Continued** | wind and gusts (worked out again every few seconds) by height above the ground | FAR only | | beta, KSP 1.12.3, tested at real-solar-system scale ([forum](https://forum.kerbalspaceprogram.com/topic/195587-1123-kerbal-wind-continued)) |
| **WindAPI** and its family (Zondulus, 2025-2026) | WindAPI adds up the winds of several mods and hands the sum to FAR's one slot. On it: **KerbalSoaring** (thermals, the rising bubbles of warm air gliders use), **RidgeLift** (air pushed up over ridges, mountain waves), **Volumetric Aware Weather** (rough gusty air under and inside thick clouds, found by stepping through blackrack's clouds above the craft), **RealRaymarchedRadar** (a weather radar), **Beaufort** (one panel for all of them: wind, how much is vertical, how rough, a wind arrow; a button to make a gust), Variometer | FAR only | CC0 ([WindAPI](https://github.com/Zondulus/WindAPI), [the author's mods](https://github.com/Zondulus?tab=repositories), [Beaufort](https://spacedock.info/mod/4295/Beaufort)) | alpha to working, 1.12.5 |

### What none of them does (the gap)

* **No mod makes one weather that both looks and pushes.** The ones that draw weather do not move craft
  (the one exception, Volumetric Aware Weather, needs FAR, WindAPI and the paid clouds); the ones that move
  craft draw nothing but a panel.
* **For the game's own aerodynamics** (what this user plays with), wind comes only from the Kerbal Weather
  Project, Advanced Atmosphere Tools or CPWE, each needing ModularFlightIntegrator and each taking the one
  slot it gives.
* **Rain, snow and lightning exist only in the paid clouds**, plus AVP's Duna storms made of EVE's flat
  effects.
* **Nobody ties weather to launching a rocket**: real launches are held for wind at the pad, wind shear
  aloft and the risk of lightning set off by the rocket itself (section 4.9). No mod knows any of it.
* **Nobody changes the light** for an overcast day (a soft, shadowless sun and a grey sky) except as the
  shadow of blackrack's clouds.
* **No smoke moves with the wind a craft feels.** Our explosions' air is the only simulated smoke in KSP;
  its wind is made up, and nothing else shares it.

---

## 3. How other games do weather

**Horizon Zero Dawn and Forbidden West** (Guerrilla, the Decima engine). The clouds are a volume stepped
through by each pixel. What kind of cloud is where comes from a "weather map", a picture that says, for
every place, how much cloud, how likely rain and which type (from flat stratus to towering cumulus); the
shapes come from noise (Perlin and Worley noise, layered) carved away at the edges; the light is worked out
from how much cloud lies towards the sun (Beer's law, the rule that light fades exponentially with the
amount of stuff it passes through), plus a darkening of a cloud's thin edges when the sun is behind the
viewer, which the author likened to powdered sugar. Forbidden West made the clouds something you fly through and added spinning
superstorms. Talks by Andrew Schneider at SIGGRAPH's "Advances in Real-Time Rendering":
[2015](https://www.advances.realtimerendering.com/s2015/The%20Real-time%20Volumetric%20Cloudscapes%20of%20Horizon%20-%20Zero%20Dawn%20-%20ARTR.pdf),
[2017 (Nubis)](https://advances.realtimerendering.com/s2017/Nubis%20-%20Authoring%20Realtime%20Volumetric%20Cloudscapes%20with%20the%20Decima%20Engine%20-%20Final%20.pdf),
[2022 (Nubis, Evolved)](https://advances.realtimerendering.com/s2022/SIGGRAPH2022-Advances-NubisEvolved-NoVideos.pdf),
[2023 (Nubis Cubed)](https://advances.realtimerendering.com/s2023/Nubis%20Cubed%20(Advances%202023).pdf).

**Red Dead Redemption 2** (Rockstar). Sky, clouds and fog are one system: the air is divided into a coarse
grid of boxes, the light passing through each is worked out once, and the same answer lights the screen,
the reflections, and a grid of probes that gives everything the sky's light from the right directions.
Fabian Bauer, "Creating the Atmospheric World of Red Dead Redemption 2", SIGGRAPH 2019
([course page](https://advances.realtimerendering.com/s2019/index.htm)). (This is what Natural Light's
ambient probe does for one place.)

**Frostbite** (EA). A physically based sky, atmosphere and clouds: Sébastien Hillaire,
[SIGGRAPH 2016](https://www.ea.com/frostbite/news/physically-based-sky-atmosphere-and-cloud-rendering)
and the [2020 paper](https://sebh.github.io/publications/egsr2020.pdf) on a sky that is cheap enough for
any game. Volumetric fog that every light shines through: Bart Wronski, *Assassin's Creed IV*, SIGGRAPH
2014 ([course page](https://advances.realtimerendering.com/s2014/index.html)).

**Microsoft Flight Simulator** (Asobo). Live weather from meteoblue's forecasts, turned into volumetric
clouds, wind and rain all over the world ([meteoblue](https://business.meteoblue.com/articles/microsoft-flight-simulator-partnership-meteoblue)).
Rain on the glass is a shader every windscreen can switch on, wiped by the wipers
([SDK documentation](https://docs.flightsimulator.com/msfs2024/retail/models-and-textures/modeling/aircraft/airframe/windshield-and-windows/)).
Its players' main complaints, in its forum and in guides to it: thunderstorms and towering clouds are
under-drawn, and the coarse weather data blurs storm cells into a flat deck. The lesson: the data is only
as good as the cells it is cut into, and a storm must stay a storm.

**X-Plane 12** (Laminar Research). Clouds stepped through as volumes since 12.0, sharpened near the plane in
12.2; rain on the windscreen built in, its drops pushed by the airflow from the propeller and the wings;
rain and snow lie on the ground, freezing or thawing by temperature; and the turbulence is real physics:
wind shear, thermals, air over terrain and storms each shake the aircraft through its flight model
([developer blog](https://developer.x-plane.com/author/thomson/), [Flying magazine](https://www.flyingmag.com/weather-wonders-of-x-plane-12/)).

**DCS World** (Eagle Dynamics). Volumetric cloud presets since 2.7 (2021): layers, coverage, thickness,
rain; since 2.8 each layer drifts with the wind at its height; rainbows, glories and ice halos; turbulence
inside clouds ([2.7 review](https://www.helisimmer.com/articles/cloud-update-dcs-world-2-7/),
[2.8.1 changes](https://www.digitalcombatsimulator.com/en/news/changelog/release/2.8.1.34667.2/)).
Weather mostly stays as the mission set it.

**Arma 3** (Bohemia). The whole weather is a handful of numbers that change slowly: overcast from 0 to 1,
rain (which cannot start below 0.7 overcast), fog with a density, a fall-off with height and a base height
(so it can lie in valleys or over the sea), a wind vector with gusts, and lightning; each is eased over a
given number of seconds ([setOvercast](https://community.bistudio.com/wiki/setOvercast),
[setRain](https://community.bistudio.com/wiki/setRain), [setFog](https://community.bistudio.com/wiki/setFog),
[setWind](https://community.bistudio.com/wiki/setWind), [setLightnings](https://community.bistudio.com/wiki/setLightnings)).
Snow is rain's particles with a flake picture. The wind moves bullets as well as smoke.

**Ghost of Tsushima** (Sucker Punch). One wind simulation drives the grass, trees, cloth and particles, so a
gust is seen crossing a field; the wind is also how the player finds the way. Speed was chosen over strict
realism to move very many blades and leaves ("Blowing from the West: Simulating Wind in Ghost of
Tsushima", GDC 2021, [GDC Vault](https://gdcvault.com/play/1027350/Blowing-from-the-West-Simulating),
[write-up](https://gamedeveloper.com/design/using-vorticles-to-simulate-wind-in-i-ghost-of-tsushima-i-)).

**Death Stranding** (Kojima Productions, also Decima): rain is the game's central threat, and by one report
of their SIGGRAPH 2017 talk the studio wrote its own fog that thickens towards the ground for its heavy,
tinted murk ("Decima Engine: Advances in Lighting and AA",
[course page](https://advances.realtimerendering.com/s2017/index.html)).

**THE FINALS** (Embark). Weather is one of the arena's conditions, told before a match: a sandstorm that
cuts how far one can see, fog, night ([Embark](https://id.embark.games/fr/the-finals/support/faq/61-arena-variants),
[map variants](https://ggrecon.com/guides/the-finals-maps-variants)).

**Rain itself**, the papers behind it:

* Natalya Tatarchuk's *ToyShop* demo (ATI, SIGGRAPH 2006): rain streaks lit by the scene, splashes, ripples
  in puddles, drops on glass, lightning, glow round lamps in the wet air
  ([notes](https://advances.realtimerendering.com/s2006/Chapter3-Artist-Directable_Real-Time_Rain_Rendering_in_City_Environments.pdf)).
* Kshitiz Garg and Shree Nayar, *Photorealistic Rendering of Rain Streaks* (SIGGRAPH 2006): a falling drop
  bends light from nearly the whole scene into the eye, so its brightness is the scene's, not its own; a
  streak is a drop smeared by the exposure, and its look changes with how the drop wobbles as it falls
  ([project](https://www.cs.columbia.edu/CAVE/projects/rain_ren/)).
* Sébastien Lagarde, *Observe rainy world* and its sequels on wet surfaces (2012-2013, the rain of *Remember
  Me*), the best list of what rain looks like: bright lamps make long streaked reflections on wet ground;
  water pools first in hollows and cracks, and only pooled water ripples; wet stone and soil go darker and
  shinier, wet metal and plastic only shinier; drops show best when lit from behind and at night; heavy
  rain makes mist and a glow round each light; walls parallel to the rain stay dry; drying is patchy and
  the shine goes before the darkening
  ([observations](https://seblagarde.wordpress.com/2012/12/10/observe-rainy-world/),
  [wet surfaces](https://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/),
  [dynamic rain](https://seblagarde.wordpress.com/2013/01/03/water-drop-2b-dynamic-rain-and-its-effects/)).
* Plants in wind: Tiago Sousa, *Vegetation Procedural Animation and Shading in Crysis* (GPU Gems 3,
  [chapter 16](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis)).

### What makes weather read as real on screen

Gathered from the above:

1. **Light first.** An overcast day is soft and nearly shadowless, the sky brightest overhead (about three
   times its brightness at the horizon: the standard overcast sky of Moon and Spencer, 1942, used by the CIE),
   contrast low, the whole scene several times darker, which an eye or camera mostly makes up for. Under a
   storm the cloud's underside is dark and the light under the cloud base at the horizon is bright.
2. **Rain is seen where it is lit**: against dark ground, against lamps, backlit by a low sun. In plain
   daylight in front of bright sky it hardly shows. A streak is motion blur, so its length goes with the
   camera's exposure time and how fast the drop crosses the view.
3. **Distance goes grey.** Rain and mist shorten how far one can see; far rain shows as grey curtains
   hanging from the cloud base.
4. **Wet things change**: darker and shinier ground, puddles that mirror, splashes, ripples.
5. **Wind is seen in what it moves**: smoke leaning, dust lifting, rain slanting, flags, grass, whitecaps,
   and gusts as patterns travelling across a field or the sea.
6. **Lightning** is brief and flickers (a flash is several strokes down the same path), lights the cloud
   from inside, and its thunder comes about three seconds a kilometre later.
7. **Sound**: the hiss of rain, the rumble of thunder, wind on the hull.
8. **Everything agrees**: the clouds overhead, the light, the rain, the wind on the smoke and the wind on
   the craft. And it changes over minutes, not at once.

---

## 4. The science

### 4.1 The air, layer by layer

In the lowest layer of the air (the troposphere, up to about 11 km on Earth) the temperature falls with
height, on average by 6.5 °C a kilometre ([U.S. Standard Atmosphere, 1976](https://ntrs.nasa.gov/citations/19770009539)).
A parcel of air that rises without exchanging heat cools faster than that, by 9.8 °C a kilometre while it is
dry, and by only about 5 to 6 °C a kilometre once its water vapour condenses, because condensing gives up
heat (Wallace and Hobbs, *Atmospheric Science*, 2nd edition, 2006). That difference decides the weather:
where rising air stays warmer than the air round it, it goes on rising (the air is **unstable**: cumulus,
showers, thunderstorms); where it ends up colder, it sinks back (**stable**: flat layers, haze, fog). The
height at which rising air starts to condense is the **cloud base**: flat-bottomed cumulus all at one
height on a fair day.

### 4.2 Clouds: kinds and heights

The World Meteorological Organization's [International Cloud Atlas](https://cloudatlas.wmo.int/) sorts
clouds by level. In middle latitudes: **low** clouds from the ground to 2 km (stratus, stratocumulus,
cumulus, and cumulonimbus, the thunderstorm cloud, which reaches from low to the top of the troposphere);
**middle** from 2 to 7 km (altostratus, altocumulus); **high** from 5 to 13 km (cirrus and its kin, made of
ice). In the tropics the high level reaches 18 km. Flat clouds come from air lifted gently over a large area,
heaped ones from air rising in bubbles.

### 4.3 Rain, drizzle, snow and hail

Raindrops are from a tenth of a millimetre (drizzle) to about 5 mm across; bigger ones break up. Drizzle falls
at under 1 m/s, a 2 mm drop at about 6.5 m/s, the biggest at about 9 m/s ([Gunn and Kinzer,
1949](https://doi.org/10.1175/1520-0469(1949)006%3C0243:TTVOFF%3E2.0.CO;2)). There are many more small
drops than big ones, and heavier rain has bigger drops (the Marshall-Palmer distribution, 1948). Snowflakes
fall at about 0.5 to 1.5 m/s (Locatelli and Hobbs, 1974), so they ride every eddy; hail falls fastest.
Most rain outside the tropics begins as snow high in the cloud and melts on the way down.

### 4.4 Weather systems and the day

Large-scale weather comes as systems: areas of low pressure with their **fronts**, the edges where warm air
meets cold. A warm front brings a long, slow lowering of cloud and steady rain; a cold front a narrow band of
heaped cloud, showers or storms, a wind shift and a sudden gust, then clearing
([NOAA JetStream, the weather school](https://www.noaa.gov/jetstream)). Systems drift with the winds aloft
(west to east in middle latitudes on Earth, east to west in the trade-wind belt near the equator).

Under them the day has its own cycle. By day the sun heats the ground, the air near it churns and the wind
at the surface picks up; cumulus build through the morning and, in moist air, showers and storms come in
the afternoon. At night the ground cools, the lowest air goes still and calm, and fog forms in damp, still
air towards dawn. On a coast the land heats faster than the sea, so by afternoon a **sea breeze** blows
inland.

### 4.5 Thunderstorms and lightning

A thunderstorm grows from a cumulus in unstable, moist air: a towering stage of rising air (10 to 20 m/s in
an ordinary storm, much more in the strongest), a mature stage with rain, hail and lightning and air
falling in the rain shaft, and a dying stage when the falling air chokes the rising
([NOAA NSSL](https://www.nssl.noaa.gov/education/svrwx101/thunderstorms/)). Its top spreads into the flat
**anvil** where the rising air reaches the stable layer above. The falling air spreads out at the ground
as a **gust front**, a sudden cold, strong wind ahead of the rain. A **microburst** is a concentrated burst
of falling air, under 4 km across, lasting minutes, with winds that can exceed 100 mph; an aircraft on
approach meets a headwind, then a downdraft, then a tailwind, and loses lift just when it is lowest
(T. T. Fujita, *The Downburst*, 1985; the crash of Delta flight 191 at Dallas-Fort Worth in 1985,
[NTSB report AAR-86/05](https://www.ntsb.gov/investigations/AccidentReports/Reports/AAR8605.pdf), is why
airports and airliners now have wind-shear warning).

**Lightning** ([NOAA NSSL](https://www.nssl.noaa.gov/education/svrwx101/lightning/); V. Rakov and M. Uman,
*Lightning: Physics and Effects*, 2003): most flashes stay inside or between clouds; a flash to the ground
is usually several strokes down the same channel a few hundredths of a second apart, which is why it
flickers; the channel is hotter than the surface of the sun; thunder is the channel's shock, heard up to
roughly 15 km away. Because sound travels about 340 m a second, the delay is about three seconds a
kilometre, and because the channel is long and crooked, each part of it is heard at a different moment: the
rumble (A. A. Few, "Thunder", *Scientific American*, July 1975; H. S. Ribner and D. Roy, "Acoustics of
thunder: a quasilinear model for tortuous lightning", *Journal of the Acoustical Society of America* 72,
1982, which builds thunder from the shape of the channel).

### 4.6 Wind near the ground

The lowest few hundred metres to a couple of kilometres of air (the **boundary layer**) is slowed by the
ground. Wind grows with height roughly as the logarithm of height divided by the ground's roughness, or
more simply as a power of height: about the seventh root over open country by day, more over rough ground or
at night ([wind profile power law](https://en.wikipedia.org/wiki/Wind_profile_power_law); R. Stull, *An
Introduction to Boundary Layer Meteorology*, 1988). (The explosions mod uses a power of 0.22, a rough
country's.) By day the churning carries momentum down and the surface wind is gusty; at night the layer
goes still near the ground while the wind above it can run faster than by day. Weather services call a gust
a rapid rise and fall of at least 10 knots between peaks and lulls ([National Weather
Service](https://forecast.weather.gov/glossary.php?word=gust)); peaks of 1.3 to 1.6 times the mean wind are
ordinary.

### 4.7 How flight simulators model gusts and turbulence

The standard models come from the United States military's flying-qualities documents: MIL-F-8785C (1980)
and MIL-HDBK-1797 (1997). They treat turbulence as random wind with a known spectrum (how much of it comes
at each size), frozen in the air and carried along by the mean wind, so that a craft flying through it at
speed V meets it as a changing gust (MathWorks' descriptions of the models:
[Dryden](https://www.mathworks.com/help/aeroblks/drydenwindturbulencemodelcontinuous.html),
[von Kármán](https://www.mathworks.com/help/aeroblks/vonkarmanwindturbulencemodelcontinuous.html)).

* **Two spectra**: von Kármán's, which matches measurements, and Dryden's, a simpler one that is easy to
  make from filtered random noise. Both give three gust parts: along the flight, sideways and vertical.
* **Near the ground** (below 1,000 ft, about 300 m) the size of the eddies grows with height (the vertical
  scale is the height itself, in MIL-F-8785C) and their strength goes with the wind at 20 ft (6 m), W20:
  the vertical gusts' typical size (standard deviation) is a tenth of W20, and the along-wind and sideways
  ones are larger than that near the ground and equal to it by 1,000 ft. W20 of about 15 knots is light
  turbulence, 30 moderate, 45 severe.
* **Above 2,000 ft** the eddies are 1,750 ft (about 530 m) in scale and their strength is read from a table
  of how often each is exceeded at each height.
* **A single gust** is given the "1 minus cosine" shape: the wind rises smoothly to its peak over a set
  distance and stays ([discrete gust model](https://www.mathworks.com/help/aeroblks/discretewindgustmodel.html)).
* **Wind shear near the ground** follows the logarithm of height
  ([wind shear model](https://www.mathworks.com/help/aeroblks/windshearmodel.html)).

### 4.8 Icing

Cloud droplets stay liquid well below freezing ("supercooled"), most often between 0 and -20 °C, and freeze
on whatever they hit: ice on a wing spoils its lift and adds drag, ice in an intake starves an engine
([FAA Advisory Circular 91-74B](https://www.faa.gov/documentLibrary/media/Advisory_Circular/AC_91-74B.pdf);
the ATR 72 of American Eagle 4184, 1994, [NTSB AAR-96/01](https://www.ntsb.gov/investigations/AccidentReports/Reports/AAR9601.pdf)).

### 4.9 Weather and rockets

Real launches are held or scrubbed for weather far more than aircraft are. NASA's published rules for a
crewed Falcon 9 ([NASA fact sheet FS-2020-05-568-KSC](https://www.nasa.gov/wp-content/uploads/2021/07/falcon9_crewdragon_launch_weather_criteria_fact_sheet.pdf))
say, among others: do not launch if the sustained wind 162 ft up the pad exceeds 30 mph; through upper-level
wind shear that could lead to control problems; for 30 minutes after lightning within 10 nautical miles
(18.5 km) of the pad or the flight path; within 10 nautical miles of a thunderstorm's anvil; through a cloud
layer more than 4,500 ft thick that reaches freezing temperatures; within 10 nautical miles of cumulus whose
tops reach freezing temperatures; through cumulus formed from a smoke plume; when electric-field meters near
the pad read over 1,500 volts a metre; or if the weather where the capsule would come down after an abort
is too rough. For the Space Launch System the peak-wind limits are given at several heights up the pad, about
29 knots at 132.5 ft to about 39 knots at 457.5 ft, with hail, cold and heat rules as well
([NASA, Artemis I weather criteria](https://www.nasa.gov/missions/artemis/orion/artemis-i-weather-criteria/)).

Why each rule exists:

* **Wind at the pad**: a rocket rising slowly beside its tower drifts downwind; standing on the pad it is
  shaken by the eddies the wind sheds off it.
* **Upper-level winds and shear**: a rocket goes through the fastest winds aloft (the jet stream, at 10 to
  14 km on Earth) close to the moment of greatest aerodynamic pressure, "max-Q". A sudden change of wind
  with height turns into a sudden angle between the rocket and the air, and a sideways load. The Space
  Shuttle's steering was re-planned on launch day from balloon soundings of the winds (the Jimsphere, a
  radar-tracked balloon that measures wind changes over a few hundred feet, up to 55,000 ft), every hour
  from about six hours before launch ([NASA, Shuttle day-of-launch trajectory design](https://ntrs.nasa.gov/citations/20110003654),
  [winds aloft during day-of-launch operations](https://ntrs.nasa.gov/citations/20050092294)).
* **Lightning, and lightning the rocket sets off**: a rocket and its long hot plume in a strong electric field
  can start a flash that would not otherwise have happened. Apollo 12 was struck twice, 36.5 and 52 seconds
  after lift-off, in November 1969, and lost nine non-essential sensors
  ([Space.com](https://www.space.com/apollo-12-lightning-strike-twice-launch-video.html)); Atlas-Centaur 67
  was struck 48 to 49 seconds after lift-off in March 1987, a stored number in its computer was changed, it
  swung its engines hard over and broke up ([NASA safety message](https://sma.nasa.gov/news/safety-messages/safety-message-item/atlas-centaur-ac-67)).
  The rules above are their consequence ([NASA/TP-2016-219439, *Rationales for the Lightning Launch Commit
  Criteria*](https://ntrs.nasa.gov/citations/20170001583)).
* **Cold**: Challenger was launched at 36 °F (2 °C), colder than any launch before it, and its solid booster's
  seals failed ([Rogers Commission report](https://history.nasa.gov/rogersrep/genindex.htm)).
* **Precipitation**: flying through rain or hail at speed erodes a vehicle's skin and its heat protection.

### 4.10 Other worlds

KSP's worlds borrow from real ones but are not copies. Pressures are the game's
([KSP wiki](https://wiki.kerbalspaceprogram.com/wiki/Atmosphere)); the densities are rough estimates from the
gas law and the game's gases and temperatures, to be read from the game itself when building. What the wind
does to a craft goes with density times the square of its speed, so "pushes like" says what wind on Kerbin
would push as hard.

| World | Air at the surface (game) | Real world it resembles, and its weather | What is plausible in the game |
| --- | --- | --- | --- |
| **Kerbin** | 1 atm, 70 km deep; a six-hour day; no seasons (its axis is not tilted) ([wiki](https://wiki.kerbalspaceprogram.com/wiki/Kerbin)) | Earth | everything in 4.1 to 4.9. The space centre is on the equator on an east coast, like Kourou or a Cape Canaveral moved south: trade winds from the east, a sea breeze from the sea in the afternoon, frequent afternoon storms in moist weather, dawn fog. A six-hour day squeezes the daily cycle into six hours |
| **Duna** | 0.067 atm, roughly a ninth of Kerbin's density; 50 km deep ([wiki](https://wiki.kerbalspaceprogram.com/wiki/Duna)) | Mars: 0.006 atm, 95% carbon dioxide, density about 0.016 kg/m³; winds 2 to 7 m/s in summer, 5 to 10 in autumn, 17 to 30 in dust storms at the Viking landers ([NASA Mars fact sheet](https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html)). Its strongest storms' winds would barely be felt; the danger is dust on everything and darkness: the 2018 global dust storm blotted out the sun for months and ended the Opportunity rover ([NASA](https://www.nasa.gov/solar-system/the-fact-and-fiction-of-martian-dust-storms/), [JPL](https://www.jpl.nasa.gov/news/nasas-opportunity-rover-mission-on-mars-comes-to-end)). Dust devils in the afternoons; a pink sky and blue sunsets, because fine dust lets blue light through towards the sun ([NASA, Curiosity's sunset](https://science.nasa.gov/resource/sunset-at-mars-gale-crater-curiosity)) | dust storms (local and, rarely, world-wide) that dim and redden the sun and hide the ground, dust devils, thin high clouds, frost. Duna's air is about ten times Mars's, so its winds push harder than Mars's would: a 20 m/s storm wind pushes like about 7 m/s on Kerbin |
| **Eve** | 5 atm, roughly five times Kerbin's density; 90 km deep ([wiki](https://wiki.kerbalspaceprogram.com/wiki/Eve)) | Venus: 92 bar, 464 °C, nearly still at the surface (0.3 to 1 m/s) under a deck of sulphuric acid cloud, with winds of about 100 m/s at the cloud tops that circle the planet in four days ([NASA Venus fact sheet](https://nssdc.gsfc.nasa.gov/planetary/factsheet/venusfact.html)); rain that evaporates before it lands (virga); lightning there is still argued over | a permanent overcast (EVE draws five layers here), dim tinted light under it, slow but heavy surface winds (3 m/s pushes like 7 m/s on Kerbin), fast winds and strong shear high up, rain streaks that end in mid-air |
| **Laythe** | 0.6 atm, about two thirds of Kerbin's density; 50 km deep ([wiki](https://wiki.kerbalspaceprogram.com/wiki/Laythe)) | an ocean world, cooler than Earth | sea weather: trade-wind cumulus, squalls, sea fog; Jool's shadow cools it every orbit |
| **Jool** | 15 atm at its datum, 200 km deep, no surface ([wiki](https://wiki.kerbalspaceprogram.com/wiki/Jool)) | Jupiter: bands of wind up to 150 m/s near the equator, 40 m/s at higher latitudes ([NASA Jupiter fact sheet](https://nssdc.gsfc.nasa.gov/planetary/factsheet/jupiterfact.html)); lightning seen by Juno, often near the poles ([Brown et al., *Nature* 2018](https://doi.org/10.1038/s41586-018-0156-5)) | a descending probe thrown about by shear between bands, severe turbulence, flashes deep in the cloud |
| Mun, Minmus and the rest | none | | nothing |

### 4.11 What this means for the game

* **One wind for everything.** In the real air the gust that rocks a plane is the gust that bends the smoke
  beside it. Flight models treat turbulence as a field frozen in the air and carried by the wind (4.7); the
  explosions' air already treats its eddies the same way. Both can read one field.
* **A few numbers, eased slowly**, describe a place's weather well enough (Arma 3, 3): cloud cover and base,
  rain, fog, wind at 10 m and aloft, how rough. A moving "weather map" says where (Horizon, 3).
* **The space centre's weather** should be tropical-coastal, built round a six-hour day: dawn fog, building
  cumulus, afternoon storms on wet days, a sea breeze, trade winds.
* **Rockets are hurt by what aircraft hardly notice**: wind shear near max-Q, lightning they set off
  themselves, cold. Real launch rules (4.9) are a ready-made game: a "go / no go" for weather.
* **Density decides force**: the same wind pushes about a third as hard on Duna and more than twice as hard
  on Eve. The game's own aerodynamics does this by itself once the air moves.
* **What makes it look real** is mostly light and consistency (the list at the end of section 3), not the
  cleverness of one effect.

---

## Books and papers named above without a link

* Wallace, J. M. and Hobbs, P. V. (2006). *Atmospheric Science: An Introductory Survey*, 2nd edition.
  Academic Press.
* Stull, R. B. (1988). *An Introduction to Boundary Layer Meteorology*. Kluwer (Springer).
* Rakov, V. A. and Uman, M. A. (2003). *Lightning: Physics and Effects*. Cambridge University Press.
* Few, A. A. (1975). "Thunder". *Scientific American* 233 (1), July 1975.
* Ribner, H. S. and Roy, D. (1982). "Acoustics of thunder: a quasilinear model for tortuous lightning".
  *Journal of the Acoustical Society of America* 72 (6).
* Fujita, T. T. (1985). *The Downburst: Microburst and Macroburst*. SMRP Research Paper 210, University of
  Chicago.
* Marshall, J. S. and Palmer, W. M. (1948). "The distribution of raindrops with size". *Journal of
  Meteorology* 5.
* Locatelli, J. D. and Hobbs, P. V. (1974). "Fall speeds and masses of solid precipitation particles".
  *Journal of Geophysical Research* 79 (15).
* Moon, P. and Spencer, D. E. (1942). "Illumination from a non-uniform sky". *Illuminating Engineering* 37
  (the overcast sky later standardised by the CIE).
* U.S. Department of Defense (1980). MIL-F-8785C, *Flying Qualities of Piloted Airplanes*; and (1997)
  MIL-HDBK-1797, *Flying Qualities of Piloted Aircraft*.
