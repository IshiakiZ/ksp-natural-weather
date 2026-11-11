using System;
using System.Collections.Generic;
using System.Reflection;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The weather near the camera taken from Blackrack's clouds while they are drawn in place of these (CloudsEve), so that the
    /// rain, the ice on a craft, the lightning that strikes it, the light under the clouds and what the page says go with the
    /// clouds that are seen; the wind, the cold, fog and dust stay the weather's own. Read from his clouds as they are drawn, on
    /// the game's thread, twice a second, through reflection (nothing of his built against): over the camera and at six places
    /// 4 km round it, how much cloud there is at ten heights through each of his layers, and of which of his kinds; from those of
    /// his layers that rain (those with a field of falling rain of his own: his shafts of rain, from the ground to the clouds'
    /// base), how much falls under the camera and how often his kinds there lighten. Given for any place within 12 km of the
    /// camera (Model.Here); and, for Natural Light, how much of his cloud there is along the line to the sun (Provider.ShadeAt).
    /// </summary>
    static class HisSky
    {
        delegate float Sampler(Vector3 at, out float kind, bool aboveSea);

        sealed class Layer
        {
            public Sampler Sample;
            public Func<float, float> RainOf, LightningOf;
            public Func<float> Inner, Outer, Sea;
            public bool Rains, Lightens;
        }

        sealed class Seen
        {
            public string World;
            public double Lat, Lon;
            public CloudLayer Low, Middle, High;
            public float Rain, Storm;
        }

        const double Reach = 12000.0;
        const int Levels = 10;
        // (written whole on the game's thread, read from any: Model.Here is asked on others)
        static volatile Seen seen;
        static Type madeFor;
        static bool usable;
        static MethodInfo sample, rainOf, lightningOf, inner, outer, sea;
        static FieldInfo rainField, boltsField;
        static readonly Dictionary<object, Layer> layers = new Dictionary<object, Layer>();
        static readonly List<object> volumes = new List<object>();
        static readonly List<Layer> drawn = new List<Layer>();
        static readonly Vector3d[] ways = new Vector3d[7];
        static CelestialBody lookedFor;
        static float lookedAt = -100f, listedAt = -100f;
        static int listedShown = -1;

        /// <summary>Whether the weather near the camera is his just now.</summary>
        public static bool On => seen != null;

        /// <summary>His clouds' calls found by name, once for his clouds' type; false where any is not there as looked for.</summary>
        static bool Usable(Type t)
        {
            if (t == madeFor) return usable;
            madeFor = t; usable = false; layers.Clear();
            if (t == null) return false;
            const BindingFlags open = BindingFlags.Instance | BindingFlags.Public, inside = open | BindingFlags.NonPublic;
            try
            {
                sample = t.GetMethod("SampleCoverage", open, null, new[] { typeof(Vector3), typeof(float).MakeByRefType(), typeof(bool) }, null);
                rainOf = t.GetMethod("GetInterpolatedCloudTypeParticleFieldDensity", open, null, new[] { typeof(float) }, null);
                lightningOf = t.GetMethod("GetInterpolatedCloudTypeLightningFrequency", open, null, new[] { typeof(float) }, null);
                inner = t.GetProperty("InnerSphereRadius", open)?.GetGetMethod();
                outer = t.GetProperty("OuterSphereRadius", open)?.GetGetMethod();
                sea = t.GetProperty("PlanetRadius", open)?.GetGetMethod();
                rainField = t.GetField("particleField", inside);
                boltsField = t.GetField("lightning", inside);
                usable = sample != null && rainOf != null && lightningOf != null && inner != null && outer != null && sea != null;
                // (and what quietens his rain and its sound: QuietRain)
                fieldConfig = rainField != null ? rainField.FieldType.GetField("particleFieldConfigObject", inside) : null;
                configMin = fieldConfig != null ? Single(fieldConfig.FieldType.GetField("minCoverageThreshold", inside)) : null;
                configMax = fieldConfig != null ? Single(fieldConfig.FieldType.GetField("maxCoverageThreshold", inside)) : null;
                kinds = t.GetProperty("CloudTypes", open);
                Type kind = kinds != null && kinds.PropertyType.IsGenericType ? kinds.PropertyType.GetGenericArguments()[0] : null;
                kindVolume = kind != null ? Single(kind.GetField("ambientVolume", inside)) : null;
            }
            catch (Exception) { usable = false; }
            return usable;
        }

        static FieldInfo Single(FieldInfo f) => f != null && f.FieldType == typeof(float) ? f : null;

        /// <summary>One of his volumes made into a Layer, once (null where it cannot be).</summary>
        static Layer LayerOf(object v)
        {
            if (layers.TryGetValue(v, out Layer l)) return l;
            l = null;
            try
            {
                var one = new Layer
                {
                    Sample = (Sampler)Delegate.CreateDelegate(typeof(Sampler), v, sample, false),
                    RainOf = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), v, rainOf, false),
                    LightningOf = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), v, lightningOf, false),
                    Inner = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), v, inner, false),
                    Outer = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), v, outer, false),
                    Sea = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), v, sea, false),
                    Rains = rainField != null && rainField.GetValue(v) != null,
                    Lightens = boltsField != null && boltsField.GetValue(v) != null,
                };
                if (one.Sample != null && one.RainOf != null && one.LightningOf != null && one.Inner != null && one.Outer != null && one.Sea != null) l = one;
            }
            catch (Exception) { }
            layers[v] = l;
            return l;
        }

        // ---------------------------------------------------------------- his rain and its sound, quietened
        // (While his clouds are drawn and the weather's own rain is on, which goes by his clouds here (Look) and falls in place of his
        // with its own sound: the two together were twice the rain. His layers switch their parts on again every frame by the
        // camera's height, so his own thresholds are raised instead, which his rain goes by every frame: his rain shows only where
        // there is more of his shaft than there can be, and his rain's sound is as loud as his kinds of rain say, nothing. Put back
        // as they were whenever his clouds are hidden or not drawn, the weather's rain is switched off, or the scene changes. His
        // wet ground likewise, where the weather's own is on (CloudsEve.QuietHisWet). His lightning is left: the weather's own storms
        // are not where his are, and flash no more under his clouds (Lightning). His drops on a cockpit's windows are left: seen
        // only from inside, where the weather's drops on the camera's glass are not drawn.)
        static FieldInfo fieldConfig, configMin, configMax, kindVolume;
        static PropertyInfo kinds;
        static readonly Dictionary<object, Vector2> keptThresholds = new Dictionary<object, Vector2>();
        static readonly Dictionary<object, float> keptVolumes = new Dictionary<object, float>();
        static bool sceneWatched;

        static bool OursRain => Weather.On != null && Weather.On && Weather.Rain != null && Weather.Rain;

        static void QuietRain(bool water)
        {
            if (!OursRain || !water) { PutBack(); return; }
            foreach (object v in volumes)
            {
                if (!layers.TryGetValue(v, out Layer l) || l == null || !l.Rains) continue;
                try
                {
                    object field = rainField.GetValue(v);
                    object config = field != null && fieldConfig != null ? fieldConfig.GetValue(field) : null;
                    if (config != null && configMin != null && configMax != null && !keptThresholds.ContainsKey(config))
                    {
                        keptThresholds[config] = new Vector2((float)configMin.GetValue(config), (float)configMax.GetValue(config));
                        configMin.SetValue(config, 2f); configMax.SetValue(config, 3f);
                    }
                    if (kindVolume != null && kinds != null && kinds.GetValue(v, null) is System.Collections.IList list)
                        foreach (object k in list)
                            if (k != null && !keptVolumes.ContainsKey(k)) { keptVolumes[k] = (float)kindVolume.GetValue(k); kindVolume.SetValue(k, 0f); }
                }
                catch (Exception) { }
            }
        }

        /// <summary>His rain and its sound put back as they were (and whenever the scene changes, or the weather's driver goes).</summary>
        public static void PutBack()
        {
            if (keptThresholds.Count == 0 && keptVolumes.Count == 0) return;
            foreach (KeyValuePair<object, Vector2> k in keptThresholds) try { configMin.SetValue(k.Key, k.Value.x); configMax.SetValue(k.Key, k.Value.y); } catch (Exception) { }
            foreach (KeyValuePair<object, float> k in keptVolumes) try { kindVolume.SetValue(k.Key, k.Value); } catch (Exception) { }
            keptThresholds.Clear(); keptVolumes.Clear();
        }

        // ---------------------------------------------------------------- his clouds read

        /// <summary>Once a frame (WeatherDriver): his clouds read again twice a second while they are drawn here; forgotten when not.</summary>
        public static void Look()
        {
            if (!sceneWatched) { sceneWatched = true; GameEvents.onGameSceneLoadRequested.Add(s => PutBack()); }
            CelestialBody body = HighLogic.LoadedSceneIsFlight && !MapView.MapIsEnabled ? FlightGlobals.currentMainBody : null;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (body == null || cam == null || !body.atmosphere || CloudsEve.HidingHis || Clouds.Drawing || !Usable(CloudsEve.HisVolumeType)) { seen = null; PutBack(); return; }
            float now = Time.realtimeSinceStartup;
            if (now - lookedAt < 0.5f && body == lookedFor) return;
            lookedAt = now;
            // (his layers listed afresh every ten seconds, at once on another world and whenever they have been shown again)
            if (body != lookedFor || now - listedAt > 10f || CloudsEve.HisShown != listedShown)
            {
                CloudsEve.HisVolumes(body, volumes);
                drawn.Clear();
                foreach (object v in volumes) { Layer l = LayerOf(v); if (l != null) drawn.Add(l); }
                lookedFor = body; listedAt = now; listedShown = CloudsEve.HisShown;
            }
            if (drawn.Count == 0) { seen = null; PutBack(); return; }
            // (his falling fields are rain and snow only on a world whose falls are water: Duna's are his dust storms', not rain,
            // and left as his, as the dust is the weather's own)
            World w = World.Of(body);
            bool water = w != null && w.Climate.Water > 0f;
            QuietRain(water);
            CloudsEve.QuietHisWet();
            Vector3d centre = body.position, camAt = cam.transform.position;
            double lat = body.GetLatitude(camAt), lon = body.GetLongitude(camAt);
            double ground = body.TerrainAltitude(lat, lon, true);
            if (body.ocean && ground < 0.0) ground = 0.0;
            Provider.Axes(body, camAt, out Vector3d e, out Vector3d n, out Vector3d u);
            ways[0] = u;
            for (int k = 1; k < 7; k++)
            {
                double a = k * Math.PI / 3.0;
                ways[k] = (u + (Math.Cos(a) * e + Math.Sin(a) * n) * (4000.0 / body.Radius)).normalized;
            }
            // ---- his shafts of rain first: how much falls under the camera, and how often they lighten there and round it
            float rain = 0f, storm = 0f;
            foreach (Layer l in drawn)
            {
                if (!l.Rains || !water) continue;
                try
                {
                    float rIn = l.Inner(), rOut = l.Outer(), rSea = l.Sea();
                    // (as his own rain does, which falls where the camera is inside the shaft: here, low in it under the camera,
                    // from where it has as much as he counts as nothing, 0.15, to all of it)
                    for (int k = 0; k < 3; k++)
                    {
                        double r = rSea + ground + 60.0 + 250.0 * k;
                        if (r < rIn || r > rOut) continue;
                        float cov = l.Sample((Vector3)(centre + u * r), out float kind, false);
                        rain = Math.Max(rain, Mathf.Clamp01((cov - 0.15f) / 0.85f) * l.RainOf(kind));
                    }
                    if (!l.Lightens) continue;
                    for (int c = 0; c < 7; c++)
                    {
                        float most = 0f, mostKind = 0f;
                        for (int k = 0; k < Levels; k++)
                        {
                            float cov = l.Sample((Vector3)(centre + ways[c] * (rIn + (k + 0.5f) / Levels * (rOut - rIn))), out float kind, false);
                            if (cov > most) { most = cov; mostKind = kind; }
                        }
                        // (his lightning strikes where there is more than 0.1 of the shaft, as often as its kind says: his storms'
                        // kind always, his rain's one time in twenty)
                        if (most > 0.1f) storm = Math.Max(storm, Mathf.Clamp01(2f * most * l.LightningOf(mostKind)));
                    }
                }
                catch (Exception) { }
            }
            // ---- his clouds: how much there is over the camera and round it, and where, in the three layers the weather has
            var bands = new CloudLayer[3];
            for (int b = 0; b < 3; b++) bands[b].Base = float.MaxValue;
            foreach (Layer l in drawn)
            {
                if (l.Rains) continue;
                try
                {
                    float rIn = l.Inner(), rOut = l.Outer(), rSea = l.Sea();
                    if (!(rOut > rIn)) continue;
                    float covered = 0f, over = 0f, low = float.MaxValue, high = float.MinValue;
                    for (int c = 0; c < 7; c++)
                    {
                        float most = 0f;
                        for (int k = 0; k < Levels; k++)
                        {
                            float r = rIn + (k + 0.5f) / Levels * (rOut - rIn);
                            float cov = l.Sample((Vector3)(centre + ways[c] * r), out float _, false);
                            most = Math.Max(most, cov);
                            if (c == 0 && cov > 0.02f) { low = Math.Min(low, r); high = Math.Max(high, r); }
                        }
                        covered += Mathf.Clamp01(most * 2f);
                        if (c == 0) over = most;
                    }
                    float cover = covered / 7f;
                    if (cover < 0.02f) continue;
                    // (his heights over the sea: where his cloud is over the camera, else his layer's whole depth)
                    float bottom = (low < float.MaxValue ? low : rIn) - rSea, top = (high > float.MinValue ? high : rOut) - rSea;
                    float above = bottom - (float)ground;
                    int band = above < 2500f ? 0 : above < 6500f ? 1 : 2;
                    // (a low layer deep over a storm of his is a thunderstorm's cloud: his own storms', 4.5 km deep over the camera)
                    CloudKind kind = band == 0 ? (top - bottom > 2500f && storm >= 0.5f ? CloudKind.Cumulonimbus : cover > 0.75f ? CloudKind.Stratocumulus : CloudKind.Cumulus)
                                   : band == 1 ? (cover > 0.75f ? CloudKind.Altostratus : CloudKind.Altocumulus) : CloudKind.Cirrus;
                    CloudLayer was = bands[band];
                    if (was.Kind == CloudKind.None || cover > was.Cover && was.Kind != CloudKind.Cumulonimbus || kind == CloudKind.Cumulonimbus) was.Kind = kind;
                    was.Cover = 1f - (1f - was.Cover) * (1f - cover);
                    was.Base = Math.Min(was.Base, bottom); was.Top = Math.Max(was.Top, top);
                    was.Water = Math.Max(was.Water, Mathf.Clamp01(over));
                    bands[band] = was;
                }
                catch (Exception) { }
            }
            for (int b = 0; b < 3; b++) if (bands[b].Kind == CloudKind.None) bands[b] = default(CloudLayer);
            seen = new Seen { World = body.bodyName, Lat = lat, Lon = lon, Low = bands[0], Middle = bands[1], High = bands[2], Rain = 0.75f * rain, Storm = storm };
        }

        /// <summary>The weather at a place, near the camera his clouds' (Model.Here): the clouds, the rain and the storms.</summary>
        public static void Over(World w, double lat, double lon, ref WeatherHere h)
        {
            Seen s = seen;
            if (s == null || w == null || w.Name != s.World || Model.Distance(w, lat, lon, s.Lat, s.Lon) > Reach) return;
            h.Low = s.Low; h.Middle = s.Middle; h.High = s.High;
            h.Rain = s.Rain; h.Storm = s.Storm;
            // (hail only out of his storms)
            if (s.Storm < 0.5f) { h.Hail = 0f; h.HailSize = 0f; }
        }

        /// <summary>How much of his cloud there is at a point, 0 to 1 as Provider.CloudAt says of these (the game's thread only).</summary>
        public static float CoverAt(World w, double lat, double lon, double alt)
        {
            Seen s = seen;
            CelestialBody body = lookedFor;
            if (s == null || body == null || w == null || w.Name != s.World) return 0f;
            var at = (Vector3)body.GetWorldSurfacePosition(lat, lon, alt);
            float most = 0f;
            foreach (Layer l in drawn)
            {
                if (l.Rains) continue;
                try { most = Math.Max(most, l.Sample(at, out float _, false)); } catch (Exception) { }
            }
            return Mathf.Clamp01(most * 2f);
        }

#if DEV
        /// <summary>(Development build: what was last read from his clouds, and of which of his layers.)</summary>
        public static string Said()
        {
            Seen s = seen;
            if (s == null) return "nothing read: " + (CloudsEve.HidingHis ? "his clouds hidden" : Clouds.Drawing ? "these clouds drawn" : CloudsEve.HisVolumeType == null ? "his clouds not found" : "none of his drawn here")
                                  + "; his rain quietened in " + keptThresholds.Count + " fields, its sound in " + keptVolumes.Count + " kinds";
            var b = new System.Text.StringBuilder();
            b.Append(s.World).Append(' ').Append(s.Lat.ToString("F3")).Append(", ").Append(s.Lon.ToString("F3")).Append(": rain ").Append(s.Rain.ToString("F2")).Append(", storm ").Append(s.Storm.ToString("F2"));
            foreach (CloudLayer l in new[] { s.Low, s.Middle, s.High })
                if (l.Kind != CloudKind.None) b.Append("; ").Append(l.Kind).Append(' ').Append(l.Cover.ToString("F2")).Append(' ').Append(l.Base.ToString("F0")).Append('-').Append(l.Top.ToString("F0")).Append(" m, water ").Append(l.Water.ToString("F2"));
            int rains = 0, lightens = 0;
            foreach (Layer l in drawn) { if (l.Rains) rains++; if (l.Lightens) lightens++; }
            b.Append(" (from ").Append(drawn.Count).Append(" of his layers, ").Append(rains).Append(" with rain, ").Append(lightens).Append(" with lightning)");
            // (and his rain quietened: how many of his fields and kinds, and how loud his rain's sound is just now)
            b.Append("; his rain quietened in ").Append(keptThresholds.Count).Append(" fields, its sound in ").Append(keptVolumes.Count).Append(" kinds");
            const BindingFlags inside = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (object v in volumes)
            {
                if (!layers.TryGetValue(v, out Layer l) || l == null || !l.Rains) continue;
                try
                {
                    object sound = v.GetType().GetField("ambientSound", inside)?.GetValue(v);
                    var source = sound?.GetType().GetField("audioSource", inside)?.GetValue(sound) as AudioSource;
                    var field = rainField.GetValue(v);
                    var shown = field?.GetType().GetField("fieldMeshRenderer", inside)?.GetValue(field) as Renderer;
                    b.Append(" | sound ").Append(source != null ? source.volume.ToString("F3") + (source.isPlaying ? " playing" : " paused") : "none")
                     .Append(", drawn ").Append(shown != null ? (shown.enabled && shown.gameObject.activeInHierarchy ? "yes" : "no") : "?");
                }
                catch (Exception) { }
            }
            return b.ToString();
        }

        /// <summary>(Development build: the light at the camera as Natural Light would be given it, from his clouds where they are drawn.)</summary>
        public static string ShadeHere()
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (body == null || cam == null) return "no camera";
            Vector3d at = cam.transform.position, toSun = (Planetarium.fetch.Sun.position - at).normalized;
            Provider.Axes(body, at, out Vector3d e, out Vector3d n, out Vector3d u);
            WeatherShade s = Provider.ShadeAt(World.Of(body), body.GetLatitude(at), body.GetLongitude(at), body.GetAltitude(at),
                (float)Vector3d.Dot(toSun, e), (float)Vector3d.Dot(toSun, n), (float)Vector3d.Dot(toSun, u), Planetarium.GetUniversalTime());
            return "the light here: sun " + s.Sun.ToString("F2") + ", sky " + s.Sky.ToString("F2") + ", grey " + s.Grey.ToString("F2") + (s.SunDimmed ? " (dimmed by the clouds drawn)" : "") + (On ? ", from his clouds" : ", from the weather's own");
        }

        /// <summary>(Development build: each of his layers drawn here, its depth over the sea, and how much cloud it has at ten heights over the camera.)</summary>
        public static string Column()
        {
            CelestialBody body = lookedFor;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (body == null || cam == null || drawn.Count == 0) return "none of his layers read";
            Vector3d centre = body.position, up = ((Vector3d)cam.transform.position - centre).normalized;
            var b = new System.Text.StringBuilder();
            foreach (Layer l in drawn)
            {
                float rIn = l.Inner(), rOut = l.Outer(), rSea = l.Sea();
                b.Append(l.Rains ? "rain" : "cloud").Append(l.Lightens ? "+lightning" : "").Append(' ').Append((rIn - rSea).ToString("F0")).Append('-').Append((rOut - rSea).ToString("F0")).Append(" m:");
                for (int k = 0; k < Levels; k++)
                {
                    float cov = l.Sample((Vector3)(centre + up * (rIn + (k + 0.5f) / Levels * (rOut - rIn))), out float kind, false);
                    b.Append(' ').Append(cov.ToString("F2")).Append('/').Append(kind.ToString("F2"));
                }
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>(Development build: where his shafts of rain rain now and where they lighten, the most of each, over a box of latitude
        /// and longitude round the camera: "degrees across, degrees apart", 40 and 1 unless said, and "land" for over dry ground only.)</summary>
        public static string FindRain(string arg)
        {
            CelestialBody body = lookedFor;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (body == null || cam == null || drawn.Count == 0) return "none of his layers read";
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            double across = t.Length > 0 ? double.Parse(t[0], System.Globalization.CultureInfo.InvariantCulture) : 40.0;
            double apart = t.Length > 1 ? double.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture) : 1.0;
            double lat0 = body.GetLatitude(cam.transform.position), lon0 = body.GetLongitude(cam.transform.position);
            Vector3d centre = body.position;
            var rains = new List<KeyValuePair<float, string>>();
            var storms = new List<KeyValuePair<float, string>>();
            for (double la = lat0 - across / 2; la <= lat0 + across / 2; la += apart)
                for (double lo = lon0 - across / 2; lo <= lon0 + across / 2; lo += apart)
                {
                    Vector3d up = (body.GetWorldSurfacePosition(la, lo, 0.0) - centre).normalized;
                    double ground = Math.Max(body.ocean ? 0.0 : double.MinValue, body.TerrainAltitude(la, lo, true));
                    float rain = 0f, storm = 0f;
                    foreach (Layer l in drawn)
                    {
                        if (!l.Rains) continue;
                        float rIn = l.Inner(), rOut = l.Outer(), rSea = l.Sea();
                        // (as Look reads them: the rain low in the shaft over the ground there, the lightning anywhere up it)
                        for (int k = 0; k < 3; k++)
                        {
                            double r = rSea + ground + 60.0 + 250.0 * k;
                            if (r < rIn || r > rOut) continue;
                            float cov = l.Sample((Vector3)(centre + up * r), out float kind, false);
                            rain = Math.Max(rain, Mathf.Clamp01((cov - 0.15f) / 0.85f) * l.RainOf(kind));
                        }
                        if (!l.Lightens) continue;
                        for (int k = 0; k < Levels; k++)
                        {
                            float cov = l.Sample((Vector3)(centre + up * (rIn + (k + 0.5f) / Levels * (rOut - rIn))), out float kind, false);
                            if (cov > 0.1f) storm = Math.Max(storm, Mathf.Clamp01(2f * cov * l.LightningOf(kind)));
                        }
                    }
                    string at = la.ToString("F1") + ", " + lo.ToString("F1");
                    // ("land" in the words: over dry ground only, where a craft can be set down under it)
                    if (Array.IndexOf(t, "land") >= 0 && body.TerrainAltitude(la, lo, true) < 5.0) continue;
                    if (rain > 0.05f) rains.Add(new KeyValuePair<float, string>(rain, at));
                    if (storm > 0.05f) storms.Add(new KeyValuePair<float, string>(storm, at));
                }
            rains.Sort((a, b) => b.Key.CompareTo(a.Key)); storms.Sort((a, b) => b.Key.CompareTo(a.Key));
            var say = new System.Text.StringBuilder("rain at " + rains.Count + " places:");
            for (int k = 0; k < Math.Min(6, rains.Count); k++) say.Append(" (").Append(rains[k].Value).Append(") ").Append(rains[k].Key.ToString("F2")).Append(';');
            say.Append(" lightning at ").Append(storms.Count).Append(" places:");
            for (int k = 0; k < Math.Min(6, storms.Count); k++) say.Append(" (").Append(storms[k].Value).Append(") ").Append(storms[k].Key.ToString("F2")).Append(';');
            return say.ToString();
        }
#endif
    }
}
