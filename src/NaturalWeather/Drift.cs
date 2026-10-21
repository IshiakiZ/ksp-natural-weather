using System;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.5.0.) Snow drifting along the ground in the wind, where snow lies (see Snow): the "snow snakes" of a windy winter
    /// day, ribbons of fine snow streaming over the ground, rising off it in a strong wind.
    ///
    /// When. Lying snow starts to move when the wind at 10 m passes a threshold: some 4 to 5 m/s for new, cold, dry snow, about
    /// 7.7 for older dry snow, about 9.9 for wet snow, whose grains are bound by its water (field measures on the Canadian
    /// prairies, Li and Pomeroy 1997; Liston and others 2007). Above freezing, none: it is wet, and soon slush. The harder the
    /// wind beyond its threshold, the more of it goes.
    ///
    /// How it looks. Most of it hops along a layer a few millimetres to ten or fifteen centimetres deep, at about half the wind's
    /// speed at 10 m (the wind over snow by the log profile, the snow's roughness a millimetre); in a strong wind some is carried
    /// up a metre or two. It goes in streamers: dense, long clouds along the wind, which the slow streaks of the air next to the
    /// ground gather it into (Watanabe and others 2024), wandering from side to side, coming and going.
    ///
    /// Here: tufts of fine grains, drawn as short strokes (as the falling snow is: a dot moving several pixels a frame is smoothed away by
    /// the camera between frames), let go near the ground round the camera along a few streamers that wander and come and go,
    /// carried by the wind near the ground with its gusts, soft and overlapping into a moving veil, a shade greyer than the snow
    /// in the light there (as the falling snow is lit). Only with the camera near the ground.
    /// </summary>
    public sealed class Drift : MonoBehaviour
    {
        public static string Report = "";
        const float Area = 36f;                 // metres round the camera it is let go in, each way
        const int Streamers = 9;
        GameObject holder;
        ParticleSystem grains;
        ParticleSystemRenderer drawn;
        Material mat;
        float strength, clock;
        readonly float[] streamWay = new float[Streamers], streamPhase = new float[Streamers], streamBeat = new float[Streamers];
        System.Random luck = new System.Random(1729);
        float owed;
        // (How the tufts are drawn, 2026-10-19, after the first film in motion: at most a fifth opaque and one flat shade a little
        // greyer than the snow, the drift changed a sunlit pixel by two to four parts in a hundred and could not be seen at all, even
        // in the frames' differences. Real drifting snow shows by its make: dense cores along the streamers that hide the snow
        // under them, a lit top and a greyer underside, soft thinning edges, all moving fast (the WMO's Cloud Atlas photograph of
        // drifting snow: a low white veil streaming over the field, below eye level).)
        static float alphaLo = 0.16f, alphaHi = 0.42f, sizeLo = 0.18f, sizeHi = 0.7f, shadeLo = 0.78f, rate = 11000f;
        // (drawn out along their way: seen against the sky off a raised floor, tufts stretched half again their size were round
        // puffs, little clouds; airborne snow is streaks along the wind)
        static float stretch = 3f, speedStretch = 1f / 16f;

#if DEV
        static float held = -1f;
        static bool red;
        /// <summary>(development build) The drift held at so much (0 to 1; less than nothing lets go), whatever the snow and the wind; "red" draws the tufts solid red (to see that they are drawn), "white" as they are.</summary>
        public static string Hold(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "red" || a == "white") { red = a == "red"; return "tufts drawn " + a; }
            held = float.TryParse((arg ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1f;
            return held >= 0f ? "held at " + held : "the drift is the weather's own";
        }
        public static string Says() => Report;
        /// <summary>(development build) How the tufts are drawn, "alphaLo alphaHi sizeLo sizeHi shadeLo rate stretch speedStretch" (any left out stay); nothing: as they are.</summary>
        public static string Tune(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            float V(int i, float now) => i < t.Length && float.TryParse(t[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : now;
            alphaLo = V(0, alphaLo); alphaHi = V(1, alphaHi); sizeLo = V(2, sizeLo); sizeHi = V(3, sizeHi); shadeLo = V(4, shadeLo); rate = V(5, rate);
            stretch = V(6, stretch); speedStretch = V(7, speedStretch);
            return "alpha " + alphaLo + "-" + alphaHi + ", size " + sizeLo + "-" + sizeHi + " m, shade from " + shadeLo + ", " + rate + " tufts a second at full strength, drawn out " + stretch + " and " + speedStretch + " s of their way";
        }
#endif

        void Start()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Alpha Blended");
            if (shader == null) { Report = "drifting snow cannot be drawn: the game's particle shader was not found"; enabled = false; return; }
            mat = new Material(shader) { mainTexture = Grain(), renderQueue = 3100, name = "NaturalWeather drifting snow" };
            holder = new GameObject("NaturalWeather drifting snow") { layer = 0 };
            grains = holder.AddComponent<ParticleSystem>();
            grains.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = grains.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.maxParticles = 32000; main.gravityModifier = 0f; main.startLifetime = 2f;
            var emission = grains.emission; emission.enabled = true; emission.rateOverTime = 0f;
            var shape = grains.shape; shape.enabled = false;
            // (the gusts' eddies near the ground: grains lifted and dropped, the streamers torn)
            var noise = grains.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.6f; noise.scrollSpeed = 0.4f; noise.damping = true; noise.quality = ParticleSystemNoiseQuality.Low;
            // (not kept on the ground by striking it: let go just above it and living a second or three, they go a few metres
            // and do not leave it far; with tens of thousands, striking the ground was the dearest of their work)
            var fade = grains.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            drawn = holder.GetComponent<ParticleSystemRenderer>();
            drawn.sharedMaterial = mat;
            drawn.renderMode = ParticleSystemRenderMode.Stretch;
            drawn.lengthScale = stretch; drawn.velocityScale = speedStretch; drawn.cameraVelocityScale = 0f;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; drawn.receiveShadows = false;
            drawn.minParticleSize = 0f; drawn.maxParticleSize = 0.05f;
            for (int k = 0; k < Streamers; k++) { streamWay[k] = ((k + 0.5f) / Streamers * 2f - 1f) * Area; streamPhase[k] = (float)luck.NextDouble() * 6.283f; streamBeat[k] = 0.15f + 0.25f * (float)luck.NextDouble(); }
            grains.Play();
        }

        void OnDestroy()
        {
            if (holder != null) Destroy(holder);
            if (mat != null) Destroy(mat);
        }

        void LateUpdate()
        {
            if (grains == null) return;
#if DEV
            drawn.lengthScale = stretch; drawn.velocityScale = speedStretch;
#endif
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            clock += dt;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody body = FlightGlobals.currentMainBody;
            float want = 0f, u10 = 0f, threshold = 0f;
            Vector3 surfaceWind = Vector3.zero, up = Vector3.up;
            double ground = 0.0;
            bool wanted = Weather.On != null && Weather.On.Value && Weather.LyingSnow != null && Weather.LyingSnow.Value && cam != null && body != null && body.atmosphere && !MapView.MapIsEnabled;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            if (wanted)
            {
                double lat = body.GetLatitude(camPos), lon = body.GetLongitude(camPos), alt = body.GetAltitude(camPos);
                ground = body.TerrainAltitude(lat, lon, true);
                if (body.ocean && ground < 0.0) ground = 0.0;
                float agl = (float)(alt - ground);
                World w = World.Of(body);
                if (w != null && agl < 60f)
                {
                    double ut = Planetarium.GetUniversalTime();
                    Provider.Axes(body, camPos, out Vector3d e, out Vector3d n, out Vector3d u);
                    up = (Vector3)u;
                    // (the wind 10 m up with its gusts; near the snow, by the log profile, about half of it)
                    Vector3 m = Provider.Local(w, lat, lon, ground + 10.0, 10f, ut, true);
                    u10 = new Vector2(m.x, m.y).magnitude;
                    Vector3 level = (Vector3)(e * m.x + n * m.y);
                    surfaceWind = level * 0.5f;
                    // (how readily the snow goes: new cold snow at 4.5 m/s, older dry snow at 7.7, wet snow at 9.9; none above freezing)
                    float t = Snow.TemperatureHere > 0f ? Snow.TemperatureHere : 300f;
                    threshold = t < 272.15f ? Mathf.Lerp(7.7f, 4.5f, Snow.FreshHere) : t < 273.65f ? 9.9f : 1e6f;
                    float lying = Mathf.Clamp01((Snow.DeepHere - 1f) / 3f);
                    want = lying * Noise.Smooth(threshold, threshold + 6f, u10);
                }
            }
#if DEV
            if (held >= 0f && wanted)
            {
                want = held;
                if (surfaceWind.sqrMagnitude < 1f)
                {
                    // (held with no wind to speak of: blown at 6 m/s along the camera's own right)
                    Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, up);
                    surfaceWind = (right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right) * 6f;
                    u10 = 12f;
                }
            }
#endif
            strength += (want - strength) * Mathf.Min(1f, dt / 2f);
            Report = strength < 0.005f ? "none (" + (threshold > 1e5f ? "too warm" : "the wind at 10 m " + u10.ToString("F1") + " m/s against the snow's " + threshold.ToString("F1") + "; " + Snow.DeepHere.ToString("F1") + " cm lying") + ")"
                : "drifting, " + Mathf.RoundToInt(100f * strength) + "% (the wind at 10 m " + u10.ToString("F1") + " m/s against the snow's " + threshold.ToString("F1") + "), " + grains.particleCount + " tufts in the air";
            if (strength < 0.005f || surfaceWind.sqrMagnitude < 0.01f) return;
            // ---- grains let go along the streamers, each as much as it comes and goes
            Vector3 along = surfaceWind.normalized, across = Vector3.Cross(up, along);
            // (as bright as snow in this light at most: a tuft's side away from the sun is in its own shade, the lowest in the
            // streamer's own, so the drift is seen against the lit snow as a moving veil with greyer depths)
            Color lit = Rain.SnowLit;
            // (each a tuft of grains, not one: a streamer is a sheet of them, seen as it moves)
            owed += strength * rate * dt;
            int count = Mathf.Min(1500, (int)owed);
            owed -= count;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                int k = luck.Next(Streamers);
                float beat = 0.5f + 0.5f * Mathf.Sin(clock * streamBeat[k] * 6.283f + streamPhase[k]);
                if ((float)luck.NextDouble() > beat * beat) continue;
                // (where: along the wind anywhere round the camera; across it on its streamer, which wanders a few metres)
                float a = ((float)luck.NextDouble() * 2f - 1f) * Area;
                float off = 0.8f * Gauss();
                float s = streamWay[k] + 3f * Mathf.Sin(clock * 0.13f + streamPhase[k] + a * 0.05f) + off;
                Vector3 at = camPos + along * a + across * s;
                // (on the ground there: found by a line down; the grain a hand's breadth or so above it, a few higher in a strong wind)
                Vector3 top = at + up * (float)(ground + 80.0 - body.GetAltitude(at));
                if (!Physics.Raycast(top, -up, out RaycastHit floor, 200f, (1 << 0) | (1 << 15), QueryTriggerInteraction.Ignore)) continue;
                if (Vector3.Dot(floor.normal, up) < 0.6f || floor.collider.GetComponentInParent<Part>() != null) continue;     // (the ground, not a craft)
                float high = -Mathf.Log(1f - 0.999f * (float)luck.NextDouble()) * (0.06f + 0.25f * strength * strength);
                p.position = floor.point + up * (0.02f + high);
                float pace = 0.55f + 0.5f * (float)luck.NextDouble();
                p.velocity = surfaceWind * pace * (1f + 0.4f * Mathf.Clamp01(high / 0.5f)) + up * (0.25f * Gauss());
                p.startSize = sizeLo + (sizeHi - sizeLo) * (float)luck.NextDouble();
                p.startLifetime = 1.2f + 1.8f * (float)luck.NextDouble();
                // (densest along the streamer's middle, thinning to its edges; greyer low down, in the streamer's own shade)
                float core = 0.45f + 0.55f * Mathf.Exp(-off * off / 0.5f);
                float alpha = (alphaLo + (alphaHi - alphaLo) * (float)luck.NextDouble()) * core * Mathf.Clamp01(strength * 1.5f);
                float shade = Mathf.Lerp(shadeLo, 0.97f, (float)luck.NextDouble()) * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(high / 0.08f));
                p.startColor = new Color(lit.r * shade, lit.g * shade, lit.b * shade, alpha);
#if DEV
                if (red) p.startColor = new Color(1f, 0f, 0f, 1f);
#endif
                grains.Emit(p, 1);
            }
        }

        float Gauss()
        {
            double u1 = 1.0 - luck.NextDouble(), u2 = luck.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        /// <summary>A grain of snow seen through a lens: a soft round spot.</summary>
        static Texture2D Grain()
        {
            const int S = 32;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "NaturalWeather drifting grain", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f, r2 = u * u + v * v;
                    float a = Mathf.Clamp01(Mathf.Exp(-r2 * 3.5f) * (1f - r2));
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }
    }
}
