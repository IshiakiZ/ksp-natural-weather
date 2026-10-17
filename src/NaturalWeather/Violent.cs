using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// A downburst: the air under a storm's rain, cooled by the rain evaporating into it and dragged down by its weight,
    /// falling out of the cloud at ten or twenty metres a second, striking the ground and spreading out along it in every
    /// direction. Under four kilometres across it is a microburst: it lasts five to fifteen minutes, its strongest wind blows
    /// along the ground just outside its middle, 50 to 100 m up, and an aircraft flying through it low meets a head wind, then
    /// sinking air, then a tail wind, within half a minute.
    /// </summary>
    struct Burst
    {
        public double Lat, Lon;
        public float Radius;      // of its core at the ground, m (the strongest wind blows a little outside it)
        public float Speed;       // the strongest wind it makes along the ground now, m/s
        public float Top;         // the cloud base it falls from, m above sea level
    }

    /// <summary>
    /// A tornado, or over water a waterspout: a column of air turning fast round an upright line from a storm's base to the
    /// ground, a few tens to a few hundred metres across where its wind is strongest. Air flows in to it along the ground,
    /// winds up round it and rises in it. A storm's own tornado comes from about one strong thunderstorm in ten, lasts some
    /// minutes, and turns the way the world does; a waterspout under a line of towering cumulus over warm sea is commoner,
    /// thinner and weaker, and may turn either way.
    /// </summary>
    public struct Twister
    {
        public int Id;
        public double Lat, Lon;
        public float Core;        // the radius at which its wind is strongest, m
        public float Speed;       // that wind now, m/s
        public float Base;        // the cloud base it hangs from, m above sea level
        public float Reach;       // how far down from the cloud its funnel shows as cloud, 0 to 1 (1: to the ground)
        public float Spin;        // +1 anticlockwise seen from above, -1 clockwise
        public bool Water;        // over water: a waterspout
        public float MoveE, MoveN; // how it moves, m/s
    }

    /// <summary>
    /// A tropical cyclone (a hurricane, when its wind passes 33 m/s): a whole weather system turning round a calm, clear
    /// eye. Round the eye a wall of the tallest storms and the strongest wind; outside it bands of storms in spirals,
    /// with steady rain between; over all of it a canopy of ice spreading outward at the top. Born over warm sea a few
    /// degrees from the equator, carried west by the trade winds and slowly towards the pole, it lives for days, and dies
    /// over land or cold water. Sized for a small world: an eye wall ten to fifteen kilometres from the middle.
    /// </summary>
    public sealed class Cyclone
    {
        public int Id;
        public double Lat, Lon;
        public float Rmw;         // the radius of its strongest wind (the eye wall), m
        public float Speed;       // that wind now, at 10 m, m/s
        public float Now;         // how strong it is against its own strongest, 0 to 1
        public float Spin;        // +1 anticlockwise (north of the equator), -1 clockwise
        public float MoveE, MoveN; // how it moves, m/s
    }

    /// <summary>A squall line: thunderstorms in a row a hundred kilometres long, moving broadside on, a wall of wind running ahead of them.</summary>
    public sealed class Squall
    {
        public int Id;
        public double Lat, Lon;   // its middle
        public float AlongE, AlongN;   // the way the row lies (a unit, east and north)
        public float MoveE, MoveN;     // how it moves, m/s (across itself)
        public float Length;      // m
        public float Now;         // how strong, 0 to 1
        public float Base;        // its clouds' base, m above sea level
    }

    /// <summary>
    /// The violent weather: downbursts, tornadoes and waterspouts (from the storms' own numbers: the same for everyone,
    /// every time), cyclones and squall lines (from the world's clock), and the wind each makes. Any thread.
    /// </summary>
    static class Violent
    {
        const double Deg = Math.PI / 180.0;

        // the space centre, for the weathers held to be looked at
        const double CentreLat = -0.0972, CentreLon = -74.5577;

        // ---------------------------------------------------------------- downbursts

        public static void Bursts(World w, List<WeatherCell> cells, double ut, List<Burst> into)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                WeatherCell cell = cells[i];
                if (cell.Rain < 0.25f && !cell.Thunder) continue;
                uint h = (uint)cell.Id;
                int count = cell.Thunder ? 1 + (int)(Noise.Unit(Noise.Hash(h ^ 0xd0b0u)) * 2.99f) : (Noise.Unit(Noise.Hash(h ^ 0xd0b0u)) < 0.35f ? 1 : 0);
                for (int k = 0; k < count; k++)
                {
                    uint hk = Noise.Hash(h ^ (uint)(k + 1) * 0x9e3779b1u);
                    float mid = 0.32f + 0.4f * Noise.Unit(hk);
                    // (two minutes coming down, two or three at its strongest, five dying away, of a storm's fifty-five)
                    float now = Noise.Smooth(mid - 0.07f, mid - 0.02f, cell.Life) * (1f - Noise.Smooth(mid + 0.03f, mid + 0.11f, cell.Life));
                    if (now < 0.02f) continue;
                    double bearing = 6.2831853 * Noise.Unit(Noise.Hash(hk ^ 0x1u));
                    double off = cell.Radius * (0.1 + 0.45 * Noise.Unit(Noise.Hash(hk ^ 0x2u)));
                    float u = Noise.Unit(Noise.Hash(hk ^ 0x3u));
                    into.Add(new Burst
                    {
                        Lat = cell.Latitude + off * Math.Cos(bearing) / w.Radius / Deg,
                        Lon = cell.Longitude + off * Math.Sin(bearing) / (w.Radius * Math.Max(0.2, Math.Cos(cell.Latitude * Deg))) / Deg,
                        Radius = 450f + 1300f * Noise.Unit(Noise.Hash(hk ^ 0x4u)),
                        Speed = (11f + 27f * u * u) * (0.5f + 0.5f * cell.Strength) * (cell.Thunder ? 1f : 0.7f) * now,
                        Top = cell.Base,
                    });
                }
            }
            // (held, to be flown through: one over the runway's eastern end, every six minutes)
            if (Model.Chosen == Model.HeldBurst)
            {
                double t = ut % 360.0;
                float now = Noise.Smooth(20f, 100f, (float)t) * (1f - Noise.Smooth(200f, 340f, (float)t));
                if (now > 0.02f) into.Add(new Burst { Lat = -0.050, Lon = -74.50, Radius = 900f, Speed = 28f * now * Model.ChosenWeight, Top = 1400f });
            }
        }

        /// <summary>The wind a downburst makes at a place and height (Oseguera and Bowles's rule, which the simulators that train pilots for it use): east, north, up, m/s.</summary>
        static void BurstWind(World w, Burst b, double lat, double lon, double alt, float agl, ref Vector3 v)
        {
            double d = Model.Distance(w, lat, lon, b.Lat, b.Lon);
            float R = b.Radius;
            if (d > 7.0 * R || b.Speed < 0.3f) return;
            // (how deep the outflow is: a quarter of the core's radius above a skin of 30 m in which the ground holds the air back)
            float zs = 0.25f * R, eps = 30f;
            float zm = zs * eps / (zs - eps) * (float)Math.Log(zs / eps);
            float most = (float)(Math.Exp(-zm / zs) - Math.Exp(-zm / eps));
            float lam = b.Speed / (0.3191f * R * most);
            float r = (float)Math.Max(d, 1.0), x = r / R;
            float z = Math.Max(agl, 0f);
            float shape = (float)(Math.Exp(-z / zs) - Math.Exp(-z / eps));
            float ur = lam * R * R / (2f * r) * (1f - (float)Math.Exp(-x * x)) * shape;
            float wz = -lam * (float)Math.Exp(-x * x) * (eps * ((float)Math.Exp(-z / eps) - 1f) - zs * ((float)Math.Exp(-z / zs) - 1f));
            // (it falls from the cloud's base: above that, none of it)
            wz *= 1f - Noise.Smooth(b.Top, b.Top + 1500f, (float)alt);
            Model.Toward(b.Lat, b.Lon, lat, lon, out float te, out float tn);
            v.x += ur * te; v.y += ur * tn; v.z += wz;
        }

        // ---------------------------------------------------------------- the air rising and sinking in a storm

        /// <summary>
        /// A storm's own up and down: the rising air that feeds it (a core a quarter of the way out from its middle, strongest
        /// half way up the cloud: 10 to 30 m/s in a thunderstorm, a few in a shower) while it grows and stands, and the sinking
        /// air in its rain, from the cloud's base to near the ground.
        /// </summary>
        static void CellDrafts(World w, WeatherCell cell, double lat, double lon, double alt, float agl, ref Vector3 v)
        {
            double d = Model.Distance(w, lat, lon, cell.Latitude, cell.Longitude);
            if (d > cell.Radius * 1.2) return;
            uint h = (uint)cell.Id;
            // ---- the rising core
            double bearing = 6.2831853 * Noise.Unit(Noise.Hash(h ^ 0x0ddba11u)) + Math.PI;     // (away from where its hail falls)
            double off = cell.Radius * 0.25;
            double cLat = cell.Latitude + off * Math.Cos(bearing) / w.Radius / Deg;
            double cLon = cell.Longitude + off * Math.Sin(bearing) / (w.Radius * Math.Max(0.2, Math.Cos(cell.Latitude * Deg))) / Deg;
            float core = cell.Radius * 0.22f;
            float dc = (float)Model.Distance(w, lat, lon, cLat, cLon) / core;
            float growing = 1f - Noise.Smooth(0.55f, 0.85f, cell.Life);
            float most = (cell.Thunder ? 8f + 22f * cell.Strength * cell.Strength : 2.5f + 6f * cell.Strength) * growing;
            float rel = ((float)alt - (cell.Base - 700f)) / Math.Max(500f, cell.Top - cell.Base + 700f);
            float tall = rel > 0f && rel < 1f ? 4f * rel * (1f - rel) : 0f;
            float up = most * (float)Math.Exp(-dc * dc) * tall;
            // ---- the sinking air in the rain: under and in the lower cloud, round the storm's middle
            float dr = (float)(d / (cell.Radius * 0.4));
            float down = (2f + 9f * cell.Rain) * Noise.Smooth(0.25f, 0.5f, cell.Life) * (float)Math.Exp(-dr * dr) * (1f - Noise.Smooth(cell.Base + 300f, cell.Base + 2500f, (float)alt));
            // (neither goes through the ground)
            v.z += (up - down) * Noise.Smooth(0f, 120f, agl);
        }

        // ---------------------------------------------------------------- tornadoes and waterspouts

        public static void Twisters(World w, List<WeatherCell> cells, double ut, List<Twister> into)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                WeatherCell cell = cells[i];
                if (cell.Strength < 0.3f) continue;
                uint h = (uint)cell.Id;
                bool water = w.Body.ocean && w.Land(cell.Latitude, cell.Longitude) < 0.3f;
                float chance = cell.Thunder && cell.Strength > 0.6f ? (water ? 0.25f : 0.1f) : !cell.Thunder && water ? 0.12f : 0f;
                if (chance <= 0f || Noise.Unit(Noise.Hash(h ^ 0x70a2ad0u)) > chance) continue;
                float mid = 0.42f + 0.2f * Noise.Unit(Noise.Hash(h ^ 0x71u));
                float half = 0.06f + 0.07f * Noise.Unit(Noise.Hash(h ^ 0x72u));
                float x = (cell.Life - mid) / half;
                if (x < -1f || x > 1f) continue;
                float now = Noise.Smooth(-1f, -0.6f, x) * (1f - Noise.Smooth(0.5f, 1f, x));
                if (now < 0.02f) continue;
                // (under the storm's flank, wandering a little as it goes)
                double bearing = 6.2831853 * (Noise.Unit(Noise.Hash(h ^ 0x73u)) + 0.08 * Math.Sin(x * 4.0));
                double off = cell.Radius * (0.32 + 0.1 * Math.Sin(x * 2.3 + 6.2831853 * Noise.Unit(Noise.Hash(h ^ 0x74u))));
                float u = Noise.Unit(Noise.Hash(h ^ 0x75u)) * (0.6f + 0.4f * cell.Strength);
                bool storm = cell.Thunder;
                float speed = !storm ? 18f + 16f * u : water ? 22f + 28f * u * u : 30f + 62f * u * u * u;
                float coreR = water ? 8f + 22f * u : 30f + 170f * u * u;
                into.Add(new Twister
                {
                    Id = (int)(Noise.Hash(h ^ 0x76u) & 0x7fffffff),
                    Lat = cell.Latitude + off * Math.Cos(bearing) / w.Radius / Deg,
                    Lon = cell.Longitude + off * Math.Sin(bearing) / (w.Radius * Math.Max(0.2, Math.Cos(cell.Latitude * Deg))) / Deg,
                    Core = coreR, Speed = speed * now, Base = cell.Base,
                    Reach = Noise.Clamp01(now * (0.45f + 0.75f * u)),
                    Spin = storm ? (cell.Latitude >= 0.0 ? 1f : -1f) * (Noise.Unit(Noise.Hash(h ^ 0x77u)) < 0.04f ? -1f : 1f) : (Noise.Unit(Noise.Hash(h ^ 0x77u)) < 0.5f ? 1f : -1f),
                    Water = water, MoveE = (float)cell.Drift.x, MoveN = (float)cell.Drift.y,
                });
            }
            // (held, to be looked at: one that goes round the space centre a kilometre and a half out, coming over the runway's
            // end, and over the sea a waterspout three kilometres off the shore)
            if (Model.Chosen == Model.HeldTornado || Model.Chosen == Model.HeldSpout)
            {
                bool spout = Model.Chosen == Model.HeldSpout;
                double a = ut / 420.0 * 6.2831853;
                double east = spout ? 5200.0 + 500.0 * Math.Cos(a) : 1500.0 * Math.Cos(a), north = spout ? 800.0 * Math.Sin(a) : 1500.0 * Math.Sin(a);
                into.Add(new Twister
                {
                    Id = spout ? 2 : 1,
                    Lat = CentreLat + north / w.Radius / Deg, Lon = CentreLon + east / (w.Radius * Math.Cos(CentreLat * Deg)) / Deg,
                    Core = spout ? 16f : 110f, Speed = (spout ? 30f : 68f) * Model.ChosenWeight, Base = 1100f, Reach = Model.ChosenWeight,
                    Spin = 1f, Water = spout, MoveE = (float)(-Math.Sin(a) * 22.0 * (spout ? 0.3 : 1.0)), MoveN = (float)(Math.Cos(a) * 22.0 * (spout ? 0.3 : 1.0)),
                });
            }
        }

        /// <summary>
        /// The wind a tornado makes at a place and height: turning round it (fastest at the edge of its core, falling away
        /// outside as the 0.7th power of the distance), flowing in to it along the ground, and rising in and round its core.
        /// </summary>
        static void TwisterWind(World w, Twister t, double lat, double lon, double alt, float agl, ref Vector3 v)
        {
            double d = Model.Distance(w, lat, lon, t.Lat, t.Lon);
            if (d > 40.0 * t.Core + 400.0 || t.Speed < 0.5f) return;
            float r = (float)Math.Max(d, 0.5);
            Model.Toward(t.Lat, t.Lon, lat, lon, out float te, out float tn);
            float round = r < t.Core ? t.Speed * r / t.Core : t.Speed * (float)Math.Pow(t.Core / r, 0.7);
            round *= 1f - Noise.Smooth(25f * t.Core, 40f * t.Core + 400f, r);
            // (above the cloud's base it is inside the storm, wider and weaker)
            round *= 1f - 0.6f * Noise.Smooth(t.Base, t.Base + 2000f, (float)alt);
            float inward = 0.45f * round * Noise.Smooth(0.6f * t.Core, 1.5f * t.Core, r) * (float)Math.Exp(-agl / 80f);
            float rc = r / (1.6f * t.Core);
            float up = 0.55f * t.Speed * (float)Math.Exp(-rc * rc) * Noise.Smooth(1f, 25f, agl) * (1f - Noise.Smooth(t.Base, t.Base + 1500f, (float)alt));
            v.x += round * -tn * t.Spin - inward * te + t.MoveE * (float)Math.Exp(-rc * rc);
            v.y += round * te * t.Spin - inward * tn + t.MoveN * (float)Math.Exp(-rc * rc);
            v.z += up;
        }

        /// <summary>The nearest tornado or waterspout to a place among those given, and how far off (m); false if none.</summary>
        public static bool Nearest(World w, List<Twister> all, double lat, double lon, out Twister nearest, out double distance)
        {
            nearest = default(Twister); distance = double.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                double d = Model.Distance(w, lat, lon, all[i].Lat, all[i].Lon);
                if (d < distance) { distance = d; nearest = all[i]; }
            }
            return distance < double.MaxValue;
        }

        /// <summary>What the small violent things add to the wind at a place, exactly there (they are far smaller than the half kilometre the wind is otherwise kept by): east, north, up, m/s.</summary>
        public static Vector3 Small(World w, Column col, double lat, double lon, double alt, float agl)
        {
            var v = Vector3.zero;
            for (int i = 0; i < col.Cells.Count; i++) CellDrafts(w, col.Cells[i], lat, lon, alt, agl, ref v);
            for (int i = 0; i < col.Bursts.Count; i++) BurstWind(w, col.Bursts[i], lat, lon, alt, agl, ref v);
            for (int i = 0; i < col.Twisters.Count; i++) TwisterWind(w, col.Twisters[i], lat, lon, alt, agl, ref v);
            // (a cyclone's eye wall: air rising at some metres a second through the depth of the storm; sinking gently in the eye)
            List<Cyclone> cyclones = Cyclones(w, col.Ut);
            for (int i = 0; i < cyclones.Count; i++)
            {
                Cyclone c = cyclones[i];
                float x = (float)(Model.Distance(w, lat, lon, c.Lat, c.Lon) / c.Rmw);
                if (x > 3f) continue;
                float tall = Noise.Smooth(200f, 1500f, agl) * (1f - Noise.Smooth(w.Climate.Tops * 0.7f, w.Climate.Tops, (float)alt));
                v.z += (7f * Noise.Bump(x, 1.15f, 0.45f) - 1.2f * (1f - Noise.Smooth(0.3f, 0.7f, x))) * c.Now * tall;
            }
            return v;
        }

        /// <summary>
        /// What violent weather there is about a place, in a sentence or two for a readout ("Tornado 2.4 km to the north-east,
        /// winds to 68 m/s."), or nothing. The nearest of each kind within sight.
        /// </summary>
        public static string Says(World w, double lat, double lon, double ut)
        {
            var said = new System.Text.StringBuilder();
            Column col = Model.ColumnAt(w, lat, lon, ut);
            if (Nearest(w, col.Twisters, lat, lon, out Twister t, out double far) && far < 30000.0)
                said.Append(t.Water ? "Waterspout " : "Tornado ").Append(Far(far)).Append(" to the ").Append(Bearing(lat, lon, t.Lat, t.Lon)).Append(", winds to ").Append(t.Speed.ToString("F0")).Append(" m/s. ");
            double nearest = double.MaxValue; Burst burst = default(Burst);
            for (int i = 0; i < col.Bursts.Count; i++) { double d = Model.Distance(w, lat, lon, col.Bursts[i].Lat, col.Bursts[i].Lon); if (d < nearest && col.Bursts[i].Speed > 6f) { nearest = d; burst = col.Bursts[i]; } }
            if (nearest < 12000.0) said.Append("Downburst ").Append(Far(nearest)).Append(" to the ").Append(Bearing(lat, lon, burst.Lat, burst.Lon)).Append(", winds to ").Append(burst.Speed.ToString("F0")).Append(" m/s along the ground. ");
            foreach (Cyclone c in Cyclones(w, ut))
            {
                double d = Model.Distance(w, lat, lon, c.Lat, c.Lon);
                if (d > 16.0 * c.Rmw) continue;
                said.Append(c.Speed >= 33f ? "Hurricane" : c.Speed >= 18f ? "Tropical storm" : "Tropical depression").Append(d < 0.5 * c.Rmw ? ": in its eye" : ": its middle " + Far(d) + " to the " + Bearing(lat, lon, c.Lat, c.Lon))
                    .Append(", winds to ").Append(c.Speed.ToString("F0")).Append(" m/s at its wall, ").Append(Far(c.Rmw)).Append(" out. ");
            }
            foreach (Squall q in Squalls(w, ut))
            {
                if (q.Now < 0.1f || !SquallAt(w, q, lat, lon, out float ahead, out float along) || Math.Abs(along) > q.Length * 0.5f) continue;
                float speed = Noise.Sqrt(q.MoveE * q.MoveE + q.MoveN * q.MoveN);
                if (ahead > 6000f) said.Append("Squall line ").Append(Far(ahead - 6000f)).Append(" off, its wind here in ").Append(Math.Max(1, (int)Math.Round((ahead - 6000f) / speed / 60f))).Append(" min. ");
                else if (ahead > -30000f) said.Append("In a squall line").Append(ahead > -3000f ? ": its wind and its storms" : ": the rain behind its storms").Append(". ");
            }
            return said.ToString().TrimEnd();
        }

        static string Far(double m) => m >= 10000.0 ? (m / 1000.0).ToString("F0") + " km" : m >= 1000.0 ? (m / 1000.0).ToString("F1") + " km" : Math.Round(m / 10.0) * 10.0 + " m";

        static string Bearing(double lat, double lon, double toLat, double toLon)
        {
            Model.Toward(lat, lon, toLat, toLon, out float e, out float n);
            return CityWeather.Compass(Mathf.Atan2(e, n) * Mathf.Rad2Deg);
        }

        // ---------------------------------------------------------------- cyclones

        const double CycloneSlot = 4.0 * 21600.0, CycloneLife = 6.5 * 21600.0;

        sealed class Kept { public World W; public long T; public int Chosen; public List<Cyclone> Cyclones; public List<Squall> Squalls; }
        static readonly Kept[] kept = new Kept[4];
        static int keptNext;
        static readonly object locker = new object();

        static Kept KeptFor(World w, double ut)
        {
            long t = (long)Math.Floor(ut / 5.0);
            int chosen = Model.Chosen;
            lock (locker)
            {
                for (int i = 0; i < kept.Length; i++)
                    if (kept[i] != null && kept[i].W == w && kept[i].T == t && kept[i].Chosen == chosen) return kept[i];
            }
            var k = new Kept { W = w, T = t, Chosen = chosen, Cyclones = MakeCyclones(w, t * 5.0), Squalls = MakeSqualls(w, t * 5.0) };
            lock (locker) { kept[keptNext] = k; keptNext = (keptNext + 1) % kept.Length; }
            return k;
        }

        /// <summary>The cyclones a world has at a moment (kept for five seconds).</summary>
        public static List<Cyclone> Cyclones(World w, double ut) => KeptFor(w, ut).Cyclones;

        /// <summary>The squall lines a world has at a moment (kept for five seconds).</summary>
        public static List<Squall> Squalls(World w, double ut) => KeptFor(w, ut).Squalls;

        static readonly List<Cyclone> noCyclones = new List<Cyclone>();
        static readonly List<Squall> noSqualls = new List<Squall>();

        static List<Cyclone> MakeCyclones(World w, double ut)
        {
            Climate c = w.Climate;
            bool held = Model.Chosen == Model.HeldCyclone && w.Name == "Kerbin";
            if (!held && (!w.Body.ocean || c.Storms <= 0.2f || c.Water <= 0f || !w.Mapped)) return noCyclones;
            var list = new List<Cyclone>();
            double slotLength = CycloneSlot / (Model.Pace <= 0.05f ? 0.05f : Model.Pace), life = CycloneLife / (Model.Pace <= 0.05f ? 0.05f : Model.Pace);
            int s0 = Noise.Floor(ut / slotLength);
            for (int s = s0 - 2; s <= s0 && !held; s++)
            {
                uint h = Noise.Hash((uint)s * 0x45d9f3bu ^ w.Seed ^ 0xc1c10eu);
                // (how often: about one slot in two on a stormy setting, one in six as installed)
                if (Noise.Unit(h) > 0.08f + 0.5f * Model.Often * c.Storms) continue;
                double born = s * slotLength + Noise.Unit(Noise.Hash(h ^ 0x1u)) * slotLength * 0.6;
                double age = ut - born;
                if (age < 0.0 || age > life) continue;
                float hemisphere = Noise.Unit(Noise.Hash(h ^ 0x2u)) < 0.5f ? 1f : -1f;
                double lat0 = hemisphere * (7.0 + 9.0 * Noise.Unit(Noise.Hash(h ^ 0x3u)));
                double lon0 = 360.0 * Noise.Unit(Noise.Hash(h ^ 0x4u)) - 180.0;
                // (born over sea: the first place eastward of the one drawn that is)
                for (int tries = 0; tries < 9 && w.Land(lat0, lon0) > 0.3f; tries++) lon0 = Wrap(lon0 + 37.0);
                if (w.Land(lat0, lon0) > 0.3f) continue;
                // ---- its way: west with the trade winds and slowly towards the pole; past 22 degrees it turns east
                double lat = lat0, lon = lon0;
                float moveE = 0f, moveN = 0f, strength = 0f;
                const int steps = 24;
                double dt = age / steps;
                float most = 0.55f + 0.45f * Noise.Unit(Noise.Hash(h ^ 0x5u));
                float fed = 0f;
                for (int k = 0; k < steps; k++)
                {
                    float turned = Noise.Smooth(20f, 27f, (float)Math.Abs(lat));
                    moveE = (-(3.5f + 2f * Noise.Unit(Noise.Hash(h ^ 0x6u))) * (1f - turned) + 6f * turned) * (c.Trade / 5f);
                    moveN = hemisphere * (0.7f + 0.6f * Noise.Unit(Noise.Hash(h ^ 0x7u)));
                    lat += moveN * dt / w.Radius / Deg;
                    lon = Wrap(lon + moveE * dt / (w.Radius * Math.Max(0.2, Math.Cos(lat * Deg))) / Deg);
                    // (it grows over warm sea and is starved over land: what it has is fed or lost a little at each step)
                    float land = w.Land(lat, lon), cold = Noise.Smooth(26f, 34f, (float)Math.Abs(lat));
                    fed += (float)(dt / (life * 0.3)) * (1f - land - cold) - (float)(dt / (life * 0.12)) * (land + cold) * fed;
                    fed = Noise.Clamp01(fed);
                }
                strength = fed * most * (1f - Noise.Smooth(0.8f, 1f, (float)(age / life)));
                if (strength < 0.12f) continue;
                list.Add(new Cyclone
                {
                    Id = (int)(h & 0x7fffffff), Lat = lat, Lon = lon,
                    Rmw = 9000f + 7000f * Noise.Unit(Noise.Hash(h ^ 0x8u)),
                    Speed = 12f + 62f * strength, Now = strength, Spin = hemisphere, MoveE = moveE, MoveN = moveN,
                });
            }
            if (held)
            {
                // (held, to be looked at: it goes to and fro along the shore, its middle passing over the space centre every
                // hour and a half: the wall, the eye, the wall again)
                double a = ut / 5400.0 * 6.2831853;
                double east = 42000.0 * Math.Sin(a), north = 0.0;
                float now = 0.85f, moveE = (float)(42000.0 * Math.Cos(a) * 6.2831853 / 5400.0);
#if DEV
                if (Probe.EyeHeld) { east = Probe.EyeEast; north = Probe.EyeNorth; now = Probe.EyeNow; moveE = 0f; }
#endif
                list.Add(new Cyclone
                {
                    Id = 7, Lat = CentreLat + north / w.Radius / Deg, Lon = CentreLon + east / (w.Radius * Math.Cos(CentreLat * Deg)) / Deg,
                    Rmw = 11000f, Speed = (12f + 54f * now) * Model.ChosenWeight, Now = now * Model.ChosenWeight, Spin = 1f,
                    MoveE = moveE, MoveN = 0f,
                });
            }
            return list.Count > 0 ? list : noCyclones;
        }

        static double Wrap(double lon) { lon %= 360.0; return lon > 180.0 ? lon - 360.0 : lon < -180.0 ? lon + 360.0 : lon; }

        /// <summary>
        /// What a cyclone is at a place: how far inside its weather the place is (0 none, 1 well inside), and of that how much
        /// is the eye, the eye wall, a spiral band. For the clouds and the rain.
        /// </summary>
        public static float CycloneAt(World w, Cyclone c, double lat, double lon, out float eye, out float wall, out float band)
        {
            eye = wall = band = 0f;
            double d = Model.Distance(w, lat, lon, c.Lat, c.Lon);
            float x = (float)(d / c.Rmw);
            if (x > 14f) return 0f;
            float shield = 1f - Noise.Smooth(8f, 14f, x);
            // (a clear eye only in a strong one; a weak one is a mass of storms with no hole in it)
            eye = (1f - Noise.Smooth(0.45f, 0.78f, x)) * Noise.Smooth(0.35f, 0.6f, c.Now);
            wall = Noise.Smooth(0.6f, 0.95f, x) * (1f - Noise.Smooth(1.5f, 2.4f, x));
            Model.Toward(c.Lat, c.Lon, lat, lon, out float te, out float tn);
            // (two bands wound in towards the middle the way the wind blows: a turn for every e-fold and a bit of distance)
            double phase = Math.Atan2(tn, te) * c.Spin + 2.3 * Math.Log(Math.Max(x, 0.5f)) + c.Id * 0.7;
            band = Noise.Smooth(0.25f, 0.8f, 0.5f + 0.5f * (float)Math.Cos(2.0 * phase)) * Noise.Smooth(2f, 3.2f, x) * (1f - Noise.Smooth(9f, 13f, x));
            return shield * Noise.Smooth(0.1f, 0.3f, c.Now);
        }

        /// <summary>A cyclone's wind at 10 m at a place (Holland's rule outside the eye wall; dying away to calm inside it): east and north, m/s; and how much of the place's weather is the cyclone's.</summary>
        public static float CycloneWind(World w, Cyclone c, double lat, double lon, out float east, out float north)
        {
            east = north = 0f;
            double d = Model.Distance(w, lat, lon, c.Lat, c.Lon);
            float x = (float)(d / c.Rmw);
            if (x > 30f) return 0f;
            const float B = 1.5f;
            float v;
            if (x < 1f) v = c.Speed * (float)Math.Pow(x, 1.4);
            else { float p = (float)Math.Pow(x, -B); v = c.Speed * Noise.Sqrt(p * (float)Math.Exp(1f - p)); }
            v *= 1f - Noise.Smooth(18f, 30f, x);
            Model.Toward(c.Lat, c.Lon, lat, lon, out float te, out float tn);
            // (the air spirals in: twenty degrees inward of the circle, outside the wall)
            float inflow = 0.36f * Noise.Smooth(0.9f, 1.6f, x);
            float re = -tn * c.Spin - inflow * te, rn = te * c.Spin - inflow * tn;
            float l = Noise.Sqrt(re * re + rn * rn);
            if (l > 1e-4f) { re /= l; rn /= l; }
            // (and it is carried along: the side where its own motion adds to its wind is the stronger)
            float carried = 0.5f * Noise.Smooth(0.3f, 1f, x) * (1f - Noise.Smooth(6f, 12f, x));
            east = v * re + c.MoveE * carried; north = v * rn + c.MoveN * carried;
            return Noise.Clamp01(v / 12f);
        }

        // ---------------------------------------------------------------- squall lines

        const double SquallSlot = 2.0 * 21600.0, SquallLife = 0.7 * 21600.0, SquallSpacing = 9000.0;

        static List<Squall> MakeSqualls(World w, double ut)
        {
            Climate c = w.Climate;
            bool held = Model.Chosen == Model.HeldSquall && w.Name == "Kerbin";
            if (!held && (c.Storms <= 0.2f || c.Lightning <= 0f)) return noSqualls;
            var list = new List<Squall>();
            double pace = Model.Pace <= 0.05f ? 0.05f : Model.Pace;
            double slotLength = SquallSlot / pace, life = SquallLife / pace;
            int s0 = Noise.Floor(ut / slotLength);
            // (three places a slot where one may form: in the middle latitudes' storm tracks and on the tropics' edges)
            for (int s = s0 - 1; s <= s0 && !held; s++)
                for (int n = 0; n < 3; n++)
                {
                    uint h = Noise.Hash((uint)s * 0x2545f491u ^ (uint)n * 0x9e3779b1u ^ w.Seed ^ 0x5a11u);
                    if (Noise.Unit(h) > 0.06f + 0.4f * Model.Often * c.Storms) continue;
                    double born = s * slotLength + Noise.Unit(Noise.Hash(h ^ 0x1u)) * slotLength;
                    double age = ut - born;
                    if (age < 0.0 || age > life) continue;
                    double lat0 = (Noise.Unit(Noise.Hash(h ^ 0x2u)) < 0.5f ? 1.0 : -1.0) * (8.0 + 42.0 * Noise.Unit(Noise.Hash(h ^ 0x3u)));
                    double lon0 = 360.0 * Noise.Unit(Noise.Hash(h ^ 0x4u)) - 180.0;
                    // (it runs ahead of the weather that made it, broadside on: eastward in the westerlies, westward in the trades)
                    float way = Math.Abs(lat0) > 28.0 ? 1f : -1f;
                    float speed = 13f + 9f * Noise.Unit(Noise.Hash(h ^ 0x5u));
                    double slant = (Noise.Unit(Noise.Hash(h ^ 0x6u)) - 0.5) * 0.9;        // (the row lies north and south, give or take 25 degrees)
                    float moveE = way * speed * (float)Math.Cos(slant), moveN = way * speed * (float)Math.Sin(slant);
                    double lat = lat0 + moveN * age / w.Radius / Deg;
                    double lon = Wrap(lon0 + moveE * age / (w.Radius * Math.Max(0.2, Math.Cos(lat0 * Deg))) / Deg);
                    float through = (float)(age / life);
                    float now = Noise.Smooth(0f, 0.15f, through) * (1f - Noise.Smooth(0.75f, 1f, through)) * (0.6f + 0.4f * Noise.Unit(Noise.Hash(h ^ 0x7u)));
                    if (now < 0.05f) continue;
                    list.Add(new Squall
                    {
                        Id = (int)(h & 0x7fffffff), Lat = lat, Lon = lon,
                        AlongE = -(float)Math.Sin(slant), AlongN = (float)Math.Cos(slant), MoveE = moveE, MoveN = moveN,
                        Length = 70000f + 80000f * Noise.Unit(Noise.Hash(h ^ 0x8u)), Now = now, Base = 900f + 500f * Noise.Unit(Noise.Hash(h ^ 0x9u)),
                    });
                }
            if (held)
            {
                // (held, to be looked at: a line that comes in off the sea over the space centre every twenty-five minutes)
                double t = ut % 1500.0;
                double east = 16000.0 - 17.0 * t;
                float now = Noise.Smooth(0f, 60f, (float)t) * (1f - Noise.Smooth(1380f, 1500f, (float)t));
#if DEV
                if (Probe.RowHeld) { east = Probe.RowEast; now = Probe.RowNow; }
#endif
                list.Add(new Squall
                {
                    Id = 9, Lat = CentreLat, Lon = CentreLon + east / (w.Radius * Math.Cos(CentreLat * Deg)) / Deg,
                    AlongE = 0f, AlongN = 1f, MoveE = -17f, MoveN = 0f, Length = 90000f,
                    Now = Model.ChosenWeight * now, Base = 1000f,
                });
            }
            return list.Count > 0 ? list : noSqualls;
        }

        /// <summary>
        /// Where a place stands against a squall line: how far ahead of the row of storms it is (m; behind: less than nothing),
        /// and how far along the row from its middle. False if it is nowhere near.
        /// </summary>
        public static bool SquallAt(World w, Squall q, double lat, double lon, out float ahead, out float along)
        {
            ahead = along = 0f;
            double d = Model.Distance(w, q.Lat, q.Lon, lat, lon);
            if (d > q.Length * 0.5 + 60000.0) return false;
            Model.Toward(q.Lat, q.Lon, lat, lon, out float te, out float tn);
            float e = (float)d * te, n = (float)d * tn;
            along = e * q.AlongE + n * q.AlongN;
            float speed = Noise.Sqrt(q.MoveE * q.MoveE + q.MoveN * q.MoveN);
            if (speed < 0.1f) return false;
            ahead = (e * q.MoveE + n * q.MoveN) / speed;
            return Math.Abs(along) < q.Length * 0.5f + 5000f && ahead > -45000f && ahead < 30000f;
        }

        /// <summary>
        /// The storms of the squall lines near a place, added to the list: one every nine kilometres along each row, each
        /// growing, raining and dying in its turn (a new one always coming up beside the old), all moving with the row.
        /// </summary>
        public static int SquallCells(World w, double lat, double lon, double radius, double ut, List<WeatherCell> into)
        {
            List<Squall> all = Squalls(w, ut);
            int found = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Squall q = all[i];
                if (q.Now < 0.05f) continue;
                if (Model.Distance(w, q.Lat, q.Lon, lat, lon) > q.Length * 0.5 + radius + 12000.0) continue;
                int half = (int)(q.Length * 0.5 / SquallSpacing);
                double cosLat = Math.Max(0.2, Math.Cos(q.Lat * Deg));
                for (int k = -half; k <= half; k++)
                {
                    uint h = Noise.Hash((uint)q.Id ^ (uint)(k + 4096) * 0x9e3779b1u);
                    // (each place in the row has a storm most of the time: one after another, fifty minutes each)
                    double turn = ut / 3000.0 + Noise.Unit(h);
                    float life = (float)(turn - Math.Floor(turn));
                    uint own = Noise.Hash(h ^ (uint)Math.Floor(turn) * 0x85ebca6bu);
                    float shape = Noise.Smooth(0f, 0.25f, life) * (1f - Noise.Smooth(0.7f, 1f, life));
                    float ends = 1f - Noise.Smooth(0.75f, 1f, Math.Abs(k) / (float)Math.Max(1, half));
                    float strength = q.Now * shape * ends * (0.7f + 0.3f * Noise.Unit(Noise.Hash(own ^ 0x1u)));
                    if (strength < 0.05f) continue;
                    double off = k * SquallSpacing + (Noise.Unit(Noise.Hash(own ^ 0x2u)) - 0.5) * 3000.0;
                    double back = (Noise.Unit(Noise.Hash(own ^ 0x3u)) - 0.5) * 2500.0;
                    float speed = Noise.Sqrt(q.MoveE * q.MoveE + q.MoveN * q.MoveN);
                    double e = off * q.AlongE + back * q.MoveE / Math.Max(speed, 0.1f), n = off * q.AlongN + back * q.MoveN / Math.Max(speed, 0.1f);
                    double cLat = q.Lat + n / w.Radius / Deg, cLon = q.Lon + e / (w.Radius * cosLat) / Deg;
                    float r = 5200f + 2600f * Noise.Unit(Noise.Hash(own ^ 0x4u));
                    if (Model.Distance(w, lat, lon, cLat, cLon) > radius + r) continue;
                    into.Add(new WeatherCell
                    {
                        Id = (int)(own & 0x7fffffff), Latitude = cLat, Longitude = cLon, Radius = r, Base = q.Base,
                        Top = q.Base + 1500f + strength * Math.Max(0f, w.Climate.Tops - q.Base - 1500f), Life = life, Strength = strength,
                        Rain = Noise.Clamp01((float)Math.Pow(strength, 1.1) * w.Climate.Water), Thunder = strength > 0.3f && w.Climate.Lightning > 0f,
                        Drift = new Vector3d(q.MoveE, q.MoveN, 0.0),
                    });
                    found++;
                }
            }
            return found;
        }

        /// <summary>
        /// The wind a squall line drives ahead of it: behind a front that runs six kilometres ahead of the row, air from the
        /// storms' rain blowing the way the row goes, strongest just behind the front and falling away over twenty-five
        /// kilometres; ahead of the front, a light wind towards the row (the air it feeds on). East and north at 10 m, m/s;
        /// and how rough (the gusts' size).
        /// </summary>
        public static void SquallWind(World w, Squall q, double lat, double lon, ref float east, ref float north, ref float rough)
        {
            if (!SquallAt(w, q, lat, lon, out float ahead, out float along)) return;
            float ends = 1f - Noise.Smooth(q.Length * 0.4f, q.Length * 0.5f + 4000f, Math.Abs(along));
            float speed = Noise.Sqrt(q.MoveE * q.MoveE + q.MoveN * q.MoveN);
            float me = q.MoveE / speed, mn = q.MoveN / speed;
            const float Front = 6000f;
            float behind = Front - ahead;                  // how far behind the front the place is
            float blow = behind > 0f ? (speed + 9f) * Noise.Smooth(0f, 350f, behind) * (float)Math.Exp(-behind / 25000f) : -3.5f * (float)Math.Exp(behind / 8000f);
            blow *= q.Now * ends;
            east += blow * me; north += blow * mn;
            rough = Math.Max(rough, q.Now * ends * 4.5f * (float)Math.Exp(-Math.Abs(behind - 800f) / 2500f));
        }
    }
}
