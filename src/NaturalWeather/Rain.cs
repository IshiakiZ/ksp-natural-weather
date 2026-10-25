using System;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// Rain and snow round the camera, drawn with Unity's particles (native code: next to nothing for the processor) and the
    /// game's own particle shader, so nothing here needs a shader of its own and it should look the same on Windows.
    ///
    /// What makes rain look like rain (Garg and Nayar; Lagarde; see the research page): a drop is seen by the light it bends
    /// towards the eye from all round it, so it is as bright as the scene is, not a light of its own; a streak is a drop
    /// smeared over the camera's exposure, so its length goes with how fast it crosses the view; it shows against dark
    /// things and lamps and hardly at all against a bright sky. So the drops are thin, faint streaks added to the picture,
    /// as bright as the light in front of the camera (the sky and the ground as Natural Light sets them) and glinting
    /// towards the sun, slanted by the same wind the craft feels, and streaked along the camera's own motion when it rides
    /// with a craft. Snow is white flakes, as bright as snow on the ground in the same light, that fall at a metre a second
    /// and are carried about by every eddy of the gusts. (Beyond the 24 metres of drops and flakes drawn here, Natural Light
    /// veils the distance by as much as the weather says one can see.)
    /// </summary>
    public sealed class Rain : MonoBehaviour
    {
        GameObject follower;
        ParticleSystem drops, flakes, motes;
        ParticleSystemRenderer dropsDrawn, flakesDrawn, motesDrawn;
        Material dropMat, flakeMat, moteMat;
        Texture2D streak, dot;
        public static string Report = "";
        /// <summary>A snowflake's colour in the light there is, as last worked out (for whatever else falls white: hail, ice pellets).</summary>
        public static Color SnowLit = Color.white;

        // ---- rain in the light of lamps: the strongest thing about rain at night (each lamp near the camera gets its own few drops, lit by it)
        sealed class Lit { public Light Lamp; public ParticleSystem Drops; public Material Mat; }
        readonly System.Collections.Generic.List<Lit> lit = new System.Collections.Generic.List<Lit>();
        float lampsAt;

        // ---- and its sound: a hiss, made by the mod (no sound files); and the wind's, a low roar that swells with the gusts
        AudioSource hiss, roar;
        AudioClip hissClip, roarClip, rushClip;
        // (since 0.5.0: the air rushing past the craft, by how hard it presses on it; and the cockpit's walls between the ear and the rain)
        AudioSource rush;
        AudioLowPassFilter rushMuffle, hissMuffle;
        Vector3 gustNow;
        // (since 0.6.0: recordings of real rain and wind in their place, where the mod has them: see Recordings)
        readonly Loops rainLoops = new Loops("NaturalWeather rain (recorded)"), windLoops = new Loops("NaturalWeather wind (recorded)");

        const float Box = 24f;           // how far round the camera drops are drawn (beyond a few tens of metres a drop is too thin to see)
        const float Shutter = 1f / 48f;  // the exposure a streak is smeared over: a film camera's half-turn shutter at 24 frames (it was 1/20: streaks twice as long as a camera makes them)

        void Start()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
            if (shader == null) { Report = "Rain cannot be drawn: the game's particle shader was not found."; Weather.Log(Report); enabled = false; return; }
            streak = MakeStreak(); dot = MakeDot();
            // (Drops are laid over the picture in the light they gather from all round, not added to it: see Shade. Added, as
            // they first were, a streak could only ever be lighter than what was behind it, and showed as a white line against
            // a grey sky, where a real one all but vanishes.)
            // (named, so that Natural Light's smoke, which tints every puff drawn over what is behind it, leaves these alone)
            dropMat = new Material(shader) { mainTexture = streak, renderQueue = 3100, name = "NaturalWeather drops" };
#if DEV
            oldDropMat = new Material(shader) { mainTexture = streak, renderQueue = 3100, name = "NaturalWeather drops (0.1.0)" };
#endif
            flakeMat = new Material(shader) { mainTexture = dot, renderQueue = 3100, name = "NaturalWeather flakes" };
            moteMat = new Material(shader) { mainTexture = dot, renderQueue = 3100, name = "NaturalWeather dust" };
            follower = new GameObject("NaturalWeather rain");
            drops = Make("drops", dropMat, true, out dropsDrawn);
            Splashes(drops);
            flakes = Make("flakes", flakeMat, false, out flakesDrawn);
            motes = Make("dust", moteMat, false, out motesDrawn);
            var mm = motes.main; mm.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.035f);
            var mn = motes.noise; mn.strength = 1.6f; mn.frequency = 0.25f;
        }

        ParticleSystem Make(string name, Material mat, bool streaks, out ParticleSystemRenderer drawn)
        {
            var go = new GameObject(name);
            go.transform.SetParent(follower.transform, false);
            go.layer = 0;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f; main.maxParticles = streaks ? 16000 : 9000;
            // (drops of every size rain comes in: a streak's width is the drop's, drawn no thinner than a pixel can carry)
            main.startSize = streaks ? new ParticleSystem.MinMaxCurve(0.018f, 0.06f) : new ParticleSystem.MinMaxCurve(0.035f, 0.07f);
            main.startLifetime = 3f; main.gravityModifier = 0f; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, streaks ? 0f : 6.283f);
            var shape = ps.shape;
            shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(Box, 1f, Box);
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0f;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f; velocity.y = 0f; velocity.z = 0f;
            if (!streaks)
            {
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.7f; noise.frequency = 0.35f; noise.scrollSpeed = 0.2f; noise.damping = true; noise.quality = ParticleSystemNoiseQuality.Medium;
            }
            drawn = go.GetComponent<ParticleSystemRenderer>();
            drawn.sharedMaterial = mat;
            // (flakes and dust too are drawn as short strokes, not dots: a dot that moves several pixels a frame is wiped out by the
            // near camera's smoothing between frames, as anything small and fast is; and a shutter smears a falling flake so)
            drawn.renderMode = ParticleSystemRenderMode.Stretch;
            drawn.lengthScale = streaks ? 2f : 1.2f; drawn.velocityScale = streaks ? Shutter : Shutter * 0.4f; drawn.cameraVelocityScale = 0f;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; drawn.receiveShadows = false;
            drawn.sortingFudge = 0f; drawn.minParticleSize = 0f; drawn.maxParticleSize = streaks ? 0.0028f : 0.01f;     // (a drop that passes right by the lens is not a smear across the picture)
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Splashes where drops strike the ground, the pad, the buildings or a craft: the drop ends there, and two or three tiny
        /// droplets jump up a hand's breadth and fall back. (Unity's particles find what they strike themselves, in its own code.
        /// The sea has nothing to strike: no splashes on water.)
        /// </summary>
        void Splashes(ParticleSystem ps)
        {
            var hit = ps.collision;
            hit.enabled = true; hit.type = ParticleSystemCollisionType.World; hit.mode = ParticleSystemCollisionMode.Collision3D;
            hit.quality = ParticleSystemCollisionQuality.Low; hit.collidesWith = (1 << 0) | (1 << 15);
            hit.lifetimeLoss = 1f; hit.bounce = 0f; hit.dampen = 1f; hit.radiusScale = 0.2f; hit.sendCollisionMessages = false;
            hit.maxCollisionShapes = 64;
            var go = new GameObject("splashes");
            go.transform.SetParent(ps.transform, false);
            ParticleSystem splash = go.AddComponent<ParticleSystem>();
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = splash.main;
            main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.016f); main.maxParticles = 4000; main.gravityModifier = 0f;
            var emission = splash.emission; emission.enabled = true; emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 3) });
            var shape = splash.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.02f;
            // (the game's world has no gravity of its own: the droplets are pulled down by a force)
            var force = splash.forceOverLifetime; force.enabled = true; force.space = ParticleSystemSimulationSpace.World;
            force.x = 0f; force.y = 0f; force.z = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = flakeMat; r.renderMode = ParticleSystemRenderMode.Billboard; r.maxParticleSize = 0.004f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var subs = ps.subEmitters;
            subs.enabled = true;
            subs.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
            splashes = splash;
        }

        ParticleSystem splashes;

        void OnEnable() { Camera.onPreRender += Drawing; }
        void OnDisable() { Camera.onPreRender -= Drawing; }

        float rainNow;
        Vector3 downNow = Vector3.down;
        bool shading;

        /// <summary>
        /// The drops and flakes are lit as the near camera is about to draw them, which is the one moment the scene's light is what
        /// it will be drawn with: Natural Light hands the game its light from all round only to the cameras of the scene, as each
        /// begins (between frames the game's own flat grey is back), and the sun's strength under cloud likewise; and Scatterer
        /// puts its own colour on the sun just then.
        /// </summary>
        void Drawing(Camera cam)
        {
            if (!shading || cam == null || !Keystone.Names.Is(cam, "Camera 00") || cam.targetTexture != null || follower == null) return;
#if DEV
            if (old) return;
#endif
            Shade(cam, rainNow, downNow);
        }

        void OnDestroy()
        {
            foreach (Lit l in lit) { if (l.Drops != null) Destroy(l.Drops.gameObject); if (l.Mat != null) Destroy(l.Mat); }
            lit.Clear();
            if (hiss != null) Destroy(hiss.gameObject);
            if (hissClip != null) Destroy(hissClip);
            if (roar != null) Destroy(roar.gameObject);
            if (roarClip != null) Destroy(roarClip);
            if (rush != null) Destroy(rush.gameObject);
            if (rushClip != null) Destroy(rushClip);
            rainLoops.Drop(); windLoops.Drop();
            if (follower != null) Destroy(follower);
            if (dropMat != null) Destroy(dropMat);
#if DEV
            if (oldDropMat != null) Destroy(oldDropMat);
#endif
            if (flakeMat != null) Destroy(flakeMat);
            if (moteMat != null) Destroy(moteMat);
            if (streak != null) Destroy(streak);
            if (dot != null) Destroy(dot);
        }

        void LateUpdate()
        {
            if (follower == null) return;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody body = FlightGlobals.currentMainBody;
            bool wanted = cam != null && body != null && body.atmosphere && Weather.On && Weather.Rain && Model.Allowed(body) && !MapView.MapIsEnabled;
            float rain = 0f, snow = 0f, dust = 0f, pellets = 0f, glaze = 0f;
            Vector3 wind = Vector3.zero, down = Vector3.down;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            // (how fast the camera goes: as the craft it rides with goes; a speed read from where the camera was last frame would
            // read nothing or double, since the game moves craft fifty times a second whatever the frame rate)
            Vector3 camVel = Vector3.zero;
            Vessel ship = FlightGlobals.ActiveVessel;
            if (ship != null && cam != null && (ship.CoMD - (Vector3d)camPos).sqrMagnitude < 2000.0 * 2000.0) camVel = (Vector3)ship.srf_velocity;
            if (wanted)
            {
                double lat = body.GetLatitude(camPos), lon = body.GetLongitude(camPos), alt = body.GetAltitude(camPos);
                World w = World.Of(body);
                double ut = Planetarium.GetUniversalTime();
                WeatherHere here = Model.Here(w, lat, lon, ut);
                // (rain falls from the cloud's base: above it there is none to see, and none at all above the cloud)
                float under = here.Low.Cover > 0.05f ? Noise.Smooth(here.Low.Top, here.Low.Base, (float)alt) : 1f;
                rain = here.Rain * under; snow = here.Snow; pellets = here.Pellets; glaze = here.Freezing;
                // (dust blows along the ground: thickest low down, none above the storm's top)
                if (Weather.Dust) dust = here.Dust * Noise.Smooth(here.Low.Kind == CloudKind.Dust ? here.Low.Top : (float)Math.Max(0.0, alt) + 1f, Math.Max(0f, here.Low.Base), (float)alt);
                Provider.Axes(body, camPos, out Vector3d e, out Vector3d n, out Vector3d u);
                down = -(Vector3)u;
                Column col = Model.ColumnAt(w, lat, lon, ut);
                // (above the ground itself: the weather's own map of the ground is coarse, and the pad stands 70 m up on it)
                double ground = body.TerrainAltitude(lat, lon, true);
                if (body.ocean && ground < 0.0) ground = 0.0;
                float agl = (float)Math.Max(1.0, alt - ground);
                // (all of the wind but the gusts: a storm's rising and sinking air, a downburst's, a tornado's among it)
                Vector3 m = Provider.Local(w, lat, lon, alt, agl, ut, false);
                wind = (Vector3)(e * m.x + n * m.y + u * m.z);
                Vector3 g = Gusts.At(col, lat, lon, alt, agl, ut);
                gustNow = new Vector3(m.x + g.x, m.y + g.y, agl);
            }
            // (rain is moved with the camera: the drops go by as the air carries them past it)
            follower.transform.position = camPos;
            follower.transform.rotation = Quaternion.identity;
            // (ice pellets are drawn by Hail; freezing rain is rain until it lands)
            Feed(drops, rain * Mathf.Clamp01(1f - snow - pellets), Mathf.Lerp(4.5f, 8.5f, Mathf.Clamp01(rain * 1.4f)), wind, down, camVel, true);
            if (splashes != null)
            {
                // (down is where the world's own pull is: the splashes fall back that way)
                var force = splashes.forceOverLifetime; Vector3 g = down * 9.8f; force.x = g.x; force.y = g.y; force.z = g.z;
                // (splashes on a craft moving fast are not seen as such: only near the ground and slow)
                var hit = drops.collision; hit.enabled = camVel.sqrMagnitude < 40f * 40f;
            }
            Feed(flakes, rain * snow, 1.1f, wind, down, camVel, false);
            Feed(motes, dust, 0.25f, wind, down, camVel, false);
#if DEV
            if (old)
            {
                if (dropsDrawn.sharedMaterial != oldDropMat) dropsDrawn.sharedMaterial = oldDropMat;
                Light(oldDropMat, 0.45f + 0.3f * Mathf.Clamp01(rain * 1.5f));
                Light(flakeMat, 0.85f, new Color(2.4f, 2.4f, 2.5f));
                Light(moteMat, 0.4f, new Color(0.85f, 0.6f, 0.42f));
            }
            else
            {
                if (dropsDrawn.sharedMaterial != dropMat) dropsDrawn.sharedMaterial = dropMat;
#endif
            // (lit as the near camera is about to draw: see Drawing)
            rainNow = rain; downNow = down; shading = true;
#if DEV
            }
#endif
            Lamps(cam, rain, snow, wind, down);
            Sound(rain * Mathf.Clamp01(1f - snow * 0.7f - pellets), camVel.magnitude);
            Roar(wanted ? gustNow : Vector3.zero);
            Airflow(ship, cam);
            Report = rain > 0.01f ? (glaze > 0.5f ? "Freezing rain" : pellets > 0.5f ? "Ice pellets falling" : snow > 0.5f ? "Snowing" : "Raining") + " here" + (lit.Count > 0 ? ", in the light of " + lit.Count + (lit.Count == 1 ? " lamp" : " lamps") : "") + "." : dust > 0.05f ? "Dust blowing here." : "";
        }

        void Feed(ParticleSystem ps, float amount, float fall, Vector3 wind, Vector3 down, Vector3 camVel, bool streaks)
        {
            var emission = ps.emission;
            if (amount < 0.005f) { emission.rateOverTime = 0f; return; }
            Vector3 air = wind + down * fall;
            Vector3 rel = air - camVel;                      // how the drops move past the camera
            float speed = rel.magnitude;
            float life = Mathf.Clamp(Box / Mathf.Max(speed, 0.5f), 0.25f, streaks ? 8f : 40f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
            var velocity = ps.velocityOverLifetime;
            velocity.x = rel.x; velocity.y = rel.y; velocity.z = rel.z;
            // where they start: upstream of the camera, so that they pass through the box about it
            var shape = ps.shape;
            shape.position = Quaternion.Inverse(ps.transform.rotation) * (-rel * (life * 0.5f));
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(Box, Box * 0.6f, Box);
            // how many: a downpour is a few thousand streaks in view at once, drizzle a few hundred
            float inView = (streaks ? 9000f : 5000f) * Mathf.Pow(Mathf.Clamp01(amount), 0.7f);
            emission.rateOverTime = inView / life;
        }

        /// <summary>
        /// The lamps near the camera (floodlights, the pad's own, buildings'), up to four: in each one's light a few more drops, as
        /// bright as the lamp makes them, which is how rain is seen at night.
        /// </summary>
        void Lamps(Camera cam, float rain, float snow, Vector3 wind, Vector3 down)
        {
            if (Time.realtimeSinceStartup > lampsAt && cam != null)
            {
                lampsAt = Time.realtimeSinceStartup + 1f;
                var near = new System.Collections.Generic.List<Light>();
                if (rain > 0.01f)
                {
                    Vector3 at = cam.transform.position;
                    foreach (Light l in FindObjectsOfType<Light>())
                    {
                        if (l == null || !l.isActiveAndEnabled || l.intensity < 0.2f || l.type == LightType.Directional || l.range < 4f) continue;
                        if ((l.cullingMask & ((1 << 0) | (1 << 15))) == 0) continue;
                        if ((l.transform.position - at).sqrMagnitude > 90f * 90f) continue;
                        near.Add(l);
                    }
                    near.Sort((a, b) => ((a.transform.position - at).sqrMagnitude / Mathf.Max(0.1f, a.intensity)).CompareTo((b.transform.position - at).sqrMagnitude / Mathf.Max(0.1f, b.intensity)));
                    if (near.Count > 4) near.RemoveRange(4, near.Count - 4);
                }
                // (the drops of lamps no longer near are let go)
                for (int i = lit.Count - 1; i >= 0; i--)
                    if (lit[i].Lamp == null || !near.Contains(lit[i].Lamp)) { if (lit[i].Drops != null) Destroy(lit[i].Drops.gameObject); if (lit[i].Mat != null) Destroy(lit[i].Mat); lit.RemoveAt(i); }
                foreach (Light l in near)
                {
                    if (lit.Exists(x => x.Lamp == l)) continue;
                    // (drops in a lamp's light add that light to what is behind them: drawn added, not laid over)
                    Shader add = Shader.Find("Legacy Shaders/Particles/Additive") ?? dropMat.shader;
                    var one = new Lit { Lamp = l, Mat = new Material(add) { mainTexture = streak, renderQueue = 3100, name = "NaturalWeather lamp drops" } };
                    one.Drops = Make("lamp drops", one.Mat, true, out ParticleSystemRenderer r);
                    one.Drops.transform.SetParent(null, true);
                    var main = one.Drops.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
                    lit.Add(one);
                }
            }
            foreach (Lit one in lit)
            {
                if (one.Lamp == null || one.Drops == null) continue;
                float reach = Mathf.Clamp(one.Lamp.range * 0.35f, 4f, 14f);
                one.Drops.transform.position = one.Lamp.transform.position - down * (reach * 0.3f);      // (about the lamp, a little above it: a floodlight stands on the ground and shines up)
                one.Drops.transform.rotation = Quaternion.identity;
                // (snow in a lamp's light: flakes, slow, drifting; rain: streaks)
                bool flaking = snow > 0.5f;
                var drawn = one.Drops.GetComponent<ParticleSystemRenderer>();
                drawn.renderMode = ParticleSystemRenderMode.Stretch;
                drawn.lengthScale = flaking ? 1.2f : 2f; drawn.velocityScale = flaking ? Shutter * 0.4f : Shutter;
                drawn.maxParticleSize = flaking ? 0.01f : 0.004f;
                one.Mat.mainTexture = flaking ? dot : streak;
                var noise = one.Drops.noise; noise.enabled = flaking; noise.strength = 0.7f; noise.frequency = 0.35f;
                var sz = one.Drops.main; sz.startSize = flaking ? new ParticleSystem.MinMaxCurve(0.035f, 0.07f) : new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                Vector3 air = wind + down * (flaking ? 1.1f : Mathf.Lerp(4.5f, 8.5f, Mathf.Clamp01(rain * 1.4f)));
                float life = Mathf.Clamp(2f * reach / Mathf.Max(air.magnitude, 0.5f), 0.3f, flaking ? 20f : 4f);
                var main = one.Drops.main; main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
                var vel = one.Drops.velocityOverLifetime; vel.x = air.x; vel.y = air.y; vel.z = air.z;
                var shape = one.Drops.shape; shape.position = -air * (life * 0.5f); shape.scale = new Vector3(reach * 2f, reach * 1.2f, reach * 2f);
                var emission = one.Drops.emission;
                emission.rateOverTime = rain < 0.01f ? 0f : 3000f * Mathf.Pow(Mathf.Clamp01(rain), 0.7f) / life;
                // (lit by the lamp: its colour, as strong as it is, faded at the edge of its reach)
                Color c = one.Lamp.color * Mathf.Clamp(one.Lamp.intensity, 0.3f, 3f) * 0.45f;
                one.Mat.SetColor("_TintColor", new Color(c.r, c.g, c.b, 0.5f));
            }
        }

        /// <summary>Whether the camera is in the cockpit, looking out through its walls.</summary>
        static bool Inside => CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA;

        /// <summary>
        /// The hiss of rain: noise made once (a few seconds that loop), its loudness by how hard it rains. (Since 0.5.0: louder the
        /// faster the craft goes through it, as more drops meet it; and from the cockpit the drumming of the drops on its shell,
        /// the walls taking off the hiss above some 1.8 kHz and the shell ringing louder than the rain outside does.)
        /// </summary>
        void Sound(float rain, float going)
        {
            bool inside = Inside;
            float meets = 1f + Mathf.Clamp01(going / 150f);
            float want = Weather.Patter * GameSettings.AMBIENCE_VOLUME * Mathf.Pow(Mathf.Clamp01(rain * meets), 0.6f) * (inside ? 1.35f : 1f) * (TimeWarp.CurrentRate > 4.1f ? 0f : 1f);
            // (since 0.6.0, where the mod has its recordings: real rain, light, medium and hard crossfaded by how hard it falls, and
            // from the cockpit the drops on its glass; the made hiss let go)
            if (Loops.Can("rain-light") && Loops.Can("rain-medium") && Loops.Can("rain-hard"))
            {
                if (hiss != null && hiss.isPlaying) { hiss.volume = Mathf.MoveTowards(hiss.volume, 0f, Time.unscaledDeltaTime * 0.5f); if (hiss.volume <= 0f) hiss.Stop(); }
                float r = Mathf.Clamp01(rain * meets);
                float hard = Noise.Smooth(0.55f, 0.85f, r), light = 1f - Noise.Smooth(0.2f, 0.5f, r), medium = Mathf.Max(0f, 1f - hard - light);
                bool glass = inside && Loops.Can("rain-cockpit");
                if (want >= 0.005f)
                {
                    if (glass) rainLoops.Want("rain-cockpit", want);
                    else { rainLoops.Want("rain-light", want * Mathf.Sqrt(light)); rainLoops.Want("rain-medium", want * Mathf.Sqrt(medium)); rainLoops.Want("rain-hard", want * Mathf.Sqrt(hard)); }
                }
                rainLoops.Step(0.4f, inside && !glass ? 1800f : 22000f);
                return;
            }
            if (want < 0.005f) { if (hiss != null && hiss.isPlaying) { hiss.volume = Mathf.MoveTowards(hiss.volume, 0f, Time.unscaledDeltaTime * 0.5f); if (hiss.volume <= 0f) hiss.Stop(); } return; }
            if (hiss == null)
            {
                var go = new GameObject("NaturalWeather rain sound");
                hiss = go.AddComponent<AudioSource>();
                hiss.spatialBlend = 0f; hiss.loop = true; hiss.playOnAwake = false; hiss.volume = 0f;
                hissClip = MakeHiss();
                hiss.clip = hissClip;
                hissMuffle = go.AddComponent<AudioLowPassFilter>();
            }
            if (hissMuffle != null) { hissMuffle.cutoffFrequency = inside ? 1800f : 22000f; hissMuffle.lowpassResonanceQ = inside ? 2f : 1f; }
            if (!hiss.isPlaying) hiss.Play();
            hiss.volume = Mathf.MoveTowards(hiss.volume, want, Time.unscaledDeltaTime * 0.3f);
        }

        /// <summary>The wind's sound near the ground: as loud as the wind is strong (from about 5 m/s), swelling and dropping with each gust; none high up, where the craft's own noise is all.</summary>
        void Roar(Vector3 gust)
        {
            float speed = new Vector2(gust.x, gust.y).magnitude, agl = gust.z;
            float want = Weather.Whoosh * GameSettings.AMBIENCE_VOLUME * Noise.Smooth(4f, 22f, speed) * Noise.Smooth(400f, 80f, agl) * (TimeWarp.CurrentRate > 4.1f ? 0f : 1f);
            // (since 0.6.0, where the mod has its recordings: real wind, gusty, strong and a hurricane's crossfaded by how hard it
            // blows, and from the cockpit its howl at the walls; the made roar let go)
            if (Loops.Can("wind-gusty") && Loops.Can("wind-strong") && Loops.Can("wind-hurricane"))
            {
                if (roar != null && roar.isPlaying) { roar.volume = Mathf.MoveTowards(roar.volume, 0f, Time.unscaledDeltaTime * 0.5f); if (roar.volume <= 0f) roar.Stop(); }
                float hurricane = Noise.Smooth(22f, 32f, speed), gusty = 1f - Noise.Smooth(9f, 17f, speed), strong = Mathf.Max(0f, 1f - gusty - hurricane);
                bool walls = Inside && Loops.Can("wind-inside");
                if (want >= 0.004f)
                {
                    if (walls) windLoops.Want("wind-inside", want);
                    else { windLoops.Want("wind-gusty", want * Mathf.Sqrt(gusty)); windLoops.Want("wind-strong", want * Mathf.Sqrt(strong)); windLoops.Want("wind-hurricane", want * Mathf.Sqrt(hurricane)); }
                }
                windLoops.Step(0.8f);
                return;
            }
            if (want < 0.004f) { if (roar != null && roar.isPlaying) { roar.volume = Mathf.MoveTowards(roar.volume, 0f, Time.unscaledDeltaTime * 0.5f); if (roar.volume <= 0f) roar.Stop(); } return; }
            if (roar == null)
            {
                var go = new GameObject("NaturalWeather wind sound");
                roar = go.AddComponent<AudioSource>();
                roar.spatialBlend = 0f; roar.loop = true; roar.playOnAwake = false; roar.volume = 0f;
                roarClip = MakeRoar();
                roar.clip = roarClip;
            }
            if (!roar.isPlaying) roar.Play();
            roar.volume = Mathf.MoveTowards(roar.volume, want, Time.unscaledDeltaTime * 0.8f);
            roar.pitch = 0.85f + 0.3f * Noise.Smooth(4f, 25f, speed);
        }

        /// <summary>
        /// (Since 0.5.0.) The air rushing past the craft in flight: a rushing noise as loud as the air presses on it (from half a
        /// kilopascal of dynamic pressure, a light aeroplane's, to some thirty, a rocket's near its hardest), higher the faster it
        /// goes, heard while the camera goes along with the craft; from the cockpit, muffled by its walls. None in a vacuum, nor
        /// under fast time warp.
        /// </summary>
        void Airflow(Vessel ship, Camera cam)
        {
            float want = 0f, speed = 0f;
            if (ship != null && cam != null && ship.mainBody != null && ship.mainBody.atmosphere && !ship.LandedOrSplashed && Weather.On && !MapView.MapIsEnabled
                && (ship.CoMD - (Vector3d)cam.transform.position).sqrMagnitude < 300.0 * 300.0)
            {
                float q = (float)ship.dynamicPressurekPa;
                speed = (float)ship.srfSpeed;
                want = Weather.Whoosh * GameSettings.AMBIENCE_VOLUME * Mathf.Pow(Noise.Smooth(0.5f, 30f, q), 0.8f) * (Inside ? 0.8f : 1f) * (TimeWarp.CurrentRate > 4.1f ? 0f : 1f);
            }
            if (want < 0.004f) { if (rush != null && rush.isPlaying) { rush.volume = Mathf.MoveTowards(rush.volume, 0f, Time.unscaledDeltaTime * 0.8f); if (rush.volume <= 0f) rush.Stop(); } return; }
            if (rush == null)
            {
                var go = new GameObject("NaturalWeather airflow sound");
                rush = go.AddComponent<AudioSource>();
                rush.spatialBlend = 0f; rush.loop = true; rush.playOnAwake = false; rush.volume = 0f;
                rushClip = MakeRush();
                rush.clip = rushClip;
                rushMuffle = go.AddComponent<AudioLowPassFilter>();
            }
            if (rushMuffle != null) rushMuffle.cutoffFrequency = Inside ? 700f : 22000f;
            if (!rush.isPlaying) rush.Play();
            rush.volume = Mathf.MoveTowards(rush.volume, want, Time.unscaledDeltaTime * 1.2f);
            rush.pitch = 0.75f + 0.5f * Mathf.Clamp01(speed / 600f);
        }

        /// <summary>(Since 0.5.0.) Air rushing past a hull: the wind's low rush with more of the hiss of air torn at edges and seams.</summary>
        static AudioClip MakeRush()
        {
            const int rate = 22050, seconds = 5;
            int n = rate * seconds;
            var buf = new float[n];
            var rng = new System.Random(9071);
            float a = 0f, b = 0f, c = 0f;
            for (int k = 0; k < n; k++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                a = 0.95f * a + 0.05f * white; b = 0.85f * b + 0.15f * a; c = 0.45f * c + 0.55f * white;
                float swell = 0.8f + 0.2f * Mathf.Sin(k * 2f * Mathf.PI / (rate * 1.7f)) * Mathf.Sin(k * 2f * Mathf.PI / (rate * 0.6f) + 0.4f);
                buf[k] = (b * 4f + c * 0.3f) * swell;
            }
            int fade = rate / 2;
            for (int k = 0; k < fade; k++) { float t = (float)k / fade; buf[k] = buf[k] * t + buf[n - fade + k] * (1f - t); }
            float peak = 1e-6f;
            for (int k = 0; k < n - fade; k++) peak = Mathf.Max(peak, Mathf.Abs(buf[k]));
            var clip = AudioClip.Create("airflow", n - fade, 1, rate, false);
            var data = new float[n - fade];
            for (int k = 0; k < data.Length; k++) data[k] = buf[k] / peak * 0.7f;
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeRoar()
        {
            const int rate = 22050, seconds = 6;
            int n = rate * seconds;
            var buf = new float[n];
            var rng = new System.Random(4242);
            float a = 0f, b = 0f, c = 0f;
            for (int k = 0; k < n; k++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                a = 0.97f * a + 0.03f * white; b = 0.9f * b + 0.1f * a; c = 0.6f * c + 0.4f * white;
                // (low rush, with a little of the hiss of air past edges, rising and falling slowly)
                float swell = 0.65f + 0.35f * Mathf.Sin(k * 2f * Mathf.PI / (rate * 3f)) * Mathf.Sin(k * 2f * Mathf.PI / (rate * 1.3f) + 1f);
                buf[k] = (b * 6f + c * 0.08f) * swell;
            }
            int fade = rate / 2;
            for (int k = 0; k < fade; k++) { float t = (float)k / fade; buf[k] = buf[k] * t + buf[n - fade + k] * (1f - t); }
            float peak = 1e-6f;
            for (int k = 0; k < n - fade; k++) peak = Mathf.Max(peak, Mathf.Abs(buf[k]));
            var clip = AudioClip.Create("wind", n - fade, 1, rate, false);
            var data = new float[n - fade];
            for (int k = 0; k < data.Length; k++) data[k] = buf[k] / peak * 0.7f;
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeHiss()
        {
            const int rate = 22050, seconds = 4;
            int n = rate * seconds;
            var buf = new float[n];
            var rng = new System.Random(12345);
            float lp = 0f, hp = 0f, last = 0f;
            for (int k = 0; k < n; k++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = 0.55f * lp + 0.45f * white;                 // (soft at the top)
                hp = 0.985f * (hp + lp - last); last = lp;        // (and no rumble)
                buf[k] = hp * 0.5f;
            }
            // the drops themselves: thousands of small ticks, each a short decaying click
            for (int d = 0; d < seconds * 900; d++)
            {
                int at = rng.Next(n);
                float amp = (float)(0.15 + 0.5 * rng.NextDouble() * rng.NextDouble());
                for (int k = 0; k < 60; k++) { int i = (at + k) % n; buf[i] += amp * Mathf.Exp(-k / 9f) * (k % 2 == 0 ? 1f : -0.6f); }
            }
            // the ends made to meet, so that the loop is not heard
            int fade = rate / 4;
            for (int k = 0; k < fade; k++) { float t = (float)k / fade; buf[k] = buf[k] * t + buf[n - fade + k] * (1f - t); }
            float peak = 1e-6f;
            for (int k = 0; k < n - fade; k++) peak = Mathf.Max(peak, Mathf.Abs(buf[k]));
            var clip = AudioClip.Create("rain", n - fade, 1, rate, false);
            var data = new float[n - fade];
            for (int k = 0; k < data.Length; k++) data[k] = buf[k] / peak * 0.7f;
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// The drops, flakes and dust in the light there is: the scene's light as Natural Light (or the game) sets it, the ambient
        /// probe and the sun, so that they are as bright as everything else lit by it.
        ///
        /// A drop is a lens: it shows a small, upside-down, wide-angle picture of what is round it, the sky above all, and none of
        /// what is straight behind it (Garg and Nayar). Smeared over the exposure into a streak, it covers what is behind it only
        /// for the moment it passes, and adds its little picture: so it is drawn added, faintly, as bright as the light from the
        /// half of the world in front of the camera (the probe facing the way the camera looks: mostly sky when it looks up,
        /// mostly ground when it looks down). Seen against dark things (a shaded wall, a hillside, the night) it shows as a fine
        /// silvery line; against a bright sky, hardly at all; never as a dark smear. Towards the sun, where the sun is out, each
        /// drop throws the sun's light forward: rain against a low sun is a shower of bright lines.
        ///
        /// A snowflake throws back most of the light on it, every way: it is as bright as white snow on the ground in the same
        /// light, lit by the sky from above and by the sun (more on the side the sun is, and a glint of it towards the sun). Against
        /// a bright sky it is no brighter than the sky, and greyer looking up into it, as real snow is; against anything darker, white.
        ///
        /// Dust is the ground's colour, lit the same way.
        /// </summary>
        void Shade(Camera cam, float rain, Vector3 down)
        {
            if (cam == null) return;
            Vector3 ahead = cam.transform.forward, up = -down;
            Vector3 level = Vector3.ProjectOnPlane(ahead, up);
            if (level.sqrMagnitude < 1e-4f) level = Vector3.ProjectOnPlane(cam.transform.up, up);
            level = level.normalized;
            Vector3 aside = Vector3.Cross(up, level);
            looks[0] = ahead; looks[1] = up; looks[2] = -ahead; looks[3] = aside; looks[4] = -aside; looks[5] = level;
            RenderSettings.ambientProbe.Evaluate(looks, seen);
            Color infront = Positive(seen[0]), above = Positive(seen[1]), behind = Positive(seen[2]);
            Color around = Positive((seen[1] + seen[3] + seen[4] + seen[5] + seen[2]) * 0.2f);
            // the sun, as it is here (Natural Light dims it for the clouds in the way)
            Light sun = Sun.Instance != null ? Sun.Instance.sunLight : null;
            Color sunShown = Color.black;
            float towards = 0f, facing = 0f;
            if (sun != null && sun.isActiveAndEnabled)
            {
                Vector3 toSun = -sun.transform.forward;
                sunShown = sun.color * sun.intensity;
                float high = Vector3.Dot(toSun, up);
                // (none once it is down; and it reaches through the air only as Natural Light's colour of it says)
                sunShown *= Mathf.Clamp01(high * 12f + 0.3f);
                float c = Vector3.Dot(ahead, toSun);
                // (how near the camera looks to the sun: a broad lobe, since the drops all over the picture are seen at angles to it)
                float angle = Mathf.Acos(Mathf.Clamp(c, -1f, 1f));
                towards = Mathf.Exp(-angle * angle / (2f * 0.5f * 0.5f));
                facing = 0.5f - 0.5f * c;           // 1 with the sun behind the camera: the lit side of every flake towards it
            }
            // ---- drops: laid over the picture, each as bright as the light it bends towards the eye from all round it (the sky
            // above it, the ground under it), and brighter towards the sun where the sun is ahead. So a streak against a bright
            // sky is all but gone, and against the dark sea, the ground or a craft it shows lighter, as real rain does; it is
            // as opaque at its middle as a drop is wide against the length it is smeared over, made up to what the eye sees
            // of the many drops that make one streak's worth of the picture.
            // (most of what a drop bends towards the eye is the sky above and behind it: brighter than the light all round)
            Color drop = Shown(around * 0.5f + above * 0.7f) + sunShown * (0.25f * towards);
            float opaque = 0.34f + 0.26f * Mathf.Clamp01(rain);
            // (the shader doubles its tint: a half is as it is)
            dropMat.SetColor("_TintColor", new Color(Mathf.Min(drop.r, 1f) * 0.5f, Mathf.Min(drop.g, 1f) * 0.5f, Mathf.Min(drop.b, 1f) * 0.5f, opaque * 0.5f));
            // ---- flakes: white, as bright as snow on the ground (lit from above, and from where the camera is), the sun's side brighter;
            // and never darker than the veil the snowfall makes in the distance (Natural Light's, lit from all round), which is the same
            // flakes, further off: a little brighter, since one near the camera is lit from the open sky above it and seen from its lit side
            Color snowLit = (Shown((above * 0.6f + behind * 0.4f) * 1.5f) + sunShown * (0.22f + 0.38f * facing + 0.3f * towards)) * 0.9f;
            // (Natural Light's veil of falling snow is lit as the light from all round, twice and a little over for snow: the flakes near the
            // camera are kept a fifth brighter than that, as they look against it. Measured under an overcast with heavy snow, the
            // flakes lit as snow on the ground alone came out at 0.20 against the veil's 0.22, and showed as dark specks.)
            Color veilLike = Shown(around * 2f) * 1.2f;
            snowLit = new Color(Mathf.Max(snowLit.r, veilLike.r), Mathf.Max(snowLit.g, veilLike.g), Mathf.Max(snowLit.b, veilLike.b));
            SetLaid(flakeMat, snowLit, 0.8f);
            { float most = Mathf.Max(1f, Mathf.Max(snowLit.r, Mathf.Max(snowLit.g, snowLit.b))); SnowLit = snowLit / most; }
            // ---- dust: as bright as the murk it makes (Natural Light's veil of it is lit from all round the same way: in a dust storm the
            // light all round is the dust's colour already), a little lighter, since a grain near the camera is lit from every side
            Color dustLit = Shown(around * 1.4f) + sunShown * (0.2f + 0.25f * facing);
            SetLaid(moteMat, new Color(dustLit.r, dustLit.g * 0.92f, dustLit.b * 0.82f), 0.35f);
#if DEV
            seenText = "ahead " + Shown(infront).ToString("F2") + ", above " + Shown(above).ToString("F2") + ", behind " + Shown(behind).ToString("F2") + ", sun " + sunShown.ToString("F2")
                + " (towards it " + towards.ToString("F2") + ", behind the camera " + facing.ToString("F2") + "); drops add " + drop.ToString("F3") + ", flakes " + snowLit.ToString("F2") + ", dust " + dustLit.ToString("F2");
#endif
        }

        readonly Vector3[] looks = new Vector3[6];
        readonly Color[] seen = new Color[6];

        static Color Positive(Color c) => new Color(Mathf.Max(0f, c.r), Mathf.Max(0f, c.g), Mathf.Max(0f, c.b));

        /// <summary>Light as it shows (the probe is light: the game turns it into shown values itself).</summary>
        static Color Shown(Color light) => new Color(Mathf.LinearToGammaSpace(light.r), Mathf.LinearToGammaSpace(light.g), Mathf.LinearToGammaSpace(light.b));

        /// <summary>A colour laid over the picture with the game's alpha-blended particle shader, which doubles its tint.</summary>
        static void SetLaid(Material mat, Color shown, float alpha)
        {
            if (mat == null) return;
            float most = Mathf.Max(shown.r, Mathf.Max(shown.g, shown.b));
            if (most > 1f) shown /= most;
            mat.SetColor("_TintColor", new Color(shown.r * 0.5f, shown.g * 0.5f, shown.b * 0.5f, alpha * 0.5f));
        }

#if DEV
        static string seenText = "";
        /// <summary>(For the development build: the light the drops and flakes were last lit with, and what they were given.)</summary>
        public static string Seen() => seenText;
        static bool old;
        Material oldDropMat;
        /// <summary>(For the development build: the drops and flakes lit as in 0.1.0, and back: for pictures of before and after.)</summary>
        public static string Old() { old = !old; return old ? "drops and flakes lit as in 0.1.0" : "drops and flakes lit as now"; }

        /// <summary>(As in 0.1.0: the probe's light from five ways and a little of the sun's, laid over the picture. Kept for the development build's pictures of before and after.)</summary>
        static void Light(Material mat, float alpha) => Light(mat, alpha, Color.white);

        static void Light(Material mat, float alpha, Color tint)
        {
            if (mat == null) return;
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            var dirs = new[] { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            var got = new Color[dirs.Length];
            probe.Evaluate(dirs, got);
            Color sum = Color.black;
            foreach (Color c in got) sum += c;
            sum /= dirs.Length;
            Light sun = Sun.Instance != null ? Sun.Instance.sunLight : null;
            if (sun != null) sum += sun.color.linear * (sun.intensity * 0.12f);
            Color shown = new Color(Mathf.Sqrt(Mathf.Max(0f, sum.r)), Mathf.Sqrt(Mathf.Max(0f, sum.g)), Mathf.Sqrt(Mathf.Max(0f, sum.b)));
            shown = new Color(shown.r * tint.r, shown.g * tint.g, shown.b * tint.b);
            float most = Mathf.Max(shown.r, Mathf.Max(shown.g, shown.b));
            if (most > 1f) shown /= most;
            // (the legacy shader doubles its tint: half of what is wanted)
            mat.SetColor("_TintColor", new Color(shown.r * 0.5f, shown.g * 0.5f, shown.b * 0.5f, alpha * 0.5f));
        }
#endif

        /// <summary>A streak: bright down its middle, soft at its sides, faded at both ends.</summary>
        static Texture2D MakeStreak()
        {
            const int W = 8, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NaturalWeather streak" };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float across = (x + 0.5f) / W * 2f - 1f, along = (y + 0.5f) / H;
                    float a = Mathf.Exp(-across * across * 4f) * Mathf.Sin(along * Mathf.PI);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply(false, true);
            return t;
        }

        /// <summary>A flake: a soft dot.</summary>
        static Texture2D MakeDot()
        {
            const int S = 16;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NaturalWeather flake" };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float r2 = dx * dx + dy * dy;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - r2) * Mathf.Clamp01(1f - r2)));
                }
            t.Apply(false, true);
            return t;
        }
    }
}
