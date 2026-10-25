using System;
using System.Collections.Generic;

namespace NaturalWeather
{
    /// <summary>A storm of the middle latitudes as it stands at a moment (see Lows).</summary>
    sealed class Low
    {
        public int Id;
        public double Lat, Lon;     // its centre
        public float Pole;          // 1 north of the equator, -1 south (which way its pattern is turned, and its wind)
        public float Size;          // the radius of its head, m
        public float Now;           // how strong, 0 to 1, over its life
        public float Wound;         // how far its head has wound in about its centre, 0 young to 1 occluded
        public float Bend;          // how far its cold front bends back west, its own (0.15 to 0.45)
    }

    /// <summary>
    /// The storms of the middle latitudes (2026-10-19; the owner: "The cloud coverage over the whole world is too even and evenly
    /// spaced out, and not how weather actually looks from afar ... They should generate in the spots they do and in the clumps
    /// they do"). Seen from afar the Earth's middle latitudes are these and little else (NASA's EPIC camera, 2026-10-16; see
    /// docs/cloud-cover-plan.md): a comma of cloud curled round each low's centre, a broad shield of it ahead (the warm front), a
    /// long narrow band trailing back towards the equator and the west (the cold front), a clear wedge between the band and the
    /// head (the dry slot), low cloud in the warm sector, and behind the front, in the cold air, a field of showers in cells. Several
    /// along each storm track at once, each at its own point of its life: growing, mature, winding in, filling.
    ///
    /// Each is born at 36 to 50 degrees, carried east by the westerlies and a little towards the pole, and lives about fourteen
    /// hours (by the weather's pace). Its size goes by how fast the world turns, as real ones do (by the distance air spreads
    /// before the turning bends it): about a quarter of the Earth's on Kerbin, which turns four times as fast, a head some 150 to
    /// 210 km across, a front three times as long. Worked out from the clock alone, as the rest of the weather is.
    /// </summary>
    static class Lows
    {
        const double Deg = Math.PI / 180.0;
        const int Tracks = 4;                       // along each storm track at once, about
        const double Life = 14.0 * 3600.0;          // seconds, at the weather's own pace

        sealed class Kept { public World W; public long T; public float Often, Pace; public List<Low> List; }
        static readonly Kept[] kept = new Kept[4];
        static int keptNext;
        static readonly object locker = new object();
        static readonly List<Low> none = new List<Low>();
        /// <summary>(Development: the storms' shapes wound and pulled about by their own flow, as since 2026-10-21; off, drawn as made.)</summary>
        public static volatile bool Flow = true;
        /// <summary>No storms (for setting one against the other: Model.OldCover).</summary>
        public static List<Low> None => none;

        /// <summary>The storms a world has at a moment (kept for five seconds).</summary>
        public static List<Low> At(World w, double ut)
        {
            long t = (long)Math.Floor(ut / 5.0);
            float often = Model.Often, pace = Model.Pace;
            lock (locker)
            {
                for (int i = 0; i < kept.Length; i++)
                    if (kept[i] != null && kept[i].W == w && kept[i].T == t && kept[i].Often == often && kept[i].Pace == pace) return kept[i].List;
            }
            var k = new Kept { W = w, T = t, Often = often, Pace = pace, List = Make(w, t * 5.0) };
            lock (locker) { kept[keptNext] = k; keptNext = (keptNext + 1) % kept.Length; }
            return k.List;
        }

        /// <summary>How far air spreads on a world before its turning bends it, m: a thousand kilometres on the Earth, as one over the
        /// turning (a quarter of that on Kerbin), at most four tenths of the world's radius. The size of its weather's systems goes by it.</summary>
        public static double Reach(World w) => w.Spin > 0.0 ? Math.Min(1.0e6 * 7.292e-5 / (2.0 * Math.PI / w.Spin), 0.4 * w.Radius) : 0.4 * w.Radius;

        /// <summary>Whether a world has such storms: westerlies and water in its air (not Duna's dry air, nor Eve's and Jool's decks, nor Jool's bands).</summary>
        public static bool Has(World w)
        {
            Climate c = w.Climate;
            return c.Wester > 0.5f && c.Wet >= 0.5f && c.Water > 0f && c.Bands <= 0f && c.Deck <= 0.5f && w.Spin > 0.0;
        }

        static List<Low> Make(World w, double ut)
        {
            Climate c = w.Climate;
            if (!Has(w)) return none;
            var list = new List<Low>();
            double pace = Model.Pace <= 0.05f ? 0.05f : Model.Pace;
            double life = Life / pace;
            double reach = Reach(w);
            for (int pole = 0; pole < 2; pole++)
                for (int track = 0; track < Tracks; track++)
                {
                    uint ht = Noise.Hash(track, pole, w.Seed ^ 0x10e5c7u);
                    double every = life * (0.7 + 0.25 * Noise.Unit(Noise.Hash(ht ^ 0x1u)));
                    double phase = Noise.Unit(Noise.Hash(ht ^ 0x2u)) * every;
                    int g0 = Noise.Floor((ut - phase) / every);
                    for (int g = g0 - 2; g <= g0; g++)
                    {
                        uint h = Noise.Hash(ht ^ ((uint)g * 0x9e3779b9u));
                        // (now and then one is not born: a quiet spell; fewer quiet ones the stormier the setting)
                        if (Noise.Unit(Noise.Hash(h ^ 0x3u)) > 0.5f + 0.9f * Model.Often) continue;
                        double born = phase + g * every;
                        double myLife = life * (0.8 + 0.45 * Noise.Unit(Noise.Hash(h ^ 0x4u)));
                        double age = ut - born;
                        if (age < 0.0 || age > myLife) continue;
                        float tau = (float)(age / myLife);
                        float sign = pole == 0 ? 1f : -1f;
                        double lat0 = 36.0 + 14.0 * Noise.Unit(Noise.Hash(h ^ 0x5u));
                        double lon0 = 360.0 * Noise.Unit(Noise.Hash(h ^ 0x6u)) - 180.0;
                        // (carried east by the westerlies aloft, about twice those at the ground, and towards the pole as it deepens)
                        double lat = lat0 + 9.0 * tau;
                        float east = (float)((1.5 + 0.7 * Noise.Unit(Noise.Hash(h ^ 0x7u))) * c.Wester);
                        double lon = lon0 + east * age / (w.Radius * Math.Max(0.25, Math.Cos((lat0 + 4.5 * tau) * Deg))) / Deg;
                        lon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
                        list.Add(new Low
                        {
                            Id = (int)(h & 0x7fffffff),
                            Lat = sign * lat,
                            Lon = lon,
                            Pole = sign,
                            Size = (float)(reach * (0.6 + 0.25 * Noise.Unit(Noise.Hash(h ^ 0x8u))) * (0.7 + 0.4 * Noise.Smooth(0f, 0.45f, tau))),
                            Now = Noise.Smooth(0f, 0.18f, tau) * (1f - Noise.Smooth(0.72f, 1f, tau)) * (0.65f + 0.35f * Noise.Unit(Noise.Hash(h ^ 0x9u))),
                            Wound = Noise.Smooth(0.1f, 0.85f, tau),
                            Bend = 0.15f + 0.3f * Noise.Unit(Noise.Hash(h ^ 0xau)),
                        });
                    }
                }
            return list;
        }

        /// <summary>
        /// What one storm makes of a place, each taken as the larger of what it was and this storm's: `wet`, its cloud (as the
        /// weather's moisture, 0 to 1); `front`, its fronts' lines; `dry`, its dry slot; `cells`, the showers of the cold air behind
        /// it; `low`, how deep its low is here, for the wind round it.
        /// </summary>
        public static void Of(World w, Low s, double lat, double lon, ref float wet, ref float front, ref float dry, ref float cells, ref float low)
        {
            if (s.Now <= 0.01f) return;
            double d = Model.Distance(w, lat, lon, s.Lat, s.Lon);
            float r = (float)(d / s.Size);
            if (r > 4.5f) return;
            Model.Toward(s.Lat, s.Lon, lat, lon, out float te, out float tn);
            // (in its own frame, in heads: x east, y towards its pole)
            float x = r * te, y = r * tn * s.Pole;
            low = Math.Max(low, s.Now * (float)Math.Exp(-(r * r) / (1.5f * 1.5f)));

            // ---- the flow's work (2026-10-21). What is drawn below is the storm's pattern as made, ellipses and lines; what is seen is
            // that pattern carried for some hours by the storm's own wind: wound round its centre the way the wind turns, more near it,
            // where the air goes round fastest, and pulled about by eddies a fraction of a head across. So each place shows what the
            // pattern held where its air came from: the place turned back against the wind by the twist there, and moved back by the
            // eddies. And what is drawn is not even: its texture, laid on the pattern as made, is wound with it into bands. (Until
            // then drawn as made: from afar a ring with a ruled tail, against the commas drawn out into arms of a weather model's
            // satellite pictures; the owner, 2026-10-21, of the Kerbal Weather Project's: "their clouds are better /more realistic".)
            float texture = 1f;
            if (Flow)
            {
                float twist = (1.1f + 1.6f * s.Wound) * (float)Math.Exp(-r * 0.75f);
                float cs = (float)Math.Cos(twist), sn = (float)Math.Sin(twist);
                float xr = x * cs + y * sn, yr = -x * sn + y * cs;
                uint seed = (uint)s.Id;
                float ex = 0.9f * Noise.Perlin(xr * 0.9, yr * 0.9, 0.37, seed ^ 0x51u) + 0.3f * Noise.Perlin(xr * 2.6, yr * 2.6, 1.9, seed ^ 0x52u);
                float ey = 0.9f * Noise.Perlin(xr * 0.9, yr * 0.9, 4.1, seed ^ 0x53u) + 0.3f * Noise.Perlin(xr * 2.6, yr * 2.6, 5.3, seed ^ 0x54u);
                x = xr + 0.42f * ex; y = yr + 0.42f * ey;
                r = (float)Math.Sqrt(x * x + y * y);
                float grain = Noise.Fbm(xr * 2.2, yr * 2.2, 7.7, 3, seed ^ 0x55u);
                texture = 0.45f + 0.55f * Noise.Smooth(-0.32f, 0.3f, grain);
            }

            // ---- the head: a solid mass poleward of the centre, and a band wound round it the way the wind turns (anticlockwise
            // north of the equator), from the warm front's end east-south-east of the centre round over the pole's side and west,
            // inward as it goes, further the older the storm (as in the EPIC views: a broad comma, not a thin hook)
            float core = Ellipse(x, y, 0f, 0.32f, 0.95f, 0.78f, 0.5f) * (0.7f + 0.3f * s.Wound);
            float theta = (float)Math.Atan2(y, x);
            const float t0 = -0.35f;
            float th = theta >= t0 ? theta : theta + 2f * (float)Math.PI;
            float most = t0 + (float)Math.PI * (1.15f + 0.85f * s.Wound);
            float head = 0f;
            for (int turn = 0; turn < 2; turn++)
            {
                float phi = th + turn * 2f * (float)Math.PI;
                if (phi > most) break;
                float along = (phi - t0) / (float)Math.PI;
                float rb = 1.2f - 0.5f * along, wb = Math.Max(0.45f - 0.13f * along, 0.08f);
                float e = (r - rb) / wb;
                head = Math.Max(head, (float)Math.Exp(-e * e) * (1f - 0.125f * along));
            }

            // ---- ahead of the warm front, its shield: broad
            float shield = Ellipse(x, y, 1.2f, 0.3f, 1.1f, 0.95f, 0.55f);

            // ---- the fronts, from the point where they meet south-east of the centre: the warm one east and a little poleward, the
            // cold one trailing back towards the equator and bending west as it goes, as lines a band wide (taken as the distance to
            // the line: sampled at points along it, as until the evening of 2026-10-19, a band narrower than the points' spacing
            // was a string of beads)
            float dw = Line(x, y, 0.55f, -0.15f, 1.1f, 0f, 1.7f, 0.2f, 1.7f, 0.2f, out float aw);
            float bend = s.Bend;
            float dc = Line(x, y, 0.55f, -0.15f, 0.3f - 0.2f * bend, -0.85f, -0.05f - 0.6f * bend, -1.6f, -0.4f - 1.2f * bend, -2.35f, out float ac);
            float warm = (float)Math.Exp(-(dw / 0.24f) * (dw / 0.24f)) * (1f - 0.3f * aw);
            float wc = 0.2f + 0.1f * ac;
            float cold = (float)Math.Exp(-(dc / wc) * (dc / wc)) * (1f - 0.35f * ac);

            // ---- the warm sector (east of the cold front, south of the warm one): broken low cloud
            float sector = Ellipse(x, y, 1f, -1f, 0.95f, 0.9f, 0.6f) * (1f - cold);
            // ---- the dry slot: the wedge of sinking air behind the cold front, between it and the head, deeper as the storm grows old
            float slot = Ellipse(x, y, -0.15f, -0.55f, 0.55f, 0.5f, 0.6f) * (0.5f + 0.5f * s.Wound) * (1f - head) * (1f - cold) * (1f - 0.7f * core);
            // ---- the cold air behind the front: showers in cells, broken
            float behind = Ellipse(x, y, -1.35f, -0.75f, 1.25f, 1.4f, 0.7f) * (1f - slot) * (1f - cold);

            float firm = 0.8f + 0.2f * texture;
            float its = Math.Max(Math.Max(head * 0.95f * firm, core * 0.92f), Math.Max(shield * 0.86f * texture, Math.Max(cold * 0.9f * firm, warm * 0.9f * texture)));
            its = Math.Max(its, Math.Max(sector * 0.64f * texture, behind * 0.6f * texture));
            wet = Math.Max(wet, s.Now * its);
            front = Math.Max(front, s.Now * Math.Max(cold, 0.55f * warm));
            dry = Math.Max(dry, s.Now * slot);
            cells = Math.Max(cells, s.Now * behind);
        }

        static float Smooth(float a, float b, float x) => Noise.Smooth(a, b, x);

        /// <summary>1 inside an ellipse (its middle and half-widths), falling to nothing over `soft` of its radius at its edge.</summary>
        static float Ellipse(float x, float y, float cx, float cy, float rx, float ry, float soft)
        {
            float u = (x - cx) / rx, v = (y - cy) / ry;
            return 1f - Smooth(1f - soft, 1f, (float)Math.Sqrt(u * u + v * v));
        }

        /// <summary>The distance from a point to a line of three pieces through four points, and how far along it the nearest point is (0 to 1).</summary>
        static float Line(float x, float y, float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy, out float along)
        {
            float best = float.MaxValue; along = 0f;
            Piece(x, y, ax, ay, bx, by, 0, ref best, ref along);
            Piece(x, y, bx, by, cx, cy, 1, ref best, ref along);
            Piece(x, y, cx, cy, dx, dy, 2, ref best, ref along);
            along /= 3f;
            return best;
        }

        static void Piece(float x, float y, float ax, float ay, float bx, float by, int k, ref float best, ref float along)
        {
            float vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
            float t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, ((x - ax) * vx + (y - ay) * vy) / l2)) : 0f;
            float qx = ax + t * vx - x, qy = ay + t * vy - y, d = (float)Math.Sqrt(qx * qx + qy * qy);
            if (d < best) { best = d; along = k + t; }
        }
    }
}
