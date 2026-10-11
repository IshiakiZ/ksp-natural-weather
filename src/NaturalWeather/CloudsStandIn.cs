using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// A stand-in for the weather, for the clouds to be drawn from where no weather mod answers Keystone.Weather (so that they
    /// can be tried alone). Mostly fair weather: fields of heaped cloud over most of the world, clear patches, now and then a
    /// grey sheet where the air is wettest, patches of middle cloud, wisps of high cloud, and storms here and there. Worked out
    /// from the place and the clock alone (smooth noise over the sphere, drifting and changing over hours), so the same moment
    /// always has the same sky. Safe on any thread.
    /// </summary>
    static class CloudStandIn
    {
        /// <summary>(For tests.) A kind of weather held everywhere, or "own" for the stand-in's weather as it comes (in place of a
        /// weather mod's), or null for the weather as it comes from wherever it comes.</summary>
        public static string Held;

        public static bool HasClouds(CelestialBody body) => body != null && body.atmosphere && (body.bodyName == "Kerbin" || body.bodyName == "Laythe");

        static CloudLayer Layer(CloudKind kind, float cover, float bottom, float top, float water = 0f) =>
            new CloudLayer { Kind = kind, Cover = Mathf.Clamp01(cover), Base = bottom, Top = top, Water = water };

        public static WeatherHere At(CelestialBody body, double latitude, double longitude, double ut)
        {
            double la = latitude * Math.PI / 180.0, lo = longitude * Math.PI / 180.0;
            double x = Math.Cos(la) * Math.Cos(lo), y = Math.Cos(la) * Math.Sin(lo), z = Math.Sin(la);
            // (the large systems drift and change over a day or two; the fields of heaps within them over hours)
            double days = ut / 21600.0;
            float wet = 0.5f + 0.5f * Fbm(x * 2.1 + days * 0.11, y * 2.1 - days * 0.07, z * 2.1 + days * 0.05, 3);
            float local = Fbm(x * 9.0 + days * 0.6, y * 9.0, z * 9.0 - days * 0.4, 2);
            float local2 = Fbm(x * 17.0 - days * 0.9, y * 17.0 + days * 0.5, z * 17.0, 2);
            float high = Mathf.Clamp01(0.1f + 1.3f * Fbm(x * 3.3 - days * 0.3, y * 3.3, z * 3.3 + days * 0.2, 3));
            float middle = Mathf.Clamp01(1.6f * Fbm(x * 4.1 + days * 0.2, y * 4.1 + days * 0.3, z * 4.1, 3) - 0.25f);
            float vary = 0.75f + 0.5f * (0.5f + 0.5f * local);

            double lat = Math.Abs(latitude);
            var w = new WeatherHere { Seeing = 40000f, Drift = new Vector3d(lat < 30.0 ? -7.0 : lat < 60.0 ? 9.0 : -4.0, 1.5, 0.0) };
            w.Wind = w.Drift * 0.6;
            string held = Held;
            if (held != null && held != "own")
            {
                float b = 1000f + 150f * local2;
                switch (held)
                {
                    case "clear": return w;
                    case "cirrus": w.High = Layer(CloudKind.Cirrus, 0.9f, 7500f, 8500f); return w;
                    case "fair": w.Low = Layer(CloudKind.Cumulus, 0.32f * vary, b, b + 1300f + 500f * local); w.High = Layer(CloudKind.Cirrus, 0.3f, 7500f, 8500f); return w;
                    case "cumulus": w.Low = Layer(CloudKind.Cumulus, 0.5f * vary, b, b + 2200f + 900f * local); w.High = Layer(CloudKind.Cirrus, 0.2f, 7500f, 8500f); return w;
                    case "towering": w.Low = Layer(CloudKind.Cumulus, 0.45f * vary, 950f, 5500f + 1200f * local, 0.2f); w.High = Layer(CloudKind.Cirrus, 0.3f, 7500f, 8500f); return w;
                    case "storm": w.Low = Layer(CloudKind.Cumulonimbus, 0.5f * vary, 800f, 4500f, 0.6f); w.High = Layer(CloudKind.Cirrus, 0.5f, 7500f, 8500f); w.Storm = 1f; w.Rain = 0.5f; return w;
                    case "layer": w.Low = Layer(CloudKind.Stratocumulus, 0.85f * vary, 1100f, 2000f); return w;
                    case "middle": w.Low = Layer(CloudKind.Cumulus, 0.2f * vary, b, b + 1200f); w.Middle = Layer(CloudKind.Altocumulus, 0.65f * vary, 3600f, 4300f); return w;
                    case "overcast": w.Low = Layer(CloudKind.Stratus, 0.97f, 600f, 1700f, 0.4f); w.Middle = Layer(CloudKind.Altostratus, 0.7f, 3000f, 4200f, 0.3f); w.Rain = 0.2f; return w;
                }
            }

            // As it comes: fair weather (scattered heaps) most of the time; clear where the air is dry; a grey sheet where it is
            // wettest; heaps that tower where it is wet; patches of middle cloud; wisps of high cloud.
            w.High = Layer(CloudKind.Cirrus, high * 0.8f, 7500f, 8500f);
            float fair = Mathf.Clamp01(0.16f + 0.24f * (0.5f + 0.5f * local) + 0.2f * (wet - 0.5f)) * Mathf.Clamp01((wet - 0.25f) * 4f);
            float bottom = 950f + 250f * local2 + 300f * (0.6f - wet);
            float tall = 1200f + 1800f * Mathf.Clamp01(wet - 0.45f) * 2f + 400f * local;
            w.Low = Layer(CloudKind.Cumulus, fair, bottom, bottom + tall);
            float sheet = Mathf.Clamp01((wet - 0.8f) * 7f);
            if (sheet > 0f)
            {
                w.Low = sheet > 0.5f ? Layer(CloudKind.Stratocumulus, Mathf.Lerp(fair, 0.9f + 0.1f * local, sheet), Mathf.Lerp(bottom, 700f, sheet), Mathf.Lerp(bottom + tall, 1900f, sheet), sheet * 0.5f) : w.Low;
                w.Rain = sheet * 0.2f;
            }
            if (middle > 0.05f) w.Middle = Layer(CloudKind.Altocumulus, middle * 0.7f, 3400f + 300f * local2, 4200f + 300f * local2);
            w.Storm = StormField(x, y, z, days) * Mathf.Clamp01((wet - 0.55f) * 4f);
            return w;
        }

        static float StormField(double x, double y, double z, double days) =>
            Mathf.Clamp01((Fbm(x * 6.0 + days * 0.8, y * 6.0 - days * 0.8, z * 6.0, 2) - 0.2f) * 3f);

        /// <summary>
        /// Storms and showers near a place: on a lattice about 26 km apart (2.5 degrees on Kerbin), each node a storm where the
        /// storm field is strong enough there, with a life of an hour or so of game time (grows, lives, dies).
        /// </summary>
        public static int Cells(CelestialBody body, double latitude, double longitude, double radius, double ut, List<WeatherCell> into)
        {
            if (body == null) return 0;
            double degree = body.Radius * Math.PI / 180.0;
            const double step = 2.5;
            int found = 0;
            double reach = radius / degree + step;
            bool held = Held == "storm";
            if (!held && Held != null && Held != "own") return 0;
            for (double la = Math.Floor((latitude - reach) / step) * step; la <= latitude + reach; la += step)
            {
                if (Math.Abs(la) > 80.0) continue;
                double lonStep = step / Math.Max(0.2, Math.Cos(la * Math.PI / 180.0));
                for (double lo = Math.Floor((longitude - reach * lonStep / step) / lonStep) * lonStep; lo <= longitude + reach * lonStep / step; lo += lonStep)
                {
                    int i = (int)Math.Round(la / step) * 7919 + (int)Math.Round(lo / lonStep) * 104729;
                    uint h = Hash((uint)i);
                    // (each node's storm lives about an hour and a half, starting at its own time in a cycle of four hours)
                    double cycle = 4.0 * 3600.0;
                    double phase = ((ut / cycle) + (h & 1023) / 1024.0) % 1.0;
                    float life = (float)(phase * cycle / (1.5 * 3600.0));
                    if (life >= 1f) continue;
                    double cla = la + ((h >> 10) & 255) / 255.0 * step * 0.8 - step * 0.4, clo = lo + ((h >> 18) & 255) / 255.0 * lonStep * 0.8 - lonStep * 0.4;
                    double rla = cla * Math.PI / 180.0, rlo = clo * Math.PI / 180.0;
                    float field = held ? 1f : StormField(Math.Cos(rla) * Math.Cos(rlo), Math.Cos(rla) * Math.Sin(rlo), Math.Sin(rla), ut / 21600.0);
                    if (!held && field < 0.45f) continue;
                    if (held && (h >> 26) > 40) continue;        // (a held storm: about two nodes in three)
                    double dLat = (cla - latitude) * degree, dLon = (clo - longitude) * degree * Math.Cos(latitude * Math.PI / 180.0);
                    if (dLat * dLat + dLon * dLon > radius * radius) continue;
                    float strength = Mathf.Sin(Mathf.PI * life) * Mathf.Clamp01(field * 1.4f);
                    into.Add(new WeatherCell
                    {
                        Id = i, Latitude = cla, Longitude = clo, Radius = 3500f + 2500f * ((h >> 5) & 31) / 31f,
                        Base = 800f, Top = 6500f + 3000f * strength, Life = life, Strength = strength, Rain = strength * 0.8f, Thunder = strength > 0.4f,
                        Drift = new Vector3d(-7.0, 1.5, 0.0),
                    });
                    found++;
                }
            }
            return found;
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
            return x;
        }

        // ---------------------------------------------------------------- smooth noise (Perlin's), safe on any thread

        static readonly int[] perm = MakePerm();

        static int[] MakePerm()
        {
            var p = new int[512];
            var r = new System.Random(20261009);
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); int t = p[i]; p[i] = p[j]; p[j] = t; }
            for (int i = 0; i < 256; i++) p[256 + i] = p[i];
            return p;
        }

        static double Grad(int h, double x, double y, double z)
        {
            int k = h & 15;
            double u = k < 8 ? x : y, v = k < 4 ? y : (k == 12 || k == 14 ? x : z);
            return ((k & 1) == 0 ? u : -u) + ((k & 2) == 0 ? v : -v);
        }

        static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        static double Noise(double x, double y, double z)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            int X = (int)fx & 255, Y = (int)fy & 255, Z = (int)fz & 255;
            x -= fx; y -= fy; z -= fz;
            double u = Fade(x), v = Fade(y), w = Fade(z);
            int A = perm[X] + Y, AA = perm[A] + Z, AB = perm[A + 1] + Z, B = perm[X + 1] + Y, BA = perm[B] + Z, BB = perm[B + 1] + Z;
            double a = Grad(perm[AA], x, y, z), b = Grad(perm[BA], x - 1, y, z), c = Grad(perm[AB], x, y - 1, z), d = Grad(perm[BB], x - 1, y - 1, z);
            double e = Grad(perm[AA + 1], x, y, z - 1), f = Grad(perm[BA + 1], x - 1, y, z - 1), g = Grad(perm[AB + 1], x, y - 1, z - 1), h = Grad(perm[BB + 1], x - 1, y - 1, z - 1);
            double l1 = a + u * (b - a), l2 = c + u * (d - c), l3 = e + u * (f - e), l4 = g + u * (h - g);
            double m1 = l1 + v * (l2 - l1), m2 = l3 + v * (l4 - l3);
            return m1 + w * (m2 - m1);
        }

        /// <summary>Layered smooth noise, about -1 to 1.</summary>
        static float Fbm(double x, double y, double z, int octaves)
        {
            double sum = 0.0, amp = 1.0, all = 0.0;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(x + 31.7 * o, y - 17.3 * o, z + 9.1 * o) * amp;
                all += amp; amp *= 0.5; x *= 2.03; y *= 2.03; z *= 2.03;
            }
            return (float)(sum / all * 1.5);
        }
    }
}
