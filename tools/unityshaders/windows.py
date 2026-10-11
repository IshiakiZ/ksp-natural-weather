#!/usr/bin/env python3
"""The mods' shaders for Direct3D 11, the Windows version of the game, made without the Unity editor (route A of WITHOUT-UNITY.md).

    python3 tools/unityshaders/windows.py "<KSP folder>"

On Windows only. Every shader of tools/shaderpack is turned into HLSL (port.py), compiled by Windows' own compiler
(d3dcompiler_47.dll, which is part of Windows: nothing is installed), created once by Direct3D 11 itself as a check, and
packed into the bundles the mods look for when the game runs on Direct3D:

    src/VolumetricExplosions/Shaders/shaders-windows.bundle   the explosions' six: Volume, Enlarge, Layers, Mark, Shock, Shadow
    src/Keystone/Shaders/lens-windows.bundle                   Keystone's lens
    src/NaturalLight/Shaders/light-windows.bundle             Natural Light's three: Shafts, Clean, Veil
    src/NaturalWeather/Shaders/clouds-windows.bundle          Natural Weather's clouds: Clouds, CloudsOver, CloudShade

Run it again after any change to a GLSL file (so after each `git pull origin main`): it makes every bundle afresh from the
GLSL as it is. The game folder is only read, for a pattern, as tools/shaderpack/make_bundle.py does for OpenGL: the game's own
UnlitAlpha, which carries Direct3D 11 programs of its own, and the index of one of its bundles. What the compiler made and
said is left in build/windows-shaders/: each shader's HLSL, its two programs as text, and the compiler's messages.

How a Direct3D 11 program sits in a bundle was worked out from the game's own (tools/unityshaders/d3d11.py, WITHOUT-UNITY.md):
each stage lists the constant buffers it uses (a material's values in $Globals, Unity's own in UnityPerCamera, UnityPerDraw
and UnityPerFrame), with only the values it uses, at the places the compiler gave them, which are read here from the
compiler's own description of the program (its RDEF chunk); each texture its slot and its sampler's.
"""
import copy
import ctypes
import hashlib
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(HERE, "..", "shaderpack"))
sys.path.insert(0, HERE)
import d3d11  # noqa: E402
import glprogram  # noqa: E402
import make_bundle  # noqa: E402
import port  # noqa: E402
import typetree  # noqa: E402
from unityfile import Asset, Bundle, Writer, lz4_store  # noqa: E402

# The bundles, each with the shaders in it, as the mods look for them (Addon.BuiltShader, Lens.OwnLens, OwnShader.Load,
# Clouds.Load).
BUNDLES = [
    ("src/VolumetricExplosions/Shaders/shaders-windows.bundle", ["volume", "enlarge", "layers", "mark", "shock", "shadow"]),
    ("src/Keystone/Shaders/lens-windows.bundle", ["lens"]),
    ("src/NaturalLight/Shaders/light-windows.bundle", ["shafts", "clean", "veil"]),
    ("src/NaturalWeather/Shaders/clouds-windows.bundle", ["clouds", "cloudsover", "cloudshade"]),
]
OUT = os.path.join(ROOT, "build", "windows-shaders")

# Unity's own buffers, as every shader surveyed has them (WITHOUT-UNITY.md): their sizes, and where each of the values our
# shaders read sits. Unity fills them itself; a value anywhere else would be read from the wrong place.
UNITY = {
    "UnityPerCamera": (144, {"_Time": 0, "_WorldSpaceCameraPos": 64, "_ProjectionParams": 80, "_ScreenParams": 96, "_ZBufferParams": 112}),
    "UnityPerDraw": (176, {"unity_ObjectToWorld": 0, "unity_WorldToObject": 64, "unity_WorldTransformParams": 144}),
    "UnityPerFrame": (368, {"glstate_matrix_projection": 80, "unity_MatrixV": 144, "unity_MatrixInvV": 208, "unity_MatrixVP": 272}),
}

D3DCOMPILE_OPTIMIZATION_LEVEL3 = 1 << 15
# Unity's numbers for a Direct3D 11 program, by stage and shader model.
PROGRAM_TYPE = {("vs", "4_0"): 15, ("vs", "5_0"): 16, ("ps", "4_0"): 17, ("ps", "5_0"): 18}
# Direct3D's kinds of binding (D3D_SHADER_INPUT_TYPE) and texture shapes (D3D_SRV_DIMENSION, as Unity's TextureDimension).
CBUFFER, TEXTURE, SAMPLER = 0, 2, 3
SHAPE = {4: 2, 8: 3, 9: 4}


# ---------------------------------------------------------------- Windows' own compiler, and Direct3D 11 itself

def system_dll(name):
    path = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32", name)
    if not os.path.exists(path): raise SystemExit("%s is not in this Windows (looked for %s)" % (name, path))
    return path, ctypes.WinDLL(path)


def version_of(path):
    """A file's version, as Windows' Explorer shows it."""
    v = ctypes.WinDLL("version")
    size = v.GetFileVersionInfoSizeW(path, None)
    if not size: return "?"
    info = ctypes.create_string_buffer(size)
    v.GetFileVersionInfoW(path, 0, size, info)
    fixed, length = ctypes.c_void_p(), ctypes.c_uint()
    if not v.VerQueryValueW(info, "\\", ctypes.byref(fixed), ctypes.byref(length)): return "?"
    ms, ls = struct.unpack_from("<II", ctypes.string_at(fixed.value, length.value), 8)
    return "%d.%d.%d.%d" % (ms >> 16, ms & 0xFFFF, ls >> 16, ls & 0xFFFF)


def method(obj, index, restype, *argtypes):
    """Method number `index` of a COM object (its table of functions, in the order of its interface)."""
    table = ctypes.cast(obj, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p)))[0]
    return ctypes.WINFUNCTYPE(restype, ctypes.c_void_p, *argtypes)(table[index])


def blob(obj):
    """What an ID3DBlob holds (and the blob let go)."""
    if not obj: return b""
    data = ctypes.string_at(method(obj, 3, ctypes.c_void_p)(obj), method(obj, 4, ctypes.c_size_t)(obj))
    method(obj, 2, ctypes.c_ulong)(obj)
    return data


class Compiler:
    def __init__(self):
        self.path, dll = system_dll("d3dcompiler_47.dll")
        self.version = version_of(self.path)
        self.compile = dll.D3DCompile
        self.compile.restype = ctypes.c_long
        self.compile.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
                                 ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
        self.disassemble = dll.D3DDisassemble
        self.disassemble.restype = ctypes.c_long
        self.disassemble.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]

    def __call__(self, source, name, entry, profile):
        """(bytecode or None, the compiler's messages)."""
        code, said = ctypes.c_void_p(), ctypes.c_void_p()
        failed = self.compile(source, len(source), name.encode(), None, None, entry.encode(), profile.encode(), D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
                              ctypes.byref(code), ctypes.byref(said))
        messages = blob(said.value).decode("utf-8", "replace").rstrip("\0")
        return (None if failed else blob(code.value)), messages

    def text(self, dxbc):
        listing = ctypes.c_void_p()
        if self.disassemble(dxbc, len(dxbc), 0, None, ctypes.byref(listing)): return "(could not be turned into text)"
        return blob(listing.value).decode("utf-8", "replace").rstrip("\0")


class Direct3D:
    """A Direct3D 11 device, only to have it create each program (and the vertex programs' input), as the game will: the
    graphics card's driver if it will make one, else Windows' own software one (WARP)."""

    def __init__(self):
        _, dll = system_dll("d3d11.dll")
        create = dll.D3D11CreateDevice
        create.restype = ctypes.c_long
        create.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_uint, ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint,
                           ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_void_p)]
        self.device = ctypes.c_void_p()
        for driver, what in ((1, "the graphics card's"), (5, "WARP, Windows' own")):
            level, context = ctypes.c_int(), ctypes.c_void_p()
            if create(None, driver, None, 0, None, 0, 7, ctypes.byref(self.device), ctypes.byref(level), ctypes.byref(context)) == 0:
                self.what = "%s, feature level %d.%d" % (what, level.value >> 12, (level.value >> 8) & 0xF)
                method(context.value, 2, ctypes.c_ulong)(context.value)
                return
        raise SystemExit("Direct3D 11 would not make a device here")

    def accepts(self, dxbc, stage):
        """Nothing if Direct3D creates the program; else what it said (an HRESULT)."""
        made = ctypes.c_void_p()
        index = 12 if stage == "vs" else 15                              # ID3D11Device::CreateVertexShader, ::CreatePixelShader
        result = method(self.device.value, index, ctypes.c_long, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p))(
            self.device.value, dxbc, len(dxbc), None, ctypes.byref(made))
        if result: return "refused (0x%08X)" % (result & 0xFFFFFFFF)
        method(made.value, 2, ctypes.c_ulong)(made.value)
        if stage == "vs":
            # (the mod's meshes give positions alone, three numbers each: the layout Unity will make for them)
            class Element(ctypes.Structure):
                _fields_ = [("name", ctypes.c_char_p), ("index", ctypes.c_uint), ("format", ctypes.c_int), ("slot", ctypes.c_uint), ("offset", ctypes.c_uint),
                            ("kind", ctypes.c_int), ("step", ctypes.c_uint)]
            position = Element(b"POSITION", 0, 6, 0, 0, 0, 0)                 # DXGI_FORMAT_R32G32B32_FLOAT, per vertex
            layout = ctypes.c_void_p()
            result = method(self.device.value, 11, ctypes.c_long, ctypes.POINTER(Element), ctypes.c_uint, ctypes.c_char_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_void_p))(
                self.device.value, ctypes.byref(position), 1, dxbc, len(dxbc), ctypes.byref(layout))
            if result: return "its input refused (0x%08X)" % (result & 0xFFFFFFFF)
            method(layout.value, 2, ctypes.c_ulong)(layout.value)
        return None


# ---------------------------------------------------------------- what a program uses, from the compiler's own description of it

def reflect(dxbc):
    """(constant buffers, bindings) of a compiled program, read from its RDEF chunk (the layout is public: Wine's and vkd3d's
    sources, among others). A buffer: {name, size, values: [{name, offset, size, used, class, type, rows, cols, elements}]};
    a binding: {name, kind, shape, slot, count}."""
    rdef = dict(d3d11.chunks(dxbc))["RDEF"]
    buffers_n, buffers_at, bindings_n, bindings_at = struct.unpack_from("<4I", rdef, 0)
    major = rdef[17]

    def text(at): return rdef[at:rdef.index(b"\0", at)].decode()

    bindings = []
    for n in range(bindings_n):
        name_at, kind, _, shape, _, slot, count, _ = struct.unpack_from("<8I", rdef, bindings_at + 32 * n)
        bindings.append({"name": text(name_at), "kind": kind, "shape": shape, "slot": slot, "count": count})
    step = 40 if major >= 5 else 24                                      # (shader model 5 adds four numbers to each value)
    buffers = []
    for n in range(buffers_n):
        name_at, values_n, values_at, size, _, _ = struct.unpack_from("<6I", rdef, buffers_at + 24 * n)
        values = []
        for m in range(values_n):
            value_at, offset, value_size, flags, type_at, _ = struct.unpack_from("<6I", rdef, values_at + step * m)
            cls, kind, rows, cols, elements, _ = struct.unpack_from("<6H", rdef, type_at)
            values.append({"name": text(value_at), "offset": offset, "size": value_size, "used": bool(flags & 2), "class": cls, "type": kind,
                           "rows": rows, "cols": cols, "elements": elements})
        buffers.append({"name": text(name_at), "size": size, "values": values})
    return buffers, bindings


def stats(dxbc):
    """Unity's four numbers for a program (shown in the editor only): instructions that are not texture reads or flow, texture
    reads, branches, temporary registers. From the compiler's STAT chunk (its words as Wine's reader of it names them: 0 the
    instructions, 1 the temporary registers, 7 and 8 static and dynamic flow, 14 to 18 the five kinds of texture read)."""
    stat = dict(d3d11.chunks(dxbc))["STAT"]
    s = struct.unpack_from("<%dI" % (len(stat) // 4), stat, 0)
    textures = s[14] + s[15] + s[16] + s[17] + s[18]
    flow = s[7] + s[8]
    return [max(s[0] - textures - flow - 1, 0), textures, flow, s[1]]


def uses(which, stage, dxbc):
    """What one stage of a shader binds and reads, checked against what the shader is known to read (make_bundle.SHADERS, and
    Unity's own buffers as Unity lays them out): any difference stops this, rather than make a program that reads the wrong place."""
    spec = make_bundle.SHADERS[which]
    ours = {n: (c, m) for n, c, m in spec["fragment"] + spec.get("vertex", make_bundle.VERTEX)}
    shapes = dict(spec["textures"])
    buffers, bindings = reflect(dxbc)
    sizes = {b["name"]: b for b in buffers}
    samplers = {b["name"]: b["slot"] for b in bindings if b["kind"] == SAMPLER}
    groups, textures = [], []
    for b in sorted((b for b in bindings if b["kind"] == CBUFFER), key=lambda b: b["slot"]):
        buffer = sizes[b["name"]]
        if b["name"] in UNITY and buffer["size"] != UNITY[b["name"]][0]:
            raise SystemExit("%s (%s): Unity's %s is %d bytes, not %d" % (which, stage, b["name"], UNITY[b["name"]][0], buffer["size"]))
        if b["name"] not in UNITY and b["name"] != "$Globals": raise SystemExit("%s (%s): a buffer of its own, %s, that Unity would not fill" % (which, stage, b["name"]))
        params = []
        for v in buffer["values"]:
            if not v["used"]: continue
            if v["type"] != 3 or v["elements"] or v["class"] not in (0, 1, 3):
                raise SystemExit("%s (%s): %s is not a plain number, vector or matrix of numbers" % (which, stage, v["name"]))
            matrix = v["class"] == 3
            if b["name"] == "$Globals":
                if v["name"] not in ours: raise SystemExit("%s (%s): reads %s, which make_bundle.py does not list for it" % (which, stage, v["name"]))
                if ours[v["name"]] != (v["cols"], matrix): raise SystemExit("%s (%s): %s is not of the shape make_bundle.py has for it" % (which, stage, v["name"]))
            elif UNITY[b["name"]][1].get(v["name"]) != v["offset"]:
                raise SystemExit("%s (%s): Unity's %s is not where Unity keeps it in %s" % (which, stage, v["name"], b["name"]))
            params.append({"name": v["name"], "type": 0, "rows": 4 if matrix else 1, "cols": v["cols"], "matrix": 1 if matrix else 0, "array": 0, "index": v["offset"]})
        groups.append({"name": b["name"], "size": buffer["size"], "slot": b["slot"], "params": params})
    for b in sorted((b for b in bindings if b["kind"] == TEXTURE), key=lambda b: b["slot"]):
        if b["name"] not in shapes: raise SystemExit("%s (%s): reads a picture, %s, that make_bundle.py does not list for it" % (which, stage, b["name"]))
        if SHAPE.get(b["shape"]) != shapes[b["name"]] or b["count"] != 1: raise SystemExit("%s (%s): %s is not of the shape make_bundle.py has for it" % (which, stage, b["name"]))
        textures.append({"name": b["name"], "slot": b["slot"], "sampler": samplers.pop("sampler" + b["name"], -1), "shape": shapes[b["name"]]})
    if samplers: raise SystemExit("%s (%s): samplers that go with no picture: %s" % (which, stage, ", ".join(samplers)))
    return groups, textures


# ---------------------------------------------------------------- one shader, compiled

def compiled(which, compiler, device):
    """{stage: (dxbc, profile)} for one shader, its HLSL and what was said written into build/windows-shaders."""
    source = port.hlsl(which)
    name = make_bundle.SHADERS[which]["name"].split("/")[1]
    open(os.path.join(OUT, name + ".hlsl"), "w", newline="\n").write(source)
    out, log = {}, []
    for stage, entry in (("vs", "vert"), ("ps", "frag")):
        for model in ("4_0", "5_0"):
            dxbc, said = compiler(source.encode(), name + ".hlsl", entry, stage + "_" + model)
            log.append("== %s_%s (%s): %s\n%s" % (stage, model, entry, "compiled" if dxbc else "FAILED", said))
            if dxbc: break
        if not dxbc:
            open(os.path.join(OUT, name + ".log"), "w", newline="\n").write("\n".join(log))
            raise SystemExit("%s: the %s program does not compile (build/windows-shaders/%s.log):\n%s" % (which, "vertex" if stage == "vs" else "pixel", name, said))
        refused = device.accepts(d3d11.strip(dxbc), stage)
        if refused: raise SystemExit("%s: Direct3D 11 %s the %s program" % (which, refused, "vertex" if stage == "vs" else "pixel"))
        open(os.path.join(OUT, "%s.%s.txt" % (name, stage)), "w", newline="\n").write(compiler.text(dxbc))
        out[stage] = (dxbc, model)
    open(os.path.join(OUT, name + ".log"), "w", newline="\n").write("\n".join(log))
    return out


# ---------------------------------------------------------------- packing: Unity's Shader objects, and the bundle

def packed_shader(which, programs, pattern, number_base):
    """One Shader object (as Python data, to be written through the pattern's type tree) with Direct3D 11 programs alone."""
    spec = make_bundle.SHADERS[which]
    shader = copy.deepcopy(pattern)
    form = shader["m_ParsedForm"]
    form["m_Name"] = spec["name"]
    texture_prop = copy.deepcopy(form["m_PropInfo"]["m_Props"][0])
    props = []
    for name, dims in spec["textures"][:-spec.get("given", 1)]:
        prop = copy.deepcopy(texture_prop)
        prop["m_Name"], prop["m_Description"] = name, name[1:]
        prop["m_DefTexture"] = {"m_DefaultName": "", "m_TexDim": dims}
        props.append(prop)
    form["m_PropInfo"]["m_Props"] = props

    stages = {stage: uses(which, stage, dxbc) for stage, (dxbc, _) in programs.items()}
    names = []
    for stage in ("vs", "ps"):
        groups, textures = stages[stage]
        for g in groups: names += [g["name"]] + [p["name"] for p in g["params"]]
        names += [t["name"] for t in textures]
    number = {}
    for name in names: number.setdefault(name, len(number))

    one = form["m_SubShaders"][0]["m_Passes"][0]
    one["m_NameIndices"] = [{"first": name, "second": number[name]} for name in sorted(number)]
    state = one["m_State"]
    source, dest = spec.get("blend", (1.0, 10.0))
    for key, value in (("srcBlend", source), ("destBlend", dest), ("srcBlendAlpha", source), ("destBlendAlpha", dest)):
        state["rtBlend0"][key]["val"] = value
    state["zWrite"]["val"] = 0.0
    state["zTest"]["val"] = spec.get("ztest", 8.0)
    state["culling"]["val"] = spec.get("cull", 1.0)
    state["gpuProgramID"] = spec["program"] + number_base

    entries = []
    for blob_index, (stage, key) in enumerate((("vs", "progVertex"), ("ps", "progFragment"))):
        dxbc, model = programs[stage]
        groups, textures = stages[stage]
        kind = PROGRAM_TYPE[(stage, model)]
        sub = copy.deepcopy([s for s in one[key]["m_SubPrograms"] if s["m_GpuProgramType"] in (15, 17)][0])
        sub["m_BlobIndex"] = blob_index
        sub["m_GpuProgramType"] = kind
        sub["m_Channels"] = {"m_Channels": [{"source": 0, "target": 0}], "m_SourceMap": 1} if stage == "vs" else {"m_Channels": [], "m_SourceMap": 0}
        sub["m_VectorParams"], sub["m_MatrixParams"], sub["m_BufferParams"], sub["m_UAVParams"], sub["m_Samplers"] = [], [], [], [], []
        sub["m_TextureParams"] = [{"m_NameIndex": number[t["name"]], "m_Index": t["slot"], "m_SamplerIndex": t["sampler"], "m_MultiSampled": False, "m_Dim": t["shape"]} for t in textures]
        sub["m_ConstantBuffers"] = [{"m_NameIndex": number[g["name"]],
                                     "m_MatrixParams": [{"m_NameIndex": number[p["name"]], "m_Index": p["index"], "m_ArraySize": 0, "m_Type": 0, "m_RowCount": 4} for p in g["params"] if p["matrix"]],
                                     "m_VectorParams": [{"m_NameIndex": number[p["name"]], "m_Index": p["index"], "m_ArraySize": 0, "m_Type": 0, "m_Dim": p["cols"]} for p in g["params"] if not p["matrix"]],
                                     "m_StructParams": [], "m_Size": g["size"]} for g in groups]
        sub["m_ConstantBufferBindings"] = [{"m_NameIndex": number[g["name"]], "m_Index": g["slot"]} for g in groups]
        one[key]["m_SubPrograms"] = [sub]

        bindings = [{"name": t["name"], "type": 0, "index": t["slot"], "extra": t["sampler"], "texture": t["shape"] << 1} for t in textures]
        bindings += [{"name": g["name"], "type": 1, "index": g["slot"], "extra": 0} for g in groups]
        entry = {"version": glprogram.VERSION, "type": kind, "stats": stats(dxbc), "source_map": 1 if stage == "vs" else 0,
                 "channels": [(0, 0)] if stage == "vs" else [],
                 "groups": [{"name": "", "size": 0, "params": []}] + [{"name": g["name"], "size": g["size"], "params": g["params"]} for g in groups],
                 "bindings": bindings}
        stripped = d3d11.strip(dxbc)
        entry["code"] = d3d11.head(entry) + stripped
        entries.append(glprogram.write_entry(entry))
    for stage in ("progGeometry", "progHull", "progDomain", "progRayTracing"):
        if stage in one: one[stage]["m_SubPrograms"] = []

    blob_bytes = glprogram.write_blob(entries)
    packed = lz4_store(blob_bytes)
    shader["platforms"] = [d3d11.D3D11]
    shader["offsets"] = [[0]]
    shader["compressedLengths"] = [[len(packed)]]
    shader["decompressedLengths"] = [[len(blob_bytes)]]
    shader["compressedBlob"] = packed
    return shader


def write_bundle(path, which_ones, programs, ksp):
    shader_pattern, shader_tree, shader_type = make_bundle.pattern(ksp, "makinghistory_scene", 48, lambda v: v["m_ParsedForm"]["m_Name"] == "UnlitAlpha")
    index, index_tree, index_type = make_bundle.pattern(ksp, "serenity.kspexpansion", 142, lambda v: True)
    if 4 not in shader_pattern["platforms"]: raise SystemExit("the game's UnlitAlpha has no Direct3D 11 programs to be a pattern")
    name = os.path.splitext(os.path.basename(path))[0]

    def packed_object(root, value):
        w = Writer()
        typetree.write(root, value, w)
        return bytes(w.b)

    objects, container = [], []
    for which in which_ones:
        spec = make_bundle.SHADERS[which]
        shader = packed_shader(which, programs[which], shader_pattern, 10000)       # (programs numbered apart from the OpenGL bundles')
        objects.append((spec["id"], 0, packed_object(shader_tree, shader)))
        container.append((spec["path"], spec["id"]))
    container.sort()
    index["m_Name"] = index["m_AssetBundleName"] = name
    index["m_PreloadTable"] = [{"m_FileID": 0, "m_PathID": path_id} for _, path_id in container]
    index["m_Container"] = [{"first": asset_path, "second": {"preloadIndex": n, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": path_id}}}
                            for n, (asset_path, path_id) in enumerate(container)]
    objects.append((1, 1, packed_object(index_tree, index)))
    asset = make_bundle.write_asset([shader_type, index_type], objects)
    Asset(asset)                                                                # reads back, or this stops here
    data = make_bundle.write_bundle("CAB-" + hashlib.md5(name.encode()).hexdigest(), asset)
    assert list(Bundle(data).files.values())[0] == asset
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "wb").write(data)
    return data


def check(path, which_ones):
    """The bundle read back: its shaders, by name, each with a Direct3D 11 vertex and pixel program that passes the checks
    d3d11.py makes of the game's own (the container's checksum, its chunks, the six bytes before it)."""
    found = {}
    for asset, shader in d3d11.shaders_in(path):
        assert shader["platforms"] == [d3d11.D3D11], shader["platforms"]
        programs = [d3d11.entry(e) for e in d3d11.entries(asset, shader, d3d11.D3D11)]
        for e in programs:
            code = e["code"]
            at = code.find(b"DXBC")
            assert at == 6 and code[:6] == d3d11.head(e) and d3d11.checksum(code[at:]) == code[at + 4:at + 20] and not e["rest"]
            assert d3d11.container(d3d11.chunks(code[at:])) == code[at:]
        found[shader["m_ParsedForm"]["m_Name"]] = [d3d11.PROGRAM[e["type"]] for e in programs]
    wanted = sorted(make_bundle.SHADERS[w]["name"] for w in which_ones)
    assert sorted(found) == wanted, (sorted(found), wanted)
    return found


def main(ksp):
    if os.name != "nt": raise SystemExit("this compiles with Windows' own compiler, so it runs on Windows only (see WITHOUT-UNITY.md for the other routes)")
    os.makedirs(OUT, exist_ok=True)
    compiler, device = Compiler(), Direct3D()
    print("compiler: %s, version %s; checked by Direct3D 11 on %s" % (compiler.path, compiler.version, device.what))
    for path, which_ones in BUNDLES:
        # (a repository of one mod has that mod's shaders and not the others': those bundles are passed over)
        if not all(os.path.exists(os.path.join(port.PACK, make_bundle.SHADERS[w]["code"])) for w in which_ones):
            print("left out: %s (its GLSL is not here)" % path)
            continue
        programs = {}
        for which in which_ones:
            programs[which] = compiled(which, compiler, device)
            print("  %-8s vertex %s (%d bytes), pixel %s (%d bytes)" % (which, "vs_" + programs[which]["vs"][1], len(d3d11.strip(programs[which]["vs"][0])),
                                                                      "ps_" + programs[which]["ps"][1], len(d3d11.strip(programs[which]["ps"][0]))))
        data = write_bundle(os.path.join(ROOT, path), which_ones, programs, ksp)
        found = check(os.path.join(ROOT, path), which_ones)
        print("wrote %s (%d bytes): %s" % (path, len(data), ", ".join("%s (%s)" % (n, " + ".join(p)) for n, p in sorted(found.items()))))


if __name__ == "__main__":
    if len(sys.argv) != 2: raise SystemExit(__doc__)
    main(sys.argv[1])
