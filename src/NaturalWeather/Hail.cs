using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// Hail and ice pellets round the camera: things that fall hard and bounce.
    ///
    /// Hail (see Model.Hail for when and where): balls of ice from 6 to 50 mm across, falling at about 4.4 times the square
    /// root of their size in millimetres (14 m/s for a pea, 31 for a golf ball: the speed at which the air's drag holds up
    /// their weight), far fewer of them in a cubic metre than raindrops. A stone that strikes the ground, a building or a
    /// craft bounces a third to a half as fast as it came, once or twice, and lies; stones of 20 mm and more break what is
    /// thin and spread out (deployed solar panels, aerials, radiators: there is a switch).
    ///
    /// Ice pellets (sleet, in North America): raindrops frozen on the way down, 2 to 5 mm, clear, falling at 8 to 10 m/s,
    /// that bounce and rattle where rain would splash.
    ///
    /// Both are Unity's particles in the world's own space (so that one that has landed stays where it landed while the
    /// camera moves), with the game's particle shader: nothing here needs a shader of its own. Above about 80 m/s of the
    /// camera's own speed nothing is seen to bounce, and they are not drawn.
    /// </summary>
    public sealed class Hail : MonoBehaviour
    {
        GameObject follower;
        ParticleSystem stones, grains;
        Material stoneMat, grainMat;
        Texture2D ball;
        AudioSource clatter;
        AudioLowPassFilter clatterMuffle;
        AudioClip clatterClip;
        float breaksAt;
        public static string Report = "";

        const float Box = 22f;       // how far round the camera they are drawn

        void Start()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Alpha Blended");
            if (shader == null) { Report = "Hail cannot be drawn: the game's particle shader was not found."; enabled = false; return; }
            ball = MakeBall();
            // (named, so that Natural Light's tint of smoke leaves them alone, as it does the rain's)
            stoneMat = new Material(shader) { mainTexture = ball, renderQueue = 3100, name = "NaturalWeather hail" };
            grainMat = new Material(shader) { mainTexture = ball, renderQueue = 3100, name = "NaturalWeather pellets" };
            follower = new GameObject("NaturalWeather hail");
            stones = Make("hail", stoneMat, 4000, 0.4f);
            grains = Make("pellets", grainMat, 6000, 0.45f);
        }

        ParticleSystem Make(string name, Material mat, int most, float bounce)
        {
            var go = new GameObject(name);
            go.transform.SetParent(follower.transform, false);
            go.layer = 0;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.maxParticles = most; main.startLifetime = 4f; main.gravityModifier = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.283f);
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(Box, 1f, Box);
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0f;
            // (they start at the speed they fall at, and the world pulls them down: the air's drag, which is what keeps a stone
            // at that speed, is a limit on how fast they go; after a bounce they rise, slow and fall back as a thrown thing does)
            var force = ps.forceOverLifetime; force.enabled = true; force.space = ParticleSystemSimulationSpace.World;
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 1f; limit.space = ParticleSystemSimulationSpace.World;
            var hit = ps.collision;
            hit.enabled = true; hit.type = ParticleSystemCollisionType.World; hit.mode = ParticleSystemCollisionMode.Collision3D;
            hit.quality = ParticleSystemCollisionQuality.Medium; hit.collidesWith = (1 << 0) | (1 << 15);
            hit.bounce = new ParticleSystem.MinMaxCurve(bounce * 0.6f, bounce * 1.2f); hit.dampen = 0.25f; hit.lifetimeLoss = 0.35f;
            hit.radiusScale = 0.5f; hit.sendCollisionMessages = false; hit.maxCollisionShapes = 64; hit.enableDynamicColliders = true;
            var drawn = go.GetComponent<ParticleSystemRenderer>();
            drawn.sharedMaterial = mat;
            // (short strokes along their way, as a shutter smears anything that falls: see Rain)
            drawn.renderMode = ParticleSystemRenderMode.Stretch; drawn.lengthScale = 1f; drawn.velocityScale = 1f / 220f; drawn.cameraVelocityScale = 0f;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; drawn.receiveShadows = false;
            drawn.minParticleSize = 0f; drawn.maxParticleSize = 0.02f;
            try { FloatingOrigin.RegisterParticleSystem(ps); } catch (Exception) { }
            ps.Play();
            return ps;
        }

        void OnDestroy()
        {
            foreach (ParticleSystem ps in new[] { stones, grains })
                if (ps != null) { try { FloatingOrigin.UnregisterParticleSystem(ps); } catch (Exception) { } }
            if (clatter != null) Destroy(clatter.gameObject);
            if (clatterClip != null) Destroy(clatterClip);
            if (follower != null) Destroy(follower);
            if (stoneMat != null) Destroy(stoneMat);
            if (grainMat != null) Destroy(grainMat);
            if (ball != null) Destroy(ball);
        }

        void LateUpdate()
        {
            if (follower == null) return;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody body = FlightGlobals.currentMainBody;
            bool wanted = cam != null && body != null && body.atmosphere && Weather.On && Weather.Rain && Model.Allowed(body) && !MapView.MapIsEnabled;
            float hail = 0f, size = 0f, pellets = 0f;
            Vector3 wind = Vector3.zero, down = Vector3.down;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 camVel = Vector3.zero;
            Vessel ship = FlightGlobals.ActiveVessel;
            if (ship != null && cam != null && (ship.CoMD - (Vector3d)camPos).sqrMagnitude < 2000.0 * 2000.0) camVel = (Vector3)ship.srf_velocity;
            if (wanted)
            {
                double lat = body.GetLatitude(camPos), lon = body.GetLongitude(camPos), alt = body.GetAltitude(camPos);
                World w = World.Of(body);
                double ut = Planetarium.GetUniversalTime();
                WeatherHere here = Model.Here(w, lat, lon, ut);
                // (they fall out of the cloud: none above its top)
                float under = here.Low.Cover > 0.05f ? Noise.Smooth(here.Low.Top, here.Low.Top - 400f, (float)alt) : 1f;
                hail = here.Hail * under; size = here.HailSize;
                pellets = here.Rain * here.Pellets * (here.Low.Cover > 0.05f ? Noise.Smooth(here.Low.Top, here.Low.Base, (float)alt) : 1f);
                Provider.Axes(body, camPos, out Vector3d e, out Vector3d n, out Vector3d u);
                down = -(Vector3)u;
                Column col = Model.ColumnAt(w, lat, lon, ut);
                double ground = body.TerrainAltitude(lat, lon, true);
                if (body.ocean && ground < 0.0) ground = 0.0;
                Vector3 m = Model.Mean(col, alt, (float)Math.Max(1.0, alt - ground));
                wind = (Vector3)(e * m.x + n * m.y);
            }
            // (too fast for anything to be seen bouncing)
            float seen = Noise.Smooth(80f, 55f, camVel.magnitude);
            follower.transform.position = camPos;
            follower.transform.rotation = Quaternion.identity;
            float stoneFall = 4.4f * Mathf.Sqrt(Mathf.Max(size, 4f));
            // (drawn half as large again as they are: a stone of 10 mm at ten metres is under a pixel)
            Feed(stones, ref owedStones, hail * seen, stoneFall, Mathf.Max(size, 6f) * 0.0015f, 420f, wind, down, camVel);
            Feed(grains, ref owedGrains, pellets * seen, 9f, 0.007f, 2600f, wind, down, camVel);
            // (lit as snow is: the rain's own colour for a flake, which is white in the light there is)
            Color lit = Rain.SnowLit;
            stoneMat.SetColor("_TintColor", new Color(lit.r * 0.5f, lit.g * 0.5f, lit.b * 0.5f, 0.5f));
            grainMat.SetColor("_TintColor", new Color(lit.r * 0.5f, lit.g * 0.5f, lit.b * 0.5f, 0.32f));
            Sound(Mathf.Max(hail * Mathf.Clamp01(size / 20f + 0.3f), pellets * 0.35f), size);
            if (wanted && Weather.HailBreaks != null && Weather.HailBreaks) Breaks(body);
            Report = hail > 0.02f ? "Hail here, stones of " + Mathf.RoundToInt(size) + " mm." : pellets > 0.02f ? "Ice pellets here." : "";
        }

        void Feed(ParticleSystem ps, ref float owed, float amount, float fall, float across, float most, Vector3 wind, Vector3 down, Vector3 camVel)
        {
            var emission = ps.emission;
            if (amount < 0.01f) { emission.rateOverTime = 0f; owed = 0f; return; }
            Vector3 air = wind + down * fall;
            Vector3 rel = air - camVel;
            float life = Mathf.Clamp(Box / Mathf.Max(rel.magnitude, 1f), 0.4f, 3f);
            var main = ps.main;
            // (long enough to fall through the box, bounce and lie a moment)
            main.startLifetime = new ParticleSystem.MinMaxCurve(life + 1.2f, life + 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(across * 0.7f, across * 1.3f);
            // (in the world's space each starts with the air's own motion; the shape stands upstream of where the camera will be)
            var velocity = ps.velocityOverLifetime; velocity.enabled = false;
            main.startSpeed = 0f;
            var inherit = ps.inheritVelocity; inherit.enabled = false;
            var shape = ps.shape;
            shape.position = -rel * (life * 0.5f) + (-down) * (Box * 0.25f);
            shape.scale = new Vector3(Box, Box * 0.5f, Box);
            var force = ps.forceOverLifetime; Vector3 g = down * 9.81f; force.x = g.x; force.y = g.y; force.z = g.z;
            var limit = ps.limitVelocityOverLifetime; limit.limit = fall * 1.05f;
            float rate = most * Mathf.Pow(Mathf.Clamp01(amount), 0.7f) / life;
            emission.rateOverTime = 0f;
            // (given their start by hand: Unity's own "start speed" goes along the shape's own direction, not the air's)
            owed += rate * Time.deltaTime;
            int n = Mathf.Min(400, (int)owed);
            if (n <= 0) return;
            owed -= n;
            var one = new ParticleSystem.EmitParams { applyShapeToPosition = true, velocity = air };
            ps.Emit(one, n);
        }
        float owedStones, owedGrains;

        /// <summary>Hail's and pellets' rattle: clicks with a ring to them, made once; louder the harder it comes down, lower for larger stones.
        /// (Since 0.5.0: from the cockpit, the stones on its shell, louder and duller through its walls.)</summary>
        void Sound(float amount, float size)
        {
            bool inside = CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA;
            float want = Weather.Patter * GameSettings.AMBIENCE_VOLUME * Mathf.Pow(Mathf.Clamp01(amount), 0.6f) * (inside ? 1.5f : 1f) * (TimeWarp.CurrentRate > 4.1f ? 0f : 1f);
            if (want < 0.005f) { if (clatter != null && clatter.isPlaying) { clatter.volume = Mathf.MoveTowards(clatter.volume, 0f, Time.unscaledDeltaTime * 0.5f); if (clatter.volume <= 0f) clatter.Stop(); } return; }
            if (clatter == null)
            {
                var go = new GameObject("NaturalWeather hail sound");
                clatter = go.AddComponent<AudioSource>();
                clatter.spatialBlend = 0f; clatter.loop = true; clatter.playOnAwake = false; clatter.volume = 0f;
                clatterClip = MakeClatter();
                clatter.clip = clatterClip;
                clatterMuffle = go.AddComponent<AudioLowPassFilter>();
            }
            if (clatterMuffle != null) clatterMuffle.cutoffFrequency = inside ? 3000f : 22000f;
            if (!clatter.isPlaying) clatter.Play();
            clatter.volume = Mathf.MoveTowards(clatter.volume, want, Time.unscaledDeltaTime * 0.4f);
            clatter.pitch = Mathf.Lerp(1.25f, 0.7f, Mathf.Clamp01((size - 5f) / 40f));
        }

        static AudioClip MakeClatter()
        {
            const int rate = 22050, seconds = 4;
            int n = rate * seconds;
            var buf = new float[n];
            var rng = new System.Random(77123);
            // (each strike: a click, and the short ring of what it struck, at a pitch of its own)
            for (int d = 0; d < seconds * 260; d++)
            {
                int at = rng.Next(n);
                float amp = (float)(0.2 + 0.8 * rng.NextDouble() * rng.NextDouble());
                float pitch = (float)(900.0 + 2600.0 * rng.NextDouble());
                float decay = (float)(0.0015 + 0.004 * rng.NextDouble()) * rate;
                for (int k = 0; k < 420; k++)
                {
                    int i = (at + k) % n;
                    buf[i] += amp * Mathf.Exp(-k / decay) * Mathf.Sin(6.2831853f * pitch * k / rate) + (k < 6 ? amp * 0.6f * (float)(rng.NextDouble() * 2.0 - 1.0) : 0f);
                }
            }
            float peak = 1e-6f;
            for (int k = 0; k < n; k++) peak = Mathf.Max(peak, Mathf.Abs(buf[k]));
            var clip = AudioClip.Create("hail", n, 1, rate, false);
            for (int k = 0; k < n; k++) buf[k] = buf[k] / peak * 0.7f;
            clip.SetData(buf, 0);
            return clip;
        }

        /// <summary>
        /// What large hail breaks: thin things spread out in it. Once a second, for every craft about that stands or flies in
        /// hail of 20 mm and more: each deployed solar panel, aerial or radiator that can break at all may, the likelier the
        /// larger the stones and the thicker they fall (a panel in the worst of it lasts some seconds).
        /// </summary>
        void Breaks(CelestialBody body)
        {
            if (Time.time < breaksAt) return;
            breaksAt = Time.time + 1f;
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            List<Vessel> all = FlightGlobals.VesselsLoaded;
            for (int i = 0; i < all.Count; i++)
            {
                Vessel v = all[i];
                if (v == null || v.packed || v.mainBody != body) continue;
                WeatherHere here = Model.Here(w, v.latitude, v.longitude, ut);
                if (here.Hail < 0.1f || here.HailSize < 20f || v.altitude > here.Low.Top) continue;
                float chance = here.Hail * Mathf.Clamp01((here.HailSize - 18f) / 22f) * 0.2f;
                for (int k = 0; k < v.parts.Count; k++)
                {
                    Part p = v.parts[k];
                    if (p == null) continue;
                    var m = p.FindModuleImplementing<ModuleDeployablePart>();
                    if (m == null || !m.isBreakable || m.deployState != ModuleDeployablePart.DeployState.EXTENDED) continue;
                    if (UnityEngine.Random.value > chance) continue;
                    try
                    {
                        m.breakPanels();
                        ScreenMessages.PostScreenMessage("Hail has broken " + p.partInfo.title + ".", 5f, ScreenMessageStyle.UPPER_CENTER);
                        Weather.Log("hail (" + Mathf.RoundToInt(here.HailSize) + " mm) broke " + p.partInfo.title + " on " + v.GetDisplayName());
                    }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>A ball of ice: a disc, brighter towards one side, soft at its rim.</summary>
        static Texture2D MakeBall()
        {
            const int S = 32;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NaturalWeather ice" };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float r2 = dx * dx + dy * dy;
                    float a = Mathf.Clamp01((1f - r2) * 6f);
                    float shade = 0.78f + 0.22f * Mathf.Clamp01(0.5f + 0.6f * (dy - dx) * 0.7f) - 0.12f * r2;
                    t.SetPixel(x, y, new Color(shade, shade, shade, a));
                }
            t.Apply(false, true);
            return t;
        }
    }
}
