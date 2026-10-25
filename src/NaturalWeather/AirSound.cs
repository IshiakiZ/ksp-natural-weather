using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0.) What the air does to the sound of craft passing the camera (docs/study-other-mods-2026-10-24.md: Camera Tools
    /// gives a fly-by both; ours are worked out from the physics, for every camera):
    ///
    ///   * **Doppler.** A craft's own sounds (its engines' roar, its parts') come in higher as it nears and fall away lower as it
    ///     goes, by how fast it comes or goes against the speed of sound: the pitch times c / (c - v), v its speed towards the
    ///     camera through the air. A camera that goes with the craft (the game's own, following it) hears it unchanged.
    ///   * **Sonic booms.** A craft faster than sound drags a cone of shock behind it, at the angle whose sine is one over its
    ///     Mach number; where that cone sweeps over the camera the camera hears the boom: the N-wave, a sharp rise, a fall
    ///     through the ambient to as far below it, a sharp rise back, the two shocks a "ba-boom" as long apart as the wave is long
    ///     (Whitham's far-field rule: longer for a longer craft, further off, and faster), as loud as the craft is long against
    ///     how far off it passes (the overpressure falls as the distance to the three quarters), weaker in thin air, its cracks
    ///     dulled with distance, and the ground's echo after it. A camera that goes with the craft is inside its cone the whole
    ///     time and hears none, as a pilot does.
    ///
    /// In air only: nothing in a vacuum or where the camera's world has none, nor at more than four times the game's speed.
    /// </summary>
    public sealed class AirSound : MonoBehaviour
    {
        // ---------------------------------------------------------------- where the camera is going

        Vector3 lastCam;
        bool haveCam;
        Vector3 camVelocity;            // the camera's own velocity, in the world's frame (as the craft's srf_velocity is)

        /// <summary>The camera's velocity from where it was a frame ago, with the frame the game moves the world in (Krakensbane) added back.</summary>
        bool TrackCamera(Camera cam, float dt)
        {
            Vector3 at = cam.transform.position;
            if (haveCam && dt > 1e-4f)
            {
                Vector3 moved = at - lastCam;
                // (the game moves everything at once now and then to keep the numbers small: a frame's jump of kilometres is that, not the camera)
                if (moved.sqrMagnitude < 2000f * 2000f) camVelocity = Vector3.Lerp(camVelocity, moved / dt + (Vector3)Krakensbane.GetFrameVelocity(), Mathf.Clamp01(dt * 8f));
            }
            lastCam = at;
            haveCam = true;
            return true;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (cam == null || dt <= 0f || !HighLogic.LoadedSceneIsFlight) return;
            TrackCamera(cam, dt);
            CelestialBody body = FlightGlobals.currentMainBody;
            bool air = Weather.On && (Weather.Doppler || Weather.Booms) && body != null && body.atmosphere && TimeWarp.CurrentRate <= 4.1f && Model.Allowed(body)
                       && body.GetAltitude(cam.transform.position) < body.atmosphereDepth;
            // (and not from the map, where the camera is far off in the planets' picture: the craft's sounds as they were meanwhile)
            if (!air || MapView.MapIsEnabled) { Release(); haveCam = false; return; }
            double camAlt = body.GetAltitude(cam.transform.position);
            double pressure = body.GetPressure(camAlt);
            if (pressure <= 1e-4) { Release(); return; }
            double temperature = body.GetTemperature(camAlt);
            float c = Mathf.Max(50f, (float)body.GetSpeedOfSound(pressure, body.GetDensity(pressure, temperature)));
            Vector3 camAt = cam.transform.position;
            List<Vessel> loaded = FlightGlobals.VesselsLoaded;
            seen.Clear();
            for (int n = 0; n < loaded.Count; n++)
            {
                Vessel v = loaded[n];
                if (v == null || !v.loaded || v.packed || v.parts == null) continue;
                seen.Add(v.id);
                Vector3 going = v.srf_velocity;
                Vector3 rel = going - camVelocity;
                Vector3 toCam = camAt - (Vector3)v.CoMD;
                float far = toCam.magnitude;
                if (far < 1f) continue;
                // ---- Doppler: the craft's speed towards the camera through the air, against the sound's
                float closing = Vector3.Dot(rel, toCam / far);
                float doppler = Weather.Doppler ? Mathf.Clamp(c / Mathf.Max(c - closing, 0.2f * c), 0.5f, 2f) : 1f;
                Pitch(v, doppler, dt);
#if DEV
                Of(v).Shift = "Doppler x" + doppler.ToString("F2") + " (closing " + closing.ToString("F0") + " m/s) on " + Of(v).Sources.Count + " sounds";
#endif
                // ---- booms: from craft faster than sound, the camera not going with it
                if (Weather.Booms) Boom(v, going, rel, toCam, far, c, (float)v.staticPressurekPa);
            }
            Forget();
        }

        // ---------------------------------------------------------------- Doppler

        sealed class Heard
        {
            public AudioSource Source;
            public float Base = 1f, Written = -1f, Doppler = 1f, DopplerLevel;
        }

        sealed class Craft
        {
            public readonly List<Heard> Sources = new List<Heard>();
            public float ScanAt = -100f;
            public bool Inside, Armed;
#if DEV
            public string Note = "", Shift = "";
#endif
        }

        readonly Dictionary<Guid, Craft> crafts = new Dictionary<Guid, Craft>();
        readonly HashSet<Guid> seen = new HashSet<Guid>();
        readonly List<Guid> gone = new List<Guid>();

        Craft Of(Vessel v)
        {
            if (!crafts.TryGetValue(v.id, out Craft k)) { k = new Craft(); crafts[v.id] = k; }
            return k;
        }

        /// <summary>
        /// Each sound of the craft's parts pitched by the Doppler factor, eased: the pitch the game (or another mod) gave it kept as
        /// its own, and noticed when it changes (the game sets an engine's pitch from its throttle when it likes); the source's own
        /// Doppler (Unity's, from how its transform moves, which the game's moving of the world makes nonsense of) held off meanwhile.
        /// </summary>
        void Pitch(Vessel v, float doppler, float dt)
        {
            Craft k = Of(v);
            if (Time.realtimeSinceStartup > k.ScanAt)
            {
                // (the craft's sources looked for again every few seconds: parts come and go, engines make theirs when first lit)
                k.ScanAt = Time.realtimeSinceStartup + 3f;
                var now = new HashSet<AudioSource>();
                foreach (Heard h in k.Sources) if (h.Source != null) now.Add(h.Source);
                foreach (Part p in v.parts)
                {
                    if (p == null) continue;
                    p.GetComponentsInChildren(true, scratch);
                    foreach (AudioSource s in scratch)
                        if (s != null && !now.Contains(s)) { k.Sources.Add(new Heard { Source = s, Base = s.pitch, DopplerLevel = s.dopplerLevel }); now.Add(s); }
                }
            }
            for (int i = k.Sources.Count - 1; i >= 0; i--)
            {
                Heard h = k.Sources[i];
                AudioSource s = h.Source;
                if (s == null) { k.Sources.RemoveAt(i); continue; }
                if (h.Written >= 0f && Mathf.Abs(s.pitch - h.Written) > 1e-4f) h.Base = s.pitch;
                else if (h.Written < 0f) h.Base = s.pitch;
                h.Doppler = Mathf.Lerp(h.Doppler, doppler, Mathf.Clamp01(dt * 10f));
                if (s.dopplerLevel != 0f) { h.DopplerLevel = s.dopplerLevel; s.dopplerLevel = 0f; }
                float want = h.Base * (Mathf.Abs(h.Doppler - 1f) < 0.002f ? 1f : h.Doppler);
                s.pitch = want;
                h.Written = want;
            }
        }

        static readonly List<AudioSource> scratch = new List<AudioSource>();

        /// <summary>The sources given back as they were (the craft gone from the air, or the camera from it).</summary>
        static void Restore(Craft k)
        {
            foreach (Heard h in k.Sources)
            {
                if (h.Source == null) continue;
                if (h.Written >= 0f && Mathf.Abs(h.Source.pitch - h.Written) < 1e-4f) h.Source.pitch = h.Base;
                h.Source.dopplerLevel = h.DopplerLevel;
            }
            k.Sources.Clear();
        }

        void Forget()
        {
            gone.Clear();
            foreach (KeyValuePair<Guid, Craft> p in crafts) if (!seen.Contains(p.Key)) gone.Add(p.Key);
            foreach (Guid g in gone) { Restore(crafts[g]); crafts.Remove(g); }
        }

        void Release()
        {
            foreach (Craft k in crafts.Values) Restore(k);
            crafts.Clear();
        }

        void OnDestroy()
        {
            Release();
            foreach (AudioSource s in voices) if (s != null) Destroy(s.gameObject);
            voices.Clear();
        }

        // ---------------------------------------------------------------- booms

        /// <summary>
        /// Whether the camera has just been swept by the craft's cone of shock: behind the craft, within the cone's angle of the line
        /// it leaves (sin α = 1 / M), having been outside it, the camera not going with the craft (a camera that does is inside the
        /// cone all along). Its boom then, from where the craft was when the shock left it.
        /// </summary>
        void Boom(Vessel v, Vector3 going, Vector3 rel, Vector3 toCam, float far, float c, float pressureKpa)
        {
            Vector3 size = v.vesselSize;
            Boom(Of(v), v.GetDisplayName(), Mathf.Max(3f, Mathf.Max(size.x, Mathf.Max(size.y, size.z))), (int)(v.persistentId & 0xffff), (Vector3)v.CoMD, going, rel, toCam, far, c, pressureKpa);
        }

        void Boom(Craft k, string name, float length, int id, Vector3 at, Vector3 going, Vector3 rel, Vector3 toCam, float far, float c, float pressureKpa)
        {
            float speed = going.magnitude, mach = speed / Mathf.Max(c, 1f);
#if DEV
            k.Note = name + ": Mach " + mach.ToString("F2") + ", against the camera " + rel.magnitude.ToString("F0") + " of " + speed.ToString("F0") + " m/s, ahead " + Vector3.Dot(toCam, going / Mathf.Max(speed, 1e-3f)).ToString("F0") + " of " + far.ToString("F0") + " m, inside " + k.Inside + ", armed " + k.Armed + "; the camera at " + camVelocity.magnitude.ToString("F0") + " m/s";
#endif
            // (the camera going with the craft, or the craft not faster than sound through the air: no cone to cross)
            if (mach < 1.02f || rel.magnitude < 0.7f * speed || far > 25000f)
            {
                // (slower than sound, or going with the camera: nothing to cross, and nothing crossed when it next is faster: a rocket
                // going through the speed of sound above a camera at the pad sweeps no shock over it, its cone opening behind it)
                k.Inside = false;
                if (mach < 0.97f || rel.magnitude < 0.5f * speed) k.Armed = false;
                return;
            }
            float sinA = 1f / mach;
            Vector3 way = going / speed;
            float along = Vector3.Dot(toCam, way);                  // ahead of the craft (+) or behind it (-)
            float cos2 = along * along / (far * far);
            bool inside = along < 0f && cos2 > 1f - sinA * sinA;
            if (!inside && (along > 0f || cos2 < (1f - sinA * sinA) * 0.95f)) k.Armed = true;       // (outside the cone, with a margin: ready)
            if (inside && !k.Inside && k.Armed)
            {
                k.Armed = false;
                Play(name, id, at, toCam, far, length, mach, c, pressureKpa);
            }
            k.Inside = inside;
        }

        readonly List<AudioSource> voices = new List<AudioSource>();
        int lastSeed;

        void Play(string name, int id, Vector3 at, Vector3 toCam, float far, float length, float mach, float c, float pressureKpa)
        {
            // (how loud: the overpressure of the far field goes as (M^2 - 1)^(1/8) L^(3/4) r^(-3/4), and with the root of the air's
            // pressure; made a volume so that a fighter's length passing within half a kilometre is as loud as the game gives: a
            // boom that near is louder than anything else there)
            float m2 = Mathf.Max(0.01f, mach * mach - 1f);
            float loud = 9f * Mathf.Pow(length / far, 0.75f) * Mathf.Pow(m2, 0.125f) * Mathf.Sqrt(Mathf.Clamp01(Mathf.Max(pressureKpa, 0f) / 101.325f));
            loud = Mathf.Clamp01(loud) * GameSettings.SHIP_VOLUME;
            if (loud < 0.01f) return;
            // (how long the N-wave is, from its front shock to its back: Whitham's rule, 1.82 M r^(1/4) L^(3/4) / (c (M^2 - 1)^(3/8)),
            // here scaled to come to the tenths of a second real booms are)
            float wave = Mathf.Clamp(0.32f * mach * Mathf.Pow(far, 0.25f) * Mathf.Pow(length, 0.75f) / (c * Mathf.Pow(m2, 0.375f)), 0.03f, 0.45f);
            int seed = ++lastSeed + id;
            AudioClip clip = Make(wave, far, seed);
            AudioSource s = Voice();
            if (s.clip != null && s.clip.name == "Natural Weather boom") Destroy(s.clip);
            s.clip = clip;
            // (from the craft's side of the camera: the loudness is set here, not by the distance (a flat rolloff), so only the way it comes from counts)
            s.transform.position = at + toCam - toCam / far * 30f;
            s.volume = loud;
            s.pitch = 1f;
            s.Play();
#if DEV
            Said = "a boom from " + name + " at Mach " + mach.ToString("F2") + ", " + far.ToString("F0") + " m off, " + length.ToString("F0") + " m long: the wave " + (wave * 1000f).ToString("F0") + " ms, volume " + loud.ToString("F2");
            Booms++;
#endif
        }

#if DEV
        public static string Said = "";
        public static int Booms;
        static AirSound running;
        void Awake() { running = this; }

        /// <summary>
        /// (Development build: a craft that is not there passed by the camera, for hearing and timing the boom: "speed m/s, how far off
        /// it passes m, how long it is m", flying level past the camera's side from a kilometre and a half before it, through the same
        /// cone's sweep as a real one, the air the camera's.)
        /// </summary>
        public static string Pass(string how)
        {
            if (running == null) return "not running";
            string[] w = (how ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float speed = w.Length > 0 ? float.Parse(w[0], System.Globalization.CultureInfo.InvariantCulture) : 600f;
            float off = w.Length > 1 ? float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : 250f;
            float length = w.Length > 2 ? float.Parse(w[2], System.Globalization.CultureInfo.InvariantCulture) : 20f;
            running.StartCoroutine(running.Passing(speed, off, length));
            return "a craft of " + length + " m passing " + off + " m off at " + speed + " m/s";
        }

        System.Collections.IEnumerator Passing(float speed, float off, float length)
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (cam == null) yield break;
            CelestialBody body = FlightGlobals.currentMainBody;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, cam.transform.position);
            Vector3 way = Vector3.ProjectOnPlane(cam.transform.right, up).normalized;
            Vector3 start = cam.transform.position + up * off - way * 1500f;
            var k = new Craft { Armed = false };
            float t = 0f;
            // (until well past where the cone sweeps the camera: off / tan(α) behind the camera's side, α the cone's angle)
            float mach0 = speed / 340f, behind = mach0 > 1f ? off / Mathf.Tan(Mathf.Asin(1f / mach0)) : off;
            while (t < (1500f + 1.3f * behind) / speed + 2f)
            {
                t += Time.deltaTime;
                double alt = body.GetAltitude(cam.transform.position), p = body.GetPressure(alt);
                float c = Mathf.Max(50f, (float)body.GetSpeedOfSound(p, body.GetDensity(p, body.GetTemperature(alt))));
                Vector3 at = start + way * speed * t, toCam = cam.transform.position - at;
                Boom(k, "a craft passing (a test)", length, 1, at, way * speed, way * speed - camVelocity, toCam, toCam.magnitude, c, (float)p);
                yield return null;
            }
        }

        /// <summary>(Development build: a boom's sound written to a file as made, "wave-seconds far-metres path", to look at.)</summary>
        public static string BoomFile(string how)
        {
            string[] w = (how ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float wave = float.Parse(w[0], System.Globalization.CultureInfo.InvariantCulture), far = float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture);
            AudioClip clip = Make(wave, far, 7);
            var data = new float[clip.samples];
            clip.GetData(data, 0);
            Destroy(clip);
            using (var f = new System.IO.BinaryWriter(System.IO.File.Create(w[2])))
            {
                f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + data.Length * 2);
                f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(Rate); f.Write(Rate * 2); f.Write((short)2); f.Write((short)16);
                f.Write(System.Text.Encoding.ASCII.GetBytes("data")); f.Write(data.Length * 2);
                foreach (float x in data) f.Write((short)Mathf.Clamp(x * 32767f, -32768f, 32767f));
            }
            return "written " + data.Length + " samples to " + w[2];
        }

        /// <summary>(Development build: each craft as the booms see it just now.)</summary>
        public static string State()
        {
            if (running == null) return "not running";
            var o = new System.Text.StringBuilder();
            foreach (Craft k in running.crafts.Values) o.Append(k.Note).Append("; ").Append(k.Shift).Append(" | ");
            return o.Length > 0 ? o.ToString() : "no craft";
        }
#endif

        AudioSource Voice()
        {
            foreach (AudioSource s in voices) if (s != null && !s.isPlaying) return s;
            if (voices.Count >= 4) return voices[0];
            var go = new GameObject("Natural Weather boom");
            go.transform.SetParent(transform, false);
            AudioSource a = go.AddComponent<AudioSource>();
            a.playOnAwake = false; a.dopplerLevel = 0f; a.spatialBlend = 0.6f; a.spread = 120f;
            a.rolloffMode = AudioRolloffMode.Custom; a.maxDistance = 50000f;
            a.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            voices.Add(a);
            return a;
        }

        const int Rate = 44100;

        /// <summary>
        /// The boom's sound: the N-wave (the front shock's rise, the fall through to as far below, the back shock's rise; each shock's
        /// rise a little slower the further it has come: a few milliseconds), the ground's echo of it a little after and weaker, and
        /// a rumble of the sky's echoes dying away; the cracks dulled the further off (the air takes the high sounds first).
        /// </summary>
        static AudioClip Make(float wave, float far, int seed)
        {
            var luck = new System.Random(seed);
            float tail = 1.6f;
            int n = (int)((wave + tail) * Rate);
            var s = new float[n];
            // (a shock rises within some tens of microseconds close by, which is its crack; over kilometres of air, turbulence and
            // absorption smear it towards a millisecond or more. A smooth rise of half a millisecond has next to nothing above 300 Hz
            // (its spectrum falls away past about 1 / (pi^2 x the rise)): it made a thump, with nothing of the crack in it)
            float k = Mathf.Clamp01(far / 8000f);
            float rise = 0.00004f + 0.0015f * k * k;
            float echo = 0.006f + 0.02f * (float)luck.NextDouble(), echoGain = 0.55f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] = NWave(t, wave, rise) + echoGain * NWave(t - echo, wave, rise * 1.6f);
            }
            // (the rumble: noise, low, from the first shock, dying over a second)
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(luck.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.02f; lp2 += (lp - lp2) * 0.02f;
                s[i] += 3.2f * lp2 * Mathf.Exp(-t / 0.55f) * Mathf.Clamp01(t / 0.01f);
            }
            // (the cracks dulled with distance: two one-pole low passes, the cut from 9 kHz close to 600 Hz kilometres off)
            float cut = Mathf.Clamp(9000f * Mathf.Pow(400f / Mathf.Max(far, 400f), 0.8f), 600f, 9000f);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cut / Rate);
            float y1 = 0f, y2 = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++) { y1 += (s[i] - y1) * a; y2 += (y1 - y2) * a; s[i] = y2; peak = Mathf.Max(peak, Mathf.Abs(y2)); }
            // (and the slow fall of the N-wave, below hearing, taken out, as the ear and a microphone do (under some 25 Hz): what is heard
            // is its two shocks; kept, it was nine tenths of the sound, and the cracks quiet beside it)
            float hp = 0f;
            for (int i = 0; i < n; i++) { hp += (s[i] - hp) * 0.0036f; s[i] -= hp; }
            peak = 1e-6f;
            foreach (float x in s) peak = Mathf.Max(peak, Mathf.Abs(x));
            for (int i = 0; i < n; i++) s[i] = s[i] / peak * 0.95f;
            AudioClip clip = AudioClip.Create("Natural Weather boom", n, 1, Rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        /// <summary>The N-wave's pressure at a time: 0 before, a rise to +1 over the shock's rise, a straight fall to -1, a rise back to 0.</summary>
        static float NWave(float t, float wave, float rise)
        {
            if (t < 0f || t > wave + rise * 4f) return 0f;
            float front = 0.5f + 0.5f * (float)Math.Tanh((t - rise * 2f) / rise);
            float back = 0.5f + 0.5f * (float)Math.Tanh((t - wave - rise * 2f) / rise);
            float ramp = 1f - 2f * Mathf.Clamp01(t / wave);
            return front * ramp * (1f - back);
        }
    }
}
