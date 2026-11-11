using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// Water thrown at the camera's glass (queue item 20, the rest of its fifth gap, research/lens-drops/understand.md section
    /// 3; 2026-11-07). Three things throw it, near the camera:
    ///
    /// Wheels rolling on wet ground. A tyre meets as much water a second as the film on the ground is deep, times the tyre's
    /// width, times its speed; about half of it goes up into the air, the rest pressed aside along the ground. Off the tread
    /// it goes as a sheet that tears into drops, the finer the faster the tyre turns: from under a tenth of a millimetre to
    /// several, some 0.8 mm on the average at a tread's Weber number of 2700 (about 10 m/s for a groove a few millimetres
    /// wide), and the mean going as the Weber number to the minus one half, so as one over the speed (Radovich and Browand,
    /// APS DFD 2010). It leaves the tyre up and back at the speed the tread turns, so over the ground nearly straight up;
    /// and the tyre's bow wave throws larger drops out to either side, low. Drops over a millimetre fly their own way and are
    /// down in a second or two (road engineers' "splash"); those under half a millimetre stop in the air within centimetres
    /// and hang in the craft's wake, a cloud of spray left along its track ("spray": Flintsch and others, FHWA-HRT-15-062).
    ///
    /// A part coming down into the water. Its splash is sized by its energy as Natural Ocean draws it: a crater whose radius
    /// is the fourth root of the energy over 4 rho g, a crown round its rim one and a half radii high. A fifth of the
    /// crater's water is taken to go up in the crown as drops of a millimetre to a centimetre, thrown up and out at fifty to
    /// seventy-five degrees and falling back round it within two or three radii (leaning downrange from a slanting blow); a
    /// fast blow throws a plume of finer spray higher, at up to a fifth of its own speed. Several parts of one craft coming
    /// in within a moment and thirty metres are one blow.
    ///
    /// A hull going fast through the water (a boat under way, a seaplane on its floats): a sheet off its sides from where its
    /// forward part meets the water, up and out, tearing into drops and mist (as Natural Ocean draws it; Savitsky's whisker
    /// spray and spray blister), the more the faster, nothing under three and a half metres a second.
    ///
    /// The water is kept as puffs of drops of five sizes: each a round cloud flying as its drops do (thrown; a crown or a
    /// hull's sheet going on as one sheet of water, too heavy for its thickness for the air to slow, until it tears into
    /// drops, a crown at its top; then each drop dragged towards the air's own speed by the square of its speed through it,
    /// falling, carried by the wind and at first by the air the craft drags along behind it), spreading as it goes. The
    /// larger drops fall on down onto what is under them; the finest, held up by the stirred air, lie along it in a cloud
    /// while they settle out onto it. The glass takes the drops it meets (Meet): as many as there are in a cubic metre where it
    /// is, times its area, times how fast they come at its face; fewer of the finest at a slow meeting, which go round the
    /// camera's body with the air instead (a drop of D mm meeting it at u m/s strikes as its Stokes number, 31 D^2 u, over
    /// that and a half, squared). Drops.Thrown lands them.
    /// </summary>
    sealed class Thrown
    {
        /// <summary>Water thrown at the glass (2026-11-07); false: none, as until then.</summary>
        public static bool On = true;
        /// <summary>The sizes thrown water is kept in, mm across (the drops' own, as they fly).</summary>
        public static readonly float[] Sizes = { 0.12f, 0.3f, 0.7f, 1.5f, 3.2f };
        // (where each size's band ends, mm: halfway between, as the sizes go up by factors)
        static readonly float[] Edges = { 0.19f, 0.46f, 1.02f, 2.2f };
        // (how fast each falls through still air, m/s: Gunn and Kinzer's, as Drops has it, from half a millimetre; under that
        // about as Stokes has it, 4 m/s a millimetre)
        static readonly float[] Fall = new float[5];
        const int Most = 4000;

        static Thrown()
        {
            for (int k = 0; k < Sizes.Length; k++)
                Fall[k] = Sizes[k] < 0.5f ? 4f * Sizes[k] : 9.65f - 10.3f * Mathf.Exp(-0.6f * Sizes[k]);
        }

        struct Puff
        {
            public Vector3d At;        // its middle, from the planet's middle (m): the scene's origin moves under it
            public Vector3 Moving;     // how its drops go over the ground (m/s)
            public Vector3 Carried;    // the air the craft dragged along with it where it was thrown (m/s), dying away
            public float Drops;        // how many drops are in it
            public float Wide, Grows;  // how far it is spread (one standard deviation, m), and how fast that grows (m/s)
            public float Age, Life;    // s
            public float Sheet;        // s: until then its water goes as one sheet, not yet torn into drops the air drags on
            public float Floor;        // how high what it came off stands, m over the sea's level
            public int Size;           // which of Sizes
        }
        readonly Puff[] puffs = new Puff[Most];
        int count;
        CelestialBody of;
        readonly System.Random random = new System.Random(20261107);
        readonly float[] shares = new float[5];

        /// <summary>What the glass met this frame (Meet), by size: how many drops, and how fast they came at its face on the
        /// average (m/s).</summary>
        public readonly float[] Landing = new float[5], Hit = new float[5];
        /// <summary>How many puffs are in the air.</summary>
        public int Count => count;
        // (for the development build's report)
        public float WheelsKgs, HullKgs, Meeting;
        public int Wheels, Hulls;
        public string LastBlow = "";

        // ---- the sources
        readonly Dictionary<Guid, List<ModuleWheelBase>> wheelsOf = new Dictionary<Guid, List<ModuleWheelBase>>();
        readonly Dictionary<Guid, int> wheelsCounted = new Dictionary<Guid, int>();
        readonly Dictionary<uint, double> wetAt = new Dictionary<uint, double>();
        HashSet<uint> dry = new HashSet<uint>(), dryNow = new HashSet<uint>();
        readonly List<uint> forget = new List<uint>();
        float clock;
        int scans;

        struct Blow
        {
            public Vector3d At;        // where it struck, from the planet's middle
            public Vector3 Going;      // how the parts were going, weighed by their energy (m/s)
            public float Energy;       // J
            public float Floor;        // the water's height there, m over the sea's level
            public double Born;        // when the first part struck (the game's time)
        }
        readonly List<Blow> blows = new List<Blow>();

        public void Clear() { count = 0; blows.Clear(); }

        /// <summary>
        /// The thrown water carried on by so many seconds, and what throws it now: wheels on ground with a film of water so
        /// many millimetres deep on it (`film`, where the camera is), parts coming down into the water and hulls going fast
        /// through it, on the craft within half a kilometre of the camera. `wind`: the air's motion there (m/s).
        /// </summary>
        public void Step(float dt, CelestialBody body, Vector3 camPos, Vector3 wind, float film)
        {
            if (body != of) { Clear(); of = body; wetAt.Clear(); blewAt.Clear(); dry.Clear(); wheelsOf.Clear(); wheelsCounted.Clear(); }
            if (!On || body == null) { count = 0; blows.Clear(); WheelsKgs = HullKgs = 0f; Wheels = Hulls = 0; return; }
            dt = Mathf.Min(dt, 0.1f);
            if (dt <= 0f) return;
            double now = Planetarium.GetUniversalTime();
            Scan(dt, body, camPos, film, now);
            for (int n = blows.Count - 1; n >= 0; n--)
            {
                if (now - blows[n].Born < 0.06 && now >= blows[n].Born) continue;
                Blow b = blows[n];
                blows.RemoveAt(n);
                Crown(body, b);
            }

            // ---- the puffs
            Vector3 gv = (Vector3)FlightGlobals.getGeeForceAtPosition((Vector3d)camPos);
            float g = Mathf.Max(gv.magnitude, 0.1f);
            float radius = (float)body.Radius;
            for (int i = count - 1; i >= 0; i--)
            {
                ref Puff p = ref puffs[i];
                p.Age += dt;
                // (a sheet of water goes as thrown, too heavy for its thickness for the air to slow; once torn into drops,
                // the air drags each as the square of its speed through it: at its end speed as much as its weight)
                float fall = Fall[p.Size], drag = p.Age < p.Sheet ? 0f : g / (fall * fall);
                Vector3 air = wind + p.Carried * Mathf.Exp(-p.Age);
                p.Moving += gv * dt;
                Vector3 through = p.Moving - air;
                p.Moving = air + through / (1f + drag * through.magnitude * dt);
                p.At += (Vector3d)(p.Moving * dt);
                p.Wide += p.Grows * dt;
                double far = p.At.magnitude;
                float high = (float)(far - radius);
                if (fall > 1.5f)
                {
                    // (drops of half a millimetre and more fall on down onto what is under them and are gone: what is left of
                    // the puff over it are those still coming down)
                    if (high < p.Floor - 2f * p.Wide) p.Drops = 0f;
                }
                else
                {
                    // (the fine ones the stirred air holds up: down on what it came off, the puff lies along it, and its drops
                    // settle out onto it, as many a second as fall through its lowest part)
                    float lowest = p.Floor + 0.5f * p.Wide;
                    if (high < lowest)
                    {
                        Vector3 up = (Vector3)(p.At / far);
                        p.At += (Vector3d)(up * (lowest - high));
                        float downward = Vector3.Dot(p.Moving, up);
                        if (downward < 0f) p.Moving -= up * downward;
                        p.Drops *= Mathf.Exp(-dt * fall / (1.25f * p.Wide));
                    }
                }
                if (p.Age > p.Life || p.Drops < 1f) puffs[i] = puffs[--count];
            }
        }

        void Scan(float dt, CelestialBody body, Vector3 camPos, float film, double now)
        {
            clock += dt;
            float span = clock;
            bool emit = clock >= 0.12f;
            if (emit) clock = 0f;
            WheelsKgs = HullKgs = 0f; Wheels = Hulls = 0;
            dryNow.Clear();
            Vector3 frame = (Vector3)Krakensbane.GetFrameVelocity();
            List<Vessel> loaded = FlightGlobals.VesselsLoaded;
            for (int vi = 0; vi < loaded.Count; vi++)
            {
                Vessel v = loaded[vi];
                if (v == null || v.packed || v.mainBody != body || v.parts == null) continue;
                if ((v.transform.position - camPos).sqrMagnitude > 500f * 500f) continue;
                // ---- wheels
                List<ModuleWheelBase> wheels = WheelsOf(v);
                for (int wi = 0; wi < wheels.Count; wi++)
                {
                    ModuleWheelBase w = wheels[wi];
                    if (w == null || w.part == null || !w.isGrounded) continue;
                    Rigidbody rb = w.part.rb;
                    if (rb == null) continue;
                    Vector3 moving = rb.velocity + frame;
                    float r = w.radius, spin = 0f;
                    Vector3 contact = w.part.transform.position;
                    try
                    {
                        KSPWheelController c = w.Wheel;
                        if (c != null)
                        {
                            if (c.wheelCollider != null && c.wheelCollider.radius > 0.01f) r = c.wheelCollider.radius;
                            var state = c.currentState;
                            if (state != null)
                            {
                                if (state.grounded) contact = state.hit.point;
                                spin = Mathf.Abs(state.angularVelocity) * r;
                            }
                        }
                    }
                    catch (Exception) { }
                    Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, (Vector3d)contact);
                    Vector3 level = moving - up * Vector3.Dot(moving, up);
                    float U = level.magnitude;
                    if (U < 2f) continue;
                    // (the water it meets: the ground's film, or the sea's where the wheel runs in it, as deep as the wheel)
                    float meets = film;
                    float above = Wet.AboveWater(body, (Vector3d)contact);
                    if (above < 0f) meets = Mathf.Max(meets, Mathf.Min(-above, r) * 1000f);
                    if (meets < 0.01f) continue;
                    float tyre = Mathf.Clamp(0.6f * r, 0.08f, 1.2f);
                    float kgs = 0.5f * meets * tyre * U;
                    WheelsKgs += kgs; Wheels++;
                    if (!emit) continue;
                    float tread = Mathf.Max(spin, U), kg = kgs * span;
                    float median = Mathf.Clamp(15f / tread, 0.15f, 3f);
                    Vector3 way = level / U, side = Vector3.Cross(up, way);
                    float floor = (float)(((Vector3d)contact - body.position).magnitude - body.Radius);
                    Vector3d at = (Vector3d)contact - body.position;
                    // (off the tread: up and back at the tread's speed, from behind the tyre's top; the air dragged along behind the
                    // craft carries it on a little at first)
                    Throw(at - (Vector3d)(way * (0.5f * r)) + (Vector3d)(up * (0.6f * r)), moving - way * (0.82f * tread) + up * (0.57f * tread), moving * 0.4f,
                          0.6f * kg, median, 0.9f, 0.5f * r + 0.1f, 0.25f + 0.012f * U, floor);
                    // (the bow wave: larger drops out to either side, low)
                    for (int s = -1; s <= 1; s += 2)
                        Throw(at + (Vector3d)(side * (s * 0.6f * tyre)), moving * 0.3f + side * (s * 0.5f * U) + up * (0.2f * U), moving * 0.2f,
                              0.2f * kg, 1.6f * median, 0.8f, 0.3f * r + 0.1f, 0.2f + 0.01f * U, floor, 0.05f);
                }
                // ---- parts in the water
                for (int pi = 0; pi < v.parts.Count; pi++)
                {
                    Part p = v.parts[pi];
                    if (p == null) continue;
                    uint id = p.flightID;
                    if (!p.WaterContact) { dryNow.Add(id); continue; }
                    Rigidbody rb = p.rb;
                    Vector3 moving = rb != null ? rb.velocity + frame : (Vector3)v.srf_velocity;
                    // (come down into it: dry when last looked at, and not in it a moment ago, as a crest leaves it and takes it again)
                    bool cameDown = dry.Contains(id) && !(wetAt.TryGetValue(id, out double last) && now - last < 2.0);
                    wetAt[id] = now;
                    if (cameDown && moving.magnitude > 2f) Struck(body, p, moving, now);
                    double sub = p.submergedPortion;
                    if (sub <= 0.02 || sub >= 0.98) continue;
                    Vector3 here = p.transform.position;
                    Vector3 upHere = (Vector3)FlightGlobals.getUpAxis(body, (Vector3d)here);
                    Vector3 level = moving - upHere * Vector3.Dot(moving, upHere);
                    float U = level.magnitude;
                    if (U < 3.5f) continue;
                    float wide = Mathf.Sqrt(Mathf.Max((float)p.radiativeArea, 0.2f) / 12.566f);
                    float deep = (float)sub * 2f * wide;
                    float kgsHull = 1.2f * wide * Mathf.Clamp01(deep / 0.4f) * (U - 3.5f);
                    HullKgs += kgsHull; Hulls++;
                    if (!emit) continue;
                    float aboveHere = Wet.AboveWater(body, (Vector3d)here);
                    if (float.IsInfinity(aboveHere)) continue;
                    Vector3 alongHere = level / U, across = Vector3.Cross(upHere, alongHere);
                    Vector3d onWater = (Vector3d)(here - upHere * aboveHere) - body.position;
                    float floorHere = (float)(onWater.magnitude - body.Radius);
                    float medianHull = Mathf.Clamp(1.6f * Mathf.Sqrt(10f / U), 0.3f, 2.5f);
                    for (int s = -1; s <= 1; s += 2)
                        Throw(onWater + (Vector3d)(across * (s * wide) + alongHere * (0.5f * wide)),
                              moving * 0.15f + across * (s * Mathf.Min(0.32f * U, 14f)) + upHere * Mathf.Min(0.15f * U, 6f), moving * 0.2f,
                              0.5f * kgsHull * span, medianHull, 0.8f, 0.4f * wide + 0.2f, 0.3f + 0.03f * U, floorHere, 0.1f);
                }
            }
            HashSet<uint> was = dry; dry = dryNow; dryNow = was;
            // (parts long dry, or gone, forgotten now and then)
            if (++scans % 600 == 0)
            {
                forget.Clear();
                foreach (var pair in wetAt) if (now - pair.Value > 60.0 || now < pair.Value) forget.Add(pair.Key);
                foreach (uint id in forget) wetAt.Remove(id);
                forget.Clear();
                foreach (var pair in blewAt) if (now - pair.Value > 60.0 || now < pair.Value) forget.Add(pair.Key);
                foreach (uint id in forget) blewAt.Remove(id);
                wheelsOf.Clear(); wheelsCounted.Clear();
            }
        }

        List<ModuleWheelBase> WheelsOf(Vessel v)
        {
            if (wheelsOf.TryGetValue(v.id, out List<ModuleWheelBase> list) && wheelsCounted.TryGetValue(v.id, out int n) && n == v.parts.Count) return list;
            if (list == null) { list = new List<ModuleWheelBase>(); wheelsOf[v.id] = list; }
            list.Clear();
            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                if (p == null) continue;
                ModuleWheelBase w = p.FindModuleImplementing<ModuleWheelBase>();
                if (w != null && w.wheelType != WheelType.LEG) list.Add(w);
            }
            wheelsCounted[v.id] = v.parts.Count;
            return list;
        }

        /// <summary>A part has come down into the water so fast (m/s, over the ground): its blow, with any other of the same craft's
        /// within a moment and thirty metres. From the parts seen coming down (Step) and from the game's crash on splashdown (Wet),
        /// a part only once.</summary>
        public void Struck(CelestialBody body, Part p, Vector3 moving, double now)
        {
            if (!On || body == null || p == null || body != of) return;
            uint id = p.flightID;
            if (blewAt.TryGetValue(id, out double was) && now - was < 2.0 && now >= was) return;
            blewAt[id] = now;
            float tonnes = p.mass + p.GetResourceMass();
            float energy = 0.5f * tonnes * 1000f * moving.sqrMagnitude;
            if (energy < 100f) return;
            Vector3 here = p.transform.position;
            float above = Wet.AboveWater(body, (Vector3d)here);
            if (float.IsInfinity(above)) return;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, (Vector3d)here);
            Vector3d at = (Vector3d)(here - up * above) - body.position;
            float floor = (float)(at.magnitude - body.Radius);
            for (int n = 0; n < blows.Count; n++)
            {
                Blow b = blows[n];
                if ((b.At - at).sqrMagnitude < 900.0 && now - b.Born < 0.5)
                {
                    float sum = b.Energy + energy;
                    b.At = (b.At * b.Energy + at * energy) / sum;
                    b.Going = (b.Going * b.Energy + moving * energy) / sum;
                    b.Floor = (b.Floor * b.Energy + floor * energy) / sum;
                    b.Energy = sum;
                    blows[n] = b;
                    return;
                }
            }
            if (blows.Count < 16) blows.Add(new Blow { At = at, Going = moving, Energy = energy, Floor = floor, Born = now });
        }
        readonly Dictionary<uint, double> blewAt = new Dictionary<uint, double>();

        /// <summary>(development build) a blow of so many joules on the water (or the ground) at a place, the parts going so fast.</summary>
        public void BlowAt(CelestialBody body, Vector3d world, Vector3 going, float energy)
        {
            if (body == null) return;
            if (body != of) { Clear(); of = body; }
            Vector3d at = world - body.position;
            blows.Add(new Blow { At = at, Going = going, Energy = energy, Floor = (float)(at.magnitude - body.Radius), Born = Planetarium.GetUniversalTime() - 1.0 });
        }

        void Crown(CelestialBody body, Blow b)
        {
            float g = (float)(body.GeeASL * 9.80665);
            float R = Mathf.Clamp(Mathf.Pow(b.Energy / (4f * 1000f * g), 0.25f), 0.3f, 60f);
            float crown = Mathf.Min(1.5f * R, 150f);
            float kg = 0.2f * 1000f * 2.0944f * R * R * R;
            Vector3d at = b.At.normalized * (body.Radius + b.Floor);
            Vector3 up = (Vector3)b.At.normalized;
            Vector3 a = Vector3.Cross(up, Vector3.right);
            if (a.sqrMagnitude < 0.01f) a = Vector3.Cross(up, Vector3.forward);
            a.Normalize();
            Vector3 c = Vector3.Cross(up, a);
            // (from a slanting blow most of the water goes forward and low, little back the way the craft came)
            Vector3 flat = Vector3.ProjectOnPlane(b.Going, up);
            float speed = b.Going.magnitude;
            float slantDown = speed > 1f ? Mathf.Atan2(-Vector3.Dot(b.Going, up), flat.magnitude) : 1.5708f;
            float lying = Mathf.Cos(Mathf.Clamp(slantDown, 0f, 1.5708f));
            Vector3 ahead = flat.sqrMagnitude > 1e-4f ? flat.normalized : a;
            int ways = Mathf.Clamp(12 + (int)(2f * R), 12, 32);
            float top = Mathf.Sqrt(2f * g * crown), weights = 0f;
            for (int k = 0; k < ways; k++)
            {
                float turn = (k + 0.5f) * 6.2832f / ways;
                weights += 1f + 0.8f * lying * Vector3.Dot(a * Mathf.Cos(turn) + c * Mathf.Sin(turn), ahead);
            }
            for (int k = 0; k < ways; k++)
            {
                float turn = (k + 0.5f) * 6.2832f / ways;
                Vector3 way = a * Mathf.Cos(turn) + c * Mathf.Sin(turn);
                float forward = Vector3.Dot(way, ahead);
                float share = (1f + 0.8f * lying * forward) / weights;
                float steep = (62f - 25f * lying * Mathf.Max(forward, 0f)) * Mathf.Deg2Rad;
                for (int band = 0; band < 2; band++)
                {
                    float rise = top * (band == 0 ? 0.55f : 0.95f);
                    float out_ = rise / Mathf.Tan(steep);
                    Vector3 moving = up * rise + way * out_ + ahead * (0.35f * lying * rise / Mathf.Sin(steep));
                    // (the crown rises as a sheet and tears into drops at its top)
                    Throw(at + (Vector3d)(way * (0.9f * R) + up * 0.2f), moving, Vector3.zero, 0.5f * share * kg, 2.2f, 0.9f,
                          0.25f * R + 0.2f, 0.1f * rise + 0.2f, b.Floor, rise / g);
                }
            }
            // (the plume of a fast blow: finer spray, higher, at up to a fifth of its speed)
            if (speed > 20f)
                for (int k = 0; k < 8; k++)
                {
                    float turn = (k + 0.5f) * 6.2832f / 8f;
                    Vector3 way = a * Mathf.Cos(turn) + c * Mathf.Sin(turn);
                    float plume = 0.2f * speed * (k % 2 == 0 ? 0.6f : 1f);
                    Throw(at + (Vector3d)(up * 0.5f), up * plume + way * (0.04f * speed), Vector3.zero,
                          0.03f * kg / 8f, 0.5f, 0.9f, 0.3f * R + 0.3f, 0.3f + 0.03f * speed, b.Floor, 0.5f * plume / g);
                }
            LastBlow = "crater " + R.ToString("F1") + " m, crown " + crown.ToString("F1") + " m, " + (kg / 1000f).ToString("F1") + " t of water thrown, from "
                + (b.Energy / 1e6f).ToString("F2") + " MJ at " + speed.ToString("F0") + " m/s";
        }

        /// <summary>So much water (kg) thrown from a place (from the planet's middle) at a speed over the ground, its drops' sizes
        /// spread about a median (mm, by their water) as a log-normal so wide: a puff of each size, so spread at first and
        /// growing so fast.</summary>
        void Throw(Vector3d at, Vector3 moving, Vector3 carried, float kg, float median, float spread, float wide, float grows, float floor, float sheet = 0f)
        {
            if (!(kg > 0f)) return;
            float before = 0f, lnMedian = Mathf.Log(median);
            for (int k = 0; k < 4; k++)
            {
                float upTo = Normal((Mathf.Log(Edges[k]) - lnMedian) / spread);
                shares[k] = upTo - before;
                before = upTo;
            }
            shares[4] = 1f - before;
            for (int k = 0; k < 5; k++)
            {
                float d = Sizes[k];
                float many = kg * shares[k] * 1e6f / (0.5236f * d * d * d);
                if (many < 1f) continue;
                int i = count < Most ? count++ : random.Next(Most);
                puffs[i] = new Puff
                {
                    At = at, Moving = moving, Carried = carried, Drops = many, Wide = Mathf.Max(wide, 0.05f), Grows = grows,
                    Life = sheet + (k == 0 ? 8f : k == 1 ? 6f : 5f), Sheet = sheet, Floor = floor, Size = k,
                };
            }
        }

        static float Normal(float x) => 0.5f * (1f + Erf(x * 0.70710678f));

        // (Abramowitz and Stegun 7.1.26, good to a few parts in ten million)
        static float Erf(float x)
        {
            float s = x < 0f ? -1f : 1f;
            x = Mathf.Abs(x);
            float t = 1f / (1f + 0.3275911f * x);
            float y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
            return s * y;
        }

        /// <summary>
        /// What the glass meets this frame (Landing, Hit): the glass at a place (from the planet's middle), going so fast over
        /// the ground (m/s), looking along `forward`, so large (m^2), over so many seconds.
        /// </summary>
        public void Meet(Vector3d camAt, Vector3 camMoving, Vector3 forward, float area, float dt)
        {
            for (int k = 0; k < 5; k++) { Landing[k] = 0f; Hit[k] = 0f; }
            Meeting = 0f;
            if (!On || count == 0 || dt <= 0f) return;
            float gather = Weather.Times(Weather.GlassGather);
            for (int i = 0; i < count; i++)
            {
                ref Puff p = ref puffs[i];
                Vector3 d = (Vector3)(p.At - camAt);
                float w2 = p.Wide * p.Wide, r2 = d.sqrMagnitude;
                if (r2 > 16f * w2) continue;
                // (drops in a cubic metre where the glass is: the puff's own, spread as a normal round its middle)
                float n = p.Drops * Mathf.Exp(-0.5f * r2 / w2) / (15.75f * w2 * p.Wide);
                Vector3 rel = p.Moving - camMoving;
                float into = -Vector3.Dot(rel, forward), face = Mathf.Max(into, 0f);
                // (coming at its face; and a little of what goes past, and of the cloud's own stirring, brought in by eddies)
                float u = face + 0.02f * (rel + forward * into).magnitude + 0.2f;
                float D = Sizes[p.Size];
                float st = 31f * D * D * u, caught = st / (st + 0.5f);
                float many = n * area * u * caught * caught * gather * dt;
                Landing[p.Size] += many;
                Hit[p.Size] += many * face;
            }
            for (int k = 0; k < 5; k++)
            {
                Meeting += Landing[k];
                Hit[k] = Landing[k] > 0f ? Hit[k] / Landing[k] : 0f;
            }
            Meeting /= dt;
        }

#if DEV
        /// <summary>(development build) the puff nearest the glass at a place (from the planet's middle), by its own spread.</summary>
        public string Nearest(Vector3d camAt)
        {
            int best = -1;
            float bestBy = float.MaxValue, lowest = float.MaxValue, highest = float.MinValue;
            Vector3 up = (Vector3)camAt.normalized;
            for (int i = 0; i < count; i++)
            {
                Vector3 d = (Vector3)(puffs[i].At - camAt);
                float by = d.magnitude / puffs[i].Wide, over = Vector3.Dot(d, up);
                if (by < bestBy) { bestBy = by; best = i; }
                lowest = Mathf.Min(lowest, over); highest = Mathf.Max(highest, over);
            }
            if (best < 0) return "no puffs";
            Puff p = puffs[best];
            Vector3 off = (Vector3)(p.At - camAt);
            return "the nearest puff " + off.magnitude.ToString("F1") + " m off (" + bestBy.ToString("F1") + " times its spread of " + p.Wide.ToString("F1") + " m), its middle "
                + Vector3.Dot(off, up).ToString("F1") + " m over the camera, drops of " + Sizes[p.Size] + " mm, " + p.Drops.ToString("E1") + " of them, " + p.Age.ToString("F1")
                + " s old, going " + p.Moving.magnitude.ToString("F1") + " m/s; the puffs from " + lowest.ToString("F1") + " to " + highest.ToString("F1") + " m over the camera";
        }
#endif

        /// <summary>(development build) how it stands.</summary>
        public string Says() => count + " puffs of thrown water in the air; " + (Wheels > 0 ? Wheels + " wheels throwing " + WheelsKgs.ToString("F2") + " kg a second; " : "")
            + (Hulls > 0 ? Hulls + " hulls throwing " + HullKgs.ToString("F2") + " kg a second; " : "") + "meeting the glass " + Meeting.ToString("F0") + " drops a second"
            + (LastBlow.Length > 0 ? "; the last splash: " + LastBlow : "") + (On ? "" : " (off)");
    }
}
