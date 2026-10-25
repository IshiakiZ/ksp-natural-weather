using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The wind on craft, through the game's own aerodynamics. The game works out how fast the air goes past a part in three
    /// places: its drag sums (FlightIntegrator.UpdateAerodynamics: drag, body lift, parachutes, dynamic pressure), wings
    /// (ModuleLiftingSurface) and control surfaces (ModuleControlSurface). In each, the air is taken to be still: a part's
    /// airspeed is its own velocity. Three small changes, made with Harmony as the game runs, take the wind away from that
    /// velocity at the one place each works it out. Nothing else in the game is touched.
    ///
    /// Where the Harmony mod is not installed, wind does not move craft (and the page says so); everything else is the same.
    /// Where a mod has taken ModularFlightIntegrator's place for the drag sums (the Kerbal Weather Project, Advanced
    /// Atmosphere Tools), those sums are no longer the game's and the change there does nothing: that mod's wind is the one.
    /// </summary>
    static class Aero
    {
        public static string Report = "";
        static bool started;

        public static void Start()
        {
            if (started) return;
            started = true;
            Far();
            if (!HarmonyThere()) { Report = "Wind cannot move craft here: the Harmony mod (HarmonyKSP) is not installed."; Weather.Log(Report); return; }
            try { Report = Patches.Apply(); Weather.Log(Report); }
            catch (Exception ex) { Report = "Wind cannot move craft: the game's aerodynamics could not be changed (" + ex.GetType().Name + ")."; Weather.Log(Report + " " + ex); }
        }

        /// <summary>
        /// Where Ferram Aerospace Research is installed, it does the aerodynamics instead of the game, and takes a wind from one
        /// function (FARAtmosphere.SetWindFunction): ours, set by name (this mod is not built against FAR). WindAPI, where it is
        /// installed too, takes such a function as the first of the winds it adds up. (Tried on the PC, 2026-10-24, with FAR 0.16.1.2:
        /// FARAtmosphere.GetWind for a craft's part gives the weather's wind there: Probe.FarWind.)
        /// </summary>
        static void Far()
        {
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("FerramAerospaceResearch.FARAtmosphere", false);
                    if (t == null) continue;
                    MethodInfo set = t.GetMethod("SetWindFunction", BindingFlags.Public | BindingFlags.Static);
                    if (set == null) continue;
                    Func<CelestialBody, Part, Vector3, Vector3> wind = FarWind;
                    set.Invoke(null, new object[] { wind });
                    FarReport = " FAR is installed: its aerodynamics take the weather's wind.";
                    Weather.Log("FAR found: the weather's wind handed to FARAtmosphere.SetWindFunction");
                    return;
                }
            }
            catch (Exception ex) { FarReport = " FAR is installed, but would not take the weather's wind (" + ex.GetType().Name + ")."; }
        }

        public static string FarReport = "";

        static Vector3 FarWind(CelestialBody body, Part part, Vector3 position)
        {
            if (part != null) return WindOn(part, position);
            // (asked about a place with no part: a mod predicting a path through the air)
            if (Weather.On == null || !Weather.On || !Weather.Push || body == null || !body.atmosphere || !Model.Allowed(body)) return Vector3.zero;
            return (Vector3)Keystone.Weather.Wind(body, (Vector3d)position, Planetarium.GetUniversalTime());
        }

        /// <summary>
        /// Whether another mod has put its own wind into the game's drag sums, through ModularFlightIntegrator (the Kerbal Weather
        /// Project, Advanced Atmosphere Tools, CPWE): then ours stands aside altogether, wings and all, so that no craft feels two
        /// winds. Looked at every few seconds.
        /// </summary>
        static bool OthersPush()
        {
            if (Time.realtimeSinceStartup < othersAt) return others;
            othersAt = Time.realtimeSinceStartup + 5f;
            others = false;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // (ModularFI.ModularFlightIntegrator: until 2026-10-24 looked up as ModularFlightIntegrator.ModularFlightIntegrator,
                    // which is not its name, so that this never stood aside. And not for FAR's override: that is its aerodynamics, which
                    // take the weather's wind from this mod (Far), not a wind of its own; seen on the PC with FAR 0.16.1.2)
                    Type t = a.GetType("ModularFI.ModularFlightIntegrator", false);
                    FieldInfo f = t != null ? t.GetField("updateAerodynamicsOverride", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) : null;
                    Delegate d = f != null ? f.GetValue(null) as Delegate : null;
                    if (d != null && d.Method.DeclaringType.Assembly.GetName().Name != "FerramAerospaceResearch") { others = true; break; }
                }
            }
            catch (Exception) { }
            if (others && !saidOthers) { saidOthers = true; Report = "Another mod puts its own wind into the game's aerodynamics (through ModularFlightIntegrator): this one stands aside, and draws in its own wind only."; Weather.Log(Report); }
            return others;
        }
        static float othersAt = -1f;
#if DEV
        internal static bool OthersPushNow => OthersPush();
#endif
        static bool others, saidOthers;

        static bool HarmonyThere()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "0Harmony") return true;
            return false;
        }

        // ---------------------------------------------------------------- what the changed code calls

        /// <summary>A part's airspeed for its drag: its velocity, less the wind where it is.</summary>
        public static Vector3 Dragged(Vector3 velocity, Part part)
        {
            if (part == null) return velocity;
            return velocity - WindOn(part, part.transform.position);
        }

        /// <summary>A wing's or a control surface's airspeed: the velocity of the point it works from, less the wind there.</summary>
        public static Vector3 Lifted(Vector3 velocity, PartModule module)
        {
            if (module == null || module.part == null) return velocity;
            return velocity - WindOn(module.part, module.part.transform.position);
        }

        static Vector3 WindOn(Part part, Vector3 at)
        {
            if (Weather.On == null || !Weather.On || !Weather.Push || OthersPush()) return Vector3.zero;
#if DEV
            if (Probe.NoPush) return Vector3.zero;
#endif
            Vessel v = part.vessel;
            if (v == null || v.packed || v.mainBody == null || !v.mainBody.atmosphere) return Vector3.zero;
            if (!Weather.Tip && v.LandedOrSplashed) return Vector3.zero;
            if (v.altitude > v.mainBody.atmosphereDepth || !Model.Allowed(v.mainBody)) return Vector3.zero;
            Sampler s = Sampler.Of(v);
            return s.At(at);
        }

        /// <summary>
        /// The wind over one craft, worked out once a physics step: at its middle, and how it changes across it (from six more
        /// points a few metres off each way), so that each part gets the wind where it is without asking the weather for each.
        /// </summary>
        sealed class Sampler
        {
            static readonly Dictionary<Guid, Sampler> all = new Dictionary<Guid, Sampler>();
            static float tidied;

            Vessel vessel;
            float stamp = -1f;
            Vector3d middle;
            Vector3 w0, ge, gn, gu, east, north, up;

            public static Sampler Of(Vessel v)
            {
                if (!all.TryGetValue(v.id, out Sampler s)) { s = new Sampler { vessel = v }; all[v.id] = s; }
                s.vessel = v;
                if (s.stamp != Time.fixedTime) s.Work();
                if (Time.realtimeSinceStartup - tidied > 30f) { tidied = Time.realtimeSinceStartup; Tidy(); }
                return s;
            }

            static void Tidy()
            {
                var gone = new List<Guid>();
                foreach (KeyValuePair<Guid, Sampler> kv in all) if (kv.Value.vessel == null || !kv.Value.vessel.loaded) gone.Add(kv.Key);
                foreach (Guid g in gone) all.Remove(g);
            }

            void Work()
            {
                stamp = Time.fixedTime;
                Vessel v = vessel;
                CelestialBody body = v.mainBody;
                World w = NaturalWeather.World.Of(body);
                double ut = Planetarium.GetUniversalTime();
                middle = v.CoMD;
                double lat = body.GetLatitude(middle), lon = body.GetLongitude(middle), alt = body.GetAltitude(middle);
                double ground = v.terrainAltitude;
                if (body.ocean && ground < 0.0) ground = 0.0;
                float agl = (float)Math.Max(0.0, alt - ground);
                Provider.Axes(body, middle, out Vector3d e, out Vector3d n, out Vector3d u);
                east = (Vector3)e; north = (Vector3)n; up = (Vector3)u;
                // (half the craft's size each way, between two and twenty metres)
                Vector3 size = v.vesselSize;
                float r = Mathf.Clamp(0.5f * Mathf.Max(size.x, Mathf.Max(size.y, size.z)), 2f, 20f);
                double dLat = r / body.Radius * 180.0 / Math.PI, dLon = dLat / Math.Max(0.05, Math.Cos(lat * Math.PI / 180.0));
                Vector3 c = Sample(w, lat, lon, alt, agl, ut);
                w0 = c;
                // (how it changes across the craft, for the one being flown: gusts that differ from one end of it to the other turn
                // it; for every other piece about, once at its middle is enough, and costs a seventh)
                if (v != FlightGlobals.ActiveVessel) { ge = gn = gu = Vector3.zero; return; }
                Vector3 pe = Sample(w, lat, lon + dLon, alt, agl, ut), me = Sample(w, lat, lon - dLon, alt, agl, ut);
                Vector3 pn = Sample(w, lat + dLat, lon, alt, agl, ut), mn = Sample(w, lat - dLat, lon, alt, agl, ut);
                Vector3 pu = Sample(w, lat, lon, alt + r, agl + r, ut), mu = Sample(w, lat, lon, alt - r, Mathf.Max(0f, agl - r), ut);
                ge = (pe - me) / (2f * r); gn = (pn - mn) / (2f * r); gu = (pu - mu) / (2f * r);
            }

            Vector3 Sample(World w, double lat, double lon, double alt, float agl, double ut)
            {
                Vector3 enu = Provider.Local(w, lat, lon, alt, agl, ut, true);
                return east * enu.x + north * enu.y + up * enu.z;
            }

            public Vector3 At(Vector3 p)
            {
                Vector3 d = p - (Vector3)middle;
                return w0 + ge * Vector3.Dot(d, east) + gn * Vector3.Dot(d, north) + gu * Vector3.Dot(d, up);
            }
        }

        /// <summary>The wind on a craft now, at its middle (world space, m/s): for readouts.</summary>
        public static Vector3 OnCraft(Vessel v)
        {
            if (v == null || v.mainBody == null || !v.mainBody.atmosphere || !Model.Allowed(v.mainBody)) return Vector3.zero;
            return Sampler.Of(v).At((Vector3)v.CoMD);
        }

        /// <summary>
        /// The wind that moves a craft now, at its middle (world space, m/s): zero wherever wind does not push craft (switched
        /// to look only, another mod's wind in charge, landed with tipping off, above the air). For the AI Bridge's autopilots,
        /// which fly by the air the craft really feels.
        /// </summary>
        public static Vector3 Pushing(Vessel v) => v == null || v.rootPart == null ? Vector3.zero : WindOn(v.rootPart, (Vector3)v.CoMD);
    }

    /// <summary>Everything that names Harmony: kept apart, so that the mod loads where Harmony is not installed.</summary>
    static class Patches
    {
        public const string Id = "naturalweather.aero";
        static int dragSites, liftSites, surfaceSites;

        public static string Apply()
        {
            var harmony = new Harmony(Id);
            // (a copy of this mod before this one, in the development build, leaves its changes in: they are taken out first, and only ours)
            try { harmony.UnpatchAll(Id); } catch (Exception) { }
            dragSites = liftSites = surfaceSites = 0;
            MethodInfo drag = AccessTools.DeclaredMethod(typeof(FlightIntegrator), "UpdateAerodynamics", new[] { typeof(Part) });
            MethodInfo lift = AccessTools.DeclaredMethod(typeof(ModuleLiftingSurface), "FixedUpdate");
            MethodInfo surface = AccessTools.DeclaredMethod(typeof(ModuleControlSurface), "FixedUpdate");
            if (drag != null) harmony.Patch(drag, transpiler: new HarmonyMethod(typeof(Patches), nameof(DragIL)));
            if (lift != null) harmony.Patch(lift, transpiler: new HarmonyMethod(typeof(Patches), nameof(LiftIL)));
            if (surface != null) harmony.Patch(surface, transpiler: new HarmonyMethod(typeof(Patches), nameof(SurfaceIL)));
            string said = "wind moves craft: the drag sums changed in " + dragSites + " place(s), wings in " + liftSites + ", control surfaces in " + surfaceSites + ".";
            if (dragSites == 0 || liftSites == 0 || surfaceSites == 0) said = "wind moves craft only in part: the game's code was not as expected (drag " + dragSites + ", wings " + liftSites + ", control surfaces " + surfaceSites + ").";
            return said;
        }

        static readonly MethodInfo frame = AccessTools.Method(typeof(Krakensbane), "GetFrameVelocity");
        static readonly MethodInfo frameV3f = AccessTools.Method(typeof(Krakensbane), "GetFrameVelocityV3f");
        static readonly FieldInfo dragVector = AccessTools.Field(typeof(Part), "dragVector");
        static readonly MethodInfo dragged = AccessTools.Method(typeof(Aero), nameof(Aero.Dragged));
        static readonly MethodInfo lifted = AccessTools.Method(typeof(Aero), nameof(Aero.Lifted));

        /// <summary>
        /// In the drag sums: just before the part's airspeed is stored (part.dragVector = velocity + the frame's velocity), take the
        /// wind away from it. (Beside KSP Community Fixes the game's drag sums are its own, FlightIntegratorPerf's, which read the
        /// frame's velocity once a physics step into a field of theirs, krakensbaneFrameVelocityF: that is taken for it too. Found on
        /// the PC with 1.41.1, 2026-10-24: there the wind had stopped pushing on the drag, the wings and control surfaces still did.)
        /// </summary>
        static IEnumerable<CodeInstruction> DragIL(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i].operand is FieldInfo f && f.Name == "dragVector" && list[i].opcode.Name == "stfld")) continue;
                bool fromFrame = false;
                var near = new System.Text.StringBuilder();
                for (int j = Math.Max(0, i - 12); j < i; j++)
                {
                    near.Append(list[j].opcode.Name).Append(' ').Append(list[j].operand is MemberInfo mm ? mm.Name : list[j].operand != null ? list[j].operand.ToString() : "").Append("; ");
                    if (list[j].operand is MethodBase m && m.Name == "GetFrameVelocity") fromFrame = true;
                    if (list[j].operand is FieldInfo kept && kept.Name.IndexOf("FrameVelocity", StringComparison.OrdinalIgnoreCase) >= 0 && list[j].opcode.Name.StartsWith("ldfld", StringComparison.Ordinal)) fromFrame = true;
                }
                if (!fromFrame) { if (dragSites == 0) Weather.Log("a store to dragVector without the frame's velocity before it: " + near); continue; }
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_1));
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, dragged));
                i += 2;
                dragSites++;
            }
            if (dragSites == 0) Tell("the drag sums", list);
            return list;
        }

        /// <summary>When the code was not as expected: what it calls that looks like the frame's velocity, in the log, to see why.</summary>
        static void Tell(string what, List<CodeInstruction> list)
        {
            var seen = new System.Text.StringBuilder();
            foreach (CodeInstruction c in list)
                if (c.operand is MemberInfo mi && (mi.Name.IndexOf("Frame", StringComparison.Ordinal) >= 0 || mi.Name.IndexOf("drag", StringComparison.OrdinalIgnoreCase) >= 0 || mi.Name.IndexOf("Point", StringComparison.Ordinal) >= 0))
                    seen.Append(c.opcode).Append(' ').Append(mi.DeclaringType != null ? mi.DeclaringType.Name : "?").Append('.').Append(mi.Name).Append("; ");
            Weather.Log("in " + what + " (" + list.Count + " instructions) nothing was found to change; it uses: " + seen);
        }

        static IEnumerable<CodeInstruction> LiftIL(IEnumerable<CodeInstruction> code) => Points(code, ref liftSites);
        static IEnumerable<CodeInstruction> SurfaceIL(IEnumerable<CodeInstruction> code) => Points(code, ref surfaceSites);

        /// <summary>In a wing's update: just after the velocity of the point it works from is added up (point velocity + the frame's velocity), take the wind away from it.</summary>
        static IEnumerable<CodeInstruction> Points(IEnumerable<CodeInstruction> code, ref int sites)
        {
            var list = new List<CodeInstruction>(code);
            for (int i = 0; i + 1 < list.Count; i++)
            {
                // (the frame's velocity itself, the moment it is fetched, less the wind: whatever it is then added to, the sum is
                // through the air, and every use of it in the wing's update is the air's)
                if (!(list[i].operand is MethodBase m && m.Name == "GetFrameVelocityV3f")) continue;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, lifted));
                i += 2;
                sites++;
            }
            if (sites == 0) Tell("a wing's update", list);
            return list;
        }
    }
}
