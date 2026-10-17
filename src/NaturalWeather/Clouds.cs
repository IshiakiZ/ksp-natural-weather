using System;
using System.Collections.Generic;
using System.IO;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// Clouds of the mod's own, drawn as real volumes.
    ///
    /// A cloud is water drops by the billion: sunlight goes into it and comes out again after bouncing many times, so it is white
    /// where the sun is on it, grey where it is deep, darker underneath, and glows at its thin edges with the sun behind it. Here
    /// each pixel's line of sight is walked through a shell of air round the planet (tools/shaderpack/clouds.glsl): where there
    /// is cloud comes from the weather (Keystone.Weather, as a map of the whole planet: how much, what kind, how high, CloudMap;
    /// and the storms near the camera, drawn as towers where the weather has them) carved by noise into heaps, sheets and towers;
    /// the light at each step is the sun's as it comes through the air (CloudAir) and through the cloud between that step and the
    /// sun, with light scattered many times over, the sky's and the ground's, and lightning from inside. This is how the clouds
    /// of Horizon Zero Dawn and Forbidden West, Frostbite and Unreal are drawn (wiki/ksp-weather/Clouds.md).
    ///
    /// The game draws the far scene (from 400 m out) with one camera and the near scene with another. The clouds are walked for
    /// each camera's own stretch, after what it draws itself, so that the craft stands in front of a cloud behind it and is hidden
    /// by one in front: the far camera always, the near one only when a cloud can be within its 400 m. Each walk is drawn at half
    /// the picture's size, its steps begun at a different place each frame, and the frames taken together (where each cloud was in
    /// the last frame is worked out from how far off it is); then laid on the picture.
    ///
    /// Their shadow: worked out for a square of ground round the camera (how much cloud lies towards the sun from each point,
    /// and between what heights). The sun itself is dimmed by as much as the cloud above the craft takes (so the craft, and all
    /// round it, is in the cloud's shade as a whole: its own shadows fade); then every pixel both cameras have drawn is put right
    /// for the cloud above its own place: darker in a shadow the craft is not in, lighter in a gap of sunlight when the craft is
    /// in shade. (So the weather's own figure for how the clouds dim the sun, Keystone.Weather.Shade, must not dim it again while
    /// these clouds are drawn: see <see cref="Drawing"/>.)
    ///
    /// The shaders are the mod's own, packed for the game's OpenGL (Macs and Linux; tools/shaderpack/make_bundle.py). On Direct3D
    /// (Windows) there are none yet unless a build of them is found in the mod's folder, and then no clouds are drawn: the page
    /// says so, and EVE's clouds are left as they are.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Clouds : MonoBehaviour
    {
        /// <summary>Whether these clouds are being drawn just now (and so dimming the sun themselves, by <see cref="SunThrough"/>).</summary>
        public static bool Drawing;
        /// <summary>How much of the sun's direct light comes through these clouds to the craft, 0 to 1 (1 with none in the way).</summary>
        public static float SunThrough = 1f;
        /// <summary>How far from the camera the nearest cloud or rain can be just now, m (the clouds are laid on the picture no nearer: see Setup).</summary>
        public static float SheetClear;
        /// <summary>Whether the clouds are laid on before the see-through things just now (see LateUpdate, "first").</summary>
        public static bool LaidFirst;
        static bool? scatterer;
        static bool ScattererThere()
        {
            if (scatterer == null)
            {
                scatterer = false;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                    if (a != null && a.name != null && a.name.ToLowerInvariant() == "scatterer") scatterer = true;
            }
            return scatterer.Value;
        }
        public static string Report = "";
        /// <summary>Whether the game draws the scene with two cameras (OpenGL: the far one from 400 m), as these clouds found it last.</summary>
        public static bool Split;
        /// <summary>Called as the clouds finish laying themselves on a camera's picture, with that camera's drawing of them: for what
        /// must be drawn after them, in front of the cloud behind it (lightning's channel seen from under its cloud).</summary>
        public static event Action<CommandBuffer, Camera> InFront;

        static Shader walkShader, overShader, shadeShader;
        static bool tried;
        static string why = "";
        static RenderTexture shape, detail;     // the noise, made once for the whole game
        static bool noiseHasMips;
        // The heaps made beforehand (tools/clouds/heapfield.swift, PluginData/heaps.field): a square of sky that repeats, read by the
        // shader in place of heaps carved from the noise. None (the file missing, or the setting off): the noise's heaps, as before 0.3.0.
        static Texture3D field, noField;
        static bool fieldTried;
        static float fieldSide = 6400f, fieldFar = 240f;
        static readonly float[] fieldLevels = new float[16];
        static string fieldWhy = "";
        /// <summary>(Development build: how many metres the small lumps stand proud of the creases between them.)</summary>
        public static float FieldLumps = 80f;
        /// <summary>How the heaps come, held for a test: 0 a sky of many small puffs, 1 the usual sky; below nought: as the day has it.</summary>
        /// <summary>How far a heap that reaches the layer's top spreads out under it, m; and how far the heaps' outlines are pushed about, m (the usual day's).</summary>
        public static float LidSpread = 225f, Pushed = 300f;
        /// <summary>How many times larger over the ground a shallow layer's heaps are read on the usual day (broad, flat-topped).</summary>
        public static float Broader = 2.3f;
        public static float HeldHabit = -1f;
        float habit = float.NaN;
#if DEV
        /// <summary>(Development build: "puffs", "usual" or anything else to let the day decide.)</summary>
        public static string Habit(string which) { HeldHabit = which == "puffs" ? 0f : which == "usual" ? 1f : -1f; if (instance != null) instance.habit = float.NaN; return "the heaps: " + (HeldHabit < 0f ? "as the day has them" : HeldHabit < 0.5f ? "many small puffs" : "the usual sky"); }
#endif
        /// <summary>
        /// How the low and the middle clouds are shaped. 1: as domes over rising air with lumps on them, worked out in metres
        /// (the rework of 2026-10-12, see the shader: formAt); 0: as before it (heaps made beforehand, sheets carved from noise).
        /// </summary>
        public static float Form = 1f;
        /// <summary>How long a heap lives, from a puff at the base to gone, in seconds.</summary>
        public static float HeapLife = 1500f;
        /// <summary>
        /// The map of the low layer's heaps, drawn once a frame over the ground round the camera and read by every asking of
        /// the walk (the shader's go 5): how many dots each way (under 64: no map, and the heaps are worked out at every
        /// asking, which costs a quarter of the frames among them), and how many metres it is across. It comes round, so it
        /// must be wider than twice as far as heaps are drawn (26 km).
        /// </summary>
        public static int HeapsDots = 1024;
        public static float HeapsAcross = 51200f;
        /// <summary>
        /// Whether what is kept of the clouds from frame to frame is as fine as the screen where the walk is done at half
        /// its size (each frame's lines of sight then go through a different one of the four pixels in turn).
        /// </summary>
        public static bool Finer = true;
        /// <summary>
        /// A table of numbers got by chance, 256 by 256, four to a place: for each point of the heaps' lattice, when its heaps
        /// come, how they rank in size, and how far out of place they stand. Made the same way everywhere (and in the lab,
        /// cloudlab.swift), so that a sky is the same sky on every machine.
        /// </summary>
        static Texture2D cellsTable;
        static uint MixBits(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
        static void MakeCellsTable()
        {
            var px = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    uint h = unchecked(MixBits((uint)x + MixBits((uint)y + 977u)));
                    px[x + y * 256] = new Color32((byte)(h & 255u), (byte)((h >> 8) & 255u), (byte)((h >> 16) & 255u), (byte)(h >> 24));
                }
            cellsTable = new Texture2D(256, 256, TextureFormat.RGBA32, false, true) { name = "Natural Weather: the heaps' lattice", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            cellsTable.SetPixels32(px);
            cellsTable.Apply(false, true);
        }
#if DEV
        /// <summary>(Development build: "old" for the clouds' shapes as before the rework of 2026-10-12, anything else for the new.)</summary>
        /// <summary>(Development build: a number the shader's trials go by, for finding a fault; 0 for none.)</summary>
        public static float Trial;
        /// <summary>(Development build: "off" draws the clouds as they were drawn before the far field of 2026-10-14, for pictures of before and after; "on" as now.)</summary>
        public static bool FarOff;
        public static string Far(string which) { FarOff = which != null && which.Trim() == "off"; return FarOff ? "the clouds' far field off (as before 2026-10-14)" : "the clouds' far field on"; }
        /// <summary>(Development build: "off" draws the clouds with no storms in them; "on" as the weather has it.)</summary>
        public static bool NoStorms;
        public static int StormsSent = 6;
        public static string StormsSaid = "";
        public static string Storms(string which)
        {
            string w = (which ?? "").Trim();
            NoStorms = w == "off";
            StormsSent = int.TryParse(w, out int k) ? Math.Max(0, Math.Min(6, k)) : 6;
            return (NoStorms ? "no storms drawn" : "storms drawn: " + StormsSent) + " | " + StormsSaid;
        }
        public static string Try2(string which) { Trial = float.Parse(which, System.Globalization.CultureInfo.InvariantCulture); return "trial " + Trial; }
#endif
        /// <summary>(Development build, for finding a fault: 1 hands the shader an empty field while telling it there is one.)</summary>
        public static float FieldTest;
        public static string Test(string arg)
        {
            FieldTest = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            // (2: the field read from its file again)
            if (FieldTest >= 2f) { if (field != null) Destroy(field); field = null; fieldTried = false; }
            return "field test " + FieldTest;
        }
        Vector2d fieldDrift;            // how far the wind has carried the field of heaps, east and north (m, kept within its side)
        static Mesh sheet;

        /// <summary>One camera's clouds.</summary>
        sealed class Pass
        {
            public string Name;
            public Camera Cam;
            public CommandBuffer Draw, Shade;
            /// <summary>After everything see-through: what is drawn in front of the clouds (the lightning), and the clouds' picture taken back from the other mods.</summary>
            public CommandBuffer After;
            public RenderTexture Now, NowD, HistA, HistDA, HistB, HistDB;
            public bool Flip;
            public Material Walk, Together, Over, Shadow;
            public bool AskedDepth, On;
            /// <summary>When in the camera's drawing the clouds are laid on (see Setup, "first").</summary>
            public CameraEvent When = CameraEvent.BeforeForwardAlpha;
            // where the camera was last frame (the planet's axes), and how it looked
            public Vector3d PrevAt;
            public Vector3 PrevRight, PrevUp, PrevAhead;
            public bool HavePrev;
            public string PrevBody = "";
        }

        readonly Pass far = new Pass { Name = "far" }, near = new Pass { Name = "near" };
        int shadowStrip = -1;                   // which quarter of the shadow's map is drawn this frame (less than nothing: all of it)
        RenderTexture shadowMap, shadowMapB;    // (B: for the walk, how high the sunlit surface stands on each line to the sun)
        Material shadowWalk;
        RenderTexture heapsMap, heapsMapB;      // the map of the low layer's heaps (see HeapsDots)
        Material heapsWalk;
        // (the low clouds' map, drawn smoothly once a frame at four times the weather's own size, which the walk reads: the
        // shader's stage 6. Read at the weather's own size, the edges of the clouds' regions seen from orbit were staircases.)
        RenderTexture mapFine;
        Material mapFineWalk;
        bool heapsOn;
        float shellTopNow = 12000f;     // (the shell's top as last handed to the walk, m: the shadow's square is left off above it)
        Texture2D readBack;
        readonly CloudMap map = new CloudMap();
        readonly List<WeatherCell> cells = new List<WeatherCell>();
        float cellsAt = -100f;
        string cellsBody = "";
        Vector3d drift;                 // how far the wind has carried the clouds (m, the planet's axes; kept within 207 lengths of the noise)
        double driftAt = double.NaN;
        string driftBody = "";
        // the wind's way as the streets and the high cloud's fibres take it: changed only when the wind has turned well round
        // (the camera's place in the noise is hundreds of lengths from its start, and patterns read along the wind there
        // would swing about with every small turn of it)
        Vector3 streetsAcross, streetsAlong;
        string streetsBody = "";
        int frame;
        // the shadow's square: fixed to the planet until the camera has gone far from its middle
        Vector3d shadowMid;
        Vector3 shadowX, shadowY;
        double shadowSide;
        string shadowBody = "";
        float groundHeight;
        Vector3 sunNow;
        // the sun, dimmed for the scene's cameras by the cloud above the craft
        Light dimmed;
        float dimmedFrom = -1f;
        float sunDim = 1f;              // what it is dimmed by (eased)
        float sunAbove = 1f;            // what the shadow says lies above the craft (as last read back)
        readonly Vector3d[] flashAt = new Vector3d[2];
        readonly float[] flashBright = new float[2];

        const double NoiseLength = 9000.0;      // metres to one repeat of the large noise (the small one is 9 times finer)
        // The camera's place in the noise wraps round only every 207 lengths of it (1863 km): 207 is a whole number of each of the
        // repeats the shader reads (the large noise's own, a third, a ninth, a 23rd and a 2.3rd of it), so that nothing jumps
        // when it does. (Wrapped at each length, as at first, everything read at other scales jumped whenever the camera
        // crossed one: every 9 km in flight.)
        const double NoiseWrap = 207.0 * NoiseLength;
        const int Shadow = 512;                 // dots along each side of the shadow's square

        // ---------------------------------------------------------------- shader names
        static readonly int idStage = Shader.PropertyToID("_CloudStage"), idCam = Shader.PropertyToID("_CloudCam"), idHigh = Shader.PropertyToID("_CloudHigh"),
            idRight = Shader.PropertyToID("_CloudRight"), idUp = Shader.PropertyToID("_CloudUp"), idAhead = Shader.PropertyToID("_CloudAhead"),
            idFixedX = Shader.PropertyToID("_CloudFixedX"), idFixedY = Shader.PropertyToID("_CloudFixedY"), idFixedZ = Shader.PropertyToID("_CloudFixedZ"),
            idNoise = Shader.PropertyToID("_CloudNoise"), idDrift = Shader.PropertyToID("_CloudDrift"), idShell = Shader.PropertyToID("_CloudShell"),
            idLook = Shader.PropertyToID("_CloudLook"), idSun = Shader.PropertyToID("_CloudSun"), idSunTint = Shader.PropertyToID("_CloudSunTint"),
            idSky = Shader.PropertyToID("_CloudSky"), idGround = Shader.PropertyToID("_CloudGround"), idAir = Shader.PropertyToID("_CloudAir"),
            idHaze = Shader.PropertyToID("_CloudHaze"), idFlashA = Shader.PropertyToID("_CloudFlashA"), idFlashB = Shader.PropertyToID("_CloudFlashB"), idHoleA = Shader.PropertyToID("_CloudHoleA"), idHoleB = Shader.PropertyToID("_CloudHoleB"), idHoleC = Shader.PropertyToID("_CloudHoleC"),
            idField = Shader.PropertyToID("_CloudField"), idFieldA = Shader.PropertyToID("_CloudFieldA"), idFieldB = Shader.PropertyToID("_CloudFieldB"),
            idFieldLevA = Shader.PropertyToID("_CloudFieldLevA"), idFieldLevB = Shader.PropertyToID("_CloudFieldLevB"), idFieldLevC = Shader.PropertyToID("_CloudFieldLevC"), idFieldLevD = Shader.PropertyToID("_CloudFieldLevD"),
            idFlashTint = Shader.PropertyToID("_CloudFlashTint"), idPrevAt = Shader.PropertyToID("_CloudPrevAt"), idPrevRight = Shader.PropertyToID("_CloudPrevRight"),
            idPrevUp = Shader.PropertyToID("_CloudPrevUp"), idPrevAhead = Shader.PropertyToID("_CloudPrevAhead"), idShadowAt = Shader.PropertyToID("_CloudShadowAt"),
            idShadowX = Shader.PropertyToID("_CloudShadowX"), idShadowY = Shader.PropertyToID("_CloudShadowY"), idSunFixed = Shader.PropertyToID("_CloudSunFixed"),
            idSheet = Shader.PropertyToID("_CloudSheet"), idOver = Shader.PropertyToID("_CloudOver"), idShadeAt = Shader.PropertyToID("_CloudShadeAt"),
            idShadeX = Shader.PropertyToID("_CloudShadeX"), idShadeY = Shader.PropertyToID("_CloudShadeY"), idShade = Shader.PropertyToID("_CloudShade"),
            idShadeSun = Shader.PropertyToID("_CloudShadeSun"), idStreets = Shader.PropertyToID("_CloudStreets"), idAlong = Shader.PropertyToID("_CloudAlong"),
            idShape = Shader.PropertyToID("_CloudShape"), idDetail = Shader.PropertyToID("_CloudDetail"), idMap = Shader.PropertyToID("_CloudMap"),
            idMapB = Shader.PropertyToID("_CloudMapB"), idMapC = Shader.PropertyToID("_CloudMapC"), idLut = Shader.PropertyToID("_CloudAirLut"), idNow = Shader.PropertyToID("_CloudNow"),
            idNowD = Shader.PropertyToID("_CloudNowD"), idHist = Shader.PropertyToID("_CloudHist"), idHistD = Shader.PropertyToID("_CloudHistD"),
            idShadowMap = Shader.PropertyToID("_CloudShadowMap"),
            idOthers = Shader.PropertyToID("_WeatherClouds"), idOthersPic = Shader.PropertyToID("_WeatherCloudsPic"), idOthersDist = Shader.PropertyToID("_WeatherCloudsDist");
        static readonly int[] idCellA = { Shader.PropertyToID("_CloudCellA0"), Shader.PropertyToID("_CloudCellA1"), Shader.PropertyToID("_CloudCellA2"), Shader.PropertyToID("_CloudCellA3"), Shader.PropertyToID("_CloudCellA4"), Shader.PropertyToID("_CloudCellA5") };
        static readonly int[] idCellB = { Shader.PropertyToID("_CloudCellB0"), Shader.PropertyToID("_CloudCellB1"), Shader.PropertyToID("_CloudCellB2"), Shader.PropertyToID("_CloudCellB3"), Shader.PropertyToID("_CloudCellB4"), Shader.PropertyToID("_CloudCellB5") };

        // ---------------------------------------------------------------- loading

        public static string Folder => KSPUtil.ApplicationRootPath + "GameData/NaturalWeather/PluginData/";

        static Shader[] Load(string file)
        {
            string path = Folder + file;
            if (!File.Exists(path)) return null;
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) return new Shader[0];
            Shader[] found = bundle.LoadAllAssets<Shader>();
            bundle.Unload(false);
            return found;
        }

        static Shader Pick(Shader[] found, string name)
        {
            if (found != null) foreach (Shader one in found) if (one != null && one.name == name && one.isSupported) return one;
            return null;
        }

        static void LoadShaders()
        {
            tried = true;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore)
                {
                    // (OpenGL: the shaders packed by hand from tools/shaderpack/*.glsl, one to a file)
                    walkShader = Pick(Load("clouds.bundle"), "NaturalWeather/Clouds");
                    overShader = Pick(Load("cloudsover.bundle"), "NaturalWeather/CloudsOver");
                    shadeShader = Pick(Load("cloudshade.bundle"), "NaturalWeather/CloudShade");
                    if (walkShader == null || overShader == null || shadeShader == null) why = "the clouds' shaders (GameData/NaturalWeather/PluginData/clouds*.bundle) are missing or this graphics card cannot run them";
                }
                else
                {
                    // (elsewhere, Direct3D on Windows: the same shaders built by the Unity editor from tools/unityshaders, if there is
                    // such a build in the mod's folder; there is none yet)
                    string system = Application.platform == RuntimePlatform.WindowsPlayer ? "windows" : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "mac";
                    Shader[] built = Load("clouds-" + system + ".bundle");
                    walkShader = Pick(built, "NaturalWeather/Clouds"); overShader = Pick(built, "NaturalWeather/CloudsOver"); shadeShader = Pick(built, "NaturalWeather/CloudShade");
                    if (walkShader == null || overShader == null || shadeShader == null)
                        why = built == null ? "there is no cloud shader for " + SystemInfo.graphicsDeviceType + " yet (only for OpenGL, on Macs and Linux)" : "the clouds' shaders for " + SystemInfo.graphicsDeviceType + " did not load";
                }
                if (walkShader == null || overShader == null || shadeShader == null) walkShader = overShader = shadeShader = null;
            }
            catch (Exception ex) { walkShader = overShader = shadeShader = null; why = "the clouds' shader did not load (" + ex.Message + ")"; }
            Debug.Log("[Natural Weather] " + (walkShader != null ? "clouds: shaders loaded" : "no clouds: " + why));
            if (sheet == null)
            {
                sheet = new Mesh { name = "Natural Weather sheet" };
                sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
                sheet.triangles = new[] { 0, 1, 2 };
                sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
        }

        /// <summary>
        /// The field of heaps made beforehand, read from its file into a texture of three dimensions with every coarser copy as
        /// the file has it (the copies' second number is not a mean, so the graphics card may not make them). Once.
        /// </summary>
        static void LoadField()
        {
            fieldTried = true;
            if (noField == null)
            {
                noField = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { name = "Natural Weather: no field of heaps" };
                noField.SetPixels32(new[] { new Color32(0, 0, 0, 255) });
                noField.Apply(false, true);
            }
            string path = Folder + "heaps.field";
            try
            {
                if (!File.Exists(path)) { fieldWhy = "there is no " + path; return; }
                byte[] all = File.ReadAllBytes(path);
                const int head = 28 + 4 * 64 + 4 * 64 * 16;
                if (all.Length < head || all[0] != 'N' || all[1] != 'W' || all[2] != 'F' || all[3] != '3') { fieldWhy = "heaps.field is not a field of heaps this version can read"; return; }
                int nx = BitConverter.ToInt32(all, 4), ny = BitConverter.ToInt32(all, 8), nz = BitConverter.ToInt32(all, 12);
                if (nx < 1 || ny < 1 || nz < 1 || nx > 1024 || ny > 1024 || nz > 256) { fieldWhy = "heaps.field has no sensible size"; return; }
                fieldSide = BitConverter.ToSingle(all, 16); fieldFar = BitConverter.ToSingle(all, 24);
                // (the level for each sixteenth of the sky, from the file's sixty-four)
                for (int i = 0; i < 16; i++) fieldLevels[i] = 0.5f * (BitConverter.ToSingle(all, 28 + 4 * (4 * i + 1)) + BitConverter.ToSingle(all, 28 + 4 * (4 * i + 2)));
                // (Four numbers a cell, though the file has three. Read into the two-number kind (RG16), when the file had two, the
                // whole field came out on this Unity's OpenGL as one thick slab of cloud, 2026-10-11.)
                var t = new Texture3D(nx, ny, nz, TextureFormat.RGBA32, true)
                {
                    wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Repeat, wrapModeW = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear, anisoLevel = 0, name = "Natural Weather: heaps made beforehand"
                };
                int at = head, mx = nx, my = ny, mz = nz;
                for (int level = 0; level < t.mipmapCount; level++)
                {
                    int count = mx * my * mz;
                    if (at + 3 * count > all.Length) { Destroy(t); fieldWhy = "heaps.field is cut short"; return; }
                    var px = new Color32[count];
                    for (int n = 0; n < count; n++) px[n] = new Color32(all[at + 3 * n], all[at + 3 * n + 1], all[at + 3 * n + 2], 255);
                    t.SetPixels32(px, level);
                    at += 3 * count;
                    mx = Math.Max(1, mx / 2); my = Math.Max(1, my / 2); mz = Math.Max(1, mz / 2);
                }
                t.Apply(false, true);
                field = t;
                Debug.Log("[Natural Weather] clouds: heaps made beforehand, " + nx + " by " + ny + " by " + nz + " over " + fieldSide.ToString("F0") + " m");
            }
            catch (Exception ex) { field = null; fieldWhy = "heaps.field did not load (" + ex.Message + ")"; }
            finally { if (field == null) Debug.Log("[Natural Weather] clouds: heaps carved from the noise, as before 0.3.0: " + fieldWhy); }
        }

        /// <summary>The two cubes of noise the clouds are carved from, made on the graphics card slice by slice (once).</summary>
        static void MakeNoise()
        {
            if (shape != null && shape.IsCreated() && detail != null && detail.IsCreated()) return;
            var maker = new Material(walkShader) { name = "Natural Weather: noise maker" };
            maker.SetVector(idSheet, new Vector4(1f, 1f, 0.5f, 1f));
            var commands = new CommandBuffer { name = "Natural Weather: the clouds' noise" };
            RenderTexture Cube(int n, string name)
            {
                var t = new RenderTexture(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    dimension = TextureDimension.Tex3D, volumeDepth = n, useMipMap = true, autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = name
                };
                t.Create();
                return t;
            }
            if (shape != null) shape.Release();
            if (detail != null) detail.Release();
            shape = Cube(128, "Natural Weather: large noise");
            detail = Cube(64, "Natural Weather: small noise");
            for (int which = 0; which < 2; which++)
            {
                RenderTexture into = which == 0 ? shape : detail;
                for (int z = 0; z < into.volumeDepth; z++)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetVector(idStage, new Vector4(which, z, 0f, 0f));
                    commands.SetRenderTarget(into, 0, CubemapFace.Unknown, z);
                    commands.DrawMesh(sheet, Matrix4x4.identity, maker, 0, 0, block);
                }
            }
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();
            Destroy(maker);
            noiseHasMips = false;
            try { shape.GenerateMips(); detail.GenerateMips(); noiseHasMips = true; }
            catch (Exception ex) { Debug.Log("[Natural Weather] the clouds' noise has no smaller copies: " + ex.Message); }
            Debug.Log("[Natural Weather] clouds: noise made (" + shape.width + " and " + detail.width + " cells each way" + (noiseHasMips ? ", with smaller copies" : "") + ")");
        }

        // ---------------------------------------------------------------- the addon

        void Awake()
        {
            CloudsPage.Page();
            Keystone.Weather.Flashed += OnFlash;
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
        }

        void OnDestroy()
        {
            Keystone.Weather.Flashed -= OnFlash;
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            Undim();
            Detach(far); Detach(near);
            Free(far); Free(near);
            if (laying != null) Destroy(laying);
            if (layNothing != null) Destroy(layNothing);
            Shader.SetGlobalVector(idOthers, Vector4.zero);
            if (shadowMap != null) { shadowMap.Release(); Destroy(shadowMap); shadowMap = null; }
            if (shadowMapB != null) { shadowMapB.Release(); Destroy(shadowMapB); shadowMapB = null; }
            if (shadowWalk != null) Destroy(shadowWalk);
            FreeHeaps();
            if (heapsWalk != null) Destroy(heapsWalk);
            if (mapFine != null) { mapFine.Release(); Destroy(mapFine); mapFine = null; }
            if (mapFineWalk != null) Destroy(mapFineWalk);
            if (readBack != null) Destroy(readBack);
            map.Release();
            CloudsEve.Show();
            Drawing = false; SunThrough = 1f;
            Report = "";
        }

        void OnFlash(WeatherFlash flash) => CloudFlashes.Add(flash);

        static void Detach(Pass p)
        {
            if (p.Cam != null)
            {
                if (p.Draw != null) p.Cam.RemoveCommandBuffer(p.When, p.Draw);
                if (p.After != null) p.Cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, p.After);
                if (p.Shade != null) p.Cam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, p.Shade);
                if (p.AskedDepth) p.Cam.depthTextureMode &= ~DepthTextureMode.Depth;
            }
            p.Cam = null; p.AskedDepth = false; p.On = false; p.HavePrev = false;
        }

        static void Free(Pass p)
        {
            foreach (RenderTexture t in new[] { p.Now, p.NowD, p.HistA, p.HistDA, p.HistB, p.HistDB }) if (t != null) { t.Release(); Destroy(t); }
            p.Now = p.NowD = p.HistA = p.HistDA = p.HistB = p.HistDB = null;
            foreach (Material m in new[] { p.Walk, p.Together, p.Over, p.Shadow }) if (m != null) Destroy(m);
            p.Walk = p.Together = p.Over = p.Shadow = null;
            if (p.Draw != null) p.Draw.Release();
            if (p.Shade != null) p.Shade.Release();
            if (p.After != null) p.After.Release();
            p.Draw = p.Shade = p.After = null;
        }

        static Camera Find(string name)
        {
            foreach (Camera c in Camera.allCameras) if (c != null && c.name == name && c.isActiveAndEnabled) return c;
            return null;
        }

        void Off(string said)
        {
            if (far.On || near.On) { Detach(far); Detach(near); }
            CloudsEve.Show();
            Drawing = false; SunThrough = 1f; sunDim = 1f; sunAbove = 1f;
            Report = said;
        }

        // ---------------------------------------------------------------- the sun, dimmed by the cloud above the craft

        // (Dimmed for the scene's two cameras only: from the first of them beginning to the last of them done. The game sets the
        // sun's strength again every frame itself.)
#if DEV
        // (for finding a fault: how far a camera has moved and turned between this component's LateUpdate, where the
        // clouds' lines of sight are set out, and the moment the camera draws)
        static Vector3 lateAt; static Quaternion lateLooks; static int lateFrame = -1; static Camera lateCam;
        static float lagTurn, lagMove; static int lagFrames;
        public static string Lag(string arg)
        {
            string said = "between LateUpdate and drawing, over " + lagFrames + " frames, the far camera turned by up to " + lagTurn.ToString("F3") + " degrees and moved by up to " + lagMove.ToString("F3") + " m";
            lagTurn = lagMove = 0f; lagFrames = 0;
            return said;
        }
#endif

        void BeforeCamera(Camera cam)
        {
            if (due && cam != null && (cam.name == "Camera 01" || cam.name == "Camera 00")) { due = false; Frame(); }
#if DEV
            if (cam != null && cam == lateCam && Time.frameCount == lateFrame)
            {
                lagTurn = Mathf.Max(lagTurn, Quaternion.Angle(lateLooks, cam.transform.rotation));
                lagMove = Mathf.Max(lagMove, (cam.transform.position - lateAt).magnitude);
                lagFrames++;
            }
#endif
            if (cam == null || !Drawing || sunDim >= 0.999f || (cam.name != "Camera 01" && cam.name != "Camera 00")) return;
            Light sun = Sun.Instance != null ? Sun.Instance.sunLight : null;
            if (sun == null) return;
            if (dimmed == null) { dimmed = sun; dimmedFrom = sun.intensity; sun.intensity = dimmedFrom * sunDim; }
        }

        void AfterCamera(Camera cam)
        {
            if (cam != null && cam.name == "Camera 00") Undim();
        }

        void Undim()
        {
            if (dimmed != null && dimmedFrom >= 0f) dimmed.intensity = dimmedFrom;
            dimmed = null; dimmedFrom = -1f;
        }

        // At the start of each frame (where reading back what the graphics card made last frame costs nothing): how much cloud the
        // shadow says lies between the craft and the sun.
        void Update()
        {
            Undim();
            if (!Drawing || shadowMap == null || !shadowMap.IsCreated() || (frame & 3) != 0) return;
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (v == null || body == null || shadowSide <= 0.0) return;
            Vector3d p = v.CoMD - body.position;
            double h = p.magnitude - body.Radius;
            Vector3d up = p.normalized;
            double sunUp = Vector3d.Dot(up, (Vector3d)sunNow);
            if (sunUp < -0.03) { sunAbove = 1f; return; }
            Vector3d g = p - (Vector3d)sunNow * ((h - groundHeight) / Math.Max(sunUp, 0.06));
            Vector3d mid = SceneOf(body, shadowMid);
            Vector3d rel = g - mid;
            double sx = Vector3d.Dot(rel, (Vector3d)SceneOfFixed(body, shadowX)) / shadowSide + 0.5, sy = Vector3d.Dot(rel, (Vector3d)SceneOfFixed(body, shadowY)) / shadowSide + 0.5;
            if (sx <= 0.0 || sx >= 1.0 || sy <= 0.0 || sy >= 1.0) { sunAbove = 1f; return; }
            if (readBack == null) readBack = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = shadowMap;
            readBack.ReadPixels(new Rect(Mathf.Clamp((int)(sx * Shadow), 0, Shadow - 1), Mathf.Clamp((int)(sy * Shadow), 0, Shadow - 1), 1, 1), 0, 0, false);
            RenderTexture.active = before;
            Color m = readBack.GetPixel(0, 0);
            float lowest = m.g * 1000f, highest = m.b * 1000f;
            float above = m.r * Mathf.Clamp01((highest - (float)h) / Mathf.Max(highest - lowest, 60f));
            sunAbove = float.IsNaN(above) ? 1f : Mathf.Exp(-above);
        }

        /// <summary>
        /// The planet's own axes as they stand in the scene now: towards latitude 0 longitude 0, towards longitude 90, the north
        /// pole. (GetSurfaceNVector, which turns the planet's frame into the scene's. GetRelSurfaceNVector does not: it answers in
        /// the planet's own frame, and the scene stands turned about the pole against that by CelestialBody.directRotAngle: a
        /// different angle after every load, and one that runs on with the planet's turning above the height where the game
        /// stops turning the scene with the planet. Until 0.2.2 these were taken from it, and were the scene's own x, z and y.)
        /// </summary>
        static void Axes(CelestialBody body, out Vector3d fx, out Vector3d fy, out Vector3d fz)
        {
            fx = body.GetSurfaceNVector(0.0, 0.0).normalized; fy = body.GetSurfaceNVector(0.0, 90.0).normalized; fz = body.GetSurfaceNVector(90.0, 0.0).normalized;
        }

        /// <summary>A place on the planet, from its centre, in the scene's axes.</summary>
        static Vector3d SceneAt(CelestialBody body, double lat, double lon, double height) => body.GetWorldSurfacePosition(lat, lon, height) - body.position;

        static Vector3d SceneOf(CelestialBody body, Vector3d f)
        {
            Axes(body, out Vector3d fx, out Vector3d fy, out Vector3d fz);
            return fx * f.x + fy * f.y + fz * f.z;
        }

        static Vector3 SceneOfFixed(CelestialBody body, Vector3 f) => (Vector3)SceneOf(body, new Vector3d(f.x, f.y, f.z));

        // ---------------------------------------------------------------- each frame

        // The clouds are set out by where the camera stands and looks WHEN IT DRAWS. The game moves its flight camera in a
        // LateUpdate of its own that comes after this one, so set out from here they were set out for the camera of the
        // frame before: with the camera swung round, or riding a rocket, the whole sky came a frame behind the ground
        // (the owner, 2026-10-13: "the clouds aren't anchored to the earth, so they have a slight movement delay";
        // measured: the camera turned by up to 80 degrees between this LateUpdate and its drawing). So the frame's work
        // is done from BeforeCamera, when the first of the flight's cameras is about to draw; from here only when there
        // is no such camera to wait for, and when the last frame's was never taken up.
        bool due;

        void LateUpdate()
        {
            bool waits = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !MapView.MapIsEnabled;
            if (due || !waits) Frame();
            due = waits;
        }

        void Frame()
        {
            frame++;
#if DEV
            if (heldOff) { Off("Clouds held off (development build)."); return; }
#endif
            if (!CloudsPage.Wanted || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || MapView.MapIsEnabled) { Off(""); return; }
            CelestialBody body = FlightGlobals.currentMainBody;
            if (!CloudSource.Worth(body)) { Off(CloudSource.WhyNot(body)); return; }
            if (!tried) LoadShaders();
            if (!fieldTried) LoadField();
            if (walkShader == null) { Off("No clouds: " + why + "."); return; }
            MakeNoise();
            CloudAir air = CloudAir.Of(body);
            Camera farCam = Find("Camera 01"), nearCam = Find("Camera 00");
            if (nearCam == null) { Off(""); return; }
#if DEV
            lateCam = farCam != null ? farCam : nearCam; lateAt = lateCam.transform.position; lateLooks = lateCam.transform.rotation; lateFrame = Time.frameCount;
#endif
            bool split = farCam != null;            // (OpenGL: two cameras; Direct3D: Camera 00 alone, as far as the scene goes)
            if (!split) farCam = nearCam;
            Split = split;

            // ---- where everything is
            double R = body.Radius;
            Vector3d camRel = (Vector3d)farCam.transform.position - body.position;
            double alt = camRel.magnitude - R;
            body.GetLatLonAlt((Vector3d)farCam.transform.position, out double camLat, out double camLon, out double _);
            double ut = Planetarium.GetUniversalTime();
            double reach = Math.Max(6.0, Math.Acos(Math.Min(1.0, R / (R + Math.Max(0.0, alt)))) * 180.0 / Math.PI + 5.0);
            if (!map.Update(body, ut, camLat, camLon, reach, false) || !air.Ready) { Off("Clouds: getting ready."); return; }

            // The storms near the camera (drawn as towers where the weather has them), asked again every second.
            if (Time.realtimeSinceStartup - cellsAt > 1f || cellsBody != body.bodyName)
            {
                cellsAt = Time.realtimeSinceStartup; cellsBody = body.bodyName;
                cells.Clear();
                CloudSource.Cells(body, camLat, camLon, Math.Min(400000.0, reach * R * Math.PI / 180.0), ut, cells);
                Vector3d here = camRel;
                cells.Sort((a, b) => Distance(body, a, here).CompareTo(Distance(body, b, here)));
                if (cells.Count > 6) cells.RemoveRange(6, cells.Count - 6);
            }
            if (!map.AnyCloud && cells.Count == 0) { Off("Clouds: none within sight."); return; }
            if (CloudsPage.HideEve != null && CloudsPage.HideEve) CloudsEve.Hide(body); else CloudsEve.Show();

            // The planet's own axes, in the scene's: towards latitude 0 longitude 0, towards longitude 90, the north pole.
            Axes(body, out Vector3d fx, out Vector3d fy, out Vector3d fz);
            Vector3d camFixed = new Vector3d(Vector3d.Dot(camRel, fx), Vector3d.Dot(camRel, fy), Vector3d.Dot(camRel, fz));
            Vector3 ToFixed(Vector3 v) => new Vector3((float)Vector3d.Dot(v, fx), (float)Vector3d.Dot(v, fy), (float)Vector3d.Dot(v, fz));

            // The wind carries the clouds (as the weather says the low clouds move here): how far, kept up from frame to frame.
            WeatherHere hereNow = CloudSource.At(body, camLat, camLon, ut);
            Vector3d up = camRel.normalized;
            Vector3d east = Vector3d.Cross(up, fz).normalized;          // (Unity's axes are left-handed: up x pole is east)
            Vector3d north = Vector3d.Cross(east, up).normalized;
            Vector3d windScene = east * hereNow.Drift.x + north * hereNow.Drift.y;
            Vector2d heapsWind = new Vector2d(hereNow.Drift.x, hereNow.Drift.y);
            Vector3d windFixed = new Vector3d(Vector3d.Dot(windScene, fx), Vector3d.Dot(windScene, fy), Vector3d.Dot(windScene, fz));
            if (double.IsNaN(driftAt) || driftBody != body.bodyName || Math.Abs(ut - driftAt) > 3600.0)
            {
                drift = windFixed * (ut % 100000.0);
                fieldDrift = new Vector2d(hereNow.Drift.x, hereNow.Drift.y) * (ut % 100000.0);
                driftBody = body.bodyName;
            }
            else { drift += windFixed * (ut - driftAt); fieldDrift += new Vector2d(hereNow.Drift.x, hereNow.Drift.y) * (ut - driftAt); }
            fieldDrift = new Vector2d(Wrap(fieldDrift.x, fieldSide), Wrap(fieldDrift.y, fieldSide));
            driftAt = ut;
            drift = new Vector3d(Wrap(drift.x, NoiseWrap), Wrap(drift.y, NoiseWrap), Wrap(drift.z, NoiseWrap));
#if DEV
            if (heldStill) { drift = Vector3d.zero; fieldDrift = Vector2d.zero; }
            driftWas = drift; utWas = ut; windWas = windFixed;
            lonGame = camLon; lonOwn = Math.Atan2(camFixed.y, camFixed.x) * 180.0 / Math.PI; turnWas = body.directRotAngle;
#endif
            Vector3d noiseAt = camFixed - drift;
            // (the craft's wake in cloud: where it has been, kept in the cloud's own frame)
            CloudsWake.Follow(body, ut, drift, fx, fy, fz, map.LowestBase, map.HighestTop, NoiseWrap);
            var noise = new Vector4((float)(noiseAt.x / NoiseLength), (float)(noiseAt.y / NoiseLength), (float)(noiseAt.z / NoiseLength), (float)NoiseLength);
            Vector3 windDir = windFixed.magnitude > 0.01 ? (Vector3)(windFixed.normalized) : Vector3.zero;
            // Cloud streets: heaps in rows along a steady wind of some strength (the churning air's rolls lie along it), not in
            // light air; the rows' way kept until the wind has turned more than 25 degrees.
            Vector3 upFixed = (Vector3)camFixed.normalized;
            Vector3 alongNow = windDir.sqrMagnitude > 0.5f ? Vector3.ProjectOnPlane(windDir, upFixed).normalized : Vector3.zero;
            if (alongNow.sqrMagnitude > 0.5f && (streetsBody != body.bodyName || streetsAlong.sqrMagnitude < 0.5f || Vector3.Angle(alongNow, streetsAlong) > 25f && Vector3.Angle(-alongNow, streetsAlong) > 25f))
            {
                streetsAlong = alongNow;
                streetsAcross = Vector3.Cross(upFixed, alongNow).normalized;
                streetsBody = body.bodyName;
            }
            float windSpeed = (float)windFixed.magnitude;
            float streetsStrength = streetsAlong.sqrMagnitude > 0.5f ? Mathf.Clamp01((windSpeed - 4f) / 8f) * 0.7f : 0f;
            float boil = (float)Wrap(ut * 0.0012, 1.0);

            // ---- the sun and the light round about
            CelestialBody sunBody = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            Vector3 toSun = sunBody != null ? (Vector3)(sunBody.position - (Vector3d)farCam.transform.position).normalized : Vector3.up;
            sunNow = toSun;
            float sunUp = Vector3.Dot((Vector3)up, toSun);
            float brightness = CloudsPage.Brightness != null ? (float)CloudsPage.Brightness : 1f;
            // (the light at the clouds' height, a kilometre or two up, from the table: the sky lights them as the sun lights the air)
            Color sunAtClouds = air.Through(1500f, Mathf.Max(sunUp, -0.2f));
            // (under a deck of cloud the light from above is the cloud's grey, not the sky's blue)
            float overhead = Mathf.Max(CloudSource.Kind(hereNow.Low) >= 0f ? hereNow.Low.Cover : 0f, CloudSource.Kind(hereNow.Middle) >= 0f ? hereNow.Middle.Cover : 0f);
            float greyed = overhead * overhead;
            float skyLum = 0.2126f * air.Sky.r + 0.7152f * air.Sky.g + 0.0722f * air.Sky.b;
            Color skyLight = Color.Lerp(air.Sky, new Color(skyLum, skyLum, skyLum), greyed) * (0.32f * brightness);
            float groundLit = Mathf.Max(0f, sunUp);
            Color groundLight = new Color(0.30f, 0.30f, 0.26f) * (0.4f * groundLit * brightness) * sunAtClouds;
            float night = 0.0035f * brightness;

            // ---- how much cloud, how thick, how fine
            float shellBottom = Mathf.Max(0f, map.LowestBase - 60f), shellTop = Mathf.Max(map.HighestTop, shellBottom + 300f) + 150f;
            foreach (WeatherCell c in cells) { shellBottom = Mathf.Min(shellBottom, Mathf.Max(0f, c.Base - 60f)); shellTop = Mathf.Max(shellTop, c.Top + 300f); }

            // ---- a cyclone within sight, as the shader takes it (its wall, canopy and bands are drawn as shapes of their
            // own: clouds.glsl, "a cyclone"): the nearest, where the weather is Natural Weather's own
            var eyeA = Vector4.zero; var eyeB = Vector4.zero; var eyeC = new Vector4(1f, 0f, 0f, 0f); var eyeD = Vector4.zero;
            Cyclone eyeOf = null;
            if (CloudSource.Keystones && Keystone.Weather.Provider is Provider)
            {
                World world = World.Of(body);
                double nearest = double.MaxValue;
                foreach (Cyclone c in Violent.Cyclones(world, ut))
                {
                    double d = Model.Distance(world, camLat, camLon, c.Lat, c.Lon);
                    if (d < nearest) { nearest = d; eyeOf = c; }
                }
                if (eyeOf != null && nearest > reach * R * Math.PI / 180.0 + 14.0 * eyeOf.Rmw) eyeOf = null;
                if (eyeOf != null)
                {
                    double ground = 0.0;
                    try { ground = Math.Max(0.0, body.TerrainAltitude(eyeOf.Lat, eyeOf.Lon, true)); } catch (Exception) { }
                    Vector3d mid = SceneAt(body, eyeOf.Lat, eyeOf.Lon, 0.0);
                    Vector3d eastThere = Vector3d.Cross(mid.normalized, fz).normalized;
                    float baseAt = (float)ground + 350f;
                    // (as tall as the weather's own storm cloud in it: Model.Build)
                    float topAt = (float)ground + 2500f + (world.Climate.Tops * 0.93f - 2500f) * (0.5f + 0.5f * eyeOf.Now);
                    eyeA = new Vector4((float)mid.x, (float)mid.y, (float)mid.z, eyeOf.Rmw);
                    eyeB = new Vector4(eyeOf.Now, eyeOf.Spin, (float)((eyeOf.Id * 0.7) % (2.0 * Math.PI)), topAt);
                    eyeC = new Vector4((float)eastThere.x, (float)eastThere.y, (float)eastThere.z, baseAt);
                    // (the wall's cloud goes round the eye at half the speed of its wind)
                    eyeD = new Vector4((float)((ut * 0.5 * eyeOf.Speed / Math.Max(eyeOf.Rmw, 1000f)) % (2.0 * Math.PI)), 0f, 0f, 0f);
                    shellTop = Mathf.Max(shellTop, topAt * 1.12f + 150f);
                    // (its rain reaches the sea under its core: from near, the clouds are walked from the sea up)
                    shellBottom = nearest < 6.0 * eyeOf.Rmw ? 0f : Mathf.Min(shellBottom, baseAt - 60f);
                }
            }
            // ---- a squall line within sight, as the shader takes it (its shelf cloud is drawn along its wind's front:
            // clouds.glsl, shelfAt): the nearest whose row is near enough to see, where the weather is Natural Weather's own
            var shelfA = Vector4.zero; var shelfB = new Vector4(1f, 0f, 0f, 0f); var shelfC = Vector4.zero;
            if (CloudSource.Keystones && Keystone.Weather.Provider is Provider)
            {
                World world = World.Of(body);
                Squall line = null; float lineAhead = 0f, best = float.MaxValue;
                foreach (Squall q in Violent.Squalls(world, ut))
                {
                    if (q.Now < 0.1f || !Violent.SquallAt(world, q, camLat, camLon, out float ahead, out float along)) continue;
                    float far = Mathf.Abs(ahead) + Mathf.Max(0f, Mathf.Abs(along) - q.Length * 0.5f);
                    if (far < best) { best = far; line = q; lineAhead = ahead; }
                }
                if (line != null)
                {
                    Vector3d mid = SceneAt(body, line.Lat, line.Lon, 0.0);
                    Vector3d upThere = mid.normalized;
                    Vector3d eastThere = Vector3d.Cross(upThere, fz).normalized, northThere = Vector3d.Cross(eastThere, upThere).normalized;
                    float speed = Mathf.Sqrt(line.MoveE * line.MoveE + line.MoveN * line.MoveN);
                    Vector3d moves = (eastThere * line.MoveE + northThere * line.MoveN) / Math.Max(speed, 0.1f);
                    shelfA = new Vector4((float)mid.x, (float)mid.y, (float)mid.z, line.Length * 0.5f);
                    shelfB = new Vector4((float)moves.x, (float)moves.y, (float)moves.z, line.Now);
                    shelfC = new Vector4(line.Base, 6000f, 0f, 0f);
                    shellBottom = Mathf.Min(shellBottom, Mathf.Max(0f, line.Base - 460f));
                    shellTop = Mathf.Max(shellTop, line.Base + 4200f);
                }
            }

            // How far from the camera the nearest cloud can be: under everything that can hold cloud, as far as that is above
            // the camera; and no further than a storm's rain, which hangs from its base to the ground. The clouds are laid on
            // the picture no nearer than that (see Setup).
            float clear = alt < shellBottom ? (float)(shellBottom - alt) : 0f;
            foreach (WeatherCell c in cells)
            {
                // (its rain as the shader draws it: an upright cylinder round the storm's middle, three quarters of its reach
                // across: clouds.glsl, rainWalk)
                if (c.Rain < 0.1f || c.Strength < 0.15f) continue;
                Vector3d axis = SceneAt(body, c.Latitude, c.Longitude, 0.0).normalized;
                double off = (camRel - axis * Vector3d.Dot(camRel, axis)).magnitude - 0.75 * c.Radius;
                clear = Mathf.Min(clear, Mathf.Max(0f, (float)off - 50f));
            }
            SheetClear = clear;
            // When the clouds are laid on: after everything see-through, as a rule. But where one camera draws the whole scene
            // (Direct3D) that puts them over smoke, flames and spray however near: a rocket's smoke at the pad stood behind the
            // clouds of the horizon, and a crash's smoke had the whole sky's heaps drawn across it (seen 2026-10-11; on OpenGL
            // the near camera draws what is near after the far camera's clouds). So there, while the camera is below
            // everything that can hold cloud, they are laid on before the see-through things: whatever of those is drawn is
            // then in front of the clouds, which from below is right for all that is lower than the clouds, the sea among
            // it. (What it gets wrong: a trail of smoke that has climbed through the clouds shows in front of them. With the
            // camera among the clouds or above them the old order is kept: seen from above, a sea drawn after the clouds
            // would be drawn over them. Kept too where Scatterer is installed: it lays its haze on from a copy of the
            // picture taken at that same moment, and which of the two comes first has not been tried.)
            // (Since 0.4.0 the clouds are laid on in their turn among the see-through things, on every system, with Scatterer or
            // without: see LaidQueue and Laying. What follows is kept only for the report.)
            bool first = !LaidLate();
            LaidFirst = first;
            int quality = CloudsPage.Quality != null ? (int)CloudsPage.Quality : 1;
            int steps = quality == 0 ? 56 : quality == 1 ? 88 : 128;
            // (light lost per metre in the thickest cloud: a little more than a real heap's core takes, 0.05 to 0.1 a metre, since
            // only the cores reach it (the shader's density reaches it a third of the way in from a heap's edge); less, and heaps
            // against the sun were pale and their shaded sides flat)
            // (The rework of 2026-10-11: 0.07 where it was 0.15. At 0.15 a heap was solid a few metres in: a hard outline round two
            // flat tones, which the user called "comical, like anime clouds", not volumetric. At 0.07, about what a real heap takes
            // (0.03 to 0.06 a metre), its thin places glow and its body shades from the lit side to the far one.)
            float thick = 0.07f * (CloudsPage.Thickness != null ? (float)CloudsPage.Thickness : 1f);
            float amount = CloudsPage.Amount != null ? (float)CloudsPage.Amount : 1f;

            // ---- lightning (the brightest two flashes going on now)
            int flashes = CloudFlashes.Now(body, ut, flashAt, flashBright);
#if DEV
            if (heldFlash > 0f) { flashAt[0] = heldFlashAt; flashBright[0] = heldFlash; flashes = Math.Max(flashes, 1); }
#endif
            // (the heaps made as smoke is made, Heaps.cs: the noise's own heaps are cleared from where they stand)
            Vector4 HoleVector(int i)
            {
                if (i >= Heaps.Now.Count || Heaps.Body != body) return Vector4.zero;
                Heaps.Heap heap = Heaps.Now[i];
                Vector3d at = SceneAt(body, heap.lat, heap.lon, heap.baseAt);
                return new Vector4((float)(at.x - camRel.x), (float)(at.y - camRel.y), (float)(at.z - camRel.z), heap.wide * 2.1f);
            }
            Vector4 FlashVector(int i) => i < flashes ? new Vector4((float)(flashAt[i].x - camRel.x), (float)(flashAt[i].y - camRel.y), (float)(flashAt[i].z - camRel.z), flashBright[i] * 0.6f) : Vector4.zero;

            // ---- the storms, as the shader takes them
            var cellA = new Vector4[6];
            var cellB = new Vector4[6];
            // (not those inside a cyclone: its own wall and bands stand there, and each storm's anvil drawn as well was a
            // round plate lying on its canopy. The weather keeps them: their lightning lights the cyclone's cloud.)
            int cellsSent = 0;
            for (int i = 0; i < cells.Count && cellsSent < 6; i++)
            {
#if DEV
                if (NoStorms || cellsSent >= StormsSent) break;
#endif
                WeatherCell c = cells[i];
                if (eyeOf != null && Model.Distance(World.Of(body), c.Latitude, c.Longitude, eyeOf.Lat, eyeOf.Lon) < 13.5 * eyeOf.Rmw) continue;
                Vector3d mid = SceneAt(body, c.Latitude, c.Longitude, 0.0);
                cellA[cellsSent] = new Vector4((float)mid.x, (float)mid.y, (float)mid.z, c.Radius);
                cellB[cellsSent] = new Vector4(c.Base, c.Top, c.Strength, c.Rain);
#if DEV
                if (cellsSent == 0) StormsSaid = "";
                StormsSaid += "#" + cellsSent + " " + (Model.Distance(World.Of(body), c.Latitude, c.Longitude, body.GetLatitude(FlightCamera.fetch != null ? FlightCamera.fetch.transform.position : Vector3.zero), body.GetLongitude(FlightCamera.fetch != null ? FlightCamera.fetch.transform.position : Vector3.zero)) / 1000.0).ToString("F1") + " km r " + c.Radius.ToString("F0") + " base " + c.Base.ToString("F0") + " top " + c.Top.ToString("F0") + " strength " + c.Strength.ToString("F2") + "; ";
#endif
                cellsSent++;
            }

            // ---- a tornado or a waterspout near the camera, as the shader takes it (its funnel is drawn with the clouds): the
            // nearest, where the weather is Natural Weather's own
            var twistA = Vector4.zero; var twistB = Vector4.zero;
            if (CloudSource.Keystones && Keystone.Weather.Provider is Provider)
            {
                World world = World.Of(body);
                Column column = Model.ColumnAt(world, camLat, camLon, ut);
                if (Violent.Nearest(world, column.Twisters, camLat, camLon, out Twister twister, out double twistFar) && twistFar < 45000.0 && twister.Reach > 0.03f)
                {
                    double ground = 0.0;
                    try { ground = Math.Max(0.0, body.TerrainAltitude(twister.Lat, twister.Lon, true)); } catch (Exception) { }
                    Vector3d foot = SceneAt(body, twister.Lat, twister.Lon, ground);
                    twistA = new Vector4((float)foot.x, (float)foot.y, (float)foot.z, twister.Core);
                    // (how far it has turned: the wind at its core's edge goes round once in 2 pi core / speed seconds)
                    double turned = twister.Spin * ut * twister.Speed / Math.Max(twister.Core, 5f) * 0.35;
                    twistB = new Vector4(Mathf.Max(100f, twister.Base - (float)ground), twister.Reach, twister.Water ? 1f : 0f, (float)(turned % 6283.185307));
                    // (the clouds are laid on the picture no nearer than the nearest cloud can be: the funnel is cloud too)
                    double off = (camRel - foot.normalized * Vector3d.Dot(camRel, foot.normalized)).magnitude - (5.0 * twister.Core + 0.2 * twistB.x);
                    if (alt < twister.Base + 200.0) clear = Mathf.Min(clear, Mathf.Max(0f, (float)off - 30f));
                }
            }
            SheetClear = clear;

            // ---- the shadow's square
            bool shadows = CloudsPage.Shadows == null || CloudsPage.Shadows;
#if DEV
            if (Trial == 20f) shadows = false;      // (for finding a fault: the clouds' shadows left off)
#endif
            double side = Math.Max(50000.0, Math.Min(600000.0, alt * 3.0 + 50000.0));
            Vector3d groundFixed = camFixed.normalized * R;
            bool shadowAll = shadowMap == null || TimeWarp.CurrentRate > 8f;     // (the whole of the shadow's map this frame, not a quarter of it)
            if (shadowBody != body.bodyName || (groundFixed - shadowMid).magnitude > side / 8.0 || Math.Abs(side - shadowSide) > shadowSide * 0.25)
            {
                shadowAll = true;
                shadowBody = body.bodyName;
                shadowMid = groundFixed;
                shadowSide = side;
                Vector3d n = groundFixed.normalized;
                Vector3d a = Vector3d.Cross(n, new Vector3d(0, 0, 1));
                if (a.magnitude < 0.1) a = Vector3d.Cross(n, new Vector3d(1, 0, 0));
                a = a.normalized;
                shadowX = (Vector3)a;
                shadowY = (Vector3)Vector3d.Cross(n, a).normalized;
            }
            groundHeight = (float)Math.Max(0.0, Math.Min(alt, FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.terrainAltitude : 0.0));
            Vector3d shadowMidScene = fx * shadowMid.x + fy * shadowMid.y + fz * shadowMid.z;
            // (the sun dimmed by the cloud above the craft, eased: a cloud's edge passes over in a few seconds)
            sunDim = shadows ? Mathf.MoveTowards(sunDim, sunAbove, Time.unscaledDeltaTime * 0.8f) : 1f;
            SunThrough = sunDim;

            // ---- the map of the low layer's heaps (drawn in the far camera's drawing, before the shadow and the walk; read
            // by both cameras' walks and by the shadow's). On the face of the noise's cube that the camera is nearest.
            bool heapsWanted = Form > 0.5f && HeapsDots >= 64;
#if DEV
            if (Trial == 21f) heapsWanted = false;      // (for timing: the heaps worked out at every asking, as before the map)
#endif
            if (heapsMap != null && (!heapsWanted || heapsMap.width != HeapsDots)) FreeHeaps();
            if (heapsWanted && heapsMap == null)
            {
                RenderTexture Make(RenderTextureFormat f, string name)
                {
                    var t = new RenderTexture(HeapsDots, HeapsDots, 0, f, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, useMipMap = false, name = "Natural Weather: " + name };
                    t.Create();
                    return t;
                }
                heapsMap = Make(RenderTextureFormat.ARGBHalf, "the heaps' map");
                heapsMapB = Make(RenderTextureFormat.RHalf, "the heaps' map (2)");
            }
            heapsOn = heapsWanted && heapsMap != null && heapsMap.IsCreated() && heapsMapB.IsCreated();
            Vector3d upF = camFixed.normalized;
            double upX = Math.Abs(upF.x), upY = Math.Abs(upF.y), upZ = Math.Abs(upF.z);
            float heapsFace = upX >= upY && upX >= upZ ? 0f : upY >= upZ ? 1f : 2f;

            // ---- everything the shaders share
            void Common(Material m, Camera cam, float width)
            {
                float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
                bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
                m.SetVector(idSheet, new Vector4(1f, 1f, 0.5f, 1f));
                m.SetVector(idCam, new Vector4((float)camRel.x, (float)camRel.y, (float)camRel.z, (float)R));
                m.SetVector(idHigh, new Vector4((float)alt, air.Top, air.ScaleHeight, width));
                Vector3 r = cam.transform.right * tanX, u = cam.transform.up * tanY, f = cam.transform.forward;
                m.SetVector(idRight, new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
                m.SetVector(idUp, new Vector4(u.x, u.y, u.z, 0f));
                m.SetVector(idAhead, new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
                m.SetVector(idFixedX, (Vector3)fx); m.SetVector(idFixedY, (Vector3)fy); m.SetVector(idFixedZ, (Vector3)fz);
                m.SetVector(idNoise, noise);
                m.SetVector(idDrift, new Vector4(windDir.x, windDir.y, windDir.z, boil));
                m.SetVector(idStreets, new Vector4(streetsAcross.x, streetsAcross.y, streetsAcross.z, streetsStrength));
                m.SetVector(idAlong, new Vector4(streetsAlong.x, streetsAlong.y, streetsAlong.z, 0f));
                m.SetVector(idShell, new Vector4(shellBottom, shellTop, map.HighCloud, 1f));
                shellTopNow = shellTop;
                m.SetVector(idLook, new Vector4(thick, 0.85f, amount, steps));
                // (How the heaps come today. The field has heaps of every size, and all of them in the sky at once, hundreds of
                // small ones each by itself, is a sky that does come but is not the common one: on about one day in eight. On
                // the others the small heaps that stand alone are left out and a shallow layer's heaps are read half as large
                // again: fewer, larger, flatter. Which it is goes by the day (the planet's own), eased over some minutes.)
                {
                    uint dayNow = unchecked((uint)(long)Math.Floor(Planetarium.GetUniversalTime() / Math.Max(body.rotationPeriod, 3600.0)));
                    uint h = dayNow * 2654435761u + 40503u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                    float wanted = HeldHabit >= 0f ? HeldHabit : (h & 0xffff) / 65536f < 0.12f ? 0f : 1f;
                    habit = float.IsNaN(habit) ? wanted : Mathf.MoveTowards(habit, wanted, Time.unscaledDeltaTime / 300f);
                    // (and on the usual day the heaps that reach the layer's top spread out under it, and no heap is round)
                    m.SetVector("_CloudHabit", new Vector4(habit, Mathf.Lerp(1f, Broader, habit), LidSpread * habit, Mathf.Lerp(70f, Pushed, habit)));
                }
                m.SetVector(idSun, new Vector4(toSun.x, toSun.y, toSun.z, 1.0f * brightness));
                m.SetVector(idSunTint, new Vector4(1f, 0.985f, 0.96f, night));
                m.SetVector(idSky, new Vector4(skyLight.r, skyLight.g, skyLight.b, 1f));
                m.SetVector(idGround, new Vector4(groundLight.r, groundLight.g, groundLight.b, 120f));
                m.SetVector(idAir, new Vector4(air.Rayleigh.x, air.Rayleigh.y, air.Rayleigh.z, air.Haze));
                m.SetVector(idHaze, new Vector4(noiseHasMips ? 1f : 0f, 40f, 1.5f, 1f));
                m.SetVector(idFlashA, FlashVector(0)); m.SetVector(idFlashB, FlashVector(1));
                m.SetVector(idHoleA, HoleVector(0)); m.SetVector(idHoleB, HoleVector(1)); m.SetVector(idHoleC, HoleVector(2));
                m.SetVector(idFlashTint, new Vector4(0.75f, 0.8f, 1f, 900f));
                for (int i = 0; i < 6; i++) { m.SetVector(idCellA[i], cellA[i]); m.SetVector(idCellB[i], cellB[i]); }
                m.SetVector("_CloudTwistA", twistA); m.SetVector("_CloudTwistB", twistB);
                m.SetVector("_CloudShelfA", shelfA); m.SetVector("_CloudShelfB", shelfB); m.SetVector("_CloudShelfC", shelfC);
                m.SetVector("_CloudEyeA", eyeA); m.SetVector("_CloudEyeB", eyeB); m.SetVector("_CloudEyeC", eyeC); m.SetVector("_CloudEyeD", eyeD);
                m.SetVector(idSunFixed, ToFixed(toSun));
                m.SetVector(idShadowAt, new Vector4((float)shadowMid.x, (float)shadowMid.y, (float)shadowMid.z, (float)shadowSide));
                m.SetVector(idShadowX, new Vector4(shadowX.x, shadowX.y, shadowX.z, groundHeight));
                // (w: whether the walk may ask the shadow's map what lies towards the sun)
                m.SetVector(idShadowY, new Vector4(shadowY.x, shadowY.y, shadowY.z, shadows && shadowMapB != null && shadowMapB.IsCreated() ? 1f : 0f));
                m.SetTexture("_CloudShadowB", shadows && shadowMapB != null ? (Texture)shadowMapB : Texture2D.blackTexture);
                m.SetTexture(idShape, shape); m.SetTexture(idDetail, detail);
                // (the shapes of the rework of 2026-10-12: whether, and the time in lifetimes of a heap, kept within 1024 of
                // them, where the shader's numbering of each point's heaps comes round)
                CloudsWake.Hand(m, ut, noiseAt, NoiseWrap, CloudsPage.Stirred == null || CloudsPage.Stirred);
                if (cellsTable == null) MakeCellsTable();
                m.SetTexture("_CloudCells", cellsTable);
                m.SetTexture("_CloudHeaps", heapsOn ? (Texture)heapsMap : Texture2D.blackTexture);
                m.SetTexture("_CloudHeapsB", heapsOn ? (Texture)heapsMapB : Texture2D.blackTexture);
                // (From above all the clouds the map is spread wider, four times the camera's height across (a dot then some
                // hundreds of metres, a few of them to a heap: as fine as the picture is from there), so that the heaps of the
                // world below are read from it and not worked out at every asking: from orbit, worked out so, they cost six
                // milliseconds a frame more than the map does.)
                float heapsAcross = alt > shellTop + 500.0 ? Mathf.Clamp(4f * (float)alt, HeapsAcross, 800000f) : HeapsAcross;
                m.SetVector("_CloudHeapsAt", new Vector4(heapsOn ? 1f : 0f, heapsAcross / (float)NoiseLength, HeapsDots, heapsFace));
#if DEV
                m.SetVector("_CloudForm", new Vector4(Form, (float)Wrap(ut / Math.Max(HeapLife, 60f), 1024.0), Trial, FarOff ? 1f : 0f));
#else
                m.SetVector("_CloudForm", new Vector4(Form, (float)Wrap(ut / Math.Max(HeapLife, 60f), 1024.0), 0f, 0f));
#endif
                // (the heaps made beforehand: carried by the wind east and north, leaning with it by up to 260 m from base to top)
                bool made = field != null && (CloudsPage.Made == null || CloudsPage.Made);
                m.SetTexture(idField, made && FieldTest != 1f ? field : noField);
                m.SetVector(idFieldA, new Vector4(1f / fieldSide, 2f * fieldFar, (float)(-fieldDrift.x / fieldSide), (float)(-fieldDrift.y / fieldSide)));
                float leans = Mathf.Min((float)heapsWind.magnitude * 18f, 260f);
                Vector2 leanWay = heapsWind.magnitude > 0.01 ? new Vector2((float)(heapsWind.x / heapsWind.magnitude), (float)(heapsWind.y / heapsWind.magnitude)) : Vector2.zero;
                m.SetVector(idFieldB, new Vector4(made ? 1f : 0f, leanWay.x * leans, leanWay.y * leans, FieldLumps));
                m.SetVector(idFieldLevA, new Vector4(fieldLevels[0], fieldLevels[1], fieldLevels[2], fieldLevels[3]));
                m.SetVector(idFieldLevB, new Vector4(fieldLevels[4], fieldLevels[5], fieldLevels[6], fieldLevels[7]));
                m.SetVector(idFieldLevC, new Vector4(fieldLevels[8], fieldLevels[9], fieldLevels[10], fieldLevels[11]));
                m.SetVector(idFieldLevD, new Vector4(fieldLevels[12], fieldLevels[13], fieldLevels[14], fieldLevels[15]));
                m.SetTexture(idMap, mapFine != null && mapFine.IsCreated() ? (Texture)mapFine : map.A); m.SetTexture(idMapB, map.B); m.SetTexture(idMapC, map.C);
                m.SetTexture(idLut, air.Lut);
            }

            if (map.A != null)
            {
                if (mapFine == null)
                {
                    mapFine = new RenderTexture(map.A.width * 4, map.A.height * 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, name = "Natural Weather: the low clouds' map, smoothed" };
                    mapFine.Create();
                }
                if (mapFineWalk == null) mapFineWalk = new Material(walkShader) { name = "Natural Weather: the low clouds' map, smoothed" };
                Common(mapFineWalk, farCam, mapFine.width);
                mapFineWalk.SetVector(idStage, new Vector4(6f, 0f, 0f, 0f));
                mapFineWalk.SetTexture(idMap, map.A);
            }

            if (heapsOn)
            {
                if (heapsWalk == null) heapsWalk = new Material(walkShader) { name = "Natural Weather: the heaps' map" };
                Common(heapsWalk, farCam, HeapsDots);
                heapsWalk.SetVector(idStage, new Vector4(5f, 0f, 0f, 0f));
                // (what is drawn into is not also read)
                heapsWalk.SetTexture("_CloudHeaps", Texture2D.blackTexture);
                heapsWalk.SetTexture("_CloudHeapsB", Texture2D.blackTexture);
            }

            // ---- the shadow map (made in the far camera's drawing, used by both)
            if (shadows)
            {
                if (shadowMap == null)
                {
                    shadowMap = new RenderTexture(Shadow, Shadow, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the clouds' shadow" };
                    shadowMap.Create();
                    shadowMapB = new RenderTexture(Shadow, Shadow, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the clouds' shadow (2)" };
                    shadowMapB.Create();
                }
                if (shadowWalk == null) shadowWalk = new Material(walkShader) { name = "Natural Weather: shadow walk" };
                Common(shadowWalk, farCam, Shadow);
                shadowWalk.SetVector(idStage, new Vector4(4f, 0f, 0f, 0f));
                // (a quarter of the map each frame, one strip of four in turn: see the shader's go 4 for why not by `discard`)
                shadowStrip = shadowAll ? -1 : (frame & 3);
                // (what is drawn into is not also read)
                shadowWalk.SetTexture("_CloudShadowB", Texture2D.blackTexture);
            }

            // ---- each camera's clouds
            const float nearReach = 450f;
            bool nearWanted = split && alt > shellBottom - nearReach && alt < shellTop + nearReach;
            Setup(far, farCam, split ? farCam.nearClipPlane * 0.98f : 0f, 0f, quality, shadows, true, Common, body, camFixed, ToFixed, shadowMidScene, sunUp, clear, first);
            if (nearWanted) Setup(near, nearCam, 0f, nearCam.farClipPlane * 1.02f, quality, shadows, false, Common, body, camFixed, ToFixed, shadowMidScene, sunUp, clear, false);
            else if (near.On) Detach(near);
            Laying(farCam, nearCam);
            Drawing = true;
            Report = "Clouds from " + CloudSource.Name + ": " + (near.On ? "near and far" : "far") + (map.HighestTop > 0f ? ", " + Mathf.RoundToInt(map.LowestBase) + " to " + Mathf.RoundToInt(map.HighestTop) + " m" : "") +
                     (cells.Count > 0 ? ", " + cells.Count + " storm" + (cells.Count > 1 ? "s" : "") + " near" : "") + (sunDim < 0.98f ? "; the craft in their shade (" + Mathf.RoundToInt(100f * sunDim) + "% of the sun)" : "") +
                     (CloudsEve.Count > 0 ? "; EVE's clouds hidden" : "");
        }

        void FreeHeaps()
        {
            if (heapsMap != null) { heapsMap.Release(); Destroy(heapsMap); heapsMap = null; }
            if (heapsMapB != null) { heapsMapB.Release(); Destroy(heapsMapB); heapsMapB = null; }
        }

        static double Distance(CelestialBody body, WeatherCell c, Vector3d camRel) => (SceneAt(body, c.Latitude, c.Longitude, 0.0) - camRel).sqrMagnitude;

        static double Wrap(double v, double length) { v %= length; return v < 0.0 ? v + length : v; }

        delegate void CommonValues(Material m, Camera cam, float width);
        delegate Vector3 Turn(Vector3 v);

        void Setup(Pass p, Camera cam, float from, float to, int quality, bool shadows, bool makesShadow, CommonValues common, CelestialBody body, Vector3d camFixed, Turn toFixed, Vector3d shadowMidScene, float sunUp, float clear, bool first)
        {
            if (p.Cam != cam) { Detach(p); p.HavePrev = false; }
            int wide = cam.pixelWidth, high = cam.pixelHeight;
            if (wide < 64 || high < 64) return;
            // (half the picture's size each way, a third for "lower"; the near camera's clouds, soft mist within 400 m, a third always
            // unless "higher")
            int div = quality == 0 ? 3 : quality == 2 ? 2 : makesShadow ? 2 : 3;
            int w = Mathf.Max(16, wide / div), h = Mathf.Max(16, high / div);
            // (What is kept from frame to frame is the picture's own size where the walk is done at half of it: each frame's
            // lines of sight go through a different one of the four pixels each of the walk's dots stands for, and over
            // four frames the kept picture is as fine as the screen. See the shader, where the frames are taken together.)
            bool finer = Finer && div == 2;
#if DEV
            if (Trial == 22f) finer = false;        // (for comparing: what is kept, at the walk's own size, as before)
#endif
            int keptW = finer ? w * 2 : w, keptH = finer ? h * 2 : h;
            if (p.Now == null || p.Now.width != w || p.Now.height != h || p.HistA == null || p.HistA.width != keptW || p.HistA.height != keptH)
            {
                foreach (RenderTexture t in new[] { p.Now, p.NowD, p.HistA, p.HistDA, p.HistB, p.HistDB }) if (t != null) { t.Release(); Destroy(t); }
                RenderTexture Make(RenderTextureFormat f, string name, int across, int up)
                {
                    var t = new RenderTexture(across, up, 0, f, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: " + p.Name + " clouds, " + name };
                    t.Create();
                    return t;
                }
                p.Now = Make(RenderTextureFormat.ARGBHalf, "this frame", w, h); p.NowD = Make(RenderTextureFormat.RHalf, "this frame's distance", w, h);
                p.HistA = Make(RenderTextureFormat.ARGBHalf, "taken together", keptW, keptH); p.HistDA = Make(RenderTextureFormat.RHalf, "distance taken together", keptW, keptH);
                p.HistB = Make(RenderTextureFormat.ARGBHalf, "taken together (2)", keptW, keptH); p.HistDB = Make(RenderTextureFormat.RHalf, "distance taken together (2)", keptW, keptH);
                p.HavePrev = false;
            }
            if (p.Walk == null)
            {
                p.Walk = new Material(walkShader) { name = "Natural Weather: walk" };
                p.Together = new Material(walkShader) { name = "Natural Weather: over frames" };
                p.Over = new Material(overShader) { name = "Natural Weather: laid on", renderQueue = LaidQueue };
                p.Shadow = new Material(shadeShader) { name = "Natural Weather: shadow laid on" };
                p.Draw = new CommandBuffer { name = "Natural Weather: clouds (" + p.Name + ")" };
                p.After = new CommandBuffer { name = "Natural Weather: after the clouds (" + p.Name + ")" };
                p.Shade = new CommandBuffer { name = "Natural Weather: clouds' shadow (" + p.Name + ")" };
            }
            if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; p.AskedDepth = true; }

            common(p.Walk, cam, w);
            common(p.Together, cam, w);
            p.Walk.SetVector(idStage, new Vector4(2f, frame % 64, from, to));
            // (the near camera's stretch is short, and flown through: finer steps)
            p.Walk.SetVector(idHaze, new Vector4(noiseHasMips ? 1f : 0f, makesShadow ? 40f : 15f, 1.5f, 1f));
            p.Together.SetVector(idStage, new Vector4(3f, frame % 64, from, to));
            // (which of the four pixels this frame's lines go through: the two on one diagonal, then the two on the other)
            int turn = frame & 3;
            var jitter = finer ? new Vector4((turn == 0 || turn == 3 ? -0.25f : 0.25f) / w, (turn == 0 || turn == 2 ? -0.25f : 0.25f) / h, 1f, 0f) : Vector4.zero;
            p.Walk.SetVector("_CloudJitter", jitter);
            p.Together.SetVector("_CloudJitter", jitter);

            // where this camera was last frame (in the planet's axes), for taking the frames together
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            bool usable = p.HavePrev && p.PrevBody == body.bodyName && (camFixed - p.PrevAt).magnitude < 5000.0;
            Vector3d moved = camFixed - p.PrevAt;
            p.Together.SetVector(idPrevAt, new Vector4((float)moved.x, (float)moved.y, (float)moved.z, usable ? 1f : 0f));
            p.Together.SetVector(idPrevRight, new Vector4(p.PrevRight.x, p.PrevRight.y, p.PrevRight.z, 0.14f));
            p.Together.SetVector(idPrevUp, p.PrevUp);
            p.Together.SetVector(idPrevAhead, p.PrevAhead);
            p.PrevAt = camFixed;
            p.PrevRight = toFixed(cam.transform.right) / tanX;
            p.PrevUp = toFixed(cam.transform.up) / tanY;
            p.PrevAhead = toFixed(cam.transform.forward);
            p.PrevBody = body.bodyName;
            p.HavePrev = true;

            RenderTexture histIn = p.Flip ? p.HistB : p.HistA, histDIn = p.Flip ? p.HistDB : p.HistDA;
            RenderTexture histOut = p.Flip ? p.HistA : p.HistB, histDOut = p.Flip ? p.HistDA : p.HistDB;
            p.Flip = !p.Flip;
            p.Together.SetTexture(idNow, p.Now); p.Together.SetTexture(idNowD, p.NowD);
            p.Together.SetTexture(idHist, histIn); p.Together.SetTexture(idHistD, histDIn);

            // laid on the picture
            bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
            Vector3 r = cam.transform.right * tanX, u = cam.transform.up * tanY, f = cam.transform.forward;
            Material over = p.Over;
            // (The sheet the clouds are laid on with stands as far from the camera as the nearest cloud can be, and whatever
            // is drawn nearer than that hides it. What is in front of a cloud the clouds know from the camera's picture of
            // distances, and not everything is in that: the sea of Natural Ocean is not. On Direct3D, where one camera draws
            // the whole scene, the clouds were laid over that sea's nearest waves when the camera was low among them, and over
            // the underside of its surface when the camera was under water (seen 2026-10-11). On OpenGL the near camera
            // draws its stretch of sea after the far camera's clouds, and covered them. The sheet's depth as the graphics
            // card counts it, which differs from system to system: taken from the camera's own projection.)
            float sheetOff = Mathf.Clamp(clear * 0.9f, cam.nearClipPlane * 1.02f, cam.farClipPlane * 0.98f);
            Matrix4x4 lens = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
            over.SetVector(idSheet, new Vector4(1f, 1f, (lens.m22 * -sheetOff + lens.m23) / (lens.m32 * -sheetOff + lens.m33), 1f));
            over.SetVector(idRight, new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
            over.SetVector(idUp, new Vector4(u.x, u.y, u.z, 0f));
            over.SetVector(idAhead, new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
            over.SetVector(idOver, new Vector4(1f / 2.2f, 1f, 1f, 0f));
            over.SetTexture(idHist, histOut); over.SetTexture(idHistD, histDOut);

            p.Draw.Clear();
            p.Draw.SetRenderTarget(new RenderTargetIdentifier[] { p.Now, p.NowD }, p.Now);
            p.Draw.DrawMesh(sheet, Matrix4x4.identity, p.Walk, 0, 0);
            p.Draw.SetRenderTarget(new RenderTargetIdentifier[] { histOut, histDOut }, histOut);
            p.Draw.DrawMesh(sheet, Matrix4x4.identity, p.Together, 0, 0);
            p.Draw.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            // (The clouds are laid on in their turn among the see-through things (LaidQueue, by the renderer `laying`): after
            // Scatterer's sky, which is one of them and would be drawn over them, and before smoke, spray and flames, which
            // would otherwise have the clouds drawn over them however near: the user saw "the clouds showing above the
            // smoke" of a crash some hundreds of metres off. What lies behind a cloud is then drawn over it; so the picture of
            // the clouds and how far off each is are handed to every shader for this camera's drawing, and Volumetric
            // Explosions dims its smoke by the cloud in front of it (its layers.glsl). Taken back after everything.)
            bool late = LaidLate();
            if (late) { p.After.Clear(); p.After.DrawMesh(sheet, Matrix4x4.identity, over, 0, 0); }
            else p.After.Clear();
            p.Draw.SetGlobalTexture(idOthersPic, histOut);
            p.Draw.SetGlobalTexture(idOthersDist, histDOut);
            p.Draw.SetGlobalVector(idOthers, new Vector4(late ? 0f : 1f, clear, 0f, 0f));
            p.After.SetGlobalVector(idOthers, Vector4.zero);
            InFront?.Invoke(p.After, cam);

            // the shadow, before anything see-through is drawn
            p.Shade.Clear();
            // (the low clouds' map smoothed, first: everything after reads it)
            if (makesShadow && mapFine != null && mapFineWalk != null)
            {
                p.Shade.SetRenderTarget(mapFine);
                p.Shade.DrawMesh(sheet, Matrix4x4.identity, mapFineWalk, 0, 0);
                p.Shade.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            }
            if (makesShadow && heapsOn && heapsWalk != null)
            {
                p.Shade.SetRenderTarget(new RenderTargetIdentifier[] { heapsMap, heapsMapB }, heapsMap);
                p.Shade.DrawMesh(sheet, Matrix4x4.identity, heapsWalk, 0, 0);
                p.Shade.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            }
            if (shadows && shadowMap != null)
            {
                if (makesShadow)
                {
                    p.Shade.SetRenderTarget(new RenderTargetIdentifier[] { shadowMap, shadowMapB }, shadowMap);
                    if (shadowStrip >= 0) p.Shade.EnableScissorRect(new Rect(0f, shadowStrip * (Shadow / 4), Shadow, Shadow / 4));
                    p.Shade.DrawMesh(sheet, Matrix4x4.identity, shadowWalk, 0, 0);
                    if (shadowStrip >= 0) p.Shade.DisableScissorRect();
                    p.Shade.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                }
                Material s = p.Shadow;
                Vector3d camRelScene = (Vector3d)cam.transform.position - body.position;
                s.SetVector(idSheet, new Vector4(1f, 1f, 0.5f, 1f));
                s.SetVector(idRight, new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
                s.SetVector(idUp, new Vector4(u.x, u.y, u.z, 0f));
                s.SetVector(idAhead, new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
                s.SetVector(idCam, new Vector4((float)camRelScene.x, (float)camRelScene.y, (float)camRelScene.z, (float)body.Radius));
                s.SetVector(idSun, sunNow);
                s.SetVector(idShadeAt, new Vector4((float)shadowMidScene.x, (float)shadowMidScene.y, (float)shadowMidScene.z, (float)shadowSide));
                Vector3 sx = SceneOfFixed(body, shadowX), sy = SceneOfFixed(body, shadowY);
                s.SetVector(idShadeX, new Vector4(sx.x, sx.y, sx.z, groundHeight));
                s.SetVector(idShadeY, sy);
                // (how much brighter the sun makes what faces it than the sky alone does: less with the sun low, when the air has
                // dimmed it and the sky's share is larger)
                float sunOverSky = 1.25f * Mathf.Clamp01(sunUp * 2.5f + 0.15f);
                // (A far shadow fades into the light the craft is in, to half at 30 km: on and near the ground, ground further off
                // is under more air and the craft's own light is the better guess there.)
                // (From above all the clouds the walk lays each heap's shadow itself, per pixel (clouds.glsl, "the clouds' shadows
                // on the ground"): the square's is left off there, faded to nothing, so that nothing is shaded twice.)
                float camHigh = (float)(camRelScene.magnitude - body.Radius);
                s.SetVector(idShade, new Vector4(sunOverSky, camHigh > shellTopNow + 500f ? 1f : 30000f, makesShadow ? cam.nearClipPlane * 0.98f : 0f, makesShadow ? 1e9f : cam.farClipPlane * 1.02f));
                s.SetVector(idShadeSun, new Vector4(sunDim, body.ocean ? 1f : 0f, 0f, 0f));
                // (and wet ground is darker ground, as far as the wet is known round the camera: Wet.cs)
                s.SetVector("_CloudWet", new Vector4(Weather.WetGround ? Wet.Ground : 0f, 2500f, 0f, 0f));
                s.SetTexture(idShadowMap, shadowMap);
                p.Shade.DrawMesh(sheet, Matrix4x4.identity, s, 0, 0);
            }
            if (!p.On)
            {
                // (the shadow straight after the camera's solid things: Scatterer copies the picture after that and lays its haze on
                // from the copy before anything see-through is drawn, so a shadow laid on later is wiped out by it. Its sea, drawn
                // after the copy, takes no shadow so.)
                cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, p.Shade);
                p.When = CameraEvent.BeforeForwardAlpha;
                cam.AddCommandBuffer(p.When, p.Draw);
                cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, p.After);
                p.Cam = cam;
                p.On = true;
            }
        }

        /// <summary>
        /// Where among the see-through things the clouds are laid on: after Scatterer's sky (2998) and everything else that
        /// stands behind any cloud, before smoke and flames (3000 and on).
        /// </summary>
        const int LaidQueue = 2999;
        GameObject laying;
        Material layNothing;

        /// <summary>
        /// The one renderer that lays the clouds on in their turn: a sheet over the whole screen, whose drawing ignores where
        /// it stands (it is only kept where the camera is, so that no camera leaves it out). Just before each camera draws
        /// it, the material of that camera's clouds is put on it, or one that draws nothing (CloudsLaid).
        /// </summary>
        void Laying(Camera farCam, Camera nearCam)
        {
            if (laying == null)
            {
                laying = new GameObject("Natural Weather: clouds laid on");
                DontDestroyOnLoad(laying);
                laying.AddComponent<MeshFilter>().sharedMesh = sheet;
                var r = laying.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                // (kept at the camera, so moved every frame: it has no motion of its own for the game's smoothing to take)
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                layNothing = new Material(overShader) { name = "Natural Weather: laid on (nothing)", renderQueue = LaidQueue };
                layNothing.SetVector(idSheet, new Vector4(0f, 0f, 0f, 1f));
                r.sharedMaterial = layNothing;
                laying.AddComponent<CloudsLaid>().Owner = this;
            }
            // (on a layer both cameras draw)
            int both = farCam.cullingMask & (nearCam != null ? nearCam.cullingMask : ~0);
            int layer = (both & (1 << 15)) != 0 ? 15 : 0;
            if ((both & (1 << layer)) == 0) for (int l = 0; l < 32; l++) if ((both & (1 << l)) != 0) { layer = l; break; }
            laying.layer = layer;
            laying.transform.position = farCam.transform.position;
        }

        /// <summary>The material the sheet is drawn with for a camera just about to draw it (CloudsLaid).</summary>
        internal Material LaidFor(Camera cam)
        {
            if (cam == null || LaidLate()) return layNothing;
            if (far.On && cam == far.Cam) return far.Over;
            if (near.On && cam == near.Cam) return near.Over;
            return layNothing;
        }

        /// <summary>(Development build, for setting one against the other: the clouds laid on after everything, as until Natural Weather 0.4.0.)</summary>
        static bool LaidLate()
        {
#if DEV
            return HeldFirst > 0.5f;
#else
            return false;
#endif
        }

        // ---------------------------------------------------------------- for tests (development build)
#if DEV
        /// <summary>(Development build: hold one kind of weather everywhere in the stand-in, which the clouds then take in place of any weather mod's: clear, fair, cumulus, towering, storm, layer, middle, overcast, cirrus; or "as it comes".)</summary>
        public static string Try(string kind)
        {
            CloudStandIn.Held = kind == "as it comes" || kind == "" ? null : kind;
            if (instance != null) instance.map.Forget();
            return "weather held: " + (CloudStandIn.Held ?? "as it comes") + (Keystone.Weather.There && CloudStandIn.Held != null ? " (in place of " + Keystone.Weather.Provider.Name + "'s, for the clouds only)" : "") + " (the map is made again within two seconds)";
        }

        /// <summary>(Development build: a flash of lightning in the nearest storm, or 2 km in front of the camera at 3 km up.)</summary>
        public static string Flash()
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = Find("Camera 01") ?? Find("Camera 00");
            if (body == null || cam == null) return "no camera";
            Vector3d at = cam.transform.position + cam.transform.forward * 4000f;
            body.GetLatLonAlt(at, out double lat, out double lon, out double _);
            var f = new WeatherFlash { Body = body, Latitude = lat, Longitude = lon, Bottom = 1000f, Top = 5000f, Ground = false, Time = Planetarium.GetUniversalTime(), Brightness = 2f, Strokes = 4, Seed = UnityEngine.Random.Range(0, 99999) };
            Keystone.Weather.Flash(f);
            return "flash at " + lat.ToString("F3") + ", " + lon.ToString("F3");
        }

        static float heldFlash;
        static Vector3d heldFlashAt;
        /// <summary>(Development build: a flash held lit, at so much brightness (0: none), 4 km in front of the camera and 3 km up, for pictures: a real one is gone in a tenth of a second.)</summary>
        public static string HoldFlash(string brightness)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = Find("Camera 01") ?? Find("Camera 00");
            if (body == null || cam == null) return "no camera";
            float.TryParse(brightness, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out heldFlash);
            Vector3d rel = (Vector3d)(cam.transform.position + cam.transform.forward * 4000f) - body.position;
            heldFlashAt = rel.normalized * (body.Radius + 3000.0);
            return heldFlash > 0f ? "flash held at " + heldFlash : "no flash held";
        }

        static bool heldOff;
        /// <summary>(Development build: the clouds off, or on again, without touching the player's settings: for timing.)</summary>
        public static string Switch() { heldOff = !heldOff; return heldOff ? "clouds held off" : "clouds on"; }

        /// <summary>(Development build: the clouds laid on before the see-through things ("1"), after them ("0"), or as it comes ("-1").)</summary>
        public static string First(string v) { HeldFirst = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return HeldFirst > 0.5f ? "laid on after everything, as until 0.4.0" : "laid on in their turn among the see-through things (after Scatterer's sky, before smoke)"; }
        static float HeldFirst = -1f;

        public static string Stats() => Report + (CloudsWake.Said.Length > 0 ? "; " + CloudsWake.Said : "") + "; laid on no nearer than " + SheetClear.ToString("F0") + " m, " + (LaidFirst ? "before" : "after") + " the see-through things; noise " + (shape != null ? shape.width + (noiseHasMips ? " with mips" : " no mips") : "none") + "; the map took " + (instance != null ? instance.map.Took.ToString("F2") : "?") + " s; the heaps' map " + (instance != null && instance.heapsOn ? HeapsDots + " dots, " + (HeapsAcross / 1000f).ToString("F1") + " km" : "off") + "; " + why
            + (instance != null ? "; at " + utWas.ToString("F1") + " s the wind has carried the noise " + (driftWas.x / 1000.0).ToString("F3") + ", " + (driftWas.y / 1000.0).ToString("F3") + ", " + (driftWas.z / 1000.0).ToString("F3") + " km (the wind " + windWas.x.ToString("F2") + ", " + windWas.y.ToString("F2") + ", " + windWas.z.ToString("F2") + " m/s)" + (heldStill ? ", held still" : "")
                + "; the camera's longitude " + lonGame.ToString("F2") + " by the game, " + lonOwn.ToString("F2") + " by the clouds' axes (apart by " + Apart(lonOwn, lonGame).ToString("F2") + "; the scene stands turned " + turnWas.ToString("F2") + " against the planet)" : "");

        /// <summary>(Development build: what the shader is handed of the heaps made beforehand.)</summary>
        public static string Field()
        {
            if (field == null) return "no field of heaps: " + fieldWhy;
            var b = new System.Text.StringBuilder();
            b.Append(field.width).Append(" by ").Append(field.height).Append(" by ").Append(field.depth).Append(", ").Append(field.mipmapCount).Append(" sizes, ").Append(field.format).Append(", ").Append(field.graphicsFormat)
             .Append("; side ").Append(fieldSide).Append(" m, far ").Append(fieldFar).Append(" m; in use: ").Append(CloudsPage.Made == null || CloudsPage.Made).Append("; levels");
            foreach (float l in fieldLevels) b.Append(' ').Append(l.ToString("F3"));
            if (instance != null) b.Append("; carried ").Append(instance.fieldDrift.x.ToString("F0")).Append(" m east, ").Append(instance.fieldDrift.y.ToString("F0")).Append(" north");
            return b.ToString();
        }

        static bool heldStill;
        static Vector3d driftWas, windWas;
        static double utWas, lonGame, lonOwn, turnWas;
        static double Apart(double a, double b) { double d = (a - b) % 360.0; if (d > 180.0) d -= 360.0; if (d < -180.0) d += 360.0; return d; }
        /// <summary>(Development build: "on" holds the clouds' noise where it is, not carried by the wind, for pictures of before and after within one flight; "off" lets the wind carry it again. It does not make two loads the same sky: see the longitudes in Stats.)</summary>
        public static string Still(string arg) { heldStill = arg != null && arg.Trim() == "on"; return heldStill ? "the clouds' noise held still" : "the clouds' noise carried by the wind"; }

        static Clouds instance;
        void Start() { instance = this; }
#endif
    }
}
