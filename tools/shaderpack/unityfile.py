"""Just enough of Unity's file formats (2019.4) to read the game's own asset files and bundles, and to write a small bundle.

Nothing here comes from Unity's tools: the layouts are the publicly documented ones that asset viewers use.
"""
import io
import lzma
import struct


class Reader:
    def __init__(self, data, big=False, at=0):
        self.d, self.p, self.big = data, at, big

    def take(self, n):
        b = self.d[self.p:self.p + n]
        if len(b) != n:
            raise EOFError("wanted %d bytes at %d of %d" % (n, self.p, len(self.d)))
        self.p += n
        return b

    def num(self, fmt):
        size = struct.calcsize(fmt)
        return struct.unpack((">" if self.big else "<") + fmt, self.take(size))[0]

    def u8(self): return self.num("B")
    def i8(self): return self.num("b")
    def u16(self): return self.num("H")
    def i16(self): return self.num("h")
    def u32(self): return self.num("I")
    def i32(self): return self.num("i")
    def i64(self): return self.num("q")
    def u64(self): return self.num("Q")
    def f32(self): return self.num("f")
    def boolean(self): return self.u8() != 0

    def cstr(self):
        end = self.d.index(b"\0", self.p)
        s = self.d[self.p:end].decode("utf-8", "replace")
        self.p = end + 1
        return s

    def align(self, n=4):
        self.p = (self.p + n - 1) // n * n

    def string(self):                    # length, bytes, padding to four
        n = self.i32()
        if n < 0 or n > len(self.d) - self.p:
            raise ValueError("bad string length %d at %d" % (n, self.p - 4))
        s = self.take(n).decode("utf-8", "replace")
        self.align()
        return s

    def blob(self):
        n = self.i32()
        if n < 0 or n > len(self.d) - self.p:
            raise ValueError("bad byte array length %d at %d" % (n, self.p - 4))
        b = self.take(n)
        self.align()
        return b


class Writer:
    def __init__(self, big=False):
        self.b, self.big = bytearray(), big

    def raw(self, data): self.b += data
    def num(self, fmt, v): self.b += struct.pack((">" if self.big else "<") + fmt, v)
    def u8(self, v): self.num("B", v)
    def i8(self, v): self.num("b", v)
    def u16(self, v): self.num("H", v)
    def i16(self, v): self.num("h", v)
    def u32(self, v): self.num("I", v)
    def i32(self, v): self.num("i", v)
    def i64(self, v): self.num("q", v)
    def u64(self, v): self.num("Q", v)
    def f32(self, v): self.num("f", v)
    def boolean(self, v): self.u8(1 if v else 0)
    def cstr(self, s): self.b += s.encode("utf-8") + b"\0"

    def align(self, n=4):
        while len(self.b) % n:
            self.b.append(0)

    def string(self, s):
        data = s.encode("utf-8")
        self.i32(len(data))
        self.b += data
        self.align()

    def blob(self, data):
        self.i32(len(data))
        self.b += data
        self.align()


def lz4_block(src, size):
    """Unpack one LZ4 block."""
    out = bytearray()
    p, n = 0, len(src)
    while p < n:
        token = src[p]; p += 1
        lit = token >> 4
        if lit == 15:
            while True:
                b = src[p]; p += 1
                lit += b
                if b != 255: break
        out += src[p:p + lit]; p += lit
        if p >= n: break
        offset = src[p] | (src[p + 1] << 8); p += 2
        length = token & 15
        if length == 15:
            while True:
                b = src[p]; p += 1
                length += b
                if b != 255: break
        length += 4
        start = len(out) - offset
        for k in range(length):
            out.append(out[start + k])
    assert len(out) == size, (len(out), size)
    return bytes(out)


def lz4_store(data):
    """Pack bytes as an LZ4 block without compressing them: one run of literals."""
    out = bytearray()
    n = len(data)
    out.append((15 if n >= 15 else n) << 4)
    if n >= 15:
        rest = n - 15
        while rest >= 255:
            out.append(255); rest -= 255
        out.append(rest)
    out += data
    return bytes(out)


def unpack(data, kind, size):
    if kind == 0: return data
    if kind == 1:
        props, dict_size = data[0], struct.unpack("<I", data[1:5])[0]
        lc, lp, pb = props % 9, (props // 9) % 5, props // 45
        d = lzma.LZMADecompressor(lzma.FORMAT_RAW, filters=[{"id": lzma.FILTER_LZMA1, "dict_size": dict_size, "lc": lc, "lp": lp, "pb": pb}])
        return d.decompress(data[5:], size)
    if kind in (2, 3): return lz4_block(data, size)
    raise ValueError("compression %d" % kind)


class Bundle:
    """A UnityFS bundle: a header, a list of blocks, and named files inside."""

    def __init__(self, data):
        r = Reader(data, big=True)
        self.signature = r.cstr()
        self.format = r.u32()
        self.player = r.cstr()
        self.engine = r.cstr()
        self.size = r.i64()
        csize, usize, self.flags = r.u32(), r.u32(), r.u32()
        if self.format >= 7: r.align(16)
        if self.flags & 0x80:
            info = data[len(data) - csize:]
        else:
            info = r.take(csize)
        info = unpack(info, self.flags & 0x3F, usize)
        ir = Reader(info, big=True)
        self.hash = ir.take(16)
        blocks = [(ir.u32(), ir.u32(), ir.u16()) for _ in range(ir.i32())]
        self.block_flags = [b[2] for b in blocks]
        nodes = [(ir.i64(), ir.i64(), ir.u32(), ir.cstr()) for _ in range(ir.i32())]
        body = bytearray()
        for usize, csize, flags in blocks:
            body += unpack(r.take(csize), flags & 0x3F, usize)
        self.files = {}
        self.node_flags = {}
        for offset, size, flags, path in nodes:
            self.files[path] = bytes(body[offset:offset + size])
            self.node_flags[path] = flags


def write_bundle(files, player="5.x.x", engine="2019.4.18f1", node_flags=4):
    """files: [(name, bytes)]. Stored without compression."""
    body = b"".join(data for _, data in files)
    info = Writer(big=True)
    info.raw(b"\0" * 16)
    info.i32(1)
    info.u32(len(body)); info.u32(len(body)); info.u16(0x40)
    info.i32(len(files))
    offset = 0
    for name, data in files:
        info.i64(offset); info.i64(len(data)); info.u32(node_flags); info.cstr(name)
        offset += len(data)
    head = Writer(big=True)
    head.cstr("UnityFS"); head.u32(6); head.cstr(player); head.cstr(engine)
    size_at = len(head.b)
    head.i64(0)
    head.u32(len(info.b)); head.u32(len(info.b)); head.u32(0x40)
    out = bytearray(head.b + info.b + body)
    out[size_at:size_at + 8] = struct.pack(">q", len(out))
    return bytes(out)


class Asset:
    """A serialized file: a table of types, a table of objects, and the objects' bytes."""

    def __init__(self, data):
        self.data = data
        r = Reader(data, big=True)
        self.meta_size, self.file_size, self.version, self.data_offset = r.u32(), r.u32(), r.u32(), r.u32()
        self.endian = r.u8(); r.take(3)
        if self.version >= 22:
            self.meta_size, self.file_size, self.data_offset = r.u32(), r.i64(), r.i64(); r.i64()
        r.big = self.endian != 0
        self.meta_start = r.p
        self.engine = r.cstr()
        self.platform = r.i32()
        self.has_trees = r.boolean()
        self.types = []
        for _ in range(r.i32()):
            self.types.append(self._type(r, False))
        self.objects = []
        for _ in range(r.i32()):
            r.align()
            path_id = r.i64()
            start = r.i64() if self.version >= 22 else r.u32()
            size, type_index = r.u32(), r.i32()
            self.objects.append({"id": path_id, "start": start + self.data_offset, "size": size, "type": type_index, "class": self.types[type_index]["class"]})
        self.scripts = []
        for _ in range(r.i32()):
            index = r.i32(); r.align(); self.scripts.append((index, r.i64()))
        self.externals = []
        for _ in range(r.i32()):
            self.externals.append({"empty": r.cstr(), "guid": r.take(16), "type": r.i32(), "path": r.cstr()})
        self.ref_types = []
        if self.version >= 20:
            for _ in range(r.i32()):
                self.ref_types.append(self._type(r, True))
        self.user = r.cstr()
        self.meta_end = r.p

    def _type(self, r, is_ref):
        start = r.p
        t = {"class": r.i32()}
        t["stripped"] = r.boolean() if self.version >= 16 else False
        t["script_index"] = r.i16() if self.version >= 17 else -1
        if self.version >= 13:
            if (is_ref and t["script_index"] >= 0) or (self.version < 16 and t["class"] < 0) or (self.version >= 16 and t["class"] == 114):
                t["script_id"] = r.take(16)
            t["hash"] = r.take(16)
        if self.has_trees:
            nodes, strings = r.i32(), r.i32()
            node_size = 32 if self.version >= 19 else 24
            t["tree"] = r.take(nodes * node_size + strings)
            t["tree_counts"] = (nodes, strings)
            if self.version >= 21:
                if is_ref:
                    t["ref"] = (r.cstr(), r.cstr(), r.cstr())
                else:
                    t["deps"] = [r.i32() for _ in range(r.i32())]
        t["raw"] = r.d[start:r.p]
        return t

    def bytes_of(self, obj):
        return self.data[obj["start"]:obj["start"] + obj["size"]]


def write_asset(types, objects, engine="2019.4.18f1", platform=2, version=21, has_trees=False, externals=()):
    """types: raw type-table entries (bytes, as read); objects: [(path_id, type_index, bytes)]."""
    meta = Writer()
    meta.cstr(engine); meta.i32(platform); meta.boolean(has_trees)
    meta.i32(len(types))
    for raw in types: meta.raw(raw)
    meta.i32(len(objects))
    body = Writer()
    for path_id, type_index, data in objects:
        while len(body.b) % 8: body.b.append(0)
        meta.align()
        meta.i64(path_id); meta.u32(len(body.b)); meta.u32(len(data)); meta.i32(type_index)
        body.raw(data)
    meta.i32(0)                       # scripts
    meta.i32(len(externals))
    for e in externals:
        meta.cstr(e["empty"]); meta.raw(e["guid"]); meta.i32(e["type"]); meta.cstr(e["path"])
    meta.i32(0)                       # reference types
    meta.cstr("")
    head_size = 20
    data_offset = head_size + len(meta.b)
    data_offset = max(4096, (data_offset + 15) // 16 * 16) if False else (data_offset + 15) // 16 * 16
    out = Writer(big=True)
    out.u32(len(meta.b)); out.u32(data_offset + len(body.b)); out.u32(version); out.u32(data_offset)
    out.u8(0); out.raw(b"\0\0\0")
    out.raw(meta.b)
    while len(out.b) < data_offset: out.b.append(0)
    out.raw(body.b)
    return bytes(out.b)
