using System;
using System.Reflection;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The weather's wind for the explosions' air (Volumetric Explosions 0.5 or later), so that smoke, explosions and engines'
    /// trails drift and gust in the same wind the craft feels. Hooked while the setting is on; without that mod, nothing.
    /// </summary>
    static class Feed
    {
        static bool hooked, tried, there;
        public static string Report = "";

        public static void Start()
        {
            if (!tried) { tried = true; there = Installed(); }
            if (!there) { Report = ""; return; }
            bool want = Weather.On && Weather.Smoke;
            if (want == hooked) return;
            try
            {
                if (want) Hook.Set(); else Hook.Clear();
                hooked = want;
                Report = want ? "Smoke drifts in the weather's wind." : "";
            }
            catch (Exception ex) { there = false; Report = "Volumetric Explosions is too old to take the weather's wind (it needs 0.5)."; Weather.Log(Report + " " + ex.GetType().Name); }
        }

        public static void Stop()
        {
            if (!hooked) return;
            try { Hook.Clear(); } catch (Exception) { }
            hooked = false;
        }

        static bool Installed()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("VolumetricExplosions.Sources", false);
                if (t != null && t.GetField("Wind", BindingFlags.Public | BindingFlags.Static) != null) return true;
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
