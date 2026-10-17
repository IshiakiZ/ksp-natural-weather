using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// A craft's wake in cloud: where the craft being flown has gone through the layers the clouds are in, kept for some
    /// minutes and handed to the clouds' shader, which draws the cloud there dragged along the path, rolled up round it
    /// and stirred, with a hole where an engine burnt it off (see the shader: wakeStep). Asked for by the user: "Clouds
    /// swirl when you go through it, like the volumetric explosions" (2026-10-12).
    ///
    /// The places are kept in the cloud's own frame: the planet's axes less how far the wind has carried the clouds, so that
    /// a wake goes with the cloud it is in and not with the ground.
    /// </summary>
    static class CloudsWake
    {
        struct Place { public Vector3d At; public double When; }

        /// <summary>The newest first.</summary>
        static readonly List<Place> places = new List<Place>();
        static string world = "";
        static Guid craft;
        static float wide = 20f, hole = 0.4f;
        /// <summary>How many places are kept (the shader takes eight), how far apart they are set (m), and for how long (s).</summary>
        const int Most = 8;
        const double Apart = 240.0, Life = 240.0;
        /// <summary>What the clouds' page says of it, for the development build.</summary>
        public static string Said = "";

        public static void Forget() { places.Clear(); Said = ""; }

        /// <summary>
        /// Each frame: the craft being flown, if it is in or near the clouds' layers and moving, leaves a place behind it every
        /// so often; the newest place is where it is now. `drift`: how far the wind has carried the clouds (the planet's axes, m);
        /// `wrap`: the length after which that comes round.
        /// </summary>
        public static void Follow(CelestialBody body, double ut, Vector3d drift, Vector3d fx, Vector3d fy, Vector3d fz, float lowest, float highest, double wrap)
        {
            Vessel v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (v == null || v.mainBody != body) { if (world != (body != null ? body.bodyName : "")) Forget(); return; }
            if (world != body.bodyName || craft != v.id) { Forget(); world = body.bodyName; craft = v.id; }
            // (gone after four minutes, the oldest first; and all of it if the game's clock has been put back)
            while (places.Count > 0 && (ut - places[places.Count - 1].When > Life || ut < places[places.Count - 1].When - 1.0)) places.RemoveAt(places.Count - 1);
            double alt = v.altitude;
            bool among = alt > lowest - 400.0 && alt < highest + 700.0 && v.srfSpeed > 12.0;
            if (!among) return;
            Vector3d rel = v.GetWorldPos3D() - body.position;
            Vector3d at = new Vector3d(Vector3d.Dot(rel, fx), Vector3d.Dot(rel, fy), Vector3d.Dot(rel, fz)) - drift;
            var now = new Place { At = at, When = ut };
            if (places.Count == 0) { places.Add(now); places.Add(now); }
            else
            {
                places[0] = now;
                // (a new place once the craft is a set way on from the last one left behind)
                if (Apart2(places[0].At, places[1].At, wrap) > Apart * Apart)
                {
                    places.Insert(1, now);
                    if (places.Count > Most) places.RemoveAt(places.Count - 1);
                }
            }
            // (How wide the wake is at first, and how much of the cloud in it is gone: some widths of the craft, and a
            // little of the cloud blown out; behind burning engines wider, and nearly all of it burnt off.)
            float thrust = 0f;
            foreach (Part p in v.parts)
                foreach (PartModule m in p.Modules)
                    if (m is ModuleEngines e && e.EngineIgnited) thrust += e.finalThrust;
            Vector3 size = v.vesselSize;
            float across = Mathf.Clamp(Mathf.Min(size.x, Mathf.Min(size.y, size.z)), 1f, 30f);
            float fire = Mathf.Clamp01(thrust / 600f);
            wide = Mathf.Lerp(wide, Mathf.Clamp(6f + 2.5f * across + 35f * Mathf.Sqrt(fire), 8f, 90f), 0.05f);
            hole = Mathf.Lerp(hole, Mathf.Lerp(0.35f, 0.95f, fire), 0.05f);
        }

        static double Apart2(Vector3d a, Vector3d b, double wrap)
        {
            Vector3d d = a - b;
            d.x -= wrap * Math.Round(d.x / wrap); d.y -= wrap * Math.Round(d.y / wrap); d.z -= wrap * Math.Round(d.z / wrap);
            return d.sqrMagnitude;
        }

        /// <summary>The wake, for the shader: each place from the camera (the planet's axes), with its age; how many; and where their middle is.</summary>
        public static void Hand(Material m, double ut, Vector3d noiseAt, double wrap, bool wanted)
        {
            int n = wanted ? Math.Min(places.Count, Most) : 0;
            // (not a wake until it has some length)
            if (n >= 2 && Apart2(places[0].At, places[n - 1].At, wrap) < 30.0 * 30.0) n = 0;
            Vector3d mid = Vector3d.zero;
            double oldest = 0.0;
            for (int k = 0; k < Most; k++)
            {
                Vector4 one = Vector4.zero;
                if (k < n)
                {
                    Vector3d d = places[k].At - noiseAt;
                    d.x -= wrap * Math.Round(d.x / wrap); d.y -= wrap * Math.Round(d.y / wrap); d.z -= wrap * Math.Round(d.z / wrap);
                    double age = Math.Max(0.0, ut - places[k].When);
                    one = new Vector4((float)d.x, (float)d.y, (float)d.z, (float)age);
                    mid += d / n;
                    oldest = Math.Max(oldest, age);
                }
                m.SetVector(ids[k], one);
            }
            float reach = 0f;
            for (int k = 0; k < n; k++)
            {
                Vector3d d = places[k].At - noiseAt;
                d.x -= wrap * Math.Round(d.x / wrap); d.y -= wrap * Math.Round(d.y / wrap); d.z -= wrap * Math.Round(d.z / wrap);
                reach = Mathf.Max(reach, (float)(d - mid).magnitude);
            }
            // (as wide as the shader lets the oldest of it grow, and twice that where it is widest, and what it is read from)
            float grown = wide * (1f + 1.5f * Mathf.Pow((float)oldest / 6f, 0.4f)) * 2f * 3.2f;
            m.SetVector(idLook, new Vector4(n, wide, hole, reach + grown));
            m.SetVector(idMid, new Vector4((float)mid.x, (float)mid.y, (float)mid.z, 0f));
            Said = n >= 2 ? "a wake of " + n + " places, " + wide.ToString("F0") + " m wide at first, the oldest " + oldest.ToString("F0") + " s old" : "";
        }

        static readonly int[] ids = { Shader.PropertyToID("_CloudWake0"), Shader.PropertyToID("_CloudWake1"), Shader.PropertyToID("_CloudWake2"), Shader.PropertyToID("_CloudWake3"),
                                      Shader.PropertyToID("_CloudWake4"), Shader.PropertyToID("_CloudWake5"), Shader.PropertyToID("_CloudWake6"), Shader.PropertyToID("_CloudWake7") };
        static readonly int idLook = Shader.PropertyToID("_CloudWakeLook"), idMid = Shader.PropertyToID("_CloudWakeMid");
    }
}
