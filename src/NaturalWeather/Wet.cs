using System;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// What rain leaves behind it: the ground wet (darker, which is done where the clouds' shadow is laid on; a mirror of
    /// the sky seen along it, and water standing on level ground, which are done here) and drops on the camera's glass.
    /// One pass over the near camera's finished picture (tools/shaderpack/wet.glsl), put on only while there is something
    /// for it to draw.
    ///
    /// How wet. The ground is wetted by rain in seconds (a heavy shower) to minutes (drizzle) and dries in a quarter of an
    /// hour; water gathers on level ground while it rains hard enough and drains and dries in ten minutes after. The
    /// glass gathers drops while the camera is out in rain, as many as the rain is heavy; they dry in half a minute, and
    /// are blown off at speed. All of it is kept for the place the camera is at; a camera that comes to a new place finds
    /// it as wet as the last twenty minutes' weather there made it.
    /// </summary>
    public sealed class Wet : MonoBehaviour
    {
        /// <summary>How wet the ground is where the camera is (0 to 1); how much of level ground stands under water; how much of the glass has drops on it.</summary>
        public static float Ground, Puddles, Glass;
        public static string Report = "";
#if DEV
        /// <summary>(development build) held by hand: less than nothing lets go.</summary>
        public static float HeldGround = -1f, HeldPuddles = -1f, HeldGlass = -1f;
        public static string Hold(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            float V(int i) => i < t.Length && float.TryParse(t[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1f;
            HeldGround = V(0); HeldPuddles = V(1); HeldGlass = V(2);
            return HeldGround < 0f && HeldGlass < 0f ? "the wet is the weather's own" : "held: ground " + HeldGround + ", puddles " + HeldPuddles + ", glass " + HeldGlass;
        }
        public static string Says() => Report;
        /// <summary>(development build) the drops on the glass: how many, how many running, how much of the glass is under water.</summary>
        public static string DropsSays() => instance != null ? instance.drops.Says() + "; rain at the glass " + instance.stepRain.ToString("F2") + ", landing " + instance.arriving.ToString("F1") + " a second (" + Mathf.RoundToInt(100f * instance.facingSaid) + "% facing the rain)" : "no drops: not in flight";
        /// <summary>(development build) the glass wiped.</summary>
        public static string WipeGlass() { if (instance != null) instance.drops.Clear(); return "wiped"; }
#endif

        static Shader shader;
        static bool tried;
        static Mesh sheet;
        static string why = "";
        static readonly int idScene = Shader.PropertyToID("_WetScene");
        Material material;
        CommandBuffer pass;
        Camera on;

        // (where the wet is known for, and when it was last brought up to date)
        string body = "";
        double lat = double.NaN, lon, at = double.NaN;
        // (the place the puddles' pattern is laid out from: fixed to the ground, moved only when the camera has gone far from it)
        double anchorLat = double.NaN, anchorLon;
        float rainNow;

        static void LoadShader()
        {
            tried = true;
            try
            {
                Shader[] found;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore) found = Load("wet.bundle");
                else
                {
                    // (elsewhere, Direct3D on Windows: with the clouds' shaders, built by the Unity editor from tools/unityshaders)
                    string system = Application.platform == RuntimePlatform.WindowsPlayer ? "windows" : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "mac";
                    found = Load("clouds-" + system + ".bundle");
                }
                if (found != null) foreach (Shader one in found) if (one != null && one.name == "NaturalWeather/Wet" && one.isSupported) shader = one;
                Shader[] own = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ? Load("drops.bundle") : found;
                if (own != null) foreach (Shader one in own) if (one != null && one.name == "NaturalWeather/Drops" && one.isSupported) dropShader = one;
                if (shader == null) why = found == null ? "its shader's file is not there" : "there is no shader for it on " + SystemInfo.graphicsDeviceType + " in this build";
            }
            catch (Exception ex) { shader = null; why = "its shader did not load (" + ex.Message + ")"; }
            Debug.Log("[Natural Weather] rain on the glass and the ground: " + (shader != null ? "shader loaded" : "not drawn: " + why));
            if (sheet == null)
            {
                sheet = new Mesh { name = "Natural Weather: over the picture" };
                sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
                sheet.triangles = new[] { 0, 1, 2 };
                sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
        }

        static Shader[] Load(string file)
        {
            string path = Clouds.Folder + file;
            if (!System.IO.File.Exists(path)) return null;
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) return new Shader[0];
            Shader[] found = bundle.LoadAllAssets<Shader>();
            bundle.Unload(false);
            return found;
        }

        void Off()
        {
            if (on != null && pass != null) on.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, pass);
            on = null;
        }

        void OnDestroy()
        {
            Camera.onPreCull -= BeforeCamera;
            if (instance == this) instance = null;
            Off();
            if (pass != null) { pass.Release(); pass = null; }
            if (material != null) Destroy(material);
            if (dropMaterial != null) Destroy(dropMaterial);
            drops.Dispose();
            Ground = Puddles = Glass = 0f;
        }

        /// <summary>The rain that reaches the ground at a place (0 to 1), water only: not snow, not ice pellets.</summary>
        static float RainAt(World w, double lat, double lon, double ut)
        {
            WeatherHere here = Model.Here(w, lat, lon, ut);
            return here.Rain * Mathf.Clamp01(1f - here.Snow - here.Pellets);
        }

        /// <summary>The ground's wet and its standing water carried on through so many seconds of so much rain.</summary>
        static void Carry(ref float ground, ref float puddles, float rain, double seconds)
        {
            // (in steps of no more than twenty seconds: under time warp a frame is minutes)
            int steps = Math.Max(1, (int)Math.Ceiling(seconds / 20.0));
            float dt = (float)(seconds / steps);
            for (int i = 0; i < steps; i++)
            {
                if (rain > 0.02f) { ground = Mathf.Min(1f, ground + rain * 0.08f * dt); puddles = Mathf.Min(1f, puddles + Mathf.Max(0f, rain - 0.12f) * 0.012f * dt); }
                else { ground *= Mathf.Exp(-dt / 900f); puddles *= Mathf.Exp(-dt / 600f); }
            }
        }

        void LateUpdate()
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody b = FlightGlobals.currentMainBody;
            bool flight = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && cam != null && b != null && !MapView.MapIsEnabled;
            bool weather = flight && b.atmosphere && Weather.On && Model.Allowed(b);
            if (!weather) { Ground = Puddles = Glass = 0f; at = double.NaN; Off(); Report = ""; return; }
            Vector3 camPos = cam.transform.position;
            double camLat = b.GetLatitude(camPos), camLon = b.GetLongitude(camPos), alt = b.GetAltitude(camPos);
            double ut = Planetarium.GetUniversalTime();
            World w = World.Of(b);

            // ---- the ground at this place: carried on from the last frame, or, at a place new to the camera, made from the
            // weather the place has had (ten looks back over twenty minutes)
            bool elsewhere = body != b.bodyName || double.IsNaN(lat) || Model.Distance(w, lat, lon, camLat, camLon) > 15000.0;
            if (elsewhere || double.IsNaN(at) || ut < at || ut - at > 7200.0)
            {
                Ground = Puddles = 0f;
                for (int k = 10; k >= 1; k--) Carry(ref Ground, ref Puddles, RainAt(w, camLat, camLon, ut - k * 120.0), 120.0);
                body = b.bodyName;
            }
            WeatherHere here = Model.Here(w, camLat, camLon, ut);
            float water = here.Rain * Mathf.Clamp01(1f - here.Snow - here.Pellets);
            if (!double.IsNaN(at) && ut > at && !elsewhere) Carry(ref Ground, ref Puddles, water, ut - at);
            lat = camLat; lon = camLon; at = ut; rainNow = water;

            // ---- the glass: drops while the camera is out in the rain (under the cloud it falls from, and not inside a craft)
            float under = here.Low.Cover > 0.05f ? Noise.Smooth(here.Low.Top, here.Low.Base, (float)alt) : 1f;
            bool outside = CameraManager.Instance == null || CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight;
            float falling = outside ? water * under : 0f;
            // (the drops themselves are carried on just before the camera draws, where it really is then: StepDrops)
            stepRain = falling;
#if DEV
            if (HeldGlass >= 0f) stepRain = HeldGlass;
#endif
            stepKeep = outside && Weather.Drops;
            if (!stepKeep) drops.Clear();
            stepWind = (Vector3)Keystone.Weather.Wind(b, (Vector3d)camPos, ut);
            Glass = drops.Covered;

            float ground = Weather.WetGround ? Ground : 0f, puddles = Weather.WetGround ? Puddles : 0f, glass = stepKeep && (stepRain > 0.005f || drops.Count > 0) ? 1f : 0f;
#if DEV
            if (HeldGround >= 0f) { ground = HeldGround; Ground = HeldGround; }
            if (HeldPuddles >= 0f) { puddles = HeldPuddles; Puddles = HeldPuddles; }
#endif
            Report = "ground " + Ground.ToString("F2") + ", standing water " + Puddles.ToString("F2") + ", drops on the glass " + drops.Count + " (" + drops.Running + " running, "
                + Mathf.RoundToInt(100f * Glass) + "% of it under water), air past the glass " + airSaid.ToString("F1") + " m/s"
                + (shader == null && tried ? " (not drawn: " + why + ")" : "");

            // ---- the pass, while there is anything for it
            bool wanted = glass > 0.5f || ground > 0.02f;
            if (wanted && !tried) LoadShader();
            if (!wanted || shader == null) { Off(); return; }
            if (material == null) material = new Material(shader) { name = "Natural Weather: rain on the glass and the ground" };
            if (pass == null) pass = new CommandBuffer { name = "Natural Weather: rain on the glass and the ground" };
            if (on != cam) { Off(); cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, pass); on = cam; }
            drawGround = ground; drawPuddles = puddles; drawGlass = glass; drawRain = rainNow * under;
        }

        float drawGround, drawPuddles, drawGlass, drawRain;
        // (the drops on the glass, one by one; and the camera's motion through the air, which drives them)
        readonly Drops drops = new Drops();
        static Shader dropShader;
        Material dropMaterial;
        Vector3d lastRel;
        bool followed;
        Vector3 camMoving, camShaken;
        static readonly int idDrops = Shader.PropertyToID("_WetDrops"), idDropsSize = Shader.PropertyToID("_WetDropsSize");


        // (what the drops' step needs from the frame's work, and the camera as it was last drawn)
        float stepRain, airSaid, arriving, facingSaid;
        bool stepKeep;
        Vector3 stepWind;
        int steppedFrame = -1;

        /// <summary>
        /// The drops carried on through the frame, from where the camera really is as it draws (the game moves its flight
        /// camera after this component's LateUpdate: read there, a camera held still by another mod read as moving at
        /// hundreds of metres a second, and every drop was blown off the glass).
        /// </summary>
        void StepDrops(Camera cam)
        {
            if (Time.frameCount == steppedFrame) return;
            steppedFrame = Time.frameCount;
            if (!stepKeep) { drops.Clear(); return; }
            Vector3 camPos = cam.transform.position;
            Vessel ship = FlightGlobals.ActiveVessel;
            float dtGame = Mathf.Min(Time.deltaTime, 0.05f);
            Transform t = cam.transform;
            // (the camera's own motion through the air: the craft's over the ground and the camera's round the craft. A jump of
            // more than ten metres in a frame is a cut, not a motion: the camera put somewhere else.)
            Vector3 moving = Vector3.zero;
            if (ship != null && Time.deltaTime > 1e-4f)
            {
                Vector3d rel = (Vector3d)camPos - ship.CoMD;
                if (followed && (rel - lastRel).sqrMagnitude < 100.0) moving = (Vector3)ship.srf_velocity + (Vector3)((rel - lastRel) / Time.deltaTime);
                else moving = ship.srf_velocity.sqrMagnitude < 1e10 ? (Vector3)ship.srf_velocity : Vector3.zero;
                lastRel = rel; followed = true;
            }
            else followed = false;
            Vector3 wasMoving = camMoving;
            camMoving = Vector3.Lerp(camMoving, moving, 1f - Mathf.Exp(-dtGame / 0.05f));
            if (dtGame > 1e-4f) camShaken = Vector3.Lerp(camShaken, (camMoving - wasMoving) / dtGame, 1f - Mathf.Exp(-dtGame / 0.1f));
            Vector3 wind = stepWind;
            Vector3 air = wind - camMoving;
            Vector3 g = (Vector3)FlightGlobals.getGeeForceAtPosition((Vector3d)camPos);
            Vector3 shaken = Vector3.ClampMagnitude(camShaken, 30f);
            float dense = ship != null ? (float)(ship.atmDensity / 1.225) : 1f;
            // (The rain as the glass meets it: so many drops a square metre a second at the ground (Marshall and Palmer's
            // sizes, at the rate the rain falls, 50 mm an hour for a cloudburst), falling at six and a half metres a second
            // with the wind, less the camera's own motion. The glass takes what comes at its face: looking up into the rain,
            // all of it; looking straight ahead in still air, next to none; looking down, or the way the wind drives the
            // rain, none; looking into the wind, or flying into the rain, more than looking up, as it comes at the glass
            // faster. And a very little besides, brought in by the eddies round the camera's body: a fiftieth of what
            // goes past its face.)
            float rainRate = 50f * stepRain;
            float flux = rainRate > 0.25f ? 1200f * Mathf.Pow(rainRate / 5f, 0.7f) : 0f;
            // (the game's gravity points down: the rain falls along it)
            Vector3 rainMeets = wind + g.normalized * 6.5f - camMoving;
            float facing = Mathf.Max(0f, -Vector3.Dot(rainMeets, t.forward)) / 6.5f;
            float area = Drops.Tall * 1e-3f * Drops.Tall * 1e-3f * cam.aspect;
            Vector3 past = rainMeets - t.forward * Vector3.Dot(rainMeets, t.forward);
            float arrive = flux * area * (facing + 0.02f * past.magnitude / 6.5f);
            arriving = arrive; facingSaid = facing;
            float lambda = 4.1f * Mathf.Pow(Mathf.Max(rainRate, 0.5f), -0.21f);
            drops.Step(dtGame, new Vector2(Vector3.Dot(g, t.right), Vector3.Dot(g, t.up)), new Vector2(Vector3.Dot(air, t.right), Vector3.Dot(air, t.up)),
                            Mathf.Max(0f, -Vector3.Dot(air, t.forward)), new Vector2(Vector3.Dot(shaken, t.right), Vector3.Dot(shaken, t.up)), dense, arrive, lambda,
                            stepRain > 0.01f ? 0f : 1f, cam.aspect);
            airSaid = new Vector2(Vector3.Dot(air, t.right), Vector3.Dot(air, t.up)).magnitude;
            Glass = drops.Covered;
        }

        static Wet instance;
        void Awake() { instance = this; Camera.onPreCull += BeforeCamera; }

        /// <summary>
        /// Just before the camera draws: the pass written out for where the camera then stands and looks. (The game moves
        /// its flight camera after this component's LateUpdate: written there, the ground's shine and its pools were
        /// laid out for the camera of the frame before, and swam over the ground whenever the camera turned.)
        /// </summary>
        void BeforeCamera(Camera cam)
        {
            if (cam == null || cam != on || pass == null || material == null) return;
            StepDrops(cam);
            CelestialBody b = FlightGlobals.currentMainBody;
            if (b == null) return;
            World w = World.Of(b);
            Vector3 camPos = cam.transform.position;
            double camLat = b.GetLatitude(camPos), camLon = b.GetLongitude(camPos);
            float ground = drawGround, puddles = drawPuddles, glass = drawGlass;
            Transform t = cam.transform;
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
            Vector3 r = t.right * tanX, u = t.up * tanY, f = t.forward;
            Vector3d camRel = (Vector3d)camPos - b.position;
            // (the ground's own two level ways, at a place fixed to the ground: the pattern of the hollows is read along them)
            if (double.IsNaN(anchorLat) || Model.Distance(w, anchorLat, anchorLon, camLat, camLon) > 3000.0) { anchorLat = camLat; anchorLon = camLon; }
            Vector3d anchor = b.GetWorldSurfacePosition(anchorLat, anchorLon, 0.0);
            Provider.Axes(b, anchor, out Vector3d east, out Vector3d north, out Vector3d upThere);
            Vector3d from = (Vector3d)camPos - anchor;
            Provider.Axes(b, camPos, out Vector3d e, out Vector3d n, out Vector3d up);
            Vector3 down = -(Vector3)up;
            Vector2 downOnGlass = new Vector2(Vector3.Dot(down, t.right), Vector3.Dot(down, t.up));
            // (the sky as a mirror shows it where nothing better is known: as bright as a white thing is in the light there is, greyed)
            Color lit = Rain.SnowLit * 0.7f;
            material.SetVector("_WetRight", new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
            material.SetVector("_WetUp", new Vector4(u.x, u.y, u.z, Time.time % 3600f));
            material.SetVector("_WetAhead", new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
            material.SetVector("_WetCam", new Vector4((float)camRel.x, (float)camRel.y, (float)camRel.z, (float)b.Radius));
            material.SetVector("_WetHere", new Vector4((float)Vector3d.Dot(from, east), (float)Vector3d.Dot(from, north), 0f, b.ocean ? 1f : 0f));
            material.SetVector("_WetAcross", (Vector3)east);
            material.SetVector("_WetAlong", (Vector3)north);
            // (How far off the wet look fades out: 600 m, and never past where this camera's own sight ends. On OpenGL the
            // game draws the flight scene with two cameras, and this one sees 400 m: with 600 m the sheen and the pools
            // stopped at a ruled line across the ground where the other camera takes over.)
            material.SetVector("_WetGround", new Vector4(ground, puddles, drawRain, Mathf.Min(600f, 0.95f * cam.farClipPlane)));
            material.SetVector("_WetSky", new Vector4(lit.r, lit.g, lit.b, 1.2f));
            material.SetVector("_WetGlass", new Vector4(glass, cam.aspect, downOnGlass.x, downOnGlass.y));
            // (where the brightest light lies on the glass: towards the sun while it is up and comes through, else straight up)
            CelestialBody sunBody = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            Vector3 toSun = sunBody != null ? (Vector3)(sunBody.position - (Vector3d)camPos).normalized : (Vector3)up;
            float sunUp = Vector3.Dot(toSun, (Vector3)up);
            float sunStrong = Mathf.Clamp01(sunUp * 4f + 0.2f) * Mathf.Clamp01(Clouds.SunThrough);
            Vector2 sunOnGlass = new Vector2(Vector3.Dot(toSun, t.right), Vector3.Dot(toSun, t.up));
            Vector2 upOnGlass = -downOnGlass;
            Vector2 light = Vector2.Lerp(upOnGlass.sqrMagnitude > 1e-4f ? upOnGlass.normalized : Vector2.up, sunOnGlass.sqrMagnitude > 1e-4f ? sunOnGlass.normalized : Vector2.up, sunStrong);
            light = light.sqrMagnitude > 1e-4f ? light.normalized : Vector2.up;
            material.SetVector("_WetSun", new Vector4(light.x, light.y, sunStrong, 0f));
            pass.Clear();
            // (the drops first, into their own picture of how high the water stands: half the picture's size)
            bool dropsDrawn = drops.Count > 0 && dropShader != null;
            if (dropsDrawn)
            {
                if (dropMaterial == null) dropMaterial = new Material(dropShader) { name = "Natural Weather: drops on the glass" };
                drops.Draw(pass, dropMaterial, cam.aspect, Mathf.Clamp(cam.pixelHeight / 2, 180, 600), 1f / 60f);
                material.SetTexture(idDrops, drops.Picture);
                material.SetVector(idDropsSize, new Vector4(1f / drops.Picture.width, 1f / drops.Picture.height, Drops.Tall / drops.Picture.height, 1f));
            }
            else material.SetVector(idDropsSize, Vector4.zero);
            pass.GetTemporaryRT(idScene, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            pass.Blit(BuiltinRenderTextureType.CameraTarget, idScene);
            pass.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            pass.DrawMesh(sheet, Matrix4x4.identity, material, 0, 0);
            pass.ReleaseTemporaryRT(idScene);
        }
    }
}
