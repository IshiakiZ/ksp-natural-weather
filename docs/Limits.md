# Limits

* **Clouds are not this part's to draw.** The weather says where cloud is, of what kind, how high and how wet
  (Keystone's `Weather`); our own volumetric clouds draw it (made alongside, in the same mod). Without them the
  sky stays as Scatterer and EVE paint it: rain, lightning and the dimmed light of an overcast then come out of
  a blue sky.
* **Fog and dust with a shape of their own (a bank of fog, a wall of dust) are for the clouds.** Unity's own fog
  was tried: the game's shaders were built without it and it shows nothing. The weather gives a Fog or a Dust
  layer lying on the ground, how far one can see, and dims the sun by them; Natural Light (0.10.0) veils the
  distance by as much, the same all round the camera; blowing dust is drawn near the ground in Duna's storms.
* **Cloud light and the veil of the distance need Natural Light** (0.10.0 for the light as on real days, the eye
  opening for it and the veil; 0.8.1 for the first cloud light). Without it the sun and the sky are as the game
  has them whatever the cloud, and rain and snow are seen only in the 24 m about the camera. The veil is a
  shader of Natural Light's own, built for Direct3D as well; seen on Windows in rain, fog and snow.
* **Wind moves craft only with the Harmony mod.** Where another mod has taken over the game's drag sums (the
  Kerbal Weather Project, Advanced Atmosphere Tools, both through ModularFlightIntegrator), the change made
  there does nothing and that mod's wind is the one: ours then stands aside altogether (wings too), so that no
  craft feels two winds. With FAR installed the game's own
  aerodynamics are not used, and the weather's wind is handed to FAR's own wind function instead (WindAPI, where
  it is installed, adds it to its other winds): written by FAR's published interface, **not tried** (FAR is not
  installed here).
* **The navball's prograde marker is over the ground**, as the game has it: in a strong wind the air comes from
  elsewhere. The AI Bridge's "weather" call gives the airspeed.
* **The AI Bridge flies by the air** (since this mod's 0.2.0): its aeroplanes hold airspeed, rotate at an airspeed,
  keep their stall margins in airspeed and point the nose along the airflow (crabbing into a crosswind), as its
  ascent and its braking through air already did; the speed over the ground is kept for taxiing, the roll-out,
  the stop and the glide slope (a path over the ground). Not yet flown in a strong wind.
* **Rain is seen nearest the camera**: drops are drawn in a box 24 m about it (beyond that a real drop is too thin
  to see), and beyond them Natural Light greys the distance by the weather where the camera is; the grey curtains
  under a storm some way off are the clouds' to draw. A drop is a faint streak added to the picture in the game's
  own particle shader, as bright as the light in front of the camera: not a refraction of what is behind it.
* **No lying snow, no wet ground, no puddles, no drops on windows**: each wants a shader of its own.
* **Lightning within a cloud** lights the scene and is heard, but its channel is not drawn (the cloud hides it);
  our clouds light themselves by it. A flash to the ground seen from under the cloud's base is drawn after our
  clouds, from the ground to the base only (the clouds are laid on after everything see-through, and drew over
  it wherever there was cloud behind it); from above the base it is drawn before them, and a cloud in front hides
  it. A cloud lower than the base where the flash is, between it and the camera, does not.
* **The space centre's weather is the same for everyone at the same moment**: it is worked out from the clock.
  So the same launch at the same time always meets the same weather (good for trying again), and a forecast is
  exact for this weather, not a guess; the forecast's "chance of no go" comes from the weather a little earlier,
  later and to either side.
* **A city's weather is laid over the space centre's region of Kerbin only** (in full within 500 km, gone by
  800), and it is the city's weather now, whatever the game's clock says. It is fetched while the game runs and
  a city is typed in; without a network the last weather fetched is kept.
* **Made and looked at on a Mac; tried on Windows** (2026-10-10, Direct3D 11, with HarmonyKSP): the clouds, rain,
  fog, snow and a storm held at the launch pad, and the clouds on OpenGL too (see [Clouds](Clouds.md)); then its
  sounds (recorded where the game's sound leaves it and measured, not listened to), lightning at night, a real
  city's weather, the wind carrying a craft in flight, and Duna's dust storm (see
  [What was tested](What-was-tested.md)). Not run on Linux.
* **A held "Windy" day** brings the wind aloft down to the ground and makes the day's wind 2.6 times as strong; on a
  calm day that is still not much (2.8 m/s at 10 m at the pad on Windows, with 5 m/s at a kilometre).
* **Duna's dust devils, Eve's virga and Jool's lightning deep in its bands** are in the weather's numbers but not
  drawn yet.
