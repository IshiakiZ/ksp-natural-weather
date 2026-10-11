using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Keystone;
using UnityEngine;
using UnityEngine.Networking;

namespace NaturalWeather
{
    /// <summary>
    /// A real city's weather, from online: the player types a city, and Kerbin's weather round the space centre becomes that
    /// city's, fetched again every quarter of an hour while the game runs and kept in the mod's settings, so it is there at
    /// the next start before anything is fetched.
    ///
    /// The service is Open-Meteo (open-meteo.com): free for uses like this one, no key, its data under CC BY 4.0. What is
    /// sent: the city's name as typed (to find where it is), then that place's latitude and longitude (for its weather).
    /// Nothing else. Off until a city is typed in; an empty box sends nothing.
    /// </summary>
    static class City
    {
        public static string Typed = "";
        public static string Words = "";
        public static string Place = "";         // "London, United Kingdom", once found
        static double lat, lon;
        static bool found, busy, saved;
        static float nextFetch;
        const float Every = 900f, Retry = 300f;

        static string Folder => KSPUtil.ApplicationRootPath + "GameData/NaturalWeather/PluginData/";

        /// <summary>What was kept from last time: the city, where it is, and its weather as last fetched.</summary>
        public static void Load()
        {
            Mod page = Weather.Page;
            if (page == null) return;
            Typed = page.Kept("city") ?? "";
            string place = page.Kept("cityplace");
            if (!string.IsNullOrEmpty(place))
            {
                string[] p = place.Split('|');
                if (p.Length >= 4 && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                {
                    found = p[0].Length > 0; Place = p[0] + (p[1].Length > 0 ? ", " + p[1] : "");
                }
            }
            CityWeather kept = Unpack(page.Kept("cityweather"));
            if (kept != null && Typed.Length > 0) { kept.Name = Place; Overrides.City = kept; Words = Say(kept); }
            nextFetch = 0f;
        }

        /// <summary>The player has typed a city (or emptied the box).</summary>
        public static void Set(string name)
        {
            name = (name ?? "").Trim();
            if (name == Typed) return;
            Typed = name;
            found = false; Place = ""; saved = false;
            Weather.Page.Keep("city", name.Length > 0 ? name : null);
            Weather.Page.Keep("cityplace", null);
            Weather.Page.Keep("cityweather", null);
            Overrides.City = null;
            Model.Forget();
            Words = name.Length > 0 ? "Looking for " + name + "..." : "";
            nextFetch = 0f;
        }

        public static void Tick()
        {
            if (Typed.Length == 0) { if (Overrides.City != null && !saved) { Overrides.City = null; Model.Forget(); } return; }
            if (busy || Time.realtimeSinceStartup < nextFetch || WeatherDriver.Instance == null) return;
            busy = true;
            WeatherDriver.Instance.StartCoroutine(Fetch());
        }

        static IEnumerator Fetch()
        {
            string asked = Typed;
            nextFetch = Time.realtimeSinceStartup + Retry;
            if (!found)
            {
                string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(asked);
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.timeout = 15;
                    yield return req.SendWebRequest();
                    if (asked != Typed) { busy = false; nextFetch = 0f; yield break; }
                    if (req.isNetworkError || req.isHttpError) { Words = "Could not reach Open-Meteo to find " + asked + " (" + req.error + "); trying again in five minutes."; busy = false; yield break; }
                    string text = req.downloadHandler.text;
                    if (!Number(text, "latitude", out lat) || !Number(text, "longitude", out lon))
                    {
                        Words = "Open-Meteo knows no city called \"" + asked + "\". Try its name in English, or a nearby larger town.";
                        nextFetch = float.MaxValue; busy = false; yield break;
                    }
                    string name = Text(text, "name") ?? asked, country = Text(text, "country") ?? "";
                    found = true; Place = name + (country.Length > 0 ? ", " + country : "");
                    Weather.Page.Keep("cityplace", Clean(name) + "|" + Clean(country) + "|" + lat.ToString("F5", CultureInfo.InvariantCulture) + "|" + lon.ToString("F5", CultureInfo.InvariantCulture));
                }
            }
            string forecast = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString("F4", CultureInfo.InvariantCulture) + "&longitude=" + lon.ToString("F4", CultureInfo.InvariantCulture)
                + "&current=temperature_2m,precipitation,snowfall,weather_code,cloud_cover_low,cloud_cover_mid,cloud_cover_high,wind_speed_10m,wind_direction_10m,wind_gusts_10m&wind_speed_unit=ms&timezone=GMT";
            using (UnityWebRequest req = UnityWebRequest.Get(forecast))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (asked != Typed) { busy = false; nextFetch = 0f; yield break; }
                if (req.isNetworkError || req.isHttpError) { Words = "Could not fetch " + Place + "'s weather (" + req.error + "); trying again in five minutes." + (Overrides.City != null ? " The last weather fetched is used meanwhile." : ""); busy = false; yield break; }
                string text = req.downloadHandler.text;
                CityWeather got = Parse(text);
                if (got == null) { Words = "Open-Meteo's answer for " + Place + " could not be read; trying again in five minutes."; busy = false; yield break; }
                got.Name = Place; got.Latitude = lat; got.Longitude = lon; got.Fetched = DateTime.UtcNow;
                Overrides.City = got;
                Model.Forget();
                Words = Say(got);
                Weather.Page.Keep("cityweather", Pack(got));
                try { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "city-last.json", text); } catch (Exception) { }
                nextFetch = Time.realtimeSinceStartup + Every;
                Weather.Log("the weather of " + Place + " fetched: " + got.Words());
            }
            busy = false;
        }

        static string Say(CityWeather c)
        {
            double minutes = (DateTime.UtcNow - c.Fetched).TotalMinutes;
            string when = minutes < 1.5 ? "just now" : minutes < 90 ? Math.Round(minutes) + " minutes ago" : minutes < 60 * 36 ? Math.Round(minutes / 60.0) + " hours ago" : "days ago";
            return c.Name + ": " + c.Words() + " (fetched " + when + ").";
        }

        /// <summary>The figures out of Open-Meteo's answer, from its "current" part.</summary>
        public static CityWeather Parse(string json)
        {
            int at = json.IndexOf("\"current\":", StringComparison.Ordinal);
            if (at < 0) return null;
            string cur = json.Substring(at);
            var c = new CityWeather();
            if (!Number(cur, "temperature_2m", out double t)) return null;
            c.Temperature = (float)t;
            if (Number(cur, "precipitation", out double p)) c.Precipitation = (float)p;
            if (Number(cur, "snowfall", out double s)) c.Snowfall = (float)s;
            if (Number(cur, "weather_code", out double code)) c.Code = (int)code;
            if (Number(cur, "cloud_cover_low", out double cl)) c.CloudLow = (float)(cl / 100.0);
            if (Number(cur, "cloud_cover_mid", out double cm)) c.CloudMid = (float)(cm / 100.0);
            if (Number(cur, "cloud_cover_high", out double ch)) c.CloudHigh = (float)(ch / 100.0);
            if (Number(cur, "wind_speed_10m", out double ws)) c.WindSpeed = (float)ws;
            if (Number(cur, "wind_direction_10m", out double wd)) c.WindFrom = (float)wd;
            if (Number(cur, "wind_gusts_10m", out double wg)) c.WindGusts = (float)wg;
            c.Visibility = c.Foggy ? 250f : 20000f;
            return c;
        }

        static bool Number(string text, string key, out double value)
        {
            value = 0.0;
            Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(\\.[0-9]+)?([eE][-+]?[0-9]+)?)");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static string Text(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string Clean(string s) => (s ?? "").Replace("|", " ").Replace("{", "").Replace("}", "").Replace("//", "/").Trim();

        // (kept as one line of plain figures: the settings file cannot hold braces)
        static string Pack(CityWeather c) => string.Join(";", new[]
        {
            "at=" + c.Fetched.ToString("o", CultureInfo.InvariantCulture), "code=" + c.Code, "t=" + F(c.Temperature), "p=" + F(c.Precipitation), "s=" + F(c.Snowfall),
            "cl=" + F(c.CloudLow), "cm=" + F(c.CloudMid), "ch=" + F(c.CloudHigh), "ws=" + F(c.WindSpeed), "wd=" + F(c.WindFrom), "wg=" + F(c.WindGusts), "lat=" + c.Latitude.ToString("F4", CultureInfo.InvariantCulture), "lon=" + c.Longitude.ToString("F4", CultureInfo.InvariantCulture),
        });

        static string F(float x) => x.ToString("0.###", CultureInfo.InvariantCulture);

        static CityWeather Unpack(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var c = new CityWeather();
            try
            {
                foreach (string part in text.Split(';'))
                {
                    int eq = part.IndexOf('=');
                    if (eq < 0) continue;
                    string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                    float f = 0f;
                    if (k != "at") float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
                    switch (k)
                    {
                        case "at": c.Fetched = DateTime.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(); break;
                        case "code": c.Code = (int)f; break;
                        case "t": c.Temperature = f; break;
                        case "p": c.Precipitation = f; break;
                        case "s": c.Snowfall = f; break;
                        case "cl": c.CloudLow = f; break;
                        case "cm": c.CloudMid = f; break;
                        case "ch": c.CloudHigh = f; break;
                        case "ws": c.WindSpeed = f; break;
                        case "wd": c.WindFrom = f; break;
                        case "wg": c.WindGusts = f; break;
                        case "lat": c.Latitude = f; break;
                        case "lon": c.Longitude = f; break;
                    }
                }
            }
            catch (Exception) { return null; }
            c.Visibility = c.Foggy ? 250f : 20000f;
            return c;
        }

#if DEV
        /// <summary>(For the development build: read a saved answer of Open-Meteo's from a file, as if it had just been fetched, without asking online.)</summary>
        public static string FromFile(string path)
        {
            if (!File.Exists(path)) return "no such file: " + path;
            CityWeather got = Parse(File.ReadAllText(path));
            if (got == null) return "could not read it";
            got.Name = "(saved) " + Path.GetFileNameWithoutExtension(path); got.Fetched = DateTime.UtcNow;
            Overrides.City = got; saved = true; Model.Forget();
            Words = Say(got);
            return Words;
        }
#endif
    }
}
