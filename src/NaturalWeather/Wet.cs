using System;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// What rain leaves behind it: the ground wet (darker, which is done where the clouds' shadow is laid on; a mirror of
    /// the sky seen along it, and water standing on level ground, which are done here) and drops on the camera's glass.
    /// Two passes over the near camera's picture with the one shader (tools/shaderpack/wet.glsl), put on only while there
    /// is something for them to draw: the ground's with the solid things, before anything see-through is drawn; the glass's
    /// over the finished picture.
    ///
    /// How wet. The ground is wetted by rain in seconds (a heavy shower) to minutes (drizzle) and dries in a quarter of an
    /// hour; water gathers on level ground while it rains hard enough and drains and dries in ten minutes after. The
    /// glass gathers drops while the camera is out in rain, as many as the rain is heavy; they dry in half a minute, and
    /// are blown off at speed. All of it is kept for the place the camera is at; a camera that comes to a new place finds
    /// it as wet as the last twenty minutes' weather there made it.
    /// </summary>
    public sealed class Wet : MonoBehaviour
    {
        /// <summary>How wet the ground is where the camera is (0 to 1); how much of level ground stands under water; how much of the glass has drops on it.</summary>
        public static float Ground, Puddles, Glass;
        /// <summary>The ground and its standing water as drawn: as wet as the rain has made them, times the page's fine details.</summary>
        public static float GroundShown => Mathf.Clamp01(Ground * Weather.Times(Weather.WetLook));
        public static float PuddlesShown => Mathf.Clamp01(Puddles * Weather.Times(Weather.StandingWater));
        public static string Report = "";
        /// <summary>How fine the picture of the water on the glass is drawn (wet.glsl reads every drop's slope from it): at the
        /// screen's own height (from 2026-11-07), or at half of it, as until then, when each of its points was two pixels of the
        /// screen and the bending through a drop magnified each kink between them into a seam, a mosaic of small blocks in
        /// every drop.</summary>
        public static bool DropsFull = true;
        /// <summary>How the camera's own motion through the air is reckoned for the drops on its glass (StepDrops): 0, from
        /// 2026-11-09, the craft's motion alone, the camera's swing round the craft and its drawing in and out being a change of
        /// view, not a flight through the air; 1, the camera's motion round the craft counted too, about the game's pivot, as
        /// from 2026-11-07; 2, about the craft's centre of mass, as until then (about it the camera shook as fast as the craft
        /// went, and the glass with it).</summary>
        internal static int CamWay;
#if DEV
        /// <summary>(development build) "craft" (or anything else; the default): the camera goes through the air as the craft does,
        /// its swing round the craft a change of view; "pivot": its motion round the craft counted too, about the point it turns
        /// round, as from 2026-11-07 until 2026-11-09; "centre": about the craft's centre of mass, as until 2026-11-07.</summary>
        public static string CamAbout(string arg)
        {
            string a = (arg ?? "").Trim();
            CamWay = a == "centre" ? 2 : a == "pivot" ? 1 : 0;
            return "the camera's motion through the air: " + (CamWay == 0 ? "the craft's alone (its swing round the craft a change of view)"
                : CamWay == 1 ? "the craft's and its own round the craft, about the camera's pivot (as until 2026-11-09)"
                : "the craft's and its own round the craft, about the craft's centre of mass (as until 2026-11-07)");
        }
        /// <summary>(development build) held by hand: less than nothing lets go.</summary>
        public static float HeldGround = -1f, HeldPuddles = -1f, HeldGlass = -1f;
        public static string Hold(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            float V(int i) => i < t.Length && float.TryParse(t[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1f;
            HeldGround = V(0); HeldPuddles = V(1); HeldGlass = V(2);
            return HeldGround < 0f && HeldGlass < 0f ? "the wet is the weather's own" : "held: ground " + HeldGround + ", puddles " + HeldPuddles + ", glass " + HeldGlass;
        }
        public static string Says() => Report;
        /// <summary>(development build) the lens's opening the drops are drawn through (mm on the glass): a number holds it, "old" draws
        /// them through half a millimetre with five lines of sight as until 2026-11-05, "light" lets the light choose it again, and
        /// "light D M" from D mm in daylight to M at the most (0.6 and 3 as installed; 0.45 and 2.6 until 2026-11-09).</summary>
        public static float HeldOpening = -1f;
        public static string Opening(string arg)
        {
            string a = (arg ?? "").Trim();
            string[] t = a.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (a == "old") HeldOpening = 0f;
            else if (float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f) HeldOpening = Mathf.Clamp(v, 0.2f, 6f);
            else
            {
                HeldOpening = -1f;
                if (t.Length >= 3 && t[0] == "light" && float.TryParse(t[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float day)
                    && float.TryParse(t[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float most))
                {
                    OpenInDay = Mathf.Clamp(day, 0.2f, 6f);
                    OpenAtMost = Mathf.Clamp(most, OpenInDay, 6f);
                }
            }
            return "the lens's opening: " + (HeldOpening == 0f ? "as before (0.45 mm, five lines)" : HeldOpening > 0f ? "held at " + HeldOpening.ToString("F2") + " mm" : "by the light, from " + OpenInDay.ToString("F2") + " mm in daylight to " + OpenAtMost.ToString("F2")) + "; now " + (instance != null ? instance.opening.ToString("F2") : "?") + " mm";
        }
#endif
        /// <summary>The lens's opening on the glass by daylight and at the most, at night (mm: the radius of the patch of glass each
        /// point of the picture is seen through, half the lens's own width): Update, where the light chooses it.</summary>
        public static float OpenInDay = 0.6f, OpenAtMost = 3f;
        /// <summary>How the drops on the glass are drawn (wet.glsl's _WetLook, 2026-11-09): x how many steps less smoothed their
        /// slope is read than the space between the lines of sight through the opening (0: as smooth as that space); y how wide
        /// the scene is smeared along each line, against how far its look lies from the middle line's (0.35: an outer line's share
        /// of the opening is about half as wide as it lies out, and the picture's smaller copies the smear is read from smooth
        /// about as much again; at 0.7 the drops by day all but vanished over even ground; 0, each a sharp copy, as until
        /// 2026-11-09); z the light in their rims (1).</summary>
        public static Vector3 Look = new Vector3(0f, 0.35f, 1f);
        /// <summary>How many times brighter than the picture holds them its lights (a lamp, a flame, a white rocket in floodlights:
        /// whatever is as bright as the picture can hold) are counted where the drops on the glass gather light, at night
        /// (wet.glsl's _WetLights and lampsIn, 2026-11-09). Nought with the lens's opening as by day, all of it with the opening
        /// LightsFullAt mm or wider, and between as the opening's area: an automatic camera's exposure goes as that, and a lamp
        /// outruns the white of the picture by as much as the exposure is long. (All of it from one millimetre, not from the
        /// widest: the game's night is never darker than opens the lens to some 2 mm, and night is when the lamps are lit.)
        /// A real lamp is thousands of times brighter than the ground it lights; the picture holds it as plain white, so the
        /// drops near it, which in a real camera are soft discs of its light, were grey smudges.</summary>
        public static float LightsAtNight = 3f;
        public static float LightsFullAt = 1f;
        /// <summary>The most of that light a drop shows, the picture's white being 1: it comes to this softly.</summary>
        public static float LightsAtMost = 0.5f;
#if DEV
        /// <summary>(development build) how many times brighter the picture's lights are counted for the drops on the glass: a number
        /// holds it whatever the lens's opening, "light N" lets the opening choose it again, N at night (3 as installed); a second
        /// number after either, the most of that light a drop shows (0.5 as installed); "plain" has the soft copy made as until
        /// 2026-11-09, a plain copy with no light counted brighter (to time the one against the other), until "light"; "" says it.</summary>
        public static float HeldLights = -1f;
        public static bool PlainCopy;
        public static string Lights(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length >= 1 && t[0] == "plain") { PlainCopy = true; HeldLights = 0f; }
            else if (t.Length >= 1 && t[0] == "light")
            {
                HeldLights = -1f; PlainCopy = false;
                if (t.Length >= 2 && float.TryParse(t[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n)) LightsAtNight = Mathf.Clamp(n, 0f, 2000f);
                if (t.Length >= 3 && float.TryParse(t[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m)) LightsAtMost = Mathf.Clamp(m, 0.01f, 4f);
            }
            else if (t.Length >= 1 && float.TryParse(t[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
            {
                HeldLights = Mathf.Clamp(v, 0f, 2000f);
                if (t.Length >= 2 && float.TryParse(t[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m)) LightsAtMost = Mathf.Clamp(m, 0.01f, 4f);
            }
            return "the picture's lights for the drops: " + (PlainCopy ? "none, the soft copy a plain one; " : "") + (HeldLights >= 0f ? "held at " + HeldLights.ToString("F1") + " times" : "by the lens's opening, " + LightsAtNight.ToString("F1") + " times at night")
                + ", at most " + LightsAtMost.ToString("F2") + "; now " + (instance != null ? instance.lights.ToString("F2") : "?");
        }
        /// <summary>(development build) how the drops on the glass are drawn: "x y z" as Look has them; "" says them.</summary>
        public static string LookAs(string arg)
        {
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < t.Length && i < 3; i++)
                if (float.TryParse(t[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) Look[i] = Mathf.Clamp(v, 0f, 8f);
            return "the drops drawn: their slope read " + Look.x.ToString("F2") + " steps less smoothed than the lines' space, the scene smeared " + Look.y.ToString("F2") + " of the lines' looks apart, the rims' light " + Look.z.ToString("F2");
        }
        /// <summary>(development build) "off": every drop lands as a cap, as until 2026-11-05; "on": large fast drops land as splats
        /// with a ring of specks (Drops.Step).</summary>
        public static string Splats(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "off") Drops.Splats = false; else if (a == "on") Drops.Splats = true;
            return "splats " + (Drops.Splats ? "on" : "off (every drop a cap)");
        }
        /// <summary>(development build) "half": the water's picture at half the screen's height, as until 2026-11-07; "full": at its
        /// own height.</summary>
        public static string DropsDetail(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "half") DropsFull = false; else if (a == "full") DropsFull = true;
            return "the water's picture at " + (DropsFull ? "the screen's height" : "half the screen's height (as before)") + (instance != null && instance.drops.Picture != null ? ": " + instance.drops.Picture.width + " x " + instance.drops.Picture.height : "");
        }
        /// <summary>(development build) the mist on the glass (Drops): "off" none, as until 2026-11-05; "on" gathered by the rain; a
        /// number from 0 to 1 holds every patch at about so much, to see it.</summary>
        public static string Mist(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "off") { Drops.MistOn = false; Drops.MistHeld = -1f; }
            else if (a == "on") { Drops.MistOn = true; Drops.MistHeld = -1f; }
            else if (float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) { Drops.MistOn = true; Drops.MistHeld = Mathf.Clamp01(v); }
            return "mist " + (!Drops.MistOn ? "off" : Drops.MistHeld >= 0f ? "held at " + Drops.MistHeld.ToString("F2") : "gathered by the rain") + "; now at most " + (instance != null ? instance.drops.MistMost.ToString("F2") : "?");
        }
        /// <summary>(development build) "off": no light streaks across the drops' wet tracks, as until 2026-11-05; "on": streaks; a number from 0 to 1 holds every track that wet, to see them.</summary>
        public static string Streaks(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "off") Drops.StreaksOn = false; else if (a == "on") { Drops.StreaksOn = true; Drops.TracksHeld = -1f; }
            else if (float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) { Drops.StreaksOn = true; Drops.TracksHeld = Mathf.Clamp01(v); }
            return "streaks " + (Drops.StreaksOn ? "on" : "off") + (Drops.TracksHeld >= 0f ? ", the glass held wet in tracks at " + Drops.TracksHeld.ToString("F2") : "");
        }
        /// <summary>(development build) the drops on the glass: how many, how many running, how much of the glass is under water.</summary>
        public static string DropsSays() => instance != null ? instance.drops.Says() + "; rain at the glass " + instance.stepRain.ToString("F2") + ", landing " + instance.arriving.ToString("F1") + " a second (" + Mathf.RoundToInt(100f * instance.facingSaid) + "% facing the rain)"
            + (instance.stepSnow > 0.02f ? "; snow at the glass " + instance.stepSnow.ToString("F2") + " mm of water an hour, flakes landing " + instance.flakesSaid.ToString("F1") + " a second" : "")
            + (instance.under ? "; the glass under water" : "; " + (float.IsInfinity(instance.lastAbove) ? "no sea" : instance.lastAbove.ToString("F1") + " m above the water")) + (instance.surfacedAt >= 0f ? "; last came out at " + instance.surfacedAt.ToString("F1") + " m/s" : "")
            + "; " + instance.thrown.Says() : "no drops: not in flight";
        /// <summary>(development build) the glass made so many degrees colder than the air now, as if just come down from high up
        /// (the seventh gap: it fogs where the air's dew point is over it), or "" to say how it stands.</summary>
        public static string Chill(string arg)
        {
            if (instance == null) return "not running";
            if (float.TryParse((arg ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float k)) instance.glassT = instance.airTSaid - k;
            return "the glass " + (instance.glassT - 273.15f).ToString("F1") + " C, the air " + (instance.airTSaid - 273.15f).ToString("F1") + " C, its dew point " + (instance.dewTSaid - 273.15f).ToString("F1") + " C; condensing " + instance.drops.Condense.ToString("F3") + " a second";
        }
        /// <summary>(development build) "off": no water spots where drops dry, as until 2026-11-05; "on": spots.</summary>
        public static string Spots(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "off") Drops.SpotsOn = false; else if (a == "on") Drops.SpotsOn = true;
            return "water spots " + (Drops.SpotsOn ? "on" : "off");
        }
        /// <summary>(development build) the glass held at so many degrees (C), whatever the air's: to see snow lie on it below
        /// freezing and melt on it above, and rain freeze on it; "" lets it come to the air's warmth again, over a few minutes.</summary>
        public static float HeldGlassC = float.NaN;
        public static string GlassAt(string arg)
        {
            HeldGlassC = float.TryParse((arg ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float c) ? Mathf.Clamp(c, -40f, 40f) : float.NaN;
            return "the glass " + (float.IsNaN(HeldGlassC) ? "left to the air's warmth" : "held at " + HeldGlassC.ToString("F1") + " C") + (instance != null ? "; now " + (instance.glassT - 273.15f).ToString("F1") + " C, the air " + (instance.airTSaid - 273.15f).ToString("F1") + " C" : "");
        }
        /// <summary>(development build) "off": no snow or ice on the glass, as until 2026-11-07 (snow left nothing on it, freezing
        /// rain ran on it as rain); "on": flakes lie on it and melt, water freezes on it below freezing.</summary>
        public static string Snow(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "off") Drops.SnowOn = false; else if (a == "on") Drops.SnowOn = true;
            return "snow and ice on the glass " + (Drops.SnowOn ? "on" : "off");
        }
        /// <summary>(development build) "off": no water thrown at the glass, as until 2026-11-07; "on": spray off wheels on wet ground,
        /// splashes and a hull's sheet reach it (Thrown); "" says how it stands.</summary>
        public static string Spray(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "off") Thrown.On = false; else if (a == "on") Thrown.On = true;
            if (instance == null) return "thrown water " + (Thrown.On ? "on" : "off") + "; not running";
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody b = FlightGlobals.currentMainBody;
            return instance.thrown.Says() + (cam != null && b != null ? "; " + instance.thrown.Nearest((Vector3d)cam.transform.position - b.position) : "");
        }
        /// <summary>(development build) a splash so far ahead of the camera (m, along the level), of so many megajoules, on the
        /// ground or the sea there, as if a craft had struck it straight down: "25 40".</summary>
        public static string SplashAt(string arg)
        {
            if (instance == null) return "not running";
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody b = FlightGlobals.currentMainBody;
            if (cam == null || b == null) return "no camera";
            string[] t = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            float V(int i, float none) => i < t.Length && float.TryParse(t[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : none;
            float ahead = V(0, 25f), megajoules = V(1, 10f);
            Vector3 camPos = cam.transform.position;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(b, (Vector3d)camPos);
            Vector3 level = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (level.sqrMagnitude < 1e-4f) level = Vector3.ProjectOnPlane(cam.transform.up, up);
            Vector3d there = (Vector3d)(camPos + level.normalized * ahead);
            double lat = b.GetLatitude(there), lon = b.GetLongitude(there);
            double h = b.TerrainAltitude(lat, lon, true);
            if (b.ocean && h < 0.0) h = 0.0;
            instance.thrown.BlowAt(b, b.GetWorldSurfacePosition(lat, lon, h), -up * 30f, megajoules * 1e6f);
            return "a splash of " + megajoules + " MJ " + ahead + " m ahead, " + h.ToString("F1") + " m over the sea's level";
        }
        /// <summary>(development build) the glass wiped.</summary>
        public static string WipeGlass() { if (instance != null) instance.drops.Clear(); return "wiped"; }
        /// <summary>(development build) the rings' anchor (and git's old OpenGL wet.bundle's puddles'): where it is laid out
        /// from, how often it has been carried on and started afresh, and where the camera stands in it.</summary>
        public static string AnchorSays() => instance == null || double.IsNaN(instance.anchorLat) ? "no anchor" : "anchor at " + instance.anchorLat.ToString("F4") + ", "
            + instance.anchorLon.ToString("F4") + " (" + instance.anchorAlt.ToString("F0") + " m), turned " + (instance.anchorTurn * 180.0 / Math.PI).ToString("F3") + " degrees, carried "
            + instance.carriedEast.ToString("F1") + ", " + instance.carriedNorth.ToString("F1") + " m; moved on " + instance.anchorMoves + " times, laid afresh " + instance.anchorFresh + " times";
#endif

        static Shader shader;
        static bool tried;
        static Mesh sheet;
        static string why = "";
        static readonly int idScene = Shader.PropertyToID("_WetScene");
        // (Direct3D draws the flight scene with one camera, and it draws the sky (Scatterer's, 2998) and the clouds (2999) among
        // the see-through things, after the ground's pass: the pools' mirror found nothing there yet where the sky would be and
        // mirrored black, the far ground under a band of black with the hills' tops showing through it (the owner's comparison
        // with Blackrack's of 2026-10-28, its "ground shadows black"). With one camera the frame's finished picture is kept, at
        // the end of the see-through things and before the glass's drops, with the way the camera looked, and the next frame's
        // pools mirror what lies far off from it: the sky, the clouds, the far ground under its haze.)
        RenderTexture last, lastFar;
        int lastFrame = -1;
        Vector3 lastRight, lastUp, lastAhead;
        static readonly int idLast = Shader.PropertyToID("_WetLast"), idLastFar = Shader.PropertyToID("_WetLastFar"), idHere = Shader.PropertyToID("_WetHere");
        // (and how far off each of its pixels was, beside it, written by the same shader with both its halves left out)
        MaterialPropertyBlock farOnly;
        Material material;
        CommandBuffer pass;
        Camera on;
        // (the ground's part, with the solid things: see BeforeCamera; and what leaves out the other half of the shader in each)
        CommandBuffer groundPass;
        MaterialPropertyBlock glassLeftOut, groundLeftOut;
        // (and what writes the picture's soft copy with its lights raised, for the drops on the glass: both halves left out; Lay)
        MaterialPropertyBlock softOnly;
        float lights;
        static readonly int idLights = Shader.PropertyToID("_WetLights");
        static readonly int idGround = Shader.PropertyToID("_WetGround"), idGlass = Shader.PropertyToID("_WetGlass");

        // (where the wet is known for, and when it was last brought up to date)
        string body = "";
        double lat = double.NaN, lon, at = double.NaN;
        // (The place the rings in the puddles are laid out from, fixed to the ground. After a load or a jump it is a point of a
        // grid fixed to the planet. As the camera goes on from there the rings' cells are carried along without a seam: every
        // kilometre the anchor is moved under the camera, with where it then lies in them and their level ways turned to keep
        // their bearings. The puddles themselves were laid out so too on 2026-10-28, until they were read through the planet
        // in three dimensions the same day; before that their place lay wherever the camera first was, and moved 3 km on from
        // it, where every pool in sight changed at once.)
        double anchorLat = double.NaN, anchorLon, anchorAlt, anchorTurn, carriedEast, carriedNorth, lastCamLat = double.NaN, lastCamLon;
        string anchorBody = "";
        int anchorMoves, anchorFresh;
        float rainNow;
        float opening;          // (the lens's opening the drops are drawn through, mm on the glass: see where _WetSun is set)

        static void LoadShader()
        {
            tried = true;
            try
            {
                Shader[] found;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore) found = Load("wet.bundle");
                else
                {
                    // (elsewhere, Direct3D on Windows: with the clouds' shaders, built by the Unity editor from tools/unityshaders)
                    string system = Application.platform == RuntimePlatform.WindowsPlayer ? "windows" : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "mac";
                    found = Load("clouds-" + system + ".bundle");
                }
                if (found != null) foreach (Shader one in found) if (one != null && one.name == "NaturalWeather/Wet" && one.isSupported) shader = one;
                Shader[] own = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ? Load("drops.bundle") : found;
                if (own != null) foreach (Shader one in own) if (one != null && one.name == "NaturalWeather/Drops" && one.isSupported) dropShader = one;
                if (shader == null) why = found == null ? "its shader's file is not there" : "there is no shader for it on " + SystemInfo.graphicsDeviceType + " in this build";
            }
            catch (Exception ex) { shader = null; why = "its shader did not load (" + ex.Message + ")"; }
            Debug.Log("[Natural Weather] rain on the glass and the ground: " + (shader != null ? "shader loaded" : "not drawn: " + why));
            if (sheet == null)
            {
                sheet = new Mesh { name = "Natural Weather: over the picture" };
                sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
                sheet.triangles = new[] { 0, 1, 2 };
                sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
        }

        static Shader[] Load(string file)
        {
            string path = Clouds.Folder + file;
            if (!System.IO.File.Exists(path)) return null;
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) return new Shader[0];
            Shader[] found = bundle.LoadAllAssets<Shader>();
            bundle.Unload(false);
            return found;
        }

        void Off()
        {
            if (on != null && pass != null) on.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, pass);
            if (on != null && groundPass != null) on.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, groundPass);
            on = null;
            ForgetLast();
        }

        void ForgetLast()
        {
            if (last != null) { last.Release(); Destroy(last); last = null; }
            if (lastFar != null) { lastFar.Release(); Destroy(lastFar); lastFar = null; }
            lastFrame = -1;
        }

        void OnDestroy()
        {
            Camera.onPreCull -= BeforeCamera;
            GameEvents.onCrashSplashdown.Remove(CrashedInWater);
            if (instance == this) instance = null;
            Off();
            if (pass != null) { pass.Release(); pass = null; }
            if (groundPass != null) { groundPass.Release(); groundPass = null; }
            if (material != null) Destroy(material);
            if (dropMaterial != null) Destroy(dropMaterial);
            drops.Dispose();
            Ground = Puddles = Glass = 0f;
        }

        /// <summary>The rain that reaches the ground at a place (0 to 1), water only: not snow, not ice pellets.</summary>
        static float RainAt(World w, double lat, double lon, double ut)
        {
            WeatherHere here = Model.Here(w, lat, lon, ut);
            return here.Rain * Mathf.Clamp01(1f - here.Snow - here.Pellets);
        }

        /// <summary>The ground's wet and its standing water carried on through so many seconds of so much rain.</summary>
        static void Carry(ref float ground, ref float puddles, float rain, double seconds)
        {
            // (in steps of no more than twenty seconds: under time warp a frame is minutes)
            int steps = Math.Max(1, (int)Math.Ceiling(seconds / 20.0));
            float dt = (float)(seconds / steps), dries = Weather.Times(Weather.GroundDries);
            for (int i = 0; i < steps; i++)
            {
                if (rain > 0.02f) { ground = Mathf.Min(1f, ground + rain * 0.08f * dt); puddles = Mathf.Min(1f, puddles + Mathf.Max(0f, rain - 0.12f) * 0.012f * dt); }
                else { ground *= Mathf.Exp(-dt * dries / 900f); puddles *= Mathf.Exp(-dt * dries / 600f); }
            }
        }

        void LateUpdate()
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody b = FlightGlobals.currentMainBody;
            bool flight = HighLogic.LoadedSceneIsFlight && Weather.FlightReady && cam != null && b != null && !MapView.MapIsEnabled;
            bool weather = flight && b.atmosphere && Weather.On && Model.Allowed(b);
            if (!weather) { Ground = Puddles = Glass = 0f; at = double.NaN; Off(); Report = ""; return; }
            Vector3 camPos = cam.transform.position;
            double camLat = b.GetLatitude(camPos), camLon = b.GetLongitude(camPos), alt = b.GetAltitude(camPos);
            double ut = Planetarium.GetUniversalTime();
            World w = World.Of(b);

            // ---- the ground at this place: carried on from the last frame, or, at a place new to the camera, made from the
            // weather the place has had (ten looks back over twenty minutes)
            bool elsewhere = body != b.bodyName || double.IsNaN(lat) || Model.Distance(w, lat, lon, camLat, camLon) > 15000.0;
            if (elsewhere || double.IsNaN(at) || ut < at || ut - at > 7200.0)
            {
                Ground = Puddles = 0f;
                for (int k = 10; k >= 1; k--) Carry(ref Ground, ref Puddles, RainAt(w, camLat, camLon, ut - k * 120.0), 120.0);
                body = b.bodyName;
            }
            WeatherHere here = Model.Here(w, camLat, camLon, ut);
            float water = here.Rain * Mathf.Clamp01(1f - here.Snow - here.Pellets);
            // (the snow's water, mm an hour, as Snow reckons it: what falls as 50 mm an hour times the square of its strength, a
            // third of that as snow, heavy snow some 2.5 to 5 mm of water an hour)
            float snowWater = 50f * 0.33f * here.Rain * here.Rain * Mathf.Clamp01(here.Snow);
            if (!double.IsNaN(at) && ut > at && !elsewhere) Carry(ref Ground, ref Puddles, water, ut - at);
            lat = camLat; lon = camLon; at = ut; rainNow = water;

            // ---- the glass's warmth (2026-11-05, queue item 20's seventh gap, research/lens-drops/understand.md): the camera's
            // glass comes to the air's warmth in a few minutes (two and a half at the ground, slower in thin air). Colder than
            // the air's dew point, as when it comes down out of the cold high air into warm wet air near the ground, water
            // condenses on it: a fog over the whole glass, patchy as the glass holds it, which drops running through it clear
            // in lanes; warmer, it dries off. The air cools 6.5 degrees a kilometre up to eleven; its dew point 2 a kilometre.
            {
                float up = Mathf.Min(Mathf.Max(0f, (float)(alt - Math.Max(0.0, b.TerrainAltitude(camLat, camLon, true)))), 11000f);
                float airT = here.Temperature - 0.0065f * up;
                float dewT = Synoptic.DewPoint(Model.Sample(w, camLat, camLon, ut), here) - 0.002f * up;
                Vessel v = FlightGlobals.ActiveVessel;
                float thick = v != null ? (float)(v.atmDensity / 1.225) : 1f;
                float dtGlass = TimeWarp.deltaTime;
                if (float.IsNaN(glassT) || elsewhere) glassT = airT;
                else if (thick > 0.001f) glassT += (airT - glassT) * (1f - Mathf.Exp(-dtGlass * Mathf.Min(thick, 1f) / 150f));
#if DEV
                if (!float.IsNaN(HeldGlassC)) glassT = HeldGlassC + 273.15f;
#endif
                airTSaid = airT; dewTSaid = dewT;
                // (and snow on it melts above freezing, water on it freezes below: Drops, 2026-11-07)
                drops.GlassC = glassT - 273.15f;
                // (3 degrees under the dew point fogs the glass in some ten seconds, as a lens brought out of the cold does; 3 over
                // dries it as fast. A third as fast, filmed 2026-11-05 (glassgaps.py glass), it was too faint to see.)
                drops.Condense = thick > 0.05f && Weather.Drops ? Mathf.Clamp((dewT - glassT) * 0.03f, -0.2f, 0.2f) : 0f;
            }

            // ---- the glass: drops while the camera is out in the rain (under the cloud it falls from, and not inside a craft)
            // (Where there is rain and the low cloud is all but gone, as in a cyclone's eye, where the low cloud is cut to a
            // quarter while the bands' rain goes on near it: the middle cloud's heights, and with none of that either, rain
            // from no higher than eight kilometres. Taken as falling there at every height, as until 2026-10-18, it left drops
            // on the glass in orbit: from 200 km over a hurricane's eye, rings scattered over the picture.)
            float under = here.Low.Cover > 0.05f ? Noise.Smooth(here.Low.Top, here.Low.Base, (float)alt)
                : here.Middle.Cover > 0.05f && here.Middle.Top > here.Middle.Base ? Noise.Smooth(here.Middle.Top, here.Middle.Base, (float)alt)
                : Noise.Smooth(8000f, 6000f, (float)alt);
            bool outside = CameraManager.Instance == null || CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight;
            float falling = outside ? water * under : 0f;
            // (the drops themselves are carried on just before the camera draws, where it really is then: StepDrops)
            stepRain = falling;
            stepSnow = outside ? snowWater * under : 0f;
#if DEV
            if (HeldGlass >= 0f) stepRain = HeldGlass;
#endif
            stepKeep = outside && Weather.Drops;
            if (!stepKeep) drops.Clear();
            stepWind = (Vector3)Keystone.Weather.Wind(b, (Vector3d)camPos, ut);
            Glass = drops.Covered;

            float ground = Weather.WetGround ? GroundShown : 0f, puddles = Weather.WetGround ? PuddlesShown : 0f, glass = stepKeep && (stepRain > 0.005f || stepSnow > 0.02f || drops.Count > 0 || drops.Lingers) ? 1f : 0f;
#if DEV
            if (HeldGround >= 0f) { ground = HeldGround; Ground = HeldGround; }
            if (HeldPuddles >= 0f) { puddles = HeldPuddles; Puddles = HeldPuddles; }
#endif
            // (Water thrown at the glass (2026-11-07): Thrown. The film of water on the ground the wheels meet: a soaked road's
            // half a millimetre, and where water stands on level ground (puddles), some eight millimetres over as much of it as
            // it stands on. Kept on while any of it is in the air, rain or none: a splash on a dry day.)
            thrown.Step(Time.deltaTime, b, camPos, stepWind, ground > 0.02f ? 0.5f * ground + 8f * puddles : 0f);
            if (stepKeep && thrown.Count > 0) glass = 1f;
            Report = "ground " + Ground.ToString("F2") + ", standing water " + Puddles.ToString("F2") + ", drops on the glass " + drops.Count + " (" + drops.Running + " running, "
                + Mathf.RoundToInt(100f * Glass) + "% of it under water), air past the glass " + airSaid.ToString("F1") + " m/s" + (leeSaid > 0.05f ? " (" + Mathf.RoundToInt(100f * leeSaid) + "% in the camera's lee)" : "")
                + (shader == null && tried ? " (not drawn: " + why + ")" : "");

            // ---- the pass, while there is anything for it
            bool wanted = glass > 0.5f || ground > 0.02f;
            if (wanted && !tried) LoadShader();
            if (!wanted || shader == null) { Off(); return; }
            if (material == null) material = new Material(shader) { name = "Natural Weather: rain on the glass and the ground" };
            if (pass == null) pass = new CommandBuffer { name = "Natural Weather: rain on the glass" };
            if (groundPass == null)
            {
                groundPass = new CommandBuffer { name = "Natural Weather: rain on the ground" };
                glassLeftOut = new MaterialPropertyBlock(); glassLeftOut.SetVector(idGlass, Vector4.zero);
                // (and the mist and spots, drawn by the glass's pass alone: drawn by the ground's as well they were laid on twice)
                glassLeftOut.SetTexture(idMist, Texture2D.blackTexture);
                groundLeftOut = new MaterialPropertyBlock(); groundLeftOut.SetVector(idGround, Vector4.zero);
            }
            if (on != cam) { Off(); cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, groundPass); cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, pass); on = cam; }
            drawGround = ground; drawPuddles = puddles; drawGlass = glass; drawRain = rainNow * under;
        }

        float drawGround, drawPuddles, drawGlass, drawRain;
        // (the drops on the glass, one by one; and the camera's motion through the air, which drives them)
        readonly Drops drops = new Drops();
        // (water thrown at the glass: spray off wheels on the wet ground, splashes, a hull's sheet; 2026-11-07)
        readonly Thrown thrown = new Thrown();
        static Shader dropShader;
        Material dropMaterial;
        Vector3d lastRel;
        bool followed;
        Vector3 camMoving, camShaken;
        static readonly int idSoft = Shader.PropertyToID("_WetSoft");
        static readonly int idLook = Shader.PropertyToID("_WetLook");
        static readonly int idDrops = Shader.PropertyToID("_WetDrops"), idDropsSize = Shader.PropertyToID("_WetDropsSize"), idMist = Shader.PropertyToID("_WetMist");


        // (the glass and the sea: whether it was under water last frame, how far above the water it stood, and how fast it
        // last came out)
        bool under;
        float lastAbove = float.PositiveInfinity, surfacedAt = -1f;
        static Func<Vector3d, float> seaHeight;
        static bool seaLooked;

        /// <summary>
        /// How far a place stands above the water there, m (below nought: under it). The water is Natural Ocean's, waves and
        /// all, where that mod is installed (looked up by name: this mod does not need it); else the sea's own level. No sea:
        /// infinitely far.
        /// </summary>
        static bool keystoneWater = true;
        static bool KeystoneWater(Vector3d at, out float height)
        {
            bool there = Keystone.Water.Provider != null;
            height = there ? Keystone.Water.HeightAt(at) : 0f;
            return there;
        }

        internal static float AboveWater(CelestialBody b, Vector3d at)
        {
            if (b == null || !b.ocean) return float.PositiveInfinity;
            double alt = b.GetAltitude(at);
            if (alt > 60.0) return (float)alt;
            // (Keystone's water, which a sea mod answers for, from Keystone 0.9.0; else the sea mod looked up by name, as before)
            if (keystoneWater)
            {
                try { if (KeystoneWater(at, out float h)) return (float)alt - h; }
                catch (Exception) { keystoneWater = false; }    // (a Keystone older than 0.9.0)
            }
            if (!seaLooked)
            {
                seaLooked = true;
                try
                {
                    Type sea = typeof(Wet).Assembly.GetType("NaturalOcean.Sea");
                    if (sea == null)
                        foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                            if (a?.assembly != null && (sea = a.assembly.GetType("NaturalOcean.Sea")) != null) break;
                    System.Reflection.MethodInfo m = sea?.GetMethod("HeightAt", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(Vector3d) }, null);
                    if (m != null && m.ReturnType == typeof(float)) seaHeight = (Func<Vector3d, float>)Delegate.CreateDelegate(typeof(Func<Vector3d, float>), m);
                }
                catch (Exception) { seaHeight = null; }
            }
            float water = 0f;
            if (seaHeight != null)
            {
                try { water = seaHeight(at); }
                catch (Exception) { seaHeight = null; }
            }
            return (float)alt - water;
        }

        // (what the drops' step needs from the frame's work, and the camera as it was last drawn)
        float stepRain, stepSnow, flakesSaid, airSaid, arriving, facingSaid, leeSaid;
        // (the glass's warmth, kelvin, and the air's and its dew point's where the camera is: queue item 20's seventh gap)
        float glassT = float.NaN, airTSaid, dewTSaid;
        bool stepKeep;
        Vector3 stepWind;
        int steppedFrame = -1;

        /// <summary>
        /// The drops carried on through the frame, from where the camera really is as it draws (the game moves its flight
        /// camera after this component's LateUpdate: read there, a camera held still by another mod read as moving at
        /// hundreds of metres a second, and every drop was blown off the glass).
        /// </summary>
        void StepDrops(Camera cam)
        {
            if (Time.frameCount == steppedFrame) return;
            steppedFrame = Time.frameCount;
            if (!stepKeep) { drops.Clear(); return; }
            Vector3 camPos = cam.transform.position;
            Vessel ship = FlightGlobals.ActiveVessel;
            float dtGame = Mathf.Min(Time.deltaTime, 0.05f);
            // (Under water the glass is all water, and holds no drops. Coming up out of it, it is left a sheet of water that
            // breaks into drops, more or fewer by how fast it came out: Drops.Surfaced. Where the water stands is Natural
            // Ocean's, with its waves, where that mod is here; else the sea's own level.)
            float above = AboveWater(FlightGlobals.currentMainBody, (Vector3d)camPos);
            if (above < 0f)
            {
                drops.Clear();
                if (!under) under = true;
                lastAbove = above; Glass = 0f; arriving = 0f;
                return;
            }
            if (under)
            {
                under = false;
                surfacedAt = Mathf.Max((above - lastAbove) / Mathf.Max(dtGame, 1e-3f), 0f);
                drops.Surfaced(surfacedAt, cam.aspect);
            }
            lastAbove = above;
            Transform t = cam.transform;
            // (the camera's own motion through the air: the craft's over the ground and the camera's round the craft. A jump of
            // more than ten metres in a frame is a cut, not a motion: the camera put somewhere else. The camera's round the
            // craft is taken about the point it turns round (the game's pivot), not about the craft's centre of mass as until
            // 2026-11-07: the pivot goes with the craft as it is drawn, between the physics steps, while the centre of mass is
            // where the last step left it, so the camera's place against the centre of mass shook by as much as the craft
            // goes in a frame, and the glass felt jerked back by up to 20 m/s^2 behind a rover going a steady 9 m/s (more the
            // faster, and so up to 300 behind a plane), flinging off its larger drops all the time (Drops.Flung;
            // recordings/2026-11-07-pc/rainrover3: 1049 flung in four seconds of a steady run). And faster than 100 m/s round
            // the pivot is a cut too: the camera set somewhere else a few metres off, as a change of the camera's mode does,
            // was a jerk of 20 m/s^2.) And from 2026-11-09 the camera's motion round the craft is not counted at all (CamWay 0):
            // swung round the craft by the player, or drawn in and out, the camera changes its view, as one panned on its stand
            // does, and does not fly through the air. Counted, a swing of sixty degrees a second 25 m off the craft was air at
            // 26 m/s along the glass, and blew every drop off it each time the view was turned (2026-11-09: the glass all but
            // clear after a swing of four seconds, recordings/2026-11-09-pc/drops-before-swing), where the owner found the
            // drops to "act less realistic". The craft's own motion is the camera's: its speed through the air and the rain, its
            // jolts (a crash, a launch) shaking the drops.
            Vector3 moving = Vector3.zero;
            if (ship != null && CamWay == 0)
            {
                moving = ship.srf_velocity.sqrMagnitude < 1e10 ? (Vector3)ship.srf_velocity : Vector3.zero;
                followed = false;
            }
            else if (ship != null && Time.deltaTime > 1e-4f)
            {
                Transform pivot = FlightCamera.fetch != null ? FlightCamera.fetch.GetPivot() : null;
                bool aboutPivot = pivot != null && CamWay == 1;
                Vector3d rel = (Vector3d)camPos - (aboutPivot ? (Vector3d)pivot.position : ship.CoMD);
                double went = (rel - lastRel).sqrMagnitude, most = aboutPivot ? Math.Min(100.0, 1e4 * Time.deltaTime * Time.deltaTime) : 100.0;
                if (followed && went < most) moving = (Vector3)ship.srf_velocity + (Vector3)((rel - lastRel) / Time.deltaTime);
                else moving = ship.srf_velocity.sqrMagnitude < 1e10 ? (Vector3)ship.srf_velocity : Vector3.zero;
                lastRel = rel; followed = true;
            }
            else followed = false;
            Vector3 wasMoving = camMoving;
            camMoving = Vector3.Lerp(camMoving, moving, 1f - Mathf.Exp(-dtGame / 0.05f));
            if (dtGame > 1e-4f) camShaken = Vector3.Lerp(camShaken, (camMoving - wasMoving) / dtGame, 1f - Mathf.Exp(-dtGame / 0.1f));
            Vector3 wind = stepWind;
            Vector3 air = wind - camMoving;
            Vector3 g = (Vector3)FlightGlobals.getGeeForceAtPosition((Vector3d)camPos);
            Vector3 shaken = Vector3.ClampMagnitude(camShaken, 30f);
            float dense = ship != null ? (float)(ship.atmDensity / 1.225) : 1f;
            // (The rain as the glass meets it: so many drops a square metre a second at the ground (Marshall and Palmer's
            // sizes, at the rate the rain falls, 50 mm an hour for a cloudburst), falling at six and a half metres a second
            // with the wind, less the camera's own motion. The glass takes what comes at its face: looking up into the rain,
            // all of it; looking straight ahead in still air, next to none; looking down, or the way the wind drives the
            // rain, none; looking into the wind, or flying into the rain, more than looking up, as it comes at the glass
            // faster. And a very little besides, brought in by the eddies round the camera's body: a fiftieth of what
            // goes past its face.)
            float rainRate = 50f * stepRain;
            float flux = rainRate > 0.25f ? 1200f * Mathf.Pow(rainRate / 5f, 0.7f) : 0f;
            // (the game's gravity points down: the rain falls along it)
            Vector3 rainMeets = wind + g.normalized * 6.5f - camMoving;
            float facing = Mathf.Max(0f, -Vector3.Dot(rainMeets, t.forward)) / 6.5f;
            float area = Drops.Tall * 1e-3f * Drops.Tall * 1e-3f * cam.aspect;
            Vector3 past = rainMeets - t.forward * Vector3.Dot(rainMeets, t.forward);
            float arrive = flux * area * (facing + 0.02f * past.magnitude / 6.5f) * Weather.Times(Weather.GlassGather);
            arriving = arrive; facingSaid = facing;
            float lambda = 4.1f * Mathf.Pow(Mathf.Max(rainRate, 0.5f), -0.21f);
            // (Snow as the glass meets it (2026-11-07): the flakes fall at about a metre a second, so the wind drives them far
            // more than rain. How many in a cubic metre of air: the snow's water a second (as Snow reckons it: above) over a
            // flake's mean water, falling at that metre a second. Their sizes, melted, after Sekhon and Srivastava: their number
            // falls off as e^(-lambda D), lambda 2.29 R^-0.45 per mm for R mm of water an hour. How many of those that come at
            // the glass stay on it: wet snow sticks where dry snow mostly bounces off, and the faster they come the fewer stay,
            // as snow builds up on power lines (Admirat: wet snow's sticking about 1/U for U metres a second): as many stay as
            // would come at a metre a second, at most, all of them wet, a tenth dry, from four degrees under. Wet: by the warmer
            // of the air and the glass, a flake landing on glass above freezing wetted by it. As first written, every flake
            // that came at the glass, and two thirds of them at -5 C, the glass was white with snow in 25 seconds facing a
            // 15 m/s wind in moderate snow (2026-11-07, recordings/2026-11-07-pc/snow-set-d3d).)
            float snowRate = stepSnow, flakes = 0f, flakeLambda = 1f;
            if (snowRate > 0.02f && Drops.SnowOn)
            {
                Vector3 snowMeets = wind + g.normalized * 1f - camMoving;
                float snowInto = Mathf.Max(0f, -Vector3.Dot(snowMeets, t.forward));
                Vector3 snowPast = snowMeets - t.forward * Vector3.Dot(snowMeets, t.forward);
                flakeLambda = 2.29f * Mathf.Pow(snowRate, -0.45f);
                // (a flake's mean water, mg: its melted size's mean cube, the sizes from 0.2 mm on; held to the largest's, 3.5 mm)
                float least = 0.2f, fl = flakeLambda;
                float meanMg = Mathf.Min(Mathf.PI / 6f * (least * least * least + 3f * least * least / fl + 6f * least / (fl * fl) + 6f / (fl * fl * fl)), 22f);
                float perCubicMetre = snowRate * 278f / Mathf.Max(meanMg, 0.05f);
                float wetC = (float.IsNaN(glassT) ? airTSaid : Mathf.Max(airTSaid, glassT)) - 273.15f;
                float stick = 0.1f + 0.9f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((wetC + 4f) / 4.5f));
                flakes = perCubicMetre * area * (Mathf.Min(snowInto, 1f) + 0.02f * snowPast.magnitude) * stick * Weather.Times(Weather.GlassGather);
            }
            flakesSaid = flakes;
            // (The air along the glass: the wind past the camera, as much of it as lies along the glass. Coming at the glass
            // across or face-on, it streams over it and the drops run its way, or spread from the middle; coming from behind
            // the camera, the glass is in the lee of the camera's own body, where the air is slow and turns back on itself: a
            // camera with the gale at its back keeps its drops where they are. Asked by the owner, 2026-10-17 18:40: "the wind
            // should blow accordingly on the raindrops on the camera".)
            float airSpeed = air.magnitude;
            float fromBehind = airSpeed > 0.1f ? Vector3.Dot(air, t.forward) / airSpeed : 0f;
            float lee = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((fromBehind - 0.1f) / 0.6f));
            Vector2 along = new Vector2(Vector3.Dot(air, t.right), Vector3.Dot(air, t.up)) * (1f - 0.85f * lee) * Weather.Times(Weather.GlassBlow);
            // (and the water thrown at it that it meets: Thrown, 2026-11-07)
            CelestialBody thrownOn = FlightGlobals.currentMainBody;
            if (thrownOn != null)
            {
                thrown.Meet((Vector3d)camPos - thrownOn.position, camMoving, t.forward, area, dtGame);
                if (thrown.Meeting > 0f) drops.Thrown(thrown.Landing, thrown.Hit, cam.aspect);
            }
            // (and how hard the glass is jerked back from its drops: the eighth gap)
            drops.Flung = Mathf.Max(0f, -Vector3.Dot(Vector3.ClampMagnitude(camShaken, 300f), t.forward));
            drops.Step(dtGame, new Vector2(Vector3.Dot(g, t.right), Vector3.Dot(g, t.up)) * Weather.Times(Weather.GlassRun), along,
                            Mathf.Max(0f, -Vector3.Dot(air, t.forward)), new Vector2(Vector3.Dot(shaken, t.right), Vector3.Dot(shaken, t.up)), dense, arrive, lambda,
                            flakes, flakeLambda, stepRain > 0.01f ? 0f : 1f, cam.aspect);
            airSaid = along.magnitude; leeSaid = lee;
            Glass = drops.Covered;
        }

        static Wet instance;
        void Awake() { instance = this; Camera.onPreCull += BeforeCamera; GameEvents.onCrashSplashdown.Add(CrashedInWater); }

        // (The game's crash on splashdown: a part broken by striking the water, which may be gone before it is seen in it. An
        // instance's method: the game's event list reads the handler's owner.)
        void CrashedInWater(EventReport report)
        {
            try
            {
                Part p = report != null ? report.origin : null;
                if (p == null || p.vessel == null) return;
                Rigidbody rb = p.rb;
                Vector3 moving = rb != null ? rb.velocity + (Vector3)Krakensbane.GetFrameVelocity() : (Vector3)p.vessel.srf_velocity;
                thrown.Struck(p.vessel.mainBody, p, moving, Planetarium.GetUniversalTime());
            }
            catch (Exception ex) { Debug.Log("[Natural Weather] a splash's water for the glass failed: " + ex.Message); }
        }

        /// <summary>
        /// Just before the camera draws: the pass written out for where the camera then stands and looks. (The game moves
        /// its flight camera after this component's LateUpdate: written there, the ground's shine and its pools were
        /// laid out for the camera of the frame before, and swam over the ground whenever the camera turned.)
        /// </summary>
        void BeforeCamera(Camera cam)
        {
            if (cam == null || cam != on || pass == null || groundPass == null || material == null) return;
            StepDrops(cam);
            CelestialBody b = FlightGlobals.currentMainBody;
            if (b == null) return;
            World w = World.Of(b);
            Vector3 camPos = cam.transform.position;
            double camLat = b.GetLatitude(camPos), camLon = b.GetLongitude(camPos);
            float ground = drawGround, puddles = drawPuddles, glass = drawGlass;
            Transform t = cam.transform;
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
            Vector3 r = t.right * tanX, u = t.up * tanY, f = t.forward;
            Vector3d camRel = (Vector3d)camPos - b.position;
            // (the ground's own two level ways, at a place fixed to the ground: the rings in the puddles are read along them)
            Anchor(b, w, camLat, camLon);
            Vector3d anchor = b.GetWorldSurfacePosition(anchorLat, anchorLon, anchorAlt);
            LevelWays(b, anchor, anchorTurn, out Vector3d east, out Vector3d north);
            Vector3d from = (Vector3d)camPos - anchor;
            double hereEast = carriedEast + Vector3d.Dot(from, east), hereNorth = carriedNorth + Vector3d.Dot(from, north);
            Provider.Axes(b, camPos, out Vector3d e, out Vector3d n, out Vector3d up);
            Vector3 down = -(Vector3)up;
            Vector2 downOnGlass = new Vector2(Vector3.Dot(down, t.right), Vector3.Dot(down, t.up));
            // (the sky as a mirror shows it where nothing better is known: as bright as a white thing is in the light there is, greyed)
            Color lit = Rain.SnowLit * 0.7f;
            material.SetVector("_WetRight", new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
            material.SetVector("_WetUp", new Vector4(u.x, u.y, u.z, Time.time % 3600f));
            material.SetVector("_WetAhead", new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
            material.SetVector("_WetCam", new Vector4((float)camRel.x, (float)camRel.y, (float)camRel.z, (float)b.Radius));
            // (and in its first two where the camera stands in the pattern, in metres, for the OpenGL wet.bundle packed before
            // 2026-10-28 alone, which git holds until the Mac packs it again from wet.glsl: this shader reads the camera's place
            // from _WetPattern, _WetPatternSmall and _WetRipples, that one from these, and with nought here its pools went along with the camera;
            // the review of 2026-10-28)
            material.SetVector("_WetHere", new Vector4((float)hereEast, (float)hereNorth, 0f, b.ocean ? 1f : 0f));
            // (the planet's own three ways in the scene, turning with it, and where the camera stands along them in each size of
            // the hollows' cells: the pools' pattern is read through the planet in three dimensions, so a place has the same
            // pools however the camera came there. Counted in cells and kept under the ten thousand after which the shader's
            // pattern repeats, worked out here in double, as fine on the far side of the planet as at the pad)
            Vector3d px = (b.GetWorldSurfacePosition(0.0, 0.0, 0.0) - b.position).normalized, py = (b.GetWorldSurfacePosition(0.0, 90.0, 0.0) - b.position).normalized;
            Vector3d pz = (b.GetWorldSurfacePosition(90.0, 0.0, 0.0) - b.position).normalized;
            Vector3d inPlanet = new Vector3d(Vector3d.Dot(camRel, px), Vector3d.Dot(camRel, py), Vector3d.Dot(camRel, pz));
            material.SetVector("_WetPlanetX", (Vector3)px);
            material.SetVector("_WetPlanetY", (Vector3)py);
            material.SetVector("_WetPlanetZ", (Vector3)pz);
            material.SetVector("_WetPattern", new Vector4(Cell(inPlanet.x, 0.12, 0.0), Cell(inPlanet.y, 0.12, 0.0), Cell(inPlanet.z, 0.12, 0.0), 0f));
            material.SetVector("_WetPatternSmall", new Vector4(Cell(inPlanet.x, 0.47, 17.0), Cell(inPlanet.y, 0.47, 17.0), Cell(inPlanet.z, 0.47, 17.0), 0f));
            // (and in the rings' two sizes of cell, along the two level ways fixed to the ground near the camera, carried as the
            // camera goes: Anchor)
            material.SetVector("_WetRipples", new Vector4(Cell(hereEast, 2.6, 0.0), Cell(hereNorth, 2.6, 0.0), Cell(hereEast, 4.3, 11.7), Cell(hereNorth, 4.3, 11.7)));
            material.SetVector("_WetAcross", (Vector3)east);
            material.SetVector("_WetAlong", (Vector3)north);
            // (How far off the wet look fades out: 600 m, and never past where this camera's own sight ends. On OpenGL the
            // game draws the flight scene with two cameras, and this one sees 400 m: with 600 m the sheen and the pools
            // stopped at a ruled line across the ground where the other camera takes over.)
            material.SetVector("_WetGround", new Vector4(ground, puddles, drawRain, Mathf.Min(600f, 0.95f * cam.farClipPlane)));
            material.SetVector("_WetSky", new Vector4(lit.r, lit.g, lit.b, 1.2f));
            material.SetVector("_WetGlass", new Vector4(glass, cam.aspect, downOnGlass.x, downOnGlass.y));
            // (where the brightest light lies on the glass: towards the sun while it is up and comes through, else straight up)
            CelestialBody sunBody = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            Vector3 toSun = sunBody != null ? (Vector3)(sunBody.position - (Vector3d)camPos).normalized : (Vector3)up;
            float sunUp = Vector3.Dot(toSun, (Vector3)up);
            float sunStrong = Mathf.Clamp01(sunUp * 4f + 0.2f) * Mathf.Clamp01(Clouds.SunThrough);
            Vector2 sunOnGlass = new Vector2(Vector3.Dot(toSun, t.right), Vector3.Dot(toSun, t.up));
            Vector2 upOnGlass = -downOnGlass;
            Vector2 light = Vector2.Lerp(upOnGlass.sqrMagnitude > 1e-4f ? upOnGlass.normalized : Vector2.up, sunOnGlass.sqrMagnitude > 1e-4f ? sunOnGlass.normalized : Vector2.up, sunStrong);
            light = light.sqrMagnitude > 1e-4f ? light.normalized : Vector2.up;
            // (the lens's opening, as an automatic camera opens it for the light: 0.6 mm in daylight, wider as the light falls, to 3
            // at night, where a drop is a soft round blur (research/lens-drops/understand.md: a drop on the lens is drawn through the
            // whole of the lens's opening). Measured as the radius of the patch of glass each point of the picture is seen through
            // (wet.glsl's outer lines of sight lie so far out from the middle one), half the width of the lens's own opening: on this
            // glass, 60 mm high across the picture, a radius of r mm blurs a point over 2r/60 of the picture's height. A wide lens
            // like the game's, some 21 mm on a full-frame camera, is 1.3 mm across at f/16 in bright day (r 0.65), 2.6 at f/8 (1.3)
            // and 7.5 wide open at f/2.8 (3.7). Until 2026-11-09 0.45 in daylight (f/23) to 2.6; from then the scene seen along
            // each line of sight is smeared as the water bends it across the line's share of the opening (wet.glsl), and with a
            // little more opening by day the drops show as soft round beads, not the cut crystals of before (recordings/
            // 2026-11-09-pc/dropstills2; wider, 1.2 mm by day, they all but vanished over even ground). Opened and closed over a
            // second or so, as a camera's iris follows the light, not at once.)
            float seen = Mathf.Max(0.2126f * lit.r + 0.7152f * lit.g + 0.0722f * lit.b, 0f) / 0.7f;
            // (By the light itself since 2026-11-09 (later), not by how bright the picture shows it: a camera's meter reads light,
            // and the picture's brightness is the light's some 2.2th root. By the picture's brightness the opening at night was
            // 1.11 mm everywhere, never near the 3 it was meant to reach, the game's night being as bright as it is (0.18 of the
            // day's in the picture, a thirtieth in light): a drop was drawn through a narrow opening as a dark stain with a rim,
            // where a real camera at night, wide open, shows it as a soft blur that hardly darkens the picture. Now 2.06 mm on
            // such a night; by day as before, 0.6.)
            float wants = Mathf.Clamp(OpenInDay * Mathf.Sqrt(Mathf.GammaToLinearSpace(0.6f) / Mathf.Max(Mathf.GammaToLinearSpace(Mathf.Clamp01(seen)), 0.0004f)), OpenInDay, OpenAtMost);
            opening = opening <= 0f ? wants : Mathf.Lerp(opening, wants, 1f - Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.2f) / 0.8f));
            float shownOpening = opening;
#if DEV
            if (HeldOpening >= 0f) shownOpening = HeldOpening;
#endif
            material.SetVector("_WetSun", new Vector4(light.x, light.y, sunStrong, shownOpening));
            material.SetVector(idLook, new Vector4(Look.x, Look.y, Look.z, 1f));
            // (the picture's lights for the drops: see LightsAtNight)
            float day2 = OpenInDay * OpenInDay;
            lights = LightsAtNight * Mathf.Clamp01((shownOpening * shownOpening - day2) / Mathf.Max(LightsFullAt * LightsFullAt - day2, 1e-3f));
#if DEV
            if (HeldLights >= 0f) lights = HeldLights;
#endif
            material.SetVector(idLights, new Vector4(lights, 0f, LightsAtMost, 0f));
            if (softOnly == null)
            {
                softOnly = new MaterialPropertyBlock();
                softOnly.SetVector(idGround, Vector4.zero);
                softOnly.SetVector(idGlass, Vector4.zero);
                softOnly.SetTexture(idMist, Texture2D.blackTexture);
                softOnly.SetTexture(idSoft, Texture2D.blackTexture);
            }
            softOnly.SetVector(idLights, new Vector4(lights, 1f, LightsAtMost, 0f));
            // (one camera, the sky not yet drawn when the ground's pass runs: the frame before's picture, where it was kept then
            // by this camera at this size, or earlier in this frame by a render of it before this one. Kept only while there
            // are pools to mirror it, which takes standing water and the depth picture: on damp ground alone, in a drizzle or
            // for the quarter of an hour the ground stays damp after the pools are gone, the copy was made every frame for
            // nothing, the review of 2026-10-28)
            Camera farCam = Names.CameraNamed("Camera 01");
            bool oneCamera = farCam == null || farCam == cam, keep = oneCamera && ground > 0.01f && puddles > 0.02f && depth;
            bool kept = keep && last != null && last.IsCreated() && lastFar != null && lastFar.IsCreated()
                && (lastFrame == Time.frameCount - 1 || lastFrame == Time.frameCount) && last.width == cam.pixelWidth && last.height == cam.pixelHeight;
            material.SetVector("_WetLastRight", new Vector4(lastRight.x, lastRight.y, lastRight.z, oneCamera ? 1f : 0f));
            material.SetVector("_WetLastUp", new Vector4(lastUp.x, lastUp.y, lastUp.z, 0f));
            material.SetVector("_WetLastAhead", new Vector4(lastAhead.x, lastAhead.y, lastAhead.z, kept ? 1f : 0f));
            if (kept) { material.SetTexture(idLast, last); material.SetTexture(idLastFar, lastFar); }
            pass.Clear();
            groundPass.Clear();
            // (and this frame's kept for the next, first in the glass's pass, before its drops)
            if (keep)
            {
                if (last == null || last.width != cam.pixelWidth || last.height != cam.pixelHeight)
                {
                    ForgetLast();
                    last = new RenderTexture(cam.pixelWidth, cam.pixelHeight, 0, RenderTextureFormat.DefaultHDR) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the picture kept for the pools" };
                    last.Create();
                    lastFar = new RenderTexture(cam.pixelWidth, cam.pixelHeight, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: how far off the kept picture's pixels were" };
                    lastFar.Create();
                }
                pass.Blit(BuiltinRenderTextureType.CameraTarget, last);
                // (how far off each pixel was, beside it: the next frame's pools take from the kept picture only what was far
                // off then too. Without it, as the camera went round a craft on wet ground, the pools showed a band in the
                // craft's colour where it had been a frame before, where now there is sky; the review of 2026-10-28)
                if (farOnly == null) farOnly = new MaterialPropertyBlock();
                farOnly.SetVector(idGround, Vector4.zero);
                farOnly.SetVector(idGlass, Vector4.zero);
                farOnly.SetVector(idHere, new Vector4(0f, 0f, 1f, 0f));
                pass.SetGlobalTexture(idScene, last);
                pass.SetRenderTarget(lastFar);
                pass.DrawMesh(sheet, Matrix4x4.identity, material, 0, 0, farOnly);
                pass.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                lastFrame = Time.frameCount; lastRight = r; lastUp = u; lastAhead = f;
            }
            else ForgetLast();
            // (the drops first, into their own picture of how high the water stands: the picture's own size (DropsFull))
            bool dropsDrawn = drops.Count > 0 && dropShader != null;
            if (dropsDrawn)
            {
                if (dropMaterial == null) dropMaterial = new Material(dropShader) { name = "Natural Weather: drops on the glass" };
                drops.Draw(pass, dropMaterial, cam.aspect, DropsFull ? Mathf.Clamp(cam.pixelHeight, 180, 2160) : Mathf.Clamp(cam.pixelHeight / 2, 180, 600), 1f / 60f);
                material.SetTexture(idDrops, drops.Picture);
                material.SetVector(idDropsSize, new Vector4(1f / drops.Picture.width, 1f / drops.Picture.height, Drops.Tall / drops.Picture.height, 1f));
            }
            else material.SetVector(idDropsSize, Vector4.zero);
            // (and the mist on the glass: black where there is none)
            material.SetTexture(idMist, drops.MistPicture());
            // The ground's half with the solid things, so that smoke, flame, spray and the sea are drawn over its pools as over
            // the ground itself, and the haze and the rain's veil laid on them as on it (straight after the solid things, as the
            // clouds' shadow is: Scatterer copies the picture after that and lays its haze on from the copy before anything
            // see-through is drawn, wiping what came between); the glass's over all of it. (One pass over the finished
            // picture, as until 2026-10-21, found the ground by the depth picture, which nothing see-through is in, and laid
            // the pools' mirror over whatever stood in front of it: a burst's fire over wet ground in a storm had the pools
            // behind it drawn across its lower part, rows of flat blocks in the sky's grey, and in the fire's own colour with
            // drops ringing them; the owner's "rectangles and squares showing through the fire and smoke", 2026-10-21.
            // research/rendering/experiments/fire-slabs.md. The pools now mirror what is drawn before them, the sky and the
            // far camera's picture, not a fire in front of the near camera. With one camera they mirror what lies far off
            // from the frame before's finished picture (`last`), and smoke and fire standing against the sky are in that.)
            if (ground > 0.01f) Lay(groundPass, glassLeftOut);
            // (the glass's from the copy just kept, where one was: the drops are drawn into their own picture, so the camera's is
            // as it was then)
            if (glass > 0.5f) Lay(pass, groundLeftOut, keep ? last : null, dropsDrawn ? cam : null);
            // (and where the drops were drawn but the glass is not laid, the camera's own picture drawn into again after this:
            // left on the drops' picture, whatever followed in the frame went into it. The frame the camera comes out of the sea
            // with the ground wet and no rain, the review of 2026-10-28)
            else if (dropsDrawn) pass.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }

        /// <summary>
        /// The place the rings in the puddles are laid out from (and the puddles themselves, by git's OpenGL wet.bundle until it is
        /// packed again from wet.glsl), for the camera now at camLat, camLon: the point of the planet's grid of a tenth of a degree
        /// nearest the craft after a load or a jump of more than 5 km in a frame (or on another body), else carried on.
        /// Carried, the anchor is moved under the camera whenever the camera is a kilometre from it, where the camera then
        /// stands in the pattern (carriedEast, carriedNorth) and the pattern's level ways laid onto the ground there and turned
        /// so as to keep their bearings (anchorTurn, from the ground's own east): nothing laid out from it moves by more than a
        /// few millimetres as it is moved, where before every pool in sight changed at once.
        /// </summary>
        void Anchor(CelestialBody b, World w, double camLat, double camLon)
        {
            bool jumped = double.IsNaN(anchorLat) || anchorBody != b.bodyName || double.IsNaN(lastCamLat) || Model.Distance(w, lastCamLat, lastCamLon, camLat, camLon) > 5000.0;
            lastCamLat = camLat; lastCamLon = camLon;
            if (jumped)
            {
                // (the grid's point nearest the craft, not the camera: laid afresh, the pattern starts from that point, and the
                // camera stands off the craft, on either side of one of the grid's lines. The same craft on the pad showed other
                // pools from a camera put 2.3 km off it, the stills of 2026-10-28, while the pools were laid out from here as
                // the rings still are (git's old OpenGL wet.bundle lays them so until it is packed again). Where the craft is now, not its latitude as
                // last worked out: set down far off, it still held the place before for the frame the camera came after it)
                Vessel v = FlightGlobals.ActiveVessel;
                bool craft = v != null && v.mainBody == b;
                Vector3d at = craft ? (Vector3d)v.transform.position : Vector3d.zero;
                anchorLat = Math.Round((craft ? b.GetLatitude(at) : camLat) * 10.0) / 10.0; anchorLon = Math.Round((craft ? b.GetLongitude(at) : camLon) * 10.0) / 10.0;
                anchorAlt = Math.Max(0.0, b.TerrainAltitude(anchorLat, anchorLon, true));
                anchorTurn = 0.0; carriedEast = carriedNorth = 0.0; anchorBody = b.bodyName;
                anchorFresh++;
                return;
            }
            if (Model.Distance(w, anchorLat, anchorLon, camLat, camLon) <= 1000.0) return;
            // (the anchor's height the ground's under the camera, not the sea's: a ring's height over the plane its pattern is
            // read on leans into the pattern by the plane's tilt, which is small near the anchor, and changes as it moves)
            Vector3d old = b.GetWorldSurfacePosition(anchorLat, anchorLon, anchorAlt);
            LevelWays(b, old, anchorTurn, out Vector3d oldEast, out Vector3d oldNorth);
            double alt = Math.Max(0.0, b.TerrainAltitude(camLat, camLon, true));
            Vector3d now = b.GetWorldSurfacePosition(camLat, camLon, alt);
            carriedEast += Vector3d.Dot(now - old, oldEast); carriedNorth += Vector3d.Dot(now - old, oldNorth);
            Provider.Axes(b, now, out Vector3d east, out Vector3d north, out Vector3d up);
            Vector3d laid = oldEast - up * Vector3d.Dot(oldEast, up);
            anchorTurn = Math.Atan2(Vector3d.Dot(laid, north), Vector3d.Dot(laid, east));
            anchorLat = camLat; anchorLon = camLon; anchorAlt = alt;
            anchorMoves++;
        }

        /// <summary>The pattern's two level ways at a place: the ground's own east and north there, turned by `turn` (radians).</summary>
        static void LevelWays(CelestialBody b, Vector3d at, double turn, out Vector3d east, out Vector3d north)
        {
            Provider.Axes(b, at, out Vector3d e, out Vector3d n, out Vector3d _);
            double c = Math.Cos(turn), s = Math.Sin(turn);
            east = e * c + n * s; north = n * c - e * s;
        }

        /// <summary>A place along one of the level ways (m) in a size of cell (cells a metre), plus a shift, as cells under ten thousand.</summary>
        static float Cell(double at, double perMetre, double plus)
        {
            double c = at * perMetre + plus;
            return (float)(c - Math.Floor(c / 10000.0) * 10000.0);
        }

        /// <summary>The picture so far copied (or the copy of it already made, given), and the shader laid over it with one of its halves left out.
        /// With `soft` (the camera, while there are drops on the glass), a copy at half its size besides, with its smaller copies:
        /// for wet.glsl to read the scene through the water as smoothed as the lens's opening smears it (2026-11-09), and to light
        /// the snow from (2026-11-07): what lies behind a flake and round it, spread wide and smooth, the lamps in it.
        /// (Twelve points read about each pixel from the full picture lit the flakes as the scene behind them, a little dimmer, and
        /// at night over the lamp-lit pad they were grey blots, like dirt on the glass: recordings/2026-11-07-pc/snow-set2-d3d.)</summary>
        void Lay(CommandBuffer into, MaterialPropertyBlock leftOut, RenderTexture copied = null, Camera soft = null)
        {
            if (copied != null) into.SetGlobalTexture(idScene, copied);
            else
            {
                into.GetTemporaryRT(idScene, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
                into.Blit(BuiltinRenderTextureType.CameraTarget, idScene);
            }
            if (soft != null)
            {
                // (written by the shader itself since 2026-11-09, with the picture's lights raised for the drops and how bright the
                // picture is as it stands beside them, in a fourth number: LightsAtNight; a plain copy until then)
                RenderTextureDescriptor half = new RenderTextureDescriptor(Mathf.Max(16, soft.pixelWidth / 2), Mathf.Max(16, soft.pixelHeight / 2), RenderTextureFormat.ARGBHalf, 0)
                { useMipMap = true, autoGenerateMips = true, sRGB = false };
                into.GetTemporaryRT(idSoft, half, FilterMode.Trilinear);
                bool plain = softOnly == null;
#if DEV
                plain |= PlainCopy;
#endif
                if (!plain)
                {
                    into.SetRenderTarget(idSoft);
                    into.DrawMesh(sheet, Matrix4x4.identity, material, 0, 0, softOnly);
                }
                else if (copied != null) into.Blit(copied, idSoft); else into.Blit(idScene, idSoft);
            }
            else into.SetGlobalTexture(idSoft, Texture2D.blackTexture);
            into.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            into.DrawMesh(sheet, Matrix4x4.identity, material, 0, 0, leftOut);
            if (soft != null) into.ReleaseTemporaryRT(idSoft);
            if (copied == null) into.ReleaseTemporaryRT(idScene);
        }
    }
}
