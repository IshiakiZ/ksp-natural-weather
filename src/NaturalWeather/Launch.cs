using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// Launch weather: "go" or "no go" for the pad the craft stands on (or the space centre's), by rules adapted from NASA's
    /// for a crewed Falcon 9 (fact sheet FS-2020-05-568-KSC), and a forecast for the next three hours. Advice only: nothing
    /// stops a launch, unless lightning strikes are switched on and the rocket flies into one.
    /// </summary>
    static class Launch
    {
        public struct Rule { public string Name, Value, Limit; public bool Go; }

        public sealed class Verdict
        {
            public bool Go = true;
            public readonly List<Rule> Rules = new List<Rule>();
            public string First = "";          // the first rule it fails
        }

        public struct Slot { public double Ut; public bool Go; public int Chance; public string Why; }

        public static Verdict Now = new Verdict();
        public static Slot[] Forecast = new Slot[0];
        public static string Where = "";
        static float judgedAt, forecastAt;
        static Task<Verdict> judging;
        static Task<Slot[]> working;

        // the limits
        const float PadWind = 13.4f;           // 30 mph, sustained, 162 ft (49 m) up the pad
        const float Shear = 8f;                // m/s a kilometre, between half and one and a half times the jet's height
        const double Lightning = 18520.0;      // 10 nautical miles
        const double Lookback = 1800.0;        // 30 minutes
        const float Thick = 1372f;             // 4,500 ft

        /// <summary>Kept up to date from the game's thread, the work done on others: the verdict now every few seconds, the forecast once a minute.</summary>
        public static void Tick()
        {
            if (judging != null && judging.IsCompleted) { if (!judging.IsFaulted) Now = judging.Result; judging = null; }
            if (working != null && working.IsCompleted) { if (!working.IsFaulted) Forecast = working.Result; working = null; }
            if (Time.realtimeSinceStartup < judgedAt) return;
            judgedAt = Time.realtimeSinceStartup + 4f;
            if (!Pad(out CelestialBody body, out double lat, out double lon, out string where)) { Where = ""; return; }
            Where = where;
            if (!body.atmosphere || !Model.Allowed(body)) { Now = new Verdict(); Now.Rules.Add(new Rule { Name = "Weather", Value = "none here", Limit = "", Go = true }); Forecast = new Slot[0]; return; }
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            if (judging == null) judging = Task.Run(() => Judge(w, lat, lon, ut));
            if (working == null && Time.realtimeSinceStartup > forecastAt)
            {
                forecastAt = Time.realtimeSinceStartup + 60f;
                working = Task.Run(() => Ahead(w, lat, lon, ut));
            }
        }

        /// <summary>Forget the forecast (a setting changed): it is worked out again at once.</summary>
        public static void Again() { forecastAt = 0f; judgedAt = 0f; }

        /// <summary>Where launch weather is judged: the craft, where it stands on the ground; else the space centre's pad.</summary>
        static bool Pad(out CelestialBody body, out double lat, out double lon, out string where)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (HighLogic.LoadedSceneIsFlight && v != null && v.LandedOrSplashed)
            {
                body = v.mainBody; lat = v.latitude; lon = v.longitude;
                where = (string.IsNullOrEmpty(v.landedAt) ? "where " + v.GetDisplayName() + " stands" : Localize(v.landedAt)) + " on " + body.bodyName;
                return true;
            }
            body = FlightGlobals.GetHomeBody();
            lat = -0.0972; lon = -74.5577; where = "the space centre's launch pad";
            return body != null;
        }

        static string Localize(string s)
        {
            try { return KSP.Localization.Localizer.Format(s); } catch (Exception) { return s; }
        }

        /// <summary>The rules, at a place and time. May run on any thread.</summary>
        public static Verdict Judge(World w, double lat, double lon, double ut)
        {
            var v = new Verdict();
            Column col = Model.ColumnAt(w, lat, lon, ut);
            WeatherHere here = col.Here;
            float ground = col.B.Ground;
            // 1. wind at the pad
            Vector3 at50 = Model.Mean(col, ground + 49.0, 49f);
            float sigma = Model.Sigmas(col, ground + 49.0, 49f, out _, out _, out _);
            float wind = new Vector2(at50.x, at50.y).magnitude;
            Add(v, "Wind at the pad", wind.ToString("F1") + " m/s (gusts to " + (wind + 2.5f * sigma).ToString("F0") + ")", "no more than " + PadWind.ToString("F1") + " m/s sustained, 49 m up", wind <= PadWind);
            // 2. shear aloft, between half and one and a half times the jet's height
            float worst = 0f, worstAt = 0f;
            float lo = Math.Max(ground + 1500f, col.JetH * 0.5f), hi = col.JetH * 1.5f;
            Vector3 prev = Model.Mean(col, lo, lo - ground);
            for (float h = lo + 500f; h <= hi; h += 500f)
            {
                Vector3 now = Model.Mean(col, h, h - ground);
                float s = (now - prev).magnitude * 2f;
                if (s > worst) { worst = s; worstAt = h; }
                prev = now;
            }
            Add(v, "Wind shear aloft", worst.ToString("F1") + " m/s a km at " + (worstAt / 1000f).ToString("F0") + " km", "no more than " + Shear.ToString("F0") + " m/s a km", worst <= Shear);
            // 3. lightning within 10 nautical miles in the last 30 minutes, and 4. a storm's anvil within 10 nautical miles now
            bool flashed = false, anvil = false;
            double nearest = double.MaxValue;
            var cells = new List<WeatherCell>();
            for (int k = 0; k <= 6; k++)
            {
                cells.Clear();
                double t = ut - k * Lookback / 6.0;
                Model.Cells(w, lat, lon, Lightning + 15000.0, t, cells);
                foreach (WeatherCell c in cells)
                {
                    if (!c.Thunder) continue;
                    double d = Model.Distance(w, lat, lon, c.Latitude, c.Longitude);
                    if (c.Strength > 0.25f && d - c.Radius < Lightning) { flashed = true; nearest = Math.Min(nearest, d); }
                    if (k == 0 && c.Strength > 0.4f && d - 2.0 * c.Radius < Lightning) anvil = true;
                }
            }
            Add(v, "Lightning", flashed ? "within " + (Math.Max(0.0, nearest) / 1000.0).ToString("F0") + " km in the last 30 minutes" : "none near", "none within 18.5 km (10 nautical miles) for 30 minutes", !flashed);
            Add(v, "Storm anvils", anvil ? "within 18.5 km" : "none near", "none within 18.5 km", !anvil);
            // 5. what falls
            Add(v, "Precipitation", here.Rain > 0.03f ? (here.Snow > 0.5f ? "snow" : "rain") + " at the pad" : "none", "none", here.Rain <= 0.03f);
            // 6. a thick layer of cloud that reaches freezing, and 7. heaped cloud with its top above freezing
            float freeze = col.B.Freeze;
            bool thick = false, heaped = false;
            foreach (CloudLayer l in new[] { here.Low, here.Middle, here.High })
            {
                if (l.Kind == CloudKind.None || l.Cover < 0.5f) continue;
                if (l.Top - l.Base > Thick && l.Top > freeze && l.Kind != CloudKind.Cumulus && l.Kind != CloudKind.Cumulonimbus && l.Kind != CloudKind.Cirrus) thick = true;
            }
            if ((here.Low.Kind == CloudKind.Cumulus || here.Low.Kind == CloudKind.Cumulonimbus) && here.Low.Cover > 0.1f && here.Low.Top > freeze) heaped = true;
            Add(v, "Thick cloud", thick ? "a layer over 1.4 km thick into freezing air" : "none", "no layer over 1,400 m thick that reaches freezing", !thick);
            Add(v, "Towering cumulus", heaped ? "tops at " + (here.Low.Top / 1000f).ToString("F1") + " km, freezing at " + (freeze / 1000f).ToString("F1") + " km" : "none", "no heaped cloud with its top above freezing", !heaped);
            return v;
        }

        static void Add(Verdict v, string name, string value, string limit, bool go)
        {
            v.Rules.Add(new Rule { Name = name, Value = value, Limit = limit, Go = go });
            if (!go && v.Go) { v.Go = false; v.First = name; }
        }

        /// <summary>The next three hours in slots of ten minutes: go or not, and how likely a "no go" is (from the weather a little earlier and later, a little to either side).</summary>
        static Slot[] Ahead(World w, double lat, double lon, double ut)
        {
            var slots = new Slot[19];
            for (int k = 0; k < slots.Length; k++)
            {
                double t = ut + k * 600.0;
                Verdict main = Judge(w, lat, lon, t);
                int bad = main.Go ? 0 : 1, all = 1;
                for (int s = 0; s < 4; s++)
                {
                    double dt = (s - 1.5) * 300.0, dl = (s % 2 == 0 ? 1 : -1) * 0.06;
                    Verdict near = Judge(w, lat + dl, lon - dl, t + dt);
                    if (!near.Go) bad++;
                    all++;
                }
                slots[k] = new Slot { Ut = t, Go = main.Go, Chance = bad * 100 / all, Why = main.First };
            }
            return slots;
        }

        /// <summary>When the forecast next says "go", from now; or a negative number if not within three hours.</summary>
        public static double NextGo()
        {
            Slot[] f = Forecast;
            double now = Planetarium.GetUniversalTime();
            foreach (Slot s in f) if (s.Go && s.Ut >= now - 300.0) return Math.Max(0.0, s.Ut - now);
            return -1.0;
        }
    }
}
