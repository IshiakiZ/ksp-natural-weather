# Settings

In the Keystone window (the keystone button on the game's toolbar, or Option-K on a Mac, Alt-K elsewhere),
on Natural Weather's page. Under the settings the page shows the weather where the craft is (or at the space
centre), the launch go / no go with its rules and forecast, and the box for a city.

| Setting | Installed as | |
| --- | --- | --- |
| Natural Weather | on | off: still air and clear skies everywhere, as the game has it |
| **The weather** | | |
| Weather | As it comes | or one kind held everywhere: Clear, Fair, Cloudy, Overcast, Rain, Storm, Fog, Snow, Windy, Dust storm (eased in over about three quarters of a minute; from one kind to another through none) |
| Bad weather | 0.25 | how often the weather turns bad: 0 never, 0.25 mostly fair, 0.5 like a temperate coast, 1 stormy |
| How fast it changes | 1 | how quickly weather systems, storms and fog come and go |
| Wind | 1 | how strong the wind is, everywhere |
| Gusts | 1 | how rough the air is |
| **Worlds** | | |
| Kerbin, Laythe, Duna, Eve, Jool | on | weather on each |
| Other worlds with air | on | worlds a planet pack adds |
| **What it does** | | |
| Wind moves craft | on | off: the weather is only seen, never felt |
| Wind can tip landed craft | on | off: craft standing on the ground or the sea feel no wind |
| Lightning can strike craft | on | off: no strikes |
| **What is drawn** | | |
| Cloud light | on | cloud dims the sun and greys the sky (with Natural Light) |
| Smoke in the wind | on | smoke and explosions (Volumetric Explosions) drift in the weather's wind |
| Rain and snow | on | |
| Lightning | on | the flashes drawn and the scene lit by them |
| Thunder | 0.8 | how loud |
| Rain's sound | 0.6 | how loud |
| Fog | on | |
| Dust | on | dust storms (Duna) |
| **A real city's weather** | | |
| Use the city's weather | on | with a city typed in the box below, Kerbin's weather round the space centre is that city's |

The clouds' own settings (whether they are drawn, how finely, how much, how thick, how bright, their shadows,
hiding EVE's clouds) are on the same page, under "Clouds". "Cloud detail" as installed is "middle"; "lower" draws
them as far and as tall in longer steps and a smaller picture (since 0.2.2: before, it drew no storm towers);
"higher" walks further before giving up and draws the cloud about the camera in a larger picture.

**The city box** sends what is typed in it to Open-Meteo (open-meteo.com), to find the city and fetch its
weather, every quarter of an hour while the game runs; nothing else is sent, and nothing at all while it is
empty. "Forget" empties it. The city, where it is and its last weather are kept in the settings file, so they
are there at the next start.
