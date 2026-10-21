# Natural Weather

Weather that is seen and felt: wind on craft and smoke, clouds that dim the sun, rain and snow, lightning and
thunder, fog and dust storms, hurricanes.

![Heaps over the pad, clouds from 11 km and from orbit, a hurricane, lightning, rain, snow, fog](src/NaturalWeather/img/natural-weather-tour.jpg)

![Lightning strikes beside the launch pad at night](src/NaturalWeather/img/natural-weather-lightning.gif)

For Kerbal Space Program 1.12.x. **Needs [Keystone](https://github.com/IshiakiZ/ksp-keystone) 0.9 or later**, a separate download
([on SpaceDock](https://spacedock.info/mod/4665/Keystone)): this mod does not load without it.

Three other mods each add one thing, and none is needed: [Natural Light](https://github.com/IshiakiZ/ksp-natural-light) (the light under
cloud; rain, fog and dust veiling the distance), [Volumetric Explosions](https://github.com/IshiakiZ/ksp-volumetric-explosions) (smoke
goes with the wind; dust devils) and HarmonyKSP (the wind moves craft).

## What it does

* **Wind** with gusts, on craft, parachutes and smoke; downbursts and a storm's air felt by craft.
* **Weather on every world with air**, the same at a given place and time, so it can be forecast: Kerbin's fair days,
  showers and storms, Duna's dust storms, Eve's deck of cloud, Jool's winds.
* **Clouds where real ones form** (0.5.0): from orbit, the storms of the middle latitudes as commas with their fronts,
  decks breaking into cells and heaps, clear sea and desert between; the sea under cloud about half the time.
* **Clouds shaped after photographs:** heaps that grow and fade, banks, towers with anvils, altocumulus in soft rows,
  lit through by the sun and fading in the land's own air; their shadows blue-grey in the sky's light. A craft leaves
  its path through cloud.
* **Rain, snow, freezing rain, ice pellets and hail; fog by kind; lightning** that can strike a craft.
* **Hurricanes** with their eye and spiral arms, **squall lines, tornadoes and waterspouts;** on hot, still
  afternoons, **dust devils** on Duna and in Kerbin's deserts (with Volumetric Explosions 0.8.0).
* **Wet ground, drops on the camera** (a setting), **haloes**, and **icing** on craft in cold cloud (a setting).
* **Lying snow** (0.5.0, a setting): after snow, the ground white until it thaws, from a line on the hills; drifting along
  the ground in a wind.
* **Its sounds,** made by the mod: rain, hail, thunder, the wind; and (0.5.0) the air rushing past a craft in flight, and
  rain and hail drumming on the cockpit.
* **Launch weather** (a go / no go and a three-hour forecast), or **a real city's weather** at the space centre.

![Raindrops on the camera](src/NaturalWeather/img/natural-weather-drops.gif)

## Install

Copy the `NaturalWeather` folder from the download into your KSP's `GameData`, beside `Keystone`.

## Settings

In the Keystone window: the keystone button on the game's toolbar, or Alt-K (Option-K on a Mac).

## Good to know

* **The newest kinds are first versions:** a tornado's funnel is smooth, a storm's tower can look like a ball from
  afar, fog has no banks with edges. The [wiki](https://github.com/IshiakiZ/ksp-natural-weather/wiki) says what is wrong with each.
* **Beside other mods:** where these clouds are drawn, EVE's clouds are hidden (its city lights stay) and Scatterer's
  sun flare goes behind them. Where another mod already puts wind into the game's aerodynamics (Kerbal Weather
  Project, Advanced Atmosphere Tools), that one wins.
* A city's weather is fetched from Open-Meteo (its name is sent every quarter of an hour; nothing with the box empty).
  Weather data by Open-Meteo.com (CC BY 4.0).

## Works on

Mac: tried. Windows 11 (Direct3D 11 and OpenGL): 0.5.0 tried (fair, cloudy and stormy skies, the shadows, lying snow, the drops on the glass, towering heaps, a thin layer from inside it; with Scatterer and EVE); 0.5.1's mend (C# only) not yet. Linux: never run.

**More:** [the long description](https://github.com/IshiakiZ/ksp-natural-weather/wiki/The-long-description) (everything in full, and what
was tried on each system) and the rest of the [wiki](https://github.com/IshiakiZ/ksp-natural-weather/wiki).

## Licence

[CC BY 4.0](LICENSE): use it, change it and pass it on as you like, as long as you credit Ishiaki and link back here.
