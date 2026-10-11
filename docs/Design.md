# Weather: a design

Written 2026-10-09, before anything was built, as a proposal; kept as it was written. It rests on the
[research](Research.md). **What was built from it** (the same evening, after the user's answers) is described
in [How it works](How-it-works.md), under the name **Natural Weather**. Where the building parted from this page:
the user chose our own volumetric clouds over EVE's, so clouds (and EVE's hiding) are the clouds part's, drawn from
this weather through Keystone's `Weather`; Unity's own fog turned out to be missing from the game's shaders, so fog
and dust as volumes are the clouds' too; lightning, the city's real weather, a rainbow, splashes and the sounds of
rain and wind were added; and the user's answers to the questions at the foot of this page are in the to-do list.

## In short

One weather for every world that has air, the same for everyone at a given place and moment, that both
**looks** and **pushes**. The wind that rocks a plane is the wind that bends the smoke beside it and slants
the rain past it. It is built on what this workspace already has: the explosions' simulated air (smoke that
already drifts in a wind), Natural Light (the sun, the sky's light, the camera's eye), Keystone (the
settings window), Rocket Smoke and Engine Flames (engines feeding that air).

The first version is small enough to build and look at in a day: **wind on craft and on smoke, the light of
an overcast day, and rain**. Lightning and thunder, fog, clouds of our own and Duna's dust storms come after,
each as its own step.

## What it adds that the others do not

From the [research](Research.md#what-none-of-them-does-the-gap):

* One weather that is both seen and felt, for the game's own aerodynamics, free, with no other mod needed
  but Keystone and Harmony (both already installed here).
* Rain and storms without the paid clouds, drawn in the game's own shaders.
* Weather for launching rockets: wind at the pad, shear aloft at max-Q, lightning the rocket sets off, and a
  "go / no go" with a forecast, as real launches have.
* The light of an overcast day.
* Smoke, explosions and engine trails moving in the same gusts the craft feels.

## How it should behave

1. **One air.** One wind field (a mean wind, its change with height, and gusts frozen into the air and
   carried along by it) that everything asks: the craft's parts, the smoke, the rain, the dust.
2. **The same weather for the same place and moment**, worked out from the place and the game's clock
   alone. Nothing is stepped forward and nothing is saved: time warp skips ahead for free, a quickload brings
   back the same sky (good for tests and for pictures), and the weather can be forecast.
3. **Realistic, not cartoony.** Light and agreement first (research, section 3's list); every effect drawn
   with what already looks real here (the explosions' volume, Natural Light's light) or kept thin, sparse
   and lit by the scene (rain).
4. **No new shader where it can be helped.** Where one is needed, it is OpenGL only for now (rule of the
   workspace), the mod notices on Direct3D that it has none and goes on without that one thing.
5. **Make way for other weather mods.** If one already does a thing, follow it instead of fighting it.
6. **Honest about gameplay.** Wind is a real force on craft, with a switch to make it look-only.

---

## The weather itself

### What each world gets

| World | Wind | Sky and light | Falling | Special | From version |
| --- | --- | --- | --- | --- | --- |
| **Kerbin** | trade winds from the east near the equator, westerlies further out, a sea breeze on coasts in the afternoon, calm nights; a jet stream at 8 to 12 km | clear, fair-weather cumulus, overcast, storms | rain; snow where the game's temperature is below freezing (poles, mountains) | afternoon thunderstorms, dawn fog | 0.1 (wind, overcast, rain), 0.2 (storms), 0.3 (fog) |
| **Duna** | 2 to 10 m/s, 17 to 30 m/s in storms (Mars's figures), pushing about a third as hard as the same wind on Kerbin | the sky's colours stay Scatterer's; dimmed and reddened in storms | none | dust storms, local and rarely world-wide; dust devils in the afternoon | 0.1 (wind), 0.4 (dust) |
| **Eve** | slow at the ground (0.5 to 3 m/s, but pushing like 1 to 7 m/s on Kerbin), fast and sheared high up | a permanent overcast under EVE's five layers | rain that evaporates before it lands | lightning in the deck | 0.1 (wind, overcast), 0.4 (the rest) |
| **Laythe** | like Kerbin's oceans | sea weather | rain | sea fog, squalls | 0.1 (wind), later |
| **Jool** | bands of 50 to 150 m/s with hard shear between them | | | severe turbulence, flashes deep in the cloud | 0.1 (wind), later |
| Airless worlds | none | | | | |

### The few numbers that describe one place

For any place and moment the weather gives: **cloud cover** (0 to 1) and **cloud base** and top; **rain**
(0 to 1, snow when cold); **wind at 10 m** (speed and direction), **the boundary layer's depth** and **the
wind aloft** up to the jet; **how rough the air is** (turbulence strength and size); **storm activity**
(flashes a minute nearby); **how far one can see**. Temperature and pressure stay the game's own: this mod
moves the air, it does not change what it is.

### How it is chosen, and how it changes

A **weather map** for each world, as in Horizon's clouds, but made by the mod from smooth noise instead of
painted: a few fields over the whole planet, each a sum of large and small waves.

* **Moisture** says where the weather systems are. Its large waves drift with the winds aloft (eastward in
  middle latitudes, westward near the equator) and change shape over a Kerbin day or two. Where it is high
  the sky is overcast and it rains; where it changes steeply there is a front, a band of heaped cloud and
  showers with a wind shift.
* **Instability** follows the sun: low at night, building through the morning, highest in the afternoon,
  stronger over land than sea. With enough moisture it turns cumulus into showers and storms, which live for
  half an hour to an hour.
* **The game's own temperature** at the place says rain or snow, and dawn plus damp plus calm says fog.
* **The wind** is the world's prevailing wind for that latitude, plus the systems' own winds, plus the sea
  breeze, plus storms' gust fronts; strongest by day near the ground, calm at night.

Every field is worked out from (world, latitude, longitude, game time) through fixed noise. That is what
makes it the same every time and lets time warp skip ahead. A world's **climate** (prevailing winds by
latitude, how often each kind of weather, how strong) is a small config with defaults for each stock world,
so planet packs can add their own.

At the space centre, as proposed (all to be tuned by eye):

| Time of a Kerbin day (6 h) | Most likely |
| --- | --- |
| Night | clear or high cloud; calm or a light land breeze |
| Dawn | fog or low cloud on damp days |
| Morning | cumulus building; the wind picks up |
| Afternoon | the sea breeze from the east, 4 to 8 m/s; showers or a storm on about one day in four |
| Evening | storms dying away, clearing |

Over several days: fair weather about half the time, showery a quarter, a wet overcast system a sixth,
stormy the rest. Wind at 10 m mostly 3 to 7 m/s, strong days 10 to 14, storm gusts 18 to 25.

**Choosing the weather.** A setting on the mod's page: "As it comes", or one of Clear, Fair, Windy, Overcast,
Rain, Storm, Fog, held where the craft is and eased in over a minute or two (Arma 3's way: never a jump).
For pictures and for practising crosswind landings.

**Forecast.** Because the weather is a function of time, the page can show the next few hours at the
craft's place: "rain from about 2 h 10 m, clearing by 3 h", and when a launch is next likely to be "go".

### Agreeing with EVE's clouds

EVE draws Kerbin's clouds from one picture that turns round the planet as a whole at 30 m/s, the same
pattern coming back about every 35 hours of game time. So the sky over the space centre does change, but by
EVE's rule, not the weather's: if the weather says rain under EVE's clear patch, the two disagree. Two ways:

* **Follow EVE** (for 0.1, where EVE is installed): read EVE's cloud picture overhead, turned as far as EVE
  has turned it by now (a copy read back from the graphics card, as Natural Light reads the planet's colour
  map), and let it set the cloud cover; the rest (instability, storms, wind) stays the weather map's. What is
  drawn and what is felt then agree, at EVE's pace.
* **Paint EVE** (later): give EVE's Kerbin layer a picture drawn from the weather map, redrawn every few game
  minutes, so that EVE's own clouds become weather systems with fronts that form, move and die. EVE is open
  source (MIT) and a layer's picture is a property of its material; its in-game editor changes layers live,
  so it can be done, but it is untested and depends on EVE's insides.

---

## What it does to flight

### What changes

The air moves. Every part's airspeed becomes its own velocity **minus the wind where it is**. Drag, wings'
lift, body lift, parachutes, dynamic pressure and Mach number all follow, because the game works them all out
from that airspeed. What does not change: the navball's surface speed (it is speed over the ground, as a
real one is), orbits, and craft on rails (time warp beyond physics warp), which feel nothing, as in the game
now.

### How it gets into the game

* **The game's own aerodynamics** (what this user plays with): three small changes made with Harmony as the
  game runs (Harmony is installed here, and Keystone already uses it to measure what each mod costs). The
  game works out a part's airspeed in three places: the drag sums (`FlightIntegrator.UpdateAerodynamics`),
  wings (`ModuleLiftingSurface`) and control surfaces (`ModuleControlSurface`). In each, the one call that
  adds the frame's velocity (`Krakensbane.GetFrameVelocity`, or `GetFrameVelocityV3f` in the two wing
  modules) is swapped for one that also takes away the wind at that part. Nothing else in the game is touched.
* **With FAR**: through WindAPI if it is installed (so its winds add to ours), otherwise through FAR's own
  wind slot.
* **With the Kerbal Weather Project, Advanced Atmosphere Tools or CPWE**: they hold ModularFlightIntegrator's
  slot and push craft themselves. This mod then pushes nothing, and draws smoke and rain in their wind (read
  through Advanced Atmosphere Tools' interface; the Kerbal Weather Project's is to be found).
* **A way with no Harmony at all**, if wanted: add the difference as forces each step. The game's drag law
  was matched to 0.1% when AI Bridge learned to predict drag, and wings' lift can be had from their own
  public methods. More work, and less exact for wings.

### Gusts and turbulence

* **Near the ground** the wind grows with height (a power law, as the explosions' air has now) up to the top
  of the boundary layer, about a kilometre by day and a few hundred metres at night, then turns towards the
  wind aloft. On a strong day the jet at 8 to 12 km reaches 30 to 40 m/s, and the change of wind with height
  (shear) near it can be 5 to 10 m/s a kilometre.
* **Turbulence** follows the flight simulators' standard (MIL-F-8785C, research 4.7): near the ground the
  vertical gusts' typical size is a tenth of the wind at 6 m, the along-wind and sideways ones larger; the
  eddies are as big as the height above the ground, up to 530 m higher up. Rough in and under cumulus and
  storms, rougher by day than by night, smooth in clear, stable air aloft.
* **Made as a field frozen in the air**: a few dozen waves of the right sizes and strengths, drifting with
  the wind, read from a table of sines (as the explosions' gusts already are). Any point at any moment can be
  asked, which is what lets the smoke and the craft feel the same gust. A fast craft runs through the short
  waves quickly; a parachute rides the long ones.
* **Thermals** by day under fair-weather cumulus: air rising at 1 to 5 m/s, sinking between.
* **Events**: a storm's gust front (the wind ramps up by 10 to 20 m/s over a few seconds, in the smooth
  "1 minus cosine" shape, and turns cold); microbursts under storms (rare, and a setting); dust devils on Duna.

### What a player will notice

* **Planes**: crosswind on take-off and landing, gusts, shear near the ground, bumpy air under cumulus.
  Landing into the wind starts to matter.
* **Rockets**: a drift at lift-off, more for a slow heavy one; at max-Q the jet's shear pushes the nose
  aside, and a rocket that was only just stable may flip earlier than it would have. Real launches wait for
  this.
* **Parachutes**: a capsule drifts downwind for as long as it hangs: 6 m/s for a minute is 360 m off target.
* **Landers**: on Duna the wind pushes gently; on Eve even a slow wind pushes hard, and a hover needs
  correcting.
* **On the ground**: a tall, light lander or rover in a gale can tip over, as real ones could. (Whether to
  soften the wind on landed craft is a question below.)
* **Autopilots**: the game's SAS copes. The AI Bridge's programs do not know about wind yet (they steer by
  speed over the ground), nor do landing predictions in MechJeb or Trajectories unless FAR is in use.

### Settings

On the mod's Keystone page: **wind moves craft** (off makes the weather look-only), **strength** (0 to 2,
for wind and gusts together), **weather** (as it comes, or one chosen), **rain and snow drawn**, and later
**lightning can strike rockets** (off as installed).

### Launch weather

A part of the page, and the line under the mod's name in Keystone's list, says "go" or "no go" for the pad
the craft stands on, with the reason, adapted from NASA's rules for a crewed Falcon 9 (research 4.9):

| Rule | As proposed |
| --- | --- |
| Wind at the pad | no go if the sustained wind 50 m up exceeds 13 m/s (30 mph) |
| Shear aloft | no go if the wind changes by more than a set amount a kilometre between 5 and 15 km, along the way the craft is pointed |
| Lightning | no go for 30 minutes after a flash within 18.5 km (10 nautical miles) |
| Storms | no go within 18.5 km of a storm's anvil |
| Rain | no go with rain on the pad |
| Thick cold cloud | "risk of lightning set off by the rocket" if the climb goes through cloud more than 1.4 km thick that reaches below freezing |

With the forecast it can say when the next "go" is likely. It is advice and nothing more, unless lightning
strikes are switched on.

---

## What it looks like

Each thing to be drawn, how, what Windows would see, and when.

### The light of an overcast day (0.1)

Through Natural Light, which already sets the sun, the sky's light and the camera's eye. Cloud cover above the
craft takes the sun's direct light away by as much as the cloud is thick, softens and then removes shadows,
and turns the sky's light grey and brightest overhead (the standard overcast sky: three times as bright
overhead as at the horizon). The eye opens to make up most of it, as a camera would, so the scene goes flat
and grey, not murky. The explosions' smoke follows by itself (it is lit by the same sun and sky). Under a
storm it is darker still.

**Windows**: no shader; the same. **Cost**: nothing measurable.

**Known gap**: Scatterer still paints a blue sky. Under a heavy overcast the visible sky should be grey;
EVE's layer covers some of it. A grey sky needs EVE painted (above) or Scatterer's sky tinted through its
settings, to be tried.

### Smoke and explosions in the same wind (0.1)

A small way in, in the explosions mod's `Sources` (where Rocket Smoke and Engine Flames already come in): the
weather mod hands each patch of air the wind at 10 m, its direction, the boundary layer, how gusty and how
rough, and the gust field's place and time. A patch asks when it is made and twice a second after, on the
game's thread, never for each particle; its own gusts and eddies then run on those numbers instead of the
ones it makes up now. Without the weather mod nothing in the explosions mod changes.

**Windows**: the volume is OpenGL only, as now; on Direct3D the explosions draw their sprites, in the same
wind. **Cost**: nothing.

### Rain (0.1)

* **Drawn with Unity's particle system** (which runs in the game's native code, so it costs the processor
  almost nothing) as **stretched billboards**: each drop a thin streak whose length follows how fast it
  crosses the view, using the game's own premultiplied particle shader. No new shader.
* In a box about 60 m round the camera, falling at real speeds (5 to 9 m/s for rain, slower for drizzle),
  slanted by the same wind, streaked along the craft's own motion when the camera rides with it.
* **Lit as the scene is**: its colour and brightness set every frame from Natural Light's sun, sky and lamps
  where the camera is, the way Natural Light already tints the game's smoke. Rain shows most where light
  falls on it from behind (research 3), so **small emitters inside each floodlight's beam and round engine
  flames** draw the drops those lamps light. That is the strongest cue there is, and cheap.
* **Streak length from the exposure**: Cinema Camera's shutter when it is set, otherwise a sixtieth of a
  second.
* **Splashes** where drops hit the ground or the craft: tiny and short-lived, from the particle system's own
  collisions. None on water at first.
* **Heavy rain greys the distance** (later, with fog).

**Windows**: the game's own shader and Unity's particles: should be the same (untested, like everything here
on Windows). **Cost** (estimate): 0.3 to 0.8 ms a frame for 10,000 to 20,000 drops.

**Risks**: the near camera's temporal smoothing (TUFX) smears small fast things (found making Engine
Flames): rain may have to be drawn after it, at the end of the frame as Keystone's lens is. And rain is the
one effect here that could look stuck on; hence thin, sparse in daylight, lit only by the scene. If it does
not pass the user's eye it is held back.

### Snow (0.2, or with rain if it comes cheap)

The same particles with flakes falling at about 1 m/s, riding every eddy (they show the turbulence better
than anything), wherever the game's temperature at the place is below freezing. No snow lying on the ground.

### Lightning and thunder (0.2)

* **Storm cells** from the weather map, each with its own flash rate. Most flashes stay in the cloud; some go
  to the ground.
* **The bolt** grows as a crooked, branching path, stepping downward with side branches (the "stepped
  leader" of real lightning), drawn as a thin strip cut into short lengths (as Natural Light's lamp beams are)
  with the game's additive particle shader. Its brightness flickers through three or four strokes in a fifth
  to half a second, and the film's glow (TUFX) blooms it.
* **The light**: a bright lamp of the cheap kind at the bolt and a brief jump in the sky's light (Natural
  Light), so the ground, the craft and the rain flash with it. A flash inside a cloud lights the cloud from
  within only for clouds this workspace draws (the explosions' volume already takes lamps); EVE's clouds
  can only be brightened as a whole.
* **Thunder made by the mod from the bolt's shape** (Ribner and Roy, research 4.5): each length of the
  channel cracks, and is heard after its own distance divided by the speed of sound, which makes the rumble.
  No sound files needed.
* **A rocket climbing through cold cloud near a storm** may be struck if that setting is on: a flash, and
  perhaps a probe core or flight computer reset, as Atlas-Centaur 67 was.

**Windows**: no new shader. **Cost**: small, and only during flashes.

### Fog and low cloud (0.3)

Dawn fog, sea fog, and a cloud base to fly up through. Three ways, to be chosen after a test:

1. **Unity's built-in fog**, if the game's shaders still carry it. The game never turns it on, so Unity
   probably left it out when the game was built; one test settles it, and if it is there it costs nothing
   and works on Windows too.
2. **A fog that thickens with distance and lies low**, drawn over the whole picture: one small new shader,
   OpenGL only; on Direct3D, no fog.
3. **Fog banks as patches of the explosions' air**: simulated, lit, with billows, the user's standard;
   OpenGL only (sprites on Direct3D), and dearer.

### Clouds (later: a choice for the user)

* **As installed, EVE draws the clouds** and the weather agrees with it (above).
* **Option A, simulated cumulus near the craft**: a handful of cumulus within a few kilometres, each a patch
  of the explosions' air: a thermal rising from the ground, turning to cloud above the cloud base and
  billowing, lit like the smoke, growing over minutes. Nobody has anything like it, and it meets the user's
  bar, but it is OpenGL only and each costs about what a fire does.
* **Option B, sky-wide volumetric clouds** like Horizon's: weeks of work, the same ground as blackrack's paid
  clouds, OpenGL only. Not recommended unless the user wants free volumetric clouds badly.

### Duna's dust (0.4)

Dust storms dim and redden the sun (Natural Light), hang a rust haze in the air (the fog of 0.3) and hide the
ground. Near the ground, blowing dust and dust devils as patches of the explosions' air (whose version 0.5.0
lifts dust off bare ground under engines, not yet seen working). Solar panels could give less in a storm (a
setting).

### Later still

* **Wet ground**: darker and shinier, if the game's terrain material takes a tint and a shine per object
  (some of the game's materials have `_SpecColor`); otherwise a shader.
* **Drops on cockpit windows**: needs a shader that bends the view through each drop; OpenGL only.
* **Waves**: Scatterer's sea has a wind speed; setting it from the weather may need its sea rebuilt.
* **Sound**: rain hiss and wind on the hull, made by the mod as thunder is.

---

## Getting along with other mods

| Mod | What happens |
| --- | --- |
| EVE Redux, free (installed) | keeps drawing the clouds; the weather follows its cloud picture or, later, paints it |
| Scatterer (installed) | keeps the sky, haze and sea; Natural Light already allows for Scatterer's dimming of the sun; its blue sky under overcast is a known gap |
| TUFX (installed) | the film's glow blooms lightning; temporal smoothing is a risk for rain |
| blackrack's True Volumetric Clouds | it owns the sky: its clouds, rain, snow and lightning are drawn and ours are not; the weather follows its clouds (as Volumetric Aware Weather does, by looking into them above the craft) and supplies the wind on craft and smoke |
| Kerbal Weather Project, Advanced Atmosphere Tools, CPWE | they push craft; ours stands down from that and draws in their wind |
| FAR | wind through WindAPI or FAR's wind slot |
| WindAPI and its mods (KerbalSoaring, RidgeLift, ...) | ours is one more provider; their winds add up |
| Weather Driven Solar Panel | reads only blackrack's clouds; unaffected |
| Volumetric Explosions, Rocket Smoke, Engine Flames | their air takes the weather's wind |
| Natural Light | takes cloud cover; lightning's flash; lamps light the rain |
| Cinema Camera | its shutter sets rain's streak length |
| AI Bridge | should learn the wind: report it, land into it, allow for drift (a question below) |

## What it costs (estimates; nothing measured yet)

| Thing | Frame time | Why |
| --- | --- | --- |
| Working out the weather | under 0.05 ms | a few noise sums, a few times a second |
| Wind on craft | under 0.05 ms | one look-up a part a physics step, a few dozen sums each |
| Smoke in the weather's wind | nothing | numbers handed over twice a second |
| Overcast light | nothing | Natural Light already does the work |
| Rain or snow | 0.3 to 0.8 ms | particles in native code; thin streaks drawn over the scene |
| Lightning | small | a strip, a lamp, a sound, during flashes |
| Fog over the whole picture | 0.3 to 0.5 ms | one pass |
| Fog banks, dust, cumulus in the explosions' air | 2 to 3 ms each in view | as a fire costs ([What it costs](https://github.com/IshiakiZ/ksp-volumetric-explosions/wiki/What-it-costs)) |
| Sky-wide volumetric clouds | 2 to 5 ms | what such renderers usually cost |

## Windows

Nothing in this workspace had run on Windows when this was written, so none of this is a claim that it works
there. (Since then the workspace's shaders have been built for Direct3D, the clouds' among them, and the other
mods tried on Windows, and this one too on 2026-10-10: see [Limits](Limits.md).)

| Thing | New shader? | On Windows |
| --- | --- | --- |
| Wind on craft, launch weather, forecast | no | the same |
| Overcast light, lightning's light | no | the same |
| Rain, snow, splashes, lightning bolts | no (the game's particle shaders) | the same |
| Thunder and other sounds | no | the same |
| Smoke in the weather's wind | the explosions' volume (OpenGL) | the explosions' sprites, in the same wind |
| Fog over the picture | yes (OpenGL only) | no fog |
| Fog banks, Duna's dust, simulated cumulus | the explosions' volume | sprites or nothing |
| Drops on windows, wet ground (if a shader) | yes | none |

---

## The first version: 0.1.0

### What is in it, most important first

1. **The weather**: the weather map, Kerbin's climate (its cloud cover following EVE's clouds where EVE is
   installed), the other worlds' winds and Eve's overcast; the Keystone page with the weather now, the wind
   at 10 m and aloft, gusts, rain, the next few hours, the launch "go / no go", and the settings.
2. **Wind on craft** through the game's aerodynamics (three Harmony changes), with the look-only switch, and
   FAR's slot or WindAPI where they are installed.
3. **Smoke and explosions in the weather's wind** (the way into the explosions mod).
4. **The light of an overcast day** (a way into Natural Light).
5. **Rain**, lit by the scene and by lamps, with splashes; snow if it comes cheap.

If the day runs short, rain moves to 0.2 and the first version is wind, light and the launch page.

### What it needs

* **A new mod**, `src/Weather` (working name "Weather"; repository `ksp-weather`), standing on Keystone 0.6
  and Harmony. It reaches the explosions mod and Natural Light only from small classes of its own, as Rocket
  Smoke reaches the explosions, so it loads and says what is missing without them.
* **Small changes in shared mods**: the explosions mod's `Sources` takes a wind (about forty lines;
  `Site.Wind` asks it, and `TestWind` still wins for tests); Natural Light takes cloud cover (about thirty
  lines). Better still, a shared "weather here" in Keystone that Natural Light reads, as `Keystone.Engines` is
  what Rocket Smoke and Engine Flames agree on (the explosions mod does not stand on Keystone, so it keeps
  its own way in).
* **No new shader.**
* **Game turns**: three or four of twenty minutes:
  1. Wind on craft: a capsule under parachute from 2 km must drift at the wind's speed (`kerbin_hill`); a
     plane in a crosswind (`aeris_island`); the Kerbal X held on the pad, then launched; the look-only switch.
  2. Smoke on the pad (`fx_pad`) leaning and gusting with the weather, with the rocket swaying in the same
     gusts.
  3. Rain by day and night at the pad: pictures, frame time with and without, and a live recording to see
     what temporal smoothing does to it.
  4. Overcast against clear, each from a fresh load, checked side by side.
* **Time**: about a day: the weather and page 2 hours, wind on craft 2, the smoke's way in 1, the light 1,
  rain 3, and the turns.

### Risks

* The Harmony changes are in the game's scrambled ("obfuscated") code: the right call has to be found in
  each of three methods. KSP 1 is finished, so once found it stays found.
* Rain under temporal smoothing, and rain looking stuck on.
* EVE's clouds and the weather disagreeing: following EVE's picture should cure it, if the picture can be
  read back as EVE has turned it.
* Scatterer's blue sky under an overcast.
* Light craft tipping over on the ground in strong wind.
* Nothing tried on Windows.

## Later versions

* **0.2**: storms: lightning, thunder, gust fronts, the lightning risk at launch; snow if not in 0.1.
* **0.3**: fog and low cloud; distance greyed by rain.
* **0.4**: other worlds: Duna's dust storms and dust devils, Eve's deck, virga and lightning, Laythe's sea
  weather, Jool's bands and turbulence.
* **0.5**: agreeing with others: EVE painted from the weather map, blackrack's clouds followed, the Kerbal
  Weather Project and Advanced Atmosphere Tools read; Scatterer's sea following the wind.
* **Then**: clouds of our own (option A or B above, if wanted), wet ground, drops on windows, icing for
  planes as a setting (ice on wings in cold cloud: less lift, more drag, research 4.8), rain and wind sounds.

## Questions only the user can answer

1. **Should wind push craft from the start**, or should the first version be look-only with pushing as a
   switch to try?
2. **How lively should Kerbin be?** Weather like a tropical coast (storms most afternoons in wet spells), or
   mostly fair with weather now and then?
3. **Clouds**: are EVE's clouds as installed good enough to build on (and to paint from the weather), or do
   they want clouds of our own (simulated cumulus near the craft, or volumetric clouds over the whole sky, a
   much bigger job)? Would they ever use blackrack's paid clouds (then ours would follow them)?
4. **After rain, which first**: lightning and thunder, fog, Duna's dust storms, or snow?
5. **Consequences**: should lightning be able to strike a rocket, and should strong wind be allowed to tip
   over landed craft, or only ever be felt in flight?
6. **The launch "go / no go"**: wanted, or noise?
7. **The AI Bridge**: should Claude's flying learn the wind (land into it, allow for drift, wait for a "go")?

## To find out in the game

* Where exactly the frame-velocity call sits in each of the three methods, and that wings and parachutes then
  feel the wind (parachutes take their direction from the part's airspeed, so they should).
* Whether Unity's built-in fog still works with the game's shaders.
* Whether the terrain's material takes a tint and a shine per object (wet ground without a shader).
* Whether EVE's turning cloud picture can be read back at the craft's place (to follow it), and swapped
  while the game runs (to paint it).
* Whether a light lander on its legs in a 20 m/s wind slides or tips.
* What temporal smoothing does to rain, and whether drawing it at the end of the frame cures it.
