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
            public Vector3 Lamp;                   // where on the channel its lamp stands, from the foot (east, north, up), see Lamped
            public float Now, Seen;                // how bright it is this frame (Envelope), and how bright it still shows (with Lingers)
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
        // (the lamp on the channel of the stroke that lights the camera's surroundings most, see Lamped; and how many lights
        // the game drew per pixel before it was lit, which is one more while it shines)
        Light lamp;
        int pixelWere = -1;
        static float lampHigh = 400f, lampRange = 4000f, lampGain = 1f, evenStroke = 0.01f, evenCloud = 0.12f;
        static bool lampShadows = false;
        /// <summary>The lamp's light where the camera is, as it shows (nought with no stroke near): the rain and snow round the camera are lit by it (Rain.Shade).</summary>
        internal static float AtCamera;
        static readonly List<Bolt> live = new List<Bolt>();
        static int lightAs = 4;
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
        float litNow, litRound = 0.125f;
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
            lamp = MakeLight("NaturalWeather stroke", LightType.Point, litMask);
            lamp.renderMode = LightRenderMode.ForcePixel;
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

        static Light MakeLight(string name, LightType type, int mask)
        {
            var go = new GameObject(name);
            Light l = go.AddComponent<Light>();
            l.type = type; l.intensity = 0f; l.enabled = false; l.shadows = LightShadows.None;
            l.color = flashColour;
            l.cullingMask = mask;
            return l;
        }

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
        ///
        /// Since 2026-11-13 only the lesser part of a flash's light comes this way: what the cloud throws down, lit up by
        /// the flash (an eighth of a flash within cloud, a hundredth of a stroke to the ground, as they were reckoned here
        /// until then). A stroke's own light is a lamp on its channel (Lamped): the even light is the same on every face
        /// the camera draws, from the camera's feet to the horizon, and a stroke 450 m off lit the whole land so, evenly,
        /// 180 levels of 255, with nothing to say where it had struck (filmed 2026-11-12; the owner: not realistic).
        /// </summary>
        void Lit(Camera cam)
        {
            if (litNow <= 0f || litSet || cam == null || (cam.cullingMask & litMask) == 0) return;
            litWas = RenderSettings.ambientProbe; litMode = RenderSettings.ambientMode; litSet = true;
            SphericalHarmonicsL2 with = EvenNow(litMode);
            with.AddDirectionalLight(litFrom, flashColour, litNow);
            with.AddAmbientLight(flashColour * (litNow * litRound));
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
            Lamped(null, 0f, null);
            live.Clear();
            if (lamp != null) Destroy(lamp.gameObject);
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
            // (the lamp on the main channel, at the point of it nearest the lamp's height: not above the cloud's base, but
            // not under 150 m for a low one, whose lit base throws the light down from as high; of a strike on a craft, a
            // little above where it meets the craft. Half way up to the base at the most, as first written, a stroke out
            // of a base 235 m up had its lamp at 117 m and lit the ground 450 m off a quarter as much as one out of a
            // base at 900 m: filmed under the held rain.)
            float lampUp = onCraft ? strikeAt.z + 60f : Mathf.Min(lampHigh, Mathf.Max(150f, Mathf.Min(b.Under, f.Top - f.Bottom)));
            float nearestUp = float.MaxValue;
            for (int i = 0; i < b.Points.Count; i++)
                if (b.Weight[i] >= 0.99f && Mathf.Abs(b.Points[i].z - lampUp) < nearestUp) { nearestUp = Mathf.Abs(b.Points[i].z - lampUp); b.Lamp = b.Points[i]; }
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
            Keystone.Weather.FlashLightRound = litRound;
            Keystone.Weather.FlashLightFrame = Time.frameCount;
        }

        void Animate(Camera cam)
        {
            float lightNow = 0f, nearNow = 0f, bothNow = 0f, bothEnv = 0f, evenNow = 0f, evenEnv = 0f, lampScore = 0f, lampEnv = 0f;
            Vector3 lightFrom = Vector3.up, bothFrom = Vector3.up, evenFrom = Vector3.up;
            Vector3 nearAt = Vector3.zero;
            Bolt lampOf = null;
            live.Clear();
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
                b.Now = env; b.Seen = seen;
                live.Add(b);
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
                // (what the cloud throws down of it, in the even light: see Lit)
                float ke = k * (b.F.Ground ? evenStroke : evenCloud);
                if (ke > evenNow) { evenNow = ke; evenEnv = env; evenFrom = from; }
                // (the lamp is the stroke's that lights the camera's surroundings most)
                if (b.F.Ground && b.Distance < 25000f)
                {
                    float score = env / (1f + b.Distance * b.Distance / 1e6f);
                    if (score > lampScore) { lampScore = score; lampOf = b; lampEnv = env; }
                }
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
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            float lit = bothEnv > 0f ? Mathf.Min(8f, bothNow / bothEnv) * (linear ? bothEnv : Mathf.Pow(bothEnv, 2.2f)) : 0f;
            float day = ByDay(cam);
            float times = Weather.Times(Weather.BoltBright) * lightGain * day;
            // (The lamp's strength is a shown value where this game keeps its colours as shown, and the daylight's share is
            // of light: through the screen's curve, as the even light's is the other way about. With the share as it stands
            // a stroke 300 m off by day raised the picture's lower half by 1.4 levels of 255 where the even light had raised
            // it by 14, filmed 2026-11-12.)
            float lampTimes = Weather.Times(Weather.BoltBright) * lightGain * (linear ? day : Mathf.Pow(day, 1f / 2.2f));
            bool lighting = !lightDark && Weather.Flashes;
            // (said to whoever lights what the cameras do not draw, a cockpit's inside, as it was reckoned before the lamp: the
            // flash's whole light where the camera is)
            litNow = lighting && bothNow > 0.01f ? lit * times : 0f;
            litFrom = bothFrom;
            litRound = lightRound;
            SayLight();
            if (lightAs == 4)
            {
                float even = evenEnv > 0f ? evenNow / evenEnv * (linear ? evenEnv : Mathf.Pow(evenEnv, 2.2f)) : 0f;
                litNow = lighting && evenNow > 0.0002f ? even * times : 0f;
                litFrom = evenFrom;
                // (a lit cloud is a wide thing overhead: half as much again from all round)
                litRound = 0.5f;
                Lamped(lighting ? lampOf : null, lampEnv * lampTimes, cam);
            }
            else
            {
                if (lightAs != 3) litNow = 0f;
                Lamped(null, 0f, cam);
            }
            if (flashLight != null)
            {
                flashLight.enabled = lightAs < 3 && lightNow > 0.01f && Weather.Flashes;
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
                nearLight.enabled = lightAs < 3 && nearNow > 0.01f && Weather.Flashes;
                nearLight.intensity = 6f * nearNow * Weather.Times(Weather.BoltBright);
                nearLight.transform.position = nearAt;
            }
        }

        /// <summary>
        /// A stroke's own light on the scene: a lamp of Unity's on its channel, some hundreds of metres up, drawn per pixel.
        /// So the ground is lit most at the stroke's foot and less with every hundred metres from it, a wall that faces
        /// the stroke is lit and its back is not, what stands between the stroke and the camera is a dark shape against
        /// lit ground, and the land a few kilometres off stays dark: as a real stroke at night lights the land (a line of
        /// light from the cloud to the ground lights level ground at a distance d from its foot as 1/d - 1/sqrt(d2 + H2),
        /// H its height: a quarter at 450 m of what it gives at 150 m, a thirtieth at 1.5 km, a hundred-and-seventieth at
        /// 3 km. One lamp 400 m up that reaches 4 km gives 0.58, 0.07 and 0.012: broader near by, which a channel's lower
        /// branches are too, and as dark far off).
        ///
        /// Until 2026-11-13 all of a flash's light was in the cameras' even light (Lit), and before 2026-11-10 in two lights
        /// that were not drawn per pixel and took the place of the pad's floodlights. This one is drawn per pixel always,
        /// and for as long as it shines the game may draw one more light per pixel than its setting says, so that it takes
        /// no other light's place (filmed close on the floodlit pad, strokes 0.45 to 3.5 km off: no part of the picture
        /// darker in any frame, the worst a third of a level of 255).
        ///
        /// It throws no shadows. The game's ground, buildings and parts take shadows from the sun alone (their shaders
        /// draw every other light without them): filmed with the lamp throwing shadows, 400 m up and 60 m up, a stroke
        /// 450 m behind the rocket on the pad, neither the rocket nor the tanks beside it threw any, and the lamp's own
        /// picture of what stands round it would be drawn for nothing. (The development build's LightAs "shadows" has it
        /// throw them, for whoever tries again.)
        /// </summary>
        void Lamped(Bolt b, float bright, Camera cam)
        {
            bool on = lamp != null && b != null && b.Body != null && bright > 0.004f;
            AtCamera = 0f;
            if (!on)
            {
                if (lamp != null && lamp.enabled) lamp.enabled = false;
                if (pixelWere >= 0) { if (QualitySettings.pixelLightCount == pixelWere + 1) QualitySettings.pixelLightCount = pixelWere; pixelWere = -1; }
                return;
            }
            Vector3d foot = b.Body.GetWorldSurfacePosition(b.F.Latitude, b.F.Longitude, b.F.Bottom);
            Provider.Axes(b.Body, foot, out Vector3d e, out Vector3d n, out Vector3d u);
            Vector3 at = (Vector3)(foot + e * b.Lamp.x + n * b.Lamp.y + u * b.Lamp.z);
            lamp.transform.position = at;
            lamp.range = lampRange;
            lamp.intensity = Mathf.Min(8f, lampGain * bright);
            LightShadows shadows = lampShadows ? LightShadows.Hard : LightShadows.None;
            if (lamp.shadows != shadows) lamp.shadows = shadows;
            if (!lamp.enabled) lamp.enabled = true;
            if (pixelWere < 0) { pixelWere = QualitySettings.pixelLightCount; QualitySettings.pixelLightCount = pixelWere + 1; }
            if (cam != null)
            {
                // (as Unity's lamps fall off: to a twenty-sixth at their range, and from there to nothing)
                float r = (at - cam.transform.position).magnitude / lampRange;
                AtCamera = r < 1f ? lamp.intensity / (1f + 25f * r * r) * Mathf.Clamp01((1f - r) * 5f) : 0f;
            }
#if DEV
            LampSaid = "lamp " + lamp.intensity.ToString("F2") + " strong, " + b.Lamp.z.ToString("F0") + " m up, " + (at - (cam != null ? cam.transform.position : at)).magnitude.ToString("F0") + " m from the camera, at the camera " + AtCamera.ToString("F2") + ", per pixel " + QualitySettings.pixelLightCount + " (was " + pixelWere + "), frame " + Time.frameCount;
#endif
        }

        /// <summary>
        /// (For the clouds, which light themselves by a flash, CloudFlashes: a flash that is drawn here, as bright as it shows
        /// this frame, so that cloud, ground and channel flash in the same frames; and for a stroke to the ground how far
        /// over its foot it comes out of the cloud's base: the clouds light their base from there, and the rain from the
        /// channel all the way down. False for a flash not drawn here: too far, or not yet begun.)
        /// </summary>
        internal static bool Live(WeatherFlash f, out float bright, out float under)
        {
            bright = 0f; under = 0f;
            foreach (Bolt b in live)
            {
                // (a flash is a set of values, not a thing: the same one is the one with the same number, seed and time. Until
                // 2026-11-13 this asked whether the two were one thing, which two sets of values never are, so no flash was
                // ever found here and the clouds went on lighting themselves as before, whatever was written of it.)
                if (b.F.Id != f.Id || b.F.Seed != f.Seed || b.F.Time != f.Time) continue;
                bright = Mathf.Max(b.Now, 0.6f * b.Seen);
                under = b.F.Ground && !b.OnCraft && b.Under < 1e6f ? b.Under : 0f;
                return true;
            }
            return false;
        }

        /// <summary>(Whether the lamp way is in use: the development build can film the old ways, and then the clouds and the rain are lit as they were too.)</summary>
        internal static bool Lamps => lightAs == 4;

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> cols = new List<Color>();
        readonly List<int> tris0 = new List<int>(), tris1 = new List<int>();

        readonly List<Vector3> placed = new List<Vector3>(), sideIn = new List<Vector3>();
        readonly List<int> hung = new List<int>(), onward = new List<int>(), kept = new List<int>(), hangs = new List<int>();
        // (the ribbons' half-widths in pixels, and how bright they are against the tints below; Lightning.Channel in a development build)
        static float coreWide = 9f, glowWide = 18f, coreBright = 1f, glowBright = 0.77f;

        /// <summary>
        /// The channel as ribbons that face the camera: a core a few pixels wide and a glow round it.
        ///
        /// Since 2026-11-13 each run of the channel is one ribbon: its lengths share their corners where they meet, lengths
        /// shorter than two and a half pixels are passed over, every free end (the top, a branch's start and its tip)
        /// fades out over a cap, a stroke's foot fades into the ground, and what a ribbon adds falls away from its middle line gently, by half from one pixel
        /// to the next (Soft), from just white at the middle in a stroke of the usual strength. Until then each length was a
        /// strip of its own with square ends, and the core's light fell away within a third of a pixel from five to twenty
        /// times white. Beside TUFX and Scatterer, which each smooth the picture over time and sharpen it as they do (two
        /// temporal antialiasings in a row, in the usual set), that left the channel black with a broken white hair down its
        /// middle wherever the ground or the rain behind it was lit, and black hairs across it at every joint. Filmed
        /// 2026-11-13 with each part of the cameras off in turn (Lightning.Parts): with TUFX's post-processing off, or with
        /// Scatterer's antialiasing over time off, the old channel was white (TUFX off, a thin darker edge to it); with
        /// TUFX's sharpening alone set to nought (Lightning.Sharpen) it was as black as before. So it takes the two together,
        /// and a line thinner than a pixel and many times white; how exactly was not found. Tried with both on: this ribbon
        /// 6 pixels to its edge and one and a half times white still had a black line down one side; 9 pixels and white, none.
        /// </summary>
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
            int count = b.Points.Count;
            bool toGround = b.F.Ground && !b.OnCraft;
            // (which points are drawn: the first, every end, every point a branch hangs from, and of the rest those two and a
            // half pixels or more from the last one drawn; `kept` is for each point the drawn one it stands for)
            placed.Clear(); sideIn.Clear(); hung.Clear(); onward.Clear(); kept.Clear(); hangs.Clear();
            for (int i = 0; i < count; i++)
            {
                Vector3 a = b.Points[i];
                placed.Add(E * a.x + N * a.y + U * a.z); sideIn.Add(Vector3.zero); hung.Add(-1); onward.Add(-1); kept.Add(i); hangs.Add(0);
            }
            for (int i = 1; i < count; i++) if (b.From[i] >= 0) hangs[b.From[i]]++;
            for (int i = 1; i < count; i++)
            {
                int j = b.From[i];
                if (j < 0) continue;
                int from = kept[j];
                float least = (origin + placed[from] - camPos).magnitude * pixel * 2.5f;
                if (hangs[i] == 1 && (placed[i] - placed[from]).sqrMagnitude < least * least) { kept[i] = from; continue; }
                hung[i] = from;
                Vector3 side = Vector3.Cross(placed[i] - placed[from], origin + (placed[i] + placed[from]) * 0.5f - camPos);
                if (side.sqrMagnitude >= 1e-8f) sideIn[i] = side.normalized;
                // (the length that goes on from a point as the same run: the first as bright as the one that came into it; a branch is fainter)
                if (onward[from] < 0 && (hung[from] < 0 || b.Weight[i] >= b.Weight[from] * 0.99f)) onward[from] = i;
            }
            for (int i = 1; i < count; i++)
            {
                int j = hung[i];
                if (j < 0 || sideIn[i] == Vector3.zero) continue;
                float z0 = b.Points[j].z, z1 = b.Points[i].z;
                if (Mathf.Min(z0, z1) >= top) continue;
                Vector3 p0 = placed[j], p1 = placed[i], s0 = sideIn[i], s1 = sideIn[i];
                // (a corner two lengths of a run share lies across the bend between them)
                bool joined0 = onward[j] == i && hung[j] >= 0 && Across(sideIn[i], sideIn[j], out s0);
                bool joined1 = onward[i] >= 0 && Across(sideIn[i], sideIn[onward[i]], out s1);
                if (z0 > top) { p0 = Vector3.Lerp(p1, p0, (top - z1) / (z0 - z1)); z0 = top; s0 = sideIn[i]; joined0 = true; }
                else if (z1 > top) { p1 = Vector3.Lerp(p0, p1, (top - z0) / (z1 - z0)); z1 = top; s1 = sideIn[i]; joined1 = true; }
                float w = b.Weight[i];
                float w0 = top < float.MaxValue ? w * Mathf.Clamp01((top - z0) / fadeOver) : w, w1 = top < float.MaxValue ? w * Mathf.Clamp01((top - z1) / fadeOver) : w;
                float d0 = (origin + p0 - camPos).magnitude * pixel, d1 = (origin + p1 - camPos).magnitude * pixel;
                // (a stroke to the ground fades out over the core's half-width above its foot: the ground cuts the ribbon off
                // square there, and under that edge the two antialiasings left a black dash, filmed)
                if (toGround) { w0 *= Mathf.Clamp01((z0 - 0.5f) / Mathf.Max(1.5f, d0 * coreWide)); w1 *= Mathf.Clamp01((z1 - 0.5f) / Mathf.Max(1.5f, d1 * coreWide)); }
                float core0 = Mathf.Max(0.6f, d0 * coreWide), core1 = Mathf.Max(0.6f, d1 * coreWide);
                float glow0 = Mathf.Max(4f, d0 * glowWide) * (0.5f + 0.5f * w), glow1 = Mathf.Max(4f, d1 * glowWide) * (0.5f + 0.5f * w);
                Strip(p0, s0 * core0, w0, p1, s1 * core1, w1, tris1);
                Strip(p0, s0 * glow0, w0 * 0.5f, p1, s1 * glow1, w1 * 0.5f, tris0);
                Vector3 along = (p1 - p0).normalized;
                if (!joined0)
                {
                    Strip(p0, s0 * core0, w0, p0 - along * core0, s0 * core0, 0f, tris1);
                    Strip(p0, s0 * glow0, w0 * 0.5f, p0 - along * glow0, s0 * glow0, 0f, tris0);
                }
                if (!joined1)
                {
                    Strip(p1, s1 * core1, w1, p1 + along * core1, s1 * core1, 0f, tris1);
                    Strip(p1, s1 * glow1, w1 * 0.5f, p1 + along * glow1, s1 * glow1, 0f, tris0);
                }
            }
            b.Mesh.Clear();
            b.Mesh.SetVertices(verts); b.Mesh.SetUVs(0, uvs); b.Mesh.SetColors(cols);
            b.Mesh.subMeshCount = 2;
            b.Mesh.SetTriangles(tris0, 0); b.Mesh.SetTriangles(tris1, 1);
            b.Mesh.RecalculateBounds();
#if DEV
            Drew = "bolt: front " + b.Front + ", under " + b.Under.ToString("F0") + " m, " + b.Distance.ToString("F0") + " m off, " + verts.Count + " corners of " + count + " points, strength " + env.ToString("F2") + ", the clouds " + (Clouds.Drawing ? "drawing" : "not drawing") + (Clouds.Split ? " (two cameras)" : " (one camera)") + ", foot " + ((Vector3)foot).ToString("F0") + ", camera " + camPos.ToString("F0") + ", box " + b.Mesh.bounds.size.ToString("F0");
#endif
            // (The legacy additive shader doubles its tint, and Soft's picture is white with the ribbon's fall in its alpha, so
            // a ribbon adds twice its tint at its middle line: the core just white in a stroke of strength 1 and up to four
            // times white in the strongest, white for two to six pixels across. Until 0.2.2 the core was just white at its
            // very middle and two thirds of a pixel wide: a quarter-grey hair once the game had smoothed it, measured on film.
            // From then until 2026-11-13 it was five and a half times white and a third of a pixel wide, see above; its light
            // summed across is now about as it was then.)
            float c = Mathf.Clamp(env, 0f, 4f) * Weather.Times(Weather.BoltBright);
            core.SetColor("_TintColor", new Color(0.475f * c * coreBright, 0.495f * c * coreBright, 0.535f * c * coreBright, 0.5f));
            glow.SetColor("_TintColor", new Color(0.33f * c * glowBright, 0.37f * c * glowBright, 0.54f * c * glowBright, 0.5f));
        }

        /// <summary>(The way a ribbon's half-width points at a corner two lengths share: across the bend between them, and so
        /// much the longer that the ribbon keeps its width. False, and the length's own, where the bend is too sharp to share
        /// a corner or the other length is not drawn: both then end there, each with its cap.)</summary>
        static bool Across(Vector3 own, Vector3 other, out Vector3 across)
        {
            across = own;
            if (Vector3.Dot(own, other) < 0.2f) return false;
            Vector3 mid = (own + other).normalized;
            across = mid / Vector3.Dot(mid, own);
            return true;
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

        /// <summary>(A length of ribbon from p0 to p1, its half-width and its strength at each end; its middle line is an edge
        /// of the mesh, so that the brightest of it runs straight from end to end however the ends are turned.)</summary>
        void Strip(Vector3 p0, Vector3 half0, float a0, Vector3 p1, Vector3 half1, float a1, List<int> tris)
        {
            int v = verts.Count;
            verts.Add(p0 - half0); verts.Add(p0); verts.Add(p0 + half0); verts.Add(p1 - half1); verts.Add(p1); verts.Add(p1 + half1);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0.5f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(new Vector2(1f, 1f));
            Color c0 = new Color(1f, 1f, 1f, a0), c1 = new Color(1f, 1f, 1f, a1);
            cols.Add(c0); cols.Add(c0); cols.Add(c0); cols.Add(c1); cols.Add(c1); cols.Add(c1);
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 4); tris.Add(v); tris.Add(v + 4); tris.Add(v + 3);
            tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 5); tris.Add(v + 1); tris.Add(v + 5); tris.Add(v + 4);
        }

        void Drop(Bolt b)
        {
            if (b.Go != null) Destroy(b.Go);
            if (b.Mesh != null) Destroy(b.Mesh);
        }

        /// <summary>
        /// (How a ribbon's light falls from its middle line to its edges, in the picture's alpha; the picture itself white.
        /// One over the hyperbolic cosine of six times the way across: round at the middle, and from there falling by the
        /// same share at every step, to a two-hundredth at the edge (taken off, so that the edge is nought). With the core 9
        /// pixels to its edge that is by half from one pixel to the next. A bell curve, which this was, falls faster the
        /// further out, without end, and the shader squared it besides.)
        /// </summary>
        static Texture2D Soft()
        {
            const int S = 64;
            var t = new Texture2D(S, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NaturalWeather bolt" };
            float edge = 1f / (float)Math.Cosh(6.0);
            for (int x = 0; x < S; x++)
            {
                float a = (x + 0.5f) / S * 2f - 1f;
                float v = Mathf.Clamp01((1f / (float)Math.Cosh(a * 6.0) - edge) / (1f - edge));
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, v));
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
        static string Drew = "", DrewInFront = "", LampSaid = "";
        /// <summary>(For the development build: the lamp as it was last lit.)</summary>
        public static string Lamp(string arg) => LampSaid;
        /// <summary>(For the development build, to find which part of the picture's making does something to the channel:
        /// nothing, every camera's parts (its scripts, on or off, and the drawing lists hung on it); "off Name" or "on Name",
        /// every script whose type's name holds Name switched so on every camera. Other mods' parts among them, by name.)</summary>
        public static string Parts(string arg)
        {
            string[] w = (arg ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var said = new System.Text.StringBuilder();
            foreach (Camera c in Camera.allCameras)
            {
                if (w.Length == 0) said.Append("\n").Append(c.name).Append(" (depth ").Append(c.depth).Append(c.allowHDR ? ", more than white kept" : "").Append(c.allowMSAA ? ", MSAA allowed" : "").Append("): ");
                foreach (MonoBehaviour m in c.GetComponents<MonoBehaviour>())
                {
                    if (m == null) continue;
                    string name = m.GetType().FullName;
                    if (w.Length == 0) said.Append(name).Append(m.enabled ? "" : " (off)").Append(", ");
                    else if (w.Length == 2 && name.IndexOf(w[1], StringComparison.OrdinalIgnoreCase) >= 0) { m.enabled = w[0] == "on"; said.Append(c.name).Append(": ").Append(name).Append(m.enabled ? " on; " : " off; "); }
                }
                if (w.Length == 0)
                    foreach (CameraEvent at in (CameraEvent[])Enum.GetValues(typeof(CameraEvent)))
                        foreach (CommandBuffer cb in c.GetCommandBuffers(at)) said.Append("[").Append(at).Append(": ").Append(cb.name).Append("] ");
            }
            return said.Length > 0 ? said.ToString() : "nothing by that name";
        }
        /// <summary>(For the development build: the channel's ribbons, "core=9 glow=18 bright=1 glowbright=0.77": their
        /// half-widths in pixels and how bright they are against what is built. Nothing: as built.)</summary>
        public static string Ribbons(string arg)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            coreWide = 9f; glowWide = 18f; coreBright = 1f; glowBright = 0.77f;
            foreach (string w in (arg ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.StartsWith("core=")) coreWide = float.Parse(w.Substring(5), inv);
                else if (w.StartsWith("glow=")) glowWide = float.Parse(w.Substring(5), inv);
                else if (w.StartsWith("bright=")) coreBright = float.Parse(w.Substring(7), inv);
                else if (w.StartsWith("glowbright=")) glowBright = float.Parse(w.Substring(11), inv);
                else return "core=9 glow=18 bright=1 glowbright=0.77";
            }
            return "the channel's core " + coreWide.ToString("0.##", inv) + " pixels to its edge, " + coreBright.ToString("0.##", inv) + " times as bright; its glow " + glowWide.ToString("0.##", inv) + " pixels, " + glowBright.ToString("0.##", inv) + " times";
        }
        /// <summary>(For the development build: the antialiasing other mods do on each camera, and with a number their
        /// temporal antialiasing's sharpening set to that: TUFX's (Unity's post-processing, 0.25 as it comes) and Scatterer's
        /// own (0.28), which both run in the usual set. The sharpening takes from each pixel a share of what its neighbours
        /// hold more than it, and beside a thin line many times white that leaves black. Found by name, since nothing here is
        /// built against those libraries.)</summary>
        public static string Sharpen(string arg)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            arg = (arg ?? "").Trim();
            var said = new System.Text.StringBuilder();
            foreach (Camera c in Camera.allCameras)
            {
                foreach (MonoBehaviour m in c.GetComponents<MonoBehaviour>())
                {
                    if (m == null) continue;
                    Type t = m.GetType();
                    object holds = m;
                    System.Reflection.FieldInfo sharp = t.GetField("sharpness", any), mode = t.GetField("antialiasingMode", any), taa = t.GetField("temporalAntialiasing", any);
                    if (sharp == null && taa != null) { holds = taa.GetValue(m); sharp = holds != null ? holds.GetType().GetField("sharpness", any) : null; }
                    if (sharp == null || sharp.FieldType != typeof(float) || (t.FullName.IndexOf("PostProcess") < 0 && t.FullName.IndexOf("AntiAliasing") < 0)) continue;
                    if (arg.Length > 0) sharp.SetValue(holds, float.Parse(arg, inv));
                    said.Append(c.name).Append(", ").Append(t.FullName).Append(mode != null ? " (" + mode.GetValue(m) + ")" : "").Append(m.enabled ? "" : " (off)").Append(": sharpening ").Append(((float)sharp.GetValue(holds)).ToString("0.###", inv)).Append("; ");
                }
            }
            return said.Length > 0 ? said.ToString() : "no camera has an antialiasing that sharpens";
        }
        /// <summary>(For the development build: a stroke's light on the clouds and their rain, "bright spread rainspread rainbright":
        /// how bright it shows on the cloud's base against a flash within cloud, how far it spreads there (m), how far in the
        /// rain, how bright the rain shows in it. Nothing: as built, "0.2 900 450 0.75".)</summary>
        public static string Cloud(string arg)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string[] w = (arg ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Clouds.StrokeBright = w.Length > 0 ? float.Parse(w[0], inv) : 0.2f;
            Clouds.StrokeSpread = w.Length > 1 ? float.Parse(w[1], inv) : 900f;
            Clouds.RainSpread = w.Length > 2 ? float.Parse(w[2], inv) : 450f;
            Clouds.RainBright = w.Length > 3 ? float.Parse(w[3], inv) : 0.75f;
            return "a stroke on the cloud's base " + Clouds.StrokeBright.ToString("0.##", inv) + " times a flash within, spread over " + Clouds.StrokeSpread.ToString("0", inv) + " m; in the rain over " + Clouds.RainSpread.ToString("0", inv) + " m, " + Clouds.RainBright.ToString("0.##", inv) + " times as bright";
        }
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
        /// always per pixel; "lamp", as built since 2026-11-13: a lamp on the stroke's channel and the cloud's share in the
        /// even light (Lamped), with "high=400 range=4000 lamp=3.5 even=0.01 evencloud=0.12 shadows" to try others.
        /// "dark" with any of them: no light thrown. "gain=2": the even light's share so many times as
        /// strong. "round=0.125": how much of it comes from all round as well (an eighth as built). "allday": as strong with
        /// the sun up as by night, as before 2026-11-10 (see ByDay). Nothing: as built, the lamp.)</summary>
        public static string LightAs(string arg)
        {
            int mode = 4; bool dark = false, allDay = false; float gain = 1f, round = 0.125f;
            lampHigh = 400f; lampRange = 4000f; lampGain = 1f; evenStroke = 0.01f; evenCloud = 0.12f; lampShadows = false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string w in (arg ?? "").Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w == "unity") mode = 0;
                else if (w == "vertex") mode = 1;
                else if (w == "pixel") mode = 2;
                else if (w == "probe") mode = 3;
                else if (w == "lamp") mode = 4;
                else if (w == "shadows") lampShadows = true;
                else if (w.StartsWith("high=")) lampHigh = float.Parse(w.Substring(5), inv);
                else if (w.StartsWith("range=")) lampRange = float.Parse(w.Substring(6), inv);
                else if (w.StartsWith("lamp=")) lampGain = float.Parse(w.Substring(5), inv);
                else if (w.StartsWith("even=")) evenStroke = float.Parse(w.Substring(5), inv);
                else if (w.StartsWith("evencloud=")) evenCloud = float.Parse(w.Substring(10), inv);
                else if (w == "dark") dark = true;
                else if (w == "allday") allDay = true;
                else if (w.StartsWith("gain=")) gain = float.Parse(w.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
                else if (w.StartsWith("round=")) round = float.Parse(w.Substring(6), System.Globalization.CultureInfo.InvariantCulture);
                else return "lamp, probe, unity, vertex or pixel, and dark, allday, gain=2, round=0.125, shadows, high=400, range=4000, lamp=3.5, even=0.01 or evencloud=0.12 if wanted";
            }
            lightAs = mode; lightDark = dark; lightGain = gain; lightRound = round; lightAllDay = allDay;
            return "a flash's light " + new[] { "as Unity chooses", "folded into each thing's light as a whole", "always per pixel", "in each camera's even light", "a lamp on the stroke's channel (" + lampHigh.ToString("0", inv) + " m up, reaching " + lampRange.ToString("0", inv) + " m, " + lampGain.ToString("0.##", inv) + " strong" + (lampShadows ? ", throwing shadows" : "") + "), the cloud's share in the even light (" + evenStroke.ToString("0.###", inv) + " of a stroke, " + evenCloud.ToString("0.###", inv) + " of a flash within cloud)" }[mode]
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
            // ("3 cloud": a flash within the cloud there, no stroke to the ground)
            bool within = (arg ?? "").Contains("cloud");
            arg = (arg ?? "").Replace("cloud", "").Trim();
            float km = string.IsNullOrEmpty(arg) ? 3f : float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
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
            var f = new WeatherFlash { Id = 1, Body = body, Latitude = lat, Longitude = lon, Bottom = within ? 3000f : Math.Max(0f, w.Ground(lat, lon)), Top = 4500f, Ground = !within, Time = Planetarium.GetUniversalTime(), Brightness = 1.2f, Strokes = within ? 2 : 4, Seed = UnityEngine.Random.Range(1, 1 << 30) };
            me.Begin(f, cam, Vector3.zero, false);
            return "a flash " + km + " km ahead" + (within ? ", within the cloud" : "");
        }
#endif
    }
}
