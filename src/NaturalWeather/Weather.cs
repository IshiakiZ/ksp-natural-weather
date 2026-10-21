using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

#if !DEV
[assembly: KSPAssembly("NaturalWeather", 0, 2)]
[assembly: KSPAssemblyDependency("Keystone", 0, 7)]
#endif

namespace NaturalWeather
{
    /// <summary>
    /// Natural Weather: one weather for every world that has air, the same for everyone at a given place and moment, that
    /// is both seen and felt. The wind that rocks a plane is the wind that bends the smoke beside it and slants the rain
    /// past it.
    ///
    /// Asked for as: "To-do list to add a weather mod", then "mostly fair weather, but there should be an ability to change
    /// the weather, how often, and all the settings"; lightning that can strike a rocket, with a button to forbid it; wind
    /// that can tip landed craft, with a toggle; a go / no go page with a forecast "like real launches"; a city's real
    /// weather from online, kept across sessions; the AI pilot knowing the wind.
    ///
    /// The weather itself is in Model (worked out from place and clock alone: see there). It is handed to every mod through
    /// Keystone's Weather (our own clouds draw from it, Natural Light dims the sun by it, the AI Bridge reads it), to the
    /// explosions' air for its smoke (Feed), and to the game's aerodynamics (Aero).
    /// </summary>
    static class Weather
    {
        public const string Version = "0.5.2";

        public static Mod Page;
        public static Toggle On, Push, Tip, Strike, HailBreaks, Icing, Light, Smoke, Rain, Drops, WetGround, LyingSnow, Flashes, Fog, Dust, Devils, UseCity;
        public static Toggle Kerbin, Duna, Eve, Laythe, Jool, Others;
        public static Choice Chosen;
        public static Slider Often, Pace, Wind, Gusts, Thunder, Patter, Whoosh;
        static readonly Provider provider = new Provider();

        public static void Setup()
        {
            if (Page != null && Kit.Find("Natural Weather") == Page) return;
            Page = Kit.Register("Natural Weather", Version, "Weather that is seen and felt: wind and gusts on craft and smoke, cloud and rain, storms and lightning, fog and dust, the same for every mod; a launch go / no go with a forecast; a real city's weather if you like.");
            On = Page.Toggle("on", "Natural Weather", true, "Off: still air and clear skies everywhere, as the game has it.");
            Page.Heading("The weather");
            Chosen = Page.Choice("weather", "Weather", 0, Model.Choices, "As it comes, or one kind held everywhere (eased in over a minute or two): for pictures, or for practising a crosswind landing.");
            Often = Page.Slider("often", "Bad weather", 0.25f, 0f, 1f, "", 2, "How often the weather turns bad: 0 never, 0.25 mostly fair (as installed), 0.5 like a temperate coast, 1 stormy.");
            Pace = Page.Slider("pace", "How fast it changes", 1f, 0.25f, 4f, "x", 2, "How quickly weather systems, storms and fog come and go.");
            Wind = Page.Slider("wind", "Wind", 1f, 0f, 2.5f, "x", 2, "How strong the wind is, everywhere.");
            Gusts = Page.Slider("gusts", "Gusts", 1f, 0f, 2.5f, "x", 2, "How rough the air is.");
            Page.Heading("Worlds");
            Kerbin = Page.Toggle("kerbin", "Kerbin", true);
            Laythe = Page.Toggle("laythe", "Laythe", true);
            Duna = Page.Toggle("duna", "Duna", true);
            Eve = Page.Toggle("eve", "Eve", true);
            Jool = Page.Toggle("jool", "Jool", true);
            Others = Page.Toggle("others", "Other worlds with air", true, "Worlds a planet pack adds.");
            Page.Heading("What it does");
            Push = Page.Toggle("push", "Wind moves craft", true, "Off: the weather is only seen, never felt.");
            Tip = Page.Toggle("tip", "Wind can tip landed craft", true, "Off: craft standing on the ground or the sea feel no wind.");
            Strike = Page.Toggle("strike", "Lightning can strike craft", true, "Off: no strikes. On: a craft in or climbing through a storm can be struck.");
            HailBreaks = Page.Toggle("hailbreaks", "Large hail can break panels", true, "Off: hail is only seen and heard. On: stones of 20 mm and more can break deployed solar panels, aerials and radiators.");
            Icing = Page.Toggle("icing", "Ice on craft", true, "In cloud colder than freezing, in freezing rain and in freezing fog, ice builds on a craft: it is heavier, drags more, and its wings lift less. Speed, engines and warmer air melt it off. Off: no ice.");
            Page.Heading("What is drawn");
            Light = Page.Toggle("light", "Cloud light", true, "Cloud dims the sun and greys the sky (through Natural Light).");
            Smoke = Page.Toggle("smoke", "Smoke in the wind", true, "Smoke and explosions (Volumetric Explosions) drift in the weather's wind.");
            Rain = Page.Toggle("rain", "Rain, snow, hail", true, "What falls, drawn round the camera: rain, snow, ice pellets and hail.");
            Drops = Page.Toggle("drops", "Drops on the camera", true, "Rain leaves drops on the camera's glass: they sit, run and dry. Off: the glass stays clear.");
            WetGround = Page.Toggle("wetground", "Wet ground", true, "Where rain has fallen the ground is darker and shines, and water stands on level ground until it dries.");
            LyingSnow = Page.Toggle("snowground", "Lying snow", true, "Where snow has fallen in the last days and it has stayed cold enough, the ground is white: in patches while it is thin, and from a line on the hills, deeper higher up, until it thaws; in a wind, its fine snow drifts along the ground in streamers. Seen from the ground and the air, not from orbit. The white ground on the Mac for now (its shader for Windows is still to be built); the drifting everywhere.");
            Flashes = Page.Toggle("lightning", "Lightning", true);
            Thunder = Page.Slider("thunder", "Thunder", 0.8f, 0f, 1f, "", 2, "How loud the thunder is.");
            Patter = Page.Slider("patter", "Rain's sound", 0.6f, 0f, 1f, "", 2, "How loud the rain is.");
            Whoosh = Page.Slider("whoosh", "Wind's sound", 0.4f, 0f, 1f, "", 2, "How loud the wind is, near the ground, in gusts; and the air rushing past a craft in flight, as hard as it presses on it.");
            Fog = Page.Toggle("fog", "Fog", true);
            Dust = Page.Toggle("dust", "Dust", true, "Dust storms (Duna).");
            Devils = Page.Toggle("devils", "Dust devils", true, "Whirlwinds of dust on a hot, still afternoon over dry ground: on Duna, and in Kerbin's deserts (with Volumetric Explosions 0.8.0 or later).");
            Page.Heading("A real city's weather");
            UseCity = Page.Toggle("usecity", "Use the city's weather", true, "With a city typed in below, Kerbin's weather round the space centre is that city's, from online.");
            Page.Changed += Apply;
            Page.Status = () => Status;
            Apply();
            City.Load();
        }

        public static string Status = "";

        public static void Log(string text) => Kit.Log("Natural Weather", text);

        /// <summary>The settings, into the model; and the weather handed to Keystone or taken back.</summary>
        public static void Apply()
        {
            Model.Often = Often; Model.Pace = Pace; Model.WindStrength = Wind; Model.GustStrength = Gusts;
            Model.Chosen = Chosen;
            Model.FogOn = Fog; Model.DustOn = Dust;
            Model.Allowed = Allowed;
            Overrides.UseCity = UseCity;
            Model.Forget();
            bool want = On;
            if (want && Keystone.Weather.Provider == null) Keystone.Weather.Provider = provider;
            else if (!want && Keystone.Weather.Provider == provider) Keystone.Weather.Provider = null;
        }

        static bool Allowed(CelestialBody body)
        {
            if (body == null || !body.atmosphere || On == null || !On) return false;
            switch (body.bodyName)
            {
                case "Kerbin": return Kerbin;
                case "Duna": return Duna;
                case "Eve": return Eve;
                case "Laythe": return Laythe;
                case "Jool": return Jool;
                default: return Others;
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class WeatherAtStart : MonoBehaviour
    {
        void Awake() { Provider.MainThread = System.Threading.Thread.CurrentThread.ManagedThreadId; Weather.Setup(); Destroy(gameObject); }
    }

    /// <summary>
    /// What runs in flight and at the space centre: keeps the model's knowledge of the worlds up to date (where the sun is,
    /// the land, the temperatures), eases a chosen weather in, and starts the parts that draw and push.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.FlightAndKSC, false)]
    public sealed class WeatherDriver : MonoBehaviour
    {
        public static WeatherDriver Instance;
        float statusAt;

        void Awake()
        {
            Instance = this;
            Provider.MainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Climate.Given();
            Weather.Setup();
            Weather.Page.Panel = Panel.Draw;
        }

        void Start()
        {
            Aero.Start();
            Feed.Start();
            if (HighLogic.LoadedSceneIsFlight) { gameObject.AddComponent<Rain>(); gameObject.AddComponent<Hail>(); gameObject.AddComponent<Rainbow>(); gameObject.AddComponent<Wet>(); gameObject.AddComponent<Snow>(); gameObject.AddComponent<Drift>(); gameObject.AddComponent<Icing>(); }
            gameObject.AddComponent<Lightning>();
        }

        void OnDestroy()
        {
            Feed.Stop();
            Model.WarpRate = 1f;
            if (Instance == this) Instance = null;
            if (Weather.Page != null && Weather.Page.Panel == (Action)Panel.Draw) Weather.Page.Panel = null;
        }

        void Update()
        {
            double ut = Planetarium.GetUniversalTime();
            Model.WarpRate = TimeWarp.fetch != null ? TimeWarp.CurrentRate : 1f;
            CelestialBody body = Here();
            if (body != null && body.atmosphere) World.Of(body).Keep(ut);
            // (a chosen weather is eased in, and out again, over about three quarters of a minute of the computer's time each
            // way; a change from one chosen weather to another goes out of the first before it comes into the second)
            int want = Weather.Chosen != null ? (int)Weather.Chosen : 0;
#if DEV
            if (Probe.Hold >= 0) want = -1;
#endif
            int have = Model.Chosen;
            if (want < 0) { }
            else
            {
            float was = Model.ChosenWeight, target;
            if (want == have) target = want != 0 ? 1f : 0f;
            else if (have != 0 && was > 0f) target = 0f;
            else { Model.Chosen = want; target = want != 0 ? 1f : 0f; }
            float now = Mathf.MoveTowards(was, target, Time.unscaledDeltaTime / 45f);
            if (now <= 0f && want == 0) Model.Chosen = 0;
            if (now != was) { Model.ChosenWeight = now; if (now == target) Model.Forget(); }
            }
            Feed.Start();
            Launch.Tick();
            City.Tick();
            if (Time.realtimeSinceStartup > statusAt) { statusAt = Time.realtimeSinceStartup + 0.5f; Weather.Status = Describe(); }
        }

        /// <summary>The world the camera or the craft is at.</summary>
        public static CelestialBody Here()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null) return FlightGlobals.ActiveVessel.mainBody;
            if (FlightGlobals.currentMainBody != null) return FlightGlobals.currentMainBody;
            return FlightGlobals.GetHomeBody();
        }

        /// <summary>Where the weather is read for the readout: the craft, or the space centre.</summary>
        public static bool Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl)
        {
            body = null; lat = lon = alt = 0.0; agl = 0f;
            Vessel v = FlightGlobals.ActiveVessel;
            if (HighLogic.LoadedSceneIsFlight && v != null)
            {
                body = v.mainBody; lat = v.latitude; lon = v.longitude; alt = v.altitude;
                agl = (float)Math.Max(0.0, v.heightFromTerrain >= 0 ? v.heightFromTerrain : v.altitude - Math.Max(0.0, v.terrainAltitude));
                return body != null;
            }
            body = FlightGlobals.GetHomeBody();
            if (body == null) return false;
            lat = -0.0972; lon = -74.5577; alt = 70.0; agl = 10f;
            return true;
        }

        static string Describe()
        {
            if (Weather.On == null || !Weather.On) return "Off: still air and clear skies.";
            if (!Place(out CelestialBody body, out double lat, out double lon, out double alt, out float agl)) return "";
            if (!body.atmosphere) return "No air here: no weather.";
            if (!Model.Allowed(body)) return "Weather is switched off on " + body.bodyName + ".";
            World w = World.Of(body);
            double ut = Planetarium.GetUniversalTime();
            WeatherHere here = Model.Here(w, lat, lon, ut);
            Vector3 wind = Provider.Local(w, lat, lon, alt, agl, ut, false);
            string violent = Violent.Says(w, lat, lon, ut);
            return Panel.Words(here) + ", wind " + new Vector2(wind.x, wind.y).magnitude.ToString("F0") + " m/s from the " + CityWeather.Compass(Mathf.Atan2(-wind.x, -wind.y) * Mathf.Rad2Deg) + "." + (violent.Length > 0 ? " " + violent : "") + (HighLogic.LoadedSceneIsFlight && Icing.Report.Length > 0 ? " " + Icing.Report : "");
        }
    }
}
