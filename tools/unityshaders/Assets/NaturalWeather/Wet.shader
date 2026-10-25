// NaturalWeather/Wet: made from tools/shaderpack/wet.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalWeather/Wet"
{
    Properties
    {
        _WetDrops ("WetDrops", 2D) = "" {}
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

            float4 _WetRight;     // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
            float4 _WetUp;     // xyz: the camera's up, times the tangent of half its height of view; w: the clock (s)
            float4 _WetAhead;     // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
            float4 _WetCam;     // xyz: the camera, from the planet's centre (m, the scene's axes); w: the planet's radius (m)
            float4 _WetHere;     // xy: where the camera is over the ground, in metres along two level ways fixed to the ground (kept small: the puddles' pattern is read by it); w: 1 if the world has a sea
            float4 _WetAcross;     // xyz: the first of those level ways (the scene's axes, a unit)
            float4 _WetAlong;     // xyz: the second
            float4 _WetGround;     // x: how wet the ground is (0 to 1); y: how much of level ground stands under water (0 to 1); z: how hard it rains now (0 to 1: rings in the puddles); w: how far off the wet is still drawn (m)
            float4 _WetSky;     // rgb: the sky's light as a mirror shows it where nothing better is known; w: how bright a drop's glint is
            float4 _WetSun;     // xy: which way the brightest light lies on the glass (towards the sun, or straight up under an overcast or at night; a unit); z: how strong it is (0 to 1)
            float4 _WetGlass;     // x: how much of the glass has drops on it (0 to 1); y: the picture's width over its height; zw: which way is down on the glass (a unit; nothing: the camera looks straight up or down, and no drop runs)
            float4 _WetDropsSize;     // the picture of the water on the glass (Drops.cs, drops.glsl): xy one of its points, as a part of the picture; z millimetres of glass to one of its points; w 1 if it is there
            Texture2D _WetDrops; SamplerState sampler_WetDrops;     // how high the water stands on the glass (mm), point by point
            Texture2D _WetScene; SamplerState sampler_WetScene;     // the picture so far
            Texture2D _CameraDepthTexture; SamplerState sampler_CameraDepthTexture;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 vs_TEXCOORD0 : TEXCOORD0;
            };

            // Natural Weather: rain on the camera's glass and on the ground (Wet.cs). One triangle over the whole picture.

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                OUT.pos = float4(in_POSITION0.xy, 0.5, 1.0);
                // (drawn into whatever the camera is drawing into, which may lie either way: as for the lens's second go, above; Natural
                // Light's veil and the sea's lens in the water the same; and Cinema Camera's look, whose picture came out upside down on
                // Direct3D without it, the camera drawing into a picture of its own there (the PC, 2026-10-20))
                float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
                way = _ProjectionParams.x;
#endif
                OUT.vs_TEXCOORD0 = float2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);
                return OUT;
            }

            // What rain does to a picture besides falling through it.
            //
            // The ground. Water fills the small roughness of a surface, so it scatters less light back up (wet ground is darker: that
            // part is done where the clouds' shadow is laid on, cloudshade.glsl, for everything in sight) and mirrors more: hardly at
            // all seen from above, strongly seen along the ground (a water surface throws back a fiftieth of the light that meets it
            // squarely and nearly all that meets it at a glance). Where level ground cannot drain, the water stands in puddles,
            // true mirrors, each ringed by the drops that fall in it. Here the mirror: for each pixel of level ground near the
            // camera, what the sky (or whatever stands far off that way) shows in it, by how wet it is and how flat the look.
            //
            // The glass. Drops land on it, sit, grow by running into each other, run when they are heavy enough or the air past the
            // glass is fast enough, and leave wet tracks: worked out drop by drop in Drops.cs, and drawn into a picture of how high
            // the water stands (drops.glsl). Here: the line of sight bent through that water.
            // Laid over the finished picture (before the lens's own effects), only while there is something to draw.

            float chance(float2 p) { float3 q = frac(((float3)(p.xyx)) * float3(0.1031, 0.1030, 0.0973)); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }
            float3 chances(float2 p) { float3 q = frac(((float3)(p.xyx)) * float3(0.1031, 0.1030, 0.0973)); q += dot(q, q.yxz + 33.33); return frac((q.xxy + q.yzz) * q.zyx); }

            // Smooth noise over the ground, 0 to 1.
            float ground2(float2 p)
            {
                float2 i = floor(p), f = p - i;
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(chance(i), chance(i + float2(1.0, 0.0)), f.x), lerp(chance(i + float2(0.0, 1.0)), chance(i + float2(1.0, 1.0)), f.x), f.y);
            }

            // ---------------------------------------------------------------- the glass
            //
            // A drop on glass is a cap of a sphere: it meets the glass at its own angle all round (thirty to forty-five degrees on
            // plain glass) and is nearly level in its middle. Seen through, it is a lens whose power is all in its slope: the line of
            // sight goes straight through the level middle and is turned more and more towards the drop's far side the nearer it
            // passes to the rim, by Snell's rule (out of water, 1.333, into air): fifteen degrees at the rim of a drop that stands
            // at thirty-five, twenty-five at forty-five, and at forty-nine and over nothing comes through the rim at all. So a drop
            // shows what lies round about, small, upside down and squeezed against its rim; its lower part is bright because the sky
            // is in it; and it needs no outline drawn round it and no glint painted on. (The same reading of glass as Apple gives of
            // its Liquid Glass: a thing that "bends, shapes and concentrates light" and is known by its lensing, strongest along its
            // edge, where frosted materials scattered the light instead; those who have rebuilt it take a height that falls away
            // near the edge, its slope, and Snell's rule, as here.)
            //
            // And it is out of focus, being on the lens: its edge is soft, and what shows in it is blurred, because every point of
            // the picture is lit through a patch of the drop as wide as the lens's opening. A drop is not a circle either: its edge
            // catches on the glass, and a large one hangs down with its weight.

            // How the water's surface leans on the glass, point by point: its slope (mm of height for a mm across, along the picture's
            // two ways), from the picture of how high it stands.
            float2 slopeAt(float2 at)
            {
                float2 px = _WetDropsSize.xy;
                float l = SampleLod(_WetDrops, at - float2(px.x, 0.0), 0.0).r, r = SampleLod(_WetDrops, at + float2(px.x, 0.0), 0.0).r;
                float b = SampleLod(_WetDrops, at - float2(0.0, px.y), 0.0).r, t = SampleLod(_WetDrops, at + float2(0.0, px.y), 0.0).r;
                return float2(r - l, t - b) / (2.0 * _WetDropsSize.z);
            }

            // For water whose surface leans so (its slope): how far the line of sight through it is turned, as the tangent of the
            // angle, and which way: towards where the water stands higher, more the steeper it leans; a thousand where nothing
            // comes through (looked through that steep a slope from inside the water, the light is all thrown back).
            float2 bendsBy(float2 slope)
            {
                float s = length(slope);
                if (s < 1e-4) return ((float2)(0.0));
                float tilt = s / sqrt(1.0 + s * s), air = 1.333 * tilt;
                if (air >= 0.995) return ((float2)(1e3));
                return slope / s * tan(asin(air) - asin(tilt));
            }

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float2 uv = IN.vs_TEXCOORD0;
                // ---------------------------------------------------------------- the glass: how much water this pixel is seen through
                float water = 0.0;
                { [loop] for (int once = _WetGlass.x > 0.003 && _WetDropsSize.w > 0.5 ? 0 : 1; once < 1; once++) water = SampleLod(_WetDrops, uv, 0.0).r; }
                float4 scene = SampleLod(_WetScene, uv, 0.0);
                float3 colour = scene.rgb;

                // ---------------------------------------------------------------- the ground: the sky in it
                // (the place drawn at each pixel, and which way the surface faces there, from how the place changes from one pixel
                // to the next: worked out for every pixel before anything is decided by them, as a card's own differences ask)
                float z = SampleLod(_CameraDepthTexture, uv, 0.0).r;
                bool nothing = _WetAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
                float3 dir = normalize(_WetAhead.xyz + _WetRight.xyz * (uv.x * 2.0 - 1.0) + _WetUp.xyz * (uv.y * 2.0 - 1.0));
                float t = nothing ? 1e7 : 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w) / max(dot(dir, _WetAhead.xyz), 1e-4);
                float3 near = dir * t;
                float3 facing = cross(ddx(near), ddy(near));
                { [loop] for (int wet = _WetGround.x > 0.01 && _WetRight.w > 0.5 ? 0 : 1; wet < 1; wet++)
                {
                    if (nothing || t > _WetGround.w) break;
                    // (the sea is drawn after the depth picture is made, which holds the seabed under it: where the line of sight meets
                    // the sea's surface first, it is the sea that is seen, which is wet enough)
                    if (_WetHere.w > 0.5)
                    {
                        float bb = dot(_WetCam.xyz, dir), cc = dot(_WetCam.xyz, _WetCam.xyz) - _WetCam.w * _WetCam.w;
                        float disc = bb * bb - cc;
                        if (disc > 0.0 && cc > 0.0 && -bb - sqrt(disc) > 0.0 && -bb - sqrt(disc) < t) break;
                    }
                    float3 up = normalize(_WetCam.xyz + near);
                    float size = length(facing);
                    facing = size > 1e-10 ? facing / size : up;
                    if (dot(facing, dir) > 0.0) facing = -facing;
                    float level = dot(facing, up);
                    if (level < 0.35) break;
                    float far = 1.0 - smoothstep(0.55, 1.0, t / max(_WetGround.w, 1.0));
                    // ---- where the water stands: the hollows of level ground, more of them the more it has rained; the same hollows
                    // every time (the pattern is fixed to the ground)
                    float2 over = _WetHere.xy + float2(dot(near, _WetAcross.xyz), dot(near, _WetAlong.xyz));
                    // (Hollows eight metres and two across, and at the most a quarter of level ground under water. How level the
                    // ground is, is known only roughly here, from one pixel to the next: with the pools cut off sharply by it, and
                    // with hollows a foot across among them, every pool's edge was a staircase of the picture's own dots, and with
                    // half the ground under water a lawn was a camouflage net.)
                    float hollow = 0.6 * ground2(over * 0.12) + 0.4 * ground2(over * 0.47 + 17.0);
                    float reach = lerp(0.84, 0.60, _WetGround.y);
                    float pool = smoothstep(reach - 0.035, reach + 0.035, hollow) * smoothstep(0.86, 0.96, level) * smoothstep(0.02, 0.1, _WetGround.y);
                    // (and not on grass, which this knows only by its colour: water among the blades of a lawn is no mirror, and a
                    // lawn with the sky lying on it in patches looked as if snow had fallen on it)
                    pool *= 1.0 - smoothstep(0.03, 0.14, (colour.g - max(colour.r, colour.b)) / max(colour.g, 1e-3));
                    float film = _WetGround.x * smoothstep(0.35, 0.8, level);
                    // ---- the water's face: level in a pool, and ringed where drops fall in it (each ring grows from where its drop
                    // fell and dies as it grows); elsewhere the ground's own
                    float3 face = normalize(lerp(facing, up, pool));
                    { [loop] for (int rings = pool * _WetGround.z > 0.01 ? 0 : 1; rings < 1; rings++)
                    {
                        float2 lean = ((float2)(0.0));
                        { [loop] for (int k = 0; k < 2; k++)
                        {
                            float2 at = over * (k == 0 ? 2.6 : 4.3) + float(k) * 11.7, id = floor(at);
                            float3 own = chances(id + float(k) * 3.3);
                            float turn = _WetUp.w * (0.9 + 0.5 * own.z) + own.x * 5.0;
                            float phase = frac(turn);
                            float3 now = chances(id + floor(turn) * 1.7 + float(k));
                            if (now.z > _WetGround.z * 1.4) continue;
                            float2 d = at - id - (0.25 + 0.5 * now.xy);
                            float out_ = length(d), ring = phase * 0.45;
                            lean += (out_ > 1e-4 ? d / out_ : ((float2)(0.0))) * sin((out_ - ring) * 55.0) * exp(-(out_ - ring) * (out_ - ring) * 260.0) * (1.0 - phase);
                        } }
                        face = normalize(face + (_WetAcross.xyz * lean.x + _WetAlong.xyz * lean.y) * 0.22);
                    } }
                    // ---- what the water shows: the picture itself in the mirrored direction, where that is on the picture and what
                    // is drawn there is far off (the sky, clouds, a far shore: near things would be shown in the wrong place); else
                    // the sky's light as given
                    float look = clamp(dot(face, -dir), 0.0, 1.0);
                    float mirrors = 0.02 + 0.98 * pow(1.0 - look, 5.0);
                    float3 back = reflect(dir, face);
                    float3 shows = _WetSky.rgb;
                    float ahead = dot(back, _WetAhead.xyz);
                    { [loop] for (int seen = ahead > 0.05 ? 0 : 1; seen < 1; seen++)
                    {
                        float2 there = 0.5 + 0.5 * float2(dot(back, _WetRight.xyz) / dot(_WetRight.xyz, _WetRight.xyz), dot(back, _WetUp.xyz) / dot(_WetUp.xyz, _WetUp.xyz)) / ahead;
                        float2 edge = min(there, 1.0 - there);
                        float inside = smoothstep(0.0, 0.06, min(edge.x, edge.y));
                        if (inside <= 0.0) break;
                        float zt = SampleLod(_CameraDepthTexture, there, 0.0).r;
                        bool sky = _WetAhead.w > 0.5 ? zt <= 0.000001 : zt >= 0.999999;
                        float tt = sky ? 1e9 : 1.0 / (_ZBufferParams.z * zt + _ZBufferParams.w);
                        float farOff = smoothstep(2.5 * t, 5.0 * t, tt);
                        shows = lerp(shows, min(SampleLod(_WetScene, there, 0.0).rgb, ((float3)(24.0))), inside * farOff);
                    } }
                    // (a pool is a mirror; damp ground a poor one, and blurred: a fifth of it, of the sky's light alone)
                    float glassy = pool + (1.0 - pool) * film * 0.2;
                    float3 thrown = lerp(_WetSky.rgb, shows, pool);
                    // (and a pool is darker than the ground it lies on where it is looked into from above: the light goes in and little comes back)
                    colour = lerp(colour * (1.0 - 0.12 * pool * far), thrown, clamp(mirrors * glassy * far, 0.0, 1.0));
                } }

                // ---------------------------------------------------------------- and the drops on the glass, over all of it
                { [loop] for (int once = water > 0.0005 ? 0 : 1; once < 1; once++)
                {
                    // (how far a turn of the line of sight moves it across the picture: by the tangent, over the picture's own)
                    float tanUp = max(length(_WetUp.xyz), 0.05);
                    float2 across = float2(0.5 / (tanUp * max(_WetGlass.y, 0.5)), 0.5 / tanUp);
                    // (out of focus, being on the lens: each point of the picture is seen through a patch of the water as wide as the
                    // lens's opening, half a millimetre here: five lines of sight, each bent by the water where it goes through; the
                    // middle one's red bent a little less and its blue a little more, as water bends them)
                    float2 spread = float2(0.45 / (60.0 * max(_WetGlass.y, 0.5)), 0.45 / 60.0);    // (not "patch": a word GLSL keeps, which NVIDIA's compiler refuses)
                    float3 bent = ((float3)(0.0));
                    float lines = 0.0, dark = 0.0;
                    float2 slope0 = ((float2)(0.0));
                    { [loop] for (int k = 0; k < 5; k++)
                    {
                        float2 off = k == 0 ? ((float2)(0.0)) : spread * float2(k == 1 ? 1.0 : k == 2 ? -1.0 : 0.0, k == 3 ? 1.0 : k == 4 ? -1.0 : 0.0);
                        float2 slope = slopeAt(uv + off);
                        if (k == 0) slope0 = slope;
                        float2 turn = bendsBy(slope);
                        float w = k == 0 ? 2.0 : 1.0;
                        lines += w;
                        if (turn.x > 500.0) { dark += w; continue; }
                        float2 there = clamp(uv + turn * across, ((float2)(0.002)), ((float2)(0.998)));
                        float3 one = min(SampleLod(_WetScene, there, 0.0).rgb, ((float3)(24.0)));
                        if (k == 0)
                        {
                            one.r = min(SampleLod(_WetScene, clamp(uv + turn * across * 0.975, ((float2)(0.002)), ((float2)(0.998))), 0.0).r, 24.0);
                            one.b = min(SampleLod(_WetScene, clamp(uv + turn * across * 1.03, ((float2)(0.002)), ((float2)(0.998))), 0.0).b, 24.0);
                        }
                        bent += one * w;
                    } }
                    // (where nothing comes through, a little of the light inside the drop; and water takes a little of all of it)
                    bent = (bent + _WetSky.rgb * 0.25 * dark) / max(lines, 1.0) * 0.96;
                    // (what a lens gathers is a little stronger in colour than the scene beside it)
                    bent = max(lerp(((float3)(dot(bent, float3(0.2126, 0.7152, 0.0722)))), bent, 1.12), ((float3)(0.0)));
                    // (the water's edge soft: it is out of focus, and as thin as nothing there)
                    float amount = smoothstep(0.0005, 0.03, water);
                    colour = lerp(colour, bent, amount);
                    // ---- the light in its rim. Light that enters a drop is gathered towards the side away from where it came from
                    // and thrown back out of the rim there: a bright sickle on the side away from the sun, a fainter thread on the
                    // side towards it, and a little all the way round. (This is the rim light by which glass drawn on a screen is
                    // made to read as glass: brightest where the edge faces a light set at an angle, half as much on the far side,
                    // a floor of an eighth all round. Here the light is the sun where it is, and the stronger side is the far one,
                    // as in water.) How near the rim: by how steep the water leans, the rim of a drop standing at 45 degrees.
                    float s0 = length(slope0);
                    float out_ = clamp(s0 / sqrt(1.0 + s0 * s0) / 0.7071, 0.0, 1.0);
                    float2 way = s0 > 1e-4 ? -slope0 / s0 : ((float2)(0.0));
                    float towards = dot(way, _WetSun.xy);
                    float ring = smoothstep(0.6, 0.86, out_) * (1.0 - smoothstep(0.93, 1.0, out_)) * amount;
                    float shines = 0.12 + pow(clamp(-towards, 0.0, 1.0), 1.5) + 0.5 * pow(clamp(towards, 0.0, 1.0), 3.0);
                    colour += _WetSky.rgb * _WetSky.w * (0.25 + 0.75 * _WetSun.z) * 0.3 * ring * shines;
                } }
                // (nothing that is not a number goes into the picture)
                if (!(colour.r + colour.g + colour.b < 1e20)) colour = scene.rgb;
                result = float4(colour, scene.a);
                return result;
            }
            ENDCG
        }
    }
}
