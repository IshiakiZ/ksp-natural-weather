"""The part of a compiled Unity shader that holds one OpenGL program: its GLSL text and the list of what it reads."""
from unityfile import Reader, Writer

VERSION = 201806140          # the format of these entries in Unity 2019
GLCORE = 6                   # "OpenGL core" program type


def read_entry(data):
    r = Reader(data)
    e = {"version": r.i32(), "type": r.i32(), "stats": [r.i32() for _ in range(4)]}
    e["keywords"] = [r.string() for _ in range(r.i32())]
    e["local_keywords"] = [r.string() for _ in range(r.i32())]
    e["code"] = r.blob()
    e["source_map"] = r.i32()
    e["channels"] = [(r.u32(), r.u32()) for _ in range(r.i32())]
    e["groups"] = []
    for _ in range(r.i32()):
        g = {"name": r.string(), "size": r.i32(), "params": [], "structs": []}
        for _ in range(r.i32()):
            g["params"].append({"name": r.string(), "type": r.i32(), "rows": r.i32(), "cols": r.i32(), "matrix": r.i32(), "array": r.i32(), "index": r.i32()})
        n = r.i32()
        assert n == 0, "struct parameters are not handled"
        e["groups"].append(g)
    e["bindings"] = []
    for _ in range(r.i32()):
        b = {"name": r.string(), "type": r.i32(), "index": r.i32(), "extra": r.i32()}
        if b["type"] == 0: b["texture"] = r.u32()        # bit 0 multisampled, the rest the dimension
        e["bindings"].append(b)
    e["rest"] = data[r.p:]
    return e


def write_entry(e):
    w = Writer()
    w.i32(e.get("version", VERSION)); w.i32(e.get("type", GLCORE))
    for s in e.get("stats", [0, 0, 0, 0]): w.i32(s)
    for key in ("keywords", "local_keywords"):
        w.i32(len(e.get(key, [])))
        for k in e.get(key, []): w.string(k)
    w.blob(e.get("code", b""))
    w.i32(e.get("source_map", 0))
    w.i32(len(e.get("channels", [])))
    for a, b in e.get("channels", []): w.u32(a); w.u32(b)
    groups = e.get("groups", [{"name": "", "size": 0, "params": []}])
    w.i32(len(groups))
    for g in groups:
        w.string(g["name"]); w.i32(g["size"]); w.i32(len(g["params"]))
        for p in g["params"]:
            w.string(p["name"]); w.i32(p["type"]); w.i32(p["rows"]); w.i32(p["cols"]); w.i32(p["matrix"]); w.i32(p["array"]); w.i32(p["index"])
        w.i32(0)
    w.i32(len(e.get("bindings", [])))
    for b in e.get("bindings", []):
        w.string(b["name"]); w.i32(b["type"]); w.i32(b["index"]); w.i32(b["extra"])
        if b["type"] == 0: w.u32(b["texture"])
    w.raw(e.get("rest", b""))
    return bytes(w.b)


def read_blob(data):
    r = Reader(data)
    return [data[o:o + n] for o, n, seg in [(r.i32(), r.i32(), r.i32()) for _ in range(r.i32())]]


def write_blob(entries):
    w = Writer()
    w.i32(len(entries))
    at = 4 + 12 * len(entries)
    for e in entries:
        w.i32(at); w.i32(len(e)); w.i32(0)
        at += len(e)
    for e in entries: w.raw(e)
    return bytes(w.b)
