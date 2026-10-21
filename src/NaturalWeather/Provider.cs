using System;
using System.Collections.Generic;
using System.Threading;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>The weather, as Keystone hands it to every mod that asks (see Keystone.Weather).</summary>
    sealed class Provider : IWeather
    {
        public static int MainThread = -1;
        internal static bool OnMain => Thread.CurrentThread.ManagedThreadId == MainThread;

        public string Name => "Natural Weather";

        static bool Allowed(CelestialBody body) => body != null && body.atmosphere && Model.Allowed(body);

        public WeatherHere At(CelestialBody body, double latitude, double longitude, double ut)
        {
            if (!Allowed(body)) return Keystone.Weather.Clear(body);
            return Model.Here(World.Of(body), latitude, longitude, ut);
        }

        /// <summary>
        /// The clouds and what falls at very many places at one moment (no wind), where this is the weather mod that answers
        /// Keystone: the model's atlas of the world, or clear skies where the weather is switched off. Null where another
        /// weather mod answers. For the clouds' map; what is handed back is for one thread.
        /// </summary>
        public static Func<double, double, WeatherHere> Many(CelestialBody body, double ut)
        {
            if (!(Keystone.Weather.Provider is Provider)) return null;
            if (!Allowed(body)) { WeatherHere clear = Keystone.Weather.Clear(body); return (latitude, longitude) => clear; }
            var atlas = new Model.Atlas(World.Of(body), ut);
            return atlas.At;
        }

        /// <summary>Whether the model has its map of this world's land and seas (see World.Keep); true where it needs none.</summary>
        public static bool Knows(CelestialBody body) => body == null || !body.atmosphere || World.Of(body).Mapped;

        public Vector3d Wind(CelestialBody body, Vector3d world, double ut)
        {
            if (!Allowed(body) || !OnMain) return Vector3d.zero;
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world), alt = body.GetAltitude(world);
            if (alt > body.atmosphereDepth) return Vector3d.zero;
            float ground = 0f;
            try { if (body.pqsController != null) ground = (float)(body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(lat, lon)) - body.Radius); }
            catch (Exception) { }
            if (body.ocean && ground < 0f) ground = 0f;
            Vector3 enu = Local(World.Of(body), lat, lon, alt, (float)(alt - ground), ut, true);
            Axes(body, world, out Vector3d east, out Vector3d north, out Vector3d up);
            return east * enu.x + north * enu.y + up * enu.z;
        }

        /// <summary>The wind at a place and height, east, north, up, m/s: the mean, and the gusts if asked for.</summary>
        public static Vector3 Local(World w, double lat, double lon, double alt, float aboveGround, double ut, bool gusts)
        {
            Column col = Model.ColumnAt(w, lat, lon, ut);
            Vector3 v = Model.Mean(col, alt, aboveGround);
            // (what is far smaller than the half kilometre the wind above is kept by: a storm's rising and sinking air, a
            // downburst, a tornado, worked out for this very place)
            v += Violent.Small(w, col, lat, lon, alt, aboveGround);
            if (gusts) v += Gusts.At(col, lat, lon, alt, aboveGround, ut);
            return v;
        }

        /// <summary>East, north and up at a point in world space (east is up × north: Unity's axes are left-handed).</summary>
        public static void Axes(CelestialBody body, Vector3d world, out Vector3d east, out Vector3d north, out Vector3d up)
        {
            up = (world - body.position).normalized;
            Vector3d pole = (Vector3d)body.transform.up;
            north = (pole - up * Vector3d.Dot(pole, up));
            north = north.sqrMagnitude > 1e-12 ? north.normalized : Vector3d.Cross(up, (Vector3d)Vector3.right).normalized;
            east = Vector3d.Cross(up, north);
        }

        public float Cloud(CelestialBody body, double latitude, double longitude, double altitude, double ut)
        {
            if (!Allowed(body)) return 0f;
            return CloudAt(World.Of(body), latitude, longitude, altitude, ut);
        }

        public int Cells(CelestialBody body, double latitude, double longitude, double radius, double ut, List<WeatherCell> into)
        {
            if (!Allowed(body)) return 0;
            return Model.Cells(World.Of(body), latitude, longitude, radius, ut, into);
        }

        public WeatherShade Shade(CelestialBody body, Vector3d world, Vector3d toSun, double ut)
        {
            if (!Allowed(body) || !OnMain || Weather.Light == null || !Weather.Light) return WeatherShade.Clear;
            // (the same answer for a quarter of a second: it is asked every frame, and the clouds do not change that fast)
            if (Time.realtimeSinceStartup - shadeAt < 0.25f && body == shadeBody && (world - shadeWorld).sqrMagnitude < 400.0) return Last;
            shadeAt = Time.realtimeSinceStartup; shadeBody = body; shadeWorld = world;
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world), alt = body.GetAltitude(world);
            Axes(body, world, out Vector3d east, out Vector3d north, out Vector3d up);
            return ShadeAt(World.Of(body), lat, lon, alt, (float)Vector3d.Dot(toSun, east), (float)Vector3d.Dot(toSun, north), (float)Vector3d.Dot(toSun, up), ut);
        }

        // ---------------------------------------------------------------- the pattern of the clouds

        /// <summary>How much light a layer of each kind takes out, its whole depth through (its "optical depth").</summary>
        public static float Tau(CloudLayer l)
        {
            switch (l.Kind)
            {
                case CloudKind.Cumulus: return 12f + 25f * l.Water;
                case CloudKind.Cumulonimbus: return 120f;
                case CloudKind.Stratocumulus: return 8f + 10f * l.Water;
                case CloudKind.Stratus: return 12f + 15f * l.Water;
                case CloudKind.Nimbostratus: return 40f + 40f * l.Water;
                case CloudKind.Altocumulus: return 4f;
                case CloudKind.Altostratus: return 6f + 14f * l.Water;
                case CloudKind.Cirrus: return 1.2f;
                case CloudKind.Fog: return 8f + 10f * l.Water;
                case CloudKind.Dust: return 6f;
                default: return 0f;
            }
        }

        /// <summary>How much cloud there is at a point: the layer it is in, and the pattern that layer has there.</summary>
        public static float CloudAt(World w, double lat, double lon, double alt, double ut)
        {
            WeatherHere here = Model.Layers(w, lat, lon, ut);
            float best = 0f;
            best = Math.Max(best, InLayer(w, here.Low, lat, lon, alt, ut, here.Drift, 1u));
            best = Math.Max(best, InLayer(w, here.Middle, lat, lon, alt, ut, here.Drift * 1.6, 2u));
            best = Math.Max(best, InLayer(w, here.High, lat, lon, alt, ut, here.Drift * 2.4, 3u));
            // storms: the towers, and the anvils spread at their tops
            if (alt > 0.0)
            {
                foreach (WeatherCell cell in Model.RegionCells(w, lat, lon, ut))
                {
                    if (alt < cell.Base || alt > cell.Top || cell.Strength < 0.2f) continue;
                    float frac = (float)((alt - cell.Base) / Math.Max(1.0, cell.Top - cell.Base));
                    float r = cell.Radius * (0.5f + 0.25f * (1f - frac)) * (1f + 1.4f * Noise.Smooth(0.8f, 0.97f, frac));
                    float d = (float)Model.Distance(w, lat, lon, cell.Latitude, cell.Longitude);
                    best = Math.Max(best, Noise.Smooth(r, r * 0.8f, d) * Noise.Smooth(0.15f, 0.5f, cell.Strength));
                }
            }
            return best;
        }

        static float InLayer(World w, CloudLayer l, double lat, double lon, double alt, double ut, Vector3d drift, uint which)
        {
            if (l.Kind == CloudKind.None || l.Cover < 0.01f || alt < l.Base || alt > l.Top) return 0f;
            float frac = (float)((alt - l.Base) / Math.Max(1.0, l.Top - l.Base));
            switch (l.Kind)
            {
                case CloudKind.Cumulus:
                case CloudKind.Cumulonimbus:
                    return Heads(w, lat, lon, ut, l.Cover, frac, drift, which);
                case CloudKind.Cirrus:
                    return Sheet(w, lat, lon, ut, l.Cover, 25000.0, drift, which, 4.0) * Noise.Bump(frac, 0.5f, 0.6f);
                case CloudKind.Fog:
                case CloudKind.Dust:
                    return l.Cover * (1f - Noise.Smooth(0.6f, 1f, frac));
                case CloudKind.Stratocumulus:
                case CloudKind.Altocumulus:
                    return Sheet(w, lat, lon, ut, l.Cover, 5000.0, drift, which, 1.0) * Noise.Bump(frac, 0.5f, 0.62f);
                default:
                    return Sheet(w, lat, lon, ut, l.Cover, 9000.0, drift, which, 1.0) * Noise.Bump(frac, 0.5f, 0.7f);
            }
        }

        const double Deg = Math.PI / 180.0;

        /// <summary>Heads of cumulus: a scatter of round clouds about two kilometres apart, as many and as broad as the cover says, domed on top, that grow and fade over a quarter of an hour.</summary>
        static float Heads(World w, double lat, double lon, double ut, float cover, float frac, Vector3d drift, uint which)
        {
            const double spacing = 2200.0, period = 900.0;
            double k = ut / period;
            int k0 = Noise.Floor(k);
            float f = (float)(k - k0), mix = f * f * (3f - 2f * f);
            float sum = 0f;
            float dome = frac < 0.45f ? 1f : Noise.Sqrt(Math.Max(0f, 1f - (frac - 0.45f) * (frac - 0.45f) / (0.55f * 0.55f)));
            for (int key = 0; key < 2; key++)
            {
                float weight = key == 0 ? 1f - mix : mix;
                if (weight < 1e-4f) continue;
                double since = ut - (k0 + key) * period;
                double cosLat = Math.Max(0.2, Math.Cos(lat * Deg));
                double la = (lat - drift.y * since / w.Radius / Deg) * Deg, lo = (lon - drift.x * since / (w.Radius * cosLat) / Deg) * Deg;
                double r = w.Radius / spacing, cl = Math.Cos(la);
                float d = Noise.Cells(cl * Math.Cos(lo) * r, cl * Math.Sin(lo) * r, Math.Sin(la) * r, w.Seed ^ which ^ (uint)(k0 + key) * 0x9e3779b1U, out uint id, out _);
                if (Noise.Unit(Noise.Hash(id)) > Math.Min(1f, cover * 1.35f + 0.05f)) continue;
                float radius = (0.22f + 0.32f * cover) * (0.7f + 0.6f * Noise.Unit(Noise.Hash(id ^ 0x5u))) * dome;
                sum += weight * Noise.Smooth(radius + 0.07f, radius - 0.07f, d);
            }
            return Noise.Clamp01(sum);
        }

        /// <summary>A sheet of cloud with gaps in it: as much of it as the cover says, the gaps a few kilometres across (`stretch` draws it out along the wind, for cirrus).</summary>
        static float Sheet(World w, double lat, double lon, double ut, float cover, double size, Vector3d drift, uint which, double stretch)
        {
            const double period = 3600.0;
            double k = ut / period;
            int k0 = Noise.Floor(k);
            float f = (float)(k - k0), mix = f * f * (3f - 2f * f);
            float sum = 0f;
            for (int key = 0; key < 2; key++)
            {
                float weight = key == 0 ? 1f - mix : mix;
                if (weight < 1e-4f) continue;
                double since = ut - (k0 + key) * period;
                double cosLat = Math.Max(0.2, Math.Cos(lat * Deg));
                double la = (lat - drift.y * since / w.Radius / Deg) * Deg, lo = (lon - drift.x * since / (w.Radius * cosLat) / Deg) * Deg;
                double r = w.Radius / size, cl = Math.Cos(la);
                double x = cl * Math.Cos(lo) * r, y = cl * Math.Sin(lo) * r, z = Math.Sin(la) * r;
                if (stretch > 1.0) { x /= stretch; }
                float n = Noise.Fbm(x, y, z, 3, w.Seed ^ (which * 0x68e31da4U) ^ (uint)(k0 + key) * 0x1b56c4e9U);
                sum += weight * n;
            }
            float v = 0.5f + 0.75f * sum;
            return Noise.Smooth(1f - cover - 0.12f, 1f - cover + 0.12f, v);
        }

        // ---------------------------------------------------------------- the light

        // (whether this mod's own clouds are drawn just now: they dim the sun themselves while they are, by Clouds.SunThrough)
        static bool CloudsDrawing() => Clouds.Drawing;

        public static WeatherShade Last = WeatherShade.Clear;
        static float shadeAt = -1f;
        static CelestialBody shadeBody;
        static Vector3d shadeWorld;

        /// <summary>How the clouds dim the sun and change the sky at a place, for a sun in the given direction (east, north, up parts).</summary>
        public static WeatherShade ShadeAt(World w, double lat, double lon, double alt, float sunE, float sunN, float sunUp, double ut)
        {
            WeatherHere here = Model.Here(w, lat, lon, ut);
            float tauDir = 0f, tauSky = 0f, coverAbove = 0f, greyFrom = 0f;
            CloudLayer[] layers = { here.Low, here.Middle, here.High };
            float notCovered = 1f;
            foreach (CloudLayer l in layers)
            {
                if (l.Kind == CloudKind.None || l.Cover < 0.01f || alt > l.Top) continue;
                float tau = Tau(l);
                float inside = alt > l.Base ? (float)((l.Top - alt) / Math.Max(1.0, l.Top - l.Base)) : 1f;
                tauSky += l.Cover * tau * inside;
                // (heaped cloud leaves blue sky between its heads: it greys the light much less than a sheet of the same cover)
                greyFrom += l.Cover * inside * (l.Kind == CloudKind.Cumulus || l.Kind == CloudKind.Cirrus || l.Kind == CloudKind.Altocumulus ? 0.3f : 1f) * Math.Min(1f, 0.4f + 0.06f * tau);
                notCovered *= 1f - l.Cover * Noise.Clamp01(inside * 2f);
                // the line to the sun: where it passes through the layer's middle, and how much cloud there is just there
                double mid = Math.Max(alt, (l.Base + l.Top) * 0.5);
                float pattern;
                if (sunUp > 0.02f)
                {
                    float level = Noise.Sqrt(sunE * sunE + sunN * sunN);
                    double run = Math.Min(40000.0, (mid - alt) * level / sunUp);
                    double dn = level > 1e-4f ? run * sunN / level : 0.0, de = level > 1e-4f ? run * sunE / level : 0.0;
                    double la = lat + dn / w.Radius / Deg, lo = lon + de / (w.Radius * Math.Max(0.05, Math.Cos(lat * Deg))) / Deg;
                    pattern = CloudAt(w, la, lo, mid, ut);
                    // (the rest of the layer's depth along the slanting line: thicker the lower the sun)
                    pattern *= Math.Min(3f, 1f / Math.Max(0.15f, sunUp));
                }
                else pattern = l.Cover;
                tauDir += pattern * tau * inside;
            }
            coverAbove = 1f - notCovered;
            var shade = new WeatherShade();
            shade.Sun = (float)Math.Exp(-0.75f * tauDir);
            // (where our own clouds are drawn they dim the sun themselves, by the cloud really in the way: not twice)
            // (where our own clouds are drawn they dim the sun themselves, by the cloud really in the way above the craft: then that is
            // what gets through, and the light is not to be dimmed twice)
            bool drawn = CloudsDrawing();
            if (drawn) { shade.Sun = Noise.Clamp01(Clouds.SunThrough); shade.SunDimmed = true; }
            // (the share of the light a layer lets through, all of it as light from the whole sky: two-stream, 1 / (1 + 0.75 tau (1 - g))
            // with cloud's g = 0.8. An overcast of stratus lets through a quarter of a clear day's light, a raincloud a tenth, a
            // thunderstorm's base a twentieth: the measured ranges are a tenth to a quarter, a twentieth to a tenth, and less)
            float through = 1f / (1f + 0.15f * tauSky);
            float spread = 1f + 3f * Math.Max(0f, sunUp);
            shade.Sky = (1f - coverAbove) + coverAbove * through * spread;
            shade.Grey = Noise.Clamp01(greyFrom);
            // dust and fog in the air itself, here
            if (here.Dust > 0.01f) { shade.Sun *= (float)Math.Exp(-3f * here.Dust); shade.Sky *= 1f - 0.3f * here.Dust; }
            if (here.Fog > 0.01f && alt < here.Low.Top) { shade.Sun *= (float)Math.Exp(-2.5f * here.Fog); shade.Grey = Math.Max(shade.Grey, here.Fog); }
            Last = shade;
            return shade;
        }
    }
}
