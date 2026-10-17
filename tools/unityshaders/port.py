#!/usr/bin/env python3
"""Write the Unity (ShaderLab) versions of this project's shaders from their GLSL.

    python3 tools/unityshaders/port.py

The shaders (the explosions' smoke, the enlarging of it, the burn mark, the shock front and the smoke's shadow,
Keystone's lens, Natural Light's shafts of sunlight and its blacking out of pixels that are not numbers, and the
weather mod's clouds, their laying on the picture and their shadow) are kept
in one place: tools/shaderpack/*.glsl, which is what the Mac and Linux versions of
the game run, packed into bundles by hand. Direct3D (Windows) wants compiled bytecode that only the Unity
editor can make, from HLSL. So the GLSL is turned into HLSL here by rule, and written out as .shader files
for the Unity project in this folder to build (see README.md). Edit the GLSL and run this again; do not
edit the .shader files.

The rules are those these need and no more: this is not a general translator. Where Direct3D differs in
a way no rule can see (which way up a picture lies), the lines concerned are replaced one by one, below,
and each such replacement must find its line exactly once, so that a changed GLSL stops this instead of
quietly leaving something out.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PACK = os.path.join(HERE, "..", "shaderpack")
sys.path.insert(0, PACK)
import make_bundle  # noqa: E402  (for each shader's name, textures and how it is blended)

# Where each goes in the Unity project.
FOLDER = {"volume": "VolumetricExplosions", "enlarge": "VolumetricExplosions", "layers": "VolumetricExplosions", "motion": "VolumetricExplosions", "mark": "VolumetricExplosions", "shock": "VolumetricExplosions", "shadow": "VolumetricExplosions", "lens": "Keystone",
          "shafts": "NaturalLight", "clean": "NaturalLight", "veil": "NaturalLight", "clouds": "NaturalWeather", "cloudsover": "NaturalWeather", "cloudshade": "NaturalWeather", "wet": "NaturalWeather", "drops": "NaturalWeather",
          "ocean": "NaturalOcean", "oceanfoam": "NaturalOcean", "oceanweed": "NaturalOcean", "oceanunder": "NaturalOcean"}

# What Unity's own headers already declare.
GIVEN = {"_WorldSpaceCameraPos", "_ProjectionParams", "_ZBufferParams", "_ScreenParams", "_Time"}

# Names that are something else in HLSL: a variable of ours that has one of them gets an underscore.
RESERVED = {"all", "any", "clip", "lit", "sample", "point", "line", "triangle", "linear", "half", "fixed", "string", "matrix", "vector", "pass", "technique",
            "texture", "sampler", "dst", "mad", "rcp", "noise", "frac", "lerp", "mul", "saturate", "fmod", "ddx", "ddy", "register", "shared", "volatile",
            "static", "export", "inline", "interface", "class", "namespace", "precise", "double", "uint", "dword", "auto", "case", "default", "switch",
            "typedef", "enum", "long", "short", "signed", "unsigned", "new", "delete", "this", "template", "typename", "union", "using", "virtual", "operator",
            "private", "protected", "public", "try", "catch", "throw", "goto", "char", "friend", "explicit", "mutable", "sizeof", "abort", "trunc_", "IN", "OUT", "result"}
# And HLSL's vector and matrix types, which GLSL has not: a variable of ours called long2 is a type there (and Microsoft's compiler,
# meeting long2, gives up with "out of memory while parsing").
HLSL_TYPES = re.compile(r"(bool|int|uint|dword|half|float|double|long|short|char|signed|unsigned|fixed|min16float|min10float|min16int|min12int|min16uint)[1-4](x[1-4])?")

# The camera's clip space ends at a different place on each system, and Direct3D counts depth backwards in this game.
HELD = """#if defined(UNITY_REVERSED_Z)
    clip.z = max(clip.z, clip.w * 0.00001);
#else
    clip.z = min(clip.z, clip.w * 0.99999);
#endif"""

# Lines that are replaced outright (in the GLSL, before anything else is done to it): (which shaders, which stage, the line, what it becomes).
# What they are about: on Direct3D a picture's first row is its top one, and the game draws into its pictures upside down to make up for
# it (_ProjectionParams.x is -1 while it does). So a place on the screen worked out from where a corner was put has to be turned over to
# match, and which way depends on what is being drawn into.
SPECIAL = [
    (("volume", "enlarge", "mark", "shadow"), "vertex", "    clip.z = min(clip.z, clip.w * 0.99999);", HELD),
    (("volume", "mark", "shadow"), "fragment", "vec2 uv = vs_TEXCOORD1.xy / vs_TEXCOORD1.w * 0.5 + 0.5;",
     "vec2 uv = vec2(vs_TEXCOORD1.x / vs_TEXCOORD1.w, vs_TEXCOORD1.y / vs_TEXCOORD1.w * _ProjectionParams.x) * 0.5 + 0.5;"),
    (("shock",), "vertex", "    vs_TEXCOORD0 = in_POSITION0.xy;", "    vs_TEXCOORD0 = vec2(in_POSITION0.x, in_POSITION0.y * _ProjectionParams.x);"),
    (("lens",), "vertex", "    vs_TEXCOORD0 = in_POSITION0.xy * 0.5 + 0.5;",
     """    // (the first of the two goes is drawn into a picture of the lens's own, which lies as every picture does on this system;
    // the second into whatever the camera is drawing into, which may lie either way: see SPECIAL in tools/unityshaders/port.py)
    float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
    way = _LensStage.x < 1.5 ? -1.0 : _ProjectionParams.x;
#endif
    vs_TEXCOORD0 = vec2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);"""),
    (("lens",), "fragment", "        turn = further * turn;", "        turn = mul(further, turn);"),
    (("shafts",), "vertex", "    vs_TEXCOORD0 = in_POSITION0.xy * 0.5 + 0.5;",
     """    // (the first two goes are drawn into pictures of the shafts' own, which lie as every picture does on this system; the
    // third into whatever the camera is drawing into, which may lie either way: as for the lens, above)
    float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
    way = _ShaftStage.x < 2.5 ? -1.0 : _ProjectionParams.x;
#endif
    vs_TEXCOORD0 = vec2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);"""),
    (("clouds",), "vertex", "    vs_TEXCOORD0 = in_POSITION0.xy * 0.5 + 0.5;",
     """    // (every go of the clouds is drawn into pictures of their own (the noise's slices among them), which lie as every
    // picture does on this system: as for the lens's first go, above)
    float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
    way = -1.0;
#endif
    vs_TEXCOORD0 = vec2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);"""),
    (("cloudsover", "cloudshade", "veil", "oceanunder", "wet"), "vertex", "    vs_TEXCOORD0 = in_POSITION0.xy * 0.5 + 0.5;",
     """    // (drawn into whatever the camera is drawing into, which may lie either way: as for the lens's second go, above; Natural
    // Light's veil and the sea's lens in the water the same)
    float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
    way = _ProjectionParams.x;
#endif
    vs_TEXCOORD0 = vec2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);"""),
    (("enlarge",), "fragment", "    if (s00 == vec4(0.0) && s10 == vec4(0.0) && s01 == vec4(0.0) && s11 == vec4(0.0)) discard;",
     "    if (AllZero(s00) && AllZero(s10) && AllZero(s01) && AllZero(s11)) discard;"),
    (("layers",), "fragment", "        if (s00 == vec4(0.0) && s10 == vec4(0.0) && s01 == vec4(0.0) && s11 == vec4(0.0)) continue;",
     "        if (AllZero(s00) && AllZero(s10) && AllZero(s01) && AllZero(s11)) continue;"),
    # (the sheet is laid over the part of the screen the camera's own projection gives; where Direct3D draws into a picture upside
    # down, the game's projection is turned over, and so must this be)
    # (vs_TEXCOORD0 stays as the screen is, taken before the turning over: the weather's pictures of the screen, which the
    # sheet reads, lie that way on every system: see "clouds" and "cloudsover" above)
    (("layers",), "vertex", "    gl_Position = vec4(at, 0.0, 1.0);",
     "    gl_Position = vec4(at.x, at.y * _ProjectionParams.x, 0.0, 1.0);"),
    # The sea (Natural Ocean), 2026-10-11.
    # (The lens's map: the sea's innermost ring drawn once more from straight above into a small picture of the water's height, its
    # corners put by hand. A picture of the sea's own: on Direct3D its first row is its top, so the second axis is turned over, to
    # put every place where the lens's pass (oceanunder.glsl, under) will look for it. And depth: OpenGL's runs from -1 to 1, the
    # nearer the less; Direct3D's from 0 to 1, in this game the nearer the greater, and anything below 0 is cut away. The higher
    # of two sheets of water must still win.)
    (("ocean",), "vertex", "        clip = vec4(dot(off, _OceanAxisA.xyz) * _OceanMapAt.w, dot(off, _OceanAxisB.xyz) * _OceanMapAt.w, clamp(-dot(off, up) * 0.5, -0.99, 0.99), 1.0);",
     """        float mapWay = 1.0;
#if UNITY_UV_STARTS_AT_TOP
        mapWay = -1.0;
#endif
#if defined(UNITY_REVERSED_Z)
        float mapDeep = 0.5 + 0.5 * clamp(dot(off, up) * 0.5, -0.99, 0.99);
#else
        float mapDeep = clamp(-dot(off, up) * 0.5, -0.99, 0.99);
#endif
        clip = vec4(dot(off, _OceanAxisA.xyz) * _OceanMapAt.w, dot(off, _OceanAxisB.xyz) * _OceanMapAt.w * mapWay, mapDeep, 1.0);"""),
    # (Which way a thing under water faces, from how its place changes from one pixel to the next. "The next pixel up" is the next
    # one down where Direct3D draws straight to the screen, and the face would come out turned away from the camera: the sun's
    # lines of light would lie on the sides of things turned from the sun. So it is turned to face the camera, whichever way it came.)
    (("oceanunder",), "fragment", "    vec3 faces = -normalize(cross(dFdx(place), dFdy(place)));",
     "    vec3 faces = -normalize(cross(dFdx(place), dFdy(place)));\n    if (dot(faces, place) > 0.0) faces = -faces;"),
]

# The shaders that only ever draw into pictures of their own, by a sheet whose corners are put by hand: the weather's clouds (their
# walk, the noise's slices, the putting together) and the sea's white water. There a pixel's place (gl_FragCoord) is the place of
# its point in the picture, the same on Direct3D as on OpenGL, and is handed on as it comes. (Neither is drawn by a camera's own
# projection, so _ProjectionParams, which FragCoord goes by, says nothing of them: the white water is worked out before any
# camera has drawn, with whatever the last one of the frame before left there.)
OWN = ("clouds", "oceanfoam")

UNITYCG = """            #include "UnityCG.cginc"
"""

# Outside the Unity editor (tools/unityshaders/windows.py compiles the program with Windows' own compiler) there is no
# UnityCG.cginc. In its place: Unity's three buffers of its own values, laid out exactly as Unity lays them out for Direct3D 11
# (Unity fills them itself, by name, wherever a program uses them: see WITHOUT-UNITY.md), the two names the rest uses, and what
# Unity's compiler would have set for Direct3D 11.
OUTSIDE = """            #define UNITY_REVERSED_Z 1
            #define UNITY_UV_STARTS_AT_TOP 1
            cbuffer UnityPerCamera
            {
                float4 _Time;
                float4 _SinTime;
                float4 _CosTime;
                float4 unity_DeltaTime;
                float3 _WorldSpaceCameraPos;
                float4 _ProjectionParams;
                float4 _ScreenParams;
                float4 _ZBufferParams;
                float4 unity_OrthoParams;
            };
            cbuffer UnityPerDraw
            {
                float4x4 unity_ObjectToWorld;
                float4x4 unity_WorldToObject;
                float4 unity_LODFade;
                float4 unity_WorldTransformParams;
                float4 unity_RenderingLayer;
            };
            cbuffer UnityPerFrame
            {
                float4 glstate_lightmodel_ambient;
                float4 unity_AmbientSky;
                float4 unity_AmbientEquator;
                float4 unity_AmbientGround;
                float4 unity_IndirectSpecColor;
                float4x4 glstate_matrix_projection;
                float4x4 unity_MatrixV;
                float4x4 unity_MatrixInvV;
                float4x4 unity_MatrixVP;
                int unity_StereoEyeIndex;
                float4 unity_ShadowColor;
            };
            #define UNITY_MATRIX_V unity_MatrixV
            #define UNITY_MATRIX_VP unity_MatrixVP
"""

COMMON = """
            // (the GLSL has each of the game's matrices as its four columns; HLSL has them as rows)
            #define hlslcc_mtx4x4unity_ObjectToWorld transpose(unity_ObjectToWorld)
            #define hlslcc_mtx4x4unity_WorldToObject transpose(unity_WorldToObject)
            #define hlslcc_mtx4x4unity_MatrixVP transpose(UNITY_MATRIX_VP)
            #define hlslcc_mtx4x4unity_MatrixV transpose(UNITY_MATRIX_V)
            #define SampleLod(S, uv, lod) S.SampleLevel(sampler##S, uv, lod)
            #define SampleFlat(S, uv) S.SampleLevel(sampler##S, uv, 0.0)
            #define SampleGradient(S, uv, dx, dy) S.SampleGrad(sampler##S, uv, dx, dy)
            #define Mod(x, y) ((x) - (y) * floor((x) / (y)))
            #define GreaterThan(a, b) ((a) > (b))
            #define GreaterThanEqual(a, b) ((a) >= (b))
            #define LessThan(a, b) ((a) < (b))
            #define LessThanEqual(a, b) ((a) <= (b))
            float4 TexelFetch(Texture2D t, int2 p, int lod) { return t.Load(int3(p, lod)); }
            float4 TexelFetch(Texture3D t, int3 p, int lod) { return t.Load(int4(p, lod)); }
            int2 TextureSize(Texture2D t, int lod) { uint w, h; t.GetDimensions(w, h); return int2(w, h); }
            bool AllZero(float4 v) { return !any(v); }
            // (one of a vector's numbers, chosen by a variable, read and given a new value: see chosen in tools/unityshaders/port.py)
            float Pick(float2 v, int n) { return n <= 0 ? v.x : v.y; }
            float Pick(float3 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : v.z; }
            float Pick(float4 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : n == 2 ? v.z : v.w; }
            float2 Put(float2 v, int n, float x) { return float2(n == 0 ? x : v.x, n == 1 ? x : v.y); }
            float3 Put(float3 v, int n, float x) { return float3(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z); }
            float4 Put(float4 v, int n, float x) { return float4(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z, n == 3 ? x : v.w); }
            // gl_FragCoord: which pixel is being drawn, its rows counted from the bottom, as every picture's are read here. Direct3D counts
            // the rows of what it draws into from the top, and the game draws into its pictures upside down to make up for it (saying so with
            // _ProjectionParams.x at -1): so where it is drawing straight to the screen, the row is turned over, or each picture read by pixel
            // (the camera's depth, the smoke drawn small) would be read upside down.
            float4 FragCoord(float4 pos)
            {
            #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x > 0.0) pos.y = _ScreenParams.y - pos.y;
            #endif
                return pos;
            }
"""

BLEND = {0.0: "Zero", 1.0: "One", 2.0: "DstColor", 3.0: "SrcColor", 5.0: "SrcAlpha", 10.0: "OneMinusSrcAlpha"}
CULL = {0.0: "Off", 1.0: "Front", 2.0: "Back"}
ZTEST = {8.0: "Always", 4.0: "LEqual"}


def bracket(text, start):
    """Where the bracket opened at text[start] is closed."""
    depth = 0
    for n in range(start, len(text)):
        if text[n] == "(": depth += 1
        elif text[n] == ")":
            depth -= 1
            if depth == 0: return n
    raise SystemExit("an unclosed bracket in: " + text[start:start + 60])


def arguments(inner):
    """The arguments between a pair of brackets, split at the commas that are not inside others."""
    out, depth, last = [], 0, 0
    for n, c in enumerate(inner):
        if c in "([": depth += 1
        elif c in ")]": depth -= 1
        elif c == "," and depth == 0:
            out.append(inner[last:n]); last = n + 1
    out.append(inner[last:])
    return [a.strip() for a in out]


def braced(text, start):
    """Where the brace opened at text[start] is closed."""
    depth = 0
    for n in range(start, len(text)):
        if text[n] == "{": depth += 1
        elif text[n] == "}":
            depth -= 1
            if depth == 0: return n
    raise SystemExit("an unclosed brace in: " + text[start:start + 60])


def scoped_loops(text):
    """GLSL keeps a loop's counter to the loop; HLSL lets it out into the block round it, where a later one of the same name is then
    defined twice (clouds.glsl, 2026-10-15: `for (int k ...)` and after it `float k`). Each loop that declares its counter is put in
    braces of its own, so that the counter ends with it, as in GLSL."""
    out, pos = [], 0
    pattern = re.compile(r"\[loop\] for \(\s*(?:int|uint|float)\s")
    while True:
        found = pattern.search(text, pos)
        if not found:
            out.append(text[pos:])
            return "".join(out)
        head = bracket(text, text.index("(", found.start()))
        end = statement_end(text, head + 1)
        out.append(text[pos:found.start()] + "{ " + text[found.start():head + 1] + scoped_loops(text[head + 1:end + 1]) + " }")
        pos = end + 1


def statement_end(text, n):
    """Where the statement that begins at or after text[n] ends: a block, a loop or an if with what it governs, or a plain one."""
    while text[n] in " \t\r\n": n += 1
    if text[n] == "{": return braced(text, n)
    found = re.match(r"(?:\[loop\]\s*)?(for|while|if)\s*\(", text[n:])
    if not found: return text.index(";", n)
    end = statement_end(text, bracket(text, n + found.end() - 1) + 1)
    if found.group(1) == "if":
        after = re.match(r"\s*else\b", text[end + 1:])
        if after: end = statement_end(text, end + 1 + after.end())
    return end


def makers(text):
    """float3(x) with one thing in it is not allowed in HLSL: it becomes a cast. And a matrix made from numbers is made row by row there, column by column in GLSL."""
    pattern = re.compile(r"(?<![\w.@])((?:float|int|uint|bool)[234](?:x[234])?)\(")
    while True:
        found = pattern.search(text)
        if not found: break
        kind, open_at = found.group(1), found.end() - 1
        close = bracket(text, open_at)
        inside = arguments(text[open_at + 1:close])
        if len(inside) == 1: new = "((%s)(%s))" % (kind, inside[0])
        elif "x" in kind: new = "transpose(%s@(%s))" % (kind, ", ".join(inside))
        else: new = "%s@(%s)" % (kind, ", ".join(inside))
        text = text[:found.start(1)] + new + text[close + 1:]
    return text.replace("@(", "(")


def uncommented(text):
    return re.sub(r"//[^\n]*", "", text)


def two_argument_atan(text):
    """GLSL's atan(y, x) is HLSL's atan2(y, x) (atan with one argument is the same in both)."""
    pattern = re.compile(r"(?<![\w.])atan\(")
    at = 0
    while True:
        found = pattern.search(text, at)
        if not found: break
        close = bracket(text, found.end() - 1)
        if len(arguments(text[found.end():close])) == 2: text = text[:found.start()] + "atan2(" + text[found.end():]
        at = found.start() + 5
    return text


KIND = r"(?:float|int|uint|bool)(?:[234](?:x[234])?)?"


def declared(text):
    """The names a stretch of HLSL declares: those that are one vector (with which kind), and those that are lists (of anything)."""
    vectors, lists = {}, set()
    text = uncommented(text)
    for found in re.finditer(r"(?<![\w.])(%s)\s+(?=[A-Za-z_])" % KIND, text):
        vector = found.group(1)[-1] in "234" and "x" not in found.group(1)
        at = found.end()
        while True:
            name = re.match(r"\s*([A-Za-z_]\w*)\s*", text[at:])
            if not name or re.fullmatch(KIND + "|in|out|inout|const|static", name.group(1)): break      # (the next of a function's arguments)
            at += name.end()
            if text[at:at + 1] == "(": break                                                          # (a function)
            if text[at:at + 1] == "[": lists.add(name.group(1))
            elif vector: vectors[name.group(1)] = found.group(1)
            # (on to the next name of the same statement, past what this one is set to)
            depth = 0
            while at < len(text):
                c = text[at]
                if c in "([{": depth += 1
                elif c in ")]}":
                    if depth == 0: break
                    depth -= 1
                elif c in ",;" and depth == 0: break
                at += 1
            if text[at:at + 1] != ",": break
            at += 1
    return vectors, lists


def chosen(text, more):
    """One of a vector's numbers chosen by a variable, v[n]: GLSL reads and writes it as it stands. Microsoft's compiler does not:

    * Written to (result[n] = x), it can only be done by writing out, turn by turn, the loop that n comes from, which the
      compiler will not do for a loop marked to be kept as one ("error X3531: can't unroll loops marked with loop attribute").
      So the whole vector is given anew: result = Put(result, n, x).
    * Read (n < 4 ? a[n] : b.x), HLSL works out both sides of the choice, and a counter that runs past the vector's end stops
      the compiler ("error X3504: literal loop terminated early due to out of bounds array access"). So: Pick(a, n), which
      gives the last number for anything past it.

    Both met in the sea's foam (2026-10-11). Lists are left as they are: any place of a list can be read and written.
    (more: the vectors declared outside this stretch, each with its kind.)"""
    vectors, lists = declared(text)
    vectors = dict(more, **vectors)
    if set(vectors) & lists: raise SystemExit("declared both as a vector and as a list: " + ", ".join(sorted(set(vectors) & lists)))
    pattern = re.compile(r"(?<![\w.])([A-Za-z_]\w*)\[")
    at = 0
    while True:
        found = pattern.search(text, at)
        if not found: break
        name, depth, close = found.group(1), 0, None
        for n in range(found.end() - 1, len(text)):
            if text[n] == "[": depth += 1
            elif text[n] == "]":
                depth -= 1
                if depth == 0: close = n; break
        if close is None: raise SystemExit("an unclosed bracket in: " + text[found.start():found.start() + 60])
        index = text[found.end():close]
        written = re.match(r"\s*([-+*/]?)=(?!=)\s*", text[close + 1:])
        at = found.end()
        if name in lists or re.fullmatch(r"\s*\d+\s*", index): continue
        if text.rfind("//", text.rfind("\n", 0, found.start()) + 1, found.start()) >= 0: continue     # (said of the code, not code)
        if name not in vectors:
            if written: raise SystemExit("%s[%s] is written to, and it cannot be told here whether %s is a vector or a list" % (name, index, name))
            continue
        if not re.fullmatch(r"float[234]", vectors[name]): raise SystemExit("%s[%s]: a %s's numbers chosen by a variable are not handled (only vectors of floats)" % (name, index, vectors[name]))
        if written:
            start = close + 1 + written.end()
            end = text.index(";", start)
            value = text[start:end]
            if written.group(1): value = "%s[%s] %s (%s)" % (name, index, written.group(1), value)
            new = "%s = Put(%s, %s, %s)" % (name, name, index, value)
            text = text[:found.start()] + new + text[end:]
            at = found.start() + len("%s = Put(%s, " % (name, name))                               # (the index and the value are gone through in their turn)
        else:
            text = text[:found.start()] + "Pick(%s, %s)" % (name, index) + text[close + 1:]
            at = found.start() + len("Pick(%s, " % name)
    return text


def convert(text, stage, vectors={}, own=False):
    """A stretch of GLSL (functions, or the inside of main) as HLSL. (vectors: the vectors declared outside it, each with its kind;
    own: the shader draws only into pictures of its own, see OWN.)"""
    text = two_argument_atan(text)
    text = re.sub(r"\bivec([234])\b", r"int\1", text)
    text = re.sub(r"\buvec([234])\b", r"uint\1", text)
    text = re.sub(r"\bbvec([234])\b", r"bool\1", text)
    text = re.sub(r"\bvec([234])\b", r"float\1", text)
    text = re.sub(r"\bmat([234])\b", r"float\1x\1", text)
    for was, now in (("textureLod(", "SampleLod("), ("textureGrad(", "SampleGradient("), ("texture(", "SampleFlat("), ("texelFetch(", "TexelFetch("), ("textureSize(", "TextureSize("), ("mix(", "lerp("), ("fract(", "frac("),
                     ("inversesqrt(", "rsqrt("), ("mod(", "Mod("), ("greaterThanEqual(", "GreaterThanEqual("), ("greaterThan(", "GreaterThan("), ("lessThanEqual(", "LessThanEqual("), ("lessThan(", "LessThan("), ("dFdx(", "ddx("), ("dFdy(", "ddy("), ("floatBitsToInt(", "asint(")):
        text = re.sub(r"(?<![\w.])" + re.escape(was), now, text)
    text = makers(text)
    text = chosen(text, vectors)
    # (a list made in one go, float4[8](a, b, ...): HLSL writes it in braces)
    while True:
        found = re.search(r"\b(?:float[234]?|int|uint)\[\d+\]\(", text)
        if not found: break
        end = bracket(text, found.end() - 1)
        text = text[:found.start()] + "{" + text[found.end():end] + "}" + text[end + 1:]
    # (which side of a triangle is seen: handed to frag as a value of its own, see program. Seen on Direct3D 11, 2026-10-11: with
    # the camera under the sea its underside was drawn as the underside and its top as the top, as on OpenGL. The game turns its
    # triangles over where Direct3D draws into a picture upside down, and which side counts as the front goes with them.)
    text = re.sub(r"\bgl_FrontFacing\b", "isFront", text)
    # (which point of the mesh this is, by its number: handed to vert as a value of its own, see program)
    text = re.sub(r"\bgl_VertexID\b", "int(vertexId)", text)
    text = re.sub(r"\bgl_FragCoord\b", "IN.pos" if own else "FragCoord(IN.pos)", text)
    text = re.sub(r"\bgl_Position\b", "OUT.pos", text)
    text = re.sub(r"\bSV_Target0\b", "result", text)
    text = re.sub(r"\bSV_Target1\b", "result1", text)
    text = re.sub(r"\bvs_TEXCOORD(\d)\b", ("OUT" if stage == "vertex" else "IN") + r".vs_TEXCOORD\1", text)
    text = re.sub(r"(\n\s*)for \(", r"\1[loop] for (", text)
    text = scoped_loops(text)
    for left in ("textureLod", "texelFetch", "textureSize", "gl_", "vec2", "vec3", "vec4", "mat2", "mat3", "mat4"):
        if re.search(r"(?<![\w.])" + re.escape(left) + r"\b", uncommented(text)): raise SystemExit("still GLSL after conversion: " + left)
    return text


def section(glsl, which):
    """The text of one stage of a GLSL file."""
    text = glsl.split("#ifdef " + which.upper())[1]
    return text[:text.index("\n#endif")]


def stage_of(text):
    """One stage taken apart: what it reads, what it is handed and hands on, and its functions."""
    uniforms, textures, matrices, ins, outs, statics, body = [], [], [], [], [], [], []
    depth = 0
    for line in text.split("\n"):
        bare = line.split("//")[0].strip()
        if depth == 0:
            if bare.startswith("#version") or bare.startswith("#extension"): continue
            found = re.match(r"uniform vec4 hlslcc_mtx4x4(\w+)\[4\];", bare)
            if found: matrices.append(found.group(1)); continue
            found = re.match(r"uniform sampler([23])D (\w+);", bare)
            if found: textures.append((found.group(2), int(found.group(1)), line.split("//", 1)[1].strip() if "//" in line else "")); continue
            # (a stack of flat textures: Natural Ocean's waves. Added 2026-10-11 on the Mac; compiled and seen on Windows the same day.)
            found = re.match(r"uniform sampler2DArray (\w+);", bare)
            if found: textures.append((found.group(1), "2DArray", line.split("//", 1)[1].strip() if "//" in line else "")); continue
            found = re.match(r"uniform (\w+) (\w+);", bare)
            if found: uniforms.append((found.group(2), found.group(1), line.split("//", 1)[1].strip() if "//" in line else "")); continue
            found = re.match(r"(in|out) (\w+) (\w+);", bare)
            if found: (ins if found.group(1) == "in" else outs).append((found.group(3), found.group(2))); continue
            # (an output given its place, for a shader that draws into two pictures at once)
            found = re.match(r"layout\(location\s*=\s*(\d)\)\s*out (\w+) (\w+);", bare)
            if found: outs.append((found.group(3), found.group(2))); continue
            found = re.match(r"(float|vec[234]|int|bool) (\w+);", bare)
            if found: statics.append((found.group(2), found.group(1))); continue
            # (one of the stage's own with a first value, several on one line, or a list, as clouds.glsl's heapsMiddle and heapsHalf
            # and formRaw[2]: kept where it is, as static, since HLSL takes a variable outside a function for a uniform, which cannot
            # be changed. 2026-10-15)
            found = re.match(r"((?:float|int|uint|bool)|(?:[iu]?vec[234])|(?:mat[234]))\s+\w+\s*(?:\[\d+\]\s*)?[=,;]", bare)
            if found: line = line.replace(found.group(1), "static " + found.group(1), 1)
        depth += bare.count("{") - bare.count("}")
        body.append(line)
    body = "\n".join(body)
    start = body.index("void main()")
    opened = body.index("{", start)
    level, closed = 0, None
    for n in range(opened, len(body)):
        if body[n] == "{": level += 1
        elif body[n] == "}":
            level -= 1
            if level == 0: closed = n; break
    return {"uniforms": uniforms, "textures": textures, "matrices": matrices, "ins": ins, "outs": outs, "statics": statics,
            "helpers": body[:start].strip("\n"), "main": body[opened + 1:closed].strip("\n")}


def kind(glsl_type):
    return {"vec2": "float2", "vec3": "float3", "vec4": "float4", "float": "float", "int": "int", "bool": "bool", "mat4": "float4x4"}[glsl_type]


def renamed(text, names):
    """Our own names that clash, given an underscore: in the code, not in what is said about it."""
    out = []
    for line in text.split("\n"):
        code, mark, note = line.partition("//")
        for name in names: code = re.sub(r"(?<![\w.])" + name + r"\b(?!\s*\()", name + "_", code)
        out.append(code + mark + note)
    return "\n".join(out)


def indented(text, by):
    return "\n".join((by + line if line.strip() and not line.lstrip().startswith("#") else line.lstrip() if line.lstrip().startswith("#") else line) for line in text.split("\n"))


def texture_parameters(stage):
    """A function that takes a texture (a sampler2D or sampler3D in GLSL) takes it in HLSL as the texture and its sampler: each such
    parameter becomes two (map: Texture2D map, SamplerState samplermap, as SampleLod and its kin name a texture's sampler), and every
    call hands on the sampler of the texture it names (_CloudMap: _CloudMap, sampler_CloudMap, as the uniforms' are declared). First
    needed by clouds.glsl's mapSmooth (2026-10-15)."""
    takes = {}

    def definition(found):
        params, new, at = arguments(found.group(3)), [], []
        for k, p in enumerate(params):
            texture = re.fullmatch(r"(?:in\s+)?sampler([23])D\s+(\w+)", p)
            if texture:
                new.append("Texture%sD %s, SamplerState sampler%s" % (texture.group(1), texture.group(2), texture.group(2)))
                at.append(k)
            else:
                new.append(p)
        if not at: return found.group(0)
        takes[found.group(2)] = at
        return "%s %s(%s)" % (found.group(1), found.group(2), ", ".join(new))

    stage["helpers"] = re.sub(r"\b(\w+)\s+(\w+)\s*\(([^()]*\bsampler[23]D\b[^()]*)\)", definition, stage["helpers"])
    for key in ("helpers", "main"):
        text = stage[key]
        for name, at in takes.items():
            call, pos = re.compile(r"(?<![\w.])" + re.escape(name) + r"\s*\("), 0
            while True:
                found = call.search(text, pos)
                if not found: break
                open_at = found.end() - 1
                close = bracket(text, open_at)
                args = arguments(text[open_at + 1:close])
                if any(a.startswith(("Texture2D ", "Texture3D ")) for a in args):
                    pos = close + 1          # (the definition itself)
                    continue
                for k in sorted(at, reverse=True):
                    if not re.fullmatch(r"\w+", args[k]): raise SystemExit("%s: a texture handed on as something other than a name: %s" % (name, args[k]))
                    args[k:k + 1] = [args[k], "sampler" + args[k]]
                new = "%s(%s)" % (name, ", ".join(args))
                text = text[:found.start()] + new + text[close + 1:]
                pos = found.start() + len(new)
        stage[key] = text


def program(which, header):
    """The program itself, as HLSL (the parts of what goes between CGPROGRAM's pragmas and ENDCG, to be joined by lines), with the
    given header first: Unity's own (UNITYCG) for the editor, or ours (OUTSIDE) for a compiler outside it. And the names that
    clashed with HLSL's, which were given an underscore."""
    spec = make_bundle.SHADERS[which]
    glsl = open(os.path.join(PACK, spec["code"])).read()
    texts = {"vertex": section(glsl, "vertex"), "fragment": section(glsl, "fragment")}
    for shaders, stage, was, now in SPECIAL:
        if which not in shaders: continue
        if texts[stage].count(was) != 1: raise SystemExit("%s.glsl (%s): expected exactly one line reading: %s" % (which, stage, was.strip()))
        texts[stage] = texts[stage].replace(was, now)
    vertex, fragment = stage_of(texts["vertex"]), stage_of(texts["fragment"])

    # Our own names that mean something else in HLSL.
    code = uncommented(vertex["helpers"] + vertex["main"] + fragment["helpers"] + fragment["main"])
    used = set(re.findall(r"(?<![\w.])([A-Za-z_]\w*)\b(?!\s*\()", code))
    clash = sorted(n for n in used if (n in RESERVED and n not in ("static", "uint", "double")) or HLSL_TYPES.fullmatch(n))
    # (those the replacements above put in on purpose are HLSL already)
    clash = [n for n in clash if n not in ("mul",)]
    for part in (vertex, fragment):
        for key in ("helpers", "main"): part[key] = renamed(part[key], clash)
        texture_parameters(part)

    seen, declared = set(), []
    for stage in (vertex, fragment):
        for name, glsl_type, note in stage["uniforms"]:
            if name in GIVEN or name in seen: continue
            seen.add(name); declared.append("            %s %s;%s" % (kind(glsl_type), name, "     // " + note if note else ""))
    textures = []
    for stage in (vertex, fragment):
        for name, dims, note in stage["textures"]:
            if name in seen: continue
            seen.add(name); textures.append("            Texture%s %s; SamplerState sampler%s;%s" % (dims if dims == "2DArray" else "%dD" % dims, name, name, "     // " + note if note else ""))
    statics = ["            static %s %s;" % (kind(t), n) for n, t in fragment["statics"] + vertex["statics"]]
    passed = ["                float4 pos : SV_POSITION;"] + ["                %s %s : TEXCOORD%s;" % (kind(t), n, n[-1]) for n, t in vertex["outs"]]

    # (the vectors each stage's code can name without declaring them: for chosen)
    outside = {}
    for stage in (vertex, fragment):
        for name, glsl_type, _ in stage["uniforms"]:
            if re.fullmatch(r"[iub]?vec[234]", glsl_type): outside[name] = {"v": "float", "i": "int", "u": "uint", "b": "bool"}[glsl_type[0]] + glsl_type[-1]
        for name, glsl_type in stage["statics"]:
            if re.fullmatch(r"vec[234]", glsl_type): outside[name + "_" if name in clash else name] = "float" + glsl_type[-1]

    out = [header + COMMON]
    out.append("\n".join(declared))
    out.append("\n".join(textures))
    if statics: out.append("\n".join(statics))
    out.append("\n            struct v2f\n            {\n%s\n            };\n" % "\n".join(passed))
    if vertex["helpers"].strip(): out.append(indented(convert(vertex["helpers"], "vertex", outside, which in OWN), "            ") + "\n")
    numbered = ", uint vertexId : SV_VertexID" if re.search(r"\bgl_VertexID\b", uncommented(vertex["main"] + vertex["helpers"])) else ""
    out.append("            v2f vert(float3 in_POSITION0 : POSITION%s)\n            {\n                v2f OUT = (v2f)0;" % numbered)
    out.append(indented(re.sub(r"\breturn;", "return OUT;", convert(vertex["main"], "vertex", outside, which in OWN)), "            "))
    out.append("                return OUT;\n            }\n")
    if fragment["helpers"].strip(): out.append(indented(convert(fragment["helpers"], "fragment", outside, which in OWN), "            ") + "\n")
    if any(name == "SV_Target1" for name, _ in fragment["outs"]):
        # (a second picture drawn into at the same time: see volume.glsl's SV_Target1)
        out.append("            struct both\n            {\n                float4 result : SV_Target0;\n                float4 result1 : SV_Target1;\n            };\n")
        out.append("            both Both(float4 a, float4 b) { both o; o.result = a; o.result1 = b; return o; }\n")
        out.append("            both frag(v2f IN)\n            {\n                float4 result = float4(0.0, 0.0, 0.0, 0.0);\n                float4 result1 = float4(0.0, 0.0, 0.0, 0.0);")
        out.append(indented(re.sub(r"\breturn;", "return Both(result, result1);", convert(fragment["main"], "fragment", outside, which in OWN)), "            "))
        out.append("                return Both(result, result1);\n            }")
    else:
        facing = ", bool isFront : SV_IsFrontFace" if re.search(r"\bgl_FrontFacing\b", uncommented(fragment["main"] + fragment["helpers"])) else ""
        # (a program that writes its own depth, as drops.glsl does, gives it as a second output: SV_Depth)
        depth = ", out float fragDepth : SV_Depth" if re.search(r"\bgl_FragDepth\b", uncommented(fragment["main"])) else ""
        out.append("            float4 frag(v2f IN%s%s) : SV_Target\n            {\n                float4 result = float4(0.0, 0.0, 0.0, 0.0);" % (facing, depth))
        if depth: out.append("                fragDepth = 0.0;")
        out.append(indented(re.sub(r"\bgl_FragDepth\b", "fragDepth", re.sub(r"\breturn;", "return result;", convert(fragment["main"], "fragment", outside, which in OWN))), "            "))
        out.append("                return result;\n            }")
    return out, clash


def hlsl(which):
    """The program as plain HLSL for a compiler outside Unity: its vertex program is vert, its pixel program frag."""
    spec = make_bundle.SHADERS[which]
    out, _ = program(which, OUTSIDE)
    return "// %s: made from tools/shaderpack/%s by tools/unityshaders/port.py.\n%s\n" % (spec["name"], spec["code"], "\n".join(out))


def port(which):
    spec = make_bundle.SHADERS[which]
    properties = ["        %s (\"%s\", %s) = \"\" {}" % (name, name[1:], "3D" if dims == 3 else "2DArray" if dims == 5 else "2D") for name, dims in spec["textures"][:-spec.get("given", 1)]]
    source, dest = spec.get("blend", (1.0, 10.0))
    body, clash = program(which, UNITYCG)

    out = []
    out.append("// %s: made from tools/shaderpack/%s by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again." % (spec["name"], spec["code"]))
    out.append("Shader \"%s\"\n{\n    Properties\n    {\n%s\n    }\n    SubShader\n    {" % (spec["name"], "\n".join(properties)))
    out.append("        Tags { \"Queue\" = \"Transparent\" \"RenderType\" = \"Transparent\" \"IgnoreProjector\" = \"True\" }\n        Pass\n        {")
    out.append("            Blend %s %s\n            ZWrite %s\n            ZTest %s\n            Cull %s\n" % (BLEND[source], BLEND[dest], "On" if spec.get("zwrite", 0.0) else "Off", ZTEST[spec.get("ztest", 8.0)], CULL[spec.get("cull", 1.0)]))
    out.append("            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #pragma target 4.5\n            #pragma only_renderers d3d11 glcore metal vulkan")
    out += body
    out.append("            ENDCG\n        }\n    }\n}")
    folder = os.path.join(HERE, "Assets", FOLDER[which])
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, spec["name"].split("/")[1] + ".shader")
    open(path, "w", newline="\n").write("\n".join(out) + "\n")
    print("wrote %s (%d lines)%s" % (os.path.relpath(path, os.path.join(HERE, "..", "..")), "\n".join(out).count("\n") + 1, "; renamed: " + ", ".join(clash) if clash else ""))


if __name__ == "__main__":
    asked = sys.argv[1:]
    for name in asked or ["volume", "enlarge", "layers", "motion", "mark", "shock", "shadow", "lens", "shafts", "clean", "veil", "clouds", "cloudsover", "cloudshade", "wet",
                          "ocean", "oceanfoam", "oceanweed", "oceanunder", "drops"]:
        # (a repository of one mod has that mod's shaders and not the others': those are passed over)
        if not asked and not os.path.exists(os.path.join(PACK, make_bundle.SHADERS[name]["code"])):
            print("left out: %s (tools/shaderpack/%s is not here)" % (name, make_bundle.SHADERS[name]["code"]))
            continue
        port(name)
