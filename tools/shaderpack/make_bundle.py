#!/usr/bin/env python3
"""Pack one of this project's shaders into a Unity asset bundle, without the Unity editor.

    python3 tools/shaderpack/make_bundle.py <KSP folder> <output file> [volume|layers|motion|enlarge|shadow|mark|shock|lens|shafts|clean|veil|clouds|cloudsover|cloudshade|wet]

Volumetric Explosions': "volume" (the default) is the smoke, "layers" what puts every patch's smoke on the screen in depth order
(since 0.5.0; "enlarge" did it a patch at a time before, and is kept for older copies of the mod), "shadow" the shadow it throws
on what is under it, "mark" the burn mark thrown onto the ground, "shock" the shock front
that bends the picture behind it. Keystone's: "lens", what a lens, a shutter and a film do to a picture.
Natural Light's: "shafts", shafts of sunlight; "clean", which blacks out the pixels that are not numbers; "veil", the weather
(rain, snow, fog, dust) in the air between the camera and the distance.

A KSP mod's own shader normally has to be compiled in the Unity editor. On OpenGL (the Mac and Linux
versions of the game) a compiled shader is only its GLSL text plus a list of what it reads, so this
takes one small shader out of a bundle that ships with the game, as a pattern, replaces the text and
the list with ours, and writes a new bundle. Direct3D (the Windows version) needs compiled bytecode,
which this cannot make: there the mod falls back to drawing its particles as soft sprites.
"""
import copy
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import glprogram
import typetree
from unityfile import Asset, Bundle, Reader, Writer, lz4_store

OPENGL_CORE = 15

# What each shader's fragment stage reads: (name, columns, is a matrix); and its textures: (name, dimensions).
# The last texture of each is the game's own depth picture, which is not a property of the material
# ("given": how many of the last are the game's own, where it is more than that one).
VERTEX = [("unity_ObjectToWorld", 4, True), ("unity_MatrixVP", 4, True)]
SHADERS = {
    "volume": {
        "name": "VolumetricExplosions/Volume", "path": "assets/volumetricexplosions/volume.shader", "id": 7307199000000000001, "program": 1987650001,
        "bundle": "volumetricexplosions", "file": "CAB-766f6c756d65747269636578706c6f73", "code": "volume.glsl",
        "fragment": [("_WorldSpaceCameraPos", 3, False), ("_ProjectionParams", 4, False), ("_ZBufferParams", 4, False), ("_VolParams", 4, False), ("_VolStep", 4, False), ("_VolGrid", 4, False),
                     ("_VolSize", 4, False), ("_VolOffset", 4, False), ("_VolDetail", 4, False), ("_VolFlow", 4, False), ("_VolCamera", 4, False), ("_VolPeak", 4, False), ("_VolThin", 4, False), ("_VolSun", 4, False),
                     ("_VolSunDir", 4, False), ("_VolSunLocal", 4, False), ("_VolAmb", 4, False), ("_VolGlow", 4, False), ("_VolLampA", 4, False), ("_VolLampB", 4, False), ("_VolLampC", 4, False), ("_VolLamps", 4, False),
                     ("_VolSceneA", 4, False), ("_VolSceneB", 4, False), ("_VolSceneC", 4, False), ("_VolSceneD", 4, False),
                     ("_VolSceneTintA", 4, False), ("_VolSceneTintB", 4, False), ("_VolSceneTintC", 4, False), ("_VolSceneTintD", 4, False),
                     ("_VolSceneDirA", 4, False), ("_VolSceneDirB", 4, False), ("_VolSceneDirC", 4, False), ("_VolSceneDirD", 4, False),
                     ("_VolJet", 4, False), ("_VolJetFrom", 4, False), ("_VolJetLook", 4, False), ("_VolJetBody", 4, False), ("_VolJetFlow", 4, False), ("_VolJetEnd", 4, False), ("_VolJetGlow", 4, False)] + [("_VolJetAt%d" % n, 4, False) for n in range(8)] + [("_VolFire", 4, False),
                     ("_VolTint", 4, False), ("_VolHot1", 4, False), ("_VolHot2", 4, False), ("_VolHot3", 4, False), ("_VolHot4", 4, False),
                     ("_VolCutA", 4, False), ("_VolCutB", 4, False), ("_VolCutC", 4, False), ("_VolCutD", 4, False), ("_VolCutE", 4, False), ("_VolCutF", 4, False), ("_VolSlot", 4, False),
                     ("_VolWakes", 4, False), ("_VolWakeLo", 4, False), ("_VolWakeHi", 4, False)]
                    + [("_VolWake%s%d" % (part, n), 4, False) for n in range(8) for part in "ABC"]
                    + [("unity_WorldToObject", 4, True), ("unity_MatrixV", 4, True)],
        "textures": [("_Volume", 3), ("_Amount", 3), ("_Detail", 3), ("_Around", 3), ("_Flow", 3), ("_RestA", 3), ("_RestB", 3), ("_RestC", 3), ("_RestD", 3), ("_RestE", 3), ("_Turns", 3), ("_Clear", 3), ("_CameraDepthTexture", 2)],
    },
    "mark": {
        "name": "VolumetricExplosions/Mark", "path": "assets/volumetricexplosions/mark.shader", "id": 7307199000000000002, "program": 1987650002,
        "bundle": "volumetricexplosionsmark", "file": "CAB-766f6c756d65747269636d61726b0000", "code": "mark.glsl",
        "fragment": [("_WorldSpaceCameraPos", 3, False), ("_ProjectionParams", 4, False), ("_ZBufferParams", 4, False), ("_VolCamera", 4, False), ("_MarkTint", 4, False), ("_MarkGlow", 4, False),
                     ("_MarkSize", 4, False), ("unity_WorldToObject", 4, True), ("unity_MatrixV", 4, True)],
        "textures": [("_MarkTex", 2), ("_MarkEmbers", 2), ("_CameraDepthTexture", 2)],
    },
    "enlarge": {
        "name": "VolumetricExplosions/Enlarge", "path": "assets/volumetricexplosions/enlarge.shader", "id": 7307199000000000004, "program": 1987650004,
        "bundle": "volumetricexplosionsenlarge", "file": "CAB-766f6c756d6574726963656e6c617267", "code": "enlarge.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_VolCamera", 4, False)],
        "textures": [("_VolHalf", 2), ("_CameraDepthTexture", 2)],
        "vertex": VERTEX + [("_VolDrawn", 4, False)],
    },
    # Where each patch's smoke is going, written into the game's picture of motion, for a game that smooths between frames (see Layers.cs).
    "motion": {
        "name": "VolumetricExplosions/Motion", "path": "assets/volumetricexplosions/motion.shader", "id": 7307199000000000011, "program": 1987650011,
        "bundle": "volumetricexplosionsmotion", "file": "CAB-766f6c756d65747269636d6f74696f6e", "code": "motion.glsl",
        "fragment": [("_WorldSpaceCameraPos", 3, False), ("_VolCamera", 4, False), ("_VolSlot", 4, False), ("_MotGoes", 4, False), ("_MotFlow", 4, False),
                     ("_MotAxisX", 4, False), ("_MotAxisY", 4, False), ("_MotAxisZ", 4, False), ("_MotNowX", 4, False), ("_MotNowY", 4, False), ("_MotNowW", 4, False),
                     ("_MotWasX", 4, False), ("_MotWasY", 4, False), ("_MotWasW", 4, False), ("unity_WorldToObject", 4, True)],
        "textures": [("_VolAtlas", 2), ("_VolAtlasDepth", 2), ("_Flow", 3)],
        "given": 0,
        "blend": (1.0, 0.0),             # what it draws takes the place of what was there
    },
    "layers": {
        "name": "VolumetricExplosions/Layers", "path": "assets/volumetricexplosions/layers.shader", "id": 7307199000000000010, "program": 1987650010,
        "bundle": "volumetricexplosionslayers", "file": "CAB-766f6c756d65747269636c6179657273", "code": "layers.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_VolCamera", 4, False), ("_VolLayers", 4, False), ("_VolSlots", 4, False), ("_WeatherClouds", 4, False)],
        "textures": [("_VolAtlas", 2), ("_VolAtlasDepth", 2), ("_VolFull", 2), ("_VolFullDepth", 2), ("_CameraDepthTexture", 2), ("_WeatherCloudsPic", 2), ("_WeatherCloudsDist", 2)],
        "vertex": [("_VolSheet", 4, False)],
        "cull": 0.0,                     # a sheet over the part of the screen the smoke is on, whichever way round
    },
    "shock": {
        "name": "VolumetricExplosions/Shock", "path": "assets/volumetricexplosions/shock.shader", "id": 7307199000000000003, "program": 1987650003,
        "bundle": "volumetricexplosionsshock", "file": "CAB-766f6c756d6574726963730068636b00", "code": "shock.glsl",
        "fragment": [("_ShockEye", 4, False), ("_ShockRight", 4, False), ("_ShockUp", 4, False), ("_ShockAhead", 4, False), ("_ShockDepth", 4, False),
                     ("_ShockC0", 4, False), ("_ShockC1", 4, False), ("_ShockC2", 4, False), ("_ShockC3", 4, False),
                     ("_ShockP0", 4, False), ("_ShockP1", 4, False), ("_ShockP2", 4, False), ("_ShockP3", 4, False),
                     ("_ShockG0", 4, False), ("_ShockG1", 4, False), ("_ShockG2", 4, False), ("_ShockG3", 4, False), ("_ShockPulse", 4, False)],
        "textures": [("_VolScene", 2), ("_CameraDepthTexture", 2)],
        "vertex": [("_ShockSheet", 4, False)],
        "cull": 0.0, "ztest": 8.0,       # a sheet over the whole screen; the shader works out for itself what stands in front of a front
    },
    "shadow": {
        "name": "VolumetricExplosions/Shadow", "path": "assets/volumetricexplosions/shadow.shader", "id": 7307199000000000009, "program": 1987650009,
        "bundle": "volumetricexplosionsshadow", "file": "CAB-766f6c756d6574726963736861646f77", "code": "shadow.glsl",
        "fragment": [("_WorldSpaceCameraPos", 3, False), ("_ProjectionParams", 4, False), ("_ZBufferParams", 4, False), ("_VolCamera", 4, False),
                     ("_ShadowGridX", 4, False), ("_ShadowGridY", 4, False), ("_ShadowGridZ", 4, False), ("_ShadowSun", 4, False), ("_ShadowSunWorld", 4, False), ("unity_MatrixV", 4, True),
                     ("_VolFlow", 4, False), ("_VolSize", 4, False)],
        "textures": [("_Volume", 3), ("_Flow", 3), ("_CameraDepthTexture", 2)],
        "blend": (2.0, 0.0),             # what is there is multiplied by what it draws: darkened, and nothing added
    },
    "lens": {
        "name": "Keystone/Lens", "path": "assets/keystone/lens.shader", "id": 7307199000000000005, "program": 1987650005,
        "bundle": "keystonelens", "file": "CAB-6b657973746f6e656c656e7300000000", "code": "lens.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_LensStage", 4, False), ("_LensSize", 4, False), ("_LensFocus", 4, False), ("_LensMove", 4, False), ("_LensMarks", 4, False), ("_LensFrame", 4, False)],
        "textures": [("_LensScene", 2), ("_LensBlur", 2), ("_CameraMotionVectorsTexture", 2), ("_CameraDepthTexture", 2)],
        "vertex": [("_LensSheet", 4, False)],
        "cull": 0.0, "ztest": 8.0,
        "blend": (1.0, 0.0),             # what it draws takes the place of what was there
        "given": 2,                      # its last two textures are the game's own (how far each pixel has moved, and how far off it is)
    },
    "shafts": {
        "name": "NaturalLight/Shafts", "path": "assets/naturallight/shafts.shader", "id": 7307199000000000006, "program": 1987650006,
        "bundle": "nlshafts", "file": "CAB-6e6c7368616674730000000000000000", "code": "shafts.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_ShaftStage", 4, False), ("_ShaftSun", 4, False), ("_ShaftLook", 4, False), ("_ShaftTint", 4, False)],
        "textures": [("_ShaftRays", 2), ("_ShaftScene", 2), ("_CameraDepthTexture", 2)],
        "vertex": [("_ShaftSheet", 4, False)],
        "cull": 0.0, "ztest": 8.0,
        "blend": (1.0, 1.0),             # what it draws is added to what was there (its own pictures are wiped first)
        "given": 2,                      # (the picture so far and how far off each pixel is: both the game's own, handed over as they are)
    },
    "clean": {
        "name": "NaturalLight/Clean", "path": "assets/naturallight/clean.shader", "id": 7307199000000000007, "program": 1987650007,
        "bundle": "nlclean", "file": "CAB-6e6c636c65616e000000000000000000", "code": "clean.glsl",
        "fragment": [("_CleanMark", 4, False)],
        "textures": [("_CleanScene", 2)],
        "vertex": [("_CleanSheet", 4, False)],
        "cull": 0.0, "ztest": 8.0,
        "blend": (1.0, 0.0),             # what it draws takes the place of what was there (and it draws only where it has to)
        "given": 1,                      # (the half-size picture is handed to it as it is made, not kept by the material)
    },
    "veil": {
        "name": "NaturalLight/Veil", "path": "assets/naturallight/veil.shader", "id": 7307199000000000021, "program": 1987650021,
        "bundle": "nlveil", "file": "CAB-6e6c7665696c00000000000000000000", "code": "veil.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_VeilView", 4, False), ("_VeilUp", 4, False), ("_VeilAir", 4, False), ("_VeilColour", 4, False), ("_VeilSun", 4, False), ("_VeilSunColour", 4, False), ("_VeilSea", 4, False)],
        "textures": [("_CameraDepthTexture", 2)],
        "vertex": [("_VeilSheet", 4, False)],
        "cull": 0.0, "ztest": 8.0,       # a sheet over the whole screen, drawn over what is there by as much weather as there is (premultiplied)
    },
    # Natural Weather's clouds: "clouds" makes the noise, walks the clouds into small pictures of its own, takes them together
    # over frames and works out their shadow (all into its own pictures); "cloudsover" lays them on the camera's picture;
    # "cloudshade" darkens the scene under them. (Numbers from 101 up, out of the way of the others'.)
    "clouds": {
        "name": "NaturalWeather/Clouds", "path": "assets/naturalweather/clouds.shader", "id": 7307199000000000101, "program": 1987650101,
        "bundle": "weatherclouds", "file": "CAB-77656174686572636c6f756473000000", "code": "clouds.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_CloudStage", 4, False), ("_CloudCam", 4, False), ("_CloudHigh", 4, False), ("_CloudRight", 4, False), ("_CloudUp", 4, False),
                     ("_CloudAhead", 4, False), ("_CloudFixedX", 4, False), ("_CloudFixedY", 4, False), ("_CloudFixedZ", 4, False), ("_CloudNoise", 4, False), ("_CloudDrift", 4, False),
                     ("_CloudShell", 4, False), ("_CloudLook", 4, False), ("_CloudSun", 4, False), ("_CloudSunTint", 4, False), ("_CloudSky", 4, False), ("_CloudGround", 4, False),
                     ("_CloudAir", 4, False), ("_CloudHaze", 4, False), ("_CloudFlashA", 4, False), ("_CloudFlashB", 4, False), ("_CloudFlashTint", 4, False),
                     ("_CloudHoleA", 4, False), ("_CloudHoleB", 4, False), ("_CloudHoleC", 4, False),
                     ("_CloudFieldA", 4, False), ("_CloudFieldB", 4, False), ("_CloudFieldLevA", 4, False), ("_CloudFieldLevB", 4, False), ("_CloudFieldLevC", 4, False), ("_CloudFieldLevD", 4, False),
                     ("_CloudPrevAt", 4, False), ("_CloudPrevRight", 4, False), ("_CloudPrevUp", 4, False), ("_CloudPrevAhead", 4, False),
                     ("_CloudShadowAt", 4, False), ("_CloudShadowX", 4, False), ("_CloudShadowY", 4, False), ("_CloudSunFixed", 4, False),
                     ("_CloudStreets", 4, False), ("_CloudAlong", 4, False), ("_CloudHabit", 4, False), ("_CloudForm", 4, False),
                     ("_CloudWakeLook", 4, False), ("_CloudWakeMid", 4, False), ("_CloudHeapsAt", 4, False), ("_CloudJitter", 4, False), ("_CloudTwistA", 4, False), ("_CloudTwistB", 4, False), ("_CloudEyeA", 4, False), ("_CloudEyeB", 4, False), ("_CloudEyeC", 4, False), ("_CloudEyeD", 4, False), ("_CloudShelfA", 4, False), ("_CloudShelfB", 4, False), ("_CloudShelfC", 4, False)] + [("_CloudWake%d" % n, 4, False) for n in range(8)]
                    + [("_CloudCellA%d" % n, 4, False) for n in range(6)] + [("_CloudCellB%d" % n, 4, False) for n in range(6)],
        "textures": [("_CloudField", 3), ("_CloudShape", 3), ("_CloudDetail", 3), ("_CloudMap", 2), ("_CloudMapB", 2), ("_CloudMapC", 2), ("_CloudAirLut", 2), ("_CloudNow", 2), ("_CloudNowD", 2),
                     ("_CloudHist", 2), ("_CloudHistD", 2), ("_CameraDepthTexture", 2), ("_CloudCells", 2), ("_CloudHeaps", 2), ("_CloudHeapsB", 2), ("_CloudShadowB", 2)],
        "vertex": [("_CloudSheet", 4, False)],
        "cull": 0.0, "ztest": 8.0,
        "blend": (1.0, 0.0),             # what it draws takes the place of what was there (its own pictures)
    },
    "cloudsover": {
        "name": "NaturalWeather/CloudsOver", "path": "assets/naturalweather/cloudsover.shader", "id": 7307199000000000102, "program": 1987650102,
        "bundle": "weathercloudsover", "file": "CAB-77656174686572636c6f7564736f7665", "code": "cloudsover.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_CloudRight", 4, False), ("_CloudUp", 4, False), ("_CloudAhead", 4, False), ("_CloudOver", 4, False)],
        "textures": [("_CloudHist", 2), ("_CloudHistD", 2), ("_CameraDepthTexture", 2)],
        "vertex": [("_CloudSheet", 4, False)],
        # (laid over what is there by how much the cloud hides: colour already multiplied by that. The sheet stands as far off
        # as the nearest cloud can be (Clouds.cs, Setup) and is hidden by whatever is drawn nearer: 4, nearer or as near)
        "cull": 0.0, "ztest": 4.0,
    },
    # Natural Ocean's sea: a mesh moved by the waves in its vertex program, drawn solid (it writes how far off it is, so that
    # what is drawn after it, smoke and spray, goes behind the waves it is behind). (Numbers from 201 up.)
    "ocean": {
        "name": "NaturalOcean/Sea", "path": "assets/naturalocean/sea.shader", "id": 7307199000000000201, "program": 1987650201,
        "bundle": "naturalocean", "file": "CAB-6e61747572616c6f6365616e00000000", "code": "ocean.glsl",
        "fragment": [("_WorldSpaceCameraPos", 3, False), ("_ZBufferParams", 4, False), ("_ScreenParams", 4, False), ("_ProjectionParams", 4, False),
                     ("_OceanRoughA", 4, False), ("_OceanRoughB", 4, False), ("_OceanSun", 4, False), ("_OceanSunTint", 4, False), ("_OceanSkyTop", 4, False), ("_OceanSkyLow", 4, False),
                     ("_OceanWater", 4, False), ("_OceanFoam", 4, False), ("_FoamAtA", 4, False), ("_FoamAtB", 4, False), ("_FoamTexel", 4, False), ("_FoamReach", 4, False), ("_OceanLaceAt", 4, False), ("_OceanLaceAtB", 4, False), ("_OceanLaceCount", 4, False), ("_OceanLaceCountB", 4, False), ("_OceanLaceTurn", 4, False), ("_OceanLaceMix", 4, False),
                     ("_OceanMirrorX", 4, False), ("_OceanMirrorY", 4, False), ("_OceanMirrorW", 4, False), ("_OceanAtSpot", 4, False),
                     ("_OceanHighA", 4, False), ("_OceanHighB", 4, False), ("_UnderwaterFogColor", 4, False), ("_OceanStreak", 4, False), ("_OceanStreakAt", 4, False),
                     ("_WeedUp", 4, False), ("_WeedFirst", 4, False), ("_WeedSecond", 4, False), ("_UnderLight", 4, False), ("_OceanFarZ", 4, False),
                     ("_WeatherVeilAir", 4, False), ("_WeatherVeilColour", 4, False), ("_WeatherVeilSun", 4, False), ("_WeatherVeilSunColour", 4, False), ("_OceanRain", 4, False), ("_OceanGust", 4, False), ("_OceanDentAt", 4, False), ("_OceanDentMid", 4, False), ("_OceanDentCurl", 4, False)],
        "vertex": [("unity_ObjectToWorld", 4, True), ("unity_MatrixVP", 4, True), ("_OceanMid", 4, False), ("_OceanAxisA", 4, False), ("_OceanAxisB", 4, False),
                   ("_OceanLay", 4, False), ("_OceanTwin", 4, False), ("_OceanShore", 4, False), ("_OceanMapAt", 4, False), ("_OceanWind", 4, False), ("_SeabedAtA", 4, False), ("_SeabedAtB", 4, False), ("_SeabedCell", 4, False),
                   ("_OceanHighA", 4, False), ("_OceanHighB", 4, False), ("_OceanSurfA", 4, False), ("_OceanSurfB", 4, False), ("_OceanAtSpot", 4, False),
                   ("_FoamAtA", 4, False), ("_FoamAtB", 4, False), ("_FoamTexel", 4, False), ("_FoamReach", 4, False), ("_OceanBreaks", 4, False), ("_OceanBreakHow", 4, False), ("_OceanDentAt", 4, False), ("_OceanDentMid", 4, False), ("_OceanDentCurl", 4, False)]
                  + [("_OceanAt%d" % n, 4, False) for n in range(8)],
        # (the waves: two stacks of eight textures each, 5 being a stack of flat textures)
        "textures": [("_OceanMoved", 5), ("_OceanSlope", 5), ("_OceanFoamMap", 2), ("_OceanLace", 2), ("_OceanMirror", 2), ("_OceanSeabed", 2), ("_OceanSurfTime", 2), ("_OceanSurfDeep", 2), ("_OceanFarDepth", 2), ("_OceanFarScene", 2), ("_OceanDent", 2), ("_OceanDentMove", 2), ("_OceanScene", 2), ("_CameraDepthTexture", 2)],
        "given": 2,                      # (the picture so far and how far off each pixel of it is: the game's own, handed over as they are)
        "blend": (1.0, 0.0), "cull": 0.0, "ztest": 4.0, "zwrite": 1.0,
    },
    # Natural Ocean's white water: worked out from the waves and from itself as it was (a picture of its own, not the screen).
    "oceanfoam": {
        "name": "NaturalOcean/Foam", "path": "assets/naturalocean/foam.shader", "id": 7307199000000000202, "program": 1987650202,
        "bundle": "naturaloceanfoam", "file": "CAB-6e61747572616c6f6365616e666f616d", "code": "oceanfoam.glsl",
        "fragment": [("_OceanLay", 4, False), ("_OceanTwin", 4, False)] + [("_OceanAt%d" % n, 4, False) for n in range(5)]
                    + [("_Foam" + n, 4, False) for n in ("Texel", "CellA", "CellB", "MovedA", "MovedB", "SnapA", "SnapB", "AtA", "AtB", "Breaks", "Time", "Count", "Mode", "Ragged", "Coarse", "Front", "Pass", "Rows", "RowsAt")]
                    + [("_SeabedAtA", 4, False), ("_SeabedAtB", 4, False), ("_SeabedCell", 4, False), ("_OceanHighA", 4, False), ("_OceanHighB", 4, False),
                       ("_OceanSurfA", 4, False), ("_OceanSurfB", 4, False), ("_OceanAtSpot", 4, False)]
                    + [("_FoamHull%d" % n, 4, False) for n in range(12)] + [("_OceanDentAt", 4, False)],
        "textures": [("_OceanSlope", 5), ("_FoamPrev", 2), ("_OceanSeabed", 2), ("_OceanSurfTime", 2), ("_OceanSurfDeep", 2), ("_OceanLace", 2), ("_OceanDentMove", 2), ("_FoamFronts", 2)],
        "vertex": [],
        "cull": 0.0, "ztest": 8.0,
        "blend": (1.0, 0.0),
    },
    # Natural Ocean's seaweed: ribbons laid out and swayed in the vertex program from a mesh that only says where each plant stands.
    "oceanweed": {
        "name": "NaturalOcean/Weed", "path": "assets/naturalocean/weed.shader", "id": 7307199000000000203, "program": 1987650203,
        "bundle": "naturaloceanweed", "file": "CAB-6e61747572616c6f6365616e77656564", "code": "oceanweed.glsl",
        "fragment": [("_WeedLight", 4, False), ("_WeedSky", 4, False), ("_WeedWater", 4, False), ("_OceanMirrorX", 4, False), ("_OceanMirrorY", 4, False), ("_OceanMirrorW", 4, False),
                     ("_WeedUpHere", 4, False)],
        "vertex": [("unity_ObjectToWorld", 4, True), ("unity_MatrixVP", 4, True), ("_WeedSway", 4, False), ("_WeedClock", 4, False), ("_WeedEye", 4, False)],
        "textures": [("_OceanMirror", 2)],
        "cull": 0.0, "ztest": 4.0, "zwrite": 1.0,
        "blend": (1.0, 0.0),
    },
    # Natural Ocean's lens in the water: laid over the finished picture while the camera's glass is at the water or under it.
    "oceanunder": {
        "name": "NaturalOcean/Under", "path": "assets/naturalocean/under.shader", "id": 7307199000000000204, "program": 1987650204,
        "bundle": "naturaloceanunder", "file": "CAB-6e61747572616c6f6365616e756e6465", "code": "oceanunder.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_UnderLens", 4, False), ("_UnderPlane", 4, False), ("_UnderUp", 4, False), ("_UnderMapA", 4, False), ("_UnderMapB", 4, False),
                     ("_UnderLight", 4, False), ("_UnderSky", 4, False), ("_UnderTakes", 4, False), ("_UnderSun", 4, False), ("_UnderEye", 4, False),
                     ("_UnderSunWay", 4, False),
                     ("_OceanLay", 4, False), ("_OceanTwin", 4, False), ("_OceanAt3", 4, False), ("_OceanAt4", 4, False), ("_OceanAt5", 4, False), ("_OceanAt6", 4, False)],
        "textures": [("_OceanSlope", 5), ("_UnderScene", 2), ("_UnderMap", 2), ("_CameraDepthTexture", 2)],
        "given": 3,                      # (the picture so far, the map of the water's height at the glass, and how far off each pixel is: handed over as they are)
        "vertex": [],
        "cull": 0.0, "ztest": 8.0,
        "blend": (1.0, 0.0),
    },
    # Natural Weather's rain on the camera's glass and on the ground: a pass over the finished picture (Wet.cs).
    # Natural Weather's drops on the camera's glass, each drawn into a picture of how high the water stands (Drops.cs).
    "drops": {
        "name": "NaturalWeather/Drops", "path": "assets/naturalweather/drops.shader", "id": 7307199000000000105, "program": 1987650105,
        "bundle": "weatherdrops", "file": "CAB-7765617468657264726f707300000000", "code": "drops.glsl",
        "fragment": [],
        "textures": [],
        "given": 0,
        "vertex": [("_ProjectionParams", 4, False)],
        "cull": 0.0, "ztest": 4.0, "zwrite": 1.0,  # (the highest water kept where two drops overlap: the height written as the depth)
        "blend": (1.0, 0.0),
    },
    "wet": {
        "name": "NaturalWeather/Wet", "path": "assets/naturalweather/wet.shader", "id": 7307199000000000104, "program": 1987650104,
        "bundle": "weatherwet", "file": "CAB-77656174686572776574000000000000", "code": "wet.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_WetRight", 4, False), ("_WetUp", 4, False), ("_WetAhead", 4, False), ("_WetCam", 4, False), ("_WetHere", 4, False),
                     ("_WetAcross", 4, False), ("_WetAlong", 4, False), ("_WetGround", 4, False), ("_WetSky", 4, False), ("_WetSun", 4, False), ("_WetGlass", 4, False),
                     ("_WetDropsSize", 4, False)],
        "textures": [("_WetDrops", 2), ("_WetScene", 2), ("_CameraDepthTexture", 2)],
        "given": 2,                      # (the picture so far and how far off each pixel is: the game's own, handed over as they are)
        "vertex": [],
        "cull": 0.0, "ztest": 8.0,
        "blend": (1.0, 0.0),
    },
    "cloudshade": {
        "name": "NaturalWeather/CloudShade", "path": "assets/naturalweather/cloudshade.shader", "id": 7307199000000000103, "program": 1987650103,
        "bundle": "weathercloudshade", "file": "CAB-77656174686572636c6f756473686164", "code": "cloudshade.glsl",
        "fragment": [("_ZBufferParams", 4, False), ("_CloudRight", 4, False), ("_CloudUp", 4, False), ("_CloudAhead", 4, False), ("_CloudCam", 4, False), ("_CloudSun", 4, False),
                     ("_CloudShadeAt", 4, False), ("_CloudShadeX", 4, False), ("_CloudShadeY", 4, False), ("_CloudShade", 4, False), ("_CloudShadeSun", 4, False), ("_CloudWet", 4, False)],
        "textures": [("_CloudShadowMap", 2), ("_CameraDepthTexture", 2)],
        "vertex": [("_CloudSheet", 4, False)],
        "cull": 0.0, "ztest": 8.0,
        "blend": (2.0, 3.0),             # what is there is multiplied by twice what it draws: darkened or lightened, nothing added
    },
}


def find(folder, name):
    for where, _, files in os.walk(folder):
        if name in files:
            return os.path.join(where, name)
    raise SystemExit("the game's %s was not found under %s" % (name, folder))


def pattern(ksp, bundle_name, class_id, pick):
    """An object of the given class from one of the game's own bundles, with the description of its layout."""
    bundle = Bundle(open(find(os.path.join(ksp, "GameData"), bundle_name), "rb").read())
    for name, data in bundle.files.items():
        if bundle.node_flags[name] != 4: continue
        asset = Asset(data)
        if asset.version != 21: continue
        for obj in asset.objects:
            if obj["class"] != class_id: continue
            kind = asset.types[obj["type"]]
            root = typetree.parse_tree(kind["tree"], kind["tree_counts"], asset.version)
            value = typetree.read(root, Reader(asset.bytes_of(obj)))
            if pick(value): return value, root, kind["raw"]
    raise SystemExit("no pattern of class %d in %s" % (class_id, bundle_name))


def layout(params):
    """Positions for a list of parameters, sixteen bytes to a vector and sixty-four to a matrix."""
    at, out = 0, []
    for name, cols, matrix in params:
        out.append((name, cols, matrix, at))
        at += 64 if matrix else 16
    return out, at


def fixed_locations(code, textures):
    """The GLSL with each texture's uniform given a location of its own (its place in the list of textures), where the driver
    takes that: GL_ARB_explicit_uniform_location, which NVIDIA's and AMD's drivers have (on Windows and Linux) and the Mac's
    OpenGL 4.1 has not (there the text reads as it was).

    Left to itself NVIDIA's driver numbers a program's uniforms in the order of their names, and Unity cannot give a texture
    unit to a texture whose uniform comes 32nd or later: it says "OpenGL Error: Invalid texture unit!" at every draw, and the
    texture is read from unit 0 instead. Found on Windows started with -force-glcore (2026-10-10): the smoke's _Volume (after
    76 _Vol... values) was right only because it is on unit 0 anyway; the clouds, with five of their maps there, drew nothing
    at all (a 2D and a 3D texture on one unit)."""
    text = code.decode("utf-8")
    at = text.index("#ifdef FRAGMENT")

    def placed_in(stage, what):
        out, placed = [], False
        for line in stage.split("\n"):
            m = re.match(r"uniform (sampler\w+) (\w+);(.*)$", line)
            if m and m.group(2) in textures:
                where = textures.index(m.group(2))
                out += ["#ifdef GL_ARB_explicit_uniform_location",
                        "layout(location = %d) uniform %s %s;%s" % (where, m.group(1), m.group(2), m.group(3)),
                        "#else", line, "#endif"]
                continue
            out.append(line)
            # (the extension asked for straight after the program's #version line, before anything that is not a directive)
            if not placed and line.startswith("#version"):
                out.append("#extension GL_ARB_explicit_uniform_location : enable")
                placed = True
        assert placed, "no #version line in the %s program" % what
        return "\n".join(out)

    # (The vertex program too, where it reads textures: the sea's does, for its waves. Left without places of their own there,
    # on NVIDIA's OpenGL the sea's two stacks of waves read as nothing and the sea lay flat as glass, with no word in the log:
    # seen on Windows started with -force-glcore, 2026-10-11.)
    head, body = text[:at], text[at:]
    if re.search(r"^uniform sampler\w+ \w+;", head, re.M): head = placed_in(head, "vertex")
    return (head + placed_in(body, "fragment")).encode("utf-8")


def build(ksp, out_path, which):
    spec = SHADERS[which]
    NAME, PATH, SHADER_ID, FRAGMENT, TEXTURES = spec["name"], spec["path"], spec["id"], spec["fragment"], spec["textures"]
    VERTEX = spec.get("vertex", globals()["VERTEX"])
    shader, shader_tree, shader_type = pattern(ksp, "makinghistory_scene", 48, lambda v: v["m_ParsedForm"]["m_Name"] == "UnlitAlpha")
    index, index_tree, index_type = pattern(ksp, "serenity.kspexpansion", 142, lambda v: True)
    code = fixed_locations(open(os.path.join(HERE, spec["code"]), "rb").read(), [t[0] for t in TEXTURES])

    names = ["$Globals"] + [p[0] for p in FRAGMENT + VERTEX] + [t[0] for t in TEXTURES]
    number = {name: n for n, name in enumerate(names)}
    fragment, fragment_size = layout(FRAGMENT)
    vertex, vertex_size = layout(VERTEX)

    # ---- the readable description of the shader
    form = shader["m_ParsedForm"]
    form["m_Name"] = NAME
    texture_prop = copy.deepcopy(form["m_PropInfo"]["m_Props"][0])
    props = []
    for name, dims in TEXTURES[:len(TEXTURES) - spec.get("given", 1)]:
        prop = copy.deepcopy(texture_prop)
        prop["m_Name"], prop["m_Description"] = name, name[1:]
        prop["m_DefTexture"] = {"m_DefaultName": "", "m_TexDim": dims}
        props.append(prop)
    form["m_PropInfo"]["m_Props"] = props
    one = form["m_SubShaders"][0]["m_Passes"][0]
    one["m_NameIndices"] = [{"first": name, "second": number[name]} for name in sorted(names)]
    state = one["m_State"]
    source, dest = spec.get("blend", (1.0, 10.0))                        # (as a rule: colour already multiplied by opacity)
    for key, value in (("srcBlend", source), ("destBlend", dest), ("srcBlendAlpha", source), ("destBlendAlpha", dest)):
        state["rtBlend0"][key]["val"] = value
    state["zWrite"]["val"] = spec.get("zwrite", 0.0)      # 1: it writes how far off it is (the sea, which is solid); as a rule nothing does
    state["zTest"]["val"] = spec.get("ztest", 8.0)       # 8, always: the shader itself stops at whatever is in front
    state["culling"]["val"] = spec.get("cull", 1.0)      # 1: draw the far faces of the box, so it still works with the camera inside
    state["gpuProgramID"] = spec["program"]

    def groups_for_form():
        out = []
        for params, size in ((fragment, fragment_size), (vertex, vertex_size)):
            out.append({"m_NameIndex": number["$Globals"],
                        "m_MatrixParams": [{"m_NameIndex": number[n], "m_Index": at, "m_ArraySize": 0, "m_Type": 0, "m_RowCount": 4} for n, c, m, at in params if m],
                        "m_VectorParams": [{"m_NameIndex": number[n], "m_Index": at, "m_ArraySize": 0, "m_Type": 0, "m_Dim": c} for n, c, m, at in params if not m],
                        "m_StructParams": [], "m_Size": size})
        return out

    gl_vertex = [s for s in one["progVertex"]["m_SubPrograms"] if s["m_GpuProgramType"] == glprogram.GLCORE][0]
    gl_fragment = [s for s in one["progFragment"]["m_SubPrograms"] if s["m_GpuProgramType"] == glprogram.GLCORE][0]
    gl_vertex["m_BlobIndex"] = 0
    gl_vertex["m_Channels"] = {"m_Channels": [], "m_SourceMap": 1}                 # positions only
    gl_vertex["m_TextureParams"] = [{"m_NameIndex": number[n], "m_Index": i, "m_SamplerIndex": i, "m_MultiSampled": False, "m_Dim": d} for i, (n, d) in enumerate(TEXTURES)]
    gl_vertex["m_ConstantBuffers"] = groups_for_form()
    gl_vertex["m_ConstantBufferBindings"] = []
    gl_fragment["m_BlobIndex"] = 1
    one["progVertex"]["m_SubPrograms"] = [gl_vertex]
    one["progFragment"]["m_SubPrograms"] = [gl_fragment]

    # ---- the program itself
    def group(params, size):
        return {"name": "$Globals", "size": size, "params": [{"name": n, "type": 0, "rows": 4 if m else 1, "cols": c, "matrix": 1 if m else 0, "array": 0, "index": at} for n, c, m, at in params]}

    first = glprogram.write_entry({"code": code, "source_map": 1, "groups": [{"name": "", "size": 0, "params": []}, group(fragment, fragment_size), group(vertex, vertex_size)],
                                   "bindings": [{"name": n, "type": 0, "index": i, "extra": i, "texture": d << 1} for i, (n, d) in enumerate(TEXTURES)]})
    second = glprogram.write_entry({})
    blob = glprogram.write_blob([first, second])
    packed = lz4_store(blob)
    shader["platforms"] = [OPENGL_CORE]
    shader["offsets"] = [[0]]
    shader["compressedLengths"] = [[len(packed)]]
    shader["decompressedLengths"] = [[len(blob)]]
    shader["compressedBlob"] = packed

    # ---- the bundle's own index
    index["m_Name"] = index["m_AssetBundleName"] = spec["bundle"]
    index["m_PreloadTable"] = [{"m_FileID": 0, "m_PathID": SHADER_ID}]
    index["m_Container"] = [{"first": PATH, "second": {"preloadIndex": 0, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": SHADER_ID}}}]

    def packed_object(root, value):
        w = Writer()
        typetree.write(root, value, w)
        return bytes(w.b)

    asset = write_asset([shader_type, index_type], [(SHADER_ID, 0, packed_object(shader_tree, shader)), (1, 1, packed_object(index_tree, index))])
    Asset(asset)                                   # reads back, or this stops here
    data = write_bundle(spec["file"], asset)
    check = Bundle(data)
    assert list(check.files.values())[0] == asset
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    open(out_path, "wb").write(data)
    print("wrote %s (%d bytes, %d of GLSL)" % (out_path, len(data), len(code)))


def write_asset(types, objects):
    meta = Writer()
    meta.cstr("2019.4.18f1"); meta.i32(19); meta.boolean(True)           # 19: the platform the game's own bundles name; with layouts included they load on any
    meta.i32(len(types))
    for raw in types: meta.raw(raw)
    meta.i32(len(objects))
    body = Writer()
    for path_id, kind, data in sorted(objects, key=lambda o: o[0]):      # the game looks objects up by number, in order
        body.align(8)
        meta.align()
        meta.i64(path_id); meta.u32(len(body.b)); meta.u32(len(data)); meta.i32(kind)
        body.raw(data)
    meta.i32(0); meta.i32(0); meta.i32(0)                                 # no scripts, no other files, no reference types
    meta.cstr("")
    start = (20 + len(meta.b) + 15) // 16 * 16
    out = Writer(big=True)
    out.u32(len(meta.b)); out.u32(start + len(body.b)); out.u32(21); out.u32(start)
    out.u8(0); out.raw(b"\0\0\0")
    out.raw(meta.b)
    while len(out.b) < start: out.b.append(0)
    out.raw(body.b)
    return bytes(out.b)


def write_bundle(name, asset):
    info = Writer(big=True)
    info.raw(b"\0" * 16)
    info.i32(1); info.u32(len(asset)); info.u32(len(asset)); info.u16(0x40)
    info.i32(1); info.i64(0); info.i64(len(asset)); info.u32(4); info.cstr(name)
    head = Writer(big=True)
    head.cstr("UnityFS"); head.u32(7); head.cstr("5.x.x"); head.cstr("2019.4.18f1")
    size_at = len(head.b)
    head.i64(0); head.u32(len(info.b)); head.u32(len(info.b)); head.u32(0x40)
    head.align(16)
    out = bytearray(head.b + info.b + asset)
    out[size_at:size_at + 8] = len(out).to_bytes(8, "big")
    return bytes(out)


if __name__ == "__main__":
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] not in SHADERS): raise SystemExit(__doc__)
    build(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) == 4 else "volume")
