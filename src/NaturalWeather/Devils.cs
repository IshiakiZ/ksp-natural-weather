using System;
using System.Collections.Generic;
using System.Reflection;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.5.0.) Dust devils: the whirlwinds of a hot, still afternoon over dry ground, lifting its dust in a thin tall column.
    ///
    /// What they are (the research, see the wiki's Research page): the ground heated by a high sun warms the air on it far beyond
    /// the air above, and the warm air rises in plumes; where a plume draws in air that was turning a little, it spins up as it
    /// narrows, as a skater pulling in their arms, into a whirl a few metres to a few tens across, its wind ten to twenty-five
    /// metres a second round it and its air going up its middle at several, lifting the loose dust off the ground. On Earth's
    /// deserts they come from late morning to late afternoon, most at two or three o'clock, in light winds (strong wind blows
    /// the plumes apart), tens to a few hundred metres high and a few metres across, lasting from seconds to some minutes and
    /// moving with the wind. On Mars (Duna here) they are larger, the air thinner and the ground hotter against it: the rovers
    /// filmed tens a day in Gusev's summer, a few tens of metres across and some hundreds high, some kilometres from orbit,
    /// drifting at a few metres a second; their tracks criss-cross the ground.
    ///
    /// Here: in the afternoon (from about 10:30 to 16:30 by the sun, most at about 13:30), under a sun more than thirty degrees
    /// up, with little low cloud, no rain and a ten-metre wind under six to eight metres a second, over dry ground: all of Duna
    /// but its poles, and Kerbin's deserts and badlands. The ground is divided into squares (700 m on Duna, 900 m on Kerbin), and
    /// each square in each four minutes has a devil or not by a number worked out from where and when it is (the same every time,
    /// so a quickload brings the same ones back), more the hotter and stiller the afternoon; each lasts two to three and a half
    /// minutes, growing and dying away over half a minute, and goes with the wind with a little wander of its own. The two
    /// nearest within four kilometres of the camera are made in the explosions' engine as dust lifted by a whirl (Volumetric
    /// Explosions 0.8.0's thermal given warm: faster, narrower than a cloud's, the ground's own colour). Without that mod, or
    /// one older, none is drawn.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Devils : MonoBehaviour
    {
        internal struct Devil { public long key; public double lat, lon; public float radius, height, running, distance; public Vector2 move; }
        internal static readonly List<Devil> Now = new List<Devil>();
        internal static CelestialBody Body;
        public static string Report = "";
        static bool tried, there, hooked;
        const int Most = 2;
        const double Reach = 4000.0, Slot = 240.0;
        static float lookAt;
        static readonly List<Devil> chosen = new List<Devil>();

        void Update()
        {
            if (!tried) { tried = true; there = Installed(); }
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            bool want = Weather.On && (Weather.Dust == null || Weather.Dust) && (Weather.Devils == null || Weather.Devils) && there && body != null && cam != null && body.atmosphere && !MapView.MapIsEnabled;
            Body = body;
            // (where they are is looked for twice a second; where each is now, every frame, from that)
            if (!want) Now.Clear();
            else if (Time.realtimeSinceStartup > lookAt) { lookAt = Time.realtimeSinceStartup + 0.5f; Choose(body, cam.transform.position); }
            if (there)
            {
                try { Whirls.Hooked(Now.Count > 0, ref hooked); }
                catch (Exception e) { there = false; Weather.Log("the dust devils are off: " + e.Message); }
            }
            Report = !there ? "" : Now.Count == 0 ? "" : Now.Count + " dust devil" + (Now.Count == 1 ? "" : "s") + " near you";
        }

        void OnDestroy()
        {
            Now.Clear();
            if (there && hooked) { try { Whirls.Hooked(false, ref hooked); } catch (Exception) { } }
        }

        /// <summary>Whether the explosions mod is there and knows dust lifted by a whirl (its Exhaust Level 5, 0.8.0).</summary>
        static bool Installed()
        {
            // (the newest of the copies loaded: a development build has its own beside the one installed)
            int most = 0;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = a.GetType("VolumetricExplosions.Sources", false); } catch (Exception) { continue; }
                PropertyInfo level = t != null ? t.GetProperty("Level", BindingFlags.Public | BindingFlags.Static) : null;
                if (level != null) { try { most = Math.Max(most, (int)level.GetValue(null, null)); } catch (Exception) { } }
            }
            return most >= 5;
        }

        static uint Mixed(long i, long j, long k, uint salt)
        {
            uint h = 2166136261u ^ salt;
            unchecked
            {
                h = (h ^ (uint)(i & 0xFFFFFFFF)) * 16777619u; h = (h ^ (uint)(j & 0xFFFFFFFF)) * 16777619u; h = (h ^ (uint)(k & 0xFFFFFFFF)) * 16777619u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            }
            return h;
        }

        /// <summary>How strongly the ground there raises dust devils, 0 to 1, by what it is: Duna's dust (not its poles), Kerbin's deserts.</summary>
        static float Ground(CelestialBody body, double lat, double lon)
        {
            string biome = "";
            try { biome = (ScienceUtil.GetExperimentBiome(body, lat, lon) ?? "").ToLowerInvariant(); } catch (Exception) { }
            if (body.bodyName == "Duna") return biome.Contains("pol") || biome.Contains("ice") ? 0f : 1f;
            if (body.bodyName == "Kerbin") return biome.Contains("desert") ? 0.45f : biome.Contains("badlands") ? 0.3f : 0f;
            return 0f;
        }

        static void Choose(CelestialBody body, Vector3d camera)
        {
            Now.Clear();
            double lat = body.GetLatitude(camera), lon = body.GetLongitude(camera), alt = body.GetAltitude(camera);
            bool duna = body.bodyName == "Duna";
            if (!duna && body.bodyName != "Kerbin") return;
            if (!Model.Allowed(body)) return;
            // (seen from low down only: from high up they are specks, and the explosions' air is for what is near)
            double ground = body.TerrainAltitude(lat, lon, true);
            if (alt - ground > 9000.0) return;
            double ut = Planetarium.GetUniversalTime();
            World w = World.Of(body);
            Column col = Model.ColumnAt(w, lat, lon, ut);
            Base b = col.B;
            float hour = (float)b.Hour;                                          // degrees from noon, afternoon positive
            float afternoon = Mathf.Exp(-((hour - 22f) / 30f) * ((hour - 22f) / 30f)) * Mathf.Clamp01((hour + 22f) / 15f) * Mathf.Clamp01((68f - hour) / 10f);
            float sunny = Mathf.Clamp01((b.SunUp - 0.5f) / 0.25f);
            float wind = Mathf.Sqrt(col.UE * col.UE + col.UN * col.UN);
            float still = 1f - Mathf.SmoothStep(0f, 1f, (wind - (duna ? 4f : 3f)) / 4f);
            float clear = Mathf.Clamp01(1f - 2f * col.Here.Low.Cover) * (col.Here.Rain > 0.02f ? 0f : 1f) * (1f - Mathf.Clamp01(b.Dust * 2f)) * (1f - Mathf.Clamp01(b.Fog * 3f));
            // (the hotter the ground against the air, the more: the weather's own measure of sun-warmed air rising, and dry)
            float warm = Mathf.Clamp01(0.35f + 0.9f * b.Inst) * Mathf.Clamp01(1.3f - b.M);
            float chance = (duna ? 0.2f : 0.1f) * afternoon * sunny * still * clear * warm;
#if DEV
            if (testChance >= 0f) chance = testChance;
#endif
            if (chance < 0.002f) return;
            double square = duna ? 700.0 : 900.0;
            double perLat = square / body.Radius * (180.0 / Math.PI), perLon = perLat / Math.Max(0.2, Math.Cos(lat * Math.PI / 180.0));
            long ci = (long)Math.Floor(lat / perLat), cj = (long)Math.Floor(lon / perLon);
            long slot = (long)Math.Floor(ut / Slot);
            // (the ten-metre wind carries them, east and north, m/s)
            Vector2 drift = new Vector2(col.UE, col.UN) * 0.8f;
            chosen.Clear();
            for (long i = ci - 5; i <= ci + 5; i++)
                for (long j = cj - 5; j <= cj + 5; j++)
                    for (long s = slot - 1; s <= slot; s++)
                    {
                        uint h = Mixed(i, j, s, 1301u);
                        if ((h & 0xFFFF) / 65535f > chance) continue;
                        uint g = Mixed(i, j, s, 7919u);
                        // (when it begins within its slot and how long it lasts: two to three and a half minutes)
                        double born = s * Slot + Slot * 0.6 * ((g & 0xFF) / 255.0), lives = 120.0 + 90.0 * (((g >> 8) & 0xFF) / 255.0);
                        double age = ut - born;
                        if (age < 0.0 || age > lives) continue;
                        double cLat = (i + 0.15 + 0.7 * ((h >> 16) & 0xFF) / 255.0) * perLat, cLon = (j + 0.15 + 0.7 * ((h >> 24) & 0xFF) / 255.0) * perLon;
                        float strength = Ground(body, cLat, cLon);
                        if (strength <= 0f) continue;
                        // (where it has gone since: with the wind, and round a little loop of its own)
                        float loop = (float)(age / 70.0 * 6.2832) + ((g >> 16) & 0xFF) / 40f;
                        double east = drift.x * age + 25.0 * Math.Sin(loop), north = drift.y * age + 25.0 * Math.Cos(loop * 0.8);
                        double dLat = north / body.Radius * (180.0 / Math.PI), dLon = east / (body.Radius * Math.Max(0.2, Math.Cos(cLat * Math.PI / 180.0))) * (180.0 / Math.PI);
                        var d = new Devil { key = (i * 1000003L + j) * 31L + (s & 0xFFFF), lat = cLat + dLat, lon = cLon + dLon, move = drift };
                        double nE = (d.lon - lon) / perLon * square, nN = (d.lat - lat) / perLat * square;
                        d.distance = (float)Math.Sqrt(nE * nE + nN * nN);
                        if (d.distance > Reach) continue;
                        float size = ((g >> 24) & 0xFF) / 255f;
                        // (many small, few large: on Duna 6 to 22 m across its dust column's foot and 250 to 700 m high; on Kerbin's deserts 2.5 to 6 and 80 to 220)
                        d.radius = duna ? Mathf.Lerp(6f, 22f, size * size) : Mathf.Lerp(2.5f, 6f, size * size);
                        d.height = (duna ? Mathf.Lerp(250f, 700f, size) : Mathf.Lerp(80f, 220f, size)) * (0.7f + 0.3f * strength);
                        // (it grows over its first half minute and dies away over its last)
                        d.running = Mathf.Clamp01((float)age / 30f) * Mathf.Clamp01((float)(lives - age) / 30f) * (0.55f + 0.45f * strength);
                        chosen.Add(d);
                    }
            chosen.Sort((a, c) => a.distance.CompareTo(c.distance));
            for (int n = 0; n < chosen.Count && n < Most; n++) Now.Add(chosen[n]);
        }

#if DEV
        static float testChance = -1f;
        /// <summary>(Development build: every square's chance in a slot held at this, to see them anywhere and at any hour, "go" as the weather has it; and where those being made stand.)</summary>
        public static string Try(string v)
        {
            if (!string.IsNullOrEmpty(v)) testChance = v == "go" ? -1f : float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            lookAt = 0f;
            return (testChance >= 0f ? "chance held at " + testChance : "as the weather has it") + "; " + Where();
        }

        public static string Where()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = Body;
            if (v == null || body == null) return "no craft";
            if (Now.Count == 0) return "no dust devils" + (there ? "" : " (no Volumetric Explosions 0.8.0 or later)");
            Provider.Axes(body, v.CoMD, out Vector3d east, out Vector3d north, out Vector3d up);
            var say = new System.Text.StringBuilder();
            foreach (Devil d in Now)
            {
                Vector3d at = body.GetWorldSurfacePosition(d.lat, d.lon, body.TerrainAltitude(d.lat, d.lon, true)) - v.CoMD;
                say.Append("east ").Append(Vector3d.Dot(at, east).ToString("F0")).Append(" north ").Append(Vector3d.Dot(at, north).ToString("F0")).Append(" up ").Append(Vector3d.Dot(at, up).ToString("F0"))
                   .Append(", ").Append((2f * d.radius).ToString("F0")).Append(" m across, ").Append(d.height.ToString("F0")).Append(" m high, at ").Append((100f * d.running).ToString("F0")).Append("%; ");
            }
            return say.ToString().TrimEnd(' ', ';');
        }
#endif

        /// <summary>Everything that names the explosions mod: kept apart, in the bodies of methods only, so this mod loads without it.</summary>
        static class Whirls
        {
            public static void Hooked(bool want, ref bool hooked)
            {
                if (want == hooked) return;
                if (want) VolumetricExplosions.Sources.Asked += Feed; else VolumetricExplosions.Sources.Asked -= Feed;
                hooked = want;
            }

            static void Feed()
            {
                CelestialBody body = Body;
                if (body == null) return;
                foreach (Devil d in Now)
                {
                    double ground = body.TerrainAltitude(d.lat, d.lon, true);
                    if (body.ocean && ground < 0.0) continue;
                    Vector3d foot = body.GetWorldSurfacePosition(d.lat, d.lon, ground + 0.5);
                    Vector3d up = (foot - body.position).normalized;
                    Provider.Axes(body, foot, out Vector3d east, out Vector3d north, out _);
                    var e = new VolumetricExplosions.Exhaust
                    {
                        id = unchecked((int)(0x50000000L + (d.key & 0x0FFFFFFF))),
                        rim = foot, along = up, going = east * d.move.x + north * d.move.y,
                        nozzle = d.radius, flame = d.height, running = d.running, amount = 0.14f, shade = 0.62f, warm = 1f,
                        lasts = 18f, tint = 0, fire = 0f, rides = null, kind = VolumetricExplosions.ExhaustKind.Thermal, reach = 600f,
                    };
                    VolumetricExplosions.Sources.Burning(e);
                }
            }
        }
    }
}
