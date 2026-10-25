using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0.) The weather centre's sounding: the air above the place, as a weather balloon's sounding is drawn on a skew-T
    /// (here against height, with the isotherms leaning right, so that the temperature, which falls with height, stands nearly
    /// upright): the temperature (red) and the dew point (green); a parcel of air lifted from the ground (white dashes), cooling at
    /// the dry rate until it clouds (the lifted condensation level, the base of heaped cloud) and at the moist rate after, and
    /// where it is warmer than the air round it, the energy a storm can draw on (CAPE, shaded); the cloud layers; the freezing
    /// level; the wind by height as barbs; where ice would build on a craft (cloud between 0 and -20 °C); and the jet stream.
    ///
    /// The air's temperature with height is the game's own, by the latitude (World.Temperature), with the warmth the sun has given
    /// the ground (the model's temperature there) mixed up through the lowest kilometre or so; a layer of warm air aloft where the
    /// model has one over freezing ground (what makes freezing rain). The dew point at the ground is the report's (Synoptic.DewPoint),
    /// falling slowly with height to the cloud base, the air saturated in cloud and dry above it.
    /// </summary>
    static class Sounding
    {
        sealed class Level { public float Z, T, Td, Parcel, Sigma, WindE, WindN; public bool Cloud, Ice; }

        sealed class Made
        {
            public World W; public double Lat, Lon, Ut;
            public float Ground, Top, Lcl, Freeze, Cape, Cin, Lfc, El, JetH, JetSpeed, JetFrom;
            public readonly List<Level> Levels = new List<Level>();
            public WeatherHere H; public Base B;
            public Color32[] Pixels;
            public readonly List<WeatherMap.Label> Labels = new List<WeatherMap.Label>();
            public string Said = "";
        }

        const int Width = 560, Height = 470;
        static Made shown;
        static Task<Made> work;
        static Texture2D texture;
        static float madeAt = -100f;

        public static void Shut()
        {
            if (texture != null) { UnityEngine.Object.Destroy(texture); texture = null; }
            shown = null; work = null; madeAt = -100f;
        }

        public static void Tick(World w, double lat, double lon, double alt, double ut)
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
                else Weather.Log("the sounding failed: " + work.Exception?.GetBaseException());
                work = null;
            }
            if (work == null && Time.realtimeSinceStartup - madeAt > 10f)
            {
                madeAt = Time.realtimeSinceStartup;
                work = Task.Run(() => Make(w, lat, lon, ut));
            }
        }

        /// <summary>How fast a saturated parcel cools as it rises, K/km, by its temperature (°C): the moist adiabat's lapse, from a few degrees a kilometre in warm air to nearly the dry rate in cold.</summary>
        static float MoistLapse(float c)
        {
            float[] t = { -40f, -30f, -20f, -10f, 0f, 10f, 20f, 30f };
            float[] g = { 9.6f, 9.2f, 8.6f, 7.7f, 6.5f, 5.3f, 4.3f, 3.5f };
            if (c <= t[0]) return g[0];
            for (int i = 1; i < t.Length; i++) if (c <= t[i]) return Mathf.Lerp(g[i - 1], g[i], (c - t[i - 1]) / (t[i] - t[i - 1]));
            return g[g.Length - 1];
        }

        static Made Make(World w, double lat, double lon, double ut)
        {
            var m = new Made { W = w, Lat = lat, Lon = lon, Ut = ut };
            Base b = Model.Sample(w, lat, lon, ut);
            var cells = new List<WeatherCell>();
            Model.Cells(w, lat, lon, 30000.0, ut, cells);
            WeatherHere h = Model.Build(w, lat, lon, ut, ref b, cells);
            Column col = Model.ColumnAt(w, lat, lon, ut);
            m.H = h; m.B = b;
            float ground = Mathf.Max(0f, b.Ground);
            m.Ground = ground;
            m.Top = Mathf.Max(ground + 8000f, w.Climate.Tops + 3000f);
            float t0 = h.Temperature - 273.15f, td0 = Synoptic.DewPoint(b, h) - 273.15f;
            float curve0 = w.Temperature(lat, ground) - 273.15f;
            // (the parcel from the ground: dry until it clouds, where its temperature meets its dew point, which falls 1.8 K a km)
            m.Lcl = ground + Mathf.Max(0f, (t0 - td0) / (9.8f - 1.8f)) * 1000f;
            float parcel = t0;
            bool cloudedYet = false, inCape = false;
            m.Freeze = -1f; m.Lfc = -1f; m.El = -1f;
            float dz = 100f;
            for (float z = ground; z <= m.Top + 1f; z += dz)
            {
                float above = z - ground;
                float env = w.Temperature(lat, z) - 273.15f + (t0 - curve0) * Mathf.Exp(-above / 1200f);
                // (a layer of warm air aloft over freezing ground, where the model has one: freezing rain and ice pellets come of it)
                if (b.Nose > 0.01f) env += 4f * b.Nose * Mathf.Exp(-((above - 1800f) / 600f) * ((above - 1800f) / 600f));
                bool inCloud = In(h.Low, z) || In(h.Middle, z) || In(h.High, z);
                float td = z <= m.Lcl ? td0 - 1.8f * above / 1000f : inCloud ? env : env - 12f - 4f * Mathf.Clamp01((z - m.Lcl) / 4000f);
                if (inCloud) td = env;
                td = Mathf.Min(td, env);
                if (z > ground)
                {
                    if (!cloudedYet && z < m.Lcl) parcel -= 9.8f * dz / 1000f;
                    else { cloudedYet = true; parcel -= MoistLapse(parcel) * dz / 1000f; }
                }
                // (CAPE: the parcel's buoyancy, g (Tp - Te) / Te, summed where it is warmer above its free convection; CIN below that)
                float buoy = 9.81f * (parcel - env) / (env + 273.15f);
                if (z > m.Lcl - 1f)
                {
                    if (buoy > 0f) { if (!inCape) { inCape = true; if (m.Lfc < 0f) m.Lfc = z; } m.Cape += buoy * dz; m.El = z; }
                    else if (!inCape) m.Cin += buoy * dz;
                }
                else if (buoy < 0f) m.Cin += buoy * dz;
                if (m.Freeze < 0f && env <= 0f) m.Freeze = z;
                Vector3 wind = Model.Mean(col, z, above);
                float sigma = Model.Sigmas(col, z, Mathf.Max(1f, above), out float _, out float _, out float _);
                m.Levels.Add(new Level { Z = z, T = env, Td = td, Parcel = parcel, Sigma = sigma, WindE = wind.x, WindN = wind.y, Cloud = inCloud, Ice = inCloud && env <= 0f && env >= -20f });
            }
            m.JetH = col.JetH;
            m.JetSpeed = Mathf.Sqrt(col.JE * col.JE + col.JN * col.JN);
            m.JetFrom = Mathf.Repeat(Mathf.Atan2(-col.JE, -col.JN) * Mathf.Rad2Deg, 360f);
            Draw(m);
            return m;
        }

        static bool In(CloudLayer l, float z) => l.Kind != CloudKind.None && l.Cover > 0.15f && z >= l.Base && z <= l.Top;

        // (the diagram: height up, temperature across, leaning 4.5 °C a kilometre to the right)
        const int Left = 40, Right = 70, Bottom = 22, TopMargin = 8;
        const float Skew = 4.5f;

        static void Draw(Made m)
        {
            var c = new Canvas(Width, Height);
            c.Fill(new Color(0.07f, 0.08f, 0.1f, 1f));
            float plotW = Width - Left - Right, plotH = Height - Bottom - TopMargin;
            float t0 = m.Levels[0].T;
            float tLeft = Mathf.Floor((t0 - 40f) / 10f) * 10f, tRight = tLeft + 60f;
            float Y(float z) => TopMargin + plotH * (1f - (z - m.Ground) / (m.Top - m.Ground));
            float X(float t, float z) => Left + plotW * ((t + Skew * (z - m.Ground) / 1000f) - tLeft) / (tRight - tLeft);
            // ---- the cloud layers, behind, as grey bands across
            foreach (CloudLayer l in new[] { m.H.Low, m.H.Middle, m.H.High })
            {
                if (l.Kind == CloudKind.None || l.Cover < 0.1f) continue;
                float y0 = Y(Mathf.Min(l.Top, m.Top)), y1 = Y(Mathf.Max(l.Base, m.Ground));
                for (int y = Mathf.Max(0, Mathf.FloorToInt(y0)); y < Mathf.Min(c.H, Mathf.CeilToInt(y1)); y++)
                    for (int x = Left; x < Width - Right; x++) c.Put(x, y, new Color(0.6f, 0.62f, 0.68f, 1f), 0.08f + 0.18f * l.Cover);
                m.Labels.Add(new WeatherMap.Label { X = Left + 4f, Y = 0.5f * (y0 + y1), Text = Synoptic.Named(l.Kind) + " " + Mathf.RoundToInt(l.Cover * 8f) + "/8", Colour = new Color(0.8f, 0.82f, 0.88f) });
            }
            // ---- isotherms every 10 °C (0 °C brighter), leaning; and the heights every kilometre
            for (float t = tLeft - 80f; t <= tRight; t += 10f)
            {
                var line = new List<Vector2> { new Vector2(X(t, m.Ground), Y(m.Ground)), new Vector2(X(t, m.Top), Y(m.Top)) };
                c.Lines(line, new Color(0.5f, 0.7f, 1f, Mathf.Abs(t) < 0.01f ? 0.55f : 0.14f), Mathf.Abs(t) < 0.01f ? 1.4f : 1f);
                float xb = X(t, m.Ground);
                if (xb >= Left && xb <= Width - Right) m.Labels.Add(new WeatherMap.Label { X = xb - 8f, Y = Height - 10f, Text = t.ToString("F0") + "°", Colour = new Color(0.6f, 0.75f, 1f) });
            }
            for (float z = Mathf.Ceil(m.Ground / 1000f) * 1000f; z <= m.Top; z += 1000f)
            {
                c.Line(Left, Y(z), Width - Right, Y(z), new Color(1f, 1f, 1f, 0.07f), 1f);
                m.Labels.Add(new WeatherMap.Label { X = 4f, Y = Y(z), Text = (z / 1000f).ToString("F0") + " km", Colour = new Color(0.8f, 0.8f, 0.8f) });
            }
            // ---- dry adiabats, faint (cooling 9.8 K a km)
            for (float th = tLeft - 40f; th <= tRight + 80f; th += 10f)
            {
                var line = new List<Vector2>();
                for (float z = m.Ground; z <= m.Top; z += 500f) line.Add(new Vector2(X(th - 9.8f * (z - m.Ground) / 1000f, z), Y(z)));
                c.Lines(line, new Color(0.9f, 0.6f, 0.3f, 0.09f), 1f);
            }
            // ---- CAPE shaded where the parcel is the warmer
            for (int i = 0; i + 1 < m.Levels.Count; i++)
            {
                Level a = m.Levels[i];
                if (a.Parcel <= a.T || a.Z < m.Lcl - 1f) continue;
                float y = Y(a.Z), x0 = X(a.T, a.Z), x1 = X(a.Parcel, a.Z);
                float yb = Y(m.Levels[i + 1].Z);
                for (int yy = Mathf.FloorToInt(yb); yy <= Mathf.CeilToInt(y); yy++)
                    for (int xx = Mathf.FloorToInt(x0); xx <= Mathf.CeilToInt(x1); xx++) c.Put(xx, yy, new Color(1f, 0.55f, 0.2f, 1f), 0.28f);
            }
            // ---- the freezing level
            if (m.Freeze > 0f) { c.Lines(new List<Vector2> { new Vector2(Left, Y(m.Freeze)), new Vector2(Width - Right, Y(m.Freeze)) }, new Color(0.5f, 0.8f, 1f, 0.8f), 1.2f, 6f, 4f); m.Labels.Add(new WeatherMap.Label { X = Width - Right - 92f, Y = Y(m.Freeze) - 8f, Text = "freezing " + (m.Freeze / 1000f).ToString("F1") + " km", Colour = new Color(0.6f, 0.85f, 1f) }); }
            // ---- the profiles
            var tl = new List<Vector2>(); var dl = new List<Vector2>(); var pl = new List<Vector2>();
            foreach (Level l in m.Levels) { tl.Add(new Vector2(X(l.T, l.Z), Y(l.Z))); dl.Add(new Vector2(X(l.Td, l.Z), Y(l.Z))); pl.Add(new Vector2(X(l.Parcel, l.Z), Y(l.Z))); }
            c.Lines(pl, new Color(1f, 1f, 1f, 0.75f), 1.3f, 5f, 4f);
            c.Lines(dl, new Color(0.35f, 0.85f, 0.4f, 1f), 2f);
            c.Lines(tl, new Color(1f, 0.35f, 0.3f, 1f), 2.4f);
            // ---- the cloud base of a lifted parcel
            if (m.Lcl < m.Top) { float y = Y(m.Lcl), x = X(m.Levels[0].T - 9.8f * (m.Lcl - m.Ground) / 1000f, m.Lcl); c.Line(x - 8f, y, x + 8f, y, Color.white, 1.4f); m.Labels.Add(new WeatherMap.Label { X = x + 10f, Y = y, Text = "cloud base (LCL) " + ((m.Lcl - m.Ground) / 1000f).ToString("F1") + " km", Colour = Color.white }); }
            // ---- icing, and the wind, on the right
            float wx = Width - Right + 34f;
            foreach (Level l in m.Levels) if (l.Ice) c.Line(Width - Right + 6f, Y(l.Z), Width - Right + 12f, Y(l.Z), new Color(0.6f, 0.85f, 1f, 0.9f), 2f);
            for (float z = Mathf.Ceil(m.Ground / 500f) * 500f + 500f; z <= m.Top; z += 1000f)
            {
                Level l = m.Levels[Mathf.Clamp(Mathf.RoundToInt((z - m.Ground) / 100f), 0, m.Levels.Count - 1)];
                Barb(c, wx, Y(z), l.WindE, l.WindN, m.Lat >= 0.0);
            }
            if (m.JetH > m.Ground && m.JetH < m.Top) m.Labels.Add(new WeatherMap.Label { X = Width - Right - 70f, Y = Y(m.JetH), Text = "jet " + Mathf.RoundToInt(m.JetSpeed) + " m/s", Colour = new Color(1f, 0.9f, 0.5f) });
            m.Pixels = c.Rows();
            c.Free();
            m.Said = Words(m);
        }

        static void Barb(Canvas c, float x, float y, float east, float north, bool northern)
        {
            // (as the map's: Barb there is private to it; drawn the same)
            float kt = Mathf.Sqrt(east * east + north * north) * 1.94384f;
            Color colour = new Color(0.95f, 0.95f, 0.95f, 0.9f);
            if (kt < 2.5f) { c.Ring(x, y, 3.2f, colour, 1.1f); return; }
            Vector2 to = new Vector2(east, -north).normalized, from = -to;
            Vector2 tip = new Vector2(x, y) + from * 15f;
            c.Line(x, y, tip.x, tip.y, colour, 1.2f);
            c.Disc(x, y, 1.4f, colour);
            Vector2 left = new Vector2(to.y, -to.x), side = northern ? left : -left;
            int fives = Mathf.RoundToInt(kt / 5f), pennants = fives / 10; fives -= pennants * 10;
            int tens = fives / 2; fives -= tens * 2;
            float at = 0f;
            for (int p = 0; p < pennants; p++) { Vector2 b0 = tip - from * at, b1 = tip - from * (at + 4f); c.Triangle(b0, b1, b0 + side * 7f - from * 1.5f, colour); at += 5f; }
            for (int t = 0; t < tens; t++) { Vector2 b0 = tip - from * at, e = b0 + side * 7f + from * 2.5f; c.Line(b0.x, b0.y, e.x, e.y, colour, 1.2f); at += 2.8f; }
            if (fives > 0) { if (at == 0f) at = 2.8f; Vector2 b0 = tip - from * at, e = b0 + side * 3.6f + from * 1.3f; c.Line(b0.x, b0.y, e.x, e.y, colour, 1.2f); }
        }

        /// <summary>What the sounding says, in words.</summary>
        static string Words(Made m)
        {
            var o = new StringBuilder();
            float cape = m.Cape, cin = -m.Cin;
            string unstable = cape < 50f ? "stable: no storms from it" : cape < 1000f ? "marginally unstable" : cape < 2500f ? "moderately unstable" : cape < 3500f ? "very unstable" : "extremely unstable";
            o.Append("<b>CAPE</b> ").Append(Mathf.RoundToInt(cape)).Append(" J/kg (").Append(unstable).Append(")");
            if (cape >= 50f) o.Append("; <b>CIN</b> ").Append(Mathf.RoundToInt(cin)).Append(" J/kg").Append(cin > 100f ? " (a strong lid: storms need a push)" : cin > 25f ? " (some lid)" : " (little to hold storms back)");
            if (m.Lfc > 0f) o.Append("; free convection from ").Append(((m.Lfc - m.Ground) / 1000f).ToString("F1")).Append(" km");
            if (m.El > 0f && cape >= 50f) o.Append(", storm tops near ").Append((m.El / 1000f).ToString("F0")).Append(" km");
            o.Append(".\n<b>Cloud base</b> of a lifted parcel (LCL) ").Append(((m.Lcl - m.Ground) / 1000f).ToString("F1")).Append(" km above the ground");
            o.Append("; <b>freezing level</b> ").Append(m.Freeze > 0f ? (m.Freeze / 1000f).ToString("F1") + " km" : "at the ground").Append(".");
            float iceLo = -1f, iceHi = -1f;
            foreach (Level l in m.Levels) if (l.Ice) { if (iceLo < 0f) iceLo = l.Z; iceHi = l.Z; }
            if (iceLo > 0f) o.Append("\n<color=#a8d8ff><b>Icing</b> in cloud from ").Append((iceLo / 1000f).ToString("F1")).Append(" to ").Append((iceHi / 1000f).ToString("F1")).Append(" km (between 0 and -20 °C).</color>");
            if (m.JetSpeed > 1f) o.Append("\n<b>Jet stream</b> at ").Append((m.JetH / 1000f).ToString("F0")).Append(" km, ").Append(Mathf.RoundToInt(m.JetSpeed)).Append(" m/s from ").Append(Mathf.RoundToInt(m.JetFrom).ToString("000")).Append("°.");
            // (the shear of the lowest six kilometres, which says what kind of storm: under 10 m/s ordinary cells, over 20 supercells)
            Level g = m.Levels[0], six = m.Levels[Mathf.Clamp(60, 0, m.Levels.Count - 1)];
            float shear = Mathf.Sqrt((six.WindE - g.WindE) * (six.WindE - g.WindE) + (six.WindN - g.WindN) * (six.WindN - g.WindN));
            o.Append("\n<b>Wind shear</b>, the ground to 6 km: ").Append(Mathf.RoundToInt(shear)).Append(" m/s").Append(cape >= 500f ? (shear > 20f ? " (with this CAPE: supercells, the long-lived storms with hail and tornadoes)" : shear > 10f ? " (with this CAPE: organised storms, multicells and lines)" : " (with this CAPE: short-lived single storms)") : "").Append('.');
            return o.ToString();
        }

        static GUIStyle tag;
        static GUIStyle Tag => tag ?? (tag = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) });

        public static void Draw(World w, double lat, double lon, double ut, Rect room)
        {
            Made m = shown;
            GUILayout.Label("<b>The air above here</b>, as a sounding is drawn (a skew-T against height): <color=#ff6a5a>temperature</color>, <color=#66d96e>dew point</color>, a parcel lifted from the ground (white dashes) and the energy storms can draw on where it is the warmer (<color=#ff9a40>CAPE</color>); the cloud, the freezing level, wind barbs by height on the right, <color=#a8d8ff>icing</color> beside them.", Host.Small);
            GUILayout.BeginHorizontal();
            Rect at = GUILayoutUtility.GetRect(Width, Height, GUILayout.Width(Width), GUILayout.Height(Height));
            if (Event.current.type == EventType.Repaint)
            {
                if (texture != null && m != null) GUI.DrawTexture(at, texture, ScaleMode.StretchToFill, false);
                else Centre.Fill(at, new Color(0.07f, 0.08f, 0.1f, 1f));
            }
            if (m != null) foreach (WeatherMap.Label l in m.Labels) Centre.Shadowed(new Rect(at.x + l.X, at.y + l.Y - 6f, 200f, 14f), l.Text, Tag, l.Colour);
            GUILayout.Space(10f);
            GUILayout.Label(m != null ? m.Said : "Working out the sounding...", Host.Small, GUILayout.Width(Mathf.Max(200f, room.width - Width - 20f)));
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// (Since 0.6.0.) The weather centre's storms of note: every low of the middle latitudes, tropical cyclone and squall line on
    /// the world, and the storms, downbursts and tornadoes near the place: where each is, how strong, which way it goes and how
    /// fast, and how near it will come to the place in the next day, and when (from its path: the model's, so it will).
    /// </summary>
    static class StormList
    {
        sealed class Row { public string Kind, Where, Strength, Going, Nearest; public double Distance; public bool Near; }

        static List<Row> rows;
        static Task<List<Row>> work;
        static float madeAt = -100f;
        static Vector2 scroll;

        public static void Shut() { rows = null; work = null; madeAt = -100f; }

        public static void Tick(World w, double lat, double lon, double ut)
        {
            if (work != null && work.IsCompleted) { if (!work.IsFaulted) rows = work.Result; else Weather.Log("the storms' list failed: " + work.Exception?.GetBaseException()); work = null; }
            if (work == null && Time.realtimeSinceStartup - madeAt > 5f) { madeAt = Time.realtimeSinceStartup; work = Task.Run(() => Make(w, lat, lon, ut)); }
        }

        static string Bearing(World w, double lat, double lon, double la, double lo, out double distance)
        {
            distance = Model.Distance(w, lat, lon, la, lo);
            Model.Toward(lat, lon, la, lo, out float te, out float tn);
            int brg = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(te, tn) * Mathf.Rad2Deg, 360f));
            return (distance / 1000.0).ToString("F0") + " km at " + brg.ToString("000") + "°";
        }

        static string Way(float east, float north)
        {
            float s = Mathf.Sqrt(east * east + north * north);
            if (s < 0.3f) return "nearly still";
            int to = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(east, north) * Mathf.Rad2Deg, 360f));
            return "towards " + to.ToString("000") + "° at " + (s * 3.6f).ToString("F0") + " km/h";
        }

        static List<Row> Make(World w, double lat, double lon, double ut)
        {
            var list = new List<Row>();
            // ---- the systems, with their paths over the next day
            var paths = Synoptic.Paths(w, ut, ut + 24.0 * 3600.0, 1800.0);
            foreach (Synoptic.Path p in paths)
            {
                if (p.Points.Count == 0 || p.Times[0] > ut + 900.0) continue;
                double near = double.MaxValue, when = 0.0;
                for (int i = 0; i < p.Points.Count; i++) { double d = Model.Distance(w, lat, lon, p.Points[i].x, p.Points[i].y); if (d < near) { near = d; when = p.Times[i] - ut; } }
                float me = 0f, mn = 0f;
                if (p.Points.Count > 1)
                {
                    double d = Model.Distance(w, p.Points[0].x, p.Points[0].y, p.Points[1].x, p.Points[1].y);
                    Model.Toward(p.Points[0].x, p.Points[0].y, p.Points[1].x, p.Points[1].y, out float te, out float tn);
                    float v = (float)(d / Math.Max(1.0, p.Times[1] - p.Times[0]));
                    me = te * v; mn = tn * v;
                }
                var r = new Row { Where = Centre.Deg(p.Points[0].x, "N", "S") + " " + Centre.Deg(p.Points[0].y, "E", "W") + ", " + Bearing(w, lat, lon, p.Points[0].x, p.Points[0].y, out double dist), Distance = dist, Going = Way(me, mn) };
                // (coming: if it will be much nearer than it is, not a few kilometres nearer before it turns away)
                r.Nearest = near < Math.Min(0.8 * dist, dist - 50000.0) ? "comes within " + (near / 1000.0).ToString("F0") + " km in " + Hours(when) : near < dist - 1000.0 ? "passes far off" : "going away";
                if (p.Kind == Synoptic.SystemKind.Low)
                {
                    float pc = Synoptic.Pressure(w, p.Points[0].x, p.Points[0].y, ut);
                    float s0 = p.Strength[0], s1 = p.Strength[Math.Min(p.Strength.Count - 1, 6)];
                    r.Kind = "Low";
                    r.Strength = pc.ToString("F0") + " hPa, " + (s1 > s0 + 0.03f ? "deepening" : s1 < s0 - 0.03f ? "filling" : "mature");
                }
                else if (p.Kind == Synoptic.SystemKind.Cyclone)
                {
                    int cat = Synoptic.Category(p.Strength[0]);
                    float pc = Synoptic.Pressure(w, p.Points[0].x, p.Points[0].y, ut);
                    r.Kind = cat < 0 ? "Tropical depression" : cat == 0 ? "Tropical storm" : "Hurricane, category " + cat;
                    r.Strength = Mathf.RoundToInt(p.Strength[0]) + " m/s (" + Mathf.RoundToInt(p.Strength[0] * 1.94384f) + " kt), " + pc.ToString("F0") + " hPa";
                }
                else { r.Kind = "Squall line"; r.Strength = "strength " + Mathf.RoundToInt(p.Strength[0] * 100f) + "%"; }
                list.Add(r);
            }
            // ---- near the place: the storms within 100 km, their downbursts and tornadoes
            var cells = new List<WeatherCell>();
            Model.Cells(w, lat, lon, 100000.0, ut, cells);
            foreach (WeatherCell c in cells)
            {
                if (c.Strength < 0.3f) continue;
                Base b = Model.Sample(w, c.Latitude, c.Longitude, ut);
                float hail = Model.Hail(w, c, b, c.Latitude, c.Longitude, out float stones);
                var r = new Row { Near = true, Kind = c.Thunder ? "Thunderstorm" : "Shower", Where = Bearing(w, lat, lon, c.Latitude, c.Longitude, out double dist), Distance = dist, Going = Way((float)c.Drift.x, (float)c.Drift.y) };
                r.Strength = "tops " + (c.Top / 1000f).ToString("F0") + " km, rain " + Mathf.RoundToInt(50f * c.Rain * c.Rain) + " mm/h" + (hail > 0.05f ? ", hail " + stones.ToString("F0") + " mm" : "") + ", " + Stage(c.Life);
                double best = double.MaxValue, when = 0.0;
                double cosLat = Math.Max(0.05, Math.Cos(c.Latitude * Math.PI / 180.0));
                for (int m = 0; m <= 90; m += 3)
                {
                    double s = m * 60.0;
                    double la = c.Latitude + c.Drift.y * s / w.Radius * 180.0 / Math.PI, lo = c.Longitude + c.Drift.x * s / (w.Radius * cosLat) * 180.0 / Math.PI;
                    double d = Model.Distance(w, lat, lon, la, lo);
                    if (d < best) { best = d; when = s; }
                }
                r.Nearest = best < dist - 500.0 ? (best < Math.Max(c.Radius, 3000f) ? "here in " + Math.Round(when / 60.0) + " min" : "passes " + (best / 1000.0).ToString("F0") + " km off in " + Math.Round(when / 60.0) + " min") : "going away";
                list.Add(r);
            }
            var twisters = new List<Twister>();
            Violent.Twisters(w, cells, ut, twisters);
            foreach (Twister t in twisters)
                list.Add(new Row { Near = true, Kind = t.Water ? "Waterspout" : "Tornado", Where = Bearing(w, lat, lon, t.Lat, t.Lon, out double dist), Distance = dist, Strength = Mathf.RoundToInt(t.Speed) + " m/s, " + Mathf.RoundToInt(t.Core * 2f) + " m across", Going = Way(t.MoveE, t.MoveN), Nearest = "" });
            list.Sort((a, b) => a.Near != b.Near ? (a.Near ? 1 : -1) : a.Distance.CompareTo(b.Distance));
            return list;
        }

        static string Stage(float life) => life < 0.3f ? "growing" : life < 0.7f ? "mature" : "dying";

        static string Hours(double s) => s < 3600.0 ? Math.Round(s / 60.0) + " min" : (s / 3600.0).ToString("F1") + " h";

        public static void Draw(World w, double lat, double lon, double ut, Rect room)
        {
            List<Row> r = rows;
            GUILayout.Label("<b>Storms of note</b>: the world's lows, tropical cyclones and squall lines, nearest first; then the storms within 100 km of here. Where each is from here, how strong, which way it goes, and how near it will come (its path is the model's, so it will).", Host.Small);
            if (r == null) { GUILayout.Label("Looking...", Host.Small); return; }
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(room.height - 90f));
            bool nearShown = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>What</b>", Host.Small, GUILayout.Width(150f)); GUILayout.Label("<b>Where</b>", Host.Small, GUILayout.Width(230f)); GUILayout.Label("<b>How strong</b>", Host.Small, GUILayout.Width(200f)); GUILayout.Label("<b>Going</b>", Host.Small, GUILayout.Width(150f)); GUILayout.Label("<b>Here?</b>", Host.Small);
            GUILayout.EndHorizontal();
            foreach (Row x in r)
            {
                if (x.Near && !nearShown) { nearShown = true; GUILayout.Space(6f); GUILayout.Label("<b>Near here</b>", Host.Small); }
                GUILayout.BeginHorizontal();
                string colour = x.Kind.StartsWith("Hurricane") || x.Kind == "Tornado" || x.Kind == "Waterspout" ? "#ff8a7a" : x.Kind == "Thunderstorm" ? "#ffe08a" : x.Kind == "Low" ? "#ff9c8c" : "#dddddd";
                GUILayout.Label("<color=" + colour + ">" + x.Kind + "</color>", Host.Small, GUILayout.Width(150f));
                GUILayout.Label(x.Where, Host.Small, GUILayout.Width(230f));
                GUILayout.Label(x.Strength, Host.Small, GUILayout.Width(200f));
                GUILayout.Label(x.Going, Host.Small, GUILayout.Width(150f));
                GUILayout.Label(x.Nearest, Host.Small);
                GUILayout.EndHorizontal();
            }
            if (!nearShown) { GUILayout.Space(6f); GUILayout.Label("<color=#aaaaaa>No storms within 100 km.</color>", Host.Small); }
            GUILayout.EndScrollView();
        }
    }
}
