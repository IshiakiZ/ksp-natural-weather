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
    float heap = clamp(hf / 0.025, 0.0, 1.0) * clamp((1.0 - hf) / 0.7, 0.0, 1.0);
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
    float billow = clamp((s.g * 0.5 + s.b * 0.3 + s.a * 0.2 - 0.3) * 1.7, 0.0, 1.0);
    return clamp(heap * (0.5 + 0.62 * billow) + 0.05, 0.0, 1.0);
}

// How much cloud of one layer there is at a point: q is the point in the noise (lengths of the large noise), h its height
// above the radius; then the layer: how much of the sky it covers, what kind, its base and top, how wet. "fine" says
// whether the small noise is used (it is left out in the light's walk and far away); lod which size of the large noise to
// read (0 its full size, below 0 nearer than that; the small noise is read 2.2 sizes finer).
float layerAt(vec3 q, float h, float cover, float kind, float base, float top, float wet, vec4 wm, bool fine, float lod)
{
    top = max(top, base + 50.0);
    float hf = (h - base) / (top - base);
    if (hf <= 0.0 || hf >= 1.0) return 0.0;
    cover = clamp(cover * _CloudLook.z, 0.0, 1.0);
    if (cover <= 0.001) return 0.0;
    // (clusters, gaps and streets; a sheet over the whole sky is left whole: they are for skies of separate heaps)
    // (as a power, not a factor: a cluster thickens the cover towards all of the sky but never to all of it, where a factor
    // filled the layer from base to top in slabs with flat tops and straight sides)
    cover = mix(pow(cover, 1.0 / max(wm.x, 0.05)), cover, cover * cover);
    // (the noise bent, and the heaps leaning with the wind, more the higher they are)
    vec3 at = q + wm.yzw * 0.6 + _CloudDrift.xyz * hf * 0.08;
    // (and a second reading of the large noise, turned and at a scale that does not divide its repeat (20.7 km against 9),
    // says whether the heaps of the first grow here: the two together repeat only every 207 km; coarsely read, it changes
    // over kilometres, as a heap's surroundings do)
    float other = textureLod(_CloudShape, at.yzx / 2.3 + vec3(0.13, 0.47, 0.29), (max(lod, 0.0) + 1.0) * _CloudHaze.x).r;
    cover = pow(cover, 1.0 / mix(0.55, 1.6, smoothstep(0.62, 0.86, other)));
    vec4 s = textureLod(_CloudShape, at, max(lod, 0.0) * _CloudHaze.x);
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
    float spread = clamp(kind * 2.0 - 1.0, 0.0, 1.0) * smoothstep(0.65, 0.9, hf);
    // (and the anvil is an even canopy, the noise counting for less in it and eating less of its edge: ragged with holes, it
    // read as scraps of cloud above the tower, not its anvil)
    heap = mix(heap, mix(0.65, 1.0, heap), spread);
    // Only the strongest of the noise is cloud where the cover is small: separate heaps, each dense inside (the cover is
    // not a thinning of the cloud, which would make thin smoke of a fair-weather sky).
    float c = clamp(remap(heap * shape, 1.0 - min(1.0, cover * (1.0 + 1.5 * spread)), 1.0, 0.0, 1.0), 0.0, 1.0);
    if (c <= 0.0) return 0.0;
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
    c = min(c * mix(1.3, 3.0, smoothstep(0.15, 0.45, kind)), 1.0);
    // (a sheet is thinner stuff than the core of a heap: a grey overcast lets a share of the light through, where a sheet as
    // dense as a heap's core is dark as a storm's base)
    c *= mix(0.3, 1.0, smoothstep(0.1, 0.45, kind));
    // (the thinnest wisps let go: else a clear sky is speckled with crumbs of cloud)
    c *= smoothstep(0.0, 0.1, c);
    // (rain clouds are thicker and darker underneath)
    return c * (1.0 + wet * (1.0 - hf));
}

// One storm or shower: a tower round its middle, narrowing upward and spreading into an anvil at the top, its edges
// broken by the same noise as the heaps; and under it, its rain.
float cellAt(vec3 p, float h, vec3 q, vec4 a, vec4 b, vec4 wm, bool fine, float lod)
{
    if (b.z < 0.15 || a.w <= 0.0) return 0.0;
    float frac = (h - b.x) / max(b.y - b.x, 100.0);
    if (frac <= 0.0 || frac >= 1.0) return 0.0;
    vec3 mid = a.xyz / max(length(a.xyz), 1.0);
    // (the anvil is carried downwind of the tower, the more the higher: the wind at the top of the air is the strongest)
    vec3 wind = _CloudDrift.x * _CloudFixedX.xyz + _CloudDrift.y * _CloudFixedY.xyz + _CloudDrift.z * _CloudFixedZ.xyz;
    vec3 from = p - wind * (a.w * 0.9 * smoothstep(0.6, 1.0, frac));
    float d = length(from - mid * dot(from, mid));
    float r = a.w * (0.68 + 0.12 * (1.0 - frac)) * (1.0 + 1.4 * smoothstep(0.7, 0.95, frac));
    if (d > r * 1.3) return 0.0;
    float inside = smoothstep(r * 1.3, r * 0.45, d) * smoothstep(0.15, 0.5, b.z);
    return layerAt(q + vec3(0.31, 0.17, 0.53), h, inside, 1.0, b.x, b.y, b.w, vec4(1.0, wm.yzw), fine, lod);
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
float cloudAt(vec3 q, vec3 p, float h, vec4 wa, vec4 wb, vec4 wc, vec4 wm, bool fine, float lod)
{
    float c = layerAt(q, h, wa.x, wa.y, wa.z, wa.w, wb.x, wm, fine, lod);
    if (wb.a > 0.004) c = max(c, layerAt(q.zxy + vec3(0.37, 0.71, 0.13), h, wb.a, wc.x, wc.y, wc.z, wc.w, wm, fine, lod));
    c = max(c, cellAt(p, h, q, _CloudCellA0, _CloudCellB0, wm, fine, lod));
    c = max(c, cellAt(p, h, q, _CloudCellA1, _CloudCellB1, wm, fine, lod));
    c = max(c, cellAt(p, h, q, _CloudCellA2, _CloudCellB2, wm, fine, lod));
    c = max(c, cellAt(p, h, q, _CloudCellA3, _CloudCellB3, wm, fine, lod));
    c = max(c, cellAt(p, h, q, _CloudCellA4, _CloudCellB4, wm, fine, lod));
    c = max(c, cellAt(p, h, q, _CloudCellA5, _CloudCellB5, wm, fine, lod));
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
    float least = _CloudHaze.y, longest = max(least, length0 / 24.0);
    float stepLen = clamp(t0 * 0.01, least, longest);
    // (each pixel's own place in the step, moved on by the golden ratio each frame: over a few frames the places fill the step evenly,
    // and neighbouring pixels differ, so nothing lines up into a pattern)
    // (the pixel's own offset from a hash: a pattern such as interleaved gradient noise changes by a set amount from row to row,
    // and what is left of it after a few frames shows on a crisp edge as stripes)
    uvec2 pix = uvec2(gl_FragCoord.xy);
    float own = float(mixBits(pix.x + mixBits(pix.y + 7919u)) & 65535u) / 65536.0;
    float jitter = fract(own + 0.61803399 * _CloudStage.y);
    float t = t0 + stepLen * jitter;
    stepLen = clamp(t * 0.01, least, longest);

    vec3 sunDir = _CloudSun.xyz;
    float cosView = dot(dir, sunDir);
    // light that goes on mostly straight ahead, some of it back; and again for light already scattered (spread more evenly)
    float ph = mix(phase(cosView, 0.86), phase(cosView, -0.22), 0.27);
    float ph2 = mix(phase(cosView, 0.43), phase(cosView, -0.15), 0.3);
    vec3 camFixed = vec3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
    vec3 dirFixed = vec3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
    vec3 sunFixed = _CloudSunFixed.xyz;
    vec3 sunTop = _CloudSunTint.rgb * _CloudSun.w;

    vec3 light = vec3(0.0);
    float through = 1.0;
    float sumT = 0.0, sumW = 0.0;
    // (the storms' rain first: it hangs below the clouds, nearer than they are along a line of sight from below)
    rainWalk(_CloudCellA0, _CloudCellB0, dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW);
    rainWalk(_CloudCellA1, _CloudCellB1, dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW);
    rainWalk(_CloudCellA2, _CloudCellB2, dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW);
    rainWalk(_CloudCellA3, _CloudCellB3, dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW);
    rainWalk(_CloudCellA4, _CloudCellB4, dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW);
    rainWalk(_CloudCellA5, _CloudCellB5, dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW);
    vec4 wa = vec4(0.0), wb = vec4(0.0), wc = vec4(0.0), wm0 = vec4(1.0, 0.0, 0.0, 0.0);
    float weatherAtT = -1e9;
    int empty = 0;
    bool coarse = false;
    int lit = 0;
    bool clear = true;       // whether the last step was in clear air
    float clearT = t0;       // the last place along the line known to be clear air
    float knownT = -1.0, knownC = 0.0;   // a place already looked at (the step after a cloud's edge), and how much cloud is there
    float lastToward = -1.0;
    float sigmaScale = _CloudLook.x;
    for (int n = 0; n < 256; n++)
    {
        if (n >= most || t > t1 || through < 0.01) break;
        vec3 p = _CloudCam.xyz + dir * t;
        float h = length(p) - R;
        // (the weather is read again every kilometre or so along the line: it changes over tens of them)
        vec3 q = _CloudNoise.xyz + dirFixed * (t / _CloudNoise.w);
        if (abs(t - weatherAtT) > 1200.0) { weatherAt(p, wa, wb, wc); wm0 = broadAt(q); weatherAtT = t; }
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
            if (cloudAt(q, p, h, wa, wb, wc, wm, false, lod + 1.0) > 0.0) { coarse = false; empty = 0; t -= stepLen * 2.0; continue; }
            clearT = t;
            t += stepLen * 3.0;
            stepLen = clamp(t * 0.01, least, longest);
            continue;
        }
        float c = abs(t - knownT) < 0.01 ? knownC : cloudAt(q, p, h, wa, wb, wc, wm, fine, lod);
        float dt = stepLen;
        // At the edge of a cloud (the first step into it after clear air): where between the last clear step and here it
        // begins, found to a quarter of the way by halving twice, and a step taken from there to here (lit there) before
        // this one. The edge is what is seen of a dense heap, which hides what is behind it within a few metres, and a step of
        // tens of metres would put it anywhere within the step: a blurred heap, its sunlit side lit as if from deep inside.
        // (Not walked in short steps instead: they spent the steps a line may take, and lines that ran out before the far side
        // of the heaps showed as stripes. Within 15 km only: beyond, a step is a pixel or two of a small heap.)
        if (c > 0.001 && clear && t > clearT + 1.0 && t < 15000.0)
        {
            float lo = clearT, hi = t, chi = c;
            for (int b = 0; b < 2; b++)
            {
                float mid = 0.5 * (lo + hi);
                vec3 mp = _CloudCam.xyz + dir * mid;
                float mc = cloudAt(_CloudNoise.xyz + dirFixed * (mid / _CloudNoise.w), mp, length(mp) - R, wa, wb, wc, wm, fine, lod);
                if (mc > 0.001) { hi = mid; chi = mc; } else lo = mid;
            }
            if (hi < t - 0.5)
            {
                knownT = t; knownC = c;
                // (the cloud over that step taken as the mean of its two ends: it thickens from nothing at the edge)
                c = 0.5 * (chi + c);
                dt = t - hi;
                t = hi;
                p = _CloudCam.xyz + dir * t;
                h = length(p) - R;
                q = _CloudNoise.xyz + dirFixed * (t / _CloudNoise.w);
            }
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
                for (int k = 0; k < 6; k++)
                {
                    if (k >= towardSteps) break;
                    lt += ls * 0.5;
                    vec3 lp = p + sunDir * lt;
                    float lh = length(lp) - R;
                    vec3 lq = q + sunFixed * (lt / _CloudNoise.w);
                    toward += cloudAt(lq, lp, lh, wa, wb, wc, wm, k < 2 && fine, lod + 0.5) * ls;
                    lt += ls * 0.5;
                    ls *= 1.9;
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
            float powder = 1.0 - 0.3 * exp(-sigma * 140.0) * clamp(0.6 - 0.6 * cosView, 0.0, 1.0);
            // (scattered once, mostly straight on: the silver lining of a heap with the sun behind it; a few times, still forward
            // and carried further (Wrenninge's octaves); and very many times over, spread evenly: what diffuses through a thick
            // cloud falls off as one over its depth, not exponentially (the two-stream rule, 1 / (1 + 0.75 (1 - g) depth) with g
            // 0.85: about a tenth of the sun through a heap a kilometre thick), which is what lights the shaded side of a heap
            // grey and its base a darker grey, and keeps a heap against the sun dark inside its bright rim)
            vec3 sunlit = sunHere * ((exp(-toward) * ph + 0.35 * exp(-toward * 0.25) * ph2) * (4.0 * PI) + 0.9 / (1.0 + 0.11 * toward)) * powder;
            // ---- the sky from above (less of it deep in the cloud and low down), the ground from below
            // (the sky's blue on what the sun does not reach: most on the tops, less on the flat base, which sees the sky only
            // at its horizon; the ground's light on the base)
            vec3 sky = _CloudSky.rgb * mix(0.42, 1.0, hf) * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) * (0.35 + 0.65 * dot(sunHere / max(sunTop, vec3(1e-4)), vec3(0.2126, 0.7152, 0.0722)));
            vec3 below = _CloudGround.rgb * (1.0 - hf) * 0.8;
            vec3 here = sunlit + sky + below + vec3(_CloudSunTint.w);
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
        }
        t += dt;
        stepLen = clamp(t * 0.01, least, longest);
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
