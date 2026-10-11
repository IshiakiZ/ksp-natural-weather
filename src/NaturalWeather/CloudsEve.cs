using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// EVE's clouds, hidden on a world where these clouds are drawn (so that there are not two skies of cloud), and shown again
    /// when they are not. EVE draws a world's clouds as a flat picture on a sphere ("EVE Clouds: Kerbin-clouds1", shader
    /// Scatterer-EVE/Cloud or EVE/Cloud), puffs near the camera (the CloudsVolume, shader ...CloudVolumeParticle) and a shadow on
    /// the ground ("EVE ScreenSpaceShadow"); each renderer is told not to draw (forceRenderingOff, which EVE's own switching
    /// leaves alone), never destroyed. City lights are left as they are. Looked for every few seconds, as EVE makes some of
    /// these only when the camera comes near.
    /// </summary>
    static class CloudsEve
    {
        static readonly List<Renderer> hidden = new List<Renderer>();
        static string hiddenFor = "";
        static float lookedAt = -100f;

        public static int Count => hidden.Count;

        static bool IsEveCloud(Material m)
        {
            if (m == null || m.shader == null) return false;
            string s = m.shader.name;
            if (s.IndexOf("EVE", StringComparison.Ordinal) < 0) return false;
            return s.EndsWith("/Cloud", StringComparison.Ordinal) || s.IndexOf("CloudVolumeParticle", StringComparison.Ordinal) >= 0 || s.IndexOf("CloudShadow", StringComparison.Ordinal) >= 0;
        }

        static bool Under(Transform t, string body)
        {
            for (int i = 0; i < 6 && t != null; i++, t = t.parent)
                if (t.name == body || t.name.IndexOf(body + "-clouds", StringComparison.Ordinal) >= 0 || t.name.StartsWith(body + " ", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Hide EVE's clouds of this world (null: show every one hidden).</summary>
        public static void Hide(CelestialBody body)
        {
            string name = body != null ? body.bodyName : "";
            if (name != hiddenFor) Show();
            if (body == null) return;
            if (Time.realtimeSinceStartup - lookedAt < 4f) return;
            lookedAt = Time.realtimeSinceStartup;
            hiddenFor = name;
            hidden.RemoveAll(r => r == null);
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || r.forceRenderingOff) continue;
                Material m = r.sharedMaterial;
                if (!IsEveCloud(m) || !Under(r.transform, name)) continue;
                r.forceRenderingOff = true;
                hidden.Add(r);
            }
        }

        public static void Show()
        {
            foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
            hiddenFor = "";
            lookedAt = -100f;
        }
    }
}
