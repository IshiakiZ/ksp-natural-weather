using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0.) The weather centre's forecast for the place: the next day (four of Kerbin's) a quarter of an hour at a time,
    /// as the National Weather Service's hourly weather graph and a meteogram show it: the temperature and the dew point, the
    /// pressure, the cloud by layer, what falls and the chance of it, thunder, the wind and its gusts and its way; day and night
    /// shaded. And the same as a TAF, the aviation forecast in groups. The model's own weather, so it comes true; the chance of rain
    /// is how much of the ground round the place has it in each hour (as a forecaster gives it: rain somewhere in the area), which
    /// the scattered showers of the model make less than certain.
    /// </summary>
    static class Meteogram
    {
        sealed class Slot
        {
            public double Ut;
            public float T, Td, P, Low, Mid, High, Fall, Snow, Mix, Storm, Wind, Gust, From, Chance, Seeing;
            public bool Day;
            public WeatherHere H;
            public Base B;
        }

        sealed class Made
        {
            public World W; public double Lat, Lon, Ut;
            public Slot[] Slots;
            public Color32[] Pixels; public int Width, Height;
            public readonly List<WeatherMap.Label> Labels = new List<WeatherMap.Label>();
            public string Taf = "", Said = "";
            public float TLo, THi, PLo, PHi, WHi, FHi;
        }

        const int Width = 900, Height = 400;
        const double Span = 24.0 * 3600.0, Step = 900.0;
        static Made shown;
        static Task<Made> work;
        static Texture2D texture;
        static float madeAt = -100f;

        public static void Shut()
        {
            if (texture != null) { UnityEngine.Object.Destroy(texture); texture = null; }
            shown = null; work = null; madeAt = -100f;
        }

        public static void Tick(World w, double lat, double lon, double ut)
        {
            if (work != null && work.IsCompleted)
            {
                if (!work.IsFaulted)
                {
                    Made m = work.Result;
                    if (texture == null) texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                    texture.SetPixels32(m.Pixels);
                    texture.Apply(false);
                    Canvas.Free(m.Pixels);
                    m.Pixels = null;
                    shown = m;
                }
                else Weather.Log("the forecast failed: " + work.Exception?.GetBaseException());
                work = null;
            }
            // (made again every half minute of the computer's clock, or at once if the place has moved some way)
            bool moved = shown != null && (shown.W != w || Model.Distance(w, lat, lon, shown.Lat, shown.Lon) > 5000.0);
            if (work == null && (shown == null || moved || Time.realtimeSinceStartup - madeAt > 30f))
            {
                madeAt = Time.realtimeSinceStartup;
                work = Task.Run(() => Make(w, lat, lon, ut));
            }
        }

        // ---------------------------------------------------------------- the forecast

        static Made Make(World w, double lat, double lon, double ut)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int n = (int)(Span / Step) + 1;
            var m = new Made { W = w, Lat = lat, Lon = lon, Ut = ut, Slots = new Slot[n] };
            var cells = new List<WeatherCell>();
            float sunLat = (float)w.SunLat;
            // (the ring the chance of rain is read on: eight places 12 km off, and the place itself)
            const double Ring = 12000.0;
            for (int k = 0; k < n; k++)
            {
                double t = ut + k * Step;
                cells.Clear();
                Model.Cells(w, lat, lon, Ring + 25000.0, t, cells);
                Base b = Model.Sample(w, lat, lon, t);
                WeatherHere h = Model.Build(w, lat, lon, t, ref b, Near(w, lat, lon, cells));
                Column col = Model.ColumnAt(w, lat, lon, t);
                float speed = Mathf.Sqrt(col.UE * col.UE + col.UN * col.UN);
                int wet = h.Rain > 0.03f ? 1 : 0;
                for (int r = 0; r < 8; r++)
                {
                    double a = r * Math.PI / 4.0;
                    double la = lat + Ring * Math.Cos(a) / w.Radius * 180.0 / Math.PI, lo = lon + Ring * Math.Sin(a) / (w.Radius * Math.Max(0.05, Math.Cos(lat * Math.PI / 180.0))) * 180.0 / Math.PI;
                    Base rb = Model.Sample(w, la, lo, t);
                    if (Model.Build(w, la, lo, t, ref rb, Near(w, la, lo, cells)).Rain > 0.03f) wet++;
                }
                float sunLon = (float)(w.SunLon - 360.0 * (t - w.SunAt) / Math.Max(w.Day, 1.0));
                double cz = Math.Sin(lat * Math.PI / 180.0) * Math.Sin(sunLat * Math.PI / 180.0) + Math.Cos(lat * Math.PI / 180.0) * Math.Cos(sunLat * Math.PI / 180.0) * Math.Cos((lon - sunLon) * Math.PI / 180.0);
                m.Slots[k] = new Slot
                {
                    Ut = t, H = h, B = b,
                    T = h.Temperature - 273.15f, Td = Synoptic.DewPoint(b, h) - 273.15f, P = Synoptic.Pressure(w, lat, lon, t, b),
                    Low = h.Low.Kind != CloudKind.None ? h.Low.Cover : 0f, Mid = h.Middle.Kind != CloudKind.None ? h.Middle.Cover : 0f, High = h.High.Kind != CloudKind.None ? h.High.Cover : 0f,
                    Fall = 50f * h.Rain * h.Rain, Snow = h.Snow, Mix = h.Freezing + h.Pellets, Storm = h.Storm,
                    Wind = speed, Gust = speed + 2.5f * h.Gusts, From = Mathf.Repeat(Mathf.Atan2(-col.UE, -col.UN) * Mathf.Rad2Deg, 360f),
                    Chance = wet / 9f, Seeing = h.Seeing, Day = cz > 0.0,
                };
            }
            // (the chance of rain is the hour's: the most of its four quarters)
            for (int k = 0; k < n; k++)
            {
                int hour = k / 4;
                float most = 0f;
                for (int q = hour * 4; q < Math.Min(n, hour * 4 + 4); q++) most = Mathf.Max(most, m.Slots[q].Chance);
                m.Slots[k].Chance = most;
            }
            Draw(m);
            m.Taf = Taf(w, lat, lon, m.Slots);
            m.Said = "Made in " + watch.ElapsedMilliseconds + " ms.";
            return m;
        }

        /// <summary>The cells whose rain reaches a place, of those found round it.</summary>
        static List<WeatherCell> Near(World w, double lat, double lon, List<WeatherCell> cells)
        {
            var near = new List<WeatherCell>();
            foreach (WeatherCell c in cells) if (Model.Distance(w, lat, lon, c.Latitude, c.Longitude) < c.Radius + 2000.0) near.Add(c);
            return near;
        }

        // ---------------------------------------------------------------- the picture

        // (the panels, from the top: temperature and pressure, cloud, what falls, wind)
        const int Left = 44, Right = 40, Top = 8;
        static readonly int[] PanelTop = { 8, 158, 222, 312 };
        static readonly int[] PanelHigh = { 140, 56, 82, 80 };

        static void Draw(Made m)
        {
            var c = new Canvas(Width, Height);
            c.Fill(new Color(0.07f, 0.08f, 0.1f, 1f));
            Slot[] s = m.Slots;
            int n = s.Length;
            float plotW = Width - Left - Right;
            float X(int k) => Left + plotW * k / (n - 1);
            // ---- day and night, behind every panel
            for (int k = 0; k + 1 < n; k++)
            {
                Color band = s[k].Day ? new Color(0.13f, 0.14f, 0.17f, 1f) : new Color(0.05f, 0.05f, 0.08f, 1f);
                for (int p = 0; p < PanelTop.Length; p++) FillRect(c, X(k), PanelTop[p], X(k + 1), PanelTop[p] + PanelHigh[p], band);
            }
            // ---- each hour a faint line, each three a stronger one with its time
            for (int k = 0; k < n; k += 4)
            {
                bool three = k % 12 == 0;
                for (int p = 0; p < PanelTop.Length; p++) c.Line(X(k), PanelTop[p], X(k), PanelTop[p] + PanelHigh[p], new Color(1f, 1f, 1f, three ? 0.16f : 0.06f), 1f);
                if (three) m.Labels.Add(new WeatherMap.Label { X = X(k) - 14f, Y = Height - 10f, Text = k == 0 ? "now" : "+" + (k / 4) + " h", Colour = new Color(0.8f, 0.8f, 0.8f) });
            }

            // ---- 1: the temperature (red) and dew point (green), and the pressure (white, its own scale on the right)
            float tlo = float.MaxValue, thi = float.MinValue, plo = float.MaxValue, phi = float.MinValue;
            foreach (Slot x in s) { tlo = Mathf.Min(tlo, Mathf.Min(x.T, x.Td)); thi = Mathf.Max(thi, x.T); plo = Mathf.Min(plo, x.P); phi = Mathf.Max(phi, x.P); }
            tlo = Mathf.Floor(tlo / 2f) * 2f - 1f; thi = Mathf.Ceil(thi / 2f) * 2f + 1f;
            float pmid = 0.5f * (plo + phi), pspan = Mathf.Max(6f, phi - plo + 2f);
            plo = pmid - 0.5f * pspan; phi = pmid + 0.5f * pspan;
            m.TLo = tlo; m.THi = thi; m.PLo = plo; m.PHi = phi;
            float Ty(float t) => PanelTop[0] + PanelHigh[0] * (1f - (t - tlo) / Mathf.Max(0.1f, thi - tlo));
            float Py(float p) => PanelTop[0] + PanelHigh[0] * (1f - (p - plo) / Mathf.Max(0.1f, phi - plo));
            float tstep = thi - tlo > 24f ? 10f : thi - tlo > 10f ? 5f : 2f;
            for (float t = Mathf.Ceil(tlo / tstep) * tstep; t <= thi; t += tstep)
            {
                c.Line(Left, Ty(t), Width - Right, Ty(t), new Color(1f, 1f, 1f, Mathf.Abs(t) < 0.01f ? 0.35f : 0.08f), 1f);
                m.Labels.Add(new WeatherMap.Label { X = 4f, Y = Ty(t), Text = t.ToString("F0") + "°", Colour = new Color(1f, 0.6f, 0.5f) });
            }
            for (float p = Mathf.Ceil(plo / 2f) * 2f; p <= phi; p += pspan > 14f ? 4f : 2f)
                m.Labels.Add(new WeatherMap.Label { X = Width - Right + 4f, Y = Py(p), Text = p.ToString("F0"), Colour = new Color(0.85f, 0.85f, 0.85f) });
            var tl = new List<Vector2>(); var dl = new List<Vector2>(); var pl = new List<Vector2>();
            for (int k = 0; k < n; k++) { tl.Add(new Vector2(X(k), Ty(s[k].T))); dl.Add(new Vector2(X(k), Ty(s[k].Td))); pl.Add(new Vector2(X(k), Py(s[k].P))); }
            c.Lines(pl, new Color(0.85f, 0.85f, 0.85f, 0.7f), 1.2f, 5f, 3f);
            c.Lines(dl, new Color(0.35f, 0.85f, 0.4f, 1f), 1.8f);
            c.Lines(tl, new Color(1f, 0.35f, 0.3f, 1f), 2.2f);

            // ---- 2: the cloud, three rows: high, middle, low; each as grey as it covers the sky
            float rowH = PanelHigh[1] / 3f;
            for (int k = 0; k + 1 < n; k++)
            {
                float[] cover = { s[k].High, s[k].Mid, s[k].Low };
                for (int r = 0; r < 3; r++)
                {
                    float v = Mathf.Clamp01(cover[r]);
                    if (v < 0.03f) continue;
                    FillRect(c, X(k), PanelTop[1] + r * rowH + 1f, X(k + 1), PanelTop[1] + (r + 1) * rowH - 1f, new Color(0.55f + 0.4f * v, 0.56f + 0.4f * v, 0.6f + 0.38f * v, 0.25f + 0.75f * v));
                }
            }

            // ---- 3: what falls (bars, mm an hour, by its kind) and the chance of it (dots, the hour's); thunder above
            float fhi = 2f;
            foreach (Slot x in s) fhi = Mathf.Max(fhi, x.Fall);
            fhi = fhi > 20f ? Mathf.Ceil(fhi / 10f) * 10f : fhi > 5f ? Mathf.Ceil(fhi / 5f) * 5f : Mathf.Ceil(fhi);
            m.FHi = fhi;
            float Fy(float f) => PanelTop[2] + PanelHigh[2] * (1f - Mathf.Clamp01(f / fhi));
            for (int k = 0; k + 1 < n; k++)
            {
                if (s[k].Fall > 0.05f)
                {
                    Color kind = s[k].Mix > 0.3f ? new Color(0.95f, 0.45f, 0.75f) : s[k].Snow > 0.5f ? new Color(0.85f, 0.9f, 1f) : new Color(0.3f, 0.6f, 1f);
                    FillRect(c, X(k) + 0.5f, Fy(s[k].Fall), X(k + 1) - 0.5f, PanelTop[2] + PanelHigh[2], kind);
                }
                if (s[k].Storm > 0.25f)
                {
                    float x0 = 0.5f * (X(k) + X(k + 1));
                    c.Triangle(new Vector2(x0 - 3f, PanelTop[2] + 2f), new Vector2(x0 + 3f, PanelTop[2] + 2f), new Vector2(x0, PanelTop[2] + 8f), new Color(1f, 0.85f, 0.2f));
                }
            }
            for (int k = 0; k < n; k += 4)
            {
                float x0 = X(Math.Min(n - 1, k + 2));
                float y0 = PanelTop[2] + PanelHigh[2] * (1f - s[k].Chance);
                c.Disc(x0, y0, 2f, new Color(0.6f, 0.9f, 1f, 0.95f));
            }
            m.Labels.Add(new WeatherMap.Label { X = 4f, Y = PanelTop[2] + 2f, Text = fhi.ToString("F0") + " mm", Colour = new Color(0.55f, 0.75f, 1f) });
            m.Labels.Add(new WeatherMap.Label { X = Width - Right + 4f, Y = PanelTop[2] + 2f, Text = "100%", Colour = new Color(0.6f, 0.9f, 1f) });

            // ---- 4: the wind at 10 m (line) and its gusts (dashes), and arrows each hour the way it blows
            float whi = 5f;
            foreach (Slot x in s) whi = Mathf.Max(whi, x.Gust);
            whi = Mathf.Ceil(whi / 5f) * 5f;
            m.WHi = whi;
            float Wy(float v) => PanelTop[3] + PanelHigh[3] * (1f - Mathf.Clamp01(v / whi));
            var wl = new List<Vector2>(); var gl = new List<Vector2>();
            for (int k = 0; k < n; k++) { wl.Add(new Vector2(X(k), Wy(s[k].Wind))); gl.Add(new Vector2(X(k), Wy(s[k].Gust))); }
            c.Lines(gl, new Color(0.95f, 0.8f, 0.4f, 0.8f), 1.2f, 4f, 3f);
            c.Lines(wl, new Color(1f, 0.95f, 0.6f, 1f), 2f);
            for (int k = 0; k < n; k += 4)
            {
                if (s[k].Wind < 0.5f) continue;
                float a = s[k].From * Mathf.Deg2Rad;
                // (pointing the way it goes: from where it comes, turned round; pixels: east right, north up)
                var to = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                var mid = new Vector2(X(Math.Min(n - 1, k + 2)), PanelTop[3] + PanelHigh[3] - 10f);
                Vector2 tip = mid + to * 7f, tail = mid - to * 7f, side = new Vector2(to.y, -to.x);
                c.Line(tail.x, tail.y, tip.x, tip.y, new Color(0.9f, 0.9f, 0.9f), 1.3f);
                c.Triangle(tip + to * 2f, tip - to * 3f + side * 3f, tip - to * 3f - side * 3f, new Color(0.9f, 0.9f, 0.9f));
            }
            m.Labels.Add(new WeatherMap.Label { X = 4f, Y = PanelTop[3] + 2f, Text = whi.ToString("F0") + " m/s", Colour = new Color(1f, 0.92f, 0.6f) });

            m.Pixels = c.Rows();
            c.Free();
            m.Width = Width; m.Height = Height;
        }

        static void FillRect(Canvas c, float x0, float y0, float x1, float y1, Color colour)
        {
            for (int y = Mathf.Max(0, Mathf.FloorToInt(y0)); y < Mathf.Min(c.H, Mathf.CeilToInt(y1)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(x0)); x < Mathf.Min(c.W, Mathf.CeilToInt(x1)); x++)
                    c.Put(x, y, colour, 1f);
        }

        // ---------------------------------------------------------------- the TAF

        /// <summary>
        /// The forecast as a TAF: the station, when it was made, the hours it is good for (day and hour to day and hour), the weather
        /// at first in a METAR's groups; then a new group FM (from) at each hour the weather has changed as a pilot would care (the
        /// wind's way by 30 degrees or more, its strength by 10 knots, the visibility, the weather, the ceiling), and PROB30 or PROB40
        /// for thunderstorms that the hour's chance puts so.
        /// </summary>
        static string Taf(World w, double lat, double lon, Slot[] s)
        {
            var o = new StringBuilder();
            string day0 = Synoptic.DayTime(s[0].Ut);
            string end = Synoptic.DayTime(s[s.Length - 1].Ut);
            o.Append("TAF ").Append(Synoptic.Station(w, lat, lon)).Append(' ').Append(day0).Append("Z ").Append(day0.Substring(0, 4)).Append('/').Append(end.Substring(0, 4)).Append(' ');
            string last = Groups(s[0]);
            o.Append(last);
            int lastHour = 0;
            for (int k = 4; k < s.Length; k += 4)
            {
                string now = Groups(s[k]);
                if (Changed(s[lastHour * 4], s[k]))
                {
                    o.Append("\n     FM").Append(Synoptic.DayTime(s[k].Ut)).Append(' ').Append(now);
                    lastHour = k / 4;
                    last = now;
                }
                else if (s[k].Chance >= 0.3f && s[k].Storm > 0.1f && !last.Contains("TS"))
                    o.Append("\n     PROB").Append(s[k].Chance >= 0.4f ? "40 " : "30 ").Append(Synoptic.DayTime(s[k].Ut).Substring(0, 4)).Append('/').Append(Synoptic.DayTime(s[Math.Min(s.Length - 1, k + 4)].Ut).Substring(0, 4)).Append(" TSRA");
            }
            o.Append('=');
            return o.ToString();
        }

        static string Groups(Slot x)
        {
            string metar = Synoptic.Groups(x.H, x.B, x.Wind, x.From, x.Gust);
            return metar;
        }

        static bool Changed(Slot a, Slot b)
        {
            float turn = Mathf.Abs(Mathf.DeltaAngle(a.From, b.From));
            if (Mathf.Max(a.Wind, b.Wind) * 1.94384f > 10f && turn >= 30f) return true;
            if (Mathf.Abs(a.Wind - b.Wind) * 1.94384f >= 10f) return true;
            if (Category(a.Seeing) != Category(b.Seeing)) return true;
            if ((a.Fall > 0.3f) != (b.Fall > 0.3f) || (a.Storm > 0.25f) != (b.Storm > 0.25f)) return true;
            return Ceiling(a) != Ceiling(b);
        }

        static int Category(float seeing) => seeing < 800f ? 0 : seeing < 1500f ? 1 : seeing < 5000f ? 2 : seeing < 9999f ? 3 : 4;

        static int Ceiling(Slot x)
        {
            float ground = Mathf.Max(0f, x.B.Ground), best = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                CloudLayer l = i == 0 ? x.H.Low : i == 1 ? x.H.Middle : x.H.High;
                if (l.Kind != CloudKind.None && l.Cover >= 0.5f) best = Mathf.Min(best, l.Base - ground);
            }
            float ft = best * 3.28084f;
            return ft < 500f ? 0 : ft < 1000f ? 1 : ft < 3000f ? 2 : ft < 5000f ? 3 : 4;
        }

        // ---------------------------------------------------------------- drawn in the window

        static GUIStyle tag;
        static GUIStyle Tag => tag ?? (tag = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) });

        public static void Draw(World w, double lat, double lon, double ut, Rect room)
        {
            Made m = shown;
            GUILayout.Label("<b>The next day here</b> (four of Kerbin's), a quarter of an hour at a time: <color=#ff6a5a>temperature</color> and <color=#66d96e>dew point</color> (°C), <color=#d9d9d9>pressure</color> (dashes, hPa on the right); the cloud, high, middle and low; what falls, mm an hour (<color=#4d99ff>rain</color>, <color=#dbe6ff>snow</color>, <color=#f273bf>freezing rain or ice pellets</color>), the hour's <color=#99e6ff>chance</color> of it round here (dots), <color=#ffd933>thunder</color> (triangles); the <color=#fff299>wind</color> at 10 m and its gusts (dashes), arrows the way it blows. Night shaded darker.", Host.Small);
            Rect at = GUILayoutUtility.GetRect(Width, Height, GUILayout.Width(Width), GUILayout.Height(Height));
            if (Event.current.type == EventType.Repaint)
            {
                if (texture != null && m != null) GUI.DrawTexture(at, texture, ScaleMode.StretchToFill, false);
                else Centre.Fill(at, new Color(0.07f, 0.08f, 0.1f, 1f));
            }
            if (m == null) { GUI.Label(new Rect(at.x + 10f, at.y + 10f, 300f, 20f), "Working out the forecast...", Host.Small); return; }
            foreach (WeatherMap.Label l in m.Labels) Centre.Shadowed(new Rect(at.x + l.X, at.y + l.Y - 6f, 60f, 14f), l.Text, Tag, l.Colour);
            GUI.Label(new Rect(at.x + 50f, at.y + PanelTop[1] + 2f, 40f, 14f), "high", Tag);
            GUI.Label(new Rect(at.x + 50f, at.y + PanelTop[1] + 20f, 40f, 14f), "middle", Tag);
            GUI.Label(new Rect(at.x + 50f, at.y + PanelTop[1] + 38f, 40f, 14f), "low", Tag);
            // (the slot under the pointer, in words)
            Vector2 mouse = Event.current.mousePosition;
            if (at.Contains(mouse))
            {
                int k = Mathf.Clamp(Mathf.RoundToInt((mouse.x - at.x - Left) / (Width - Left - Right) * (m.Slots.Length - 1)), 0, m.Slots.Length - 1);
                Slot x = m.Slots[k];
                float px = at.x + Left + (Width - Left - Right) * k / (m.Slots.Length - 1);
                Centre.Fill(new Rect(px, at.y, 1f, Height), new Color(1f, 1f, 1f, 0.35f));
                string said = "+" + Math.Round((x.Ut - m.Ut) / 3600.0, 2) + " h: " + x.T.ToString("F0") + " °C (dew point " + x.Td.ToString("F0") + "), " + x.P.ToString("F0") + " hPa; " + Centre.Words(x.H)
                              + "; wind from " + Mathf.RoundToInt(x.From).ToString("000") + "° at " + x.Wind.ToString("F0") + " m/s, gusts " + x.Gust.ToString("F0") + "; chance of rain or snow " + Mathf.RoundToInt(x.Chance * 100f) + "%";
                GUILayout.Label(said, Host.Small);
            }
            else GUILayout.Label("<color=#aaaaaa>Point at the forecast for an hour's weather in words.</color>", Host.Small);
            GUILayout.Label("<b>TAF</b> (the aviation forecast: from, change groups FM, PROB the chance of what follows)", Host.Small);
            GUILayout.Label("<size=13><color=#bdf2bd>" + m.Taf + "</color></size>", Host.Rich);
        }
    }
}
