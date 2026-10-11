using System;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// Gusts: turbulence as the flight simulators model it (von Kármán's spectrum, the sizes and strengths of MIL-F-8785C),
    /// made as a field frozen into the air and carried along by the wind. A few dozen waves of every size from 4 m to 2 km,
    /// in fixed directions, each as strong as the spectrum gives for eddies of the local size. Any point at any moment can
    /// be asked, so the craft, the smoke and the rain feel the same gust; a fast craft runs through the short waves quickly,
    /// a parachute rides the long ones. The pattern is renewed every minute and a half (one pattern mixed into the next),
    /// as real eddies come and go.
    /// </summary>
    static class Gusts
    {
        const int Octaves = 10, Modes = 2, PerComponent = Octaves * Modes;
        const double Renew = 90.0;
        static readonly double[] dx = new double[3 * PerComponent], dy = new double[3 * PerComponent], dz = new double[3 * PerComponent];
        static readonly float[] waves = new float[Octaves];

        static Gusts()
        {
            for (int o = 0; o < Octaves; o++) waves[o] = 4f * (1 << o);
            for (int n = 0; n < 3 * PerComponent; n++)
            {
                // a direction evenly at random over the sphere
                uint h = Noise.Hash((uint)n * 0x632be59bU + 0x1b873593U);
                double z = 2.0 * Noise.Unit(h) - 1.0, t = 2.0 * Math.PI * Noise.Unit(Noise.Hash(h ^ 0x85ebca6bU));
                double r = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                dx[n] = r * Math.Cos(t); dy[n] = r * Math.Sin(t); dz[n] = z;
            }
            for (int n = 0; n <= ShareSize; n++)
            {
                double x = Math.Pow(2.0, ShareLeast + n * ShareStep);
                shares[n] = (float)(x / Math.Pow(1.0 + x * x, 5.0 / 6.0));
            }
        }

        // How much of the gusts' strength comes in each doubling of size, for eddies of a given size: from von Kármán's spectrum
        // (k times the spectrum, per doubling of k), as a table over the logarithm of 1.339 k L.
        const float ShareLeast = -9f, ShareStep = 0.125f;
        const int ShareSize = 200;
        static readonly float[] shares = new float[ShareSize + 1];

        static float Share(float wave, float length)
        {
            float x = 1.339f * 6.2831853f * length / wave;
            float l = Log2(x);
            float i = (l - ShareLeast) / ShareStep;
            if (i <= 0f) return shares[0];
            if (i >= ShareSize) return shares[ShareSize];
            int n = (int)i;
            return shares[n] + (shares[n + 1] - shares[n]) * (i - n);
        }

        /// <summary>The logarithm to base 2, near enough (from the bits, and a curve between): good to about a hundredth.</summary>
        static float Log2(float x)
        {
            if (x <= 0f) return -30f;
            var u = new Bits { f = x };
            int e = ((u.i >> 23) & 255) - 128;
            u.i = (u.i & 0x007fffff) | 0x3f800000;           // the mantissa as 1 to 2
            float m = u.f;
            return e + (-0.34484843f * m + 2.02466578f) * m - 0.67487759f;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct Bits { [System.Runtime.InteropServices.FieldOffset(0)] public float f; [System.Runtime.InteropServices.FieldOffset(0)] public int i; }

        /// <summary>The gust at a place, height and moment: east, north, up, m/s.</summary>
        public static Vector3 At(Column col, double lat, double lon, double altitude, float aboveGround, double ut)
        {
            float su = Model.Sigmas(col, altitude, aboveGround, out float sw, out float lu, out float lw);
            if (su < 1e-3f && sw < 1e-3f) return Vector3.zero;
            const double Deg = Math.PI / 180.0;
            double la = lat * Deg, lo = lon * Deg, r = col.W.Radius + altitude;
            double sla = Math.Sin(la), cla = Math.Cos(la), slo = Math.Sin(lo), clo = Math.Cos(lo);
            double px = r * cla * clo, py = r * cla * slo, pz = r * sla;
            // (east and north here, in the same frame: the wind that carries the pattern along)
            double ex = -slo, ey = clo, nx = -sla * clo, ny = -sla * slo, nz = cla;
            double ce = col.RefE, cn = col.RefN;
            double cx = ce * ex + cn * nx, cy = ce * ey + cn * ny, cz = cn * nz;

            // each octave's strength, for this size of eddy
            Fill(su, lu, ref uA);
            Fill(sw, lw, ref wA);

            double k = ut / Renew;
            int k0 = Noise.Floor(k);
            float f = (float)(k - k0), mix = f * f * (3f - 2f * f);
            float norm = 1f / Noise.Sqrt((1f - mix) * (1f - mix) + mix * mix);
            float e = 0f, n = 0f, u = 0f;
            for (int key = 0; key < 2; key++)
            {
                int kk = k0 + key;
                float weight = key == 0 ? 1f - mix : mix;
                if (weight < 1e-4f) continue;
                double since = ut - kk * Renew;
                double qx = px - cx * since, qy = py - cy * since, qz = pz - cz * since;
                uint seed = Noise.Hash((uint)kk * 0x27d4eb2dU ^ col.W.Seed);
                float se = 0f, sn = 0f, sup = 0f;
                for (int o = 0; o < Octaves; o++)
                {
                    double inv = 1.0 / waves[o];
                    float au = uA[o], aw = wA[o];
                    for (int m = 0; m < Modes; m++)
                    {
                        int i = o * Modes + m;
                        int iE = i, iN = PerComponent + i, iU = 2 * PerComponent + i;
                        if (au > 0f)
                        {
                            se += au * Noise.CosTurns((dx[iE] * qx + dy[iE] * qy + dz[iE] * qz) * inv + Noise.Unit(Noise.Hash((uint)iE ^ seed)));
                            sn += au * Noise.CosTurns((dx[iN] * qx + dy[iN] * qy + dz[iN] * qz) * inv + Noise.Unit(Noise.Hash((uint)iN ^ seed)));
                        }
                        if (aw > 0f)
                            sup += aw * Noise.CosTurns((dx[iU] * qx + dy[iU] * qy + dz[iU] * qz) * inv + Noise.Unit(Noise.Hash((uint)iU ^ seed)));
                    }
                }
                e += se * weight; n += sn * weight; u += sup * weight;
            }
            // (the vertical gusts die away at the ground itself)
            float ground = Noise.Smooth(0f, 3f, aboveGround);
            return new Vector3(e * norm, n * norm, u * norm * ground);
        }

        // (each wave's strength by octave, along the wind and up; kept per thread, since any thread may ask)
        [ThreadStatic] static float[] uA, wA;

        /// <summary>The amplitude of each wave of each octave, for gusts of size `sigma` and eddies of size `length` (each wave a cosine: its share times two, over the number of waves).</summary>
        static void Fill(float sigma, float length, ref float[] into)
        {
            if (into == null) into = new float[Octaves];
            if (sigma <= 1e-4f) { for (int o = 0; o < Octaves; o++) into[o] = 0f; return; }
            float total = 0f;
            for (int o = 0; o < Octaves; o++) { into[o] = Share(waves[o], length); total += into[o]; }
            float scale = total > 1e-6f ? 2f / (total * Modes) : 0f;
            for (int o = 0; o < Octaves; o++) into[o] = sigma * Noise.Sqrt(into[o] * scale);
        }
    }
}
