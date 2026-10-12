# How it works

Natural Weather's own code is in `src/NaturalWeather`. What it found out before it was built is in
[Research](Research.md), and what it set out to be in [Design](Design.md); this page is what it is.

## The weather itself

Everything is worked out from the place and the game's clock alone (`Model.cs`). Nothing is stepped forward
from one moment to the next and nothing is saved, so:

* the weather at a place and moment is the same for every mod that asks, and after a quickload;
* time warp skips ahead for free;
* the weather of the coming hours can be worked out now: that is the forecast.

A world's weather is a few smooth fields over the whole planet, each a sum of large and small waves of noise
("a weather map", as Horizon's clouds are drawn from, but made instead of painted):

* **Moisture** says where the weather systems are. Its pattern drifts with the winds aloft (westward in the
  trade-wind belt near the equator, eastward in the middle latitudes) and changes into a new one over about
  three hours of game time. Where it is high there is a sheet of cloud and rain; where it is low the sky is
  clear; at the edges of wet areas, where it changes, showers line up as along a front. It is wetter near the
  equator and in the storm tracks, drier in the subtropics and at the poles, and the setting **Bad weather**
  moves it all wetter or drier (0.25, as installed, is mostly fair).
* **The sun's warming** follows the sun at each place, lagging it: air rises most in the middle of the
  afternoon, more over land than over the sea. With enough moisture it heaps fair-weather cumulus through
  the day and, wetter still, showers and thunderstorms.
* **Storms and showers** are cells on two lattices of places about 22 km apart, each with a chance of a cell
  every cycle of a hundred minutes, the two lattices half a cycle apart. A cell is born, grows for a quarter
  of an hour, rains for half an hour and dies; it is as strong as the warming and the moisture where it is
  allow; the strongest are thunderstorms. They drift with the weather.
* **The game's own temperatures** (by latitude and height, read from the game's curves when a world is first
  seen) decide rain or snow, the height of freezing (for the launch rules and for lightning set off by
  rockets), and **dawn, damp and calm** together make fog.

Each world has its own climate (`World.cs`): Kerbin's as above; Laythe wetter and more maritime; Duna dry, its
weather dust storms (local ones often, now and then for weeks one over the whole world) with strong winds;
Eve under a deck of cloud that never goes away, its air slow at the ground and fast high up, its rain drying
before it lands; Jool in bands of wind of a hundred metres a second and more, rough throughout. A world from a
planet pack gets a climate worked out from its air and whether it has seas.

The land and the sea are read from the game's terrain once (a coarse map, made a few dozen points a frame
when a world is first wanted).

## The wind

At 10 m above the ground (`Model.Make`): the prevailing wind for the latitude (trade winds from the east near
the equator, westerlies further out, polar easterlies), the weather systems' own winds (round the wet lows, as
the world's turning makes them, and straight into them near the equator), a sea breeze on coasts by day and a
land breeze by night, and storms' outflow (the gust front that runs out ahead of a storm's rain). Near the
ground the air is mostly still (since 0.2.0): at night and through the morning the ground cools the air over it
into a layer the wind above slides over; by day the sun's stirring brings a little of the wind above down in fair
weather and most of it in unsettled weather, when a system's own wind blows (and all of it on a held "Windy" day).
Measured over eight days at the space centre's pad, every ten minutes, the weather as it comes, the mean wind 2 m
up was still (under 0.5 m/s) 53% of the time, light (to 2 m/s) 45%, a breeze (to 5) 1.4%, moderate (to 10) 1%, and
stronger only in a storm's gust front; by night still 72% of the time; 0.6 m/s on average (0.9 at 10 m). Asked for:
smoke near the ground drifts slowly, the wind "90% of the time zero, and then the rest minimal wind, and very, very,
very rarely strong wind". (Before 0.2.0 it was as windy as a real coast: still 3% of the time, a breeze or more 47%,
2.5 m/s on average 2 m up. The "Wind" setting still makes it as strong as anyone likes.)

With height (`Model.Mean`) it grows as a power of the height up to the top of the layer of air the ground
holds back (about a kilometre by day, a few hundred metres at night), turning as it goes, then towards the
wind high up: a jet stream about 10 km up on Kerbin (westerly in the middle latitudes, weaker and easterly over
the tropics, meandering over days), and now and then a thin layer where it turns and strengthens sharply
(what scrubs real launches for "upper-level wind shear").

**Gusts** (`Gusts.cs`) are the flight simulators' turbulence: von Kármán's spectrum, with the sizes and
strengths of MIL-F-8785C near the ground (the vertical gusts a tenth of the wind at 6 m, the eddies as big as
the height), rough inside and under heaped cloud, rougher still near storms, smooth in clear air aloft. They
are a field frozen into the air and carried along by the wind: twenty waves of every size from 4 m to 2 km
in each of three directions. Any point at any moment can be asked, so a craft, the smoke beside it and the
rain all feel the same gust; the pattern is renewed every minute and a half.

## The wind on craft

The game works out how fast the air goes past a part in three places, and in each takes the air to be still.
Three small changes, made with Harmony as the game runs (`Aero.cs`), take the wind away there:

* the drag sums (`FlightIntegrator.UpdateAerodynamics`): just before a part's airspeed is stored, which drag,
  body lift, parachutes and dynamic pressure are all worked out from;
* wings (`ModuleLiftingSurface.FixedUpdate`) and control surfaces (`ModuleControlSurface.FixedUpdate`): the
  frame's velocity, the moment it is fetched, less the wind.

The game's code is matched by the names of what it calls, and the log says how many places were changed in
each ("the drag sums changed in 1 place(s), wings in 1, control surfaces in 1").

The wind over a craft is worked out once a physics step: at its middle, and (for the craft being flown) six
more points a few metres off each way, so that each part gets the wind where it is and gusts that differ along
the craft turn it. Other craft about feel the wind once at their middle. **Wind moves craft** off makes the
weather look-only; **Wind can tip landed craft** off spares craft on the ground or the sea.

What does not change: the navball's surface speed (it is speed over the ground), orbits, and craft on rails.

## What is drawn, and how

* **Cloud light** (with Natural Light 0.10.0): the clouds over the craft, and along the line to the sun, take
  the sun's direct light (by how thick each layer is: thin cirrus little, a storm all of it); the sunlight
  they take is spread over the sky, which then lights everything more evenly and greyer (`Provider.ShadeAt`,
  `Keystone.Weather.Shade`). Each layer lets through 1 / (1 + 0.15 x its optical depth) of the light (the
  two-stream rule for cloud), so an overcast of stratus leaves about a quarter of a clear day's light on the
  ground, a raincloud a tenth, a thunderstorm's base a twentieth: the ranges measured on real days are a tenth
  to a quarter, a twentieth to a tenth, and less. Natural Light works the light out from a clear day's (its sun
  and its sky as they show on level ground) and opens the camera's eye for what the clouds take, as a camera's
  meter does. Heaped cloud with blue sky between greys the light much less than a sheet of the same cover.
  Where our own clouds are drawn, they dim the sun themselves and this leaves it alone.
* **Rain and snow** (`Rain.cs`): Unity's particles with the game's own particle shaders, round the camera:
  drops as thin streaks (3 to 5 cm wide, never more than three pixels in a picture 1080 high, in a box 24 m
  about the camera: beyond that a drop is too thin to see), smeared over a twentieth of a second as a film
  camera's shutter would, falling at real speeds (4.5 to 8.5 m/s), slanted by the same wind, streaked along the
  craft's own motion when the camera rides with it; more drops in the light of each lamp near the camera (how
  rain is seen at night), in that lamp's colour; a hiss made by the mod. Snow is flakes falling at about a metre a
  second, carried about by turbulence. Dust blows along the ground in Duna's storms.
  * **How they are lit** (since 0.2.0). A drop is a lens: it shows a small, upside-down, wide-angle picture of
    what is round it, the sky above all, and none of what is straight behind it (Garg and Nayar). Smeared into a
    streak it covers what is behind for only the moment it passes, and adds its little picture: so drops are drawn
    *added* to the picture (the game's additive particle shader), as bright as a fifth of the light from the half
    of the world in front of the camera (the scene's ambient light, as Natural Light sets it, facing the way the
    camera looks: mostly sky looking up, mostly ground looking down), and glinting towards the sun where the sun
    is out. They show as fine silvery lines against anything darker than the sky and hardly at all against the sky,
    never as dark smears. (In 0.1.0 they were laid over the picture in the colour of the light round the camera,
    which by day is darker than the sky behind: dark smears.)
  * A flake throws back most of the light on it, every way: it is drawn as bright as white snow on the ground in
    the same light, lit from above by the sky, from behind the camera, and by the sun (brighter on the side the sun
    is, with a glint towards it). Against a bright sky it is no brighter than the sky, and greyer looking up into
    it, as real snow is; against anything darker, and against the veil of the snow itself, white.
  * **The distance** is Natural Light's (0.10.0): beyond the 24 m of drops and flakes, it veils what the camera
    sees by as much as the weather says one can see (`WeatherHere.Seeing`), up to the weather's top (the cloud
    base in rain and snow), in the light of the sky and the ground and brighter towards the sun. In thick snow
    the hills whiten into the distance; in heavy rain the far side of the pad greys out.
* **Lightning** (`Lightning.cs`): when each storm flashes is part of the weather (from the storm and the clock),
  several times a minute for a strong one, most within the cloud; each flash is announced through
  `Keystone.Weather.Flashed` (our clouds light themselves from inside by it: until a mend in 0.2.2, not yet looked at
  in the game, they put that light at the wrong longitude, see the clouds' page). A flash to the ground is drawn as a crooked, branching
  channel that flickers through its strokes: a core some three pixels wide at any distance, added to the picture at
  more than twice white in its middle so that a pixel and a half of it stays white through the game's smoothing of
  thin lines (until 0.2.2 it was two thirds of a pixel of white, which the smoothing left a quarter-grey hair), a
  glow of some twenty pixels round it, and the channel still glowing for about a tenth of a second after each stroke,
  as a real one does; the scene is lit from it; its thunder is made from the channel's shape (each length of it heard
  after its own distance over the speed of sound, high notes lost with distance, a rumble, echoes off the ground)
  and comes as late as the distance makes it. A craft in a storm, or climbing fast through cold cloud with its
  engines burning, can be struck: a flash, a hot spot on the part struck, and its stability assist dropping out
  for two seconds. **Lightning can strike craft** off forbids it. On a world with no ground under its air (Jool)
  every flash is within the cloud (since 0.2.2; three in ten of a strong storm's had gone down to the height the
  model takes for ground where it knows none).
* **Fog and dust in the air** are veiled by Natural Light (0.10.0) from how far the weather says one can see, as
  thick as that up to the fog's or the dust storm's top and thinning out above, in the dust's own colour (the
  ground's, deepened) for dust; as volumes with shape they are for the clouds to draw: the weather gives them a Fog
  or Dust layer lying on the ground. (Unity's own fog was tried: the game's shaders were built without it, and it
  shows nothing.)
* **Smoke in the wind**: the explosions' air (Volumetric Explosions 0.5) asks the weather for the wind at each
  patch of air when it is made and twice a second after (`Feed.cs`, `Sources.Wind`), and its gusts are made as
  strong as the weather's. What it is given is the mean wind at the patch's own height above the ground itself
  (the boundary layer's profile: next to nothing just over the ground, about four fifths of the 10 m wind 2 m up on
  a sunny afternoon, under two thirds on a still night) and the gusts' size there, so the explosions must take no
  profile of their own off it. (Before 0.2.0 it was the 10 m wind at any height.)

## Launch weather

`Launch.cs`: go or no go for the pad the craft stands on (or the space centre's), by rules adapted from NASA's
for a crewed Falcon 9: the sustained wind 49 m up the pad no more than 13.4 m/s (30 mph); the wind's change with
height between half and one and a half times the jet's height no more than 8 m/s a kilometre; no lightning within
18.5 km (10 nautical miles) in the last 30 minutes; no storm's anvil within 18.5 km; no precipitation at the pad;
no layer of cloud more than 1,400 m thick that reaches freezing; no heaped cloud whose top is above freezing.
The forecast judges the same rules every ten minutes for three hours, and how likely a "no go" is from the
weather a little earlier, later and to either side. All of it is worked out off the game's thread.

## A city's real weather

`City.cs`: a city typed on the page is looked up with Open-Meteo's geocoding service, and its weather now
(cloud at three heights, precipitation, snowfall, the weather code, temperature, wind, gusts) fetched from its
forecast service, every quarter of an hour while the game runs. It is kept in the mod's settings (as plain
figures), so it is there at the next start before anything is fetched. Round Kerbin's space centre (in full
within 500 km, gone by 800) the weather becomes it: the layers' cover, the rain or snow, the wind and its gusts,
fog for a foggy code, storms for a thundery one. Nothing else is sent; an empty box sends nothing. Open-Meteo is
free for uses like this, needs no key, and its data are CC BY 4.0.

## For other mods

Through Keystone 0.7's `Weather` (see Keystone's page for mod authors): `At` (the clouds in three layers with
their kind, cover, base, top and wetness; rain, snow, storms, fog, dust, how far one can see, the wind at 10 m,
its gusts, how the clouds drift, the temperature), `Cloud` (the pattern of the clouds at a point: heads of
cumulus about two kilometres apart, sheets with gaps, wisps of cirrus, storm towers with anvils), `Cells` (the
storms and showers near a place), `Wind` (at a point, gusts and all), `Shade` (how the clouds dim the sun and the
sky), `Flashed` and `Recent` (lightning). `At`, `Cloud` and `Cells` may be asked from any thread (`Cloud` costs
about 15 microseconds, `At` a few microseconds once the place is known, a quarter to half a millisecond for a new
one: most of that is finding the storms about the place, and the wind over it).

For what has to ask at very many places at one moment there is, inside the mod, `Model.Atlas` (since 0.2.2; the
clouds' map of the planet uses it): every storm and shower on the world found once and sorted into squares of
latitude and longitude, after which the clouds and what falls at a place (no wind) cost about what the large fields
alone do, and are the same to the last figure as `At` gives (checked outside the game at 126,000 places: see
[What was tested](What-was-tested.md)). It is not in Keystone's interface: another mod that wants the weather of a
whole world asks `At` place by place, as before.

The AI Bridge (which does not need this mod) finds `NaturalWeather.Bridge` by name: its "weather" call, a line in
"status", and the wind on the craft for its ascent (it keeps near the airflow through the air, not over the
ground) and its braking through air.

## What it costs

Measured in the game on 2026-10-09 (development build, Apple M5 Pro): working out the large fields at a place
10 µs, the clouds at a place 20 µs, the pattern of the clouds at a point 15 µs, the gusts at a point 14 µs, the
storms within 25 km 0.45 ms, everything about a new place 0.5 ms (each place is kept for two seconds). The wind on
the craft being flown: seven points a physics step, about 0.1 ms a frame. Rain's particles run in Unity's own
code. Not yet measured: the frame rate with heavy rain, many lamps, or a storm overhead.

Measured again on the evening of 2026-10-10 (the same machine, a quieter game): the large fields at a place 4.6 µs,
the clouds at a place 11 to 13 µs, the storms within 25 km 0.20 ms, everything about a new place 0.22 ms. The clouds'
map of the planet, 32,768 places: 0.32 s of a thread for Kerbin by the atlas, where asking each place by itself is
seven seconds at that rate (see [Clouds](Clouds.md), "0.2.2").
