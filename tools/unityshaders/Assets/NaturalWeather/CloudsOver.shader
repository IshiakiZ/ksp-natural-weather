// NaturalWeather/CloudsOver: made from tools/shaderpack/cloudsover.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalWeather/CloudsOver"
{
    Properties
    {
        _CloudHist ("CloudHist", 2D) = "" {}
        _CloudHistD ("CloudHistD", 2D) = "" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
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
            float4 _CloudOver;     // x: the power that turns light into shown colour (1/2.2); y: how much of the clouds to show (0 to 1); z: how much brighter (the eye); w: unused
            Texture2D _CloudHist; SamplerState sampler_CloudHist;     // the clouds, small: their light, and how much shows through them
            Texture2D _CloudHistD; SamplerState sampler_CloudHistD;     // and how far off each is
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

            // The clouds put on the picture (Natural Weather's Clouds.cs; clouds.glsl drew them, small, and took them together over
            // frames). Each pixel takes the four nearest of the small picture, leaving out any whose cloud lies behind what is drawn at
            // this pixel (a mountain, a building, the craft): so a ridge in front of a cloud keeps its edge. The colour is worked out
            // as light and turned into the game's own way of showing colours at the end, and laid over what is there by how much of it
            // the cloud hides.

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float2 uv = IN.vs_TEXCOORD0;
                float3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
                float scene = 1e9;
                if (_CloudRight.w > 0.5)
                {
                    float z = SampleLod(_CameraDepthTexture, uv, 0.0).r;
                    bool nothing = _CloudAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
                    if (!nothing) scene = 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w) / max(dot(dir, _CloudAhead.xyz), 1e-4);
                }
                float2 size = ((float2)(TextureSize(_CloudHist, 0)));
                float2 at = uv * size - 0.5;
                float2 f = frac(at);
                float2 corner = floor(at);
                float4 sum = ((float4)(0.0));
                float weights = 0.0;
                [loop] for (int j = 0; j < 2; j++)
                    [loop] for (int i = 0; i < 2; i++)
                    {
                        float2 tap = (corner + float2(float(i), float(j)) + 0.5) / size;
                        float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y) + 1e-4;
                        float4 c = SampleLod(_CloudHist, tap, 0.0);
                        float d = SampleLod(_CloudHistD, tap, 0.0).r * 1000.0;     // (kept in kilometres)
                        // (a cloud whose light comes from beyond what is drawn here is hidden by it)
                        if (d > 0.0 && d > scene * 1.03 + 30.0) c = float4(0.0, 0.0, 0.0, 1.0);
                        sum += c * w;
                        weights += w;
                    }
                float4 cloud = sum / weights;
                float cover = clamp((1.0 - cloud.a) * _CloudOver.y, 0.0, 1.0);
                if (cover < 0.002) discard;
                float3 colour = max(cloud.rgb, ((float3)(0.0))) / max(1.0 - cloud.a, 1e-3) * _CloudOver.z;
                // (a soft shoulder above 0.7: the far camera's picture holds nothing brighter than white, and a sunlit top cut off flat
                // there loses all its modelling; rolled over, its brighter and dimmer billows still show)
                float3 over = max(colour - 0.7, ((float3)(0.0)));
                colour = min(colour, ((float3)(0.7))) + 0.3 * (1.0 - exp(-over / 0.3));
                colour = pow(colour, ((float3)(_CloudOver.x)));
                result = float4(colour * cover, cover);
                return result;
            }
            ENDCG
        }
    }
}
