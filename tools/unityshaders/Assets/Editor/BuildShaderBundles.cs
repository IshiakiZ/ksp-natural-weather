using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds this project's shaders into asset bundles for each kind of computer the game runs on: the four of
/// Volumetric Explosions into one ("shaders"), Keystone's lens into another ("lens"), Natural Light's two into
/// a third ("light"), Natural Weather's three clouds' shaders into a fourth ("clouds"). Run from the command
/// line (see tools/unityshaders/README.md), or from the menu in the editor. The game is Unity 2019.4.18f1 and
/// loads only bundles made by that version.
/// </summary>
public static class BuildShaderBundles
{
    [MenuItem("KSP mods/Build shader bundles")]
    public static void All()
    {
        var every = new[]
        {
            new AssetBundleBuild
            {
                assetBundleName = "shaders",
                assetNames = new[] { "Assets/VolumetricExplosions/Volume.shader", "Assets/VolumetricExplosions/Enlarge.shader", "Assets/VolumetricExplosions/Layers.shader", "Assets/VolumetricExplosions/Mark.shader",
                                     "Assets/VolumetricExplosions/Shock.shader", "Assets/VolumetricExplosions/Shadow.shader" },
            },
            new AssetBundleBuild { assetBundleName = "lens", assetNames = new[] { "Assets/Keystone/Lens.shader" } },
            new AssetBundleBuild { assetBundleName = "light", assetNames = new[] { "Assets/NaturalLight/Shafts.shader", "Assets/NaturalLight/Clean.shader", "Assets/NaturalLight/Veil.shader" } },
            new AssetBundleBuild { assetBundleName = "clouds", assetNames = new[] { "Assets/NaturalWeather/Clouds.shader", "Assets/NaturalWeather/CloudsOver.shader", "Assets/NaturalWeather/CloudShade.shader" } },
        };
        // (a repository of one mod has that mod's shaders and not the others': a bundle none of whose shaders are here is passed over)
        var here = new List<AssetBundleBuild>();
        foreach (AssetBundleBuild build in every)
        {
            bool any = false;
            foreach (string path in build.assetNames) if (File.Exists(path)) any = true;
            if (any) here.Add(build); else Debug.Log("BUILD: left out " + build.assetBundleName + ": its shaders are not in this folder");
        }
        AssetBundleBuild[] builds = here.ToArray();
        // (every one of them must have compiled, or the bundle would hold a shader that draws nothing and the game would use it)
        bool sound = true;
        foreach (AssetBundleBuild build in builds)
            foreach (string path in build.assetNames)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) { Debug.LogError("BUILD: " + path + " is missing"); sound = false; continue; }
                if (ShaderUtil.ShaderHasError(shader))
                {
                    sound = false;
                    foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                        Debug.LogError("BUILD: " + path + " line " + message.line + " (" + message.platform + "): " + message.message);
                }
                else Debug.Log("BUILD: " + path + " compiles");
            }
        if (!sound) { Debug.LogError("BUILD: stopped, a shader does not compile"); EditorApplication.Exit(1); return; }
        Build(builds, BuildTarget.StandaloneWindows64, "windows");
        Build(builds, BuildTarget.StandaloneOSX, "mac");
        Build(builds, BuildTarget.StandaloneLinux64, "linux");
    }

    static void Build(AssetBundleBuild[] builds, BuildTarget target, string name)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            Debug.Log("BUILD: skipped " + name + ": this editor has no build support for it installed");
            return;
        }
        string folder = "Bundles/" + name;
        Directory.CreateDirectory(folder);
        BuildPipeline.BuildAssetBundles(folder, builds, BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
        foreach (AssetBundleBuild build in builds)
        {
            string made = folder + "/" + build.assetBundleName, wanted = "Bundles/" + build.assetBundleName + "-" + name + ".bundle";
            if (File.Exists(made)) { File.Copy(made, wanted, true); Debug.Log("BUILD: wrote " + wanted + " (" + new FileInfo(wanted).Length + " bytes)"); }
            else Debug.LogError("BUILD: no " + build.assetBundleName + " bundle was made for " + name);
        }
    }
}
