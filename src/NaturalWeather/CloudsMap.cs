using System;
using System.Threading.Tasks;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The weather over a whole planet, as three small pictures the cloud shader reads (as in Horizon Zero Dawn's clouds, where a
    /// "weather map" says how much cloud of what kind is where, and noise makes the clouds themselves): longitude across,
    /// latitude up. The first holds the low clouds (how much, what kind, base, top); the second how wet, the high cloud, how
    /// stormy and how much middle cloud; the third the middle clouds (kind, base, top, how wet). Asked of the weather
    /// (CloudSource: Keystone's, or the stand-in) for every dot on another thread, every couple of seconds, and handed to the
    /// graphics card when done.
    /// </summary>
    sealed class CloudMap
    {
        public const int Wide = 256, High = 128;

        public Texture2D A, B, C;
        public CelestialBody Body;
        public float HighestTop, LowestBase, HighCloud = 8000f;   // within sight of the place the map was made for
        public bool AnyCloud;                                     // any cloud at all within sight
        public float FogTop, FogBase, FogTopLowest;               // fog within sight: its highest top, lowest base and lowest top (none: FogTop 0). Not in the three above: drawn only from over it (Clouds)
        public double MadeFor = double.NaN;                       // the moment it holds
        public int Made;
        public float Took;                                        // how long the last one took to make (s, on its own thread)
        public int Stride = 1;                                    // how finely the last one was made: the weather asked at one dot in so many each way

        Color32[] nextA, nextB, nextC;
        float nextTop, nextBase, nextHigh, nextTook, nextFogTop, nextFogBase, nextFogTopLowest;
        int nextStride = 1;
        bool nextAny, nextSettled, settled = true;     // (settled: made when the weather knew the world's land and seas: see CloudSource.Settled)
        double nextFor;
        Task working;
        float askedAt = -100f;

        /// <summary>(Development: 1 or 2, every map made so finely; -1, by how long the last took, as until 2026-10-19; 0, as released.)</summary>
        public static int HeldStride;

        /// <summary>Made again at the next chance (the weather was changed by hand).</summary>
        public void Forget() { MadeFor = double.NaN; askedAt = -100f; }

        public void Release()
        {
            foreach (Texture2D t in new[] { A, B, C }) if (t != null) UnityEngine.Object.Destroy(t);
            A = B = C = null;
            Body = null; MadeFor = double.NaN;
        }

        static Texture2D Picture(string name) =>
            new Texture2D(Wide, High, TextureFormat.RGBA32, false, true) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };

        /// <summary>Brings the map up to date for a world and moment (latitude and longitude of the camera, and how many degrees round it can be seen, for the heights within sight); true once there is a map to draw with.</summary>
        public bool Update(CelestialBody body, double ut, double latitude, double longitude, double reach, bool now)
        {
            if (working != null && working.IsCompleted)
            {
                if (working.IsFaulted) Debug.Log("[Natural Weather] the clouds' map could not be made: " + working.Exception?.GetBaseException().Message);
                else if (nextA != null)
                {
                    if (A == null)
                    {
                        A = Picture("Natural Weather: low clouds over the planet");
                        B = Picture("Natural Weather: rain, high and middle cloud over the planet");
                        C = Picture("Natural Weather: middle clouds over the planet");
                    }
                    A.SetPixels32(nextA); A.Apply(false, false);
                    B.SetPixels32(nextB); B.Apply(false, false);
                    C.SetPixels32(nextC); C.Apply(false, false);
                    HighestTop = nextTop; LowestBase = nextBase; HighCloud = nextHigh; AnyCloud = nextAny; MadeFor = nextFor; Took = nextTook; settled = nextSettled; Stride = nextStride; Made++;
                    FogTop = nextFogTop; FogBase = nextFogBase; FogTopLowest = nextFogTopLowest;
                }
                working = null;
            }
            bool otherWorld = body != Body;
            if (otherWorld) { Body = body; MadeFor = double.NaN; }
            float wait = Time.realtimeSinceStartup - askedAt;
            // (made again when the clock has moved on half a minute, at most every two seconds, as under time warp; and at least every
            // twenty seconds: the weather changes over hours, and a thread kept busy costs the game's own threads time on this Mac)
            // (and once more as soon as the weather knows the world's land and seas, if the map in use was made before it did: in the
            // first seconds of the first flight of a session; else the sky changed by itself up to twenty seconds later)
            if (working == null && (otherWorld || now || double.IsNaN(MadeFor) || (wait > 2f && Math.Abs(ut - MadeFor) > 30.0) || (wait > 20f && Math.Abs(ut - MadeFor) > 0.5)
                                    || (!settled && A != null && wait > 1f && CloudSource.Settled(body))))
            {
                askedAt = Time.realtimeSinceStartup;
                CelestialBody b = body;
                double t = ut, lat = latitude, lon = longitude, within = reach;
                // (How finely: the weather asked at every dot; at every other dot each way, the dots between taking their neighbour's,
                // only while time is warped more than ten times, when the map is made every two seconds and the sky races anyway.
                // Until 2026-10-19 it was every other dot whenever the last map had taken more than a second, "a thread kept busy
                // costs the game's own time on this Mac": here it took 0.9 s, more whenever the computer was busy, and then the
                // next map was made coarse and the one after fine again, each time the clouds within sight shifting by half a dot,
                // some 7 km, and back: see Clouds.Trace.)
                int stride = HeldStride > 0 ? HeldStride : HeldStride < 0 ? (Took > 1f ? 2 : 1) : TimeWarp.CurrentRate > 10f ? 2 : 1;
                working = Task.Run(() => Make(b, t, lat, lon, within, stride));
            }
            return A != null && Body == body;
        }

        void Make(CelestialBody body, double ut, double latNear, double lonNear, double reach, int stride)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool known = CloudSource.Settled(body);
            var a = new Color32[Wide * High];
            var b = new Color32[Wide * High];
            var c = new Color32[Wide * High];
            float top = 0f, lowest = 99999f, highSum = 0f, highWeight = 0f, fogTop = 0f, fogLowest = 99999f, fogTopLowest = 99999f;
            bool any = false;
            // (the weather asked for all the places at once: with Natural Weather's own, every storm on the world is found once
            // for the lot. A map of Kerbin took 3.4 s of this thread until 0.2.2, each place asked by itself; see CloudSource.Many)
            Func<double, double, WeatherHere> weatherAt = CloudSource.Many(body, ut);
            for (int j = 0; j < High; j += stride)
            {
                double lat = (j + 0.5 * stride) / High * 180.0 - 90.0;
                for (int i = 0; i < Wide; i += stride)
                {
                    double lon = (i + 0.5 * stride) / Wide * 360.0 - 180.0;
                    WeatherHere w = weatherAt(lat, lon);
                    float lowKind = CloudSource.Kind(w.Low), midKind = CloudSource.Kind(w.Middle);
                    float lowCover = lowKind < 0f ? 0f : Mathf.Clamp01(w.Low.Cover), midCover = midKind < 0f ? 0f : Mathf.Clamp01(w.Middle.Cover);
                    float highCover = w.High.Kind == CloudKind.Cirrus || w.High.Kind == CloudKind.Altostratus ? Mathf.Clamp01(w.High.Cover) : 0f;
                    var ca = new Color32(Byte(lowCover), Byte(Mathf.Max(0f, lowKind)), Byte(w.Low.Base / 8000f), Byte(w.Low.Top / 16000f));
                    var cb = new Color32(Byte(Mathf.Max(w.Low.Water, w.Rain)), Byte(highCover), Byte(w.Storm), Byte(midCover));
                    var cc = new Color32(Byte(Mathf.Max(0f, midKind)), Byte(w.Middle.Base / 12000f), Byte(w.Middle.Top / 16000f), Byte(w.Middle.Water));
                    for (int jj = j; jj < Math.Min(High, j + stride); jj++)
                        for (int ii = i; ii < Math.Min(Wide, i + stride); ii++)
                        {
                            int at = ii + jj * Wide;
                            a[at] = ca; b[at] = cb; c[at] = cc;
                        }
                    // (the highest top and lowest base within sight of the camera: the shell walked through is cut to them)
                    double dLat = Math.Abs(lat - latNear), dLon = Math.Abs(lon - lonNear);
                    if (dLon > 180.0) dLon = 360.0 - dLon;
                    if (dLat < reach && dLon * Math.Cos(lat * Math.PI / 180.0) < reach)
                    {
                        if (lowCover > 0.01f && w.Low.Kind == CloudKind.Fog) { fogTop = Math.Max(fogTop, w.Low.Top); fogLowest = Math.Min(fogLowest, w.Low.Base); fogTopLowest = Math.Min(fogTopLowest, w.Low.Top); }
                        else if (lowCover > 0.01f) { top = Math.Max(top, w.Low.Top); lowest = Math.Min(lowest, w.Low.Base); any = true; }
                        if (midCover > 0.01f) { top = Math.Max(top, w.Middle.Top); lowest = Math.Min(lowest, w.Middle.Base); any = true; }
                        if (highCover > 0.01f) { highSum += 0.5f * (w.High.Base + w.High.Top) * highCover; highWeight += highCover; any = true; }
                    }
                }
            }
            nextA = a; nextB = b; nextC = c; nextTop = top; nextBase = lowest > 99000f ? 1000f : lowest; nextAny = any;
            nextFogTop = fogTop; nextFogBase = fogLowest > 99000f ? 0f : fogLowest; nextFogTopLowest = fogTopLowest;
            nextHigh = highWeight > 0.01f ? highSum / highWeight : 8000f;
            nextSettled = known;
            nextFor = ut; nextTook = (float)clock.Elapsed.TotalSeconds; nextStride = stride;
        }

        static byte Byte(float x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x * 255f), 0, 255);
    }
}
