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
// The clouds taken together over frames (Natural Weather's Clouds.cs): what clouds.glsl's walk drew small this frame, laid into
// what was kept from the frames before, where each cloud was in the last one worked out from how far off it is. The third go of
// clouds.glsl until 2026-10-26, moved here as it was: the walk's program is the largest the card is given, and on Direct3D it is
// compiled without the compiler's optimising (tools/unityshaders/windows.py, HEAVY), which this go paid for with it; and the
// walk wants the samplers this go held.
uniform vec4 _CloudRight;    // xyz: the camera's right, times the tangent of half its width of view
uniform vec4 _CloudUp;       // xyz: the camera's up, times the tangent of half its height of view
uniform vec4 _CloudAhead;    // xyz: the way the camera looks
uniform vec4 _CloudFixedX;   // the planet's own axes, in the scene's: x towards latitude 0 longitude 0, y towards longitude 90, z the north pole
uniform vec4 _CloudFixedY;
uniform vec4 _CloudFixedZ;
uniform vec4 _CloudPrevAt;   // xyz: where the camera is from where it was last frame (m, the planet's axes); w: 1 if last frame's picture can be used
uniform vec4 _CloudPrevRight; // the near camera's: last frame's camera, in the planet's axes: right over the tangent of half the width of view; the far camera's: what last frame's kept picture was held to, as _CloudHeldRight; w (near): how much of this frame is taken (0 to 1)
uniform vec4 _CloudPrevUp;   // up over the tangent of half the height of view
uniform vec4 _CloudPrevAhead; // the way it looked
uniform vec4 _CloudJitter;   // xy: how far this frame's lines of sight were moved off the middles of the walk's pixels (in widths and heights of the picture); z: 1 if what is kept from frame to frame has more dots than the walk's picture
uniform vec4 _CloudKeep;     // (the far camera's: see where its pixels are kept) x: how many of its own lines a kept pixel holds at most, read at the middle of last frame's pixel; y: read half a pixel or more between them; z: how many lines the walk's lines round it, spread, count for where nothing kept can be used; w: how far past what this frame's lines round a pixel saw what is kept may stand, in shares of their spread. Nought: the near camera's, taken together as until 2026-10-26
uniform vec4 _CloudForm;     // z: (development builds) the trial (Clouds.Try2): 23, what is kept read by Catmull and Rom's curve; 26, how many lines each kept pixel holds, shown as grey
uniform vec4 _CloudHeld;       // (the far camera's) xy: the screen's kept size (pixels); z: kept pixels a dot of the walk is each way; w: 0 to keep, 1 to keep on a frame what is kept is held anew, 2 to lay it on the screen's grid
uniform vec4 _CloudHeldRight;  // what is kept is held to (the planet's axes): right over the tangent of half its width of view
uniform vec4 _CloudHeldUp;     // up over the tangent of half its height of view
uniform vec4 _CloudHeldAhead;  // the way it looks
uniform vec4 _CloudSeeRight;   // this frame's camera (the planet's axes): right over the tangent of half its width of view
uniform vec4 _CloudSeeUp;      // up over the tangent of half its height of view
uniform vec4 _CloudSeeAhead;   // the way it looks
uniform sampler2D _CloudNow;     // this frame's walk: the light, and how much shows through
uniform sampler2D _CloudNowD;    // and: r, how far off (km); g, how much the shadow from above takes from the land's light; b, a: (the far camera's) where in its dot the line went (shares of the dot)
uniform sampler2D _CloudHist;    // the clouds as taken together up to last frame
uniform sampler2D _CloudHistD;    // and: r, how far off (km); g, as _CloudNowD's; b, (the far camera's) how many of its own lines each pixel holds
uniform sampler2D _CameraDepthTexture;   // (the far camera's) what the camera has drawn, how far off (_CloudRight.w: 1 if it can be used; _CloudAhead.w: 1 if depths run backwards)
uniform vec4 _ZBufferParams;
in vec2 vs_TEXCOORD0;
layout(location = 0) out vec4 SV_Target0;
layout(location = 1) out vec4 SV_Target1;

// What was kept, read between its pixels by Keys's curve (a = -0.5 is Catmull and Rom's, as until 2026-10-26; -0.6 a little
// sharper: in the lab (2026-10-26), turning aslant, the lumps 3 to 6 pixels across kept 0.51 of themselves against 0.44; -0.75
// grainier), by five readings of the card's own blending, the corners left out (Jimenez). The answer is held within the four
// pixels nearest (hold): a sharper curve overshoots at a hard edge, and read again every frame the overshoot grows into a rim.
// (Held within its five readings instead, which are blends of the four, it was pulled back to straight blending, and lost the
// sharpening it was there for.) The near camera's not held so, as until 2026-10-26: it is held within the 3x3 around it after.
vec4 keptAt(vec2 uv, float a, bool hold)
{
    vec2 size = vec2(textureSize(_CloudHist, 0));
    vec2 pos = uv * size, c = floor(pos - 0.5) + 0.5, f = pos - c;
    vec2 w0 = a * f * (1.0 - f) * (1.0 - f), w3 = a * f * f * (1.0 - f);
    vec2 w1 = 1.0 + f * f * ((a + 2.0) * f - (a + 3.0)), w2 = 1.0 - w0 - w1 - w3;
    vec2 w12 = w1 + w2, t0 = (c - 1.0) / size, t3 = (c + 2.0) / size, t12 = (c + w2 / w12) / size;
    vec4 r = textureLod(_CloudHist, vec2(t12.x, t0.y), 0.0) * (w12.x * w0.y) + textureLod(_CloudHist, vec2(t0.x, t12.y), 0.0) * (w0.x * w12.y)
           + textureLod(_CloudHist, t12, 0.0) * (w12.x * w12.y)
           + textureLod(_CloudHist, vec2(t3.x, t12.y), 0.0) * (w3.x * w12.y) + textureLod(_CloudHist, vec2(t12.x, t3.y), 0.0) * (w12.x * w3.y);
    r /= w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y;
    if (!hold) return r;
    ivec2 i0 = ivec2(c - 0.5), top = ivec2(size) - 1;
    vec4 p00 = texelFetch(_CloudHist, clamp(i0, ivec2(0), top), 0), p10 = texelFetch(_CloudHist, clamp(i0 + ivec2(1, 0), ivec2(0), top), 0);
    vec4 p01 = texelFetch(_CloudHist, clamp(i0 + ivec2(0, 1), ivec2(0), top), 0), p11 = texelFetch(_CloudHist, clamp(i0 + ivec2(1, 1), ivec2(0), top), 0);
    return clamp(r, min(min(p00, p10), min(p01, p11)), max(max(p00, p10), max(p01, p11)));
}

// The kept pixel nearest a place in last frame's kept picture: its distance (km), g, and how many lines it holds.
vec4 keptNearest(vec2 at, vec2 size)
{
    return texelFetch(_CloudHistD, clamp(ivec2(floor(at * size)), ivec2(0), ivec2(size) - 1), 0);
}

vec4 toYCoCg(vec4 c) { return vec4(0.25 * c.r + 0.5 * c.g + 0.25 * c.b, 0.5 * c.r - 0.5 * c.b, -0.25 * c.r + 0.5 * c.g - 0.25 * c.b, c.a); }
vec4 fromYCoCg(vec4 y) { return vec4(y.x + y.y - y.z, y.x + y.z, y.x - y.y - y.z, y.w); }

// Where a way out from a camera (the planet's axes) stands in a picture looking along a, with r and u its right and up over the
// tangents of half its width and height of view: 0 to 1 each way; z, how far ahead.
vec3 inPicture(vec3 v, vec3 r, vec3 u, vec3 a)
{
    float ahead = dot(v, a);
    return vec3(vec2(dot(v, r), dot(v, u)) / max(ahead, 1e-6) * 0.5 + 0.5, ahead);
}

// And the way out through a place in such a picture.
vec3 throughPicture(vec2 at, vec3 r, vec3 u, vec3 a)
{
    return normalize(a + r / dot(r, r) * (at.x * 2.0 - 1.0) + u / dot(u, u) * (at.y * 2.0 - 1.0));
}

// Where this frame's line of a dot of the walk went through the kept picture (in the screen's kept pixels, from its corner): the
// walk writes where in its dot each line went (b and a of its distance; clouds.glsl, lookIn).
vec2 lineAt(ivec2 cell, float div, vec4 dg)
{
    return (vec2(cell) + dg.ba) * div;
}

// How far ahead of the camera (m) what it has drawn stands at a place on the screen (0 to 1 each way, as the walk's own places): 1e9
// where nothing is drawn there, or where the depth picture is not to be had.
float stopAt(vec2 at)
{
    if (_CloudRight.w < 0.5) return 1e9;
    ivec2 full = textureSize(_CameraDepthTexture, 0);
    float z = texelFetch(_CameraDepthTexture, clamp(ivec2(floor(at * vec2(full))), ivec2(0), full - 1), 0).r;
    bool nothing = _CloudAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
    return nothing ? 1e9 : 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w);
}

void main()
{
    vec2 uv = vs_TEXCOORD0;
    // ---------------------------------------------------------------- the far camera's kept picture laid on the screen's grid
    // (for everything that reads it: the laying on (cloudsover.glsl), the shadow's tint, Volumetric Explosions, Scatterer's flare,
    // Keystone's motion, the meter. Read between pixels every frame, and not kept: nothing of it is read again.)
    if (_CloudHeld.w > 1.5)
    {
        vec3 way = throughPicture(uv, _CloudSeeRight.xyz, _CloudSeeUp.xyz, _CloudSeeAhead.xyz);
        vec2 at = inPicture(way, _CloudHeldRight.xyz, _CloudHeldUp.xyz, _CloudHeldAhead.xyz).xy;
        vec4 c = keptAt(at, _CloudForm.z == 23.0 ? -0.5 : -0.6, true);
        SV_Target0 = vec4(max(c.rgb, vec3(0.0)), clamp(c.a, 0.0, 1.0));
        // (how far off, from the nearest kept pixel: a cloud's distance and the sky's nought are not mixed)
        vec4 nearest = keptNearest(at, vec2(textureSize(_CloudHist, 0)));
        SV_Target1 = vec4(nearest.r, textureLod(_CloudHistD, at, 0.0).g, 0.0, 1.0);
        // (development builds, trial 26: how many of its own lines each kept pixel holds, as grey, white at the most it may. Shown
        // here and not kept: written into what is kept, as at first, the grey was next frame's kept colour, held to the lines'
        // colours, far from it, and the count was cut by the very clip it was there to show.)
        if (_CloudForm.z == 26.0) SV_Target0 = vec4(vec3(nearest.b / max(_CloudKeep.x, 1.0)), 0.0);
        return;
    }
    vec3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
    vec3 d = vec3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
    // ---------------------------------------------------------------- the near camera's: as until 2026-10-26
    // (OpenGL's near camera: its walk a third of the picture's size and kept at that size, or a half and finer on "higher"; and any
    // walk kept at its own size (development builds, trial 22). _CloudKeep nought.)
    if (_CloudKeep.x <= 0.0)
    {
        // (The walk is done at half the picture's size each way, and until the evening of 2026-10-12 what was kept from
        // frame to frame was that size too: every dot of cloud two pixels of the screen each way, and no lump on a heap
        // smaller than that ever seen: heaps of wool. Now what is kept is the picture's own size, and each frame the walk's
        // lines go through a different one of the four pixels that each of its dots stands for. A pixel takes much of a
        // frame whose line went through it and little of the three whose lines went past: over four frames with the
        // camera still, a picture as fine as the screen, for the same walk.)
        vec2 uvNow = uv - _CloudJitter.xy;
        vec4 now = textureLod(_CloudNow, uvNow, 0.0);
        // (and, g, how much the walk's shadow from above takes from the land's light: carried as the distance is)
        vec2 nowDG = textureLod(_CloudNowD, uvNow, 0.0).rg;
        float nowD = nowDG.r * 1000.0;
        // (the least and most of this frame round this pixel: what is kept from before is held inside them, so that nothing
        // left behind by a cloud that moved can linger)
        vec2 lowSize = vec2(textureSize(_CloudNow, 0));
        vec2 px = vec2(1.0) / lowSize;
        vec4 lo = now, hi = now;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                if (i == 0 && j == 0) continue;
                vec4 n = textureLod(_CloudNow, uvNow + vec2(float(i), float(j)) * px, 0.0);
                lo = min(lo, n); hi = max(hi, n);
            }
        float take = _CloudPrevRight.w, on = 1.0;
        bool finerKept = _CloudJitter.z > 0.5;
        if (finerKept)
        {
            // (how far this pixel is from where a line went this frame, in pixels of what is kept: none, or one)
            vec2 off = (fract(uvNow * lowSize) - 0.5) * (vec2(textureSize(_CloudHist, 0)) / lowSize);
            on = clamp(1.0 - max(abs(off.x), abs(off.y)), 0.0, 1.0);
            take *= mix(0.4, 2.2, on * on);
        }
        vec4 outC = now;
        float outD = nowD, outG = nowDG.g;
        if (_CloudPrevAt.w > 0.5)
        {
            // Where this pixel's cloud was in last frame's picture.
            float far = nowD > 0.0 ? nowD : 30000.0;
            vec3 v = d * far + _CloudPrevAt.xyz;
            float ahead = dot(v, _CloudPrevAhead.xyz);
            if (ahead > 1.0)
            {
                vec2 was = vec2(dot(v, _CloudPrevRight.xyz), dot(v, _CloudPrevUp.xyz)) / ahead * 0.5 + 0.5;
                if (was.x > 0.0 && was.x < 1.0 && was.y > 0.0 && was.y < 1.0)
                {
                    // (Where the picture hardly moves, a pixel the walk's line passed by takes next to nothing of this frame's
                    // picture enlarged from the walk's size, and keeps what its own line gave it. With the walk at a quarter of
                    // the picture's size each pixel's own line comes round once in sixteen frames, and the fifteen between, each
                    // taking an eighteenth of the enlarged picture, had blurred more than half of it away again: from orbit
                    // the clouds were soft, and a still view boiled. As the picture moves, as before: half a pixel a frame and
                    // more.)
                    if (finerKept)
                    {
                        float moved = length((was - uv) * vec2(textureSize(_CloudHist, 0)));
                        take = _CloudPrevRight.w * mix(mix(0.02, 0.4, smoothstep(0.03, 0.4, moved)), 2.2, on * on);
                    }
                    vec4 before = clamp(keptAt(was, -0.5, false), lo, hi);
                    vec2 beforeDG = textureLod(_CloudHistD, was, 0.0).rg;
                    float beforeD = beforeDG.r * 1000.0;
                    outG = mix(beforeDG.g, nowDG.g, take);
                    outC = mix(before, now, take);
                    outD = nowD > 0.0 && beforeD > 0.0 ? mix(beforeD, nowD, take) : (nowD > 0.0 ? nowD : beforeD);
                }
            }
        }
        if (!(outC.a >= 0.0) || (floatBitsToInt(outC.r + outC.g + outC.b + outC.a) & 0x7F800000) == 0x7F800000) outC = vec4(0.0, 0.0, 0.0, 1.0);
        SV_Target0 = outC;
        SV_Target1 = vec4(outD / 1000.0, outG, 0.0, 1.0);
        return;
    }

    // ---------------------------------------------------------------- the far camera's (Direct3D's one camera)
    // Each kept pixel counts the lines of its own it holds (b of the kept distance). A line through it this frame is taken as one
    // more of them, by how many it holds: the first whole, the second half, and so on to so many (_CloudKeep.x; fewer where what was
    // kept is read between its pixels, which softens it a little each time: y). A pixel no line went through this frame keeps what
    // it had, carried to where its cloud is now, and takes nothing of the walk's picture enlarged. Only where nothing kept can be
    // used (after a cut, coming in at the picture's edge, where a cloud has moved off what was behind it) does it take this frame's
    // lines round it, spread, and only until a line of its own comes. (Until 2026-10-26 each pixel took four tenths of the frame's
    // share of the walk's picture enlarged as soon as the picture moved half a pixel a frame: in any turn three quarters of what was
    // kept was the quarter-size walk enlarged, and the clouds went soft while the ground beside them stayed sharp (the side-by-side's
    // A2). The count after the survey of Yang, Liu and Salvi 2020; what is kept held near this frame's lines at its own distance
    // after Bauer 2019, and moved toward their mean, not each colour alone, after Karis 2014.)
    // (What is kept is held to the sky (Clouds.cs, Setup): each of its pixels a fixed way out from the camera in the planet's axes,
    // the picture a margin larger than the screen, held so while the camera turns, until a corner of the screen comes near its edge
    // and it is held anew where the camera looks. So in a turn each kept pixel is read at the middle of the same pixel as the frame
    // before, where the curve is exact, not between four: held to the screen, as until 2026-10-26, a turning picture was read between
    // its pixels in each of the fifteen frames between a pixel's own lines (the lab: 0.30 of the 4-pixel lumps kept turning against
    // 0.89 still with the count alone, 0.87 held to the sky). Read between pixels still: on a frame it is held anew, flying past
    // near cloud, and the wind's carrying.)
    vec2 heldSize = vec2(textureSize(_CloudHist, 0));
    ivec2 lowSize = textureSize(_CloudNow, 0);
    float div = _CloudHeld.z;
    d = throughPicture(uv, _CloudHeldRight.xyz, _CloudHeldUp.xyz, _CloudHeldAhead.xyz);
    // (where this kept pixel's way out is in this frame's picture, in the screen's kept pixels; in the margin outside it no line
    // comes, and what is kept there is carried unheld until it comes back)
    vec3 onScreen = inPicture(d, _CloudSeeRight.xyz, _CloudSeeUp.xyz, _CloudSeeAhead.xyz);
    vec2 mid = onScreen.xy * _CloudHeld.xy;
    bool seen = onScreen.z > 0.0 && mid.x >= 0.0 && mid.y >= 0.0 && mid.x < _CloudHeld.x && mid.y < _CloudHeld.y;
    bool anew = _CloudHeld.w > 0.5;
    ivec2 cell0 = clamp(ivec2(floor(mid / div)), ivec2(0), lowSize - 1);
    bool kept = _CloudPrevAt.w > 0.5;
    // (how many pixels a cloud's place in last frame's picture moves for each 1/m nearer it is: the camera's move across this way
    // out, at the picture's scale; nought in a turn, where how far off a cloud is does not change where it was)
    float parallax = length(_CloudPrevAt.xyz - d * dot(d, _CloudPrevAt.xyz)) * heldSize.y * 0.5 * length(_CloudPrevUp.xyz);

    // (This frame's lines round this pixel that stopped at something much nearer than what this pixel sees are left out of all it takes
    // from them below: the nearest cloud, the box what was kept is held in, and the lines spread. A craft before the clouds stops the
    // lines through it at its skin, and they hold none of the cloud behind it; what is kept is held to the sky, and as the camera goes
    // round the craft its outline moves across it: the kept pixels it has just left held its "no cloud" until their own line came,
    // up to sixteen frames on, and the box did not take it away, for the lines through the craft beside them, "no cloud" too, were in
    // the box. A band of sky a few pixels wide along the craft's trailing edge, cut into the clouds behind it, in steps of the walk's
    // dots: the owner's "invisible thing that cuts out the background clouds" round the rocket at an anvil's top (2026-11-04, TODO
    // item 19), filmed 2026-11-10 with the camera going round the rocket at 7.5 degrees a second: some 600 pixels of blue on the
    // anvil's white beside the rocket, none with the camera held still. The mend of 2026-11-04 in the laying on (cloudsover.glsl)
    // weighs the four kept pixels nearest each pixel, and the kept picture is the screen's own size on Direct3D: it could not reach
    // a band four to sixteen pixels wide. Where every line round is so stopped, all are taken, as before. Development builds' trial
    // 27: none left out, as before.)
    float hereStop = seen && _CloudForm.z != 27.0 ? stopAt(onScreen.xy) : 0.0;
    int blocked = 0, open = 0;
    float near = 0.0, nearAll = 0.0;
    for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++)
        {
            ivec2 at = clamp(cell0 + ivec2(i, j), ivec2(0), lowSize - 1);
            vec4 dg = texelFetch(_CloudNowD, at, 0);
            vec2 went = lineAt(at, div, dg);
            vec2 off = went - mid;
            float dist = dg.r * 1000.0;
            bool stopped = hereStop > 0.0 && stopAt(went / _CloudHeld.xy) < hereStop * 0.7 - 20.0;
            if (stopped) blocked |= 1 << ((j + 1) * 3 + i + 1);
            else open++;
            // (Karis's nearest of the neighbours: a cloud's soft edge is carried with the cloud, not with the sky beside it)
            if (dist > 0.0 && max(abs(off.x), abs(off.y)) < 0.5 * div + 1.0)
            {
                nearAll = nearAll > 0.0 ? min(nearAll, dist) : dist;
                if (!stopped) near = near > 0.0 ? min(near, dist) : dist;
            }
        }
    if (open == 0) { blocked = 0; near = nearAll; }

    // What was kept, carried here.
    vec4 held = vec4(0.0, 0.0, 0.0, 1.0);
    float heldD = 0.0, heldG = 0.0, heldN = 0.0, between = 0.0;
    if (kept)
    {
        // Where this pixel's cloud was in last frame's kept picture: first as far off as the nearest cloud round it (the sky as
        // 30 km); then, where what was kept there stands so much nearer or farther that the camera's move would have carried it more
        // than a pixel elsewhere, as far off as that (a second guess); kept only where one of the two agrees with itself. (Its
        // distance read at the kept pixel nearest, never between pixels: read between them, as until 2026-10-26, a heap's edge had
        // a distance halfway to nothing, and was carried as if far off.)
        float guess = near > 0.0 ? near : 30000.0;
        vec3 was = inPicture(d * guess + _CloudPrevAt.xyz, _CloudPrevRight.xyz, _CloudPrevUp.xyz, _CloudPrevAhead.xyz);
        vec4 there = was.z > 1.0 ? keptNearest(was.xy, heldSize) : vec4(0.0);
        float thereD = there.r > 0.0 ? max(there.r * 1000.0 - dot(d, _CloudPrevAt.xyz), 1.0) : 30000.0;
        float miss = abs(1.0 / guess - 1.0 / thereD) * parallax;
        if (miss > 1.0)
        {
            vec3 was2 = inPicture(d * thereD + _CloudPrevAt.xyz, _CloudPrevRight.xyz, _CloudPrevUp.xyz, _CloudPrevAhead.xyz);
            vec4 there2 = was2.z > 1.0 ? keptNearest(was2.xy, heldSize) : vec4(0.0);
            float thereD2 = there2.r > 0.0 ? max(there2.r * 1000.0 - dot(d, _CloudPrevAt.xyz), 1.0) : 30000.0;
            float miss2 = abs(1.0 / thereD - 1.0 / thereD2) * parallax;
            if (miss2 < miss) { was = was2; there = there2; thereD = thereD2; miss = miss2; }
        }
        float fits = 1.0 - smoothstep(1.0, 2.0, miss);
        if (fits > 0.0 && was.z > 1.0 && was.x > 0.0 && was.x < 1.0 && was.y > 0.0 && was.y < 1.0)
        {
            held = keptAt(was.xy, _CloudForm.z == 23.0 ? -0.5 : -0.6, true);
            held = vec4(max(held.rgb, vec3(0.0)), clamp(held.a, 0.0, 1.0));
            heldG = textureLod(_CloudHistD, was.xy, 0.0).g;
            heldD = there.r > 0.0 ? thereD : 0.0;
            heldN = there.b * fits;
            // (how far between last frame's pixels it was read)
            vec2 q = was.xy * heldSize - 0.5;
            vec2 f = abs(q - floor(q + 0.5));
            between = max(f.x, f.y);
        }
    }

    // This frame's lines round this pixel (the walk's dot it lies in and the eight round that, each where it went): the one that
    // went through it, the nine spread (each by how near it went, a bell six tenths of a dot wide), and the box of those whose
    // place the camera's move would change by no more than a pixel and a half from what was kept here (Bauer 2019).
    bool hit = false;
    vec4 own = vec4(0.0, 0.0, 0.0, 1.0), spread = vec4(0.0);
    float ownD = 0.0, ownG = 0.0, ownOff = 0.5, spreadG = 0.0, spreadW = 0.0, wide = 0.6 * div;
    float heldNear = 1.0 / (heldD > 0.0 ? heldD : 30000.0);
    vec4 lo = vec4(1e9), hi = vec4(-1e9), mean = vec4(0.0);
    float gLo = 1e9, gHi = -1e9, count = 0.0;
    for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++)
        {
            ivec2 at = clamp(cell0 + ivec2(i, j), ivec2(0), lowSize - 1);
            vec4 c = texelFetch(_CloudNow, at, 0);
            vec4 dg = texelFetch(_CloudNowD, at, 0);
            vec2 off = lineAt(at, div, dg) - mid;
            float dist = dg.r * 1000.0;
            if (seen && max(abs(off.x), abs(off.y)) < ownOff) { hit = true; ownOff = max(abs(off.x), abs(off.y)); own = c; ownD = dist; ownG = dg.g; }
            // (a line stopped at something much nearer than what this pixel sees: its own line taken still, nothing else of it)
            if (((blocked >> ((j + 1) * 3 + i + 1)) & 1) != 0) continue;
            float w = exp(-dot(off, off) / (2.0 * wide * wide));
            spread += c * w; spreadG += dg.g * w; spreadW += w;
            if (abs(1.0 / (dist > 0.0 ? dist : 30000.0) - heldNear) * parallax <= 1.5)
            {
                vec4 yc = toYCoCg(c);
                lo = min(lo, yc); hi = max(hi, yc); mean += yc;
                gLo = min(gLo, dg.g); gHi = max(gHi, dg.g); count += 1.0;
            }
        }
    spread /= max(spreadW, 1e-5); spreadG /= max(spreadW, 1e-5);
    // (just after a cut every pixel takes the lines spread, so that none stands out sharp among soft ones)
    bool fromOwn = hit && kept;
    vec4 fresh = fromOwn ? own : spread;
    float freshG = fromOwn ? ownG : spreadG, freshD = fromOwn ? ownD : near;

    // What was kept, moved toward the mean of the lines at its distance as far as their box, widened by a share of its size
    // (_CloudKeep.w: a lump smaller than the walk's dots, seen by this pixel's own line some frames ago, stands a little outside
    // what the lines round it see this frame; held inside the plain box, as until 2026-10-26, it was clipped away: in the lab
    // (2026-10-26) a quarter beyond kept 0.90 of the finest lumps held still, the plain box 0.69). No line at its distance: what
    // was kept here has gone from in front, or come out from behind, and is not used.
    if (heldN > 0.0 && seen)
    {
        if (count < 0.5) heldN = 0.0;
        else
        {
            mean /= count;
            vec4 widen = (hi - lo) * _CloudKeep.w + vec4(vec3(0.03 * max(mean.x, 0.0) + 0.0005), 0.01);
            lo -= widen; hi += widen;
            vec4 e = toYCoCg(held) - mean;
            vec4 room = mix(mean - lo, hi - mean, step(0.0, e));
            vec4 past = abs(e) / max(room, vec4(1e-6));
            float beyond = max(max(past.x, past.y), max(past.z, past.w));
            float s = beyond > 1.0 ? 1.0 / beyond : 1.0;
            held = fromYCoCg(mean + e * s);
            held = vec4(max(held.rgb, vec3(0.0)), clamp(held.a, 0.0, 1.0));
            heldG = clamp(heldG, gLo - 0.02, gHi + 0.02);
            // (and trusted the less, the further it had to be moved: a cloud that has changed is taken up the sooner)
            heldN *= mix(0.6, 1.0, smoothstep(0.3, 0.8, s));
        }
    }

    // How many of its own lines this pixel holds now, and how much it takes of what came this frame.
    float most = anew ? _CloudKeep.x : mix(_CloudKeep.x, _CloudKeep.y, smoothstep(0.1, 0.4, between));
    heldN = min(heldN, most);
    float add = fromOwn ? 1.0 : (seen ? _CloudKeep.z * clamp(1.0 - 2.0 * heldN, 0.0, 1.0) : 0.0);
    float newN = min(heldN + add, most);
    float take = newN > 1e-4 ? add / newN : 1.0;
    vec4 outC = mix(held, fresh, take);
    float outG = mix(heldG, freshG, take);
    // (how far off: the two by how much cloud each has, so that the sky, which has no distance, does not pull a cloud's toward
    // nought, and a cloud gone leaves none behind; until 2026-10-26 a distance once kept stayed where the cloud had gone)
    float coverH = (1.0 - held.a) * (1.0 - take), coverF = (1.0 - fresh.a) * take;
    float outD = coverH + coverF > 1e-4 ? (heldD * coverH + freshD * coverF) / (coverH + coverF) : 0.0;
    float outN = newN;
    if (!(outC.a >= 0.0) || (floatBitsToInt(outC.r + outC.g + outC.b + outC.a) & 0x7F800000) == 0x7F800000) { outC = vec4(0.0, 0.0, 0.0, 1.0); outN = 0.0; }
    SV_Target0 = outC;
    SV_Target1 = vec4(outD / 1000.0, outG, outN, 1.0);
}
#endif
