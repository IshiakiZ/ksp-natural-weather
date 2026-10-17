// NaturalWeather/Drops: made from tools/shaderpack/drops.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalWeather/Drops"
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
            ZWrite On
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




            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 vs_TEXCOORD0 : TEXCOORD0;
                float2 vs_TEXCOORD1 : TEXCOORD1;
            };

            // Natural Weather: the drops on the camera's glass, drawn into a picture of how high the water stands on it (Drops.cs).
            // Each drop is a quad laid round its foot on the glass, stretched along its way when it runs or has just merged; the
            // quad's corners carry, in their third number, which corner each is (0 to 3), the drop's foot (mm, in fiftieths) and a
            // seed for the shape of its edge: z = corner + 4 x (foot / 0.02 x 128 + seed).

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                float code = floor(in_POSITION0.z * 0.25 + 0.0625);
                float corner = in_POSITION0.z - 4.0 * code;
                float seed = code - 128.0 * floor(code / 128.0 + 0.0039);
                OUT.vs_TEXCOORD0 = float2(corner > 0.5 && corner < 2.5 ? 1.0 : -1.0, corner > 1.5 ? 1.0 : -1.0);
                OUT.vs_TEXCOORD1 = float2(floor(code / 128.0 + 0.0039) * 0.02, seed / 128.0);
                // (the picture is the glass: x and y are already in its own units, -1 to 1; upside down where a picture drawn into is)
                OUT.pos = float4(in_POSITION0.x, in_POSITION0.y * _ProjectionParams.x, 0.5, 1.0);
                return OUT;
            }

            // The height of the water: a cap of a sphere standing on the glass at 45 degrees (on a plain coated lens a drop meets the
            // glass at forty to fifty), of the drop's foot, its edge not quite round: it catches on the glass here and there, a few
            // shallow lobes by its seed. Where two drops overlap the higher water is kept (they merge the next moment): the depth
            // written is the water's height, nearest the highest.

            float4 frag(v2f IN, out float fragDepth : SV_Depth) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                fragDepth = 0.0;
                float2 at = IN.vs_TEXCOORD0;
                float angle = atan2(at.y, at.x);
                float s = IN.vs_TEXCOORD1.y * 6.2832;
                float edge = 1.0 + 0.045 * sin(3.0 * angle + s) + 0.03 * sin(5.0 * angle + 2.7 * s) + 0.02 * sin(7.0 * angle - 1.3 * s);
                float q = length(at) / edge;
                if (q >= 1.0) discard;
                float foot = max(IN.vs_TEXCOORD1.x, 0.02);
                // (a sphere of radius foot / sin 45, cut where it stands 45 degrees to the glass)
                float R = foot * 1.41421;
                float rho = q * foot;
                float h = sqrt(max(R * R - rho * rho, 0.0)) - R * 0.70711;
                fragDepth = clamp(1.0 - h / 16.0, 0.0, 1.0);
                result = float4(max(h, 0.0), 0.0, 0.0, 1.0);
                return result;
            }
            ENDCG
        }
    }
}
