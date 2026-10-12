# Natural Weather

Weather that is seen and felt. The wind that rocks a plane is the wind that bends the smoke beside it and
slants the rain past it; clouds dim the sun; storms flash and rumble; and the launch pad has a go / no go
with a forecast, as real launches do.

For Kerbal Space Program 1.12.x. Needs [Keystone](https://github.com/IshiakiZ/ksp-keystone) 0.7 (in the download). Better with
[Natural Light](https://github.com/IshiakiZ/ksp-natural-light) 0.10 (cloud light, and the rain, snow, fog and dust veiling the
distance), [Volumetric Explosions](https://github.com/IshiakiZ/ksp-volumetric-explosions) (smoke in the wind) and the Harmony mod
(wind on craft); without each, that one part does nothing.

## What it does

* **Wind on craft**, through the game's own aerodynamics: drag, wings, control surfaces and parachutes all
  feel it, with gusts as the flight simulators model them (stronger near the ground, rougher by day, under
  cumulus and near storms) and the wind turning and strengthening with height to a jet stream.
* **One weather for every world with air**, the same for everyone at a given place and moment: worked out
  from the place and the clock, so a quickload brings back the same sky and the weather can be forecast.
  Kerbin's is mostly fair (as installed) with weather systems, afternoon showers and storms, dawn fog and
  snow where it is cold; Duna has dust storms; Eve a deck of cloud that never goes away; Jool bands of wind.
* **Cloud light**: under cloud the sun is dimmed and the sky's light greys and spreads: about a fifth of a
  clear day's light under an overcast and a twelfth in rain, as on real days, and the camera's eye opens for
  it (with Natural Light).
* **Rain and snow** round the camera, streaked by the wind and by the craft's speed: faint silvery streaks by day,
  seen against anything darker than the sky, white flakes, and both bright in the light of lamps by night;
  beyond them the distance greys out in rain and whitens in snow (with Natural Light).
* **Lightning and thunder**: storms flash; a flash to the ground is drawn and lights the scene; its thunder
  is made from the channel's own shape and arrives as late as the distance makes it. A craft in a storm, or
  climbing through cold cloud near one, can be struck.
* **Fog, haze and dust**: how far one can see falls in fog, heavy rain and dust storms, and the distance is
  veiled by as much (with Natural Light): a dust storm on Duna is an ochre murk.
* **Launch weather**: go / no go for the pad, by rules adapted from NASA's (wind at the pad, shear aloft,
  lightning within 18.5 km in the last half hour, anvils, precipitation, thick or towering cold cloud), and
  a three-hour forecast.
* **A real city's weather**, if you like: type a city and Kerbin's weather round the space centre becomes
  that city's, from online, kept across sessions.
* **For other mods**, through Keystone's `Weather`: our own clouds draw from it, Natural Light lights by it,
  the AI Bridge flies by it.

## Install

Copy the `Keystone` and `NaturalWeather` folders from the download into your KSP `GameData`.

## Settings

In the Keystone window (the keystone button on the game's toolbar, or Option-K on a Mac, Alt-K elsewhere):
the weather (as it comes, or one kind held), how often it turns bad, how fast it changes, the wind and the
gusts, which worlds have weather, whether wind moves craft (off: look only), whether it can tip landed
craft, whether lightning can strike, and each thing drawn. The page also shows the weather now and the
launch go / no go with its forecast.

**A city's weather goes online.** A city typed on the page is sent to Open-Meteo (open-meteo.com) to find it
and to fetch its weather, every quarter of an hour while the game runs; nothing else is sent. An empty box
sends nothing. Weather data by Open-Meteo.com (CC BY 4.0).

## Good to know

* **Its clouds are in the middle of being remade** (2026-10-11), and it shows: a fair day's heaps are too
  alike and too evenly strewn, with edges that look made; seen from space, from very far off, or while flying
  towards them, they have faults in how they are drawn and how they come into being; and from among or above the
  clouds, far cloud can still be drawn over near smoke. [What is wrong and what is next](docs/Clouds.md).
  Rain that looks real, wet ground and drops on the camera are still to come.
* Made on a Mac, and tried on Windows (see below). Its clouds are shaders of its own, packed for the game's
  OpenGL and built for Direct3D (`PluginData/clouds-windows.bundle`); the veil of the distance is Natural
  Light's, built for both as well.
* Its sounds (the rain's hiss, the wind, thunder) are made by the mod and have not yet been listened to by
  anyone.
* Its clouds are drawn over worlds with ground under their air. Jool has none, and keeps the clouds the game
  (and EVE) draw it with: its weather is wind, rough air and lightning within the cloud.
* Wind moves craft only with the Harmony mod (HarmonyKSP) installed. Where another mod already puts wind
  into the game's aerodynamics (the Kerbal Weather Project, Advanced Atmosphere Tools), that one wins.

## Where it works

| System | |
| --- | --- |
| Mac | Made and tried here (KSP 1.12.5, OpenGL). |
| Windows | Tried on Windows 11 (KSP 1.12.5, Direct3D 11, with HarmonyKSP), in a development build that held one kind of weather at a time at the launch pad: cloudy, rain, fog, snow and a storm, the clouds drawn by their shaders built for Direct3D, rain's streaks, and the distance veiled by Natural Light as far as the weather said one could see (1.8 km in rain, 150 m in fog, 900 m in snow). With the game started on OpenGL (`-force-glcore`) the clouds drew nothing until the OpenGL bundles were packed again on 2026-10-10 (see [Clouds](docs/Clouds.md)); since then they draw. Later that day: its sounds, recorded where the game's sound leaves it (rain's hiss, the thunder of a flash 1.5 km off arriving 4.5 s after it, the wind's roar in a dust storm; silence in clear weather), lightning at night (its channel was hidden by the clouds wherever there was cloud behind it; now drawn after them, from the ground to the cloud base, on Direct3D and OpenGL), a real city's weather (Reykjavik typed on the page: the space centre's weather became its overcast, 5 °C and southerly wind), the wind carrying a capsule under its parachute (as fast as the wind, within 0.2 m/s; straight down with the push off), and Duna's dust storm. Then 0.3.0's remade clouds (2026-10-11, their shaders built for Direct3D again that day, with HarmonyKSP and TUFX): fair weather, a mostly cloudy sky and a storm held in turn, each photographed from the ground at the pad, from 2,500 m among the clouds and from 6 km above them: small white heaps with flat bases on a blue sky; larger heaps under grey sheets, with storms standing as flat-topped towers; an overcast with dark towers; picture for picture as on OpenGL on the same computer. With TUFX taken out (the game drawing straight to the screen) the clouds were seen again over the pad and the sea, behind a launch's smoke and a crash's. Rain's streaks against the sky, and a flash of lightning seen by chance over the sea. Two faults of Direct3D, where one camera draws the whole scene, were found and mended that day: the clouds were laid over smoke, flames and spray however near (a rocket's smoke column stood behind the clouds of the horizon), and over Natural Ocean's nearest waves and, with the camera under water, the underside of its surface (see [Clouds](docs/Clouds.md)). Still so there: with the camera among the clouds or above them, and wherever Scatterer is installed (not tried), far clouds are drawn over near smoke; and from below, a trail that has climbed above the clouds shows in front of them. |
| Linux | **Never run.** With NVIDIA's driver the clouds would have drawn nothing before 2026-10-10, as on Windows on OpenGL. |

More: [how it works](docs/How-it-works.md), [research](docs/Research.md), [design](docs/Design.md).

## Licence

[CC BY 4.0](LICENSE): use it, change it and pass it on as you like, as long as you credit Ishiaki and link back here.
