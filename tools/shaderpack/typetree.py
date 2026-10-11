"""Read and write any Unity object whose file carries a description of its layout (a "type tree")."""
import struct
from unityfile import Reader, Writer

# Unity's built-in names, in the order of its own table; a name's position is the sum of the lengths before it.
COMMON = ["AABB", "AnimationClip", "AnimationCurve", "AnimationState", "Array", "Base", "BitField", "bitset", "bool", "char", "ColorRGBA", "Component", "data", "deque", "double",
          "dynamic_array", "FastPropertyName", "first", "float", "Font", "GameObject", "Generic Mono", "GradientNEW", "GUID", "GUIStyle", "int", "list", "long long", "map", "Matrix4x4f",
          "MdFour", "MonoBehaviour", "MonoScript", "m_ByteSize", "m_Curve", "m_EditorClassIdentifier", "m_EditorHideFlags", "m_Enabled", "m_ExtensionPtr", "m_GameObject", "m_Index",
          "m_IsArray", "m_IsStatic", "m_MetaFlag", "m_Name", "m_ObjectHideFlags", "m_PrefabInternal", "m_PrefabParentObject", "m_Script", "m_StaticEditorFlags", "m_Type", "m_Version",
          "Object", "pair", "PPtr<Component>", "PPtr<GameObject>", "PPtr<Material>", "PPtr<MonoBehaviour>", "PPtr<MonoScript>", "PPtr<Object>", "PPtr<Prefab>", "PPtr<Sprite>",
          "PPtr<TextAsset>", "PPtr<Texture>", "PPtr<Texture2D>", "PPtr<Transform>", "Prefab", "Quaternionf", "Rectf", "RectInt", "RectOffset", "second", "set", "short", "size", "SInt16",
          "SInt32", "SInt64", "SInt8", "staticvector", "string", "TextAsset", "TextMesh", "Texture", "Texture2D", "Transform", "TypelessData", "UInt16", "UInt32", "UInt64", "UInt8",
          "unsigned int", "unsigned long long", "unsigned short", "vector", "Vector2f", "Vector3f", "Vector4f", "m_ScriptingClassIdentifier", "Gradient", "Type*", "int2_storage",
          "int3_storage", "BoundsInt", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "FileSize", "Hash128"]
COMMON_AT = {}
_at = 0
for _s in COMMON:
    COMMON_AT[_at] = _s
    _at += len(_s) + 1

PRIMITIVE = {"SInt8": "b", "UInt8": "B", "char": "B", "short": "h", "SInt16": "h", "unsigned short": "H", "UInt16": "H", "int": "i", "SInt32": "i", "unsigned int": "I", "UInt32": "I",
             "Type*": "I", "long long": "q", "SInt64": "q", "unsigned long long": "Q", "UInt64": "Q", "FileSize": "Q", "float": "f", "double": "d", "bool": "?"}


class Node:
    __slots__ = ("level", "flags", "type", "name", "size", "meta", "kids")


def parse_tree(tree, counts, version=21):
    """The flat list of nodes of a type tree, made into a nested one."""
    nodes_n, strings_n = counts
    node_size = 32 if version >= 19 else 24
    strings = tree[nodes_n * node_size:]

    def text(offset):
        if offset & 0x80000000:
            return COMMON_AT.get(offset & 0x7FFFFFFF, "?%d" % (offset & 0x7FFFFFFF))
        end = strings.index(b"\0", offset)
        return strings[offset:end].decode()

    flat = []
    for n in range(nodes_n):
        ver, level, flags, type_at, name_at, size, index, meta = struct.unpack_from("<HBBIIiii", tree, n * node_size)
        node = Node()
        node.level, node.flags, node.type, node.name, node.size, node.meta, node.kids = level, flags, text(type_at), text(name_at), size, meta, []
        flat.append(node)
    stack = []
    for node in flat:
        while stack and stack[-1].level >= node.level:
            stack.pop()
        if stack:
            stack[-1].kids.append(node)
        stack.append(node)
    return flat[0]


def read(node, r):
    """One value described by 'node', as Python data: dicts for structures, lists for arrays."""
    t = node.type
    aligned = bool(node.meta & 0x4000)
    if t in PRIMITIVE:
        v = r.num(PRIMITIVE[t])
    elif t == "string":
        n = r.i32()
        v = r.take(n).decode("utf-8", "surrogateescape")
        aligned = True
    elif t == "TypelessData":
        v = r.take(r.i32())
    elif node.kids and node.kids[0].type == "Array":
        array = node.kids[0]
        if array.meta & 0x4000: aligned = True
        item = array.kids[1]
        n = r.i32()
        if item.type in ("UInt8", "char") and not item.kids:
            v = r.take(n)
        else:
            v = [read(item, r) for _ in range(n)]
    else:
        v = {}
        for kid in node.kids:
            v[kid.name] = read(kid, r)
    if aligned: r.align()
    return v


def write(node, v, w):
    t = node.type
    aligned = bool(node.meta & 0x4000)
    if t in PRIMITIVE:
        w.num(PRIMITIVE[t], v)
    elif t == "string":
        data = v.encode("utf-8", "surrogateescape")
        w.i32(len(data)); w.raw(data)
        aligned = True
    elif t == "TypelessData":
        w.i32(len(v)); w.raw(v)
    elif node.kids and node.kids[0].type == "Array":
        array = node.kids[0]
        if array.meta & 0x4000: aligned = True
        item = array.kids[1]
        w.i32(len(v))
        if item.type in ("UInt8", "char") and not item.kids:
            w.raw(bytes(v))
        else:
            for x in v: write(item, x, w)
    else:
        for kid in node.kids:
            write(kid, v[kid.name], w)
    if aligned: w.align()


def describe(node, depth=0, out=None, limit=4):
    out = [] if out is None else out
    out.append("  " * depth + "%s %s%s" % (node.type, node.name, " (aligned)" if node.meta & 0x4000 else ""))
    if depth < limit:
        for kid in node.kids: describe(kid, depth + 1, out, limit)
    return out
