using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>The large-scale weather at a place and time, before it is turned into clouds, rain and wind.</summary>
    struct Base
    {
        public float M;              // moisture, 0 dry to 1 a wet system overhead (after the climate and the settings)
        public float Inst;           // how strongly sun-warmed air is rising (heaped cloud, showers, storms), 0 to 1
        public float Front;          // the edge of a wet system, where showers line up, 0 to 1
        public float Land;           // 0 sea, 1 land
        public float SunUp;          // the sine of how high the sun stands
        public double Hour;          // degrees from noon (afternoon positive)
        public float Dawn;           // 0 to 1 about sunrise
        public float T0;             // the temperature at the ground, kelvin
        public float Ground;         // roughly how high the ground is, m (0 at sea)
        public float Freeze;         // the height of freezing, m above sea level
        public float DriftE, DriftN; // how the weather moves, m/s
        public float Dust;           // dust storm, 0 to 1
        public float Fog;            // 0 to 1
        public float Calm;           // how much the wind at the ground is held back, 1 none
        public float WindScale;      // the wind's strength from the settings and a chosen weather
        public float StormBias;      // a chosen storm: storms more readily
        public float Windy;          // a chosen windy weather: the wind above brought down to the ground, 0 to 1
        public bool Overridden;      // a chosen weather or a city's
    }

    /// <summary>The wind and the roughness of the air over one place, worked out now and then and kept for a moment.</summary>
    sealed class Column
    {
        public World W;
        public double Lat, Lon, Ut;
        public Base B;
        public WeatherHere Here;
        public float UE, UN;              // the wind at 10 m, m/s
        public float Alpha, Hbl;          // how it grows with height near the ground, and the boundary layer's depth (m above the ground)
        public float JE, JN, JetH;        // the wind high up, and its height (m above sea level)
        public float ShearE, ShearN, ShearH; // a sharp change of wind across a thin layer aloft, and its height (none: 0)
        public float Veer;                // how far it turns between the ground and the top of the boundary layer (radians)
        public float SigGround;           // the gusts' size near the ground (m/s)
        public float SigAloft;            // above the boundary layer, in clear air
        public float SigCloud;            // inside cloud
        public float SigStorm;            // near a storm
        public float LowBase, LowTop;     // the low cloud, where there is any (m above sea level)
        public float RefE, RefN;          // what carries the gusts along (m/s)
        public readonly List<WeatherCell> Cells = new List<WeatherCell>();
    }

    /// <summary>
    /// The weather itself: worked out from the place and the clock alone, so that it is the same every time, for every
    /// mod, after a quickload and in a forecast. Every method here may be called from any thread.
    ///
    /// A world's weather is a few smooth fields over the whole planet (like the weather map Horizon's clouds are drawn
    /// from, but made from noise instead of painted): moisture, which says where the weather systems are and drifts with
    /// the winds; the sun's warming, which builds heaped cloud through the morning and storms in the afternoon; and the
    /// game's own temperatures, which say rain or snow. Storms and showers are cells that are born, rain and die within
    /// an hour or so, drifting with the weather. The wind is the world's prevailing wind for the latitude, plus the
    /// systems' own winds, a sea breeze, and storms' outflow; it grows with height to a jet stream, and carries gusts
    /// frozen into it (the flight simulators' model of turbulence).
    /// </summary>
    static class Model
    {
        const double Deg = Math.PI / 180.0;

        // ---------------------------------------------------------------- the settings, as the model reads them

        public static volatile float Often = 0.25f;     // how often the weather turns bad, 0 never to 1 stormy
        public static volatile float Pace = 1f;         // how fast it changes
        public static volatile float WindStrength = 1f, GustStrength = 1f;
        public static volatile bool FogOn = true, DustOn = true;     // (off: none anywhere, for every mod that asks)
        public static volatile int Chosen;              // 0 as it comes, else one of Choices
        public static volatile float ChosenWeight;      // eased in and out
        public static Func<CelestialBody, bool> Allowed = b => true;

        public static readonly string[] Choices = { "As it comes", "Clear", "Fair", "Cloudy", "Overcast", "Rain", "Storm", "Fog", "Snow", "Windy", "Dust storm" };

        // ---------------------------------------------------------------- the large fields

        /// <summary>A pattern of noise over the planet that drifts east at `drift` m/s and changes into a new one every `period` seconds.</summary>
        static float Drifting(World w, double lat, double lon, double ut, double size, double period, uint seed, int octaves, float driftE)
        {
            period /= Pace <= 0.05f ? 0.05f : Pace;
            double k = ut / period;
            int k0 = Noise.Floor(k);
            float f = (float)(k - k0);
            float wgt = f * f * (3f - 2f * f);
            float a = Pattern(w, lat, lon, ut - k0 * period, size, seed + (uint)k0 * 7919u, driftE, octaves);
            float b = Pattern(w, lat, lon, ut - (k0 + 1) * period, size, seed + (uint)(k0 + 1) * 7919u, driftE, octaves);
            float n = a * (1f - wgt) + b * wgt;
            // (two unrelated patterns mixed half and half are flatter than either: made as lively again)
            return n / Noise.Sqrt((1f - wgt) * (1f - wgt) + wgt * wgt);
        }

        static float Pattern(World w, double lat, double lon, double since, double size, uint seed, float driftE, int octaves)
        {
            double cosLat = Math.Max(0.2, Math.Cos(lat * Deg));
            double lo = (lon - driftE * since / (w.Radius * cosLat) / Deg) * Deg, la = lat * Deg;
            double r = w.Radius / size;
            double cl = Math.Cos(la);
            return Noise.Fbm(cl * Math.Cos(lo) * r, cl * Math.Sin(lo) * r, Math.Sin(la) * r, octaves, seed ^ w.Seed);
        }

        /// <summary>How the weather systems move at a latitude, east (m/s): westward in the trade winds, eastward in the westerlies.</summary>
        static float BandDrift(World w, double lat)
        {
            float a = (float)Math.Abs(lat);
            Climate c = w.Climate;
            if (c.Bands > 0f) return c.Bands * 0.15f * (float)Math.Cos(lat * Deg * 6.0);
            return -0.9f * c.Trade * (1f - Noise.Smooth(18f, 34f, a)) + 1.4f * c.Wester * Noise.Bump(a, 48f, 22f) - 0.3f * c.Trade * Noise.Smooth(66f, 80f, a);
        }

        public static Base Sample(World w, double lat, double lon, double ut)
        {
            var b = new Base();
            Climate c = w.Climate;
            b.DriftE = BandDrift(w, lat);
            b.DriftN = 0f;
            float n = Drifting(w, lat, lon, ut, c.Systems, 3.0 * 3600.0, 11u, 3, b.DriftE);
            float a = (float)Math.Abs(lat);
            // (wetter near the equator and in the storm tracks, drier in the subtropics and at the poles)
            float bias = 0.07f * Noise.Bump(a, 0f, 14f) - 0.10f * Noise.Bump(a, 28f, 14f) + 0.06f * Noise.Bump(a, 55f, 16f) - 0.08f * Noise.Smooth(70f, 86f, a);
            float m = 0.5f + 0.62f * n + bias + (Often - 0.45f) * 0.55f + (c.Wet - 1f) * 0.32f;
            b.M = Noise.Clamp01(m);
            b.Front = Noise.Bump(b.M, 0.6f, 0.085f);

            b.Ground = Math.Max(0f, w.Ground(lat, lon));
            b.Land = w.Land(lat, lon);
            b.SunUp = w.SunUp(lat, lon, ut, out b.Hour);
            // The sun's warming lags it: the air rises most in the middle of the afternoon.
            float lag = (float)Math.Cos((b.Hour - 38.0) * Deg);
            float heat = lag > 0f ? lag * lag * Noise.Smooth(-0.05f, 0.3f, b.SunUp + 0.1f) : 0f;
            float latFactor = (float)Math.Sqrt(Math.Max(0.0, Math.Cos(lat * Deg)));
            b.Inst = Noise.Clamp01(heat * (0.3f + 0.7f * b.Land) * latFactor * c.Cumulus + 0.12f * (1f - b.Land) * (1f - heat));
            double sunrise = -90.0;
            b.Dawn = Noise.Bump((float)(b.Hour - sunrise), 12f, 30f);
            float t = w.Temperature(lat, Math.Max(0f, b.Ground));
            b.T0 = t + (b.Land > 0.5f ? 6f * (heat - 0.35f) : 1.5f * (heat - 0.35f));
            b.Freeze = w.Freezing(lat) + (b.T0 - t) / 0.0065f * 0.3f;
            b.Calm = 1f; b.WindScale = WindStrength; b.StormBias = 0f;

            if (c.Dusty > 0f)
            {
                float d = Drifting(w, lat, lon, ut, 600000.0, 2.0 * w.Day, 23u, 2, b.DriftE * 1.3f);
                float local = Noise.Smooth(0.42f, 0.62f, d + (Often - 0.25f) * 0.4f);
                // (and now and then, for weeks, the whole world)
                double slow = ut / (30.0 * 24.0 * 3600.0 / Math.Max(0.05f, Pace));
                int s0 = Noise.Floor(slow);
                float sf = (float)(slow - s0);
                float g0 = Noise.Unit(Noise.Hash((uint)s0 ^ w.Seed)), g1 = Noise.Unit(Noise.Hash((uint)(s0 + 1) ^ w.Seed));
                float g = g0 + (g1 - g0) * sf * sf * (3f - 2f * sf);
                float global = Noise.Smooth(0.88f, 0.97f, g + (Often - 0.25f) * 0.1f);
                b.Dust = Noise.Clamp01(Math.Max(local, global) * c.Dusty);
            }
            if (c.Fogs > 0f && c.Water > 0f)
                b.Fog = Noise.Clamp01(c.Fogs * b.Dawn * Noise.Smooth(0.45f, 0.7f, b.M) * (0.45f + 0.55f * (1f - b.Land) + 0.4f * Noise.Bump(b.Land, 0.5f, 0.5f)));

            Overrides.Apply(w, lat, lon, ref b);
            if (!FogOn) b.Fog = 0f;
            if (!DustOn) b.Dust = 0f;
            return b;
        }

        // ---------------------------------------------------------------- storms and showers

        const double CellBox = 22000.0;       // the spacing of the places storms can be born
        const double CellCycle = 6000.0;      // each place has one chance a cycle (two lattices, half a cycle apart)

        /// <summary>The storms and showers within `radius` metres of a place.</summary>
        public static int Cells(World w, double lat, double lon, double radius, double ut, List<WeatherCell> into)
        {
            if (w.Climate.Storms <= 0f && Chosen != 6) return 0;
            int found = 0;
            double cycle = CellCycle / (Pace <= 0.05f ? 0.05f : Pace);
            for (int lattice = 0; lattice < 2; lattice++)
            {
                double shifted = ut + lattice * cycle * 0.5;
                int c = Noise.Floor(shifted / cycle);
                double since = shifted - c * cycle;            // seconds since this lattice's cycle began
                float drift = BandDrift(w, lat) * 0.8f;
                double cosLat = Math.Max(0.2, Math.Cos(lat * Deg));
                double lonD = lon - drift * since / (w.Radius * cosLat) / Deg;      // where the place is in the frame the cells are born in
                double la = lat * Deg, lo = lonD * Deg, cl = Math.Cos(la);
                double px = w.Radius * cl * Math.Cos(lo) / CellBox, py = w.Radius * cl * Math.Sin(lo) / CellBox, pz = w.Radius * Math.Sin(la) / CellBox;
                int reach = (int)Math.Ceiling((radius + 9000.0) / CellBox) + 1;
                int ix = Noise.Floor(px), iy = Noise.Floor(py), iz = Noise.Floor(pz);
                double rr = w.Radius / CellBox;
                uint seed = w.Seed ^ (uint)(lattice * 0x51ed27u) ^ (uint)c * 0x2c1b3c6du;
                for (int a = -reach; a <= reach; a++)
                    for (int bb = -reach; bb <= reach; bb++)
                        for (int cc = -reach; cc <= reach; cc++)
                        {
                            int x = ix + a, y = iy + bb, z = iz + cc;
                            // (only the boxes the planet's surface passes through)
                            double cx = x + 0.5, cy = y + 0.5, cz = z + 0.5, cd = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                            if (Math.Abs(cd - rr) > 1.0) continue;
                            uint h = Noise.Hash(x, y, z, seed);
                            if (Noise.Unit(h) > 0.5f) continue;                       // half of them have a chance at all
                            float born = 0.45f * Noise.Unit(Noise.Hash(h ^ 0x1234567u));
                            float life = 0.5f + 0.1f * Noise.Unit(Noise.Hash(h ^ 0x89abcdu));
                            float age = (float)(since / cycle) - born;
                            if (age < 0f || age > life) continue;
                            float through = age / life;
                            // its place: a random point in the box, put on the surface
                            double qx = x + Noise.Unit(Noise.Hash(h ^ 0x9e3779b9U)), qy = y + Noise.Unit(Noise.Hash(h ^ 0x7f4a7c15U)), qz = z + Noise.Unit(Noise.Hash(h ^ 0x94d049bbU));
                            double ql = Math.Sqrt(qx * qx + qy * qy + qz * qz);
                            double cLat = Math.Asin(qz / ql) / Deg, cLonD = Math.Atan2(qy, qx) / Deg;
                            double cCos = Math.Max(0.2, Math.Cos(cLat * Deg));
                            float cDrift = BandDrift(w, cLat) * 0.8f;
                            double cLon = cLonD + cDrift * since / (w.Radius * cCos) / Deg;
                            // how far from the place
                            double dist = Distance(w, lat, lon, cLat, cLon);
                            if (dist > radius + 9000.0) continue;
                            Base cb = Sample(w, cLat, cLon, ut);
                            float potential = cb.Inst * Noise.Smooth(0.32f, 0.68f, cb.M) + 0.55f * cb.Front * Noise.Smooth(0.5f, 0.7f, cb.M);
                            potential = potential * w.Climate.Storms * (0.75f + 0.5f * Noise.Unit(Noise.Hash(h ^ 0xfeedu))) + cb.StormBias;
                            float most = Noise.Smooth(0.28f, 0.7f, potential);
                            if (most <= 0.02f) continue;
                            float shape = Noise.Smooth(0f, 0.3f, through) * (1f - Noise.Smooth(0.65f, 1f, through));
                            float strength = most * shape;
                            if (strength <= 0.02f) continue;
                            float radiusM = (float)(2500.0 + 5500.0 * most * (0.6f + 0.8f * Noise.Unit(Noise.Hash(h ^ 0xabcu))));
                            if (dist > radius + radiusM) continue;
                            bool thunder = w.Climate.Lightning > 0f && potential > 0.5f && Noise.Unit(Noise.Hash(h ^ 0x777u)) < 0.85f;
                            float baseH = cb.Ground + CumulusBase(cb);
                            float top = baseH + 1500f + (thunder ? 1f : 0.55f) * most * Math.Max(0f, w.Climate.Tops - baseH - 1500f);
                            into.Add(new WeatherCell
                            {
                                Id = (int)(h & 0x7fffffff), Latitude = cLat, Longitude = cLon, Radius = radiusM, Base = baseH, Top = top, Life = through,
                                Strength = strength, Rain = Noise.Clamp01((float)Math.Pow(strength, 1.2) * (thunder ? 1f : 0.65f) * w.Climate.Water),
                                Thunder = thunder, Drift = new Vector3d(cDrift, 0.0, 0.0),
                            });
                            found++;
                        }
            }
            return found;
        }

        /// <summary>The distance along the surface between two places, metres.</summary>
        public static double Distance(World w, double lat1, double lon1, double lat2, double lon2)
        {
            double a = lat1 * Deg, b = lat2 * Deg, dl = (lon2 - lon1) * Deg;
            double c = Math.Sin(a) * Math.Sin(b) + Math.Cos(a) * Math.Cos(b) * Math.Cos(dl);
            return w.Radius * Math.Acos(Math.Max(-1.0, Math.Min(1.0, c)));
        }

        /// <summary>The bearing from one place to another, as east and north parts of a unit length.</summary>
        public static void Toward(double lat1, double lon1, double lat2, double lon2, out float east, out float north)
        {
            double a = lat1 * Deg, b = lat2 * Deg, dl = (lon2 - lon1) * Deg;
            double y = Math.Sin(dl) * Math.Cos(b), x = Math.Cos(a) * Math.Sin(b) - Math.Sin(a) * Math.Cos(b) * Math.Cos(dl);
            double l = Math.Sqrt(x * x + y * y);
            if (l < 1e-9) { east = 0f; north = 0f; return; }
            east = (float)(y / l); north = (float)(x / l);
        }

        /// <summary>How high above the ground heaped cloud has its base: lower in moist air, higher in the afternoon.</summary>
        static float CumulusBase(Base b) => 350f + (1f - b.M) * 1500f + 500f * b.Inst;

        // ---------------------------------------------------------------- the weather at a place

        public static WeatherHere Here(World w, double lat, double lon, double ut) => ColumnAt(w, lat, lon, ut).Here;

        /// <summary>
        /// The clouds and what falls at a place, without the wind: cheaper than Here (no wind worked out, and the storms taken
        /// from a list kept for the region), for what asks at many places (the pattern of the clouds, the light).
        /// </summary>
        public static WeatherHere Layers(World w, double lat, double lon, double ut)
        {
            Base b = Sample(w, lat, lon, ut);
            return Build(w, lat, lon, ut, ref b, RegionCells(w, lat, lon, ut));
        }

        sealed class Region { public World W; public int A, B; public long T; public readonly List<WeatherCell> Cells = new List<WeatherCell>(); }
        static readonly Region[] regions = new Region[16];
        static int regionNext;

        /// <summary>The storms and showers about a place: those within 35 km of the middle of its two-degree square, kept for ten seconds.</summary>
        public static List<WeatherCell> RegionCells(World w, double lat, double lon, double ut)
        {
            double box = Math.Max(0.05, 2.0 * 600000.0 / w.Radius);
            int a = Noise.Floor(lat / box), bb = Noise.Floor(lon / box);
            long t = (long)Math.Floor(ut / 10.0);
            lock (cacheLock)
            {
                foreach (Region one in regions)
                    if (one != null && one.W == w && one.A == a && one.B == bb && one.T == t) return one.Cells;
            }
            var r = new Region { W = w, A = a, B = bb, T = t };
            double reach = box * 0.75 * Math.PI / 180.0 * w.Radius + 25000.0;
            Cells(w, (a + 0.5) * box, (bb + 0.5) * box, reach, ut, r.Cells);
            lock (cacheLock) { regions[regionNext] = r; regionNext = (regionNext + 1) % regions.Length; }
            return r.Cells;
        }

        public static WeatherHere Build(World w, double lat, double lon, double ut, ref Base b, List<WeatherCell> cells)
        {
            Climate c = w.Climate;
            var here = new WeatherHere();
            float tops = c.Tops / 11000f;
            float strat = Noise.Smooth(0.55f, 0.78f, b.M);
            float wet = Noise.Smooth(0.62f, 0.92f, b.M);
            float cu = Noise.Smooth(0.05f, 0.45f, b.Inst * Noise.Smooth(0.22f, 0.55f, b.M)) * 0.6f * c.Cumulus * (1f - strat);
            // ---- the low layer
            if (strat >= cu)
            {
                bool raining = wet > 0.5f && b.M > 0.72f;
                here.Low.Kind = raining ? CloudKind.Nimbostratus : strat > 0.7f ? CloudKind.Stratus : CloudKind.Stratocumulus;
                here.Low.Cover = strat;
                here.Low.Base = b.Ground + (raining ? 300f + 500f * (1f - wet) : here.Low.Kind == CloudKind.Stratus ? 200f + 600f * (1f - wet) : 600f + 900f * (1f - b.M));
                here.Low.Top = here.Low.Base + (raining ? 2500f + 2500f * wet : 400f + 1100f * wet);
                here.Low.Water = wet;
            }
            else
            {
                here.Low.Kind = CloudKind.Cumulus;
                here.Low.Cover = cu;
                here.Low.Base = b.Ground + CumulusBase(b);
                here.Low.Top = here.Low.Base + 300f + 2600f * b.Inst * b.M;
                here.Low.Water = 0.15f + 0.5f * Noise.Clamp01((here.Low.Top - here.Low.Base) / 3000f);
            }
            if (here.Low.Cover < 0.01f) { here.Low.Kind = CloudKind.None; here.Low.Cover = 0f; }
            // ---- the middle and high layers
            float mid = Noise.Smooth(0.5f, 0.76f, b.M) * 0.9f;
            here.Middle.Kind = mid > 0.55f ? CloudKind.Altostratus : CloudKind.Altocumulus;
            here.Middle.Cover = mid;
            here.Middle.Base = Math.Max(b.Ground + 1800f, 3000f * tops);
            here.Middle.Top = here.Middle.Base + 1200f + 1800f * wet;
            here.Middle.Water = 0.3f * wet;
            if (mid < 0.01f) { here.Middle.Kind = CloudKind.None; here.Middle.Cover = 0f; }
            float cirrus = Noise.Smooth(0.4f, 0.62f, b.M) * 0.55f + 0.3f * Noise.Clamp01(Drifting(w, lat, lon, ut, 120000.0, 2.0 * 3600.0, 31u, 2, b.DriftE * 2f) * 1.5f - 0.2f);
            here.High.Kind = cirrus > 0.01f ? CloudKind.Cirrus : CloudKind.None;
            here.High.Cover = Noise.Clamp01(cirrus);
            here.High.Base = Math.Max(b.Ground + 4000f, 7500f * tops);
            here.High.Top = here.High.Base + 1500f;
            here.High.Water = 0f;
            // ---- a deck that never goes away (Eve, Jool)
            if (c.Deck > 0f)
            {
                here.Middle.Kind = CloudKind.Altostratus; here.Middle.Cover = Math.Max(here.Middle.Cover, c.Deck);
                here.Middle.Base = c.DeckBase; here.Middle.Top = c.DeckTop; here.Middle.Water = 0.6f;
                if (w.Name == "Eve" && here.Low.Cover < 0.5f) { here.Low.Kind = CloudKind.Stratus; here.Low.Cover = 0.5f; here.Low.Base = 1500f; here.Low.Top = 3500f; here.Low.Water = 0.4f; }
                if (w.Name == "Jool") { here.Low.Kind = CloudKind.Stratus; here.Low.Cover = 1f; here.Low.Base = 0f; here.Low.Top = 15000f; here.Low.Water = 0.8f; }
            }
            // ---- what falls
            float rain = Noise.Smooth(0.72f, 0.96f, b.M) * 0.42f * c.Water;
            float storm = 0f;
            foreach (WeatherCell cell in cells)
            {
                double d = Distance(w, lat, lon, cell.Latitude, cell.Longitude);
                float inside = Noise.Clamp01(1f - (float)(d / cell.Radius));
                float core = inside * inside;
                rain = Math.Max(rain, cell.Rain * Noise.Smooth(0f, 0.6f, core));
                if (inside > 0f)
                {
                    float cover = Noise.Smooth(0f, 0.25f, inside) * cell.Strength;
                    if (cover > here.Low.Cover * 0.8f || cell.Thunder)
                    {
                        here.Low.Kind = cell.Thunder && cell.Strength > 0.35f ? CloudKind.Cumulonimbus : CloudKind.Cumulus;
                        here.Low.Cover = Math.Max(here.Low.Cover, cover);
                        here.Low.Base = Math.Min(here.Low.Base > 0f ? here.Low.Base : cell.Base, cell.Base);
                        here.Low.Top = Math.Max(here.Low.Top, cell.Top);
                        here.Low.Water = Math.Max(here.Low.Water, 0.5f + 0.5f * cell.Strength);
                    }
                }
                if (cell.Thunder) storm = Math.Max(storm, cell.Strength * Noise.Clamp01(1.4f - (float)(d / 20000.0)));
            }
            here.Rain = Noise.Clamp01(rain);
            here.Storm = storm;
            here.Snow = Noise.Smooth(275.6f, 272.4f, b.T0);
            here.Fog = b.Fog;
            here.Dust = b.Dust;
            if (b.Dust > 0.01f && here.Low.Cover < b.Dust)
            {
                here.Low.Kind = CloudKind.Dust; here.Low.Cover = b.Dust; here.Low.Base = b.Ground; here.Low.Top = b.Ground + 1500f + 4000f * b.Dust; here.Low.Water = 0f;
            }
            if (b.Fog > 0.3f && here.Low.Cover < b.Fog)
            {
                here.Low.Kind = CloudKind.Fog; here.Low.Cover = b.Fog; here.Low.Base = b.Ground; here.Low.Top = b.Ground + 60f + 240f * b.Fog; here.Low.Water = 0.3f;
            }
            // ---- how far one can see
            float seeing = 40000f * (1f - 0.55f * Noise.Smooth(0.4f, 0.9f, b.M));
            if (here.Rain > 0.01f) seeing = Math.Min(seeing, 12000f / (1f + 14f * here.Rain * (1f + 1.5f * here.Snow)));
            if (b.Fog > 0.01f) seeing = Math.Min(seeing, 150f + 20000f * (1f - b.Fog) * (1f - b.Fog) * (1f - b.Fog));
            // (a storm at its height leaves half a kilometre or so: a murk, with the nearest hills gone)
            if (b.Dust > 0.01f) seeing = Math.Min(seeing, 500f + 30000f * (1f - b.Dust) * (1f - b.Dust) * (1f - b.Dust));
            here.Seeing = seeing;
            here.Drift = new Vector3d(b.DriftE, b.DriftN, 0.0);
            here.Temperature = b.T0;
            Overrides.Shape(w, lat, lon, ref here);
            return here;
        }

        // ---------------------------------------------------------------- the wind

        static readonly object cacheLock = new object();
        static readonly Column[] cache = new Column[64];
        static int cacheNext;

        /// <summary>The weather and the wind over a place: kept for a moment (two seconds, half a kilometre), so that asking often costs little.</summary>
        public static Column ColumnAt(World w, double lat, double lon, double ut)
        {
            double qLat = Math.Round(lat * 20.0) / 20.0, qLon = Math.Round(lon * 20.0) / 20.0, qUt = Math.Floor(ut / 2.0) * 2.0;
            lock (cacheLock)
            {
                for (int n = 0; n < cache.Length; n++)
                {
                    Column one = cache[n];
                    if (one != null && one.W == w && one.Lat == qLat && one.Lon == qLon && one.Ut == qUt) return one;
                }
            }
            Column col = Make(w, qLat, qLon, qUt);
            lock (cacheLock)
            {
                cache[cacheNext] = col;
                cacheNext = (cacheNext + 1) % cache.Length;
            }
            return col;
        }

        /// <summary>Forget what is kept (when a setting has changed).</summary>
        public static void Forget() { lock (cacheLock) { for (int n = 0; n < cache.Length; n++) cache[n] = null; for (int n = 0; n < regions.Length; n++) regions[n] = null; } }

        static Column Make(World w, double lat, double lon, double ut)
        {
            var col = new Column { W = w, Lat = lat, Lon = lon, Ut = ut };
            Base b = Sample(w, lat, lon, ut);
            Cells(w, lat, lon, 25000.0, ut, col.Cells);
            Climate c = w.Climate;
            float a = (float)Math.Abs(lat);

            // ---- the prevailing wind for the latitude (east, north)
            float pe = BandDrift(w, lat) * Prevail, pn = -Math.Sign(lat) * 0.25f * c.Trade * (1f - Noise.Smooth(18f, 34f, a)) * (Prevail / 0.85f);
#if DEV
            if (OldWind) { pe = BandDrift(w, lat) * 0.85f; pn = -Math.Sign(lat) * 0.25f * c.Trade * (1f - Noise.Smooth(18f, 34f, a)); }
#endif
            // ---- the systems' own wind: round the wet lows, as the turning of the world makes it (and straight into them near the equator)
            const double step = 40000.0;
            double dLat = step / w.Radius / Deg, dLon = dLat / Math.Max(0.2, Math.Cos(lat * Deg));
            float mE = Sample(w, lat, lon + dLon, ut).M - Sample(w, lat, lon - dLon, ut).M;
            float mN = Sample(w, lat + dLat, lon, ut).M - Sample(w, lat - dLat, lon, ut).M;
            float gE = mE / (float)(2.0 * step / 100000.0), gN = mN / (float)(2.0 * step / 100000.0);       // per 100 km
            float s = (float)Math.Sin(lat * Deg);
            float turn = Noise.Smooth(0.08f, 0.35f, Math.Abs(s));
            float k = 28f * (c.Wester / 7f);
            float se = Math.Sign(lat) * k * gN * turn, sn = -Math.Sign(lat) * k * gE * turn;
            se += k * 0.5f * gE * (1f - turn); sn += k * 0.5f * gN * (1f - turn);
            float ue = pe + se, un = pn + sn;
            // ---- a sea breeze by day, a land breeze by night: where land and sea meet
            if (w.Body.ocean && b.Land > 0.02f && b.Land < 0.98f)
            {
                float l1 = w.Land(lat, lon + dLon) - w.Land(lat, lon - dLon), l2 = w.Land(lat + dLat, lon) - w.Land(lat - dLat, lon);
                float l = Noise.Sqrt(l1 * l1 + l2 * l2);
                if (l > 1e-3f)
                {
                    float breeze = Breeze * b.Inst - 1.2f * (1f - Noise.Smooth(-0.1f, 0.2f, b.SunUp));
#if DEV
                    if (OldWind) breeze = 3.5f * b.Inst - 1.2f * (1f - Noise.Smooth(-0.1f, 0.2f, b.SunUp));
#endif
                    ue += breeze * l1 / l; un += breeze * l2 / l;
                }
            }
            // ---- near the ground the air is mostly still. At night and in the early morning the ground cools the air over it into a
            // layer that the wind above slides over (the calm of a clear night, which lasts until the sun has warmed the ground again);
            // by day the sun's stirring brings some of the wind above down, a little in fair weather and most of it in unsettled weather,
            // when a system's own wind blows. (Asked for: smoke and explosions near the ground drift slowly, the wind "90% of the time
            // zero, and then the rest minimal wind, and very, very, very rarely strong wind", and with this mod as the weather is.)
            float day = Noise.Smooth(-0.1f, 0.25f, b.SunUp);
            float stirred = Noise.Smooth(0.05f, 0.6f, b.SunUp);         // (the stirring lags the dawn: the morning stays calm a while)
            float unsettled = Noise.Smooth(0.35f, 0.75f, b.M);
            float near = (NightNear + ((FairDayNear + (1f - FairDayNear) * unsettled) - NightNear) * stirred) * c.Calm * b.Calm;
            // (a system's wind blows night and day: an unsettled night is not still)
            near = Math.Max(near, 0.6f * unsettled * c.Calm * b.Calm);
            // (and a windy day held by the player has the wind at the ground, night and day)
            near += (c.Calm * b.Calm - near) * b.Windy;
#if DEV
            if (OldWind)
            {
                near = (0.5f + 0.5f * day) * c.Calm * b.Calm;
                near *= 0.55f + 0.45f * Noise.Smooth(0.2f, 0.5f, b.M) + 0.45f * day * (1f - Noise.Smooth(0.2f, 0.5f, b.M));
            }
#endif
            ue *= near; un *= near;
            // ---- storms blow out from under them: the gust front
            float sigStorm = 0f;
            foreach (WeatherCell cell in col.Cells)
            {
                double d = Distance(w, lat, lon, cell.Latitude, cell.Longitude);
                float x = (float)(d / cell.Radius);
                float out1 = 13f * cell.Strength * (cell.Thunder ? 1f : 0.55f) * (float)Math.Exp(-(x - 1.25f) * (x - 1.25f) / 0.8f) * Noise.Smooth(0.15f, 0.6f, x);
                if (out1 > 0.05f)
                {
                    Toward(cell.Latitude, cell.Longitude, lat, lon, out float te, out float tn);
                    ue += out1 * te; un += out1 * tn;
                }
                sigStorm = Math.Max(sigStorm, cell.Strength * (cell.Thunder ? 3.2f : 1.6f) * Noise.Clamp01(2.2f - x * 0.8f));
            }
            // Duna's dust storms are its strong winds
            if (b.Dust > 0.05f)
            {
                float l = Noise.Sqrt(ue * ue + un * un);
                float want = 4f + 20f * b.Dust;
                if (l < want) { float f = l > 0.3f ? want / l : 0f; if (f > 0f) { ue *= f; un *= f; } else ue += want; }
            }
            float scale = b.WindScale;
            col.UE = ue * scale; col.UN = un * scale;
            // (a real city's wind, where it reaches)
            bool city = Overrides.Wind(w, lat, lon, out float cityE, out float cityN, out float citySigma, out float cityWeight);
            if (city) { col.UE += (cityE * WindStrength - col.UE) * cityWeight; col.UN += (cityN * WindStrength - col.UN) * cityWeight; }

            // ---- how it changes with height
            col.Alpha = 0.13f + 0.17f * (1f - day) + 0.04f * b.Land;
            col.Hbl = 300f + 900f * day * (0.5f + 0.5f * b.Land);
            col.Veer = Math.Sign(lat) * -0.35f * turn;
            // (westerly jets in the middle latitudes, weaker easterlies over the tropics, both meandering and pulsing over days)
            float jetLat = c.Bands > 0f ? c.Bands * (float)Math.Cos(lat * Deg * 6.0) : c.Jet * (Noise.Bump(a, 40f, 22f) - 0.25f * (1f - Noise.Smooth(15f, 30f, a)));
            float meander = Drifting(w, lat, lon, ut, 900000.0, 8.0 * 3600.0, 47u, 2, b.DriftE);
            float meander2 = Drifting(w, lat, lon, ut, 900000.0, 8.0 * 3600.0, 53u, 2, b.DriftE);
            col.JE = (jetLat + c.Jet * 0.55f * meander + ue * 1.2f) * scale;
            col.JN = (c.Jet * 0.35f * meander2 + un * 1.2f) * scale;
            col.JetH = c.JetHeight;
            // (and now and then a thin layer aloft where the wind turns and strengthens sharply: what scrubs real launches for shear)
            float layer = Noise.Smooth(0.72f, 0.95f, 0.5f + Drifting(w, lat, lon, ut, 700000.0, 6.0 * 3600.0, 59u, 2, b.DriftE));
            if (layer > 0.01f)
            {
                float turnTo = 6.2831853f * Noise.Unit(Noise.Hash((uint)Math.Floor(ut / 21600.0) ^ w.Seed ^ 0x777u));
                col.ShearE = c.Jet * 0.5f * layer * (float)Math.Cos(turnTo) * scale; col.ShearN = c.Jet * 0.5f * layer * (float)Math.Sin(turnTo) * scale;
                col.ShearH = c.JetHeight * (0.35f + 0.6f * Noise.Unit(Noise.Hash((uint)Math.Floor(ut / 21600.0) ^ w.Seed ^ 0x999u)));
            }

            // ---- how rough the air is (MIL-F-8785C near the ground: the vertical gusts a tenth of the wind at 20 ft)
            float w20 = Noise.Sqrt(col.UE * col.UE + col.UN * col.UN) * 0.93f;
            float rough = (0.75f + 0.45f * b.Land) * c.Rough;
            col.SigGround = (0.1f * w20 * rough + 0.9f * b.Inst * b.Land * day + 0.1f) * GustStrength;
            if (city) col.SigGround += (citySigma * GustStrength - col.SigGround) * cityWeight;
            col.SigStorm = sigStorm * GustStrength;
            col.Here = Build(w, lat, lon, ut, ref b, col.Cells);
            col.Here.Wind = new Vector3d(col.UE, col.UN, 0.0);
            col.LowBase = col.Here.Low.Cover > 0.05f ? col.Here.Low.Base : 1e9f;
            col.LowTop = col.Here.Low.Cover > 0.05f ? col.Here.Low.Top : -1e9f;
            col.SigCloud = (col.Here.Low.Kind == CloudKind.Cumulonimbus ? 4f : col.Here.Low.Kind == CloudKind.Cumulus ? 1.6f : 0.8f) * Math.Max(col.Here.Low.Cover, 0.3f) * GustStrength * c.Rough;
            col.SigAloft = 0.35f * GustStrength * c.Rough;
            col.Here.Gusts = Sigmas(col, b.Ground + 10.0, 10f, out _, out _, out _);
            col.RefE = col.UE; col.RefN = col.UN;
            col.B = b;
            return col;
        }

        // How much of the wind above comes down to the ground: at night (and in the morning until the sun stirs the air), and by day in
        // fair weather (in unsettled weather all of it); how much of the latitude's prevailing wind blows at the ground; and how strong
        // the afternoon sea breeze is at most (m/s at 10 m). Measured over eight days at the space centre's pad, every ten minutes, the
        // weather as it comes, the mean wind 2 m up: still (under half a metre a second) 53% of the time, light (to 2 m/s) 45%, a breeze
        // (to 5) 1.4%, moderate (to 10) 1%, stronger only in a storm's gust front; by night still 72% of the time; on average 0.6 m/s
        // (0.9 at 10 m). Before 0.2.0 (half at night, all of it by day in fair weather but for a little under high pressure, 0.85,
        // 3.5): still 3%, light 50%, a breeze 40%, moderate 5%, strong 2%, on average 2.5 m/s (3.6 at 10 m), as windy as a real coast
        // and far windier than asked for ("90% of the time zero, and then the rest minimal wind, and very, very, very rarely strong").
        internal static float NightNear = 0.12f, FairDayNear = 0.3f, Prevail = 0.4f, Breeze = 2f;
#if DEV
        /// <summary>(For the development build: the wind near the ground worked out as before 0.2.0.)</summary>
        internal static bool OldWind;
#endif

        /// <summary>The mean wind (no gusts) at a height: east, north, up, m/s.</summary>
        public static Vector3 Mean(Column col, double altitude, float aboveGround)
        {
            float h = aboveGround < 0.5f ? 0.5f : aboveGround;
            float ue, un;
            if (h <= col.Hbl)
            {
                float f = (float)Math.Pow(h / 10f, col.Alpha);
                float turn = col.Veer * h / col.Hbl;
                float cs = (float)Math.Cos(turn), sn = (float)Math.Sin(turn);
                ue = (col.UE * cs - col.UN * sn) * f; un = (col.UE * sn + col.UN * cs) * f;
            }
            else
            {
                float f = (float)Math.Pow(col.Hbl / 10f, col.Alpha);
                float cs = (float)Math.Cos(col.Veer), sn = (float)Math.Sin(col.Veer);
                float be = (col.UE * cs - col.UN * sn) * f, bn = (col.UE * sn + col.UN * cs) * f;
                float topOfLayer = (float)(altitude - aboveGround) + col.Hbl;
                float rise = Noise.Smooth(topOfLayer, Math.Max(topOfLayer + 500f, col.JetH), (float)altitude);
                float above = (float)altitude > col.JetH ? (float)Math.Exp(-((altitude - col.JetH) / (0.7 * col.JetH)) * ((altitude - col.JetH) / (0.7 * col.JetH))) : 1f;
                ue = (be + (col.JE - be) * rise) * Math.Max(above, 0.25f);
                un = (bn + (col.JN - bn) * rise) * Math.Max(above, 0.25f);
                // (a thin layer where it changes sharply, about 800 m deep)
                if (col.ShearE != 0f || col.ShearN != 0f) { float st = Noise.Smooth(col.ShearH - 400f, col.ShearH + 400f, (float)altitude); ue += col.ShearE * st; un += col.ShearN * st; }
                // (where the air is all but gone it hardly matters, and is let go)
                float thin = 1f - Noise.Smooth((float)(col.W.Depth * 0.55), (float)(col.W.Depth * 0.9), (float)altitude);
                ue *= thin; un *= thin;
            }
            return new Vector3(ue, un, 0f);
        }

        /// <summary>How big the gusts are at a height (along the wind, across it, up) and how large the eddies (m): the flight simulators' rules near the ground, the cloud and the storms above.</summary>
        public static float Sigmas(Column col, double altitude, float aboveGround, out float sigW, out float lengthU, out float lengthW)
        {
            float h = aboveGround < 1f ? 1f : aboveGround;
            float ft = h * 3.28084f;
            float fc = ft < 10f ? 10f : ft > 1000f ? 1000f : ft;
            float q = 0.177f + 0.000823f * fc;
            float sw = col.SigGround;
            float su = sw / (float)Math.Pow(q, 0.4);
            float lw = Math.Min(h, 300f), lu = Math.Min(fc / (float)Math.Pow(q, 1.2) / 3.28084f, 533f);
            // above the layer near the ground: clear air, cloud, storms
            float aloft = Noise.Smooth(300f, 700f, h);
            float alt = (float)altitude;
            float inCloud = alt > col.LowBase - 100f && alt < col.LowTop + 200f ? 1f : 0f;
            float s2 = col.SigAloft + col.SigCloud * inCloud;
            su = su + (s2 - su) * aloft; sw = sw + (s2 - sw) * aloft;
            lu = lu + (533f - lu) * aloft; lw = lw + (533f - lw) * aloft;
            // storms are rough all the way up
            su = Math.Max(su, col.SigStorm); sw = Math.Max(sw, col.SigStorm * 0.8f);
            sigW = sw; lengthU = lu; lengthW = lw;
            return su;
        }
    }
}
