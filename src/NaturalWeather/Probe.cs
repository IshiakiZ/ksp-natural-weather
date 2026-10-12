#if DEV
using System;
using System.Diagnostics;
using System.Text;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>(For the development build, through dev_call: what the weather is, a weather held for a test, a map, timings.)</summary>
    public static class Probe
    {
        /// <summary>A weather held for tests (the number of one of Model.Choices), without touching the player's settings; -1 lets go.</summary>
        public static int Hold = -1;

        /// <summary>For a test with the wind not moving craft, without touching the player's settings.</summary>
        public static bool NoPush;
        public static string Push(string arg) { NoPush = arg != null && arg.Trim() == "off"; return NoPush ? "wind does not move craft (test)" : "wind moves craft as set"; }

        /// <summary>Where the craft is against the space centre's pad (metres east and north), how high, and how fast over the ground east, north and up.</summary>
        public static string Track()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            CelestialBody b = v.mainBody;
            double dn = (v.latitude - (-0.0972)) * Math.PI / 180.0 * b.Radius, de = (v.longitude - (-74.5577)) * Math.PI / 180.0 * b.Radius * Math.Cos(v.latitude * Math.PI / 180.0);
            Provider.Axes(b, v.CoMD, out Vector3d e, out Vector3d n, out Vector3d u);
            Vector3d s = v.srf_velocity;
            Vector3 wind = Aero.OnCraft(v);
            return "t " + v.missionTime.ToString("F1") + " s: " + de.ToString("F1") + " m east, " + dn.ToString("F1") + " m north, " + v.altitude.ToString("F0") + " m up; going " + Vector3d.Dot(s, e).ToString("F2") + " east, " + Vector3d.Dot(s, n).ToString("F2") + " north, " + Vector3d.Dot(s, u).ToString("F1") + " up; wind on it " + Vector3d.Dot(wind, e).ToString("F1") + " east, " + Vector3d.Dot(wind, n).ToString("F1") + " north" + (NoPush ? " (not pushing: test)" : "");
        }

        public static string Choose(string arg)
        {
            int n = int.Parse(arg.Trim());
            Hold = n;
            Model.Chosen = Math.Max(0, n); Model.ChosenWeight = n > 0 ? 1f : 0f;
            Model.Forget(); Launch.Again();
            return n >= 0 ? "held: " + Model.Choices[Math.Max(0, n)] : "let go";
        }

        public static string Here()
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl)) return "nowhere";
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            WeatherHere h = Model.Here(w, lat, lon, ut);
            Column col = Model.ColumnAt(w, lat, lon, ut);
            var b = new StringBuilder();
            b.Append(body.bodyName).Append(" ").Append(lat.ToString("F3")).Append(", ").Append(lon.ToString("F3")).Append(" at ").Append(alt.ToString("F0")).Append(" m (").Append(agl.ToString("F0")).Append(" above ground; the map says ground ").Append(col.B.Ground.ToString("F0")).Append(w.Grounded ? ")" : "; the world has no ground under its air)").Append("\n");
            b.Append(Panel.Words(h)).Append("\n");
            foreach (var pair in new[] { ("low", h.Low), ("middle", h.Middle), ("high", h.High) })
                b.Append(pair.Item1).Append(": ").Append(pair.Item2.Kind).Append(" ").Append(pair.Item2.Cover.ToString("F2")).Append(" ").Append(pair.Item2.Base.ToString("F0")).Append("-").Append(pair.Item2.Top.ToString("F0")).Append(" water ").Append(pair.Item2.Water.ToString("F2")).Append("\n");
            b.Append("rain ").Append(h.Rain.ToString("F2")).Append(" snow ").Append(h.Snow.ToString("F2")).Append(" storm ").Append(h.Storm.ToString("F2")).Append(" fog ").Append(h.Fog.ToString("F2")).Append(" dust ").Append(h.Dust.ToString("F2")).Append(" seeing ").Append(h.Seeing.ToString("F0")).Append(" T ").Append((h.Temperature - 273.15f).ToString("F1")).Append(" C\n");
            b.Append("fields: M ").Append(col.B.M.ToString("F2")).Append(" inst ").Append(col.B.Inst.ToString("F2")).Append(" front ").Append(col.B.Front.ToString("F2")).Append(" land ").Append(col.B.Land.ToString("F2")).Append(" sun ").Append(col.B.SunUp.ToString("F2")).Append(" hour ").Append(col.B.Hour.ToString("F0")).Append(" freeze ").Append(col.B.Freeze.ToString("F0")).Append(" drift ").Append(col.B.DriftE.ToString("F1")).Append("\n");
            b.Append("wind 10 m: ").Append(col.UE.ToString("F1")).Append(" E ").Append(col.UN.ToString("F1")).Append(" N; gusts ").Append(h.Gusts.ToString("F2")).Append("; boundary layer ").Append(col.Hbl.ToString("F0")).Append(" m, alpha ").Append(col.Alpha.ToString("F2")).Append("; jet ").Append(col.JE.ToString("F0")).Append(" E ").Append(col.JN.ToString("F0")).Append(" N at ").Append(col.JetH.ToString("F0")).Append("\n");
            foreach (float hh in new[] { 2f, 10f, 50f, 200f, 1000f, 3000f, 8000f, 12000f })
            {
                Vector3 m = Model.Mean(col, col.B.Ground + hh, hh);
                Vector3 g = Gusts.At(col, lat, lon, col.B.Ground + hh, hh, ut);
                b.Append("  at ").Append(hh.ToString("F0")).Append(" m: mean ").Append(new Vector2(m.x, m.y).magnitude.ToString("F1")).Append(" gust now ").Append(g.ToString("F1")).Append(" sigma ").Append(Model.Sigmas(col, col.B.Ground + hh, hh, out float sw, out float lu, out float lw).ToString("F2")).Append("/").Append(sw.ToString("F2")).Append(" L ").Append(lu.ToString("F0")).Append("/").Append(lw.ToString("F0")).Append("\n");
            }
            b.Append("cells within 25 km: ").Append(col.Cells.Count);
            foreach (WeatherCell c in col.Cells) b.Append("\n  ").Append(c.Thunder ? "storm" : "shower").Append(" ").Append((Model.Distance(w, lat, lon, c.Latitude, c.Longitude) / 1000.0).ToString("F1")).Append(" km, r ").Append((c.Radius / 1000f).ToString("F1")).Append(" km, strength ").Append(c.Strength.ToString("F2")).Append(" life ").Append(c.Life.ToString("F2")).Append(" top ").Append(c.Top.ToString("F0"));
            WeatherShade s = Provider.Last;
            b.Append("\nshade: sun ").Append(s.Sun.ToString("F2")).Append(" sky ").Append(s.Sky.ToString("F2")).Append(" grey ").Append(s.Grey.ToString("F2"));
            b.Append("\nlaunch: ").Append(Launch.Now.Go ? "GO" : "NO GO (" + Launch.Now.First + ")");
            foreach (Launch.Rule r in Launch.Now.Rules) b.Append("\n  ").Append(r.Go ? "go    " : "no go ").Append(r.Name).Append(": ").Append(r.Value);
            b.Append("\n").Append(Aero.Report).Append(" ").Append(Feed.Report).Append(" ").Append(Rain.Report).Append(" ").Append(Lightning.Report);
            return b.ToString();
        }

        /// <summary>A map of the clouds about the place: 64 by 24 characters, `arg` km each way (60 by default), at the low cloud's middle.</summary>
        public static string Map(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out _, out _)) return "nowhere";
            double reach = 60000.0;
            if (!string.IsNullOrEmpty(arg)) reach = double.Parse(arg) * 1000.0;
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            const string shades = " .:-=+*#%@";
            var b = new StringBuilder();
            var t = Stopwatch.StartNew();
            for (int y = 11; y >= -12; y--)
            {
                for (int x = -32; x < 32; x++)
                {
                    double dn = y / 12.0 * reach, de = x / 32.0 * reach;
                    double la = lat + dn / w.Radius * 180.0 / Math.PI, lo = lon + de / (w.Radius * Math.Cos(lat * Math.PI / 180.0)) * 180.0 / Math.PI;
                    WeatherHere h = Model.Layers(w, la, lo, ut);
                    double mid = h.Low.Cover > 0.02f ? (h.Low.Base + h.Low.Top) * 0.5 : h.Middle.Cover > 0.02f ? (h.Middle.Base + h.Middle.Top) * 0.5 : 3000.0;
                    float c = Provider.CloudAt(w, la, lo, mid, ut);
                    char ch = x == 0 && y == 0 ? 'X' : h.Rain > 0.1f ? (h.Snow > 0.5f ? '~' : 'r') : shades[Math.Min(9, (int)(c * 9.99f))];
                    b.Append(ch);
                }
                b.Append('\n');
            }
            b.Append("(" + (t.ElapsedMilliseconds) + " ms for 1536 places; r = rain, ~ = snow, X = here)");
            return b.ToString();
        }

        public static string Timing()
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl)) return "nowhere";
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            var b = new StringBuilder();
            var t = Stopwatch.StartNew();
            for (int n = 0; n < 200; n++) Model.Sample(w, lat + n * 0.01, lon, ut);
            b.Append("Sample ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 200).ToString("F1")).Append(" us; ");
            t.Restart();
            for (int n = 0; n < 200; n++) Model.Layers(w, lat + n * 0.01, lon, ut);
            b.Append("Layers ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 200).ToString("F1")).Append(" us; ");
            t.Restart();
            for (int n = 0; n < 200; n++) Provider.CloudAt(w, lat + n * 0.01, lon, 1500.0, ut);
            b.Append("Cloud ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 200).ToString("F1")).Append(" us; ");
            Column col = Model.ColumnAt(w, lat, lon, ut);
            t.Restart();
            for (int n = 0; n < 1000; n++) Gusts.At(col, lat, lon, alt + n * 0.1, agl + n * 0.1f, ut + n * 0.02);
            b.Append("Gusts ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 1000).ToString("F1")).Append(" us; ");
            t.Restart();
            for (int n = 0; n < 20; n++) { var list = new System.Collections.Generic.List<WeatherCell>(); Model.Cells(w, lat + n * 0.2, lon, 25000.0, ut, list); }
            b.Append("Cells(25 km) ").Append((t.Elapsed.TotalMilliseconds / 20).ToString("F2")).Append(" ms; ");
            Model.Forget();
            t.Restart();
            for (int n = 0; n < 20; n++) Model.ColumnAt(w, lat + n * 0.2, lon, ut);
            b.Append("a new column ").Append((t.Elapsed.TotalMilliseconds / 20).ToString("F2")).Append(" ms");
            return b.ToString();
        }

        /// <summary>
        /// (For the development build: the model's atlas of the world against the weather asked place by place, at so many
        /// places all over the world (4000 by default) and at as many within 300 km of the craft: how many answers differ in
        /// their clouds, rain or storm, and what each way costs.)
        /// </summary>
        public static string Atlas(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat0, out double lon0, out _, out _)) return "nowhere";
            int count = string.IsNullOrEmpty(arg) ? 4000 : int.Parse(arg);
            World w = World.Of(body);
            double ut = Math.Floor(Planetarium.GetUniversalTime() / 2.0) * 2.0;
            var t = Stopwatch.StartNew();
            var atlas = new Model.Atlas(w, ut);
            double made = t.Elapsed.TotalMilliseconds;
            var rng = new System.Random(12345);
            var places = new double[count * 2][];
            double near = 300000.0 / (w.Radius * Math.PI / 180.0);
            for (int n = 0; n < places.Length; n++)
            {
                double la = n < count ? Math.Asin(rng.NextDouble() * 2.0 - 1.0) * 180.0 / Math.PI : lat0 + (rng.NextDouble() * 2.0 - 1.0) * near;
                double lo = n < count ? rng.NextDouble() * 360.0 - 180.0 : lon0 + (rng.NextDouble() * 2.0 - 1.0) * near;
                places[n] = new[] { Math.Max(-89.9, Math.Min(89.9, Math.Round(la * 20.0) / 20.0)), Math.Round(lo * 20.0) / 20.0 };
            }
            var mine = new WeatherHere[places.Length];
            t.Restart();
            for (int n = 0; n < places.Length; n++) mine[n] = atlas.At(places[n][0], places[n][1]);
            double fast = t.Elapsed.TotalMilliseconds * 1000.0 / places.Length;
            Model.Forget();
            t.Restart();
            int differ = 0, stormy = 0;
            string first = "";
            for (int n = 0; n < places.Length; n++)
            {
                WeatherHere a = mine[n], b = Model.Here(w, places[n][0], places[n][1], ut);
                if (b.Storm > 0.01f || b.Low.Kind == CloudKind.Cumulonimbus) stormy++;
                bool same = Same(a.Low, b.Low) && Same(a.Middle, b.Middle) && Same(a.High, b.High) && Math.Abs(a.Rain - b.Rain) < 0.004f && Math.Abs(a.Storm - b.Storm) < 0.03f;
                if (same) continue;
                differ++;
                if (first.Length == 0)
                    first = " (the first at " + places[n][0].ToString("F2") + ", " + places[n][1].ToString("F2") + ": low " + a.Low.Kind + " " + a.Low.Cover.ToString("F3") + " " + a.Low.Base.ToString("F0") + "-" + a.Low.Top.ToString("F0") + " against " + b.Low.Kind + " " + b.Low.Cover.ToString("F3") + " " + b.Low.Base.ToString("F0") + "-" + b.Low.Top.ToString("F0")
                            + ", rain " + a.Rain.ToString("F3") + " against " + b.Rain.ToString("F3") + ", storm " + a.Storm.ToString("F3") + " against " + b.Storm.ToString("F3") + ")";
            }
            double slow = t.Elapsed.TotalMilliseconds * 1000.0 / places.Length;
            return body.bodyName + ": the atlas made in " + made.ToString("F1") + " ms, " + (atlas.Storms < 0 ? "its storms not counted (each place finds its own)" : atlas.Storms + " storms and showers on the world") + "; " + places.Length + " places (half all over, half within 300 km): "
                + differ + " differ" + first + "; " + stormy + " of them have a storm near; a place costs " + fast.ToString("F1") + " us by the atlas, " + slow.ToString("F1") + " us asked by itself; so a map of 32,768 places " + ((made + fast * 32.768) / 1000.0).ToString("F2") + " s against " + (slow * 32.768 / 1000.0).ToString("F2") + " s";
        }

        static bool Same(CloudLayer a, CloudLayer b) =>
            a.Kind == b.Kind && Math.Abs(a.Cover - b.Cover) < 0.004f && (a.Cover < 0.01f || Math.Abs(a.Base - b.Base) < 32f && Math.Abs(a.Top - b.Top) < 64f && Math.Abs(a.Water - b.Water) < 0.004f);

        public static string City(string path) => NaturalWeather.City.FromFile(path);

        /// <summary>A city as if typed on the page (empty: forget it). Its weather is fetched from online within a moment.</summary>
        public static string CityName(string name) { NaturalWeather.City.Set(name ?? ""); return "city: '" + NaturalWeather.City.Typed + "'"; }

        /// <summary>What the city part says now.</summary>
        public static string CityNow() => NaturalWeather.City.Words + " | used: " + (Overrides.City != null ? Overrides.City.Words() : "none");

        /// <summary>A lightning strike on the craft now (as if the storm had found it).</summary>
        public static string Strike()
        {
            Lightning me = UnityEngine.Object.FindObjectOfType<Lightning>();
            if (me == null) return "not running";
            return me.StrikeNow();
        }

        /// <summary>The weather at the place every half hour of game time for `arg` Kerbin days (4 by default): to see how often each kind comes.</summary>
        public static string Days(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out _, out _)) return "nowhere";
            int days = string.IsNullOrEmpty(arg) ? 4 : int.Parse(arg);
            World w = World.Of(body);
            double ut0 = Planetarium.GetUniversalTime();
            var b = new StringBuilder();
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            int slots = (int)(days * w.Day / 1800.0), go = 0, rainy = 0, stormy = 0;
            for (int k = 0; k < slots; k++)
            {
                double ut = ut0 + k * 1800.0;
                WeatherHere h = Model.Here(w, lat, lon, ut);
                Launch.Verdict v = Launch.Judge(w, lat, lon, ut);
                string words = Panel.Words(h);
                string key = words.Split(',')[0];
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                if (v.Go) go++;
                if (h.Rain > 0.05f) rainy++;
                if (h.Storm > 0.2f) stormy++;
                Column col = Model.ColumnAt(w, lat, lon, ut);
                if (k < 48) b.Append((k * 0.5).ToString("F1")).Append("h ").Append(words).Append(", wind ").Append(new Vector2(col.UE, col.UN).magnitude.ToString("F1")).Append(", M ").Append(col.B.M.ToString("F2")).Append(" inst ").Append(col.B.Inst.ToString("F2")).Append(v.Go ? " GO" : " no go: " + v.First).Append("\n");
            }
            b.Append("over ").Append(days).Append(" days (").Append(slots).Append(" half hours): ");
            foreach (var kv in counts) b.Append(kv.Key).Append(" ").Append(kv.Value * 100 / slots).Append("%, ");
            b.Append("rain ").Append(rainy * 100 / slots).Append("%, storms near ").Append(stormy * 100 / slots).Append("%, launch go ").Append(go * 100 / slots).Append("%");
            return b.ToString();
        }

        /// <summary>
        /// (For the development build: how windy it is near the ground where the craft is, over so many days from now, every ten minutes
        /// of game time, the weather as it comes: the share of the time the mean wind 2 m and 10 m up is calm (under 0.5 m/s), light (to
        /// 2), gentle (to 5), moderate (to 10) or strong; and 2 m up by night and by day, and with rain or storms about.)
        /// </summary>
        public static string Winds(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out _, out _)) return "nowhere";
            int days = string.IsNullOrEmpty(arg) ? 8 : int.Parse(arg);
            World w = World.Of(body);
            double ut0 = Planetarium.GetUniversalTime();
            int slots = (int)(days * w.Day / 600.0);
            float[] edges = { 0.5f, 2f, 5f, 10f };
            int[] at2 = new int[5], at10 = new int[5], night2 = new int[5], day2 = new int[5], wet2 = new int[5];
            int nights = 0, daysN = 0, wets = 0;
            double sum2 = 0, sum10 = 0, sumGust = 0, most2 = 0;
            Func<float, int> bin = v => { int k = 0; while (k < edges.Length && v >= edges[k]) k++; return k; };
            for (int k = 0; k < slots; k++)
            {
                double ut = ut0 + k * 600.0;
                Column col = Model.ColumnAt(w, lat, lon, ut);
                float g = col.B.Ground;
                float v2 = Model.Mean(col, g + 2.0, 2f).magnitude, v10 = Model.Mean(col, g + 10.0, 10f).magnitude;
                at2[bin(v2)]++; at10[bin(v10)]++;
                sum2 += v2; sum10 += v10; sumGust += col.Here.Gusts; most2 = Math.Max(most2, v2);
                if (col.B.SunUp > 0f) { day2[bin(v2)]++; daysN++; } else { night2[bin(v2)]++; nights++; }
                if (col.Here.Rain > 0.05f || col.Here.Storm > 0.2f) { wet2[bin(v2)]++; wets++; }
            }
            Func<int[], int, string> share = (c, n) =>
            {
                if (n == 0) return "none";
                var s = new StringBuilder();
                string[] names = { "calm", "light", "gentle", "moderate", "strong" };
                for (int i = 0; i < 5; i++) s.Append(names[i]).Append(" ").Append((100.0 * c[i] / n).ToString("F1")).Append("% ");
                return s.ToString().TrimEnd();
            };
            return "over " + days + " days (" + slots + " times, ten minutes apart)" + (Model.OldWind ? ", the wind near the ground as before 0.2.0" : "")
                + ": 2 m up " + share(at2, slots) + "; 10 m up " + share(at10, slots) + "; 2 m up by night " + share(night2, nights) + "; by day " + share(day2, daysN)
                + "; with rain or storms about (" + (100.0 * wets / slots).ToString("F0") + "% of the time) " + share(wet2, wets)
                + "; mean 2 m " + (sum2 / slots).ToString("F2") + " m/s, 10 m " + (sum10 / slots).ToString("F2") + ", most at 2 m " + most2.ToString("F1") + "; gusts (1 sigma at 10 m) " + (sumGust / slots).ToString("F2")
                + " (knobs: night " + Model.NightNear + ", fair day " + Model.FairDayNear + ", prevailing " + Model.Prevail + ", sea breeze " + Model.Breeze + ")";
        }

        /// <summary>(For the development build: "night fairday prevailing breeze" for the wind near the ground, or "old" for it as before 0.2.0, or "new".)</summary>
        public static string WindKnobs(string arg)
        {
            string[] p = (arg ?? "").Trim().Split(' ');
            if (p[0] == "old") Model.OldWind = true;
            else if (p[0] == "new") Model.OldWind = false;
            else
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (p.Length > 0 && p[0].Length > 0) Model.NightNear = float.Parse(p[0], inv);
                if (p.Length > 1) Model.FairDayNear = float.Parse(p[1], inv);
                if (p.Length > 2) Model.Prevail = float.Parse(p[2], inv);
                if (p.Length > 3) Model.Breeze = float.Parse(p[3], inv);
                Model.OldWind = false;
            }
            Model.Forget();
            return (Model.OldWind ? "as before 0.2.0" : "night " + Model.NightNear + ", fair day " + Model.FairDayNear + ", prevailing " + Model.Prevail + ", sea breeze " + Model.Breeze);
        }

        /// <summary>The root part's airspeed as the game's drag sums have it now, against the craft's ground speed and the wind on it: the two must differ by the wind.</summary>
        public static string Drag()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            Part p = v.rootPart;
            Vector3 wind = Aero.OnCraft(v), ground = (Vector3)v.srf_velocity;
            return "dragVector " + p.dragVector.ToString("F2") + " (" + p.dragVector.magnitude.ToString("F2") + " m/s); ground speed " + ground.ToString("F2") + "; wind on the craft " + wind.ToString("F2") + "; ground less wind " + (ground - wind).ToString("F2")
                + "; dynamic pressure " + p.dynamicPressurekPa.ToString("F4") + " kPa, drag " + p.dragScalar.ToString("F3") + " kN";
        }

        public static string Wind()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            Vector3 on = Aero.OnCraft(v);
            Provider.Axes(v.mainBody, v.CoMD, out Vector3d e, out Vector3d n, out Vector3d u);
            return "wind on " + v.GetDisplayName() + ": " + on.magnitude.ToString("F2") + " m/s (east " + Vector3d.Dot(on, e).ToString("F2") + ", north " + Vector3d.Dot(on, n).ToString("F2") + ", up " + Vector3d.Dot(on, u).ToString("F2") + "); ground speed " + v.srfSpeed.ToString("F2") + " m/s, "
                + (v.LandedOrSplashed ? "landed" : "flying") + "; " + Aero.Report;
        }
    }
}
#endif
