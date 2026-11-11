#ifdef VERTEX
#version 150
uniform vec4 _CloudSheet;    // xy: 1 (the sheet as large as what is drawn into), z: its depth, w: 1
in vec3 in_POSITION0;
out vec2 vs_TEXCOORD0;
void main()
{
    // One triangle that covers the whole of what is drawn into, given in its own coordinates (-1 to 1 each way).
    gl_Position = vec4(in_POSITION0.xy * _CloudSheet.xy, _CloudSheet.z, _CloudSheet.w);
    vs_TEXCOORD0 = in_POSITION0.xy * 0.5 + 0.5;
}
#endif
#ifdef FRAGMENT
#version 150
#extension GL_ARB_explicit_attrib_location : require
#extension GL_ARB_shader_bit_encoding : enable
// Clouds (Natural Weather's Clouds.cs sets it going). Real clouds are water drops by the billion: light goes into them
// and comes out again after bouncing many times, so a cloud is white where the sun is on it, grey where it is deep, and
// glows at its thin edges with the sun behind it. They are drawn here as a volume in a shell round the planet, and every
// pixel's line of sight is walked through it, as the explosions' smoke is walked, but over tens of kilometres:
//
//   0, 1. The noise the clouds are carved from, made once into two cubes (on the graphics card, slice by slice).
//   2.    The walk. Where there is cloud is the weather's (a map over the planet: how much, what kind, how high) carved by
//         the noise; the light at each step is the sun's, as much as reaches through the air and the cloud above it
//         (walked again, towards the sun), with the light scattered many times over, and the sky's and the ground's.
//         Drawn small, with each frame's steps begun at a different place.
//   3.    Those small pictures taken together over frames: cloudskeep.glsl since 2026-10-26 (a program of its own; Clouds.cs).
//   4.    The clouds' shadow: from points on the ground round the camera, how much cloud lies towards the sun.
// (What is drawn is put on the picture by cloudsover.glsl; the shadow is laid on the scene by cloudshade.glsl.)
//
// Places are kept relative to the planet's centre, in the scene's axes; the noise is looked up in the planet's own axes
// (so it turns with the planet and moves only with the wind), from where the camera is in it, which is worked out on the
// processor in double precision and kept small.
uniform vec4 _ZBufferParams;
uniform vec4 _CloudStage;    // x: which go (0, 1: the noise; 2: the walk; 3: over frames; 4: the shadow; 5: the map of the lattice's heaps); y: the slice (noise) or the frame's place in the step (walk); z: where the walk may begin (m from the camera); w: where it must end (m), 0: no end but the shell's
uniform vec4 _CloudCam;      // xyz: the camera, from the planet's centre (m, the scene's axes); w: the planet's radius (m)
uniform vec4 _CloudHigh;     // x: the camera's height above the radius (m, worked out in double precision); y: the top of the air (m above the radius); z: the air's scale height (m); w: the picture's width in pixels (of what is drawn into)
uniform vec4 _CloudRight;    // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
uniform vec4 _CloudUp;       // xyz: the camera's up, times the tangent of half its height of view
uniform vec4 _CloudAhead;    // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
uniform vec4 _CloudFixedX;   // the planet's own axes, in the scene's: x towards latitude 0 longitude 0, y towards longitude 90, z the north pole
uniform vec4 _CloudFixedY;
uniform vec4 _CloudFixedZ;
uniform vec4 _CloudNoise;    // xyz: where the camera is in the noise (in lengths of the large noise; wrapped round only every 207 of them), w: metres to a length of the large noise
uniform vec4 _CloudDrift;    // xyz: which way the wind blows the clouds, in the planet's axes (a unit; in the second form the low wind eased, as long as its speed over 2 m/s and at most 1: Clouds.cs); w: how far the small noise has turned over, in its own lengths (it boils slowly)
uniform vec4 _CloudShell;    // x: the lowest a cloud's base can be (m above the radius); y: the highest a top can be; z: the high cloud's height; w: the high cloud's amount, as set
uniform vec4 _CloudShellEased; // x, y: the shell's floor and top as they are let follow _CloudShell's over seconds (Clouds.cs): what the steps' lengths and the distance inside the shell go by, so that a floor or top that jumps does not move them
uniform vec4 _CloudLook;     // x: how thick cloud is (light lost per metre in the thickest); y: how much the small noise eats the edges; z: more or less cloud than the weather says (1 as it says); w: how many steps at most
uniform vec4 _CloudSun;      // xyz: towards the sun (the scene's axes); w: the sun's brightness above the air (as light)
uniform vec4 _CloudSunTint;  // rgb: the sun's colour above the air; w: 1 if the moon or another world lights the night (its strength)
uniform vec4 _CloudMoon;     // xyz: towards the brightest other world in the sky (the Mun over Kerbin; the scene's axes); w: its light on a veil of ice by night (nothing by day)
uniform vec4 _CloudSky;      // rgb: the sky's light from above, where the sun is high (as light); w: how much of the sky's own colour the air's glow between the camera and a cloud keeps (1, the first form's: all of it; 0: a grey as bright)
uniform vec4 _CloudGround;   // rgb: the light the ground throws up (as light); w: the high cloud's thickness (m)
uniform vec4 _CloudAir;      // rgb: how much a metre of air at the bottom scatters (each colour); w: the same for haze
uniform vec4 _CloudAirHigh;  // x: the height over which the air's own scattering falls by e (m); y: the haze's (lower: it lies in the lowest kilometre or two); z: 1 if the world has a sea; w: how much of the sky's own colour the second form's sky light on its clouds keeps (the rest a white as strong as it; 0.7, his tint)
uniform vec4 _CloudHaze;     // x: 1 if the noise has smaller copies to read far off (0: only the full one); y: the shortest step (m); z: how much more light the air adds than one scattering gives (light scattered again and again); w: how strongly distance fades the clouds (1 as worked out)
uniform vec4 _CloudFlashA;   // xyz: a flash of lightning, from the camera (m, the scene's axes); w: its brightness (0: none)
uniform vec4 _CloudFlashB;
uniform vec4 _CloudFlashTint; // rgb: the flash's colour; w: how far its light spreads in cloud (m)
uniform vec4 _CloudHoleA;    // xyz: a heap made as smoke is made (Heaps.cs), the middle of its base from the camera (m, the scene's axes); w: how far round it the noise's own heaps are cleared (m; 0: none)
uniform vec4 _CloudHoleB;
uniform vec4 _CloudHoleC;
uniform vec4 _CloudShadowAt; // xyz: the middle of the shadow's square, from the planet's centre (the planet's axes, m); w: its side (m)
uniform vec4 _CloudShadowX;  // xyz: the square's two sides, in the planet's axes; w: the height of the ground it lies on (m above the radius)
uniform vec4 _CloudShadowY;
uniform vec4 _CloudSunFixed; // xyz: towards the sun, in the planet's axes
uniform vec4 _CloudStreets;  // xyz: across the wind, level, in the planet's axes (a unit; changed only when the wind has turned well round); w: how strongly heaps line up in streets along the wind (0 to 1)
uniform vec4 _CloudAlong;    // xyz: along the wind, the same way (for the high cloud's fibres)
uniform vec4 _CloudFieldA;   // the heaps made beforehand (see _CloudField). x: repeats of the field per metre; y: metres over which its number runs from none to all; zw: how far the wind has carried it east and north, in repeats
uniform vec4 _CloudFieldB;   // x: 1 if there is such a field (0: the heaps are carved from the noise, as before 0.3.0); yz: how many metres east and north the top of the layer leans with the wind from its base; w: how many metres the small lumps stand proud of the creases between them
uniform vec4 _CloudHabit;    // how the heaps come today. x: how much the small heaps that stand alone are left out (0: all of them there, a sky of many small puffs, which is a rare day; 1: the usual sky, fewer and larger); y: how many times larger over the ground the field is read than the layer's depth alone would have it (1; 1.6 on the usual day: broader, flatter heaps); z: how far a heap that reaches the layer's top spreads out under it, m (the lid: see layerAt); w: how far the heaps' outlines are pushed about, m (no heap round)
uniform vec4 _CloudAnvil;    // the second form's anvil his way (cellTwo; Clouds.cs, AnvilKnobs): x, how far in from its rim its cover is whole (a share of its last disc's half-width); y, how much of its cover is left where it has been carried furthest; z, its density, times his (its fraction under 100), and in its hundreds how many times denser it is by the tower than along its trail, in tenths (Clouds.cs, AnvilCore); w, where its edge's band begins (a share of the storm's column)
uniform vec4 _CloudSpec;     // the second form after the owner's spec of Blackrack's clouds (2026-11-04, research/clouds-r5/spec-gaps-2026-11-04.md): xyz, how far the billows have risen through the clouds since (in lengths of the noise; Clouds.cs, Rise); w, for setting one against the other: 1 reads the billows as coarsely as a pixel, 2 a heap's water thickening over its lowest 1.5 km (as until then), 4 no towers among the heaps (as until 2026-11-05, see towerOf), 8 the sky's light not dimmed by the clouds over a place (as until 2026-11-05, the light field's sky part), 16 a storm's anvil the lens drawn round a hull (as until 2026-11-05, cellTwo), 32 the anvil as dense by its tower as along its trail (as until 2026-11-05, cellTwo), 64 a heap's cover as a sheet's, the power 1.6 (as until 2026-11-06, coverCurve); 128 the small heaps' pattern 330 m to 8 km, faded out over a heap's lowest 900 m (as until 2026-11-06, smallOf; Clouds.cs gives the old pattern with it); 256 a low deck's top as until 2026-11-07 (formDeck); 512 a deck's light as until 2026-11-07 (deckLight); 1024 a deck's billows as coarse as a heap's, as until 2026-11-07 (deckFine); added
uniform vec4 _CloudLight2;   // the second form's light (the rebuild on Blackrack's recipe, 2026-10-27): x, how bright the sun's light is in it (his lobes' own weights times this, fitted on his frame at the pad); y, its sky's light; z, the ground's (1 as made); w, how many times finer its billows are read than made (1; 0 taken as 1)
uniform vec4 _CloudDeck2;    // the second form's low decks (Clouds.cs, DeckCrease2; 2026-11-09): x, the power to which one less the depth inside a sheet's top cover is taken for the light scattered many times there (2 until then; 0 taken as 2); y, the least share of that light kept however deep (0 until then); z, how much more the billows eat into a sheet's upper part than below (E times one and this; 1 until then; 0 taken as 1); w: none
uniform vec4 _CloudForm;     // x: 1 if the low and middle clouds are shaped as mounds with lumps on them, from the noise (the rework of 2026-10-12; 0: as before it); y: how far the pattern of clouds has moved through itself, in lengths of the large noise (the sky changing with time); z: a trial's number (development build); w: 1 to draw without the far field (development build: before and after)
uniform vec4 _CloudWake0;    // where a craft has gone through cloud, the newest place first (up to eight; a line through them is its path). xyz: the place, from the camera (m, the planet's axes; carried on with the wind since, as the cloud is); w: how long ago (s)
uniform vec4 _CloudWake1;
uniform vec4 _CloudWake2;
uniform vec4 _CloudWake3;
uniform vec4 _CloudWake4;
uniform vec4 _CloudWake5;
uniform vec4 _CloudWake6;
uniform vec4 _CloudWake7;
uniform vec4 _CloudWakeLook; // x: how many such places (under two: no wake); y: how wide the wake is where it is new (m: some craft's widths, more behind a burning engine); z: how much of the cloud in its path is burnt off or blown out (0 to 1); w: how far from _CloudWakeMid any of it can reach (m)
uniform vec4 _CloudWakeMid;  // xyz: the middle of the path, from the camera (m, the planet's axes)
uniform vec4 _CloudFieldLevA; // the level of the field above which is cloud, for so much of the sky covered: 1/32, 3/32, 5/32 and 7/32 of it;
uniform vec4 _CloudFieldLevB; // 9/32 to 15/32;
uniform vec4 _CloudFieldLevC; // 17/32 to 23/32;
uniform vec4 _CloudFieldLevD; // 25/32 to 31/32
uniform sampler3D _CloudField;   // heaps made beforehand, lumps upon lumps (tools/clouds/heapfield.swift): a square of sky that repeats east and north, base to top. r: how far inside a heap's surface a place is (above a half) or outside it, less the heap's own number; g: the most r reaches within two cells of there (at every size of the field: for walking through clear air in long strides without stepping over a heap)
uniform sampler2D _CloudHeaps;   // the low layer's heaps of the lattice, drawn once a frame into a map over the ground round the camera (go 5): for each place over the ground, the heap that has it or stands nearest: r, how far the place is from that heap's upright (m); g, the heap's half-width (m; nothing: none near); b, its height (m); a, how far through its life (-1 to 1)
uniform sampler2D _CloudHeapsB;  // and: r, how far above the layer's base that heap's own base stands (m)
uniform vec4 _CloudHeapsAt;      // x: 1 if there is such a map (0: the heaps are worked out at every asking); y: how many lengths of the noise the map is across (it comes round, and is read by where a place is in the noise); z: how many dots across; w: which face of the cube it lies on (0, 1, 2: the one the camera is nearest)
uniform sampler2D _CloudCells;   // a table of numbers got by chance, 256 by 256, four to a place (the same in the game and the lab: Clouds.cs, cloudlab.swift): for each point of the heaps' lattice, when its heaps come, how they rank in size, and how far out of place they stand
uniform sampler3D _CloudShape;   // the large noise: r, the rounded heaps (Perlin noise swelled by cells); g, b, a: cells at three sizes
uniform sampler3D _CloudDetail;  // the small noise: cells at three sizes
uniform vec4 _CloudTwistA;    // a tornado or a waterspout near the camera: xyz, its foot on the ground, from the planet's centre (the scene's axes, m); w, the radius of its strongest wind (m; nothing: none)
uniform vec4 _CloudTwistB;    // x: how high the cloud's base stands over its foot (m); y: how far down from the cloud its funnel is cloud, 0 to 1; z: 1 over water (spray at its foot), 0 over land (dust); w: how far it has turned (radians, either way)
uniform vec4 _CloudEyeA;      // a cyclone within sight: xyz, its middle on the ground, from the planet's centre (the scene's axes, m); w, how far out its strongest wind is (m; nothing: no cyclone)
uniform vec4 _CloudEyeB;      // x: how strong it is now (0 to 1); y: which way it turns, seen from above (1: against the clock, as north of the equator; -1: with it); z: where round it its bands lie (radians); w: its top (m above the radius)
uniform vec4 _CloudEyeC;      // xyz: east at its middle (the scene's axes, a unit); w: its base (m above the radius)
uniform vec4 _CloudShelfA;    // a squall line within sight: xyz, the middle of its row of storms on the ground, from the planet's centre (the scene's axes, m); w, half the row's length (m; nothing: no line)
uniform vec4 _CloudShelfB;    // xyz: the way it moves, level at its middle (the scene's axes, a unit); w: how strong (0 to 1)
uniform vec4 _CloudShelfC;    // x: its storms' base (m above the radius); y: how far ahead of the row its wind's front runs (m)
uniform vec4 _CloudEyeD;      // x: how far its wall's cloud has turned round the eye (radians); y: 1 to draw the cyclone's own cloud alone, without the layers' (for looking at it in the lab)
uniform vec4 _CloudCellA0;    // storms and showers near the camera (up to six): xyz, the middle on the ground, from the planet's centre (the scene's axes, m); w, how far it reaches (m)
uniform vec4 _CloudCellA1;
uniform vec4 _CloudCellA2;
uniform vec4 _CloudCellA3;
uniform vec4 _CloudCellA4;
uniform vec4 _CloudCellA5;
uniform vec4 _CloudCellB0;    // and for each: x, its base; y, its top (m above the radius); z, how strong (0 to 1); w, how wet
uniform vec4 _CloudCellB1;
uniform vec4 _CloudCellB2;
uniform vec4 _CloudCellB3;
uniform vec4 _CloudCellB4;
uniform vec4 _CloudCellB5;
uniform vec4 _CloudCellC0;    // and for each, as the second form draws them (cellTwo): x, which way the wind aloft blows (radians from east towards north); y, how far through its life (0 to 1); z, how high its overshooting dome stands over its top (m, whole) and, in its fraction, its own turn for its turrets; w, how far down the wind aloft its anvil reaches (m)
uniform vec4 _CloudCellC1;
uniform vec4 _CloudCellC2;
uniform vec4 _CloudCellC3;
uniform vec4 _CloudCellC4;
uniform vec4 _CloudCellC5;
uniform sampler2D _CloudMap;     // the weather over the planet (longitude across, latitude up), its low clouds: r, how much cloud; g, what kind (0 flat sheets to 1 storm towers); b, the base and a, the top (in 8 km and 16 km)
uniform sampler2D _CloudMapB;    // r: how wet (rain darkens the base); g: high cloud; b: storm; a: how much middle cloud
uniform sampler2D _CloudMapC;    // the middle clouds: r, what kind; g, the base (in 12 km); b, the top (in 16 km); a, how wet
uniform sampler2D _CloudAirLut;  // how much sunlight comes through the air to a height (up) with the sun at an angle (across: its sine, -1 to 1)
uniform sampler2D _CameraDepthTexture;
uniform sampler2D _CloudLight;  // the sun-side light field (go 9; 2026-10-18), in place of the shadow's second map: for each cell round the camera, how much of the sun's light comes through the cloud between it and the sun past the walk's first step that way (28 m), kept as exp(-0.1 e-folds): r out to 154 m on, g to 346, b to 711 (where the walk's own third, fourth and fifth steps end), a beyond, out of the shell (research/rendering/experiments/cloud-light-field.md)
uniform sampler3D _CloudBillow; // the second form's billows (go 7, billowMake; 2026-10-27): rounded cells at eight sizes, 0 to 1, a byte a place, 256 places each way over half a length of the noise (4.5 km), made once at load (Clouds.cs, MakeBillow)
uniform sampler2D _CloudPattern; // the second form's pattern of where its clouds stand (tools/clouds/pattern.py, PluginData/cover.pattern): a square of 1024 that repeats every 7/207 of the noise's wrap (266 km), bell-spread numbers 0.5 + g / 8 (g a standard normal): r, masses of cloud; g, read four times finer, a field's small heaps; b and a, in the file the same two again, apart, read by nothing yet, and as Clouds.cs loads it (MostOfPattern) the most of r and of g within a dot of each dot of each copy but the finest (whose are its own r and g), for the walk's strides (see coverOf, patternMost)
uniform sampler2D _CloudBlue;   // spatiotemporal blue noise (tools/clouds/stbn.py, after Wolfe et al. 2022): 64 squares of 64 by 64 places, eight across and eight up, a byte a place; in each square places near one another far apart in value, and each place's values over the squares spread evenly (Clouds.cs, LoadBlue)
uniform vec4 _CloudLightF;    // x: cells across each level; y: levels (each four times as wide as the last); z: heights (a multiple of four); w: readings beyond 739 m (8 if nought)
uniform vec4 _CloudLightG;    // x: the finest level's cell (m); y: the lowest height (m above the radius); z: the heights' spacing (m); w: 1 where the walk may read it (nought: not made yet, or off)
uniform vec4 _CloudLightA;    // xyz: the field's middle, on the ground: a way from the planet's centre (the scene's axes)
uniform vec4 _CloudLightE;    // xyz: the field's east there (a unit, the scene's axes)
uniform vec4 _CloudLightN;    // xyz: its north
uniform vec4 _CloudJitter;   // xy: how far this frame's lines of sight are moved off the middles of the walk's pixels (in widths and heights of the picture); z: 1 if what is kept from frame to frame has more dots than the walk's picture (each frame's lines then go through a different one of the kept picture's dots in turn)
uniform vec4 _CloudAim;      // (the far camera's walk) xy: a dot of the walk, in widths and heights of the picture; z: the frame (0 to 1023); w: how many kept pixels a dot stands for each way (4, or 2 on "higher"). Nought for the near camera's, whose lines all go _CloudJitter's way
// The first step of the walk at or past t0, on one grid laid along the line from the camera: steps of `least` near the
// camera, then each a share `each` of its distance (from where that share comes to `least`), then of `longest` (from where it
// comes to that): the walk's own steps, t + clamp(t * each, least, longest), begun not where the line enters the shell but at
// the camera, the grid moved on by `j` (a share of a step) each frame. A floor or top of the shell that moves then only adds
// or takes away steps at the ends; the steps between stay where they were.
// The sun-side light field's cells (go 9): level `level` (each four times the last's cell), cell (i, j) of its n x n, height k:
// its middle, from the planet's centre (the scene's axes). Laid on the plane that touches the planet at the field's middle
// and put back on the sphere (so that near the middle the cells are nearly square, as far as the field reaches).
vec3 lightCellAt(int level, int i, int j, int k)
{
    float n = _CloudLightF.x, cell = _CloudLightG.x * pow(4.0, float(level));
    vec2 xy = (vec2(float(i), float(j)) + 0.5 - 0.5 * n) * cell;
    float h = _CloudLightG.y + (float(k) + 0.5) * _CloudLightG.z;
    return normalize(_CloudLightA.xyz * _CloudCam.w + _CloudLightE.xyz * xy.x + _CloudLightN.xyz * xy.y) * (_CloudCam.w + h);
}

// The field at a place (from the planet's centre, the scene's axes): the finest level that holds it with a cell to spare, read
// between the four cells round it in its height by the card and by hand between two heights. `ok`: false where no level holds it;
// `inner`: how far inside the last level's edge; `cell`: how wide the cells read are (m); `skyLit`: how much of the sky's light
// comes down to there past all the clouds over it (the field's second half, below; 0.3 to 1).
vec4 lightFieldAt(vec3 p, out bool ok, out float inner, out float cell, out float skyLit)
{
    vec3 d = normalize(p);
    float cd = dot(d, _CloudLightA.xyz), n = _CloudLightF.x, H = _CloudLightF.z, rows = H / 4.0;
    vec2 xy = vec2(dot(d, _CloudLightE.xyz), dot(d, _CloudLightN.xyz)) * (_CloudCam.w / max(cd, 0.5));
    float kf = (length(p) - _CloudCam.w - _CloudLightG.y) / _CloudLightG.z - 0.5;
    float level = 0.0;
    vec2 g = xy / _CloudLightG.x + 0.5 * n;
    if (min(g.x, g.y) < 1.0 || max(g.x, g.y) > n - 1.0) { level = 1.0; g = xy / (4.0 * _CloudLightG.x) + 0.5 * n; }
    if (min(g.x, g.y) < 1.0 || max(g.x, g.y) > n - 1.0) { level = 2.0; g = xy / (16.0 * _CloudLightG.x) + 0.5 * n; }
    ok = cd >= 0.5 && level < _CloudLightF.y && min(g.x, g.y) >= 1.0 && max(g.x, g.y) <= n - 1.0 && kf >= -0.5 && kf <= H - 0.5;
    cell = _CloudLightG.x * pow(4.0, level);
    // (how far inside the last level's edge: 0 at it to 1 eight cells in, where the walk's own steps take over beyond it)
    inner = level < _CloudLightF.y - 1.0 ? 1.0 : clamp((min(min(g.x, g.y), n - max(g.x, g.y)) - 1.0) / 8.0, 0.0, 1.0);
    float k0 = clamp(floor(kf), 0.0, H - 1.0), k1 = min(k0 + 1.0, H - 1.0), fk = clamp(kf - k0, 0.0, 1.0);
    // (twice as tall as its levels: the sun's half, then the sky's, the same cells in the same places)
    vec2 size = vec2(4.0 * n, 2.0 * rows * n * _CloudLightF.y);
    vec2 inTile = clamp(g, vec2(0.5), vec2(n - 0.5));
    vec2 uv0 = (vec2(mod(k0, 4.0), floor(k0 / 4.0) + level * rows) * n + inTile) / size;
    vec2 uv1 = (vec2(mod(k1, 4.0), floor(k1 / 4.0) + level * rows) * n + inTile) / size;
    skyLit = mix(textureLod(_CloudLight, uv0 + vec2(0.0, 0.5), 0.0).r, textureLod(_CloudLight, uv1 + vec2(0.0, 0.5), 0.0).r, fk);
    return mix(textureLod(_CloudLight, uv0, 0.0), textureLod(_CloudLight, uv1, 0.0), fk);
}

float firstStep(float t0, float j, float least, float longest, float each)
{
    float sA = least / each;
    // (uniform steps of `least` to sA, the first point at or past it begins the growing steps)
    float pAB = (ceil(sA / least - j) + j) * least;
    if (t0 <= pAB || longest <= least) return (ceil(t0 / least - j) + j) * least;
    float g = log(1.0 + each);
    float sB = longest / each;
    float nBC = ceil(log(max(sB / pAB, 1.0)) / g);
    float pBC = pAB * exp(nBC * g);
    if (t0 <= pBC) return pAB * exp(ceil(log(t0 / pAB) / g) * g);
    return pBC + ceil((t0 - pBC) / longest) * longest;
}
in vec2 vs_TEXCOORD0;
layout(location = 0) out vec4 SV_Target0;
layout(location = 1) out vec4 SV_Target1;

#define PI 3.14159265

float remap(float v, float a, float b, float c, float d) { return c + (v - a) / max(b - a, 1e-5) * (d - c); }

// (Development build: trials 51 to 81 put stage 5's light terms (2026-10-26) back as they were, a piece at a time, for films of
// before and after in one sitting (Clouds.Try2). Over 50, any sum of: 1, the light scattered many times as much as the body it is
// in is thick (the walk's `bodyShare`); 2, the heaps' water by height (`condensed`); 4, all of the layers' light (the forward light
// dimmed by 0.26 of the depth in a sheet and 0.15 in a heap, the depth to a sheet's own top worked out whole, the light deep in a
// sheet by the two-stream rule); 8, only the heaps' and pads' forward light by a tenth of the depth, as it was; 16, only the light
// deep in a sheet as it was. 65 is the light as it was. Nought, and every release build, which sends nought, has them all. Trials
// 20 to 22 are Clouds.cs's own.)
bool trialOff(float piece) { float k = _CloudForm.z - 50.0; return k > 0.5 && k < 31.5 && mod(floor(k / piece + 0.01), 2.0) > 0.5; }

// The clouds' second form (2026-10-27): 1 in the copies of this shader built for it (make_bundle.py: clouds2, clouds2storm,
// clouds2calm), whose low and middle clouds are shaped by fieldAt; nought in the others, whose are shaped by formAt. A constant,
// so that each copy holds only its own form's code (Windows' compiler puts every function inline: both would double the walk).
const float FORM_TWO = 0.0;

// ---------------------------------------------------------------- noise to make the cubes from

uint mixBits(uint x)
{
    x ^= x >> 16u; x *= 0x7feb352du; x ^= x >> 15u; x *= 0x846ca68bu; x ^= x >> 16u;
    return x;
}

ivec3 wrap(ivec3 c, int period) { return c - period * ivec3(floor(vec3(c) / float(period))); }

uint cellHash(ivec3 c, int period, uint seed)
{
    ivec3 w = wrap(c, period);
    return mixBits(uint(w.x) + mixBits(uint(w.y) + mixBits(uint(w.z) + seed)));
}

// Cells (Worley noise): 1 at each cell's point, falling to 0 a cell's width away. The pattern repeats every "period" cells.
float cells(vec3 p, int period, uint seed)
{
    vec3 base = floor(p);
    vec3 f = p - base;
    ivec3 b = ivec3(base);
    float best = 9.0;
    for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                ivec3 o = ivec3(x, y, z);
                uint h = cellHash(b + o, period, seed);
                vec3 point = vec3(float(h & 1023u), float((h >> 10u) & 1023u), float((h >> 20u) & 1023u)) / 1023.0;
                vec3 d = vec3(o) + point - f;
                best = min(best, dot(d, d));
            }
    return clamp(1.0 - sqrt(best), 0.0, 1.0);
}

float cellsLayered(vec3 p, int period, uint seed)
{
    return cells(p * float(period), period, seed) * 0.625 + cells(p * float(period * 2), period * 2, seed + 7u) * 0.25 + cells(p * float(period * 4), period * 4, seed + 13u) * 0.125;
}

// Cells measured by the square of the distance to the nearest of their points (in cells): nought at a point, rising round it as a
// bowl. One less this is a rounded dome on every point, where one less the distance (cells, above) is a cone.
float cellsSq(vec3 p, int period, uint seed)
{
    vec3 base = floor(p);
    vec3 f = p - base;
    ivec3 b = ivec3(base);
    float best = 9.0;
    for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                ivec3 o = ivec3(x, y, z);
                uint h = cellHash(b + o, period, seed);
                vec3 point = vec3(float(h & 1023u), float((h >> 10u) & 1023u), float((h >> 20u) & 1023u)) / 1023.0;
                vec3 d = vec3(o) + point - f;
                best = min(best, dot(d, d));
            }
    return best;
}

// The second form's billows at a place of their cube (0 to 1 across it), before they are stretched (go 7): eight octaves of
// rounded cells, 2 to 256 across the cube, each 0.57 of the last (Blackrack's count and falloff for Kerbin's clouds, and his
// rounded cells: by the square of the distance, which is what makes a heap's cauliflower), each octave's lattice moved off the
// others'. Its 0.1 % and 99.9 % points, 0.36389 and 0.91240, are measured by tools/clouds/billow.py, which works out the same
// sum with the same points.
float billowMake(vec3 p)
{
    float sum = 0.0, amp = 1.0, total = 0.0;
    for (int o = 0; o < 8; o++)
    {
        int period = 2 << o;
        vec3 off = fract(vec3(0.291, 0.617, 0.853) * float(o + 1) * 2.71);
        sum += amp * (1.0 - min(cellsSq(p * float(period) + off, period, 701u + 31u * uint(o)), 1.0));
        total += amp; amp *= 0.57;
    }
    return sum / total;
}

vec3 slope(ivec3 c, int period, uint seed)
{
    uint h = cellHash(c, period, seed);
    vec3 g = vec3(float(h & 1023u), float((h >> 10u) & 1023u), float((h >> 20u) & 1023u)) / 511.5 - 1.0;
    return g / max(length(g), 1e-3);
}

// Smooth noise (Perlin's), -1 to 1 or near it, repeating every "period".
float smoothNoise(vec3 p, int period, uint seed)
{
    vec3 base = floor(p);
    vec3 f = p - base;
    ivec3 i = ivec3(base);
    vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float a = dot(slope(i, period, seed), f);
    float b = dot(slope(i + ivec3(1, 0, 0), period, seed), f - vec3(1.0, 0.0, 0.0));
    float c = dot(slope(i + ivec3(0, 1, 0), period, seed), f - vec3(0.0, 1.0, 0.0));
    float d = dot(slope(i + ivec3(1, 1, 0), period, seed), f - vec3(1.0, 1.0, 0.0));
    float e = dot(slope(i + ivec3(0, 0, 1), period, seed), f - vec3(0.0, 0.0, 1.0));
    float g = dot(slope(i + ivec3(1, 0, 1), period, seed), f - vec3(1.0, 0.0, 1.0));
    float h = dot(slope(i + ivec3(0, 1, 1), period, seed), f - vec3(0.0, 1.0, 1.0));
    float k = dot(slope(i + ivec3(1, 1, 1), period, seed), f - vec3(1.0, 1.0, 1.0));
    return mix(mix(mix(a, b, u.x), mix(c, d, u.x), u.y), mix(mix(e, g, u.x), mix(h, k, u.x), u.y), u.z) * 1.4;
}

// ---------------------------------------------------------------- the weather and the shapes

// ---------------------------------------------------------------- a cyclone
//
// What a hurricane is, and so what is drawn. Over warm sea the air near the water flows in towards a low, turning as it
// comes (against the clock north of the equator), and rises where it can come no nearer: in a ring round the middle, the
// eye wall, a ring of storm cloud from a few hundred metres up to the top of the weather. The ring leans outward as it
// rises (the air keeps its spin as it climbs, and what holds it in weakens with height), so the eye is twice as wide at
// its top as at the sea: from inside, a stadium. At the top the air can rise no further and flows out all round: a
// canopy of ice many times wider than the wall, thick at the wall and thinning to streaks at its rim, with the wall's
// own tops bubbling up through it round the eye. In the eye the air sinks and dries: clear from the canopy's level down
// to a little low cloud on its floor. Outside the wall the air comes in along spiral bands, rows of storm towers with
// gaps of nearly clear air between them, wound in towards the middle the way the wind blows.
//
// The wall, the canopy and the bands are shapes of their own here (cycloneAt), laid out round the cyclone's middle as
// the weather has it (Violent.cs: the same eye, wall and bands the wind and the rain go by). The planet's cloud map
// cannot hold them: a dot of it is 15 km on Kerbin, and an eye is 12 across. What the map's layers do inside a cyclone
// is set here too (weatherAt): a floor of broken low cloud in the eye and nothing above it, no high cloud where the
// canopy is, and the gaps between the outer bands.

vec2 eyeXY;          // where the place last asked about is from the cyclone's middle: metres east and north
float eyeBreak;      // and a number of the noise that changes along its bands and from one to the next: where a band is broken
float eyeAcross;     // how far the place is to one side of the nearest band's middle line or the other (m)
float eyeTurn;       // the bands' angle there as the rule alone has it (the canopy's fibres go by this one)
float eyeAlong;      // how far along its band the place is: a number that is the same all the way across a band and grows along it by one for every 2.5 radians of the bands' angle

// Where a place is in the cyclone: x, how far out, in radii of its strongest wind; y, how far the place is from the
// middle line of the nearest band (m); z, how far out (m); w, the bands' own angle there (a band's middle line runs
// wherever it is a whole number of half turns).
vec4 eyeAt(vec3 p)
{
    vec3 mid = _CloudEyeA.xyz / max(length(_CloudEyeA.xyz), 1.0);
    float along = dot(p, mid);
    if (along <= 0.0) return vec4(1e6, 1e9, 1e9, 0.0);
    vec3 flat_ = p - mid * along;
    eyeXY = vec2(dot(flat_, _CloudEyeC.xyz), dot(flat_, cross(_CloudEyeC.xyz, mid)));
    float r = length(eyeXY), x = r / _CloudEyeA.w;
    // (as Violent.CycloneAt has it: two bands, a turn inward for every e-fold and a bit of distance)
    float angle = r > 1.0 ? atan(eyeXY.y, eyeXY.x) : 0.0;
    float turn = angle * _CloudEyeB.y + 2.3 * log(max(x, 0.5)) + _CloudEyeB.z;
    eyeTurn = turn;
    // (the curves that cross the bands squarely: 2.3 times the angle round, less the logarithm of the distance out)
    eyeAlong = 2.3 * angle * _CloudEyeB.y - log(max(x, 0.5));
    // (and outside the core no band is the curve the rule draws: each wanders a quarter of a radian either way of it,
    // which is well inside the width the weather gives its rain. Drawn by the rule alone, the two bands seen from
    // above were a pair of sickles.)
    eyeBreak = 0.5;
    for (int once = x > 3.0 && x < 14.5 ? 0 : 1; once < 1; once++)
    {
        vec4 wander = textureLod(_CloudShape, vec3(cos(angle) * 0.35, sin(angle) * 0.35, x * 0.09) + vec3(0.11, 0.53, 0.29), 2.0 * _CloudHaze.x);
        turn += (wander.r - 0.74) * 3.0 * smoothstep(3.0, 6.0, x);
        eyeBreak = wander.g;
    }
    float off = turn - PI * floor(turn / PI + 0.5);
    eyeAcross = off * r / 2.508;
    return vec4(x, abs(eyeAcross), r, turn);
}

// How far out a cyclone's canopy reaches, in radii of its strongest wind.
float eyeRim() { return 5.5 + 3.5 * _CloudEyeB.x; }

// One of the weather's maps read smoothly: the cubic spline through its dots, from four bilinear readings (Sigg and
// Hadwiger, 2005). Read bilinearly, a field that the clouds' cover cuts at a level is cut along the map's own dots: from
// orbit the edge of a region of cloud was a staircase, straight runs and right angles fifteen kilometres long. (Used by
// stage 6, once a frame, for the low clouds' map: see weatherAt.)
vec4 mapSmooth(sampler2D map, vec2 uv)
{
    vec2 size = vec2(textureSize(map, 0));
    vec2 at = uv * size - 0.5;
    vec2 i = floor(at), f = at - i;
    vec2 f2 = f * f, f3 = f2 * f;
    vec2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0, w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
    vec2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0, w3 = f3 / 6.0;
    vec2 g0 = w0 + w1, g1 = w2 + w3;
    vec2 h0 = (i - 0.5 + w1 / g0) / size, h1 = (i + 1.5 + w3 / g1) / size;
    return g0.y * (g0.x * textureLod(map, h0, 0.0) + g1.x * textureLod(map, vec2(h1.x, h0.y), 0.0))
         + g1.y * (g0.x * textureLod(map, vec2(h0.x, h1.y), 0.0) + g1.x * textureLod(map, h1, 0.0));
}

// The weather at a place (from the planet's centre, the scene's axes): the low clouds (amount, kind, base, top), the rest
// (wet, high, storm, middle amount) and the middle clouds (kind, base, top, wet).
void weatherAt(vec3 p, out vec4 a, out vec4 b, out vec4 c)
{
    vec3 f = vec3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
    float r = max(length(f), 1.0);
    float lon = atan(f.y, f.x);
    float lat = asin(clamp(f.z / r, -1.0, 1.0));
    vec2 uv = vec2(lon / (2.0 * PI) + 0.5, lat / PI + 0.5);
    // (and the reading bent a little, by up to most of a dot of the weather's map, over some tens of kilometres, as a
    // weather's edge wanders: so that no edge runs along the map's rows and columns, even where it changes sharply)
    vec2 bend = textureLod(_CloudShape, f / 61000.0 + vec3(0.37, 0.11, 0.73), 2.0 * _CloudHaze.x).gb - 0.48;
    uv += bend * (1.6 / vec2(textureSize(_CloudMapB, 0)));
    // (The low clouds' map as Clouds.cs hands it: drawn smoothly once a frame at four times the weather's own (stage 6),
    // so that this one reading of it is smooth. Read here smoothly, four readings at every 1200 m of a line cost one and
    // a half milliseconds a frame from the ground.)
    a = textureLod(_CloudMap, uv, 0.0);
    b = textureLod(_CloudMapB, uv, 0.0);
    c = textureLod(_CloudMapC, uv, 0.0);
    a.z *= 8000.0; a.w *= 16000.0;
    c.y *= 12000.0; c.z *= 16000.0;
    // ---- in a cyclone: what the map is too coarse to say
    for (int once = _CloudEyeA.w > 0.0 ? 0 : 1; once < 1; once++)
    {
        vec4 e = eyeAt(p);
        if (e.x > 14.0) break;
        float now = _CloudEyeB.x, rim = eyeRim(), base = _CloudEyeC.w, deep = max(_CloudEyeB.w - base, 1000.0);
        // (under the canopy, whatever the map's dots make of it: rain cloud from just over the sea to the middle of the
        // storm's depth, and a sheet of middle cloud over that)
        // (its edge wandering in and out by a radius and more: a circle's edge showed from above as a ring round the canopy)
        float under = (1.0 - smoothstep(rim - 3.0, rim - 0.5, e.x + (eyeBreak - 0.5) * 3.0)) * smoothstep(0.1, 0.3, now);
        a = mix(a, vec4(0.97, 0.04, base + 50.0, base + 0.42 * deep), under);
        b.x = mix(b.x, 0.9, under); b.a = mix(b.a, 0.9, under);
        c = mix(c, vec4(0.1, base + 0.45 * deep, base + 0.7 * deep, 0.6), under);
        // (the eye, where a cyclone is strong enough to have one: broken low cloud on its floor, and nothing over it)
        float eye = (1.0 - smoothstep(0.5, 0.7, e.x)) * smoothstep(0.35, 0.6, now);
        a = mix(a, vec4(0.32, 0.5, base + 150.0, base + 600.0), eye);
        b = mix(b, vec4(0.15, 0.0, 0.0, 0.0), eye);
        // (the canopy is the high cloud, where it is)
        b.g *= smoothstep(rim - 1.0, rim + 1.5, e.x);
        // (and outside it: heaps, many near the bands and few in the gaps between them, and little middle cloud. Not
        // what the map makes of a cyclone's edge, a sheet of rain cloud kilometres thick with holes in it: from above
        // that is a tableland cut by canyons.)
        float band = smoothstep(0.25, 0.8, 0.5 + 0.5 * cos(2.0 * e.w)) * (1.0 - smoothstep(9.0, 13.0, e.x)) * (0.3 + 0.7 * smoothstep(0.35, 0.65, eyeBreak));
        float outside = smoothstep(rim - 3.0, rim - 0.5, e.x + (eyeBreak - 0.5) * 3.0) * (1.0 - smoothstep(11.0, 14.0, e.x)) * smoothstep(0.1, 0.3, now);
        // (Along the bands the heaps are the bands' own storms, crowded and tall: towering heaps to six kilometres, nearly
        // closing up along a band's middle, under the ice the bands throw out (the canopy's arms). From orbit a band is a
        // lumpy line of storms under its outflow, as photographs have one; with half the sky in heaps a kilometre tall
        // there, the arms were smooth combed ice over a scatter of flecks (the main chat, 2026-10-19 17:06).)
        float crowd = band * band * (3.0 - 2.0 * band);
        a = mix(a, vec4(mix(0.16, 0.95, crowd), mix(0.52, 0.68, band), base + 100.0, base + mix(650.0, 6000.0, band)), outside);
        b.x = mix(b.x, 0.25 * band, outside); b.a *= 1.0 - outside;
    }
}

// What changes over kilometres to hundreds of them, read where the weather is read (every 1200 m along a line of sight; the
// same for the light's steps from there): x, how much more or less cover there is than the weather says (heaps in clusters
// a few kilometres across with clear sky between, and wider patches of more and of less over tens of kilometres); yzw, a bend
// of the noise (its 9 km repeat broken up). These are read at a third, a ninth and a 23rd of the large noise's scale
// (repeating every 27, 81 and 207 km), which the camera's place wrapping round every 207 lengths leaves unchanged. (Read from
// the noise's 16-cube, smooth over those 1200 m: the full noise, read once every 1200 m, jumped there, and the jumps showed as
// lines across the heaps.)
vec4 broadAt(vec3 g)
{
    float m = 3.0 * _CloudHaze.x;
    vec4 group = textureLod(_CloudShape, g.yzx / 3.0 + vec3(0.71, 0.37, 0.19), m);
    vec4 broad = textureLod(_CloudShape, g / 9.0 + vec3(0.53, 0.29, 0.71), m);
    vec4 broader = textureLod(_CloudShape, g.zxy / 23.0 + vec3(0.17, 0.83, 0.41), m);
    // (the 16-cube's first channel lies between 0.655 and 0.82 over eight tenths of it, its middle at 0.745, measured: each
    // spread from about none to all round that middle, so that on the whole there is as much cloud as the weather says)
    float cluster = smoothstep(0.66, 0.83, group.r);
    float wander = smoothstep(0.68, 0.81, (broad.r + broader.r) * 0.5);
    float raise = mix(0.4, 1.8, cluster) * mix(0.55, 1.6, wander);
    return vec4(raise, broad.g - 0.48, broader.g - 0.48, broad.b - 0.48);
}

// Cloud streets: where a steady wind blows over churning air, the churning rolls lie along the wind and lift the air in lines
// a few kilometres apart, and the heaps grow in rows along it. A factor on the cover (on the whole 1), the rows wandering;
// read at every step (they are 3 km apart, far too near together to be read only every 1200 m).
float streetsAt(vec3 g, vec4 wm)
{
    if (_CloudStreets.w <= 0.0) return 1.0;
    float across = dot(g, _CloudStreets.xyz) * (9000.0 / 3000.0) * 6.2832 + wm.y * 14.0;
    return mix(1.0, 0.45 + 1.1 * smoothstep(-0.4, 0.7, cos(across)), _CloudStreets.w);
}

// How the cloud of each kind fills out from its base (0) to its top (1): flat sheets, heaps rounded on top with flat bases
// (all at the height where rising air begins to condense), towers that spread out where they reach the top.
float heightShape(float hf, float kind)
{
    float sheet = clamp(hf / 0.12, 0.0, 1.0) * clamp((1.0 - hf) / 0.4, 0.0, 1.0);
    // (a heap is full nearly to its top and domed there: 1 - h^2.6. Until the rework of 2026-10-11 it thinned in a straight line
    // from three tenths of the way up, which with the cover's threshold left heaps a few hundred metres thick and kilometres
    // wide: pancakes, of which one saw the grey base from the ground and a pad of cotton wool from above)
    float heap = clamp(hf / 0.025, 0.0, 1.0) * (1.0 - pow(hf, 2.6));
    float tower = clamp(hf / 0.02, 0.0, 1.0) * clamp((1.0 - hf) / 0.1, 0.0, 1.0);
    float a = clamp(kind * 2.0, 0.0, 1.0), b = clamp(kind * 2.0 - 1.0, 0.0, 1.0);
    return mix(mix(sheet, heap, a), tower, b);
}

// The heaps in a reading of the large noise: its first channel swelled by its cells (Perlin's noise with Worley's, as in
// Nubis), spread out, and the cells' own billows on the outline.
float heapsOf(vec4 s)
{
    float lowCells = s.g * 0.625 + s.b * 0.25 + s.a * 0.125;
    float heap = clamp(remap(s.r, lowCells - 1.0, 1.0, 0.0, 1.0), 0.0, 1.0);
    // (that lies between 0.74 and 0.92 nearly everywhere, measured over the whole cube: spread out to 0 to 1, so that a cover of
    // 0.4 is cloud in about four tenths of the air and not in all of it)
    heap = clamp((heap - 0.74) * 5.5, 0.0, 1.0);
    // The cells themselves swell the outline: billows on billows (the formula above leaves them almost no say: its slope with
    // the cells is about a fifth). Heaps of cauliflower, not balls.
    // (the finer cells count for as much as the coarse: turrets a few hundred metres across on a heap a kilometre wide)
    float billow = clamp((s.g * 0.3 + s.b * 0.35 + s.a * 0.35 - 0.3) * 1.9, 0.0, 1.0);
    return clamp(heap * (0.52 + 0.6 * billow) + 0.05, 0.0, 1.0);
}

// How much of the sky the place last read of the field of heaps sees (its third number); less than nothing: no such reading.
float heapsOpen;
// And how many metres inside a heap's surface that place is, small lumps and all; far below nothing: no such reading.
float heapsInside;
// And how many metres inside it would be were the heap without its lumps (the new shapes'); far below nothing: not known.
float heapsSmooth;
// How near any cloud can be to the place last asked about (cloudAt): less than nothing, at least so many metres off, and the
// walk may stride that far through clear air; nothing or more, here or unknown. Every source of cloud whose shape is kept as
// a distance says how near it is (the field's heaps, the storms' towers; a layer the place is above or below, by how far);
// one carved from noise, which knows no distances, says unknown (a great number).
float heapsNear;
// What the light asks of the body of cloud the place last asked about is in (stage 5, 2026-10-26: the walk's light), said by the new
// shapes' sources with heapsOpen. cloudAt says "thick, a heap's top, not a sheet" first; the old heaps, the storms, the cyclone and
// the shelf leave it so. x: how thick the body is the thinnest way through it, in metres of cloud as dense as a heap's core (1e5:
// thick); y: how much of a heap's water by height is at the place, against its top's (1: all, or not a heap); z: in a layer with a
// sheet, how many metres up the sheet's own top is over the place (less than nothing: no sheet); w: how dense the sheet is through,
// against a heap's core, by how much the sheet has the place (nothing where a pad has it).
vec4 heapsBody;
// (and of the layer that body is of, for the light deep in a sheet: how much of it is sheet, and the height of its base. Set with
// heapsBody by formAt, nothing by cloudAt first; a storm's body, which says it is no sheet, leaves it as it was.)
vec2 heapsDeck;
// What the second form's light asks of the place last asked about (fieldAt; the rebuild of 2026-10-27): x, how deep in its cover the
// place is, the billows left out (the column's curve and the cover, less one: 1 and more deep in a heap); y, how far it is past the
// billows' edge, as the same; z, how much of the sky above it sees through the cloud between it and its own top there (less than
// nothing: not known); w, how bright its light scattered many times over is (his stratus' 0.8; 1), less than nothing for a storm's
// cloud, lit by his storm's weights. cloudAt says "a storm's, half way in, not known" first.
vec4 formLight;
// (The second form: how coarsely the billows are read for the light towards the sun past its first two steps, as one of the cube's
// smaller copies: the third, eight times coarser, as Blackrack's light reads his noise beyond the march's own few steps (his light
// volume). Read as finely as for the picture, the sun's way through a heap went through the gaps between its billows: the sun
// came through a heap's eaten outer half at two to five e-folds where his meets ten and more, and every shaded side and base was
// lit nearly as its tops (the false-colour stills of 2026-10-27). 0 for the picture.)
float billowLight = 0.0;

// The level of the field above which is cloud, for so much of the sky to be covered (sixteen levels given, read between).
float fieldLevelAt(int i)
{
    vec4 v = i < 4 ? _CloudFieldLevA : i < 8 ? _CloudFieldLevB : i < 12 ? _CloudFieldLevC : _CloudFieldLevD;
    int j = i - (i / 4) * 4;
    return j == 0 ? v.x : j == 1 ? v.y : j == 2 ? v.z : v.w;
}

float fieldLevel(float cover)
{
    float x = cover * 16.0 - 0.5;
    if (x <= 0.0) return mix(1.0, _CloudFieldLevA.x, clamp(cover * 32.0, 0.0, 1.0));
    x = min(x, 15.0);
    int i = int(x);
    return mix(fieldLevelAt(i), fieldLevelAt(min(i + 1, 15)), x - float(i));
}

// The heaps made beforehand, at a place (from the planet's centre, the scene's axes) so far up its layer (0 base to 1 top):
// how many metres inside the surface that the cover picks it is (outside it, less than nothing).
// Where in the field: east and north over the ground. The planet is a ball and the field is flat, so the ground is taken in
// bands of latitude, each with its own scale east and west (a band is as wide as the cosine of the latitude takes to fall
// by a tenth: nowhere are the heaps stretched or squeezed by more than a twentieth); the band round the equator reaches to
// 18 degrees each way.
vec3 heapsPlace(vec3 p)
{
    vec3 f = vec3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
    float r = max(length(f), 1.0);
    float lat = asin(clamp(f.z / r, -1.0, 1.0)), lon = atan(f.y, f.x);
    // (the equator in the middle of its band: as an edge it fell wherever the cosine of the latitude, worked out in single
    // precision, first came to less than one, 150 m to either side of the equator: a strip of other heaps across the sky of
    // a space centre that stands a kilometre from it)
    float band = floor(log2(max(cos(lat), 0.02)) * 7.0 + 0.5);
    return vec3(lon * exp2(band / 7.0) * _CloudCam.w, lat * _CloudCam.w, band);
}

// The field read at a place in it (heapsPlace: metres east and north, and which band), `size` times larger over the ground
// than it was made (for a deeper layer: see layerAt).
float heapsAt(vec3 place, float hf, float level, vec4 wm, float lod, float pixel, bool seek, float size)
{
    float per = _CloudFieldA.x / size;
    vec2 uv = (place.xy + wm.yz * 5400.0 + _CloudFieldB.yz * (hf * size)) * per + _CloudFieldA.zw / size + (place.z + size) * vec2(0.37, 0.61);
    // (Seeking: whether there may be any heap within a stride and a half of here, the stride being three hundredths of the
    // distance from the camera. The field's second number at the copy of it whose cells are a third of that stride: never
    // less than the number anywhere within the stride. The stride is 0.03 of the distance, the distance 25 km times two to
    // the power of `lod` less one (the seeker asks one size coarser than the walk reads), a cell 25 m times `size`.)
    if (seek) return (textureLod(_CloudField, vec3(uv, hf), ceil(max(lod + 2.33 - log2(size), 0.0))).g - level) * _CloudFieldA.y * size;
    // (Not seeking: the copy of the field whose cells are as wide as a pixel of the picture is there, `pixel` metres.)
    vec4 cell = textureLod(_CloudField, vec3(uv, hf), max(log2(pixel * per * 256.0) + 0.3, 0.0));
    heapsOpen = cell.b;
    // (The usual sky. The field has heaps of every size, many small and few large, as a count of real ones has; but all
    // of them in the sky at once, the hundreds of small ones each by itself, is a sky that does come and is not the
    // common one ("super noisy": the user, of a picture from above, 2026-10-11). Commonly the small ones stand round
    // the large or are not there. So unless today is such a day the small heaps that stand alone are left out: the field
    // read very coarsely, near its base, is high only where a wide heap is, and away from there the level is raised by
    // more than a small heap has. The seeker is not told: it may say "perhaps cloud" where there is now none, never the
    // other way.)
    for (int once = _CloudHabit.x > 0.01 ? 0 : 1; once < 1; once++)
    {
        float wide = textureLod(_CloudField, vec3(uv, 0.22), 3.6).r;
        level += _CloudHabit.x * 0.24 * (1.0 - smoothstep(0.36, 0.5, wide));
    }
    return (cell.r - level) * _CloudFieldA.y * size;
}

// How large the field is read for a layer so deep. Its heaps are made up to 1500 m tall, and its top is the layer's top: a
// layer much deeper than that would stretch them into pillars (a 3 km layer made columns of every heap, seen from beside
// them), and a much shallower one press them into plates. So for a deeper layer the field is read larger over the ground,
// heaps as much wider as they are taller (which they are: a tower three kilometres high stands on a base kilometres across):
// five sizes, each 1.6 times the last, each serving layers from seven tenths of its own depth to one and fifteen hundredths
// of it. Where one size gives way to the next (the last eighth of its range: `next` then runs from 0 to 1) both are read,
// the heaps of the one shrinking away as the other's grow.
float heapsSize(float deep, out float next)
{
    float sizing = log2(max(deep, 200.0) / 1500.0) / 0.678 + 0.703;
    // (And smaller for a shallower one, three sizes down: fair weather's layer is a few hundred metres deep, and the field
    // read at its own size under so low a top was pressed into plates lying on the air, saucers in stacks.)
    float which = clamp(floor(sizing), -3.0, 4.0);
    next = which < 3.5 ? clamp((sizing - which - 0.88) / 0.12, 0.0, 1.0) : 0.0;
    // (and as much larger again as the day has them: on the usual day a shallow layer's heaps are broader than they are
    // tall, the flat-topped heaps of fair weather; the tall layers' heaps are large already and are left)
    return exp2(0.678 * which) * mix(max(_CloudHabit.y, 1.0), 1.0, clamp(which / 2.0, 0.0, 1.0));
}

// The heaps made beforehand at a place, for a layer so deep with so much of the sky covered: how many metres inside a
// heap's surface it is, before the small lumps (seeking: the most that could be, anywhere within a stride). `size`: how
// large the field was read.
float heapsMetres(vec3 p, float hf, float deep, float cover, vec4 wm, float lod, float pixel, bool seek, bool turned, vec2 pushed, out float size)
{
    float next;
    size = heapsSize(deep, next);
    vec3 place = heapsPlace(p);
    // (no heap is round: where each stands is pushed about by a pattern that changes over a kilometre or two, more for
    // the larger readings of the field, so that an outline runs out in one place and is bitten into in another)
    place.xy += pushed * size;
    if (turned) place = vec3(place.y + 2371.0, -place.x + 1289.0, place.z);
    // (six hundredths less than asked: measured from straight above over 37 km by 21, the heaps hid 0.17, 0.26, 0.36, 0.45,
    // 0.55 and 0.70 of the ground where 0.1, 0.2, 0.3, 0.4, 0.5 and 0.65 were asked for; the clusters and gaps that the cover
    // is given do not come out even)
    float level = fieldLevel(max(cover - 0.06, cover * 0.4));
    float inside = heapsAt(place, hf, level, wm, lod, pixel, seek, size) - next * 300.0 * size;
    for (int again = next > 0.0 ? 0 : 1; again < 1; again++)
    {
        float open = heapsOpen, other = heapsAt(place, hf, level, wm, lod, pixel, seek, size * 1.6) - (1.0 - next) * 480.0 * size;
        if (other > inside) inside = other; else heapsOpen = open;
    }
    return inside;
}

// The lumps too small for the field's cells (25 m), carved at the pixel: cells of 50, 25 and 12 m, lumps upon lumps, each
// standing proud of the creases round it by about its own half-width, as the lumps of a cauliflower do. Their middle is left
// where the field has the surface (the carving is taken about its mean, which is also what the small noise comes to when
// read coarsely, far off: no carving there, and as much cloud). Where the field is read larger, its cells are larger too,
// and lumps of the sizes between are carved as well.
float heapsLumps(float inside, vec3 at, float pixel, float size)
{
    if (inside <= -60.0 * size || inside >= 60.0 * size) return inside;
    float lumps = textureLod(_CloudDetail, at * 45.0 + vec3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / 3.1) + 0.3, 0.0, 6.0) * _CloudHaze.x).r;
    inside += (lumps - 0.48) * _CloudFieldB.w;
    for (int more = size > 1.5 ? 0 : 1; more < 1; more++)
    {
        float larger = textureLod(_CloudDetail, at.yzx * (45.0 / size) + vec3(0.31, 0.57, _CloudDrift.w * 0.6), clamp(log2(pixel / (3.1 * size)) + 0.3, 0.0, 6.0) * _CloudHaze.x).r;
        inside += (larger - 0.48) * _CloudFieldB.w * size * 0.8;
    }
    return inside;
}

// ---------------------------------------------------------------- the shapes of the rework of 2026-10-12
//
// The low and the middle clouds as shapes worked out in metres: heaps as domes over rising air, with lumps on them.
//
// What was wrong before. Heaps carved from noise in three dimensions and cut off at the layer's base and top are whatever
// the noise happens to be there: smooth lenses in a shallow layer. Heaps made beforehand and pressed to the layer's depth
// were pressed lumps and all (plates), and thinned for a smaller cover by wearing each heap down to its core (pointed hats).
//
// What a heap is. Air rises from the ground in separate bubbles, some hundreds of metres to a kilometre or two across,
// one after another over a spot for some minutes. Each condenses at the same height (a level base, the same for every
// heap of a sky) and goes on up as far as its warmth carries it, the wide ones higher than the narrow: a dome over each,
// wider than tall on a fair day. The top of the layer is where the air stops rising (a warmer layer above): what reaches
// it goes no higher and is flat there. A heap lives ten minutes to half an hour: it comes as a small puff at the base,
// grows up and outward with a sharp lumpy outline, and when the air under it stops rising it goes ragged and thin and
// evaporates from its base upward. And the surface of the dome is not smooth: it is the tops of smaller bubbles, rounded,
// each with smaller ones on it, with creases between.
//
// So: places where air rises are set out over the layer's base, one to each point of a lattice of triangles (a little out
// of place each, each with a size and a time of its own). A heap stands over each: it comes, grows, stands and fades,
// and the next one over that point comes somewhere a little different, of another size. Each is a dome (an egg standing
// on the base, cut level there), as tall as it is wide times so much, never higher than the layer's top; neighbours run
// together. On that, round lumps of several sizes (the cells, read in all three directions: they are round whatever the
// layer's depth), each proud by about its own half-width. All of it in metres, so that a surface can be as sharp as a
// pixel, the lumps are the size they should be for the heap, the walk can be told how far off the nearest cloud is, and
// the light which way the surface faces.
//
// The lattice lies on a face of a cube round the planet (the face the place is nearest: two of the place's three numbers
// in the noise's space), so each point of it is found from the two numbers alone and only the three points of the
// triangle a place is in have to be looked at. (Set out through all three directions instead, as it first was, eight
// cells had to be looked at for every place asked about, and the clouds cost three and four times what they had.)

// An egg standing on a level: how many metres inside it a place is (outside: less than nothing). `out_`: how far the place
// is from the egg's upright, level; `up`: how far above the level it stands on; R: its half-width; H: its height.
float eggAt(float out_, float up, float R, float H)
{
    vec2 pos = vec2(out_, up - 0.3 * H), axes = max(vec2(R, 0.7 * H), vec2(1.0));
    float k0 = length(pos / axes), k1 = length(pos / (axes * axes));
    return k0 * (1.0 - k0) / max(k1, 1e-6);
}

// A heap's own dome (heapsRound, and the heaps read from the map): how high its middle stands and how far its half-height
// reaches, as parts of its height. A heap that towers (congestus, near storms) is built as eggAt's egg, widest three tenths
// of the way up with its sides upright there: a column. An ordinary heap, as photographs of a fair or a mostly cloudy sky
// show it, is widest at its flat base and narrows the whole way up into a dome. (Built as the tower's egg everywhere, as it
// was until 2026-10-15, every heap of an ordinary sky was a drum: upright sides, a rounded top and a flat dark bottom, as
// tall as it was wide. The owner: "the clouds are really tall and pillar-like. I don't understand why they're so tall and
// pillary.") `tower`: 0 for an ordinary heap, 1 for one that towers.
// (A heap that towers stands on a flat base as wide as nearly all of it, its sides rising almost upright from the base: widest
// a seventh of the way up, and its base rounded up towards the rim by a sixth of what an ordinary heap's is. Built widest
// three tenths of the way up, with its base rounded up a fifth of its height at the rim as an ordinary heap's, as it was until
// 2026-10-20, a towering heap near a storm had a round underside under its widest part: an egg (the owner, watching the PC:
// "why do the bottoms of the clouds look like eggs?").)
float heapsMiddle = 0.3, heapsHalf = 0.7, heapsRimmed = 1.0;
// (and how far its base is rounded up towards its rim, as a part of its height: nothing in its middle, a fifth of its height
// at its rim for an ordinary heap; `x` how far out from its upright the place is, as a part of its half-width)
float heapsRim(float x) { float r = clamp(x, 0.0, 1.2); return heapsRimmed * 0.2 * r * r * sqrt(r); }
void heapsBuild(float tower) { heapsMiddle = mix(-0.25, 0.15, tower); heapsHalf = mix(1.25, 0.85, tower); heapsRimmed = mix(1.0, 0.15, tower); }
float heapEgg(float out_, float up, float R, float H)
{
    vec2 pos = vec2(out_, up - heapsMiddle * H), axes = max(vec2(R, heapsHalf * H), vec2(1.0));
    float k0 = length(pos / axes), k1 = length(pos / (axes * axes));
    return k0 * (1.0 - k0) / max(k1, 1e-6);
}

// The larger of two, with a rounded join k wide.
float larger(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return max(a, b) + h * h * k * 0.25; }

// The heaps near a place. uv: the place on the layer's base under this one, across the face, in cells of the lattice;
// tilt: what turns a step across the face into a step along the ground there; C: metres to a cell; per: cells to a length
// of the noise; when: the time, in lifetimes of a heap; above: metres above the base; share: how many of the lattice's
// points have a heap; widest: the largest's half-width (metres; none wider than 0.42 of a cell: only the three points of
// the triangle a place is in are looked at, and a heap that reached further than two thirds of a cell from its point,
// lumps and all, would be cut off where the next triangle begins); evenly: 0 if most are near the largest, 1 if they
// are of every size alike; join: how wide the rounded join where two meet (metres); tall: how tall a heap is for its
// width. How many metres inside the surface
// of the heaps the place is (outside: less than nothing), before the lumps and before the base is cut level. Also given:
// how large the heap is that has the place (its half-width, metres), how far through its life (-1 new, 1 gone), and how
// far above the layer's base its own base is (metres).
float heapsRound(vec2 uv, vec2 tilt, vec2 going2, float C, int per, float when, float above, float share, float widest, float evenly, float join, float tall, float lid, float pads, out float size, out float age, out float lift, out float leaves)
{
    // (rows nine tenths of a cell apart, each half a cell along from the last: nine tenths and not the 0.866 of even
    // triangles, so that the lattice comes round where the noise does)
    float n = uv.y * (10.0 / 9.0), m = uv.x - 0.5 * n;
    vec2 corner = floor(vec2(m, n));
    vec2 in_ = vec2(m, n) - corner;
    bool upper = in_.x + in_.y > 1.0;
    // (How far along the line being walked this triangle goes on, in metres: `going2` is how fast the line crosses the
    // face, cells to a metre. Only the heaps of this triangle's three points can be in it, so the walk may stride to
    // where the line leaves it and no further: there other heaps begin. A triangle is three straight edges; the line
    // leaves by the first of them it comes to.)
    float dn = going2.y * (10.0 / 9.0), dm = going2.x - 0.5 * dn;
    vec3 room = upper ? vec3(1.0 - in_.x, 1.0 - in_.y, in_.x + in_.y - 1.0) : vec3(in_.x, in_.y, 1.0 - in_.x - in_.y);
    vec3 closing = upper ? vec3(dm, dn, -dm - dn) : vec3(-dm, -dn, dm + dn);
    vec3 until = room / max(closing, vec3(1e-7));
    leaves = min(until.x, min(until.y, until.z));
    vec2 round_ = vec2(23.0, 230.0) * float(per);
    float inside = -1e5, lowest = 1e5, nearest = -1e5, liftNear = 0.0;
    size = 0.0; age = 0.0; lift = 0.0;
    for (int k = 0; k < 3; k++)
    {
        vec2 cell = corner + (k == 0 ? (upper ? vec2(1.0, 1.0) : vec2(0.0, 0.0)) : k == 1 ? vec2(1.0, 0.0) : vec2(0.0, 1.0));
        // (the point's own numbers, from the table: the lattice comes round where the noise does, and the table within that)
        vec4 its = texelFetch(_CloudCells, ivec2(cell - round_ * floor(cell / round_)) & 255, 0);
        // (its time: each point's heaps follow one another, each begun when the last has gone, at its own moment)
        float turn = its.r + when;
        float born = floor(turn);
        float life = (turn - born) * 2.0 - 1.0;
        // (each heap that comes there of another size than the last: its rank among the others moved on each time)
        float rank = fract(its.g + born * (633.0 / 1024.0));
        // (Most points of a fair sky have no heap, and nothing more is worked out for those: with all of it worked out for
        // every point, as at first, the new shapes cost twice what the old had.)
        if (rank >= share) continue;
        // (It grows over the first quarter of its time, stands, and from seven tenths of the way through it goes: the
        // air under it has stopped rising, and it shrinks and evaporates from its base upward, what is left of it ending
        // as a shred near where its top was.)
        float going = clamp((life - 0.4) / 0.6, 0.0, 1.0);
        going *= going * (3.0 - 2.0 * going);
        float z = 1.0 - rank / share;
        // (A heap that is going. The air under it has stopped rising, and it is eaten away from below: its base climbs
        // through it, it narrows, and what is left at the end is a ragged cap where its top was. It stays where it
        // stood. Until the evening of 2026-10-12 the whole egg was lifted off the base as it shrank: a solid egg afloat
        // and aslant in the sky, which no cloud is.)
        float full = widest * mix(sqrt(z), z, evenly), grown = clamp((life + 1.0) * 2.0, 0.0, 1.0), R = full * grown * (1.0 - 0.45 * going);
        if (R < 15.0 || going > 0.97) continue;
        // (an egg standing on the base: widest three tenths of the way up, so that its sides overhang a little there; each
        // its own shape, between three quarters and one and three tenths of the day's height for its width)
        // (its own base a little above the layer's, by up to forty metres: the height where rising air condenses is nearly
        // but not quite one height, and bases all at one height to the metre lie along the horizon like the slats of a blind)
        float own = tall * (0.75 + 0.55 * fract(its.g * 5.13 + its.r * 1.71));
        float H = 2.0 * full * grown * own, raised = 40.0 * its.a + going * 0.9 * H, up = above;
        // (A place well above its top is that far from it at the least, which is all the walk needs to know of it: nothing
        // more worked out. In a layer of many low heaps that is most of what is asked.)
        if (up > H + 0.35 * C) { inside = max(inside, H - up); continue; }
        // (out of place by up to 0.15 of a cell each way: a heap that stood further out would have its far side cut off
        // where the next triangle begins)
        vec2 d = uv - (vec2(cell.x + 0.5 * cell.y, 0.9 * cell.y) + (its.ba - 0.5) * 0.3);
        float t = dot(d, tilt);
        float out_ = sqrt(dot(d, d) + t * t) * C;
        // (The spreading top. A heap that reaches the layer's top cannot go higher and goes sideways: it is wider under
        // the lid than its own shape would have it, and the more the older. That, and not a sky of separate bulbs, is the
        // usual sky: "usually the clouds aren't huge bulbs ... They're kind of sprawled out" (the user, 2026-10-11).)
        float spread = pads * clamp((H - 0.9 * lid) / (0.3 * lid), 0.0, 1.0) * clamp((up - 0.55 * lid) / (0.3 * lid), 0.0, 1.0) * clamp(life + 0.8, 0.0, 1.0);
        // (never wider so than 0.46 of a cell, for the same reason)
        vec2 pos = vec2(out_ / min(1.0 + 0.9 * spread, 0.46 * C / R), up - heapsMiddle * H), turned = 1.0 / max(vec2(R, heapsHalf * H), vec2(1.0));
        vec2 scaled = pos * turned;
        float k0 = length(scaled);
        float one = k0 * (1.0 - k0) / max(length(scaled * turned), 1e-6);
        // (the numbers of whichever heap has the place; but how high its base is cut, the lowest of all that have it: a
        // heap that is going, standing in a larger one that is not, cut a hollow up into it, the height of its own
        // risen base: caves in the flanks of towers)
        if (one > inside) { size = max(R, 0.3 * full); age = life; }
        // (its base level in the middle and rounded up into its side towards the rim, as a heap's underside is in the
        // photographs: cut level right out to the rim, as it was until 2026-10-15, every heap had a sharp flat dark bottom,
        // and with its upright sides was a drum)
        float rimmed = raised + heapsRim(out_ / max(R, 1.0)) * H;
        if (one > 0.0) lowest = min(lowest, rimmed); else if (one > nearest) { nearest = one; liftNear = rimmed; }
        inside = larger(inside, one, join);
    }
    lift = lowest < 1e4 ? lowest : liftNear;
    return inside;
}

// The middle sky's cloudlets in rows, where the air they are in is sheared (altocumulus undulatus): soft cloudlets, a little
// drawn out, lying in loose lines across the wind, thinning and fraying towards the lanes of sky between the lines, a
// line to every 500-odd metres (a cell and a seventh); over regions some tens of kilometres across, not everywhere (the WMO's atlas,
// research/rendering/experiments/altocumulus-rows.md). Set for each face of the lattice by formOneNear.
float padsRows = 0.0;          // how much the cloudlets lie in rows here (0 to 1)
vec2 padsRowsWay = vec2(1.0, 0.0);   // across the rows, on the lattice's face: the way the wind blows
float padsFray = 0.0;          // how much softer a cloudlet's margin is here, for a row's lane (see formOneNear's edge)

// A layer of pads as the cells of the air's churning (2026-10-17). A layer of pads, stratocumulus low and altocumulus in
// the middle sky, is air turning over in cells: rising in each cell's middle, where the cloud is, and sinking at its
// edges, where it is clear. From the ground it is a network: cloudlets of many sizes and shapes, each filling its cell to
// a crack of open sky, the cracks narrow where the sky is mostly covered and wider where less, until each cell's cloud is
// a tuft in its middle (photographs: the mackerel skies among Wikimedia Commons' altocumulus). Drawn as pads each round a
// point of the heaps' lattice, as they were, a raft of them was rows of balls alike with open sky between. So: points on
// a square grid, each out of place by up to nine twentieths of a cell (cells then of every size and shape, as Worley's
// are); in each cell's part of the ground (the nearest point's, as a Voronoi diagram has it) a pillow out to `crack`
// metres short of the cell's edge, a cushion with a flat top under the lid and rounded down into the crack;
// `share` of the cells have one; and each cell's cloud comes and goes in its time, as a heap does: grown in an eighth of
// it, thinning and drawn in towards its middle from seven tenths of the way through.
// uv, tilt, going2, C, per, when, above: as heapsRound; share: how many of the cells have cloud; crack: metres of open
// sky either side of a cell's edge; used: how deep the layer is (m); corner: how wide the rounding of a cell's corners is
// (cells; nothing: the cell's edge as the soft second-nearest point, see padsColumn). How many metres inside the pillow
// the place is (outside: less than nothing), and given out as heapsRound gives.
// (A cell for a place over the ground, the same at every height (the map of the heaps holds it for the low layer): how
// far inside its pillow's edge the place is (metres; outside: less than nothing), the pillow's reach through it (m;
// nothing: an empty cell), how thick it is (m), and how far through its life; `liftOut`: how high its base stands
// there, over the layer's.)
vec4 padsColumn(vec2 uv, vec2 tilt, float C, int per, float when, float share, float crack, float used, float corner, out float liftOut)
{
    float round_ = 23.0 * float(per);
    // (the nearest two by distance over weight, their weights, and the true distance to the nearest, in cells; and each of the
    // nine's distance over weight and weight, for the edge taken from the lines where another comes as near)
    vec2 home = floor(uv);
    float f1 = 1e5, f2 = 1e5, w1 = 1.0, w2 = 1.0, r1 = 0.0, all = 0.0;
    float fs[9], ws[9];
    int n1 = 0;
    vec4 its1 = vec4(0.0);
    for (int k = 0; k < 9; k++)
    {
        float kk = float(k), row = floor(kk / 3.0);
        vec2 cell = home + vec2(kk - 3.0 * row - 1.0, row - 1.0);
        vec4 its = texelFetch(_CloudCells, ivec2(cell - round_ * floor(cell / round_)) & 255, 0);
        vec2 d = uv - (cell + 0.5 + (its.ba - 0.5) * 0.9);
        float t = dot(d, tilt);
        // (each point with a weight of its own, its distances divided by it: the cells then of more sizes still, and their
        // edges arcs, not the straight lines of plain Voronoi cells)
        float r = sqrt(dot(d, d) + t * t), w = 0.75 + 0.5 * fract(its.g * 3.7 + its.b * 1.3), f = r / w;
        fs[k] = f; ws[k] = w;
        if (f < f1) { f2 = f1; w2 = w1; f1 = f; w1 = w; r1 = r; its1 = its; n1 = k; } else if (f < f2) { f2 = f; w2 = w; }
        all += exp(-f * 3.5);
    }
    // (How far to the cell's edge, metres: where the nearest and the next come to the same, by how fast their difference
    // grows, which is the sum of their weights' inverses. The next taken softly, from all the others together, two
    // sevenths of a cell wide: where two or three are nearly as near, at the cell's corners, it is nearer, and the corners
    // are rounded off. Sharp, every cloudlet was a shard of broken ice.)
    float f2s = min(f2, -log(max(all - exp(-f1 * 3.5), 1e-30)) / 3.5);
    float edge = max(f2s - f1, 0.0) / (1.0 / w1 + 1.0 / w2) * C;
    // (The low layer's: the nearest of the lines where one of the others comes as near, each by how fast the two distances
    // part, taken softly over `corner` of a cell, so that a corner, where two lines are as near, is rounded and an edge is
    // where it is. The second-nearest taken softly from all eight others drew every edge in, not the corners alone, by a
    // sixth of a cell or so: with the crack on top, a stratocumulus sky the weather calls nine tenths covered was a third
    // covered from straight below or above, scattered floes, and from the ground a sky of separate puffs
    // (research/rendering/experiments/clouds-from-above.md, the pads).)
    for (int once = corner > 0.0 ? 0 : 1; once < 1; once++)
    {
        float lines = 0.0;
        for (int k = 0; k < 9; k++)
            if (k != n1) lines += exp(-((fs[k] - f1) / (1.0 / w1 + 1.0 / ws[k])) / corner);
        edge = max(-corner * log(max(lines, 1e-30)), 0.0) * C;
    }
    float turn = its1.r + when, born = floor(turn), life = (turn - born) * 2.0 - 1.0;
    float rank = fract(its1.g + born * (633.0 / 1024.0));
    liftOut = 0.0;
    // (an empty cell: the nearest cloud is across its edge and the crack, as high as a pillow is on the whole)
    if (rank >= share) return vec4(-(edge + crack), 0.0, 0.8 * used, life);
    float going = clamp((life - 0.7) / 0.3, 0.0, 1.0);
    going *= going * (3.0 - 2.0 * going);
    float strong = clamp((life + 1.0) * 4.0, 0.0, 1.0) * (1.0 - going);
    // (How far inside the pillow's edge, over the ground: to the crack (each cell's crack its own, from two thirds of the
    // day's to half as wide again), the pillow drawn in towards its middle while it grows and goes.)
    float across = r1 * C + edge;
    float e = edge - crack * (0.65 + 0.85 * fract(its1.a * 7.1 + its1.g * 2.9)) - (1.0 - mix(0.35, 1.0, strong)) * across;
    // (Its top: thickest over the middle of the cell, where the air rises, nearly as thick as the layer and each its own,
    // from three fifths of it to all of it (cut flat by the layer's top, as the thicker were, they were white floes on
    // the sea from above), and a little more than half that at its edge (padsProfile); its base level out to the crack.
    // Seen from below, darker where it is thick, as photographs of the cells show them. A dome on a rounded base, as a
    // heap is, left half of every cell open sky; thinned out to nothing at the crack, each cloudlet had a thin ring
    // round it, parted from it by a band of sky; a cushion, as thick at its edge as in its middle, was moulded plastic.)
    float H = used * (0.6 + 0.4 * fract(its1.g * 5.13 + its1.r * 1.71)) * mix(0.5, 1.0, strong);
    liftOut = 30.0 * its1.a + going * 0.5 * H;
    return vec4(e, across, H, life);
}

// (A cell's pillow, from its column (padsColumn), at `above` metres over the layer's base: how many metres inside it.)
// (Outside its outline as inside it, no higher than the pillow is there. Outside, as until 2026-10-18, it was the distance to
// the outline alone, the same at every height: the soft edge round every pillow stood up as a sleeve of thin cloud to as high
// as the layer may be asked about (150 m over what it uses). Over decks of stratocumulus and altocumulus that was thin whiskers
// round the cloudlets; in a deep middle layer between pads and sheet (the weather's 0.18, using 1.6 km of a 3 km layer), a sky
// of pillars hanging from the cloudlets, which is why its altocumulus is drawn as pads alone (CloudsWeather.Kind).)
float padsProfile(vec4 col, float above)
{
    return min(col.x, col.z * mix(0.55, 1.0, sqrt(clamp(col.x / max(0.4 * col.y, 1.0), 0.0, 1.0))) - above);
}

float padsCells(vec2 uv, vec2 tilt, vec2 going2, float C, int per, float when, float above, float share, float crack, float used, float corner, out float size, out float age, out float lift, out float leaves)
{
    // (The ground under the cells bent by up to a fifth of a cell each way, over a cell and a half and less: their edges
    // then wander as the edges of real cells do. Straight, and even as arcs, a deck of them was a quilt of tiles. The
    // large noise's coarser cells for the one way and its finer for the other, which are not alike.)
    vec4 bend = textureLod(_CloudShape, vec3(uv * 0.17, 0.37), 1.5);
    uv += (bend.gb - 0.48) * 1.3;
    float laneAt = 0.0, laneDeep = 1.0;
    padsFray = 0.0;
    // (in rows: the cells drawn out along the rows by two fifths, the rows bent a little over some cells and their lanes deeper
    // here and shallower there along them, so that the lines part and join as real ones do; the line's
    // way along the ground the same, for how far it may stride)
    for (int once = padsRows > 0.01 ? 0 : 1; once < 1; once++)
    {
        vec2 b = padsRowsWay, a = vec2(-b.y, b.x);
        float ua = dot(uv, a), ub = dot(uv, b);
        ub += (textureLod(_CloudShape, vec3(ua * 0.06, ub * 0.02, 0.61), 1.0).r - 0.5) * 0.9 * padsRows;
        laneAt = ub;
        laneDeep = 0.45 + 0.55 * smoothstep(0.3, 0.7, textureLod(_CloudShape, vec3(ua * 0.12, ub * 0.04, 0.27), 1.0).g);
        float s = mix(1.0, 0.72, padsRows);
        uv = a * (ua * s) + b * ub;
        going2 = a * (dot(going2, a) * s) + b * dot(going2, b);
    }
    // (how far along the line being walked this square of the grid goes on: the points looked at are its own and its
    // eight neighbours', and the two nearest of all are among those, so the walk may stride to where the line leaves it;
    // a quarter less, for the bend)
    vec2 in_ = uv - floor(uv);
    vec2 room = mix(in_, 1.0 - in_, step(0.0, going2));
    vec2 until = room / max(abs(going2), vec2(1e-7));
    leaves = 0.75 * min(until.x, until.y);
    vec4 col = padsColumn(uv, tilt, C, per, when, share, crack, used, corner, lift);
    // (The lanes: towards the lines' troughs the cloudlets thin to a fifth and draw back a little, and their margins soften (padsFray),
    // so that a lane is sky between wisps and not a cut; the cloudlets that are there grow by as much as the lanes took (only those
    // grow: the clear patches of the day's pattern stay clear). Drawn back to an edge and joined along the crests, as first tried, the
    // rows were hard strips: "too hard edged, they don't look real" (the owner, 2026-10-19).)
    for (int once = padsRows > 0.01 ? 0 : 1; once < 1; once++)
    {
        float wave = 0.5 + 0.5 * cos(6.2832 * laneAt / 1.15);
        float lane = smoothstep(0.15, 1.0, 1.0 - wave) * laneDeep;
        col.x += padsRows * ((col.y > 0.0 ? 0.045 * C : 0.0) - lane * 0.28 * C);
        col.z *= mix(1.0, mix(1.0, 0.2, lane), padsRows);
        padsFray = padsRows * lane;
    }
    size = col.y > 0.0 ? max(col.y, 0.25 * C) : 0.0; age = col.w;
    return padsProfile(col, above);
}

// The same heaps, for the map (go 5): for a place over the ground, the heap that has it or stands nearest, with the heaps
// beside it taken in where two meet, so that what is read from the map between them is a join and not a seam. Six points
// of the lattice are looked at here (the triangle's three and the three across its edges), where the walk could afford
// three: it is done once for each dot of the map, not at every asking. Given: x, how far the place is from the heap's
// upright (m); y, its half-width (m; nothing: none near, and x is then how far off the nearest could be); z, its height (m);
// w, how far through its life. `raisedOut`: how far its own base stands above the layer's.
vec4 heapsMap(vec2 uv, vec2 tilt, float C, int per, float when, float share, float widest, float evenly, float join, float tall, out float raisedOut)
{
    float n = uv.y * (10.0 / 9.0), m = uv.x - 0.5 * n;
    vec2 corner = floor(vec2(m, n));
    bool upper = (m - corner.x) + (n - corner.y) > 1.0;
    vec2 round_ = vec2(23.0, 230.0) * float(per);
    float gapBest = 0.5 * C, sum = 0.0, sumR = 0.0, sumH = 0.0, sumLife = 0.0, sumRaised = 0.0, gapSoft = 1e5;
    float gaps[6]; vec4 found[6];
    for (int k = 0; k < 6; k++)
    {
        vec2 cell = corner + (k == 0 ? (upper ? vec2(1.0, 1.0) : vec2(0.0, 0.0)) : k == 1 ? vec2(1.0, 0.0) : k == 2 ? vec2(0.0, 1.0)
                              : k == 3 ? (upper ? vec2(0.0, 0.0) : vec2(1.0, 1.0)) : k == 4 ? (upper ? vec2(2.0, 0.0) : vec2(1.0, -1.0)) : (upper ? vec2(0.0, 2.0) : vec2(-1.0, 1.0)));
        vec4 its = texelFetch(_CloudCells, ivec2(cell - round_ * floor(cell / round_)) & 255, 0);
        float turn = its.r + when;
        float born = floor(turn);
        float life = (turn - born) * 2.0 - 1.0;
        float rank = fract(its.g + born * (633.0 / 1024.0));
        gaps[k] = 1e5; found[k] = vec4(0.0);
        if (rank >= share) continue;
        float going = clamp((life - 0.4) / 0.6, 0.0, 1.0);
        going *= going * (3.0 - 2.0 * going);
        float z = 1.0 - rank / share;
        float full = widest * mix(sqrt(z), z, evenly), grown = clamp((life + 1.0) * 2.0, 0.0, 1.0), R = full * grown * (1.0 - 0.45 * going);
        if (R < 15.0 || going > 0.97) continue;
        float own = tall * (0.75 + 0.55 * fract(its.g * 5.13 + its.r * 1.71));
        // (no heap round: each an oval of its own, up to one and a third times as long as it is wide, lying its own way;
        // out of place by up to a quarter of a cell each way)
        vec2 astray = its.ba - 0.5;
        vec2 d = uv - (vec2(cell.x + 0.5 * cell.y, 0.9 * cell.y) + astray * 0.5);
        float oval = 1.0 + 0.16 * fract(its.b * 5.1 + its.a * 2.3);
        vec2 way = normalize(astray + vec2(0.003, 0.001));
        d = way * (dot(d, way) / oval) + vec2(-way.y, way.x) * (dot(d, vec2(-way.y, way.x)) * oval);
        float t = dot(d, tilt);
        float out_ = sqrt(dot(d, d) + t * t) * C;
        gaps[k] = out_ - R;
        found[k] = vec4(R, 2.0 * full * grown * own, life, 40.0 * its.a + going * 0.9 * 2.0 * full * grown * own);
        gapBest = min(gapBest, gaps[k]);
        // (the gap to the nearest edge, with a rounded join where two heaps' edges are near one another)
        gapSoft = -larger(-gapSoft, -gaps[k], join);
    }
    // (whose numbers: the nearest heap's, and those of any other as near within the join's width, by how near. But how
    // high the base is cut: of those, the lower counts for far more. A heap that is going, standing in a larger one that
    // is not, otherwise cut a hollow up into it as high as its own risen base: caves in the flanks of towers.)
    float sumLow = 0.0;
    for (int k = 0; k < 6; k++)
    {
        float w = max(1.0 - (gaps[k] - gapBest) / max(join, 1.0), 0.0);
        w *= w;
        float low = w * exp(-found[k].w / 60.0);
        sum += w; sumR += w * found[k].x; sumH += w * found[k].y; sumLife += w * found[k].z; sumRaised += low * found[k].w; sumLow += low;
    }
    raisedOut = 0.0;
    if (sum <= 0.0 || sumR <= 0.0) return vec4(0.5 * C, 0.0, 0.0, 0.0);
    float R = sumR / sum;
    raisedOut = sumRaised / max(sumLow, 1e-20);
    return vec4(max(R + min(gapSoft, 0.5 * C), 0.0), R, sumH / sum, sumLife / sum);
}

// What the two layers' shapes go by, worked out once for each step of the walk (formStep) and used by every asking of that
// step: the step's own, the light's from it, the search for a cloud's edge. [0] the low layer, [1] the middle one.
// (Until the evening of 2026-10-12 all of it was worked out afresh at every asking: the new shapes then cost one and a half
// times to twice what the old had, and by count they asked no more often. It was the arithmetic.)
vec4 formKind[2];    // x: how much of it is separate heaps; y: how much one even sheet; z: how much of the layer's depth it uses (m); w: how far a heap leans downwind for each metre up
vec4 formSize[2];    // x: how tall a heap for its width; y, z: the lattice's two sizes (cells to a length of the noise); w: how much of the layer's cloud is the second size's
vec4 formHeap[2];    // x: how many of the lattice's points have a heap; y: the largest's half-width (cells); z: 0 if most are near the largest, 1 if of every size alike; w: the join where two meet (cells)
vec4 formBank[2];    // x: how much of a bank there is (0: none); y: the level of the banks' pattern above which is bank; z: how much the heaps that reach the lid spread under it; w: how much of a sheet there is
float formDeck[2];   // how much of it is a low deck of stratus or stratocumulus drawn after Blackrack's stratus (deckNew: its column, its curve's top, its billows' spread there, its light's reckoning of its top); 0 for the middle sky, fog, rain's sheet and heaps
vec4 formMore[2];    // x: the cover the heaps go by; y: the level of the smooth noise above which is sheet; z: the layer's depth (m); w: its base (m above the radius)
vec3 formUp;         // the upright at the step, in the planet's axes
vec3 formWay = vec3(0.0);   // which way the line now being walked goes (a unit, the planet's axes): the line of sight, or towards the sun for the light's steps; nothing: not told
float formFace, formOther, formMine;   // which face of the cube the lattice lies on here (0, 1, 2: by which of the three directions is most upright), the next nearest, and this one's share
float formCoverHi[2];       // (formStep) the most cover each layer can have within a stride of the step, for the walk's strides (clearFor): the streets at their crest, the gatherings at the larger of the two readings the step lies between, pads as in a raft
float formGatherHi = -1.0;  // (the walk) the larger of the gatherings' two readings the step lies between (below nought: not told)
float formCoverLo[2];       // (formStep) the least cover each layer can have within a stride of the step, for the towers' bounds in the walk's strides (clearFor): the streets at their trough, the gatherings at the smaller of the two readings, pads as outside a raft
float formGatherLo = 1e9;   // (the walk) the smaller of the gatherings' two readings the step lies between (1e9: not told)
float formPer;       // a pixel's width for each metre of distance
float formAway;      // how far into the stretch where the lattice's heaps give way with distance the step is (formStep): 0 not at all, 1 wholly
float formChoice = -1.0;    // how wide a pixel of the walk's picture is at the place asked about (m), by which the near shapes or the far field draw it (formOne), for everything asked from the walk's step there and for the shadow's map; nothing: each asking's own
float formFade = 1.0;       // (formOne) how much of the near shapes there is: 1 all; less, each shrunk towards nothing as the far field takes over
float farGrow = 1.0;        // (formOne) how much of the far field there is: 1 all; less, only its strongest parts, as it takes over
float formClimbs = 2.0;     // how fast the line of sight climbs at the step now being walked (the sine of its angle above level there); 2: not told (all but the walk's own reading of the step)
float formAhead = 1e9;      // (the walk) how far along the line the weather read for the step now being walked holds (m): to the next reading, past which the layers' kind, columns and cover, the gatherings and the cube's faces are not known (clearFor goes no further)

// How far along the line being walked a level is that lies `d` metres below the place (`down`) or above it, the place
// being h above the planet's radius: the planet is round, so a line that goes down at first comes up again further on.
// Not told which way the line goes: `d` itself, the distance straight up or down, which is never too far.
// (What it is for: a place over the top of everything a layer can hold, or under its base, is in clear air, and the walk
// may stride as far as that air goes along its own line. Told only how far it is straight up or down, as until the
// evening of 2026-10-12, a line that came down on a layer at three degrees crept up on it: each stride a part of what
// height was left, which along such a line is a nineteenth of the way. Counted: half as many steps again as the old
// shapes took over a fair sky seen from just above it.)
float alongTo(float d, float h, bool down)
{
    if (formClimbs > 1.5) return d;
    float c = formClimbs, Rh = _CloudCam.w + h;
    float two = down ? 2.0 * d - d * d / Rh : 2.0 * d + d * d / Rh;
    if (!down) return two / (c + sqrt(c * c + two / Rh));
    float left = c * c - two / Rh;
    return c < 0.0 && left >= 0.0 ? two / (-c + sqrt(left)) : 1e6;
}

// (a place's two numbers across a face, and the upright's, with the third after them)
vec3 faceOf(vec3 v, float face) { return face < 0.5 ? v.yzx : face < 1.5 ? v.zxy : v; }
// (and back again)
vec3 unfaceOf(vec3 v, float face) { return face < 0.5 ? v.zxy : face < 1.5 ? v.yzx : v; }

// The level of the smooth noise above which a share `cover` of the sky lies. (It is spread like a bell round a half,
// 0.083 wide: measured, see the lab.)
float formLevel(float cover)
{
    float x = clamp(cover, 0.003, 0.997);
    return 0.5 - 0.083 * log(x / (1.0 - x)) * 0.6;
}

vec4 formRaw[2];     // x: the cover as the weather has it; y: its logarithm to the base two; z: 1 if there is any of this layer

// (Decks' tops after Blackrack's stratus (2026-11-07, TODO 16; research/clouds-r5/deck-top-plan-2026-11-06.md): _CloudSpec.w's
// 256 sets the shape back (formDeck, rowAt's stratus, columnOf), 512 the light (the walk's sun steps in a deck); 1024 the billows
// twice as fine; 2048 a sheet's top lit at its lumps (2026-11-08: see the second form's light); 4096 a sheet's billows twice as
// deep over its upper part, 8192 a deep deck's billows at the heaps' size (2026-11-08: see fieldAt).)
float deckNew() { return mod(floor(_CloudSpec.w / 256.0), 2.0) > 0.5 ? 0.0 : 1.0; }
float deckLight() { return mod(floor(_CloudSpec.w / 512.0), 2.0) > 0.5 ? 0.0 : 1.0; }
float deckFine() { return mod(floor(_CloudSpec.w / 1024.0), 2.0) > 0.5 ? 0.0 : 1.0; }
float deckTop() { return mod(floor(_CloudSpec.w / 2048.0), 2.0) > 0.5 ? 0.0 : 1.0; }
float deckDeep() { return mod(floor(_CloudSpec.w / 4096.0), 2.0) > 0.5 ? 0.0 : 1.0; }
float deckWide() { return mod(floor(_CloudSpec.w / 8192.0), 2.0) > 0.5 ? 0.0 : 1.0; }

// Once for each reading of the weather along a line (every 1200 m): what both layers' shapes go by that the weather alone
// decides. p: the place (from the planet's centre, the scene's axes).
void formWeather(vec3 p, vec4 wa, vec4 wb, vec4 wc)
{
    float N = _CloudNoise.w;
    vec3 pf = vec3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
    formUp = pf / max(length(pf), 1.0);
    // (as fine as what is kept from frame to frame, where that is finer than the walk's own picture)
    // (where what is kept is finer than the walk's picture (z), how much finer a kept dot is: the walk's w, 0.5 at half size and
    // 0.25 at a quarter; nought, as before 2026-10-18, is 0.5. The shadow's map gives its own w, for go 4.)
    formPer = 2.0 * length(_CloudRight.xyz) / max(_CloudHigh.w, 1.0) * (_CloudJitter.z > 0.5 ? (_CloudJitter.w > 0.0 ? _CloudJitter.w : 0.5) : 1.0);
    // (The face of the cube: the two of the three directions of the noise's space that lie most nearly along the ground
    // here. Near where two faces meet, within a degree or so, both lattices are there, each with a share of the heaps
    // that falls to a half at the meeting: across it the two change places, and no heap is cut.)
    vec3 au = abs(formUp);
    float most = au.z, next = max(au.x, au.y);
    formFace = 2.0; formOther = au.x > au.y ? 0.0 : 1.0;
    if (au.x >= au.y && au.x >= au.z) { formFace = 0.0; most = au.x; next = max(au.y, au.z); formOther = au.z > au.y ? 2.0 : 1.0; }
    else if (au.y >= au.z) { formFace = 1.0; most = au.y; next = max(au.x, au.z); formOther = au.z > au.x ? 2.0 : 0.0; }
    formMine = 0.5 + 0.5 * smoothstep(0.0, 0.025, most - next);
    for (int L = 0; L < 2; L++)
    {
        float cover = L == 0 ? wa.x : wb.a, kind = L == 0 ? wa.y : wc.x, base = L == 0 ? wa.z : wc.y, top = L == 0 ? wa.w : wc.z;
        top = max(top, base + 50.0);
        cover = clamp(cover * _CloudLook.z, 0.0, 1.0);
        formRaw[L] = vec4(cover, log2(max(cover, 1e-4)), cover > (L == 0 ? 0.001 : 0.004) ? 1.0 : 0.0, smoothstep(0.5, 0.68, kind));
        float deep = top - base;
        // ---- what kind: how much of it is separate heaps, and how much one even sheet; the rest is pads
        float heapy = smoothstep(0.34, 0.46, kind), sheet = 1.0 - smoothstep(0.12, 0.24, kind);
        // (a lumpy layer is a few hundred metres thick whatever the depth of air it is given; a sheet fills what it is given)
        float used = mix(mix(min(deep, L == 1 ? 190.0 : 560.0), deep, sheet), deep, heapy);
        float lean = mix(0.22, 0.12, smoothstep(1500.0, 4000.0, deep));
        formKind[L] = vec4(heapy, sheet, used, lean);
        // (a low deck of stratus or stratocumulus, from kind 0.05 up to where heaps begin; not fog nor rain's sheet below it)
        formDeck[L] = L == 0 ? deckNew() * smoothstep(0.05, 0.08, kind) * (1.0 - heapy) : 0.0;
        // (how tall a heap for its width (its height over its whole width): wider than tall, nearly twice as wide in fair
        // weather's shallow layer and still half as wide again in a deep one; taller than wide only where they tower. At
        // as tall as wide in a deep layer, as it was until 2026-10-15, a mostly cloudy sky was a field of pillars.)
        float tall = mix(0.45, 0.7, smoothstep(350.0, 1600.0, deep)) * mix(1.0, 2.4, smoothstep(0.5, 0.68, kind));
        // ---- the lattice's size. For heaps: such that the widest just reach the layer's top. For pads: a kilometre and a
        // quarter to a cell low down, under half of that in the middle sky, where they are small and many. It comes in set
        // sizes, each half as large again as the last (3, 4, 6, 9, 14, 20 or 30 cells to a length of the noise: a lattice
        // whose size slid with the layer's depth would slide its heaps over the ground wherever the depth changed); where
        // the size asked for is in the upper third of the way from one to the next, both are there, the one's heaps going
        // as the other's come.
        // (The middle sky's pads on a set size of their own, 20 to a length of the noise (450 m): at 480 m, as they were, the
        // size asked for fell between two, and both were always there, each with a share of the pads. Its sheets as they
        // were: the two sizes' readings of the wide noise taken together are what keep a sheet's holes soft, and with one,
        // altostratus seen aslant was a field of broken crystal.)
        float cells = clamp(mix(N / (L == 1 ? mix(480.0, 430.0, step(0.5, (1.0 - heapy) * (1.0 - sheet))) : 1250.0), N * tall / (1.05 * deep), heapy), 3.0, 30.0);
        float rung = log(cells / 3.0) / log(1.4678);
        float low = min(floor(rung), 5.0);
        formSize[L] = vec4(tall, floor(3.0 * pow(1.4678, low) + 0.5), floor(3.0 * pow(1.4678, low + 1.0) + 0.5), smoothstep(0.67, 1.0, rung - low));
        // (how much of a bank there is, how much the heaps that reach the layer's top spread out under it (on the usual
        // day, in a shallow layer; not where they tower, whose tops are far above); the rest by the step)
        // (Where the heaps tower there is a bank too, a little less of one: towers stand up out of a deck of lesser
        // heaps that has one level base, they do not stand each by itself on a base of its own with clear sky between,
        // as they were drawn until the evening of 2026-10-12: skittles.)
        formBank[L] = vec4(heapy * _CloudHabit.x * mix(1.0, 0.8, smoothstep(1500.0, 2600.0, deep)), 0.5, heapy * _CloudHabit.x * (1.0 - smoothstep(1400.0, 2600.0, deep)), sheet);
        formMore[L] = vec4(cover, 0.5, deep, base);
    }
}

// (a straight ramp from nothing at a to all at b: where a smooth one is not needed, and this is worked out at every step)
float ramp(float a, float b, float x) { return clamp((x - a) / (b - a), 0.0, 1.0); }

// Once a step: what the gatherings and the streets make of each layer's cover there, and what goes by that.
// How far the camera is above all the clouds, for what is drawn otherwise from there (formStep, formOne, the heaps' map, the
// lumps' reach, the walk's own shade on the ground, the shadow's map): 0 within 100 m over the highest a top can be, 1 from
// 1100 m over it. Taken at a step 500 m over it, as until 2026-10-18, it changed them all at once there: climbing or coming down
// through that height, the heaps at middle distance went or came and the gaps between them opened, in one frame.
float aboveness() { return smoothstep(_CloudShell.y + 100.0, _CloudShell.y + 1100.0, _CloudHigh.x); }

void formStep(vec3 q, vec4 wm, float t)
{
    // (How far off. Past 100 km the lattice's heaps are fewer, the smallest gone first, and past 130 km there are none: the
    // banks, which are what is seen of a sky from afar, stay, and are given the heaps' share of the cover; a lumpy layer far
    // off is its sheet. Until 2026-10-18 that was from 15 to 26 km, because a line of sight running level through the layer
    // for 90 km worked every one of its 88 steps' heaps out in full (in the game, 58 frames a second among fair-weather heaps
    // became 38); since the heaps are made beforehand they cost little there (in the lab 1-8 % of the walk in six views, the
    // same noise in flight), and a sky whose heaps end 26 km off and lie flat beyond reads as small: from the ground and from
    // cloud height a real field of heaps goes on to the horizon, smaller and paler (research/rendering/notes/cloud-scale.md).)
    float away = smoothstep(100000.0, 130000.0, t);
    formAway = away;
    // (Seen from above all the clouds, from high in the air or from orbit, `t` is how far the line has run inside the
    // shell, not from the camera: a line from orbit crosses the layer steeply, in a few kilometres and a few steps, and the
    // heaps it meets 250 km off are as cheap as near ones and a few pixels wide. By the distance from the camera, as it
    // was, a cumulus sky had no heaps at all from orbit, and only the banks' patches were left of it: "the clouds from
    // space look kind of crap" (the user, 2026-10-13). Where such a line does run far along the layer (towards the
    // planet's rim), the heaps there go as before, and from above, their layer is a sheet there, holed as much as it is
    // covered: heaps far off along a layer seen from above are seen one behind another, run together.)
    float fromAbove = aboveness();
    // (What gatherings and streets do to the cover. As a power, not a factor: a gathering thickens the cover towards
    // all of the sky but never to all of it. The low heaps take both; the banks, and the middle sky, the gatherings alone.)
    float streets = max(streetsAt(q, wm), 0.2);
    for (int L = 0; L < 2; L++)
    {
        if (formRaw[L].z < 0.5) continue;
        float raw = formRaw[L].x, heapy = formKind[L].x, sheet = formKind[L].y;
        float gathered = max(wm.x / streets, 0.05), both = max(L == 0 ? wm.x : wm.x / streets, 0.05);
        float coverBanks = mix(exp2(formRaw[L].y / gathered), raw, raw * raw);
        float cover = mix(exp2(formRaw[L].y / both), raw, raw * raw);
        // ---- rafts (2026-10-17). Pads (stratocumulus low, altocumulus in the middle sky) are not strewn evenly over the
        // whole sky: photographs of them from the ground (the mackerel skies among Wikimedia Commons' altocumulus) show the
        // cloudlets packed into rafts kilometres to tens of kilometres across, parted by narrow cracks, with clear sky
        // between the rafts and only a stray tuft in it. Strewn evenly, as they were, a mostly cloudy sky was hundreds of
        // separate round puffs alike: cotton balls. So: a pattern over the ground says where the rafts are (its strongest
        // part, as much of the sky as the cover asks, less the cracks); in a raft the pads' cover is three quarters (all of
        // it where the whole sky is covered), outside one a thirtieth of the sky's, and across a raft's edge, over a
        // kilometre or so, from the one to the other: a ragged edge of wider cracks and smaller cloudlets. (The pattern is
        // read as the banks' is (see formOneNear), at its own place for each layer, and finer in the middle sky.)
        float rafty = (1.0 - heapy) * (1.0 - sheet);
        for (int once = rafty > 0.01 ? 0 : 1; once < 1; once++)
        {
            float k = L == 1 ? 0.41 : 0.27;
            vec4 rp = textureLod(_CloudShape, q.zxy * k + (L == 1 ? vec3(0.13, 0.77, 0.41) : vec3(0.83, 0.21, 0.57)), 2.0 * _CloudHaze.x);
            float strength = clamp((rp.r - rp.g) / max(1.0 - rp.g, 0.02), 0.0, 1.0);
            float most_ = clamp(cover / 0.85, 0.003, 0.997);
            // (metres inside the raft's edge: the pattern is about 0.55 steeper per length of the noise read at 0.27)
            float within = (strength - (0.5 - 0.045 * log(most_ / (1.0 - most_)))) * (_CloudNoise.w * 0.27 / (0.55 * k));
            cover = mix(cover, mix(0.03 * cover, mix(0.74, 0.95, ramp(0.6, 1.0, raw)), smoothstep(-600.0, 600.0, within)), rafty);
        }
        // (how many of the points have a heap, how large the largest, how alike in size: a sky of heaps a tenth covered is a
        // few small ones, all different; half covered, most points have one and they are large and crowd together. Never
        // all of the points: a sky with a heap at every point of the lattice shows the lattice, heaps in ranks one behind
        // another; with one in five of them missing, by chance, it does not. Pads are at nearly all of them and much alike.)
        float share = mix(min(cover * 1.7, 0.96), min(cover * 3.2, 0.8), heapy);
        float widest = mix(0.40, mix(0.32, 0.40, ramp(0.1, 0.45, cover)), heapy);
        float evenly = mix(0.5, ramp(0.35, 1.0, mix(1.25, 0.35, ramp(0.1, 0.5, cover))), heapy);
        // (how wide the join where two meet: wider the more of the sky is covered, when neighbours run together into one mass)
        float join = mix(mix(0.1, 0.34, ramp(0.45, 0.9, cover)), mix(0.06, 0.2, ramp(0.3, 0.6, cover)), heapy);
        // ---- the banks (see formOne): where their pattern's level stands. The lattice's heaps the same in a bank and out of
        // it, few and small: in it they are its turrets, out of it the small heaps that stand alone. And the banks placed
        // by the cover without the streets in it: the streets are rows of more and of less cover three kilometres apart,
        // for the heaps to stand in; a bank's edge moves four kilometres for a tenth more cover, and with the streets in
        // what places it, the edge of every bank was drawn out along each street: a comb.
        if (formBank[L].x > 0.01)
        {
            share = min(share, min(cover * 1.1, 0.8)); widest = min(widest, mix(0.3, 0.38, ramp(0.1, 0.45, cover)));
            float most_ = clamp(coverBanks * mix(0.85, 1.0, away), 0.003, 0.997);
            formBank[L].y = 0.5 - 0.045 * log(most_ / (1.0 - most_));
        }
        formHeap[L] = vec4(share * (1.0 - away), widest, evenly, join);
        // (And the most the cover can be within a stride of here, for the walk's strides (clearFor), which take a place's cover for
        // all of the stride: the streets at their crest, the gatherings at the larger of the two readings the step lies between,
        // and pads as inside a raft. With the place's own cover, a stride from a street's trough or from outside a raft passed
        // over the cloud on its crest or at the raft's edge (the review of the evening of 2026-10-27: 0.04-1.2 % of clear steps
        // across streets, up to 7.6 % at a raft's steep edge, mostly thin cloud). Not past the next reading, which is not known: the
        // strides go no further (formAhead).)
        float gatherHi = max(formGatherHi, wm.x / streets);
        float bothHi = max(L == 0 ? gatherHi * (1.0 + 0.55 * max(_CloudStreets.w, 0.0)) : gatherHi, 0.05);
        float coverHi = mix(exp2(formRaw[L].y / bothHi), raw, raw * raw);
        if (rafty > 0.01) coverHi = mix(coverHi, max(0.03 * coverHi, mix(0.74, 0.95, ramp(0.6, 1.0, raw))), rafty);
        formCoverHi[L] = max(cover, coverHi);
        // (and the least, as the most is had: a tower stands only where the cover is under 0.8 (towerOf), and a stride from a
        // street's crest could reach one in its trough (the review of 2026-11-06))
        float gatherLo = min(min(formGatherLo, wm.x), wm.x / streets);
        float bothLo = max(L == 0 ? gatherLo * (1.0 - 0.55 * max(_CloudStreets.w, 0.0)) : gatherLo / 1.55, 0.05);
        float coverLo = mix(exp2(formRaw[L].y / bothLo), raw, raw * raw);
        if (rafty > 0.01) coverLo = mix(coverLo, 0.03 * coverLo, rafty);
        formCoverLo[L] = min(cover, coverLo);
        // (how much of a sheet there is: all of one in a sheet, and under a lumpy layer that covers most of the sky a
        // thinner one that joins the pads; and the level of the smooth noise above which it is)
        // (none under pads, which are cells (padsCells) and cover the sky themselves, with cracks of open sky between them
        // that close as it fills: under a sheet joining them, a deck of them was a crumpled white sheet)
        float fill = max(max(sheet, (1.0 - heapy) * (1.0 - rafty) * ramp(0.72, 0.97, cover)), (1.0 - heapy * (1.0 - fromAbove)) * away);
        formBank[L].w = fill;
        formMore[L].x = cover;
        if (fill > 0.001) formMore[L].y = formLevel(cover);
    }
}

// One layer's cloud at a place, with the lattice at one size (`perF` cells to a length of the noise) and `weight` of the
// layer's cloud given to that size (see formStep: where the layer's depth asks for a size between two, both are there, each
// with its share). Returns how much cloud (seeking: 1 if there may be any within a stride and a half); given out: how
// near any of it can be (as heapsNear), how far inside its surface the place is, and how much of the sky is seen.
//
// The kinds. Heaps (a kind of a half and more): separate domes, as above. A lumpy layer (stratocumulus, about a quarter;
// the dappled middle sky, altocumulus, the same but small): the same domes pressed flat under a lid a few hundred metres
// above their base, wide for their depth, at nearly every point of the lattice and run together where they touch: pads
// with cracks between, the cracks closing as the sky fills. A sheet (stratus and the rain sheet, a tenth and less; the veil
// of the middle sky): one slab from base to top, thinner here and thicker there, with holes only where the sky is not
// all covered, and soft everywhere.
// Where the banks gather (2026-10-19). The banks' own pattern has one size, a few kilometres, and at any cover its banks stood as
// evenly as a pattern of one size does: from orbit every sky was an even popcorn of banks, where the weather's map had clumped it
// (the owner: "The cloud coverage over the whole world is too even and evenly spaced out"). Real cloud comes in clumps within
// clumps, its sizes one power law from a tenth of a kilometre to some thousands (Wood and Field 2011; research/rendering/notes/
// cloud-climatology.md). So two larger patterns are laid over the banks' own, some 15 and 80 km across, each with more say than
// the last (0.8 and 1.4 times the banks' own), the three spread back to the banks' own spread together, so that as much of the
// sky is bank as before; and read the same where the near shapes and the far field read the banks, so that coming down through
// the air they stay where they were. (In the lab from 250 km straight down, heaps: the cover 43.7 % of the picture against
// 43.6 at 0.3, 68.9 against 69.5 at 0.6; the banks in fields with clear between, of several sizes.)
float bankGather(vec3 col)
{
    vec4 g = textureLod(_CloudShape, col.zxy * 0.11 + vec3(0.29, 0.67, 0.41), 2.0 * _CloudHaze.x);
    vec4 h = textureLod(_CloudShape, col.xzy * 0.6 + vec3(0.53, 0.19, 0.71), 2.0 * _CloudHaze.x);
    return 1.4 * (clamp((g.r - g.g) / max(1.0 - g.g, 0.02), 0.0, 1.0) - 0.5) + 0.8 * (clamp((h.r - h.g) / max(1.0 - h.g, 0.02), 0.0, 1.0) - 0.5);
}
// (the banks' pattern where it is read, with the larger two: how strongly a place is bank)
float bankStrength(vec4 broad, vec3 col) { return 0.5 + (clamp((broad.r - broad.g) / max(1.0 - broad.g, 0.02), 0.0, 1.0) - 0.5 + bankGather(col)) / 1.8974; }

// ---- The clouds from afar (2026-10-14 evening; the owner: "Clouds and ocean look horrible from far away").
// What a sky of clouds is from tens of kilometres up and from orbit, as the photographs from the space station and from
// airliners show it: fields. Heaps a kilometre or two across stand in clumps, the clumps run together into banks and rows
// tens of kilometres long with clear water between; a lumpy layer is a deck cracked into cells; a sheet is even, holed
// where the sky is not all covered. What was drawn instead: the lattice's heaps, each a few pixels wide, as scattered round
// dots and floes on the sea, and beyond 15 to 26 km of the layer the sheet that stood in for them, which from above was a
// frosted rim round a ring of dots (the step from one to the other at a set distance into the layer: a circle round the
// point below the camera).
// So from so far that a pixel is wider than a small heap, and from above all the clouds wherever the lattice's heaps give
// way with distance, the layer is drawn as the height its top stands at over each place: patterns at the sizes of the
// photographs' clumps and banks, bent so that no clump is round, the sky covered as much as the weather says. The walk
// lights it as it does the rest: tops bright to the sun, sides and cracks shaded, its shadow on the sea below. How many
// metres inside that surface a place is (outside: less than nothing, about as far as the nearest cloud may be).
float farThin;       // (farInside) how dense the far field's cloud is where it was last asked, against its densest
float farRim;        // (farInside) how far in from the field's outline over the ground the place is, m (outside, less than nothing)
float farTop;        // (farInside) how high the field's top stands over the layer's base there, m (for the light deep in a sheet: see formOne)

float farInside(int L, vec3 col, float above, float used, float heapy, float sheet, float cover, float pixel, bool fine)
{
    // (the large noise's cubes are 9 km to a repeat in 128 dots: read no finer than a pixel)
    float mip = max(log2(pixel / 70.0), 0.0) * _CloudHaze.x;
    // (bent over a kilometre or so)
    vec4 warp = textureLod(_CloudShape, col.zxy * 0.47 + vec3(0.23, 0.59, 0.11), max(mip - 1.0, 0.0));
    vec3 c = col + (warp.gba - 0.48) * 0.16;
    // (where the clumps gather: patterns 33 and 108 km across, strongest where both are. The large noise's first channel
    // lies between 0.74 and 0.92 nearly everywhere: spread over that, else a gathering is everywhere and the field from
    // orbit an even popcorn over the whole disc)
    // (read where the banks are read, unbent: one reading serves both; the bend is nothing at this size)
    vec4 broad = textureLod(_CloudShape, col.yzx * 0.27 + vec3(0.61, 0.17, 0.43), 2.0 * _CloudHaze.x);
    vec4 broader = textureLod(_CloudShape, c.xzy * 0.083 + vec3(0.37, 0.83, 0.29), 2.0 * _CloudHaze.x);
    float gather = smoothstep(0.77, 0.9, broad.r) * 0.6 + smoothstep(0.76, 0.88, broader.r) * 0.4;
    // (the clumps: the cubes' cells, 2.2, 1.1 and 0.56 km across, each bright at its middle; which size has most say
    // changes over ten kilometres or so, so that one stretch has large heaps and the next small ones; and each clump's
    // outline is bitten into by a pattern of its own size, that none is a round dot)
    // (each reading only where its kind is: what the far field costs is in how many it reads at every step)
    float clumps = 0.48, bankCells = 0.5, biteR = 0.5, deck = 0.5;
    for (int once = heapy > 0.001 ? 0 : 1; once < 1; once++)
    {
        vec4 s = textureLod(_CloudShape, c + vec3(0.31, 0.77, 0.53), mip);
        float big = smoothstep(0.35, 0.65, broad.g);
        clumps = mix(s.g * 0.3 + s.b * 0.42 + s.a * 0.28, s.g * 0.62 + s.b * 0.28 + s.a * 0.1, big);
        // (the light's own walk towards the sun asks only for the shape under the bites)
        for (int bites = fine ? 0 : 1; bites < 1; bites++)
        {
            vec4 bite = textureLod(_CloudShape, c.zxy * 2.3 + vec3(0.71, 0.29, 0.47), mip + 1.0);
            biteR = bite.r;
            clumps *= mix(0.72, 1.18, bite.r * 0.5 + bite.b * 0.5);
        }
        // (and a bank's cells, the clumps' own: its lumps from afar)
        bankCells = smoothstep(0.36, 0.62, s.g * 0.55 + s.b * 0.3 + s.a * 0.15);
    }
    for (int once = heapy < 0.999 ? 0 : 1; once < 1; once++)
    {
        // (a deck's cells: the cells' own pattern at 4.5 and 2.2 km, 1 in a cell's middle and nothing at its rim; their
        // numbers lie close round 0.48: spread out from there, else a deck's cells hardly differ and it is a plain sheet)
        vec4 deckRead = textureLod(_CloudShape, c * 0.5 + vec3(0.13, 0.41, 0.87), max(mip - 1.0, 0.0));
        deck = smoothstep(0.36, 0.62, deckRead.g * 0.7 + deckRead.b * 0.3);
        // (and from high up, the cells a deck of stratocumulus is made of seen from orbit: closed cells 10 to 40 km across, thick and
        // bright in their middles and thinner at their rims, the deck's own small cells within them (Wood and Hartmann 2006; research/
        // rendering/notes/cloud-climatology.md). The noise's cells read at a seventh of the size, about 16 and 8 km. Only where a pixel
        // is wider than 30 to 120 m: nearer, the patches of a deck seen from a few kilometres over it lost their lumpy edges to them.)
        vec4 mesoRead = textureLod(_CloudShape, c * 0.14 + vec3(0.57, 0.23, 0.61), max(mip - 2.8, 0.0));
        float mesoCells = smoothstep(0.36, 0.62, mesoRead.g * 0.75 + mesoRead.b * 0.25);
        deck = mix(deck, mesoCells * (0.55 + 0.45 * deck), 0.7 * smoothstep(30.0, 120.0, pixel));
    }
    // (The banks: the plateaus the usual sky's heaps run together into ("They're like huge plateaus": the owner,
    // 2026-10-12), placed exactly where the near shapes place them (formOne: the same pattern, at the same level for the
    // cover), so that coming down through the air they stay where they were. In a bank the clumps are its lumpy top and
    // it is cloud throughout; between banks only the strongest clumps stand, scattered.)
    vec4 B = formBank[L];
    float inBank = 0.0;
    for (int once = B.x > 0.01 ? 0 : 1; once < 1; once++)
    {
        float strength = bankStrength(broad, col);
        float within = (strength - B.y) * (_CloudNoise.w / 0.55) - (1.0 - B.x) * 3000.0;
        // (its edge ragged by the clumps and by a pattern of their size: drawn by the broad pattern alone, a bank seen
        // from above had the smooth outline of spilt paint)
        within += (clumps - 0.48) * 2500.0 + (biteR - 0.5) * 1200.0;
        inBank = smoothstep(-300.0, 300.0, within);
    }
    // (One number a place, from 0 to about 1, highest where the sky is fullest: for heaps, clumps gathered, and in a bank
    // its cells; for a lumpy layer the deck's cells; for a sheet the broad pattern alone. A bank or a deck seen from above
    // is not one white plain (as drawn at first: spilt paint, a featureless sheet) but cells, thick and bright in their
    // middles and thinner at their rims, cracked open between them where the sky is less than all covered: what a deck
    // of stratocumulus is from the air and from orbit.)
    // (Between the banks no more cloud than the near shapes have there, a few small heaps: a sky of 0.4's. With the whole sky's
    // cover there, as it was, a sky of 0.8 was nine tenths covered from afar where the near shapes covered two thirds, and from
    // above the gaps between the banks filled in round a ring about the point under the camera.)
    // (Where the sky is all but covered, all of it cloud. The far field's level for the cover, and the room it clears between the
    // banks for heaps by themselves, left holes in every kind at a cover of 1 (an eighth of the sky in heaps, a third under the
    // thin rims of a deck's cells, seen from 80 km in the lab), and the weather's overcast was a broken sky from orbit: under 0.8
    // as before; from there to 0.98 no room cleared, the level lowered, and the rims of the cells as thick as half their middles.)
    float fillAll = smoothstep(0.8, 0.98, cover);
    float lone = B.x * max(mix(0.78, 0.2, min(cover, 0.4)) - mix(0.78, 0.2, cover), 0.0) * (1.0 - fillAll);
    float heaps_ = mix(clumps * mix(0.6, 1.0, gather) - lone, 0.42 + 0.6 * bankCells, inBank);
    float lumpy = (0.3 + 0.75 * deck) * mix(0.85, 1.08, gather);
    // (a sheet's: the broad pattern spread over the field's span, and the finer bend's own first channel in it: from
    // mix(0.35, 0.75, broad.r), which spans only 0.61 to 0.68, where its holes fell was the weather's map's alone, its
    // dots taken between in straight lines, and fog seen from the air was holed with polygons)
    float even = clamp(0.5 + (broad.r - 0.745) * 2.2 + (warp.r - 0.745) * 0.9, 0.0, 1.0) * mix(0.85, 1.1, gather);
    float field = mix(mix(lumpy, even, sheet), heaps_, heapy);
    // (how dense the cloud is here against its middles: thinner at a cell's rim, which from above is greyer)
    farThin = mix(mix(mix(mix(0.12, 0.4, fillAll), 1.0, deck), mix(0.75, 1.0, broad.r), sheet), mix(1.0, mix(mix(0.2, 0.6, fillAll), 1.0, bankCells), inBank), heapy);
    // (the level above which is cloud, for so much of the sky to be covered: the field lies mostly between 0.25 and 0.75)
    // (and while it takes over from the near shapes, only its strongest parts: see formOne)
    // (Heaps' own: their field lies higher than that, and at the general level four fifths of the world from orbit was cloud at
    // a cover of 0.3 where the near shapes cover a quarter (the main chat's "uniform fleece", 2026-10-18). Matched to what the
    // near shapes cover seen straight down from 250 km, at 0.3 and 0.6 (experiments/heaps-from-orbit.md).)
    float level = mix(0.98, mix(mix(0.78, 0.2, clamp(cover, 0.0, 1.0)), mix(0.565, 0.452, clamp(cover, 0.0, 1.0)), heapy) - 0.3 * fillAll, farGrow);
    float t = (field - level) / max(1.0 - level, 0.05);
    farRim = t * (1.0 - level) * 600.0;
    // (the top: domes over heaps, a deck nearly level with its cells raised a little, a sheet level)
    float top = used * mix(mix(0.62 + 0.3 * sqrt(max(t, 0.0)), 0.75 + 0.2 * t, sheet), 0.3 + 0.7 * pow(clamp(t, 0.0, 1.0), 0.55), heapy);
    // (its full height only some way in from its edge, and lower towards it: a heap a dome as wide as it is tall, a deck and a
    // sheet with a shoulder some hundreds of metres wide. At full height right to the edge, as it was, every patch had upright
    // walls: from above floes, shards and a deck of ice, and from the ground the far heaps a palisade of white pillars along
    // the horizon.)
    top *= sqrt(clamp(t * (1.0 - level) * 600.0 / mix(mix(300.0, 400.0, sheet), max(350.0, 0.8 * top), heapy), 0.0, 1.0));
    // (and on it lumps of 250, 125 and 62 m, the small noise's cells, as far as a pixel can show them: from ten kilometres
    // above, a cauliflower and not a smooth floe; from orbit they are below a pixel and the top is smooth)
    float fineMip = log2(pixel / 15.6);
    for (int read = fineMip < 4.0 && fine ? 0 : 1; read < 1; read++)
    {
        vec3 d = textureLod(_CloudDetail, c * 36.0 + vec3(0.0, 0.0, _CloudDrift.w), max(fineMip - 2.0, 0.0) * _CloudHaze.x).rgb;
        top += dot(d - 0.48, vec3(1.0, 0.5, 0.25)) * mix(mix(90.0, 30.0, sheet), 160.0, heapy) * (1.0 - smoothstep(2.5, 4.0, fineMip)) * clamp(t * 4.0, 0.0, 1.0);
    }
    // (outside: no further off than the field's slope allows at its steepest, about a whole over 600 m; and the walk is
    // never told more than 300 m: told more, a line from above strode clean through a layer 400 m thick and its clouds)
    farTop = top;
    if (t <= 0.0) return max(t * (1.0 - level) * 600.0, -300.0) - max(above - top, 0.0);
    return min(above, top - above);
}

// How much of a heap's cloud there is z metres over its own base (stage 5, 2026-10-26): air rising from where it first condenses
// condenses more water with every metre it climbs, one to two and a half grams a cubic metre for each kilometre by how warm it is,
// and what a cubic metre of cloud takes from the light goes as its water to the two thirds, its drops growing and their number not
// (Rogers and Yau): a thin grey base and a dense top. From three tenths at the base, not from nothing (the air the heap draws in at
// its sides there, and the base's own ramp under it), to the whole from 300 m up, so that the top of every heap as tall as that, and
// its sunlit side, is as dense as it was. A smaller heap's top has had less room to condense its water, and is as thin as that: a
// heap 200 m tall at eight tenths at its top, one of 100 m at six tenths, and the sky shows through it more. Until then a heap was
// as dense forty metres over its base as at its top: its base a hard flat plate.
float condensed(float z) { return trialOff(2.0) ? 1.0 : mix(0.3, 1.0, pow(clamp(z / 300.0, 0.0, 1.0), 0.6667)); }

float formOneNear(int L, float perF, float weight, vec3 q, vec3 col, float above, vec4 wm, bool fine, float lod, bool seek, out float nearOut, out float insideOut, out float openOut, out float smoothOut);

float formOne(int L, float perF, float weight, vec3 q, vec3 col, float above, vec4 wm, bool fine, float lod, bool seek, out float nearOut, out float insideOut, out float openOut, out float smoothOut)
{
    // (how much of the far field there is here: all of it where a pixel is wider than 130 m, none under 45; and from above
    // all the clouds, wherever the lattice's heaps give way with distance)
    float pixel = 25000.0 * exp2(lod) * formPer;
    // (Which of the two draws a place goes by how wide a pixel of the walk's picture is there, the same for all that is asked
    // about it: the walk's step, its light's steps and far looks towards the sun, which read the noise a half and a whole size
    // coarser and with that chose the far field sooner (heaps drawn by the near shapes were then shaded by the far field's
    // clumps beside them: blue mottling over the banks), and the shadow's map, which was told of a picture of its own 512 dots
    // and chose the far field everywhere (the far field's shadows under heaps drawn by the near shapes).)
    float pixelChoice = formChoice > 0.0 ? formChoice : pixel;
    // (from above all the clouds sooner: their banks seen from over them, lumps damped flat under the layer's lid, were
    // smooth white floes on the sea from ten kilometres up)
    float fromAbove = aboveness();
    // (from ten kilometres above them the lattice's heaps were still round dots, polka dots on the sea: the far field, with its
    // lumps, takes over from a few kilometres above the clouds)
    // (And from below as well, as the lattice's heaps give way with distance, 15 to 26 km off. From below it came in only
    // where a pixel is 45 m wide, some fifty kilometres off and more: between, a sky of heaps had no clouds at all, and
    // from a craft under a deck the deck ended in a straight line a little above level, with clear sky under it to the
    // horizon: "a strip of light blue in the distance ... that rendered in front of ... the clouds" (the owner, 2026-10-15).)
    // (Heaps from above: from where a pixel is 30 to 60 m wide, 19 to 37 km off on a screen 720 lines tall. From a few metres,
    // as the rest are, from 4 km up the far field drew them from 3 km off: flat slabs with upright walls, cut along the cells
    // of its pattern into shards and square-cornered holes (the main chat, 2026-10-18), where the near shapes are heaps and
    // banks as the photographs show them (research/rendering/experiments/clouds-from-above.md). From 20 to 26 m, as it then
    // was, the banks 12 to 30 km off seen from 1.5 to 10 km above them were still the far field's blocks with flat tops and
    // upright sides, and its shards from higher up (experiments/heaps-from-above.md): from 30 m, the near shapes' rounded
    // heaps with their shade, for a fifth to three tenths more of the walk from a few kilometres above the heaps and next to
    // nothing more from ten. Over a band, where both are worked out.)
    float farW = max(max(smoothstep(45.0, 130.0, pixelChoice), formAway), fromAbove * mix(smoothstep(4.0, 12.0, pixelChoice), smoothstep(30.0, 60.0, pixelChoice), formKind[L].x));
    farW *= 1.0 - step(0.5, _CloudForm.w);
    // (Handed over as one becoming the other: the near shapes shrink away as the far field grows from its strongest parts, each
    // as solid as it is, and a place is the larger of the two. Mixed as amounts of cloud, as they were, half of each was a veil
    // of thin cloud over both, and over the clear sky between heaps a fog of the far field's.)
    formFade = clamp((1.0 - farW) * 1.6, 0.0, 1.0); farGrow = clamp(farW * 1.6, 0.0, 1.0);
    // (thick, a heap's top and not a sheet, until the near shapes or the far field say otherwise: see heapsBody)
    heapsBody = vec4(1e5, 1.0, -1.0, 0.0);
    float cNear = 0.0, nearNear = -1e9, insideNear = -1e9, openNear = -1.0, smoothNear = -1e9;
    for (int once = farW < 0.999 ? 0 : 1; once < 1; once++)
        cNear = formOneNear(L, perF, weight, q, col, above, wm, fine, lod, seek, nearNear, insideNear, openNear, smoothNear);
    nearOut = nearNear; insideOut = insideNear; openOut = openNear; smoothOut = smoothNear;
    if (farW < 0.001) return cNear;
    vec4 bodyNear = heapsBody;
    vec4 K = formKind[L], M = formMore[L];
    float heapy = K.x, sheet = K.y, used = K.z;
    float fi = farInside(L, col, above, used, heapy, sheet, M.x, pixel, fine) * weight + (weight - 1.0) * 400.0;
    float stride = 375.0 * exp2(lod);
    if (seek) return max(cNear, fi + 0.3 * used + 1.5 * stride > 0.0 ? 1.0 : 0.0);
    float nearFar = max((fi + 6.0 + 0.6 * pixel) * 0.7, -300.0);
    float edge = max(mix(mix(26.0, 110.0, sheet), 10.0, heapy), 1.2 * pixel);
    // (Softened by a pixel's width across its outline over the ground, where a pixel can fall half on it; and through it by the
    // edge of a slab as seen close. Softened by the pixel through it, as it was, a clump some hundreds of metres thick never came
    // to its full density from far off, where a pixel is wider than that: from orbit every clump's thinner part, and so most of
    // the world, was a grey veil to see through, under a popcorn of the thicker parts.)
    float thinFar = L == 1 ? mix(0.6, 1.0, max(sheet, heapy)) : 1.0;      // (the middle sky's pads as thin far off as near: see formOneNear)
    float waterFar = 1.0 - heapy * (1.0 - condensed(above));               // (and the heaps' water by height, as near: see condensed)
    float cFar = nearFar < 0.0 ? 0.0 : clamp(farRim / edge + 0.5, 0.0, 1.0) * clamp(fi / 40.0 + 0.5, 0.0, 1.0) * clamp(mix(0.15, 0.35, heapy) + above / mix(mix(90.0, 200.0, sheet), 60.0, heapy), 0.0, 1.0) * farThin * thinFar * waterFar;
    nearOut = farW > 0.999 ? nearFar : max(nearNear, nearFar);
    if (fi > insideNear) { insideOut = fi; smoothOut = fi; openOut = clamp(0.3 + 0.9 * above / max(used, 60.0), 0.0, 1.0); }
    // (The far field's clumps, for the light (heapsBody): as deep as the layer uses, a sheet's and a closed raft's thick, and as
    // thin as the near shapes are made there. Over the handover the near shapes' and the far field's each by its share of the
    // cloud here, so that the light goes over from the one to the other as the cloud does; by which of the two is further inside,
    // as the rest of what is said here goes, it would go over pixel by pixel, a mottled ring 15 to 26 km off.)
    float farShare = cFar / max(cNear + cFar, 1e-6);
    float chordFar = max(mix(used, 1e5, sheet), 1000.0 * smoothstep(0.35, 0.7, M.x) * (1.0 - heapy));
    heapsBody.x = mix(bodyNear.x, chordFar * thinFar * (1.0 - heapy * (1.0 - condensed(0.5 * used))), farShare);
    heapsBody.y = bodyNear.y + (waterFar - bodyNear.y) * farShare;
    // (and a sheet far off as deep as the far field's top stands over the place (farTop): it knows no slab of its own. Across the
    // handover by the share of the cloud wherever either of the two says a sheet, the other's taken as none, its depth that of the
    // one that says so and its density nothing. By whichever had the most of the cloud, as it first was, where the near shapes had
    // a pad over the slab and the far field a sheet, the light deep in the sheet went on and off pixel by pixel as the noise has
    // the one or the other: a mottled ring 15 to 26 km off under a sheet with pads (the review of 2026-10-26). And by the top the
    // far field works out, not the layer's whole depth, which a far sheet's top reaches only at its thickest.)
    vec2 deckFar = sheet > 0.01 ? vec2(max(farTop - above, 0.0), thinFar) : vec2(-1.0, 0.0);
    bool deckNear = bodyNear.z >= 0.0, deckFarOn = deckFar.x >= 0.0;
    heapsBody.zw = deckNear || deckFarOn ? mix(deckNear ? bodyNear.zw : vec2(deckFar.x, 0.0), deckFarOn ? deckFar : vec2(bodyNear.z, 0.0), farShare) : bodyNear.zw;
    return max(cNear, cFar);
}

float formOneNear(int L, float perF, float weight, vec3 q, vec3 col, float above, vec4 wm, bool fine, float lod, bool seek, out float nearOut, out float insideOut, out float openOut, out float smoothOut)
{
    smoothOut = -1e9;
    float N = _CloudNoise.w;
    vec4 K = formKind[L], H = formHeap[L], B = formBank[L], M = formMore[L];
    float heapy = K.x, sheet = K.y, used = K.z, deep = M.z, cover = M.x;
    // (each heap a dome over its base, or a column where they tower)
    heapsBuild(formRaw[L].w);
    int per = int(perF + 0.5);
    float C = N / perF;
    // (how wide a pixel of the picture is at this distance, in metres, and a stride of the seeker's (see the walk); `lod`
    // is the distance as a power of two of 25 km)
    float far = exp2(lod), pixel = 25000.0 * far * formPer, stride = 375.0 * far;
    // (How tall for its width. Heaps: as asked, but so that the widest just reach the layer's top and are flat there,
    // within a quarter of what was asked: the lattice comes in set sizes, the layer's depth in any. Pads: about as tall as
    // the layer, the largest pressed flat under its top in their middles and the rest low domes under it: pillows. Made
    // half as tall again as the layer and cut flat by its top, as they were until 2026-10-15, every pad had upright sides
    // and a flat top and bottom: a drum, and a sky of them was the owner's "really tall and pillar-like".)
    float tall = mix(1.05 * used / (0.8 * C), clamp(1.05 * deep / (0.84 * C), 0.8 * formSize[L].x, 1.25 * formSize[L].x), heapy);
    // (Pads as thick as pads are, whatever share of the layer is sheet: a lumpy layer's lid (formWeather). With the layer's
    // own, where the kind is between pads and sheet (at the edge of a sheet of the middle sky, 0.1 to 0.3), every pad stood as
    // tall as the sheet's slab can be: a kilometre and more of upright column under each cloudlet, in a deep layer.)
    float usedPads = min(used, L == 1 ? 190.0 : 560.0);
    float share = H.x * weight, widest = C * H.y, evenly = H.z, join = C * H.w;
    // (What changes over a kilometre or two: how high the base stands, how thick a sheet is, how lumpy a top. One
    // coarse reading of the large noise for it where the layer is a sheet or pads; where it is heaps, the banks' own
    // reading, below, serves; where it is neither (towers, the day of puffs), none.)
    float softWide = 0.5;
    for (int once = heapy < 0.999 ? 0 : 1; once < 1; once++)
    {
        vec4 wide = textureLod(_CloudShape, col.zxy * (perF / 7.3) + vec3(0.29, 0.61, 0.13), 3.4 * _CloudHaze.x);
        softWide = clamp((wide.r - wide.g) / max(1.0 - wide.g, 0.02), 0.0, 1.0);
    }
    float size = 0.0, age = 0.0, lift = 0.0, inside = -1e5, leaves = 1e6;
    // ---- the banks. The usual sky is not heaps strewn evenly: "usually the clouds aren't so scattered. They're like huge
    // plateaus" (the user, 2026-10-12), and photographs say the same: the heaps of a fair day stand gathered, run
    // together into banks some kilometres across and several times wider than they are deep, level and grey underneath
    // and lumpy on top, with wide clear sky between and a few small heaps by themselves in it. So: over the ground, a
    // pattern that changes over kilometres says where the banks are (the strongest of it, as much as makes most of the
    // sky's cover); a bank is that patch of the layer filled from the base to some way under the layer's top, with a
    // steep side; the lattice's heaps stand through it. Not on the day of many small puffs, and not where the heaps tower.
    // (How much of a bank there is: all of it on the usual day in a shallow layer, none where the heaps tower. Where
    // there is less than all, the bank is drawn in from its edge, by up to three kilometres, until it is gone: its
    // distances stay distances. Multiplied by a share instead, as they first were (and by this lattice's share of the
    // layer, where two sizes of lattice are both there), a bank with a small share had every distance from it made
    // small, and the lumps, which stand a hundred metres proud, then made cloud of the clear air for kilometres round
    // it: in the game, where the layer's depth changes from place to place, teeth along the edge of every bank.)
    float banks = B.x, bank = -1e5, bankFoot = 0.0;
    for (int once = banks > 0.01 ? 0 : 1; once < 1; once++)
    {
        vec4 broad = textureLod(_CloudShape, col.yzx * 0.27 + vec3(0.61, 0.17, 0.43), 2.0 * _CloudHaze.x);
        float strength = bankStrength(broad, col);
        // (how far inside the bank's edge, over the ground: the pattern is about 0.55 steeper per length of the noise
        // at this size)
        float within = (strength - B.y) * (N / 0.55) - (1.0 - banks) * 3000.0;
        // (and how far the place is within the bank's own ground, over a few hundred metres at its edge: there the heaps in it stand on
        // its base, see `under`)
        bankFoot = smoothstep(-200.0, 200.0, within);
        // (and what changes over a kilometre or two, from the same reading's cells)
        if (heapy > 0.999) softWide = clamp(0.5 + (broad.a - 0.48) * 0.9, 0.0, 1.0);
        // (its own top well under the layer's, lower at its edge than further in, and rounded off towards its side:
        // the heaps in it stand through it, a top of domes and not a level lid)
        float top = min(used, 900.0) * mix(0.42, 0.68, smoothstep(0.0, 900.0, within)) * (0.9 + 2.0 * (softWide - 0.5));
        bank = -larger(-within, above - top, 140.0);
    }
    // (Read from the map drawn once a frame, where there is one: two readings, and the egg of the heap they name.)
    // (The map comes round: a place further from the camera than half its width would be given the heaps of another. There
    // are none so far off: see go 5.)
    bool mapped = L == 0 && _CloudHeapsAt.x > 0.5;
    vec2 mapAt = faceOf(col, _CloudHeapsAt.w).xy, mapFrom = abs(mapAt - faceOf(_CloudNoise.xyz, _CloudHeapsAt.w).xy);
    // (the map holds heaps where the low layer is heaps, and cells where it is pads: where it is something of both, neither
    // is read from it)
    bool onMap = mapped && sheet < 0.999 && (heapy > 0.98 || heapy < 0.02) && max(mapFrom.x, mapFrom.y) < 0.5 * _CloudHeapsAt.y;
    // (pads: the open sky either side of a cell's edge, metres: narrow where most of the sky is covered, and so wide where
    // little is that each cell's cloud is a tuft in its middle)
    // (the low layer's cracks closing to a few tens of metres where most of the sky is covered, its cells' corners rounded
    // over a twentieth of a cell: a deck, as stratocumulus of nine tenths is, broken by the gaps between its rafts and its
    // cells that come and go; the middle sky's as they were, the mackerel sky's cloudlets with sky between them)
    float crack = C * (L == 0 ? mix(0.42, 0.012, ramp(0.05, 0.75, cover)) : mix(0.42, 0.05, ramp(0.05, 0.8, cover)));
    for (int once = onMap ? 0 : 1; once < 1; once++)
    {
        vec2 muv = mapAt / _CloudHeapsAt.y;
        vec4 heap = textureLod(_CloudHeaps, muv, 0.0);
        inside = -heap.r;
        for (int pads = heapy < 0.5 ? 0 : 1; pads < 1; pads++)
        {
            inside = padsProfile(heap, above);
            size = heap.g > 0.0 ? max(heap.g, 0.25 * C) : 0.0; age = heap.a; lift = textureLod(_CloudHeapsB, muv, 0.0).r;
        }
        for (int there = heapy >= 0.5 && heap.g > 15.0 ? 0 : 1; there < 1; there++)
        {
            float R = heap.g, H = heap.b, raised = textureLod(_CloudHeapsB, muv, 0.0).r, up = above;
            // (the spreading top, as heapsRound has it)
            float spread = B.z * clamp((H - 0.9 * used) / (0.3 * used), 0.0, 1.0) * clamp((up - 0.55 * used) / (0.3 * used), 0.0, 1.0) * clamp(heap.a + 0.8, 0.0, 1.0);
            inside = heapEgg(heap.r / min(1.0 + 0.9 * spread, 0.46 * C / R), up, R, H);
            size = R; age = heap.a; lift = raised + heapsRim(heap.r / max(R, 1.0)) * H;
        }
    }
    // (Off the map, worked out at the asking, as where there is no map: from close by there are none so far off (the
    // walk's fading of them with distance), but from above the clouds there are, as far as the planet's rim.)
    for (int once = !onMap && sheet < 0.999 && share > 0.002 ? 0 : 1; once < 1; once++)
    {
        // (how the place on the layer's base moves as the line is walked: the line's way along the ground, and the heaps'
        // lean for what it climbs)
        float climbs = dot(formWay, formUp);
        vec3 across = formWay - formUp * climbs - _CloudDrift.xyz * (K.w * climbs);
        // (Each face's lattice moved off the others' by its own amount, the same wherever it is looked at from. Until the
        // evening of 2026-10-12 it was the nearer face's that stood unmoved and the further's that was moved: across the
        // line where two faces meet, each lattice then changed places with itself, and its heaps were cut along that line.)
        // (The face the place is nearest, and near where two faces meet the other one too: in one loop, so that Windows'
        // compiler, which puts every function inline, makes one copy of heapsRound here and not two.)
        for (int side = 0; side < (formMine < 0.999 ? 2 : 1); side++)
        {
            float f = side == 0 ? formFace : formOther, mine = side == 0 ? formMine : 1.0 - formMine;
            vec3 cg = faceOf(col, f), ug = faceOf(formUp, f);
            float size2 = 0.0, age2 = 0.0, lift2 = 0.0, leaves2 = 1e6, inside2 = -1e5;
            // (the middle sky's cloudlets in rows across the wind, over some regions some tens of kilometres across: see padsRows)
            vec2 wd = faceOf(_CloudDrift.xyz, f).xy;
            padsRowsWay = dot(wd, wd) > 1e-6 ? normalize(wd) : vec2(1.0, 0.0);
            padsRows = L == 1 ? smoothstep(0.42, 0.58, textureLod(_CloudShape, col * 0.11 + vec3(0.37, 0.71, 0.19), 3.0).r) : 0.0;
            // (heaps round their points of the lattice; pads in the cells of the churning (padsCells); where the kind is
            // between the two, both, each by its share)
            for (int once2 = heapy > 0.02 ? 0 : 1; once2 < 1; once2++)
                inside2 = heapsRound(cg.xy * perF + vec2(7.3, 3.1) * f, -ug.xy / ug.z, faceOf(across, f).xy * (perF / N), C, per, _CloudForm.y, above, share * mine, widest, evenly, join, tall, used, B.z, size2, age2, lift2, leaves2);
            for (int once2 = heapy < 0.98 ? 0 : 1; once2 < 1; once2++)
            {
                float sizeC, ageC, liftC, leavesC, w = heapy > 0.02 ? heapy : 0.0;
                float insideC = padsCells(cg.xy * perF + vec2(7.3, 3.1) * f, -ug.xy / ug.z, faceOf(across, f).xy * (perF / N), C, per, _CloudForm.y, above, share * mine, crack, usedPads, L == 0 ? 0.05 : 0.0, sizeC, ageC, liftC, leavesC);
                inside2 = mix(insideC, inside2, w); size2 = mix(sizeC, size2, w); age2 = mix(ageC, age2, w); lift2 = mix(liftC, lift2, w); leaves2 = min(leaves2, leavesC);
            }
            if (side == 0) { inside = inside2; size = size2; age = age2; lift = lift2; leaves = leaves2; }
            else
            {
                if (inside2 > inside) { size = size2; age = age2; lift = lift2; }
                inside = larger(inside, inside2, join);
                leaves = min(leaves, leaves2);
            }
        }
    }
    // (the bank with the heaps in it and on it; where the bank has the place, its numbers: as if a heap as wide as the
    // lattice's largest, in the middle of its life, on the layer's base)
    // (Where two sizes of lattice are both there, the bank is in both, whole, and its lumps, which are of each lattice's
    // own size, stand as proud in each as that lattice's share: the one's lumps go as the other's come.)
    float bankShare = onMap ? smoothstep(0.0, 0.5, weight) : 1.0, whole = 0.0;    // (whole: how much a bank or a sheet has the place, for the light: see heapsBody below)
    for (int once = banks > 0.01 ? 0 : 1; once < 1; once++)
    {
        // (The bank's numbers taken over from the heap's across the width the two are joined over, not at the line where the
        // bank comes nearer than the heap: a heap's base rises towards its rim by up to a fifth of its height, the bank's by three
        // tenths of that, and switched at that line, the base had a ledge tens of metres high there: slabs hung under heaps (the
        // lab), and, it seems, the thin bright arc across a heap's underside from the pad (the main chat, 2026-10-18).)
        float toBank = clamp(0.5 + 0.5 * (bank - inside) / (0.2 * C), 0.0, 1.0);
        whole = toBank;
        size = mix(size, mix(size, 0.42 * C, 0.7), toBank); age = mix(age, age * 0.3, toBank); lift = mix(lift, lift * 0.3, toBank);
        bankShare = mix(bankShare, smoothstep(0.0, 0.5, weight), toBank);
        inside = larger(inside, bank, 0.2 * C);
    }
    // ---- the sheet: a slab from the base up, as thick as the smooth noise is strong there over the level that leaves so
    // much of the sky covered (holes with thin edges where it is not all covered); and under a lumpy layer that covers
    // most of the sky, a thinner one that joins the pads
    float fill = B.w * weight, slabUp = -1.0, slabShare = 0.0;      // (slabUp: how far up the sheet's own top is over the place; slabShare: how much the sheet has the place: see heapsBody)
    for (int once = fill > 0.001 ? 0 : 1; once < 1; once++)
    {
        float thick = used * mix(0.55, 1.0, sheet) * fill * clamp((softWide - M.y) / 0.045 + 0.2, 0.0, mix(1.0, 0.75 + 0.5 * softWide, sheet));
        float slab = min(above, thick - above);
        // (the pads' numbers kept where a pad has the place, the sheet's own where it has)
        if (slab > inside) { size = mix(size, 0.42 * C, 0.5); lift = 0.0; }
        slabUp = max(thick - above, 0.0);
        slabShare = clamp(0.5 + (slab - inside) / max(0.5 * join, 1.0), 0.0, 1.0);
        whole = max(whole, slabShare);
        inside = larger(inside, slab, 0.5 * join);
    }
    // (the lumps: the largest about a third of a cell across on a heap, half that on a pad, and wide and slight on a sheet;
    // how far they can stand out)
    // (On a heap larger and prouder since 2026-10-21, three tenths of a cell and as proud as their size before: set beside stills of
    // blackrack's clouds, which the owner holds "look way better", our heaps were a smooth dome under an even coat of small lumps,
    // cotton, where his and the photographs' are lobes upon lobes, each a good part of the heap, with deep creases between.)
    float lump = C * mix(mix(0.15, 0.4, sheet), 0.36, heapy);
    // (and not as proud everywhere: over some kilometres a top is smoother, where the air has stopped rising, and over
    // others lumpier, where it still is; lumps all alike over a whole bank were a sheet of bubbles)
    float proud = mix(mix(0.7, 0.22, sheet), 1.25, heapy) * mix(0.45, 1.25, smoothstep(0.42, 0.6, softWide));
    // (Under the lid of a shallow layer the lumps count for little: the air there is held down, and what spreads out
    // under it is flat and thin. With lumps a hundred metres proud on a pad a hundred metres thick, the pad was torn
    // into leaves, one over another. And they reach as much less far: the walk goes slowly only as near a surface as its
    // lumps can stand out from it, which over the flat top of a bank seen from just above is most of the way.)
    float damp = (1.0 - 0.8 * heapy * (1.0 - smoothstep(1400.0, 2600.0, deep)) * smoothstep(0.5, 0.85, above / used)) * bankShare;
    // (On a heap two thirds of it: its lumps no further out than before they were made larger and prouder, held to a fifth of their
    // share (below), however much deeper their creases. With the whole, the walk went slowly over a wider band round every heap:
    // 5 ms a frame more at the pad (the main chat, 2026-10-21), for the same picture within a level.)
    float reach = 0.5 * lump * proud * damp * mix(1.0, 0.67, heapy);
    insideOut = -1e9; openOut = -1.0;
    // (How far off the nearest cloud is, is known only for the heaps of the three points looked at: a heap of the next
    // triangle may stand just across its edge. So the walk is never told it may stride further than three tenths of a
    // cell, whatever these three say, and the seeker says "perhaps" whenever its stride is longer than that. Told the
    // plain distance, as at first, it strode clean over far heaps: for whole rows of the picture at once, since every
    // line of sight of a row comes to a far heap by nearly the same strides: heaps cut into level slats.)
    // (Where the lattice has no heaps, far off, there is nothing unseen: the walk is told the whole distance. Where it
    // has, no further than to where the line being walked leaves this triangle of the lattice, and a little way in to
    // the next; a line whose way is not told, three tenths of a cell.)
    // (Read from the map: no heap's edge within half a cell is known there, and nothing further. Off the map there are
    // no heaps.)
    // (shrunk towards nothing as the far field takes over: see formOne)
    inside -= (1.0 - formFade) * max(size, 0.3 * C);
    float most = onMap ? 0.45 * C : share > 0.002 ? (dot(formWay, formWay) > 0.5 ? leaves + 0.04 * C : 0.3 * C) : 1e6;
    if (seek) { nearOut = 1e9; return inside + reach + 1.5 * stride > 0.0 || stride > most ? 1.0 : 0.0; }
    nearOut = max((inside + reach + 6.0 + 0.6 * pixel + (L == 1 ? padsFray * 99.0 : 0.0)) * 0.7, -most);   // (the softer margin of a row's lane reaches half its 2.2 x 90 m further out)
    if (nearOut < 0.0) return 0.0;
    // ---- the lumps: round in every direction; three readings in one, each of cells half the last's size and two sizes
    // finer with them; and finer again near the camera. On a small heap the large ones count for less.
    vec3 at = q + wm.yzw * 0.6;
    float ls = 2250.0 / lump;
    vec3 part = clamp(size / (vec3(1.3, 0.65, 0.33) * lump), 0.0, 1.0);
    // (Each lump rounded on top: a cell is a cone, highest at a point, and cones standing on a level sheet are a bed of
    // spikes; one less the square of what it lacks of its top is a dome, with the same sharp crease where it meets the
    // next. That comes to 0.715 on the whole, where the cells come to 0.48.)
    // (Read only where the small noise is read too: at the walk's own steps, and at the light's first two towards the
    // sun, which are what show a lump its own shade. The light's further steps, tens and hundreds of metres off, ask
    // only for the shape under the lumps.)
    float moved = 0.0, wisps = 1.0;
    if (fine)
    {
        vec4 big = textureLod(_CloudShape, at * ls + vec3(0.19, 0.43, 0.67), max(lod + log2(ls) - 1.0, 0.0) * _CloudHaze.x);
        // (and where the cloud is in wisps and holes: see the density at the end)
        wisps = smoothstep(0.42, 0.6, big.g * 0.5 + big.b * 0.3 + big.a * 0.2);
        vec3 domes = 1.0 - (1.0 - big.gba) * (1.0 - big.gba);
        // (A crease between two lumps no deeper than three tenths of a lump's size: the cells' own creases go down to
        // nothing between any two of them, and a heap's side was scored with clefts, some of them through it.)
        // (The smaller two sizes count for more than they did, two thirds and two fifths of the largest, not a half and a quarter:
        // lumps on lumps. And no lump stands out by more than a fifth of its share: prouder, they stood clear of their heap, loose
        // puffs over its top, a spray of popcorn.)
        moved = dot(clamp(domes - 0.715, vec3(-0.3), vec3(0.2)) * part, vec3(1.0, 0.65, 0.4)) * 1.6 * lump * proud;
        // (read no finer than a pixel of the picture is wide: the small noise's cells are a sixteenth of what is read here
        // to a dot of it. Read two sizes finer than that, as it first was, cells of nine metres were read by pixels ten
        // metres apart, and what came of it was level stripes across every heap a few kilometres off.)
        float cellSmall = lump / 8.0;
        // (three sizes of the small noise from its one reading, as of the large: until the evening of 2026-10-12 only the
        // coarsest of them, and a tower a few kilometres off had no lump on it under a hundred metres: modelling clay)
        // (not read at all where it would be read so coarsely that it is the same everywhere: far off, from above the clouds)
        float smallLevel = clamp(log2(pixel / (cellSmall / 16.0)) + 0.3, 0.0, 6.0);
        for (int read = smallLevel < 4.5 ? 0 : 1; read < 1; read++)
        {
            vec3 small = textureLod(_CloudDetail, at * (N / (cellSmall * 4.0)) + vec3(0.0, 0.0, _CloudDrift.w), smallLevel * _CloudHaze.x).rgb;
            moved += dot(small - 0.48, vec3(1.0, 0.5, 0.3)) * 1.6 * cellSmall * proud;
        }
    }
    moved *= damp;
    float plain = inside;
    inside += moved;
    // (Level underneath: cut after the lumps, so that the rim of the base is scalloped by the lumps standing over it and
    // the underside itself is smooth. Cut before them, the lumps and the creases between them were on the underside too:
    // a base veined like marble, seen from below. Not level to the metre: it rises and dips by a few tens of metres, gently.)
    // (A heap that is going has no level base any more: what is left of it is ragged underneath, by its own lumps.)
    float going = heapy * smoothstep(0.4, 0.95, age);
    // (The rise and dip from the wide reading, which is the same at every height over a place. From the lumps' own
    // reading, as it first was, it changed with height as fast as it stood: the cut was then not one surface but
    // several, and every heap had thin level slits through its lower part, stripes across it from a few kilometres off.)
    // (One level base wherever a bank is: the heaps in a bank stand on its base, as a field's heaps stand on the level where the air
    // first condenses, and each heap's own base, raised by chance and rounded up towards its rim, is a heap's by itself. Kept in
    // part under a bank (three tenths of it, as until the evening of 2026-10-18), it terraced the bank's underside: from below at a
    // few kilometres a wide grey lens with arcs nested in it (the main chat's lens in the middle sky), a crease across a bank
    // overhead, plates stacked one over another at the horizon, upright stripes on a bank's face (the lab).)
    float under = above - lift * (1.0 - bankFoot) - (softWide - 0.5) * 260.0 * mix(0.5, 1.0, heapy) + going * (1.5 * moved + 40.0);
    // (The top under the lid is uneven by lumps too: low domes. But not by the lumps as they are at this place: they
    // change as fast with height as they stand proud, and a level top moved up and down by them is not one surface but
    // several, one over another every twenty metres: level stripes across every heap that reaches the layer's top, and a
    // sheet seen from above like whipped cream. By the lumps as they are at the lid's own height over this place: one
    // surface. Read only near the lid, and only where the small noise is read too.)
    float below = used - above, lidMoved = 0.0, domed = mix(0.55, 0.35, heapy);
    for (int once = fine && below < 0.75 * lump * proud * domed + inside + moved + 30.0 ? 0 : 1; once < 1; once++)
    {
        vec4 over = textureLod(_CloudShape, (at + formUp * (below / N)) * ls + vec3(0.19, 0.43, 0.67), max(lod + log2(ls) - 1.0, 0.0) * _CloudHaze.x);
        vec3 overDomes = 1.0 - (1.0 - over.gba) * (1.0 - over.gba);
        lidMoved = dot((overDomes - 0.715) * part, vec3(1.0, 0.5, 0.25)) * 1.6 * lump * proud * domed * bankShare;
    }
    inside = min(inside, min(under, below + lidMoved));
    insideOut = inside;
    smoothOut = min(plain, min(under, below + lidMoved));
    // (how much sky is seen: more on the tops of lumps than in the creases between them, more high on a heap than low)
    // (In the creases half of what the tops of lumps see, not a twelfth: the light of the sky comes into a crease from
    // the lit cloud on either side of it as well, and creases drawn as dark as an open sky alone would leave them were
    // black lines over every shaded side.)
    openOut = mix(0.5, 1.0, clamp(0.5 + moved / (0.5 * lump * proud), 0.08, 1.0)) * clamp(0.25 + 0.9 * above / max(mix(used, 1.2 * size, heapy), 60.0), 0.0, 1.0);
    // (The surface is a few metres of clear air mixing in; far off, as wide as a pixel of the picture; a sheet's is tens of
    // metres of thinning mist. Underneath it is not a surface at all: the cloud thins downward over some tens of metres
    // to where the air is first cloudy, a sheet over more.)
    // (And a heap that is going is soft all round, and thin: it is evaporating.)
    // (pads' edges softer than heaps': the cells' cloud is thin, and its edges are seen through)
    // (The middle sky's pads, altocumulus, as thin as they are and softer still: a cloudlet holds 0.1 to 0.3 g of water a cubic
    // metre where a heap holds 0.5 to 1 and more, and loses a third to a half as much light a metre (1.5 LWC / (rho_w r_e) with
    // droplets of 10 microns); drawn as dense as heaps, a sky of them was the main chat's "small hard puffs", solid white to a
    // crisp outline, where photographs show cloudlets the sky's blue comes through at their margins, frayed over tens of metres
    // (research/rendering/experiments/altocumulus-look.md).)
    // (and in a row's lane, the middle sky's margins as much as three times as soft: see padsRows)
    float edge = max(mix(mix(L == 1 ? 90.0 : 40.0, 110.0, sheet), 10.0, heapy) * (1.0 + 5.0 * going), 1.2 * pixel) * (1.0 + 2.2 * (L == 1 ? padsFray : 0.0));
    float thinner = L == 1 ? mix(0.6, 1.0, max(sheet, heapy)) : 1.0;
    // (The middle sky's pads in wisps and holes through their lower 160 m, by the lumps' own pattern: a thick cloudlet's underside
    // mottled, a thin one (a pad of a little over a hundred metres, as many are) patchy all through. Even through, as they were, the
    // thin cloudlets were clear film with a crisp outline among the white ones, cellophane (the main chat, 2026-10-18 18:33), where
    // a thin altocumulus in the photographs is a patch of wisps with the sky seen through its gaps.)
    float patchy = mix(mix(1.0, wisps, L == 1 ? (1.0 - heapy) * (1.0 - sheet) : 0.0), 1.0, clamp(under / 160.0, 0.0, 1.0));
    // (How thick the body the place is in is, the thinnest way through it, for the light scattered many times over (heapsBody's x;
    // stage 5, 2026-10-26): a heap as tall as it is made for its width, under the layer's top, and no wider; a pad as tall as its
    // layer lets it be, and no wider; as thin as a cloudlet of the middle sky is, and a heap that is going. A bank where it has the
    // place, and a sheet: thick, kilometres across and deep enough (a sunlit deck seen from above is white: the reworks of
    // 2026-10-11 and -12). And pads closed into a deck, the layer's cover over a third: by its scattering alone a deck of pads a
    // hundred metres deep is a sixth less white from above than a heap's top, but a sunlit deck seen from above is to stay white
    // (the owner); a pad on its own goes by its size, and the sky shows through it.)
    float chord = size > 1.0 ? mix(min(2.0 * size, usedPads), min(2.0 * size * min(tall, 1.0), used), heapy) : 1e5;
    chord = mix(max(chord, 1000.0 * smoothstep(0.35, 0.7, cover) * (1.0 - heapy)), 1e5, whole);
    heapsBody.x = chord * thinner * (1.0 - 0.55 * going) * (1.0 - heapy * (1.0 - condensed(0.5 * chord)));   // (as dense as its water at half its height: the mean through it)
    // (and a heap's water by height over its own base: see condensed; the walk's powder and skin go by it. The sheet that joins a
    // lumpy layer's heaps where they cover most of the sky too: it is their bases grown together, as thin at its foot as theirs. A
    // layer with a sheet of its own has no heaps (heapy and sheet are never both above nought), and is as dense through as it was.)
    heapsBody.y = 1.0 - heapy * (1.0 - condensed(under));
    // (and in a layer with a sheet, how far up its own top is and how dense it is through, by how much it has the place: the walk's
    // light deep in it. By whether it has the place, as at first, the light went over from the sheet's to a pad's in a step where the
    // two are joined (the second look, 2026-10-26); by how much, over the width they are joined over.)
    heapsBody.zw = vec2(slabUp, thinner * slabShare);
    return clamp(inside / edge + 0.5, 0.0, 1.0) * clamp(mix(0.15, 0.35, heapy) + under / mix(mix(90.0, 200.0, sheet), 60.0, heapy), 0.0, 1.0) * (1.0 - 0.55 * going) * thinner * patchy * heapsBody.y;
}

// One layer's cloud at a place, so made: the low layer (L 0) or the middle one (1), by what formStep worked out for the
// step. As layerAt, which hands over to it.
float formAt(int L, vec3 q, float h, float kind, float wet, vec4 wm, bool fine, float lod, bool seek)
{
    float N = _CloudNoise.w;
    vec4 K = formKind[L], Z = formSize[L];
    float deep = formMore[L].z, above = h - formMore[L].w, hf = above / deep;
    // (a lumpy layer uses only the lowest few hundred metres of the air it is given: nothing is asked above that)
    if (above > K.z + 150.0) { heapsNear = max(heapsNear, -alongTo(above - K.z - 150.0, h, true)); return 0.0; }
    // ---- the place on the layer's base under this one (so that a heap stands upright), moved downwind as much as the
    // heap leans by this height, and bent over kilometres
    vec3 col = q - formUp * (above / N) - _CloudDrift.xyz * (above * K.w / N) + wm.yzw * 0.6 + vec3(0.37, 0.71, 0.13) * float(L);
    // (The lattice at its size, and where the layer's depth asks for a size between two, at the next too: in one loop, so that
    // Windows' compiler, which puts every function inline, makes one copy of formOne here and not two.)
    float nearA = -1e9, insideA = -1e9, openA = -1.0, smoothA = -1e9, c = 0.0;
    vec4 bodyA = vec4(1e5, 1.0, -1.0, 0.0);
    for (int size_ = 0; size_ < (Z.w > 0.001 ? 2 : 1); size_++)
    {
        float nearB, insideB, openB, smoothB;
        float c2 = formOne(L, size_ == 0 ? Z.y : Z.z, size_ == 0 ? 1.0 - Z.w : Z.w, q, col, above, wm, fine, lod, seek, nearB, insideB, openB, smoothB);
        vec4 bodyB = heapsBody;     // (formOne's, given out by the way: see heapsBody)
        if (size_ == 0) { c = c2; nearA = nearB; insideA = insideB; openA = openB; smoothA = smoothB; bodyA = bodyB; }
        else
        {
            nearA = max(nearA, nearB);
            if (c2 > c || (c <= 0.0 && insideB > insideA)) { c = c2; insideA = insideB; openA = openB; smoothA = smoothB; bodyA = bodyB; }
        }
    }
    if (seek) return c;
    heapsNear = nearA;
    if (c <= 0.0) return 0.0;
    heapsInside = insideA; heapsOpen = openA; heapsSmooth = smoothA; heapsBody = bodyA; heapsDeck = vec2(formKind[L].y, formMore[L].w);
    // (a sheet is thinner stuff than the core of a heap: a grey overcast lets a share of the light through, where a sheet as
    // dense as a heap's core is dark as a storm's base)
    float kindF = mix(0.3, 1.0, smoothstep(0.1, 0.45, kind));
    c *= kindF;
    // (and the body as thin as its kind, and thicker by the rain's thickening underneath, half of it over the whole on the mean)
    heapsBody.xw *= kindF * (1.0 + 0.5 * wet);
    // (Fog: a low layer of a kind under 0.02 (CloudSource.Kind). The weather's own to draw while the camera is in it or
    // near it (Natural Light's veil, which leaves off two kilometres or so over its top); the clouds draw it only from over
    // that, where banks of it lie on the sea and the low ground with their edges, coming in by how dense it is (by how much
    // of the sky it covers, it came in as scattered slabs). Nothing drew it from there before 2026-10-17.)
    for (int fog = L == 0 && kind < 0.03 ? 0 : 1; fog < 1; fog++)
        c *= mix(1.0, smoothstep(formMore[0].w + formMore[0].z + 1600.0, formMore[0].w + formMore[0].z + 2400.0, _CloudHigh.x), 1.0 - smoothstep(0.015, 0.03, kind));
    // (rain clouds are thicker and darker underneath)
    return c * (1.0 + wet * (1.0 - hf));
}

// ---------------------------------------------------------------- the second form (2026-10-27)
// The low and middle clouds as Blackrack's are made (his method, learned from his files and written here in our own words and
// code: research/clouds-r5/blackrack-source.md, R6 and R16). A pattern over the planet says how much of the sky over a place is
// cloud (coverOf: as much of the pattern covered as the weather's cover asks, the covered part thin at its edge and thicker
// inside); each kind of cloud says how much of its column stands at each height (its height curve: a cumulus widest a little
// over its flat base and narrowing to its top, a sheet whole from its base nearly to its top); the two added, less one, say how
// deep inside the cloud a place is; and the billows eat into that by up to E, what is left being cloud, with an edge as hard as
// the kind's. Deeper inside than the billows can reach a place is whole, and they are not read. Each kind's numbers are his
// where he has the kind (cumulus, congestus, and his stratus for altostratus: Kerbin's clouds.cfg; written as curves of our own
// through his points), ours where he has not (fog, the rain sheet, stratus, the lumpy layers). Drawn by the copies of this
// shader built for it (FORM_TWO), a development trial until the owner has seen both forms side by side.

// (a table of nine numbers at 0, 1 ... 8, read between: x from 0 to 8)
float pick4(vec4 v, int i) { return i == 0 ? v.x : i == 1 ? v.y : i == 2 ? v.z : v.w; }
float table9(float x, vec4 a, vec4 b, float c)
{
    x = clamp(x, 0.0, 8.0);
    int i = int(min(floor(x), 7.0));
    float lo = i < 4 ? pick4(a, i) : pick4(b, i - 4);
    float hi = i < 3 ? pick4(a, i + 1) : i < 7 ? pick4(b, i - 3) : c;
    return mix(lo, hi, x - float(i));
}

// (How much of the small heaps' pattern a layer's cover is made with, at a height over its base: see coverOf.)
// (A heap's: the same at every height since 2026-11-06, each small heap standing through its whole column. Until then, with
// _CloudSpec.w's 128 still, less of it the higher over the base, none from 900 m: then the pattern's small heaps were a third of
// a kilometre to a few across, and over a heap's column of 2.7 km (columnOf) they stood as spikes a kilometre and more high,
// every wall fluted top to bottom with its finest detail, drawn straight up (the stills of 2026-10-27); faded out, a field of
// heaps stood as thin mats some 800 m deep, where Blackrack's heaps rise 1.4-2 km. The pattern's small heaps are now 2 to 10 km
// across (pattern.py: a field a third covered, the median heap 1.4 km across and 2.1 km apart, his 2.3 and 4.5 km; wider than
// that the masses' own 1.3 km set the median), and the finer detail his noise's alone, as his is. The same pattern makes a lumpy
// layer's cells (stratocumulus, and the middle sky's altocumulus, which never faded): theirs are 2 km and more now too, broader pads
// where they were broken down to a third of a kilometre.)
float smallOld() { return mod(floor(_CloudSpec.w / 128.0), 2.0) > 0.5 ? 1.0 : 0.0; }
float smallOf(float heapy, float sheet, float above) { return mix(1.0 - sheet, 0.6, heapy) * (1.0 - smallOld() * heapy * smoothstep(0.0, 900.0, above)); }

// (How much of a column stands over a place, from the pattern's reading there (g) and how much of the pattern is covered: see
// coverOf.)
float coverFrom(float g, float most) { return pow(clamp((1.0 / (1.0 + exp(-1.702 * g)) - (1.0 - most)) / max(most, 1e-4), 0.0, 1.0), 1.6); }

// (How much of the column stands over a place from the covered share's own place in it (v, 0 at its edge to 1 at its strongest),
// for a layer so much heaps. A heap's after Blackrack's: his cover maps, read where his cumulus stands, are low and flat (within
// cloud a tenth of them under 0.06, half under 0.2, nine tenths under 0.54, the rest a few strong cores), and cloud stands where
// they and the height curve together pass the billows, so that most of his heaps are low and rounded, a few tall, with clear sky
// between; the curve here is ours through those points (research/clouds-r5/his-code-2026-11-05.md). As v to the power 1.6, as
// until 2026-11-06 (_CloudSpec.w's 64, as it still is for sheets), half the covered part stood over 0.3 and climbed a kilometre
// in a few hundred metres at its edge: a field of heaps one mass, its walls straight up.)
float heapsNew() { return mod(floor(_CloudSpec.w / 64.0), 2.0) > 0.5 ? 0.0 : 1.0; }
float coverCurve(float v, float heapy)
{
    // (the least, 0.04, reached over the covered part's first twentieth, not at once: a step there stood specks of cloud at its edge)
    float Fh = 0.04 * min(v * 20.0, 1.0) + 0.6 * v * v + 0.3 * pow(v, 20.0);
    return mix(pow(v, 1.6), Fh, heapy * heapsNew());
}
// (How much of the pattern is covered for so much of the sky to be cloud at a layer's base, once the billows have eaten into it
// (pattern.py, from billow.py's spread): for the power 1.6 and for the heaps' curve, between them by how much heaps.)
float mostOf(float cover, float heapy)
{
    float a = table9(cover * 8.0, vec4(0.0, 0.2174, 0.434, 0.651), vec4(0.8652, 1.0811, 1.3056, 1.5864), 12.0);
    float b = table9(cover * 8.0, vec4(0.0, 0.2970, 0.5956, 0.8943), vec4(1.1970, 1.5183, 1.9218, 2.6093), 12.0);
    return mix(a, b, heapy * heapsNew());
}

// (Which copies of the pattern coverOf reads where a pixel of the picture is so wide (m): the masses' (x) and the small heaps' (y,
// read four times finer), each at the copy as coarse as the pixel, no coarser than the seventh, where the masses are all but gone;
// and how far those copies have narrowed each kind of reading's spread (pattern.py), by which coverOf divides its readings so that
// as much of the sky is covered from any distance. Here for coverOf and the walk's strides alike (clearFor, patternMost), whose
// bound must take in every copy coverOf reads.)
vec2 patternLods(float pixel) { return clamp(log2(max(pixel, 1.0) / vec2(260.0, 65.0)), 0.0, 6.0); }
vec2 patternSpread(vec2 lods)
{
    return vec2(table9(lods.x, vec4(1.0, 0.9924, 0.968, 0.911), vec4(0.7995, 0.6046, 0.2709, 0.0884), 0.0442),
                smallOld() > 0.5 ? table9(lods.y, vec4(1.0, 0.9845, 0.9365, 0.8412), vec4(0.6961, 0.4852, 0.2295, 0.0617), 0.0188)
                                 : table9(lods.y, vec4(1.0, 0.9987, 0.9935, 0.9737), vec4(0.9029, 0.7093, 0.412, 0.1025), 0.0398));
}

// (Towers among the heaps, after the owner's spec of Blackrack's clouds (2026-11-04, research/clouds-r5/spec-gaps-2026-11-04.md,
// item 4): his weather's type map puts congestus among the cumulus a few kilometres apart, and the type sets the column, 2.7 km for
// cumulus to 5.7 km for congestus; ours drew every heap of a layer over one column, a field of heaps all of a height. Here the
// strongest few hundredths of the masses' pattern (g, a standard normal: from 1.3 to 2.1) in a layer of heaps, where the sky is
// not mostly covered (a deck is not towers), stand on a column up to 2.1 times as deep and lean towards congestus. None in the
// middle sky, nor with _CloudSpec.w's 4 (as until 2026-11-05).)
float towerOn(int L) { return L == 0 && mod(floor(_CloudSpec.w / 4.0), 2.0) < 0.5 ? 1.0 : 0.0; }
float towerOf(int L, float g, float heapy) { return towerOn(L) * heapy * smoothstep(1.3, 2.1, g) * (1.0 - smoothstep(0.45, 0.8, formMore[L].x)); }
// How much of a cloud's column stands over a place, 0 to 1, from the pattern on the layer's base under it (col, in lengths of
// the noise), the layer's cover there and how much of it is heaps or pads; `pat`: the pattern's reading there before a tower's
// share of the masses' is mixed in (g, as a standard normal), how much of the pattern is covered (most), and how far the copies read have narrowed each kind of reading's spread,
// for the walk's strides (clearFor). `pixel`: how wide a pixel of the picture is there (m), by which the pattern's coarser copies
// are read.
float coverOf(int L, vec3 col, float cover, float heapy, float sheet, float pixel, float above, out vec4 pat, out float gMass)
{
    // (each kind of reading at the copy as coarse as the pixel, divided by how far that copy has narrowed the pattern's spread, so
    // that as much of the sky is covered from any distance: see patternLods)
    vec2 lods = patternLods(pixel), sds = patternSpread(lods);
    float lodR = lods.x, lodG = lods.y, sdR = sds.x, sdG = sds.y;
    // (a field of heaps: the small heaps' pattern as well as the masses', and more of it; a lumpy layer some of it; a sheet the
    // masses' alone)
    float wp = smallOf(heapy, sheet, above);
    // (laid on the face of the cube the layer lies on here, as the first form's lattice is (formWeather), at a place of its own for
    // each layer; near where two faces meet, both, each by its share, taken together so that the spread stays as it is)
    float g = 0.0, w2 = 0.0;
    gMass = 0.0;
    for (int side = 0; side < (formMine < 0.999 ? 2 : 1); side++)
    {
        float w = side == 0 ? formMine : 1.0 - formMine;
        vec2 a = faceOf(col, side == 0 ? formFace : formOther).xy * (7.0 / 207.0) + (L == 1 ? vec2(0.371, 0.613) : vec2(0.0));
        float gm = (textureLod(_CloudPattern, a, lodR).r - 0.5) * 8.0 / sdR, gp = 0.0;
        for (int once = wp > 0.001 ? 0 : 1; once < 1; once++) gp = (textureLod(_CloudPattern, a * 4.0 + vec2(0.29, 0.83), lodG).g - 0.5) * 8.0 / sdG;
        g += w * (gm + wp * gp) / sqrt(1.0 + wp * wp);
        gMass += w * gm;
        w2 += w * w;
    }
    g /= sqrt(max(w2, 1e-6));
    // (the masses' pattern alone, as g is taken: what a tower stands on (towerOf), smooth over a whole heap)
    gMass /= sqrt(max(w2, 1e-6));
    // (and a tower's cover too, as much as it is a tower, since 2026-11-06: with the small heaps' pattern taken at every height
    // (smallOf), a tower's whole column stood only where the small heaps' reading was strong as well, and the towering view's
    // tower was a slim pillar where it had been a broad column (the stills of 2026-11-06, oldnew/). Until then, as with
    // _CloudSpec.w's 128 still, the fade left a tower's upper part on the masses' pattern alone.)
    // (The walk's strides are given the place's own reading, before this (pat.x), and the masses' alone (gMass): see clearFor.)
    float gOwn = g;
    for (int once = smallOld() > 0.5 ? 1 : 0; once < 1; once++) g = mix(g, gMass, towerOf(L, gMass, heapy));
    // (as a share of the sky, spread evenly from 0 to 1: the bell's own share, near enough)
    float u = 1.0 / (1.0 + exp(-1.702 * g));
    // (How much of the pattern is covered for so much of the sky to be cloud at a cumulus's base, once the billows have eaten into
    // it (pattern.py, from billow.py's spread): more than the cover. The covered share's own place in it, from nothing at its edge
    // to whole at its strongest, is how much of the column stands over the place, as a power: more of it thin than thick, as
    // Blackrack's maps are (their cover within cloud falls about four times from its thinnest tenth to its thickest). For more of
    // the sky than about two thirds, more than the whole pattern, so that its weakest place is some way in: a deck with holes.
    // With a share of a fixed width, as at first (0.4), the covered part was whole over most of its width under a cover of a half:
    // slabs with flat tops and upright sides, and nothing for the billows to carve.)
    // (For heaps, a curve of their own: see coverCurve.)
    float most = mostOf(cover, heapy);
    pat = vec4(gOwn, most, sdR, sdG);
    return coverCurve(clamp((u - (1.0 - most)) / max(most, 1e-4), 0.0, 1.0), heapy);
}

// One kind's numbers at a height of its column (hf, 0 at its base to 1 at its top; z, m over the base): how much of a cloud of the
// kind stands there for its column (H: its height curve, which added to the cover, less one, is how deep in the cloud a place
// is), how dense it is there against its densest (D), how dense its densest is (s: light lost a metre, in the walk's units, of
// which Clouds.cs's 0.07 a metre is 1), how hard its edge (1 less the width of the billows' edge) and how deep the billows eat
// in (E). Rows: 0 fog, 1 the rain sheet (nimbostratus), 2 stratus, 3 stratocumulus, 4 cumulus, 5 congestus, 6 altostratus,
// 7 altocumulus. Cumulus, congestus and altostratus are after Blackrack's cumulus, congestus and stratus (their curves through
// his points; his 0.012, 0.006 and 0.0015 a metre); the rest ours, after his.
void rowAt(int r, float hf, float z, float column, out float H, out float D, out float s, out float hard, out float E)
{
    D = 1.0; hard = 0.97; E = 0.65;
    if (r == 0) { H = 1.0 - smoothstep(0.5, 1.0, hf); s = 0.056; hard = 0.6; E = 0.35; }
    else if (r == 1) { H = smoothstep(0.0, 0.05, hf) * (1.0 - smoothstep(0.8, 1.0, hf)); s = 0.086; hard = 0.8; E = 0.4; }
    else if (r == 2)
    {
        // (His stratus' curve and its density rising from the base, as altostratus' (row 6), on a column a third deeper (columnOf):
        // the billows move its top some 46 m each way where they moved ours 11, and a hole's wall is straight for 165 m, not 290,
        // its rim rounded over. As until 2026-11-07 (_CloudSpec.w's 256), level from 0.1 of the column to 0.7 and as dense through:
        // from above, a table, white and flat.)
        if (deckNew() > 0.5) { H = smoothstep(0.0, 0.1, hf) * (1.0 - smoothstep(0.37, 0.973, hf)); D = max(0.15, hf); }
        else H = smoothstep(0.0, 0.1, hf) * (1.0 - smoothstep(0.7, 1.0, hf));
        s = 0.043; hard = 0.9; E = 0.5;
    }
    else if (r == 3 || r == 7)
    {
        H = smoothstep(0.02, 0.12, hf) * pow(1.0 - smoothstep(0.45, 0.98, hf), 0.8);
        D = max(0.3, pow(min(1.0, z / min(600.0, column)), 0.6667));
        s = r == 3 ? 0.086 : 0.057; hard = 0.93;
    }
    else if (r == 6) { H = 0.96 * smoothstep(0.017, 0.114, hf) * (1.0 - smoothstep(0.37, 0.973, hf)); D = max(0.15, hf); s = 0.021; hard = 0.9; }
    else
    {
        // (a heap's water thickens with height as adiabatic water does, from little at its base to all of it at its top, over the whole
        // column, as Blackrack's heaps do (his density curve, straight from nought at the kind's base to one at its top). Over its
        // lowest kilometre and a half alone, as until 2026-11-04 (_CloudSpec.w 2), a heap's lowest 500 m was some three times as
        // dense as his once columns were 2.7 km deep: bases dark and solid where his are grey and soft.)
        D = mod(floor(_CloudSpec.w / 2.0), 2.0) > 0.5 ? max(0.15, pow(min(1.0, z / min(1500.0, column)), 0.6667)) : max(0.1, hf);
        if (r == 4) { H = smoothstep(0.015, 0.09, hf) * pow(1.0 - smoothstep(0.09, 1.0, hf), 0.8); s = 0.171; }
        // (congestus: a deep heap, whose cloud thins less with height and is less dense through)
        else { H = smoothstep(0.015, 0.09, hf) * mix(0.98, 0.39, clamp((hf - 0.09) / 0.79, 0.0, 1.0)) * (1.0 - smoothstep(0.88, 0.98, hf)); s = mix(0.171, 0.086, smoothstep(3000.0, 4500.0, column)); }
    }
}

// Which two rows a kind lies between on the weather's scale (CloudsWeather.Kind: fog 0.01, the rain sheet 0.04, stratus 0.08,
// stratocumulus 0.27, cumulus 0.5 to 0.68 by its depth; in the middle sky altostratus 0.1, altocumulus 0.3), and how far from the
// first: a kind between two is drawn between them, as Blackrack's are.
void kindRows(int L, float kind, out int ra, out int rb, out float t)
{
    if (L == 1) { ra = 6; rb = 7; t = clamp((kind - 0.1) / 0.2, 0.0, 1.0); }
    else if (kind < 0.04) { ra = 0; rb = 1; t = clamp((kind - 0.01) / 0.03, 0.0, 1.0); }
    else if (kind < 0.08) { ra = 1; rb = 2; t = (kind - 0.04) / 0.04; }
    else if (kind < 0.27) { ra = 2; rb = 3; t = (kind - 0.08) / 0.19; }
    else if (kind < 0.5) { ra = 3; rb = 4; t = (kind - 0.27) / 0.23; }
    else { ra = 4; rb = 5; t = clamp((kind - 0.5) / 0.18, 0.0, 1.0); }
}

// The column a layer's kind is drawn over, from its base (m): a lumpy layer's few hundred metres (formWeather), a sheet's whole
// depth, and heaps a third deeper than the weather's layer and never under 2700 m, Blackrack's cumulus column (a heap's top stands
// under its column's top: his cumulus reaches it only where the cover is whole), within the shell (Clouds.cs raises the shell's top
// for it). (Never under 1200 m as first written, a heap under the weather's usual 400 m to 1.2 km layer stood 700 m, his 1.5 to
// 2.2 km: a field of small flat heaps, every one lit through to its base (the false-colour stills of 2026-10-27).)
float columnOf(int L)
{
    // (a low deck's a third deeper than what it uses of its layer (formDeck): his stratus' curve, falling from 0.37 of its column to
    // 0.973, stands its top over the whole cover near 0.81 of it, which is about the weather's own top)
    float column = mix(formKind[L].z * (1.0 + 0.3 * formDeck[L]), max(2700.0, 1.3 * formMore[L].z), formKind[L].x);
    return max(min(column, _CloudShell.y - 50.0 - formMore[L].w), 50.0);
}

// (a column so much of a tower deep, within the shell)
float columnTall(int L, float column, float tower)
{
    return min(column * (1.0 + 1.1 * tower), max(_CloudShell.y - 50.0 - formMore[L].w, column));
}

// (Where a row's height curve is highest, from and to, as shares of the column (rowAt): below, it rises; above, it falls.)
vec2 rowTop(int r) { return r == 0 ? vec2(0.0, 0.5) : r == 1 ? vec2(0.05, 0.8) : r == 2 ? (deckNew() > 0.5 ? vec2(0.1, 0.37) : vec2(0.1, 0.7)) : r == 3 || r == 7 ? vec2(0.12, 0.45) : r == 6 ? vec2(0.114, 0.37) : vec2(0.09); }

// (The most the pattern can read within rho metres of a place on its layer's base (col), on one face of the cube, as coverOf reads
// it (a standard normal), with between wpLo and wpHi of the small heaps' pattern, anywhere along a stride (far: worked out for it
// by clearFor): from the b and a, at the place's own dot, of the copy whose dots are at least as wide as rho and half a dot of the
// coarsest copy coverOf reads along the stride (far.xy, in dots of the finest copy), which hold the most within a dot of each dot
// (Clouds.cs, MostOfPattern). A reading within rho blends dots of its copy whose near edges are no further past the place's own
// dot than rho and half a dot of that copy: all within the three dots by three about the place's own. Never under a dot of the
// finest copy, so that the finest copy, whose b and a are its own r and g, is not read; no coarser than the coarsest copy, one
// dot, which holds the most of the whole (as the stand-in where there is no pattern does, one grey dot: Clouds.cs, NoPattern).
// Each reading is divided by its own copy's spread, the place's own (sdR, sdG) or, further on, less (far.zw, the far end's): the
// larger quotient. (As first written, a dot of the finest copy for all of it, which holds for the two finest copies alone: far
// off, where coverOf reads the third and coarser, a reading could take in a dot outside the block and read over the bound some
// 300 to 400 km off: the masses by 0.15 of their spread on tries of about 500 m (the review of the evening of 2026-10-27), and
// on the shortest by 0.4, and the small heaps by nearly 2 (the check of 2026-10-28).)
float patternMost(int L, vec3 col, float side, float rho, vec4 far, float sdR, float sdG, float wpLo, float wpHi, out float massMost)
{
    vec2 a = faceOf(col, side).xy * (7.0 / 207.0) + (L == 1 ? vec2(0.371, 0.613) : vec2(0.0));
    ivec2 n0 = textureSize(_CloudPattern, 0);
    float dot0 = (207.0 / 7.0) * _CloudNoise.w / float(n0.x), top = floor(log2(float(n0.x)) + 0.5);
    int kR = int(min(ceil(log2(rho / dot0 + far.x)), top));
    // (each level's size from the finest's: Direct3D's port gives the finest's size whatever level is asked (port.py's TextureSize),
    // and the reading then fell outside the coarser level and was nought, the bound nothing (the review of 2026-10-27))
    ivec2 nR = max(n0 >> kR, ivec2(1));
    float bR = texelFetch(_CloudPattern, min(ivec2(fract(a) * vec2(nR)), nR - 1), kR).b - 0.5;
    float gm = bR * 8.0 / (bR > 0.0 ? far.z : sdR), gp = 0.0;
    massMost = gm;
    for (int once = wpHi > 0.001 ? 0 : 1; once < 1; once++)
    {
        int kG = int(min(ceil(log2(4.0 * rho / dot0 + far.y)), top));
        ivec2 nG = max(n0 >> kG, ivec2(1));
        float bG = texelFetch(_CloudPattern, min(ivec2(fract(a * 4.0 + vec2(0.29, 0.83)) * vec2(nG)), nG - 1), kG).a - 0.5;
        gp = bG * 8.0 / (bG > 0.0 ? far.w : sdG);
    }
    // (at the most of either end of the share of the small heaps' pattern, or between them, where the two readings are taken in
    // the share of their own sizes)
    float ends = max((gm + wpLo * gp) / sqrt(1.0 + wpLo * wpLo), (gm + wpHi * gp) / sqrt(1.0 + wpHi * wpHi));
    return gm > 0.0 && gp > 0.0 && gp >= wpLo * gm && gp <= wpHi * gm ? sqrt(gm * gm + gp * gp) : ends;
}

// How far along the line of sight the nearest cloud of a layer is at the least (m), from a place in clear air within its column,
// for the walk's strides (heapsNear). There is none within a stride of R where, all along it, the pattern cannot read what the
// column would need for cloud. Along R the line climbs or sinks so far, and the planet falls away under it a little; between those
// heights each kind's height curve is at most its value at the height nearest its highest (rowTop), and the cloud's kind may be
// the stronger heaps' (fieldAt), if the pattern reads so much. What the pattern reads within the stride's reach is at most the
// most its copies hold there (patternMost); and it rises along the line at most as fast as across the ground (pattern.py: its
// 99.9 % point, a metre), with a heap's lean, the broad weather's bend (wm.yzw: from the large noise's cells as broadAt reads them,
// reckoned at a third of a metre a metre along the line at the most) and more where two faces of the cube meet; with _CloudSpec.w's
// 128 (as until 2026-11-06), low in a heap's column, how much of the small heaps' pattern is taken changes with height as well. Where a stride of R cannot be taken whole,
// as much of it as the pattern needs to rise to what the column needs, at that rate; and tried again shorter, whose heights and
// reach ask less. (Until the evening of 2026-10-27 the second form said only how far off the pattern's covered part was, and
// within it nothing: from 4 km up, inside a field of heaps' column of 2.7 km and above every heap's top, a line of sight crept over
// every covered patch a step at a time, and a storm 15 km off was drawn in pieces, its head with a square notch cut out of it
// (the towering view's stills). With this its head stands whole; the far lines still ran out under level, which the walk's
// steps behind time mend (see the walk).) And no further than the walk's next reading of the weather (formAhead): past it the
// layer's kind, base, column and cover, the gatherings and the cube's faces and their shares are that reading's, which is not
// made yet. (Run on past it, as at first, a stride could pass over the next reading's cloud where its column stood taller or its
// cover was more, and near the cube's corners a face it had not read: the review of the evening of 2026-10-27, through a change
// of kind, 1.6 % of strides held the next reading's cloud (0.24 % passed over it whole), and 7-18 % just under a column's top
// (2-10 % whole). Held to the reading, these strides pass over none in a model of the walk on the pattern, in as many steps: see
// the walk.)
float clearFor(int L, vec3 col, vec4 pat, float gMass0, float hf, float above, float h, float column, float heapy, float sheet, float kind, float lod, float pixel)
{
    float c = clamp(formClimbs, -1.0, 1.0), g0 = pat.x, wp0 = mix(1.0 - sheet, 0.6, heapy);
    // (the pattern's covered share as the most cover within the stride asks it (formStep's formCoverHi), not the place's own)
    float most = max(pat.y, mostOf(formCoverHi[L], heapy));
    float spread = 1.4 * (formMine < 0.999 ? 1.6 : 1.0);
    int ra0, rb0;
    float t0;
    kindRows(L, kind, ra0, rb0, t0);
    float R = min(clamp(5000.0 * exp2(lod), 600.0, 4000.0), formAhead), best = 0.0;
    // (where a tower may stand within the stride at all (towerOn, a layer with heaps, the least cover in reach under a deck's), the
    // masses' pattern alone bounds a tower's cover; its steepness from pattern.py, with the lean and the bends as the rest's)
    float towerAll = towerOn(L) * heapy * (1.0 - smoothstep(0.45, 0.8, formCoverLo[L]));
    bool towers = smallOld() < 0.5 && towerAll > 0.0;
    float rateM = spread * 0.00248;
    // (for patternMost, the coarsest copies coverOf reads along the stride, those of a pixel as wide as at the far end of the longest
    // try (`pixel` here, wider by formPer each metre on): half a dot of each, in dots of the finest copy and never under one, and
    // their spreads, the least along the stride; while those are the finest copies, as within some tens of kilometres, one dot and
    // the place's own spreads (pat.zw, coverOf's here), without asking)
    vec2 lods = patternLods(pixel + R * formPer);
    vec4 far = vec4(1.0, 1.0, pat.zw);
    for (int once = lods.y > 0.0 ? 0 : 1; once < 1; once++) far = vec4(exp2(max(ceil(lods) - 1.0, 0.0)), patternSpread(lods));
    // (none where the stride may go nowhere, on the reading's end itself: the claim is nought either way, and a try of nought
    // would read the finest copy (the review of 2026-10-28))
    for (int k = 0; k < (formClimbs <= 1.5 && R > 0.0 ? 3 : 0); k++)
    {
        float lo = above + min(0.0, c * R), hi = above + max(0.0, c * R) + R * R / (2.0 * (_CloudCam.w + h));
        float wp = smallOf(heapy, sheet, max(lo, 0.0)), wpLo = smallOf(heapy, sheet, max(hi, 0.0));
        // (the patterns' steepness from pattern.py, as the shader reads them, the bytes bilinearly between their dots (until the
        // evening of 2026-11-06 measured on the field before it was kept in bytes, 2-4 % less): the masses 0.00248 a metre,
        // the small heaps 0.00413 at 2 km and over, 0.01420 at 330 m as until 2026-11-06 (_CloudSpec.w's 128); and only then, low
        // in a heap's column, the change of its share with height (smallOf))
        float rate = spread * (0.00248 + wp * (smallOld() > 0.5 ? 0.01420 : 0.00413)) / sqrt(1.0 + wp * wp) + (lo < 900.0 ? abs(c) * (4.0 + 4.0 * wp0) * wp0 * heapy * 0.00167 * smallOld() : 0.0);
        // (the most the pattern reads within the stride's reach, on the face or faces it is read on here, joined by their shares as
        // coverOf joins them: within the reading the faces and their shares are the same all along the stride. (Until the night of
        // 2026-10-27, when the strides ran on past the reading, both faces' most wherever the stride might reach where two are taken
        // together, joined to hold for any shares, sqrt(a^2 + b^2), as a stride read with the place's own shares had dropped faint
        // heap tops near the cube's edges (the review of that evening): wider than need be, and near the cube's corners a third
        // face was still not read.))
        float am, bm;
        float a = patternMost(L, col, formFace, 1.4 * R, far, pat.z, pat.w, wpLo, wp, am), gHi = a, massHi = am;
        for (int other = formMine < 0.999 ? 0 : 1; other < 1; other++)
        {
            float b = patternMost(L, col, formOther, 1.4 * R, far, pat.z, pat.w, wpLo, wp, bm);
            gHi = (formMine * a + (1.0 - formMine) * b) / sqrt(formMine * formMine + (1.0 - formMine) * (1.0 - formMine));
            massHi = (formMine * am + (1.0 - formMine) * bm) / sqrt(formMine * formMine + (1.0 - formMine) * (1.0 - formMine));
        }
        float gTop = min(g0 + rate * R, gHi);
        // (Where a tower may stand, coverOf takes its cover from between the place's own reading and the masses' alone, as much as
        // it is a tower (since 2026-11-06): at most the more of the two, and the place's own wherever the masses read under a
        // tower's 1.3. So the masses' most within the stride (by their rate from here, and by the copies' most) counts as well
        // where it passes 1.3. Not by how fast the tower's share changes, as first written: that held strides near heaps to a
        // third (the review of 2026-11-06). Towers taken as standing whatever the cover along the stride: it changes within one.)
        float mTop = min(gMass0 + rateM * R, massHi);
        if (towers && mTop > 1.3) gTop = max(gTop, mTop);
        int ra1, rb1;
        float t1;
        // (a tower as tall and as near congestus as the most the pattern reads within the stride makes one (towerOf): its heights are
        // looked at over every column from the layer's own to that, lo over the tallest to hi over the layer's own)
        // (towers stand on the masses' pattern alone (fieldAt): bounded by the most of either reading)
        // (towerOf's share for a deck (cover from 0.45) taken at the least cover within the stride (formCoverLo): until 2026-11-06
        // the place's own, and across the streets the cover can fall to a tower's within a stride (that day's review))
        float towerHi = towerAll * smoothstep(1.3, 2.1, max(gTop, massHi)), columnHi = columnTall(L, column, towerHi);
        kindRows(L, kind + 0.08 * smoothstep(0.85, 1.0, coverFrom(gTop, most)) * heapy + 0.18 * towerHi, ra1, rb1, t1);
        // (each kind's most between the two heights: the first kind, the second, and the stronger heaps' second where it is another)
        float A = 0.0, B = 0.0, C = 0.0;
        for (int i = 0; i < (rb1 != rb0 ? 3 : 2); i++)
        {
            int r = i == 0 ? ra0 : i == 1 ? rb0 : rb1;
            vec2 top = rowTop(r);
            float x = clamp(clamp(hf, top.x, top.y), lo / columnHi, hi / column), H, D, s, hard, E;
            rowAt(r, x, x * column, column, H, D, s, hard, E);
            if (i == 0) A = H; else if (i == 1) B = H; else C = H;
        }
        float Hm = max(mix(A, B, t0), rb1 != rb0 ? max(B, mix(B, C, t1)) : mix(A, B, t1));
        // (what the pattern would have to read for so much of the column, and how far it is from that at the rate)
        // (the covered share's place for it, under either curve (coverCurve): the heaps' is under 0.04 + 0.9 v^2, and a mix of the
        // two is under the more of them, so that the less of the two places bounds it)
        float T = max(1.0 - Hm, 0.0), v = pow(T, 0.625);
        for (int once = heapy * heapsNew() > 0.0 ? 0 : 1; once < 1; once++) v = min(v, sqrt(max(T - 0.04, 0.0) / 0.9));
        float u = 1.0 - most + most * v;
        float need = u <= 0.0 ? -1e9 : log(min(u, 0.99999) / max(1.0 - u, 1e-5)) / 1.702;
        if (gTop < need) { best = max(best, R); break; }
        float fits = (need - g0) / rate;
        // (and where a tower may stand within it, no further than the masses can rise to the more of what the column needs and a
        // tower's 1.3: past it the place's own reading alone)
        if (towers && mTop > max(need, 1.3)) fits = min(fits, (max(need, 1.3) - gMass0) / rateM);
        best = max(best, min(R, fits));
        R = clamp(2.0 * fits, 0.125 * R, 0.5 * R);
    }
    return best;
}

// One layer's cloud at a place (q: in the noise; h: m over the radius; above: m over the layer's base, under its column's top),
// as layerAt asks it of formAt in the first form, and what it says by the way (heapsNear and the rest: see cloudAt).
float fieldAt(int L, vec3 q, float h, float above, float column, float kind, float wet, vec4 wm, bool fine, float lod, bool seek)
{
    float N = _CloudNoise.w;
    vec4 K = formKind[L];
    float heapy = K.x, sheet = K.y, hf = above / column;
    // (no far field: the same shapes at every distance, the pattern and the billows read as coarsely as a pixel is wide there)
    farGrow = 0.0; formFade = 1.0;
    // ---- the place on the layer's base under this one, moved as the first form's columns are (formAt): downwind as much as a
    // cloud leans by this height, and bent over kilometres; and how much of the column stands over it. (A tower (towerOf) leans
    // no further above the layer's own column: it rises straight up from there, as a tower's updraft does. Leaning all the way,
    // as at first (2026-11-05), a tower twice the column's depth stood its top up to a kilometre downwind of its base, over
    // clear air, and the masses' pattern there, read so far off, thinned the overhang through to the sky.)
    vec3 col = q - formUp * (above / N) - _CloudDrift.xyz * (min(above, column) * K.w / N) + wm.yzw * 0.6 + vec3(0.37, 0.71, 0.13) * float(L);
    float pixel = 25000.0 * exp2(lod) * formPer;
    vec4 pat;
    float gMass;
    float F = coverOf(L, col, formMore[L].x, heapy, sheet, pixel, above, pat, gMass);
    // ---- a tower's column here (towerOf), and the kind's numbers (the strongest heaps of a field a little nearer congestus, a
    // tower much nearer); the walk's strides are bounded by the layer's own column and the tallest tower within them (clearFor)
    // (By the masses' pattern alone, not the place's own with the small heaps': with them, as at first (2026-11-05), a tower's
    // height fell wherever the small heaps' pattern dipped within it, and a tall one had notches cut down into it and holes
    // through it, the sky seen through its shoulder.)
    float column0 = column, hf0 = hf, tower = towerOf(L, gMass, heapy);
    column = columnTall(L, column, tower);
    hf = above / column;
    int ra, rb;
    float t;
    kindRows(L, kind + 0.08 * smoothstep(0.85, 1.0, F) * heapy + 0.18 * tower, ra, rb, t);
    float Ha, Da, sa, ha, Ea, Hb, Db, sb, hb, Eb;
    rowAt(ra, hf, above, column, Ha, Da, sa, ha, Ea);
    rowAt(rb, hf, above, column, Hb, Db, sb, hb, Eb);
    float H = mix(Ha, Hb, t), D = mix(Da, Db, t), s = mix(sa, sb, t), hard = mix(ha, hb, t), E = mix(Ea, Eb, t);
    // (A sheet's billows eat twice as deep over its upper part, wholly from 0.6 of its column up (2026-11-08, TODO 16). As deep as
    // below, a deck's top had about half the relief of Blackrack's: seen from 4.5 km up, his stratus held at his cover and heights
    // (CloudsStandIn's st=) ran on to the horizon as a field of domes where ours lay flat a few kilometres out. Its billows read at
    // full detail far off, the sun's light over it at full detail and five steps far off, and the clouds' detail "higher" left it
    // as flat; deeper billows alone lumped it (development trials 280-286). The base, by whose billows the cover is reckoned
    // (mostOf), as before. _CloudSpec.w's 4096 sets it back. How much more, since 2026-11-09 a knob (_CloudDeck2.z, Clouds.DeckRelief2:
    // E times one and it; 1, twice as deep, as first written): his deck's shading grows with distance, ours fell off, the relief
    // that shows far off still less than his (TODO 16).)
    E *= 1.0 + formDeck[L] * sheet * smoothstep(0.45, 0.6, hf) * deckDeep() * (_CloudDeck2.z > 0.0 ? _CloudDeck2.z : 1.0);
    // ---- how deep inside the cloud
    float inside = H + F - 1.0;
    if (seek) return inside > -0.3 ? 1.0 : 0.0;
    if (inside < 0.01)
    {
        // (how far off the nearest cloud of this layer is along the line: for the walk's own step alone (formClimbs told), not the
        // light's readings, which do not stride)
        for (int once = formClimbs <= 1.5 ? 0 : 1; once < 1; once++) heapsNear = max(heapsNear, -clearFor(L, col, pat, gMass, hf0, above, h, column0, heapy, sheet, kind, lod, pixel));
        return 0.0;
    }
    heapsNear = max(heapsNear, 0.0);
    // ---- the billows, where they can reach: read as coarsely as the pixel (the cube's dots are 17.6 m), and the edge softened as much
    float ramp = max(1.0 - hard, 0.005), shape = 1.0, nHere = 0.62;     // (nHere: the billows' value, their mean where not read)
    for (int once = inside < E + ramp ? 0 : 1; once < 1; once++)
    {
        // (The cube 2.25 km across for the low heaps, its largest cells 1.1 km: half Blackrack's. At his size, on columns of 1.2 to
        // 1.6 km, every heap was a smooth mound or a cone (the probe of 2026-10-27, f2c); on columns as deep as his (columnOf) his
        // size and half of it make heaps alike, but at his the cube's finest octaves are lost in its own dots (17.6 m) and every heap
        // was soft, where his are crisp (the sheets of the evening of 2026-10-27). The middle sky's at half that again: its cloudlets
        // are small.)
        float finer = _CloudLight2.w > 0.0 ? _CloudLight2.w : 1.0;
        // (Read two sizes finer than a pixel, as his are, the edge as crisp at every distance: the picture kept from frame to frame
        // smooths what that leaves of the cube's finest. As coarsely as the pixel and the edge widened with it, as until 2026-11-04
        // (_CloudSpec.w 1), a heap 22 km off had its finest at 35 m and an edge twice as soft, where his finest there is 18 m: far
        // heaps read soft.) And the billows rise through the cloud, at a few metres a second, while the cloud stands: his only life,
        // the edges churning upward all the time (Clouds.cs, Rise).
        float older = mod(_CloudSpec.w, 2.0) > 0.5 ? 0.0 : 2.0;
        float lodB = max(max(log2(max(pixel, 1.0) * finer / (L == 1 ? 4.4 : 8.8)) - older, 0.0) + (fine ? 0.0 : 1.0), billowLight);
        float n = textureLod(_CloudBillow, (q - _CloudSpec.xyz) * ((L == 1 ? 8.0 : 4.0) * finer), lodB).r;
        // (A low deck's billows twice as fine (2026-11-07, TODO 16): at a heap's size its top, seen from 1 to 3 km, was a smooth
        // blur, the cube's largest cells 2.25 km and the smaller ones too shallow to show; twice as fine it is lumped and shaded
        // as his is from as near (Clouds.Light2's billow knob tried at 0.5, 0.7, 1 and 1.4 from 2.5 and 4 km over the craft at
        // deck_above: recordings/2026-11-07-pc/deckknobs). Read beside the heaps' size and mixed by how much of a deck it is, not
        // by changing the one read's size from place to place, which would squeeze its noise into streaks where the kind changes.
        // _CloudSpec.w's 1024 sets it back. And a sheet's at the heaps' size the deeper its column, from 900 m to 1600 m
        // (2026-11-08): twice as fine on a column as deep as his (2.2 km for his 1.7 km of stratus), and twice as deep over its
        // upper part (fieldAt, above), its lumps came out small and knobbly where his are broad soft domes. At the heaps' size the
        // far deck and the middle distance shaded as strongly as his (luma less its blur at 40 px, its spread 9.3 and 10.2 where
        // his is 10.5 and 9.8 and ours was 4.1 and 4.3; trial 287). A deck of the weather's usual 300 to 600 m keeps them twice as
        // fine. _CloudSpec.w's 8192 sets it back.)
        float wFine = formDeck[L] * deckFine() * (1.0 - sheet * smoothstep(900.0, 1600.0, column) * deckWide());
        if (wFine > 0.0)
        {
            float lodF = max(max(log2(max(pixel, 1.0) * 2.0 * finer / (L == 1 ? 4.4 : 8.8)) - older, 0.0) + (fine ? 0.0 : 1.0), billowLight);
            n = mix(n, textureLod(_CloudBillow, (q - _CloudSpec.xyz) * ((L == 1 ? 8.0 : 4.0) * 2.0 * finer) + vec3(0.29, 0.53, 0.17), lodF).r, wFine);
            lodB = mix(lodB, lodF, wFine);
        }
        // (Over a low deck's upper part (formDeck), near enough to be seen, the billows' lows lower and their highs higher: 1.63 times
        // as steep at their mean, which is kept, and within 0 and 1, so that they eat no deeper. Not near the base, by whose billows
        // the cover is reckoned (mostOf); not far off, where they would shimmer.)
        float topBand = formDeck[L] * smoothstep(0.45, 0.6, hf) * (1.0 - smoothstep(2.0, 4.0, lodB));
        n = mix(n, smoothstep(0.1, 1.0, n), topBand);
        shape = clamp((inside - E * (1.0 - n)) / (ramp * exp2(max(lodB - 1.0 - older, 0.0))), 0.0, 1.0);
        nHere = n;
    }
    if (shape <= 0.0) return 0.0;
    // ---- what the light asks (see heapsBody): as thick as a heap's core; in a sheet, the sheet's own top over the place and how
    // dense it is through
    heapsOpen = -1.0; heapsInside = -1e9; heapsSmooth = -1e9;
    // (y, read only by the first form's light, carries the place's height in its own column for the second's: see the walk's light)
    heapsBody = vec4(1e5, hf, sheet > 0.01 ? column - above : -1.0, sheet > 0.01 ? s * D : 0.0);
    heapsDeck = vec2(sheet, formMore[L].w);
    float c = shape * s * D;
    // ---- what the second form's light asks (see formLight): how deep in the cover, how far past the billows' edge; how much of the
    // sky it sees through the cloud over it, up to its own top here; and how bright its light scattered many times over is (his
    // stratus' 0.8, as St and As are drawn). (Its top here: where the column's curve has fallen to one less the cover, which for a
    // heap's (falling from its peak at 0.09 of the column to nothing at its top) is near 0.09 + 0.91 of the cover; and in the upper
    // part, where the curve falls about 1.3 of the cover in a column's height, the depth inside over 1.3, whichever is the more. By
    // the depth inside alone, a heap's base, where it has only begun, was taken to have little over it and was lit as its top.
    // The cloud on the way as dense as half way between here and the heap's own, its upper part eaten into; the billows' crowns
    // more open than their creases.)
    // (A low deck's sheet after his stratus (formDeck): its top here where his curve, falling from 0.37 of the column to 0.973, has
    // fallen to the cover less what the billows eat on the mean, and in the upper part the depth inside over 1.66, the curve's fall in
    // a column's height. Taken as a heap's, as until 2026-11-07, the 120 m of clear air over a deck's top was counted as cloud and
    // the top's sky a fifth too dim.)
    float st = formDeck[L] * sheet;
    float topHf = mix(0.09 + 0.91 * F, 0.37 + 0.6 * max(F - E * 0.38, 0.0), st);
    float upTo = min(clamp(max(inside / mix(1.3, 1.66, st), topHf - hf), 0.0, 1.0) * column, max(column - above, 0.0));
    float Ba = ra == 2 || ra == 6 ? 0.8 : 1.0, Bb = rb == 2 || rb == 6 ? 0.8 : 1.0;
    formLight = vec4(inside, inside - E * (1.0 - nHere), exp(-s * mix(D, 1.0, 0.5) * _CloudLook.x * upTo * 0.6) * mix(0.55, 1.0, nHere), mix(Ba, Bb, t));
    // (fog: drawn only from well over its top, as the first form's is (formAt))
    for (int fog = L == 0 && kind < 0.03 ? 0 : 1; fog < 1; fog++)
        c *= mix(1.0, smoothstep(formMore[0].w + column + 1600.0, formMore[0].w + column + 2400.0, _CloudHigh.x), 1.0 - smoothstep(0.015, 0.03, kind));
    // (rain clouds are thicker and darker underneath)
    return c * (1.0 + wet * (1.0 - hf));
}

// How much cloud of one layer there is at a point: q is the point in the noise (lengths of the large noise), h its height
// above the radius; then the layer: how much of the sky it covers, what kind, its base and top, how wet. "fine" says
// whether the small noise is used (it is left out in the light's walk and far away); lod which size of the large noise to
// read (0 its full size, below 0 nearer than that; the small noise is read 2.2 sizes finer).
float layerAt(vec3 q, vec3 p, float field, float h, float cover, float kind, float base, float top, float wet, vec4 wm, bool fine, float lod)
{
    // (a size of the noise past 50 says: seeking. Is there any cloud within a long stride of here? See the walk.)
    bool seek = lod > 50.0;
    if (seek) lod -= 100.0;
    // (the second form draws each kind over a column of its own: see columnOf; and a heap's tower over up to 2.1 times it, up to
    // whose top the layer is looked in: see towerOf. The tower's share for a deck taken at the least cover within a stride
    // (formCoverLo), as clearFor takes it, and not at the place's own: from over a street's crest (cover 0.85, no tower) the
    // line could be told it was over the top of everything and stride through a tower in the trough beside it (0.7), the
    // review of 2026-11-06.)
    float columnTwo = 50.0;
    for (int once = FORM_TWO > 0.5 ? 0 : 1; once < 1; once++)
    {
        int Lt = field > 1.5 ? 1 : 0;
        columnTwo = columnOf(Lt);
        top = base + columnTall(Lt, columnTwo, towerOn(Lt) * formKind[Lt].x * (1.0 - smoothstep(0.45, 0.8, formCoverLo[Lt])));
    }
    top = max(top, base + 50.0);
    float hf = (h - base) / (top - base);
    if (hf <= 0.0 || hf >= 1.0)
    {
        float off = hf <= 0.0 ? base - h : h - top;
        // (Not held to the weather read for the step (formAhead), as clearFor's strides are: tried on the evening of 2026-11-06,
        // a line over a deck or between the layers then went a reading, 1200 m, a step, and a near-level one from 3 km spent 80 of
        // its 88 steps where it had spent 25, and 44 of 44 by 47 km on the lower setting: the far deck lost again. Past the
        // reading the next one's base, column and cover are taken as this one's, as they always were.)
        heapsNear = max(heapsNear, -(_CloudForm.x > 0.5 ? alongTo(off, h, hf >= 1.0) : off));
        return 0.0;
    }
    cover = clamp(cover * _CloudLook.z, 0.0, 1.0);
    // (No cover here: the next cloud of this layer is where the weather's map gives it some, which changes over kilometres.
    // Said to be 200 m off wherever this layer's heights had none, as it was until 2026-10-15, a line of sight running a
    // little above level through such heights strode 200 m at a time, spent its steps by 13 km, and never reached the deck
    // above beyond that: the owner's "strip of light blue in the distance". A tenth of the distance far off.)
    if (cover <= 0.001) { heapsNear = max(heapsNear, -max(200.0, 2500.0 * exp2(lod))); return 0.0; }
    // (A thin layer comes in with its cover, from nothing at the level below which it is left out (here, and for the middle
    // clouds in the walk, 0.004), and is whole from about three in a hundred. The weather's map is read bilinearly between its
    // dots, and a level cuts such a reading along straight lines with steps where the dots are; a layer that began at full
    // strength there was cut along them. From inside a thin layer every line of sight begins in it, and the whole view parted
    // along such a line: altocumulus at 0.02 between 4.8 and 5.1 km and the camera at 5 km, a straight-edged veil over the sea
    // along the coast and clear sea beside it (the "seam" of 2026-10-20; none from 3 km or 7 km).)
    float comes = field > 1.5 ? smoothstep(0.004, 0.03, cover) : smoothstep(0.001, 0.012, cover);
    // (clusters, gaps and streets; a sheet over the whole sky is left whole: they are for skies of separate heaps)
    // (as a power, not a factor: a cluster thickens the cover towards all of the sky but never to all of it, where a factor
    // filled the layer from base to top in slabs with flat tops and straight sides)
    // (the shapes of the rework of 2026-10-12, where they are asked for, which work out for themselves what gatherings
    // and streets do to the cover: the rest of this is how it was before)
    // (the shapes of the rework of 2026-10-12, which work out for themselves what gatherings and streets do to the cover.
    // The shapes from before it, kept until 2026-10-16 for a development switch, are gone: Windows' compiler, which puts every
    // function inline, made a copy of them for every place a cloud was asked for.)
    if (FORM_TWO > 0.5) return fieldAt(field > 1.5 ? 1 : 0, q, h, h - base, columnTwo, kind, wet, wm, fine, lod, seek) * comes;
    return formAt(field > 1.5 ? 1 : 0, q, h, kind, wet, wm, fine, lod, seek) * comes;
}

// The storms near the camera by number (0 the nearest), and how many there are. They are walked in a loop whose length is
// not known until the picture is drawn, and so is written out once: written out six times over, one after another (as until
// 0.3.0), the six copies of a storm's tower cost a sky with no storm in it two thirds of its time on this Mac's graphics card.
vec4 stormA(int k) { return k < 3 ? (k == 0 ? _CloudCellA0 : k == 1 ? _CloudCellA1 : _CloudCellA2) : (k == 3 ? _CloudCellA3 : k == 4 ? _CloudCellA4 : _CloudCellA5); }
vec4 stormB(int k) { return k < 3 ? (k == 0 ? _CloudCellB0 : k == 1 ? _CloudCellB1 : _CloudCellB2) : (k == 3 ? _CloudCellB3 : k == 4 ? _CloudCellB4 : _CloudCellB5); }
vec4 stormC(int k) { return k < 3 ? (k == 0 ? _CloudCellC0 : k == 1 ? _CloudCellC1 : _CloudCellC2) : (k == 3 ? _CloudCellC3 : k == 4 ? _CloudCellC4 : _CloudCellC5); }
int storms() { return _CloudCellA0.w <= 0.0 ? 0 : _CloudCellA1.w <= 0.0 ? 1 : _CloudCellA2.w <= 0.0 ? 2 : _CloudCellA3.w <= 0.0 ? 3 : _CloudCellA4.w <= 0.0 ? 4 : _CloudCellA5.w <= 0.0 ? 5 : 6; }

// (How wide a pixel of the picture is at the storms' own shapes, at 25 km times two to the power lod: as the layers' shapes have
// it (formPer), a dot of what is kept where that is finer than the walk's picture. Worked out from the width of the walk's own
// picture alone, as until the evening of 2026-10-18, it was four times as wide with the walk at a quarter size (as the game has it
// from orbit) and twice at half, and every storm's surface as much softer: a hurricane's outer towers seen from orbit were smooth
// grey pebbles, and its core under the ice a grey ring (the lab: white, and lumpy, with the walk at the picture's size).)
// (The `lod` the walk draws the place at, for the time of its light towards the sun, whose readings are coarser: the storms'
// shapes soften their surface by no wider a pixel than the walk's own. Read as coarsely as the light reads them, a storm's
// surface was softened over twice the width, half of it outside, and from orbit the light from its top went through a haze of
// its dense cloud a kilometre and more over it: a grey ring round a hurricane's eye seen aslant, where its core's top stands
// through the canopy. 100 elsewhere: no limit.)
float stormLod = 100.0;
float stormPixel(float lod) { return 25000.0 * exp2(lod) * (_CloudForm.x > 0.5 ? formPer : 2.0 * length(_CloudRight.xyz) / max(_CloudHigh.w, 1.0)); }

// One storm or shower. Where there is a field of heaps made beforehand: a tower of rising air round its middle, widest at the
// base, with the field's heaps crowded on it and round it (all of them at the middle, fewer outward: the flanking towers of a
// real storm, each lower than the last), lumps carved on the lot; and at its top the anvil, ice spread out downwind under the
// height where the air stops rising, thin and streaked. Where there is no field, as before 0.3.0: a tower and its anvil
// carved from the noise.
// One storm, in the shapes of the rework of 2026-10-12 (see formAt): a thundercloud as it is built. The great tower over
// the rising air, an egg kilometres across that reaches the top of the weather, leaning with the wind; beside it on the
// side the storm feeds from, a line of lesser towers, each lower than the last (the next ones growing); on all of them
// lumps of five and six sizes, the largest some hundreds of metres across. Where the tower meets the height it cannot
// pass it spreads into the anvil: ice, drawn out downwind, far wider than the tower, thin, soft-edged and smooth above,
// with a dome where the tower's own top overshoots it.
float cellNew(vec3 p, float h, vec3 q, vec4 a, vec4 b, vec4 wm, bool fine, float lod)
{
    float N = _CloudNoise.w;
    float deep = max(b.y - b.x, 100.0), above = h - b.x, frac = above / deep;
    bool seek = lod > 50.0;
    if (seek) lod -= 100.0;
    float pixel = stormPixel(lod), pixelC = stormPixel(min(lod, stormLod));
    float stride = 0.03 * 25000.0 * exp2(lod - 1.0);
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    vec3 wind = _CloudDrift.x * _CloudFixedX.xyz + _CloudDrift.y * _CloudFixedY.xyz + _CloudDrift.z * _CloudFixedZ.xyz;
    wind -= mid * dot(wind, mid);
    float blowing = length(wind);
    // (two ways along the ground at the storm: downwind, and to the right of that; with no wind to speak of, any two)
    vec3 along = blowing > 0.05 ? wind / blowing : normalize(cross(mid, vec3(0.31, 0.57, 0.76)));
    vec3 beside = cross(mid, along);
    float reach = a.w, strength = smoothstep(0.15, 0.5, b.z);
    vec3 flat_ = p - mid * dot(p, mid);
    vec2 at2 = vec2(dot(flat_, along), dot(flat_, beside));
    if (dot(at2, at2) > 9.0 * reach * reach) { heapsNear = max(heapsNear, 3.0 * reach - length(at2)); return 0.0; }
    // ---- the towers: the great one, leaning downwind by a tenth of its height, and three lesser ones in a line on its
    // upwind flank, to the right of the wind (where a storm in this hemisphere draws its air in)
    float lean = 0.1 * above;
    float R0 = reach * 0.42 * (0.6 + 0.4 * strength);
    float inside = eggAt(length(at2 - vec2(lean, 0.0)), above, R0, 1.04 * deep);
    float size = R0;
    for (int k = 0; k < 3; k++)
    {
        float turn = 2.5 + 0.55 * float(k), far = reach * (0.5 + 0.2 * float(k));
        float R = reach * (0.24 - 0.045 * float(k)) * (0.6 + 0.4 * strength), H = deep * (0.72 - 0.16 * float(k)) * (0.7 + 0.3 * strength);
        float one = eggAt(length(at2 - vec2(cos(turn), sin(turn)) * far - vec2(0.6 * lean, 0.0)), above, R, H);
        if (one > inside) size = R;
        inside = larger(inside, one, 0.12 * reach);
    }
    // (the lumps: the largest a quarter of the great tower's half-width)
    float lump = 0.25 * R0, reachOut = 0.5 * lump;
    // ---- the anvil: from four fifths of the way up, a slab drawn out downwind (its middle a reach and more downwind of the
    // tower, its length twice its width), thickest over the tower and thinning to nothing at its rim
    vec2 an = (at2 - vec2(1.1 * reach * strength, 0.0)) / (vec2(2.6, 1.7) * reach * (0.5 + 0.5 * strength));
    float rim = 1.0 - length(an);
    float under = mix(0.82, 0.74, smoothstep(0.0, 0.6, rim)), over = 0.985;
    float thick = (over - under) * deep;
    // (how far inside the slab: from its rim over the ground, as far as it is wide there, and from its faces)
    float anvil = min(rim * 1.7 * reach * 0.6, min(above - under * deep, over * deep - above));
    if (seek) return max(inside + reachOut, anvil + 0.08 * thick) + 1.5 * stride > 0.0 ? 1.0 : 0.0;
    float near = max((inside + reachOut + 6.0 + 0.6 * pixel) * 0.7, (anvil + 0.1 * thick + 6.0) * 0.7);
    // (No further than 0.15 of its reach is ever said, as its shape is not known closely enough to say more; but far off,
    // a tenth of the distance: under a sky with storms about, every line of sight within three of a storm's reaches of it was
    // held to strides of a kilometre, and one a little above level under a deck spent its steps before it reached the deck,
    // the owner's "strip of light blue in the distance" (2026-10-15). `lod` is the distance, as a power of two of 25 km.)
    heapsNear = max(heapsNear, max(near, -max(0.15 * reach, 2500.0 * exp2(lod))));
    if (near < 0.0) return 0.0;
    // ---- the towers' lumps, as a heap's: round, of three sizes in one reading and three finer near the camera
    vec3 atq = q + wm.yzw * 0.6;
    float ls = 2250.0 / lump;
    vec4 big = textureLod(_CloudShape, atq * ls + vec3(0.47, 0.11, 0.83), max(lod + log2(ls) - 1.0, 0.0) * _CloudHaze.x);
    vec3 domes = 1.0 - (1.0 - big.gba) * (1.0 - big.gba);
    vec3 part = clamp(size / (vec3(1.3, 0.65, 0.33) * lump), 0.0, 1.0);
    float moved = dot(max(domes - 0.715, vec3(-0.3)) * part, vec3(1.0, 0.5, 0.25)) * 1.6 * lump;
    float soft = clamp((big.r - big.g) / max(1.0 - big.g, 0.02), 0.0, 1.0);
    if (fine)
    {
        float cellSmall = lump / 8.0;
        vec3 small = textureLod(_CloudDetail, atq * (N / (cellSmall * 4.0)) + vec3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / (cellSmall / 16.0)) + 0.3, 0.0, 6.0) * _CloudHaze.x).rgb;
        moved += dot(small - 0.48, vec3(1.0, 0.5, 0.3)) * 1.6 * cellSmall;
    }
    float tower = min(inside + moved, above - (soft - 0.4) * 160.0);
    // (the tower's water by height over its base, as a heap's: see condensed; the anvil's ice, which takes over where it is denser,
    // as it was)
    float water = condensed(above);
    float c = clamp(tower / max(12.0, 1.2 * pixelC) + 0.5, 0.0, 1.0) * clamp(0.3 + above / 90.0, 0.0, 1.0) * water;
    // ---- the anvil's own look: ice. Smooth and soft above, drawn out in streaks along the wind, ragged at its rim (the
    // smooth noise stretched along the wind eats into it there), a third as thick as the tower's cloud.
    float streaks = textureLod(_CloudShape, (atq - (along.x * _CloudFixedX.xyz + along.y * _CloudFixedY.xyz + along.z * _CloudFixedZ.xyz) * 0.0) * 0.9 + vec3(0.23, 0.61, 0.07), 1.5 * _CloudHaze.x).r;
    float ragged = anvil - (0.78 - streaks) * 2.2 * thick * (1.0 - smoothstep(0.25, 0.7, rim)) + 0.25 * moved * step(above, (under + 0.3 * (over - under)) * deep);
    float ice = clamp(ragged / max(0.22 * thick, 1.2 * pixelC) + 0.5, 0.0, 1.0) * 0.36 * strength;
    if (ice > c) { c = ice; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = clamp(0.35 + (above - under * deep) / thick, 0.0, 1.0); }
    else { heapsInside = tower; heapsSmooth = min(inside, above - (soft - 0.4) * 160.0); heapsOpen = mix(0.5, 1.0, clamp(0.5 + moved / (0.5 * lump), 0.08, 1.0)) * clamp(0.2 + 0.9 * frac, 0.0, 1.0); heapsBody.y = water; }
    return c * (1.0 + b.w * (1.0 - frac));
}

// (the second form's storm's great tower: its half-width at its base (m), as cellTwo and its rain have it)
float towerTwo(vec4 a, vec4 b, float life) { return max(a.w * mix(0.15, 0.38, smoothstep(0.0, 0.4, life)), max(b.y - b.x, 100.0) / 6.0); }

// The second form's storms (the rebuild on Blackrack's recipe, its step 6; 2026-10-27, research/clouds-r5): a storm drawn as
// the second form's heaps are, by a cover and a column's curve eaten into by the billows, in place of cellNew's eggs and
// slab. Blackrack's storms are three kinds of his over one column 1750 to 11450 m up, painted where his maps put them: a core
// 0.002 a metre on a 3 km tile of his noise, upright to near its top; an anvil's rim, 0.001 on 4 km, crisp; and the anvil's
// trail, 0.001 on 50 km, soft (his clouds.cfg, read 2026-10-27: numbers learned, nothing copied). Ours stand on the weather's
// own cells and live as a storm lives (Model.cs: an hour or so), so that a storm is never a slab on a stalk (the mushroom).
// Their shape was made again on 2026-11-02, after the side-by-side of 2026-10-28, where both judges saw ours as "a can" or
// "stacked drums" with a plate for an anvil, and his as a wide anvil over several stepped towers (research/clouds-r5/
// sidebyside-2026-10-28.md, fault 1):
//  - the great tower: from a heap a seventh of the storm's depth (the gate's opening) climbing to the top by 0.35 of its life;
//    narrow while young, a sixth of the storm's reach or less, at its widest nearly two fifths by four tenths of its life (and
//    never narrower than a sixth of its height); while it climbs, tapering upward as a towering heap does (the congestus'
//    curve), and once it has reached the height where the air stops rising, narrowing upward to three fifths of its base's
//    width under the top (as a column, upright to nine tenths of its height, it was the can); lobes of its own size on it, so
//    that its outline is not a straight wall from afar (the billows' largest cells are a kilometre and a half); four turrets
//    round its upper part, each rising and falling in its turn, on bearings of the storm's own; over the middle, from 0.3 to
//    0.6 of its life, the dome its strongest updraught throws up through the top (Clouds.cs: by how unstable the air is);
//    leaning a tenth of its height downwind; gone from 0.7 to 0.9 of its life;
//  - the lesser towers round it: five, all the way round it on bearings of the storm's own, each its own size and its own
//    way out, from 0.55 to 0.92 of the great one's height, rising and sinking a little in its turn as the cells of a storm
//    of many cells do, and standing as long as the storm stands; so that the storm is a mass of towers, its outline stepping
//    up to the great one;
//  - the anvil: none before 0.35 of its life (the tower has to reach the top first); then a lens of ice under the top,
//    thickest over the tower and three tenths of that at its far end, thinning to nothing at its rim; round over the tower
//    and a little wider than its top (the overhang upwind) when it begins, then spreading out every way as the tower goes
//    on feeding it, fast at first and slower as it goes, as the outflow at a real storm's top does: by half the storm's life
//    1.6 of its reach on every side, by the end 2.4 (at 1.3 by the end, and by the root of its age, it was no wider than the
//    lesser towers' ring at half its life, and the two a cap on a stalk: the pictures of 2026-11-02), while the wind aloft
//    carries it off as far as it has since (Clouds.cs; no more than eight of the tower's widths): what a disc of ice
//    spreading as it is carried sweeps out, the hull of the first one and the last (as a disc carried off alone, it was a
//    plate as wide as the tower's top where the wind aloft is weak); soft through (his trail's tile), streaked along the wind
//    aloft, crisp at its rim (his rim's); it outlives the tower, fading from 0.9 of its life;
//  - all of it comes and goes with the storm's strength over the twentieth above cellAt's cut (0.15), so that nothing pops in
//    or out whole, whatever makes the cell; and is sized by its reach (its strength at its height, the same all its life),
//    not by its strength now, by which its anvil drew back eight kilometres upwind as it weakened (the review of 2026-10-27).
// e: x, which way the wind aloft blows (radians from east towards north); y, how far through its life (0 to 1); z, the
// overshooting dome's height over the top (m, whole) and, in its fraction, the storm's own turn for its turrets; w, how far
// down the wind aloft the anvil reaches (m).
float cellTwo(vec3 p, float h, vec3 q, vec4 a, vec4 b, vec4 e, vec4 wm, bool fine, float lod)
{
    bool seek = lod > 50.0;
    if (seek) lod -= 100.0;
    float life = e.y, fadeS = smoothstep(0.15, 0.2, b.z), reach = a.w, power = smoothstep(4000.0, 12000.0, reach);
    float deep = max(b.y - b.x, 100.0), above = h - b.x;
    float pixel = stormPixel(lod);
    float stride = 0.03 * 25000.0 * exp2(lod - 1.0);
    // ---- where the place is from the storm's middle, along the ground: east and north there (m) (up x the pole is east, as
    // Clouds.cs has it: the pole x up is west, and the anvil went upwind of a westerly jet)
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    vec3 east = cross(mid, _CloudFixedZ.xyz);
    east = dot(east, east) > 1e-6 ? normalize(east) : normalize(cross(mid, vec3(0.31, 0.57, 0.76)));
    vec3 north = cross(east, mid);
    vec3 flat_ = p - mid * dot(p, mid);
    vec2 at2 = vec2(dot(flat_, east), dot(flat_, north));
    // (the wind low down, which leans the tower: in the second form not a unit way but the low wind eased over some seconds,
    // as long as its speed over 2 m/s and no longer than 1 (Clouds.cs), so that the lean passes through nought where the wind
    // turns about (a unit way flipped in one frame where the model's drift crossed nought, and every tower in sight moved its
    // top two kilometres with it: the second review of 2026-10-27); the wind aloft, which carries the anvil)
    vec3 wind = _CloudDrift.x * _CloudFixedX.xyz + _CloudDrift.y * _CloudFixedY.xyz + _CloudDrift.z * _CloudFixedZ.xyz;
    vec2 low = vec2(dot(wind, east), dot(wind, north));
    vec2 aloft = vec2(cos(e.x), sin(e.x)), side = vec2(-aloft.y, aloft.x);
    // ---- the great tower: how wide, how high it has climbed (m over its base), and from its own axis at this height
    float Rt = towerTwo(a, b, life);
    float climbed = deep * mix(0.15, 1.0, smoothstep(0.05, 0.35, life));
    float alive = (1.0 - smoothstep(0.7, 0.9, life)) * fadeS, upright = smoothstep(0.25, 0.45, life);
    float over_ = floor(max(e.z, 0.0)), turn0 = 6.2832 * fract(max(e.z, 0.0));
    vec2 fromAxis = at2 - low * (0.1 * clamp(above, 0.0, climbed));
    float d = length(fromAxis);
    // (the turrets' highest top just now: each rises from 0.8 of the tower's height to a little over it and sinks again, three
    // times in the storm's life)
    float l3 = life * 3.0;
    float sT = max(max(sin(PI * fract(l3)), sin(PI * fract(l3 + 0.29))), max(sin(PI * fract(l3 + 0.58)), sin(PI * fract(l3 + 0.87))));
    float topT = max(climbed + over_, climbed * (0.8 + 0.25 * sT));
    // (the lesser towers: from when the great one is well up until it dies)
    float flankOn = smoothstep(0.1, 0.3, life) * alive;
    // ---- the anvil: along the wind aloft from the tower's top (u), across it (v); spreading out from a little wider than the
    // tower's top as it ages, carried off down the wind aloft; how far outside the hull of its first disc and its last the
    // place is (m, less than nought inside: the uneven capsule's distance, as Inigo Quilez gives it); its greatest thickness
    float anvilOn = smoothstep(0.35, 0.45, life) * (1.0 - smoothstep(0.9, 1.0, life)) * fadeS;
    float r0 = 1.1 * Rt, spread = 2.4 * reach * pow(clamp((life - 0.35) / 0.55, 0.0, 1.0), 0.4), r1 = r0 + spread;
    float gone = min(max(e.w, 0.0), 8.0 * Rt);
    vec2 fromTop = at2 - low * (0.1 * climbed);
    float u = dot(fromTop, aloft), v = dot(fromTop, side);
    float sdA = length(vec2(u - gone, v)) - r1;
    // (carried off further than it has spread: the hull of the two; otherwise the last disc holds every one before it)
    for (int once = gone > spread + 1.0 ? 0 : 1; once < 1; once++)
    {
        float sb = -spread / gone, ca = sqrt(1.0 - sb * sb);
        vec2 pp = vec2(abs(v), u);
        float k = dot(pp, vec2(-sb, ca));
        sdA = k < 0.0 ? length(pp) - r0 : k > ca * gone ? sdA : dot(pp, vec2(ca, sb)) - r0;
    }
    float thickA = mix(800.0, 2000.0, power) * anvilOn;
    // (the anvil his way, below, or as the lens it was until 2026-11-05: _CloudSpec.w's 16)
    bool lensAnvil = mod(floor(_CloudSpec.w / 16.0), 2.0) > 0.5;
    // ---- how far off its cloud can be at the most (for the walk's strides): the great tower's widest, its lobes, its turrets
    // and the overshoot over its top; the lesser towers' ring, lower; and the anvil's hull, between its thickest underside and
    // its top
    float outT = alive <= 0.0 ? 1e9 : max(d - 1.12 * Rt, max(above - topT, -above));
    if (flankOn > 0.0) outT = min(outT, max(d - 2.5 * Rt, max(above - 0.93 * climbed, -above)));
    float outA = anvilOn <= 0.0 ? 1e9 : max(sdA - 0.12 * r1, lensAnvil ? max(deep - thickA - above, above - deep - 60.0) : max(_CloudAnvil.w * deep - above, above - 0.81 * deep));
    float outAll = min(outT, outA);
    if (seek) return outAll < 1.5 * stride ? 1.0 : 0.0;
    float near = (6.0 + 0.6 * pixel - outAll) * 0.7;
    heapsNear = max(heapsNear, max(near, -max(0.15 * reach, 2500.0 * exp2(lod))));
    if (near < 0.0) return 0.0;
    float cT = 0.0, cA = 0.0, insideT = -1.0, eT = -1.0, insideA = -1.0, eA = -1.0, hfT = clamp(above / climbed, 0.0, 1.0);
    // (the height of the top over the place, in the part of the tower it is in, and its height in that part's column: for the light)
    float overT = climbed - above, hfHere = hfT, topA = deep, dA = 0.014;
    // ---- the towers' cloud: the great one's curve with height (the congestus' while it climbs, narrowing upward once up),
    // whole at its axis and none past its radius; the turrets, each a narrower column of its own on its upper part; the
    // lesser towers, each a congestus; the overshooting dome
    for (int once = outT < 6.0 + 0.6 * pixel ? 0 : 1; once < 1; once++)
    {
        float Hc = smoothstep(0.015, 0.09, hfT) * mix(0.98, 0.39, clamp((hfT - 0.09) / 0.79, 0.0, 1.0)) * (1.0 - smoothstep(0.88, 0.98, hfT));
        // (once up, narrowing to two thirds of its width by seven tenths of its height and spreading again from there into the
        // anvil's underside, as a real tower's top does where the air can rise no further: narrowing all the way up, it met the
        // anvil by a neck, and the two were a mushroom)
        float Hu = smoothstep(0.01, 0.06, hfT) * (mix(1.0, 0.68, smoothstep(0.06, 0.7, hfT)) + 0.3 * smoothstep(0.7, 0.92, hfT)) * (1.0 - smoothstep(0.93, 1.0, hfT));
        float H = mix(Hc, Hu, upright);
        // (the lobes, on the billows read at a twelve-kilometre tile, so that their cells are the tower's own size; only as much as
        // the tower is there, so that none stands over its top or past the lesser towers' own)
        float lobes = 0.55 * (textureLod(_CloudBillow, q * 0.75 + vec3(0.31, 0.77, 0.19), max(log2(max(pixel, 1.0) / 46.8), 0.0)).r - 0.62);
        insideT = H + alive * (1.0 - (d / Rt) * (d / Rt)) - 1.0 + lobes * H * alive;
        // (the turrets placed and sized by the column's own width at their foot, half its height (Hc and Hu there: 0.674, 0.754):
        // by its whole width they stood out past it as a crown wider than its base while it climbed and tapered; each swelling
        // out of the column from its foot, and only as much as the column is there at their foot: at full height while the
        // column faded in or out under them, they floated over a stub (the second review). Nearer its axis than they were
        // on the upright column, which is narrower now near its top.)
        float footH = mix(0.674, 0.754, upright), rRef = Rt * sqrt(footH), footOn = clamp((footH + alive - 1.0) / footH, 0.0, 1.0);
        for (int k = 0; k < 4; k++)
        {
            float turn = turn0 + 2.2 + 1.35 * float(k);
            float rk = (0.28 + 0.04 * float(k)) * rRef;
            float dk = length(fromAxis - vec2(cos(turn), sin(turn)) * (0.45 * rRef));
            float topk = climbed * (0.8 + 0.25 * sin(PI * fract(l3 + 0.29 * float(k))));
            float hk = (above - 0.5 * climbed) / max(topk - 0.5 * climbed, 1.0);
            float Hk = smoothstep(0.0, 0.3, hk) * (1.0 - smoothstep(0.55, 1.0, hk));
            insideT = max(insideT, Hk * footOn + alive * (1.0 - (dk / rk) * (dk / rk)) - 1.0);
        }
        // (the lesser towers: all the way round the great one, a fifth of a turn apart and wandering a little from that (on half a
        // turn alone, from the other side they all stood behind it), each its own way out and its own size, standing as long as
        // the storm does and rising and sinking a little in its turn (grown from nothing and gone again in turn, at any time one
        // or two were small and the storm a stalk again))
        for (int k = 0; k < 5; k++)
        {
            if (flankOn <= 0.0) break;
            float own = fract(0.618 * float(k) + 0.37 * fract(max(e.z, 0.0))), own2 = fract(0.382 * float(k) + 0.71 * own);
            float bear = turn0 + 1.2566 * float(k) + 0.3 * sin(2.3 * float(k) + turn0);
            float rk = Rt * (0.6 + 0.15 * own2);
            float dk = length(fromAxis - vec2(cos(bear), sin(bear)) * (Rt * (1.25 + 0.35 * own)));
            float topk = climbed * (0.62 + 0.3 * own2) * (0.85 + 0.15 * sin(6.2832 * (1.5 * life + 0.2 * float(k) + own)));
            float grown = flankOn;
            float hk = clamp(above / topk, 0.0, 1.0);
            float Pk = smoothstep(0.015, 0.09, hk) * mix(0.98, 0.39, clamp((hk - 0.09) / 0.79, 0.0, 1.0)) * (1.0 - smoothstep(0.88, 0.98, hk));
            float one = Pk + grown * (1.0 - (dk / rk) * (dk / rk)) - 1.0 + lobes * Pk * grown;
            if (one > insideT) { insideT = one; overT = topk - above; hfHere = hk; }
        }
        // (the dome: grown in over its first 200 m, its lower part with it: it stood in whole where its height went from nought to
        // a metre (the second review))
        float rO = 0.6 * Rt, domeOn = smoothstep(0.0, 200.0, over_);
        insideT = max(insideT, over_ > 0.5 ? alive * domeOn * (1.0 - (d / rO) * (d / rO)) - max(above - climbed, 0.0) / over_ - step(above, 0.7 * climbed) : -1.0);
        if (insideT < 0.01) break;
        // (eaten into by the billows on his core's tile, 3 km (q is in lengths of 9 km), as crisp as his core; deeper than a heap's,
        // so that the towers' outlines are billowed and not only their faces: a tower is kilometres wide, and at a heap's 0.65
        // the billows moved its edge some 600 m, where its own width moves it four)
        float E = 0.9, ramp = 0.03;
        // (two sizes finer than a pixel and rising, as a heap's: see fieldAt)
        float older = mod(_CloudSpec.w, 2.0) > 0.5 ? 0.0 : 2.0;
        float lodB = max(log2(max(pixel, 1.0) / 11.7) - older, 0.0) + (fine ? 0.0 : 1.0);
        float n = textureLod(_CloudBillow, (q - _CloudSpec.xyz) * 3.0, lodB).r;
        eT = insideT - E * (1.0 - n);
        float shape = insideT >= E + ramp ? 1.0 : clamp(eT / (ramp * exp2(max(lodB - 1.0 - older, 0.0))), 0.0, 1.0);
        // (the water by height over its base, as a heap's; rain darkens its base)
        float D = max(0.15, pow(min(1.0, max(above, 0.0) / min(1500.0, climbed)), 0.6667));
        cT = shape * 0.029 * D * (1.0 + b.w * (1.0 - hfT));
    }
    // ---- the anvil's cloud: a lens under the top, its thickness the most over the tower and three tenths of that at its far
    // end, thinning to nothing at its rim over four tenths of its last disc's half-width in from it (rounded there, it was a
    // puffed cap on a stalk: a mushroom); flat under and a little domed over; soft through (his trail's tile, 50 km), drawn out
    // in streaks along the wind aloft as Blackrack's anvils are, crisp at the rim (his rim's, 4 km) but eaten into less there
    // than through (as deep, it broke into bits that floated over the rim)
    for (int once = lensAnvil && outA < 6.0 + 0.6 * pixel ? 0 : 1; once < 1; once++)
    {
        // (its outline bulging in and out by as much as a ninth of its last disc's half-width, on the billows read at the towers'
        // lobes' tile: as the hull alone, it was a perfect ellipse from afar, a saucer)
        float bulge = 0.3 * r1 * (textureLod(_CloudBillow, q * 0.75 + vec3(0.71, 0.23, 0.47), max(log2(max(pixel, 1.0) / 46.8), 0.0)).r - 0.62);
        float fA = clamp((bulge - sdA) / (0.3 * r1), 0.0, 1.0), along = clamp(length(vec2(max(u, 0.0), v)) / max(gone + r1, 1.0), 0.0, 1.0);
        float T = thickA * fA * (2.0 - fA) * mix(1.0, 0.3, along);
        if (T < 10.0) break;
        topA = deep - 0.25 * (thickA - T);
        insideA = min(above - (topA - T), topA - above) / (0.5 * thickA);
        if (insideA < 0.0) break;
        float rim = 1.0 - smoothstep(0.05, 0.35, fA);
        // (the two tiles as near his as repeat with the noise's own, 207 of its lengths (Clouds.NoiseWrap): 37 and 466 of them to
        // the 207, 50.4 and 4.0 km, so that nothing jumps where the drift is carried back by the 207; the streaks on the billows
        // read in the storm's own ways, 36 km along the wind aloft to 2.5 km across it and up)
        float lodT = max(log2(max(pixel, 1.0) / 196.7), 0.0), lodE = max(log2(max(pixel, 1.0) / 15.62), 0.0) + (fine ? 0.0 : 1.0);
        float streak = textureLod(_CloudBillow, vec3(u / 36000.0, v / 2500.0, above / 2500.0) + vec3(0.13, 0.57, 0.31), max(log2(max(pixel, 1.0) / 9.77), 0.0)).r;
        float n = mix(mix(textureLod(_CloudBillow, q * (37.0 / 207.0), lodT).r, streak, 0.5), textureLod(_CloudBillow, q * (466.0 / 207.0), lodE).r, rim);
        // (crisp over the towers, as his rim's kind, and soft where it has been carried off, as his trail's: soft all through, it
        // was a haze of an ellipse)
        float E = mix(0.4, 0.3, rim), ramp = mix(mix(0.1, 0.5, smoothstep(0.2, 0.9, along)), 0.1, rim);
        eA = insideA - E * (1.0 - n);
        float shape = clamp(eA / (ramp * exp2(max(mix(lodT, lodE, rim) - 1.0, 0.0))), 0.0, 1.0);
        // (denser than his anvils' 0.001 a metre over the towers, as the anvil of a real storm is there (some tens of its depths
        // through a couple of kilometres): at his, it was a haze lit as brightly under as over; his where it has been carried off)
        dA = mix(0.026, 0.014, smoothstep(0.3, 0.9, along));
        cA = shape * dA * anvilOn;
    }
    // ---- the anvil his way (2026-11-05: item 6 of the build order in the owner's spec of Blackrack's clouds, its gap 5,
    // research/clouds-r5/spec-gaps-2026-11-04.md): not a lens drawn round a hull but the same reckoning as every cloud, a cover
    // and a curve by height eaten into by the billows. The cover: whole over the tower's top and falling to nothing at the hull of
    // the first disc and the last (as above), its outline bulging by the billows at the towers' lobes' tile, thinner the further
    // it has been carried. The kind: the anvil's edge by the tower, its trail further out. The curves are ours through the points
    // of his, over the storm's column from its base to its top: the edge's a band from 0.38 (_CloudAnvil.w) to 0.81 of it, highest at 0.72; the
    // trail's from 0.08 higher to 0.81, highest at 0.69, steep above and gradual below. So it is thick where the cover is whole and a
    // thin sheet near the curves' top where there is little: a lens of itself, flat under where the cover falls off. His density,
    // 0.001 a metre (0.0145 in ours, as his core's 0.002 is our towers' 0.029): a dark underside from the light field, the
    // sun's part and the sky's, not from more water. The edge crisp on his edge's 4 km tile, rising with the billows; the trail
    // soft (his hardness 0.5) on his 50 km tile, streaked down the wind aloft, its reading pushed about by kilometres as his curl
    // noise pushes his.
    for (int once = !lensAnvil && outA < 6.0 + 0.6 * pixel ? 0 : 1; once < 1; once++)
    {
        float bulge = 0.3 * r1 * (textureLod(_CloudBillow, q * 0.75 + vec3(0.71, 0.23, 0.47), max(log2(max(pixel, 1.0) / 46.8), 0.0)).r - 0.62);
        float along = clamp(length(vec2(max(u, 0.0), v)) / max(gone + r1, 1.0), 0.0, 1.0);
        float cov = clamp((bulge - sdA) / (_CloudAnvil.x * r1), 0.0, 1.0) * mix(1.0, _CloudAnvil.y, along) * anvilOn;
        if (cov <= 0.0) break;
        float hfS = above / deep, ty = smoothstep(1.3 * Rt, 3.5 * Rt, length(fromTop));
        float lowE = _CloudAnvil.w, lowT = lowE + 0.08;
        float He = smoothstep(lowE, 0.725, hfS) * (1.0 - smoothstep(0.725, 0.81, hfS));
        float Ht = 0.96 * smoothstep(lowT, 0.69, hfS) * (1.0 - smoothstep(0.69, 0.81, hfS));
        insideA = mix(He, Ht, ty) + cov - 1.0;
        if (insideA <= 0.0) break;
        float lodT = max(log2(max(pixel, 1.0) / 196.7), 0.0), lodE = max(log2(max(pixel, 1.0) / 15.62), 0.0) + (fine ? 0.0 : 1.0);
        // (the push: the billows read coarsely at twice the trail's tile, two ways, up to some kilometres along the ground)
        float w1 = textureLod(_CloudBillow, q * (18.5 / 207.0) + vec3(0.41, 0.13, 0.77), lodT + 2.0).r - 0.62;
        float w2 = textureLod(_CloudBillow, q * (18.5 / 207.0) + vec3(0.83, 0.49, 0.21), lodT + 2.0).r - 0.62;
        vec2 uvP = vec2(u, v) + vec2(w1, w2) * (8000.0 * ty);
        vec3 push = (east * w1 + north * w2) * (8000.0 * ty / _CloudNoise.w);
        vec3 pushF = vec3(dot(push, _CloudFixedX.xyz), dot(push, _CloudFixedY.xyz), dot(push, _CloudFixedZ.xyz));
        float streak = textureLod(_CloudBillow, vec3(uvP.x / 36000.0, uvP.y / 2500.0, above / 2500.0) + vec3(0.13, 0.57, 0.31), max(log2(max(pixel, 1.0) / 9.77), 0.0)).r;
        float nT = mix(textureLod(_CloudBillow, (q + pushF) * (37.0 / 207.0), lodT).r, streak, 0.5);
        float nE = textureLod(_CloudBillow, (q - _CloudSpec.xyz) * (466.0 / 207.0), lodE).r;
        float n = mix(nE, nT, ty), ramp = mix(0.04, 0.5, ty);
        eA = insideA - 0.65 * (1.0 - n);
        float shape = clamp(eA / (ramp * exp2(max(mix(lodE, lodT, ty) - 1.0, 0.0))), 0.0, 1.0);
        // (Denser by the tower than along the trail: seen from under it, 2026-11-05, an anvil of his density all through was a
        // bright white sheet overhead, some three e-folds of cloud between its underside and the sun, lit nearly as its top;
        // a real anvil near its storm holds tens, and its underside is grey. By the tower _CloudAnvil.z's hundreds (in tenths) times
        // his, falling to his own density over the first four fifths of its reach; _CloudSpec.w's 32 sets it back.)
        float coreQ = floor(_CloudAnvil.z / 100.0);
        float core = mod(floor(_CloudSpec.w / 32.0), 2.0) > 0.5 ? 1.0 : max(coreQ * 0.1, 1.0);
        dA = 0.0145 * (_CloudAnvil.z - 100.0 * coreQ) * mix(core, 1.0, smoothstep(0.1, 0.8, along)); topA = 0.81 * deep;
        cA = shape * dA;
    }
    // ---- what the light asks (see formLight): a storm's lobes (w < 0); how much of the sky is seen through the cloud over the
    // place (the tower's billows' crowns more open than their creases)
    if (cA > cT)
    {
        formLight = vec4(insideA, eA, exp(-dA * _CloudLook.x * max(topA - above, 0.0) * 0.6), -1.0);
        heapsBody = vec4(1e5, 1.0, -1.0, 0.0);
        return cA;
    }
    formLight = vec4(insideT, eT, exp(-0.029 * _CloudLook.x * max(overT, 0.0) * 0.6) * mix(0.55, 1.0, clamp(eT * 4.0, 0.0, 1.0)), -1.0);
    heapsBody = vec4(1e5, hfHere, -1.0, 0.0);
    return cT;
}

float cellAt(vec3 p, float h, vec3 q, vec4 a, vec4 b, vec4 e, vec4 wm, bool fine, float lod)
{
    if (b.z < 0.15 || a.w <= 0.0) return 0.0;
    // (the second form's: their own shapes, and their own bounds, the dome over the top among them)
    if (FORM_TWO > 0.5) return cellTwo(p, h, q, a, b, e, wm, fine, lod);
    float deep = max(b.y - b.x, 100.0);
    float frac = (h - b.x) / deep;
    // (Under its base or over its top: as far as the line of sight has to go to climb or come down to it, and no nearer than
    // the place is outside the widest the storm reaches, its anvil's rim. Said to be as far as the base is above the place,
    // whatever the distance, as it was until 2026-10-15, a strong storm 33 km off with its base 60 m over the camera held
    // every line of sight at the camera's height to strides of 60 m: from a craft at 2.3 km, a deck above ended in a straight
    // line some 13 km off, clear sky under it to the horizon, the owner's "strip of light blue in the distance".)
    if (frac <= 0.0 || frac >= 1.0)
    {
        vec3 axis_ = a.xyz / max(length(a.xyz), 1.0);
        float outside = length(p - axis_ * dot(p, axis_)) - 3.7 * a.w;
        heapsNear = max(heapsNear, -max(frac <= 0.0 ? alongTo(b.x - h, h, false) : alongTo(h - b.y, h, true), outside));
        return 0.0;
    }
    // (Only the storms of the clouds' rework (cellNew). The old ones, kept until 2026-10-16 for a development switch, were in
    // every copy Windows' compiler made of this, which puts every function inline: a third of a layer's and the old heaps.)
    return cellNew(p, h, q, a, b, wm, fine, lod);
}

// A cyclone's own cloud at a place (see "a cyclone" above; e: the place in it, from eyeAt): its core and its outer bands,
// storm cloud with a surface to it, and over them the canopy, ice.
//
// As photographs have them (the station's of Florence's eye, 2018; a hurricane hunter's from inside Katrina's, 2005). The
// eye's wall is not a cliff of cauliflower: it is a soft white slope, with long terraces and bulges that run round it
// nearly level, sharp against the sky along its lip. Round the eye, for a couple of its widths, the top of the core
// bubbles (the wall's own storms, and the first bands wound tight round it), in arcs that wind round the eye; beyond
// that it is smooth ice, fibrous, drawn out along the way the air flows out, thinning until the sea shows through it.
float cycloneAt(vec3 p, float h, vec3 q, vec4 e, vec4 wm, bool fine, float lod)
{
    float N = _CloudNoise.w, Rm = _CloudEyeA.w, now = _CloudEyeB.x;
    float base = _CloudEyeC.w, deep = max(_CloudEyeB.w - base, 1000.0), above = h - base, frac = above / deep;
    float x = e.x, r = e.z;
    bool seek = lod > 50.0;
    if (seek) lod -= 100.0;
    if (frac > 1.12) { heapsNear = max(heapsNear, 1.12 * deep - above); return 0.0; }
    float pixel = stormPixel(lod), pixelC = stormPixel(min(lod, stormLod));
    float stride = 0.03 * 25000.0 * exp2(lod - 1.0);
    float strength = smoothstep(0.1, 0.3, now), eyeOn = smoothstep(0.35, 0.6, now), rim = eyeRim();
    // ---- under its base, its rain: under the core, heaviest under the wall; grey, in streaks, down to the sea. (The wall of a real eye stands on the sea: there is no clear air under it.) It reaches
    // a hundred metres up past the base, which is ragged: with rain only below the base's own level, the sky showed
    // through between the two in slivers.
    float rain = 0.0;
    for (int once = above < 110.0 && h > 0.0 ? 0 : 1; once < 1; once++)
    {
        float foot = mix(-0.3 * Rm, 0.62 * Rm, eyeOn);
        float falls = smoothstep(foot, foot + 0.12 * Rm, r) * (1.0 - 0.6 * smoothstep(1.2, 2.5, x)) * (1.0 - smoothstep(3.2, 4.0, x));
        if (falls <= 0.0) break;
        float streak = textureLod(_CloudShape, vec3(eyeXY / N * 2.3, h / N * 0.23) + vec3(0.7, 0.1, 0.4), 0.0).g;
        // (streaked, but nowhere without rain: seen through a gap in the low cloud, streaks with clear air between them
        // were a comb)
        rain = 0.05 * strength * falls * (0.45 + 0.55 * smoothstep(0.2, 0.85, streak)) * (0.5 + 0.5 * clamp(h / max(base, 100.0), 0.0, 1.0)) * (1.0 - smoothstep(60.0, 110.0, above));
    }
    if (frac < 0.0)
    {
        // (No rain here: how far off the cyclone's cloud is. In the eye that is how far it is to the wall's foot, not how
        // far it is up to the level of the base, where over the eye there is nothing: told the second, a line of sight
        // along the sea from the middle of the eye went fifty metres at a step, ran out of steps four kilometres on,
        // short of the wall, and showed the sky through under it.)
        float foot = mix(-0.3 * Rm, 0.62 * Rm, eyeOn);
        if (rain <= 0.0) { heapsNear = max(heapsNear, r < foot ? -max(foot - r, -above) * 0.9 : min(above, -40.0)); return 0.0; }
        heapsNear = 1e9; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = 0.3;
        return seek ? 1.0 : rain;
    }
    // ---- the core: from the eye's edge out to four radii, solid. Its inner face is the wall, leaning outward as it rises
    // (0.62 of the radius at the sea, 1.17 at the top: a real eye is about twice as wide aloft); its top stands a little
    // above the canopy round the eye and sinks under it outward. A weak cyclone has no eye: the core is filled in.
    float f = clamp(frac, 0.0, 1.0);
    float inner = mix(-0.3 * Rm, Rm * (0.62 + 0.55 * pow(f, 1.7)), eyeOn);
    // (its top: up through the canopy for two radii round the eye, then down well under it. Where it lay a few hundred
    // metres under the ice all the way out, a line of sight from orbit, whose steps are that long, lit some pixels as
    // ice and some as the cloud under it: a disc of another tone, four radii across, with a hard edge.)
    float coreTop = deep * mix(1.03, 0.6, smoothstep(2.0, 3.6, x));
    float inside = min(min((r - inner) * 0.8, 4.0 * Rm - r), coreTop - above);
    float size = Rm;
    bool core = x < 4.5;
    // ---- the outer bands, from where the canopy thins outward: along each band a row of storm towers, one to every
    // 1.2 radii or so of its length, each an egg of its own width and height standing somewhere across the band's
    // width, and one place in four with none (a row of storms with gaps in it: drawn as one ridge along the band's
    // middle line, as it first was, a band seen from above was a hose laid on the sea); lower the further out
    float from = max(rim - 1.5, 4.6);
    for (int once = x > from - 0.6 && x < 13.5 ? 0 : 1; once < 1; once++)
    {
        float ends = smoothstep(from - 0.5, from + 1.0, x) * (1.0 - smoothstep(10.0, 13.0, x));
        float half_ = Rm * (0.2 + 0.035 * x) * ends;
        if (e.y > 1.6 * half_ + 0.3 * Rm || half_ < 1.0) { inside = max(inside, (1.6 * half_ + 0.3 * Rm - e.y) * 0.8 - 0.3 * Rm); break; }
        // (How far along the band, counted in places for a tower: 39 of them to a turn round the eye, which is one to
        // every 0.37 of the number that grows along a band, about 1.2 radii at eight radii out; numbered so that they
        // come round with the angle. Not by how far out the place is, as it first was: that changes across a band
        // far faster than along it, and every tower was a slab a kilometre thick, sheared flat.)
        float v = eyeAlong / 0.370546, place = floor(v), fv = v - place, which = floor(e.w / PI + 0.5);
        float span = 0.370546 * r / 2.508;
        for (int k = -1; k <= 1; k++)
        {
            vec4 chance = texelFetch(_CloudCells, ivec2(int(mod(place + float(k), 39.0)) + 64, int(mod(which, 2.0)) * 7 + 141), 0);
            if (chance.a < 0.25) continue;
            float Rt = min(half_ * (0.5 + 0.5 * chance.b), 0.48 * span);
            float Ht = deep * mix(0.9, 0.45, smoothstep(rim - 1.0, 12.0, x)) * (0.5 + 0.5 * chance.b);
            vec2 off = vec2((fv - float(k) - 0.3 - 0.4 * chance.r) * span, eyeAcross - (chance.g - 0.5) * 1.3 * half_);
            float one = eggAt(length(off), above, Rt, Ht);
            if (one > inside) size = Rt;
            inside = larger(inside, one, 0.3 * Rt);
        }
    }
    float lump = 0.07 * Rm, reachOut = 0.6 * lump;
    // ---- the canopy: ice, from the wall out to the rim, its top sinking a little outward, its underside rising until
    // at the rim a thin sheet is left; the rim drawn out along the bands, and into streaks that wind the way the bands
    // do, more loosely (the air flows out along them)
    float out_ = smoothstep(1.2, rim, x);
    float under = mix(0.70, 0.872, out_), over = 0.985 - 0.10 * out_;
    // (The bands' shields: the storms along each band throw their ice out aloft, and it runs together into a ribbon along the
    // band that reaches far past the canopy's rim (half and more of the cirrus round a hurricane comes from its bands: Nolan,
    // Fischer and O'Neill). So the canopy is drawn out along the two bands, deeper over a band's middle. From orbit a
    // hurricane is a white spiral, its arms the bands; with the canopy a round disc and the bands rows of single towers on
    // the open sea, it was a dinner plate with pebbles round it.)
    float onBand = (1.0 - smoothstep(0.45, 1.0, e.y / (Rm * (0.600 + 0.080 * x)))) * smoothstep(rim - 2.0, rim, x);
    under = mix(under, 0.76, onBand * out_); over = mix(over, 0.95, onBand * out_);
    float thick = (over - under) * deep;
    float streakTurn = eyeTurn - 1.2 * log(max(x, 0.5));
    // (fibres three degrees wide and some radii long, and with them the rim's wider tongues)
    float fibre = textureLod(_CloudShape, vec3(cos(streakTurn) * 3.2, sin(streakTurn) * 3.2, x * 0.07) + vec3(0.23, 0.61, 0.07), 1.5 * _CloudHaze.x).r;
    float tongue = textureLod(_CloudShape, vec3(cos(streakTurn) * 1.1, sin(streakTurn) * 1.1, x * 0.05) + vec3(0.61, 0.07, 0.23), 2.0 * _CloudHaze.x).r;
    float rimAt = rim + 0.9 * cos(2.0 * eyeTurn + 0.8) + (tongue - 0.74) * 9.0 + (fibre - 0.74) * 5.0;
    // (the arms ending by 13 radii, past which the cyclone is not asked about at all (cloudAt))
    rimAt = max(rimAt, min(rimAt + 8.00 * onBand * mix(0.35, 1.0, smoothstep(0.6, 0.85, eyeBreak)), 13.0));
    // (its top in low ridges along the fibres)
    // (no ridge taller than the sheet is thick there: where one was, it cut the thin rim into ribbons with hard edges)
    float top_ = over * deep + (fibre - 0.74) * min(2200.0, 2.5 * thick);
    float anvil = min(min((rimAt - x) * Rm * 0.6, above - under * deep), min(top_ - above, (r - inner) * 0.8));
    if (seek) return rain > 0.0 || max(inside + reachOut, anvil + 0.1 * thick) + 1.5 * stride > 0.0 ? 1.0 : 0.0;
    float near = max((inside + reachOut + 6.0 + 0.6 * pixel) * 0.7, (anvil + 0.1 * thick + 6.0) * 0.7);
    if (rain > 0.0) near = 1e9;
    heapsNear = max(heapsNear, max(near, -0.3 * Rm));
    if (near < 0.0) return 0.0;
    // ---- the surface's relief, read in the cyclone's own frame. The core's goes round the eye with the wall's cloud and
    // is drawn out three times as long round the eye as it is deep or high, winding a little outward: the wall's
    // terraces, and the arcs of the tops round the eye. The bands' is a storm tower's lumps.
    float turned = core ? _CloudEyeD.x * _CloudEyeB.y : 0.0;
    float cs = cos(turned), sn = sin(turned);
    vec3 own = vec3(eyeXY.x * cs + eyeXY.y * sn, eyeXY.y * cs - eyeXY.x * sn, above) / N + wm.yzw * 0.3;
    float round_ = eyeTurn - 1.8 * log(max(x, 0.5)) - turned;
    float lodBig = max(lod + log2(2250.0 / lump) - 1.0, 0.0) * _CloudHaze.x;
    vec3 part = clamp(size / (vec3(1.3, 0.65, 0.33) * lump), 0.0, 1.0);
    // (the core's top bubbles as a tower's does, with a tower's round lumps; its wall is a slope, with half the relief
    // and little of the finer sizes, drawn out round the eye)
    float onTop = core ? smoothstep(-0.5 * lump, 0.5 * lump, (r - inner) * 0.8 - (coreTop - above)) : 1.0;
    vec4 big = vec4(0.5);
    float moved = 0.0;
    for (int top = onTop > 0.01 ? 0 : 1; top < 1; top++)
    {
        big = textureLod(_CloudShape, own * (2250.0 / lump) + vec3(0.47, 0.11, 0.83), lodBig);
        vec3 domes = 1.0 - (1.0 - big.gba) * (1.0 - big.gba);
        moved = dot(max(domes - 0.715, vec3(-0.3)) * part, vec3(1.0, 0.5, 0.25)) * 1.6 * lump * onTop;
    }
    for (int face = onTop < 0.99 ? 0 : 1; face < 1; face++)
    {
        vec4 wide = textureLod(_CloudShape, vec3(cos(round_) * 1.19, sin(round_) * 1.19, (above + 0.6 * r) / (4.0 * lump)) + vec3(0.47, 0.11, 0.83), lodBig);
        vec3 domes = 1.0 - (1.0 - wide.gba) * (1.0 - wide.gba);
        moved += dot(max(domes - 0.715, vec3(-0.3)) * part, vec3(1.0, 0.25, 0.08)) * 0.75 * lump * (1.0 - onTop);
        big = mix(wide, big, onTop);
    }
    float soft = clamp((big.r - big.g) / max(1.0 - big.g, 0.02), 0.0, 1.0);
    if (fine)
    {
        float cellSmall = lump / 8.0;
        vec3 small = textureLod(_CloudDetail, own * (N / (cellSmall * 4.0)) + vec3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / (cellSmall / 16.0)) + 0.3, 0.0, 6.0) * _CloudHaze.x).rgb;
        moved += dot(small - 0.48, vec3(1.0, 0.5, 0.3)) * mix(0.45, 1.6, onTop) * cellSmall;
    }
    float tower = min(inside + moved, above - (soft - 0.4) * 160.0);
    float c = clamp(tower / max(12.0, 1.2 * pixelC) + 0.5, 0.0, 1.0) * clamp(0.3 + above / 90.0, 0.0, 1.0);
    // ---- the canopy's own look: ice, a third as thick as the towers' cloud, soft above; thinner and thinner towards
    // its rim, where it ends in nothing, not at an edge
    // (At its rim a twentieth of what it is at the wall: a fringe of ice lets the sun's disc through. At a fifth, as it
    // first was, each tongue of it seen from below was a solid blade with a shaded underside, like a wing.)
    float thin = (1.0 - 0.95 * smoothstep(0.45, 1.0, x / max(rimAt, 1.0))) * smoothstep(0.0, 1.6, rimAt - x) * mix(1.0, clamp(0.4 + (fibre - 0.66) * 6.0, 0.25, 1.4), out_);
    // (and towards the rim it has no faces at all: it is thickest in the middle of its depth and fades to nothing above
    // and below, a veil. With faces, each streak of the fringe seen from below was a solid white ribbon.)
    float ice = clamp(anvil / max(mix(min(0.22 * thick, 140.0), 0.5 * thick, out_), 1.2 * pixelC) + mix(0.5, 0.0, out_), 0.0, 1.0) * 0.36 * strength * thin;
    if (ice > c) { c = ice; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = clamp(0.35 + (above - under * deep) / max(thick, 50.0), 0.0, 1.0); }
    else { heapsInside = tower; heapsSmooth = min(inside, above - (soft - 0.4) * 160.0); heapsOpen = mix(core ? 0.75 : 0.5, 1.0, clamp(0.5 + moved / (0.5 * lump), 0.08, 1.0)) * clamp(0.2 + 0.9 * frac, 0.0, 1.0); }
    c *= 2.0 - frac;
    if (rain > c) { c = rain; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = 0.3; }
    return c;
}

// ---------------------------------------------------------------- a squall line's shelf cloud
//
// Under a line of storms the rain drags cold air down, and it runs out ahead of them along the ground as a shallow
// wedge with a sharp nose: the gust front. The warm air it runs into is lifted over the nose, and where it is lifted
// past its condensation level a cloud forms along the whole front: a long, low wedge hung on the storms' base, its
// leading edge the lowest and thinnest part of it, its upper face smooth and stepped in tiers (air lifted in layers,
// evenly, all along the line), its underside torn and boiling (the cold air's own turbulence). It comes on at the
// line's speed; under it the wind turns and the rain begins.
//
// Drawn where the weather has the front (Violent.SquallWind: so far ahead of the row of storms): from the nose back
// to the storms, four and a half kilometres, a few hundred metres thick at the nose and under the storms' base there,
// a kilometre and three quarters where it joins them.
float shelfAt(vec3 p, float h, vec4 wm, bool fine, float lod)
{
    float N = _CloudNoise.w;
    vec3 mid = _CloudShelfA.xyz / max(length(_CloudShelfA.xyz), 1.0);
    float up = dot(p, mid);
    if (up <= 0.0) return 0.0;
    vec3 flat_ = p - mid * up;
    float ahead = dot(flat_, _CloudShelfB.xyz), along = dot(flat_, cross(mid, _CloudShelfB.xyz));
    float base = _CloudShelfC.x, strength = smoothstep(0.1, 0.35, _CloudShelfB.w);
    bool seek = lod > 50.0;
    if (seek) lod -= 100.0;
    // (how far behind the nose, which is nowhere a straight line: it bulges ahead and hangs back by half a kilometre)
    vec4 wave = textureLod(_CloudShape, vec3(along / 26000.0, 0.37, 0.61), 2.0 * _CloudHaze.x);
    float s = ahead - _CloudShelfC.y - (wave.r - 0.74) * 5000.0;
    float ends = 1.0 - smoothstep(0.4, 0.5, abs(along) / max(_CloudShelfA.w, 1.0));
    float deep = 8000.0;
    float out_ = max(max(s, -s - deep), max(base - 500.0 - h, h - base - 4100.0));
    if (out_ > 0.0 || ends * strength <= 0.0) { heapsNear = max(heapsNear, min(-out_, -1.0)); return 0.0; }
    float back = -s;
    // (the underside: lowest at the nose, rising to the storms' base three and a half kilometres behind it)
    float under = base - 380.0 * pow(1.0 - smoothstep(0.0, 3500.0, back), 1.3) * strength;
    // (How thick: a lip of two hundred metres at the nose, and within a kilometre and a half behind it a kilometre and
    // more, in three plates each set back on the one below: the stacked look of a shelf seen coming. Not thickening
    // evenly over its whole depth, as it first was: seen from in front that was a ramp, like a bank of snow.)
    float risen = 1.0 - exp(-back / 700.0);
    float plates = risen * 3.0, stair = (floor(plates) + smoothstep(0.35, 1.0, fract(plates))) / 3.0;
    // (and behind the plates the cloud climbs into the storms: a wall along the whole row, three and a half kilometres
    // high where the storms stand. A shelf hangs from the front of a line of cloud, it does not lie out in the open
    // with the sky over it: drawn alone, with gaps of sky between it and each storm, its upper face was a white
    // plateau seen from everywhere.)
    float wall = smoothstep(1800.0, 4500.0, back);
    float thick = (200.0 + 900.0 * mix(risen, stair, 0.7) + 2600.0 * wall) * (1.0 - smoothstep(6500.0, 8000.0, back)) * (0.5 + 0.5 * ends) * (0.6 + 0.4 * strength);
    float over = under + thick;
    float inside = min(min(over - h, h - under), min(back, deep - back) * 0.7);
    float pixel = stormPixel(lod), pixelC = stormPixel(min(lod, stormLod));
    if (seek) return inside + 340.0 > 0.0 ? 1.0 : 0.0;
    float near = (inside + 330.0 + 0.6 * pixel) * 0.7;
    heapsNear = max(heapsNear, near);
    if (near < 0.0) return 0.0;
    // ---- its relief, read in the line's own frame (it goes with the line). The upper face smooth, in long streaks
    // along the line; the underside torn: lumps of a hundred and fifty metres, hanging.
    vec3 own = vec3(along, ahead, h) / N + wm.yzw * 0.2;
    float onTop = smoothstep(-60.0, 60.0, (h - under) - (over - h));
    vec4 streaks = textureLod(_CloudShape, vec3(own.x * 0.35, own.y * 3.0, own.z * 3.0) + vec3(0.29, 0.83, 0.17), max(lod, 0.0) * _CloudHaze.x);
    // (the plates smooth, in streaks along the line; the wall behind them, and the underside, lumpy: the wall as heaps
    // are, the underside torn)
    float smooth_ = onTop * (1.0 - wall);
    float moved = (streaks.r - 0.74) * 420.0 * smooth_;
    for (int below = smooth_ < 0.99 ? 0 : 1; below < 1; below++)
    {
        float lumps = mix(13.0, 5.0, wall * onTop);
        vec4 torn = textureLod(_CloudShape, own * lumps + vec3(0.61, 0.07, 0.43), max(lod + log2(lumps) - 1.0, 0.0) * _CloudHaze.x);
        vec3 domes = 1.0 - (1.0 - torn.gba) * (1.0 - torn.gba);
        moved += dot(max(domes - 0.715, vec3(-0.3)), vec3(1.0, 0.5, 0.25)) * mix(190.0, 520.0, wall * onTop) * (1.0 - smooth_);
        if (fine) moved += dot(textureLod(_CloudDetail, own * 60.0 + vec3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / 2.3) + 0.3, 0.0, 6.0) * _CloudHaze.x).rgb - 0.48, vec3(1.0, 0.5, 0.3)) * 45.0 * (1.0 - onTop);
    }
    float surface = inside + moved;
    float c = clamp(surface / max(10.0, 1.2 * pixelC) + 0.5, 0.0, 1.0) * ends * strength;
    heapsInside = surface; heapsSmooth = inside;
    // (it sees little sky from underneath, and more from its upper face)
    heapsOpen = mix(0.25, 0.9, onTop);
    return c * 1.4;
}

// Under a raining storm, its rain: where it falls (from the middle, along the ground: its reach, m) and how much of it there is
// now (0 to 1). Under the tower and no wider than it: a curtain three quarters of the storm's reach across stood out past the
// cloud above it as a grey slab with straight sides (the side-by-side with Blackrack's of 2026-10-28, storm_outside). In the
// second form a storm rains from when its tower is up until it has rained itself out. It comes and goes with the storm's
// strength and its rain as the tower does (cellTwo's fadeS): cut at once, a whole curtain went in a frame, and one came under
// a tower not yet there (the review of 2026-10-28).
vec2 rainReach(vec4 a, vec4 b, vec4 e)
{
    if (b.w < 0.1 || b.z < 0.15 || a.w <= 0.0) return vec2(0.0);
    float fade = smoothstep(0.1, 0.15, b.w) * smoothstep(0.15, 0.2, b.z);
    if (FORM_TWO > 0.5)
    {
        float life = e.y;
        return vec2(0.9 * towerTwo(a, b, life), fade * smoothstep(0.22, 0.4, life) * (1.0 - smoothstep(0.85, 0.97, life)));
    }
    return vec2(0.9 * a.w * 0.42 * (0.6 + 0.4 * smoothstep(0.15, 0.5, b.z)), fade);
}

// The rain at a point: x, how thick; y, how far out in it the point is (0 at its middle, 1 at its rim: for its light). Shafts
// from the base to the ground, uneven across and ragged at their rim, carried downwind as they fall (by up to a fifth of
// the fall: the drops take minutes to come down), in streaks drawn out up and down; thickest just under the base and fading
// up into it, thinner near the ground where the drops have spread out. Thin as cloud goes: heavy rain lets one see a
// kilometre or two.
vec2 rainAt(vec3 p, float h, vec4 a, vec4 b, vec4 e)
{
    vec2 reach = rainReach(a, b, e);
    float R = reach.x;
    if (R <= 0.0 || reach.y <= 0.0 || h > b.x || h < 0.0) return vec2(0.0);
    // (the storm's own level ways, east and north there, as cellTwo has them; the low wind along them, at most 1: the second
    // form's eased lean. The first form's _CloudDrift is the wind's way alone, which turns about in a frame where the wind
    // drops through nought: its rain is not carried, or the curtain's foot would jump across.)
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    vec3 east = cross(mid, _CloudFixedZ.xyz);
    east = dot(east, east) > 1e-6 ? normalize(east) : normalize(cross(mid, vec3(0.31, 0.57, 0.76)));
    vec3 north = cross(east, mid);
    vec3 wind = _CloudDrift.x * _CloudFixedX.xyz + _CloudDrift.y * _CloudFixedY.xyz + _CloudDrift.z * _CloudFixedZ.xyz;
    vec2 low = vec2(dot(wind, east), dot(wind, north));
    low /= max(1.0, length(low));
    float fallen = max(b.x - h, 0.0);
    vec2 drift = low * (0.2 * fallen * FORM_TWO);
    vec3 pd = p - east * drift.x - north * drift.y;
    vec3 flat_ = pd - mid * dot(pd, mid);
    float d = length(vec2(dot(flat_, east), dot(flat_, north)));
    if (d > 1.35 * R) return vec2(0.0);
    // (read from the storm's own middle, in the planet's fixed axes, so that the rain goes along with the storm: the shafts
    // a few across it and changing slowly with height, the streaks drawn out ten times up and down)
    vec3 pf = vec3(dot(pd, _CloudFixedX.xyz), dot(pd, _CloudFixedY.xyz), dot(pd, _CloudFixedZ.xyz));
    vec3 up = pf / max(length(pf), 1.0);
    vec3 mf = vec3(dot(mid, _CloudFixedX.xyz), dot(mid, _CloudFixedY.xyz), dot(mid, _CloudFixedZ.xyz));
    vec3 rel = (pf - mf * length(pf)) / _CloudNoise.w;
    float uneven = textureLod(_CloudShape, rel * (6000.0 / max(R, 500.0)) + up * (h / _CloudNoise.w) * 0.12 + vec3(0.21, 0.63, 0.37), 0.0).r;
    // (the heavy shafts dense and the rain between them thin, so that they show: thin all over, the sky behind showed through
    // it evenly, a dark blue band)
    float shafts = smoothstep(0.35, 0.75, uneven + 0.3 * (1.0 - d / R));
    if (shafts <= 0.0) return vec2(0.0);
    shafts *= shafts;
    float streak = textureLod(_CloudShape, (rel + up * (h / _CloudNoise.w) * 0.1) * 2.3 + vec3(0.7, 0.1, 0.4), 0.0).g;
    // (its rim wanders with height, and the rain spreads out a little as it nears the ground: read alone, the shafts made an
    // upright drum with a flat top, its sides straight. Never past the cut above, or the rain would end on its upright circle
    // there, where the rim reaches out furthest near the ground: the review of 2026-10-28.)
    float ragged = textureLod(_CloudShape, rel * (12000.0 / max(R, 500.0)) + up * (h / _CloudNoise.w) * 0.8 + vec3(0.53, 0.29, 0.81), 0.0).r;
    float fall = clamp(h / max(b.x, 100.0), 0.0, 1.0);
    float rim = min(R * (0.4 + 0.55 * uneven + 0.35 * ragged) * (1.0 + 0.25 * (1.0 - fall)), 1.35 * R);
    float body = smoothstep(rim, rim * 0.45, d);
    // (and finer columns within them, a few hundred metres across, nearly upright: rain falls in curtains, not as a fog)
    float columns = textureLod(_CloudShape, rel * (15000.0 / max(R, 500.0)) + up * (h / _CloudNoise.w) * 0.05 + vec3(0.37, 0.71, 0.13), 0.0).r;
    shafts *= mix(0.2, 1.0, smoothstep(0.4, 0.7, columns));
    // (fading up into the base, gone at it: faded from 900 m under it, as at first, the rain was thickest a third of the way
    // up and a lighter band hung under the dark base; reaching 200 m into the base, it was laid whole in front of the base's
    // wall, a grey band with a level top across the tower seen from near its height (both the review))
    float top = smoothstep(b.x, b.x - 400.0, h);
    float c = b.w * 0.12 * reach.y * body * shafts * mix(0.08, 1.0, smoothstep(0.3, 0.75, streak)) * (0.5 + 0.5 * fall) * top;
    return vec2(c, clamp(d / max(rim, 1.0), 0.0, 1.0));
}

// How much cloud there is at a point (q in the noise, p from the planet's centre, h its height): the low clouds, the middle
// ones, and the storms near the camera, whichever is the most.
// How much of the noise's low cloud is left at a point, beside a heap that is made as smoke is made: none within its width
// of the upright through its middle, all of it from 1.7 widths out.
float holeAt(vec3 p, vec4 hole)
{
    if (hole.w <= 0.0) return 1.0;
    vec3 axis = normalize(_CloudCam.xyz + hole.xyz);
    return smoothstep(hole.w, hole.w * 1.7, length(p - axis * dot(p, axis)));
}

// ---------------------------------------------------------------- a craft's wake in cloud
//
// "Clouds swirl when you go through it, like the volumetric explosions" (the user, 2026-10-12; and 2026-10-11: "if a rocket
// goes through it, can we have the deformation and curling like the volumetric explosions do?").
//
// What a body does to the air it goes through. It pushes the air aside and the air closes in behind it, but not to where
// it was: what lay near its path has been carried some way along with it (the more the nearer), so a rocket that comes up
// through a sheet draws a spike of the sheet up after it. Between the air it drags and the air beside that, which goes
// back the other way, there is a shear that rolls up into rings round the path, one after another, like smoke rings:
// those are the curls. The wake widens with time, its turbulence stirs what is in it, and after some minutes it has
// faded into the cloud. A burning engine also burns the cloud off where its fire goes: a hole.
//
// Drawn by reading the cloud, near the path, not where a place is but where what is now there came from: back along the
// path by the drag; round the nearest ring's middle by the roll; round the path itself by a twist; and a little way by
// the stirring. With the hole taken out of what is found.

vec4 wakePlace(int k) { return k < 4 ? (k == 0 ? _CloudWake0 : k == 1 ? _CloudWake1 : k == 2 ? _CloudWake2 : _CloudWake3) : (k == 4 ? _CloudWake4 : k == 5 ? _CloudWake5 : k == 6 ? _CloudWake6 : _CloudWake7); }

// For a place (from the camera, the planet's axes, m): where to read the cloud instead, as a step from the place; and how
// much of what is found there is left (the hole).
float wakeOut;       // (wakeStep) how far outside the wake's own tube the place is, where the wake moves nothing (m; below nothing: in it)

vec3 wakeStep(vec3 x, out float left)
{
    left = 1.0;
    int count = int(_CloudWakeLook.x + 0.5);
    // (the path: the piece of it nearest this place; how far along the whole path, how far out, and how old there)
    float out_ = 1e9, along = 0.0, sofar = 0.0, age = 0.0, whole = 0.0;
    vec3 axis = vec3(0.0, 0.0, 1.0), foot = vec3(0.0);
    for (int k = 0; k + 1 < count; k++)
    {
        vec4 a = wakePlace(k), b = wakePlace(k + 1);
        vec3 ab = b.xyz - a.xyz;
        float L = max(length(ab), 1.0);
        vec3 ax = ab / L;
        float s = clamp(dot(x - a.xyz, ax), 0.0, L);
        vec3 f = a.xyz + ax * s;
        float r = length(x - f);
        if (r < out_) { out_ = r; along = sofar + s; age = mix(a.w, b.w, s / L); axis = ax; foot = f; }
        sofar += L;
    }
    whole = sofar;
    // (how wide the wake has grown: from its first width, as the cube root of its age and more; gone after four minutes)
    float first = _CloudWakeLook.y;
    float wide = first * (1.0 + 1.5 * pow(age / 6.0, 0.4));
    wakeOut = out_ - 3.2 * wide;
    if (wakeOut > 0.0) return vec3(0.0);
    float there = smoothstep(0.0, 2.5, age) * (1.0 - smoothstep(150.0, 240.0, age));
    // (and it ends as it began, not cut off: fading over a wake's width at each end of the path)
    there *= smoothstep(0.0, wide, along) * smoothstep(0.0, wide, whole - along);
    vec3 radial = out_ > 0.01 ? (x - foot) / out_ : vec3(0.0);
    // (Nothing in a wake is even. Three numbers of the small noise, read on the wake's own edge abreast of this place (so
    // that they are the same all the way out from the path), say how far along this side's rings stand from where they
    // would, how hard each has rolled, and how wide the wake is on this side. With every ring whole, alike and evenly
    // spaced, as it first was, the wake was a screw: a thing turned on a lathe.)
    vec3 own = textureLod(_CloudDetail, (foot + radial * first) / (11.0 * first) + vec3(0.41, 0.07, 0.63), 1.0).rgb;
    wide *= 0.7 + 1.3 * own.b;
    // ---- the rings: one every two and a half first widths along the path or so, each a roll about a circle round the
    // path at the wake's edge; what is near that circle has been carried round it, by more the nearer and the older (up
    // to a turn and a half), in the plane of the path and the way out from it
    float apart = 2.5 * first;
    float alongOwn = along + (own.r - 0.48) * 1.4 * apart;
    float ringAt = (floor(alongOwn / apart) + 0.5) * apart;
    vec2 d = vec2(alongOwn - ringAt, out_ - 0.8 * wide);
    float roll = there * min(1.6 * sqrt(age), 9.4) * clamp((own.g - 0.3) * 3.5, 0.0, 1.6) * exp(-dot(d, d) / (0.32 * apart * apart));
    float cs = cos(roll), sn = sin(roll);
    vec2 was = vec2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
    // ---- the drag: what is near the path has come from further back along it
    float drag = there * 5.0 * first * (0.5 + own.b) * exp(-out_ * out_ / (0.9 * wide * wide));
    float alongWas = ringAt + was.x + drag - (alongOwn - along), outWas = max(0.8 * wide + was.y, 0.0);
    // ---- the twist round the path itself (the cloud's lumps drawn out into a screw, one way on one side and the other
    // on another), and the stirring, which grows as the rings break up: three numbers of the small noise, read in the
    // wake, push the place about by a share of the wake's width
    float twist = there * 2.4 * (own.r - 0.48) * 2.0 * exp(-out_ * out_ / (1.6 * wide * wide));
    vec3 side = cross(axis, radial);
    vec3 radialWas = radial * cos(twist) + side * sin(twist);
    // (read coarsely: the stirring is of eddies a good part of the wake's width across; read finely, and pushed far, it
    // shredded the cloud into specks)
    vec3 stir = (textureLod(_CloudDetail, x / (16.0 * first) + vec3(0.13, 0.57, 0.31) + axis * (age * 0.002), 1.5).rgb - 0.48) * (there * (1.2 + 1.6 * smoothstep(10.0, 90.0, age)) * wide * exp(-out_ * out_ / (2.0 * wide * wide)));
    // ---- the hole: what the fire burnt off and the craft blew out, closing over the minutes
    left = 1.0 - _CloudWakeLook.z * (1.0 - smoothstep(60.0, 220.0, age)) * smoothstep(0.0, wide, along) * smoothstep(0.0, wide, whole - along) * exp(-out_ * out_ / (0.5 * wide * wide));
    return (foot + axis * (alongWas - along) + radialWas * outWas + stir) - x;
}

float cloudAt(vec3 q, vec3 p, float h, vec4 wa, vec4 wb, vec4 wc, vec4 wm, bool fine, float lod)
{
    heapsOpen = -1.0; heapsInside = -1e9; heapsNear = -1e9; heapsSmooth = -1e9; heapsBody = vec4(1e5, 1.0, -1.0, 0.0); heapsDeck = vec2(0.0);
    formLight = vec4(0.5, 0.5, -1.0, -1.0);
    // (a craft's wake: only where there is one, and only near it)
    float left = 1.0;
    bool waked = false;
    for (int once = _CloudWakeLook.x > 1.5 ? 0 : 1; once < 1; once++)
    {
        vec3 x = (q - _CloudNoise.xyz) * _CloudNoise.w;
        vec3 off = x - _CloudWakeMid.xyz;
        if (dot(off, off) > _CloudWakeLook.w * _CloudWakeLook.w) break;
        vec3 step_ = wakeStep(x, left);
        // (Outside the wake's own tube it moves nothing: the walk is told only how far off the tube is. Told to go carefully
        // everywhere within the round space that holds the whole wake, as it was until 2026-10-15, a camera following a craft
        // that had just climbed through cloud was in that space, a kilometre or two across, and every line of sight from it
        // spent its steps there twenty-five metres at a time: from 2.3 km up, the deck above ended in a straight line some
        // 13 km off, with clear sky under it to the horizon, the owner's "strip of light blue in the distance".)
        if (wakeOut > 0.0) { heapsNear = max(heapsNear, -wakeOut); break; }
        waked = true;
        q += step_ / _CloudNoise.w;
        vec3 moved = step_.x * _CloudFixedX.xyz + step_.y * _CloudFixedY.xyz + step_.z * _CloudFixedZ.xyz;
        h += dot(moved, p / max(length(p), 1.0));
        p += moved;
    }
    // (The low clouds, then the middle ones where there are any: in one loop, so that Windows' compiler, which puts every
    // function inline, makes one copy of layerAt here and not two. The streets are the low heaps': the middle clouds are not
    // set in them; the new shapes stand upright in the noise's own space, so each layer takes its own part of the pattern.)
    // (How much of the far field there is, too, is the thicker layer's: the walk's light goes by it.)
    float c = 0.0;
    for (int L = 0; L < (wb.a > 0.004 ? 2 : 1); L++)
    {
        bool low = L == 0;
        vec4 body = heapsBody;
        vec2 deck = heapsDeck;
        vec4 lit2 = formLight;
        float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, grown = farGrow;
        float one = layerAt(q, p, low ? 1.0 : 2.0, h, low ? wa.x : wb.a, low ? wa.y : wc.x, low ? wa.z : wc.y, low ? wa.w : wc.z, low ? wb.x : wc.w, wm, fine, lod);
        if (low) { c = one; if (c > 0.0) c *= holeAt(p, _CloudHoleA) * holeAt(p, _CloudHoleB) * holeAt(p, _CloudHoleC); }
        else if (one > c) c = one;
        else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; farGrow = grown; heapsBody = body; heapsDeck = deck; formLight = lit2; }
    }
    // (development builds, trial 120: the layers' clouds left out, the storms alone, for pictures of a storm's own shape)
    if (abs(_CloudForm.z - 120.0) < 0.5) c = 0.0;
    // (each storm; whichever is thickest here says how much sky is seen and which way its surface faces; and for the light, a
    // storm's cloud, a cyclone's and a squall's shelf are thick, kilometres through, and not a sheet: see heapsBody)
    int count = storms();
    for (int k = 0; k < count; k++)
    {
        vec4 body = heapsBody, lit2 = formLight;
        heapsBody = vec4(1e5, 1.0, -1.0, 0.0); formLight = vec4(0.5, 0.5, -1.0, -1.0);
        float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, storm = cellAt(p, h, q, stormA(k), stormB(k), stormC(k), wm, fine, lod);
        if (storm > c) c = storm; else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; heapsBody = body; formLight = lit2; }
    }
    // (a cyclone within sight: the layers' cloud is kept out of its wall, which is its own, and out of its eye above the
    // eye's floor; then its own cloud)
    for (int once = _CloudEyeA.w > 0.0 ? 0 : 1; once < 1; once++)
    {
        vec4 e = eyeAt(p);
        if (e.x > 13.5) break;
        float low = _CloudEyeC.w, tall = max(_CloudEyeB.w - low, 1000.0);
        float eyeOn = smoothstep(0.35, 0.6, _CloudEyeB.x);
        float inner = _CloudEyeA.w * (0.62 + 0.55 * pow(clamp((h - low) / tall, 0.0, 1.0), 1.7));
        float keep = e.z < inner ? 1.0 - smoothstep(low + 700.0, low + 1000.0, h) : smoothstep(1.5, 2.3, e.x);
        c *= mix(1.0, keep, eyeOn);
        if (_CloudEyeD.y > 0.5) c = 0.0;        // (the lab's: the cyclone's own cloud alone)
        vec4 body = heapsBody, lit2 = formLight;
        heapsBody = vec4(1e5, 1.0, -1.0, 0.0); formLight = vec4(0.5, 0.5, -1.0, -1.0);
        float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, storm = cycloneAt(p, h, q, e, wm, fine, lod);
        if (storm > c) c = storm; else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; heapsBody = body; formLight = lit2; }
    }
    // (a squall line's shelf cloud, where one is within sight)
    for (int once = _CloudShelfA.w > 0.0 ? 0 : 1; once < 1; once++)
    {
        vec4 body = heapsBody, lit2 = formLight;
        heapsBody = vec4(1e5, 1.0, -1.0, 0.0); formLight = vec4(0.5, 0.5, -1.0, -1.0);
        float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, shelf = shelfAt(p, h, wm, fine, lod);
        if (shelf > c) c = shelf; else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; heapsBody = body; formLight = lit2; }
    }
    // (in a wake nothing is where its distance says: the walk is told to go carefully, and the light not to trust which
    // way a surface faces)
    if (waked) { heapsNear = max(heapsNear, -25.0); heapsInside = -1e9; heapsSmooth = -1e9; c *= left; }
    return c;
}

// ---------------------------------------------------------------- light

// Henyey and Greenstein's rule for how much light goes on in each direction: g near 1 mostly straight on, below 0 back.
float phase(float c, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5));
}

// The sunlight that comes through the air to a place: its height above the radius, and the sine of the sun's height there.
vec3 sunThroughAir(float h, float up)
{
    vec2 uv = vec2(clamp(up * 0.5 + 0.5, 0.0, 1.0), clamp(h / max(_CloudHigh.y, 1.0), 0.0, 1.0));
    return textureLod(_CloudAirLut, uv, 0.0).rgb;
}

// Where a line (from o, along a unit d) crosses a sphere of radius r round the centre: the two distances, or -1 if never.
vec2 sphere(vec3 o, vec3 d, float r)
{
    float b = dot(o, d);
    float c = dot(o, o) - r * r;
    float h = b * b - c;
    if (h < 0.0) return vec2(-1.0);
    h = sqrt(h);
    return vec2(-b - h, -b + h);
}

// The same for the camera, done with its height (worked out in double precision): far steadier near the ground.
vec2 shellFromCamera(float mu, float hr)
{
    float R = _CloudCam.w, alt = _CloudHigh.x;
    float b = (R + alt) * mu;
    float c = (alt - hr) * (2.0 * R + alt + hr);
    float h = b * b - c;
    if (h < 0.0) return vec2(-1.0);
    h = sqrt(h);
    return vec2(-b - h, -b + h);
}

// How much of what is seen of the sea at a place, from the camera, is the sun's light on it there, as light (not as the
// picture's numbers): what a cloud's shadow there takes away. The water's own blue (a clear deep sea's, as Natural Ocean
// has it: by the eye's weights about a fortieth of what a white thing in the sun sends back), lit by the sun and by the
// sky; the sky mirrored in the surface, which a shadow on the sea leaves as it is; the sun's glitter on the waves' facets
// (Cox and Munk's spread of slopes, for a breeze of about 7 m/s), all of it the sun's; and in front of all of it the air's
// own light, which no shadow on the sea takes away: the air from the camera down to the place as the walk has it (its
// thickness on that line; its light as the clouds' haze has it, the sun's scattered towards the eye and the whole sky's).
// In the game's measure, where a white thing facing the sun is 1. `dir`: the line of sight; `up`: up at the place; `h`:
// its height; `sunHere`: the sun's light there.
float seaSunShare(vec3 dir, vec3 up, vec3 sunDir, float h, vec3 sunHere)
{
    const vec3 eye = vec3(0.2126, 0.7152, 0.0722);
    float sunL = max(dot(sunHere, eye), 1e-6), skyL = dot(_CloudSky.rgb, eye) / sunL;
    float muS = max(dot(up, sunDir), 0.0), muV = max(-dot(dir, up), 0.02);
    float body = 0.027 * (muS + skyL), bodySun = 0.027 * muS;
    float mirrored = (0.02 + 0.98 * pow(1.0 - muV, 5.0)) * skyL * 1.3;
    vec3 facet = normalize(sunDir - dir);
    float muN = max(dot(facet, up), 0.05), slopes = 0.039;
    float glitter = muS > 0.0 ? (0.02 + 0.98 * pow(1.0 - clamp(dot(facet, sunDir), 0.0, 1.0), 5.0)) * exp(-(1.0 - muN * muN) / (muN * muN * slopes)) / (4.0 * slopes * muV * muN * muN * muN * muN) : 0.0;
    float rH = max(_CloudAirHigh.x, 100.0), hH = max(_CloudAirHigh.y, 100.0), hc = max(_CloudHigh.x, h + 1.0), hg = max(h, 0.0);
    float thick = (dot(_CloudAir.rgb, eye) * rH * (exp(-hg / rH) - exp(-hc / rH)) + _CloudAir.w * hH * (exp(-hg / hH) - exp(-hc / hH))) / max(muV, 0.08);
    float seen = exp(-thick);
    float cosView = dot(dir, sunDir), rayleigh = dot(_CloudAir.rgb, eye) + 1e-12;
    float once = (rayleigh * 3.0 / (16.0 * PI) * (1.0 + cosView * cosView) + _CloudAir.w * phase(cosView, 0.75)) / (rayleigh + _CloudAir.w);
    float airL = _CloudHaze.z * once + dot(_CloudSky.rgb, eye) * 2.2 * clamp(dot(normalize(_CloudCam.xyz), sunDir) * 3.0 + 0.3, 0.0, 1.0) / sunL;
    return clamp(seen * (bodySun + glitter) / max(seen * (body + mirrored + glitter) + (1.0 - seen) * airL, 1e-6), 0.0, 1.0);
}

float dither(vec2 p) { return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715)))); }

// A storm's rain along this line of sight, walked apart from the clouds (it hangs below them, where the clouds' own walk does
// not go): where the line passes through an upright cylinder round the storm's middle, from the ground to its base, wide
// enough for the rain carried downwind, ten steps. Kept apart (rl, rth, rsT, rsW), each storm's on its own, and laid into
// the clouds' walk where the line first meets that storm's rain (rFront), the nearest first: laid in front of all of them,
// as until 2026-10-28, a curtain far off hid the low clouds before it (storm_outside); all the storms' together, laid where
// the nearest began, as for an afternoon after, a second storm's did the same. Lit as rain is: grey, the sky above it hidden by the storm so its light comes in from the
// sides, more at its rim; the sun's only where it reaches past the storm's side, and bright only looking towards it (a
// drop throws light on forward).
// (Where the line passes a funnel standing in the rain (split, where it meets the funnel), the rain beyond goes into totals of
// its own (rl2...), laid after the funnel: laid whole, the curtain's far side veiled the funnel in front of it, the review.)
void rainWalk(vec4 a, vec4 b, vec4 e, vec3 dir, vec3 sunTop, vec3 sunDir, float stop, float jitter, inout vec3 rl, inout float rth, inout float rsT, inout float rsW, inout float rFront, float split, inout vec3 rl2, inout float rth2, inout float rsT2, inout float rsW2, inout float rFront2)
{
    vec2 reach = rainReach(a, b, e);
    if (reach.x <= 0.0 || reach.y <= 0.0) return;
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    vec3 o = _CloudCam.xyz - mid * dot(_CloudCam.xyz, mid);
    vec3 d = dir - mid * dot(dir, mid);
    float r = 1.35 * reach.x + 0.2 * max(b.x, 0.0) * FORM_TWO;   // (and room for the rain carried downwind: the second form's alone)
    float A = dot(d, d), B = dot(o, d), C = dot(o, o) - r * r;
    float disc = B * B - A * C;
    if (A < 1e-8 || disc <= 0.0) return;
    disc = sqrt(disc);
    float ta = max((-B - disc) / A, _CloudStage.z), tb = min((-B + disc) / A, stop);
    if (_CloudStage.w > 0.0) tb = min(tb, _CloudStage.w);
    // (and only where the line is under the storm's base: walked up through the whole cylinder, a line looking up spent most
    // of its ten steps above the rain)
    vec2 band = shellFromCamera(dot(_CloudCam.xyz / max(length(_CloudCam.xyz), 1.0), dir), max(b.x, 0.0));
    if (_CloudHigh.x < max(b.x, 0.0)) tb = min(tb, band.y);
    else { if (band.x < 0.0) return; ta = max(ta, band.x); tb = min(tb, band.y); }
    if (tb <= ta) return;
    float dt = (tb - ta) / 10.0;
    float lobe = 0.02 + 0.6 * phase(dot(dir, sunDir), 0.75);
    // (the sky's light grey, as rain shows it: the blue sky's own light made it a periwinkle slab)
    vec3 sky = mix(vec3(dot(_CloudSky.rgb, vec3(0.2126, 0.7152, 0.0722))), _CloudSky.rgb, 0.15) * 0.9;
    for (int k = 0; k < 10; k++)
    {
        float t = ta + (float(k) + jitter) * dt;
        vec3 p = _CloudCam.xyz + dir * t;
        float h = length(p) - _CloudCam.w;
        vec2 c = rainAt(p, h, a, b, e);
        if (c.x <= 0.0) continue;
        bool beyond = t >= split;
        if (beyond) rFront2 = min(rFront2, max(split, t - 0.5 * dt));
        else rFront = min(rFront, t - 0.5 * dt);
        float sigma = c.x * _CloudLook.x;
        float sunUp = dot(p / max(length(p), 1.0), sunDir);
        float open = mix(0.3, 0.75, c.y);
        float past = smoothstep(0.5, 1.0, c.y) * mix(1.0, 0.4, smoothstep(0.4, 0.9, sunUp));
        vec3 lit = (sky * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) * open + _CloudGround.rgb * 0.5 + vec3(_CloudSunTint.w)) * 0.75
                 + sunTop * sunThroughAir(h, sunUp) * (0.06 + lobe) * past;
        float pass = exp(-sigma * dt);
        if (beyond)
        {
            rl2 += rth2 * lit * (1.0 - pass);
            rsT2 += t * rth2 * (1.0 - pass);
            rsW2 += rth2 * (1.0 - pass);
            rth2 *= pass;
        }
        else
        {
            rl += rth * lit * (1.0 - pass);
            rsT += t * rth * (1.0 - pass);
            rsW += rth * (1.0 - pass);
            rth *= pass;
        }
    }
}

// A tornado's funnel (or a waterspout's) along this line of sight, walked apart from the clouds as a storm's rain is, and
// laid as it is, where the line first meets it (front): it hangs under them, to the ground, in the rain or beside it.
// (Written straight into the walk's light, it showed through the rain's near side unveiled.)
//
// What is seen of a tornado is not its wind but two things the wind makes. The funnel: cloud, where the air's pressure in
// the turning core has fallen so far that it condenses below the storm's base; widest at the cloud, narrowing downward,
// smooth-sided where the turning is steady and ragged where it is not, and reaching the ground only in a strong one (in a
// weak one a stub under the cloud, with clear air turning unseen between it and the ground). And at its foot what it has
// picked up: over land a low, wide whirl of dust and wreckage, darker than the funnel; over water a ring of spray. Both turn:
// the pattern on them goes round, faster near the middle.
void funnelWalk(vec3 dir, vec3 sunTop, vec3 sunDir, float stop, float jitter, inout vec3 light, inout float through, inout float sumT, inout float sumW, inout float front)
{
    float core = _CloudTwistA.w, tall = _CloudTwistB.x;
    if (core <= 0.0 || tall < 50.0) return;
    float foot = length(_CloudTwistA.xyz);
    vec3 mid = _CloudTwistA.xyz / max(foot, 1.0);
    // (it leans and bends a little, the more the higher: its upper end is carried on by the storm, its foot dragged over the ground)
    vec3 aside = normalize(cross(mid, vec3(0.31, 0.87, 0.38)));
    vec3 other = cross(mid, aside);
    // (the line against an upright cylinder that holds funnel, lean and whirl)
    float wide = 5.0 * core + 0.2 * tall;
    vec3 o = _CloudCam.xyz - mid * dot(_CloudCam.xyz, mid);
    vec3 d = dir - mid * dot(dir, mid);
    float A = dot(d, d), B = dot(o, d), C = dot(o, o) - wide * wide;
    float disc = B * B - A * C;
    if (A < 1e-8 || disc <= 0.0) return;
    disc = sqrt(disc);
    float ta = max((-B - disc) / A, _CloudStage.z), tb = min((-B + disc) / A, stop);
    if (_CloudStage.w > 0.0) tb = min(tb, _CloudStage.w);
    // (and between the ground and a little above the cloud's base)
    float climbs = dot(dir, mid), high0 = dot(_CloudCam.xyz, mid) - foot;
    if (abs(climbs) > 1e-4)
    {
        float t0 = (-10.0 - high0) / climbs, t1 = (tall * 1.15 - high0) / climbs;
        ta = max(ta, min(t0, t1)); tb = min(tb, max(t0, t1));
    }
    else if (high0 < -10.0 || high0 > tall * 1.15) return;
    if (tb <= ta) return;
    float dt = (tb - ta) / 32.0;
    float reach = clamp(_CloudTwistB.y, 0.0, 1.0);
    vec3 dust = mix(vec3(0.42, 0.33, 0.25), vec3(0.92, 0.95, 1.0), _CloudTwistB.z);
    for (int k = 0; k < 32; k++)
    {
        if (through < 0.02) break;
        float t = ta + (float(k) + jitter) * dt;
        vec3 p = _CloudCam.xyz + dir * t;
        float z = dot(p, mid) - foot, up = clamp(z / tall, 0.0, 1.0);
        vec3 from = p - mid * dot(p, mid) - (aside * (0.22 * tall * up * up) + other * (0.06 * tall * sin(up * 5.0 + _CloudTwistB.w * 0.05)));
        float r = length(from);
        // (the pattern goes round with the air: faster near the middle, and wound up the funnel)
        float turn = _CloudTwistB.w * clamp(core / max(r, 0.4 * core), 0.15, 2.5) + up * 9.0;
        float cs = cos(turn), sn = sin(turn);
        float e = dot(from, aside), n = dot(from, other);
        vec3 round = vec3(e * cs - n * sn, e * sn + n * cs, z);
        float rough = textureLod(_CloudDetail, round / (core * 5.0) + vec3(0.13, 0.57, 0.29), 0.0).r;
        float fine = textureLod(_CloudDetail, round / (core * 1.3) + vec3(0.61, 0.11, 0.83), 0.0).g;
        // ---- the funnel: its radius by height (widest at the cloud), cloud only from the cloud down as far as it reaches
        float radius = core * (0.4 + 3.6 * pow(up, 2.4)) * (0.8 + 0.5 * rough);
        float low = tall * (1.0 - reach);
        float shown = smoothstep(low - 0.04 * tall, low + 0.1 * tall, z) * step(z, tall * 1.12);
        // (a strong one narrows to the ground and stays as wide as its core there; the tip of one that does not reach tapers)
        radius *= mix(0.35 + 0.65 * smoothstep(low, low + 0.25 * tall, z), 1.0, smoothstep(0.92, 1.0, reach));
        // (its side is a surface: cloud begins within a few metres, ragged by a tenth of its width)
        float funnel = smoothstep(radius, radius * 0.86 - 4.0, r * (0.92 + 0.16 * fine)) * shown;
        // ---- the whirl at its foot: low and wide, thick near the ground
        float whirl = smoothstep(3.6 * core, 1.0 * core, r * (0.8 + 0.5 * rough)) * exp(-max(z, 0.0) / (1.5 * core + 12.0)) * (0.35 + 0.9 * fine) * smoothstep(0.15, 0.5, reach + 0.3) * step(-10.0, z);
        float c = max(funnel, 0.0);
        float both = c + whirl * 0.7;
        if (both <= 0.002) continue;
        front = min(front, t - 0.5 * dt);
        float h = length(p) - _CloudCam.w;
        float sunUp = dot(p / max(length(p), 1.0), sunDir);
        vec3 sunHere = sunTop * sunThroughAir(h, sunUp);
        // (lit as a pillar is: the side towards the sun bright, the other in its own shade; and it stands under a storm)
        vec3 out_ = from / max(r, 1.0);
        float facing = clamp(dot(out_, sunDir) * 0.6 + 0.4, 0.0, 1.0);
        vec3 lit = _CloudSky.rgb * 0.7 * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) + _CloudGround.rgb + sunHere * (0.08 + 0.45 * facing) + vec3(_CloudSunTint.w);
        lit *= mix(dust, vec3(1.0), c / max(both, 1e-4));
        float sigma = both * _CloudLook.x * 0.8;
        float pass = exp(-sigma * dt);
        light += through * lit * (1.0 - pass);
        sumT += t * through * (1.0 - pass);
        sumW += through * (1.0 - pass);
        through *= pass;
    }
}

// Where in its dot this frame's line of a dot of the walk goes (a share of the dot each way, 0 to 1), which kept pixel that is,
// and which of the dot's rounds this frame is. Each dot goes through the sixteen (four on "higher") kept pixels it stands for once
// in each round of sixteen (four) frames, in Bayer's order, so that any four frames running cover its quarters evenly, as since
// 2026-10-12; but each dot from its own place in that order, a hash of the dot, the same in every round. (Until 2026-10-26 every
// dot went through the same pixel in the same frame: with the picture moving a pixel a frame sideways, half the places on a cloud
// had no line of their own in 64 frames and the rest eight, a pattern four pixels across that went with the cloud, the comb along
// a tower's top (the lab's count; Andersson et al. 2019 on fixed orders and motion).) Within the pixel each of the dot's rounds is
// moved by a point of the R2 sequence within half of it, as Clouds.cs's SubDot did for all.
vec2 lookIn(ivec2 cell, out ivec2 kept, out int round_)
{
    int div = int(_CloudAim.w + 0.5), n = div * div;
    uint h = mixBits(uint(cell.x) + mixBits(uint(cell.y) + 104729u));
    int f = int(_CloudAim.z + 0.5) + int(h & 1023u);
    int v = f % n;
    round_ = f / n;
    // (Bayer's order backwards: v's bits, two at a time, name the quarter and then the pixel within it; checked against Clouds.cs's
    // table of 2026-10-21 for all sixteen)
    ivec2 at = div == 4 ? ivec2(((v ^ (v >> 1)) & 1) * 2 + (((v >> 2) ^ (v >> 3)) & 1), (v & 1) * 2 + ((v >> 2) & 1))
                        : ivec2((v ^ (v >> 1)) & 1, v & 1);
    // (development builds, trial 24: each dot's square of pixels also turned or mirrored its own way, one of eight)
    if (_CloudForm.z == 24.0)
    {
        uint s = (h >> 10u) & 7u;
        if ((s & 1u) != 0u) at.x = div - 1 - at.x;
        if ((s & 2u) != 0u) at.y = div - 1 - at.y;
        if ((s & 4u) != 0u) at = at.yx;
    }
    kept = cell * div + at;
    vec2 sub = (fract(0.5 + float(round_ & 255) * vec2(0.7548776662, 0.5698402910)) - 0.5) * 0.5;
    return (vec2(at) + 0.5 + sub) / float(div);
}

// The blue noise at a place, in one of its 64 squares (both wrap round), 0 to 1.
float blueAt(ivec2 at, int square)
{
    ivec2 corner = ivec2(square & 7, (square >> 3) & 7) * 64;
    return (floor(texelFetch(_CloudBlue, corner + (at & 63), 0).r * 255.0 + 0.5) + 0.5) / 256.0;
}

void main()
{
    vec2 uv = vs_TEXCOORD0;
    float stage = _CloudStage.x;

    // ---------------------------------------------------------------- 0, 1: the noise
    if (stage < 1.5)
    {
        float size = stage < 0.5 ? 128.0 : 64.0;
        vec3 p = vec3(gl_FragCoord.xy / size, (_CloudStage.y + 0.5) / size);
        if (stage < 0.5)
        {
            float smoothSum = 0.0, amp = 1.0, total = 0.0;
            for (int o = 0; o < 4; o++)
            {
                int period = 4 << o;
                // (each octave's lattice moved off the others': Perlin's noise is nought at every point of its lattice, and with
                // the lattices of all four on top of one another the sum was nought on the coarsest one's, a regular grid every
                // 2.25 km, which showed as rows of heaps along the horizon and a lattice from orbit)
                vec3 off = fract(vec3(0.137, 0.713, 0.371) * float(o + 1) * 3.17);
                smoothSum += smoothNoise(p * float(period) + off, period, 11u + uint(o)) * amp;
                total += amp; amp *= 0.5;
            }
            float soft = clamp(smoothSum / total * 0.5 + 0.5, 0.0, 1.0);
            float c4 = cellsLayered(p, 4, 101u), c8 = cellsLayered(p, 8, 211u), c16 = cellsLayered(p, 16, 307u);
            // (Perlin's noise swelled by the cells: rounded heaps, with the cells' billows on them)
            float heaps = clamp(remap(soft, 0.0, 1.0, c4, 1.0), 0.0, 1.0);
            SV_Target0 = vec4(heaps, c4, c8, c16);
        }
        else
        {
            SV_Target0 = vec4(cellsLayered(p, 4, 401u), cellsLayered(p, 8, 503u), cells(p * 16.0, 16, 601u) * 0.7 + cells(p * 32.0, 32, 607u) * 0.3, 1.0);
        }
        SV_Target1 = vec4(0.0);
        return;
    }

    // ---------------------------------------------------------------- 7: the second form's billows (2026-10-27)
    // Once, at load, a slice of their cube a draw (Clouds.cs, MakeBillow), stretched from the sum's 0.1 % and 99.9 % points to 0 and 1.
    if (stage > 6.5 && stage < 7.5)
    {
        vec3 p = vec3(gl_FragCoord.xy / 256.0, (_CloudStage.y + 0.5) / 256.0);
        SV_Target0 = vec4(clamp((billowMake(p) - 0.36389) / (0.91240 - 0.36389), 0.0, 1.0));
        SV_Target1 = vec4(0.0);
        return;
    }

    float R = _CloudCam.w;

    // ---------------------------------------------------------------- 5: the map of the lattice's heaps
    // Once a frame, for each dot of a map that lies over the ground round the camera (on the face of the cube the camera
    // is nearest, and coming round every so many lengths of the noise, so that a place is found in it by where it is in the
    // noise and nothing has to be moved when the camera moves): the low layer's heap of the lattice that has that place or
    // stands nearest, as heapsMap gives it. Every asking then reads its heap here. (Worked out at every asking, as it was
    // until the evening of 2026-10-12, the lattice doubled what the clouds cost: by count nearly everything asked is clear
    // air, and each asking had three points of the lattice to look at to learn it.)
    // ---------------------------------------------------------------- 9: the sun-side light field (2026-10-18)
    // Each cell asks the clouds towards the sun from its middle, past the walk's first step that way (28 m): four readings in each
    // of the three stretches the walk's own steps end at (154, 346, 711 m on), _CloudLightF.w beyond (8 if nought), each as much
    // longer than the last as brings them to where the line leaves the shell; all with the cell's own weather, as the walk's
    // steps that way use the lit place's; each cell at its own fixed place in each stretch. Four tiles across, a level under the
    // last. What is kept is the light let through, exp(-0.1 e-folds) (read back between cells as that: between a sunlit cell and
    // one deep in a heap, a blend of the light let through is truer at a surface than a blend of the light lost). The walk reads
    // it after its own first step (see the walk), in place of its other steps that way, its far looks and the shadow's second map:
    // in the lab, light closer to the truth (11.4 against 16.8 / 255 in a storm, 7.0 against 11.4 under heaps) for 10-31 %
    // less of the walk (research/rendering/experiments/cloud-light-field.md).
    if (stage > 8.5)
    {
        int n = int(_CloudLightF.x + 0.5), rows = int(_CloudLightF.z + 0.5) / 4;
        ivec2 px = ivec2(gl_FragCoord.xy);
        // (the upper half of the field: the sky's part for the same cells, below)
        int sunRows = rows * n * int(_CloudLightF.y + 0.5);
        bool skyHalf = px.y >= sunRows;
        if (skyHalf) px.y -= sunRows;
        int level = px.y / (rows * n), ly = px.y - level * rows * n;
        int k = (ly / n) * 4 + px.x / n;
        vec3 C = lightCellAt(level, px.x % n, ly % n, k);
        vec3 rel = C - _CloudCam.xyz;
        vec3 relFixed = vec3(dot(rel, _CloudFixedX.xyz), dot(rel, _CloudFixedY.xyz), dot(rel, _CloudFixedZ.xyz));
        vec3 q = _CloudNoise.xyz + relFixed / _CloudNoise.w;
        float dist = length(rel);
        // (the near shapes or the far field as the walk chooses them at this cell, by how wide a pixel of its picture is there:
        // see formChoice)
        formChoice = _CloudJitter.w > 0.0 ? dist * _CloudJitter.w : -1.0;
        vec4 wa, wb, wc;
        weatherAt(C, wa, wb, wc);
        if (_CloudForm.x > 0.5) formWeather(C, wa, wb, wc);
        vec4 wm0 = broadAt(q);
        vec4 wm = vec4(wm0.x * streetsAt(q, wm0), wm0.yzw);
        if (_CloudForm.x > 0.5) formStep(q, wm, dist);
        vec3 sunDir = _CloudSun.xyz, sunFixed = _CloudSunFixed.xyz;
        formWay = sunFixed;
        billowLight = FORM_TWO > 0.5 ? 3.0 : 0.0;
        float lod = min(log2(max(dist, 1.0) / 25000.0), 4.0);
        float place = fract(52.9829189 * fract(dot(vec2(px), vec2(0.06711056, 0.00583715))));
        // ---- the sky's part (2026-11-05, gap 4 of the owner's spec of Blackrack's clouds and item 5 of its build order, research/clouds-r5/spec-gaps-2026-11-04.md):
        // how much of the sky's light comes down to the cell past all the clouds over it, every layer's and every storm's. Three
        // lines of sight up from the cell, one straight up and two leaning 50 degrees, at a way round of the cell's own; on each
        // six readings out to where it leaves the shell (no further than 12 km), each longer than the last; what each holds turned
        // into the light let through as his ambient is, 0.3 + 0.7 / (1 + 0.035 a + 0.00065 a^2) of it for `a` e-folds of cloud,
        // and the three taken together. Until then the sky's light on a place in the second form was dimmed by its own column
        // alone: under an anvil, a deck or a storm a heap was lit by the whole sky.
        if (skyHalf)
        {
            vec3 upC = normalize(C);
            vec3 e1 = normalize(cross(upC, abs(upC.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0))), e2 = cross(upC, e1);
            float lit3 = 0.0;
            for (int r = 0; r < 3; r++)
            {
                float az = 6.2832 * (place + float(r) * 0.5);
                vec3 way = r == 0 ? upC : normalize(upC * 0.6428 + (e1 * cos(az) + e2 * sin(az)) * 0.766);
                vec3 wayFixed = vec3(dot(way, _CloudFixedX.xyz), dot(way, _CloudFixedY.xyz), dot(way, _CloudFixedZ.xyz));
                formWay = wayFixed;
                float bw = dot(C, way), cw = dot(C, C) - (R + _CloudShell.y) * (R + _CloudShell.y);
                float reach = clamp(-bw + sqrt(max(bw * bw - cw, 0.0)), 0.0, 12000.0) - 30.0;
                float lostUp = 0.0;
                if (reach > 0.0)
                {
                    float grow = 1.8, str = reach * (grow - 1.0) / (pow(grow, 6.0) - 1.0), at0 = 30.0;
                    for (int s = 0; s < 6; s++)
                    {
                        float at = at0 + str * fract(place + 0.618 * float(s + r));
                        vec3 lp = C + way * at;
                        lostUp += cloudAt(q + wayFixed * (at / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 1.0) * str;
                        at0 += str; str *= grow;
                    }
                }
                float a = 0.1 * lostUp * _CloudLook.x;
                lit3 += 0.3 + 0.7 / (1.0 + 0.035 * a + 0.00065 * a * a);
            }
            SV_Target0 = vec4(lit3 / 3.0, 0.0, 0.0, 1.0);
            SV_Target1 = vec4(0.0);
            return;
        }
        vec4 lost = vec4(0.0);
        for (int seg = 0; seg < 3; seg++)
        {
            float a0 = seg == 0 ? 0.0 : seg == 1 ? 154.0 : 346.0, a1 = seg == 0 ? 154.0 : seg == 1 ? 346.0 : 711.0;
            float len = (a1 - a0) / 4.0, sum = 0.0;
            for (int s = 0; s < 4; s++)
            {
                float at = 28.0 + a0 + (float(s) + place) * len;
                vec3 lp = C + sunDir * at;
                sum += cloudAt(q + sunFixed * (at / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 0.5) * len;
            }
            if (seg == 0) lost.r = sum; else if (seg == 1) lost.g = sum; else lost.b = sum;
        }
        float b2 = dot(C, sunDir), c2 = dot(C, C) - (R + _CloudShell.y) * (R + _CloudShell.y);
        float exitAt = -b2 + sqrt(max(b2 * b2 - c2, 0.0));
        if (exitAt > 739.0)
        {
            int farN = _CloudLightF.w > 0.5 ? int(_CloudLightF.w) : 8;
            float grow = exp2(8.0 / float(farN)), str = (exitAt - 739.0) * (grow - 1.0) / (pow(grow, float(farN)) - 1.0), at0 = 739.0;
            for (int s = 0; s < 32; s++)
            {
                if (s >= farN) break;
                float at = at0 + str * place;
                vec3 lp = C + sunDir * at;
                lost.a += cloudAt(q + sunFixed * (at / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 1.0) * str;
                at0 += str; str *= grow;
                place = fract(place + 0.618);
            }
        }
        SV_Target0 = exp(-0.1 * lost * _CloudLook.x);
        SV_Target1 = vec4(0.0);
        return;
    }

    // ---------------------------------------------------------------- 6: the low clouds' map, smoothed
    // Once a frame, the weather's map of the low clouds read smoothly (mapSmooth) into one four times as fine, which every
    // asking then reads plainly (weatherAt). Here `_CloudMap` is the weather's own.
    if (stage > 5.5)
    {
        SV_Target0 = mapSmooth(_CloudMap, uv);
        SV_Target1 = vec4(0.0);
        return;
    }

    if (stage > 4.5)
    {
        float N = _CloudNoise.w, E = _CloudHeapsAt.y, face = _CloudHeapsAt.w;
        vec3 camF = vec3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
        vec3 camIn = faceOf(_CloudNoise.xyz, face);
        vec2 across = camIn.xy + E * (fract(uv - camIn.xy / E + 0.5) - 0.5);
        vec4 wa, wb, wc;
        weatherAt(_CloudCam.xyz, wa, wb, wc);
        // (the place's third number: it is on the ball of the layer's base. From the planet's centre, a place in the noise
        // is as many lengths on, plus how far the wind has carried the noise.)
        vec3 carried = faceOf(camF - _CloudNoise.xyz * N, face);
        float ball = R + wa.z;
        vec2 level = across * N + carried.xy;
        float third = sqrt(max(ball * ball - dot(level, level), 1.0)) * sign(faceOf(camF, face).z);
        vec3 P = unfaceOf(vec3(level, third), face);
        vec3 col = unfaceOf(vec3(across, (third - carried.z) / N), face);
        vec3 p = P.x * _CloudFixedX.xyz + P.y * _CloudFixedY.xyz + P.z * _CloudFixedZ.xyz;
        weatherAt(p, wa, wb, wc);
        vec4 wm0 = broadAt(col);
        formWeather(p, wa, wb, wc);
        // (no heaps past 0.48 of the map's width from the camera: the walk's own fading of them with distance, drawn in
        // so far that it has ended before the map comes round)
        // (from above all the clouds: how far over the ground from under the camera, as the walk goes by the distance it runs
        // inside the shell, which from high above is short wherever the map lies)
        float fromCam = mix(length(P - camF), length(across - camIn.xy) * N, aboveness());
        formStep(col, vec4(wm0.x * streetsAt(col, wm0), wm0.yzw), fromCam * (26000.0 / (0.48 * E * N)));
        vec4 K = formKind[0], Z = formSize[0], Hh = formHeap[0];
        vec4 best = vec4(1e5, 0.0, 0.0, 0.0), padsBest = vec4(-1e5, 0.0, 0.0, 0.0);
        float raisedBest = 0.0;
        // (both sizes of lattice where two are there, and both faces near where two meet: the heap whose edge is nearer has
        // the place, and where the edges of two are as near as one another within a join's width, something of both, as
        // heapsMap does among the heaps of one lattice)
        for (int which = 0; which < 2; which++)
        {
            float perF = which == 0 ? Z.y : Z.z, weight = which == 0 ? 1.0 - Z.w : Z.w;
            if (weight < 0.001 || formRaw[0].z < 0.5 || K.y > 0.999) continue;
            float C = N / perF;
            float tall = mix(1.05 * K.z / (0.8 * C), clamp(1.05 * formMore[0].z / (0.84 * C), 0.8 * Z.x, 1.25 * Z.x), K.x);
            for (int side = 0; side < 2; side++)
            {
                float mine = side == 0 ? formMine : 1.0 - formMine, f = side == 0 ? formFace : formOther;
                if (mine < 0.001) continue;
                vec3 cf = faceOf(col, f), uf = faceOf(formUp, f);
                float raised;
                // (pads: their cells' columns (padsColumn), the one the place is further inside having it)
                if (K.x < 0.5)
                {
                    vec2 puv = cf.xy * perF + vec2(7.3, 3.1) * f;
                    vec4 bend = textureLod(_CloudShape, vec3(puv * 0.17, 0.37), 1.5);
                    float crack = C * mix(0.42, 0.012, ramp(0.05, 0.75, formMore[0].x));
                    vec4 cell = padsColumn(puv + (bend.gb - 0.48) * 1.3, -uf.xy / uf.z, C, int(perF + 0.5), _CloudForm.y, Hh.x * weight * mine, crack, min(K.z, 560.0), 0.05, raised);
                    if (padsBest.x < -1e4 || cell.x > padsBest.x) { padsBest = cell; raisedBest = raised; }
                    continue;
                }
                vec4 one = heapsMap(cf.xy * perF + vec2(7.3, 3.1) * f, -uf.xy / uf.z, C, int(perF + 0.5), _CloudForm.y, Hh.x * weight * mine, C * Hh.y, Hh.z, C * Hh.w, tall, raised);
                if (one.y <= 0.0) { if (best.y <= 0.0) best.x = min(best.x, one.x); continue; }
                if (best.y <= 0.0) { best = one; raisedBest = raised; continue; }
                float gapA = best.x - best.y, gapB = one.x - one.y, k = max(C * Hh.w, 1.0);
                float h = clamp(0.5 + 0.5 * (gapB - gapA) / k, 0.0, 1.0);
                best.yzw = mix(one.yzw, best.yzw, h); raisedBest = mix(raised, raisedBest, h);
                best.x = max(best.y + mix(gapB, gapA, h) - k * h * (1.0 - h), 0.0);
            }
        }
        SV_Target0 = K.x < 0.5 ? padsBest : best;
        SV_Target1 = vec4(raisedBest, 0.0, 0.0, 1.0);
        return;
    }

    // ---------------------------------------------------------------- 4: the shadow, from points on the ground towards the sun
    if (stage > 3.5)
    {
        // (An eighth of this map is drawn each frame, one strip of eight in turn, by a rectangle set outside the shader:
        // clouds, sun and wind move a dot of it in seconds, not in a seventh of one, and the whole of it every frame
        // cost the new shapes a twelfth of the frames. A quarter until 2026-10-18, when the clouds began to be read here as
        // the walk draws them, which costs half as much again a dot. NOT by throwing dots away here: with a `discard` anywhere in
        // this shader, this Mac's OpenGL (which is Metal underneath) cannot make the program ready for the card at all
        // ("buildPipelineState failed") and draws with it in software, saying so only in the game's Player.log; for a
        // night the game drew its clouds so, and the lab hung.)
        vec3 g = _CloudShadowAt.xyz + (_CloudShadowX.xyz * (uv.x - 0.5) + _CloudShadowY.xyz * (uv.y - 0.5)) * _CloudShadowAt.w;
        g = normalize(g) * (R + _CloudShadowX.w);
        vec3 s = _CloudSunFixed.xyz;
        // (The clouds read here as the walk draws them at each place: at the width of a pixel of its picture there (Clouds.cs gives
        // it, for a metre of distance, as _CloudJitter.w), so with the same shapes and the same detail, the lumps on the heaps
        // within 40 km as the walk has them, and the heaps giving way with distance as the walk's do (formStep told the distance,
        // from below the clouds; from above, as the walk from above, not). Read coarsely everywhere, as until 2026-10-18 (at 0.65,
        // without lumps), a heap's shadow was a soft round blot under a heap with a cauliflower's edge: "the clouds' shadows are
        // very janky. They aren't actually what the clouds are like", the owner, 2026-10-18. Measured in the lab against shadows
        // walked finely from every pixel of the ground: a third as far off as with the choice of near or far alone, and as steady
        // as the drawn clouds while they drift. _CloudJitter.z 1: without the lumps, for a map drawn whole in one frame, when
        // its square has been moved on or in a fast warp.)
        bool asDrawn = _CloudJitter.w > 0.0;
        // (g is in the planet's own axes; the weather map is read in the scene's, so it is turned back for that)
        vec3 gScene = g.x * _CloudFixedX.xyz + g.y * _CloudFixedY.xyz + g.z * _CloudFixedZ.xyz;
        vec3 sScene = s.x * _CloudFixedX.xyz + s.y * _CloudFixedY.xyz + s.z * _CloudFixedZ.xyz;
        // (through the shell as eased (_CloudShellEased): its 24 steps are laid between the floor and the top, and with the
        // shell's own, which jumps when the weather's map is made again or a storm grows, every one of them moved, and the
        // clouds' light and their shadow on the ground jumped with them)
        vec2 inner = sphere(gScene, sScene, R + _CloudShellEased.x);
        vec2 outer = sphere(gScene, sScene, R + _CloudShellEased.y);
        float t0 = max(inner.y > 0.0 && dot(gScene, gScene) < (R + _CloudShellEased.x) * (R + _CloudShellEased.x) ? inner.y : 0.0, 0.0);
        float t1 = outer.y;
        float depth = 0.0, lowest = 99999.0, highest = 0.0;
        // (And for the walk, which asks how much cloud lies between a place in a cloud and the sun further off than its own
        // few steps that way reach: how high the line is where, coming down from the sun, it is first a depth of one into
        // cloud, and of six; and how much cloud is on it in all. The line is therefore walked from its far end downward.)
        float fromTop = 0.0, high1 = -1000.0, high6 = -1000.0;
        if (t1 > t0 && dot(normalize(gScene), sScene) > -0.2)
        {
            t1 = min(t1, t0 + 30000.0);
            float dt = (t1 - t0) / 24.0;
            vec4 shadowWm = vec4(1.0, 0.0, 0.0, 0.0);
            // (the noise is moved as the camera's place in it is: the shadow's square is fixed to the planet)
            vec3 camFixed = vec3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
            float clearT = t1;
            vec4 wa = vec4(0.0), wb = vec4(0.0), wc = vec4(0.0);
            for (int n = 0; n < 24; n++)
            {
                float t = t1 - (float(n) + 0.5) * dt;
                vec3 p = gScene + sScene * t;
                float h = length(p) - R;
                vec3 q = _CloudNoise.xyz + (g + s * t - camFixed) / _CloudNoise.w;
                if (n == 0) shadowWm = broadAt(_CloudNoise.xyz + (g + s * (0.5 * (t0 + t1)) - camFixed) / _CloudNoise.w);
                // (the noise read a little coarsely, but not so coarsely that the far heaps' third reading comes in: the shadow is
                // of the heaps near the camera, which are drawn without it)
                vec4 stepWm = vec4(shadowWm.x * streetsAt(q, shadowWm), shadowWm.yzw);
                // (the weather read at every sixth step, and what the new shapes go by worked out there: along these few
                // kilometres it hardly changes, and worked out at all twenty-four it was most of what this map cost)
                for (int read = (n % 6) == 0 ? 0 : 1; read < 1; read++)
                {
                    weatherAt(p, wa, wb, wc);
                    for (int once = _CloudForm.x > 0.5 ? 0 : 1; once < 1; once++) { formWeather(p, wa, wb, wc); formPer = asDrawn ? _CloudJitter.w : formPer; formStep(q, stepWm, asDrawn ? length(p - _CloudCam.xyz) * (1.0 - aboveness()) : 0.0); }
                }
                // (the near shapes or the far field as the walk chooses them for this place, by how wide a pixel of its picture is
                // here (Clouds.cs gives it as _CloudJitter.w): by the map's own 512 dots, it was the far field everywhere)
                float fromCam = length(p - _CloudCam.xyz);
                formChoice = asDrawn ? fromCam * _CloudJitter.w : -1.0;
                float lodS = asDrawn ? min(log2(max(fromCam, 1.0) / 25000.0), 4.0) : 0.65;
                bool fineS = asDrawn && _CloudJitter.z < 0.5 && fromCam < 40000.0;
                float c = cloudAt(q, p, h, wa, wb, wc, stepWm, fineS, lodS);
                if (c <= 0.0) { clearT = t; continue; }
                // (an eighth of the cloud's own thickness: the walk here is coarse and without the small noise, and a shadow taken at
                // full strength is all or nothing, with the edge of each dot of the square in it)
                float add = c * _CloudLook.x * dt;
                // (the second form's at its full depth: its clouds are a sixth as dense through as the first form's heaps, and taken
                // at an eighth their shadows on the ground all but went)
                depth += add * (FORM_TWO > 0.5 ? 1.0 : 0.12);
                lowest = min(lowest, h);
                highest = max(highest, h);
                // (the first cloud from the top: where between the last clear place and this one it begins, halved three
                // times; a depth of one is some metres further in)
                float topT = t + 0.5 * dt;
                for (int first = high1 < -500.0 ? 0 : 1; first < 1; first++)
                {
                    float in_ = t, out_ = min(clearT, t + dt);
                    for (int b = 0; b < 3; b++)
                    {
                        float mid = 0.5 * (in_ + out_);
                        vec3 mp = gScene + sScene * mid;
                        float mc = cloudAt(_CloudNoise.xyz + (g + s * mid - camFixed) / _CloudNoise.w, mp, length(mp) - R, wa, wb, wc, stepWm, fineS, lodS);
                        if (mc > 0.0) in_ = mid; else out_ = mid;
                    }
                    high1 = length(gScene + sScene * (in_ - 1.0 / max(c * _CloudLook.x, 0.002))) - R;
                    // (what of this step lies above where the cloud begins is not cloud)
                    topT = in_;
                    add = c * _CloudLook.x * max(in_ - (t - 0.5 * dt), 0.0);
                }
                for (int sixth = high6 < -500.0 && fromTop + add >= 6.0 ? 0 : 1; sixth < 1; sixth++)
                    high6 = length(gScene + sScene * clamp(topT - (6.0 - fromTop) / max(c * _CloudLook.x, 0.002), t - 0.5 * dt, topT)) - R;
                fromTop += add;
            }
        }
        SV_Target0 = vec4(depth, lowest / 1000.0, highest / 1000.0, 1.0);
        SV_Target1 = vec4(high1 / 1000.0, high6 / 1000.0, min(max(fromTop, 0.0), 60.0), 1.0);
        return;
    }

    // The line of sight through this pixel.
    // (The walk's lines are moved off their pixels' middles, a different way each frame: see cloudskeep.glsl, where the frames
    // are taken together.)
    vec2 aim = stage < 2.5 ? uv + _CloudJitter.xy : uv;
    // (the far camera's: each dot's line through the kept pixel lookIn gives it this frame, and where in the dot that is written with
    // the distance (b, a) for cloudskeep.glsl; the near camera's all _CloudJitter's way, as before)
    vec2 aimIn = vec2(0.5);
    ivec2 keptPx = ivec2(gl_FragCoord.xy);
    int roundOf = 0;
    if (_CloudAim.w > 0.5)
    {
        aimIn = lookIn(ivec2(gl_FragCoord.xy), keptPx, roundOf);
        aim = uv + (aimIn - 0.5) * _CloudAim.xy;
    }
    vec3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (aim.x * 2.0 - 1.0) + _CloudUp.xyz * (aim.y * 2.0 - 1.0));

    // ---------------------------------------------------------------- 2: the walk
    float mu = dot(normalize(_CloudCam.xyz), dir);
    vec2 inner = shellFromCamera(mu, _CloudShell.x);
    vec2 outer = shellFromCamera(mu, _CloudShell.y);
    vec2 ground = shellFromCamera(mu, 0.0);
    float alt = _CloudHigh.x;
    float t0, t1;
    if (alt > _CloudShell.y)
    {
        // above the clouds: from where the line comes into the shell to where it reaches its floor, or leaves again
        if (outer.x < 0.0) { SV_Target0 = vec4(0.0, 0.0, 0.0, 1.0); SV_Target1 = vec4(0.0, 0.0, aimIn); return; }
        t0 = outer.x;
        t1 = inner.x > 0.0 ? inner.x : outer.y;
    }
    else if (alt > _CloudShell.x)
    {
        // inside the shell: from here to its floor, or to its roof
        t0 = 0.0;
        t1 = inner.x > 0.0 ? inner.x : outer.y;
    }
    else
    {
        // below the clouds: from the floor of the shell to its roof (nothing if the ground is in the way first; but the rain
        // under a storm and a funnel may still be: see below)
        t0 = inner.y;
        t1 = outer.y;
        if (ground.x > 0.0)
        {
            if (storms() == 0 && _CloudTwistA.w <= 0.0) { SV_Target0 = vec4(0.0, 0.0, 0.0, 1.0); SV_Target1 = vec4(0.0, 0.0, aimIn); return; }
            t0 = 0.0; t1 = -1.0;
        }
    }
    if (ground.x > 0.0) t1 = min(t1, ground.x);
    float t0e = t0, t1e = t1;
    {
        vec2 innerE = shellFromCamera(mu, _CloudShellEased.x), outerE = shellFromCamera(mu, _CloudShellEased.y);
        if (alt > _CloudShellEased.y) { t0e = outerE.x >= 0.0 ? outerE.x : t0; t1e = innerE.x > 0.0 ? innerE.x : outerE.y; }
        else if (alt > _CloudShellEased.x) { t0e = 0.0; t1e = innerE.x > 0.0 ? innerE.x : outerE.y; }
        else { t0e = innerE.y; t1e = outerE.y; }
        if (ground.x > 0.0) t1e = min(t1e, ground.x);
        t1e = max(t1e, t0e + 1.0);
    }
    // What the camera has drawn in the way (its depth picture: the farthest of the four pixels round where this line goes through
    // the picture; where what is kept is the walk's own size, the four at the corner of those this dot stands for). (Until
    // 2026-10-26 the four at the corner always, whichever of the sixteen pixels the line went through: all sixteen lines of a dot
    // stopped where its corner's did, and a heap behind a ridge was cut in a stair of four pixels that held still while the
    // lines moved, which no number of frames smoothed (the side-by-side's A2). The near camera's walk, a third of the picture's
    // size and its lines through its dots' middles, as before.)
    float stop = 1e9;
    if (_CloudRight.w > 0.5)
    {
        ivec2 full = textureSize(_CameraDepthTexture, 0);
        vec2 inFull = _CloudJitter.z > 0.5 ? aim * vec2(full) - 0.5
            : (gl_FragCoord.xy - 0.5) * vec2(full) / max(vec2(_CloudHigh.w, _CloudHigh.w * float(full.y) / float(full.x)), vec2(1.0));
        ivec2 at = clamp(ivec2(floor(inFull)), ivec2(0), full - 2);
        float a = texelFetch(_CameraDepthTexture, at, 0).r, b = texelFetch(_CameraDepthTexture, at + ivec2(1, 0), 0).r;
        float c = texelFetch(_CameraDepthTexture, at + ivec2(0, 1), 0).r, e = texelFetch(_CameraDepthTexture, at + ivec2(1, 1), 0).r;
        float z = _CloudAhead.w > 0.5 ? min(min(a, b), min(c, e)) : max(max(a, b), max(c, e));
        bool nothing = _CloudAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
        if (!nothing) stop = 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w) / max(dot(dir, _CloudAhead.xyz), 1e-4);
    }
    t0 = max(t0, _CloudStage.z);
    t1 = min(t1, stop);
    if (_CloudStage.w > 0.0) t1 = min(t1, _CloudStage.w);
    // (no further than 90 km into the shell: a line of sight along the horizon crosses hundreds of kilometres of it, which
    // the steps could only cover kilometres apart, in stripes; and beyond that the haze has the clouds anyway)
    t1 = min(t1, t0 + 90000.0);
    // (A line with no cloud to walk ends here, unless it meets a storm's rain or a funnel (below): until 2026-10-28 it ended at
    // once, and from under the clouds a curtain was cut off level at the horizon, or at a hill's outline, and from a mountain
    // looking down there was none.)
    bool noWalk = t1 <= t0;
    if (noWalk && storms() == 0 && _CloudTwistA.w <= 0.0) { SV_Target0 = vec4(0.0, 0.0, 0.0, 1.0); SV_Target1 = vec4(0.0, 0.0, aimIn); return; }
    if (noWalk) t1 = t0 - 1.0;

    // Steps: short near the camera, longer far away; never more than so many.
    float length0 = min(t1e, t0e + 90000.0) - t0e;
    int most = min(int(_CloudLook.w), 256);   // (no more than the loop below can take: a development build's Steps may ask more)
    // Each step as long as a hundredth of its distance from the camera (so what is near is walked finely and what is far, which
    // is small in the picture, coarsely), but never longer than a 24th of this line's stretch through the shell (seen from orbit
    // the layer is crossed in a few kilometres, and still wants two dozen steps), nor shorter than the shortest step given.
    // (Even steps, as at first, gave a storm 12 km off steps of a kilometre when the line went on 90 km: plates.)
    // (With fewer steps to spend than the middle setting's 88, each is as much longer: the lower setting walks as far as the
    // middle one, more coarsely. With the same steps and fewer of them, as at first, a line ran out before it reached a storm's
    // tower twelve kilometres off: no tower at all on the lower setting.)
    float coarser = max(1.0, 88.0 / max(_CloudLook.w, 1.0));
    float least = _CloudHaze.y * coarser, longest = max(least, length0 / 24.0);
    float each = 0.01 * coarser;
    float stepLen = clamp(t0 * each, least, longest);
    // (each line's own place in the step: on the far camera's walk from the blue noise, by the kept pixel the line goes through and
    // its dot's round, so that each kept pixel's own lines over the rounds have their places spread evenly and neighbouring kept
    // pixels' differ as blue noise does, which taken together over frames leaves less grain than a hash moved on by the golden
    // ratio (Wolfe et al. 2022); on the near camera's walk as until 2026-10-26, each pixel's own offset from a hash moved on by the
    // golden ratio each frame (a pattern such as interleaved gradient noise changes by a set amount from row to row, and what is
    // left of it after a few frames shows on a crisp edge as stripes))
    float jitter;
    if (_CloudAim.w > 0.5) jitter = blueAt(keptPx, roundOf & 63);
    else
    {
        uvec2 pix = uvec2(gl_FragCoord.xy);
        float own = float(mixBits(pix.x + mixBits(pix.y + 7919u)) & 65535u) / 65536.0;
        jitter = fract(own + 0.61803399 * _CloudStage.y);
    }
    float t = firstStep(t0, jitter, least, longest, each);
    stepLen = clamp(t * each, least, longest);

    vec3 sunDir = _CloudSun.xyz;
    float cosView = dot(dir, sunDir);
    // light that goes on mostly straight ahead, some of it back (kept apart: what goes straight on is dimmed less by the cloud
    // it passes, see the silver lining below); and again for light already scattered (spread more evenly)
    float phOn = 0.73 * phase(cosView, 0.86), phBack = 0.27 * phase(cosView, -0.22);
    float ph2 = mix(phase(cosView, 0.43), phase(cosView, -0.15), 0.3);
    // (the second form's, his: two narrow lobes for the light scattered once, a broad pair for what is scattered many times over)
    float ssA = phase(cosView, 0.95), ssB = phase(cosView, 0.8), msA = phase(cosView, 0.2), msB = phase(cosView, -0.4);
    vec3 camFixed = vec3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
    vec3 dirFixed = vec3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
    vec3 sunFixed = _CloudSunFixed.xyz;
    vec3 sunTop = _CloudSunTint.rgb * _CloudSun.w;

    vec3 light = vec3(0.0);
    float through = 1.0;
    float sumT = 0.0, sumW = 0.0;
    // (the storms' rain, each storm's walked first on its own and laid where the line first meets it, nearest first: see
    // rainWalk; and a tornado's funnel, where there is one near, kept the same way as a seventh (6), walked first, and each
    // storm's rain past it apart (7 on). Both end at the sea where the line meets it: a curtain over the sea, walked on to the
    // ground under the water, spent its steps under it, the review.)
    vec3 rL[13];
    float rTh[13];
    float rST[13];
    float rSW[13];
    float rF[13];
    for (int k = 0; k < 13; k++) { rL[k] = vec3(0.0); rTh[k] = 1.0; rST[k] = 0.0; rSW[k] = 0.0; rF[k] = 1e30; }
    float stopR = ground.x > 0.0 ? min(stop, ground.x) : stop;
    for (int once = _CloudTwistA.w > 0.0 ? 0 : 1; once < 1; once++) funnelWalk(dir, sunTop, sunDir, stopR, jitter, rL[6], rTh[6], rST[6], rSW[6], rF[6]);
    int rains = storms();
    for (int k = 0; k < rains; k++) rainWalk(stormA(k), stormB(k), stormC(k), dir, sunTop, sunDir, stopR, jitter, rL[k], rTh[k], rST[k], rSW[k], rF[k],
                                             rF[6], rL[7 + k], rTh[7 + k], rST[7 + k], rSW[7 + k], rF[7 + k]);
    float rainNext = 1e30;   // (the nearest front not yet laid)
    for (int k = 0; k < 13; k++) rainNext = min(rainNext, rF[k]);
    if (noWalk && rainNext >= 1e30) { SV_Target0 = vec4(0.0, 0.0, 0.0, 1.0); SV_Target1 = vec4(0.0, 0.0, aimIn); return; }
    vec4 wa = vec4(0.0), wb = vec4(0.0), wc = vec4(0.0), wmFrom = vec4(1.0, 0.0, 0.0, 0.0), wmTo = vec4(1.0, 0.0, 0.0, 0.0);
    float weatherAtT = -1e9, wmToT = -1e9;
    int empty = 0;
    int lit = 0;
    vec3 lastHere = vec3(0.0);   // the light of the last step taken in cloud (see where the walk ends)
    bool inCloud = false;        // and whether there was one
    bool clear = true;       // whether the last step was in clear air
    float clearT = max(t - clamp(t / (1.0 + each) * each, least, longest), 0.0);   // the last place along the line known to be clear air (the grid's step before the first: outside the shell, clear)
    float knownT = -1.0, knownC = 0.0;   // a place already looked at (the step after a cloud's edge), and how much cloud is there
    vec4 knownBody = vec4(1e5, 1.0, -1.0, 0.0);   // (and what the light asks of the body there: see heapsBody)
    vec2 knownDeck = vec2(0.0);                   // (and of its layer: see heapsDeck)
    vec4 knownForm = vec4(0.5, 0.5, -1.0, -1.0);  // (and what the second form's light asks there: see formLight)
    float lastToward = -1.0;
    float lastFar = 0.0;     // (and of that, what lies further towards the sun than the walk's own steps that way look: another cloud's)
    float lastFaces = 2.0;   // which way a heap's surface faces at the last lit step, as the cosine of its angle to the sun (2: not known)
    bool lastPads = false;   // whether the last lit step was in the middle sky's pads (see deepIn)
    float sigmaScale = _CloudLook.x;
    // (how far off a cloud's edge is still searched for (see below): 40 km, and from above the clouds at any distance)
    float edgeTo = 40000.0 / max(1.0 - aboveness(), 1e-4);
    int taken = 0;   // (how many steps the line has taken: for the development trials 101 and 102, below)
    for (int n = 0; n < 256; n++)
    {
        // (the rain and the funnel the line has come to by here, each laid whole, the nearest first)
        for (int m = 0; m < 13; m++)
        {
            if (t < rainNext) break;
            int k = 0;
            for (int j = 1; j < 13; j++) if (rF[j] < rF[k]) k = j;
            light += through * rL[k]; sumT += through * rST[k]; sumW += through * rSW[k]; through *= rTh[k];
            rF[k] = 1e30;
            rainNext = 1e30;
            for (int j = 0; j < 13; j++) rainNext = min(rainNext, rF[j]);
        }
        if (n >= most || t > t1 || through < 0.01) break;
        taken = n + 1;
        vec3 p = _CloudCam.xyz + dir * t;
        float h = length(p) - R;
        // (the weather is read again every kilometre or so along the line: it changes over tens of them)
        vec3 q = _CloudNoise.xyz + dirFixed * (t / _CloudNoise.w);
        // (What changes over kilometres is read there and 1200 m further on, and taken between the two at each step. Until 0.3.0
        // it was held as read until the next reading: the cover and the bend of the pattern then jumped at each reading, by a
        // hundred metres of bend and more, and the readings fall at nearly the same places for every pixel of a row of the
        // picture: far heaps were cut into level bands, a row of pixels showing the heap and the next the sky beside it.)
        // (In a loop that runs once or not at all, as the two ways of making heaps are in layerAt, and for the same reason:
        // written as a plain "if", the nine readings were made at every step and thrown away at all but one in a dozen.)
        for (int read = t > wmToT || t < weatherAtT - 1.0 ? 0 : 1; read < 1; read++)
        {
            // (read on a grid of 1200 m laid along the line from the camera, not from where the walk began: a floor or top of
            // the shell that moved moved every reading with it, and the clouds' shapes along the whole line changed)
            float gridT = floor(t / 1200.0) * 1200.0;
            vec3 pg = _CloudCam.xyz + dir * gridT;
            weatherAt(pg, wa, wb, wc);
            for (int once = _CloudForm.x > 0.5 ? 0 : 1; once < 1; once++) formWeather(pg, wa, wb, wc);
            wmFrom = wmTo;
            for (int first = wmToT > -1e8 && abs(gridT - wmToT) < 1.0 ? 1 : 0; first < 1; first++) wmFrom = broadAt(_CloudNoise.xyz + dirFixed * (gridT / _CloudNoise.w));
            weatherAtT = gridT; wmToT = gridT + 1200.0;
            wmTo = broadAt(_CloudNoise.xyz + dirFixed * (wmToT / _CloudNoise.w));
        }
        vec4 wm0 = mix(wmFrom, wmTo, clamp((t - weatherAtT) / 1200.0, 0.0, 1.0));
        vec4 wm = vec4(wm0.x * streetsAt(q, wm0), wm0.yzw);
        // (which size of the noise to read: the large noise's full size out to 25 km, the small one's to about 5 km; coarser beyond,
        // where heaps a few pixels across would otherwise sparkle)
        float lod = min(log2(max(t, 1.0) / 25000.0), 4.0);
        formChoice = 25000.0 * exp2(lod) * formPer;
        // (And from above all the clouds, however far: the lumps are what a heap and a bank are seen by from orbit, a
        // cauliflower and a lumpy top with its shade. Without them, past 40 km, a heap was a smooth egg (from orbit a field
        // of white pearls) and a bank a flat white lid with a smooth edge (ice floes). Read as coarsely as the distance
        // asks, as they are, they cost a reading or two more at each of the few steps a line from above takes.)
        bool fine = t < 40000.0 / max(1.0 - aboveness(), 1e-4);
        // (what the new shapes go by at this step, worked out once for all that is asked from it)
        formGatherHi = max(wmFrom.x, wmTo.x);
        formGatherLo = min(wmFrom.x, wmTo.x);
        for (int once = _CloudForm.x > 0.5 ? 0 : 1; once < 1; once++) formStep(q, wm, mix(t, t - t0e, aboveness()));
        formWay = dirFixed;
        formClimbs = dot(dir, p) / max(length(p), 1.0);
        formAhead = wmToT - t;
        bool knownStep = abs(t - knownT) < 0.01;
        float c = knownStep ? knownC : cloudAt(q, p, h, wa, wb, wc, wm, fine, lod);
        float open = heapsOpen, insideHere = heapsInside, near = heapsNear, smoothHere = heapsSmooth;
        // (the climb is told to this step's own reading alone: the second form's clearFor is worked out only for it)
        formClimbs = 2.0;
        // (What the light asks of the body the place is in (heapsBody). At the step after a cloud's edge, which is not asked again,
        // as it was when that step was first asked: what the globals hold there is what the walk towards the sun last asked about,
        // at a place that moves every frame, and a small puff's first step in would be lit now as a thick one, now as itself.)
        vec4 bodyHere = knownStep ? knownBody : heapsBody;
        vec2 deckHere = knownStep ? knownDeck : heapsDeck;
        vec4 formHere = knownStep ? knownForm : formLight;
        float dt = stepLen;
        // At the edge of a cloud (the first step into it after clear air): where between the last clear step and here it
        // begins, found to a quarter of the way by halving twice, and a step taken from there to here (lit there) before
        // this one. The edge is what is seen of a dense heap, which hides what is behind it within a few metres, and a step of
        // tens of metres would put it anywhere within the step: a blurred heap, its sunlit side lit as if from deep inside.
        // (Not walked in short steps instead: they spent the steps a line may take, and lines that ran out before the far side
        // of the heaps showed as stripes.)
        // (Out to 40 km; until 0.2.2 to 15, and from more than 2 km above the clouds to 15 until 2026-10-18, as a step there was
        // taken to be less than a pixel. It is, looking straight down; at a slant a step of 150 to 300 m is several pixels, and
        // past 15 km the tops of the banks were drawn as stacks of upright cards facing the camera, each where a step first
        // found cloud: the "flat floes with hard edges" seen from 2-3 km above the heaps (the main chat, 2026-10-18). And not
        // for a cloud of which eight tenths is already hidden by the cloud in front.)
        // (And from above the clouds at any distance, since the evening of 2026-10-18. From orbit a step is half a kilometre, a
        // 24th of the line's way through the shell, and a storm's tower takes up to a seventh of the light a metre: its first
        // step in was lit from anywhere up to half a kilometre inside it, twenty and more e-folds from the sun, and a hurricane's
        // outer towers were round grey pebbles about its white spiral (the main chat's picture from 200 km). Searched for, they
        // are white where the sun is on them. A line from above crosses few clouds, and the search is two readings a cloud.)
        if (c > 0.001 && clear && t > clearT + 1.0 && t < edgeTo && through > 0.2)
        {
            float lo = clearT, hi = t, chi = c;
            // (and the second form's light as the cloud nearest the edge asks it: see formLight. As the step's, its rim was lit as a
            // place up to three quarters of a step further in, with the body's light scattered many times over and not the edge's)
            vec4 formEdge = formHere;
            for (int b = 0; b < 2; b++)
            {
                float mid = 0.5 * (lo + hi);
                vec3 mp = _CloudCam.xyz + dir * mid;
                float mc = cloudAt(_CloudNoise.xyz + dirFixed * (mid / _CloudNoise.w), mp, length(mp) - R, wa, wb, wc, wm, fine, lod);
                if (mc > 0.001) { hi = mid; chi = mc; formEdge = formLight; } else lo = mid;
            }
            // (Lit from no more than ten metres past the last place known to be clear. The edge is somewhere in the quarter of
            // a step between that place and the first one known to be cloud: ten metres with a step of forty, near the camera,
            // but forty at 15 km and a hundred at 40, and lit from the far end of that, as until 0.2.2, a heap's sunlit side
            // was lit from where no sunlight is: every heap past 10 km a flat grey, a smear beside the nearer ones.)
            float litAt = min(hi, lo + 10.0);
            if (hi < t - 0.5)
            {
                knownT = t; knownC = c; knownBody = bodyHere; knownDeck = deckHere; knownForm = formHere;
                formHere = formEdge;
                // (the cloud over that step taken as the mean of its two ends: it thickens from nothing at the edge)
                c = 0.5 * (chi + c);
                dt = t - hi;
                t = hi;
            }
            p = _CloudCam.xyz + dir * litAt;
            h = length(p) - R;
            q = _CloudNoise.xyz + dirFixed * (litAt / _CloudNoise.w);
        }
        // (The second form: a line behind with its steps takes them longer, each as much longer than a hundredth of its distance as
        // the steps it has left need to reach the end of its stretch, growing with the distance as the steps do. Through a sky of
        // heaps as tall as the second form's (columns of 2.7 km), a line of sight a little under level from among them passes near
        // a heap's top or flank for tens of kilometres, where it may not stride, and spent its 88 steps short of the far heaps and of
        // a storm 15 km off: a lilac haze where they should have been (the towering view's stills of the evening of 2026-10-27, each
        // line that ran out painted, Try2 101). Its steps longer far off, where a pixel is wide, every line reaches its end. Not at
        // the step after a cloud's edge, which goes back to where the edge was found. Taken before the step's cloud is added, so that
        // a step made longer in cloud is added whole: taken after it, as at first, the cloud between the step's own length and the
        // longer one was passed over unread, and a long thin cloud seen by a line behind came out thinner than it is (the darker
        // flank of a storm against the same view at four times the steps, the evening of 2026-10-27).)
        for (int once = FORM_TWO > 0.5 && knownT < t + 0.5 ? 0 : 1; once < 1; once++)
        {
            float left = float(most - n - 1);
            // (each by its own share of that, a half to one and a half, a different one for every pixel and frame, as the strides
            // have: taken whole, every frame's line came to the same steps of the grid, and what lay between them was never read)
            // (The last of them whole, to the stretch's end: by a share under one, rounded to the grid's nearest step (below),
            // it fell short by more than the one step left after it, and the line ran out, where rounded up, as at first, every
            // step had run ahead and the last share seldom counted. In a model of the walk (the review of 2026-10-28) a fifth to a
            // third of the lines held back to the end ran out so, and 0.07-0.10 % of a view's on the pattern; taken whole, none.)
            if (left >= 1.0 && t1 > t + dt) dt = max(dt, t * (pow(t1 / max(t, 1.0), 1.0 / left) - 1.0) * (left > 1.5 ? 0.5 + fract(jitter * 3.71 + 0.618 * float(n)) : 1.0));
            // (and to the grid's nearest step to that, from half a step short of it: to the next step past it, as at first, a share
            // of a little over one step came out at two, and the steps behind time were a third longer than they asked on the mean,
            // the lines through a deck ending in 26 steps where they had 34 to 43 (the review of the evening of 2026-10-27). Still
            // at least the grid's next step, as half a step short of a share over one step is past this one.)
            if (dt > stepLen * 1.01) dt = firstStep(t + dt - 0.5 * clamp((t + dt) * each / (1.0 + each), least, longest), jitter, least, longest, each) - t;
        }
        clear = c <= 0.001;
        if (clear) clearT = t;
        if (c > 0.001)
        {
            float sigma = c * sigmaScale;
            vec3 up = p / max(length(p), 1.0);
            float sunUp = dot(up, sunDir);
            float hf = clamp((h - wa.z) / max(wa.w - wa.z, 50.0), 0.0, 1.0);
            // ---- the sun: through the air, then through the cloud between here and the sun (six steps, each longer)
            vec3 sunHere = sunTop * sunThroughAir(h, sunUp);
            float toward = lastToward, beyond = lastFar;
            // (walked again on every lit step, since 2026-10-21; with four steps instead of six beyond 8 km, where a cloud is small in
            // the picture. On every other one only, as until then, each step's light was the last one's half the time: the shaded
            // sides of heaps came out too bright, in bands, and speckled where the two differed, a grain over every heap that the
            // frames taken together did not take away (research/rendering/experiments/cloud-walk-noise.md: 18-32 % nearer the truth
            // at the same steps; the walk 3-5 % dearer, as it stops a few steps into a dense heap).)
            bool again = true;
            lit++;
            stormLod = lod;
            // (how much of the sky's light comes down to here past every cloud over it, from the light field where it holds the place)
            float skyFieldHere = 1.0;
            if (again && dot(sunHere, sunHere) > 1e-8)
            {
                toward = 0.0; beyond = 0.0;
                float lt = 0.0, ls = 28.0;
                // (the new shapes cost more to ask about than the old: five steps near, four past 4 km, three past 12)
                int towardSteps = _CloudForm.x > 0.5 ? (t > 12000.0 ? 3 : t > 4000.0 ? 4 : 5) : (t > 8000.0 ? 4 : 6);
                // (and three in a layer of pads, which is a few hundred metres thick: the sun's way out of it is in the first
                // three, and the steps past them asked about clear air over it. Looks the same, a twentieth less to pay. Not
                // in a sheet: a sheet of altostratus seen aslant was streaked with the bands of so few readings.)
                // (to the top of the pads' own column (columnOf), a low deck's a third deeper than what it uses of its layer: to that,
                // as until 2026-11-07 (_CloudSpec.w's 256), the deeper deck's upper part had five steps and its lower three, a band
                // of light across it)
                bool padsHere = false;
                lastPads = false;
                for (int L = 0; L < 2; L++)
                    if (formRaw[L].z > 0.5 && formKind[L].x < 0.02 && formKind[L].y < 0.02 && h > formMore[L].w - 50.0 && h < formMore[L].w + (deckNew() > 0.5 ? columnOf(L) : formKind[L].z) + 50.0) { towardSteps = min(towardSteps, 3); padsHere = true; lastPads = lastPads || L == 1; }
                // (each step read at its own place in its stretch, a different one for every pixel and frame: read always at the
                // middle, six readings of a heap with an edge to it can only come to so many sums, and its shade lay in flat
                // bands with steps between them, like a drawing shaded with three pencils)
                float place = fract(jitter * 5.371 + 0.618 * float(lit));
                formWay = sunFixed;
                bool usedField = false;
                // (The sun-side light field (go 9), where it is made and holds the place: read here, once, and used after the
                // walk's own first steps (below).)
                bool fieldOk = false;
                float fieldIn = 0.0, fieldCell = 0.0;
                vec4 nf = vec4(1.0);
                float skyRead = 1.0;
                if (_CloudLightG.w > 0.5) nf = lightFieldAt(p, fieldOk, fieldIn, fieldCell, skyRead);
                if (fieldOk && mod(floor(_CloudSpec.w / 8.0), 2.0) < 0.5) skyFieldHere = mix(1.0, skyRead, fieldIn);
                // (How many of its own steps the walk takes before it. The field's cells are 75 m wide out to 4.8 km from its
                // middle, 300 m to 19 km and 1.2 km beyond, and a heap's sunlit top and the folds in it are a few hundred metres
                // across and less: what a cell says of the first 180 m towards the sun is a blend of the cells round the place,
                // some of them in the heap and some in the clear air beside it, and a sunlit top 5 to 20 km off was shaded in grey
                // patches a cell wide; what one of 1.2 km says of the first 740 m darkened the tops further off as a whole (the
                // main chat's grey blotches, 2026-10-18). So the walk takes the first 180 m itself (its first three steps), past
                // the cells of 300 m the first 740 m (five), and reads the field for what lies beyond: in the lab, from 4.5 km over
                // heaps, light nearly half as far from the truth (1.2 against 2.2 / 255 off the light worked out in even steps)
                // for a seventh more of the walk, and from the ground (0.8 against 1.2) for a quarter more; with the field unused,
                // 1.1 and 1.3, for a fifth and a quarter more. Not in the far field, which is as coarse as the cells: its light
                // came hardly nearer the truth for the steps (research/rendering/experiments/heaps-from-orbit.md).)
                // (Not at all in the far field where its cells are 1.2 km: a dense top there was lit from the cells round it, some of
                // them inside, as if from within, and the field's square showed from orbit as a straight line across a hurricane,
                // the side within it greyer (the lab, the evening of 2026-10-18).
                // There, the far looks below, as before the field.)
                if (farGrow > 0.99 && fieldCell >= 500.0) fieldOk = false;
                int fieldAfter = !fieldOk || farGrow > 0.99 ? 0 : fieldCell < 500.0 ? 2 : 4;
                int stepsHere = max(towardSteps, fieldAfter + 1);
                vec3 walked = vec3(0.0);
                // (In a deck, pads' or sheet's, the billows read at their own fineness on the sun's third step as well, 81 to 182 m on,
                // and as coarsely as the light field's from its fourth: there a crown 40 to 60 m high shades the hollow in its lee,
                // with the sun low, as in his. From the third, as until 2026-11-07 (_CloudSpec.w's 512), the hollows were lit as the
                // crowns.)
                bool deckLit = deckLight() > 0.5 && (padsHere || deckHere.x > 0.5);
                for (int k = 0; k < 6; k++)
                {
                    if (k >= stepsHere) break;
                    // (the first at the middle of its stretch always: which way the surface faces is taken from it, and taken over a
                    // different distance each frame it came out different each frame: heaps like sandpaper)
                    float at = lt + ls * (k == 0 ? 0.5 : place);
                    vec3 lp = p + sunDir * at;
                    float lh = length(lp) - R;
                    vec3 lq = q + sunFixed * (at / _CloudNoise.w);
                    billowLight = FORM_TWO > 0.5 && k >= (deckLit ? 3 : 2) ? 3.0 : 0.0;
                    float got = cloudAt(lq, lp, lh, wa, wb, wc, wm, k < 2 && fine, k == 0 ? lod : lod + 0.5) * ls;
                    toward += got;
                    // (by the stretches the field is made in: steps 1 and 2 are its first, 28 to 182 m on, 3 its second, to 374 m,
                    // and 4 its third, to 739 m)
                    if (k == 1 || k == 2) walked.r += got; else if (k == 3) walked.g += got; else if (k == 4) walked.b += got;
                    // (Which way the heap's surface faces here, from its first reading towards the sun: the number of metres
                    // inside the surface falls by a metre for every metre straight out through it, and by less aslant. The
                    // cosine of the angle between the way out and the sun, near enough.)
                    if (k == 0)
                    {
                        lastFaces = insideHere > -1e8 && heapsInside > -1e8 ? clamp((insideHere - heapsInside) / max(at, 4.0), -1.0, 1.0) : 2.0;
                        // (For the new shapes, mostly by the heap without its lumps: light goes a hundred metres and more
                        // through cloud before it has forgotten which way it came, so a lump of thirty metres is not lit as a
                        // stone of that size would be, bright on one side and dark on the other; a heap is, and a tower.
                        // Lit by the lumps' own facing, every heap was a model in plaster.)
                        if (lastFaces < 1.5 && smoothHere > -1e8 && heapsSmooth > -1e8) lastFaces = mix(clamp((smoothHere - heapsSmooth) / max(at, 4.0), -1.0, 1.0), lastFaces, 0.25);
                    }
                    lt += ls;
                    // (After the walk's own first steps (three or five, above; in the far field one), the sun-side light field: what
                    // its other steps that way and its far looks would find, worked out beforehand for cells round the camera. The
                    // first step is always the walk's own: which way the surface faces is taken from it.)
                    if (k == fieldAfter && fieldOk)
                    {
                        nf = -log(max(nf, vec4(1e-7))) / 0.1;
                        // (the stretches the walk has just walked itself: its own steps in place of the field's cells, counted
                        // as the field's are, below)
                        if (fieldAfter >= 2) { nf.r = walked.r * sigmaScale; toward -= walked.r; }
                        if (fieldAfter >= 4) { nf.g = walked.g * sigmaScale; nf.b = walked.b * sigmaScale; toward -= walked.g + walked.b; }
                        // (What counts as near, the same at every distance. The walk's own steps count less of it the further
                        // off a place is, to save steps; read so from the field, which costs the same whatever is counted, the
                        // stretch from 346 to 711 m went at 4 km from counting for under half to counting in full: the shaded
                        // side of every heap a step darker past 4 km, and a heap coming nearer lit afresh at that distance (the
                        // lab, 2026-10-18). As the walk's own steps count it only towards the field's far edge, where they take
                        // over; and in a layer of pads.)
                        float walkNear = towardSteps >= 5 ? nf.g + nf.b : towardSteps == 4 ? nf.g : 0.0;
                        float nearF = nf.r + mix(walkNear, padsHere ? 0.0 : nf.g + nf.b, fieldIn);
                        toward += nearF / sigmaScale;
                        beyond = nf.r + nf.g + nf.b + nf.a - nearF;
                        usedField = true;
                        break;
                    }
                    // (deep in cloud already: what lies further on would dim what little diffuses through by a part of itself;
                    // taken as half as much again, and the walk left off)
                    // (The new shapes: only when far deeper. How much cloud lies within a few hundred metres towards the sun
                    // is what says whether a place is in a fold of a sunlit side or on the shaded side of the heap, and they
                    // are now lit very differently: see below.)
                    if (toward * sigmaScale > (_CloudForm.x > 0.5 ? 30.0 : 12.0)) { toward *= 1.5; break; }
                    ls *= 1.9;
                    place = fract(place + 0.618);
                }
                // (And further on, for a tower or another cloud kilometres away between here and the sun: three looks, one
                // somewhere in each of three stretches, each stretch twice the last (seven kilometres in all), at a different
                // place in it for every pixel and frame. Until the evening of 2026-10-12 it was one look, always two
                // kilometres on: what a tower's flank then took for the shadow of its neighbour was the shape of that
                // neighbour where a line happened to cross it at that one distance: a hard-edged blue patch the shape of
                // a lump, like a cave in the flank, and no shadow at all from a tower three kilometres off.)
                // (Where the light field has been read, it knows all of this already. Where it has not (not yet made, or the
                // place beyond its reach), as before the shadow's second map was made: three looks.)
                float stretch = 1000.0;
                billowLight = FORM_TWO > 0.5 ? 3.0 : 0.0;
                for (int k = usedField ? 3 : 0; k < 3; k++)
                {
                    if (toward * sigmaScale + beyond > 12.0) break;
                    float far = lt + stretch * place;
                    vec3 lp = p + sunDir * far;
                    beyond += cloudAt(q + sunFixed * (far / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 1.0) * stretch * sigmaScale;
                    lt += stretch; stretch *= 2.0;
                    place = fract(place + 0.618);
                }
                toward *= sigmaScale;
                lastToward = toward; lastFar = beyond;
                formWay = dirFixed;
                billowLight = 0.0;
            }
            else if (again) { toward = 0.0; lastToward = 0.0; beyond = 0.0; lastFar = 0.0; }
            stormLod = 100.0;
            // Light scattered once, twice, three times over (each time it carries further and spreads more evenly), and the
            // darkening of a cloud's sunlit edge where little light has yet been scattered towards the eye (the "powder").
            // (Its own thinness for the powder: as dense as it would be without a heap's water by height (heapsBody's y; stage 5). A
            // heap's base and lower sides are thin by the water they hold, not wisps: taken as wisps, every sunlit side with the sun
            // behind the camera went a seventh to a fifteenth greyer over its lowest twenty metres.)
            float cDry = c / max(bodyHere.y, 0.3);
            float powder = 1.0 - 0.55 * exp(-cDry * 6.0) * clamp(0.75 - 0.5 * cosView, 0.0, 1.0);
            // (scattered once, mostly straight on: the silver lining of a heap with the sun behind it; a few times, still forward
            // and carried further (Wrenninge's octaves); and very many times over, spread evenly: what diffuses through a thick
            // cloud falls off as one over its depth, not exponentially (the two-stream rule, 1 / (1 + 0.75 (1 - g) depth) with g
            // 0.85: about a tenth of the sun through a heap a kilometre thick), which is what lights the shaded side of a heap
            // grey and its base a darker grey, and keeps a heap against the sun dark inside its bright rim)
            // (The silver lining. A drop throws most of the light it scatters on within a few degrees of the way it was going, and
            // light scattered so is as good as not scattered at all: seen from near the line to the sun it is still sunlight. So
            // what goes straight on is dimmed by only the rest of what the cloud takes: 1 - g squared of it, 0.28 for g 0.85 (the
            // delta-Eddington rule), in a real heap, which takes 0.03 to 0.05 of the light in each metre; counted against the
            // 0.15 a metre these heaps are given (Clouds.cs: thick), a tenth. It comes through the outermost sixty metres or so of
            // a heap, and lights its billows and thin places from behind, within twenty degrees or so of the sun. Dimmed by all
            // the cloud takes, as until 0.2.2, it came through seven metres: less than a pixel of rim at any distance, which is
            // why the linings went thin when the heaps were made dense and sharp.)
            // (The rework of 2026-10-11: what diffuses through counted for most of the light at every depth (0.9 / (1 + 0.11 depth)), so a
            // heap was one tone where the sun reached it and another where it did not. Now the light scattered a few times carries most
            // of it and falls off through the cloud (two more octaves), and the diffused floor is a third of what it was.)
            // (0.3.0: what diffuses through by the two-stream rule itself, 0.55 / (1 + 0.11 depth): with the floor that stood here
            // before (a third of that at the base, falling off half as fast) the underside of a heap three hundred metres thick
            // came out at a fifteenth of its top, a dark plate under every small heap, where a real one's is a third.)
            // (And the whole of it under a half of what it was: with heaps that have a surface, most of what is seen of a heap is
            // within a few metres of that surface, where all of these are at their most, and every heap in the game was white
            // from top to bottom. A sunlit top now comes to about one, a shaded side to a third of that with the sky's blue
            // in it, the base of a thin heap to a fifth and of a thick one to a tenth.)
            // (A heap's surface is as bright as the sunlight falling on each square metre of it: the full sun where it faces
            // the sun, half where it is turned sixty degrees away, as a white wall is. Worked out from the depth of cloud
            // towards the sun alone, every part of a heap the sun reached at all was as bright as every other, and its sunlit
            // side one flat white. So what is scattered many times is weighed by the way the surface faces, near the surface
            // (deep inside there is no surface to speak of); 1 at forty-five degrees.)
            // (by the cloud's depth, not its metres, since stage 5: where a heap's water is thin, low on its sides, the sun and the eye
            // go as much further in, and its surface faces the sun as far in (heapsBody's y))
            float skin = lastFaces > 1.5 ? 0.0 : exp(-max(insideHere, 0.0) * bodyHere.y / 30.0);
            float faces = mix(1.0, 0.3 + 1.0 * clamp(lastFaces, 0.0, 1.0), skin);
            // (The rework of 2026-10-12: all of it at two thirds of what it was. A surface facing the sun came to 1.4 and one
            // turned sixty degrees from it to 1.0, and the picture's roll-off puts both within a twentieth of white: a heap's
            // sunlit side, and a sheet seen from above, was one flat white however its lumps faced. Facing the sun now comes
            // to 0.95, sixty degrees off to 0.65, edge-on to a third.)
            float level = _CloudForm.x > 0.5 ? 0.66 : 1.0;
            // (What has been scattered many times is as bright a hundred and fifty metres in from where the sun strikes as
            // at the surface itself: the same reason. Counted from the surface, the crease between two lumps on a sunlit
            // top was grey, and a field of heaps seen from above was dirty where a real one is one even white.)
            // (And not by all the cloud towards the sun alike. What the heap's own folds put in the way within a few hundred
            // metres counts for less than half, and its first hundred and fifty metres for nothing: a fold in a sunlit side
            // is lit from the sunlit cloud all round it, and is a little less white than the lumps beside it, not grey.
            // Counted in full, every fold in the game was a bruise. What another cloud puts in the way further off counts
            // in full: there is no sunlit cloud beside a place in a tower's shadow.)
            float all = toward + beyond;
            // (The middle sky's pads from their first e-fold towards the sun: a cloudlet of altocumulus is all skin and no heap,
            // and with the first ten given as nothing, as a heap's sunlit folds have them, every one was lit as a sunlit top
            // throughout, its underside too: one white, where photographs have a light grey middle and brighter margins.)
            float deepIn = _CloudForm.x > 0.5 ? (lastPads ? toward / 2.25 : max(toward - 10.0, 0.0) / 2.25) + beyond : all;
            // (The light scattered many times over is as much as the body the place is in is thick enough to give back (stage 5,
            // 2026-10-26): a slab of x, its depth times 1 - g with g 0.86 (the two-stream rule above), sends back x / (4/3 + x) of
            // what falls on it, counted here against x of 1.1, a heap 180 m through with its water by height (condensed), from which
            // a heap is as white as any. A puff 40 m through is lit to a quarter of that, one of 100 m to six tenths, one of 150 m to
            // nine tenths, and the sky shows through them; lit as if every body were a kilometre thick, every small puff and cloudlet
            // glowed as a heap's sunlit side does (the side-by-side films of 2026-10-25). Not the light scattered once, which a thin
            // edge has as much as a thick one: the silver lining. At 0.07 a metre whatever the Thickness setting, which says how much
            // shows through, not how white.)
            float xBody = 0.14 * 0.07 * bodyHere.x;
            float bodyShare = trialOff(1.0) ? 1.0 : clamp(xBody / (1.3333 + xBody) / 0.45, 0.0, 1.0);
            // ---- in a layer's sheet (stage 5, 2026-10-26): the sun's light by the depth to the sheet's own top, and deep in it by the
            // two-stream rule
            // (the layer whose body the place is in, if any: how much of it is sheet, and its base (heapsDeck). Chosen by the place's
            // height, as it first was, where two layers' margins met the base of one deck was lit by the other's, as if all the low
            // one's depth were under the middle one's base (the reviews of 2026-10-26).)
            float sheetHere = _CloudForm.x > 0.5 ? deckHere.x : 0.0, layerBase = deckHere.y;
            bool layersOff = trialOff(4.0);
            float deckOn = layersOff || bodyHere.z < 0.0 ? 0.0 : sheetHere;
            float upTop = max(bodyHere.z, 0.0), perM = bodyHere.w * sigmaScale;
            // (Where the walk towards the sun has stopped counting (its caps above: twelve to forty-five e-folds), the depth that way
            // to the sheet's own top over the place, worked out whole: a sheet is a slab from its base up, as dense through as its
            // middle, under a sphere. Without it, where the light field is not read, the sun showed through any depth of nimbostratus
            // at the caps' strength. Not where the walk counted it all: altostratus, through which the sun shows as through ground
            // glass, is thinner than that. And the sheet's own top, not its layer's: where the slab dips at a hole's margin, its top is
            // its top. (The way from R + h to the sphere of R + h + up, written so as not to lose it against the planet's radius:
            // up (2 (R + h) + up) over its root plus (R + h) sunUp.))
            float bS = (R + h) * sunUp, cS = upTop * (2.0 * (R + h) + upTop);
            float toTop = cS / max(sqrt(bS * bS + cS) + bS, 1.0);
            float allF = max(all, deckOn * perM * toTop * smoothstep(10.0, 14.0, all));
            // (The light gone on nearly straight is dimmed by 1 - g squared of the depth, 0.26 for g 0.86 (the delta-Eddington rule:
            // the silver lining above), in a sheet, which takes about what real overcast takes; in the heaps and pads by 0.15, the same
            // rule for a real heap's 0.04 a metre counted against the 0.07 they are given (Clouds.cs: thick). At the tenth set for
            // 0.15 a metre a heap's lining came through 143 m to the e-fold, where the rule has 95; and the sun's glow through 1.2 km
            // of nimbostratus at half the sun's own light (the side-by-side's A7, 2026-10-25): now two thousand times less, and the
            // sky there one grey with no sun in it, as in a nimbostratus.)
            // (In the sheet itself, not in all at its layer's height: a pad over the slab, and a storm's tower or a cyclone's cloud
            // where it crosses the layer, are dimmed as heaps are. By the layer's height alone, as it first was, their silver linings
            // were dimmed as a sheet's (the review of 2026-10-26). By how much the sheet has the place: heapsBody's w, which where the
            // sheet has it all is 0.3 or more (the thinnest kind's), and goes to nothing over the width the sheet and a pad are joined
            // over, and across the far field's handover where the near shapes have a pad; as a step where the one gives way to the
            // other, a pad's lining had a seam where it rose from the slab (the second look, 2026-10-26).)
            float kOn = layersOff ? 0.1 : mix(_CloudForm.x > 0.5 && !trialOff(8.0) ? 0.15 : 0.1, 0.26, deckOn * clamp(bodyHere.w / 0.3, 0.0, 1.0));
            float straightOn = exp(-allF * kOn);
            // (Deep in a sheet the sunlight has been scattered too often to remember its way, and goes as diffusion does: what reaches
            // a place falls off in a straight line from the top to the base, as (2/3 + x) over (2/3 + the whole), x the depth under the
            // place times 1 - g (the two-stream rule for a cloud that absorbs nothing; Eddington's); and it still drifts down, so what
            // is scattered towards the eye from above is brighter than from the side, by g mu over (2/3 + x) (it carries g of the
            // light's own tilt): at an overcast's base its zenith two and a third times its horizon, where measured overcasts have two
            // to three. Taken over from three to ten e-folds in, by the sheet's own top over the place and by the walk's count towards
            // the sun both, so that a sheet seen from above, a thin one and the margins of its holes are as they were; and not by the
            // way its surface faces, of which deep in a sheet there is none. By the old floor, which goes by the depth towards the sun
            // alone, the middle of a deep deck was half as bright as diffusion makes it, and the base of one of fifteen e-folds as
            // bright as two thirds of its top.)
            // (And all of it as much as the sunlight that falls on each square metre of the sheet's top over the place: the sine of the
            // sun's height there times 2/3 and it, by the same rule for a slab lit from a slant, counted against the sun overhead (a
            // third of that with the sun thirty degrees up; a twentieth at six). Without it the middle and the base of an overcast at
            // sunset were as bright as at noon, three times what the old floor made them and brighter than the sheet's own sunlit top
            // (the review of 2026-10-26). By the sun over the place, not where its light comes in: a sheet's top is not lit from below
            // its own level, and with the sun under that the light deep in it comes from where its top is lit, kilometres off.)
            // (And within the first few e-folds under the top, more: the slanting sunlight is still being given up there, and goes
            // as far in as 1 - g squared of the depth along it lets it (the delta-Eddington rule again), so that from the top down
            // the share goes from the whole to the sun's on the top. By the share on the top alone, with the sun thirty degrees up,
            // the light fell five and a half times between three and ten e-folds down, where the two-stream rule has it fall less
            // than twice (the second look, 2026-10-26: within a fifth of that rule's at every depth, a twentieth deep in).)
            float sunOnTop = clamp(sunUp, 0.0, 1.0) * (0.4 + 0.6 * clamp(sunUp, 0.0, 1.0));
            float sunDeep = sunOnTop + (1.0 - sunOnTop) * exp(-0.26 * perM * upTop / max(sunUp, 0.01));
            float muUp = dot(dir, up), xUp = 0.14 * perM * upTop, xDn = 0.14 * perM * max(h - layerBase, 0.0);
            float inDeck = trialOff(16.0) ? 0.0 : deckOn * smoothstep(3.0, 10.0, min(perM * upTop, allF));
            float deckFloor = 0.9 * max(0.6667 + xDn + 0.86 * muUp, 0.0) / (0.6667 + xUp + xDn) * sunDeep;
            float diffused = mix((0.30 * exp(-deepIn * 0.35) + 0.25 * exp(-deepIn * 0.12) + 0.35 / (1.0 + 0.11 * deepIn)) * faces, deckFloor, inDeck);
            vec3 sunlit = sunHere * ((straightOn * phOn + exp(-allF) * phBack + 0.35 * bodyShare * exp(-allF * 0.25) * ph2) * (4.0 * PI * 0.45) + diffused * bodyShare) * powder * level;
            // ---- the sky from above (less of it deep in the cloud and low down), the ground from below
            // (the sky's blue on what the sun does not reach: most on the tops, less on the flat base, which sees the sky only
            // at its horizon; the ground's light on the base)
            // (how much of the sky this place sees: for a heap made beforehand, as worked out when it was made, from its own
            // shape and its neighbours': open on its top and its shoulders, less in the creases between its turrets and on
            // its sides, none under its middle; for the rest, by how deep in the cloud and how high in the layer)
            float seesSky = open >= 0.0 ? mix(0.1, 1.3, open * open) : (1.0 - 0.45 * c) * mix(0.2, 1.15, smoothstep(0.0, 0.4, hf));
            vec3 sky = _CloudSky.rgb * seesSky * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) * (0.35 + 0.65 * dot(sunHere / max(sunTop, vec3(1e-4)), vec3(0.2126, 0.7152, 0.0722)));
            vec3 below = _CloudGround.rgb * (1.0 - hf) * 0.8;
            // (the night's own light, from the sky: more on what sees more of it, so that a heap at night has a top and an underside)
            vec3 here = sunlit + sky + below + vec3(_CloudSunTint.w) * (open >= 0.0 ? mix(0.35, 1.3, open) : 1.0);
            // ---- the second form's light: Blackrack's way, in our own words (the rebuild of 2026-10-27; research/clouds-r5)
            // The sun's light scattered once, in two narrow lobes, dimmed by all the cloud towards the sun; what is scattered many
            // times over, in a broad pair, falling off through the cloud as one over a square (deep in a heap more slowly than
            // exponentially), and as much of it as the place is inside its cloud: little at an eaten edge, all of it in the body,
            // so that the billows' rims are lit by the sun alone and their bodies glow (none of the first form's powder, facing,
            // body's thickness or diffused floor); deep in a sheet, the sheet's own diffusion as in the first form. The sky's light
            // as much as the sky is seen over the place, never under three tenths of it, and as the root of how far the place is
            // from its cloud's own inside: the cores are dark, the edges lit by the sky. The ground's only low in a cloud.
            for (int two = FORM_TWO > 0.5 ? 0 : 1; two < 1; two++)
            {
                bool stormy = formHere.w < 0.0;
                // (how high in its own column the place is: the second form's own (heapsBody's y, as fieldAt gives it), the walk's by the
                // weather's low layer for a storm's cloud. By the weather's layer, as first written, a heap drawn over a column four times
                // the layer's depth had its ground light gone above a quarter of its height, and the middle sky's none at all)
                float hf2 = stormy ? hf : clamp(bodyHere.y, 0.0, 1.0);
                float tau = allF, gain = 4.0 * PI * _CloudLight2.x;
                float once_ = exp(-tau) * (stormy ? 0.05 * ssA + 0.15 * ssB : 0.1 * ssA + 0.2 * ssB);
                float inCloud2 = 0.05 + 0.95 * smoothstep(0.0, 1.0, clamp(formHere.x + formHere.y, 0.0, 1.0));
                // (Over a sheet's top, the upper part of its column where its curve falls (from 0.3 to 0.45 of it up), no dark edge, and
                // the light scattered many times the less the deeper in the cover, as the square of one less the depth inside: what
                // comes out of a deck's top is light scattered near its surface, and a crease between two lumps lies deeper in the
                // cover than their crowns (2026-11-08, TODO 16). With the dark edge, the sun's light on a deck seen from above was a
                // smooth glow from under its lumps, dim at their surfaces (the trials of 2026-11-07); with this, drawn alone (trial
                // 131, and the light scattered many times alone, since taken out) it is lumped with grey creases, as the sky's is and
                // as his deck is. Together, with the sky's light at seven as until now, they sat so near the picture's white that the
                // lumps were all but lost; with the sky at five (Clouds.LightSky) they show. From 1.4 km over the deck_above view's
                // top (deckknobs.py, OpenGL, recordings/2026-11-08-pc/decksky-gl): with the dark edge, flat with the sky at seven, five
                // or three and a half. Only over the upper part, so that a sheet seen from below is lit as before. _CloudSpec.w's 2048
                // sets it back.)
                // (How fast it falls off into the creases, since 2026-11-09 a knob (_CloudDeck2, Clouds.DeckCrease2): one less the
                // depth inside to the power x, never under the share y of it. At the square and nothing kept, as first written, the
                // deck_above view's deck was a flat white sheet broken by a few deep creases, dark and lilac (the sky's light falls
                // off into them only as the root of the same), where his is an even field of softly shaded domes: its near band's
                // shading 12.3 against his 7.8 and its creases bluer than his (TODO 16).)
                float topLit = deckOn * smoothstep(0.3, 0.45, hf2) * deckTop(), outside = 1.0 - clamp(formHere.x, 0.0, 1.0);
                float creasePow = _CloudDeck2.x > 0.0 ? _CloudDeck2.x : 2.0;
                inCloud2 = mix(inCloud2, mix(clamp(_CloudDeck2.y, 0.0, 1.0), 1.0, pow(outside, creasePow)), topLit);
                float many = inCloud2 * (stormy ? 2.0 * msA + 0.2 * msB : 3.0 * msA + 0.3 * msB) * abs(formHere.w) / (1.0 + 0.35 * tau + 0.065 * tau * tau);
                vec3 sunlit2 = sunHere * (once_ * gain + mix(many * gain, deckFloor * 0.66 * abs(formHere.w), inDeck));
                float open2 = formHere.z >= 0.0 ? formHere.z : mix(0.3, 1.0, smoothstep(0.0, 0.6, hf2));
                // (the sky's colour seven tenths of itself and three of a white as strong as it: his tint, which leaves a heap's shaded
                // sides a blue grey and not the sky's own blue)
                // (and no more of it than comes down past every cloud over the place (the light field's sky part): the lesser of the two,
                // so that the place's own cloud over it is not taken twice)
                vec3 sky2 = mix(vec3(length(_CloudSky.rgb)), _CloudSky.rgb, _CloudAirHigh.w) * (min(max(0.3, open2), skyFieldHere) * sqrt(1.0 - clamp(formHere.x, 0.0, 1.0)) * _CloudLight2.y) * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) * (0.35 + 0.65 * dot(sunHere / max(sunTop, vec3(1e-4)), vec3(0.2126, 0.7152, 0.0722)));
                vec3 below2 = _CloudGround.rgb * ((1.0 - hf2) * (1.0 - hf2) * 0.8 * _CloudLight2.z);
                here = sunlit2 + sky2 + below2 + vec3(_CloudSunTint.w) * mix(0.35, 1.3, open2);
                // (development builds, trials 130 to 132 (Clouds.Try2): the light a term at a time, to find where a shade's colour comes
                // from: 130 the sky's alone, 131 the sun's alone, 132 the ground's and the tint's)
                if (_CloudForm.z > 129.5 && _CloudForm.z < 132.5) here = _CloudForm.z < 130.5 ? sky2 : _CloudForm.z < 131.5 ? sunlit2 : below2 + vec3(_CloudSunTint.w) * mix(0.35, 1.3, open2);
            }
            // ---- lightning inside the cloud (its light spreads through the cloud by being scattered over and over, so it falls
            // off with distance more than it is dimmed by the cloud on the way)
            if (_CloudFlashA.w > 0.0)
            {
                vec3 f = p - (_CloudCam.xyz + _CloudFlashA.xyz);
                float d2 = dot(f, f);
                float spread = _CloudFlashTint.w;
                here += _CloudFlashTint.rgb * _CloudFlashA.w / (1.0 + d2 / (spread * spread));
            }
            if (_CloudFlashB.w > 0.0)
            {
                vec3 f = p - (_CloudCam.xyz + _CloudFlashB.xyz);
                float d2 = dot(f, f);
                float spread = _CloudFlashTint.w;
                here += _CloudFlashTint.rgb * _CloudFlashB.w / (1.0 + d2 / (spread * spread));
            }
            lastHere = here; inCloud = true;
            // ---- what this step adds, and how much of what lies beyond it still shows (taken exactly over the step)
            float pass = exp(-sigma * dt);
            light += through * here * (1.0 - pass);
            sumT += t * through * (1.0 - pass);
            sumW += through * (1.0 - pass);
            through *= pass;
            empty = 0;
        }
        else
        {
            // (after a few steps of clear air, the long steps)
            empty++;
            lastToward = -1.0;
            // (Not with the new shapes. Their seeker can say "no cloud" only for strides shorter than a part of the
            // lattice's cell, which is near the camera; further off it says "perhaps" every time, and the walk then
            // asked twice at every step of clear air, the seeker and then the step itself: by count, half of all that
            // was asked in a fair sky and six tenths under a storm. They tell the walk how far it may stride instead.)
            // (Clear air, and no cloud within so many metres, as the field and the storms keep their shapes: on by that
            // much at once, but no more than a dozen steps' worth or 600 m, the cover and the storms being read afresh as
            // the walk goes. Without this a line of sight that began inside the layer of air the clouds are in, among heaps
            // or under a storm's anvil, spent its eighty-eight steps of forty metres on clear air and stopped three and a
            // half kilometres out: sky, where the anvil should have been.)
            // (And each stride by its own share of that, a half to the whole, a different one for every pixel and frame: striding the
            // whole way each time, every line of sight came up to a heap at the same places whatever its start, the walk's steps
            // no longer fell at different places from frame to frame, and the heaps were shaded in level bands.)
            // (The new shapes, far off: three steps' worth, as the old shapes' seeker strode. Past 20 km that is more than
            // the 600 m. And a fifth of the way come: a line of sight a little above level under a deck creeps towards it,
            // its gap closing by a fiftieth of each stride, and at 600 m a stride it spent its 88 steps before it reached a
            // deck 40 km off: clear sky between the deck's last and the horizon, the owner's "strip of light blue in the
            // distance" (2026-10-15). The distance it is told is along the line, and still holds the stride back near cloud.)
            float reach = min(12.0 * stepLen, 600.0);
            if (_CloudForm.x > 0.5) reach = max(reach, max(3.0 * stepLen, 0.2 * t));
            float before = dt;
            if (near < 0.0 && near > -1e8) dt = max(dt, min(-near * (0.5 + 0.5 * fract(jitter * 7.13 + 0.618 * float(n))), reach));
            // (The second form: where the clear air is known to the end of the weather's reading, on to there whole (to the claim's
            // own end, where the rounding leaves it a little short: never past what is known), the next step past it reading
            // the weather again. clearFor's claim ends there (formAhead), and by a share of it, as the other strides are taken, a line
            // came up to a reading's end in two or three steps: in a model of the walk on the pattern (the night of 2026-10-27), up to
            // 22 % more steps than strides that ran on past the reading took, and 55 % more on the level lines just under a column's
            // top. Taken whole, no more than those, but for under two more on those level lines. The share is for where a stride ends
            // near cloud, which is not where a reading ends.)
            if (FORM_TWO > 0.5 && near > -1e8 && near <= t - wmToT + 0.5 && wmToT - t <= reach) dt = max(dt, min(-near, wmToT - t));
            // (and on to the grid's next step from there: a stride decides which of the grid's steps are passed over, not where
            // the steps fall, so that a walk begun a few steps sooner or later meets each cloud at the same places. Only a step the
            // stride made longer: one the second form's steps behind time made longer is on the grid already, and snapped again
            // it went on a step more as the rounding fell. And one that comes to the reading's end, however little longer: left
            // on it, within a hundredth of a step, the weather was not read again until a step more (the review of 2026-10-28).)
            if (dt > before && (dt > stepLen * 1.01 || t + dt >= wmToT)) dt = firstStep(t + dt, jitter, least, longest, each) - t;
        }
        t += dt;
        stepLen = clamp(t * each, least, longest);
    }
    // (The walk stops where a hundredth of what lies beyond still shows (its first line). That hundredth is cloud too, the rest of
    // the heap, lit about as its last step was: taken so. Left to show through, as until 2026-10-26, the sky above the horizon and
    // the sea below it showed in the same dark cloud a level and a half apart, a level line across a deck at the horizon's height
    // (the side-by-side's A6, deck_rain).)
    // (the rain and the funnel the walk ended before, the nearest first)
    for (int m = 0; m < 13; m++)
    {
        if (rainNext >= 1e30) break;
        int k = 0;
        for (int j = 1; j < 13; j++) if (rF[j] < rF[k]) k = j;
        light += through * rL[k]; sumT += through * rST[k]; sumW += through * rSW[k]; through *= rTh[k];
        rF[k] = 1e30;
        rainNext = 1e30;
        for (int j = 0; j < 13; j++) rainNext = min(rainNext, rF[j]);
    }
    if (through < 0.01 && inCloud)
    {
        light += through * lastHere;
        sumT += t * through; sumW += through;
        through = 0.0;
    }
    // (how much still showed when the walk ended, before the high cloud's veil: for trial 101, below)
    float walkThrough = through;

    // ---------------------------------------------------------------- the high cloud: a thin sheet, drawn where the line crosses it
    if (_CloudShell.w > 0.0)
    {
        float hc = _CloudShell.z;
        vec2 cross0 = shellFromCamera(mu, hc);
        float tc = alt < hc ? cross0.y : cross0.x;
        if (tc > _CloudStage.z && tc < stop && (ground.x < 0.0 || tc < ground.x) && (_CloudStage.w <= 0.0 || tc < _CloudStage.w))
        {
            vec3 p = _CloudCam.xyz + dir * tc;
            vec4 ca, cb, cc;
            weatherAt(p, ca, cb, cc);
            vec3 q = _CloudNoise.xyz + dirFixed * (tc / _CloudNoise.w);
            // (long streaks drawn out along the wind)
            // (the noise read nearly seven times more slowly along the wind than across it: long fibres along the wind)
            vec3 streak = (q - _CloudAlong.xyz * dot(q, _CloudAlong.xyz) * 0.85) * 0.5;
            vec4 s = textureLod(_CloudShape, streak, 1.0);
            vec3 d = textureLod(_CloudDetail, q * 3.0 + vec3(0.0, 0.0, _CloudDrift.w * 0.3), 1.0).rgb;
            float amount = clamp(cb.g * _CloudShell.w, 0.0, 1.0);
            float wisp = clamp(remap(s.g * 0.6 + s.b * 0.4, 1.0 - amount * 0.8, 1.0, 0.0, 1.0), 0.0, 1.0);
            wisp = clamp(wisp - (1.0 - d.g) * 0.35, 0.0, 1.0);
            float slant = abs(dot(normalize(p), dir));
            // (The veil, cirrostratus: where the high cloud covers most of the sky there is, under the wisps, a thin even
            // sheet of ice from horizon to horizon, through which the sun still casts shadows. It is what comes a day
            // ahead of rain, and it is where the sun's ring is seen.)
            float veil = smoothstep(0.5, 0.9, amount) * (0.55 + 0.45 * s.r);
            float ice = (wisp * _CloudGround.w * 0.004 + veil * 0.22) / max(slant, 0.08);
            float alpha = 1.0 - exp(-ice);
            if (alpha > 0.002)
            {
                vec3 up = normalize(p);
                vec3 sunHere = sunTop * sunThroughAir(hc, dot(up, sunDir));
                // (The light the ice throws on towards the eye, at a third of what it was until 2026-10-13. As it was, a veil
                // 22 degrees from the sun came out twice as bright as a white wall in full sun, which thin ice is not (seven
                // tenths of one, by the sum): half the sky round the sun was one flat glare at the top of what the picture
                // can show, and nothing drawn in it could be seen.)
                vec3 lit = sunHere * (mix(phase(cosView, 0.65), phase(cosView, -0.2), 0.25) * 4.0 * PI * 0.33 + 0.25) + _CloudSky.rgb * 0.6;
                // ---- the sun's ring and the mock suns. Ice in high cloud is six-sided crystals, and light that goes in at
                // one side face and out at the next but one is turned by no less than 21.8 degrees (red a little less, blue
                // a little more) and mostly by little more than that: so the sky 22 degrees from the sun, all the way
                // round it, is brighter than the sky inside that, with a sharp reddish inner edge and a soft outer one.
                // Crystals that are flat plates fall level, and turn the light sideways only: two bright patches level
                // with the sun, 22 degrees to either side of it when it is on the horizon and further out the higher it
                // stands (none once it is above 61 degrees), red towards the sun, with a white tail away from it. Both
                // are light turned once: they show through thin ice and are lost in thick.
                for (int once = cosView > 0.5 && cosView < 0.96 ? 0 : 1; once < 1; once++)
                {
                    float away = degrees(acos(cosView));
                    vec3 past = vec3(away) - vec3(21.5, 21.85, 22.3);
                    vec3 ring = smoothstep(vec3(-0.4), vec3(0.2), past) * exp(-max(past, vec3(0.0)) / 2.4);
                    float sinS = dot(sunDir, up), sinV = dot(dir, up);
                    float turned = (1.7161 - sinS * sinS) / max(1.0 - sinS * sinS, 1e-4);
                    vec3 mock = vec3(0.0);
                    for (int low = turned < 3.9 && sinS > -0.05 ? 0 : 1; low < 1; low++)
                    {
                        float aside = 2.0 * asin(0.5 * sqrt(turned)) - 1.0472;
                        vec3 levelV = normalize(dir - up * sinV), levelS = normalize(sunDir - up * sinS);
                        float along = degrees(acos(clamp(dot(levelV, levelS), -1.0, 1.0)) - aside) * sqrt(max(1.0 - sinS * sinS, 0.0));
                        float above = degrees(asin(clamp(sinV, -1.0, 1.0)) - asin(clamp(sinS, -1.0, 1.0)));
                        vec3 pastMock = vec3(along) - vec3(-0.35, 0.0, 0.45);
                        // (plates are not everywhere in the cloud: the mock suns come and go as it drifts by)
                        mock = smoothstep(vec3(-0.5), vec3(0.3), pastMock) * exp(-max(pastMock, vec3(0.0)) / 5.0) * exp(-above * above / 7.0) * 7.0 * smoothstep(0.3, 0.6, s.r);
                    }
                    // ---- the arcs over the ring and under it (2026-10-17). Columns, crystals longer than they are wide,
                    // fall with their long axes level, every way round. Light turned through their side faces goes round
                    // the long axis: what it has along the axis it keeps, and what it turns, it turns by no less than the
                    // least that crystal allows a ray so far aslant to its faces (Bravais: such a ray goes as if the ice's
                    // index were sqrt(n^2 - sin^2 e) / cos e). For a place in the sky there is one way round a level column
                    // can send the sun's light there: its axis square to the way from the sun to the place. Where the turn
                    // that asks is the least that column allows, the light gathers: the upper and lower tangent arcs,
                    // touching the ring above and below the sun, spread into wings when the sun is low, and drawn in round
                    // the ring as the circumscribed halo when it is high. Red on the side towards the sun, as the ring.
                    // (Columns are not everywhere alike either: their own share of the cloud, not the plates', but never none
                    // in a veil: gone where the share was low, one wing of an arc was missing.)
                    vec3 axis = cross(up, dir - sunDir);
                    float axisL = length(axis);
                    vec3 arcs = vec3(0.0);
                    for (int columns = axisL > 1e-4 && sinS > -0.05 ? 0 : 1; columns < 1; columns++)
                    {
                        axis /= axisL;
                        float sinE = dot(sunDir, axis);
                        vec3 sp = sunDir - axis * sinE, vp = dir - axis * sinE;
                        float turnedBy = degrees(acos(clamp(dot(sp, vp) / max(length(sp) * length(vp), 1e-6), -1.0, 1.0)));
                        vec3 n = vec3(1.3062, 1.3104, 1.3165);
                        vec3 half_ = sqrt(max(n * n - sinE * sinE, vec3(1.0))) / sqrt(max(1.0 - sinE * sinE, 1e-4)) * 0.5;
                        vec3 pastArc = vec3(turnedBy) - (degrees(2.0 * asin(min(half_, vec3(1.0)))) - 60.0);
                        arcs = smoothstep(vec3(-0.4), vec3(0.2), pastArc) * exp(-max(pastArc, vec3(0.0)) / 1.8) * step(half_, vec3(1.0)) * 4.0 * mix(0.35, 1.0, smoothstep(0.35, 0.65, s.g));
                    }
                    lit += sunHere * (ring * 3.0 + mock + arcs) * exp(-1.5 * ice);
                }
                // ---- the moon by night (2026-10-17): a veil of ice lit by the Mun, forward most, and the same ring round it
                // as round the sun, in its light. (Until then the veil by night was lit by the sky alone, which is black.)
                for (int moon = _CloudMoon.w > 1e-6 ? 0 : 1; moon < 1; moon++)
                {
                    float cosMoon = dot(dir, _CloudMoon.xyz);
                    vec3 moonHere = vec3(0.92, 0.95, 1.0) * _CloudMoon.w;
                    lit += moonHere * (mix(phase(cosMoon, 0.65), phase(cosMoon, -0.2), 0.25) * 4.0 * PI * 0.33 + 0.25);
                    // (one ring for all colours: by moonlight the eye sees no colour, and a halo round the moon is white)
                    float pastM = degrees(acos(clamp(cosMoon, -1.0, 1.0))) - 21.85;
                    lit += moonHere * (smoothstep(-0.5, 0.3, pastM) * exp(-max(pastM, 0.0) / 2.4) * 3.0 * exp(-1.5 * ice));
                }
                // ---- the sun's pillar (2026-10-17): plates fall nearly level, rocking by a few degrees, and under a low sun
                // the light thrown back off their faces stands in a column over it and under it, about as wide as the sun
                // and some degrees tall, fading away from it. Thrown back once: through thin ice, as the mock suns.
                // (Measured from the eye's own level, not the cloud's there: near the horizon the veil the line meets is a
                // hundred kilometres off and more, where on a world as small as Kerbin the ground is turned ten degrees from
                // the eye's, and the sun stands that much higher.)
                vec3 upP = normalize(_CloudCam.xyz);
                for (int pillar = cosView > 0.97 && dot(sunDir, upP) < 0.14 && dot(sunDir, upP) > -0.06 ? 0 : 1; pillar < 1; pillar++)
                {
                    float sinS = dot(sunDir, upP), sinV = dot(dir, upP);
                    vec3 levelV = normalize(dir - upP * sinV), levelS = normalize(sunDir - upP * sinS);
                    float aside = degrees(acos(clamp(dot(levelV, levelS), -1.0, 1.0))) * sqrt(max(1.0 - sinV * sinV, 0.0));
                    float over = degrees(asin(clamp(sinV, -1.0, 1.0)) - asin(clamp(sinS, -1.0, 1.0)));
                    float column = exp(-aside * aside / 0.18) * exp(-abs(over) / 3.5) * smoothstep(0.0, 0.7, abs(over));
                    // (as bright at its foot as a third of the veil's own glow there: ice throws light back well at so low an
                    // angle, and at a fifteenth of that, as at first, it was lost in the glow round the sun)
                    lit += sunHere * (column * 30.0 * (1.0 - smoothstep(0.06, 0.14, sinS)) * mix(0.35, 1.0, smoothstep(0.3, 0.6, s.r)) * exp(-0.35 * ice));
                }
                if (alt < hc)
                {
                    // behind the low clouds
                    light += through * lit * alpha;
                    sumT += tc * through * alpha; sumW += through * alpha;
                    through *= 1.0 - alpha;
                }
                else
                {
                    // in front of them
                    light = lit * alpha + light * (1.0 - alpha);
                    sumT += tc * alpha; sumW += alpha;
                    through *= 1.0 - alpha;
                }
            }
        }
    }

    float meanT = sumW > 1e-5 ? sumT / sumW : 0.0;
    // ---------------------------------------------------------------- the air between the camera and the cloud
    if (sumW > 1e-5 && _CloudHaze.w > 0.0)
    {
        // Sunlight scattered towards the eye by the air on the way, and the cloud's own light dimmed by it (Rayleigh's and
        // haze's scattering, with the air thinning upward: six steps).
        vec3 tau = vec3(0.0);
        vec3 gained = vec3(0.0);
        float seg = meanT / 6.0;
        float pr = 3.0 / (16.0 * PI) * (1.0 + cosView * cosView);
        float pm = phase(cosView, 0.75);
        for (int k = 0; k < 6; k++)
        {
            vec3 p = _CloudCam.xyz + dir * (seg * (float(k) + 0.5));
            float h = max(length(p) - R, 0.0);
            vec2 dens = exp(-h / max(_CloudAirHigh.xy, vec2(100.0)));
            vec3 ext = _CloudAir.rgb * dens.x + vec3(_CloudAir.w * dens.y);
            vec3 sunHere = sunTop * sunThroughAir(h, dot(p / max(length(p), 1.0), sunDir));
            gained += exp(-tau - ext * seg * 0.5) * (_CloudAir.rgb * (pr * dens.x) + vec3(_CloudAir.w * pm * dens.y)) * sunHere * seg;
            tau += ext * seg;
        }
        vec3 airThrough = exp(-tau * _CloudHaze.w);
        // (and the air lit by the whole sky, not by the sun alone: light scattered again and again, which is why distance goes
        // pale blue-white and not the yellow that the sun's light alone, robbed of its blue on the way, would give)
        float camSun = clamp(dot(normalize(_CloudCam.xyz), sunDir) * 3.0 + 0.3, 0.0, 1.0);
        // (Of the sky's own colour as much as _CloudSky.w says, the rest a grey as bright (2026-11-09, TODO 16): the air seen along a
        // long line near the ground is lit by the whole sky, its pale horizon as much as its blue top, and so glows a paler blue than
        // the sky overhead. Of the sky's own deep blue, the glow over a far cloud was bluer than the cloud's own light was red after
        // its blue was lost on the way, and the two added left green short: the far bands near the horizon pink and lilac where
        // Blackrack's are cream.)
        vec3 skyHaze = mix(vec3(dot(_CloudSky.rgb, vec3(0.2126, 0.7152, 0.0722))), _CloudSky.rgb, _CloudSky.w) * 2.2 * camSun;
        light = light * airThrough + (gained * _CloudHaze.z + skyHaze * (1.0 - airThrough)) * _CloudHaze.w * (1.0 - through);
    }
    // ---------------------------------------------------------------- the clouds' shadows on the ground, seen from above them
    // From above all the clouds, where this line comes down to the ground: how much of the sun the clouds over that place
    // let through on its way down, along the sun's line through the shell there, in four steps. From orbit a heap and its
    // shadow beside it are what makes a field of them look like heaps and not like paint (photographs from the station of
    // fair-weather heaps over land: each a bright dot with its dark one beside it). The shadow's square round the camera
    // cannot give that from high up (a point of it is as wide as a heap), and is left off there (Clouds.cs). Only the
    // sun's share of the ground's light is taken, and that less the more air the line comes through: the air's own glow
    // over the ground is not in the shadow.
    float groundShade = 1.0;
    // (and how much of the land's light that takes away, handed to the shadows' pass, which tints it: grey, one number for the
    // three colours laid on with the alpha, the shade from above was the sunlit ground's own colour darkened, as it was below the
    // clouds until 2026-10-19. Nothing on the sea, whose shadow is worked out as light.)
    float landDark = 0.0;
    // (The ground as drawn where there is some: land stands above the sea's level, so the line meets it before the ball of
    // that level; where nothing is drawn (the world far off, drawn small by the game), that ball.)
    // (And on a world with a sea, the sea's surface where the line meets it first. What is drawn is known from the camera's
    // picture of depths, which is made of the solid things alone: Natural Ocean's sea and Scatterer's are drawn after it, and
    // over the sea it holds the seabed. The shadow was worked out on the bottom, and from 13 km over water half a kilometre
    // deep, looking 45 degrees down with the sun 40 degrees up, the heaps it was the shadow of stood a kilometre off.)
    float tGround = stop < 1e8 ? stop : ground.x;
    bool onSea = _CloudAirHigh.z > 0.5 && ground.x > 0.0 && ground.x <= tGround;
    if (onSea) tGround = ground.x;
    for (int once = aboveness() > 0.0 && tGround > 0.0 && tGround < 1e8 && through > 0.1 ? 0 : 1; once < 1; once++)
    {
        float tG = tGround;
        vec3 G = _CloudCam.xyz + dir * tG;
        vec3 upG = G / max(length(G), 1.0);
        float sunUpG = dot(upG, sunDir);
        if (sunUpG < 0.04) break;
        float hG = length(G) - R;
        vec3 qG = _CloudNoise.xyz + dirFixed * (tG / _CloudNoise.w);
        vec4 swa, swb, swc;
        weatherAt(G, swa, swb, swc);
        vec4 swm0 = broadAt(qG);
        vec4 swm = vec4(swm0.x * streetsAt(qG, swm0), swm0.yzw);
        for (int form = _CloudForm.x > 0.5 ? 0 : 1; form < 1; form++) { formWeather(G, swa, swb, swc); formStep(qG, swm, 0.0); }
        // (the four steps through the low layer, where the heaps are, and a little over it: spread through the whole shell,
        // up to the high cloud's height, one or two of them fell among the heaps)
        // (and from a little under its base: from the shell's floor, the four steps were some hundreds of metres apart, and came to a
        // heap 450 m thick once or not at all, a different way for each pixel in turn: shadows with dotted edges (the main chat,
        // 2026-10-18))
        float lowTop = _CloudForm.x > 0.5 && formRaw[0].z > 0.5 ? formMore[0].w + formMore[0].z + 300.0 : _CloudShell.y;
        float lowBase = _CloudForm.x > 0.5 && formRaw[0].z > 0.5 ? max(formMore[0].w - 50.0, _CloudShell.x) : _CloudShell.x;
        float fromS = max((lowBase - hG) / sunUpG, 0.0), toS = max((min(_CloudShell.y, lowTop) - hG) / sunUpG, fromS + 1.0);
        // (Twelve readings, with the heaps' lumps, where a shadow's outline can be seen (within 20 km of the camera), fewer and
        // without the lumps further off, down to four by 60 km. Four readings some 700 m apart through the layer, without the
        // lumps, smeared a heap's outline: each shadow a smooth dark oval with a grainy rim, where a real one has the heap's
        // lumpy outline and a crisp edge, and at a low sun the long shadows faint and smeared. Twelve with the lumps come within
        // a sixth of a level of 255 of the shadow read 64 times (from 8 km), where four were a level off (research/rendering/
        // experiments/cloud-shadows-from-above-shape.md). From orbit the outlines are less than a pixel, and twelve readings
        // over the whole disc cost a fifth more of the walk for nothing.)
        float nearShade = 1.0 - smoothstep(20000.0, 60000.0, tG);
        int readings = int(mix(4.0, 12.0, nearShade) + 0.5);
        bool lumps = nearShade > 0.5;
        float dS = (toS - fromS) / float(readings);
        float lodG = min(log2(max(tG, 1.0) / 25000.0), 4.0);
        formChoice = 25000.0 * exp2(lodG) * formPer;
        float depthS = 0.0;
        for (int k = 0; k < 12; k++)
        {
            if (k >= readings) break;
            float sk = fromS + (float(k) + jitter) * dS;
            vec3 pk = G + sunDir * sk;
            vec3 qk = qG + sunFixed * (sk / _CloudNoise.w);
            depthS += cloudAt(qk, pk, length(pk) - R, swa, swb, swc, swm, lumps, lodG) * sigmaScale * dS;
        }
        // (light scattered into the shadow from the cloud's sides softens it: an eighth of the cloud's own thickness, as the
        // shadow's square has it)
        float sunThrough = exp(-depthS * 0.125);
        float k = 1.25 * clamp(sunUpG * 2.5 + 0.15, 0.0, 1.0) * sunUpG;
        float share = k / (k + 1.0);
        float airT = exp(-(dot(_CloudAir.rgb, vec3(0.3333)) * max(_CloudAirHigh.x, 100.0) + _CloudAir.w * max(_CloudAirHigh.y, 100.0)) / max(abs(dot(dir, upG)), 0.08));
        groundShade = 1.0 - airT * share * (1.0 - sunThrough);
        // (On the sea, the share of what is seen that is the sun's (seaSunShare), taken away as light and given as the
        // picture's numbers, which is what this shade multiplies: the game draws the land as numbers, lit by numbers, and
        // its share is in those, but Natural Ocean works its sea out as light. Shaded as land is, every heap's shadow on the
        // open sea was nearly black: from 13 km a third of the sea's light beside it (the main chat's climb, 2026-10-18),
        // where photographs from an airliner at noon over trade cumulus have it nine to seven tenths on the deep blue, and a
        // fifth in the sun's glitter, where all of the sea's light is the sun's.)
        if (onSea) groundShade = pow(max(1.0 - seaSunShare(dir, upG, sunDir, hG, sunTop * sunThroughAir(hG, sunUpG)) * (1.0 - sunThrough), 0.0), 1.0 / 2.2);
        groundShade = mix(1.0, groundShade, aboveness());
        if (!onSea) landDark = 1.0 - groundShade;
        // (and where no cloud is on the line itself, the shadow is as far off as the ground, for what is in front of it)
        if (sumW <= 1e-5) { sumT = tG * 1e-4; sumW = 1e-4; meanT = tG; }
    }
    vec4 result = vec4(light, through * groundShade);
    // (Development trials (Clouds.Try2): 101, each line of sight that ran out of steps while still see-through painted red; 102,
    // each that took more than half of them green, the brighter the more. How the walk spends its steps, 2026-10-27.)
    // (Asked of what still showed when the walk ended, not after the veil: a dense veil, as at a grazing angle (to 0.94), hid
    // from it lines that ran out see-through behind it (the review of the evening of 2026-10-27).)
    if (abs(_CloudForm.z - 101.0) < 0.5 && taken >= most && walkThrough >= 0.01 && t <= t1) result = vec4(length(sunTop) * vec3(3.0, 0.0, 0.0), 0.0);
    if (abs(_CloudForm.z - 102.0) < 0.5 && taken >= most / 2) result = vec4(length(sunTop) * vec3(0.0, 3.0 * float(taken) / float(max(most, 1)), 0.0), 0.0);
    if ((floatBitsToInt(result.r + result.g + result.b + result.a) & 0x7F800000) == 0x7F800000) result = vec4(0.0, 0.0, 0.0, 1.0);
    SV_Target0 = result;
    // (how far off, in kilometres: half floats end at 65504)
    // (and, g, how much the shadow from above takes from the land's light here: cloudshade.glsl)
    SV_Target1 = vec4(meanT / 1000.0, landDark, aimIn);
}
#endif
