using System;
using System.Collections.Generic;
using System.Reflection;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The nearest heaps made as smoke is made (the rework of 2026-10-11). Of the clouds drawn from noise the user said "comical,
    /// like anime clouds", and "I don't think they look realistic because of how they're made. Our volumetric explosions: the smoke
    /// looks almost real." So where the weather has fair-weather heaps, the few nearest the camera are built of thermals in the
    /// explosions' engine (Volumetric Explosions 0.6.3, ExhaustKind.Thermal): puffs that rise from the cloud's base, widen as they
    /// mix, stall at its top and are gone in a couple of minutes, lit through a grid as the smoke is. The noise goes on drawing
    /// everything further off, and is cleared from where these stand (Clouds hands their places to its shader).
    ///
    /// Where: the ground is divided into squares 1.1 km across; a square has a heap or not by a number worked out from where it
    /// is (the same every time), more of them the more cover the weather has; the nearest three within 2.4 km are made. Each is a
    /// main thermal and three lesser round it, each waxing and waning in its own time, so that its turrets rise in turn.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Heaps : MonoBehaviour
    {
        internal struct Heap { public long key; public double lat, lon; public float baseAt, deep, wide, phase, distance; }
        /// <summary>The heaps being made this frame (Clouds clears its own from where they stand).</summary>
        internal static readonly List<Heap> Now = new List<Heap>();
        internal static CelestialBody Body;
        public static string Report = "";
        const double Square = 1100.0, Reach = 2400.0;
        const int Most = 3;
        static bool tried, there, hooked;
        static readonly HashSet<long> kept = new HashSet<long>();

        void Update()
        {
            Now.Clear();
            if (!tried) { tried = true; there = Installed(); }
            bool want = Weather.On && (CloudsPage.On == null || CloudsPage.On) && CloudsPage.Heaps != null && CloudsPage.Heaps;
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            Body = body;
            if (want && there && body != null && cam != null && body.atmosphere && !MapView.MapIsEnabled) Choose(body, cam.transform.position);
            if (there)
            {
                try { Thermals.Hooked(Now.Count > 0, ref hooked); }
                catch (Exception e) { there = false; Weather.Log("the heaps made as smoke are off: " + e.Message); }
            }
            Report = !there ? "Heaps as smoke: needs Volumetric Explosions 0.6.3 or later." : Now.Count == 0 ? "" : Now.Count + " heap" + (Now.Count == 1 ? "" : "s") + " near you made as smoke is made";
        }

        void OnDestroy()
        {
            Now.Clear();
            if (there && hooked) { try { Thermals.Hooked(false, ref hooked); } catch (Exception) { } }
        }

        static bool Installed()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("VolumetricExplosions.ExhaustKind", false);
                if (t != null && t.GetField("Thermal", BindingFlags.Public | BindingFlags.Static) != null) return true;
            }
            return false;
        }

        static uint Mixed(long i, long j, uint salt)
        {
            uint h = 2166136261u ^ salt;
            unchecked
            {
                h = (h ^ (uint)(i & 0xFFFFFFFF)) * 16777619u; h = (h ^ (uint)(j & 0xFFFFFFFF)) * 16777619u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            }
            return h;
        }

        static void Choose(CelestialBody body, Vector3d camera)
        {
            double lat = body.GetLatitude(camera), lon = body.GetLongitude(camera), alt = body.GetAltitude(camera);
            // (the weather the clouds are drawn from, so that these stand where it has heaps)
            WeatherHere here = CloudSource.At(body, lat, lon, Planetarium.GetUniversalTime());
            CloudLayer low = here.Low;
            // (fair-weather heaps only: a sheet has no turrets, and a storm is its own thing; and not from far above them)
            if (low.Kind != CloudKind.Cumulus || low.Cover < 0.04f || low.Top - low.Base < 250f || alt > low.Top + 5000.0) return;
            double perLat = Square / body.Radius * (180.0 / Math.PI), perLon = perLat / Math.Max(0.2, Math.Cos(lat * Math.PI / 180.0));
            long ci = (long)Math.Floor(lat / perLat), cj = (long)Math.Floor(lon / perLon);
            float chance = Mathf.Clamp(low.Cover * 1.6f, 0.08f, 0.85f);
            for (long i = ci - 3; i <= ci + 3; i++)
                for (long j = cj - 3; j <= cj + 3; j++)
                {
                    uint h = Mixed(i, j, 77u);
                    if ((h & 0xFFFF) / 65535f > chance) continue;
                    var heap = new Heap { key = i * 1000003L + j };
                    heap.lat = (i + 0.2 + 0.6 * ((h >> 16) & 0xFF) / 255.0) * perLat;
                    heap.lon = (j + 0.2 + 0.6 * ((h >> 24) & 0xFF) / 255.0) * perLon;
                    double north = (heap.lat - lat) / perLat * Square, east = (heap.lon - lon) / perLon * Square;
                    heap.distance = (float)Math.Sqrt(north * north + east * east);
                    // (one being made is kept until it is well out of reach, so that the set does not change with every move of the camera)
                    bool keeping = kept.Contains(heap.key);
                    if (heap.distance > (keeping ? Reach * 1.5 : Reach)) continue;
                    if (keeping) heap.distance -= 100000f;
                    uint g = Mixed(i, j, 911u);
                    float size = (g & 0xFF) / 255f;                                        // many small, few large
                    heap.wide = Mathf.Lerp(80f, 230f, size * size);
                    heap.deep = Mathf.Min(low.Top - low.Base, heap.wide * Mathf.Lerp(1.3f, 2.6f, ((g >> 8) & 0xFF) / 255f));
                    heap.baseAt = low.Base;
                    heap.phase = ((g >> 16) & 0xFFFF) / 65535f;
                    Now.Add(heap);
                }
            Now.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (Now.Count > Most) Now.RemoveRange(Most, Now.Count - Most);
            kept.Clear();
            for (int n = 0; n < Now.Count; n++)
            {
                Heap h = Now[n];
                if (h.distance < -50000f) { h.distance += 100000f; Now[n] = h; }
                kept.Add(h.key);
            }
        }

#if DEV
        /// <summary>(Development build: where the heaps being made stand from the craft, for pointing a camera at them.)</summary>
        public static string Where()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = Body;
            if (v == null || body == null) return "no craft";
            if (Now.Count == 0) return "no heaps" + (there ? "" : " (no Volumetric Explosions with thermals)");
            Provider.Axes(body, v.CoMD, out Vector3d east, out Vector3d north, out Vector3d up);
            var b = new System.Text.StringBuilder();
            foreach (Heap heap in Now)
            {
                Vector3d d = body.GetWorldSurfacePosition(heap.lat, heap.lon, heap.baseAt) - v.CoMD;
                b.Append("east ").Append(Vector3d.Dot(d, east).ToString("F0")).Append(" north ").Append(Vector3d.Dot(d, north).ToString("F0")).Append(" up ").Append(Vector3d.Dot(d, up).ToString("F0"))
                 .Append(" wide ").Append(heap.wide.ToString("F0")).Append(" deep ").Append(heap.deep.ToString("F0")).Append("\n");
            }
            return b.ToString().TrimEnd();
        }
#endif

        /// <summary>Everything that names the explosions mod: kept apart, in the bodies of methods only, so this mod loads without it.</summary>
        static class Thermals
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
                double ut = Planetarium.GetUniversalTime();
                foreach (Heap heap in Now)
                {
                    Vector3d middle = body.GetWorldSurfacePosition(heap.lat, heap.lon, heap.baseAt);
                    Vector3d up = (middle - body.position).normalized;
                    Vector3d east = Vector3d.Cross(up, body.transform.up).normalized, north = Vector3d.Cross(east, up);
                    for (int k = 0; k < 4; k++)
                    {
                        // (one main thermal and three lesser round it, each waxing and waning in its own time: turrets that rise in turn)
                        double turn = heap.phase * 6.2832 + k * 2.0944, out_ = k == 0 ? 0.0 : 0.95 * heap.wide;
                        float size = k == 0 ? 1f : 0.6f;
                        float beat = 0.5f + 0.5f * Mathf.Sin((float)(ut / (k == 0 ? 260.0 : 170.0 + 25.0 * k) * 6.2832 + heap.phase * 40.0 + k * 1.7));
                        float running = (k == 0 ? 0.45f : 0.2f) + (k == 0 ? 0.4f : 0.45f) * beat;
                        var e = new VolumetricExplosions.Exhaust
                        {
                            id = unchecked((int)(0x40000000L + ((heap.key & 0xFFFFFF) << 2) + k)),
                            rim = middle + east * (out_ * Math.Cos(turn)) + north * (out_ * Math.Sin(turn)), along = up, going = Vector3d.zero,
                            nozzle = heap.wide * size, flame = heap.deep * (k == 0 ? 1f : 0.62f), running = running, amount = 1f, shade = 1f, warm = 0f,
                            lasts = 110f, tint = 0, fire = 0f, rides = null, kind = VolumetricExplosions.ExhaustKind.Thermal, reach = 2.3f * heap.wide,
                        };
                        VolumetricExplosions.Sources.Burning(e);
                    }
                }
            }
        }
    }
}
