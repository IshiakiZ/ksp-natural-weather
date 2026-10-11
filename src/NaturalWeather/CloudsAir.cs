using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// A world's air, as the clouds need it: how much sunlight comes through it to any height with the sun at any angle (a
    /// small table the shader reads: a low sun comes through red, and a cloud top still in sunlight after the ground has gone
    /// dark is lit pink, then not at all once the planet itself is in the way), how thick it is with height, and the colour of
    /// its sky.
    ///
    /// Worked out from the game's own air for that world (its pressure and temperature with height, which give how dense it is)
    /// and the three wavelengths the game draws its sky with (air scatters short waves more, by the fourth power), with Earth's
    /// air for the scale: blue is lost 4 times faster than red. Once for each world, on another thread.
    /// </summary>
    sealed class CloudAir
    {
        public const int Across = 128, Up = 48;

        public Texture2D Lut;
        public float Top;               // the top of the air (m above the radius)
        public float ScaleHeight;       // how far up the air keeps a mean thickness (m)
        public Vector3 Rayleigh;        // how much a metre of air at the bottom scatters, red green blue
        public float Haze;              // and the same for haze (all colours alike)
        public Color Sky;               // the sky's colour as light (its brightest part 1)
        public bool Ready => Lut != null;

        Color[] made;
        Task working;

        static readonly Dictionary<string, CloudAir> known = new Dictionary<string, CloudAir>();

        public static CloudAir Of(CelestialBody body)
        {
            if (known.TryGetValue(body.bodyName, out CloudAir air))
            {
                air.Finish();
                return air;
            }
            air = new CloudAir();
            known[body.bodyName] = air;
            air.Begin(body);
            return air;
        }

        void Begin(CelestialBody body)
        {
            double R = body.Radius;
            Top = (float)Math.Max(1000.0, body.atmosphereDepth);
            // How dense the air is with height, from the game's own figures (as a share of its density at the bottom).
            const int N = 512;
            var dense = new double[N + 1];
            double bottom = 0.0;
            for (int i = 0; i <= N; i++)
            {
                double h = Top * i / (double)N;
                double d = 0.0;
                try { d = body.GetDensity(body.GetPressure(h), body.GetTemperature(h)); } catch (Exception) { }
                if (double.IsNaN(d) || d < 0.0) d = 0.0;
                if (i == 0) bottom = d;
                dense[i] = d;
            }
            if (bottom <= 0.0) { for (int i = 0; i <= N; i++) dense[i] = Math.Exp(-Top * i / (double)N / 5600.0); bottom = 1.0; }
            double column = 0.0;
            for (int i = 0; i <= N; i++) { dense[i] /= bottom; column += dense[i] * Top / N; }
            ScaleHeight = (float)Math.Max(200.0, column);
            // Scattering at the bottom: Earth's air (5.8, 13.5, 33.1 per million metres at 680, 550, 440 nm) moved to the game's
            // wavelengths for this world's sky, and scaled by how dense this air is against Earth's.
            Color w = new Color(0.65f, 0.57f, 0.475f);
            try { if (body.afg != null && body.afg.waveLength.r > 0.1f && body.afg.waveLength.g > 0.1f && body.afg.waveLength.b > 0.1f) w = body.afg.waveLength; } catch (Exception) { }
            double thick = Math.Max(0.02, Math.Min(10.0, bottom / 1.225));
            Func<double, double> scatter = l => 33.1e-6 * Math.Pow(0.44 / l, 4.0) * thick;
            Rayleigh = new Vector3((float)scatter(w.r), (float)scatter(w.g), (float)scatter(w.b));
            Haze = (float)(2.2e-6 * thick);
            var s = new Vector3(1f / Mathf.Pow(w.r, 4f), 1f / Mathf.Pow(w.g, 4f), 1f / Mathf.Pow(w.b, 4f));
            float most = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            Sky = Color.Lerp(new Color(s.x / most, s.y / most, s.z / most), Color.white, 0.25f);

            Vector3 ray = Rayleigh;
            float haze = Haze, top = Top;
            working = Task.Run(() => made = Table(R, top, dense, ray, haze));
        }

        /// <summary>The table: across, the sine of the sun's height (-1 to 1); up, the height (0 to the top of the air).</summary>
        static Color[] Table(double R, double top, double[] dense, Vector3 ray, float haze)
        {
            var o = new Color[Across * Up];
            double Rt = R + top;
            int N = dense.Length - 1;
            for (int j = 0; j < Up; j++)
            {
                double h = top * j / (Up - 1.0);
                double r0 = R + h;
                for (int i = 0; i < Across; i++)
                {
                    double mu = i / (Across - 1.0) * 2.0 - 1.0;
                    // (the planet itself in the way: no sunlight at all)
                    double disc = r0 * r0 * (mu * mu - 1.0) + R * R;
                    if (mu < 0.0 && disc >= 0.0) { o[i + j * Across] = new Color(0f, 0f, 0f, 1f); continue; }
                    double toTop = -r0 * mu + Math.Sqrt(Math.Max(0.0, r0 * r0 * (mu * mu - 1.0) + Rt * Rt));
                    const int S = 40;
                    double ds = toTop / S, column = 0.0;
                    for (int k = 0; k < S; k++)
                    {
                        double s = (k + 0.5) * ds;
                        double r = Math.Sqrt(r0 * r0 + s * s + 2.0 * r0 * mu * s);
                        double at = (r - R) / top * N;
                        if (at >= N) continue;
                        if (at < 0.0) at = 0.0;
                        int a = (int)at;
                        double f = at - a;
                        column += (dense[a] * (1.0 - f) + dense[Math.Min(N, a + 1)] * f) * ds;
                    }
                    o[i + j * Across] = new Color((float)Math.Exp(-column * (ray.x + haze)), (float)Math.Exp(-column * (ray.y + haze)), (float)Math.Exp(-column * (ray.z + haze)), 1f);
                }
            }
            return o;
        }

        void Finish()
        {
            if (Lut != null || working == null || !working.IsCompleted || made == null) return;
            Lut = new Texture2D(Across, Up, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Weather: sunlight through the air" };
            Lut.SetPixels(made);
            Lut.Apply(false, false);           // (kept readable: Through reads it back)
            made = null;
        }

        /// <summary>The sunlight that comes through the air to a height with the sun at a height (its sine), worked out on the processor from the table (for the light the clouds are given from round about).</summary>
        public Color Through(float height, float sunUp)
        {
            if (Lut == null) return Color.white;
            float u = Mathf.Clamp01(sunUp * 0.5f + 0.5f), v = Mathf.Clamp01(height / Mathf.Max(Top, 1f));
            return Lut.GetPixelBilinear(u, v);
        }
    }
}
