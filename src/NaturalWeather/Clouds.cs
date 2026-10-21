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
    /// <summary>The clouds' shaders made ready at the first main menu of a game (Clouds.Ahead), so that the first flight does not wait for them.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class CloudsAhead : MonoBehaviour
    {
        void Start() { try { Clouds.Ahead(); } catch (Exception) { } }
    }

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
        // (the walk's copies without the weather's rarer features, and the one this frame's sky allows: see Frame, "which copy")
        static Shader walkStorm, walkCalm;
        Shader walkNow;
        static bool warmed;
#if DEV
        static int CopyHeld; static string CopyNow = "";
        /// <summary>(Development build: "whole", "storm" or "as the sky has it": which copy of the walk draws the clouds; says which does now.)</summary>
        public static string Copy(string which) { string w = (which ?? "").Trim(); CopyHeld = w == "whole" ? 1 : w == "storm" ? 2 : 0; return "the walk drawn by " + (CopyHeld == 1 ? "the whole shader" : CopyHeld == 2 ? "the storm's copy" : "the copy the sky allows") + " (last frame: " + CopyNow + ")"; }
#endif

        /// <summary>
        /// Each copy of the walk drawn once, into a picture of one pixel, when the clouds are first drawn: OpenGL makes a shader
        /// ready for the card only when something is first drawn with it, which for one of this size holds the game up for a
        /// moment. Done here, a storm coming into sight in flight (and the copy for it) does not.
        /// </summary>
        static void Warm()
        {
            if (warmed) return;
            warmed = true;
            RenderTexture pixel = null;
            try
            {
                pixel = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf);
                foreach (Shader copy in new[] { walkStorm, walkCalm })
                {
                    if (copy == null) continue;
                    var m = new Material(copy);
                    Graphics.Blit(null, pixel, m);
                    Destroy(m);
                }
            }
            catch (Exception) { }
            finally { if (pixel != null) RenderTexture.ReleaseTemporary(pixel); }
        }

        /// <summary>
        /// (2026-10-18) Every one of the clouds' shaders drawn once, into a picture of one pixel, at the main menu (CloudsAhead):
        /// this Mac's OpenGL compiles and links a program when it is first drawn with (the rendering chat's lab: 78 ms for the walk
        /// natively, 145 under Rosetta), and the walk has three copies besides the laying and the shadow. About half a second, once
        /// a game, that held up the first cloudy frame of the first flight (the climb's read-out: "each camera" 547 ms at worst).
        /// The shaders are kept for the whole game, and their programs with them.
        /// </summary>
        public static void Ahead()
        {
            if (warmed || (Weather.On != null && !Weather.On) || (CloudsPage.On != null && !CloudsPage.On)) return;
            if (!tried) LoadShaders();
            if (walkShader == null) return;
            warmed = true;
            RenderTexture pixel = null;
            var took = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                pixel = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf);
                foreach (Shader one in new[] { walkShader, walkStorm, walkCalm, overShader, shadeShader })
                {
                    if (one == null) continue;
                    var m = new Material(one);
                    Graphics.Blit(null, pixel, m);
                    Destroy(m);
                }
            }
            catch (Exception) { }
            finally { if (pixel != null) RenderTexture.ReleaseTemporary(pixel); }
            Debug.Log("[Natural Weather] clouds: their shaders made ready ahead, in " + took.ElapsedMilliseconds + " ms");
        }
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
        /// asking, which costs a quarter of the frames among them), and how many metres it is across at the least (from above all
        /// the clouds it is wider: see HeapsWidth). It comes round, so it must be wider than twice as far as heaps are drawn (26 km).
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
#if DEV
        /// <summary>(Testing: the walk's steps times this; for the grey band at cloud height, whether the walk runs out of steps.)</summary>
        public static float StepsTimes = 1f;
        public static string Steps(string v) { StepsTimes = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return "steps x" + StepsTimes; }
#endif
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

        // (in methods of their own: a Keystone from before 2026-10-18 has no FarMotion, and it is the method that names it that fails;
        // with one, the clouds go on as before, and the smoothing between frames follows the ground under them)
        static bool farMotionMissing;
        static void TellFarMotion(Texture picture, Texture distance)
        {
            if (farMotionMissing) return;
            try { FarMotionClouds(picture, distance); } catch (Exception) { farMotionMissing = true; }
        }
        static void FarMotionClouds(Texture picture, Texture distance) => Keystone.FarMotion.Clouds(picture, distance);

        /// <summary>One camera's clouds.</summary>
        sealed class Pass
        {
            public string Name;
            public Camera Cam;
            public CommandBuffer Draw, Shade;
            /// <summary>
            /// Once the camera's depth picture is made and before it draws its own picture: the far camera's maps (the low
            /// clouds' map smoothed, the heaps' map, the shadow's strip, the light field's band) and the walk and its taking
            /// together, each into a picture of its own. (Until the evening of 2026-10-18 the maps were drawn after the camera's
            /// solid things and the walk before its see-through ones: twice in the middle of the far camera's own picture, which
            /// was put away and taken up again round each, a whole picture and its depths each way. The main chat's measure of
            /// the pad frame: bound by the render thread, four tenths of it spent beginning such passes.)
            /// </summary>
            public CommandBuffer Early;
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
        // ---- the sun-side light field (2026-10-18; research/rendering/experiments/cloud-light-field.md). For cells round the
        // camera (three levels of 128 x 128, the finest 75 m, each next four times as wide; 32 heights through the shell), how much
        // of the sun's light comes through the cloud between each cell and the sun (the shader's go 9). The walk reads it after
        // its own first step towards the sun, in place of its other steps that way, its far looks and the shadow's second map:
        // closer to the truth, and 10-31 % less of the walk, in the lab. Two pictures: the walk reads one made whole during the
        // last round of 64 frames while the next is made, a 64th a frame (a band of rows, in the far camera's pass before its
        // own picture: Pass.Early), round where the camera will be while it is read, fixed to the planet for the round.
        public static bool LightField = true;
#if DEV
        /// <summary>(Development build, for timing the field against walking to the sun: "off" walks, anything else uses the field.)</summary>
        public static string Field(string on) { LightField = on == null || on.Trim() != "off"; return "light field " + (LightField ? "on" : "off"); }
        /// <summary>
        /// (Development build, for setting the rendering chat's lab up as the game had it: the far camera's walk's values as last set,
        /// by name, "a,b,c"; with none named, the sun's, the eye's and twist's, the camera's and the shell's.)
        /// </summary>
        public static string Uniforms(string names)
        {
            Material m = instance != null ? instance.far.Walk : null;
            if (m == null) return "no far walk yet";
            string[] want = string.IsNullOrEmpty(names) || names.Trim().Length == 0
                ? new[] { "_CloudSun", "_CloudSunTint", "_CloudSunFixed", "_CloudEyeA", "_CloudEyeB", "_CloudEyeC", "_CloudEyeD", "_CloudTwistA", "_CloudTwistB", "_CloudCam", "_CloudHigh", "_CloudShell", "_CloudShellEased", "_CloudForm", "_CloudGround", "_CloudAir", "_CloudAirHigh", "_CloudLightF", "_CloudLightG", "_CloudJitter" }
                : names.Split(',');
            var b = new System.Text.StringBuilder("the far walk (" + (m.shader != null ? m.shader.name : "?") + "):");
            foreach (string one in want)
            {
                string n = one.Trim();
                if (n.Length > 0) b.Append("\n  ").Append(n).Append(" = ").Append(m.GetVector(n).ToString("G7"));
            }
            return b.ToString();
        }
#endif
        const int LightCells = 128, LightLevels = 3, LightHeights = 32, LightRound = 64;
        const float LightCell = 75f;
        RenderTexture lightRead, lightMaking;
        Material lightWalk;
        int lightBand = -1;                 // the band of rows of the field being made that is drawn this frame (less than nothing: none begun)
        bool lightReady;                    // whether lightRead holds a whole field
        bool lightHigh;                     // whether the camera is far above the clouds, where the field is neither made nor read
        string lightBody;
        Vector3d lightMid, lightEast, lightNorth, readMid, readEast, readNorth;   // (where each lies: the planet's own axes)
        float lightLow, lightStep, readLow, readStep;                            // (its heights: the lowest, m above the radius, and how far apart)
        Vector3d lightCamWas, lightGoes;    // (where the camera was last frame, the planet's axes; and how fast it goes over the planet, m/s, smoothed)
        bool lightCamKnown;
        static readonly int idLight = Shader.PropertyToID("_CloudLight"), idLightF = Shader.PropertyToID("_CloudLightF"), idLightG = Shader.PropertyToID("_CloudLightG"),
            idLightA = Shader.PropertyToID("_CloudLightA"), idLightE = Shader.PropertyToID("_CloudLightE"), idLightN = Shader.PropertyToID("_CloudLightN");

        /// <summary>
        /// How wide the heaps' map is laid this frame, m: at least as wide as asked, and its least width times a power of two.
        /// Laid at just four times the camera's height, as it was until 2026-10-18, its dots moved over the heaps in every frame
        /// of a climb, and from 13 km up, where the walk still reads the map, a few hundred pixels at the heaps' edges changed
        /// by up to a third of white from one frame to the next: the heaps below flickered while the camera rose (the owner,
        /// 2026-10-18: "the clouds flicker a little bit" going up) and were still when it held its height. Laid in powers of two
        /// its dots stay where they are on the heaps between doublings, and at a doubling the picture changes once, by about
        /// what three of those frames did (research/rendering/experiments/heaps-map-width.md). Halved again only a tenth below
        /// where the half would do, so that a camera holding its height near a doubling does not go to and fro between two.
        /// </summary>
        float HeapsWidth(double wanted)
        {
            if (heapsLeast != HeapsAcross || heapsWide < HeapsAcross) { heapsLeast = HeapsAcross; heapsWide = HeapsAcross; }
            while (heapsWide < wanted && heapsWide < 800000f) heapsWide *= 2f;
            while (heapsWide > HeapsAcross && wanted < 0.45 * heapsWide) heapsWide *= 0.5f;
            return heapsWide;
        }

        static RenderTexture MakeLight(string what)
        {
            var t = new RenderTexture(4 * LightCells, LightHeights / 4 * LightCells * LightLevels, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the light field (" + what + ")" };
            t.Create();
            return t;
        }

        /// <summary>A field's place and heights handed to a material: its middle and axes from the planet's own axes into the scene's.</summary>
        static void SetLight(Material m, Vector3d mid, Vector3d east, Vector3d north, float low, float step, float on, Vector3d fx, Vector3d fy, Vector3d fz)
        {
            Vector3 Scene(Vector3d v) => (Vector3)(fx * v.x + fy * v.y + fz * v.z);
            // (Sixteen readings beyond the first 739 m towards the sun, not eight. Under a second layer far up that way (an overcast's
            // stratus under its altostratus) eight gave it two readings, each cell its own two at its own places, and seen from
            // above the deck the shade between the upper layer's lumps was a checker of the field's 75 m cells (2026-10-19). The
            // field is a third dearer to make, about 0.07 ms a frame.)
#if DEV
            m.SetVector(idLightF, new Vector4(LightCells, LightLevels, LightHeights, TestFieldFar));
#else
            m.SetVector(idLightF, new Vector4(LightCells, LightLevels, LightHeights, 16f));
#endif
            m.SetVector(idLightG, new Vector4(LightCell, low, step, on));
            m.SetVector(idLightA, Scene(mid)); m.SetVector(idLightE, Scene(east)); m.SetVector(idLightN, Scene(north));
        }
        // (the low clouds' map, drawn smoothly once a frame at four times the weather's own size, which the walk reads: the
        // shader's stage 6. Read at the weather's own size, the edges of the clouds' regions seen from orbit were staircases.)
        RenderTexture mapFine;
        Material mapFineWalk;
        bool heapsOn;
        float heapsWide, heapsLeast;    // (the heaps' map's width as it is laid now, and the least width it was laid from: see HeapsWidth)
        float shellTopNow = 12000f;     // (the shell's top as last handed to the walk, m: the shadow's square is left off above it)
        float easedBottom = float.NaN, easedTop;        // (the shell's floor and top as eased: see Frame)
        string easedBody;
        static readonly int idShellEased = Shader.PropertyToID("_CloudShellEased");
        readonly CloudMap map = new CloudMap();
        readonly List<WeatherCell> cells = new List<WeatherCell>();
        float cellsAt = -100f;
        double cellsUt0, cellsUt1 = double.NegativeInfinity;
        readonly Dictionary<int, WeatherCell> nextById = new Dictionary<int, WeatherCell>();

        /// <summary>A storm as it stands at the moment ut, between how it was asked for now and one step on (see Frame).</summary>
        WeatherCell Between(WeatherCell c, double ut)
        {
            if (!nextById.TryGetValue(c.Id, out WeatherCell n) || cellsUt1 <= cellsUt0) return c;
            float f = Mathf.Clamp01((float)((ut - cellsUt0) / (cellsUt1 - cellsUt0)));
            double dLon = n.Longitude - c.Longitude;
            if (dLon > 180.0) dLon -= 360.0; else if (dLon < -180.0) dLon += 360.0;
            c.Latitude += (n.Latitude - c.Latitude) * f; c.Longitude += dLon * f;
            c.Radius = Mathf.Lerp(c.Radius, n.Radius, f); c.Base = Mathf.Lerp(c.Base, n.Base, f); c.Top = Mathf.Lerp(c.Top, n.Top, f);
            c.Strength = Mathf.Lerp(c.Strength, n.Strength, f); c.Rain = Mathf.Lerp(c.Rain, n.Rain, f); c.Life = Mathf.Lerp(c.Life, n.Life, f);
            return c;
        }
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
        Vector4 shadeTint = new Vector4(1f, 1f, 1f, 1f);     // (the sun's light over the sky's in each colour, against green's: see Frame)
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
            idLook = Shader.PropertyToID("_CloudLook"), idSun = Shader.PropertyToID("_CloudSun"), idSunTint = Shader.PropertyToID("_CloudSunTint"), idMoon = Shader.PropertyToID("_CloudMoon"),
            idSky = Shader.PropertyToID("_CloudSky"), idGround = Shader.PropertyToID("_CloudGround"), idAir = Shader.PropertyToID("_CloudAir"), idAirHigh = Shader.PropertyToID("_CloudAirHigh"),
            idHaze = Shader.PropertyToID("_CloudHaze"), idFlashA = Shader.PropertyToID("_CloudFlashA"), idFlashB = Shader.PropertyToID("_CloudFlashB"), idHoleA = Shader.PropertyToID("_CloudHoleA"), idHoleB = Shader.PropertyToID("_CloudHoleB"), idHoleC = Shader.PropertyToID("_CloudHoleC"),
            idField = Shader.PropertyToID("_CloudField"), idFieldA = Shader.PropertyToID("_CloudFieldA"), idFieldB = Shader.PropertyToID("_CloudFieldB"),
            idFieldLevA = Shader.PropertyToID("_CloudFieldLevA"), idFieldLevB = Shader.PropertyToID("_CloudFieldLevB"), idFieldLevC = Shader.PropertyToID("_CloudFieldLevC"), idFieldLevD = Shader.PropertyToID("_CloudFieldLevD"),
            idFlashTint = Shader.PropertyToID("_CloudFlashTint"), idPrevAt = Shader.PropertyToID("_CloudPrevAt"), idPrevRight = Shader.PropertyToID("_CloudPrevRight"),
            idPrevUp = Shader.PropertyToID("_CloudPrevUp"), idPrevAhead = Shader.PropertyToID("_CloudPrevAhead"), idShadowAt = Shader.PropertyToID("_CloudShadowAt"),
            idShadowX = Shader.PropertyToID("_CloudShadowX"), idShadowY = Shader.PropertyToID("_CloudShadowY"), idSunFixed = Shader.PropertyToID("_CloudSunFixed"),
            idSheet = Shader.PropertyToID("_CloudSheet"), idOver = Shader.PropertyToID("_CloudOver"), idShadeAt = Shader.PropertyToID("_CloudShadeAt"),
            idShadeX = Shader.PropertyToID("_CloudShadeX"), idShadeY = Shader.PropertyToID("_CloudShadeY"), idShade = Shader.PropertyToID("_CloudShade"),
            idShadeSun = Shader.PropertyToID("_CloudShadeSun"), idShadeTint = Shader.PropertyToID("_CloudShadeTint"), idStreets = Shader.PropertyToID("_CloudStreets"), idAlong = Shader.PropertyToID("_CloudAlong"),
            idShape = Shader.PropertyToID("_CloudShape"), idDetail = Shader.PropertyToID("_CloudDetail"), idMap = Shader.PropertyToID("_CloudMap"),
            idMapB = Shader.PropertyToID("_CloudMapB"), idMapC = Shader.PropertyToID("_CloudMapC"), idLut = Shader.PropertyToID("_CloudAirLut"), idNow = Shader.PropertyToID("_CloudNow"),
            idNowD = Shader.PropertyToID("_CloudNowD"), idHist = Shader.PropertyToID("_CloudHist"), idHistD = Shader.PropertyToID("_CloudHistD"),
            idShadowMap = Shader.PropertyToID("_CloudShadowMap"),
            idWeatherShadowMap = Shader.PropertyToID("_WeatherShadowMap"), idWeatherShadowAt = Shader.PropertyToID("_WeatherShadowAt"), idWeatherShadowX = Shader.PropertyToID("_WeatherShadowX"),
            idWeatherShadowY = Shader.PropertyToID("_WeatherShadowY"), idWeatherShadowCentre = Shader.PropertyToID("_WeatherShadowCentre"), idWeatherShadowFade = Shader.PropertyToID("_WeatherShadowFade"),
            idWeatherShadowSun = Shader.PropertyToID("_WeatherShadowSun"),
            idOthers = Shader.PropertyToID("_WeatherClouds"), idOthersPic = Shader.PropertyToID("_WeatherCloudsPic"), idOthersDist = Shader.PropertyToID("_WeatherCloudsDist"),
            idFlareCloud = Shader.PropertyToID("scattererReconstructedCloud");
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
                    walkStorm = Pick(Load("cloudsstorm.bundle"), "NaturalWeather/CloudsStorm"); walkCalm = Pick(Load("cloudscalm.bundle"), "NaturalWeather/CloudsCalm");
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
                    walkStorm = Pick(built, "NaturalWeather/CloudsStorm"); walkCalm = Pick(built, "NaturalWeather/CloudsCalm");
                    if (walkShader == null || overShader == null || shadeShader == null)
                        why = built == null ? "there is no cloud shader for " + SystemInfo.graphicsDeviceType + " yet (only for OpenGL, on Macs and Linux)" : "the clouds' shaders for " + SystemInfo.graphicsDeviceType + " did not load";
                }
                if (walkShader == null || overShader == null || shadeShader == null) { walkShader = overShader = shadeShader = null; walkStorm = walkCalm = null; }
            }
            catch (Exception ex) { walkShader = overShader = shadeShader = null; walkStorm = walkCalm = null; why = "the clouds' shader did not load (" + ex.Message + ")"; }
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
            ShadowAloneOff();
            if (nearShadow != null) Destroy(nearShadow);
            if (nearShade != null) nearShade.Release();
            if (laying != null) Destroy(laying);
            if (layNothing != null) Destroy(layNothing);
            Shader.SetGlobalVector(idOthers, Vector4.zero);
            if (shadowMap != null) { shadowMap.Release(); Destroy(shadowMap); shadowMap = null; }
            if (shadowMapB != null) { shadowMapB.Release(); Destroy(shadowMapB); shadowMapB = null; }
            if (shadowWalk != null) Destroy(shadowWalk);
            FreeHeaps();
            if (heapsWalk != null) Destroy(heapsWalk);
            if (lightRead != null) { lightRead.Release(); Destroy(lightRead); lightRead = null; }
            if (lightMaking != null) { lightMaking.Release(); Destroy(lightMaking); lightMaking = null; }
            if (lightWalk != null) { Destroy(lightWalk); lightWalk = null; }
            lightReady = false; lightBand = -1; lightCamKnown = false;
            if (mapFine != null) { mapFine.Release(); Destroy(mapFine); mapFine = null; }
            if (mapFineWalk != null) Destroy(mapFineWalk);
            if (shadowRead != null) { shadowRead.Free(); shadowRead = null; }
            map.Release();
            CloudsEve.Show();
            Shader.SetGlobalTexture(idFlareCloud, Texture2D.whiteTexture);
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
                if (p.Early != null) p.Cam.RemoveCommandBuffer(CameraEvent.AfterDepthTexture, p.Early);
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
            if (p.Early != null) p.Early.Release();
            p.Draw = p.Shade = p.After = p.Early = null;
        }

        static Camera Find(string name)
        {
            Camera named = Names.CameraNamed(name); if (named != null) return named;
            return null;
        }

        void Off(string said, bool eveBack = true)
        {
            if (far.On || near.On) { Detach(far); Detach(near); }
            ShadowAloneOff();
            // (EVE's clouds back only where these will not be drawn for a while: switched off, hiding EVE's switched off, no shader,
            // another scene or world, the map view. Through a pause (the map getting ready after a load, no near camera) or with no
            // cloud within sight they stay hidden: brought back, EVE's stock clouds stood in a sky this weather has clear, the
            // research's finding of 2026-10-19.)
            if (eveBack) CloudsEve.Show();
            Shader.SetGlobalTexture(idFlareCloud, Texture2D.whiteTexture);
            Drawing = false; SunThrough = 1f; sunDim = 1f; sunAbove = 1f;
            Shader.SetGlobalVector(idWeatherShadowSun, Vector4.zero);
            Report = said;
#if DEV
            TraceOff(said);
#endif
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

        // (For the owner's twitch of 2026-10-19, "the clouds twitch a lot when the chats are loading up commands": the clouds
        // frame by frame. Trace("frames") begins; TraceRead() gives a line a frame: the wall clock, how long the frame was by the
        // real clock and by the game's, and what changed in it: a weather map handed over (how finely made, how long it took),
        // a light field swapped in (how far from the camera it was centred, how fast the camera was reckoned to go), the
        // shader's copy, the near camera's clouds, the storms, the shell as eased, the clouds off. Stall("ms") holds the game's
        // thread so long, once: a long frame made on purpose, as a save, a screenshot or a scene's loading makes one.)
        static readonly System.Text.StringBuilder trace = new System.Text.StringBuilder();
        static int traceLeft, traceMade = -1, traceCells = -1;
        static string traceCopy = "", traceOff = "";
        static bool traceNear, traceSwap;
        static float traceBottom = -1f, traceTop = -1f;
        public static string Trace(string frames)
        {
            int n = 600;
            if (!string.IsNullOrEmpty(frames)) int.TryParse(frames.Trim(), out n);
            trace.Length = 0; traceLeft = Mathf.Clamp(n, 1, 20000);
            traceMade = traceCells = -1; traceCopy = traceOff = ""; traceNear = traceSwap = false; traceBottom = traceTop = -1f;
            return "tracing the clouds for " + traceLeft + " frames";
        }
        public static string TraceRead() { traceLeft = 0; return trace.ToString(); }
        /// <summary>(Development build: the weather's map of the whole world as a PNG, longitude across and latitude up: white for the low
        /// cloud's cover, blue for the middle cloud's, red for rain, the land a dim green under them.)</summary>
        public static string MapPicture(string path)
        {
            if (instance == null || instance.map.A == null) return "no map yet";
            CloudMap m = instance.map;
            Color32[] a = m.A.GetPixels32(), b = m.B.GetPixels32(), c = m.C.GetPixels32();
            var picture = new Texture2D(CloudMap.Wide, CloudMap.High, TextureFormat.RGB24, false);
            var px = new Color32[a.Length];
            World w = World.Of(m.Body);
            for (int j = 0; j < CloudMap.High; j++)
                for (int i = 0; i < CloudMap.Wide; i++)
                {
                    int k = i + j * CloudMap.Wide;
                    double lat = (j + 0.5) / CloudMap.High * 180.0 - 90.0, lon = (i + 0.5) / CloudMap.Wide * 360.0 - 180.0;
                    float land = w != null ? w.Land(lat, lon) : 0f;
                    float low = a[k].r / 255f, mid = b[k].a / 255f, rain = b[k].r / 255f;
                    float cr = 0.05f + 0.1f * land, cg = 0.1f + 0.25f * land, cb = 0.3f - 0.15f * land;
                    cr += (1f - cr) * low; cg += (1f - cg) * low; cb += (1f - cb) * low;
                    cr = Mathf.Lerp(cr, 0.55f, 0.5f * mid * (1f - low)); cg = Mathf.Lerp(cg, 0.7f, 0.5f * mid * (1f - low)); cb = Mathf.Lerp(cb, 1f, 0.5f * mid * (1f - low));
                    cr = Mathf.Lerp(cr, 1f, 0.45f * rain); cg *= 1f - 0.35f * rain; cb *= 1f - 0.35f * rain;
                    px[k] = new Color32((byte)(255 * Mathf.Clamp01(cr)), (byte)(255 * Mathf.Clamp01(cg)), (byte)(255 * Mathf.Clamp01(cb)), 255);
                }
            picture.SetPixels32(px); picture.Apply(false);
            System.IO.File.WriteAllBytes(path, picture.EncodeToPNG());
            Destroy(picture);
            return "the map of " + m.Body.bodyName + " at " + m.MadeFor.ToString("F0") + " s, made one dot in " + m.Stride + ", written to " + path;
        }

        /// <summary>(Development build: the far camera's two kept pictures and this frame's walk, read back at once into PNGs in a folder.)</summary>
        public static string Kept(string folder)
        {
            if (instance == null || instance.far.HistA == null || instance.far.HistB == null || instance.far.Now == null) return "nothing kept";
            System.IO.Directory.CreateDirectory(folder);
            void Save(RenderTexture rt, string name)
            {
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                t.Apply(false);
                RenderTexture.active = was;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), t.EncodeToPNG());
                Destroy(t);
            }
            Save(instance.far.HistA, "histA"); Save(instance.far.HistB, "histB"); Save(instance.far.Now, "now");
            return "written to " + folder + "; the next frame writes " + (instance.far.Flip ? "A" : "B");
        }
        /// <summary>(Development build: how finely the weather's map is made: "1" or "2" (one dot in so many each way), "took" (by how long the last one took, as until 2026-10-19), anything else as released; the next map made at once.)</summary>
        public static string MapStride(string how)
        {
            string h = (how ?? "").Trim();
            CloudMap.HeldStride = h == "1" ? 1 : h == "2" ? 2 : h == "took" ? -1 : 0;
            if (instance != null) instance.map.Forget();
            return "the weather's map made " + (CloudMap.HeldStride > 0 ? "at one dot in " + CloudMap.HeldStride : CloudMap.HeldStride < 0 ? "by how long the last took" : "as released") + ", the next at once";
        }
        public static string Stall(string ms)
        {
            int n = 300;
            if (!string.IsNullOrEmpty(ms)) int.TryParse(ms.Trim(), out n);
            n = Mathf.Clamp(n, 1, 5000);
            System.Threading.Thread.Sleep(n);
            return "held the game's thread " + n + " ms";
        }
        static void TraceHead()
        {
            trace.Append(DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
                 .Append(' ').Append((Time.unscaledDeltaTime * 1000f).ToString("F1")).Append(' ').Append((Time.deltaTime * 1000f).ToString("F1"));
        }
        static void TraceOff(string said)
        {
            if (traceLeft <= 0) return;
            traceLeft--;
            TraceHead();
            if (said != traceOff) trace.Append(" | off: ").Append(said.Length > 0 ? said : "(not drawn here)");
            traceOff = said;
            trace.Append('\n');
        }
        static Vector3d traceCamAt; static Vector3 traceAhead;
        static int traceScans = -1;
        static string traceWho = ""; static int traceDrew01, traceDrew00, traceDrewOther, traceFrameWas = -1;
        static readonly System.Text.StringBuilder traceOrder = new System.Text.StringBuilder();     // (every camera that began to draw since the last line, in turn)
        void TraceFrame(float shellBottom, float shellTop, Vector3d camFixed, double R)
        {
            traceLeft--;
            TraceHead();
            // (how far the far camera moved over the planet since the last frame traced, and how far it turned; and which of
            // its two kept pictures is written this frame)
            if (far.Cam != null)
            {
                Vector3 ahead = far.Cam.transform.forward;
                trace.Append(" @ ").Append((camFixed - traceCamAt).magnitude.ToString("F3")).Append(" m ")
                     .Append((traceAhead.sqrMagnitude > 0.5f ? Vector3.Angle(traceAhead, ahead) : 0f).ToString("F3")).Append(" deg ")
                     .Append(far.Flip ? 'B' : 'A').Append(' ').Append(far.Cam.name).Append(far.HavePrev ? "" : " (nothing kept)");
                traceCamAt = camFixed; traceAhead = ahead;
            }
            // (who set this frame's clouds out, and how many times each camera began to draw since the last line; and whether this
            // is the same frame of the game as the last line: set out twice in one frame)
            trace.Append(" by ").Append(traceWho).Append(" drew ").Append(traceDrew01).Append('/').Append(traceDrew00).Append('/').Append(traceDrewOther)
                 .Append(Time.frameCount == traceFrameWas ? " SAME FRAME" : "").Append(" [").Append(traceOrder).Append(']');
            traceDrew01 = traceDrew00 = traceDrewOther = 0; traceFrameWas = Time.frameCount; traceOrder.Length = 0;
            if (traceOff.Length > 0 || trace.Length < 40) { trace.Append(" | drawn"); traceOff = ""; }
            if (map.Made != traceMade) { trace.Append(" | map ").Append(map.Made).Append(" one dot in ").Append(map.Stride).Append(", took ").Append(map.Took.ToString("F2")).Append(" s"); traceMade = map.Made; }
            if (traceSwap) { trace.Append(" | light field swapped in, centred ").Append(((readMid - camFixed.normalized).magnitude * R).ToString("F0")).Append(" m from the camera, which was reckoned at ").Append(lightGoes.magnitude.ToString("F0")).Append(" m/s"); traceSwap = false; }
            string copy = walkNow == walkShader ? "whole" : walkNow == walkStorm ? "storm" : "calm";
            if (copy != traceCopy) { trace.Append(" | copy ").Append(copy); traceCopy = copy; }
            if (near.On != traceNear) { trace.Append(" | near ").Append(near.On ? "on" : "off"); traceNear = near.On; }
            if (cells.Count != traceCells) { trace.Append(" | storms ").Append(cells.Count); traceCells = cells.Count; }
            if (CloudsEve.Scans != traceScans) { trace.Append(" | EVE looked for: ").Append(CloudsEve.NewlyHidden).Append(" more hidden, ").Append(CloudsEve.Count).Append(" in all"); traceScans = CloudsEve.Scans; }
            if (Mathf.Abs(easedBottom - traceBottom) > 5f || Mathf.Abs(easedTop - traceTop) > 5f)
            {
                trace.Append(" | shell eased ").Append(easedBottom.ToString("F0")).Append('-').Append(easedTop.ToString("F0")).Append(" m, wanted ").Append(shellBottom.ToString("F0")).Append('-').Append(shellTop.ToString("F0"));
                traceBottom = easedBottom; traceTop = easedTop;
            }
            trace.Append('\n');
        }
#endif

        void BeforeCamera(Camera cam)
        {
#if DEV
            if (cam != null) { if (Names.Is(cam, "Camera 01")) traceDrew01++; else if (Names.Is(cam, "Camera 00")) traceDrew00++; else traceDrewOther++; }
            if (cam != null && traceLeft > 0 && traceOrder.Length < 400) traceOrder.Append(traceOrder.Length > 0 ? ", " : "").Append(cam.name);
            if (due && cam != null && (Names.Is(cam, "Camera 01") || Names.Is(cam, "Camera 00"))) traceWho = cam.name;
#endif
            if (due && cam != null && (Names.Is(cam, "Camera 01") || Names.Is(cam, "Camera 00"))) { due = false; Frame(); }
#if DEV
            if (cam != null && cam == lateCam && Time.frameCount == lateFrame)
            {
                lagTurn = Mathf.Max(lagTurn, Quaternion.Angle(lateLooks, cam.transform.rotation));
                lagMove = Mathf.Max(lagMove, (cam.transform.position - lateAt).magnitude);
                lagFrames++;
            }
#endif
            if (cam == null || !Drawing || sunDim >= 0.999f || (!Names.Is(cam, "Camera 01") && !Names.Is(cam, "Camera 00"))) return;
            Light sun = Sun.Instance != null ? Sun.Instance.sunLight : null;
            if (sun == null) return;
            if (dimmed == null) { dimmed = sun; dimmedFrom = sun.intensity; sun.intensity = dimmedFrom * sunDim; }
        }

        void AfterCamera(Camera cam)
        {
            if (cam != null && Names.Is(cam, "Camera 00")) Undim();
        }

        void Undim()
        {
            if (dimmed != null && dimmedFrom >= 0f) dimmed.intensity = dimmedFrom;
            dimmed = null; dimmedFrom = -1f;
        }

#if DEV
        static bool skipRead;
        /// <summary>(For the development build: the shadow map's read-back each fourth frame skipped, and back: to measure what it costs the game's thread.)</summary>
        public static string SkipRead() { skipRead = !skipRead; return skipRead ? "the shadow's read-back skipped" : "the shadow read back every fourth frame"; }
        /// <summary>(Development build: what the shadow says lies above the craft, and how it was read.)</summary>
        public static string Above() => instance == null ? "no clouds" : "the sun through the cloud above the craft: " + instance.sunAbove.ToString("F3") + " (" + (instance.shadowRead != null ? instance.shadowRead.Way + ", the last answer " + instance.shadowRead.Frames + " frames late" : "not read") + "); the shell " + instance.easedBottom.ToString("F0") + " to " + instance.easedTop.ToString("F0") + " m as eased";
#endif

        // At the start of each frame: how much cloud the shadow says lies between the craft and the sun. The shadow's one pixel
        // under the craft is read back from the graphics card through Keystone's Readback: without the game waiting where it can
        // be (Unity's own asynchronous read, or on OpenGL Volumetric Explosions' library), the answer some frames late; where it
        // cannot, read at once every fourth frame, as before (that waits for the card to finish all it was given, the clouds'
        // own walk with it: measured on the Mac, 6 to 12 ms of the main thread every fourth frame).
        void Update()
        {
            Undim();
            if (!Drawing || shadowMap == null || !shadowMap.IsCreated()) return;
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (v == null || body == null || shadowSide <= 0.0) return;
            Vector3d p = v.CoMD - body.position;
            // (Whose light the scene is drawn in: the craft's, or the camera's while it is far from the craft, as Photo Studio's and
            // Cinema Camera's views can be. From 6 km over a rocket standing in a cloud's shade, every sunlit gap of the sea below
            // was drawn dull and without its glitter, in the rocket's shade (2026-10-18). Over 1.5 km off the camera's, back to the
            // craft's within 1 km; the dimming eases over either way.)
            Camera eye = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (eye != null)
            {
                Vector3d c = (Vector3d)eye.transform.position - body.position;
                double apart = (c - p).magnitude;
                if (apart > 1500.0) byCamera = true; else if (apart < 1000.0) byCamera = false;
                if (byCamera) p = c;
            }
            double h = p.magnitude - body.Radius;
            if (shadowRead == null) shadowRead = new Readback(1, 1, true);
            // (an answer that has come back: the shadow's column under where the craft was then, the craft's height now)
            if (shadowRead.Take(out object _))
            {
                Color m = shadowRead.Pixel();
                float lowest = m.g * 1000f, highest = m.b * 1000f;
                float above = m.r * Mathf.Clamp01((highest - (float)h) / Mathf.Max(highest - lowest, 60f));
                sunAbove = float.IsNaN(above) ? 1f : Mathf.Exp(-above);
            }
            if (!shadowRead.Later && (frame & 3) != 0) return;
            Vector3d up = p.normalized;
            double sunUp = Vector3d.Dot(up, (Vector3d)sunNow);
            if (sunUp < -0.03) { sunAbove = 1f; return; }
            Vector3d g = p - (Vector3d)sunNow * ((h - groundHeight) / Math.Max(sunUp, 0.06));
            Vector3d mid = SceneOf(body, shadowMid);
            Vector3d rel = g - mid;
            double sx = Vector3d.Dot(rel, (Vector3d)SceneOfFixed(body, shadowX)) / shadowSide + 0.5, sy = Vector3d.Dot(rel, (Vector3d)SceneOfFixed(body, shadowY)) / shadowSide + 0.5;
            if (sx <= 0.0 || sx >= 1.0 || sy <= 0.0 || sy >= 1.0) { sunAbove = 1f; return; }
#if DEV
            if (skipRead) return;
#endif
            shadowRead.Ask(shadowMap, Mathf.Clamp((int)(sx * Shadow), 0, Shadow - 1), Mathf.Clamp((int)(sy * Shadow), 0, Shadow - 1), 1, 1);
            // (read at once, the answer is here now)
            if (!shadowRead.Later && shadowRead.Take(out object _))
            {
                Color m = shadowRead.Pixel();
                float lowest = m.g * 1000f, highest = m.b * 1000f;
                float above = m.r * Mathf.Clamp01((highest - (float)h) / Mathf.Max(highest - lowest, 60f));
                sunAbove = float.IsNaN(above) ? 1f : Mathf.Exp(-above);
            }
        }
        Readback shadowRead;
        bool byCamera;          // the shade read under the camera, far from the craft (see Update)

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
#if DEV
            if (due || !waits) traceWho = "LateUpdate";
#endif
            if (due || !waits) Frame();
            due = waits;
        }

        // (Development: where Frame's time goes, part by part: Clouds.Parts. Begin and Mark are left out of a release build.)
        [System.Diagnostics.Conditional("DEV")] static void Begin()
        {
#if DEV
            partsWatch.Reset(); partsWatch.Start(); partsLast = 0.0; partsFrames++;
#endif
        }
        [System.Diagnostics.Conditional("DEV")] static void Mark(string name)
        {
#if DEV
            double now = partsWatch.Elapsed.TotalMilliseconds, d = now - partsLast; partsLast = now;
            if (!partsSum.ContainsKey(name)) { partsSum[name] = 0.0; partsWorst[name] = 0.0; partsOrder.Add(name); }
            partsSum[name] += d; if (d > partsWorst[name]) partsWorst[name] = d;
#endif
        }
#if DEV
        static readonly System.Diagnostics.Stopwatch partsWatch = new System.Diagnostics.Stopwatch();
        static readonly Dictionary<string, double> partsSum = new Dictionary<string, double>(), partsWorst = new Dictionary<string, double>();
        static readonly List<string> partsOrder = new List<string>();
        static int partsFrames; static double partsLast;
        public static string Parts()
        {
            var b = new System.Text.StringBuilder("Frame over " + partsFrames + " frames, ms (mean, worst):");
            foreach (string n in partsOrder) b.Append(" | ").Append(n).Append(' ').Append((partsSum[n] / Math.Max(1, partsFrames)).ToString("F3")).Append(", ").Append(partsWorst[n].ToString("F2"));
            partsFrames = 0; partsSum.Clear(); partsWorst.Clear(); partsOrder.Clear();
            return b.ToString();
        }
#endif

        void Frame()
        {
            frame++;
            Begin();
#if DEV
            // (EVE's kept hidden too: held off for a test, no clouds at all; brought back, EVE's stock layer stood over the sea in a test of
            // the slicks from afar, 2026-10-20. And hidden afresh, as below: after a quickload EVE makes its clouds and their shadow on
            // the ground anew, and nothing hid them while these were held off. Its stock layer and its shadows were likely what lay
            // over the land in the far ground's high views, 2026-10-20 14:00, the dark blotches of the owner's crops.)
            if (heldOff)
            {
                CelestialBody held = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready ? FlightGlobals.currentMainBody : null;
                if (held != null && CloudsPage.HideEve != null && CloudsPage.HideEve) CloudsEve.Hide(held);
                Off("Clouds held off (development build).", false);
                return;
            }
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
            if (nearCam == null) { Off("", false); return; }
            // (EVE's clouds hidden as soon as these are the world's, before the map is ready and whether or not a cloud is in sight:
            // hidden only once a cloud was drawn, EVE's stock layer showed in clear weather from a load until the first cloud came)
            if (CloudsPage.HideEve != null && CloudsPage.HideEve) CloudsEve.Hide(body); else CloudsEve.Show();
            Mark("EVE");
#if DEV
            lateCam = farCam != null ? farCam : nearCam; lateAt = lateCam.transform.position; lateLooks = lateCam.transform.rotation; lateFrame = Time.frameCount;
#endif
            bool split = farCam != null;            // (OpenGL: two cameras; Direct3D: Camera 00 alone, as far as the scene goes)
            if (!split) farCam = nearCam;
            Split = split;

            Mark("start");
            // ---- where everything is
            double R = body.Radius;
            Vector3d camRel = (Vector3d)farCam.transform.position - body.position;
            double alt = camRel.magnitude - R;
            body.GetLatLonAlt((Vector3d)farCam.transform.position, out double camLat, out double camLon, out double _);
            double ut = Planetarium.GetUniversalTime();
            double reach = Math.Max(6.0, Math.Acos(Math.Min(1.0, R / (R + Math.Max(0.0, alt)))) * 180.0 / Math.PI + 5.0);
            if (!map.Update(body, ut, camLat, camLon, reach, false) || !air.Ready) { Off("Clouds: getting ready.", false); return; }
            Mark("map");

            // The storms near the camera (drawn as towers where the weather has them), asked again every second, each for now and
            // for one step on (a second of the game's clock, more when time is warped): what is drawn is each storm between the
            // two, moved on and grown frame by frame. (Until 2026-10-18 they were drawn as asked, held for the second, and every
            // tower jumped once a second by its drift and its growth: one of the twitches the owner saw, measured in a storm with
            // the clouds' noise held still as 2 to 5 % of the picture changed at once, every second.)
            // (The storms within sight, found once a second; with the weather's own model, on a thread of their own, the nearest
            // six sorted out here when it is done: found on the game's thread, as until 2026-10-18, it was a 3.9 ms hitch every
            // second. Under another weather (the stand-in) still here.)
            if (cellsWork != null && cellsWork.IsCompleted)
            {
                if (!cellsWork.IsFaulted && cellsWorkBody == body.bodyName) Found(body, camRel, cellsFound, cellsFoundNext, cellsWorkUt0, cellsWorkUt1);
                cellsWork = null;
            }
            if (cellsBody != body.bodyName) { cells.Clear(); nextById.Clear(); cellsBody = body.bodyName; cellsAt = -100f; }
            if (cellsWork == null && (Time.realtimeSinceStartup - cellsAt > 1f || ut > cellsUt1))
            {
                cellsAt = Time.realtimeSinceStartup;
                double radius = Math.Min(400000.0, reach * R * Math.PI / 180.0), lat = camLat, lon = camLon, t0 = ut, t1 = ut + Math.Max(1.0, TimeWarp.CurrentRate);
                if (CloudSource.Keystones)
                {
                    CelestialBody b = body; List<WeatherCell> found = cellsFound, next = cellsFoundNext;
                    cellsWorkBody = body.bodyName; cellsWorkUt0 = t0; cellsWorkUt1 = t1;
                    cellsWork = System.Threading.Tasks.Task.Run(() => { found.Clear(); next.Clear(); CloudSource.Cells(b, lat, lon, radius, t0, found); CloudSource.Cells(b, lat, lon, radius, t1, next); });
                }
                else
                {
                    cellsFound.Clear(); cellsFoundNext.Clear();
                    CloudSource.Cells(body, lat, lon, radius, t0, cellsFound); CloudSource.Cells(body, lat, lon, radius, t1, cellsFoundNext);
                    Found(body, camRel, cellsFound, cellsFoundNext, t0, t1);
                }
            }
            Mark("storm cells");
            // (fog within sight, drawn by the clouds only from well over its top: under that, Natural Light's veil draws it, and
            // the shell walked through is not taken down to the ground for it; see CloudSource.Kind and the shader's formAt,
            // which leaves out each place's fog by its own top. Over the lowest fog in sight, not the highest: hill fog in
            // sight, its top on high ground, kept sea fog from being drawn under a camera three kilometres up.)
            bool fogFromOver = map.FogTop > 0f && alt > map.FogTopLowest + 1500.0;
            if (!map.AnyCloud && !fogFromOver && cells.Count == 0) { Off("Clouds: none within sight.", false); return; }

            // The planet's own axes, in the scene's: towards latitude 0 longitude 0, towards longitude 90, the north pole.
            Axes(body, out Vector3d fx, out Vector3d fy, out Vector3d fz);
            Vector3d camFixed = new Vector3d(Vector3d.Dot(camRel, fx), Vector3d.Dot(camRel, fy), Vector3d.Dot(camRel, fz));
            Vector3 ToFixed(Vector3 v) => new Vector3((float)Vector3d.Dot(v, fx), (float)Vector3d.Dot(v, fy), (float)Vector3d.Dot(v, fz));

            // The wind carries the clouds (as the weather says the low clouds move here): how far, kept up from frame to frame.
            WeatherHere hereNow = CloudSource.At(body, camLat, camLon, ut);
            Mark("weather here");
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
            Mark("drift");
            CloudsWake.Follow(body, ut, drift, fx, fy, fz, map.LowestBase, map.HighestTop, NoiseWrap);
            Mark("wake");
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

            Mark("where");
            // ---- the sun and the light round about
            CelestialBody sunBody = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            Vector3 toSun = sunBody != null ? (Vector3)(sunBody.position - (Vector3d)farCam.transform.position).normalized : Vector3.up;
            sunNow = toSun;
            float sunUp = Vector3.Dot((Vector3)up, toSun);
            // (the brightest other world in the sky, by how much of it is lit and how large it stands: the Mun over Kerbin.
            // By night a veil of ice is lit by it and has its ring round it (the shader's high cloud); 1 for a full Mun.)
            Vector3 toMoon = Vector3.up;
            float moonLit = 0f;
            if (sunBody != null && sunUp < 0.08f)
                foreach (CelestialBody other in FlightGlobals.Bodies)
                {
                    if (other == null || other == body || other == sunBody || other.isStar) continue;
                    Vector3d rel = other.position - (Vector3d)farCam.transform.position;
                    double d = rel.magnitude;
                    if (d < other.Radius * 2.0) continue;
                    float phase = 0.5f * (1f - (float)Vector3d.Dot(rel / d, (sunBody.position - other.position).normalized));
                    float lit = phase * (float)(other.Radius * other.Radius / (d * d)) / 2.8e-4f;
                    if (lit > moonLit) { moonLit = lit; toMoon = (Vector3)(rel / d); }
                }
            // (by night, and with the moon over the horizon: from under it, it lights nothing the eye sees)
            float moonHigh = moonLit;
            moonLit = Mathf.Min(moonLit, 1.5f) * Mathf.Clamp01((0.08f - sunUp) / 0.1f) * Mathf.Clamp01((Vector3.Dot(toMoon, (Vector3)up) + 0.02f) / 0.06f);
#if DEV
            {
                Vector3 mNorth = Vector3.ProjectOnPlane((Vector3)(body.position + (Vector3d)body.transform.up * body.Radius - (Vector3d)farCam.transform.position), (Vector3)up).normalized;
                Vector3 mEast = Vector3.Cross((Vector3)up, mNorth);
                MoonSaid = "the brightest moon: " + (moonHigh > 0f ? Mathf.RoundToInt(Mathf.Atan2(Vector3.Dot(toMoon, mEast), Vector3.Dot(toMoon, mNorth)) * Mathf.Rad2Deg) + " deg round from north, " + Mathf.RoundToInt(Mathf.Asin(Mathf.Clamp(Vector3.Dot(toMoon, (Vector3)up), -1f, 1f)) * Mathf.Rad2Deg) + " up, its light " + moonLit.ToString("F2") + " of a full Mun's (" + moonHigh.ToString("F2") + " if it were up and the sun down)" : "none (by day, or none in the sky)") + "; the sun " + Mathf.RoundToInt(Mathf.Atan2(Vector3.Dot(toSun, mEast), Vector3.Dot(toSun, mNorth)) * Mathf.Rad2Deg) + " deg round from north, " + Mathf.RoundToInt(Mathf.Asin(Mathf.Clamp(sunUp, -1f, 1f)) * Mathf.Rad2Deg) + " deg up";
            }
#endif
            float brightness = CloudsPage.Brightness != null ? (float)CloudsPage.Brightness : 1f;
            // (the light at the clouds' height, a kilometre or two up, from the table: the sky lights them as the sun lights the air)
            Color sunAtClouds = air.Through(1500f, Mathf.Max(sunUp, -0.2f));
            // (under a deck of cloud the light from above is the cloud's grey, not the sky's blue)
            float overhead = Mathf.Max(CloudSource.Kind(hereNow.Low) >= 0f && hereNow.Low.Kind != CloudKind.Fog ? hereNow.Low.Cover : 0f, CloudSource.Kind(hereNow.Middle) >= 0f ? hereNow.Middle.Cover : 0f);
            float greyed = overhead * overhead;
            float skyLum = 0.2126f * air.Sky.r + 0.7152f * air.Sky.g + 0.0722f * air.Sky.b;
            Color skyLight = Color.Lerp(air.Sky, new Color(skyLum, skyLum, skyLum), greyed) * (0.32f * brightness);
            // (the shade's colour, for the shadow's pass (cloudshade.glsl): what a shadow leaves is the sky's light, which is bluer than
            // the sun's; the sun's light over the sky's in each colour, against green's: about 1.36, 1, 0.57 for Kerbin's clear sky
            // with the sun high, more so with it low, nearly even under a grey sky)
            {
                float overGreen = sunAtClouds.g / Mathf.Max(skyLight.g, 1e-5f);
                shadeTint = overGreen > 1e-4f
                    ? new Vector4(Mathf.Clamp(sunAtClouds.r / Mathf.Max(skyLight.r, 1e-5f) / overGreen, 0.25f, 4f), 1f, Mathf.Clamp(sunAtClouds.b / Mathf.Max(skyLight.b, 1e-5f) / overGreen, 0.25f, 4f), 1f)
                    : new Vector4(1f, 1f, 1f, 1f);
            }
            float groundLit = Mathf.Max(0f, sunUp);
            Color groundLight = new Color(0.30f, 0.30f, 0.26f) * (0.4f * groundLit * brightness) * sunAtClouds;
            float night = 0.0035f * brightness;

            Mark("light");
            // ---- how much cloud, how thick, how fine
            float lowestBase = map.AnyCloud ? map.LowestBase : 99999f, highestTop = map.HighestTop;
            if (fogFromOver) { lowestBase = Mathf.Min(lowestBase, map.FogBase); highestTop = Mathf.Max(highestTop, map.FogTop); }
            if (lowestBase > 99000f) lowestBase = map.LowestBase;
            float shellBottom = Mathf.Max(0f, lowestBase - 60f), shellTop = Mathf.Max(highestTop, shellBottom + 300f) + 150f;
            foreach (WeatherCell c in cells) { shellBottom = Mathf.Min(shellBottom, Mathf.Max(0f, c.Base - 60f)); shellTop = Mathf.Max(shellTop, c.Top + 300f); }

            Mark("how much (map)");
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
            Mark("cyclone");
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

            // The shell as eased: its floor and top follow the shell's own over a few seconds. The lengths that go by the shell's
            // thickness (the walk's farthest steps, the distance run inside it from above, the shadow map's steps) go by these, so
            // that a floor or top that jumps, as it does whenever the weather's map is made again, a storm grows or a cyclone
            // goes through its life, does not move them all at once; the walk's steps, its weather readings and its strides
            // through clear air are laid on grids from the camera (clouds.glsl, firstStep), so that the shell's own floor and top
            // only add or take away steps at the ends of each line. Until 2026-10-18 they were laid out from where each line
            // entered the shell, and the whole picture of the clouds jumped at every change: the owner's "twitch ... spasming",
            // on every system (found and measured by the rendering research: research/rendering/experiments/cloud-twitch.md).
            // Begun afresh on another world or after a jump of more than 20 km.
            if (float.IsNaN(easedBottom) || easedBody != body.bodyName || Mathf.Abs(shellTop - easedTop) > 20000f)
            { easedBottom = shellBottom; easedTop = shellTop; easedBody = body.bodyName; }
            else
            {
                float k = 1f - Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.25f) / 3f);
                easedBottom += (shellBottom - easedBottom) * k; easedTop += (shellTop - easedTop) * k;
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
#if DEV
            steps = Mathf.RoundToInt(steps * StepsTimes);
#endif
            // (light lost per metre in the thickest cloud: a little more than a real heap's core takes, 0.05 to 0.1 a metre, since
            // only the cores reach it (the shader's density reaches it a third of the way in from a heap's edge); less, and heaps
            // against the sun were pale and their shaded sides flat)
            // (The rework of 2026-10-11: 0.07 where it was 0.15. At 0.15 a heap was solid a few metres in: a hard outline round two
            // flat tones, which the user called "comical, like anime clouds", not volumetric. At 0.07, about what a real heap takes
            // (0.03 to 0.06 a metre), its thin places glow and its body shades from the lit side to the far one.)
            float thick = 0.07f * (CloudsPage.Thickness != null ? (float)CloudsPage.Thickness : 1f);
            float amount = CloudsPage.Amount != null ? (float)CloudsPage.Amount : 1f;

            Mark("squall");
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

            Mark("lightning");
            // ---- the storms, as the shader takes them
            // (kept from frame to frame: two new arrays every frame were garbage the collector came round for)
            Vector4[] cellA = keptCellA, cellB = keptCellB;
            Array.Clear(cellA, 0, cellA.Length); Array.Clear(cellB, 0, cellB.Length);
            // (not those inside a cyclone: its own wall and bands stand there, and each storm's anvil drawn as well was a
            // round plate lying on its canopy. The weather keeps them: their lightning lights the cyclone's cloud.)
            int cellsSent = 0;
            for (int i = 0; i < cells.Count && cellsSent < 6; i++)
            {
#if DEV
                if (NoStorms || cellsSent >= StormsSent) break;
#endif
                WeatherCell c = Between(cells[i], ut);
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

            Mark("storms");
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

            Mark("tornado");
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

            Mark("shadow's square");
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

            Mark("heaps' map");
            // (From above all the clouds the map is spread wider, four times the camera's height across or more (a dot then some
            // hundreds of metres, a few of them to a heap: as fine as the picture is from there), so that the heaps of the
            // world below are read from it and not worked out at every asking: from orbit, worked out so, they cost six
            // milliseconds a frame more than the map does.)
            float heapsAcross = HeapsWidth(alt > shellTop + 500.0 ? 4.0 * alt : 0.0);
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
                m.SetVector(idShellEased, new Vector4(easedBottom, easedTop, 0f, 0f));
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
                // (a full Mun's own light on a veil of ice: twelve times the night's light from all round, which is the sky's
                // glow and the Mun's light scattered about together; at twice, the ring and the glow round the Mun were lost)
                m.SetVector(idMoon, new Vector4(toMoon.x, toMoon.y, toMoon.z, 12f * night * moonLit));
                m.SetVector(idSky, new Vector4(skyLight.r, skyLight.g, skyLight.b, 1f));
                m.SetVector(idGround, new Vector4(groundLight.r, groundLight.g, groundLight.b, 120f));
                m.SetVector(idAir, new Vector4(air.Rayleigh.x, air.Rayleigh.y, air.Rayleigh.z, air.Haze));
                // (and whether the world has a sea: from above the clouds the walk shades the sea's surface, which the camera's
                // picture of depths does not hold, as the sea is shaded, not as land)
                m.SetVector(idAirHigh, new Vector4(air.RayleighHeight, air.HazeHeight, body.ocean ? 1f : 0f, 0f));
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
                // (the light field the walk reads: the one made whole; nought in w where there is none yet)
                bool lightOn = LightField && !lightHigh && lightReady && lightRead != null;
                SetLight(m, readMid, readEast, readNorth, readLow, readStep, lightOn ? 1f : 0f, fx, fy, fz);
                m.SetTexture(idLight, lightOn ? (Texture)lightRead : Texture2D.whiteTexture);
                m.SetTexture(idShape, shape); m.SetTexture(idDetail, detail);
                // (the shapes of the rework of 2026-10-12: whether, and the time in lifetimes of a heap, kept within 1024 of
                // them, where the shader's numbering of each point's heaps comes round)
                CloudsWake.Hand(m, ut, noiseAt, NoiseWrap, CloudsPage.Stirred == null || CloudsPage.Stirred);
                if (cellsTable == null) MakeCellsTable();
                m.SetTexture("_CloudCells", cellsTable);
                m.SetTexture("_CloudHeaps", heapsOn ? (Texture)heapsMap : Texture2D.blackTexture);
                m.SetTexture("_CloudHeapsB", heapsOn ? (Texture)heapsMapB : Texture2D.blackTexture);
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

            Mark("shared");
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
                // (and how wide a pixel of the far camera's walk is for each metre off, by which the shader chooses the near shapes or
                // the far field at each place of the map as the walk chooses them for it: told only of the map's own 512 dots, it
                // chose the far field everywhere, and heaps drawn by the near shapes had the far field's shadows under them)
                // (and, z, whether the map is drawn whole this frame: then without the lumps on the heaps, which cost half as much again
                // and would come all at once, and in their strips within the next eight frames)
                shadowWalk.SetVector("_CloudJitter", new Vector4(0f, 0f, shadowAll ? 1f : 0f, WalkPixel(farCam, quality)));
                // (an eighth of the map each frame, one strip of eight in turn: see the shader's go 4 for why not by `discard`; a
                // quarter until 2026-10-18, when the map began to be made of the clouds as drawn)
                shadowStrip = shadowAll ? -1 : (frame & 7);
            }

            Mark("shadow map");
            // ---- the light field: a band of the one being made each frame (drawn in the far camera's shade pass, after the shadow
            // map); when it is whole it becomes the one read, and the next is begun round where the camera will be halfway through
            // the round it is read in (the camera's speed over the planet, smoothed)
            // (Neither made nor read with the camera far above the clouds' top: from there every cloud is drawn by the far field,
            // which the field's cells are too coarse to light (research/rendering/experiments/hurricane-towers-from-orbit.md), and
            // the main chat measured it costing 0.8 ms a frame from 200 km (2026-10-18). Off above 15 km over the top; below 12 km
            // again, a whole field made afresh before it is read.)
            float overTop = (float)alt - shellTop;
            if (!lightHigh && overTop > 15000f) { lightHigh = true; lightReady = false; lightBand = -1; }
            else if (lightHigh && overTop < 12000f) lightHigh = false;
            if (LightField && !lightHigh)
            {
                if (lightMaking == null) { lightRead = MakeLight("read"); lightMaking = MakeLight("being made"); lightReady = false; lightBand = -1; }
                if (lightBody != body.bodyName) { lightBody = body.bodyName; lightReady = false; lightBand = -1; lightCamKnown = false; }
                float dtNow = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                if (lightCamKnown && (camFixed - lightCamWas).magnitude < 20000.0)
                    lightGoes = Vector3d.Lerp(lightGoes, (camFixed - lightCamWas) / dtNow, Mathf.Clamp01(dtNow * 2f));
                else lightGoes = Vector3d.zero;
                lightCamWas = camFixed; lightCamKnown = true;
                if (lightBand < 0 || lightBand >= LightRound)
                {
                    if (lightBand >= LightRound)
                    {
                        RenderTexture was = lightRead; lightRead = lightMaking; lightMaking = was;
                        readMid = lightMid; readEast = lightEast; readNorth = lightNorth; readLow = lightLow; readStep = lightStep;
                        lightReady = true;
#if DEV
                        traceSwap = true;
#endif
                    }
                    Vector3d at = camFixed + lightGoes * (1.5 * LightRound * dtNow);
                    lightMid = at.normalized;
                    Vector3d across = Vector3d.Cross(new Vector3d(0.0, 0.0, 1.0), lightMid);
                    if (across.sqrMagnitude < 1e-6) across = Vector3d.Cross(new Vector3d(0.0, 1.0, 0.0), lightMid);
                    lightEast = across.normalized;
                    lightNorth = Vector3d.Cross(lightMid, lightEast).normalized;
                    lightLow = Mathf.Max(0f, easedBottom); lightStep = Mathf.Max(easedTop - lightLow, 300f) / LightHeights;
                    lightBand = 0;
                }
                if (lightWalk == null) lightWalk = new Material(walkShader) { name = "Natural Weather: the light field" };
                Common(lightWalk, farCam, 4 * LightCells);
                // (and how wide a pixel of the far camera's walk is, as the shadow's map is told: see the shader's formChoice)
                lightWalk.SetVector("_CloudJitter", new Vector4(0f, 0f, 0f, WalkPixel(farCam, quality)));
                lightWalk.SetVector(idStage, new Vector4(9f, 0f, 0f, 0f));
                SetLight(lightWalk, lightMid, lightEast, lightNorth, lightLow, lightStep, 0f, fx, fy, fz);
                // (what is drawn into is not also read)
                lightWalk.SetTexture(idLight, Texture2D.whiteTexture);
            }
            else if (!LightField && lightMaking != null)
            {
                lightRead.Release(); Destroy(lightRead); lightRead = null;
                lightMaking.Release(); Destroy(lightMaking); lightMaking = null;
                lightReady = false; lightBand = -1;
            }

            Mark("light field");
            // ---- which copy of the shader walks the clouds this frame (2026-10-18). A feature's code costs the walk even where it
            // does not run: without the cyclone and the tornado the walk is about a quarter cheaper, without the storm's parts as
            // well about two fifths, for the same picture (research/rendering/experiments/cloud-shader-variants.md). So the whole
            // shader only where a cyclone or a tornado is near; the storm's copy where there are storm cells, lightning, a squall
            // line or heaps made as smoke; the calm one otherwise. (Where a copy is missing, an older bundle, the next fuller one.)
            bool stormy = shelfA.w > 0f || flashes > 0 || cells.Count > 0 || (Heaps.Body == body && Heaps.Now.Count > 0);
            walkNow = eyeA.w > 0f || twistA.w > 0f ? walkShader : stormy ? (walkStorm ?? walkShader) : (walkCalm ?? walkStorm ?? walkShader);
#if DEV
            if (CopyHeld == 1) walkNow = walkShader; else if (CopyHeld == 2 && walkStorm != null) walkNow = walkStorm;
            CopyNow = walkNow == walkShader ? "the whole shader" : walkNow == walkStorm ? "the storm's copy" : "the calm copy";
#endif

            Mark("which shader");
            // ---- each camera's clouds
            const float nearReach = 450f;
            bool nearWanted = split && alt > shellBottom - nearReach && alt < shellTop + nearReach;
            Setup(far, farCam, split ? farCam.nearClipPlane * 0.98f : 0f, 0f, quality, shadows, true, Common, body, camFixed, ToFixed, shadowMidScene, sunUp, clear, first);
            if (nearWanted) { ShadowAloneOff(); Setup(near, nearCam, 0f, nearCam.farClipPlane * 1.02f, quality, shadows, false, Common, body, camFixed, ToFixed, shadowMidScene, sunUp, clear, false); }
            else
            {
                if (near.On) Detach(near);
                ShadowAlone(split ? nearCam : null, shadows, body, shadowMidScene, sunUp);
            }
            Mark("each camera");
            Laying(farCam, nearCam);
            Drawing = true;
            Mark("laying");
            // (the words twice a second, not a new string every frame)
            if (Time.unscaledTime - reportAt > 0.5f || Report.Length == 0)
            {
                reportAt = Time.unscaledTime;
            Report = "Clouds from " + CloudSource.Name + ": " + (near.On ? "near and far" : "far") + (map.HighestTop > 0f ? ", " + Mathf.RoundToInt(map.LowestBase) + " to " + Mathf.RoundToInt(map.HighestTop) + " m" : "") +
                     (cells.Count > 0 ? ", " + cells.Count + " storm" + (cells.Count > 1 ? "s" : "") + " near" : "") + (sunDim < 0.98f ? "; the craft in their shade (" + Mathf.RoundToInt(100f * sunDim) + "% of the sun)" : "") +
                     (CloudsEve.Count > 0 ? "; EVE's clouds hidden" : "");
            }
#if DEV
            if (traceLeft > 0) TraceFrame(shellBottom, shellTop, camFixed, R);
#endif
        }

        readonly Vector4[] keptCellA = new Vector4[6], keptCellB = new Vector4[6];
        float reportAt = -10f;

        void FreeHeaps()
        {
            if (heapsMap != null) { heapsMap.Release(); Destroy(heapsMap); heapsMap = null; }
            if (heapsMapB != null) { heapsMapB.Release(); Destroy(heapsMapB); heapsMapB = null; }
        }

        /// <summary>The storms found (see Frame): the nearest six kept, and where each will be at the next moment asked.</summary>
        void Found(CelestialBody body, Vector3d here, List<WeatherCell> found, List<WeatherCell> next, double t0, double t1)
        {
            cells.Clear(); cells.AddRange(found);
            cells.Sort((a, b) => Distance(body, a, here).CompareTo(Distance(body, b, here)));
            if (cells.Count > 6) cells.RemoveRange(6, cells.Count - 6);
            cellsUt0 = t0; cellsUt1 = t1;
            nextById.Clear();
            foreach (WeatherCell n in next) nextById[n.Id] = n;
        }
        readonly List<WeatherCell> cellsFound = new List<WeatherCell>(), cellsFoundNext = new List<WeatherCell>();
        System.Threading.Tasks.Task cellsWork;
        string cellsWorkBody;
        double cellsWorkUt0, cellsWorkUt1;

        static double Distance(CelestialBody body, WeatherCell c, Vector3d camRel) => (SceneAt(body, c.Latitude, c.Longitude, 0.0) - camRel).sqrMagnitude;

        // (Never the length itself: a drift that stands still but for rounding, a hair under nought on a day when the wind has nothing
        // along the planet's axis, came to the length exactly when the length was added back (the hair is far under what a double
        // holds beside 1863 km), the length to nought the next frame, and so on: the noise's place along the axis was 0 and 207
        // lengths in turn, and the patterns read at other repeats (the banks', the cloudlets' bend) drew two skies, one each frame
        // (the main chat's held views of 2026-10-19, four in seven).)
        static double Wrap(double v, double length) { v %= length; if (v < 0.0) v += length; return v < length ? v : 0.0; }

        delegate void CommonValues(Material m, Camera cam, float width);
        delegate Vector3 Turn(Vector3 v);

        // (How much smaller each way than the picture the far camera's walk is: a quarter for "lower" and "middle", a half for
        // "higher". What is kept from frame to frame is the picture's own size, each frame's lines going through one of the
        // sixteen (or four) pixels each of the walk's dots stands for, in turn. Until 2026-10-18 "middle" walked at half size and
        // "lower" at a third without the finer keeping (blurred by three each way); in the lab the quarter walk with the light
        // field took a quarter to a third of the old middle's time, as close to the truth from the ground and inside a layer,
        // and closer than the old middle flying at 150 m/s and turning ten degrees a second (research/rendering/experiments/
        // cloud-light-field.md, research/rendering/notes/clouds-plan.md 3).)
        static int FarDiv(int quality) => quality == 2 ? 2 : 4;

        // (How wide a pixel of the far camera's walk is, for each metre off: as Setup sizes that walk's pictures (makesShadow), and
        // as the shader's formWeather works out formPer from them.)
        static float WalkPixel(Camera cam, int quality)
        {
            int div = FarDiv(quality);
            int w = Mathf.Max(16, cam.pixelWidth / div);
            int kept = Finer ? div * w : w;
            return 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect / kept;
        }

        // (the sixteen pixels a quarter-size walk's dot stands for, in Bayer's order: each frame's lines go through the one whose
        // number is the frame's, so that any four frames running cover the dot evenly)
        static readonly int[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>
        /// The clouds' shadow laid on what a camera has drawn, into its pass (cloudshade.glsl): the scene is drawn with the sun dimmed
        /// by the cloud above the craft (BeforeCamera), and each pixel's light is put right for the cloud above its own place,
        /// darker in a shadow the craft is not in, lighter in a gap of sunlight when the craft is in shade.
        /// </summary>
        void LayShadow(Material s, CommandBuffer into, Camera cam, CelestialBody body, Vector3d shadowMidScene, float sunUp, bool makesShadow, RenderTexture histD)
        {
            bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            Vector3 r = cam.transform.right * tanX, u = cam.transform.up * tanY, f = cam.transform.forward;
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
            // (how far the camera is above all the clouds, as the walk has it (clouds.glsl, aboveness): 0 within 100 m over the
            // shell's top, 1 from 1100 m over it. The square's shadow is weighted by one less this, the walk's own by this, so that
            // between the two nothing is shaded twice or not at all. Left off at a step 500 m over the top, as until 2026-10-18,
            // it went out in one frame while the walk's came in over a kilometre.)
            float above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shellTopNow + 100f, shellTopNow + 1100f, camHigh));
            s.SetVector(idShade, new Vector4(sunOverSky, 30000f, makesShadow ? cam.nearClipPlane * 0.98f : 0f, makesShadow ? 1e9f : cam.farClipPlane * 1.02f));
            s.SetVector(idShadeSun, new Vector4(sunDim, body.ocean ? 1f : 0f, above, 0f));
#if DEV
            s.SetVector(idShadeTint, TestNoTint ? new Vector4(1f, 1f, 1f, 0f) : shadeTint);
            TintSaid = "tint " + shadeTint.x.ToString("F2") + ", " + shadeTint.y.ToString("F2") + ", " + shadeTint.z.ToString("F2") + (TestNoTint ? " (held off)" : "") + "; above the clouds " + above.ToString("F2");
#else
            s.SetVector(idShadeTint, shadeTint);
#endif
            // (and wet ground is darker ground, as far as the wet is known round the camera: Wet.cs)
            s.SetVector("_CloudWet", new Vector4(Weather.WetGround ? Wet.Ground : 0f, 2500f, 0f, 0f));
            s.SetTexture(idShadowMap, shadowMap);
            // (and this camera's clouds as taken together this frame, made before this pass: from above the clouds, how much
            // the walk's own shadow takes from the land, which this pass tints)
            s.SetTexture(idHistD, histD != null ? (Texture)histD : Texture2D.blackTexture);
            into.DrawMesh(sheet, Matrix4x4.identity, s, 0, 0);
            // (And the same shadow handed to what is drawn after this pass and cannot be shaded by it: Natural Ocean's sea,
            // drawn after Scatterer copies the picture, which reads the shadow at each point of its surface for its sun's
            // glitter and light (ocean.glsl). In the world's own axes, from the pass that makes the shadow, once a frame.)
            if (makesShadow)
            {
                Vector3d mid = body.position + shadowMidScene;
                Shader.SetGlobalTexture(idWeatherShadowMap, shadowMap);
                Shader.SetGlobalVector(idWeatherShadowAt, new Vector4((float)mid.x, (float)mid.y, (float)mid.z, (float)shadowSide));
                Shader.SetGlobalVector(idWeatherShadowX, new Vector4(sx.x, sx.y, sx.z, groundHeight));
                Shader.SetGlobalVector(idWeatherShadowY, new Vector4(sy.x, sy.y, sy.z, 0f));
                Shader.SetGlobalVector(idWeatherShadowCentre, new Vector4((float)body.position.x, (float)body.position.y, (float)body.position.z, (float)body.Radius));
                Shader.SetGlobalVector(idWeatherShadowFade, new Vector4(sunDim, 30000f, above, 0f));
                Shader.SetGlobalVector(idWeatherShadowSun, new Vector4(sunNow.x, sunNow.y, sunNow.z, 1f));
            }
        }

        // ---- the near camera's shadow alone (2026-10-21). Its shadow is laid by the near camera's clouds' pass, which is drawn only
        // within 450 m of the clouds (Setup); below that the near camera laid none. The scene's sun is dimmed for both cameras by the
        // cloud above the craft, and only the far camera's ground was put right: with the craft under a heap, the ground within 400 m
        // of the camera stayed dimmed all over while beyond it the sunlit land came back, a straight edge across the picture where
        // the two cameras meet (up to 85 levels of 255 in a climb from the pad; and a heap's shadow within 400 m was not laid at all).
        // research/rendering/experiments/near-far-seam.md.
        Material nearShadow;
        CommandBuffer nearShade;
        Camera nearShadeCam;
        bool nearShadeAskedDepth;

        void ShadowAlone(Camera cam, bool shadows, CelestialBody body, Vector3d shadowMidScene, float sunUp)
        {
            if (cam == null || !shadows || shadowMap == null || shadeShader == null) { ShadowAloneOff(); return; }
            if (nearShadow == null)
            {
                nearShadow = new Material(shadeShader) { name = "Natural Weather: shadow laid on (near, alone)" };
                nearShade = new CommandBuffer { name = "Natural Weather: clouds' shadow (near, alone)" };
            }
            if (nearShadeCam != cam) { ShadowAloneOff(); cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, nearShade); nearShadeCam = cam; }
            if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; nearShadeAskedDepth = true; }
            nearShade.Clear();
            LayShadow(nearShadow, nearShade, cam, body, shadowMidScene, sunUp, false, null);
        }

        void ShadowAloneOff()
        {
            if (nearShadeCam != null)
            {
                if (nearShade != null) nearShadeCam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, nearShade);
                if (nearShadeAskedDepth) nearShadeCam.depthTextureMode &= ~DepthTextureMode.Depth;
            }
            nearShadeCam = null; nearShadeAskedDepth = false;
        }

        void Setup(Pass p, Camera cam, float from, float to, int quality, bool shadows, bool makesShadow, CommonValues common, CelestialBody body, Vector3d camFixed, Turn toFixed, Vector3d shadowMidScene, float sunUp, float clear, bool first)
        {
            if (p.Cam != cam) { Detach(p); p.HavePrev = false; }
            int wide = cam.pixelWidth, high = cam.pixelHeight;
            if (wide < 64 || high < 64) return;
            // (the far camera's: a quarter of the picture's size each way, a half for "higher" (FarDiv); the near camera's clouds,
            // soft mist within 400 m, a third always unless "higher")
            int div = makesShadow ? FarDiv(quality) : quality == 2 ? 2 : 3;
            int w = Mathf.Max(16, wide / div), h = Mathf.Max(16, high / div);
            // (What is kept from frame to frame is the picture's own size where the walk is done at half of it: each frame's
            // lines of sight go through a different one of the four pixels each of the walk's dots stands for, and over
            // four frames the kept picture is as fine as the screen. See the shader, where the frames are taken together.)
            bool finer = Finer && (div == 2 || div == 4);
#if DEV
            if (Trial == 22f) finer = false;        // (for comparing: what is kept, at the walk's own size, as before)
#endif
            int keptW = finer ? w * div : w, keptH = finer ? h * div : h;
            if (p.Now == null || p.Now.width != w || p.Now.height != h || p.HistA == null || p.HistA.width != keptW || p.HistA.height != keptH)
            {
                foreach (RenderTexture t in new[] { p.Now, p.NowD, p.HistA, p.HistDA, p.HistB, p.HistDB }) if (t != null) { t.Release(); Destroy(t); }
                RenderTexture Make(RenderTextureFormat f, string name, int across, int up)
                {
                    var t = new RenderTexture(across, up, 0, f, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: " + p.Name + " clouds, " + name };
                    t.Create();
                    return t;
                }
                // (the distance pictures with a second channel: how much the walk's own shadow from above the clouds takes from the
                // land's light, for the shadow's pass to tint (cloudshade.glsl, aboveTint))
                p.Now = Make(RenderTextureFormat.ARGBHalf, "this frame", w, h); p.NowD = Make(RenderTextureFormat.RGHalf, "this frame's distance", w, h);
                p.HistA = Make(RenderTextureFormat.ARGBHalf, "taken together", keptW, keptH); p.HistDA = Make(RenderTextureFormat.RGHalf, "distance taken together", keptW, keptH);
                p.HistB = Make(RenderTextureFormat.ARGBHalf, "taken together (2)", keptW, keptH); p.HistDB = Make(RenderTextureFormat.RGHalf, "distance taken together (2)", keptW, keptH);
                p.HavePrev = false;
            }
            if (p.Walk == null)
            {
                Warm();
                p.Walk = new Material(walkShader) { name = "Natural Weather: walk" };
                p.Together = new Material(walkShader) { name = "Natural Weather: over frames" };
                p.Over = new Material(overShader) { name = "Natural Weather: laid on", renderQueue = LaidQueue };
                p.Shadow = new Material(shadeShader) { name = "Natural Weather: shadow laid on" };
                p.Draw = new CommandBuffer { name = "Natural Weather: clouds (" + p.Name + ")" };
                p.After = new CommandBuffer { name = "Natural Weather: after the clouds (" + p.Name + ")" };
                p.Shade = new CommandBuffer { name = "Natural Weather: clouds' shadow (" + p.Name + ")" };
                p.Early = new CommandBuffer { name = "Natural Weather: clouds walked (" + p.Name + ")" };
            }
            // (the copy this frame's sky allows: the values set on the material stay with it, and all of them are set again below)
            if (walkNow != null && p.Walk.shader != walkNow) p.Walk.shader = walkNow;
            if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; p.AskedDepth = true; }

            common(p.Walk, cam, w);
            common(p.Together, cam, w);
            p.Walk.SetVector(idStage, new Vector4(2f, frame % 64, from, to));
            // (the near camera's stretch is short, and flown through: finer steps)
            p.Walk.SetVector(idHaze, new Vector4(noiseHasMips ? 1f : 0f, makesShadow ? 40f : 15f, 1.5f, 1f));
            p.Together.SetVector(idStage, new Vector4(3f, frame % 64, from, to));
            // (which of the four pixels this frame's lines go through: the two on one diagonal, then the two on the other; of sixteen,
            // in Bayer's order; and, w, how much finer a kept dot is than the walk's)
            Vector4 jitter = Vector4.zero;
            if (finer && div == 2)
            {
                int turn = frame & 3;
                jitter = new Vector4((turn == 0 || turn == 3 ? -0.25f : 0.25f) / w, (turn == 0 || turn == 2 ? -0.25f : 0.25f) / h, 1f, 0.5f);
            }
            else if (finer)
            {
                int k = Array.IndexOf(Bayer4, frame & 15);
                jitter = new Vector4(((k % 4 + 0.5f) / 4f - 0.5f) / w, ((k / 4 + 0.5f) / 4f - 0.5f) / h, 1f, 0.25f);
            }
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
            // (the far camera's clouds, as they will be laid this frame, and how far off they are, for Keystone's pass that writes
            // the motion of what lies beyond the near camera: where they hide most of what is behind them, a smoothing between
            // frames follows them, not the ground under them)
            if (makesShadow) TellFarMotion(histOut, histDOut);
            // (Scatterer's sun flare, its rays and its ghosts, dimmed by how much of the sun these clouds let through: its shader
            // multiplies them by the alpha of the global texture `scattererReconstructedCloud` where the sun stands in the picture,
            // which Scatterer sets white once, at its flare's making, for a mod of raymarched clouds to hand over. The far camera's
            // clouds as they will be laid this frame are that (their alpha is how much of what is behind them shows through), made
            // in the far camera's drawing before Scatterer's near camera draws its flare. Not handed over, the flare shone through
            // the thickest cloud: the sun's disc and rays in the middle of a dark heap at dusk (Natural Ocean's evening picture,
            // 2026-10-18; research/rendering/experiments/sun-flare-through-cloud.md). Clouds within the near camera's 400 m on
            // OpenGL are not in it: the flare goes on through those, as before.)
            if (makesShadow) Shader.SetGlobalTexture(idFlareCloud, histOut);
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

            // ---- what is drawn into pictures of its own, once the camera's depth picture is made and before its own picture is
            // begun (see Pass.Early): the far camera's maps, then the walk, which reads them, and its taking together
            p.Early.Clear();
            // (the low clouds' map smoothed, first: everything after reads it)
            if (makesShadow && mapFine != null && mapFineWalk != null)
            {
                p.Early.SetRenderTarget(mapFine);
                p.Early.DrawMesh(sheet, Matrix4x4.identity, mapFineWalk, 0, 0);
            }
            if (makesShadow && heapsOn && heapsWalk != null)
            {
                p.Early.SetRenderTarget(new RenderTargetIdentifier[] { heapsMap, heapsMapB }, heapsMap);
                p.Early.DrawMesh(sheet, Matrix4x4.identity, heapsWalk, 0, 0);
            }
            if (shadows && shadowMap != null && makesShadow)
            {
                p.Early.SetRenderTarget(new RenderTargetIdentifier[] { shadowMap, shadowMapB }, shadowMap);
                if (shadowStrip >= 0) p.Early.EnableScissorRect(new Rect(0f, shadowStrip * (Shadow / 8), Shadow, Shadow / 8));
                p.Early.DrawMesh(sheet, Matrix4x4.identity, shadowWalk, 0, 0);
                if (shadowStrip >= 0) p.Early.DisableScissorRect();
            }
            // (the light field: this frame's band of rows of the one being made, before any walk reads the one made whole)
            if (makesShadow && LightField && !lightHigh && lightWalk != null && lightMaking != null && lightBand >= 0 && lightBand < LightRound)
            {
                int rows = LightHeights / 4 * LightCells * LightLevels, band = (rows + LightRound - 1) / LightRound;
                p.Early.SetRenderTarget(lightMaking);
                p.Early.EnableScissorRect(new Rect(0f, lightBand * band, 4 * LightCells, band));
                p.Early.DrawMesh(sheet, Matrix4x4.identity, lightWalk, 0, 0);
                p.Early.DisableScissorRect();
                lightBand++;
            }
            p.Early.SetRenderTarget(new RenderTargetIdentifier[] { p.Now, p.NowD }, p.Now);
            p.Early.DrawMesh(sheet, Matrix4x4.identity, p.Walk, 0, 0);
            p.Early.SetRenderTarget(new RenderTargetIdentifier[] { histOut, histDOut }, histOut);
            p.Early.DrawMesh(sheet, Matrix4x4.identity, p.Together, 0, 0);
            // (and the camera's own picture to be drawn into after this, as before it)
            p.Early.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

            // the shadow, before anything see-through is drawn
            p.Shade.Clear();
            if (shadows && shadowMap != null) LayShadow(p.Shadow, p.Shade, cam, body, shadowMidScene, sunUp, makesShadow, histDOut);
            else if (makesShadow) Shader.SetGlobalVector(idWeatherShadowSun, Vector4.zero);
            if (!p.On)
            {
                // (the shadow straight after the camera's solid things: Scatterer copies the picture after that and lays its haze on
                // from the copy before anything see-through is drawn, so a shadow laid on later is wiped out by it. Its sea, drawn
                // after the copy, takes no shadow so.)
                cam.AddCommandBuffer(CameraEvent.AfterDepthTexture, p.Early);
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
        /// <summary>(Development build: the shadows' sky tint held off ("off") or as worked out ("on"), and what it is.)</summary>
        public static bool TestNoTint;
        public static string TintSaid = "not drawn yet";
        /// <summary>(Development build: how many readings the light field takes beyond its first stretch towards the sun: 8, as until 2026-10-19, or 16.)</summary>
        public static float TestFieldFar = 16f;
        public static string FieldFar(string arg) { if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n) && n >= 1f) TestFieldFar = n; return "the light field's far readings: " + TestFieldFar; }
        public static string Tint(string arg) { if (arg == "off") TestNoTint = true; else if (arg == "on") TestNoTint = false; return TintSaid; }
        /// <summary>(Development build: where the brightest moon stands and how lit, as the clouds take it for its ring.)</summary>
        public static string MoonSaid = "";
        public static string Moon() => MoonSaid;

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
