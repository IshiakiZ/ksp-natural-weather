// NaturalWeather/Snow: made from tools/shaderpack/snow.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalWeather/Snow"
{
    Properties
    {

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

            float4 _SnowRight;     // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
            float4 _SnowUp;     // xyz: the camera's up, times the tangent of half its height of view
            float4 _SnowAhead;     // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
            float4 _SnowCam;     // xyz: the camera, from the planet's centre (m, the scene's axes); w: the planet's radius (m)
            float4 _SnowHere;     // xy: where the camera is over the ground, in metres along two level ways fixed to the ground (kept small); z: 1 to draw the light measured, 2 why each pixel has no snow (development); w: 1 if the world has a sea
            float4 _SnowAcross;     // xyz: the first of those level ways (the scene's axes, a unit)
            float4 _SnowAlong;     // xyz: the second
            float4 _SnowDeep0;     // the depth of the snow, cm, at the first four heights
            float4 _SnowDeep1;     // and at the next four
            float4 _SnowBands;     // x: the first height (m above the sea); y: metres between heights; z: how far off the snow is drawn (m); w: how many craft boxes are given
            float4 _SnowSun;     // xyz: towards the sun (a unit); w: how much of its light reaches the ground (0 at night)
            float4 _SnowSunLight;     // rgb: the sunlight on a white face turned to it, as the picture has it
            float4 _SnowSkyLight;     // rgb: the sky's light on a white face turned up, as the picture has it
            float4 _SnowBox0X;     // the box round a craft: from a place (from the camera) to the box's own measures, its inside -1 to 1 each way: across,
            float4 _SnowBox0Y;     // up and along, each as so much of the place's three measures and a constant (the rows of the change)
            float4 _SnowBox0Z;
            float4 _SnowBox1X;     // and the next three craft's
            float4 _SnowBox1Y;
            float4 _SnowBox1Z;
            float4 _SnowBox2X;
            float4 _SnowBox2Y;
            float4 _SnowBox2Z;
            float4 _SnowBox3X;
            float4 _SnowBox3Y;
            float4 _SnowBox3Z;
            Texture2D _SnowScene; SamplerState sampler_SnowScene;     // the picture so far
            Texture2D _CameraDepthTexture; SamplerState sampler_CameraDepthTexture;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 vs_TEXCOORD0 : TEXCOORD0;
            };

            // Natural Weather: lying snow (Snow.cs). One triangle over the whole picture.

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

            // Snow lying on the ground, laid over the picture of the ground as the game drew it, before anything in the air (clouds,
            // smoke, rain, fog) is drawn over it, on the near camera's picture and the far one's alike.
            //
            // How much lies. Worked out in Snow.cs for a few heights above the sea round the camera's place, from the last days'
            // weather there: what fell as snow piles up, the warmth melts it (by how far above freezing, hour by hour). Here each
            // pixel takes the depth for its own height, so the snow starts at a line on the hills and lies deeper above it.
            //
            // Where it lies. On what faces up: it slides off what is steeper than about fifty-five degrees (rock faces and cliffs
            // stand dark among the white, as in any photograph of snowy mountains), and lies thinner on what is steeper than about
            // thirty-five. Not on the sea, not on the craft (their boxes are given), not on the sky. Thin snow lies in patches: grass,
            // tufts and stones show through until it is deeper than they stand (a few centimetres), the hollows filling first; at ten
            // centimetres the ground is white.
            //
            // How it looks. Snow throws back four fifths of the light (fresh snow 0.8 to 0.9; grass about a fifth, bare rock and
            // concrete about a third). The light at each point is known from the picture itself: what the game drew there is the
            // ground's own colour lit by that light, shadows and the clouds' shade included; so the snow is that light times the
            // snow's whiteness. Where the sun is on it, it takes the sun's colour; in shadow, the sky's (snow in shade is blue). And
            // it is never brighter than white in the full light there.

            float chance(float2 p) { float3 q = frac(((float3)(p.xyx)) * float3(0.1031, 0.1030, 0.0973)); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }

            // Smooth noise over the ground, 0 to 1.
            float ground2(float2 p)
            {
                float2 i = floor(p), f = p - i;
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(chance(i), chance(i + float2(1.0, 0.0)), f.x), lerp(chance(i + float2(0.0, 1.0)), chance(i + float2(1.0, 1.0)), f.x), f.y);
            }

            bool inside(float4 x, float4 y, float4 z, float3 p)
            {
                float4 q = float4(p, 1.0);
                return max(abs(dot(x, q)), max(abs(dot(y, q)), abs(dot(z, q)))) < 1.0;
            }

            float luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float2 uv = IN.vs_TEXCOORD0;
                float4 scene = SampleLod(_SnowScene, uv, 0.0);
                float3 colour = scene.rgb;
                // (the place drawn at each pixel, and which way the surface faces there, from how the place changes from one pixel to
                // the next: worked out for every pixel before anything is decided by them, as a card's own differences ask)
                float z = SampleLod(_CameraDepthTexture, uv, 0.0).r;
                bool nothing = _SnowAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
                float3 dir = normalize(_SnowAhead.xyz + _SnowRight.xyz * (uv.x * 2.0 - 1.0) + _SnowUp.xyz * (uv.y * 2.0 - 1.0));
                float along = nothing ? 1e7 : 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w);   // (how far along the way the camera looks)
                float t = along / max(dot(dir, _SnowAhead.xyz), 1e-4);
                float3 near = dir * t;
                float3 facing = cross(ddx(near), ddy(near));
                // (development: each pixel coloured by why it has no snow: blue the sea, red too steep, yellow too thin, magenta a craft,
                // cyan past the reach, grey white where it has some)
                bool why = _SnowHere.z > 1.5;
                float3 because = colour * 0.3;
                { [loop] for (int once = _SnowRight.w > 0.5 ? 0 : 1; once < 1; once++)
                {
                    if (nothing) break;
                    // (its reach by how far along the way the camera looks, as the camera's own sight is: by the distance along each
                    // pixel's line, the slanting lines at the picture's sides went past it short of the near camera's end, and the
                    // ground there stayed bare in a strip where the far camera takes over)
                    if (along > _SnowBands.z) { because = float3(0.0, 1.0, 1.0); break; }
                    // (not on the sea: where the line of sight meets the sea's surface first, it is the sea that is seen)
                    if (_SnowHere.w > 0.5)
                    {
                        float bb = dot(_SnowCam.xyz, dir), cc = dot(_SnowCam.xyz, _SnowCam.xyz) - _SnowCam.w * _SnowCam.w;
                        float disc = bb * bb - cc;
                        if (disc > 0.0 && cc > 0.0 && -bb - sqrt(disc) > 0.0 && -bb - sqrt(disc) < t) { because = float3(0.0, 0.0, 1.0); break; }
                    }
                    // (not on a craft)
                    int boxes = int(_SnowBands.w + 0.5);
                    bool craft = (boxes > 0 && inside(_SnowBox0X, _SnowBox0Y, _SnowBox0Z, near)) || (boxes > 1 && inside(_SnowBox1X, _SnowBox1Y, _SnowBox1Z, near))
                              || (boxes > 2 && inside(_SnowBox2X, _SnowBox2Y, _SnowBox2Z, near)) || (boxes > 3 && inside(_SnowBox3X, _SnowBox3Y, _SnowBox3Z, near));
                    if (craft) { because = float3(1.0, 0.0, 1.0); break; }
                    float3 fromCentre = _SnowCam.xyz + near;
                    float r = length(fromCentre);
                    float3 up = fromCentre / r;
                    // ---- how deep it lies at this height
                    float k = clamp((r - _SnowCam.w - _SnowBands.x) / max(_SnowBands.y, 1.0), 0.0, 6.999);
                    int band = int(k);
                    float f = k - float(band);
                    float deepA = band < 4 ? Pick(_SnowDeep0, band) : Pick(_SnowDeep1, band - 4);
                    float deepB = band < 3 ? Pick(_SnowDeep0, band + 1) : Pick(_SnowDeep1, band - 3);
                    float deep = lerp(deepA, deepB, f);
                    if (deep < 0.05) { because = float3(1.0, 1.0, 0.0); break; }
                    // ---- on what faces up
                    float size = length(facing);
                    facing = size > 1e-10 ? facing / size : up;
                    if (dot(facing, dir) > 0.0) facing = -facing;
                    float level = dot(facing, up);
                    float holds = smoothstep(0.55, 0.82, level + 0.004 * min(deep, 40.0));
                    if (holds <= 0.0) { because = float3(1.0, 0.0, 0.0); break; }
                    // ---- thin, it is a veil: the blades and stones of the ground (a few centimetres) stand through it until it is deeper
                    // than they are, so a dusting is the ground paled, not white spots on it; the hollows (metres across) fill first and
                    // the bumps last, so it is patchy over metres; at ten centimetres or so, white all over. Fixed to the ground. Far
                    // off, where the patches are smaller than a pixel, how much of the ground is white on the whole.
                    float2 over = _SnowHere.xy + float2(dot(near, _SnowAcross.xyz), dot(near, _SnowAlong.xyz));
                    float lumps = 0.6 * ground2(over * 0.35 + 7.0) + 0.4 * ground2(over * 0.11 + 31.0);
                    float pixel = t * length(_SnowUp.xyz) / 300.0;                      // (metres a pixel covers there, about)
                    float fine = smoothstep(12.0, 3.0, pixel);
                    float stands = lerp(4.5, 1.5 + 7.0 * lumps, fine);                   // (cm: how deep it must be to bury what stands here)
                    float cover = smoothstep(0.0, 1.0, deep / stands);
                    cover = cover * cover * (3.0 - 2.0 * cover) * holds;
                    cover *= smoothstep(1.0, 0.92, along / max(_SnowBands.z, 1.0));
                    if (cover <= 0.002) { because = float3(1.0, 1.0, 0.0); break; }
                    // ---- the light here, from the picture: the ground's own colour taken out (grass, by its green, about a fifth of
                    // the light; anything else about a third); how much of it is the sky's, the sun's where it shines (none in shadow),
                    // and by night what is more than that, the lamps'
                    float green = smoothstep(0.03, 0.2, (colour.g - max(colour.r, colour.b)) / max(colour.g, 1e-3));
                    float lit_ = luma(colour) / lerp(0.3, 0.18, green);
                    float sunOn = max(dot(facing, _SnowSun.xyz), 0.0) * _SnowSun.w;
                    float3 sky = _SnowSkyLight.rgb * (0.55 + 0.45 * level);
                    float3 sun = _SnowSunLight.rgb * sunOn;
                    float skyL = max(luma(sky), 1e-4), sunL = luma(sun);
                    float day = smoothstep(0.05, 0.3, _SnowSun.w);
                    // (by day: the sun and the sky, the picture saying only where the sun does not reach, which a ground darker or
                    // lighter than taken cannot then make too dark or too bright)
                    float inSun = sunL > 1e-4 ? clamp((lit_ - skyL) / sunL, 0.0, 1.0) : 0.0;
                    float3 byDay = sky + sun * inSun;
                    // (by night: the light as the picture has it, its colour too: the ground's own colour taken out of the picture's (a
                    // grass's green, or a grey's), what is left the light's own, the moon's and the sky's or a lamp's, held to gentle
                    // tints. Until 2026-10-19 the light more than the sky's was taken for a lamp's: the game lights the ground by night far
                    // more than its sky's light says, and all the snow came out sepia)
                    float3 groundColour = lerp(((float3)(0.3)), float3(0.147, 0.202, 0.064), green);
                    float3 nightLight = colour / max(groundColour, ((float3)(0.02)));
                    float3 byNight = lerp(((float3)(luma(nightLight))), nightLight, 0.6);
                    // (its surface a little uneven, as wind and settling leave it: a few hundredths of brightness over metres)
                    float grain = 0.94 + 0.08 * ground2(over * 0.6 + 3.0) + 0.04 * ground2(over * 2.3 + 11.0);
                    float3 snow = 0.82 * lerp(byNight, byDay, day) * grain;
                    // (development: the light the picture says against the light expected, drawn as grey, a half for the two the same)
                    if (_SnowHere.z > 0.5 && !why) { snow = ((float3)(0.5 * lit_ / max(skyL + sunL, 1e-4))); cover = 1.0; }
                    because = ((float3)(0.5 + 0.5 * cover));
                    colour = lerp(colour, snow, cover);
                } }
                if (why) colour = because;
                result = float4(colour, scene.a);
                return result;
            }
            ENDCG
        }
    }
}
