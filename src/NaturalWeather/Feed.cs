using System;
using System.Reflection;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The weather's wind for the explosions' air (Volumetric Explosions 0.5 or later), so that smoke, explosions and engines'
    /// trails drift and gust in the same wind the craft feels; and how damp the air is (0.8 or later), for the cloud a big
    /// blast leaves behind its shock in damp air. Hooked while the setting is on; without that mod, nothing.
    /// </summary>
    static class Feed
    {
        static bool hooked, tried, there, damp;
        public static string Report = "";

        public static void Start()
        {
            if (!tried) { tried = true; there = Installed(); damp = there && Has("Damp"); }
            if (!there) { Report = ""; return; }
            bool want = Weather.On && Weather.Smoke;
            if (want == hooked) return;
            try
            {
                if (want) Hook.Set(); else Hook.Clear();
                // (the dampness only where the explosions know of it: kept apart, so that an older one still takes the wind)
                if (damp) { if (want) Hook.SetDamp(); else Hook.ClearDamp(); }
                hooked = want;
                Report = want ? "Smoke drifts in the weather's wind." : "";
            }
            catch (Exception ex) { there = false; Report = "Volumetric Explosions is too old to take the weather's wind (it needs 0.5)."; Weather.Log(Report + " " + ex.GetType().Name); }
        }

        public static void Stop()
        {
            if (!hooked) return;
            try { Hook.Clear(); } catch (Exception) { }
            if (damp) try { Hook.ClearDamp(); } catch (Exception) { }
            hooked = false;
        }

        static bool Installed() => Has("Wind");

        static bool Has(string field)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("VolumetricExplosions.Sources", false);
                if (t != null && t.GetField(field, BindingFlags.Public | BindingFlags.Static) != null) return true;
            }
            return false;
        }
    }

    /// <summary>Everything that names the explosions mod: kept apart, in the bodies of methods only, so this mod loads without it.</summary>
    static class Hook
    {
        public static void Set() { VolumetricExplosions.Sources.Wind = Wind; }

        public static void Clear()
        {
            var mine = (Func<Vector3d, double, VolumetricExplosions.Sources.Breeze>)Wind;
            if (VolumetricExplosions.Sources.Wind != null && VolumetricExplosions.Sources.Wind.Method == mine.Method) VolumetricExplosions.Sources.Wind = null;
        }

        public static void SetDamp() { VolumetricExplosions.Sources.Damp = Damp; }

        public static void ClearDamp()
        {
            var mine = (Func<Vector3d, double, float>)Damp;
            if (VolumetricExplosions.Sources.Damp != null && VolumetricExplosions.Sources.Damp.Method == mine.Method) VolumetricExplosions.Sources.Damp = null;
        }

        /// <summary>
        /// How damp the air is at a place: its relative humidity, 0 to 1. In fog, or inside a cloud, all the water it can hold.
        /// Under the low cloud's base, as damp as the base says: air stirred up from the ground cools by 9.8 degrees a kilometre
        /// as it rises and its dew point by 1.8, so the two meet at the base (which is why the cloud's base is where it is), and
        /// below it the air is 8 degrees above its dew point for every kilometre down. Steady rain wets the air under it besides.
        /// Elsewhere from the weather's moisture (dry air 0.4, a wet system 0.85), damper over the sea and by night.
        /// </summary>
        static float Damp(Vector3d world, double ut)
        {
            CelestialBody body = FlightGlobals.getMainBody(world);
            if (body == null || !body.atmosphere || !Model.Allowed(body)) return 0f;
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world), alt = body.GetAltitude(world);
            Column col = Model.ColumnAt(World.Of(body), lat, lon, ut);
            WeatherHere here = col.Here;
            double ground = body.TerrainAltitude(lat, lon, true);
            if (body.ocean && ground < 0.0) ground = 0.0;
            if (here.Fog > 0.25f && alt - ground < here.FogDeep) return 1f;
            float rh = 0.4f + 0.45f * col.B.M + 0.08f * (1f - col.B.Land) + (col.B.SunUp < 0f ? 0.07f : 0f);
            rh = Within(here.Low, alt, rh, true);
            rh = Within(here.Middle, alt, rh, false);
            rh = Within(here.High, alt, rh, false);
            if (here.Rain > 0.05f && (here.Low.Cover < 0.05f || alt < here.Low.Base)) rh = Math.Max(rh, 0.9f + 0.08f * Mathf.Clamp01(here.Rain * 2f));
            return Mathf.Clamp01(rh);
        }

        static float Within(CloudLayer c, double alt, float rh, bool low)
        {
            if (c.Cover < 0.05f || c.Top <= c.Base) return rh;
            if (alt >= c.Base && alt <= c.Top) return Math.Max(rh, 0.9f + 0.1f * Mathf.Clamp01(c.Cover * 1.5f));
            if (!low || alt >= c.Base || c.Base - alt > 3000.0) return rh;
            // (the gap below the base, kelvin, and the relative humidity it makes about 15 degrees: Magnus's formula)
            float gap = (float)(c.Base - alt) * 0.008f;
            float under = Mathf.Exp(-gap / 15.5f);
            return Math.Max(rh, Mathf.Lerp(rh, under, Mathf.Clamp01(c.Cover * 2f)));
        }

        /// <summary>
        /// The wind for a patch of the explosions' air: the mean wind at the patch's own height above the ground, as it grows with
        /// height near the ground (Model.Mean: next to nothing just over the ground, about four fifths of the 10 m wind at 2 m on a
        /// sunny afternoon, under two thirds on a still night) and above, and the gusts' size there. So the explosions must not take
        /// a profile of their own off it: it is the wind where the smoke is. (Before 0.2.0 it was the 10 m wind, whatever the height.)
        /// </summary>
        static VolumetricExplosions.Sources.Breeze Wind(Vector3d world, double ut)
        {
            var breeze = new VolumetricExplosions.Sources.Breeze();
            CelestialBody body = FlightGlobals.getMainBody(world);
            if (body == null || !body.atmosphere || !Model.Allowed(body)) return breeze;
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world), alt = body.GetAltitude(world);
            World w = World.Of(body);
            Column col = Model.ColumnAt(w, lat, lon, ut);
            // (how high above the ground itself: the weather's own map of the ground is coarse, the pad stands 70 m up on it)
            double ground = body.TerrainAltitude(lat, lon, true);
            if (body.ocean && ground < 0.0) ground = 0.0;
            float agl = (float)Math.Max(0.5, alt - ground);
            Vector3 m = Model.Mean(col, alt, agl);
            Provider.Axes(body, world, out Vector3d east, out Vector3d north, out _);
            breeze.wind = east * m.x + north * m.y;
            breeze.gusts = Model.Sigmas(col, alt, agl, out _, out _, out _);
            return breeze;
        }
    }
}
