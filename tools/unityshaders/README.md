# Building the shaders in the Unity editor

**Since 2026-10-09 the Direct3D bundles are made without the editor**, on Windows, by `windows.py` in this
folder (route A of [WITHOUT-UNITY.md](WITHOUT-UNITY.md)): `python3 tools/unityshaders/windows.py "<KSP
folder>"` turns every shader into its Direct3D 11 bundle in one go, with Windows' own compiler. Those bundles
were tried in the game on Windows. What follows is the editor's way, kept for if it ever runs again; the
`.shader` files `port.py` writes come from the same HLSL that `windows.py` compiles.

The seven shaders of these mods (the explosions' smoke, the enlarging of it, the burn mark and the shock
front, Keystone's lens, and Natural Light's shafts of sunlight and its blacking out of pixels that are not
numbers) are OpenGL text, packed into bundles by hand (`tools/shaderpack`). That is what
the Mac and Linux versions of the game run. The Windows version runs on Direct3D 11, which wants compiled
bytecode that only the Unity editor can make. This folder is a Unity project that makes it.

## State (2026-10-08): written, never compiled

* `port.py` turns each GLSL file into a `.shader` file of HLSL by rule (`Assets/VolumetricExplosions/*.shader`,
  `Assets/Keystone/Lens.shader`, `Assets/NaturalLight/*.shader`). Edit the GLSL and run it again; do not edit the `.shader` files. The few
  lines that depend on which way up Direct3D keeps a picture are replaced one by one (`SPECIAL` in that
  file, which says why).
* `Assets/Editor/BuildShaderBundles.cs` builds three bundles for each system: `shaders-<system>.bundle` (the
  explosions' four), `lens-<system>.bundle` and `light-<system>.bundle` (Natural Light's two).
* The mods look for them: Volumetric Explosions for `PluginData/shaders-windows.bundle` (all four shaders by
  name) wherever the game is not on OpenGL, Keystone for `PluginData/lens-windows.bundle`, Natural Light for
  `PluginData/light-windows.bundle` (both shaders by name). With
  `built = true` in the explosions' `settings.cfg` the built bundle is used on OpenGL too
  (`shaders-mac.bundle`), which is how the HLSL can be checked against the GLSL on a Mac.
* **The editor cannot be made to run.** Unity 2019.4.18f1 (the game's version, unpacked at
  `~/.ksp-ai-bridge/unity/2019.4.18f1` with Windows build support) starts in batch mode, but cannot get a
  licence, and this is on Unity's side:
  * its own licensing helper (1.6.0, of January 2021) cannot read the licence file Unity now issues
    (`The 'IssueDate' attribute is not declared`), and a Personal licence cannot be had in the older form
    (`Unity_lic.ulf`) at all;
  * it will not use the Unity Hub's helper, nor Unity's current one (1.18.3) put in place of its own as
    [Unity's article](https://support.unity.com/hc/en-us/articles/52670753024404) describes for newer
    editors: it checks who signed the helper, and Unity has changed who signs since. Tried on 2026-10-08
    and undone.
  * [Others have the same](https://discussions.unity.com/t/unity-hub-doesnt-run-2019/1737741) (September
    2026, unresolved; Unity's staff had asked their licensing team).

  So: wait for Unity, or make the Direct3D bytecode without Unity (an open-source HLSL compiler and a packer
  like `tools/shaderpack`'s, which nobody has written).

In a repository of one mod (they are made from a workspace that holds all of them) only that mod's shaders
are here; `port.py` and the build pass over the others.

## Building, once the editor runs

    python3 tools/unityshaders/port.py
    ~/.ksp-ai-bridge/unity/2019.4.18f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit \
        -projectPath tools/unityshaders -executeMethod BuildShaderBundles.All -logFile -

That writes `tools/unityshaders/Bundles/shaders-windows.bundle`, `lens-windows.bundle` and `light-windows.bundle` (and `-mac`,
`-linux` where the editor has those build supports). Expect compile errors the first time: nothing here
has been through a compiler. Then, before anything goes to Windows, put `shaders-mac.bundle` in the game's
`GameData/VolumetricExplosions/PluginData`, set `built = true`, and compare the picture with the hand-packed
shaders'.

The editor needs `/Library/Application Support/Unity` to exist and be writable (it does, since 2026-10-08):
without it, even a batch-mode start puts up a Mac password prompt.
