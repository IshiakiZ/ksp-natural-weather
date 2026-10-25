using System;
using System.Text;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>The mod's page, under its settings: the weather now, the launch go / no go with its forecast, and the city.</summary>
    static class Panel
    {
        static string typing;

        public static void Draw()
        {
            // ---- (since 0.6.0) the weather centre
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Centre.Open ? "Shut the weather centre" : "Open the weather centre", GUILayout.Width(190f))) Centre.Toggle();
            GUILayout.Label("<color=#aaaaaa>maps, radar, fronts, storms' paths, the forecast" + (Weather.CentreKey != null && Weather.CentreKey.Code != KeyCode.None ? "  ·  " + Weather.CentreKey.Shown : "") + "</color>", Host.Small);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            // ---- the weather now
            if (WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl) && body.atmosphere && Weather.On && Model.Allowed(body))
            {
                World w = World.Of(body);
                double ut = Planetarium.GetUniversalTime();
                WeatherHere here = Model.Here(w, lat, lon, ut);
                Vector3 at10 = Provider.Local(w, lat, lon, alt - agl + 10.0, 10f, ut, false);
                Vector3 there = Provider.Local(w, lat, lon, alt, agl, ut, false);
                bool flight = HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null;
                GUILayout.Label("<b>" + (flight ? "Where the craft is" : "At the space centre") + "</b>: " + Words(here) + ".", Host.Rich);
                GUILayout.Label("Wind at 10 m " + Wind(at10) + ", gusts of " + here.Gusts.ToString("F1") + " m/s" + (flight && agl > 30f ? "; at the craft's height (" + (agl / 1000f).ToString(agl > 1000f ? "F1" : "F2") + " km above the ground) " + Wind(there) : "") + ".", Host.Small);
                GUILayout.Label(Clouds(here) + "  " + (here.Temperature - 273.15f).ToString("F0") + " °C at the ground; can see " + Far(here.Seeing) + ".", Host.Small);
                if (flight && Weather.Push) { Vector3 on = Aero.OnCraft(FlightGlobals.ActiveVessel); GUILayout.Label("The wind on the craft now, gusts and all: " + on.magnitude.ToString("F1") + " m/s" + (Mathf.Abs(Vector3.Dot(on, (Vector3)FlightGlobals.ActiveVessel.upAxis)) > 1.5f ? " (" + Vector3.Dot(on, (Vector3)FlightGlobals.ActiveVessel.upAxis).ToString("+0.0;-0.0") + " m/s of it up)" : "") + ".", Host.Small); }
                string violent = Violent.Says(w, lat, lon, ut);
                if (violent.Length > 0) GUILayout.Label("<color=#ff8a7a>" + violent + "</color>", Host.Rich);
                if (flight && Icing.Report.Length > 0) GUILayout.Label("<color=#a8d8ff>" + Icing.Report + "</color>", Host.Rich);
            }
            else GUILayout.Label(Weather.Status, Host.Small);
            if (Aero.Report.Length > 0) GUILayout.Label(Aero.Report, Host.Small);
            if (Feed.Report.Length > 0) GUILayout.Label(Feed.Report, Host.Small);

            // ---- launch weather
            Launch.Verdict v = Launch.Now;
            if (Launch.Where.Length > 0 && v != null && v.Rules.Count > 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label("<b>Launch weather</b> at " + Launch.Where + ": " + (v.Go ? "<color=#8fd18f>GO</color>" : "<color=#ff8a7a>NO GO</color> (" + v.First.ToLowerInvariant() + ")"), Host.Rich);
                foreach (Launch.Rule r in v.Rules)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(r.Go ? "<color=#8fd18f>go</color>" : "<color=#ff8a7a>no go</color>", Host.Small, GUILayout.Width(44f));
                    GUILayout.Label("<b>" + r.Name + "</b>: " + r.Value + (r.Limit.Length > 0 ? "  <color=#aaaaaa>(" + r.Limit + ")</color>" : ""), Host.Small);
                    GUILayout.EndHorizontal();
                }
                Launch.Slot[] f = Launch.Forecast;
                if (f.Length > 0)
                {
                    double now = Planetarium.GetUniversalTime();
                    var line = new StringBuilder("Forecast, chance of no go:  ");
                    for (int k = 0; k < f.Length; k += 2)
                    {
                        double ahead = f[k].Ut - now;
                        if (ahead < -300.0) continue;
                        line.Append(Mins(ahead)).Append(" ").Append(f[k].Chance >= 50 ? "<color=#ff8a7a>" : f[k].Chance >= 20 ? "<color=#e8d17a>" : "<color=#8fd18f>").Append(f[k].Chance).Append("%</color>   ");
                    }
                    GUILayout.Label(line.ToString(), Host.Small);
                    double next = Launch.NextGo();
                    if (!v.Go) GUILayout.Label(next >= 0.0 ? "Next likely go: in " + Mins(next) + "." : "No go is forecast within three hours.", Host.Small);
                }
            }

            // ---- the city
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("City", GUILayout.Width(40f));
            string name = Host.TypedName + "Natural Weather:city";
            Event e = Event.current;
            bool had = GUI.GetNameOfFocusedControl() == name;
            bool done = had && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (done) e.Use();
            GUI.SetNextControlName(name);
            typing = GUILayout.TextField(typing ?? City.Typed, 60, GUILayout.Width(200f));
            if (had && e.type == EventType.MouseDown && !GUILayoutUtility.GetLastRect().Contains(e.mousePosition)) done = true;
            if (GUILayout.Button(typing != null && typing.Trim() != City.Typed ? "Use" : "Again", GUILayout.Width(56f)) || done)
            {
                GUIUtility.keyboardControl = 0;
                if (typing != null && typing.Trim() != City.Typed) City.Set(typing);
                typing = null;
            }
            if (City.Typed.Length > 0 && GUILayout.Button("Forget", GUILayout.Width(60f))) { City.Set(""); typing = null; }
            GUILayout.EndHorizontal();
            if (City.Words.Length > 0) GUILayout.Label(City.Words, Host.Small);
            GUILayout.Label("<color=#aaaaaa>A city typed here is sent to Open-Meteo (open-meteo.com) to find it and fetch its weather, every quarter of an hour while the game runs; nothing else is sent. Empty: nothing at all. Weather data by Open-Meteo.com, CC BY 4.0.</color>", Host.Small);
        }

        public static string Words(WeatherHere h)
        {
            string sky;
            float cover = h.Cover;
            if (h.Fog > 0.4f) sky = (h.FogFreezes ? "freezing " : "") + (h.FogOf == FogKind.Radiation ? "ground fog" : h.FogOf == FogKind.Advection ? "sea fog" : h.FogOf == FogKind.Upslope ? "hill fog" : h.FogOf == FogKind.Steam ? "steam fog on the water" : h.FogOf == FogKind.Frontal ? "rain and fog" : "fog");
            else if (h.Fog > 0.12f && h.FogOf == FogKind.Radiation) sky = "mist in the low ground";
            else if (h.Dust > 0.4f) sky = h.Dust > 0.75f ? "a dust storm" : "dust in the air";
            else if (h.Low.Kind == CloudKind.Cumulonimbus) sky = "thunderstorms";
            else if (cover < 0.08f) sky = "clear";
            else if (cover < 0.3f) sky = h.Low.Kind == CloudKind.Cumulus ? "fair, with cumulus" : "mostly clear";
            else if (cover < 0.65f) sky = "partly cloudy";
            else if (cover < 0.9f) sky = "mostly cloudy";
            else sky = "overcast";
            if (h.Rain > 0.03f)
            {
                sky += ", " + Falling(h);
            }
            if (h.Hail > 0.05f) sky += ", hail" + (h.HailSize >= 15f ? " (stones of " + Mathf.RoundToInt(h.HailSize) + " mm)" : "");
            if (h.Storm > 0.2f && h.Low.Kind != CloudKind.Cumulonimbus) sky += ", storms nearby";
            return char.ToUpperInvariant(sky[0]) + sky.Substring(1);
        }

        /// <summary>What falls, in a word or two: "light rain", "freezing rain", "heavy snow".</summary>
        public static string Falling(WeatherHere h)
        {
            // (sleet as in North America: ice pellets; a fall that is part rain and part snow is called that)
            string what = h.Freezing > 0.5f ? "freezing rain" : h.Pellets > 0.5f ? "sleet (ice pellets)" : h.Snow > 0.6f ? "snow" : h.Snow > 0.25f ? "rain and snow" : h.Rain < 0.1f ? (h.Freezing > 0.25f ? "freezing drizzle" : "drizzle") : "rain";
            string much = h.Rain > 0.6f ? "heavy " : h.Rain < 0.15f && !what.EndsWith("drizzle") ? "light " : "";
            return much + what;
        }

        static string Wind(Vector3 v)
        {
            float s = new Vector2(v.x, v.y).magnitude;
            if (s < 0.5f) return "calm";
            return s.ToString("F1") + " m/s from the " + CityWeather.Compass(Mathf.Atan2(-v.x, -v.y) * Mathf.Rad2Deg);
        }

        static string Clouds(WeatherHere h)
        {
            var b = new StringBuilder();
            foreach (CloudLayer l in new[] { h.Low, h.Middle, h.High })
            {
                if (l.Kind == CloudKind.None || l.Cover < 0.05f) continue;
                if (b.Length > 0) b.Append("; ");
                b.Append(l.Kind.ToString().ToLowerInvariant()).Append(" ").Append(Mathf.RoundToInt(l.Cover * 100f)).Append("% from ").Append((l.Base / 1000f).ToString("F1")).Append(" to ").Append((l.Top / 1000f).ToString("F1")).Append(" km");
            }
            return b.Length > 0 ? "Cloud: " + b + "." : "No cloud.";
        }

        static string Far(float m) => m >= 10000f ? (m / 1000f).ToString("F0") + " km" : m >= 1000f ? (m / 1000f).ToString("F1") + " km" : Mathf.RoundToInt(m / 10f) * 10 + " m";

        static string Mins(double s)
        {
            if (s < 60.0) return "now";
            int m = (int)Math.Round(s / 60.0);
            return m < 60 ? m + "m" : (m / 60) + "h" + (m % 60 > 0 ? (m % 60).ToString("00") : "");
        }
    }
}
