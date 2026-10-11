#!/usr/bin/env python3
"""What a compiled Direct3D 11 shader program is inside a Unity 2019 bundle, read and checked (see WITHOUT-UNITY.md).

    python3 tools/unityshaders/d3d11.py survey <bundle> [...]     every shader's platforms, and checks of every D3D11 program
    python3 tools/unityshaders/d3d11.py show <bundle> <shader>    one shader's Direct3D 11 programs, field by field

Written to find out how a packer could add Direct3D 11 programs to this project's hand-packed bundles (as
tools/shaderpack does for OpenGL) without the Unity editor. Bundles to read: the game's own
(GameData/SquadExpansion/MakingHistory/AssetBundles/makinghistory_scene) and other mods' (TUFX, EVE, Waterfall,
Scatterer). The checksum and the container below are verified by "survey" against every program it reads.
"""
import collections
import json
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shaderpack"))
import typetree  # noqa: E402
from unityfile import Asset, Bundle, Reader, unpack  # noqa: E402

D3D11, OPENGL_CORE = 4, 15                       # Unity's numbers for the two graphics APIs (ShaderCompilerPlatform)
PROGRAM = {6: "GLCore32", 7: "GLCore41", 8: "GLCore43", 15: "DX11VertexSM40", 16: "DX11VertexSM50", 17: "DX11PixelSM40", 18: "DX11PixelSM50",
           19: "DX11GeometrySM40", 20: "DX11GeometrySM50", 21: "DX11HullSM50", 22: "DX11DomainSM50"}
BINDING = {0: "texture", 1: "constant buffer", 2: "buffer", 4: "sampler"}


# ---------------------------------------------------------------- DXBC: the container Direct3D's bytecode comes in

_S = [7, 12, 17, 22] * 4 + [5, 9, 14, 20] * 4 + [4, 11, 16, 23] * 4 + [6, 10, 15, 21] * 4
_K = [int(abs(__import__("math").sin(i + 1)) * 2 ** 32) & 0xFFFFFFFF for i in range(64)]


def _md5_block(state, block):
    a, b, c, d = state
    m = struct.unpack("<16I", block)
    for i in range(64):
        if i < 16: f, g = (b & c) | (~b & d), i
        elif i < 32: f, g = (d & b) | (~d & c), (5 * i + 1) % 16
        elif i < 48: f, g = b ^ c ^ d, (3 * i + 5) % 16
        else: f, g = c ^ (b | ~d), (7 * i) % 16
        f = (f + a + _K[i] + m[g]) & 0xFFFFFFFF
        a, d, c = d, c, b
        b = (b + (((f << _S[i]) | (f >> (32 - _S[i]))) & 0xFFFFFFFF)) & 0xFFFFFFFF
    return [(x + y) & 0xFFFFFFFF for x, y in zip(state, (a, b, c, d))]


def checksum(container):
    """The sixteen bytes Direct3D checks at offset 4 of a container: MD5's rounds over everything after the first 20
    bytes, with Microsoft's own ending (the length in bits first in the last block, and again, shifted, at its end)."""
    data = bytes(container[20:])
    state = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476]
    whole = len(data) // 64 * 64
    for at in range(0, whole, 64):
        state = _md5_block(state, data[at:at + 64])
    bits = (len(data) * 8) & 0xFFFFFFFF
    last = bytearray(data[whole:]) + b"\x80"
    if len(last) > 56:                           # (no room for the length: a block of its own)
        state = _md5_block(state, bytes(last + b"\0" * (64 - len(last))))
        last = bytearray(64)
    else:
        last = bytearray(4) + last + b"\0" * (60 - len(last))
    last[0:4] = struct.pack("<I", bits)
    last[60:64] = struct.pack("<I", (bits >> 2) | 1)
    return struct.pack("<4I", *_md5_block(state, bytes(last)))


def chunks(container):
    """[(fourcc, bytes)] of a DXBC container."""
    magic, _, one, total, count = struct.unpack_from("<4s16sIII", container, 0)
    if magic != b"DXBC" or one != 1 or total != len(container): raise ValueError("not a DXBC container")
    out = []
    for at in struct.unpack_from("<%dI" % count, container, 32):
        fourcc, size = struct.unpack_from("<4sI", container, at)
        out.append((fourcc.decode("latin1"), bytes(container[at + 8:at + 8 + size])))
    return out


def container(parts):
    """A DXBC container of these chunks, signed with its checksum."""
    head = 32 + 4 * len(parts)
    offsets, body = [], bytearray()
    for fourcc, data in parts:
        offsets.append(head + len(body))
        body += fourcc.encode("latin1") + struct.pack("<I", len(data)) + data
    out = bytearray(b"DXBC" + bytes(16) + struct.pack("<III", 1, head + len(body), len(parts)) + struct.pack("<%dI" % len(parts), *offsets)) + body
    out[4:20] = checksum(out)
    return bytes(out)


def strip(dxbc):
    """What Unity keeps of a compiled program: its signatures, the features it needs (SFI0) and the code; not the
    reflection (RDEF) nor the statistics (STAT). Every one of the 8,381 programs surveyed is kept so."""
    keep = ("ISGN", "OSGN", "OSG5", "PCSG", "SFI0", "SHDR", "SHEX")
    return container([(fourcc, data) for fourcc, data in chunks(dxbc) if fourcc in keep])


def signature(data):
    """The elements of an ISGN/OSGN chunk: (semantic, index, register, mask)."""
    n = struct.unpack_from("<I", data, 0)[0]
    out = []
    for i in range(n):
        name_at, index, _, _, register, mask = struct.unpack_from("<IIIIIB", data, 8 + 24 * i)
        out.append((data[name_at:data.index(b"\0", name_at)].decode(), index, register, mask))
    return out


# ---------------------------------------------------------------- Unity's side: the Shader object and its blob

def shaders_in(path):
    """(asset, shader as read through its type tree) for every Shader in a bundle."""
    bundle = Bundle(open(path, "rb").read())
    for name, data in bundle.files.items():
        if bundle.node_flags[name] != 4: continue
        asset = Asset(data)
        for obj in asset.objects:
            if obj["class"] != 48 or not asset.has_trees: continue
            kind = asset.types[obj["type"]]
            root = typetree.parse_tree(kind["tree"], kind["tree_counts"], asset.version)
            yield asset, typetree.read(root, Reader(asset.bytes_of(obj)))


def entries(asset, shader, platform):
    """The programs of one platform, as bytes. Unity 2019.3 and after keep each platform as one or more LZ4 chunks and
    give each entry its chunk; 2019.2 kept one block per platform and a table of (offset, length) only."""
    p = shader["platforms"].index(platform)
    offsets, packed, sizes = shader["offsets"][p], shader["compressedLengths"][p], shader["decompressedLengths"][p]
    if isinstance(offsets, int): offsets, packed, sizes = [offsets], [packed], [sizes]
    blocks = [unpack(shader["compressedBlob"][o:o + c], 2, d) for o, c, d in zip(offsets, packed, sizes)]
    major, minor = (int(x) for x in asset.engine.split(".")[:2])
    three = (major, minor) >= (2019, 3)
    r = Reader(blocks[0])
    table = [(r.i32(), r.i32(), r.i32() if three else 0) for _ in range(r.i32())]
    return [blocks[seg][o:o + n] for o, n, seg in table]


def entry(data):
    """One program: the same layout for every graphics API (tools/shaderpack/glprogram.py writes the OpenGL kind);
    for Direct3D 11 the code is six bytes and a DXBC container."""
    r = Reader(data)
    e = {"version": r.i32(), "type": r.i32(), "stats": [r.i32() for _ in range(4)]}
    e["keywords"] = [r.string() for _ in range(r.i32())]
    e["local_keywords"] = [r.string() for _ in range(r.i32())]
    e["code"] = r.blob()
    e["source_map"] = r.i32()
    e["channels"] = [(r.u32(), r.u32()) for _ in range(r.i32())]
    e["groups"] = []
    for _ in range(r.i32()):
        g = {"name": r.string(), "size": r.i32(), "params": []}
        for _ in range(r.i32()):
            g["params"].append({"name": r.string(), "type": r.i32(), "rows": r.i32(), "cols": r.i32(), "matrix": r.i32(), "array": r.i32(), "index": r.i32()})
        if r.i32(): raise ValueError("struct parameters are not read here")
        e["groups"].append(g)
    e["bindings"] = []
    for _ in range(r.i32()):
        b = {"name": r.string(), "type": r.i32(), "index": r.i32(), "extra": r.i32()}
        if b["type"] == 0: b["texture"] = r.u32()        # (dimension << 1) | multisampled
        e["bindings"].append(b)
    e["rest"] = data[r.p:]
    return e


def head(e, primitive=0):
    """The six bytes Unity puts before a Direct3D 11 program's DXBC, from its bindings: 1, how many resource views
    (textures and buffers), constant buffers and samplers it binds, 0 (presumably random-write views: none were seen),
    and for a geometry shader its input primitive (1 points and 3 triangles were seen), else 0."""
    kinds = collections.Counter(b["type"] for b in e["bindings"])
    samplers = len({b["extra"] for b in e["bindings"] if b["type"] == 0 and b["extra"] >= 0}) + kinds.get(4, 0)
    return bytes([1, kinds.get(0, 0) + kinds.get(2, 0), kinds.get(1, 0), samplers, 0, primitive])


# ---------------------------------------------------------------- the commands

def survey(paths):
    seen = collections.Counter()
    for path in paths:
        for asset, shader in shaders_in(path):
            names = ["D3D11" if p == D3D11 else "OpenGLCore" if p == OPENGL_CORE else str(p) for p in shader["platforms"]]
            seen["shaders with " + "+".join(names) + " (" + asset.engine + ")"] += 1
            if D3D11 not in shader["platforms"]: continue
            for data in entries(asset, shader, D3D11):
                if not data: continue
                e = entry(data)
                code = e["code"]
                at = code.find(b"DXBC")
                dxbc = code[at:]
                seen["D3D11 programs"] += 1
                seen["... of them %s" % PROGRAM.get(e["type"], e["type"])] += 1
                seen["... with %d bytes before DXBC" % at] += 1
                seen["... chunks " + "+".join(c for c, _ in chunks(dxbc))] += 1
                seen["... nothing after the bindings" if not e["rest"] else "... something after the bindings"] += 1
                seen["... checksum right" if checksum(dxbc) == dxbc[4:20] else "... CHECKSUM WRONG"] += 1
                seen["... rebuilt byte for byte from its chunks" if container(chunks(dxbc)) == dxbc else "... NOT rebuilt"] += 1
                primitive = code[5] if e["type"] in (19, 20) else 0
                seen["... six bytes as their bindings predict" if code[:at] == head(e, primitive) else "... six bytes NOT as predicted"] += 1
    for k, n in seen.items():
        print("%7d  %s" % (n, k))


def show(path, name):
    for asset, shader in shaders_in(path):
        form = shader["m_ParsedForm"]
        if form["m_Name"] != name: continue
        print("platforms", shader["platforms"], "offsets", shader["offsets"], "compressed", shader["compressedLengths"], "decompressed", shader["decompressedLengths"])
        for s, sub in enumerate(form["m_SubShaders"]):
            for p, one in enumerate(sub["m_Passes"]):
                print("== subshader %d pass %d: program mask %d, names %s" % (s, p, one["m_ProgramMask"], one["m_NameIndices"]))
                for stage in ("progVertex", "progFragment", "progGeometry"):
                    for sp in one[stage]["m_SubPrograms"]:
                        if sp["m_GpuProgramType"] in PROGRAM and sp["m_GpuProgramType"] >= 15:
                            print("-- %s %s:" % (stage, PROGRAM[sp["m_GpuProgramType"]]), json.dumps(sp))
        if D3D11 in shader["platforms"]:
            for n, data in enumerate(entries(asset, shader, D3D11)):
                if not data: continue
                e = entry(data)
                code = e.pop("code")
                at = code.find(b"DXBC")
                e["head"] = code[:at].hex(" ")
                e["dxbc"] = [(c, len(d)) for c, d in chunks(code[at:])]
                for c, d in chunks(code[at:]):
                    if c in ("ISGN", "OSGN"): e[c] = signature(d)
                e["rest"] = len(e["rest"])
                print("-- D3D11 entry %d:" % n, json.dumps(e))
        return
    raise SystemExit("no shader called %r in %s" % (name, path))


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "survey": survey(sys.argv[2:])
    elif len(sys.argv) == 4 and sys.argv[1] == "show": show(sys.argv[2], sys.argv[3])
    else: raise SystemExit(__doc__)
