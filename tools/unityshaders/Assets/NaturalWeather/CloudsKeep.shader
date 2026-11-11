// NaturalWeather/CloudsKeep: made from tools/shaderpack/cloudskeep.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalWeather/CloudsKeep"
{
    Properties
    {
        _CloudNow ("CloudNow", 2D) = "" {}
        _CloudNowD ("CloudNowD", 2D) = "" {}
        _CloudHist ("CloudHist", 2D) = "" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One Zero
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma only_renderers d3d11 glcore metal vulkan
            #include "UnityCG.cginc"

            // (the GLSL has each of the game's matrices as its four columns; HLSL has them as rows)
            #define hlslcc_mtx4x4unity_ObjectToWorld transpose(unity_ObjectToWorld)
            #define hlslcc_mtx4x4unity_WorldToObject transpose(unity_WorldToObject)
            #define hlslcc_mtx4x4unity_MatrixVP transpose(UNITY_MATRIX_VP)
            #define hlslcc_mtx4x4unity_MatrixV transpose(UNITY_MATRIX_V)
            #define SampleLod(S, uv, lod) S.SampleLevel(sampler##S, uv, lod)
            #define SampleFlat(S, uv) S.SampleLevel(sampler##S, uv, 0.0)
            #define SampleGradient(S, uv, dx, dy) S.SampleGrad(sampler##S, uv, dx, dy)
            #define Mod(x, y) ((x) - (y) * floor((x) / (y)))
            #define GreaterThan(a, b) ((a) > (b))
            #define GreaterThanEqual(a, b) ((a) >= (b))
            #define LessThan(a, b) ((a) < (b))
            #define LessThanEqual(a, b) ((a) <= (b))
            float4 TexelFetch(Texture2D t, int2 p, int lod) { return t.Load(int3(p, lod)); }
            float4 TexelFetch(Texture3D t, int3 p, int lod) { return t.Load(int4(p, lod)); }
            int2 TextureSize(Texture2D t, int lod) { uint w, h; t.GetDimensions(w, h); return int2(w, h); }
            bool AllZero(float4 v) { return !any(v); }
            // (one of a vector's numbers, chosen by a variable, read and given a new value: see chosen in tools/unityshaders/port.py)
            float Pick(float2 v, int n) { return n <= 0 ? v.x : v.y; }
            float Pick(float3 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : v.z; }
            float Pick(float4 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : n == 2 ? v.z : v.w; }
            float2 Put(float2 v, int n, float x) { return float2(n == 0 ? x : v.x, n == 1 ? x : v.y); }
            float3 Put(float3 v, int n, float x) { return float3(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z); }
            float4 Put(float4 v, int n, float x) { return float4(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z, n == 3 ? x : v.w); }
            // gl_FragCoord: which pixel is being drawn, its rows counted from the bottom, as every picture's are read here. Direct3D counts
            // the rows of what it draws into from the top, and the game draws into its pictures upside down to make up for it (saying so with
            // _ProjectionParams.x at -1): so where it is drawing straight to the screen, the row is turned over, or each picture read by pixel
            // (the camera's depth, the smoke drawn small) would be read upside down.
            float4 FragCoord(float4 pos)
            {
            #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x > 0.0) pos.y = _ScreenParams.y - pos.y;
            #endif
                return pos;
            }

            float4 _CloudSheet;     // xy: 1 (the sheet as large as what is drawn into), z: its depth, w: 1
            float4 _CloudRight;     // xyz: the camera's right, times the tangent of half its width of view
            float4 _CloudUp;     // xyz: the camera's up, times the tangent of half its height of view
            float4 _CloudAhead;     // xyz: the way the camera looks
            float4 _CloudFixedX;     // the planet's own axes, in the scene's: x towards latitude 0 longitude 0, y towards longitude 90, z the north pole
            float4 _CloudFixedY;
            float4 _CloudFixedZ;
            float4 _CloudPrevAt;     // xyz: where the camera is from where it was last frame (m, the planet's axes); w: 1 if last frame's picture can be used
            float4 _CloudPrevRight;     // the near camera's: last frame's camera, in the planet's axes: right over the tangent of half the width of view; the far camera's: what last frame's kept picture was held to, as _CloudHeldRight; w (near): how much of this frame is taken (0 to 1)
            float4 _CloudPrevUp;     // up over the tangent of half the height of view
            float4 _CloudPrevAhead;     // the way it looked
            float4 _CloudJitter;     // xy: how far this frame's lines of sight were moved off the middles of the walk's pixels (in widths and heights of the picture); z: 1 if what is kept from frame to frame has more dots than the walk's picture
            float4 _CloudKeep;     // (the far camera's: see where its pixels are kept) x: how many of its own lines a kept pixel holds at most, read at the middle of last frame's pixel; y: read half a pixel or more between them; z: how many lines the walk's lines round it, spread, count for where nothing kept can be used; w: how far past what this frame's lines round a pixel saw what is kept may stand, in shares of their spread. Nought: the near camera's, taken together as until 2026-10-26
            float4 _CloudForm;     // z: (development builds) the trial (Clouds.Try2): 23, what is kept read by Catmull and Rom's curve; 26, how many lines each kept pixel holds, shown as grey
            float4 _CloudHeld;     // (the far camera's) xy: the screen's kept size (pixels); z: kept pixels a dot of the walk is each way; w: 0 to keep, 1 to keep on a frame what is kept is held anew, 2 to lay it on the screen's grid
            float4 _CloudHeldRight;     // what is kept is held to (the planet's axes): right over the tangent of half its width of view
            float4 _CloudHeldUp;     // up over the tangent of half its height of view
            float4 _CloudHeldAhead;     // the way it looks
            float4 _CloudSeeRight;     // this frame's camera (the planet's axes): right over the tangent of half its width of view
            float4 _CloudSeeUp;     // up over the tangent of half its height of view
            float4 _CloudSeeAhead;     // the way it looks
            Texture2D _CloudNow; SamplerState sampler_CloudNow;     // this frame's walk: the light, and how much shows through
            Texture2D _CloudNowD; SamplerState sampler_CloudNowD;     // and: r, how far off (km); g, how much the shadow from above takes from the land's light; b, a: (the far camera's) where in its dot the line went (shares of the dot)
            Texture2D _CloudHist; SamplerState sampler_CloudHist;     // the clouds as taken together up to last frame
            Texture2D _CloudHistD; SamplerState sampler_CloudHistD;     // and: r, how far off (km); g, as _CloudNowD's; b, (the far camera's) how many of its own lines each pixel holds

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 vs_TEXCOORD0 : TEXCOORD0;
            };

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                // One triangle that covers the whole of what is drawn into, given in its own coordinates (-1 to 1 each way).
                OUT.pos = float4(in_POSITION0.xy * _CloudSheet.xy, _CloudSheet.z, _CloudSheet.w);
                // (every go of the clouds is drawn into pictures of their own (the noise's slices among them), which lie as every
                // picture does on this system: as for the lens's first go, above)
                float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
                way = -1.0;
#endif
                OUT.vs_TEXCOORD0 = float2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);
                return OUT;
            }

            // The clouds taken together over frames (Natural Weather's Clouds.cs): what clouds.glsl's walk drew small this frame, laid into
            // what was kept from the frames before, where each cloud was in the last one worked out from how far off it is. The third go of
            // clouds.glsl until 2026-10-26, moved here as it was: the walk's program is the largest the card is given, and on Direct3D it is
            // compiled without the compiler's optimising (tools/unityshaders/windows.py, HEAVY), which this go paid for with it; and the
            // walk wants the samplers this go held.

            // What was kept, read between its pixels by Keys's curve (a = -0.5 is Catmull and Rom's, as until 2026-10-26; -0.6 a little
            // sharper: in the lab (2026-10-26), turning aslant, the lumps 3 to 6 pixels across kept 0.51 of themselves against 0.44; -0.75
            // grainier), by five readings of the card's own blending, the corners left out (Jimenez). The answer is held within the four
            // pixels nearest (hold): a sharper curve overshoots at a hard edge, and read again every frame the overshoot grows into a rim.
            // (Held within its five readings instead, which are blends of the four, it was pulled back to straight blending, and lost the
            // sharpening it was there for.) The near camera's not held so, as until 2026-10-26: it is held within the 3x3 around it after.
            float4 keptAt(float2 uv, float a, bool hold)
            {
                float2 size = ((float2)(TextureSize(_CloudHist, 0)));
                float2 pos = uv * size, c = floor(pos - 0.5) + 0.5, f = pos - c;
                float2 w0 = a * f * (1.0 - f) * (1.0 - f), w3 = a * f * f * (1.0 - f);
                float2 w1 = 1.0 + f * f * ((a + 2.0) * f - (a + 3.0)), w2 = 1.0 - w0 - w1 - w3;
                float2 w12 = w1 + w2, t0 = (c - 1.0) / size, t3 = (c + 2.0) / size, t12 = (c + w2 / w12) / size;
                float4 r = SampleLod(_CloudHist, float2(t12.x, t0.y), 0.0) * (w12.x * w0.y) + SampleLod(_CloudHist, float2(t0.x, t12.y), 0.0) * (w0.x * w12.y)
                       + SampleLod(_CloudHist, t12, 0.0) * (w12.x * w12.y)
                       + SampleLod(_CloudHist, float2(t3.x, t12.y), 0.0) * (w3.x * w12.y) + SampleLod(_CloudHist, float2(t12.x, t3.y), 0.0) * (w12.x * w3.y);
                r /= w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y;
                if (!hold) return r;
                int2 i0 = ((int2)(c - 0.5)), top = ((int2)(size)) - 1;
                float4 p00 = TexelFetch(_CloudHist, clamp(i0, ((int2)(0)), top), 0), p10 = TexelFetch(_CloudHist, clamp(i0 + int2(1, 0), ((int2)(0)), top), 0);
                float4 p01 = TexelFetch(_CloudHist, clamp(i0 + int2(0, 1), ((int2)(0)), top), 0), p11 = TexelFetch(_CloudHist, clamp(i0 + int2(1, 1), ((int2)(0)), top), 0);
                return clamp(r, min(min(p00, p10), min(p01, p11)), max(max(p00, p10), max(p01, p11)));
            }

            // The kept pixel nearest a place in last frame's kept picture: its distance (km), g, and how many lines it holds.
            float4 keptNearest(float2 at, float2 size)
            {
                return TexelFetch(_CloudHistD, clamp(((int2)(floor(at * size))), ((int2)(0)), ((int2)(size)) - 1), 0);
            }

            float4 toYCoCg(float4 c) { return float4(0.25 * c.r + 0.5 * c.g + 0.25 * c.b, 0.5 * c.r - 0.5 * c.b, -0.25 * c.r + 0.5 * c.g - 0.25 * c.b, c.a); }
            float4 fromYCoCg(float4 y) { return float4(y.x + y.y - y.z, y.x + y.z, y.x - y.y - y.z, y.w); }

            // Where a way out from a camera (the planet's axes) stands in a picture looking along a, with r and u its right and up over the
            // tangents of half its width and height of view: 0 to 1 each way; z, how far ahead.
            float3 inPicture(float3 v, float3 r, float3 u, float3 a)
            {
                float ahead = dot(v, a);
                return float3(float2(dot(v, r), dot(v, u)) / max(ahead, 1e-6) * 0.5 + 0.5, ahead);
            }

            // And the way out through a place in such a picture.
            float3 throughPicture(float2 at, float3 r, float3 u, float3 a)
            {
                return normalize(a + r / dot(r, r) * (at.x * 2.0 - 1.0) + u / dot(u, u) * (at.y * 2.0 - 1.0));
            }

            // Where this frame's line of a dot of the walk went through the kept picture (in the screen's kept pixels, from its corner): the
            // walk writes where in its dot each line went (b and a of its distance; clouds.glsl, lookIn).
            float2 lineAt(int2 cell, float div, float4 dg)
            {
                return (((float2)(cell)) + dg.ba) * div;
            }

            struct both
            {
                float4 result : SV_Target0;
                float4 result1 : SV_Target1;
            };

            both Both(float4 a, float4 b) { both o; o.result = a; o.result1 = b; return o; }

            both frag(v2f IN)
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float4 result1 = float4(0.0, 0.0, 0.0, 0.0);
                float2 uv = IN.vs_TEXCOORD0;
                // ---------------------------------------------------------------- the far camera's kept picture laid on the screen's grid
                // (for everything that reads it: the laying on (cloudsover.glsl), the shadow's tint, Volumetric Explosions, Scatterer's flare,
                // Keystone's motion, the meter. Read between pixels every frame, and not kept: nothing of it is read again.)
                if (_CloudHeld.w > 1.5)
                {
                    float3 way = throughPicture(uv, _CloudSeeRight.xyz, _CloudSeeUp.xyz, _CloudSeeAhead.xyz);
                    float2 at = inPicture(way, _CloudHeldRight.xyz, _CloudHeldUp.xyz, _CloudHeldAhead.xyz).xy;
                    float4 c = keptAt(at, _CloudForm.z == 23.0 ? -0.5 : -0.6, true);
                    result = float4(max(c.rgb, ((float3)(0.0))), clamp(c.a, 0.0, 1.0));
                    // (how far off, from the nearest kept pixel: a cloud's distance and the sky's nought are not mixed)
                    float4 nearest = keptNearest(at, ((float2)(TextureSize(_CloudHist, 0))));
                    result1 = float4(nearest.r, SampleLod(_CloudHistD, at, 0.0).g, 0.0, 1.0);
                    // (development builds, trial 26: how many of its own lines each kept pixel holds, as grey, white at the most it may. Shown
                    // here and not kept: written into what is kept, as at first, the grey was next frame's kept colour, held to the lines'
                    // colours, far from it, and the count was cut by the very clip it was there to show.)
                    if (_CloudForm.z == 26.0) result = float4(((float3)(nearest.b / max(_CloudKeep.x, 1.0))), 0.0);
                    return Both(result, result1);
                }
                float3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
                float3 d = float3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
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
                    float2 uvNow = uv - _CloudJitter.xy;
                    float4 now = SampleLod(_CloudNow, uvNow, 0.0);
                    // (and, g, how much the walk's shadow from above takes from the land's light: carried as the distance is)
                    float2 nowDG = SampleLod(_CloudNowD, uvNow, 0.0).rg;
                    float nowD = nowDG.r * 1000.0;
                    // (the least and most of this frame round this pixel: what is kept from before is held inside them, so that nothing
                    // left behind by a cloud that moved can linger)
                    float2 lowSize = ((float2)(TextureSize(_CloudNow, 0)));
                    float2 px = ((float2)(1.0)) / lowSize;
                    float4 lo = now, hi = now;
                    { [loop] for (int j = -1; j <= 1; j++)
                        { [loop] for (int i = -1; i <= 1; i++)
                        {
                            if (i == 0 && j == 0) continue;
                            float4 n = SampleLod(_CloudNow, uvNow + float2(float(i), float(j)) * px, 0.0);
                            lo = min(lo, n); hi = max(hi, n);
                        } } }
                    float take = _CloudPrevRight.w, on = 1.0;
                    bool finerKept = _CloudJitter.z > 0.5;
                    if (finerKept)
                    {
                        // (how far this pixel is from where a line went this frame, in pixels of what is kept: none, or one)
                        float2 off = (frac(uvNow * lowSize) - 0.5) * (((float2)(TextureSize(_CloudHist, 0))) / lowSize);
                        on = clamp(1.0 - max(abs(off.x), abs(off.y)), 0.0, 1.0);
                        take *= lerp(0.4, 2.2, on * on);
                    }
                    float4 outC = now;
                    float outD = nowD, outG = nowDG.g;
                    if (_CloudPrevAt.w > 0.5)
                    {
                        // Where this pixel's cloud was in last frame's picture.
                        float far = nowD > 0.0 ? nowD : 30000.0;
                        float3 v = d * far + _CloudPrevAt.xyz;
                        float ahead = dot(v, _CloudPrevAhead.xyz);
                        if (ahead > 1.0)
                        {
                            float2 was = float2(dot(v, _CloudPrevRight.xyz), dot(v, _CloudPrevUp.xyz)) / ahead * 0.5 + 0.5;
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
                                    float moved = length((was - uv) * ((float2)(TextureSize(_CloudHist, 0))));
                                    take = _CloudPrevRight.w * lerp(lerp(0.02, 0.4, smoothstep(0.03, 0.4, moved)), 2.2, on * on);
                                }
                                float4 before = clamp(keptAt(was, -0.5, false), lo, hi);
                                float2 beforeDG = SampleLod(_CloudHistD, was, 0.0).rg;
                                float beforeD = beforeDG.r * 1000.0;
                                outG = lerp(beforeDG.g, nowDG.g, take);
                                outC = lerp(before, now, take);
                                outD = nowD > 0.0 && beforeD > 0.0 ? lerp(beforeD, nowD, take) : (nowD > 0.0 ? nowD : beforeD);
                            }
                        }
                    }
                    if (!(outC.a >= 0.0) || (asint(outC.r + outC.g + outC.b + outC.a) & 0x7F800000) == 0x7F800000) outC = float4(0.0, 0.0, 0.0, 1.0);
                    result = outC;
                    result1 = float4(outD / 1000.0, outG, 0.0, 1.0);
                    return Both(result, result1);
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
                float2 heldSize = ((float2)(TextureSize(_CloudHist, 0)));
                int2 lowSize = TextureSize(_CloudNow, 0);
                float div = _CloudHeld.z;
                d = throughPicture(uv, _CloudHeldRight.xyz, _CloudHeldUp.xyz, _CloudHeldAhead.xyz);
                // (where this kept pixel's way out is in this frame's picture, in the screen's kept pixels; in the margin outside it no line
                // comes, and what is kept there is carried unheld until it comes back)
                float3 onScreen = inPicture(d, _CloudSeeRight.xyz, _CloudSeeUp.xyz, _CloudSeeAhead.xyz);
                float2 mid = onScreen.xy * _CloudHeld.xy;
                bool seen = onScreen.z > 0.0 && mid.x >= 0.0 && mid.y >= 0.0 && mid.x < _CloudHeld.x && mid.y < _CloudHeld.y;
                bool anew = _CloudHeld.w > 0.5;
                int2 cell0 = clamp(((int2)(floor(mid / div))), ((int2)(0)), lowSize - 1);
                bool kept = _CloudPrevAt.w > 0.5;
                // (how many pixels a cloud's place in last frame's picture moves for each 1/m nearer it is: the camera's move across this way
                // out, at the picture's scale; nought in a turn, where how far off a cloud is does not change where it was)
                float parallax = length(_CloudPrevAt.xyz - d * dot(d, _CloudPrevAt.xyz)) * heldSize.y * 0.5 * length(_CloudPrevUp.xyz);

                // The nearest cloud this frame's lines close round this pixel saw (Karis's nearest of the neighbours: a cloud's soft edge is
                // carried with the cloud, not with the sky beside it).
                float near = 0.0;
                { [loop] for (int j = -1; j <= 1; j++)
                    { [loop] for (int i = -1; i <= 1; i++)
                    {
                        int2 at = clamp(cell0 + int2(i, j), ((int2)(0)), lowSize - 1);
                        float4 dg = TexelFetch(_CloudNowD, at, 0);
                        float2 off = lineAt(at, div, dg) - mid;
                        float dist = dg.r * 1000.0;
                        if (dist > 0.0 && max(abs(off.x), abs(off.y)) < 0.5 * div + 1.0) near = near > 0.0 ? min(near, dist) : dist;
                    } } }

                // What was kept, carried here.
                float4 held = float4(0.0, 0.0, 0.0, 1.0);
                float heldD = 0.0, heldG = 0.0, heldN = 0.0, between = 0.0;
                if (kept)
                {
                    // Where this pixel's cloud was in last frame's kept picture: first as far off as the nearest cloud round it (the sky as
                    // 30 km); then, where what was kept there stands so much nearer or farther that the camera's move would have carried it more
                    // than a pixel elsewhere, as far off as that (a second guess); kept only where one of the two agrees with itself. (Its
                    // distance read at the kept pixel nearest, never between pixels: read between them, as until 2026-10-26, a heap's edge had
                    // a distance halfway to nothing, and was carried as if far off.)
                    float guess = near > 0.0 ? near : 30000.0;
                    float3 was = inPicture(d * guess + _CloudPrevAt.xyz, _CloudPrevRight.xyz, _CloudPrevUp.xyz, _CloudPrevAhead.xyz);
                    float4 there = was.z > 1.0 ? keptNearest(was.xy, heldSize) : ((float4)(0.0));
                    float thereD = there.r > 0.0 ? max(there.r * 1000.0 - dot(d, _CloudPrevAt.xyz), 1.0) : 30000.0;
                    float miss = abs(1.0 / guess - 1.0 / thereD) * parallax;
                    if (miss > 1.0)
                    {
                        float3 was2 = inPicture(d * thereD + _CloudPrevAt.xyz, _CloudPrevRight.xyz, _CloudPrevUp.xyz, _CloudPrevAhead.xyz);
                        float4 there2 = was2.z > 1.0 ? keptNearest(was2.xy, heldSize) : ((float4)(0.0));
                        float thereD2 = there2.r > 0.0 ? max(there2.r * 1000.0 - dot(d, _CloudPrevAt.xyz), 1.0) : 30000.0;
                        float miss2 = abs(1.0 / thereD - 1.0 / thereD2) * parallax;
                        if (miss2 < miss) { was = was2; there = there2; thereD = thereD2; miss = miss2; }
                    }
                    float fits = 1.0 - smoothstep(1.0, 2.0, miss);
                    if (fits > 0.0 && was.z > 1.0 && was.x > 0.0 && was.x < 1.0 && was.y > 0.0 && was.y < 1.0)
                    {
                        held = keptAt(was.xy, _CloudForm.z == 23.0 ? -0.5 : -0.6, true);
                        held = float4(max(held.rgb, ((float3)(0.0))), clamp(held.a, 0.0, 1.0));
                        heldG = SampleLod(_CloudHistD, was.xy, 0.0).g;
                        heldD = there.r > 0.0 ? thereD : 0.0;
                        heldN = there.b * fits;
                        // (how far between last frame's pixels it was read)
                        float2 q = was.xy * heldSize - 0.5;
                        float2 f = abs(q - floor(q + 0.5));
                        between = max(f.x, f.y);
                    }
                }

                // This frame's lines round this pixel (the walk's dot it lies in and the eight round that, each where it went): the one that
                // went through it, the nine spread (each by how near it went, a bell six tenths of a dot wide), and the box of those whose
                // place the camera's move would change by no more than a pixel and a half from what was kept here (Bauer 2019).
                bool hit = false;
                float4 own = float4(0.0, 0.0, 0.0, 1.0), spread = ((float4)(0.0));
                float ownD = 0.0, ownG = 0.0, ownOff = 0.5, spreadG = 0.0, spreadW = 0.0, wide = 0.6 * div;
                float heldNear = 1.0 / (heldD > 0.0 ? heldD : 30000.0);
                float4 lo = ((float4)(1e9)), hi = ((float4)(-1e9)), mean = ((float4)(0.0));
                float gLo = 1e9, gHi = -1e9, count = 0.0;
                { [loop] for (int j = -1; j <= 1; j++)
                    { [loop] for (int i = -1; i <= 1; i++)
                    {
                        int2 at = clamp(cell0 + int2(i, j), ((int2)(0)), lowSize - 1);
                        float4 c = TexelFetch(_CloudNow, at, 0);
                        float4 dg = TexelFetch(_CloudNowD, at, 0);
                        float2 off = lineAt(at, div, dg) - mid;
                        float dist = dg.r * 1000.0;
                        if (seen && max(abs(off.x), abs(off.y)) < ownOff) { hit = true; ownOff = max(abs(off.x), abs(off.y)); own = c; ownD = dist; ownG = dg.g; }
                        float w = exp(-dot(off, off) / (2.0 * wide * wide));
                        spread += c * w; spreadG += dg.g * w; spreadW += w;
                        if (abs(1.0 / (dist > 0.0 ? dist : 30000.0) - heldNear) * parallax <= 1.5)
                        {
                            float4 yc = toYCoCg(c);
                            lo = min(lo, yc); hi = max(hi, yc); mean += yc;
                            gLo = min(gLo, dg.g); gHi = max(gHi, dg.g); count += 1.0;
                        }
                    } } }
                spread /= max(spreadW, 1e-5); spreadG /= max(spreadW, 1e-5);
                // (just after a cut every pixel takes the lines spread, so that none stands out sharp among soft ones)
                bool fromOwn = hit && kept;
                float4 fresh = fromOwn ? own : spread;
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
                        float4 widen = (hi - lo) * _CloudKeep.w + float4(((float3)(0.03 * max(mean.x, 0.0) + 0.0005)), 0.01);
                        lo -= widen; hi += widen;
                        float4 e = toYCoCg(held) - mean;
                        float4 room = lerp(mean - lo, hi - mean, step(0.0, e));
                        float4 past = abs(e) / max(room, ((float4)(1e-6)));
                        float beyond = max(max(past.x, past.y), max(past.z, past.w));
                        float s = beyond > 1.0 ? 1.0 / beyond : 1.0;
                        held = fromYCoCg(mean + e * s);
                        held = float4(max(held.rgb, ((float3)(0.0))), clamp(held.a, 0.0, 1.0));
                        heldG = clamp(heldG, gLo - 0.02, gHi + 0.02);
                        // (and trusted the less, the further it had to be moved: a cloud that has changed is taken up the sooner)
                        heldN *= lerp(0.6, 1.0, smoothstep(0.3, 0.8, s));
                    }
                }

                // How many of its own lines this pixel holds now, and how much it takes of what came this frame.
                float most = anew ? _CloudKeep.x : lerp(_CloudKeep.x, _CloudKeep.y, smoothstep(0.1, 0.4, between));
                heldN = min(heldN, most);
                float add = fromOwn ? 1.0 : (seen ? _CloudKeep.z * clamp(1.0 - 2.0 * heldN, 0.0, 1.0) : 0.0);
                float newN = min(heldN + add, most);
                float take = newN > 1e-4 ? add / newN : 1.0;
                float4 outC = lerp(held, fresh, take);
                float outG = lerp(heldG, freshG, take);
                // (how far off: the two by how much cloud each has, so that the sky, which has no distance, does not pull a cloud's toward
                // nought, and a cloud gone leaves none behind; until 2026-10-26 a distance once kept stayed where the cloud had gone)
                float coverH = (1.0 - held.a) * (1.0 - take), coverF = (1.0 - fresh.a) * take;
                float outD = coverH + coverF > 1e-4 ? (heldD * coverH + freshD * coverF) / (coverH + coverF) : 0.0;
                float outN = newN;
                if (!(outC.a >= 0.0) || (asint(outC.r + outC.g + outC.b + outC.a) & 0x7F800000) == 0x7F800000) { outC = float4(0.0, 0.0, 0.0, 1.0); outN = 0.0; }
                result = outC;
                result1 = float4(outD / 1000.0, outG, outN, 1.0);
                return Both(result, result1);
            }
            ENDCG
        }
    }
}
