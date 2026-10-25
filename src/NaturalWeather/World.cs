using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// What kind of weather a world has: the numbers that make Kerbin's afternoons stormy and Duna's air dusty. Each
    /// stock world has its own; any other world with air (from a planet pack) gets one worked out from its air and seas.
    /// </summary>
    sealed class Climate
    {
        public float Wet = 1f;                 // how much weather the world has: 0 none, 1 Kerbin's
        public float Trade = 5f;               // the trade winds near the equator, from the east, at 10 m (m/s)
        public float Wester = 7f;              // the westerlies of the middle latitudes (m/s)
        public float Jet = 30f;                // the jet stream (m/s)
        public float JetHeight = 10000f;       // its height (m above sea level)
        public float Storms = 1f;              // how readily heaped cloud turns into showers and storms
        public float Lightning = 1f;           // whether storms make lightning
        public float Fogs = 1f;                // how readily fog forms
        public float Dusty = 0f;               // dust storms (Duna)
        public float Deck = 0f;                // a deck of cloud that never goes away (Eve, Jool), 0 to 1
        public float DeckBase = 4000f, DeckTop = 12000f;
        public float Tops = 11000f;            // how high the weather reaches (the top of the troposphere), m
        public float Systems = 350000f;        // how big weather systems are, m
        public float Calm = 1f;                // the surface wind against the wind aloft (Eve's air hardly moves at the ground)
        public float Rough = 1f;               // how rough the air is
        public float Bands = 0f;               // Jool's bands of wind (m/s)
        public float Water = 1f;               // whether what falls is water (Eve's falls and dries before it lands: 0)
        public float Cumulus = 1f;             // how readily fair-weather cumulus forms by day
        // (since 0.5.0, for worlds given a climate of their own, see Given: Real Worlds' Mars, whose water makes no low cloud and
        // only thin ice cloud high up; Venus, nothing but its deck)
        public float Low = 1f, Middle = 1f, High = 1f;          // how much of the low, middle and high cloud the air's moisture makes
        public float UnderDeck = 0f;                             // a low sheet of cloud under the deck, how much of the sky at least (Eve's, Jool's)
        public float UnderDeckBase = 1500f, UnderDeckTop = 3500f, UnderDeckWater = 0.4f;

        /// <summary>
        /// A world's climate: its own if it is a stock world, else worked out from its air and seas; and over either, whatever a
        /// config names for it (NATURAL_WEATHER_CLIMATE, since 0.5.0: see Given).
        /// </summary>
        public static Climate Of(CelestialBody body)
        {
            Climate c = Builtin(body);
            if (body != null && given != null && given.TryGetValue(body.bodyName, out Dictionary<string, float> values))
                foreach (KeyValuePair<string, float> v in values)
                {
                    FieldInfo f = typeof(Climate).GetField(v.Key, BindingFlags.Instance | BindingFlags.Public);
                    if (f != null && f.FieldType == typeof(float)) f.SetValue(c, v.Value);
                }
            return c;
        }

        static Dictionary<string, Dictionary<string, float>> given;

        /// <summary>
        /// (Since 0.5.0.) Climates given in configs, read once on the game's thread after it has loaded them (any thread may make a
        /// world): a node NATURAL_WEATHER_CLIMATE with the world's name as `body` and any of Climate's numbers by their names, each
        /// over the world's own. For a planet pack's worlds, or Real Worlds', whose real worlds stand in the stock ones' places
        /// under their names (its Venus is Eve, and must not have Eve's low cloud). Worlds already made take them at once.
        /// </summary>
        internal static void Given()
        {
            if (given != null || GameDatabase.Instance == null) return;
            var found = new Dictionary<string, Dictionary<string, float>>();
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("NATURAL_WEATHER_CLIMATE"))
            {
                string body = n.GetValue("body");
                if (string.IsNullOrEmpty(body)) continue;
                if (!found.TryGetValue(body, out Dictionary<string, float> values)) found[body] = values = new Dictionary<string, float>();
                foreach (ConfigNode.Value v in n.values)
                    if (v.name != "body" && float.TryParse(v.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) values[v.name] = x;
            }
            given = found;
            if (found.Count > 0) World.ClimatesAgain();
        }

        static Climate Builtin(CelestialBody body)
        {
            string name = body != null ? body.bodyName : "";
            switch (name)
            {
                case "Kerbin": return new Climate();
                case "Laythe": return new Climate { Wet = 1.15f, Trade = 6f, Wester = 8f, Jet = 25f, JetHeight = 8000f, Storms = 0.75f, Tops = 9000f, Fogs = 1.4f, Systems = 300000f };
                case "Duna": return new Climate { Wet = 0.12f, Trade = 4f, Wester = 7f, Jet = 25f, JetHeight = 7000f, Storms = 0f, Lightning = 0f, Fogs = 0.2f, Dusty = 1f, Tops = 9000f, Systems = 450000f, Cumulus = 0.15f, Water = 0f };
                case "Eve": return new Climate { Wet = 1.4f, Trade = 1.2f, Wester = 1.5f, Jet = 70f, JetHeight = 22000f, Storms = 0.35f, Lightning = 0.6f, Fogs = 0.5f, Deck = 1f, DeckBase = 4000f, DeckTop = 12000f, Tops = 26000f, Calm = 0.3f, Systems = 500000f, Water = 0f, Cumulus = 0.2f, UnderDeck = 0.5f, UnderDeckBase = 1500f, UnderDeckTop = 3500f, UnderDeckWater = 0.4f };
                case "Jool": return new Climate { Wet = 1f, Trade = 30f, Wester = 40f, Jet = 140f, JetHeight = 60000f, Storms = 0.7f, Lightning = 1f, Fogs = 0f, Deck = 1f, DeckBase = 20000f, DeckTop = 90000f, Tops = 120000f, Bands = 120f, Rough = 2.5f, Systems = 2000000f, Water = 0f, Cumulus = 0f, UnderDeck = 1f, UnderDeckBase = 0f, UnderDeckTop = 15000f, UnderDeckWater = 0.8f };
            }
            // A world from a planet pack: from its air and its seas.
            var c = new Climate();
            if (body == null || !body.atmosphere) { c.Wet = 0f; return c; }
            double scale = Math.Max(0.3, Math.Min(3.0, body.atmosphereDepth / 70000.0));
            c.JetHeight = (float)(10000.0 * scale); c.Tops = (float)(11000.0 * scale);
            if (!body.ocean) { c.Wet = 0.25f; c.Storms = 0.2f; c.Dusty = 0.5f; c.Fogs = 0.3f; c.Water = 0f; c.Cumulus = 0.3f; }
            return c;
        }
    }

    /// <summary>
    /// One world as the weather needs it: its size, its day, where its sun is, where its land and seas are, how warm its air
    /// is. The last three are read from the game on its own thread and kept, so that any thread can ask for the weather.
    /// </summary>
    sealed class World
    {
        public CelestialBody Body;
        public string Name;
        public double Radius, Depth, Day, Spin;
        public uint Seed;
        public Climate Climate;
        public CloudClimate Clouds;            // where its cloud usually is, if that is known (stock Kerbin's: see CloudClimate); read on the game's thread (Keep)
        bool cloudsAsked;
        public bool Air;
        public bool Grounded;                  // whether there is ground under the air (Jool has none)

        // where the sun stands over the world (the point it is straight above), and when that was so
        public double SunLat, SunLon, SunAt;

        // land and sea, and roughly how high the ground is: a coarse map, made on the game's thread when first wanted
        public const int MapW = 128, MapH = 64;
        public float[] Heights;                // metres above sea level (below 0: sea, to its floor)
        public bool Mapped;

        // the air's temperature by latitude (every 5 degrees) and height, kelvin, from the game's own curves
        const int TempLats = 19, TempHeights = 25;
        float[] temps;
        float tempStep;

        static readonly Dictionary<CelestialBody, World> all = new Dictionary<CelestialBody, World>();
        static readonly object locker = new object();

        public static World Of(CelestialBody body)
        {
            if (body == null) return null;
            lock (locker)
            {
                if (all.TryGetValue(body, out World w)) return w;
                w = new World(body);
                all[body] = w;
                return w;
            }
        }

        /// <summary>(Since 0.5.0.) Every world made so far takes its climate again (when climates given in configs have been read).</summary>
        internal static void ClimatesAgain()
        {
            lock (locker) foreach (World w in all.Values) w.Climate = Climate.Of(w.Body);
        }

        World(CelestialBody body)
        {
            Body = body; Name = body.bodyName; Radius = body.Radius; Air = body.atmosphere;
            Grounded = body.hasSolidSurface && (object)body.pqsController != null;      // (asked so that any thread may: no call into the game)
            Depth = body.atmosphere ? body.atmosphereDepth : 0.0;
            Spin = body.rotationPeriod;
            Day = body.solarDayLength > 1.0 ? body.solarDayLength : Math.Max(1.0, body.rotationPeriod);
            Seed = Noise.Hash((uint)Name.GetHashCode() ^ 0x5bd1e995U);
            Climate = Climate.Of(body);
            SunAt = double.NaN;
        }

        /// <summary>On the game's thread, now and then: where the sun stands, and a few more rows of the land map and the temperatures.</summary>
        public void Keep(double ut)
        {
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (sun != null && Body != sun)
            {
                Vector3d at = sun.position;
                SunLat = Body.GetLatitude(at); SunLon = Body.GetLongitude(at); SunAt = ut;
            }
            if (temps == null && Air) MakeTemps();
            // (its cloud's climate, once: the game's folder is known on its own thread alone; the weather worked out before it is forgotten)
            if (!cloudsAsked) { cloudsAsked = true; Clouds = CloudClimate.For(Name, Radius); if (Clouds != null) Model.Forget(); }
            // (a few dozen points a frame: each asks the terrain, which is not cheap; the whole map is made in a few seconds)
            if (!Mapped) MapSome(32);
        }

        /// <summary>The longitude the sun stands over at a time (the world turns under it, one turn a solar day).</summary>
        public double SunLongitude(double ut) => double.IsNaN(SunAt) ? 0.0 : SunLon - 360.0 * (ut - SunAt) / Day;

        /// <summary>The sine of how high the sun stands at a place and time, and the hour angle (degrees from noon, afternoon positive).</summary>
        public float SunUp(double lat, double lon, double ut, out double hour)
        {
            hour = lon - SunLongitude(ut);
            hour -= 360.0 * Math.Floor((hour + 180.0) / 360.0);
            double la = lat * Math.PI / 180.0, d = SunLat * Math.PI / 180.0, h = hour * Math.PI / 180.0;
            return (float)(Math.Sin(la) * Math.Sin(d) + Math.Cos(la) * Math.Cos(d) * Math.Cos(h));
        }

        void MapSome(int points)
        {
            if (Body.pqsController == null) { Mapped = true; return; }
            if (Heights == null) Heights = new float[MapW * MapH];
            for (int k = 0; k < points && !Mapped; k++)
            {
                int at = mapNext++;
                if (at >= MapW * MapH) { Mapped = true; break; }
                int row = at / MapW, i = at % MapW;
                double lat = -90.0 + (row + 0.5) * 180.0 / MapH, lon = -180.0 + (i + 0.5) * 360.0 / MapW;
                float h;
                try { h = (float)Body.TerrainAltitude(lat, lon, true); }
                catch (Exception) { h = 100f; }
                Heights[at] = h;
            }
        }
        int mapNext;

        /// <summary>Roughly how high the ground is (below 0: the sea floor), from the coarse map, between its points; 100 m where it is not made yet.</summary>
        public float Ground(double lat, double lon)
        {
            float[] h = Heights;
            if (h == null || !Mapped) return 100f;
            double x = (lon + 180.0) / 360.0 * MapW - 0.5, y = (lat + 90.0) / 180.0 * MapH - 0.5;
            int ix = Noise.Floor(x), iy = Noise.Floor(y);
            float fx = (float)(x - ix), fy = (float)(y - iy);
            int x0 = ((ix % MapW) + MapW) % MapW, x1 = (x0 + 1) % MapW;
            int y0 = Math.Max(0, Math.Min(MapH - 1, iy)), y1 = Math.Max(0, Math.Min(MapH - 1, iy + 1));
            float a = h[y0 * MapW + x0], b = h[y0 * MapW + x1], c = h[y1 * MapW + x0], d = h[y1 * MapW + x1];
            return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
        }

        // (how far east of each dot of the map the nearest land lies, along its latitude, m: the decks of cloud over the cool sea off
        // the west coasts (Model.Usual). Made once the map is whole.)
        float[] eastLand;

        /// <summary>How far east of the place the nearest land is, along its latitude, m (0 on land; very far where the map is not made).</summary>
        public float EastLand(double lat, double lon)
        {
            float[] h = Heights;
            if (h == null || !Mapped) return 1e8f;
            float[] e = eastLand;
            if (e == null)
            {
                e = new float[MapW * MapH];
                for (int j = 0; j < MapH; j++)
                {
                    double rowLat = (j + 0.5) / MapH * 180.0 - 90.0;
                    float step = (float)(2.0 * Math.PI / MapW * Radius * Math.Max(0.05, Math.Cos(rowLat * Math.PI / 180.0)));
                    // (swept from east to west twice round the row, so that every dot has seen the land east of it)
                    float since = 1e8f;
                    for (int n = 2 * MapW - 1; n >= 0; n--)
                    {
                        int i = n % MapW;
                        since = h[j * MapW + i] > 0f ? 0f : Math.Min(since + step, 1e8f);
                        if (n < MapW) e[j * MapW + i] = since;
                    }
                }
                eastLand = e;
            }
            // (between the map's dots, not by its nearest: by the nearest, a deck's edge followed the map's rows, 2.8 degrees each, and
            // from afar the decks were white bars along the latitudes)
            double fx = (lon + 180.0) / 360.0 * MapW - 0.5, fy = (lat + 90.0) / 180.0 * MapH - 0.5;
            int ix = Noise.Floor(fx), iy = Noise.Floor(fy);
            float tx = (float)(fx - ix), ty = (float)(fy - iy);
            int x0 = ((ix % MapW) + MapW) % MapW, x1 = (x0 + 1) % MapW;
            int y0 = Math.Max(0, Math.Min(MapH - 1, iy)), y1 = Math.Max(0, Math.Min(MapH - 1, iy + 1));
            float a = Math.Min(e[y0 * MapW + x0], 4e6f), b = Math.Min(e[y0 * MapW + x1], 4e6f), c = Math.Min(e[y1 * MapW + x0], 4e6f), d = Math.Min(e[y1 * MapW + x1], 4e6f);
            return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
        }

        /// <summary>How much of the place is land, 0 sea to 1 land (soft at the coast), from the coarse map.</summary>
        public float Land(double lat, double lon)
        {
            if (!Body.ocean) return 1f;
            float g = Ground(lat, lon);
            return Noise.Smooth(-60f, 60f, g);
        }

        void MakeTemps()
        {
            tempStep = (float)Math.Max(200.0, Math.Min(Depth, 60000.0) / (TempHeights - 1));
            var t = new float[TempLats * TempHeights];
            for (int a = 0; a < TempLats; a++)
                for (int b = 0; b < TempHeights; b++)
                {
                    double alt = b * tempStep;
                    double lat = a * 5.0;
                    double k;
                    try
                    {
                        // (the game's own rule, the day's average: half the sun's share; see the gotchas about GetFullTemperature)
                        k = Body.GetTemperature(alt) + Body.atmosphereTemperatureSunMultCurve.Evaluate((float)alt) * (Body.latitudeTemperatureBiasCurve.Evaluate((float)lat) + Body.latitudeTemperatureSunMultCurve.Evaluate((float)lat) * 0.5);
                    }
                    catch (Exception) { k = 288.0 - 0.0065 * alt; }
                    t[a * TempHeights + b] = (float)k;
                }
            temps = t;
        }

        /// <summary>The air's temperature at a latitude and height above sea level, kelvin (the day's average).</summary>
        public float Temperature(double lat, double alt)
        {
            float[] t = temps;
            if (t == null) return (float)(288.0 - 0.0065 * Math.Max(0.0, alt));
            double x = Math.Min(Math.Abs(lat), 90.0) / 5.0, y = Math.Max(0.0, alt) / tempStep;
            int ix = Math.Min(TempLats - 2, (int)x), iy = Math.Min(TempHeights - 2, (int)y);
            float fx = (float)(x - ix), fy = (float)Math.Min(1.0, y - iy);
            float a = t[ix * TempHeights + iy], b = t[(ix + 1) * TempHeights + iy], c = t[ix * TempHeights + iy + 1], d = t[(ix + 1) * TempHeights + iy + 1];
            return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
        }

        /// <summary>The height above sea level where the air is at freezing, at a latitude (or the top of the table if it never is).</summary>
        public float Freezing(double lat)
        {
            float prev = Temperature(lat, 0.0);
            if (prev <= 273.15f) return 0f;
            for (int b = 1; b < TempHeights; b++)
            {
                float now = Temperature(lat, b * tempStep);
                if (now <= 273.15f) return (b - 1 + (prev - 273.15f) / Math.Max(0.01f, prev - now)) * tempStep;
                prev = now;
            }
            return (TempHeights - 1) * tempStep;
        }
    }
}
