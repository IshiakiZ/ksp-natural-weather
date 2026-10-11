using System;
using System.Runtime.CompilerServices;

namespace NaturalWeather
{
    /// <summary>
    /// Smooth randomness that is the same every time it is asked the same question: the weather is worked out from
    /// the place and the clock alone, so a quickload, a forecast and every mod that asks all get the same answer.
    /// (No calls into the runtime's own maths in the inner parts: as this game runs them they cost a tenth of a
    /// microsecond each.)
    /// </summary>
    static class Noise
    {
        /// <summary>An integer scrambled (Chris Wellons' "lowbias32").</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash(int a, int b, int c, uint seed) => Hash((uint)a * 0x8da6b343U ^ Hash((uint)b * 0xd8163841U ^ Hash((uint)c * 0xcb1ab31fU ^ seed)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash(int a, int b, uint seed) => Hash((uint)a * 0x8da6b343U ^ Hash((uint)b * 0xd8163841U ^ seed));

        /// <summary>0 to 1 (not quite 1) from a scrambled number.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Unit(uint h) => (h & 0xFFFFFF) * (1f / 16777216f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Floor(double x) { int i = (int)x; return x < i ? i - 1 : i; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Grad(uint h, float x, float y, float z)
        {
            switch (h & 15)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x + z; case 5: return -x + z; case 6: return x - z; case 7: return -x - z;
                case 8: return y + z; case 9: return -y + z; case 10: return y - z; case 11: return -y - z;
                case 12: return x + y; case 13: return -x + y; case 14: return -y + z; default: return -y - z;
            }
        }

        /// <summary>Gradient noise in three dimensions (Perlin's improved noise): about -1 to 1, smooth, one bump to a unit.</summary>
        public static float Perlin(double x, double y, double z, uint seed)
        {
            int ix = Floor(x), iy = Floor(y), iz = Floor(z);
            float dx = (float)(x - ix), dy = (float)(y - iy), dz = (float)(z - iz);
            float u = Fade(dx), v = Fade(dy), w = Fade(dz);
            float n000 = Grad(Hash(ix, iy, iz, seed), dx, dy, dz);
            float n100 = Grad(Hash(ix + 1, iy, iz, seed), dx - 1f, dy, dz);
            float n010 = Grad(Hash(ix, iy + 1, iz, seed), dx, dy - 1f, dz);
            float n110 = Grad(Hash(ix + 1, iy + 1, iz, seed), dx - 1f, dy - 1f, dz);
            float n001 = Grad(Hash(ix, iy, iz + 1, seed), dx, dy, dz - 1f);
            float n101 = Grad(Hash(ix + 1, iy, iz + 1, seed), dx - 1f, dy, dz - 1f);
            float n011 = Grad(Hash(ix, iy + 1, iz + 1, seed), dx, dy - 1f, dz - 1f);
            float n111 = Grad(Hash(ix + 1, iy + 1, iz + 1, seed), dx - 1f, dy - 1f, dz - 1f);
            float x00 = n000 + u * (n100 - n000), x10 = n010 + u * (n110 - n010), x01 = n001 + u * (n101 - n001), x11 = n011 + u * (n111 - n011);
            float y0 = x00 + v * (x10 - x00), y1 = x01 + v * (x11 - x01);
            return y0 + w * (y1 - y0);
        }

        /// <summary>Several sizes of gradient noise added, each half the size and half as strong as the last (turned a little between them, so that no grid shows).</summary>
        public static float Fbm(double x, double y, double z, int octaves, uint seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Perlin(x, y, z, seed + (uint)o * 101u);
                norm += amp;
                // (turned between sizes: a rotation about a slanted axis, then doubled)
                double nx = 0.36 * x + 0.48 * y - 0.80 * z, ny = -0.80 * x + 0.60 * y, nz = 0.48 * x + 0.64 * y + 0.60 * z;
                x = nx * 2.03; y = ny * 2.03; z = nz * 2.03;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>
        /// Cells, as of foam: the distance to the nearest of a scatter of points, one in each unit box, in units of the box.
        /// `id` is which point it was. Used for the heads of cumulus and for the cells of storms.
        /// </summary>
        public static float Cells(double x, double y, double z, uint seed, out uint id, out float second)
        {
            int ix = Floor(x), iy = Floor(y), iz = Floor(z);
            float best = 9f, next = 9f; id = 0;
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    for (int c = -1; c <= 1; c++)
                    {
                        uint h = Hash(ix + a, iy + b, iz + c, seed);
                        float px = ix + a + Unit(h), py = iy + b + Unit(Hash(h ^ 0x9e3779b9U)), pz = iz + c + Unit(Hash(h ^ 0x7f4a7c15U));
                        float dx = (float)(px - x), dy = (float)(py - y), dz = (float)(pz - z);
                        float d = dx * dx + dy * dy + dz * dz;
                        if (d < best) { next = best; best = d; id = h; }
                        else if (d < next) next = d;
                    }
            second = Sqrt(next);
            return Sqrt(best);
        }

        /// <summary>A square root without the runtime's (Newton's method from a bit-level first guess: good to a few parts in a million).</summary>
        public static float Sqrt(float x)
        {
            if (x <= 0f) return 0f;
            var u = new FloatInt { f = x };
            u.i = 0x1fbd1df5 + (u.i >> 1);
            float y = u.f;
            y = 0.5f * (y + x / y);
            y = 0.5f * (y + x / y);
            return y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct FloatInt { [System.Runtime.InteropServices.FieldOffset(0)] public float f; [System.Runtime.InteropServices.FieldOffset(0)] public int i; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Smooth(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        /// <summary>A bump: 1 at the middle, falling smoothly to nothing `half` either side.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Bump(float x, float middle, float half)
        {
            float t = (x - middle) / half;
            t = 1f - t * t;
            return t <= 0f ? 0f : t * t;
        }

        // ---- a table of sines, for the gusts (thousands a second)
        const int SinSize = 4096;
        static readonly float[] sines = MakeSines();
        static float[] MakeSines()
        {
            var t = new float[SinSize + 1];
            for (int n = 0; n <= SinSize; n++) t[n] = (float)Math.Cos(n * 2.0 * Math.PI / SinSize);
            return t;
        }

        /// <summary>The cosine of an angle given in turns (1 is a whole turn), from the table, between entries by where between them it lies.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CosTurns(double turns)
        {
            long whole = (long)turns;
            if (turns < whole) whole--;
            float f = (float)(turns - whole) * SinSize;
            int i = (int)f;
            if (i >= SinSize) i = SinSize - 1;
            float t = f - i;
            return sines[i] + (sines[i + 1] - sines[i]) * t;
        }
    }
}
