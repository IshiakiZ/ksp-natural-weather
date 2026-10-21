using System;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>A city's weather as an online weather service gave it (see City).</summary>
    sealed class CityWeather
    {
        public string Name = "", Country = "";
        public double Latitude, Longitude;
        public float CloudLow, CloudMid, CloudHigh;      // 0 to 1
        public float Precipitation;                       // mm in the last hour
        public float Snowfall;                            // cm in the last hour
        public int Code;                                  // the World Meteorological Organization's weather code
        public float Temperature;                         // degrees Celsius
        public float WindSpeed, WindFrom, WindGusts;      // m/s, degrees from north the wind comes from, m/s
        public float Visibility = 20000f;                 // m
        public DateTime Fetched;                          // when, by the computer's clock (UTC)

        public bool Thunder => Code >= 95;
        public bool Showers => Code >= 80 && Code <= 86;
        public bool Foggy => Code == 45 || Code == 48;

        /// <summary>In a few words: "light rain, 12 °C, wind 5 m/s from the south-west".</summary>
        public string Words()
        {
            string what = Describe(Code);
            return what + ", " + Temperature.ToString("F0") + " °C, wind " + WindSpeed.ToString("F0") + " m/s from the " + Compass(WindFrom) + (WindGusts > WindSpeed + 2f ? ", gusts " + WindGusts.ToString("F0") : "");
        }

        public static string Compass(float degrees)
        {
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            int i = (int)Math.Floor(((degrees % 360f) + 360f + 22.5f) % 360f / 45f);
            return names[Math.Max(0, Math.Min(7, i))];
        }

        public static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "clear";
                case 1: return "mainly clear";
                case 2: return "partly cloudy";
                case 3: return "overcast";
                case 45: case 48: return "fog";
                case 51: case 53: case 55: return "drizzle";
                case 56: case 57: return "freezing drizzle";
                case 61: return "light rain";
                case 63: return "rain";
                case 65: return "heavy rain";
                case 66: case 67: return "freezing rain";
                case 71: return "light snow";
                case 73: return "snow";
                case 75: return "heavy snow";
                case 77: return "snow grains";
                case 80: return "light showers";
                case 81: return "showers";
                case 82: return "violent showers";
                case 85: case 86: return "snow showers";
                case 95: return "thunderstorm";
                case 96: case 99: return "thunderstorm with hail";
                default: return "weather code " + code;
            }
        }
    }

    /// <summary>
    /// The weather when it is not left to itself: one chosen on the mod's page (everywhere on every world, eased in), or a
    /// real city's, fetched from online (on Kerbin, round the space centre).
    /// </summary>
    static class Overrides
    {
        public static volatile CityWeather City;
        public static volatile bool UseCity = true;

        // the space centre, round which a city's weather is laid: in full within 500 km, gone by 800
        const double CentreLat = -0.0972, CentreLon = -74.5577;

        static float CityWeight(World w, double lat, double lon)
        {
            CityWeather city = City;
            if (city == null || !UseCity || w.Name != "Kerbin") return 0f;
            double d = Model.Distance(w, lat, lon, CentreLat, CentreLon);
            return 1f - Noise.Smooth(500000f, 800000f, (float)d);
        }

        /// <summary>The large fields, moved towards what was chosen, or what the city has. (A chosen weather without storms in it
        /// damps the rising air: over the equator's rain belt, where the air rises by day and night since 2026-10-18, a held cloudy
        /// or overcast sky was a thunderstorm.)</summary>
        public static void Apply(World w, double lat, double lon, ref Base b)
        {
            int chosen = Model.Chosen;
            float wc = Model.ChosenWeight;
            if (chosen > 0 && wc > 0.001f)
            {
                b.Overridden = true;
                switch (chosen)
                {
                    case 1: Toward(ref b.M, 0.15f, wc); Toward(ref b.Usual, 0f, wc); b.Inst *= 1f - 0.85f * wc; b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;                         // clear
                    case 2: Toward(ref b.M, 0.42f, wc); Toward(ref b.Stab, 0.2f, wc); Toward(ref b.Aloft, 0.25f, wc); b.Inst = Math.Max(b.Inst, 0.55f * wc * Noise.Smooth(-0.05f, 0.2f, b.SunUp)); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break; // fair
                    case 3: Toward(ref b.M, 0.63f, wc); Toward(ref b.Stab, 0.66f, wc); b.Inst *= 1f - 0.85f * wc; b.HeldBreeze = Math.Max(b.HeldBreeze, 4f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;                                                   // cloudy
                    case 4: Toward(ref b.M, 0.77f, wc); Toward(ref b.Stab, 0.92f, wc); b.Inst *= 1f - 0.85f * wc; b.HeldBreeze = Math.Max(b.HeldBreeze, 5.5f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;                                                   // overcast
                    case 5: Toward(ref b.M, 0.93f, wc); b.Inst *= 1f - 0.85f * wc; b.HeldBreeze = Math.Max(b.HeldBreeze, 8f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;                                                   // rain
                    case 6: Toward(ref b.M, 0.68f, wc); Toward(ref b.Stab, 0.1f, wc); Toward(ref b.Inst, 1f, wc); b.StormBias += 0.6f * wc; b.HeldBreeze = Math.Max(b.HeldBreeze, 9f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break; // storm
                    case 7: Toward(ref b.M, 0.62f, wc); b.Inst *= 1f - 0.85f * wc; Toward(ref b.Fog, 0.95f, wc); b.Calm *= 1f - 0.8f * wc; b.Dust *= 1f - wc; if (wc > 0.5f && (b.FogOf == FogKind.None || b.FogOf == FogKind.Steam)) { b.FogOf = b.Land > 0.5f ? FogKind.Radiation : FogKind.Advection; b.FogDeep = b.Land > 0.5f ? 110f : 350f; } break;              // fog
                    case 8: Toward(ref b.M, 0.9f, wc); b.Inst *= 1f - 0.85f * wc; Toward(ref b.T0, 268f, wc); b.HeldBreeze = Math.Max(b.HeldBreeze, 6f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;                       // snow
                    case 9: b.WindScale *= 1f + 1.6f * wc; b.Windy = Math.Max(b.Windy, wc); break;                                                                                     // windy
                    case 10: Toward(ref b.M, 0.2f, wc); Toward(ref b.Usual, 0f, wc); Toward(ref b.Dust, 0.9f, wc); b.Fog *= 1f - wc; break;                                        // dust storm
                    // (freezing rain: a wet system's rain, the ground two degrees under freezing, warm air aloft; ice pellets: the same over ground eight under)
                    case 11: Toward(ref b.M, 0.93f, wc); b.Inst *= 1f - 0.85f * wc; Toward(ref b.T0, 271.2f, wc); Toward(ref b.Nose, 1f, wc); b.HeldBreeze = Math.Max(b.HeldBreeze, 6f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;
                    case 12: Toward(ref b.M, 0.93f, wc); b.Inst *= 1f - 0.85f * wc; Toward(ref b.T0, 265f, wc); Toward(ref b.Nose, 1f, wc); b.HeldBreeze = Math.Max(b.HeldBreeze, 7f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;
                    // (a hailstorm: storms as for a held storm, and every strong one of them with hail)
                    case 13: Toward(ref b.M, 0.68f, wc); Toward(ref b.Inst, 1f, wc); b.StormBias += 0.6f * wc; b.HailBias += wc; b.HeldBreeze = Math.Max(b.HeldBreeze, 9f * wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;
                    // (a downburst, a tornado: a stormy sky for them to come out of; a waterspout: towering heaps over the sea; a
                    // hurricane and a squall line bring their own weather, into a sky with some cloud in it)
                    case Model.HeldBurst: case Model.HeldTornado: Toward(ref b.M, 0.68f, wc); Toward(ref b.Inst, 0.9f, wc); b.StormBias += 0.4f * wc; b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;
                    case Model.HeldSpout: Toward(ref b.M, 0.6f, wc); Toward(ref b.Inst, 0.85f, wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;
                    case Model.HeldCyclone: case Model.HeldSquall: Toward(ref b.M, 0.5f, wc); b.Fog *= 1f - wc; b.Dust *= 1f - wc; break;
                }
            }
            float wcity = CityWeight(w, lat, lon);
            if (wcity > 0.001f)
            {
                CityWeather city = City;
                b.Overridden = true;
                float cover = Math.Max(city.CloudLow, city.CloudMid * 0.85f);
                float m = 0.2f + 0.55f * cover + (city.Precipitation > 0.1f || city.Snowfall > 0.05f ? 0.2f + 0.1f * Math.Min(1f, city.Precipitation / 4f) : 0f);
                Toward(ref b.M, Math.Min(0.97f, m), wcity);
                if (city.Showers || city.Thunder) { Toward(ref b.Inst, city.Thunder ? 1f : 0.75f, wcity); if (city.Thunder) b.StormBias += 0.6f * wcity; }
                else b.Inst *= 1f - 0.7f * wcity;
                Toward(ref b.Fog, city.Foggy ? 0.9f : 0f, wcity);
                Toward(ref b.T0, 273.15f + city.Temperature, wcity);
            }
        }

        /// <summary>The finished weather, shaped by the city's own figures where they say more than the fields can.</summary>
        public static void Shape(World w, double lat, double lon, ref WeatherHere here)
        {
            float wcity = CityWeight(w, lat, lon);
            if (wcity <= 0.001f) return;
            CityWeather city = City;
            Layer(ref here.Low, city.CloudLow, wcity);
            Layer(ref here.Middle, city.CloudMid, wcity);
            Layer(ref here.High, city.CloudHigh, wcity);
            float rain = 1f - (float)Math.Exp(-(city.Precipitation + city.Snowfall * 2f) / 8f);
            here.Rain = here.Rain + (rain - here.Rain) * wcity;
            float snow = city.Snowfall > 0.05f ? 1f : Noise.Smooth(2f, 0f, city.Temperature);
            here.Snow = here.Snow + (snow - here.Snow) * wcity;
            here.Seeing = here.Seeing + (Mathf.Clamp(city.Visibility, 50f, 60000f) - here.Seeing) * wcity;
        }

        static void Layer(ref CloudLayer layer, float cover, float weight)
        {
            if (layer.Kind == CloudKind.None && cover > 0.02f) layer.Kind = CloudKind.Stratocumulus;
            layer.Cover = layer.Cover + (cover - layer.Cover) * weight;
        }

        /// <summary>The city's wind at 10 m (east, north) and how gusty, and how much of it to take here; false where it does not reach.</summary>
        public static bool Wind(World w, double lat, double lon, out float east, out float north, out float sigma, out float weight)
        {
            east = north = sigma = 0f;
            weight = CityWeight(w, lat, lon);
            if (weight <= 0.001f) return false;
            CityWeather city = City;
            double from = city.WindFrom * Math.PI / 180.0;
            east = -(float)(city.WindSpeed * Math.Sin(from));
            north = -(float)(city.WindSpeed * Math.Cos(from));
            sigma = Math.Max(0.3f, (city.WindGusts - city.WindSpeed) / 2.4f);
            return true;
        }

        static void Toward(ref float value, float target, float weight) => value += (target - value) * weight;
    }
}
