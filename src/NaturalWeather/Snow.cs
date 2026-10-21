using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.5.0.) Lying snow: after snow has fallen where it is cold enough, the ground white, thinly and in patches at first,
    /// until it thaws.
    ///
    /// How much lies, and where. Snow settles where the ground is at or below freezing, and melts by how far the air is above
    /// it: in the snow-melt models of hydrology, some 3 to 8 mm of water a day for each degree above (here 0.2 mm an hour, a
    /// little under 5 a day), and rain on snow a little more (the rain's warmth: a millimetre of rain at 8 degrees melts a tenth
    /// of a millimetre). Fresh snow is a tenth water, so a millimetre of water falls as a centimetre of snow; it settles in a
    /// day or two to a third water. Higher up it is colder, by 6.5 degrees a kilometre (the standard atmosphere's lapse, as the
    /// weather model has it), so the snow lies from a line on the hills and deeper above it, as on real mountains.
    ///
    /// Here: the weather model's own past at the camera's place (its sky is a sum of systems and cells that move with time, so
    /// any hour gone can be asked for), hour by hour over the last three days, for eight heights above the sea 400 m apart, the
    /// camera's ground among the lowest; then brought on as the game's time goes. What falls: the model's 0 to 1 of rain or
    /// snow (drizzle 0.1, steady 0.3, heavy 0.6, a cloudburst 1), as water about 50 mm an hour times its square (drizzle half a
    /// millimetre, steady rain four, heavy eighteen, a cloudburst fifty, as those are measured), a third of that as snow (heavy
    /// snow is 2.5 to 5 mm of water an hour), snow by the model's own rule at each height's temperature (all of it below -0.75
    /// C, none above 2.45 C).
    ///
    /// How it is drawn: one pass over the near camera's picture and one over the far one's, after the ground and everything
    /// solid is drawn and before anything in the air (tools/shaderpack/snow.glsl: on what faces up, in patches while thin, lit
    /// by the light the picture itself shows there; not on the sea, the sky or the craft, whose boxes are given). Only while
    /// there is snow anywhere in sight. Not from orbit (the planet seen from there is the game's own far picture of it).
    /// </summary>
    public sealed class Snow : MonoBehaviour
    {
        public static string Report = "";
        /// <summary>At the camera's own ground: how deep the snow lies (cm), how much of it is fresh (0 to 1), the air's temperature there (kelvin; nothing when not known). For the snow drifting in the wind (Drift).</summary>
        public static float DeepHere, FreshHere, TemperatureHere;
        const int Bands = 8;
        const float Step = 400f;                    // metres between the heights reckoned for
        const double Back = 72.0 * 3600.0;          // how far back the snow on the ground is reckoned, s
        const float MeltPerDegree = 0.2f;           // mm of water an hour for each degree above freezing
        const int AskedAFrame = 4;                  // of the model's past hours, worked out a frame

        // (the place reckoned for: the world, where, the heights, up to when)
        CelestialBody body;
        double lat = double.NaN, lon, reckonedTo = double.NaN;
        float lowest, groundAlt;
        readonly float[] water = new float[Bands];     // mm of water lying as snow, at each height
        readonly float[] fresh = new float[Bands];     // how much of it is fresh, 0 to 1
        readonly float[] deep = new float[Bands];      // cm of snow
        // (a new place's past being worked out, a few hours a frame; drawn from the last place's meanwhile)
        bool reckoning;
        double reckonFrom, reckonAt;
        readonly float[] nextWater = new float[Bands], nextFresh = new float[Bands];
        double nextLat, nextLon;
        float nextLowest, nextGround;
        CelestialBody nextBody;
        float snowingNow;
        int chosenWas = -1;                         // (the weather held by the settings: another, and the past is another too)
        // (what working out the past costs: the most a frame, ms)
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        float reckonMost;

        static Shader shader;
        static bool tried;
        static string why = "";
        static Mesh sheet;
        static readonly int idScene = Shader.PropertyToID("_SnowScene");
        sealed class Pass { public Camera Cam; public CommandBuffer Draw; public Material Mat; public bool AskedDepth; public CameraEvent At; }
        // (after the solid things and before anything in the air; development builds can try it after the see-through things)
        static CameraEvent When = CameraEvent.AfterForwardOpaque;
        readonly Pass near = new Pass(), far = new Pass();

        // (the craft not to lay snow on: their boxes, in their own frames, looked over twice a second)
        sealed class Box { public Vessel Ship; public Vector3 Middle, Half; }
        readonly List<Box> boxes = new List<Box>();
        float boxesLooked = -10f;
        static readonly int[] craftIds = { Shader.PropertyToID("_SnowBox0X"), Shader.PropertyToID("_SnowBox1X"), Shader.PropertyToID("_SnowBox2X"), Shader.PropertyToID("_SnowBox3X") };
        static readonly int[] craftIdsY = { Shader.PropertyToID("_SnowBox0Y"), Shader.PropertyToID("_SnowBox1Y"), Shader.PropertyToID("_SnowBox2Y"), Shader.PropertyToID("_SnowBox3Y") };
        static readonly int[] craftIdsZ = { Shader.PropertyToID("_SnowBox0Z"), Shader.PropertyToID("_SnowBox1Z"), Shader.PropertyToID("_SnowBox2Z"), Shader.PropertyToID("_SnowBox3Z") };

#if DEV
        static float heldDeep = -1f;
        static bool showLight, showWhy;
        /// <summary>(development build) The snow held at so many centimetres at every height (less than nothing lets go); "light" draws the light measured against the light expected instead of the snow, "snow" the snow again.</summary>
        public static string Hold(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "light") { showLight = true; showWhy = false; return "the light measured shown"; }
            if (a == "why") { showWhy = true; showLight = false; return "why each pixel has no snow shown (blue sea, red steep, yellow thin, magenta craft, cyan beyond)"; }
            if (a == "snow") { showLight = showWhy = false; return "the snow shown"; }
            if (a == "alpha" || a == "opaque") { When = a == "alpha" ? CameraEvent.AfterForwardAlpha : CameraEvent.AfterForwardOpaque; return "drawn " + When; }
            heldDeep = float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1f;
            return heldDeep >= 0f ? "held at " + heldDeep + " cm" : "the snow is the weather's own";
        }
        public static string Says() => Report;
        /// <summary>(development build) The snow the last three days would leave at a place, "lat lon", reckoned there and then at once (not drawn).</summary>
        public static string At(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            CelestialBody b = FlightGlobals.currentMainBody;
            if (t.Length < 2 || b == null) return "give: lat lon";
            double la = double.Parse(t[0], System.Globalization.CultureInfo.InvariantCulture), lo = double.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture);
            World w = World.Of(b);
            float ground = (float)Math.Max(0.0, b.TerrainAltitude(la, lo));
            float low = Lowest(ground);
            var wa = new float[Bands]; var fr = new float[Bands];
            double now = Planetarium.GetUniversalTime();
            int snowing = 0;
            for (double ut = now - Back; ut < now; ut += 3600.0) { if (StepPlace(w, la, lo, ground, low, ut, 3600.0, wa, fr) > 0f) snowing++; }
            var say = new System.Text.StringBuilder("ground " + ground.ToString("F0") + " m; it snowed in " + snowing + " of the last 72 hours; snow at ");
            for (int k = 0; k < Bands; k++) say.Append((low + k * Step).ToString("F0")).Append(" m: ").Append(Depth(wa[k], fr[k]).ToString("F1")).Append(" cm").Append(k < Bands - 1 ? ", " : "");
            return say.ToString();
        }
#endif

        void Awake() => Camera.onPreCull += BeforeCamera;

        void OnDestroy()
        {
            Camera.onPreCull -= BeforeCamera;
            Off(near); Off(far);
        }

        static float Lowest(float ground) => Mathf.Max(0f, Mathf.Floor((ground - Step) / Step) * Step);

        /// <summary>
        /// One stretch of the weather at a place, from ut on for so many seconds, laid on the snow at each height: what fell as
        /// snow piled up, the warmth's melt taken away. What fell there now (0 to 1, as snow at the place's own height).
        /// </summary>
        static float StepPlace(World w, double la, double lo, float ground, float low, double ut, double seconds, float[] wa, float[] fr)
        {
            WeatherHere here = Model.Layers(w, la, lo, ut);
            lastTemperature = here.Temperature;
            float hours = (float)(seconds / 3600.0);
            // (what falls, as mm of water an hour; and how much warmer the place is than the model's air for its height there:
            // its sun on the land by day, its own cooling by night, which the heights round it share)
            float r = Mathf.Clamp01(here.Rain), falls = 50f * r * r;
            // (snow holds less water than rain of the same strength: heavy snow is some 2.5 to 5 mm of water an hour, heavy rain
            // some 10 to 16: a third)
            const float SnowWater = 0.33f;
            float offset = here.Temperature > 0f ? here.Temperature - w.Temperature(la, ground) : 0f;
            for (int k = 0; k < Bands; k++)
            {
                float t = w.Temperature(la, low + k * Step) + offset;
                float asSnow = Noise.Smooth(275.6f, 272.4f, t);
                float snowfall = falls * SnowWater * asSnow * hours, rainfall = falls * (1f - asSnow) * hours;
                float warm = Mathf.Max(0f, t - 273.15f);
                float melt = (MeltPerDegree * warm * hours + rainfall * warm / 80f);
                float before = wa[k];
                wa[k] = Mathf.Max(0f, before + snowfall - melt);
                // (fresh snow settles: the fresh share halves in a day and a half)
                float kept = before > 0f ? Mathf.Min(before, wa[k]) * fr[k] * Mathf.Exp(-hours / 52f) : 0f;
                fr[k] = wa[k] > 1e-4f ? Mathf.Clamp01((kept + Mathf.Min(snowfall, wa[k])) / wa[k]) : 0f;
            }
            return falls > 0.01f ? Noise.Smooth(275.6f, 272.4f, here.Temperature > 0f ? here.Temperature : 300f) * r : 0f;
        }

        static float lastTemperature;

        /// <summary>cm of snow for so many mm of water lying: fresh snow a tenth water, settled snow a third.</summary>
        static float Depth(float mm, float freshShare) => mm / (10f * (0.3f - 0.2f * freshShare));

        void LateUpdate()
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody b = FlightGlobals.currentMainBody;
            bool wanted = Weather.On != null && Weather.On.Value && Weather.LyingSnow != null && Weather.LyingSnow.Value && cam != null && b != null && b.atmosphere && !MapView.MapIsEnabled;
            // (Since 0.5.0: nothing of it worked out from above the air, where it is never drawn. From orbit the craft went 20 km every
            // ten seconds, and every frame under time warp, and the last three days at each new place were worked out again, four
            // hours a frame: 20 to 23 ms of every frame of a warp, the most of its hitches (2026-10-20).)
            if (wanted && b.GetAltitude(cam.transform.position) > b.atmosphereDepth) { Report = "above the air: no lying snow worked out"; Off(near); Off(far); return; }
            World w = wanted ? World.Of(b) : null;
            if (w == null) { Report = "no lying snow here"; Off(near); Off(far); return; }
            double now = Planetarium.GetUniversalTime();
            Vector3 camPos = cam.transform.position;
            double camLat = b.GetLatitude(camPos), camLon = b.GetLongitude(camPos);
            // ---- a new place (or world, or weather held by the settings): its past worked out, a few hours a frame
            bool again = Model.Chosen != chosenWas;
            chosenWas = Model.Chosen;
            bool moved = body != b || double.IsNaN(lat) || Model.Distance(w, lat, lon, camLat, camLon) > 20000.0;
            if (again || (moved && (!reckoning || nextBody != b || Model.Distance(w, nextLat, nextLon, camLat, camLon) > 20000.0)))
            {
                reckoning = true; nextBody = b; nextLat = camLat; nextLon = camLon;
                nextGround = (float)Math.Max(0.0, b.TerrainAltitude(camLat, camLon));
                nextLowest = Lowest(nextGround);
                reckonFrom = now - Back; reckonAt = reckonFrom;
                Array.Clear(nextWater, 0, Bands); Array.Clear(nextFresh, 0, Bands);
            }
            if (reckoning)
            {
                clock.Restart();
                for (int n = 0; n < AskedAFrame && reckonAt < now; n++, reckonAt += 3600.0)
                    StepPlace(World.Of(nextBody), nextLat, nextLon, nextGround, nextLowest, reckonAt, Math.Min(3600.0, now - reckonAt), nextWater, nextFresh);
                reckonMost = Mathf.Max(reckonMost, (float)clock.Elapsed.TotalMilliseconds);
                if (reckonAt >= now)
                {
                    reckoning = false; body = nextBody; lat = nextLat; lon = nextLon; groundAlt = nextGround; lowest = nextLowest; reckonedTo = now;
                    Array.Copy(nextWater, water, Bands); Array.Copy(nextFresh, fresh, Bands);
                    TemperatureHere = lastTemperature;
                }
            }
            // ---- brought on as the game's time goes: every ten minutes of it, in steps of no more than an hour (a few a frame
            // under time warp, where an hour is a moment)
            if (!double.IsNaN(reckonedTo) && body == b)
            {
                if (now < reckonedTo) reckonedTo = now;                    // (a save loaded from before)
                // (under time warp two a frame, not four: it is caught up when the warp ends)
                int asked = TimeWarp.CurrentRate > 1.01f ? 2 : AskedAFrame;
                for (int n = 0; n < asked && now - reckonedTo >= 600.0; n++)
                {
                    double span = Math.Min(3600.0, now - reckonedTo);
                    snowingNow = StepPlace(w, lat, lon, groundAlt, lowest, reckonedTo, span, water, fresh);
                    TemperatureHere = lastTemperature;
                    reckonedTo += span;
                }
            }
            float most = 0f;
            for (int k = 0; k < Bands; k++) { deep[k] = Depth(water[k], fresh[k]); most = Mathf.Max(most, deep[k]); }
            // (at the camera's own ground, between the heights reckoned for)
            float at = Mathf.Clamp((groundAlt - lowest) / Step, 0f, Bands - 1.001f);
            int below = (int)at;
            DeepHere = double.IsNaN(reckonedTo) ? 0f : Mathf.Lerp(deep[below], deep[below + 1], at - below);
            FreshHere = Mathf.Lerp(fresh[below], fresh[below + 1], at - below);
#if DEV
            if (heldDeep >= 0f) { for (int k = 0; k < Bands; k++) deep[k] = heldDeep; most = heldDeep; DeepHere = heldDeep; }
#endif
            var say = new System.Text.StringBuilder();
            say.Append(reckoning ? "working out the last three days here (" + Mathf.RoundToInt((float)((reckonAt - reckonFrom) / Back * 100.0)) + "%); " : "");
            say.Append(double.IsNaN(reckonedTo) ? "no place reckoned yet" : "snow lying, cm, at ");
            if (!double.IsNaN(reckonedTo))
                for (int k = 0; k < Bands; k++) say.Append((lowest + k * Step).ToString("F0")).Append(" m ").Append(deep[k].ToString("F1")).Append(k < Bands - 1 ? ", " : "");
            say.Append(snowingNow > 0.01f ? "; snowing" : "");
            say.Append("; working out a place's past took at most " + reckonMost.ToString("F1") + " ms a frame");
            if (shader == null && tried) say.Append(" (not drawn: " + why + ")");
            Report = say.ToString();

            // ---- the passes, while there is snow anywhere
            if (most < 0.05f || double.IsNaN(reckonedTo)) { Off(near); Off(far); return; }
            if (!tried) LoadShader();
            if (shader == null) { Off(near); Off(far); return; }
            On(near, cam);
            Camera farCam = Names.CameraNamed("Camera 01");
            if (farCam != null && farCam != cam) On(far, farCam); else Off(far);
            if (Time.unscaledTime - boxesLooked > 0.5f) { boxesLooked = Time.unscaledTime; LookForCraft(camPos); }
        }

        void On(Pass p, Camera cam)
        {
            if (p.Cam == cam && p.Draw != null && p.At == When) return;
            Off(p);
            p.Cam = cam;
            if (p.Mat == null) p.Mat = new Material(shader) { name = "Natural Weather: lying snow" };
            p.Draw = new CommandBuffer { name = "Natural Weather: lying snow" };
            if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; p.AskedDepth = true; }
            p.At = When;
            cam.AddCommandBuffer(p.At, p.Draw);
        }

        static void Off(Pass p)
        {
            if (p.Cam != null && p.Draw != null) p.Cam.RemoveCommandBuffer(p.At, p.Draw);
            if (p.Cam != null && p.AskedDepth) p.Cam.depthTextureMode &= ~DepthTextureMode.Depth;
            if (p.Draw != null) p.Draw.Release();
            p.Draw = null; p.Cam = null; p.AskedDepth = false;
        }

        /// <summary>The craft near the camera, the nearest four: each one's box round everything drawn of it, in its own frame (and a hand's breadth more).</summary>
        void LookForCraft(Vector3 camPos)
        {
            boxes.Clear();
            var near_ = new List<Vessel>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded) if (v != null && v.loaded && (v.transform.position - camPos).sqrMagnitude < 3000f * 3000f) near_.Add(v);
            near_.Sort((a, b2) => (a.transform.position - camPos).sqrMagnitude.CompareTo((b2.transform.position - camPos).sqrMagnitude));
            foreach (Vessel v in near_)
            {
                if (boxes.Count >= craftIds.Length) break;
                Transform frame = v.transform;
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
                bool any = false;
                foreach (Part part in v.parts)
                {
                    if (part == null) continue;
                    foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
                    {
                        if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                        Bounds bb = r.bounds;
                        for (int c = 0; c < 8; c++)
                        {
                            Vector3 corner = bb.center + Vector3.Scale(bb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                            Vector3 l = frame.InverseTransformPoint(corner);
                            lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l);
                            any = true;
                        }
                    }
                }
                if (any) boxes.Add(new Box { Ship = v, Middle = 0.5f * (lo + hi), Half = 0.5f * (hi - lo) + Vector3.one * 0.15f });
            }
        }

        static void LoadShader()
        {
            tried = true;
            try
            {
                // (on OpenGL its own bundle, packed by hand; elsewhere, Direct3D on Windows, with the clouds' shaders, built by the
                // Unity editor from tools/unityshaders: until that is built with it, not drawn there)
                string system = Application.platform == RuntimePlatform.WindowsPlayer ? "windows" : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "mac";
                string path = Clouds.Folder + (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ? "snow.bundle" : "clouds-" + system + ".bundle");
                AssetBundle bundle = System.IO.File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
                if (bundle != null)
                {
                    foreach (Shader one in bundle.LoadAllAssets<Shader>()) if (one != null && one.name == "NaturalWeather/Snow" && one.isSupported) shader = one;
                    bundle.Unload(false);
                }
                if (shader == null) why = bundle == null ? "its shader's file is not there" : SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ? "its shader would not load" : "its shader for " + SystemInfo.graphicsDeviceType + " is not built yet";
            }
            catch (Exception ex) { shader = null; why = "its shader did not load (" + ex.Message + ")"; }
            Debug.Log("[Natural Weather] lying snow: " + (shader != null ? "shader loaded" : "not drawn: " + why));
            if (sheet == null)
            {
                sheet = new Mesh { name = "Natural Weather: lying snow over the picture" };
                sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
                sheet.triangles = new[] { 0, 1, 2 };
                sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
        }

        /// <summary>Just before a camera draws (the game moves its flight camera after LateUpdate): the pass written out for where it then stands and looks.</summary>
        void BeforeCamera(Camera cam)
        {
            Pass p = cam != null && cam == near.Cam ? near : cam != null && cam == far.Cam ? far : null;
            if (p == null || p.Draw == null || p.Mat == null || body == null) return;
            CelestialBody b = body;
            Material m = p.Mat;
            Transform t = cam.transform;
            Vector3 camPos = t.position;
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
            Vector3 r = t.right * tanX, u = t.up * tanY, f = t.forward;
            Vector3d camRel = (Vector3d)camPos - b.position;
            // (the ground's own two level ways, at the place reckoned for: the patches are laid out along them)
            Vector3d anchor = b.GetWorldSurfacePosition(lat, lon, 0.0);
            Provider.Axes(b, anchor, out Vector3d east, out Vector3d north, out Vector3d upThere);
            Vector3d from = (Vector3d)camPos - anchor;
            Provider.Axes(b, camPos, out Vector3d e, out Vector3d n, out Vector3d up);
            m.SetVector("_SnowRight", new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
            m.SetVector("_SnowUp", new Vector4(u.x, u.y, u.z, 0f));
            m.SetVector("_SnowAhead", new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
            m.SetVector("_SnowCam", new Vector4((float)camRel.x, (float)camRel.y, (float)camRel.z, (float)b.Radius));
            float showLight_ = 0f;
#if DEV
            if (showLight) showLight_ = 1f;
            if (showWhy) showLight_ = 2f;
#endif
            // (kept small, as the shader reads its patches by it: 20 km of drift taken off at a time, the pattern unbroken)
            double ax = Vector3d.Dot(from, east), ay = Vector3d.Dot(from, north);
            ax -= Math.Floor(ax / 20000.0) * 20000.0; ay -= Math.Floor(ay / 20000.0) * 20000.0;
            m.SetVector("_SnowHere", new Vector4((float)ax, (float)ay, showLight_, b.ocean ? 1f : 0f));
            m.SetVector("_SnowAcross", (Vector3)east);
            m.SetVector("_SnowAlong", (Vector3)north);
            m.SetVector("_SnowDeep0", new Vector4(deep[0], deep[1], deep[2], deep[3]));
            m.SetVector("_SnowDeep1", new Vector4(deep[4], deep[5], deep[6], deep[7]));
            // (how far off it is drawn: all the near camera sees, to the end of its sight, where the far camera takes over with
            // no seam (it fades out only past its reach); the far camera's to 60 km, past which the ground seen is the game's
            // own far picture of the planet)
            bool isNear = p == near;
            float reach = isNear ? 1.1f * cam.farClipPlane : Mathf.Min(60000f, cam.farClipPlane);
            int given = 0;
            if (isNear)
                for (int k = 0; k < boxes.Count && k < craftIds.Length; k++)
                {
                    Box box = boxes[k];
                    if (box.Ship == null || box.Ship.transform == null) continue;
                    Matrix4x4 toBox = Matrix4x4.Scale(new Vector3(1f / box.Half.x, 1f / box.Half.y, 1f / box.Half.z)) * Matrix4x4.Translate(-box.Middle) * box.Ship.transform.worldToLocalMatrix * Matrix4x4.Translate(camPos);
                    // (as the rows of the change, three of them: the shader takes each box so)
                    m.SetVector(craftIds[given], toBox.GetRow(0)); m.SetVector(craftIdsY[given], toBox.GetRow(1)); m.SetVector(craftIdsZ[given], toBox.GetRow(2));
                    given++;
                }
            m.SetVector("_SnowBands", new Vector4(lowest, Step, reach, given));
            // ---- the light: the sun's on a white face turned to it, and the sky's on one turned up, as the picture has them
            // (as Rain's falling snow is lit: the sky from the game's own light probe, the sun as Natural Light leaves it)
            Light sun = global::Sun.Instance != null ? global::Sun.Instance.sunLight : null;
            Vector3 toSun = sun != null ? -sun.transform.forward : (Vector3)up;
            float high = Vector3.Dot(toSun, (Vector3)up);
            Color sunLight = sun != null && sun.isActiveAndEnabled ? sun.color * sun.intensity * Mathf.Clamp01(high * 12f + 0.3f) : Color.black;
            looks[0] = (Vector3)up;
            RenderSettings.ambientProbe.Evaluate(looks, seen);
            Color sky = Shown(Positive(seen[0])), sunShown = Shown(sunLight);
            m.SetVector("_SnowSun", new Vector4(toSun.x, toSun.y, toSun.z, Mathf.Clamp01(high * 12f + 0.3f) * Mathf.Clamp01(Clouds.SunThrough)));
            m.SetVector("_SnowSunLight", new Vector4(sunShown.r, sunShown.g, sunShown.b, 0f));
            m.SetVector("_SnowSkyLight", new Vector4(sky.r, sky.g, sky.b, 0f));
            p.Draw.Clear();
            p.Draw.GetTemporaryRT(idScene, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            p.Draw.Blit(BuiltinRenderTextureType.CameraTarget, idScene);
            p.Draw.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            p.Draw.DrawMesh(sheet, Matrix4x4.identity, m, 0, 0);
            p.Draw.ReleaseTemporaryRT(idScene);
        }

        static readonly Vector3[] looks = new Vector3[1];
        static readonly Color[] seen = new Color[1];
        static Color Positive(Color c) => new Color(Mathf.Max(0f, c.r), Mathf.Max(0f, c.g), Mathf.Max(0f, c.b));
        static Color Shown(Color light) => new Color(Mathf.LinearToGammaSpace(light.r), Mathf.LinearToGammaSpace(light.g), Mathf.LinearToGammaSpace(light.b));
    }
}
