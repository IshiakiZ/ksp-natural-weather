using System;
using System.Collections.Generic;
using System.Diagnostics;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0.) The weather centre's map, worked out from the model and drawn on another thread, handed over as pixels and
    /// the words to write on them. Round a place (100 to 1600 km across, distances from it true) or the whole world, at a
    /// moment (now, behind or ahead: the weather is the model's own, so the map of an hour ahead is the weather that will be):
    ///
    ///   * satellite pictures: infrared, the cloud tops by how cold they are (higher, colder, whiter; the ground dark where it is
    ///     warm), day and night; or visible, the sunlit side of the world as a camera sees it, the clouds by how thick they are;
    ///   * radar: what falls, in the reflectivity a weather radar would see (dBZ, Marshall and Palmer's rule for rain), in the
    ///     National Weather Service's colours, snow in blues and freezing rain and ice pellets in pinks; every storm cell drawn;
    ///   * the surface analysis: isobars (every 1, 2 or 4 hPa by the size of the map), the highs and lows with their pressure,
    ///     the fronts with their symbols (cold: blue triangles; warm: red half-discs; occluded: purple, both);
    ///   * the wind at 10 m as barbs (a half barb 5 knots, a whole 10, a pennant 50; on the low pressure's side);
    ///   * lightning of the last twenty minutes, by its age from white to dark red, as the storms have flashed;
    ///   * the systems' paths: each low's, cyclone's and squall line's, from six hours back to twelve (a day for a cyclone)
    ///     ahead, marked every three hours; and each storm cell's last hour and next, in steps of a quarter of an hour, as a
    ///     weather radar tracks them, with when it will reach the place if it is coming;
    ///   * the temperature at the ground, and the freezing line.
    /// </summary>
    sealed class WeatherMap
    {
        [Flags]
        public enum Show { Infrared = 1, Visible = 2, Radar = 4, Pressure = 8, Wind = 16, Lightning = 32, Paths = 64, Temperature = 128 }

        public struct Label { public float X, Y; public string Text; public Color Colour; public bool Big; }

        // ---- what is asked for
        public World W;
        public double Lat, Lon;          // the middle
        public double Ut;                // the moment shown
        public double Now;               // the game's time now (for the lightning's age, the paths' past and future)
        public double Half;              // half the map's width, m (the whole world: unused)
        public bool Whole;
        public int Width, Height;
        public Show Layers;
        public double PlaceLat, PlaceLon; // the craft, or the space centre

        // ---- what is made
        public Color32[] Pixels;
        public readonly List<Label> Labels = new List<Label>();
        public string Said = "";
        public float Took;               // ms
        public ChartView View;

        static readonly List<WeatherCell> noCells = new List<WeatherCell>();

        /// <summary>The key a map is kept by: the same place, size, layers and moment make the same map.</summary>
        public string Key => (W != null ? W.Name : "") + "|" + Lat.ToString("F3") + "|" + Lon.ToString("F3") + "|" + (Whole ? "w" : Half.ToString("F0")) + "|" + (int)Layers + "|" + Math.Round(Ut / 60.0);

        public void Make()
        {
            var watch = Stopwatch.StartNew();
            var view = new ChartView(Lat, Lon, W.Radius, Half, Width, Height, Whole);
            View = view;
            var c = new Canvas(Width, Height);

            // ---- the storm cells of the whole map, found once (asked place by place, the model's own cells about a place were 3.5 s
            // of a 400 km map: such a map is 38 degrees across on Kerbin, 361 of the squares the model keeps the cells of, and it
            // keeps 16), and handed to each place by buckets about 20 km across: those whose rain reaches into the bucket
            var cells = new List<WeatherCell>();
            // (a map that takes in much of the world, as Kerbin's 1600 km does (half of it), walks the world's own lattice: Cells asked
            // for a wide reach walks a cube of it as wide, 3 s there against 0.6)
            if (Whole || Half * 1.45 > 0.6 * W.Radius)
            {
                Model.AllCells(W, Ut, cells);
                if (!Whole) cells.RemoveAll(cell => Model.Distance(W, Lat, Lon, cell.Latitude, cell.Longitude) > Half * 1.45 + cell.Radius);
            }
            else Model.Cells(W, Lat, Lon, Half * 1.45 + 30000.0, Ut, cells);
            int bucket = Mathf.Max(8, Mathf.RoundToInt(view.Pixels(20000.0)));
            int bw = Width / bucket + 1, bh = Height / bucket + 1;
            var buckets = new List<WeatherCell>[bw * bh];
            if (!Whole)
            {
                // (each cell into the buckets its rain reaches, by where it is on the map: a bucket asked of every cell was 2 s on a wide map)
                foreach (WeatherCell cell in cells)
                {
                    if (!view.ToPixel(cell.Latitude, cell.Longitude, out float cx, out float cy)) continue;
                    float r = view.Pixels(cell.Radius + 2000.0) + 1f;
                    int x0 = Mathf.FloorToInt((cx - r) / bucket), x1 = Mathf.FloorToInt((cx + r) / bucket), y0 = Mathf.FloorToInt((cy - r) / bucket), y1 = Mathf.FloorToInt((cy + r) / bucket);
                    for (int by = Math.Max(0, y0); by <= Math.Min(bh - 1, y1); by++)
                        for (int bx = Math.Max(0, x0); bx <= Math.Min(bw - 1, x1); bx++)
                            (buckets[by * bw + bx] ?? (buckets[by * bw + bx] = new List<WeatherCell>())).Add(cell);
                }
                for (int k = 0; k < buckets.Length; k++) if (buckets[k] == null) buckets[k] = noCells;
            }
            Lap("cells");

            // ---- the model asked on a grid, a sample every few pixels, the rows shared among a few threads
            int step = 4;
            int gw = Width / step + 2, gh = Height / step + 2;
            int n = gw * gh;
            var press = new float[n]; var land = new float[n]; var temp = new float[n]; var ir = new float[n];
            var vis = new Color[n]; var dbz = new float[n]; var kind = new byte[n];
            bool wantVisible = (Layers & Show.Visible) != 0;
            float sunLat = (float)W.SunLat, sunLon = (float)(W.SunLon - 360.0 * (Ut - W.SunAt) / Math.Max(W.Day, 1.0));
            var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3)) };
            System.Threading.Tasks.Parallel.For(0, gh, options, j =>
            {
                for (int i = 0; i < gw; i++)
                {
                    int k = j * gw + i;
                    view.ToPlace(i * step, j * step, out double la, out double lo);
                    Base b = Model.Sample(W, la, lo, Ut);
                    int bx = Math.Min(bw - 1, Math.Max(0, i * step / bucket)), by = Math.Min(bh - 1, Math.Max(0, j * step / bucket));
                    WeatherHere h = Model.Build(W, la, lo, Ut, ref b, Whole ? noCells : buckets[by * bw + bx]);
                    press[k] = Synoptic.Pressure(W, la, lo, Ut, b);
                    land[k] = b.Land;
                    temp[k] = h.Temperature;
                    ir[k] = Infrared(h, Mathf.Max(0f, b.Ground));
                    if (wantVisible) vis[k] = Visible(h, b, la, lo, sunLat, sunLon);
                    dbz[k] = Reflectivity(h, out kind[k]);
                }
            });
            Lap("grid");

            // ---- the ground, or a satellite's picture of it
            bool infrared = (Layers & Show.Infrared) != 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float gx = x / (float)step, gy = y / (float)step;
                    int i0 = Math.Min((int)gx, gw - 2), j0 = Math.Min((int)gy, gh - 2);
                    float fx = gx - i0, fy = gy - j0;
                    int k = j0 * gw + i0;
                    Color px;
                    if (wantVisible) px = Lerp4(vis[k], vis[k + 1], vis[k + gw], vis[k + gw + 1], fx, fy);
                    else if (infrared) { float g = Bi(ir, k, gw, fx, fy); px = new Color(g, g, g, 1f); }
                    else { float l = Bi(land, k, gw, fx, fy); px = Color.Lerp(new Color(0.09f, 0.13f, 0.19f), new Color(0.30f, 0.30f, 0.26f), Mathf.Clamp01((l - 0.4f) * 5f)); }
                    c.Px[y * Width + x] = px;
                }

            Lap("ground");
            // ---- the temperature at the ground
            if ((Layers & Show.Temperature) != 0)
            {
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        float gx = x / (float)step, gy = y / (float)step;
                        int i0 = Math.Min((int)gx, gw - 2), j0 = Math.Min((int)gy, gh - 2);
                        c.Put(x, y, Warmth(Bi(temp, j0 * gw + i0, gw, gx - i0, gy - j0) - 273.15f), 0.45f);
                    }
                Contours(c, temp, gw, gh, step, new[] { 273.15f }, new Color(0.55f, 0.8f, 1f, 0.9f), 1.4f, 5f, 4f, null, 0f);
            }

            Lap("temperature");
            // ---- radar
            if ((Layers & Show.Radar) != 0)
            {
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        float gx = x / (float)step, gy = y / (float)step;
                        int i0 = Math.Min((int)gx, gw - 2), j0 = Math.Min((int)gy, gh - 2);
                        int k = j0 * gw + i0;
                        float z = Bi(dbz, k, gw, gx - i0, gy - j0);
                        if (z < 5f) continue;
                        byte kd = kind[k + (gx - i0 > 0.5f ? 1 : 0) + (gy - j0 > 0.5f ? gw : 0)];
                        c.Put(x, y, RadarColour(z, kd), 0.85f);
                    }
            }

            Lap("radar");
            // ---- the coasts
            Contours(c, land, gw, gh, step, new[] { 0.5f }, infrared || wantVisible ? new Color(0.95f, 0.85f, 0.4f, 0.55f) : new Color(0.75f, 0.72f, 0.6f, 0.7f), 1f, 0f, 0f, null, 0f);

            Lap("coasts");
            // ---- the storm cells: drawn on the radar, and tracked
            if ((Layers & Show.Radar) != 0)
                foreach (WeatherCell cell in cells)
                {
                    if (!view.ToPixel(cell.Latitude, cell.Longitude, out float cx, out float cy)) continue;
                    float r = view.Pixels(cell.Radius);
                    if (r < 0.7f || cx < -1.6f * r || cy < -1.6f * r || cx > Width + 1.6f * r || cy > Height + 1.6f * r) continue;
                    // (a cell as a radar sees one, not a bullseye: drawn out along the way it goes, ragged, its core towards its front
                    // and its edge sharper there; each its own shape, from its number)
                    Vector2 way = new Vector2((float)cell.Drift.x, -(float)cell.Drift.y);
                    way = way.sqrMagnitude > 0.25f ? way.normalized : new Vector2(1f, 0f);
                    uint seed = (uint)cell.Id;
                    float p1 = 6.283f * Noise.Unit(Noise.Hash(seed ^ 0x31u)), p2 = 6.283f * Noise.Unit(Noise.Hash(seed ^ 0x32u));
                    Vector2 core = new Vector2(cx, cy) + way * (0.25f * r);
                    for (int y = Mathf.FloorToInt(cy - 1.6f * r); y <= Mathf.CeilToInt(cy + 1.6f * r); y++)
                        for (int x = Mathf.FloorToInt(cx - 1.6f * r); x <= Mathf.CeilToInt(cx + 1.6f * r); x++)
                        {
                            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                            float dx = x - cx, dy = y - cy;
                            float along = dx * way.x + dy * way.y, across = -dx * way.y + dy * way.x;
                            float ax = along / (along > 0f ? 1.15f : 1.5f), ay = across / 0.85f;
                            float d = Mathf.Sqrt(ax * ax + ay * ay) / r;
                            float theta = Mathf.Atan2(ay, ax);
                            float rough = (float)Noise.Perlin(x * 0.35 / Math.Max(r, 1f) * 3.0, y * 0.35 / Math.Max(r, 1f) * 3.0, seed * 0.01, seed);
                            float lump = 1f + 0.18f * Mathf.Sin(3f * theta + p1) + 0.12f * Mathf.Sin(5f * theta + p2) + 0.3f * rough;
                            float e = d / Mathf.Max(lump, 0.4f);
                            float rain = cell.Rain * Mathf.Exp(-Mathf.Pow(e, 2.4f) / 0.3f);
                            float toCore = (new Vector2(x, y) - core).magnitude / r;
                            float z = RainDbz(rain) + (cell.Thunder && toCore < 0.3f ? 7f * cell.Strength * (1f - toCore / 0.3f) : 0f);
                            if (z < 5f) continue;
                            c.Put(x, y, RadarColour(z, 0), 0.85f);
                        }
                }

            Lap("radar cells");
            // ---- the surface analysis: isobars, highs and lows, fronts. The pressure smoothed over some 40 km first, as an analysis is
            // drawn at the scale of the systems (the model's own wind sees its field so, by its gradient over 40 km); unsmoothed, the
            // moisture's grain made the isobars wander and one low several
            var labels = Labels;
            if ((Layers & Show.Pressure) != 0)
            {
                float sigma = (float)(40000.0 / (step * (Whole ? W.Radius * Deg * view.MetresPerPixel : view.MetresPerPixel)));
                Smooth(press, gw, gh, Mathf.Clamp(Mathf.RoundToInt(sigma), 1, 24));
                float every = Whole || Half > 900000.0 ? 4f : Half > 250000.0 ? 2f : 1f;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (float p in press) { lo = Mathf.Min(lo, p); hi = Mathf.Max(hi, p); }
                var levels = new List<float>();
                for (float p = Mathf.Ceil(lo / every) * every; p <= hi; p += every) levels.Add(p);
                Contours(c, press, gw, gh, step, levels.ToArray(), new Color(0.95f, 0.95f, 0.95f, 0.7f), 1.15f, 0f, 0f, labels, every);
                // (a centre stands out from what is within 150 km of it: on the world's map, from what is within a few points)
                Centres(press, gw, gh, step, labels, Mathf.Clamp(Mathf.RoundToInt((float)(150000.0 / (step * (Whole ? W.Radius * Deg * view.MetresPerPixel : view.MetresPerPixel)))), 3, 30));
                var fronts = new List<Synoptic.Front>();
                Synoptic.Fronts(W, Ut, fronts);
                foreach (Synoptic.Front f in fronts) DrawFront(c, view, f);
                foreach (Squall q in Violent.Squalls(W, Ut))
                {
                    if (q.Now < 0.1f) continue;
                    var line = new List<Vector2>();
                    for (int s = -5; s <= 5; s++)
                    {
                        double along = 0.5 * q.Length * s / 5.0;
                        double la = q.Lat + along * q.AlongN / W.Radius / Deg, lo2 = q.Lon + along * q.AlongE / (W.Radius * Math.Max(0.05, Math.Cos(q.Lat * Deg))) / Deg;
                        if (view.ToPixel(la, lo2, out float x, out float y)) line.Add(new Vector2(x, y));
                    }
                    c.Lines(line, new Color(0.9f, 0.35f, 0.9f, 0.95f), 2f, 9f, 4f);
                }
            }

            Lap("analysis");
            // ---- the wind at 10 m
            if ((Layers & Show.Wind) != 0)
            {
                int every = Whole ? 32 : 40;
                for (int y = every / 2; y < Height; y += every)
                    for (int x = every / 2; x < Width; x += every)
                    {
                        view.ToPlace(x, y, out double la, out double lo);
                        Column col = Model.ColumnAt(W, la, lo, Ut);
                        Barb(c, x, y, col.UE, col.UN, la >= 0.0, new Color(0.98f, 0.98f, 0.98f, 0.9f));
                    }
            }

            Lap("wind");
            // ---- lightning of the last twenty minutes (as the storms flash: Lightning.Schedule's rule); on the whole world, the strikes
            // to the ground of the last five only (every storm's flashes over the world were some twenty-five thousand marks)
            int strikes = 0;
            if ((Layers & Show.Lightning) != 0 && Ut <= Now + 1.0)
            {
                long last = (long)Math.Floor(Ut / 2.0);
                // (and on the 1600 km map too, the strikes to the ground of the last five minutes, as dots: crosses for twenty minutes
                // were a blot over each storm there, some 28 strikes each, that hid the fronts)
                long slots = Whole || Half > 500000.0 ? 150 : 600;
                foreach (WeatherCell cell in cells)
                {
                    if (!cell.Thunder || cell.Strength < 0.25f) continue;
                    float perMinute = 8f * cell.Strength * cell.Strength * W.Climate.Lightning;
                    for (long s = last - slots; s <= last; s++)
                    {
                        uint hh = Noise.Hash((uint)cell.Id ^ Noise.Hash((uint)s * 0x9e3779b1U));
                        if (Noise.Unit(hh) > perMinute * 2f / 60f) continue;
                        double t = s * 2.0 + 2.0 * Noise.Unit(Noise.Hash(hh ^ 0x51u));
                        if (t > Ut) continue;
                        // (the cell then: where its drift had it)
                        double back = Ut - t;
                        WeatherCell then = cell;
                        then.Latitude = cell.Latitude - cell.Drift.y * back / W.Radius / Deg;
                        then.Longitude = cell.Longitude - cell.Drift.x * back / (W.Radius * Math.Max(0.05, Math.Cos(cell.Latitude * Deg))) / Deg;
                        WeatherFlash f = Lightning.Make(W.Body, W, then, t, hh);
                        if ((Whole || Half > 500000.0) && !f.Ground) continue;
                        if (!view.ToPixel(f.Latitude, f.Longitude, out float x, out float y) || x < 0 || y < 0 || x >= Width || y >= Height) continue;
                        bool small = Whole || Half > 500000.0;
                        Color age = Aged((float)back);
                        age.a = small ? 0.55f : 0.85f;
                        float arm = small ? 1.6f : Half > 120000.0 ? 2.4f : 3f;
                        if (small) c.Disc(x, y, 0.75f, age);
                        else if (f.Ground) { c.Line(x - arm, y, x + arm, y, age, 1.3f); c.Line(x, y - arm, x, y + arm, age, 1.3f); }
                        else if (Half < 120000.0) c.Disc(x, y, 0.9f, age);
                        else continue;
                        strikes++;
                    }
                }
            }

            Lap("lightning");
            // ---- the paths of the systems, and the storm cells' tracks
            if ((Layers & Show.Paths) != 0)
            {
                foreach (Synoptic.Path p in Synoptic.Paths(W, Ut - 6.0 * 3600.0, Ut + 24.0 * 3600.0, 1800.0))
                {
                    if (p.Kind == Synoptic.SystemKind.Low && p.Times[p.Times.Count - 1] > Ut + 12.0 * 3600.0) Trim(p, Ut + 12.0 * 3600.0);
                    DrawPath(c, view, p, Ut, labels);
                }
                // (each storm cell's track: not on the whole world's map, where they are too small to be told apart; on the
                // largest of the rest, the thunderstorms that matter only)
                if (!Whole) foreach (WeatherCell cell in cells) if (Half < 150000.0 ? cell.Strength > 0.2f : Half < 500000.0 ? cell.Thunder && cell.Strength > 0.5f : cell.Thunder && cell.Strength > 0.75f) Track(c, view, cell, labels);
            }

            Lap("paths");
            // ---- where the craft is (or the space centre)
            if (view.ToPixel(PlaceLat, PlaceLon, out float mx, out float my))
            {
                c.Ring(mx, my, 5f, new Color(0f, 0f, 0f, 0.8f), 3f);
                c.Ring(mx, my, 5f, new Color(1f, 1f, 1f, 1f), 1.4f);
                c.Disc(mx, my, 1.2f, Color.white);
            }

            Pixels = c.Rows();
            c.Free();
            Lap("rows");
            Took = (float)watch.Elapsed.TotalMilliseconds;
            Said = (strikes > 0 ? strikes + (Whole || Half > 500000.0 ? " strikes to the ground in the last 5 minutes. " : " strikes in the last 20 minutes. ") : "") + "Made in " + Took.ToString("F0") + " ms" + (laps.Length > 0 ? " (" + laps + ")" : "") + ".";
        }

        // (development build: where the time went, part by part)
        readonly Stopwatch lapWatch = Stopwatch.StartNew();
        string laps = "";
        [Conditional("DEV")]
        void Lap(string part)
        {
            laps += (laps.Length > 0 ? ", " : "") + part + " " + lapWatch.Elapsed.TotalMilliseconds.ToString("F0");
            lapWatch.Reset(); lapWatch.Start();
        }

        const double Deg = Math.PI / 180.0;

        static float Bi(float[] a, int k, int gw, float fx, float fy) => Mathf.Lerp(Mathf.Lerp(a[k], a[k + 1], fx), Mathf.Lerp(a[k + gw], a[k + gw + 1], fx), fy);
        static Color Lerp4(Color a, Color b, Color c, Color d, float fx, float fy) => Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);

        // ---------------------------------------------------------------- what each pixel is

        /// <summary>
        /// An infrared satellite's brightness at a place: what it sees is warmth, the colder the whiter (300 K black, 200 K white),
        /// through the cloud layers from the top down: the highest's top where it covers the sky, the ground through its gaps;
        /// thin high cloud only partly. The air cools some 6.5 degrees a kilometre up.
        /// </summary>
        static float Infrared(WeatherHere h, float ground)
        {
            float g = Bright(h.Temperature);
            for (int i = 0; i < 3; i++)
            {
                CloudLayer l = i == 0 ? h.Low : i == 1 ? h.Middle : h.High;
                if (l.Kind == CloudKind.None || l.Cover <= 0.01f) continue;
                float top = Mathf.Max(l.Top, l.Base + 200f);
                float t = Mathf.Max(200f, h.Temperature - 0.0065f * (top - ground));
                float opaque = l.Kind == CloudKind.Cirrus ? 0.45f + 0.4f * l.Water : 0.75f + 0.25f * Mathf.Clamp01(l.Water * 2f);
                g = Mathf.Lerp(g, Bright(t), Mathf.Clamp01(l.Cover * opaque));
            }
            return g;
        }

        static float Bright(float kelvin) => Mathf.Clamp01((300f - kelvin) / 100f) * 0.92f + 0.04f;

        /// <summary>A visible satellite's picture at a place: the ground (sea dark, land green-brown, snow white where it is freezing over land) and the clouds by how thick they are, as the sun lights them.</summary>
        static Color Visible(WeatherHere h, Base b, double lat, double lon, float sunLat, float sunLon)
        {
            double cz = Math.Sin(lat * Deg) * Math.Sin(sunLat * Deg) + Math.Cos(lat * Deg) * Math.Cos(sunLat * Deg) * Math.Cos((lon - sunLon) * Deg);
            float light = Mathf.Pow(Mathf.Clamp01((float)cz + 0.03f), 0.7f);
            Color ground = Color.Lerp(new Color(0.03f, 0.07f, 0.13f), new Color(0.22f, 0.27f, 0.14f), Mathf.Clamp01((b.Land - 0.4f) * 5f));
            if (b.Land > 0.5f && h.Temperature < 266f) ground = Color.Lerp(ground, new Color(0.85f, 0.87f, 0.9f), 0.8f);
            Color px = ground;
            for (int i = 0; i < 3; i++)
            {
                CloudLayer l = i == 0 ? h.Low : i == 1 ? h.Middle : h.High;
                if (l.Kind == CloudKind.None || l.Cover <= 0.01f) continue;
                float thick = Mathf.Clamp01((l.Top - l.Base) / 4000f);
                float white = l.Kind == CloudKind.Cirrus ? 0.55f : 0.7f + 0.3f * thick;
                float opaque = l.Kind == CloudKind.Cirrus ? 0.4f : 0.6f + 0.4f * Mathf.Max(thick, l.Water);
                px = Color.Lerp(px, new Color(white, white, white * 1.02f), Mathf.Clamp01(l.Cover * opaque));
            }
            if (h.Fog > 0.3f) px = Color.Lerp(px, new Color(0.75f, 0.76f, 0.78f), 0.6f * h.Fog);
            px *= light;
            px.a = 1f;
            return px;
        }

        /// <summary>
        /// The reflectivity a weather radar would see at a place, dBZ, and what falls there (0 rain, 1 snow, 2 freezing rain or ice
        /// pellets). What falls is Rain, all of it (0.1 drizzle, 0.3 steady rain, 0.6 heavy, 1 a cloudburst of about 50 mm an hour);
        /// Snow, Freezing and Pellets are the shares of it that are each.
        /// </summary>
        static float Reflectivity(WeatherHere h, out byte kind)
        {
            float mix = h.Freezing + h.Pellets;
            kind = mix > 0.3f ? (byte)2 : h.Snow > 0.5f ? (byte)1 : (byte)0;
            float z = kind == 1 ? SnowDbz(h.Rain) : RainDbz(h.Rain);
            if (h.Hail > 0.05f) z = Mathf.Max(z, 55f + 10f * Mathf.Clamp01(h.HailSize / 40f));
            return z;
        }

        /// <summary>Rain's reflectivity: the model's 0 to 1 taken as 50 mm an hour times its square (Keystone's scale: 0.1 a drizzle of 0.5 mm/h, 0.3 a steady 4.5, 0.6 a heavy 18, 1 a cloudburst of 50), then Marshall and Palmer's Z = 200 R^1.6: 18, 34, 43 and 50 dBZ.</summary>
        static float RainDbz(float rain)
        {
            if (rain <= 0.005f) return 0f;
            float r = 50f * rain * rain;
            return 10f * Mathf.Log10(200f * Mathf.Pow(r, 1.6f));
        }

        /// <summary>Snow's, the same fall as water, by Z = 2000 S^2 (Sekhon and Srivastava's for snow): flakes reflect more than drops of the same water.</summary>
        static float SnowDbz(float fall)
        {
            if (fall <= 0.005f) return 0f;
            float s = 50f * fall * fall;
            return 10f * Mathf.Log10(2000f * s * s);
        }

        // (the National Weather Service's reflectivity colours, 5 dBZ apart from 5)
        static readonly Color[] rainColours =
        {
            C(4, 233, 231), C(1, 159, 244), C(3, 0, 244), C(2, 253, 2), C(1, 197, 1), C(0, 142, 0), C(253, 248, 2), C(229, 188, 0),
            C(253, 149, 0), C(253, 0, 0), C(212, 0, 0), C(188, 0, 0), C(248, 0, 253), C(152, 84, 198), C(253, 253, 253)
        };
        static readonly Color[] snowColours = { C(140, 200, 255), C(100, 170, 255), C(70, 135, 245), C(45, 100, 230), C(30, 70, 210), C(20, 45, 180), C(15, 30, 150), C(10, 20, 120) };
        static readonly Color[] mixColours = { C(255, 190, 225), C(250, 150, 205), C(240, 110, 185), C(225, 70, 165), C(200, 40, 145), C(170, 20, 125) };

        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        public static Color RadarColour(float dbz, byte kind)
        {
            Color[] table = kind == 1 ? snowColours : kind == 2 ? mixColours : rainColours;
            int k = Mathf.Clamp(Mathf.FloorToInt((dbz - 5f) / 5f), 0, table.Length - 1);
            return table[k];
        }

        /// <summary>A colour for a temperature (°C): violet at -30, blue, white at the freezing point, green, yellow, red at 35.</summary>
        public static Color Warmth(float c)
        {
            Color[] stops = { new Color(0.45f, 0.2f, 0.65f), new Color(0.2f, 0.35f, 0.9f), new Color(0.85f, 0.9f, 1f), new Color(0.3f, 0.75f, 0.35f), new Color(0.95f, 0.85f, 0.25f), new Color(0.9f, 0.25f, 0.15f) };
            float[] at = { -30f, -12f, 0f, 12f, 24f, 35f };
            if (c <= at[0]) return stops[0];
            for (int i = 1; i < at.Length; i++) if (c <= at[i]) return Color.Lerp(stops[i - 1], stops[i], (c - at[i - 1]) / (at[i] - at[i - 1]));
            return stops[stops.Length - 1];
        }

        /// <summary>Lightning's colour by its age (seconds): white now, yellow, orange, red, dark red at twenty minutes (Blitzortung's way).</summary>
        static Color Aged(float s)
        {
            float m = s / 60f;
            if (m < 2f) return new Color(1f, 1f, 1f, 1f);
            if (m < 5f) return new Color(1f, 0.95f, 0.3f, 1f);
            if (m < 10f) return new Color(1f, 0.6f, 0.15f, 1f);
            if (m < 15f) return new Color(0.95f, 0.2f, 0.1f, 1f);
            return new Color(0.6f, 0.08f, 0.06f, 1f);
        }

        // ---------------------------------------------------------------- lines of equal value

        /// <summary>
        /// Lines of equal value through the grid (marching squares), drawn; labelled if labels are given: once near each end of
        /// each level's line across the map (an isobar's last digits).
        /// </summary>
        static void Contours(Canvas c, float[] v, int gw, int gh, int step, float[] levels, Color colour, float width, float dash, float gap, List<Label> labels, float every)
        {
            foreach (float level in levels)
            {
                float leftX = float.MaxValue, leftY = 0f, rightX = float.MinValue, rightY = 0f;
                for (int j = 0; j < gh - 1; j++)
                    for (int i = 0; i < gw - 1; i++)
                    {
                        int k = j * gw + i;
                        float a = v[k] - level, b = v[k + 1] - level, d = v[k + gw + 1] - level, e = v[k + gw] - level;
                        int code = (a > 0 ? 1 : 0) | (b > 0 ? 2 : 0) | (d > 0 ? 4 : 0) | (e > 0 ? 8 : 0);
                        if (code == 0 || code == 15) continue;
                        float x0 = i * step, y0 = j * step;
                        // (where the level crosses each edge: top a-b, right b-d, bottom e-d, left a-e)
                        Vector2 top = new Vector2(x0 + step * Frac(a, b), y0), right = new Vector2(x0 + step, y0 + step * Frac(b, d));
                        Vector2 bottom = new Vector2(x0 + step * Frac(e, d), y0 + step), left = new Vector2(x0, y0 + step * Frac(a, e));
                        Vector2 p = Vector2.zero, q = Vector2.zero, p2 = Vector2.zero, q2 = Vector2.zero;
                        bool two = false;
                        switch (code)
                        {
                            case 1: case 14: p = left; q = top; break;
                            case 2: case 13: p = top; q = right; break;
                            case 3: case 12: p = left; q = right; break;
                            case 4: case 11: p = right; q = bottom; break;
                            case 6: case 9: p = top; q = bottom; break;
                            case 7: case 8: p = left; q = bottom; break;
                            case 5: p = left; q = top; p2 = right; q2 = bottom; two = true; break;
                            case 10: p = top; q = right; p2 = left; q2 = bottom; two = true; break;
                        }
                        if (dash > 0f && ((i + j) & 1) == 1) continue;
                        c.Line(p.x, p.y, q.x, q.y, colour, width);
                        if (two) c.Line(p2.x, p2.y, q2.x, q2.y, colour, width);
                        if (labels != null)
                        {
                            Vector2 mid = 0.5f * (p + q);
                            if (mid.x > 24f && mid.y > 12f && mid.y < c.H - 12f && mid.x < leftX) { leftX = mid.x; leftY = mid.y; }
                            if (mid.x < c.W - 24f && mid.y > 12f && mid.y < c.H - 12f && mid.x > rightX) { rightX = mid.x; rightY = mid.y; }
                        }
                    }
                if (labels != null && leftX < float.MaxValue)
                {
                    string text = (Mathf.RoundToInt(level) % 100).ToString("00");
                    labels.Add(new Label { X = leftX, Y = leftY, Text = text, Colour = new Color(0.95f, 0.95f, 0.95f, 0.9f) });
                    if (rightX - leftX > 120f) labels.Add(new Label { X = rightX, Y = rightY, Text = text, Colour = new Color(0.95f, 0.95f, 0.95f, 0.9f) });
                }
            }
        }

        static float Frac(float a, float b) => Mathf.Abs(a - b) < 1e-9f ? 0.5f : Mathf.Clamp01(a / (a - b));

        /// <summary>A grid smoothed in place, about a Gaussian of so many points (three box blurs each way).</summary>
        static void Smooth(float[] v, int gw, int gh, int sigma)
        {
            int r = Math.Max(1, (int)Math.Round(sigma * 0.9));
            var tmp = new float[v.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int j = 0; j < gh; j++)
                    for (int i = 0; i < gw; i++)
                    {
                        float sum = 0f; int n = 0;
                        for (int a = Math.Max(0, i - r); a <= Math.Min(gw - 1, i + r); a++) { sum += v[j * gw + a]; n++; }
                        tmp[j * gw + i] = sum / n;
                    }
                for (int j = 0; j < gh; j++)
                    for (int i = 0; i < gw; i++)
                    {
                        float sum = 0f; int n = 0;
                        for (int b = Math.Max(0, j - r); b <= Math.Min(gh - 1, j + r); b++) { sum += tmp[b * gw + i]; n++; }
                        v[j * gw + i] = sum / n;
                    }
            }
        }

        /// <summary>The highs and lows: the grid's points that are the most or least of the pressure for some way round, and stand out from it by a hectopascal at least.</summary>
        static void Centres(float[] p, int gw, int gh, int step, List<Label> labels, int reach)
        {
            for (int j = reach; j < gh - reach; j++)
                for (int i = reach; i < gw - reach; i++)
                {
                    float v = p[j * gw + i];
                    bool most = true, least = true;
                    float sum = 0f; int count = 0;
                    for (int b = -reach; b <= reach && (most || least); b++)
                        for (int a = -reach; a <= reach; a++)
                        {
                            if (a == 0 && b == 0) continue;
                            float o = p[(j + b) * gw + i + a];
                            if (o >= v) most = false;
                            if (o <= v) least = false;
                            sum += o; count++;
                        }
                    if (!most && !least) continue;
                    float mean = sum / Math.Max(count, 1);
                    if (Mathf.Abs(v - mean) < 1f) continue;
                    labels.Add(new Label { X = i * step, Y = j * step, Text = (most ? "H" : "L") + "\n" + Mathf.RoundToInt(v), Colour = most ? new Color(0.45f, 0.65f, 1f) : new Color(1f, 0.4f, 0.35f), Big = true });
                }
        }

        // ---------------------------------------------------------------- fronts, barbs, paths, tracks

        static void DrawFront(Canvas c, ChartView view, Synoptic.Front f)
        {
            Color colour = f.Kind == Synoptic.FrontKind.Cold ? new Color(0.25f, 0.5f, 1f, 0.95f) : f.Kind == Synoptic.FrontKind.Warm ? new Color(1f, 0.3f, 0.25f, 0.95f) : new Color(0.78f, 0.35f, 0.95f, 0.95f);
            var pts = new List<Vector2>();
            foreach (Vector2d g in f.Points) if (view.ToPixel(g.x, g.y, out float x, out float y)) pts.Add(new Vector2(x, y));
            if (pts.Count < 2) return;
            float width = 1.6f + 1.2f * f.Strength;
            c.Lines(pts, colour, width);
            // (the symbols every so many pixels along it, on the side it goes: triangles for cold, half-discs for warm, by turns for occluded)
            float spacing = 26f, size = 4.5f + 2f * f.Strength, along = spacing * 0.5f;
            int nth = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = (b - a).magnitude;
                if (len < 1e-3f) continue;
                Vector2 dir = (b - a) / len;
                // (to the left of the line as drawn, on a map with north up: (dy, -dx) in pixels)
                Vector2 side = new Vector2(dir.y, -dir.x) * f.Side;
                while (along <= len)
                {
                    Vector2 at = a + dir * along;
                    bool cold = f.Kind == Synoptic.FrontKind.Cold || (f.Kind == Synoptic.FrontKind.Occluded && (nth & 1) == 0);
                    if (cold) c.Triangle(at - dir * size, at + dir * size, at + side * size * 1.5f, colour);
                    else c.HalfDisc(at, size * 1.1f, side, colour);
                    nth++;
                    along += spacing;
                }
                along -= len;
            }
        }

        /// <summary>A wind barb: its staff from the place into the wind, a pennant for each 50 knots, a long barb for each 10, a short one for 5; on the left of the wind's way north of the equator, on the right south of it. Calm: a ring.</summary>
        static void Barb(Canvas c, float x, float y, float east, float north, bool northern, Color colour)
        {
            float kt = Mathf.Sqrt(east * east + north * north) * 1.94384f;
            if (kt < 2.5f) { c.Ring(x, y, 3.2f, colour, 1.1f); return; }
            // (pixels: east right, north up; the staff points to where it comes from)
            Vector2 to = new Vector2(east, -north).normalized, from = -to;
            float staff = 15f;
            Vector2 tip = new Vector2(x, y) + from * staff;
            c.Line(x, y, tip.x, tip.y, colour, 1.2f);
            c.Disc(x, y, 1.4f, colour);
            Vector2 left = new Vector2(to.y, -to.x);
            Vector2 side = northern ? left : -left;
            int fives = Mathf.RoundToInt(kt / 5f);
            int pennants = fives / 10; fives -= pennants * 10;
            int tens = fives / 2; fives -= tens * 2;
            float at = 0f;
            for (int p = 0; p < pennants; p++)
            {
                Vector2 b0 = tip - from * at, b1 = tip - from * (at + 4f);
                c.Triangle(b0, b1, b0 + side * 7f - from * 1.5f, colour);
                at += 5f;
            }
            for (int t = 0; t < tens; t++) { Vector2 b0 = tip - from * at; Vector2 e = b0 + side * 7f + from * 2.5f; c.Line(b0.x, b0.y, e.x, e.y, colour, 1.2f); at += 2.8f; }
            if (fives > 0) { if (at == 0f) at = 2.8f; Vector2 b0 = tip - from * at; Vector2 e = b0 + side * 3.6f + from * 1.3f; c.Line(b0.x, b0.y, e.x, e.y, colour, 1.2f); }
        }

        static void Trim(Synoptic.Path p, double until)
        {
            for (int i = p.Times.Count - 1; i >= 0; i--)
                if (p.Times[i] > until) { p.Times.RemoveAt(i); p.Points.RemoveAt(i); p.Strength.RemoveAt(i); }
        }

        /// <summary>A system's path: where it has been (a thin line), where it will go (dashed), a dot every three hours with how far ahead or behind, and the system now: L for a low, the cyclone's symbol and category, a squall line's middle.</summary>
        void DrawPath(Canvas c, ChartView view, Synoptic.Path p, double ut, List<Label> labels)
        {
            Color colour = p.Kind == Synoptic.SystemKind.Low ? new Color(1f, 0.55f, 0.5f, 0.9f) : p.Kind == Synoptic.SystemKind.Cyclone ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(0.9f, 0.55f, 0.95f, 0.9f);
            var past = new List<Vector2>(); var ahead = new List<Vector2>();
            Vector2 now = Vector2.zero; bool hasNow = false;
            for (int i = 0; i < p.Points.Count; i++)
            {
                if (!view.ToPixel(p.Points[i].x, p.Points[i].y, out float x, out float y)) continue;
                var v = new Vector2(x, y);
                if (p.Times[i] <= ut + 1.0) past.Add(v);
                if (p.Times[i] >= ut - 1.0) ahead.Add(v);
                if (!hasNow && p.Times[i] >= ut - 900.0) { now = v; hasNow = true; }
                double hours = (p.Times[i] - ut) / 3600.0;
                double every = Whole ? 6.0 : 3.0;
                if (Math.Abs(hours) > 0.1 && Math.Abs(hours - Math.Round(hours / every) * every) < 0.01 && x > 0 && y > 0 && x < c.W && y < c.H)
                {
                    c.Disc(x, y, 2.2f, colour);
                    labels.Add(new Label { X = x + 4f, Y = y - 7f, Text = (hours > 0 ? "+" : "") + Math.Round(hours).ToString("0") + "h", Colour = colour });
                }
            }
            // (a path that wraps the world's map from one edge to the other is cut there)
            c.Lines(Cut(past, c.W), colour, 1.3f);
            c.Lines(Cut(ahead, c.W), colour, 1.5f, 6f, 4f);
            if (!hasNow || now.x < 0 || now.y < 0 || now.x > c.W || now.y > c.H) return;
            int k = Math.Max(0, Math.Min(p.Strength.Count - 1, p.Times.FindIndex(t => t >= ut - 900.0)));
            if (p.Kind == Synoptic.SystemKind.Cyclone)
            {
                int cat = Synoptic.Category(p.Strength[k]);
                c.Ring(now.x, now.y, 6f, colour, 2.2f);
                c.Disc(now.x, now.y, 2.5f, colour);
                labels.Add(new Label { X = now.x + 8f, Y = now.y - 9f, Text = cat < 0 ? "TD" : cat == 0 ? "TS" : "H" + cat, Colour = colour, Big = true });
            }
        }

        /// <summary>Lines broken where they jump across the world's map from one edge to the other.</summary>
        static List<Vector2> Cut(List<Vector2> pts, int width)
        {
            var o = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0 && Mathf.Abs(pts[i].x - pts[i - 1].x) > 0.5f * width) break;
                o.Add(pts[i]);
            }
            return o;
        }

        /// <summary>
        /// A storm cell as a weather radar tracks it: its last hour (dots a quarter of an hour apart) and its next (a line with a tick
        /// at each quarter), worked out from how it drifts; and if it comes within its own size of the place, when.
        /// </summary>
        void Track(Canvas c, ChartView view, WeatherCell cell, List<Label> labels)
        {
            if (cell.Strength < 0.2f) return;
            if (!view.ToPixel(cell.Latitude, cell.Longitude, out float x, out float y) || x < -20 || y < -20 || x > c.W + 20 || y > c.H + 20) return;
            Color colour = cell.Thunder ? new Color(1f, 0.92f, 0.45f, 0.95f) : new Color(0.85f, 0.92f, 1f, 0.8f);
            double cosLat = Math.Max(0.05, Math.Cos(cell.Latitude * Deg));
            Vector2 At(double seconds)
            {
                double la = cell.Latitude + cell.Drift.y * seconds / W.Radius / Deg, lo = cell.Longitude + cell.Drift.x * seconds / (W.Radius * cosLat) / Deg;
                view.ToPixel(la, lo, out float px, out float py);
                return new Vector2(px, py);
            }
            for (int q = 1; q <= 4; q++) { Vector2 b = At(-900.0 * q); c.Disc(b.x, b.y, 1.1f, new Color(colour.r, colour.g, colour.b, 0.6f)); }
            Vector2 end = At(3600.0);
            c.Line(x, y, end.x, end.y, colour, 1.2f);
            Vector2 dir = (end - new Vector2(x, y)); float len = dir.magnitude;
            if (len > 2f)
            {
                dir /= len;
                Vector2 side = new Vector2(dir.y, -dir.x);
                for (int q = 1; q <= 4; q++) { Vector2 t = At(900.0 * q); c.Line(t.x - side.x * 2.5f, t.y - side.y * 2.5f, t.x + side.x * 2.5f, t.y + side.y * 2.5f, colour, 1.1f); }
            }
            c.Ring(x, y, Mathf.Max(2.5f, view.Pixels(cell.Radius) * 0.5f), colour, 1.1f);
            // (when it reaches the place, if it is coming: the nearest its path comes, within its own size)
            double best = double.MaxValue, when = 0.0;
            for (int m = 0; m <= 60; m += 2)
            {
                double s = m * 60.0;
                double la = cell.Latitude + cell.Drift.y * s / W.Radius / Deg, lo = cell.Longitude + cell.Drift.x * s / (W.Radius * cosLat) / Deg;
                double d = Model.Distance(W, PlaceLat, PlaceLon, la, lo);
                if (d < best) { best = d; when = s; }
            }
            if (best < Math.Max(cell.Radius, 3000f) && when > 60.0) labels.Add(new Label { X = x + 6f, Y = y + 2f, Text = "here in " + Math.Round(when / 60.0) + " min", Colour = colour });
        }
    }
}
