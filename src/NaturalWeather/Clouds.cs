using System;
using System.Collections.Generic;
using System.IO;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalWeather
{
    /// <summary>
    /// Clouds of the mod's own, drawn as real volumes.
    ///
    /// A cloud is water drops by the billion: sunlight goes into it and comes out again after bouncing many times, so it is white
    /// where the sun is on it, grey where it is deep, darker underneath, and glows at its thin edges with the sun behind it. Here
    /// each pixel's line of sight is walked through a shell of air round the planet (tools/shaderpack/clouds.glsl): where there
    /// is cloud comes from the weather (Keystone.Weather, as a map of the whole planet: how much, what kind, how high, CloudMap;
    /// and the storms near the camera, drawn as towers where the weather has them) carved by noise into heaps, sheets and towers;
    /// the light at each step is the sun's as it comes through the air (CloudAir) and through the cloud between that step and the
    /// sun, with light scattered many times over, the sky's and the ground's, and lightning from inside. This is how the clouds
    /// of Horizon Zero Dawn and Forbidden West, Frostbite and Unreal are drawn (wiki/ksp-weather/Clouds.md).
    ///
    /// The game draws the far scene (from 400 m out) with one camera and the near scene with another. The clouds are walked for
    /// each camera's own stretch, after what it draws itself, so that the craft stands in front of a cloud behind it and is hidden
    /// by one in front: the far camera always, the near one only when a cloud can be within its 400 m. Each walk is drawn at half
    /// the picture's size, its steps begun at a different place each frame, and the frames taken together (where each cloud was in
    /// the last frame is worked out from how far off it is); then laid on the picture.
    ///
    /// Their shadow: worked out for a square of ground round the camera (how much cloud lies towards the sun from each point,
    /// and between what heights). The sun itself is dimmed by as much as the cloud above the craft takes (so the craft, and all
    /// round it, is in the cloud's shade as a whole: its own shadows fade); then every pixel both cameras have drawn is put right
    /// for the cloud above its own place: darker in a shadow the craft is not in, lighter in a gap of sunlight when the craft is
    /// in shade. (So the weather's own figure for how the clouds dim the sun, Keystone.Weather.Shade, must not dim it again while
    /// these clouds are drawn: see <see cref="Drawing"/>.)
    ///
    /// The shaders are the mod's own, packed for the game's OpenGL (Macs and Linux; tools/shaderpack/make_bundle.py). On Direct3D
    /// (Windows) there are none yet unless a build of them is found in the mod's folder, and then no clouds are drawn: the page
    /// says so, and EVE's clouds are left as they are.
    /// </summary>
    /// <summary>The clouds' shaders made ready at the first main menu of a game (Clouds.Ahead), so that the first flight does not wait for them.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class CloudsAhead : MonoBehaviour
    {
        void Start() { try { Clouds.Ahead(); } catch (Exception) { } }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Clouds : MonoBehaviour
    {
        /// <summary>Whether these clouds are being drawn just now (and so dimming the sun themselves, by <see cref="SunThrough"/>).</summary>
        public static bool Drawing;
        /// <summary>How much of the sun's direct light comes through these clouds to the craft, 0 to 1 (1 with none in the way).</summary>
        public static float SunThrough = 1f;
        /// <summary>How far from the camera the nearest cloud or rain can be just now, m (the clouds are laid on the picture no nearer: see Setup).</summary>
        public static float SheetClear;
        /// <summary>Whether the clouds are laid on before the see-through things just now (see LateUpdate, "first").</summary>
        public static bool LaidFirst;
        static bool? scatterer;
        static bool ScattererThere()
        {
            if (scatterer == null)
            {
                scatterer = false;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                    if (a != null && a.name != null && a.name.ToLowerInvariant() == "scatterer") scatterer = true;
            }
            return scatterer.Value;
        }
        public static string Report = "";
        /// <summary>Whether the game draws the scene with two cameras (OpenGL: the far one from 400 m), as these clouds found it last.</summary>
        public static bool Split;
        /// <summary>Called as the clouds finish laying themselves on a camera's picture, with that camera's drawing of them: for what
        /// must be drawn after them, in front of the cloud behind it (lightning's channel seen from under its cloud).</summary>
        public static event Action<CommandBuffer, Camera> InFront;

        static Shader walkShader, keepShader, overShader, shadeShader;
        // (the walk's copies without the weather's rarer features, and the one this frame's sky allows: see Frame, "which copy")
        static Shader walkStorm, walkCalm;
        // (the second form's three copies (clouds2*.bundle; 2026-10-27, a development trial), drawn with while FormTwo)
        static Shader walk2, walkStorm2, walkCalm2;
        /// <summary>The whole walk of the form being drawn: the shadow's map, the light field and the other goes are drawn with it.</summary>
        static Shader WalkWhole => Two ? walk2 : walkShader;
        /// <summary>Whether the second form is drawn: asked for, and its shaders there.</summary>
        static bool Two => FormTwo && walk2 != null;
        Shader walkNow;
        static bool warmed;
#if DEV
        static int CopyHeld; static string CopyNow = "";
        /// <summary>(Development build: "whole", "storm" or "as the sky has it": which copy of the walk draws the clouds; says which does now.)</summary>
        public static string Copy(string which) { string w = (which ?? "").Trim(); CopyHeld = w == "whole" ? 1 : w == "storm" ? 2 : 0; return "the walk drawn by " + (CopyHeld == 1 ? "the whole shader" : CopyHeld == 2 ? "the storm's copy" : "the copy the sky allows") + " (last frame: " + CopyNow + ")"; }
#endif

        /// <summary>
        /// Each copy of the walk drawn once, into a picture of one pixel, when the clouds are first drawn: OpenGL makes a shader
        /// ready for the card only when something is first drawn with it, which for one of this size holds the game up for a
        /// moment. Done here, a storm coming into sight in flight (and the copy for it) does not.
        /// </summary>
        static void Warm()
        {
            if (warmed) return;
            warmed = true;
            RenderTexture pixel = null;
            try
            {
                pixel = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf);
                foreach (Shader copy in new[] { walkStorm, walkCalm })
                {
                    if (copy == null) continue;
                    var m = new Material(copy);
                    Graphics.Blit(null, pixel, m);
                    Destroy(m);
                }
            }
            catch (Exception) { }
            finally { if (pixel != null) RenderTexture.ReleaseTemporary(pixel); }
        }

        /// <summary>
        /// (2026-10-18) Every one of the clouds' shaders drawn once, into a picture of one pixel, at the main menu (CloudsAhead):
        /// this Mac's OpenGL compiles and links a program when it is first drawn with (the rendering chat's lab: 78 ms for the walk
        /// natively, 145 under Rosetta), and the walk has three copies besides the laying and the shadow. About half a second, once
        /// a game, that held up the first cloudy frame of the first flight (the climb's read-out: "each camera" 547 ms at worst).
        /// The shaders are kept for the whole game, and their programs with them.
        /// </summary>
        public static void Ahead()
        {
            if (warmed || (Weather.On != null && !Weather.On) || (CloudsPage.On != null && !CloudsPage.On)) return;
            if (!tried) LoadShaders();
            if (walkShader == null) return;
            warmed = true;
            RenderTexture pixel = null;
            var took = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                pixel = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf);
                foreach (Shader one in new[] { walkShader, walkStorm, walkCalm, keepShader, overShader, shadeShader })
                {
                    if (one == null) continue;
                    var m = new Material(one);
                    Graphics.Blit(null, pixel, m);
                    Destroy(m);
                }
            }
            catch (Exception) { }
            finally { if (pixel != null) RenderTexture.ReleaseTemporary(pixel); }
            Debug.Log("[Natural Weather] clouds: their shaders made ready ahead, in " + took.ElapsedMilliseconds + " ms");
        }
        static bool tried;
        static string why = "";
        static RenderTexture shape, detail;     // the noise, made once for the whole game
        static bool noiseHasMips;
        // The heaps made beforehand (tools/clouds/heapfield.swift, PluginData/heaps.field): a square of sky that repeats, read by the
        // shader in place of heaps carved from the noise. None (the file missing, or the setting off): the noise's heaps, as before 0.3.0.
        static Texture3D field, noField;
        static bool fieldTried;
        // The second form's billows (made on the graphics card at load: see MakeBillow) and its pattern of where clouds stand
        // (PluginData/cover.pattern: see LoadPattern), made and read only once the second form is asked for; and the pattern's
        // stand-in where there is none (NoPattern).
        static RenderTexture billow;
        static Texture2D pattern, noPattern;
        static bool patternTried;
#if DEV
        // (the pattern as until 2026-11-06, its small heaps 330 m to 8 km across, for SpecOld's 128: PluginData/cover-2026-11-05.pattern,
        // which no download carries; tools/clouds/pattern.py's `old` makes it again, written into the test copy's own PluginData
        // (or git, as of 9796b5d8). Only in the development build, read the first time it is asked for.)
        static Texture2D patternOld;
        static bool patternOldTried;
#endif
        static string patternWhy = "not read yet";
        static float fieldSide = 6400f, fieldFar = 240f;
        static readonly float[] fieldLevels = new float[16];
        static string fieldWhy = "";
        /// <summary>(Development build: how many metres the small lumps stand proud of the creases between them.)</summary>
        public static float FieldLumps = 80f;
        /// <summary>How the heaps come, held for a test: 0 a sky of many small puffs, 1 the usual sky; below nought: as the day has it.</summary>
        /// <summary>How far a heap that reaches the layer's top spreads out under it, m; and how far the heaps' outlines are pushed about, m (the usual day's).</summary>
        public static float LidSpread = 225f, Pushed = 300f;
        /// <summary>How many times larger over the ground a shallow layer's heaps are read on the usual day (broad, flat-topped).</summary>
        public static float Broader = 2.3f;
        public static float HeldHabit = -1f;
        float habit = float.NaN;
#if DEV
        /// <summary>(Development build: "puffs", "usual" or anything else to let the day decide.)</summary>
        public static string Habit(string which) { HeldHabit = which == "puffs" ? 0f : which == "usual" ? 1f : -1f; if (instance != null) instance.habit = float.NaN; return "the heaps: " + (HeldHabit < 0f ? "as the day has them" : HeldHabit < 0.5f ? "many small puffs" : "the usual sky"); }
#endif
        /// <summary>
        /// How the low and the middle clouds are shaped. 1: as domes over rising air with lumps on them, worked out in metres
        /// (the rework of 2026-10-12, see the shader: formAt); 0: as before it (heaps made beforehand, sheets carved from noise).
        /// </summary>
        public static float Form = 1f;
        /// <summary>
        /// The clouds' second form (2026-10-27, a development trial): the low and middle clouds shaped as Blackrack's are, by a pattern
        /// saying how much of the sky over a place is cloud and billows eating into that (clouds.glsl, fieldAt; his method learned from
        /// his files, research/clouds-r5/blackrack-source.md), drawn with the walk's copies of their own (clouds2*.bundle) where they are
        /// there. Off: the first form (formAt), as released.
        /// </summary>
        public static bool FormTwo;
#if DEV
        /// <summary>(Development build: "two" draws the clouds' second form, anything else the first.)</summary>
        public static string Shapes(string which)
        {
            bool was = FormTwo;
            FormTwo = (which ?? "").Trim() == "two";
            if (was != FormTwo && instance != null) instance.map.Forget();
            if (!tried) LoadShaders();
            return "the clouds' shapes: " + (!FormTwo ? "the first form" : walk2 != null ? "the second form (billows " + (billow != null ? "made" : "not made yet") + ", pattern " + (pattern != null ? "read" : patternWhy) + ")" : "the second form asked for, but its shaders are not there: the first");
        }
#endif
        /// <summary>
        /// The second form's light (the shader's _CloudLight2): how bright the sun's light is in its clouds, his lobes' own weights
        /// times this; and its sky's and its ground's light, as many times as made. (The gain fitted on his frame at the pad,
        /// 2026-10-27, the cumulus held there: the cloud's middle brightness on the screen 190 of 255 at 1.1 against his 188, 185 at
        /// 0.9, 177 at 0.7 and 163 at 0.5; its brightest tenth no higher than 208 at any of them, against his 215, held down by the
        /// shoulder the clouds are laid on with (cloudsover.glsl). The sky's seven times, fitted on the same frame once the light
        /// towards the sun read the billows as coarsely as his and the sky had the air's own colour (the evening of 2026-10-27): the
        /// darkest tenth 146, the middle 187 and the colour 180 182 189 against his 142, 188 and 180 184 191; at once, 157, 188 and
        /// 184 184 185, neutral where his are blue; at ten, 155, 192 and 186 187 192. The sky five times since 2026-11-08: with a
        /// sheet's top lit at its lumps (clouds.glsl, deckTop), at seven the deck_above view's deck sat so near white that its lumps
        /// were lost, at five they show; and on the light as it is now five brings the cloud's darkest tenth nearer his at the pad
        /// and over the cumulus field, its middle and brightest tenth within one to five of his as at seven (OpenGL, recordings/
        /// 2026-11-08-pc/sky-pad-gl and sky-cumulus_field-gl, the darkest tenth, middle and brightest tenth): at the pad 184, 202,
        /// 211 against his 174, 201, 215 (seven 187, 205, 212; three and a half 180, 199, 209); over the cumulus field 187, 206,
        /// 216 against his 187, 208, 221 (seven 190, 208, 216; three and a half 184, 204, 216).)
        /// </summary>
        public static float LightGain = 1f, LightSky = 5f, LightGround = 1f;
        /// <summary>The second form's sun above the air, as a colour (the shader's _CloudSunTint): its red, green and blue; the air's
        /// own table colours it further on the way down. White, as until 2026-11-08: made warmer (its green and blue 0.97 and 0.93
        /// of its red, down to 0.93 and 0.84) it moved the clouds' brightest tenth by at most 1 of 255. (Development builds:
        /// Clouds.Light2 "sung=0.97 sunb=0.92", its green and blue against its red.)</summary>
        public static Vector3 SunTint2 = new Vector3(1f, 1f, 1f);
        /// <summary>The second form's shoulder rolled over by a colour's brightest channel, the others in proportion, so that its hue
        /// is kept, where the colour is little tinged (a sunlit top, cream), and by each channel by itself as before where it is
        /// strongly tinged (the far bands near the horizon, which kept would be salmon) (cloudsover.glsl; _CloudOver.w's 8), since
        /// 2026-11-08: each channel by itself, a sunlit top's warmth was lost near white, the clouds' brightest tenth all but grey
        /// where Blackrack's are cream (red less blue +1 to +3 of 255 against his +5 to +15 in the five views with clouds; now +4
        /// to +7 on Direct3D). False sets it back (Light2 "hue=0").</summary>
        public static bool HueKept = true;
        /// <summary>How much of the sky's own colour the second form's air keeps in its glow between the camera and a cloud (the
        /// shader's _CloudSky.w; the rest a grey as bright; the first form's all of it). A half since 2026-11-09 (TODO item 16): all of
        /// it, the far bands near the horizon were pink and lilac where Blackrack's are cream, the share of the clouds' bright
        /// pixels pink 9 to 24 % in four of the five views with clouds where his are 0 to 4 %; at a half 0.03 to 3.5 %, cream
        /// (OpenGL and Direct3D alike; TODO.md); at a quarter and less the bands go yellow. (Development builds: Clouds.Light2
        /// "hazet=1" as before.)</summary>
        public static float HazeTint2 = 0.5f;
        /// <summary>The second form's air between the camera and a cloud (clouds.glsl's _CloudHaze): x how many times one scattering's
        /// light the air adds, y how strongly it dims and veils the cloud behind it (1 the air as it is, 0 none).</summary>
        public static Vector2 HazeAir2 = new Vector2(1f, 1f);
        /// <summary>How much of the sky's own colour the second form's sky light on its clouds keeps, the rest a white as strong as
        /// it (the shader's _CloudAirHigh.w): 0.7, his tint (his skylightTintMultiplier). (Development builds: Clouds.Light2
        /// "skyt=0.7".)</summary>
        public static float SkyTint2 = 0.7f;
        /// <summary>How deep the second form draws a low deck of stratus or stratocumulus at the least (m): from the weather's own
        /// base up, kept 300 m under a middle layer over it (CloudsMap; the shell walked through with it). 1700 since 2026-11-09
        /// (TODO item 16), Blackrack's: at the deck_above view our weather has stratus over 998-1487 m, his 1750-3450 m, and
        /// the shader's deck (its domes, its billows' depth, its light) was fitted on his heights. The weather itself keeps its
        /// decks 300 to 600 m deep, as real stratus mostly is, and its base, where rain falls from and a pilot comes out under
        /// it, is the weather's: the first form draws them so. 0: as the weather has them. (Development builds: Clouds.Light2
        /// "deckd=0".)</summary>
        public static float DeckDeep2 = 1700f;
        /// <summary>How the second form's light scattered many times falls off into a low deck's top, where the creases between its
        /// lumps lie deeper in the cover than their crowns (clouds.glsl, the second form's light over a sheet's top; the shader's
        /// _CloudDeck2): x, one less the depth inside to this power; y, never under this share of it. 1.25 and nothing kept since
        /// 2026-11-09 (TODO item 16), with DeckRelief2 at 2: at the square, as first written (2026-11-08), Blackrack's deck at
        /// the deck_above view (CloudsStandIn's st=0.63,1750,3450) was drawn with puffy domes and deep creases, dark and lilac:
        /// its shading (creases.py: luma less its blur over the cloud alone, far / middle distance / nearer) 9.0 / 10.1 / 13.1
        /// against his 10.2 / 9.5 / 8.0, its nearer creases' red less blue -14 against his -2. Now 7.5 / 8.5 / 10.8 and -2,
        /// softer and grey as his (Direct3D, recordings/2026-11-09-pc/crease-d3d3). Lower, at 1, the nearer band his (7.8) but
        /// the deck flatter than his further off (6.4 / 6.6); a floor kept (0.25) flattened it all. (Development builds:
        /// Clouds.Light2 "creasep=2 creasef=0" as first written.)</summary>
        public static Vector2 DeckCrease2 = new Vector2(1.25f, 0f);
        /// <summary>How much more the second form's billows eat into a low deck's upper part than below (clouds.glsl, fieldAt; the
        /// shader's _CloudDeck2.z): its billows' depth there times one and this. 1, twice as deep, as first written (2026-11-08);
        /// 2, three times, since 2026-11-09 with DeckCrease2's softer creases, which alone flattened the deck in the middle
        /// distance (6.9 at 1.5, 8.5 at 2, 8.9 at 3, his 9.5). (Development builds: Clouds.Light2 "relief=1" as first
        /// written.)</summary>
        public static float DeckRelief2 = 2f;
        /// <summary>The second form's billows read this many times finer than made (a development knob for fitting their size on his
        /// frames; 1 as made). A half since 2026-11-06: the low heaps' cube 4.5 km across, its largest cells 2.25 km, as Blackrack's
        /// 2300 m (research/clouds-r5/his-code-2026-11-05.md), now the heaps stand on columns as deep as his and his low, flat cover
        /// (clouds.glsl, coverCurve). At 1, filmed beside his that day (D:\ksp-ai-bridge-films\2026-11-06-pc-billow-*.jpg), every
        /// crown was ragged with spikes and loose puffs where his are round and full; at a half they are round, and a tower one
        /// rounded mass.</summary>
        public static float BillowFiner = 0.5f;
        /// <summary>
        /// How fast the second form's billows rise through its clouds, metres a second, the clouds themselves standing (after the
        /// owner's spec of Blackrack's clouds, 2026-11-04: his noise climbs at 5 m/s through a cover that stays put, the only life his
        /// clouds have; ours were carved statues sliding with the wind). Under time warp no faster than twice that.
        /// </summary>
        public static float RiseSpeed = 5f;
        /// <summary>For setting one against the other: true lays the clouds' smaller picture on with its four dots taken alike whatever
        /// stood in front of each, as until 2026-11-04 (a band of the clouds behind a craft cut away round its outline: cloudsover.glsl).</summary>
        public static bool EdgesAsBefore;
        /// <summary>For setting one against the other: true rolls the second form's brightest over from 0.7 as the first form's, as until
        /// 2026-11-05 (from 0.82 after the owner's spec: brighter tops; cloudsover.glsl, the shoulder).</summary>
        public static bool TopsAsBefore;
        /// <summary>For setting one against the other (the shader's _CloudSpec.w): 1 the billows read as coarsely as a pixel, 2 a heap's
        /// water thickening over its lowest 1.5 km, as until 2026-11-04; 4 no towers among the heaps, every heap of a layer over one
        /// column, as until 2026-11-05 (clouds.glsl, towerOf); 8 the sky's light on a place dimmed by its own column alone, not by
        /// every cloud over it (the light field's sky part), as until 2026-11-05; 16 a storm's anvil the lens drawn round a hull, as
        /// until 2026-11-05 (clouds.glsl, cellTwo); 32 the anvil as dense by its tower as along its trail (AnvilCore), as until
        /// 2026-11-05; 64 a heap's cover as a sheet's, the power 1.6, not Blackrack's low flat cover, as until 2026-11-06 (clouds.glsl,
        /// coverCurve); 128 the small heaps of the pattern 330 m to 8 km across and faded out over a heap's lowest 900 m, as until
        /// 2026-11-06 (clouds.glsl, smallOf; the pattern's old file, patternOld, in the development build alone); 256 a low deck's top,
        /// 512 its light and 1024 its billows as until 2026-11-07 (clouds.glsl, formDeck, deckLight, deckFine); 2048 a sheet's top
        /// lit with the dark edge, as until 2026-11-08 (clouds.glsl, deckTop); 4096 a sheet's billows as deep over its upper
        /// part as below, and 8192 a deep deck's billows twice as fine as a shallow one's, as until 2026-11-08 (clouds.glsl,
        /// deckDeep, deckWide); added. 0: after the spec.</summary>
        public static int SpecOld;
        /// <summary>The second form's anvil (clouds.glsl, cellTwo, its anvil his way; the shader's _CloudAnvil): x, how far in from its
        /// rim its cover is whole, as a share of its last disc's half-width; y, how much of its cover is left where it has been carried
        /// furthest; z, its density, times his; w, where its edge's band begins, as a share of the storm's column. Chosen by eye from
        /// the storm seen from outside, 2026-11-05 (D:\ksp-ai-bridge-films\2026-11-05-pc\anvil-tune2.jpg): whole from 0.15 in, so
        /// that it is thick over the tower and the tower runs up into it; under half left far off, so that its trail thins.</summary>
        public static Vector4 AnvilKnobs = new Vector4(0.15f, 0.45f, 1f, 0.38f);
        /// <summary>How many times denser the anvil is by its tower than out along its trail (clouds.glsl, cellTwo; sent in
        /// _CloudAnvil.z's hundreds, in tenths). Seen from under it, 2026-11-05, an anvil of his density all through was a bright
        /// white sheet overhead, lit nearly as its top is: some three e-folds of cloud between its underside and the sun, where a
        /// real anvil near its storm holds tens and its underside is grey. Three times by the tower, falling to his own density
        /// along the trail, which keeps the trail as it was chosen from outside. SpecOld's 32 sets it back.</summary>
        public static float AnvilCore = 3f;
        Vector3d rise;
        Vector4 specNow;
        double riseAt = double.NaN;
#if DEV
        /// <summary>The second form's light, set while the game runs ("gain=0.35 sky=1 ground=1", any of them; Light2 by the bridge),
        /// and its billows' size ("billow=1.4": that many times finer); what they are now.</summary>
        public static string Light2(string how)
        {
            foreach (string part in (how ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return "not understood: " + part;
                switch (kv[0].Trim().ToLowerInvariant())
                {
                    case "gain": LightGain = v; break;
                    case "sky": LightSky = v; break;
                    case "ground": LightGround = v; break;
                    case "billow": BillowFiner = Mathf.Clamp(v, 0.25f, 4f); break;
                    case "rise": RiseSpeed = Mathf.Clamp(v, 0f, 50f); break;
                    case "old": SpecOld = Mathf.Clamp((int)v, 0, 16383); break;
                    case "edges": EdgesAsBefore = v > 0.5f; break;
                    case "tops": TopsAsBefore = v > 0.5f; break;
                    case "acover": AnvilKnobs.x = Mathf.Clamp(v, 0.05f, 2f); break;
                    case "afar": AnvilKnobs.y = Mathf.Clamp01(v); break;
                    case "adense": AnvilKnobs.z = Mathf.Clamp(v, 0f, 5f); break;
                    case "alow": AnvilKnobs.w = Mathf.Clamp(v, 0.1f, 0.6f); break;
                    case "acore": AnvilCore = Mathf.Clamp(v, 1f, 9f); break;
                    case "sung": SunTint2.y = Mathf.Clamp(v, 0.5f, 1.2f); break;
                    case "sunb": SunTint2.z = Mathf.Clamp(v, 0.5f, 1.2f); break;
                    case "hue": HueKept = v > 0.5f; break;
                    case "hazet": HazeTint2 = Mathf.Clamp01(v); break;
                    case "try": Trial = v; break;
                    case "hazez": HazeAir2.x = Mathf.Clamp(v, 0f, 4f); break;
                    case "hazew": HazeAir2.y = Mathf.Clamp(v, 0f, 4f); break;
                    case "skyt": SkyTint2 = Mathf.Clamp01(v); break;
                    case "deckd": DeckDeep2 = Mathf.Clamp(v, 0f, 4000f); if (instance != null) instance.map.Forget(); break;
                    case "creasep": DeckCrease2.x = Mathf.Clamp(v, 0.1f, 4f); break;
                    case "creasef": DeckCrease2.y = Mathf.Clamp01(v); break;
                    case "relief": DeckRelief2 = Mathf.Clamp(v, 0.05f, 4f); break;
                    default: return "not understood: " + part;
                }
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "the second form's light: gain {0}, sky {1}, ground {2}; billows {3} times finer, rising {4} m/s; old ways {5}; the anvil's cover whole {6} in, {7} of it far off, density {8} ({10} times that by the tower), from {9} of the column; the sun above the air {11}, {12}, {13}; the tops' hue {14}; the air's glow {15} of the sky's colour, the sky's light on the clouds {16}; low decks at least {17} m deep, their creases to the power {18}, kept at least {19}, their billows {20} more over their upper part", LightGain, LightSky, LightGround, BillowFiner, RiseSpeed, SpecOld, AnvilKnobs.x, AnvilKnobs.y, AnvilKnobs.z, AnvilKnobs.w, AnvilCore, SunTint2.x, SunTint2.y, SunTint2.z, HueKept ? "kept" : "rolled out by channel", HazeTint2, SkyTint2, DeckDeep2, DeckCrease2.x, DeckCrease2.y, DeckRelief2);
        }
#endif
        /// <summary>How long a heap lives, from a puff at the base to gone, in seconds.</summary>
        public static float HeapLife = 1500f;
        /// <summary>
        /// The map of the low layer's heaps, drawn once a frame over the ground round the camera and read by every asking of
        /// the walk (the shader's go 5): how many dots each way (under 64: no map, and the heaps are worked out at every
        /// asking, which costs a quarter of the frames among them), and how many metres it is across at the least (from above all
        /// the clouds it is wider: see HeapsWidth). It comes round, so it must be wider than twice as far as heaps are drawn (26 km).
        /// </summary>
        public static int HeapsDots = 1024;
        public static float HeapsAcross = 51200f;
        /// <summary>
        /// Whether what is kept of the clouds from frame to frame is as fine as the screen where the walk is done at half
        /// its size (each frame's lines of sight then go through a different one of the four pixels in turn).
        /// </summary>
        public static bool Finer = true;
        /// <summary>
        /// A table of numbers got by chance, 256 by 256, four to a place: for each point of the heaps' lattice, when its heaps
        /// come, how they rank in size, and how far out of place they stand. Made the same way everywhere (and in the lab,
        /// cloudlab.swift), so that a sky is the same sky on every machine.
        /// </summary>
        static Texture2D cellsTable;
        static uint MixBits(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
        static void MakeCellsTable()
        {
            var px = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    uint h = unchecked(MixBits((uint)x + MixBits((uint)y + 977u)));
                    px[x + y * 256] = new Color32((byte)(h & 255u), (byte)((h >> 8) & 255u), (byte)((h >> 16) & 255u), (byte)(h >> 24));
                }
            cellsTable = new Texture2D(256, 256, TextureFormat.RGBA32, false, true) { name = "Natural Weather: the heaps' lattice", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            cellsTable.SetPixels32(px);
            cellsTable.Apply(false, true);
        }
#if DEV
        /// <summary>(Development build: "old" for the clouds' shapes as before the rework of 2026-10-12, anything else for the new.)</summary>
        /// <summary>(Development build: a number the shader's trials go by, for finding a fault; 0 for none.)</summary>
        public static float Trial;
        /// <summary>(Development build: "off" draws the clouds as they were drawn before the far field of 2026-10-14, for pictures of before and after; "on" as now.)</summary>
        public static bool FarOff;
        public static string Far(string which) { FarOff = which != null && which.Trim() == "off"; return FarOff ? "the clouds' far field off (as before 2026-10-14)" : "the clouds' far field on"; }
        /// <summary>(Development build: "off" draws the clouds with no storms in them; "on" as the weather has it.)</summary>
        public static bool NoStorms;
        public static int StormsSent = 6;
        public static string StormsSaid = "";
        public static string Storms(string which)
        {
            string w = (which ?? "").Trim();
            NoStorms = w == "off";
            StormsSent = int.TryParse(w, out int k) ? Math.Max(0, Math.Min(6, k)) : 6;
            return (NoStorms ? "no storms drawn" : "storms drawn: " + StormsSent) + " | " + StormsSaid;
        }
#if DEV
        /// <summary>(Testing: the walk's steps times this; for the grey band at cloud height, whether the walk runs out of steps.)</summary>
        public static float StepsTimes = 1f;
        public static string Steps(string v) { StepsTimes = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return "steps x" + StepsTimes; }
#endif
        public static string Try2(string which) { Trial = float.Parse(which, System.Globalization.CultureInfo.InvariantCulture); return "trial " + Trial; }
#endif
        /// <summary>(Development build, for finding a fault: 1 hands the shader an empty field while telling it there is one.)</summary>
        public static float FieldTest;
        public static string Test(string arg)
        {
            FieldTest = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            // (2: the field read from its file again)
            if (FieldTest >= 2f) { if (field != null) Destroy(field); field = null; fieldTried = false; }
            return "field test " + FieldTest;
        }
        Vector2d fieldDrift;            // how far the wind has carried the field of heaps, east and north (m, kept within its side)
        static Mesh sheet;

        // (in methods of their own: a Keystone from before 2026-10-18 has no FarMotion, and it is the method that names it that fails;
        // with one, the clouds go on as before, and the smoothing between frames follows the ground under them)
        static bool farMotionMissing;
        static void TellFarMotion(Texture picture, Texture distance)
        {
            if (farMotionMissing) return;
            try { FarMotionClouds(picture, distance); } catch (Exception) { farMotionMissing = true; }
        }
        static void FarMotionClouds(Texture picture, Texture distance) => Keystone.FarMotion.Clouds(picture, distance);

        /// <summary>One camera's clouds.</summary>
        sealed class Pass
        {
            public string Name;
            public Camera Cam;
            public CommandBuffer Draw, Shade;
            /// <summary>
            /// Once the camera's depth picture is made and before it draws its own picture: the far camera's maps (the low
            /// clouds' map smoothed, the heaps' map, the shadow's strip, the light field's band) and the walk and its taking
            /// together, each into a picture of its own. (Until the evening of 2026-10-18 the maps were drawn after the camera's
            /// solid things and the walk before its see-through ones: twice in the middle of the far camera's own picture, which
            /// was put away and taken up again round each, a whole picture and its depths each way. The main chat's measure of
            /// the pad frame: bound by the render thread, four tenths of it spent beginning such passes.)
            /// </summary>
            public CommandBuffer Early;
            /// <summary>After everything see-through: what is drawn in front of the clouds (the lightning), and the clouds' picture taken back from the other mods.</summary>
            public CommandBuffer After;
            public RenderTexture Now, NowD, HistA, HistDA, HistB, HistDB, Shown, ShownD;
            public bool Flip;
            public Material Walk, Together, Lay, Over, Shadow;
            public bool AskedDepth, On;
            /// <summary>When in the camera's drawing the clouds are laid on (see Setup, "first").</summary>
            public CameraEvent When = CameraEvent.BeforeForwardAlpha;
            /// <summary>When the clouds' own pictures are made (see EarlyFor).</summary>
            public CameraEvent EarlyWhen = CameraEvent.AfterDepthTexture;
            // where the camera was last frame (the planet's axes), and how it looked
            public Vector3d PrevAt;
            public double PrevUt;
            public Vector3 PrevRight, PrevUp, PrevAhead;
            public bool HavePrev;
            /// <summary>Frames since the camera last jumped (or first looked): see where the frames are taken together.</summary>
            public int SinceCut;
            public string PrevBody = "";
            /// <summary>What the far camera's kept picture is held to (the planet's axes: right and up over the tangents of half its width
            /// and height of view, and ahead), the field of view it was held at, and whether it is held to anything yet (Setup).</summary>
            public Vector3 HeldRight, HeldUp, HeldAhead;
            public float HeldFov;
            public bool HeldOk;
        }

        readonly Pass far = new Pass { Name = "far" }, near = new Pass { Name = "near" };
        int shadowStrip = -1;                   // which quarter of the shadow's map is drawn this frame (less than nothing: all of it)
        RenderTexture shadowMap, shadowMapB;    // (B: for the walk, how high the sunlit surface stands on each line to the sun)
        Material shadowWalk;
        RenderTexture heapsMap, heapsMapB;      // the map of the low layer's heaps (see HeapsDots)
        Material heapsWalk;
        // ---- the sun-side light field (2026-10-18; research/rendering/experiments/cloud-light-field.md). For cells round the
        // camera (three levels of 128 x 128, the finest 75 m, each next four times as wide; 32 heights through the shell), how much
        // of the sun's light comes through the cloud between each cell and the sun (the shader's go 9). The walk reads it after
        // its own first step towards the sun, in place of its other steps that way, its far looks and the shadow's second map:
        // closer to the truth, and 10-31 % less of the walk, in the lab. Two pictures: the walk reads one made whole during the
        // last round of 64 frames while the next is made, a 64th a frame (a band of rows, in the far camera's pass before its
        // own picture: Pass.Early), round where the camera will be while it is read, fixed to the planet for the round.
        public static bool LightField = true;
#if DEV
        /// <summary>(Development build, for timing the field against walking to the sun: "off" walks, anything else uses the field.)</summary>
        public static string Field(string on) { LightField = on == null || on.Trim() != "off"; return "light field " + (LightField ? "on" : "off"); }
        /// <summary>(Development build) the light field as read now, level by level: the sun's part (its four stretches' light let
        /// through, the mean and the least) and the sky's (how much of the sky's light comes down past the clouds over a cell).</summary>
        public static string FieldSays()
        {
            Clouds c = instance;
            if (c == null || c.lightRead == null) return "no light field";
            var was = RenderTexture.active;
            var t = new Texture2D(c.lightRead.width, c.lightRead.height, TextureFormat.RGBAHalf, false, true);
            try
            {
                RenderTexture.active = c.lightRead;
                t.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0, false);
                t.Apply(false);
            }
            finally { RenderTexture.active = was; }
            Color[] px = t.GetPixels();
            Destroy(t);
            int w = c.lightRead.width, perLevel = LightHeights / 4 * LightCells, half = perLevel * LightLevels;
            var b = new System.Text.StringBuilder("ready " + c.lightReady + ", " + w + " x " + c.lightRead.height + ":");
            for (int level = 0; level < LightLevels; level++)
            {
                double sunSum = 0, skySum = 0; float sunLeast = 1f, skyLeast = 1f, skyMost = 0f; int n = 0, under = 0;
                for (int y = level * perLevel; y < (level + 1) * perLevel; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color s = px[y * w + x], k = px[(y + half) * w + x];
                        float sun = s.r * s.g * s.b * s.a;
                        sunSum += sun; sunLeast = Mathf.Min(sunLeast, sun);
                        skySum += k.r; skyLeast = Mathf.Min(skyLeast, k.r); skyMost = Mathf.Max(skyMost, k.r);
                        if (k.r < 0.9f) under++;
                        n++;
                    }
                b.Append("\n  level " + level + ": the sun's light let through " + (sunSum / n).ToString("F3") + " (least " + sunLeast.ToString("F3") + "); the sky's " + (skySum / n).ToString("F3") + " (" + skyLeast.ToString("F3") + " to " + skyMost.ToString("F3") + "), under 0.9 in " + (100.0 * under / n).ToString("F1") + " %");
            }
            return b.ToString();
        }
        /// <summary>
        /// (Development build, for setting the rendering chat's lab up as the game had it: the far camera's walk's values as last set,
        /// by name, "a,b,c"; with none named, the sun's, the eye's and twist's, the camera's and the shell's.)
        /// </summary>
        public static string Uniforms(string names)
        {
            // ("keep:" first: the far camera's keeping's values, which the walk does not have (_CloudPrevAt, _CloudKeep, _CloudHeld...))
            bool keep = names != null && names.StartsWith("keep:");
            if (keep) names = names.Substring(5);
            Material m = instance != null ? (keep ? instance.far.Together : instance.far.Walk) : null;
            if (m == null) return "no far walk yet";
            string[] want = string.IsNullOrEmpty(names) || names.Trim().Length == 0
                ? new[] { "_CloudSun", "_CloudSunTint", "_CloudSunFixed", "_CloudEyeA", "_CloudEyeB", "_CloudEyeC", "_CloudEyeD", "_CloudTwistA", "_CloudTwistB", "_CloudCam", "_CloudHigh", "_CloudShell", "_CloudShellEased", "_CloudForm", "_CloudGround", "_CloudAir", "_CloudAirHigh", "_CloudLightF", "_CloudLightG", "_CloudJitter" }
                : names.Split(',');
            var b = new System.Text.StringBuilder((keep ? "the far keeping (" : "the far walk (") + (m.shader != null ? m.shader.name : "?") + "):");
            foreach (string one in want)
            {
                string n = one.Trim();
                if (n.Length > 0) b.Append("\n  ").Append(n).Append(" = ").Append(m.GetVector(n).ToString("G7"));
            }
            return b.ToString();
        }
#endif
        const int LightCells = 128, LightLevels = 3, LightHeights = 32, LightRound = 64;
        const float LightCell = 75f;
        RenderTexture lightRead, lightMaking;
        Material lightWalk;
        int lightBand = -1;                 // the band of rows of the field being made that is drawn this frame (less than nothing: none begun)
        bool lightReady;                    // whether lightRead holds a whole field
        bool lightHigh;                     // whether the camera is far above the clouds, where the field is neither made nor read
        string lightBody;
        Vector3d lightMid, lightEast, lightNorth, readMid, readEast, readNorth;   // (where each lies: the planet's own axes)
        float lightLow, lightStep, readLow, readStep;                            // (its heights: the lowest, m above the radius, and how far apart)
        Vector3d lightCamWas, lightGoes;    // (where the camera was last frame, the planet's axes; and how fast it goes over the planet, m/s, smoothed)
        bool lightCamKnown;
        static readonly int idLight = Shader.PropertyToID("_CloudLight"), idLightF = Shader.PropertyToID("_CloudLightF"), idLightG = Shader.PropertyToID("_CloudLightG"),
            idLightA = Shader.PropertyToID("_CloudLightA"), idLightE = Shader.PropertyToID("_CloudLightE"), idLightN = Shader.PropertyToID("_CloudLightN");

        /// <summary>
        /// How wide the heaps' map is laid this frame, m: at least as wide as asked, and its least width times a power of two.
        /// Laid at just four times the camera's height, as it was until 2026-10-18, its dots moved over the heaps in every frame
        /// of a climb, and from 13 km up, where the walk still reads the map, a few hundred pixels at the heaps' edges changed
        /// by up to a third of white from one frame to the next: the heaps below flickered while the camera rose (the owner,
        /// 2026-10-18: "the clouds flicker a little bit" going up) and were still when it held its height. Laid in powers of two
        /// its dots stay where they are on the heaps between doublings, and at a doubling the picture changes once, by about
        /// what three of those frames did (research/rendering/experiments/heaps-map-width.md). Halved again only a tenth below
        /// where the half would do, so that a camera holding its height near a doubling does not go to and fro between two.
        /// </summary>
        float HeapsWidth(double wanted)
        {
            if (heapsLeast != HeapsAcross || heapsWide < HeapsAcross) { heapsLeast = HeapsAcross; heapsWide = HeapsAcross; }
            while (heapsWide < wanted && heapsWide < 800000f) heapsWide *= 2f;
            while (heapsWide > HeapsAcross && wanted < 0.45 * heapsWide) heapsWide *= 0.5f;
            return heapsWide;
        }

        static RenderTexture MakeLight(string what)
        {
            // (twice as tall as its levels: the sun's half, then the sky's (how much of the sky's light comes down past the clouds over
            // each cell, 2026-11-05), the same cells in the same places)
            var t = new RenderTexture(4 * LightCells, 2 * (LightHeights / 4 * LightCells * LightLevels), 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the light field (" + what + ")" };
            t.Create();
            return t;
        }

        /// <summary>A field's place and heights handed to a material: its middle and axes from the planet's own axes into the scene's.</summary>
        static void SetLight(Material m, Vector3d mid, Vector3d east, Vector3d north, float low, float step, float on, Vector3d fx, Vector3d fy, Vector3d fz)
        {
            Vector3 Scene(Vector3d v) => (Vector3)(fx * v.x + fy * v.y + fz * v.z);
            // (Sixteen readings beyond the first 739 m towards the sun, not eight. Under a second layer far up that way (an overcast's
            // stratus under its altostratus) eight gave it two readings, each cell its own two at its own places, and seen from
            // above the deck the shade between the upper layer's lumps was a checker of the field's 75 m cells (2026-10-19). The
            // field is a third dearer to make, about 0.07 ms a frame.)
#if DEV
            m.SetVector(idLightF, new Vector4(LightCells, LightLevels, LightHeights, TestFieldFar));
#else
            m.SetVector(idLightF, new Vector4(LightCells, LightLevels, LightHeights, 16f));
#endif
            m.SetVector(idLightG, new Vector4(LightCell, low, step, on));
            m.SetVector(idLightA, Scene(mid)); m.SetVector(idLightE, Scene(east)); m.SetVector(idLightN, Scene(north));
        }
        // (the low clouds' map, drawn smoothly once a frame at four times the weather's own size, which the walk reads: the
        // shader's stage 6. Read at the weather's own size, the edges of the clouds' regions seen from orbit were staircases.)
        RenderTexture mapFine;
        Material mapFineWalk;
        bool heapsOn;
        float heapsWide, heapsLeast;    // (the heaps' map's width as it is laid now, and the least width it was laid from: see HeapsWidth)
        float shellTopNow = 12000f;     // (the shell's top as last handed to the walk, m: the shadow's square is left off above it)
        float easedBottom = float.NaN, easedTop;        // (the shell's floor and top as eased: see Frame)
        string easedBody;
        static readonly int idShellEased = Shader.PropertyToID("_CloudShellEased");
        readonly CloudMap map = new CloudMap();
        readonly List<WeatherCell> cells = new List<WeatherCell>();
        float cellsAt = -100f;
        double cellsUt0, cellsUt1 = double.NegativeInfinity;
        readonly Dictionary<int, WeatherCell> nextById = new Dictionary<int, WeatherCell>();

        /// <summary>A storm as it stands at the moment ut, between how it was asked for now and one step on (see Frame).</summary>
        WeatherCell Between(WeatherCell c, double ut)
        {
            if (!nextById.TryGetValue(c.Id, out WeatherCell n) || cellsUt1 <= cellsUt0) return c;
            float f = Mathf.Clamp01((float)((ut - cellsUt0) / (cellsUt1 - cellsUt0)));
            double dLon = n.Longitude - c.Longitude;
            if (dLon > 180.0) dLon -= 360.0; else if (dLon < -180.0) dLon += 360.0;
            c.Latitude += (n.Latitude - c.Latitude) * f; c.Longitude += dLon * f;
            c.Radius = Mathf.Lerp(c.Radius, n.Radius, f); c.Base = Mathf.Lerp(c.Base, n.Base, f); c.Top = Mathf.Lerp(c.Top, n.Top, f);
            c.Strength = Mathf.Lerp(c.Strength, n.Strength, f); c.Rain = Mathf.Lerp(c.Rain, n.Rain, f); c.Life = Mathf.Lerp(c.Life, n.Life, f);
            return c;
        }
        string cellsBody = "";
        Vector3d drift;                 // how far the wind has carried the clouds (m, the planet's axes; kept within 207 lengths of the noise)
        double driftAt = double.NaN;
        string driftBody = "";
        // (a jump of the camera, decided once a frame in Frame for both cameras' clouds (Setup): where the camera was last frame and
        // the game's time then, the step it took as last learned and the game's time that step took, and whether last frame was one)
        Vector3d jumpWas, jumpStep;
        double jumpUt = double.NaN, jumpDut;
        string jumpBody = "";
        bool jumped, jumpedLast;
        // (a craft switched to, or Cut (development builds): the next frame is a jump, wherever the camera went)
        static bool cutAsked;
        // the wind at the camera (m/s, the planet's axes; nought held still) and the game's time it was taken at: what carried the
        // clouds since each pass's last frame (Setup; a pass's own time, so a frame it missed is carried too)
        Vector3d windNow;
        double windUt;
        // (the far camera's keeping, as multiples of the release's (Keep, development builds); and held to the screen, for comparing)
        static Vector4 keepSaid = Vector4.one;
        static bool heldToScreen;
        // the wind's way as the streets and the high cloud's fibres take it: changed only when the wind has turned well round
        // (the camera's place in the noise is hundreds of lengths from its start, and patterns read along the wind there
        // would swing about with every small turn of it)
        Vector3 streetsAcross, streetsAlong;
        string streetsBody = "";
        int frame;
        // the shadow's square: fixed to the planet until the camera has gone far from its middle
        Vector3d shadowMid;
        Vector3 shadowX, shadowY;
        double shadowSide;
        Vector4 shadeTint = new Vector4(1f, 1f, 1f, 1f);     // (the sun's light over the sky's in each colour, against green's: see Frame)
        string shadowBody = "";
        float groundHeight;
        Vector3 sunNow;
        // the sun, dimmed for the scene's cameras by the cloud above the craft
        Light dimmed;
        float dimmedFrom = -1f;
        float sunDim = 1f;              // what it is dimmed by (eased)
        float sunAbove = 1f;            // what the shadow says lies above the craft (as last read back)
        readonly Vector3d[] flashAt = new Vector3d[2];
        readonly float[] flashBright = new float[2];

        const double NoiseLength = 9000.0;      // metres to one repeat of the large noise (the small one is 9 times finer)
        // The camera's place in the noise wraps round only every 207 lengths of it (1863 km): 207 is a whole number of each of the
        // repeats the shader reads (the large noise's own, a third, a ninth, a 23rd and a 2.3rd of it), so that nothing jumps
        // when it does. (Wrapped at each length, as at first, everything read at other scales jumped whenever the camera
        // crossed one: every 9 km in flight.)
        const double NoiseWrap = 207.0 * NoiseLength;
        const int Shadow = 512;                 // dots along each side of the shadow's square

        // ---------------------------------------------------------------- shader names
        static readonly int idStage = Shader.PropertyToID("_CloudStage"), idCam = Shader.PropertyToID("_CloudCam"), idHigh = Shader.PropertyToID("_CloudHigh"),
            idRight = Shader.PropertyToID("_CloudRight"), idUp = Shader.PropertyToID("_CloudUp"), idAhead = Shader.PropertyToID("_CloudAhead"),
            idFixedX = Shader.PropertyToID("_CloudFixedX"), idFixedY = Shader.PropertyToID("_CloudFixedY"), idFixedZ = Shader.PropertyToID("_CloudFixedZ"),
            idNoise = Shader.PropertyToID("_CloudNoise"), idDrift = Shader.PropertyToID("_CloudDrift"), idShell = Shader.PropertyToID("_CloudShell"),
            idLook = Shader.PropertyToID("_CloudLook"), idLight2 = Shader.PropertyToID("_CloudLight2"), idDeck2 = Shader.PropertyToID("_CloudDeck2"), idSpec = Shader.PropertyToID("_CloudSpec"), idAnvil = Shader.PropertyToID("_CloudAnvil"), idSun = Shader.PropertyToID("_CloudSun"), idSunTint = Shader.PropertyToID("_CloudSunTint"), idMoon = Shader.PropertyToID("_CloudMoon"),
            idSky = Shader.PropertyToID("_CloudSky"), idGround = Shader.PropertyToID("_CloudGround"), idAir = Shader.PropertyToID("_CloudAir"), idAirHigh = Shader.PropertyToID("_CloudAirHigh"),
            idHaze = Shader.PropertyToID("_CloudHaze"), idFlashA = Shader.PropertyToID("_CloudFlashA"), idFlashB = Shader.PropertyToID("_CloudFlashB"), idHoleA = Shader.PropertyToID("_CloudHoleA"), idHoleB = Shader.PropertyToID("_CloudHoleB"), idHoleC = Shader.PropertyToID("_CloudHoleC"),
            idField = Shader.PropertyToID("_CloudField"), idFieldA = Shader.PropertyToID("_CloudFieldA"), idFieldB = Shader.PropertyToID("_CloudFieldB"),
            idFieldLevA = Shader.PropertyToID("_CloudFieldLevA"), idFieldLevB = Shader.PropertyToID("_CloudFieldLevB"), idFieldLevC = Shader.PropertyToID("_CloudFieldLevC"), idFieldLevD = Shader.PropertyToID("_CloudFieldLevD"),
            idFlashTint = Shader.PropertyToID("_CloudFlashTint"), idPrevAt = Shader.PropertyToID("_CloudPrevAt"), idPrevRight = Shader.PropertyToID("_CloudPrevRight"),
            idPrevUp = Shader.PropertyToID("_CloudPrevUp"), idPrevAhead = Shader.PropertyToID("_CloudPrevAhead"), idShadowAt = Shader.PropertyToID("_CloudShadowAt"),
            idShadowX = Shader.PropertyToID("_CloudShadowX"), idShadowY = Shader.PropertyToID("_CloudShadowY"), idSunFixed = Shader.PropertyToID("_CloudSunFixed"),
            idSheet = Shader.PropertyToID("_CloudSheet"), idOver = Shader.PropertyToID("_CloudOver"), idVeilUp = Shader.PropertyToID("_CloudVeilUp"), idShadeAt = Shader.PropertyToID("_CloudShadeAt"),
            idShadeX = Shader.PropertyToID("_CloudShadeX"), idShadeY = Shader.PropertyToID("_CloudShadeY"), idShade = Shader.PropertyToID("_CloudShade"),
            idShadeSun = Shader.PropertyToID("_CloudShadeSun"), idShadeTint = Shader.PropertyToID("_CloudShadeTint"), idStreets = Shader.PropertyToID("_CloudStreets"), idAlong = Shader.PropertyToID("_CloudAlong"),
            idShape = Shader.PropertyToID("_CloudShape"), idDetail = Shader.PropertyToID("_CloudDetail"), idMap = Shader.PropertyToID("_CloudMap"),
            idMapB = Shader.PropertyToID("_CloudMapB"), idMapC = Shader.PropertyToID("_CloudMapC"), idLut = Shader.PropertyToID("_CloudAirLut"), idNow = Shader.PropertyToID("_CloudNow"),
            idNowD = Shader.PropertyToID("_CloudNowD"), idHist = Shader.PropertyToID("_CloudHist"), idHistD = Shader.PropertyToID("_CloudHistD"),
            idShadowMap = Shader.PropertyToID("_CloudShadowMap"),
            idWeatherShadowMap = Shader.PropertyToID("_WeatherShadowMap"), idWeatherShadowAt = Shader.PropertyToID("_WeatherShadowAt"), idWeatherShadowX = Shader.PropertyToID("_WeatherShadowX"),
            idWeatherShadowY = Shader.PropertyToID("_WeatherShadowY"), idWeatherShadowCentre = Shader.PropertyToID("_WeatherShadowCentre"), idWeatherShadowFade = Shader.PropertyToID("_WeatherShadowFade"),
            idWeatherShadowSun = Shader.PropertyToID("_WeatherShadowSun"),
            idOthers = Shader.PropertyToID("_WeatherClouds"), idOthersPic = Shader.PropertyToID("_WeatherCloudsPic"), idOthersDist = Shader.PropertyToID("_WeatherCloudsDist"),
            idFlareCloud = Shader.PropertyToID("scattererReconstructedCloud");
        static readonly int idKeep = Shader.PropertyToID("_CloudKeep");
        static readonly int idAim = Shader.PropertyToID("_CloudAim");
        static readonly int idBlue = Shader.PropertyToID("_CloudBlue");
        static readonly int idHeld = Shader.PropertyToID("_CloudHeld"), idHeldRight = Shader.PropertyToID("_CloudHeldRight"), idHeldUp = Shader.PropertyToID("_CloudHeldUp"),
            idHeldAhead = Shader.PropertyToID("_CloudHeldAhead"), idSeeRight = Shader.PropertyToID("_CloudSeeRight"), idSeeUp = Shader.PropertyToID("_CloudSeeUp"), idSeeAhead = Shader.PropertyToID("_CloudSeeAhead");
        static readonly int[] idCellA = { Shader.PropertyToID("_CloudCellA0"), Shader.PropertyToID("_CloudCellA1"), Shader.PropertyToID("_CloudCellA2"), Shader.PropertyToID("_CloudCellA3"), Shader.PropertyToID("_CloudCellA4"), Shader.PropertyToID("_CloudCellA5") };
        static readonly int[] idCellB = { Shader.PropertyToID("_CloudCellB0"), Shader.PropertyToID("_CloudCellB1"), Shader.PropertyToID("_CloudCellB2"), Shader.PropertyToID("_CloudCellB3"), Shader.PropertyToID("_CloudCellB4"), Shader.PropertyToID("_CloudCellB5") };
        static readonly int[] idCellC = { Shader.PropertyToID("_CloudCellC0"), Shader.PropertyToID("_CloudCellC1"), Shader.PropertyToID("_CloudCellC2"), Shader.PropertyToID("_CloudCellC3"), Shader.PropertyToID("_CloudCellC4"), Shader.PropertyToID("_CloudCellC5") };

        // ---------------------------------------------------------------- loading

        public static string Folder => KSPUtil.ApplicationRootPath + "GameData/NaturalWeather/PluginData/";

        static Shader[] Load(string file)
        {
            string path = Folder + file;
            if (!File.Exists(path)) return null;
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) return new Shader[0];
            Shader[] found = bundle.LoadAllAssets<Shader>();
            bundle.Unload(false);
            return found;
        }

        static Shader Pick(Shader[] found, string name)
        {
            if (found != null) foreach (Shader one in found) if (one != null && one.name == name && one.isSupported) return one;
            return null;
        }

        static void LoadShaders()
        {
            tried = true;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore)
                {
                    // (OpenGL: the shaders packed by hand from tools/shaderpack/*.glsl, one to a file)
                    walkShader = Pick(Load("clouds.bundle"), "NaturalWeather/Clouds");
                    walkStorm = Pick(Load("cloudsstorm.bundle"), "NaturalWeather/CloudsStorm"); walkCalm = Pick(Load("cloudscalm.bundle"), "NaturalWeather/CloudsCalm");
                    walk2 = Pick(Load("clouds2.bundle"), "NaturalWeather/Clouds2");
                    walkStorm2 = Pick(Load("clouds2storm.bundle"), "NaturalWeather/Clouds2Storm"); walkCalm2 = Pick(Load("clouds2calm.bundle"), "NaturalWeather/Clouds2Calm");
                    overShader = Pick(Load("cloudsover.bundle"), "NaturalWeather/CloudsOver");
                    shadeShader = Pick(Load("cloudshade.bundle"), "NaturalWeather/CloudShade");
                    keepShader = Pick(Load("cloudskeep.bundle"), "NaturalWeather/CloudsKeep");
                    if (walkShader == null || overShader == null || shadeShader == null || keepShader == null) why = "the clouds' shaders (GameData/NaturalWeather/PluginData/clouds*.bundle) are missing or this graphics card cannot run them";
                }
                else
                {
                    // (elsewhere, Direct3D on Windows: the same shaders built by the Unity editor from tools/unityshaders, if there is
                    // such a build in the mod's folder; there is none yet)
                    string system = Application.platform == RuntimePlatform.WindowsPlayer ? "windows" : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "mac";
                    Shader[] built = Load("clouds-" + system + ".bundle");
                    walkShader = Pick(built, "NaturalWeather/Clouds"); overShader = Pick(built, "NaturalWeather/CloudsOver"); shadeShader = Pick(built, "NaturalWeather/CloudShade");
                    walkStorm = Pick(built, "NaturalWeather/CloudsStorm"); walkCalm = Pick(built, "NaturalWeather/CloudsCalm");
                    walk2 = Pick(built, "NaturalWeather/Clouds2"); walkStorm2 = Pick(built, "NaturalWeather/Clouds2Storm"); walkCalm2 = Pick(built, "NaturalWeather/Clouds2Calm");
                    keepShader = Pick(built, "NaturalWeather/CloudsKeep");
                    if (walkShader == null || overShader == null || shadeShader == null || keepShader == null)
                        why = built == null ? "there is no cloud shader for " + SystemInfo.graphicsDeviceType + " yet (only for OpenGL, on Macs and Linux)" : "the clouds' shaders for " + SystemInfo.graphicsDeviceType + " did not load";
                }
                if (walkShader == null || overShader == null || shadeShader == null || keepShader == null) { walkShader = keepShader = overShader = shadeShader = null; walkStorm = walkCalm = walk2 = walkStorm2 = walkCalm2 = null; }
            }
            catch (Exception ex) { walkShader = keepShader = overShader = shadeShader = null; walkStorm = walkCalm = walk2 = walkStorm2 = walkCalm2 = null; why = "the clouds' shader did not load (" + ex.Message + ")"; }
            Debug.Log("[Natural Weather] " + (walkShader != null ? "clouds: shaders loaded" : "no clouds: " + why));
            if (sheet == null)
            {
                sheet = new Mesh { name = "Natural Weather sheet" };
                sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
                sheet.triangles = new[] { 0, 1, 2 };
                sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
        }

        /// <summary>
        /// The field of heaps made beforehand, read from its file into a texture of three dimensions with every coarser copy as
        /// the file has it (the copies' second number is not a mean, so the graphics card may not make them). Once.
        /// </summary>
        static void LoadField()
        {
            fieldTried = true;
            if (noField == null)
            {
                noField = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { name = "Natural Weather: no field of heaps" };
                noField.SetPixels32(new[] { new Color32(0, 0, 0, 255) });
                noField.Apply(false, true);
            }
            string path = Folder + "heaps.field";
            try
            {
                if (!File.Exists(path)) { fieldWhy = "there is no " + path; return; }
                byte[] all = File.ReadAllBytes(path);
                const int head = 28 + 4 * 64 + 4 * 64 * 16;
                if (all.Length < head || all[0] != 'N' || all[1] != 'W' || all[2] != 'F' || all[3] != '3') { fieldWhy = "heaps.field is not a field of heaps this version can read"; return; }
                int nx = BitConverter.ToInt32(all, 4), ny = BitConverter.ToInt32(all, 8), nz = BitConverter.ToInt32(all, 12);
                if (nx < 1 || ny < 1 || nz < 1 || nx > 1024 || ny > 1024 || nz > 256) { fieldWhy = "heaps.field has no sensible size"; return; }
                fieldSide = BitConverter.ToSingle(all, 16); fieldFar = BitConverter.ToSingle(all, 24);
                // (the level for each sixteenth of the sky, from the file's sixty-four)
                for (int i = 0; i < 16; i++) fieldLevels[i] = 0.5f * (BitConverter.ToSingle(all, 28 + 4 * (4 * i + 1)) + BitConverter.ToSingle(all, 28 + 4 * (4 * i + 2)));
                // (Four numbers a cell, though the file has three. Read into the two-number kind (RG16), when the file had two, the
                // whole field came out on this Unity's OpenGL as one thick slab of cloud, 2026-10-11.)
                var t = new Texture3D(nx, ny, nz, TextureFormat.RGBA32, true)
                {
                    wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Repeat, wrapModeW = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear, anisoLevel = 0, name = "Natural Weather: heaps made beforehand"
                };
                int at = head, mx = nx, my = ny, mz = nz;
                for (int level = 0; level < t.mipmapCount; level++)
                {
                    int count = mx * my * mz;
                    if (at + 3 * count > all.Length) { Destroy(t); fieldWhy = "heaps.field is cut short"; return; }
                    var px = new Color32[count];
                    for (int n = 0; n < count; n++) px[n] = new Color32(all[at + 3 * n], all[at + 3 * n + 1], all[at + 3 * n + 2], 255);
                    t.SetPixels32(px, level);
                    at += 3 * count;
                    mx = Math.Max(1, mx / 2); my = Math.Max(1, my / 2); mz = Math.Max(1, mz / 2);
                }
                t.Apply(false, true);
                field = t;
                Debug.Log("[Natural Weather] clouds: heaps made beforehand, " + nx + " by " + ny + " by " + nz + " over " + fieldSide.ToString("F0") + " m");
            }
            catch (Exception ex) { field = null; fieldWhy = "heaps.field did not load (" + ex.Message + ")"; }
            finally { if (field == null) Debug.Log("[Natural Weather] clouds: heaps carved from the noise, as before 0.3.0: " + fieldWhy); }
        }

        /// <summary>
        /// The spatiotemporal blue noise the far camera's walk takes its steps' places from (clouds.glsl, blueAt; made by
        /// tools/clouds/stbn.py: 64 squares of 64 by 64 places, eight across and eight up, a byte a place), from PluginData/blue.stbn.
        /// Without the file, each place's own hashed number moved on by the golden ratio from square to square, as the steps were
        /// placed until 2026-10-26, and a word in the log. Once a game.
        /// </summary>
        static Texture2D blueTable;
        static void LoadBlue()
        {
            var bytes = new byte[512 * 512];
            string path = Folder + "blue.stbn";
            bool fromFile = false;
            try { if (File.Exists(path)) { byte[] b = File.ReadAllBytes(path); if (b.Length == bytes.Length) { bytes = b; fromFile = true; } } } catch (Exception) { }
            if (!fromFile)
                for (int t = 0; t < 64; t++)
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            double v = unchecked(MixBits((uint)x + MixBits((uint)y + 7919u)) & 65535u) / 65536.0 + 0.61803399 * t;
                            bytes[((t >> 3) * 64 + y) * 512 + (t & 7) * 64 + x] = (byte)(256.0 * (v - Math.Floor(v)));
                        }
            blueTable = new Texture2D(512, 512, TextureFormat.R8, false, true) { name = "Natural Weather: blue noise", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            blueTable.LoadRawTextureData(bytes);
            blueTable.Apply(false, true);
            Debug.Log("[Natural Weather] clouds: the walk's steps placed by " + (fromFile ? "blue.stbn" : "hashed numbers (no " + path + ")"));
        }

        /// <summary>The two cubes of noise the clouds are carved from, made on the graphics card slice by slice (once).</summary>
        static void MakeNoise()
        {
            if (shape != null && shape.IsCreated() && detail != null && detail.IsCreated()) return;
            var maker = new Material(walkShader) { name = "Natural Weather: noise maker" };
            maker.SetVector(idSheet, new Vector4(1f, 1f, 0.5f, 1f));
            var commands = new CommandBuffer { name = "Natural Weather: the clouds' noise" };
            RenderTexture Cube(int n, string name)
            {
                var t = new RenderTexture(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    dimension = TextureDimension.Tex3D, volumeDepth = n, useMipMap = true, autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = name
                };
                t.Create();
                return t;
            }
            if (shape != null) shape.Release();
            if (detail != null) detail.Release();
            shape = Cube(128, "Natural Weather: large noise");
            detail = Cube(64, "Natural Weather: small noise");
            for (int which = 0; which < 2; which++)
            {
                RenderTexture into = which == 0 ? shape : detail;
                for (int z = 0; z < into.volumeDepth; z++)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetVector(idStage, new Vector4(which, z, 0f, 0f));
                    commands.SetRenderTarget(into, 0, CubemapFace.Unknown, z);
                    commands.DrawMesh(sheet, Matrix4x4.identity, maker, 0, 0, block);
                }
            }
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();
            Destroy(maker);
            noiseHasMips = false;
            try { shape.GenerateMips(); detail.GenerateMips(); noiseHasMips = true; }
            catch (Exception ex) { Debug.Log("[Natural Weather] the clouds' noise has no smaller copies: " + ex.Message); }
            Debug.Log("[Natural Weather] clouds: noise made (" + shape.width + " and " + detail.width + " cells each way" + (noiseHasMips ? ", with smaller copies" : "") + ")");
        }

        /// <summary>
        /// The second form's billows (2026-10-27): one cube of noise, a byte a place, 256 places each way over half a length of the
        /// large noise (4.5 km), made on the graphics card slice by slice (the shader's go 7, billowMake), with its coarser copies.
        /// Once, when the second form is first drawn.
        /// </summary>
        static void MakeBillow()
        {
            if (billow != null && billow.IsCreated()) return;
            // (drawn by the second form's own copy: go 7 is only in shaders packed from text that has it, and the first form's
            // bundle may be an older one put back by a build)
            var maker = new Material(walk2 ?? walkShader) { name = "Natural Weather: billow maker" };
            maker.SetVector(idSheet, new Vector4(1f, 1f, 0.5f, 1f));
            var commands = new CommandBuffer { name = "Natural Weather: the second form's billows" };
            if (billow != null) billow.Release();
            // (one number a place; where the card cannot draw into such a cube, four)
            RenderTextureFormat one = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            billow = new RenderTexture(256, 256, 0, one, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = 256, useMipMap = true, autoGenerateMips = false,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "Natural Weather: billows"
            };
            billow.Create();
            for (int z = 0; z < 256; z++)
            {
                var block = new MaterialPropertyBlock();
                block.SetVector(idStage, new Vector4(7f, z, 0f, 0f));
                commands.SetRenderTarget(billow, 0, CubemapFace.Unknown, z);
                commands.DrawMesh(sheet, Matrix4x4.identity, maker, 0, 0, block);
            }
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();
            Destroy(maker);
            bool mips = false;
            try { billow.GenerateMips(); mips = true; }
            catch (Exception ex) { Debug.Log("[Natural Weather] the second form's billows have no smaller copies: " + ex.Message); }
            Debug.Log("[Natural Weather] clouds: the second form's billows made (256 cells each way, " + one + (mips ? ", with smaller copies)" : ")"));
        }

        /// <summary>
        /// The second form's pattern of where its clouds stand (tools/clouds/pattern.py, PluginData/cover.pattern): a square of 1024 that
        /// repeats, four bell-spread numbers a dot, with every coarser copy as the file has it (the shader divides each copy's reading by
        /// how much that copy has narrowed the spread, measured from the file's own). Once.
        /// </summary>
        static void LoadPattern()
        {
            patternTried = true;
            pattern = ReadPattern("cover.pattern", out patternWhy);
            if (pattern != null) Debug.Log("[Natural Weather] clouds: the second form's pattern, " + pattern.width + " by " + pattern.width);
            else Debug.Log("[Natural Weather] clouds: the second form has no pattern: " + patternWhy);
        }

        /// <summary>One pattern file of PluginData (see LoadPattern) as the shader's texture, or null and why not.</summary>
        static Texture2D ReadPattern(string file, out string why)
        {
            string path = Folder + file;
            try
            {
                if (!File.Exists(path)) { why = "there is no " + path; return null; }
                byte[] all = File.ReadAllBytes(path);
                const int head = 16;
                if (all.Length < head || all[0] != 'N' || all[1] != 'W' || all[2] != 'P' || all[3] != '1') { why = file + " is not a pattern this version can read"; return null; }
                int n = BitConverter.ToInt32(all, 4), levels = BitConverter.ToInt32(all, 12);
                if (n < 1 || n > 4096 || (n & (n - 1)) != 0) { why = file + " has no sensible size"; return null; }
                var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true)
                {
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 0, name = "Natural Weather: where the second form's clouds stand (" + file + ")"
                };
                long want = 0;
                for (int level = 0, m = n; level < t.mipmapCount; level++, m = Math.Max(1, m / 2)) want += 4L * m * m;
                if (levels != t.mipmapCount || all.Length - head != want) { Destroy(t); why = file + " is not the size it says"; return null; }
                var data = new byte[want];
                Buffer.BlockCopy(all, head, data, 0, (int)want);
                MostOfPattern(data, n, t.mipmapCount);
                t.LoadRawTextureData(data);
                t.Apply(false, true);
                why = "read";
                return t;
            }
            catch (Exception ex) { why = file + " did not load (" + ex.Message + ")"; return null; }
        }

        /// <summary>The pattern the shader is given: the old one for SpecOld's 128 where the development build has it, else the pattern,
        /// else the grey dot (NoPattern).</summary>
        static Texture PatternNow()
        {
#if DEV
            if ((SpecOld & 128) != 0)
            {
                if (!patternOldTried)
                {
                    patternOldTried = true;
                    patternOld = ReadPattern("cover-2026-11-05.pattern", out string why);
                    Debug.Log("[Natural Weather] clouds: the pattern as until 2026-11-06: " + why);
                }
                if (patternOld != null) return patternOld;
            }
#endif
            return pattern != null ? (Texture)pattern : NoPattern();
        }

        /// <summary>
        /// The pattern's b and a, as the shader reads them: for each of its copies but the finest, the most of r and of g within a
        /// dot of each dot (its own and its eight neighbours', the copy's dot itself the most of the four finer ones it covers), for
        /// the walk's strides (clouds.glsl, clearFor: there is no cloud within a stride where even the most the pattern reads within
        /// its reach is too little for the column to stand so high). The finest copy's are its own r and g, not spread: the shader
        /// reads the second copy at the finest (patternMost), and spreading the finest, as at first, was three quarters of the work
        /// for what nothing read (the review of the evening of 2026-10-27). In the file b and a are the same two again, apart, for
        /// the pattern's changing with time, which nothing reads yet.
        /// </summary>
        static void MostOfPattern(byte[] data, int n, int levels)
        {
            int at = 0, m = n;
            byte[] r = null, g = null;
            for (int level = 0; level < levels; level++)
            {
                var r2 = new byte[m * m];
                var g2 = new byte[m * m];
                for (int y = 0; y < m; y++)
                    for (int x = 0; x < m; x++)
                    {
                        int i = y * m + x;
                        if (level == 0) { r2[i] = data[4 * i + 2] = data[4 * i]; g2[i] = data[4 * i + 3] = data[4 * i + 1]; continue; }
                        int f = 2 * m, a = 2 * y * f + 2 * x;
                        r2[i] = Math.Max(Math.Max(r[a], r[a + 1]), Math.Max(r[a + f], r[a + f + 1]));
                        g2[i] = Math.Max(Math.Max(g[a], g[a + 1]), Math.Max(g[a + f], g[a + f + 1]));
                    }
                // (each dot's most within a dot of it, in every copy but the finest: see above)
                for (int y = 0; level > 0 && y < m; y++)
                    for (int x = 0; x < m; x++)
                    {
                        byte rm = 0, gm = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int j = (y + dy + m) % m * m + (x + dx + m) % m;
                                if (r2[j] > rm) rm = r2[j];
                                if (g2[j] > gm) gm = g2[j];
                            }
                        data[at + 4 * (y * m + x) + 2] = rm;
                        data[at + 4 * (y * m + x) + 3] = gm;
                    }
                r = r2; g = g2;
                at += 4 * m * m;
                m = Math.Max(1, m / 2);
            }
        }

        /// <summary>
        /// What the shader reads for the pattern where there is none (cover.pattern missing, or not read): one dot of grey, the
        /// pattern's middle, its b and a as MostOfPattern would make them, the same grey. The walk's strides read a copy of the
        /// pattern no coarser than its coarsest (clouds.glsl, patternMost), here the dot itself, and find there just what coverOf
        /// reads everywhere. (Unity's grey square, bound as first written, may have no coarser copies, and a stride reading a copy
        /// that is not there reads nought on Direct3D, and anything on OpenGL: the bound nothing, and the stride through the even
        /// deck the grey makes (the review of the evening of 2026-10-27).)
        /// </summary>
        static Texture2D NoPattern()
        {
            if (noPattern == null)
            {
                noPattern = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Natural Weather: no pattern" };
                noPattern.SetPixels32(new[] { new Color32(128, 128, 128, 128) });
                noPattern.Apply(false, true);
            }
            return noPattern;
        }

        // ---------------------------------------------------------------- the addon

        void Awake()
        {
            CloudsPage.Page();
            Keystone.Weather.Flashed += OnFlash;
            GameEvents.onVesselChange.Add(OnVesselChange);
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
        }

        void OnDestroy()
        {
            Keystone.Weather.Flashed -= OnFlash;
            GameEvents.onVesselChange.Remove(OnVesselChange);
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            Undim();
            Detach(far); Detach(near);
            Free(far); Free(near);
            ShadowAloneOff();
            if (nearShadow != null) Destroy(nearShadow);
            if (nearShade != null) nearShade.Release();
            if (laying != null) Destroy(laying);
            if (layNothing != null) Destroy(layNothing);
            Shader.SetGlobalVector(idOthers, Vector4.zero);
            if (shadowMap != null) { shadowMap.Release(); Destroy(shadowMap); shadowMap = null; }
            if (shadowMapB != null) { shadowMapB.Release(); Destroy(shadowMapB); shadowMapB = null; }
            if (shadowWalk != null) Destroy(shadowWalk);
            FreeHeaps();
            if (heapsWalk != null) Destroy(heapsWalk);
            if (lightRead != null) { lightRead.Release(); Destroy(lightRead); lightRead = null; }
            if (lightMaking != null) { lightMaking.Release(); Destroy(lightMaking); lightMaking = null; }
            if (lightWalk != null) { Destroy(lightWalk); lightWalk = null; }
            lightReady = false; lightBand = -1; lightCamKnown = false;
            if (mapFine != null) { mapFine.Release(); Destroy(mapFine); mapFine = null; }
            if (mapFineWalk != null) Destroy(mapFineWalk);
            if (shadowRead != null) { shadowRead.Free(); shadowRead = null; }
            if (meterRead != null) { meterRead.Free(); meterRead = null; }
            if (meterPic != null) { meterPic.Release(); Destroy(meterPic); meterPic = null; }
            deckAt = -1e9f;
            SayDeck();
            map.Release();
            CloudsEve.Release();
            Shader.SetGlobalTexture(idFlareCloud, Texture2D.whiteTexture);
            Drawing = false; SunThrough = 1f;
            Report = "";
        }

        void OnFlash(WeatherFlash flash) => CloudFlashes.Add(flash);
        // (a craft switched to: nothing kept from frame to frame is used, wherever the camera went)
        void OnVesselChange(Vessel v) => cutAsked = true;

        static void Detach(Pass p)
        {
            if (p.Cam != null)
            {
                if (p.Draw != null) p.Cam.RemoveCommandBuffer(p.When, p.Draw);
                if (p.After != null) p.Cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, p.After);
                if (p.Shade != null) p.Cam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, p.Shade);
                if (p.Early != null) p.Cam.RemoveCommandBuffer(p.EarlyWhen, p.Early);
                if (p.AskedDepth) p.Cam.depthTextureMode &= ~DepthTextureMode.Depth;
            }
            p.Cam = null; p.AskedDepth = false; p.On = false; p.HavePrev = false;
        }

        /// <summary>
        /// When in a camera's drawing the clouds' own pictures are made (Pass.Early): once its depth picture is made. Under deferred
        /// shading (as the Deferred mod has the game's cameras draw) no depth picture is made of its own, the depth coming with the
        /// G-buffer, and a buffer hung after one is never run: the clouds' pictures were never made, and read as no light through,
        /// the sky and everything far off black (Natural Weather issue #1; seen on the PC with Deferred 1.3.5, 2026-10-24). There
        /// the depth is ready before the lighting, and the picture drawn into after it is the lighting's own.
        /// </summary>
        static CameraEvent EarlyFor(Camera cam) =>
            cam.actualRenderingPath == RenderingPath.DeferredShading ? CameraEvent.BeforeLighting : CameraEvent.AfterDepthTexture;

        static void Free(Pass p)
        {
            foreach (RenderTexture t in new[] { p.Now, p.NowD, p.HistA, p.HistDA, p.HistB, p.HistDB, p.Shown, p.ShownD }) if (t != null) { t.Release(); Destroy(t); }
            p.Now = p.NowD = p.HistA = p.HistDA = p.HistB = p.HistDB = p.Shown = p.ShownD = null;
            foreach (Material m in new[] { p.Walk, p.Together, p.Lay, p.Over, p.Shadow }) if (m != null) Destroy(m);
            p.Walk = p.Together = p.Lay = p.Over = p.Shadow = null;
            if (p.Draw != null) p.Draw.Release();
            if (p.Shade != null) p.Shade.Release();
            if (p.After != null) p.After.Release();
            if (p.Early != null) p.Early.Release();
            p.Draw = p.Shade = p.After = p.Early = null;
        }

        static Camera Find(string name)
        {
            Camera named = Names.CameraNamed(name); if (named != null) return named;
            return null;
        }

        void Off(string said, bool eveBack = true)
        {
            jumpUt = double.NaN;        // (the camera's step forgotten: the first frame back is a jump)
            if (far.On || near.On) { Detach(far); Detach(near); }
            ShadowAloneOff();
            // (EVE's clouds back only where these will not be drawn for a while: switched off, hiding EVE's switched off, no shader,
            // another scene or world, the map view. Through a pause (the map getting ready after a load, no near camera) or with no
            // cloud within sight they stay hidden: brought back, EVE's stock clouds stood in a sky this weather has clear, the
            // research's finding of 2026-10-19.)
            if (eveBack) CloudsEve.Show();
            Shader.SetGlobalTexture(idFlareCloud, Texture2D.whiteTexture);
            Drawing = false; SunThrough = 1f; sunDim = 1f; sunAbove = 1f;
            Shader.SetGlobalVector(idWeatherShadowSun, Vector4.zero);
            Report = said;
#if DEV
            TraceOff(said);
#endif
        }

        // ---------------------------------------------------------------- the sun, dimmed by the cloud above the craft

        // (Dimmed for the scene's two cameras only: from the first of them beginning to the last of them done. The game sets the
        // sun's strength again every frame itself.)
#if DEV
        // (for finding a fault: how far a camera has moved and turned between this component's LateUpdate, where the
        // clouds' lines of sight are set out, and the moment the camera draws)
        static Vector3 lateAt; static Quaternion lateLooks; static int lateFrame = -1; static Camera lateCam;
        static float lagTurn, lagMove; static int lagFrames;
        public static string Lag(string arg)
        {
            string said = "between LateUpdate and drawing, over " + lagFrames + " frames, the far camera turned by up to " + lagTurn.ToString("F3") + " degrees and moved by up to " + lagMove.ToString("F3") + " m";
            lagTurn = lagMove = 0f; lagFrames = 0;
            return said;
        }

        // (For the owner's twitch of 2026-10-19, "the clouds twitch a lot when the chats are loading up commands": the clouds
        // frame by frame. Trace("frames") begins; TraceRead() gives a line a frame: the wall clock, how long the frame was by the
        // real clock and by the game's, and what changed in it: a weather map handed over (how finely made, how long it took),
        // a light field swapped in (how far from the camera it was centred, how fast the camera was reckoned to go), the
        // shader's copy, the near camera's clouds, the storms, the shell as eased, the clouds off. Stall("ms") holds the game's
        // thread so long, once: a long frame made on purpose, as a save, a screenshot or a scene's loading makes one.)
        static readonly System.Text.StringBuilder trace = new System.Text.StringBuilder();
        static int traceLeft, traceMade = -1, traceCells = -1;
        static string traceCopy = "", traceOff = "";
        static bool traceNear, traceSwap;
        static float traceBottom = -1f, traceTop = -1f;
        public static string Trace(string frames)
        {
            int n = 600;
            if (!string.IsNullOrEmpty(frames)) int.TryParse(frames.Trim(), out n);
            trace.Length = 0; traceLeft = Mathf.Clamp(n, 1, 20000);
            traceMade = traceCells = -1; traceCopy = traceOff = ""; traceNear = traceSwap = false; traceBottom = traceTop = -1f;
            return "tracing the clouds for " + traceLeft + " frames";
        }
        public static string TraceRead() { traceLeft = 0; return trace.ToString(); }
        /// <summary>(Development build: the weather's map of the whole world as a PNG, longitude across and latitude up: white for the low
        /// cloud's cover, blue for the middle cloud's, red for rain, the land a dim green under them.)</summary>
        public static string MapPicture(string path)
        {
            if (instance == null || instance.map.A == null) return "no map yet";
            CloudMap m = instance.map;
            Color32[] a = m.A.GetPixels32(), b = m.B.GetPixels32(), c = m.C.GetPixels32();
            var picture = new Texture2D(CloudMap.Wide, CloudMap.High, TextureFormat.RGB24, false);
            var px = new Color32[a.Length];
            World w = World.Of(m.Body);
            for (int j = 0; j < CloudMap.High; j++)
                for (int i = 0; i < CloudMap.Wide; i++)
                {
                    int k = i + j * CloudMap.Wide;
                    double lat = (j + 0.5) / CloudMap.High * 180.0 - 90.0, lon = (i + 0.5) / CloudMap.Wide * 360.0 - 180.0;
                    float land = w != null ? w.Land(lat, lon) : 0f;
                    float low = a[k].r / 255f, mid = b[k].a / 255f, rain = b[k].r / 255f;
                    float cr = 0.05f + 0.1f * land, cg = 0.1f + 0.25f * land, cb = 0.3f - 0.15f * land;
                    cr += (1f - cr) * low; cg += (1f - cg) * low; cb += (1f - cb) * low;
                    cr = Mathf.Lerp(cr, 0.55f, 0.5f * mid * (1f - low)); cg = Mathf.Lerp(cg, 0.7f, 0.5f * mid * (1f - low)); cb = Mathf.Lerp(cb, 1f, 0.5f * mid * (1f - low));
                    cr = Mathf.Lerp(cr, 1f, 0.45f * rain); cg *= 1f - 0.35f * rain; cb *= 1f - 0.35f * rain;
                    px[k] = new Color32((byte)(255 * Mathf.Clamp01(cr)), (byte)(255 * Mathf.Clamp01(cg)), (byte)(255 * Mathf.Clamp01(cb)), 255);
                }
            picture.SetPixels32(px); picture.Apply(false);
            System.IO.File.WriteAllBytes(path, picture.EncodeToPNG());
            Destroy(picture);
            return "the map of " + m.Body.bodyName + " at " + m.MadeFor.ToString("F0") + " s, made one dot in " + m.Stride + ", written to " + path;
        }

        /// <summary>(Development build: the far camera's two kept pictures and this frame's walk, read back at once into PNGs in a folder.)</summary>
        public static string Kept(string folder)
        {
            if (instance == null || instance.far.HistA == null || instance.far.HistB == null || instance.far.Now == null) return "nothing kept";
            // (histA and histB: what is kept, held to the sky with a margin (since 2026-10-26's step 8); shown: it laid on the screen)
            System.IO.Directory.CreateDirectory(folder);
            void Save(RenderTexture rt, string name)
            {
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                t.Apply(false);
                RenderTexture.active = was;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), t.EncodeToPNG());
                Destroy(t);
            }
            Save(instance.far.HistA, "histA"); Save(instance.far.HistB, "histB"); Save(instance.far.Now, "now");
            if (instance.far.Shown != null) Save(instance.far.Shown, "shown");
            return "written to " + folder + "; the next frame writes " + (instance.far.Flip ? "A" : "B");
        }
        /// <summary>(Development build: how finely the weather's map is made: "1" or "2" (one dot in so many each way), "took" (by how long the last one took, as until 2026-10-19), anything else as released; the next map made at once.)</summary>
        public static string MapStride(string how)
        {
            string h = (how ?? "").Trim();
            CloudMap.HeldStride = h == "1" ? 1 : h == "2" ? 2 : h == "took" ? -1 : 0;
            if (instance != null) instance.map.Forget();
            return "the weather's map made " + (CloudMap.HeldStride > 0 ? "at one dot in " + CloudMap.HeldStride : CloudMap.HeldStride < 0 ? "by how long the last took" : "as released") + ", the next at once";
        }
        public static string Stall(string ms)
        {
            int n = 300;
            if (!string.IsNullOrEmpty(ms)) int.TryParse(ms.Trim(), out n);
            n = Mathf.Clamp(n, 1, 5000);
            System.Threading.Thread.Sleep(n);
            return "held the game's thread " + n + " ms";
        }
        static void TraceHead()
        {
            trace.Append(DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
                 .Append(' ').Append((Time.unscaledDeltaTime * 1000f).ToString("F1")).Append(' ').Append((Time.deltaTime * 1000f).ToString("F1"));
        }
        static void TraceOff(string said)
        {
            if (traceLeft <= 0) return;
            traceLeft--;
            TraceHead();
            if (said != traceOff) trace.Append(" | off: ").Append(said.Length > 0 ? said : "(not drawn here)");
            traceOff = said;
            trace.Append('\n');
        }
        static Vector3d traceCamAt; static Vector3 traceAhead;
        static int traceScans = -1;
        static string traceWho = ""; static int traceDrew01, traceDrew00, traceDrewOther, traceFrameWas = -1;
        static readonly System.Text.StringBuilder traceOrder = new System.Text.StringBuilder();     // (every camera that began to draw since the last line, in turn)
        void TraceFrame(float shellBottom, float shellTop, Vector3d camFixed, double R)
        {
            traceLeft--;
            TraceHead();
            // (how far the far camera moved over the planet since the last frame traced, and how far it turned; and which of
            // its two kept pictures is written this frame)
            if (far.Cam != null)
            {
                Vector3 ahead = far.Cam.transform.forward;
                trace.Append(" @ ").Append((camFixed - traceCamAt).magnitude.ToString("F3")).Append(" m ")
                     .Append((traceAhead.sqrMagnitude > 0.5f ? Vector3.Angle(traceAhead, ahead) : 0f).ToString("F3")).Append(" deg ")
                     .Append(far.Flip ? 'B' : 'A').Append(' ').Append(far.Cam.name).Append(far.HavePrev ? "" : " (nothing kept)");
                traceCamAt = camFixed; traceAhead = ahead;
            }
            // (who set this frame's clouds out, and how many times each camera began to draw since the last line; and whether this
            // is the same frame of the game as the last line: set out twice in one frame)
            trace.Append(" by ").Append(traceWho).Append(" drew ").Append(traceDrew01).Append('/').Append(traceDrew00).Append('/').Append(traceDrewOther)
                 .Append(Time.frameCount == traceFrameWas ? " SAME FRAME" : "").Append(" [").Append(traceOrder).Append(']');
            traceDrew01 = traceDrew00 = traceDrewOther = 0; traceFrameWas = Time.frameCount; traceOrder.Length = 0;
            if (traceOff.Length > 0 || trace.Length < 40) { trace.Append(" | drawn"); traceOff = ""; }
            if (map.Made != traceMade) { trace.Append(" | map ").Append(map.Made).Append(" one dot in ").Append(map.Stride).Append(", took ").Append(map.Took.ToString("F2")).Append(" s"); traceMade = map.Made; }
            if (traceSwap) { trace.Append(" | light field swapped in, centred ").Append(((readMid - camFixed.normalized).magnitude * R).ToString("F0")).Append(" m from the camera, which was reckoned at ").Append(lightGoes.magnitude.ToString("F0")).Append(" m/s"); traceSwap = false; }
            string copy = (walkNow != null && (walkNow == walk2 || walkNow == walkStorm2 || walkNow == walkCalm2) ? "form 2 " : "") + (walkNow == walkShader || walkNow == walk2 ? "whole" : walkNow == walkStorm || walkNow == walkStorm2 ? "storm" : "calm");
            if (copy != traceCopy) { trace.Append(" | copy ").Append(copy); traceCopy = copy; }
            if (near.On != traceNear) { trace.Append(" | near ").Append(near.On ? "on" : "off"); traceNear = near.On; }
            if (cells.Count != traceCells) { trace.Append(" | storms ").Append(cells.Count); traceCells = cells.Count; }
            if (CloudsEve.Scans != traceScans) { trace.Append(" | EVE looked for: ").Append(CloudsEve.NewlyHidden).Append(" more hidden, ").Append(CloudsEve.Count).Append(" in all"); traceScans = CloudsEve.Scans; }
            if (Mathf.Abs(easedBottom - traceBottom) > 5f || Mathf.Abs(easedTop - traceTop) > 5f)
            {
                trace.Append(" | shell eased ").Append(easedBottom.ToString("F0")).Append('-').Append(easedTop.ToString("F0")).Append(" m, wanted ").Append(shellBottom.ToString("F0")).Append('-').Append(shellTop.ToString("F0"));
                traceBottom = easedBottom; traceTop = easedTop;
            }
            trace.Append('\n');
        }
#endif

        void BeforeCamera(Camera cam)
        {
#if DEV
            if (cam != null) { if (Names.Is(cam, "Camera 01")) traceDrew01++; else if (Names.Is(cam, "Camera 00")) traceDrew00++; else traceDrewOther++; }
            if (cam != null && traceLeft > 0 && traceOrder.Length < 400) traceOrder.Append(traceOrder.Length > 0 ? ", " : "").Append(cam.name);
            if (due && cam != null && (Names.Is(cam, "Camera 01") || Names.Is(cam, "Camera 00"))) traceWho = cam.name;
#endif
            if (due && cam != null && (Names.Is(cam, "Camera 01") || Names.Is(cam, "Camera 00"))) { due = false; Frame(); }
#if DEV
            if (cam != null && cam == lateCam && Time.frameCount == lateFrame)
            {
                lagTurn = Mathf.Max(lagTurn, Quaternion.Angle(lateLooks, cam.transform.rotation));
                lagMove = Mathf.Max(lagMove, (cam.transform.position - lateAt).magnitude);
                lagFrames++;
            }
#endif
            if (cam == null || !Drawing || sunDim >= 0.999f || (!Names.Is(cam, "Camera 01") && !Names.Is(cam, "Camera 00"))) return;
            if (!DimsFor(cam)) { Undim(); return; }
            Light sun = Sun.Instance != null ? Sun.Instance.sunLight : null;
            if (sun == null) return;
            if (dimmed == null) { dimmed = sun; dimmedFrom = sun.intensity; sun.intensity = dimmedFrom * sunDim; }
        }

        void AfterCamera(Camera cam)
        {
            if (cam != null && Names.Is(cam, "Camera 00")) Undim();
        }

        /// <summary>
        /// Whether the scene's sun is dimmed for a camera by the cloud over the craft, the clouds' shadow then putting each place
        /// right (BeforeCamera, LayShadow). Not under deferred shading (as the Deferred mod draws): there the light on the ground comes
        /// almost all from the sun, and the shadow, which puts each place right as the game's own shaders share the light between
        /// the sun and the sky, could not give it back: under any cloud's shade the ground went dark all over (seen on the PC with
        /// Deferred 1.3.5, 2026-10-24). There the sun is left as it is, and the shadow laid on place by place.
        /// </summary>
        static bool DimsFor(Camera cam) => cam.actualRenderingPath != RenderingPath.DeferredShading;

        void Undim()
        {
            if (dimmed != null && dimmedFrom >= 0f) dimmed.intensity = dimmedFrom;
            dimmed = null; dimmedFrom = -1f;
        }

#if DEV
        static bool skipRead;
        /// <summary>(For the development build: the shadow map's read-back each fourth frame skipped, and back: to measure what it costs the game's thread.)</summary>
        public static string SkipRead() { skipRead = !skipRead; return skipRead ? "the shadow's read-back skipped" : "the shadow read back every fourth frame"; }
        /// <summary>(Development build: what the shadow says lies above the craft, and how it was read.)</summary>
        public static string Above() => instance == null ? "no clouds" : "the sun through the cloud above the craft: " + instance.sunAbove.ToString("F3") + " (" + (instance.shadowRead != null ? instance.shadowRead.Way + ", the last answer " + instance.shadowRead.Frames + " frames late" : "not read") + "); the shell " + instance.easedBottom.ToString("F0") + " to " + instance.easedTop.ToString("F0") + " m as eased";
#endif

        // At the start of each frame: how much cloud the shadow says lies between the craft and the sun. The shadow's one pixel
        // under the craft is read back from the graphics card through Keystone's Readback: without the game waiting where it can
        // be (Unity's own asynchronous read, or on OpenGL Volumetric Explosions' library), the answer some frames late; where it
        // cannot, read at once every fourth frame, as before (that waits for the card to finish all it was given, the clouds'
        // own walk with it: measured on the Mac, 6 to 12 ms of the main thread every fourth frame).
        void Update()
        {
            Undim();
            if (Drawing) Meter();
            if (!Drawing || shadowMap == null || !shadowMap.IsCreated()) return;
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (v == null || body == null || shadowSide <= 0.0) return;
            Vector3d p = v.CoMD - body.position;
            // (Whose light the scene is drawn in: the craft's, or the camera's while it is far from the craft, as Photo Studio's and
            // Cinema Camera's views can be. From 6 km over a rocket standing in a cloud's shade, every sunlit gap of the sea below
            // was drawn dull and without its glitter, in the rocket's shade (2026-10-18). Over 1.5 km off the camera's, back to the
            // craft's within 1 km; the dimming eases over either way.)
            Camera eye = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (eye != null)
            {
                Vector3d c = (Vector3d)eye.transform.position - body.position;
                double apart = (c - p).magnitude;
                if (apart > 1500.0) byCamera = true; else if (apart < 1000.0) byCamera = false;
                if (byCamera) p = c;
            }
            double h = p.magnitude - body.Radius;
            if (shadowRead == null) shadowRead = new Readback(1, 1, true);
            // (an answer that has come back: the shadow's column under where the craft was then, the craft's height now)
            if (shadowRead.Take(out object _))
            {
                Color m = shadowRead.Pixel();
                float lowest = m.g * 1000f, highest = m.b * 1000f;
                float above = m.r * Mathf.Clamp01((highest - (float)h) / Mathf.Max(highest - lowest, 60f));
                sunAbove = float.IsNaN(above) ? 1f : Mathf.Exp(-above);
            }
            if (!shadowRead.Later && (frame & 3) != 0) return;
            Vector3d up = p.normalized;
            double sunUp = Vector3d.Dot(up, (Vector3d)sunNow);
            if (sunUp < -0.03) { sunAbove = 1f; return; }
            Vector3d g = p - (Vector3d)sunNow * ((h - groundHeight) / Math.Max(sunUp, 0.06));
            Vector3d mid = SceneOf(body, shadowMid);
            Vector3d rel = g - mid;
            double sx = Vector3d.Dot(rel, (Vector3d)SceneOfFixed(body, shadowX)) / shadowSide + 0.5, sy = Vector3d.Dot(rel, (Vector3d)SceneOfFixed(body, shadowY)) / shadowSide + 0.5;
            if (sx <= 0.0 || sx >= 1.0 || sy <= 0.0 || sy >= 1.0) { sunAbove = 1f; return; }
#if DEV
            if (skipRead) return;
#endif
            shadowRead.Ask(shadowMap, Mathf.Clamp((int)(sx * Shadow), 0, Shadow - 1), Mathf.Clamp((int)(sy * Shadow), 0, Shadow - 1), 1, 1);
            // (read at once, the answer is here now)
            if (!shadowRead.Later && shadowRead.Take(out object _))
            {
                Color m = shadowRead.Pixel();
                float lowest = m.g * 1000f, highest = m.b * 1000f;
                float above = m.r * Mathf.Clamp01((highest - (float)h) / Mathf.Max(highest - lowest, 60f));
                sunAbove = float.IsNaN(above) ? 1f : Mathf.Exp(-above);
            }
        }
        Readback shadowRead;
        bool byCamera;          // the shade read under the camera, far from the craft (see Update)

        // The clouds' own light over the sky, as they are drawn, for what lights the air under them (Keystone.Weather.CloudLight:
        // Natural Light's veil, whose own light under an overcast was the light from all round as Natural Light works it out,
        // four to seven times the deck drawn over it, so that rain and haze washed the deck out to a flat pale grey and the far
        // hills stood white against it; 2026-10-25). The far camera's clouds as they were taken together, made small (32 by 18)
        // and read back through Keystone's Readback: of the dots whose line of sight is between six and thirty-five degrees
        // above level, how much of what is behind them they hide, and their light by as much as they hide. (The same stretch of
        // sky whichever way the camera looks: taken over all the sky in sight, the haze's light followed the camera, half as
        // bright again looking level as looking up. And not the last few degrees over the horizon, where the far clouds are
        // drawn into the clear air's own blue whatever covers it, half as bright again as the deck.) While none of it is in
        // sight the last answer stands, by as much as the light on the clouds and the weather here have changed since (see
        // Measured). Every eighth frame where the answer comes back later; every two seconds where it is read at once, which
        // waits for the card.
        const int MeterW = 32, MeterH = 18;
        float coverHere;
        RenderTexture meterPic;
        Readback meterRead;
        Vector3 meterRight, meterUp, meterAhead, meterSky;
        int meterMade = -1000;
        float meterAskedAt = -1e9f, meterAnswerAt = -1e9f;
        sealed class MeterView { public Vector3 Right, Up, Ahead, Sky; public int Made; }
        readonly MeterView[] meterViews = { new MeterView(), new MeterView(), new MeterView(), new MeterView(), new MeterView(), new MeterView(), new MeterView(), new MeterView() };
        int meterNext;
#if DEV
        string meterSaid = "not read yet";
#endif

        void Meter()
        {
            if (meterPic == null || !meterPic.IsCreated() || frame - meterMade > 2) return;
            if (meterRead == null) meterRead = new Readback(MeterW, MeterH, true);
            if (meterRead.Take(out object tag) && tag is MeterView seen) Measured(seen);
            if ((frame & 7) != 0 || (!meterRead.Later && Time.unscaledTime - meterAskedAt < 2f)) return;
            // (what the small picture made in the last frame looked at: handed back with the answer, some frames later)
            MeterView v = meterViews[meterNext];
            meterNext = (meterNext + 1) % meterViews.Length;
            v.Right = meterRight; v.Up = meterUp; v.Ahead = meterAhead; v.Sky = meterSky; v.Made = meterMade;
            meterRead.Ask(meterPic, 0, 0, MeterW, MeterH, v);
            meterAskedAt = Time.unscaledTime;
            if (!meterRead.Later && meterRead.Take(out tag) && tag is MeterView now) Measured(now);
        }

        void Measured(MeterView v)
        {
            float r = 0f, g = 0f, b = 0f, hides = 0f;
            int n = 0;
#if DEV
            float lowHides = 0f, highHides = 0f; int lowN = 0, highN = 0;
#endif
            for (int j = 0; j < MeterH; j++)
                for (int i = 0; i < MeterW; i++)
                {
                    // (the first row is the picture's bottom, as the clouds' own shaders read it)
                    Vector3 d = v.Ahead + v.Right * ((i + 0.5f) / MeterW * 2f - 1f) + v.Up * ((j + 0.5f) / MeterH * 2f - 1f);
                    Color c = meterRead.Pixel(j * MeterW + i);
                    if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsNaN(c.a)) continue;
                    float hide = Mathf.Clamp01(1f - c.a);
#if DEV
                    if (j < 3) { lowHides += hide; lowN++; } else if (j >= MeterH - 3) { highHides += hide; highN++; }
#endif
                    float rise = Vector3.Dot(d, v.Sky) / d.magnitude;
                    if (rise < 0.105f || rise > 0.574f) continue;
                    r += Mathf.Max(c.r, 0f); g += Mathf.Max(c.g, 0f); b += Mathf.Max(c.b, 0f); hides += hide; n++;
                }
#if DEV
            meterSaid = n + " dots of low sky; the bottom three rows hide " + (lowN > 0 ? lowHides / lowN : 0f).ToString("F2") + ", the top three " + (highN > 0 ? highHides / highN : 0f).ToString("F2");
#endif
            float dt = Mathf.Clamp(Time.unscaledTime - meterAnswerAt, 0f, 10f);
            meterAnswerAt = Time.unscaledTime;
            bool fresh = LightFresh();
            float ease = fresh ? 1f - Mathf.Exp(-dt / 1.5f) : 1f;
            // (Too little of that sky in sight to say, the camera looking down or straight up: the last light read stands, by as
            // much as the light falling on the clouds has changed since (the sun going down), and its cover by as much as the
            // weather's here has. Held as it was, it was the afternoon's deck all through a night warped through looking down at a
            // rover, a pale grey veil over a dark ground; and eased towards the weather's cover, which counts a thin veil of ice as
            // a whole sky's, the veil's light moved with nothing but the camera's pitch: both found by review, 2026-10-25. Nothing
            // is said where no light has been read, nor where the clouds have four times the light they were read in.)
            if (n < 16)
            {
                if (!fresh || deckLit <= 0f) return;
                float since = litNow / deckLit;
                if (since > 4f) return;
                deckSaid = deckLight * since;
                deckCover = Mathf.Lerp(deckCover, Mathf.Clamp01(coverRead + coverHere - coverHereRead), 1f - Mathf.Exp(-dt / 6f));
                deckAt = Time.unscaledTime;
                SayDeck();
                return;
            }
            float cover = hides / n;
            if (hides > 0.5f)
            {
                Color light = new Color(r / hides, g / hides, b / hides);
                // (the first light read is taken as it is, not eased up from black)
                bool first = deckLight.maxColorComponent <= 0f || deckLit <= 0f;
                deckLight = first ? light : Color.Lerp(deckLight, light, ease);
                deckLit = first ? litNow : Mathf.Lerp(deckLit, litNow, ease);
            }
            deckCover = Mathf.Lerp(deckCover, cover, ease);
            coverRead = deckCover; coverHereRead = coverHere;
            deckSaid = deckLight;
            deckAt = Time.unscaledTime;
            SayDeck();
        }

        static bool LightFresh() => Time.unscaledTime - deckAt < 10f;

        // (what was last read, said through Keystone's Weather.CloudLight, which came after Keystone 0.10.0: kept in a method of its
        // own and guarded, so that with an older Keystone there is nowhere to say it, the first try finds so, and nothing else stops)
        static Color deckLight, deckSaid;
        static float deckCover, deckAt = -1e9f;
        // (and for while none of that sky is in sight: the light falling on the clouds when the deck's was read, and now; the
        // cover read, and the weather's here then)
        static float deckLit, litNow, coverRead, coverHereRead;
        static bool deckUnsaid;

        static void SayDeck()
        {
            if (deckUnsaid) return;
            try { SayDeckToKeystone(); }
            catch (Exception) { deckUnsaid = true; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void SayDeckToKeystone()
        {
            Keystone.Weather.CloudLight = deckSaid;
            Keystone.Weather.CloudLightCover = deckCover;
            Keystone.Weather.CloudLightAt = deckAt;
        }

        /// <summary>
        /// The planet's own axes as they stand in the scene now: towards latitude 0 longitude 0, towards longitude 90, the north
        /// pole. (GetSurfaceNVector, which turns the planet's frame into the scene's. GetRelSurfaceNVector does not: it answers in
        /// the planet's own frame, and the scene stands turned about the pole against that by CelestialBody.directRotAngle: a
        /// different angle after every load, and one that runs on with the planet's turning above the height where the game
        /// stops turning the scene with the planet. Until 0.2.2 these were taken from it, and were the scene's own x, z and y.)
        /// </summary>
        static void Axes(CelestialBody body, out Vector3d fx, out Vector3d fy, out Vector3d fz)
        {
            fx = body.GetSurfaceNVector(0.0, 0.0).normalized; fy = body.GetSurfaceNVector(0.0, 90.0).normalized; fz = body.GetSurfaceNVector(90.0, 0.0).normalized;
        }

        /// <summary>A place on the planet, from its centre, in the scene's axes.</summary>
        static Vector3d SceneAt(CelestialBody body, double lat, double lon, double height) => body.GetWorldSurfacePosition(lat, lon, height) - body.position;

        static Vector3d SceneOf(CelestialBody body, Vector3d f)
        {
            Axes(body, out Vector3d fx, out Vector3d fy, out Vector3d fz);
            return fx * f.x + fy * f.y + fz * f.z;
        }

        static Vector3 SceneOfFixed(CelestialBody body, Vector3 f) => (Vector3)SceneOf(body, new Vector3d(f.x, f.y, f.z));

        // ---------------------------------------------------------------- each frame

        // The clouds are set out by where the camera stands and looks WHEN IT DRAWS. The game moves its flight camera in a
        // LateUpdate of its own that comes after this one, so set out from here they were set out for the camera of the
        // frame before: with the camera swung round, or riding a rocket, the whole sky came a frame behind the ground
        // (the owner, 2026-10-13: "the clouds aren't anchored to the earth, so they have a slight movement delay";
        // measured: the camera turned by up to 80 degrees between this LateUpdate and its drawing). So the frame's work
        // is done from BeforeCamera, when the first of the flight's cameras is about to draw; from here only when there
        // is no such camera to wait for, and when the last frame's was never taken up.
        bool due;

        void LateUpdate()
        {
            bool waits = HighLogic.LoadedSceneIsFlight && Weather.FlightReady && !MapView.MapIsEnabled;
#if DEV
            if (due || !waits) traceWho = "LateUpdate";
#endif
            if (due || !waits) Frame();
            due = waits;
        }

        // (Development: where Frame's time goes, part by part: Clouds.Parts. Begin and Mark are left out of a release build.)
        [System.Diagnostics.Conditional("DEV")] static void Begin()
        {
#if DEV
            partsWatch.Reset(); partsWatch.Start(); partsLast = 0.0; partsFrames++;
#endif
        }
        [System.Diagnostics.Conditional("DEV")] static void Mark(string name)
        {
#if DEV
            double now = partsWatch.Elapsed.TotalMilliseconds, d = now - partsLast; partsLast = now;
            if (!partsSum.ContainsKey(name)) { partsSum[name] = 0.0; partsWorst[name] = 0.0; partsOrder.Add(name); }
            partsSum[name] += d; if (d > partsWorst[name]) partsWorst[name] = d;
#endif
        }
#if DEV
        static readonly System.Diagnostics.Stopwatch partsWatch = new System.Diagnostics.Stopwatch();
        static readonly Dictionary<string, double> partsSum = new Dictionary<string, double>(), partsWorst = new Dictionary<string, double>();
        static readonly List<string> partsOrder = new List<string>();
        static int partsFrames; static double partsLast;
        public static string Parts()
        {
            var b = new System.Text.StringBuilder("Frame over " + partsFrames + " frames, ms (mean, worst):");
            foreach (string n in partsOrder) b.Append(" | ").Append(n).Append(' ').Append((partsSum[n] / Math.Max(1, partsFrames)).ToString("F3")).Append(", ").Append(partsWorst[n].ToString("F2"));
            partsFrames = 0; partsSum.Clear(); partsWorst.Clear(); partsOrder.Clear();
            return b.ToString();
        }
#endif

        void Frame()
        {
            frame++;
            Begin();
#if DEV
            // (EVE's kept hidden too: held off for a test, no clouds at all; brought back, EVE's stock layer stood over the sea in a test of
            // the slicks from afar, 2026-10-20. And hidden afresh, as below: after a quickload EVE makes its clouds and their shadow on
            // the ground anew, and nothing hid them while these were held off. Its stock layer and its shadows were likely what lay
            // over the land in the far ground's high views, 2026-10-20 14:00, the dark blotches of the owner's crops.)
            if (heldOff)
            {
                CelestialBody held = HighLogic.LoadedSceneIsFlight && Weather.FlightReady ? FlightGlobals.currentMainBody : null;
                if (held != null && CloudsPage.HideEve != null && CloudsPage.HideEve) CloudsEve.Hide(held);
                Off("Clouds held off (development build).", false);
                return;
            }
#endif
            if (!CloudsPage.Wanted || !HighLogic.LoadedSceneIsFlight || !Weather.FlightReady || MapView.MapIsEnabled) { Off(""); return; }
            CelestialBody body = FlightGlobals.currentMainBody;
            if (!CloudSource.Worth(body)) { Off(CloudSource.WhyNot(body)); return; }
            if (!tried) LoadShaders();
            if (!fieldTried) LoadField();
            if (blueTable == null) LoadBlue();
            if (walkShader == null) { Off("No clouds: " + why + "."); return; }
            MakeNoise();
            if (Two) { MakeBillow(); if (!patternTried) LoadPattern(); }
            CloudAir air = CloudAir.Of(body);
            Camera farCam = Find("Camera 01"), nearCam = Find("Camera 00");
            if (nearCam == null) { Off("", false); return; }
            // (EVE's clouds hidden as soon as these are the world's, before the map is ready and whether or not a cloud is in sight:
            // hidden only once a cloud was drawn, EVE's stock layer showed in clear weather from a load until the first cloud came)
            if (CloudsPage.HideEve != null && CloudsPage.HideEve) CloudsEve.Hide(body); else CloudsEve.Show();
            Mark("EVE");
#if DEV
            lateCam = farCam != null ? farCam : nearCam; lateAt = lateCam.transform.position; lateLooks = lateCam.transform.rotation; lateFrame = Time.frameCount;
#endif
            bool split = farCam != null;            // (OpenGL: two cameras; Direct3D: Camera 00 alone, as far as the scene goes)
            if (!split) farCam = nearCam;
            Split = split;

            Mark("start");
            // ---- where everything is
            double R = body.Radius;
            Vector3d camRel = (Vector3d)farCam.transform.position - body.position;
            double alt = camRel.magnitude - R;
            body.GetLatLonAlt((Vector3d)farCam.transform.position, out double camLat, out double camLon, out double _);
            double ut = Planetarium.GetUniversalTime();
            double reach = Math.Max(6.0, Math.Acos(Math.Min(1.0, R / (R + Math.Max(0.0, alt)))) * 180.0 / Math.PI + 5.0);
            if (!map.Update(body, ut, camLat, camLon, reach, false) || !air.Ready) { Off("Clouds: getting ready.", false); return; }
            Mark("map");

            // The storms near the camera (drawn as towers where the weather has them), asked again every second, each for now and
            // for one step on (a second of the game's clock, more when time is warped): what is drawn is each storm between the
            // two, moved on and grown frame by frame. (Until 2026-10-18 they were drawn as asked, held for the second, and every
            // tower jumped once a second by its drift and its growth: one of the twitches the owner saw, measured in a storm with
            // the clouds' noise held still as 2 to 5 % of the picture changed at once, every second.)
            // (The storms within sight, found once a second; with the weather's own model, on a thread of their own, the nearest
            // six sorted out here when it is done: found on the game's thread, as until 2026-10-18, it was a 3.9 ms hitch every
            // second. Under another weather (the stand-in) still here.)
            if (cellsWork != null && cellsWork.IsCompleted)
            {
                if (!cellsWork.IsFaulted && cellsWorkBody == body.bodyName) Found(body, camRel, cellsFound, cellsFoundNext, cellsWorkUt0, cellsWorkUt1);
                cellsWork = null;
            }
            if (cellsBody != body.bodyName) { cells.Clear(); nextById.Clear(); heldSize.Clear(); cellsBody = body.bodyName; cellsAt = -100f; }
            if (cellsWork == null && (Time.realtimeSinceStartup - cellsAt > 1f || ut > cellsUt1))
            {
                cellsAt = Time.realtimeSinceStartup;
                double radius = Math.Min(400000.0, reach * R * Math.PI / 180.0), lat = camLat, lon = camLon, t0 = ut, t1 = ut + Math.Max(1.0, TimeWarp.CurrentRate);
                if (CloudSource.Keystones)
                {
                    CelestialBody b = body; List<WeatherCell> found = cellsFound, next = cellsFoundNext;
                    cellsWorkBody = body.bodyName; cellsWorkUt0 = t0; cellsWorkUt1 = t1;
                    cellsWork = System.Threading.Tasks.Task.Run(() => { found.Clear(); next.Clear(); CloudSource.Cells(b, lat, lon, radius, t0, found); CloudSource.Cells(b, lat, lon, radius, t1, next); });
                }
                else
                {
                    cellsFound.Clear(); cellsFoundNext.Clear();
                    CloudSource.Cells(body, lat, lon, radius, t0, cellsFound); CloudSource.Cells(body, lat, lon, radius, t1, cellsFoundNext);
                    Found(body, camRel, cellsFound, cellsFoundNext, t0, t1);
                }
            }
            Mark("storm cells");
            // (fog within sight, drawn by the clouds only from well over its top: under that, Natural Light's veil draws it, and
            // the shell walked through is not taken down to the ground for it; see CloudSource.Kind and the shader's formAt,
            // which leaves out each place's fog by its own top. Over the lowest fog in sight, not the highest: hill fog in
            // sight, its top on high ground, kept sea fog from being drawn under a camera three kilometres up.)
            bool fogFromOver = map.FogTop > 0f && alt > map.FogTopLowest + 1500.0;
            if (!map.AnyCloud && !fogFromOver && cells.Count == 0) { Off("Clouds: none within sight.", false); return; }

            // The planet's own axes, in the scene's: towards latitude 0 longitude 0, towards longitude 90, the north pole.
            Axes(body, out Vector3d fx, out Vector3d fy, out Vector3d fz);
            Vector3d camFixed = new Vector3d(Vector3d.Dot(camRel, fx), Vector3d.Dot(camRel, fy), Vector3d.Dot(camRel, fz));
            Vector3 ToFixed(Vector3 v) => new Vector3((float)Vector3d.Dot(v, fx), (float)Vector3d.Dot(v, fy), (float)Vector3d.Dot(v, fz));

            // ---- a jump: the camera further from where it was than its own motion explains. Foretold by the last step it took, for
            // the game's time since, so that flying, an orbit, a change of warp and a pause are all explained; the test putting the
            // camera down somewhere, a craft switched to, are not. Nothing kept from frame to frame is used after one (Setup).
            // (Until 2026-10-26 only a move of 5 km or more was a cut: the test's camera put down 4 km off took the last picture over,
            // and for two seconds the clouds filled in from it, white ticks along their edges, holes through heaps, shadows with no
            // heap over them (research/clouds-r5/sidebyside-2026-10-25.md, A1).)
            // (The step is learned again on every frame the game's clock moves, but not from the first frame of a jump: the frame
            // after one is foretold by how the camera moved before it, so a second jump straight after is caught too; where the
            // camera's motion itself changed at the jump (a craft switched to that flies otherwise), that frame is a jump as well,
            // and its step is learned. By the game's time, not the frame's: on rails a change of warp changes the step a
            // hundredfold with frames as long as before, and a step scaled by the frame's length was a jump on every frame after.)
            {
                Vector3d stepNow = camFixed - jumpWas;
                double dut = ut - jumpUt;
                bool jump = double.IsNaN(jumpUt) || jumpBody != body.bodyName || cutAsked || dut < 0.0;
                if (!jump)
                {
                    Vector3d expected = jumpDut > 1e-6 ? jumpStep * (dut / jumpDut) : Vector3d.zero;
                    // (150 m, a tenth of the step foretold, and a fiftieth of the height over the clouds' tops: from high over them a
                    // miss of the same metres moves their picture less)
                    double allowed = 150.0 + 0.1 * expected.magnitude + 0.02 * Math.Max(0.0, alt - shellTopNow);
                    jump = (stepNow - expected).magnitude > allowed;
                }
                if (dut > 1e-6 && (!jump || jumpedLast)) { jumpStep = stepNow; jumpDut = dut; }
                jumped = jump; jumpedLast = jump;
#if DEV
                if (jump) { JumpsSeen++; JumpLast = ut; }
#endif
                jumpWas = camFixed; jumpUt = ut; jumpBody = body.bodyName; cutAsked = false;
            }

            // The wind carries the clouds (as the weather says the low clouds move here): how far, kept up from frame to frame.
            WeatherHere hereNow = CloudSource.At(body, camLat, camLon, ut);
            // (how much of the sky the weather here covers, the layers one over another: for Meter, while no sky is in sight)
            coverHere = 1f - (1f - LayerCover(hereNow.Low)) * (1f - LayerCover(hereNow.Middle)) * (1f - LayerCover(hereNow.High));
            Mark("weather here");
            Vector3d up = camRel.normalized;
            Vector3d east = Vector3d.Cross(up, fz).normalized;          // (Unity's axes are left-handed: up x pole is east)
            Vector3d north = Vector3d.Cross(east, up).normalized;
            Vector3d windScene = east * hereNow.Drift.x + north * hereNow.Drift.y;
            Vector2d heapsWind = new Vector2d(hereNow.Drift.x, hereNow.Drift.y);
            Vector3d windFixed = new Vector3d(Vector3d.Dot(windScene, fx), Vector3d.Dot(windScene, fy), Vector3d.Dot(windScene, fz));
            // (and how far the wind carried the clouds since the last frame: where each pixel's cloud was in last frame's picture is
            // that much further upwind (Setup))
            windNow = windFixed; windUt = ut;
            if (double.IsNaN(driftAt) || driftBody != body.bodyName || Math.Abs(ut - driftAt) > 3600.0)
            {
                drift = windFixed * (ut % 100000.0);
                fieldDrift = new Vector2d(hereNow.Drift.x, hereNow.Drift.y) * (ut % 100000.0);
                driftBody = body.bodyName;
            }
            else { drift += windFixed * (ut - driftAt); fieldDrift += new Vector2d(hereNow.Drift.x, hereNow.Drift.y) * (ut - driftAt); }
            fieldDrift = new Vector2d(Wrap(fieldDrift.x, fieldSide), Wrap(fieldDrift.y, fieldSide));
            driftAt = ut;
            drift = new Vector3d(Wrap(drift.x, NoiseWrap), Wrap(drift.y, NoiseWrap), Wrap(drift.z, NoiseWrap));
#if DEV
            if (heldStill) { drift = Vector3d.zero; fieldDrift = Vector2d.zero; windNow = Vector3d.zero; }
            driftWas = drift; utWas = ut; windWas = windFixed;
            lonGame = camLon; lonOwn = Math.Atan2(camFixed.y, camFixed.x) * 180.0 / Math.PI; turnWas = body.directRotAngle;
#endif
            Vector3d noiseAt = camFixed - drift;
            // (the craft's wake in cloud: where it has been, kept in the cloud's own frame)
            Mark("drift");
            CloudsWake.Follow(body, ut, drift, fx, fy, fz, map.LowestBase, map.HighestTop, NoiseWrap);
            Mark("wake");
            var noise = new Vector4((float)(noiseAt.x / NoiseLength), (float)(noiseAt.y / NoiseLength), (float)(noiseAt.z / NoiseLength), (float)NoiseLength);
            Vector3 windDir = windFixed.magnitude > 0.01 ? (Vector3)(windFixed.normalized) : Vector3.zero;
            // (the second form's lean of its heaps and storms: the low wind eased over some seconds, as long as its speed over 2 m/s
            // and no longer than 1. By windDir, a unit way, it flipped in one frame where the model's drift crossed nought (at
            // 29.525 degrees, between its column's rows), and every tower in sight moved its top two kilometres; and a drift of a
            // hundredth of a metre a second leant them as far as a gale (the second review of 2026-10-27). Carried only between
            // frames that follow on, as everything kept from frame to frame.)
            Vector2 leanNow = new Vector2((float)hereNow.Drift.x, (float)hereNow.Drift.y);
            leanEased = leanSet && !jumped ? Vector2.Lerp(leanEased, leanNow, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 5f)) : leanNow;
            leanSet = true;
            Vector2 lean2 = leanEased * Mathf.Min(0.5f, 1f / Mathf.Max(leanEased.magnitude, 1e-4f));
            Vector3 leanFixed = ToFixed((Vector3)(east * lean2.x + north * lean2.y));
            // Cloud streets: heaps in rows along a steady wind of some strength (the churning air's rolls lie along it), not in
            // light air; the rows' way kept until the wind has turned more than 25 degrees.
            Vector3 upFixed = (Vector3)camFixed.normalized;
            Vector3 alongNow = windDir.sqrMagnitude > 0.5f ? Vector3.ProjectOnPlane(windDir, upFixed).normalized : Vector3.zero;
            if (alongNow.sqrMagnitude > 0.5f && (streetsBody != body.bodyName || streetsAlong.sqrMagnitude < 0.5f || Vector3.Angle(alongNow, streetsAlong) > 25f && Vector3.Angle(-alongNow, streetsAlong) > 25f))
            {
                streetsAlong = alongNow;
                streetsAcross = Vector3.Cross(upFixed, alongNow).normalized;
                streetsBody = body.bodyName;
            }
            float windSpeed = (float)windFixed.magnitude;
            float streetsStrength = streetsAlong.sqrMagnitude > 0.5f ? Mathf.Clamp01((windSpeed - 4f) / 8f) * 0.7f : 0f;
            float boil = (float)Wrap(ut * 0.0012, 1.0);
            // (the billows rising through the clouds: see RiseSpeed. Along the up here, summed frame by frame so that going round the
            // world does not swing what has risen already; each way wrapped at a length of the noise, a whole number of the billows'
            // own repeats, so that the wrap is not seen.)
            double riseStep = double.IsNaN(riseAt) || jumped ? 0.0 : Math.Max(0.0, Math.Min(ut - riseAt, 2.0 * Time.unscaledDeltaTime));
            riseAt = ut;
#if DEV
            if (heldStill) riseStep = 0.0;
#endif
            rise += (Vector3d)upFixed * (riseStep * RiseSpeed);
            rise = new Vector3d(Wrap(rise.x, NoiseLength), Wrap(rise.y, NoiseLength), Wrap(rise.z, NoiseLength));
            specNow = new Vector4((float)(rise.x / NoiseLength), (float)(rise.y / NoiseLength), (float)(rise.z / NoiseLength), SpecOld);

            Mark("where");
            // ---- the sun and the light round about
            CelestialBody sunBody = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            Vector3 toSun = sunBody != null ? (Vector3)(sunBody.position - (Vector3d)farCam.transform.position).normalized : Vector3.up;
            sunNow = toSun;
            float sunUp = Vector3.Dot((Vector3)up, toSun);
            // (the brightest other world in the sky, by how much of it is lit and how large it stands: the Mun over Kerbin.
            // By night a veil of ice is lit by it and has its ring round it (the shader's high cloud); 1 for a full Mun.)
            Vector3 toMoon = Vector3.up;
            float moonLit = 0f;
            if (sunBody != null && sunUp < 0.08f)
                foreach (CelestialBody other in FlightGlobals.Bodies)
                {
                    if (other == null || other == body || other == sunBody || other.isStar) continue;
                    Vector3d rel = other.position - (Vector3d)farCam.transform.position;
                    double d = rel.magnitude;
                    if (d < other.Radius * 2.0) continue;
                    float phase = 0.5f * (1f - (float)Vector3d.Dot(rel / d, (sunBody.position - other.position).normalized));
                    float lit = phase * (float)(other.Radius * other.Radius / (d * d)) / 2.8e-4f;
                    if (lit > moonLit) { moonLit = lit; toMoon = (Vector3)(rel / d); }
                }
            // (by night, and with the moon over the horizon: from under it, it lights nothing the eye sees)
            float moonHigh = moonLit;
            moonLit = Mathf.Min(moonLit, 1.5f) * Mathf.Clamp01((0.08f - sunUp) / 0.1f) * Mathf.Clamp01((Vector3.Dot(toMoon, (Vector3)up) + 0.02f) / 0.06f);
#if DEV
            {
                Vector3 mNorth = Vector3.ProjectOnPlane((Vector3)(body.position + (Vector3d)body.transform.up * body.Radius - (Vector3d)farCam.transform.position), (Vector3)up).normalized;
                Vector3 mEast = Vector3.Cross((Vector3)up, mNorth);
                MoonSaid = "the brightest moon: " + (moonHigh > 0f ? Mathf.RoundToInt(Mathf.Atan2(Vector3.Dot(toMoon, mEast), Vector3.Dot(toMoon, mNorth)) * Mathf.Rad2Deg) + " deg round from north, " + Mathf.RoundToInt(Mathf.Asin(Mathf.Clamp(Vector3.Dot(toMoon, (Vector3)up), -1f, 1f)) * Mathf.Rad2Deg) + " up, its light " + moonLit.ToString("F2") + " of a full Mun's (" + moonHigh.ToString("F2") + " if it were up and the sun down)" : "none (by day, or none in the sky)") + "; the sun " + Mathf.RoundToInt(Mathf.Atan2(Vector3.Dot(toSun, mEast), Vector3.Dot(toSun, mNorth)) * Mathf.Rad2Deg) + " deg round from north, " + Mathf.RoundToInt(Mathf.Asin(Mathf.Clamp(sunUp, -1f, 1f)) * Mathf.Rad2Deg) + " deg up";
            }
#endif
            float brightness = CloudsPage.Brightness != null ? (float)CloudsPage.Brightness : 1f;
            // (the light at the clouds' height, a kilometre or two up, from the table: the sky lights them as the sun lights the air)
            Color sunAtClouds = air.Through(1500f, Mathf.Max(sunUp, -0.2f));
            // (under a deck of cloud the light from above is the cloud's grey, not the sky's blue)
            float overhead = Mathf.Max(CloudSource.Kind(hereNow.Low) >= 0f && hereNow.Low.Kind != CloudKind.Fog ? hereNow.Low.Cover : 0f, CloudSource.Kind(hereNow.Middle) >= 0f ? hereNow.Middle.Cover : 0f);
            float skyLum = 0.2126f * air.Sky.r + 0.7152f * air.Sky.g + 0.0722f * air.Sky.b;
            Color skyGrey = Color.Lerp(air.Sky, new Color(skyLum, skyLum, skyLum), overhead * overhead) * (0.32f * brightness);
            // (The clouds' own sky light: not greyed in the second form, whose clouds' own depth darkens what the sky lights inside them;
            // greyed by the camera's own sky, every cloud in sight went grey as the sky overhead clouded over. The ground's shade (below)
            // is still the greyed sky's in both: under a deck the light from above is grey there, as it is.)
            // (And the second form's of the air's own colour (SkyOwn), not of the first's, which is a quarter of the way to white: the
            // shader greys it three tenths as Blackrack's does his sky's light (his skylightTintMultiplier 0.7), and greyed twice the
            // shaded sides of heaps were half as blue as his (B less R 19 against his 39 at sunset, 23 against 90 in the field).)
            Color skyLight = Two ? air.SkyOwn * (0.32f * brightness) : skyGrey;
            // (the shade's colour, for the shadow's pass (cloudshade.glsl): what a shadow leaves is the sky's light, which is bluer than
            // the sun's; the sun's light over the sky's in each colour, against green's: about 1.36, 1, 0.57 for Kerbin's clear sky
            // with the sun high, more so with it low, nearly even under a grey sky)
            {
                float overGreen = sunAtClouds.g / Mathf.Max(skyGrey.g, 1e-5f);
                shadeTint = overGreen > 1e-4f
                    ? new Vector4(Mathf.Clamp(sunAtClouds.r / Mathf.Max(skyGrey.r, 1e-5f) / overGreen, 0.25f, 4f), 1f, Mathf.Clamp(sunAtClouds.b / Mathf.Max(skyGrey.b, 1e-5f) / overGreen, 0.25f, 4f), 1f)
                    : new Vector4(1f, 1f, 1f, 1f);
            }
            float groundLit = Mathf.Max(0f, sunUp);
            // (and by the weather the ground lies under: a sunlit ground's light, as until 2026-10-25, lit the inside and the base of a
            // thunderstorm from below as a fair day's ground does, the ground's warm colour the most of their light there, where the
            // ground under the storm has a twentieth of a clear day's light: the inside of a storm was a bright beige)
            float groundShare = GroundShare(hereNow, sunUp);
#if DEV
            if (groundAsBefore) groundShare = 1f;
            GroundShareSaid = groundShare;
#endif
            Color groundLight = new Color(0.30f, 0.30f, 0.26f) * (0.4f * groundLit * groundShare * brightness) * sunAtClouds;
            float night = 0.0035f * brightness;
            // (how much light falls on the clouds' tops, the sun's on them as they face it and the sky's, for the meter's light
            // while it cannot be read: see Measured)
            litNow = (0.2126f * sunAtClouds.r + 0.7152f * sunAtClouds.g + 0.0722f * sunAtClouds.b) * brightness * Mathf.Max(sunUp, 0f)
                   + 0.2126f * skyGrey.r + 0.7152f * skyGrey.g + 0.0722f * skyGrey.b + night;

            Mark("light");
            // ---- how much cloud, how thick, how fine
            float lowestBase = map.AnyCloud ? map.LowestBase : 99999f, highestTop = map.HighestTop;
            if (fogFromOver) { lowestBase = Mathf.Min(lowestBase, map.FogBase); highestTop = Mathf.Max(highestTop, map.FogTop); }
            if (lowestBase > 99000f) lowestBase = map.LowestBase;
            float shellBottom = Mathf.Max(0f, lowestBase - 60f), shellTop = Mathf.Max(highestTop, shellBottom + 300f) + 150f;
            // (the second form draws a layer of heaps over a column a third deeper than the weather's layer, and never under 2700 m:
            // its heaps' tops stand under their column's (clouds.glsl, columnOf))
            // (and a heap's tower among them over up to 2.1 times that (clouds.glsl, towerOf), unless set back with SpecOld's 4)
            if (Two && map.AnyCloud)
            {
                float heapColumn = Mathf.Max(1.3f * Mathf.Max(highestTop - lowestBase, 0f), 2700f) * ((SpecOld & 4) == 0 ? 2.1f : 1f);
                shellTop = Mathf.Max(shellTop, Mathf.Max(highestTop, lowestBase + heapColumn) + 150f);
            }
            // (for the second form's storms (cellTwo): the wind aloft at the camera, which carries every anvil in sight, and how
            // unstable the air is there, which throws the strongest updraughts' tops up through the top. The model's own, from the
            // place and the clock, whoever's weather the clouds are drawing; the same for the six storms near the camera. The wind
            // without the storms' own outflow near the ground (Column.AE, AN: with it, every anvil in sight swung round with the
            // camera's side of the nearest storm's gust front), and eased over some seconds, so that the steps the model's column
            // is kept in (0.05 degrees, 2 s) do not jump the anvils, nor every dome in sight where the column steps over a coast.
            // Carried only between frames that follow on with storms in sight: after a jump, a frame with none or with the clouds
            // off, the anvils took the old place's wind and swung round for 5 to 15 s (the second review of 2026-10-27). Eased by
            // the real time or the game's, whichever is longer: on rails the model's wind moves by the game's.)
            float unstable = 0.5f;
            if (Two && cells.Count > 0)
            {
                Column aloftAt = Model.ColumnAt(World.Of(body), camLat, camLon, ut);
                var aloftNow = new Vector2(aloftAt.AE, aloftAt.AN);
                bool carry = aloftEasedSet && !jumped;
                float ease = 1f - Mathf.Exp(-Mathf.Max(Time.unscaledDeltaTime, carry ? (float)(ut - aloftUt) : 0f) / 5f);
                aloftEased = carry ? Vector2.Lerp(aloftEased, aloftNow, ease) : aloftNow;
                unstableEased = carry ? Mathf.Lerp(unstableEased, aloftAt.B.Inst, ease) : aloftAt.B.Inst;
                aloftEasedSet = true; aloftUt = ut;
                unstable = unstableEased;
            }
            else aloftEasedSet = false;
            // (the dome over a storm's strongest updraught, from 0.3 to 0.6 of its life, 300 m to 1.5 km by how unstable the air is)
            float OvershootOf(WeatherCell c) => (300f + 1200f * unstable) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.4f, c.Life)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.6f, c.Life))) * Mathf.Clamp01(c.Strength * 2f);
            // (each storm as it is drawn this frame, once, for the shell, the nearest cloud and the shader alike: the shell and the
            // nearest cloud were worked out from the storms as asked, up to a second of the game's clock (a thousand under warp)
            // behind what the shader draws, and the dome stood through the shell's top, cut flat (the second review). In the
            // second form, with the most its reach and its top have been: both come from the weather where it stands now, which
            // follows the hour, so through its life its anvil narrowed, thinned and sank 7 km, and dropped 2 km in a second where
            // the model's storm stopped being a thunderstorm; its strength still fades it out.)
            int cellsCount = Math.Min(cells.Count, cellsNow.Length);
            for (int i = 0; i < cellsCount; i++)
            {
                WeatherCell c = Between(cells[i], ut);
                if (Two)
                {
                    Vector2 most = new Vector2(c.Radius, c.Top);
                    if (heldSize.TryGetValue(c.Id, out Vector2 held)) most = Vector2.Max(most, held);
                    heldSize[c.Id] = most;
                    c.Radius = most.x; c.Top = most.y;
                }
                cellsNow[i] = c;
            }
            for (int i = 0; i < cellsCount; i++)
            {
                WeatherCell c = cellsNow[i];
                shellBottom = Mathf.Min(shellBottom, Mathf.Max(0f, c.Base - 60f));
                // (the second form's storms stand over their top: their turrets by up to a twentieth of their depth, the dome by its
                // own height; the walk takes no step over the shell's top, and with 300 m both were cut flat)
                shellTop = Mathf.Max(shellTop, c.Top + (Two ? Mathf.Max(300f, Mathf.Max(0.05f * (c.Top - c.Base) + 100f, OvershootOf(c) + 150f)) : 300f));
            }

            Mark("how much (map)");
            // ---- a cyclone within sight, as the shader takes it (its wall, canopy and bands are drawn as shapes of their
            // own: clouds.glsl, "a cyclone"): the nearest, where the weather is Natural Weather's own
            var eyeA = Vector4.zero; var eyeB = Vector4.zero; var eyeC = new Vector4(1f, 0f, 0f, 0f); var eyeD = Vector4.zero;
            Cyclone eyeOf = null;
            if (CloudSource.Keystones && Keystone.Weather.Provider is Provider)
            {
                World world = World.Of(body);
                double nearest = double.MaxValue;
                foreach (Cyclone c in Violent.Cyclones(world, ut))
                {
                    double d = Model.Distance(world, camLat, camLon, c.Lat, c.Lon);
                    if (d < nearest) { nearest = d; eyeOf = c; }
                }
                if (eyeOf != null && nearest > reach * R * Math.PI / 180.0 + 14.0 * eyeOf.Rmw) eyeOf = null;
                if (eyeOf != null)
                {
                    double ground = 0.0;
                    try { ground = Math.Max(0.0, body.TerrainAltitude(eyeOf.Lat, eyeOf.Lon, true)); } catch (Exception) { }
                    Vector3d mid = SceneAt(body, eyeOf.Lat, eyeOf.Lon, 0.0);
                    Vector3d eastThere = Vector3d.Cross(mid.normalized, fz).normalized;
                    float baseAt = (float)ground + 350f;
                    // (as tall as the weather's own storm cloud in it: Model.Build)
                    float topAt = (float)ground + 2500f + (world.Climate.Tops * 0.93f - 2500f) * (0.5f + 0.5f * eyeOf.Now);
                    eyeA = new Vector4((float)mid.x, (float)mid.y, (float)mid.z, eyeOf.Rmw);
                    eyeB = new Vector4(eyeOf.Now, eyeOf.Spin, (float)((eyeOf.Id * 0.7) % (2.0 * Math.PI)), topAt);
                    eyeC = new Vector4((float)eastThere.x, (float)eastThere.y, (float)eastThere.z, baseAt);
                    // (the wall's cloud goes round the eye at half the speed of its wind)
                    eyeD = new Vector4((float)((ut * 0.5 * eyeOf.Speed / Math.Max(eyeOf.Rmw, 1000f)) % (2.0 * Math.PI)), 0f, 0f, 0f);
                    shellTop = Mathf.Max(shellTop, topAt * 1.12f + 150f);
                    // (its rain reaches the sea under its core: from near, the clouds are walked from the sea up)
                    shellBottom = nearest < 6.0 * eyeOf.Rmw ? 0f : Mathf.Min(shellBottom, baseAt - 60f);
                }
            }
            Mark("cyclone");
            // ---- a squall line within sight, as the shader takes it (its shelf cloud is drawn along its wind's front:
            // clouds.glsl, shelfAt): the nearest whose row is near enough to see, where the weather is Natural Weather's own
            var shelfA = Vector4.zero; var shelfB = new Vector4(1f, 0f, 0f, 0f); var shelfC = Vector4.zero;
            if (CloudSource.Keystones && Keystone.Weather.Provider is Provider)
            {
                World world = World.Of(body);
                Squall line = null; float lineAhead = 0f, best = float.MaxValue;
                foreach (Squall q in Violent.Squalls(world, ut))
                {
                    if (q.Now < 0.1f || !Violent.SquallAt(world, q, camLat, camLon, out float ahead, out float along)) continue;
                    float far = Mathf.Abs(ahead) + Mathf.Max(0f, Mathf.Abs(along) - q.Length * 0.5f);
                    if (far < best) { best = far; line = q; lineAhead = ahead; }
                }
                if (line != null)
                {
                    Vector3d mid = SceneAt(body, line.Lat, line.Lon, 0.0);
                    Vector3d upThere = mid.normalized;
                    Vector3d eastThere = Vector3d.Cross(upThere, fz).normalized, northThere = Vector3d.Cross(eastThere, upThere).normalized;
                    float speed = Mathf.Sqrt(line.MoveE * line.MoveE + line.MoveN * line.MoveN);
                    Vector3d moves = (eastThere * line.MoveE + northThere * line.MoveN) / Math.Max(speed, 0.1f);
                    shelfA = new Vector4((float)mid.x, (float)mid.y, (float)mid.z, line.Length * 0.5f);
                    shelfB = new Vector4((float)moves.x, (float)moves.y, (float)moves.z, line.Now);
                    shelfC = new Vector4(line.Base, 6000f, 0f, 0f);
                    shellBottom = Mathf.Min(shellBottom, Mathf.Max(0f, line.Base - 460f));
                    shellTop = Mathf.Max(shellTop, line.Base + 4200f);
                }
            }

            // The shell as eased: its floor and top follow the shell's own over a few seconds. The lengths that go by the shell's
            // thickness (the walk's farthest steps, the distance run inside it from above, the shadow map's steps) go by these, so
            // that a floor or top that jumps, as it does whenever the weather's map is made again, a storm grows or a cyclone
            // goes through its life, does not move them all at once; the walk's steps, its weather readings and its strides
            // through clear air are laid on grids from the camera (clouds.glsl, firstStep), so that the shell's own floor and top
            // only add or take away steps at the ends of each line. Until 2026-10-18 they were laid out from where each line
            // entered the shell, and the whole picture of the clouds jumped at every change: the owner's "twitch ... spasming",
            // on every system (found and measured by the rendering research: research/rendering/experiments/cloud-twitch.md).
            // Begun afresh on another world or after a jump of more than 20 km.
            if (float.IsNaN(easedBottom) || easedBody != body.bodyName || Mathf.Abs(shellTop - easedTop) > 20000f)
            { easedBottom = shellBottom; easedTop = shellTop; easedBody = body.bodyName; }
            else
            {
                float k = 1f - Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.25f) / 3f);
                easedBottom += (shellBottom - easedBottom) * k; easedTop += (shellTop - easedTop) * k;
            }

            // How far from the camera the nearest cloud can be: under everything that can hold cloud, as far as that is above
            // the camera; and no further than a storm's rain, which hangs from its base to the ground. The clouds are laid on
            // the picture no nearer than that (see Setup).
            float clear = alt < shellBottom ? (float)(shellBottom - alt) : 0f;
            for (int i = 0; i < cellsCount; i++)
            {
                WeatherCell c = cellsNow[i];
                // (its rain as the shader draws it: an upright cylinder round the storm's middle, three quarters of its reach
                // across: clouds.glsl, rainWalk)
                if (c.Rain < 0.1f || c.Strength < 0.15f) continue;
                Vector3d axis = SceneAt(body, c.Latitude, c.Longitude, 0.0).normalized;
                double off = (camRel - axis * Vector3d.Dot(camRel, axis)).magnitude - 0.75 * c.Radius;
                clear = Mathf.Min(clear, Mathf.Max(0f, (float)off - 50f));
            }
            SheetClear = clear;
            // When the clouds are laid on: after everything see-through, as a rule. But where one camera draws the whole scene
            // (Direct3D) that puts them over smoke, flames and spray however near: a rocket's smoke at the pad stood behind the
            // clouds of the horizon, and a crash's smoke had the whole sky's heaps drawn across it (seen 2026-10-11; on OpenGL
            // the near camera draws what is near after the far camera's clouds). So there, while the camera is below
            // everything that can hold cloud, they are laid on before the see-through things: whatever of those is drawn is
            // then in front of the clouds, which from below is right for all that is lower than the clouds, the sea among
            // it. (What it gets wrong: a trail of smoke that has climbed through the clouds shows in front of them. With the
            // camera among the clouds or above them the old order is kept: seen from above, a sea drawn after the clouds
            // would be drawn over them. Kept too where Scatterer is installed: it lays its haze on from a copy of the
            // picture taken at that same moment, and which of the two comes first has not been tried.)
            // (Since 0.4.0 the clouds are laid on in their turn among the see-through things, on every system, with Scatterer or
            // without: see LaidQueue and Laying. What follows is kept only for the report.)
            bool first = !LaidLate();
            LaidFirst = first;
            int quality = CloudsPage.Quality != null ? (int)CloudsPage.Quality : 1;
            int steps = quality == 0 ? 56 : quality == 1 ? 88 : 128;
#if DEV
            steps = Mathf.RoundToInt(steps * StepsTimes);
#endif
            // (light lost per metre in the thickest cloud: a little more than a real heap's core takes, 0.05 to 0.1 a metre, since
            // only the cores reach it (the shader's density reaches it a third of the way in from a heap's edge); less, and heaps
            // against the sun were pale and their shaded sides flat)
            // (The rework of 2026-10-11: 0.07 where it was 0.15. At 0.15 a heap was solid a few metres in: a hard outline round two
            // flat tones, which the user called "comical, like anime clouds", not volumetric. At 0.07, about what a real heap takes
            // (0.03 to 0.06 a metre), its thin places glow and its body shades from the lit side to the far one.)
            float thick = 0.07f * (CloudsPage.Thickness != null ? (float)CloudsPage.Thickness : 1f);
            float amount = CloudsPage.Amount != null ? (float)CloudsPage.Amount : 1f;

            Mark("squall");
            // ---- lightning (the brightest two flashes going on now)
            int flashes = CloudFlashes.Now(body, ut, flashAt, flashBright);
#if DEV
            if (heldFlash > 0f) { flashAt[0] = heldFlashAt; flashBright[0] = heldFlash; flashes = Math.Max(flashes, 1); }
#endif
            // (the heaps made as smoke is made, Heaps.cs: the noise's own heaps are cleared from where they stand)
            Vector4 HoleVector(int i)
            {
                if (i >= Heaps.Now.Count || Heaps.Body != body) return Vector4.zero;
                Heaps.Heap heap = Heaps.Now[i];
                Vector3d at = SceneAt(body, heap.lat, heap.lon, heap.baseAt);
                return new Vector4((float)(at.x - camRel.x), (float)(at.y - camRel.y), (float)(at.z - camRel.z), heap.wide * 2.1f);
            }
            Vector4 FlashVector(int i) => i < flashes ? new Vector4((float)(flashAt[i].x - camRel.x), (float)(flashAt[i].y - camRel.y), (float)(flashAt[i].z - camRel.z), flashBright[i] * 0.6f) : Vector4.zero;

            Mark("lightning");
            // ---- the storms, as the shader takes them
            // (kept from frame to frame: two new arrays every frame were garbage the collector came round for)
            Vector4[] cellA = keptCellA, cellB = keptCellB, cellC = keptCellC;
            Array.Clear(cellA, 0, cellA.Length); Array.Clear(cellB, 0, cellB.Length); Array.Clear(cellC, 0, cellC.Length);
            // (not those inside a cyclone: its own wall and bands stand there, and each storm's anvil drawn as well was a
            // round plate lying on its canopy. The weather keeps them: their lightning lights the cyclone's cloud.)
            int cellsSent = 0;
            for (int i = 0; i < cellsCount && cellsSent < 6; i++)
            {
#if DEV
                if (NoStorms || cellsSent >= StormsSent) break;
#endif
                WeatherCell c0 = cells[i], c = cellsNow[i];
                if (eyeOf != null && Model.Distance(World.Of(body), c.Latitude, c.Longitude, eyeOf.Lat, eyeOf.Lon) < 13.5 * eyeOf.Rmw) continue;
                Vector3d mid = SceneAt(body, c.Latitude, c.Longitude, 0.0);
                cellA[cellsSent] = new Vector4((float)mid.x, (float)mid.y, (float)mid.z, c.Radius);
                cellB[cellsSent] = new Vector4(c.Base, c.Top, c.Strength, c.Rain);
                // (its life; the dome over its strongest updraught (OvershootOf) in whole metres, and in their fraction a turn of its
                // own for its turrets, the same all its life (from its number: by the wind aloft, every storm in sight had its turrets
                // on the same bearings, and they swung round as that wind turned); its anvil from 0.35, as far as the wind aloft has
                // carried it since, against the storm's own going: the ice is left behind by the storm as well as blown off it. Its
                // own going and its own span of life from the two asks it is drawn between (the model's storms, the squalls', the
                // stand-in's and another weather's alike: the model's span was used for all, the stand-in's anvils 39 % short and a
                // squall's four times long at a quarter of the pace; its drift, not how the stand-in's stand still); the model's
                // span and its drift where it was not asked for twice within ten minutes.)
                float span = (float)Model.CellSpan;
                Vector2 going = new Vector2((float)c0.Drift.x, (float)c0.Drift.y);
                double askedApart = cellsUt1 - cellsUt0;
                if (askedApart > 0.0 && askedApart <= 600.0 && nextById.TryGetValue(c0.Id, out WeatherCell c1))
                {
                    if (c1.Life > c0.Life) span = (float)(askedApart / (c1.Life - c0.Life));
                    double dLon = c1.Longitude - c0.Longitude, perDegree = body.Radius * Math.PI / 180.0;
                    if (dLon > 180.0) dLon -= 360.0; else if (dLon < -180.0) dLon += 360.0;
                    going = new Vector2((float)(dLon * perDegree * Math.Cos(c0.Latitude * Math.PI / 180.0) / askedApart), (float)((c1.Latitude - c0.Latitude) * perDegree / askedApart));
                }
                Vector2 blown = aloftEased - going;
                float turn = (float)(unchecked((uint)c.Id * 2654435761u) >> 8) / 16777216f;
                cellC[cellsSent] = new Vector4(Mathf.Atan2(blown.y, blown.x), c.Life, Mathf.Floor(OvershootOf(c)) + 0.999f * turn, blown.magnitude * Mathf.Max(c.Life - 0.35f, 0f) * span);
#if DEV
                if (cellsSent == 0) StormsSaid = "";
                StormsSaid += "#" + cellsSent + " " + (Model.Distance(World.Of(body), c.Latitude, c.Longitude, body.GetLatitude(FlightCamera.fetch != null ? FlightCamera.fetch.transform.position : Vector3.zero), body.GetLongitude(FlightCamera.fetch != null ? FlightCamera.fetch.transform.position : Vector3.zero)) / 1000.0).ToString("F1") + " km r " + c.Radius.ToString("F0") + " base " + c.Base.ToString("F0") + " top " + c.Top.ToString("F0") + " strength " + c.Strength.ToString("F2") + "; ";
#endif
                cellsSent++;
            }

            Mark("storms");
            // ---- a tornado or a waterspout near the camera, as the shader takes it (its funnel is drawn with the clouds): the
            // nearest, where the weather is Natural Weather's own
            var twistA = Vector4.zero; var twistB = Vector4.zero;
            if (CloudSource.Keystones && Keystone.Weather.Provider is Provider)
            {
                World world = World.Of(body);
                Column column = Model.ColumnAt(world, camLat, camLon, ut);
                if (Violent.Nearest(world, column.Twisters, camLat, camLon, out Twister twister, out double twistFar) && twistFar < 45000.0 && twister.Reach > 0.03f)
                {
                    double ground = 0.0;
                    try { ground = Math.Max(0.0, body.TerrainAltitude(twister.Lat, twister.Lon, true)); } catch (Exception) { }
                    Vector3d foot = SceneAt(body, twister.Lat, twister.Lon, ground);
                    twistA = new Vector4((float)foot.x, (float)foot.y, (float)foot.z, twister.Core);
                    // (how far it has turned: the wind at its core's edge goes round once in 2 pi core / speed seconds)
                    double turned = twister.Spin * ut * twister.Speed / Math.Max(twister.Core, 5f) * 0.35;
                    twistB = new Vector4(Mathf.Max(100f, twister.Base - (float)ground), twister.Reach, twister.Water ? 1f : 0f, (float)(turned % 6283.185307));
                    // (the clouds are laid on the picture no nearer than the nearest cloud can be: the funnel is cloud too)
                    double off = (camRel - foot.normalized * Vector3d.Dot(camRel, foot.normalized)).magnitude - (5.0 * twister.Core + 0.2 * twistB.x);
                    if (alt < twister.Base + 200.0) clear = Mathf.Min(clear, Mathf.Max(0f, (float)off - 30f));
                }
            }
            SheetClear = clear;

            Mark("tornado");
            // ---- the shadow's square
            bool shadows = CloudsPage.Shadows == null || CloudsPage.Shadows;
#if DEV
            if (Trial == 20f) shadows = false;      // (for finding a fault: the clouds' shadows left off)
#endif
            double side = Math.Max(50000.0, Math.Min(600000.0, alt * 3.0 + 50000.0));
            Vector3d groundFixed = camFixed.normalized * R;
            bool shadowAll = shadowMap == null || TimeWarp.CurrentRate > 8f;     // (the whole of the shadow's map this frame, not a quarter of it)
            if (shadowBody != body.bodyName || (groundFixed - shadowMid).magnitude > side / 8.0 || Math.Abs(side - shadowSide) > shadowSide * 0.25)
            {
                shadowAll = true;
                shadowBody = body.bodyName;
                shadowMid = groundFixed;
                shadowSide = side;
                Vector3d n = groundFixed.normalized;
                Vector3d a = Vector3d.Cross(n, new Vector3d(0, 0, 1));
                if (a.magnitude < 0.1) a = Vector3d.Cross(n, new Vector3d(1, 0, 0));
                a = a.normalized;
                shadowX = (Vector3)a;
                shadowY = (Vector3)Vector3d.Cross(n, a).normalized;
            }
            groundHeight = (float)Math.Max(0.0, Math.Min(alt, FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.terrainAltitude : 0.0));
            Vector3d shadowMidScene = fx * shadowMid.x + fy * shadowMid.y + fz * shadowMid.z;
            // (the sun dimmed by the cloud above the craft, eased: a cloud's edge passes over in a few seconds)
            sunDim = shadows ? Mathf.MoveTowards(sunDim, sunAbove, Time.unscaledDeltaTime * 0.8f) : 1f;
            SunThrough = sunDim;

            Mark("shadow's square");
            // ---- the map of the low layer's heaps (drawn in the far camera's drawing, before the shadow and the walk; read
            // by both cameras' walks and by the shadow's). On the face of the noise's cube that the camera is nearest.
            bool heapsWanted = Form > 0.5f && HeapsDots >= 64;
#if DEV
            if (Trial == 21f) heapsWanted = false;      // (for timing: the heaps worked out at every asking, as before the map)
#endif
            if (heapsMap != null && (!heapsWanted || heapsMap.width != HeapsDots)) FreeHeaps();
            if (heapsWanted && heapsMap == null)
            {
                RenderTexture Make(RenderTextureFormat f, string name)
                {
                    var t = new RenderTexture(HeapsDots, HeapsDots, 0, f, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, useMipMap = false, name = "Natural Weather: " + name };
                    t.Create();
                    return t;
                }
                heapsMap = Make(RenderTextureFormat.ARGBHalf, "the heaps' map");
                heapsMapB = Make(RenderTextureFormat.RHalf, "the heaps' map (2)");
            }
            // (not in the second form, which never reads it: kept, for going back)
            heapsOn = !Two && heapsWanted && heapsMap != null && heapsMap.IsCreated() && heapsMapB.IsCreated();
            Vector3d upF = camFixed.normalized;
            double upX = Math.Abs(upF.x), upY = Math.Abs(upF.y), upZ = Math.Abs(upF.z);
            float heapsFace = upX >= upY && upX >= upZ ? 0f : upY >= upZ ? 1f : 2f;

            Mark("heaps' map");
            // (From above all the clouds the map is spread wider, four times the camera's height across or more (a dot then some
            // hundreds of metres, a few of them to a heap: as fine as the picture is from there), so that the heaps of the
            // world below are read from it and not worked out at every asking: from orbit, worked out so, they cost six
            // milliseconds a frame more than the map does.)
            float heapsAcross = HeapsWidth(alt > shellTop + 500.0 ? 4.0 * alt : 0.0);
            // ---- everything the shaders share
            void Common(Material m, Camera cam, float width)
            {
                float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
                bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
                m.SetVector(idSheet, new Vector4(1f, 1f, 0.5f, 1f));
                m.SetVector(idCam, new Vector4((float)camRel.x, (float)camRel.y, (float)camRel.z, (float)R));
                m.SetVector(idHigh, new Vector4((float)alt, air.Top, air.ScaleHeight, width));
                Vector3 r = cam.transform.right * tanX, u = cam.transform.up * tanY, f = cam.transform.forward;
                m.SetVector(idRight, new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
                m.SetVector(idUp, new Vector4(u.x, u.y, u.z, 0f));
                m.SetVector(idAhead, new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
                m.SetVector(idFixedX, (Vector3)fx); m.SetVector(idFixedY, (Vector3)fy); m.SetVector(idFixedZ, (Vector3)fz);
                m.SetVector(idNoise, noise);
                m.SetVector(idDrift, Two ? new Vector4(leanFixed.x, leanFixed.y, leanFixed.z, boil) : new Vector4(windDir.x, windDir.y, windDir.z, boil));
                m.SetVector(idStreets, new Vector4(streetsAcross.x, streetsAcross.y, streetsAcross.z, streetsStrength));
                m.SetVector(idAlong, new Vector4(streetsAlong.x, streetsAlong.y, streetsAlong.z, 0f));
                m.SetVector(idShell, new Vector4(shellBottom, shellTop, map.HighCloud, 1f));
                m.SetVector(idShellEased, new Vector4(easedBottom, easedTop, 0f, 0f));
                shellTopNow = shellTop;
                m.SetVector(idLook, new Vector4(thick, 0.85f, amount, steps));
                m.SetVector(idLight2, new Vector4(LightGain, LightSky, LightGround, BillowFiner));
                m.SetVector(idDeck2, new Vector4(DeckCrease2.x, DeckCrease2.y, DeckRelief2, 0f));
                m.SetVector(idSpec, specNow);
                m.SetVector(idAnvil, new Vector4(AnvilKnobs.x, AnvilKnobs.y, AnvilKnobs.z + 100f * Mathf.Round(AnvilCore * 10f), AnvilKnobs.w));
                // (How the heaps come today. The field has heaps of every size, and all of them in the sky at once, hundreds of
                // small ones each by itself, is a sky that does come but is not the common one: on about one day in eight. On
                // the others the small heaps that stand alone are left out and a shallow layer's heaps are read half as large
                // again: fewer, larger, flatter. Which it is goes by the day (the planet's own), eased over some minutes.)
                {
                    uint dayNow = unchecked((uint)(long)Math.Floor(Planetarium.GetUniversalTime() / Math.Max(body.rotationPeriod, 3600.0)));
                    uint h = dayNow * 2654435761u + 40503u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                    float wanted = HeldHabit >= 0f ? HeldHabit : (h & 0xffff) / 65536f < 0.12f ? 0f : 1f;
                    habit = float.IsNaN(habit) ? wanted : Mathf.MoveTowards(habit, wanted, Time.unscaledDeltaTime / 300f);
                    // (and on the usual day the heaps that reach the layer's top spread out under it, and no heap is round)
                    m.SetVector("_CloudHabit", new Vector4(habit, Mathf.Lerp(1f, Broader, habit), LidSpread * habit, Mathf.Lerp(70f, Pushed, habit)));
                }
                m.SetVector(idSun, new Vector4(toSun.x, toSun.y, toSun.z, 1.0f * brightness));
                // (the second form's sun above the air as SunTint2 says: the air's own table colours it on the way down, at a low sun
                // the most)
                m.SetVector(idSunTint, Two ? new Vector4(SunTint2.x, SunTint2.y, SunTint2.z, night) : new Vector4(1f, 0.985f, 0.96f, night));
                // (a full Mun's own light on a veil of ice: twelve times the night's light from all round, which is the sky's
                // glow and the Mun's light scattered about together; at twice, the ring and the glow round the Mun were lost)
                m.SetVector(idMoon, new Vector4(toMoon.x, toMoon.y, toMoon.z, 12f * night * moonLit));
                m.SetVector(idSky, new Vector4(skyLight.r, skyLight.g, skyLight.b, Two ? HazeTint2 : 1f));
                m.SetVector(idGround, new Vector4(groundLight.r, groundLight.g, groundLight.b, 120f));
                m.SetVector(idAir, new Vector4(air.Rayleigh.x, air.Rayleigh.y, air.Rayleigh.z, air.Haze));
                // (and whether the world has a sea: from above the clouds the walk shades the sea's surface, which the camera's
                // picture of depths does not hold, as the sea is shaded, not as land)
                m.SetVector(idAirHigh, new Vector4(air.RayleighHeight, air.HazeHeight, body.ocean ? 1f : 0f, SkyTint2));
                // (the air's light scattered again and again: in the second form once, its clouds far off as milky as the hills there)
                m.SetVector(idHaze, new Vector4(noiseHasMips ? 1f : 0f, 40f, Two ? HazeAir2.x : 1.5f, Two ? HazeAir2.y : 1f));
                m.SetVector(idFlashA, FlashVector(0)); m.SetVector(idFlashB, FlashVector(1));
                m.SetVector(idHoleA, HoleVector(0)); m.SetVector(idHoleB, HoleVector(1)); m.SetVector(idHoleC, HoleVector(2));
                m.SetVector(idFlashTint, new Vector4(0.75f, 0.8f, 1f, 900f));
                for (int i = 0; i < 6; i++) { m.SetVector(idCellA[i], cellA[i]); m.SetVector(idCellB[i], cellB[i]); m.SetVector(idCellC[i], cellC[i]); }
                m.SetVector("_CloudTwistA", twistA); m.SetVector("_CloudTwistB", twistB);
                m.SetVector("_CloudShelfA", shelfA); m.SetVector("_CloudShelfB", shelfB); m.SetVector("_CloudShelfC", shelfC);
                m.SetVector("_CloudEyeA", eyeA); m.SetVector("_CloudEyeB", eyeB); m.SetVector("_CloudEyeC", eyeC); m.SetVector("_CloudEyeD", eyeD);
                m.SetVector(idSunFixed, ToFixed(toSun));
                m.SetVector(idShadowAt, new Vector4((float)shadowMid.x, (float)shadowMid.y, (float)shadowMid.z, (float)shadowSide));
                m.SetVector(idShadowX, new Vector4(shadowX.x, shadowX.y, shadowX.z, groundHeight));
                // (w: whether the walk may ask the shadow's map what lies towards the sun)
                m.SetVector(idShadowY, new Vector4(shadowY.x, shadowY.y, shadowY.z, shadows && shadowMapB != null && shadowMapB.IsCreated() ? 1f : 0f));
                // (the light field the walk reads: the one made whole; nought in w where there is none yet)
                bool lightOn = LightField && !lightHigh && lightReady && lightRead != null;
                SetLight(m, readMid, readEast, readNorth, readLow, readStep, lightOn ? 1f : 0f, fx, fy, fz);
                m.SetTexture(idLight, lightOn ? (Texture)lightRead : Texture2D.whiteTexture);
                m.SetTexture(idShape, shape); m.SetTexture(idDetail, detail);
                // (the second form's billows and pattern, where it is drawn; elsewhere a cube of one place, and a dot of grey: NoPattern)
                m.SetTexture("_CloudBillow", billow != null && billow.IsCreated() ? (Texture)billow : noField);
                m.SetTexture("_CloudPattern", PatternNow());
                // (the shapes of the rework of 2026-10-12: whether, and the time in lifetimes of a heap, kept within 1024 of
                // them, where the shader's numbering of each point's heaps comes round)
                CloudsWake.Hand(m, ut, noiseAt, NoiseWrap, CloudsPage.Stirred == null || CloudsPage.Stirred);
                if (cellsTable == null) MakeCellsTable();
                m.SetTexture("_CloudCells", cellsTable);
                m.SetTexture("_CloudHeaps", heapsOn ? (Texture)heapsMap : Texture2D.blackTexture);
                m.SetTexture("_CloudHeapsB", heapsOn ? (Texture)heapsMapB : Texture2D.blackTexture);
                m.SetVector("_CloudHeapsAt", new Vector4(heapsOn ? 1f : 0f, heapsAcross / (float)NoiseLength, HeapsDots, heapsFace));
#if DEV
                m.SetVector("_CloudForm", new Vector4(Form, (float)Wrap(ut / Math.Max(HeapLife, 60f), 1024.0), Trial, FarOff ? 1f : 0f));
#else
                m.SetVector("_CloudForm", new Vector4(Form, (float)Wrap(ut / Math.Max(HeapLife, 60f), 1024.0), 0f, 0f));
#endif
                // (the heaps made beforehand: carried by the wind east and north, leaning with it by up to 260 m from base to top)
                bool made = field != null && (CloudsPage.Made == null || CloudsPage.Made);
                m.SetTexture(idField, made && FieldTest != 1f ? field : noField);
                m.SetVector(idFieldA, new Vector4(1f / fieldSide, 2f * fieldFar, (float)(-fieldDrift.x / fieldSide), (float)(-fieldDrift.y / fieldSide)));
                float leans = Mathf.Min((float)heapsWind.magnitude * 18f, 260f);
                Vector2 leanWay = heapsWind.magnitude > 0.01 ? new Vector2((float)(heapsWind.x / heapsWind.magnitude), (float)(heapsWind.y / heapsWind.magnitude)) : Vector2.zero;
                m.SetVector(idFieldB, new Vector4(made ? 1f : 0f, leanWay.x * leans, leanWay.y * leans, FieldLumps));
                m.SetVector(idFieldLevA, new Vector4(fieldLevels[0], fieldLevels[1], fieldLevels[2], fieldLevels[3]));
                m.SetVector(idFieldLevB, new Vector4(fieldLevels[4], fieldLevels[5], fieldLevels[6], fieldLevels[7]));
                m.SetVector(idFieldLevC, new Vector4(fieldLevels[8], fieldLevels[9], fieldLevels[10], fieldLevels[11]));
                m.SetVector(idFieldLevD, new Vector4(fieldLevels[12], fieldLevels[13], fieldLevels[14], fieldLevels[15]));
                m.SetTexture(idMap, mapFine != null && mapFine.IsCreated() ? (Texture)mapFine : map.A); m.SetTexture(idMapB, map.B); m.SetTexture(idMapC, map.C);
                m.SetTexture(idLut, air.Lut);
            }

            if (map.A != null)
            {
                if (mapFine == null)
                {
                    mapFine = new RenderTexture(map.A.width * 4, map.A.height * 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, name = "Natural Weather: the low clouds' map, smoothed" };
                    mapFine.Create();
                }
                if (mapFineWalk == null) mapFineWalk = new Material(walkShader) { name = "Natural Weather: the low clouds' map, smoothed" };
                if (mapFineWalk.shader != WalkWhole) mapFineWalk.shader = WalkWhole;
                Common(mapFineWalk, farCam, mapFine.width);
                mapFineWalk.SetVector(idStage, new Vector4(6f, 0f, 0f, 0f));
                mapFineWalk.SetTexture(idMap, map.A);
            }

            if (heapsOn)
            {
                if (heapsWalk == null) heapsWalk = new Material(walkShader) { name = "Natural Weather: the heaps' map" };
                if (heapsWalk.shader != WalkWhole) heapsWalk.shader = WalkWhole;
                Common(heapsWalk, farCam, HeapsDots);
                heapsWalk.SetVector(idStage, new Vector4(5f, 0f, 0f, 0f));
                // (what is drawn into is not also read)
                heapsWalk.SetTexture("_CloudHeaps", Texture2D.blackTexture);
                heapsWalk.SetTexture("_CloudHeapsB", Texture2D.blackTexture);
            }

            Mark("shared");
            // ---- the shadow map (made in the far camera's drawing, used by both)
            if (shadows)
            {
                if (shadowMap == null)
                {
                    shadowMap = new RenderTexture(Shadow, Shadow, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the clouds' shadow" };
                    shadowMap.Create();
                    shadowMapB = new RenderTexture(Shadow, Shadow, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the clouds' shadow (2)" };
                    shadowMapB.Create();
                }
                if (shadowWalk == null) shadowWalk = new Material(walkShader) { name = "Natural Weather: shadow walk" };
                if (shadowWalk.shader != WalkWhole) shadowWalk.shader = WalkWhole;
                Common(shadowWalk, farCam, Shadow);
                shadowWalk.SetVector(idStage, new Vector4(4f, 0f, 0f, 0f));
                // (and how wide a pixel of the far camera's walk is for each metre off, by which the shader chooses the near shapes or
                // the far field at each place of the map as the walk chooses them for it: told only of the map's own 512 dots, it
                // chose the far field everywhere, and heaps drawn by the near shapes had the far field's shadows under them)
                // (and, z, whether the map is drawn whole this frame: then without the lumps on the heaps, which cost half as much again
                // and would come all at once, and in their strips within the next eight frames)
                shadowWalk.SetVector("_CloudJitter", new Vector4(0f, 0f, shadowAll ? 1f : 0f, WalkPixel(farCam, quality)));
                // (an eighth of the map each frame, one strip of eight in turn: see the shader's go 4 for why not by `discard`; a
                // quarter until 2026-10-18, when the map began to be made of the clouds as drawn)
                shadowStrip = shadowAll ? -1 : (frame & 7);
            }

            Mark("shadow map");
            // ---- the light field: a band of the one being made each frame (drawn in the far camera's shade pass, after the shadow
            // map); when it is whole it becomes the one read, and the next is begun round where the camera will be halfway through
            // the round it is read in (the camera's speed over the planet, smoothed)
            // (Neither made nor read with the camera far above the clouds' top: from there every cloud is drawn by the far field,
            // which the field's cells are too coarse to light (research/rendering/experiments/hurricane-towers-from-orbit.md), and
            // the main chat measured it costing 0.8 ms a frame from 200 km (2026-10-18). Off above 15 km over the top; below 12 km
            // again, a whole field made afresh before it is read.)
            float overTop = (float)alt - shellTop;
            if (!lightHigh && overTop > 15000f) { lightHigh = true; lightReady = false; lightBand = -1; }
            else if (lightHigh && overTop < 12000f) lightHigh = false;
            if (LightField && !lightHigh)
            {
                if (lightMaking == null) { lightRead = MakeLight("read"); lightMaking = MakeLight("being made"); lightReady = false; lightBand = -1; }
                if (lightBody != body.bodyName) { lightBody = body.bodyName; lightReady = false; lightBand = -1; lightCamKnown = false; }
                float dtNow = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                if (lightCamKnown && (camFixed - lightCamWas).magnitude < 20000.0)
                    lightGoes = Vector3d.Lerp(lightGoes, (camFixed - lightCamWas) / dtNow, Mathf.Clamp01(dtNow * 2f));
                else lightGoes = Vector3d.zero;
                lightCamWas = camFixed; lightCamKnown = true;
                if (lightBand < 0 || lightBand >= LightRound)
                {
                    if (lightBand >= LightRound)
                    {
                        RenderTexture was = lightRead; lightRead = lightMaking; lightMaking = was;
                        readMid = lightMid; readEast = lightEast; readNorth = lightNorth; readLow = lightLow; readStep = lightStep;
                        lightReady = true;
#if DEV
                        traceSwap = true;
#endif
                    }
                    Vector3d at = camFixed + lightGoes * (1.5 * LightRound * dtNow);
                    lightMid = at.normalized;
                    Vector3d across = Vector3d.Cross(new Vector3d(0.0, 0.0, 1.0), lightMid);
                    if (across.sqrMagnitude < 1e-6) across = Vector3d.Cross(new Vector3d(0.0, 1.0, 0.0), lightMid);
                    lightEast = across.normalized;
                    lightNorth = Vector3d.Cross(lightMid, lightEast).normalized;
                    lightLow = Mathf.Max(0f, easedBottom); lightStep = Mathf.Max(easedTop - lightLow, 300f) / LightHeights;
                    lightBand = 0;
                }
                if (lightWalk == null) lightWalk = new Material(walkShader) { name = "Natural Weather: the light field" };
                if (lightWalk.shader != WalkWhole) lightWalk.shader = WalkWhole;
                Common(lightWalk, farCam, 4 * LightCells);
                // (and how wide a pixel of the far camera's walk is, as the shadow's map is told: see the shader's formChoice)
                lightWalk.SetVector("_CloudJitter", new Vector4(0f, 0f, 0f, WalkPixel(farCam, quality)));
                lightWalk.SetVector(idStage, new Vector4(9f, 0f, 0f, 0f));
                SetLight(lightWalk, lightMid, lightEast, lightNorth, lightLow, lightStep, 0f, fx, fy, fz);
                // (what is drawn into is not also read)
                lightWalk.SetTexture(idLight, Texture2D.whiteTexture);
            }
            else if (!LightField && lightMaking != null)
            {
                lightRead.Release(); Destroy(lightRead); lightRead = null;
                lightMaking.Release(); Destroy(lightMaking); lightMaking = null;
                lightReady = false; lightBand = -1;
            }

            Mark("light field");
            // ---- which copy of the shader walks the clouds this frame (2026-10-18). A feature's code costs the walk even where it
            // does not run: without the cyclone and the tornado the walk is about a quarter cheaper, without the storm's parts as
            // well about two fifths, for the same picture (research/rendering/experiments/cloud-shader-variants.md). So the whole
            // shader only where a cyclone or a tornado is near; the storm's copy where there are storm cells, lightning, a squall
            // line or heaps made as smoke; the calm one otherwise. (Where a copy is missing, an older bundle, the next fuller one.)
            bool stormy = shelfA.w > 0f || flashes > 0 || cells.Count > 0 || (Heaps.Body == body && Heaps.Now.Count > 0);
            // (and of the form being drawn: see FormTwo)
            bool two = Two;
            Shader whole = two ? walk2 : walkShader, storm = two ? walkStorm2 : walkStorm, calm = two ? walkCalm2 : walkCalm;
            walkNow = eyeA.w > 0f || twistA.w > 0f ? whole : stormy ? (storm ?? whole) : (calm ?? storm ?? whole);
#if DEV
            if (CopyHeld == 1) walkNow = whole; else if (CopyHeld == 2 && storm != null) walkNow = storm;
            CopyNow = (two ? "the second form's " : "the ") + (walkNow == whole ? "whole shader" : walkNow == storm ? "storm's copy" : "calm copy");
#endif

            Mark("which shader");
            // ---- under the sea nothing of the clouds is laid on: what shows of the sky from below comes through the water's
            // window overhead, which the sea's own pass under the water draws. Laid on as everywhere, they showed through the sea
            // as a broken line of white near the horizon from 12 m down, and the clouds' shadow on the ground was laid over the
            // water as well (2026-10-25). Only once the whole of the near camera's glass is under: the sea's lens draws the water's
            // surface where it cuts that glass, a hand's breadth across, and the sky above the cut is the camera's own picture,
            // clouds and all; taken at the camera's middle, the clouds came and went with every wave at the waterline.
            {
                float tanU = Mathf.Tan(nearCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float glass = nearCam.nearClipPlane * Mathf.Sqrt(1f + tanU * tanU * (1f + nearCam.aspect * nearCam.aspect)) + 0.25f;
                underSea = Keystone.Water.Depth(body, (Vector3d)nearCam.transform.position) > glass;
#if DEV
                if (underSeaAsBefore) underSea = false;
#endif
            }
            // ---- each camera's clouds
            const float nearReach = 450f;
            bool nearWanted = split && alt > shellBottom - nearReach && alt < shellTop + nearReach;
            Setup(far, farCam, split ? farCam.nearClipPlane * 0.98f : 0f, 0f, quality, shadows, true, Common, body, camFixed, ToFixed, shadowMidScene, sunUp, clear, first);
            if (nearWanted) { ShadowAloneOff(); Setup(near, nearCam, 0f, nearCam.farClipPlane * 1.02f, quality, shadows, false, Common, body, camFixed, ToFixed, shadowMidScene, sunUp, clear, false); }
            else
            {
                if (near.On) Detach(near);
                ShadowAlone(split ? nearCam : null, shadows, body, shadowMidScene, sunUp);
            }
            Mark("each camera");
            Laying(farCam, nearCam);
            Drawing = true;
            Mark("laying");
            // (the words twice a second, not a new string every frame)
            if (Time.unscaledTime - reportAt > 0.5f || Report.Length == 0)
            {
                reportAt = Time.unscaledTime;
            Report = "Clouds from " + CloudSource.Name + ": " + (near.On ? "near and far" : "far") + (map.HighestTop > 0f ? ", " + Mathf.RoundToInt(map.LowestBase) + " to " + Mathf.RoundToInt(map.HighestTop) + " m" : "") +
                     (cells.Count > 0 ? ", " + cells.Count + " storm" + (cells.Count > 1 ? "s" : "") + " near" : "") + (sunDim < 0.98f ? "; the craft in their shade (" + Mathf.RoundToInt(100f * sunDim) + "% of the sun)" : "") +
                     (CloudsEve.Count > 0 ? "; EVE's clouds hidden" : "");
            }
#if DEV
            if (traceLeft > 0) TraceFrame(shellBottom, shellTop, camFixed, R);
#endif
        }

        readonly Vector4[] keptCellA = new Vector4[6], keptCellB = new Vector4[6], keptCellC = new Vector4[6];
        // (the storms as drawn this frame; and for the second form the most each storm's reach and top have been, by its number,
        // kept while it is found: see the shell in Frame)
        readonly WeatherCell[] cellsNow = new WeatherCell[6];
        readonly Dictionary<int, Vector2> heldSize = new Dictionary<int, Vector2>();
        readonly List<int> heldGone = new List<int>();
        // (the second form's wind aloft for the anvils and how unstable the air is for the domes, eased, and the game's time they
        // were last eased at; the low wind for the lean, eased: see Frame)
        Vector2 aloftEased, leanEased;
        float unstableEased;
        double aloftUt;
        bool aloftEasedSet, leanSet;
        float reportAt = -10f;

        void FreeHeaps()
        {
            if (heapsMap != null) { heapsMap.Release(); Destroy(heapsMap); heapsMap = null; }
            if (heapsMapB != null) { heapsMapB.Release(); Destroy(heapsMapB); heapsMapB = null; }
        }

        /// <summary>The storms found (see Frame): the nearest six kept, those strong enough to be drawn before those that are not,
        /// and where each will be at the next moment asked.</summary>
        void Found(CelestialBody body, Vector3d here, List<WeatherCell> found, List<WeatherCell> next, double t0, double t1)
        {
            cells.Clear(); cells.AddRange(found);
            // (by nearness alone, weak storms the shader does not draw (under 0.15: cellAt) crowded drawn ones further off out of
            // the six, and those popped in and out whole as the near ones' strength crossed 0.15 (the second review))
            cells.Sort((a, b) => a.Strength >= 0.15f != b.Strength >= 0.15f ? (a.Strength >= 0.15f ? -1 : 1) : Distance(body, a, here).CompareTo(Distance(body, b, here)));
            if (cells.Count > 6) cells.RemoveRange(6, cells.Count - 6);
            // (the sizes held only for storms still found, every one of them, not only the six)
            heldGone.Clear();
            foreach (int id in heldSize.Keys)
            {
                bool still = false;
                foreach (WeatherCell f in found) if (f.Id == id) { still = true; break; }
                if (!still) heldGone.Add(id);
            }
            foreach (int id in heldGone) heldSize.Remove(id);
            cellsUt0 = t0; cellsUt1 = t1;
            nextById.Clear();
            foreach (WeatherCell n in next) nextById[n.Id] = n;
        }
        readonly List<WeatherCell> cellsFound = new List<WeatherCell>(), cellsFoundNext = new List<WeatherCell>();
        System.Threading.Tasks.Task cellsWork;
        string cellsWorkBody;
        double cellsWorkUt0, cellsWorkUt1;

        static double Distance(CelestialBody body, WeatherCell c, Vector3d camRel) => (SceneAt(body, c.Latitude, c.Longitude, 0.0) - camRel).sqrMagnitude;

        // (Never the length itself: a drift that stands still but for rounding, a hair under nought on a day when the wind has nothing
        // along the planet's axis, came to the length exactly when the length was added back (the hair is far under what a double
        // holds beside 1863 km), the length to nought the next frame, and so on: the noise's place along the axis was 0 and 207
        // lengths in turn, and the patterns read at other repeats (the banks', the cloudlets' bend) drew two skies, one each frame
        // (the main chat's held views of 2026-10-19, four in seven).)
        static double Wrap(double v, double length) { v %= length; if (v < 0.0) v += length; return v < length ? v : 0.0; }

        delegate void CommonValues(Material m, Camera cam, float width);
        delegate Vector3 Turn(Vector3 v);

        // (How much smaller each way than the picture the far camera's walk is: a quarter for "lower" and "middle", a half for
        // "higher". What is kept from frame to frame is the picture's own size, each frame's lines going through one of the
        // sixteen (or four) pixels each of the walk's dots stands for, in turn. Until 2026-10-18 "middle" walked at half size and
        // "lower" at a third without the finer keeping (blurred by three each way); in the lab the quarter walk with the light
        // field took a quarter to a third of the old middle's time, as close to the truth from the ground and inside a layer,
        // and closer than the old middle flying at 150 m/s and turning ten degrees a second (research/rendering/experiments/
        // cloud-light-field.md, research/rendering/notes/clouds-plan.md 3).)
        static int FarDiv(int quality) => quality == 2 ? 2 : 4;

        // (How wide a pixel of the far camera's walk is, for each metre off: as Setup sizes that walk's pictures (makesShadow), and
        // as the shader's formWeather works out formPer from them.)
        static float WalkPixel(Camera cam, int quality)
        {
            int div = FarDiv(quality);
            int w = Mathf.Max(16, cam.pixelWidth / div);
            int kept = Finer ? div * w : w;
            return 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect / kept;
        }

        /// <summary>A point of the R2 sequence, each of its two numbers from -0.5 to 0.5: spread as evenly as any over the first few, and never the same.</summary>
        static Vector2 SubDot(int n) => new Vector2((float)((0.5 + n * 0.7548776662) % 1.0) - 0.5f, (float)((0.5 + n * 0.5698402910) % 1.0) - 0.5f);

        /// <summary>
        /// The clouds' shadow laid on what a camera has drawn, into its pass (cloudshade.glsl): the scene is drawn with the sun dimmed
        /// by the cloud above the craft (BeforeCamera), and each pixel's light is put right for the cloud above its own place,
        /// darker in a shadow the craft is not in, lighter in a gap of sunlight when the craft is in shade.
        /// </summary>
        void LayShadow(Material s, CommandBuffer into, Camera cam, CelestialBody body, Vector3d shadowMidScene, float sunUp, bool makesShadow, RenderTexture histD)
        {
            bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            Vector3 r = cam.transform.right * tanX, u = cam.transform.up * tanY, f = cam.transform.forward;
            Vector3d camRelScene = (Vector3d)cam.transform.position - body.position;
            s.SetVector(idSheet, new Vector4(1f, 1f, 0.5f, 1f));
            s.SetVector(idRight, new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
            s.SetVector(idUp, new Vector4(u.x, u.y, u.z, 0f));
            s.SetVector(idAhead, new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
            s.SetVector(idCam, new Vector4((float)camRelScene.x, (float)camRelScene.y, (float)camRelScene.z, (float)body.Radius));
            s.SetVector(idSun, sunNow);
            s.SetVector(idShadeAt, new Vector4((float)shadowMidScene.x, (float)shadowMidScene.y, (float)shadowMidScene.z, (float)shadowSide));
            Vector3 sx = SceneOfFixed(body, shadowX), sy = SceneOfFixed(body, shadowY);
            s.SetVector(idShadeX, new Vector4(sx.x, sx.y, sx.z, groundHeight));
            s.SetVector(idShadeY, sy);
            // (how much brighter the sun makes what faces it than the sky alone does: less with the sun low, when the air has
            // dimmed it and the sky's share is larger)
            float sunOverSky = 1.25f * Mathf.Clamp01(sunUp * 2.5f + 0.15f);
            // (A far shadow fades into the light the craft is in, to half at 30 km: on and near the ground, ground further off
            // is under more air and the craft's own light is the better guess there.)
            // (From above all the clouds the walk lays each heap's shadow itself, per pixel (clouds.glsl, "the clouds' shadows
            // on the ground"): the square's is left off there, faded to nothing, so that nothing is shaded twice.)
            float camHigh = (float)(camRelScene.magnitude - body.Radius);
            // (how far the camera is above all the clouds, as the walk has it (clouds.glsl, aboveness): 0 within 100 m over the
            // shell's top, 1 from 1100 m over it. The square's shadow is weighted by one less this, the walk's own by this, so that
            // between the two nothing is shaded twice or not at all. Left off at a step 500 m over the top, as until 2026-10-18,
            // it went out in one frame while the walk's came in over a kilometre.)
            float above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shellTopNow + 100f, shellTopNow + 1100f, camHigh));
            s.SetVector(idShade, new Vector4(sunOverSky, 30000f, makesShadow ? cam.nearClipPlane * 0.98f : 0f, makesShadow ? 1e9f : cam.farClipPlane * 1.02f));
            // (what the game's sun was dimmed by for this camera: nothing under deferred shading, see DimsFor)
            float dimmedBy = DimsFor(cam) ? sunDim : 1f;
            s.SetVector(idShadeSun, new Vector4(dimmedBy, body.ocean ? 1f : 0f, above, 0f));
#if DEV
            s.SetVector(idShadeTint, TestNoTint ? new Vector4(1f, 1f, 1f, 0f) : shadeTint);
            TintSaid = "tint " + shadeTint.x.ToString("F2") + ", " + shadeTint.y.ToString("F2") + ", " + shadeTint.z.ToString("F2") + (TestNoTint ? " (held off)" : "") + "; above the clouds " + above.ToString("F2");
#else
            s.SetVector(idShadeTint, shadeTint);
#endif
            // (and wet ground is darker ground, as far as the wet is known round the camera: Wet.cs)
            s.SetVector("_CloudWet", new Vector4(Weather.WetGround ? Wet.GroundShown : 0f, 2500f, 0f, 0f));
            s.SetTexture(idShadowMap, shadowMap);
            // (and this camera's clouds as taken together this frame, made before this pass: from above the clouds, how much
            // the walk's own shadow takes from the land, which this pass tints)
            s.SetTexture(idHistD, histD != null ? (Texture)histD : Texture2D.blackTexture);
            into.DrawMesh(sheet, Matrix4x4.identity, s, 0, 0);
            // (And the same shadow handed to what is drawn after this pass and cannot be shaded by it: Natural Ocean's sea,
            // drawn after Scatterer copies the picture, which reads the shadow at each point of its surface for its sun's
            // glitter and light (ocean.glsl). In the world's own axes, from the pass that makes the shadow, once a frame.)
            if (makesShadow)
            {
                Vector3d mid = body.position + shadowMidScene;
                Shader.SetGlobalTexture(idWeatherShadowMap, shadowMap);
                Shader.SetGlobalVector(idWeatherShadowAt, new Vector4((float)mid.x, (float)mid.y, (float)mid.z, (float)shadowSide));
                Shader.SetGlobalVector(idWeatherShadowX, new Vector4(sx.x, sx.y, sx.z, groundHeight));
                Shader.SetGlobalVector(idWeatherShadowY, new Vector4(sy.x, sy.y, sy.z, 0f));
                Shader.SetGlobalVector(idWeatherShadowCentre, new Vector4((float)body.position.x, (float)body.position.y, (float)body.position.z, (float)body.Radius));
                Shader.SetGlobalVector(idWeatherShadowFade, new Vector4(dimmedBy, 30000f, above, 0f));
                Shader.SetGlobalVector(idWeatherShadowSun, new Vector4(sunNow.x, sunNow.y, sunNow.z, 1f));
            }
        }

        // ---- the near camera's shadow alone (2026-10-21). Its shadow is laid by the near camera's clouds' pass, which is drawn only
        // within 450 m of the clouds (Setup); below that the near camera laid none. The scene's sun is dimmed for both cameras by the
        // cloud above the craft, and only the far camera's ground was put right: with the craft under a heap, the ground within 400 m
        // of the camera stayed dimmed all over while beyond it the sunlit land came back, a straight edge across the picture where
        // the two cameras meet (up to 85 levels of 255 in a climb from the pad; and a heap's shadow within 400 m was not laid at all).
        // research/rendering/experiments/near-far-seam.md.
        Material nearShadow;
        CommandBuffer nearShade;
        Camera nearShadeCam;
        bool nearShadeAskedDepth;

        void ShadowAlone(Camera cam, bool shadows, CelestialBody body, Vector3d shadowMidScene, float sunUp)
        {
            if (cam == null || !shadows || shadowMap == null || shadeShader == null) { ShadowAloneOff(); return; }
            if (nearShadow == null)
            {
                nearShadow = new Material(shadeShader) { name = "Natural Weather: shadow laid on (near, alone)" };
                nearShade = new CommandBuffer { name = "Natural Weather: clouds' shadow (near, alone)" };
            }
            if (nearShadeCam != cam) { ShadowAloneOff(); cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, nearShade); nearShadeCam = cam; }
            if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; nearShadeAskedDepth = true; }
            nearShade.Clear();
            LayShadow(nearShadow, nearShade, cam, body, shadowMidScene, sunUp, false, null);
        }

        void ShadowAloneOff()
        {
            if (nearShadeCam != null)
            {
                if (nearShade != null) nearShadeCam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, nearShade);
                if (nearShadeAskedDepth) nearShadeCam.depthTextureMode &= ~DepthTextureMode.Depth;
            }
            nearShadeCam = null; nearShadeAskedDepth = false;
        }

        void Setup(Pass p, Camera cam, float from, float to, int quality, bool shadows, bool makesShadow, CommonValues common, CelestialBody body, Vector3d camFixed, Turn toFixed, Vector3d shadowMidScene, float sunUp, float clear, bool first)
        {
            if (p.Cam != cam) { Detach(p); p.HavePrev = false; }
            int wide = cam.pixelWidth, high = cam.pixelHeight;
            if (wide < 64 || high < 64) return;
            // (the far camera's: a quarter of the picture's size each way, a half for "higher" (FarDiv); the near camera's clouds,
            // soft mist within 400 m, a third always unless "higher")
            int div = makesShadow ? FarDiv(quality) : quality == 2 ? 2 : 3;
            int w = Mathf.Max(16, wide / div), h = Mathf.Max(16, high / div);
            // (What is kept from frame to frame is the picture's own size where the walk is done at half of it: each frame's
            // lines of sight go through a different one of the four pixels each of the walk's dots stands for, and over
            // four frames the kept picture is as fine as the screen. See the shader, where the frames are taken together.)
            bool finer = Finer && (div == 2 || div == 4);
#if DEV
            if (Trial == 22f) finer = false;        // (for comparing: what is kept, at the walk's own size, as before)
#endif
            // (the far camera's kept pixels count their own lines (cloudskeep.glsl); the near camera's are taken together as before)
            bool counts = finer && makesShadow;
            int keptW = finer ? w * div : w, keptH = finer ? h * div : h;
            // (What the far camera keeps is held to the sky (cloudskeep.glsl): the screen's kept size and a margin of a sixteenth of its
            // height each side, 1.20 times the screen's pixels at 1920 by 1080, so that the camera may turn that far before it is held
            // anew. Its copy on the screen's own grid, laid each frame (Shown), is what everything else reads.)
            int margin = counts ? Mathf.Max(8, keptH / 16) : 0, heldW = keptW + 2 * margin, heldH = keptH + 2 * margin;
            if (p.Now == null || p.Now.width != w || p.Now.height != h || p.HistA == null || p.HistA.width != heldW || p.HistA.height != heldH || counts != (p.Shown != null))
            {
                foreach (RenderTexture t in new[] { p.Now, p.NowD, p.HistA, p.HistDA, p.HistB, p.HistDB, p.Shown, p.ShownD }) if (t != null) { t.Release(); Destroy(t); }
                p.Shown = p.ShownD = null;
                RenderTexture Make(RenderTextureFormat f, string name, int across, int up)
                {
                    var t = new RenderTexture(across, up, 0, f, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: " + p.Name + " clouds, " + name };
                    t.Create();
                    return t;
                }
                // (the distance pictures with a second channel: how much the walk's own shadow from above the clouds takes from the
                // land's light, for the shadow's pass to tint (cloudshade.glsl, aboveTint); the walk's with, b and a, where in its dot
                // each line went; the kept one with, b, how many of its own lines each kept pixel holds (cloudskeep.glsl))
                p.Now = Make(RenderTextureFormat.ARGBHalf, "this frame", w, h); p.NowD = Make(RenderTextureFormat.ARGBHalf, "this frame's distance", w, h);
                p.HistA = Make(RenderTextureFormat.ARGBHalf, "taken together", heldW, heldH); p.HistDA = Make(RenderTextureFormat.ARGBHalf, "distance taken together", heldW, heldH);
                p.HistB = Make(RenderTextureFormat.ARGBHalf, "taken together (2)", heldW, heldH); p.HistDB = Make(RenderTextureFormat.ARGBHalf, "distance taken together (2)", heldW, heldH);
                if (counts) { p.Shown = Make(RenderTextureFormat.ARGBHalf, "laid on the screen", keptW, keptH); p.ShownD = Make(RenderTextureFormat.RGHalf, "distance laid on the screen", keptW, keptH); }
                p.HavePrev = false; p.HeldOk = false;
            }
            if (p.Walk == null)
            {
                Warm();
                p.Walk = new Material(walkShader) { name = "Natural Weather: walk" };
                p.Together = new Material(keepShader) { name = "Natural Weather: over frames" };
                p.Lay = new Material(keepShader) { name = "Natural Weather: laid on the screen's grid" };
                p.Over = new Material(overShader) { name = "Natural Weather: laid on", renderQueue = LaidQueue };
                p.Shadow = new Material(shadeShader) { name = "Natural Weather: shadow laid on" };
                p.Draw = new CommandBuffer { name = "Natural Weather: clouds (" + p.Name + ")" };
                p.After = new CommandBuffer { name = "Natural Weather: after the clouds (" + p.Name + ")" };
                p.Shade = new CommandBuffer { name = "Natural Weather: clouds' shadow (" + p.Name + ")" };
                p.Early = new CommandBuffer { name = "Natural Weather: clouds walked (" + p.Name + ")" };
            }
            // (the copy this frame's sky allows: the values set on the material stay with it, and all of them are set again below)
            if (walkNow != null && p.Walk.shader != walkNow) p.Walk.shader = walkNow;
            if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; p.AskedDepth = true; }

            common(p.Walk, cam, w);
            common(p.Together, cam, w);
            common(p.Lay, cam, w);
            p.Walk.SetVector(idStage, new Vector4(2f, frame % 64, from, to));
            // (the near camera's stretch is short, and flown through: finer steps)
            p.Walk.SetVector(idHaze, new Vector4(noiseHasMips ? 1f : 0f, makesShadow ? 40f : 15f, Two ? HazeAir2.x : 1.5f, Two ? HazeAir2.y : 1f));
            p.Together.SetVector(idStage, new Vector4(3f, frame % 64, from, to));
            // (which of the four pixels this frame's lines go through: the two on one diagonal, then the two on the other; of sixteen,
            // in Bayer's order; and, w, how much finer a kept dot is than the walk's)
            // (And each round of them moved by a point of the R2 sequence within half a kept dot, since 2026-10-21: each kept dot's own line
            // went through its middle every time, and a crisp edge was a stair of whole dots, notched, where a heap stood against the sky
            // (the main chat's pictures; the owner, of blackrack's clouds, "way better"). Seen at more places within each dot over the
            // frames, as the picture's other taking-together does, the edge is smooth and the grain within less, in the lab.)
            Vector4 jitter = Vector4.zero;
            if (counts)
            {
                // (the far camera's: which kept pixel each dot's line goes through the walk works out dot by dot (clouds.glsl, lookIn),
                // each dot from its own place in Bayer's order; z and w as before, for the walk's choice of near and far shapes)
                jitter = new Vector4(0f, 0f, 1f, 1f / div);
                p.Walk.SetVector(idAim, new Vector4(1f / w, 1f / h, frame & 1023, div));
                p.Walk.SetTexture(idBlue, blueTable);
            }
            else
            {
                p.Walk.SetVector(idAim, Vector4.zero);
                if (finer)
                {
                    // (the near camera's on "higher": every dot's line through the same one of its four pixels, the two on one diagonal,
                    // then the two on the other, each round moved by a point of the R2 sequence, as since 2026-10-21)
                    int turn = frame & 3;
                    Vector2 sub = SubDot(frame >> 2) * 0.5f;
                    jitter = new Vector4(((turn == 0 || turn == 3 ? -0.25f : 0.25f) + sub.x * 0.5f) / w, ((turn == 0 || turn == 2 ? -0.25f : 0.25f) + sub.y * 0.5f) / h, 1f, 0.5f);
                }
            }
            p.Walk.SetVector("_CloudJitter", jitter);
            p.Together.SetVector("_CloudJitter", jitter);

            // where this camera was last frame (in the planet's axes), for taking the frames together
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            // (nothing kept used after a jump of the camera (Frame), a move of 5 km or more, or on another world)
            bool usable = p.HavePrev && !jumped && p.PrevBody == body.bodyName && (camFixed - p.PrevAt).magnitude < 5000.0;
            Vector3d moved = camFixed - p.PrevAt;
            // (and where each pixel's cloud was is further upwind by what the wind carried it since (Frame, windNow): left out, a heap
            // 2 km off in a wind of 15 m/s slid an eighth of a pixel a frame through what was kept, and its edges trailed)
            double since = windUt - p.PrevUt;
            Vector3d carried = moved - (Math.Abs(since) > 3600.0 ? Vector3d.zero : windNow * since);
            p.Together.SetVector(idPrevAt, new Vector4((float)carried.x, (float)carried.y, (float)carried.z, usable ? 1f : 0f));
            // (w: how much of this frame's walk a kept pixel takes: 0.07 since 2026-10-21, 0.14 until then. Set beside the same view
            // taken together over 64 frames, the game's picture was grainy all over every heap: the walk's own noise, half taken
            // away; the shading that gives each lobe its form was lost in it, and a sky of heaps looked like cotton (the owner: the
            // clouds of blackrack's "look way better"). With half as much each frame, the grain is about half; turning, the picture
            // is as soft as before (research/rendering/experiments/clouds-kwp-blackrack.md).)
            // (And after a cut, more of each frame for the first few, easing down: 0.3 / (1 + frames since / 6), never less than 0.07.
            // With 0.07 from the first frame, what a cut showed afresh took twice as long to lose its grain (the main chat, 19:0x: a
            // second against half that); with this it settles as fast as with 0.14 and goes on to the finer picture (the lab, after a
            // cut, against the picture taken over 32 frames: 3.87, 2.78, 2.37, 2.07 at 4, 8, 16, 32 frames, where 0.14 gave 3.85, 2.89,
            // 2.52, 2.41 and 0.07 4.07, 3.34, 2.70, 2.16). A cut: nothing kept that can be used, or the camera turned through more
            // than twenty degrees since the last frame (a turn of twenty degrees a second is under half a degree a frame).)
            bool cut = !usable || Vector3.Dot(toFixed(cam.transform.forward), p.PrevAhead) < 0.94f;
            p.SinceCut = cut ? 0 : Math.Min(p.SinceCut + 1, 1000);
            float take = Mathf.Max(0.07f, Mathf.Min(0.45f, 0.3f / (1f + p.SinceCut / 6f)));
            Vector3 camR = toFixed(cam.transform.right), camU = toFixed(cam.transform.up), camA = toFixed(cam.transform.forward);
            if (counts)
            {
                // (held anew after a cut, when a corner of the screen comes within two pixels of its edge, or when the field of view
                // changes; read this frame from what it was held to last frame)
                bool anew = !usable || !p.HeldOk || heldToScreen || Mathf.Abs(cam.fieldOfView - p.HeldFov) > 0.002f * p.HeldFov;
                for (int c = 0; c < 4 && !anew; c++)
                {
                    Vector3 v = camA + camR * (tanX * (c % 2 == 0 ? -1f : 1f)) + camU * (tanY * (c < 2 ? -1f : 1f));
                    float ahead = Vector3.Dot(v, p.HeldAhead);
                    anew = ahead <= 0f || Mathf.Abs(Vector3.Dot(v, p.HeldRight) / ahead) > 1f - 4f / heldW || Mathf.Abs(Vector3.Dot(v, p.HeldUp) / ahead) > 1f - 4f / heldH;
                }
                p.Together.SetVector(idPrevRight, p.HeldRight); p.Together.SetVector(idPrevUp, p.HeldUp); p.Together.SetVector(idPrevAhead, p.HeldAhead);
                if (anew)
                {
                    p.HeldRight = camR / (tanX * heldW / keptW); p.HeldUp = camU / (tanY * heldH / keptH); p.HeldAhead = camA;
                    p.HeldFov = cam.fieldOfView; p.HeldOk = true;
                }
                foreach (Material m in new[] { p.Together, p.Lay })
                {
                    m.SetVector(idHeldRight, p.HeldRight); m.SetVector(idHeldUp, p.HeldUp); m.SetVector(idHeldAhead, p.HeldAhead);
                    m.SetVector(idSeeRight, camR / tanX); m.SetVector(idSeeUp, camU / tanY); m.SetVector(idSeeAhead, camA);
                }
                p.Together.SetVector(idHeld, new Vector4(keptW, keptH, div, anew ? 1f : 0f));
                p.Lay.SetVector(idHeld, new Vector4(keptW, keptH, div, 2f));
            }
            else
            {
                p.Together.SetVector(idPrevRight, new Vector4(p.PrevRight.x, p.PrevRight.y, p.PrevRight.z, take));
                p.Together.SetVector(idPrevUp, p.PrevUp);
                p.Together.SetVector(idPrevAhead, p.PrevAhead);
            }
            // (how the far camera's kept pixels take this frame's lines (cloudskeep.glsl): x, at most 6.5 of their own lines' worth read
            // at the middles of last frame's pixels (a still view's grain as with the 0.07 of 2026-10-21); y, 4 read between them, which
            // softens a little each time; z, the walk's lines spread counting a quarter of one where nothing kept can be used; w, what is
            // kept held within a quarter of the lines' spread beyond them. Fewer lines held as time goes faster and the clouds boil: from
            // warp 4, as 4 over the rate. Nought for the near camera's.)
            float warpShare = Mathf.Min(1f, 4f / Mathf.Max(1f, TimeWarp.CurrentRate));
            Vector4 keep = counts ? new Vector4(Mathf.Max(1f, 6.5f * warpShare * keepSaid.x), Mathf.Max(1f, 4f * warpShare * keepSaid.y), 0.25f * keepSaid.z, 0.25f * keepSaid.w) : Vector4.zero;
            p.Together.SetVector(idKeep, keep);
            p.Lay.SetVector(idKeep, keep);      // (for the count shown in development builds' trial 26 only)
            p.PrevAt = camFixed;
            p.PrevUt = windUt;
            p.PrevRight = toFixed(cam.transform.right) / tanX;
            p.PrevUp = toFixed(cam.transform.up) / tanY;
            p.PrevAhead = toFixed(cam.transform.forward);
            p.PrevBody = body.bodyName;
            p.HavePrev = true;

            RenderTexture histIn = p.Flip ? p.HistB : p.HistA, histDIn = p.Flip ? p.HistDB : p.HistDA;
            RenderTexture histOut = p.Flip ? p.HistA : p.HistB, histDOut = p.Flip ? p.HistDA : p.HistDB;
            p.Flip = !p.Flip;
            // (what everything else reads: the far camera's kept picture as laid on the screen's grid; the near camera's as kept)
            RenderTexture shown = counts ? p.Shown : histOut, shownD = counts ? p.ShownD : histDOut;
            if (counts) { p.Lay.SetTexture(idHist, histOut); p.Lay.SetTexture(idHistD, histDOut); }
            // (the far camera's clouds, as they will be laid this frame, and how far off they are, for Keystone's pass that writes
            // the motion of what lies beyond the near camera: where they hide most of what is behind them, a smoothing between
            // frames follows them, not the ground under them)
            if (makesShadow) TellFarMotion(shown, shownD);
            // (Scatterer's sun flare, its rays and its ghosts, dimmed by how much of the sun these clouds let through: its shader
            // multiplies them by the alpha of the global texture `scattererReconstructedCloud` where the sun stands in the picture,
            // which Scatterer sets white once, at its flare's making, for a mod of raymarched clouds to hand over. The far camera's
            // clouds as they will be laid this frame are that (their alpha is how much of what is behind them shows through), made
            // in the far camera's drawing before Scatterer's near camera draws its flare. Not handed over, the flare shone through
            // the thickest cloud: the sun's disc and rays in the middle of a dark heap at dusk (Natural Ocean's evening picture,
            // 2026-10-18; research/rendering/experiments/sun-flare-through-cloud.md). Clouds within the near camera's 400 m on
            // OpenGL are not in it: the flare goes on through those, as before.)
            if (makesShadow) Shader.SetGlobalTexture(idFlareCloud, shown);
            p.Together.SetTexture(idNow, p.Now); p.Together.SetTexture(idNowD, p.NowD);
            p.Together.SetTexture(idHist, histIn); p.Together.SetTexture(idHistD, histDIn);

            // laid on the picture
            bool depth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
            Vector3 r = cam.transform.right * tanX, u = cam.transform.up * tanY, f = cam.transform.forward;
            Material over = p.Over;
            // (The sheet the clouds are laid on with stands as far from the camera as the nearest cloud can be, and whatever
            // is drawn nearer than that hides it. What is in front of a cloud the clouds know from the camera's picture of
            // distances, and not everything is in that: the sea of Natural Ocean is not. On Direct3D, where one camera draws
            // the whole scene, the clouds were laid over that sea's nearest waves when the camera was low among them, and over
            // the underside of its surface when the camera was under water (seen 2026-10-11). On OpenGL the near camera
            // draws its stretch of sea after the far camera's clouds, and covered them. The sheet's depth as the graphics
            // card counts it, which differs from system to system: taken from the camera's own projection.)
            float sheetOff = Mathf.Clamp(clear * 0.9f, cam.nearClipPlane * 1.02f, cam.farClipPlane * 0.98f);
            Matrix4x4 lens = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
            over.SetVector(idSheet, new Vector4(1f, 1f, (lens.m22 * -sheetOff + lens.m23) / (lens.m32 * -sheetOff + lens.m33), 1f));
            over.SetVector(idRight, new Vector4(r.x, r.y, r.z, depth ? 1f : 0f));
            over.SetVector(idUp, new Vector4(u.x, u.y, u.z, 0f));
            over.SetVector(idAhead, new Vector4(f.x, f.y, f.z, SystemInfo.usesReversedZBuffer ? 1f : 0f));
            // (The weather's veil, Natural Light's: laid on Camera 00 before the clouds (on OpenGL's near camera before anything
            // see-through; on Direct3D's one camera in the clouds' own turn, just before them), and on Camera 01 after everything.
            // So the clouds laid on Camera 00, the only camera on Direct3D, take it themselves, by how far off each is
            // (cloudsover.glsl); Camera 01's are veiled with everything else. Until 2026-10-25 none did: in fog on the pad the
            // ground and the sky were white and the heaps over them crisp and blue.)
            bool veils = Names.Is(cam, "Camera 00");
#if DEV
            if (veilAsBefore) veils = false;
#endif
            over.SetVector(idOver, new Vector4(1f / 2.2f, 1f, 1f, (veils ? 1f : 0f) + (EdgesAsBefore ? 2f : 0f) + (FormTwo && !TopsAsBefore ? 4f : 0f) + (FormTwo && HueKept ? 8f : 0f)));
            Vector3 upHere = ((Vector3)((Vector3d)cam.transform.position - body.position)).normalized;
            over.SetVector(idVeilUp, new Vector4(upHere.x, upHere.y, upHere.z, (float)body.Radius));
            over.SetTexture(idHist, shown); over.SetTexture(idHistD, shownD);

            p.Draw.Clear();
            // (The clouds are laid on in their turn among the see-through things (LaidQueue, by the renderer `laying`): after
            // Scatterer's sky, which is one of them and would be drawn over them, and before smoke, spray and flames, which
            // would otherwise have the clouds drawn over them however near: the user saw "the clouds showing above the
            // smoke" of a crash some hundreds of metres off. What lies behind a cloud is then drawn over it; so the picture of
            // the clouds and how far off each is are handed to every shader for this camera's drawing, and Volumetric
            // Explosions dims its smoke by the cloud in front of it (its layers.glsl). Taken back after everything.)
            bool late = LaidLate();
            if (late) { p.After.Clear(); p.After.DrawMesh(sheet, Matrix4x4.identity, over, 0, 0); }
            else p.After.Clear();
            p.Draw.SetGlobalTexture(idOthersPic, shown);
            p.Draw.SetGlobalTexture(idOthersDist, shownD);
            // (none laid on under the sea: nor is the smoke to be dimmed by them)
            p.Draw.SetGlobalVector(idOthers, new Vector4(late || underSea ? 0f : 1f, clear, 0f, 0f));
            p.After.SetGlobalVector(idOthers, Vector4.zero);
            InFront?.Invoke(p.After, cam);

            // ---- what is drawn into pictures of its own, once the camera's depth picture is made and before its own picture is
            // begun (see Pass.Early): the far camera's maps, then the walk, which reads them, and its taking together
            p.Early.Clear();
            // (the low clouds' map smoothed, first: everything after reads it)
            if (makesShadow && mapFine != null && mapFineWalk != null)
            {
                p.Early.SetRenderTarget(mapFine);
                p.Early.DrawMesh(sheet, Matrix4x4.identity, mapFineWalk, 0, 0);
            }
            if (makesShadow && heapsOn && heapsWalk != null)
            {
                p.Early.SetRenderTarget(new RenderTargetIdentifier[] { heapsMap, heapsMapB }, heapsMap);
                p.Early.DrawMesh(sheet, Matrix4x4.identity, heapsWalk, 0, 0);
            }
            if (shadows && shadowMap != null && makesShadow)
            {
                p.Early.SetRenderTarget(new RenderTargetIdentifier[] { shadowMap, shadowMapB }, shadowMap);
                if (shadowStrip >= 0) p.Early.EnableScissorRect(new Rect(0f, shadowStrip * (Shadow / 8), Shadow, Shadow / 8));
                p.Early.DrawMesh(sheet, Matrix4x4.identity, shadowWalk, 0, 0);
                if (shadowStrip >= 0) p.Early.DisableScissorRect();
            }
            // (the light field: this frame's band of rows of the one being made, before any walk reads the one made whole)
            if (makesShadow && LightField && !lightHigh && lightWalk != null && lightMaking != null && lightBand >= 0 && lightBand < LightRound)
            {
                int rows = 2 * (LightHeights / 4 * LightCells * LightLevels), band = (rows + LightRound - 1) / LightRound;
                p.Early.SetRenderTarget(lightMaking);
                p.Early.EnableScissorRect(new Rect(0f, lightBand * band, 4 * LightCells, band));
                p.Early.DrawMesh(sheet, Matrix4x4.identity, lightWalk, 0, 0);
                p.Early.DisableScissorRect();
                lightBand++;
            }
            p.Early.SetRenderTarget(new RenderTargetIdentifier[] { p.Now, p.NowD }, p.Now);
            p.Early.DrawMesh(sheet, Matrix4x4.identity, p.Walk, 0, 0);
            p.Early.SetRenderTarget(new RenderTargetIdentifier[] { histOut, histDOut }, histOut);
            p.Early.DrawMesh(sheet, Matrix4x4.identity, p.Together, 0, 0);
            if (counts)
            {
                p.Early.SetRenderTarget(new RenderTargetIdentifier[] { p.Shown, p.ShownD }, p.Shown);
                p.Early.DrawMesh(sheet, Matrix4x4.identity, p.Lay, 0, 0);
            }
            // (and the far camera's clouds made small, for their own light over the sky: see Meter)
            if (makesShadow)
            {
                if (meterPic == null || !meterPic.IsCreated())
                {
                    meterPic = new RenderTexture(MeterW, MeterH, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Natural Weather: the clouds' light, small" };
                    meterPic.Create();
                }
                p.Early.Blit(shown, meterPic);
                meterRight = r; meterUp = u; meterAhead = f; meterSky = upHere; meterMade = frame;
            }
            // (and the camera's own picture to be drawn into after this, as before it)
            p.Early.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

            // the shadow, before anything see-through is drawn
            p.Shade.Clear();
            if (shadows && shadowMap != null) LayShadow(p.Shadow, p.Shade, cam, body, shadowMidScene, sunUp, makesShadow, shownD);
            else if (makesShadow) Shader.SetGlobalVector(idWeatherShadowSun, Vector4.zero);
            if (!p.On)
            {
                // (the shadow straight after the camera's solid things: Scatterer copies the picture after that and lays its haze on
                // from the copy before anything see-through is drawn, so a shadow laid on later is wiped out by it. Its sea, drawn
                // after the copy, takes no shadow so.)
                p.EarlyWhen = EarlyFor(cam);
                cam.AddCommandBuffer(p.EarlyWhen, p.Early);
                cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, p.Shade);
                p.When = CameraEvent.BeforeForwardAlpha;
                cam.AddCommandBuffer(p.When, p.Draw);
                cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, p.After);
                p.Cam = cam;
                p.On = true;
            }
            else if (p.EarlyWhen != EarlyFor(cam))
            {
                // (the camera switched between forward and deferred shading since: see EarlyFor)
                cam.RemoveCommandBuffer(p.EarlyWhen, p.Early);
                p.EarlyWhen = EarlyFor(cam);
                cam.AddCommandBuffer(p.EarlyWhen, p.Early);
            }
        }


        /// <summary>
        /// Where among the see-through things the clouds are laid on: after Scatterer's sky (2998) and everything else that
        /// stands behind any cloud, before smoke and flames (3000 and on).
        /// </summary>
        const int LaidQueue = 2999;
        GameObject laying;
        Material layNothing;
        bool underSea;

        /// <summary>
        /// The one renderer that lays the clouds on in their turn: a sheet over the whole screen, whose drawing ignores where
        /// it stands (it is only kept where the camera is, so that no camera leaves it out). Just before each camera draws
        /// it, the material of that camera's clouds is put on it, or one that draws nothing (CloudsLaid).
        /// </summary>
        void Laying(Camera farCam, Camera nearCam)
        {
            if (laying == null)
            {
                laying = new GameObject("Natural Weather: clouds laid on");
                DontDestroyOnLoad(laying);
                laying.AddComponent<MeshFilter>().sharedMesh = sheet;
                var r = laying.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                // (kept at the camera, so moved every frame: it has no motion of its own for the game's smoothing to take)
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                layNothing = new Material(overShader) { name = "Natural Weather: laid on (nothing)", renderQueue = LaidQueue };
                layNothing.SetVector(idSheet, new Vector4(0f, 0f, 0f, 1f));
                r.sharedMaterial = layNothing;
                laying.AddComponent<CloudsLaid>().Owner = this;
            }
            // (on a layer both cameras draw)
            int both = farCam.cullingMask & (nearCam != null ? nearCam.cullingMask : ~0);
            int layer = (both & (1 << 15)) != 0 ? 15 : 0;
            if ((both & (1 << layer)) == 0) for (int l = 0; l < 32; l++) if ((both & (1 << l)) != 0) { layer = l; break; }
            laying.layer = layer;
            laying.transform.position = farCam.transform.position;
        }

        /// <summary>The material the sheet is drawn with for a camera just about to draw it (CloudsLaid).</summary>
        internal Material LaidFor(Camera cam)
        {
            if (cam == null || LaidLate() || underSea) return layNothing;
            if (far.On && cam == far.Cam) return far.Over;
            if (near.On && cam == near.Cam) return near.Over;
            return layNothing;
        }

        /// <summary>
        /// The share of a sunlit ground's light that the ground under this weather throws up: the sun on it, as it comes through the gaps
        /// and the thin places on the average round about, and of what the clouds take from the sun, what they let down as light from the
        /// whole sky, by the same two-stream rule as the weather's shade (Provider.ShadeAt). About 0.06 under a thunderstorm, a fifth
        /// under an overcast, seven tenths under fair-weather heaps.
        /// </summary>
        static float GroundShare(WeatherHere here, float sunUp)
        {
            float tauSky = 0f, notCovered = 1f, sun = 1f, up = Mathf.Max(0f, sunUp);
            // (layer by layer, not from an array of the three: this is every frame)
            OverGround(here.Low, up, ref tauSky, ref notCovered, ref sun);
            OverGround(here.Middle, up, ref tauSky, ref notCovered, ref sun);
            OverGround(here.High, up, ref tauSky, ref notCovered, ref sun);
            float spread = 1f + 3f * up;
            float sky = notCovered + (1f - notCovered) * spread / (1f + 0.15f * tauSky);
            return Mathf.Clamp01(sun + (1f - sun) * Mathf.Clamp01(sky / spread));
        }

        /// <summary>How much of the sky a layer covers as cloud overhead (fog and dust are the air's, the veil's, not a deck).</summary>
        static float LayerCover(CloudLayer l) => l.Kind == CloudKind.None || l.Kind == CloudKind.Fog || l.Kind == CloudKind.Dust ? 0f : Mathf.Clamp01(l.Cover);

        static void OverGround(CloudLayer l, float up, ref float tauSky, ref float notCovered, ref float sun)
        {
            if (l.Kind == CloudKind.None || l.Cover < 0.01f) return;
            float tau = Provider.Tau(l), cover = Mathf.Clamp01(l.Cover);
            tauSky += cover * tau;
            notCovered *= 1f - cover;
            sun *= 1f - cover + cover * Mathf.Exp(-0.75f * tau / Mathf.Max(up, 0.15f));
        }

        /// <summary>(Development build, for setting one against the other: the clouds laid on after everything, as until Natural Weather 0.4.0.)</summary>
        static bool LaidLate()
        {
#if DEV
            return HeldFirst > 0.5f;
#else
            return false;
#endif
        }

        // ---------------------------------------------------------------- for tests (development build)
#if DEV
        /// <summary>(Development build: hold one kind of weather everywhere in the stand-in, which the clouds then take in place of any weather mod's: clear, fair, cumulus, towering, storm, layer, middle, overcast, cirrus; or "as it comes".)</summary>
        public static string Try(string kind)
        {
            CloudStandIn.Held = kind == "as it comes" || kind == "" ? null : kind;
            if (instance != null) instance.map.Forget();
            return "weather held: " + (CloudStandIn.Held ?? "as it comes") + (Keystone.Weather.There && CloudStandIn.Held != null ? " (in place of " + Keystone.Weather.Provider.Name + "'s, for the clouds only)" : "") + " (the map is made again within two seconds)";
        }

        /// <summary>(Development build: a flash of lightning in the nearest storm, or 2 km in front of the camera at 3 km up.)</summary>
        public static string Flash()
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = Find("Camera 01") ?? Find("Camera 00");
            if (body == null || cam == null) return "no camera";
            Vector3d at = cam.transform.position + cam.transform.forward * 4000f;
            body.GetLatLonAlt(at, out double lat, out double lon, out double _);
            var f = new WeatherFlash { Body = body, Latitude = lat, Longitude = lon, Bottom = 1000f, Top = 5000f, Ground = false, Time = Planetarium.GetUniversalTime(), Brightness = 2f, Strokes = 4, Seed = UnityEngine.Random.Range(0, 99999) };
            Keystone.Weather.Flash(f);
            return "flash at " + lat.ToString("F3") + ", " + lon.ToString("F3");
        }

        static float heldFlash;
        static Vector3d heldFlashAt;
        /// <summary>(Development build: a flash held lit, at so much brightness (0: none), 4 km in front of the camera and 3 km up, for pictures: a real one is gone in a tenth of a second.)</summary>
        public static string HoldFlash(string brightness)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = Find("Camera 01") ?? Find("Camera 00");
            if (body == null || cam == null) return "no camera";
            float.TryParse(brightness, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out heldFlash);
            Vector3d rel = (Vector3d)(cam.transform.position + cam.transform.forward * 4000f) - body.position;
            heldFlashAt = rel.normalized * (body.Radius + 3000.0);
            return heldFlash > 0f ? "flash held at " + heldFlash : "no flash held";
        }

        static bool heldOff;
        /// <summary>(Development build: the clouds off, or on again, without touching the player's settings: for timing.)</summary>
        public static string Switch() { heldOff = !heldOff; return heldOff ? "clouds held off" : "clouds on"; }
        static bool groundAsBefore;
        static float GroundShareSaid = 1f;
        /// <summary>(Development build: the share of a sunlit ground's light the ground under the weather throws up on the clouds.)</summary>
        public static string GroundLight() => "the ground's light on the clouds: " + GroundShareSaid.ToString("F3") + " of a sunlit ground's" + (groundAsBefore ? " (held as before)" : "");
        /// <summary>(Development build: the light the ground throws up on the clouds as a sunlit ground's whatever the weather over it, as
        /// before 2026-10-25, and back: for pictures of before and after.)</summary>
        public static string GroundAsBefore() { groundAsBefore = !groundAsBefore; return groundAsBefore ? "the ground's light on the clouds as before 2026-10-25" : "the ground's light on the clouds under its weather"; }
        static bool veilAsBefore, underSeaAsBefore;
        /// <summary>(Development build: the clouds laid on Camera 00 without the weather's veil, as before 2026-10-25, and back.)</summary>
        public static string VeilAsBefore() { veilAsBefore = !veilAsBefore; return veilAsBefore ? "the clouds unveiled, as before 2026-10-25" : "the clouds veiled by the weather in front of them"; }
        /// <summary>(Development build: the clouds laid on with the camera under the sea, as before 2026-10-25, and back.)</summary>
        public static string UnderSeaAsBefore() { underSeaAsBefore = !underSeaAsBefore; return underSeaAsBefore ? "the clouds laid on under the sea, as before 2026-10-25" : "no clouds laid on under the sea"; }
        /// <summary>(Development build: the clouds' own light over the sky as last read back (see Meter), and what the read saw.)</summary>
        public static string Light()
        {
            Color c = deckSaid;
            return "the clouds' light over the sky " + c.r.ToString("F3") + " " + c.g.ToString("F3") + " " + c.b.ToString("F3") + " (as light), covering " + deckCover.ToString("F2") +
                   " of it, " + (Time.unscaledTime - deckAt).ToString("F1") + " s ago" + (deckUnsaid ? " (not said: a Keystone older than the deck's light)" : "") + "; " + (instance != null ? instance.meterSaid + (instance.meterRead != null ? " (" + instance.meterRead.Way + ")" : "") : "no clouds") + "; under the sea " + (instance != null && instance.underSea);
        }
        /// <summary>(Development build: both passes' pictures and buffers let go, to be made afresh in the next frame as at a flight's
        /// start: with the cameras drawn deferred (CloudsProbe.Deferred), what a game with the Deferred mod has from the first frame.)</summary>
        public static string Afresh()
        {
            if (instance == null) return "no clouds";
            foreach (Pass p in new[] { instance.far, instance.near }) { Detach(p); Free(p); }
            return "the clouds' pictures made afresh";
        }
        /// <summary>(Development build: nothing kept from frame to frame used at the next frame, as after a jump; for comparing. The
        /// jump test leaves it alone: it is there for the rule that finds a jump by itself.)</summary>
        public static string Cut() { cutAsked = true; return "a cut at the next frame"; }
        /// <summary>(Development build: how many frames the jump test (Frame) has found a jump in since the game began, and the game's
        /// time of the last; for checking that flying, orbits and changes of warp are not taken for jumps.)</summary>
        public static int JumpsSeen;
        public static double JumpLast = double.NaN;
        public static string Jumps() { return "jumps seen: " + JumpsSeen + ", the last at " + JumpLast.ToString("F1") + " s (now " + Planetarium.GetUniversalTime().ToString("F1") + " s)"; }
        /// <summary>(Development build: the far camera's keeping as multiples of the release's, "still move spread widen" (1 1 1 1 as
        /// released); "screen" holds what is kept to the screen, held anew every frame (the count alone, for comparing), "sky" to the
        /// sky (from 2026-10-26's step 8).)</summary>
        public static string Keep(string what)
        {
            if (what == "screen" || what == "sky") heldToScreen = what == "screen";
            else
            {
                string[] said = (what ?? "").Split(' ');
                for (int i = 0; i < 4 && i < said.Length; i++)
                    if (float.TryParse(said[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) keepSaid[i] = v;
            }
            return "kept: " + keepSaid.ToString("F2") + (heldToScreen ? ", held to the screen" : ", held to the sky");
        }
        /// <summary>(Development build: the shadows' sky tint held off ("off") or as worked out ("on"), and what it is.)</summary>
        public static bool TestNoTint;
        public static string TintSaid = "not drawn yet";
        /// <summary>(Development build: how many readings the light field takes beyond its first stretch towards the sun: 8, as until 2026-10-19, or 16.)</summary>
        public static float TestFieldFar = 16f;
        public static string FieldFar(string arg) { if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n) && n >= 1f) TestFieldFar = n; return "the light field's far readings: " + TestFieldFar; }
        public static string Tint(string arg) { if (arg == "off") TestNoTint = true; else if (arg == "on") TestNoTint = false; return TintSaid; }
        /// <summary>(Development build: where the brightest moon stands and how lit, as the clouds take it for its ring.)</summary>
        public static string MoonSaid = "";
        public static string Moon() => MoonSaid;

        /// <summary>(Development build: the clouds laid on before the see-through things ("1"), after them ("0"), or as it comes ("-1").)</summary>
        public static string First(string v) { HeldFirst = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return HeldFirst > 0.5f ? "laid on after everything, as until 0.4.0" : "laid on in their turn among the see-through things (after Scatterer's sky, before smoke)"; }
        static float HeldFirst = -1f;

        public static string Stats() => Report + (CloudsWake.Said.Length > 0 ? "; " + CloudsWake.Said : "") + "; laid on no nearer than " + SheetClear.ToString("F0") + " m, " + (LaidFirst ? "before" : "after") + " the see-through things; noise " + (shape != null ? shape.width + (noiseHasMips ? " with mips" : " no mips") : "none") + "; the map took " + (instance != null ? instance.map.Took.ToString("F2") : "?") + " s; the heaps' map " + (instance != null && instance.heapsOn ? HeapsDots + " dots, " + (HeapsAcross / 1000f).ToString("F1") + " km" : "off") + "; " + why
            + (instance != null ? "; at " + utWas.ToString("F1") + " s the wind has carried the noise " + (driftWas.x / 1000.0).ToString("F3") + ", " + (driftWas.y / 1000.0).ToString("F3") + ", " + (driftWas.z / 1000.0).ToString("F3") + " km (the wind " + windWas.x.ToString("F2") + ", " + windWas.y.ToString("F2") + ", " + windWas.z.ToString("F2") + " m/s)" + (heldStill ? ", held still" : "")
                + "; the camera's longitude " + lonGame.ToString("F2") + " by the game, " + lonOwn.ToString("F2") + " by the clouds' axes (apart by " + Apart(lonOwn, lonGame).ToString("F2") + "; the scene stands turned " + turnWas.ToString("F2") + " against the planet)" : "");

        /// <summary>(Development build: what the shader is handed of the heaps made beforehand.)</summary>
        public static string Field()
        {
            if (field == null) return "no field of heaps: " + fieldWhy;
            var b = new System.Text.StringBuilder();
            b.Append(field.width).Append(" by ").Append(field.height).Append(" by ").Append(field.depth).Append(", ").Append(field.mipmapCount).Append(" sizes, ").Append(field.format).Append(", ").Append(field.graphicsFormat)
             .Append("; side ").Append(fieldSide).Append(" m, far ").Append(fieldFar).Append(" m; in use: ").Append(CloudsPage.Made == null || CloudsPage.Made).Append("; levels");
            foreach (float l in fieldLevels) b.Append(' ').Append(l.ToString("F3"));
            if (instance != null) b.Append("; carried ").Append(instance.fieldDrift.x.ToString("F0")).Append(" m east, ").Append(instance.fieldDrift.y.ToString("F0")).Append(" north");
            return b.ToString();
        }

        static bool heldStill;
        static Vector3d driftWas, windWas;
        static double utWas, lonGame, lonOwn, turnWas;
        static double Apart(double a, double b) { double d = (a - b) % 360.0; if (d > 180.0) d -= 360.0; if (d < -180.0) d += 360.0; return d; }
        /// <summary>(Development build: "on" holds the clouds' noise where it is, not carried by the wind, for pictures of before and after within one flight; "off" lets the wind carry it again. It does not make two loads the same sky: see the longitudes in Stats.)</summary>
        public static string Still(string arg) { heldStill = arg != null && arg.Trim() == "on"; return heldStill ? "the clouds' noise held still" : "the clouds' noise carried by the wind"; }

        static Clouds instance;
        void Start() { instance = this; }
#endif
    }
}
