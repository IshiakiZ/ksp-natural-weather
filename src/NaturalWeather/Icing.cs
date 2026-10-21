using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// Ice on craft: felt, not drawn.
    ///
    /// What was read first (the FAA's and the Met Office's guidance on airframe icing, and the physics of accretion): ice
    /// forms only from water that is liquid though colder than freezing, which cloud holds from 0 down to about -20 degrees
    /// (then less and less, as its drops freeze, and none by -40). Heaped cloud (cumulus, the towers of a storm) holds the
    /// most water in the largest drops: ice builds fast, clear and heavy. Layer cloud holds less in smaller drops: slower,
    /// milky rime. Freezing rain, rain falling through air colder than freezing below a warm layer, is the worst of all: big
    /// drops that run back before they freeze. Freezing fog rimes whatever stands in it, slowly. A surface collects so much
    /// of the water it flies through (the cloud's water, times its speed, times how much of what meets it its shape catches,
    /// over the density of ice): in a cloud of half a gram a cubic metre at 80 m/s, a centimetre in seven or eight minutes
    /// ("severe"). Speed warms the skin where the air meets it (about 0.85 of v^2 / 2c_p: four degrees at 100 m/s, seventeen at
    /// 200), so a fast craft stays clear; engines, sunshine and re-entry warm it as the game's own heating says. Warmer air
    /// melts it off; very dry cold air wears it slowly away.
    ///
    /// What it does: a centimetre or two of ice on a wing takes a third or more of its lift and doubles its drag, the
    /// craft is heavier by the ice, and every part drags more. Here, with the game's own aerodynamics (with Ferram's, only the
    /// weight): the wings' lift less by up to four tenths, their drag more by up to twice, every part's drag up by up to
    /// six tenths, and the ice's weight; the worse, the thicker it is, up to fifteen millimetres. For every craft about.
    /// The one being flown is said in the weather's window and to the AI, and a word on the screen when it begins, gets
    /// worse, and is gone.
    /// </summary>
    public sealed class Icing : MonoBehaviour
    {
        sealed class Iced
        {
            public float Mm;               // the thickness of ice on its leading faces, mm
            public float Rate;             // how fast it builds now, mm/s (less than nought: melting)
            public float Skin;             // the leading faces' temperature, K
            public float Air;              // the air's, K
            public string Kind = "";       // what it comes from
            public float LookedAt = -1f;
            public int Said;               // the worst it has been said to be (0 none, 1 light, 2 moderate, 3 severe)
        }

        readonly Dictionary<Guid, Iced> craft = new Dictionary<Guid, Iced>();
        readonly List<Guid> gone = new List<Guid>();
        static bool far;
        /// <summary>The craft being flown: how thick its ice is (mm), whether it is building, and a line for the weather's window.</summary>
        public static float Now;
        public static bool Building;
        public static string Report = "";
#if DEV
        /// <summary>(development build) the craft being flown held at so many millimetres of ice; less than nought lets go.</summary>
        public static float Held = -1f;
        public static string Hold(string arg)
        {
            Held = float.TryParse((arg ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1f;
            return Held < 0f ? "ice as the weather makes it" : "held at " + Held + " mm";
        }
        public static string Says() => Report.Length > 0 ? Report + " " + said : "no ice; " + said;
        static string said = "";
#endif

        void Start()
        {
            // (Ferram's aerodynamics do the lift and drag themselves: there only the weight is felt)
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "FerramAerospaceResearch") far = true;
        }

        void FixedUpdate()
        {
            bool on = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && Weather.On != null && Weather.On && Weather.Icing != null && Weather.Icing;
            if (!on) { craft.Clear(); Now = 0f; Building = false; Report = ""; return; }
            float dt = TimeWarp.fixedDeltaTime;
            double ut = Planetarium.GetUniversalTime();
            List<Vessel> loaded = FlightGlobals.VesselsLoaded;
            for (int i = 0; i < loaded.Count; i++)
            {
                Vessel v = loaded[i];
                if (v == null || v.mainBody == null) continue;
                if (!craft.TryGetValue(v.id, out Iced ice)) { ice = new Iced(); craft[v.id] = ice; }
                bool air = v.mainBody.atmosphere && Model.Allowed(v.mainBody) && v.altitude < v.mainBody.atmosphereDepth;
                if (!air) { ice.Mm = Mathf.Max(0f, ice.Mm - 0.05f * dt); ice.Rate = 0f; continue; }
                // (the weather where it is, twice a second: it does not change faster than that)
                if (Time.time - ice.LookedAt > 0.5f || ice.LookedAt < 0f) { ice.LookedAt = Time.time; Look(v, ice, ut); }
                ice.Mm = Mathf.Clamp(ice.Mm + ice.Rate * dt, 0f, 40f);
#if DEV
                if (Held >= 0f && v == FlightGlobals.ActiveVessel) ice.Mm = Held;
#endif
                if (!v.packed && ice.Mm > 0.2f) Feel(v, ice.Mm);
            }
            // (craft no longer about)
            if (Time.frameCount % 300 == 0)
            {
                gone.Clear();
                foreach (KeyValuePair<Guid, Iced> kv in craft) if (FlightGlobals.FindVessel(kv.Key) == null || !FlightGlobals.FindVessel(kv.Key).loaded) gone.Add(kv.Key);
                foreach (Guid g in gone) craft.Remove(g);
            }
            Tell();
        }

        /// <summary>How fast ice builds on a craft now, or melts: from the cloud, rain and fog it is in, the cold, and its own warmth.</summary>
        void Look(Vessel v, Iced ice, double ut)
        {
            CelestialBody body = v.mainBody;
            World w = World.Of(body);
            WeatherHere h = Model.Here(w, v.latitude, v.longitude, ut);
            double alt = v.altitude;
            double ground = Math.Max(body.ocean ? 0.0 : double.MinValue, v.terrainAltitude);
            float above = (float)Math.Max(0.0, alt - ground);
            // ---- the air's temperature at the craft: the weather's at the ground, less six and a half degrees a kilometre
            float tAir = h.Temperature > 1f ? h.Temperature - 0.0065f * above : (float)v.atmosphericTemperature;
            // ---- how much water there is, as against a cloud of half a gram a cubic metre (1)
            float water = 0f;
            string kind = "";
            float layer = CloudWater(h.Low, alt);
            if (layer > water) { water = layer; kind = h.Low.Kind == CloudKind.Cumulus || h.Low.Kind == CloudKind.Cumulonimbus ? "heaped cloud" : "layer cloud"; }
            layer = CloudWater(h.Middle, alt);
            if (layer > water) { water = layer; kind = "middle cloud"; }
            // (the towers of storms and showers)
            // (Since 0.5.0 the region's cells as the model keeps them, made ahead (see Model.RegionCells): worked out afresh for the
            // craft every half second, 30 km round, they were 10 ms a time, a hitch twice a second where ice could form (2026-10-20).
            // Only those the craft is inside count, and the region's reach holds every one of them.)
            List<WeatherCell> cells = Model.RegionCells(w, v.latitude, v.longitude, ut);
            for (int k = 0; k < cells.Count; k++)
            {
                WeatherCell c = cells[k];
                if (alt < c.Base || alt > c.Top) continue;
                float d = (float)Model.Distance(w, v.latitude, v.longitude, c.Latitude, c.Longitude);
                float inside = Mathf.Clamp01(1f - d / Mathf.Max(c.Radius, 100f));
                float tower = 1.6f * c.Strength * Mathf.Sqrt(inside);
                if (tower > water) { water = tower; kind = c.Thunder ? "a thunderstorm" : "a shower cloud"; }
            }
            // (freezing fog, for what stands or flies low in it)
            if (h.FogFreezes && h.Fog > 0.05f && above < Mathf.Max(h.FogDeep, 30f))
            {
                float fog = 0.2f * h.Fog;
                if (fog > water) { water = fog; kind = "freezing fog"; }
            }
            // ---- what of it is still liquid: all of it down to -15, less and less to -40, none colder; none above freezing
            float liquid = tAir > 273.15f ? 0f : tAir >= 258f ? 1f : Mathf.Clamp01((tAir - 233f) / 25f);
            // ---- the leading faces' warmth: the air's, and what speed adds where the air meets them, or what the game's
            // own heating has given its skin (engines, sunshine, re-entry), whichever is more
            float speed = (float)Math.Max(v.srfSpeed, 0.0);
            float ram = 0.85f * speed * speed / (2f * 1005f);
            float skin = SkinWarmer(v);
            float tSkin = tAir + Mathf.Max(ram, skin);
            float freezes = Mathf.Clamp01((273.15f - tSkin) / 2f);
            // ---- freezing rain: falls on it standing or flying, and the faster it flies the more it meets
            float rainRate = 50f * h.Rain * h.Freezing;                                   // mm an hour of rain
            float rainOn = 0f;
            if (rainRate > 0.05f && alt < h.Low.Base + 200f)
            {
                float rainWater = 0.072f * Mathf.Pow(rainRate, 0.88f);                      // g/m^3 in the air
                rainOn = 1.09f * rainRate / 3600f * 0.5f + 1.09e-3f * 0.9f * rainWater * speed;
                if (rainOn > 0f && rainRate * 0.02f > water) kind = "freezing rain";
            }
            // ---- the build-up: the water met (half a gram a cubic metre to a cloud of 1, a little over half of it caught by
            // a wing's leading edge), frozen, over the density of ice; mm a second
            float flown = Mathf.Max(speed, (float)new Vector2((float)h.Wind.x, (float)h.Wind.y).magnitude, 1f);
            float build = (1.09e-3f * 0.55f * 0.5f * water * flown + rainOn) * liquid * freezes;
            // ---- and the melting: in air above freezing, faster the warmer; very slowly worn away in dry, cold air
            float melt = tSkin > 273.15f ? 0.05f * Mathf.Clamp01((tSkin - 273.15f) / 4f) + 0.002f * speed / 100f : (water < 0.01f && rainOn <= 0f ? 0.0003f : 0f);
            ice.Rate = build - melt;
            ice.Skin = tSkin; ice.Air = tAir;
            ice.Kind = build > 1e-5f ? kind : "";
#if DEV
            if (v == FlightGlobals.ActiveVessel)
                said = "(air " + (tAir - 273.15f).ToString("F1") + " C, skin " + (tSkin - 273.15f).ToString("F1") + " C, water " + water.ToString("F2") + " of a cloud's half gram, liquid " + liquid.ToString("F2")
                     + ", freezes " + freezes.ToString("F2") + ", freezing rain " + rainRate.ToString("F1") + " mm/h, building " + (build * 60f).ToString("F2") + " mm a minute, melting " + (melt * 60f).ToString("F2") + ")";
#endif
        }

        /// <summary>How much a cloud layer holds where the craft is, against half a gram a cubic metre: heaped cloud most, layer cloud less, none in ice cloud.</summary>
        static float CloudWater(CloudLayer l, double alt)
        {
            if (l.Kind == CloudKind.None || l.Cover < 0.02f || alt < l.Base || alt > l.Top) return 0f;
            switch (l.Kind)
            {
                case CloudKind.Cumulus: return 0.9f * l.Cover * (0.5f + 0.5f * l.Water);
                case CloudKind.Cumulonimbus: return 1.4f * l.Cover;
                case CloudKind.Stratocumulus:
                case CloudKind.Stratus:
                case CloudKind.Nimbostratus: return 0.45f * l.Cover * (0.6f + 0.4f * l.Water);
                case CloudKind.Altocumulus: return 0.35f * l.Cover;
                case CloudKind.Altostratus: return 0.25f * l.Cover;
                default: return 0f;     // (cirrus is ice already; fog is taken apart; dust holds no water)
            }
        }

        /// <summary>How much warmer than the air the craft's skin is by the game's own heating: the middle of its parts' (engines alone do not warm its wings).</summary>
        static float SkinWarmer(Vessel v)
        {
            List<Part> parts = v.parts;
            if (parts == null || parts.Count == 0) return 0f;
            int n = Mathf.Min(parts.Count, 64);
            var excess = new float[n];
            for (int i = 0; i < n; i++)
            {
                Part p = parts[i * parts.Count / n];
                excess[i] = p != null ? (float)(p.skinTemperature - v.atmosphericTemperature) : 0f;
            }
            Array.Sort(excess);
            return Mathf.Max(0f, excess[n / 2]);
        }

        /// <summary>
        /// What the ice does to the craft, this physics step: its wings lift less and drag more, every part drags more (all
        /// by how thick it is, worst at fifteen millimetres), and it weighs what it weighs.
        /// </summary>
        static void Feel(Vessel v, float mm)
        {
            float bad = Mathf.Pow(Mathf.Clamp01(mm / 15f), 0.75f);
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (p == null || p.rb == null) continue;
                if (!far && !v.LandedOrSplashed)
                {
                    // (the game's own drag on the part, more by up to six tenths)
                    if (p.dragScalar > 0f) p.AddForceAtPosition(-p.dragVectorDir * (p.dragScalar * 0.6f * bad), p.partTransform.TransformPoint(p.CoPOffset));
                    // (its wings: less lift, more drag)
                    for (int k = 0; k < p.Modules.Count; k++)
                    {
                        if (!(p.Modules[k] is ModuleLiftingSurface wing)) continue;
                        p.AddForceAtPosition(-wing.liftForce * (0.4f * bad), p.partTransform.TransformPoint(p.CoLOffset));
                        p.AddForceAtPosition(wing.dragForce * bad, p.partTransform.TransformPoint(p.CoPOffset));
                    }
                }
                // (its weight: a quarter of the part's skin faces into the weather, iced so thick; ice is 0.917 t a cubic metre)
                float tonnes = (float)(mm * 1e-3 * 0.25 * p.skinExposedArea * 0.917);
                if (tonnes > 0f) p.AddForce(FlightGlobals.getGeeForceAtPosition(p.transform.position) * tonnes);
            }
        }

        /// <summary>For the craft being flown: the line in the weather's window and a word on the screen when it begins, gets worse, and is gone.</summary>
        void Tell()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || !craft.TryGetValue(v.id, out Iced ice)) { Now = 0f; Building = false; Report = ""; return; }
            Now = ice.Mm; Building = ice.Rate > 1e-5f;
            int level = ice.Mm >= 15f ? 3 : ice.Mm >= 5f ? 2 : ice.Mm >= 0.5f ? 1 : 0;
            string[] names = { "", "light", "moderate", "severe" };
            if (ice.Mm < 0.2f && !Building) Report = "";
            else Report = "Ice on the craft: " + (ice.Mm < 1f ? ice.Mm.ToString("F1") : ice.Mm.ToString("F0")) + " mm" + (level > 0 ? " (" + names[level] + ")" : "")
                          + (Building ? ", building " + (ice.Rate * 60f).ToString(ice.Rate * 60f < 1f ? "F2" : "F1") + " mm a minute" + (ice.Kind.Length > 0 ? " in " + ice.Kind : "") : ice.Rate < -1e-5f ? ", melting" : "") + ".";
            if (level > ice.Said)
            {
                string words = level == 1 ? "Ice is forming on the craft" + (ice.Kind.Length > 0 ? " (" + ice.Kind + ")" : "") + "."
                             : level == 2 ? "Moderate icing: the craft is heavier and its wings lift less."
                             : "Severe icing: lift is falling and drag rising. Get out of the cloud, or down into warmer air.";
                ScreenMessages.PostScreenMessage(words, 6f, ScreenMessageStyle.UPPER_CENTER);
                ice.Said = level;
            }
            else if (ice.Said > 0 && ice.Mm < 0.2f)
            {
                ScreenMessages.PostScreenMessage("The ice is gone.", 4f, ScreenMessageStyle.UPPER_CENTER);
                ice.Said = 0;
            }
            else if (level < ice.Said - 1) ice.Said = level + 1;
        }
    }
}
