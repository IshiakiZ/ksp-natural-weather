using System.Collections.Generic;
#if DEV
using System;
using System.Diagnostics;
using System.Text;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>(For the development build, through dev_call: what the weather is, a weather held for a test, a map, timings.)</summary>
    public static class Probe
    {
        /// <summary>A weather held for tests (the number of one of Model.Choices), without touching the player's settings; -1 lets go.</summary>
        public static int Hold = -1;

        /// <summary>
        /// What falls, set by hand everywhere for a test or a picture (what the weather itself gives is rare and passes in
        /// minutes): "hail 0.8 30" (how thickly, the stones' size in mm), "pellets 0.7", "freezing 0.6", "snow 0.7", "rain 0.5";
        /// several at once, in one line; "off" lets go.
        /// </summary>
        public static string Falls(string arg)
        {
            forced = false; fRain = fSnow = fPellets = fFreezing = fHail = -1f; fSize = 25f;
            string[] words = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                float Next(float otherwise) { return i + 1 < words.Length && float.TryParse(words[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : otherwise; }
                switch (words[i])
                {
                    case "hail": fHail = Next(0.7f); if (i + 2 < words.Length && float.TryParse(words[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mm)) fSize = mm; forced = true; break;
                    case "pellets": fPellets = Next(0.7f); forced = true; break;
                    case "freezing": fFreezing = Next(0.7f); forced = true; break;
                    case "snow": fSnow = Next(0.7f); forced = true; break;
                    case "rain": fRain = Next(0.5f); forced = true; break;
                }
            }
            Model.Forget(); Launch.Again();
            return forced ? "what falls is set by hand: " + arg : "what falls is the weather's own";
        }
        static volatile bool forced;
        static float fRain = -1f, fSnow = -1f, fPellets = -1f, fFreezing = -1f, fHail = -1f, fSize = 25f;

        /// <summary>(Called for every place the weather is worked out for: what Falls set.)</summary>
        public static void Shape(ref WeatherHere here)
        {
            if (!forced) return;
            if (fRain >= 0f) here.Rain = fRain;
            if (fSnow >= 0f) { here.Snow = fSnow; here.Rain = Math.Max(here.Rain, 0.5f); here.Freezing = here.Pellets = 0f; }
            if (fPellets >= 0f) { here.Pellets = 1f; here.Snow = here.Freezing = 0f; here.Rain = Math.Max(here.Rain, fPellets); }
            if (fFreezing >= 0f) { here.Freezing = 1f; here.Snow = here.Pellets = 0f; here.Rain = Math.Max(here.Rain, fFreezing); }
            if (fHail >= 0f) { here.Hail = fHail; here.HailSize = fSize; }
        }

        /// <summary>
        /// A fog of one kind held everywhere, for a picture: "radiation 0.9 100" (the kind: radiation, advection, upslope,
        /// steam, frontal; how thick, 0 to 1; how deep, metres); "off" lets go.
        /// </summary>
        public static string FogOf(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0 || t[0] == "off") { FogHeld = false; Model.Forget(); Launch.Again(); return "fog as the weather has it"; }
            if (!Enum.TryParse(t[0], true, out FogKindHeld)) return "no such kind: " + t[0];
            FogAmount = t.Length > 1 ? float.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture) : 0.9f;
            FogDeepHeld = t.Length > 2 ? float.Parse(t[2], System.Globalization.CultureInfo.InvariantCulture) : FogKindHeld == FogKind.Radiation ? 90f : FogKindHeld == FogKind.Steam ? 12f : 350f;
            FogHeld = true;
            Model.Forget(); Launch.Again();
            return "held: " + FogKindHeld + " fog, " + FogAmount.ToString("F2") + ", " + FogDeepHeld.ToString("F0") + " m deep";
        }
        public static volatile bool FogHeld;
        public static FogKind FogKindHeld;
        public static float FogAmount, FogDeepHeld;

        /// <summary>What each kind of fog comes to at a place, and what it is worked out from: "lat lon".</summary>
        public static string FogAt(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            double lat = t.Length > 0 ? double.Parse(t[0], System.Globalization.CultureInfo.InvariantCulture) : 0.0, lon = t.Length > 1 ? double.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
            Base b = Model.Sample(World.Of(FlightGlobals.currentMainBody), lat, lon, Planetarium.GetUniversalTime());
            return Model.FogParts + " | fog " + b.Fog.ToString("F2") + " " + b.FogOf + " " + b.FogDeep.ToString("F0") + " m deep";
        }

        /// <summary>
        /// How much of each kind of fog there is over the world, by the hour: for each of eight hours of the sun's round, the
        /// share of 2,592 places (every five degrees) that have each kind thicker than 0.3, over land and over sea.
        /// </summary>
        public static string Fogs()
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null) return "no world";
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            var said = new StringBuilder();
            string[] names = { "none", "ground", "sea", "hill", "steam", "rain" };
            int[] atHour = new int[8 * 6]; int[] places = new int[8];
            int[] onLand = new int[6], atSea = new int[6]; int land = 0, sea = 0;
            for (int i = 0; i < 72; i++)
                for (int j = 0; j < 36; j++)
                {
                    double lat = -87.5 + 5.0 * j, lon = -177.5 + 5.0 * i;
                    Base b = Model.Sample(w, lat, lon, ut);
                    int kind = b.Fog > 0.3f ? (int)b.FogOf : 0;
                    int hour = (int)Math.Floor(((b.Hour + 180.0) % 360.0) / 45.0) % 8;
                    atHour[hour * 6 + kind]++; places[hour]++;
                    if (b.Land > 0.5f) { land++; onLand[kind]++; } else { sea++; atSea[kind]++; }
                }
            for (int h = 0; h < 8; h++)
            {
                said.Append("\n").Append(new[] { "midnight", "3 h", "sunrise", "9 h", "noon", "15 h", "sunset", "21 h" }[h]).Append(": ");
                for (int k = 1; k < 6; k++) said.Append(names[k]).Append(' ').Append((100.0 * atHour[h * 6 + k] / Math.Max(1, places[h])).ToString("F1")).Append("%  ");
            }
            said.Append("\nover land: ");
            for (int k = 1; k < 6; k++) said.Append(names[k]).Append(' ').Append((100.0 * onLand[k] / Math.Max(1, land)).ToString("F1")).Append("%  ");
            said.Append("\nover sea: ");
            for (int k = 1; k < 6; k++) said.Append(names[k]).Append(' ').Append((100.0 * atSea[k] / Math.Max(1, sea)).ToString("F1")).Append("%  ");
            return said.ToString();
        }

        /// <summary>
        /// Where the held hurricane's middle stands, for a picture: "east north" in metres from the space centre, and
        /// optionally how strong (0 to 1); "off" lets it go to and fro again. (Hold it with Choose 17 first.)
        /// </summary>
        public static string Eye(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            double V(int i, double otherwise) => i < t.Length && double.TryParse(t[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : otherwise;
            if (t.Length == 0 || t[0] == "off") { EyeHeld = false; Model.Forget(); return "the held hurricane goes to and fro"; }
            EyeEast = V(0, 0.0); EyeNorth = V(1, 0.0); EyeNow = (float)V(2, 0.85); EyeHeld = true;
            Model.Forget(); Launch.Again();
            return "the held hurricane's middle stands " + EyeEast.ToString("F0") + " m east and " + EyeNorth.ToString("F0") + " m north of the space centre, strength " + EyeNow.ToString("F2");
        }
        public static volatile bool EyeHeld;
        public static double EyeEast, EyeNorth;
        public static float EyeNow = 0.85f;

        /// <summary>
        /// Where the held squall line's row of storms stands, for a picture: metres east of the space centre (it comes
        /// from the east, moving west), and optionally how strong; "off" lets it come on again. (Hold it with Choose 18 first.)
        /// </summary>
        public static string Row(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            double V(int i, double otherwise) => i < t.Length && double.TryParse(t[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : otherwise;
            if (t.Length == 0 || t[0] == "off") { RowHeld = false; Model.Forget(); return "the held squall line comes on"; }
            RowEast = V(0, 9000.0); RowNow = (float)V(1, 1.0); RowHeld = true;
            Model.Forget(); Launch.Again();
            return "the held squall line's row stands " + RowEast.ToString("F0") + " m east of the space centre, strength " + RowNow.ToString("F2");
        }
        public static volatile bool RowHeld;
        public static double RowEast;
        public static float RowNow = 1f;

        /// <summary>
        /// The wind along a line over the space centre, without gusts: "e0 n0 e1 n1 steps height" (metres east and north of
        /// the pad for its two ends, how many places, how high above the ground). For each place: east, north and up, m/s.
        /// </summary>
        public static string Line(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 6) return "e0 n0 e1 n1 steps height";
            float F(int i) => float.Parse(t[i], System.Globalization.CultureInfo.InvariantCulture);
            CelestialBody body = FlightGlobals.GetHomeBody();
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            int steps = Math.Max(2, (int)F(4));
            var said = new StringBuilder();
            for (int i = 0; i < steps; i++)
            {
                float f = i / (float)(steps - 1);
                double e = F(0) + (F(2) - F(0)) * f, n = F(1) + (F(3) - F(1)) * f;
                double lat = -0.0972 + n / body.Radius * 180.0 / Math.PI, lon = -74.5577 + e / (body.Radius * Math.Cos(-0.0972 * Math.PI / 180.0)) * 180.0 / Math.PI;
                double ground = Math.Max(0.0, body.TerrainAltitude(lat, lon, true));
                Vector3 v = Provider.Local(w, lat, lon, ground + F(5), F(5), ut, false);
                said.Append(e.ToString("F0")).Append(",").Append(n.ToString("F0")).Append(": ").Append(v.x.ToString("F1")).Append(" ").Append(v.y.ToString("F1")).Append(" ").Append(v.z.ToString("F1")).Append(" | ");
            }
            return said.ToString() + Violent.Says(w, -0.0972, -74.5577, ut);
        }

        /// <summary>For a test with the wind not moving craft, without touching the player's settings.</summary>
        public static bool NoPush;
        public static string Push(string arg) { NoPush = arg != null && arg.Trim() == "off"; return NoPush ? "wind does not move craft (test)" : "wind moves craft as set"; }

        /// <summary>Where the craft is against the space centre's pad (metres east and north), how high, and how fast over the ground east, north and up.</summary>
        public static string Track()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            CelestialBody b = v.mainBody;
            double dn = (v.latitude - (-0.0972)) * Math.PI / 180.0 * b.Radius, de = (v.longitude - (-74.5577)) * Math.PI / 180.0 * b.Radius * Math.Cos(v.latitude * Math.PI / 180.0);
            Provider.Axes(b, v.CoMD, out Vector3d e, out Vector3d n, out Vector3d u);
            Vector3d s = v.srf_velocity;
            Vector3 wind = Aero.OnCraft(v);
            return "t " + v.missionTime.ToString("F1") + " s: " + de.ToString("F1") + " m east, " + dn.ToString("F1") + " m north, " + v.altitude.ToString("F0") + " m up; going " + Vector3d.Dot(s, e).ToString("F2") + " east, " + Vector3d.Dot(s, n).ToString("F2") + " north, " + Vector3d.Dot(s, u).ToString("F1") + " up; wind on it " + Vector3d.Dot(wind, e).ToString("F1") + " east, " + Vector3d.Dot(wind, n).ToString("F1") + " north" + (NoPush ? " (not pushing: test)" : "");
        }

        /// <summary>(For the rendering research: how far the world's terrain is drawn from here, as the game decides it. Each
        /// field named is read from the world's terrain sphere, private or not; and the far body's fade.)</summary>
        public static string Horizon()
        {
            CelestialBody b = FlightGlobals.currentMainBody;
            if (b == null || b.pqsController == null) return "no terrain here";
            PQS q = b.pqsController;
            var sb = new StringBuilder(b.bodyName + " terrain:");
            foreach (string name in new[] { "visRadSeaLevelValue", "visRadAltitudeValue", "visRadAltitudeMax", "visibleRadius", "horizonDistance", "horizonAngle", "visRad",
                                            "radiusMin", "radiusMax", "halfChord", "minDetailDistance", "maxDetailDistance", "minLevel", "maxLevel", "detailAltitudeMax",
                                            "detailAltitudeQuads", "maxQuadLenghtsPerFrame", "visibleAltitude", "targetHeight", "targetSpeed", "radius" })
            {
                var f = typeof(PQS).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var pr = f == null ? typeof(PQS).GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) : null;
                object v = f != null ? f.GetValue(q) : pr != null ? pr.GetValue(q, null) : "(none)";
                if (v is Array arr) { var parts = new List<string>(); foreach (object o in arr) parts.Add(o.ToString()); v = "[" + string.Join(", ", parts.ToArray()) + "]"; }
                sb.Append(' ').Append(name).Append('=').Append(v);
            }
            var thresholds = typeof(PQS).GetField("subdivisionThresholds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (thresholds != null && thresholds.GetValue(q) is Array th) { var parts = new List<string>(); foreach (object o in th) parts.Add(Convert.ToDouble(o).ToString("0")); sb.Append(" subdivisionThresholds=[").Append(string.Join(", ", parts.ToArray())).Append(']'); }
            ScaledSpaceFader fader = b.scaledBody != null ? b.scaledBody.GetComponentInChildren<ScaledSpaceFader>() : null;
            if (fader != null) sb.Append("; far body fadeStart=").Append(fader.fadeStart).Append(" fadeEnd=").Append(fader.fadeEnd);
            Vessel v2 = FlightGlobals.ActiveVessel;
            if (v2 != null) sb.Append("; the craft ").Append(v2.altitude.ToString("F0")).Append(" m up");
            return sb.ToString();
        }

        /// <summary>(How deep the world's weather can reach: the game's own temperature by height, at the equator, 30 and 60
        /// degrees, the day's average as World reads it, and the first height where it stops falling by 2 K a kilometre over
        /// the next two (the tropopause as the meteorologists take it). "Kerbin" or nothing for the world below.)</summary>
        public static string Air(string arg)
        {
            CelestialBody b = FlightGlobals.currentMainBody;
            if (!string.IsNullOrEmpty(arg)) foreach (CelestialBody c in FlightGlobals.Bodies) if (c.bodyName == arg.Trim()) b = c;
            if (b == null || !b.atmosphere) return "no air here";
            var sb = new StringBuilder(b.bodyName + ", air " + (b.atmosphereDepth / 1000.0).ToString("F0") + " km deep; K by km up:");
            foreach (double lat in new[] { 0.0, 30.0, 60.0 })
            {
                Func<double, double> at = alt => b.GetTemperature(alt) + b.atmosphereTemperatureSunMultCurve.Evaluate((float)alt) * (b.latitudeTemperatureBiasCurve.Evaluate((float)lat) + b.latitudeTemperatureSunMultCurve.Evaluate((float)lat) * 0.5);
                sb.Append("\n  ").Append(lat.ToString("F0")).Append(" deg:");
                for (int km = 0; km <= 30; km += 2) sb.Append(' ').Append(at(km * 1000.0).ToString("F0"));
                double pause = -1;
                for (double h = 1000; h < Math.Min(b.atmosphereDepth, 40000); h += 250)
                    if ((at(h) - at(h + 2000)) / 2.0 < 2.0) { pause = h; break; }
                sb.Append("; tropopause ").Append(pause < 0 ? "none under 40 km" : (pause / 1000.0).ToString("F2") + " km");
            }
            return sb.ToString();
        }

        /// <summary>(The weather's cloud over the whole world, to set against Earth's (the satellites' climatology: some two
        /// thirds of the world under cloud; low cloud most of it, stratocumulus the most common kind): for the world below,
        /// over a grid of places weighted by the area each stands for, at the hours given ("0,6,12,18" by default, hours on
        /// from now), the share of the world under cloud, under each kind of low cloud, under middle and high cloud and
        /// overcast, and how deep the low cloud is.)</summary>
        public static string Climate(string arg)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null || !body.atmosphere) return "no air here";
            World w = World.Of(body);
            double now = Planetarium.GetUniversalTime();
            var hours = new List<double>();
            foreach (string s in (string.IsNullOrEmpty(arg) ? "0,6,12,18" : arg).Split(',')) hours.Add(double.Parse(s.Trim()));
            int kinds = Enum.GetValues(typeof(CloudKind)).Length;
            // [0] the sea, [1] the land: area; under any cloud; clear (under less than a twentieth); overcast; each kind's
            // amount (cover times area), low kinds by the low layer, Ac and As by the middle, Ci by the high; the low kinds'
            // bases (cover-weighted sums)
            var area = new double[2]; var any = new double[2]; var clear = new double[2]; var overcast = new double[2];
            var amount = new double[2, 16]; var baseSum = new double[2, 16];
            var depths = new List<KeyValuePair<float, double>>();
            var t = Stopwatch.StartNew();
            foreach (double h in hours)
                for (double la = -88; la < 90; la += 4)
                    for (double lo = -178; lo < 180; lo += 4)
                    {
                        double a = Math.Cos(la * Math.PI / 180.0);
                        int land = w.Land(la, lo) > 0.5f ? 1 : 0;
                        WeatherHere x = Model.Layers(w, la, lo, now + h * 3600.0);
                        float l = x.Low.Kind == CloudKind.None ? 0f : x.Low.Cover, m = x.Middle.Kind == CloudKind.None ? 0f : x.Middle.Cover, g = x.High.Kind == CloudKind.None ? 0f : x.High.Cover;
                        double total = 1.0 - (1.0 - l) * (1.0 - m) * (1.0 - g);
                        area[land] += a; any[land] += a * total;
                        if (total < 0.05) clear[land] += a;
                        if (Math.Max(l, Math.Max(m, g)) > 0.7f) overcast[land] += a;
                        amount[land, (int)x.Low.Kind] += a * l; baseSum[land, (int)x.Low.Kind] += a * l * (x.Low.Base - Math.Max(0f, w.Ground(la, lo)));
                        amount[land, (int)x.Middle.Kind] += a * m;
                        amount[land, (int)x.High.Kind] += a * g;
                        if (l > 0.05f && x.Low.Kind != CloudKind.Fog) depths.Add(new KeyValuePair<float, double>(x.Low.Top - x.Low.Base, a * l));
                    }
            var sb = new StringBuilder(body.bodyName + ", " + hours.Count + " hours, " + t.ElapsedMilliseconds + " ms; the land " + (area[1] / (area[0] + area[1])).ToString("P0") + " of the world");
            for (int land = 0; land < 2; land++)
            {
                double A = Math.Max(area[land], 1e-9);
                sb.Append("\n  ").Append(land == 0 ? "sea: " : "land: ").Append("under cloud ").Append((any[land] / A).ToString("P0")).Append(", clear ").Append((clear[land] / A).ToString("P0"))
                  .Append(", overcast ").Append((overcast[land] / A).ToString("P0")).Append(";");
                for (int k = 1; k < kinds; k++)
                {
                    if (amount[land, k] <= 0) continue;
                    sb.Append(' ').Append(((CloudKind)k).ToString()).Append(' ').Append((amount[land, k] / A).ToString("P1"));
                    if (baseSum[land, k] > 0) sb.Append(" (base ").Append((baseSum[land, k] / amount[land, k]).ToString("F0")).Append(" m)");
                }
            }
            depths.Sort((p, q) => p.Key.CompareTo(q.Key));
            double all = 0; foreach (var p in depths) all += p.Value;
            sb.Append("\n  low cloud's depth (by cover, fog left out):");
            foreach (double share in new[] { 0.1, 0.25, 0.5, 0.75, 0.9, 0.98 })
            {
                double run = 0; float at = 0f;
                foreach (var p in depths) { run += p.Value; at = p.Key; if (run >= share * all) break; }
                sb.Append(' ').Append((share * 100).ToString("F0")).Append("% ").Append(at.ToString("F0")).Append(" m");
            }
            sb.Append("\n  (Earth, by observers (Warren, Eastman and Hahn 2014): sea 69 % under cloud, clear 3 %: fog 1, St 13, Sc 22, Cu 13, Cb 6, Ns 5, As 6, Ac 18, high 12;"
                    + " land 54 %, clear 22 %: fog 1, St 5, Sc 12, Cu 5, Cb 4, Ns 5, As 4, Ac 17, high 22; bases over the sea St 400, Sc 600, Cu 600, Cb 500 m, over land 500, 1000, 1100, 1000 m)");
            return sb.ToString();
        }

        /// <summary>(For setting one against the other: "old" makes the cover as until 2026-10-19, without the storms of the middle latitudes, the equator's clusters and the decks off west coasts; "new" as now.)</summary>
        public static string Cover(string arg) { Model.OldCover = arg != null && arg.Trim() == "old"; Model.Forget(); return Model.OldCover ? "the cover as until 2026-10-19" : "the cover with the storms, the clusters and the decks"; }

        /// <summary>(For setting one against the other: "old" leaves out the usual sky's own cloud, as until 2026-10-18; "new" puts it back.)</summary>
        public static string Usual(string arg) { Model.TestUsual = arg != null && arg.Trim() == "old" ? 1f : 0f; Model.Forget(); return Model.TestUsual == 1f ? "the usual sky left out (as until 2026-10-18)" : "the usual sky in"; }

        public static string Choose(string arg)
        {
            int n = int.Parse(arg.Trim());
            Hold = n;
            Model.Chosen = Math.Max(0, n); Model.ChosenWeight = n > 0 ? 1f : 0f;
            Model.Forget(); Launch.Again();
            return n >= 0 ? "held: " + Model.Choices[Math.Max(0, n)] : "let go";
        }

        public static string Here()
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl)) return "nowhere";
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            WeatherHere h = Model.Here(w, lat, lon, ut);
            Column col = Model.ColumnAt(w, lat, lon, ut);
            var b = new StringBuilder();
            b.Append(body.bodyName).Append(" ").Append(lat.ToString("F3")).Append(", ").Append(lon.ToString("F3")).Append(" at ").Append(alt.ToString("F0")).Append(" m (").Append(agl.ToString("F0")).Append(" above ground; the map says ground ").Append(col.B.Ground.ToString("F0")).Append(w.Grounded ? ")" : "; the world has no ground under its air)").Append("\n");
            b.Append(Panel.Words(h)).Append("\n");
            foreach (var pair in new[] { ("low", h.Low), ("middle", h.Middle), ("high", h.High) })
                b.Append(pair.Item1).Append(": ").Append(pair.Item2.Kind).Append(" ").Append(pair.Item2.Cover.ToString("F2")).Append(" ").Append(pair.Item2.Base.ToString("F0")).Append("-").Append(pair.Item2.Top.ToString("F0")).Append(" water ").Append(pair.Item2.Water.ToString("F2")).Append("\n");
            b.Append("rain ").Append(h.Rain.ToString("F2")).Append(" snow ").Append(h.Snow.ToString("F2")).Append(" storm ").Append(h.Storm.ToString("F2")).Append(" fog ").Append(h.Fog.ToString("F2")).Append(" dust ").Append(h.Dust.ToString("F2")).Append(" seeing ").Append(h.Seeing.ToString("F0")).Append(" T ").Append((h.Temperature - 273.15f).ToString("F1")).Append(" C\n");
            b.Append("fields: M ").Append(col.B.M.ToString("F2")).Append(" inst ").Append(col.B.Inst.ToString("F2")).Append(" front ").Append(col.B.Front.ToString("F2")).Append(" land ").Append(col.B.Land.ToString("F2")).Append(" sun ").Append(col.B.SunUp.ToString("F2")).Append(" hour ").Append(col.B.Hour.ToString("F0")).Append(" freeze ").Append(col.B.Freeze.ToString("F0")).Append(" drift ").Append(col.B.DriftE.ToString("F1")).Append("\n");
            b.Append("wind 10 m: ").Append(col.UE.ToString("F1")).Append(" E ").Append(col.UN.ToString("F1")).Append(" N; gusts ").Append(h.Gusts.ToString("F2")).Append("; boundary layer ").Append(col.Hbl.ToString("F0")).Append(" m, alpha ").Append(col.Alpha.ToString("F2")).Append("; jet ").Append(col.JE.ToString("F0")).Append(" E ").Append(col.JN.ToString("F0")).Append(" N at ").Append(col.JetH.ToString("F0")).Append("\n");
            foreach (float hh in new[] { 2f, 10f, 50f, 200f, 1000f, 3000f, 8000f, 12000f })
            {
                Vector3 m = Model.Mean(col, col.B.Ground + hh, hh);
                Vector3 g = Gusts.At(col, lat, lon, col.B.Ground + hh, hh, ut);
                b.Append("  at ").Append(hh.ToString("F0")).Append(" m: mean ").Append(new Vector2(m.x, m.y).magnitude.ToString("F1")).Append(" gust now ").Append(g.ToString("F1")).Append(" sigma ").Append(Model.Sigmas(col, col.B.Ground + hh, hh, out float sw, out float lu, out float lw).ToString("F2")).Append("/").Append(sw.ToString("F2")).Append(" L ").Append(lu.ToString("F0")).Append("/").Append(lw.ToString("F0")).Append("\n");
            }
            b.Append("cells within 25 km: ").Append(col.Cells.Count);
            foreach (WeatherCell c in col.Cells) b.Append("\n  ").Append(c.Thunder ? "storm" : "shower").Append(" ").Append((Model.Distance(w, lat, lon, c.Latitude, c.Longitude) / 1000.0).ToString("F1")).Append(" km, r ").Append((c.Radius / 1000f).ToString("F1")).Append(" km, strength ").Append(c.Strength.ToString("F2")).Append(" life ").Append(c.Life.ToString("F2")).Append(" top ").Append(c.Top.ToString("F0"));
            WeatherShade s = Provider.Last;
            b.Append("\nshade: sun ").Append(s.Sun.ToString("F2")).Append(" sky ").Append(s.Sky.ToString("F2")).Append(" grey ").Append(s.Grey.ToString("F2"));
            b.Append("\nlaunch: ").Append(Launch.Now.Go ? "GO" : "NO GO (" + Launch.Now.First + ")");
            foreach (Launch.Rule r in Launch.Now.Rules) b.Append("\n  ").Append(r.Go ? "go    " : "no go ").Append(r.Name).Append(": ").Append(r.Value);
            b.Append("\n").Append(Aero.Report).Append(" ").Append(Feed.Report).Append(" ").Append(Rain.Report).Append(" ").Append(Lightning.Report);
            return b.ToString();
        }

        /// <summary>A map of the clouds about the place: 64 by 24 characters, `arg` km each way (60 by default), at the low cloud's middle.</summary>
        /// <summary>
        /// (Development build) What makes the low cloud down a line of longitude on the home world: "lon lat0 lat1 steps". For each
        /// place: the lowest air's stability and moisture, land, the usual sky's deck and its makings (how subtropical, how far land
        /// lies to the east against how far a deck reaches, how freely heaps grow), and the low and middle cloud as drawn.
        /// </summary>
        public static string Meridian(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 4) return "lon lat0 lat1 steps";
            double D(int i) => double.Parse(t[i], System.Globalization.CultureInfo.InvariantCulture);
            CelestialBody body = FlightGlobals.GetHomeBody();
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime(), lon = D(0);
            int steps = Math.Max(2, (int)D(3));
            var said = new StringBuilder("lat: stab moist land | deck offWest subtropic east/reach km lifted | low kind cover base-top | middle cover\n");
            for (int i = 0; i < steps; i++)
            {
                double lat = D(1) + (D(2) - D(1)) * i / (steps - 1);
                Base b = Model.Sample(w, lat, lon, ut);
                Model.Probing = true;
                WeatherHere h = Model.Layers(w, lat, lon, ut);
                Model.Probing = false;
                said.Append(lat.ToString("F1")).Append(": ").Append(b.Stab.ToString("F2")).Append(' ').Append(b.M.ToString("F2")).Append(' ').Append(b.Land.ToString("F1"))
                    .Append(" | ").Append(Model.PDeck.ToString("F2")).Append(' ').Append(Model.POffWest.ToString("F2")).Append(' ').Append(Model.PSubtropic.ToString("F2")).Append(' ')
                    .Append((Model.PEast / 1000f).ToString("F0")).Append('/').Append((Model.PReach / 1000f).ToString("F0")).Append(' ').Append(Model.PLifted.ToString("F2"))
                    .Append(" | ").Append(h.Low.Kind).Append(' ').Append(h.Low.Cover.ToString("F2")).Append(' ').Append(h.Low.Base.ToString("F0")).Append('-').Append(h.Low.Top.ToString("F0"))
                    .Append(" | ").Append(h.Middle.Cover.ToString("F2")).Append('\n');
            }
            return said.ToString();
        }

        public static string Map(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out _, out _)) return "nowhere";
            double reach = 60000.0;
            if (!string.IsNullOrEmpty(arg)) reach = double.Parse(arg) * 1000.0;
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            const string shades = " .:-=+*#%@";
            var b = new StringBuilder();
            var t = Stopwatch.StartNew();
            for (int y = 11; y >= -12; y--)
            {
                for (int x = -32; x < 32; x++)
                {
                    double dn = y / 12.0 * reach, de = x / 32.0 * reach;
                    double la = lat + dn / w.Radius * 180.0 / Math.PI, lo = lon + de / (w.Radius * Math.Cos(lat * Math.PI / 180.0)) * 180.0 / Math.PI;
                    WeatherHere h = Model.Layers(w, la, lo, ut);
                    double mid = h.Low.Cover > 0.02f ? (h.Low.Base + h.Low.Top) * 0.5 : h.Middle.Cover > 0.02f ? (h.Middle.Base + h.Middle.Top) * 0.5 : 3000.0;
                    float c = Provider.CloudAt(w, la, lo, mid, ut);
                    char ch = x == 0 && y == 0 ? 'X' : h.Rain > 0.1f ? (h.Snow > 0.5f ? '~' : 'r') : shades[Math.Min(9, (int)(c * 9.99f))];
                    b.Append(ch);
                }
                b.Append('\n');
            }
            b.Append("(" + (t.ElapsedMilliseconds) + " ms for 1536 places; r = rain, ~ = snow, X = here)");
            return b.ToString();
        }

        public static string Timing()
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl)) return "nowhere";
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            var b = new StringBuilder();
            var t = Stopwatch.StartNew();
            for (int n = 0; n < 200; n++) Model.Sample(w, lat + n * 0.01, lon, ut);
            b.Append("Sample ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 200).ToString("F1")).Append(" us; ");
            t.Restart();
            for (int n = 0; n < 200; n++) Model.Layers(w, lat + n * 0.01, lon, ut);
            b.Append("Layers ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 200).ToString("F1")).Append(" us; ");
            t.Restart();
            for (int n = 0; n < 200; n++) Provider.CloudAt(w, lat + n * 0.01, lon, 1500.0, ut);
            b.Append("Cloud ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 200).ToString("F1")).Append(" us; ");
            Column col = Model.ColumnAt(w, lat, lon, ut);
            t.Restart();
            for (int n = 0; n < 1000; n++) Gusts.At(col, lat, lon, alt + n * 0.1, agl + n * 0.1f, ut + n * 0.02);
            b.Append("Gusts ").Append((t.Elapsed.TotalMilliseconds * 1000.0 / 1000).ToString("F1")).Append(" us; ");
            t.Restart();
            for (int n = 0; n < 20; n++) { var list = new System.Collections.Generic.List<WeatherCell>(); Model.Cells(w, lat + n * 0.2, lon, 25000.0, ut, list); }
            b.Append("Cells(25 km) ").Append((t.Elapsed.TotalMilliseconds / 20).ToString("F2")).Append(" ms; ");
            Model.Forget();
            t.Restart();
            for (int n = 0; n < 20; n++) Model.ColumnAt(w, lat + n * 0.2, lon, ut);
            b.Append("a new column ").Append((t.Elapsed.TotalMilliseconds / 20).ToString("F2")).Append(" ms");
            return b.ToString();
        }

        /// <summary>
        /// (For the development build: the model's atlas of the world against the weather asked place by place, at so many
        /// places all over the world (4000 by default) and at as many within 300 km of the craft: how many answers differ in
        /// their clouds, rain or storm, and what each way costs.)
        /// </summary>
        public static string Atlas(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat0, out double lon0, out _, out _)) return "nowhere";
            int count = string.IsNullOrEmpty(arg) ? 4000 : int.Parse(arg);
            World w = World.Of(body);
            double ut = Math.Floor(Planetarium.GetUniversalTime() / 2.0) * 2.0;
            var t = Stopwatch.StartNew();
            var atlas = new Model.Atlas(w, ut);
            double made = t.Elapsed.TotalMilliseconds;
            var rng = new System.Random(12345);
            var places = new double[count * 2][];
            double near = 300000.0 / (w.Radius * Math.PI / 180.0);
            for (int n = 0; n < places.Length; n++)
            {
                double la = n < count ? Math.Asin(rng.NextDouble() * 2.0 - 1.0) * 180.0 / Math.PI : lat0 + (rng.NextDouble() * 2.0 - 1.0) * near;
                double lo = n < count ? rng.NextDouble() * 360.0 - 180.0 : lon0 + (rng.NextDouble() * 2.0 - 1.0) * near;
                places[n] = new[] { Math.Max(-89.9, Math.Min(89.9, Math.Round(la * 20.0) / 20.0)), Math.Round(lo * 20.0) / 20.0 };
            }
            var mine = new WeatherHere[places.Length];
            t.Restart();
            for (int n = 0; n < places.Length; n++) mine[n] = atlas.At(places[n][0], places[n][1]);
            double fast = t.Elapsed.TotalMilliseconds * 1000.0 / places.Length;
            Model.Forget();
            t.Restart();
            int differ = 0, stormy = 0;
            string first = "";
            for (int n = 0; n < places.Length; n++)
            {
                WeatherHere a = mine[n], b = Model.Here(w, places[n][0], places[n][1], ut);
                if (b.Storm > 0.01f || b.Low.Kind == CloudKind.Cumulonimbus) stormy++;
                bool same = Same(a.Low, b.Low) && Same(a.Middle, b.Middle) && Same(a.High, b.High) && Math.Abs(a.Rain - b.Rain) < 0.004f && Math.Abs(a.Storm - b.Storm) < 0.03f;
                if (same) continue;
                differ++;
                if (first.Length == 0)
                    first = " (the first at " + places[n][0].ToString("F2") + ", " + places[n][1].ToString("F2") + ": low " + a.Low.Kind + " " + a.Low.Cover.ToString("F3") + " " + a.Low.Base.ToString("F0") + "-" + a.Low.Top.ToString("F0") + " against " + b.Low.Kind + " " + b.Low.Cover.ToString("F3") + " " + b.Low.Base.ToString("F0") + "-" + b.Low.Top.ToString("F0")
                            + ", rain " + a.Rain.ToString("F3") + " against " + b.Rain.ToString("F3") + ", storm " + a.Storm.ToString("F3") + " against " + b.Storm.ToString("F3") + ")";
            }
            double slow = t.Elapsed.TotalMilliseconds * 1000.0 / places.Length;
            return body.bodyName + ": the atlas made in " + made.ToString("F1") + " ms, " + (atlas.Storms < 0 ? "its storms not counted (each place finds its own)" : atlas.Storms + " storms and showers on the world") + "; " + places.Length + " places (half all over, half within 300 km): "
                + differ + " differ" + first + "; " + stormy + " of them have a storm near; a place costs " + fast.ToString("F1") + " us by the atlas, " + slow.ToString("F1") + " us asked by itself; so a map of 32,768 places " + ((made + fast * 32.768) / 1000.0).ToString("F2") + " s against " + (slow * 32.768 / 1000.0).ToString("F2") + " s";
        }

        static bool Same(CloudLayer a, CloudLayer b) =>
            a.Kind == b.Kind && Math.Abs(a.Cover - b.Cover) < 0.004f && (a.Cover < 0.01f || Math.Abs(a.Base - b.Base) < 32f && Math.Abs(a.Top - b.Top) < 64f && Math.Abs(a.Water - b.Water) < 0.004f);

        public static string City(string path) => NaturalWeather.City.FromFile(path);

        /// <summary>A city as if typed on the page (empty: forget it). Its weather is fetched from online within a moment.</summary>
        public static string CityName(string name) { NaturalWeather.City.Set(name ?? ""); return "city: '" + NaturalWeather.City.Typed + "'"; }

        /// <summary>What the city part says now.</summary>
        public static string CityNow() => NaturalWeather.City.Words + " | used: " + (Overrides.City != null ? Overrides.City.Words() : "none");

        /// <summary>A lightning strike on the craft now (as if the storm had found it).</summary>
        public static string Strike()
        {
            Lightning me = UnityEngine.Object.FindObjectOfType<Lightning>();
            if (me == null) return "not running";
            return me.StrikeNow();
        }

        /// <summary>The weather at the place every half hour of game time for `arg` Kerbin days (4 by default): to see how often each kind comes.</summary>
        public static string Days(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out _, out _)) return "nowhere";
            int days = string.IsNullOrEmpty(arg) ? 4 : int.Parse(arg);
            World w = World.Of(body);
            double ut0 = Planetarium.GetUniversalTime();
            var b = new StringBuilder();
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            int slots = (int)(days * w.Day / 1800.0), go = 0, rainy = 0, stormy = 0;
            for (int k = 0; k < slots; k++)
            {
                double ut = ut0 + k * 1800.0;
                WeatherHere h = Model.Here(w, lat, lon, ut);
                Launch.Verdict v = Launch.Judge(w, lat, lon, ut);
                string words = Panel.Words(h);
                string key = words.Split(',')[0];
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                if (v.Go) go++;
                if (h.Rain > 0.05f) rainy++;
                if (h.Storm > 0.2f) stormy++;
                Column col = Model.ColumnAt(w, lat, lon, ut);
                if (k < 48) b.Append((k * 0.5).ToString("F1")).Append("h ").Append(words).Append(", wind ").Append(new Vector2(col.UE, col.UN).magnitude.ToString("F1")).Append(", M ").Append(col.B.M.ToString("F2")).Append(" inst ").Append(col.B.Inst.ToString("F2")).Append(v.Go ? " GO" : " no go: " + v.First).Append("\n");
            }
            b.Append("over ").Append(days).Append(" days (").Append(slots).Append(" half hours): ");
            foreach (var kv in counts) b.Append(kv.Key).Append(" ").Append(kv.Value * 100 / slots).Append("%, ");
            b.Append("rain ").Append(rainy * 100 / slots).Append("%, storms near ").Append(stormy * 100 / slots).Append("%, launch go ").Append(go * 100 / slots).Append("%");
            return b.ToString();
        }

        /// <summary>
        /// (For the development build: how windy it is near the ground where the craft is, over so many days from now, every ten minutes
        /// of game time, the weather as it comes: the share of the time the mean wind 2 m and 10 m up is calm (under 0.5 m/s), light (to
        /// 2), gentle (to 5), moderate (to 10) or strong; and 2 m up by night and by day, and with rain or storms about.)
        /// </summary>
        public static string Winds(string arg)
        {
            if (!WeatherDriver.Place(out CelestialBody body, out double lat, out double lon, out _, out _)) return "nowhere";
            int days = string.IsNullOrEmpty(arg) ? 8 : int.Parse(arg);
            World w = World.Of(body);
            double ut0 = Planetarium.GetUniversalTime();
            int slots = (int)(days * w.Day / 600.0);
            float[] edges = { 0.5f, 2f, 5f, 10f };
            int[] at2 = new int[5], at10 = new int[5], night2 = new int[5], day2 = new int[5], wet2 = new int[5];
            int nights = 0, daysN = 0, wets = 0;
            double sum2 = 0, sum10 = 0, sumGust = 0, most2 = 0;
            Func<float, int> bin = v => { int k = 0; while (k < edges.Length && v >= edges[k]) k++; return k; };
            for (int k = 0; k < slots; k++)
            {
                double ut = ut0 + k * 600.0;
                Column col = Model.ColumnAt(w, lat, lon, ut);
                float g = col.B.Ground;
                float v2 = Model.Mean(col, g + 2.0, 2f).magnitude, v10 = Model.Mean(col, g + 10.0, 10f).magnitude;
                at2[bin(v2)]++; at10[bin(v10)]++;
                sum2 += v2; sum10 += v10; sumGust += col.Here.Gusts; most2 = Math.Max(most2, v2);
                if (col.B.SunUp > 0f) { day2[bin(v2)]++; daysN++; } else { night2[bin(v2)]++; nights++; }
                if (col.Here.Rain > 0.05f || col.Here.Storm > 0.2f) { wet2[bin(v2)]++; wets++; }
            }
            Func<int[], int, string> share = (c, n) =>
            {
                if (n == 0) return "none";
                var s = new StringBuilder();
                string[] names = { "calm", "light", "gentle", "moderate", "strong" };
                for (int i = 0; i < 5; i++) s.Append(names[i]).Append(" ").Append((100.0 * c[i] / n).ToString("F1")).Append("% ");
                return s.ToString().TrimEnd();
            };
            return "over " + days + " days (" + slots + " times, ten minutes apart)" + (Model.OldWind ? ", the wind near the ground as before 0.2.0" : "")
                + ": 2 m up " + share(at2, slots) + "; 10 m up " + share(at10, slots) + "; 2 m up by night " + share(night2, nights) + "; by day " + share(day2, daysN)
                + "; with rain or storms about (" + (100.0 * wets / slots).ToString("F0") + "% of the time) " + share(wet2, wets)
                + "; mean 2 m " + (sum2 / slots).ToString("F2") + " m/s, 10 m " + (sum10 / slots).ToString("F2") + ", most at 2 m " + most2.ToString("F1") + "; gusts (1 sigma at 10 m) " + (sumGust / slots).ToString("F2")
                + " (knobs: night " + Model.NightNear + ", fair day " + Model.FairDayNear + ", prevailing " + Model.Prevail + ", sea breeze " + Model.Breeze + ")";
        }

        /// <summary>(For the development build: "night fairday prevailing breeze" for the wind near the ground, or "old" for it as before 0.2.0, or "new".)</summary>
        public static string WindKnobs(string arg)
        {
            string[] p = (arg ?? "").Trim().Split(' ');
            if (p[0] == "old") Model.OldWind = true;
            else if (p[0] == "new") Model.OldWind = false;
            else
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (p.Length > 0 && p[0].Length > 0) Model.NightNear = float.Parse(p[0], inv);
                if (p.Length > 1) Model.FairDayNear = float.Parse(p[1], inv);
                if (p.Length > 2) Model.Prevail = float.Parse(p[2], inv);
                if (p.Length > 3) Model.Breeze = float.Parse(p[3], inv);
                Model.OldWind = false;
            }
            Model.Forget();
            return (Model.OldWind ? "as before 0.2.0" : "night " + Model.NightNear + ", fair day " + Model.FairDayNear + ", prevailing " + Model.Prevail + ", sea breeze " + Model.Breeze);
        }

        /// <summary>The root part's airspeed as the game's drag sums have it now, against the craft's ground speed and the wind on it: the two must differ by the wind.</summary>
        public static string Drag()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            Part p = v.rootPart;
            Vector3 wind = Aero.OnCraft(v), ground = (Vector3)v.srf_velocity;
            return "dragVector " + p.dragVector.ToString("F2") + " (" + p.dragVector.magnitude.ToString("F2") + " m/s); ground speed " + ground.ToString("F2") + "; wind on the craft " + wind.ToString("F2") + "; ground less wind " + (ground - wind).ToString("F2")
                + "; dynamic pressure " + p.dynamicPressurekPa.ToString("F4") + " kPa, drag " + p.dragScalar.ToString("F3") + " kN";
        }

        public static string Wind()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            Vector3 on = Aero.OnCraft(v);
            Provider.Axes(v.mainBody, v.CoMD, out Vector3d e, out Vector3d n, out Vector3d u);
            return "wind on " + v.GetDisplayName() + ": " + on.magnitude.ToString("F2") + " m/s (east " + Vector3d.Dot(on, e).ToString("F2") + ", north " + Vector3d.Dot(on, n).ToString("F2") + ", up " + Vector3d.Dot(on, u).ToString("F2") + "); ground speed " + v.srfSpeed.ToString("F2") + " m/s, "
                + (v.LandedOrSplashed ? "landed" : "flying") + "; " + Aero.Report;
        }
    }
}
#endif
