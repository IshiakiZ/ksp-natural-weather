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
            float4 _CloudOver;     // x: the power that turns light into shown colour (1/2.2); y: how much of the clouds to show (0 to 1); z: how much brighter (the eye); w: 1 where this camera's veil of weather (Natural Light's) was laid on before the clouds, and they take it here: Camera 00's (the only camera on Direct3D; on OpenGL the near one, whose clouds are laid on within some hundreds of metres of their shell); 0 for Camera 01's, which its own veil covers after them
            float4 _CloudVeilUp;     // xyz: up, at the camera, in the scene; w: the world's radius (m)
            float4 _WeatherVeilAir;     // the weather in the air (Natural Light's veil, handed to every shader): x how thick (the light lost in each metre; 0: none); y its top, above the camera (m); z how soon it thins out above its top (m)
            float4 _WeatherVeilColour;     // rgb: its own light, lit from all round (as it shows)
            float4 _WeatherVeilSun;     // xyz: towards the sun, in the scene; w: how strongly what is in the air throws light forward
            float4 _WeatherVeilSunColour;     // rgb: what the sun adds to it looking straight towards the sun (as it shows); a: how much of the sunlight its top throws back up
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
                // Light's veil and the sea's lens in the water the same; and Cinema Camera's look, whose picture came out upside down on
                // Direct3D without it, the camera drawing into a picture of its own there (the PC, 2026-10-20))
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

            // The weather along a stretch of line that lies above the weather's top, from a to b metres out, counted in metres of the
            // weather as thick as it is under its top: the veil's own sum (veil.glsl, which says why it is summed so; three times wrong
            // before, 2026-10-25). On a round world the line's height over the camera after s metres is mu s + s^2 / 2R, lowest at
            // s = -mu R: six pieces bunched towards that lowest point from the end it is at, or six each side of it where it is inside,
            // each straight between the line's own heights over the top at its two ends (both at least 0, so every power of e is at
            // most 1), the weather summed exactly along each.
            float above(float a, float b, float mu, float top, float soft, float R)
            {
                float low = clamp(-mu * R, a, b);
                bool both = low > a && low < b;
                float hLow = max(mu * low + low * low / (2.0 * R) - top, 0.0), eLow = exp(-hLow / soft);
                float sum = 0.0, sWas = low, hWas = hLow, eWas = eLow;
                { [loop] for (int i = 1; i <= 12; i++)
                {
                    float to, t;
                    if (both)
                    {
                        to = i <= 6 ? a : b;
                        t = float(i <= 6 ? i : i - 6) / 6.0;
                        if (i == 7) { sWas = low; hWas = hLow; eWas = eLow; }
                    }
                    else
                    {
                        if (i > 6) break;
                        to = low <= a ? b : a;
                        t = float(i) / 6.0;
                    }
                    float s = low + (to - low) * t * t;
                    float h = max(mu * s + s * s / (2.0 * R) - top, 0.0);
                    float e = exp(-h / soft);
                    float len = abs(s - sWas);
                    float k = (h - hWas) / soft;
                    sum += abs(k) < 0.001 ? eWas * len * (1.0 - 0.5 * k) : len * (eWas - e) / k;
                    sWas = s; hWas = h; eWas = e;
                } }
                return sum;
            }

            // Interleaved gradient noise (Jimenez 2014): 0 to 1, a different value at each pixel of the screen, the same every frame.
            float dither(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }

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
                float weights = 0.0, farSum = 0.0, hiding = 0.0;
                { [loop] for (int j = 0; j < 2; j++)
                    { [loop] for (int i = 0; i < 2; i++)
                    {
                        float2 tap = (corner + float2(float(i), float(j)) + 0.5) / size;
                        float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y) + 1e-4;
                        float4 c = SampleLod(_CloudHist, tap, 0.0);
                        float d = SampleLod(_CloudHistD, tap, 0.0).r * 1000.0;     // (kept in kilometres)
                        // (a cloud whose light comes from beyond what is drawn here is hidden by it)
                        if (d > 0.0 && d > scene * 1.03 + 30.0) c = float4(0.0, 0.0, 0.0, 1.0);
                        sum += c * w;
                        weights += w;
                        // (and how far off the cloud here is, each of the four by how much of it it hides)
                        if (d > 0.0)
                        {
                            float hides = max(1.0 - c.a, 0.0) * w;
                            farSum += d * hides;
                            hiding += hides;
                        }
                    } } }
                float4 cloud = sum / weights;
                float cover = clamp((1.0 - cloud.a) * _CloudOver.y, 0.0, 1.0);
                if (cover < 0.002) discard;
                float3 colour = max(cloud.rgb, ((float3)(0.0))) / max(1.0 - cloud.a, 1e-3) * _CloudOver.z;
                // (a soft shoulder above 0.7: the far camera's picture holds nothing brighter than white, and a sunlit top cut off flat
                // there loses all its modelling; rolled over, its brighter and dimmer billows still show)
                // (Thin cloud that is brighter than white: a veil of ice or a heap's edge near the line to the sun, which throw the
                // sun's light on towards the eye. Laid over the picture as a colour, as all cloud was until 2026-10-13, the brightest
                // a thin cloud could make a pixel was white times the little it hides: a veil in front of the sun was milk, with no
                // glare round the sun and nothing of the sun's ring. What it has beyond white is added to the picture instead, where
                // the cloud is thin (a thick cloud's sunlit top is a surface, and keeps the shoulder below).)
                float3 beyond = max(colour - 1.0, ((float3)(0.0))) * (0.7 * cover * (1.0 - cover) * (1.0 - cover));
                float3 over = max(colour - 0.7, ((float3)(0.0)));
                colour = min(colour, ((float3)(0.7))) + 0.3 * (1.0 - exp(-over / 0.3));
                colour = pow(colour, ((float3)(_CloudOver.x)));
                // ---- the weather in the air between the eye and the cloud: rain, snow, fog, dust (Natural Light's veil: its numbers, and
                // the same sums as its own pass, veil.glsl), where this camera's veil was laid on before the clouds (_CloudOver.w). That
                // pass has taken what lies behind the cloud already, so the cloud's own colour is veiled by its own distance and laid on
                // by as much as it hides, as before: which is exactly what the veil over the whole line comes to. (Until 2026-10-25 they
                // were laid on over Camera 00's veil untouched: in 280 m of fog on the pad the ground and the sky were white and the
                // heaps over them crisp and blue, on Direct3D, where that camera is the only one.)
                if (_CloudOver.w > 0.5 && _WeatherVeilAir.x > 0.0 && hiding > 0.0)
                {
                    // (no further than the veil takes the sky behind it: Natural Light's Veil.cs)
                    float far = min(farSum / hiding, 60000.0);
                    float mu = dot(dir, _CloudVeilUp.xyz);
                    float top = _WeatherVeilAir.y;
                    float soft = max(_WeatherVeilAir.z, 1.0);
                    float R = max(_CloudVeilUp.w, 1000.0);
                    // (on a round world the weather's top curves away under the line of sight: veil.glsl)
                    float meets = mu * mu + 2.0 * top / R;
                    float tau, inLayer = 0.0;
                    if (top > 0.0)
                    {
                        // ---- the camera in the weather: all of it until the line rises out through the top, then thinning out
                        float out1 = R * (-mu + sqrt(max(meets, 0.0)));
                        if (far <= out1) tau = far;
                        else tau = out1 + above(out1, far, mu, top, soft, R);
                    }
                    else if (mu >= 0.0 || meets <= 0.0)
                    {
                        // ---- the camera above its top, and the line never comes down to it
                        tau = above(0.0, far, mu, top, soft, R);
                    }
                    else
                    {
                        // ---- the camera above its top, the line down into the weather and (if it goes on far enough) out of it again
                        float in1 = R * (-mu - sqrt(meets)), out1 = R * (-mu + sqrt(meets));
                        if (far <= in1) tau = above(0.0, far, mu, top, soft, R);
                        else { inLayer = min(far, out1) - in1; tau = above(0.0, in1, mu, top, soft, R) + inLayer + (far > out1 ? above(out1, far, mu, top, soft, R) : 0.0); }
                    }
                    float veiled = 1.0 - exp(-_WeatherVeilAir.x * tau);
                    // (its light: from all round, and towards the sun more, by a Henyey-Greenstein lobe made 1 straight towards the sun;
                    // and from over its top, the sunlight it throws back up, by as much of what is veiled as lies under its top)
                    float g = clamp(_WeatherVeilSun.w, 0.0, 0.95);
                    float c = dot(dir, _WeatherVeilSun.xyz);
                    float lobe = (1.0 - g) * (1.0 - g) / pow(max(1.0 + g * g - 2.0 * g * c, 0.0001), 1.5) * (1.0 - g * g) / (1.0 + g);
                    float lid = 3.0 * _WeatherVeilSunColour.a * inLayer / max(tau, 1e-3);
                    colour = colour * (1.0 - veiled) + (_WeatherVeilColour.rgb + _WeatherVeilSunColour.rgb * (lobe + lid)) * veiled;
                    beyond *= 1.0 - veiled;
                }
                // (and dithered by half a level of 255 each way, by how much the cloud hides: the picture is kept in 255 levels, and a glow
                // fading through cloud over hundreds of pixels came out in rings a level apart (the side-by-side's A8, deck_rain). Fixed to
                // the screen, the same every frame, so that nothing in it flickers.)
                colour += (dither(FragCoord(IN.pos).xy) - 0.5) / 255.0;
                result = float4(colour * cover + beyond, cover);
                return result;
            }
            ENDCG
        }
    }
}
