using System;
using Keystone;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// A rainbow, where there is one to see: the sun low behind the camera (under 42 degrees up), shining, and rain falling
    /// in front of it, away from the sun. The bow stands 42 degrees from the point straight opposite the sun (red outside,
    /// violet inside, about two degrees wide), with the faint second bow at 51 degrees, its colours the other way round, and
    /// the sky darker between them (Alexander's band). Drawn with the game's own additive particle shader on a ring a couple of
    /// kilometres off, faint, and only as strong as the sun on the rain and the rain itself allow; so it is seldom seen,
    /// as a real one is.
    /// </summary>
    public sealed class Rainbow : MonoBehaviour
    {
        GameObject bow;
        Mesh mesh;
        Material mat;
        Texture2D bands;
        float strength, lookAt, want;
        const float Far = 2400f;

        void Start()
        {
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
            if (add == null) { enabled = false; return; }
            bands = Bands();
            mat = new Material(add) { mainTexture = bands, renderQueue = 2990 };
            bow = new GameObject("NaturalWeather rainbow");
            bow.layer = 0;
            mesh = Ring();
            bow.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = bow.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            bow.SetActive(false);
        }

        void OnDestroy()
        {
            if (bow != null) Destroy(bow);
            if (mesh != null) Destroy(mesh);
            if (mat != null) Destroy(mat);
            if (bands != null) Destroy(bands);
        }

        void LateUpdate()
        {
            if (bow == null) return;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody body = FlightGlobals.currentMainBody;
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (cam == null || body == null || sun == null || !body.atmosphere || !Weather.On || !Weather.Rain || !Model.Allowed(body) || MapView.MapIsEnabled)
            {
                strength = 0f; bow.SetActive(false); return;
            }
            Vector3 at = cam.transform.position;
            Vector3 toSun = ((Vector3)(sun.position - (Vector3d)at)).normalized;
            Vector3 up = ((Vector3)((Vector3d)at - body.position)).normalized;
            float sunUp = Vector3.Dot(toSun, up);
            // (looked at again twice a second: is there rain away from the sun, and sun on it?)
            if (Time.realtimeSinceStartup > lookAt)
            {
                lookAt = Time.realtimeSinceStartup + 0.5f;
                want = 0f;
                if (sunUp > 0.02f && sunUp < 0.67f)
                {
                    Vector3 away = -toSun; away -= up * Vector3.Dot(away, up); away.Normalize();
                    World w = World.Of(body);
                    double ut = Planetarium.GetUniversalTime();
                    // the rain a few kilometres off, opposite the sun
                    Vector3d there = (Vector3d)at + (Vector3d)away * 3500.0;
                    WeatherHere far = Model.Here(w, body.GetLatitude(there), body.GetLongitude(there), ut);
                    // and the sun where the camera is (it must be shining here, between the clouds)
                    WeatherShade shade = Provider.Last;
                    float rain = Mathf.Clamp01(far.Rain * 3f) * (1f - far.Snow);
                    want = rain * Mathf.Clamp01(shade.Sun * 1.2f) * Noise.Smooth(0.67f, 0.45f, sunUp) * Noise.Smooth(0.02f, 0.08f, sunUp);
                }
            }
            strength = Mathf.MoveTowards(strength, want, Time.unscaledDeltaTime * 0.15f);
            if (strength < 0.01f) { bow.SetActive(false); return; }
            bow.SetActive(true);
            // the ring about the point opposite the sun, at a fixed distance from the camera
            bow.transform.position = at - toSun * Far;
            bow.transform.rotation = Quaternion.LookRotation(-toSun, up);
            float k = 0.2f * strength;
            mat.SetColor("_TintColor", new Color(k, k, k, 0.5f));
        }

        /// <summary>A flat ring facing the camera from Far away: its inner edge 38 degrees from the middle, its outer 54, the colours from the texture across it.</summary>
        static Mesh Ring()
        {
            const int Steps = 160;
            const float Inner = 38f, Outer = 54f;
            var verts = new Vector3[Steps * 2 + 2];
            var uvs = new Vector2[Steps * 2 + 2];
            var tris = new int[Steps * 6];
            for (int i = 0; i <= Steps; i++)
            {
                float a = i * 2f * Mathf.PI / Steps;
                float ri = Far * Mathf.Tan(Inner * Mathf.Deg2Rad), ro = Far * Mathf.Tan(Outer * Mathf.Deg2Rad);
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                // (the ring lies in a plane through the point opposite the sun; seen from the camera, Far behind it, the radii give the angles)
                verts[i * 2] = dir * ri; verts[i * 2 + 1] = dir * ro;
                uvs[i * 2] = new Vector2(0f, 0.5f); uvs[i * 2 + 1] = new Vector2(1f, 0.5f);
                if (i < Steps)
                {
                    int t = i * 6, v = i * 2;
                    tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 3; tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
                }
            }
            var m = new Mesh { name = "rainbow" };
            m.vertices = verts; m.uv = uvs; m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * Far * 4f);
            return m;
        }

        /// <summary>
        /// The colours from 38 to 54 degrees: the primary bow from violet at about 40.5 to red at 42.3, a dark band between the
        /// bows, the secondary from red at about 50.5 to violet at 53.5, weaker. (The texture runs across the ring, in angle
        /// against the tangent's stretch: the ring's radius goes with the tangent of the angle, so the bands are placed by it.)
        /// </summary>
        static Texture2D Bands()
        {
            const int W = 256;
            var t = new Texture2D(W, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NaturalWeather rainbow" };
            float ti = Mathf.Tan(38f * Mathf.Deg2Rad), to = Mathf.Tan(54f * Mathf.Deg2Rad);
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float angle = Mathf.Atan(ti + (to - ti) * u) * Mathf.Rad2Deg;
                Color c = Color.black;
                c += Hue(angle, 40.6f, 42.3f) * 1f;            // primary: violet inside, red outside
                c += Hue(angle, 53.4f, 50.4f) * 0.42f;         // secondary: red inside, violet outside
                t.SetPixel(x, 0, new Color(c.r, c.g, c.b, 1f));
            }
            t.Apply(false, true);
            return t;
        }

        /// <summary>The colour of a bow at an angle, for a bow that runs from violet at `violet` to red at `red` degrees: a soft band of the spectrum.</summary>
        static Color Hue(float angle, float violet, float red)
        {
            float s = (angle - violet) / (red - violet);                // 0 violet, 1 red
            if (s < -0.25f || s > 1.25f) return Color.black;
            float edge = Mathf.Clamp01(1f - Mathf.Abs(s - 0.5f) * 2f + 0.5f);
            edge = Mathf.Clamp01(edge);
            // a spectrum from violet through blue, green and yellow to red, desaturated (the drops' sizes smear it)
            float r = Mathf.Clamp01(1.6f * s - 0.4f) + 0.35f * Mathf.Clamp01(0.3f - s) * 2f;
            float g = Mathf.Clamp01(1.4f - Mathf.Abs(s - 0.55f) * 3f);
            float b = Mathf.Clamp01(1.2f - 1.8f * s);
            var c = new Color(r, g, b);
            c = Color.Lerp(c, new Color(0.5f, 0.5f, 0.5f), 0.35f);
            return c * edge * edge;
        }
    }
}
