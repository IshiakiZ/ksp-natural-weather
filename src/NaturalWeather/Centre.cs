using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0.) The weather centre: a window with the weather as a forecaster's tools show it (docs/weather-window-plan.md;
    /// the owner, 2026-10-21: "a modal that gives a whole bunch of weather data, so you can see weather paths and everything.
    /// Basically, like real life"). Opened from the mod's page and by its key. In tabs: the observation where the craft is (or at
    /// the space centre) as a METAR and in words, with launch weather and warnings; a map (satellite, radar, the surface analysis,
    /// wind, lightning, the systems' paths and the storms' tracks, from three hours back to a day ahead); the forecast hour by hour
    /// with a TAF; a sounding of the air above; and every storm of note. All of it the model's own weather, worked out on other
    /// threads while the window is open, and nothing at all while it is shut.
    /// </summary>
    static class Centre
    {
        static Sheet sheet;
        static int tab;
        static readonly string[] Tabs = { "Now", "Map", "Forecast", "Sounding", "Storms" };

        /// <summary>Whether it is open.</summary>
        public static bool Open => sheet != null && sheet.Open;

        public static void Toggle()
        {
            if (sheet == null) { sheet = new Sheet("Weather centre", 930f, 690f, Draw) { Shade = 0.88f }; sheet.Closed += Shut; }
            sheet.Open = !sheet.Open;
        }

        static void Shut()
        {
            if (mapTexture != null) { UnityEngine.Object.Destroy(mapTexture); mapTexture = null; }
            shown = null; mapWork = null; nowWork = null; now = null;
            Meteogram.Shut(); Sounding.Shut(); StormList.Shut();
        }

        /// <summary>Each frame, from the weather's driver: the key, and the window's work handed over as it is done.</summary>
        public static void Tick()
        {
            if (Weather.CentreKey != null && Weather.CentreKey.Pressed) Toggle();
            if (!Open) return;
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl) || !body.atmosphere || !Model.Allowed(body)) return;
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            switch (tab)
            {
                case 0: TickNow(w, lat, lon, ut); break;
                case 1: TickMap(w, lat, lon, ut); break;
                case 2: Meteogram.Tick(w, lat, lon, ut); break;
                case 3: Sounding.Tick(w, lat, lon, alt, ut); break;
                case 4: StormList.Tick(w, lat, lon, ut); break;
            }
        }

        static GUIStyle tag, tagBig;
        static GUIStyle Tag => tag ?? (tag = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = false, richText = false, padding = new RectOffset(0, 0, 0, 0) });
        static GUIStyle TagBig => tagBig ?? (tagBig = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) });

        static void Draw(Rect room)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++)
                if (GUILayout.Toggle(tab == i, Tabs[i], GUI.skin.button, GUILayout.Width(96f)) && tab != i) tab = i;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl) || !body.atmosphere || !Weather.On || !Model.Allowed(body))
            {
                GUILayout.Label(body != null && !body.atmosphere ? "No air here, so no weather: " + body.bodyName + "." : Weather.Status, Host.Rich);
                return;
            }
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            GUILayout.Label(Where(w, lat, lon, ut), Host.Small);
            switch (tab)
            {
                case 0: DrawNow(w, lat, lon, ut); break;
                case 1: DrawMap(w, lat, lon, ut, room); break;
                case 2: Meteogram.Draw(w, lat, lon, ut, room); break;
                case 3: Sounding.Draw(w, lat, lon, ut, room); break;
                case 4: StormList.Draw(w, lat, lon, ut, room); break;
            }
        }

        /// <summary>Where the weather is read and when: the craft (or the space centre), the world, the place, the date.</summary>
        static string Where(World w, double lat, double lon, double ut)
        {
            bool flight = HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null;
            string station = Synoptic.Station(w, lat, lon);
            string what = station == "KKSC" ? "the Kerbal Space Center (KKSC)" : station == "KISL" ? "the Island Airfield (KISL)" : flight ? FlightGlobals.ActiveVessel.GetDisplayName() : "the space centre";
            return "<b>" + what + "</b>, " + w.Body.bodyName + ", " + Deg(lat, "N", "S") + " " + Deg(lon, "E", "W") + "  ·  " + KSPUtil.PrintDate(ut, true, false) + "  ·  " + Synoptic.DayTime(ut) + "Z";
        }

        public static string Deg(double v, string pos, string neg) => Math.Abs(v).ToString("F2") + "° " + (v >= 0.0 ? pos : neg);

        /// <summary>Text with a dark edge round it, so that it reads over any picture.</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style, Color colour)
        {
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x - 1f, r.y, r.width, r.height), text, style);
            GUI.color = colour;
            GUI.Label(r, text, style);
            GUI.color = was;
        }

        // ---------------------------------------------------------------- now

        sealed class NowSaid { public string Metar = "", Words = "", Warnings = ""; public double Ut; }
        static NowSaid now;
        static Task<NowSaid> nowWork;
        static float nowAt;

        static void TickNow(World w, double lat, double lon, double ut)
        {
            if (nowWork != null && nowWork.IsCompleted) { if (!nowWork.IsFaulted) now = nowWork.Result; else Weather.Log("the observation failed: " + nowWork.Exception?.GetBaseException().Message); nowWork = null; }
            if (nowWork == null && Time.realtimeSinceStartup > nowAt)
            {
                nowAt = Time.realtimeSinceStartup + 2f;
                nowWork = Task.Run(() =>
                {
                    var said = new NowSaid { Ut = ut };
                    said.Metar = Synoptic.Metar(w, lat, lon, ut, out said.Words);
                    said.Warnings = Violent.Says(w, lat, lon, ut);
                    return said;
                });
            }
        }

        static void DrawNow(World w, double lat, double lon, double ut)
        {
            GUILayout.Space(4f);
            GUILayout.Label("<b>The observation</b> (as an automatic weather station reports it: a METAR)", Host.Rich);
            NowSaid n = now;
            if (n == null) { GUILayout.Label("Reading the weather...", Host.Small); return; }
            GUILayout.Label("<size=15><color=#bdf2bd>" + n.Metar + "</color></size>", Host.Rich);
            GUILayout.Label(n.Words, Host.Rich);
            if (n.Warnings.Length > 0 || Icing.Report.Length > 0 || Lightning.Report.Length > 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label("<b>Warnings</b>", Host.Rich);
                if (n.Warnings.Length > 0) GUILayout.Label("<color=#ff8a7a>" + n.Warnings + "</color>", Host.Rich);
                if (Icing.Report.Length > 0) GUILayout.Label("<color=#a8d8ff>" + Icing.Report + "</color>", Host.Rich);
                if (Lightning.Report.Length > 0) GUILayout.Label("<color=#ffe08a>Lightning: " + Lightning.Report + "</color>", Host.Rich);
            }
            // ---- launch weather (Launch.cs: NASA's rules for a crewed Falcon 9)
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
                    var line = new StringBuilder("The chance of a no go, the next three hours:  ");
                    for (int k = 0; k < f.Length; k += 2)
                    {
                        double ahead = f[k].Ut - ut;
                        if (ahead < -300.0) continue;
                        line.Append(ahead < 60.0 ? "now" : "+" + Math.Round(ahead / 60.0) + " min").Append(" ").Append(f[k].Chance >= 50 ? "<color=#ff8a7a>" : f[k].Chance >= 20 ? "<color=#e8d17a>" : "<color=#8fd18f>").Append(f[k].Chance).Append("%</color>   ");
                    }
                    GUILayout.Label(line.ToString(), Host.Small);
                }
            }
            GUILayout.Space(8f);
            GUILayout.Label("<color=#aaaaaa>How to read a METAR: the station (ZZZZ: none, the craft's own reading), the day and time (Z: the world's own clock), the wind (where from, in tens of degrees, and knots; G its gusts), visibility in metres (9999: 10 km or more), the weather (+ heavy, - light; RA rain, SN snow, SH showers, TS thunder, FZ freezing, FG fog, BR mist), the clouds (FEW, SCT, BKN, OVC, their bases in hundreds of feet; CB a thunderstorm's), temperature/dew point (M below zero), Q the pressure in hPa, RMK remarks.</color>", Host.Small);
        }

        // ---------------------------------------------------------------- the map

        static readonly string[] Ranges = { "100 km", "400 km", "1600 km", "World" };
        static readonly double[] Halves = { 50000.0, 200000.0, 800000.0, 0.0 };
        static int range = 1;
        static int satellite = 1;           // 0 none, 1 infrared, 2 visible
        // (lightning starts off: over an outbreak's radar the strikes made each storm a sparkle that hid it; shown as its own layer, as real services do)
        static bool radar = true, pressure = true, wind = false, lightning = false, paths = true, temperature = false;
        static float ahead;                 // hours from now, the moment shown
        static bool playing;
        static float playAt;
        static WeatherMap shown;
        static Task<WeatherMap> mapWork;
        static Texture2D mapTexture;
        static double mapLat = double.NaN, mapLon;
#if DEV
        static double testLat = double.NaN, testLon;
#endif

        static WeatherMap.Show Layers()
        {
            WeatherMap.Show s = 0;
            if (satellite == 1) s |= WeatherMap.Show.Infrared;
            if (satellite == 2) s |= WeatherMap.Show.Visible;
            if (radar) s |= WeatherMap.Show.Radar;
            if (pressure) s |= WeatherMap.Show.Pressure;
            if (wind) s |= WeatherMap.Show.Wind;
            if (lightning) s |= WeatherMap.Show.Lightning;
            if (paths) s |= WeatherMap.Show.Paths;
            if (temperature) s |= WeatherMap.Show.Temperature;
            return s;
        }

        static WeatherMap Wanted(World w, double lat, double lon, double ut)
        {
#if DEV
            if (!double.IsNaN(testLat)) { lat = testLat; lon = testLon; }
#endif
            bool whole = range == 3;
            double half = Halves[range];
            // (the middle moves with the craft only once it has gone a twentieth of the map: not a new map for every metre flown)
            if (double.IsNaN(mapLat) || whole || Model.Distance(w, lat, lon, mapLat, mapLon) > 0.05 * half * 2.0) { mapLat = lat; mapLon = lon; }
            int size = whole ? 0 : 500;
            return new WeatherMap
            {
                W = w, Lat = whole ? 0.0 : mapLat, Lon = whole ? lon : mapLon, Ut = ut + ahead * 3600.0, Now = ut, Half = half, Whole = whole,
                Width = whole ? 640 : size, Height = whole ? 320 : size, Layers = Layers(), PlaceLat = lat, PlaceLon = lon,
            };
        }

        static void TickMap(World w, double lat, double lon, double ut)
        {
            if (mapWork != null && mapWork.IsCompleted)
            {
                if (!mapWork.IsFaulted)
                {
                    WeatherMap m = mapWork.Result;
                    if (mapTexture == null || mapTexture.width != m.Width || mapTexture.height != m.Height)
                    {
                        if (mapTexture != null) UnityEngine.Object.Destroy(mapTexture);
                        mapTexture = new Texture2D(m.Width, m.Height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                    }
                    mapTexture.SetPixels32(m.Pixels);
                    mapTexture.Apply(false);
                    Canvas.Free(m.Pixels);
                    m.Pixels = null;
                    shown = m;
                }
                else Weather.Log("the map failed: " + mapWork.Exception?.GetBaseException());
                mapWork = null;
            }
            if (playing && mapWork == null && Time.realtimeSinceStartup > playAt)
            {
                playAt = Time.realtimeSinceStartup + 0.4f;
                ahead += 0.25f;
                if (ahead > 24f) ahead = -3f;
            }
            if (mapWork != null) return;
            WeatherMap want = Wanted(w, lat, lon, ut);
            if (shown != null && shown.Key == want.Key) return;
            mapWork = Task.Run(() => { want.Make(); return want; });
        }

        static void DrawMap(World w, double lat, double lon, double ut, Rect room)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Ranges.Length; i++)
                if (GUILayout.Toggle(range == i, Ranges[i], GUI.skin.button, GUILayout.Width(70f)) && range != i) range = i;
            GUILayout.Space(12f);
            GUILayout.Label("Satellite:", Host.Small, GUILayout.Width(56f));
            if (GUILayout.Toggle(satellite == 1, "Infrared", GUI.skin.button, GUILayout.Width(70f))) satellite = 1;
            if (GUILayout.Toggle(satellite == 2, "Visible", GUI.skin.button, GUILayout.Width(62f))) satellite = 2;
            if (GUILayout.Toggle(satellite == 0, "Off", GUI.skin.button, GUILayout.Width(40f))) satellite = 0;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            radar = GUILayout.Toggle(radar, "Radar", GUILayout.Width(84f));
            pressure = GUILayout.Toggle(pressure, "Pressure and fronts", GUILayout.Width(178f));
            wind = GUILayout.Toggle(wind, "Wind", GUILayout.Width(76f));
            lightning = GUILayout.Toggle(lightning, "Lightning", GUILayout.Width(108f));
            paths = GUILayout.Toggle(paths, "Paths and tracks", GUILayout.Width(160f));
            temperature = GUILayout.Toggle(temperature, "Temperature", GUILayout.Width(130f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Ahead(ahead), Host.Small, GUILayout.Width(190f));
            float was = ahead;
            ahead = Mathf.Round(GUILayout.HorizontalSlider(ahead, -3f, 24f, GUILayout.Width(380f)) * 4f) / 4f;
            if (ahead != was) playing = false;
            if (GUILayout.Button("Now", GUILayout.Width(50f))) { ahead = 0f; playing = false; }
            if (GUILayout.Button(playing ? "Stop" : "Play", GUILayout.Width(56f))) playing = !playing;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            WeatherMap m = shown;
            float mw = m != null ? m.Width : 500f, mh = m != null ? m.Height : 500f;
            Rect at = GUILayoutUtility.GetRect(mw, mh, GUILayout.Width(mw), GUILayout.Height(mh));
            if (Event.current.type == EventType.Repaint)
            {
                if (mapTexture != null && m != null) GUI.DrawTexture(at, mapTexture, ScaleMode.StretchToFill, false);
                else Fill(at, new Color(0.05f, 0.06f, 0.08f, 1f));
            }
            if (m != null)
                foreach (WeatherMap.Label l in m.Labels)
                {
                    if (l.Big) Shadowed(new Rect(at.x + l.X - 20f, at.y + l.Y - 16f, 40f, 34f), l.Text, TagBig, l.Colour);
                    else Shadowed(new Rect(at.x + l.X, at.y + l.Y - 6f, 90f, 14f), l.Text, Tag, l.Colour);
                }
            if (mapWork != null) Shadowed(new Rect(at.x + 6f, at.y + at.height - 18f, 200f, 14f), "drawing...", Tag, Color.white);
            GUILayout.Space(8f);
            side = Mathf.Max(160f, room.width - mw - 14f);
            GUILayout.BeginVertical(GUILayout.Width(side));
            Pointed(w, at, m, lat, lon);
            Legend(m);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        static string Ahead(float hours)
        {
            if (Mathf.Abs(hours) < 0.01f) return "<b>Now</b>";
            int mins = Mathf.RoundToInt(Mathf.Abs(hours) * 60f);
            string by = (mins >= 60 ? mins / 60 + " h" : "") + (mins % 60 != 0 ? " " + mins % 60 + " min" : "");
            return hours > 0f ? "<b>" + by.Trim() + " ahead</b> (the forecast)" : "<b>" + by.Trim() + " ago</b>";
        }

        static Texture2D white;

        public static void Fill(Rect r, Color c)
        {
            if (white == null) { white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }
            Color was = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = was;
        }

        static float side = 300f;
        static string pointedSaid = "";
        static Vector2 pointedAt = new Vector2(-1f, -1f);
        static float pointedWhen;

        /// <summary>The weather where the pointer is over the map: where that is, how far from the place, what the weather is there.</summary>
        static void Pointed(World w, Rect at, WeatherMap m, double lat, double lon)
        {
            Vector2 mouse = Event.current.mousePosition;
            if (m != null && m.View != null && at.Contains(mouse))
            {
                Vector2 px = mouse - new Vector2(at.x, at.y);
                if ((px - pointedAt).sqrMagnitude > 1f && Time.realtimeSinceStartup - pointedWhen > 0.08f && Event.current.type == EventType.Repaint)
                {
                    pointedAt = px; pointedWhen = Time.realtimeSinceStartup;
                    m.View.ToPlace(px.x * m.Width / at.width, px.y * m.Height / at.height, out double la, out double lo);
                    Base b = Model.Sample(w, la, lo, m.Ut);
                    WeatherHere h = Model.Build(w, la, lo, m.Ut, ref b, Model.RegionCells(w, la, lo, m.Ut));
                    double d = Model.Distance(w, lat, lon, la, lo);
                    Model.Toward(lat, lon, la, lo, out float te, out float tn);
                    int brg = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(te, tn) * Mathf.Rad2Deg, 360f));
                    pointedSaid = "<b>" + Deg(la, "N", "S") + " " + Deg(lo, "E", "W") + "</b>\n" + (d / 1000.0).ToString("F0") + " km at " + brg.ToString("000") + "° from here\n"
                                  + Words(h) + "\n" + (h.Temperature - 273.15f).ToString("F0") + " °C, " + Synoptic.Pressure(w, la, lo, m.Ut, b).ToString("F0") + " hPa" + (b.Land > 0.5f ? ", over land" : ", over the sea");
                }
            }
            GUILayout.Label(pointedSaid.Length > 0 ? pointedSaid : "<color=#aaaaaa>Point at the map for the weather there.</color>", Host.Small, GUILayout.Width(side), GUILayout.Height(84f));
        }

        /// <summary>A short account of the weather at a place in words (as the mod's page has it).</summary>
        public static string Words(WeatherHere h)
        {
            string sky = h.Cover < 0.1f ? "clear" : h.Cover < 0.35f ? "a few clouds" : h.Cover < 0.65f ? "partly cloudy" : h.Cover < 0.9f ? "mostly cloudy" : "overcast";
            var falls = new List<string>();
            if (h.Storm > 0.25f) falls.Add("a thunderstorm");
            if (h.Hail > 0.05f) falls.Add("hail");
            // (Rain is all that falls; Snow, Freezing and Pellets the shares of it that are each)
            if (h.Rain > 0.03f)
            {
                string how = h.Rain > 0.6f ? "heavy " : h.Rain < 0.2f ? "light " : "";
                if (h.Freezing > 0.25f) falls.Add(how + "freezing rain");
                else if (h.Snow > 0.5f) falls.Add(how + "snow");
                else if (h.Pellets > 0.25f) falls.Add("ice pellets");
                else falls.Add(how + (h.Snow > 0.25f ? "rain and snow" : "rain"));
            }
            if (h.Fog > 0.3f) falls.Add("fog");
            if (h.Dust > 0.2f) falls.Add("dust");
            string s = char.ToUpper(sky[0]) + sky.Substring(1) + (falls.Count > 0 ? "; " + string.Join(", ", falls.ToArray()) : "");
            return s;
        }

#if DEV
        /// <summary>
        /// (Development build, through Probe.Centre: the window worked from outside.) "open", "shut", "tab 1", "range 2", "ahead 3.5",
        /// "satellite 2", "layers radar pressure wind lightning paths temperature" (those named on, the rest off), "play"; and what
        /// the map last said and how long it took.
        /// </summary>
        internal static string Drive(string how)
        {
            string[] w = (how ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length > 0)
                switch (w[0])
                {
                    case "open": if (!Open) Toggle(); break;
                    case "shut": if (Open) Toggle(); break;
                    case "tab": tab = int.Parse(w[1]); break;
                    case "range": range = int.Parse(w[1]); break;
                    case "ahead": ahead = float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture); playing = false; break;
                    case "satellite": satellite = int.Parse(w[1]); break;
                    case "play": playing = !playing; break;
                    case "lows":
                        {
                            if (!WeatherDriver.Place(out CelestialBody body, out double la, out double lo, out double _, out float _)) return "nowhere";
                            World wd = World.Of(body);
                            var o = new StringBuilder();
                            foreach (Low l in Lows.At(wd, Planetarium.GetUniversalTime())) o.Append("low ").Append(l.Lat.ToString("F1")).Append(' ').Append(l.Lon.ToString("F1")).Append(" now ").Append(l.Now.ToString("F2")).Append(" wound ").Append(l.Wound.ToString("F2")).Append("; ");
                            foreach (Cyclone cy in Violent.Cyclones(wd, Planetarium.GetUniversalTime())) o.Append("cyclone ").Append(cy.Lat.ToString("F1")).Append(' ').Append(cy.Lon.ToString("F1")).Append(' ').Append(cy.Speed.ToString("F0")).Append(" m/s; ");
                            return o.ToString();
                        }
                    case "middle": testLat = w.Length > 2 ? double.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : double.NaN; testLon = w.Length > 2 ? double.Parse(w[2], System.Globalization.CultureInfo.InvariantCulture) : 0.0; mapLat = double.NaN; break;
                    case "layers":
                        var on = new HashSet<string>(w);
                        radar = on.Contains("radar"); pressure = on.Contains("pressure"); wind = on.Contains("wind"); lightning = on.Contains("lightning"); paths = on.Contains("paths"); temperature = on.Contains("temperature");
                        break;
                }
            WeatherMap m = shown;
            return (Open ? "open, tab " + Tabs[tab] : "shut") + "; map: " + (m == null ? "none yet" : m.Width + "x" + m.Height + ", " + m.Labels.Count + " labels, " + m.Said) + (mapWork != null ? " (drawing another)" : "")
                   + "; now: " + (now != null ? now.Metar : "none");
        }
#endif

        static void Legend(WeatherMap m)
        {
            GUILayout.Space(6f);
            if (radar)
            {
                GUILayout.Label("<b>Radar</b> (dBZ): light to heavy rain, hail at 55+; snow in blues, freezing rain and ice pellets in pinks", Host.Small, GUILayout.Width(side));
                Rect r = GUILayoutUtility.GetRect(side, 12f, GUILayout.Width(side));
                float cell = Mathf.Min(20f, side / 15f);
                if (Event.current.type == EventType.Repaint)
                    for (int k = 0; k < 15; k++) Fill(new Rect(r.x + k * cell, r.y, cell - 1f, 10f), WeatherMap.RadarColour(5f + k * 5f, 0));
                Rect t = GUILayoutUtility.GetRect(side, 12f, GUILayout.Width(side));
                for (int k = 0; k < 15; k += 2) GUI.Label(new Rect(t.x + k * cell, t.y - 2f, 30f, 14f), (5 + k * 5).ToString(), Tag);
            }
            if (pressure) GUILayout.Label("<b>Pressure</b>: isobars every " + (range >= 2 ? (range == 3 ? "4" : "4") : range == 1 ? "2" : "1") + " hPa (their last two figures), H and L with the centre's pressure. <color=#6f9bff>Cold front</color> (triangles), <color=#ff6655>warm front</color> (half-discs), <color=#c67df2>occluded</color>, <color=#e98ae6>squall line</color> (dashed): the symbols on the side the front is moving to.", Host.Small, GUILayout.Width(side));
            if (lightning) GUILayout.Label(range >= 2 ? "<b>Lightning</b>: the strikes to the ground of the last 5 minutes, a dot each; <color=#ffffff>white</color> under 2 min, <color=#fff04d>yellow</color> to 5." : "<b>Lightning</b>, the last 20 minutes: + to the ground, · in the cloud; <color=#ffffff>white</color> under 2 min, <color=#fff04d>yellow</color> 5, <color=#ff9a26>orange</color> 10, <color=#f2331a>red</color> 15, <color=#9a1410>dark red</color> 20.", Host.Small, GUILayout.Width(side));
            if (paths) GUILayout.Label("<b>Paths</b>: <color=#ff8c80>lows</color> and <color=#ffd94d>tropical cyclones</color> from 6 h back (line) to 12 h ahead (dashes; a cyclone's a day), a dot each 3 h. <b>Storm tracks</b> as a radar follows them: the <color=#ffeb73>thunderstorms</color> and <color=#d9ebff>showers</color>, their last hour (dots) and next (ticks each 15 min).", Host.Small, GUILayout.Width(side));
            if (wind) GUILayout.Label("<b>Wind</b> at 10 m, barbs from where it blows: a short barb 5 knots, a long one 10, a pennant 50.", Host.Small, GUILayout.Width(side));
            if (m != null && m.Said.Length > 0) GUILayout.Label("<color=#999999>" + m.Said + "</color>", Host.Small, GUILayout.Width(side));
        }
    }
}
