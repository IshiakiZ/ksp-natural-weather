using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// Lightning: the flashes of the storms near the camera, drawn, lit and heard; and strikes on craft.
    ///
    /// When each storm flashes is part of the weather (worked out from the storm and the clock, so a flash is where and when
    /// it is for every mod): a strong storm flashes several times a minute, most flashes within the cloud, some to the
    /// ground. Each is announced through Keystone's Weather (Flashed), so that what draws clouds can light them from inside.
    /// Here: a flash to the ground is drawn as a crooked, branching channel (the stepped leader's path), thin and white, that
    /// flickers through its strokes in a fifth of a second to half a second; the scene is lit from where it is for as long;
    /// and the thunder is made from the channel's own shape (each length of it heard after its own distance divided by the
    /// speed of sound, which is the rumble: Ribner and Roy, 1982), and comes as late as the distance makes it. A craft in a
    /// storm, or climbing through cold cloud near one with its engines running (lightning the rocket sets off itself, as
    /// struck Apollo 12 and Atlas-Centaur 67), can be struck, if that is allowed.
    /// </summary>
    public sealed class Lightning : MonoBehaviour
    {
        public static string Report = "";

        sealed class Bolt
        {
            public WeatherFlash F;
            public CelestialBody Body;
            public float Born;                 // realtime it began
            public int BornFrame;              // and the frame
            public float[] Strokes;            // when each stroke comes, seconds after Born
            public readonly List<Vector3> Points = new List<Vector3>();       // in metres east, north, up from its foot
            public readonly List<int> From = new List<int>();                 // the point each one hangs from (-1 none)
            public readonly List<float> Weight = new List<float>();           // how bright each length is, 1 the main channel
            public bool OnCraft;               // a strike on a craft,
            public Vector3 Through;            // and where on the craft the channel meets it, from the foot (east, north, up)
            public GameObject Go;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool Drawn;
            public float Distance;
            public float Under = float.MaxValue;   // how far above its foot the cloud it comes out of begins (no cloud: no limit)
            public bool Front;                     // drawn by the clouds after themselves (seen from under that cloud)
            public Vector3 Origin;                 // its foot in the scene, this frame
        }

        readonly List<WeatherFlash> pending = new List<WeatherFlash>();
        readonly List<Bolt> bolts = new List<Bolt>();
        readonly List<float> lately = new List<float>();
        long lastSlot = long.MinValue;
        Material core, glow;
        Texture2D soft;
        // (How a flash lights the scene, see Lit: 3, in the even light of each camera that draws the scene, as built since
        // 2026-11-10. The development build's LightAs changes it, and only there are these two lights of Unity's made, for
        // films of how it was before: a light from the way of the flash and a lamp at a near strike's foot.)
        Light flashLight, nearLight;
        static int lightAs = 3;
        static bool lightDark = false;
        static float lightGain = 1f;
        // (for the development build's films of how it was: a flash's light as strong by day as by night)
        static bool lightAllDay = false;
        // (how much of a flash's light showed for the daylight at the last flash, see ByDay)
        internal static float dayShare = 1f;
        // (how much of a flash's light comes from all round as well as from its own way, see Lit)
        static float lightRound = 0.125f;
        static readonly Color flashColour = new Color(0.84f, 0.88f, 1f);
        const int litLayers = ~((1 << 5) | (1 << 9) | (1 << 10) | (1 << 16) | (1 << 20));
        // (the flash's light this frame: how strong, and the way to it; and what a camera's even light was before it was added)
        float litNow;
        Vector3 litFrom = Vector3.up;
        int litMask = litLayers;
        bool litSet;
        SphericalHarmonicsL2 litWas;
        AmbientMode litMode;
        readonly List<AudioSource> voices = new List<AudioSource>();
        float strikeCheck;
        float sasBackAt = -1f;
        bool sasWas;
        Vessel sasOf;
        float nearestKm = -1f;

        void Start()
        {
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
            if (add != null)
            {
                soft = Soft();
                core = new Material(add) { mainTexture = soft, renderQueue = 3150 };
                glow = new Material(add) { mainTexture = soft, renderQueue = 3150 };
            }
            // (what the sun lights, a flash lights)
            litMask = Sun.Instance != null && Sun.Instance.sunLight != null ? Sun.Instance.sunLight.cullingMask : litLayers;
#if DEV
            flashLight = MakeLight("NaturalWeather flash", LightType.Directional, litMask);
            nearLight = MakeLight("NaturalWeather strike", LightType.Point, litMask);
            nearLight.range = 2500f;
            nearLight.renderMode = LightRenderMode.ForceVertex;
#endif
            Clouds.InFront += InFront;
            Camera.onPreRender += Lit;
            Camera.onPostRender += Unlit;
        }

#if DEV
        static Light MakeLight(string name, LightType type, int mask)
        {
            var go = new GameObject(name);
            Light l = go.AddComponent<Light>();
            l.type = type; l.intensity = 0f; l.enabled = false; l.shadows = LightShadows.None;
            l.color = flashColour;
            l.cullingMask = mask;
            return l;
        }
#endif

        /// <summary>
        /// A flash's light, given to a camera that is about to draw the scene: added to the even light the camera gives
        /// everything it draws (the ambient probe: nine numbers a colour, which say how much light comes from each way), and
        /// taken out again when the camera has drawn (Unlit). The light comes from the way of the flash, as a far light's does,
        /// with an eighth as much from all round as well (the sky and the ground a flash lights up), so that no face is
        /// given less than nothing: by itself, a light from one way written in nine numbers gives the faces turned 100 to 150
        /// degrees from it a little less than nothing (a twenty-seventh of the light at the worst), and that would be taken
        /// off whatever else lights them.
        ///
        /// Why not a light of Unity's, as until 2026-11-10 (a far light from the way of the flash, and a lamp at the foot of a
        /// strike within 3 km): at night on the launch pad under Natural Light's floodlights both made the pad DARKER while
        /// they shone (filmed, the picture's lower half measured in levels of 255, about 23 before a flash). The lamp took the
        /// place of one of the four lamps Unity lights each thing by, and two of the pad's walls lost a floodlight for as long
        /// as the flash lasted: 29 levels darker over a third of the lower half, 6 on the whole, a strike 2.5 km off with only
        /// the lamp lit. The far light alone, a strike 3.5 km ahead: 1.3 to 1.6 darker on the whole (the faces turned sideways
        /// to it darker, as above; a tenth of it shone from the far side as well did not mend it). In the even light a flash
        /// only ever adds: strikes 3.5, 2.5, 1 and 0.3 km ahead and 0.5 km behind, no frame darker than before.
        /// </summary>
        void Lit(Camera cam)
        {
            if (litNow <= 0f || litSet || cam == null || (cam.cullingMask & litMask) == 0) return;
            litWas = RenderSettings.ambientProbe; litMode = RenderSettings.ambientMode; litSet = true;
            SphericalHarmonicsL2 with = EvenNow(litMode);
            with.AddDirectionalLight(litFrom, flashColour, litNow);
            with.AddAmbientLight(flashColour * (litNow * lightRound));
            if (litMode != AmbientMode.Custom) RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = with;
        }

        /// <summary>
        /// The even light as nine numbers, whatever way it is set. Set as a probe (by a mod: Natural Light's) or from the sky,
        /// it is the probe. Set as the game sets it by itself, one flat colour, the probe is NOT that colour: it is whatever
        /// probe was last set, by anybody (measured 2026-11-10 with Natural Light off: the flat colour 0.24, the probe read back
        /// 0.013, and a faint flash added to that made the pad 5 levels of 255 darker). So from the colours then, as light:
        /// a probe's numbers are amounts of light, whichever way the game keeps its colours, and the colours are as shown (the
        /// flat colour put in as it stands made the pad 13 levels brighter than the game's own flat light does; put in as
        /// light it matched the game's own flat light within 0.04 of a level, filmed). The three-colour way is not one the
        /// game or any mod of ours sets: its branch is a near guess, never tried.
        /// (Rain and Snow read the even light through this too, since 2026-11-10: with Natural Light off from the game's
        /// start the probe read back black, and the rain, the falling snow and the dust were lit by nothing, the lying snow
        /// by no sky.)
        /// </summary>
        internal static SphericalHarmonicsL2 EvenNow(AmbientMode mode)
        {
            if (mode == AmbientMode.Custom || mode == AmbientMode.Skybox) return RenderSettings.ambientProbe;
            var p = new SphericalHarmonicsL2();
            if (mode == AmbientMode.Trilight)
            {
                // (three colours, for faces turned up, sideways and down: near enough the middle one from all round, and what
                // the other two differ from it by from straight above and below)
                Color sky = RenderSettings.ambientSkyColor, mid = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;
                sky = sky.linear; mid = mid.linear; ground = ground.linear;
                p.AddAmbientLight(mid);
                p.AddDirectionalLight(Vector3.up, sky - mid, 1f);
                p.AddDirectionalLight(Vector3.down, ground - mid, 1f);
            }
            else
            {
                p.AddAmbientLight(RenderSettings.ambientLight.linear);
            }
            return p;
        }

        void Unlit(Camera cam)
        {
            if (!litSet) return;
            litSet = false;
            RenderSettings.ambientProbe = litWas;
            if (RenderSettings.ambientMode != litMode) RenderSettings.ambientMode = litMode;
        }

        void OnDestroy()
        {
            Clouds.InFront -= InFront;
            Camera.onPreRender -= Lit;
            Camera.onPostRender -= Unlit;
            Unlit(null);
            foreach (Bolt b in bolts) Drop(b);
            bolts.Clear();
            if (flashLight != null) Destroy(flashLight.gameObject);
            if (nearLight != null) Destroy(nearLight.gameObject);
            foreach (AudioSource a in voices) if (a != null) { if (a.clip != null) Destroy(a.clip); Destroy(a.gameObject); }
            if (core != null) Destroy(core);
            if (glow != null) Destroy(glow);
            if (soft != null) Destroy(soft);
            if (sasBackAt > 0f && sasOf != null) sasOf.ActionGroups.SetGroup(KSPActionGroup.SAS, sasWas);
        }

        void Update()
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            CelestialBody body = WeatherDriver.Here();
            double ut = Planetarium.GetUniversalTime();
            bool on = Weather.On && cam != null && body != null && body.atmosphere && Model.Allowed(body);
            // (none of the weather's own storms' flashes under Blackrack's clouds, drawn in place of these: his storms are not
            // where these storms are, and his own lightning flashes in his; a craft can still be struck in his, see Strikes)
            if (on && !HisSky.On) Schedule(body, cam.transform.position, ut);
            else lastSlot = long.MinValue;
            // the flashes whose moment has come
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Time > ut) continue;
                WeatherFlash f = pending[i];
                pending.RemoveAt(i);
                if (ut - f.Time > 3.0) continue;                   // (left behind by a jump in time)
                Begin(f, cam, Vector3.zero, false);
            }
            Animate(cam);
            if (HighLogic.LoadedSceneIsFlight && on) Strikes(body, ut);
            if (sasBackAt > 0f && Time.time > sasBackAt) { if (sasOf != null) sasOf.ActionGroups.SetGroup(KSPActionGroup.SAS, sasWas); sasBackAt = -1f; sasOf = null; }
            lately.RemoveAll(t => Time.realtimeSinceStartup - t > 60f);
            Report = lately.Count > 0 ? lately.Count + (lately.Count == 1 ? " flash" : " flashes") + " in the last minute" + (nearestKm >= 0f ? ", the nearest " + nearestKm.ToString("F0") + " km off" : "") + "." : "";
        }

        // ---------------------------------------------------------------- when the storms flash

        /// <summary>The storms near the camera, for the slots of two seconds since last time: in each, a strong storm flashes with a chance of a few in ten.</summary>
        void Schedule(CelestialBody body, Vector3 camPos, double ut)
        {
            long slot = (long)Math.Floor(ut / 2.0);
            if (lastSlot == long.MinValue || slot - lastSlot > 15 || TimeWarp.CurrentRate > 4f) { lastSlot = slot; return; }
            if (slot == lastSlot) return;
            World w = World.Of(body);
            double lat = body.GetLatitude(camPos), lon = body.GetLongitude(camPos);
            List<WeatherCell> cells = Model.RegionCells(w, lat, lon, ut);
            for (long s = lastSlot + 1; s <= slot; s++)
                foreach (WeatherCell c in cells)
                {
                    if (!c.Thunder || c.Strength < 0.25f) continue;
                    float perMinute = 8f * c.Strength * c.Strength * w.Climate.Lightning;
                    uint h = Noise.Hash((uint)c.Id ^ Noise.Hash((uint)s * 0x9e3779b1U));
                    if (Noise.Unit(h) > perMinute * 2f / 60f) continue;
                    pending.Add(Make(body, w, c, s * 2.0 + 2.0 * Noise.Unit(Noise.Hash(h ^ 0x51u)), h));
                }
            lastSlot = slot;
        }

        internal static WeatherFlash Make(CelestialBody body, World w, WeatherCell c, double time, uint h)
        {
            float angle = 6.2831853f * Noise.Unit(Noise.Hash(h ^ 0x11u)), reach = c.Radius * 0.7f * Noise.Sqrt(Noise.Unit(Noise.Hash(h ^ 0x12u)));
            double dn = reach * Math.Cos(angle), de = reach * Math.Sin(angle);
            double lat = c.Latitude + dn / w.Radius * 180.0 / Math.PI, lon = c.Longitude + de / (w.Radius * Math.Max(0.05, Math.Cos(c.Latitude * Math.PI / 180.0))) * 180.0 / Math.PI;
            // (to the ground only where there is ground: on Jool every flash is within the cloud. Until 0.2.2 three in ten of a strong
            // storm's flashes there went down a channel to the height the model takes for ground where it knows none, 100 m.)
            bool ground = w.Grounded && Noise.Unit(Noise.Hash(h ^ 0x13u)) < 0.3f * c.Strength + 0.05f;
#if DEV
            if (ground) madeToGround++; else madeInCloud++;
#endif
            float groundH = Math.Max(0f, w.Ground(lat, lon));
            return new WeatherFlash
            {
                Id = (int)(h & 0x7fffffff), Body = body, Latitude = lat, Longitude = lon,
                Bottom = ground ? groundH : c.Base + 1500f, Top = c.Base + (c.Top - c.Base) * (0.45f + 0.25f * Noise.Unit(Noise.Hash(h ^ 0x14u))),
                Ground = ground, Time = time, Brightness = 0.6f + 0.8f * Noise.Unit(Noise.Hash(h ^ 0x15u)),
                Strokes = ground ? 1 + (int)(Noise.Unit(Noise.Hash(h ^ 0x16u)) * 4.99f) : 1 + (int)(Noise.Unit(Noise.Hash(h ^ 0x17u)) * 2.5f), Seed = (int)h,
            };
        }

        // ---------------------------------------------------------------- a flash

        void Begin(WeatherFlash f, Camera cam, Vector3 strikeAt, bool onCraft, bool onward = false)
        {
            Keystone.Weather.Flash(f);
            lately.Add(Time.realtimeSinceStartup);
            if (cam == null || f.Body == null) return;
            Vector3d foot = f.Body.GetWorldSurfacePosition(f.Latitude, f.Longitude, f.Bottom);
            float dist = (float)((Vector3d)cam.transform.position - foot).magnitude;
            nearestKm = nearestKm < 0f || dist / 1000f < nearestKm || lately.Count <= 1 ? dist / 1000f : nearestKm;
            if (dist > 80000f) return;
            var b = new Bolt { F = f, Body = f.Body, Born = Time.realtimeSinceStartup, BornFrame = Time.frameCount, Distance = dist };
            b.Strokes = new float[Math.Max(1, f.Strokes)];
            var rng = new System.Random(f.Seed);
            float t = 0f;
            for (int k = 0; k < b.Strokes.Length; k++) { b.Strokes[k] = t; t += 0.035f + 0.06f * (float)rng.NextDouble(); }
            b.OnCraft = onCraft; b.Through = onCraft ? strikeAt : Vector3.zero;
            Channel(b, rng, f.Top - f.Bottom, f.Ground, onCraft, strikeAt, onward);
            if (!onCraft) b.Under = CloudUnder(f);
            bool draw = Weather.Flashes && core != null && (f.Ground || onCraft);
            if (draw)
            {
                b.Go = new GameObject("NaturalWeather bolt");
                b.Go.layer = 0;
                b.Mesh = new Mesh { name = "bolt" };
                b.Mesh.MarkDynamic();
                var mf = b.Go.AddComponent<MeshFilter>(); mf.sharedMesh = b.Mesh;
                var mr = b.Go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { glow, core };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                b.Renderer = mr;
                b.Drawn = true;
            }
            bolts.Add(b);
            if (Weather.Thunder > 0.01f && TimeWarp.CurrentRate <= 1.01f) Thunder(b, cam.transform.position, foot);
        }

        /// <summary>
        /// The channel: from the cloud down to the ground (or, for a flash within the cloud, across it), stepping crookedly,
        /// with side branches that stop in mid-air. A strike on a craft comes down to the craft (strikeAt, from the foot), and,
        /// if the craft is in the air (onward), goes on from it to the ground. Since 2026-11-10 the steps grow shorter toward
        /// where the channel comes down (the ground, or the craft), down to two metres: until then every step was a fifty-fifth
        /// of the channel's height, tens of metres, and a strike seen from 20 m was a ruler-straight bar (filmed).
        /// </summary>
        static void Channel(Bolt b, System.Random rng, float height, bool ground, bool onCraft, Vector3 strikeAt, bool onward)
        {
            height = Mathf.Max(300f, height);
            b.Points.Add(new Vector3(0f, 0f, height)); b.From.Add(-1); b.Weight.Add(1f);
            float step = height / 55f;
            if (onCraft)
            {
                Walk(b, rng, 0, strikeAt, step, 1f, 2, true, strikeAt);
                if (onward && strikeAt.z > 4f) Walk(b, rng, b.Points.Count - 1, Vector3.zero, step, 1f, 1, true, strikeAt);
                return;
            }
            Vector3 target = ground ? Vector3.zero : new Vector3((float)(rng.NextDouble() - 0.5) * height * 2f, (float)(rng.NextDouble() - 0.5) * height * 2f, height * 0.7f);
            Walk(b, rng, 0, target, step, 1f, 2, ground, target);
        }

        /// <summary>(fine: the steps no longer than a fifth of the way to `about`, two metres at the least; its branches' too.)</summary>
        static void Walk(Bolt b, System.Random rng, int from, Vector3 target, float step, float weight, int depth, bool fine = false, Vector3 about = default(Vector3))
        {
            Vector3 at = b.Points[from];
            int last = from;
            int guard = 0;
            float full = step;
            while (guard++ < 400)
            {
                if (fine) step = Mathf.Min(full, Mathf.Max(2f, (about - at).magnitude * 0.2f));
                if ((target - at).magnitude <= step) break;
                Vector3 toward = (target - at).normalized;
                Vector3 kink = new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1) * 0.5f);
                Vector3 dir = (toward + kink * 0.75f).normalized;
                float len = step * (0.5f + (float)rng.NextDouble());
                at += dir * len;
                b.Points.Add(at); b.From.Add(last); b.Weight.Add(weight);
                last = b.Points.Count - 1;
                // (a branch as often by the metre whatever the step: a short step is so much the less likely to bear one)
                if (depth > 0 && rng.NextDouble() < 0.07 * step / full)
                {
                    // a branch: off to one side and down, for a stretch, and it stops
                    Vector3 side = new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), -0.6f - (float)rng.NextDouble()).normalized;
                    float run = (at.z) * (0.15f + 0.35f * (float)rng.NextDouble());
                    Walk(b, rng, last, at + side * run, full * 0.8f, weight * 0.35f, depth - 1, fine, about);
                }
            }
            b.Points.Add(target); b.From.Add(last); b.Weight.Add(weight);
        }

        /// <summary>How bright a flash is at a moment after it began: a sharp burst for each stroke, and a dim glow between them while the current goes on.</summary>
        static float Envelope(Bolt b, float t)
        {
#if DEV
            // (a flash held at its brightest for so long, for a picture of it: a stroke is over in a fiftieth of a second)
            if (t < heldFor || Time.frameCount - b.BornFrame < heldFrames) return b.F.Brightness;
#endif
            float v = 0f;
            for (int k = 0; k < b.Strokes.Length; k++)
            {
                float dt = t - b.Strokes[k];
                if (dt < 0f) continue;
                v = Mathf.Max(v, Mathf.Exp(-dt / 0.018f) * (k == 0 ? 1f : 0.75f));
            }
            float end = b.Strokes[b.Strokes.Length - 1] + 0.08f;
            if (t < end) v = Mathf.Max(v, 0.12f);
            else v = Mathf.Max(v, 0.12f * Mathf.Exp(-(t - end) / 0.05f));
            return v * b.F.Brightness;
        }

        /// <summary>How bright the channel itself still shows at a moment after the flash began: each stroke's glow dying away over about a tenth of a second.</summary>
        static float Lingers(Bolt b, float t)
        {
            float v = 0f;
            for (int k = 0; k < b.Strokes.Length; k++)
            {
                float dt = t - b.Strokes[k];
                if (dt < 0f) continue;
                v = Mathf.Max(v, Mathf.Exp(-dt / 0.09f) * (k == 0 ? 1f : 0.75f));
            }
            return v * b.F.Brightness;
        }

        /// <summary>
        /// How much of a flash's light shows on the scene for the daylight there is: all of it by night, a seventeenth with
        /// the sun up. A camera set for the day, and an eye used to it, takes in thousands of times less light than one set
        /// for the night, and this game shows a night and a day at much the same levels on the screen: a flash that adds the
        /// same light to both can be right for one of them only, and it was made for the night. Filmed 2026-11-10 with the sun
        /// up under a storm, the lower half of the picture 50 of 255 before: a strike 3.5 km ahead raised it by 34, one 1 km
        /// ahead by 84, and the whole field went pale for as long as the flash lasted. In film of real strikes by day the land
        /// does not change at such distances: the channel is seen, and only a strike a few hundred metres off or nearer
        /// brightens the picture, for a frame or two. Reckoned roughly (the light a stroke gives off, spread over a frame of a
        /// film, against a few thousand lux under a storm cloud by day): a tenth more light at 1 km, as much again at 300 m.
        /// A seventeenth is more than that, so that a near strike is still felt. Filmed after, the lower half 61 before: the
        /// strike 3.5 km ahead raised it by 4, the one 1 km ahead by 13, one 0.3 km ahead by 14, one on the craft by 87.
        /// The channel, and the clouds lit from within, are as they were.
        /// </summary>
        static float ByDay(Camera cam)
        {
            const float Share = 0.06f;
            CelestialBody body = FlightGlobals.currentMainBody, sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (cam == null || body == null || sun == null || body == sun || lightAllDay) return dayShare = 1f;
            Vector3d at = cam.transform.position;
            float sunUp = (float)Vector3d.Dot((at - body.position).normalized, (sun.position - at).normalized);
            // (from 8 degrees under the horizon, the dusk all but gone, to 4 above it)
            return dayShare = Mathf.Pow(Share, Noise.Smooth(-0.14f, 0.07f, sunUp));
        }

        // ---- the flash's light said to whoever lights what the scene's cameras do not draw (Natural Light, a cockpit's inside
        // through its windows: filmed 2026-11-10 from the cockpit at night, four strikes from 3.5 km to one on the craft itself
        // changed no frame of the cabin by a twentieth of a level of 255). Through Keystone's Weather.FlashLight, which came
        // after Keystone 0.10.0: kept in a method of its own and guarded, so that with an older Keystone there is nowhere to say
        // it, the first try finds so, and nothing else stops.
        static bool lightUnsaid;

        void SayLight()
        {
            if (lightUnsaid) return;
            try { SayLightToKeystone(); }
            catch (Exception) { lightUnsaid = true; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void SayLightToKeystone()
        {
            Keystone.Weather.FlashLight = flashColour * litNow;
            Keystone.Weather.FlashLightFrom = litFrom;
            Keystone.Weather.FlashLightRound = lightRound;
            Keystone.Weather.FlashLightFrame = Time.frameCount;
        }

        void Animate(Camera cam)
        {
            float lightNow = 0f, nearNow = 0f, bothNow = 0f, bothEnv = 0f;
            Vector3 lightFrom = Vector3.up, bothFrom = Vector3.up;
            Vector3 nearAt = Vector3.zero;
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                Bolt b = bolts[i];
                float t = Time.realtimeSinceStartup - b.Born;
                float env = Envelope(b, t);
                // (the channel itself is seen for longer than the light it throws on the scene: it goes on glowing for some tens of
                // thousandths of a second after each stroke, and the eye keeps so bright a line longer still. And a line a pixel
                // wide that is there for one frame hardly shows at all: the game's smoothing between frames lets a new thing in
                // over three or four of them.)
                float seen = Mathf.Max(env, Lingers(b, t));
                if (t > 0.3f && seen < 0.004f) { Drop(b); bolts.RemoveAt(i); continue; }
                if (cam == null || b.Body == null) continue;
                Vector3d foot = b.Body.GetWorldSurfacePosition(b.F.Latitude, b.F.Longitude, b.F.Bottom);
                Vector3 camPos = cam.transform.position;
                // the scene lit from the flash: a distant one dimly, a near one brightly (from the middle of the channel, or from inside the cloud)
                Provider.Axes(b.Body, foot, out Vector3d e, out Vector3d n, out Vector3d u);
                float midUp = b.F.Ground ? (b.F.Top - b.F.Bottom) * 0.35f : (b.F.Top - b.F.Bottom) * 0.8f;
                Vector3 mid = (Vector3)(foot + u * midUp);
                float d = Mathf.Max(200f, (mid - camPos).magnitude);
                float k = env * (b.F.Ground ? 1.6f : 0.9f) / (1f + (d / 4000f) * (d / 4000f));
                Vector3 from = (mid - camPos).normalized;
                // (the lowest of a channel to the ground lights what is near it most; of a strike on a craft, where it meets the craft)
                Vector3 low = b.OnCraft && b.Through.z > 30f ? (Vector3)(foot + e * b.Through.x + n * b.Through.y + u * b.Through.z) : (Vector3)foot + (Vector3)u * 30f;
                if (k > lightNow) { lightNow = k; lightFrom = from; }
                if (b.F.Ground && d < 3000f && env > nearNow) { nearNow = env; nearAt = low; }
                if (b.F.Ground)
                {
                    // (A strike to the ground near by: the foot of its channel lights what is about the camera far more brightly
                    // than its middle does, and from low down.)
                    float r = (low - camPos).magnitude;
                    float kn = env * 6f / (1f + 25f * r * r / (2500f * 2500f));
                    from = (from * k + (low - camPos).normalized * kn).normalized;
                    k += kn;
                }
                if (k > bothNow) { bothNow = k; bothFrom = from; bothEnv = env; }
                // (seen from under the cloud it comes out of, the channel is drawn by the clouds after themselves, as far up as their
                // base: drawn before them, as everything see-through is, it was hidden wherever there was cloud behind it, since the
                // clouds are laid on over all of that. Inside the cloud it is the cloud that lights up. From above the base it is
                // drawn as before, and a cloud in front of it hides it.)
                b.Front = Clouds.Drawing && (!Clouds.Split || b.Distance > 450f)
                          && (b.Under >= 1e6f || b.Body.GetAltitude(camPos) < b.F.Bottom + b.Under);
                if (b.Drawn) Draw(b, cam, foot, e, n, u, seen);
            }
            // (The even light is reckoned as amounts of light and shown through the screen's curve, where this game keeps its
            // colours as shown: an eighth as much light shows as two fifths as bright. So that the glow between a flash's
            // strokes shows as the eighth of a stroke it is meant to be, and each stroke dies away as fast as it is meant to,
            // how bright the flash is at this moment goes in by that curve turned about. Without that, measured 2026-11-10: the
            // glow 26 levels of 255 on the lit pad where a stroke was 66, and as slow again to die.)
            float lit = bothEnv > 0f ? Mathf.Min(8f, bothNow / bothEnv) * (QualitySettings.activeColorSpace == ColorSpace.Linear ? bothEnv : Mathf.Pow(bothEnv, 2.2f)) : 0f;
            litNow = lightAs == 3 && !lightDark && bothNow > 0.01f && Weather.Flashes ? lit * Weather.Times(Weather.BoltBright) * lightGain * ByDay(cam) : 0f;
            litFrom = bothFrom;
            SayLight();
            if (flashLight != null)
            {
                flashLight.enabled = lightAs != 3 && lightNow > 0.01f && Weather.Flashes;
                flashLight.intensity = (lightDark ? 0f : 1f) * Mathf.Min(2.5f, lightNow) * Weather.Times(Weather.BoltBright);
                if (flashLight.enabled) flashLight.transform.rotation = Quaternion.LookRotation(-lightFrom);
                // (Only in the development build, for films of how it was, see Lit. From 2026-11-09 to 2026-11-10 the flash's light
                // was this light folded into the light Unity gives each thing as a whole (its spherical harmonics, as for any
                // light marked not important): "vertex". Before that Unity chose, and drew it per pixel: "unity". Drawn per pixel,
                // a flash however faint changed how the launch pad's floodlights are drawn at night (Natural Light's, marked not
                // important, with no other light from a direction in the scene to draw per pixel): the lamp-lit concrete went a
                // sixth darker for as long as the flash lasted, a flash 30 km off as much as one 8 km off, and the pad flickered
                // between the two with each stroke of a flash of several.)
                LightRenderMode mode = lightAs == 0 ? LightRenderMode.Auto : lightAs == 2 ? LightRenderMode.ForcePixel : LightRenderMode.ForceVertex;
                if (flashLight.renderMode != mode) flashLight.renderMode = mode;
            }
            if (nearLight != null)
            {
                nearLight.enabled = lightAs != 3 && nearNow > 0.01f && Weather.Flashes;
                nearLight.intensity = 6f * nearNow * Weather.Times(Weather.BoltBright);
                nearLight.transform.position = nearAt;
            }
        }

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> cols = new List<Color>();
        readonly List<int> tris0 = new List<int>(), tris1 = new List<int>();

        /// <summary>The channel as thin strips that face the camera: a core about two pixels wide and a glow round it.</summary>
        void Draw(Bolt b, Camera cam, Vector3d foot, Vector3d e, Vector3d n, Vector3d u, float env)
        {
            verts.Clear(); uvs.Clear(); cols.Clear(); tris0.Clear(); tris1.Clear();
            Vector3 camPos = cam.transform.position;
            Vector3 origin = (Vector3)foot;
            b.Go.transform.position = origin;
            b.Go.transform.rotation = Quaternion.identity;
            b.Origin = origin;
            if (b.Renderer != null) b.Renderer.enabled = !b.Front;
            // (drawn in front of the clouds: no higher than their base, the last stretch below it fading into it)
            float top = b.Front && b.Under < 1e6f ? b.Under : float.MaxValue, fadeOver = Mathf.Max(30f, 0.12f * b.Under);
            float pixel = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);
            Vector3 E = (Vector3)e, N = (Vector3)n, U = (Vector3)u;
            for (int i = 1; i < b.Points.Count; i++)
            {
                int j = b.From[i];
                if (j < 0) continue;
                Vector3 a0 = b.Points[j], a1 = b.Points[i];
                float fade = 1f;
                if (top < float.MaxValue)
                {
                    if (Mathf.Min(a0.z, a1.z) >= top) continue;
                    if (a0.z > top) a0 = Vector3.Lerp(a1, a0, (top - a1.z) / (a0.z - a1.z));
                    else if (a1.z > top) a1 = Vector3.Lerp(a0, a1, (top - a0.z) / (a1.z - a0.z));
                    fade = Mathf.Clamp01((top - 0.5f * (a0.z + a1.z)) / fadeOver);
                }
                Vector3 p0 = E * a0.x + N * a0.y + U * a0.z;
                Vector3 p1 = E * a1.x + N * a1.y + U * a1.z;
                Vector3 along = p1 - p0;
                Vector3 view = (origin + (p0 + p1) * 0.5f) - camPos;
                float d = view.magnitude;
                Vector3 side = Vector3.Cross(along, view);
                if (side.sqrMagnitude < 1e-8f) continue;
                side.Normalize();
                float w = b.Weight[i] * fade;
                Strip(p0, p1, side * Mathf.Max(0.6f, d * pixel * 1.6f) * (0.5f + 0.5f * w), new Color(1f, 1f, 1f, w), tris1);
                Strip(p0, p1, side * Mathf.Max(4f, d * pixel * 10f) * (0.5f + 0.5f * w), new Color(1f, 1f, 1f, w * 0.5f), tris0);
            }
            b.Mesh.Clear();
            b.Mesh.SetVertices(verts); b.Mesh.SetUVs(0, uvs); b.Mesh.SetColors(cols);
            b.Mesh.subMeshCount = 2;
            b.Mesh.SetTriangles(tris0, 0); b.Mesh.SetTriangles(tris1, 1);
            b.Mesh.RecalculateBounds();
#if DEV
            Drew = "bolt: front " + b.Front + ", under " + b.Under.ToString("F0") + " m, " + b.Distance.ToString("F0") + " m off, " + verts.Count + " corners, strength " + env.ToString("F2") + ", the clouds " + (Clouds.Drawing ? "drawing" : "not drawing") + (Clouds.Split ? " (two cameras)" : " (one camera)") + ", foot " + ((Vector3)foot).ToString("F0") + ", camera " + camPos.ToString("F0") + ", box " + b.Mesh.bounds.size.ToString("F0");
#endif
            // (The legacy additive shader doubles its tint, and what it adds falls off across the strip as the square of the soft
            // dot: with the core five and a half times white at its middle in a full stroke, a pixel and a half of it is white
            // in a picture that holds nothing brighter (the far camera's), and in one that does (the near camera's) it blooms.
            // Until 0.2.2 the core was just white at its very middle, two thirds of a pixel wide: a quarter-grey hair once the
            // game had smoothed it, measured on film.)
            float c = Mathf.Clamp(env, 0f, 4f) * Weather.Times(Weather.BoltBright);
            core.SetColor("_TintColor", new Color(2.3f * c, 2.4f * c, 2.6f * c, 0.5f));
            glow.SetColor("_TintColor", new Color(0.33f * c, 0.37f * c, 0.54f * c, 0.5f));
        }

        /// <summary>(Called by the clouds as they finish laying themselves on a camera's picture: the channels seen from under their cloud.)</summary>
        void InFront(CommandBuffer draw, Camera cam)
        {
            if (core == null) return;
            foreach (Bolt b in bolts)
            {
                if (!b.Drawn || !b.Front || b.Mesh == null) continue;
#if DEV
                DrewInFront = "drawn after the clouds on " + cam.name + " at frame " + Time.frameCount;
#endif
                Matrix4x4 at = Matrix4x4.Translate(b.Origin);
                draw.DrawMesh(b.Mesh, at, glow, 0);
                draw.DrawMesh(b.Mesh, at, core, 1);
            }
        }

        /// <summary>How far above a flash's foot the cloud it comes out of begins: the lowest layer of cloud there, by the weather.</summary>
        static float CloudUnder(WeatherFlash f)
        {
            if (f.Body == null) return float.MaxValue;
            WeatherHere h = Model.Layers(World.Of(f.Body), f.Latitude, f.Longitude, Planetarium.GetUniversalTime());
            float lowest = float.MaxValue;
            foreach (CloudLayer l in new[] { h.Low, h.Middle, h.High }) if (l.Cover > 0.05f && l.Base < lowest) lowest = l.Base;
            return lowest < float.MaxValue ? Mathf.Max(50f, lowest - f.Bottom) : float.MaxValue;
        }

        void Strip(Vector3 p0, Vector3 p1, Vector3 half, Color c, List<int> tris)
        {
            int v = verts.Count;
            verts.Add(p0 - half); verts.Add(p0 + half); verts.Add(p1 + half); verts.Add(p1 - half);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2); tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }

        void Drop(Bolt b)
        {
            if (b.Go != null) Destroy(b.Go);
            if (b.Mesh != null) Destroy(b.Mesh);
        }

        static Texture2D Soft()
        {
            const int S = 32;
            var t = new Texture2D(S, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NaturalWeather bolt" };
            for (int x = 0; x < S; x++)
            {
                float a = (x + 0.5f) / S * 2f - 1f;
                float v = Mathf.Exp(-a * a * 5f);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(v, v, v, v));
            }
            t.Apply(false, true);
            return t;
        }

        // ---------------------------------------------------------------- thunder

        /// <summary>
        /// Thunder made from the channel: each length of it gives a short crack (an N-shaped pressure wave, longer the further it
        /// has come), heard after its own distance over the speed of sound; the far ones lose their high notes on the way; and a
        /// low rumble follows the cracks as they arrive, with echoes off the ground. Made on another thread, played when the first
        /// of it arrives.
        /// </summary>
        void Thunder(Bolt b, Vector3 ear, Vector3d foot)
        {
            if (Hush.Paused) return;     // (docs/sound-rules.md, B1: nothing started or changed while the game is paused)
            Provider.Axes(b.Body, foot, out Vector3d e, out Vector3d n, out Vector3d u);
            int count = b.Points.Count;
            var dist = new float[count];
            var len = new float[count];
            var wt = new float[count];
            float nearest = float.MaxValue;
            for (int i = 1; i < count; i++)
            {
                int j = b.From[i];
                if (j < 0) continue;
                Vector3 p = b.Points[i], q = b.Points[j];
                Vector3 mid = (Vector3)(foot + e * ((p.x + q.x) * 0.5) + n * ((p.y + q.y) * 0.5) + u * ((p.z + q.z) * 0.5));
                dist[i] = Mathf.Max(30f, (mid - ear).magnitude);
                len[i] = (p - q).magnitude;
                wt[i] = b.Weight[i];
                nearest = Mathf.Min(nearest, dist[i]);
            }
            if (nearest > 25000f) return;
            float loud = Weather.Thunder * GameSettings.AMBIENCE_VOLUME * Mathf.Clamp(1500f / nearest, 0.04f, 1f) * Mathf.Clamp01(b.F.Brightness);
            if (loud < 0.01f) return;
            int seed = b.F.Seed;
            float delay = nearest / 343f;
            // (since 0.6.0, where the mod has its recordings: a real clap within a kilometre and a half, a far rumble beyond, in
            // place of the rumble made from the bolt's own shape; see Recordings)
            List<AudioClip> recorded = Recordings.Starting(nearest < 1500f ? "thunder-near-" : "thunder-far-");
            if (recorded.Count > 0)
            {
                AudioSource voice = Voice();
                if (voice.clip != null && voice.clip.name == "thunder") Destroy(voice.clip);
                voice.clip = recorded[(seed & 0x7FFFFFFF) % recorded.Count];
                voice.volume = loud;
                voice.pitch = 0.92f + 0.12f * (((seed >> 8) & 0xFF) / 255f);
                voice.PlayDelayed(delay);
                return;
            }
            Task<float[]> made = Task.Run(() => Rumble(dist, len, wt, nearest, seed));
            StartCoroutine(PlayWhenMade(made, delay, loud));
        }

        System.Collections.IEnumerator PlayWhenMade(Task<float[]> made, float delay, float loud)
        {
            float asked = Time.realtimeSinceStartup;
            while (!made.IsCompleted) yield return null;
            if (made.IsFaulted || made.Result == null) yield break;
            float[] samples = made.Result;
            AudioSource voice = Voice();
            if (voice.clip != null && voice.clip.name == "thunder") Destroy(voice.clip);      // (a made one; a recording is kept)
            AudioClip clip = AudioClip.Create("thunder", samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            voice.clip = clip;
            voice.volume = loud;
            voice.pitch = 1f;
            voice.PlayDelayed(Mathf.Max(0f, delay - (Time.realtimeSinceStartup - asked)));
        }

        AudioSource Voice()
        {
            foreach (AudioSource a in voices) if (a != null && !a.isPlaying) return a;
            var go = new GameObject("NaturalWeather thunder");
            DontDestroyOnLoad(go);
            AudioSource s = go.AddComponent<AudioSource>();
            s.spatialBlend = 0f; s.playOnAwake = false; s.loop = false; s.dopplerLevel = 0f;
            Hush.Add(s);
            voices.Add(s);
            return s;
        }

        const int Rate = 22050;

        static float[] Rumble(float[] dist, float[] len, float[] wt, float nearest, int seed)
        {
            float far = nearest;
            for (int i = 0; i < dist.Length; i++) if (dist[i] > far) far = dist[i];
            float seconds = Mathf.Min(14f, (far - nearest) / 343f + 3.5f);
            int n = (int)(seconds * Rate);
            var buf = new float[n];
            var rng = new System.Random(seed);
            // the cracks
            for (int i = 0; i < dist.Length; i++)
            {
                if (len[i] <= 0f) continue;
                int at = (int)((dist[i] - nearest) / 343f * Rate);
                float tau = 0.004f * (1f + dist[i] / 1500f);
                int width = Mathf.Max(4, (int)(tau * Rate));
                float amp = len[i] * (0.5f + 0.5f * wt[i]) / dist[i];
                for (int k = 0; k < width && at + k < n; k++)
                {
                    float ph = (float)k / width;
                    buf[at + k] += amp * (1f - 2f * ph);
                }
            }
            // the far ones lose their high notes: smoothed by as much as the nearest has come (a one-pole filter, run twice)
            float cutoff = 3500f / (1f + nearest / 1200f);
            float a = Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
            for (int pass = 0; pass < 2; pass++)
            {
                float y = 0f;
                for (int k = 0; k < n; k++) { y = (1f - a) * buf[k] + a * y; buf[k] = y; }
            }
            // the rumble: low noise that swells as the cracks arrive
            var env = new float[n];
            float sm = 0f, ea = Mathf.Exp(-1f / (0.25f * Rate));
            for (int k = 0; k < n; k++) { sm = Mathf.Max(Mathf.Abs(buf[k]), sm * ea); env[k] = sm; }
            float lp = 0f, lp2 = 0f, la = Mathf.Exp(-2f * Mathf.PI * 90f / Rate);
            float peakCrack = 1e-9f;
            for (int k = 0; k < n; k++) peakCrack = Mathf.Max(peakCrack, Mathf.Abs(buf[k]));
            for (int k = 0; k < n; k++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = (1f - la) * noise + la * lp; lp2 = (1f - la) * lp + la * lp2;
                buf[k] += lp2 * 14f * env[k] + lp2 * 3f * peakCrack * Mathf.Exp(-(float)k / (Rate * 2.2f));
            }
            // echoes off the ground and hills
            int[] delays = { (int)(0.043f * Rate), (int)(0.071f * Rate), (int)(0.113f * Rate) };
            foreach (int dl in delays)
                for (int k = dl; k < n; k++) buf[k] += 0.33f * buf[k - dl];
            // a soft fade at the end, and the whole made as loud as one
            float peak = 1e-9f;
            for (int k = 0; k < n; k++) peak = Mathf.Max(peak, Mathf.Abs(buf[k]));
            int fade = Math.Min(n, Rate);
            for (int k = 0; k < n; k++)
            {
                float f = k > n - fade ? (float)(n - k) / fade : 1f;
                buf[k] = buf[k] / peak * 0.9f * f;
            }
            return buf;
        }

        // ---------------------------------------------------------------- strikes on craft

        void Strikes(CelestialBody body, double ut)
        {
            if (Time.time < strikeCheck) return;
            strikeCheck = Time.time + 0.5f;
            Vessel v = FlightGlobals.ActiveVessel;
            if (!Weather.Strike || v == null || v.packed || v.mainBody != body || TimeWarp.CurrentRate > 1.01f) return;
            // (a craft that has just been destroyed is still the game's craft for a while, with no part left to strike:
            // seen 2026-10-11, an exception half a minute after a rocket went into the sea under a storm)
            if (v.parts == null || v.parts.Count == 0) return;
            World w = World.Of(body);
            double lat = v.latitude, lon = v.longitude, alt = v.altitude;
            WeatherHere here = Model.Here(w, lat, lon, ut);
            float risk = 0f;
            // (under Blackrack's clouds, drawn in place of these, his storms, as the weather here has them from his clouds: the
            // weather's own storms are not where his are; as strong as in the middle of one of these)
            if (HisSky.On) { if (here.Storm >= 0.3f && alt < Math.Max(here.Low.Top, here.Middle.Top)) risk = 0.0042f * here.Storm * here.Storm; }
            else
                foreach (WeatherCell c in Model.RegionCells(w, lat, lon, ut))
                {
                    if (!c.Thunder || c.Strength < 0.3f || alt > c.Top) continue;
                    double d = Model.Distance(w, lat, lon, c.Latitude, c.Longitude);
                    if (d > c.Radius) continue;
                    float inside = 1f - (float)(d / c.Radius);
                    risk = Mathf.Max(risk, 0.003f * c.Strength * c.Strength * (0.4f + inside));
                }
            // lightning the rocket sets off itself: climbing fast through cold cloud with its engines burning (a long hot plume)
            bool inCloud = here.Low.Cover > 0.3f && alt > here.Low.Base && alt < here.Low.Top || here.Middle.Cover > 0.5f && alt > here.Middle.Base && alt < here.Middle.Top;
            bool cold = Model.ColumnAt(w, lat, lon, ut).B.Freeze < Math.Max(here.Low.Top, here.Middle.Top);
            if (inCloud && cold && v.verticalSpeed > 60.0 && Burning(v)) risk = risk * 4f + (here.Storm > 0.1f ? 0.004f : 0.0012f);
            if (risk <= 0f || UnityEngine.Random.value > risk * 0.5f) return;
            Strike(v, here);
        }

        static bool Burning(Vessel v)
        {
            foreach (Part p in v.parts)
                foreach (PartModule m in p.Modules)
                    if (m is ModuleEngines e && e.EngineIgnited && e.finalThrust > 0.5f * e.maxThrust * 0.1f) return true;
            return false;
        }

        void Strike(Vessel v, WeatherHere here)
        {
            // (The part struck: the highest of the craft, as lightning takes the highest point; until 2026-11-10 any part by
            // chance, and the channel came down to the ground under the craft's middle, past the craft wherever its last step
            // took it: filmed on the pad, 12 m to one side of the rocket.)
            CelestialBody body = v.mainBody;
            Part struck = v.parts[0];
            double highest = double.MinValue;
            foreach (Part p in v.parts)
            {
                if (p == null || p.transform == null) continue;
                double a = body.GetAltitude(p.transform.position);
                if (a > highest) { highest = a; struck = p; }
            }
            float groundH = (float)Math.Max(0.0, v.terrainAltitude);
            float cloudBase = here.Low.Cover > 0.05f ? here.Low.Base : (float)v.altitude + 1500f;
            var f = new WeatherFlash
            {
                Id = UnityEngine.Random.Range(1, int.MaxValue), Body = body, Latitude = v.latitude, Longitude = v.longitude,
                Bottom = groundH, Top = Mathf.Max(cloudBase, (float)v.altitude + 600f), Ground = true, Time = Planetarium.GetUniversalTime(),
                Brightness = 1.6f, Strokes = 3, Seed = UnityEngine.Random.Range(1, int.MaxValue),
            };
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            Vector3d foot = body.GetWorldSurfacePosition(f.Latitude, f.Longitude, f.Bottom);
            Provider.Axes(body, foot, out Vector3d e, out Vector3d n, out Vector3d u);
            Vector3d to = (Vector3d)struck.transform.position - foot;
            Begin(f, cam, new Vector3((float)Vector3d.Dot(to, e), (float)Vector3d.Dot(to, n), Mathf.Max(0f, (float)Vector3d.Dot(to, u))), true, !v.LandedOrSplashed);
            // what it does: a jolt to the craft's electronics (its stability assist drops out for a moment) and a hot spot where it struck
            ScreenMessages.PostScreenMessage("Struck by lightning!", 4f, ScreenMessageStyle.UPPER_CENTER);
            try { struck.skinTemperature = Math.Min(struck.skinMaxTemp * 0.9, struck.skinTemperature + 180.0); } catch (Exception) { }
            if (sasBackAt < 0f) { sasOf = v; sasWas = v.ActionGroups[KSPActionGroup.SAS]; v.ActionGroups.SetGroup(KSPActionGroup.SAS, false); sasBackAt = Time.time + 2f; }
            Weather.Log(v.GetDisplayName() + " struck by lightning at " + v.altitude.ToString("F0") + " m (part: " + (struck.partInfo != null ? struck.partInfo.title : struck.name) + ")");
            LastStrike = v.GetDisplayName() + " struck at " + v.altitude.ToString("F0") + " m";
        }

        public static string LastStrike = "";

        /// <summary>(For tests: strike the craft now, whatever the weather.)</summary>
        public string StrikeNow()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            CelestialBody body = v.mainBody;
            WeatherHere here = Model.Here(World.Of(body), v.latitude, v.longitude, Planetarium.GetUniversalTime());
            Strike(v, here);
            return LastStrike;
        }

#if DEV
        static string Drew = "", DrewInFront = "";
        static float heldFor;
        static int heldFrames;
        /// <summary>
        /// (For the development build: every flash from now on is held at its brightest for so many seconds, or for so many
        /// frames ("6 frames"), so that a picture can be taken of it or a film can show it; after that it goes on as it would
        /// have. Nought: as they are. A film recorded frame by frame wants frames: its frames are a thirtieth to a tenth of a
        /// second apart by the clock a flash goes by, as the scene costs, and a stroke is at its brightest for a fiftieth.)
        /// </summary>
        public static string Hold(string arg)
        {
            arg = (arg ?? "").Trim();
            heldFor = 0f; heldFrames = 0;
            if (arg.EndsWith("frames") || arg.EndsWith("frame")) heldFrames = Math.Max(0, int.Parse(arg.Substring(0, arg.IndexOf('f')).Trim()));
            else if (arg.Length > 0) heldFor = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            return heldFrames > 0 ? "flashes held for " + heldFrames + " frames" : heldFor > 0f ? "flashes held for " + heldFor + " s" : "flashes as they are";
        }
        /// <summary>(For the development build: how a flash lights the scene. "probe": in each camera's even light (as built);
        /// or as a light of Unity's with a lamp at a near strike's foot, as before 2026-11-10: "vertex", folded into the light
        /// each thing is given as a whole (as built from 2026-11-09), "unity", as Unity chooses (as before that), "pixel",
        /// always per pixel. "dark" with any of them: no light thrown. "gain=2": the even light's share so many times as
        /// strong. "round=0.125": how much of it comes from all round as well (an eighth as built). "allday": as strong with
        /// the sun up as by night, as before 2026-11-10 (see ByDay). Nothing: as built.)</summary>
        public static string LightAs(string arg)
        {
            int mode = 3; bool dark = false, allDay = false; float gain = 1f, round = 0.125f;
            foreach (string w in (arg ?? "").Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w == "unity") mode = 0;
                else if (w == "vertex") mode = 1;
                else if (w == "pixel") mode = 2;
                else if (w == "probe") mode = 3;
                else if (w == "dark") dark = true;
                else if (w == "allday") allDay = true;
                else if (w.StartsWith("gain=")) gain = float.Parse(w.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
                else if (w.StartsWith("round=")) round = float.Parse(w.Substring(6), System.Globalization.CultureInfo.InvariantCulture);
                else return "probe, unity, vertex or pixel, and dark, allday, gain=2 or round=0.125 if wanted";
            }
            lightAs = mode; lightDark = dark; lightGain = gain; lightRound = round; lightAllDay = allDay;
            return "a flash's light " + new[] { "as Unity chooses", "folded into each thing's light as a whole", "always per pixel", "in each camera's even light" }[mode]
                   + (dark ? ", throwing none" : "") + (gain != 1f ? ", " + gain.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " times as strong" : "")
                   + (round != 0.125f ? ", " + round.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " of it from all round" : "")
                   + (allDay ? ", as strong by day as by night" : ", " + dayShare.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " of it showing for the daylight at the last flash");
        }

        /// <summary>(For the development build: the craft struck by lightning now, whatever the weather: StrikeNow.)</summary>
        public static string StrikeCraft(string arg)
        {
            Lightning me = FindObjectOfType<Lightning>();
            return me == null ? "not running" : me.StrikeNow();
        }

        /// <summary>
        /// (For the development build: the even light as it is now, and what RenderSettings.ambientProbe gives back when the
        /// game's own flat light is on, which is where Lit starts from when no mod has set a probe of its own: the mode and
        /// the flat colour, then the probe read back looking up, sideways and down, as it is and with the mode set to flat for
        /// the reading.)
        /// </summary>
        public static string Even(string arg)
        {
            AmbientMode was = RenderSettings.ambientMode;
            string o = "mode " + was + ", flat colour " + RenderSettings.ambientLight + " x " + RenderSettings.ambientIntensity.ToString("F2") + "; read back: " + Read(RenderSettings.ambientProbe);
            RenderSettings.ambientMode = AmbientMode.Flat;
            o += "; as flat: " + Read(RenderSettings.ambientProbe) + "; made from the flat colour: " + Read(EvenNow(AmbientMode.Flat));
            RenderSettings.ambientMode = was;
            return o + "; " + (QualitySettings.activeColorSpace == ColorSpace.Linear ? "linear colours" : "gamma colours");
        }

        static string Read(SphericalHarmonicsL2 p)
        {
            var ways = new[] { Vector3.up, Vector3.right, Vector3.down };
            var seen = new Color[3];
            p.Evaluate(ways, seen);
            return "up " + seen[0] + ", sideways " + seen[1] + ", down " + seen[2];
        }

        /// <summary>(For the development build: what the last channel drawn was, and whether the clouds drew it after themselves.)</summary>
        public static string Last() => Drew + " | " + DrewInFront + " | flashes the storms have made since this build began: " + madeToGround + " to the ground, " + madeInCloud + " within cloud";
        static int madeToGround, madeInCloud;

        /// <summary>(For the development build: a flash to the ground at a distance (km) in front of the camera, now.)</summary>
        public static string Flash(string arg)
        {
            Lightning me = FindObjectOfType<Lightning>();
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody body = WeatherDriver.Here();
            if (me == null || cam == null || body == null) return "not running";
            float km = string.IsNullOrEmpty(arg) ? 3f : float.Parse(arg);
            // (ahead on the level: from a cockpit on the pad the camera looks straight up at the instruments, and a flash so many
            // km "ahead" along that came down on the craft itself, whatever the km, filmed 2026-11-10; looking up or down so,
            // the way the top of the picture points)
            Vector3 up = ((Vector3)((Vector3d)cam.transform.position - body.position)).normalized;
            Vector3 ahead = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (ahead.magnitude < 0.3f) ahead = Vector3.ProjectOnPlane(cam.transform.up, up);
            ahead.Normalize();
            Vector3d at = (Vector3d)cam.transform.position + (Vector3d)ahead * km * 1000.0;
            double lat = body.GetLatitude(at), lon = body.GetLongitude(at);
            World w = World.Of(body);
            var f = new WeatherFlash { Id = 1, Body = body, Latitude = lat, Longitude = lon, Bottom = Math.Max(0f, w.Ground(lat, lon)), Top = 4500f, Ground = true, Time = Planetarium.GetUniversalTime(), Brightness = 1.2f, Strokes = 4, Seed = UnityEngine.Random.Range(1, 1 << 30) };
            me.Begin(f, cam, Vector3.zero, false);
            return "a flash " + km + " km ahead";
        }
#endif
    }
}
