using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// The drops on the camera's glass, one by one: each a cap of water with a place, a size and a speed.
    ///
    /// What was read first (2026-10-13; TODO.md, that evening): Kaneda and others' drops on a glass plate (1993, 1999),
    /// taken onto graphics cards by Tatarchuk for ATI (2006), and the physics of drops pinned on a surface and driven off it
    /// by gravity and by wind (contact-angle hysteresis; wind-forced depinning; the coalescence of drops sitting on glass).
    /// What it comes to:
    ///
    /// A drop on glass is held where it is: its edge clings, and the most the glass can hold it with grows with its width
    /// (surface tension times width times the difference of the cosines of its receding and advancing angles). What pushes
    /// it grows faster: its weight with the cube of its size, the air's push with its frontal area and the square of the
    /// air's speed past the glass. So the small drops stay where they land, and a drop starts to run when it has grown
    /// big enough (on upright glass about two and a half millimetres across its foot), or when the air past the glass is
    /// fast enough (a millimetre drop at about ten metres a second). The camera's own motion through the air is that
    /// air: swing the camera fast to the left and the air streams past its glass to the right, and the drops go with it.
    ///
    /// A running drop slows in its own water: it goes at a speed where its push less the glass's hold is met by friction
    /// in the water, centimetres a second. It does not go straight: the glass is not everywhere alike (dirt, scratches,
    /// coating), and it goes where it is held least, which is where water has already been: a running drop leaves a wet
    /// track, and beads of itself along it, and the next drop that runs that way follows the track.
    ///
    /// Two drops that touch become one. A bridge of water forms between them and widens in a hundredth of a second (the
    /// time sqrt(density x size^3 / surface tension)), and the one drop rocks and settles in a few hundredths more. Drops
    /// do not draw each other from afar: they merge when they grow into each other, are blown into each other, or when
    /// one runs into another, which is how a running drop gathers the drops below it and runs on faster, and how a
    /// window in rain clears in streaks.
    ///
    /// Drawn each frame into a small picture of how high the water stands on the glass (tools/shaderpack/drops.glsl):
    /// each drop a cap, the highest where two overlap (they merge the next moment). The pass over the picture (wet.glsl)
    /// reads the slope of the water from it and bends the line of sight through it by Snell's rule.
    /// </summary>
    sealed class Drops
    {
        /// <summary>The glass: the front of a lens about this many millimetres tall (the picture's height).</summary>
        public const float Tall = 60f;
        public const int Most = 900;
        /// <summary>The angle a drop stands at on the glass, as its sine (45 degrees: a plain coated lens).</summary>
        public const float Stands = 0.7071f;

        struct Drop
        {
            public float X, Y;          // mm from the middle of the glass: x to the picture's right, y up the picture
            public float Vx, Vy;        // mm/s
            public float Volume;        // mm^3
            public float Grip;          // this drop's share of the glass's hold (the glass is not everywhere alike)
            public bool Moving;
            public float AxisX, AxisY, Squash;   // a merging's stretch: along which way, and how much is left of it
            public float Travel;        // mm run since it last left a bead
            public int Seed;            // for its outline
        }

        readonly Drop[] drops = new Drop[Most];
        int count;
        readonly System.Random random = new System.Random(20261013);

        // ---- the glass: how wet each patch of it is (a running drop wets its track, which dries), and how hard it holds
        const int GridX = 112, GridY = 64;
        readonly float[] wet = new float[GridX * GridY];
        readonly float[] hold = new float[GridX * GridY];

        // ---- drawing
        Mesh mesh;
        Vector3[] corners = new Vector3[Most * 4];
        int[] order = new int[Most * 6];
        public RenderTexture Picture;
        Material material;
        CommandBuffer draw;

        public Drops()
        {
            // (the glass's hold, patch by patch: from three-fifths to seven-fifths of the plain glass's, in blotches of
            // two or three millimetres)
            var coarse = new float[(GridX / 4 + 2) * (GridY / 4 + 2)];
            for (int i = 0; i < coarse.Length; i++) coarse[i] = (float)random.NextDouble();
            for (int y = 0; y < GridY; y++)
                for (int x = 0; x < GridX; x++)
                {
                    float fx = x / 4f, fy = y / 4f;
                    int ix = (int)fx, iy = (int)fy;
                    float tx = fx - ix, ty = fy - iy;
                    int w = GridX / 4 + 2;
                    float a = coarse[iy * w + ix], b = coarse[iy * w + ix + 1], c = coarse[(iy + 1) * w + ix], d = coarse[(iy + 1) * w + ix + 1];
                    float s = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    hold[y * GridX + x] = 0.6f + 0.8f * s * (0.75f + 0.5f * (float)random.NextDouble());
                }
            for (int q = 0; q < Most; q++)
            {
                order[q * 6] = q * 4; order[q * 6 + 1] = q * 4 + 1; order[q * 6 + 2] = q * 4 + 2;
                order[q * 6 + 3] = q * 4; order[q * 6 + 4] = q * 4 + 2; order[q * 6 + 5] = q * 4 + 3;
            }
        }

        public int Count => count;
        /// <summary>How much of the glass is under water (0 to 1), as the drops' feet add up.</summary>
        public float Covered { get; private set; }
        /// <summary>How many are running now.</summary>
        public int Running { get; private set; }

        public void Clear() { count = 0; Array.Clear(wet, 0, wet.Length); }

        /// <summary>The foot of a drop of so much water: its radius on the glass, mm (a cap standing at 45 degrees).</summary>
        public static float Foot(float volume) => Mathf.Pow(Mathf.Max(volume, 1e-6f) / 0.688f, 1f / 3f);

        int Cell(float x, float y, float wide)
        {
            int gx = Mathf.Clamp((int)((x / (wide * Tall) + 0.5f) * GridX), 0, GridX - 1);
            int gy = Mathf.Clamp((int)((y / Tall + 0.5f) * GridY), 0, GridY - 1);
            return gy * GridX + gx;
        }

        /// <summary>
        /// The drops carried on by so many seconds. `down`: the way things fall, on the glass (picture axes, x right, y up),
        /// times how much of gravity lies along the glass (m/s^2); `air`: the air streaming past the glass along it (m/s,
        /// picture axes); `into`: how fast the air comes at the glass face-on (m/s; it then spreads out from the middle);
        /// `shaken`: the camera's own acceleration along the glass (m/s^2, picture axes: the drops feel it backwards);
        /// `dense`: the air's density against Kerbin's at sea level; `arrive`: drops landing a second; `dry`: how fast they
        /// dry (0 in rain, about 1 out of it); `lambda`: how fast the number of raindrops falls off with their size (per mm
        /// of diameter: about 3 in steady rain, 2 in a downpour); `wide`: the picture's width over its height.
        /// </summary>
        public void Step(float dt, Vector2 down, Vector2 air, float into, Vector2 shaken, float dense, float arrive, float lambda, float dry, float wide)
        {
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);
            float halfX = 0.5f * wide * Tall, halfY = 0.5f * Tall;

            // ---- new drops: as many as land, each a raindrop spread into a cap (the sizes rain comes in: many small, few large)
            float owed = arrive * dt;
            int landing = (int)owed + (random.NextDouble() < owed - (int)owed ? 1 : 0);
            for (int n = 0; n < landing && count < Most; n++)
            {
                // (Marshall and Palmer: the number of drops of each size falls away exponentially with their diameter, by
                // about e to the minus 3 for each mm in steady rain: most are under a millimetre)
                double u = random.NextDouble();
                float across = Mathf.Clamp(0.25f + (float)(-Math.Log(1.0 - u * 0.985) / Mathf.Max(lambda, 1f)), 0.25f, 3.5f);
                float volume = Mathf.PI * across * across * across / 6f;
                drops[count] = new Drop
                {
                    X = (float)(random.NextDouble() * 2.0 - 1.0) * halfX, Y = (float)(random.NextDouble() * 2.0 - 1.0) * halfY,
                    Volume = volume, Grip = 0.8f + 0.4f * (float)random.NextDouble(), Seed = random.Next(128),
                };
                count++;
            }

            // ---- the forces on each, and its motion
            // (water: 1000 kg/m^3; surface tension 0.072 N/m; the cosines of a receding 35 and an advancing 55 degrees differ by 0.245)
            const float holdPerMm = 0.072f * 2f * 0.245f * 1e-3f;      // N for each mm of the foot's radius
            float airDensity = 1.2f * Mathf.Max(dense, 0f);
            int running = 0;
            float covered = 0f;
            for (int i = 0; i < count; i++)
            {
                ref Drop d = ref drops[i];
                float r = Foot(d.Volume);
                float mass = d.Volume * 1e-6f;                            // kg (a mm^3 of water is a milligram)
                // (the air past it: along the glass, and spreading out from the middle where it comes at the glass face-on;
                // the drop sits low in the air that clings to the glass, where it is about half as fast)
                float ax = air.x + into * 0.6f * d.X / halfY, ay = air.y + into * 0.6f * d.Y / halfY;
                ax *= 0.5f; ay *= 0.5f;
                float speedAir = Mathf.Sqrt(ax * ax + ay * ay);
                float frontal = 2f * r * 1e-3f * (r * 0.414f * 1e-3f) * 0.7f;  // m^2: its width times its height (a 45-degree cap stands 0.41 of its foot)
                float pushAir = 0.5f * airDensity * frontal * speedAir;  // times the air's velocity: N per (m/s)
                float fx = mass * (down.x - shaken.x) + pushAir * ax;
                float fy = mass * (down.y - shaken.y) + pushAir * ay;
                // (the glass's hold here: the plain glass's, by this patch and this drop, much less on a wet track)
                int c = Cell(d.X, d.Y, wide);
                float held = holdPerMm * r * hold[c] * d.Grip * (1f - 0.65f * wet[c]);
                float push = Mathf.Sqrt(fx * fx + fy * fy);
                // (a drop that is running is held less than one at rest: it goes on until its push is under about half what started it)
                float holds = d.Moving ? 0.55f * held : held;
                if (push <= holds)
                {
                    d.Moving = false;
                    d.Vx = d.Vy = 0f;
                }
                else
                {
                    d.Moving = true;
                    running++;
                    float over = (push - holds) / push;
                    float gx = fx * over, gy = fy * over;
                    // (it goes where the glass holds it least: the hold a little ahead to either side pulls it that way)
                    float vx = d.Vx, vy = d.Vy;
                    float sp = Mathf.Sqrt(vx * vx + vy * vy);
                    float wx = sp > 1e-3f ? vx / sp : gx / push, wy = sp > 1e-3f ? vy / sp : gy / push;
                    float px = -wy, py = wx;
                    int left = Cell(d.X + (wx + px) * r, d.Y + (wy + py) * r, wide), right = Cell(d.X + (wx - px) * r, d.Y + (wy - py) * r, wide);
                    float hl = hold[left] * (1f - 0.65f * wet[left]), hr = hold[right] * (1f - 0.65f * wet[right]);
                    float side = Mathf.Clamp((hr - hl) * 0.8f, -0.6f, 0.6f) * push;
                    gx += px * side; gy += py * side;
                    // (friction in the water: the drop's speed goes towards what its push and the friction balance at; the
                    // friction grows with its width; a semi-implicit step, steady at any frame time)
                    float drag = 0.25f * r * 1e-3f;                        // N per (m/s)
                    float vxm = d.Vx * 1e-3f, vym = d.Vy * 1e-3f;
                    vxm = (vxm + gx / mass * dt) / (1f + drag / mass * dt);
                    vym = (vym + gy / mass * dt) / (1f + drag / mass * dt);
                    float v = Mathf.Sqrt(vxm * vxm + vym * vym);
                    if (v > 1.5f) { vxm *= 1.5f / v; vym *= 1.5f / v; v = 1.5f; }
                    d.Vx = vxm * 1e3f; d.Vy = vym * 1e3f;
                    float stepX = d.Vx * dt, stepY = d.Vy * dt, moved = Mathf.Sqrt(stepX * stepX + stepY * stepY);
                    // (the track it leaves: wet, and every so often a bead of itself, more of them the faster it goes)
                    int steps = Mathf.Clamp((int)(moved / 0.8f) + 1, 1, 24);
                    for (int s = 1; s <= steps; s++)
                    {
                        int k = Cell(d.X + stepX * s / steps, d.Y + stepY * s / steps, wide);
                        wet[k] = 1f;
                    }
                    d.X += stepX; d.Y += stepY;
                    d.Travel += moved;
                    float every = Mathf.Max(0.6f, 2.6f * r / (1f + v * 4f));
                    if (d.Travel > every && d.Volume > 0.15f && count < Most && random.NextDouble() < 0.7)
                    {
                        d.Travel = 0f;
                        float share = Mathf.Lerp(0.008f, 0.03f, Mathf.Clamp01(v * 3f));
                        float left2 = d.Volume * share;
                        d.Volume -= left2;
                        // (left at its tail, a little way behind it)
                        float spNow = Mathf.Max(Mathf.Sqrt(d.Vx * d.Vx + d.Vy * d.Vy), 1e-3f), back = r * 0.8f;
                        drops[count] = new Drop
                        {
                            X = d.X - d.Vx / spNow * back, Y = d.Y - d.Vy / spNow * back,
                            Volume = left2, Grip = 1.3f, Seed = random.Next(128),
                        };
                        count++;
                    }
                }
                // (drying, from its edge: slow in rain, a minute or two for a millimetre drop out of it)
                d.Volume -= (0.0006f + 0.006f * dry) * r * dt;
                // (a merging's stretch dies away in a few hundredths of a second)
                d.Squash *= Mathf.Exp(-dt / 0.035f);
                covered += Mathf.PI * r * r;
            }
            Covered = Mathf.Clamp01(covered / (4f * halfX * halfY));

            // ---- what has dried or gone off the glass
            for (int i = 0; i < count; i++)
            {
                ref Drop d = ref drops[i];
                float r = Foot(d.Volume);
                if (d.Volume < 0.004f || Mathf.Abs(d.X) > halfX + r || Mathf.Abs(d.Y) > halfY + r) { drops[i] = drops[count - 1]; count--; i--; }
            }

            // ---- drops that touch become one
            Merge(wide);

            running = 0;
            for (int i = 0; i < count; i++) if (drops[i].Moving) running++;
            Running = running;

            // ---- the tracks dry (a minute or so)
            float keep = Mathf.Exp(-dt / (40f + 200f * (1f - dry)));
            for (int k = 0; k < wet.Length; k++) wet[k] *= keep;
        }

        // (a grid of the drops by place, to find the ones that touch without trying every pair)
        const float Bucket = 4f;
        int[] heads = new int[1], next = new int[Most];
        readonly bool[] gone = new bool[Most];
        int bucketsX, bucketsY;

        void Merge(float wide)
        {
            float halfX = 0.5f * wide * Tall, halfY = 0.5f * Tall;
            bucketsX = Mathf.CeilToInt(2f * halfX / Bucket) + 2; bucketsY = Mathf.CeilToInt(2f * halfY / Bucket) + 2;
            if (heads.Length < bucketsX * bucketsY) heads = new int[bucketsX * bucketsY];
            for (int k = 0; k < bucketsX * bucketsY; k++) heads[k] = -1;
            for (int i = 0; i < count; i++)
            {
                int k = BucketOf(drops[i].X, drops[i].Y, halfX, halfY);
                next[i] = heads[k]; heads[k] = i;
            }
            Array.Clear(gone, 0, count);
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (gone[i]) continue;
                float ri = Foot(drops[i].Volume);
                int bx = Mathf.Clamp((int)((drops[i].X + halfX) / Bucket) + 1, 0, bucketsX - 1), by = Mathf.Clamp((int)((drops[i].Y + halfY) / Bucket) + 1, 0, bucketsY - 1);
                int reach = Mathf.CeilToInt((ri + 3.5f) / Bucket);
                for (int yy = Mathf.Max(0, by - reach); yy <= Mathf.Min(bucketsY - 1, by + reach); yy++)
                    for (int xx = Mathf.Max(0, bx - reach); xx <= Mathf.Min(bucketsX - 1, bx + reach); xx++)
                        for (int j = heads[yy * bucketsX + xx]; j >= 0; j = next[j])
                        {
                            if (j == i || gone[j] || gone[i]) continue;
                            float rj = Foot(drops[j].Volume);
                            float dx = drops[j].X - drops[i].X, dy = drops[j].Y - drops[i].Y;
                            float reachBoth = (ri + rj) * 0.96f;
                            if (dx * dx + dy * dy > reachBoth * reachBoth) continue;
                            // (one drop: all the water, where the water's middle was, moving as the two together did; stretched
                            // for a moment along the line between them)
                            ref Drop a = ref drops[i];
                            ref Drop b = ref drops[j];
                            float va = a.Volume, vb = b.Volume, vt = va + vb;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            a.X = (a.X * va + b.X * vb) / vt; a.Y = (a.Y * va + b.Y * vb) / vt;
                            a.Vx = (a.Vx * va + b.Vx * vb) / vt; a.Vy = (a.Vy * va + b.Vy * vb) / vt;
                            a.Moving = a.Moving || b.Moving;
                            a.Grip = (a.Grip * va + b.Grip * vb) / vt;
                            a.Volume = vt;
                            if (dist > 1e-4f) { a.AxisX = dx / dist; a.AxisY = dy / dist; a.Squash = Mathf.Min(0.6f, dist / Mathf.Max(Foot(vt), 0.05f) * 0.45f); }
                            if (vb > va) a.Seed = b.Seed;
                            gone[j] = true; any = true;
                            ri = Foot(vt);
                        }
            }
            if (!any) return;
            int w = 0;
            for (int i = 0; i < count; i++) if (!gone[i]) drops[w++] = drops[i];
            count = w;
        }

        int BucketOf(float x, float y, float halfX, float halfY)
        {
            int bx = Mathf.Clamp((int)((x + halfX) / Bucket) + 1, 0, bucketsX - 1), by = Mathf.Clamp((int)((y + halfY) / Bucket) + 1, 0, bucketsY - 1);
            return by * bucketsX + bx;
        }

        /// <summary>
        /// The picture of how high the water stands on the glass, drawn for this frame: each drop a quad on the glass
        /// whose third number carries which corner it is, the drop's foot (mm), and a seed for its outline (drops.glsl).
        /// A running drop is drawn drawn out along its way (a rounded head and its own smear over the time the shutter is
        /// open: a fast one is a streak); a drop that has just merged, along the line it merged on.
        /// </summary>
        public void Draw(CommandBuffer c, Material drawn, float wide, int high, float shutter)
        {
            int across = Mathf.Max(16, Mathf.RoundToInt(high * wide)), up = Mathf.Max(16, high);
            if (Picture == null || Picture.width != across || Picture.height != up)
            {
                if (Picture != null) { Picture.Release(); UnityEngine.Object.Destroy(Picture); }
                Picture = new RenderTexture(across, up, 16, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear) { name = "Natural Weather: water on the glass", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Picture.Create();
            }
            if (mesh == null) { mesh = new Mesh { name = "Natural Weather: drops on the glass" }; mesh.MarkDynamic(); }
            float halfY = 0.5f * Tall;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                Drop d = drops[i];
                float r = Foot(d.Volume) * 1.04f;
                float sp = Mathf.Sqrt(d.Vx * d.Vx + d.Vy * d.Vy);
                float ux, uy, a, b, cx = d.X, cy = d.Y;
                if (d.Moving && sp > 0.5f)
                {
                    ux = d.Vx / sp; uy = d.Vy / sp;
                    // (drawn out: a running drop is longer than wide, and smeared over the shutter's time)
                    float smear = sp * shutter;
                    a = r * (1f + Mathf.Min(sp / 60f, 0.8f)) + 0.5f * smear;
                    b = r * r / (r * (1f + Mathf.Min(sp / 60f, 0.8f)));
                    cx -= ux * 0.5f * smear; cy -= uy * 0.5f * smear;
                }
                else if (d.Squash > 0.02f)
                {
                    ux = d.AxisX; uy = d.AxisY;
                    a = r * (1f + d.Squash); b = r / (1f + d.Squash);
                }
                else { ux = 1f; uy = 0f; a = b = r; }
                float vx = -uy, vy = ux;
                // (into the picture's own units, the height being 2)
                float k = 1f / halfY;
                int code = Mathf.Clamp(Mathf.RoundToInt(Foot(d.Volume) / 0.02f), 1, 1023) * 128 + (d.Seed & 127);
                for (int corner = 0; corner < 4; corner++)
                {
                    float su = corner == 1 || corner == 2 ? 1f : -1f, sv = corner >= 2 ? 1f : -1f;
                    float px = cx + ux * su * a + vx * sv * b, py = cy + uy * su * a + vy * sv * b;
                    corners[n * 4 + corner] = new Vector3(px * k / wide, py * k, corner + 4f * code);
                }
                n++;
            }
            for (int q = n * 4; q < Math.Min(corners.Length, (n + 1) * 4); q++) corners[q] = Vector3.zero;
            mesh.Clear(false);
            mesh.vertices = corners;
            mesh.SetIndices(order, 0, n * 6, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
            c.SetRenderTarget(Picture);
            c.ClearRenderTarget(true, true, Color.clear, 1f);
            if (n > 0) c.DrawMesh(mesh, Matrix4x4.identity, drawn, 0, 0);
        }

        public void Dispose()
        {
            if (Picture != null) { Picture.Release(); UnityEngine.Object.Destroy(Picture); Picture = null; }
            if (mesh != null) { UnityEngine.Object.Destroy(mesh); mesh = null; }
        }

#if DEV
        public string Says() => count + " drops, " + Running + " running, " + Mathf.RoundToInt(100f * Covered) + "% of the glass under water";
#endif
    }
}
