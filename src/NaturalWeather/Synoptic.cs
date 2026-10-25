using System;
using System.Collections.Generic;
using System.Text;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0, for the weather centre.) The weather as a forecaster reads it, worked out from the model: the pressure at sea
    /// level, which the model has not of its own; the fronts' lines; a station's report (a METAR) and the same in words; and the
    /// systems' paths. Any thread, as the model's own methods.
    ///
    /// The pressure. The model's wind is the prevailing wind for the latitude and the systems' own, which is the gradient of one
    /// field (Base.Low, how deep the lows are) turned by the world's turning (Model.ColumnAt). So the pressure is made the same
    /// way back: from the prevailing wind, integrated across the latitudes (the subtropical highs and the lows of the stormy
    /// latitudes, as the balance of the pressure's push and the turning gives them), and from that field, at the scale that
    /// balance gives the systems' wind; and in a tropical cyclone, its own low, as deep as its wind makes it. Its isobars then
    /// run along the wind the player feels, closer where it blows harder. On Kerbin, which turns four times as fast as the Earth
    /// and has lows a quarter the size, a low comes out about as deep as one of the Earth's that blows as hard.
    /// </summary>
    static class Synoptic
    {
        const double Deg = Math.PI / 180.0;

        // ---------------------------------------------------------------- the pressure

        sealed class Zonal { public World W; public float Often; public float[] P; }
        static Zonal zonal;
        static readonly object zonalLock = new object();
        const float ZonalStep = 0.5f;          // degrees

        /// <summary>The air's density at the world's sea level, kg/m³ (Kerbin's 1.225).</summary>
        static float Density(World w) => w.Body != null && w.Body.atmDensityASL > 0.0 ? (float)w.Body.atmDensityASL : 1.225f;

        /// <summary>The world's turning, its Coriolis parameter at a latitude (1/s).</summary>
        static float Turning(World w, double lat) => w.Spin > 0.0 ? (float)(2.0 * (2.0 * Math.PI / w.Spin) * Math.Sin(lat * Deg)) : 0f;

        /// <summary>The world's own sea-level pressure, hPa (Kerbin's 1013.25).</summary>
        public static float Mean(World w) => w.Body != null && w.Body.atmospherePressureSeaLevel > 0.0 ? (float)(w.Body.atmospherePressureSeaLevel * 10.0) : 1013.25f;

        /// <summary>The part of the pressure that goes by latitude alone, from the prevailing wind: hPa against the world's own.</summary>
        static float ZonalAt(World w, double lat)
        {
            Zonal z = zonal;
            if (z == null || z.W != w)
            {
                lock (zonalLock)
                {
                    z = zonal;
                    if (z == null || z.W != w)
                    {
                        z = new Zonal { W = w, P = MakeZonal(w) };
                        zonal = z;
                    }
                }
            }
            double i = (lat + 90.0) / ZonalStep;
            int i0 = Math.Max(0, Math.Min(z.P.Length - 2, (int)Math.Floor(i)));
            float f = (float)Math.Max(0.0, Math.Min(1.0, i - i0));
            return z.P[i0] * (1f - f) + z.P[i0 + 1] * f;
        }

        static float[] MakeZonal(World w)
        {
            int n = (int)(180f / ZonalStep) + 1;
            var p = new float[n];
            float rho = Density(w);
            double dy = w.Radius * ZonalStep * Deg;
            // (from the south pole north: the pressure falls to the left of the wind's way, north of the equator, so a westerly
            // wind has it falling towards the pole; dP/dy = -rho f u)
            for (int k = 1; k < n; k++)
            {
                double lat = -90.0 + (k - 0.5) * ZonalStep;
                p[k] = p[k - 1] - (float)(rho * Turning(w, lat) * Model.PrevailingEast(w, lat) * dy) / 100f;
            }
            // (the mean over the world's surface its own sea-level pressure: weighted by the cosine of the latitude)
            double sum = 0.0, weights = 0.0;
            for (int k = 0; k < n; k++) { double c = Math.Cos((-90.0 + k * ZonalStep) * Deg); sum += p[k] * c; weights += c; }
            float mean = (float)(sum / Math.Max(weights, 1e-9));
            for (int k = 0; k < n; k++) p[k] -= mean;
            return p;
        }

        /// <summary>The pressure at sea level at a place (hPa), from the model's own fields there (b: Model.Sample there and then).</summary>
        public static float Pressure(World w, double lat, double lon, double ut, Base b)
        {
            float p = Mean(w) + ZonalAt(w, lat);
            // (the systems': the wind for a unit of Low across 100 km is SystemsWind, so a unit of it is rho f times that, times 100 km,
            // of pressure; about the field's middle, so that its own change with the latitude adds little that the wind has not)
            float scale = Density(w) * Math.Abs(Turning(w, lat)) * Model.SystemsWind(w, lat) * 1.0e5f / 100f;
            p -= scale * (b.Low - 0.5f);
            // (a tropical cyclone's own low: as deep as one and a half times the air's density by its wind squared, as a vortex
            // whose wind falls off as one over the root of the distance outside its eye wall; Holland's profile)
            foreach (Cyclone c in Violent.Cyclones(w, ut))
            {
                double d = Model.Distance(w, lat, lon, c.Lat, c.Lon);
                if (d > 40.0 * c.Rmw) continue;
                float deficit = 1.5f * Density(w) * c.Speed * c.Speed / 100f;
                float r = (float)Math.Max(d, 1.0);
                p -= deficit * (float)Math.Exp(-Math.Pow(c.Rmw / r, 1.5));
            }
            return p;
        }

        public static float Pressure(World w, double lat, double lon, double ut) => Pressure(w, lat, lon, ut, Model.Sample(w, lat, lon, ut));

        /// <summary>A cyclone's central pressure (hPa), and the Saffir-Simpson category its wind makes it (0 a tropical storm, -1 a depression).</summary>
        public static float Central(World w, Cyclone c, double ut, out int category)
        {
            category = Category(c.Speed);
            return Pressure(w, c.Lat, c.Lon, ut);
        }

        /// <summary>The Saffir-Simpson category of a wind (m/s, sustained at 10 m): -1 a tropical depression, 0 a tropical storm, 1 to 5 a hurricane.</summary>
        public static int Category(float speed)
        {
            float kt = speed * 1.94384f;
            return kt < 34f ? -1 : kt < 64f ? 0 : kt < 83f ? 1 : kt < 96f ? 2 : kt < 113f ? 3 : kt < 137f ? 4 : 5;
        }

        // ---------------------------------------------------------------- the fronts

        public enum FrontKind { Cold, Warm, Occluded }

        public sealed class Front
        {
            public FrontKind Kind;
            public int Low;                    // the low it belongs to (its Id)
            public float Strength;             // how strong the low is, 0 to 1
            public readonly List<Vector2d> Points = new List<Vector2d>();   // latitude, longitude, along the line
            /// <summary>Which side its symbols go: +1 to the left of the line as drawn from its first point (on the map, north up), -1 right.</summary>
            public float Side;
        }

        /// <summary>
        /// The fronts of the world's lows at a moment, as lines (Lows.Of draws them as bands): for each low, its cold front
        /// trailing back towards the equator from where the two meet, its warm front ahead of it, and once it has wound in, the
        /// occluded front from its centre to where they meet. Each line wound round the low as its pattern is by the flow (Lows.Of:
        /// a place there shows the pattern turned back by the twist there; so the pattern's line is seen turned on by it).
        /// </summary>
        public static void Fronts(World w, double ut, List<Front> into)
        {
            foreach (Low s in Lows.At(w, ut))
            {
                if (s.Now < 0.15f) continue;
                float bend = s.Bend;
                // (in the low's own frame, in heads: x east, y towards its pole; the points of Lows.Of's two lines)
                var cold = new[] { new Vector2(0.55f, -0.15f), new Vector2(0.3f - 0.2f * bend, -0.85f), new Vector2(-0.05f - 0.6f * bend, -1.6f), new Vector2(-0.4f - 1.2f * bend, -2.35f) };
                var warm = new[] { new Vector2(0.55f, -0.15f), new Vector2(1.1f, 0f), new Vector2(1.7f, 0.2f) };
                Vector2 meet = cold[0];
                if (s.Wound > 0.45f)
                {
                    // (wound in: the cold front has caught the warm one near the centre; where they meet is carried out along
                    // the occlusion, and the occluded front runs from the centre to it)
                    float o = Mathf.Clamp01((s.Wound - 0.45f) / 0.4f);
                    var f = new Front { Kind = FrontKind.Occluded, Low = s.Id, Strength = s.Now, Side = 1f };
                    int k = 8;
                    for (int i = 0; i <= k; i++) f.Points.Add(Place(w, s, Vector2.Lerp(Vector2.zero, meet, i / (float)k) * (0.4f + 0.6f * o)));
                    into.Add(f);
                }
                // (the symbols on the side each goes: the cold front's towards the warm air ahead of it, east of it; the warm
                // front's towards the cold air it climbs over, poleward of it: on the left of both as drawn from where they meet,
                // north of the equator, and on the right south of it, where the pattern is mirrored)
                into.Add(Along(w, s, cold, FrontKind.Cold, 1f));
                into.Add(Along(w, s, warm, FrontKind.Warm, 1f));
            }
        }

        static Front Along(World w, Low s, Vector2[] line, FrontKind kind, float side)
        {
            var f = new Front { Kind = kind, Low = s.Id, Strength = s.Now, Side = side * s.Pole };
            for (int p = 0; p < line.Length - 1; p++)
            {
                int steps = 6;
                for (int i = p == 0 ? 0 : 1; i <= steps; i++) f.Points.Add(Place(w, s, Vector2.Lerp(line[p], line[p + 1], i / (float)steps)));
            }
            return f;
        }

        /// <summary>A point of a low's pattern (in heads, its own frame) where it is seen: turned on by the flow's twist, then on the world.</summary>
        static Vector2d Place(World w, Low s, Vector2 q)
        {
            float r = q.magnitude;
            if (Lows.Flow)
            {
                float twist = (1.1f + 1.6f * s.Wound) * (float)Math.Exp(-r * 0.75f);
                float c = (float)Math.Cos(twist), sn = (float)Math.Sin(twist);
                q = new Vector2(q.x * c - q.y * sn, q.x * sn + q.y * c);
            }
            double east = q.x * s.Size, north = q.y * s.Pole * s.Size;
            double lat = s.Lat + north / w.Radius / Deg;
            double lon = s.Lon + east / (w.Radius * Math.Max(0.05, Math.Cos(lat * Deg))) / Deg;
            return new Vector2d(lat, Wrap(lon));
        }

        public static double Wrap(double lon) => ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;

        // ---------------------------------------------------------------- the systems' paths

        public enum SystemKind { Low, Cyclone, Squall }

        public sealed class Path
        {
            public SystemKind Kind;
            public int Id;
            public readonly List<Vector2d> Points = new List<Vector2d>();   // where it is at each of Times
            public readonly List<double> Times = new List<double>();
            public readonly List<float> Strength = new List<float>();       // how strong then (a cyclone: its wind, m/s)
        }

        /// <summary>
        /// The paths of the world's systems, from `from` to `to` every `step` seconds: each low, cyclone and squall line by its Id,
        /// where it is at each moment it is alive. Worked out from the clock, as the systems themselves are: the paths are the
        /// paths they will take.
        /// </summary>
        public static List<Path> Paths(World w, double from, double to, double step)
        {
            var byId = new Dictionary<long, Path>();
            var list = new List<Path>();
            for (double t = from; t <= to + 1e-6; t += step)
            {
                foreach (Low s in Lows.At(w, t))
                    if (s.Now > 0.05f) Add(byId, list, SystemKind.Low, s.Id, s.Lat, s.Lon, t, s.Now);
                foreach (Cyclone c in Violent.Cyclones(w, t))
                    Add(byId, list, SystemKind.Cyclone, c.Id, c.Lat, c.Lon, t, c.Speed);
                foreach (Squall q in Violent.Squalls(w, t))
                    if (q.Now > 0.05f) Add(byId, list, SystemKind.Squall, q.Id, q.Lat, q.Lon, t, q.Now);
            }
            return list;
        }

        static void Add(Dictionary<long, Path> byId, List<Path> list, SystemKind kind, int id, double lat, double lon, double t, float strength)
        {
            long key = ((long)kind << 32) | (uint)id;
            if (!byId.TryGetValue(key, out Path p)) { p = new Path { Kind = kind, Id = id }; byId[key] = p; list.Add(p); }
            p.Points.Add(new Vector2d(lat, lon)); p.Times.Add(t); p.Strength.Add(strength);
        }

        // ---------------------------------------------------------------- a station's report

        /// <summary>
        /// The dew point at the ground (kelvin): from the height air from the ground would be lifted to before it clouds (about 125 m
        /// for each degree between the temperature and the dew point), the air saturated in fog and nearly so in rain.
        /// </summary>
        public static float DewPoint(Base b, WeatherHere h)
        {
            float spread = Model.LiftedBase(b) / 125f;
            if (h.Fog > 0.3f) spread = Mathf.Lerp(spread, 0f, Mathf.Clamp01((h.Fog - 0.3f) / 0.4f));
            if (h.Rain > 0.05f) spread = Mathf.Min(spread, Mathf.Lerp(3f, 0.7f, Mathf.Clamp01(h.Rain * 2f)));
            return h.Temperature - Mathf.Max(0f, spread);
        }

        /// <summary>A station's name for the place: the space centre's, the island airfield's, or none (ZZZZ, as a report from where there is no station).</summary>
        public static string Station(World w, double lat, double lon)
        {
            if (w.Body != null && w.Body.isHomeWorld)
            {
                if (Model.Distance(w, lat, lon, -0.0972, -74.5577) < 15000.0) return "KKSC";
                if (Model.Distance(w, lat, lon, -1.5177, -71.9000) < 10000.0) return "KISL";
            }
            return "ZZZZ";
        }

        /// <summary>
        /// The weather at a place as a METAR (an aviation weather report), and the same in words: the station, day and time, the
        /// wind (where from, to ten degrees, knots, and its gusts), how far one can see, what is falling or about, the cloud layers
        /// (how much of the sky, their bases in hundreds of feet above the ground, CB for a thunderstorm's), the temperature and
        /// dew point, the pressure (QNH), and remarks: lightning seen, storms near by.
        /// </summary>
        public static string Metar(World w, double lat, double lon, double ut, out string words)
        {
            Base b = Model.Sample(w, lat, lon, ut);
            WeatherHere h = Model.Here(w, lat, lon, ut);
            Column col = Model.ColumnAt(w, lat, lon, ut);
            float ground = Math.Max(0f, b.Ground);
            var m = new StringBuilder();
            var say = new StringBuilder();
            m.Append(Station(w, lat, lon)).Append(' ').Append(DayTime(ut)).Append("Z AUTO ");

            // ---- the wind at 10 m
            float ue = col.UE, un = col.UN, speed = Mathf.Sqrt(ue * ue + un * un);
            // (the gust a report gives: the strongest few seconds of the last ten minutes, about the mean and two and a half times the gusts' spread)
            float kt = speed * 1.94384f, gust = (speed + 2.5f * h.Gusts) * 1.94384f;
            int from = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(-ue, -un) * Mathf.Rad2Deg, 360f) / 10f) * 10;
            if (from == 0) from = 360;
            if (kt < 1f) { m.Append("00000KT "); say.Append("Calm. "); }
            else if (kt < 3f) { m.Append("VRB").Append(Mathf.RoundToInt(kt).ToString("00")).Append("KT "); say.Append("Wind light and variable. "); }
            else
            {
                m.Append(from.ToString("000")).Append(Mathf.RoundToInt(kt).ToString("00"));
                bool gusty = gust - kt >= 10f;
                if (gusty) m.Append('G').Append(Mathf.RoundToInt(gust).ToString("00"));
                m.Append("KT ");
                say.Append("Wind from ").Append(Compass(from)).Append(" (").Append(from.ToString("000")).Append("°) at ").Append(speed.ToString("F0")).Append(" m/s")
                   .Append(gusty ? ", gusting " + (speed + 2.5f * h.Gusts).ToString("F0") + " m/s" : "").Append(". ");
            }

            // ---- how far one can see, and what is falling or about
            float seeing = Mathf.Max(0f, h.Seeing);
            string wx = Weather(h, out string wxWords);
            bool cavok = seeing >= 10000f && wx.Length == 0 && Lowest(h, ground, out _) > 5000f / 3.28084f && !Towering(h);
            if (cavok) { m.Append("CAVOK "); say.Append("Ceiling and visibility OK: 10 km or more, no cloud below 5,000 ft, no storm cloud, nothing falling. "); }
            else
            {
                m.Append(seeing >= 9999f ? "9999" : (Mathf.Floor(Mathf.Max(seeing, 0f) / 100f) * 100f).ToString("0000")).Append(' ');
                say.Append("Visibility ").Append(seeing >= 9999f ? "10 km or more" : seeing >= 5000f ? (seeing / 1000f).ToString("F0") + " km" : seeing >= 1000f ? (seeing / 1000f).ToString("F1") + " km" : seeing.ToString("F0") + " m").Append(". ");
                if (wx.Length > 0) { m.Append(wx).Append(' '); say.Append(wxWords).Append(". "); }
                // ---- the cloud layers, lowest first, each in feet above the ground (to hundreds)
                string layers = Layers(h, ground, out string layerWords);
                m.Append(layers.Length > 0 ? layers : "NCD").Append(' ');
                say.Append(layerWords.Length > 0 ? layerWords : "No cloud detected").Append(". ");
            }

            // ---- temperature and dew point, pressure
            float t = h.Temperature - 273.15f, td = DewPoint(b, h) - 273.15f;
            m.Append(Temp(t)).Append('/').Append(Temp(td)).Append(' ');
            float qnh = Pressure(w, lat, lon, ut, b);
            m.Append('Q').Append(Mathf.RoundToInt(qnh).ToString("0000"));
            say.Append("Temperature ").Append(t.ToString("F0")).Append(" °C, dew point ").Append(td.ToString("F0")).Append(" °C; pressure ").Append(qnh.ToString("F0")).Append(" hPa at sea level.");

            // ---- remarks: storms and lightning near by
            string rmk = Remarks(w, lat, lon, ut, out string rmkWords);
            if (rmk.Length > 0) { m.Append(" RMK ").Append(rmk); say.Append(' ').Append(rmkWords); }
            words = say.ToString();
            return m.ToString();
        }

        /// <summary>A report's groups for the weather, as a METAR and a TAF give them: the wind (m/s given; where from, degrees; the gust), visibility or CAVOK, the weather, the cloud.</summary>
        public static string Groups(WeatherHere h, Base b, float speed, float from, float gust)
        {
            var g = new StringBuilder();
            float kt = speed * 1.94384f, gkt = gust * 1.94384f;
            int dir = Mathf.RoundToInt(Mathf.Repeat(from, 360f) / 10f) * 10;
            if (dir == 0) dir = 360;
            if (kt < 1f) g.Append("00000KT");
            else if (kt < 3f) g.Append("VRB").Append(Mathf.RoundToInt(kt).ToString("00")).Append("KT");
            else g.Append(dir.ToString("000")).Append(Mathf.RoundToInt(kt).ToString("00")).Append(gkt - kt >= 10f ? "G" + Mathf.RoundToInt(gkt).ToString("00") : "").Append("KT");
            float ground = Mathf.Max(0f, b.Ground), seeing = Mathf.Max(0f, h.Seeing);
            string wx = Weather(h, out string _);
            if (seeing >= 10000f && wx.Length == 0 && Lowest(h, ground, out _) > 5000f / 3.28084f && !Towering(h)) { g.Append(" CAVOK"); return g.ToString(); }
            g.Append(' ').Append(seeing >= 9999f ? "9999" : (Mathf.Floor(seeing / 100f) * 100f).ToString("0000"));
            if (wx.Length > 0) g.Append(' ').Append(wx);
            string layers = Layers(h, ground, out string _);
            g.Append(' ').Append(layers.Length > 0 ? layers : "NSC");
            return g.ToString();
        }

        /// <summary>The day of the year and the time of day, as a report's DDHHMM (Kerbin's day of six hours: hours 00 to 05).</summary>
        public static string DayTime(double ut)
        {
            double day = KSPUtil.dateTimeFormatter != null ? KSPUtil.dateTimeFormatter.Day : 21600.0;
            double year = KSPUtil.dateTimeFormatter != null ? KSPUtil.dateTimeFormatter.Year : 9203545.0;
            double inYear = ut - Math.Floor(ut / year) * year;
            int d = (int)Math.Floor(inYear / day) + 1;
            double inDay = inYear - (d - 1) * day;
            int hh = (int)Math.Floor(inDay / 3600.0), mm = (int)Math.Floor((inDay - hh * 3600.0) / 60.0);
            return d.ToString("00") + hh.ToString("00") + mm.ToString("00");
        }

        static string Temp(float c) { int r = Mathf.RoundToInt(c); return r < 0 ? "M" + (-r).ToString("00") : r.ToString("00"); }

        static string Compass(int deg)
        {
            string[] names = { "the north", "the north-north-east", "the north-east", "the east-north-east", "the east", "the east-south-east", "the south-east", "the south-south-east",
                               "the south", "the south-south-west", "the south-west", "the west-south-west", "the west", "the west-north-west", "the north-west", "the north-north-west" };
            return names[Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 22.5f) % 16];
        }

        /// <summary>The weather now as a report's group (-SHRA, +TSRAGR, FZRA, BR ...), and in words.</summary>
        static string Weather(WeatherHere h, out string words)
        {
            var g = new List<string>();
            var w = new List<string>();
            bool storm = h.Storm > 0.25f;
            // (Rain is all that falls; Snow, Freezing and Pellets the shares of it that are each)
            float falling = h.Rain, liquid = Mathf.Max(0f, 1f - h.Snow - h.Freezing - h.Pellets);
            if (falling > 0.03f || storm)
            {
                string sign = falling > 0.6f ? "+" : falling < 0.2f ? "-" : "";
                string how = falling > 0.6f ? "heavy " : falling < 0.2f ? "light " : "";
                var kinds = new StringBuilder();
                var named = new List<string>();
                if (falling > 0.03f)
                {
                    if (h.Freezing > 0.25f) { kinds.Append("RA"); named.Add("freezing rain"); }
                    else if (liquid > 0.25f) { kinds.Append("RA"); named.Add("rain"); }
                    if (h.Snow > 0.25f) { kinds.Append("SN"); named.Add("snow"); }
                    if (h.Pellets > 0.25f) { kinds.Append("PL"); named.Add("ice pellets"); }
                }
                if (h.Hail > 0.05f) { kinds.Append(h.HailSize >= 5f ? "GR" : "GS"); named.Add(h.HailSize >= 5f ? "hail of " + h.HailSize.ToString("F0") + " mm" : "small hail"); }
                string desc = storm ? "TS" : h.Freezing > 0.25f ? "FZ" : h.Low.Kind == CloudKind.Cumulus || h.Low.Kind == CloudKind.Cumulonimbus ? "SH" : "";
                // (the sign is what falls' intensity: thunder with nothing falling here is plain TS)
                if (kinds.Length > 0 || storm) g.Add((kinds.Length > 0 ? sign : "") + desc + kinds);
                string what = string.Join(" and ", named.ToArray());
                if (storm) w.Add("Thunderstorm" + (what.Length > 0 ? " with " + how + what : ""));
                else if (what.Length > 0) w.Add(char.ToUpper((how + (desc == "SH" ? what + " showers" : what))[0]) + (how + (desc == "SH" ? what + " showers" : what)).Substring(1));
            }
            if (h.Fog > 0.3f && h.Seeing < 1000f) { g.Add(h.FogFreezes ? "FZFG" : "FG"); w.Add(h.FogFreezes ? "Freezing fog" : "Fog"); }
            else if (h.Seeing < 5000f && h.Fog > 0.1f) { g.Add("BR"); w.Add("Mist"); }
            if (h.Dust > 0.2f) { g.Add(h.Dust > 0.6f && h.Seeing < 1000f ? "DS" : "DU"); w.Add(h.Dust > 0.6f ? "Dust storm" : "Dust"); }
            words = string.Join("; ", w.ToArray());
            return string.Join(" ", g.ToArray());
        }

        static bool Towering(WeatherHere h) => h.Low.Kind == CloudKind.Cumulonimbus || (h.Low.Kind == CloudKind.Cumulus && h.Low.Top - h.Low.Base > 3000f);

        /// <summary>The lowest layer's base above the ground that covers half the sky or more (the ceiling), m; none: very high.</summary>
        static float Lowest(WeatherHere h, float ground, out bool any)
        {
            float best = float.MaxValue; any = false;
            foreach (CloudLayer l in new[] { h.Low, h.Middle, h.High })
            {
                if (l.Kind == CloudKind.None || l.Cover < 0.05f) continue;
                any = true;
                if (l.Cover >= 0.5f) best = Mathf.Min(best, Mathf.Max(0f, l.Base - ground));
            }
            return best;
        }

        /// <summary>The cloud layers as a report gives them (FEW020 SCT045CB BKN120 ...), and in words.</summary>
        static string Layers(WeatherHere h, float ground, out string words)
        {
            var g = new List<string>();
            var w = new List<string>();
            bool vv = h.Fog > 0.5f && h.Seeing < 500f;
            if (vv) { g.Add("VV" + Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(h.FogDeep, 30f) * 3.28084f / 100f), 0, 999).ToString("000")); w.Add("the sky hidden by fog, seen up to " + Mathf.Max(h.FogDeep, 30f).ToString("F0") + " m"); }
            foreach (CloudLayer l in new[] { h.Low, h.Middle, h.High })
            {
                if (l.Kind == CloudKind.None || l.Cover < 0.07f) continue;
                if (vv && l.Base - ground < 300f) continue;
                int oktas = Mathf.Clamp(Mathf.RoundToInt(l.Cover * 8f), 1, 8);
                string amount = oktas <= 2 ? "FEW" : oktas <= 4 ? "SCT" : oktas <= 7 ? "BKN" : "OVC";
                string amountWords = oktas <= 2 ? "a few" : oktas <= 4 ? "scattered" : oktas <= 7 ? "broken" : "overcast";
                float above = Mathf.Max(0f, l.Base - ground);
                int hundreds = Mathf.Clamp(Mathf.RoundToInt(above * 3.28084f / 100f), 0, 999);
                string kind = l.Kind == CloudKind.Cumulonimbus ? "CB" : l.Kind == CloudKind.Cumulus && l.Top - l.Base > 3000f ? "TCU" : "";
                g.Add(amount + hundreds.ToString("000") + kind);
                w.Add(amountWords + " " + Named(l.Kind) + " at " + (above < 1000f ? above.ToString("F0") + " m" : (above / 1000f).ToString("F1") + " km") + " (" + (hundreds * 100).ToString("N0") + " ft)");
            }
            words = w.Count > 0 ? char.ToUpper(w[0][0]) + string.Join(", ", w.ToArray()).Substring(1) : "";
            return string.Join(" ", g.ToArray());
        }

        public static string Named(CloudKind k)
        {
            switch (k)
            {
                case CloudKind.Cumulus: return "cumulus";
                case CloudKind.Cumulonimbus: return "cumulonimbus";
                case CloudKind.Stratus: return "stratus";
                case CloudKind.Stratocumulus: return "stratocumulus";
                case CloudKind.Nimbostratus: return "nimbostratus";
                case CloudKind.Altostratus: return "altostratus";
                case CloudKind.Altocumulus: return "altocumulus";
                case CloudKind.Cirrus: return "cirrus";
                case CloudKind.Fog: return "fog";
                case CloudKind.Dust: return "dust";
                default: return k.ToString().ToLowerInvariant();
            }
        }

        /// <summary>Remarks: the nearest thunderstorm within 60 km, where it is and which way it goes (LTG DSNT W, TS 18KM NW MOV E).</summary>
        static string Remarks(World w, double lat, double lon, double ut, out string words)
        {
            var cells = new List<WeatherCell>();
            Model.Cells(w, lat, lon, 60000.0, ut, cells);
            WeatherCell best = default(WeatherCell);
            double near = double.MaxValue;
            foreach (WeatherCell c in cells)
            {
                if (!c.Thunder || c.Strength < 0.25f) continue;
                double d = Model.Distance(w, lat, lon, c.Latitude, c.Longitude);
                if (d < near) { near = d; best = c; }
            }
            words = "";
            if (near == double.MaxValue) return "";
            Model.Toward(lat, lon, best.Latitude, best.Longitude, out float te, out float tn);
            int brg = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(te, tn) * Mathf.Rad2Deg, 360f));
            string dir = Octant(brg);
            float me = (float)best.Drift.x, mn = (float)best.Drift.y;
            string mov = Mathf.Sqrt(me * me + mn * mn) > 1f ? " MOV " + Octant(Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(me, mn) * Mathf.Rad2Deg, 360f))) : "";
            string movWords = mov.Length > 0 ? ", moving " + Compass(Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(me, mn) * Mathf.Rad2Deg, 360f))).Replace("the ", "") + "ward" : "";
            if (near < Math.Max(best.Radius, 3000f)) { words = "A thunderstorm overhead" + movWords + "."; return "TS OHD" + mov; }
            if (near < 18520.0) { words = "A thunderstorm " + (near / 1000.0).ToString("F0") + " km to " + Compass(brg) + movWords + " (lightning within ten nautical miles)."; return "TS " + (near / 1000.0).ToString("F0") + "KM " + dir + mov; }
            words = "Lightning far off to " + Compass(brg) + ", " + (near / 1000.0).ToString("F0") + " km" + movWords + ".";
            return "LTG DSNT " + dir;
        }

        static string Octant(int deg)
        {
            string[] o = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return o[Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 45f) % 8];
        }
    }
}
