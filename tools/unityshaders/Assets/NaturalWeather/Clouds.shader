// NaturalWeather/Clouds: made from tools/shaderpack/clouds.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalWeather/Clouds"
{
    Properties
    {
        _CloudField ("CloudField", 3D) = "" {}
        _CloudShape ("CloudShape", 3D) = "" {}
        _CloudDetail ("CloudDetail", 3D) = "" {}
        _CloudMap ("CloudMap", 2D) = "" {}
        _CloudMapB ("CloudMapB", 2D) = "" {}
        _CloudMapC ("CloudMapC", 2D) = "" {}
        _CloudAirLut ("CloudAirLut", 2D) = "" {}
        _CloudNow ("CloudNow", 2D) = "" {}
        _CloudNowD ("CloudNowD", 2D) = "" {}
        _CloudHist ("CloudHist", 2D) = "" {}
        _CloudHistD ("CloudHistD", 2D) = "" {}
        _CameraDepthTexture ("CameraDepthTexture", 2D) = "" {}
        _CloudCells ("CloudCells", 2D) = "" {}
        _CloudHeaps ("CloudHeaps", 2D) = "" {}
        _CloudHeapsB ("CloudHeapsB", 2D) = "" {}
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
            float4 _CloudStage;     // x: which go (0, 1: the noise; 2: the walk; 3: over frames; 4: the shadow; 5: the map of the lattice's heaps); y: the slice (noise) or the frame's place in the step (walk); z: where the walk may begin (m from the camera); w: where it must end (m), 0: no end but the shell's
            float4 _CloudCam;     // xyz: the camera, from the planet's centre (m, the scene's axes); w: the planet's radius (m)
            float4 _CloudHigh;     // x: the camera's height above the radius (m, worked out in double precision); y: the top of the air (m above the radius); z: the air's scale height (m); w: the picture's width in pixels (of what is drawn into)
            float4 _CloudRight;     // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
            float4 _CloudUp;     // xyz: the camera's up, times the tangent of half its height of view
            float4 _CloudAhead;     // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
            float4 _CloudFixedX;     // the planet's own axes, in the scene's: x towards latitude 0 longitude 0, y towards longitude 90, z the north pole
            float4 _CloudFixedY;
            float4 _CloudFixedZ;
            float4 _CloudNoise;     // xyz: where the camera is in the noise (in lengths of the large noise; wrapped round only every 207 of them), w: metres to a length of the large noise
            float4 _CloudDrift;     // xyz: which way the wind blows the clouds, in the planet's axes (a unit); w: how far the small noise has turned over, in its own lengths (it boils slowly)
            float4 _CloudShell;     // x: the lowest a cloud's base can be (m above the radius); y: the highest a top can be; z: the high cloud's height; w: the high cloud's amount, as set
            float4 _CloudShellEased;     // x, y: the shell's floor and top as they are let follow _CloudShell's over seconds (Clouds.cs): what the steps' lengths and the distance inside the shell go by, so that a floor or top that jumps does not move them
            float4 _CloudLook;     // x: how thick cloud is (light lost per metre in the thickest); y: how much the small noise eats the edges; z: more or less cloud than the weather says (1 as it says); w: how many steps at most
            float4 _CloudSun;     // xyz: towards the sun (the scene's axes); w: the sun's brightness above the air (as light)
            float4 _CloudSunTint;     // rgb: the sun's colour above the air; w: 1 if the moon or another world lights the night (its strength)
            float4 _CloudMoon;     // xyz: towards the brightest other world in the sky (the Mun over Kerbin; the scene's axes); w: its light on a veil of ice by night (nothing by day)
            float4 _CloudSky;     // rgb: the sky's light from above, where the sun is high (as light); w: how blue the sky's light is kept in shade
            float4 _CloudGround;     // rgb: the light the ground throws up (as light); w: the high cloud's thickness (m)
            float4 _CloudAir;     // rgb: how much a metre of air at the bottom scatters (each colour); w: the same for haze
            float4 _CloudAirHigh;     // x: the height over which the air's own scattering falls by e (m); y: the haze's (lower: it lies in the lowest kilometre or two); z: 1 if the world has a sea
            float4 _CloudHaze;     // x: 1 if the noise has smaller copies to read far off (0: only the full one); y: the shortest step (m); z: how much more light the air adds than one scattering gives (light scattered again and again); w: how strongly distance fades the clouds (1 as worked out)
            float4 _CloudFlashA;     // xyz: a flash of lightning, from the camera (m, the scene's axes); w: its brightness (0: none)
            float4 _CloudFlashB;
            float4 _CloudFlashTint;     // rgb: the flash's colour; w: how far its light spreads in cloud (m)
            float4 _CloudHoleA;     // xyz: a heap made as smoke is made (Heaps.cs), the middle of its base from the camera (m, the scene's axes); w: how far round it the noise's own heaps are cleared (m; 0: none)
            float4 _CloudHoleB;
            float4 _CloudHoleC;
            float4 _CloudPrevAt;     // xyz: where the camera is from where it was last frame (m, the planet's axes); w: 1 if last frame's picture can be used
            float4 _CloudPrevRight;     // last frame's camera, in the planet's axes: right over the tangent of half the width of view; w: how much of this frame is taken (0 to 1)
            float4 _CloudPrevUp;     // up over the tangent of half the height of view
            float4 _CloudPrevAhead;     // the way it looked
            float4 _CloudShadowAt;     // xyz: the middle of the shadow's square, from the planet's centre (the planet's axes, m); w: its side (m)
            float4 _CloudShadowX;     // xyz: the square's two sides, in the planet's axes; w: the height of the ground it lies on (m above the radius)
            float4 _CloudShadowY;
            float4 _CloudSunFixed;     // xyz: towards the sun, in the planet's axes
            float4 _CloudStreets;     // xyz: across the wind, level, in the planet's axes (a unit; changed only when the wind has turned well round); w: how strongly heaps line up in streets along the wind (0 to 1)
            float4 _CloudAlong;     // xyz: along the wind, the same way (for the high cloud's fibres)
            float4 _CloudFieldA;     // the heaps made beforehand (see _CloudField). x: repeats of the field per metre; y: metres over which its number runs from none to all; zw: how far the wind has carried it east and north, in repeats
            float4 _CloudFieldB;     // x: 1 if there is such a field (0: the heaps are carved from the noise, as before 0.3.0); yz: how many metres east and north the top of the layer leans with the wind from its base; w: how many metres the small lumps stand proud of the creases between them
            float4 _CloudHabit;     // how the heaps come today. x: how much the small heaps that stand alone are left out (0: all of them there, a sky of many small puffs, which is a rare day; 1: the usual sky, fewer and larger); y: how many times larger over the ground the field is read than the layer's depth alone would have it (1; 1.6 on the usual day: broader, flatter heaps); z: how far a heap that reaches the layer's top spreads out under it, m (the lid: see layerAt); w: how far the heaps' outlines are pushed about, m (no heap round)
            float4 _CloudForm;     // x: 1 if the low and middle clouds are shaped as mounds with lumps on them, from the noise (the rework of 2026-10-12; 0: as before it); y: how far the pattern of clouds has moved through itself, in lengths of the large noise (the sky changing with time); z: a trial's number (development build); w: 1 to draw without the far field (development build: before and after)
            float4 _CloudWake0;     // where a craft has gone through cloud, the newest place first (up to eight; a line through them is its path). xyz: the place, from the camera (m, the planet's axes; carried on with the wind since, as the cloud is); w: how long ago (s)
            float4 _CloudWake1;
            float4 _CloudWake2;
            float4 _CloudWake3;
            float4 _CloudWake4;
            float4 _CloudWake5;
            float4 _CloudWake6;
            float4 _CloudWake7;
            float4 _CloudWakeLook;     // x: how many such places (under two: no wake); y: how wide the wake is where it is new (m: some craft's widths, more behind a burning engine); z: how much of the cloud in its path is burnt off or blown out (0 to 1); w: how far from _CloudWakeMid any of it can reach (m)
            float4 _CloudWakeMid;     // xyz: the middle of the path, from the camera (m, the planet's axes)
            float4 _CloudFieldLevA;     // the level of the field above which is cloud, for so much of the sky covered: 1/32, 3/32, 5/32 and 7/32 of it;
            float4 _CloudFieldLevB;     // 9/32 to 15/32;
            float4 _CloudFieldLevC;     // 17/32 to 23/32;
            float4 _CloudFieldLevD;     // 25/32 to 31/32
            float4 _CloudHeapsAt;     // x: 1 if there is such a map (0: the heaps are worked out at every asking); y: how many lengths of the noise the map is across (it comes round, and is read by where a place is in the noise); z: how many dots across; w: which face of the cube it lies on (0, 1, 2: the one the camera is nearest)
            float4 _CloudTwistA;     // a tornado or a waterspout near the camera: xyz, its foot on the ground, from the planet's centre (the scene's axes, m); w, the radius of its strongest wind (m; nothing: none)
            float4 _CloudTwistB;     // x: how high the cloud's base stands over its foot (m); y: how far down from the cloud its funnel is cloud, 0 to 1; z: 1 over water (spray at its foot), 0 over land (dust); w: how far it has turned (radians, either way)
            float4 _CloudEyeA;     // a cyclone within sight: xyz, its middle on the ground, from the planet's centre (the scene's axes, m); w, how far out its strongest wind is (m; nothing: no cyclone)
            float4 _CloudEyeB;     // x: how strong it is now (0 to 1); y: which way it turns, seen from above (1: against the clock, as north of the equator; -1: with it); z: where round it its bands lie (radians); w: its top (m above the radius)
            float4 _CloudEyeC;     // xyz: east at its middle (the scene's axes, a unit); w: its base (m above the radius)
            float4 _CloudShelfA;     // a squall line within sight: xyz, the middle of its row of storms on the ground, from the planet's centre (the scene's axes, m); w, half the row's length (m; nothing: no line)
            float4 _CloudShelfB;     // xyz: the way it moves, level at its middle (the scene's axes, a unit); w: how strong (0 to 1)
            float4 _CloudShelfC;     // x: its storms' base (m above the radius); y: how far ahead of the row its wind's front runs (m)
            float4 _CloudEyeD;     // x: how far its wall's cloud has turned round the eye (radians); y: 1 to draw the cyclone's own cloud alone, without the layers' (for looking at it in the lab)
            float4 _CloudCellA0;     // storms and showers near the camera (up to six): xyz, the middle on the ground, from the planet's centre (the scene's axes, m); w, how far it reaches (m)
            float4 _CloudCellA1;
            float4 _CloudCellA2;
            float4 _CloudCellA3;
            float4 _CloudCellA4;
            float4 _CloudCellA5;
            float4 _CloudCellB0;     // and for each: x, its base; y, its top (m above the radius); z, how strong (0 to 1); w, how wet
            float4 _CloudCellB1;
            float4 _CloudCellB2;
            float4 _CloudCellB3;
            float4 _CloudCellB4;
            float4 _CloudCellB5;
            float4 _CloudLightF;     // x: cells across each level; y: levels (each four times as wide as the last); z: heights (a multiple of four); w: readings beyond 739 m (8 if nought)
            float4 _CloudLightG;     // x: the finest level's cell (m); y: the lowest height (m above the radius); z: the heights' spacing (m); w: 1 where the walk may read it (nought: not made yet, or off)
            float4 _CloudLightA;     // xyz: the field's middle, on the ground: a way from the planet's centre (the scene's axes)
            float4 _CloudLightE;     // xyz: the field's east there (a unit, the scene's axes)
            float4 _CloudLightN;     // xyz: its north
            float4 _CloudJitter;     // xy: how far this frame's lines of sight are moved off the middles of the walk's pixels (in widths and heights of the picture); z: 1 if what is kept from frame to frame has more dots than the walk's picture (each frame's lines then go through a different one of the kept picture's dots in turn)
            Texture3D _CloudField; SamplerState sampler_CloudField;     // heaps made beforehand, lumps upon lumps (tools/clouds/heapfield.swift): a square of sky that repeats east and north, base to top. r: how far inside a heap's surface a place is (above a half) or outside it, less the heap's own number; g: the most r reaches within two cells of there (at every size of the field: for walking through clear air in long strides without stepping over a heap)
            Texture2D _CloudHeaps; SamplerState sampler_CloudHeaps;     // the low layer's heaps of the lattice, drawn once a frame into a map over the ground round the camera (go 5): for each place over the ground, the heap that has it or stands nearest: r, how far the place is from that heap's upright (m); g, the heap's half-width (m; nothing: none near); b, its height (m); a, how far through its life (-1 to 1)
            Texture2D _CloudHeapsB; SamplerState sampler_CloudHeapsB;     // and: r, how far above the layer's base that heap's own base stands (m)
            Texture2D _CloudCells; SamplerState sampler_CloudCells;     // a table of numbers got by chance, 256 by 256, four to a place (the same in the game and the lab: Clouds.cs, cloudlab.swift): for each point of the heaps' lattice, when its heaps come, how they rank in size, and how far out of place they stand
            Texture3D _CloudShape; SamplerState sampler_CloudShape;     // the large noise: r, the rounded heaps (Perlin noise swelled by cells); g, b, a: cells at three sizes
            Texture3D _CloudDetail; SamplerState sampler_CloudDetail;     // the small noise: cells at three sizes
            Texture2D _CloudMap; SamplerState sampler_CloudMap;     // the weather over the planet (longitude across, latitude up), its low clouds: r, how much cloud; g, what kind (0 flat sheets to 1 storm towers); b, the base and a, the top (in 8 km and 16 km)
            Texture2D _CloudMapB; SamplerState sampler_CloudMapB;     // r: how wet (rain darkens the base); g: high cloud; b: storm; a: how much middle cloud
            Texture2D _CloudMapC; SamplerState sampler_CloudMapC;     // the middle clouds: r, what kind; g, the base (in 12 km); b, the top (in 16 km); a, how wet
            Texture2D _CloudAirLut; SamplerState sampler_CloudAirLut;     // how much sunlight comes through the air to a height (up) with the sun at an angle (across: its sine, -1 to 1)
            Texture2D _CloudNow; SamplerState sampler_CloudNow;     // (go 3) this frame's walk: the light, and how much shows through
            Texture2D _CloudNowD; SamplerState sampler_CloudNowD;     // (go 3) and how far off the cloud is
            Texture2D _CloudHist; SamplerState sampler_CloudHist;     // (go 3) the clouds as taken together up to last frame
            Texture2D _CloudHistD; SamplerState sampler_CloudHistD;
            Texture2D _CameraDepthTexture; SamplerState sampler_CameraDepthTexture;
            Texture2D _CloudLight; SamplerState sampler_CloudLight;     // the sun-side light field (go 9; 2026-10-18), in place of the shadow's second map: for each cell round the camera, how much of the sun's light comes through the cloud between it and the sun past the walk's first step that way (28 m), kept as exp(-0.1 e-folds): r out to 154 m on, g to 346, b to 711 (where the walk's own third, fourth and fifth steps end), a beyond, out of the shell (research/rendering/experiments/cloud-light-field.md)
            static float2 eyeXY;
            static float eyeBreak;
            static float eyeAcross;
            static float eyeTurn;
            static float eyeAlong;
            static float heapsOpen;
            static float heapsInside;
            static float heapsSmooth;
            static float heapsNear;
            static float3 formUp;
            static float formPer;
            static float formAway;
            static float farThin;
            static float farRim;
            static float wakeOut;

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
            // The first step of the walk at or past t0, on one grid laid along the line from the camera: steps of `least` near the
            // camera, then each a share `each` of its distance (from where that share comes to `least`), then of `longest` (from where it
            // comes to that): the walk's own steps, t + clamp(t * each, least, longest), begun not where the line enters the shell but at
            // the camera, the grid moved on by `j` (a share of a step) each frame. A floor or top of the shell that moves then only adds
            // or takes away steps at the ends; the steps between stay where they were.
            // The sun-side light field's cells (go 9): level `level` (each four times the last's cell), cell (i, j) of its n x n, height k:
            // its middle, from the planet's centre (the scene's axes). Laid on the plane that touches the planet at the field's middle
            // and put back on the sphere (so that near the middle the cells are nearly square, as far as the field reaches).
            float3 lightCellAt(int level, int i, int j, int k)
            {
                float n = _CloudLightF.x, cell = _CloudLightG.x * pow(4.0, float(level));
                float2 xy = (float2(float(i), float(j)) + 0.5 - 0.5 * n) * cell;
                float h = _CloudLightG.y + (float(k) + 0.5) * _CloudLightG.z;
                return normalize(_CloudLightA.xyz * _CloudCam.w + _CloudLightE.xyz * xy.x + _CloudLightN.xyz * xy.y) * (_CloudCam.w + h);
            }

            // The field at a place (from the planet's centre, the scene's axes): the finest level that holds it with a cell to spare, read
            // between the four cells round it in its height by the card and by hand between two heights. `ok`: false where no level holds it;
            // `inner`: how far inside the last level's edge; `cell`: how wide the cells read are (m).
            float4 lightFieldAt(float3 p, out bool ok, out float inner, out float cell)
            {
                float3 d = normalize(p);
                float cd = dot(d, _CloudLightA.xyz), n = _CloudLightF.x, H = _CloudLightF.z, rows = H / 4.0;
                float2 xy = float2(dot(d, _CloudLightE.xyz), dot(d, _CloudLightN.xyz)) * (_CloudCam.w / max(cd, 0.5));
                float kf = (length(p) - _CloudCam.w - _CloudLightG.y) / _CloudLightG.z - 0.5;
                float level = 0.0;
                float2 g = xy / _CloudLightG.x + 0.5 * n;
                if (min(g.x, g.y) < 1.0 || max(g.x, g.y) > n - 1.0) { level = 1.0; g = xy / (4.0 * _CloudLightG.x) + 0.5 * n; }
                if (min(g.x, g.y) < 1.0 || max(g.x, g.y) > n - 1.0) { level = 2.0; g = xy / (16.0 * _CloudLightG.x) + 0.5 * n; }
                ok = cd >= 0.5 && level < _CloudLightF.y && min(g.x, g.y) >= 1.0 && max(g.x, g.y) <= n - 1.0 && kf >= -0.5 && kf <= H - 0.5;
                cell = _CloudLightG.x * pow(4.0, level);
                // (how far inside the last level's edge: 0 at it to 1 eight cells in, where the walk's own steps take over beyond it)
                inner = level < _CloudLightF.y - 1.0 ? 1.0 : clamp((min(min(g.x, g.y), n - max(g.x, g.y)) - 1.0) / 8.0, 0.0, 1.0);
                float k0 = clamp(floor(kf), 0.0, H - 1.0), k1 = min(k0 + 1.0, H - 1.0), fk = clamp(kf - k0, 0.0, 1.0);
                float2 size = float2(4.0 * n, rows * n * _CloudLightF.y);
                float2 inTile = clamp(g, ((float2)(0.5)), ((float2)(n - 0.5)));
                float2 uv0 = (float2(Mod(k0, 4.0), floor(k0 / 4.0) + level * rows) * n + inTile) / size;
                float2 uv1 = (float2(Mod(k1, 4.0), floor(k1 / 4.0) + level * rows) * n + inTile) / size;
                return lerp(SampleLod(_CloudLight, uv0, 0.0), SampleLod(_CloudLight, uv1, 0.0), fk);
            }

            float firstStep(float t0, float j, float least, float longest, float each)
            {
                float sA = least / each;
                // (uniform steps of `least` to sA, the first point at or past it begins the growing steps)
                float pAB = (ceil(sA / least - j) + j) * least;
                if (t0 <= pAB || longest <= least) return (ceil(t0 / least - j) + j) * least;
                float g = log(1.0 + each);
                float sB = longest / each;
                float nBC = ceil(log(max(sB / pAB, 1.0)) / g);
                float pBC = pAB * exp(nBC * g);
                if (t0 <= pBC) return pAB * exp(ceil(log(t0 / pAB) / g) * g);
                return pBC + ceil((t0 - pBC) / longest) * longest;
            }

#define PI 3.14159265

            float remap(float v, float a, float b, float c, float d) { return c + (v - a) / max(b - a, 1e-5) * (d - c); }

            // ---------------------------------------------------------------- noise to make the cubes from

            uint mixBits(uint x)
            {
                x ^= x >> 16u; x *= 0x7feb352du; x ^= x >> 15u; x *= 0x846ca68bu; x ^= x >> 16u;
                return x;
            }

            int3 wrap(int3 c, int period) { return c - period * ((int3)(floor(((float3)(c)) / float(period)))); }

            uint cellHash(int3 c, int period, uint seed)
            {
                int3 w = wrap(c, period);
                return mixBits(uint(w.x) + mixBits(uint(w.y) + mixBits(uint(w.z) + seed)));
            }

            // Cells (Worley noise): 1 at each cell's point, falling to 0 a cell's width away. The pattern repeats every "period" cells.
            float cells(float3 p, int period, uint seed)
            {
                float3 base = floor(p);
                float3 f = p - base;
                int3 b = ((int3)(base));
                float best = 9.0;
                { [loop] for (int z = -1; z <= 1; z++)
                    { [loop] for (int y = -1; y <= 1; y++)
                        { [loop] for (int x = -1; x <= 1; x++)
                        {
                            int3 o = int3(x, y, z);
                            uint h = cellHash(b + o, period, seed);
                            float3 point_ = float3(float(h & 1023u), float((h >> 10u) & 1023u), float((h >> 20u) & 1023u)) / 1023.0;
                            float3 d = ((float3)(o)) + point_ - f;
                            best = min(best, dot(d, d));
                        } } } }
                return clamp(1.0 - sqrt(best), 0.0, 1.0);
            }

            float cellsLayered(float3 p, int period, uint seed)
            {
                return cells(p * float(period), period, seed) * 0.625 + cells(p * float(period * 2), period * 2, seed + 7u) * 0.25 + cells(p * float(period * 4), period * 4, seed + 13u) * 0.125;
            }

            float3 slope(int3 c, int period, uint seed)
            {
                uint h = cellHash(c, period, seed);
                float3 g = float3(float(h & 1023u), float((h >> 10u) & 1023u), float((h >> 20u) & 1023u)) / 511.5 - 1.0;
                return g / max(length(g), 1e-3);
            }

            // Smooth noise (Perlin's), -1 to 1 or near it, repeating every "period".
            float smoothNoise(float3 p, int period, uint seed)
            {
                float3 base = floor(p);
                float3 f = p - base;
                int3 i = ((int3)(base));
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float a = dot(slope(i, period, seed), f);
                float b = dot(slope(i + int3(1, 0, 0), period, seed), f - float3(1.0, 0.0, 0.0));
                float c = dot(slope(i + int3(0, 1, 0), period, seed), f - float3(0.0, 1.0, 0.0));
                float d = dot(slope(i + int3(1, 1, 0), period, seed), f - float3(1.0, 1.0, 0.0));
                float e = dot(slope(i + int3(0, 0, 1), period, seed), f - float3(0.0, 0.0, 1.0));
                float g = dot(slope(i + int3(1, 0, 1), period, seed), f - float3(1.0, 0.0, 1.0));
                float h = dot(slope(i + int3(0, 1, 1), period, seed), f - float3(0.0, 1.0, 1.0));
                float k = dot(slope(i + int3(1, 1, 1), period, seed), f - float3(1.0, 1.0, 1.0));
                return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y), lerp(lerp(e, g, u.x), lerp(h, k, u.x), u.y), u.z) * 1.4;
            }

            // ---------------------------------------------------------------- the weather and the shapes

            // ---------------------------------------------------------------- a cyclone
            //
            // What a hurricane is, and so what is drawn. Over warm sea the air near the water flows in towards a low, turning as it
            // comes (against the clock north of the equator), and rises where it can come no nearer: in a ring round the middle, the
            // eye wall, a ring of storm cloud from a few hundred metres up to the top of the weather. The ring leans outward as it
            // rises (the air keeps its spin as it climbs, and what holds it in weakens with height), so the eye is twice as wide at
            // its top as at the sea: from inside, a stadium. At the top the air can rise no further and flows out all round: a
            // canopy of ice many times wider than the wall, thick at the wall and thinning to streaks at its rim, with the wall's
            // own tops bubbling up through it round the eye. In the eye the air sinks and dries: clear from the canopy's level down
            // to a little low cloud on its floor. Outside the wall the air comes in along spiral bands, rows of storm towers with
            // gaps of nearly clear air between them, wound in towards the middle the way the wind blows.
            //
            // The wall, the canopy and the bands are shapes of their own here (cycloneAt), laid out round the cyclone's middle as
            // the weather has it (Violent.cs: the same eye, wall and bands the wind and the rain go by). The planet's cloud map
            // cannot hold them: a dot of it is 15 km on Kerbin, and an eye is 12 across. What the map's layers do inside a cyclone
            // is set here too (weatherAt): a floor of broken low cloud in the eye and nothing above it, no high cloud where the
            // canopy is, and the gaps between the outer bands.


            // Where a place is in the cyclone: x, how far out, in radii of its strongest wind; y, how far the place is from the
            // middle line of the nearest band (m); z, how far out (m); w, the bands' own angle there (a band's middle line runs
            // wherever it is a whole number of half turns).
            float4 eyeAt(float3 p)
            {
                float3 mid = _CloudEyeA.xyz / max(length(_CloudEyeA.xyz), 1.0);
                float along = dot(p, mid);
                if (along <= 0.0) return float4(1e6, 1e9, 1e9, 0.0);
                float3 flat_ = p - mid * along;
                eyeXY = float2(dot(flat_, _CloudEyeC.xyz), dot(flat_, cross(_CloudEyeC.xyz, mid)));
                float r = length(eyeXY), x = r / _CloudEyeA.w;
                // (as Violent.CycloneAt has it: two bands, a turn inward for every e-fold and a bit of distance)
                float angle = r > 1.0 ? atan2(eyeXY.y, eyeXY.x) : 0.0;
                float turn = angle * _CloudEyeB.y + 2.3 * log(max(x, 0.5)) + _CloudEyeB.z;
                eyeTurn = turn;
                // (the curves that cross the bands squarely: 2.3 times the angle round, less the logarithm of the distance out)
                eyeAlong = 2.3 * angle * _CloudEyeB.y - log(max(x, 0.5));
                // (and outside the core no band is the curve the rule draws: each wanders a quarter of a radian either way of it,
                // which is well inside the width the weather gives its rain. Drawn by the rule alone, the two bands seen from
                // above were a pair of sickles.)
                eyeBreak = 0.5;
                { [loop] for (int once = x > 3.0 && x < 14.5 ? 0 : 1; once < 1; once++)
                {
                    float4 wander = SampleLod(_CloudShape, float3(cos(angle) * 0.35, sin(angle) * 0.35, x * 0.09) + float3(0.11, 0.53, 0.29), 2.0 * _CloudHaze.x);
                    turn += (wander.r - 0.74) * 3.0 * smoothstep(3.0, 6.0, x);
                    eyeBreak = wander.g;
                } }
                float off = turn - PI * floor(turn / PI + 0.5);
                eyeAcross = off * r / 2.508;
                return float4(x, abs(eyeAcross), r, turn);
            }

            // How far out a cyclone's canopy reaches, in radii of its strongest wind.
            float eyeRim() { return 5.5 + 3.5 * _CloudEyeB.x; }

            // One of the weather's maps read smoothly: the cubic spline through its dots, from four bilinear readings (Sigg and
            // Hadwiger, 2005). Read bilinearly, a field that the clouds' cover cuts at a level is cut along the map's own dots: from
            // orbit the edge of a region of cloud was a staircase, straight runs and right angles fifteen kilometres long. (Used by
            // stage 6, once a frame, for the low clouds' map: see weatherAt.)
            float4 mapSmooth(Texture2D map, SamplerState samplermap, float2 uv)
            {
                float2 size = ((float2)(TextureSize(map, 0)));
                float2 at = uv * size - 0.5;
                float2 i = floor(at), f = at - i;
                float2 f2 = f * f, f3 = f2 * f;
                float2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0, w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
                float2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0, w3 = f3 / 6.0;
                float2 g0 = w0 + w1, g1 = w2 + w3;
                float2 h0 = (i - 0.5 + w1 / g0) / size, h1 = (i + 1.5 + w3 / g1) / size;
                return g0.y * (g0.x * SampleLod(map, h0, 0.0) + g1.x * SampleLod(map, float2(h1.x, h0.y), 0.0))
                     + g1.y * (g0.x * SampleLod(map, float2(h0.x, h1.y), 0.0) + g1.x * SampleLod(map, h1, 0.0));
            }

            // The weather at a place (from the planet's centre, the scene's axes): the low clouds (amount, kind, base, top), the rest
            // (wet, high, storm, middle amount) and the middle clouds (kind, base, top, wet).
            void weatherAt(float3 p, out float4 a, out float4 b, out float4 c)
            {
                float3 f = float3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
                float r = max(length(f), 1.0);
                float lon = atan2(f.y, f.x);
                float lat = asin(clamp(f.z / r, -1.0, 1.0));
                float2 uv = float2(lon / (2.0 * PI) + 0.5, lat / PI + 0.5);
                // (and the reading bent a little, by up to most of a dot of the weather's map, over some tens of kilometres, as a
                // weather's edge wanders: so that no edge runs along the map's rows and columns, even where it changes sharply)
                float2 bend = SampleLod(_CloudShape, f / 61000.0 + float3(0.37, 0.11, 0.73), 2.0 * _CloudHaze.x).gb - 0.48;
                uv += bend * (1.6 / ((float2)(TextureSize(_CloudMapB, 0))));
                // (The low clouds' map as Clouds.cs hands it: drawn smoothly once a frame at four times the weather's own (stage 6),
                // so that this one reading of it is smooth. Read here smoothly, four readings at every 1200 m of a line cost one and
                // a half milliseconds a frame from the ground.)
                a = SampleLod(_CloudMap, uv, 0.0);
                b = SampleLod(_CloudMapB, uv, 0.0);
                c = SampleLod(_CloudMapC, uv, 0.0);
                a.z *= 8000.0; a.w *= 16000.0;
                c.y *= 12000.0; c.z *= 16000.0;
                // ---- in a cyclone: what the map is too coarse to say
                { [loop] for (int once = _CloudEyeA.w > 0.0 ? 0 : 1; once < 1; once++)
                {
                    float4 e = eyeAt(p);
                    if (e.x > 14.0) break;
                    float now = _CloudEyeB.x, rim = eyeRim(), base = _CloudEyeC.w, deep = max(_CloudEyeB.w - base, 1000.0);
                    // (under the canopy, whatever the map's dots make of it: rain cloud from just over the sea to the middle of the
                    // storm's depth, and a sheet of middle cloud over that)
                    // (its edge wandering in and out by a radius and more: a circle's edge showed from above as a ring round the canopy)
                    float under = (1.0 - smoothstep(rim - 3.0, rim - 0.5, e.x + (eyeBreak - 0.5) * 3.0)) * smoothstep(0.1, 0.3, now);
                    a = lerp(a, float4(0.97, 0.04, base + 50.0, base + 0.42 * deep), under);
                    b.x = lerp(b.x, 0.9, under); b.a = lerp(b.a, 0.9, under);
                    c = lerp(c, float4(0.1, base + 0.45 * deep, base + 0.7 * deep, 0.6), under);
                    // (the eye, where a cyclone is strong enough to have one: broken low cloud on its floor, and nothing over it)
                    float eye = (1.0 - smoothstep(0.5, 0.7, e.x)) * smoothstep(0.35, 0.6, now);
                    a = lerp(a, float4(0.32, 0.5, base + 150.0, base + 600.0), eye);
                    b = lerp(b, float4(0.15, 0.0, 0.0, 0.0), eye);
                    // (the canopy is the high cloud, where it is)
                    b.g *= smoothstep(rim - 1.0, rim + 1.5, e.x);
                    // (and outside it: heaps, many near the bands and few in the gaps between them, and little middle cloud. Not
                    // what the map makes of a cyclone's edge, a sheet of rain cloud kilometres thick with holes in it: from above
                    // that is a tableland cut by canyons.)
                    float band = smoothstep(0.25, 0.8, 0.5 + 0.5 * cos(2.0 * e.w)) * (1.0 - smoothstep(9.0, 13.0, e.x)) * (0.3 + 0.7 * smoothstep(0.35, 0.65, eyeBreak));
                    float outside = smoothstep(rim - 3.0, rim - 0.5, e.x + (eyeBreak - 0.5) * 3.0) * (1.0 - smoothstep(11.0, 14.0, e.x)) * smoothstep(0.1, 0.3, now);
                    // (Along the bands the heaps are the bands' own storms, crowded and tall: towering heaps to six kilometres, nearly
                    // closing up along a band's middle, under the ice the bands throw out (the canopy's arms). From orbit a band is a
                    // lumpy line of storms under its outflow, as photographs have one; with half the sky in heaps a kilometre tall
                    // there, the arms were smooth combed ice over a scatter of flecks (the main chat, 2026-10-19 17:06).)
                    float crowd = band * band * (3.0 - 2.0 * band);
                    a = lerp(a, float4(lerp(0.16, 0.95, crowd), lerp(0.52, 0.68, band), base + 100.0, base + lerp(650.0, 6000.0, band)), outside);
                    b.x = lerp(b.x, 0.25 * band, outside); b.a *= 1.0 - outside;
                } }
            }

            // What changes over kilometres to hundreds of them, read where the weather is read (every 1200 m along a line of sight; the
            // same for the light's steps from there): x, how much more or less cover there is than the weather says (heaps in clusters
            // a few kilometres across with clear sky between, and wider patches of more and of less over tens of kilometres); yzw, a bend
            // of the noise (its 9 km repeat broken up). These are read at a third, a ninth and a 23rd of the large noise's scale
            // (repeating every 27, 81 and 207 km), which the camera's place wrapping round every 207 lengths leaves unchanged. (Read from
            // the noise's 16-cube, smooth over those 1200 m: the full noise, read once every 1200 m, jumped there, and the jumps showed as
            // lines across the heaps.)
            float4 broadAt(float3 g)
            {
                float m = 3.0 * _CloudHaze.x;
                float4 group = SampleLod(_CloudShape, g.yzx / 3.0 + float3(0.71, 0.37, 0.19), m);
                float4 broad = SampleLod(_CloudShape, g / 9.0 + float3(0.53, 0.29, 0.71), m);
                float4 broader = SampleLod(_CloudShape, g.zxy / 23.0 + float3(0.17, 0.83, 0.41), m);
                // (the 16-cube's first channel lies between 0.655 and 0.82 over eight tenths of it, its middle at 0.745, measured: each
                // spread from about none to all round that middle, so that on the whole there is as much cloud as the weather says)
                float cluster = smoothstep(0.66, 0.83, group.r);
                float wander = smoothstep(0.68, 0.81, (broad.r + broader.r) * 0.5);
                float raise = lerp(0.4, 1.8, cluster) * lerp(0.55, 1.6, wander);
                return float4(raise, broad.g - 0.48, broader.g - 0.48, broad.b - 0.48);
            }

            // Cloud streets: where a steady wind blows over churning air, the churning rolls lie along the wind and lift the air in lines
            // a few kilometres apart, and the heaps grow in rows along it. A factor on the cover (on the whole 1), the rows wandering;
            // read at every step (they are 3 km apart, far too near together to be read only every 1200 m).
            float streetsAt(float3 g, float4 wm)
            {
                if (_CloudStreets.w <= 0.0) return 1.0;
                float across = dot(g, _CloudStreets.xyz) * (9000.0 / 3000.0) * 6.2832 + wm.y * 14.0;
                return lerp(1.0, 0.45 + 1.1 * smoothstep(-0.4, 0.7, cos(across)), _CloudStreets.w);
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
                return lerp(lerp(sheet, heap, a), tower, b);
            }

            // The heaps in a reading of the large noise: its first channel swelled by its cells (Perlin's noise with Worley's, as in
            // Nubis), spread out, and the cells' own billows on the outline.
            float heapsOf(float4 s)
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
            // And how many metres inside a heap's surface that place is, small lumps and all; far below nothing: no such reading.
            // And how many metres inside it would be were the heap without its lumps (the new shapes'); far below nothing: not known.
            // How near any cloud can be to the place last asked about (cloudAt): less than nothing, at least so many metres off, and the
            // walk may stride that far through clear air; nothing or more, here or unknown. Every source of cloud whose shape is kept as
            // a distance says how near it is (the field's heaps, the storms' towers; a layer the place is above or below, by how far);
            // one carved from noise, which knows no distances, says unknown (a great number).

            // The level of the field above which is cloud, for so much of the sky to be covered (sixteen levels given, read between).
            float fieldLevelAt(int i)
            {
                float4 v = i < 4 ? _CloudFieldLevA : i < 8 ? _CloudFieldLevB : i < 12 ? _CloudFieldLevC : _CloudFieldLevD;
                int j = i - (i / 4) * 4;
                return j == 0 ? v.x : j == 1 ? v.y : j == 2 ? v.z : v.w;
            }

            float fieldLevel(float cover)
            {
                float x = cover * 16.0 - 0.5;
                if (x <= 0.0) return lerp(1.0, _CloudFieldLevA.x, clamp(cover * 32.0, 0.0, 1.0));
                x = min(x, 15.0);
                int i = int(x);
                return lerp(fieldLevelAt(i), fieldLevelAt(min(i + 1, 15)), x - float(i));
            }

            // The heaps made beforehand, at a place (from the planet's centre, the scene's axes) so far up its layer (0 base to 1 top):
            // how many metres inside the surface that the cover picks it is (outside it, less than nothing).
            // Where in the field: east and north over the ground. The planet is a ball and the field is flat, so the ground is taken in
            // bands of latitude, each with its own scale east and west (a band is as wide as the cosine of the latitude takes to fall
            // by a tenth: nowhere are the heaps stretched or squeezed by more than a twentieth); the band round the equator reaches to
            // 18 degrees each way.
            float3 heapsPlace(float3 p)
            {
                float3 f = float3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
                float r = max(length(f), 1.0);
                float lat = asin(clamp(f.z / r, -1.0, 1.0)), lon = atan2(f.y, f.x);
                // (the equator in the middle of its band: as an edge it fell wherever the cosine of the latitude, worked out in single
                // precision, first came to less than one, 150 m to either side of the equator: a strip of other heaps across the sky of
                // a space centre that stands a kilometre from it)
                float band = floor(log2(max(cos(lat), 0.02)) * 7.0 + 0.5);
                return float3(lon * exp2(band / 7.0) * _CloudCam.w, lat * _CloudCam.w, band);
            }

            // The field read at a place in it (heapsPlace: metres east and north, and which band), `size` times larger over the ground
            // than it was made (for a deeper layer: see layerAt).
            float heapsAt(float3 place, float hf, float level, float4 wm, float lod, float pixel, bool seek, float size)
            {
                float per = _CloudFieldA.x / size;
                float2 uv = (place.xy + wm.yz * 5400.0 + _CloudFieldB.yz * (hf * size)) * per + _CloudFieldA.zw / size + (place.z + size) * float2(0.37, 0.61);
                // (Seeking: whether there may be any heap within a stride and a half of here, the stride being three hundredths of the
                // distance from the camera. The field's second number at the copy of it whose cells are a third of that stride: never
                // less than the number anywhere within the stride. The stride is 0.03 of the distance, the distance 25 km times two to
                // the power of `lod` less one (the seeker asks one size coarser than the walk reads), a cell 25 m times `size`.)
                if (seek) return (SampleLod(_CloudField, float3(uv, hf), ceil(max(lod + 2.33 - log2(size), 0.0))).g - level) * _CloudFieldA.y * size;
                // (Not seeking: the copy of the field whose cells are as wide as a pixel of the picture is there, `pixel` metres.)
                float4 cell = SampleLod(_CloudField, float3(uv, hf), max(log2(pixel * per * 256.0) + 0.3, 0.0));
                heapsOpen = cell.b;
                // (The usual sky. The field has heaps of every size, many small and few large, as a count of real ones has; but all
                // of them in the sky at once, the hundreds of small ones each by itself, is a sky that does come and is not the
                // common one ("super noisy": the user, of a picture from above, 2026-10-11). Commonly the small ones stand round
                // the large or are not there. So unless today is such a day the small heaps that stand alone are left out: the field
                // read very coarsely, near its base, is high only where a wide heap is, and away from there the level is raised by
                // more than a small heap has. The seeker is not told: it may say "perhaps cloud" where there is now none, never the
                // other way.)
                { [loop] for (int once = _CloudHabit.x > 0.01 ? 0 : 1; once < 1; once++)
                {
                    float wide = SampleLod(_CloudField, float3(uv, 0.22), 3.6).r;
                    level += _CloudHabit.x * 0.24 * (1.0 - smoothstep(0.36, 0.5, wide));
                } }
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
                return exp2(0.678 * which) * lerp(max(_CloudHabit.y, 1.0), 1.0, clamp(which / 2.0, 0.0, 1.0));
            }

            // The heaps made beforehand at a place, for a layer so deep with so much of the sky covered: how many metres inside a
            // heap's surface it is, before the small lumps (seeking: the most that could be, anywhere within a stride). `size`: how
            // large the field was read.
            float heapsMetres(float3 p, float hf, float deep, float cover, float4 wm, float lod, float pixel, bool seek, bool turned, float2 pushed, out float size)
            {
                float next;
                size = heapsSize(deep, next);
                float3 place = heapsPlace(p);
                // (no heap is round: where each stands is pushed about by a pattern that changes over a kilometre or two, more for
                // the larger readings of the field, so that an outline runs out in one place and is bitten into in another)
                place.xy += pushed * size;
                if (turned) place = float3(place.y + 2371.0, -place.x + 1289.0, place.z);
                // (six hundredths less than asked: measured from straight above over 37 km by 21, the heaps hid 0.17, 0.26, 0.36, 0.45,
                // 0.55 and 0.70 of the ground where 0.1, 0.2, 0.3, 0.4, 0.5 and 0.65 were asked for; the clusters and gaps that the cover
                // is given do not come out even)
                float level = fieldLevel(max(cover - 0.06, cover * 0.4));
                float inside = heapsAt(place, hf, level, wm, lod, pixel, seek, size) - next * 300.0 * size;
                { [loop] for (int again = next > 0.0 ? 0 : 1; again < 1; again++)
                {
                    float open = heapsOpen, other = heapsAt(place, hf, level, wm, lod, pixel, seek, size * 1.6) - (1.0 - next) * 480.0 * size;
                    if (other > inside) inside = other; else heapsOpen = open;
                } }
                return inside;
            }

            // The lumps too small for the field's cells (25 m), carved at the pixel: cells of 50, 25 and 12 m, lumps upon lumps, each
            // standing proud of the creases round it by about its own half-width, as the lumps of a cauliflower do. Their middle is left
            // where the field has the surface (the carving is taken about its mean, which is also what the small noise comes to when
            // read coarsely, far off: no carving there, and as much cloud). Where the field is read larger, its cells are larger too,
            // and lumps of the sizes between are carved as well.
            float heapsLumps(float inside, float3 at, float pixel, float size)
            {
                if (inside <= -60.0 * size || inside >= 60.0 * size) return inside;
                float lumps = SampleLod(_CloudDetail, at * 45.0 + float3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / 3.1) + 0.3, 0.0, 6.0) * _CloudHaze.x).r;
                inside += (lumps - 0.48) * _CloudFieldB.w;
                { [loop] for (int more = size > 1.5 ? 0 : 1; more < 1; more++)
                {
                    float larger = SampleLod(_CloudDetail, at.yzx * (45.0 / size) + float3(0.31, 0.57, _CloudDrift.w * 0.6), clamp(log2(pixel / (3.1 * size)) + 0.3, 0.0, 6.0) * _CloudHaze.x).r;
                    inside += (larger - 0.48) * _CloudFieldB.w * size * 0.8;
                } }
                return inside;
            }

            // ---------------------------------------------------------------- the shapes of the rework of 2026-10-12
            //
            // The low and the middle clouds as shapes worked out in metres: heaps as domes over rising air, with lumps on them.
            //
            // What was wrong before. Heaps carved from noise in three dimensions and cut off at the layer's base and top are whatever
            // the noise happens to be there: smooth lenses in a shallow layer. Heaps made beforehand and pressed to the layer's depth
            // were pressed lumps and all (plates), and thinned for a smaller cover by wearing each heap down to its core (pointed hats).
            //
            // What a heap is. Air rises from the ground in separate bubbles, some hundreds of metres to a kilometre or two across,
            // one after another over a spot for some minutes. Each condenses at the same height (a level base, the same for every
            // heap of a sky) and goes on up as far as its warmth carries it, the wide ones higher than the narrow: a dome over each,
            // wider than tall on a fair day. The top of the layer is where the air stops rising (a warmer layer above): what reaches
            // it goes no higher and is flat there. A heap lives ten minutes to half an hour: it comes as a small puff at the base,
            // grows up and outward with a sharp lumpy outline, and when the air under it stops rising it goes ragged and thin and
            // evaporates from its base upward. And the surface of the dome is not smooth: it is the tops of smaller bubbles, rounded,
            // each with smaller ones on it, with creases between.
            //
            // So: places where air rises are set out over the layer's base, one to each point of a lattice of triangles (a little out
            // of place each, each with a size and a time of its own). A heap stands over each: it comes, grows, stands and fades,
            // and the next one over that point comes somewhere a little different, of another size. Each is a dome (an egg standing
            // on the base, cut level there), as tall as it is wide times so much, never higher than the layer's top; neighbours run
            // together. On that, round lumps of several sizes (the cells, read in all three directions: they are round whatever the
            // layer's depth), each proud by about its own half-width. All of it in metres, so that a surface can be as sharp as a
            // pixel, the lumps are the size they should be for the heap, the walk can be told how far off the nearest cloud is, and
            // the light which way the surface faces.
            //
            // The lattice lies on a face of a cube round the planet (the face the place is nearest: two of the place's three numbers
            // in the noise's space), so each point of it is found from the two numbers alone and only the three points of the
            // triangle a place is in have to be looked at. (Set out through all three directions instead, as it first was, eight
            // cells had to be looked at for every place asked about, and the clouds cost three and four times what they had.)

            // An egg standing on a level: how many metres inside it a place is (outside: less than nothing). `out_`: how far the place
            // is from the egg's upright, level; `up`: how far above the level it stands on; R: its half-width; H: its height.
            float eggAt(float out_, float up, float R, float H)
            {
                float2 pos = float2(out_, up - 0.3 * H), axes = max(float2(R, 0.7 * H), ((float2)(1.0)));
                float k0 = length(pos / axes), k1 = length(pos / (axes * axes));
                return k0 * (1.0 - k0) / max(k1, 1e-6);
            }

            // A heap's own dome (heapsRound, and the heaps read from the map): how high its middle stands and how far its half-height
            // reaches, as parts of its height. A heap that towers (congestus, near storms) is built as eggAt's egg, widest three tenths
            // of the way up with its sides upright there: a column. An ordinary heap, as photographs of a fair or a mostly cloudy sky
            // show it, is widest at its flat base and narrows the whole way up into a dome. (Built as the tower's egg everywhere, as it
            // was until 2026-10-15, every heap of an ordinary sky was a drum: upright sides, a rounded top and a flat dark bottom, as
            // tall as it was wide. The owner: "the clouds are really tall and pillar-like. I don't understand why they're so tall and
            // pillary.") `tower`: 0 for an ordinary heap, 1 for one that towers.
            // (A heap that towers stands on a flat base as wide as nearly all of it, its sides rising almost upright from the base: widest
            // a seventh of the way up, and its base rounded up towards the rim by a sixth of what an ordinary heap's is. Built widest
            // three tenths of the way up, with its base rounded up a fifth of its height at the rim as an ordinary heap's, as it was until
            // 2026-10-20, a towering heap near a storm had a round underside under its widest part: an egg (the owner, watching the PC:
            // "why do the bottoms of the clouds look like eggs?").)
            static float heapsMiddle = 0.3, heapsHalf = 0.7, heapsRimmed = 1.0;
            // (and how far its base is rounded up towards its rim, as a part of its height: nothing in its middle, a fifth of its height
            // at its rim for an ordinary heap; `x` how far out from its upright the place is, as a part of its half-width)
            float heapsRim(float x) { float r = clamp(x, 0.0, 1.2); return heapsRimmed * 0.2 * r * r * sqrt(r); }
            void heapsBuild(float tower) { heapsMiddle = lerp(-0.25, 0.15, tower); heapsHalf = lerp(1.25, 0.85, tower); heapsRimmed = lerp(1.0, 0.15, tower); }
            float heapEgg(float out_, float up, float R, float H)
            {
                float2 pos = float2(out_, up - heapsMiddle * H), axes = max(float2(R, heapsHalf * H), ((float2)(1.0)));
                float k0 = length(pos / axes), k1 = length(pos / (axes * axes));
                return k0 * (1.0 - k0) / max(k1, 1e-6);
            }

            // The larger of two, with a rounded join k wide.
            float larger(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return max(a, b) + h * h * k * 0.25; }

            // The heaps near a place. uv: the place on the layer's base under this one, across the face, in cells of the lattice;
            // tilt: what turns a step across the face into a step along the ground there; C: metres to a cell; per: cells to a length
            // of the noise; when: the time, in lifetimes of a heap; above: metres above the base; share: how many of the lattice's
            // points have a heap; widest: the largest's half-width (metres; none wider than 0.42 of a cell: only the three points of
            // the triangle a place is in are looked at, and a heap that reached further than two thirds of a cell from its point,
            // lumps and all, would be cut off where the next triangle begins); evenly: 0 if most are near the largest, 1 if they
            // are of every size alike; join: how wide the rounded join where two meet (metres); tall: how tall a heap is for its
            // width. How many metres inside the surface
            // of the heaps the place is (outside: less than nothing), before the lumps and before the base is cut level. Also given:
            // how large the heap is that has the place (its half-width, metres), how far through its life (-1 new, 1 gone), and how
            // far above the layer's base its own base is (metres).
            float heapsRound(float2 uv, float2 tilt, float2 going2, float C, int per, float when, float above, float share, float widest, float evenly, float join, float tall, float lid, float pads, out float size, out float age, out float lift, out float leaves)
            {
                // (rows nine tenths of a cell apart, each half a cell along from the last: nine tenths and not the 0.866 of even
                // triangles, so that the lattice comes round where the noise does)
                float n = uv.y * (10.0 / 9.0), m = uv.x - 0.5 * n;
                float2 corner = floor(float2(m, n));
                float2 in_ = float2(m, n) - corner;
                bool upper = in_.x + in_.y > 1.0;
                // (How far along the line being walked this triangle goes on, in metres: `going2` is how fast the line crosses the
                // face, cells to a metre. Only the heaps of this triangle's three points can be in it, so the walk may stride to
                // where the line leaves it and no further: there other heaps begin. A triangle is three straight edges; the line
                // leaves by the first of them it comes to.)
                float dn = going2.y * (10.0 / 9.0), dm = going2.x - 0.5 * dn;
                float3 room = upper ? float3(1.0 - in_.x, 1.0 - in_.y, in_.x + in_.y - 1.0) : float3(in_.x, in_.y, 1.0 - in_.x - in_.y);
                float3 closing = upper ? float3(dm, dn, -dm - dn) : float3(-dm, -dn, dm + dn);
                float3 until = room / max(closing, ((float3)(1e-7)));
                leaves = min(until.x, min(until.y, until.z));
                float2 round_ = float2(23.0, 230.0) * float(per);
                float inside = -1e5, lowest = 1e5, nearest = -1e5, liftNear = 0.0;
                size = 0.0; age = 0.0; lift = 0.0;
                { [loop] for (int k = 0; k < 3; k++)
                {
                    float2 cell = corner + (k == 0 ? (upper ? float2(1.0, 1.0) : float2(0.0, 0.0)) : k == 1 ? float2(1.0, 0.0) : float2(0.0, 1.0));
                    // (the point's own numbers, from the table: the lattice comes round where the noise does, and the table within that)
                    float4 its = TexelFetch(_CloudCells, ((int2)(cell - round_ * floor(cell / round_))) & 255, 0);
                    // (its time: each point's heaps follow one another, each begun when the last has gone, at its own moment)
                    float turn = its.r + when;
                    float born = floor(turn);
                    float life = (turn - born) * 2.0 - 1.0;
                    // (each heap that comes there of another size than the last: its rank among the others moved on each time)
                    float rank = frac(its.g + born * (633.0 / 1024.0));
                    // (Most points of a fair sky have no heap, and nothing more is worked out for those: with all of it worked out for
                    // every point, as at first, the new shapes cost twice what the old had.)
                    if (rank >= share) continue;
                    // (It grows over the first quarter of its time, stands, and from seven tenths of the way through it goes: the
                    // air under it has stopped rising, and it shrinks and evaporates from its base upward, what is left of it ending
                    // as a shred near where its top was.)
                    float going = clamp((life - 0.4) / 0.6, 0.0, 1.0);
                    going *= going * (3.0 - 2.0 * going);
                    float z = 1.0 - rank / share;
                    // (A heap that is going. The air under it has stopped rising, and it is eaten away from below: its base climbs
                    // through it, it narrows, and what is left at the end is a ragged cap where its top was. It stays where it
                    // stood. Until the evening of 2026-10-12 the whole egg was lifted off the base as it shrank: a solid egg afloat
                    // and aslant in the sky, which no cloud is.)
                    float full = widest * lerp(sqrt(z), z, evenly), grown = clamp((life + 1.0) * 2.0, 0.0, 1.0), R = full * grown * (1.0 - 0.45 * going);
                    if (R < 15.0 || going > 0.97) continue;
                    // (an egg standing on the base: widest three tenths of the way up, so that its sides overhang a little there; each
                    // its own shape, between three quarters and one and three tenths of the day's height for its width)
                    // (its own base a little above the layer's, by up to forty metres: the height where rising air condenses is nearly
                    // but not quite one height, and bases all at one height to the metre lie along the horizon like the slats of a blind)
                    float own = tall * (0.75 + 0.55 * frac(its.g * 5.13 + its.r * 1.71));
                    float H = 2.0 * full * grown * own, raised = 40.0 * its.a + going * 0.9 * H, up = above;
                    // (A place well above its top is that far from it at the least, which is all the walk needs to know of it: nothing
                    // more worked out. In a layer of many low heaps that is most of what is asked.)
                    if (up > H + 0.35 * C) { inside = max(inside, H - up); continue; }
                    // (out of place by up to 0.15 of a cell each way: a heap that stood further out would have its far side cut off
                    // where the next triangle begins)
                    float2 d = uv - (float2(cell.x + 0.5 * cell.y, 0.9 * cell.y) + (its.ba - 0.5) * 0.3);
                    float t = dot(d, tilt);
                    float out_ = sqrt(dot(d, d) + t * t) * C;
                    // (The spreading top. A heap that reaches the layer's top cannot go higher and goes sideways: it is wider under
                    // the lid than its own shape would have it, and the more the older. That, and not a sky of separate bulbs, is the
                    // usual sky: "usually the clouds aren't huge bulbs ... They're kind of sprawled out" (the user, 2026-10-11).)
                    float spread = pads * clamp((H - 0.9 * lid) / (0.3 * lid), 0.0, 1.0) * clamp((up - 0.55 * lid) / (0.3 * lid), 0.0, 1.0) * clamp(life + 0.8, 0.0, 1.0);
                    // (never wider so than 0.46 of a cell, for the same reason)
                    float2 pos = float2(out_ / min(1.0 + 0.9 * spread, 0.46 * C / R), up - heapsMiddle * H), turned = 1.0 / max(float2(R, heapsHalf * H), ((float2)(1.0)));
                    float2 scaled = pos * turned;
                    float k0 = length(scaled);
                    float one = k0 * (1.0 - k0) / max(length(scaled * turned), 1e-6);
                    // (the numbers of whichever heap has the place; but how high its base is cut, the lowest of all that have it: a
                    // heap that is going, standing in a larger one that is not, cut a hollow up into it, the height of its own
                    // risen base: caves in the flanks of towers)
                    if (one > inside) { size = max(R, 0.3 * full); age = life; }
                    // (its base level in the middle and rounded up into its side towards the rim, as a heap's underside is in the
                    // photographs: cut level right out to the rim, as it was until 2026-10-15, every heap had a sharp flat dark bottom,
                    // and with its upright sides was a drum)
                    float rimmed = raised + heapsRim(out_ / max(R, 1.0)) * H;
                    if (one > 0.0) lowest = min(lowest, rimmed); else if (one > nearest) { nearest = one; liftNear = rimmed; }
                    inside = larger(inside, one, join);
                } }
                lift = lowest < 1e4 ? lowest : liftNear;
                return inside;
            }

            // The middle sky's cloudlets in rows, where the air they are in is sheared (altocumulus undulatus): soft cloudlets, a little
            // drawn out, lying in loose lines across the wind, thinning and fraying towards the lanes of sky between the lines, a
            // line to every 500-odd metres (a cell and a seventh); over regions some tens of kilometres across, not everywhere (the WMO's atlas,
            // research/rendering/experiments/altocumulus-rows.md). Set for each face of the lattice by formOneNear.
            static float padsRows = 0.0;          // how much the cloudlets lie in rows here (0 to 1)
            static float2 padsRowsWay = float2(1.0, 0.0);   // across the rows, on the lattice's face: the way the wind blows
            static float padsFray = 0.0;          // how much softer a cloudlet's margin is here, for a row's lane (see formOneNear's edge)

            // A layer of pads as the cells of the air's churning (2026-10-17). A layer of pads, stratocumulus low and altocumulus in
            // the middle sky, is air turning over in cells: rising in each cell's middle, where the cloud is, and sinking at its
            // edges, where it is clear. From the ground it is a network: cloudlets of many sizes and shapes, each filling its cell to
            // a crack of open sky, the cracks narrow where the sky is mostly covered and wider where less, until each cell's cloud is
            // a tuft in its middle (photographs: the mackerel skies among Wikimedia Commons' altocumulus). Drawn as pads each round a
            // point of the heaps' lattice, as they were, a raft of them was rows of balls alike with open sky between. So: points on
            // a square grid, each out of place by up to nine twentieths of a cell (cells then of every size and shape, as Worley's
            // are); in each cell's part of the ground (the nearest point's, as a Voronoi diagram has it) a pillow out to `crack`
            // metres short of the cell's edge, a cushion with a flat top under the lid and rounded down into the crack;
            // `share` of the cells have one; and each cell's cloud comes and goes in its time, as a heap does: grown in an eighth of
            // it, thinning and drawn in towards its middle from seven tenths of the way through.
            // uv, tilt, going2, C, per, when, above: as heapsRound; share: how many of the cells have cloud; crack: metres of open
            // sky either side of a cell's edge; used: how deep the layer is (m); corner: how wide the rounding of a cell's corners is
            // (cells; nothing: the cell's edge as the soft second-nearest point, see padsColumn). How many metres inside the pillow
            // the place is (outside: less than nothing), and given out as heapsRound gives.
            // (A cell for a place over the ground, the same at every height (the map of the heaps holds it for the low layer): how
            // far inside its pillow's edge the place is (metres; outside: less than nothing), the pillow's reach through it (m;
            // nothing: an empty cell), how thick it is (m), and how far through its life; `liftOut`: how high its base stands
            // there, over the layer's.)
            float4 padsColumn(float2 uv, float2 tilt, float C, int per, float when, float share, float crack, float used, float corner, out float liftOut)
            {
                float round_ = 23.0 * float(per);
                // (the nearest two by distance over weight, their weights, and the true distance to the nearest, in cells; and each of the
                // nine's distance over weight and weight, for the edge taken from the lines where another comes as near)
                float2 home = floor(uv);
                float f1 = 1e5, f2 = 1e5, w1 = 1.0, w2 = 1.0, r1 = 0.0, all_ = 0.0;
                float fs[9], ws[9];
                int n1 = 0;
                float4 its1 = ((float4)(0.0));
                { [loop] for (int k = 0; k < 9; k++)
                {
                    float kk = float(k), row = floor(kk / 3.0);
                    float2 cell = home + float2(kk - 3.0 * row - 1.0, row - 1.0);
                    float4 its = TexelFetch(_CloudCells, ((int2)(cell - round_ * floor(cell / round_))) & 255, 0);
                    float2 d = uv - (cell + 0.5 + (its.ba - 0.5) * 0.9);
                    float t = dot(d, tilt);
                    // (each point with a weight of its own, its distances divided by it: the cells then of more sizes still, and their
                    // edges arcs, not the straight lines of plain Voronoi cells)
                    float r = sqrt(dot(d, d) + t * t), w = 0.75 + 0.5 * frac(its.g * 3.7 + its.b * 1.3), f = r / w;
                    fs[k] = f; ws[k] = w;
                    if (f < f1) { f2 = f1; w2 = w1; f1 = f; w1 = w; r1 = r; its1 = its; n1 = k; } else if (f < f2) { f2 = f; w2 = w; }
                    all_ += exp(-f * 3.5);
                } }
                // (How far to the cell's edge, metres: where the nearest and the next come to the same, by how fast their difference
                // grows, which is the sum of their weights' inverses. The next taken softly, from all the others together, two
                // sevenths of a cell wide: where two or three are nearly as near, at the cell's corners, it is nearer, and the corners
                // are rounded off. Sharp, every cloudlet was a shard of broken ice.)
                float f2s = min(f2, -log(max(all_ - exp(-f1 * 3.5), 1e-30)) / 3.5);
                float edge = max(f2s - f1, 0.0) / (1.0 / w1 + 1.0 / w2) * C;
                // (The low layer's: the nearest of the lines where one of the others comes as near, each by how fast the two distances
                // part, taken softly over `corner` of a cell, so that a corner, where two lines are as near, is rounded and an edge is
                // where it is. The second-nearest taken softly from all eight others drew every edge in, not the corners alone, by a
                // sixth of a cell or so: with the crack on top, a stratocumulus sky the weather calls nine tenths covered was a third
                // covered from straight below or above, scattered floes, and from the ground a sky of separate puffs
                // (research/rendering/experiments/clouds-from-above.md, the pads).)
                { [loop] for (int once = corner > 0.0 ? 0 : 1; once < 1; once++)
                {
                    float lines = 0.0;
                    { [loop] for (int k = 0; k < 9; k++)
                        if (k != n1) lines += exp(-((fs[k] - f1) / (1.0 / w1 + 1.0 / ws[k])) / corner); }
                    edge = max(-corner * log(max(lines, 1e-30)), 0.0) * C;
                } }
                float turn = its1.r + when, born = floor(turn), life = (turn - born) * 2.0 - 1.0;
                float rank = frac(its1.g + born * (633.0 / 1024.0));
                liftOut = 0.0;
                // (an empty cell: the nearest cloud is across its edge and the crack, as high as a pillow is on the whole)
                if (rank >= share) return float4(-(edge + crack), 0.0, 0.8 * used, life);
                float going = clamp((life - 0.7) / 0.3, 0.0, 1.0);
                going *= going * (3.0 - 2.0 * going);
                float strong = clamp((life + 1.0) * 4.0, 0.0, 1.0) * (1.0 - going);
                // (How far inside the pillow's edge, over the ground: to the crack (each cell's crack its own, from two thirds of the
                // day's to half as wide again), the pillow drawn in towards its middle while it grows and goes.)
                float across = r1 * C + edge;
                float e = edge - crack * (0.65 + 0.85 * frac(its1.a * 7.1 + its1.g * 2.9)) - (1.0 - lerp(0.35, 1.0, strong)) * across;
                // (Its top: thickest over the middle of the cell, where the air rises, nearly as thick as the layer and each its own,
                // from three fifths of it to all of it (cut flat by the layer's top, as the thicker were, they were white floes on
                // the sea from above), and a little more than half that at its edge (padsProfile); its base level out to the crack.
                // Seen from below, darker where it is thick, as photographs of the cells show them. A dome on a rounded base, as a
                // heap is, left half of every cell open sky; thinned out to nothing at the crack, each cloudlet had a thin ring
                // round it, parted from it by a band of sky; a cushion, as thick at its edge as in its middle, was moulded plastic.)
                float H = used * (0.6 + 0.4 * frac(its1.g * 5.13 + its1.r * 1.71)) * lerp(0.5, 1.0, strong);
                liftOut = 30.0 * its1.a + going * 0.5 * H;
                return float4(e, across, H, life);
            }

            // (A cell's pillow, from its column (padsColumn), at `above` metres over the layer's base: how many metres inside it.)
            // (Outside its outline as inside it, no higher than the pillow is there. Outside, as until 2026-10-18, it was the distance to
            // the outline alone, the same at every height: the soft edge round every pillow stood up as a sleeve of thin cloud to as high
            // as the layer may be asked about (150 m over what it uses). Over decks of stratocumulus and altocumulus that was thin whiskers
            // round the cloudlets; in a deep middle layer between pads and sheet (the weather's 0.18, using 1.6 km of a 3 km layer), a sky
            // of pillars hanging from the cloudlets, which is why its altocumulus is drawn as pads alone (CloudsWeather.Kind).)
            float padsProfile(float4 col, float above)
            {
                return min(col.x, col.z * lerp(0.55, 1.0, sqrt(clamp(col.x / max(0.4 * col.y, 1.0), 0.0, 1.0))) - above);
            }

            float padsCells(float2 uv, float2 tilt, float2 going2, float C, int per, float when, float above, float share, float crack, float used, float corner, out float size, out float age, out float lift, out float leaves)
            {
                // (The ground under the cells bent by up to a fifth of a cell each way, over a cell and a half and less: their edges
                // then wander as the edges of real cells do. Straight, and even as arcs, a deck of them was a quilt of tiles. The
                // large noise's coarser cells for the one way and its finer for the other, which are not alike.)
                float4 bend = SampleLod(_CloudShape, float3(uv * 0.17, 0.37), 1.5);
                uv += (bend.gb - 0.48) * 1.3;
                float laneAt = 0.0, laneDeep = 1.0;
                padsFray = 0.0;
                // (in rows: the cells drawn out along the rows by two fifths, the rows bent a little over some cells and their lanes deeper
                // here and shallower there along them, so that the lines part and join as real ones do; the line's
                // way along the ground the same, for how far it may stride)
                { [loop] for (int once = padsRows > 0.01 ? 0 : 1; once < 1; once++)
                {
                    float2 b = padsRowsWay, a = float2(-b.y, b.x);
                    float ua = dot(uv, a), ub = dot(uv, b);
                    ub += (SampleLod(_CloudShape, float3(ua * 0.06, ub * 0.02, 0.61), 1.0).r - 0.5) * 0.9 * padsRows;
                    laneAt = ub;
                    laneDeep = 0.45 + 0.55 * smoothstep(0.3, 0.7, SampleLod(_CloudShape, float3(ua * 0.12, ub * 0.04, 0.27), 1.0).g);
                    float s = lerp(1.0, 0.72, padsRows);
                    uv = a * (ua * s) + b * ub;
                    going2 = a * (dot(going2, a) * s) + b * dot(going2, b);
                } }
                // (how far along the line being walked this square of the grid goes on: the points looked at are its own and its
                // eight neighbours', and the two nearest of all are among those, so the walk may stride to where the line leaves it;
                // a quarter less, for the bend)
                float2 in_ = uv - floor(uv);
                float2 room = lerp(in_, 1.0 - in_, step(0.0, going2));
                float2 until = room / max(abs(going2), ((float2)(1e-7)));
                leaves = 0.75 * min(until.x, until.y);
                float4 col = padsColumn(uv, tilt, C, per, when, share, crack, used, corner, lift);
                // (The lanes: towards the lines' troughs the cloudlets thin to a fifth and draw back a little, and their margins soften (padsFray),
                // so that a lane is sky between wisps and not a cut; the cloudlets that are there grow by as much as the lanes took (only those
                // grow: the clear patches of the day's pattern stay clear). Drawn back to an edge and joined along the crests, as first tried, the
                // rows were hard strips: "too hard edged, they don't look real" (the owner, 2026-10-19).)
                { [loop] for (int once = padsRows > 0.01 ? 0 : 1; once < 1; once++)
                {
                    float wave = 0.5 + 0.5 * cos(6.2832 * laneAt / 1.15);
                    float lane = smoothstep(0.15, 1.0, 1.0 - wave) * laneDeep;
                    col.x += padsRows * ((col.y > 0.0 ? 0.045 * C : 0.0) - lane * 0.28 * C);
                    col.z *= lerp(1.0, lerp(1.0, 0.2, lane), padsRows);
                    padsFray = padsRows * lane;
                } }
                size = col.y > 0.0 ? max(col.y, 0.25 * C) : 0.0; age = col.w;
                return padsProfile(col, above);
            }

            // The same heaps, for the map (go 5): for a place over the ground, the heap that has it or stands nearest, with the heaps
            // beside it taken in where two meet, so that what is read from the map between them is a join and not a seam. Six points
            // of the lattice are looked at here (the triangle's three and the three across its edges), where the walk could afford
            // three: it is done once for each dot of the map, not at every asking. Given: x, how far the place is from the heap's
            // upright (m); y, its half-width (m; nothing: none near, and x is then how far off the nearest could be); z, its height (m);
            // w, how far through its life. `raisedOut`: how far its own base stands above the layer's.
            float4 heapsMap(float2 uv, float2 tilt, float C, int per, float when, float share, float widest, float evenly, float join, float tall, out float raisedOut)
            {
                float n = uv.y * (10.0 / 9.0), m = uv.x - 0.5 * n;
                float2 corner = floor(float2(m, n));
                bool upper = (m - corner.x) + (n - corner.y) > 1.0;
                float2 round_ = float2(23.0, 230.0) * float(per);
                float gapBest = 0.5 * C, sum = 0.0, sumR = 0.0, sumH = 0.0, sumLife = 0.0, sumRaised = 0.0, gapSoft = 1e5;
                float gaps[6]; float4 found[6];
                { [loop] for (int k = 0; k < 6; k++)
                {
                    float2 cell = corner + (k == 0 ? (upper ? float2(1.0, 1.0) : float2(0.0, 0.0)) : k == 1 ? float2(1.0, 0.0) : k == 2 ? float2(0.0, 1.0)
                                          : k == 3 ? (upper ? float2(0.0, 0.0) : float2(1.0, 1.0)) : k == 4 ? (upper ? float2(2.0, 0.0) : float2(1.0, -1.0)) : (upper ? float2(0.0, 2.0) : float2(-1.0, 1.0)));
                    float4 its = TexelFetch(_CloudCells, ((int2)(cell - round_ * floor(cell / round_))) & 255, 0);
                    float turn = its.r + when;
                    float born = floor(turn);
                    float life = (turn - born) * 2.0 - 1.0;
                    float rank = frac(its.g + born * (633.0 / 1024.0));
                    gaps[k] = 1e5; found[k] = ((float4)(0.0));
                    if (rank >= share) continue;
                    float going = clamp((life - 0.4) / 0.6, 0.0, 1.0);
                    going *= going * (3.0 - 2.0 * going);
                    float z = 1.0 - rank / share;
                    float full = widest * lerp(sqrt(z), z, evenly), grown = clamp((life + 1.0) * 2.0, 0.0, 1.0), R = full * grown * (1.0 - 0.45 * going);
                    if (R < 15.0 || going > 0.97) continue;
                    float own = tall * (0.75 + 0.55 * frac(its.g * 5.13 + its.r * 1.71));
                    // (no heap round: each an oval of its own, up to one and a third times as long as it is wide, lying its own way;
                    // out of place by up to a quarter of a cell each way)
                    float2 astray = its.ba - 0.5;
                    float2 d = uv - (float2(cell.x + 0.5 * cell.y, 0.9 * cell.y) + astray * 0.5);
                    float oval = 1.0 + 0.16 * frac(its.b * 5.1 + its.a * 2.3);
                    float2 way = normalize(astray + float2(0.003, 0.001));
                    d = way * (dot(d, way) / oval) + float2(-way.y, way.x) * (dot(d, float2(-way.y, way.x)) * oval);
                    float t = dot(d, tilt);
                    float out_ = sqrt(dot(d, d) + t * t) * C;
                    gaps[k] = out_ - R;
                    found[k] = float4(R, 2.0 * full * grown * own, life, 40.0 * its.a + going * 0.9 * 2.0 * full * grown * own);
                    gapBest = min(gapBest, gaps[k]);
                    // (the gap to the nearest edge, with a rounded join where two heaps' edges are near one another)
                    gapSoft = -larger(-gapSoft, -gaps[k], join);
                } }
                // (whose numbers: the nearest heap's, and those of any other as near within the join's width, by how near. But how
                // high the base is cut: of those, the lower counts for far more. A heap that is going, standing in a larger one that
                // is not, otherwise cut a hollow up into it as high as its own risen base: caves in the flanks of towers.)
                float sumLow = 0.0;
                { [loop] for (int k = 0; k < 6; k++)
                {
                    float w = max(1.0 - (gaps[k] - gapBest) / max(join, 1.0), 0.0);
                    w *= w;
                    float low = w * exp(-found[k].w / 60.0);
                    sum += w; sumR += w * found[k].x; sumH += w * found[k].y; sumLife += w * found[k].z; sumRaised += low * found[k].w; sumLow += low;
                } }
                raisedOut = 0.0;
                if (sum <= 0.0 || sumR <= 0.0) return float4(0.5 * C, 0.0, 0.0, 0.0);
                float R = sumR / sum;
                raisedOut = sumRaised / max(sumLow, 1e-20);
                return float4(max(R + min(gapSoft, 0.5 * C), 0.0), R, sumH / sum, sumLife / sum);
            }

            // What the two layers' shapes go by, worked out once for each step of the walk (formStep) and used by every asking of that
            // step: the step's own, the light's from it, the search for a cloud's edge. [0] the low layer, [1] the middle one.
            // (Until the evening of 2026-10-12 all of it was worked out afresh at every asking: the new shapes then cost one and a half
            // times to twice what the old had, and by count they asked no more often. It was the arithmetic.)
            static float4 formKind[2];    // x: how much of it is separate heaps; y: how much one even sheet; z: how much of the layer's depth it uses (m); w: how far a heap leans downwind for each metre up
            static float4 formSize[2];    // x: how tall a heap for its width; y, z: the lattice's two sizes (cells to a length of the noise); w: how much of the layer's cloud is the second size's
            static float4 formHeap[2];    // x: how many of the lattice's points have a heap; y: the largest's half-width (cells); z: 0 if most are near the largest, 1 if of every size alike; w: the join where two meet (cells)
            static float4 formBank[2];    // x: how much of a bank there is (0: none); y: the level of the banks' pattern above which is bank; z: how much the heaps that reach the lid spread under it; w: how much of a sheet there is
            static float4 formMore[2];    // x: the cover the heaps go by; y: the level of the smooth noise above which is sheet; z: the layer's depth (m); w: its base (m above the radius)
            static float3 formWay = ((float3)(0.0));   // which way the line now being walked goes (a unit, the planet's axes): the line of sight, or towards the sun for the light's steps; nothing: not told
            static float formFace, formOther, formMine;   // which face of the cube the lattice lies on here (0, 1, 2: by which of the three directions is most upright), the next nearest, and this one's share
            static float formChoice = -1.0;    // how wide a pixel of the walk's picture is at the place asked about (m), by which the near shapes or the far field draw it (formOne), for everything asked from the walk's step there and for the shadow's map; nothing: each asking's own
            static float formFade = 1.0;       // (formOne) how much of the near shapes there is: 1 all; less, each shrunk towards nothing as the far field takes over
            static float farGrow = 1.0;        // (formOne) how much of the far field there is: 1 all; less, only its strongest parts, as it takes over
            static float formClimbs = 2.0;     // how fast the line of sight climbs at the step now being walked (the sine of its angle above level there); 2: not told

            // How far along the line being walked a level is that lies `d` metres below the place (`down`) or above it, the place
            // being h above the planet's radius: the planet is round, so a line that goes down at first comes up again further on.
            // Not told which way the line goes: `d` itself, the distance straight up or down, which is never too far.
            // (What it is for: a place over the top of everything a layer can hold, or under its base, is in clear air, and the walk
            // may stride as far as that air goes along its own line. Told only how far it is straight up or down, as until the
            // evening of 2026-10-12, a line that came down on a layer at three degrees crept up on it: each stride a part of what
            // height was left, which along such a line is a nineteenth of the way. Counted: half as many steps again as the old
            // shapes took over a fair sky seen from just above it.)
            float alongTo(float d, float h, bool down)
            {
                if (formClimbs > 1.5) return d;
                float c = formClimbs, Rh = _CloudCam.w + h;
                float two = down ? 2.0 * d - d * d / Rh : 2.0 * d + d * d / Rh;
                if (!down) return two / (c + sqrt(c * c + two / Rh));
                float left = c * c - two / Rh;
                return c < 0.0 && left >= 0.0 ? two / (-c + sqrt(left)) : 1e6;
            }

            // (a place's two numbers across a face, and the upright's, with the third after them)
            float3 faceOf(float3 v, float face) { return face < 0.5 ? v.yzx : face < 1.5 ? v.zxy : v; }
            // (and back again)
            float3 unfaceOf(float3 v, float face) { return face < 0.5 ? v.zxy : face < 1.5 ? v.yzx : v; }

            // The level of the smooth noise above which a share `cover` of the sky lies. (It is spread like a bell round a half,
            // 0.083 wide: measured, see the lab.)
            float formLevel(float cover)
            {
                float x = clamp(cover, 0.003, 0.997);
                return 0.5 - 0.083 * log(x / (1.0 - x)) * 0.6;
            }

            static float4 formRaw[2];     // x: the cover as the weather has it; y: its logarithm to the base two; z: 1 if there is any of this layer

            // Once for each reading of the weather along a line (every 1200 m): what both layers' shapes go by that the weather alone
            // decides. p: the place (from the planet's centre, the scene's axes).
            void formWeather(float3 p, float4 wa, float4 wb, float4 wc)
            {
                float N = _CloudNoise.w;
                float3 pf = float3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
                formUp = pf / max(length(pf), 1.0);
                // (as fine as what is kept from frame to frame, where that is finer than the walk's own picture)
                // (where what is kept is finer than the walk's picture (z), how much finer a kept dot is: the walk's w, 0.5 at half size and
                // 0.25 at a quarter; nought, as before 2026-10-18, is 0.5. The shadow's map gives its own w, for go 4.)
                formPer = 2.0 * length(_CloudRight.xyz) / max(_CloudHigh.w, 1.0) * (_CloudJitter.z > 0.5 ? (_CloudJitter.w > 0.0 ? _CloudJitter.w : 0.5) : 1.0);
                // (The face of the cube: the two of the three directions of the noise's space that lie most nearly along the ground
                // here. Near where two faces meet, within a degree or so, both lattices are there, each with a share of the heaps
                // that falls to a half at the meeting: across it the two change places, and no heap is cut.)
                float3 au = abs(formUp);
                float most = au.z, next = max(au.x, au.y);
                formFace = 2.0; formOther = au.x > au.y ? 0.0 : 1.0;
                if (au.x >= au.y && au.x >= au.z) { formFace = 0.0; most = au.x; next = max(au.y, au.z); formOther = au.z > au.y ? 2.0 : 1.0; }
                else if (au.y >= au.z) { formFace = 1.0; most = au.y; next = max(au.x, au.z); formOther = au.z > au.x ? 2.0 : 0.0; }
                formMine = 0.5 + 0.5 * smoothstep(0.0, 0.025, most - next);
                { [loop] for (int L = 0; L < 2; L++)
                {
                    float cover = L == 0 ? wa.x : wb.a, kind = L == 0 ? wa.y : wc.x, base = L == 0 ? wa.z : wc.y, top = L == 0 ? wa.w : wc.z;
                    top = max(top, base + 50.0);
                    cover = clamp(cover * _CloudLook.z, 0.0, 1.0);
                    formRaw[L] = float4(cover, log2(max(cover, 1e-4)), cover > (L == 0 ? 0.001 : 0.004) ? 1.0 : 0.0, smoothstep(0.5, 0.68, kind));
                    float deep = top - base;
                    // ---- what kind: how much of it is separate heaps, and how much one even sheet; the rest is pads
                    float heapy = smoothstep(0.34, 0.46, kind), sheet = 1.0 - smoothstep(0.12, 0.24, kind);
                    // (a lumpy layer is a few hundred metres thick whatever the depth of air it is given; a sheet fills what it is given)
                    float used = lerp(lerp(min(deep, L == 1 ? 190.0 : 560.0), deep, sheet), deep, heapy);
                    float lean = lerp(0.22, 0.12, smoothstep(1500.0, 4000.0, deep));
                    formKind[L] = float4(heapy, sheet, used, lean);
                    // (how tall a heap for its width (its height over its whole width): wider than tall, nearly twice as wide in fair
                    // weather's shallow layer and still half as wide again in a deep one; taller than wide only where they tower. At
                    // as tall as wide in a deep layer, as it was until 2026-10-15, a mostly cloudy sky was a field of pillars.)
                    float tall = lerp(0.45, 0.7, smoothstep(350.0, 1600.0, deep)) * lerp(1.0, 2.4, smoothstep(0.5, 0.68, kind));
                    // ---- the lattice's size. For heaps: such that the widest just reach the layer's top. For pads: a kilometre and a
                    // quarter to a cell low down, under half of that in the middle sky, where they are small and many. It comes in set
                    // sizes, each half as large again as the last (3, 4, 6, 9, 14, 20 or 30 cells to a length of the noise: a lattice
                    // whose size slid with the layer's depth would slide its heaps over the ground wherever the depth changed); where
                    // the size asked for is in the upper third of the way from one to the next, both are there, the one's heaps going
                    // as the other's come.
                    // (The middle sky's pads on a set size of their own, 20 to a length of the noise (450 m): at 480 m, as they were, the
                    // size asked for fell between two, and both were always there, each with a share of the pads. Its sheets as they
                    // were: the two sizes' readings of the wide noise taken together are what keep a sheet's holes soft, and with one,
                    // altostratus seen aslant was a field of broken crystal.)
                    float cells = clamp(lerp(N / (L == 1 ? lerp(480.0, 430.0, step(0.5, (1.0 - heapy) * (1.0 - sheet))) : 1250.0), N * tall / (1.05 * deep), heapy), 3.0, 30.0);
                    float rung = log(cells / 3.0) / log(1.4678);
                    float low = min(floor(rung), 5.0);
                    formSize[L] = float4(tall, floor(3.0 * pow(1.4678, low) + 0.5), floor(3.0 * pow(1.4678, low + 1.0) + 0.5), smoothstep(0.67, 1.0, rung - low));
                    // (how much of a bank there is, how much the heaps that reach the layer's top spread out under it (on the usual
                    // day, in a shallow layer; not where they tower, whose tops are far above); the rest by the step)
                    // (Where the heaps tower there is a bank too, a little less of one: towers stand up out of a deck of lesser
                    // heaps that has one level base, they do not stand each by itself on a base of its own with clear sky between,
                    // as they were drawn until the evening of 2026-10-12: skittles.)
                    formBank[L] = float4(heapy * _CloudHabit.x * lerp(1.0, 0.8, smoothstep(1500.0, 2600.0, deep)), 0.5, heapy * _CloudHabit.x * (1.0 - smoothstep(1400.0, 2600.0, deep)), sheet);
                    formMore[L] = float4(cover, 0.5, deep, base);
                } }
            }

            // (a straight ramp from nothing at a to all at b: where a smooth one is not needed, and this is worked out at every step)
            float ramp(float a, float b, float x) { return clamp((x - a) / (b - a), 0.0, 1.0); }

            // Once a step: what the gatherings and the streets make of each layer's cover there, and what goes by that.
            // How far the camera is above all the clouds, for what is drawn otherwise from there (formStep, formOne, the heaps' map, the
            // lumps' reach, the walk's own shade on the ground, the shadow's map): 0 within 100 m over the highest a top can be, 1 from
            // 1100 m over it. Taken at a step 500 m over it, as until 2026-10-18, it changed them all at once there: climbing or coming down
            // through that height, the heaps at middle distance went or came and the gaps between them opened, in one frame.
            float aboveness() { return smoothstep(_CloudShell.y + 100.0, _CloudShell.y + 1100.0, _CloudHigh.x); }

            void formStep(float3 q, float4 wm, float t)
            {
                // (How far off. Past 100 km the lattice's heaps are fewer, the smallest gone first, and past 130 km there are none: the
                // banks, which are what is seen of a sky from afar, stay, and are given the heaps' share of the cover; a lumpy layer far
                // off is its sheet. Until 2026-10-18 that was from 15 to 26 km, because a line of sight running level through the layer
                // for 90 km worked every one of its 88 steps' heaps out in full (in the game, 58 frames a second among fair-weather heaps
                // became 38); since the heaps are made beforehand they cost little there (in the lab 1-8 % of the walk in six views, the
                // same noise in flight), and a sky whose heaps end 26 km off and lie flat beyond reads as small: from the ground and from
                // cloud height a real field of heaps goes on to the horizon, smaller and paler (research/rendering/notes/cloud-scale.md).)
                float away = smoothstep(100000.0, 130000.0, t);
                formAway = away;
                // (Seen from above all the clouds, from high in the air or from orbit, `t` is how far the line has run inside the
                // shell, not from the camera: a line from orbit crosses the layer steeply, in a few kilometres and a few steps, and the
                // heaps it meets 250 km off are as cheap as near ones and a few pixels wide. By the distance from the camera, as it
                // was, a cumulus sky had no heaps at all from orbit, and only the banks' patches were left of it: "the clouds from
                // space look kind of crap" (the user, 2026-10-13). Where such a line does run far along the layer (towards the
                // planet's rim), the heaps there go as before, and from above, their layer is a sheet there, holed as much as it is
                // covered: heaps far off along a layer seen from above are seen one behind another, run together.)
                float fromAbove = aboveness();
                // (What gatherings and streets do to the cover. As a power, not a factor: a gathering thickens the cover towards
                // all of the sky but never to all of it. The low heaps take both; the banks, and the middle sky, the gatherings alone.)
                float streets = max(streetsAt(q, wm), 0.2);
                { [loop] for (int L = 0; L < 2; L++)
                {
                    if (formRaw[L].z < 0.5) continue;
                    float raw = formRaw[L].x, heapy = formKind[L].x, sheet = formKind[L].y;
                    float gathered = max(wm.x / streets, 0.05), both = max(L == 0 ? wm.x : wm.x / streets, 0.05);
                    float coverBanks = lerp(exp2(formRaw[L].y / gathered), raw, raw * raw);
                    float cover = lerp(exp2(formRaw[L].y / both), raw, raw * raw);
                    // ---- rafts (2026-10-17). Pads (stratocumulus low, altocumulus in the middle sky) are not strewn evenly over the
                    // whole sky: photographs of them from the ground (the mackerel skies among Wikimedia Commons' altocumulus) show the
                    // cloudlets packed into rafts kilometres to tens of kilometres across, parted by narrow cracks, with clear sky
                    // between the rafts and only a stray tuft in it. Strewn evenly, as they were, a mostly cloudy sky was hundreds of
                    // separate round puffs alike: cotton balls. So: a pattern over the ground says where the rafts are (its strongest
                    // part, as much of the sky as the cover asks, less the cracks); in a raft the pads' cover is three quarters (all of
                    // it where the whole sky is covered), outside one a thirtieth of the sky's, and across a raft's edge, over a
                    // kilometre or so, from the one to the other: a ragged edge of wider cracks and smaller cloudlets. (The pattern is
                    // read as the banks' is (see formOneNear), at its own place for each layer, and finer in the middle sky.)
                    float rafty = (1.0 - heapy) * (1.0 - sheet);
                    { [loop] for (int once = rafty > 0.01 ? 0 : 1; once < 1; once++)
                    {
                        float k = L == 1 ? 0.41 : 0.27;
                        float4 rp = SampleLod(_CloudShape, q.zxy * k + (L == 1 ? float3(0.13, 0.77, 0.41) : float3(0.83, 0.21, 0.57)), 2.0 * _CloudHaze.x);
                        float strength = clamp((rp.r - rp.g) / max(1.0 - rp.g, 0.02), 0.0, 1.0);
                        float most_ = clamp(cover / 0.85, 0.003, 0.997);
                        // (metres inside the raft's edge: the pattern is about 0.55 steeper per length of the noise read at 0.27)
                        float within = (strength - (0.5 - 0.045 * log(most_ / (1.0 - most_)))) * (_CloudNoise.w * 0.27 / (0.55 * k));
                        cover = lerp(cover, lerp(0.03 * cover, lerp(0.74, 0.95, ramp(0.6, 1.0, raw)), smoothstep(-600.0, 600.0, within)), rafty);
                    } }
                    // (how many of the points have a heap, how large the largest, how alike in size: a sky of heaps a tenth covered is a
                    // few small ones, all different; half covered, most points have one and they are large and crowd together. Never
                    // all of the points: a sky with a heap at every point of the lattice shows the lattice, heaps in ranks one behind
                    // another; with one in five of them missing, by chance, it does not. Pads are at nearly all of them and much alike.)
                    float share = lerp(min(cover * 1.7, 0.96), min(cover * 3.2, 0.8), heapy);
                    float widest = lerp(0.40, lerp(0.32, 0.40, ramp(0.1, 0.45, cover)), heapy);
                    float evenly = lerp(0.5, ramp(0.35, 1.0, lerp(1.25, 0.35, ramp(0.1, 0.5, cover))), heapy);
                    // (how wide the join where two meet: wider the more of the sky is covered, when neighbours run together into one mass)
                    float join = lerp(lerp(0.1, 0.34, ramp(0.45, 0.9, cover)), lerp(0.06, 0.2, ramp(0.3, 0.6, cover)), heapy);
                    // ---- the banks (see formOne): where their pattern's level stands. The lattice's heaps the same in a bank and out of
                    // it, few and small: in it they are its turrets, out of it the small heaps that stand alone. And the banks placed
                    // by the cover without the streets in it: the streets are rows of more and of less cover three kilometres apart,
                    // for the heaps to stand in; a bank's edge moves four kilometres for a tenth more cover, and with the streets in
                    // what places it, the edge of every bank was drawn out along each street: a comb.
                    if (formBank[L].x > 0.01)
                    {
                        share = min(share, min(cover * 1.1, 0.8)); widest = min(widest, lerp(0.3, 0.38, ramp(0.1, 0.45, cover)));
                        float most_ = clamp(coverBanks * lerp(0.85, 1.0, away), 0.003, 0.997);
                        formBank[L].y = 0.5 - 0.045 * log(most_ / (1.0 - most_));
                    }
                    formHeap[L] = float4(share * (1.0 - away), widest, evenly, join);
                    // (how much of a sheet there is: all of one in a sheet, and under a lumpy layer that covers most of the sky a
                    // thinner one that joins the pads; and the level of the smooth noise above which it is)
                    // (none under pads, which are cells (padsCells) and cover the sky themselves, with cracks of open sky between them
                    // that close as it fills: under a sheet joining them, a deck of them was a crumpled white sheet)
                    float fill = max(max(sheet, (1.0 - heapy) * (1.0 - rafty) * ramp(0.72, 0.97, cover)), (1.0 - heapy * (1.0 - fromAbove)) * away);
                    formBank[L].w = fill;
                    formMore[L].x = cover;
                    if (fill > 0.001) formMore[L].y = formLevel(cover);
                } }
            }

            // One layer's cloud at a place, with the lattice at one size (`perF` cells to a length of the noise) and `weight` of the
            // layer's cloud given to that size (see formStep: where the layer's depth asks for a size between two, both are there, each
            // with its share). Returns how much cloud (seeking: 1 if there may be any within a stride and a half); given out: how
            // near any of it can be (as heapsNear), how far inside its surface the place is, and how much of the sky is seen.
            //
            // The kinds. Heaps (a kind of a half and more): separate domes, as above. A lumpy layer (stratocumulus, about a quarter;
            // the dappled middle sky, altocumulus, the same but small): the same domes pressed flat under a lid a few hundred metres
            // above their base, wide for their depth, at nearly every point of the lattice and run together where they touch: pads
            // with cracks between, the cracks closing as the sky fills. A sheet (stratus and the rain sheet, a tenth and less; the veil
            // of the middle sky): one slab from base to top, thinner here and thicker there, with holes only where the sky is not
            // all covered, and soft everywhere.
            // Where the banks gather (2026-10-19). The banks' own pattern has one size, a few kilometres, and at any cover its banks stood as
            // evenly as a pattern of one size does: from orbit every sky was an even popcorn of banks, where the weather's map had clumped it
            // (the owner: "The cloud coverage over the whole world is too even and evenly spaced out"). Real cloud comes in clumps within
            // clumps, its sizes one power law from a tenth of a kilometre to some thousands (Wood and Field 2011; research/rendering/notes/
            // cloud-climatology.md). So two larger patterns are laid over the banks' own, some 15 and 80 km across, each with more say than
            // the last (0.8 and 1.4 times the banks' own), the three spread back to the banks' own spread together, so that as much of the
            // sky is bank as before; and read the same where the near shapes and the far field read the banks, so that coming down through
            // the air they stay where they were. (In the lab from 250 km straight down, heaps: the cover 43.7 % of the picture against
            // 43.6 at 0.3, 68.9 against 69.5 at 0.6; the banks in fields with clear between, of several sizes.)
            float bankGather(float3 col)
            {
                float4 g = SampleLod(_CloudShape, col.zxy * 0.11 + float3(0.29, 0.67, 0.41), 2.0 * _CloudHaze.x);
                float4 h = SampleLod(_CloudShape, col.xzy * 0.6 + float3(0.53, 0.19, 0.71), 2.0 * _CloudHaze.x);
                return 1.4 * (clamp((g.r - g.g) / max(1.0 - g.g, 0.02), 0.0, 1.0) - 0.5) + 0.8 * (clamp((h.r - h.g) / max(1.0 - h.g, 0.02), 0.0, 1.0) - 0.5);
            }
            // (the banks' pattern where it is read, with the larger two: how strongly a place is bank)
            float bankStrength(float4 broad, float3 col) { return 0.5 + (clamp((broad.r - broad.g) / max(1.0 - broad.g, 0.02), 0.0, 1.0) - 0.5 + bankGather(col)) / 1.8974; }

            // ---- The clouds from afar (2026-10-14 evening; the owner: "Clouds and ocean look horrible from far away").
            // What a sky of clouds is from tens of kilometres up and from orbit, as the photographs from the space station and from
            // airliners show it: fields. Heaps a kilometre or two across stand in clumps, the clumps run together into banks and rows
            // tens of kilometres long with clear water between; a lumpy layer is a deck cracked into cells; a sheet is even, holed
            // where the sky is not all covered. What was drawn instead: the lattice's heaps, each a few pixels wide, as scattered round
            // dots and floes on the sea, and beyond 15 to 26 km of the layer the sheet that stood in for them, which from above was a
            // frosted rim round a ring of dots (the step from one to the other at a set distance into the layer: a circle round the
            // point below the camera).
            // So from so far that a pixel is wider than a small heap, and from above all the clouds wherever the lattice's heaps give
            // way with distance, the layer is drawn as the height its top stands at over each place: patterns at the sizes of the
            // photographs' clumps and banks, bent so that no clump is round, the sky covered as much as the weather says. The walk
            // lights it as it does the rest: tops bright to the sun, sides and cracks shaded, its shadow on the sea below. How many
            // metres inside that surface a place is (outside: less than nothing, about as far as the nearest cloud may be).

            float farInside(int L, float3 col, float above, float used, float heapy, float sheet, float cover, float pixel, bool fine)
            {
                // (the large noise's cubes are 9 km to a repeat in 128 dots: read no finer than a pixel)
                float mip = max(log2(pixel / 70.0), 0.0) * _CloudHaze.x;
                // (bent over a kilometre or so)
                float4 warp = SampleLod(_CloudShape, col.zxy * 0.47 + float3(0.23, 0.59, 0.11), max(mip - 1.0, 0.0));
                float3 c = col + (warp.gba - 0.48) * 0.16;
                // (where the clumps gather: patterns 33 and 108 km across, strongest where both are. The large noise's first channel
                // lies between 0.74 and 0.92 nearly everywhere: spread over that, else a gathering is everywhere and the field from
                // orbit an even popcorn over the whole disc)
                // (read where the banks are read, unbent: one reading serves both; the bend is nothing at this size)
                float4 broad = SampleLod(_CloudShape, col.yzx * 0.27 + float3(0.61, 0.17, 0.43), 2.0 * _CloudHaze.x);
                float4 broader = SampleLod(_CloudShape, c.xzy * 0.083 + float3(0.37, 0.83, 0.29), 2.0 * _CloudHaze.x);
                float gather = smoothstep(0.77, 0.9, broad.r) * 0.6 + smoothstep(0.76, 0.88, broader.r) * 0.4;
                // (the clumps: the cubes' cells, 2.2, 1.1 and 0.56 km across, each bright at its middle; which size has most say
                // changes over ten kilometres or so, so that one stretch has large heaps and the next small ones; and each clump's
                // outline is bitten into by a pattern of its own size, that none is a round dot)
                // (each reading only where its kind is: what the far field costs is in how many it reads at every step)
                float clumps = 0.48, bankCells = 0.5, biteR = 0.5, deck = 0.5;
                { [loop] for (int once = heapy > 0.001 ? 0 : 1; once < 1; once++)
                {
                    float4 s = SampleLod(_CloudShape, c + float3(0.31, 0.77, 0.53), mip);
                    float big = smoothstep(0.35, 0.65, broad.g);
                    clumps = lerp(s.g * 0.3 + s.b * 0.42 + s.a * 0.28, s.g * 0.62 + s.b * 0.28 + s.a * 0.1, big);
                    // (the light's own walk towards the sun asks only for the shape under the bites)
                    { [loop] for (int bites = fine ? 0 : 1; bites < 1; bites++)
                    {
                        float4 bite = SampleLod(_CloudShape, c.zxy * 2.3 + float3(0.71, 0.29, 0.47), mip + 1.0);
                        biteR = bite.r;
                        clumps *= lerp(0.72, 1.18, bite.r * 0.5 + bite.b * 0.5);
                    } }
                    // (and a bank's cells, the clumps' own: its lumps from afar)
                    bankCells = smoothstep(0.36, 0.62, s.g * 0.55 + s.b * 0.3 + s.a * 0.15);
                } }
                { [loop] for (int once = heapy < 0.999 ? 0 : 1; once < 1; once++)
                {
                    // (a deck's cells: the cells' own pattern at 4.5 and 2.2 km, 1 in a cell's middle and nothing at its rim; their
                    // numbers lie close round 0.48: spread out from there, else a deck's cells hardly differ and it is a plain sheet)
                    float4 deckRead = SampleLod(_CloudShape, c * 0.5 + float3(0.13, 0.41, 0.87), max(mip - 1.0, 0.0));
                    deck = smoothstep(0.36, 0.62, deckRead.g * 0.7 + deckRead.b * 0.3);
                    // (and from high up, the cells a deck of stratocumulus is made of seen from orbit: closed cells 10 to 40 km across, thick and
                    // bright in their middles and thinner at their rims, the deck's own small cells within them (Wood and Hartmann 2006; research/
                    // rendering/notes/cloud-climatology.md). The noise's cells read at a seventh of the size, about 16 and 8 km. Only where a pixel
                    // is wider than 30 to 120 m: nearer, the patches of a deck seen from a few kilometres over it lost their lumpy edges to them.)
                    float4 mesoRead = SampleLod(_CloudShape, c * 0.14 + float3(0.57, 0.23, 0.61), max(mip - 2.8, 0.0));
                    float mesoCells = smoothstep(0.36, 0.62, mesoRead.g * 0.75 + mesoRead.b * 0.25);
                    deck = lerp(deck, mesoCells * (0.55 + 0.45 * deck), 0.7 * smoothstep(30.0, 120.0, pixel));
                } }
                // (The banks: the plateaus the usual sky's heaps run together into ("They're like huge plateaus": the owner,
                // 2026-10-12), placed exactly where the near shapes place them (formOne: the same pattern, at the same level for the
                // cover), so that coming down through the air they stay where they were. In a bank the clumps are its lumpy top and
                // it is cloud throughout; between banks only the strongest clumps stand, scattered.)
                float4 B = formBank[L];
                float inBank = 0.0;
                { [loop] for (int once = B.x > 0.01 ? 0 : 1; once < 1; once++)
                {
                    float strength = bankStrength(broad, col);
                    float within = (strength - B.y) * (_CloudNoise.w / 0.55) - (1.0 - B.x) * 3000.0;
                    // (its edge ragged by the clumps and by a pattern of their size: drawn by the broad pattern alone, a bank seen
                    // from above had the smooth outline of spilt paint)
                    within += (clumps - 0.48) * 2500.0 + (biteR - 0.5) * 1200.0;
                    inBank = smoothstep(-300.0, 300.0, within);
                } }
                // (One number a place, from 0 to about 1, highest where the sky is fullest: for heaps, clumps gathered, and in a bank
                // its cells; for a lumpy layer the deck's cells; for a sheet the broad pattern alone. A bank or a deck seen from above
                // is not one white plain (as drawn at first: spilt paint, a featureless sheet) but cells, thick and bright in their
                // middles and thinner at their rims, cracked open between them where the sky is less than all covered: what a deck
                // of stratocumulus is from the air and from orbit.)
                // (Between the banks no more cloud than the near shapes have there, a few small heaps: a sky of 0.4's. With the whole sky's
                // cover there, as it was, a sky of 0.8 was nine tenths covered from afar where the near shapes covered two thirds, and from
                // above the gaps between the banks filled in round a ring about the point under the camera.)
                // (Where the sky is all but covered, all of it cloud. The far field's level for the cover, and the room it clears between the
                // banks for heaps by themselves, left holes in every kind at a cover of 1 (an eighth of the sky in heaps, a third under the
                // thin rims of a deck's cells, seen from 80 km in the lab), and the weather's overcast was a broken sky from orbit: under 0.8
                // as before; from there to 0.98 no room cleared, the level lowered, and the rims of the cells as thick as half their middles.)
                float fillAll = smoothstep(0.8, 0.98, cover);
                float lone = B.x * max(lerp(0.78, 0.2, min(cover, 0.4)) - lerp(0.78, 0.2, cover), 0.0) * (1.0 - fillAll);
                float heaps_ = lerp(clumps * lerp(0.6, 1.0, gather) - lone, 0.42 + 0.6 * bankCells, inBank);
                float lumpy = (0.3 + 0.75 * deck) * lerp(0.85, 1.08, gather);
                // (a sheet's: the broad pattern spread over the field's span, and the finer bend's own first channel in it: from
                // lerp(0.35, 0.75, broad.r), which spans only 0.61 to 0.68, where its holes fell was the weather's map's alone, its
                // dots taken between in straight lines, and fog seen from the air was holed with polygons)
                float even = clamp(0.5 + (broad.r - 0.745) * 2.2 + (warp.r - 0.745) * 0.9, 0.0, 1.0) * lerp(0.85, 1.1, gather);
                float field = lerp(lerp(lumpy, even, sheet), heaps_, heapy);
                // (how dense the cloud is here against its middles: thinner at a cell's rim, which from above is greyer)
                farThin = lerp(lerp(lerp(lerp(0.12, 0.4, fillAll), 1.0, deck), lerp(0.75, 1.0, broad.r), sheet), lerp(1.0, lerp(lerp(0.2, 0.6, fillAll), 1.0, bankCells), inBank), heapy);
                // (the level above which is cloud, for so much of the sky to be covered: the field lies mostly between 0.25 and 0.75)
                // (and while it takes over from the near shapes, only its strongest parts: see formOne)
                // (Heaps' own: their field lies higher than that, and at the general level four fifths of the world from orbit was cloud at
                // a cover of 0.3 where the near shapes cover a quarter (the main chat's "uniform fleece", 2026-10-18). Matched to what the
                // near shapes cover seen straight down from 250 km, at 0.3 and 0.6 (experiments/heaps-from-orbit.md).)
                float level = lerp(0.98, lerp(lerp(0.78, 0.2, clamp(cover, 0.0, 1.0)), lerp(0.565, 0.452, clamp(cover, 0.0, 1.0)), heapy) - 0.3 * fillAll, farGrow);
                float t = (field - level) / max(1.0 - level, 0.05);
                farRim = t * (1.0 - level) * 600.0;
                // (the top: domes over heaps, a deck nearly level with its cells raised a little, a sheet level)
                float top = used * lerp(lerp(0.62 + 0.3 * sqrt(max(t, 0.0)), 0.75 + 0.2 * t, sheet), 0.3 + 0.7 * pow(clamp(t, 0.0, 1.0), 0.55), heapy);
                // (its full height only some way in from its edge, and lower towards it: a heap a dome as wide as it is tall, a deck and a
                // sheet with a shoulder some hundreds of metres wide. At full height right to the edge, as it was, every patch had upright
                // walls: from above floes, shards and a deck of ice, and from the ground the far heaps a palisade of white pillars along
                // the horizon.)
                top *= sqrt(clamp(t * (1.0 - level) * 600.0 / lerp(lerp(300.0, 400.0, sheet), max(350.0, 0.8 * top), heapy), 0.0, 1.0));
                // (and on it lumps of 250, 125 and 62 m, the small noise's cells, as far as a pixel can show them: from ten kilometres
                // above, a cauliflower and not a smooth floe; from orbit they are below a pixel and the top is smooth)
                float fineMip = log2(pixel / 15.6);
                { [loop] for (int read = fineMip < 4.0 && fine ? 0 : 1; read < 1; read++)
                {
                    float3 d = SampleLod(_CloudDetail, c * 36.0 + float3(0.0, 0.0, _CloudDrift.w), max(fineMip - 2.0, 0.0) * _CloudHaze.x).rgb;
                    top += dot(d - 0.48, float3(1.0, 0.5, 0.25)) * lerp(lerp(90.0, 30.0, sheet), 160.0, heapy) * (1.0 - smoothstep(2.5, 4.0, fineMip)) * clamp(t * 4.0, 0.0, 1.0);
                } }
                // (outside: no further off than the field's slope allows at its steepest, about a whole over 600 m; and the walk is
                // never told more than 300 m: told more, a line from above strode clean through a layer 400 m thick and its clouds)
                if (t <= 0.0) return max(t * (1.0 - level) * 600.0, -300.0) - max(above - top, 0.0);
                return min(above, top - above);
            }

            float formOneNear(int L, float perF, float weight, float3 q, float3 col, float above, float4 wm, bool fine, float lod, bool seek, out float nearOut, out float insideOut, out float openOut, out float smoothOut);

            float formOne(int L, float perF, float weight, float3 q, float3 col, float above, float4 wm, bool fine, float lod, bool seek, out float nearOut, out float insideOut, out float openOut, out float smoothOut)
            {
                // (how much of the far field there is here: all of it where a pixel is wider than 130 m, none under 45; and from above
                // all the clouds, wherever the lattice's heaps give way with distance)
                float pixel = 25000.0 * exp2(lod) * formPer;
                // (Which of the two draws a place goes by how wide a pixel of the walk's picture is there, the same for all that is asked
                // about it: the walk's step, its light's steps and far looks towards the sun, which read the noise a half and a whole size
                // coarser and with that chose the far field sooner (heaps drawn by the near shapes were then shaded by the far field's
                // clumps beside them: blue mottling over the banks), and the shadow's map, which was told of a picture of its own 512 dots
                // and chose the far field everywhere (the far field's shadows under heaps drawn by the near shapes).)
                float pixelChoice = formChoice > 0.0 ? formChoice : pixel;
                // (from above all the clouds sooner: their banks seen from over them, lumps damped flat under the layer's lid, were
                // smooth white floes on the sea from ten kilometres up)
                float fromAbove = aboveness();
                // (from ten kilometres above them the lattice's heaps were still round dots, polka dots on the sea: the far field, with its
                // lumps, takes over from a few kilometres above the clouds)
                // (And from below as well, as the lattice's heaps give way with distance, 15 to 26 km off. From below it came in only
                // where a pixel is 45 m wide, some fifty kilometres off and more: between, a sky of heaps had no clouds at all, and
                // from a craft under a deck the deck ended in a straight line a little above level, with clear sky under it to the
                // horizon: "a strip of light blue in the distance ... that rendered in front of ... the clouds" (the owner, 2026-10-15).)
                // (Heaps from above: from where a pixel is 30 to 60 m wide, 19 to 37 km off on a screen 720 lines tall. From a few metres,
                // as the rest are, from 4 km up the far field drew them from 3 km off: flat slabs with upright walls, cut along the cells
                // of its pattern into shards and square-cornered holes (the main chat, 2026-10-18), where the near shapes are heaps and
                // banks as the photographs show them (research/rendering/experiments/clouds-from-above.md). From 20 to 26 m, as it then
                // was, the banks 12 to 30 km off seen from 1.5 to 10 km above them were still the far field's blocks with flat tops and
                // upright sides, and its shards from higher up (experiments/heaps-from-above.md): from 30 m, the near shapes' rounded
                // heaps with their shade, for a fifth to three tenths more of the walk from a few kilometres above the heaps and next to
                // nothing more from ten. Over a band, where both are worked out.)
                float farW = max(max(smoothstep(45.0, 130.0, pixelChoice), formAway), fromAbove * lerp(smoothstep(4.0, 12.0, pixelChoice), smoothstep(30.0, 60.0, pixelChoice), formKind[L].x));
                farW *= 1.0 - step(0.5, _CloudForm.w);
                // (Handed over as one becoming the other: the near shapes shrink away as the far field grows from its strongest parts, each
                // as solid as it is, and a place is the larger of the two. Mixed as amounts of cloud, as they were, half of each was a veil
                // of thin cloud over both, and over the clear sky between heaps a fog of the far field's.)
                formFade = clamp((1.0 - farW) * 1.6, 0.0, 1.0); farGrow = clamp(farW * 1.6, 0.0, 1.0);
                float cNear = 0.0, nearNear = -1e9, insideNear = -1e9, openNear = -1.0, smoothNear = -1e9;
                { [loop] for (int once = farW < 0.999 ? 0 : 1; once < 1; once++)
                    cNear = formOneNear(L, perF, weight, q, col, above, wm, fine, lod, seek, nearNear, insideNear, openNear, smoothNear); }
                nearOut = nearNear; insideOut = insideNear; openOut = openNear; smoothOut = smoothNear;
                if (farW < 0.001) return cNear;
                float4 K = formKind[L], M = formMore[L];
                float heapy = K.x, sheet = K.y, used = K.z;
                float fi = farInside(L, col, above, used, heapy, sheet, M.x, pixel, fine) * weight + (weight - 1.0) * 400.0;
                float stride = 375.0 * exp2(lod);
                if (seek) return max(cNear, fi + 0.3 * used + 1.5 * stride > 0.0 ? 1.0 : 0.0);
                float nearFar = max((fi + 6.0 + 0.6 * pixel) * 0.7, -300.0);
                float edge = max(lerp(lerp(26.0, 110.0, sheet), 10.0, heapy), 1.2 * pixel);
                // (Softened by a pixel's width across its outline over the ground, where a pixel can fall half on it; and through it by the
                // edge of a slab as seen close. Softened by the pixel through it, as it was, a clump some hundreds of metres thick never came
                // to its full density from far off, where a pixel is wider than that: from orbit every clump's thinner part, and so most of
                // the world, was a grey veil to see through, under a popcorn of the thicker parts.)
                float cFar = nearFar < 0.0 ? 0.0 : clamp(farRim / edge + 0.5, 0.0, 1.0) * clamp(fi / 40.0 + 0.5, 0.0, 1.0) * clamp(lerp(0.15, 0.35, heapy) + above / lerp(lerp(90.0, 200.0, sheet), 60.0, heapy), 0.0, 1.0) * farThin
                           * (L == 1 ? lerp(0.6, 1.0, max(sheet, heapy)) : 1.0);     // (the middle sky's pads as thin far off as near: see formOneNear)
                nearOut = farW > 0.999 ? nearFar : max(nearNear, nearFar);
                if (fi > insideNear) { insideOut = fi; smoothOut = fi; openOut = clamp(0.3 + 0.9 * above / max(used, 60.0), 0.0, 1.0); }
                return max(cNear, cFar);
            }

            float formOneNear(int L, float perF, float weight, float3 q, float3 col, float above, float4 wm, bool fine, float lod, bool seek, out float nearOut, out float insideOut, out float openOut, out float smoothOut)
            {
                smoothOut = -1e9;
                float N = _CloudNoise.w;
                float4 K = formKind[L], H = formHeap[L], B = formBank[L], M = formMore[L];
                float heapy = K.x, sheet = K.y, used = K.z, deep = M.z, cover = M.x;
                // (each heap a dome over its base, or a column where they tower)
                heapsBuild(formRaw[L].w);
                int per = int(perF + 0.5);
                float C = N / perF;
                // (how wide a pixel of the picture is at this distance, in metres, and a stride of the seeker's (see the walk); `lod`
                // is the distance as a power of two of 25 km)
                float far = exp2(lod), pixel = 25000.0 * far * formPer, stride = 375.0 * far;
                // (How tall for its width. Heaps: as asked, but so that the widest just reach the layer's top and are flat there,
                // within a quarter of what was asked: the lattice comes in set sizes, the layer's depth in any. Pads: about as tall as
                // the layer, the largest pressed flat under its top in their middles and the rest low domes under it: pillows. Made
                // half as tall again as the layer and cut flat by its top, as they were until 2026-10-15, every pad had upright sides
                // and a flat top and bottom: a drum, and a sky of them was the owner's "really tall and pillar-like".)
                float tall = lerp(1.05 * used / (0.8 * C), clamp(1.05 * deep / (0.84 * C), 0.8 * formSize[L].x, 1.25 * formSize[L].x), heapy);
                // (Pads as thick as pads are, whatever share of the layer is sheet: a lumpy layer's lid (formWeather). With the layer's
                // own, where the kind is between pads and sheet (at the edge of a sheet of the middle sky, 0.1 to 0.3), every pad stood as
                // tall as the sheet's slab can be: a kilometre and more of upright column under each cloudlet, in a deep layer.)
                float usedPads = min(used, L == 1 ? 190.0 : 560.0);
                float share = H.x * weight, widest = C * H.y, evenly = H.z, join = C * H.w;
                // (What changes over a kilometre or two: how high the base stands, how thick a sheet is, how lumpy a top. One
                // coarse reading of the large noise for it where the layer is a sheet or pads; where it is heaps, the banks' own
                // reading, below, serves; where it is neither (towers, the day of puffs), none.)
                float softWide = 0.5;
                { [loop] for (int once = heapy < 0.999 ? 0 : 1; once < 1; once++)
                {
                    float4 wide = SampleLod(_CloudShape, col.zxy * (perF / 7.3) + float3(0.29, 0.61, 0.13), 3.4 * _CloudHaze.x);
                    softWide = clamp((wide.r - wide.g) / max(1.0 - wide.g, 0.02), 0.0, 1.0);
                } }
                float size = 0.0, age = 0.0, lift = 0.0, inside = -1e5, leaves = 1e6;
                // ---- the banks. The usual sky is not heaps strewn evenly: "usually the clouds aren't so scattered. They're like huge
                // plateaus" (the user, 2026-10-12), and photographs say the same: the heaps of a fair day stand gathered, run
                // together into banks some kilometres across and several times wider than they are deep, level and grey underneath
                // and lumpy on top, with wide clear sky between and a few small heaps by themselves in it. So: over the ground, a
                // pattern that changes over kilometres says where the banks are (the strongest of it, as much as makes most of the
                // sky's cover); a bank is that patch of the layer filled from the base to some way under the layer's top, with a
                // steep side; the lattice's heaps stand through it. Not on the day of many small puffs, and not where the heaps tower.
                // (How much of a bank there is: all of it on the usual day in a shallow layer, none where the heaps tower. Where
                // there is less than all, the bank is drawn in from its edge, by up to three kilometres, until it is gone: its
                // distances stay distances. Multiplied by a share instead, as they first were (and by this lattice's share of the
                // layer, where two sizes of lattice are both there), a bank with a small share had every distance from it made
                // small, and the lumps, which stand a hundred metres proud, then made cloud of the clear air for kilometres round
                // it: in the game, where the layer's depth changes from place to place, teeth along the edge of every bank.)
                float banks = B.x, bank = -1e5, bankFoot = 0.0;
                { [loop] for (int once = banks > 0.01 ? 0 : 1; once < 1; once++)
                {
                    float4 broad = SampleLod(_CloudShape, col.yzx * 0.27 + float3(0.61, 0.17, 0.43), 2.0 * _CloudHaze.x);
                    float strength = bankStrength(broad, col);
                    // (how far inside the bank's edge, over the ground: the pattern is about 0.55 steeper per length of the noise
                    // at this size)
                    float within = (strength - B.y) * (N / 0.55) - (1.0 - banks) * 3000.0;
                    // (and how far the place is within the bank's own ground, over a few hundred metres at its edge: there the heaps in it stand on
                    // its base, see `under`)
                    bankFoot = smoothstep(-200.0, 200.0, within);
                    // (and what changes over a kilometre or two, from the same reading's cells)
                    if (heapy > 0.999) softWide = clamp(0.5 + (broad.a - 0.48) * 0.9, 0.0, 1.0);
                    // (its own top well under the layer's, lower at its edge than further in, and rounded off towards its side:
                    // the heaps in it stand through it, a top of domes and not a level lid)
                    float top = min(used, 900.0) * lerp(0.42, 0.68, smoothstep(0.0, 900.0, within)) * (0.9 + 2.0 * (softWide - 0.5));
                    bank = -larger(-within, above - top, 140.0);
                } }
                // (Read from the map drawn once a frame, where there is one: two readings, and the egg of the heap they name.)
                // (The map comes round: a place further from the camera than half its width would be given the heaps of another. There
                // are none so far off: see go 5.)
                bool mapped = L == 0 && _CloudHeapsAt.x > 0.5;
                float2 mapAt = faceOf(col, _CloudHeapsAt.w).xy, mapFrom = abs(mapAt - faceOf(_CloudNoise.xyz, _CloudHeapsAt.w).xy);
                // (the map holds heaps where the low layer is heaps, and cells where it is pads: where it is something of both, neither
                // is read from it)
                bool onMap = mapped && sheet < 0.999 && (heapy > 0.98 || heapy < 0.02) && max(mapFrom.x, mapFrom.y) < 0.5 * _CloudHeapsAt.y;
                // (pads: the open sky either side of a cell's edge, metres: narrow where most of the sky is covered, and so wide where
                // little is that each cell's cloud is a tuft in its middle)
                // (the low layer's cracks closing to a few tens of metres where most of the sky is covered, its cells' corners rounded
                // over a twentieth of a cell: a deck, as stratocumulus of nine tenths is, broken by the gaps between its rafts and its
                // cells that come and go; the middle sky's as they were, the mackerel sky's cloudlets with sky between them)
                float crack = C * (L == 0 ? lerp(0.42, 0.012, ramp(0.05, 0.75, cover)) : lerp(0.42, 0.05, ramp(0.05, 0.8, cover)));
                { [loop] for (int once = onMap ? 0 : 1; once < 1; once++)
                {
                    float2 muv = mapAt / _CloudHeapsAt.y;
                    float4 heap = SampleLod(_CloudHeaps, muv, 0.0);
                    inside = -heap.r;
                    { [loop] for (int pads = heapy < 0.5 ? 0 : 1; pads < 1; pads++)
                    {
                        inside = padsProfile(heap, above);
                        size = heap.g > 0.0 ? max(heap.g, 0.25 * C) : 0.0; age = heap.a; lift = SampleLod(_CloudHeapsB, muv, 0.0).r;
                    } }
                    { [loop] for (int there = heapy >= 0.5 && heap.g > 15.0 ? 0 : 1; there < 1; there++)
                    {
                        float R = heap.g, H = heap.b, raised = SampleLod(_CloudHeapsB, muv, 0.0).r, up = above;
                        // (the spreading top, as heapsRound has it)
                        float spread = B.z * clamp((H - 0.9 * used) / (0.3 * used), 0.0, 1.0) * clamp((up - 0.55 * used) / (0.3 * used), 0.0, 1.0) * clamp(heap.a + 0.8, 0.0, 1.0);
                        inside = heapEgg(heap.r / min(1.0 + 0.9 * spread, 0.46 * C / R), up, R, H);
                        size = R; age = heap.a; lift = raised + heapsRim(heap.r / max(R, 1.0)) * H;
                    } }
                } }
                // (Off the map, worked out at the asking, as where there is no map: from close by there are none so far off (the
                // walk's fading of them with distance), but from above the clouds there are, as far as the planet's rim.)
                { [loop] for (int once = !onMap && sheet < 0.999 && share > 0.002 ? 0 : 1; once < 1; once++)
                {
                    // (how the place on the layer's base moves as the line is walked: the line's way along the ground, and the heaps'
                    // lean for what it climbs)
                    float climbs = dot(formWay, formUp);
                    float3 across = formWay - formUp * climbs - _CloudDrift.xyz * (K.w * climbs);
                    // (Each face's lattice moved off the others' by its own amount, the same wherever it is looked at from. Until the
                    // evening of 2026-10-12 it was the nearer face's that stood unmoved and the further's that was moved: across the
                    // line where two faces meet, each lattice then changed places with itself, and its heaps were cut along that line.)
                    // (The face the place is nearest, and near where two faces meet the other one too: in one loop, so that Windows'
                    // compiler, which puts every function inline, makes one copy of heapsRound here and not two.)
                    { [loop] for (int side = 0; side < (formMine < 0.999 ? 2 : 1); side++)
                    {
                        float f = side == 0 ? formFace : formOther, mine = side == 0 ? formMine : 1.0 - formMine;
                        float3 cg = faceOf(col, f), ug = faceOf(formUp, f);
                        float size2 = 0.0, age2 = 0.0, lift2 = 0.0, leaves2 = 1e6, inside2 = -1e5;
                        // (the middle sky's cloudlets in rows across the wind, over some regions some tens of kilometres across: see padsRows)
                        float2 wd = faceOf(_CloudDrift.xyz, f).xy;
                        padsRowsWay = dot(wd, wd) > 1e-6 ? normalize(wd) : float2(1.0, 0.0);
                        padsRows = L == 1 ? smoothstep(0.42, 0.58, SampleLod(_CloudShape, col * 0.11 + float3(0.37, 0.71, 0.19), 3.0).r) : 0.0;
                        // (heaps round their points of the lattice; pads in the cells of the churning (padsCells); where the kind is
                        // between the two, both, each by its share)
                        { [loop] for (int once2 = heapy > 0.02 ? 0 : 1; once2 < 1; once2++)
                            inside2 = heapsRound(cg.xy * perF + float2(7.3, 3.1) * f, -ug.xy / ug.z, faceOf(across, f).xy * (perF / N), C, per, _CloudForm.y, above, share * mine, widest, evenly, join, tall, used, B.z, size2, age2, lift2, leaves2); }
                        { [loop] for (int once2 = heapy < 0.98 ? 0 : 1; once2 < 1; once2++)
                        {
                            float sizeC, ageC, liftC, leavesC, w = heapy > 0.02 ? heapy : 0.0;
                            float insideC = padsCells(cg.xy * perF + float2(7.3, 3.1) * f, -ug.xy / ug.z, faceOf(across, f).xy * (perF / N), C, per, _CloudForm.y, above, share * mine, crack, usedPads, L == 0 ? 0.05 : 0.0, sizeC, ageC, liftC, leavesC);
                            inside2 = lerp(insideC, inside2, w); size2 = lerp(sizeC, size2, w); age2 = lerp(ageC, age2, w); lift2 = lerp(liftC, lift2, w); leaves2 = min(leaves2, leavesC);
                        } }
                        if (side == 0) { inside = inside2; size = size2; age = age2; lift = lift2; leaves = leaves2; }
                        else
                        {
                            if (inside2 > inside) { size = size2; age = age2; lift = lift2; }
                            inside = larger(inside, inside2, join);
                            leaves = min(leaves, leaves2);
                        }
                    } }
                } }
                // (the bank with the heaps in it and on it; where the bank has the place, its numbers: as if a heap as wide as the
                // lattice's largest, in the middle of its life, on the layer's base)
                // (Where two sizes of lattice are both there, the bank is in both, whole, and its lumps, which are of each lattice's
                // own size, stand as proud in each as that lattice's share: the one's lumps go as the other's come.)
                float bankShare = onMap ? smoothstep(0.0, 0.5, weight) : 1.0;
                { [loop] for (int once = banks > 0.01 ? 0 : 1; once < 1; once++)
                {
                    // (The bank's numbers taken over from the heap's across the width the two are joined over, not at the line where the
                    // bank comes nearer than the heap: a heap's base rises towards its rim by up to a fifth of its height, the bank's by three
                    // tenths of that, and switched at that line, the base had a ledge tens of metres high there: slabs hung under heaps (the
                    // lab), and, it seems, the thin bright arc across a heap's underside from the pad (the main chat, 2026-10-18).)
                    float toBank = clamp(0.5 + 0.5 * (bank - inside) / (0.2 * C), 0.0, 1.0);
                    size = lerp(size, lerp(size, 0.42 * C, 0.7), toBank); age = lerp(age, age * 0.3, toBank); lift = lerp(lift, lift * 0.3, toBank);
                    bankShare = lerp(bankShare, smoothstep(0.0, 0.5, weight), toBank);
                    inside = larger(inside, bank, 0.2 * C);
                } }
                // ---- the sheet: a slab from the base up, as thick as the smooth noise is strong there over the level that leaves so
                // much of the sky covered (holes with thin edges where it is not all covered); and under a lumpy layer that covers
                // most of the sky, a thinner one that joins the pads
                float fill = B.w * weight;
                { [loop] for (int once = fill > 0.001 ? 0 : 1; once < 1; once++)
                {
                    float thick = used * lerp(0.55, 1.0, sheet) * fill * clamp((softWide - M.y) / 0.045 + 0.2, 0.0, lerp(1.0, 0.75 + 0.5 * softWide, sheet));
                    float slab = min(above, thick - above);
                    // (the pads' numbers kept where a pad has the place, the sheet's own where it has)
                    if (slab > inside) { size = lerp(size, 0.42 * C, 0.5); lift = 0.0; }
                    inside = larger(inside, slab, 0.5 * join);
                } }
                // (the lumps: the largest about a third of a cell across on a heap, half that on a pad, and wide and slight on a sheet;
                // how far they can stand out)
                // (On a heap larger and prouder since 2026-10-21, three tenths of a cell and as proud as their size before: set beside stills of
                // blackrack's clouds, which the owner holds "look way better", our heaps were a smooth dome under an even coat of small lumps,
                // cotton, where his and the photographs' are lobes upon lobes, each a good part of the heap, with deep creases between.)
                float lump = C * lerp(lerp(0.15, 0.4, sheet), 0.36, heapy);
                // (and not as proud everywhere: over some kilometres a top is smoother, where the air has stopped rising, and over
                // others lumpier, where it still is; lumps all alike over a whole bank were a sheet of bubbles)
                float proud = lerp(lerp(0.7, 0.22, sheet), 1.25, heapy) * lerp(0.45, 1.25, smoothstep(0.42, 0.6, softWide));
                // (Under the lid of a shallow layer the lumps count for little: the air there is held down, and what spreads out
                // under it is flat and thin. With lumps a hundred metres proud on a pad a hundred metres thick, the pad was torn
                // into leaves, one over another. And they reach as much less far: the walk goes slowly only as near a surface as its
                // lumps can stand out from it, which over the flat top of a bank seen from just above is most of the way.)
                float damp = (1.0 - 0.8 * heapy * (1.0 - smoothstep(1400.0, 2600.0, deep)) * smoothstep(0.5, 0.85, above / used)) * bankShare;
                // (On a heap two thirds of it: its lumps no further out than before they were made larger and prouder, held to a fifth of their
                // share (below), however much deeper their creases. With the whole, the walk went slowly over a wider band round every heap:
                // 5 ms a frame more at the pad (the main chat, 2026-10-21), for the same picture within a level.)
                float reach = 0.5 * lump * proud * damp * lerp(1.0, 0.67, heapy);
                insideOut = -1e9; openOut = -1.0;
                // (How far off the nearest cloud is, is known only for the heaps of the three points looked at: a heap of the next
                // triangle may stand just across its edge. So the walk is never told it may stride further than three tenths of a
                // cell, whatever these three say, and the seeker says "perhaps" whenever its stride is longer than that. Told the
                // plain distance, as at first, it strode clean over far heaps: for whole rows of the picture at once, since every
                // line of sight of a row comes to a far heap by nearly the same strides: heaps cut into level slats.)
                // (Where the lattice has no heaps, far off, there is nothing unseen: the walk is told the whole distance. Where it
                // has, no further than to where the line being walked leaves this triangle of the lattice, and a little way in to
                // the next; a line whose way is not told, three tenths of a cell.)
                // (Read from the map: no heap's edge within half a cell is known there, and nothing further. Off the map there are
                // no heaps.)
                // (shrunk towards nothing as the far field takes over: see formOne)
                inside -= (1.0 - formFade) * max(size, 0.3 * C);
                float most = onMap ? 0.45 * C : share > 0.002 ? (dot(formWay, formWay) > 0.5 ? leaves + 0.04 * C : 0.3 * C) : 1e6;
                if (seek) { nearOut = 1e9; return inside + reach + 1.5 * stride > 0.0 || stride > most ? 1.0 : 0.0; }
                nearOut = max((inside + reach + 6.0 + 0.6 * pixel + (L == 1 ? padsFray * 99.0 : 0.0)) * 0.7, -most);   // (the softer margin of a row's lane reaches half its 2.2 x 90 m further out)
                if (nearOut < 0.0) return 0.0;
                // ---- the lumps: round in every direction; three readings in one, each of cells half the last's size and two sizes
                // finer with them; and finer again near the camera. On a small heap the large ones count for less.
                float3 at = q + wm.yzw * 0.6;
                float ls = 2250.0 / lump;
                float3 part = clamp(size / (float3(1.3, 0.65, 0.33) * lump), 0.0, 1.0);
                // (Each lump rounded on top: a cell is a cone, highest at a point, and cones standing on a level sheet are a bed of
                // spikes; one less the square of what it lacks of its top is a dome, with the same sharp crease where it meets the
                // next. That comes to 0.715 on the whole, where the cells come to 0.48.)
                // (Read only where the small noise is read too: at the walk's own steps, and at the light's first two towards the
                // sun, which are what show a lump its own shade. The light's further steps, tens and hundreds of metres off, ask
                // only for the shape under the lumps.)
                float moved = 0.0, wisps = 1.0;
                if (fine)
                {
                    float4 big = SampleLod(_CloudShape, at * ls + float3(0.19, 0.43, 0.67), max(lod + log2(ls) - 1.0, 0.0) * _CloudHaze.x);
                    // (and where the cloud is in wisps and holes: see the density at the end)
                    wisps = smoothstep(0.42, 0.6, big.g * 0.5 + big.b * 0.3 + big.a * 0.2);
                    float3 domes = 1.0 - (1.0 - big.gba) * (1.0 - big.gba);
                    // (A crease between two lumps no deeper than three tenths of a lump's size: the cells' own creases go down to
                    // nothing between any two of them, and a heap's side was scored with clefts, some of them through it.)
                    // (The smaller two sizes count for more than they did, two thirds and two fifths of the largest, not a half and a quarter:
                    // lumps on lumps. And no lump stands out by more than a fifth of its share: prouder, they stood clear of their heap, loose
                    // puffs over its top, a spray of popcorn.)
                    moved = dot(clamp(domes - 0.715, ((float3)(-0.3)), ((float3)(0.2))) * part, float3(1.0, 0.65, 0.4)) * 1.6 * lump * proud;
                    // (read no finer than a pixel of the picture is wide: the small noise's cells are a sixteenth of what is read here
                    // to a dot of it. Read two sizes finer than that, as it first was, cells of nine metres were read by pixels ten
                    // metres apart, and what came of it was level stripes across every heap a few kilometres off.)
                    float cellSmall = lump / 8.0;
                    // (three sizes of the small noise from its one reading, as of the large: until the evening of 2026-10-12 only the
                    // coarsest of them, and a tower a few kilometres off had no lump on it under a hundred metres: modelling clay)
                    // (not read at all where it would be read so coarsely that it is the same everywhere: far off, from above the clouds)
                    float smallLevel = clamp(log2(pixel / (cellSmall / 16.0)) + 0.3, 0.0, 6.0);
                    { [loop] for (int read = smallLevel < 4.5 ? 0 : 1; read < 1; read++)
                    {
                        float3 small = SampleLod(_CloudDetail, at * (N / (cellSmall * 4.0)) + float3(0.0, 0.0, _CloudDrift.w), smallLevel * _CloudHaze.x).rgb;
                        moved += dot(small - 0.48, float3(1.0, 0.5, 0.3)) * 1.6 * cellSmall * proud;
                    } }
                }
                moved *= damp;
                float plain = inside;
                inside += moved;
                // (Level underneath: cut after the lumps, so that the rim of the base is scalloped by the lumps standing over it and
                // the underside itself is smooth. Cut before them, the lumps and the creases between them were on the underside too:
                // a base veined like marble, seen from below. Not level to the metre: it rises and dips by a few tens of metres, gently.)
                // (A heap that is going has no level base any more: what is left of it is ragged underneath, by its own lumps.)
                float going = heapy * smoothstep(0.4, 0.95, age);
                // (The rise and dip from the wide reading, which is the same at every height over a place. From the lumps' own
                // reading, as it first was, it changed with height as fast as it stood: the cut was then not one surface but
                // several, and every heap had thin level slits through its lower part, stripes across it from a few kilometres off.)
                // (One level base wherever a bank is: the heaps in a bank stand on its base, as a field's heaps stand on the level where the air
                // first condenses, and each heap's own base, raised by chance and rounded up towards its rim, is a heap's by itself. Kept in
                // part under a bank (three tenths of it, as until the evening of 2026-10-18), it terraced the bank's underside: from below at a
                // few kilometres a wide grey lens with arcs nested in it (the main chat's lens in the middle sky), a crease across a bank
                // overhead, plates stacked one over another at the horizon, upright stripes on a bank's face (the lab).)
                float under = above - lift * (1.0 - bankFoot) - (softWide - 0.5) * 260.0 * lerp(0.5, 1.0, heapy) + going * (1.5 * moved + 40.0);
                // (The top under the lid is uneven by lumps too: low domes. But not by the lumps as they are at this place: they
                // change as fast with height as they stand proud, and a level top moved up and down by them is not one surface but
                // several, one over another every twenty metres: level stripes across every heap that reaches the layer's top, and a
                // sheet seen from above like whipped cream. By the lumps as they are at the lid's own height over this place: one
                // surface. Read only near the lid, and only where the small noise is read too.)
                float below = used - above, lidMoved = 0.0, domed = lerp(0.55, 0.35, heapy);
                { [loop] for (int once = fine && below < 0.75 * lump * proud * domed + inside + moved + 30.0 ? 0 : 1; once < 1; once++)
                {
                    float4 over = SampleLod(_CloudShape, (at + formUp * (below / N)) * ls + float3(0.19, 0.43, 0.67), max(lod + log2(ls) - 1.0, 0.0) * _CloudHaze.x);
                    float3 overDomes = 1.0 - (1.0 - over.gba) * (1.0 - over.gba);
                    lidMoved = dot((overDomes - 0.715) * part, float3(1.0, 0.5, 0.25)) * 1.6 * lump * proud * domed * bankShare;
                } }
                inside = min(inside, min(under, below + lidMoved));
                insideOut = inside;
                smoothOut = min(plain, min(under, below + lidMoved));
                // (how much sky is seen: more on the tops of lumps than in the creases between them, more high on a heap than low)
                // (In the creases half of what the tops of lumps see, not a twelfth: the light of the sky comes into a crease from
                // the lit cloud on either side of it as well, and creases drawn as dark as an open sky alone would leave them were
                // black lines over every shaded side.)
                openOut = lerp(0.5, 1.0, clamp(0.5 + moved / (0.5 * lump * proud), 0.08, 1.0)) * clamp(0.25 + 0.9 * above / max(lerp(used, 1.2 * size, heapy), 60.0), 0.0, 1.0);
                // (The surface is a few metres of clear air mixing in; far off, as wide as a pixel of the picture; a sheet's is tens of
                // metres of thinning mist. Underneath it is not a surface at all: the cloud thins downward over some tens of metres
                // to where the air is first cloudy, a sheet over more.)
                // (And a heap that is going is soft all round, and thin: it is evaporating.)
                // (pads' edges softer than heaps': the cells' cloud is thin, and its edges are seen through)
                // (The middle sky's pads, altocumulus, as thin as they are and softer still: a cloudlet holds 0.1 to 0.3 g of water a cubic
                // metre where a heap holds 0.5 to 1 and more, and loses a third to a half as much light a metre (1.5 LWC / (rho_w r_e) with
                // droplets of 10 microns); drawn as dense as heaps, a sky of them was the main chat's "small hard puffs", solid white to a
                // crisp outline, where photographs show cloudlets the sky's blue comes through at their margins, frayed over tens of metres
                // (research/rendering/experiments/altocumulus-look.md).)
                // (and in a row's lane, the middle sky's margins as much as three times as soft: see padsRows)
                float edge = max(lerp(lerp(L == 1 ? 90.0 : 40.0, 110.0, sheet), 10.0, heapy) * (1.0 + 5.0 * going), 1.2 * pixel) * (1.0 + 2.2 * (L == 1 ? padsFray : 0.0));
                float thinner = L == 1 ? lerp(0.6, 1.0, max(sheet, heapy)) : 1.0;
                // (The middle sky's pads in wisps and holes through their lower 160 m, by the lumps' own pattern: a thick cloudlet's underside
                // mottled, a thin one (a pad of a little over a hundred metres, as many are) patchy all through. Even through, as they were, the
                // thin cloudlets were clear film with a crisp outline among the white ones, cellophane (the main chat, 2026-10-18 18:33), where
                // a thin altocumulus in the photographs is a patch of wisps with the sky seen through its gaps.)
                float patchy = lerp(lerp(1.0, wisps, L == 1 ? (1.0 - heapy) * (1.0 - sheet) : 0.0), 1.0, clamp(under / 160.0, 0.0, 1.0));
                return clamp(inside / edge + 0.5, 0.0, 1.0) * clamp(lerp(0.15, 0.35, heapy) + under / lerp(lerp(90.0, 200.0, sheet), 60.0, heapy), 0.0, 1.0) * (1.0 - 0.55 * going) * thinner * patchy;
            }

            // One layer's cloud at a place, so made: the low layer (L 0) or the middle one (1), by what formStep worked out for the
            // step. As layerAt, which hands over to it.
            float formAt(int L, float3 q, float h, float kind, float wet, float4 wm, bool fine, float lod, bool seek)
            {
                float N = _CloudNoise.w;
                float4 K = formKind[L], Z = formSize[L];
                float deep = formMore[L].z, above = h - formMore[L].w, hf = above / deep;
                // (a lumpy layer uses only the lowest few hundred metres of the air it is given: nothing is asked above that)
                if (above > K.z + 150.0) { heapsNear = max(heapsNear, -alongTo(above - K.z - 150.0, h, true)); return 0.0; }
                // ---- the place on the layer's base under this one (so that a heap stands upright), moved downwind as much as the
                // heap leans by this height, and bent over kilometres
                float3 col = q - formUp * (above / N) - _CloudDrift.xyz * (above * K.w / N) + wm.yzw * 0.6 + float3(0.37, 0.71, 0.13) * float(L);
                // (The lattice at its size, and where the layer's depth asks for a size between two, at the next too: in one loop, so that
                // Windows' compiler, which puts every function inline, makes one copy of formOne here and not two.)
                float nearA = -1e9, insideA = -1e9, openA = -1.0, smoothA = -1e9, c = 0.0;
                { [loop] for (int size_ = 0; size_ < (Z.w > 0.001 ? 2 : 1); size_++)
                {
                    float nearB, insideB, openB, smoothB;
                    float c2 = formOne(L, size_ == 0 ? Z.y : Z.z, size_ == 0 ? 1.0 - Z.w : Z.w, q, col, above, wm, fine, lod, seek, nearB, insideB, openB, smoothB);
                    if (size_ == 0) { c = c2; nearA = nearB; insideA = insideB; openA = openB; smoothA = smoothB; }
                    else
                    {
                        nearA = max(nearA, nearB);
                        if (c2 > c || (c <= 0.0 && insideB > insideA)) { c = c2; insideA = insideB; openA = openB; smoothA = smoothB; }
                    }
                } }
                if (seek) return c;
                heapsNear = nearA;
                if (c <= 0.0) return 0.0;
                heapsInside = insideA; heapsOpen = openA; heapsSmooth = smoothA;
                // (a sheet is thinner stuff than the core of a heap: a grey overcast lets a share of the light through, where a sheet as
                // dense as a heap's core is dark as a storm's base)
                c *= lerp(0.3, 1.0, smoothstep(0.1, 0.45, kind));
                // (Fog: a low layer of a kind under 0.02 (CloudSource.Kind). The weather's own to draw while the camera is in it or
                // near it (Natural Light's veil, which leaves off two kilometres or so over its top); the clouds draw it only from over
                // that, where banks of it lie on the sea and the low ground with their edges, coming in by how dense it is (by how much
                // of the sky it covers, it came in as scattered slabs). Nothing drew it from there before 2026-10-17.)
                { [loop] for (int fog = L == 0 && kind < 0.03 ? 0 : 1; fog < 1; fog++)
                    c *= lerp(1.0, smoothstep(formMore[0].w + formMore[0].z + 1600.0, formMore[0].w + formMore[0].z + 2400.0, _CloudHigh.x), 1.0 - smoothstep(0.015, 0.03, kind)); }
                // (rain clouds are thicker and darker underneath)
                return c * (1.0 + wet * (1.0 - hf));
            }

            // How much cloud of one layer there is at a point: q is the point in the noise (lengths of the large noise), h its height
            // above the radius; then the layer: how much of the sky it covers, what kind, its base and top, how wet. "fine" says
            // whether the small noise is used (it is left out in the light's walk and far away); lod which size of the large noise to
            // read (0 its full size, below 0 nearer than that; the small noise is read 2.2 sizes finer).
            float layerAt(float3 q, float3 p, float field, float h, float cover, float kind, float base, float top, float wet, float4 wm, bool fine, float lod)
            {
                // (a size of the noise past 50 says: seeking. Is there any cloud within a long stride of here? See the walk.)
                bool seek = lod > 50.0;
                if (seek) lod -= 100.0;
                top = max(top, base + 50.0);
                float hf = (h - base) / (top - base);
                if (hf <= 0.0 || hf >= 1.0)
                {
                    float off = hf <= 0.0 ? base - h : h - top;
                    heapsNear = max(heapsNear, -(_CloudForm.x > 0.5 ? alongTo(off, h, hf >= 1.0) : off));
                    return 0.0;
                }
                cover = clamp(cover * _CloudLook.z, 0.0, 1.0);
                // (No cover here: the next cloud of this layer is where the weather's map gives it some, which changes over kilometres.
                // Said to be 200 m off wherever this layer's heights had none, as it was until 2026-10-15, a line of sight running a
                // little above level through such heights strode 200 m at a time, spent its steps by 13 km, and never reached the deck
                // above beyond that: the owner's "strip of light blue in the distance". A tenth of the distance far off.)
                if (cover <= 0.001) { heapsNear = max(heapsNear, -max(200.0, 2500.0 * exp2(lod))); return 0.0; }
                // (A thin layer comes in with its cover, from nothing at the level below which it is left out (here, and for the middle
                // clouds in the walk, 0.004), and is whole from about three in a hundred. The weather's map is read bilinearly between its
                // dots, and a level cuts such a reading along straight lines with steps where the dots are; a layer that began at full
                // strength there was cut along them. From inside a thin layer every line of sight begins in it, and the whole view parted
                // along such a line: altocumulus at 0.02 between 4.8 and 5.1 km and the camera at 5 km, a straight-edged veil over the sea
                // along the coast and clear sea beside it (the "seam" of 2026-10-20; none from 3 km or 7 km).)
                float comes = field > 1.5 ? smoothstep(0.004, 0.03, cover) : smoothstep(0.001, 0.012, cover);
                // (clusters, gaps and streets; a sheet over the whole sky is left whole: they are for skies of separate heaps)
                // (as a power, not a factor: a cluster thickens the cover towards all of the sky but never to all of it, where a factor
                // filled the layer from base to top in slabs with flat tops and straight sides)
                // (the shapes of the rework of 2026-10-12, where they are asked for, which work out for themselves what gatherings
                // and streets do to the cover: the rest of this is how it was before)
                // (the shapes of the rework of 2026-10-12, which work out for themselves what gatherings and streets do to the cover.
                // The shapes from before it, kept until 2026-10-16 for a development switch, are gone: Windows' compiler, which puts every
                // function inline, made a copy of them for every place a cloud was asked for.)
                return formAt(field > 1.5 ? 1 : 0, q, h, kind, wet, wm, fine, lod, seek) * comes;
            }

            // The storms near the camera by number (0 the nearest), and how many there are. They are walked in a loop whose length is
            // not known until the picture is drawn, and so is written out once: written out six times over, one after another (as until
            // 0.3.0), the six copies of a storm's tower cost a sky with no storm in it two thirds of its time on this Mac's graphics card.
            float4 stormA(int k) { return k < 3 ? (k == 0 ? _CloudCellA0 : k == 1 ? _CloudCellA1 : _CloudCellA2) : (k == 3 ? _CloudCellA3 : k == 4 ? _CloudCellA4 : _CloudCellA5); }
            float4 stormB(int k) { return k < 3 ? (k == 0 ? _CloudCellB0 : k == 1 ? _CloudCellB1 : _CloudCellB2) : (k == 3 ? _CloudCellB3 : k == 4 ? _CloudCellB4 : _CloudCellB5); }
            int storms() { return _CloudCellA0.w <= 0.0 ? 0 : _CloudCellA1.w <= 0.0 ? 1 : _CloudCellA2.w <= 0.0 ? 2 : _CloudCellA3.w <= 0.0 ? 3 : _CloudCellA4.w <= 0.0 ? 4 : _CloudCellA5.w <= 0.0 ? 5 : 6; }

            // (How wide a pixel of the picture is at the storms' own shapes, at 25 km times two to the power lod: as the layers' shapes have
            // it (formPer), a dot of what is kept where that is finer than the walk's picture. Worked out from the width of the walk's own
            // picture alone, as until the evening of 2026-10-18, it was four times as wide with the walk at a quarter size (as the game has it
            // from orbit) and twice at half, and every storm's surface as much softer: a hurricane's outer towers seen from orbit were smooth
            // grey pebbles, and its core under the ice a grey ring (the lab: white, and lumpy, with the walk at the picture's size).)
            // (The `lod` the walk draws the place at, for the time of its light towards the sun, whose readings are coarser: the storms'
            // shapes soften their surface by no wider a pixel than the walk's own. Read as coarsely as the light reads them, a storm's
            // surface was softened over twice the width, half of it outside, and from orbit the light from its top went through a haze of
            // its dense cloud a kilometre and more over it: a grey ring round a hurricane's eye seen aslant, where its core's top stands
            // through the canopy. 100 elsewhere: no limit.)
            static float stormLod = 100.0;
            float stormPixel(float lod) { return 25000.0 * exp2(lod) * (_CloudForm.x > 0.5 ? formPer : 2.0 * length(_CloudRight.xyz) / max(_CloudHigh.w, 1.0)); }

            // One storm or shower. Where there is a field of heaps made beforehand: a tower of rising air round its middle, widest at the
            // base, with the field's heaps crowded on it and round it (all of them at the middle, fewer outward: the flanking towers of a
            // real storm, each lower than the last), lumps carved on the lot; and at its top the anvil, ice spread out downwind under the
            // height where the air stops rising, thin and streaked. Where there is no field, as before 0.3.0: a tower and its anvil
            // carved from the noise.
            // One storm, in the shapes of the rework of 2026-10-12 (see formAt): a thundercloud as it is built. The great tower over
            // the rising air, an egg kilometres across that reaches the top of the weather, leaning with the wind; beside it on the
            // side the storm feeds from, a line of lesser towers, each lower than the last (the next ones growing); on all of them
            // lumps of five and six sizes, the largest some hundreds of metres across. Where the tower meets the height it cannot
            // pass it spreads into the anvil: ice, drawn out downwind, far wider than the tower, thin, soft-edged and smooth above,
            // with a dome where the tower's own top overshoots it.
            float cellNew(float3 p, float h, float3 q, float4 a, float4 b, float4 wm, bool fine, float lod)
            {
                float N = _CloudNoise.w;
                float deep = max(b.y - b.x, 100.0), above = h - b.x, frac_ = above / deep;
                bool seek = lod > 50.0;
                if (seek) lod -= 100.0;
                float pixel = stormPixel(lod), pixelC = stormPixel(min(lod, stormLod));
                float stride = 0.03 * 25000.0 * exp2(lod - 1.0);
                float3 mid = a.xyz / max(length(a.xyz), 1.0);
                float3 wind = _CloudDrift.x * _CloudFixedX.xyz + _CloudDrift.y * _CloudFixedY.xyz + _CloudDrift.z * _CloudFixedZ.xyz;
                wind -= mid * dot(wind, mid);
                float blowing = length(wind);
                // (two ways along the ground at the storm: downwind, and to the right of that; with no wind to speak of, any two)
                float3 along = blowing > 0.05 ? wind / blowing : normalize(cross(mid, float3(0.31, 0.57, 0.76)));
                float3 beside = cross(mid, along);
                float reach = a.w, strength = smoothstep(0.15, 0.5, b.z);
                float3 flat_ = p - mid * dot(p, mid);
                float2 at2 = float2(dot(flat_, along), dot(flat_, beside));
                if (dot(at2, at2) > 9.0 * reach * reach) { heapsNear = max(heapsNear, 3.0 * reach - length(at2)); return 0.0; }
                // ---- the towers: the great one, leaning downwind by a tenth of its height, and three lesser ones in a line on its
                // upwind flank, to the right of the wind (where a storm in this hemisphere draws its air in)
                float lean = 0.1 * above;
                float R0 = reach * 0.42 * (0.6 + 0.4 * strength);
                float inside = eggAt(length(at2 - float2(lean, 0.0)), above, R0, 1.04 * deep);
                float size = R0;
                { [loop] for (int k = 0; k < 3; k++)
                {
                    float turn = 2.5 + 0.55 * float(k), far = reach * (0.5 + 0.2 * float(k));
                    float R = reach * (0.24 - 0.045 * float(k)) * (0.6 + 0.4 * strength), H = deep * (0.72 - 0.16 * float(k)) * (0.7 + 0.3 * strength);
                    float one = eggAt(length(at2 - float2(cos(turn), sin(turn)) * far - float2(0.6 * lean, 0.0)), above, R, H);
                    if (one > inside) size = R;
                    inside = larger(inside, one, 0.12 * reach);
                } }
                // (the lumps: the largest a quarter of the great tower's half-width)
                float lump = 0.25 * R0, reachOut = 0.5 * lump;
                // ---- the anvil: from four fifths of the way up, a slab drawn out downwind (its middle a reach and more downwind of the
                // tower, its length twice its width), thickest over the tower and thinning to nothing at its rim
                float2 an = (at2 - float2(1.1 * reach * strength, 0.0)) / (float2(2.6, 1.7) * reach * (0.5 + 0.5 * strength));
                float rim = 1.0 - length(an);
                float under = lerp(0.82, 0.74, smoothstep(0.0, 0.6, rim)), over = 0.985;
                float thick = (over - under) * deep;
                // (how far inside the slab: from its rim over the ground, as far as it is wide there, and from its faces)
                float anvil = min(rim * 1.7 * reach * 0.6, min(above - under * deep, over * deep - above));
                if (seek) return max(inside + reachOut, anvil + 0.08 * thick) + 1.5 * stride > 0.0 ? 1.0 : 0.0;
                float near = max((inside + reachOut + 6.0 + 0.6 * pixel) * 0.7, (anvil + 0.1 * thick + 6.0) * 0.7);
                // (No further than 0.15 of its reach is ever said, as its shape is not known closely enough to say more; but far off,
                // a tenth of the distance: under a sky with storms about, every line of sight within three of a storm's reaches of it was
                // held to strides of a kilometre, and one a little above level under a deck spent its steps before it reached the deck,
                // the owner's "strip of light blue in the distance" (2026-10-15). `lod` is the distance, as a power of two of 25 km.)
                heapsNear = max(heapsNear, max(near, -max(0.15 * reach, 2500.0 * exp2(lod))));
                if (near < 0.0) return 0.0;
                // ---- the towers' lumps, as a heap's: round, of three sizes in one reading and three finer near the camera
                float3 atq = q + wm.yzw * 0.6;
                float ls = 2250.0 / lump;
                float4 big = SampleLod(_CloudShape, atq * ls + float3(0.47, 0.11, 0.83), max(lod + log2(ls) - 1.0, 0.0) * _CloudHaze.x);
                float3 domes = 1.0 - (1.0 - big.gba) * (1.0 - big.gba);
                float3 part = clamp(size / (float3(1.3, 0.65, 0.33) * lump), 0.0, 1.0);
                float moved = dot(max(domes - 0.715, ((float3)(-0.3))) * part, float3(1.0, 0.5, 0.25)) * 1.6 * lump;
                float soft = clamp((big.r - big.g) / max(1.0 - big.g, 0.02), 0.0, 1.0);
                if (fine)
                {
                    float cellSmall = lump / 8.0;
                    float3 small = SampleLod(_CloudDetail, atq * (N / (cellSmall * 4.0)) + float3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / (cellSmall / 16.0)) + 0.3, 0.0, 6.0) * _CloudHaze.x).rgb;
                    moved += dot(small - 0.48, float3(1.0, 0.5, 0.3)) * 1.6 * cellSmall;
                }
                float tower = min(inside + moved, above - (soft - 0.4) * 160.0);
                float c = clamp(tower / max(12.0, 1.2 * pixelC) + 0.5, 0.0, 1.0) * clamp(0.3 + above / 90.0, 0.0, 1.0);
                // ---- the anvil's own look: ice. Smooth and soft above, drawn out in streaks along the wind, ragged at its rim (the
                // smooth noise stretched along the wind eats into it there), a third as thick as the tower's cloud.
                float streaks = SampleLod(_CloudShape, (atq - (along.x * _CloudFixedX.xyz + along.y * _CloudFixedY.xyz + along.z * _CloudFixedZ.xyz) * 0.0) * 0.9 + float3(0.23, 0.61, 0.07), 1.5 * _CloudHaze.x).r;
                float ragged = anvil - (0.78 - streaks) * 2.2 * thick * (1.0 - smoothstep(0.25, 0.7, rim)) + 0.25 * moved * step(above, (under + 0.3 * (over - under)) * deep);
                float ice = clamp(ragged / max(0.22 * thick, 1.2 * pixelC) + 0.5, 0.0, 1.0) * 0.36 * strength;
                if (ice > c) { c = ice; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = clamp(0.35 + (above - under * deep) / thick, 0.0, 1.0); }
                else { heapsInside = tower; heapsSmooth = min(inside, above - (soft - 0.4) * 160.0); heapsOpen = lerp(0.5, 1.0, clamp(0.5 + moved / (0.5 * lump), 0.08, 1.0)) * clamp(0.2 + 0.9 * frac_, 0.0, 1.0); }
                return c * (1.0 + b.w * (1.0 - frac_));
            }

            float cellAt(float3 p, float h, float3 q, float4 a, float4 b, float4 wm, bool fine, float lod)
            {
                if (b.z < 0.15 || a.w <= 0.0) return 0.0;
                float deep = max(b.y - b.x, 100.0);
                float frac_ = (h - b.x) / deep;
                // (Under its base or over its top: as far as the line of sight has to go to climb or come down to it, and no nearer than
                // the place is outside the widest the storm reaches, its anvil's rim. Said to be as far as the base is above the place,
                // whatever the distance, as it was until 2026-10-15, a strong storm 33 km off with its base 60 m over the camera held
                // every line of sight at the camera's height to strides of 60 m: from a craft at 2.3 km, a deck above ended in a straight
                // line some 13 km off, clear sky under it to the horizon, the owner's "strip of light blue in the distance".)
                if (frac_ <= 0.0 || frac_ >= 1.0)
                {
                    float3 axis_ = a.xyz / max(length(a.xyz), 1.0);
                    float outside = length(p - axis_ * dot(p, axis_)) - 3.7 * a.w;
                    heapsNear = max(heapsNear, -max(frac_ <= 0.0 ? alongTo(b.x - h, h, false) : alongTo(h - b.y, h, true), outside));
                    return 0.0;
                }
                // (Only the storms of the clouds' rework (cellNew). The old ones, kept until 2026-10-16 for a development switch, were in
                // every copy Windows' compiler made of this, which puts every function inline: a third of a layer's and the old heaps.)
                return cellNew(p, h, q, a, b, wm, fine, lod);
            }

            // A cyclone's own cloud at a place (see "a cyclone" above; e: the place in it, from eyeAt): its core and its outer bands,
            // storm cloud with a surface to it, and over them the canopy, ice.
            //
            // As photographs have them (the station's of Florence's eye, 2018; a hurricane hunter's from inside Katrina's, 2005). The
            // eye's wall is not a cliff of cauliflower: it is a soft white slope, with long terraces and bulges that run round it
            // nearly level, sharp against the sky along its lip. Round the eye, for a couple of its widths, the top of the core
            // bubbles (the wall's own storms, and the first bands wound tight round it), in arcs that wind round the eye; beyond
            // that it is smooth ice, fibrous, drawn out along the way the air flows out, thinning until the sea shows through it.
            float cycloneAt(float3 p, float h, float3 q, float4 e, float4 wm, bool fine, float lod)
            {
                float N = _CloudNoise.w, Rm = _CloudEyeA.w, now = _CloudEyeB.x;
                float base = _CloudEyeC.w, deep = max(_CloudEyeB.w - base, 1000.0), above = h - base, frac_ = above / deep;
                float x = e.x, r = e.z;
                bool seek = lod > 50.0;
                if (seek) lod -= 100.0;
                if (frac_ > 1.12) { heapsNear = max(heapsNear, 1.12 * deep - above); return 0.0; }
                float pixel = stormPixel(lod), pixelC = stormPixel(min(lod, stormLod));
                float stride = 0.03 * 25000.0 * exp2(lod - 1.0);
                float strength = smoothstep(0.1, 0.3, now), eyeOn = smoothstep(0.35, 0.6, now), rim = eyeRim();
                // ---- under its base, its rain: under the core, heaviest under the wall; grey, in streaks, down to the sea. (The wall of a real eye stands on the sea: there is no clear air under it.) It reaches
                // a hundred metres up past the base, which is ragged: with rain only below the base's own level, the sky showed
                // through between the two in slivers.
                float rain = 0.0;
                { [loop] for (int once = above < 110.0 && h > 0.0 ? 0 : 1; once < 1; once++)
                {
                    float foot = lerp(-0.3 * Rm, 0.62 * Rm, eyeOn);
                    float falls = smoothstep(foot, foot + 0.12 * Rm, r) * (1.0 - 0.6 * smoothstep(1.2, 2.5, x)) * (1.0 - smoothstep(3.2, 4.0, x));
                    if (falls <= 0.0) break;
                    float streak = SampleLod(_CloudShape, float3(eyeXY / N * 2.3, h / N * 0.23) + float3(0.7, 0.1, 0.4), 0.0).g;
                    // (streaked, but nowhere without rain: seen through a gap in the low cloud, streaks with clear air between them
                    // were a comb)
                    rain = 0.05 * strength * falls * (0.45 + 0.55 * smoothstep(0.2, 0.85, streak)) * (0.5 + 0.5 * clamp(h / max(base, 100.0), 0.0, 1.0)) * (1.0 - smoothstep(60.0, 110.0, above));
                } }
                if (frac_ < 0.0)
                {
                    // (No rain here: how far off the cyclone's cloud is. In the eye that is how far it is to the wall's foot, not how
                    // far it is up to the level of the base, where over the eye there is nothing: told the second, a line of sight
                    // along the sea from the middle of the eye went fifty metres at a step, ran out of steps four kilometres on,
                    // short of the wall, and showed the sky through under it.)
                    float foot = lerp(-0.3 * Rm, 0.62 * Rm, eyeOn);
                    if (rain <= 0.0) { heapsNear = max(heapsNear, r < foot ? -max(foot - r, -above) * 0.9 : min(above, -40.0)); return 0.0; }
                    heapsNear = 1e9; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = 0.3;
                    return seek ? 1.0 : rain;
                }
                // ---- the core: from the eye's edge out to four radii, solid. Its inner face is the wall, leaning outward as it rises
                // (0.62 of the radius at the sea, 1.17 at the top: a real eye is about twice as wide aloft); its top stands a little
                // above the canopy round the eye and sinks under it outward. A weak cyclone has no eye: the core is filled in.
                float f = clamp(frac_, 0.0, 1.0);
                float inner = lerp(-0.3 * Rm, Rm * (0.62 + 0.55 * pow(f, 1.7)), eyeOn);
                // (its top: up through the canopy for two radii round the eye, then down well under it. Where it lay a few hundred
                // metres under the ice all the way out, a line of sight from orbit, whose steps are that long, lit some pixels as
                // ice and some as the cloud under it: a disc of another tone, four radii across, with a hard edge.)
                float coreTop = deep * lerp(1.03, 0.6, smoothstep(2.0, 3.6, x));
                float inside = min(min((r - inner) * 0.8, 4.0 * Rm - r), coreTop - above);
                float size = Rm;
                bool core = x < 4.5;
                // ---- the outer bands, from where the canopy thins outward: along each band a row of storm towers, one to every
                // 1.2 radii or so of its length, each an egg of its own width and height standing somewhere across the band's
                // width, and one place in four with none (a row of storms with gaps in it: drawn as one ridge along the band's
                // middle line, as it first was, a band seen from above was a hose laid on the sea); lower the further out
                float from = max(rim - 1.5, 4.6);
                { [loop] for (int once = x > from - 0.6 && x < 13.5 ? 0 : 1; once < 1; once++)
                {
                    float ends = smoothstep(from - 0.5, from + 1.0, x) * (1.0 - smoothstep(10.0, 13.0, x));
                    float half_ = Rm * (0.2 + 0.035 * x) * ends;
                    if (e.y > 1.6 * half_ + 0.3 * Rm || half_ < 1.0) { inside = max(inside, (1.6 * half_ + 0.3 * Rm - e.y) * 0.8 - 0.3 * Rm); break; }
                    // (How far along the band, counted in places for a tower: 39 of them to a turn round the eye, which is one to
                    // every 0.37 of the number that grows along a band, about 1.2 radii at eight radii out; numbered so that they
                    // come round with the angle. Not by how far out the place is, as it first was: that changes across a band
                    // far faster than along it, and every tower was a slab a kilometre thick, sheared flat.)
                    float v = eyeAlong / 0.370546, place = floor(v), fv = v - place, which = floor(e.w / PI + 0.5);
                    float span = 0.370546 * r / 2.508;
                    { [loop] for (int k = -1; k <= 1; k++)
                    {
                        float4 chance = TexelFetch(_CloudCells, int2(int(Mod(place + float(k), 39.0)) + 64, int(Mod(which, 2.0)) * 7 + 141), 0);
                        if (chance.a < 0.25) continue;
                        float Rt = min(half_ * (0.5 + 0.5 * chance.b), 0.48 * span);
                        float Ht = deep * lerp(0.9, 0.45, smoothstep(rim - 1.0, 12.0, x)) * (0.5 + 0.5 * chance.b);
                        float2 off = float2((fv - float(k) - 0.3 - 0.4 * chance.r) * span, eyeAcross - (chance.g - 0.5) * 1.3 * half_);
                        float one = eggAt(length(off), above, Rt, Ht);
                        if (one > inside) size = Rt;
                        inside = larger(inside, one, 0.3 * Rt);
                    } }
                } }
                float lump = 0.07 * Rm, reachOut = 0.6 * lump;
                // ---- the canopy: ice, from the wall out to the rim, its top sinking a little outward, its underside rising until
                // at the rim a thin sheet is left; the rim drawn out along the bands, and into streaks that wind the way the bands
                // do, more loosely (the air flows out along them)
                float out_ = smoothstep(1.2, rim, x);
                float under = lerp(0.70, 0.872, out_), over = 0.985 - 0.10 * out_;
                // (The bands' shields: the storms along each band throw their ice out aloft, and it runs together into a ribbon along the
                // band that reaches far past the canopy's rim (half and more of the cirrus round a hurricane comes from its bands: Nolan,
                // Fischer and O'Neill). So the canopy is drawn out along the two bands, deeper over a band's middle. From orbit a
                // hurricane is a white spiral, its arms the bands; with the canopy a round disc and the bands rows of single towers on
                // the open sea, it was a dinner plate with pebbles round it.)
                float onBand = (1.0 - smoothstep(0.45, 1.0, e.y / (Rm * (0.600 + 0.080 * x)))) * smoothstep(rim - 2.0, rim, x);
                under = lerp(under, 0.76, onBand * out_); over = lerp(over, 0.95, onBand * out_);
                float thick = (over - under) * deep;
                float streakTurn = eyeTurn - 1.2 * log(max(x, 0.5));
                // (fibres three degrees wide and some radii long, and with them the rim's wider tongues)
                float fibre = SampleLod(_CloudShape, float3(cos(streakTurn) * 3.2, sin(streakTurn) * 3.2, x * 0.07) + float3(0.23, 0.61, 0.07), 1.5 * _CloudHaze.x).r;
                float tongue = SampleLod(_CloudShape, float3(cos(streakTurn) * 1.1, sin(streakTurn) * 1.1, x * 0.05) + float3(0.61, 0.07, 0.23), 2.0 * _CloudHaze.x).r;
                float rimAt = rim + 0.9 * cos(2.0 * eyeTurn + 0.8) + (tongue - 0.74) * 9.0 + (fibre - 0.74) * 5.0;
                // (the arms ending by 13 radii, past which the cyclone is not asked about at all (cloudAt))
                rimAt = max(rimAt, min(rimAt + 8.00 * onBand * lerp(0.35, 1.0, smoothstep(0.6, 0.85, eyeBreak)), 13.0));
                // (its top in low ridges along the fibres)
                // (no ridge taller than the sheet is thick there: where one was, it cut the thin rim into ribbons with hard edges)
                float top_ = over * deep + (fibre - 0.74) * min(2200.0, 2.5 * thick);
                float anvil = min(min((rimAt - x) * Rm * 0.6, above - under * deep), min(top_ - above, (r - inner) * 0.8));
                if (seek) return rain > 0.0 || max(inside + reachOut, anvil + 0.1 * thick) + 1.5 * stride > 0.0 ? 1.0 : 0.0;
                float near = max((inside + reachOut + 6.0 + 0.6 * pixel) * 0.7, (anvil + 0.1 * thick + 6.0) * 0.7);
                if (rain > 0.0) near = 1e9;
                heapsNear = max(heapsNear, max(near, -0.3 * Rm));
                if (near < 0.0) return 0.0;
                // ---- the surface's relief, read in the cyclone's own frame. The core's goes round the eye with the wall's cloud and
                // is drawn out three times as long round the eye as it is deep or high, winding a little outward: the wall's
                // terraces, and the arcs of the tops round the eye. The bands' is a storm tower's lumps.
                float turned = core ? _CloudEyeD.x * _CloudEyeB.y : 0.0;
                float cs = cos(turned), sn = sin(turned);
                float3 own = float3(eyeXY.x * cs + eyeXY.y * sn, eyeXY.y * cs - eyeXY.x * sn, above) / N + wm.yzw * 0.3;
                float round_ = eyeTurn - 1.8 * log(max(x, 0.5)) - turned;
                float lodBig = max(lod + log2(2250.0 / lump) - 1.0, 0.0) * _CloudHaze.x;
                float3 part = clamp(size / (float3(1.3, 0.65, 0.33) * lump), 0.0, 1.0);
                // (the core's top bubbles as a tower's does, with a tower's round lumps; its wall is a slope, with half the relief
                // and little of the finer sizes, drawn out round the eye)
                float onTop = core ? smoothstep(-0.5 * lump, 0.5 * lump, (r - inner) * 0.8 - (coreTop - above)) : 1.0;
                float4 big = ((float4)(0.5));
                float moved = 0.0;
                { [loop] for (int top = onTop > 0.01 ? 0 : 1; top < 1; top++)
                {
                    big = SampleLod(_CloudShape, own * (2250.0 / lump) + float3(0.47, 0.11, 0.83), lodBig);
                    float3 domes = 1.0 - (1.0 - big.gba) * (1.0 - big.gba);
                    moved = dot(max(domes - 0.715, ((float3)(-0.3))) * part, float3(1.0, 0.5, 0.25)) * 1.6 * lump * onTop;
                } }
                { [loop] for (int face = onTop < 0.99 ? 0 : 1; face < 1; face++)
                {
                    float4 wide = SampleLod(_CloudShape, float3(cos(round_) * 1.19, sin(round_) * 1.19, (above + 0.6 * r) / (4.0 * lump)) + float3(0.47, 0.11, 0.83), lodBig);
                    float3 domes = 1.0 - (1.0 - wide.gba) * (1.0 - wide.gba);
                    moved += dot(max(domes - 0.715, ((float3)(-0.3))) * part, float3(1.0, 0.25, 0.08)) * 0.75 * lump * (1.0 - onTop);
                    big = lerp(wide, big, onTop);
                } }
                float soft = clamp((big.r - big.g) / max(1.0 - big.g, 0.02), 0.0, 1.0);
                if (fine)
                {
                    float cellSmall = lump / 8.0;
                    float3 small = SampleLod(_CloudDetail, own * (N / (cellSmall * 4.0)) + float3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / (cellSmall / 16.0)) + 0.3, 0.0, 6.0) * _CloudHaze.x).rgb;
                    moved += dot(small - 0.48, float3(1.0, 0.5, 0.3)) * lerp(0.45, 1.6, onTop) * cellSmall;
                }
                float tower = min(inside + moved, above - (soft - 0.4) * 160.0);
                float c = clamp(tower / max(12.0, 1.2 * pixelC) + 0.5, 0.0, 1.0) * clamp(0.3 + above / 90.0, 0.0, 1.0);
                // ---- the canopy's own look: ice, a third as thick as the towers' cloud, soft above; thinner and thinner towards
                // its rim, where it ends in nothing, not at an edge
                // (At its rim a twentieth of what it is at the wall: a fringe of ice lets the sun's disc through. At a fifth, as it
                // first was, each tongue of it seen from below was a solid blade with a shaded underside, like a wing.)
                float thin = (1.0 - 0.95 * smoothstep(0.45, 1.0, x / max(rimAt, 1.0))) * smoothstep(0.0, 1.6, rimAt - x) * lerp(1.0, clamp(0.4 + (fibre - 0.66) * 6.0, 0.25, 1.4), out_);
                // (and towards the rim it has no faces at all: it is thickest in the middle of its depth and fades to nothing above
                // and below, a veil. With faces, each streak of the fringe seen from below was a solid white ribbon.)
                float ice = clamp(anvil / max(lerp(min(0.22 * thick, 140.0), 0.5 * thick, out_), 1.2 * pixelC) + lerp(0.5, 0.0, out_), 0.0, 1.0) * 0.36 * strength * thin;
                if (ice > c) { c = ice; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = clamp(0.35 + (above - under * deep) / max(thick, 50.0), 0.0, 1.0); }
                else { heapsInside = tower; heapsSmooth = min(inside, above - (soft - 0.4) * 160.0); heapsOpen = lerp(core ? 0.75 : 0.5, 1.0, clamp(0.5 + moved / (0.5 * lump), 0.08, 1.0)) * clamp(0.2 + 0.9 * frac_, 0.0, 1.0); }
                c *= 2.0 - frac_;
                if (rain > c) { c = rain; heapsInside = -1e9; heapsSmooth = -1e9; heapsOpen = 0.3; }
                return c;
            }

            // ---------------------------------------------------------------- a squall line's shelf cloud
            //
            // Under a line of storms the rain drags cold air down, and it runs out ahead of them along the ground as a shallow
            // wedge with a sharp nose: the gust front. The warm air it runs into is lifted over the nose, and where it is lifted
            // past its condensation level a cloud forms along the whole front: a long, low wedge hung on the storms' base, its
            // leading edge the lowest and thinnest part of it, its upper face smooth and stepped in tiers (air lifted in layers,
            // evenly, all along the line), its underside torn and boiling (the cold air's own turbulence). It comes on at the
            // line's speed; under it the wind turns and the rain begins.
            //
            // Drawn where the weather has the front (Violent.SquallWind: so far ahead of the row of storms): from the nose back
            // to the storms, four and a half kilometres, a few hundred metres thick at the nose and under the storms' base there,
            // a kilometre and three quarters where it joins them.
            float shelfAt(float3 p, float h, float4 wm, bool fine, float lod)
            {
                float N = _CloudNoise.w;
                float3 mid = _CloudShelfA.xyz / max(length(_CloudShelfA.xyz), 1.0);
                float up = dot(p, mid);
                if (up <= 0.0) return 0.0;
                float3 flat_ = p - mid * up;
                float ahead = dot(flat_, _CloudShelfB.xyz), along = dot(flat_, cross(mid, _CloudShelfB.xyz));
                float base = _CloudShelfC.x, strength = smoothstep(0.1, 0.35, _CloudShelfB.w);
                bool seek = lod > 50.0;
                if (seek) lod -= 100.0;
                // (how far behind the nose, which is nowhere a straight line: it bulges ahead and hangs back by half a kilometre)
                float4 wave = SampleLod(_CloudShape, float3(along / 26000.0, 0.37, 0.61), 2.0 * _CloudHaze.x);
                float s = ahead - _CloudShelfC.y - (wave.r - 0.74) * 5000.0;
                float ends = 1.0 - smoothstep(0.4, 0.5, abs(along) / max(_CloudShelfA.w, 1.0));
                float deep = 8000.0;
                float out_ = max(max(s, -s - deep), max(base - 500.0 - h, h - base - 4100.0));
                if (out_ > 0.0 || ends * strength <= 0.0) { heapsNear = max(heapsNear, min(-out_, -1.0)); return 0.0; }
                float back = -s;
                // (the underside: lowest at the nose, rising to the storms' base three and a half kilometres behind it)
                float under = base - 380.0 * pow(1.0 - smoothstep(0.0, 3500.0, back), 1.3) * strength;
                // (How thick: a lip of two hundred metres at the nose, and within a kilometre and a half behind it a kilometre and
                // more, in three plates each set back on the one below: the stacked look of a shelf seen coming. Not thickening
                // evenly over its whole depth, as it first was: seen from in front that was a ramp, like a bank of snow.)
                float risen = 1.0 - exp(-back / 700.0);
                float plates = risen * 3.0, stair = (floor(plates) + smoothstep(0.35, 1.0, frac(plates))) / 3.0;
                // (and behind the plates the cloud climbs into the storms: a wall along the whole row, three and a half kilometres
                // high where the storms stand. A shelf hangs from the front of a line of cloud, it does not lie out in the open
                // with the sky over it: drawn alone, with gaps of sky between it and each storm, its upper face was a white
                // plateau seen from everywhere.)
                float wall = smoothstep(1800.0, 4500.0, back);
                float thick = (200.0 + 900.0 * lerp(risen, stair, 0.7) + 2600.0 * wall) * (1.0 - smoothstep(6500.0, 8000.0, back)) * (0.5 + 0.5 * ends) * (0.6 + 0.4 * strength);
                float over = under + thick;
                float inside = min(min(over - h, h - under), min(back, deep - back) * 0.7);
                float pixel = stormPixel(lod), pixelC = stormPixel(min(lod, stormLod));
                if (seek) return inside + 340.0 > 0.0 ? 1.0 : 0.0;
                float near = (inside + 330.0 + 0.6 * pixel) * 0.7;
                heapsNear = max(heapsNear, near);
                if (near < 0.0) return 0.0;
                // ---- its relief, read in the line's own frame (it goes with the line). The upper face smooth, in long streaks
                // along the line; the underside torn: lumps of a hundred and fifty metres, hanging.
                float3 own = float3(along, ahead, h) / N + wm.yzw * 0.2;
                float onTop = smoothstep(-60.0, 60.0, (h - under) - (over - h));
                float4 streaks = SampleLod(_CloudShape, float3(own.x * 0.35, own.y * 3.0, own.z * 3.0) + float3(0.29, 0.83, 0.17), max(lod, 0.0) * _CloudHaze.x);
                // (the plates smooth, in streaks along the line; the wall behind them, and the underside, lumpy: the wall as heaps
                // are, the underside torn)
                float smooth_ = onTop * (1.0 - wall);
                float moved = (streaks.r - 0.74) * 420.0 * smooth_;
                { [loop] for (int below = smooth_ < 0.99 ? 0 : 1; below < 1; below++)
                {
                    float lumps = lerp(13.0, 5.0, wall * onTop);
                    float4 torn = SampleLod(_CloudShape, own * lumps + float3(0.61, 0.07, 0.43), max(lod + log2(lumps) - 1.0, 0.0) * _CloudHaze.x);
                    float3 domes = 1.0 - (1.0 - torn.gba) * (1.0 - torn.gba);
                    moved += dot(max(domes - 0.715, ((float3)(-0.3))), float3(1.0, 0.5, 0.25)) * lerp(190.0, 520.0, wall * onTop) * (1.0 - smooth_);
                    if (fine) moved += dot(SampleLod(_CloudDetail, own * 60.0 + float3(0.0, 0.0, _CloudDrift.w), clamp(log2(pixel / 2.3) + 0.3, 0.0, 6.0) * _CloudHaze.x).rgb - 0.48, float3(1.0, 0.5, 0.3)) * 45.0 * (1.0 - onTop);
                } }
                float surface = inside + moved;
                float c = clamp(surface / max(10.0, 1.2 * pixelC) + 0.5, 0.0, 1.0) * ends * strength;
                heapsInside = surface; heapsSmooth = inside;
                // (it sees little sky from underneath, and more from its upper face)
                heapsOpen = lerp(0.25, 0.9, onTop);
                return c * 1.4;
            }

            // Under a raining storm, its rain: a grey curtain from the base to the ground, in streaks (the noise drawn out up and down),
            // thinning towards its edge and near the ground where the drops have spread out. Thin as cloud goes: heavy rain lets one see
            // a kilometre or two.
            float rainAt(float3 p, float h, float3 q, float4 a, float4 b)
            {
                float3 mid = a.xyz / max(length(a.xyz), 1.0);
                float d = length(p - mid * dot(p, mid));
                if (d > a.w * 0.75 || h > b.x || h < 0.0) return 0.0;
                // (the streaks read from the storm's own middle, drawn out ten times up and down)
                float3 pf = float3(dot(p, _CloudFixedX.xyz), dot(p, _CloudFixedY.xyz), dot(p, _CloudFixedZ.xyz));
                float3 up = pf / max(length(pf), 1.0);
                float3 mf = float3(dot(mid, _CloudFixedX.xyz), dot(mid, _CloudFixedY.xyz), dot(mid, _CloudFixedZ.xyz));
                float3 qs = (pf - mf * length(pf)) / _CloudNoise.w + up * (h / _CloudNoise.w) * 0.1;
                float streak = SampleLod(_CloudShape, qs * 2.3 + float3(0.7, 0.1, 0.4), 0.0).g;
                float fall = clamp(h / max(b.x, 100.0), 0.0, 1.0);
                return b.w * 0.08 * smoothstep(a.w * 0.75, a.w * 0.25, d) * smoothstep(0.35, 0.75, streak) * (0.45 + 0.55 * fall);
            }

            // How much cloud there is at a point (q in the noise, p from the planet's centre, h its height): the low clouds, the middle
            // ones, and the storms near the camera, whichever is the most.
            // How much of the noise's low cloud is left at a point, beside a heap that is made as smoke is made: none within its width
            // of the upright through its middle, all of it from 1.7 widths out.
            float holeAt(float3 p, float4 hole)
            {
                if (hole.w <= 0.0) return 1.0;
                float3 axis = normalize(_CloudCam.xyz + hole.xyz);
                return smoothstep(hole.w, hole.w * 1.7, length(p - axis * dot(p, axis)));
            }

            // ---------------------------------------------------------------- a craft's wake in cloud
            //
            // "Clouds swirl when you go through it, like the volumetric explosions" (the user, 2026-10-12; and 2026-10-11: "if a rocket
            // goes through it, can we have the deformation and curling like the volumetric explosions do?").
            //
            // What a body does to the air it goes through. It pushes the air aside and the air closes in behind it, but not to where
            // it was: what lay near its path has been carried some way along with it (the more the nearer), so a rocket that comes up
            // through a sheet draws a spike of the sheet up after it. Between the air it drags and the air beside that, which goes
            // back the other way, there is a shear that rolls up into rings round the path, one after another, like smoke rings:
            // those are the curls. The wake widens with time, its turbulence stirs what is in it, and after some minutes it has
            // faded into the cloud. A burning engine also burns the cloud off where its fire goes: a hole.
            //
            // Drawn by reading the cloud, near the path, not where a place is but where what is now there came from: back along the
            // path by the drag; round the nearest ring's middle by the roll; round the path itself by a twist; and a little way by
            // the stirring. With the hole taken out of what is found.

            float4 wakePlace(int k) { return k < 4 ? (k == 0 ? _CloudWake0 : k == 1 ? _CloudWake1 : k == 2 ? _CloudWake2 : _CloudWake3) : (k == 4 ? _CloudWake4 : k == 5 ? _CloudWake5 : k == 6 ? _CloudWake6 : _CloudWake7); }

            // For a place (from the camera, the planet's axes, m): where to read the cloud instead, as a step from the place; and how
            // much of what is found there is left (the hole).

            float3 wakeStep(float3 x, out float left)
            {
                left = 1.0;
                int count = int(_CloudWakeLook.x + 0.5);
                // (the path: the piece of it nearest this place; how far along the whole path, how far out, and how old there)
                float out_ = 1e9, along = 0.0, sofar = 0.0, age = 0.0, whole = 0.0;
                float3 axis = float3(0.0, 0.0, 1.0), foot = ((float3)(0.0));
                { [loop] for (int k = 0; k + 1 < count; k++)
                {
                    float4 a = wakePlace(k), b = wakePlace(k + 1);
                    float3 ab = b.xyz - a.xyz;
                    float L = max(length(ab), 1.0);
                    float3 ax = ab / L;
                    float s = clamp(dot(x - a.xyz, ax), 0.0, L);
                    float3 f = a.xyz + ax * s;
                    float r = length(x - f);
                    if (r < out_) { out_ = r; along = sofar + s; age = lerp(a.w, b.w, s / L); axis = ax; foot = f; }
                    sofar += L;
                } }
                whole = sofar;
                // (how wide the wake has grown: from its first width, as the cube root of its age and more; gone after four minutes)
                float first = _CloudWakeLook.y;
                float wide = first * (1.0 + 1.5 * pow(age / 6.0, 0.4));
                wakeOut = out_ - 3.2 * wide;
                if (wakeOut > 0.0) return ((float3)(0.0));
                float there = smoothstep(0.0, 2.5, age) * (1.0 - smoothstep(150.0, 240.0, age));
                // (and it ends as it began, not cut off: fading over a wake's width at each end of the path)
                there *= smoothstep(0.0, wide, along) * smoothstep(0.0, wide, whole - along);
                float3 radial = out_ > 0.01 ? (x - foot) / out_ : ((float3)(0.0));
                // (Nothing in a wake is even. Three numbers of the small noise, read on the wake's own edge abreast of this place (so
                // that they are the same all the way out from the path), say how far along this side's rings stand from where they
                // would, how hard each has rolled, and how wide the wake is on this side. With every ring whole, alike and evenly
                // spaced, as it first was, the wake was a screw: a thing turned on a lathe.)
                float3 own = SampleLod(_CloudDetail, (foot + radial * first) / (11.0 * first) + float3(0.41, 0.07, 0.63), 1.0).rgb;
                wide *= 0.7 + 1.3 * own.b;
                // ---- the rings: one every two and a half first widths along the path or so, each a roll about a circle round the
                // path at the wake's edge; what is near that circle has been carried round it, by more the nearer and the older (up
                // to a turn and a half), in the plane of the path and the way out from it
                float apart = 2.5 * first;
                float alongOwn = along + (own.r - 0.48) * 1.4 * apart;
                float ringAt = (floor(alongOwn / apart) + 0.5) * apart;
                float2 d = float2(alongOwn - ringAt, out_ - 0.8 * wide);
                float roll = there * min(1.6 * sqrt(age), 9.4) * clamp((own.g - 0.3) * 3.5, 0.0, 1.6) * exp(-dot(d, d) / (0.32 * apart * apart));
                float cs = cos(roll), sn = sin(roll);
                float2 was = float2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
                // ---- the drag: what is near the path has come from further back along it
                float drag = there * 5.0 * first * (0.5 + own.b) * exp(-out_ * out_ / (0.9 * wide * wide));
                float alongWas = ringAt + was.x + drag - (alongOwn - along), outWas = max(0.8 * wide + was.y, 0.0);
                // ---- the twist round the path itself (the cloud's lumps drawn out into a screw, one way on one side and the other
                // on another), and the stirring, which grows as the rings break up: three numbers of the small noise, read in the
                // wake, push the place about by a share of the wake's width
                float twist = there * 2.4 * (own.r - 0.48) * 2.0 * exp(-out_ * out_ / (1.6 * wide * wide));
                float3 side = cross(axis, radial);
                float3 radialWas = radial * cos(twist) + side * sin(twist);
                // (read coarsely: the stirring is of eddies a good part of the wake's width across; read finely, and pushed far, it
                // shredded the cloud into specks)
                float3 stir = (SampleLod(_CloudDetail, x / (16.0 * first) + float3(0.13, 0.57, 0.31) + axis * (age * 0.002), 1.5).rgb - 0.48) * (there * (1.2 + 1.6 * smoothstep(10.0, 90.0, age)) * wide * exp(-out_ * out_ / (2.0 * wide * wide)));
                // ---- the hole: what the fire burnt off and the craft blew out, closing over the minutes
                left = 1.0 - _CloudWakeLook.z * (1.0 - smoothstep(60.0, 220.0, age)) * smoothstep(0.0, wide, along) * smoothstep(0.0, wide, whole - along) * exp(-out_ * out_ / (0.5 * wide * wide));
                return (foot + axis * (alongWas - along) + radialWas * outWas + stir) - x;
            }

            float cloudAt(float3 q, float3 p, float h, float4 wa, float4 wb, float4 wc, float4 wm, bool fine, float lod)
            {
                heapsOpen = -1.0; heapsInside = -1e9; heapsNear = -1e9; heapsSmooth = -1e9;
                // (a craft's wake: only where there is one, and only near it)
                float left = 1.0;
                bool waked = false;
                { [loop] for (int once = _CloudWakeLook.x > 1.5 ? 0 : 1; once < 1; once++)
                {
                    float3 x = (q - _CloudNoise.xyz) * _CloudNoise.w;
                    float3 off = x - _CloudWakeMid.xyz;
                    if (dot(off, off) > _CloudWakeLook.w * _CloudWakeLook.w) break;
                    float3 step_ = wakeStep(x, left);
                    // (Outside the wake's own tube it moves nothing: the walk is told only how far off the tube is. Told to go carefully
                    // everywhere within the round space that holds the whole wake, as it was until 2026-10-15, a camera following a craft
                    // that had just climbed through cloud was in that space, a kilometre or two across, and every line of sight from it
                    // spent its steps there twenty-five metres at a time: from 2.3 km up, the deck above ended in a straight line some
                    // 13 km off, with clear sky under it to the horizon, the owner's "strip of light blue in the distance".)
                    if (wakeOut > 0.0) { heapsNear = max(heapsNear, -wakeOut); break; }
                    waked = true;
                    q += step_ / _CloudNoise.w;
                    float3 moved = step_.x * _CloudFixedX.xyz + step_.y * _CloudFixedY.xyz + step_.z * _CloudFixedZ.xyz;
                    h += dot(moved, p / max(length(p), 1.0));
                    p += moved;
                } }
                // (The low clouds, then the middle ones where there are any: in one loop, so that Windows' compiler, which puts every
                // function inline, makes one copy of layerAt here and not two. The streets are the low heaps': the middle clouds are not
                // set in them; the new shapes stand upright in the noise's own space, so each layer takes its own part of the pattern.)
                // (How much of the far field there is, too, is the thicker layer's: the walk's light goes by it.)
                float c = 0.0;
                { [loop] for (int L = 0; L < (wb.a > 0.004 ? 2 : 1); L++)
                {
                    bool low = L == 0;
                    float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, grown = farGrow;
                    float one = layerAt(q, p, low ? 1.0 : 2.0, h, low ? wa.x : wb.a, low ? wa.y : wc.x, low ? wa.z : wc.y, low ? wa.w : wc.z, low ? wb.x : wc.w, wm, fine, lod);
                    if (low) { c = one; if (c > 0.0) c *= holeAt(p, _CloudHoleA) * holeAt(p, _CloudHoleB) * holeAt(p, _CloudHoleC); }
                    else if (one > c) c = one;
                    else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; farGrow = grown; }
                } }
                // (each storm; whichever is thickest here says how much sky is seen and which way its surface faces)
                int count = storms();
                { [loop] for (int k = 0; k < count; k++)
                {
                    float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, storm = cellAt(p, h, q, stormA(k), stormB(k), wm, fine, lod);
                    if (storm > c) c = storm; else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; }
                } }
                // (a cyclone within sight: the layers' cloud is kept out of its wall, which is its own, and out of its eye above the
                // eye's floor; then its own cloud)
                { [loop] for (int once = _CloudEyeA.w > 0.0 ? 0 : 1; once < 1; once++)
                {
                    float4 e = eyeAt(p);
                    if (e.x > 13.5) break;
                    float low = _CloudEyeC.w, tall = max(_CloudEyeB.w - low, 1000.0);
                    float eyeOn = smoothstep(0.35, 0.6, _CloudEyeB.x);
                    float inner = _CloudEyeA.w * (0.62 + 0.55 * pow(clamp((h - low) / tall, 0.0, 1.0), 1.7));
                    float keep = e.z < inner ? 1.0 - smoothstep(low + 700.0, low + 1000.0, h) : smoothstep(1.5, 2.3, e.x);
                    c *= lerp(1.0, keep, eyeOn);
                    if (_CloudEyeD.y > 0.5) c = 0.0;        // (the lab's: the cyclone's own cloud alone)
                    float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, storm = cycloneAt(p, h, q, e, wm, fine, lod);
                    if (storm > c) c = storm; else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; }
                } }
                // (a squall line's shelf cloud, where one is within sight)
                { [loop] for (int once = _CloudShelfA.w > 0.0 ? 0 : 1; once < 1; once++)
                {
                    float open = heapsOpen, inside = heapsInside, plain = heapsSmooth, shelf = shelfAt(p, h, wm, fine, lod);
                    if (shelf > c) c = shelf; else { heapsOpen = open; heapsInside = inside; heapsSmooth = plain; }
                } }
                // (in a wake nothing is where its distance says: the walk is told to go carefully, and the light not to trust which
                // way a surface faces)
                if (waked) { heapsNear = max(heapsNear, -25.0); heapsInside = -1e9; heapsSmooth = -1e9; c *= left; }
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
            float3 sunThroughAir(float h, float up)
            {
                float2 uv = float2(clamp(up * 0.5 + 0.5, 0.0, 1.0), clamp(h / max(_CloudHigh.y, 1.0), 0.0, 1.0));
                return SampleLod(_CloudAirLut, uv, 0.0).rgb;
            }

            // Where a line (from o, along a unit d) crosses a sphere of radius r round the centre: the two distances, or -1 if never.
            float2 sphere(float3 o, float3 d, float r)
            {
                float b = dot(o, d);
                float c = dot(o, o) - r * r;
                float h = b * b - c;
                if (h < 0.0) return ((float2)(-1.0));
                h = sqrt(h);
                return float2(-b - h, -b + h);
            }

            // The same for the camera, done with its height (worked out in double precision): far steadier near the ground.
            float2 shellFromCamera(float mu, float hr)
            {
                float R = _CloudCam.w, alt = _CloudHigh.x;
                float b = (R + alt) * mu;
                float c = (alt - hr) * (2.0 * R + alt + hr);
                float h = b * b - c;
                if (h < 0.0) return ((float2)(-1.0));
                h = sqrt(h);
                return float2(-b - h, -b + h);
            }

            // How much of what is seen of the sea at a place, from the camera, is the sun's light on it there, as light (not as the
            // picture's numbers): what a cloud's shadow there takes away. The water's own blue (a clear deep sea's, as Natural Ocean
            // has it: by the eye's weights about a fortieth of what a white thing in the sun sends back), lit by the sun and by the
            // sky; the sky mirrored in the surface, which a shadow on the sea leaves as it is; the sun's glitter on the waves' facets
            // (Cox and Munk's spread of slopes, for a breeze of about 7 m/s), all of it the sun's; and in front of all of it the air's
            // own light, which no shadow on the sea takes away: the air from the camera down to the place as the walk has it (its
            // thickness on that line; its light as the clouds' haze has it, the sun's scattered towards the eye and the whole sky's).
            // In the game's measure, where a white thing facing the sun is 1. `dir`: the line of sight; `up`: up at the place; `h`:
            // its height; `sunHere`: the sun's light there.
            float seaSunShare(float3 dir, float3 up, float3 sunDir, float h, float3 sunHere)
            {
                const float3 eye = float3(0.2126, 0.7152, 0.0722);
                float sunL = max(dot(sunHere, eye), 1e-6), skyL = dot(_CloudSky.rgb, eye) / sunL;
                float muS = max(dot(up, sunDir), 0.0), muV = max(-dot(dir, up), 0.02);
                float body = 0.027 * (muS + skyL), bodySun = 0.027 * muS;
                float mirrored = (0.02 + 0.98 * pow(1.0 - muV, 5.0)) * skyL * 1.3;
                float3 facet = normalize(sunDir - dir);
                float muN = max(dot(facet, up), 0.05), slopes = 0.039;
                float glitter = muS > 0.0 ? (0.02 + 0.98 * pow(1.0 - clamp(dot(facet, sunDir), 0.0, 1.0), 5.0)) * exp(-(1.0 - muN * muN) / (muN * muN * slopes)) / (4.0 * slopes * muV * muN * muN * muN * muN) : 0.0;
                float rH = max(_CloudAirHigh.x, 100.0), hH = max(_CloudAirHigh.y, 100.0), hc = max(_CloudHigh.x, h + 1.0), hg = max(h, 0.0);
                float thick = (dot(_CloudAir.rgb, eye) * rH * (exp(-hg / rH) - exp(-hc / rH)) + _CloudAir.w * hH * (exp(-hg / hH) - exp(-hc / hH))) / max(muV, 0.08);
                float seen = exp(-thick);
                float cosView = dot(dir, sunDir), rayleigh = dot(_CloudAir.rgb, eye) + 1e-12;
                float once = (rayleigh * 3.0 / (16.0 * PI) * (1.0 + cosView * cosView) + _CloudAir.w * phase(cosView, 0.75)) / (rayleigh + _CloudAir.w);
                float airL = _CloudHaze.z * once + dot(_CloudSky.rgb, eye) * 2.2 * clamp(dot(normalize(_CloudCam.xyz), sunDir) * 3.0 + 0.3, 0.0, 1.0) / sunL;
                return clamp(seen * (bodySun + glitter) / max(seen * (body + mirrored + glitter) + (1.0 - seen) * airL, 1e-6), 0.0, 1.0);
            }

            float dither(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }

            // A storm's rain along this line of sight, walked apart from the clouds (it hangs below them, where the clouds' own walk does
            // not go): where the line passes through the curtain (an upright cylinder round the storm's middle, from the ground to its
            // base), eight steps, lit by the sky and the ground and a little of the sun that comes through the storm. Added in front of
            // the clouds.
            void rainWalk(float4 a, float4 b, float3 dir, float3 dirFixed, float3 sunTop, float3 sunDir, float stop, float jitter, inout float3 light, inout float through, inout float sumT, inout float sumW)
            {
                if (b.w < 0.1 || b.z < 0.15 || a.w <= 0.0) return;
                float R = _CloudCam.w;
                float3 mid = a.xyz / max(length(a.xyz), 1.0);
                float3 o = _CloudCam.xyz - mid * dot(_CloudCam.xyz, mid);
                float3 d = dir - mid * dot(dir, mid);
                float r = a.w * 0.75;
                float A = dot(d, d), B = dot(o, d), C = dot(o, o) - r * r;
                float disc = B * B - A * C;
                if (A < 1e-8 || disc <= 0.0) return;
                disc = sqrt(disc);
                float ta = max((-B - disc) / A, _CloudStage.z), tb = min((-B + disc) / A, stop);
                if (_CloudStage.w > 0.0) tb = min(tb, _CloudStage.w);
                if (tb <= ta) return;
                float dt = (tb - ta) / 8.0;
                { [loop] for (int k = 0; k < 8; k++)
                {
                    float t = ta + (float(k) + jitter) * dt;
                    float3 p = _CloudCam.xyz + dir * t;
                    float h = length(p) - R;
                    float3 q = _CloudNoise.xyz + dirFixed * (t / _CloudNoise.w);
                    float c = rainAt(p, h, q, a, b);
                    if (c <= 0.0) continue;
                    float sigma = c * _CloudLook.x;
                    float sunUp = dot(p / max(length(p), 1.0), sunDir);
                    float3 lit_ = _CloudSky.rgb * 0.8 * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) + _CloudGround.rgb + sunTop * sunThroughAir(h, sunUp) * 0.08 + ((float3)(_CloudSunTint.w));
                    float pass_ = exp(-sigma * dt);
                    light += through * lit_ * (1.0 - pass_);
                    sumT += t * through * (1.0 - pass_);
                    sumW += through * (1.0 - pass_);
                    through *= pass_;
                } }
            }

            // A tornado's funnel (or a waterspout's) along this line of sight, walked apart from the clouds as a storm's rain is: it
            // hangs under them, to the ground.
            //
            // What is seen of a tornado is not its wind but two things the wind makes. The funnel: cloud, where the air's pressure in
            // the turning core has fallen so far that it condenses below the storm's base; widest at the cloud, narrowing downward,
            // smooth-sided where the turning is steady and ragged where it is not, and reaching the ground only in a strong one (in a
            // weak one a stub under the cloud, with clear air turning unseen between it and the ground). And at its foot what it has
            // picked up: over land a low, wide whirl of dust and wreckage, darker than the funnel; over water a ring of spray. Both turn:
            // the pattern on them goes round, faster near the middle.
            void funnelWalk(float3 dir, float3 sunTop, float3 sunDir, float stop, float jitter, inout float3 light, inout float through, inout float sumT, inout float sumW)
            {
                float core = _CloudTwistA.w, tall = _CloudTwistB.x;
                if (core <= 0.0 || tall < 50.0) return;
                float foot = length(_CloudTwistA.xyz);
                float3 mid = _CloudTwistA.xyz / max(foot, 1.0);
                // (it leans and bends a little, the more the higher: its upper end is carried on by the storm, its foot dragged over the ground)
                float3 aside = normalize(cross(mid, float3(0.31, 0.87, 0.38)));
                float3 other = cross(mid, aside);
                // (the line against an upright cylinder that holds funnel, lean and whirl)
                float wide = 5.0 * core + 0.2 * tall;
                float3 o = _CloudCam.xyz - mid * dot(_CloudCam.xyz, mid);
                float3 d = dir - mid * dot(dir, mid);
                float A = dot(d, d), B = dot(o, d), C = dot(o, o) - wide * wide;
                float disc = B * B - A * C;
                if (A < 1e-8 || disc <= 0.0) return;
                disc = sqrt(disc);
                float ta = max((-B - disc) / A, _CloudStage.z), tb = min((-B + disc) / A, stop);
                if (_CloudStage.w > 0.0) tb = min(tb, _CloudStage.w);
                // (and between the ground and a little above the cloud's base)
                float climbs = dot(dir, mid), high0 = dot(_CloudCam.xyz, mid) - foot;
                if (abs(climbs) > 1e-4)
                {
                    float t0 = (-10.0 - high0) / climbs, t1 = (tall * 1.15 - high0) / climbs;
                    ta = max(ta, min(t0, t1)); tb = min(tb, max(t0, t1));
                }
                else if (high0 < -10.0 || high0 > tall * 1.15) return;
                if (tb <= ta) return;
                float dt = (tb - ta) / 32.0;
                float reach = clamp(_CloudTwistB.y, 0.0, 1.0);
                float3 dust = lerp(float3(0.42, 0.33, 0.25), float3(0.92, 0.95, 1.0), _CloudTwistB.z);
                { [loop] for (int k = 0; k < 32; k++)
                {
                    if (through < 0.02) break;
                    float t = ta + (float(k) + jitter) * dt;
                    float3 p = _CloudCam.xyz + dir * t;
                    float z = dot(p, mid) - foot, up = clamp(z / tall, 0.0, 1.0);
                    float3 from = p - mid * dot(p, mid) - (aside * (0.22 * tall * up * up) + other * (0.06 * tall * sin(up * 5.0 + _CloudTwistB.w * 0.05)));
                    float r = length(from);
                    // (the pattern goes round with the air: faster near the middle, and wound up the funnel)
                    float turn = _CloudTwistB.w * clamp(core / max(r, 0.4 * core), 0.15, 2.5) + up * 9.0;
                    float cs = cos(turn), sn = sin(turn);
                    float e = dot(from, aside), n = dot(from, other);
                    float3 round = float3(e * cs - n * sn, e * sn + n * cs, z);
                    float rough = SampleLod(_CloudDetail, round / (core * 5.0) + float3(0.13, 0.57, 0.29), 0.0).r;
                    float fine = SampleLod(_CloudDetail, round / (core * 1.3) + float3(0.61, 0.11, 0.83), 0.0).g;
                    // ---- the funnel: its radius by height (widest at the cloud), cloud only from the cloud down as far as it reaches
                    float radius = core * (0.4 + 3.6 * pow(up, 2.4)) * (0.8 + 0.5 * rough);
                    float low = tall * (1.0 - reach);
                    float shown = smoothstep(low - 0.04 * tall, low + 0.1 * tall, z) * step(z, tall * 1.12);
                    // (a strong one narrows to the ground and stays as wide as its core there; the tip of one that does not reach tapers)
                    radius *= lerp(0.35 + 0.65 * smoothstep(low, low + 0.25 * tall, z), 1.0, smoothstep(0.92, 1.0, reach));
                    // (its side is a surface: cloud begins within a few metres, ragged by a tenth of its width)
                    float funnel = smoothstep(radius, radius * 0.86 - 4.0, r * (0.92 + 0.16 * fine)) * shown;
                    // ---- the whirl at its foot: low and wide, thick near the ground
                    float whirl = smoothstep(3.6 * core, 1.0 * core, r * (0.8 + 0.5 * rough)) * exp(-max(z, 0.0) / (1.5 * core + 12.0)) * (0.35 + 0.9 * fine) * smoothstep(0.15, 0.5, reach + 0.3) * step(-10.0, z);
                    float c = max(funnel, 0.0);
                    float both = c + whirl * 0.7;
                    if (both <= 0.002) continue;
                    float h = length(p) - _CloudCam.w;
                    float sunUp = dot(p / max(length(p), 1.0), sunDir);
                    float3 sunHere = sunTop * sunThroughAir(h, sunUp);
                    // (lit as a pillar is: the side towards the sun bright, the other in its own shade; and it stands under a storm)
                    float3 out_ = from / max(r, 1.0);
                    float facing = clamp(dot(out_, sunDir) * 0.6 + 0.4, 0.0, 1.0);
                    float3 lit_ = _CloudSky.rgb * 0.7 * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) + _CloudGround.rgb + sunHere * (0.08 + 0.45 * facing) + ((float3)(_CloudSunTint.w));
                    lit_ *= lerp(dust, ((float3)(1.0)), c / max(both, 1e-4));
                    float sigma = both * _CloudLook.x * 0.8;
                    float pass_ = exp(-sigma * dt);
                    light += through * lit_ * (1.0 - pass_);
                    sumT += t * through * (1.0 - pass_);
                    sumW += through * (1.0 - pass_);
                    through *= pass_;
                } }
            }

            // What was kept, read between its pixels by Catmull and Rom's curve, not straight between the four nearest: read straight,
            // each frame's reading of the last frame's picture between its pixels blurs it a little, and with the camera turning, frame
            // after frame of it blurred the clouds' lumps away (in the lab, 90 degrees a second: the kept picture 2 to 5 % nearer the
            // truth so; held still, the same to the bit). Five readings by the card's own blending, the corners left out (Jimenez).
            float4 keptAt(float2 uv)
            {
                float2 size = ((float2)(TextureSize(_CloudHist, 0)));
                float2 pos = uv * size, c = floor(pos - 0.5) + 0.5, f = pos - c;
                float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f)), w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
                float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f)), w3 = f * f * (-0.5 + 0.5 * f);
                float2 w12 = w1 + w2, t0 = (c - 1.0) / size, t3 = (c + 2.0) / size, t12 = (c + w2 / w12) / size;
                float4 r = SampleLod(_CloudHist, float2(t12.x, t0.y), 0.0) * (w12.x * w0.y) + SampleLod(_CloudHist, float2(t0.x, t12.y), 0.0) * (w0.x * w12.y)
                       + SampleLod(_CloudHist, t12, 0.0) * (w12.x * w12.y)
                       + SampleLod(_CloudHist, float2(t3.x, t12.y), 0.0) * (w3.x * w12.y) + SampleLod(_CloudHist, float2(t12.x, t3.y), 0.0) * (w12.x * w3.y);
                return r / (w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y);
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
                float stage = _CloudStage.x;

                // ---------------------------------------------------------------- 0, 1: the noise
                if (stage < 1.5)
                {
                    float size = stage < 0.5 ? 128.0 : 64.0;
                    float3 p = float3(IN.pos.xy / size, (_CloudStage.y + 0.5) / size);
                    if (stage < 0.5)
                    {
                        float smoothSum = 0.0, amp = 1.0, total = 0.0;
                        { [loop] for (int o = 0; o < 4; o++)
                        {
                            int period = 4 << o;
                            // (each octave's lattice moved off the others': Perlin's noise is nought at every point of its lattice, and with
                            // the lattices of all four on top of one another the sum was nought on the coarsest one's, a regular grid every
                            // 2.25 km, which showed as rows of heaps along the horizon and a lattice from orbit)
                            float3 off = frac(float3(0.137, 0.713, 0.371) * float(o + 1) * 3.17);
                            smoothSum += smoothNoise(p * float(period) + off, period, 11u + uint(o)) * amp;
                            total += amp; amp *= 0.5;
                        } }
                        float soft = clamp(smoothSum / total * 0.5 + 0.5, 0.0, 1.0);
                        float c4 = cellsLayered(p, 4, 101u), c8 = cellsLayered(p, 8, 211u), c16 = cellsLayered(p, 16, 307u);
                        // (Perlin's noise swelled by the cells: rounded heaps, with the cells' billows on them)
                        float heaps = clamp(remap(soft, 0.0, 1.0, c4, 1.0), 0.0, 1.0);
                        result = float4(heaps, c4, c8, c16);
                    }
                    else
                    {
                        result = float4(cellsLayered(p, 4, 401u), cellsLayered(p, 8, 503u), cells(p * 16.0, 16, 601u) * 0.7 + cells(p * 32.0, 32, 607u) * 0.3, 1.0);
                    }
                    result1 = ((float4)(0.0));
                    return Both(result, result1);
                }

                float R = _CloudCam.w;

                // ---------------------------------------------------------------- 5: the map of the lattice's heaps
                // Once a frame, for each dot of a map that lies over the ground round the camera (on the face of the cube the camera
                // is nearest, and coming round every so many lengths of the noise, so that a place is found in it by where it is in the
                // noise and nothing has to be moved when the camera moves): the low layer's heap of the lattice that has that place or
                // stands nearest, as heapsMap gives it. Every asking then reads its heap here. (Worked out at every asking, as it was
                // until the evening of 2026-10-12, the lattice doubled what the clouds cost: by count nearly everything asked is clear
                // air, and each asking had three points of the lattice to look at to learn it.)
                // ---------------------------------------------------------------- 9: the sun-side light field (2026-10-18)
                // Each cell asks the clouds towards the sun from its middle, past the walk's first step that way (28 m): four readings in each
                // of the three stretches the walk's own steps end at (154, 346, 711 m on), _CloudLightF.w beyond (8 if nought), each as much
                // longer than the last as brings them to where the line leaves the shell; all with the cell's own weather, as the walk's
                // steps that way use the lit place's; each cell at its own fixed place in each stretch. Four tiles across, a level under the
                // last. What is kept is the light let through, exp(-0.1 e-folds) (read back between cells as that: between a sunlit cell and
                // one deep in a heap, a blend of the light let through is truer at a surface than a blend of the light lost). The walk reads
                // it after its own first step (see the walk), in place of its other steps that way, its far looks and the shadow's second map:
                // in the lab, light closer to the truth (11.4 against 16.8 / 255 in a storm, 7.0 against 11.4 under heaps) for 10-31 %
                // less of the walk (research/rendering/experiments/cloud-light-field.md).
                if (stage > 8.5)
                {
                    int n = int(_CloudLightF.x + 0.5), rows = int(_CloudLightF.z + 0.5) / 4;
                    int2 px = ((int2)(IN.pos.xy));
                    int level = px.y / (rows * n), ly = px.y - level * rows * n;
                    int k = (ly / n) * 4 + px.x / n;
                    float3 C = lightCellAt(level, px.x % n, ly % n, k);
                    float3 rel = C - _CloudCam.xyz;
                    float3 relFixed = float3(dot(rel, _CloudFixedX.xyz), dot(rel, _CloudFixedY.xyz), dot(rel, _CloudFixedZ.xyz));
                    float3 q = _CloudNoise.xyz + relFixed / _CloudNoise.w;
                    float dist = length(rel);
                    // (the near shapes or the far field as the walk chooses them at this cell, by how wide a pixel of its picture is there:
                    // see formChoice)
                    formChoice = _CloudJitter.w > 0.0 ? dist * _CloudJitter.w : -1.0;
                    float4 wa, wb, wc;
                    weatherAt(C, wa, wb, wc);
                    if (_CloudForm.x > 0.5) formWeather(C, wa, wb, wc);
                    float4 wm0 = broadAt(q);
                    float4 wm = float4(wm0.x * streetsAt(q, wm0), wm0.yzw);
                    if (_CloudForm.x > 0.5) formStep(q, wm, dist);
                    float3 sunDir = _CloudSun.xyz, sunFixed = _CloudSunFixed.xyz;
                    formWay = sunFixed;
                    float lod = min(log2(max(dist, 1.0) / 25000.0), 4.0);
                    float place = frac(52.9829189 * frac(dot(((float2)(px)), float2(0.06711056, 0.00583715))));
                    float4 lost = ((float4)(0.0));
                    { [loop] for (int seg = 0; seg < 3; seg++)
                    {
                        float a0 = seg == 0 ? 0.0 : seg == 1 ? 154.0 : 346.0, a1 = seg == 0 ? 154.0 : seg == 1 ? 346.0 : 711.0;
                        float len = (a1 - a0) / 4.0, sum = 0.0;
                        { [loop] for (int s = 0; s < 4; s++)
                        {
                            float at = 28.0 + a0 + (float(s) + place) * len;
                            float3 lp = C + sunDir * at;
                            sum += cloudAt(q + sunFixed * (at / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 0.5) * len;
                        } }
                        if (seg == 0) lost.r = sum; else if (seg == 1) lost.g = sum; else lost.b = sum;
                    } }
                    float b2 = dot(C, sunDir), c2 = dot(C, C) - (R + _CloudShell.y) * (R + _CloudShell.y);
                    float exitAt = -b2 + sqrt(max(b2 * b2 - c2, 0.0));
                    if (exitAt > 739.0)
                    {
                        int farN = _CloudLightF.w > 0.5 ? int(_CloudLightF.w) : 8;
                        float grow = exp2(8.0 / float(farN)), str = (exitAt - 739.0) * (grow - 1.0) / (pow(grow, float(farN)) - 1.0), at0 = 739.0;
                        { [loop] for (int s = 0; s < 32; s++)
                        {
                            if (s >= farN) break;
                            float at = at0 + str * place;
                            float3 lp = C + sunDir * at;
                            lost.a += cloudAt(q + sunFixed * (at / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 1.0) * str;
                            at0 += str; str *= grow;
                            place = frac(place + 0.618);
                        } }
                    }
                    result = exp(-0.1 * lost * _CloudLook.x);
                    result1 = ((float4)(0.0));
                    return Both(result, result1);
                }

                // ---------------------------------------------------------------- 6: the low clouds' map, smoothed
                // Once a frame, the weather's map of the low clouds read smoothly (mapSmooth) into one four times as fine, which every
                // asking then reads plainly (weatherAt). Here `_CloudMap` is the weather's own.
                if (stage > 5.5)
                {
                    result = mapSmooth(_CloudMap, sampler_CloudMap, uv);
                    result1 = ((float4)(0.0));
                    return Both(result, result1);
                }

                if (stage > 4.5)
                {
                    float N = _CloudNoise.w, E = _CloudHeapsAt.y, face = _CloudHeapsAt.w;
                    float3 camF = float3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
                    float3 camIn = faceOf(_CloudNoise.xyz, face);
                    float2 across = camIn.xy + E * (frac(uv - camIn.xy / E + 0.5) - 0.5);
                    float4 wa, wb, wc;
                    weatherAt(_CloudCam.xyz, wa, wb, wc);
                    // (the place's third number: it is on the ball of the layer's base. From the planet's centre, a place in the noise
                    // is as many lengths on, plus how far the wind has carried the noise.)
                    float3 carried = faceOf(camF - _CloudNoise.xyz * N, face);
                    float ball = R + wa.z;
                    float2 level = across * N + carried.xy;
                    float third = sqrt(max(ball * ball - dot(level, level), 1.0)) * sign(faceOf(camF, face).z);
                    float3 P = unfaceOf(float3(level, third), face);
                    float3 col = unfaceOf(float3(across, (third - carried.z) / N), face);
                    float3 p = P.x * _CloudFixedX.xyz + P.y * _CloudFixedY.xyz + P.z * _CloudFixedZ.xyz;
                    weatherAt(p, wa, wb, wc);
                    float4 wm0 = broadAt(col);
                    formWeather(p, wa, wb, wc);
                    // (no heaps past 0.48 of the map's width from the camera: the walk's own fading of them with distance, drawn in
                    // so far that it has ended before the map comes round)
                    // (from above all the clouds: how far over the ground from under the camera, as the walk goes by the distance it runs
                    // inside the shell, which from high above is short wherever the map lies)
                    float fromCam = lerp(length(P - camF), length(across - camIn.xy) * N, aboveness());
                    formStep(col, float4(wm0.x * streetsAt(col, wm0), wm0.yzw), fromCam * (26000.0 / (0.48 * E * N)));
                    float4 K = formKind[0], Z = formSize[0], Hh = formHeap[0];
                    float4 best = float4(1e5, 0.0, 0.0, 0.0), padsBest = float4(-1e5, 0.0, 0.0, 0.0);
                    float raisedBest = 0.0;
                    // (both sizes of lattice where two are there, and both faces near where two meet: the heap whose edge is nearer has
                    // the place, and where the edges of two are as near as one another within a join's width, something of both, as
                    // heapsMap does among the heaps of one lattice)
                    { [loop] for (int which = 0; which < 2; which++)
                    {
                        float perF = which == 0 ? Z.y : Z.z, weight = which == 0 ? 1.0 - Z.w : Z.w;
                        if (weight < 0.001 || formRaw[0].z < 0.5 || K.y > 0.999) continue;
                        float C = N / perF;
                        float tall = lerp(1.05 * K.z / (0.8 * C), clamp(1.05 * formMore[0].z / (0.84 * C), 0.8 * Z.x, 1.25 * Z.x), K.x);
                        { [loop] for (int side = 0; side < 2; side++)
                        {
                            float mine = side == 0 ? formMine : 1.0 - formMine, f = side == 0 ? formFace : formOther;
                            if (mine < 0.001) continue;
                            float3 cf = faceOf(col, f), uf = faceOf(formUp, f);
                            float raised;
                            // (pads: their cells' columns (padsColumn), the one the place is further inside having it)
                            if (K.x < 0.5)
                            {
                                float2 puv = cf.xy * perF + float2(7.3, 3.1) * f;
                                float4 bend = SampleLod(_CloudShape, float3(puv * 0.17, 0.37), 1.5);
                                float crack = C * lerp(0.42, 0.012, ramp(0.05, 0.75, formMore[0].x));
                                float4 cell = padsColumn(puv + (bend.gb - 0.48) * 1.3, -uf.xy / uf.z, C, int(perF + 0.5), _CloudForm.y, Hh.x * weight * mine, crack, min(K.z, 560.0), 0.05, raised);
                                if (padsBest.x < -1e4 || cell.x > padsBest.x) { padsBest = cell; raisedBest = raised; }
                                continue;
                            }
                            float4 one = heapsMap(cf.xy * perF + float2(7.3, 3.1) * f, -uf.xy / uf.z, C, int(perF + 0.5), _CloudForm.y, Hh.x * weight * mine, C * Hh.y, Hh.z, C * Hh.w, tall, raised);
                            if (one.y <= 0.0) { if (best.y <= 0.0) best.x = min(best.x, one.x); continue; }
                            if (best.y <= 0.0) { best = one; raisedBest = raised; continue; }
                            float gapA = best.x - best.y, gapB = one.x - one.y, k = max(C * Hh.w, 1.0);
                            float h = clamp(0.5 + 0.5 * (gapB - gapA) / k, 0.0, 1.0);
                            best.yzw = lerp(one.yzw, best.yzw, h); raisedBest = lerp(raised, raisedBest, h);
                            best.x = max(best.y + lerp(gapB, gapA, h) - k * h * (1.0 - h), 0.0);
                        } }
                    } }
                    result = K.x < 0.5 ? padsBest : best;
                    result1 = float4(raisedBest, 0.0, 0.0, 1.0);
                    return Both(result, result1);
                }

                // ---------------------------------------------------------------- 4: the shadow, from points on the ground towards the sun
                if (stage > 3.5)
                {
                    // (An eighth of this map is drawn each frame, one strip of eight in turn, by a rectangle set outside the shader:
                    // clouds, sun and wind move a dot of it in seconds, not in a seventh of one, and the whole of it every frame
                    // cost the new shapes a twelfth of the frames. A quarter until 2026-10-18, when the clouds began to be read here as
                    // the walk draws them, which costs half as much again a dot. NOT by throwing dots away here: with a `discard` anywhere in
                    // this shader, this Mac's OpenGL (which is Metal underneath) cannot make the program ready for the card at all
                    // ("buildPipelineState failed") and draws with it in software, saying so only in the game's Player.log; for a
                    // night the game drew its clouds so, and the lab hung.)
                    float3 g = _CloudShadowAt.xyz + (_CloudShadowX.xyz * (uv.x - 0.5) + _CloudShadowY.xyz * (uv.y - 0.5)) * _CloudShadowAt.w;
                    g = normalize(g) * (R + _CloudShadowX.w);
                    float3 s = _CloudSunFixed.xyz;
                    // (The clouds read here as the walk draws them at each place: at the width of a pixel of its picture there (Clouds.cs gives
                    // it, for a metre of distance, as _CloudJitter.w), so with the same shapes and the same detail, the lumps on the heaps
                    // within 40 km as the walk has them, and the heaps giving way with distance as the walk's do (formStep told the distance,
                    // from below the clouds; from above, as the walk from above, not). Read coarsely everywhere, as until 2026-10-18 (at 0.65,
                    // without lumps), a heap's shadow was a soft round blot under a heap with a cauliflower's edge: "the clouds' shadows are
                    // very janky. They aren't actually what the clouds are like", the owner, 2026-10-18. Measured in the lab against shadows
                    // walked finely from every pixel of the ground: a third as far off as with the choice of near or far alone, and as steady
                    // as the drawn clouds while they drift. _CloudJitter.z 1: without the lumps, for a map drawn whole in one frame, when
                    // its square has been moved on or in a fast warp.)
                    bool asDrawn = _CloudJitter.w > 0.0;
                    // (g is in the planet's own axes; the weather map is read in the scene's, so it is turned back for that)
                    float3 gScene = g.x * _CloudFixedX.xyz + g.y * _CloudFixedY.xyz + g.z * _CloudFixedZ.xyz;
                    float3 sScene = s.x * _CloudFixedX.xyz + s.y * _CloudFixedY.xyz + s.z * _CloudFixedZ.xyz;
                    // (through the shell as eased (_CloudShellEased): its 24 steps are laid between the floor and the top, and with the
                    // shell's own, which jumps when the weather's map is made again or a storm grows, every one of them moved, and the
                    // clouds' light and their shadow on the ground jumped with them)
                    float2 inner = sphere(gScene, sScene, R + _CloudShellEased.x);
                    float2 outer = sphere(gScene, sScene, R + _CloudShellEased.y);
                    float t0 = max(inner.y > 0.0 && dot(gScene, gScene) < (R + _CloudShellEased.x) * (R + _CloudShellEased.x) ? inner.y : 0.0, 0.0);
                    float t1 = outer.y;
                    float depth = 0.0, lowest = 99999.0, highest = 0.0;
                    // (And for the walk, which asks how much cloud lies between a place in a cloud and the sun further off than its own
                    // few steps that way reach: how high the line is where, coming down from the sun, it is first a depth of one into
                    // cloud, and of six; and how much cloud is on it in all. The line is therefore walked from its far end downward.)
                    float fromTop = 0.0, high1 = -1000.0, high6 = -1000.0;
                    if (t1 > t0 && dot(normalize(gScene), sScene) > -0.2)
                    {
                        t1 = min(t1, t0 + 30000.0);
                        float dt = (t1 - t0) / 24.0;
                        float4 shadowWm = float4(1.0, 0.0, 0.0, 0.0);
                        // (the noise is moved as the camera's place in it is: the shadow's square is fixed to the planet)
                        float3 camFixed = float3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
                        float clearT = t1;
                        float4 wa = ((float4)(0.0)), wb = ((float4)(0.0)), wc = ((float4)(0.0));
                        { [loop] for (int n = 0; n < 24; n++)
                        {
                            float t = t1 - (float(n) + 0.5) * dt;
                            float3 p = gScene + sScene * t;
                            float h = length(p) - R;
                            float3 q = _CloudNoise.xyz + (g + s * t - camFixed) / _CloudNoise.w;
                            if (n == 0) shadowWm = broadAt(_CloudNoise.xyz + (g + s * (0.5 * (t0 + t1)) - camFixed) / _CloudNoise.w);
                            // (the noise read a little coarsely, but not so coarsely that the far heaps' third reading comes in: the shadow is
                            // of the heaps near the camera, which are drawn without it)
                            float4 stepWm = float4(shadowWm.x * streetsAt(q, shadowWm), shadowWm.yzw);
                            // (the weather read at every sixth step, and what the new shapes go by worked out there: along these few
                            // kilometres it hardly changes, and worked out at all twenty-four it was most of what this map cost)
                            { [loop] for (int read = (n % 6) == 0 ? 0 : 1; read < 1; read++)
                            {
                                weatherAt(p, wa, wb, wc);
                                { [loop] for (int once = _CloudForm.x > 0.5 ? 0 : 1; once < 1; once++) { formWeather(p, wa, wb, wc); formPer = asDrawn ? _CloudJitter.w : formPer; formStep(q, stepWm, asDrawn ? length(p - _CloudCam.xyz) * (1.0 - aboveness()) : 0.0); } }
                            } }
                            // (the near shapes or the far field as the walk chooses them for this place, by how wide a pixel of its picture is
                            // here (Clouds.cs gives it as _CloudJitter.w): by the map's own 512 dots, it was the far field everywhere)
                            float fromCam = length(p - _CloudCam.xyz);
                            formChoice = asDrawn ? fromCam * _CloudJitter.w : -1.0;
                            float lodS = asDrawn ? min(log2(max(fromCam, 1.0) / 25000.0), 4.0) : 0.65;
                            bool fineS = asDrawn && _CloudJitter.z < 0.5 && fromCam < 40000.0;
                            float c = cloudAt(q, p, h, wa, wb, wc, stepWm, fineS, lodS);
                            if (c <= 0.0) { clearT = t; continue; }
                            // (an eighth of the cloud's own thickness: the walk here is coarse and without the small noise, and a shadow taken at
                            // full strength is all or nothing, with the edge of each dot of the square in it)
                            float add = c * _CloudLook.x * dt;
                            depth += add * 0.12;
                            lowest = min(lowest, h);
                            highest = max(highest, h);
                            // (the first cloud from the top: where between the last clear place and this one it begins, halved three
                            // times; a depth of one is some metres further in)
                            float topT = t + 0.5 * dt;
                            { [loop] for (int first = high1 < -500.0 ? 0 : 1; first < 1; first++)
                            {
                                float in_ = t, out_ = min(clearT, t + dt);
                                { [loop] for (int b = 0; b < 3; b++)
                                {
                                    float mid = 0.5 * (in_ + out_);
                                    float3 mp = gScene + sScene * mid;
                                    float mc = cloudAt(_CloudNoise.xyz + (g + s * mid - camFixed) / _CloudNoise.w, mp, length(mp) - R, wa, wb, wc, stepWm, fineS, lodS);
                                    if (mc > 0.0) in_ = mid; else out_ = mid;
                                } }
                                high1 = length(gScene + sScene * (in_ - 1.0 / max(c * _CloudLook.x, 0.002))) - R;
                                // (what of this step lies above where the cloud begins is not cloud)
                                topT = in_;
                                add = c * _CloudLook.x * max(in_ - (t - 0.5 * dt), 0.0);
                            } }
                            { [loop] for (int sixth = high6 < -500.0 && fromTop + add >= 6.0 ? 0 : 1; sixth < 1; sixth++)
                                high6 = length(gScene + sScene * clamp(topT - (6.0 - fromTop) / max(c * _CloudLook.x, 0.002), t - 0.5 * dt, topT)) - R; }
                            fromTop += add;
                        } }
                    }
                    result = float4(depth, lowest / 1000.0, highest / 1000.0, 1.0);
                    result1 = float4(high1 / 1000.0, high6 / 1000.0, min(max(fromTop, 0.0), 60.0), 1.0);
                    return Both(result, result1);
                }

                // The line of sight through this pixel.
                // (The walk's lines are moved off their pixels' middles, a different way each frame: see where the frames are taken
                // together.)
                float2 aim = stage < 2.5 ? uv + _CloudJitter.xy : uv;
                float3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (aim.x * 2.0 - 1.0) + _CloudUp.xyz * (aim.y * 2.0 - 1.0));

                // ---------------------------------------------------------------- 3: taken together over frames
                if (stage > 2.5)
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
                        float3 d = float3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
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
                                float4 before = clamp(keptAt(was), lo, hi);
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

                // ---------------------------------------------------------------- 2: the walk
                float mu = dot(normalize(_CloudCam.xyz), dir);
                float2 inner = shellFromCamera(mu, _CloudShell.x);
                float2 outer = shellFromCamera(mu, _CloudShell.y);
                float2 ground = shellFromCamera(mu, 0.0);
                float alt = _CloudHigh.x;
                float t0, t1;
                if (alt > _CloudShell.y)
                {
                    // above the clouds: from where the line comes into the shell to where it reaches its floor, or leaves again
                    if (outer.x < 0.0) { result = float4(0.0, 0.0, 0.0, 1.0); result1 = ((float4)(0.0)); return Both(result, result1); }
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
                    if (ground.x > 0.0) { result = float4(0.0, 0.0, 0.0, 1.0); result1 = ((float4)(0.0)); return Both(result, result1); }
                    t0 = inner.y;
                    t1 = outer.y;
                }
                if (ground.x > 0.0) t1 = min(t1, ground.x);
                float t0e = t0, t1e = t1;
                {
                    float2 innerE = shellFromCamera(mu, _CloudShellEased.x), outerE = shellFromCamera(mu, _CloudShellEased.y);
                    if (alt > _CloudShellEased.y) { t0e = outerE.x >= 0.0 ? outerE.x : t0; t1e = innerE.x > 0.0 ? innerE.x : outerE.y; }
                    else if (alt > _CloudShellEased.x) { t0e = 0.0; t1e = innerE.x > 0.0 ? innerE.x : outerE.y; }
                    else { t0e = innerE.y; t1e = outerE.y; }
                    if (ground.x > 0.0) t1e = min(t1e, ground.x);
                    t1e = max(t1e, t0e + 1.0);
                }
                // What the camera has drawn in the way (its depth picture: the farthest of the four pixels this one stands for).
                float stop = 1e9;
                if (_CloudRight.w > 0.5)
                {
                    int2 full = TextureSize(_CameraDepthTexture, 0);
                    int2 at = clamp(((int2)((IN.pos.xy - 0.5) * ((float2)(full)) / max(float2(_CloudHigh.w, _CloudHigh.w * float(full.y) / float(full.x)), ((float2)(1.0))))), ((int2)(0)), full - 2);
                    float a = TexelFetch(_CameraDepthTexture, at, 0).r, b = TexelFetch(_CameraDepthTexture, at + int2(1, 0), 0).r;
                    float c = TexelFetch(_CameraDepthTexture, at + int2(0, 1), 0).r, e = TexelFetch(_CameraDepthTexture, at + int2(1, 1), 0).r;
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
                if (t1 <= t0) { result = float4(0.0, 0.0, 0.0, 1.0); result1 = ((float4)(0.0)); return Both(result, result1); }

                // Steps: short near the camera, longer far away; never more than so many.
                float length0 = min(t1e, t0e + 90000.0) - t0e;
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
                uint2 pix = ((uint2)(IN.pos.xy));
                float own = float(mixBits(pix.x + mixBits(pix.y + 7919u)) & 65535u) / 65536.0;
                float jitter = frac(own + 0.61803399 * _CloudStage.y);
                float t = firstStep(t0, jitter, least, longest, each);
                stepLen = clamp(t * each, least, longest);

                float3 sunDir = _CloudSun.xyz;
                float cosView = dot(dir, sunDir);
                // light that goes on mostly straight ahead, some of it back (kept apart: what goes straight on is dimmed less by the cloud
                // it passes, see the silver lining below); and again for light already scattered (spread more evenly)
                float phOn = 0.73 * phase(cosView, 0.86), phBack = 0.27 * phase(cosView, -0.22);
                float ph2 = lerp(phase(cosView, 0.43), phase(cosView, -0.15), 0.3);
                float3 camFixed = float3(dot(_CloudCam.xyz, _CloudFixedX.xyz), dot(_CloudCam.xyz, _CloudFixedY.xyz), dot(_CloudCam.xyz, _CloudFixedZ.xyz));
                float3 dirFixed = float3(dot(dir, _CloudFixedX.xyz), dot(dir, _CloudFixedY.xyz), dot(dir, _CloudFixedZ.xyz));
                float3 sunFixed = _CloudSunFixed.xyz;
                float3 sunTop = _CloudSunTint.rgb * _CloudSun.w;

                float3 light = ((float3)(0.0));
                float through = 1.0;
                float sumT = 0.0, sumW = 0.0;
                // (the storms' rain first: it hangs below the clouds, nearer than they are along a line of sight from below)
                int rains = storms();
                { [loop] for (int k = 0; k < rains; k++) rainWalk(stormA(k), stormB(k), dir, dirFixed, sunTop, sunDir, stop, jitter, light, through, sumT, sumW); }
                // (and a tornado's funnel, where there is one near)
                { [loop] for (int once = _CloudTwistA.w > 0.0 ? 0 : 1; once < 1; once++) funnelWalk(dir, sunTop, sunDir, stop, jitter, light, through, sumT, sumW); }
                float4 wa = ((float4)(0.0)), wb = ((float4)(0.0)), wc = ((float4)(0.0)), wmFrom = float4(1.0, 0.0, 0.0, 0.0), wmTo = float4(1.0, 0.0, 0.0, 0.0);
                float weatherAtT = -1e9, wmToT = -1e9;
                int empty = 0;
                int lit_ = 0;
                bool clear = true;       // whether the last step was in clear air
                float clearT = max(t - clamp(t / (1.0 + each) * each, least, longest), 0.0);   // the last place along the line known to be clear air (the grid's step before the first: outside the shell, clear)
                float knownT = -1.0, knownC = 0.0;   // a place already looked at (the step after a cloud's edge), and how much cloud is there
                float lastToward = -1.0;
                float lastFar = 0.0;     // (and of that, what lies further towards the sun than the walk's own steps that way look: another cloud's)
                float lastFaces = 2.0;   // which way a heap's surface faces at the last lit step, as the cosine of its angle to the sun (2: not known)
                bool lastPads = false;   // whether the last lit step was in the middle sky's pads (see deepIn)
                float sigmaScale = _CloudLook.x;
                // (how far off a cloud's edge is still searched for (see below): 40 km, and from above the clouds at any distance)
                float edgeTo = 40000.0 / max(1.0 - aboveness(), 1e-4);
                { [loop] for (int n = 0; n < 256; n++)
                {
                    if (n >= most || t > t1 || through < 0.01) break;
                    float3 p = _CloudCam.xyz + dir * t;
                    float h = length(p) - R;
                    // (the weather is read again every kilometre or so along the line: it changes over tens of them)
                    float3 q = _CloudNoise.xyz + dirFixed * (t / _CloudNoise.w);
                    // (What changes over kilometres is read there and 1200 m further on, and taken between the two at each step. Until 0.3.0
                    // it was held as read until the next reading: the cover and the bend of the pattern then jumped at each reading, by a
                    // hundred metres of bend and more, and the readings fall at nearly the same places for every pixel of a row of the
                    // picture: far heaps were cut into level bands, a row of pixels showing the heap and the next the sky beside it.)
                    // (In a loop that runs once or not at all, as the two ways of making heaps are in layerAt, and for the same reason:
                    // written as a plain "if", the nine readings were made at every step and thrown away at all but one in a dozen.)
                    { [loop] for (int read = t > wmToT || t < weatherAtT - 1.0 ? 0 : 1; read < 1; read++)
                    {
                        // (read on a grid of 1200 m laid along the line from the camera, not from where the walk began: a floor or top of
                        // the shell that moved moved every reading with it, and the clouds' shapes along the whole line changed)
                        float gridT = floor(t / 1200.0) * 1200.0;
                        float3 pg = _CloudCam.xyz + dir * gridT;
                        weatherAt(pg, wa, wb, wc);
                        { [loop] for (int once = _CloudForm.x > 0.5 ? 0 : 1; once < 1; once++) formWeather(pg, wa, wb, wc); }
                        wmFrom = wmTo;
                        { [loop] for (int first = wmToT > -1e8 && abs(gridT - wmToT) < 1.0 ? 1 : 0; first < 1; first++) wmFrom = broadAt(_CloudNoise.xyz + dirFixed * (gridT / _CloudNoise.w)); }
                        weatherAtT = gridT; wmToT = gridT + 1200.0;
                        wmTo = broadAt(_CloudNoise.xyz + dirFixed * (wmToT / _CloudNoise.w));
                    } }
                    float4 wm0 = lerp(wmFrom, wmTo, clamp((t - weatherAtT) / 1200.0, 0.0, 1.0));
                    float4 wm = float4(wm0.x * streetsAt(q, wm0), wm0.yzw);
                    // (which size of the noise to read: the large noise's full size out to 25 km, the small one's to about 5 km; coarser beyond,
                    // where heaps a few pixels across would otherwise sparkle)
                    float lod = min(log2(max(t, 1.0) / 25000.0), 4.0);
                    formChoice = 25000.0 * exp2(lod) * formPer;
                    // (And from above all the clouds, however far: the lumps are what a heap and a bank are seen by from orbit, a
                    // cauliflower and a lumpy top with its shade. Without them, past 40 km, a heap was a smooth egg (from orbit a field
                    // of white pearls) and a bank a flat white lid with a smooth edge (ice floes). Read as coarsely as the distance
                    // asks, as they are, they cost a reading or two more at each of the few steps a line from above takes.)
                    bool fine = t < 40000.0 / max(1.0 - aboveness(), 1e-4);
                    // (what the new shapes go by at this step, worked out once for all that is asked from it)
                    { [loop] for (int once = _CloudForm.x > 0.5 ? 0 : 1; once < 1; once++) formStep(q, wm, lerp(t, t - t0e, aboveness())); }
                    formWay = dirFixed;
                    formClimbs = dot(dir, p) / max(length(p), 1.0);
                    float c = abs(t - knownT) < 0.01 ? knownC : cloudAt(q, p, h, wa, wb, wc, wm, fine, lod);
                    float open = heapsOpen, insideHere = heapsInside, near = heapsNear, smoothHere = heapsSmooth;
                    float dt = stepLen;
                    // At the edge of a cloud (the first step into it after clear air): where between the last clear step and here it
                    // begins, found to a quarter of the way by halving twice, and a step taken from there to here (lit there) before
                    // this one. The edge is what is seen of a dense heap, which hides what is behind it within a few metres, and a step of
                    // tens of metres would put it anywhere within the step: a blurred heap, its sunlit side lit as if from deep inside.
                    // (Not walked in short steps instead: they spent the steps a line may take, and lines that ran out before the far side
                    // of the heaps showed as stripes.)
                    // (Out to 40 km; until 0.2.2 to 15, and from more than 2 km above the clouds to 15 until 2026-10-18, as a step there was
                    // taken to be less than a pixel. It is, looking straight down; at a slant a step of 150 to 300 m is several pixels, and
                    // past 15 km the tops of the banks were drawn as stacks of upright cards facing the camera, each where a step first
                    // found cloud: the "flat floes with hard edges" seen from 2-3 km above the heaps (the main chat, 2026-10-18). And not
                    // for a cloud of which eight tenths is already hidden by the cloud in front.)
                    // (And from above the clouds at any distance, since the evening of 2026-10-18. From orbit a step is half a kilometre, a
                    // 24th of the line's way through the shell, and a storm's tower takes up to a seventh of the light a metre: its first
                    // step in was lit from anywhere up to half a kilometre inside it, twenty and more e-folds from the sun, and a hurricane's
                    // outer towers were round grey pebbles about its white spiral (the main chat's picture from 200 km). Searched for, they
                    // are white where the sun is on them. A line from above crosses few clouds, and the search is two readings a cloud.)
                    if (c > 0.001 && clear && t > clearT + 1.0 && t < edgeTo && through > 0.2)
                    {
                        float lo = clearT, hi = t, chi = c;
                        { [loop] for (int b = 0; b < 2; b++)
                        {
                            float mid = 0.5 * (lo + hi);
                            float3 mp = _CloudCam.xyz + dir * mid;
                            float mc = cloudAt(_CloudNoise.xyz + dirFixed * (mid / _CloudNoise.w), mp, length(mp) - R, wa, wb, wc, wm, fine, lod);
                            if (mc > 0.001) { hi = mid; chi = mc; } else lo = mid;
                        } }
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
                        float3 up = p / max(length(p), 1.0);
                        float sunUp = dot(up, sunDir);
                        float hf = clamp((h - wa.z) / max(wa.w - wa.z, 50.0), 0.0, 1.0);
                        // ---- the sun: through the air, then through the cloud between here and the sun (six steps, each longer)
                        float3 sunHere = sunTop * sunThroughAir(h, sunUp);
                        float toward = lastToward, beyond = lastFar;
                        // (walked again on every lit step, since 2026-10-21; with four steps instead of six beyond 8 km, where a cloud is small in
                        // the picture. On every other one only, as until then, each step's light was the last one's half the time: the shaded
                        // sides of heaps came out too bright, in bands, and speckled where the two differed, a grain over every heap that the
                        // frames taken together did not take away (research/rendering/experiments/cloud-walk-noise.md: 18-32 % nearer the truth
                        // at the same steps; the walk 3-5 % dearer, as it stops a few steps into a dense heap).)
                        bool again = true;
                        lit_++;
                        stormLod = lod;
                        if (again && dot(sunHere, sunHere) > 1e-8)
                        {
                            toward = 0.0; beyond = 0.0;
                            float lt = 0.0, ls = 28.0;
                            // (the new shapes cost more to ask about than the old: five steps near, four past 4 km, three past 12)
                            int towardSteps = _CloudForm.x > 0.5 ? (t > 12000.0 ? 3 : t > 4000.0 ? 4 : 5) : (t > 8000.0 ? 4 : 6);
                            // (and three in a layer of pads, which is a few hundred metres thick: the sun's way out of it is in the first
                            // three, and the steps past them asked about clear air over it. Looks the same, a twentieth less to pay. Not
                            // in a sheet: a sheet of altostratus seen aslant was streaked with the bands of so few readings.)
                            bool padsHere = false;
                            lastPads = false;
                            { [loop] for (int L = 0; L < 2; L++)
                                if (formRaw[L].z > 0.5 && formKind[L].x < 0.02 && formKind[L].y < 0.02 && h > formMore[L].w - 50.0 && h < formMore[L].w + formKind[L].z + 50.0) { towardSteps = min(towardSteps, 3); padsHere = true; lastPads = lastPads || L == 1; } }
                            // (each step read at its own place in its stretch, a different one for every pixel and frame: read always at the
                            // middle, six readings of a heap with an edge to it can only come to so many sums, and its shade lay in flat
                            // bands with steps between them, like a drawing shaded with three pencils)
                            float place = frac(jitter * 5.371 + 0.618 * float(lit_));
                            formWay = sunFixed;
                            bool usedField = false;
                            // (The sun-side light field (go 9), where it is made and holds the place: read here, once, and used after the
                            // walk's own first steps (below).)
                            bool fieldOk = false;
                            float fieldIn = 0.0, fieldCell = 0.0;
                            float4 nf = ((float4)(1.0));
                            if (_CloudLightG.w > 0.5) nf = lightFieldAt(p, fieldOk, fieldIn, fieldCell);
                            // (How many of its own steps the walk takes before it. The field's cells are 75 m wide out to 4.8 km from its
                            // middle, 300 m to 19 km and 1.2 km beyond, and a heap's sunlit top and the folds in it are a few hundred metres
                            // across and less: what a cell says of the first 180 m towards the sun is a blend of the cells round the place,
                            // some of them in the heap and some in the clear air beside it, and a sunlit top 5 to 20 km off was shaded in grey
                            // patches a cell wide; what one of 1.2 km says of the first 740 m darkened the tops further off as a whole (the
                            // main chat's grey blotches, 2026-10-18). So the walk takes the first 180 m itself (its first three steps), past
                            // the cells of 300 m the first 740 m (five), and reads the field for what lies beyond: in the lab, from 4.5 km over
                            // heaps, light nearly half as far from the truth (1.2 against 2.2 / 255 off the light worked out in even steps)
                            // for a seventh more of the walk, and from the ground (0.8 against 1.2) for a quarter more; with the field unused,
                            // 1.1 and 1.3, for a fifth and a quarter more. Not in the far field, which is as coarse as the cells: its light
                            // came hardly nearer the truth for the steps (research/rendering/experiments/heaps-from-orbit.md).)
                            // (Not at all in the far field where its cells are 1.2 km: a dense top there was lit from the cells round it, some of
                            // them inside, as if from within, and the field's square showed from orbit as a straight line across a hurricane,
                            // the side within it greyer (the lab, the evening of 2026-10-18).
                            // There, the far looks below, as before the field.)
                            if (farGrow > 0.99 && fieldCell >= 500.0) fieldOk = false;
                            int fieldAfter = !fieldOk || farGrow > 0.99 ? 0 : fieldCell < 500.0 ? 2 : 4;
                            int stepsHere = max(towardSteps, fieldAfter + 1);
                            float3 walked = ((float3)(0.0));
                            { [loop] for (int k = 0; k < 6; k++)
                            {
                                if (k >= stepsHere) break;
                                // (the first at the middle of its stretch always: which way the surface faces is taken from it, and taken over a
                                // different distance each frame it came out different each frame: heaps like sandpaper)
                                float at = lt + ls * (k == 0 ? 0.5 : place);
                                float3 lp = p + sunDir * at;
                                float lh = length(lp) - R;
                                float3 lq = q + sunFixed * (at / _CloudNoise.w);
                                float got = cloudAt(lq, lp, lh, wa, wb, wc, wm, k < 2 && fine, k == 0 ? lod : lod + 0.5) * ls;
                                toward += got;
                                // (by the stretches the field is made in: steps 1 and 2 are its first, 28 to 182 m on, 3 its second, to 374 m,
                                // and 4 its third, to 739 m)
                                if (k == 1 || k == 2) walked.r += got; else if (k == 3) walked.g += got; else if (k == 4) walked.b += got;
                                // (Which way the heap's surface faces here, from its first reading towards the sun: the number of metres
                                // inside the surface falls by a metre for every metre straight out through it, and by less aslant. The
                                // cosine of the angle between the way out and the sun, near enough.)
                                if (k == 0)
                                {
                                    lastFaces = insideHere > -1e8 && heapsInside > -1e8 ? clamp((insideHere - heapsInside) / max(at, 4.0), -1.0, 1.0) : 2.0;
                                    // (For the new shapes, mostly by the heap without its lumps: light goes a hundred metres and more
                                    // through cloud before it has forgotten which way it came, so a lump of thirty metres is not lit as a
                                    // stone of that size would be, bright on one side and dark on the other; a heap is, and a tower.
                                    // Lit by the lumps' own facing, every heap was a model in plaster.)
                                    if (lastFaces < 1.5 && smoothHere > -1e8 && heapsSmooth > -1e8) lastFaces = lerp(clamp((smoothHere - heapsSmooth) / max(at, 4.0), -1.0, 1.0), lastFaces, 0.25);
                                }
                                lt += ls;
                                // (After the walk's own first steps (three or five, above; in the far field one), the sun-side light field: what
                                // its other steps that way and its far looks would find, worked out beforehand for cells round the camera. The
                                // first step is always the walk's own: which way the surface faces is taken from it.)
                                if (k == fieldAfter && fieldOk)
                                {
                                    nf = -log(max(nf, ((float4)(1e-7)))) / 0.1;
                                    // (the stretches the walk has just walked itself: its own steps in place of the field's cells, counted
                                    // as the field's are, below)
                                    if (fieldAfter >= 2) { nf.r = walked.r * sigmaScale; toward -= walked.r; }
                                    if (fieldAfter >= 4) { nf.g = walked.g * sigmaScale; nf.b = walked.b * sigmaScale; toward -= walked.g + walked.b; }
                                    // (What counts as near, the same at every distance. The walk's own steps count less of it the further
                                    // off a place is, to save steps; read so from the field, which costs the same whatever is counted, the
                                    // stretch from 346 to 711 m went at 4 km from counting for under half to counting in full: the shaded
                                    // side of every heap a step darker past 4 km, and a heap coming nearer lit afresh at that distance (the
                                    // lab, 2026-10-18). As the walk's own steps count it only towards the field's far edge, where they take
                                    // over; and in a layer of pads.)
                                    float walkNear = towardSteps >= 5 ? nf.g + nf.b : towardSteps == 4 ? nf.g : 0.0;
                                    float nearF = nf.r + lerp(walkNear, padsHere ? 0.0 : nf.g + nf.b, fieldIn);
                                    toward += nearF / sigmaScale;
                                    beyond = nf.r + nf.g + nf.b + nf.a - nearF;
                                    usedField = true;
                                    break;
                                }
                                // (deep in cloud already: what lies further on would dim what little diffuses through by a part of itself;
                                // taken as half as much again, and the walk left off)
                                // (The new shapes: only when far deeper. How much cloud lies within a few hundred metres towards the sun
                                // is what says whether a place is in a fold of a sunlit side or on the shaded side of the heap, and they
                                // are now lit very differently: see below.)
                                if (toward * sigmaScale > (_CloudForm.x > 0.5 ? 30.0 : 12.0)) { toward *= 1.5; break; }
                                ls *= 1.9;
                                place = frac(place + 0.618);
                            } }
                            // (And further on, for a tower or another cloud kilometres away between here and the sun: three looks, one
                            // somewhere in each of three stretches, each stretch twice the last (seven kilometres in all), at a different
                            // place in it for every pixel and frame. Until the evening of 2026-10-12 it was one look, always two
                            // kilometres on: what a tower's flank then took for the shadow of its neighbour was the shape of that
                            // neighbour where a line happened to cross it at that one distance: a hard-edged blue patch the shape of
                            // a lump, like a cave in the flank, and no shadow at all from a tower three kilometres off.)
                            // (Where the light field has been read, it knows all of this already. Where it has not (not yet made, or the
                            // place beyond its reach), as before the shadow's second map was made: three looks.)
                            float stretch = 1000.0;
                            { [loop] for (int k = usedField ? 3 : 0; k < 3; k++)
                            {
                                if (toward * sigmaScale + beyond > 12.0) break;
                                float far = lt + stretch * place;
                                float3 lp = p + sunDir * far;
                                beyond += cloudAt(q + sunFixed * (far / _CloudNoise.w), lp, length(lp) - R, wa, wb, wc, wm, false, lod + 1.0) * stretch * sigmaScale;
                                lt += stretch; stretch *= 2.0;
                                place = frac(place + 0.618);
                            } }
                            toward *= sigmaScale;
                            lastToward = toward; lastFar = beyond;
                            formWay = dirFixed;
                        }
                        else if (again) { toward = 0.0; lastToward = 0.0; beyond = 0.0; lastFar = 0.0; }
                        stormLod = 100.0;
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
                        float faces = lerp(1.0, 0.3 + 1.0 * clamp(lastFaces, 0.0, 1.0), skin);
                        // (The rework of 2026-10-12: all of it at two thirds of what it was. A surface facing the sun came to 1.4 and one
                        // turned sixty degrees from it to 1.0, and the picture's roll-off puts both within a twentieth of white: a heap's
                        // sunlit side, and a sheet seen from above, was one flat white however its lumps faced. Facing the sun now comes
                        // to 0.95, sixty degrees off to 0.65, edge-on to a third.)
                        float level = _CloudForm.x > 0.5 ? 0.66 : 1.0;
                        // (What has been scattered many times is as bright a hundred and fifty metres in from where the sun strikes as
                        // at the surface itself: the same reason. Counted from the surface, the crease between two lumps on a sunlit
                        // top was grey, and a field of heaps seen from above was dirty where a real one is one even white.)
                        // (And not by all the cloud towards the sun alike. What the heap's own folds put in the way within a few hundred
                        // metres counts for less than half, and its first hundred and fifty metres for nothing: a fold in a sunlit side
                        // is lit from the sunlit cloud all round it, and is a little less white than the lumps beside it, not grey.
                        // Counted in full, every fold in the game was a bruise. What another cloud puts in the way further off counts
                        // in full: there is no sunlit cloud beside a place in a tower's shadow.)
                        float all_ = toward + beyond;
                        // (The middle sky's pads from their first e-fold towards the sun: a cloudlet of altocumulus is all skin and no heap,
                        // and with the first ten given as nothing, as a heap's sunlit folds have them, every one was lit as a sunlit top
                        // throughout, its underside too: one white, where photographs have a light grey middle and brighter margins.)
                        float deepIn = _CloudForm.x > 0.5 ? (lastPads ? toward / 2.25 : max(toward - 10.0, 0.0) / 2.25) + beyond : all_;
                        float3 sunlit = sunHere * ((exp(-all_ * 0.1) * phOn + exp(-all_) * phBack + 0.35 * exp(-all_ * 0.25) * ph2) * (4.0 * PI * 0.45) + (0.30 * exp(-deepIn * 0.35) + 0.25 * exp(-deepIn * 0.12) + 0.35 / (1.0 + 0.11 * deepIn)) * faces) * powder * level;
                        // ---- the sky from above (less of it deep in the cloud and low down), the ground from below
                        // (the sky's blue on what the sun does not reach: most on the tops, less on the flat base, which sees the sky only
                        // at its horizon; the ground's light on the base)
                        // (how much of the sky this place sees: for a heap made beforehand, as worked out when it was made, from its own
                        // shape and its neighbours': open on its top and its shoulders, less in the creases between its turrets and on
                        // its sides, none under its middle; for the rest, by how deep in the cloud and how high in the layer)
                        float seesSky = open >= 0.0 ? lerp(0.1, 1.3, open * open) : (1.0 - 0.45 * c) * lerp(0.2, 1.15, smoothstep(0.0, 0.4, hf));
                        float3 sky = _CloudSky.rgb * seesSky * clamp(sunUp * 3.0 + 0.35, 0.0, 1.0) * (0.35 + 0.65 * dot(sunHere / max(sunTop, ((float3)(1e-4))), float3(0.2126, 0.7152, 0.0722)));
                        float3 below = _CloudGround.rgb * (1.0 - hf) * 0.8;
                        // (the night's own light, from the sky: more on what sees more of it, so that a heap at night has a top and an underside)
                        float3 here = sunlit + sky + below + ((float3)(_CloudSunTint.w)) * (open >= 0.0 ? lerp(0.35, 1.3, open) : 1.0);
                        // ---- lightning inside the cloud (its light spreads through the cloud by being scattered over and over, so it falls
                        // off with distance more than it is dimmed by the cloud on the way)
                        if (_CloudFlashA.w > 0.0)
                        {
                            float3 f = p - (_CloudCam.xyz + _CloudFlashA.xyz);
                            float d2 = dot(f, f);
                            float spread = _CloudFlashTint.w;
                            here += _CloudFlashTint.rgb * _CloudFlashA.w / (1.0 + d2 / (spread * spread));
                        }
                        if (_CloudFlashB.w > 0.0)
                        {
                            float3 f = p - (_CloudCam.xyz + _CloudFlashB.xyz);
                            float d2 = dot(f, f);
                            float spread = _CloudFlashTint.w;
                            here += _CloudFlashTint.rgb * _CloudFlashB.w / (1.0 + d2 / (spread * spread));
                        }
                        // ---- what this step adds, and how much of what lies beyond it still shows (taken exactly over the step)
                        float pass_ = exp(-sigma * dt);
                        light += through * here * (1.0 - pass_);
                        sumT += t * through * (1.0 - pass_);
                        sumW += through * (1.0 - pass_);
                        through *= pass_;
                        empty = 0;
                    }
                    else
                    {
                        // (after a few steps of clear air, the long steps)
                        empty++;
                        lastToward = -1.0;
                        // (Not with the new shapes. Their seeker can say "no cloud" only for strides shorter than a part of the
                        // lattice's cell, which is near the camera; further off it says "perhaps" every time, and the walk then
                        // asked twice at every step of clear air, the seeker and then the step itself: by count, half of all that
                        // was asked in a fair sky and six tenths under a storm. They tell the walk how far it may stride instead.)
                        // (Clear air, and no cloud within so many metres, as the field and the storms keep their shapes: on by that
                        // much at once, but no more than a dozen steps' worth or 600 m, the cover and the storms being read afresh as
                        // the walk goes. Without this a line of sight that began inside the layer of air the clouds are in, among heaps
                        // or under a storm's anvil, spent its eighty-eight steps of forty metres on clear air and stopped three and a
                        // half kilometres out: sky, where the anvil should have been.)
                        // (And each stride by its own share of that, a half to the whole, a different one for every pixel and frame: striding the
                        // whole way each time, every line of sight came up to a heap at the same places whatever its start, the walk's steps
                        // no longer fell at different places from frame to frame, and the heaps were shaded in level bands.)
                        // (The new shapes, far off: three steps' worth, as the old shapes' seeker strode. Past 20 km that is more than
                        // the 600 m. And a fifth of the way come: a line of sight a little above level under a deck creeps towards it,
                        // its gap closing by a fiftieth of each stride, and at 600 m a stride it spent its 88 steps before it reached a
                        // deck 40 km off: clear sky between the deck's last and the horizon, the owner's "strip of light blue in the
                        // distance" (2026-10-15). The distance it is told is along the line, and still holds the stride back near cloud.)
                        float reach = min(12.0 * stepLen, 600.0);
                        if (_CloudForm.x > 0.5) reach = max(reach, max(3.0 * stepLen, 0.2 * t));
                        if (near < 0.0 && near > -1e8) dt = max(dt, min(-near * (0.5 + 0.5 * frac(jitter * 7.13 + 0.618 * float(n))), reach));
                        // (and on to the grid's next step from there: a stride decides which of the grid's steps are passed over, not where
                        // the steps fall, so that a walk begun a few steps sooner or later meets each cloud at the same places)
                        if (dt > stepLen * 1.01) dt = firstStep(t + dt, jitter, least, longest, each) - t;
                    }
                    t += dt;
                    stepLen = clamp(t * each, least, longest);
                } }

                // ---------------------------------------------------------------- the high cloud: a thin sheet, drawn where the line crosses it
                if (_CloudShell.w > 0.0)
                {
                    float hc = _CloudShell.z;
                    float2 cross0 = shellFromCamera(mu, hc);
                    float tc = alt < hc ? cross0.y : cross0.x;
                    if (tc > _CloudStage.z && tc < stop && (ground.x < 0.0 || tc < ground.x) && (_CloudStage.w <= 0.0 || tc < _CloudStage.w))
                    {
                        float3 p = _CloudCam.xyz + dir * tc;
                        float4 ca, cb, cc;
                        weatherAt(p, ca, cb, cc);
                        float3 q = _CloudNoise.xyz + dirFixed * (tc / _CloudNoise.w);
                        // (long streaks drawn out along the wind)
                        // (the noise read nearly seven times more slowly along the wind than across it: long fibres along the wind)
                        float3 streak = (q - _CloudAlong.xyz * dot(q, _CloudAlong.xyz) * 0.85) * 0.5;
                        float4 s = SampleLod(_CloudShape, streak, 1.0);
                        float3 d = SampleLod(_CloudDetail, q * 3.0 + float3(0.0, 0.0, _CloudDrift.w * 0.3), 1.0).rgb;
                        float amount = clamp(cb.g * _CloudShell.w, 0.0, 1.0);
                        float wisp = clamp(remap(s.g * 0.6 + s.b * 0.4, 1.0 - amount * 0.8, 1.0, 0.0, 1.0), 0.0, 1.0);
                        wisp = clamp(wisp - (1.0 - d.g) * 0.35, 0.0, 1.0);
                        float slant = abs(dot(normalize(p), dir));
                        // (The veil, cirrostratus: where the high cloud covers most of the sky there is, under the wisps, a thin even
                        // sheet of ice from horizon to horizon, through which the sun still casts shadows. It is what comes a day
                        // ahead of rain, and it is where the sun's ring is seen.)
                        float veil = smoothstep(0.5, 0.9, amount) * (0.55 + 0.45 * s.r);
                        float ice = (wisp * _CloudGround.w * 0.004 + veil * 0.22) / max(slant, 0.08);
                        float alpha = 1.0 - exp(-ice);
                        if (alpha > 0.002)
                        {
                            float3 up = normalize(p);
                            float3 sunHere = sunTop * sunThroughAir(hc, dot(up, sunDir));
                            // (The light the ice throws on towards the eye, at a third of what it was until 2026-10-13. As it was, a veil
                            // 22 degrees from the sun came out twice as bright as a white wall in full sun, which thin ice is not (seven
                            // tenths of one, by the sum): half the sky round the sun was one flat glare at the top of what the picture
                            // can show, and nothing drawn in it could be seen.)
                            float3 lit_ = sunHere * (lerp(phase(cosView, 0.65), phase(cosView, -0.2), 0.25) * 4.0 * PI * 0.33 + 0.25) + _CloudSky.rgb * 0.6;
                            // ---- the sun's ring and the mock suns. Ice in high cloud is six-sided crystals, and light that goes in at
                            // one side face and out at the next but one is turned by no less than 21.8 degrees (red a little less, blue
                            // a little more) and mostly by little more than that: so the sky 22 degrees from the sun, all the way
                            // round it, is brighter than the sky inside that, with a sharp reddish inner edge and a soft outer one.
                            // Crystals that are flat plates fall level, and turn the light sideways only: two bright patches level
                            // with the sun, 22 degrees to either side of it when it is on the horizon and further out the higher it
                            // stands (none once it is above 61 degrees), red towards the sun, with a white tail away from it. Both
                            // are light turned once: they show through thin ice and are lost in thick.
                            { [loop] for (int once = cosView > 0.5 && cosView < 0.96 ? 0 : 1; once < 1; once++)
                            {
                                float away = degrees(acos(cosView));
                                float3 past = ((float3)(away)) - float3(21.5, 21.85, 22.3);
                                float3 ring = smoothstep(((float3)(-0.4)), ((float3)(0.2)), past) * exp(-max(past, ((float3)(0.0))) / 2.4);
                                float sinS = dot(sunDir, up), sinV = dot(dir, up);
                                float turned = (1.7161 - sinS * sinS) / max(1.0 - sinS * sinS, 1e-4);
                                float3 mock = ((float3)(0.0));
                                { [loop] for (int low = turned < 3.9 && sinS > -0.05 ? 0 : 1; low < 1; low++)
                                {
                                    float aside = 2.0 * asin(0.5 * sqrt(turned)) - 1.0472;
                                    float3 levelV = normalize(dir - up * sinV), levelS = normalize(sunDir - up * sinS);
                                    float along = degrees(acos(clamp(dot(levelV, levelS), -1.0, 1.0)) - aside) * sqrt(max(1.0 - sinS * sinS, 0.0));
                                    float above = degrees(asin(clamp(sinV, -1.0, 1.0)) - asin(clamp(sinS, -1.0, 1.0)));
                                    float3 pastMock = ((float3)(along)) - float3(-0.35, 0.0, 0.45);
                                    // (plates are not everywhere in the cloud: the mock suns come and go as it drifts by)
                                    mock = smoothstep(((float3)(-0.5)), ((float3)(0.3)), pastMock) * exp(-max(pastMock, ((float3)(0.0))) / 5.0) * exp(-above * above / 7.0) * 7.0 * smoothstep(0.3, 0.6, s.r);
                                } }
                                // ---- the arcs over the ring and under it (2026-10-17). Columns, crystals longer than they are wide,
                                // fall with their long axes level, every way round. Light turned through their side faces goes round
                                // the long axis: what it has along the axis it keeps, and what it turns, it turns by no less than the
                                // least that crystal allows a ray so far aslant to its faces (Bravais: such a ray goes as if the ice's
                                // index were sqrt(n^2 - sin^2 e) / cos e). For a place in the sky there is one way round a level column
                                // can send the sun's light there: its axis square to the way from the sun to the place. Where the turn
                                // that asks is the least that column allows, the light gathers: the upper and lower tangent arcs,
                                // touching the ring above and below the sun, spread into wings when the sun is low, and drawn in round
                                // the ring as the circumscribed halo when it is high. Red on the side towards the sun, as the ring.
                                // (Columns are not everywhere alike either: their own share of the cloud, not the plates', but never none
                                // in a veil: gone where the share was low, one wing of an arc was missing.)
                                float3 axis = cross(up, dir - sunDir);
                                float axisL = length(axis);
                                float3 arcs = ((float3)(0.0));
                                { [loop] for (int columns = axisL > 1e-4 && sinS > -0.05 ? 0 : 1; columns < 1; columns++)
                                {
                                    axis /= axisL;
                                    float sinE = dot(sunDir, axis);
                                    float3 sp = sunDir - axis * sinE, vp = dir - axis * sinE;
                                    float turnedBy = degrees(acos(clamp(dot(sp, vp) / max(length(sp) * length(vp), 1e-6), -1.0, 1.0)));
                                    float3 n = float3(1.3062, 1.3104, 1.3165);
                                    float3 half_ = sqrt(max(n * n - sinE * sinE, ((float3)(1.0)))) / sqrt(max(1.0 - sinE * sinE, 1e-4)) * 0.5;
                                    float3 pastArc = ((float3)(turnedBy)) - (degrees(2.0 * asin(min(half_, ((float3)(1.0))))) - 60.0);
                                    arcs = smoothstep(((float3)(-0.4)), ((float3)(0.2)), pastArc) * exp(-max(pastArc, ((float3)(0.0))) / 1.8) * step(half_, ((float3)(1.0))) * 4.0 * lerp(0.35, 1.0, smoothstep(0.35, 0.65, s.g));
                                } }
                                lit_ += sunHere * (ring * 3.0 + mock + arcs) * exp(-1.5 * ice);
                            } }
                            // ---- the moon by night (2026-10-17): a veil of ice lit by the Mun, forward most, and the same ring round it
                            // as round the sun, in its light. (Until then the veil by night was lit by the sky alone, which is black.)
                            { [loop] for (int moon = _CloudMoon.w > 1e-6 ? 0 : 1; moon < 1; moon++)
                            {
                                float cosMoon = dot(dir, _CloudMoon.xyz);
                                float3 moonHere = float3(0.92, 0.95, 1.0) * _CloudMoon.w;
                                lit_ += moonHere * (lerp(phase(cosMoon, 0.65), phase(cosMoon, -0.2), 0.25) * 4.0 * PI * 0.33 + 0.25);
                                // (one ring for all colours: by moonlight the eye sees no colour, and a halo round the moon is white)
                                float pastM = degrees(acos(clamp(cosMoon, -1.0, 1.0))) - 21.85;
                                lit_ += moonHere * (smoothstep(-0.5, 0.3, pastM) * exp(-max(pastM, 0.0) / 2.4) * 3.0 * exp(-1.5 * ice));
                            } }
                            // ---- the sun's pillar (2026-10-17): plates fall nearly level, rocking by a few degrees, and under a low sun
                            // the light thrown back off their faces stands in a column over it and under it, about as wide as the sun
                            // and some degrees tall, fading away from it. Thrown back once: through thin ice, as the mock suns.
                            // (Measured from the eye's own level, not the cloud's there: near the horizon the veil the line meets is a
                            // hundred kilometres off and more, where on a world as small as Kerbin the ground is turned ten degrees from
                            // the eye's, and the sun stands that much higher.)
                            float3 upP = normalize(_CloudCam.xyz);
                            { [loop] for (int pillar = cosView > 0.97 && dot(sunDir, upP) < 0.14 && dot(sunDir, upP) > -0.06 ? 0 : 1; pillar < 1; pillar++)
                            {
                                float sinS = dot(sunDir, upP), sinV = dot(dir, upP);
                                float3 levelV = normalize(dir - upP * sinV), levelS = normalize(sunDir - upP * sinS);
                                float aside = degrees(acos(clamp(dot(levelV, levelS), -1.0, 1.0))) * sqrt(max(1.0 - sinV * sinV, 0.0));
                                float over = degrees(asin(clamp(sinV, -1.0, 1.0)) - asin(clamp(sinS, -1.0, 1.0)));
                                float column = exp(-aside * aside / 0.18) * exp(-abs(over) / 3.5) * smoothstep(0.0, 0.7, abs(over));
                                // (as bright at its foot as a third of the veil's own glow there: ice throws light back well at so low an
                                // angle, and at a fifteenth of that, as at first, it was lost in the glow round the sun)
                                lit_ += sunHere * (column * 30.0 * (1.0 - smoothstep(0.06, 0.14, sinS)) * lerp(0.35, 1.0, smoothstep(0.3, 0.6, s.r)) * exp(-0.35 * ice));
                            } }
                            if (alt < hc)
                            {
                                // behind the low clouds
                                light += through * lit_ * alpha;
                                sumT += tc * through * alpha; sumW += through * alpha;
                                through *= 1.0 - alpha;
                            }
                            else
                            {
                                // in front of them
                                light = lit_ * alpha + light * (1.0 - alpha);
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
                    float3 tau = ((float3)(0.0));
                    float3 gained = ((float3)(0.0));
                    float seg = meanT / 6.0;
                    float pr = 3.0 / (16.0 * PI) * (1.0 + cosView * cosView);
                    float pm = phase(cosView, 0.75);
                    { [loop] for (int k = 0; k < 6; k++)
                    {
                        float3 p = _CloudCam.xyz + dir * (seg * (float(k) + 0.5));
                        float h = max(length(p) - R, 0.0);
                        float2 dens = exp(-h / max(_CloudAirHigh.xy, ((float2)(100.0))));
                        float3 ext = _CloudAir.rgb * dens.x + ((float3)(_CloudAir.w * dens.y));
                        float3 sunHere = sunTop * sunThroughAir(h, dot(p / max(length(p), 1.0), sunDir));
                        gained += exp(-tau - ext * seg * 0.5) * (_CloudAir.rgb * (pr * dens.x) + ((float3)(_CloudAir.w * pm * dens.y))) * sunHere * seg;
                        tau += ext * seg;
                    } }
                    float3 airThrough = exp(-tau * _CloudHaze.w);
                    // (and the air lit by the whole sky, not by the sun alone: light scattered again and again, which is why distance goes
                    // pale blue-white and not the yellow that the sun's light alone, robbed of its blue on the way, would give)
                    float camSun = clamp(dot(normalize(_CloudCam.xyz), sunDir) * 3.0 + 0.3, 0.0, 1.0);
                    float3 skyHaze = _CloudSky.rgb * 2.2 * camSun;
                    light = light * airThrough + (gained * _CloudHaze.z + skyHaze * (1.0 - airThrough)) * _CloudHaze.w * (1.0 - through);
                }
                // ---------------------------------------------------------------- the clouds' shadows on the ground, seen from above them
                // From above all the clouds, where this line comes down to the ground: how much of the sun the clouds over that place
                // let through on its way down, along the sun's line through the shell there, in four steps. From orbit a heap and its
                // shadow beside it are what makes a field of them look like heaps and not like paint (photographs from the station of
                // fair-weather heaps over land: each a bright dot with its dark one beside it). The shadow's square round the camera
                // cannot give that from high up (a point of it is as wide as a heap), and is left off there (Clouds.cs). Only the
                // sun's share of the ground's light is taken, and that less the more air the line comes through: the air's own glow
                // over the ground is not in the shadow.
                float groundShade = 1.0;
                // (and how much of the land's light that takes away, handed to the shadows' pass, which tints it: grey, one number for the
                // three colours laid on with the alpha, the shade from above was the sunlit ground's own colour darkened, as it was below the
                // clouds until 2026-10-19. Nothing on the sea, whose shadow is worked out as light.)
                float landDark = 0.0;
                // (The ground as drawn where there is some: land stands above the sea's level, so the line meets it before the ball of
                // that level; where nothing is drawn (the world far off, drawn small by the game), that ball.)
                // (And on a world with a sea, the sea's surface where the line meets it first. What is drawn is known from the camera's
                // picture of depths, which is made of the solid things alone: Natural Ocean's sea and Scatterer's are drawn after it, and
                // over the sea it holds the seabed. The shadow was worked out on the bottom, and from 13 km over water half a kilometre
                // deep, looking 45 degrees down with the sun 40 degrees up, the heaps it was the shadow of stood a kilometre off.)
                float tGround = stop < 1e8 ? stop : ground.x;
                bool onSea = _CloudAirHigh.z > 0.5 && ground.x > 0.0 && ground.x <= tGround;
                if (onSea) tGround = ground.x;
                { [loop] for (int once = aboveness() > 0.0 && tGround > 0.0 && tGround < 1e8 && through > 0.1 ? 0 : 1; once < 1; once++)
                {
                    float tG = tGround;
                    float3 G = _CloudCam.xyz + dir * tG;
                    float3 upG = G / max(length(G), 1.0);
                    float sunUpG = dot(upG, sunDir);
                    if (sunUpG < 0.04) break;
                    float hG = length(G) - R;
                    float3 qG = _CloudNoise.xyz + dirFixed * (tG / _CloudNoise.w);
                    float4 swa, swb, swc;
                    weatherAt(G, swa, swb, swc);
                    float4 swm0 = broadAt(qG);
                    float4 swm = float4(swm0.x * streetsAt(qG, swm0), swm0.yzw);
                    { [loop] for (int form = _CloudForm.x > 0.5 ? 0 : 1; form < 1; form++) { formWeather(G, swa, swb, swc); formStep(qG, swm, 0.0); } }
                    // (the four steps through the low layer, where the heaps are, and a little over it: spread through the whole shell,
                    // up to the high cloud's height, one or two of them fell among the heaps)
                    // (and from a little under its base: from the shell's floor, the four steps were some hundreds of metres apart, and came to a
                    // heap 450 m thick once or not at all, a different way for each pixel in turn: shadows with dotted edges (the main chat,
                    // 2026-10-18))
                    float lowTop = _CloudForm.x > 0.5 && formRaw[0].z > 0.5 ? formMore[0].w + formMore[0].z + 300.0 : _CloudShell.y;
                    float lowBase = _CloudForm.x > 0.5 && formRaw[0].z > 0.5 ? max(formMore[0].w - 50.0, _CloudShell.x) : _CloudShell.x;
                    float fromS = max((lowBase - hG) / sunUpG, 0.0), toS = max((min(_CloudShell.y, lowTop) - hG) / sunUpG, fromS + 1.0);
                    // (Twelve readings, with the heaps' lumps, where a shadow's outline can be seen (within 20 km of the camera), fewer and
                    // without the lumps further off, down to four by 60 km. Four readings some 700 m apart through the layer, without the
                    // lumps, smeared a heap's outline: each shadow a smooth dark oval with a grainy rim, where a real one has the heap's
                    // lumpy outline and a crisp edge, and at a low sun the long shadows faint and smeared. Twelve with the lumps come within
                    // a sixth of a level of 255 of the shadow read 64 times (from 8 km), where four were a level off (research/rendering/
                    // experiments/cloud-shadows-from-above-shape.md). From orbit the outlines are less than a pixel, and twelve readings
                    // over the whole disc cost a fifth more of the walk for nothing.)
                    float nearShade = 1.0 - smoothstep(20000.0, 60000.0, tG);
                    int readings = int(lerp(4.0, 12.0, nearShade) + 0.5);
                    bool lumps = nearShade > 0.5;
                    float dS = (toS - fromS) / float(readings);
                    float lodG = min(log2(max(tG, 1.0) / 25000.0), 4.0);
                    formChoice = 25000.0 * exp2(lodG) * formPer;
                    float depthS = 0.0;
                    { [loop] for (int k = 0; k < 12; k++)
                    {
                        if (k >= readings) break;
                        float sk = fromS + (float(k) + jitter) * dS;
                        float3 pk = G + sunDir * sk;
                        float3 qk = qG + sunFixed * (sk / _CloudNoise.w);
                        depthS += cloudAt(qk, pk, length(pk) - R, swa, swb, swc, swm, lumps, lodG) * sigmaScale * dS;
                    } }
                    // (light scattered into the shadow from the cloud's sides softens it: an eighth of the cloud's own thickness, as the
                    // shadow's square has it)
                    float sunThrough = exp(-depthS * 0.125);
                    float k = 1.25 * clamp(sunUpG * 2.5 + 0.15, 0.0, 1.0) * sunUpG;
                    float share = k / (k + 1.0);
                    float airT = exp(-(dot(_CloudAir.rgb, ((float3)(0.3333))) * max(_CloudAirHigh.x, 100.0) + _CloudAir.w * max(_CloudAirHigh.y, 100.0)) / max(abs(dot(dir, upG)), 0.08));
                    groundShade = 1.0 - airT * share * (1.0 - sunThrough);
                    // (On the sea, the share of what is seen that is the sun's (seaSunShare), taken away as light and given as the
                    // picture's numbers, which is what this shade multiplies: the game draws the land as numbers, lit by numbers, and
                    // its share is in those, but Natural Ocean works its sea out as light. Shaded as land is, every heap's shadow on the
                    // open sea was nearly black: from 13 km a third of the sea's light beside it (the main chat's climb, 2026-10-18),
                    // where photographs from an airliner at noon over trade cumulus have it nine to seven tenths on the deep blue, and a
                    // fifth in the sun's glitter, where all of the sea's light is the sun's.)
                    if (onSea) groundShade = pow(max(1.0 - seaSunShare(dir, upG, sunDir, hG, sunTop * sunThroughAir(hG, sunUpG)) * (1.0 - sunThrough), 0.0), 1.0 / 2.2);
                    groundShade = lerp(1.0, groundShade, aboveness());
                    if (!onSea) landDark = 1.0 - groundShade;
                    // (and where no cloud is on the line itself, the shadow is as far off as the ground, for what is in front of it)
                    if (sumW <= 1e-5) { sumT = tG * 1e-4; sumW = 1e-4; meanT = tG; }
                } }
                float4 result_ = float4(light, through * groundShade);
                if ((asint(result_.r + result_.g + result_.b + result_.a) & 0x7F800000) == 0x7F800000) result_ = float4(0.0, 0.0, 0.0, 1.0);
                result = result_;
                // (how far off, in kilometres: half floats end at 65504)
                // (and, g, how much the shadow from above takes from the land's light here: cloudshade.glsl)
                result1 = float4(meanT / 1000.0, landDark, 0.0, 1.0);
                return Both(result, result1);
            }
            ENDCG
        }
    }
}
