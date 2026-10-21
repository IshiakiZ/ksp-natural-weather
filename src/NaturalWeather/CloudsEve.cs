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
    /// leaves alone), never destroyed; the puffs' holders are made inactive, whole (see HidePuffs). City lights are left as they
    /// are. Looked for every few seconds and whenever the camera's height crosses a power of two of kilometres; the puffs, which EVE
    /// makes anew whenever the camera comes near, every frame (HidePuffs).
    /// </summary>
    static class CloudsEve
    {
        static readonly List<Renderer> hidden = new List<Renderer>();
        static string hiddenFor = "";
        static float lookedAt = -100f, lookEvery = 4f;
        static int lookedBand = int.MinValue;
        static readonly List<GameObject> puffs = new List<GameObject>();
        static readonly HashSet<int> seen = new HashSet<int>();
        static Transform seenUnder;
        // (EVE's cloud shadows' own materials, with the strength each had: put to nothing while its clouds are hidden, see Quiet)
        static readonly Dictionary<Material, float> shadowsKept = new Dictionary<Material, float>();
        static readonly int idShadowFactor = Shader.PropertyToID("_ShadowFactor");

        public static int Count => hidden.Count + puffs.Count;

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
            HidePuffs(body);
            Quiet();
            // (The rest of EVE's clouds it makes once, at a scene's start, and switches on and off by the camera's height (its flat
            // layer's picture near or far, its shadow on the ground near: EVE's Clouds2D): so looked for every few seconds, and at once
            // whenever the camera's height crosses a power of two of kilometres (eight times on the way to orbit), as Natural Ocean
            // looks for Scatterer's sea. Looked for at once whenever the camera had gone 3 km, as until 2026-10-20 for the puffs, the
            // whole scene was looked through every frame under time warp in orbit: 5-58 ms a frame.)
            Camera eye = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            double height = eye != null ? ((Vector3d)eye.transform.position - body.position).magnitude - body.Radius : 0.0;
            int band = (int)Math.Floor(Math.Log(Math.Max(height, 500.0) / 1000.0, 2.0));
            if (Time.realtimeSinceStartup - lookedAt < lookEvery && band == lookedBand) return;
            lookedAt = Time.realtimeSinceStartup; lookedBand = band;
            hiddenFor = name;
            hidden.RemoveAll(r => r == null);
            int before = hidden.Count;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || r.forceRenderingOff) continue;
                Material m = r.sharedMaterial;
                if (!IsEveCloud(m) || !Under(r.transform, name)) continue;
                r.forceRenderingOff = true;
                hidden.Add(r);
                if (m.shader.name.IndexOf("CloudShadow", StringComparison.Ordinal) >= 0 && !shadowsKept.ContainsKey(m)) shadowsKept[m] = m.GetFloat(idShadowFactor);
            }
            Quiet();
            // (Every renderer in the scene looked through takes milliseconds with everything installed: 4.3 at the pad, a hitch
            // every four seconds until 2026-10-18. So again in four seconds while EVE is still making its clouds (some found
            // this time), then after 8, 16 ... up to two minutes; from the start again on another world.)
            lookEvery = hidden.Count > before ? 4f : Mathf.Min(lookEvery * 2f, 120f);
#if DEV
            Scans++; NewlyHidden = hidden.Count - before;
#endif
        }

        // EVE's cloud shadows on the ground put to nothing while its clouds are hidden: its shadow's renderer hidden is not enough,
        // for Scatterer lays the very same material over the picture itself, every frame, from its sea's command buffer (its
        // EVEReflectionHandler takes EVE's screenSpaceShadow.material, OceanCommandBuffer blits it), whatever EVE's renderers do.
        // EVE's stock cloud shadows lay on the land under these clouds as dark blue blotches, in every view from high up (the owner's
        // "glitchy ... land ... from far away", 2026-10-20: the far ground's views from 10 to 50 km, clouds held off or not). The
        // shadow's colour is 1 + (its cloud's colour - 1) x its alpha x _ShadowFactor (EVE/ScreenSpaceCloudShadow, read out of EVE's
        // bundle): at nought it darkens nothing. Set again each frame (a few materials), in case EVE sets it again; put back as it was
        // when EVE's clouds are shown again.
        static void Quiet()
        {
            foreach (KeyValuePair<Material, float> k in shadowsKept) if (k.Key != null) k.Key.SetFloat(idShadowFactor, 0f);
        }

        // EVE's puffs (its CloudsVolume): for each of the world's layers that has them, a holder named "CloudsVolume" hung straight
        // under the world's own transform (EVE's CloudsPQS gives CloudsVolume.Apply celestialBody.transform), and made anew each
        // time EVE's sphere comes on again: a scene's start, the camera back near the world, the map view left. Looked for every
        // frame among the world's own children (a handful, each one's name read once), not by the search of every renderer above,
        // which backs off to two minutes: a holder made just after a search showed for as long (the main chat's films at the pad
        // just after a picture from 200 km, 2026-10-19 18:11 and 18:14: a soft cream cluster of puffs over the pad through all their
        // 11 s). Taken out whole, the holder made inactive (EVE switches the sections under it, never the holder): on Direct3D 11
        // EVE gives each puff renderer a material of EVE/Invisible and draws the puffs itself, from a command buffer, for each
        // camera about to draw that renderer, so the search above, which goes by the material's shader, never knew them. (Switched
        // once for each holder, from the first flight camera's callback as the rest of this is: not off and on every frame, which
        // inside the cameras' callbacks loses what Unity keeps of a renderer from frame to frame, as the far sea once did.)
        static void HidePuffs(CelestialBody body)
        {
            Transform under = body.transform;
            if (under != seenUnder) { seen.Clear(); seenUnder = under; }
            for (int i = 0; i < under.childCount; i++)
            {
                Transform c = under.GetChild(i);
                if (!seen.Add(c.GetInstanceID()) || c.name != "CloudsVolume" || !c.gameObject.activeSelf) continue;
                c.gameObject.SetActive(false);
                puffs.RemoveAll(g => g == null);      // (the holders EVE has let go since)
                puffs.Add(c.gameObject);
            }
        }

#if DEV
        /// <summary>(Development build: how many times the scene has been looked through, and how many were hidden the last time.)</summary>
        public static int Scans, NewlyHidden;
        /// <summary>(Development build: look through the scene again at the next frame.)</summary>
        public static string Rescan() { lookedAt = -100f; lookEvery = 4f; return "EVE's clouds looked for again at the next frame (" + hidden.Count + " hidden now)"; }
        static readonly List<Renderer> planetHidden = new List<Renderer>();
        /// <summary>(Development build: EVE's other screen-space pass, the light and shadow of other worlds ("EVE/ScreenSpacePlanetLight",
        /// its "Celestial ScreenSpaceShadow"), which is not hidden with the clouds: "hide", "show", or nothing to say what there is.)</summary>
        public static string Planet(string v)
        {
            v = (v ?? "").Trim();
            if (v == "show") { foreach (Renderer r in planetHidden) if (r != null) r.forceRenderingOff = false; planetHidden.Clear(); }
            var say = new System.Text.StringBuilder();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                Material m = r != null ? r.sharedMaterial : null;
                if (m == null || m.shader == null || m.shader.name.IndexOf("PlanetLight", StringComparison.Ordinal) < 0) continue;
                if (v == "hide" && !r.forceRenderingOff) { r.forceRenderingOff = true; planetHidden.Add(r); }
                say.Append(r.name).Append(" (").Append(m.shader.name).Append(r.forceRenderingOff ? ", hidden" : ", drawn").Append(r.enabled ? "" : ", off").Append("); ");
            }
            return say.Length > 0 ? say.ToString() : "no renderer with EVE's planet light";
        }

        /// <summary>(Development build: every renderer drawn with one of EVE's shaders, where it hangs, and whether it is hidden.)</summary>
        public static string Said()
        {
            var say = new System.Text.StringBuilder();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                Material m = r != null ? r.sharedMaterial : null;
                if (m == null || m.shader == null || m.shader.name.IndexOf("EVE", StringComparison.Ordinal) < 0) continue;
                string path = r.name;
                for (Transform t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                say.Append(m.shader.name).Append(" | ").Append(path).Append(" | ").Append(r.GetType().Name).Append(r.enabled ? " on" : " off").Append(r.forceRenderingOff ? ", hidden" : "").Append(r.gameObject.activeInHierarchy ? "" : ", inactive").Append("\n");
            }
            foreach (GameObject g in puffs)
                if (g != null) say.Append("EVE's puffs | ").Append(g.transform.parent != null ? g.transform.parent.name + "/" : "").Append(g.name).Append(" | taken out whole\n");
            return say.Length > 0 ? say.ToString() : "no renderer with an EVE shader";
        }
#endif

        public static void Show()
        {
            foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
            foreach (KeyValuePair<Material, float> k in shadowsKept) if (k.Key != null) k.Key.SetFloat(idShadowFactor, k.Value);
            shadowsKept.Clear();
            foreach (GameObject g in puffs) if (g != null) g.SetActive(true);
            puffs.Clear(); seen.Clear();
            hiddenFor = "";
            lookedAt = -100f; lookEvery = 4f;
        }
    }
}
