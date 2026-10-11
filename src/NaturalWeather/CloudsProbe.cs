#if DEV
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// (Development build only.) What the clouds need to know about the game's cameras and the other mods' drawing,
    /// asked through dev_call: every camera with its range, depth picture and command buffers; and every renderer whose
    /// shader's name has some text in it (EVE's clouds, Scatterer's sky), with its layer, queue and place.
    /// </summary>
    public static class CloudsProbe
    {
        public static string Cameras()
        {
            var o = new StringBuilder();
            var all = new Camera[Camera.allCamerasCount];
            Camera.GetAllCameras(all);
            foreach (Camera c in all)
            {
                if (c == null) continue;
                o.Append(c.name).Append(": depth ").Append(c.depth).Append(", ").Append(c.nearClipPlane.ToString("G4")).Append(" to ").Append(c.farClipPlane.ToString("G6"))
                 .Append(", clear ").Append(c.clearFlags).Append(", hdr ").Append(c.allowHDR).Append(", into ").Append(c.targetTexture != null ? c.targetTexture.name : "screen")
                 .Append(", depth picture ").Append(c.depthTextureMode).Append(", mask ").Append(c.cullingMask).Append(", fov ").Append(c.fieldOfView.ToString("F1"))
                 .Append(", ").Append(c.pixelWidth).Append("x").Append(c.pixelHeight).Append(", at ").Append(c.transform.position.ToString("F1"));
                foreach (CameraEvent e in Enum.GetValues(typeof(CameraEvent)))
                {
                    CommandBuffer[] buffers = c.GetCommandBuffers(e);
                    if (buffers.Length == 0) continue;
                    o.Append(" | ").Append(e).Append(": ");
                    foreach (CommandBuffer b in buffers) o.Append(b.name).Append("; ");
                }
                o.Append(" | parts: ");
                foreach (Behaviour b in c.GetComponents<Behaviour>()) if (b != null) o.Append(b.GetType().Name).Append(b.enabled ? "" : " (off)").Append(", ");
                o.Append("\n");
            }
            o.Append("graphics ").Append(SystemInfo.graphicsDeviceType).Append(" ").Append(SystemInfo.graphicsDeviceVersion).Append(", 3D render textures ").Append(SystemInfo.supports3DRenderTextures)
             .Append(", float textures ").Append(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)).Append(", reversed z ").Append(SystemInfo.usesReversedZBuffer)
             .Append(", copy texture ").Append(SystemInfo.copyTextureSupport);
            return o.ToString();
        }

        static readonly Dictionary<string, string> seen = new Dictionary<string, string>();
        static bool watching;

        static void Note(Camera c, string when)
        {
            if (c == null || (c.name != "Camera 00" && c.name != "Camera 01" && c.name != "Camera ScaledSpace")) return;
            var o = new StringBuilder();
            foreach (CameraEvent e in Enum.GetValues(typeof(CameraEvent)))
            {
                CommandBuffer[] buffers = c.GetCommandBuffers(e);
                if (buffers.Length == 0) continue;
                o.Append(e).Append(": ");
                foreach (CommandBuffer b in buffers) o.Append(b.name).Append(" (").Append(b.sizeInBytes).Append("); ");
            }
            seen[c.name + " " + when] = o.ToString();
        }
        static void Pre(Camera c) => Note(c, "before");
        static void Post(Camera c) => Note(c, "after");

        /// <summary>The command buffers each scene camera has as it begins drawing and as it ends (some mods add theirs only for the drawing).</summary>
        public static string Watch()
        {
            if (!watching) { Camera.onPreRender += Pre; Camera.onPostRender += Post; watching = true; return "watching: call again"; }
            var o = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in seen) o.Append(kv.Key).Append(" | ").Append(kv.Value).Append("\n");
            Camera.onPreRender -= Pre; Camera.onPostRender -= Post; watching = false;
            return o.ToString();
        }

        /// <summary>Every renderer whose shader's name holds the text given, one a line (at most 60).</summary>
        public static string Every(string text)
        {
            var o = new StringBuilder();
            int n = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.shader.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++n > 60) return o.ToString() + "...";
                    o.Append(m.shader.name).Append(" q").Append(m.renderQueue).Append(" ").Append(Path(r.transform)).Append(" layer ").Append(r.gameObject.layer)
                     .Append(r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)").Append(r.forceRenderingOff ? " (hidden)" : "").Append(" ").Append(r.GetType().Name).Append("\n");
                }
            }
            return o.ToString();
        }

        /// <summary>Renderers whose shader's name holds the text given (e.g. "EVE", "Scatterer", "Sky"): grouped by shader, with where they are.</summary>
        public static string Shaders(string text)
        {
            var groups = new Dictionary<string, List<Renderer>>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.shader.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string key = m.shader.name + " (queue " + m.renderQueue + ")";
                    if (!groups.TryGetValue(key, out List<Renderer> list)) groups[key] = list = new List<Renderer>();
                    list.Add(r);
                }
            }
            var o = new StringBuilder();
            foreach (KeyValuePair<string, List<Renderer>> kv in groups)
            {
                o.Append(kv.Key).Append(": ").Append(kv.Value.Count).Append(" | ");
                for (int i = 0; i < Math.Min(4, kv.Value.Count); i++)
                {
                    Renderer r = kv.Value[i];
                    o.Append(Path(r.transform)).Append(" layer ").Append(r.gameObject.layer).Append(r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)").Append(r.forceRenderingOff ? " (hidden)" : "")
                     .Append(" size ").Append(r.bounds.size.magnitude.ToString("G3")).Append("; ");
                }
                o.Append("\n");
            }
            return o.Length > 0 ? o.ToString() : "none";
        }

        static string Path(Transform t)
        {
            string s = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
#endif
