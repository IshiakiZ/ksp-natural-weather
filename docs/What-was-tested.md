# What was tested

On 2026-10-09, in the game on a Mac (Apple M5 Pro, 1280 by 720), on the development build, at the space centre's
pad (the test save's `fx_pad`: a three-engine rocket late in the afternoon), with Scatterer, EVE, TUFX, Waterfall,
Natural Light, Volumetric Explosions, Rocket Smoke and Engine Flames installed. Four turns of ten to fifteen minutes.
The pictures are in the helper's scratch folder (`scratchpad/weather/`), not in the mod.

**Loads and runs.** The page in Keystone's window shows every setting; the weather is handed to Keystone
("the weather comes from Natural Weather"); nothing in the log but the mod's own lines.

**Wind moves craft.** The game's code was changed in all three places ("the drag sums changed in 1 place(s),
wings in 1, control surfaces in 1"; the first try found none: in the development build the game's types did not
compare equal by reference, and a wing fetches the frame's velocity and then duplicates it, so it is now matched
by name and the frame's velocity itself has the wind taken off). On the pad the root part's airspeed as the drag
sums have it was the wind turned round (5.25 m/s against 4.4 m/s of wind a physics step later, the gusts moving),
the dynamic pressure as for that airspeed. A launch straight up under the stability assist in "Windy" weather (the
wind 15 to 19 m/s from the east-south-east at a kilometre or two): 13 seconds in, the rocket was 27 m east and 22 m
south of the pad; the same launch from a fresh load with the wind not moving craft, 10 m west and 4 m north. The
difference, some 45 m, is upwind: the finned rocket turned its nose into the wind and its thrust carried it that
way, as real rockets weathercock. A parachute drifting downwind was not tried.

**Four days of the space centre's weather** (every half hour, worked out at once): clear 48% of the time, partly
or mostly cloudy 29%, overcast 8%, fog 2%, storms near 6%, launch go 51% (most of the no-gos were for shear aloft:
those thin layers were then made about a fifth as frequent).

**What it costs**, measured in the game: the large fields at a place 10 µs, the clouds at a place 20 µs, the
pattern of the clouds at a point 15 µs, the gusts at a point 14 µs, the storms within 25 km 0.45 ms, everything
about a new place 0.5 ms. With the game running at 25 frames a second that turn (other helpers' builds and mods
in the development build), heavy rain, a clear sky and the mod switched off all measured the same: 25.3, 25.3
and 25.1 frames a second. A real cost of rain and of a storm overhead is still to be measured on the release build.

**Cloud light.** Overcast against clear, each from a fresh load with Natural Light's exposure held still: the
overcast scene darker, flatter and greyer, the sun's direct light gone (shade: sun 0, sky 0.44, grey 1). Without
clouds drawn the sky above stays Scatterer's blue, so it reads as dusk more than as an overcast day: it waits for
our own clouds.

**Rain.** At first not to be seen at all (drops a centimetre wide 30 to 70 m off are under a pixel); then, 3 to 5 cm
wide in a 24 m box about the camera and smeared over a twentieth of a second, dark streaks slanting across the sky
in the wind, as rain looks against a bright sky. A drop passing right by the lens made a broad smear: drops are now
held to 0.4% of the picture's width. Not looked at: splashes, the sound.

**Rain at night** (the pad 3,000 s later, the floodlights on): the drops lit only by the sky's light are not seen at
all, as rain at night is not; the drops in the floodlights' light were at first put under the ground (a floodlight
stands on the ground and shines up), then about each lamp, added to its light: with four floodlights about the pad,
bright slanting streaks across the whole floodlit pad, which reads as rain at night.

**Snow at night**: not seen, lamp-lit or not, even with flakes made larger. Most likely the near camera's smoothing
between frames wipes out a small dot that moves several pixels a frame (the rain's streaks, drawn along their motion,
survive it); flakes and dust are now drawn as short strokes along their motion. Not yet looked at again.

**Snow, again** (a fourth turn, flakes now drawn as short strokes along their motion): by day, dark flecks drifting
across the bright sky, as snow looks against the sky under a heavy cloud (the flakes were made brighter, as a white
thing in that light; over the dark ground beyond the 24 m about the camera there are none to see); at night, light
flecks scattered through the floodlit pad. Lighter than a real heavy snowfall.

**Duna's dust storm** (`duna_landed`, warped on to day, "Dust storm" held): the sky Scatterer's dusty orange, dust
blowing past as dark strokes, the wind 22 m/s, seeing 280 m, the launch rules saying no go for the wind. The ground
and the lander went black at first: the sun gone behind the dust, and Natural Light's sky light on thin-aired Duna
next to nothing. Natural Light now adds the light the cloud or dust itself scatters of the sun it takes (which needs no
thick air): the lander can be seen, but the ground is still far too dark. To be tuned, with the dust drawn as a
volume by our clouds.

**Fog.** Unity's own fog does nothing: held at 150 m seeing, the picture did not change. The game's shaders were
built without it. Fog is for our clouds to draw.

**Lightning.** A flash 2.5 km away: a thin, crooked white channel behind the rocket, the scene lit from it. A strike
on the rocket on the pad: "Struck by lightning!" on the screen, the scene flashed bright. Thunder was not listened to
(the helper cannot hear the game).

**A real city.** London typed in: two requests to Open-Meteo (the place, then its weather), "London, United Kingdom:
mainly clear, 11 °C, wind 4 m/s from the west, gusts 8"; the space centre's weather became it (stratocumulus at 28%
as London's low cloud was, the wind 3.6 m/s from the west-south-west). Forgotten again after; the answer is kept as
test data (`src/NaturalWeather/Tests/london-open-meteo.json`).

## Round two (0.2.0, with Natural Light 0.10.0), 2026-10-10

Two turns on the development build, the same pad, the second with our own clouds drawn (merged in between). Each picture of a comparison was taken in a scene of its own, loaded afresh (`fx_pad` late in the
afternoon, the sun 17 degrees up; `fx_pad` and 3,000 s for night; `duna_landed` and 30,000 s for day on Duna): "before"
with the development build's switches putting back 0.1.0's drops and flakes, no veil, Natural Light 0.9.0's light
under cloud and its eye blind to the clouds; "after" as now. The clouds' map was made again at once after each
weather was held (`Clouds.Try`), and each picture taken 18 s after. Brightness is the mean shown value (0 to 1) of
the picture's lower third (the pad and the ground near it).

**Why 0.1.0's daytime scenes went dark, or stayed sunny.** Natural Light set the sun's colour to black under an
overcast, but as the scene's cameras drew, the sun was found at (1.15, 0.86, 0.46): Scatterer puts a colour of its own
on the sun just before those cameras draw, kept from when the scene began. So the dimming did nothing to the sun
wherever Scatterer is installed: in the first turn's "before" pictures (no clouds drawn yet) the sun shines through the
overcast and throws shadows, with the sky's light dimmed under it; with our clouds drawn they dim the sun themselves,
and the scene under them was lit by Natural Light 0.9.0's dimmed sky alone: night-dark (this round's "before" pictures).
That sky light was itself far too little (0.9.0 had guessed the sun's share of a clear day's light much too small), and
the eye could not open for it with a bright sky in view. Natural Light 0.10.0 dims the sun by its strength as the scene's
cameras draw (Scatterer leaves that alone), and stands aside where our clouds dim it (`WeatherShade.SunDimmed`).

**The light under cloud, against real days** (Natural Light's own figures for white level ground, as light; noon on the
pad, the sun 63 degrees up): a clear day 1.43; under an overcast 0.32, 22% of it; in rain 0.11, 8%. Real days: an
overcast a tenth to a quarter of a clear day's light, a raincloud a twentieth to a tenth. The eye opens 1.3 stops for the
overcast and 2.2 for the rain (six tenths of the 2.2 and 3.7 stops lost): level ground then shows about half a clear
day's light under the overcast and a third in the rain. In the pictures at noon the pad, much of it upright or (in
sunshine) in shadow, came out at 0.40 clear, 0.43 overcast, 0.40 in rain, the sky white under both: as a camera's
meter makes an overcast day, flat rather than dark. (At three quarters of the stops lost, tried first, the overcast
pad came out at 0.47.)

**Rain by day** (held "Rain": steady rain, 1.8 km of seeing, cloud base 300 m above the pad). Before: the ground 0.03
under the cloud deck, night-dark, and the drops dark smears across the sky. After: the ground 0.22, a dull grey day;
the far side of the pad and the hills greyed out, the sky under the low base a uniform grey (the rain between the camera
and the cloud); the drops faint silvery streaks, seen against the tower and the grey, never dark. Looked at closely: a
streak adds about a fifth of the light in front of the camera at its middle.

**Snow by day** (held "Snow": 0.9 km of seeing). Before: the ground 0.03, the flakes dark specks against the sky. After
(second turn): the ground 0.22, everything whitening into the distance; but the flakes still dark specks. Made a fifth
brighter than the veil (third turn): still dark specks against the grey, though their colour was 0.30 against the veil's
0.24. The cause was Natural Light's smoke: it takes every particle drawn over what is behind it by the game's
alpha-blended shader for smoke, and gives it a tint of its own, worked out from the colour the material had when it was
found; the weather's flakes, its dust and 0.1.0's drops were all taken for smoke, which is why every alpha-blended thing
of the weather's read dark, whatever light it was given (the drops, added since 0.2.0, were not). Natural Light 0.10.0
leaves materials named "NaturalWeather..." alone. After that (`snow4-after.jpg`): white flakes drifting across the grey
sky and the pad, soft, lighter than what is behind them. Nothing to do with the clouds.

**Overcast by day.** Before: the ground 0.09 under the grey deck, like dusk. After: 0.20, flat and soft, no shadows;
a light drizzle's streaks here and there (the held overcast has 7 km of seeing).

**Duna's nearest ground**, against a clear day at the same hour (`duna_landed`, warped 30,000 s, each loaded afresh): on
the clear day the ground round the lander is black (0.006) with the sun 39 degrees up and the lander lit: it stands on a
slope turned away from the sun, which Duna's thin clear sky leaves in shade. In the storm the same ground is 0.07: the
light from the whole sky reaches the slope, 40% of a clear day's light on level ground, in the dust's ochre, and the
eye opens 0.8 stops for it. So its dark rust is the ground's own dark red (the planet's map has it at 0.24, 0.10, 0.08)
under that light, not too little light.

**Duna's dust storm** (held "Dust storm", the sun 39 degrees up). Before: the ground black (0.007), the sky Scatterer's
ochre. After: an ochre murk: the far ground and the sky ochre (0.39 and 0.63), the nearest hills gone at about half a
kilometre, the lander a little hazed at 40 m, the near ground a dark rust-brown (0.08); 40% of a clear day's light,
all from the sky, in the dust's colour. First tried with the dust's colour taken as the ground's deepened: a salmon-red
murk far redder than the game's own Duna sky; now paler and warmed. Storms at their height were made thinner (half a
kilometre of seeing, not 280 m).

**Rain at night** (3,000 s later, the floodlights on): as before, bright slanting streaks through the floodlit pad;
if anything clearer, the drops lit by the sky alone being now added (and so invisible in the dark) where before they
were laid over the lamp-lit ones as faint dark streaks. The far lights of the runway dimmed by the rain.

**Fog** (held "Fog", 150 m of seeing): the rocket 30 m off plain, the tower 80 m off faint, everything past it gone;
the sky white. (Before, in round one, fog showed nothing at all.)

**The wind near the ground** (eight days at the pad, every ten minutes, the weather as it comes, the mean wind 2 m up):
before 0.2.0 still (under 0.5 m/s) 3% of the time, light (to 2 m/s) 50%, a breeze (to 5) 40%, moderate (to 10) 5%,
strong 2%, 2.5 m/s on average (3.6 at 10 m): as windy as a real coast, far windier than asked for. Now still 53%, light
45%, a breeze 1.4%, moderate 1%, strong only in a storm's gust front; by night still 72%; 0.6 m/s on average (0.9 at
10 m). Tried on the way: half as calm again at night and twice as much of the wind above by day gave still 39%.

**Cost of the veil**: two passes over the whole picture, reading the depth picture. In rain on the pad, the frame-rate
cap lifted, the veil switched on and off by turns: 31.6 and 27.0 thousandths of a second a frame while the scene was
still settling (each faster than the one before), then 20.1 with it and 20.4 without: within the noise.

**The AI Bridge's aeroplanes by airspeed**: compiled, not flown (no aeroplane was flown in wind in these turns).

## On Windows, 2026-10-10

Windows 11, KSP 1.12.5 on Direct3D 11 (and once on OpenGL), an NVIDIA GeForce RTX 4080 SUPER, the development
build with HarmonyKSP, TUFX and the workspace's other mods; the weather held with `Probe.Choose`. First the clouds,
rain, fog, snow and a storm at the pad (see the README). Then:

**Its sounds**, recorded where the game's sound leaves it (Mission Music's tap of the listener, 48 kHz, its own
music turned down to nothing; measured, not listened to). Clear at the pad: silence. Rain (0.4): a steady hiss at
-44 dB, spread across the range (36% of it between 600 and 2,500 Hz, 25% to 8 kHz, 17% above). A storm, and a flash
put 1.5 km ahead: the thunder came about 4.5 s after it (1.5 km at 343 m/s is 4.4 s), up to -31 dB, 86% of it under
150 Hz, dying away over some 8 s. Duna's dust storm (22 m/s at 10 m): the wind's roar at -43 dB, swelling and
dropping with the gusts between -53 and -40, 74% of it under 150 Hz; let go to clear, it faded out.

**Lightning seen.** At night with a storm held, flashes put half a kilometre to three kilometres ahead: only the
part of the channel in front of the ground showed; against the sky it was not there, though the scene was lit
(the picture's mean brightness from 23 to 80). With the clouds held off the whole channel showed, from the ground
up, with its branches. The clouds are laid on after everything see-through (`AfterForwardAlpha`), and covered the
channel wherever there was cloud behind it. Now the clouds call `Clouds.InFront` as they finish each camera's
drawing, and a channel seen from below the base of its cloud (the weather's lowest layer there) is drawn then,
from the ground to that base, fading over its last eighth; from above the base it is drawn as before. After: the
channel from the ground to the base at 0.5, 1.5 and 3 km, and on OpenGL (the far and near cameras, as on a Mac)
the same.

**And on the Mac** (the same day, OpenGL 4.1, two cameras, with Scatterer, EVE and TUFX; development build): a
night storm held at the pad, a flash put 1.5 km ahead and one 300 m ahead. The far one's channel was drawn after
the clouds by the far camera, a crooked white line with its glow from the horizon up to the cloud's base at
933 m; the near one's the ordinary way, striking beside the pad. To see either, the flash had to be held at its
brightest (`NaturalWeather.Lightning.Hold`, development build): a stroke is at its brightest for a fiftieth of a
second, and neither a picture asked for through the bridge nor a film recorded frame by frame (whose frames are
a tenth of a second apart by the clock the flash goes by) ever caught one. In play it is four flickers in a third
of a second, as lightning is. (Which is also why the Mac's first film of a storm showed the ground lit and no bolt.)

**A real city.** "Reykjavik" typed into the page's box and Return: "Reykjavik, Iceland: overcast, 5 °C, wind 2 m/s from
the south (fetched just now)"; the weather at the space centre became overcast, 5.3 °C, wind 2.2 m/s from 164
degrees, stratocumulus over 53% of the sky at 3 to 4.2 km and a full cirrus layer, and the city, its place and
its weather were kept in the settings. Forget: the space centre's own weather back (partly cloudy, 36 °C) and the
city gone from the file.

**Wind on a craft in flight.** The Jumping Flea's capsule set 1,500 m over the pad, its parachute opened, a windy day
held: under the canopy it drifted as fast as the wind on it, -3.4 to -4.1 m/s east and 2.4 to 3.1 north against a
wind of -3.3 to -4.1 and 2.5 to 3.1 (within 0.2 m/s), 137 m west and 97 m north in 45 s. With the push held off
(`Probe.Push off`), straight down: 0.3 m in 45 s.

**Duna's dust storm** ("Dust storm" held, the sun 35 degrees up): the sky darkened, the ground and the distance
ochre, streaks of dust blowing past; 530 m of seeing, wind 22 m/s at 10 m, the launch rules no go on the wind.

**Not tried at all:** Linux; FAR; the Kerbal Weather Project or Advanced Atmosphere Tools alongside;
the AI Bridge's aeroplanes in wind; rain against a low sun (the glint); Jool; the rainbow; splashes; the sounds by
ear (on Windows they were recorded and measured, above, but nobody who can hear has listened to them); the go / no go
page's forecast as drawn (worked out and read through the development build only); a full flight.
