using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The weather the clouds are drawn from: Keystone's (Keystone.Weather: whatever weather mod answers there, Natural
    /// Weather's own model as a rule), so that the clouds drawn are the ones the light, the craft, the rain and the lightning
    /// agree with. With no weather mod answering, the clouds' own stand-in (CloudStandIn: mostly fair weather), so that they can
    /// be tried alone. Every method may be called from any thread but Drift's.
    /// </summary>
    static class CloudSource
    {
        /// <summary>Whether Keystone's weather is used (a weather mod answers, and no stand-in weather is held for a test).</summary>
        public static bool Keystones => Keystone.Weather.There && CloudStandIn.Held == null;

        public static string Name => Keystones ? Keystone.Weather.Provider.Name : "the clouds' own stand-in weather" + (CloudStandIn.Held != null ? " (held: " + CloudStandIn.Held + ")" : "");

        /// <summary>The weather at a place and moment (Keystone's types).</summary>
        public static WeatherHere At(CelestialBody body, double latitude, double longitude, double ut) =>
            Keystones ? Keystone.Weather.At(body, latitude, longitude, ut) : CloudStandIn.At(body, latitude, longitude, ut);

        /// <summary>
        /// The weather at one moment, for asking at very many places (the map of the whole planet): the clouds and what falls at
        /// each, the wind left out. Where the weather is Natural Weather's own it is asked of the model's atlas of the world,
        /// which finds every storm once for all the places (Model.Atlas: until 0.2.2 each of the map's 32,768 places was asked
        /// through Keystone, which works out the wind over each as well and finds the storms about each afresh: 3.4 seconds of a
        /// thread for a map of Kerbin, and no clouds until it was made). What is handed back is for one thread.
        /// </summary>
        public static Func<double, double, WeatherHere> Many(CelestialBody body, double ut)
        {
            if (!Keystones) return (latitude, longitude) => CloudStandIn.At(body, latitude, longitude, ut);
            Func<double, double, WeatherHere> own = Provider.Many(body, ut);
            if (own != null) return own;
            return (latitude, longitude) => Keystone.Weather.At(body, latitude, longitude, ut);
        }

        /// <summary>
        /// Whether the weather knows all it needs of this world yet. Natural Weather's own reads a coarse map of the world's land
        /// and seas from the game a few dozen places a frame, in the first seconds of the first flight of a session, and until
        /// that is done takes the whole world for land a hundred metres up: a map of the clouds made meanwhile is made again
        /// when it is. (Any thread.)
        /// </summary>
        public static bool Settled(CelestialBody body) => !Keystones || !(Keystone.Weather.Provider is Provider) || Provider.Knows(body);

        /// <summary>The storms and showers within a distance (m) of a place.</summary>
        public static int Cells(CelestialBody body, double latitude, double longitude, double radius, double ut, List<WeatherCell> into) =>
            Keystones ? Keystone.Weather.Cells(body, latitude, longitude, radius, ut, into) : CloudStandIn.Cells(body, latitude, longitude, radius, ut, into);

        /// <summary>Whether this world can have clouds at all (air; and weather for it, where a weather mod answers).</summary>
        public static bool Worth(CelestialBody body) => body != null && body.atmosphere && Grounded(body) && (Keystones || CloudStandIn.HasClouds(body));

        /// <summary>
        /// Whether the world has ground under its air. One that has none (Jool) is cloud all the way down, in its own colours,
        /// and what the game (and EVE) draw of it is its clouds already: these clouds, made for white heaps and sheets in the
        /// lowest sixteen kilometres of air over a ground, are not drawn there. (Until 0.2.2 they were: from a low orbit of Jool
        /// its weather's deck showed as a flat grey disc under the craft, as far out as the walk through 90 km of air reached
        /// it, on a planet whose own bands EVE had been told to hide.)
        /// </summary>
        public static bool Grounded(CelestialBody body) => body != null && body.hasSolidSurface && body.pqsController != null;

        /// <summary>Why these clouds are not drawn on a world that has air, for the page; nothing where they are, or where there is no more to say.</summary>
        public static string WhyNot(CelestialBody body) =>
            body != null && body.atmosphere && !Grounded(body) ? "No clouds of the mod's own on " + body.bodyName + ": it has no ground under its air, and keeps the clouds the game draws it with." : "";

        /// <summary>
        /// A kind of cloud as the shader takes it: 0 a flat sheet, 0.25 a lumpy layer, 0.5 heaps, 0.75 towering heaps, 1 storm
        /// towers. (Storm towers themselves are drawn from the storm cells, where lightning comes from: a layer called
        /// cumulonimbus is drawn as heaps building upward round them, kept well short of towers, so that the storms' own
        /// towers stand out of the layer as they do out of a real sky; a whole layer of towers was a wall.) -1: nothing to
        /// draw as cloud.
        /// </summary>
        public static float Kind(CloudLayer layer)
        {
            switch (layer.Kind)
            {
                case CloudKind.Cumulus: return 0.5f + Mathf.Clamp((layer.Top - layer.Base - 2000f) / 4000f, 0f, 0.18f);
                case CloudKind.Cumulonimbus: return 0.62f;
                case CloudKind.Stratocumulus: return 0.27f;
                case CloudKind.Stratus: return 0.08f;
                case CloudKind.Nimbostratus: return 0.04f;
                case CloudKind.Altocumulus: return 0.3f;
                case CloudKind.Altostratus: return 0.1f;
                default: return -1f;            // none, fog and dust (drawn by the weather itself), cirrus (drawn as the high sheet)
            }
        }
    }

    /// <summary>
    /// Lightning, as the clouds draw it: each flash the weather raises (Keystone.Weather.Flashed) lights the cloud round it from
    /// inside, flickering with each stroke down the same channel. (The weather mod decides when and where; nothing more is
    /// needed from it.)
    /// </summary>
    static class CloudFlashes
    {
        static readonly List<WeatherFlash> flashes = new List<WeatherFlash>();

        public static void Add(WeatherFlash f)
        {
            flashes.Add(f);
            if (flashes.Count > 16) flashes.RemoveAt(0);
        }

        /// <summary>
        /// The brightest two flashes lighting the clouds now: where (from the planet's centre, the scene's axes) and how bright.
        /// A flash's strokes come about 70 ms apart, each dying away in about 40 ms after its first bright instant.
        /// </summary>
        public static int Now(CelestialBody body, double ut, Vector3d[] at, float[] brightness)
        {
            for (int i = 0; i < brightness.Length; i++) brightness[i] = 0f;
            flashes.RemoveAll(f => f.Body != body || ut - f.Time > 2.0 || f.Time - ut > 60.0);
            int n = 0;
            foreach (WeatherFlash f in flashes)
            {
                double since = ut - f.Time;
                if (since < 0.0) continue;
                float b = 0f;
                for (int k = 0; k < Math.Max(1, f.Strokes); k++)
                {
                    double t = since - k * 0.07 - (k > 0 ? 0.02 * ((f.Seed >> k) & 3) : 0.0);
                    if (t >= 0.0) b += (float)Math.Exp(-t / 0.04) * (k == 0 ? 1f : 0.7f);
                }
                b *= Mathf.Max(0.2f, f.Brightness);
                if (b < 0.01f) continue;
                // (a flash to the ground is seen lighting its cloud from the lower part of the cloud; one in the cloud from its middle)
                double height = f.Ground ? Math.Max(f.Bottom, f.Top - 1500.0) : (f.Bottom + f.Top) * 0.5;
                // (from the planet's centre in the scene's axes, as the clouds take it: GetRelSurfacePosition answers in the planet's
                // own frame, which the scene stands turned against)
                Vector3d where = body.GetWorldSurfacePosition(f.Latitude, f.Longitude, height) - body.position;
                // (the brightest ones kept, in order)
                int slot = -1;
                if (n < at.Length) slot = n++;
                else { int dimmest = 0; for (int i = 1; i < n; i++) if (brightness[i] < brightness[dimmest]) dimmest = i; if (b > brightness[dimmest]) slot = dimmest; }
                if (slot >= 0) { at[slot] = where; brightness[slot] = b; }
            }
            return n;
        }
    }
}
