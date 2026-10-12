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
the AI Bridge's aeroplanes in wind; rain against a low sun (the glint); the rainbow; splashes; the sounds by
ear (on Windows they were recorded and measured, above, but nobody who can hear has listened to them); the go / no go
page's forecast as drawn (worked out and read through the development build only); a full flight.

## 0.2.2, 2026-10-10 (evening), on the Mac

One turn at the game of some seventeen minutes (development build, the test save's pad; the game was then wanted for
other work), the cloud shader's lab between and after, the weather's model run by itself outside the game, and two more
turns late that night of ten and eleven minutes (at the end of this part). The clouds' part is on their own page
([Clouds](Clouds.md), "0.2.2"); in short, and the rest:

**A new build loads a newly packed cloud shader** (the shaders are loaded once for each build of the mod: a
development build swapped in loads them again; a game on the release build has to be started again).

**The silver lining, the far heaps, the lower setting, the first map, what the clouds cost**: see the clouds' page.
Seen in the game: sunrise looking at the sun with the new shader (lit rims and thin places, where 0.2.1 had dark shapes;
not the same heaps in the two pictures), a storm held on the middle and the lower setting (the same towers on both),
the first map of a flight in 0.32 s, and the frame with clouds 14.03 ms with the old shader and 14.06 with the new, by
turns in one scene. Not seen in the first turn: the far heaps side by side on the same sky, and how long after a flight
begins the clouds are there (both taken up in the later turns, below).

**The weather's model outside the game.** `Model.cs`, `Noise.cs`, `World.cs` and `Overrides.cs` compiled by themselves
against the game's libraries (a scratch program, not part of the mod), six worlds (Kerbin, Laythe, Eve, Duna, Jool
and a small one of 90 km), the weather as it comes and a storm and rain held, four moments each: the atlas of each
world's storms made in 2 to 22 ms, and at 6,000 places a time (1,500 on Jool; one in seven within ten degrees of a
pole, one in eleven within three degrees of the line where longitude wraps), 126,000 in all, the clouds, rain, snow,
storm, fog, dust, seeing and temperature the atlas gives were the same as the place asked by itself gives, to the last
figure, places with two and more storms in reach included (the storms have to be taken in the same order for that).
A place cost 1.3 to 2.4 µs by the atlas and 14 to 16 µs by itself there; in the game, by itself, 220 µs.

**Jool, from a 260 km orbit, day and night side** (the first look): a flat pale grey disc with a fuzzy round edge under
the craft, the rest of the planet a plain dark green (EVE's clouds of it hidden), Scatterer's bright limb beyond; the
map of it took 8.2 s. Mended by not drawing these clouds where there is no ground (see the clouds' page); the mend was
looked at in the later turns (below). With it, no map of Jool is made at all.

**Duna's nearest ground** (`duna_landed`, warped 30,000 s, the sun 39 degrees up, the lander lying on a slope; each of
three views). On a clear day the ground is all but black in every view across it (the lower third of the picture
0.017, 0.003, 0.002 as shown), and a dark blood red looking down on it from 8 m (0.086, 0.016, 0.008), with the
lander's shadow plain on it: the sun is on that ground, and that is how dark Duna's ground there is in the picture as
the game is set up now (the ground's own dark red, next to no light from Duna's thin sky, and the film look's darks).
In the storm held ("Dust storm", 530 m of seeing, 40.1% of a clear day's light on level ground by Natural Light's own
figure, its eye open 1.3 stops for it) the same lower third is 0.177, 0.023, 0.003: brighter than on the clear day, but
an oxblood red with no green or blue in it, under a sky and a distance of bright ochre: the storm's veil, 0.83, 0.49,
0.27 as it shows, takes a hundred metres to cover anything, and the ground within twenty has almost none of it. So the
near ground is not short of light against a clear day: it is short of the storm. A dust storm's dust is thickest in its
lowest metre or two, and the ground about the camera is seen through that. **Not mended in this version.** A mend was
written and looked at in the later turns (below), and left out: Natural Light's veil given the dust blown
along the ground (thinning by e every metre and a half above the ground under the camera, the ground's own slope taken
from a line straight down; by its figures as first set, 27% of veil on ground ten metres from a camera three metres
up, 59% at thirty, 9% straight down, 14 to 20% on a craft fourteen metres off).

**Lightning on film.** The film's first storm shot, measured frame by frame: its frames were 0.031 s apart by the
computer's clock (not a tenth: a dark scene is quick to draw); each flash lit the ground for ten frames or so, brightest
on the three or four a stroke fell near (the picture's mean 0.14 to 0.21 against 0.087 before and after) and dim
between; and the sky's part of the picture did not change at all through either flash (0.014 throughout): no channel
was drawn against the sky on any frame. That shot was recorded before the channel was drawn after the clouds (above:
they had been laid over it wherever there was cloud behind it), which is most of why it showed none; what a flash's
own timing adds is that its strokes fall on single frames, at whatever strength the frame happens to catch. The
development build's `Lightning.Hold` now takes a number of frames as well as seconds ("6 frames": the flash at its
brightest for six frames drawn, a tenth of a second of film, then on as it would have gone), so that it does not depend
on how far apart the frames are by the clock, and the film tool's shot asks for six and lets go afterwards
(`tools/reel.py`: `stage_storm`, `weather_back`). The shot was recorded again twice in the later turns (below).

### Later that night: two more turns at the game

Ten and eleven minutes, the development build, the test save; the navball left minimised throughout; the game given
back each time with the pad's quicksave loaded, nothing held and the settings as the user's.

**How soon the clouds are there, and the frame meanwhile; the atlas asked in the game; Jool after the change; what the
clouds cost among the heaps**: on the clouds' page. In short: clouds 0.8 s after the mods start at the pad and 1.1 s
from orbit (0.2.1: none within twelve seconds); the atlas' answers the same as place by place at 12,000 places in the
game; Jool plain green with no grey disc, EVE's clouds of it left alone, every flash there within cloud; no dearer
among the heaps than 0.2.1's shader.

**The same sky twice: not to be had by loading.** Tried as the first turn's notes proposed (the weather held, the
clock set, then the mods started again) and then with a development switch that stops the wind carrying the clouds'
noise (`Clouds.Still on`) besides: the two loads' skies differ every time. The clouds' readout shows why: with the
game's clock the same to half a second (9,325,010.3 and 9,325,009.9 s), the wind over the pad came out in the clouds'
"planet's own axes" as -0.60, -6.97, 1.50 m/s in the one load and 6.99, 0.41, 1.50 in the other. Those axes are the
scene's, not the planet's: see the clouds' page ("Found and not mended") for what that means for where the weather,
the storms and the flashes' light are drawn. So the far heaps of 0.2.1's shader and this one were photographed on two
different skies (six pairs), and say little: heaps to the horizon in both, the farthest band more distinct in this
one's.

**Lightning on film, recorded again** (the shot `nw_lightning` into a folder of its own; the film itself not made
again). With the six-frame hold alone, the first time: each flash lit the scene for five frames (the picture's mean
0.23 to 0.29 against 0.10) and the channel was there, but as a dark grey crooked hair above the rocket: its brightest
dot against the frame before rose 0.01, 0.07 to 0.13, 0.15 to 0.20 and stayed at 0.21 to 0.27. The channel's white core
was two thirds of a pixel wide, and the game's smoothing of thin lines over frames leaves a line that thin a quarter
grey however long it is held: holding the flash was not what the shot lacked. The core was made a pixel and a half of
white in a strip three pixels wide, the glow round it stronger, and the channel left glowing for about a tenth of a
second after each stroke (it does). Recorded again (570 frames, 0.030 s apart, two flashes): the channel is a crooked
blue-white line from the top of the picture down onto the rocket's nose, with a kink and a short branch, plain against
the black sky from the third frame of the flash and for eight frames more while the ground's light dies away; measured
as before (the sky part of the picture shrunk four times, against the frame before the flash) its brightest dot rose
0.08, 0.14, 0.24, 0.27, 0.30, 0.36 to 0.42 in the first flash and to 0.53 in the second, with 30 to 80 of the 23,040
dots brighter by a sixth of white. Still so: it is at its faintest on the first lit frame
and takes five or six frames to come to full brightness (the smoothing again), where a real channel is brightest at
the first instant; it is a thin line, two pixels at this size of picture; the flash lights the ground flatly and
brightly; and the sky above stays black through the flash, with no cloud lit by it (the light of a flash within the
clouds is placed by the mistaken axes, above).

**Duna's nearest ground, with the dust blown along the ground** (Natural Light's veil, the branch `sky-ground-dust`;
`duna_landed` warped 30,000 s as in the first turn, the sun 39 degrees up; four views: the lander from 14 m and from
30 m with the camera some three metres up, down onto it from 8 m, and from 6 m with the camera 0.3 m above the
ground; the dust's thickness at the ground in the thickest storm set to nothing, 0.05, 0.085 (as written) and 0.12 a
metre by a development call).
*Clear day, nothing against 0.085*: the lower third of the picture 0.006 and 0.006, 0.007 and 0.007, 0.018 and 0.018
in three views, and the whole picture the same to three places in all three: unchanged, as it must be (the dust is
nothing when there is no storm: 0.0000 a metre by its readout). In the fourth view (down onto the lander) 0.029 against
0.024, the ground's grain smeared differently in the two pictures and the lander the same: put down to the camera
not having come to rest in that view (not proved); the dust has no way to darken a clear day's ground, and would have
shown most in the views across the ground, which did not change.
The ground itself is all but black on the clear day, as the first turn found, under a bright ochre sky. *The storm
held*: with no
ground dust (as 0.2.1) the near ground is a black-maroon slab in every view, the lower third 0.055, 0.053, 0.084, 0.050
(red 0.18 to 0.23, green 0.01 to 0.05), ending in a hard line against the bright far dunes where the ground crests.
With 0.085: 0.166, 0.191, 0.168, 0.108 (red 0.28 to 0.37, green 0.07 to 0.15): from a few metres out the ground comes
a dim orange-brown, grading smoothly into the far dunes, no band or edge to it. What is not right: the ground in the
first two or three metres before a low camera stays a dark maroon (the fourth view, where it fills the lower third:
0.108 against 0.050, and 0.130 at 0.12 a metre: no thickness mends it, a couple of metres of any dust glows little, and
the ground itself has next to no light on it); and the lander thirty metres off is a pale ghost at 0.085 and nearly
gone at 0.12, in a storm said to let one see 530 m (at 0.05 it is veiled and plain, and the near ground a dimmer
red-brown: 0.124, 0.140, 0.136, 0.082). So: the near ground better in every view, right from above and from a few
metres out, not right at the camera's feet, and the dust too thick for a craft thirty metres away. **Left on its
branch.**
What the pictures point to is that the ground in a storm wants the storm's light on it (the glowing dust all round
lights real ground from every side), which is the scene's light, not a veil's. Also seen, in every Duna picture with
and without the change, clear day and storm: a brighter frame a hundredth of the picture wide along its left, right
and lower edges.

## The axes mended, and run in the game (2026-10-10, midnight)

The mend of the fault above (the planet's axes taken in the scene's frame, storms and flashes placed by
`GetWorldSurfacePosition`: the branch `sky-true-axes`) was merged and run on the Mac in the development build, three
ways, as its notes asked (pictures in `~/.ksp-ai-bridge/pictures/sky/axes/`):

* **The camera's longitude by the game and by the clouds' axes** (`Clouds.Stats`): -74.56 by both, apart by 0.00, on
  six loads, in which the scene stood turned -140.1, -141.3, -72.0, -2.9 and -184.7 degrees against the planet.
* **The same sky twice.** Two loads of the pad at midday with fair weather held, fair-weather heaps and the noise held
  still (`Clouds.Still on`), photographed straight up, to the east and to the north: the same heaps in the same
  places in all three views (`same_sheet.jpg`); only the smallest tufts differ. Before the mend no two loads had the
  same sky (above).
* **A flash within cloud lights the cloud it is in.** At night under storm cloud, on a load turned 184.7 degrees, a
  flash by the weather's own route (by latitude and longitude, 4 km in front of the camera) lit the cloud the camera
  was looking at, three times (`flash2_sheet.jpg`: faint, and there). Turned that far, the old axes would have lit
  the sky behind the camera. (A first try proved nothing: `Lightning.Flash` draws a channel to the ground and does
  not go by latitude and longitude, and that load happened to stand turned 2.9 degrees.)

Still so: a flash's light on the cloud is faint in a picture taken a third of a second after it; the channel is thin.
