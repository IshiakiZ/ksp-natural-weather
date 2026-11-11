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
            public float Flat;          // 0 a cap; 1 a splat: a large fast drop spread flat where it landed (see Step)
            public float Snow;          // 0 water; 1 a snowflake lying on the glass, white and lacy, that melts into a drop on warm glass
            public float Ice;           // 0 water; 1 frozen where it sits, on glass below freezing: clear, bumpy, held fast
        }

        /// <summary>Snow and ice on the glass (2026-11-07, queue item 20; research/lens-drops/understand.md: "flakes land and stay
        /// as white tufts until they melt into drops"; "freezing rain: drops freeze where they land into clear bumpy ice that does
        /// not run"). Snowflakes land as the snow comes at the glass and lie as white tufts, melting into drops on glass above
        /// freezing; water on glass below freezing freezes where it sits and thaws again as the glass warms. False: none, as until
        /// then: snow left nothing on the glass, and freezing rain ran on it as rain.</summary>
        public static bool SnowOn = true;
        /// <summary>The glass's warmth, degrees C (Wet: it comes to the air's in a few minutes): above nought snow on it melts,
        /// below it water on it freezes.</summary>
        public float GlassC = 10f;
        static bool Solid(in Drop d) => d.Snow > 0.5f || d.Ice > 0.5f;
        /// <summary>The radius a snowflake of so much water lies over on the glass, mm: an aggregate of crystals about five
        /// millimetres across for each square root of a milligram of its water (the masses and sizes of aggregate flakes, a
        /// milligram's some 5 mm across, a tenth of one under 2).</summary>
        static float FlakeFoot(float volume) => 2.5f * Mathf.Sqrt(Mathf.Max(volume, 1e-6f));

        /// <summary>Large fast drops land as splats with a ring of specks (2026-11-05); false: every drop a cap, as until then.
        /// Off from 2026-11-05: in a night storm (lensveil streaks6, splatpair) the splats were grey crumpled clumps, read as grit
        /// on the glass, not water, beside the round caps. On again 2026-11-07, made again (drops.glsl): a thin level film with
        /// a rounded rim, clear through its middle, bending the scene at its edge (recordings/2026-11-07-pc/splat-after-d3d,
        /// splat-after-gl).</summary>
        public static bool Splats = true;

        /// <summary>The foot a drop stands on, as Foot, wider for a splat: flatter, the same water spread wider (a cap at 25 degrees
        /// is about a quarter wider than one at 45 holding as much). (At 15 degrees, half as wide again, with an edge four times as
        /// lobed, as first written, a field of them in a downpour was a field of flowers with spiked petals, and merged ones
        /// grew into great dark arcs: the films of 2026-11-05, recordings/2026-11-05-pc/veil and streaks.) A snowflake's, as it
        /// lies (FlakeFoot), going to its water's as it melts.</summary>
        static float FootOf(in Drop d) => d.Snow > 0f ? Mathf.Lerp(Foot(d.Volume) * (1f + 0.25f * d.Flat), FlakeFoot(d.Volume), d.Snow) : Foot(d.Volume) * (1f + 0.25f * d.Flat);

        readonly Drop[] drops = new Drop[Most];
        int count;
        readonly System.Random random = new System.Random(20261013);

        // ---- the glass: how wet each patch of it is (a running drop wets its track, which dries), and how hard it holds
        const int GridX = 112, GridY = 64;
        readonly float[] wet = new float[GridX * GridY];
        readonly float[] hold = new float[GridX * GridY];
        // ---- the mist on the glass (2026-11-05, queue item 20's third gap: research/lens-drops/understand.md): drops too small
        // to draw one by one, a few tenths of a millimetre and less, which together are a grey veil that lowers the picture's
        // contrast and puts haloes round its lights. How much of each patch of the glass they cover (0 to 1): built up by
        // drizzle and fine rain, more where the glass holds more, dried off slowly in rain and in half a minute out of it,
        // gathered up and cleared wherever a drop runs (a lane through it) and gone with a wipe or under water.
        readonly float[] mist = new float[GridX * GridY];
        readonly byte[] mistBytes = new byte[GridX * GridY * 4];
        Texture2D mistPicture;
        /// <summary>The mist on the glass is gathered and drawn (2026-11-05); false: none, as until then.</summary>
        public static bool MistOn = true;
        /// <summary>(development build) when 0 or more, every patch of the glass held at so much mist (by its hold), to see it.</summary>
        public static float MistHeld = -1f;
        /// <summary>The most mist on any patch of the glass now (0 to 1).</summary>
        public float MistMost { get; private set; }
        /// <summary>Condensation on the glass (Wet: the glass colder than the air's dew point), so much more of each patch
        /// misted a second; less than nothing, the mist dried off so much faster (queue item 20's seventh gap, 2026-11-05).</summary>
        public float Condense;
        // ---- water spots (queue item 20's sixth gap, 2026-11-05): where a drop dried where it sat, what was in the water stays
        // on the glass: a faint spot that scatters a little light and holds the next drops harder, so that rain gathers on
        // the old spots. Only a wipe or water over the whole glass takes them off.
        readonly float[] spots = new float[GridX * GridY];
        float spotsMost;
        /// <summary>Drops leave water spots where they dry (2026-11-05); false: none, as until then.</summary>
        public static bool SpotsOn = true;
        /// <summary>How hard the glass is jerked away from its drops now (m/s^2, the camera's acceleration backwards, Wet): the
        /// drops whose grip cannot hold them against it are thrown off (queue item 20's eighth gap, 2026-11-05).</summary>
        public float Flung;
        /// <summary>How many drops have been jerked off the glass (Flung), for the development build's count.</summary>
        internal int FlungCount;
        /// <summary>The hardest jerk back from the glass since the development build's report last read it (m/s^2).</summary>
        internal float FlungMost;
        /// <summary>Something on the glass with no drops: mist, spots, wet tracks, or water condensing (the glass's pass goes on for it).</summary>
        public bool Lingers => MistMost > 0.003f || spotsMost > 0.02f || tracksMost > 0.02f || Condense > 0f || TracksHeld >= 0f;
        /// <summary>Light streaks across the wet tracks drops leave (2026-11-05, wet.glsl); false: none, as until then.</summary>
        public static bool StreaksOn = true;
        /// <summary>(development build) when 0 or more, the glass held wet in tracks so much (by its hold), to see the streaks.</summary>
        public static float TracksHeld = -1f;
        float tracksMost;

        // ---- drawing
        Mesh mesh;
        Vector3[] corners = new Vector3[Most * 4];
        Vector3[] marks = new Vector3[Most * 4];      // (each corner's drop: how much of it is snow, how much ice, and its water's own foot over its quad's)
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
        /// <summary>Whether any snow or ice was on the glass as last drawn (Draw): wet.glsl then lights it from a soft copy of the picture (Wet).</summary>
        public bool Whitened { get; private set; }

        public void Clear() { count = 0; Array.Clear(wet, 0, wet.Length); Array.Clear(mist, 0, mist.Length); MistMost = 0f; Array.Clear(spots, 0, spots.Length); spotsMost = 0f; }

        /// <summary>
        /// The glass has just come up out of the water. (`speed`: how fast it left the surface, m/s; `wide`: the picture's
        /// width over its height.) Under water it was all water and held no drops. Coming out it carries a sheet of water up
        /// with it, the thicker the faster it is drawn out (Landau and Levich: a plate drawn out of a liquid at a speed U
        /// carries a film 0.94 times the capillary length times the capillary number to the two-thirds, the capillary number
        /// being the liquid's viscosity times U over its surface tension: for water a tenth of a millimetre at half a metre
        /// a second, a quarter at two, more than half at eight). Most of the sheet runs straight off; what is left breaks up
        /// as it thins into drops a millimetre or two across, the whole glass wet between them. From then on they sit, run,
        /// merge and dry as rain's do, and the air past the glass blows them off as it blows rain's: a camera that comes out
        /// and goes on fast is clear in a moment, one that is left where a wave fell away from it is beaded.
        /// (Asked for 2026-10-17: "if the camera has water droplets on it and then the camera goes underwater, the water
        /// droplets should go away. Then, when the camera comes back out, depending on speed and a bunch of parameters,
        /// there should be water droplets or no water droplets.")
        /// </summary>
        public void Surfaced(float speed, float wide)
        {
            count = 0;
            for (int k = 0; k < wet.Length; k++) wet[k] = 1f;
            Array.Clear(mist, 0, mist.Length);
            Array.Clear(spots, 0, spots.Length);
            float halfX = 0.5f * wide * Tall, halfY = 0.5f * Tall;
            // (water: viscosity 1e-3 Pa s, surface tension 0.072 N/m, capillary length 2.7 mm)
            float film = 0.94f * 2.7f * Mathf.Pow(1e-3f * Mathf.Clamp(speed, 0.05f, 20f) / 0.072f, 2f / 3f);    // mm
            // (what stays on the glass once the sheet has run off it: about a seventh)
            float left = 0.15f * film * (2f * halfX) * (2f * halfY);                                                // mm^3
            while (left > 0f && count < Most)
            {
                // (the drops a thinning sheet breaks into: a millimetre or so across, a few of them large)
                double u = random.NextDouble();
                float across = Mathf.Clamp(0.5f + (float)(-Math.Log(1.0 - u * 0.98) * 0.7), 0.4f, 3.2f) * Weather.Times(Weather.GlassSize);
                float volume = Mathf.PI * across * across * across / 6f;
                drops[count] = new Drop
                {
                    X = (float)(random.NextDouble() * 2.0 - 1.0) * halfX, Y = (float)(random.NextDouble() * 2.0 - 1.0) * halfY,
                    Volume = volume, Grip = 0.8f + 0.4f * (float)random.NextDouble(), Seed = random.Next(128),
                };
                count++;
                left -= volume;
            }
        }

        /// <summary>The foot of a drop of so much water: its radius on the glass, mm (a cap standing at 45 degrees).</summary>
        public static float Foot(float volume) => Mathf.Pow(Mathf.Max(volume, 1e-6f) / 0.688f, 1f / 3f);

        int Cell(float x, float y, float wide)
        {
            int gx = Mathf.Clamp((int)((x / (wide * Tall) + 0.5f) * GridX), 0, GridX - 1);
            int gy = Mathf.Clamp((int)((y / Tall + 0.5f) * GridY), 0, GridY - 1);
            return gy * GridX + gx;
        }

        /// <summary>A drop of so much water joins one already on the glass: the snow in it so much the less of it, and on ice it
        /// freezes onto it (2026-11-07).</summary>
        static void Join(ref Drop on, float volume)
        {
            float before = on.Volume;
            on.Volume = before + volume;
            on.Snow *= before / on.Volume;
            on.Ice = on.Ice > 0.5f && on.Snow <= 0.5f ? Mathf.Max(on.Ice * before / on.Volume, 0.55f) : on.Ice * before / on.Volume;
        }

        /// <summary>
        /// A drop so many millimetres across lands on the glass at a random place, coming at it so fast face-on (`hit`, m/s).
        /// (How it lands goes by its Weber number, how hard it hits against how strongly its surface holds it together: the
        /// water's density times its speed squared times its size over its surface tension, 13.9 v^2 D for v in m/s and D in
        /// mm. A small slow drop sits down as a cap; from some hundreds, as a 2-3 mm drop falling or any drop met at speed, it
        /// spreads into a thin sheet several times its width, its rim throws a crown of fine droplets round it, and it pulls
        /// back to a flat ragged splat: research/lens-drops/understand.md, section 1.) Rain's drops (Step) and water thrown at
        /// the glass (Thrown) land so alike.
        /// </summary>
        void Land(float across, float hit, float halfX, float halfY)
        {
            float volume = Mathf.PI * across * across * across / 6f;
            if (count >= Most)
            {
                // (The glass holding as many as it may, the drop lands on one already there and joins it; on ice, it freezes
                // onto it (2026-11-07). Until then rain stopped landing there, and freezing rain's glaze stopped growing with
                // under half the glass covered: recordings/2026-11-07-pc/ice-grow-d3d.)
                Join(ref drops[random.Next(count)], volume);
                return;
            }
            float weber = 13.9f * hit * hit * across;
            float flat = Splats ? Mathf.Clamp01((weber - 150f) / 450f) : 0f;
            float x0 = (float)(random.NextDouble() * 2.0 - 1.0) * halfX, y0 = (float)(random.NextDouble() * 2.0 - 1.0) * halfY;
            drops[count] = new Drop
            {
                X = x0, Y = y0,
                Volume = volume, Grip = 0.8f + 0.4f * (float)random.NextDouble(), Seed = random.Next(128), Flat = flat,
            };
            count++;
            // (the crown's specks: a ring of a few tiny drops round the splat, more the harder it hit, a fifth to a third of a
            // millimetre across, at one and a half to two and a half times its foot; they dry in a minute or two, so they
            // take places only while the glass holds under three fifths of all it may: the rest kept for the rain, which
            // otherwise found every place taken in a downpour and stopped landing, 2026-11-05)
            if (flat > 0.3f)
            {
                float rs = Foot(volume) * (1f + 0.25f * flat);
                int specks = Mathf.Min(4 + (int)(weber / 400f), 10);
                for (int k = 0; k < specks && count < Most * 3 / 5; k++)
                {
                    float turn = (float)(random.NextDouble() * 2.0 * Math.PI), out_ = rs * (1.5f + (float)random.NextDouble());
                    float sa = 0.2f + 0.15f * (float)random.NextDouble();
                    drops[count] = new Drop
                    {
                        X = x0 + Mathf.Cos(turn) * out_, Y = y0 + Mathf.Sin(turn) * out_,
                        Volume = Mathf.PI * sa * sa * sa / 6f, Grip = 1.3f, Seed = random.Next(128),
                    };
                    count++;
                }
            }
        }

        /// <summary>
        /// Water thrown at the glass this frame (Thrown, 2026-11-07: spray off a craft's wheels, a splash, a hull's sheet): so
        /// many drops of each of Thrown.Sizes, each coming at the glass so fast face-on on the average (m/s). The finest, under
        /// a quarter of a millimetre, too small to draw one by one, go to the mist on the glass, as much more of it as their
        /// feet cover; the rest land as rain's do, caps or splats by how hard they hit. Forty of a size in a frame at most land
        /// on their own places: the water of the rest joins drops already there, so a deluge of it (a camera in a splash's
        /// falling crown) makes few great drops that run, not more than the glass can be drawn with; and it wets the glass all
        /// over between them, as a wave's sheet does (Surfaced). `wide`: the picture's width over its height.
        /// </summary>
        public void Thrown(float[] landing, float[] hit, float wide)
        {
            float halfX = 0.5f * wide * Tall, halfY = 0.5f * Tall;
            float size = Weather.Times(Weather.GlassSize);
            float glassArea = 4f * halfX * halfY;
            float water = 0f;
            for (int k = 0; k < landing.Length && k < NaturalWeather.Thrown.Sizes.Length; k++)
            {
                float owed = landing[k];
                if (!(owed > 0f)) continue;
                float d = NaturalWeather.Thrown.Sizes[k] * size;
                float volume = Mathf.PI * d * d * d / 6f;
                if (d < 0.25f)
                {
                    if (!MistOn || MistHeld >= 0f) continue;
                    float foot = Foot(volume);
                    float gain = Mathf.Min(owed * Mathf.PI * foot * foot / glassArea, 1f);
                    for (int i = 0; i < mist.Length; i++) mist[i] = Mathf.Clamp01(mist[i] + gain * hold[i] * (1f - mist[i]));
                    continue;
                }
                water += owed * volume;
                int n = (int)Mathf.Min(owed, 1e6f);
                if (random.NextDouble() < owed - n) n++;
                int own = Mathf.Min(n, 40);
                for (int j = 0; j < own; j++) Land(d * Mathf.Exp(((float)random.NextDouble() - 0.5f) * 0.7f), hit[k], halfX, halfY);
                float left = (n - own) * volume;
                if (left > 0f && count > 0)
                {
                    int joins = Mathf.Min(count, 30);
                    for (int j = 0; j < joins; j++) Join(ref drops[random.Next(count)], left / joins);
                }
            }
            // (the glass wet all over between the drops: a twentieth of a millimetre of water over it at once wets all of it)
            float wetting = Mathf.Min(20f * water / glassArea, 1f);
            if (wetting > 0.001f)
                for (int i = 0; i < wet.Length; i++) wet[i] = wet[i] + wetting * (1f - wet[i]);
        }

        /// <summary>
        /// The drops carried on by so many seconds. `down`: the way things fall, on the glass (picture axes, x right, y up),
        /// times how much of gravity lies along the glass (m/s^2); `air`: the air streaming past the glass along it (m/s,
        /// picture axes); `into`: how fast the air comes at the glass face-on (m/s; it then spreads out from the middle);
        /// `shaken`: the camera's own acceleration along the glass (m/s^2, picture axes: the drops feel it backwards);
        /// `dense`: the air's density against Kerbin's at sea level; `arrive`: drops landing a second; `dry`: how fast they
        /// dry (0 in rain, about 1 out of it); `lambda`: how fast the number of raindrops falls off with their size (per mm
        /// of diameter: about 3 in steady rain, 2 in a downpour); `flakes`: snowflakes landing and staying a second, and
        /// `flakeLambda` the same for their water melted (per mm); `wide`: the picture's width over its height.
        /// </summary>
        public void Step(float dt, Vector2 down, Vector2 air, float into, Vector2 shaken, float dense, float arrive, float lambda, float flakes, float flakeLambda, float dry, float wide)
        {
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);
            FlungMost = Mathf.Max(FlungMost, Flung);
            float halfX = 0.5f * wide * Tall, halfY = 0.5f * Tall;
            float size = Weather.Times(Weather.GlassSize), dries = Weather.Times(Weather.GlassDry);   // (the page's fine details: 1 as installed)

            // ---- new drops: as many as land, each a raindrop spread into a cap (the sizes rain comes in: many small, few large)
            float owed = arrive * dt;
            int landing = (int)owed + (random.NextDouble() < owed - (int)owed ? 1 : 0);
            for (int n = 0; n < landing; n++)
            {
                // (Marshall and Palmer: the number of drops of each size falls away exponentially with their diameter, by
                // about e to the minus 3 for each mm in steady rain: most are under a millimetre)
                double u = random.NextDouble();
                float across = Mathf.Clamp(0.25f + (float)(-Math.Log(1.0 - u * 0.985) / Mathf.Max(lambda, 1f)), 0.25f, 3.5f) * size;
                // (its speed at the glass: the air coming at it face-on and about half its fall, its end speed 9.65 - 10.3
                // e^(-0.6 D) m/s after Gunn and Kinzer; the rain comes at the glass at a slant)
                float fall = Mathf.Max(9.65f - 10.3f * Mathf.Exp(-0.6f * across), 0.5f);
                Land(across, Mathf.Max(into, 0f) + 0.5f * fall, halfX, halfY);
            }

            // ---- snowflakes landing (2026-11-07): each an aggregate of crystals, its water as a drop (melted) a few tenths of a
            // millimetre to a few millimetres across, many small and few large (Sekhon and Srivastava's sizes for the rate it
            // falls at: Wet gives how fast their number falls off with size), lying on the glass as a white lacy tuft some
            // millimetres across (FlakeFoot), until it melts
            float owedFlakes = SnowOn ? flakes * dt : 0f;
            int flakesNow = (int)owedFlakes + (random.NextDouble() < owedFlakes - (int)owedFlakes ? 1 : 0);
            for (int n = 0; n < flakesNow && count < Most; n++)
            {
                double u = random.NextDouble();
                float melted = Mathf.Clamp(0.2f + (float)(-Math.Log(1.0 - u * 0.985) / Mathf.Max(flakeLambda, 0.3f)), 0.2f, 3.5f) * size;
                drops[count] = new Drop
                {
                    X = (float)(random.NextDouble() * 2.0 - 1.0) * halfX, Y = (float)(random.NextDouble() * 2.0 - 1.0) * halfY,
                    Volume = Mathf.PI * melted * melted * melted / 6f, Grip = 0.8f + 0.4f * (float)random.NextDouble(), Seed = random.Next(128), Snow = 1f,
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
                float r = FootOf(d);
                float mass = d.Volume * 1e-6f;                            // kg (a mm^3 of water is a milligram)
                // (the air past it: along the glass, and spreading out from the middle where it comes at the glass face-on;
                // the drop sits low in the air that clings to the glass, where it is about half as fast)
                float ax = air.x + into * 0.6f * d.X / halfY, ay = air.y + into * 0.6f * d.Y / halfY;
                ax *= 0.5f; ay *= 0.5f;
                float speedAir = Mathf.Sqrt(ax * ax + ay * ay);
                float r45 = Foot(d.Volume), frontal = 2f * r45 * 1e-3f * (r45 * 0.414f * 1e-3f) * 0.7f;  // m^2: its width times its height (a 45-degree cap stands 0.41 of its foot)
                float pushAir = 0.5f * airDensity * frontal * speedAir;  // times the air's velocity: N per (m/s)
                float fx = mass * (down.x - shaken.x) + pushAir * ax;
                float fy = mass * (down.y - shaken.y) + pushAir * ay;
                // (the glass's hold here: the plain glass's, by this patch and this drop, much less on a wet track)
                int c = Cell(d.X, d.Y, wide);
                float held = holdPerMm * r * hold[c] * (1f + 0.8f * spots[c]) * d.Grip * (1f - 0.65f * wet[c]);
                float push = Mathf.Sqrt(fx * fx + fy * fy);
                // (jerked off: the glass pulled away from the drop harder than its grip can hold it to the glass, taken as
                // twice and a half what holds it from sliding; it is gone, with no spot)
                if (Flung > 0f && d.Ice < 0.5f && mass * Flung > 2.5f * held) { d.Volume = -1f; FlungCount++; continue; }
                // (a drop that is running is held less than one at rest: it goes on until its push is under about half what started it;
                // a flake or ice holds fast where it lies: the snow caught in the glass's roughness, the ice frozen to it)
                float holds = Solid(d) ? float.MaxValue : d.Moving ? 0.55f * held : held;
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
                        mist[k] = 0f;
                    }
                    d.X += stepX; d.Y += stepY;
                    d.Travel += moved;
                    float every = Mathf.Max(0.6f, 2.6f * r / (1f + v * 4f));
                    // (none too small to last: one under what dries away at once was gone in the same frame, and left a spot, 2026-11-05)
                    float share = Mathf.Lerp(0.008f, 0.03f, Mathf.Clamp01(v * 3f));
                    if (d.Travel > every && d.Volume > 0.15f && d.Volume * share >= 0.004f && count < Most && random.NextDouble() < 0.7)
                    {
                        d.Travel = 0f;
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
                // (drying, from its edge: slow in rain, a minute or two for a millimetre drop out of it; snow and ice next to not at all)
                d.Volume -= (0.0006f + 0.006f * dry) * r * dt * dries * (1f - Mathf.Max(d.Snow, d.Ice));
                // (Snow melts on glass above freezing, faster the warmer the glass and the smaller the flake: a milligram's in some four
                // seconds at five degrees, half a minute at one; ice thaws as fast. Water freezes on glass below freezing: four cubic
                // millimetres in some four seconds at two degrees under, a small drop in two, so that freezing rain glazes the glass
                // where it lands.)
                if (!SnowOn) d.Snow = d.Ice = 0f;
                else if (GlassC > 0f)
                {
                    float melts = dt * GlassC / (20f * Mathf.Pow(Mathf.Max(d.Volume, 0.05f), 1f / 3f));
                    d.Snow = Mathf.Max(0f, d.Snow - melts);
                    d.Ice = Mathf.Max(0f, d.Ice - melts);
                }
                else if (GlassC < 0f && d.Snow < 0.5f)
                    d.Ice = Mathf.Min(1f, d.Ice - dt * GlassC / (8f * Mathf.Pow(Mathf.Max(d.Volume / 4f, 0.01f), 1f / 3f)));
                // (a merging's stretch dies away in a few hundredths of a second)
                d.Squash *= Mathf.Exp(-dt / 0.035f);
                // (a splat that runs gathers itself into a running drop's rounded head within a few tenths of a second)
                if (d.Moving) d.Flat *= Mathf.Exp(-dt / 0.3f);
                covered += Mathf.PI * r * r;
            }
            Covered = Mathf.Clamp01(covered / (4f * halfX * halfY));

            // ---- what has dried or gone off the glass
            for (int i = 0; i < count; i++)
            {
                ref Drop d = ref drops[i];
                float r = Foot(d.Volume);
                bool off = Mathf.Abs(d.X) > halfX + r || Mathf.Abs(d.Y) > halfY + r;
                if (d.Volume < 0.004f || off)
                {
                    // (dried where it sat: a spot; not a drop flung off (less than nothing), nor one dried while running)
                    if (!off && d.Volume >= 0f && !d.Moving && SpotsOn) { int c = Cell(d.X, d.Y, wide); spots[c] = Mathf.Min(1f, spots[c] + 0.2f); }
                    drops[i] = drops[count - 1]; count--; i--;
                }
            }

            // ---- drops that touch become one
            Merge(wide);

            running = 0;
            for (int i = 0; i < count; i++) if (drops[i].Moving) running++;
            Running = running;

            // ---- the tracks dry (a minute or so)
            float keep = Mathf.Exp(-dt * dries / (40f + 200f * (1f - dry)));
            for (int k = 0; k < wet.Length; k++) wet[k] *= keep;
            // (development build: every track held at so much, to see the streaks they make)
            if (TracksHeld >= 0f) for (int k = 0; k < wet.Length; k++) wet[k] = Mathf.Max(wet[k], TracksHeld * (0.5f + 0.5f * hold[k]));

            // ---- the mist: fine drops landing as the rain lands, the more of them the finer the rain (lambda over about 4, a
            // drizzle, is nearly all fine), so that steady rain covers a patch about a fifth in two minutes and a drizzle near
            // half; dried away in about four minutes in rain and half a minute out of it
            float most = 0f;
            if (!MistOn) Array.Clear(mist, 0, mist.Length);
            else if (MistHeld >= 0f) for (int k = 0; k < mist.Length; k++) mist[k] = Mathf.Clamp01(MistHeld * hold[k]);
            else
            {
                float gather = 0.012f * (arrive / 30f) * Mathf.Clamp((lambda - 2f) / 4f, 0.1f, 1f) * dt + Mathf.Max(Condense, 0f) * dt;
                float fades = Mathf.Exp(-dt * (dries * (0.004f + 0.03f * dry) + Mathf.Max(-Condense, 0f)));
                for (int k = 0; k < mist.Length; k++) mist[k] = Mathf.Clamp01((mist[k] + gather * hold[k] * (1f - mist[k])) * fades);
            }
            for (int k = 0; k < mist.Length; k++) most = Mathf.Max(most, mist[k]);
            MistMost = most;
            float tracks = 0f;
            for (int k = 0; k < wet.Length; k++) tracks = Mathf.Max(tracks, wet[k]);
            float spotted = 0f;
            if (!SpotsOn) Array.Clear(spots, 0, spots.Length);
            for (int k = 0; k < spots.Length; k++) spotted = Mathf.Max(spotted, spots[k]);
            spotsMost = spotted;
            tracksMost = StreaksOn ? tracks : 0f;
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
                // (snow and ice do not run together with anything: a flake lies over what is under it, ice is fast where it froze;
                // but water that meets ice freezes onto it, where the ice is, below)
                if (gone[i] || Solid(drops[i])) continue;
                float ri = FootOf(drops[i]);
                int bx = Mathf.Clamp((int)((drops[i].X + halfX) / Bucket) + 1, 0, bucketsX - 1), by = Mathf.Clamp((int)((drops[i].Y + halfY) / Bucket) + 1, 0, bucketsY - 1);
                int reach = Mathf.CeilToInt((ri + 3.5f) / Bucket);
                for (int yy = Mathf.Max(0, by - reach); yy <= Mathf.Min(bucketsY - 1, by + reach); yy++)
                    for (int xx = Mathf.Max(0, bx - reach); xx <= Mathf.Min(bucketsX - 1, bx + reach); xx++)
                        for (int j = heads[yy * bucketsX + xx]; j >= 0; j = next[j])
                        {
                            if (j == i || gone[j] || gone[i]) continue;
                            bool frozen = drops[j].Ice > 0.5f && drops[j].Snow <= 0.5f;
                            if (Solid(drops[j]) && !frozen) continue;
                            float rj = FootOf(drops[j]);
                            float dx = drops[j].X - drops[i].X, dy = drops[j].Y - drops[i].Y;
                            float reachBoth = (ri + rj) * 0.96f;
                            if (dx * dx + dy * dy > reachBoth * reachBoth) continue;
                            if (frozen)
                            {
                                // (Water meeting ice on the glass freezes onto it, where the ice is, held fast by it: freezing rain's
                                // glaze thickening, the beads growing into one another (2026-11-07). Each drop kept apart until then,
                                // in a downpour the glass came to hold as many as it can, 900, with a fifth of it covered, and took
                                // no more: recordings/2026-11-07-pc/ice-set-d3d.)
                                ref Drop ice = ref drops[j];
                                ref Drop water = ref drops[i];
                                float vi = ice.Volume, vw = water.Volume, vs = vi + vw;
                                ice.Ice = Mathf.Max((ice.Ice * vi + water.Ice * vw) / vs, 0.55f);
                                ice.Flat = (ice.Flat * vi + water.Flat * vw) / vs;
                                ice.Volume = vs;
                                gone[i] = true; any = true;
                                continue;
                            }
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
                            a.Flat = (a.Flat * va + b.Flat * vb) / vt;
                            a.Snow = (a.Snow * va + b.Snow * vb) / vt; a.Ice = (a.Ice * va + b.Ice * vb) / vt;
                            a.Volume = vt;
                            if (dist > 1e-4f) { a.AxisX = dx / dist; a.AxisY = dy / dist; a.Squash = Mathf.Min(0.6f, dist / Mathf.Max(Foot(vt), 0.05f) * 0.45f); }
                            if (vb > va) a.Seed = b.Seed;
                            gone[j] = true; any = true;
                            ri = FootOf(a);
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
            // (red how high the water stands, mm; green how white it is there: snow, or the frost in ice (2026-11-07))
            if (Picture == null || Picture.width != across || Picture.height != up || Picture.format != RenderTextureFormat.RGHalf)
            {
                if (Picture != null) { Picture.Release(); UnityEngine.Object.Destroy(Picture); }
                Picture = new RenderTexture(across, up, 16, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear) { name = "Natural Weather: water on the glass", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = true, autoGenerateMips = false };
                Picture.Create();
            }
            if (mesh == null) { mesh = new Mesh { name = "Natural Weather: drops on the glass" }; mesh.MarkDynamic(); }
            float halfY = 0.5f * Tall;
            int n = 0;
            bool white = false;
            for (int i = 0; i < count; i++)
            {
                Drop d = drops[i];
                white = white || d.Snow > 0.01f || d.Ice > 0.01f;
                // (a splat's ragged edge reaches out further than a cap's: its quad a quarter wider at the most)
                float r = FootOf(d) * 1.04f * (1f + 0.25f * d.Flat);
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
                int code = (Mathf.Clamp(Mathf.RoundToInt(FootOf(d) / 0.02f), 1, 1023) * 8 + Mathf.Clamp(Mathf.RoundToInt(d.Flat * 7f), 0, 7)) * 128 + (d.Seed & 127);
                // (a snowflake lies wider than its water would as a drop: the water's own foot, as a part of the quad's, for its melt)
                Vector3 mark = new Vector3(d.Snow, d.Ice, d.Snow > 0f ? Foot(d.Volume) * (1f + 0.25f * d.Flat) / Mathf.Max(FootOf(d), 1e-4f) : 1f);
                for (int corner = 0; corner < 4; corner++)
                {
                    float su = corner == 1 || corner == 2 ? 1f : -1f, sv = corner >= 2 ? 1f : -1f;
                    float px = cx + ux * su * a + vx * sv * b, py = cy + uy * su * a + vy * sv * b;
                    corners[n * 4 + corner] = new Vector3(px * k / wide, py * k, corner + 4f * code);
                    marks[n * 4 + corner] = mark;
                }
                n++;
            }
            for (int q = n * 4; q < Math.Min(corners.Length, (n + 1) * 4); q++) { corners[q] = Vector3.zero; marks[q] = Vector3.zero; }
            Whitened = white;
            mesh.Clear(false);
            mesh.vertices = corners;
            mesh.SetUVs(0, marks);
            mesh.SetIndices(order, 0, n * 6, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
            c.SetRenderTarget(Picture);
            c.ClearRenderTarget(true, true, Color.clear, 1f);
            if (n > 0) c.DrawMesh(mesh, Matrix4x4.identity, drawn, 0, 0);
            // (and its smaller copies: through a wide lens's opening the water is seen as one of them, as wide as the opening (wet.glsl))
            c.GenerateMips(Picture);
        }

        /// <summary>The mist on the glass and the wet tracks drops leave, as a small picture for wet.glsl (_WetMist: red the mist,
        /// green the tracks, blue the water spots; a patch a point, read smoothly between them), or none where there is next to none of either.</summary>
        public Texture MistPicture()
        {
            if (MistMost < 0.003f && tracksMost < 0.02f && spotsMost < 0.02f) return Texture2D.blackTexture;
            if (mistPicture == null) mistPicture = new Texture2D(GridX, GridY, TextureFormat.RGBA32, false, true) { name = "Natural Weather: mist, tracks and spots on the glass", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float tracksOn = StreaksOn ? 255f : 0f;
            for (int k = 0; k < mist.Length; k++)
            {
                mistBytes[4 * k] = (byte)Mathf.RoundToInt(255f * mist[k]);
                mistBytes[4 * k + 1] = (byte)Mathf.RoundToInt(tracksOn * wet[k]);
                mistBytes[4 * k + 2] = (byte)Mathf.RoundToInt(255f * spots[k]);
                mistBytes[4 * k + 3] = 255;
            }
            mistPicture.SetPixelData(mistBytes, 0);
            mistPicture.Apply(false);
            return mistPicture;
        }

        public void Dispose()
        {
            if (mistPicture != null) { UnityEngine.Object.Destroy(mistPicture); mistPicture = null; }
            if (Picture != null) { Picture.Release(); UnityEngine.Object.Destroy(Picture); Picture = null; }
            if (mesh != null) { UnityEngine.Object.Destroy(mesh); mesh = null; }
        }

#if DEV
        public string Says()
        {
            int flakes = 0, frozen = 0, splats = 0;
            for (int i = 0; i < count; i++) { if (drops[i].Snow > 0.5f) flakes++; else if (drops[i].Ice > 0.5f) frozen++; else if (drops[i].Flat > 0.5f) splats++; }
            string said = count + " drops (" + splats + " splats, " + flakes + " snowflakes, " + frozen + " frozen; the glass " + GlassC.ToString("F1") + " C), " + Running + " running, " + Mathf.RoundToInt(100f * Covered) + "% of the glass under water, mist at most " + MistMost.ToString("F2") + ", spots at most " + spotsMost.ToString("F2") + "; " + FlungCount + " flung off so far (the glass jerked back at most " + FlungMost.ToString("F1") + " m/s2 since the last report)";
            FlungMost = 0f;
            return said;
        }
#endif
    }
}
