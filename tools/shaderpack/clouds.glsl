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
//   3.    Those small pictures taken together over frames (where each cloud was in the last one is worked out from how far
//         off it is), which smooths the steps away.
//   4.    The clouds' shadow: from points on the ground round the camera, how much cloud lies towards the sun.
// (What is drawn is put on the picture by cloudsover.glsl; the shadow is laid on the scene by cloudshade.glsl.)
//
// Places are kept relative to the planet's centre, in the scene's axes; the noise is looked up in the planet's own axes
// (so it turns with the planet and moves only with the wind), from where the camera is in it, which is worked out on the
// processor in double precision and kept small.
uniform vec4 _ZBufferParams;
uniform vec4 _CloudStage;    // x: which go (0, 1: the noise; 2: the walk; 3: over frames; 4: the shadow); y: the slice (noise) or the frame's place in the step (walk); z: where the walk may begin (m from the camera); w: where it must end (m), 0: no end but the shell's
uniform vec4 _CloudCam;      // xyz: the camera, from the planet's centre (m, the scene's axes); w: the planet's radius (m)
uniform vec4 _CloudHigh;     // x: the camera's height above the radius (m, worked out in double precision); y: the top of the air (m above the radius); z: the air's scale height (m); w: the picture's width in pixels (of what is drawn into)
uniform vec4 _CloudRight;    // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
uniform vec4 _CloudUp;       // xyz: the camera's up, times the tangent of half its height of view
uniform vec4 _CloudAhead;    // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
uniform vec4 _CloudFixedX;   // the planet's own axes, in the scene's: x towards latitude 0 longitude 0, y towards longitude 90, z the north pole
uniform vec4 _CloudFixedY;
uniform vec4 _CloudFixedZ;
uniform vec4 _CloudNoise;    // xyz: where the camera is in the noise (in lengths of the large noise; wrapped round only every 207 of them), w: metres to a length of the large noise
uniform vec4 _CloudDrift;    // xyz: which way the wind blows the clouds, in the planet's axes (a unit); w: how far the small noise has turned over, in its own lengths (it boils slowly)
uniform vec4 _CloudShell;    // x: the lowest a cloud's base can be (m above the radius); y: the highest a top can be; z: the high cloud's height; w: the high cloud's amount, as set
uniform vec4 _CloudLook;     // x: how thick cloud is (light lost per metre in the thickest); y: how much the small noise eats the edges; z: more or less cloud than the weather says (1 as it says); w: how many steps at most
uniform vec4 _CloudSun;      // xyz: towards the sun (the scene's axes); w: the sun's brightness above the air (as light)
uniform vec4 _CloudSunTint;  // rgb: the sun's colour above the air; w: 1 if the moon or another world lights the night (its strength)
uniform vec4 _CloudSky;      // rgb: the sky's light from above, where the sun is high (as light); w: how blue the sky's light is kept in shade
uniform vec4 _CloudGround;   // rgb: the light the ground throws up (as light); w: the high cloud's thickness (m)
uniform vec4 _CloudAir;      // rgb: how much a metre of air at the bottom scatters (each colour); w: the same for haze
uniform vec4 _CloudHaze;     // x: 1 if the noise has smaller copies to read far off (0: only the full one); y: the shortest step (m); z: how much more light the air adds than one scattering gives (light scattered again and again); w: how strongly distance fades the clouds (1 as worked out)
uniform vec4 _CloudFlashA;   // xyz: a flash of lightning, from the camera (m, the scene's axes); w: its brightness (0: none)
uniform vec4 _CloudFlashB;
uniform vec4 _CloudFlashTint; // rgb: the flash's colour; w: how far its light spreads in cloud (m)
uniform vec4 _CloudHoleA;    // xyz: a heap made as smoke is made (Heaps.cs), the middle of its base from the camera (m, the scene's axes); w: how far round it the noise's own heaps are cleared (m; 0: none)
uniform vec4 _CloudHoleB;
uniform vec4 _CloudHoleC;
uniform vec4 _CloudPrevAt;   // xyz: where the camera is from where it was last frame (m, the planet's axes); w: 1 if last frame's picture can be used
uniform vec4 _CloudPrevRight; // last frame's camera, in the planet's axes: right over the tangent of half the width of view; w: how much of this frame is taken (0 to 1)
uniform vec4 _CloudPrevUp;   // up over the tangent of half the height of view
uniform vec4 _CloudPrevAhead; // the way it looked
uniform vec4 _CloudShadowAt; // xyz: the middle of the shadow's square, from the planet's centre (the planet's axes, m); w: its side (m)
uniform vec4 _CloudShadowX;  // xyz: the square's two sides, in the planet's axes; w: the height of the ground it lies on (m above the radius)
uniform vec4 _CloudShadowY;
uniform vec4 _CloudSunFixed; // xyz: towards the sun, in the planet's axes
uniform vec4 _CloudStreets;  // xyz: across the wind, level, in the planet's axes (a unit; changed only when the wind has turned well round); w: how strongly heaps line up in streets along the wind (0 to 1)
uniform vec4 _CloudAlong;    // xyz: along the wind, the same way (for the high cloud's fibres)
uniform vec4 _CloudFieldA;   // the heaps made beforehand (see _CloudField). x: repeats of the field per metre; y: metres over which its number runs from none to all; zw: how far the wind has carried it east and north, in repeats
uniform vec4 _CloudFieldB;   // x: 1 if there is such a field (0: the heaps are carved from the noise, as before 0.3.0); yz: how many metres east and north the top of the layer leans with the wind from its base; w: how many metres the small lumps stand proud of the creases between them
uniform vec4 _CloudHabit;    // how the heaps come today. x: how much the small heaps that stand alone are left out (0: all of them there, a sky of many small puffs, which is a rare day; 1: the usual sky, fewer and larger); y: how many times larger over the ground the field is read than the layer's depth alone would have it (1; 1.6 on the usual day: broader, flatter heaps); z: how far a heap that reaches the layer's top spreads out under it, m (the lid: see layerAt); w: how far the heaps' outlines are pushed about, m (no heap round)
uniform vec4 _CloudFieldLevA; // the level of the field above which is cloud, for so much of the sky covered: 1/32, 3/32, 5/32 and 7/32 of it;
uniform vec4 _CloudFieldLevB; // 9/32 to 15/32;
uniform vec4 _CloudFieldLevC; // 17/32 to 23/32;
uniform vec4 _CloudFieldLevD; // 25/32 to 31/32
uniform sampler3D _CloudField;   // heaps made beforehand, lumps upon lumps (tools/clouds/heapfield.swift): a square of sky that repeats east and north, base to top. r: how far inside a heap's surface a place is (above a half) or outside it, less the heap's own number; g: the most r reaches within two cells of there (at every size of the field: for walking through clear air in long strides without stepping over a heap)
uniform sampler3D _CloudShape;   // the large noise: r, the rounded heaps (Perlin noise swelled by cells); g, b, a: cells at three sizes
uniform sampler3D _CloudDetail;  // the small noise: cells at three sizes
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
uniform sampler2D _CloudMap;     // the weather over the planet (longitude across, latitude up), its low clouds: r, how much cloud; g, what kind (0 flat sheets to 1 storm towers); b, the base and a, the top (in 8 km and 16 km)
uniform sampler2D _CloudMapB;    // r: how wet (rain darkens the base); g: high cloud; b: storm; a: how much middle cloud
uniform sampler2D _CloudMapC;    // the middle clouds: r, what kind; g, the base (in 12 km); b, the top (in 16 km); a, how wet
uniform sampler2D _CloudAirLut;  // how much sunlight comes through the air to a height (up) with the sun at an angle (across: its sine, -1 to 1)
uniform sampler2D _CloudNow;     // (go 3) this frame's walk: the light, and how much shows through
uniform sampler2D _CloudNowD;    // (go 3) and how far off the cloud is
uniform sampler2D _CloudHist;    // (go 3) the clouds as taken together up to last frame
uniform sampler2D _CloudHistD;
uniform sampler2D _CameraDepthTexture;
in vec2 vs_TEXCOORD0;
layout(location = 0) out vec4 SV_Target0;
layout(location = 1) out vec4 SV_Target1;

#define PI 3.14159265

float remap(float v, float a, float b, float c, float d) { return c + (v - a) / max(b - a, 1e-5) * (d - c); }

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

// The weather at a place (from the planet's centre, the scene's axes): the low clouds (amount, kind, base, top), the rest
// (wet, high, storm, middle amount) and the middle clouds (kind, base, top, wet).
void weatherAt(vec3 p, out vec4 a, out vec4 b, out vec4 c)
{
    vec3 f = vec3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
    float r = max(length(f), 1.0);
    float lon = atan(f.y, f.x);
    float lat = asin(clamp(f.z / r, -1.0, 1.0));
    vec2 uv = vec2(lon / (2.0 * PI) + 0.5, lat / PI + 0.5);
    a = textureLod(_CloudMap, uv, 0.0);
    b = textureLod(_CloudMapB, uv, 0.0);
    c = textureLod(_CloudMapC, uv, 0.0);
    a.z *= 8000.0; a.w *= 16000.0;
    c.y *= 12000.0; c.z *= 16000.0;
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
// How near any cloud can be to the place last asked about (cloudAt): less than nothing, at least so many metres off, and the
// walk may stride that far through clear air; nothing or more, here or unknown. Every source of cloud whose shape is kept as
// a distance says how near it is (the field's heaps, the storms' towers; a layer the place is above or below, by how far);
// one carved from noise, which knows no distances, says unknown (a great number).
float heapsNear;

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

// How much cloud of one layer there is at a point: q is the point in the noise (lengths of the large noise), h its height
// above the radius; then the layer: how much of the sky it covers, what kind, its base and top, how wet. "fine" says
// whether the small noise is used (it is left out in the light's walk and far away); lod which size of the large noise to
// read (0 its full size, below 0 nearer than that; the small noise is read 2.2 sizes finer).
float layerAt(vec3 q, vec3 p, float field, float h, float cover, float kind, float base, float top, float wet, vec4 wm, bool fine, float lod)
{
    // (a size of the noise past 50 says: seeking. Is there any cloud within a long stride of here? See the walk.)
    bool seek = lod > 50.0;
    if (seek) lod -= 100.0;
    top = max(top, base + 50.0);
    float hf = (h - base) / (top - base);
    if (hf <= 0.0 || hf >= 1.0) { heapsNear = max(heapsNear, hf <= 0.0 ? h - base : top - h); return 0.0; }
    cover = clamp(cover * _CloudLook.z, 0.0, 1.0);
    if (cover <= 0.001) { heapsNear = max(heapsNear, -200.0); return 0.0; }
    // (clusters, gaps and streets; a sheet over the whole sky is left whole: they are for skies of separate heaps)
    // (as a power, not a factor: a cluster thickens the cover towards all of the sky but never to all of it, where a factor
    // filled the layer from base to top in slabs with flat tops and straight sides)
    cover = mix(pow(cover, 1.0 / max(wm.x, 0.05)), cover, cover * cover);
    // (the noise bent, and the heaps leaning with the wind, more the higher they are)
    vec3 at = q + wm.yzw * 0.6 + _CloudDrift.xyz * hf * 0.08;
    // (and a second reading of the large noise, turned and at a scale that does not divide its repeat (20.7 km against 9),
    // says whether the heaps of the first grow here: the two together repeat only every 207 km; coarsely read, it changes
    // over kilometres, as a heap's surroundings do)
    vec4 otherAll = textureLod(_CloudShape, at.yzx / 2.3 + vec3(0.13, 0.47, 0.29), (max(lod, 0.0) + 1.0) * _CloudHaze.x);
    float other = otherAll.r;
    // (On the usual day far more so: the heaps stand crowded together in gatherings some kilometres across, touching, with
    // clear sky between the gatherings and only a straggler in it. With the spreading under the lid, below, a gathering
    // is one sprawling mass of cloud, lumpy on top and full of holes, and not a scatter of separate puffs.)
    float gathered = _CloudHabit.x;
    cover = pow(cover, 1.0 / mix(mix(0.55, 0.36, gathered), mix(1.6, 2.4, gathered), smoothstep(mix(0.62, 0.5, gathered), mix(0.86, 0.8, gathered), other)));
    // (heaps are read from the large noise half as fine again as sheets and towers are: a real fair-weather heap is some
    // hundreds of metres to two kilometres across and about as tall as it is wide; read at the noise's own size they came
    // out two to five kilometres across)
    // (heaps, from fair-weather ones to towering ones, are read from the field made beforehand, where there is one: the
    // rest of this, sheets and storms' towers, is carved from the noise)
    // (0.3.0: lumpy layers too, from a kind of 0.26: a layer of heaps pressed flat and crowded together until they touch is
    // what such a layer is; the field's heaps under a shallow top with most of the sky covered. `field` 2: the middle clouds,
    // which read the field turned and moved over, so that they are not the low clouds' own heaps again higher up.)
    float made = min(field, 1.0) * _CloudFieldB.x * smoothstep(0.2, 0.26, kind) * (1.0 - smoothstep(0.86, 0.95, kind));
    // (how wide a pixel of the picture is at this distance, in metres: what is read of the field and of the small noise is
    // no finer than that, and no coarser; `lod` is the distance, as a power of two of 25 km)
    float pixel = 25000.0 * exp2(lod) * 2.0 * length(_CloudRight.xyz) / max(_CloudHigh.w, 1.0);
    // From so far off that a pixel is wider than a small heap (120 to 260 m: beyond some forty to eighty kilometres from
    // the ground, and all of it from orbit) the noise's heaps stand in for the field's: no one heap can be told there, and the
    // field, a square of 6.4 km laid side by side, showed from orbit as the weave of a cloth. The noise's own heaps cover
    // far less than they are asked for (measured from straight above: 0.19 when asked for 0.46, 0.46 for 0.65, 0.79 for
    // 0.8), so they are asked for as much more as gives the cover wanted.
    float standIn = made * smoothstep(120.0, 260.0, pixel);
    made -= standIn;
    float asked = standIn > 0.0 ? min(1.0, 1.04 * pow(cover, 0.48) * (1.0 - 0.22 * cover)) : cover;
    float c = 0.0, spread = 0.0;
    // (Each of the two ways, the noise's and the field's, stands in a loop that runs once or not at all. Written as plain
    // "if this kind, else that", the graphics card's compiler works both out at every place and keeps one: a sky of heaps
    // from the field then paid for the noise's heaps as well, and a sky of sheets for the field, three times over in all.)
    for (int once = made < 0.999 ? 0 : 1; once < 1; once++)
    {
    heapsNear = 1e9;
    float narrow = mix(1.0, 1.45, smoothstep(0.3, 0.5, kind) * (1.0 - smoothstep(0.75, 0.9, kind)));
    vec4 s = textureLod(_CloudShape, at * narrow, max(lod, 0.0) * _CloudHaze.x);
    float heap = heapsOf(s);
    // Far off (beyond 40 km or so, where a heap is a few pixels and seen from high up a hundred of them at once, in the noise's
    // lattice), the heaps of a third reading of it, at a scale that does not divide its repeat either (10.35 km), taken in with
    // them: no lattice from orbit, nor rows of heaps along the horizon. (Only far off: near, it would blur the heaps' shapes.)
    if (lod > 0.7)
    {
        vec4 s3 = textureLod(_CloudShape, at.zxy / 1.15 + vec3(0.61, 0.23, 0.87), max(lod, 0.0) * _CloudHaze.x);
        heap = mix(heap, heapsOf(s3), 0.5 * smoothstep(0.7, 1.8, lod));
    }
    // (sheets are evener than heaps: the noise counts for less in them)
    heap = mix(mix(0.6, 1.0, heap), heap, clamp(kind * 3.0, 0.0, 1.0));
    float shape = heightShape(hf, kind);
    // (storm towers spread out at the top into an anvil)
    spread = clamp(kind * 2.0 - 1.0, 0.0, 1.0) * smoothstep(0.65, 0.9, hf);
    // (and the anvil is an even canopy, the noise counting for less in it and eating less of its edge: ragged with holes, it
    // read as scraps of cloud above the tower, not its anvil)
    heap = mix(heap, mix(0.65, 1.0, heap), spread);
    // Only the strongest of the noise is cloud where the cover is small: separate heaps, each dense inside (the cover is
    // not a thinning of the cloud, which would make thin smoke of a fair-weather sky).
    c = clamp(remap(heap * shape, 1.0 - min(1.0, asked * (1.0 + 1.5 * spread)), 1.0, 0.0, 1.0), 0.0, 1.0);
    }
    for (int once = made > 0.001 ? 0 : 1; once < 1; once++)
    {
        // (level underneath: the base is where the rising air begins to condense, the same height for every heap, and only
        // a little ragged)
        // (seeking: anywhere the small lumps could reach counts, and the base's own height does not)
        float size;
        float inside = heapsMetres(p, hf, top - base, cover, wm, lod, pixel, seek, field > 1.5, (otherAll.gb - 0.5) * (2.0 * _CloudHabit.w), size);
        // The lid. The layer's top is where the air stops rising: a warmer layer that the heaps cannot go through. A heap
        // that reaches it does not end in a dome there: it spreads out under it, into a pad wider than the heap that
        // feeds it, and the pads of neighbours run together. That, and not a sky of separate bulbs, is the usual sky
        // (the user, 2026-10-11: "usually the clouds aren't huge bulbs ... They're kind of sprawled out"; see
        // wiki/ksp-natural-weather/Kinds-of-cloud.md, 2). So towards the layer's top the heaps are grown outward, by more
        // in one stretch of sky than in another; a heap that does not come near the top is left as it is. Shallow layers
        // only (fair-weather heaps and lumpy sheets): a tower has its own top far above.
        // (never by as far as the field's numbers reach from a heap, 240 m at its own size: past that it knows only "far
        // from any heap", everywhere alike, and a pad grown so far was a sheet over all the sky with the small lumps'
        // lattice printed on it; and not evenly: holes and bays by a finer pattern)
        float lid = min(_CloudHabit.z * smoothstep(0.55, 0.88, hf) * (0.12 + 1.1 * smoothstep(0.42, 0.75, other)) * (0.45 + 1.1 * otherAll.b), 190.0) * size * (1.0 - smoothstep(0.52, 0.62, kind));
        inside += lid;
        // (seeking: anywhere the small lumps could reach counts, and the base's own height does not)
        if (seek) return inside + 0.52 * _CloudFieldB.w * size > 0.0 || c > 0.0 ? 1.0 : 0.0;
        // (how near a heap can be: its surface as the field has it, less what the small lumps can stand out by; taken at six
        // tenths, the field being pressed or stretched by the layer's depth and bent by what changes over kilometres)
        heapsNear = max(heapsNear, (inside + 0.52 * _CloudFieldB.w * size * (size > 1.5 ? 1.8 : 1.0) + 6.0 + 0.6 * pixel) * 0.6);
        inside = heapsLumps(inside, at, pixel, size);
        // (The surface is a few metres of clear air mixing in, no more; far off, as wide as a pixel of the picture: how much
        // of the pixel is inside the surface, taking the surface to cross it straight. Cut off sharply there instead, a far
        // heap is a pile of plates.)
        float edge = max(9.0, 1.2 * pixel);
        heapsInside = inside;
        float heaps = clamp(inside / edge + 0.5, 0.0, 1.0) * clamp(hf / 0.02, 0.0, 1.0);
        c = mix(c, heaps, made);
        spread *= 1.0 - made;
        fine = fine && made < 0.999;
    }
    if (c <= 0.0) return 0.0;
    float whole = c;
    if (fine)
    {
        // The small noise eats the edges: wisps at the bottom, rounded billows higher up, at two sizes (a kilometre to 60 m,
        // and two and a half times finer again), which is what makes a heap a cauliflower and not a ball.
        // (a storm's tower is several times a heap's height, and its billows larger: the small noise read at under half the
        // size there, else the tower is foam; and eating less of it, else its billows stand apart like bubbles)
        bool tower = kind > 0.9;
        float big = tower ? 4.0 : 9.0, bigger = tower ? 10.0 : 23.0, coarser = tower ? -1.17 : 0.0;
        vec3 d = textureLod(_CloudDetail, at * big + vec3(0.0, 0.0, _CloudDrift.w), max(lod + 2.2 + coarser, 0.0) * _CloudHaze.x).rgb;
        float finer = textureLod(_CloudDetail, at * bigger + vec3(0.37, 0.11, _CloudDrift.w * 1.7), max(lod + 3.5 + coarser, 0.0) * _CloudHaze.x).r;
        float small = (d.r * 0.625 + d.g * 0.25 + d.b * 0.125) * 0.72 + finer * 0.28;
        // (it lies mostly within a tenth of 0.48, measured: spread out, for billows with an edge to them)
        small = clamp((small - 0.48) * 2.6 + 0.5, 0.0, 1.0);
        float eat = mix(small, 1.0 - small, clamp(hf * 10.0, 0.0, 1.0));
        // (and it eats little at the base, which is the flat height where the rising air begins to condense, ragged only a
        // little: fully from a fifth of the way up)
        c = clamp(remap(c, eat * _CloudLook.y * mix(0.5, 1.0, smoothstep(0.0, 0.2, hf)) * (1.0 - 0.5 * spread) * (tower ? 0.65 : 1.0), 1.0, 0.0, 1.0), 0.0, 1.0);
    }
    // (full density a third of the way in from where the noise begins it: a real heap's edge is a few metres of clear air
    // mixing in, not a hundred metres of thin mist, which softened every heap into cotton wool; a sheet's underside stays
    // softer, else a deck seen far off ends in a hard line above the horizon)
    // (never solid at once: a soft curve in place of a limit, so that a cloud's edge is a gradient however dense its middle;
    // taken, as the next two changes are, from how the Solas shader pack's clouds are made, which the user likes)
    { float d = c * mix(1.3, 2.3, smoothstep(0.15, 0.45, kind)); c = d / sqrt(d * d + 0.4); }
    // (a sheet is thinner stuff than the core of a heap: a grey overcast lets a share of the light through, where a sheet as
    // dense as a heap's core is dark as a storm's base)
    c *= mix(0.3, 1.0, smoothstep(0.1, 0.45, kind));
    // (the thinnest wisps let go: else a clear sky is speckled with crumbs of cloud)
    // (and among heaps, what was barely cloud before the small noise ate at it: the crumbs that floated round every heap)
    c *= smoothstep(0.0, 0.1, c) * mix(1.0, smoothstep(0.04, 0.2, whole), smoothstep(0.3, 0.5, kind));
    // (rain clouds are thicker and darker underneath)
    return c * (1.0 + wet * (1.0 - hf));
}

// The storms near the camera by number (0 the nearest), and how many there are. They are walked in a loop whose length is
// not known until the picture is drawn, and so is written out once: written out six times over, one after another (as until
// 0.3.0), the six copies of a storm's tower cost a sky with no storm in it two thirds of its time on this Mac's graphics card.
vec4 stormA(int k) { return k < 3 ? (k == 0 ? _CloudCellA0 : k == 1 ? _CloudCellA1 : _CloudCellA2) : (k == 3 ? _CloudCellA3 : k == 4 ? _CloudCellA4 : _CloudCellA5); }
vec4 stormB(int k) { return k < 3 ? (k == 0 ? _CloudCellB0 : k == 1 ? _CloudCellB1 : _CloudCellB2) : (k == 3 ? _CloudCellB3 : k == 4 ? _CloudCellB4 : _CloudCellB5); }
int storms() { return _CloudCellA0.w <= 0.0 ? 0 : _CloudCellA1.w <= 0.0 ? 1 : _CloudCellA2.w <= 0.0 ? 2 : _CloudCellA3.w <= 0.0 ? 3 : _CloudCellA4.w <= 0.0 ? 4 : _CloudCellA5.w <= 0.0 ? 5 : 6; }

// One storm or shower. Where there is a field of heaps made beforehand: a tower of rising air round its middle, widest at the
// base, with the field's heaps crowded on it and round it (all of them at the middle, fewer outward: the flanking towers of a
// real storm, each lower than the last), lumps carved on the lot; and at its top the anvil, ice spread out downwind under the
// height where the air stops rising, thin and streaked. Where there is no field, as before 0.3.0: a tower and its anvil
// carved from the noise.
float cellAt(vec3 p, float h, vec3 q, vec4 a, vec4 b, vec4 wm, bool fine, float lod)
{
    if (b.z < 0.15 || a.w <= 0.0) return 0.0;
    float deep = max(b.y - b.x, 100.0);
    float frac = (h - b.x) / deep;
    if (frac <= 0.0 || frac >= 1.0) { heapsNear = max(heapsNear, frac <= 0.0 ? h - b.x : b.y - h); return 0.0; }
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    // (the anvil is carried downwind of the tower, the more the higher: the wind at the top of the air is the strongest)
    vec3 wind = _CloudDrift.x * _CloudFixedX.xyz + _CloudDrift.y * _CloudFixedY.xyz + _CloudDrift.z * _CloudFixedZ.xyz;
    vec3 from = p - wind * (a.w * 0.9 * smoothstep(0.6, 1.0, frac));
    float d = length(from - mid * dot(from, mid));
    float strength = smoothstep(0.15, 0.5, b.z);
    for (int once = _CloudFieldB.x > 0.5 ? 1 : 0; once < 1; once++)
    {
        float r = a.w * (0.68 + 0.12 * (1.0 - frac)) * (1.0 + 1.4 * smoothstep(0.7, 0.95, frac));
        if (d > r * 1.3) { heapsNear = max(heapsNear, r * 1.3 - d); return 0.0; }
        heapsNear = 1e9;
        float within = smoothstep(r * 1.3, r * 0.45, d) * strength;
        return layerAt(q + vec3(0.31, 0.17, 0.53), p, 0.0, h, within, 1.0, b.x, b.y, b.w, vec4(1.0, wm.yzw), fine, lod);
    }
    float reach = a.w, anvilReach = a.w * 2.2;
    if (d > max(reach * 1.15, anvilReach * 1.05)) { heapsNear = max(heapsNear, max(reach * 1.15, anvilReach * 1.05) - d); return 0.0; }
    bool seek = lod > 50.0;
    if (seek) lod -= 100.0;
    float pixel = 25000.0 * exp2(lod) * 2.0 * length(_CloudRight.xyz) / max(_CloudHigh.w, 1.0);
    // The tower: widest where it stands, narrowing upward, leaning a third as far as the anvil is carried, and nowhere
    // round: its width swells and shrinks by a third with the large noise, read over kilometres (a straight column with
    // lumps on it was a pillar of popcorn). Rounded off under the top, where the anvil takes over.
    vec3 lean = p - wind * (a.w * 0.3 * frac);
    vec3 off = lean - mid * dot(lean, mid);
    vec4 swell = textureLod(_CloudShape, (q + vec3(0.31, 0.17, 0.53)) * 1.7, 2.0 * _CloudHaze.x);
    float column = reach * (0.60 - 0.30 * frac + 0.10 * frac * frac) * (0.55 + 0.45 * strength) * (0.62 + 0.8 * swell.r) - length(off);
    column = min(column, (0.97 - frac) * deep);
    // (the field's heaps on it and round it: only as far out as there are any)
    float size = 1.0, heaps = -1e5;
    float crowd = clamp(1.1 - d / reach, 0.0, 0.93) * strength;
    for (int once = crowd > 0.01 ? 0 : 1; once < 1; once++) heaps = heapsMetres(p, frac, deep, crowd, wm, lod, pixel, seek, false, vec2(0.0), size);
    float inside = max(column, heaps);
    // The anvil: ice spread out under the height where the air stops rising, downwind: thickest round the tower, thinning
    // to a ragged edge drawn out along the wind, its underside hanging lower near the tower; a third as thick as the tower.
    float out_ = d / anvilReach;
    float under = mix(0.80, 0.70, 1.0 - smoothstep(0.1, 0.5, out_));
    // (how far the place is from where the anvil can be: below its underside, or out beyond its rim)
    float anvilOff = max((under - 0.06 - frac) * deep, d - anvilReach);
    if (seek) return inside + 0.52 * _CloudFieldB.w * size > 0.0 || anvilOff < 0.04 * 25000.0 * exp2(lod) ? 1.0 : 0.0;
    heapsNear = max(heapsNear, max((inside + 0.52 * _CloudFieldB.w * size * (size > 1.5 ? 1.8 : 1.0) + 6.0 + 0.6 * pixel) * 0.6, -anvilOff));
    // (how much sky is seen: of a heap, as the field has it; of the tower, by how high and how deep in it)
    float open = heaps >= column ? heapsOpen : clamp(0.1 + frac - column / 500.0, 0.02, 1.0);
    float c = 0.0;
    for (int once = inside > -400.0 ? 0 : 1; once < 1; once++)
    {
        float next;
        if (size < 1.5) size = heapsSize(deep, next);
        inside = heapsLumps(inside, q + wm.yzw * 0.6, pixel, size);
        float edge = max(9.0, 1.2 * pixel);
        c = clamp(inside / edge + 0.5, 0.0, 1.0) * clamp(frac / 0.012, 0.0, 1.0);
    }
    float slab = smoothstep(under - 0.06, under + 0.03, frac) * (1.0 - smoothstep(0.93, 0.995, frac));
    float ragged = clamp(0.25 + 1.1 * swell.g, 0.0, 1.0);
    float anvil = slab * (1.0 - smoothstep(0.25 + 0.5 * ragged, 1.0, out_)) * strength * 0.33;
    if (anvil > c) { c = anvil; heapsInside = -1e9; heapsOpen = clamp(frac * 1.2 - 0.3, 0.0, 1.0); }
    else { heapsInside = inside; heapsOpen = open; }
    return c * (1.0 + b.w * (1.0 - frac));
}

// Under a raining storm, its rain: a grey curtain from the base to the ground, in streaks (the noise drawn out up and down),
// thinning towards its edge and near the ground where the drops have spread out. Thin as cloud goes: heavy rain lets one see
// a kilometre or two.
float rainAt(vec3 p, float h, vec3 q, vec4 a, vec4 b)
{
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    float d = length(p - mid * dot(p, mid));
    if (d > a.w * 0.75 || h > b.x || h < 0.0) return 0.0;
    // (the streaks read from the storm's own middle, drawn out ten times up and down)
    vec3 pf = vec3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
    vec3 up = pf / max(length(pf), 1.0);
    vec3 mf = vec3(dot(mid, _CloudFixedX.xyz), dot(mid, _CloudFixedY.xyz), dot(mid, _CloudFixedZ.xyz));
    vec3 qs = (pf - mf * length(pf)) / _CloudNoise.w + up * (h / _CloudNoise.w) * 0.1;
    float streak = textureLod(_CloudShape, qs * 2.3 + vec3(0.7, 0.1, 0.4), 0.0).g;
    float fall = clamp(h / max(b.x, 100.0), 0.0, 1.0);
    return b.w * 0.08 * smoothstep(a.w * 0.75, a.w * 0.25, d) * smoothstep(0.35, 0.75, streak) * (0.45 + 0.55 * fall);
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

float cloudAt(vec3 q, vec3 p, float h, vec4 wa, vec4 wb, vec4 wc, vec4 wm, bool fine, float lod)
{
    heapsOpen = -1.0; heapsInside = -1e9; heapsNear = -1e9;
    float c = layerAt(q, p, 1.0, h, wa.x, wa.y, wa.z, wa.w, wb.x, wm, fine, lod);
    if (c > 0.0) c *= holeAt(p, _CloudHoleA) * holeAt(p, _CloudHoleB) * holeAt(p, _CloudHoleC);
    // (the middle clouds and each storm only where there are any: each in a loop that runs once or not at all, see layerAt)
    for (int mid = wb.a > 0.004 ? 0 : 1; mid < 1; mid++)
    {
        float open = heapsOpen, inside = heapsInside, middle = layerAt(q.zxy + vec3(0.37, 0.71, 0.13), p, 2.0, h, wb.a, wc.x, wc.y, wc.z, wc.w, wm, fine, lod);
        if (middle > c) c = middle; else { heapsOpen = open; heapsInside = inside; }
    }
    // (each storm; whichever is thickest here says how much sky is seen and which way its surface faces)
    int count = storms();
    for (int k = 0; k < count; k++)
    {
        float open = heapsOpen, inside = heapsInside, storm = cellAt(p, h, q, stormA(k), stormB(k), wm, fine, lod);
        if (storm > c) c = storm; else { heapsOpen = open; heapsInside = inside; }
    }
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

float dither(vec2 p) { return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715)))); }

// A storm's rain along this line of sight, walked apart from the clouds (it hangs below them, where the clouds' own walk does
// not go): where the line passes through the curtain (an upright cylinder round the storm's middle, from the ground to its
// base), eight steps, lit by the sky and the ground and a little of the sun that comes through the storm. Added in front of
// the clouds.
void rainWalk(vec4 a, vec4 b, vec3 dir, vec3 dirFixed, vec3 sunTop, vec3 sunDir, float stop, float jitter, inout vec3 light, inout float through, inout float sumT, inout float sumW)
{
    if (b.w < 0.1 || b.z < 0.15 || a.w <= 0.0) return;
    float R = _CloudCam.w;
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    vec3 o = _CloudCam.xyz - mid * dot(_CloudCam.xyz, mid);
    vec3 d = dir - mid * dot(dir, mid);
    float r = a.w * 0.75;
    float A = dot(d, d), B = dot(o, d), C = dot(o, o) - r * r;
    float disc = B * B - A * C;
    if (A < 1e-8 || disc <= 0.0) return;
    disc = sqrt(disc);
    float ta = max((-B - disc) / A, _CloudStage.z), tb = min((-B + disc) / A, stop);
    if (_CloudStage.w > 0.0) tb = min(tb, _CloudStage.w);
    if (tb <= ta) return;
    float dt = (tb - ta) / 8.0;
    for (int k = 0; k < 8; k++)
    {
        float t = ta + (float(k) + jitter) * dt;
        vec3 p = _CloudCam.xyz + dir * t;
        float h = length(p) - R;
        vec3 q = _CloudNoise.xyz + dirFixed * (t / _CloudNoise.w);
        float c = rainAt(p, h, q, a, b);
        if (c <= 0.0) continue;
        float sigma = c * _CloudLook.x;
        float sunUp = dot(p / max(length(p), 1.0), sunDir);
        vec3 lit = _CloudSky.rgb * 0.8 * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) + _CloudGround.rgb + sunTop * sunThroughAir(h, sunUp) * 0.08 + vec3(_CloudSunTint.w);
        float pass = exp(-sigma * dt);
        light += through * lit * (1.0 - pass);
        sumT += t * through * (1.0 - pass);
        sumW += through * (1.0 - pass);
        through *= pass;
    }
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

    float R = _CloudCam.w;

    // ---------------------------------------------------------------- 4: the shadow, from points on the ground towards the sun
    if (stage > 3.5)
    {
        vec3 g = _CloudShadowAt.xyz + (_CloudShadowX.xyz * (uv.x - 0.5) + _CloudShadowY.xyz * (uv.y - 0.5)) * _CloudShadowAt.w;
        g = normalize(g) * (R + _CloudShadowX.w);
        vec3 s = _CloudSunFixed.xyz;
        // (g is in the planet's own axes; the weather map is read in the scene's, so it is turned back for that)
        vec3 gScene = g.x * _CloudFixedX.xyz + g.y * _CloudFixedY.xyz + g.z * _CloudFixedZ.xyz;
        vec3 sScene = s.x * _CloudFixedX.xyz + s.y * _CloudFixedY.xyz + s.z * _CloudFixedZ.xyz;
        vec2 inner = sphere(gScene, sScene, R + _CloudShell.x);
        vec2 outer = sphere(gScene, sScene, R + _CloudShell.y);
        float t0 = max(inner.y > 0.0 && dot(gScene, gScene) < (R + _CloudShell.x) * (R + _CloudShell.x) ? inner.y : 0.0, 0.0);
        float t1 = outer.y;
        float depth = 0.0, lowest = 99999.0, highest = 0.0;
        if (t1 > t0 && dot(normalize(gScene), sScene) > -0.2)
        {
            t1 = min(t1, t0 + 30000.0);
            float dt = (t1 - t0) / 24.0;
            vec4 shadowWm = vec4(1.0, 0.0, 0.0, 0.0);
            // (the noise is moved as the camera's place in it is: the shadow's square is fixed to the planet)
            vec3 camFixed = vec3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
            for (int n = 0; n < 24; n++)
            {
                float t = t0 + (float(n) + 0.5) * dt;
                vec3 p = gScene + sScene * t;
                float h = length(p) - R;
                vec4 wa, wb, wc;
                weatherAt(p, wa, wb, wc);
                vec3 q = _CloudNoise.xyz + (g + s * t - camFixed) / _CloudNoise.w;
                if (n == 0) shadowWm = broadAt(q);
                // (the noise read a little coarsely, but not so coarsely that the far heaps' third reading comes in: the shadow is
                // of the heaps near the camera, which are drawn without it)
                float c = cloudAt(q, p, h, wa, wb, wc, vec4(shadowWm.x * streetsAt(q, shadowWm), shadowWm.yzw), false, 0.65);
                if (c > 0.0)
                {
                    // (an eighth of the cloud's own thickness: the walk here is coarse and without the small noise, and a shadow taken at
                    // full strength is all or nothing, with the edge of each dot of the square in it)
                    depth += c * _CloudLook.x * dt * 0.12;
                    lowest = min(lowest, h);
                    highest = max(highest, h);
                }
            }
        }
        SV_Target0 = vec4(depth, lowest / 1000.0, highest / 1000.0, 1.0);
        SV_Target1 = vec4(0.0);
        return;
    }

    // The line of sight through this pixel.
    vec3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));

    // ---------------------------------------------------------------- 3: taken together over frames
    if (stage > 2.5)
    {
        vec4 now = textureLod(_CloudNow, uv, 0.0);
        float nowD = textureLod(_CloudNowD, uv, 0.0).r * 1000.0;
        // (the least and most of this frame round this pixel: what is kept from before is held inside them, so that nothing
        // left behind by a cloud that moved can linger)
        vec2 px = vec2(1.0) / vec2(textureSize(_CloudNow, 0));
        vec4 lo = now, hi = now;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                if (i == 0 && j == 0) continue;
                vec4 n = textureLod(_CloudNow, uv + vec2(float(i), float(j)) * px, 0.0);
                lo = min(lo, n); hi = max(hi, n);
            }
        float take = _CloudPrevRight.w;
        vec4 outC = now;
        float outD = nowD;
        if (_CloudPrevAt.w > 0.5)
        {
            // Where this pixel's cloud was in last frame's picture.
            float far = nowD > 0.0 ? nowD : 30000.0;
            vec3 d = vec3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
            vec3 v = d * far + _CloudPrevAt.xyz;
            float ahead = dot(v, _CloudPrevAhead.xyz);
            if (ahead > 1.0)
            {
                vec2 was = vec2(dot(v, _CloudPrevRight.xyz), dot(v, _CloudPrevUp.xyz)) / ahead * 0.5 + 0.5;
                if (was.x > 0.0 && was.x < 1.0 && was.y > 0.0 && was.y < 1.0)
                {
                    vec4 before = clamp(textureLod(_CloudHist, was, 0.0), lo, hi);
                    float beforeD = textureLod(_CloudHistD, was, 0.0).r * 1000.0;
                    outC = mix(before, now, take);
                    outD = nowD > 0.0 && beforeD > 0.0 ? mix(beforeD, nowD, take) : (nowD > 0.0 ? nowD : beforeD);
                }
            }
        }
        if (!(outC.a >= 0.0) || (floatBitsToInt(outC.r + outC.g + outC.b + outC.a) & 0x7F800000) == 0x7F800000) outC = vec4(0.0, 0.0, 0.0, 1.0);
        SV_Target0 = outC;
        SV_Target1 = vec4(outD / 1000.0, 0.0, 0.0, 1.0);
        return;
    }

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
        if (outer.x < 0.0) { SV_Target0 = vec4(0.0, 0.0, 0.0, 1.0); SV_Target1 = vec4(0.0); return; }
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
        // below the clouds: from the floor of the shell to its roof (nothing if the ground is in the way first)
        if (ground.x > 0.0) { SV_Target0 = vec4(0.0, 0.0, 0.0, 1.0); SV_Target1 = vec4(0.0); return; }
        t0 = inner.y;
        t1 = outer.y;
    }
    if (ground.x > 0.0) t1 = min(t1, ground.x);
    // What the camera has drawn in the way (its depth picture: the farthest of the four pixels this one stands for).
    float stop = 1e9;
    if (_CloudRight.w > 0.5)
    {
        ivec2 full = textureSize(_CameraDepthTexture, 0);
        ivec2 at = clamp(ivec2((gl_FragCoord.xy - 0.5) * vec2(full) / max(vec2(_CloudHigh.w, _CloudHigh.w * float(full.y) / float(full.x)), vec2(1.0))), ivec2(0), full - 2);
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
    if (t1 <= t0) { SV_Target0 = vec4(0.0, 0.0, 0.0, 1.0); SV_Target1 = vec4(0.0); return; }

    // Steps: short near the camera, longer far away; never more than so many.
    float length0 = t1 - t0;
    int most = int(_CloudLook.w);
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
    // (each pixel's own place in the step, moved on by the golden ratio each frame: over a few frames the places fill the step evenly,
    // and neighbouring pixels differ, so nothing lines up into a pattern)
    // (the pixel's own offset from a hash: a pattern such as interleaved gradient noise changes by a set amount from row to row,
    // and what is left of it after a few frames shows on a crisp edge as stripes)
    uvec2 pix = uvec2(gl_FragCoord.xy);
    float own = float(mixBits(pix.x + mixBits(pix.y + 7919u)) & 65535u) / 65536.0;
    float jitter = fract(own + 0.61803399 * _CloudStage.y);
    float t = t0 + stepLen * jitter;
    stepLen = clamp(t * each, least, longest);

    vec3 sunDir = _CloudSun.xyz;
    float cosView = dot(dir, sunDir);
    // light that goes on mostly straight ahead, some of it back (kept apart: what goes straight on is dimmed less by the cloud
    // it passes, see the silver lining below); and again for light already scattered (spread more evenly)
    float phOn = 0.73 * phase(cosView, 0.86), phBack = 0.27 * phase(cosView, -0.22);
    float ph2 = mix(phase(cosView, 0.43), phase(cosView, -0.15), 0.3);
    vec3 camFixed = vec3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
    vec3 dirFixed = vec3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
    vec3 sunFixed = _CloudSunFixed.xyz;
    vec3 sunTop = _CloudSunTint.rgb * _CloudSun.w;

    vec3 light = vec3(0.0);
    float through = 1.0;
    float sumT = 0.0, sumW = 0.0;
    // (the storms' rain first: it hangs below the clouds, nearer than they are along a line of sight from below)
    int rains = storms();
    for (int k = 0; k < rains; k++) rainWalk(stormA(k), stormB(k), dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW);
    vec4 wa = vec4(0.0), wb = vec4(0.0), wc = vec4(0.0), wmFrom = vec4(1.0, 0.0, 0.0, 0.0), wmTo = vec4(1.0, 0.0, 0.0, 0.0);
    float weatherAtT = -1e9, wmToT = -1e9;
    int empty = 0;
    bool coarse = false;
    bool strode = false;     // whether the last move was a long stride through clear air
    int lit = 0;
    bool clear = true;       // whether the last step was in clear air
    float clearT = t0;       // the last place along the line known to be clear air
    float knownT = -1.0, knownC = 0.0;   // a place already looked at (the step after a cloud's edge), and how much cloud is there
    float lastToward = -1.0;
    float lastFaces = 2.0;   // which way a heap's surface faces at the last lit step, as the cosine of its angle to the sun (2: not known)
    float sigmaScale = _CloudLook.x;
    float edgeTo = alt < _CloudShell.y + 2000.0 ? 40000.0 : 15000.0;   // how far off a cloud's edge is still searched for (see below)
    for (int n = 0; n < 256; n++)
    {
        if (n >= most || t > t1 || through < 0.01) break;
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
            weatherAt(p, wa, wb, wc);
            wmFrom = wmTo;
            for (int first = wmToT > -1e8 && t <= wmToT + 600.0 ? 1 : 0; first < 1; first++) wmFrom = broadAt(q);
            weatherAtT = t; wmToT = t + 1200.0;
            wmTo = broadAt(_CloudNoise.xyz + dirFixed * (wmToT / _CloudNoise.w));
        }
        vec4 wm0 = mix(wmFrom, wmTo, clamp((t - weatherAtT) / 1200.0, 0.0, 1.0));
        vec4 wm = vec4(wm0.x * streetsAt(q, wm0), wm0.yzw);
        // (which size of the noise to read: the large noise's full size out to 25 km, the small one's to about 5 km; coarser beyond,
        // where heaps a few pixels across would otherwise sparkle)
        float lod = min(log2(max(t, 1.0) / 25000.0), 4.0);
        bool fine = t < 40000.0;
        if (coarse)
        {
            // Through clear air three steps at a time, with the noise read coarsely; at the first sign of cloud, two steps back
            // and on at the usual length (so that where a cloud begins is found to within a step, as the steps' start, which
            // moves from frame to frame, has it: else its edge shows in rings).
            // (Back only after a stride. Among heaps made beforehand the seeker says "there may be cloud" for some way round
            // every heap, truly; the walk then went two steps back, four on finding nothing, asked again, went two back: five
            // readings for every two steps of clear air beside a heap, two and a half times what walking it plainly costs.)
            if (cloudAt(q, p, h, wa, wb, wc, wm, false, lod + 1.0 + 100.0) > 0.0)
            {
                coarse = false; empty = 0;
                if (strode) { strode = false; t -= stepLen * 2.0; continue; }
            }
            else
            {
                clearT = t;
                t += stepLen * 3.0;
                stepLen = clamp(t * each, least, longest);
                strode = true;
                continue;
            }
        }
        float c = abs(t - knownT) < 0.01 ? knownC : cloudAt(q, p, h, wa, wb, wc, wm, fine, lod);
        float open = heapsOpen, insideHere = heapsInside, near = heapsNear;
        float dt = stepLen;
        // At the edge of a cloud (the first step into it after clear air): where between the last clear step and here it
        // begins, found to a quarter of the way by halving twice, and a step taken from there to here (lit there) before
        // this one. The edge is what is seen of a dense heap, which hides what is behind it within a few metres, and a step of
        // tens of metres would put it anywhere within the step: a blurred heap, its sunlit side lit as if from deep inside.
        // (Not walked in short steps instead: they spent the steps a line may take, and lines that ran out before the far side
        // of the heaps showed as stripes.)
        // (From in or under the clouds out to 40 km; until 0.2.2 to 15, as it still is from high above them, where a step is
        // less than a pixel. And not for a cloud of which eight tenths is already hidden by the cloud in front.)
        if (c > 0.001 && clear && t > clearT + 1.0 && t < edgeTo && through > 0.2)
        {
            float lo = clearT, hi = t, chi = c;
            for (int b = 0; b < 2; b++)
            {
                float mid = 0.5 * (lo + hi);
                vec3 mp = _CloudCam.xyz + dir * mid;
                float mc = cloudAt(_CloudNoise.xyz + dirFixed * (mid / _CloudNoise.w), mp, length(mp) - R, wa, wb, wc, wm, fine, lod);
                if (mc > 0.001) { hi = mid; chi = mc; } else lo = mid;
            }
            // (Lit from no more than ten metres past the last place known to be clear. The edge is somewhere in the quarter of
            // a step between that place and the first one known to be cloud: ten metres with a step of forty, near the camera,
            // but forty at 15 km and a hundred at 40, and lit from the far end of that, as until 0.2.2, a heap's sunlit side
            // was lit from where no sunlight is: every heap past 10 km a flat grey, a smear beside the nearer ones.)
            float litAt = min(hi, lo + 10.0);
            if (hi < t - 0.5)
            {
                knownT = t; knownC = c;
                // (the cloud over that step taken as the mean of its two ends: it thickens from nothing at the edge)
                c = 0.5 * (chi + c);
                dt = t - hi;
                t = hi;
            }
            p = _CloudCam.xyz + dir * litAt;
            h = length(p) - R;
            q = _CloudNoise.xyz + dirFixed * (litAt / _CloudNoise.w);
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
            float toward = lastToward;
            // (walked again on every other lit step only, and on the first after clear air: the next one along is lit much the
            // same; and with four steps instead of six beyond 8 km, where a cloud is small in the picture)
            bool again = (lit & 1) == 0 || lastToward < 0.0;
            lit++;
            if (again && dot(sunHere, sunHere) > 1e-8)
            {
                toward = 0.0;
                float lt = 0.0, ls = 28.0;
                int towardSteps = t > 8000.0 ? 4 : 6;
                // (each step read at its own place in its stretch, a different one for every pixel and frame: read always at the
                // middle, six readings of a heap with an edge to it can only come to so many sums, and its shade lay in flat
                // bands with steps between them, like a drawing shaded with three pencils)
                float place = fract(jitter * 5.371 + 0.618 * float(lit));
                for (int k = 0; k < 6; k++)
                {
                    if (k >= towardSteps) break;
                    // (the first at the middle of its stretch always: which way the surface faces is taken from it, and taken over a
                    // different distance each frame it came out different each frame: heaps like sandpaper)
                    float at = lt + ls * (k == 0 ? 0.5 : place);
                    vec3 lp = p + sunDir * at;
                    float lh = length(lp) - R;
                    vec3 lq = q + sunFixed * (at / _CloudNoise.w);
                    toward += cloudAt(lq, lp, lh, wa, wb, wc, wm, k < 2 && fine, k == 0 ? lod : lod + 0.5) * ls;
                    // (Which way the heap's surface faces here, from its first reading towards the sun: the number of metres
                    // inside the surface falls by a metre for every metre straight out through it, and by less aslant. The
                    // cosine of the angle between the way out and the sun, near enough.)
                    if (k == 0) lastFaces = insideHere > -1e8 && heapsInside > -1e8 ? clamp((insideHere - heapsInside) / max(at, 4.0), -1.0, 1.0) : 2.0;
                    lt += ls;
                    // (deep in cloud already: what lies further on would dim what little diffuses through by a part of itself;
                    // taken as half as much again, and the walk left off)
                    if (toward * sigmaScale > 12.0) { toward *= 1.5; break; }
                    ls *= 1.9;
                    place = fract(place + 0.618);
                }
                // (and one long look further on, for a tower or another cloud kilometres away between here and the sun)
                {
                    float far = lt + 2000.0;
                    vec3 lp = p + sunDir * far;
                    toward += cloudAt(q + sunFixed * (far / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 1.0) * 3000.0;
                }
                toward *= sigmaScale;
                lastToward = toward;
            }
            else if (again) { toward = 0.0; lastToward = 0.0; }
            // Light scattered once, twice, three times over (each time it carries further and spreads more evenly), and the
            // darkening of a cloud's sunlit edge where little light has yet been scattered towards the eye (the "powder").
            float powder = 1.0 - 0.55 * exp(-c * 6.0) * clamp(0.75 - 0.5 * cosView, 0.0, 1.0);
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
            float skin = lastFaces > 1.5 ? 0.0 : exp(-max(insideHere, 0.0) / 30.0);
            float faces = mix(1.0, 0.3 + 1.0 * clamp(lastFaces, 0.0, 1.0), skin);
            vec3 sunlit = sunHere * ((exp(-toward * 0.1) * phOn + exp(-toward) * phBack + 0.35 * exp(-toward * 0.25) * ph2) * (4.0 * PI * 0.45) + (0.30 * exp(-toward * 0.35) + 0.25 * exp(-toward * 0.12) + 0.35 / (1.0 + 0.11 * toward)) * faces) * powder;
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
            if (empty > 3) coarse = true;
            // (Clear air, and no cloud within so many metres, as the field and the storms keep their shapes: on by that
            // much at once, but no more than a dozen steps' worth or 600 m, the cover and the storms being read afresh as
            // the walk goes. Without this a line of sight that began inside the layer of air the clouds are in, among heaps
            // or under a storm's anvil, spent its eighty-eight steps of forty metres on clear air and stopped three and a
            // half kilometres out: sky, where the anvil should have been.)
            // (And each stride by its own share of that, a half to the whole, a different one for every pixel and frame: striding the
            // whole way each time, every line of sight came up to a heap at the same places whatever its start, the walk's steps
            // no longer fell at different places from frame to frame, and the heaps were shaded in level bands.)
            if (near < 0.0 && near > -1e8) dt = max(dt, min(-near * (0.5 + 0.5 * fract(jitter * 7.13 + 0.618 * float(n))), min(12.0 * stepLen, 600.0)));
        }
        t += dt;
        stepLen = clamp(t * each, least, longest);
    }

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
            float alpha = 1.0 - exp(-wisp * _CloudGround.w * 0.004 / max(slant, 0.08));
            if (alpha > 0.002)
            {
                vec3 up = normalize(p);
                vec3 sunHere = sunTop * sunThroughAir(hc, dot(up, sunDir));
                vec3 lit = sunHere * (mix(phase(cosView, 0.65), phase(cosView, -0.2), 0.25) * 4.0 * PI * 0.8 + 0.25) + _CloudSky.rgb * 0.6;
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
            float dens = exp(-h / max(_CloudHigh.z, 100.0));
            vec3 ext = (_CloudAir.rgb + vec3(_CloudAir.w)) * dens;
            vec3 sunHere = sunTop * sunThroughAir(h, dot(p / max(length(p), 1.0), sunDir));
            gained += exp(-tau - ext * seg * 0.5) * dens * (_CloudAir.rgb * pr + vec3(_CloudAir.w) * pm) * sunHere * seg;
            tau += ext * seg;
        }
        vec3 airThrough = exp(-tau * _CloudHaze.w);
        // (and the air lit by the whole sky, not by the sun alone: light scattered again and again, which is why distance goes
        // pale blue-white and not the yellow that the sun's light alone, robbed of its blue on the way, would give)
        float camSun = clamp(dot(normalize(_CloudCam.xyz), sunDir) * 3.0 + 0.3, 0.0, 1.0);
        vec3 skyHaze = _CloudSky.rgb * 2.2 * camSun;
        light = light * airThrough + (gained * _CloudHaze.z + skyHaze * (1.0 - airThrough)) * _CloudHaze.w * (1.0 - through);
    }
    vec4 result = vec4(light, through);
    if ((floatBitsToInt(result.r + result.g + result.b + result.a) & 0x7F800000) == 0x7F800000) result = vec4(0.0, 0.0, 0.0, 1.0);
    SV_Target0 = result;
    // (how far off, in kilometres: half floats end at 65504)
    SV_Target1 = vec4(meanT / 1000.0, 0.0, 0.0, 1.0);
}
#endif
