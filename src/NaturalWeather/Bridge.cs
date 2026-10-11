using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// For the AI Bridge (which does not need this mod: it finds these by name, and goes without when they are not there):
    /// the weather where the craft is, in a form it can hand on as it is, the launch go / no go with its forecast, and the
    /// wind on the craft for its autopilots.
    /// </summary>
    public static class Bridge
    {
        static double R(double v, int d = 1) => Math.Round(v, d);

        /// <summary>One line: "Rain, wind 6 m/s from the east; launch NO GO (precipitation)".</summary>
        public static string Brief()
        {
            if (Weather.On == null || !Weather.On) return null;
            string s = Weather.Status;
            Launch.Verdict v = Launch.Now;
            if (Launch.Where.Length > 0 && v != null && v.Rules.Count > 1) s += " Launch " + (v.Go ? "GO" : "NO GO (" + v.First.ToLowerInvariant() + ")") + ".";
            return s;
        }

        /// <summary>The wind on a craft now, at its middle, gusts and all: world space, m/s (zero where wind does not move craft).</summary>
        public static Vector3d Wind(Vessel v)
        {
            if (v == null || Weather.On == null || !Weather.On) return Vector3d.zero;
            return (Vector3d)Aero.Pushing(v);
        }

        /// <summary>Everything, for the bridge's "weather" call.</summary>
        public static Dictionary<string, object> Report(Vessel vessel)
        {
            var o = new Dictionary<string, object>();
            o["mod"] = "Natural Weather " + Weather.Version;
            if (Weather.On == null || !Weather.On) { o["on"] = false; o["note"] = "Natural Weather is switched off: still air and clear skies."; return o; }
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl)) { o["note"] = "nowhere to read the weather"; return o; }
            o["where"] = HighLogic.LoadedSceneIsFlight && vessel != null ? "the craft, " + body.bodyName : "the space centre";
            if (!body.atmosphere) { o["note"] = "no air here: no weather"; return o; }
            if (!Model.Allowed(body)) { o["note"] = "weather is switched off on " + body.bodyName; return o; }
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            WeatherHere h = Model.Here(w, lat, lon, ut);
            o["sky"] = Panel.Words(h);
            o["rain"] = R(h.Rain, 2); o["snow_share"] = R(h.Snow, 2); o["storms_near"] = R(h.Storm, 2); o["fog"] = R(h.Fog, 2); o["dust"] = R(h.Dust, 2);
            o["visibility_m"] = Math.Round(h.Seeing);
            o["temperature_c"] = R(h.Temperature - 273.15, 1);
            var layers = new List<object>();
            foreach (CloudLayer l in new[] { h.Low, h.Middle, h.High })
                if (l.Kind != CloudKind.None && l.Cover > 0.05f)
                    layers.Add(new Dictionary<string, object> { { "kind", l.Kind.ToString().ToLowerInvariant() }, { "cover", R(l.Cover, 2) }, { "base_m", Math.Round(l.Base) }, { "top_m", Math.Round(l.Top) } });
            o["clouds"] = layers;
            Vector3 at10 = Provider.Local(w, lat, lon, alt - agl + 10.0, 10f, ut, false);
            o["wind_10m"] = Wind(at10);
            o["gusts_ms"] = R(h.Gusts, 1);
            if (HighLogic.LoadedSceneIsFlight && vessel != null)
            {
                Vector3 there = Provider.Local(w, lat, lon, alt, agl, ut, false);
                o["wind_at_craft"] = Wind(there);
                o["wind_moves_craft"] = Weather.Push && (Weather.Tip || !vessel.LandedOrSplashed);
                if (Weather.Push)
                {
                    Vector3 on = Aero.OnCraft(vessel);
                    Provider.Axes(body, vessel.CoMD, out Vector3d e, out Vector3d n, out Vector3d u);
                    o["wind_on_craft_now"] = new Dictionary<string, object> { { "east_ms", R(Vector3d.Dot(on, e)) }, { "north_ms", R(Vector3d.Dot(on, n)) }, { "up_ms", R(Vector3d.Dot(on, u)) }, { "note", "gusts and all, this moment" } };
                    Vector3d air = vessel.srf_velocity - (Vector3d)on;
                    o["airspeed_ms"] = R(air.magnitude);
                    o["ground_speed_ms"] = R(vessel.srfSpeed);
                }
            }
            // the wind aloft, every kilometre or two up to the jet stream
            Column col = Model.ColumnAt(w, lat, lon, ut);
            var aloft = new List<object>();
            foreach (float km in new[] { 0.5f, 1f, 2f, 4f, 6f, 8f, 10f, 12f, 15f, 20f })
            {
                double hh = km * 1000.0;
                if (hh > body.atmosphereDepth * 0.6) break;
                Vector3 m = Model.Mean(col, hh, (float)Math.Max(1.0, hh - col.B.Ground));
                var d = Wind(m);
                d["altitude_km"] = km;
                aloft.Add(d);
            }
            o["wind_aloft"] = aloft;
            if (Launch.Where.Length > 0)
            {
                Launch.Verdict v = Launch.Now;
                var rules = new List<object>();
                foreach (Launch.Rule r in v.Rules) rules.Add(new Dictionary<string, object> { { "rule", r.Name }, { "now", r.Value }, { "limit", r.Limit }, { "go", r.Go } });
                var forecast = new List<object>();
                foreach (Launch.Slot s in Launch.Forecast)
                    forecast.Add(new Dictionary<string, object> { { "minutes_ahead", Math.Round((s.Ut - ut) / 60.0) }, { "go", s.Go }, { "chance_of_no_go_pct", s.Chance }, { "why", s.Why } });
                double next = Launch.NextGo();
                o["launch"] = new Dictionary<string, object> { { "at", Launch.Where }, { "go", v.Go }, { "first_reason", v.First }, { "rules", rules }, { "forecast", forecast }, { "next_go_minutes", next >= 0.0 ? (object)Math.Round(next / 60.0) : "not within three hours" } };
            }
            if (Lightning.LastStrike.Length > 0) o["last_lightning_strike"] = Lightning.LastStrike;
            if (Overrides.City != null && Overrides.UseCity) o["real_weather_from"] = Overrides.City.Name;
            return o;
        }

        static Dictionary<string, object> Wind(Vector3 enu)
        {
            float s = new Vector2(enu.x, enu.y).magnitude;
            return new Dictionary<string, object> { { "speed_ms", R(s) }, { "from_deg", s < 0.3f ? 0.0 : Math.Round((Math.Atan2(-enu.x, -enu.y) * 180.0 / Math.PI + 360.0) % 360.0) }, { "from", s < 0.3f ? "calm" : CityWeather.Compass(Mathf.Atan2(-enu.x, -enu.y) * Mathf.Rad2Deg) } };
        }
    }
}
