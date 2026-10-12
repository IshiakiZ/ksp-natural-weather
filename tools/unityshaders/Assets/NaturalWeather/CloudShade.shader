// NaturalWeather/CloudShade: made from tools/shaderpack/cloudshade.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalWeather/CloudShade"
{
    Properties
    {
        _CloudShadowMap ("CloudShadowMap", 2D) = "" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend DstColor SrcColor
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

            float4 _CloudSheet;     // xy: 1 (the sheet as large as the screen), z: its depth, w: 1
            float4 _CloudRight;     // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
            float4 _CloudUp;     // xyz: the camera's up, times the tangent of half its height of view
            float4 _CloudAhead;     // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
            float4 _CloudCam;     // xyz: the camera, from the planet's centre (m, the scene's axes); w: the planet's radius (m)
            float4 _CloudSun;     // xyz: towards the sun (the scene's axes)
            float4 _CloudShadeAt;     // xyz: the middle of the shadow's square, from the planet's centre (the scene's axes, m); w: its side (m)
            float4 _CloudShadeX;     // xyz: the square's two sides, in the scene's axes; w (X): the height of the ground it lies on (m above the radius)
            float4 _CloudShadeY;
            float4 _CloudShade;     // x: how much brighter the sun makes what faces it straight on than the sky alone (about 1.7 by day); y: how far off the pattern has faded to half (m); z: the least the camera draws (m); w: the most
            float4 _CloudShadeSun;     // x: what the sun was dimmed by for the whole scene (the cloud above the craft), 0 to 1; y: 1 if the world has a sea
            Texture2D _CloudShadowMap; SamplerState sampler_CloudShadowMap;     // r: how much cloud lies towards the sun; g, b: the lowest and highest of it (km above the radius)
            Texture2D _CameraDepthTexture; SamplerState sampler_CameraDepthTexture;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 vs_TEXCOORD0 : TEXCOORD0;
            };

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                OUT.pos = float4(in_POSITION0.xy * _CloudSheet.xy, _CloudSheet.z, _CloudSheet.w);
                // (drawn into whatever the camera is drawing into, which may lie either way: as for the lens's second go, above; Natural
                // Light's veil and the sea's lens in the water the same)
                float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
                way = _ProjectionParams.x;
#endif
                OUT.vs_TEXCOORD0 = float2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);
                return OUT;
            }

            // The clouds' shadow, laid on everything the camera has drawn (Natural Weather's Clouds.cs; the shadow itself was worked
            // out by clouds.glsl, from points on the ground round the camera, as how much cloud lies towards the sun and between what
            // heights). The scene was drawn with the sun already dimmed by the cloud above the craft. For each pixel the place drawn
            // there is found from its depth, followed along the sun's line down to that ground, and its light put right for the cloud
            // above it instead: darker in a shadow the craft is not in, lighter in a gap of sunlight when the craft is in shade, each by
            // the sun's share of the light on that face (none for a face turned from the sun). Further off, the pattern fades into the
            // craft's own light, under the air's.

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float2 uv = IN.vs_TEXCOORD0;
                if (_CloudRight.w < 0.5) discard;
                float z = SampleLod(_CameraDepthTexture, uv, 0.0).r;
                bool nothing = _CloudAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
                if (nothing) discard;
                float3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
                float t = 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w) / max(dot(dir, _CloudAhead.xyz), 1e-4);
                // (the sea is drawn by Scatterer after the depth picture is made, which holds the seabed under it: where the line of sight
                // meets the sea's surface first, that is what is shaded)
                if (_CloudShadeSun.y > 0.5)
                {
                    float b = dot(_CloudCam.xyz, dir), c = dot(_CloudCam.xyz, _CloudCam.xyz) - _CloudCam.w * _CloudCam.w;
                    float disc = b * b - c;
                    if (disc > 0.0 && c > 0.0)
                    {
                        float sea = -b - sqrt(disc);
                        if (sea > 0.0 && sea < t) t = sea;
                    }
                }
                if (t < _CloudShade.z || t > _CloudShade.w) discard;
                float3 near = dir * t;                       // from the camera (small numbers: their differences are clean)
                float3 p = _CloudCam.xyz + near;             // from the planet's centre
                float R = _CloudCam.w;
                float r = length(p);
                float3 up = p / max(r, 1.0);
                float3 sun = _CloudSun.xyz;
                float sunUp = dot(up, sun);
                if (sunUp < -0.03) discard;
                // Which way the surface faces (from how the place changes from one pixel to the next, turned to face the camera).
                float3 facing = cross(ddx(near), ddy(near));
                float size = length(facing);
                facing = size > 1e-10 ? facing / size : -dir;
                if (dot(facing, dir) > 0.0) facing = -facing;
                float ndl = dot(facing, sun);
                if (ndl <= 0.0) discard;
                // Down the sun's line to the ground the shadow was worked out on.
                float h = r - R;
                float3 g = p - sun * ((h - _CloudShadeX.w) / max(sunUp, 0.06));
                float3 rel = g - _CloudShadeAt.xyz;
                float2 st = float2(dot(rel, _CloudShadeX.xyz), dot(rel, _CloudShadeY.xyz)) / _CloudShadeAt.w + 0.5;
                if (st.x <= 0.0 || st.x >= 1.0 || st.y <= 0.0 || st.y >= 1.0) discard;
                float4 m = SampleLod(_CloudShadowMap, st, 0.0);
                float lowest = m.g * 1000.0, highest = m.b * 1000.0;
                // (only the cloud above this place takes its sun: a craft flying between two layers is shaded by the upper one alone)
                float above = m.r * clamp((highest - h) / max(highest - lowest, 60.0), 0.0, 1.0);
                // (the edge of the square fades, and so does a far shadow, into the light the craft is in)
                float2 edge = min(st, 1.0 - st);
                float fade = clamp(min(edge.x, edge.y) * 12.0, 0.0, 1.0) * _CloudShade.y / (_CloudShade.y + t);
                float through = lerp(_CloudShadeSun.x, exp(-above), fade);
                // The sun's share of the light on this face, as it would be undimmed; the light as drawn, and as it should be.
                float k = _CloudShade.x * ndl;
                float share = k / (k + 1.0);
                float should = 1.0 - share + share * through, drawn = 1.0 - share + share * _CloudShadeSun.x;
                float factor = clamp(should / max(drawn, 0.05), 0.0, 2.0);
                if (!(abs(factor - 1.0) > 0.004)) discard;
                // (drawn doubled: what is there is multiplied by twice this, so it can be lightened as well as darkened)
                result = float4(((float3)(factor * 0.5)), 1.0);
                return result;
            }
            ENDCG
        }
    }
}
