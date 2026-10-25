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
        public float Nose;           // a layer of air above freezing aloft, over ground that is below it, 0 to 1 (what falls melts in it: freezing rain, ice pellets)
        public float HailBias;       // a chosen hailstorm: storms' hail more readily
        public float Cyc;            // how far inside a cyclone's weather the place is, 0 to 1
        public float Eye, Wall, Band; // and of that: its clear eye, the wall of storms round the eye, a spiral band of storms
        public float Squall;         // the rain behind a squall line's row of storms, 0 to 1
        public float DriftE, DriftN; // how the weather moves, m/s
        public float Dust;           // dust storm, 0 to 1
        public float Fog;            // 0 to 1
        public FogKind FogOf;        // how it came about
        public float FogDeep;        // how deep it lies over the ground, m
        public float Calm;           // how much the wind at the ground is held back, 1 none
        public float WindScale;      // the wind's strength from the settings and a chosen weather
        public float StormBias;      // a chosen storm: storms more readily
        public float Windy;          // a chosen windy weather: the wind above brought down to the ground, 0 to 1
        public float HeldBreeze;     // a chosen weather's own wind at the ground, m/s: the wind its kind of system blows with (holding it evens out the systems that would have made it)
        public bool Overridden;      // a chosen weather or a city's
        public float Heat;           // how warm the sun has made the ground, lagging the sun (most in the middle of the afternoon), 0 to 1
        public float Stab;           // how stable the lowest air is under the lid of sinking air above it, 0 (rising: heaps) to 1 (a strong lid: a deck of cloud under it)
        public float Usual;          // how much of the usual sky's own low and middle cloud there is (1; a chosen clear sky or dust storm: none)
        public float Aloft;          // and of its middle cloud (1; a chosen fair sky: a little)
        public float Low;            // how deep the lows are here, for the wind round them: the wet systems' and the storms' (Lows), 0 to 1
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
        public readonly List<Burst> Bursts = new List<Burst>();          // the downbursts of those storms now
        public readonly List<Twister> Twisters = new List<Twister>();    // and their tornadoes and waterspouts
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
        public static volatile float TestUsual;          // (for setting one against the other: 1 leaves out the usual sky's own cloud (Usual, Dapple), as until 2026-10-18)
        // (the cover as until 2026-10-19, without the storms of the middle latitudes, the equator's clusters, the decks off west
        // coasts in their own regions, and the broken sky's clumps: the new cover is installed since 2026-10-19, 04:19, seen from
        // orbit against the old (docs/cloud-cover-plan.md); the development build's Probe.Cover("old") brings the old back)
        public static volatile bool OldCover = false;
#if DEV
        // (Development build: Probe.Meridian's view into the usual sky's low cloud at the place last worked out while Probing)
        internal static bool Probing;
        internal static float PDeck, POffWest, PSubtropic, PReach, PEast, PLifted;
#endif
        public static volatile float Pace = 1f;         // how fast it changes
        public static volatile float WindStrength = 1f, GustStrength = 1f;
        public static volatile bool FogOn = true, DustOn = true;     // (off: none anywhere, for every mod that asks)
        public static volatile int Chosen;              // 0 as it comes, else one of Choices
        public static volatile float ChosenWeight;      // eased in and out
        public static Func<CelestialBody, bool> Allowed = b => true;

        // (new kinds go on the end: a saved setting is the number of its place in this list)
        public static readonly string[] Choices = { "As it comes", "Clear", "Fair", "Cloudy", "Overcast", "Rain", "Storm", "Fog", "Snow", "Windy", "Dust storm", "Freezing rain", "Ice pellets", "Hailstorm",
                                                    "Downburst", "Tornado", "Waterspout", "Hurricane", "Squall line" };
        // (the violent ones, held: each is put where it can be looked at from the space centre, on Kerbin; see Violent)
        public const int HeldBurst = 14, HeldTornado = 15, HeldSpout = 16, HeldCyclone = 17, HeldSquall = 18;

        // ---------------------------------------------------------------- the large fields

        /// <summary>A pattern of noise over the planet that drifts east at `drift` m/s and changes into a new one every `period` seconds.</summary>
        static float Drifting(World w, double lat, double lon, double ut, double size, double period, uint seed, int octaves, float driftE, float pulled = 0f)
        {
            period /= Pace <= 0.05f ? 0.05f : Pace;
            double k = ut / period;
            int k0 = Noise.Floor(k);
            float f = (float)(k - k0);
            float wgt = f * f * (3f - 2f * f);
            float a = Pattern(w, lat, lon, ut - k0 * period, size, seed + (uint)k0 * 7919u, driftE, octaves, pulled);
            float b = Pattern(w, lat, lon, ut - (k0 + 1) * period, size, seed + (uint)(k0 + 1) * 7919u, driftE, octaves, pulled);
            float n = a * (1f - wgt) + b * wgt;
            // (two unrelated patterns mixed half and half are flatter than either: made as lively again)
            return n / Noise.Sqrt((1f - wgt) * (1f - wgt) + wgt * wgt);
        }

        /// <summary>
        /// How the heaps of a broken sky gather (the new cover): seen from afar a field of cumulus is never even, but clumps of
        /// clumps, patches tens of kilometres across where the ground or the sea warms more, or where outflows and sea breezes meet,
        /// with clear sky between, and the clumps gathered again into fields of a couple of hundred kilometres (the satellites'
        /// pictures: "cloud streets" and cell fields, never an even sprinkling). Two drifting patterns, of about 180 km (changing over
        /// ten hours) and 45 km (three), give each place a share of the weather's cover from nought to about four times it, about
        /// four fifths of it on average, so a region keeps nearly the cover the weather gives it. Until 2026-10-19 every place had its region's cover,
        /// and the far field drew it as an even popcorn the whole world over (the owner: "too even and evenly spaced out").
        /// </summary>
        static float Clumps(World w, double lat, double lon, double ut, float driftE)
        {
            float fields = Drifting(w, lat, lon, ut, 180000.0, 10.0 * 3600.0, 211u, 1, driftE, Warp);
            float clumps = Drifting(w, lat, lon, ut, 45000.0, 3.0 * 3600.0, 223u, 2, driftE, Warp);
            return 2.8f * Noise.Smooth(-0.1f, 0.3f, fields) * (0.15f + 1.4f * Noise.Smooth(-0.15f, 0.35f, clumps));
        }

        static float Pattern(World w, double lat, double lon, double since, double size, uint seed, float driftE, int octaves, float pulled)
        {
            double cosLat = Math.Max(0.2, Math.Cos(lat * Deg));
            double lo = (lon - driftE * since / (w.Radius * cosLat) / Deg) * Deg, la = lat * Deg;
            double r = w.Radius / size;
            double cl = Math.Cos(la), sl = Math.Sin(la), co = Math.Cos(lo), so = Math.Sin(lo);
            double px = cl * co * r, py = cl * so * r, pz = sl * r;
            // (The flow's work, 2026-10-21, for the patterns that are seen as cloud: the pattern read where the air now here was, pulled
            // about by a wind of eddies three times its own size, more east and west than north and south, as the jets shear the weather
            // out along them. Read where it is, a pattern of noise is round blobs of one kind from pole to pole; the satellites' and the
            // weather models' cloud is drawn out into bands, swirls and filaments. The owner, of the Kerbal Weather Project's: "their
            // clouds are better /more realistic". Not for those that only decide what kind of sky a region has: unseen, and it costs.)
            if (pulled > 0f)
            {
                double q = 0.33;
                uint ws = seed ^ w.Seed ^ 0x77a1u;
                float fe = Noise.Fbm(px * q + 3.1, py * q, pz * q, 2, ws), fn = Noise.Fbm(px * q, py * q + 5.7, pz * q - 2.3, 2, ws ^ 0x5u);
                double ae = 1.9 * pulled * fe, an = 0.9 * pulled * fn;
                px += ae * -so + an * -sl * co; py += ae * co + an * -sl * so; pz += an * cl;
            }
            return Noise.Fbm(px, py, pz, octaves, seed ^ w.Seed);
        }

        /// <summary>(Development: how much of the regional wet and dry over Kerbin goes by its cloud's climate (CloudClimate), 0 none, 1 all; and how strongly, and the shift of the whole.)</summary>
        public static volatile float Climatology = 1f, ClimateGain = 0.7f, ClimateShift = 0f;

        /// <summary>(Development: how far the drifting patterns are pulled about by their eddies, 0 not at all, 1 as made since 2026-10-21.)</summary>
        public static volatile float Warp = 1f;

        /// <summary>How the weather systems move at a latitude, east (m/s): westward in the trade winds, eastward in the westerlies.</summary>
        static float BandDrift(World w, double lat)
        {
            float a = (float)Math.Abs(lat);
            Climate c = w.Climate;
            if (c.Bands > 0f) return c.Bands * 0.15f * (float)Math.Cos(lat * Deg * 6.0);
            return -0.9f * c.Trade * (1f - Noise.Smooth(18f, 34f, a)) + 1.4f * c.Wester * Noise.Bump(a, 48f, 22f) - 0.3f * c.Trade * Noise.Smooth(66f, 80f, a);
        }

        /// <summary>(For the weather centre's pressure: the prevailing wind east at a latitude, above the ground's calm, m/s.)</summary>
        internal static float PrevailingEast(World w, double lat) => BandDrift(w, lat) * Prevail;

        /// <summary>(And how strongly the systems' wind turns round their lows there: the wind for a unit of Base.Low across 100 km, m/s.)</summary>
        internal static float SystemsWind(World w, double lat) => 28f * (w.Climate.Wester / 7f) * Noise.Smooth(0.08f, 0.35f, (float)Math.Abs(Math.Sin(lat * Deg)));

        public static Base Sample(World w, double lat, double lon, double ut)
        {
            var b = new Base();
            Climate c = w.Climate;
            b.DriftE = BandDrift(w, lat);
            b.DriftN = 0f;
            float n = Drifting(w, lat, lon, ut, c.Systems, 3.0 * 3600.0, 11u, 3, b.DriftE, Warp);
            float a = (float)Math.Abs(lat);
            // (wetter near the equator and in the storm tracks, drier in the subtropics and at the poles)
            float bias = 0.07f * Noise.Bump(a, 0f, 14f) - 0.10f * Noise.Bump(a, 28f, 14f) + 0.06f * Noise.Bump(a, 55f, 16f) - 0.08f * Noise.Smooth(70f, 86f, a);
            // (Over stock Kerbin, since 2026-10-21, where a weather model of its air over its land and sea puts its cloud, by place and
            // the time of day, against its average (CloudClimate): the storm tracks, the subtropics, the poles, the deserts and the
            // cloudier sea, where the bands by latitude above knew only the latitude, and made the land cloudier than the sea.)
            CloudClimate usualCloud = w.Clouds;
            if (usualCloud != null && Climatology > 0f)
                bias += (ClimateGain * (usualCloud.At(CloudClimate.Total, lat, lon, ut) - usualCloud.Mean) + ClimateShift - bias) * Climatology;
            // (Where the storms of the middle latitudes are (Lows), they are the weather's systems, with their shapes: the noise's
            // wet and dry regions, which are of one size, much weaker there. Until 2026-10-19 the noise was the systems everywhere,
            // blobs of one size from pole to pole: from afar, the owner's "too even and evenly spaced out".)
            float tracks = Lows.Has(w) && !OldCover ? Noise.Bump(a, 52f, 20f) : 0f;
            float m = 0.5f + 0.62f * n * (1f - 0.55f * tracks) - 0.06f * tracks + bias + (Often - 0.45f) * 0.55f + (c.Wet - 1f) * 0.32f;
            b.M = Noise.Clamp01(m);
            b.Low = b.M;
            b.Front = Noise.Bump(b.M, 0.6f, 0.085f);
            // ---- the storms of the middle latitudes: their heads, shields and fronts wet, their dry slots clear, showers behind them
            float lowWet = 0f, lowFront = 0f, lowDry = 0f, lowCells = 0f, lowDeep = 0f;
            List<Low> lows = OldCover ? Lows.None : Lows.At(w, ut);
            for (int i = 0; i < lows.Count; i++) Lows.Of(w, lows[i], lat, lon, ref lowWet, ref lowFront, ref lowDry, ref lowCells, ref lowDeep);
            if (lowWet > 0.001f || lowDry > 0.001f)
            {
                b.M = Math.Min(Math.Max(b.M, lowWet), 1f - 0.8f * lowDry);
                b.Front = Math.Max(b.Front * (1f - lowDry), lowFront);
            }
            b.Low = Math.Max(b.Low, 0.5f + 0.45f * lowDeep);

            b.Ground = Math.Max(0f, w.Ground(lat, lon));
            b.Land = w.Land(lat, lon);
            b.SunUp = w.SunUp(lat, lon, ut, out b.Hour);
            // The sun's warming lags it: the air rises most in the middle of the afternoon.
            float lag = (float)Math.Cos((b.Hour - 38.0) * Deg);
            float heat = lag > 0f ? lag * lag * Noise.Smooth(-0.05f, 0.3f, b.SunUp + 0.1f) : 0f;
            float latFactor = (float)Math.Sqrt(Math.Max(0.0, Math.Cos(lat * Deg)));
            b.Inst = Noise.Clamp01(heat * (0.3f + 0.7f * b.Land) * latFactor * c.Cumulus + 0.12f * (1f - b.Land) * (1f - heat));
            // (and over the warm sea of the equator's rain belt, by day and by night: where the trade winds meet and the air rises in
            // towers, the cumulonimbus of the observers' tables (6 % of the oceans' sky), most of them in a band a few degrees wide)
            // (Not an even band: from afar the equator's storms are clusters, each some hundreds of kilometres on the Earth, with
            // clear sea between, along a line that wanders a few degrees either side (NASA's EPIC views, 2026-10-16): here clusters
            // about a tenth of the world's radius across, drifting west with the trade winds and coming and going over hours.)
            float wander = 3.5f * (float)Math.Sin(lon * Deg * 2.0 + 1.7 * Math.Sin(lon * Deg * 5.0 + 0.7) + ut / (5.0 * w.Day));
            float clusters = Noise.Smooth(0.42f, 0.64f, 0.5f + 0.5f * Drifting(w, lat, lon, ut, 0.11 * w.Radius, 3.0 * 3600.0, 71u, 2, -0.8f * c.Trade, Warp));
            if (OldCover) b.Inst = Math.Max(b.Inst, 0.62f * Noise.Bump(a, 0f, 11f) * (1f - b.Land) * c.Cumulus);
            else b.Inst = Math.Max(b.Inst, 0.62f * Noise.Bump((float)Math.Abs(lat - wander), 0f, 9f) * (1f - b.Land) * c.Cumulus * (0.2f + 0.8f * clusters));
            // (and behind a storm's cold front, cold air over the warmer sea: showers in cells)
            if (lowCells > 0.001f) b.Inst = Math.Max(b.Inst, 0.5f * lowCells * (0.35f + 0.65f * (1f - b.Land)) * c.Cumulus);
            double sunrise = -90.0;
            b.Dawn = Noise.Bump((float)(b.Hour - sunrise), 12f, 30f);
            float t = w.Temperature(lat, Math.Max(0f, b.Ground));
            b.T0 = t + (b.Land > 0.5f ? 6f * (heat - 0.35f) : 1.5f * (heat - 0.35f));
            b.Freeze = w.Freezing(lat) + (b.T0 - t) / 0.0065f * 0.3f;
            // (Warm air riding up over cold: at the edge of a wet system, where the ground is at or below freezing, the
            // air a kilometre or two up is often above it. Snow falling through that layer melts; what it does next is
            // by how deep the cold air under it is: see Build.)
            b.Nose = b.Front * Noise.Smooth(276f, 272.5f, b.T0);
            b.Calm = 1f; b.WindScale = WindStrength; b.StormBias = 0f;
            b.Heat = heat; b.Usual = TestUsual == 1f ? 0f : 1f; b.Aloft = 1f;
            // How stable the lowest air is (roughly what the meteorologists call the lower troposphere's stability: how much
            // warmer the air three kilometres up is than the air over the sea). Where air sinks from aloft and lies warm over
            // the cooler sea under it (the subtropical highs, strongest about 20 to 35 degrees, and the cold air of high
            // latitudes), a lid caps the lowest kilometre and it fills with a sheet of stratocumulus, as over the eastern
            // oceans for weeks on end; where air rises (the equator's rain belt) or the sea is warm under it, it holds heaps.
            // Over regions a thousand kilometres or so across that last a couple of days, and the drier the air aloft (the
            // more it has sunk), the stronger the lid.
            float region = Drifting(w, lat, lon, ut, 700000.0, 12.0 * 3600.0, 57u, 2, b.DriftE * 0.6f);
            // (The lid of high latitudes in regions, not a ring round the world: the cold seas' decks come and go with the air over
            // them, some hundreds of kilometres at a time. As a ring, from afar a white belt at 70 degrees, the same all round.)
            float polar = OldCover ? 1f : Noise.Smooth(0.38f, 0.62f, 0.5f + 0.5f * Drifting(w, lat, lon, ut, 1.6 * Lows.Reach(w), 10.0 * 3600.0, 83u, 2, b.DriftE));
            // (and, the new cover, in regions of a few hundred kilometres as well, the world's size taken into account as the storms'
            // are: on a small world the 700 km pattern above is nearly the whole of it, and the lid followed the latitude alone, so
            // that everything it decides (decks, heaps, deserts) changed along a line of latitude: from afar, straight-edged stripes)
            float lidHere = OldCover ? 0f : 0.3f * Drifting(w, lat, lon, ut, 1.2 * Lows.Reach(w), 18.0 * 3600.0, 61u, 1, b.DriftE);
            b.Stab = Noise.Clamp01(0.18f + 0.72f * Noise.Bump(a, 27f, 15f) + 0.4f * Noise.Smooth(50f, 72f, a) * polar - 0.18f * Noise.Bump(a, 0f, 9f) + 0.5f * region + lidHere + 0.45f * (0.45f - b.M));

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
            if (c.Fogs > 0f && c.Water > 0f) Fogs(w, lat, lon, ut, c, t, ref b);

            Overrides.Apply(w, lat, lon, ref b);
            // ---- a cyclone's own weather: overcast from its rim inward, storms in its wall and its bands, a clear eye
            List<Cyclone> cyclones = Violent.Cyclones(w, ut);
            for (int i = 0; i < cyclones.Count; i++)
            {
                float inside = Violent.CycloneAt(w, cyclones[i], lat, lon, out float eye, out float wall, out float band);
                if (inside <= b.Cyc) continue;
                b.Cyc = inside; b.Eye = eye; b.Wall = wall; b.Band = band;
            }
            if (b.Cyc > 0.01f)
            {
                float storms = Math.Max(b.Wall, b.Band);
                b.M += (0.8f + 0.17f * storms - b.M) * b.Cyc;
                b.M += (0.3f - b.M) * b.Eye;
                b.Inst += (storms - b.Inst) * b.Cyc;
                b.Inst *= 1f - b.Eye;
                b.StormBias += 0.5f * storms * b.Cyc * (1f - b.Eye);
                b.Fog *= 1f - b.Cyc; b.Dust *= 1f - b.Cyc; b.Calm += (1f - b.Calm) * b.Cyc;
            }
            // ---- a squall line: its row of storms is its own (Violent.SquallCells); behind the row, twenty-five
            // kilometres of steady rain from the spread-out cloud the storms leave
            List<Squall> squalls = Violent.Squalls(w, ut);
            for (int i = 0; i < squalls.Count; i++)
            {
                Squall q = squalls[i];
                if (!Violent.SquallAt(w, q, lat, lon, out float ahead, out float along)) continue;
                float ends = 1f - Noise.Smooth(q.Length * 0.4f, q.Length * 0.5f, Math.Abs(along));
                float trailing = Noise.Smooth(-32000f, -22000f, ahead) * (1f - Noise.Smooth(-2000f, 4000f, ahead)) * q.Now * ends;
                b.Squall = Math.Max(b.Squall, trailing);
                b.M += (0.9f - b.M) * trailing;
                // (and ahead of it the sky it comes out of: some cloud, no more)
                float near = (1f - Noise.Smooth(4000f, 26000f, Math.Abs(ahead))) * q.Now * ends;
                b.M = Math.Max(b.M, 0.6f * near);
            }
            if (!FogOn) b.Fog = 0f;
            if (!DustOn) b.Dust = 0f;
            return b;
        }

        // ---------------------------------------------------------------- fog
        //
        // Fog is cloud on the ground, and it comes about in a handful of ways that have little to do with one another.
        // Which way says where it lies, how deep it is and when it goes; so each is worked out by itself, from what the
        // model already has of a place (land or sea, how high, how damp, how cold, the hour), and the thickest is the fog
        // there. (Until 2026-10-13 there was one fog: about sunrise, mostly at sea.)
        //
        // Ground fog (radiation). Over land on a still night under a sky clear enough for the ground to cool by: the air
        // on the ground cools to its dew point. It gathers from the middle of the night, is thickest at sunrise, lies in
        // low ground, is shallow (tens of metres: masts stand out of it, and the sky shows overhead), and the sun burns
        // it off in two or three hours. Wind stirs it away; an overcast stops it forming.
        // Sea fog (advection). Damp air drifting over cooler sea, cooled from below: banks a hundred kilometres across
        // that go with the wind, at any hour, a few hundred metres deep; over cool seas more than warm ones; onto the
        // coast by night and back off it as the land warms.
        // Hill fog (upslope). Damp air lifted over high ground cools as it climbs: the cloud's base is on the hill.
        // Steam fog. Air well below freezing come off the land over open water at the end of a still night: the water
        // smokes, a few metres high. Only where it is cold.
        // Rain fog (frontal). Under the steady rain of a wet system, in cool air: the rain wets the air beneath it
        // until it can hold no more.

        static void Fogs(World w, double lat, double lon, double ut, Climate c, float seaLevel, ref Base b)
        {
            float hour = (float)b.Hour;
            bool day = hour > -90f && hour < 90f;
            // (how far through the night, 0 at sunset to 1 at sunrise; and by day, how long since sunrise, in degrees of the sun's round: 15 to an hour)
            float night = hour >= 90f ? (hour - 90f) / 180f : hour <= -90f ? (hour + 270f) / 180f : 0f;
            float since = day ? hour + 90f : 0f;
            // (where the night is still: patches some hundreds of kilometres across that last half a day)
            float still = Noise.Smooth(0.4f, 0.56f, 0.5f + 0.5f * Drifting(w, lat, lon, ut, 260000.0, 6.0 * 3600.0, 41u, 2, b.DriftE * 0.5f));
            float damp = Noise.Bump(b.M, 0.55f, 0.21f);
            float radiation = 1.5f * Noise.Smooth(0.5f, 0.8f, b.Land) * (day ? 1f - Noise.Smooth(15f, 50f, since) : Noise.Smooth(0.4f, 0.85f, night)) * damp * still * (1f - Noise.Smooth(250f, 900f, b.Ground));
            // (the new cover: radiation fog lies in patches, the low and damp places, tens of kilometres across, not in sheets the width
            // of a continent; as sheets they followed the moisture, which goes with the latitude, and from afar made stripes)
            if (!OldCover) radiation *= Noise.Smooth(0.42f, 0.62f, 0.5f + 0.5f * Drifting(w, lat, lon, ut, 0.25 * Lows.Reach(w), 5.0 * 3600.0, 67u, 1, b.DriftE * 0.3f));

            float bank = Noise.Smooth(0.5f, 0.68f, 0.5f + 0.5f * Drifting(w, lat, lon, ut, 180000.0, 9.0 * 3600.0, 43u, 3, b.DriftE));
            float ashore = Noise.Bump(b.Land, 0.5f, 0.3f) * (day ? 1f - 0.85f * Noise.Smooth(15f, 60f, since) * (1f - Noise.Smooth(150f, 178f, since)) : 1f);
            float where = Noise.Clamp01(1f - Noise.Smooth(0.3f, 0.6f, b.Land) + ashore) * (1f - Noise.Smooth(100f, 400f, b.Ground));
            float advection = 1.3f * where * Noise.Smooth(0.46f, 0.68f, b.M) * bank * (0.35f + 0.65f * Noise.Smooth(297f, 286f, seaLevel));

            float upslope = 0.85f * Noise.Smooth(450f, 1300f, b.Ground) * Noise.Smooth(0.52f, 0.72f, b.M);

            float steam = Noise.Bump(b.Land, 0.3f, 0.25f) * (day ? 1f - Noise.Smooth(5f, 25f, since) : Noise.Smooth(0.6f, 0.9f, night)) * Noise.Smooth(283f, 273f, seaLevel) * still;

            float frontal = 0.55f * Noise.Smooth(0.7f, 0.86f, b.M) * Noise.Smooth(289f, 279f, b.T0);

#if DEV
            FogParts = "land " + b.Land.ToString("F2") + " ground " + b.Ground.ToString("F0") + " M " + b.M.ToString("F2") + " T0 " + b.T0.ToString("F0") + " sea-level T " + seaLevel.ToString("F0") + " hour " + hour.ToString("F0") + " still " + still.ToString("F2") + " bank " + bank.ToString("F2") + " where " + where.ToString("F2")
                     + " | radiation " + radiation.ToString("F2") + " advection " + advection.ToString("F2") + " upslope " + upslope.ToString("F2") + " steam " + steam.ToString("F2") + " frontal " + frontal.ToString("F2");
#endif
            // (the thickest of them is the fog there)
            float most = 0f, deep = 0f;
            FogKind kind = FogKind.None;
            Thicker(radiation * c.Fogs, FogKind.Radiation, 20f + 110f * radiation, ref most, ref kind, ref deep);
            Thicker(advection * c.Fogs, FogKind.Advection, 120f + 330f * advection, ref most, ref kind, ref deep);
            Thicker(upslope * c.Fogs, FogKind.Upslope, 350f, ref most, ref kind, ref deep);
            Thicker(steam * c.Fogs, FogKind.Steam, 5f + 14f * steam, ref most, ref kind, ref deep);
            Thicker(frontal * c.Fogs, FogKind.Frontal, 420f, ref most, ref kind, ref deep);
            b.Fog = most; b.FogOf = kind; b.FogDeep = deep;
#if DEV
            if (Probe.FogHeld) { b.Fog = Probe.FogAmount; b.FogOf = Probe.FogKindHeld; b.FogDeep = Probe.FogDeepHeld; }
#endif
        }

#if DEV
        [ThreadStatic] public static string FogParts;
#endif

        static void Thicker(float amount, FogKind kind, float deep, ref float most, ref FogKind of, ref float deepest)
        {
            amount = Noise.Clamp01(amount);
            if (amount > most) { most = amount; of = kind; deepest = deep; }
        }

        // ---------------------------------------------------------------- storms and showers

        /// <summary>Whether a weather with storms in it is held (a storm, a hailstorm): then even a world that has none of its own has them.</summary>
        static bool StormsHeld => Chosen == 6 || Chosen == 13 || Chosen == HeldBurst || Chosen == HeldTornado || Chosen == HeldSquall;

        const double CellBox = 22000.0;       // the spacing of the places storms can be born
        const double CellCycle = 6000.0;      // each place has one chance a cycle (two lattices, half a cycle apart)

        /// <summary>The storms and showers within `radius` metres of a place.</summary>
        public static int Cells(World w, double lat, double lon, double radius, double ut, List<WeatherCell> into)
        {
            if (w.Climate.Storms <= 0f && !StormsHeld) return 0;
            int found = 0;
            double cycle = CellCycle / (Pace <= 0.05f ? 0.05f : Pace);
            for (int lattice = 0; lattice < 2; lattice++)
            {
                Cycle(w, lattice, ut, cycle, out double since, out uint seed);
                float drift = BandDrift(w, lat) * 0.8f;
                double cosLat = Math.Max(0.2, Math.Cos(lat * Deg));
                double lonD = lon - drift * since / (w.Radius * cosLat) / Deg;      // where the place is in the frame the cells are born in
                double la = lat * Deg, lo = lonD * Deg, cl = Math.Cos(la);
                double px = w.Radius * cl * Math.Cos(lo) / CellBox, py = w.Radius * cl * Math.Sin(lo) / CellBox, pz = w.Radius * Math.Sin(la) / CellBox;
                int reach = (int)Math.Ceiling((radius + 9000.0) / CellBox) + 1;
                int ix = Noise.Floor(px), iy = Noise.Floor(py), iz = Noise.Floor(pz);
                double rr = w.Radius / CellBox;
                for (int a = -reach; a <= reach; a++)
                    for (int bb = -reach; bb <= reach; bb++)
                        for (int cc = -reach; cc <= reach; cc++)
                        {
                            if (!Born(w, ix + a, iy + bb, iz + cc, seed, since, cycle, rr, out uint h, out float through, out double cLat, out double cLon, out float cDrift)) continue;
                            // how far from the place
                            double dist = Distance(w, lat, lon, cLat, cLon);
                            if (dist > radius + 9000.0) continue;
                            if (!Grown(w, h, through, cLat, cLon, cDrift, ut, out WeatherCell cell)) continue;
                            if (dist > radius + cell.Radius) continue;
                            into.Add(cell);
                            found++;
                        }
            }
            return found + Violent.SquallCells(w, lat, lon, radius, ut, into);
        }

        /// <summary>
        /// (Since 0.6.0, for the weather centre's map of the whole world.) Every storm and shower on the world, as Cells finds those
        /// near a place: the boxes of both lattices the world's surface passes through, all of them. (Cells asked for the whole
        /// world from one place walks a cube as wide as the world's half way round, three times the world itself: 2.8 s on Kerbin.)
        /// </summary>
        public static int AllCells(World w, double ut, List<WeatherCell> into)
        {
            if (w.Climate.Storms <= 0f && !StormsHeld) return 0;
            int found = 0;
            double cycle = CellCycle / (Pace <= 0.05f ? 0.05f : Pace);
            double rr = w.Radius / CellBox;
            int reach = (int)Math.Ceiling(rr) + 2;
            for (int lattice = 0; lattice < 2; lattice++)
            {
                Cycle(w, lattice, ut, cycle, out double since, out uint seed);
                for (int a = -reach; a <= reach; a++)
                    for (int bb = -reach; bb <= reach; bb++)
                        for (int cc = -reach; cc <= reach; cc++)
                        {
                            if (!Born(w, a, bb, cc, seed, since, cycle, rr, out uint h, out float through, out double cLat, out double cLon, out float cDrift)) continue;
                            if (!Grown(w, h, through, cLat, cLon, cDrift, ut, out WeatherCell cell)) continue;
                            into.Add(cell);
                            found++;
                        }
            }
            return found + Violent.SquallCells(w, 0.0, 0.0, Math.PI * w.Radius, ut, into);
        }

        /// <summary>One of the two lattices storms are born on, at a moment: how long its cycle has run, and the cycle's own number in its seed.</summary>
        static void Cycle(World w, int lattice, double ut, double cycle, out double since, out uint seed)
        {
            double shifted = ut + lattice * cycle * 0.5;
            int c = Noise.Floor(shifted / cycle);
            since = shifted - c * cycle;            // seconds since this lattice's cycle began
            seed = w.Seed ^ (uint)(lattice * 0x51ed27u) ^ (uint)c * 0x2c1b3c6du;
        }

        /// <summary>Whether a box of a lattice has a storm alive in it now, and if so where the storm is and how far through its life (cheap: no weather is asked).</summary>
        static bool Born(World w, int x, int y, int z, uint seed, double since, double cycle, double rr, out uint h, out float through, out double cLat, out double cLon, out float cDrift)
        {
            h = 0u; through = 0f; cLat = 0.0; cLon = 0.0; cDrift = 0f;
            // (only the boxes the planet's surface passes through)
            double cx = x + 0.5, cy = y + 0.5, cz = z + 0.5, cd = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            if (Math.Abs(cd - rr) > 1.0) return false;
            h = Noise.Hash(x, y, z, seed);
            if (Noise.Unit(h) > 0.5f) return false;                       // half of them have a chance at all
            float born = 0.45f * Noise.Unit(Noise.Hash(h ^ 0x1234567u));
            float life = 0.5f + 0.1f * Noise.Unit(Noise.Hash(h ^ 0x89abcdu));
            float age = (float)(since / cycle) - born;
            if (age < 0f || age > life) return false;
            through = age / life;
            // its place: a random point in the box, put on the surface
            double qx = x + Noise.Unit(Noise.Hash(h ^ 0x9e3779b9U)), qy = y + Noise.Unit(Noise.Hash(h ^ 0x7f4a7c15U)), qz = z + Noise.Unit(Noise.Hash(h ^ 0x94d049bbU));
            double ql = Math.Sqrt(qx * qx + qy * qy + qz * qz);
            cLat = Math.Asin(qz / ql) / Deg;
            double cLonD = Math.Atan2(qy, qx) / Deg;
            double cCos = Math.Max(0.2, Math.Cos(cLat * Deg));
            cDrift = BandDrift(w, cLat) * 0.8f;
            cLon = cLonD + cDrift * since / (w.Radius * cCos) / Deg;
            return true;
        }

        /// <summary>The storm of a box that has one (see Born), as the weather where it stands makes it: false if that leaves nothing of it.</summary>
        static bool Grown(World w, uint h, float through, double cLat, double cLon, float cDrift, double ut, out WeatherCell cell)
        {
            cell = default(WeatherCell);
            Base cb = Sample(w, cLat, cLon, ut);
            float potential = cb.Inst * Noise.Smooth(0.32f, 0.68f, cb.M) + 0.55f * cb.Front * Noise.Smooth(0.5f, 0.7f, cb.M);
            potential = potential * w.Climate.Storms * (0.75f + 0.5f * Noise.Unit(Noise.Hash(h ^ 0xfeedu))) + cb.StormBias;
            float most = Noise.Smooth(0.28f, 0.7f, potential);
            if (most <= 0.02f) return false;
            float shape = Noise.Smooth(0f, 0.3f, through) * (1f - Noise.Smooth(0.65f, 1f, through));
            float strength = most * shape;
            if (strength <= 0.02f) return false;
            // (a storm's cloud 3 to 17 km in radius as it is strong: until 2026-10-18 at most 8, its anvil and the heaps round it left out)
            float radiusM = (float)(3000.0 + 8000.0 * most * (0.6f + 0.8f * Noise.Unit(Noise.Hash(h ^ 0xabcu))));
            bool thunder = w.Climate.Lightning > 0f && potential > 0.5f && Noise.Unit(Noise.Hash(h ^ 0x777u)) < 0.85f;
            float baseH = cb.Ground + CumulusBase(cb);
            float top = baseH + 1500f + (thunder ? 1f : 0.55f) * most * Math.Max(0f, w.Climate.Tops - baseH - 1500f);
            cell = new WeatherCell
            {
                Id = (int)(h & 0x7fffffff), Latitude = cLat, Longitude = cLon, Radius = radiusM, Base = baseH, Top = top, Life = through,
                Strength = strength, Rain = Noise.Clamp01((float)Math.Pow(strength, 1.2) * (thunder ? 1f : 0.65f) * w.Climate.Water),
                Thunder = thunder, Drift = new Vector3d(cDrift, 0.0, 0.0),
            };
            return true;
        }

        /// <summary>
        /// The weather of a whole world at one moment, for what asks at very many places at once (the clouds' map of the planet,
        /// 32,768 places): the clouds and what falls at each, as Layers gives them, without the wind. What costs most in the
        /// weather at one place is finding the storms about it (some seven hundred boxes of the lattice looked into, and the
        /// weather asked where each storm stands, for every place); here every storm on the world is found once, sorted into
        /// squares of latitude and longitude, and each place looks only into the squares round it. On a world too large for
        /// that (Jool: a million boxes) each place finds its own, as before. One thread at a time.
        /// </summary>
        public sealed class Atlas
        {
            struct Kept { public WeatherCell Cell; public int Order; }

            readonly World w;
            readonly double ut;
            readonly List<Kept>[] squares;             // (null: each place finds its own storms)
            readonly int rows, columns;
            readonly double square;                    // degrees to a square's side
            readonly List<Kept> found = new List<Kept>();
            readonly List<WeatherCell> near = new List<WeatherCell>();
            /// <summary>How many storms and showers the world has at this moment (-1: not counted, the world is too large).</summary>
            public readonly int Storms = -1;

            public Atlas(World world, double moment)
            {
                w = world; ut = moment;
                double rr = w.Radius / CellBox;
                if (w.Climate.Storms <= 0f && !StormsHeld) { Storms = 0; return; }
                if (rr > 64.0) return;
                // (squares as wide as a storm can reach: Cells takes those within 25 km and their own radius, or 9 km if that is less)
                square = Math.Max(1.0, 36000.0 / (w.Radius * Deg));
                rows = Math.Max(1, (int)Math.Ceiling(180.0 / square));
                columns = Math.Max(1, (int)Math.Ceiling(360.0 / square));
                squares = new List<Kept>[rows * columns];
                double cycle = CellCycle / (Pace <= 0.05f ? 0.05f : Pace);
                int n = (int)Math.Ceiling(rr) + 2;
                Storms = 0;
                // (the boxes gone through in the order Cells goes through them, and each storm's turn kept: where two storms
                // overlap, which of them names the cloud depends on the order they come in)
                for (int lattice = 0; lattice < 2; lattice++)
                {
                    Cycle(w, lattice, ut, cycle, out double since, out uint seed);
                    for (int x = -n; x <= n; x++)
                        for (int y = -n; y <= n; y++)
                            for (int z = -n; z <= n; z++)
                            {
                                if (!Born(w, x, y, z, seed, since, cycle, rr, out uint h, out float through, out double cLat, out double cLon, out float cDrift)) continue;
                                if (!Grown(w, h, through, cLat, cLon, cDrift, ut, out WeatherCell cell)) continue;
                                int at = Row(cell.Latitude) * columns + Column(cell.Longitude);
                                if (squares[at] == null) squares[at] = new List<Kept>();
                                squares[at].Add(new Kept { Cell = cell, Order = Storms });
                                Storms++;
                            }
                }
                // (and the storms of the squall lines, after the others, as Cells has them)
                var inRows = new List<WeatherCell>();
                Violent.SquallCells(w, 0.0, 0.0, 1e9, ut, inRows);
                for (int i = 0; i < inRows.Count; i++)
                {
                    int at = Row(inRows[i].Latitude) * columns + Column(inRows[i].Longitude);
                    if (squares[at] == null) squares[at] = new List<Kept>();
                    squares[at].Add(new Kept { Cell = inRows[i], Order = Storms });
                    Storms++;
                }
            }

            int Row(double lat) { int r = (int)((lat + 90.0) / square); return r < 0 ? 0 : r >= rows ? rows - 1 : r; }
            int Column(double lon) { int c = Noise.Floor((lon + 180.0) / square) % columns; return c < 0 ? c + columns : c; }

            /// <summary>The clouds and what falls at a place (no wind): what Here gives for them, to the last figure.</summary>
            public WeatherHere At(double lat, double lon)
            {
                Base b = Sample(w, lat, lon, ut);
                near.Clear();
                if (squares == null) { if (Storms != 0) Cells(w, lat, lon, 25000.0, ut, near); }
                else if (Storms > 0)
                {
                    // (the storms Cells finds within 25 km of the place: from the squares round it, as many to each side as 36 km of
                    // longitude come to in the row nearer the pole, and all of them near a pole)
                    double perDegree = w.Radius * Deg, cosLat = Math.Cos(Math.Min(90.0, Math.Abs(lat) + square) * Deg);
                    int row = Row(lat), column = Column(lon);
                    int aside = cosLat > 0.05 ? (int)Math.Ceiling(1.0 / cosLat) : columns;
                    bool round = 2 * aside + 1 >= columns;       // (all the way round the world: every square of the row, once)
                    int first = round ? 0 : column - aside, last = round ? columns - 1 : column + aside;
                    found.Clear();
                    for (int r = Math.Max(0, row - 1); r <= Math.Min(rows - 1, row + 1); r++)
                        for (int k = first; k <= last; k++)
                        {
                            List<Kept> held = squares[r * columns + (k % columns + columns) % columns];
                            if (held == null) continue;
                            for (int i = 0; i < held.Count; i++)
                            {
                                WeatherCell cell = held[i].Cell;
                                double reach = 25000.0 + Math.Min(9000.0, cell.Radius);
                                double dLat = (cell.Latitude - lat) * perDegree;
                                if (dLat > reach + 100.0 || dLat < -reach - 100.0) continue;
                                if (Distance(w, lat, lon, cell.Latitude, cell.Longitude) > reach) continue;
                                // (kept in their turn: a few at most)
                                int at = found.Count;
                                while (at > 0 && found[at - 1].Order > held[i].Order) at--;
                                found.Insert(at, held[i]);
                            }
                        }
                    for (int i = 0; i < found.Count; i++) near.Add(found[i].Cell);
                }
                return Build(w, lat, lon, ut, ref b, near);
            }
        }

        /// <summary>
        /// A storm's hail at a place: how thickly it falls there now, 0 to 1, and the size of the stones (mm).
        ///
        /// Hail is ice that a storm's rising air holds up in cloud far colder than freezing while water freezes on to it,
        /// until it is too heavy to hold or is thrown clear. So it wants a thunderstorm at its strongest (the stronger the
        /// air rises, the larger the stone it can carry: pea size at 15 m/s, golf ball at 40), with several kilometres of
        /// its cloud above the freezing level; it comes from about one storm in three or four, falls for five to ten
        /// minutes of the storm's life, in a shaft a kilometre or two across beside the storm's middle, not all over it;
        /// and small stones that fall a long way through warm air melt before they land (rain with a cold splash).
        /// Worked out from the storm's own number, like everything else of it: the same for everyone, every time.
        /// </summary>
        public static float Hail(World w, WeatherCell cell, Base b, double lat, double lon, out float size)
        {
            size = 0f;
            if (!cell.Thunder || w.Climate.Water <= 0f) return 0f;
            uint h = (uint)cell.Id;
            float deepCold = (cell.Top - Math.Max(b.Freeze, cell.Base)) / 1000f;            // km of the cloud above freezing
            float able = Noise.Smooth(0.45f, 0.8f, cell.Strength) * Noise.Smooth(3f, 5.5f, deepCold);
            if (able <= 0.01f) return 0f;
            // (about one strong storm in three; every one, where a hailstorm is held)
            if (Noise.Unit(Noise.Hash(h ^ 0x4a11c0deu)) > 0.32f + b.HailBias) return 0f;
            // (its minutes: a stretch of the storm's life about its height)
            float mid = 0.4f + 0.2f * Noise.Unit(Noise.Hash(h ^ 0x51ee7u));
            float now = Noise.Bump(cell.Life, mid, 0.07f + 0.04f * b.HailBias);
            if (now <= 0.01f) return 0f;
            // (its shaft: a third of the way out from the storm's middle, in a direction of its own, as wide as a fifth to a third of the storm)
            double bearing = 6.2831853 * Noise.Unit(Noise.Hash(h ^ 0x0ddba11u));
            double off = cell.Radius * 0.3;
            double sLat = cell.Latitude + off * Math.Cos(bearing) / w.Radius / Deg;
            double sLon = cell.Longitude + off * Math.Sin(bearing) / (w.Radius * Math.Max(0.2, Math.Cos(cell.Latitude * Deg))) / Deg;
            float shaft = cell.Radius * (0.2f + 0.13f * Noise.Unit(Noise.Hash(h ^ 0x5ca1eu)));
            float inside = 1f - (float)(Distance(w, lat, lon, sLat, sLon) / shaft);
            if (inside <= 0f) return 0f;
            // (the stones: from 6 mm to 50, mostly small; the largest only from the strongest storms)
            float u = Noise.Unit(Noise.Hash(h ^ 0x57011e5u));
            size = 6f + 44f * u * u * Noise.Smooth(0.5f, 1f, cell.Strength) + 6f * b.HailBias;
            // (melting on the way down: a small stone loses about 3 mm for each kilometre of air above freezing it falls through)
            size -= 3f * Math.Max(0f, b.Freeze - b.Ground) / 1000f * Noise.Smooth(30f, 10f, size);
            if (size < 4f) { size = 0f; return 0f; }
            return Noise.Clamp01(able * now * Noise.Smooth(0f, 0.5f, inside) * 1.2f);
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

        /// <summary>(For the weather centre: the height air from the ground would be lifted to before its water condenses, m above the ground: the dew point from it.)</summary>
        internal static float LiftedBase(Base b) => CumulusBase(b);

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
            Region r = MakeRegion(w, a, bb, t, box, ut);
            RegionAhead(w, a, bb, t + 1, box);
            return r.Cells;
        }

        static Region MakeRegion(World w, int a, int bb, long t, double box, double ut)
        {
            var r = new Region { W = w, A = a, B = bb, T = t };
            double reach = box * 0.75 * Math.PI / 180.0 * w.Radius + 25000.0;
            Cells(w, (a + 0.5) * box, (bb + 0.5) * box, reach, ut, r.Cells);
            lock (cacheLock) { regions[regionNext] = r; regionNext = (regionNext + 1) % regions.Length; }
            return r;
        }

        // (Since 0.5.0: the next stretch of the game's time made ahead on another thread, for a place's region (ten seconds) and its
        // column (two), so that the frame the clock comes to it in finds it made. Made on the game's thread when first asked for,
        // each new stretch cost whoever asked first 3 to 15 ms: the lightning every ten seconds, the wind on the craft and the
        // light under the clouds every two, a hitch each time, and under time warp every few frames (2026-10-20).)
        static readonly HashSet<string> makingAhead = new HashSet<string>();

        static bool StartAhead(string key)
        {
            lock (cacheLock)
            {
                if (makingAhead.Contains(key) || makingAhead.Count >= 6) return false;
                makingAhead.Add(key);
                return true;
            }
        }

        static void RegionAhead(World w, int a, int bb, long t, double box)
        {
            lock (cacheLock)
                foreach (Region one in regions)
                    if (one != null && one.W == w && one.A == a && one.B == bb && one.T == t) return;
            string key = "r " + w.Name + " " + a + " " + bb + " " + t;
            if (!StartAhead(key)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { MakeRegion(w, a, bb, t, box, t * 10.0); }
                catch (Exception) { }
                finally { lock (cacheLock) makingAhead.Remove(key); }
            });
        }

        static void ColumnAhead(World w, double qLat, double qLon, double qUt)
        {
            lock (cacheLock)
                for (int n = 0; n < cache.Length; n++)
                {
                    Column one = cache[n];
                    if (one != null && one.W == w && one.Lat == qLat && one.Lon == qLon && one.Ut == qUt) return;
                }
            string key = "c " + w.Name + " " + qLat + " " + qLon + " " + qUt;
            if (!StartAhead(key)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Column col = Make(w, qLat, qLon, qUt);
                    lock (cacheLock) { cache[cacheNext] = col; cacheNext = (cacheNext + 1) % cache.Length; }
                }
                catch (Exception) { }
                finally { lock (cacheLock) makingAhead.Remove(key); }
            });
        }

        /// <summary>
        /// The usual sky's low cloud: what the lowest kilometre or two holds on most days, wet system or none. Over the sea there
        /// is low cloud nine times in ten, and the commonest kind on Earth is the flat one: stratocumulus covers 22 % of the
        /// oceans' sky averaged over the year, cumulus 13 %, stratus 13 %; over land 12, 5 and 5 % (Warren, Eastman and Hahn,
        /// "Clouds: climatology", 2014, from the observers' reports, table 3). Until 2026-10-18 a sky with no wet system over it
        /// was clear but for the sun's heaps, and Kerbin was under cloud 15 % of the time where Earth is 64 % (Probe.Climate);
        /// the owner: "the clouds are way too tiny ... the atmosphere feels way too small".
        /// Over the sea, by how stable the lowest air is (Base.Stab): the heaps of the trade winds (a fifth to a third of the sky,
        /// bases about 600 m, tops wherever the lid is, deeper the less of one there is), heaps under a breaking sheet, then a
        /// deck, thinned by the afternoon's sun (it is thickest before dawn), in cold air a low grey sheet. Over land: by day
        /// the sun's heaps (bases 1 to 2 km, higher the drier); towards evening their tops spread into pads as the heaps under
        /// them die; by night clear, or a low sheet where the air is damp; nothing over dry land under a strong lid (deserts).
        /// </summary>
        static CloudLayer Usual(World w, double lat, double lon, double ut, ref Base b)
        {
            var l = new CloudLayer();
            Climate c = w.Climate;
            if (b.Usual <= 0.001f || c.Water <= 0f || c.Wet < 0.5f) return l;      // (worlds whose air holds no water to speak of: none)
            float a = (float)Math.Abs(lat), hour = (float)b.Hour;
            float sea = 1f - b.Land;
            // ---- the sea's
            // (Decks of stratocumulus lie over the cool sea off the west coasts in the subtropics (off Peru and Chile, south-west Africa,
            // California: hundreds of kilometres of cells, breaking into heaps further out), and over the cool seas of high latitudes;
            // over the open subtropical sea the air sinks clear but for small heaps. Until 2026-10-19 a deck lay wherever the lid was
            // strong, over every subtropical sea alike: from afar a belt of grey round the world.)
            // (how far out to sea a deck reaches goes from place to place and day to day: twice as far here as there, never a band)
            float reach = (float)Lows.Reach(w) * (0.55f + 0.9f * Noise.Clamp01(0.5f + 0.6f * Drifting(w, lat, lon, ut, 1.2 * Lows.Reach(w), 18.0 * 3600.0, 97u, 2, b.DriftE)));
            float eastLand = w.EastLand(lat, lon);
            float offWest = Noise.Smooth(1.8f * reach, 0.4f * reach, eastLand);
            float subtropic = Noise.Smooth(6f, 14f, a) * (1f - Noise.Smooth(38f, 46f, a));
            float deck = Noise.Smooth(0.42f, 0.75f, b.Stab) * (OldCover ? 1f : 1f - subtropic * (1f - offWest));
            // (the new cover: and only in some regions of a stable band, a few hundred kilometres each, as the real decks lie in their
            // own basins and come and go over days: the band's lid alone, strong from 20 to 35 degrees everywhere, made one deck the
            // band's whole length)
            if (!OldCover) deck *= Noise.Smooth(0.35f, 0.6f, 0.5f + 0.5f * Drifting(w, lat, lon, ut, 1.5 * Lows.Reach(w), 30.0 * 3600.0, 71u, 1, b.DriftE, Warp));
            float lifted = Noise.Clamp01((1f - b.Stab) * Noise.Smooth(0.25f, 0.7f, b.M));       // (how freely heaps grow)
#if DEV
            if (Probing) { PDeck = deck; POffWest = offWest; PSubtropic = subtropic; PReach = reach; PEast = eastLand; PLifted = lifted; }
#endif
            var s = new CloudLayer();
            if (deck >= 0.5f)
            {
                bool cold = b.T0 < 291f && b.Stab > 0.55f;                        // (a cool sea under a lid, in the middle and high latitudes: a low grey sheet)
                s.Kind = cold ? CloudKind.Stratus : CloudKind.Stratocumulus;
                s.Cover = (0.75f + 0.22f * Noise.Smooth(0.55f, 0.95f, b.Stab)) * (1f - 0.18f * b.Heat);
                // (the new cover: a deck is closed cells, nearly whole, by the coast and in its heart, and breaks into open cells and
                // then heaps towards its edges and further out to sea (the decks' westward break-up off California and Peru), so the
                // broken part gathers into clumps and its edge is ragged. Whole to its edges, as before, a deck ended along a line
                // of latitude: from afar a straight-edged stripe)
                if (!OldCover) s.Cover *= 0.45f + 0.55f * Noise.Smooth(0.5f, 0.9f, deck) * (subtropic > 0.01f ? Noise.Smooth(1.6f * reach, 0.3f * reach, eastLand) : 1f);
                s.Base = b.Ground + (cold ? 250f + 250f * (1f - b.M) : 380f + 450f * (1f - deck) + 300f * (1f - b.M));
                s.Top = s.Base + (cold ? 300f : 220f + 380f * Noise.Smooth(0.3f, 0.8f, b.M));
                s.Water = 0.15f;
            }
            else
            {
                s.Kind = CloudKind.Cumulus;
                // (heaps under a breaking sheet nearer a deck; few in air that has sunk dry: the clear blue sea between systems)
                s.Cover = (0.18f + 0.15f * lifted + 0.3f * deck) * (0.35f + 0.65f * Noise.Smooth(0.15f, 0.4f, b.M));
                s.Base = b.Ground + 450f + 350f * (1f - b.M);
                // (up to the trade winds' lid, 1.5 to 2.5 km up: heaps a kilometre or two deep and as wide again, deeper still in the
                // equator's rain belt; shallower under a strong lid. At 450 m deep in dry air, as first written, they were a field of
                // small flat puffs)
                s.Top = s.Base + (900f + 1300f * (1f - b.Stab) * (0.4f + 0.6f * Noise.Smooth(0.2f, 0.6f, b.M))) * (1f - 0.5f * deck) + 1600f * lifted * Noise.Bump(a, 0f, 12f);
                s.Water = 0.15f + 0.5f * Noise.Clamp01((s.Top - s.Base) / 3000f);
            }
            // ---- the land's
            var g = new CloudLayer();
            float desert = Noise.Smooth(0.55f, 0.8f, b.Stab) * Noise.Smooth(0.45f, 0.25f, b.M);
            float evening = Noise.Smooth(40f, 75f, hour) * (1f - Noise.Smooth(115f, 140f, hour));
            float byDay = Noise.Smooth(0.05f, 0.35f, b.Heat);
            float heaps = (0.15f + 0.35f * Noise.Smooth(0.15f, 0.6f, b.M)) * byDay * (1f - 0.6f * evening);
            float pads = (0.3f + 0.4f * Noise.Smooth(0.3f, 0.7f, b.M)) * evening;
            // (a sheet by night where the air is damp, and by day too under a strong lid, where the sun cannot break it up: the grey
            // days of winter in the middle latitudes)
            float sheet = Math.Max(Noise.Smooth(0.38f, 0.7f, b.M) * 0.8f * (1f - byDay), 0.7f * Noise.Smooth(0.5f, 0.8f, b.Stab) * Noise.Smooth(0.3f, 0.6f, b.M) * Noise.Smooth(25f, 45f, a));
            float landBase = b.Ground + 700f + 800f * (1f - b.M);
            if (heaps >= pads && heaps >= sheet)
            {
                float unsettled = b.Heat * Noise.Smooth(0.35f, 0.75f, b.M);
                g.Kind = CloudKind.Cumulus; g.Cover = heaps; g.Base = landBase;
                g.Top = g.Base + 400f + 900f * b.Heat * Noise.Smooth(0.15f, 0.5f, b.M) + 1500f * unsettled + 1800f * Noise.Smooth(0.5f, 0.85f, unsettled);
                g.Water = 0.15f + 0.5f * Noise.Clamp01((g.Top - g.Base) / 3000f);
            }
            else if (pads >= sheet)
            {
                g.Kind = CloudKind.Stratocumulus; g.Cover = pads; g.Base = landBase - 100f; g.Top = g.Base + 250f + 250f * b.M; g.Water = 0.1f;
            }
            else
            {
                g.Kind = b.M > 0.62f ? CloudKind.Stratus : CloudKind.Stratocumulus; g.Cover = sheet;
                g.Base = b.Ground + 350f + 500f * (1f - b.M); g.Top = g.Base + 300f + 300f * b.M; g.Water = 0.15f;
            }
            g.Cover *= 1f - desert;
            // ---- by how much of the place is sea (on a coast, the larger of the two)
            l = sea >= 0.5f ? s : g;
            l.Cover = Math.Max(s.Cover * sea, g.Cover * (1f - sea)) * b.Usual * Noise.Clamp01(c.Wet);
            if (l.Cover < 0.01f) { l.Kind = CloudKind.None; l.Cover = 0f; }
            return l;
        }

        /// <summary>The dappled sky's cover: altocumulus in fields a few hundred kilometres across wherever the air aloft is
        /// damp enough, more of it towards a wet system: 18 % of the oceans' sky and 17 % of the land's, the observers' commonest
        /// kind after stratocumulus (as Usual). Until 2026-10-18 the middle sky had cloud only in a wet system: 2 % of Kerbin's.</summary>
        static float Dapple(World w, double lat, double lon, double ut, ref Base b)
        {
            if (b.Usual <= 0.001f || w.Climate.Wet < 0.5f) return 0f;
            float field = Drifting(w, lat, lon, ut, 240000.0, 4.0 * 3600.0, 61u, 3, b.DriftE * 1.5f, Warp);
            // (in patches with edges, not a thin dapple everywhere: seen from far off, cloud spread thinly over the whole world was a
            // planet of grey fleece, where Earth's is gathered into systems with the blue sea between them. Some 12 % of the sky, not
            // the observers' 18 %, while the shader draws its elements as small shaded puffs: a sky full of them reads as tiny)
            return Noise.Smooth(0.1f, 0.35f, field + 0.8f * (b.M - 0.42f)) * 0.75f * b.Usual * b.Aloft;
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
                // (How deep the heaps are, and how much of the sky they take. On the usual fair day the air stops rising at
                // a lid a few hundred metres above where it first condenses: flat heaps, wider than tall, a quarter of the
                // sky. Only where the air is both unstable and damp is there no lid to speak of, and the heaps tower and
                // crowd. As it first was, a fair day's heaps were 900 m deep over half the sky: the showery day every day,
                // and the user: "usually the clouds aren't huge bulbs like they are now". See Kinds-of-cloud.md.)
                float unsettled = b.Inst * b.M;
                here.Low.Kind = CloudKind.Cumulus;
                here.Low.Cover = cu * (0.5f + 0.5f * Noise.Smooth(0.3f, 0.7f, unsettled));
                here.Low.Base = b.Ground + CumulusBase(b);
                here.Low.Top = here.Low.Base + 220f + 900f * unsettled + 2400f * Noise.Smooth(0.42f, 0.8f, unsettled);
                here.Low.Water = 0.15f + 0.5f * Noise.Clamp01((here.Low.Top - here.Low.Base) / 3000f);
            }
            if (here.Low.Cover < 0.01f) { here.Low.Kind = CloudKind.None; here.Low.Cover = 0f; }
            // ---- the usual sky's own low cloud, where the weather's is less (see Usual)
            if (here.Low.Kind != CloudKind.Nimbostratus)
            {
                CloudLayer usual = Usual(w, lat, lon, ut, ref b);
                if (usual.Cover > here.Low.Cover) here.Low = usual;
            }
            // ---- (the new cover) a broken sky's heaps gathered into clumps, and the clumps into fields, with clear sky between
            if (!OldCover && (here.Low.Kind == CloudKind.Cumulus || here.Low.Kind == CloudKind.Stratocumulus) && here.Low.Cover < 0.85f)
                here.Low.Cover = Math.Min(0.95f, here.Low.Cover * Clumps(w, lat, lon, ut, b.DriftE));
            here.Low.Cover *= c.Low;
            if (here.Low.Cover < 0.01f) { here.Low.Kind = CloudKind.None; here.Low.Cover = 0f; }
            // ---- the middle and high layers
            float mid = Noise.Smooth(0.5f, 0.76f, b.M) * 0.9f;
            here.Middle.Kind = mid > 0.55f ? CloudKind.Altostratus : CloudKind.Altocumulus;
            here.Middle.Cover = mid;
            here.Middle.Base = Math.Max(b.Ground + 1800f, 3000f * tops);
            here.Middle.Top = here.Middle.Base + 1200f + 1800f * wet;
            here.Middle.Water = 0.3f * wet;
            if (mid < 0.01f) { here.Middle.Kind = CloudKind.None; here.Middle.Cover = 0f; }
            float dapple = Dapple(w, lat, lon, ut, ref b);
            if (dapple > here.Middle.Cover)
            {
                // (higher in the tropics, where the whole of the weather stands higher: some 5 km over the equator, 2.6 km from 35 degrees)
                // (where it is thick and the air damp, the grey sheet of altostratus: a wet system's coming)
                bool sheetAloft = dapple > 0.6f && b.M > 0.68f;
                here.Middle.Kind = sheetAloft ? CloudKind.Altostratus : CloudKind.Altocumulus; here.Middle.Cover = dapple;
                here.Middle.Base = Math.Max(b.Ground + 2000f, (2600f + 2200f * Noise.Bump((float)Math.Abs(lat), 0f, 35f)) * tops);
                here.Middle.Top = here.Middle.Base + (sheetAloft ? 900f + 1200f * wet : 300f + 500f * dapple); here.Middle.Water = sheetAloft ? 0.2f : 0.1f;
            }
            float cirrus = Noise.Smooth(0.4f, 0.62f, b.M) * 0.55f + 0.3f * Noise.Clamp01(Drifting(w, lat, lon, ut, 120000.0, 2.0 * 3600.0, 31u, 2, b.DriftE * 2f, 1.5f * Warp) * 1.5f - 0.2f);
            // (more over land, 22 % of its sky against 12 % of the sea's (the observers' table), and over the equator's rain belt,
            // where the storms' anvils spread into it)
            cirrus += (0.14f * b.Land + 0.15f * Noise.Bump((float)Math.Abs(lat), 0f, 14f)) * b.Usual * Noise.Smooth(0.15f, 0.45f, b.M);
            here.Middle.Cover *= c.Middle;
            if (here.Middle.Cover < 0.01f) { here.Middle.Kind = CloudKind.None; here.Middle.Cover = 0f; }
            cirrus *= c.High;
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
                // (and under it a low sheet of the world's own, where it has one: Eve's 1.5 to 3.5 km over half the sky at least,
                // Jool's from the bottom of its air everywhere. Until 0.5.0 by their names: Real Worlds' Venus, in Eve's place, had
                // Eve's low sheet under its own deck 48 to 70 km up)
                if (c.UnderDeck > 0f && (c.UnderDeck >= 1f || here.Low.Cover < c.UnderDeck))
                {
                    here.Low.Kind = CloudKind.Stratus; here.Low.Cover = c.UnderDeck; here.Low.Base = c.UnderDeckBase; here.Low.Top = c.UnderDeckTop; here.Low.Water = c.UnderDeckWater;
                }
            }
            // ---- a cyclone: its wall and bands are storm cloud to the top of the weather, the rest of it rain cloud; over all of it
            // a canopy of ice; its eye open to the sky, with a little low cloud on its floor
            if (b.Cyc > 0.01f)
            {
                float storms = Math.Max(b.Wall, 0.85f * b.Band) * b.Cyc * (1f - b.Eye);
                if (storms > 0.25f)
                {
                    here.Low.Kind = CloudKind.Cumulonimbus; here.Low.Cover = Math.Max(here.Low.Cover, 0.6f + 0.4f * storms);
                    here.Low.Base = b.Ground + 350f; here.Low.Top = Math.Max(here.Low.Top, b.Ground + 2500f + (c.Tops * 0.93f - 2500f) * storms); here.Low.Water = 1f;
                }
                else if (b.Eye < 0.5f && b.Cyc > 0.85f)
                {
                    here.Low.Kind = CloudKind.Nimbostratus; here.Low.Cover = Math.Max(here.Low.Cover, 0.95f * b.Cyc);
                    here.Low.Base = b.Ground + 400f; here.Low.Top = Math.Max(here.Low.Top, here.Low.Base + 3500f); here.Low.Water = 0.8f;
                }
                else if (b.Eye < 0.5f)
                {
                    // (outside its canopy: heaps, more of them towards it. Not rain cloud thinned out: a sheet kilometres
                    // thick that covers half the sky is, seen from above, a tableland cut by canyons.)
                    here.Low.Kind = CloudKind.Cumulus; here.Low.Cover = Math.Max(here.Low.Cover * (1f - b.Cyc), 0.15f + 0.3f * b.Cyc);
                    here.Low.Base = b.Ground + 450f; here.Low.Top = here.Low.Base + 650f + 650f * b.Band; here.Low.Water = 0.25f * b.Band;
                }
                if (b.Eye > 0.01f)
                {
                    here.Low.Cover *= 1f - 0.75f * b.Eye; here.Middle.Cover *= 1f - b.Eye;
                    if (b.Eye > 0.5f) { here.Low.Kind = CloudKind.Stratocumulus; here.Low.Base = b.Ground + 500f; here.Low.Top = here.Low.Base + 500f; here.Low.Water = 0.2f; }
                }
                // (a sheet of middle cloud under the canopy only)
                float under = Noise.Smooth(0.7f, 0.9f, b.Cyc);
                if (under > 0.01f) { here.Middle.Kind = CloudKind.Altostratus; here.Middle.Cover = Math.Max(here.Middle.Cover, 0.9f * under * (1f - b.Eye)); }
                else here.Middle.Cover *= 1f - b.Cyc;
                here.High.Kind = CloudKind.Cirrus; here.High.Cover = Math.Max(here.High.Cover, b.Cyc) * (1f - 0.85f * b.Eye);
            }
            // ---- what falls
            float rain = Noise.Smooth(0.72f, 0.96f, b.M) * 0.42f * c.Water;
            if (b.Cyc > 0.01f) rain = Math.Max(rain, (0.3f + 0.7f * Math.Max(b.Wall, 0.8f * b.Band)) * b.Cyc * (1f - b.Eye) * c.Water);
            if (b.Squall > 0.01f) rain = Math.Max(rain, 0.35f * b.Squall * c.Water);
            float storm = 0f, hail = 0f, hailSize = 0f;
            foreach (WeatherCell cell in cells)
            {
                double d = Distance(w, lat, lon, cell.Latitude, cell.Longitude);
                float falls = Hail(w, cell, b, lat, lon, out float stones);
                if (falls > hail) { hail = falls; hailSize = stones; }
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
            // ---- what has melted aloft and falls into air below freezing. Through a shallow cold layer (the ground a
            // few degrees under freezing: a few hundred metres of it) the drops have no time to freeze, reach the ground
            // as water colder than ice, and freeze on whatever they strike: glaze. Through a deep one (the ground five
            // degrees under and colder) they freeze on the way, and land as ice pellets that bounce. The rest that
            // falls in such cold is snow.
            float melted = b.Nose * Noise.Smooth(273.6f, 272.4f, b.T0);
            float shallow = Noise.Smooth(267f, 270.5f, b.T0);
            here.Freezing = melted * shallow;
            here.Pellets = melted * (1f - shallow);
            here.Snow *= 1f - melted;
            here.Hail = hail; here.HailSize = hailSize;
            here.Fog = b.Fog;
            // (what kind, how high its top, and whether it is colder than ice: fog that is, rimes whatever stands in it)
            if (b.Fog > 0.01f)
            {
                here.FogOf = b.FogOf == FogKind.None ? FogKind.Radiation : b.FogOf;
                here.FogDeep = b.FogDeep > 1f ? b.FogDeep : 60f + 240f * b.Fog;
                here.FogFreezes = b.T0 < 273.15f;
            }
            here.Dust = b.Dust;
            if (b.Dust > 0.01f && here.Low.Cover < b.Dust)
            {
                here.Low.Kind = CloudKind.Dust; here.Low.Cover = b.Dust; here.Low.Base = b.Ground; here.Low.Top = b.Ground + 1500f + 4000f * b.Dust; here.Low.Water = 0f;
            }
            if (b.Fog > 0.3f && here.Low.Cover < b.Fog)
            {
                here.Low.Kind = CloudKind.Fog; here.Low.Cover = b.Fog; here.Low.Base = b.Ground; here.Low.Top = b.Ground + here.FogDeep; here.Low.Water = 0.3f;
            }
            // ---- how far one can see
            float seeing = 40000f * (1f - 0.55f * Noise.Smooth(0.4f, 0.9f, b.M));
            if (here.Rain > 0.01f) seeing = Math.Min(seeing, 12000f / (1f + 14f * here.Rain * (1f + 1.5f * here.Snow)));
            // (steam fog is wisps on the water, not a wall: one sees through it; rain fog is never the thickest)
            float blinds = b.Fog * (b.FogOf == FogKind.Steam ? 0.45f : b.FogOf == FogKind.Frontal ? 0.8f : 1f);
            if (b.Fog > 0.01f) seeing = Math.Min(seeing, 150f + 20000f * (1f - blinds) * (1f - blinds) * (1f - blinds));
            // (a storm at its height leaves half a kilometre or so: a murk, with the nearest hills gone)
            if (b.Dust > 0.01f) seeing = Math.Min(seeing, 500f + 30000f * (1f - b.Dust) * (1f - b.Dust) * (1f - b.Dust));
            here.Seeing = seeing;
            here.Drift = new Vector3d(b.DriftE, b.DriftN, 0.0);
            here.Temperature = b.T0;
            Overrides.Shape(w, lat, lon, ref here);
#if DEV
            Probe.Shape(ref here);
#endif
            return here;
        }

        // ---------------------------------------------------------------- the wind

        /// <summary>How fast time is warped (set on the game's thread every frame by the weather's driver).</summary>
        internal static float WarpRate = 1f;
        static readonly object cacheLock = new object();
        static readonly Column[] cache = new Column[64];
        static int cacheNext;

        /// <summary>The weather and the wind over a place: kept for a moment (two seconds, half a kilometre), so that asking often costs little.</summary>
        public static Column ColumnAt(World w, double lat, double lon, double ut)
        {
            double qLat = Math.Round(lat * 20.0) / 20.0, qLon = Math.Round(lon * 20.0) / 20.0, qUt = Math.Floor(ut / 2.0) * 2.0;
            Column found = null, older = null;
            lock (cacheLock)
            {
                for (int n = 0; n < cache.Length; n++)
                {
                    Column one = cache[n];
                    if (one == null || one.W != w || one.Lat != qLat || one.Lon != qLon) continue;
                    if (one.Ut == qUt) { found = one; break; }
                    if (one.Ut < qUt && (older == null || one.Ut > older.Ut)) older = one;
                }
            }
            // (Since 0.5.0, under time warp, on the game's thread: the newest column made for the place, if it is no more than a
            // quarter of a second's worth of the warp old, while the one asked for is made on another thread. At x1000 every frame
            // is sixteen seconds of the game's time and so a new column for every place asked about, ten of them in a frame on
            // the game's thread, 12 to 28 ms (2026-10-20); the weather of a place moves little in that quarter of a second.)
            if (found == null && older != null && WarpRate > 4f && qUt - older.Ut <= WarpRate * 0.25 && Provider.OnMain)
            {
                ColumnAhead(w, qLat, qLon, qUt);
                return older;
            }
            if (found == null)
            {
                found = Make(w, qLat, qLon, qUt);
                lock (cacheLock)
                {
                    cache[cacheNext] = found;
                    cacheNext = (cacheNext + 1) % cache.Length;
                }
            }
            ColumnAhead(w, qLat, qLon, qUt + 2.0);
            return found;
        }

        /// <summary>Forget what is kept (when a setting has changed).</summary>
        public static void Forget() { lock (cacheLock) { for (int n = 0; n < cache.Length; n++) cache[n] = null; for (int n = 0; n < regions.Length; n++) regions[n] = null; } }

        static Column Make(World w, double lat, double lon, double ut)
        {
            var col = new Column { W = w, Lat = lat, Lon = lon, Ut = ut };
            Base b = Sample(w, lat, lon, ut);
            Cells(w, lat, lon, 25000.0, ut, col.Cells);
            Violent.Bursts(w, col.Cells, ut, col.Bursts);
            Violent.Twisters(w, col.Cells, ut, col.Twisters);
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
            float mE = Sample(w, lat, lon + dLon, ut).Low - Sample(w, lat, lon - dLon, ut).Low;
            float mN = Sample(w, lat + dLat, lon, ut).Low - Sample(w, lat - dLat, lon, ut).Low;
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
            // (Not so over the sea: water does not cool at night as land does, and the air over it stays stirred, night and
            // day; offshore the wind is often strongest at night. At sea the wind above comes down to the surface all but whole.)
            if (w.Body.ocean && b.Land < 0.98f) near += (Math.Max(near, 0.8f * c.Calm * b.Calm) - near) * (1f - b.Land);
            ue *= near; un *= near;
            // (a held weather blows as its kind of system does: holding it makes the sky alike everywhere round, and with that
            // the differences that drive a system's wind are gone; rain held at the pad came with a breath of wind)
            if (b.HeldBreeze > 0.1f)
            {
                float l = Noise.Sqrt(ue * ue + un * un), want = b.HeldBreeze * c.Calm * Math.Max(b.Calm, 0.2f);
                if (l < want)
                {
                    float ae = l > 0.3f ? ue / l : (b.DriftE >= 0f ? 1f : -1f), an = l > 0.3f ? un / l : 0.25f;
                    float n = Noise.Sqrt(ae * ae + an * an);
                    ue = ae / n * want; un = an / n * want;
                }
            }
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
            // ---- a cyclone's wind, and a squall line's (neither is held back near the ground: they are the weather's own)
            float cyclone = 0f;
            List<Cyclone> cyclones = Violent.Cyclones(w, ut);
            for (int i = 0; i < cyclones.Count; i++)
            {
                float share = Violent.CycloneWind(w, cyclones[i], lat, lon, out float ce, out float cn);
                if (share <= 0f) continue;
                ue += ce; un += cn; cyclone = Math.Max(cyclone, share);
            }
            List<Squall> squalls = Violent.Squalls(w, ut);
            for (int i = 0; i < squalls.Count; i++) Violent.SquallWind(w, squalls[i], lat, lon, ref ue, ref un, ref sigStorm);
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
            // (in a cyclone the wind high up is the cyclone's own, a third as strong as at the ground and dying away: there is
            // no jet stream over one, or it would be torn apart)
            float aloft = 1.2f - 0.85f * cyclone, jets = 1f - cyclone;
            col.JE = ((jetLat + c.Jet * 0.55f * meander) * jets + ue * aloft) * scale;
            col.JN = (c.Jet * 0.35f * meander2 * jets + un * aloft) * scale;
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
