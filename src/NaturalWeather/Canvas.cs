using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0, for the weather centre.) A picture drawn by hand into an array of pixels, on any thread (made into a texture
    /// on the game's): lines with soft edges, dashed or not, discs, rings, filled triangles. x to the right, y down, as the window's
    /// own; the rows are put the right way up for a texture when it is handed over (Rows). Words are not drawn here: the window
    /// writes them over the picture with its own labels.
    /// </summary>
    sealed class Canvas
    {
        public readonly int W, H;
        /// <summary>The pixels, row by row from the top.</summary>
        public readonly Color[] Px;

        public Canvas(int w, int h) { W = w; H = h; Px = Take(w * h); }

        // (the pixels of pictures made before and let go, kept to be drawn on again: a map was some 5 MB of arrays thrown away, nothing
        // once a minute, but the map's loop makes one a second, and the game's collector stops everything while it runs)
        static readonly List<Color[]> spareColours = new List<Color[]>();
        static readonly List<Color32[]> spareRows = new List<Color32[]>();

        static Color[] Take(int n)
        {
            lock (spareColours)
                for (int i = spareColours.Count - 1; i >= 0; i--)
                    if (spareColours[i].Length == n) { Color[] a = spareColours[i]; spareColours.RemoveAt(i); return a; }
            return new Color[n];
        }

        /// <summary>This picture's pixels given back to be drawn on again (when what was made from them has been handed over).</summary>
        public void Free() { lock (spareColours) if (spareColours.Count < 3) spareColours.Add(Px); }

        /// <summary>A texture's pixels given back once the texture has them, for the next picture's Rows.</summary>
        public static void Free(Color32[] rows) { if (rows == null) return; lock (spareRows) if (spareRows.Count < 3) spareRows.Add(rows); }

        public void Fill(Color c) { for (int i = 0; i < Px.Length; i++) Px[i] = c; }

        /// <summary>A colour laid over a pixel, so much of it (0 to 1, times its own alpha).</summary>
        public void Put(int x, int y, Color c, float much)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            float a = Mathf.Clamp01(much * c.a);
            if (a <= 0f) return;
            int i = y * W + x;
            Color o = Px[i];
            Px[i] = new Color(o.r + (c.r - o.r) * a, o.g + (c.g - o.g) * a, o.b + (c.b - o.b) * a, 1f);
        }

        /// <summary>A line from one point to another, so wide (pixels), its edges softened over a pixel.</summary>
        public void Line(float x0, float y0, float x1, float y1, Color c, float width)
        {
            float half = 0.5f * width;
            int xa = Mathf.FloorToInt(Mathf.Min(x0, x1) - half - 1f), xb = Mathf.CeilToInt(Mathf.Max(x0, x1) + half + 1f);
            int ya = Mathf.FloorToInt(Mathf.Min(y0, y1) - half - 1f), yb = Mathf.CeilToInt(Mathf.Max(y0, y1) + half + 1f);
            if (xb < 0 || yb < 0 || xa >= W || ya >= H) return;
            xa = Math.Max(xa, 0); ya = Math.Max(ya, 0); xb = Math.Min(xb, W - 1); yb = Math.Min(yb, H - 1);
            float vx = x1 - x0, vy = y1 - y0, l2 = vx * vx + vy * vy;
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                {
                    float px = x + 0.5f - x0, py = y + 0.5f - y0;
                    float t = l2 > 1e-6f ? Mathf.Clamp01((px * vx + py * vy) / l2) : 0f;
                    float dx = px - t * vx, dy = py - t * vy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float cover = Mathf.Clamp01(half + 0.5f - d);
                    if (cover > 0f) Put(x, y, c, cover);
                }
        }

        /// <summary>A line through points, dashed if a dash is given (pixels of line, then of gap).</summary>
        public void Lines(IList<Vector2> p, Color c, float width, float dash = 0f, float gap = 0f)
        {
            if (p.Count < 2) return;
            if (dash <= 0f) { for (int i = 0; i + 1 < p.Count; i++) Line(p[i].x, p[i].y, p[i + 1].x, p[i + 1].y, c, width); return; }
            float along = 0f, period = dash + gap;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                float len = (b - a).magnitude;
                float s = 0f;
                while (s < len)
                {
                    float inPeriod = (along + s) % period;
                    float left = inPeriod < dash ? dash - inPeriod : period - inPeriod;
                    float e = Mathf.Min(len, s + left);
                    if (inPeriod < dash) { Vector2 u = Vector2.Lerp(a, b, s / len), v = Vector2.Lerp(a, b, e / len); Line(u.x, u.y, v.x, v.y, c, width); }
                    s = e + 1e-3f;
                }
                along += len;
            }
        }

        /// <summary>A filled disc.</summary>
        public void Disc(float cx, float cy, float r, Color c)
        {
            int xa = Mathf.FloorToInt(cx - r - 1f), xb = Mathf.CeilToInt(cx + r + 1f), ya = Mathf.FloorToInt(cy - r - 1f), yb = Mathf.CeilToInt(cy + r + 1f);
            for (int y = Math.Max(ya, 0); y <= Math.Min(yb, H - 1); y++)
                for (int x = Math.Max(xa, 0); x <= Math.Min(xb, W - 1); x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float cover = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    if (cover > 0f) Put(x, y, c, cover);
                }
        }

        /// <summary>Half a disc: the half on one side of its middle (side: a direction, pixels).</summary>
        public void HalfDisc(Vector2 centre, float r, Vector2 side, Color c)
        {
            int xa = Mathf.FloorToInt(centre.x - r - 1f), xb = Mathf.CeilToInt(centre.x + r + 1f), ya = Mathf.FloorToInt(centre.y - r - 1f), yb = Mathf.CeilToInt(centre.y + r + 1f);
            float sl = side.magnitude;
            if (sl < 1e-6f) return;
            side /= sl;
            for (int y = Math.Max(ya, 0); y <= Math.Min(yb, H - 1); y++)
                for (int x = Math.Max(xa, 0); x <= Math.Min(xb, W - 1); x++)
                {
                    float dx = x + 0.5f - centre.x, dy = y + 0.5f - centre.y;
                    float round = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    float flat = Mathf.Clamp01(dx * side.x + dy * side.y + 0.5f);
                    float cover = Mathf.Min(round, flat);
                    if (cover > 0f) Put(x, y, c, cover);
                }
        }

        /// <summary>A ring, so wide.</summary>
        public void Ring(float cx, float cy, float r, Color c, float width)
        {
            float half = 0.5f * width;
            int xa = Mathf.FloorToInt(cx - r - half - 1f), xb = Mathf.CeilToInt(cx + r + half + 1f), ya = Mathf.FloorToInt(cy - r - half - 1f), yb = Mathf.CeilToInt(cy + r + half + 1f);
            for (int y = Math.Max(ya, 0); y <= Math.Min(yb, H - 1); y++)
                for (int x = Math.Max(xa, 0); x <= Math.Min(xb, W - 1); x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float cover = Mathf.Clamp01(half + 0.5f - Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r));
                    if (cover > 0f) Put(x, y, c, cover);
                }
        }

        /// <summary>A filled triangle.</summary>
        public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int xa = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 1, xb = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 1;
            int ya = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 1, yb = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 1;
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-4f) return;
            for (int y = Math.Max(ya, 0); y <= Math.Min(yb, H - 1); y++)
                for (int x = Math.Max(xa, 0); x <= Math.Min(xb, W - 1); x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    // (how far inside each edge, in pixels: the least of them, softened over a pixel)
                    float e0 = Edge(a, b, p, area), e1 = Edge(b, c, p, area), e2 = Edge(c, a, p, area);
                    float cover = Mathf.Clamp01(Mathf.Min(e0, Mathf.Min(e1, e2)) + 0.5f);
                    if (cover > 0f) Put(x, y, col, cover);
                }
        }

        static float Edge(Vector2 a, Vector2 b, Vector2 p, float area)
        {
            Vector2 e = b - a;
            float len = e.magnitude;
            if (len < 1e-6f) return 0f;
            float cross = e.x * (p.y - a.y) - e.y * (p.x - a.x);
            return Mathf.Sign(area) * cross / len;
        }

        /// <summary>The pixels as a texture takes them: rows from the bottom.</summary>
        public Color32[] Rows()
        {
            Color32[] o = null;
            lock (spareRows)
                for (int i = spareRows.Count - 1; i >= 0; i--)
                    if (spareRows[i].Length == W * H) { o = spareRows[i]; spareRows.RemoveAt(i); break; }
            if (o == null) o = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                int from = y * W, to = (H - 1 - y) * W;
                for (int x = 0; x < W; x++) o[to + x] = Px[from + x];
            }
            return o;
        }
    }

    /// <summary>
    /// Where a place is on a map round a middle: distances and bearings from the middle true (an azimuthal equidistant
    /// projection, north up), so many metres a pixel; or the whole world, longitude across and latitude up (equirectangular).
    /// </summary>
    sealed class ChartView
    {
        const double Deg = Math.PI / 180.0;
        public readonly double Lat0, Lon0, Radius;
        public readonly double MetresPerPixel;   // (the world's: degrees of longitude a pixel instead)
        public readonly bool Whole;
        public readonly int W, H;

        public ChartView(double lat0, double lon0, double radius, double halfWidth, int w, int h, bool whole)
        {
            Lat0 = lat0; Lon0 = lon0; Radius = radius; W = w; H = h; Whole = whole;
            MetresPerPixel = whole ? 360.0 / w : 2.0 * halfWidth / w;
        }

        public bool ToPixel(double lat, double lon, out float x, out float y)
        {
            if (Whole)
            {
                x = (float)((Synoptic.Wrap(lon - Lon0) + 180.0) / 360.0 * W);
                y = (float)((90.0 - lat) / 180.0 * H);
                return true;
            }
            double a = Lat0 * Deg, b = lat * Deg, dl = (lon - Lon0) * Deg;
            double cosc = Math.Sin(a) * Math.Sin(b) + Math.Cos(a) * Math.Cos(b) * Math.Cos(dl);
            double c = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosc)));
            double k = c < 1e-9 ? 1.0 : c / Math.Sin(c);
            double e = k * Math.Cos(b) * Math.Sin(dl), n = k * (Math.Cos(a) * Math.Sin(b) - Math.Sin(a) * Math.Cos(b) * Math.Cos(dl));
            x = (float)(0.5 * W + e * Radius / MetresPerPixel);
            y = (float)(0.5 * H - n * Radius / MetresPerPixel);
            return c < 0.5 * Math.PI;
        }

        public void ToPlace(float x, float y, out double lat, out double lon)
        {
            if (Whole)
            {
                lon = Synoptic.Wrap(Lon0 - 180.0 + (x / (double)W) * 360.0);
                lat = 90.0 - (y / (double)H) * 180.0;
                return;
            }
            double e = (x - 0.5 * W) * MetresPerPixel / Radius, n = (0.5 * H - y) * MetresPerPixel / Radius;
            double c = Math.Sqrt(e * e + n * n);
            double a = Lat0 * Deg;
            if (c < 1e-12) { lat = Lat0; lon = Lon0; return; }
            double sc = Math.Sin(c), cc = Math.Cos(c);
            lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, cc * Math.Sin(a) + n * sc * Math.Cos(a) / c))) / Deg;
            lon = Synoptic.Wrap(Lon0 + Math.Atan2(e * sc, c * Math.Cos(a) * cc - n * Math.Sin(a) * sc) / Deg);
        }

        /// <summary>How many pixels so many metres are, about the middle (the world's map: at the equator).</summary>
        public float Pixels(double metres) => Whole ? (float)(metres / (Radius * Deg) / MetresPerPixel) : (float)(metres / MetresPerPixel);
    }
}
