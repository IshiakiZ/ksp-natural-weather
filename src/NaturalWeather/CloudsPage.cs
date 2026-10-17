using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The clouds' settings, on Natural Weather's page in Keystone's window (found by its name: the weather makes it as the
    /// game starts; should it not be there, the clouds make it). The assembly's own attributes are in Natural Weather's main
    /// file.
    /// </summary>
    public static class CloudsPage
    {
        public static Toggle On, Shadows, HideEve, Heaps, Made, Stirred;
        public static Choice Quality;
        public static Slider Amount, Thickness, Brightness;
        static Mod added;

        /// <summary>The clouds' settings, added to the weather mod's page (under a heading of their own).</summary>
        public static void AddTo(Mod page)
        {
            if (page == null || page == added) return;
            added = page;
            page.Heading("Clouds");
            On = page.Toggle("clouds", "Clouds", true, "Clouds of the mod's own, drawn as real volumes that the light goes into: heaps, sheets, storm towers and high wisps, where the weather has them, with their shadows on the ground. Seen from below, from inside as you fly through them, and from orbit. (A shader of the mod's own, packed for the game's OpenGL on a Mac or Linux and built for Direct3D on Windows.)");
            Quality = page.Choice("cloudquality", "Cloud detail", 1, new[] { "lower", "middle", "higher" }, "How finely the clouds are drawn: more steps through each and a larger picture of them cost more time each frame.");
            Amount = page.Slider("cloudamount", "How much cloud", 1f, 0f, 2f, "", 2, "More or less cloud than the weather says: 1 as it says.");
            Thickness = page.Slider("cloudthick", "How thick", 1f, 0.3f, 3f, "", 2, "How dense a cloud is inside: thicker clouds are darker underneath and hide more.");
            Brightness = page.Slider("cloudlight", "How bright", 1f, 0.3f, 2f, "", 2, "How bright the clouds are in the light they are in.");
            Stirred = page.Toggle("cloudstir", "A craft stirs the cloud it goes through", true, "The craft you fly leaves a wake in cloud: the cloud dragged a little way after it, rolled up and stirred along its path, and a hole where its engines burnt the cloud off. It closes over in some minutes.");
            Shadows = page.Toggle("cloudshadows", "Their shadows", true, "The clouds' shadows on the ground, on buildings and on craft, as the sun throws them.");
            Made = page.Toggle("cloudsmade", "Heaps built of lumps", true, "Fair-weather heaps, and the taller ones, are shapes made beforehand of lumps upon lumps (a turret or several on a level base, lumps on the turrets, lumps on those), read by the shader, with smaller lumps carved at the pixel: cauliflowers. Off: every cloud carved from noise, as before 0.3.0 (smooth shards).");
            Heaps = page.Toggle("cloudheaps", "Nearest heaps made as smoke is made (a trial)", false, "A trial, off unless you turn it on. The three fair-weather heaps nearest you are built of rising thermals in Volumetric Explosions' own engine (0.6.3 or later), as its smoke is, and lit through a grid: real billows. They take about two minutes to fill out, cost a few frames a second, and do not yet sit well among the clouds drawn round them (a level line at the clouds' base, and bits of the drawn clouds in front).");
            HideEve = page.Toggle("cloudshideeve", "Hide EVE's clouds where these are drawn", true, "Where these clouds are drawn, EVE's flat cloud layer, its puffs and its cloud shadows are hidden on that world, so that there are not two skies of cloud. EVE's city lights are left alone.");
        }

        /// <summary>The page the settings are on: the weather mod's, or (until there is one) one the clouds make.</summary>
        public static void Page()
        {
            if (added != null && Kit.Find(added.Name) == added) return;
            Mod weather = Kit.Find("Natural Weather");
            if (weather == null) weather = Kit.Register("Natural Weather", Weather.Version, "Weather for the worlds with air.");
            AddTo(weather);
        }

        public static bool Wanted => On == null || On;
    }

    /// <summary>The clouds' settings put on the page at the main menu, so that they are there before the first flight.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class CloudsPageAtStart : MonoBehaviour
    {
        void Start() { CloudsPage.Page(); Destroy(gameObject); }
    }
}
